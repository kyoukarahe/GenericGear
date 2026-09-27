using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

internal static class ContinuousPlanComposer
{
    private static readonly CardinalRotation[] RotationOrder =
    {
        CardinalRotation.Degrees0,
        CardinalRotation.Degrees90,
        CardinalRotation.Degrees180,
        CardinalRotation.Degrees270,
    };

    public static ContinuousPlanCompositionResult Compose(
        CompiledMechanicalRequirementPlan plan,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        ContinuousPlanCompositionRequest request,
        Func<GenerationCandidate, string> candidateIdFactory,
        Func<ExactRatioMechanismCandidate, string> artifactHashFactory,
        CancellationToken cancellationToken)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (selectedEdges is null) throw new ArgumentNullException(nameof(selectedEdges));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (candidateIdFactory is null) throw new ArgumentNullException(nameof(candidateIdFactory));
        if (artifactHashFactory is null) throw new ArgumentNullException(nameof(artifactHashFactory));

        var fingerprint = new ContinuousPlanCompositionFingerprint(
            ContinuousPlanCompositionContract.BackendId,
            ContinuousPlanCompositionContract.BackendVersion,
            request.DeterminismProfile);
        var diagnostics = ValidateRequest(request);
        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            return Terminal(plan, request, fingerprint, Array.Empty<CompositionInputEdgeProvenance>(),
                ContinuousPlanCompositionStatus.InvalidInput, diagnostics);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionCancelled,
                DiagnosticSeverity.Warning,
                "Continuous plan composition was cancelled before deterministic work began."));
            return Terminal(plan, request, fingerprint, Array.Empty<CompositionInputEdgeProvenance>(),
                ContinuousPlanCompositionStatus.Cancelled, diagnostics);
        }

        if (!TryBuildPlanChain(plan, diagnostics, out var chain, out var unsupported))
        {
            return Terminal(plan, request, fingerprint, Array.Empty<CompositionInputEdgeProvenance>(),
                unsupported ? ContinuousPlanCompositionStatus.Unsupported : ContinuousPlanCompositionStatus.InvalidInput,
                diagnostics);
        }

        if (!TryOrderSelections(chain!, selectedEdges, candidateIdFactory, artifactHashFactory,
                diagnostics, out var edgeContexts, out var inputEdges, out var pitchScale))
        {
            return Terminal(plan, request, fingerprint, inputEdges,
                ContinuousPlanCompositionStatus.InvalidInput, diagnostics);
        }

        var requestCanonical = ContinuousPlanCompositionIdentity.BuildRequestCanonicalRepresentation(
            plan.CompiledPlanId, inputEdges, request);
        var requestId = ContinuousPlanCompositionIdentity.ComputeRequestId(requestCanonical);

        var kinematicWatch = Stopwatch.StartNew();
        var kinematic = BuildKinematic(edgeContexts!);
        var solved = KinematicSolver.Solve(kinematic);
        var bindings = BuildSemanticBindings(chain!);
        var kinematicValid = ValidateComposedKinematics(chain!, solved, bindings, diagnostics);
        var composedKinematicId = ContinuousPlanCompositionIdentity.ComputeComposedKinematicCandidateId(
            kinematic, fingerprint);
        kinematicWatch.Stop();
        if (!kinematicValid || solved.Solution is null)
        {
            return new ContinuousPlanCompositionResult(
                plan.CompiledPlanId,
                request,
                requestCanonical,
                requestId,
                fingerprint,
                ContinuousPlanCompositionStatus.InvalidInput,
                inputEdges,
                Array.Empty<ContinuousPlanCompositionCandidate>(),
                diagnostics,
                EmptySummary(searchComplete: false),
                new ContinuousPlanCompositionPerformance(ToMicroseconds(kinematicWatch.ElapsedTicks), 0, 0));
        }

        var anchor = edgeContexts![0];
        var moving = edgeContexts[1];
        var layerMappings = EnumerateLayerMappings(anchor, moving, request.MaxLayers);
        var draftsBySpatialId = new Dictionary<string, CandidateDraft>(StringComparer.Ordinal);
        var rawFeasible = 0L;
        var expansions = 0L;
        var rotationsVisited = 0L;
        var budgetExhausted = false;
        var cancelled = false;
        var spatialTicks = 0L;
        var validationTicks = 0L;

        foreach (var rotation in RotationOrder)
        {
            if (layerMappings.Count == 0)
            {
                break;
            }

            rotationsVisited++;
            foreach (var layerMapping in layerMappings)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                if (expansions >= request.MaxCompositionExpansions)
                {
                    budgetExhausted = true;
                    break;
                }

                expansions++;
                var spatialWatch = Stopwatch.StartNew();
                var built = BuildSpatial(anchor, moving, rotation, layerMapping);
                var playback = ResolvedPlaybackBuilder.Build(solved.Solution, built.Spatial);
                spatialWatch.Stop();
                spatialTicks += spatialWatch.ElapsedTicks;

                var validationWatch = Stopwatch.StartNew();
                var spatialValidation = SpatialValidator.ValidateDetailed(
                    built.Spatial,
                    kinematic,
                    new SpatialValidationOptions(pitchScale, request.MaxLayers, request.ClearanceTicks));
                var playbackValidation = ResolvedPlaybackValidator.Validate(playback, solved.Solution, built.Spatial);
                var validation = new ValidationBundle(
                    solved.Diagnostics.Concat(spatialValidation.Diagnostics).Concat(playbackValidation.Diagnostics));
                validationWatch.Stop();
                validationTicks += validationWatch.ElapsedTicks;
                if (!validation.IsValid)
                {
                    continue;
                }

                rawFeasible++;
                var mechanical = new GenerationCandidate(
                    requestId,
                    kinematic,
                    solved.Solution,
                    built.Spatial,
                    playback,
                    validation);
                var composedSpatialId = ContinuousPlanCompositionIdentity.ComputeComposedSpatialCandidateId(
                    composedKinematicId,
                    built.Spatial,
                    request.ClearanceTicks,
                    fingerprint);
                if (draftsBySpatialId.ContainsKey(composedSpatialId))
                {
                    continue;
                }

                var metrics = BuildMetrics(
                    kinematic,
                    built.Spatial,
                    spatialValidation.UnrelatedSameLayerPairChecks);
                draftsBySpatialId.Add(
                    composedSpatialId,
                    new CandidateDraft(
                        mechanical,
                        composedSpatialId,
                        candidateIdFactory(mechanical),
                        rotation,
                        built.NormalizedLayerMapping,
                        metrics));
            }

            if (budgetExhausted || cancelled)
            {
                break;
            }
        }

        var orderedDrafts = draftsBySpatialId.Values.OrderBy(item => item, CandidateDraftComparer.Instance).ToList();
        var returnedDrafts = orderedDrafts.Take(request.MaxReturnedCompositions).ToList();
        var truncated = orderedDrafts.Count > returnedDrafts.Count;
        var searchComplete = !budgetExhausted && !cancelled;
        var status = cancelled
            ? ContinuousPlanCompositionStatus.Cancelled
            : budgetExhausted
                ? ContinuousPlanCompositionStatus.IncompleteBudget
                : returnedDrafts.Count == 0
                    ? ContinuousPlanCompositionStatus.Infeasible
                    : ContinuousPlanCompositionStatus.Complete;
        if (cancelled)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionCancelled,
                DiagnosticSeverity.Warning,
                "Continuous plan composition was cancelled between deterministic expansions."));
        }

        if (budgetExhausted)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionBudgetExhausted,
                DiagnosticSeverity.Warning,
                "The deterministic composition expansion budget was exhausted.",
                "maxCompositionExpansions"));
        }

        if (status == ContinuousPlanCompositionStatus.Infeasible)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionInfeasible,
                DiagnosticSeverity.Info,
                "No valid shared-node composition exists within the declared layer and collision constraints."));
        }

        if (truncated)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionResultTruncated,
                DiagnosticSeverity.Info,
                "Composed candidates were truncated by maxReturnedCompositions.",
                "maxReturnedCompositions"));
        }

        var summary = new ContinuousPlanCompositionSearchSummary(
            rotationsVisited,
            layerMappings.Count,
            expansions,
            rawFeasible,
            orderedDrafts.Count,
            returnedDrafts.Count,
            truncated,
            searchComplete);
        var candidates = returnedDrafts.Select(draft => new ContinuousPlanCompositionCandidate(
            draft.Mechanical,
            composedKinematicId,
            draft.ComposedSpatialId,
            draft.CandidateId,
            bindings,
            new ContinuousPlanCompositionProvenance(
                plan.CompiledPlanId,
                request,
                requestCanonical,
                requestId,
                fingerprint,
                pitchScale,
                inputEdges,
                draft.Rotation,
                draft.LayerMapping,
                summary),
            draft.Metrics));
        return new ContinuousPlanCompositionResult(
            plan.CompiledPlanId,
            request,
            requestCanonical,
            requestId,
            fingerprint,
            status,
            inputEdges,
            candidates,
            diagnostics,
            summary,
            new ContinuousPlanCompositionPerformance(
                ToMicroseconds(kinematicWatch.ElapsedTicks),
                ToMicroseconds(spatialTicks),
                ToMicroseconds(validationTicks)));
    }

    private static List<Diagnostic> ValidateRequest(ContinuousPlanCompositionRequest request)
    {
        var diagnostics = new List<Diagnostic>();
        if (request.MaxLayers <= 0 || request.ClearanceTicks.Sign < 0 ||
            request.MaxCompositionExpansions <= 0 || request.MaxReturnedCompositions <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionInvalidRequest,
                DiagnosticSeverity.Error,
                "Composition limits must be positive and clearanceTicks must be non-negative.",
                "compositionRequest"));
        }

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                ContinuousPlanCompositionContract.DefaultDeterminismProfile))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionUnsupportedProfile,
                DiagnosticSeverity.Error,
                "The built-in composer supports only determinism profile '" +
                ContinuousPlanCompositionContract.DefaultDeterminismProfile + "'.",
                "determinismProfile"));
        }

        return diagnostics;
    }

    private static bool TryBuildPlanChain(
        CompiledMechanicalRequirementPlan plan,
        ICollection<Diagnostic> diagnostics,
        out PlanChain? chain,
        out bool unsupported)
    {
        chain = null;
        unsupported = false;
        var nodes = plan.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(plan.CompiledPlanId) ||
            nodes.Count != plan.Nodes.Count ||
            plan.OrderedRequirements.Select(item => item.CompiledRequirementId).Distinct(StringComparer.Ordinal).Count() !=
            plan.OrderedRequirements.Count ||
            plan.OrderedRequirements.Any(requirement =>
                !nodes.ContainsKey(requirement.FromSemanticNodeId) ||
                !nodes.ContainsKey(requirement.ToSemanticNodeId)))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionInvalidPlan,
                DiagnosticSeverity.Error,
                "The compiled plan has duplicate IDs or unknown endpoint references.",
                plan.CompiledPlanId));
            return false;
        }

        if (plan.Nodes.Count != 3 || plan.OrderedRequirements.Count != 2)
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionUnsupportedTopology,
                DiagnosticSeverity.Warning,
                "The built-in composer supports exactly three nodes and two ordered continuous requirements.",
                plan.CompiledPlanId));
            return false;
        }

        var first = plan.OrderedRequirements[0];
        var second = plan.OrderedRequirements[1];
        var nodeSet = new HashSet<string>(new[]
        {
            first.FromSemanticNodeId,
            first.ToSemanticNodeId,
            second.ToSemanticNodeId,
        }, StringComparer.Ordinal);
        if (!StringComparer.Ordinal.Equals(first.ToSemanticNodeId, second.FromSemanticNodeId) ||
            nodeSet.Count != 3 ||
            !nodeSet.SetEquals(plan.Nodes.Select(item => item.Id)))
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionUnsupportedTopology,
                DiagnosticSeverity.Warning,
                "The compiled requirements do not form one acyclic two-edge linear chain.",
                plan.CompiledPlanId));
            return false;
        }

        if (first.ExactPhaseRelation != Rational.Zero || second.ExactPhaseRelation != Rational.Zero)
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionPhaseUnsupported,
                DiagnosticSeverity.Warning,
                "The initial composer supports only zero shared endpoint phase.",
                first.ToSemanticNodeId));
            return false;
        }

        chain = new PlanChain(first, second);
        return true;
    }

    private static bool TryOrderSelections(
        PlanChain chain,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        Func<GenerationCandidate, string> candidateIdFactory,
        Func<ExactRatioMechanismCandidate, string> artifactHashFactory,
        ICollection<Diagnostic> diagnostics,
        out IReadOnlyList<EdgeContext>? contexts,
        out IReadOnlyList<CompositionInputEdgeProvenance> inputEdges,
        out BigInteger pitchScale)
    {
        contexts = null;
        pitchScale = BigInteger.Zero;
        inputEdges = Array.Empty<CompositionInputEdgeProvenance>();
        var grouped = selectedEdges.GroupBy(item => item.CompiledRequirementId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var requirements = chain.Requirements;
        if (selectedEdges.Count != requirements.Count ||
            requirements.Any(requirement => !grouped.TryGetValue(requirement.CompiledRequirementId, out var matches) || matches.Count != 1) ||
            grouped.Keys.Except(requirements.Select(item => item.CompiledRequirementId), StringComparer.Ordinal).Any())
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionEdgeMappingMismatch,
                DiagnosticSeverity.Error,
                "Exactly one selected mechanism must match each compiled requirement ID.",
                "selectedEdges"));
            return false;
        }

        var built = new List<EdgeContext>();
        var provenance = new List<CompositionInputEdgeProvenance>();
        for (var index = 0; index < requirements.Count; index++)
        {
            var requirement = requirements[index];
            var selected = grouped[requirement.CompiledRequirementId].Single();
            if (!StringComparer.Ordinal.Equals(selected.SemanticFromNodeId, requirement.FromSemanticNodeId) ||
                !StringComparer.Ordinal.Equals(selected.SemanticToNodeId, requirement.ToSemanticNodeId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CompositionEdgeMappingMismatch,
                    DiagnosticSeverity.Error,
                    "Selected edge endpoint mapping does not match the compiled requirement.",
                    requirement.CompiledRequirementId));
                return false;
            }

            var candidateId = candidateIdFactory(selected.Mechanism.Candidate);
            var artifactHash = artifactHashFactory(selected.Mechanism);
            var edgeValidation = GenerationEngine.ValidateGeneratedMechanismCandidate(
                selected.Mechanism.Candidate,
                selected.Mechanism.Provenance.GenerationRequest);
            if (!StringComparer.Ordinal.Equals(candidateId, selected.CandidateId) ||
                !StringComparer.Ordinal.Equals(artifactHash, selected.ArtifactHash) ||
                !edgeValidation.IsValid ||
                selected.Mechanism.KinematicSource.ExactTransfer != requirement.SignedTargetTransfer ||
                !TryBuildEdgeContext(index, requirement, selected, diagnostics, out var context))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CompositionEdgeCandidateMismatch,
                    DiagnosticSeverity.Error,
                    "Selected edge identity, exact transfer, topology, or stored mechanical payload is inconsistent.",
                    requirement.CompiledRequirementId));
                return false;
            }

            built.Add(context!);
            provenance.Add(new CompositionInputEdgeProvenance(
                index,
                requirement.CompiledRequirementId,
                requirement.FromSemanticNodeId,
                requirement.ToSemanticNodeId,
                requirement.SignedTargetTransfer,
                requirement.ExactPhaseRelation,
                selected.Mechanism.Provenance.GenerationRequestId,
                selected.Mechanism.Provenance.KinematicCandidateId,
                selected.Mechanism.Provenance.SpatialCandidateId,
                selected.CandidateId,
                selected.ArtifactHash));
        }

        if (built[0].PitchScale != built[1].PitchScale || built[0].PitchScale.Sign <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionPitchScaleMismatch,
                DiagnosticSeverity.Error,
                "Selected edge mechanisms do not share one positive pitch-radius tick scale.",
                "pitchRadiusTicksPerTooth"));
            return false;
        }

        contexts = built;
        inputEdges = provenance;
        pitchScale = built[0].PitchScale;
        return true;
    }

    private static bool TryBuildEdgeContext(
        int edgeIndex,
        CompiledContinuousTransferRequirement requirement,
        SelectedPlanEdgeMechanism selected,
        ICollection<Diagnostic> diagnostics,
        out EdgeContext? context)
    {
        context = null;
        var candidate = selected.Mechanism.Candidate;
        var couplingsByDriver = candidate.Kinematic.Couplings
            .GroupBy(item => item.DriverDofId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var pathDofs = new List<string> { candidate.Kinematic.RootDofId };
        var pathCouplings = new List<ExternalGearCoupling>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { candidate.Kinematic.RootDofId };
        var current = candidate.Kinematic.RootDofId;
        while (pathCouplings.Count < candidate.Kinematic.Couplings.Count)
        {
            if (!couplingsByDriver.TryGetValue(current, out var outgoing) || outgoing.Count != 1)
            {
                return false;
            }

            var coupling = outgoing[0];
            if (!visited.Add(coupling.DrivenDofId))
            {
                return false;
            }

            pathCouplings.Add(coupling);
            pathDofs.Add(coupling.DrivenDofId);
            current = coupling.DrivenDofId;
        }

        if (pathDofs.Count != candidate.Kinematic.Dofs.Count ||
            pathCouplings.Count != selected.Mechanism.KinematicSource.OrderedStages.Count ||
            !candidate.Solution.TryGetState(pathDofs[pathDofs.Count - 1], out var outputState) ||
            outputState!.Coefficient != requirement.SignedTargetTransfer ||
            outputState.PhaseOffset != requirement.ExactPhaseRelation)
        {
            return false;
        }

        var dofMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < pathDofs.Count; index++)
        {
            var composed = index == 0
                ? NodeDofId(edgeIndex)
                : index == pathDofs.Count - 1
                    ? NodeDofId(edgeIndex + 1)
                    : IntermediateDofId(edgeIndex, index - 1);
            dofMap.Add(pathDofs[index], composed);
        }

        var couplingMap = pathCouplings.Select((item, index) => new
        {
            item.Id,
            Composed = CouplingId(edgeIndex, index),
        }).ToDictionary(item => item.Id, item => item.Composed, StringComparer.Ordinal);
        var bodyGroups = candidate.Spatial.Bodies.GroupBy(item => item.AxisId, StringComparer.Ordinal).ToList();
        var axisMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in bodyGroups)
        {
            var dofs = group.Select(item => item.DofId).Distinct(StringComparer.Ordinal).ToList();
            if (dofs.Count != 1 || !dofMap.TryGetValue(dofs[0], out var composedDof))
            {
                return false;
            }

            axisMap.Add(group.Key, AxisId(composedDof));
        }

        if (axisMap.Count != candidate.Spatial.Axes.Count ||
            candidate.Spatial.Axes.Any(axis => !axisMap.ContainsKey(axis.Id)))
        {
            return false;
        }

        var bodiesById = candidate.Spatial.Bodies.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var contactsByConstraint = candidate.Spatial.Contacts
            .GroupBy(item => item.ConstraintId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var bodyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var contactMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var stage = 0; stage < pathCouplings.Count; stage++)
        {
            var coupling = pathCouplings[stage];
            if (!contactsByConstraint.TryGetValue(coupling.Id, out var matches) || matches.Count != 1)
            {
                return false;
            }

            var contact = matches[0];
            if (!bodiesById.TryGetValue(contact.BodyAId, out var bodyA) ||
                !bodiesById.TryGetValue(contact.BodyBId, out var bodyB))
            {
                return false;
            }

            var driver = StringComparer.Ordinal.Equals(bodyA.DofId, coupling.DriverDofId) ? bodyA : bodyB;
            var driven = StringComparer.Ordinal.Equals(bodyA.DofId, coupling.DrivenDofId) ? bodyA : bodyB;
            if (!bodyMap.ContainsKey(driver.Id)) bodyMap.Add(driver.Id, BodyId(edgeIndex, stage, "driver"));
            if (!bodyMap.ContainsKey(driven.Id)) bodyMap.Add(driven.Id, BodyId(edgeIndex, stage, "driven"));
            contactMap.Add(contact.Id, ContactId(edgeIndex, stage));
        }

        if (bodyMap.Count != candidate.Spatial.Bodies.Count || contactMap.Count != candidate.Spatial.Contacts.Count)
        {
            return false;
        }

        BigInteger? pitchScale = null;
        foreach (var body in candidate.Spatial.Bodies)
        {
            if (body.ToothCount <= 0 || body.PitchRadius.Sign <= 0 ||
                body.PitchRadius % body.ToothCount != BigInteger.Zero)
            {
                return false;
            }

            var scale = body.PitchRadius / body.ToothCount;
            if (pitchScale.HasValue && pitchScale.Value != scale) return false;
            pitchScale = scale;
        }

        context = new EdgeContext(
            edgeIndex,
            requirement,
            selected,
            pathDofs,
            pathCouplings,
            dofMap,
            couplingMap,
            axisMap,
            bodyMap,
            contactMap,
            pitchScale ?? BigInteger.Zero);
        return true;
    }

    private static KinematicSpecification BuildKinematic(IReadOnlyList<EdgeContext> contexts)
    {
        var dofs = new Dictionary<string, RotationalDof>(StringComparer.Ordinal);
        var couplings = new List<ExternalGearCoupling>();
        foreach (var context in contexts)
        {
            foreach (var pair in context.DofMap)
            {
                if (!dofs.ContainsKey(pair.Value))
                {
                    dofs.Add(pair.Value, new RotationalDof(pair.Value, StringComparer.Ordinal.Equals(pair.Value, NodeDofId(0))));
                }
            }

            foreach (var coupling in context.PathCouplings)
            {
                couplings.Add(new ExternalGearCoupling(
                    context.CouplingMap[coupling.Id],
                    context.DofMap[coupling.DriverDofId],
                    context.DofMap[coupling.DrivenDofId],
                    coupling.DriverTeeth,
                    coupling.DrivenTeeth,
                    coupling.PhaseOffset));
            }
        }

        return new KinematicSpecification(NodeDofId(0), dofs.Values, couplings);
    }

    private static IReadOnlyList<SemanticDofBinding> BuildSemanticBindings(PlanChain chain) =>
        new[]
        {
            new SemanticDofBinding(chain.First.FromSemanticNodeId, NodeDofId(0), SemanticBindingRole.Root),
            new SemanticDofBinding(chain.First.ToSemanticNodeId, NodeDofId(1), SemanticBindingRole.IntermediateOutput),
            new SemanticDofBinding(chain.Second.ToSemanticNodeId, NodeDofId(2), SemanticBindingRole.FinalOutput),
        };

    private static bool ValidateComposedKinematics(
        PlanChain chain,
        KinematicSolveResult solved,
        IReadOnlyList<SemanticDofBinding> bindings,
        ICollection<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in solved.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }
        if (!solved.IsValid || solved.Solution is null) return false;
        var root = solved.Solution.States.Single(item => item.DofId == NodeDofId(0));
        var shared = solved.Solution.States.Single(item => item.DofId == NodeDofId(1));
        var final = solved.Solution.States.Single(item => item.DofId == NodeDofId(2));
        var valid = root.Coefficient == Rational.One && root.PhaseOffset == Rational.Zero &&
            shared.Coefficient == chain.First.SignedTargetTransfer &&
            shared.PhaseOffset == chain.First.ExactPhaseRelation &&
            final.Coefficient == chain.First.SignedTargetTransfer * chain.Second.SignedTargetTransfer &&
            final.PhaseOffset == Rational.Zero &&
            final.Coefficient / shared.Coefficient == chain.Second.SignedTargetTransfer;
        if (!valid)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.CompositionEdgeCandidateMismatch,
                DiagnosticSeverity.Error,
                "Merged Kinematic propagation does not preserve the compiled exact edge and derived relations.",
                chain.First.ToSemanticNodeId));
        }

        foreach (var diagnostic in ContinuousPlanCompositionValidation.ValidateSemanticBindings(
            bindings,
            new[]
            {
                new CompositionInputEdgeProvenance(0, chain.First.CompiledRequirementId,
                    chain.First.FromSemanticNodeId, chain.First.ToSemanticNodeId,
                    chain.First.SignedTargetTransfer, chain.First.ExactPhaseRelation, "", "", "", "", ""),
                new CompositionInputEdgeProvenance(1, chain.Second.CompiledRequirementId,
                    chain.Second.FromSemanticNodeId, chain.Second.ToSemanticNodeId,
                    chain.Second.SignedTargetTransfer, chain.Second.ExactPhaseRelation, "", "", "", "", ""),
            },
            solved.Solution).Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }
        return valid && !diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
    }

    private static List<IReadOnlyDictionary<int, int>> EnumerateLayerMappings(
        EdgeContext anchor,
        EdgeContext moving,
        int maxLayers)
    {
        if (anchor.Selected.Mechanism.Candidate.Spatial.Bodies.Any(body => body.Layer < 0 || body.Layer >= maxLayers))
        {
            return new List<IReadOnlyDictionary<int, int>>();
        }

        var localLayers = moving.Selected.Mechanism.Candidate.Spatial.Bodies
            .Select(item => item.Layer)
            .Distinct()
            .OrderBy(item => item)
            .ToList();
        var anchorSharedLayers = anchor.Selected.Mechanism.Candidate.Spatial.Bodies
            .Where(item => StringComparer.Ordinal.Equals(item.DofId, anchor.PathDofs[anchor.PathDofs.Count - 1]))
            .Select(item => item.Layer)
            .ToHashSet();
        var movingSharedLocalLayers = moving.Selected.Mechanism.Candidate.Spatial.Bodies
            .Where(item => StringComparer.Ordinal.Equals(item.DofId, moving.PathDofs[0]))
            .Select(item => item.Layer)
            .ToHashSet();
        var result = new List<IReadOnlyDictionary<int, int>>();
        Enumerate(0, new Dictionary<int, int>(), new HashSet<int>());
        return result;

        void Enumerate(int index, IDictionary<int, int> current, ISet<int> used)
        {
            if (index == localLayers.Count)
            {
                if (movingSharedLocalLayers.Any(local => anchorSharedLayers.Contains(current[local]))) return;
                result.Add(new Dictionary<int, int>(current));
                return;
            }

            var local = localLayers[index];
            for (var global = 0; global < maxLayers; global++)
            {
                if (!used.Add(global)) continue;
                current.Add(local, global);
                Enumerate(index + 1, current, used);
                current.Remove(local);
                used.Remove(global);
            }
        }
    }

    private static BuiltSpatial BuildSpatial(
        EdgeContext anchor,
        EdgeContext moving,
        CardinalRotation rotation,
        IReadOnlyDictionary<int, int> movingLayerMap)
    {
        var axes = new Dictionary<string, SpatialAxis>(StringComparer.Ordinal);
        foreach (var axis in anchor.Selected.Mechanism.Candidate.Spatial.Axes)
        {
            axes.Add(anchor.AxisMap[axis.Id], new SpatialAxis(anchor.AxisMap[axis.Id], axis.X, axis.Y));
        }

        var sharedAnchorLocalAxis = anchor.Selected.Mechanism.Candidate.Spatial.Bodies
            .Single(item => StringComparer.Ordinal.Equals(item.DofId, anchor.PathDofs[anchor.PathDofs.Count - 1])).AxisId;
        var sharedMovingLocalAxis = moving.Selected.Mechanism.Candidate.Spatial.Bodies
            .Single(item => StringComparer.Ordinal.Equals(item.DofId, moving.PathDofs[0])).AxisId;
        var anchorPoint = anchor.Selected.Mechanism.Candidate.Spatial.Axes.Single(item => item.Id == sharedAnchorLocalAxis);
        var movingPivot = moving.Selected.Mechanism.Candidate.Spatial.Axes.Single(item => item.Id == sharedMovingLocalAxis);
        foreach (var axis in moving.Selected.Mechanism.Candidate.Spatial.Axes)
        {
            var composedId = moving.AxisMap[axis.Id];
            var rotated = Rotate(axis.X - movingPivot.X, axis.Y - movingPivot.Y, rotation);
            var x = anchorPoint.X + rotated.X;
            var y = anchorPoint.Y + rotated.Y;
            if (axes.TryGetValue(composedId, out var existing))
            {
                if (existing.X != x || existing.Y != y)
                    throw new InvalidOperationException("Shared composed axis alignment was not exact.");
                continue;
            }

            axes.Add(composedId, new SpatialAxis(composedId, x, y));
        }

        var unnormalizedBodies = new List<SpatialBody>();
        unnormalizedBodies.AddRange(anchor.Selected.Mechanism.Candidate.Spatial.Bodies.Select(body => new SpatialBody(
            anchor.BodyMap[body.Id], body.Kind, anchor.AxisMap[body.AxisId], anchor.DofMap[body.DofId],
            body.Layer, body.ToothCount, body.PitchRadius, body.ExactMountingPhase)));
        unnormalizedBodies.AddRange(moving.Selected.Mechanism.Candidate.Spatial.Bodies.Select(body => new SpatialBody(
            moving.BodyMap[body.Id], body.Kind, moving.AxisMap[body.AxisId], moving.DofMap[body.DofId],
            movingLayerMap[body.Layer], body.ToothCount, body.PitchRadius, body.ExactMountingPhase)));
        var usedLayers = unnormalizedBodies.Select(item => item.Layer).Distinct().OrderBy(item => item).ToList();
        var normalization = usedLayers.Select((layer, index) => new { layer, index })
            .ToDictionary(item => item.layer, item => item.index);
        var bodies = unnormalizedBodies.Select(body => new SpatialBody(
            body.Id, body.Kind, body.AxisId, body.DofId, normalization[body.Layer],
            body.ToothCount, body.PitchRadius, body.ExactMountingPhase));
        var contacts = anchor.Selected.Mechanism.Candidate.Spatial.Contacts.Select(contact => new SpatialContact(
                anchor.ContactMap[contact.Id], contact.Kind, anchor.CouplingMap[contact.ConstraintId],
                anchor.BodyMap[contact.BodyAId], anchor.BodyMap[contact.BodyBId]))
            .Concat(moving.Selected.Mechanism.Candidate.Spatial.Contacts.Select(contact => new SpatialContact(
                moving.ContactMap[contact.Id], contact.Kind, moving.CouplingMap[contact.ConstraintId],
                moving.BodyMap[contact.BodyAId], moving.BodyMap[contact.BodyBId])));
        var normalizedMap = movingLayerMap.OrderBy(item => item.Key)
            .Select(item => new CompositionLayerMapping(item.Key, normalization[item.Value]))
            .ToList();
        return new BuiltSpatial(new SpatialMechanism(axes.Values, bodies, contacts), normalizedMap);
    }

    private static (BigInteger X, BigInteger Y) Rotate(BigInteger x, BigInteger y, CardinalRotation rotation) =>
        rotation switch
        {
            CardinalRotation.Degrees0 => (x, y),
            CardinalRotation.Degrees90 => (BigInteger.Negate(y), x),
            CardinalRotation.Degrees180 => (BigInteger.Negate(x), BigInteger.Negate(y)),
            CardinalRotation.Degrees270 => (y, BigInteger.Negate(x)),
            _ => throw new InvalidOperationException("Unsupported cardinal rotation '" + rotation + "'."),
        };

    private static ContinuousPlanCompositionMetrics BuildMetrics(
        KinematicSpecification kinematic,
        SpatialMechanism spatial,
        int collisionChecks)
    {
        var axes = spatial.Axes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var first = spatial.Bodies[0];
        var firstAxis = axes[first.AxisId];
        var minX = firstAxis.X - first.PitchRadius;
        var maxX = firstAxis.X + first.PitchRadius;
        var minY = firstAxis.Y - first.PitchRadius;
        var maxY = firstAxis.Y + first.PitchRadius;
        foreach (var body in spatial.Bodies.Skip(1))
        {
            var axis = axes[body.AxisId];
            minX = BigInteger.Min(minX, axis.X - body.PitchRadius);
            maxX = BigInteger.Max(maxX, axis.X + body.PitchRadius);
            minY = BigInteger.Min(minY, axis.Y - body.PitchRadius);
            maxY = BigInteger.Max(maxY, axis.Y + body.PitchRadius);
        }

        return new ContinuousPlanCompositionMetrics(
            spatial.Bodies.Select(item => item.Layer).Distinct().Count(),
            kinematic.Dofs.Count,
            spatial.Axes.Count,
            spatial.Bodies.Count,
            spatial.Contacts.Count,
            maxX - minX,
            maxY - minY,
            collisionChecks);
    }

    private static ContinuousPlanCompositionResult Terminal(
        CompiledMechanicalRequirementPlan plan,
        ContinuousPlanCompositionRequest request,
        ContinuousPlanCompositionFingerprint fingerprint,
        IReadOnlyList<CompositionInputEdgeProvenance> inputEdges,
        ContinuousPlanCompositionStatus status,
        IEnumerable<Diagnostic> diagnostics)
    {
        var canonical = ContinuousPlanCompositionIdentity.BuildRequestCanonicalRepresentation(
            plan.CompiledPlanId, inputEdges, request);
        return new ContinuousPlanCompositionResult(
            plan.CompiledPlanId,
            request,
            canonical,
            ContinuousPlanCompositionIdentity.ComputeRequestId(canonical),
            fingerprint,
            status,
            inputEdges,
            Array.Empty<ContinuousPlanCompositionCandidate>(),
            diagnostics,
            EmptySummary(searchComplete: false),
            new ContinuousPlanCompositionPerformance(0, 0, 0));
    }

    private static ContinuousPlanCompositionSearchSummary EmptySummary(bool searchComplete) =>
        new(0, 0, 0, 0, 0, 0, false, searchComplete);

    private static long ToMicroseconds(long ticks) =>
        (long)Math.Round(ticks * 1_000_000d / Stopwatch.Frequency, MidpointRounding.AwayFromZero);

    private static string NodeDofId(int nodeIndex) =>
        "dof:plan-node:" + nodeIndex.ToString("D2", CultureInfo.InvariantCulture);

    private static string IntermediateDofId(int edgeIndex, int intermediateIndex) =>
        "dof:plan-edge:" + edgeIndex.ToString("D2", CultureInfo.InvariantCulture) +
        ":intermediate:" + intermediateIndex.ToString("D2", CultureInfo.InvariantCulture);

    private static string CouplingId(int edgeIndex, int stageIndex) =>
        "mesh:plan-edge:" + edgeIndex.ToString("D2", CultureInfo.InvariantCulture) +
        ":" + stageIndex.ToString("D2", CultureInfo.InvariantCulture);

    private static string AxisId(string dofId) =>
        dofId.StartsWith("dof:", StringComparison.Ordinal) ? "axis:" + dofId.Substring(4) : "axis:" + dofId;

    private static string BodyId(int edgeIndex, int stageIndex, string role) =>
        "body:plan-edge:" + edgeIndex.ToString("D2", CultureInfo.InvariantCulture) +
        ":stage:" + stageIndex.ToString("D2", CultureInfo.InvariantCulture) + ":" + role;

    private static string ContactId(int edgeIndex, int stageIndex) =>
        "contact:plan-edge:" + edgeIndex.ToString("D2", CultureInfo.InvariantCulture) +
        ":mesh:" + stageIndex.ToString("D2", CultureInfo.InvariantCulture);

    private sealed class PlanChain
    {
        public PlanChain(CompiledContinuousTransferRequirement first, CompiledContinuousTransferRequirement second)
        {
            First = first;
            Second = second;
            Requirements = new[] { first, second };
        }

        public CompiledContinuousTransferRequirement First { get; }
        public CompiledContinuousTransferRequirement Second { get; }
        public IReadOnlyList<CompiledContinuousTransferRequirement> Requirements { get; }
    }

    private sealed class EdgeContext
    {
        public EdgeContext(
            int index,
            CompiledContinuousTransferRequirement requirement,
            SelectedPlanEdgeMechanism selected,
            IReadOnlyList<string> pathDofs,
            IReadOnlyList<ExternalGearCoupling> pathCouplings,
            IReadOnlyDictionary<string, string> dofMap,
            IReadOnlyDictionary<string, string> couplingMap,
            IReadOnlyDictionary<string, string> axisMap,
            IReadOnlyDictionary<string, string> bodyMap,
            IReadOnlyDictionary<string, string> contactMap,
            BigInteger pitchScale)
        {
            Index = index;
            Requirement = requirement;
            Selected = selected;
            PathDofs = pathDofs;
            PathCouplings = pathCouplings;
            DofMap = dofMap;
            CouplingMap = couplingMap;
            AxisMap = axisMap;
            BodyMap = bodyMap;
            ContactMap = contactMap;
            PitchScale = pitchScale;
        }

        public int Index { get; }
        public CompiledContinuousTransferRequirement Requirement { get; }
        public SelectedPlanEdgeMechanism Selected { get; }
        public IReadOnlyList<string> PathDofs { get; }
        public IReadOnlyList<ExternalGearCoupling> PathCouplings { get; }
        public IReadOnlyDictionary<string, string> DofMap { get; }
        public IReadOnlyDictionary<string, string> CouplingMap { get; }
        public IReadOnlyDictionary<string, string> AxisMap { get; }
        public IReadOnlyDictionary<string, string> BodyMap { get; }
        public IReadOnlyDictionary<string, string> ContactMap { get; }
        public BigInteger PitchScale { get; }
    }

    private sealed class BuiltSpatial
    {
        public BuiltSpatial(SpatialMechanism spatial, IReadOnlyList<CompositionLayerMapping> normalizedLayerMapping)
        {
            Spatial = spatial;
            NormalizedLayerMapping = normalizedLayerMapping;
        }

        public SpatialMechanism Spatial { get; }
        public IReadOnlyList<CompositionLayerMapping> NormalizedLayerMapping { get; }
    }

    private sealed class CandidateDraft
    {
        public CandidateDraft(
            GenerationCandidate mechanical,
            string composedSpatialId,
            string candidateId,
            CardinalRotation rotation,
            IReadOnlyList<CompositionLayerMapping> layerMapping,
            ContinuousPlanCompositionMetrics metrics)
        {
            Mechanical = mechanical;
            ComposedSpatialId = composedSpatialId;
            CandidateId = candidateId;
            Rotation = rotation;
            LayerMapping = layerMapping;
            Metrics = metrics;
        }

        public GenerationCandidate Mechanical { get; }
        public string ComposedSpatialId { get; }
        public string CandidateId { get; }
        public CardinalRotation Rotation { get; }
        public IReadOnlyList<CompositionLayerMapping> LayerMapping { get; }
        public ContinuousPlanCompositionMetrics Metrics { get; }
    }

    private sealed class CandidateDraftComparer : IComparer<CandidateDraft>
    {
        public static readonly CandidateDraftComparer Instance = new();

        public int Compare(CandidateDraft? left, CandidateDraft? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var value = left.Metrics.UsedLayerCount.CompareTo(right.Metrics.UsedLayerCount);
            if (value != 0) return value;
            value = left.Metrics.BoundingArea.CompareTo(right.Metrics.BoundingArea);
            if (value != 0) return value;
            value = left.Metrics.MaximumExtent.CompareTo(right.Metrics.MaximumExtent);
            if (value != 0) return value;
            value = ((int)left.Rotation).CompareTo((int)right.Rotation);
            if (value != 0) return value;
            value = StringComparer.Ordinal.Compare(
                string.Join(",", left.LayerMapping.Select(item => item.LocalLayer + ":" + item.GlobalLayer)),
                string.Join(",", right.LayerMapping.Select(item => item.LocalLayer + ":" + item.GlobalLayer)));
            return value != 0 ? value : StringComparer.Ordinal.Compare(left.ComposedSpatialId, right.ComposedSpatialId);
        }
    }
}

