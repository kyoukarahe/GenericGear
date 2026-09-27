using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public static partial class MechanicalAuthoringJson
{
    public static byte[] WriteAnalysis(MechanicalAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    /// <summary>There is deliberately no trust-based analysis reader; this method recomputes and verifies every persisted field.</summary>
    public static MechanicalAnalysis VerifyAnalysis(MechanicalDraft draft, byte[] bytes)
    {
        var analysis = MechanicalAnalyzer.Analyze(draft);
        Require(bytes.SequenceEqual(WriteAnalysis(analysis)), "Stored mechanical analysis differs from current exact reanalysis."); return analysis;
    }
    public static byte[] WriteComparison(OutputEquivalenceResult result) => Guard(() => Encode(w => Comparison(w, result)));
    public static byte[] WriteComparisonRequest(OutputComparisonRequest request) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.output-comparison-request"); w.WritePropertyName("request"); ComparisonRequest(w, request); w.WriteEndObject(); }));
    public static OutputComparisonRequest ReadComparisonRequest(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.output-comparison-request"); var request = ComparisonRequest(doc.RootElement.GetProperty("request")); Require(bytes.SequenceEqual(WriteComparisonRequest(request)), "Noncanonical comparison request."); return request; });
    public static byte[] WriteConnection(ConnectionCompatibilityResult result) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.connection-compatibility"); w.WritePropertyName("result"); Compatibility(w, result); w.WriteEndObject(); }));
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash,
        bool originalBytesPreserved, IEnumerable<MechanicalDiagnostic> diagnostics) => Encode(w =>
    {
        Start(w, "gear-invest.mechanical-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId);
        w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); w.WriteBoolean("originalBytesPreserved", originalBytesPreserved);
        Array(w, "diagnostics", diagnostics, Diagnostic); w.WriteEndObject();
    });
    internal static void Analysis(Utf8JsonWriter w, MechanicalAnalysis a)
    {
        Start(w, AnalysisFormat, VersionFor(a.Draft.Definition)); w.WriteString("analysisId", a.AnalysisId); w.WriteString("policy", a.Policy); w.WriteString("motionDomain", a.MotionDomain);
        w.WriteString("definitionId", a.DefinitionId); w.WriteString("draftId", a.DraftId); Long(w, "revision", a.Revision); w.WriteString("selectedInputId", a.SelectedInputId);
        w.WriteString("referenceIntegrity", a.ReferenceIntegrity.ToString()); w.WriteString("declaredConnectivity", a.DeclaredConnectivity.ToString());
        w.WriteString("constraintAdmission", a.ConstraintAdmission.ToString()); w.WriteString("geometry", a.Geometry.ToString()); w.WriteString("targets", a.Targets.ToString());
        w.WriteBoolean("isMechanicallyValid", a.IsMechanicallyValid); w.WriteString("exportAdmission", a.ExportAdmission.ToString());
        Array(w, "edges", a.Edges, (x, e) => { x.WriteStartObject(); x.WriteString("kind", e.Kind); x.WriteString("id", e.Id); x.WriteString("constraintKey", e.ConstraintKey);
            x.WriteString("shaftAId", e.ShaftAId); x.WriteString("shaftBId", e.ShaftBId); x.WriteBoolean("isDeclaredResolvable", e.IsDeclaredResolvable); x.WriteBoolean("isAdmitted", e.IsAdmitted);
            x.WritePropertyName("compatibility"); Compatibility(x, e.Compatibility); x.WriteEndObject(); });
        Array(w, "declaredComponents", a.DeclaredComponents, Component); Array(w, "admittedComponents", a.AdmittedComponents, Component);
        Array(w, "outputs", a.Outputs, (x, o) =>
        {
            x.WriteStartObject(); x.WritePropertyName("binding"); Output(x, o.Binding); x.WriteBoolean("isDeclaredReachable", o.IsDeclaredReachable); x.WriteBoolean("isAdmittedReachable", o.IsAdmittedReachable);
            x.WriteString("determinacy", o.Determinacy.ToString()); x.WriteBoolean("hasDeterminedMotion", o.HasDeterminedMotion); Relation(x, "shaftRelation", o.ShaftRelation); Relation(x, "portRelation", o.PortRelation);
            OptionalVector(x, "shaftPositiveAxis", o.ShaftPositiveAxis); if (o.PortFrame is null) x.WriteNull("portFrame"); else Frame(x, "portFrame", o.PortFrame);
            Strings(x, "constraintPath", o.ConstraintPath); Strings(x, "declaredConstraintPath", o.DeclaredConstraintPath); Strings(x, "blockedPrerequisites", o.BlockedPrerequisites); x.WriteString("target", o.Target.ToString());
            Array(x, "diagnostics", o.Diagnostics, Diagnostic); x.WriteEndObject();
        });
        Array(w, "geometryChecks", a.GeometryChecks, Check); Array(w, "pitchProofs", a.PitchProofs, PitchClearanceJson.Proof);
        Array(w, "diagnostics", a.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    private static void Component(Utf8JsonWriter w, MechanicalConnectivityComponent c)
    {
        w.WriteStartObject(); w.WriteString("componentKey", c.ComponentKey); Strings(w, "shaftIds", c.ShaftIds); Strings(w, "bodyIds", c.BodyIds);
        Strings(w, "portIds", c.PortIds); Strings(w, "edgeKeys", c.EdgeKeys); Strings(w, "blockedEdgeKeys", c.BlockedEdgeKeys); Strings(w, "prescribedInputIds", c.PrescribedInputIds); Strings(w, "outputKeys", c.OutputKeys);
        w.WriteBoolean("isSelectedInputReachable", c.IsSelectedInputReachable); w.WritePropertyName("affine");
        if (c.Affine is null) w.WriteNullValue(); else Affine(w, c.Affine); w.WriteEndObject();
    }
    private static void Affine(Utf8JsonWriter w, AffineComponentAnalysis a)
    {
        w.WriteStartObject(); w.WriteString("parameterId", a.ParameterId); w.WriteString("determinacy", a.Determinacy.ToString());
        Strings(w, "memberIds", a.MemberIds); Strings(w, "constraintIds", a.ConstraintIds); Strings(w, "prescribedInputIds", a.PrescribedInputIds);
        Array(w, "relativeRelations", a.Relations, (x, r) => { x.WriteStartObject(); x.WriteString("dofId", r.DofId); Relation(x, "relation", r.Relation); Strings(x, "constraintPath", r.ConstraintPath); x.WriteEndObject(); });
        Array(w, "closures", a.Closures, (x, c) => { x.WriteStartObject(); x.WriteString("constraintId", c.ConstraintId); Derived(x, "coefficient", c.Coefficient); Derived(x, "phase", c.Phase);
            x.WriteBoolean("isRedundant", c.IsRedundant); x.WritePropertyName("existing"); Path(x, c.Existing); x.WritePropertyName("alternative"); Path(x, c.Alternative); x.WriteEndObject(); });
        OptionalDerived(w, "pinnedParameter", a.PinnedParameter); Array(w, "diagnostics", a.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    internal static void Compatibility(Utf8JsonWriter w, ConnectionCompatibilityResult c)
    {
        w.WriteStartObject(); w.WriteString("kind", c.Kind); w.WriteString("connectionId", c.ConnectionId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        Array(w, "involved", c.Involved, Reference); Array(w, "checks", c.Checks, Check); Array(w, "facts", c.Facts, Fact); OptionalDerived(w, "signedTransfer", c.SignedTransfer);
        Array(w, "diagnostics", c.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    internal static void ComparisonRequest(Utf8JsonWriter w, OutputComparisonRequest r)
    {
        w.WriteStartObject(); w.WriteString("requestId", r.RequestId); w.WriteString("beforeOutputKey", r.BeforeOutputKey); w.WriteString("afterOutputKey", r.AfterOutputKey);
        w.WriteString("beforeInputId", r.BeforeInputId); w.WriteString("afterInputId", r.AfterInputId); Fraction(w, "alpha", r.Alpha); Fraction(w, "beta", r.Beta);
        Fraction(w, "sign", r.Sign); Fraction(w, "delta", r.Delta); w.WriteString("mode", r.Mode.ToString()); w.WriteString("motionDomain", r.MotionDomain); w.WriteEndObject();
    }
    internal static OutputComparisonRequest ComparisonRequest(JsonElement p)
    {
        var request = new OutputComparisonRequest(S(p, "beforeOutputKey"), S(p, "afterOutputKey"), S(p, "beforeInputId"), S(p, "afterInputId"), F(p.GetProperty("alpha")),
            F(p.GetProperty("beta")), F(p.GetProperty("sign")), F(p.GetProperty("delta")), E<OutputComparisonMode>(p, "mode"), S(p, "motionDomain"));
        Require(request.RequestId == S(p, "requestId"), "Comparison request identity mismatch."); return request;
    }
    internal static void Comparison(Utf8JsonWriter w, OutputEquivalenceResult r)
    {
        Start(w, ComparisonFormat); w.WriteString("comparisonId", r.ComparisonId); w.WritePropertyName("request"); ComparisonRequest(w, r.Request);
        w.WriteString("verdict", r.Verdict.ToString()); w.WriteString("scope", r.Scope); Relation(w, "beforeRelation", r.BeforeRelation); Relation(w, "mappedAfterRelation", r.MappedAfterRelation);
        OptionalVector(w, "beforeWorldAngularRate", r.BeforeWorldAngularRate); OptionalVector(w, "mappedAfterWorldAngularRate", r.MappedAfterWorldAngularRate);
        w.WriteString("beforeAnalysisId", r.BeforeAnalysisId); w.WriteString("afterAnalysisId", r.AfterAnalysisId); w.WriteString("beforeGeometry", r.BeforeGeometry.ToString()); w.WriteString("afterGeometry", r.AfterGeometry.ToString());
        w.WriteString("beforeExportAdmission", r.BeforeExportAdmission?.ToString()); w.WriteString("afterExportAdmission", r.AfterExportAdmission?.ToString()); Array(w, "diagnostics", r.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    internal static void Diagnostic(Utf8JsonWriter w, MechanicalDiagnostic d)
    {
        w.WriteStartObject(); w.WriteString("code", d.Code); w.WriteString("stage", d.Stage); w.WriteString("severity", d.Severity.ToString()); w.WriteBoolean("required", d.Required);
        Array(w, "related", d.Related, Reference); Array(w, "facts", d.Facts, Fact); Strings(w, "affectedOutputs", d.AffectedOutputs); Array(w, "paths", d.Paths, Path);
        Strings(w, "blockedPrerequisites", d.BlockedPrerequisites); w.WriteString("proofReference", d.ProofReference); w.WriteString("scope", d.Scope); w.WriteBoolean("complete", d.Complete); w.WriteString("detail", d.Detail); w.WriteEndObject();
    }
    private static void Path(Utf8JsonWriter w, MechanicalPathWitness p)
    { w.WriteStartObject(); w.WriteString("rootId", p.RootId); w.WriteString("targetId", p.TargetId); Derived(w, "coefficient", p.Coefficient); Derived(w, "phase", p.Phase); Strings(w, "constraintIds", p.ConstraintIds); w.WriteEndObject(); }
    private static void Fact(Utf8JsonWriter w, MechanicalExactFact f)
    { w.WriteStartObject(); w.WriteString("key", f.Key); OptionalDerived(w, "expected", f.Expected); OptionalDerived(w, "actual", f.Actual); w.WriteEndObject(); }
    private static void Check(Utf8JsonWriter w, OrientedDomainCheck c)
    { w.WriteStartObject(); w.WriteString("domain", c.Domain); w.WriteString("subject", c.Subject); w.WriteString("verdict", c.Verdict.ToString()); w.WriteBoolean("required", c.Required); w.WriteString("detail", c.Detail); w.WriteEndObject(); }
    private static void Relation(Utf8JsonWriter w, string key, ExactAffineRelation? relation)
    { if (!relation.HasValue) { w.WriteNull(key); return; } w.WriteStartObject(key); Derived(w, "coefficient", relation.Value.Coefficient); Derived(w, "phase", relation.Value.Phase); w.WriteEndObject(); }
    private static void OptionalVector(Utf8JsonWriter w, string key, ExactVector3? vector)
    { if (!vector.HasValue) { w.WriteNull(key); return; } w.WriteStartArray(key); Derived(w, vector.Value.X); Derived(w, vector.Value.Y); Derived(w, vector.Value.Z); w.WriteEndArray(); }
    private static void OptionalDerived(Utf8JsonWriter w, string key, Rational? value)
    { if (value.HasValue) Derived(w, key, value.Value); else w.WriteNull(key); }
    private static void Derived(Utf8JsonWriter w, string key, Rational value) { w.WritePropertyName(key); Derived(w, value); }
    private static void Derived(Utf8JsonWriter w, Rational value)
    {
        var n = value.Numerator.ToString(CultureInfo.InvariantCulture); var d = value.Denominator.ToString(CultureInfo.InvariantCulture);
        Require(n.Length <= MechanicalAuthoringProfile.MaxDerivedDigits && d.Length <= MechanicalAuthoringProfile.MaxDerivedDigits, "Derived exact digit bound exceeded.");
        w.WriteStartObject(); w.WriteString("numerator", n); w.WriteString("denominator", d); w.WriteEndObject();
    }
}
