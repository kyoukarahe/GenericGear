using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class ContinuousEventBridgeContract
{
    public const string BackendId = "builtin-exact-forward-phase-crossing";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-exact-forward-phase-crossing-v1";
    public const string DefaultDeterminismProfile = "portable-managed-event-bridge-v1";
    public const string ResultFormat = "gear-invest.continuous-event-bridge-result";
    public const string ResultFormatVersion = "0.1";
}

public enum ContinuousEventBridgeStatus
{
    Complete,
    IncompleteBudget,
    InvalidInput,
    Unsupported,
    Cancelled,
}

public sealed class ContinuousEventBridgeRequest
{
    public ContinuousEventBridgeRequest(
        string driverId,
        Rational previousRootTurns,
        Rational currentRootTurns,
        IEnumerable<PeriodicPhaseEventDefinition> eventDefinitions,
        int maxEventOccurrences,
        string determinismProfile = ContinuousEventBridgeContract.DefaultDeterminismProfile)
    {
        DriverId = driverId ?? string.Empty;
        PreviousRootTurns = previousRootTurns;
        CurrentRootTurns = currentRootTurns;
        EventDefinitions = (eventDefinitions ?? throw new ArgumentNullException(nameof(eventDefinitions)))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ThenBy(item => item.SourceDofId, StringComparer.Ordinal)
            .ThenBy(item => item.PeriodTurns)
            .ThenBy(item => item.CrossingPhaseTurns)
            .ThenBy(item => item.TraversalPolicy)
            .ToList()
            .AsReadOnly();
        MaxEventOccurrences = maxEventOccurrences;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public string DriverId { get; }
    public Rational PreviousRootTurns { get; }
    public Rational CurrentRootTurns { get; }
    public ReadOnlyCollection<PeriodicPhaseEventDefinition> EventDefinitions { get; }
    public int MaxEventOccurrences { get; }
    public string DeterminismProfile { get; }
}

public sealed class ContinuousEventBridgeFingerprint
{
    public ContinuousEventBridgeFingerprint(
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
    public string SemanticVersion => BackendId + "-v" + BackendVersion;
    public string DeterminismProfile { get; }
}

public sealed class ContinuousEventBridgeSourceObservation
{
    public ContinuousEventBridgeSourceObservation(string candidateId, string artifactHash, string driverId)
    {
        CandidateId = candidateId ?? string.Empty;
        ArtifactHash = artifactHash ?? string.Empty;
        DriverId = driverId ?? string.Empty;
    }

    public string CandidateId { get; }
    public string ArtifactHash { get; }
    public string DriverId { get; }
}

public sealed class NormalizedPeriodicEventDefinition
{
    public NormalizedPeriodicEventDefinition(
        string key,
        string periodicEventDefinitionId,
        string sourceDofId,
        Rational exactSourceCoefficient,
        Rational exactSourcePhase,
        Rational periodTurns,
        Rational normalizedCrossingPhaseTurns,
        PeriodicEventTraversalPolicy traversalPolicy)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        PeriodicEventDefinitionId = periodicEventDefinitionId ?? throw new ArgumentNullException(nameof(periodicEventDefinitionId));
        SourceDofId = sourceDofId ?? throw new ArgumentNullException(nameof(sourceDofId));
        ExactSourceCoefficient = exactSourceCoefficient;
        ExactSourcePhase = exactSourcePhase;
        PeriodTurns = periodTurns;
        NormalizedCrossingPhaseTurns = normalizedCrossingPhaseTurns;
        TraversalPolicy = traversalPolicy;
    }

    public string Key { get; }
    public string PeriodicEventDefinitionId { get; }
    public string SourceDofId { get; }
    public Rational ExactSourceCoefficient { get; }
    public Rational ExactSourcePhase { get; }
    public Rational PeriodTurns { get; }
    public Rational NormalizedCrossingPhaseTurns { get; }
    public PeriodicEventTraversalPolicy TraversalPolicy { get; }
}

public sealed class ContinuousEventBridgeSearchSummary
{
    public ContinuousEventBridgeSearchSummary(
        int definitionCount,
        BigInteger knownTotalOccurrenceCount,
        BigInteger emittedOccurrenceCount,
        BigInteger omittedOccurrenceCount)
    {
        DefinitionCount = definitionCount;
        KnownTotalOccurrenceCount = knownTotalOccurrenceCount;
        EmittedOccurrenceCount = emittedOccurrenceCount;
        OmittedOccurrenceCount = omittedOccurrenceCount;
    }

    public int DefinitionCount { get; }
    public BigInteger KnownTotalOccurrenceCount { get; }
    public BigInteger EmittedOccurrenceCount { get; }
    public BigInteger OmittedOccurrenceCount { get; }
}

public sealed class ContinuousEventBridgePerformance
{
    public ContinuousEventBridgePerformance(
        long definitionNormalizationMicroseconds,
        long sourceBindingMicroseconds,
        long crossingRangeCalculationMicroseconds,
        long occurrenceMaterializationMicroseconds,
        long totalEvaluationMicroseconds)
    {
        DefinitionNormalizationMicroseconds = definitionNormalizationMicroseconds;
        SourceBindingMicroseconds = sourceBindingMicroseconds;
        CrossingRangeCalculationMicroseconds = crossingRangeCalculationMicroseconds;
        OccurrenceMaterializationMicroseconds = occurrenceMaterializationMicroseconds;
        TotalEvaluationMicroseconds = totalEvaluationMicroseconds;
    }

