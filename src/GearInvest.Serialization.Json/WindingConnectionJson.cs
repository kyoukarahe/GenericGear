using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public sealed class WindingConnectionArtifact
{
    private readonly byte[] bytes;
    internal WindingConnectionArtifact(WindingDifferentialDefinition source, WindingDifferentialAnalysis analysis, byte[] bytes)
    { Source = source; Analysis = analysis; this.bytes = (byte[])bytes.Clone(); ArtifactId = Hash(bytes); }
    public WindingDifferentialDefinition Source { get; }
    public WindingDifferentialAnalysis Analysis { get; }
    public string ArtifactId { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

public sealed class WindingRecordingArtifact
{
    private readonly byte[] bytes;
    internal WindingRecordingArtifact(WindingConnectionArtifact source, WindingDriveRecording recording, byte[] bytes)
    { Source = source; Recording = recording; this.bytes = (byte[])bytes.Clone(); RecordingId = Hash(bytes); }
    public WindingConnectionArtifact Source { get; }
    public WindingDriveRecording Recording { get; }
    public string RecordingId { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

/// <summary>New formats, not a reinterpretation of existing E2/assembly/hybrid formats.</summary>
public static class WindingConnectionJson
{
    public const string DraftFormat = "gear-invest.winding-connection-draft";
    public const string ArtifactFormat = "gear-invest.winding-connection-mechanism";
    public const string RecordingFormat = "gear-invest.winding-drive-recording";
    public const string ReplayFormat = "gear-invest.winding-connection-replay";
    public const int MaxBytes = 4 * 1024 * 1024;
    public static WindingConnectionArtifact ReadRuntimeSource(byte[] bytes) => SpatialWindingJson.IsFormat(bytes, SpatialWindingJson.ArtifactFormat) ? SpatialWindingJson.ReadArtifact(bytes) : ReadArtifact(bytes);
    public static byte[] WriteDraft(WindingDifferentialDefinition s) => Encode(w =>
    {
        Start(w,DraftFormat); w.WriteString("profile",WindingDifferentialDefinition.Profile); w.WriteString("definitionId",s.DefinitionId);
        w.WritePropertyName("winding"); Winding(w,s.Winding); w.WritePropertyName("suffix"); Suffix(w,s.Suffix);
        w.WriteString("couplingId",s.CouplingId); w.WriteString("sunPortId",s.SunPortId); w.WriteString("planetPortId",s.PlanetPortId);
        Fraction(w,"initialCouplingOffset",s.InitialCouplingOffset); MechanicalAuthoringJson.Strings(w,"requiredDomains",s.RequiredDomains); w.WriteEndObject();
    });
    public static WindingDifferentialDefinition ReadDraft(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p,DraftFormat);
        var s = new WindingDifferentialDefinition(Winding(p.GetProperty("winding")),Suffix(p.GetProperty("suffix")),S(p,"couplingId"),S(p,"sunPortId"),S(p,"planetPortId"),F(p.GetProperty("initialCouplingOffset")),Items(p,"requiredDomains",16).Select(x=>x.GetString()!));
        Require(bytes.SequenceEqual(WriteDraft(s)),"Noncanonical/unknown winding source fields or identity."); return s;
    });
    public static WindingConnectionArtifact WriteArtifact(WindingDifferentialDefinition s, DifferentialArtifact parent)
    {
        var a = WindingDifferentialEngine.Prepare(s); Require(a.IsValid,"Connected admission: " + string.Join(",",a.Diagnostics));
        Require(parent.Request.RequestId == s.Suffix.Parent.RequestId,"Parent source mismatch.");
        var bytes = Encode(w =>
        {
            Start(w,ArtifactFormat); w.WriteString("profile",WindingDifferentialDefinition.Profile); w.WriteString("definitionId",s.DefinitionId);
            w.WriteBase64String("sourceDraftUtf8",WriteDraft(s)); w.WriteBase64String("parentArtifactUtf8",parent.Bytes);
            w.WriteString("parentArtifactId",parent.ArtifactHash); w.WritePropertyName("outputLaw"); Law(w,a.Suffix.OutputLaw!);
            w.WriteString("validation","source-admission+exact-relations+separated-pitch-planes");
            w.WriteString("numericalPolicy",FiniteWindingGeometry.Policy); w.WriteString("unperformed","tooth-solids;swept-solids;dynamics"); w.WriteEndObject();
        });
        return new(s,a,bytes);
    }
    public static WindingConnectionArtifact ReadArtifact(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p,ArtifactFormat);
        var s = ReadDraft(Raw(p,"sourceDraftUtf8")); var parent = DifferentialJson.ReadArtifact(Raw(p,"parentArtifactUtf8")); var fresh = WriteArtifact(s,parent);
        Require(bytes.SequenceEqual(fresh.Bytes),"Connected source/parent/compiled law differs."); return fresh;
    });
    public static WindingRecordingArtifact WriteRecording(WindingConnectionArtifact source, WindingDriveRecording recording)
    {
        Require(source.Source.DefinitionId == recording.Analysis.Source.DefinitionId,"Foreign recording source.");
        var bytes = Encode(w =>
        {
            Start(w,RecordingFormat); w.WriteString("sourceArtifactId",source.ArtifactId); w.WriteBase64String("sourceArtifactUtf8",source.Bytes);
            Fraction(w,"initialPlanetPortTurns",recording.InitialPlanetPortTurns);
            Array(w,"requests",recording.Attempts,(x,a)=>Inputs(x,a.Requested));
            w.WritePropertyName("results"); Results(w,recording); w.WriteEndObject();
        });
        return new(source,recording,bytes);
    }
    private static void Inputs(Utf8JsonWriter w, IEnumerable<WindingDriveInput> values)
    { w.WriteStartArray(); foreach(var input in values) { w.WriteStartObject(); Fraction(w,"driverTurns",input.DriverTurns); Fraction(w,"planetPortTurns",input.PlanetPortTurns); w.WriteEndObject(); } w.WriteEndArray(); }
    public static WindingRecordingArtifact ReadRecording(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc=Parse(bytes); var p=doc.RootElement; Header(p,RecordingFormat);
        var source=ReadArtifact(Raw(p,"sourceArtifactUtf8")); Require(source.ArtifactId==S(p,"sourceArtifactId"),"Foreign source identity.");
        var requests=Items(p,"requests",16).Select(x=>
        { Require(x.ValueKind==JsonValueKind.Array && x.GetArrayLength()<=16,"Segment bound."); return x.EnumerateArray().Select(y=>new WindingDriveInput(F(y.GetProperty("driverTurns")),F(y.GetProperty("planetPortTurns")))).ToArray(); }).ToArray();
        var run=WindingDifferentialEngine.Record(source.Analysis,F(p.GetProperty("initialPlanetPortTurns")),requests);
        using var fresh=Parse(WriteRecording(source,run).Bytes);
        Compare(p,fresh.RootElement,"recording"); return new WindingRecordingArtifact(source,run,bytes);
    });
    public static byte[] WriteReplay(WindingRecordingArtifact artifact)
    {
        // Keep the recorded bytes and their results paired. A current-source rebuild may differ
        // within the declared binary64 tolerance; it must not silently relabel cached bytes.
        using var recorded = Parse(artifact.Bytes);
        var payload=Encode(w=>
        {
            w.WriteStartObject(); w.WriteString("profile",WindingDifferentialDefinition.Profile); w.WriteString("sourceId",artifact.Source.Source.DefinitionId);
            w.WriteString("sourceArtifactId",artifact.Source.ArtifactId); w.WriteString("recordingId",artifact.RecordingId); w.WriteBase64String("recordingUtf8",artifact.Bytes);
            w.WritePropertyName("scene"); Scene(w,artifact.Source);
            w.WritePropertyName("results"); recorded.RootElement.GetProperty("results").WriteTo(w); w.WriteEndObject();
        });
        return Encode(w=>{ Start(w,ReplayFormat); w.WriteString("replayId",Hash(payload)); w.WriteBase64String("payloadUtf8",payload); w.WriteEndObject(); });
    }
    public static WindingRecordingArtifact ReadReplay(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc=Parse(bytes); var p=doc.RootElement; Header(p,ReplayFormat); var raw=Raw(p,"payloadUtf8"); Require(Hash(raw)==S(p,"replayId"),"Replay digest differs.");
        Require(bytes.SequenceEqual(Encode(w=>{Start(w,ReplayFormat);w.WriteString("replayId",Hash(raw));w.WriteBase64String("payloadUtf8",raw);w.WriteEndObject();})),"Unknown/noncanonical replay envelope.");
        using var payload=Parse(raw); var artifact=ReadRecording(Raw(payload.RootElement,"recordingUtf8"));
        using var fresh=Parse(WriteReplay(artifact)); using var expected=Parse(Raw(fresh.RootElement,"payloadUtf8"));
        Compare(payload.RootElement,expected.RootElement,"replay"); return artifact;
    });
    internal static void Results(Utf8JsonWriter w,WindingDriveRecording r)
    {
        w.WriteStartObject(); w.WritePropertyName("initial"); Frame(w,r.Initial,r.Analysis.Source.Winding);
        Array(w,"attempts",r.Attempts,(x,a)=>
        {
            x.WriteStartObject(); x.WriteString("status",a.Status); Integer(x,"appliedSegments",a.AppliedSegments);
            x.WriteString("lastValidSnapshotId",a.LastValidSnapshot.SnapshotId); x.WritePropertyName("remainder"); Inputs(x,a.Remainder);
            Array(x,"frames",a.Frames,(y,f)=>Frame(y,f,r.Analysis.Source.Winding)); x.WriteEndObject();
        }); w.WriteString("finalSnapshotId",r.Final.SnapshotId); w.WriteEndObject();
    }
    internal static void Scene(Utf8JsonWriter w,WindingConnectionArtifact artifact)
    {
        if (artifact.Source.WindingSource is SpatialWindingDefinition) { SpatialWindingJson.Scene(w, artifact); return; }
        var s=artifact.Source;var g=s.Winding.Geometry;w.WriteStartArray();
        void Node(string id,string owner,string kind,double radius,IEnumerable<WindingPoint>? points=null)
        {w.WriteStartObject();w.WriteString("id",id);w.WriteString("owner",owner);w.WriteString("kind",kind);w.WriteNumber("radiusMm",radius);Array(w,"pointsMm",points??System.Array.Empty<WindingPoint>(),Point);w.WriteEndObject();}
        double D(Rational r)=>(double)r.Numerator/(double)r.Denominator;
        foreach(var n in artifact.Analysis.Suffix.Parent.PoseNodes.Where(n=>n.BodyId is not null))
            if(n.PitchRadiusMm.HasValue)Node(n.Id,n.BodyId!,"pitch-circle",D(n.PitchRadiusMm.Value));
            else Node(n.Id,n.BodyId!,"polyline",0,new[]{new WindingPoint(0,0),new WindingPoint(D(s.Suffix.Parent.Definition.PlanetShaft.FrameInCarrier.Origin.X),D(s.Suffix.Parent.Definition.PlanetShaft.FrameInCarrier.Origin.Y))});
        Node(ConnectedPoseIds.DriverGear,s.Suffix.DriverGear.Id,"pitch-circle",D(s.Suffix.DriverGear.OuterPitchRadius));
        Node(ConnectedPoseIds.OutputGear,s.Suffix.OutputGear.Id,"pitch-circle",D(s.Suffix.OutputGear.OuterPitchRadius));
        Node(ConnectedPoseIds.WindingDriver,s.Winding.DriverBodyId,"polyline",0,g.DriverSeats.Concat(g.DriverSeats.Take(1)));
        Node(ConnectedPoseIds.WindingOutput,s.Winding.OutputBodyId,"polyline",0,g.OutputSeats.Concat(g.OutputSeats.Take(1)));
        for(var i=0;i<g.LinkCount;i++)Node(ConnectedPoseIds.Link(i),s.Winding.LinkId(i),"polyline",0,new[]{new WindingPoint(0,0),new WindingPoint(g.PitchMm,0)});
        for(var i=0;i<=g.LinkCount;i++)Node(ConnectedPoseIds.Pin(i),s.Winding.PinId(i),"pin",g.PitchMm*.08);
        w.WriteEndArray();
    }
    internal static void Frame(Utf8JsonWriter w,ConnectedFrame f,FiniteWindingDefinition source)
    {
        w.WriteStartObject(); w.WriteString("definitionId",f.DefinitionId); w.WriteString("snapshotId",f.SnapshotId); Fraction(w,"driverTurns",f.DriverTurns);
        Array(w,"coordinates",f.Coordinates,(x,p)=>{ x.WriteStartObject(); x.WriteString("shaftId",p.Key); x.WritePropertyName("value"); Value(x,p.Value); x.WriteEndObject(); });
        Array(w,"ports",f.Ports,(x,p)=>{ x.WriteStartObject(); x.WriteString("portId",p.Key); x.WritePropertyName("value"); Value(x,p.Value); x.WriteEndObject(); });
        var g=f.Winding; w.WriteStartObject("winding"); Integer(w,"driverContact",g.DriverContact); Integer(w,"outputContact",g.OutputContact);
        w.WriteNumber("outputEstimateTurns",g.OutputTurns); w.WriteString("quality",g.Quality); w.WriteNull("solutionErrorBoundTurns");
        w.WriteNumber("pitchResidualMm",g.PitchResidualMm); w.WriteNumber("totalLengthResidualMm",g.TotalLengthResidualMm); w.WriteNumber("attachmentResidualMm",g.AttachmentResidualMm);
        w.WriteNumber("attachmentDirectionResidualMm",g.AttachmentDirectionResidualMm); w.WriteNumber("guidePinResidualMm",g.GuidePinResidualMm); w.WriteNumber("planarityResidualMm",g.PlanarityResidualMm);
        w.WriteNumber("supportResidualMm",g.SupportResidualMm); w.WriteNumber("maximumBendDegrees",g.MaximumBendDegrees);
        Array(w,"pins",g.Pins.Select((p,i)=>(p,i)),(x,v)=>{x.WriteStartObject();x.WriteString("id",source.PinId(v.i));Point(x,"positionMm",v.p);x.WriteEndObject();});
        w.WriteEndObject(); w.WriteString("displayUnavailableReason",f.DisplayUnavailableReason);
        Array(w,"matricesMm",f.MatricesMm,(x,p)=>{ x.WriteStartObject(); x.WriteString("id",p.Key); x.WriteStartArray("matrix");foreach(var n in p.Value)x.WriteNumberValue(n);x.WriteEndArray();x.WriteEndObject(); });w.WriteEndObject();
    }
    internal static void Frame(Utf8JsonWriter w, ConnectedFrame f, ConnectedWindingSource source)
    {
        if (source is FiniteWindingDefinition planar) Frame(w, f, planar);
        else SpatialWindingJson.WriteFrame(w, f, (SpatialWindingDefinition)source);
    }
    internal static void Value(Utf8JsonWriter w,ConnectedMotionValue value)
    {
        w.WriteStartObject(); w.WriteString("kind",value.Kind); w.WriteString("unit","turn"); w.WriteString("accumulation","unwrapped");
        OptionalFraction(w,"exact",value.Exact); if(value.IsExact)w.WriteNull("estimate");else w.WriteNumber("estimate",value.Estimate);
        w.WriteNull("solutionErrorBoundTurns"); w.WriteString("precision",value.IsExact?"exact-rational":"binary64");
        Fraction(w,"constant",value.Constant); Array(w,"terms",value.Terms,(x,t)=>{x.WriteStartObject();x.WriteString("latentId",t.Source.Id);x.WriteNumber("estimate",t.Source.Estimate);Fraction(x,"coefficient",t.Coefficient);x.WriteEndObject();});w.WriteEndObject();
    }
    internal static void Law(Utf8JsonWriter w,DifferentialLaw law)
    {w.WriteStartObject();w.WriteStartObject("coefficients");foreach(var p in law.Coefficients)Fraction(w,p.Key,p.Value);w.WriteEndObject();Fraction(w,"offset",law.Offset);w.WriteEndObject();}

    internal static void Winding(Utf8JsonWriter w,FiniteWindingDefinition s)
    {
        w.WriteStartObject();w.WriteString("definitionId",s.DefinitionId);w.WritePropertyName("driverShaft");MechanicalAuthoringJson.WriteShaft(w,s.DriverShaft);
        w.WritePropertyName("outputShaft");MechanicalAuthoringJson.WriteShaft(w,s.OutputShaft);w.WriteString("driverBodyId",s.DriverBodyId);w.WriteString("outputBodyId",s.OutputBodyId);w.WriteString("chainId",s.ChainId);
        Fraction(w,"initialDriverTurns",s.InitialDriverTurns);Integer(w,"initialDriverContact",s.InitialDriverContact);Integer(w,"initialOutputContact",s.InitialOutputContact);
        var g=s.Geometry;w.WriteStartObject("geometry");Point(w,"driverCenterMm",g.DriverCenter);Point(w,"outputCenterMm",g.OutputCenter);w.WriteNumber("planeZMm",g.PlaneZMm);
        Array(w,"driverSeats",g.DriverSeats,Point);Array(w,"outputSeats",g.OutputSeats,Point);Integer(w,"linkCount",g.LinkCount);w.WriteNumber("pitchMm",g.PitchMm);
        Integer(w,"maxDriverContact",g.MaxDriverContact);Integer(w,"maxOutputContact",g.MaxOutputContact);w.WriteNumber("maxBendDegrees",g.MaxBendDegrees);
        w.WriteNumber("driverMinimumTurns",g.DriverMinimumTurns);w.WriteNumber("driverMaximumTurns",g.DriverMaximumTurns);w.WriteNumber("outputMinimumTurns",g.OutputMinimumTurns);w.WriteNumber("outputMaximumTurns",g.OutputMaximumTurns);
        w.WriteEndObject();w.WriteEndObject();
    }
    internal static FiniteWindingDefinition Winding(JsonElement p)
    {
        var g=p.GetProperty("geometry");var geometry=new FiniteWindingGeometry(Point(g.GetProperty("driverCenterMm")),Point(g.GetProperty("outputCenterMm")),D(g,"planeZMm"),Items(g,"driverSeats",12).Select(Point),Items(g,"outputSeats",12).Select(Point),I(g,"linkCount"),D(g,"pitchMm"),I(g,"maxDriverContact"),I(g,"maxOutputContact"),D(g,"maxBendDegrees"),D(g,"driverMinimumTurns"),D(g,"driverMaximumTurns"),D(g,"outputMinimumTurns"),D(g,"outputMaximumTurns"));
        return new(MechanicalAuthoringJson.ReadShaft(p.GetProperty("driverShaft")),MechanicalAuthoringJson.ReadShaft(p.GetProperty("outputShaft")),S(p,"driverBodyId"),S(p,"outputBodyId"),S(p,"chainId"),geometry,F(p.GetProperty("initialDriverTurns")),I(p,"initialDriverContact"),I(p,"initialOutputContact"));
    }
    internal static void Suffix(Utf8JsonWriter w,DifferentialSuffixDefinition s)
    {
        w.WriteStartObject();w.WriteString("definitionId",s.DefinitionId);w.WriteBase64String("parentDraftUtf8",DifferentialJson.WriteDraft(s.Parent));w.WriteString("sourceShaftId",s.SourceShaftId);
        w.WritePropertyName("outputShaft");MechanicalAuthoringJson.WriteShaft(w,s.OutputShaft);w.WritePropertyName("driverGear");MechanicalAuthoringJson.Body(w,s.DriverGear);w.WritePropertyName("outputGear");MechanicalAuthoringJson.Body(w,s.OutputGear);
        w.WriteString("contactId",s.ContactId);RotaryLinearJson.Quantity(w,"driverMount",s.DriverMount);RotaryLinearJson.Quantity(w,"outputMount",s.OutputMount);Fraction(w,"toothRegistration",s.ToothRegistration);RotaryLinearJson.Quantity(w,"outputReference",s.OutputReference);
        w.WriteStartObject("outputPort");w.WriteString("id",s.OutputPort.Id);w.WriteString("shaftId",s.OutputPort.ShaftId);CanonicalOrientedJson.Frame(w,"frameInShaft",s.OutputPort.FrameInShaft);RotaryLinearJson.Quantity(w,"readoutOffset",s.OutputPort.ReadoutOffset);w.WriteEndObject();w.WriteEndObject();
    }
    internal static DifferentialSuffixDefinition Suffix(JsonElement p)
    {
        var port=p.GetProperty("outputPort");return new(DifferentialJson.ReadDraft(Raw(p,"parentDraftUtf8")),S(p,"sourceShaftId"),MechanicalAuthoringJson.ReadShaft(p.GetProperty("outputShaft")),MechanicalAuthoringJson.Body(p.GetProperty("driverGear")),MechanicalAuthoringJson.Body(p.GetProperty("outputGear")),S(p,"contactId"),RotaryLinearJson.Quantity(p.GetProperty("driverMount")),RotaryLinearJson.Quantity(p.GetProperty("outputMount")),F(p.GetProperty("toothRegistration")),RotaryLinearJson.Quantity(p.GetProperty("outputReference")),new(S(port,"id"),S(port,"shaftId"),MechanicalAuthoringJson.LooseFrame(port.GetProperty("frameInShaft")),RotaryLinearJson.Quantity(port.GetProperty("readoutOffset"))));
    }
    private static void Point(Utf8JsonWriter w,string key,WindingPoint p){w.WritePropertyName(key);Point(w,p);}
    private static void Point(Utf8JsonWriter w,WindingPoint p){w.WriteStartArray();w.WriteNumberValue(p.X);w.WriteNumberValue(p.Y);w.WriteEndArray();}
    private static WindingPoint Point(JsonElement p){Require(p.GetArrayLength()==2,"Two planar coordinates required.");return new(p[0].GetDouble(),p[1].GetDouble());}
    private static double D(JsonElement p,string key){var n=p.GetProperty(key).GetDouble();Require(double.IsFinite(n),"Finite binary64 required.");return n;}
    internal static byte[] Raw(JsonElement p,string key){var s=p.GetProperty(key);Require(s.GetString()!.Length<=MaxBytes,"Encoded data bound.");return s.GetBytesFromBase64();}
    internal static void Start(Utf8JsonWriter w,string format)=>MechanicalAuthoringJson.Start(w,format,"0.1");
    internal static byte[] Encode(Action<Utf8JsonWriter> write)=>MechanicalAuthoringJson.Encode(write);
    internal static JsonDocument Parse(byte[] bytes){Require(bytes.Length<=MaxBytes,"Connected document bound.");return MechanicalAuthoringJson.Parse(bytes);}
    internal static void Compare(JsonElement actual,JsonElement expected,string path)
    {
        Require(actual.ValueKind==expected.ValueKind,"Kind differs at "+path);
        if(expected.ValueKind==JsonValueKind.Object)
        {
            var aa=actual.EnumerateObject().ToArray();var ee=expected.EnumerateObject().ToArray();Require(aa.Select(p=>p.Name).SequenceEqual(ee.Select(p=>p.Name)),"Fields/order differ at "+path);
            for(var i=0;i<ee.Length;i++)Compare(aa[i].Value,ee[i].Value,path+"/"+ee[i].Name);
        }
        else if(expected.ValueKind==JsonValueKind.Array)
        {Require(actual.GetArrayLength()==expected.GetArrayLength(),"Count differs at "+path);for(var i=0;i<expected.GetArrayLength();i++)Compare(actual[i],expected[i],path+"/"+i);}
        else if(expected.ValueKind==JsonValueKind.Number)
        {var a=actual.GetDouble();var e=expected.GetDouble();var tolerance=path.Contains("matricesMm")?1e-8:1e-10;Require(double.IsFinite(a)&&Math.Abs(a-e)<=tolerance,"Rebuilt numerical value differs at "+path);}
        else Require(actual.GetRawText()==expected.GetRawText(),"Exact/status/identity differs at "+path);
    }
}
