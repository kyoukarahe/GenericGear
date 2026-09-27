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

internal static class BranchedContinuousPlanComposer
{
    private static readonly CardinalRotation[] RotationOrder =
    {
        CardinalRotation.Degrees0,
        CardinalRotation.Degrees90,
        CardinalRotation.Degrees180,
        CardinalRotation.Degrees270,
    };

    public static BranchedContinuousCompositionResult Compose(
        CompiledMechanicalRequirementPlan plan,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        BranchedContinuousCompositionRequest request,
        Func<GenerationCandidate, string> candidateIdFactory,
        Func<ExactRatioMechanismCandidate, string> artifactHashFactory,
        CancellationToken cancellationToken)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (selectedEdges is null) throw new ArgumentNullException(nameof(selectedEdges));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (candidateIdFactory is null) throw new ArgumentNullException(nameof(candidateIdFactory));
        if (artifactHashFactory is null) throw new ArgumentNullException(nameof(artifactHashFactory));

        var fingerprint = new BranchedContinuousCompositionFingerprint(
            BranchedContinuousCompositionContract.BackendId,
            BranchedContinuousCompositionContract.BackendVersion,
            request.DeterminismProfile);
        var diagnostics = ValidateRequest(request);
        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            var unsupportedProfile = diagnostics.Any(item =>
                item.Code == DiagnosticCodes.BranchedCompositionUnsupportedProfile);
            return Terminal(
                plan,
                request,
                fingerprint,
                Array.Empty<CompositionInputEdgeProvenance>(),
                unsupportedProfile
                    ? BranchedContinuousCompositionStatus.Unsupported
                    : BranchedContinuousCompositionStatus.InvalidInput,
                diagnostics);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionCancelled,
                DiagnosticSeverity.Warning,
                "Branched continuous composition was cancelled before deterministic work began."));
            return Terminal(
                plan,
                request,
                fingerprint,
                Array.Empty<CompositionInputEdgeProvenance>(),
                BranchedContinuousCompositionStatus.Cancelled,
                diagnostics);
        }

        if (!TryBuildTopology(plan, request, diagnostics, out var topology, out var unsupported))
        {
            return Terminal(
                plan,
                request,
                fingerprint,
                Array.Empty<CompositionInputEdgeProvenance>(),
                unsupported
                    ? BranchedContinuousCompositionStatus.Unsupported
                    : BranchedContinuousCompositionStatus.InvalidInput,
                diagnostics);
        }

        if (!TryOrderSelections(
                topology!,
                selectedEdges,
                candidateIdFactory,
                artifactHashFactory,
                diagnostics,
                out var edgeContexts,
                out var inputEdges,
                out var pitchScale))
        {
            return Terminal(
                plan,
                request,
                fingerprint,
                inputEdges,
                BranchedContinuousCompositionStatus.InvalidInput,
                diagnostics);
        }

        if (!ValidateHints(topology!, request, diagnostics))
        {
            return Terminal(
                plan,
                request,
                fingerprint,
                inputEdges,
                BranchedContinuousCompositionStatus.InvalidInput,
                diagnostics);
        }

        var requestCanonical = BranchedContinuousCompositionIdentity.BuildRequestCanonicalRepresentation(
            plan.CompiledPlanId,
            inputEdges,
            request);
        var requestId = BranchedContinuousCompositionIdentity.ComputeRequestId(requestCanonical);

        var kinematicWatch = Stopwatch.StartNew();
        var kinematic = BuildKinematic(edgeContexts!);
        var solved = KinematicSolver.Solve(kinematic);
        var bindings = BuildSemanticBindings(topology!);
        var kinematicValid = ValidateComposedKinematics(
            plan,
            topology!,
            solved,
            bindings,
            inputEdges,
            diagnostics);
        var composedKinematicId = BranchedContinuousCompositionIdentity.ComputeBranchedComposedKinematicCandidateId(
            kinematic,
            fingerprint);
        kinematicWatch.Stop();
        if (!kinematicValid || solved.Solution is null)
        {
            return new BranchedContinuousCompositionResult(
                plan.CompiledPlanId,
                request,
                requestCanonical,
                requestId,
                fingerprint,
                BranchedContinuousCompositionStatus.InvalidInput,
                inputEdges,
                Array.Empty<BranchedContinuousCompositionCandidate>(),
                diagnostics,
                EmptySummary(searchComplete: false),
                new BranchedContinuousCompositionPerformance(
                    ToMicroseconds(kinematicWatch.ElapsedTicks),
                    0,
                    0));
        }

        var anchor = edgeContexts![0];
        var branches = edgeContexts.Skip(1).ToList();
        var rotationTuples = EnumerateRotationTuples();
        var layerMappingTuples = EnumerateLayerMappingTuples(anchor, branches, request.MaxLayers);
        var draftsBySpatialId = new Dictionary<string, CandidateDraft>(StringComparer.Ordinal);
        var rawFeasible = 0L;
        var hintFiltered = 0L;
        var hintRanked = 0L;
        var expansions = 0L;
        var budgetExhausted = false;
        var cancelled = false;
        var spatialTicks = 0L;
        var validationTicks = 0L;

        foreach (var rotations in rotationTuples)
        {
            foreach (var layerTuple in layerMappingTuples)
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
                var built = BuildSpatial(anchor, branches, rotations, layerTuple);
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
                var hintMetrics = EvaluateHints(request.BranchHints, built.BranchPlacements);
                if (!hintMetrics.RequiredSatisfied)
                {
                    hintFiltered++;
                    continue;
                }

                hintRanked++;
                var mechanical = new GenerationCandidate(
                    requestId,
                    kinematic,
                    solved.Solution,
                    built.Spatial,
                    playback,
                    validation);
                var composedSpatialId = BranchedContinuousCompositionIdentity.ComputeBranchedComposedSpatialCandidateId(
                    composedKinematicId,
                    built.Spatial,
                    request.ClearanceTicks,
                    fingerprint);
                var mechanicalMetrics = BuildMechanicalMetrics(
                    kinematic,
                    built.Spatial,
                    spatialValidation.UnrelatedSameLayerPairChecks);
                var metrics = new BranchedContinuousCompositionMetrics(
                    hintMetrics.RequiredSatisfactionCount,
                    hintMetrics.SoftDirectionMissCount,
                    hintMetrics.SoftLayerMissCount,
                    hintMetrics.OutputAnchorPenalty,
                    mechanicalMetrics);
                var draft = new CandidateDraft(
                    mechanical,
                    composedSpatialId,
                    candidateIdFactory(mechanical),
                    built.BranchPlacements,
                    metrics);
                if (!draftsBySpatialId.TryGetValue(composedSpatialId, out var existing) ||
                    CandidateDraftComparer.Instance.Compare(draft, existing) < 0)
                {
                    draftsBySpatialId[composedSpatialId] = draft;
                }
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
            ? BranchedContinuousCompositionStatus.Cancelled
            : budgetExhausted
                ? BranchedContinuousCompositionStatus.IncompleteBudget
                : returnedDrafts.Count == 0
                    ? BranchedContinuousCompositionStatus.Infeasible
                    : BranchedContinuousCompositionStatus.Complete;
        if (cancelled)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionCancelled,
                DiagnosticSeverity.Warning,
                "Branched continuous composition was cancelled between deterministic expansions."));
        }

        if (budgetExhausted)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionBudgetExhausted,
                DiagnosticSeverity.Warning,
                "The deterministic branched composition expansion budget was exhausted.",
                "maxCompositionExpansions"));
        }

        if (status == BranchedContinuousCompositionStatus.Infeasible)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionInfeasible,
                DiagnosticSeverity.Info,
                hintFiltered > 0
                    ? "No valid branched composition satisfies all Required placement hints."
                    : "No valid branched composition exists within the declared layer and collision constraints."));
        }

        if (truncated)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionResultTruncated,
                DiagnosticSeverity.Info,
                "Branched composition candidates were truncated by maxReturnedCompositions.",
                "maxReturnedCompositions"));
        }

        var summary = new BranchedContinuousCompositionSearchSummary(
            branches.Count,
            rotationTuples.Count,
            layerMappingTuples.Count,
            expansions,
            rawFeasible,
            hintFiltered,
            hintRanked,
            orderedDrafts.Count,
            returnedDrafts.Count,
            truncated,
            searchComplete);
        var candidates = returnedDrafts.Select(draft => new BranchedContinuousCompositionCandidate(
            draft.Mechanical,
            composedKinematicId,
            draft.ComposedSpatialId,
            draft.CandidateId,
            bindings,
            new BranchedContinuousCompositionProvenance(
                BranchedContinuousCompositionContract.TopologyKind,
                plan.CompiledPlanId,
                request,
                requestCanonical,
                requestId,
                fingerprint,
                pitchScale,
                inputEdges,
                draft.BranchPlacements,
                summary),
            draft.Metrics));
        return new BranchedContinuousCompositionResult(
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
            new BranchedContinuousCompositionPerformance(
                ToMicroseconds(kinematicWatch.ElapsedTicks),
                ToMicroseconds(spatialTicks),
                ToMicroseconds(validationTicks)));
    }

    private static List<Diagnostic> ValidateRequest(BranchedContinuousCompositionRequest request)
    {
        var diagnostics = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(request.AnchorRequirementId) ||
            request.MaxLayers <= 0 ||
            request.ClearanceTicks.Sign < 0 ||
            request.MaxCompositionExpansions <= 0 ||
            request.MaxReturnedCompositions <= 0 ||
            request.BranchHints.Any(hint =>
                string.IsNullOrWhiteSpace(hint.CompiledRequirementId) ||
                string.IsNullOrWhiteSpace(hint.SemanticOutputNodeId) ||
                hint.PreferredSharedInputLayer < 0))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionInvalidRequest,
                DiagnosticSeverity.Error,
                "Branched composition IDs and limits must be valid and clearanceTicks must be non-negative.",
                "branchedCompositionRequest"));
        }

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                BranchedContinuousCompositionContract.DefaultDeterminismProfile))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionUnsupportedProfile,
                DiagnosticSeverity.Error,
                "The built-in branched composer supports only determinism profile '" +
                BranchedContinuousCompositionContract.DefaultDeterminismProfile + "'.",
                "determinismProfile"));
        }

        return diagnostics;
    }

    private static bool TryBuildTopology(
        CompiledMechanicalRequirementPlan plan,
        BranchedContinuousCompositionRequest request,
        ICollection<Diagnostic> diagnostics,
        out BranchTopology? topology,
        out bool unsupported)
    {
        topology = null;
        unsupported = false;
        var nodeGroups = plan.Nodes.GroupBy(item => item.Id, StringComparer.Ordinal).ToList();
        var requirementGroups = plan.OrderedRequirements
            .GroupBy(item => item.CompiledRequirementId, StringComparer.Ordinal)
            .ToList();
        var nodes = nodeGroups.Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(plan.CompiledPlanId) ||
            nodes.Count != plan.Nodes.Count ||
            requirementGroups.Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1) ||
            plan.OrderedRequirements.Any(requirement =>
                !nodes.ContainsKey(requirement.FromSemanticNodeId) ||
                !nodes.ContainsKey(requirement.ToSemanticNodeId) ||
                StringComparer.Ordinal.Equals(requirement.FromSemanticNodeId, requirement.ToSemanticNodeId)))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionInvalidPlan,
                DiagnosticSeverity.Error,
                "The compiled plan has duplicate IDs, unknown endpoint references, or a self-reference.",
                plan.CompiledPlanId));
            return false;
        }

        if (plan.Nodes.Count != 4 || plan.OrderedRequirements.Count != 3)
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionUnsupportedTopology,
                DiagnosticSeverity.Warning,
                "The built-in branched composer supports exactly four nodes and three continuous requirements.",
                plan.CompiledPlanId));
            return false;
        }

        var indegree = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.Keys.ToDictionary(
            id => id,
            _ => new List<CompiledContinuousTransferRequirement>(),
            StringComparer.Ordinal);
        foreach (var requirement in plan.OrderedRequirements)
        {
            indegree[requirement.ToSemanticNodeId]++;
            outgoing[requirement.FromSemanticNodeId].Add(requirement);
        }

        var rootIds = nodes.Keys.Where(id => indegree[id] == 0 && outgoing[id].Count == 1).ToList();
        var sharedIds = nodes.Keys.Where(id => indegree[id] == 1 && outgoing[id].Count == 2).ToList();
        var leafIds = nodes.Keys.Where(id => indegree[id] == 1 && outgoing[id].Count == 0).ToList();
        if (rootIds.Count != 1 || sharedIds.Count != 1 || leafIds.Count != 2 ||
            rootIds.Count + sharedIds.Count + leafIds.Count != nodes.Count)
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionUnsupportedTopology,
                DiagnosticSeverity.Warning,
                "The compiled requirements do not form one acyclic depth-two single-shared-node branch.",
                plan.CompiledPlanId));
            return false;
        }

        var rootId = rootIds[0];
        var sharedId = sharedIds[0];
        var anchor = outgoing[rootId].Single();
        var branches = plan.OrderedRequirements.Where(requirement =>
                StringComparer.Ordinal.Equals(requirement.FromSemanticNodeId, sharedId))
            .ToList();
        if (!StringComparer.Ordinal.Equals(anchor.ToSemanticNodeId, sharedId) ||
            branches.Count != 2 ||
            branches.Select(item => item.ToSemanticNodeId).Distinct(StringComparer.Ordinal).Count() != 2 ||
            !new HashSet<string>(branches.Select(item => item.ToSemanticNodeId), StringComparer.Ordinal).SetEquals(leafIds))
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionUnsupportedTopology,
                DiagnosticSeverity.Warning,
                "The compiled requirements do not form the supported root-to-shared-to-two-leaf shape.",
                plan.CompiledPlanId));
            return false;
        }

        if (plan.OrderedRequirements.Any(item => item.ExactPhaseRelation != Rational.Zero))
        {
            unsupported = true;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionPhaseUnsupported,
                DiagnosticSeverity.Warning,
                "The branched v1 composer supports only zero exact phase relations.",
                plan.CompiledPlanId));
            return false;
        }

        if (!StringComparer.Ordinal.Equals(request.AnchorRequirementId, anchor.CompiledRequirementId))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionEdgeMappingMismatch,
                DiagnosticSeverity.Error,
                "anchorRequirementId must identify the unique root-to-shared requirement.",
                request.AnchorRequirementId));
            return false;
        }

        topology = new BranchTopology(rootId, sharedId, anchor, branches);
        return true;
    }

    private static bool ValidateHints(
        BranchTopology topology,
        BranchedContinuousCompositionRequest request,
        ICollection<Diagnostic> diagnostics)
    {
        var branchById = topology.Branches.ToDictionary(item => item.CompiledRequirementId, StringComparer.Ordinal);
        var duplicateHints = request.BranchHints
            .GroupBy(item => item.CompiledRequirementId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        var invalid = duplicateHints.Count > 0 || request.BranchHints.Any(hint =>
            !branchById.TryGetValue(hint.CompiledRequirementId, out var requirement) ||
            !StringComparer.Ordinal.Equals(hint.SemanticOutputNodeId, requirement.ToSemanticNodeId) ||
            hint.PreferredSharedInputLayer >= request.MaxLayers);
        if (!invalid)
        {
            return true;
        }

        diagnostics.Add(new Diagnostic(
            DiagnosticCodes.BranchedCompositionHintMismatch,
            DiagnosticSeverity.Error,
            "Each typed branch hint must uniquely match one leaf requirement and stay within maxLayers.",
            duplicateHints.FirstOrDefault() ?? "branchHints"));
        return false;
    }

    private static bool TryOrderSelections(
        BranchTopology topology,
        IReadOnlyList<SelectedPlanEdgeMechanism> selectedEdges,
        Func<GenerationCandidate, string> candidateIdFactory,
        Func<ExactRatioMechanismCandidate, string> artifactHashFactory,
        ICollection<Diagnostic> diagnostics,
        out IReadOnlyList<EdgeContext>? contexts,
        out IReadOnlyList<CompositionInputEdgeProvenance> inputEdges,
        out BigInteger pitchScale)
    {
        contexts = null;
        inputEdges = Array.Empty<CompositionInputEdgeProvenance>();
        pitchScale = BigInteger.Zero;
        var requirements = topology.OrderedRequirements;
        var grouped = selectedEdges.GroupBy(item => item.CompiledRequirementId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        if (selectedEdges.Count != requirements.Count ||
            requirements.Any(requirement =>
                !grouped.TryGetValue(requirement.CompiledRequirementId, out var matches) || matches.Count != 1) ||
            grouped.Keys.Except(requirements.Select(item => item.CompiledRequirementId), StringComparer.Ordinal).Any())
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionEdgeMappingMismatch,
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
                    DiagnosticCodes.BranchedCompositionEdgeMappingMismatch,
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
            var fromNodeIndex = index == 0 ? 0 : 1;
            var toNodeIndex = index == 0 ? 1 : index + 1;
            if (!StringComparer.Ordinal.Equals(candidateId, selected.CandidateId) ||
                !StringComparer.Ordinal.Equals(artifactHash, selected.ArtifactHash) ||
                !edgeValidation.IsValid ||
                selected.Mechanism.KinematicSource.ExactTransfer != requirement.SignedTargetTransfer ||
                !TryBuildEdgeContext(
                    index,
                    fromNodeIndex,
                    toNodeIndex,
                    requirement,
                    selected,
                    out var context))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.BranchedCompositionEdgeCandidateMismatch,
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

        if (built.Any(item => item.PitchScale.Sign <= 0 || item.PitchScale != built[0].PitchScale))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionPitchScaleMismatch,
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
        int fromNodeIndex,
        int toNodeIndex,
        CompiledContinuousTransferRequirement requirement,
        SelectedPlanEdgeMechanism selected,
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
                ? NodeDofId(fromNodeIndex)
                : index == pathDofs.Count - 1
                    ? NodeDofId(toNodeIndex)
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
                    dofs.Add(pair.Value, new RotationalDof(
                        pair.Value,
                        StringComparer.Ordinal.Equals(pair.Value, NodeDofId(0))));
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

    private static IReadOnlyList<SemanticDofBinding> BuildSemanticBindings(BranchTopology topology)
    {
        var result = new List<SemanticDofBinding>
        {
            new(topology.RootNodeId, NodeDofId(0), SemanticBindingRole.Root),
            new(topology.SharedNodeId, NodeDofId(1), SemanticBindingRole.SharedBranchOutput),
        };
        for (var index = 0; index < topology.Branches.Count; index++)
        {
            result.Add(new SemanticDofBinding(
                topology.Branches[index].ToSemanticNodeId,
                NodeDofId(index + 2),
                SemanticBindingRole.LeafOutput));
        }

        return result;
    }

    private static bool ValidateComposedKinematics(
        CompiledMechanicalRequirementPlan plan,
        BranchTopology topology,
        KinematicSolveResult solved,
        IReadOnlyList<SemanticDofBinding> bindings,
        IReadOnlyList<CompositionInputEdgeProvenance> inputEdges,
        ICollection<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in solved.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        if (!solved.IsValid || solved.Solution is null)
        {
            return false;
        }

        var bindingBySemantic = bindings.ToDictionary(item => item.SemanticNodeId, StringComparer.Ordinal);
        var states = solved.Solution.States.ToDictionary(item => item.DofId, StringComparer.Ordinal);
        var valid = states[NodeDofId(0)].Coefficient == Rational.One &&
            states[NodeDofId(0)].PhaseOffset == Rational.Zero;
        foreach (var requirement in topology.OrderedRequirements)
        {
            var fromState = states[bindingBySemantic[requirement.FromSemanticNodeId].DofId];
            var toState = states[bindingBySemantic[requirement.ToSemanticNodeId].DofId];
            valid &= toState.Coefficient / fromState.Coefficient == requirement.SignedTargetTransfer &&
                toState.PhaseOffset == requirement.ExactPhaseRelation;
        }

        foreach (var relation in plan.DerivedRelations)
        {
            if (!bindingBySemantic.TryGetValue(relation.FromSemanticNodeId, out var fromBinding) ||
                !bindingBySemantic.TryGetValue(relation.ToSemanticNodeId, out var toBinding))
            {
                continue;
            }

            var fromState = states[fromBinding.DofId];
            var toState = states[toBinding.DofId];
            valid &= toState.Coefficient / fromState.Coefficient == relation.SignedTransfer &&
                toState.PhaseOffset == relation.ExactPhaseRelation;
        }

        if (!valid)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.BranchedCompositionEdgeCandidateMismatch,
                DiagnosticSeverity.Error,
                "Merged Kinematic propagation does not preserve every compiled edge and derived exact relation.",
                topology.SharedNodeId));
        }

        foreach (var diagnostic in BranchedContinuousCompositionValidation.ValidateSemanticBindings(
            bindings,
            inputEdges,
            topology.Anchor.CompiledRequirementId,
            solved.Solution).Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        return valid && !diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
    }

    private static List<RotationTuple> EnumerateRotationTuples()
    {
        var result = new List<RotationTuple>();
        foreach (var first in RotationOrder)
        {
            foreach (var second in RotationOrder)
            {
                result.Add(new RotationTuple(new[] { first, second }));
            }
        }

        return result;
    }

    private static List<LayerMappingTuple> EnumerateLayerMappingTuples(
        EdgeContext anchor,
        IReadOnlyList<EdgeContext> branches,
        int maxLayers)
    {
        if (branches.Count != 2 ||
            anchor.Selected.Mechanism.Candidate.Spatial.Bodies.Any(body => body.Layer < 0 || body.Layer >= maxLayers))
        {
            return new List<LayerMappingTuple>();
        }

        var anchorSharedLayers = SharedLocalLayers(anchor, output: true)
            .Select(local => local)
            .ToHashSet();
        var firstMappings = EnumerateInjectiveMappings(branches[0], maxLayers);
        var secondMappings = EnumerateInjectiveMappings(branches[1], maxLayers);
        var firstSharedLocal = SharedLocalLayers(branches[0], output: false).Single();
        var secondSharedLocal = SharedLocalLayers(branches[1], output: false).Single();
        var result = new List<LayerMappingTuple>();
        foreach (var first in firstMappings)
        {
            var firstShared = first[firstSharedLocal];
            if (anchorSharedLayers.Contains(firstShared)) continue;
            foreach (var second in secondMappings)
            {
                var secondShared = second[secondSharedLocal];
                if (anchorSharedLayers.Contains(secondShared) || secondShared == firstShared) continue;
                result.Add(new LayerMappingTuple(new[] { first, second }));
            }
        }

        return result;
    }

    private static List<IReadOnlyDictionary<int, int>> EnumerateInjectiveMappings(
        EdgeContext context,
        int maxLayers)
    {
        var localLayers = context.Selected.Mechanism.Candidate.Spatial.Bodies
            .Select(item => item.Layer)
            .Distinct()
            .OrderBy(item => item)
            .ToList();
        var result = new List<IReadOnlyDictionary<int, int>>();
        Enumerate(0, new Dictionary<int, int>(), new HashSet<int>());
        return result;

        void Enumerate(int index, IDictionary<int, int> current, ISet<int> used)
        {
            if (index == localLayers.Count)
            {
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

    private static IReadOnlyList<int> SharedLocalLayers(EdgeContext context, bool output)
    {
        var sharedDof = output
            ? context.PathDofs[context.PathDofs.Count - 1]
            : context.PathDofs[0];
        return context.Selected.Mechanism.Candidate.Spatial.Bodies
            .Where(item => StringComparer.Ordinal.Equals(item.DofId, sharedDof))
            .Select(item => item.Layer)
            .Distinct()
            .OrderBy(item => item)
            .ToList();
    }

    private static BuiltSpatial BuildSpatial(
        EdgeContext anchor,
        IReadOnlyList<EdgeContext> branches,
        RotationTuple rotations,
        LayerMappingTuple layerTuple)
    {
        var axes = new Dictionary<string, SpatialAxis>(StringComparer.Ordinal);
        foreach (var axis in anchor.Selected.Mechanism.Candidate.Spatial.Axes)
        {
            axes.Add(anchor.AxisMap[axis.Id], new SpatialAxis(anchor.AxisMap[axis.Id], axis.X, axis.Y));
        }

        var sharedAnchorLocalAxis = anchor.Selected.Mechanism.Candidate.Spatial.Bodies
            .Single(item => StringComparer.Ordinal.Equals(
                item.DofId,
                anchor.PathDofs[anchor.PathDofs.Count - 1])).AxisId;
        var anchorPoint = anchor.Selected.Mechanism.Candidate.Spatial.Axes
            .Single(item => item.Id == sharedAnchorLocalAxis);
        for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            var branch = branches[branchIndex];
            var sharedBranchLocalAxis = branch.Selected.Mechanism.Candidate.Spatial.Bodies
                .Single(item => StringComparer.Ordinal.Equals(item.DofId, branch.PathDofs[0])).AxisId;
            var branchPivot = branch.Selected.Mechanism.Candidate.Spatial.Axes
                .Single(item => item.Id == sharedBranchLocalAxis);
            foreach (var axis in branch.Selected.Mechanism.Candidate.Spatial.Axes)
            {
                var composedId = branch.AxisMap[axis.Id];
                var rotated = Rotate(axis.X - branchPivot.X, axis.Y - branchPivot.Y, rotations.Values[branchIndex]);
                var x = anchorPoint.X + rotated.X;
                var y = anchorPoint.Y + rotated.Y;
                if (axes.TryGetValue(composedId, out var existing))
                {
                    if (existing.X != x || existing.Y != y)
                    {
                        throw new InvalidOperationException("Shared composed branch axis alignment was not exact.");
                    }

                    continue;
                }

                axes.Add(composedId, new SpatialAxis(composedId, x, y));
            }
        }

        var unnormalizedBodies = new List<SpatialBody>();
        unnormalizedBodies.AddRange(anchor.Selected.Mechanism.Candidate.Spatial.Bodies.Select(body => new SpatialBody(
            anchor.BodyMap[body.Id],
            body.Kind,
            anchor.AxisMap[body.AxisId],
            anchor.DofMap[body.DofId],
            body.Layer,
            body.ToothCount,
            body.PitchRadius,
            body.ExactMountingPhase)));
        for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            var branch = branches[branchIndex];
            var layerMap = layerTuple.Values[branchIndex];
            unnormalizedBodies.AddRange(branch.Selected.Mechanism.Candidate.Spatial.Bodies.Select(body => new SpatialBody(
                branch.BodyMap[body.Id],
                body.Kind,
                branch.AxisMap[body.AxisId],
                branch.DofMap[body.DofId],
                layerMap[body.Layer],
                body.ToothCount,
                body.PitchRadius,
                body.ExactMountingPhase)));
        }

        var usedLayers = unnormalizedBodies.Select(item => item.Layer).Distinct().OrderBy(item => item).ToList();
        var normalization = usedLayers.Select((layer, index) => new { layer, index })
            .ToDictionary(item => item.layer, item => item.index);
        var bodies = unnormalizedBodies.Select(body => new SpatialBody(
            body.Id,
            body.Kind,
            body.AxisId,
            body.DofId,
            normalization[body.Layer],
            body.ToothCount,
            body.PitchRadius,
            body.ExactMountingPhase)).ToList();
        var contacts = new List<SpatialContact>();
        contacts.AddRange(anchor.Selected.Mechanism.Candidate.Spatial.Contacts.Select(contact => new SpatialContact(
            anchor.ContactMap[contact.Id],
            contact.Kind,
            anchor.CouplingMap[contact.ConstraintId],
            anchor.BodyMap[contact.BodyAId],
            anchor.BodyMap[contact.BodyBId])));
        foreach (var branch in branches)
        {
            contacts.AddRange(branch.Selected.Mechanism.Candidate.Spatial.Contacts.Select(contact => new SpatialContact(
                branch.ContactMap[contact.Id],
                contact.Kind,
                branch.CouplingMap[contact.ConstraintId],
                branch.BodyMap[contact.BodyAId],
                branch.BodyMap[contact.BodyBId])));
        }

        var spatial = new SpatialMechanism(axes.Values, bodies, contacts);
        var placements = new List<BranchedBranchPlacement>();
        for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            var branch = branches[branchIndex];
            var normalizedMap = layerTuple.Values[branchIndex]
                .OrderBy(item => item.Key)
                .Select(item => new CompositionLayerMapping(item.Key, normalization[item.Value]))
                .ToList();
            var sharedLocalLayer = SharedLocalLayers(branch, output: false).Single();
            var outputAxis = axes[branch.AxisMap[branch.Selected.Mechanism.Candidate.Spatial.Bodies
                .Single(item => StringComparer.Ordinal.Equals(
                    item.DofId,
                    branch.PathDofs[branch.PathDofs.Count - 1])).AxisId]];
            placements.Add(new BranchedBranchPlacement(
                branch.Requirement.CompiledRequirementId,
                branch.Requirement.ToSemanticNodeId,
                rotations.Values[branchIndex],
                DirectionFromRotation(rotations.Values[branchIndex]),
                normalization[layerTuple.Values[branchIndex][sharedLocalLayer]],
                new BranchOutputAnchor(outputAxis.X, outputAxis.Y),
                normalizedMap));
        }

        return new BuiltSpatial(spatial, placements);
    }

    private static HintMetrics EvaluateHints(
        IEnumerable<BranchPlacementHint> hints,
        IReadOnlyList<BranchedBranchPlacement> placements)
    {
        var byRequirement = placements.ToDictionary(item => item.CompiledRequirementId, StringComparer.Ordinal);
        var requiredSatisfied = true;
        var requiredCount = 0;
        var directionMisses = 0;
        var layerMisses = 0;
        var anchorPenalty = BigInteger.Zero;
        foreach (var hint in hints)
        {
            var placement = byRequirement[hint.CompiledRequirementId];
            var directionMatches = !hint.PreferredDirection.HasValue ||
                hint.PreferredDirection.Value == placement.ActualDirection;
            var layerMatches = !hint.PreferredSharedInputLayer.HasValue ||
                hint.PreferredSharedInputLayer.Value == placement.SharedInputGlobalLayer;
            var anchorMatches = hint.PreferredOutputAnchor is null ||
                (hint.PreferredOutputAnchor.X == placement.OutputAnchor.X &&
                 hint.PreferredOutputAnchor.Y == placement.OutputAnchor.Y);
            if (hint.Strength == BranchHintStrength.Required)
            {
                if (directionMatches && layerMatches && anchorMatches)
                {
                    requiredCount++;
                }
                else
                {
                    requiredSatisfied = false;
                }
            }
            else
            {
                if (!directionMatches) directionMisses++;
                if (!layerMatches) layerMisses++;
                if (hint.PreferredOutputAnchor is not null)
                {
                    anchorPenalty += BigInteger.Abs(
                        placement.OutputAnchor.X - hint.PreferredOutputAnchor.X);
                    anchorPenalty += BigInteger.Abs(
                        placement.OutputAnchor.Y - hint.PreferredOutputAnchor.Y);
                }
            }
        }

        return new HintMetrics(
            requiredSatisfied,
            requiredCount,
            directionMisses,
            layerMisses,
            anchorPenalty);
    }

    private static (BigInteger X, BigInteger Y) Rotate(
        BigInteger x,
        BigInteger y,
        CardinalRotation rotation) =>
        rotation switch
        {
            CardinalRotation.Degrees0 => (x, y),
            CardinalRotation.Degrees90 => (BigInteger.Negate(y), x),
            CardinalRotation.Degrees180 => (BigInteger.Negate(x), BigInteger.Negate(y)),
            CardinalRotation.Degrees270 => (y, BigInteger.Negate(x)),
            _ => throw new InvalidOperationException("Unsupported cardinal rotation '" + rotation + "'."),
        };

    private static CardinalDirection DirectionFromRotation(CardinalRotation rotation) =>
        rotation switch
        {
            CardinalRotation.Degrees0 => CardinalDirection.East,
            CardinalRotation.Degrees90 => CardinalDirection.North,
            CardinalRotation.Degrees180 => CardinalDirection.West,
            CardinalRotation.Degrees270 => CardinalDirection.South,
            _ => throw new InvalidOperationException("Unsupported cardinal rotation '" + rotation + "'."),
        };

    private static ContinuousPlanCompositionMetrics BuildMechanicalMetrics(
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

    private static BranchedContinuousCompositionResult Terminal(
        CompiledMechanicalRequirementPlan plan,
        BranchedContinuousCompositionRequest request,
        BranchedContinuousCompositionFingerprint fingerprint,
        IReadOnlyList<CompositionInputEdgeProvenance> inputEdges,
        BranchedContinuousCompositionStatus status,
        IEnumerable<Diagnostic> diagnostics)
    {
        var canonical = BranchedContinuousCompositionIdentity.BuildRequestCanonicalRepresentation(
            plan.CompiledPlanId,
            inputEdges,
            request);
        return new BranchedContinuousCompositionResult(
            plan.CompiledPlanId,
            request,
            canonical,
            BranchedContinuousCompositionIdentity.ComputeRequestId(canonical),
            fingerprint,
            status,
            inputEdges,
            Array.Empty<BranchedContinuousCompositionCandidate>(),
            diagnostics,
            EmptySummary(searchComplete: false),
            new BranchedContinuousCompositionPerformance(0, 0, 0));
    }

    private static BranchedContinuousCompositionSearchSummary EmptySummary(bool searchComplete) =>
        new(2, 0, 0, 0, 0, 0, 0, 0, 0, false, searchComplete);

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

    private sealed class BranchTopology
    {
        public BranchTopology(
            string rootNodeId,
            string sharedNodeId,
            CompiledContinuousTransferRequirement anchor,
            IEnumerable<CompiledContinuousTransferRequirement> branches)
        {
            RootNodeId = rootNodeId;
            SharedNodeId = sharedNodeId;
            Anchor = anchor;
            Branches = branches.ToList();
            OrderedRequirements = new[] { anchor }.Concat(Branches).ToList();
        }

        public string RootNodeId { get; }
        public string SharedNodeId { get; }
        public CompiledContinuousTransferRequirement Anchor { get; }
        public IReadOnlyList<CompiledContinuousTransferRequirement> Branches { get; }
        public IReadOnlyList<CompiledContinuousTransferRequirement> OrderedRequirements { get; }
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

    private sealed class RotationTuple
    {
        public RotationTuple(IEnumerable<CardinalRotation> values)
        {
            Values = values.ToList();
        }

        public IReadOnlyList<CardinalRotation> Values { get; }
    }

    private sealed class LayerMappingTuple
    {
        public LayerMappingTuple(IEnumerable<IReadOnlyDictionary<int, int>> values)
        {
            Values = values.ToList();
        }

        public IReadOnlyList<IReadOnlyDictionary<int, int>> Values { get; }
    }

    private sealed class BuiltSpatial
    {
        public BuiltSpatial(
            SpatialMechanism spatial,
            IReadOnlyList<BranchedBranchPlacement> branchPlacements)
        {
            Spatial = spatial;
            BranchPlacements = branchPlacements;
        }

        public SpatialMechanism Spatial { get; }
        public IReadOnlyList<BranchedBranchPlacement> BranchPlacements { get; }
    }

    private sealed class HintMetrics
    {
        public HintMetrics(
            bool requiredSatisfied,
            int requiredSatisfactionCount,
            int softDirectionMissCount,
            int softLayerMissCount,
            BigInteger outputAnchorPenalty)
        {
            RequiredSatisfied = requiredSatisfied;
            RequiredSatisfactionCount = requiredSatisfactionCount;
            SoftDirectionMissCount = softDirectionMissCount;
            SoftLayerMissCount = softLayerMissCount;
            OutputAnchorPenalty = outputAnchorPenalty;
        }

        public bool RequiredSatisfied { get; }
        public int RequiredSatisfactionCount { get; }
        public int SoftDirectionMissCount { get; }
        public int SoftLayerMissCount { get; }
        public BigInteger OutputAnchorPenalty { get; }
    }

    private sealed class CandidateDraft
    {
        public CandidateDraft(
            GenerationCandidate mechanical,
            string composedSpatialId,
            string candidateId,
            IReadOnlyList<BranchedBranchPlacement> branchPlacements,
            BranchedContinuousCompositionMetrics metrics)
        {
            Mechanical = mechanical;
            ComposedSpatialId = composedSpatialId;
            CandidateId = candidateId;
            BranchPlacements = branchPlacements;
            Metrics = metrics;
        }

        public GenerationCandidate Mechanical { get; }
        public string ComposedSpatialId { get; }
        public string CandidateId { get; }
        public IReadOnlyList<BranchedBranchPlacement> BranchPlacements { get; }
        public BranchedContinuousCompositionMetrics Metrics { get; }
    }

    private sealed class CandidateDraftComparer : IComparer<CandidateDraft>
    {
        public static readonly CandidateDraftComparer Instance = new();

        public int Compare(CandidateDraft? left, CandidateDraft? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var value = right.Metrics.RequiredHintSatisfactionCount.CompareTo(
                left.Metrics.RequiredHintSatisfactionCount);
            if (value != 0) return value;
            value = left.Metrics.SoftDirectionMissCount.CompareTo(right.Metrics.SoftDirectionMissCount);
            if (value != 0) return value;
            value = left.Metrics.SoftLayerMissCount.CompareTo(right.Metrics.SoftLayerMissCount);
            if (value != 0) return value;
            value = left.Metrics.OutputAnchorPenalty.CompareTo(right.Metrics.OutputAnchorPenalty);
            if (value != 0) return value;
            value = left.Metrics.Mechanical.UsedLayerCount.CompareTo(right.Metrics.Mechanical.UsedLayerCount);
            if (value != 0) return value;
            value = left.Metrics.Mechanical.BodyCount.CompareTo(right.Metrics.Mechanical.BodyCount);
            if (value != 0) return value;
            value = left.Metrics.Mechanical.BoundingArea.CompareTo(right.Metrics.Mechanical.BoundingArea);
            if (value != 0) return value;
            value = left.Metrics.Mechanical.MaximumExtent.CompareTo(right.Metrics.Mechanical.MaximumExtent);
            if (value != 0) return value;
            value = StringComparer.Ordinal.Compare(RotationSignature(left), RotationSignature(right));
            if (value != 0) return value;
            value = StringComparer.Ordinal.Compare(LayerSignature(left), LayerSignature(right));
            return value != 0
                ? value
                : StringComparer.Ordinal.Compare(left.ComposedSpatialId, right.ComposedSpatialId);
        }

        private static string RotationSignature(CandidateDraft draft) =>
            string.Join(",", draft.BranchPlacements.Select(item =>
                ((int)item.Rotation).ToString("D3", CultureInfo.InvariantCulture)));

        private static string LayerSignature(CandidateDraft draft) =>
            string.Join("|", draft.BranchPlacements.Select(item =>
                item.CompiledRequirementId + ":" + string.Join(",", item.LayerMapping.Select(mapping =>
                    mapping.LocalLayer.ToString(CultureInfo.InvariantCulture) + ":" +
                    mapping.GlobalLayer.ToString(CultureInfo.InvariantCulture)))));
    }
}