public static class ContinuousPlanCompositionValidation
{
    public static ValidationReport ValidateSemanticBindings(
        IEnumerable<SemanticDofBinding> bindings,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges,
        KinematicSolution solution)
    {
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));
        if (inputEdges is null) throw new ArgumentNullException(nameof(inputEdges));
        if (solution is null) throw new ArgumentNullException(nameof(solution));
        var diagnostics = new List<Diagnostic>();
        var bindingList = bindings.ToList();
        var edges = inputEdges.OrderBy(item => item.Index).ToList();
        var states = solution.States.ToDictionary(item => item.DofId, StringComparer.Ordinal);
        if (edges.Count != 2 || bindingList.Count != 3 ||
            bindingList.Select(item => item.SemanticNodeId).Distinct(StringComparer.Ordinal).Count() != bindingList.Count ||
            bindingList.Select(item => item.DofId).Distinct(StringComparer.Ordinal).Count() != bindingList.Count)
        {
            diagnostics.Add(Failure("semanticBindings"));
            return new ValidationReport(diagnostics);
        }

        var expected = new[]
        {
            new { Semantic = edges[0].SemanticFromNodeId, Role = SemanticBindingRole.Root,
                Coefficient = Rational.One, Phase = Rational.Zero },
            new { Semantic = edges[0].SemanticToNodeId, Role = SemanticBindingRole.IntermediateOutput,
                Coefficient = edges[0].SignedTargetTransfer, Phase = edges[0].ExactPhaseRelation },
            new { Semantic = edges[1].SemanticToNodeId, Role = SemanticBindingRole.FinalOutput,
                Coefficient = edges[0].SignedTargetTransfer * edges[1].SignedTargetTransfer, Phase = Rational.Zero },
        };
        if (!StringComparer.Ordinal.Equals(edges[0].SemanticToNodeId, edges[1].SemanticFromNodeId))
        {
            diagnostics.Add(Failure("inputEdges"));
            return new ValidationReport(diagnostics);
        }

        foreach (var item in expected)
        {
            var matches = bindingList.Where(binding =>
                StringComparer.Ordinal.Equals(binding.SemanticNodeId, item.Semantic) && binding.Role == item.Role).ToList();
            if (matches.Count != 1 || !states.TryGetValue(matches[0].DofId, out var state) ||
                state.Coefficient != item.Coefficient || state.PhaseOffset != item.Phase)
            {
                diagnostics.Add(Failure(item.Semantic));
            }
        }

        var rootBinding = bindingList.SingleOrDefault(item => item.Role == SemanticBindingRole.Root);
        if (rootBinding is null || !StringComparer.Ordinal.Equals(rootBinding.DofId, solution.RootDofId))
        {
            diagnostics.Add(Failure("root"));
        }

        return new ValidationReport(diagnostics);
    }

    private static Diagnostic Failure(string subject) => new(
        DiagnosticCodes.CompositionSemanticBindingMismatch,
        DiagnosticSeverity.Error,
        "Composition semantic binding does not match the exact composed Kinematic solution.",
        subject);
}
