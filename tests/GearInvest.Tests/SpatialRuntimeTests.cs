using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Security.Cryptography;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;
using GearInvest.WindingExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class SpatialRuntimeTests
{
    [Theory]
    [InlineData(201, 1)]
    [InlineData(249, 2)]
    [InlineData(320, 3)]
    public void Public_author_finalize_prepare_new_input_and_checkpoint_rebuild(int links, int stages)
    {
        var sdk = GearInvestSdk.CreateDefault(); var source = SpatialExample.Create(links, materialId:"material-"+links, extraStages:stages);
        var artifact = sdk.FinalizeWindingConnection(source);
        Assert.Equal(artifact.Bytes, sdk.ReadWindingConnectionArtifact(artifact.Bytes).Bytes);
        using var runtime = sdk.PrepareMechanicalRuntime(artifact.Bytes,"ordinary-spatial-consumer",new Rational(3,10));
        var state = runtime.Current; var r = runtime.Advance(Request(state,"input-not-recorded",new Rational(13,1000),new Rational(2,7)));
        Assert.True(r.IsAccepted,r.Status); Assert.NotEqual(state.StateId,runtime.Current.StateId);
        var frame = runtime.Current.Mechanical.Frame; Assert.Equal(links+1,frame.SpatialWinding!.Pins.Count);
        Assert.Equal(6+stages,frame.Coordinates.Count);
        Assert.True(frame.MatricesMm.ContainsKey("terminal/attached-body"));
        var previous = frame.Coordinates[source.Suffix.OutputShaft.Id];
        foreach(var stage in source.Transmission!.Stages)
        {
            var actual = frame.Coordinates[stage.OutputShaft.Id]; Assert.Equal(Rational.Zero,actual.Plus(previous).Exact);
            Assert.Equal(Rational.Zero,frame.Ports[stage.OutputPort.Id].Minus(actual.Scale(1,new Rational(1,7))).Exact); previous=actual;
        }
        var bytes = runtime.Checkpoint().Bytes;
        using var restored = sdk.RestoreMechanicalRuntime(bytes); Assert.Equal(runtime.Current.StateId,restored.Current.StateId);
        var more = restored.Advance(Request(restored.Current,"after-reopen",new Rational(-7,1000),new Rational(1,9)));
        Assert.True(more.IsAccepted,more.Status);
        using var snapshot=JsonDocument.Parse(restored.Snapshot(true));
        Assert.Equal(MechanicalRuntime.SpatialProfile,snapshot.RootElement.GetProperty("profile").GetString());
        Assert.True(snapshot.RootElement.GetProperty("scene").GetArrayLength()>links*2);
    }
    [Fact]
    public void Same_composition_release_independent_input_capture_world_lock_restore()
    {
        var sdk=GearInvestSdk.CreateDefault();var source=SpatialExample.Create();var artifact=sdk.FinalizeWindingConnection(source);
        using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"modes-spatial",new Rational(3,10));
        Accept(Request(run.Current,"release",new Rational(17,1000),new Rational(1,4),MechanicalConnectionEventKind.Release));
        var current=run.Current;
        var missing=run.Advance(Request(current,"missing",new Rational(2,100),new Rational(1,5)));
        Assert.Equal("Underdetermined",missing.Status);Assert.Same(current,run.Current);
        var independent=new MechanicalModeInput(new Rational(2,100),new Dictionary<string,Rational>{{source.SunPortId,new Rational(3,11)},{source.PlanetPortId,new Rational(-2,9)}});
        Accept(new("capture",current.SessionId,current.Definition.DefinitionId,current.StateId,current.Epoch,current.Revision,
            new[]{new RuntimeSegment(independent,new[]{new RuntimeEvent("event-capture",current.EventCursor+1,0,MechanicalConnectionEventKind.Capture)})}));
        Accept(Request(run.Current,"world-lock",new Rational(21,1000),new Rational(1,6),MechanicalConnectionEventKind.LockWorldCarrier));
        var locked=run.Current.Mechanical.Frame.Coordinates[source.Suffix.Parent.Definition.CarrierShaft.Id];
        var before=run.Current;
        Accept(new("advance-locked",before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,
            new[]{new RuntimeSegment(new(new Rational(3,100),Array.Empty<KeyValuePair<string,Rational>>()))}));
        Assert.Equal(Rational.Zero,run.Current.Mechanical.Frame.Coordinates[source.Suffix.Parent.Definition.CarrierShaft.Id].Minus(locked).Exact);
        using var restored=sdk.RestoreMechanicalRuntime(run.Checkpoint().Bytes);Assert.Equal(run.Current.StateId,restored.Current.StateId);
        void Accept(RuntimeRequest request){var r=run.Advance(request);Assert.True(r.IsAccepted,r.Status);}
    }
    [Fact]
    public void New_geometry_does_not_change_old_planar_format_or_runtime_profile()
    {
        var sdk=GearInvestSdk.CreateDefault();var old=sdk.FinalizeWindingConnection(Example.Create());
        using var run=sdk.PrepareMechanicalRuntime(old.Bytes,"old-consumer",0);
        using var checkpoint=JsonDocument.Parse(run.Checkpoint().Bytes);
        Assert.Equal("1.0",checkpoint.RootElement.GetProperty("formatVersion").GetString());
        using var source=JsonDocument.Parse(old.Bytes);Assert.Equal(WindingConnectionJson.ArtifactFormat,source.RootElement.GetProperty("format").GetString());
    }
    internal static RuntimeRequest Request(RuntimeSnapshot state,string id,Rational q,Rational planet,MechanicalConnectionEventKind? kind=null)
    {
        var input=new MechanicalModeInput(q,new Dictionary<string,Rational>{{state.Definition.Connection.PlanetPortId,planet}});
        return new(id,state.SessionId,state.Definition.DefinitionId,state.StateId,state.Epoch,state.Revision,
            new[]{new RuntimeSegment(input,kind is null?Array.Empty<RuntimeEvent>():new[]{new RuntimeEvent(id+"/event",state.EventCursor+BigInteger.One,0,kind.Value)})});
    }
    [Theory]
    [InlineData("pin")][InlineData("guide")][InlineData("owner")][InlineData("branch")][InlineData("latent")][InlineData("mode")][InlineData("cursor")][InlineData("source")]
    public void Rehashed_spatial_checkpoint_is_recomputed_not_digest_trusted(string change)
    {
        var sdk=GearInvestSdk.CreateDefault();var artifact=sdk.FinalizeWindingConnection(SpatialExample.Create());
        using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"spatial-tamper",0);
        Assert.True(run.Advance(Request(run.Current,"advance",new Rational(1,100),new Rational(2,9))).IsAccepted);
        var envelope=JsonNode.Parse(run.Checkpoint().Bytes)!;var p=JsonNode.Parse(Convert.FromBase64String(envelope["payloadUtf8"]!.GetValue<string>()))!;
        var s=p["state"]!;
        switch(change)
        {
            case "pin":s["frame"]!["winding"]!["pins"]![10]!["positionMm"]![2]=99;break;
            case "guide":s["frame"]!["winding"]!["pins"]![10]!["guidePiece"]="2";break;
            case "owner":s["frame"]!["coordinates"]![0]!["shaftId"]="fake-fixed-planet";break;
            case "branch":s["frame"]!["winding"]!["driverContact"]="99";break;
            case "latent":s["witnesses"]![0]!["driverTurns"]!["numerator"]="3";break;
            case "mode":s["mode"]="Released";break;
            case "cursor":s["eventCursor"]="11";break;
            case "source":p["sourceArtifactId"]=new string('0',64);break;
        }
        var raw=Encoding.UTF8.GetBytes(p.ToJsonString());envelope["payloadUtf8"]=Convert.ToBase64String(raw);envelope["payloadId"]=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        Assert.ThrowsAny<Exception>(()=>sdk.RestoreMechanicalRuntime(Encoding.UTF8.GetBytes(envelope.ToJsonString())));
    }
    [Fact]
    public void Whole_out_and_back_request_refuses_middle_boundary_without_publishing_any_arrival()
    {
        var sdk=GearInvestSdk.CreateDefault();var artifact=sdk.FinalizeWindingConnection(SpatialExample.Create());using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"atomic-spatial",0);
        var before=run.Current;var valid=Request(before,"draft",new Rational(1,100),0).Segments[0];
        var invalid=new RuntimeSegment(new(2,new Dictionary<string,Rational>{{artifact.Source.PlanetPortId,0}}));
        var request=new RuntimeRequest("out-and-back",before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,new[]{valid,invalid,valid});
        Assert.Equal("WindingBoundary",run.Advance(request).Status);Assert.Same(before,run.Current);
    }

    [Theory]
    [InlineData(3,4,201)]
    [InlineData(5,4,320)]
    public void Public_authoring_scales_pitch_radius_height_and_actual_owner_frames(int numerator,int denominator,int links)
    {
        var sdk=GearInvestSdk.CreateDefault();var s=SpatialExample.Create(links,new Rational(numerator,denominator),"variant-material",1);
        var artifact=sdk.FinalizeWindingConnection(s);using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"scaled-consumer",0);
        var result=run.Advance(Request(run.Current,"new-input",new Rational(-17,100),new Rational(2,11)));
        Assert.True(result.IsAccepted,result.Status);Assert.Equal(links+1,run.Current.Mechanical.Frame.SpatialWinding!.Pins.Count);
        Assert.InRange(run.Current.Mechanical.Frame.SpatialWinding.PitchResidualMm,0,1e-8);
    }

    [Fact]
    public void Authored_nonunit_ratio_mount_and_registration_propagate_without_using_port_offset_as_body_mount()
    {
        var sdk=GearInvestSdk.CreateDefault();var s=SpatialExample.Create(extraStages:1);var shaft=s.Suffix.OutputShaft;
        var old=s.Transmission!.Stages[0];var frame=old.OutputShaft.Frame.At(old.DriverGear.MountingFrame.Origin+new ExactVector3(30,0,0));
        var output=new OrientedShaft(old.OutputShaft.Id,frame);var reference=-new Rational(2,3)*s.Suffix.OutputReference.Value+new Rational(1,36);
        var stage=new FixedAxisSpurStage(shaft.Id,output,
            new(old.DriverGear.Id,shaft.Id,OrientedGearKind.PlanarSpur,old.DriverGear.MountingFrame,24,12,"nonunit-stage"),
            new(old.OutputGear.Id,output.Id,OrientedGearKind.PlanarSpur,frame,36,18,"nonunit-stage"),old.ContactId,
            new Rational(1,24),new Rational(-1,36),1,reference,
            new(old.OutputPort.Id,output.Id,OrientedFrame.Identity,ExactQuantity.Turns(new Rational(2,13))));
        var continuation=new ConnectedTransmission(new[]{stage},output.Id,"nonunit/carrier",frame,new Rational(1,9),
            new[]{new CarrierRigidAttachment("nonunit/carried",OrientedFrame.Identity.At(new ExactVector3(5,0,2)))});
        var source=new WindingDifferentialDefinition((SpatialWindingDefinition)s.WindingSource,s.Suffix,s.CouplingId,s.SunPortId,s.PlanetPortId,s.InitialCouplingOffset,transmission:continuation);
        var artifact=sdk.FinalizeWindingConnection(source);using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"nonunit-consumer",new Rational(3,10));
        Assert.True(run.Advance(Request(run.Current,"nonunit-input",new Rational(13,100),new Rational(-2,7))).IsAccepted);
        var f=run.Current.Mechanical.Frame;var value=f.Coordinates[output.Id];var parent=f.Coordinates[shaft.Id];
        Assert.Equal(Rational.Zero,value.Scale(36,-1).Plus(parent.Scale(24,0)).Exact);
        Assert.Equal(new Rational(2,13),f.Ports[stage.OutputPort.Id].Minus(value).Exact);
        var angle=2*Math.PI*(value.Estimate-1d/36);var m=f.MatricesMm[stage.OutputGear.Id];
        Assert.Equal(-ExactVector3.UnitY,frame.Y); // Actual suffix owner uses a negative world-Z axis.
        Assert.InRange(Math.Abs(m[0]-Math.Cos(angle)),0,1e-10);Assert.InRange(Math.Abs(m[1]+Math.Sin(angle)),0,1e-10);
        var carrier=2*Math.PI*(value.Estimate+1d/9);var carried=f.MatricesMm["nonunit/carried"];
        Assert.InRange(Math.Abs(carried[12]-((double)frame.Origin.X.Numerator/(double)frame.Origin.X.Denominator+5*Math.Cos(carrier))),0,1e-9);
        Assert.InRange(Math.Abs(carried[13]+5*Math.Sin(carrier)),0,1e-9);
    }
}
