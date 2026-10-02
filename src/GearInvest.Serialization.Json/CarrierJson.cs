using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public sealed class CarrierArtifact
{
    private readonly byte[] bytes;
    internal CarrierArtifact(CarrierDefinition request, CarrierAnalysis analysis, string hash, byte[] bytes)
    { Request = request; Analysis = analysis; ArtifactHash = hash; this.bytes = (byte[])bytes.Clone(); }
    public CarrierDefinition Request { get; }
    public CarrierAnalysis Analysis { get; }
    public string ArtifactHash { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

/// <summary>Strict canonical source codec. Use the facade to also re-finalize original source provenance.</summary>
public static class CarrierJson
{
    public const string DraftFormat = "gear-invest.carrier-draft";
    public const string ArtifactFormat = "gear-invest.carrier-mechanism";
    public const string ReplayFormat = "gear-invest.carrier-replay";
    public const string Version = "0.1";
    public static byte[] WriteDraft(CarrierDefinition d) => Encode(w =>
    { Start(w, DraftFormat); w.WritePropertyName("request"); Request(w, d); w.WriteEndObject(); });
    public static CarrierDefinition ReadDraft(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, DraftFormat); var d = Request(doc.RootElement.GetProperty("request"));
        Require(bytes.SequenceEqual(WriteDraft(d)), "Noncanonical or unknown carrier draft fields."); return d;
    });
    public static CarrierArtifact WriteArtifact(CarrierDefinition d, byte[]? sourceBytes, string? sourceIdentity)
    {
        var a = CarrierAnalyzer.Analyze(d); Require(a.IsValid, "Carrier mechanics/required validation did not pass.");
        if (sourceBytes is not null) Require(sourceBytes.Length <= 4 * 1024 * 1024, "Carrier attached source byte limit exceeded.");
        var hash = Hash(Document(d, a, sourceBytes, sourceIdentity, null));
        return new(d, a, hash, Document(d, a, sourceBytes, sourceIdentity, hash));
    }
    public static CarrierArtifact ReadArtifact(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, ArtifactFormat);
        var request = Request(p.GetProperty("request")); var source = p.GetProperty("attachedSource");
        var raw = source.GetProperty("artifactUtf8");
        if (raw.ValueKind != JsonValueKind.Null) Require(raw.GetString()!.Length <= 4 * ((4 * 1024 * 1024 + 2) / 3), "Carrier attached source byte limit exceeded.");
        var fresh = WriteArtifact(request, raw.ValueKind == JsonValueKind.Null ? null : raw.GetBytesFromBase64(), MechanicalAuthoringJson.NullableString(source, "identity"));
        Require(bytes.SequenceEqual(fresh.Bytes), "Fresh carrier request/law/frame/owner/identity differs from canonical artifact."); return fresh;
    });
    private static byte[] Document(CarrierDefinition d, CarrierAnalysis a, byte[]? source, string? sourceIdentity, string? hash) => Encode(w =>
    {
        Start(w, ArtifactFormat); w.WriteString("profile", CarrierProfile.Id); w.WriteString("definitionId", d.DefinitionId);
        if (hash is not null) w.WriteString("artifactHash", hash);
        w.WritePropertyName("request"); Request(w, d);
        w.WriteStartObject("attachedSource"); w.WriteString("identity", sourceIdentity);
        if (source is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", source);
        w.WriteEndObject(); w.WritePropertyName("compiled"); Compiled(w, a); w.WriteEndObject();
    });

    public static byte[] WriteReplay(CarrierArtifact artifact)
    {
        var payload = Encode(w =>
        {
            w.WriteStartObject(); w.WriteString("profile", CarrierProfile.Id); w.WriteString("artifactId", artifact.ArtifactHash);
            w.WriteString("definitionId", artifact.Request.DefinitionId); w.WriteString("sourceRawSha256", Hash(artifact.Bytes));
            w.WriteBase64String("sourceArtifactUtf8", artifact.Bytes); w.WritePropertyName("compiled"); Compiled(w, artifact.Analysis); w.WriteEndObject();
        });
        return Encode(w => { Start(w, ReplayFormat); w.WriteString("replayId", Hash(payload)); w.WriteBase64String("payloadUtf8", payload); w.WriteEndObject(); });
    }
    public static CarrierArtifact ReadReplaySource(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, ReplayFormat);
        var encoded = doc.RootElement.GetProperty("payloadUtf8"); Require(encoded.GetString()!.Length <= CarrierProfile.MaxDocumentBytes, "Carrier payload byte bound exceeded.");
        using var payload = Parse(encoded.GetBytesFromBase64());
        var source = payload.RootElement.GetProperty("sourceArtifactUtf8"); Require(source.GetString()!.Length <= CarrierProfile.MaxDocumentBytes, "Carrier source byte bound exceeded.");
        var artifact = ReadArtifact(source.GetBytesFromBase64());
        Require(bytes.SequenceEqual(WriteReplay(artifact)), "Carrier replay does not reproduce current source/compiled content."); return artifact;
    });

    public static byte[] WriteAnalysis(CarrierAnalysis a) => Encode(w => Compiled(w, a));
    private static void Compiled(Utf8JsonWriter w, CarrierAnalysis a)
    {
        w.WriteStartObject(); w.WriteString("policy", CarrierProfile.Policy); w.WriteString("rootShaftId", a.Request.Source.Definition.RootShaftId);
        w.WriteString("carrierShaftId", a.Request.CarrierShaftId); w.WriteString("sunShaftId", a.Request.SunShaft.Id); w.WriteString("planetShaftId", a.Request.PlanetShaft.Id);
        w.WriteString("portId", a.Request.OutputPort.Id); w.WriteBoolean("isValid", a.IsValid);
        Array(w, "checks", a.Checks, (x, c) => { x.WriteStartObject(); x.WriteString("domain", c.Domain); x.WriteString("verdict", c.Verdict.ToString()); x.WriteBoolean("required", c.Required); x.WriteString("detail", c.Detail); x.WriteEndObject(); });
        Array(w, "diagnostics", a.Diagnostics, (x, c) => { x.WriteStartObject(); x.WriteString("code", c.Code); x.WriteString("stage", c.Stage); x.WriteString("detail", c.Detail); x.WriteEndObject(); });
        Law(w, "carrierCommon", a.CarrierCommon); Law(w, "planetCommon", a.PlanetCommon); Law(w, "portReadout", a.PortReadout);
        Array(w, "shafts", a.Shafts, (x, s) => { x.WriteStartObject(); x.WriteString("id", s.ShaftId); x.WriteString("motion", s.Motion); x.WriteString("poseNodeId", s.PoseNodeId);
            Law(x, "world", s.World); Law(x, "carrierRelative", s.CarrierRelative); x.WriteEndObject(); });
        Array(w, "poseNodes", a.PoseNodes, (x, n) =>
        {
            x.WriteStartObject(); x.WriteString("id", n.Id); x.WriteString("parentId", n.ParentId); DerivedFrame(x, n.Frame); Law(x, "rotation", n.Rotation);
            x.WriteString("shaftId", n.ShaftId); x.WriteString("bodyId", n.BodyId);
            if (n.Teeth.HasValue) Integer(x, "teeth", n.Teeth.Value); else x.WriteNull("teeth");
            if (n.PitchRadiusMm.HasValue) { x.WritePropertyName("pitchRadiusMm"); Derived(x, n.PitchRadiusMm.Value); } else x.WriteNull("pitchRadiusMm"); x.WriteEndObject();
        });
        w.WriteEndObject();
    }
    private static void Law(Utf8JsonWriter w, string key, ExactAffineRelation? law)
    {
        if (!law.HasValue) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WritePropertyName("q"); Derived(w, law.Value.Coefficient); w.WritePropertyName("p"); Derived(w, law.Value.Phase); w.WriteEndObject();
    }
    private static void Derived(Utf8JsonWriter w, Rational r)
    {
        var n = r.Numerator.ToString(CultureInfo.InvariantCulture); var d = r.Denominator.ToString(CultureInfo.InvariantCulture);
        Require(n.Length <= CarrierProfile.MaxDerivedDigits && d.Length <= CarrierProfile.MaxDerivedDigits, "Carrier derived rational resource bound exceeded.");
        w.WriteStartObject(); w.WriteString("numerator", n); w.WriteString("denominator", d); w.WriteEndObject();
    }
    private static void DerivedFrame(Utf8JsonWriter w, OrientedFrame frame)
    {
        void V(string key, ExactVector3 v) { w.WriteStartArray(key); Derived(w, v.X); Derived(w, v.Y); Derived(w, v.Z); w.WriteEndArray(); }
        w.WriteStartObject("frame"); V("origin", frame.Origin); V("x", frame.X); V("y", frame.Y); V("z", frame.Z); w.WriteEndObject();
    }
    private static void Request(Utf8JsonWriter w, CarrierDefinition d)
    {
        w.WriteStartObject(); w.WriteString("definitionId", d.DefinitionId); w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); RotaryLinearJson.Mapping(w, d.SourceMapping); w.WriteString("carrierShaftId", d.CarrierShaftId);
        Frame(w, "planeMm", d.PlaneMm); w.WritePropertyName("sunShaft"); MechanicalAuthoringJson.WriteShaft(w, d.SunShaft);
        w.WriteStartObject("planetShaft"); w.WriteString("id", d.PlanetShaft.Id); w.WriteString("carrierShaftId", d.PlanetShaft.CarrierShaftId);
        Frame(w, "frameInCarrier", d.PlanetShaft.FrameInCarrier); w.WriteBoolean("isPrescribed", d.PlanetShaft.IsPrescribed); w.WriteEndObject();
        Integer(w, "sunTeeth", d.SunTeeth); Integer(w, "planetTeeth", d.PlanetTeeth); RotaryLinearJson.Quantity(w, "module", d.Module);
        RotaryLinearJson.Quantity(w, "carrierReference", d.CarrierReference); RotaryLinearJson.Quantity(w, "sunReference", d.SunReference);
        RotaryLinearJson.Quantity(w, "planetReference", d.PlanetReference); RotaryLinearJson.Quantity(w, "sunMount", d.SunMount); RotaryLinearJson.Quantity(w, "planetMount", d.PlanetMount);
        Fraction(w, "toothRegistration", d.ToothRegistration); w.WriteStartObject("outputPort"); w.WriteString("id", d.OutputPort.Id); w.WriteString("shaftId", d.OutputPort.ShaftId);
        Frame(w, "frameInShaft", d.OutputPort.FrameInShaft); RotaryLinearJson.Quantity(w, "readoutOffset", d.OutputPort.ReadoutOffset); w.WriteEndObject();
        w.WriteString("carrierBodyId", d.CarrierBodyId); w.WriteString("sunBodyId", d.SunBodyId); w.WriteString("planetBodyId", d.PlanetBodyId);
        w.WriteBoolean("sunHeld", d.SunHeld); w.WriteBoolean("contactPresent", d.ContactPresent); MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static CarrierDefinition Request(JsonElement p)
    {
        var planet = p.GetProperty("planetShaft"); var port = p.GetProperty("outputPort");
        var d = new CarrierDefinition(MechanicalAuthoringJson.Draft(p.GetProperty("source")), RotaryLinearJson.Mapping(p.GetProperty("sourceMapping"))!, S(p, "carrierShaftId"),
            MechanicalAuthoringJson.LooseFrame(p.GetProperty("planeMm")), MechanicalAuthoringJson.ReadShaft(p.GetProperty("sunShaft")),
            new CarrierLocalShaft(S(planet, "id"), S(planet, "carrierShaftId"), MechanicalAuthoringJson.LooseFrame(planet.GetProperty("frameInCarrier")), planet.GetProperty("isPrescribed").GetBoolean()),
            I(p, "sunTeeth"), I(p, "planetTeeth"), RotaryLinearJson.Quantity(p.GetProperty("module")), RotaryLinearJson.Quantity(p.GetProperty("carrierReference")),
            RotaryLinearJson.Quantity(p.GetProperty("sunReference")), RotaryLinearJson.Quantity(p.GetProperty("planetReference")), RotaryLinearJson.Quantity(p.GetProperty("sunMount")), RotaryLinearJson.Quantity(p.GetProperty("planetMount")), F(p.GetProperty("toothRegistration")),
            new CarrierOutputPort(S(port, "id"), S(port, "shaftId"), MechanicalAuthoringJson.LooseFrame(port.GetProperty("frameInShaft")), RotaryLinearJson.Quantity(port.GetProperty("readoutOffset"))),
            S(p, "carrierBodyId"), S(p, "sunBodyId"), S(p, "planetBodyId"), p.GetProperty("sunHeld").GetBoolean(), p.GetProperty("contactPresent").GetBoolean(), Items(p, "requiredValidationDomains", 16).Select(x => x.GetString()!));
        Require(d.DefinitionId == S(p, "definitionId"), "Carrier request identity differs."); return d;
    }
    private static void Start(Utf8JsonWriter w, string format) => MechanicalAuthoringJson.Start(w, format, Version);
    private static byte[] Encode(Action<Utf8JsonWriter> write)
    { var bytes = MechanicalAuthoringJson.Encode(write); Require(bytes.Length <= CarrierProfile.MaxDocumentBytes, "Carrier document byte bound exceeded."); return bytes; }
    private static JsonDocument Parse(byte[] bytes)
    { Require(bytes is not null && bytes.Length <= CarrierProfile.MaxDocumentBytes, "Carrier document byte bound exceeded."); return MechanicalAuthoringJson.Parse(bytes!); }
}
