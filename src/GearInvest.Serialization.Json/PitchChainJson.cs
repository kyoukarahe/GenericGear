using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit bounded declarations. A selected immutable chain is never rewritten to match a changed current route.</summary>
public static partial class PitchChainJson
{
    // Local authored declaration only; the 30A engine supplies source context separately.
    internal static void AssemblyDeclaration(Utf8JsonWriter w, AssemblyPitchChainDeclaration d)
    {
        w.WriteStartObject(); w.WriteString("profile", PitchChainProfile.Id); w.WriteString("targetBasis", d.TargetBasis.ToString());
        w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("outputTerminal"); Terminal(w, d.OutputTerminal);
        w.WritePropertyName("output"); MechanicalAuthoringJson.Output(w, d.Output); OptionalFraction(w, "requiredOutputPhase", d.RequiredOutputPhase);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static AssemblyPitchChainDeclaration AssemblyDeclaration(JsonElement p)
    {
        Require(S(p, "profile") == PitchChainProfile.Id, "Unsupported assembly member profile.");
        return new AssemblyPitchChainDeclaration(Device(p.GetProperty("device")),
            Terminal(p.GetProperty("outputTerminal")), MechanicalAuthoringJson.Output(p.GetProperty("output")), OptionalFraction(p, "requiredOutputPhase"),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!), E<AssemblyTargetBasis>(p, "targetBasis"));
    }

