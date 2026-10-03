using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using static GearInvest.Serialization.Json.WindingConnectionJson;

namespace GearInvest.Serialization.Json;

public sealed class MechanicalModeRecordingArtifact
{
    private readonly byte[] bytes;
    internal MechanicalModeRecordingArtifact(WindingConnectionArtifact source,MechanicalModeRecording recording,byte[] bytes)
    {Source=source;Recording=recording;this.bytes=(byte[])bytes.Clone();RecordingId=Hash(bytes);}
    public WindingConnectionArtifact Source{get;}
    public MechanicalModeRecording Recording{get;}
    public string RecordingId{get;}
    public byte[] Bytes=>(byte[])bytes.Clone();
}

public static class MechanicalModesJson
{
    public const string RecordingFormat="gear-invest.mechanical-mode-recording";
    public const string ReplayFormat="gear-invest.mechanical-mode-replay";
    public static MechanicalModeRecordingArtifact Write(WindingConnectionArtifact source,MechanicalModeRecording run)
    {
        Require(run.Definition.Connection.DefinitionId==source.Source.DefinitionId,"Foreign mode source.");
        var bytes=Encode(w=>
        {
            Start(w,RecordingFormat);w.WriteString("profile",MechanicalModeDefinition.Profile);w.WriteString("sourceArtifactId",source.ArtifactId);w.WriteBase64String("sourceArtifactUtf8",source.Bytes);
            w.WriteStartObject("policy");w.WriteString("definitionId",run.Definition.DefinitionId);MechanicalAuthoringJson.Strings(w,"allowedModes",run.Definition.AllowedModes.Select(x=>x.ToString()));Fraction(w,"alignmentOffset",run.Definition.AlignmentOffset);w.WriteEndObject();
            Fraction(w,"initialPlanetPortTurns",run.InitialPlanetPortTurns);Array(w,"requests",run.Attempts,(x,a)=>Request(x,a.Request));w.WritePropertyName("results");Results(w,run);w.WriteEndObject();
        });return new(source,run,bytes);
    }
    public static MechanicalModeRecordingArtifact Read(byte[] bytes)=>MechanicalAuthoringJson.Guard(()=>
    {
        using var doc=Parse(bytes);var p=doc.RootElement;Header(p,RecordingFormat);Require(S(p,"profile")==MechanicalModeDefinition.Profile,"Unsupported mode profile.");
        var source=ReadArtifact(Raw(p,"sourceArtifactUtf8"));Require(source.ArtifactId==S(p,"sourceArtifactId"),"Foreign mode source.");var policy=p.GetProperty("policy");
        var d=new MechanicalModeDefinition(source.Source,Items(policy,"allowedModes",5).Select(x=>Enum.Parse<MechanicalConnectionMode>(x.GetString()!)),F(policy.GetProperty("alignmentOffset")));
        Require(d.DefinitionId==S(policy,"definitionId"),"Mode policy identity differs.");
        var run=MechanicalModeRecording.Rebuild(d,F(p.GetProperty("initialPlanetPortTurns")),Items(p,"requests",32).Select(Request));
        using var fresh=Parse(Write(source,run).Bytes);Compare(p,fresh.RootElement,"mode-recording");return new MechanicalModeRecordingArtifact(source,run,bytes);
    });
    public static byte[] WriteReplay(MechanicalModeRecordingArtifact artifact)
    {
        using var recorded=Parse(artifact.Bytes);
        var payload=Encode(w=>
        {
            w.WriteStartObject();w.WriteString("profile",MechanicalModeDefinition.Profile);w.WriteString("connectionId",artifact.Source.Source.DefinitionId);w.WriteString("definitionId",artifact.Recording.Definition.DefinitionId);
            w.WriteString("sourceArtifactId",artifact.Source.ArtifactId);w.WriteString("recordingId",artifact.RecordingId);w.WriteBase64String("recordingUtf8",artifact.Bytes);
            w.WritePropertyName("scene");Scene(w,artifact.Source);w.WritePropertyName("results");recorded.RootElement.GetProperty("results").WriteTo(w);w.WriteEndObject();
        });return Encode(w=>{Start(w,ReplayFormat);w.WriteString("replayId",Hash(payload));w.WriteBase64String("payloadUtf8",payload);w.WriteEndObject();});
    }
    public static MechanicalModeRecordingArtifact ReadReplay(byte[] bytes)=>MechanicalAuthoringJson.Guard(()=>
    {
        using var doc=Parse(bytes);var p=doc.RootElement;Header(p,ReplayFormat);var raw=Raw(p,"payloadUtf8");Require(Hash(raw)==S(p,"replayId"),"Mode replay digest differs.");
        Require(bytes.SequenceEqual(Encode(w=>{Start(w,ReplayFormat);w.WriteString("replayId",Hash(raw));w.WriteBase64String("payloadUtf8",raw);w.WriteEndObject();})),"Unknown/noncanonical mode envelope.");
        using var payload=Parse(raw);var artifact=Read(Raw(payload.RootElement,"recordingUtf8"));using var fresh=Parse(WriteReplay(artifact));using var expected=Parse(Raw(fresh.RootElement,"payloadUtf8"));
        Compare(payload.RootElement,expected.RootElement,"mode-replay");return artifact;
    });
    private static void Request(Utf8JsonWriter w,MechanicalModeRequest r)
    {
        w.WriteStartObject();w.WriteString("id",r.Id);w.WriteString("sourceId",r.SourceId);w.WriteString("expectedStateId",r.ExpectedStateId);MechanicalAuthoringJson.Long(w,"expectedRevision",r.ExpectedRevision);w.WriteString("payloadId",r.PayloadId);
        Array(w,"segments",r.Segments,(x,s)=>
        {
            x.WriteStartObject();Fraction(x,"driverTurns",s.Input.DriverTurns);x.WriteStartObject("independentPorts");foreach(var p in s.Input.IndependentPorts)Fraction(x,p.Key,p.Value);x.WriteEndObject();
            x.WriteStartObject("observations");foreach(var p in s.Input.Observations)Fraction(x,p.Key,p.Value);x.WriteEndObject();
            Array(x,"events",s.Events,(y,e)=>{y.WriteStartObject();y.WriteString("id",e.Id);MechanicalAuthoringJson.Long(y,"sequence",e.Sequence);Integer(y,"ordinal",e.Ordinal);y.WriteString("kind",e.Kind.ToString());y.WriteEndObject();});x.WriteEndObject();
        });w.WriteEndObject();
    }
    private static MechanicalModeRequest Request(JsonElement p)=>new(S(p,"id"),S(p,"sourceId"),S(p,"expectedStateId"),MechanicalAuthoringJson.Long(p,"expectedRevision"),Items(p,"segments",16).Select(s=>
        new MechanicalModeSegment(new(F(s.GetProperty("driverTurns")),s.GetProperty("independentPorts").EnumerateObject().Select(v=>new System.Collections.Generic.KeyValuePair<string,GearInvest.Core.Rational>(v.Name,F(v.Value))),
            s.GetProperty("observations").EnumerateObject().Select(v=>new System.Collections.Generic.KeyValuePair<string,GearInvest.Core.Rational>(v.Name,F(v.Value)))),
            Items(s,"events",16).Select(e=>new MechanicalConnectionEvent(S(e,"id"),MechanicalAuthoringJson.Long(e,"sequence"),I(e,"ordinal"),E<MechanicalConnectionEventKind>(e,"kind"))))));
    private static void Results(Utf8JsonWriter w,MechanicalModeRecording run)
    {
        w.WriteStartObject();w.WritePropertyName("initial");State(w,run.Initial,run.Definition);
        Array(w,"attempts",run.Attempts,(x,a)=>
        {
            x.WriteStartObject();x.WriteString("status",a.Status);Integer(x,"appliedSegments",a.AppliedSegments);x.WriteString("lastValidStateId",a.LastValidSnapshot.StateId);x.WriteString("replayedStateId",a.ReplayedStateId);
            x.WritePropertyName("state");if(a.State is null)x.WriteNullValue();else State(x,a.State,run.Definition);
            Array(x,"samples",a.Samples,(y,s)=>State(y,s,run.Definition));Integer(x,"remainingSegments",a.Remainder.Count);x.WriteEndObject();
        });w.WriteString("finalStateId",run.Final.StateId);w.WriteEndObject();
    }
    private static void State(Utf8JsonWriter w,MechanicalModeState s,MechanicalModeDefinition definition)
    {
        w.WriteStartObject();w.WriteString("definitionId",s.DefinitionId);w.WriteString("stateId",s.StateId);w.WriteString("historyId",s.HistoryId);w.WriteString("mode",s.Mode.ToString());
        MechanicalAuthoringJson.Long(w,"revision",s.Revision);MechanicalAuthoringJson.Long(w,"eventCursor",s.EventCursor);w.WriteNumber("allowedDirection",s.AllowedDirection);
        MechanicalAuthoringJson.Strings(w,"requiredInputPorts",MechanicalModeEngine.RequiredInputPorts(definition,s.Mode));
        w.WritePropertyName("couplingOffset");Value(w,s.CouplingOffset);w.WritePropertyName("lockReference");if(s.LockReference is null)w.WriteNullValue();else Value(w,s.LockReference);
        Array(w,"ledger",s.Ledger,(x,e)=>{x.WriteStartObject();x.WriteString("requestId",e.RequestId);x.WriteString("payloadId",e.PayloadId);x.WriteString("resultStateId",e.ResultStateId);MechanicalAuthoringJson.Strings(x,"eventIds",e.EventIds);x.WriteEndObject();});
        w.WritePropertyName("frame");Frame(w,s.Frame,definition.Connection.Winding);w.WriteEndObject();
    }
}
