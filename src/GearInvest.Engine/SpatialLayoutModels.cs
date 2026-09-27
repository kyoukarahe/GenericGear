using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class SpatialLayoutContract
{
    public const string BackendId = "builtin-cardinal-layout";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-cardinal-layout-v1";
    public const string DefaultDeterminismProfile = "portable-managed-spatial-v1";
    public const string ResultFormat = "gear-invest.spatial-layout-result";
    public const string ResultFormatVersion = "0.1";
}

public enum SpatialLayoutStatus
{
    Complete,
    IncompleteBudget,
    Infeasible,
    InvalidInput,
    Cancelled,
}

public enum CardinalDirection
{
    East,
    North,
    West,
    South,
}

public sealed class SpatialLayoutRequest
{
    public SpatialLayoutRequest(
        BigInteger rootAxisX,
        BigInteger rootAxisY,
        BigInteger pitchRadiusTicksPerTooth,
        int maxLayers,
        int maxReturnedLayouts,
        long maxPlacementExpansions,
        BigInteger clearanceTicks,
        string determinismProfile = SpatialLayoutContract.DefaultDeterminismProfile)
    {
        RootAxisX = rootAxisX;
        RootAxisY = rootAxisY;
        PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        MaxLayers = maxLayers;
        MaxReturnedLayouts = maxReturnedLayouts;
        MaxPlacementExpansions = maxPlacementExpansions;
        ClearanceTicks = clearanceTicks;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public BigInteger RootAxisX { get; }

    public BigInteger RootAxisY { get; }

    public BigInteger PitchRadiusTicksPerTooth { get; }

    public int MaxLayers { get; }

    public int MaxReturnedLayouts { get; }

    public long MaxPlacementExpansions { get; }

    public BigInteger ClearanceTicks { get; }

    public string DeterminismProfile { get; }
}

public sealed class SpatialLayoutFingerprint
{
    public SpatialLayoutFingerprint(string backendId, string backendVersion, string determinismProfile)
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

public sealed class SpatialLayoutMetrics
{
    public SpatialLayoutMetrics(
        int usedLayerCount,
        int bodyCount,
        int contactCount,
        BigInteger boundingWidth,
        BigInteger boundingHeight,
        int directionChangeCount)
    {
        UsedLayerCount = usedLayerCount;
        BodyCount = bodyCount;
        ContactCount = contactCount;
        BoundingWidth = boundingWidth;
        BoundingHeight = boundingHeight;
        BoundingArea = boundingWidth * boundingHeight;
        DirectionChangeCount = directionChangeCount;
        MaximumExtent = BigInteger.Max(boundingWidth, boundingHeight);
    }

    public int UsedLayerCount { get; }

    public int BodyCount { get; }

    public int ContactCount { get; }

    public BigInteger BoundingWidth { get; }

    public BigInteger BoundingHeight { get; }

    public BigInteger BoundingArea { get; }

    public int DirectionChangeCount { get; }

    public BigInteger MaximumExtent { get; }
}

public sealed class SpatialLayoutSearchSummary
{
    public SpatialLayoutSearchSummary(
        long directionAssignmentsConsidered,
        long layerAssignmentsConsidered,
        long placementExpansions,
        long rawFeasibleLayouts,
        long deduplicatedLayouts,
        int returnedLayouts,
        bool resultTruncated,
        bool searchComplete)
    {
        DirectionAssignmentsConsidered = directionAssignmentsConsidered;
        LayerAssignmentsConsidered = layerAssignmentsConsidered;
        PlacementExpansions = placementExpansions;
        RawFeasibleLayouts = rawFeasibleLayouts;
        DeduplicatedLayouts = deduplicatedLayouts;
        ReturnedLayouts = returnedLayouts;
        ResultTruncated = resultTruncated;
        SearchComplete = searchComplete;
    }

    public long DirectionAssignmentsConsidered { get; }

    public long LayerAssignmentsConsidered { get; }

    public long PlacementExpansions { get; }

    public long RawFeasibleLayouts { get; }

