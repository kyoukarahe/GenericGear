using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class BranchedContinuousCompositionContract
{
    public const string TopologyKind = "threeEdgeSingleSharedNodeBranch";
    public const string BackendId = "builtin-three-edge-branch-composition";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-three-edge-branch-composition-v1";
    public const string DefaultDeterminismProfile = "portable-managed-branched-composition-v1";
    public const string ResultFormat = "gear-invest.branched-continuous-composition-result";
    public const string ResultFormatVersion = "0.1";
}

public enum BranchedContinuousCompositionStatus
{
    Complete,
    IncompleteBudget,
    Infeasible,
    InvalidInput,
    Unsupported,
    Cancelled,
}

public enum BranchHintStrength
{
    Preferred,
    Required,
}

public sealed class BranchOutputAnchor
{
    public BranchOutputAnchor(BigInteger x, BigInteger y)
    {
        X = x;
        Y = y;
    }

    public BigInteger X { get; }
    public BigInteger Y { get; }
}

public sealed class BranchPlacementHint
{
    public BranchPlacementHint(
        string compiledRequirementId,
        string semanticOutputNodeId,
        CardinalDirection? preferredDirection,
        int? preferredSharedInputLayer,
        BranchOutputAnchor? preferredOutputAnchor,
        BranchHintStrength strength)
    {
        CompiledRequirementId = compiledRequirementId ?? string.Empty;
        SemanticOutputNodeId = semanticOutputNodeId ?? string.Empty;
        PreferredDirection = preferredDirection;
        PreferredSharedInputLayer = preferredSharedInputLayer;
        PreferredOutputAnchor = preferredOutputAnchor;
        Strength = strength;
    }

    public string CompiledRequirementId { get; }
    public string SemanticOutputNodeId { get; }
    public CardinalDirection? PreferredDirection { get; }
    public int? PreferredSharedInputLayer { get; }
    public BranchOutputAnchor? PreferredOutputAnchor { get; }
    public BranchHintStrength Strength { get; }
}

public sealed class BranchedContinuousCompositionRequest
{
    public BranchedContinuousCompositionRequest(
        string anchorRequirementId,
        IEnumerable<BranchPlacementHint> branchHints,
        int maxLayers,
        BigInteger clearanceTicks,
        long maxCompositionExpansions,
        int maxReturnedCompositions,
        string determinismProfile = BranchedContinuousCompositionContract.DefaultDeterminismProfile)
    {
        AnchorRequirementId = anchorRequirementId ?? string.Empty;
        BranchHints = (branchHints ?? throw new ArgumentNullException(nameof(branchHints)))
            .OrderBy(item => item.CompiledRequirementId, StringComparer.Ordinal)
            .ThenBy(item => item.SemanticOutputNodeId, StringComparer.Ordinal)
            .ThenBy(item => item.Strength)
            .ThenBy(item => item.PreferredDirection)
            .ThenBy(item => item.PreferredSharedInputLayer)
            .ThenBy(item => item.PreferredOutputAnchor?.X)
            .ThenBy(item => item.PreferredOutputAnchor?.Y)
            .ToList()
            .AsReadOnly();
        MaxLayers = maxLayers;
        ClearanceTicks = clearanceTicks;
        MaxCompositionExpansions = maxCompositionExpansions;
        MaxReturnedCompositions = maxReturnedCompositions;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public string AnchorRequirementId { get; }
    public ReadOnlyCollection<BranchPlacementHint> BranchHints { get; }
    public int MaxLayers { get; }
    public BigInteger ClearanceTicks { get; }
    public long MaxCompositionExpansions { get; }
    public int MaxReturnedCompositions { get; }
    public string DeterminismProfile { get; }
}

public sealed class BranchedContinuousCompositionFingerprint
{
    public BranchedContinuousCompositionFingerprint(
        string backendId,
        string backendVersion,
        string determinismProfile)
    {
        BackendId = backendId ?? throw new ArgumentNullException(nameof(backendId));
        BackendVersion = backendVersion ?? throw new ArgumentNullException(nameof(backendVersion));
        DeterminismProfile = determinismProfile ?? throw new ArgumentNullException(nameof(determinismProfile));
    }

