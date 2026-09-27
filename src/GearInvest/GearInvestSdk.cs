using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Modules.Clock;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed class ArtifactValidationResult
{
    public ArtifactValidationResult(ValidationBundle validation, ArtifactIdentityVerification identity)
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public ValidationBundle Validation { get; }

    public ArtifactIdentityVerification Identity { get; }

    public bool IsValid => Validation.IsValid && Identity.CandidateIdMatches && Identity.ArtifactHashMatches;
}

public sealed partial class GearInvestSdk
{
    public DiscreteLayoutNormalization NormalizeDiscreteLayoutRequest(DiscreteLayoutRequest request) => new ParametricDiscreteLayoutGenerator().Normalize(request);
    public DiscreteLayoutGeneration GenerateDiscreteLayouts(DiscreteEmbodimentModel model, DiscreteLayoutRequest request, CancellationToken cancellationToken = default, Action<int, int>? progress = null) =>
        new ParametricDiscreteLayoutGenerator().Generate(model, request, cancellationToken, progress);
    public byte[] WriteDiscreteLayoutRequest(DiscreteLayoutRequest request) => Serialization.DiscreteLayoutJson.WriteRequest(request);
    public DiscreteLayoutRequest ReadDiscreteLayoutRequest(byte[] bytes) => Serialization.DiscreteLayoutJson.ReadRequest(bytes);
    public byte[] WriteDiscreteLayoutGeneration(DiscreteEmbodimentModel model, DiscreteLayoutGeneration result) => Serialization.DiscreteLayoutJson.WriteGeneration(model, result);
    public DiscreteLayoutGeneration ReadDiscreteLayoutGeneration(DiscreteEmbodimentModel model, byte[] bytes) => Serialization.DiscreteLayoutJson.ReadGeneration(model, bytes);
    public DiscreteLayoutAssets ReadDiscreteLayoutAssets(byte[] model, byte[] mechanism, DiscreteLayoutSource source) => new DiscreteLayoutAssets(this, model, mechanism, source);
    public GeometricDiscreteResult EvaluateDiscreteLayout(DiscreteLayoutAssets assets, DiscreteLayoutGeneration generation, string selectedId, DiscreteLayoutEvaluation setup, CancellationToken token = default) =>
        DiscreteLayoutProjects.Evaluate(this, assets, generation, selectedId, setup, token);
    public DiscreteLayoutProjectManifest SaveDiscreteLayoutProject(DiscreteLayoutProject project, string directory, bool overwrite = false) => DiscreteLayoutProjects.Save(this, project, directory, overwrite);
    public DiscreteLayoutProject LoadDiscreteLayoutProject(string directory) => DiscreteLayoutProjects.Load(this, directory);
    public DiscreteLayoutProject RegenerateDiscreteLayoutProject(DiscreteLayoutProject project, CancellationToken token = default, Action<int, int>? progress = null) => DiscreteLayoutProjects.Regenerate(this, project, token, progress);
    public DiscreteCompilationResult CompileDiscreteEmbodiment(GuardedIndexedStateTransitionPlan plan, DiscreteProjectionRequest projection) =>
        new DiscreteEmbodimentCompiler().Compile(plan, projection);

    public DiscreteValidation ValidateDiscreteEmbodiment(DiscreteEmbodimentModel model) => new DiscreteEmbodimentCompiler().Validate(model);

    public DiscreteActuationResult EvaluateDiscreteEmbodiment(DiscreteEmbodimentModel model, DiscreteEmbodimentState initial,
        ContinuousEventBridgeResult events, int maxOccurrences, CancellationToken cancellationToken = default) =>
        new DiscreteEmbodimentEvaluator().Evaluate(model, initial, events, maxOccurrences, cancellationToken);

    public DiscreteValidation ValidateDiscreteActuationResult(DiscreteEmbodimentModel model, DiscreteActuationResult result) =>
        new DiscreteEmbodimentEvaluator().ValidateResult(model, result);

    public DiscreteActuationFrame SampleDiscreteActuationFrame(DiscreteEmbodimentModel model, DiscreteActuationCycle cycle, Rational phase) =>
        new DiscreteEmbodimentEvaluator().Sample(model, cycle, phase);

