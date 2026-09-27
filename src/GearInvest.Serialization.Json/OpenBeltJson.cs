using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit bounded declarations. A selected immutable belt reference is never rewritten to match a changed current route.</summary>
public static partial class OpenBeltJson
{
    // Local authored declaration only; the 30A engine supplies source context separately.
    internal static void AssemblyDeclaration(Utf8JsonWriter w, AssemblyOpenBeltDeclaration d)
    {
        w.WriteStartObject(); w.WriteString("profile", OpenBeltProfile.Id); w.WriteString("targetBasis", d.TargetBasis.ToString());
        w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("outputTerminal"); Terminal(w, d.OutputTerminal);
        w.WritePropertyName("output"); MechanicalAuthoringJson.Output(w, d.Output); OptionalFraction(w, "requiredOutputPhase", d.RequiredOutputPhase);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static AssemblyOpenBeltDeclaration AssemblyDeclaration(JsonElement p)
    {
        Require(S(p, "profile") == OpenBeltProfile.Id, "Unsupported assembly member profile.");
        return new AssemblyOpenBeltDeclaration(Device(p.GetProperty("device")),
            Terminal(p.GetProperty("outputTerminal")), MechanicalAuthoringJson.Output(p.GetProperty("output")), OptionalFraction(p, "requiredOutputPhase"),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!), E<AssemblyTargetBasis>(p, "targetBasis"));
    }