    public string BackendId { get; }
    public string BackendVersion { get; }
    public string DeterminismProfile { get; }
    public string SemanticVersion => BackendId + "-v" + BackendVersion;
}

public sealed class BranchedBranchPlacement
{
    public BranchedBranchPlacement(
        string compiledRequirementId,
        string semanticOutputNodeId,
        CardinalRotation rotation,
        CardinalDirection actualDirection,
        int sharedInputGlobalLayer,
        BranchOutputAnchor outputAnchor,
        IEnumerable<CompositionLayerMapping> layerMapping)
    {
        CompiledRequirementId = compiledRequirementId ?? throw new ArgumentNullException(nameof(compiledRequirementId));
        SemanticOutputNodeId = semanticOutputNodeId ?? throw new ArgumentNullException(nameof(semanticOutputNodeId));
        Rotation = rotation;
        ActualDirection = actualDirection;
        SharedInputGlobalLayer = sharedInputGlobalLayer;
        OutputAnchor = outputAnchor ?? throw new ArgumentNullException(nameof(outputAnchor));
        LayerMapping = (layerMapping ?? throw new ArgumentNullException(nameof(layerMapping)))
            .OrderBy(item => item.LocalLayer)
            .ThenBy(item => item.GlobalLayer)
            .ToList()
            .AsReadOnly();
    }

    public string CompiledRequirementId { get; }
    public string SemanticOutputNodeId { get; }
    public CardinalRotation Rotation { get; }
    public CardinalDirection ActualDirection { get; }
    public int SharedInputGlobalLayer { get; }
    public BranchOutputAnchor OutputAnchor { get; }
    public ReadOnlyCollection<CompositionLayerMapping> LayerMapping { get; }
}

public sealed class BranchedContinuousCompositionSearchSummary
{
    public BranchedContinuousCompositionSearchSummary(
        int branchCount,
        long rotationTuples,
        long layerMappingTuples,
        long compositionExpansions,
        long rawFeasibleCandidates,
        long hintFilteredCandidates,
        long hintRankedCandidates,
        long deduplicatedCandidates,
        int returnedCandidates,
        bool resultTruncated,
        bool searchComplete)
    {
        BranchCount = branchCount;
        RotationTuples = rotationTuples;
        LayerMappingTuples = layerMappingTuples;
        CompositionExpansions = compositionExpansions;
        RawFeasibleCandidates = rawFeasibleCandidates;
        HintFilteredCandidates = hintFilteredCandidates;
        HintRankedCandidates = hintRankedCandidates;
        DeduplicatedCandidates = deduplicatedCandidates;
        ReturnedCandidates = returnedCandidates;
        ResultTruncated = resultTruncated;
        SearchComplete = searchComplete;
    }

    public int BranchCount { get; }
    public long RotationTuples { get; }
    public long LayerMappingTuples { get; }
    public long CompositionExpansions { get; }
    public long RawFeasibleCandidates { get; }
    public long HintFilteredCandidates { get; }
    public long HintRankedCandidates { get; }
    public long DeduplicatedCandidates { get; }
    public int ReturnedCandidates { get; }
    public bool ResultTruncated { get; }
    public bool SearchComplete { get; }
}

public sealed class BranchedContinuousCompositionPerformance
{
    public BranchedContinuousCompositionPerformance(
        long kinematicMergeMicroseconds,
        long spatialCompositionMicroseconds,
        long validationMicroseconds)
    {
        KinematicMergeMicroseconds = kinematicMergeMicroseconds;
        SpatialCompositionMicroseconds = spatialCompositionMicroseconds;
        ValidationMicroseconds = validationMicroseconds;
    }

    public long KinematicMergeMicroseconds { get; }
    public long SpatialCompositionMicroseconds { get; }
    public long ValidationMicroseconds { get; }
}

public sealed class BranchedContinuousCompositionMetrics
{
    public BranchedContinuousCompositionMetrics(
        int requiredHintSatisfactionCount,
        int softDirectionMissCount,
        int softLayerMissCount,
        BigInteger outputAnchorPenalty,
        ContinuousPlanCompositionMetrics mechanical)
    {
        RequiredHintSatisfactionCount = requiredHintSatisfactionCount;
        SoftDirectionMissCount = softDirectionMissCount;
        SoftLayerMissCount = softLayerMissCount;
        OutputAnchorPenalty = outputAnchorPenalty;
        Mechanical = mechanical ?? throw new ArgumentNullException(nameof(mechanical));
    }

