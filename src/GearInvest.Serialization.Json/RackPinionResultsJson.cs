using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class RackPinionJson
{
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.rack-pinion-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));
    public static byte[] WriteAnalysis(RackPinionAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static RackPinionAnalysis VerifyAnalysis(RackPinionDraft draft, byte[] bytes)
    { var a = RackPinionAnalyzer.Analyze(draft); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored rack analysis differs from fresh current definition analysis."); return a; }
    public static byte[] WriteEvaluation(RackPinionEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static RackPinionEvaluation VerifyEvaluation(RackPinionAnalysis analysis, ExactQuantity input, byte[] bytes)
    { var evaluation = RackPinionAnalyzer.Evaluate(analysis, input); Require(bytes.SequenceEqual(WriteEvaluation(evaluation)), "Stored rack evaluation differs from fresh absolute-input evaluation."); return evaluation; }
    public static byte[] WriteCompatibility(RackPinionCompatibilityResult compatibility) => Guard(() => Encode(w => Compatibility(w, compatibility)));
    public static byte[] WriteComparison(PrismaticOutputEquivalenceResult comparison) => Guard(() => Encode(w => Comparison(w, comparison)));
    private static void Analysis(Utf8JsonWriter w, RackPinionAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("numericSemantics", RackPinionProfile.NumericSemantics); w.WriteString("analysisId", a.AnalysisId);
        w.WriteString("policy", a.Policy); w.WriteString("motionDomain", a.MotionDomain); w.WriteString("definitionId", a.DefinitionId); w.WriteString("draftId", a.DraftId);
        MechanicalAuthoringJson.Long(w, "revision", a.Revision); w.WriteString("selectedInputId", a.SelectedInputId); w.WriteBoolean("isMechanicallyValid", a.IsMechanicallyValid);
        w.WriteString("exportAdmission", a.ExportAdmission.ToString()); w.WritePropertyName("sourceAnalysis"); MechanicalAuthoringJson.Analysis(w, a.SourceAnalysis);
        w.WritePropertyName("localCompatibility"); Compatibility(w, a.LocalCompatibility);
        Array(w, "nodes", a.Nodes, (x, n) => { x.WriteStartObject(); x.WriteString("id", n.Id); x.WriteString("kind", n.Kind.ToString()); x.WriteBoolean("isPrescribed", n.IsPrescribed); x.WriteEndObject(); });
        Array(w, "admittedEdges", a.AdmittedEdges, (x, e) => { x.WriteStartObject(); x.WriteString("id", e.Id); x.WriteString("driverDofId", e.DriverDofId); x.WriteString("drivenDofId", e.DrivenDofId); R.DerivedQuantity(x, "transfer", e.Transfer); R.DerivedQuantity(x, "offset", e.Offset); x.WriteEndObject(); });
        Array(w, "admittedComponents", a.AdmittedComponents, (x, c) =>
        {
            x.WriteStartObject(); x.WriteString("parameterId", c.ParameterId); x.WriteString("determinacy", c.Determinacy.ToString());
            MechanicalAuthoringJson.Strings(x, "memberIds", c.MemberIds); MechanicalAuthoringJson.Strings(x, "constraintIds", c.ConstraintIds);
            MechanicalAuthoringJson.Strings(x, "prescribedInputIds", c.PrescribedInputIds);
            Array(x, "relations", c.Relations, (y, r) => { y.WriteStartObject(); y.WriteString("dofId", r.DofId); R.Affine(y, "relation", r.Relation); MechanicalAuthoringJson.Strings(y, "constraintPath", r.ConstraintPath); y.WriteEndObject(); });
            Array(x, "closures", c.Closures, (y, closure) => { y.WriteStartObject(); y.WriteString("constraintId", closure.ConstraintId); R.Derived(y, "coefficient", closure.Coefficient); R.Derived(y, "phase", closure.Phase); y.WriteBoolean("isRedundant", closure.IsRedundant); y.WriteEndObject(); });
            if (c.PinnedParameter.HasValue) R.Derived(x, "pinnedParameter", c.PinnedParameter.Value); else x.WriteNull("pinnedParameter");
            Array(x, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); x.WriteEndObject();
        });
        w.WritePropertyName("linearOutput"); var o = a.LinearOutput; w.WriteStartObject(); w.WritePropertyName("binding"); Output(w, o.Binding);
        w.WriteString("determinacy", o.Determinacy.ToString()); w.WriteBoolean("hasDeterminedMotion", o.HasDeterminedMotion);
        w.WriteBoolean("isDeclaredReachable", o.IsDeclaredReachable); w.WriteBoolean("isAdmittedReachable", o.IsAdmittedReachable);
        R.Affine(w, "sourceRelation", o.SourceRelation); R.Relation(w, "guideRelation", o.GuideRelation); R.Relation(w, "terminalRelation", o.TerminalRelation);
        R.DerivedVector(w, "sourcePositiveAxis", o.SourcePositiveAxis, "dimensionless"); R.DerivedVector(w, "guidePositiveAxis", o.GuidePositiveAxis, "dimensionless");
        R.DerivedVector(w, "worldGainMmPerRootTurn", o.WorldGainMmPerRootTurn, "mm/turn"); PiVector(w, "worldOffsetMm", o.WorldOffsetMm);
        R.DerivedInterval(w, "validRootInterval", o.ValidRootInterval); w.WriteString("requiredRange", o.RequiredRange.ToString()); w.WriteString("target", o.Target.ToString());
        MechanicalAuthoringJson.Strings(w, "constraintPath", o.ConstraintPath); MechanicalAuthoringJson.Strings(w, "declaredConstraintPath", o.DeclaredConstraintPath);
        MechanicalAuthoringJson.Strings(w, "blockedPrerequisites", o.BlockedPrerequisites); Array(w, "diagnostics", o.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
        Array(w, "checks", a.Checks, R.Check); Array(w, "diagnostics", a.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void Compatibility(Utf8JsonWriter w, RackPinionCompatibilityResult c)
    {
        Start(w, "gear-invest.rack-pinion-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("transmissionPresent", c.TransmissionPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted); R.DerivedVector(w, "pinionPositiveAxis", c.PinionPositiveAxis, "dimensionless");
        if (c.ContactSign.HasValue) R.Derived(w, "contactSign", c.ContactSign.Value); else w.WriteNull("contactSign");
        if (c.SourcePortCoordinateSign.HasValue) R.Derived(w, "sourcePortCoordinateSign", c.SourcePortCoordinateSign.Value); else w.WriteNull("sourcePortCoordinateSign");
        R.DerivedQuantity(w, "localGain", c.LocalGain); PiLength(w, "pitchRadius", c.PitchRadius); PiVector(w, "fixedContactPointMm", c.FixedContactPointMm);
        Array(w, "facts", c.Facts, (x, f) => { x.WriteStartObject(); x.WriteString("key", f.Key); if (f.Expected.HasValue) R.Derived(x, "expected", f.Expected.Value); else x.WriteNull("expected"); if (f.Actual.HasValue) R.Derived(x, "actual", f.Actual.Value); else x.WriteNull("actual"); x.WriteEndObject(); });
        Array(w, "checks", c.Checks, R.Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void Evaluation(Utf8JsonWriter w, RackPinionEvaluation e)
    {
        Start(w, "gear-invest.rack-pinion-evaluation"); w.WriteString("numericSemantics", RackPinionProfile.NumericSemantics); w.WriteString("analysisId", e.AnalysisId); w.WriteString("status", e.Status.ToString()); R.DerivedQuantity(w, "input", e.Input);
        Array(w, "rotary", e.Rotary, (x, r) => { x.WriteStartObject(); x.WriteString("kind", "AngularPosition"); x.WriteString("shaftId", r.ShaftId);
            R.DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            R.DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); R.DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject(); });
        w.WritePropertyName("linear"); Position(w, e.Linear); w.WritePropertyName("diagnosticLinear"); Position(w, e.DiagnosticLinear);
        Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void Position(Utf8JsonWriter w, RackPositionEvaluation? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("outputKey", p.OutputKey); w.WriteString("linearDofId", p.LinearDofId); w.WriteString("rackBodyId", p.RackBodyId);
        R.DerivedQuantity(w, "pinionTurns", p.PinionTurns); R.DerivedQuantity(w, "guidePosition", p.GuidePosition); R.DerivedQuantity(w, "terminalPosition", p.TerminalPosition);
        PiVector(w, "worldPositionMm", p.WorldPositionMm); PiFrame(w, "rackOrientation", p.RackOrientation);
        R.DerivedQuantity(w, "guideGain", p.GuideGain); R.DerivedQuantity(w, "terminalGain", p.TerminalGain);
        R.DerivedQuantity(w, "materialContactCoordinate", p.MaterialContactCoordinate); PiVector(w, "fixedContactPointMm", p.FixedContactPointMm); w.WriteEndObject();
    }
    private static void Comparison(Utf8JsonWriter w, PrismaticOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.prismatic-output-comparison"); w.WriteString("numericSemantics", RackPinionProfile.NumericSemantics);
        w.WriteString("comparisonId", r.ComparisonId); w.WritePropertyName("request"); R.ComparisonRequest(w, r.Request);
        w.WriteString("verdict", r.Verdict.ToString()); w.WriteString("scope", r.Scope); R.Relation(w, "beforeRelation", r.BeforeRelation); R.Relation(w, "mappedAfterRelation", r.MappedAfterRelation);
        R.DerivedVector(w, "beforeWorldGainMmPerRootTurn", r.BeforeWorldGainMmPerRootTurn, "mm/turn"); R.DerivedVector(w, "mappedAfterWorldGainMmPerRootTurn", r.MappedAfterWorldGainMmPerRootTurn, "mm/turn");
        PiVector(w, "beforeWorldOffsetMm", r.BeforeWorldOffsetMm); PiVector(w, "mappedAfterWorldOffsetMm", r.MappedAfterWorldOffsetMm); w.WriteBoolean("operatingDomainsEqual", r.OperatingDomainsEqual);
        R.DerivedInterval(w, "beforeOperatingDomain", r.BeforeOperatingDomain); R.DerivedInterval(w, "mappedAfterOperatingDomain", r.MappedAfterOperatingDomain);
        if (r.BothFeasibleOnComparisonInterval.HasValue) w.WriteBoolean("bothFeasibleOnComparisonInterval", r.BothFeasibleOnComparisonInterval.Value); else w.WriteNull("bothFeasibleOnComparisonInterval");
        w.WriteString("beforeAnalysisId", r.BeforeAnalysisId); w.WriteString("afterAnalysisId", r.AfterAnalysisId); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
