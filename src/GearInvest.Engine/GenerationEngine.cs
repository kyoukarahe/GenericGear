using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class GenerationEngine
{
    private readonly ExactRatioSynthesizer _exactRatioSynthesizer = new();
    private readonly CardinalSpatialLayoutEngine _spatialLayoutEngine = new();
    private readonly ExactRatioMechanismGenerator _exactRatioMechanismGenerator;

    public GenerationEngine()
    {
        _exactRatioMechanismGenerator = new ExactRatioMechanismGenerator(
            _exactRatioSynthesizer,
            _spatialLayoutEngine);
    }

    public GenerationResult Generate(LowLevelMechanicalSpecification specification)
    {
        if (specification is null)
        {
            throw new ArgumentNullException(nameof(specification));
        }

        var kinematic = KinematicSolver.Solve(specification.Kinematic);
        var diagnostics = new List<Diagnostic>(kinematic.Diagnostics);
        if (!kinematic.IsValid || kinematic.Solution is null)
        {
            return new GenerationResult(Array.Empty<GenerationCandidate>(), diagnostics);
        }

        var spatial = SpatialValidator.Validate(specification.Spatial, specification.Kinematic);
        diagnostics.AddRange(spatial.Diagnostics);

        var playback = ResolvedPlaybackBuilder.Build(kinematic.Solution, specification.Spatial);
        var playbackValidation = ResolvedPlaybackValidator.Validate(
            playback,
            kinematic.Solution,
            specification.Spatial);
        diagnostics.AddRange(playbackValidation.Diagnostics);

        var validation = new ValidationBundle(diagnostics);
        if (!validation.IsValid)
        {
            return new GenerationResult(Array.Empty<GenerationCandidate>(), validation.Diagnostics);
        }

        var candidate = new GenerationCandidate(
            specification.SourceId,
            specification.Kinematic,
            kinematic.Solution,
            specification.Spatial,
            playback,
            validation);
        return new GenerationResult(new[] { candidate }, diagnostics);
    }

    public ValidationBundle Validate(GenerationCandidate candidate)
    {
        return ValidateCandidate(candidate, null);
    }

    public ValidationBundle Validate(
        GenerationCandidate candidate,
        SpatialValidationOptions spatialValidationOptions)
    {
        if (spatialValidationOptions is null)
        {
            throw new ArgumentNullException(nameof(spatialValidationOptions));
        }

        return ValidateCandidate(candidate, spatialValidationOptions);
    }

    public ValidationBundle ValidateGeneratedMechanism(
        GenerationCandidate candidate,
        ExactRatioMechanismGenerationRequest request)
    {
        return ValidateGeneratedMechanismCandidate(
            candidate,
            request ?? throw new ArgumentNullException(nameof(request)));
    }

    internal static ValidationBundle ValidateCandidate(
        GenerationCandidate candidate,
        SpatialValidationOptions? spatialValidationOptions)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        var diagnostics = new List<Diagnostic>();
        var solved = KinematicSolver.Solve(candidate.Kinematic);
        diagnostics.AddRange(solved.Diagnostics);

        if (solved.Solution is not null)
        {
            CompareStoredSolution(candidate.Solution, solved.Solution, diagnostics);
            diagnostics.AddRange(SpatialValidator.ValidateDetailed(
                candidate.Spatial,
                candidate.Kinematic,
                spatialValidationOptions).Diagnostics);
            diagnostics.AddRange(ResolvedPlaybackValidator.Validate(
                candidate.ResolvedPlayback,
                solved.Solution,
                candidate.Spatial).Diagnostics);
        }

        return new ValidationBundle(diagnostics);
    }

    internal static ValidationBundle ValidateGeneratedMechanismCandidate(
        GenerationCandidate candidate,
        ExactRatioMechanismGenerationRequest request)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var layout = request.LayoutRequest;
        var diagnostics = new List<Diagnostic>(ValidateCandidate(
            candidate,
            new SpatialValidationOptions(
                layout.PitchRadiusTicksPerTooth,
                layout.MaxLayers,
                layout.ClearanceTicks)).Diagnostics);
        var synthesis = request.SynthesisRequest;
        var couplings = candidate.Kinematic.Couplings.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
        var outputDofId = couplings.Count == 0 ? string.Empty : couplings[couplings.Count - 1].DrivenDofId;
        var outputState = candidate.Solution.States.SingleOrDefault(
            state => StringComparer.Ordinal.Equals(state.DofId, outputDofId));
        if (outputState is null ||
            outputState.Coefficient != synthesis.TargetTransfer ||
            outputState.PhaseOffset != Rational.Zero)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationTargetMismatch,
                DiagnosticSeverity.Error,
                "Final candidate output does not match the requested signed exact transfer and zero phase.",
                outputDofId));
        }

        var toothBoundsMatch = couplings.All(coupling =>
            coupling.DriverTeeth >= synthesis.MinTeeth && coupling.DriverTeeth <= synthesis.MaxTeeth &&
            coupling.DrivenTeeth >= synthesis.MinTeeth && coupling.DrivenTeeth <= synthesis.MaxTeeth);
        var hasCompoundIntermediate = couplings.Zip(
            couplings.Skip(1),
            (incoming, outgoing) => incoming.DrivenTeeth != outgoing.DriverTeeth).Any(value => value);
        if (couplings.Count < synthesis.MinStages || couplings.Count > synthesis.MaxStages ||
            !toothBoundsMatch || (!synthesis.AllowCompound && hasCompoundIntermediate))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationRequestBoundsMismatch,
                DiagnosticSeverity.Error,
                "Final candidate topology or tooth counts are outside the generated request bounds.",
                "synthesisRequest"));
        }

        var rootBody = candidate.Spatial.Bodies.FirstOrDefault(body =>
            StringComparer.Ordinal.Equals(body.DofId, candidate.Kinematic.RootDofId));
        var rootAxis = rootBody is null
            ? null
            : candidate.Spatial.Axes.FirstOrDefault(axis => StringComparer.Ordinal.Equals(axis.Id, rootBody.AxisId));
        if (rootAxis is null || rootAxis.X != layout.RootAxisX || rootAxis.Y != layout.RootAxisY)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationRequestBoundsMismatch,
                DiagnosticSeverity.Error,
                "Final candidate root axis does not match the generated layout request.",
                candidate.Kinematic.RootDofId));
        }

        diagnostics.AddRange(CanonicalMechanicalObjectIdRemapper.ValidateCanonicalIds(
            candidate.Kinematic,
            candidate.Spatial));
        return new ValidationBundle(diagnostics);
    }

    public KinematicEvaluation Evaluate(GenerationCandidate candidate, Rational rootTurns)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return KinematicEvaluator.Evaluate(candidate.Solution, rootTurns);
    }

    public KinematicSynthesisResult SynthesizeExactRatio(
        ExactRatioSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        return _exactRatioSynthesizer.Synthesize(request, cancellationToken);
    }

    public SpatialLayoutResult CreateLayouts(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        return _spatialLayoutEngine.CreateLayouts(sourceCandidate, request, cancellationToken);
    }

    public ExactRatioMechanismGenerationResult GenerateExactRatioMechanisms(
        ExactRatioMechanismGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return _exactRatioMechanismGenerator.Generate(request, cancellationToken);
    }

    public ContinuousPlanCompositionResult ComposeContinuousPlan(
        CompiledMechanicalRequirementPlan plan,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        ContinuousPlanCompositionRequest request,
        Func<GenerationCandidate, string> candidateIdFactory,
        Func<ExactRatioMechanismCandidate, string> artifactHashFactory,
        CancellationToken cancellationToken = default)
    {
        return ContinuousPlanComposer.Compose(
            plan,
            selectedEdges,
            request,
            candidateIdFactory,
            artifactHashFactory,
            cancellationToken);
    }

    public BranchedContinuousCompositionResult ComposeBranchedContinuousPlan(
        CompiledMechanicalRequirementPlan plan,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        BranchedContinuousCompositionRequest request,
        Func<GenerationCandidate, string> candidateIdFactory,
        Func<ExactRatioMechanismCandidate, string> artifactHashFactory,
        CancellationToken cancellationToken = default)
    {
        return BranchedContinuousPlanComposer.Compose(
            plan,
            selectedEdges,
            request,
            candidateIdFactory,
            artifactHashFactory,
            cancellationToken);
    }

    public CompiledGenerationPlan LowerCompiledPlan(
        CompiledMechanicalRequirementPlan plan,
        ExactRatioPlanGenerationPolicy policy)
    {
        return CompiledPlanLowering.Lower(plan, policy);
    }

    public CompiledPlanGenerationResult GenerateCompiledPlan(
        SemanticCompilationResult compilation,
        ExactRatioPlanGenerationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (compilation is null) throw new ArgumentNullException(nameof(compilation));
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        var policyId = ExactRatioPlanGenerationPolicyIdentity.ComputePolicyId(policy);
        if (!compilation.IsSuccess || compilation.CompiledPlan is null)
        {
            var diagnostics = new List<Diagnostic>(compilation.Diagnostics)
            {
                new Diagnostic(
                    DiagnosticCodes.GenerationInvalidCompiledPlan,
                    DiagnosticSeverity.Error,
                    "A complete semantic compilation result is required before plan generation."),
            };
            return new CompiledPlanGenerationResult(
                compilation.SourceKind,
                compilation.SourceCatalogId,
                compilation.SemanticSpecificationId,
                compilation.CompilerFingerprint,
                string.Empty,
                policyId,
                policy,
                compilation.Status == SemanticCompilationStatus.Cancelled
                    ? CompiledPlanGenerationStatus.Cancelled
                    : CompiledPlanGenerationStatus.InvalidInput,
                Array.Empty<CompiledPlanGenerationEdgeResult>(),
                Array.Empty<DerivedContinuousTransferRelation>(),
                diagnostics);
        }

        var lowered = CompiledPlanLowering.Lower(compilation.CompiledPlan, policy);
        var edgeResults = new List<CompiledPlanGenerationEdgeResult>();
        var allDiagnostics = new List<Diagnostic>(compilation.Diagnostics);
        if (lowered.OrderedEdges.Count == 0)
        {
            allDiagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationInvalidCompiledPlan,
                DiagnosticSeverity.Error,
                "A compiled generation plan must contain at least one continuous transfer edge."));
        }

        foreach (var edge in lowered.OrderedEdges)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var generated = GenerateExactRatioMechanisms(edge.GenerationRequest, cancellationToken);
            edgeResults.Add(new CompiledPlanGenerationEdgeResult(edge, generated));
            allDiagnostics.AddRange(generated.Diagnostics);
        }

        var status = AggregateStatus(lowered.OrderedEdges.Count, edgeResults, cancellationToken.IsCancellationRequested);
        return new CompiledPlanGenerationResult(
            compilation.SourceKind,
            compilation.SourceCatalogId,
            compilation.SemanticSpecificationId,
            compilation.CompilerFingerprint,
            compilation.CompiledPlan.CompiledPlanId,
            lowered.GenerationPolicyId,
            policy,
            status,
            edgeResults,
            compilation.CompiledPlan.DerivedRelations,
            allDiagnostics);
    }

    private static CompiledPlanGenerationStatus AggregateStatus(
        int requiredEdgeCount,
        IReadOnlyCollection<CompiledPlanGenerationEdgeResult> results,
        bool cancellationRequested)
    {
        if (requiredEdgeCount == 0)
        {
            return CompiledPlanGenerationStatus.InvalidInput;
        }

        if (cancellationRequested || results.Any(item =>
                item.GenerationResult.Status == ExactRatioMechanismGenerationStatus.Cancelled))
        {
            return CompiledPlanGenerationStatus.Cancelled;
        }

        if (results.Count != requiredEdgeCount || results.Any(item =>
                item.GenerationResult.Status == ExactRatioMechanismGenerationStatus.InvalidInput))
        {
            return CompiledPlanGenerationStatus.InvalidInput;
        }

        if (results.Any(item => item.GenerationResult.Status == ExactRatioMechanismGenerationStatus.IncompleteBudget))
        {
            return CompiledPlanGenerationStatus.IncompleteBudget;
        }

        if (results.Any(item => item.GenerationResult.Status == ExactRatioMechanismGenerationStatus.Infeasible))
        {
            return CompiledPlanGenerationStatus.Infeasible;
        }

        return results.All(item => item.GenerationResult.IsSuccess)
            ? CompiledPlanGenerationStatus.Complete
            : CompiledPlanGenerationStatus.InvalidInput;
    }

    public GenerationResult ComposeExactRatioMechanism(
        KinematicSynthesisCandidate kinematicCandidate,
        SpatialLayoutCandidate spatialCandidate,
        SpatialLayoutRequest layoutRequest,
        string sourceId)
    {
        if (kinematicCandidate is null)
        {
            throw new ArgumentNullException(nameof(kinematicCandidate));
        }

        var minimumTeeth = kinematicCandidate.OrderedStages.Min(stage => Math.Min(stage.DriverTeeth, stage.DrivenTeeth));
        var maximumTeeth = kinematicCandidate.OrderedStages.Max(stage => Math.Max(stage.DriverTeeth, stage.DrivenTeeth));
        return ExactRatioMechanismGenerator.ComposeExactRatioMechanism(
            kinematicCandidate,
            spatialCandidate,
            layoutRequest,
            sourceId,
            new ExactRatioSynthesisRequest(
                kinematicCandidate.ExactTransfer,
                minimumTeeth,
                maximumTeeth,
                kinematicCandidate.OrderedStages.Count,
                kinematicCandidate.OrderedStages.Count,
                true,
                1,
                1));
    }

    private static void CompareStoredSolution(
        KinematicSolution stored,
        KinematicSolution expected,
        ICollection<Diagnostic> diagnostics)
    {
        var storedByDof = stored.States.ToDictionary(state => state.DofId, StringComparer.Ordinal);
        foreach (var expectedState in expected.States)
        {
            if (!storedByDof.TryGetValue(expectedState.DofId, out var storedState) ||
                storedState.Coefficient != expectedState.Coefficient ||
                storedState.PhaseOffset != expectedState.PhaseOffset)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.StoredSolutionMismatch,
                    DiagnosticSeverity.Error,
                    $"Stored solution for DOF '{expectedState.DofId}' does not match exact propagation.",
                    expectedState.DofId));
            }
        }

        foreach (var extra in storedByDof.Keys.Except(expected.States.Select(state => state.DofId), StringComparer.Ordinal))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.StoredSolutionMismatch,
                DiagnosticSeverity.Error,
                $"Stored solution contains unknown DOF '{extra}'.",
                extra));
        }
    }
}
