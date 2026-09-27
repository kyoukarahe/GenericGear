using System;
using System.Numerics;

namespace GearInvest.Core;

public enum PeriodicEventTraversalPolicy
{
    ForwardOnly,
}

public enum PeriodicEventTraversalDirection
{
    Forward,
}

public sealed class PeriodicPhaseEventDefinition
{
    public PeriodicPhaseEventDefinition(
        string key,
        string sourceDofId,
        Rational periodTurns,
        Rational crossingPhaseTurns,
        PeriodicEventTraversalPolicy traversalPolicy = PeriodicEventTraversalPolicy.ForwardOnly)
    {
        Key = key ?? string.Empty;
        SourceDofId = sourceDofId ?? string.Empty;
        PeriodTurns = periodTurns;
        CrossingPhaseTurns = crossingPhaseTurns;
        TraversalPolicy = traversalPolicy;
    }

    public string Key { get; }

    public string SourceDofId { get; }

    public Rational PeriodTurns { get; }

    public Rational CrossingPhaseTurns { get; }

    public PeriodicEventTraversalPolicy TraversalPolicy { get; }
}

public sealed class PeriodicEventOccurrence
{
    public PeriodicEventOccurrence(
        string occurrenceId,
        string eventKey,
        string periodicEventDefinitionId,
        BigInteger eventOrdinal,
        string driverId,
        Rational rootTurnsAtCrossing,
        string sourceDofId,
        Rational sourceTurnsAtCrossing,
        PeriodicEventTraversalDirection traversalDirection)
    {
        OccurrenceId = Require(occurrenceId, nameof(occurrenceId));
        EventKey = Require(eventKey, nameof(eventKey));
        PeriodicEventDefinitionId = Require(periodicEventDefinitionId, nameof(periodicEventDefinitionId));
        EventOrdinal = eventOrdinal;
        DriverId = Require(driverId, nameof(driverId));
        RootTurnsAtCrossing = rootTurnsAtCrossing;
        SourceDofId = Require(sourceDofId, nameof(sourceDofId));
        SourceTurnsAtCrossing = sourceTurnsAtCrossing;
        TraversalDirection = traversalDirection;
    }

    public string OccurrenceId { get; }

    public string EventKey { get; }

    public string PeriodicEventDefinitionId { get; }

    public BigInteger EventOrdinal { get; }

    public string DriverId { get; }

    public Rational RootTurnsAtCrossing { get; }

    public string SourceDofId { get; }

    public Rational SourceTurnsAtCrossing { get; }

    public PeriodicEventTraversalDirection TraversalDirection { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable event value is required.", parameterName);
        }

        return value;
    }
}
