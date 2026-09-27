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

public static class ContinuousPlanCompositionContract
{
    public const string BackendId = "builtin-linear-continuous-composition";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-linear-continuous-composition-v1";
    public const string DefaultDeterminismProfile = "portable-managed-composition-v1";
    public const string ResultFormat = "gear-invest.continuous-plan-composition-result";
    public const string ResultFormatVersion = "0.1";
}

public enum ContinuousPlanCompositionStatus
{
    Complete,
    IncompleteBudget,
    Infeasible,
    InvalidInput,
    Unsupported,
    Cancelled,
}

public enum CardinalRotation
{
    Degrees0 = 0,
    Degrees90 = 90,
    Degrees180 = 180,
    Degrees270 = 270,
}

public enum SemanticBindingRole
{
    Root,
    IntermediateOutput,
    FinalOutput,
    SharedBranchOutput,
    LeafOutput,
}

public sealed class ContinuousPlanCompositionRequest
{
    public ContinuousPlanCompositionRequest(
        int maxLayers,
        BigInteger clearanceTicks,
        long maxCompositionExpansions,
        int maxReturnedCompositions,
        string determinismProfile = ContinuousPlanCompositionContract.DefaultDeterminismProfile)
    {
        MaxLayers = maxLayers;
        ClearanceTicks = clearanceTicks;
        MaxCompositionExpansions = maxCompositionExpansions;
        MaxReturnedCompositions = maxReturnedCompositions;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public int MaxLayers { get; }
    public BigInteger ClearanceTicks { get; }
    public long MaxCompositionExpansions { get; }
    public int MaxReturnedCompositions { get; }
    public string DeterminismProfile { get; }

    public static ContinuousPlanCompositionRequest ReviewedDefault { get; } = new(
        maxLayers: 3,
        clearanceTicks: BigInteger.Zero,
        maxCompositionExpansions: 100,
        maxReturnedCompositions: 5);
}

public sealed class ContinuousPlanCompositionFingerprint
{
    public ContinuousPlanCompositionFingerprint(
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

public sealed class SelectedPlanEdgeMechanism
{
    public SelectedPlanEdgeMechanism(
        string compiledRequirementId,
        string semanticFromNodeId,
        string semanticToNodeId,
        ExactRatioMechanismCandidate mechanism,
        string candidateId,
        string artifactHash)
    {
        CompiledRequirementId = Require(compiledRequirementId, nameof(compiledRequirementId));
        SemanticFromNodeId = Require(semanticFromNodeId, nameof(semanticFromNodeId));
        SemanticToNodeId = Require(semanticToNodeId, nameof(semanticToNodeId));
        Mechanism = mechanism ?? throw new ArgumentNullException(nameof(mechanism));
        CandidateId = Require(candidateId, nameof(candidateId));
        ArtifactHash = Require(artifactHash, nameof(artifactHash));
    }

    public string CompiledRequirementId { get; }
    public string SemanticFromNodeId { get; }
    public string SemanticToNodeId { get; }
    public ExactRatioMechanismCandidate Mechanism { get; }
    public string CandidateId { get; }
    public string ArtifactHash { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable selected-edge value is required.", parameterName);
        }

        return value;
    }
}

public sealed class CompositionInputEdgeProvenance
{
    public CompositionInputEdgeProvenance(
        int index,
        string compiledRequirementId,
        string semanticFromNodeId,
        string semanticToNodeId,
        Rational signedTargetTransfer,
        Rational exactPhaseRelation,
        string generationRequestId,
        string kinematicCandidateId,
        string spatialCandidateId,
        string candidateId,
        string artifactHash)
    {
        Index = index;
        CompiledRequirementId = compiledRequirementId ?? throw new ArgumentNullException(nameof(compiledRequirementId));
        SemanticFromNodeId = semanticFromNodeId ?? throw new ArgumentNullException(nameof(semanticFromNodeId));
        SemanticToNodeId = semanticToNodeId ?? throw new ArgumentNullException(nameof(semanticToNodeId));
        SignedTargetTransfer = signedTargetTransfer;
        ExactPhaseRelation = exactPhaseRelation;
        GenerationRequestId = generationRequestId ?? throw new ArgumentNullException(nameof(generationRequestId));
        KinematicCandidateId = kinematicCandidateId ?? throw new ArgumentNullException(nameof(kinematicCandidateId));
        SpatialCandidateId = spatialCandidateId ?? throw new ArgumentNullException(nameof(spatialCandidateId));
        CandidateId = candidateId ?? throw new ArgumentNullException(nameof(candidateId));
        ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
    }

