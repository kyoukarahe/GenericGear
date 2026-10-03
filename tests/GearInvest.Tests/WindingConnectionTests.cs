using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.WindingExample;
using GearInvest.Serialization.Json;
using System.Numerics;
using Xunit;

namespace GearInvest.Tests;

public sealed class WindingConnectionTests
{
    [Theory]
    [InlineData(false, 21, 1)]
    [InlineData(true, 21, 1)]
    [InlineData(false, 20, 2)]
    public void Authored_material_chain_drives_actual_two_input_carrier_and_suffix(bool circular, int links, int scale)
    {
        var s = Example.Create(scale, links, circular); var a = WindingDifferentialEngine.Prepare(s);
        Assert.True(a.IsValid, string.Join(",", a.Diagnostics));
        var first = WindingDifferentialEngine.Evaluate(a, new(0, new Rational(3,10))).Frame!;
        Assert.NotNull(first); Assert.Equal(links + 1, first.Winding.Pins.Count);
        for (var i = 0; i <= 240; i++)
        {
            var q = new Rational(i - 120, 960); var result = WindingDifferentialEngine.Evaluate(a, new(q, new Rational(3,10)));
            Assert.True(result.IsAccepted, result.Status); var frame = result.Frame!;
            Assert.Equal("NumericResidualOnly", frame.Coordinates["suffix/out"].Kind);
            Assert.Null(frame.Coordinates["suffix/out"].SolutionErrorBoundTurns);
            Assert.InRange(frame.Winding.PitchResidualMm, 0, 1e-9);
            Assert.InRange(frame.Winding.TotalLengthResidualMm, 0, links * 1e-9);
            Assert.Equal(frame.Coordinates["carrier"].Estimate / 2 - 3d/16, frame.Coordinates["suffix/out"].Estimate, 12);
            Assert.Null(frame.DisplayUnavailableReason);
        }
        var path = new[] { new WindingDriveInput(new Rational(1,8), new Rational(3,10)), new WindingDriveInput(new Rational(-1,8), new Rational(3,10)), new WindingDriveInput(0,new Rational(3,10)) };
        var advanced = WindingDifferentialEngine.Advance(a,first,path); Assert.True(advanced.IsAccepted, advanced.Status);
        Assert.Equal(first.SnapshotId,advanced.Frame!.SnapshotId);
        var secondInput = WindingDifferentialEngine.Evaluate(a,new(0,new Rational(4,10))).Frame!;
        Assert.Equal(-1d/220,secondInput.Coordinates["suffix/out"].Estimate-first.Coordinates["suffix/out"].Estimate,12);
    }

    [Fact]
    public void Intermediate_boundary_failure_returns_no_partial_frame_and_preserves_source()
    {
        var a = WindingDifferentialEngine.Prepare(Example.Create()); var first = WindingDifferentialEngine.Evaluate(a,new(0,0)).Frame!;
        foreach (var end in new[] { new Rational(3,10), new Rational(-3,10) })
        {
            var result = WindingDifferentialEngine.Advance(a,first,new[] { new WindingDriveInput(new Rational(1,10),0),new WindingDriveInput(end,0),new WindingDriveInput(0,0) });
            Assert.False(result.IsAccepted); Assert.Equal("WindingBoundary",result.Status); Assert.Null(result.Frame); Assert.Empty(result.Frames); Assert.Equal(0,result.AppliedSegments); Assert.Same(first,result.LastValidSnapshot); Assert.Equal(3,result.Remainder.Count);
        }
    }

    [Fact]
    public void Suffix_exact_law_retains_signed_mount_and_both_inputs()
    {
        var a = DifferentialSuffixAnalyzer.Prepare(Example.Create().Suffix); Assert.True(a.IsValid,string.Join(",",a.Diagnostics));
        Assert.Equal(new Rational(1,2),a.Transfer); Assert.Equal(new Rational(-3,16),a.Offset);
        Assert.Equal(new Rational(5,11),a.OutputLaw!.Coefficients["sun-port"]); Assert.Equal(new Rational(-1,22),a.OutputLaw.Coefficients["planet-port"]); Assert.Equal(new Rational(-129,880),a.OutputLaw.Offset);
    }