    public const string DraftFormat = "gear-invest.pitch-chain-draft";
    public const string BatchFormat = "gear-invest.pitch-chain-edit-batch";
    public const string SessionFormat = "gear-invest.pitch-chain-edit-session";
    public const string AnalysisFormat = "gear-invest.pitch-chain-analysis";
    public const string SpecificationFormat = "gear-invest.pitch-chain-specification";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = PitchChainProfile.MaxDocumentBytes;
    public const int MaxDocumentDepth = 48, MaxDocumentNodes = 1048576, MaxObjectKeys = 64;
    public static byte[] WriteDraft(PitchChainDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static PitchChainDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown pitch chain draft fields."); return draft; });
    public static byte[] WriteDefinition(PitchChainDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    public static byte[] WriteSpecification(PitchChainSpecification specification) => Guard(() => Encode(w =>
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        Start(w, SpecificationFormat); w.WritePropertyName("specification"); Specification(w, specification); w.WriteEndObject();
    }));
    public static PitchChainSpecification ReadSpecification(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SpecificationFormat);
        var specification = Specification(p.GetProperty("specification")); Require(specification is not null, "Explicit selected chain specification required.");
        Require(bytes.SequenceEqual(WriteSpecification(specification!)), "Noncanonical or unknown selected chain specification fields."); return specification!;
    });
    internal static void Draft(Utf8JsonWriter w, PitchChainDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static PitchChainDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new PitchChainDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Pitch chain draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, PitchChainDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", PitchChainProfile.Id); w.WriteString("geometrySemantics", PitchChainProfile.GeometrySemantics); w.WriteString("poseSemantics", PitchChainProfile.PoseSemantics);
        w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); R.Mapping(w, d.SourceMapping);
        w.WritePropertyName("device"); Device(w, d.Device); w.WritePropertyName("outputTerminal"); Terminal(w, d.OutputTerminal);
        w.WritePropertyName("output"); MechanicalAuthoringJson.Output(w, d.Output); OptionalFraction(w, "requiredOutputPhase", d.RequiredOutputPhase);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static PitchChainDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == PitchChainProfile.Id && S(p, "geometrySemantics") == PitchChainProfile.GeometrySemantics && S(p, "poseSemantics") == PitchChainProfile.PoseSemantics, "Unsupported pitch chain profile or numeric semantics.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), R.Mapping(p.GetProperty("sourceMapping")), Device(p.GetProperty("device")),
            Terminal(p.GetProperty("outputTerminal")), MechanicalAuthoringJson.Output(p.GetProperty("output")), OptionalFraction(p, "requiredOutputPhase"),
            Items(p, "requiredValidationDomains", 24).Select(x => x.GetString()!));
    }
    private static void Device(Utf8JsonWriter w, PitchChainTransmissionDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("inputShaftId", d.InputShaftId); w.WriteString("inputSprocketBodyId", d.InputSprocketBodyId);
        Integer(w, "inputToothCount", d.InputToothCount); R.Quantity(w, "inputPitch", d.InputPitch); R.Quantity(w, "inputSprocketStation", d.InputSprocketStation);
        w.WritePropertyName("outputShaft"); LengthShaft(w, d.OutputShaft); w.WriteString("outputSprocketBodyId", d.OutputSprocketBodyId);
        Integer(w, "outputToothCount", d.OutputToothCount); R.Quantity(w, "outputPitch", d.OutputPitch); R.Quantity(w, "outputSprocketStation", d.OutputSprocketStation);
        R.DerivedVector(w, "outputSprocketCenterMm", d.OutputSprocketCenterMm, "mm");
        Vector(w, "routeNormal", d.RouteNormal); R.Quantity(w, "inputMountingPhase", d.InputMountingPhase); R.Quantity(w, "outputMountingPhase", d.OutputMountingPhase);
        R.Quantity(w, "inputReferenceTurns", d.InputReferenceTurns); R.Quantity(w, "outputReferenceTurns", d.OutputReferenceTurns);
        ExactInteger(w, "toothRegistration", d.ToothRegistration, false);
        w.WritePropertyName("selectedChain"); Specification(w, d.SelectedChain); w.WriteBoolean("transmissionPresent", d.TransmissionPresent);
        w.WriteBoolean("inputCenterFixed", d.InputCenterFixed); w.WriteBoolean("inputAxisFixed", d.InputAxisFixed);
        w.WriteBoolean("outputCenterFixed", d.OutputCenterFixed); w.WriteBoolean("outputAxisFixed", d.OutputAxisFixed);
        w.WriteString("inputPortId", d.InputPortId); w.WriteString("routingKind", d.RoutingKind); w.WriteEndObject();
    }
    private static PitchChainTransmissionDefinition Device(JsonElement p) =>
        new(S(p, "id"), S(p, "inputShaftId"), S(p, "inputSprocketBodyId"), I(p, "inputToothCount"),
            R.Quantity(p.GetProperty("inputPitch")), R.Quantity(p.GetProperty("inputSprocketStation")),
            LengthShaft(p.GetProperty("outputShaft")), S(p, "outputSprocketBodyId"), I(p, "outputToothCount"),
            R.Quantity(p.GetProperty("outputPitch")), R.Quantity(p.GetProperty("outputSprocketStation")), Vector(p.GetProperty("routeNormal")),
            R.Quantity(p.GetProperty("inputMountingPhase")), R.Quantity(p.GetProperty("outputMountingPhase")),
            R.Quantity(p.GetProperty("inputReferenceTurns")), R.Quantity(p.GetProperty("outputReferenceTurns")),
            ExactInteger(p, "toothRegistration", false), Specification(p.GetProperty("selectedChain")),
            p.GetProperty("transmissionPresent").GetBoolean(), p.GetProperty("inputCenterFixed").GetBoolean(), p.GetProperty("inputAxisFixed").GetBoolean(),
            p.GetProperty("outputCenterFixed").GetBoolean(), p.GetProperty("outputAxisFixed").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "inputPortId"), S(p, "routingKind"));
    private static void Specification(Utf8JsonWriter w, PitchChainSpecification? s)
    {
        if (s is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("specificationId", s.SpecificationId); w.WriteString("chainId", s.ChainId);
        R.Quantity(w, "pitch", s.Pitch); Integer(w, "linkCount", s.LinkCount); w.WriteString("topology", s.Topology);
        ExactInteger(w, "materialRegistration", s.MaterialRegistration, false);
        w.WriteString("pinIdSemantics", "chain-id-and-material-index-sha256-v1");
        w.WriteString("linkIdSemantics", "chain-id-and-material-index-sha256-v1");
        w.WriteString("linkConnectivity", "material-i-to-i-plus-one-mod-N");
        w.WriteString("linkAlternation", "even-material-inner-odd-material-outer"); w.WriteEndObject();
    }
    private static PitchChainSpecification? Specification(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Null) return null;
        var s = new PitchChainSpecification(S(p, "chainId"), R.Quantity(p.GetProperty("pitch")), I(p, "linkCount"),
            ExactInteger(p, "materialRegistration", false), S(p, "topology"));
        Require(s.SpecificationId == S(p, "specificationId"), "Selected chain specification identity mismatch."); return s;
    }
    private static void LengthFrame(Utf8JsonWriter w, string key, OrientedFrame frame)
    { w.WriteStartObject(key); w.WriteString("originUnit", "mm"); Frame(w, "frame", frame); w.WriteEndObject(); }
    private static OrientedFrame LengthFrame(JsonElement p)
    { Require(S(p, "originUnit") == "mm", "Explicit mm frame origin required."); return MechanicalAuthoringJson.LooseFrame(p.GetProperty("frame")); }
    private static void LengthShaft(Utf8JsonWriter w, OrientedShaft shaft)
    { w.WriteStartObject(); w.WriteString("id", shaft.Id); LengthFrame(w, "frameMm", shaft.Frame); w.WriteBoolean("isPrescribed", shaft.IsPrescribed); w.WriteEndObject(); }
    private static OrientedShaft LengthShaft(JsonElement p) => new(S(p, "id"), LengthFrame(p.GetProperty("frameMm")), p.GetProperty("isPrescribed").GetBoolean());
    private static void Terminal(Utf8JsonWriter w, ShaftPort p)
    {
        w.WriteStartObject(); w.WriteString("id", p.Id); w.WriteString("shaftId", p.ShaftId); LengthFrame(w, "frameMm", p.Frame);
        Fraction(w, "phaseOffset", p.PhaseOffset); w.WriteString("kind", p.Kind.ToString()); w.WriteEndObject();
    }
    private static ShaftPort Terminal(JsonElement p) => new(S(p, "id"), S(p, "shaftId"), LengthFrame(p.GetProperty("frameMm")), F(p.GetProperty("phaseOffset")), E<ShaftConnectionKind>(p, "kind"));
    private static void Start(Utf8JsonWriter w, string format) => MechanicalAuthoringJson.Start(w, format);
    // Symmetric device-specific resource checks include every pin/link recipe; overflows never drop material poses.
    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) write(writer);
        var bytes = stream.ToArray(); Require(bytes.Length <= MaxDocumentBytes, "Pitch chain document byte limit exceeded.");
        using var parsed = Parse(bytes); return bytes;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length <= MaxDocumentBytes, "Bounded pitch chain document required.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = MaxDocumentDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; CheckNodes(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void CheckNodes(JsonElement p, ref int nodes)
    {
        Require(++nodes <= MaxDocumentNodes, "Pitch chain JSON node limit exceeded.");
        if (p.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in p.EnumerateObject())
            { Require(keys.Count < MaxObjectKeys && keys.Add(property.Name), "Duplicate or excessive pitch chain object keys."); CheckNodes(property.Value, ref nodes); }
        }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var child in p.EnumerateArray()) CheckNodes(child, ref nodes);
    }
    private static void ExactInteger(Utf8JsonWriter w, string key, BigInteger value, bool derived)
    {
        Require(BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).Length <= (derived ? 8192 : 128), "Pitch chain integer digit bound exceeded.");
        w.WriteString(key, value.ToString(CultureInfo.InvariantCulture));
    }
    private static BigInteger ExactInteger(JsonElement p, string key, bool derived)
    {
        var text = S(p, key); var maxDigits = derived ? 8192 : 128;
        Require(text.Length > 0 && text.Length <= maxDigits + 1, "Pitch chain integer digit bound exceeded.");
        Require(BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) &&
            value.ToString(CultureInfo.InvariantCulture) == text, "Noncanonical pitch chain integer.");
        Require(BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).Length <= maxDigits, "Pitch chain integer digit bound exceeded.");
        return value;
    }
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
