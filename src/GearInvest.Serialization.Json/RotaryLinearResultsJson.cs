using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public static partial class RotaryLinearJson
{
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.rotary-linear-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));
    public static byte[] WriteAnalysis(RotaryLinearAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static RotaryLinearAnalysis VerifyAnalysis(RotaryLinearDraft draft, byte[] bytes)
    { var a = RotaryLinearAnalyzer.Analyze(draft); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored mixed analysis differs from fresh current definition analysis."); return a; }
    public static byte[] WriteEvaluation(RotaryLinearEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static byte[] WriteCompatibility(LeadScrewCompatibilityResult compatibility) => Guard(() => Encode(w => Compatibility(w, compatibility)));
    public static byte[] WriteComparison(LinearOutputEquivalenceResult comparison) => Guard(() => Encode(w => Comparison(w, comparison)));
    public static byte[] WriteComparisonRequest(LinearOutputComparisonRequest request) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.linear-output-comparison-request"); w.WritePropertyName("request"); ComparisonRequest(w, request); w.WriteEndObject(); }));
    public static LinearOutputComparisonRequest ReadComparisonRequest(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.linear-output-comparison-request");
        var r = ComparisonRequest(doc.RootElement.GetProperty("request")); Require(bytes.SequenceEqual(WriteComparisonRequest(r)), "Noncanonical linear comparison request."); return r;
    });
    internal static void Analysis(Utf8JsonWriter w, RotaryLinearAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("analysisId", a.AnalysisId); w.WriteString("policy", a.Policy); w.WriteString("motionDomain", a.MotionDomain);
        w.WriteString("definitionId", a.DefinitionId); w.WriteString("draftId", a.DraftId); MechanicalAuthoringJson.Long(w, "revision", a.Revision);
        w.WriteString("selectedInputId", a.SelectedInputId); w.WriteBoolean("isMechanicallyValid", a.IsMechanicallyValid);
        w.WritePropertyName("sourceAnalysis"); MechanicalAuthoringJson.Analysis(w, a.SourceAnalysis);
        w.WritePropertyName("localCompatibility"); Compatibility(w, a.LocalCompatibility);
        Array(w, "nodes", a.Nodes, (x, n) => { x.WriteStartObject(); x.WriteString("id", n.Id); x.WriteString("kind", n.Kind.ToString()); x.WriteBoolean("isPrescribed", n.IsPrescribed); x.WriteEndObject(); });
        Array(w, "admittedEdges", a.AdmittedEdges, (x, e) => { x.WriteStartObject(); x.WriteString("id", e.Id); x.WriteString("driverDofId", e.DriverDofId); x.WriteString("drivenDofId", e.DrivenDofId); DerivedQuantity(x, "transfer", e.Transfer); DerivedQuantity(x, "offset", e.Offset); x.WriteEndObject(); });
        Array(w, "admittedComponents", a.AdmittedComponents, (x, c) =>
        {
            x.WriteStartObject(); x.WriteString("parameterId", c.ParameterId); x.WriteString("determinacy", c.Determinacy.ToString());
            MechanicalAuthoringJson.Strings(x, "memberIds", c.MemberIds); MechanicalAuthoringJson.Strings(x, "constraintIds", c.ConstraintIds);
            MechanicalAuthoringJson.Strings(x, "prescribedInputIds", c.PrescribedInputIds);
            Array(x, "relations", c.Relations, (y, r) => { y.WriteStartObject(); y.WriteString("dofId", r.DofId); Affine(y, "relation", r.Relation); MechanicalAuthoringJson.Strings(y, "constraintPath", r.ConstraintPath); y.WriteEndObject(); });
            Array(x, "closures", c.Closures, (y, closure) => { y.WriteStartObject(); y.WriteString("constraintId", closure.ConstraintId); Derived(y, "coefficient", closure.Coefficient); Derived(y, "phase", closure.Phase); y.WriteBoolean("isRedundant", closure.IsRedundant); y.WriteEndObject(); });
            if (c.PinnedParameter.HasValue) Derived(x, "pinnedParameter", c.PinnedParameter.Value); else x.WriteNull("pinnedParameter");
            Array(x, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); x.WriteEndObject();
        });
        w.WritePropertyName("linearOutput"); var o = a.LinearOutput; w.WriteStartObject(); w.WritePropertyName("binding"); Output(w, o.Binding);
        w.WriteString("determinacy", o.Determinacy.ToString()); w.WriteBoolean("hasDeterminedMotion", o.HasDeterminedMotion);
        w.WriteBoolean("isDeclaredReachable", o.IsDeclaredReachable); w.WriteBoolean("isAdmittedReachable", o.IsAdmittedReachable);
        Affine(w, "screwRelation", o.ScrewRelation); Relation(w, "guideRelation", o.GuideRelation); Relation(w, "terminalRelation", o.TerminalRelation);
        DerivedVector(w, "screwPositiveAxis", o.ScrewPositiveAxis, "dimensionless"); DerivedVector(w, "guidePositiveAxis", o.GuidePositiveAxis, "dimensionless");
        DerivedVector(w, "worldGainMmPerRootTurn", o.WorldGainMmPerRootTurn, "mm/turn"); DerivedVector(w, "worldOffsetMm", o.WorldOffsetMm, "mm");
        DerivedInterval(w, "validRootInterval", o.ValidRootInterval); w.WriteString("requiredRange", o.RequiredRange.ToString()); w.WriteString("target", o.Target.ToString());
        MechanicalAuthoringJson.Strings(w, "constraintPath", o.ConstraintPath); MechanicalAuthoringJson.Strings(w, "declaredConstraintPath", o.DeclaredConstraintPath);
        MechanicalAuthoringJson.Strings(w, "blockedPrerequisites", o.BlockedPrerequisites); Array(w, "diagnostics", o.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
        Array(w, "checks", a.Checks, Check); Array(w, "diagnostics", a.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Compatibility(Utf8JsonWriter w, LeadScrewCompatibilityResult c)
    {
        Start(w, "gear-invest.lead-screw-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("transmissionPresent", c.TransmissionPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted);
        DerivedVector(w, "screwPositiveAxis", c.ScrewPositiveAxis, "dimensionless");
        if (c.Epsilon.HasValue) Derived(w, "epsilon", c.Epsilon.Value); else w.WriteNull("epsilon");
        if (c.Sigma.HasValue) Derived(w, "sigma", c.Sigma.Value); else w.WriteNull("sigma");
        if (c.SourcePortCoordinateSign.HasValue) Derived(w, "sourcePortCoordinateSign", c.SourcePortCoordinateSign.Value); else w.WriteNull("sourcePortCoordinateSign");
        DerivedQuantity(w, "localGain", c.LocalGain);
        Array(w, "facts", c.Facts, (x, f) => { x.WriteStartObject(); x.WriteString("key", f.Key); if (f.Expected.HasValue) Derived(x, "expected", f.Expected.Value); else x.WriteNull("expected"); if (f.Actual.HasValue) Derived(x, "actual", f.Actual.Value); else x.WriteNull("actual"); x.WriteEndObject(); });
        Array(w, "checks", c.Checks, Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Evaluation(Utf8JsonWriter w, RotaryLinearEvaluation e)
    {
        Start(w, "gear-invest.rotary-linear-evaluation"); w.WriteString("analysisId", e.AnalysisId); w.WriteString("status", e.Status.ToString()); DerivedQuantity(w, "input", e.Input);
        Array(w, "rotary", e.Rotary, (x, r) => { x.WriteStartObject(); x.WriteString("kind", "AngularPosition"); x.WriteString("shaftId", r.ShaftId);
            DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject(); });
        w.WritePropertyName("linear"); Position(w, e.Linear); w.WritePropertyName("diagnosticLinear"); Position(w, e.DiagnosticLinear);
        Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void Position(Utf8JsonWriter w, LinearPositionEvaluation? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("outputKey", p.OutputKey); w.WriteString("linearDofId", p.LinearDofId); w.WriteString("nutBodyId", p.NutBodyId);
        DerivedQuantity(w, "screwTurns", p.ScrewTurns); DerivedQuantity(w, "guidePosition", p.GuidePosition); DerivedQuantity(w, "terminalPosition", p.TerminalPosition);
        DerivedVector(w, "worldPositionMm", p.WorldPositionMm, "mm"); LengthFrame(w, "nutOrientation", p.NutOrientation);
        DerivedQuantity(w, "guideGain", p.GuideGain); DerivedQuantity(w, "terminalGain", p.TerminalGain); w.WriteEndObject();
    }
    internal static void ComparisonRequest(Utf8JsonWriter w, LinearOutputComparisonRequest r)
    {
        w.WriteStartObject(); w.WriteString("requestId", r.RequestId); w.WriteString("beforeOutputKey", r.BeforeOutputKey); w.WriteString("afterOutputKey", r.AfterOutputKey);
        w.WriteString("beforeInputId", r.BeforeInputId); w.WriteString("afterInputId", r.AfterInputId); Quantity(w, "alpha", r.Alpha); Quantity(w, "beta", r.Beta);
        Integer(w, "sign", r.Sign); Quantity(w, "delta", r.Delta); w.WriteString("mode", r.Mode.ToString()); Interval(w, "comparisonInterval", r.ComparisonInterval);
        w.WriteString("beforeReferencePointId", r.BeforeReferencePointId); w.WriteString("afterReferencePointId", r.AfterReferencePointId); w.WriteEndObject();
    }
    internal static LinearOutputComparisonRequest ComparisonRequest(JsonElement p)
    {
        var r = new LinearOutputComparisonRequest(S(p, "beforeOutputKey"), S(p, "afterOutputKey"), S(p, "beforeInputId"), S(p, "afterInputId"),
            Quantity(p.GetProperty("alpha")), Quantity(p.GetProperty("beta")), I(p, "sign"), Quantity(p.GetProperty("delta")), E<LinearOutputComparisonMode>(p, "mode"),
            p.GetProperty("comparisonInterval").ValueKind == JsonValueKind.Null ? null : Interval(p.GetProperty("comparisonInterval")),
            MechanicalAuthoringJson.NullableString(p, "beforeReferencePointId"), MechanicalAuthoringJson.NullableString(p, "afterReferencePointId"));
        Require(r.RequestId == S(p, "requestId"), "Linear comparison request identity mismatch."); return r;
    }
    internal static void Comparison(Utf8JsonWriter w, LinearOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.linear-output-comparison"); w.WriteString("comparisonId", r.ComparisonId); w.WritePropertyName("request"); ComparisonRequest(w, r.Request);
        w.WriteString("verdict", r.Verdict.ToString()); w.WriteString("scope", r.Scope); Relation(w, "beforeRelation", r.BeforeRelation); Relation(w, "mappedAfterRelation", r.MappedAfterRelation);
        DerivedVector(w, "beforeWorldGainMmPerRootTurn", r.BeforeWorldGainMmPerRootTurn, "mm/turn"); DerivedVector(w, "mappedAfterWorldGainMmPerRootTurn", r.MappedAfterWorldGainMmPerRootTurn, "mm/turn");
        DerivedVector(w, "beforeWorldOffsetMm", r.BeforeWorldOffsetMm, "mm"); DerivedVector(w, "mappedAfterWorldOffsetMm", r.MappedAfterWorldOffsetMm, "mm");
        w.WriteBoolean("operatingDomainsEqual", r.OperatingDomainsEqual);
        DerivedInterval(w, "beforeOperatingDomain", r.BeforeOperatingDomain); DerivedInterval(w, "mappedAfterOperatingDomain", r.MappedAfterOperatingDomain);
        if (r.BothFeasibleOnComparisonInterval.HasValue) w.WriteBoolean("bothFeasibleOnComparisonInterval", r.BothFeasibleOnComparisonInterval.Value); else w.WriteNull("bothFeasibleOnComparisonInterval");
        w.WriteString("beforeAnalysisId", r.BeforeAnalysisId); w.WriteString("afterAnalysisId", r.AfterAnalysisId); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Affine(Utf8JsonWriter w, string key, ExactAffineRelation? r)
    { if (!r.HasValue) { w.WriteNull(key); return; } w.WriteStartObject(key); Derived(w, "coefficient", r.Value.Coefficient); Derived(w, "phase", r.Value.Phase); w.WriteEndObject(); }
    internal static void Check(Utf8JsonWriter w, OrientedDomainCheck c)
    { w.WriteStartObject(); w.WriteString("domain", c.Domain); w.WriteString("subject", c.Subject); w.WriteString("verdict", c.Verdict.ToString()); w.WriteBoolean("required", c.Required); w.WriteString("detail", c.Detail); w.WriteEndObject(); }
}