    public int RequiredHintSatisfactionCount { get; }
    public int SoftDirectionMissCount { get; }
    public int SoftLayerMissCount { get; }
    public BigInteger OutputAnchorPenalty { get; }
    public ContinuousPlanCompositionMetrics Mechanical { get; }
}

public sealed class BranchedContinuousCompositionProvenance
{
    public BranchedContinuousCompositionProvenance(
        string topologyKind,
        string compiledPlanId,
        BranchedContinuousCompositionRequest request,
        string requestCanonicalRepresentation,
        string branchedCompositionRequestId,
        BranchedContinuousCompositionFingerprint backendFingerprint,
        BigInteger pitchRadiusTicksPerTooth,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges,
        IEnumerable<BranchedBranchPlacement> selectedBranches,
        BranchedContinuousCompositionSearchSummary searchSummary)
    {
        TopologyKind = topologyKind ?? throw new ArgumentNullException(nameof(topologyKind));
        CompiledPlanId = compiledPlanId ?? throw new ArgumentNullException(nameof(compiledPlanId));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        BranchedCompositionRequestId = branchedCompositionRequestId ?? throw new ArgumentNullException(nameof(branchedCompositionRequestId));
        BackendFingerprint = backendFingerprint ?? throw new ArgumentNullException(nameof(backendFingerprint));
        PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        InputEdges = (inputEdges ?? throw new ArgumentNullException(nameof(inputEdges)))
            .OrderBy(item => item.Index)
            .ToList()
            .AsReadOnly();
        SelectedBranches = (selectedBranches ?? throw new ArgumentNullException(nameof(selectedBranches)))
            .OrderBy(item => InputIndex(InputEdges, item.CompiledRequirementId))
            .ToList()
            .AsReadOnly();
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
    }

    public string TopologyKind { get; }
    public string CompiledPlanId { get; }
    public BranchedContinuousCompositionRequest Request { get; }
    public string RequestCanonicalRepresentation { get; }
    public string BranchedCompositionRequestId { get; }
    public BranchedContinuousCompositionFingerprint BackendFingerprint { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<CompositionInputEdgeProvenance> InputEdges { get; }
    public ReadOnlyCollection<BranchedBranchPlacement> SelectedBranches { get; }
    public BranchedContinuousCompositionSearchSummary SearchSummary { get; }

    private static int InputIndex(
        IEnumerable<CompositionInputEdgeProvenance> edges,
        string requirementId)
    {
        var edge = edges.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.CompiledRequirementId, requirementId));
        return edge?.Index ?? int.MaxValue;
    }
}

public sealed class BranchedContinuousCompositionCandidate
{
    public BranchedContinuousCompositionCandidate(
        GenerationCandidate candidate,
        string branchedComposedKinematicCandidateId,
        string branchedComposedSpatialCandidateId,
        string candidateId,
        IEnumerable<SemanticDofBinding> semanticBindings,
        BranchedContinuousCompositionProvenance provenance,
        BranchedContinuousCompositionMetrics metrics)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        BranchedComposedKinematicCandidateId = branchedComposedKinematicCandidateId ?? throw new ArgumentNullException(nameof(branchedComposedKinematicCandidateId));
        BranchedComposedSpatialCandidateId = branchedComposedSpatialCandidateId ?? throw new ArgumentNullException(nameof(branchedComposedSpatialCandidateId));
        CandidateId = candidateId ?? throw new ArgumentNullException(nameof(candidateId));
        SemanticBindings = (semanticBindings ?? throw new ArgumentNullException(nameof(semanticBindings)))
            .OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public GenerationCandidate Candidate { get; }
    public string BranchedComposedKinematicCandidateId { get; }
    public string BranchedComposedSpatialCandidateId { get; }
    public string CandidateId { get; }
    public ReadOnlyCollection<SemanticDofBinding> SemanticBindings { get; }
    public BranchedContinuousCompositionProvenance Provenance { get; }
    public BranchedContinuousCompositionMetrics Metrics { get; }
}

