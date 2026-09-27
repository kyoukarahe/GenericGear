using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class WormDriveJson
{
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.worm-drive-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));
    public static byte[] WriteAnalysis(WormDriveAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static WormDriveAnalysis VerifyAnalysis(WormDriveDraft draft, byte[] bytes)
    { var a = WormDriveAnalyzer.Analyze(draft); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored worm drive analysis differs from fresh current definition analysis."); return a; }
    public static byte[] WriteEvaluation(WormDriveEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static WormDriveEvaluation VerifyEvaluation(WormDriveAnalysis analysis, ExactQuantity input, byte[] bytes)
    { var evaluation = WormDriveAnalyzer.Evaluate(analysis, input); Require(bytes.SequenceEqual(WriteEvaluation(evaluation)), "Stored worm drive evaluation differs from fresh absolute-input evaluation."); return evaluation; }
    public static byte[] WriteCompatibility(WormDriveCompatibilityResult compatibility) => Guard(() => Encode(w => Compatibility(w, compatibility)));
    public static byte[] WriteComparison(WormDriveOutputEquivalenceResult comparison) => Guard(() => Encode(w => Comparison(w, comparison)));
    private static void Analysis(Utf8JsonWriter w, WormDriveAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("geometrySemantics", WormDriveProfile.GeometrySemantics); w.WriteString("poseSemantics", WormDriveProfile.PoseSemantics); w.WriteString("analysisId", a.AnalysisId);
        w.WriteString("policy", a.Policy); w.WriteString("motionDomain", a.MotionDomain); w.WriteString("definitionId", a.DefinitionId); w.WriteString("draftId", a.DraftId);
        MechanicalAuthoringJson.Long(w, "revision", a.Revision); w.WriteString("selectedInputId", a.SelectedInputId); w.WriteBoolean("isMechanicallyValid", a.IsMechanicallyValid);
        w.WriteString("geometry", a.Geometry.ToString()); w.WriteString("exportAdmission", a.ExportAdmission.ToString());
        w.WritePropertyName("sourceAnalysis"); MechanicalAuthoringJson.Analysis(w, a.SourceAnalysis);
        w.WritePropertyName("localCompatibility"); Compatibility(w, a.LocalCompatibility);
        Array(w, "nodes", a.Nodes, (x, n) => { x.WriteStartObject(); x.WriteString("id", n.Id); x.WriteString("kind", n.Kind.ToString()); x.WriteBoolean("isPrescribed", n.IsPrescribed); x.WriteEndObject(); });
        Array(w, "admittedEdges", a.AdmittedEdges, (x, e) => { x.WriteStartObject(); x.WriteString("id", e.Id); x.WriteString("driverDofId", e.DriverDofId); x.WriteString("drivenDofId", e.DrivenDofId); R.DerivedQuantity(x, "transfer", e.Transfer); R.DerivedQuantity(x, "offset", e.Offset); x.WriteEndObject(); });
        Array(w, "admittedComponents", a.AdmittedComponents, (x, c) =>
        {
            x.WriteStartObject(); x.WriteString("parameterId", c.ParameterId); x.WriteString("determinacy", c.Determinacy.ToString());
            MechanicalAuthoringJson.Strings(x, "memberIds", c.MemberIds); MechanicalAuthoringJson.Strings(x, "constraintIds", c.ConstraintIds);
            MechanicalAuthoringJson.Strings(x, "prescribedInputIds", c.PrescribedInputIds);
            Array(x, "relations", c.Relations, (y, r) => { y.WriteStartObject(); y.WriteString("dofId", r.DofId); R.Affine(y, "relation", r.Relation); MechanicalAuthoringJson.Strings(y, "constraintPath", r.ConstraintPath); y.WriteEndObject(); });
            Array(x, "closures", c.Closures, (y, closure) =>
            {
                y.WriteStartObject(); y.WriteString("constraintId", closure.ConstraintId); R.Derived(y, "coefficient", closure.Coefficient); R.Derived(y, "phase", closure.Phase);
                y.WriteBoolean("isRedundant", closure.IsRedundant); y.WritePropertyName("existing"); Path(y, closure.Existing); y.WritePropertyName("alternative"); Path(y, closure.Alternative); y.WriteEndObject();
            });
            OptionalDerived(x, "pinnedParameter", c.PinnedParameter); Array(x, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); x.WriteEndObject();
        });
        R.Affine(w, "retainedInputRelation", a.RetainedInputRelation); w.WritePropertyName("output"); OutputAnalysis(w, a.Output);
        Array(w, "checks", a.Checks, R.Check); Array(w, "diagnostics", a.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void OutputAnalysis(Utf8JsonWriter w, MechanicalOutputAnalysis o)
    {
        w.WriteStartObject(); w.WritePropertyName("binding"); MechanicalAuthoringJson.Output(w, o.Binding);
        w.WriteBoolean("isDeclaredReachable", o.IsDeclaredReachable); w.WriteBoolean("isAdmittedReachable", o.IsAdmittedReachable);
        w.WriteString("determinacy", o.Determinacy.ToString()); w.WriteBoolean("hasDeterminedMotion", o.HasDeterminedMotion);
        R.Affine(w, "shaftRelation", o.ShaftRelation); R.Affine(w, "portRelation", o.PortRelation);
        R.DerivedVector(w, "shaftPositiveAxis", o.ShaftPositiveAxis, "dimensionless");
        if (o.PortFrame is null) w.WriteNull("portFrameMm"); else LengthFrame(w, "portFrameMm", o.PortFrame);
        MechanicalAuthoringJson.Strings(w, "constraintPath", o.ConstraintPath); MechanicalAuthoringJson.Strings(w, "declaredConstraintPath", o.DeclaredConstraintPath);
        MechanicalAuthoringJson.Strings(w, "blockedPrerequisites", o.BlockedPrerequisites); w.WriteString("target", o.Target.ToString());
        Array(w, "diagnostics", o.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Compatibility(Utf8JsonWriter w, WormDriveCompatibilityResult c)
    {
        Start(w, "gear-invest.worm-drive-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("transmissionPresent", c.TransmissionPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted);
        R.DerivedVector(w, "inputPositiveAxis", c.InputPositiveAxis, "dimensionless"); R.DerivedVector(w, "outputPositiveAxis", c.OutputPositiveAxis, "dimensionless");
        OptionalDerived(w, "epsilon", c.Epsilon); OptionalDerived(w, "sigma", c.Sigma);
        OptionalDerived(w, "sourcePortCoordinateSign", c.SourcePortCoordinateSign); OptionalDerived(w, "terminalCoordinateSign", c.TerminalCoordinateSign);
        OptionalDerived(w, "prospectiveTransfer", c.ProspectiveTransfer); OptionalDerived(w, "admittedTransfer", c.AdmittedTransfer);
        w.WritePropertyName("geometry"); Geometry(w, c.Geometry); Array(w, "facts", c.Facts, Fact);
        Array(w, "checks", c.Checks, R.Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void Geometry(Utf8JsonWriter w, WormDriveGeometryDescriptor? g)
    {
        if (g is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("semantics", g.Semantics); w.WriteString("geometryId", g.GeometryId);
        R.Derived(w, "axialPitchPiCoefficientMm", g.AxialPitchPiCoefficientMm);
        R.Derived(w, "wheelPitchPiCoefficientMm", g.WheelPitchPiCoefficientMm);
        R.Derived(w, "leadPiCoefficientMm", g.LeadPiCoefficientMm);
        R.Derived(w, "leadSlope", g.LeadSlope);
        R.Derived(w, "wormPitchRadiusMm", g.WormPitchRadiusMm);
        R.Derived(w, "wheelPitchRadiusMm", g.WheelPitchRadiusMm);
        R.Derived(w, "centerDistanceMm", g.CenterDistanceMm);
        R.Derived(w, "epsilon", g.Epsilon);
        R.Derived(w, "sigma", g.Sigma);
        R.Derived(w, "phaseAdvanceTransfer", g.PhaseAdvanceTransfer);
        R.Derived(w, "covectorTransfer", g.CovectorTransfer);
        w.WriteString("pitchAndLeadRepresentation", "pi-times-exact-mm-coefficient");
        w.WriteString("leadAngleRecipe", g.LeadAngleRecipe);
        R.DerivedVector(w, "inputPitchCenterMm", g.InputPitchCenterMm, "mm");
        R.DerivedVector(w, "outputPitchCenterMm", g.OutputPitchCenterMm, "mm");
        R.DerivedVector(w, "pitchPointMm", g.PitchPointMm, "mm");
        R.DerivedVector(w, "wormTrace", g.WormTrace, "dimensionless");
        R.DerivedVector(w, "wheelTrace", g.WheelTrace, "dimensionless");
        R.DerivedVector(w, "phaseCovector", g.PhaseCovector, "dimensionless");
        R.DerivedVector(w, "wormMaterialVelocityReduced", g.WormMaterialVelocityReduced, "mm-per-reduced-input-turn");
        R.DerivedVector(w, "wheelMaterialVelocityReduced", g.WheelMaterialVelocityReduced, "mm-per-reduced-input-turn");
        R.DerivedVector(w, "relativeMaterialVelocityReduced", g.RelativeMaterialVelocityReduced, "mm-per-reduced-input-turn");
        Array(w, "proofs", g.Proofs, Proof); w.WriteBoolean("hasExactProof", g.HasExactProof); w.WriteEndObject();
    }
    private static void Proof(Utf8JsonWriter w, WormDriveExactProof p)
    {
        w.WriteStartObject(); w.WriteString("id", p.Id); w.WriteString("kind", p.Kind); R.Derived(w, "residual", p.Residual);
        w.WriteBoolean("passed", p.Passed); w.WriteString("detail", p.Detail); w.WriteEndObject();
    }
    private static void Fact(Utf8JsonWriter w, MechanicalExactFact f)
    { w.WriteStartObject(); w.WriteString("key", f.Key); OptionalDerived(w, "expected", f.Expected); OptionalDerived(w, "actual", f.Actual); w.WriteEndObject(); }
    private static void Path(Utf8JsonWriter w, MechanicalPathWitness p)
    {
        w.WriteStartObject(); w.WriteString("rootId", p.RootId); w.WriteString("targetId", p.TargetId); R.Derived(w, "coefficient", p.Coefficient);
        R.Derived(w, "phase", p.Phase); MechanicalAuthoringJson.Strings(w, "constraintIds", p.ConstraintIds); w.WriteEndObject();
    }
    private static void OptionalDerived(Utf8JsonWriter w, string key, Rational? value)
    { if (value.HasValue) R.Derived(w, key, value.Value); else w.WriteNull(key); }
    private static void Evaluation(Utf8JsonWriter w, WormDriveEvaluation e)
    {
        Start(w, "gear-invest.worm-drive-evaluation"); w.WriteString("geometrySemantics", WormDriveProfile.GeometrySemantics); w.WriteString("poseSemantics", WormDriveProfile.PoseSemantics); w.WriteString("analysisId", e.AnalysisId);
        w.WriteString("status", e.Status.ToString()); R.DerivedQuantity(w, "input", e.Input);
        Array(w, "rotary", e.Rotary, (x, r) =>
        {
            x.WriteStartObject(); x.WriteString("kind", "AngularPosition"); x.WriteString("shaftId", r.ShaftId); R.DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            R.DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); R.DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject();
        });
        w.WritePropertyName("output"); EvaluationOutput(w, e.Output); Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void EvaluationOutput(Utf8JsonWriter w, WormDriveOutputEvaluation? o)
    {
        if (o is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("outputKey", o.OutputKey); w.WriteString("shaftId", o.ShaftId); w.WriteString("wheelBodyId", o.WheelBodyId); w.WriteString("terminalId", o.TerminalId);
        R.DerivedQuantity(w, "inputWormTurns", o.InputWormTurns); R.DerivedQuantity(w, "shaftTurns", o.ShaftTurns); R.DerivedQuantity(w, "terminalTurns", o.TerminalTurns);
        R.DerivedVector(w, "shaftPositiveAxis", o.ShaftPositiveAxis, "dimensionless"); R.DerivedVector(w, "wheelCenterMm", o.WheelCenterMm, "mm");
        R.Derived(w, "shaftGain", o.ShaftGain); R.Derived(w, "terminalGain", o.TerminalGain);
        R.Derived(w, "wormPhaseIndex", o.WormPhaseIndex); R.Derived(w, "wheelPhaseIndex", o.WheelPhaseIndex);
        w.WriteBoolean("phaseConstraintSatisfied", o.PhaseConstraintSatisfied); w.WriteEndObject();
    }
    private static void Comparison(Utf8JsonWriter w, WormDriveOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.worm-drive-output-comparison");
        w.WriteString("geometrySemantics", WormDriveProfile.GeometrySemantics); w.WriteString("poseSemantics", WormDriveProfile.PoseSemantics);
        w.WriteString("comparisonId", r.ComparisonId); w.WritePropertyName("motion"); MechanicalAuthoringJson.Comparison(w, r.Motion);
        w.WriteString("beforeCompatibility", r.BeforeCompatibility.ToString()); w.WriteString("afterCompatibility", r.AfterCompatibility.ToString());
        w.WriteString("beforeGeometryId", r.BeforeGeometryId); w.WriteString("afterGeometryId", r.AfterGeometryId);
        if (r.SameGeometry.HasValue) w.WriteBoolean("sameGeometry", r.SameGeometry.Value); else w.WriteNull("sameGeometry");
        w.WriteString("beforeWormSpecificationId", r.BeforeWormSpecificationId); w.WriteString("afterWormSpecificationId", r.AfterWormSpecificationId);
        w.WriteString("beforeWheelSpecificationId", r.BeforeWheelSpecificationId); w.WriteString("afterWheelSpecificationId", r.AfterWheelSpecificationId);
        w.WriteBoolean("sameWormSpecification", r.SameWormSpecification); w.WriteBoolean("sameWheelSpecification", r.SameWheelSpecification);
        w.WriteBoolean("beforeWholeValid", r.BeforeWholeValid); w.WriteBoolean("afterWholeValid", r.AfterWholeValid); w.WriteEndObject();
    }
}
