using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class OpenBeltJson
{
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.open-belt-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));
    public static byte[] WriteAnalysis(OpenBeltAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static OpenBeltAnalysis VerifyAnalysis(OpenBeltDraft draft, byte[] bytes)
    { var a = OpenBeltAnalyzer.Analyze(draft); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored open belt analysis differs from fresh current definition analysis."); return a; }
    public static byte[] WriteEvaluation(OpenBeltEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static OpenBeltEvaluation VerifyEvaluation(OpenBeltAnalysis analysis, ExactQuantity input, byte[] bytes)
    { var evaluation = OpenBeltAnalyzer.Evaluate(analysis, input); Require(bytes.SequenceEqual(WriteEvaluation(evaluation)), "Stored open belt evaluation differs from fresh absolute-input evaluation."); return evaluation; }
    public static byte[] WriteCompatibility(OpenBeltCompatibilityResult compatibility) => Guard(() => Encode(w => Compatibility(w, compatibility)));
    public static byte[] WriteComparison(OpenBeltOutputEquivalenceResult comparison) => Guard(() => Encode(w => Comparison(w, comparison)));
    private static void Analysis(Utf8JsonWriter w, OpenBeltAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("numericSemantics", OpenBeltProfile.NumericSemantics); w.WriteString("analysisId", a.AnalysisId);
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
    internal static void Compatibility(Utf8JsonWriter w, OpenBeltCompatibilityResult c)
    {
        Start(w, "gear-invest.open-belt-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("transmissionPresent", c.TransmissionPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted);
        R.DerivedVector(w, "inputPositiveAxis", c.InputPositiveAxis, "dimensionless"); R.DerivedVector(w, "outputPositiveAxis", c.OutputPositiveAxis, "dimensionless");
        OptionalDerived(w, "inputAxisRouteSign", c.InputAxisRouteSign); OptionalDerived(w, "outputAxisRouteSign", c.OutputAxisRouteSign);
        OptionalDerived(w, "sourcePortCoordinateSign", c.SourcePortCoordinateSign); OptionalDerived(w, "terminalCoordinateSign", c.TerminalCoordinateSign);
        OptionalDerived(w, "prospectiveTransfer", c.ProspectiveTransfer); OptionalDerived(w, "admittedTransfer", c.AdmittedTransfer);
        w.WritePropertyName("route"); Route(w, c.Route); w.WritePropertyName("lengthCompatibility"); LengthCompatibility(w, c.LengthCompatibility);
        Array(w, "contactVelocityProofs", c.ContactVelocityProofs, Proof); Array(w, "facts", c.Facts, Fact);
        Array(w, "checks", c.Checks, R.Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void LengthCompatibility(Utf8JsonWriter w, OpenBeltLengthCompatibility c)
    {
        w.WriteStartObject(); w.WriteString("compatibilityId", c.CompatibilityId); w.WriteString("verdict", c.Verdict.ToString()); w.WriteString("proofKind", c.ProofKind);
        w.WriteString("selectedSpecificationId", c.SelectedSpecificationId); w.WritePropertyName("requiredLength"); LengthDescriptor(w, c.RequiredLength);
        w.WritePropertyName("referenceLength"); LengthDescriptor(w, c.ReferenceLength); Array(w, "facts", c.Facts, Fact); w.WriteString("detail", c.Detail); w.WriteEndObject();
    }
    internal static void Route(Utf8JsonWriter w, OpenBeltRouteDescriptor? r)
    {
        if (r is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("numericSemantics", r.NumericSemantics); w.WriteString("routeId", r.RouteId);
        R.DerivedVector(w, "inputCenterMm", r.InputCenterMm, "mm"); R.DerivedVector(w, "outputCenterMm", r.OutputCenterMm, "mm");
        R.DerivedQuantity(w, "inputPitchRadius", r.InputPitchRadius); R.DerivedQuantity(w, "outputPitchRadius", r.OutputPitchRadius);
        R.DerivedVector(w, "normal", r.Normal, "dimensionless"); R.DerivedVector(w, "centerDirection", r.CenterDirection, "dimensionless"); R.DerivedVector(w, "sideDirection", r.SideDirection, "dimensionless");
        R.Derived(w, "centerDistanceMm", r.CenterDistance); R.Derived(w, "h", r.H); R.Derived(w, "radicand", r.Radicand);
        w.WriteString("squareRootBranch", r.SquareRootBranch); w.WriteString("traversal", r.Traversal);
        Radical(w, "nplus", r.Nplus, "dimensionless"); Radical(w, "nminus", r.Nminus, "dimensionless");
        Radical(w, "p1plus", r.P1plus, "mm"); Radical(w, "p2plus", r.P2plus, "mm"); Radical(w, "p2minus", r.P2minus, "mm"); Radical(w, "p1minus", r.P1minus, "mm");
        w.WritePropertyName("plusSpan"); Span(w, r.PlusSpan); w.WritePropertyName("outputArc"); Arc(w, r.OutputArc);
        w.WritePropertyName("minusSpan"); Span(w, r.MinusSpan); w.WritePropertyName("inputArc"); Arc(w, r.InputArc);
        w.WritePropertyName("length"); LengthDescriptor(w, r.Length); Array(w, "proofs", r.Proofs, Proof); w.WriteBoolean("hasExactProof", r.HasExactProof); w.WriteEndObject();
    }
    private static void Radical(Utf8JsonWriter w, string key, OpenBeltRadicalVector v, string unit)
    {
        w.WriteStartObject(key); w.WriteString("representation", "rational-vector-plus-positive-radical-vector"); w.WriteString("unit", unit);
        R.DerivedVector(w, "rationalPart", v.RationalPart, unit); R.DerivedVector(w, "radicalCoefficient", v.RadicalCoefficient, unit);
        R.Derived(w, "radicand", v.Radicand); w.WriteString("squareRootBranch", v.SquareRootBranch); w.WriteEndObject();
    }
    private static void Span(Utf8JsonWriter w, OpenBeltSpanDescriptor s)
    {
        w.WriteStartObject(); w.WriteString("id", s.Id); Radical(w, "start", s.Start, "mm"); Radical(w, "end", s.End, "mm");
        R.Derived(w, "lengthSquaredMm2", s.LengthSquared); w.WriteString("lengthSquareRootBranch", s.LengthSquareRootBranch); w.WriteEndObject();
    }
    private static void Arc(Utf8JsonWriter w, OpenBeltArcDescriptor a)
    {
        w.WriteStartObject(); w.WriteString("id", a.Id); R.DerivedVector(w, "centerMm", a.CenterMm, "mm"); R.Derived(w, "radiusMm", a.RadiusMm);
        Radical(w, "start", a.Start, "mm"); Radical(w, "end", a.End, "mm"); R.DerivedVector(w, "throughDirection", a.ThroughDirection, "dimensionless");
        w.WriteString("angleUnit", "radian"); w.WriteBoolean("clockwise", a.Clockwise); Integer(w, "piCoefficient", a.PiCoefficient);
        Integer(w, "asinCoefficient", a.AsinCoefficient); w.WriteString("sweepBranch", a.SweepBranch); w.WriteEndObject();
    }
    private static void LengthDescriptor(Utf8JsonWriter w, OpenBeltLengthDescriptor? d)
    {
        if (d is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("semantics", d.Semantics); w.WriteString("descriptorId", d.DescriptorId); w.WriteString("unit", "mm");
        R.Derived(w, "centerDistance", d.CenterDistance); R.Derived(w, "inputRadius", d.InputRadius); R.Derived(w, "outputRadius", d.OutputRadius);
        R.Derived(w, "radiusDifference", d.RadiusDifference); R.Derived(w, "spanLengthSquared", d.SpanLengthSquared);
        Integer(w, "spanSquareRootCoefficient", d.SpanSquareRootCoefficient); w.WriteString("squareRootBranch", d.SquareRootBranch);
        R.Derived(w, "piCoefficient", d.PiCoefficient); R.Derived(w, "asinArgument", d.AsinArgument); R.Derived(w, "asinCoefficient", d.AsinCoefficient);
        w.WriteString("asinBranch", d.AsinBranch); w.WriteEndObject();
    }
    private static void Proof(Utf8JsonWriter w, OpenBeltExactProof p)
    {
        w.WriteStartObject(); w.WriteString("id", p.Id); w.WriteString("scope", p.Scope); R.Derived(w, "rationalResidual", p.RationalResidual);
        R.Derived(w, "radicalResidual", p.RadicalResidual); R.Derived(w, "radicand", p.Radicand); w.WriteBoolean("isVerified", p.IsVerified); w.WriteString("detail", p.Detail); w.WriteEndObject();
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
    private static void Evaluation(Utf8JsonWriter w, OpenBeltEvaluation e)
    {
        Start(w, "gear-invest.open-belt-evaluation"); w.WriteString("numericSemantics", OpenBeltProfile.NumericSemantics); w.WriteString("analysisId", e.AnalysisId);
        w.WriteString("status", e.Status.ToString()); R.DerivedQuantity(w, "input", e.Input);
        Array(w, "rotary", e.Rotary, (x, r) =>
        {
            x.WriteStartObject(); x.WriteString("kind", "AngularPosition"); x.WriteString("shaftId", r.ShaftId); R.DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            R.DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); R.DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject();
        });
        w.WritePropertyName("output"); EvaluationOutput(w, e.Output); Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void EvaluationOutput(Utf8JsonWriter w, OpenBeltOutputEvaluation? o)
    {
        if (o is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("outputKey", o.OutputKey); w.WriteString("shaftId", o.ShaftId); w.WriteString("pulleyBodyId", o.PulleyBodyId); w.WriteString("terminalId", o.TerminalId);
        R.DerivedQuantity(w, "inputPulleyTurns", o.InputPulleyTurns); R.DerivedQuantity(w, "shaftTurns", o.ShaftTurns); R.DerivedQuantity(w, "terminalTurns", o.TerminalTurns);
        R.DerivedVector(w, "shaftPositiveAxis", o.ShaftPositiveAxis, "dimensionless"); R.DerivedVector(w, "pulleyCenterMm", o.PulleyCenterMm, "mm");
        R.Derived(w, "shaftGain", o.ShaftGain); R.Derived(w, "terminalGain", o.TerminalGain);
        w.WriteStartObject("beltMaterialTravel"); w.WriteString("representation", "pi-multiple"); w.WriteString("unit", "mm"); w.WriteString("traversal", "clockwise");
        R.Derived(w, "piCoefficient", o.BeltMaterialTravelPiCoefficientMm); w.WriteEndObject(); w.WriteEndObject();
    }
    private static void Comparison(Utf8JsonWriter w, OpenBeltOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.open-belt-output-comparison"); w.WriteString("numericSemantics", OpenBeltProfile.NumericSemantics); w.WriteString("comparisonId", r.ComparisonId);
        w.WritePropertyName("motion"); MechanicalAuthoringJson.Comparison(w, r.Motion);
        w.WriteString("beforeLength", r.BeforeLength.ToString()); w.WriteString("afterLength", r.AfterLength.ToString());
        w.WriteString("beforeRouteId", r.BeforeRouteId); w.WriteString("afterRouteId", r.AfterRouteId);
        if (r.SameRoute.HasValue) w.WriteBoolean("sameRoute", r.SameRoute.Value); else w.WriteNull("sameRoute");
        w.WriteString("beforeBeltSpecificationId", r.BeforeBeltSpecificationId); w.WriteString("afterBeltSpecificationId", r.AfterBeltSpecificationId);
        w.WriteBoolean("sameBeltSpecification", r.SameBeltSpecification); w.WriteBoolean("beforeWholeValid", r.BeforeWholeValid); w.WriteBoolean("afterWholeValid", r.AfterWholeValid); w.WriteEndObject();
    }
}
