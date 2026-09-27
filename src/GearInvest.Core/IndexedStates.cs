using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GearInvest.Core;

public static class IndexedStateContract
{
    public const string CyclicStateSemanticVersion = "cyclic-indexed-state-v1";
    public const string TransitionSemanticVersion = "indexed-state-transition-v1";
    public const string TransitionPlanSemanticVersion = "indexed-state-transition-plan-v1";
    public const string SnapshotSemanticVersion = "hybrid-state-snapshot-v1";
    public const string ApplicationSemanticVersion = "indexed-state-transition-application-v1";
}

public sealed class CyclicIndexedStateDefinition
{
    public CyclicIndexedStateDefinition(string key, BigInteger cycleLength)
    {
        Key = key ?? string.Empty;
        CycleLength = cycleLength;
        CyclicStateDefinitionId = IndexedStateIdentity.ComputeCyclicStateDefinitionId(Key, CycleLength);
    }

    public string Key { get; }
    public BigInteger CycleLength { get; }
    public string CyclicStateDefinitionId { get; }
}

public sealed class CyclicIndexedStateValue
{
    public CyclicIndexedStateValue(string stateDefinitionId, BigInteger currentIndex)
    {
        StateDefinitionId = stateDefinitionId ?? string.Empty;
        CurrentIndex = currentIndex;
    }

    public string StateDefinitionId { get; }
    public BigInteger CurrentIndex { get; }
}

public sealed class IndexedStateTransitionDefinition
{
    public IndexedStateTransitionDefinition(
        string key,
        string sourcePeriodicEventDefinitionId,
        string targetCyclicStateDefinitionId,
        BigInteger signedDelta)
    {
        Key = key ?? string.Empty;
        SourcePeriodicEventDefinitionId = sourcePeriodicEventDefinitionId ?? string.Empty;
        TargetCyclicStateDefinitionId = targetCyclicStateDefinitionId ?? string.Empty;
        SignedDelta = signedDelta;
        IndexedStateTransitionDefinitionId = IndexedStateIdentity.ComputeTransitionDefinitionId(
            SourcePeriodicEventDefinitionId,
            TargetCyclicStateDefinitionId,
            SignedDelta);
    }

    public string Key { get; }
    public string SourcePeriodicEventDefinitionId { get; }
    public string TargetCyclicStateDefinitionId { get; }
    public BigInteger SignedDelta { get; }
    public string IndexedStateTransitionDefinitionId { get; }
}

public sealed class IndexedStateTransitionPlan
{
    public IndexedStateTransitionPlan(
        IEnumerable<CyclicIndexedStateDefinition> stateDefinitions,
        IEnumerable<IndexedStateTransitionDefinition> transitionDefinitions)
    {
        OrderedStateDefinitions = (stateDefinitions ?? throw new ArgumentNullException(nameof(stateDefinitions)))
            .OrderBy(item => item.CyclicStateDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        OrderedTransitionDefinitions = (transitionDefinitions ?? throw new ArgumentNullException(nameof(transitionDefinitions)))
            .OrderBy(item => item.IndexedStateTransitionDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        IndexedStateTransitionPlanId = IndexedStateIdentity.ComputeTransitionPlanId(
            OrderedStateDefinitions,
            OrderedTransitionDefinitions);
    }

    public ReadOnlyCollection<CyclicIndexedStateDefinition> OrderedStateDefinitions { get; }
    public ReadOnlyCollection<IndexedStateTransitionDefinition> OrderedTransitionDefinitions { get; }
    public string IndexedStateTransitionPlanId { get; }
}

public sealed class HybridEventCursor
{
    public HybridEventCursor(string periodicEventDefinitionId, BigInteger lastAppliedOrdinal)
    {
        PeriodicEventDefinitionId = periodicEventDefinitionId ?? string.Empty;
        LastAppliedOrdinal = lastAppliedOrdinal;
    }

    public string PeriodicEventDefinitionId { get; }
    public BigInteger LastAppliedOrdinal { get; }
}

public sealed class HybridStateSnapshot
{
    public HybridStateSnapshot(
        string sourceMechanismCandidateId,
        string driverId,
        Rational exactRootTurns,
        IEnumerable<CyclicIndexedStateValue> states,
        IEnumerable<HybridEventCursor> eventCursors)
    {
        SourceMechanismCandidateId = sourceMechanismCandidateId ?? string.Empty;
        DriverId = driverId ?? string.Empty;
        ExactRootTurns = exactRootTurns;
        States = (states ?? throw new ArgumentNullException(nameof(states)))
            .OrderBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.CurrentIndex)
            .ToList()
            .AsReadOnly();
        EventCursors = (eventCursors ?? throw new ArgumentNullException(nameof(eventCursors)))
            .OrderBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.LastAppliedOrdinal)
            .ToList()
            .AsReadOnly();
        HybridStateSnapshotId = IndexedStateIdentity.ComputeSnapshotId(
            SourceMechanismCandidateId,
            DriverId,
            ExactRootTurns,
            States,
            EventCursors);
    }

    public string HybridStateSnapshotId { get; }
    public string SourceMechanismCandidateId { get; }
    public string DriverId { get; }
    public Rational ExactRootTurns { get; }
    public ReadOnlyCollection<CyclicIndexedStateValue> States { get; }
    public ReadOnlyCollection<HybridEventCursor> EventCursors { get; }
}

