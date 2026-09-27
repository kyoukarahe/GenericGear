using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class GuardedHybridStateTransitionContract
{
    public const string BackendId = "builtin-guarded-indexed-state";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-guarded-indexed-state-v1";
    public const string DefaultDeterminismProfile = GuardedIndexedStateContract.DefaultDeterminismProfile;
    public const string ResultFormat = "gear-invest.guarded-hybrid-state-transition-result";
    public const string ResultFormatVersion = "0.1";
}

public enum GuardedHybridStateTransitionStatus
{
    Complete,
    IncompleteBudget,
    InvalidInput,
    Unsupported,
    Cancelled,
}

public sealed class GuardedHybridStateAdvanceRequest
{
    public GuardedHybridStateAdvanceRequest(
        int maxAppliedOccurrences,
        string determinismProfile = GuardedHybridStateTransitionContract.DefaultDeterminismProfile)
    {
        MaxAppliedOccurrences = maxAppliedOccurrences;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public int MaxAppliedOccurrences { get; }
    public string DeterminismProfile { get; }
}

public sealed class GuardedHybridStateTransitionSummary
{
    public GuardedHybridStateTransitionSummary(
        BigInteger knownOccurrenceCount,
        BigInteger appliedOccurrenceCount,
        BigInteger omittedOccurrenceCount,
        BigInteger occurrenceGroupCount,
        BigInteger transitionApplicationCount)
    {
        KnownOccurrenceCount = knownOccurrenceCount;
        AppliedOccurrenceCount = appliedOccurrenceCount;
        OmittedOccurrenceCount = omittedOccurrenceCount;
        OccurrenceGroupCount = occurrenceGroupCount;
        TransitionApplicationCount = transitionApplicationCount;
    }

    public BigInteger KnownOccurrenceCount { get; }
    public BigInteger AppliedOccurrenceCount { get; }
    public BigInteger OmittedOccurrenceCount { get; }
    public BigInteger OccurrenceGroupCount { get; }
    public BigInteger TransitionApplicationCount { get; }
}

public sealed class GuardedHybridStateTransitionPerformance
{
    public GuardedHybridStateTransitionPerformance(
        long planAndInitialValidationMicroseconds,
        long occurrenceNormalizationMicroseconds,
        long guardEvaluationMicroseconds,
        long effectPlanningMicroseconds,
        long atomicCommitMicroseconds,
        long snapshotMaterializationMicroseconds,
        long totalEvaluationMicroseconds)
    {
        PlanAndInitialValidationMicroseconds = planAndInitialValidationMicroseconds;
        OccurrenceNormalizationMicroseconds = occurrenceNormalizationMicroseconds;
        GuardEvaluationMicroseconds = guardEvaluationMicroseconds;
        EffectPlanningMicroseconds = effectPlanningMicroseconds;
        AtomicCommitMicroseconds = atomicCommitMicroseconds;
        SnapshotMaterializationMicroseconds = snapshotMaterializationMicroseconds;
        TotalEvaluationMicroseconds = totalEvaluationMicroseconds;
    }

    public long PlanAndInitialValidationMicroseconds { get; }
    public long OccurrenceNormalizationMicroseconds { get; }
    public long GuardEvaluationMicroseconds { get; }
    public long EffectPlanningMicroseconds { get; }
    public long AtomicCommitMicroseconds { get; }
    public long SnapshotMaterializationMicroseconds { get; }
    public long TotalEvaluationMicroseconds { get; }