    public byte[] WriteDiscreteEmbodiment(DiscreteEmbodimentModel model) => Serialization.DiscreteEmbodimentJson.WriteModel(model);
    public DiscreteEmbodimentModel ReadDiscreteEmbodiment(byte[] bytes) => Serialization.DiscreteEmbodimentJson.ReadModel(bytes);
    public DiscreteGeometryGenerationResult CreateDiscreteGeometryCandidates(DiscreteEmbodimentModel model, DiscreteGeometryRequest request, CancellationToken cancellationToken = default) =>
        new DiscreteGeometryGenerator().Generate(model, request, cancellationToken);
    public DiscreteGeometryValidation ValidateDiscreteGeometry(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry) => new DiscreteGeometryGenerator().Validate(model, geometry);
    public DiscreteGeometryHit ReadDiscreteGeometryProbe(DiscreteGeometryCandidate geometry, string probeId, Rational wheelTurns) => GearInvest.Layout.AxialTrackQuery.Read(geometry, probeId, wheelTurns);
    public GeometricDiscreteResult EvaluateGeometricDiscreteEmbodiment(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, DiscreteEmbodimentState initial,
        ContinuousEventBridgeResult events, int maxOccurrences, CancellationToken cancellationToken = default) => new GeometricDiscreteEvaluator().Evaluate(model, geometry, initial, events, maxOccurrences, cancellationToken);
    public DiscreteGeometryValidation ValidateGeometricDiscreteResult(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, GeometricDiscreteResult result) => new GeometricDiscreteEvaluator().ValidateResult(model, geometry, result);
    public GeometricDiscreteFrame SampleGeometricDiscreteFrame(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, GeometricDiscreteCycle cycle, Rational phase) => new GeometricDiscreteEvaluator().Sample(model, geometry, cycle, phase);
    public byte[] WriteDiscreteGeometry(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry) => Serialization.DiscreteGeometryJson.WriteGeometry(model, geometry);
    public DiscreteGeometryCandidate ReadDiscreteGeometry(DiscreteEmbodimentModel model, byte[] bytes) => Serialization.DiscreteGeometryJson.ReadGeometry(model, bytes);
    public byte[] WriteGeometricDiscreteResult(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, GeometricDiscreteResult result) => Serialization.DiscreteGeometryJson.WriteResult(model, geometry, result);
    public GeometricDiscreteResult ReadGeometricDiscreteResult(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, byte[] bytes) => Serialization.DiscreteGeometryJson.ReadResult(model, geometry, bytes);
    public byte[] WriteDiscreteActuationResult(DiscreteEmbodimentModel model, DiscreteActuationResult result) => Serialization.DiscreteEmbodimentJson.WriteResult(model, result);
    public DiscreteActuationResult ReadDiscreteActuationResult(DiscreteEmbodimentModel model, byte[] bytes) => Serialization.DiscreteEmbodimentJson.ReadResult(model, bytes);

    private readonly GenerationEngine _engine;
    private readonly CanonicalMechanismJson _serializer;
    private readonly CanonicalKinematicSynthesisJson _synthesisSerializer;
    private readonly CanonicalSpatialLayoutJson _spatialLayoutSerializer;
    private readonly CanonicalExactRatioMechanismGenerationJson _generationSerializer;
    private readonly CanonicalSemanticCompilationJson _semanticCompilationSerializer;
    private readonly CanonicalCompiledPlanGenerationJson _compiledPlanGenerationSerializer;
    private readonly CanonicalContinuousPlanCompositionJson _continuousPlanCompositionSerializer;
    private readonly CanonicalBranchedContinuousCompositionJson _branchedContinuousCompositionSerializer;
    private readonly CanonicalContinuousEventBridgeJson _continuousEventBridgeSerializer;
    private readonly CanonicalHybridStateTransitionJson _hybridStateTransitionSerializer;
    private readonly CanonicalGuardedHybridStateTransitionJson _guardedHybridStateTransitionSerializer;
    private readonly CanonicalHybridStateSnapshotJson _hybridStateSnapshotSerializer;
    private readonly ExactForwardPeriodicEventBridge _continuousEventBridge;
    private readonly CyclicIndexedStateTransitionEngine _hybridStateTransitionEngine;
    private readonly GuardedIndexedStateTransitionEngine _guardedHybridStateTransitionEngine;
    private readonly CompositeGuardedIndexedStateTransitionEngine _compositeGuardedHybridStateTransitionEngine;
    private readonly ComputedGuardedIndexedStateTransitionEngine _computedGuardedHybridStateTransitionEngine;
    private readonly HybridStateSnapshotValidator _hybridStateSnapshotValidator;
    private readonly ClockRecipeCompiler _clockCompiler;

    private GearInvestSdk(
        GenerationEngine engine,
        CanonicalMechanismJson serializer,
        CanonicalKinematicSynthesisJson synthesisSerializer,
        CanonicalSpatialLayoutJson spatialLayoutSerializer,
        CanonicalExactRatioMechanismGenerationJson generationSerializer,
        CanonicalSemanticCompilationJson semanticCompilationSerializer,
        CanonicalCompiledPlanGenerationJson compiledPlanGenerationSerializer,
        CanonicalContinuousPlanCompositionJson continuousPlanCompositionSerializer,
        CanonicalBranchedContinuousCompositionJson branchedContinuousCompositionSerializer,
        CanonicalContinuousEventBridgeJson continuousEventBridgeSerializer,
        CanonicalHybridStateTransitionJson hybridStateTransitionSerializer,
        CanonicalGuardedHybridStateTransitionJson guardedHybridStateTransitionSerializer,
        CanonicalHybridStateSnapshotJson hybridStateSnapshotSerializer,
        ExactForwardPeriodicEventBridge continuousEventBridge,
        CyclicIndexedStateTransitionEngine hybridStateTransitionEngine,
        GuardedIndexedStateTransitionEngine guardedHybridStateTransitionEngine,
        CompositeGuardedIndexedStateTransitionEngine compositeGuardedHybridStateTransitionEngine,
        ComputedGuardedIndexedStateTransitionEngine computedGuardedHybridStateTransitionEngine,
        HybridStateSnapshotValidator hybridStateSnapshotValidator,
        ClockRecipeCompiler clockCompiler)
    {
        _engine = engine;
        _serializer = serializer;
        _synthesisSerializer = synthesisSerializer;
        _spatialLayoutSerializer = spatialLayoutSerializer;
        _generationSerializer = generationSerializer;
        _semanticCompilationSerializer = semanticCompilationSerializer;
        _compiledPlanGenerationSerializer = compiledPlanGenerationSerializer;
        _continuousPlanCompositionSerializer = continuousPlanCompositionSerializer;
        _branchedContinuousCompositionSerializer = branchedContinuousCompositionSerializer;
        _continuousEventBridgeSerializer = continuousEventBridgeSerializer;
        _hybridStateTransitionSerializer = hybridStateTransitionSerializer;
        _guardedHybridStateTransitionSerializer = guardedHybridStateTransitionSerializer;
        _hybridStateSnapshotSerializer = hybridStateSnapshotSerializer;
        _continuousEventBridge = continuousEventBridge;
        _hybridStateTransitionEngine = hybridStateTransitionEngine;
        _guardedHybridStateTransitionEngine = guardedHybridStateTransitionEngine;
        _compositeGuardedHybridStateTransitionEngine = compositeGuardedHybridStateTransitionEngine;
        _computedGuardedHybridStateTransitionEngine = computedGuardedHybridStateTransitionEngine;
        _hybridStateSnapshotValidator = hybridStateSnapshotValidator;
        _clockCompiler = clockCompiler;
    }

