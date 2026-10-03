using System.Text;
using System.Text.Json.Nodes;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using GearInvest.WindingExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class MechanicalModeTests
{
    [Fact]
    public void Real_connected_capture_locks_release_and_atomic_refusals_preserve_contact_source()
    {
        var sdk=GearInvestSdk.CreateDefault();var source=sdk.FinalizeWindingConnection(Example.Create());var bytes=source.Bytes;
        var artifact=ModeExample.Create(sdk,source);var r=artifact.Recording;
        Assert.Equal(19,r.Attempts.Count);Assert.Equal(5,r.Attempts.Count(a=>!a.IsAccepted&&a.Status!="AlreadyApplied"));Assert.Single(r.Attempts,a=>a.Status=="AlreadyApplied");
        var before=r.Attempts.Single(a=>a.Request.Id=="released-motion").State!;var capture=r.Attempts.Single(a=>a.Request.Id=="capture").State!;
        Assert.Equal(before.Frame.SnapshotId,capture.Frame.SnapshotId);Assert.Equal("NumericResidualOnly",capture.CouplingOffset.Kind);
        var locked=r.Attempts.Single(a=>a.Request.Id=="lock-world").State!;var moved=r.Attempts.Single(a=>a.Request.Id=="world-locked-motion").State!;
        Assert.Equal(locked.Frame.Coordinates["carrier"].Estimate,moved.Frame.Coordinates["carrier"].Estimate,14);
        Assert.NotEqual(locked.Frame.Coordinates["planet"].Estimate,moved.Frame.Coordinates["planet"].Estimate);
        var relative=r.Attempts.Single(a=>a.Request.Id=="lock-relative").State!;var after=r.Attempts.Single(a=>a.Request.Id=="relative-locked-motion").State!;
        Assert.Equal(-relative.Frame.Coordinates["planet"].Estimate-relative.Frame.Coordinates["carrier"].Estimate,-after.Frame.Coordinates["planet"].Estimate-after.Frame.Coordinates["carrier"].Estimate,13);
        Assert.NotEqual(relative.Frame.Coordinates["carrier"].Estimate,after.Frame.Coordinates["carrier"].Estimate);
        foreach(var fail in r.Attempts.Where(a=>!a.IsAccepted)){Assert.Null(fail.State);Assert.Empty(fail.Samples);Assert.Equal(0,fail.AppliedSegments);}
        Assert.Equal(bytes,sdk.FinalizeWindingConnection(artifact.Source.Source).Bytes);
        Assert.Equal(r.Final.StateId,sdk.ReadMechanicalModeRecording(artifact.Bytes).Recording.Final.StateId);
        Assert.Equal(r.Final.StateId,sdk.RebuildMechanicalModeReplay(sdk.ExportMechanicalModeReplay(artifact)).Recording.Final.StateId);
        Assert.Equal(artifact.Bytes,sdk.ReadMechanicalModeRecording(artifact.Bytes).Bytes);
    }
    [Fact]
    public void Stale_foreign_conflicting_and_invalid_order_requests_do_not_change_state()
    {
        var d=new MechanicalModeDefinition(Example.Create());var state=MechanicalModeEngine.Start(d,0);var port=d.Connection.PlanetPortId;
        var segment=new MechanicalModeSegment(new(0,new Dictionary<string,Rational>{{port,0}}),new[]{new MechanicalConnectionEvent("capture",1,0,MechanicalConnectionEventKind.Capture)});
        var request=new MechanicalModeRequest("batch",d.DefinitionId,state.StateId,0,new[]{segment});var ok=MechanicalModeEngine.Advance(d,state,request);Assert.True(ok.IsAccepted);
        Assert.Equal("AlreadyApplied",MechanicalModeEngine.Advance(d,ok.State!,request).Status);
        var conflict=new MechanicalModeRequest("batch",d.DefinitionId,ok.State!.StateId,ok.State.Revision,new[]{segment});Assert.Equal("IdempotencyConflict",MechanicalModeEngine.Advance(d,ok.State,conflict).Status);
        var stale=new MechanicalModeRequest("other",d.DefinitionId,state.StateId,0,new[]{segment});Assert.Equal("StaleSnapshot",MechanicalModeEngine.Advance(d,ok.State,stale).Status);
        var foreign=new MechanicalModeRequest("foreign","foreign",state.StateId,0,new[]{segment});Assert.Equal("ForeignSnapshot",MechanicalModeEngine.Advance(d,state,foreign).Status);
        var ordered=new MechanicalModeSegment(segment.Input,new[]{new MechanicalConnectionEvent("release",1,1,MechanicalConnectionEventKind.Release)});
        Assert.Equal("InvalidEventOrder",MechanicalModeEngine.Advance(d,state,new("invalid-order",d.DefinitionId,state.StateId,0,new[]{ordered})).Status);
    }
    [Fact]
    public void Correlated_numeric_alignment_is_exact_only_when_original_relation_proves_it()
    {
        var d=new MechanicalModeDefinition(Example.Create());var state=MechanicalModeEngine.Start(d,0);
        var s=new MechanicalModeSegment(new(0,new Dictionary<string,Rational>{{d.Connection.PlanetPortId,0}}),new[]{new MechanicalConnectionEvent("align",1,0,MechanicalConnectionEventKind.AlignCapture)});
        var ok=MechanicalModeEngine.Advance(d,state,new("align",d.DefinitionId,state.StateId,0,new[]{s}));Assert.True(ok.IsAccepted,ok.Status);Assert.True(ok.State!.CouplingOffset.IsExact);
        var other=new MechanicalModeDefinition(d.Connection,alignmentOffset:new Rational(1,10));var first=MechanicalModeEngine.Start(other,0);
        Assert.Equal("AlignmentConflict",MechanicalModeEngine.Advance(other,first,new("wrong-alignment",other.DefinitionId,first.StateId,0,new[]{s})).Status);
    }
    [Fact]
    public void Rehashed_or_mutated_cached_coordinates_are_not_current_source_evidence()
    {
        var sdk=GearInvestSdk.CreateDefault();var artifact=ModeExample.Create(sdk,sdk.FinalizeWindingConnection(Example.Create()));
        var json=JsonNode.Parse(artifact.Bytes)!;json["results"]!["initial"]!["couplingOffset"]!["constant"]!["numerator"]="1";
        Assert.Throws<ArtifactFormatException>(()=>sdk.ReadMechanicalModeRecording(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tolerated_cross_runtime_residuals_keep_original_recording_and_replay_paired(bool mode)
    {
        var sdk=GearInvestSdk.CreateDefault();var source=sdk.FinalizeWindingConnection(Example.Create());
        var bytes=mode?ModeExample.Create(sdk,source).Bytes:sdk.RecordWindingConnection(source,0,Array.Empty<WindingDriveInput[]>()).Bytes;
        var root=JsonNode.Parse(bytes)!;var initial=root["results"]!["initial"]!;
        var frame=mode?initial["frame"]!:initial;var residual=frame["winding"]!["pitchResidualMm"]!.GetValue<double>()+1e-12;
        frame["winding"]!["pitchResidualMm"]=residual;
        var edited=Encoding.UTF8.GetBytes(root.ToJsonString());
        var replay=mode?sdk.ExportMechanicalModeReplay(sdk.ReadMechanicalModeRecording(edited)):sdk.ExportWindingReplay(sdk.ReadWindingRecording(edited));
        var payload=JsonNode.Parse(Convert.FromBase64String(JsonNode.Parse(replay)!["payloadUtf8"]!.GetValue<string>()))!;
        var stored=JsonNode.Parse(Convert.FromBase64String(payload["recordingUtf8"]!.GetValue<string>()))!;
        Assert.True(JsonNode.DeepEquals(stored["results"],payload["results"]));
        Assert.Equal(edited,mode?sdk.RebuildMechanicalModeReplay(replay).Bytes:sdk.RebuildWindingReplay(replay).Bytes);
    }

    [Fact]
    public void Negative_direction_and_bounded_retry_ledger_do_not_invent_reverse_events()
    {
        var d=new MechanicalModeDefinition(Example.Create());var state=MechanicalModeEngine.Start(d,0);var first=state;
        MechanicalModeRequest Request(string id,MechanicalModeState current,Rational q,params MechanicalConnectionEvent[] events)=>new(id,d.DefinitionId,current.StateId,current.Revision,new[]{new MechanicalModeSegment(new(q,new Dictionary<string,Rational>{{d.Connection.PlanetPortId,0}}),events)});
        var original=Request("zero-0",state,0);
        for(var i=0;i<17;i++){var next=MechanicalModeEngine.Advance(d,state,i==0?original:Request("zero-"+i,state,0));Assert.True(next.IsAccepted);state=next.State!;}
        Assert.Equal(16,state.Ledger.Count);Assert.Equal("StaleSnapshot",MechanicalModeEngine.Advance(d,state,original).Status);
        var capture=MechanicalModeEngine.Advance(d,state,Request("negative",state,0,new MechanicalConnectionEvent("negative",1,0,MechanicalConnectionEventKind.CaptureNegative)));Assert.True(capture.IsAccepted);state=capture.State!;
        var backward=MechanicalModeEngine.Advance(d,state,Request("backward",state,new Rational(-1,100)));Assert.True(backward.IsAccepted);state=backward.State!;
        Assert.Equal("DirectionConflict",MechanicalModeEngine.Advance(d,state,Request("forward",state,0)).Status);
        Assert.Equal(1,state.EventCursor);Assert.Equal(0,first.EventCursor);
    }

    [Fact]
    public void Events_too_close_to_active_contact_handoff_are_not_arbitrarily_ordered()
    {
        var d=new MechanicalModeDefinition(Example.Create());var state=MechanicalModeEngine.Start(d,0);var g=d.Connection.Winding.Geometry;
        var root=GearInvest.Layout.FiniteWindingSolver.GuardCandidates(g).First(q=>
        {var l=GearInvest.Layout.FiniteWindingSolver.Evaluate(g,q-1e-8);var r=GearInvest.Layout.FiniteWindingSolver.Evaluate(g,q+1e-8);return l.IsAccepted&&r.IsAccepted&&(l.Pose!.DriverContact!=r.Pose!.DriverContact||l.Pose.OutputContact!=r.Pose.OutputContact);});
        var q=new Rational((long)Math.Round(root*1e12),1000000000000L);
        var request=new MechanicalModeRequest("handoff",d.DefinitionId,state.StateId,0,new[]{new MechanicalModeSegment(new(q,new Dictionary<string,Rational>{{d.Connection.PlanetPortId,0}}),new[]{new MechanicalConnectionEvent("release",1,0,MechanicalConnectionEventKind.Release)})});
        var result=MechanicalModeEngine.Advance(d,state,request);Assert.Equal("GuardIndeterminate",result.Status);Assert.Null(result.State);Assert.Same(state,result.LastValidSnapshot);Assert.Empty(result.Samples);
    }
}
