using System.Diagnostics;
using System.Text.Json;
using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

// An ordinary public-API observer. It supplies new requests and checks distances;
// it does not generate poses or supply a substitute winding/constraint engine.
internal static class SpatialAcceptance
{
    public static void Run(string sourcePath,Rational? minimum=null,Rational? maximum=null)
    {
        var sdk=GearInvestSdk.CreateDefault();var bytes=File.ReadAllBytes(sourcePath);
        var timer=Stopwatch.StartNew();var artifact=sdk.ReadWindingConnectionArtifact(bytes);
        var admissionMs=timer.Elapsed.TotalMilliseconds;
        var source=artifact.Source.WindingSource as SpatialWindingDefinition??throw new ArgumentException("Spatial source required.");
        using var session=sdk.PrepareMechanicalRuntime(bytes,"spatial-domain-consumer",new Rational(3,10));
        var material=Enumerable.Range(0,source.LinkCount+1).Select(source.PinId).ToArray();
        var states=new List<object>();double maximumPitchResidual=0,maximumBend=0;var contacts=new HashSet<(int,int)>();
        // Deliberately crosses more than two turns in one normal request, stops,
        // reverses and rewinds. Limits are finite and never reduced modulo one.
        var lower=minimum??new Rational(-23,20);var upper=maximum??new Rational(23,20);
        if(upper-lower<=2)throw new ArgumentException("This multi-turn observer requires an explicit interval wider than two turns.");
        var inputs=new[]{lower,upper,upper,lower,Rational.Zero};
        for(var i=0;i<inputs.Length;i++)
        {
            var before=session.Current;
            var request=new RuntimeRequest("domain-"+i,before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,
                new[]{new RuntimeSegment(new(inputs[i],new[]{new KeyValuePair<string,Rational>(artifact.Source.PlanetPortId,new Rational(3,10))}))});
            timer.Restart();var advanced=session.Advance(request);var elapsed=timer.Elapsed.TotalMilliseconds;
            if(!advanced.IsAccepted)throw new InvalidOperationException("Normal whole-domain path: "+advanced.Status);
            var pose=session.Current.Mechanical.Frame.SpatialWinding!;
            if(!material.SequenceEqual(Enumerable.Range(0,pose.Pins.Count).Select(source.PinId)))throw new InvalidOperationException("Material identity");
            Check(pose);
            states.Add(new{q=inputs[i].ToString(),status=advanced.Status,revision=session.Current.Revision.ToString(),elapsedMs=elapsed,
                pose.OutputTurns,pose.DriverContact,pose.OutputContact,pose.PitchResidualMm,pose.MaximumBendDegrees});
        }
        // Independent direct distance/bend checks and work observations on53
        // point queries. This diagnostic grid is not a continuous proof.
        long maximumWork=0;var refused=new Dictionary<string,int>();
        for(var i=0;i<53;i++)
        {
            var exact=lower+(upper-lower)*new Rational(i,52);var q=(double)exact.Numerator/(double)exact.Denominator;
            var query=source.HasSelectedDriveBoundary?SpatialWindingSolver.EvaluateRefined(source.Geometry,q):SpatialWindingSolver.Evaluate(source.Geometry,q);maximumWork=Math.Max(maximumWork,query.NumericWork);
            if(!query.IsAccepted){refused[query.Status]=refused.GetValueOrDefault(query.Status)+1;continue;}
            Check(query.Pose!);contacts.Add((query.Pose!.DriverContact,query.Pose.OutputContact));
        }
        if(refused.Count!=0)throw new InvalidOperationException("Representative grid refused: "+JsonSerializer.Serialize(refused));
        var checkpoint=session.Checkpoint();using var restored=sdk.RestoreMechanicalRuntime(checkpoint.Bytes);
        if(restored.Current.StateId!=session.Current.StateId)throw new InvalidOperationException("Fresh session restore identity");
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",profile=SpatialWindingGeometry.Profile,sourceArtifactId=artifact.ArtifactId,
            sourceBytes=bytes.Length,admissionMs,links=source.LinkCount,source.PitchMm,observedMinimum=lower.ToString(),observedMaximum=upper.ToString(),wholePathRequests=states,gridQueries=53,refused,
            distinctContactCounts=contacts.Count,maximumWork,maximumPitchResidualMm=maximumPitchResidual,maximumBendDegrees=maximumBend,
            checkpointBytes=checkpoint.Bytes.Length,checkpointStateId=session.Current.StateId,
            limits=new[]{"ordinary runtime validates sampled interior paths, not continuous proof","53 point queries are a numerical observation, not a global root uniqueness proof","latency samples include public runtime work; no p95 with5 requests"}}));
        void Check(SpatialWindingPose pose)
        {
            if(pose.Pins.Count!=source.LinkCount+1)throw new InvalidOperationException("Pin count");
            for(var i=1;i<pose.Pins.Count;i++)
            {
                var p=pose.Pins[i].PositionMm;var a=pose.Pins[i-1].PositionMm;
                var dx=p.X-a.X;var dy=p.Y-a.Y;var dz=p.Z-a.Z;var length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                var residual=Math.Abs(length-source.PitchMm);maximumPitchResidual=Math.Max(maximumPitchResidual,residual);
                if(residual>1e-8)throw new InvalidOperationException("Independent Euclidean pin pitch");
                if(i<2)continue;
                var b=pose.Pins[i-2].PositionMm;var ux=a.X-b.X;var uy=a.Y-b.Y;var uz=a.Z-b.Z;
                var cosine=(ux*dx+uy*dy+uz*dz)/(Math.Sqrt(ux*ux+uy*uy+uz*uz)*length);
                var bend=Math.Acos(Math.Clamp(cosine,-1,1))*180/Math.PI;maximumBend=Math.Max(maximumBend,bend);
                if(bend>source.Geometry.MaxBendDegrees)throw new InvalidOperationException("Independent joint bend");
            }
        }
    }
}
