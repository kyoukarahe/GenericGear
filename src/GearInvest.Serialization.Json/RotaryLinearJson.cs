using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit dimensioned, bounded, AOT-safe 0.1 documents. No cached answer is an authority.</summary>
public static partial class RotaryLinearJson
{
    public const string DraftFormat = "gear-invest.rotary-linear-draft";
    public const string BatchFormat = "gear-invest.rotary-linear-edit-batch";
    public const string SessionFormat = "gear-invest.rotary-linear-edit-session";
    public const string AnalysisFormat = "gear-invest.rotary-linear-analysis";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024;
    public static byte[] WriteDraft(RotaryLinearDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static RotaryLinearDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown rotary-linear draft fields."); return draft; });
    public static byte[] WriteDefinition(RotaryLinearDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    internal static void Draft(Utf8JsonWriter w, RotaryLinearDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static RotaryLinearDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new RotaryLinearDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Mixed draft/definition identity mismatch."); return d;
    }
    internal static void Definition(Utf8JsonWriter w, RotaryLinearDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", "cardinal-grounded-lead-screw-v1");
        w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); Mapping(w, d.SourceMapping);
        w.WritePropertyName("device"); Device(w, d.Device); w.WritePropertyName("output"); Output(w, d.Output);
        w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static RotaryLinearDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == "cardinal-grounded-lead-screw-v1", "Unsupported rotary-linear profile.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), Mapping(p.GetProperty("sourceMapping")),
            Device(p.GetProperty("device")), Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")),
            Items(p, "requiredValidationDomains", 16).Select(x => x.GetString()!));
    }
    internal static void Mapping(Utf8JsonWriter w, SourceLengthMapping? m)
    {
        if (m is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("sourceCoordinates", "original-unitless");
        w.WriteString("lengthUnit", "mm"); Fraction(w, "millimetersPerSourceUnit", m.MillimetersPerSourceUnit);
        Frame(w, "poseMm", m.PoseMm); w.WriteEndObject();
    }
    internal static SourceLengthMapping? Mapping(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Null) return null;
        Require(S(p, "sourceCoordinates") == "original-unitless" && S(p, "lengthUnit") == "mm", "Source mapping unit contract mismatch.");
        return new(F(p.GetProperty("millimetersPerSourceUnit")), MechanicalAuthoringJson.LooseFrame(p.GetProperty("poseMm")));
    }
    internal static void Device(Utf8JsonWriter w, GroundedLeadScrewDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("screwShaftId", d.ScrewShaftId); w.WriteString("screwBodyId", d.ScrewBodyId);
        w.WriteString("nutBodyId", d.NutBodyId); w.WriteString("guideId", d.GuideId); w.WriteString("linearDofId", d.LinearDofId); w.WriteString("screwPortId", d.ScrewPortId);
        Vector(w, "physicalAxis", d.PhysicalAxis); LengthVector(w, "screwAxialDatumMm", d.ScrewAxialDatumMm); LengthFrame(w, "guideFrameMm", d.GuideFrameMm);
        Quantity(w, "lead", d.Lead); Quantity(w, "screwReferenceTurns", d.ScrewReferenceTurns); Quantity(w, "nutReferencePosition", d.NutReferencePosition);
        Interval(w, "guideInterval", d.GuideInterval); Interval(w, "engagementInterval", d.EngagementInterval); Integer(w, "handedness", d.Handedness);
        w.WriteBoolean("transmissionPresent", d.TransmissionPresent); w.WriteBoolean("screwAxiallyFixed", d.ScrewAxiallyFixed);
        w.WriteBoolean("guidePresent", d.GuidePresent); w.WriteBoolean("nutRotationFixed", d.NutRotationFixed); w.WriteBoolean("transverseMotionFixed", d.TransverseMotionFixed); w.WriteEndObject();
    }
    internal static GroundedLeadScrewDefinition Device(JsonElement p) => new(S(p, "id"), S(p, "screwShaftId"), S(p, "screwBodyId"), S(p, "nutBodyId"), S(p, "guideId"), S(p, "linearDofId"),
        Vector(p.GetProperty("physicalAxis")), LengthVector(p.GetProperty("screwAxialDatumMm")), LengthFrame(p.GetProperty("guideFrameMm")),
        Quantity(p.GetProperty("lead")), Quantity(p.GetProperty("screwReferenceTurns")), Quantity(p.GetProperty("nutReferencePosition")),
        Interval(p.GetProperty("guideInterval")), Interval(p.GetProperty("engagementInterval")), I(p, "handedness"),
        p.GetProperty("transmissionPresent").GetBoolean(), p.GetProperty("screwAxiallyFixed").GetBoolean(), p.GetProperty("guidePresent").GetBoolean(),
        p.GetProperty("nutRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "screwPortId"));
    internal static void Output(Utf8JsonWriter w, LinearOutputDefinition o)
    {
        w.WriteStartObject(); w.WriteString("kind", "LinearPosition"); w.WriteString("key", o.Key); w.WriteString("linearDofId", o.LinearDofId);
        w.WriteString("nutBodyId", o.NutBodyId); w.WriteString("referencePointId", o.ReferencePointId); Integer(w, "terminalSign", o.TerminalSign); Quantity(w, "terminalDatum", o.TerminalDatum); w.WriteEndObject();
    }
    internal static LinearOutputDefinition Output(JsonElement p)
    {
        Require(S(p, "kind") == "LinearPosition", "DimensionMismatch: linear output kind required.");
        return new(S(p, "key"), S(p, "linearDofId"), S(p, "nutBodyId"), S(p, "referencePointId"), I(p, "terminalSign"), Quantity(p.GetProperty("terminalDatum")));
    }
    internal static void Requirement(Utf8JsonWriter w, LinearOutputRequirement r)
    {
        w.WriteStartObject(); Quantity(w, "requiredGain", r.RequiredGain); Quantity(w, "requiredReferencePosition", r.RequiredReferencePosition);
        w.WriteString("referenceInput", "zero-global-root-turns"); Interval(w, "requiredRootInterval", r.RequiredRootInterval); w.WriteEndObject();
    }
    internal static LinearOutputRequirement Requirement(JsonElement p)
    {
        Require(S(p, "referenceInput") == "zero-global-root-turns", "Explicit reference-input convention required.");
        return new(OptionalQuantity(p, "requiredGain"), OptionalQuantity(p, "requiredReferencePosition"), Interval(p.GetProperty("requiredRootInterval")));
    }
    internal static void Quantity(Utf8JsonWriter w, string key, ExactQuantity? q) { w.WritePropertyName(key); if (q is null) w.WriteNullValue(); else Quantity(w, q.Value); }
    internal static void Quantity(Utf8JsonWriter w, ExactQuantity q)
    { w.WriteStartObject(); w.WriteString("kind", q.Kind.ToString()); w.WriteString("unit", q.Unit); Fraction(w, "value", q.Value); w.WriteEndObject(); }
    internal static ExactQuantity Quantity(JsonElement p)
    {
        var q = new ExactQuantity(E<QuantityKind>(p, "kind"), F(p.GetProperty("value")), S(p, "unit"));
        Require(q.Unit == S(p, "unit") && q.Value == F(p.GetProperty("value")), "Canonical normalized quantity required; input conversion belongs to typed construction."); return q;
    }
    internal static ExactQuantity? OptionalQuantity(JsonElement p, string key) => p.GetProperty(key).ValueKind == JsonValueKind.Null ? null : Quantity(p.GetProperty(key));
    internal static void Interval(Utf8JsonWriter w, string key, ExactQuantityInterval? i)
    { w.WritePropertyName(key); if (i is null) { w.WriteNullValue(); return; } w.WriteStartObject(); w.WriteString("boundary", "closed"); Quantity(w, "lower", i.Lower); Quantity(w, "upper", i.Upper); w.WriteEndObject(); }
    internal static ExactQuantityInterval Interval(JsonElement p)
    { Require(S(p, "boundary") == "closed", "Finite closed interval required."); return new(Quantity(p.GetProperty("lower")), Quantity(p.GetProperty("upper"))); }
    internal static void LengthVector(Utf8JsonWriter w, string key, ExactVector3 v)
    { w.WriteStartObject(key); w.WriteString("kind", "LinearPosition"); w.WriteString("unit", "mm"); Vector(w, "components", v); w.WriteEndObject(); }
    internal static ExactVector3 LengthVector(JsonElement p)
    { Require(S(p, "kind") == "LinearPosition" && S(p, "unit") == "mm", "DimensionMismatch: canonical mm position vector required."); return Vector(p.GetProperty("components")); }
    private static void LengthFrame(Utf8JsonWriter w, string key, OrientedFrame f)
    { w.WriteStartObject(key); w.WriteString("originUnit", "mm"); Frame(w, "frame", f); w.WriteEndObject(); }
    private static OrientedFrame LengthFrame(JsonElement p)
    { Require(S(p, "originUnit") == "mm", "Explicit mm frame origin required."); return MechanicalAuthoringJson.LooseFrame(p.GetProperty("frame")); }
    internal static void Start(Utf8JsonWriter w, string format) => MechanicalAuthoringJson.Start(w, format);
    internal static byte[] Encode(Action<Utf8JsonWriter> write) => MechanicalAuthoringJson.Encode(write);
    internal static JsonDocument Parse(byte[] bytes) => MechanicalAuthoringJson.Parse(bytes);
    internal static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
    internal static void Derived(Utf8JsonWriter w, string key, Rational r)
    {
        var n = r.Numerator.ToString(CultureInfo.InvariantCulture); var d = r.Denominator.ToString(CultureInfo.InvariantCulture);
        Require(n.Length <= 8192 && d.Length <= 8192, "Derived exact digit bound exceeded.");
        w.WriteStartObject(key); w.WriteString("numerator", n); w.WriteString("denominator", d); w.WriteEndObject();
    }
    internal static void DerivedQuantity(Utf8JsonWriter w, string key, ExactQuantity? q)
    {
        if (q is null) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WriteString("kind", q.Value.Kind.ToString()); w.WriteString("unit", q.Value.Unit); Derived(w, "value", q.Value.Value); w.WriteEndObject();
    }
    internal static void DerivedVector(Utf8JsonWriter w, string key, ExactVector3? vector, string unit)
    {
        if (!vector.HasValue) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WriteString("unit", unit); Derived(w, "x", vector.Value.X); Derived(w, "y", vector.Value.Y); Derived(w, "z", vector.Value.Z); w.WriteEndObject();
    }
    internal static void DerivedInterval(Utf8JsonWriter w, string key, ExactQuantityInterval? i)
    { if (i is null) { w.WriteNull(key); return; } w.WriteStartObject(key); w.WriteString("boundary", "closed"); DerivedQuantity(w, "lower", i.Lower); DerivedQuantity(w, "upper", i.Upper); w.WriteEndObject(); }
    internal static void Relation(Utf8JsonWriter w, string key, DimensionedAffineRelation? r)
    { if (r is null) { w.WriteNull(key); return; } w.WriteStartObject(key); DerivedQuantity(w, "gain", r.Value.Gain); DerivedQuantity(w, "offset", r.Value.Offset); w.WriteEndObject(); }
}
