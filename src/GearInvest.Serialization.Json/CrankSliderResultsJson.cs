using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class CrankSliderJson
{
    public static byte[] WriteAnalysis(CrankSliderAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static CrankSliderAnalysis VerifyAnalysis(CrankSliderDraft draft, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var a = CrankSliderAnalyzer.Analyze(draft); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored crank-slider analysis differs from fresh current definition."); return a; });
    public static byte[] WriteEvaluation(CrankSliderEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static CrankSliderEvaluation VerifyEvaluation(CrankSliderAnalysis analysis, ExactQuantity root, CrankSliderNumericRequest request, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var e = CrankSliderAnalyzer.Evaluate(analysis, root, request); Require(bytes.SequenceEqual(WriteEvaluation(e)), "Stored crank-slider evaluation differs from fresh explicit root and numeric request."); return e; });
    public static byte[] WriteCompatibility(CrankSliderCompatibilityResult result) => Guard(() => Encode(w => Compatibility(w, result)));
    public static byte[] WriteComparison(CrankSliderOutputEquivalenceResult result) => Guard(() => Encode(w => Comparison(w, result)));
    public static byte[] WriteComparisonRequest(CrankSliderOutputComparisonRequest request) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.crank-slider-comparison-request"); w.WritePropertyName("request"); ComparisonRequest(w, request); w.WriteEndObject(); }));
    public static CrankSliderOutputComparisonRequest ReadComparisonRequest(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.crank-slider-comparison-request"); var r = ComparisonRequest(doc.RootElement.GetProperty("request"));
        Require(bytes.SequenceEqual(WriteComparisonRequest(r)), "Noncanonical crank-slider comparison request."); return r;
    });
    public static byte[] WriteNumericRequest(CrankSliderAnalysis analysis, ExactQuantity root, CrankSliderNumericRequest request) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.crank-slider-numeric-request"); BoundNumericRequest(w, analysis.DefinitionId, analysis.AnalysisId, root, analysis.CreatePoseRecipe(root)?.RecipeId, request); w.WriteEndObject(); }));
    public static CrankSliderNumericRequest ReadNumericRequest(CrankSliderAnalysis analysis, ExactQuantity root, byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.crank-slider-numeric-request"); var request = NumericOptions(doc.RootElement.GetProperty("options"));
        Require(bytes.SequenceEqual(WriteNumericRequest(analysis, root, request)), "Numeric request differs from explicit definition/analysis/root/recipe binding."); return request;
    });
    private static string NumericRequestId(string definition, string analysis, ExactQuantity root, string? recipe, CrankSliderNumericRequest request) =>
        Hash(System.Text.Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("crank-slider-bound-numeric-request-v1", definition, analysis,
            root.Kind.ToString(), root.Unit, root.Value.ToString(), recipe ?? "", request.CanonicalRepresentation)));
    private static void BoundNumericRequest(Utf8JsonWriter w, string definition, string analysis, ExactQuantity root, string? recipe, CrankSliderNumericRequest request)
    {
        w.WriteString("bindingPolicy", "definition-analysis-unwrapped-root-recipe-numeric-options-v1");
        w.WriteString("requestId", NumericRequestId(definition, analysis, root, recipe, request)); w.WriteString("definitionId", definition); w.WriteString("analysisId", analysis);
        R.Quantity(w, "rootTurns", root); w.WriteString("recipeId", recipe); w.WritePropertyName("options"); NumericOptions(w, request);
    }
    private static void Analysis(Utf8JsonWriter w, CrankSliderAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("analysisId", a.AnalysisId); w.WriteString("definitionId", a.DefinitionId); w.WriteString("draftId", a.DraftId);
        MechanicalAuthoringJson.Long(w, "revision", a.Revision); w.WriteString("policy", a.Policy); w.WriteString("motionDomain", a.MotionDomain); w.WriteString("selectedInputId", a.SelectedInputId);
        w.WriteBoolean("isMechanicallyValid", a.IsMechanicallyValid); w.WriteString("exportAdmission", a.ExportAdmission.ToString());
        w.WritePropertyName("sourceAnalysis"); MechanicalAuthoringJson.Analysis(w, a.SourceAnalysis); w.WritePropertyName("localCompatibility"); Compatibility(w, a.LocalCompatibility);
        w.WriteString("determinacy", a.Determinacy.ToString()); R.Affine(w, "sourceRelation", a.SourceRelation); w.WritePropertyName("descriptor"); Descriptor(w, a.Descriptor);
        w.WriteBoolean("hasDeterminedMotion", a.HasDeterminedMotion); w.WriteBoolean("hasDeterminedCrankMotion", a.HasDeterminedCrankMotion);
        w.WriteString("guideTravelCoverage", a.GuideTravelCoverage.ToString()); w.WriteString("target", a.Target.ToString());
        w.WriteBoolean("isDeclaredReachable", a.IsDeclaredReachable); w.WriteBoolean("isAdmittedReachable", a.IsAdmittedReachable);
        MechanicalAuthoringJson.Strings(w, "constraintPath", a.ConstraintPath); MechanicalAuthoringJson.Strings(w, "declaredConstraintPath", a.DeclaredConstraintPath);
        MechanicalAuthoringJson.Strings(w, "blockedPrerequisites", a.BlockedPrerequisites); Array(w, "checks", a.Checks, R.Check); Array(w, "diagnostics", a.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Compatibility(Utf8JsonWriter w, CrankSliderCompatibilityResult c)
    {
        Start(w, "gear-invest.crank-slider-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("transmissionPresent", c.TransmissionPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted); w.WriteBoolean("hasValidCrankMounting", c.HasValidCrankMounting);
        if (c.MappedShaftFrameMm is null) w.WriteNull("mappedShaftFrameMm"); else DerivedFrame(w, "mappedShaftFrameMm", c.MappedShaftFrameMm);
        OptionalDerived(w, "gammaTurns", c.GammaTurns); OptionalDerived(w, "epsilon", c.Epsilon); w.WritePropertyName("envelope"); Envelope(w, c.Envelope);
        Array(w, "facts", c.Facts, (x, f) => { x.WriteStartObject(); x.WriteString("key", f.Key); OptionalDerived(x, "expected", f.Expected); OptionalDerived(x, "actual", f.Actual); x.WriteEndObject(); });
        Array(w, "checks", c.Checks, R.Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Descriptor(Utf8JsonWriter w, CrankSliderMotionDescriptor? d)
    {
        if (d is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("descriptorId", d.DescriptorId); w.WriteString("policy", d.Policy); w.WriteString("positionRecipe", d.PositionRecipe);
        R.Derived(w, "crankRadiusMm", d.CrankRadiusMm); R.Derived(w, "rodLengthMm", d.RodLengthMm); R.Derived(w, "guideDatumMm", d.GuideDatumMm); R.Derived(w, "guideOffsetMm", d.GuideOffsetMm);
        R.DerivedVector(w, "pivotMm", d.PivotMm, "mm"); R.DerivedVector(w, "guideOriginMm", d.GuideOriginMm, "mm"); R.DerivedVector(w, "guideDirection", d.GuideDirection, "dimensionless");
        R.DerivedVector(w, "inPlanePerpendicular", d.InPlanePerpendicular, "dimensionless"); R.DerivedVector(w, "planeNormal", d.PlaneNormal, "dimensionless");
        DerivedFrame(w, "sliderOrientation", d.SliderOrientation);
        Integer(w, "branch", d.Branch); Integer(w, "terminalSign", d.TerminalSign); R.Derived(w, "terminalDatumMm", d.TerminalDatumMm); R.Derived(w, "epsilon", d.Epsilon); R.Derived(w, "gammaTurns", d.GammaTurns);
        R.Affine(w, "sourceRelation", d.SourceRelation); R.Affine(w, "physicalPhaseRelation", d.PhysicalPhaseRelation); OptionalDerived(w, "positionRepeatRootTurns", d.PositionRepeatRootTurns);
        w.WritePropertyName("envelope"); Envelope(w, d.Envelope); w.WriteEndObject();
    }
    private static void Envelope(Utf8JsonWriter w, CrankSliderEnvelope? e)
    {
        if (e is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); Radical(w, "lower", e.Lower); Radical(w, "upper", e.Upper); R.Derived(w, "strictMarginMm", e.StrictMarginMm);
        R.Derived(w, "minimumRadicandMmSquared", e.MinimumRadicandMmSquared); R.Derived(w, "AminusMmSquared", e.AminusMmSquared); R.Derived(w, "AplusMmSquared", e.AplusMmSquared);
        OptionalDerived(w, "exactStrokeMm", e.ExactStrokeMm); w.WriteString("strokeRecipe", e.StrokeRecipe); w.WriteString("attainmentProof", e.AttainmentProof); w.WriteEndObject();
    }
    private static void Radical(Utf8JsonWriter w, string key, CrankSliderRadicalLength? r)
    {
        if (r is null) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WriteString("representation", "rational-plus-signed-positive-square-root"); w.WriteString("unit", "mm"); R.Derived(w, "rationalPartMm", r.RationalPartMm);
        Integer(w, "rootSign", r.RootSign); R.Derived(w, "radicandMmSquared", r.RadicandMmSquared); w.WriteBoolean("isRational", r.IsRational); OptionalDerived(w, "exactMillimeters", r.ExactMillimeters); w.WriteEndObject();
    }
    internal static void PoseRecipe(Utf8JsonWriter w, CrankSliderPoseRecipe? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("recipeId", p.RecipeId); w.WriteString("descriptorId", p.Descriptor.DescriptorId); R.DerivedQuantity(w, "root", p.Root);
        R.Derived(w, "sourceTurns", p.SourceTurns); R.Derived(w, "physicalPhaseTurns", p.PhysicalPhaseTurns); R.Derived(w, "reducedPhaseTurns", p.ReducedPhaseTurns);
        w.WriteString("rodOrientationRecipe", p.RodOrientationRecipe); DerivedFrame(w, "sliderOrientation", p.SliderOrientation);
        Radical(w, "exactGuidePosition", p.ExactGuidePosition); Radical(w, "exactTerminalPosition", p.ExactTerminalPosition); w.WriteEndObject();
    }
    private static void DerivedFrame(Utf8JsonWriter w, string key, OrientedFrame f)
    { w.WriteStartObject(key); R.DerivedVector(w, "origin", f.Origin, "mm"); R.DerivedVector(w, "x", f.X, "dimensionless"); R.DerivedVector(w, "y", f.Y, "dimensionless"); R.DerivedVector(w, "z", f.Z, "dimensionless"); w.WriteEndObject(); }
    private static void OptionalDerived(Utf8JsonWriter w, string key, Rational? value) { if (value.HasValue) R.Derived(w, key, value.Value); else w.WriteNull(key); }
    private static void Evaluation(Utf8JsonWriter w, CrankSliderEvaluation e)
    {
        Start(w, "gear-invest.crank-slider-evaluation"); w.WriteString("evaluationId", e.EvaluationId);
        BoundNumericRequest(w, e.DefinitionId, e.AnalysisId, e.RootTurns, e.Recipe?.RecipeId, e.Request);
        w.WriteString("status", e.Status.ToString()); w.WriteString("travelDecision", e.TravelDecision.ToString()); w.WriteBoolean("isSuccess", e.IsSuccess);
        Array(w, "rotary", e.Rotary, (x, r) => { x.WriteStartObject(); x.WriteString("shaftId", r.ShaftId); R.DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            R.DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); R.DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject(); });
        w.WritePropertyName("crank"); if (e.Crank is null) w.WriteNullValue(); else
        {
            var c = e.Crank; w.WriteStartObject(); w.WriteString("bodyId", c.BodyId); R.DerivedVector(w, "pivotMm", c.PivotMm, "mm"); R.DerivedVector(w, "positiveAxis", c.PositiveAxis, "dimensionless");
            R.DerivedVector(w, "zeroRay", c.ZeroRay, "dimensionless"); R.Derived(w, "sourceTurns", c.SourceTurns); R.Derived(w, "mountingTurns", c.MountingTurns); OptionalDerived(w, "physicalPhaseTurns", c.PhysicalPhaseTurns); w.WriteEndObject();
        }
        w.WritePropertyName("recipe"); PoseRecipe(w, e.Recipe); w.WritePropertyName("numericComputation"); NumericComputation(w, e.NumericComputation);
        w.WritePropertyName("pose"); NumericPose(w, e.Pose); w.WritePropertyName("diagnosticPose"); NumericPose(w, e.DiagnosticPose);
        Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void ComparisonRequest(Utf8JsonWriter w, CrankSliderOutputComparisonRequest r)
    {
        w.WriteStartObject(); w.WriteString("requestId", r.RequestId); w.WriteString("beforeOutputKey", r.BeforeOutputKey); w.WriteString("afterOutputKey", r.AfterOutputKey);
        w.WriteString("beforeInputId", r.BeforeInputId); w.WriteString("afterInputId", r.AfterInputId); R.Quantity(w, "alpha", r.Alpha); R.Quantity(w, "beta", r.Beta); Integer(w, "sign", r.Sign); R.Quantity(w, "delta", r.Delta);
        w.WriteString("mode", r.Mode.ToString()); w.WriteString("domain", r.Domain); w.WriteString("proofPolicy", r.ProofPolicy); w.WriteString("beforeReferencePointId", r.BeforeReferencePointId); w.WriteString("afterReferencePointId", r.AfterReferencePointId);
        if (r.AfterWorldToBeforeWorldMm is null) w.WriteNull("afterWorldToBeforeWorldMm"); else LengthFrame(w, "afterWorldToBeforeWorldMm", r.AfterWorldToBeforeWorldMm);
        Array(w, "witnessRoots", r.WitnessRoots, (x, root) => { x.WriteStartObject(); R.Quantity(x, "root", root); x.WriteEndObject(); }); w.WritePropertyName("numericRequest"); NumericOptions(w, r.NumericRequest); w.WriteEndObject();
    }
    private static CrankSliderOutputComparisonRequest ComparisonRequest(JsonElement p)
    {
        var r = new CrankSliderOutputComparisonRequest(S(p, "beforeOutputKey"), S(p, "afterOutputKey"), S(p, "beforeInputId"), S(p, "afterInputId"),
            R.Quantity(p.GetProperty("alpha")), R.Quantity(p.GetProperty("beta")), I(p, "sign"), R.Quantity(p.GetProperty("delta")), E<CrankSliderOutputComparisonMode>(p, "mode"),
            MechanicalAuthoringJson.NullableString(p, "beforeReferencePointId"), MechanicalAuthoringJson.NullableString(p, "afterReferencePointId"),
            p.GetProperty("afterWorldToBeforeWorldMm").ValueKind == JsonValueKind.Null ? null : LengthFrame(p.GetProperty("afterWorldToBeforeWorldMm")),
            Items(p, "witnessRoots", 32).Select(x => R.Quantity(x.GetProperty("root"))), NumericOptions(p.GetProperty("numericRequest")), S(p, "domain"), S(p, "proofPolicy"));
        Require(r.RequestId == S(p, "requestId"), "Nonlinear comparison request identity mismatch."); return r;
    }
    internal static void Comparison(Utf8JsonWriter w, CrankSliderOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.crank-slider-output-comparison"); w.WriteString("resultId", r.ResultId); w.WritePropertyName("request"); ComparisonRequest(w, r.Request);
        w.WriteString("beforeAnalysisId", r.BeforeAnalysisId); w.WriteString("afterAnalysisId", r.AfterAnalysisId); w.WriteString("verdict", r.Verdict.ToString()); w.WriteString("proofRule", r.ProofRule);
        w.WriteString("policy", r.Policy); w.WriteString("scope", r.Scope); if (r.OperatingDomainsEqual.HasValue) w.WriteBoolean("operatingDomainsEqual", r.OperatingDomainsEqual.Value); else w.WriteNull("operatingDomainsEqual");
        w.WriteString("beforeFullCycleTravel", r.BeforeFullCycleTravel.ToString()); w.WriteString("afterFullCycleTravel", r.AfterFullCycleTravel.ToString()); w.WriteString("beforeExportAdmission", r.BeforeExportAdmission.ToString()); w.WriteString("afterExportAdmission", r.AfterExportAdmission.ToString());
        Array(w, "witnesses", r.Witnesses, (x, v) => { x.WriteStartObject(); R.DerivedQuantity(x, "beforeRoot", v.BeforeRoot); R.DerivedQuantity(x, "afterRoot", v.AfterRoot); x.WriteString("coordinate", v.Coordinate);
            Interval(x, "before", v.Before, "mm"); Interval(x, "mappedAfter", v.MappedAfter, "mm"); x.WriteBoolean("isSeparating", v.IsSeparating); x.WriteEndObject(); });
        Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.crank-slider-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));

    private static void NumericOptions(Utf8JsonWriter w, CrankSliderNumericRequest r)
    {
        w.WriteStartObject(); w.WriteString("policy", r.Policy); R.Quantity(w, "absoluteWidth", r.AbsoluteWidth);
        Integer(w, "maximumWork", r.MaximumWork); Integer(w, "maximumPrecisionBits", r.MaximumPrecisionBits); Integer(w, "maximumRefinements", r.MaximumRefinements); w.WriteEndObject();
    }
    private static CrankSliderNumericRequest NumericOptions(JsonElement p) => new(R.Quantity(p.GetProperty("absoluteWidth")), I(p, "maximumWork"), I(p, "maximumPrecisionBits"), I(p, "maximumRefinements"), MechanicalAuthoringJson.NullableString(p, "policy")!);
    internal static void NumericComputation(Utf8JsonWriter w, CrankSliderNumericComputation? c)
    {
        if (c is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("status", c.Status.ToString()); Integer(w, "work", c.Work); Integer(w, "precisionBits", c.PrecisionBits); Integer(w, "refinements", c.Refinements);
        w.WriteBoolean("isAvailable", c.IsAvailable); w.WriteString("detail", c.Detail);
        w.WritePropertyName("pose"); NumericPose(w, c.Pose); w.WritePropertyName("diagnosticPose"); NumericPose(w, c.DiagnosticPose); w.WriteEndObject();
    }
    private static void NumericPose(Utf8JsonWriter w, CrankSliderNumericPose? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); Interval(w, "guidePositionMm", p.GuidePositionMm, "mm"); Interval(w, "terminalPositionMm", p.TerminalPositionMm, "mm");
        VectorInterval(w, "crankPinMm", p.CrankPinMm, "mm"); VectorInterval(w, "sliderPinMm", p.SliderPinMm, "mm"); VectorInterval(w, "rodMidpointMm", p.RodMidpointMm, "mm");
        VectorInterval(w, "rodDirection", p.RodDirection, "dimensionless"); VectorInterval(w, "rodTransverseDirection", p.RodTransverseDirection, "dimensionless");
        Interval(w, "sin", p.Sin, "dimensionless"); Interval(w, "cos", p.Cos, "dimensionless"); Interval(w, "radicandMmSquared", p.RadicandMmSquared, "mm2"); Interval(w, "positiveRootMm", p.PositiveRootMm, "mm"); w.WriteEndObject();
    }
    private static void Interval(Utf8JsonWriter w, string key, CrankSliderInterval i, string unit)
    {
        w.WriteStartObject(key); w.WriteString("representation", "certified-rational-enclosure"); w.WriteString("unit", unit); w.WriteString("boundary", "closed");
        R.Derived(w, "lower", i.Lower); R.Derived(w, "upper", i.Upper); R.Derived(w, "width", i.Width); w.WriteBoolean("isExact", i.IsExact); w.WriteEndObject();
    }
    private static void VectorInterval(Utf8JsonWriter w, string key, CrankSliderVectorInterval v, string unit)
    { w.WriteStartObject(key); Interval(w, "x", v.X, unit); Interval(w, "y", v.Y, unit); Interval(w, "z", v.Z, unit); w.WriteEndObject(); }
}
