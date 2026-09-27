using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class PitchChainJson
{
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, string? artifactBytesHash, IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.pitch-chain-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId); w.WriteString("artifactIdentity", artifactIdentity); w.WriteString("artifactBytesHash", artifactBytesHash); Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject(); }));
    public static byte[] WriteAnalysis(PitchChainAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static PitchChainAnalysis VerifyAnalysis(PitchChainDraft draft, byte[] bytes)
    { var a = PitchChainAnalyzer.Analyze(draft); Require(bytes.SequenceEqual(WriteAnalysis(a)), "Stored pitch chain analysis differs from fresh current definition analysis."); return a; }
    public static byte[] WriteEvaluation(PitchChainEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static PitchChainEvaluation VerifyEvaluation(PitchChainAnalysis analysis, ExactQuantity input, byte[] bytes)
    { var evaluation = PitchChainAnalyzer.Evaluate(analysis, input); Require(bytes.SequenceEqual(WriteEvaluation(evaluation)), "Stored pitch chain evaluation differs from fresh absolute-input evaluation."); return evaluation; }
    public static byte[] WriteCompatibility(PitchChainCompatibilityResult compatibility) => Guard(() => Encode(w => Compatibility(w, compatibility)));
    public static byte[] WriteComparison(PitchChainOutputEquivalenceResult comparison) => Guard(() => Encode(w => Comparison(w, comparison)));
    private static void Analysis(Utf8JsonWriter w, PitchChainAnalysis a)
    {
        Start(w, AnalysisFormat); w.WriteString("geometrySemantics", PitchChainProfile.GeometrySemantics); w.WriteString("poseSemantics", PitchChainProfile.PoseSemantics); w.WriteString("analysisId", a.AnalysisId);
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
        w.WriteStartObject("inventory"); Integer(w, "rigidRotatingBodies", a.RigidRotatingBodyCount); Integer(w, "shaftDofs", a.ShaftDofCount);
        Integer(w, "requiredPins", a.RequiredPinCount); Integer(w, "selectedPins", a.SelectedPinCount); Integer(w, "admittedPins", a.AdmittedPinCount);
        Integer(w, "admittedIdealLinks", a.AdmittedIdealLinkCount); Integer(w, "sourceContacts", a.SourceContactCount);
        Integer(w, "chainTransmissionConstraints", a.ChainTransmissionConstraintCount); Integer(w, "prescribedDrivers", a.Nodes.Count(n => n.IsPrescribed)); w.WriteEndObject();
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
    internal static void Compatibility(Utf8JsonWriter w, PitchChainCompatibilityResult c)
    {
        Start(w, "gear-invest.pitch-chain-compatibility"); w.WriteString("deviceId", c.DeviceId); w.WriteString("scope", c.Scope); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteBoolean("transmissionPresent", c.TransmissionPresent); w.WriteBoolean("isAdmitted", c.IsAdmitted);
        R.DerivedVector(w, "inputCenterMm", c.InputCenterMm, "mm");
        R.DerivedVector(w, "inputPositiveAxis", c.InputPositiveAxis, "dimensionless"); R.DerivedVector(w, "outputPositiveAxis", c.OutputPositiveAxis, "dimensionless");
        OptionalDerived(w, "inputAxisRouteSign", c.InputAxisRouteSign); OptionalDerived(w, "outputAxisRouteSign", c.OutputAxisRouteSign);
        OptionalDerived(w, "sourcePortCoordinateSign", c.SourcePortCoordinateSign); OptionalDerived(w, "terminalCoordinateSign", c.TerminalCoordinateSign);
        OptionalDerived(w, "prospectiveTransfer", c.ProspectiveTransfer); OptionalDerived(w, "admittedTransfer", c.AdmittedTransfer);
        w.WritePropertyName("geometry"); Geometry(w, c.Geometry); w.WritePropertyName("phaseRegistration"); Phase(w, c.PhaseRegistration);
        w.WritePropertyName("linkCompatibility"); LinkCompatibility(w, c.LinkCompatibility); Array(w, "facts", c.Facts, Fact);
        Array(w, "checks", c.Checks, R.Check); Array(w, "diagnostics", c.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    private static void LinkCompatibility(Utf8JsonWriter w, PitchChainLinkCompatibility c)
    {
        w.WriteStartObject(); w.WriteString("compatibilityId", c.CompatibilityId); w.WriteString("verdict", c.Verdict.ToString());
        w.WriteString("selectedSpecificationId", c.SelectedSpecificationId); OptionalInteger(w, "requiredLinkCount", c.RequiredLinkCount); OptionalInteger(w, "selectedLinkCount", c.SelectedLinkCount);
        OptionalDerived(w, "requiredPitchMm", c.RequiredPitchMm); if (c.SelectedPitch.HasValue) R.DerivedQuantity(w, "selectedPitch", c.SelectedPitch.Value); else w.WriteNull("selectedPitch");
        Array(w, "facts", c.Facts, Fact); w.WriteString("detail", c.Detail); w.WriteEndObject();
    }
    private static void Geometry(Utf8JsonWriter w, PitchChainGeometryDescriptor? g)
    {
        if (g is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("geometryId", g.GeometryId); w.WriteString("semantics", g.Semantics);
        w.WritePropertyName("pitch"); Pitch(w, g.Pitch); R.DerivedVector(w, "inputCenterMm", g.InputCenterMm, "mm"); R.DerivedVector(w, "outputCenterMm", g.OutputCenterMm, "mm");
        R.DerivedVector(w, "normal", g.Normal, "dimensionless"); R.DerivedVector(w, "centerDirection", g.CenterDirection, "dimensionless"); R.DerivedVector(w, "sideDirection", g.SideDirection, "dimensionless");
        R.Derived(w, "centerDistanceMm", g.CenterDistanceMm); Integer(w, "centerPitchCount", g.CenterPitchCount); Integer(w, "requiredLinkCount", g.RequiredLinkCount);
        R.Derived(w, "contourLengthMm", g.ContourLengthMm); w.WriteString("traversal", g.Traversal); Array(w, "proofs", g.Proofs, Proof); w.WriteBoolean("hasExactProof", g.HasExactProof); w.WriteEndObject();
    }
    private static void Pitch(Utf8JsonWriter w, RegularSprocketPitchDescriptor p)
    {
        w.WriteStartObject(); w.WriteString("descriptorId", p.DescriptorId); w.WriteString("semantics", p.Semantics); Integer(w, "toothCount", p.ToothCount);
        R.Derived(w, "pitchMm", p.PitchMm); R.Derived(w, "chordLengthSquaredMm2", p.ChordLengthSquaredMm); R.Derived(w, "polygonContourLengthMm", p.PolygonContourLengthMm);
        w.WriteString("radiusRecipe", p.RadiusRecipe); w.WriteString("radiusBranch", p.RadiusBranch); w.WriteEndObject();
    }
    private static void Phase(Utf8JsonWriter w, PitchChainPhaseRegistration? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("registrationId", p.RegistrationId);
        R.Derived(w, "inputGammaTurns", p.InputGammaTurns); R.Derived(w, "outputGammaTurns", p.OutputGammaTurns);
        R.Derived(w, "inputAxisRouteSign", p.InputAxisRouteSign); R.Derived(w, "outputAxisRouteSign", p.OutputAxisRouteSign);
        R.Derived(w, "inputReferenceTurns", p.InputReferenceTurns); R.Derived(w, "outputReferenceTurns", p.OutputReferenceTurns);
        R.Derived(w, "inputPhysicalReferenceTurns", p.InputPhysicalReferenceTurns); R.Derived(w, "outputPhysicalReferenceTurns", p.OutputPhysicalReferenceTurns);
        R.Derived(w, "requiredPhysicalDifferenceTurns", p.RequiredPhysicalDifferenceTurns); ExactInteger(w, "toothRegistration", p.ToothRegistration, true);
        R.Derived(w, "residualTurns", p.ResidualTurns); w.WriteBoolean("isCompatible", p.IsCompatible); w.WritePropertyName("proof"); Proof(w, p.Proof); w.WriteEndObject();
    }
    private static void Proof(Utf8JsonWriter w, PitchChainExactProof p)
    {
        w.WriteStartObject(); w.WriteString("proofId", p.ProofId); w.WriteString("id", p.Id); w.WriteString("scope", p.Scope); w.WriteString("theorem", p.Theorem);
        w.WriteBoolean("isVerified", p.IsVerified); Array(w, "facts", p.Facts, Fact); w.WriteString("detail", p.Detail); w.WriteEndObject();
    }
    internal static void Pose(Utf8JsonWriter w, IndexedChainPoseDescriptor? p)
    {
        if (p is null) { w.WriteNullValue(); return; }
        Require(p.Pins.Count <= PitchChainProfile.MaxMaterialPoses && p.Links.Count <= PitchChainProfile.MaxLinks, "Pitch chain material pose bound exceeded.");
        w.WriteStartObject(); w.WriteString("poseId", p.PoseId); w.WriteString("semantics", p.Semantics); w.WriteString("phaseRegistrationId", p.PhaseRegistrationId);
        w.WriteString("selectedSpecificationId", p.SelectedSpecificationId); w.WriteString("chainId", p.ChainId); w.WriteString("materialRegistrationId", p.MaterialRegistrationId);
        ExactInteger(w, "materialRegistration", p.MaterialRegistration, true); R.Derived(w, "inputPhysicalPhaseTurns", p.InputPhysicalPhaseTurns);
        R.Derived(w, "outputPhysicalPhaseTurns", p.OutputPhysicalPhaseTurns); R.Derived(w, "referencePhysicalPhaseTurns", p.ReferencePhysicalPhaseTurns);
        ExactInteger(w, "unwrappedSupportIndex", p.UnwrappedSupportIndex, true); ExactInteger(w, "referenceSupportIndex", p.ReferenceSupportIndex, true);
        ExactInteger(w, "materialIndexAdvance", p.MaterialIndexAdvance, true); Integer(w, "materialRouteShift", p.MaterialRouteShift);
        R.Derived(w, "supportResidualTurns", p.SupportResidualTurns); R.Derived(w, "chainCirculationPhysicalTurns", p.ChainCirculationPhysicalTurns);
        R.Derived(w, "fullLabeledAssemblyPeriodPhysicalTurns", p.FullLabeledAssemblyPeriodPhysicalTurns);
        w.WritePropertyName("geometry"); Geometry(w, p.Geometry);
        Array(w, "pins", p.Pins, (x, pin) =>
        {
            x.WriteStartObject(); x.WriteString("pinId", pin.PinId); Integer(x, "materialIndex", pin.MaterialIndex); Integer(x, "routeIndex", pin.RouteIndex);
            x.WriteString("section", pin.Section.ToString()); x.WriteString("sprocketBodyId", pin.SprocketBodyId); Integer(x, "toothIndex", pin.ToothIndex); x.WriteBoolean("isPolygonVertex", pin.IsPolygonVertex);
            x.WritePropertyName("position"); Point(x, pin.Position); x.WriteEndObject();
        });
        Array(w, "links", p.Links, (x, link) =>
        {
            x.WriteStartObject(); x.WriteString("linkId", link.LinkId); Integer(x, "materialIndex", link.MaterialIndex); x.WriteString("startPinId", link.StartPinId); x.WriteString("endPinId", link.EndPinId);
            x.WriteString("alternation", link.Alternation); R.Derived(x, "lengthSquaredMm2", link.LengthSquaredMm); x.WriteString("proofKind", link.ProofKind); x.WriteEndObject();
        });
        Array(w, "proofs", p.Proofs, Proof); w.WriteBoolean("hasExactProof", p.HasExactProof); w.WriteEndObject();
    }
    private static void Point(Utf8JsonWriter w, PitchChainPointRecipe p)
    {
        w.WriteStartObject(); w.WriteString("recipeId", p.RecipeId); w.WriteString("pitchDescriptorId", p.Pitch.DescriptorId);
        w.WriteString("representation", "center-plus-integer-pitch-and-regular-polygon-radius-v1"); w.WriteString("unit", "mm");
        R.DerivedVector(w, "centerMm", p.CenterMm, "mm"); R.DerivedVector(w, "centerDirection", p.CenterDirection, "dimensionless"); R.DerivedVector(w, "sideDirection", p.SideDirection, "dimensionless");
        R.Derived(w, "angleTurns", p.AngleTurns); Integer(w, "pitchOffset", p.PitchOffset); w.WriteEndObject();
    }
    private static void OptionalInteger(Utf8JsonWriter w, string key, int? value)
    { if (value.HasValue) Integer(w, key, value.Value); else w.WriteNull(key); }
    private static void Fact(Utf8JsonWriter w, MechanicalExactFact f)
    { w.WriteStartObject(); w.WriteString("key", f.Key); OptionalDerived(w, "expected", f.Expected); OptionalDerived(w, "actual", f.Actual); w.WriteEndObject(); }
    private static void Path(Utf8JsonWriter w, MechanicalPathWitness p)
    {
        w.WriteStartObject(); w.WriteString("rootId", p.RootId); w.WriteString("targetId", p.TargetId); R.Derived(w, "coefficient", p.Coefficient);
        R.Derived(w, "phase", p.Phase); MechanicalAuthoringJson.Strings(w, "constraintIds", p.ConstraintIds); w.WriteEndObject();
    }
    private static void OptionalDerived(Utf8JsonWriter w, string key, Rational? value)
    { if (value.HasValue) R.Derived(w, key, value.Value); else w.WriteNull(key); }
    private static void Evaluation(Utf8JsonWriter w, PitchChainEvaluation e)
    {
        Start(w, "gear-invest.pitch-chain-evaluation"); w.WriteString("geometrySemantics", PitchChainProfile.GeometrySemantics); w.WriteString("poseSemantics", PitchChainProfile.PoseSemantics); w.WriteString("analysisId", e.AnalysisId);
        w.WriteString("status", e.Status.ToString()); R.DerivedQuantity(w, "input", e.Input);
        Array(w, "rotary", e.Rotary, (x, r) =>
        {
            x.WriteStartObject(); x.WriteString("kind", "AngularPosition"); x.WriteString("shaftId", r.ShaftId); R.DerivedQuantity(x, "turns", ExactQuantity.FromCanonical(QuantityKind.AngularPosition, r.Turns));
            R.DerivedVector(x, "positiveAxis", r.PositiveAxis, "dimensionless"); R.DerivedVector(x, "worldAngularVelocityPerRoot", r.WorldAngularVelocityPerRoot, "turn/turn"); x.WriteEndObject();
        });
        w.WritePropertyName("output"); EvaluationOutput(w, e.Output); w.WritePropertyName("chainPose"); Pose(w, e.ChainPose); Array(w, "diagnostics", e.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
    internal static void EvaluationOutput(Utf8JsonWriter w, PitchChainOutputEvaluation? o)
    {
        if (o is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("outputKey", o.OutputKey); w.WriteString("shaftId", o.ShaftId); w.WriteString("sprocketBodyId", o.SprocketBodyId); w.WriteString("terminalId", o.TerminalId);
        R.DerivedQuantity(w, "inputSprocketTurns", o.InputSprocketTurns); R.DerivedQuantity(w, "shaftTurns", o.ShaftTurns); R.DerivedQuantity(w, "terminalTurns", o.TerminalTurns);
        R.DerivedVector(w, "shaftPositiveAxis", o.ShaftPositiveAxis, "dimensionless"); R.DerivedVector(w, "sprocketCenterMm", o.SprocketCenterMm, "mm");
        R.Derived(w, "shaftGain", o.ShaftGain); R.Derived(w, "terminalGain", o.TerminalGain);
        w.WriteEndObject();
    }

    private static void Comparison(Utf8JsonWriter w, PitchChainOutputEquivalenceResult r)
    {
        Start(w, "gear-invest.pitch-chain-output-comparison"); w.WriteString("geometrySemantics", PitchChainProfile.GeometrySemantics); w.WriteString("poseSemantics", PitchChainProfile.PoseSemantics); w.WriteString("comparisonId", r.ComparisonId);
        w.WritePropertyName("motion"); MechanicalAuthoringJson.Comparison(w, r.Motion);
        w.WriteString("beforeLinkVerdict", r.BeforeLinkVerdict.ToString()); w.WriteString("afterLinkVerdict", r.AfterLinkVerdict.ToString());
        w.WriteString("beforeGeometryId", r.BeforeGeometryId); w.WriteString("afterGeometryId", r.AfterGeometryId);
        if (r.SameGeometry.HasValue) w.WriteBoolean("sameGeometry", r.SameGeometry.Value); else w.WriteNull("sameGeometry");
        w.WriteString("beforePhaseRegistrationId", r.BeforePhaseRegistrationId); w.WriteString("afterPhaseRegistrationId", r.AfterPhaseRegistrationId);
        w.WriteString("beforeChainSpecificationId", r.BeforeChainSpecificationId); w.WriteString("afterChainSpecificationId", r.AfterChainSpecificationId);
        w.WriteBoolean("sameChainSpecification", r.SameChainSpecification);
        w.WriteString("beforeMaterialConstructionId", r.BeforeMaterialConstructionId); w.WriteString("afterMaterialConstructionId", r.AfterMaterialConstructionId);
        if (r.SameMaterialConstruction.HasValue) w.WriteBoolean("sameMaterialConstruction", r.SameMaterialConstruction.Value); else w.WriteNull("sameMaterialConstruction");
        w.WriteBoolean("beforeWholeValid", r.BeforeWholeValid); w.WriteBoolean("afterWholeValid", r.AfterWholeValid); w.WriteEndObject();
    }
}
