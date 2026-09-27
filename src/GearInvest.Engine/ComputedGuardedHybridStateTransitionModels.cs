using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class ComputedGuardedHybridStateTransitionContract
{
    public const string BackendId = "builtin-computed-guarded-indexed-state";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-computed-guarded-indexed-state-v1";
    public const string DefaultDeterminismProfile = ComputedGuardedIndexedStateContract.DefaultDeterminismProfile;
    public const string ResultFormat = "gear-invest.guarded-hybrid-state-transition-result";
    public const string ResultFormatVersion = "0.3";
}
public sealed class ComputedGuardedHybridStateTransitionResult
{
    public ComputedGuardedHybridStateTransitionResult(
        ContinuousEventBridgeResult eventInput,
        ComputedGuardedIndexedStateTransitionPlan transitionPlan,
        GuardedHybridStateAdvanceRequest normalizedRequest,
        string guardedHybridStateAdvanceRequestId,
        GuardedHybridStateTransitionStatus status,
        bool searchComplete,
        bool resultTruncated,
        GuardedHybridStateTransitionSummary summary,
        HybridStateSnapshot initialSnapshot,
        IEnumerable<StateTransitionApplication> unconditionalApplications,
        IEnumerable<GuardedStateEffectApplication> guardedEffectApplications,
        IEnumerable<ComputedIndexedValueEvaluation> computedEvaluations,
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
        UnconditionalApplications = (unconditionalApplications ?? throw new ArgumentNullException(nameof(unconditionalApplications)))
            .OrderBy(item => item.RootTurnsAtApplication)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.TransitionDefinitionId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        GuardedEffectApplications = (guardedEffectApplications ?? throw new ArgumentNullException(nameof(guardedEffectApplications)))
            .OrderBy(item => item.RootTurnsAtApplication)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.EffectDefinitionId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        ComputedEvaluations = (computedEvaluations ?? throw new ArgumentNullException(nameof(computedEvaluations)))
            .OrderBy(item => item.RootTurnsAtEvaluation)
            .ThenBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.EventOrdinal)
            .ThenBy(item => item.ComputedIndexedValueDefinitionId, StringComparer.Ordinal)
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
    public ComputedGuardedIndexedStateTransitionPlan TransitionPlan { get; }
    public GuardedHybridStateAdvanceRequest NormalizedRequest { get; }
    public string GuardedHybridStateAdvanceRequestId { get; }
    public GuardedHybridStateTransitionStatus Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public GuardedHybridStateTransitionSummary Summary { get; }
    public HybridStateSnapshot InitialSnapshot { get; }
    public ReadOnlyCollection<StateTransitionApplication> UnconditionalApplications { get; }
    public ReadOnlyCollection<GuardedStateEffectApplication> GuardedEffectApplications { get; }
    public ReadOnlyCollection<ComputedIndexedValueEvaluation> ComputedEvaluations { get; }
    public ReadOnlyCollection<GuardedOccurrenceApplicationGroup> OccurrenceGroups { get; }
    public HybridStateSnapshot? CheckpointSnapshot { get; }
    public HybridStateSnapshot? FinalSnapshot { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public GuardedHybridStateTransitionPerformance Performance { get; }
    public bool IsSuccess => Status == GuardedHybridStateTransitionStatus.Complete && SearchComplete &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public static class ComputedGuardedHybridStateTransitionIdentity
{
    public static string ComputeAdvanceRequestId(
        HybridStateSnapshot initialSnapshot,
        ComputedGuardedIndexedStateTransitionPlan transitionPlan,
        string eventBridgeRequestId,
        GuardedHybridStateAdvanceRequest request)
    {
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        if (transitionPlan is null) throw new ArgumentNullException(nameof(transitionPlan));
        if (request is null) throw new ArgumentNullException(nameof(request));
        var canonical = ComputedGuardedHybridStateTransitionContract.BackendSemanticVersion +
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