    public static GuardedHybridStateTransitionPerformance Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

public sealed class GuardedHybridStateTransitionResult
{
    public GuardedHybridStateTransitionResult(
        ContinuousEventBridgeResult eventInput,
        GuardedIndexedStateTransitionPlan transitionPlan,
        GuardedHybridStateAdvanceRequest normalizedRequest,
        string guardedHybridStateAdvanceRequestId,
        GuardedHybridStateTransitionStatus status,
        bool searchComplete,
        bool resultTruncated,
        GuardedHybridStateTransitionSummary summary,
        HybridStateSnapshot initialSnapshot,
        IEnumerable<StateTransitionApplication> unconditionalApplications,
        IEnumerable<GuardedStateEffectApplication> guardedEffectApplications,
        IEnumerable<GuardedOccurrenceApplicationGroup> occurrenceGroups,
        HybridStateSnapshot? checkpointSnapshot,
        HybridStateSnapshot? finalSnapshot,
        IEnumerable<Diagnostic> diagnostics,
        GuardedHybridStateTransitionPerformance performance)
    {
        EventInput = eventInput ?? throw new ArgumentNullException(nameof(eventInput));
        TransitionPlan = transitionPlan ?? throw new ArgumentNullException(nameof(transitionPlan));
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        GuardedHybridStateAdvanceRequestId = guardedHybridStateAdvanceRequestId ?? string.Empty;
        Status = status;
        SearchComplete = searchComplete;
        ResultTruncated = resultTruncated;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        InitialSnapshot = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));
        UnconditionalApplications = CanonicalApplications(unconditionalApplications);
        GuardedEffectApplications = (guardedEffectApplications ?? throw new ArgumentNullException(nameof(guardedEffectApplications)))
            .OrderBy(item => item.RootTurnsAtApplication)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.EffectDefinitionId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        OccurrenceGroups = (occurrenceGroups ?? throw new ArgumentNullException(nameof(occurrenceGroups)))
            .OrderBy(item => item.RootTurnsAtApplication)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.GuardedOccurrenceApplicationGroupId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        CheckpointSnapshot = checkpointSnapshot;
        FinalSnapshot = finalSnapshot;
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        Performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    public ContinuousEventBridgeResult EventInput { get; }
    public GuardedIndexedStateTransitionPlan TransitionPlan { get; }
    public GuardedHybridStateAdvanceRequest NormalizedRequest { get; }
    public string GuardedHybridStateAdvanceRequestId { get; }
    public GuardedHybridStateTransitionStatus Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public GuardedHybridStateTransitionSummary Summary { get; }
    public HybridStateSnapshot InitialSnapshot { get; }
    public ReadOnlyCollection<StateTransitionApplication> UnconditionalApplications { get; }
    public ReadOnlyCollection<GuardedStateEffectApplication> GuardedEffectApplications { get; }
    public ReadOnlyCollection<GuardedOccurrenceApplicationGroup> OccurrenceGroups { get; }
    public HybridStateSnapshot? CheckpointSnapshot { get; }
    public HybridStateSnapshot? FinalSnapshot { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public GuardedHybridStateTransitionPerformance Performance { get; }
    public bool IsSuccess => Status == GuardedHybridStateTransitionStatus.Complete && SearchComplete &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);

    private static ReadOnlyCollection<StateTransitionApplication> CanonicalApplications(
        IEnumerable<StateTransitionApplication> applications) =>
        (applications ?? throw new ArgumentNullException(nameof(applications)))
            .OrderBy(item => item.RootTurnsAtApplication)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.TransitionDefinitionId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
}

public static class GuardedHybridStateTransitionIdentity
{
    public static string ComputeAdvanceRequestId(
        HybridStateSnapshot initialSnapshot,
        GuardedIndexedStateTransitionPlan transitionPlan,
        string eventBridgeRequestId,
        GuardedHybridStateAdvanceRequest request)
    {
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        if (transitionPlan is null) throw new ArgumentNullException(nameof(transitionPlan));
        if (request is null) throw new ArgumentNullException(nameof(request));
        var canonical = GuardedHybridStateTransitionContract.BackendSemanticVersion +
            "|initialSnapshotId=" + LengthPrefixed(initialSnapshot.HybridStateSnapshotId) +
            "|transitionPlanId=" + LengthPrefixed(transitionPlan.GuardedIndexedTransitionPlanId) +
            "|eventBridgeRequestId=" + LengthPrefixed(eventBridgeRequestId ?? string.Empty) +
            "|maxAppliedOccurrences=" + request.MaxAppliedOccurrences.ToString(CultureInfo.InvariantCulture) +
            "|determinismProfile=" + LengthPrefixed(request.DeterminismProfile);
        return PeriodicSemanticIdentity.Hash("guarded-hybrid-state-advance-request-sha256:", canonical);
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