    public long DeduplicatedLayouts { get; }

    public int ReturnedLayouts { get; }

    public bool ResultTruncated { get; }

    public bool SearchComplete { get; }
}

public sealed class SpatialLayoutCandidate
{
    internal SpatialLayoutCandidate(
        string kinematicCandidateId,
        string spatialCandidateId,
        string canonicalSignature,
        SpatialMechanism spatialMechanism,
        IEnumerable<CardinalDirection> orderedDirections,
        IEnumerable<int> stageLayers,
        SpatialValidationBundle validation,
        SpatialLayoutMetrics metrics)
    {
        KinematicCandidateId = kinematicCandidateId ?? throw new ArgumentNullException(nameof(kinematicCandidateId));
        SpatialCandidateId = spatialCandidateId ?? throw new ArgumentNullException(nameof(spatialCandidateId));
        CanonicalSignature = canonicalSignature ?? throw new ArgumentNullException(nameof(canonicalSignature));
        SpatialMechanism = spatialMechanism ?? throw new ArgumentNullException(nameof(spatialMechanism));
        OrderedDirections = (orderedDirections ?? throw new ArgumentNullException(nameof(orderedDirections))).ToList().AsReadOnly();
        StageLayers = (stageLayers ?? throw new ArgumentNullException(nameof(stageLayers))).ToList().AsReadOnly();
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public string KinematicCandidateId { get; }

    public string SpatialCandidateId { get; }

    public string CanonicalSignature { get; }

    public SpatialMechanism SpatialMechanism { get; }

    public ReadOnlyCollection<CardinalDirection> OrderedDirections { get; }

    public ReadOnlyCollection<int> StageLayers { get; }

    public SpatialValidationBundle Validation { get; }

    public SpatialLayoutMetrics Metrics { get; }
}

public sealed class SpatialLayoutResult
{
    public SpatialLayoutResult(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest normalizedRequest,
        string requestCanonicalRepresentation,
        string requestId,
        SpatialLayoutStatus status,
        IEnumerable<SpatialLayoutCandidate> candidates,
        IEnumerable<Diagnostic> diagnostics,
        SpatialLayoutSearchSummary searchSummary,
        SpatialLayoutFingerprint backendFingerprint)
    {
        SourceCandidate = sourceCandidate ?? throw new ArgumentNullException(nameof(sourceCandidate));
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Status = status;
        Candidates = (candidates ?? throw new ArgumentNullException(nameof(candidates))).ToList().AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
        BackendFingerprint = backendFingerprint ?? throw new ArgumentNullException(nameof(backendFingerprint));
    }

    public KinematicSynthesisCandidate SourceCandidate { get; }

    public SpatialLayoutRequest NormalizedRequest { get; }

    public string RequestCanonicalRepresentation { get; }

    public string RequestId { get; }

    public SpatialLayoutStatus Status { get; }

    public ReadOnlyCollection<SpatialLayoutCandidate> Candidates { get; }

    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }

    public SpatialLayoutSearchSummary SearchSummary { get; }

    public SpatialLayoutFingerprint BackendFingerprint { get; }

    public bool IsSuccess =>
        Status == SpatialLayoutStatus.Complete &&
        Candidates.Count > 0 &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

internal sealed class SpatialLayoutCandidateComparer : IComparer<SpatialLayoutCandidate>
{
    public static readonly SpatialLayoutCandidateComparer Instance = new();

    private SpatialLayoutCandidateComparer()
    {
    }

    public int Compare(SpatialLayoutCandidate? left, SpatialLayoutCandidate? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var comparison = left.Metrics.UsedLayerCount.CompareTo(right.Metrics.UsedLayerCount);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.BodyCount.CompareTo(right.Metrics.BodyCount);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.BoundingArea.CompareTo(right.Metrics.BoundingArea);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.MaximumExtent.CompareTo(right.Metrics.MaximumExtent);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.DirectionChangeCount.CompareTo(right.Metrics.DirectionChangeCount);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.CanonicalSignature, right.CanonicalSignature);
    }
}
