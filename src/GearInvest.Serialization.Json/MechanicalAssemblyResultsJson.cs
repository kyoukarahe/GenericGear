using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class MechanicalAssemblyJson
{
    public static byte[] WriteFinalization(string status, string definitionId, string? artifactIdentity, bool originalRootBytesPreserved,
        IEnumerable<MechanicalDiagnostic> diagnostics) => Guard(() => Encode(w =>
    {
        Start(w, "gear-invest.mechanical-assembly-finalization"); w.WriteString("status", status); w.WriteString("definitionId", definitionId);
        w.WriteString("artifactIdentity", artifactIdentity); w.WriteBoolean("originalRootBytesPreserved", originalRootBytesPreserved);
        Array(w, "diagnostics", diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }));
    public static byte[] WriteAnalysis(MechanicalAssemblyAnalysis analysis) => Guard(() => Encode(w => Analysis(w, analysis)));
    public static MechanicalAssemblyAnalysis VerifyAnalysis(MechanicalAssemblyDraft draft, AssemblyNumericRequest request, byte[] bytes) => Guard(() =>
    { var fresh = MechanicalAssemblyAnalyzer.Analyze(draft, request); Require(bytes.SequenceEqual(WriteAnalysis(fresh)), "Fresh assembly analysis differs from stored evidence."); return fresh; });
    public static byte[] WriteEvaluation(MechanicalAssemblyEvaluation evaluation) => Guard(() => Encode(w => Evaluation(w, evaluation)));
    public static byte[] WriteNumericRequest(AssemblyNumericRequest request) => Guard(() => Encode(w => NumericRequest(w, request)));
    public static AssemblyNumericRequest ReadNumericRequest(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var request = NumericRequest(doc.RootElement); Require(bytes.SequenceEqual(WriteNumericRequest(request)), "Noncanonical assembly numeric request."); return request; });
    internal static void NumericRequest(Utf8JsonWriter w, AssemblyNumericRequest request)
    {
        w.WriteStartObject(); w.WriteString("policy", request.Policy); Integer(w, "maximumWork", request.MaximumWork);
        R.Quantity(w, "angularWidth", request.AngularWidth); R.Quantity(w, "linearWidth", request.LinearWidth); Fraction(w, "directionWidth", request.DirectionWidth);
        Integer(w, "maximumPrecisionBits", request.MaximumPrecisionBits); Integer(w, "maximumRefinements", request.MaximumRefinements);
        w.WriteBoolean("includeDisplay", request.IncludeDisplay); Integer(w, "camContourSamples", request.CamContourSamples); w.WriteEndObject();
    }
    internal static AssemblyNumericRequest NumericRequest(JsonElement p) => new(I(p, "maximumWork"), R.Quantity(p.GetProperty("angularWidth")),
        R.Quantity(p.GetProperty("linearWidth")), F(p.GetProperty("directionWidth")), I(p, "maximumPrecisionBits"), I(p, "maximumRefinements"), p.GetProperty("includeDisplay").GetBoolean(), S(p, "policy"), I(p, "camContourSamples"));
    internal static void Analysis(Utf8JsonWriter w, MechanicalAssemblyAnalysis analysis)
    {
        Start(w, "gear-invest.mechanical-assembly-analysis", VersionFor(analysis.Draft.Definition.Profile)); w.WriteString("analysisId", analysis.AnalysisId); w.WriteString("definitionId", analysis.DefinitionId);
        w.WriteString("draftId", analysis.Draft.DraftId); w.WriteString("policy", analysis.Policy); w.WritePropertyName("numericRequest"); NumericRequest(w, analysis.NumericRequest);
        w.WriteBoolean("topologyValid", analysis.TopologyValid); w.WriteBoolean("isMechanicallyValid", analysis.IsMechanicallyValid); w.WriteString("exportAdmission", analysis.ExportAdmission.ToString());
        Integer(w, "numericWork", analysis.NumericWork); Integer(w, "rootAnalysisCount", analysis.RootAnalysisCount); Integer(w, "localAnalysisCount", analysis.LocalAnalysisCount);
        MechanicalAuthoringJson.Strings(w, "topologicalOrder", analysis.TopologicalOrder);
        w.WritePropertyName("rootAnalysis"); MechanicalAuthoringJson.Analysis(w, analysis.RootAnalysis);
        Array(w, "inventory", analysis.Inventory, (writer, item) =>
        {
            writer.WriteStartObject(); writer.WritePropertyName("reference"); Reference(writer, item.Reference); writer.WriteString("role", item.Role);
            writer.WritePropertyName("mountedShaft"); if (item.MountedShaft is null) writer.WriteNullValue(); else Reference(writer, item.MountedShaft);
            writer.WriteBoolean("isPrescribed", item.IsPrescribed); writer.WriteBoolean("isNonlinearDependent", item.IsNonlinearDependent); writer.WriteEndObject();
        });
        Array(w, "members", analysis.Members, (writer, member) => MemberAnalysis(writer, member, analysis.Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId));
        Array(w, "outputs", analysis.Outputs, (writer, output) =>
        {
            writer.WriteStartObject(); writer.WritePropertyName("binding"); Output(writer, output.Binding); writer.WriteBoolean("isResolved", output.IsResolved);
            writer.WriteString("capability", output.Capability?.ToString()); R.Affine(writer, "affineRelation", output.AffineRelation); writer.WriteString("target", output.Target.ToString());
            Array(writer, "diagnostics", output.Diagnostics, Diagnostic); writer.WriteEndObject();
        });
        Array(w, "checks", analysis.Checks, R.Check); Array(w, "diagnostics", analysis.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    internal static void Diagnostic(Utf8JsonWriter w, AssemblyDiagnostic diagnostic)
    {
        w.WriteStartObject(); w.WriteString("code", diagnostic.Code); w.WriteString("stage", diagnostic.Stage); w.WriteString("detail", diagnostic.Detail);
        Array(w, "related", diagnostic.Related, Reference); Array(w, "dependencyPath", diagnostic.DependencyPath, Reference);
        w.WritePropertyName("localDiagnostic"); if (diagnostic.LocalDiagnostic is null) w.WriteNullValue(); else MechanicalAuthoringJson.Diagnostic(w, diagnostic.LocalDiagnostic); w.WriteEndObject();
    }
    private static void MemberAnalysis(Utf8JsonWriter w, AssemblyMemberAnalysis member, bool suffixProfile)
    {
        w.WriteStartObject(); w.WriteString("instanceId", member.InstanceId); w.WriteString("declarationId", member.Member.Declaration.DeclarationId);
        w.WriteString("capability", member.Capability.ToString()); w.WritePropertyName("bindingAnalysis"); BindingAnalysis(w, member.Binding);
        w.WriteBoolean("localAdmitted", member.LocalAdmitted); w.WriteBoolean("hasDeterminedMotion", member.HasDeterminedMotion);
        R.Affine(w, "shaftRelation", member.ShaftRelation); R.Affine(w, "terminalRelation", member.TerminalRelation);
        if (member.LocalTransfer.HasValue) R.Derived(w, "localTransfer", member.LocalTransfer.Value); else w.WriteNull("localTransfer");
        w.WriteString("target", member.Target.ToString()); w.WriteBoolean("requiredScopesSatisfied", member.RequiredScopesSatisfied); Integer(w, "numericWork", member.NumericWork);
        w.WritePropertyName("localCompatibility");
        switch (member.LocalCompatibility)
        {
            case WormDriveCompatibilityResult local: WormDriveJson.Compatibility(w, local); break;
            case OpenBeltCompatibilityResult local: OpenBeltJson.Compatibility(w, local); break;
            case PitchChainCompatibilityResult local: PitchChainJson.Compatibility(w, local); break;
            case GenevaCompatibilityResult local: GenevaJson.Compatibility(w, local); break;
            case CrankSliderCompatibilityResult local: CrankSliderJson.Compatibility(w, local); break;
            case CamFollowerCompatibilityResult local: CamFollowerJson.Compatibility(w, local); break;
            case null: w.WriteNullValue(); break;
            default: throw new ArtifactFormatException("Unknown assembly local compatibility type.");
        }
        w.WritePropertyName("motionDescriptor");
        switch (member.MotionDescriptor)
        {
            case GenevaMotionDescriptor descriptor: GenevaJson.Descriptor(w, descriptor); break;
            case CrankSliderMotionDescriptor descriptor: CrankSliderJson.Descriptor(w, descriptor); break;
            case CamFollowerMotionDescriptor descriptor: CamFollowerJson.Descriptor(w, descriptor); break;
            case null: w.WriteNullValue(); break;
            default: throw new ArtifactFormatException("Unknown assembly descriptor type.");
        }
        if (suffixProfile)
        {
            w.WriteString("globalMotionKind", member.GlobalMotionKind);
            w.WriteString("localCapability", member.LocalCapability.ToString());
            w.WritePropertyName("genevaInputMotion"); GenevaAffineMotion(w, member.Binding.GenevaInputMotion);
            w.WritePropertyName("genevaShaftMotion"); GenevaAffineMotion(w, member.GenevaShaftMotion);
            w.WritePropertyName("genevaTerminalMotion"); GenevaAffineMotion(w, member.GenevaTerminalMotion);
        }
        Array(w, "checks", member.Checks, R.Check); Array(w, "diagnostics", member.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    private static void BindingAnalysis(Utf8JsonWriter w, AssemblyBindingAnalysis binding)
    {
        w.WriteStartObject(); w.WriteString("instanceId", binding.InstanceId); w.WriteString("dependencyIdentity", binding.DependencyIdentity);
        w.WritePropertyName("binding"); Binding(w, binding.Binding); w.WriteBoolean("structuralValidity", binding.StructuralValidity);
        w.WriteBoolean("mountingValidity", binding.MountingValidity); w.WriteBoolean("hasDeterminedInput", binding.HasDeterminedInput);
        DerivedFrame(w, "fixedInputFrameMm", binding.FixedInputFrameMm); R.Affine(w, "inputRelation", binding.InputRelation);
        Array(w, "dependencyPath", binding.DependencyPath, Reference); Array(w, "diagnostics", binding.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    internal static void Evaluation(Utf8JsonWriter w, MechanicalAssemblyEvaluation evaluation)
    {
        Start(w, "gear-invest.mechanical-assembly-evaluation", VersionFor(evaluation.Analysis.Draft.Definition.Profile)); w.WriteString("evaluationId", evaluation.EvaluationId); w.WriteString("analysisId", evaluation.AnalysisId);
        R.DerivedQuantity(w, "rootInput", evaluation.RootInput); Integer(w, "numericWork", evaluation.NumericWork);
        Integer(w, "rootEvaluationCount", evaluation.RootEvaluationCount); Integer(w, "localEvaluationCount", evaluation.LocalEvaluationCount);
        w.WriteBoolean("allRequestedNumericAvailable", evaluation.AllRequestedNumericAvailable); w.WriteBoolean("allRequestedDisplayAvailable", evaluation.AllRequestedDisplayAvailable);
        Array(w, "shafts", evaluation.Shafts, (writer, shaft) =>
        {
            writer.WriteStartObject(); writer.WritePropertyName("reference"); Reference(writer, shaft.Reference); DerivedFrame(writer, "fixedFrameMm", shaft.FixedFrameMm);
            R.DerivedQuantity(writer, "turns", shaft.Turns); R.Affine(writer, "relation", shaft.Relation);
            R.DerivedVector(writer, "worldAngularVelocityPerRoot", shaft.WorldAngularVelocityPerRoot, "turn/turn"); writer.WriteEndObject();
        });
        Array(w, "members", evaluation.Members, (writer, member) => MemberEvaluation(writer, member, evaluation.Analysis.Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId)); Array(w, "diagnostics", evaluation.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    private static void MemberEvaluation(Utf8JsonWriter w, AssemblyMemberEvaluation evaluation, bool suffixProfile)
    {
        w.WriteStartObject(); w.WriteString("instanceId", evaluation.InstanceId); w.WriteString("status", evaluation.Status); R.DerivedQuantity(w, "inputTurns", evaluation.InputTurns);
        w.WriteBoolean("numericAvailable", evaluation.NumericAvailable); w.WriteBoolean("displayAvailable", evaluation.DisplayAvailable); Integer(w, "numericWork", evaluation.NumericWork);
        w.WritePropertyName("affineOutput");
        switch (evaluation.AffineOutput)
        {
            case WormDriveOutputEvaluation output: WormDriveJson.EvaluationOutput(w, output); break;
            case OpenBeltOutputEvaluation output: OpenBeltJson.EvaluationOutput(w, output); break;
            case PitchChainOutputEvaluation output: PitchChainJson.EvaluationOutput(w, output); break;
            case null: w.WriteNullValue(); break;
            default: throw new ArtifactFormatException("Unknown assembly affine evaluation type.");
        }
        w.WritePropertyName("material");
        if (evaluation.Material is IndexedChainPoseDescriptor chain) PitchChainJson.Pose(w, chain);
        else if (evaluation.Material is WormDriveGeometryDescriptor worm) WormDriveJson.Geometry(w, worm);
        else if (evaluation.Material is OpenBeltRouteDescriptor belt) OpenBeltJson.Route(w, belt);
        else if (evaluation.Material is null) w.WriteNullValue(); else throw new ArtifactFormatException("Unknown assembly material type.");
        w.WritePropertyName("recipe");
        switch (evaluation.Recipe)
        {
            case GenevaPoseRecipe recipe: GenevaJson.PoseRecipe(w, recipe); break;
            case CrankSliderPoseRecipe recipe: CrankSliderJson.PoseRecipe(w, recipe); break;
            case CamFollowerPoseRecipe recipe: CamFollowerJson.PoseRecipe(w, recipe); break;
            case null: w.WriteNullValue(); break;
            default: throw new ArtifactFormatException("Unknown assembly pose recipe type.");
        }
        w.WritePropertyName("numeric"); NumericResult(w, evaluation.Numeric);
        w.WritePropertyName("displayNumeric");
        // Float rendering samples have separate tolerances; exact/numeric feature evidence is retained here.
        if (evaluation.Display is GenevaNumericComputation || evaluation.Display is CrankSliderNumericComputation || evaluation.Display is CamNumericResult) NumericResult(w, evaluation.Display);
        else if (evaluation.Display is GenevaDriverNumericComputation driver) GenevaJson.DriverNumeric(w, driver);
        else if (evaluation.Display is AssemblyCamDisplaySamples contour) CamDisplay(w, contour);
        else w.WriteNullValue();
        w.WritePropertyName("inputObservation"); InputObservation(w, evaluation.InputObservation);
        if (suffixProfile) { w.WritePropertyName("genevaSuffix"); GenevaSuffix(w, evaluation.GenevaSuffix); }
        Array(w, "diagnostics", evaluation.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    private static void NumericResult(Utf8JsonWriter w, object? numeric)
    {
        switch (numeric)
        {
            case GenevaNumericComputation result: GenevaJson.NumericComputation(w, result); break;
            case CrankSliderNumericComputation result: CrankSliderJson.NumericComputation(w, result); break;
            case CamNumericResult result: CamFollowerJson.NumericComputation(w, result); break;
            case null: w.WriteNullValue(); break;
            default: throw new ArtifactFormatException("Unknown assembly numeric result type.");
        }
    }
    private static void CamDisplay(Utf8JsonWriter w, AssemblyCamDisplaySamples display)
    {
        w.WriteStartObject(); w.WriteString("resultId", display.ResultId); w.WriteString("analysisId", display.AnalysisId);
        R.DerivedQuantity(w, "rootTurns", display.RootTurns); Integer(w, "requestedSamples", display.RequestedSamples); Integer(w, "completedSamples", display.CompletedSamples);
        w.WriteBoolean("isAvailable", display.IsAvailable); w.WriteString("status", display.Status.ToString()); Integer(w, "numericWork", display.NumericWork); w.WriteString("detail", display.Detail);
        Array(w, "contourPointsMm", display.ContourPointsMm, (writer, point) => { writer.WriteStartObject(); CamFollowerJson.VectorInterval(writer, "point", point, "mm"); writer.WriteEndObject(); });
        if (display.MaterialMarkerMm is null) w.WriteNull("materialMarkerMm"); else CamFollowerJson.VectorInterval(w, "materialMarkerMm", display.MaterialMarkerMm, "mm");
        Array(w, "sampleResults", display.SampleResults, CamFollowerJson.NumericComputation); w.WriteEndObject();
    }
    private static void InputObservation(Utf8JsonWriter w, object? observation)
    {
        if (observation is null) { w.WriteNullValue(); return; }
        string body; ExactVector3 center, axis, zero; Rational turns; Rational? phase;
        switch (observation)
        {
            case GenevaDriverObservation value: body = value.BodyId; center = value.CenterMm; axis = value.PositiveAxis; zero = value.ZeroRay; turns = value.SourceTurns; phase = value.PhysicalPhaseTurns; break;
            case CrankSliderCrankEvaluation value: body = value.BodyId; center = value.PivotMm; axis = value.PositiveAxis; zero = value.ZeroRay; turns = value.SourceTurns; phase = value.PhysicalPhaseTurns; break;
            case CamFollowerCamEvaluation value: body = value.BodyId; center = value.CenterMm; axis = value.PositiveAxis; zero = value.ZeroRay; turns = value.SourceTurns; phase = value.PhysicalPhaseTurns; break;
            default: throw new ArtifactFormatException("Unknown assembly input observation type.");
        }
        w.WriteStartObject(); w.WriteString("bodyId", body); R.DerivedVector(w, "centerMm", center, "mm"); R.DerivedVector(w, "positiveAxis", axis, "dimensionless");
        R.DerivedVector(w, "zeroRay", zero, "dimensionless"); R.Derived(w, "sourceTurns", turns);
        if (phase.HasValue) R.Derived(w, "physicalPhaseTurns", phase.Value); else w.WriteNull("physicalPhaseTurns");
        if (observation is CrankSliderCrankEvaluation crank) R.Derived(w, "mountingTurns", crank.MountingTurns); w.WriteEndObject();
    }
    private static void DerivedFrame(Utf8JsonWriter w, string key, OrientedFrame? frame)
    {
        if (frame is null) { w.WriteNull(key); return; }
        w.WriteStartObject(key); R.DerivedVector(w, "origin", frame.Origin, "mm"); R.DerivedVector(w, "x", frame.X, "dimensionless");
        R.DerivedVector(w, "y", frame.Y, "dimensionless"); R.DerivedVector(w, "z", frame.Z, "dimensionless"); w.WriteEndObject();
    }
}