    [Fact]
    public void Exact_input_just_outside_binary64_domain_is_not_rounded_back_inside()
    {
        var a=WindingDifferentialEngine.Prepare(Example.Create());var outside=new Rational(1,8)+new Rational(1,BigInteger.Pow(10,50));
        Assert.Equal("WindingBoundary",WindingDifferentialEngine.Evaluate(a,new(outside,0)).Status);
        var w=a.Source.Winding;var initial=new FiniteWindingDefinition(w.DriverShaft,w.OutputShaft,w.DriverBodyId,w.OutputBodyId,w.ChainId,w.Geometry,outside,4,1);
        Assert.Equal("WindingBoundary",initial.Validate());
    }

    [Fact]
    public void Invalid_pitch_joint_length_and_unperformed_solid_domains_cannot_finalize()
    {
        var s=Example.Create();var g=s.Winding.Geometry;
        FiniteWindingGeometry Change(int links,double pitch,double bend)=>new(g.DriverCenter,g.OutputCenter,g.PlaneZMm,g.DriverSeats,g.OutputSeats,links,pitch,5,5,bend,g.DriverMinimumTurns,g.DriverMaximumTurns,g.OutputMinimumTurns,g.OutputMaximumTurns);
        Assert.Equal("InconsistentPitch",Change(21,2.1,45).Validate());Assert.Equal("JointLimit",Change(21,2,20).Validate());Assert.Equal("InfeasibleProvedByEndpointDistanceBound",Change(4,2,45).Validate());
        var required=new WindingDifferentialDefinition(s.Winding,s.Suffix,s.CouplingId,s.SunPortId,s.PlanetPortId,0,new[]{"tooth-solids","bounded-path"});
        Assert.Contains("RequiredNotPerformed:tooth-solids",WindingDifferentialEngine.Prepare(required).Diagnostics);
        Assert.Contains("RequiredNotPerformed:bounded-path",WindingDifferentialEngine.Prepare(required).Diagnostics);
        Assert.Throws<ArgumentException>(()=>GearInvestSdk.CreateDefault().FinalizeWindingConnection(required));
        foreach(var count in new[]{-1,int.MaxValue})
        {
            var bad=new FiniteWindingDefinition(s.Winding.DriverShaft,s.Winding.OutputShaft,s.Winding.DriverBodyId,s.Winding.OutputBodyId,s.Winding.ChainId,Change(count,2,45),0,3,3);
            var invalid=new WindingDifferentialDefinition(bad,s.Suffix,s.CouplingId,s.SunPortId,s.PlanetPortId,0);
            Assert.Contains("ResourceLimit",WindingDifferentialEngine.Prepare(invalid).Diagnostics);
        }
    }

    [Fact]
    public void Suffix_rejects_moving_owner_off_axis_wrong_distance_wrong_phase_and_alias_basis()
    {
        var s=Example.Create().Suffix;
        DifferentialSuffixDefinition Copy(string? owner=null,GearInvest.Layout.OrientedGearBody? gear=null,ExactQuantity? reference=null,DifferentialRequest? parent=null)=>new(parent??s.Parent,owner??s.SourceShaftId,s.OutputShaft,s.DriverGear,gear??s.OutputGear,s.ContactId,s.DriverMount,s.OutputMount,s.ToothRegistration,reference??s.OutputReference,s.OutputPort);
        Assert.Contains("UnsupportedMovingOrUnknownSourceOwner",DifferentialSuffixAnalyzer.Prepare(Copy(owner:"planet")).Diagnostics);
        var bad=new GearInvest.Layout.OrientedGearBody(s.OutputGear.Id,s.OutputGear.ShaftId,s.OutputGear.Kind,s.OutputGear.MountingFrame.At(new ExactVector3(31,0,20)),40,20,"suffix");
        Assert.False(DifferentialSuffixAnalyzer.Prepare(Copy(gear:bad)).IsValid);
        Assert.Contains("InconsistentReferenceRegistration",DifferentialSuffixAnalyzer.Prepare(Copy(reference:ExactQuantity.Turns(0))).Diagnostics);
        Assert.False(DifferentialSuffixAnalyzer.Prepare(Copy(parent:new DifferentialRequest(s.Parent.Definition,new[]{"sun-port"}))).IsValid);
        var sdk=GearInvestSdk.CreateDefault();var a=sdk.PrepareDifferentialSuffix(s);
        foreach(var pair in new[]{(new Rational(1,5),new Rational(3,10)),(new Rational(-7,4),new Rational(9,8)),(new Rational(1000000),new Rational(-500000))})
        {
            var vector=new DifferentialInputSnapshot(new Dictionary<string,ExactQuantity>{{"sun-port",ExactQuantity.Turns(pair.Item1)},{"planet-port",ExactQuantity.Turns(pair.Item2)}});
            var o=sdk.EvaluateDifferentialSuffix(a,vector);Assert.Equal(new Rational(5,11)*pair.Item1-new Rational(1,22)*pair.Item2-new Rational(129,880),o);Assert.True(sdk.DisplayDifferentialSuffix(a,vector).IsAvailable);
        }
        Assert.Equal(sdk.FinalizeDifferentialSuffix(s).Bytes,sdk.ReadDifferentialSuffix(sdk.FinalizeDifferentialSuffix(s).Bytes).Bytes);
    }

