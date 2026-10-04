using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;
using GearInvest.WindingExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class DriveBoundaryContractTests
{
    private readonly GearInvestSdk sdk=GearInvestSdk.CreateDefault();
    [Theory]
    [InlineData(201,3,4,1)] [InlineData(320,5,4,3)]
    public void D3_roles_material_neighbours_geometry_frames_and_scale(int links,int n,int d,int stages)
    {
        var old=SpatialExample.Create(links,new Rational(n,d),"variant-"+links,stages);
        var original=(SpatialWindingDefinition)old.WindingSource;
        var chosen=original.SelectDriveBoundary(original.OutputShaft.Id,new Rational(17,1000));
        Assert.Same(original.Geometry.Output,chosen.Geometry.Driver);Assert.Same(original.Geometry.Driver,chosen.Geometry.Output);
        Assert.Equal(original.Geometry.Bridge.Reverse(),chosen.Geometry.Bridge);
        for(var i=0;i<=links;i++)Assert.Equal(original.PinId(links-i),chosen.PinId(i));
        for(var i=0;i<links;i++)Assert.Equal(original.LinkId(links-1-i),chosen.LinkId(i));
        var returned=chosen.SelectDriveBoundary(original.DriverShaft.Id,0);
        Assert.Equal(original.PinId(0),returned.PinId(0));Assert.Equal(original.Geometry.Driver.Hand,returned.Geometry.Driver.Hand);
        Assert.Throws<ArgumentException>(()=>original.SelectDriveBoundary(old.Suffix.OutputShaft.Id,0));
        var source=new WindingDifferentialDefinition(chosen,chosen.DriverShaft.Id,old.Suffix,old.CouplingId,old.SunPortId,old.PlanetPortId,old.InitialCouplingOffset,transmission:old.Transmission);
        var artifact=sdk.FinalizeWindingConnection(source);using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"variant",new Rational(2,7));
        DriveBoundaryTests.Apply(run,"forward",new Rational(27,1000),new Rational(-2,9));
        DriveBoundaryTests.Apply(run,"reverse",new Rational(-13,1000),new Rational(3,11));
        var f=run.Current.Mechanical.Frame;Assert.Equal(links+1,f.SpatialWinding!.Pins.Count);
        CheckChords(chosen,f);
        Assert.Equal(artifact.Bytes,sdk.ReadWindingConnectionArtifact(artifact.Bytes).Bytes);
    }
    [Fact]
    public void D3_different_passive_frame_phase_and_nonunit_tooth_contact_are_not_special_cased()
    {
        var s=SpatialExample.CreateSelectedDrive(extraStages:1);var w=(SpatialWindingDefinition)s.WindingSource;var g=w.Geometry;var h=g.Output;
        var frame=new OrientedFrame(h.Frame.Origin,ExactVector3.UnitY,-ExactVector3.UnitX,ExactVector3.UnitZ);
        var guide=new HelicalPinGuide(frame,h.RadiusMm,h.RadiusChangeMmPerTurn,h.HeightChangeMmPerTurn,h.Hand,1d/16,h.ExitAzimuthTurns,h.WindingBranch,h.MaximumGuideTurns);
        var geometry=new SpatialWindingGeometry(g.Driver,guide,new[]{g.Bridge[0],guide.Meridian(guide.MaximumGuideTurns)},g.LinkCount,g.PitchMm,g.MaxBendDegrees,g.MaxAttachmentBendDegrees,g.DriverMinimumTurns,g.DriverMaximumTurns,g.OutputMinimumTurns,g.OutputMaximumTurns);
        var winding=new SpatialWindingDefinition(w.DriverShaft,new("different-passive-owner",frame),w.DriverBodyId,"different-passive-body","different-material",geometry,w.InitialDriverTurns,w.MaterialOrder);
        var old=s.Transmission!.Stages[0];var of=old.OutputShaft.Frame.At(old.DriverGear.MountingFrame.Origin+new ExactVector3(30,0,0));
        var shaft=new OrientedShaft("new-terminal-owner",of);var reference=-new Rational(2,3)*s.Suffix.OutputReference.Value+new Rational(1,36);
        var stage=new FixedAxisSpurStage(s.Suffix.OutputShaft.Id,shaft,
            new("new-24",s.Suffix.OutputShaft.Id,OrientedGearKind.PlanarSpur,old.DriverGear.MountingFrame,24,12,"changed-stage"),
            new("new-36",shaft.Id,OrientedGearKind.PlanarSpur,of,36,18,"changed-stage"),"new-contact",new Rational(1,24),new Rational(-1,36),1,reference,
            new("new-port",shaft.Id,OrientedFrame.Identity,ExactQuantity.Turns(new Rational(2,13))));
        var transmission=new ConnectedTransmission(new[]{stage},shaft.Id,"new-carrier",of,new Rational(1,9),Array.Empty<CarrierRigidAttachment>());
        var source=new WindingDifferentialDefinition(winding,winding.DriverShaft.Id,s.Suffix,s.CouplingId,s.SunPortId,s.PlanetPortId,s.InitialCouplingOffset,transmission:transmission);
        var artifact=sdk.FinalizeWindingConnection(source);using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"changed-geometry",new Rational(3,10));
        DriveBoundaryTests.Apply(run,"new",new Rational(7,100),new Rational(-2,7));var f=run.Current.Mechanical.Frame;
        Assert.Equal(Rational.Zero,f.Coordinates[shaft.Id].Scale(36,-1).Plus(f.Coordinates[s.Suffix.OutputShaft.Id].Scale(24,0)).Exact);
        CheckChords(winding,f);
    }
    [Fact]
    public void D4_modes_alignment_locks_direction_and_atomic_input_rejection()
    {
        var s=DriveBoundaryTests.Source();var artifact=sdk.FinalizeWindingConnection(s);var policy=new MechanicalModeDefinition(s,alignmentOffset:s.InitialCouplingOffset);
        using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"mode-contract",new Rational(3,10),policy);
        var bytes=artifact.Bytes; var material=((SpatialWindingDefinition)s.WindingSource).PinId(0);
        Send("aligned",new Rational(13,1000),MechanicalConnectionEventKind.AlignCapture);
        Send("release",new Rational(17,1000),MechanicalConnectionEventKind.Release);
        Reject("Underdetermined",new MechanicalModeInput(new Rational(17,1000),new Dictionary<string,Rational>{{s.PlanetPortId,0}}));
        // Arrival remains Released; a failed capture never publishes that arrival.
        Send("bad-align",new Rational(17,1000),MechanicalConnectionEventKind.AlignCapture,"AlignmentConflict",sun:1);
        Send("align-from-independent",new Rational(17,1000),MechanicalConnectionEventKind.AlignCapture,sun:new Rational(17,1000)+s.InitialCouplingOffset);
        Send("positive",new Rational(17,1000),MechanicalConnectionEventKind.CapturePositive);
        Send("back-conflict",new Rational(16,1000),expected:"DirectionConflict");
        Send("forward",new Rational(19,1000));
        Send("negative",new Rational(19,1000),MechanicalConnectionEventKind.CaptureNegative);
        Send("forward-conflict",new Rational(20,1000),expected:"DirectionConflict");
        Send("back",new Rational(18,1000));
        Send("world",new Rational(18,1000),MechanicalConnectionEventKind.LockWorldCarrier);
        var locked=run.Current.Mechanical.Frame.Coordinates[s.Suffix.Parent.Definition.CarrierShaft.Id];
        Reject("ModeInputOwnershipConflict",new MechanicalModeInput(new Rational(18,1000),new Dictionary<string,Rational>{{s.PlanetPortId,0}}));
        Send("world-move",new Rational(21,1000));
        Assert.Equal(Rational.Zero,run.Current.Mechanical.Frame.Coordinates[s.Suffix.Parent.Definition.CarrierShaft.Id].Minus(locked).Exact);
        Send("relative",new Rational(21,1000),MechanicalConnectionEventKind.LockPlanetRelative);
        var prior=run.Current.Mechanical.Frame;Send("relative-move",new Rational(23,1000));var next=run.Current.Mechanical.Frame;
        // Planet local axis is -world Z, so relative world rotation is -planet_native-carrier.
        var pd=s.Suffix.Parent.Definition;
        Assert.Equal(Rational.Zero,next.Coordinates[pd.PlanetShaft.Id].Plus(next.Coordinates[pd.CarrierShaft.Id]).Minus(prior.Coordinates[pd.PlanetShaft.Id].Plus(prior.Coordinates[pd.CarrierShaft.Id])).Exact);
        using var reopened=sdk.RestoreMechanicalRuntime(run.Checkpoint().Bytes);Assert.Equal(run.Current.StateId,reopened.Current.StateId);
        var st=reopened.Current;var more=new RuntimeRequest("locked-restored",st.SessionId,st.Definition.DefinitionId,st.StateId,st.Epoch,st.Revision,new[]{new RuntimeSegment(new(new Rational(29,1000),Array.Empty<KeyValuePair<string,Rational>>()))});
        Assert.True(reopened.Advance(more).IsAccepted);
        Assert.Equal(bytes,sdk.FinalizeWindingConnection(s).Bytes);Assert.Equal(material,((SpatialWindingDefinition)s.WindingSource).PinId(0));
        void Reject(string status,MechanicalModeInput input)
        {var before=run.Current;var snapshot=run.Snapshot();var result=run.Advance(new("bad-"+status,before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,new[]{new RuntimeSegment(input)}));Assert.Equal(status,result.Status);Assert.Same(before,run.Current);Assert.Equal(snapshot,run.Snapshot());}
        void Send(string id,Rational q,MechanicalConnectionEventKind? kind=null,string expected="Accepted",Rational? sun=null)
        {
            var st=run.Current;var ports=MechanicalModeEngine.RequiredInputPorts(st.Definition,st.Mechanical.Mode).ToDictionary(x=>x,x=>x==s.SunPortId?sun??new Rational(7,19):new Rational(3,10));
            var request=new RuntimeRequest(id,st.SessionId,st.Definition.DefinitionId,st.StateId,st.Epoch,st.Revision,
                new[]{new RuntimeSegment(new(q,ports),kind.HasValue?new[]{new RuntimeEvent(id+"-event",st.EventCursor+1,0,kind.Value)}:Array.Empty<RuntimeEvent>())});
            Assert.Equal(expected,run.Advance(request).Status);if(expected!="Accepted")Assert.Same(st,run.Current);
        }
    }
    [Theory]
    [InlineData("boundary")] [InlineData("coupling")] [InlineData("role")] [InlineData("latent")]
    [InlineData("capture")] [InlineData("lock")] [InlineData("material")]
    public void D7_rehashed_boundary_owner_and_witness_tampering_is_not_trusted(string mutation)
    {
        var s=DriveBoundaryTests.Source();using var run=sdk.PrepareMechanicalRuntime(sdk.FinalizeWindingConnection(s).Bytes,"tamper-selected",0);
        DriveBoundaryTests.Apply(run,"capture",new Rational(13,1000),0,MechanicalConnectionEventKind.Release);
        var b=run.Current;var req=new RuntimeRequest("independent-capture",b.SessionId,b.Definition.DefinitionId,b.StateId,b.Epoch,b.Revision,
            new[]{new RuntimeSegment(new(new Rational(17,1000),new Dictionary<string,Rational>{{s.SunPortId,new Rational(3,8)},{s.PlanetPortId,0}}),new[]{new RuntimeEvent("capture-event",b.EventCursor+1,0,MechanicalConnectionEventKind.Capture)})});
        Assert.True(run.Advance(req).IsAccepted);DriveBoundaryTests.Apply(run,"lock",new Rational(19,1000),0,MechanicalConnectionEventKind.LockWorldCarrier);
        var envelope=JsonNode.Parse(run.Checkpoint().Bytes)!;var p=JsonNode.Parse(Convert.FromBase64String(envelope["payloadUtf8"]!.GetValue<string>()))!;var state=p["state"]!;
        switch(mutation)
        {
            case "boundary":state["driveBoundary"]!["prescribedShaftId"]=s.WindingSource.OutputShaft.Id;break;
            case "coupling":state["driveBoundary"]!["couplingShaftId"]=s.WindingSource.OutputShaft.Id;break;
            case "role":envelope["formatVersion"]="2.0";break;
            case "latent":state["witnesses"]![0]!["driverTurns"]!["numerator"]="91";break;
            case "capture":state["captureWitness"]!["driverTurns"]!["numerator"]="5";break;
            case "lock":state["lockWitness"]!["driverTurns"]!["numerator"]="5";break;
            case "material":state["frame"]!["winding"]!["pins"]![0]!["id"]="different-material/pin-000";break;
        }
        var raw=Encoding.UTF8.GetBytes(p.ToJsonString());envelope["payloadUtf8"]=Convert.ToBase64String(raw);envelope["payloadId"]=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        Assert.ThrowsAny<Exception>(()=>sdk.RestoreMechanicalRuntime(Encoding.UTF8.GetBytes(envelope.ToJsonString())));
    }
    [Fact]
    public void D5_reversed_finite_range_budget_branch_and_atomic_out_and_back()
    {
        var s=DriveBoundaryTests.Source();var w=(SpatialWindingDefinition)s.WindingSource;var artifact=sdk.FinalizeWindingConnection(s);
        using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"range-selected",0);var before=run.Current;
        var near=sdk.EvaluateWindingConnection(artifact.Analysis,new(new Rational(-1,1),0));Assert.True(near.IsAccepted,near.Status);CheckChords(w,near.Frame!);
        var query=SpatialWindingSolver.EvaluateRefined(w.Geometry,.02,10);Assert.Equal("ResourceLimit",query.Status);Assert.Null(query.Pose);
        var g=w.Geometry;var restricted=new SpatialWindingGeometry(g.Driver,g.Output,g.Bridge,g.LinkCount,g.PitchMm,g.MaxBendDegrees,g.MaxAttachmentBendDegrees,g.DriverMinimumTurns,g.DriverMaximumTurns,-1.19,-1.18);
        Assert.Equal("NoBracketInDeclaredBranch",SpatialWindingSolver.EvaluateRefined(restricted,.02).Status);
        var snapshot=run.Snapshot();
        var oppositeBoundary=run.Advance(SpatialRuntimeTests.Request(before,"opposite-range",new Rational(-23,20),0));
        Assert.Equal("NoBracketInDeclaredBranch",oppositeBoundary.Status);Assert.Same(before,run.Current);Assert.Equal(snapshot,run.Snapshot());
        var p=new Dictionary<string,Rational>{{s.PlanetPortId,0}};
        var request=new RuntimeRequest("out-and-back",before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,
            new[]{new RuntimeSegment(new(new Rational(3,100),p)),new RuntimeSegment(new(2,p)),new RuntimeSegment(new(new Rational(13,1000),p))});
        Assert.Equal("WindingBoundary",run.Advance(request).Status);Assert.Same(before,run.Current);
    }
    [Fact]
    public void D3_explicit_original_drive_keeps_numerical_passive_coupling_and_correlation()
    {
        var old=SpatialExample.Create();var w=(SpatialWindingDefinition)old.WindingSource;
        var selected=w.SelectDriveBoundary(w.DriverShaft.Id,new Rational(13,1000));
        var source=new WindingDifferentialDefinition(selected,selected.OutputShaft.Id,old.Suffix,old.CouplingId,old.SunPortId,old.PlanetPortId,old.InitialCouplingOffset,transmission:old.Transmission);
        using var run=sdk.PrepareMechanicalRuntime(sdk.FinalizeWindingConnection(source).Bytes,"explicit-original",0);
        DriveBoundaryTests.Apply(run,"release",new Rational(23,1000),0,MechanicalConnectionEventKind.Release);
        var state=run.Current;
        var input=new MechanicalModeInput(new Rational(31,1000),new Dictionary<string,Rational>{{source.SunPortId,new Rational(7,19)},{source.PlanetPortId,new Rational(2,7)}});
        Assert.True(run.Advance(new("capture",state.SessionId,state.Definition.DefinitionId,state.StateId,state.Epoch,state.Revision,new[]{new RuntimeSegment(input,new[]{new RuntimeEvent("capture-event",state.EventCursor+1,0,MechanicalConnectionEventKind.Capture)})})).IsAccepted);
        Assert.False(run.Current.Mechanical.CouplingOffset.IsExact);
        using var restored=sdk.RestoreMechanicalRuntime(run.Checkpoint().Bytes);
        DriveBoundaryTests.Apply(restored,"continued",new Rational(-17,1000),new Rational(1,9));
        var f=restored.Current.Mechanical.Frame;
        Assert.Equal(new Rational(-17,1000),f.Coordinates[selected.DriverShaft.Id].Exact);
        Assert.Equal(Rational.Zero,f.Coordinates[source.Suffix.Parent.Definition.SunShaft.Id].Minus(f.Coordinates[selected.OutputShaft.Id]).Minus(restored.Current.Mechanical.CouplingOffset).Exact);
        CheckChords(selected,f);
    }
    [Fact]
    public void D5_request_resource_admission_cannot_mutate_selected_runtime()
    {
        var s=DriveBoundaryTests.Source();using var run=sdk.PrepareMechanicalRuntime(sdk.FinalizeWindingConnection(s).Bytes,"large-request",0);
        var before=run.Current;var snapshot=run.Snapshot();
        var segments=Enumerable.Range(0,17).Select(_=>new RuntimeSegment(new(new Rational(17,1000),new Dictionary<string,Rational>{{s.PlanetPortId,0}})));
        var error=Assert.Throws<ArgumentException>(()=>new RuntimeRequest("too-many-segments",before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,segments));
        Assert.Equal("ResourceLimit",error.Message);Assert.Same(before,run.Current);Assert.Equal(snapshot,run.Snapshot());
    }
    [Fact]
    public void Legacy_terminal_extra_and_observation_are_still_not_inverse_commands()
    {
        var s=SpatialExample.Create();using var run=sdk.PrepareMechanicalRuntime(sdk.FinalizeWindingConnection(s).Bytes,"legacy-boundary",new Rational(3,10));var before=run.Current;
        foreach(var observed in new[]{false,true})
        {
            var ports=new Dictionary<string,Rational>{{s.PlanetPortId,new Rational(3,10)}};var observations=new Dictionary<string,Rational>();
            (observed?observations:ports).Add(s.Transmission!.Stages.Last().OutputPort.Id,new Rational(1,60));
            var req=new RuntimeRequest("legacy-"+observed,before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,new[]{new RuntimeSegment(new(0,ports,observations))});
            Assert.Equal(observed?"GuardIndeterminate":"ModeInputOwnershipConflict",run.Advance(req).Status);Assert.Same(before,run.Current);
        }
    }
    [Fact]
    public void Legacy_positional_null_domains_remains_source_compatible()
    {
        var s=SpatialExample.Create();
        var authored=new WindingDifferentialDefinition((SpatialWindingDefinition)s.WindingSource,s.Suffix,s.CouplingId,s.SunPortId,s.PlanetPortId,s.InitialCouplingOffset,null);
        Assert.False(authored.HasSelectedDriveBoundary);Assert.Equal(s.WindingSource.OutputShaft.Id,authored.CouplingShaftId);
        Assert.NotNull(sdk.FinalizeWindingConnection(authored));
    }
    private static void CheckChords(SpatialWindingDefinition source,ConnectedFrame frame)
    {
        var pins=frame.SpatialWinding!.Pins;var g=source.Geometry;
        for(var i=1;i<pins.Count;i++)
        {var a=pins[i-1].PositionMm;var b=pins[i].PositionMm;var chord=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2)+Math.Pow(b.Z-a.Z,2));Assert.InRange(Math.Abs(chord-g.PitchMm),0,1e-8);}
        Assert.InRange(frame.SpatialWinding.MaximumBendDegrees,0,g.MaxBendDegrees);
        Assert.InRange(frame.SpatialWinding.MaximumAttachmentBendDegrees,0,g.MaxAttachmentBendDegrees);
        Assert.InRange(frame.SpatialWinding.AttachmentResidualMm,0,1e-8);
        Assert.Equal(source.DriverShaft.Id,frame.Coordinates.Single(c=>c.Key==source.DriverShaft.Id&&c.Value.IsExact).Key);
    }
}