public sealed class BranchedContinuousCompositionResult
{
    public BranchedContinuousCompositionResult(
        string compiledPlanId,
        BranchedContinuousCompositionRequest normalizedRequest,
        string requestCanonicalRepresentation,
        string branchedCompositionRequestId,
        BranchedContinuousCompositionFingerprint backendFingerprint,
        BranchedContinuousCompositionStatus status,
        IEnumerable<CompositionInputEdgeProvenance> orderedInputEdges,
        IEnumerable<BranchedContinuousCompositionCandidate> candidates,
        IEnumerable<Diagnostic> diagnostics,
        BranchedContinuousCompositionSearchSummary searchSummary,
        BranchedContinuousCompositionPerformance performance)
    {
        CompiledPlanId = compiledPlanId ?? throw new ArgumentNullException(nameof(compiledPlanId));
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        BranchedCompositionRequestId = branchedCompositionRequestId ?? throw new ArgumentNullException(nameof(branchedCompositionRequestId));
        BackendFingerprint = backendFingerprint ?? throw new ArgumentNullException(nameof(backendFingerprint));
        Status = status;
        OrderedInputEdges = (orderedInputEdges ?? throw new ArgumentNullException(nameof(orderedInputEdges)))
            .OrderBy(item => item.Index)
            .ToList()
            .AsReadOnly();
        Candidates = (candidates ?? throw new ArgumentNullException(nameof(candidates))).ToList().AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
        Performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    public string CompiledPlanId { get; }
    public BranchedContinuousCompositionRequest NormalizedRequest { get; }
    public string RequestCanonicalRepresentation { get; }
    public string BranchedCompositionRequestId { get; }
    public BranchedContinuousCompositionFingerprint BackendFingerprint { get; }
    public BranchedContinuousCompositionStatus Status { get; }
    public ReadOnlyCollection<CompositionInputEdgeProvenance> OrderedInputEdges { get; }
    public ReadOnlyCollection<BranchedContinuousCompositionCandidate> Candidates { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public BranchedContinuousCompositionSearchSummary SearchSummary { get; }
    public BranchedContinuousCompositionPerformance Performance { get; }
    public bool IsSuccess => Status == BranchedContinuousCompositionStatus.Complete && Candidates.Count > 0 &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public static class BranchedContinuousCompositionIdentity
{
    public static string BuildRequestCanonicalRepresentation(
        string compiledPlanId,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges,
        BranchedContinuousCompositionRequest request)
    {
        if (compiledPlanId is null) throw new ArgumentNullException(nameof(compiledPlanId));
        if (inputEdges is null) throw new ArgumentNullException(nameof(inputEdges));
        if (request is null) throw new ArgumentNullException(nameof(request));
        var builder = new StringBuilder(BranchedContinuousCompositionContract.BackendSemanticVersion);
        builder.Append("|topologyKind=").Append(LengthPrefixed(BranchedContinuousCompositionContract.TopologyKind));
        builder.Append("|profile=").Append(LengthPrefixed(request.DeterminismProfile));
        builder.Append("|compiledPlanId=").Append(LengthPrefixed(compiledPlanId));
        builder.Append("|anchorRequirementId=").Append(LengthPrefixed(request.AnchorRequirementId));
        foreach (var edge in inputEdges.OrderBy(item => item.Index))
        {
            builder.Append("|edge=").Append(edge.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(LengthPrefixed(edge.CompiledRequirementId));
            builder.Append(',').Append(LengthPrefixed(edge.CandidateId));
            builder.Append(',').Append(LengthPrefixed(edge.ArtifactHash));
        }

        foreach (var hint in request.BranchHints)
        {
            builder.Append("|hint=").Append(LengthPrefixed(hint.CompiledRequirementId));
            builder.Append(',').Append(LengthPrefixed(hint.SemanticOutputNodeId));
            builder.Append(',').Append(hint.Strength.ToString());
            builder.Append(',').Append(hint.PreferredDirection?.ToString() ?? "none");
            builder.Append(',').Append(hint.PreferredSharedInputLayer?.ToString(CultureInfo.InvariantCulture) ?? "none");
            builder.Append(',').Append(hint.PreferredOutputAnchor?.X.ToString(CultureInfo.InvariantCulture) ?? "none");
            builder.Append(',').Append(hint.PreferredOutputAnchor?.Y.ToString(CultureInfo.InvariantCulture) ?? "none");
        }

        builder.Append("|maxLayers=").Append(request.MaxLayers.ToString(CultureInfo.InvariantCulture));
        builder.Append("|clearanceTicks=").Append(request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        builder.Append("|maxCompositionExpansions=").Append(request.MaxCompositionExpansions.ToString(CultureInfo.InvariantCulture));
        builder.Append("|maxReturnedCompositions=").Append(request.MaxReturnedCompositions.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    public static string ComputeRequestId(string canonicalRepresentation) =>
        PeriodicSemanticIdentity.Hash("branched-composition-request-sha256:", canonicalRepresentation);

    public static string ComputeBranchedComposedKinematicCandidateId(
        KinematicSpecification kinematic,
        BranchedContinuousCompositionFingerprint fingerprint)
    {
        if (kinematic is null) throw new ArgumentNullException(nameof(kinematic));
        if (fingerprint is null) throw new ArgumentNullException(nameof(fingerprint));
        var builder = new StringBuilder(fingerprint.SemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(fingerprint.DeterminismProfile));
        builder.Append("|root=").Append(LengthPrefixed(kinematic.RootDofId));
        foreach (var dof in kinematic.Dofs.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|dof=").Append(LengthPrefixed(dof.Id));
            builder.Append(',').Append(dof.IsPrescribed ? '1' : '0');
        }

        foreach (var coupling in kinematic.Couplings.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|coupling=").Append(LengthPrefixed(coupling.Id));
            builder.Append(',').Append(LengthPrefixed(coupling.DriverDofId));
            builder.Append(',').Append(LengthPrefixed(coupling.DrivenDofId));
            builder.Append(',').Append(coupling.DriverTeeth.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(coupling.DrivenTeeth.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(coupling.PhaseOffset.ToString());
        }

        return PeriodicSemanticIdentity.Hash("branched-composed-kinematic-sha256:", builder.ToString());
    }

    public static string ComputeBranchedComposedSpatialCandidateId(
        string branchedComposedKinematicCandidateId,
        SpatialMechanism spatial,
        BigInteger clearanceTicks,
        BranchedContinuousCompositionFingerprint fingerprint)
    {
        if (branchedComposedKinematicCandidateId is null) throw new ArgumentNullException(nameof(branchedComposedKinematicCandidateId));
        if (spatial is null) throw new ArgumentNullException(nameof(spatial));
        if (fingerprint is null) throw new ArgumentNullException(nameof(fingerprint));
        var builder = new StringBuilder(fingerprint.SemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(fingerprint.DeterminismProfile));
        builder.Append("|kinematic=").Append(LengthPrefixed(branchedComposedKinematicCandidateId));
        builder.Append("|clearanceTicks=").Append(clearanceTicks.ToString(CultureInfo.InvariantCulture));
        foreach (var axis in spatial.Axes.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|axis=").Append(LengthPrefixed(axis.Id));
            builder.Append(',').Append(axis.X.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(axis.Y.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var body in spatial.Bodies.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|body=").Append(LengthPrefixed(body.Id));
            builder.Append(',').Append(LengthPrefixed(body.AxisId));
            builder.Append(',').Append(LengthPrefixed(body.DofId));
            builder.Append(',').Append(body.Layer.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(body.ToothCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(body.PitchRadius.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(body.ExactMountingPhase.ToString());
        }

        foreach (var contact in spatial.Contacts.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|contact=").Append(LengthPrefixed(contact.Id));
            builder.Append(',').Append(LengthPrefixed(contact.ConstraintId));
            builder.Append(',').Append(LengthPrefixed(contact.BodyAId));
            builder.Append(',').Append(LengthPrefixed(contact.BodyBId));
        }

        return PeriodicSemanticIdentity.Hash("branched-composed-spatial-sha256:", builder.ToString());
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}

public static class BranchedContinuousCompositionValidation
{
    public static ValidationReport ValidateSemanticBindings(
        IEnumerable<SemanticDofBinding> bindings,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges,
        string anchorRequirementId,
        KinematicSolution solution)
    {
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));
        if (inputEdges is null) throw new ArgumentNullException(nameof(inputEdges));
        if (anchorRequirementId is null) throw new ArgumentNullException(nameof(anchorRequirementId));
        if (solution is null) throw new ArgumentNullException(nameof(solution));
        var diagnostics = new List<Diagnostic>();
        var bindingList = bindings.ToList();
        var edges = inputEdges.OrderBy(item => item.Index).ToList();
        var states = solution.States.ToDictionary(item => item.DofId, StringComparer.Ordinal);
        var anchorMatches = edges.Where(item =>
            StringComparer.Ordinal.Equals(item.CompiledRequirementId, anchorRequirementId)).ToList();
        var anchor = anchorMatches.Count == 1 ? anchorMatches[0] : null;
        if (edges.Count != 3 || anchor is null || bindingList.Count != 4 ||
            bindingList.Select(item => item.SemanticNodeId).Distinct(StringComparer.Ordinal).Count() != bindingList.Count ||
            bindingList.Select(item => item.DofId).Distinct(StringComparer.Ordinal).Count() != bindingList.Count)
        {
            diagnostics.Add(Failure("semanticBindings"));
            return new ValidationReport(diagnostics);
        }

        var branches = edges.Where(item => !ReferenceEquals(item, anchor))
            .OrderBy(item => item.Index)
            .ToList();
        if (branches.Count != 2 || branches.Any(item =>
                !StringComparer.Ordinal.Equals(item.SemanticFromNodeId, anchor.SemanticToNodeId)))
        {
            diagnostics.Add(Failure("inputEdges"));
            return new ValidationReport(diagnostics);
        }

        var expected = new List<ExpectedBinding>
        {
            new(anchor.SemanticFromNodeId, SemanticBindingRole.Root, Rational.One, Rational.Zero),
            new(anchor.SemanticToNodeId, SemanticBindingRole.SharedBranchOutput,
                anchor.SignedTargetTransfer, anchor.ExactPhaseRelation),
        };
        expected.AddRange(branches.Select(item => new ExpectedBinding(
            item.SemanticToNodeId,
            SemanticBindingRole.LeafOutput,
            anchor.SignedTargetTransfer * item.SignedTargetTransfer,
            Rational.Zero)));
        foreach (var item in expected)
        {
            var matches = bindingList.Where(binding =>
                StringComparer.Ordinal.Equals(binding.SemanticNodeId, item.SemanticNodeId) && binding.Role == item.Role).ToList();
            if (matches.Count != 1 || !states.TryGetValue(matches[0].DofId, out var state) ||
                state.Coefficient != item.Coefficient || state.PhaseOffset != item.Phase)
            {
                diagnostics.Add(Failure(item.SemanticNodeId));
            }
        }

        var rootBindings = bindingList.Where(item => item.Role == SemanticBindingRole.Root).ToList();
        var rootBinding = rootBindings.Count == 1 ? rootBindings[0] : null;
        if (rootBinding is null || !StringComparer.Ordinal.Equals(rootBinding.DofId, solution.RootDofId))
        {
            diagnostics.Add(Failure("root"));
        }

        return new ValidationReport(diagnostics);
    }

    private static Diagnostic Failure(string subject) => new(
        DiagnosticCodes.BranchedCompositionSemanticBindingMismatch,
        DiagnosticSeverity.Error,
        "Branched composition semantic binding does not match the exact composed Kinematic solution.",
        subject);

    private sealed class ExpectedBinding
    {
        public ExpectedBinding(
            string semanticNodeId,
            SemanticBindingRole role,
            Rational coefficient,
            Rational phase)
        {
            SemanticNodeId = semanticNodeId;
            Role = role;
            Coefficient = coefficient;
            Phase = phase;
        }

        public string SemanticNodeId { get; }
        public SemanticBindingRole Role { get; }
        public Rational Coefficient { get; }
        public Rational Phase { get; }
    }
}