    public static GearInvestSdk CreateDefault()
    {
        return new GearInvestSdk(
            new GenerationEngine(),
            new CanonicalMechanismJson(),
            new CanonicalKinematicSynthesisJson(),
            new CanonicalSpatialLayoutJson(),
            new CanonicalExactRatioMechanismGenerationJson(),
            new CanonicalSemanticCompilationJson(),
            new CanonicalCompiledPlanGenerationJson(),
            new CanonicalContinuousPlanCompositionJson(),
            new CanonicalBranchedContinuousCompositionJson(),
            new CanonicalContinuousEventBridgeJson(),
            new CanonicalHybridStateTransitionJson(),
            new CanonicalGuardedHybridStateTransitionJson(),
            new CanonicalHybridStateSnapshotJson(),
            new ExactForwardPeriodicEventBridge(),
            new CyclicIndexedStateTransitionEngine(),
            new GuardedIndexedStateTransitionEngine(),
            new CompositeGuardedIndexedStateTransitionEngine(),
            new ComputedGuardedIndexedStateTransitionEngine(),
            new HybridStateSnapshotValidator(),
            new ClockRecipeCompiler());
    }

    public GenerationResult Generate(LowLevelMechanicalSpecification specification)
    {
        return _engine.Generate(specification);
    }

    public KinematicSynthesisResult SynthesizeExactRatio(
        ExactRatioSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        return _engine.SynthesizeExactRatio(request, cancellationToken);
    }

    public byte[] WriteKinematicSynthesisResult(KinematicSynthesisResult result)
    {
        return _synthesisSerializer.Write(result);
    }

    public SpatialLayoutResult CreateLayouts(
        KinematicSynthesisCandidate candidate,
        SpatialLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        return _engine.CreateLayouts(candidate, request, cancellationToken);
    }

    public ExactRatioMechanismGenerationResult GenerateExactRatioMechanisms(
        ExactRatioMechanismGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return _engine.GenerateExactRatioMechanisms(request, cancellationToken);
    }

    public SemanticCompilationResult CompileSemanticSpecification(
        SemanticPeriodicSpecification specification,
        SemanticCompilerFingerprint? compilerFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        var compiler = new PeriodicSemanticCompiler(compilerFingerprint);
        return compiler.Compile(specification, cancellationToken: cancellationToken);
    }

    public SemanticCompilationResult CompileClockRecipe(
        ClockDisplayRecipe recipe,
        ClockCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _clockCompiler.Compile(recipe, options, cancellationToken);
    }

    public CompiledGenerationPlan LowerCompiledPlan(
        CompiledMechanicalRequirementPlan plan,
        ExactRatioPlanGenerationPolicy policy)
    {
        return _engine.LowerCompiledPlan(plan, policy);
    }

    public CompiledPlanGenerationResult GenerateCompiledPlan(
        SemanticCompilationResult compilation,
        ExactRatioPlanGenerationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        return _engine.GenerateCompiledPlan(compilation, policy, cancellationToken);
    }