    public int Index { get; }
    public string CompiledRequirementId { get; }
    public string SemanticFromNodeId { get; }
    public string SemanticToNodeId { get; }
    public Rational SignedTargetTransfer { get; }
    public Rational ExactPhaseRelation { get; }
    public string GenerationRequestId { get; }
    public string KinematicCandidateId { get; }
    public string SpatialCandidateId { get; }
    public string CandidateId { get; }
    public string ArtifactHash { get; }
}

public sealed class CompositionLayerMapping
{
    public CompositionLayerMapping(int localLayer, int globalLayer)
    {
        LocalLayer = localLayer;
        GlobalLayer = globalLayer;
    }

    public int LocalLayer { get; }
    public int GlobalLayer { get; }
}

public sealed class SemanticDofBinding
{
    public SemanticDofBinding(string semanticNodeId, string dofId, SemanticBindingRole role)
    {
        SemanticNodeId = semanticNodeId ?? throw new ArgumentNullException(nameof(semanticNodeId));
        DofId = dofId ?? throw new ArgumentNullException(nameof(dofId));
        Role = role;
    }

    public string SemanticNodeId { get; }
    public string DofId { get; }
    public SemanticBindingRole Role { get; }
}

public sealed class ContinuousPlanCompositionSearchSummary
{
    public ContinuousPlanCompositionSearchSummary(
        long rotationAssignments,
        long layerMappings,
        long compositionExpansions,
        long rawFeasibleCandidates,
        long deduplicatedCandidates,
        int returnedCandidates,
        bool resultTruncated,
        bool searchComplete)
    {
        RotationAssignments = rotationAssignments;
        LayerMappings = layerMappings;
        CompositionExpansions = compositionExpansions;
        RawFeasibleCandidates = rawFeasibleCandidates;
        DeduplicatedCandidates = deduplicatedCandidates;
        ReturnedCandidates = returnedCandidates;
        ResultTruncated = resultTruncated;
        SearchComplete = searchComplete;
    }

