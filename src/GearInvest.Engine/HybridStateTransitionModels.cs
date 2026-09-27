using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class HybridStateTransitionContract
{
    public const string BackendId = "builtin-cyclic-indexed-state";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-cyclic-indexed-state-v1";
    public const string DefaultDeterminismProfile = "portable-managed-indexed-state-v1";
    public const string ResultFormat = "gear-invest.hybrid-state-transition-result";
    public const string ResultFormatVersion = "0.1";
}

public enum HybridStateTransitionStatus
{
    Complete,
    IncompleteBudget,
    InvalidInput,
    Unsupported,
    Cancelled,
}

public sealed class HybridStateAdvanceRequest
{
    public HybridStateAdvanceRequest(
        int maxAppliedOccurrences,
        string determinismProfile = HybridStateTransitionContract.DefaultDeterminismProfile)
    {
        MaxAppliedOccurrences = maxAppliedOccurrences;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public int MaxAppliedOccurrences { get; }
    public string DeterminismProfile { get; }
}

public sealed class HybridStateTransitionSummary
{
    public HybridStateTransitionSummary(
        BigInteger knownOccurrenceCount,
        BigInteger appliedOccurrenceCount,
        BigInteger omittedOccurrenceCount,
        BigInteger transitionApplicationCount)
    {
        KnownOccurrenceCount = knownOccurrenceCount;
        AppliedOccurrenceCount = appliedOccurrenceCount;
        OmittedOccurrenceCount = omittedOccurrenceCount;
        TransitionApplicationCount = transitionApplicationCount;
    }

    public BigInteger KnownOccurrenceCount { get; }
    public BigInteger AppliedOccurrenceCount { get; }
    public BigInteger OmittedOccurrenceCount { get; }
    public BigInteger TransitionApplicationCount { get; }
}

public sealed class HybridStateTransitionPerformance
{
    public HybridStateTransitionPerformance(
        long planValidationMicroseconds,
        long occurrenceNormalizationMicroseconds,
        long transitionApplicationMicroseconds,
        long snapshotMaterializationMicroseconds,
        long totalEvaluationMicroseconds)
    {
        PlanValidationMicroseconds = planValidationMicroseconds;
        OccurrenceNormalizationMicroseconds = occurrenceNormalizationMicroseconds;
        TransitionApplicationMicroseconds = transitionApplicationMicroseconds;
        SnapshotMaterializationMicroseconds = snapshotMaterializationMicroseconds;
        TotalEvaluationMicroseconds = totalEvaluationMicroseconds;
    }

    public long PlanValidationMicroseconds { get; }
    public long OccurrenceNormalizationMicroseconds { get; }
    public long TransitionApplicationMicroseconds { get; }
    public long SnapshotMaterializationMicroseconds { get; }
    public long TotalEvaluationMicroseconds { get; }

    public static HybridStateTransitionPerformance Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed class HybridStateTransitionResult
{
    public HybridStateTransitionResult(
        ContinuousEventBridgeResult eventInput,
        IndexedStateTransitionPlan transitionPlan,
        HybridStateAdvanceRequest normalizedRequest,
        string hybridStateAdvanceRequestId,
        HybridStateTransitionStatus status,
        bool searchComplete,
        bool resultTruncated,
        HybridStateTransitionSummary summary,
        HybridStateSnapshot initialSnapshot,
        IEnumerable<StateTransitionApplication> applications,
        HybridStateSnapshot? checkpointSnapshot,
        HybridStateSnapshot? finalSnapshot,
        IEnumerable<Diagnostic> diagnostics,
        HybridStateTransitionPerformance performance)
    {
        EventInput = eventInput ?? throw new ArgumentNullException(nameof(eventInput));
        TransitionPlan = transitionPlan ?? throw new ArgumentNullException(nameof(transitionPlan));
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        HybridStateAdvanceRequestId = hybridStateAdvanceRequestId ?? string.Empty;
        Status = status;
        SearchComplete = searchComplete;
        ResultTruncated = resultTruncated;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        InitialSnapshot = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));
        Applications = (applications ?? throw new ArgumentNullException(nameof(applications)))
            .OrderBy(item => item.RootTurnsAtApplication)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.TransitionDefinitionId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        CheckpointSnapshot = checkpointSnapshot;
        FinalSnapshot = finalSnapshot;
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        Performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    public ContinuousEventBridgeResult EventInput { get; }
    public IndexedStateTransitionPlan TransitionPlan { get; }
    public HybridStateAdvanceRequest NormalizedRequest { get; }
    public string HybridStateAdvanceRequestId { get; }
    public HybridStateTransitionStatus Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public HybridStateTransitionSummary Summary { get; }
    public HybridStateSnapshot InitialSnapshot { get; }
    public ReadOnlyCollection<StateTransitionApplication> Applications { get; }
    public HybridStateSnapshot? CheckpointSnapshot { get; }
    public HybridStateSnapshot? FinalSnapshot { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public HybridStateTransitionPerformance Performance { get; }
    public bool IsSuccess => Status == HybridStateTransitionStatus.Complete && SearchComplete &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public static class HybridStateTransitionIdentity
{
    public static string ComputeAdvanceRequestId(
        HybridStateSnapshot initialSnapshot,
        IndexedStateTransitionPlan transitionPlan,
        string eventBridgeRequestId,
        HybridStateAdvanceRequest request)
    {
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        if (transitionPlan is null) throw new ArgumentNullException(nameof(transitionPlan));
        if (request is null) throw new ArgumentNullException(nameof(request));
        var canonical = HybridStateTransitionContract.BackendSemanticVersion +
            "|initialSnapshotId=" + LengthPrefixed(initialSnapshot.HybridStateSnapshotId) +
            "|transitionPlanId=" + LengthPrefixed(transitionPlan.IndexedStateTransitionPlanId) +
            "|eventBridgeRequestId=" + LengthPrefixed(eventBridgeRequestId ?? string.Empty) +
            "|maxAppliedOccurrences=" + request.MaxAppliedOccurrences.ToString(CultureInfo.InvariantCulture) +
            "|determinismProfile=" + LengthPrefixed(request.DeterminismProfile);
        return PeriodicSemanticIdentity.Hash("hybrid-state-advance-request-sha256:", canonical);
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
