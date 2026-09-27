using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit bounded declarations with independent worm and wheel specifications.</summary>
public static partial class WormDriveJson
{
    // Local authored declaration only; the 30A engine supplies source context separately.
    internal static void AssemblyDeclaration(Utf8JsonWriter w, AssemblyWormDeclaration d)
    {
        w.WriteStartObject(); w.WriteString("profile", WormDriveProfile.Id); w.WriteString("targetBasis", d.TargetBasis.ToString());
        w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("outputTerminal"); Terminal(w, d.OutputTerminal);
        w.WritePropertyName("output"); MechanicalAuthoringJson.Output(w, d.Output); OptionalFraction(w, "requiredOutputPhase", d.RequiredOutputPhase);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static AssemblyWormDeclaration AssemblyDeclaration(JsonElement p)
    {
        Require(S(p, "profile") == WormDriveProfile.Id, "Unsupported assembly member profile.");
        return new AssemblyWormDeclaration(Device(p.GetProperty("device")),
            Terminal(p.GetProperty("outputTerminal")), MechanicalAuthoringJson.Output(p.GetProperty("output")), OptionalFraction(p, "requiredOutputPhase"),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!), E<AssemblyTargetBasis>(p, "targetBasis"));
    }

    public const string DraftFormat = "gear-invest.worm-drive-draft";
    public const string BatchFormat = "gear-invest.worm-drive-edit-batch";
    public const string SessionFormat = "gear-invest.worm-drive-edit-session";
    public const string AnalysisFormat = "gear-invest.worm-drive-analysis";
    public const string SpecificationFormat = "gear-invest.worm-drive-specification";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = WormDriveProfile.MaxDocumentBytes;
    public const int MaxDocumentDepth = WormDriveProfile.MaxDocumentDepth, MaxDocumentNodes = WormDriveProfile.MaxJsonNodes, MaxObjectKeys = 64;
    public static byte[] WriteDraft(WormDriveDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static WormDriveDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown worm drive draft fields."); return draft; });
    public static byte[] WriteDefinition(WormDriveDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    public static byte[] WriteSpecification(IdealWormWheelSpecification specification) => Guard(() => Encode(w =>
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        Start(w, SpecificationFormat); w.WritePropertyName("specification"); Wheel(w, specification); w.WriteEndObject();
    }));
    public static IdealWormWheelSpecification ReadSpecification(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SpecificationFormat);
        var specification = Wheel(p.GetProperty("specification")); Require(specification is not null, "Explicit ideal wheel specification required.");
        Require(bytes.SequenceEqual(WriteSpecification(specification!)), "Noncanonical or unknown wheel specification fields."); return specification!;
    });
    public static byte[] WriteWormSpecification(CylindricalWormSpecification specification) => Guard(() => Encode(w =>
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        Start(w, "gear-invest.worm-drive-worm-specification"); w.WritePropertyName("specification"); Worm(w, specification); w.WriteEndObject();
    }));
    public static CylindricalWormSpecification ReadWormSpecification(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, "gear-invest.worm-drive-worm-specification");
        var specification = Worm(p.GetProperty("specification"));
        Require(bytes.SequenceEqual(WriteWormSpecification(specification)), "Noncanonical or unknown worm specification fields."); return specification;
    });
    internal static void Draft(Utf8JsonWriter w, WormDriveDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static WormDriveDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new WormDriveDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Worm drive draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, WormDriveDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", WormDriveProfile.Id); w.WriteString("geometrySemantics", WormDriveProfile.GeometrySemantics); w.WriteString("poseSemantics", WormDriveProfile.PoseSemantics);
        w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); R.Mapping(w, d.SourceMapping);
        w.WritePropertyName("device"); Device(w, d.Device); w.WritePropertyName("outputTerminal"); Terminal(w, d.OutputTerminal);
        w.WritePropertyName("output"); MechanicalAuthoringJson.Output(w, d.Output); OptionalFraction(w, "requiredOutputPhase", d.RequiredOutputPhase);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static WormDriveDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == WormDriveProfile.Id && S(p, "geometrySemantics") == WormDriveProfile.GeometrySemantics && S(p, "poseSemantics") == WormDriveProfile.PoseSemantics, "Unsupported worm drive profile or numeric semantics.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), R.Mapping(p.GetProperty("sourceMapping")), Device(p.GetProperty("device")),
            Terminal(p.GetProperty("outputTerminal")), MechanicalAuthoringJson.Output(p.GetProperty("output")), OptionalFraction(p, "requiredOutputPhase"),
            Items(p, "requiredValidationDomains", 24).Select(x => x.GetString()!));
    }
    private static void Device(Utf8JsonWriter w, WormDriveTransmissionDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("inputShaftId", d.InputShaftId);
        w.WriteString("inputWormBodyId", d.InputWormBodyId); w.WritePropertyName("worm"); Worm(w, d.Worm);
        Vector(w, "physicalHelixAxis", d.PhysicalHelixAxis); R.Quantity(w, "inputPitchStation", d.InputPitchStation);
        w.WritePropertyName("outputShaft"); LengthShaft(w, d.OutputShaft); w.WriteString("outputWheelBodyId", d.OutputWheelBodyId);
        w.WritePropertyName("selectedWheel"); Wheel(w, d.SelectedWheel); R.Quantity(w, "outputPitchStation", d.OutputPitchStation);
        R.DerivedVector(w, "outputPitchCenterMm", d.OutputPitchCenterMm, "mm"); Vector(w, "contactSide", d.ContactSide);
        R.Quantity(w, "inputMountingPhase", d.InputMountingPhase); R.Quantity(w, "outputMountingPhase", d.OutputMountingPhase);
        R.Quantity(w, "inputReferenceTurns", d.InputReferenceTurns); R.Quantity(w, "outputReferenceTurns", d.OutputReferenceTurns);
        w.WriteBoolean("transmissionPresent", d.TransmissionPresent);
        w.WriteBoolean("inputCenterFixed", d.InputCenterFixed); w.WriteBoolean("inputAxisFixed", d.InputAxisFixed);
        w.WriteBoolean("outputCenterFixed", d.OutputCenterFixed); w.WriteBoolean("outputAxisFixed", d.OutputAxisFixed);
        w.WriteString("inputPortId", d.InputPortId); w.WriteString("profile", d.Profile); w.WriteEndObject();
    }
    private static WormDriveTransmissionDefinition Device(JsonElement p) =>
        new(S(p, "id"), S(p, "inputShaftId"), S(p, "inputWormBodyId"), Worm(p.GetProperty("worm")),
            Vector(p.GetProperty("physicalHelixAxis")), R.Quantity(p.GetProperty("inputPitchStation")),
            LengthShaft(p.GetProperty("outputShaft")), S(p, "outputWheelBodyId"), Wheel(p.GetProperty("selectedWheel")),
            R.Quantity(p.GetProperty("outputPitchStation")), Vector(p.GetProperty("contactSide")),
            R.Quantity(p.GetProperty("inputMountingPhase")), R.Quantity(p.GetProperty("outputMountingPhase")),
            R.Quantity(p.GetProperty("inputReferenceTurns")), R.Quantity(p.GetProperty("outputReferenceTurns")),
            p.GetProperty("transmissionPresent").GetBoolean(), p.GetProperty("inputCenterFixed").GetBoolean(),
            p.GetProperty("inputAxisFixed").GetBoolean(), p.GetProperty("outputCenterFixed").GetBoolean(),
            p.GetProperty("outputAxisFixed").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "inputPortId"), S(p, "profile"));
    private static void Worm(Utf8JsonWriter w, CylindricalWormSpecification s)
    {
        w.WriteStartObject(); w.WriteString("specificationId", s.SpecificationId); Integer(w, "starts", s.Starts);
        R.Quantity(w, "axialModule", s.AxialModule); R.Quantity(w, "pitchRadius", s.PitchRadius); Integer(w, "handedness", s.Handedness);
        w.WriteString("parameterization", s.Parameterization); w.WriteString("geometryKind", s.GeometryKind); w.WriteEndObject();
    }
    private static CylindricalWormSpecification Worm(JsonElement p)
    {
        var s = new CylindricalWormSpecification(I(p, "starts"), R.Quantity(p.GetProperty("axialModule")),
            R.Quantity(p.GetProperty("pitchRadius")), I(p, "handedness"), S(p, "parameterization"), S(p, "geometryKind"));
        Require(s.SpecificationId == S(p, "specificationId"), "Worm specification identity mismatch."); return s;
    }
    private static void Wheel(Utf8JsonWriter w, IdealWormWheelSpecification? s)
    {
        if (s is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("specificationId", s.SpecificationId); Integer(w, "toothCount", s.ToothCount);
        R.Quantity(w, "transverseModule", s.TransverseModule); Integer(w, "handedness", s.Handedness);
        Fraction(w, "traceSlope", s.TraceSlope); w.WriteString("parameterization", s.Parameterization);
        w.WriteString("traceSemantics", s.TraceSemantics); w.WriteEndObject();
    }
    private static IdealWormWheelSpecification? Wheel(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Null) return null;
        var s = new IdealWormWheelSpecification(I(p, "toothCount"), R.Quantity(p.GetProperty("transverseModule")),
            I(p, "handedness"), F(p.GetProperty("traceSlope")), S(p, "parameterization"), S(p, "traceSemantics"));
        Require(s.SpecificationId == S(p, "specificationId"), "Wheel specification identity mismatch."); return s;
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
    // Writers parse their complete result with precisely the same byte/depth/node/key checks as readers.
    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) write(writer);
        var bytes = stream.ToArray(); Require(bytes.Length <= MaxDocumentBytes, "Worm drive document byte limit exceeded.");
        using var parsed = Parse(bytes); return bytes;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length <= MaxDocumentBytes, "Bounded worm drive document required.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = MaxDocumentDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; CheckNodes(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void CheckNodes(JsonElement p, ref int nodes)
    {
        Require(++nodes <= MaxDocumentNodes, "Worm drive JSON node limit exceeded.");
        if (p.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in p.EnumerateObject())
            { Require(keys.Count < MaxObjectKeys && keys.Add(property.Name), "Duplicate or excessive worm drive object keys."); CheckNodes(property.Value, ref nodes); }
        }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var child in p.EnumerateArray()) CheckNodes(child, ref nodes);
    }
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