    public long RotationAssignments { get; }
    public long LayerMappings { get; }
    public long CompositionExpansions { get; }
    public long RawFeasibleCandidates { get; }
    public long DeduplicatedCandidates { get; }
    public int ReturnedCandidates { get; }
    public bool ResultTruncated { get; }
    public bool SearchComplete { get; }
}

public sealed class ContinuousPlanCompositionPerformance
{
    public ContinuousPlanCompositionPerformance(
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

public sealed class ContinuousPlanCompositionMetrics
{
    public ContinuousPlanCompositionMetrics(
        int usedLayerCount,
        int dofCount,
        int axisCount,
        int bodyCount,
        int contactCount,
        BigInteger boundingWidth,
        BigInteger boundingHeight,
        int unrelatedSameLayerPairChecks)
    {
        UsedLayerCount = usedLayerCount;
        DofCount = dofCount;
        AxisCount = axisCount;
        BodyCount = bodyCount;
        ContactCount = contactCount;
        BoundingWidth = boundingWidth;
        BoundingHeight = boundingHeight;
        BoundingArea = boundingWidth * boundingHeight;
        MaximumExtent = BigInteger.Max(boundingWidth, boundingHeight);
        UnrelatedSameLayerPairChecks = unrelatedSameLayerPairChecks;
    }

    public int UsedLayerCount { get; }
    public int DofCount { get; }
    public int AxisCount { get; }
    public int BodyCount { get; }
    public int ContactCount { get; }
    public BigInteger BoundingWidth { get; }
    public BigInteger BoundingHeight { get; }
    public BigInteger BoundingArea { get; }
    public BigInteger MaximumExtent { get; }
    public int UnrelatedSameLayerPairChecks { get; }
}

public sealed class ContinuousPlanCompositionProvenance
{
    public ContinuousPlanCompositionProvenance(
        string compiledPlanId,
        ContinuousPlanCompositionRequest request,
        string requestCanonicalRepresentation,
        string compositionRequestId,
        ContinuousPlanCompositionFingerprint backendFingerprint,
        BigInteger pitchRadiusTicksPerTooth,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges,
        CardinalRotation selectedRotation,
        IEnumerable<CompositionLayerMapping> selectedLayerMapping,
        ContinuousPlanCompositionSearchSummary searchSummary)
    {
        CompiledPlanId = compiledPlanId ?? throw new ArgumentNullException(nameof(compiledPlanId));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        CompositionRequestId = compositionRequestId ?? throw new ArgumentNullException(nameof(compositionRequestId));
        BackendFingerprint = backendFingerprint ?? throw new ArgumentNullException(nameof(backendFingerprint));
        PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        InputEdges = (inputEdges ?? throw new ArgumentNullException(nameof(inputEdges)))
            .OrderBy(item => item.Index)
            .ToList()
            .AsReadOnly();
        SelectedRotation = selectedRotation;
        SelectedLayerMapping = (selectedLayerMapping ?? throw new ArgumentNullException(nameof(selectedLayerMapping)))
            .OrderBy(item => item.LocalLayer)
            .ThenBy(item => item.GlobalLayer)
            .ToList()
            .AsReadOnly();
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
    }

    public string CompiledPlanId { get; }
    public ContinuousPlanCompositionRequest Request { get; }
    public string RequestCanonicalRepresentation { get; }
    public string CompositionRequestId { get; }
    public ContinuousPlanCompositionFingerprint BackendFingerprint { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<CompositionInputEdgeProvenance> InputEdges { get; }
    public CardinalRotation SelectedRotation { get; }
    public ReadOnlyCollection<CompositionLayerMapping> SelectedLayerMapping { get; }
    public ContinuousPlanCompositionSearchSummary SearchSummary { get; }
}

public sealed class ContinuousPlanCompositionCandidate
{
    public ContinuousPlanCompositionCandidate(
        GenerationCandidate candidate,
        string composedKinematicCandidateId,
        string composedSpatialCandidateId,
        string candidateId,
        IEnumerable<SemanticDofBinding> semanticBindings,
        ContinuousPlanCompositionProvenance provenance,
        ContinuousPlanCompositionMetrics metrics)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        ComposedKinematicCandidateId = composedKinematicCandidateId ?? throw new ArgumentNullException(nameof(composedKinematicCandidateId));
        ComposedSpatialCandidateId = composedSpatialCandidateId ?? throw new ArgumentNullException(nameof(composedSpatialCandidateId));
        CandidateId = candidateId ?? throw new ArgumentNullException(nameof(candidateId));
        SemanticBindings = (semanticBindings ?? throw new ArgumentNullException(nameof(semanticBindings)))
            .OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public GenerationCandidate Candidate { get; }
    public string ComposedKinematicCandidateId { get; }
    public string ComposedSpatialCandidateId { get; }
    public string CandidateId { get; }
    public ReadOnlyCollection<SemanticDofBinding> SemanticBindings { get; }
    public ContinuousPlanCompositionProvenance Provenance { get; }
    public ContinuousPlanCompositionMetrics Metrics { get; }
}

public sealed class ContinuousPlanCompositionResult
{
    public ContinuousPlanCompositionResult(
        string compiledPlanId,
        ContinuousPlanCompositionRequest normalizedRequest,
        string requestCanonicalRepresentation,
        string compositionRequestId,
        ContinuousPlanCompositionFingerprint backendFingerprint,
        ContinuousPlanCompositionStatus status,
        IEnumerable<CompositionInputEdgeProvenance> orderedInputEdges,
        IEnumerable<ContinuousPlanCompositionCandidate> candidates,
        IEnumerable<Diagnostic> diagnostics,
        ContinuousPlanCompositionSearchSummary searchSummary,
        ContinuousPlanCompositionPerformance performance)
    {
        CompiledPlanId = compiledPlanId ?? throw new ArgumentNullException(nameof(compiledPlanId));
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        CompositionRequestId = compositionRequestId ?? throw new ArgumentNullException(nameof(compositionRequestId));
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
    public ContinuousPlanCompositionRequest NormalizedRequest { get; }
    public string RequestCanonicalRepresentation { get; }
    public string CompositionRequestId { get; }
    public ContinuousPlanCompositionFingerprint BackendFingerprint { get; }
    public ContinuousPlanCompositionStatus Status { get; }
    public ReadOnlyCollection<CompositionInputEdgeProvenance> OrderedInputEdges { get; }
    public ReadOnlyCollection<ContinuousPlanCompositionCandidate> Candidates { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public ContinuousPlanCompositionSearchSummary SearchSummary { get; }
    public ContinuousPlanCompositionPerformance Performance { get; }
    public bool IsSuccess => Status == ContinuousPlanCompositionStatus.Complete && Candidates.Count > 0 &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public static class ContinuousPlanCompositionIdentity
{
    public static string BuildRequestCanonicalRepresentation(
        string compiledPlanId,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges,
        ContinuousPlanCompositionRequest request)
    {
        if (compiledPlanId is null) throw new ArgumentNullException(nameof(compiledPlanId));
        if (inputEdges is null) throw new ArgumentNullException(nameof(inputEdges));
        if (request is null) throw new ArgumentNullException(nameof(request));
        var builder = new StringBuilder(ContinuousPlanCompositionContract.BackendSemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(request.DeterminismProfile));
        builder.Append("|compiledPlanId=").Append(LengthPrefixed(compiledPlanId));
        foreach (var edge in inputEdges.OrderBy(item => item.Index))
        {
            builder.Append("|edge=").Append(edge.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(LengthPrefixed(edge.CompiledRequirementId));
            builder.Append(',').Append(LengthPrefixed(edge.CandidateId));
            builder.Append(',').Append(LengthPrefixed(edge.ArtifactHash));
        }

        builder.Append("|maxLayers=").Append(request.MaxLayers.ToString(CultureInfo.InvariantCulture));
        builder.Append("|clearanceTicks=").Append(request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        builder.Append("|maxCompositionExpansions=").Append(request.MaxCompositionExpansions.ToString(CultureInfo.InvariantCulture));
        builder.Append("|maxReturnedCompositions=").Append(request.MaxReturnedCompositions.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    public static string ComputeRequestId(string canonicalRepresentation) =>
        PeriodicSemanticIdentity.Hash("composition-request-sha256:", canonicalRepresentation);

    public static string ComputeComposedKinematicCandidateId(
        KinematicSpecification kinematic,
        ContinuousPlanCompositionFingerprint fingerprint)
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

        return PeriodicSemanticIdentity.Hash("composed-kinematic-sha256:", builder.ToString());
    }

    public static string ComputeComposedSpatialCandidateId(
        string composedKinematicCandidateId,
        SpatialMechanism spatial,
        BigInteger clearanceTicks,
        ContinuousPlanCompositionFingerprint fingerprint)
    {
        if (composedKinematicCandidateId is null) throw new ArgumentNullException(nameof(composedKinematicCandidateId));
        if (spatial is null) throw new ArgumentNullException(nameof(spatial));
        if (fingerprint is null) throw new ArgumentNullException(nameof(fingerprint));
        var builder = new StringBuilder(fingerprint.SemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(fingerprint.DeterminismProfile));
        builder.Append("|kinematic=").Append(LengthPrefixed(composedKinematicCandidateId));
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

        return PeriodicSemanticIdentity.Hash("composed-spatial-sha256:", builder.ToString());
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