    [Fact]
    public void Authored_body_names_do_not_collide_with_internal_parent_pose_names()
    {
        var sdk=GearInvestSdk.CreateDefault();var s=Example.Create().Suffix;
        OrientedGearBody Rename(OrientedGearBody b,string id)=>new(id,b.ShaftId,b.Kind,b.MountingFrame,b.Teeth,b.OuterPitchRadius,b.SourceModuleId);
        var source=new DifferentialSuffixDefinition(s.Parent,s.SourceShaftId,s.OutputShaft,Rename(s.DriverGear,"carrier-frame"),Rename(s.OutputGear,"planet-shaft"),s.ContactId,s.DriverMount,s.OutputMount,s.ToothRegistration,s.OutputReference,s.OutputPort);
        var a=sdk.PrepareDifferentialSuffix(source);Assert.True(a.IsValid,string.Join(",",a.Diagnostics));
        var pose=sdk.DisplayDifferentialSuffix(a,new(new Dictionary<string,ExactQuantity>{{"sun-port",ExactQuantity.Turns(0)},{"planet-port",ExactQuantity.Turns(0)}}));
        Assert.True(pose.IsAvailable,pose.UnavailableReason);Assert.Equal(a.Parent.PoseNodes.Count+2,pose.Matrices.Count);
        Assert.Contains(ConnectedPoseIds.DriverGear,pose.Matrices.Keys);Assert.Contains(ConnectedPoseIds.OutputGear,pose.Matrices.Keys);
    }

    [Fact]
    public void Guard_nearby_checkpoints_rebuild_and_foreign_or_modified_recordings_are_refused()
    {
        var sdk=GearInvestSdk.CreateDefault();var artifact=sdk.FinalizeWindingConnection(Example.Create());var g=artifact.Source.Winding.Geometry;
        var guards=FiniteWindingSolver.GuardCandidates(g).Where(q=>
        {var l=FiniteWindingSolver.Evaluate(g,q-1e-8);var r=FiniteWindingSolver.Evaluate(g,q+1e-8);return l.IsAccepted&&r.IsAccepted&&(l.Pose!.DriverContact!=r.Pose!.DriverContact||l.Pose.OutputContact!=r.Pose.OutputContact);}).Distinct().Take(7).ToArray();
        Assert.NotEmpty(guards);
        foreach(var root in guards)
        {
            Rational Input(double x)=>new((long)Math.Round(x*1e12),1000000000000L);
            var recording=sdk.RecordWindingConnection(artifact,0,new[]{new[]{new WindingDriveInput(Input(root-1e-8),0),new WindingDriveInput(Input(root+1e-8),0)}});
            Assert.True(recording.Recording.Attempts[0].IsAccepted,recording.Recording.Attempts[0].Status);
            Assert.Equal(recording.Recording.Final.SnapshotId,sdk.ReadWindingRecording(recording.Bytes).Recording.Final.SnapshotId);
        }
        var first=sdk.EvaluateWindingConnection(artifact.Analysis,new(0,0)).Frame!;var foreign=sdk.PrepareWindingConnection(Example.Create(2,20));
        Assert.Equal("ForeignSnapshot",sdk.AdvanceWindingConnection(foreign,first,new[]{new WindingDriveInput(0,0)}).Status);
        var tampered=System.Text.Json.Nodes.JsonNode.Parse(artifact.Bytes)!;tampered["validation"]="fake-pass";
        Assert.Throws<ArtifactFormatException>(()=>sdk.ReadWindingConnectionArtifact(System.Text.Encoding.UTF8.GetBytes(tampered.ToJsonString())));
    }
}
