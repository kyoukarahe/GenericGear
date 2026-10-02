using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public sealed class DifferentialArtifact
{
    private readonly byte[] bytes;
    internal DifferentialArtifact(DifferentialRequest request, DifferentialAnalysis analysis, string hash, byte[] bytes)
    { Request = request; Analysis = analysis; ArtifactHash = hash; this.bytes = (byte[])bytes.Clone(); }
    public DifferentialRequest Request { get; }
    public DifferentialAnalysis Analysis { get; }
    public string ArtifactHash { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

/// <summary>Strict source codec. The facade additionally re-finalizes actual prefix source provenance.</summary>
public static class DifferentialJson
{
    public const string DraftFormat = "gear-invest.differential-draft";
    public const string ArtifactFormat = "gear-invest.differential-mechanism";
    public const string ReplayFormat = "gear-invest.differential-replay";
    public const string AnalysisFormat = "gear-invest.differential-analysis";
    public const string Version = "0.1";
    public static byte[] WriteDraft(DifferentialRequest request) => Encode(w =>
    { Start(w, DraftFormat); w.WritePropertyName("request"); Request(w, request); w.WriteEndObject(); });
    public static DifferentialRequest ReadDraft(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, DraftFormat); var r = Request(doc.RootElement.GetProperty("request"));
        Require(bytes.SequenceEqual(WriteDraft(r)), "Noncanonical or unknown differential draft fields."); return r;
    });
    public static DifferentialArtifact WriteArtifact(DifferentialRequest request, byte[]? sourceBytes, string? sourceIdentity)
    {
        var a = DifferentialAnalyzer.Prepare(request); Require(a.CanExport, "Differential required admission/independent basis/determinacy did not pass.");
        Require(sourceBytes is null || sourceBytes.Length <= 4 * 1024 * 1024, "Differential attached source bound exceeded.");
        var hash = Hash(Document(request, a, sourceBytes, sourceIdentity, null));
        return new(request, a, hash, Document(request, a, sourceBytes, sourceIdentity, hash));
    }
    public static DifferentialArtifact ReadArtifact(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, ArtifactFormat);
        var request = Request(p.GetProperty("request")); var source = p.GetProperty("attachedSource"); var raw = source.GetProperty("artifactUtf8");
        if (raw.ValueKind != JsonValueKind.Null) Require(raw.GetString()!.Length <= 4 * ((4 * 1024 * 1024 + 2) / 3), "Differential attached source bound exceeded.");
        var fresh = WriteArtifact(request, raw.ValueKind == JsonValueKind.Null ? null : raw.GetBytesFromBase64(), MechanicalAuthoringJson.NullableString(source, "identity"));
        Require(bytes.SequenceEqual(fresh.Bytes), "Fresh differential source/rows/Q/b/K/ownership/holds/frames/basis/checks differ."); return fresh;
    });
    private static byte[] Document(DifferentialRequest r, DifferentialAnalysis a, byte[]? source, string? identity, string? hash) => Encode(w =>
    {
        Start(w, ArtifactFormat); w.WriteString("profile", DifferentialProfile.Id); w.WriteString("requestId", r.RequestId);
        if (hash is not null) w.WriteString("artifactHash", hash);
        w.WritePropertyName("request"); Request(w, r); w.WriteStartObject("attachedSource"); w.WriteString("identity", identity);
        if (source is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", source);
        w.WriteEndObject(); w.WritePropertyName("compiled"); Compiled(w, a); w.WriteEndObject();
    });
    public static byte[] WriteReplay(DifferentialArtifact artifact)
    {
        var payload = Encode(w =>
        {
            w.WriteStartObject(); w.WriteString("profile", DifferentialProfile.Id); w.WriteString("artifactId", artifact.ArtifactHash); w.WriteString("requestId", artifact.Request.RequestId);
            w.WriteString("sourceRawSha256", Hash(artifact.Bytes)); w.WriteBase64String("sourceArtifactUtf8", artifact.Bytes);
            w.WritePropertyName("compiled"); Compiled(w, artifact.Analysis); w.WriteEndObject();
        });
        return Encode(w => { Start(w, ReplayFormat); w.WriteString("replayId", Hash(payload)); w.WriteBase64String("payloadUtf8", payload); w.WriteEndObject(); });
    }
    public static DifferentialArtifact ReadReplaySource(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, ReplayFormat);
        var encoded = doc.RootElement.GetProperty("payloadUtf8"); Require(encoded.GetString()!.Length <= DifferentialProfile.MaxDocumentBytes, "Differential payload bound exceeded.");
        using var payload = Parse(encoded.GetBytesFromBase64()); var source = payload.RootElement.GetProperty("sourceArtifactUtf8");
        Require(source.GetString()!.Length <= DifferentialProfile.MaxDocumentBytes, "Differential source bound exceeded.");
        var artifact = ReadArtifact(source.GetBytesFromBase64()); Require(bytes.SequenceEqual(WriteReplay(artifact)), "Differential replay differs from fresh source."); return artifact;
    });
    /// <summary>Readonly analysis receipt, never accepted as an executable mechanism or authoritative cached analysis.</summary>
    public static byte[] WriteAnalysis(DifferentialAnalysis analysis) => Encode(w =>
    {
        Start(w, AnalysisFormat); w.WritePropertyName("request"); Request(w, analysis.Request);
        w.WriteBoolean("numericBoundaryAnalysis", analysis.IsNumericBoundaryAnalysis);
        Array(w, "boundary", analysis.Boundary, (x, b) => { x.WriteStartObject(); x.WriteString("id", b.Id); x.WriteString("portId", b.PortId); RotaryLinearJson.Quantity(x, "position", b.Position); x.WriteEndObject(); });
        w.WritePropertyName("compiled"); Compiled(w, analysis); w.WriteEndObject();
    });
    private static void Compiled(Utf8JsonWriter w, DifferentialAnalysis a)
    {
        var d = a.Request.Definition;
        w.WriteStartObject(); w.WriteString("policy", DifferentialProfile.Policy); w.WriteString("definitionId", d.DefinitionId); w.WriteString("requestId", a.Request.RequestId);
        w.WriteString("status", a.Status.ToString()); w.WriteBoolean("canExport", a.CanExport); w.WriteBoolean("isFullyDetermined", a.IsFullyDetermined);
        w.WriteString("carrierShaftId", d.CarrierShaft.Id); w.WriteString("sunShaftId", d.SunShaft.Id); w.WriteString("planetShaftId", d.PlanetShaft.Id);
        MechanicalAuthoringJson.Strings(w, "inputPortIds", a.Request.InputPortIds); MechanicalAuthoringJson.Strings(w, "coordinateIds", a.CoordinateIds);
        if (a.Reduction is null) w.WriteNull("rank"); else Integer(w, "rank", a.Reduction.Rank);
        Array(w, "rows", a.Rows, (x, r) => { x.WriteStartObject(); x.WriteString("id", r.Id); Array(x, "a", r.Coefficients, Derived); Array(x, "rhs", r.RightHandSide, Derived); x.WriteEndObject(); });
        Array(w, "reducedRows", a.Reduction?.Rows ?? System.Array.Empty<ExactReducedRow>().ToList().AsReadOnly(), (x, r) =>
        { x.WriteStartObject(); Integer(x, "pivot", r.Pivot); Array(x, "a", r.Coefficients, Derived); Array(x, "rhs", r.RightHandSide, Derived); MechanicalAuthoringJson.Strings(x, "witnessRows", r.WitnessRows); x.WriteEndObject(); });
        Array(w, "checks", a.Checks, (x, c) => { x.WriteStartObject(); x.WriteString("domain", c.Domain); x.WriteString("verdict", c.Verdict.ToString()); x.WriteBoolean("required", c.Required); x.WriteString("detail", c.Detail); x.WriteEndObject(); });
        Array(w, "diagnostics", a.Diagnostics, (x, c) => { x.WriteStartObject(); x.WriteString("code", c.Code); x.WriteString("stage", c.Stage); x.WriteString("detail", c.Detail); MechanicalAuthoringJson.Strings(x, "related", c.Related.Select(r => r.Key)); x.WriteEndObject(); });
        Array(w, "coordinates", a.Coordinates, (x, c) =>
        {
            x.WriteStartObject(); x.WriteString("shaftId", c.ShaftId); x.WriteBoolean("isKnown", c.IsKnown); Law(x, "law", c.Law);
            x.WriteStartObject("freeTerms"); foreach (var p in c.FreeTerms) { x.WritePropertyName(p.Key); Derived(x, p.Value); } x.WriteEndObject(); x.WriteEndObject();
        });
        Law(w, "carrierCommon", a.CarrierCommon); Law(w, "planetCommon", a.PlanetCommon); Law(w, "planetRelative", a.PlanetRelative);
        Array(w, "ports", d.Ports, (x, p) => { x.WriteStartObject(); x.WriteString("id", p.Id); x.WriteString("shaftId", p.ShaftId); Fraction(x, "sign", p.FrameInShaft.Z.Z); RotaryLinearJson.Quantity(x, "readoutOffset", p.ReadoutOffset); x.WriteEndObject(); });
        Array(w, "poseNodes", a.PoseNodes, (x, n) =>
        {
            x.WriteStartObject(); x.WriteString("id", n.Id); x.WriteString("parentId", n.ParentId); DerivedFrame(x, n.Frame); Law(x, "rotation", n.Rotation);
            x.WriteString("shaftId", n.ShaftId); x.WriteString("bodyId", n.BodyId); if (n.Teeth.HasValue) Integer(x, "teeth", n.Teeth.Value); else x.WriteNull("teeth");
            if (n.PitchRadiusMm.HasValue) { x.WritePropertyName("pitchRadiusMm"); Derived(x, n.PitchRadiusMm.Value); } else x.WriteNull("pitchRadiusMm"); x.WriteEndObject();
        }); w.WriteEndObject();
    }
    private static void Law(Utf8JsonWriter w, string key, DifferentialLaw? law)
    {
        if (law is null) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WriteStartObject("q"); foreach (var p in law.Coefficients) { w.WritePropertyName(p.Key); Derived(w, p.Value); } w.WriteEndObject();
        w.WritePropertyName("b"); Derived(w, law.Offset); w.WriteEndObject();
    }
    private static void Derived(Utf8JsonWriter w, Rational r)
    {
        var n = r.Numerator.ToString(CultureInfo.InvariantCulture); var den = r.Denominator.ToString(CultureInfo.InvariantCulture);
        Require(n.Length <= BoundedLinearBlock.MaxDerivedDigits && den.Length <= BoundedLinearBlock.MaxDerivedDigits, "Differential derived fraction bound exceeded.");
        w.WriteStartObject(); w.WriteString("numerator", n); w.WriteString("denominator", den); w.WriteEndObject();
    }
    private static void DerivedFrame(Utf8JsonWriter w, OrientedFrame f)
    {
        void V(string key, ExactVector3 v) { w.WriteStartArray(key); Derived(w, v.X); Derived(w, v.Y); Derived(w, v.Z); w.WriteEndArray(); }
        w.WriteStartObject("frame"); V("origin", f.Origin); V("x", f.X); V("y", f.Y); V("z", f.Z); w.WriteEndObject();
    }
    private static void Request(Utf8JsonWriter w, DifferentialRequest r)
    {
        var d = r.Definition; w.WriteStartObject(); w.WriteString("requestId", r.RequestId); MechanicalAuthoringJson.Strings(w, "inputPortIds", r.InputPortIds);
        w.WriteStartObject("definition"); w.WriteString("definitionId", d.DefinitionId);
        w.WritePropertyName("carrierShaft"); MechanicalAuthoringJson.WriteShaft(w, d.CarrierShaft); w.WritePropertyName("sunShaft"); MechanicalAuthoringJson.WriteShaft(w, d.SunShaft);
        w.WriteStartObject("planetShaft"); w.WriteString("id", d.PlanetShaft.Id); w.WriteString("carrierShaftId", d.PlanetShaft.CarrierShaftId); Frame(w, "frameInCarrier", d.PlanetShaft.FrameInCarrier); w.WriteBoolean("isPrescribed", d.PlanetShaft.IsPrescribed); w.WriteEndObject();
        Frame(w, "planeMm", d.PlaneMm); Integer(w, "sunTeeth", d.SunTeeth); Integer(w, "planetTeeth", d.PlanetTeeth); RotaryLinearJson.Quantity(w, "module", d.Module);
        RotaryLinearJson.Quantity(w, "carrierReference", d.CarrierReference); RotaryLinearJson.Quantity(w, "sunReference", d.SunReference); RotaryLinearJson.Quantity(w, "planetReference", d.PlanetReference);
        RotaryLinearJson.Quantity(w, "sunMount", d.SunMount); RotaryLinearJson.Quantity(w, "planetMount", d.PlanetMount); Fraction(w, "toothRegistration", d.ToothRegistration);
        Array(w, "ports", d.Ports, (x, p) => { x.WriteStartObject(); x.WriteString("id", p.Id); x.WriteString("shaftId", p.ShaftId); Frame(x, "frameInShaft", p.FrameInShaft); RotaryLinearJson.Quantity(x, "readoutOffset", p.ReadoutOffset); x.WriteEndObject(); });
        Array(w, "holds", d.Holds, (x, h) => { x.WriteStartObject(); x.WriteString("id", h.Id); x.WriteString("shaftId", h.ShaftId); RotaryLinearJson.Quantity(x, "position", h.Position); x.WriteEndObject(); });
        w.WritePropertyName("prefix"); if (d.Prefix is null) w.WriteNullValue(); else MechanicalAuthoringJson.Draft(w, d.Prefix);
        w.WritePropertyName("prefixMapping"); if (d.PrefixMapping is null) w.WriteNullValue(); else RotaryLinearJson.Mapping(w, d.PrefixMapping);
        w.WriteString("carrierBodyId", d.CarrierBodyId); w.WriteString("sunBodyId", d.SunBodyId); w.WriteString("planetBodyId", d.PlanetBodyId); w.WriteBoolean("contactPresent", d.ContactPresent);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject(); w.WriteEndObject();
    }
    private static DifferentialRequest Request(JsonElement r)
    {
        var p = r.GetProperty("definition"); var planet = p.GetProperty("planetShaft"); var prefix = p.GetProperty("prefix"); var map = p.GetProperty("prefixMapping");
        var d = new DifferentialDefinition(MechanicalAuthoringJson.ReadShaft(p.GetProperty("carrierShaft")), MechanicalAuthoringJson.ReadShaft(p.GetProperty("sunShaft")),
            new CarrierLocalShaft(S(planet, "id"), S(planet, "carrierShaftId"), MechanicalAuthoringJson.LooseFrame(planet.GetProperty("frameInCarrier")), planet.GetProperty("isPrescribed").GetBoolean()),
            MechanicalAuthoringJson.LooseFrame(p.GetProperty("planeMm")), I(p, "sunTeeth"), I(p, "planetTeeth"), RotaryLinearJson.Quantity(p.GetProperty("module")),
            RotaryLinearJson.Quantity(p.GetProperty("carrierReference")), RotaryLinearJson.Quantity(p.GetProperty("sunReference")), RotaryLinearJson.Quantity(p.GetProperty("planetReference")),
            RotaryLinearJson.Quantity(p.GetProperty("sunMount")), RotaryLinearJson.Quantity(p.GetProperty("planetMount")), F(p.GetProperty("toothRegistration")),
            Items(p, "ports", 6).Select(x => new CarrierOutputPort(S(x, "id"), S(x, "shaftId"), MechanicalAuthoringJson.LooseFrame(x.GetProperty("frameInShaft")), RotaryLinearJson.Quantity(x.GetProperty("readoutOffset")))),
            Items(p, "holds", 6).Select(x => new DifferentialHold(S(x, "id"), S(x, "shaftId"), RotaryLinearJson.Quantity(x.GetProperty("position")))),
            prefix.ValueKind == JsonValueKind.Null ? null : MechanicalAuthoringJson.Draft(prefix), map.ValueKind == JsonValueKind.Null ? null : RotaryLinearJson.Mapping(map),
            S(p, "carrierBodyId"), S(p, "sunBodyId"), S(p, "planetBodyId"), p.GetProperty("contactPresent").GetBoolean(), Items(p, "requiredValidationDomains", 16).Select(x => x.GetString()!));
        var request = new DifferentialRequest(d, Items(r, "inputPortIds", 2).Select(x => x.GetString()!));
        Require(d.DefinitionId == S(p, "definitionId") && request.RequestId == S(r, "requestId"), "Differential definition/request identity differs."); return request;
    }
    private static void Start(Utf8JsonWriter w, string format) => MechanicalAuthoringJson.Start(w, format, Version);
    private static byte[] Encode(Action<Utf8JsonWriter> write) { var b = MechanicalAuthoringJson.Encode(write); Require(b.Length <= DifferentialProfile.MaxDocumentBytes, "Differential document bound exceeded."); return b; }
    private static JsonDocument Parse(byte[] b) { Require(b is not null && b.Length <= DifferentialProfile.MaxDocumentBytes, "Differential document bound exceeded."); return MechanicalAuthoringJson.Parse(b!); }
}
