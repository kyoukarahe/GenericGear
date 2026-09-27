using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class CamFollowerJson
{
    public static byte[] WriteAnalysis(CamFollowerAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static CamFollowerAnalysis VerifyAnalysis(CamFollowerDraft draft, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var a = CamFollowerAnalyzer.Analyze(draft, ProofOptions(parsed.RootElement.GetProperty("proofRequest"))); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored cam-follower analysis differs from fresh current definition."); return a; });
    public static CamFollowerAnalysis VerifyAnalysis(CamFollowerDraft draft, CamGeometryProofRequest proofRequest, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var a = CamFollowerAnalyzer.Analyze(draft, proofRequest); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored cam-follower analysis differs from the explicit current definition and proof request."); return a; });
    public static byte[] WriteEvaluation(CamFollowerEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static CamFollowerEvaluation VerifyEvaluation(CamFollowerAnalysis analysis, ExactQuantity root, CamNumericRequest request, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var e = CamFollowerAnalyzer.Evaluate(analysis, root, request); Require(bytes.SequenceEqual(WriteEvaluation(e)), "Stored cam-follower evaluation differs from fresh explicit root and numeric request."); return e; });
    public static byte[] WriteCompatibility(CamFollowerCompatibilityResult result) => Guard(() => Encode(w => Compatibility(w, result)));
    public static CamFollowerCompatibilityResult VerifyCompatibility(CamFollowerDraft draft, CamGeometryProofRequest proofRequest, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var result = CamFollowerAnalyzer.Query(draft, proofRequest); Require(bytes.SequenceEqual(WriteCompatibility(result)), "Stored cam-follower compatibility differs from fresh explicit request."); return result; });
    public static byte[] WriteComparison(CamFollowerOutputEquivalenceResult result) => Guard(() => Encode(w => Comparison(w, result)));
    public static CamFollowerOutputEquivalenceResult VerifyComparison(CamFollowerAnalysis before, CamFollowerAnalysis after, CamFollowerOutputComparisonRequest request, byte[] bytes) => Guard(() =>
    { using var parsed = Parse(bytes); var result = CamFollowerOutputComparer.Compare(before, after, request); Require(bytes.SequenceEqual(WriteComparison(result)), "Stored cam-follower comparison differs from fresh explicit correspondence."); return result; });
    public static byte[] WriteComparisonRequest(CamFollowerOutputComparisonRequest request) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.cam-follower-comparison-request"); w.WritePropertyName("request"); ComparisonRequest(w, request); w.WriteEndObject(); }));
    public static CamFollowerOutputComparisonRequest ReadComparisonRequest(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.cam-follower-comparison-request"); var r = ComparisonRequest(doc.RootElement.GetProperty("request"));
        Require(bytes.SequenceEqual(WriteComparisonRequest(r)), "Noncanonical cam-follower comparison request."); return r;
    });
    public static byte[] WriteNumericRequest(CamFollowerAnalysis analysis, ExactQuantity root, CamNumericRequest request) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.cam-follower-numeric-request"); BoundNumericRequest(w, analysis.DefinitionId, analysis.AnalysisId, root, analysis.CreatePoseRecipe(root)?.RecipeId, request); w.WriteEndObject(); }));
    public static CamNumericRequest ReadNumericRequest(CamFollowerAnalysis analysis, ExactQuantity root, byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.cam-follower-numeric-request"); var request = NumericOptions(doc.RootElement.GetProperty("options"));
        Require(bytes.SequenceEqual(WriteNumericRequest(analysis, root, request)), "Numeric request differs from explicit definition/analysis/root/recipe binding."); return request;
    });
    private static string NumericRequestId(string definition, string analysis, ExactQuantity root, string? recipe, CamNumericRequest request) =>
        Hash(System.Text.Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("cam-follower-bound-numeric-request-v1", definition, analysis,
            root.Kind.ToString(), root.Unit, root.Value.ToString(), recipe ?? "", request.CanonicalRepresentation)));
    private static void BoundNumericRequest(Utf8JsonWriter w, string definition, string analysis, ExactQuantity root, string? recipe, CamNumericRequest request)
    {
        w.WriteString("bindingPolicy", "definition-analysis-unwrapped-root-recipe-numeric-options-v1");
        w.WriteString("requestId", NumericRequestId(definition, analysis, root, recipe, request)); w.WriteString("definitionId", definition); w.WriteString("analysisId", analysis);
        R.Quantity(w, "rootTurns", root); w.WriteString("recipeId", recipe); w.WritePropertyName("options"); NumericOptions(w, request);
    }
    private static void Analysis(Utf8JsonWriter w, CamFollowerAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("analysisId", a.AnalysisId); w.WriteString("definitionId", a.DefinitionId); w.WriteString("draftId", a.DraftId);
        MechanicalAuthoringJson.Long(w, "revision", a.Revision); w.WriteString("policy", a.Policy); w.WriteString("motionDomain", a.MotionDomain); w.WriteString("selectedInputId", a.SelectedInputId);
        w.WritePropertyName("proofRequest"); ProofOptions(w, a.ProofRequest); w.WriteBoolean("isMechanicallyValid", a.IsMechanicallyValid); w.WriteString("exportAdmission", a.ExportAdmission.ToString());
        w.WritePropertyName("sourceAnalysis"); MechanicalAuthoringJson.Analysis(w, a.SourceAnalysis); w.WritePropertyName("localCompatibility"); Compatibility(w, a.LocalCompatibility);
        w.WriteString("determinacy", a.Determinacy.ToString()); R.Affine(w, "sourceRelation", a.SourceRelation); w.WritePropertyName("descriptor"); Descriptor(w, a.Descriptor);
        w.WriteBoolean("hasDeterminedMotion", a.HasDeterminedMotion); w.WriteBoolean("hasDeterminedCamMotion", a.HasDeterminedCamMotion);
        w.WriteString("faceCoverage", a.FaceCoverage.ToString()); w.WriteString("guideTravelCoverage", a.GuideTravelCoverage.ToString()); w.WriteString("target", a.Target.ToString());
        w.WriteBoolean("isDeclaredReachable", a.IsDeclaredReachable); w.WriteBoolean("isAdmittedReachable", a.IsAdmittedReachable);
        MechanicalAuthoringJson.Strings(w, "constraintPath", a.ConstraintPath); MechanicalAuthoringJson.Strings(w, "declaredConstraintPath", a.DeclaredConstraintPath);
        MechanicalAuthoringJson.Strings(w, "blockedPrerequisites", a.BlockedPrerequisites); Array(w, "checks", a.Checks, R.Check); Array(w, "diagnostics", a.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Compatibility(Utf8JsonWriter w, CamFollowerCompatibilityResult c)
    {
        Start(w, "gear-invest.cam-follower-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("contactPresent", c.ContactPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted); w.WriteBoolean("hasValidCamMounting", c.HasValidCamMounting);
        if (c.MappedShaftFrameMm is null) w.WriteNull("mappedShaftFrameMm"); else DerivedFrame(w, "mappedShaftFrameMm", c.MappedShaftFrameMm);
        OptionalDerived(w, "gammaTurns", c.GammaTurns); OptionalDerived(w, "epsilon", c.Epsilon); w.WritePropertyName("envelope"); Envelope(w, c.Envelope); w.WriteBoolean("hasValidDependency", c.HasValidDependency); w.WritePropertyName("geometryProof"); Proof(w, c.GeometryProof);
        Array(w, "facts", c.Facts, (x, f) => { x.WriteStartObject(); x.WriteString("key", f.Key); OptionalDerived(x, "expected", f.Expected); OptionalDerived(x, "actual", f.Actual); x.WriteEndObject(); });
        Array(w, "checks", c.Checks, R.Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Descriptor(Utf8JsonWriter w, CamFollowerMotionDescriptor? d)
    {
        if (d is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("descriptorId", d.DescriptorId); w.WriteString("policy", CamFollowerProfile.AnalysisPolicy);
        w.WriteString("supportProfileId", d.Device.SupportProfile.ProfileId); R.Derived(w, "guideDatumMm", d.GuideDatumMm); R.Derived(w, "guideOffsetMm", d.GuideOffsetMm);
        R.DerivedVector(w, "centerMm", d.CenterMm, "mm"); R.DerivedVector(w, "guideOriginMm", d.GuideOriginMm, "mm"); R.DerivedVector(w, "guideDirection", d.GuideDirection, "dimensionless");
        R.DerivedVector(w, "inPlanePerpendicular", d.InPlanePerpendicular, "dimensionless"); R.DerivedVector(w, "planeNormal", d.PlaneNormal, "dimensionless");
        Integer(w, "terminalSign", d.TerminalSign); R.Derived(w, "terminalDatumMm", d.TerminalDatumMm);
        R.Affine(w, "sourceRelation", d.SourceRelation); R.Affine(w, "physicalPhaseRelation", d.PhysicalPhaseRelation); w.WriteEndObject();
    }
    private static void Envelope(Utf8JsonWriter w, CamFollowerEnvelope? e)
    {
        if (e is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); R.Derived(w, "minimumHeightMm", e.Support.MinimumHeightMm); R.Derived(w, "maximumHeightMm", e.Support.MaximumHeightMm);
        R.Derived(w, "lowerMm", e.LowerMm); R.Derived(w, "upperMm", e.UpperMm); R.Derived(w, "strokeMm", e.StrokeMm);
        R.Derived(w, "guideDatumMm", e.GuideDatumMm); R.Derived(w, "guideOffsetMm", e.GuideOffsetMm);
        PiLength(w, "minimumTangentOffsetMm", e.Support.MinimumTangentOffsetMm); PiLength(w, "maximumTangentOffsetMm", e.Support.MaximumTangentOffsetMm);
        PiLength(w, "minimumFaceCoordinateMm", e.MinimumFaceCoordinateMm); PiLength(w, "maximumFaceCoordinateMm", e.MaximumFaceCoordinateMm);
        w.WriteString("attainmentProof", "quintic-monotone-endpoints-and-derivative-midpoint-v1"); w.WriteEndObject();
    }
    internal static void PoseRecipe(Utf8JsonWriter w, CamFollowerPoseRecipe? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("recipeId", p.RecipeId); w.WriteString("descriptorId", p.Descriptor.DescriptorId); R.DerivedQuantity(w, "rootTurns", p.RootTurns);
        R.Derived(w, "sourceTurns", p.SourceTurns); R.Derived(w, "physicalPhaseTurns", p.PhysicalPhaseTurns); R.Derived(w, "contactParameterTurns", p.ContactParameterTurns);
        w.WritePropertyName("support"); SupportSample(w, p.Support); R.DerivedQuantity(w, "guidePosition", p.GuidePosition); R.DerivedQuantity(w, "terminalPosition", p.TerminalPosition);
        R.DerivedVector(w, "followerReferencePointMm", p.FollowerReferencePointMm, "mm"); PiVector(w, "contactPointMm", p.ContactPointMm); PiLength(w, "faceCoordinateMm", p.FaceCoordinateMm); w.WriteEndObject();
    }
    private static void SupportSample(Utf8JsonWriter w, CamSupportSample s)
    {
        w.WriteStartObject(); w.WriteString("segmentId", s.SegmentId); R.Derived(w, "reducedTurns", s.ReducedTurns); R.Derived(w, "localParameter", s.LocalParameter);
        R.Derived(w, "heightMm", s.HeightMm); R.Derived(w, "firstDerivativeMmPerTurn", s.FirstDerivativeMmPerTurn); R.Derived(w, "secondDerivativeMmPerTurnSquared", s.SecondDerivativeMmPerTurnSquared);
        PiLength(w, "tangentOffsetMm", s.TangentOffsetMm); w.WriteEndObject();
    }
    private static void PiLength(Utf8JsonWriter w, string key, ExactPiLength p)
    {
        w.WriteStartObject(key); w.WriteString("representation", "rational-plus-inverse-pi"); w.WriteString("unit", "mm");
        R.Derived(w, "rationalPartMm", p.RationalPartMm); R.Derived(w, "inversePiCoefficientMm", p.InversePiCoefficientMm); w.WriteEndObject();
    }
    private static void PiVector(Utf8JsonWriter w, string key, ExactPiVector3 p)
    {
        w.WriteStartObject(key); w.WriteString("representation", "rational-vector-plus-inverse-pi"); R.DerivedVector(w, "rationalPartMm", p.RationalPartMm, "mm");
        R.DerivedVector(w, "inversePiCoefficientMm", p.InversePiCoefficientMm, "mm"); w.WriteEndObject();
    }
    private static void DerivedFrame(Utf8JsonWriter w, string key, OrientedFrame f)
    { w.WriteStartObject(key); R.DerivedVector(w, "origin", f.Origin, "mm"); R.DerivedVector(w, "x", f.X, "dimensionless"); R.DerivedVector(w, "y", f.Y, "dimensionless"); R.DerivedVector(w, "z", f.Z, "dimensionless"); w.WriteEndObject(); }
    private static void OptionalDerived(Utf8JsonWriter w, string key, Rational? value) { if (value.HasValue) R.Derived(w, key, value.Value); else w.WriteNull(key); }
    private static void Evaluation(Utf8JsonWriter w, CamFollowerEvaluation e)
    {
        Start(w, "gear-invest.cam-follower-evaluation"); w.WriteString("evaluationId", e.EvaluationId);
        BoundNumericRequest(w, e.DefinitionId, e.AnalysisId, e.RootTurns, e.Recipe?.RecipeId, e.Request);
        w.WriteString("status", e.Status.ToString()); w.WriteString("faceDecision", e.FaceDecision.ToString()); w.WriteString("travelDecision", e.TravelDecision.ToString()); w.WriteBoolean("isSuccess", e.IsSuccess);
        Integer(w, "numericWork", e.NumericWork); w.WritePropertyName("faceLowerComparison"); PiComparison(w, e.FaceLowerComparison);
        w.WritePropertyName("faceUpperComparison"); PiComparison(w, e.FaceUpperComparison);
        Array(w, "rotary", e.Rotary, (x, r) => { x.WriteStartObject(); x.WriteString("shaftId", r.ShaftId); R.DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            R.DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); R.DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject(); });
        w.WritePropertyName("cam"); if (e.Cam is null) w.WriteNullValue(); else
        {
            var c = e.Cam; w.WriteStartObject(); w.WriteString("bodyId", c.BodyId); R.DerivedVector(w, "centerMm", c.CenterMm, "mm"); R.DerivedVector(w, "positiveAxis", c.PositiveAxis, "dimensionless");
            R.DerivedVector(w, "zeroRay", c.ZeroRay, "dimensionless"); R.Derived(w, "sourceTurns", c.SourceTurns); OptionalDerived(w, "physicalPhaseTurns", c.PhysicalPhaseTurns); w.WriteEndObject();
        }
        w.WritePropertyName("recipe"); PoseRecipe(w, e.Recipe); w.WritePropertyName("contactNumeric"); NumericComputation(w, e.ContactNumeric); w.WritePropertyName("materialNumeric"); NumericComputation(w, e.MaterialNumeric);
        w.WritePropertyName("pose"); NumericPose(w, e.Pose); w.WritePropertyName("diagnosticPose"); NumericPose(w, e.DiagnosticPose);
        Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void ComparisonRequest(Utf8JsonWriter w, CamFollowerOutputComparisonRequest r)
    {
        w.WriteStartObject(); w.WriteString("requestId", r.RequestId); w.WriteString("beforeOutputKey", r.BeforeOutputKey); w.WriteString("afterOutputKey", r.AfterOutputKey);
        w.WriteString("beforeInputId", r.BeforeInputId); w.WriteString("afterInputId", r.AfterInputId); R.Quantity(w, "alpha", r.Alpha); R.Quantity(w, "beta", r.Beta); Integer(w, "sign", r.Sign); R.Quantity(w, "delta", r.Delta);
        w.WriteString("mode", r.Mode.ToString()); w.WriteString("domain", r.Domain); w.WriteString("proofPolicy", r.ProofPolicy); w.WriteString("beforeReferencePointId", r.BeforeReferencePointId); w.WriteString("afterReferencePointId", r.AfterReferencePointId);
        if (r.AfterWorldToBeforeWorldMm is null) w.WriteNull("afterWorldToBeforeWorldMm"); else LengthFrame(w, "afterWorldToBeforeWorldMm", r.AfterWorldToBeforeWorldMm);
        Array(w, "witnessRoots", r.WitnessRoots, (x, root) => { x.WriteStartObject(); R.Quantity(x, "root", root); x.WriteEndObject(); }); w.WritePropertyName("numericRequest"); NumericOptions(w, r.NumericRequest); w.WriteEndObject();
    }
    private static CamFollowerOutputComparisonRequest ComparisonRequest(JsonElement p)
    {
        var r = new CamFollowerOutputComparisonRequest(S(p, "beforeOutputKey"), S(p, "afterOutputKey"), S(p, "beforeInputId"), S(p, "afterInputId"),
            R.Quantity(p.GetProperty("alpha")), R.Quantity(p.GetProperty("beta")), I(p, "sign"), R.Quantity(p.GetProperty("delta")), E<CamFollowerOutputComparisonMode>(p, "mode"),
            MechanicalAuthoringJson.NullableString(p, "beforeReferencePointId"), MechanicalAuthoringJson.NullableString(p, "afterReferencePointId"),
            p.GetProperty("afterWorldToBeforeWorldMm").ValueKind == JsonValueKind.Null ? null : LengthFrame(p.GetProperty("afterWorldToBeforeWorldMm")),
            Items(p, "witnessRoots", 32).Select(x => R.Quantity(x.GetProperty("root"))), NumericOptions(p.GetProperty("numericRequest")), S(p, "domain"), S(p, "proofPolicy"));
        Require(r.RequestId == S(p, "requestId"), "Nonlinear comparison request identity mismatch."); return r;
    }
    internal static void Comparison(Utf8JsonWriter w, CamFollowerOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.cam-follower-output-comparison"); w.WriteString("resultId", r.ResultId); w.WritePropertyName("request"); ComparisonRequest(w, r.Request);
        w.WriteString("beforeAnalysisId", r.BeforeAnalysisId); w.WriteString("afterAnalysisId", r.AfterAnalysisId); w.WriteString("verdict", r.Verdict.ToString()); w.WriteString("proofRule", r.ProofRule);
        w.WriteString("policy", r.Policy); w.WriteString("scope", r.Scope); if (r.OperatingDomainsEqual.HasValue) w.WriteBoolean("operatingDomainsEqual", r.OperatingDomainsEqual.Value); else w.WriteNull("operatingDomainsEqual");
        w.WriteString("beforeFullCycleTravel", r.BeforeFullCycleTravel.ToString()); w.WriteString("afterFullCycleTravel", r.AfterFullCycleTravel.ToString()); w.WriteString("beforeExportAdmission", r.BeforeExportAdmission.ToString()); w.WriteString("afterExportAdmission", r.AfterExportAdmission.ToString());
        w.WriteString("beforeFullCycleFace", r.BeforeFullCycleFace.ToString()); w.WriteString("afterFullCycleFace", r.AfterFullCycleFace.ToString());
        MechanicalAuthoringJson.Strings(w, "proofCells", r.ProofCells);
        Array(w, "witnesses", r.Witnesses, (x, v) => { x.WriteStartObject(); R.DerivedQuantity(x, "beforeRoot", v.BeforeRoot); R.DerivedQuantity(x, "afterRoot", v.AfterRoot); x.WriteString("coordinate", v.Coordinate);
            Interval(x, "before", v.Before, "mm"); Interval(x, "mappedAfter", v.MappedAfter, "mm"); x.WriteBoolean("isSeparating", v.IsSeparating); x.WriteEndObject(); });
        Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.cam-follower-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));

    private static void NumericOptions(Utf8JsonWriter w, CamNumericRequest r)
    {
        w.WriteStartObject(); w.WriteString("policy", r.Policy); R.Quantity(w, "absoluteWidth", r.AbsoluteWidth);
        Integer(w, "maximumWork", r.MaximumWork); Integer(w, "maximumPrecisionBits", r.MaximumPrecisionBits); Integer(w, "maximumRefinements", r.MaximumRefinements); w.WriteEndObject();
    }
    private static CamNumericRequest NumericOptions(JsonElement p) => new(R.Quantity(p.GetProperty("absoluteWidth")), I(p, "maximumWork"), I(p, "maximumPrecisionBits"), I(p, "maximumRefinements"), MechanicalAuthoringJson.NullableString(p, "policy")!);
    internal static void NumericComputation(Utf8JsonWriter w, CamNumericResult? c)
    {
        if (c is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("resultId", c.ResultId); w.WriteString("inputId", c.InputId); w.WritePropertyName("request"); NumericOptions(w, c.Request);
        w.WriteString("status", c.Status.ToString()); Integer(w, "work", c.Work); Integer(w, "precisionBits", c.PrecisionBits); Integer(w, "refinements", c.Refinements);
        w.WriteBoolean("isAvailable", c.IsAvailable); w.WriteString("detail", c.Detail);
        if (c.Point is null) w.WriteNull("point"); else VectorInterval(w, "point", c.Point, "mm");
        if (c.DiagnosticPoint is null) w.WriteNull("diagnosticPoint"); else VectorInterval(w, "diagnosticPoint", c.DiagnosticPoint, "mm"); w.WriteEndObject();
    }
    private static void PiComparison(Utf8JsonWriter w, CamPiComparisonResult? c)
    {
        if (c is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("resultId", c.ResultId); PiLength(w, "left", c.Left); PiLength(w, "right", c.Right);
        w.WritePropertyName("request"); NumericOptions(w, c.Request); w.WriteString("comparison", c.Comparison.ToString()); w.WriteString("status", c.Status.ToString());
        w.WriteBoolean("isResolved", c.IsResolved); Integer(w, "work", c.Work); Integer(w, "precisionBits", c.PrecisionBits); Integer(w, "refinements", c.Refinements);
        if (c.PiScaledDifferenceInterval is null) w.WriteNull("piScaledDifferenceInterval"); else Interval(w, "piScaledDifferenceInterval", c.PiScaledDifferenceInterval, "mm");
        w.WriteString("detail", c.Detail); w.WriteEndObject();
    }
    private static void NumericPose(Utf8JsonWriter w, CamFollowerPose? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("recipeId", p.Recipe.RecipeId); R.DerivedVector(w, "followerReferencePointMm", p.FollowerReferencePointMm, "mm");
        VectorInterval(w, "contactPointMm", p.ContactPointMm, "mm"); VectorInterval(w, "materialZeroPointMm", p.MaterialZeroPointMm, "mm"); w.WriteEndObject();
    }
    private static void Interval(Utf8JsonWriter w, string key, CamInterval i, string unit)
    {
        w.WriteStartObject(key); w.WriteString("representation", "certified-rational-enclosure"); w.WriteString("unit", unit); w.WriteString("boundary", "closed");
        R.Derived(w, "lower", i.Lower); R.Derived(w, "upper", i.Upper); R.Derived(w, "width", i.Width); w.WriteBoolean("isExact", i.IsExact); w.WriteEndObject();
    }
    internal static void VectorInterval(Utf8JsonWriter w, string key, CamVectorInterval v, string unit)
    { w.WriteStartObject(key); Interval(w, "x", v.X, unit); Interval(w, "y", v.Y, unit); Interval(w, "z", v.Z, unit); w.WriteEndObject(); }
}