    public long DefinitionNormalizationMicroseconds { get; }
    public long SourceBindingMicroseconds { get; }
    public long CrossingRangeCalculationMicroseconds { get; }
    public long OccurrenceMaterializationMicroseconds { get; }
    public long TotalEvaluationMicroseconds { get; }

    public static ContinuousEventBridgePerformance Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed class ContinuousEventBridgeResult
{
    public ContinuousEventBridgeResult(
        ContinuousEventBridgeSourceObservation source,
        ContinuousEventBridgeRequest normalizedRequest,
        string requestCanonicalRepresentation,
        string eventBridgeRequestId,
        ContinuousEventBridgeFingerprint backendFingerprint,
        ContinuousEventBridgeStatus status,
        bool searchComplete,
        bool resultTruncated,
        IEnumerable<NormalizedPeriodicEventDefinition> normalizedDefinitions,
        IEnumerable<PeriodicEventOccurrence> occurrences,
        ContinuousEventBridgeSearchSummary searchSummary,
        IEnumerable<Diagnostic> diagnostics,
        ContinuousEventBridgePerformance performance)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? string.Empty;
        EventBridgeRequestId = eventBridgeRequestId ?? string.Empty;
        BackendFingerprint = backendFingerprint ?? throw new ArgumentNullException(nameof(backendFingerprint));
        Status = status;
        SearchComplete = searchComplete;
        ResultTruncated = resultTruncated;
        NormalizedDefinitions = (normalizedDefinitions ?? throw new ArgumentNullException(nameof(normalizedDefinitions)))
            .OrderBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        Occurrences = (occurrences ?? throw new ArgumentNullException(nameof(occurrences))).ToList().AsReadOnly();
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        Performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    public ContinuousEventBridgeSourceObservation Source { get; }
    public ContinuousEventBridgeRequest NormalizedRequest { get; }
    public string RequestCanonicalRepresentation { get; }
    public string EventBridgeRequestId { get; }
    public ContinuousEventBridgeFingerprint BackendFingerprint { get; }
    public ContinuousEventBridgeStatus Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public ReadOnlyCollection<NormalizedPeriodicEventDefinition> NormalizedDefinitions { get; }
    public ReadOnlyCollection<PeriodicEventOccurrence> Occurrences { get; }
    public ContinuousEventBridgeSearchSummary SearchSummary { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public ContinuousEventBridgePerformance Performance { get; }
    public bool IsSuccess => Status == ContinuousEventBridgeStatus.Complete && SearchComplete &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public static class ContinuousEventBridgeIdentity
{
    public static string ComputeDefinitionId(
        string sourceCandidateId,
        string driverId,
        string sourceDofId,
        Rational normalizedPeriodTurns,
        Rational normalizedCrossingPhaseTurns,
        PeriodicEventTraversalPolicy traversalPolicy,
        ContinuousEventBridgeFingerprint fingerprint)
    {
        if (fingerprint is null) throw new ArgumentNullException(nameof(fingerprint));
        var canonical = fingerprint.SemanticVersion +
            "|profile=" + LengthPrefixed(fingerprint.DeterminismProfile) +
            "|candidateId=" + LengthPrefixed(sourceCandidateId) +
            "|driverId=" + LengthPrefixed(driverId) +
            "|sourceDofId=" + LengthPrefixed(sourceDofId) +
            "|periodTurns=" + normalizedPeriodTurns +
            "|crossingPhaseTurns=" + normalizedCrossingPhaseTurns +
            "|traversalPolicy=" + traversalPolicy;
        return PeriodicSemanticIdentity.Hash("periodic-event-definition-sha256:", canonical);
    }

    public static string BuildRequestCanonicalRepresentation(
        string sourceCandidateId,
        ContinuousEventBridgeRequest request,
        IEnumerable<NormalizedPeriodicEventDefinition> normalizedDefinitions)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (normalizedDefinitions is null) throw new ArgumentNullException(nameof(normalizedDefinitions));
        var builder = new StringBuilder(ContinuousEventBridgeContract.BackendSemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(request.DeterminismProfile));
        builder.Append("|candidateId=").Append(LengthPrefixed(sourceCandidateId));
        builder.Append("|driverId=").Append(LengthPrefixed(request.DriverId));
        builder.Append("|previousRootTurns=").Append(request.PreviousRootTurns);
        builder.Append("|currentRootTurns=").Append(request.CurrentRootTurns);
        foreach (var definition in normalizedDefinitions
                     .OrderBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            builder.Append("|definition=").Append(LengthPrefixed(definition.Key));
            builder.Append(',').Append(LengthPrefixed(definition.PeriodicEventDefinitionId));
        }

        builder.Append("|maxEventOccurrences=").Append(request.MaxEventOccurrences.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    public static string ComputeRequestId(string canonicalRepresentation) =>
        PeriodicSemanticIdentity.Hash("event-bridge-request-sha256:", canonicalRepresentation);

    public static string ComputeOccurrenceId(
        string periodicEventDefinitionId,
        BigInteger eventOrdinal,
        PeriodicEventTraversalDirection traversalDirection)
    {
        var canonical = ContinuousEventBridgeContract.BackendSemanticVersion +
            "|definitionId=" + LengthPrefixed(periodicEventDefinitionId) +
            "|eventOrdinal=" + eventOrdinal.ToString(CultureInfo.InvariantCulture) +
            "|traversalDirection=" + traversalDirection;
        return PeriodicSemanticIdentity.Hash("periodic-event-occurrence-sha256:", canonical);
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