    public const string DraftFormat = "gear-invest.open-belt-draft";
    public const string BatchFormat = "gear-invest.open-belt-edit-batch";
    public const string SessionFormat = "gear-invest.open-belt-edit-session";
    public const string AnalysisFormat = "gear-invest.open-belt-analysis";
    public const string LengthSpecificationFormat = "gear-invest.open-belt-length-specification";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = OpenBeltProfile.MaxDocumentBytes;
    public static byte[] WriteDraft(OpenBeltDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static OpenBeltDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown open belt draft fields."); return draft; });
    public static byte[] WriteDefinition(OpenBeltDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    public static byte[] WriteLengthSpecification(OpenBeltLengthSpecification specification) => Guard(() => Encode(w =>
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        Start(w, LengthSpecificationFormat); w.WritePropertyName("specification"); LengthSpecification(w, specification); w.WriteEndObject();
    }));
    public static OpenBeltLengthSpecification ReadLengthSpecification(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, LengthSpecificationFormat);
        var specification = LengthSpecification(p.GetProperty("specification")); Require(specification is not null, "Explicit selected belt specification required.");
        Require(bytes.SequenceEqual(WriteLengthSpecification(specification!)), "Noncanonical or unknown selected belt specification fields."); return specification!;
    });
    internal static void Draft(Utf8JsonWriter w, OpenBeltDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static OpenBeltDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new OpenBeltDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Open belt draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, OpenBeltDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", OpenBeltProfile.Id); w.WriteString("numericSemantics", OpenBeltProfile.NumericSemantics);
        w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); R.Mapping(w, d.SourceMapping);
        w.WritePropertyName("device"); Device(w, d.Device); w.WritePropertyName("outputTerminal"); Terminal(w, d.OutputTerminal);
        w.WritePropertyName("output"); MechanicalAuthoringJson.Output(w, d.Output); OptionalFraction(w, "requiredOutputPhase", d.RequiredOutputPhase);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static OpenBeltDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == OpenBeltProfile.Id && S(p, "numericSemantics") == OpenBeltProfile.NumericSemantics, "Unsupported open belt profile or numeric semantics.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), R.Mapping(p.GetProperty("sourceMapping")), Device(p.GetProperty("device")),
            Terminal(p.GetProperty("outputTerminal")), MechanicalAuthoringJson.Output(p.GetProperty("output")), OptionalFraction(p, "requiredOutputPhase"),
            Items(p, "requiredValidationDomains", 24).Select(x => x.GetString()!));
    }
    private static void Device(Utf8JsonWriter w, OpenBeltTransmissionDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("inputShaftId", d.InputShaftId); w.WriteString("inputPulleyBodyId", d.InputPulleyBodyId);
        R.LengthVector(w, "inputPulleyCenterMm", d.InputPulleyCenterMm); R.Quantity(w, "inputPitchRadius", d.InputPitchRadius);
        w.WritePropertyName("outputShaft"); LengthShaft(w, d.OutputShaft); w.WriteString("outputPulleyBodyId", d.OutputPulleyBodyId);
        R.Quantity(w, "outputPulleyStation", d.OutputPulleyStation); R.Quantity(w, "outputPitchRadius", d.OutputPitchRadius);
        R.DerivedVector(w, "outputPulleyCenterMm", d.OutputPulleyCenterMm, "mm");
        Vector(w, "routeNormal", d.RouteNormal); R.Quantity(w, "inputReferenceTurns", d.InputReferenceTurns); R.Quantity(w, "outputReferenceTurns", d.OutputReferenceTurns);
        w.WritePropertyName("selectedBelt"); LengthSpecification(w, d.SelectedBelt); w.WriteBoolean("transmissionPresent", d.TransmissionPresent);
        w.WriteBoolean("inputCenterFixed", d.InputCenterFixed); w.WriteBoolean("inputAxisFixed", d.InputAxisFixed);
        w.WriteBoolean("outputCenterFixed", d.OutputCenterFixed); w.WriteBoolean("outputAxisFixed", d.OutputAxisFixed);
        w.WriteString("inputPortId", d.InputPortId); w.WriteString("routingKind", d.RoutingKind); w.WriteEndObject();
    }
    private static OpenBeltTransmissionDefinition Device(JsonElement p) =>
        new(S(p, "id"), S(p, "inputShaftId"), S(p, "inputPulleyBodyId"), R.LengthVector(p.GetProperty("inputPulleyCenterMm")),
            R.Quantity(p.GetProperty("inputPitchRadius")), LengthShaft(p.GetProperty("outputShaft")), S(p, "outputPulleyBodyId"),
            R.Quantity(p.GetProperty("outputPulleyStation")), R.Quantity(p.GetProperty("outputPitchRadius")), Vector(p.GetProperty("routeNormal")),
            R.Quantity(p.GetProperty("inputReferenceTurns")), R.Quantity(p.GetProperty("outputReferenceTurns")), LengthSpecification(p.GetProperty("selectedBelt")),
            p.GetProperty("transmissionPresent").GetBoolean(), p.GetProperty("inputCenterFixed").GetBoolean(), p.GetProperty("inputAxisFixed").GetBoolean(),
            p.GetProperty("outputCenterFixed").GetBoolean(), p.GetProperty("outputAxisFixed").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "inputPortId"), S(p, "routingKind"));
    private static void LengthSpecification(Utf8JsonWriter w, OpenBeltLengthSpecification? s)
    {
        if (s is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("specificationId", s.SpecificationId); w.WriteString("semantics", s.Semantics);
        R.Quantity(w, "centerDistance", s.CenterDistance); R.Quantity(w, "inputPitchRadius", s.InputPitchRadius); R.Quantity(w, "outputPitchRadius", s.OutputPitchRadius); w.WriteEndObject();
    }
    private static OpenBeltLengthSpecification? LengthSpecification(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Null) return null;
        var s = new OpenBeltLengthSpecification(R.Quantity(p.GetProperty("centerDistance")), R.Quantity(p.GetProperty("inputPitchRadius")), R.Quantity(p.GetProperty("outputPitchRadius")), S(p, "semantics"));
        Require(s.SpecificationId == S(p, "specificationId"), "Selected belt specification identity mismatch."); return s;
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
    private static byte[] Encode(Action<Utf8JsonWriter> write) => MechanicalAuthoringJson.Encode(write);
    private static JsonDocument Parse(byte[] bytes) => MechanicalAuthoringJson.Parse(bytes);
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
