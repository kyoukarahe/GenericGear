using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit bounded CP rack documents; source, scalar quantities and comparison requests use their existing codecs unchanged.</summary>
public static partial class RackPinionJson
{
    public const string DraftFormat = "gear-invest.rack-pinion-draft";
    public const string BatchFormat = "gear-invest.rack-pinion-edit-batch";
    public const string SessionFormat = "gear-invest.rack-pinion-edit-session";
    public const string AnalysisFormat = "gear-invest.rack-pinion-analysis";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = RackPinionProfile.MaxDocumentBytes;
    public static byte[] WriteDraft(RackPinionDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static RackPinionDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown rack-pinion draft fields."); return draft; });
    public static byte[] WriteDefinition(RackPinionDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    internal static void Draft(Utf8JsonWriter w, RackPinionDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static RackPinionDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new RackPinionDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Rack draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, RackPinionDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", RackPinionProfile.Id); w.WriteString("numericSemantics", RackPinionProfile.NumericSemantics);
        w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); RotaryLinearJson.Mapping(w, d.SourceMapping);
        w.WritePropertyName("device"); Device(w, d.Device); w.WritePropertyName("output"); Output(w, d.Output);
        w.WritePropertyName("requirement"); RotaryLinearJson.Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static RackPinionDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == RackPinionProfile.Id && S(p, "numericSemantics") == RackPinionProfile.NumericSemantics, "Unsupported rack-pinion profile or numeric semantics.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), RotaryLinearJson.Mapping(p.GetProperty("sourceMapping")),
            Device(p.GetProperty("device")), Output(p.GetProperty("output")), RotaryLinearJson.Requirement(p.GetProperty("requirement")),
            Items(p, "requiredValidationDomains", 24).Select(x => x.GetString()!));
    }
    private static void Device(Utf8JsonWriter w, CircularPitchRackDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("parameterization", d.Parameterization);
        w.WriteString("pinionShaftId", d.PinionShaftId); w.WriteString("pinionBodyId", d.PinionBodyId); w.WriteString("rackBodyId", d.RackBodyId);
        w.WriteString("guideId", d.GuideId); w.WriteString("linearDofId", d.LinearDofId); w.WriteString("pinionPortId", d.PinionPortId);
        Integer(w, "pinionToothCount", d.PinionToothCount); RotaryLinearJson.Quantity(w, "pinionCircularPitch", d.PinionCircularPitch); RotaryLinearJson.Quantity(w, "rackPitch", d.RackPitch);
        PiLength(w, "pitchRadius", d.PitchRadius); RotaryLinearJson.DerivedQuantity(w, "advancePerTurn", d.AdvancePerTurn);
        RotaryLinearJson.LengthVector(w, "pinionCenterMm", d.PinionCenterMm); Vector(w, "contactNormal", d.ContactNormal); PiFrame(w, "guideFrameMm", d.GuideFrameMm);
        RotaryLinearJson.Quantity(w, "longitudinalOffset", d.LongitudinalOffset); RotaryLinearJson.Quantity(w, "pinionReferenceTurns", d.PinionReferenceTurns); RotaryLinearJson.Quantity(w, "rackReferencePosition", d.RackReferencePosition);
        RotaryLinearJson.Interval(w, "activeMaterialInterval", d.ActiveMaterialInterval); RotaryLinearJson.Interval(w, "guideInterval", d.GuideInterval);
        w.WriteBoolean("transmissionPresent", d.TransmissionPresent); w.WriteBoolean("pinionCenterFixed", d.PinionCenterFixed); w.WriteBoolean("pinionAxisFixed", d.PinionAxisFixed);
        w.WriteBoolean("guidePresent", d.GuidePresent); w.WriteBoolean("rackRotationFixed", d.RackRotationFixed); w.WriteBoolean("transverseMotionFixed", d.TransverseMotionFixed); w.WriteEndObject();
    }
    private static CircularPitchRackDefinition Device(JsonElement p)
    {
        var d = new CircularPitchRackDefinition(S(p, "id"), S(p, "pinionShaftId"), S(p, "pinionBodyId"), S(p, "rackBodyId"), S(p, "guideId"), S(p, "linearDofId"), I(p, "pinionToothCount"),
            RotaryLinearJson.Quantity(p.GetProperty("pinionCircularPitch")), RotaryLinearJson.Quantity(p.GetProperty("rackPitch")), RotaryLinearJson.LengthVector(p.GetProperty("pinionCenterMm")),
            Vector(p.GetProperty("contactNormal")), PiFrame(p.GetProperty("guideFrameMm")), RotaryLinearJson.Quantity(p.GetProperty("longitudinalOffset")),
            RotaryLinearJson.Quantity(p.GetProperty("pinionReferenceTurns")), RotaryLinearJson.Quantity(p.GetProperty("rackReferencePosition")),
            RotaryLinearJson.Interval(p.GetProperty("activeMaterialInterval")), RotaryLinearJson.Interval(p.GetProperty("guideInterval")),
            p.GetProperty("transmissionPresent").GetBoolean(), p.GetProperty("pinionCenterFixed").GetBoolean(), p.GetProperty("pinionAxisFixed").GetBoolean(),
            p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("rackRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean(),
            MechanicalAuthoringJson.NullableString(p, "pinionPortId"), S(p, "parameterization"));
        Require(PiLength(p.GetProperty("pitchRadius")) == d.PitchRadius, "Cached pitch radius differs from exact current Z*p/(2*pi).");
        var advance = p.GetProperty("advancePerTurn");
        Require(S(advance, "kind") == "LinearPerAngular" && S(advance, "unit") == "mm/turn" && ReadDerived(advance.GetProperty("value")) == d.AdvancePerTurn.Value,
            "Cached advance differs from exact current Z*p.");
        return d;
    }
    private static void Output(Utf8JsonWriter w, PrismaticOutputDefinition o)
    {
        w.WriteStartObject(); w.WriteString("kind", "LinearPosition"); w.WriteString("key", o.Key); w.WriteString("linearDofId", o.LinearDofId);
        w.WriteString("bodyId", o.BodyId); w.WriteString("referencePointId", o.ReferencePointId); Integer(w, "terminalSign", o.TerminalSign); RotaryLinearJson.Quantity(w, "terminalDatum", o.TerminalDatum); w.WriteEndObject();
    }
    private static PrismaticOutputDefinition Output(JsonElement p)
    {
        Require(S(p, "kind") == "LinearPosition", "DimensionMismatch: prismatic output kind required.");
        return new(S(p, "key"), S(p, "linearDofId"), S(p, "bodyId"), S(p, "referencePointId"), I(p, "terminalSign"), RotaryLinearJson.Quantity(p.GetProperty("terminalDatum")));
    }
    private static void PiLength(Utf8JsonWriter w, string key, ExactPiLength length)
    {
        w.WriteStartObject(key); w.WriteString("representation", "rational-plus-inverse-pi"); w.WriteString("kind", "LinearPosition"); w.WriteString("unit", "mm");
        RotaryLinearJson.Derived(w, "rationalPart", length.RationalPartMm); RotaryLinearJson.Derived(w, "inversePiCoefficient", length.InversePiCoefficientMm); w.WriteEndObject();
    }
    private static ExactPiLength PiLength(JsonElement p)
    {
        Require(S(p, "representation") == "rational-plus-inverse-pi" && S(p, "kind") == "LinearPosition" && S(p, "unit") == "mm", "Exact r+c/pi mm descriptor required; decimal radius is not a CP input.");
        return ExactPiLength.FromCanonical(ReadDerived(p.GetProperty("rationalPart")), ReadDerived(p.GetProperty("inversePiCoefficient")));
    }
    private static void PiVector(Utf8JsonWriter w, string key, ExactPiVector3? vector)
    {
        if (vector is null) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WriteString("representation", "rational-plus-inverse-pi"); w.WriteString("kind", "LinearPosition"); w.WriteString("unit", "mm");
        Coefficients(w, "rationalPart", vector.Value.RationalPartMm); Coefficients(w, "inversePiCoefficient", vector.Value.InversePiCoefficientMm); w.WriteEndObject();
    }
    private static void Coefficients(Utf8JsonWriter w, string key, ExactVector3 v)
    { w.WriteStartObject(key); RotaryLinearJson.Derived(w, "x", v.X); RotaryLinearJson.Derived(w, "y", v.Y); RotaryLinearJson.Derived(w, "z", v.Z); w.WriteEndObject(); }
    private static ExactPiVector3 PiVector(JsonElement p)
    {
        Require(S(p, "representation") == "rational-plus-inverse-pi" && S(p, "kind") == "LinearPosition" && S(p, "unit") == "mm", "Exact r+c/pi mm vector required.");
        // Explicit input placement keeps the existing 128-digit declaration bound. Results can use 8192 digits.
        return new(InputCoefficients(p.GetProperty("rationalPart")), InputCoefficients(p.GetProperty("inversePiCoefficient")));
    }
    private static ExactVector3 InputCoefficients(JsonElement p) => new(F(p.GetProperty("x")), F(p.GetProperty("y")), F(p.GetProperty("z")));
    private static void PiFrame(Utf8JsonWriter w, string key, ExactPiFrame frame)
    { w.WriteStartObject(key); PiVector(w, "origin", frame.Origin); Vector(w, "x", frame.X); Vector(w, "y", frame.Y); Vector(w, "z", frame.Z); w.WriteEndObject(); }
    private static ExactPiFrame PiFrame(JsonElement p) => new(PiVector(p.GetProperty("origin")), Vector(p.GetProperty("x")), Vector(p.GetProperty("y")), Vector(p.GetProperty("z")));
    private static Rational ReadDerived(JsonElement p)
    {
        var n = S(p, "numerator"); var d = S(p, "denominator"); Require(n.Length <= 8192 && d.Length <= 8192, "Derived exact digit bound exceeded.");
        var value = new Rational(BigInteger.Parse(n, CultureInfo.InvariantCulture), BigInteger.Parse(d, CultureInfo.InvariantCulture));
        Require(value.Numerator.ToString(CultureInfo.InvariantCulture) == n && value.Denominator.ToString(CultureInfo.InvariantCulture) == d, "Canonical reduced fraction required."); return value;
    }
    private static void Start(Utf8JsonWriter w, string format) => MechanicalAuthoringJson.Start(w, format);
    private static byte[] Encode(Action<Utf8JsonWriter> write) => MechanicalAuthoringJson.Encode(write);
    private static JsonDocument Parse(byte[] bytes) => MechanicalAuthoringJson.Parse(bytes);
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