public sealed class StateTransitionApplication
{
    public StateTransitionApplication(
        string transitionDefinitionId,
        string periodicEventOccurrenceId,
        string periodicEventDefinitionId,
        BigInteger eventOrdinal,
        Rational rootTurnsAtApplication,
        string targetStateDefinitionId,
        BigInteger beforeIndex,
        BigInteger signedDelta,
        BigInteger afterIndex)
    {
        TransitionDefinitionId = transitionDefinitionId ?? string.Empty;
        PeriodicEventOccurrenceId = periodicEventOccurrenceId ?? string.Empty;
        PeriodicEventDefinitionId = periodicEventDefinitionId ?? string.Empty;
        EventOrdinal = eventOrdinal;
        RootTurnsAtApplication = rootTurnsAtApplication;
        TargetStateDefinitionId = targetStateDefinitionId ?? string.Empty;
        BeforeIndex = beforeIndex;
        SignedDelta = signedDelta;
        AfterIndex = afterIndex;
        TransitionApplicationId = IndexedStateIdentity.ComputeApplicationId(
            TransitionDefinitionId,
            PeriodicEventOccurrenceId,
            BeforeIndex,
            AfterIndex);
    }

    public string TransitionApplicationId { get; }
    public string TransitionDefinitionId { get; }
    public string PeriodicEventOccurrenceId { get; }
    public string PeriodicEventDefinitionId { get; }
    public BigInteger EventOrdinal { get; }
    public Rational RootTurnsAtApplication { get; }
    public string TargetStateDefinitionId { get; }
    public BigInteger BeforeIndex { get; }
    public BigInteger SignedDelta { get; }
    public BigInteger AfterIndex { get; }
}

public static class IndexedStateIdentity
{
    public static string ComputeCyclicStateDefinitionId(string key, BigInteger cycleLength)
    {
        var canonical = IndexedStateContract.CyclicStateSemanticVersion +
            "|key=" + LengthPrefixed(key ?? string.Empty) +
            "|cycleLength=" + cycleLength.ToString(CultureInfo.InvariantCulture);
        return PeriodicSemanticIdentity.Hash("cyclic-state-definition-sha256:", canonical);
    }

    public static string ComputeTransitionDefinitionId(
        string sourcePeriodicEventDefinitionId,
        string targetCyclicStateDefinitionId,
        BigInteger signedDelta)
    {
        var canonical = IndexedStateContract.TransitionSemanticVersion +
            "|sourcePeriodicEventDefinitionId=" + LengthPrefixed(sourcePeriodicEventDefinitionId ?? string.Empty) +
            "|targetCyclicStateDefinitionId=" + LengthPrefixed(targetCyclicStateDefinitionId ?? string.Empty) +
            "|signedDelta=" + signedDelta.ToString(CultureInfo.InvariantCulture);
        return PeriodicSemanticIdentity.Hash("indexed-transition-definition-sha256:", canonical);
    }

    public static string ComputeTransitionPlanId(
        IEnumerable<CyclicIndexedStateDefinition> stateDefinitions,
        IEnumerable<IndexedStateTransitionDefinition> transitionDefinitions)
    {
        var builder = new StringBuilder(IndexedStateContract.TransitionPlanSemanticVersion);
        foreach (var state in stateDefinitions.OrderBy(item => item.CyclicStateDefinitionId, StringComparer.Ordinal))
        {
            builder.Append("|stateDefinitionId=").Append(LengthPrefixed(state.CyclicStateDefinitionId));
        }

        foreach (var transition in transitionDefinitions.OrderBy(
                     item => item.IndexedStateTransitionDefinitionId,
                     StringComparer.Ordinal))
        {
            builder.Append("|transitionDefinitionId=")
                .Append(LengthPrefixed(transition.IndexedStateTransitionDefinitionId));
        }

        return PeriodicSemanticIdentity.Hash("indexed-transition-plan-sha256:", builder.ToString());
    }

    public static string ComputeSnapshotId(
        string sourceMechanismCandidateId,
        string driverId,
        Rational exactRootTurns,
        IEnumerable<CyclicIndexedStateValue> states,
        IEnumerable<HybridEventCursor> cursors)
    {
        var builder = new StringBuilder(IndexedStateContract.SnapshotSemanticVersion);
        builder.Append("|sourceMechanismCandidateId=").Append(LengthPrefixed(sourceMechanismCandidateId ?? string.Empty));
        builder.Append("|driverId=").Append(LengthPrefixed(driverId ?? string.Empty));
        builder.Append("|exactRootTurns=").Append(exactRootTurns);
        foreach (var state in states.OrderBy(item => item.StateDefinitionId, StringComparer.Ordinal))
        {
            builder.Append("|state=").Append(LengthPrefixed(state.StateDefinitionId));
            builder.Append(',').Append(state.CurrentIndex.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var cursor in cursors.OrderBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal))
        {
            builder.Append("|cursor=").Append(LengthPrefixed(cursor.PeriodicEventDefinitionId));
            builder.Append(',').Append(cursor.LastAppliedOrdinal.ToString(CultureInfo.InvariantCulture));
        }

        return PeriodicSemanticIdentity.Hash("hybrid-state-snapshot-sha256:", builder.ToString());
    }

    public static string ComputeApplicationId(
        string transitionDefinitionId,
        string periodicEventOccurrenceId,
        BigInteger beforeIndex,
        BigInteger afterIndex)
    {
        var canonical = IndexedStateContract.ApplicationSemanticVersion +
            "|transitionDefinitionId=" + LengthPrefixed(transitionDefinitionId ?? string.Empty) +
            "|periodicEventOccurrenceId=" + LengthPrefixed(periodicEventOccurrenceId ?? string.Empty) +
            "|beforeIndex=" + beforeIndex.ToString(CultureInfo.InvariantCulture) +
            "|afterIndex=" + afterIndex.ToString(CultureInfo.InvariantCulture);
        return PeriodicSemanticIdentity.Hash("state-transition-application-sha256:", canonical);
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