    public SelectedPlanEdgeMechanism SelectPlanEdgeMechanism(
        CompiledPlanGenerationEdgeResult edgeResult,
        int candidateIndex = 0)
    {
        if (edgeResult is null) throw new ArgumentNullException(nameof(edgeResult));
        if (candidateIndex < 0 || candidateIndex >= edgeResult.GenerationResult.Candidates.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateIndex));
        }

        var candidate = edgeResult.GenerationResult.Candidates[candidateIndex];
        var artifact = _serializer.Write(candidate.Candidate, ArtifactMetadata.CreateGenerated(candidate)).Artifact;
        return new SelectedPlanEdgeMechanism(
            edgeResult.Edge.Requirement.CompiledRequirementId,
            edgeResult.Edge.Requirement.FromSemanticNodeId,
            edgeResult.Edge.Requirement.ToSemanticNodeId,
            candidate,
            artifact.CandidateId,
            artifact.ArtifactHash);
    }

    public ContinuousPlanCompositionResult ComposeContinuousPlan(
        CompiledMechanicalRequirementPlan plan,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        ContinuousPlanCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        return _engine.ComposeContinuousPlan(
            plan,
            selectedEdges,
            request,
            _serializer.ComputeCandidateId,
            candidate => _serializer.Write(
                candidate.Candidate,
                ArtifactMetadata.CreateGenerated(candidate)).Artifact.ArtifactHash,
            cancellationToken);
    }

    public BranchedContinuousCompositionResult ComposeBranchedContinuousPlan(
        CompiledMechanicalRequirementPlan plan,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        BranchedContinuousCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        return _engine.ComposeBranchedContinuousPlan(
            plan,
            selectedEdges,
            request,
            _serializer.ComputeCandidateId,
            candidate => _serializer.Write(
                candidate.Candidate,
                ArtifactMetadata.CreateGenerated(candidate)).Artifact.ArtifactHash,
            cancellationToken);
    }

    public GenerationResult ComposeExactRatioMechanism(
        KinematicSynthesisCandidate kinematicCandidate,
        SpatialLayoutCandidate spatialCandidate,
        SpatialLayoutRequest layoutRequest,
        string sourceId)
    {
        return _engine.ComposeExactRatioMechanism(
            kinematicCandidate,
            spatialCandidate,
            layoutRequest,
            sourceId);
    }

    public byte[] WriteSpatialLayoutResult(SpatialLayoutResult result)
    {
        return _spatialLayoutSerializer.Write(result);
    }

    public ValidationBundle Validate(GenerationCandidate candidate)
    {
        return _engine.Validate(candidate);
    }

    public ArtifactValidationResult Validate(MechanismArtifact artifact)
    {
        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        var generated = artifact.Metadata.Generated;
        var composed = artifact.Metadata.Composed;
        var branchedComposed = artifact.Metadata.BranchedComposed;
        var validation = generated is not null
            ? _engine.ValidateGeneratedMechanism(
                artifact.Candidate,
                generated.Provenance.GenerationRequest)
            : composed is not null
                ? _engine.Validate(
                    artifact.Candidate,
                    new GearInvest.Layout.SpatialValidationOptions(
                        composed.Provenance.PitchRadiusTicksPerTooth,
                        composed.Provenance.Request.MaxLayers,
                        composed.Provenance.Request.ClearanceTicks))
                : branchedComposed is not null
                    ? _engine.Validate(
                        artifact.Candidate,
                        new GearInvest.Layout.SpatialValidationOptions(
                            branchedComposed.Provenance.PitchRadiusTicksPerTooth,
                            branchedComposed.Provenance.Request.MaxLayers,
                            branchedComposed.Provenance.Request.ClearanceTicks))
                : _engine.Validate(artifact.Candidate);
        var identity = _serializer.VerifyIdentity(artifact);
        var diagnostics = new List<Diagnostic>(validation.Diagnostics);

        if (generated is not null)
        {
            var provenance = generated.Provenance;
            var canonicalRequest = ExactRatioMechanismGenerationRequestIdentity.BuildCanonicalRepresentation(
                provenance.GenerationRequest);
            var requestId = ExactRatioMechanismGenerationRequestIdentity.ComputeRequestId(canonicalRequest);
            var sourceMatches =
                StringComparer.Ordinal.Equals(provenance.GenerationRequestCanonicalRepresentation, canonicalRequest) &&
                StringComparer.Ordinal.Equals(provenance.GenerationRequestId, requestId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Source.SpecificationId, requestId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Source.GenerationOptions, canonicalRequest);
            var generatorMatches =
                StringComparer.Ordinal.Equals(artifact.Metadata.Generator.BackendId, provenance.GenerationFingerprint.BackendId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Generator.GeneratorVersion, provenance.GenerationFingerprint.SemanticVersion) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Generator.DeterminismProfile, provenance.GenerationFingerprint.DeterminismProfile) &&
                StringComparer.Ordinal.Equals(provenance.GenerationFingerprint.DeterminismProfile, provenance.GenerationRequest.DeterminismProfile) &&
                StringComparer.Ordinal.Equals(provenance.SynthesisFingerprint.DeterminismProfile, provenance.GenerationRequest.SynthesisRequest.DeterminismProfile) &&
                StringComparer.Ordinal.Equals(provenance.LayoutFingerprint.DeterminismProfile, provenance.GenerationRequest.LayoutRequest.DeterminismProfile);
            if (!sourceMatches || !generatorMatches)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.GenerationProvenanceMismatch,
                    DiagnosticSeverity.Error,
                    "Stored generation provenance is not internally consistent with its normalized request or generator fingerprint."));
            }
        }

        if (composed is not null)
        {
            var provenance = composed.Provenance;
            var canonicalRequest = ContinuousPlanCompositionIdentity.BuildRequestCanonicalRepresentation(
                provenance.CompiledPlanId,
                provenance.InputEdges,
                provenance.Request);
            var requestId = ContinuousPlanCompositionIdentity.ComputeRequestId(canonicalRequest);
            var kinematicId = ContinuousPlanCompositionIdentity.ComputeComposedKinematicCandidateId(
                artifact.Candidate.Kinematic,
                provenance.BackendFingerprint);
            var spatialId = ContinuousPlanCompositionIdentity.ComputeComposedSpatialCandidateId(
                kinematicId,
                artifact.Candidate.Spatial,
                provenance.Request.ClearanceTicks,
                provenance.BackendFingerprint);
            var sourceMatches =
                StringComparer.Ordinal.Equals(provenance.RequestCanonicalRepresentation, canonicalRequest) &&
                StringComparer.Ordinal.Equals(provenance.CompositionRequestId, requestId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Source.SpecificationId, requestId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Source.GenerationOptions, canonicalRequest) &&
                StringComparer.Ordinal.Equals(artifact.Candidate.SourceId, requestId);
            var generatorMatches =
                StringComparer.Ordinal.Equals(artifact.Metadata.Generator.BackendId, provenance.BackendFingerprint.BackendId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Generator.GeneratorVersion, provenance.BackendFingerprint.SemanticVersion) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Generator.DeterminismProfile, provenance.BackendFingerprint.DeterminismProfile) &&
                StringComparer.Ordinal.Equals(provenance.Request.DeterminismProfile, provenance.BackendFingerprint.DeterminismProfile);
            var metricsMatch =
                composed.Metrics.DofCount == artifact.Candidate.Kinematic.Dofs.Count &&
                composed.Metrics.AxisCount == artifact.Candidate.Spatial.Axes.Count &&
                composed.Metrics.BodyCount == artifact.Candidate.Spatial.Bodies.Count &&
                composed.Metrics.ContactCount == artifact.Candidate.Spatial.Contacts.Count &&
                composed.Metrics.UsedLayerCount == artifact.Candidate.Spatial.Bodies.Select(body => body.Layer).Distinct().Count();
            if (!sourceMatches || !generatorMatches || !metricsMatch)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CompositionProvenanceMismatch,
                    DiagnosticSeverity.Error,
                    "Stored composition provenance is inconsistent with its request, backend, source, or mechanical counts."));
            }

            if (!StringComparer.Ordinal.Equals(composed.ComposedKinematicCandidateId, kinematicId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CompositionKinematicIdMismatch,
                    DiagnosticSeverity.Error,
                    "Stored composed Kinematic candidate ID does not match the canonical merged graph.",
                    composed.ComposedKinematicCandidateId));
            }

            if (!StringComparer.Ordinal.Equals(composed.ComposedSpatialCandidateId, spatialId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CompositionSpatialIdMismatch,
                    DiagnosticSeverity.Error,
                    "Stored composed Spatial candidate ID does not match the canonical merged embodiment.",
                    composed.ComposedSpatialCandidateId));
            }

            diagnostics.AddRange(ContinuousPlanCompositionValidation.ValidateSemanticBindings(
                composed.SemanticBindings,
                provenance.InputEdges,
                artifact.Candidate.Solution).Diagnostics);
        }

        if (branchedComposed is not null)
        {
            var provenance = branchedComposed.Provenance;
            var canonicalRequest = BranchedContinuousCompositionIdentity.BuildRequestCanonicalRepresentation(
                provenance.CompiledPlanId,
                provenance.InputEdges,
                provenance.Request);
            var requestId = BranchedContinuousCompositionIdentity.ComputeRequestId(canonicalRequest);
            var kinematicId = BranchedContinuousCompositionIdentity.ComputeBranchedComposedKinematicCandidateId(
                artifact.Candidate.Kinematic,
                provenance.BackendFingerprint);
            var spatialId = BranchedContinuousCompositionIdentity.ComputeBranchedComposedSpatialCandidateId(
                kinematicId,
                artifact.Candidate.Spatial,
                provenance.Request.ClearanceTicks,
                provenance.BackendFingerprint);
            var sourceMatches =
                StringComparer.Ordinal.Equals(
                    provenance.TopologyKind,
                    BranchedContinuousCompositionContract.TopologyKind) &&
                StringComparer.Ordinal.Equals(provenance.RequestCanonicalRepresentation, canonicalRequest) &&
                StringComparer.Ordinal.Equals(provenance.BranchedCompositionRequestId, requestId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Source.SpecificationId, requestId) &&
                StringComparer.Ordinal.Equals(artifact.Metadata.Source.GenerationOptions, canonicalRequest) &&
                StringComparer.Ordinal.Equals(artifact.Candidate.SourceId, requestId);
            var generatorMatches =
                StringComparer.Ordinal.Equals(
                    artifact.Metadata.Generator.BackendId,
                    provenance.BackendFingerprint.BackendId) &&
                StringComparer.Ordinal.Equals(
                    artifact.Metadata.Generator.GeneratorVersion,
                    provenance.BackendFingerprint.SemanticVersion) &&
                StringComparer.Ordinal.Equals(
                    artifact.Metadata.Generator.DeterminismProfile,
                    provenance.BackendFingerprint.DeterminismProfile) &&
                StringComparer.Ordinal.Equals(
                    provenance.Request.DeterminismProfile,
                    provenance.BackendFingerprint.DeterminismProfile);
            var mechanicalMetrics = branchedComposed.Metrics.Mechanical;
            var metricsMatch =
                mechanicalMetrics.DofCount == artifact.Candidate.Kinematic.Dofs.Count &&
                mechanicalMetrics.AxisCount == artifact.Candidate.Spatial.Axes.Count &&
                mechanicalMetrics.BodyCount == artifact.Candidate.Spatial.Bodies.Count &&
                mechanicalMetrics.ContactCount == artifact.Candidate.Spatial.Contacts.Count &&
                mechanicalMetrics.UsedLayerCount == artifact.Candidate.Spatial.Bodies
                    .Select(body => body.Layer).Distinct().Count();
            var topologyMatches = provenance.InputEdges.Count == 3 &&
                provenance.InputEdges.Select(edge => edge.CompiledRequirementId)
                    .Distinct(StringComparer.Ordinal).Count() == 3 &&
                provenance.SelectedBranches.Count == 2 &&
                provenance.SelectedBranches.Select(branch => branch.CompiledRequirementId)
                    .Distinct(StringComparer.Ordinal).Count() == 2 &&
                provenance.SelectedBranches.All(branch => provenance.InputEdges.Any(edge =>
                    StringComparer.Ordinal.Equals(
                        edge.CompiledRequirementId,
                        branch.CompiledRequirementId) &&
                    StringComparer.Ordinal.Equals(
                        edge.SemanticToNodeId,
                        branch.SemanticOutputNodeId)));
            if (!sourceMatches || !generatorMatches || !metricsMatch || !topologyMatches)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.BranchedCompositionProvenanceMismatch,
                    DiagnosticSeverity.Error,
                    "Stored branched composition provenance is inconsistent with its request, backend, topology, source, or mechanical counts."));
            }

            if (!StringComparer.Ordinal.Equals(
                    branchedComposed.BranchedComposedKinematicCandidateId,
                    kinematicId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.BranchedCompositionKinematicIdMismatch,
                    DiagnosticSeverity.Error,
                    "Stored branched composed Kinematic candidate ID does not match the canonical merged graph.",
                    branchedComposed.BranchedComposedKinematicCandidateId));
            }

            if (!StringComparer.Ordinal.Equals(
                    branchedComposed.BranchedComposedSpatialCandidateId,
                    spatialId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.BranchedCompositionSpatialIdMismatch,
                    DiagnosticSeverity.Error,
                    "Stored branched composed Spatial candidate ID does not match the canonical merged embodiment.",
                    branchedComposed.BranchedComposedSpatialCandidateId));
            }

            diagnostics.AddRange(BranchedContinuousCompositionValidation.ValidateSemanticBindings(
                branchedComposed.SemanticBindings,
                provenance.InputEdges,
                provenance.Request.AnchorRequirementId,
                artifact.Candidate.Solution).Diagnostics);
        }

        if (!identity.CandidateIdMatches)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CandidateIdMismatch,
                DiagnosticSeverity.Error,
                $"Stored candidateId '{artifact.CandidateId}' does not match '{identity.ComputedCandidateId}'.",
                artifact.CandidateId));
        }

        if (!identity.ArtifactHashMatches)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ArtifactHashMismatch,
                DiagnosticSeverity.Error,
                $"Stored artifactHash '{artifact.ArtifactHash}' does not match '{identity.ComputedArtifactHash}'.",
                artifact.ArtifactHash));
        }

        if (artifact.Candidate.Validation.IsValid != validation.IsValid ||
            !HaveSameDiagnostics(artifact.Candidate.Validation.Diagnostics, validation.Diagnostics))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.StoredValidationMismatch,
                DiagnosticSeverity.Error,
                "Stored validation does not match validation recomputed from canonical Kinematic and Spatial data."));
        }

        return new ArtifactValidationResult(new ValidationBundle(diagnostics), identity);
    }

    public KinematicEvaluation Evaluate(GenerationCandidate candidate, Rational rootTurns)
    {
        return _engine.Evaluate(candidate, rootTurns);
    }

    public KinematicEvaluation Evaluate(MechanismArtifact artifact, Rational rootTurns)
    {
        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        return _engine.Evaluate(artifact.Candidate, rootTurns);
    }

    public ContinuousEventBridgeResult EvaluatePeriodicEvents(
        MechanismArtifact artifact,
        ContinuousEventBridgeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (request is null) throw new ArgumentNullException(nameof(request));
        var trust = Validate(artifact);
        if (!trust.IsValid)
        {
            return _continuousEventBridge.InvalidArtifact(
                artifact.CandidateId,
                artifact.ArtifactHash,
                request,
                trust.Validation.Diagnostics);
        }

        return _continuousEventBridge.Evaluate(
            artifact.CandidateId,
            artifact.ArtifactHash,
            artifact.Candidate.Solution,
            artifact.Candidate.ResolvedPlayback,
            request,
            cancellationToken);
    }

    public HybridStateTransitionResult AdvanceHybridState(
        HybridStateSnapshot initialSnapshot,
        IndexedStateTransitionPlan transitionPlan,
        ContinuousEventBridgeResult eventResult,
        HybridStateAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return _hybridStateTransitionEngine.Apply(
            initialSnapshot,
            transitionPlan,
            eventResult,
            request,
            cancellationToken);
    }

    public GuardedHybridStateTransitionResult AdvanceGuardedHybridState(
        HybridStateSnapshot initialSnapshot,
        GuardedIndexedStateTransitionPlan transitionPlan,
        ContinuousEventBridgeResult eventResult,
        GuardedHybridStateAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return _guardedHybridStateTransitionEngine.Apply(
            initialSnapshot,
            transitionPlan,
            eventResult,
            request,
            cancellationToken);
    }

    public CompositeGuardedHybridStateTransitionResult AdvanceGuardedHybridState(
        HybridStateSnapshot initialSnapshot,
        CompositeGuardedIndexedStateTransitionPlan transitionPlan,
        ContinuousEventBridgeResult eventResult,
        GuardedHybridStateAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return _compositeGuardedHybridStateTransitionEngine.Apply(
            initialSnapshot,
            transitionPlan,
            eventResult,
            request,
            cancellationToken);
    }

    public ComputedGuardedHybridStateTransitionResult AdvanceGuardedHybridState(
        HybridStateSnapshot initialSnapshot,
        ComputedGuardedIndexedStateTransitionPlan transitionPlan,
        ContinuousEventBridgeResult eventResult,
        GuardedHybridStateAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return _computedGuardedHybridStateTransitionEngine.Apply(
            initialSnapshot,
            transitionPlan,
            eventResult,
            request,
            cancellationToken);
    }

    public ArtifactWriteResult WriteArtifact(GenerationCandidate candidate, ArtifactMetadata? metadata = null)
    {
        return _serializer.Write(candidate, metadata);
    }

    public ArtifactWriteResult WriteArtifact(ExactRatioMechanismCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return _serializer.Write(candidate.Candidate, ArtifactMetadata.CreateGenerated(candidate));
    }

    public ArtifactWriteResult WriteArtifact(ContinuousPlanCompositionCandidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        return _serializer.Write(candidate.Candidate, ArtifactMetadata.CreateComposed(candidate));
    }

    public ArtifactWriteResult WriteArtifact(BranchedContinuousCompositionCandidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        return _serializer.Write(candidate.Candidate, ArtifactMetadata.CreateBranchedComposed(candidate));
    }

    public byte[] WriteExactRatioMechanismGenerationResult(
        ExactRatioMechanismGenerationResult result,
        IEnumerable<GeneratedMechanismArtifactRecord> artifactRecords)
    {
        return _generationSerializer.Write(result, artifactRecords);
    }

    public byte[] WriteSemanticCompilationResult(SemanticCompilationResult result)
    {
        return _semanticCompilationSerializer.Write(result);
    }

    public byte[] WriteCompiledPlanGenerationResult(
        CompiledPlanGenerationResult result,
        IEnumerable<GeneratedPlanEdgeArtifactRecord> artifactRecords)
    {
        return _compiledPlanGenerationSerializer.Write(result, artifactRecords);
    }

    public byte[] WriteContinuousPlanCompositionResult(
        ContinuousPlanCompositionResult result,
        IEnumerable<ComposedMechanismArtifactRecord> artifactRecords)
    {
        return _continuousPlanCompositionSerializer.Write(result, artifactRecords);
    }

    public byte[] WriteBranchedContinuousCompositionResult(
        BranchedContinuousCompositionResult result,
        IEnumerable<BranchedComposedMechanismArtifactRecord> artifactRecords)
    {
        return _branchedContinuousCompositionSerializer.Write(result, artifactRecords);
    }

    public byte[] WriteContinuousEventBridgeResult(ContinuousEventBridgeResult result)
    {
        return _continuousEventBridgeSerializer.Write(result);
    }

    public byte[] WriteHybridStateTransitionResult(HybridStateTransitionResult result)
    {
        return _hybridStateTransitionSerializer.Write(result);
    }

    public byte[] WriteGuardedHybridStateTransitionResult(GuardedHybridStateTransitionResult result)
    {
        return _guardedHybridStateTransitionSerializer.Write(result);
    }

    public byte[] WriteGuardedHybridStateTransitionResult(CompositeGuardedHybridStateTransitionResult result)
    {
        return _guardedHybridStateTransitionSerializer.Write(result);
    }

    public byte[] WriteGuardedHybridStateTransitionResult(ComputedGuardedHybridStateTransitionResult result)
    {
        return _guardedHybridStateTransitionSerializer.Write(result);
    }

    public HybridStateSnapshotDocument CreateHybridStateSnapshotDocument(
        HybridStateSnapshot snapshot,
        GuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind authorityKind,
        string determinismProfile,
        string? sourceResultFormat = null,
        string? sourceResultRequestId = null)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (compatiblePlan is null) throw new ArgumentNullException(nameof(compatiblePlan));
        if ((sourceResultFormat is null) != (sourceResultRequestId is null))
        {
            throw new ArgumentException("Snapshot provenance fields must either both be present or both be absent.");
        }

        var provenance = sourceResultFormat is null
            ? null
            : new HybridStateSnapshotProvenance(sourceResultFormat, sourceResultRequestId!);
        return new HybridStateSnapshotDocument(
            new HybridStateSnapshotAuthority(
                authorityKind,
                compatiblePlan.GuardedIndexedTransitionPlanId,
                determinismProfile),
            snapshot,
            provenance);
    }

    public HybridStateSnapshotDocument CreateHybridStateSnapshotDocument(
        HybridStateSnapshot snapshot,
        CompositeGuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind authorityKind,
        string determinismProfile,
        string? sourceResultFormat = null,
        string? sourceResultRequestId = null)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (compatiblePlan is null) throw new ArgumentNullException(nameof(compatiblePlan));
        if ((sourceResultFormat is null) != (sourceResultRequestId is null))
        {
            throw new ArgumentException("Snapshot provenance fields must either both be present or both be absent.");
        }

        var provenance = sourceResultFormat is null
            ? null
            : new HybridStateSnapshotProvenance(sourceResultFormat, sourceResultRequestId!);
        return new HybridStateSnapshotDocument(
            new HybridStateSnapshotAuthority(
                authorityKind,
                compatiblePlan.GuardedIndexedTransitionPlanId,
                determinismProfile),
            snapshot,
            provenance);
    }

    public HybridStateSnapshotDocument CreateHybridStateSnapshotDocument(
        HybridStateSnapshot snapshot,
        ComputedGuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind authorityKind,
        string determinismProfile,
        string? sourceResultFormat = null,
        string? sourceResultRequestId = null)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (compatiblePlan is null) throw new ArgumentNullException(nameof(compatiblePlan));
        if ((sourceResultFormat is null) != (sourceResultRequestId is null))
        {
            throw new ArgumentException("Snapshot provenance fields must either both be present or both be absent.");
        }

        var provenance = sourceResultFormat is null
            ? null
            : new HybridStateSnapshotProvenance(sourceResultFormat, sourceResultRequestId!);
        return new HybridStateSnapshotDocument(
            new HybridStateSnapshotAuthority(
                authorityKind,
                compatiblePlan.GuardedIndexedTransitionPlanId,
                determinismProfile),
            snapshot,
            provenance);
    }

    public byte[] WriteHybridStateSnapshot(HybridStateSnapshotDocument document)
    {
        return _hybridStateSnapshotSerializer.Write(document);
    }

    public HybridStateSnapshotReadResult ReadHybridStateSnapshot(byte[] utf8Json)
    {
        if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
        try
        {
            return new HybridStateSnapshotReadResult(
                HybridStateSnapshotReadStatus.Success,
                _hybridStateSnapshotSerializer.Read(utf8Json),
                Array.Empty<Diagnostic>());
        }
        catch (HybridStateSnapshotFormatException exception)
        {
            var status = exception.UnsupportedVersion
                ? HybridStateSnapshotReadStatus.Unsupported
                : HybridStateSnapshotReadStatus.InvalidInput;
            var code = exception.UnsupportedVersion
                ? DiagnosticCodes.StateSnapshotUnsupportedVersion
                : DiagnosticCodes.StateSnapshotFormatInvalid;
            return new HybridStateSnapshotReadResult(
                status,
                null,
                new[] { new Diagnostic(code, DiagnosticSeverity.Error, exception.Message) });
        }
    }

    public HybridStateSnapshotIdentityVerification VerifyHybridStateSnapshotIdentity(
        HybridStateSnapshotDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return new HybridStateSnapshotIdentityVerification(
            document.StoredHybridStateSnapshotId,
            document.Snapshot.HybridStateSnapshotId);
    }

    public HybridStateSnapshotValidationResult ValidateHybridStateSnapshot(
        HybridStateSnapshotDocument document,
        MechanismArtifact expectedMechanism,
        string expectedDriverId,
        GuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind = HybridStateSnapshotAuthorityKind.Final,
        string? expectedDeterminismProfile = null)
    {
        if (expectedMechanism is null) throw new ArgumentNullException(nameof(expectedMechanism));
        return _hybridStateSnapshotValidator.Validate(
            document,
            expectedMechanism.CandidateId,
            expectedDriverId,
            compatiblePlan,
            requiredAuthorityKind,
            expectedDeterminismProfile ?? compatiblePlan.DeterminismProfile);
    }

    public HybridStateSnapshotValidationResult ValidateHybridStateSnapshot(
        HybridStateSnapshotDocument document,
        MechanismArtifact expectedMechanism,
        string expectedDriverId,
        ComputedGuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind = HybridStateSnapshotAuthorityKind.Final,
        string? expectedDeterminismProfile = null)
    {
        if (expectedMechanism is null) throw new ArgumentNullException(nameof(expectedMechanism));
        return _hybridStateSnapshotValidator.Validate(
            document,
            expectedMechanism.CandidateId,
            expectedDriverId,
            compatiblePlan,
            requiredAuthorityKind,
            expectedDeterminismProfile ?? compatiblePlan.DeterminismProfile);
    }

    public HybridStateSnapshotValidationResult ValidateHybridStateSnapshot(
        HybridStateSnapshotDocument document,
        MechanismArtifact expectedMechanism,
        string expectedDriverId,
        CompositeGuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind = HybridStateSnapshotAuthorityKind.Final,
        string? expectedDeterminismProfile = null)
    {
        if (expectedMechanism is null) throw new ArgumentNullException(nameof(expectedMechanism));
        return _hybridStateSnapshotValidator.Validate(
            document,
            expectedMechanism.CandidateId,
            expectedDriverId,
            compatiblePlan,
            requiredAuthorityKind,
            expectedDeterminismProfile ?? compatiblePlan.DeterminismProfile);
    }

    public ArtifactWriteResult WriteArtifact(MechanismArtifact artifact)
    {
        return _serializer.Write(artifact);
    }

    public MechanismArtifact ReadArtifact(byte[] utf8Json)
    {
        return _serializer.Read(utf8Json);
    }

    public ArtifactIdentityVerification VerifyIdentity(MechanismArtifact artifact)
    {
        return _serializer.VerifyIdentity(artifact);
    }

    private static bool HaveSameDiagnostics(
        IReadOnlyCollection<Diagnostic> left,
        IReadOnlyCollection<Diagnostic> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right, (a, b) =>
                StringComparer.Ordinal.Equals(a.Code, b.Code) &&
                a.Severity == b.Severity &&
                StringComparer.Ordinal.Equals(a.Message, b.Message) &&
                StringComparer.Ordinal.Equals(a.SubjectId, b.SubjectId))
            .All(equal => equal);
    }
}
