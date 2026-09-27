using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class ExactForwardPeriodicEventBridge
{
    private readonly ContinuousEventBridgeFingerprint _fingerprint = new(
        ContinuousEventBridgeContract.BackendId,
        ContinuousEventBridgeContract.BackendVersion,
        ContinuousEventBridgeContract.DefaultDeterminismProfile);

    public ContinuousEventBridgeResult Evaluate(
        string sourceCandidateId,
        string sourceArtifactHash,
        KinematicSolution solution,
        ResolvedPlayback playback,
        ContinuousEventBridgeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (sourceCandidateId is null) throw new ArgumentNullException(nameof(sourceCandidateId));
        if (sourceArtifactHash is null) throw new ArgumentNullException(nameof(sourceArtifactHash));
        if (solution is null) throw new ArgumentNullException(nameof(solution));
        if (playback is null) throw new ArgumentNullException(nameof(playback));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var totalWatch = Stopwatch.StartNew();
        var normalizationMicroseconds = 0L;
        var bindingMicroseconds = 0L;
        var rangeMicroseconds = 0L;
        var materializationMicroseconds = 0L;
        var diagnostics = new List<Diagnostic>();
        var invalid = false;
        var unsupported = false;
        var source = new ContinuousEventBridgeSourceObservation(
            sourceCandidateId,
            sourceArtifactHash,
            request.DriverId);

        var bindingWatch = Stopwatch.StartNew();
        PlaybackDriver? driver = null;
        if (string.IsNullOrWhiteSpace(request.DriverId))
        {
            invalid = true;
            diagnostics.Add(Error(DiagnosticCodes.EventBridgeUnknownDriver, "A driver ID is required."));
        }
        else if (playback.Drivers.Count != 1)
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.EventBridgeMultipleDriversUnsupported,
                "The forward event bridge profile requires exactly one resolved driver.",
                request.DriverId));
        }
        else
        {
            driver = playback.Drivers.SingleOrDefault(item =>
                StringComparer.Ordinal.Equals(item.Id, request.DriverId));
            if (driver is null)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeUnknownDriver,
                    "The requested driver does not exist in resolved playback.",
                    request.DriverId));
            }
            else if (!StringComparer.Ordinal.Equals(driver.RootDofId, solution.RootDofId))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeUnknownDriver,
                    "The requested driver does not reference the canonical Kinematic root.",
                    request.DriverId));
            }
        }

        bindingWatch.Stop();
        bindingMicroseconds = ToMicroseconds(bindingWatch.ElapsedTicks);

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                ContinuousEventBridgeContract.DefaultDeterminismProfile))
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.EventBridgeUnsupportedProfile,
                "Unsupported event-bridge determinism profile '" + request.DeterminismProfile + "'."));
        }

        if (request.MaxEventOccurrences <= 0)
        {
            invalid = true;
            diagnostics.Add(Error(
                DiagnosticCodes.EventBridgeInvalidBudget,
                "maxEventOccurrences must be greater than zero."));
        }

        if (request.EventDefinitions.Count == 0)
        {
            invalid = true;
            diagnostics.Add(Error(
                DiagnosticCodes.EventBridgeInvalidDefinition,
                "At least one periodic event definition is required."));
        }

        if (request.CurrentRootTurns < request.PreviousRootTurns)
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.EventBridgeReverseUnsupported,
                "ForwardOnly does not support decreasing root coordinates."));
        }

        var normalizationWatch = Stopwatch.StartNew();
        var normalizedDefinitions = new List<NormalizedPeriodicEventDefinition>();
        var normalizedRequestDefinitions = new List<PeriodicPhaseEventDefinition>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var mechanicalDefinitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in request.EventDefinitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Key))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeInvalidDefinition,
                    "A periodic event definition key is required."));
                continue;
            }

            if (!keys.Add(definition.Key))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeDuplicateDefinitionKey,
                    "Periodic event definition key '" + definition.Key + "' is duplicated.",
                    definition.Key));
                continue;
            }

            if (!Enum.IsDefined(typeof(PeriodicEventTraversalPolicy), definition.TraversalPolicy) ||
                definition.TraversalPolicy != PeriodicEventTraversalPolicy.ForwardOnly)
            {
                unsupported = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeTraversalUnsupported,
                    "The requested periodic event traversal policy is not supported.",
                    definition.Key));
                continue;
            }

            if (definition.PeriodTurns <= Rational.Zero)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeNonPositivePeriod,
                    "Periodic event periodTurns must be strictly positive.",
                    definition.Key));
                continue;
            }

            if (string.IsNullOrWhiteSpace(definition.SourceDofId) ||
                !solution.TryGetState(definition.SourceDofId, out var state) || state is null)
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeUnknownSourceDof,
                    "Periodic event definition references an unknown source DOF.",
                    definition.SourceDofId));
                continue;
            }

            var normalizedPhase = NormalizePhase(definition.CrossingPhaseTurns, definition.PeriodTurns);
            var mechanicalKey = definition.SourceDofId + "\u001f" + definition.PeriodTurns + "\u001f" +
                normalizedPhase + "\u001f" + definition.TraversalPolicy;
            if (!mechanicalDefinitions.Add(mechanicalKey))
            {
                invalid = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeDuplicateNormalizedDefinition,
                    "Two keys describe the same normalized periodic event definition.",
                    definition.Key));
                continue;
            }

            if (state.Coefficient <= Rational.Zero)
            {
                unsupported = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeSourceCoefficientUnsupported,
                    "ForwardOnly requires a strictly positive source coefficient.",
                    definition.SourceDofId));
            }

            var definitionId = ContinuousEventBridgeIdentity.ComputeDefinitionId(
                sourceCandidateId,
                request.DriverId,
                definition.SourceDofId,
                definition.PeriodTurns,
                normalizedPhase,
                definition.TraversalPolicy,
                _fingerprint);
            normalizedDefinitions.Add(new NormalizedPeriodicEventDefinition(
                definition.Key,
                definitionId,
                definition.SourceDofId,
                state.Coefficient,
                state.PhaseOffset,
                definition.PeriodTurns,
                normalizedPhase,
                definition.TraversalPolicy));
            normalizedRequestDefinitions.Add(new PeriodicPhaseEventDefinition(
                definition.Key,
                definition.SourceDofId,
                definition.PeriodTurns,
                normalizedPhase,
                definition.TraversalPolicy));
        }

        normalizedDefinitions = normalizedDefinitions
            .OrderBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
        var normalizedRequest = new ContinuousEventBridgeRequest(
            request.DriverId,
            request.PreviousRootTurns,
            request.CurrentRootTurns,
            normalizedRequestDefinitions,
            request.MaxEventOccurrences,
            request.DeterminismProfile);
        normalizationWatch.Stop();
        normalizationMicroseconds = ToMicroseconds(normalizationWatch.ElapsedTicks);

        var requestCanonical = invalid
            ? string.Empty
            : ContinuousEventBridgeIdentity.BuildRequestCanonicalRepresentation(
                sourceCandidateId,
                normalizedRequest,
                normalizedDefinitions);
        var requestId = requestCanonical.Length == 0
            ? string.Empty
            : ContinuousEventBridgeIdentity.ComputeRequestId(requestCanonical);

        if (invalid || unsupported)
        {
            totalWatch.Stop();
            return Terminal(
                source,
                normalizedRequest,
                requestCanonical,
                requestId,
                invalid ? ContinuousEventBridgeStatus.InvalidInput : ContinuousEventBridgeStatus.Unsupported,
                normalizedDefinitions,
                diagnostics,
                new ContinuousEventBridgePerformance(
                    normalizationMicroseconds,
                    bindingMicroseconds,
                    0,
                    0,
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(Error(
                DiagnosticCodes.EventBridgeCancelled,
                "Periodic event evaluation was cancelled before range evaluation."));
            totalWatch.Stop();
            return Terminal(
                source,
                normalizedRequest,
                requestCanonical,
                requestId,
                ContinuousEventBridgeStatus.Cancelled,
                normalizedDefinitions,
                diagnostics,
                new ContinuousEventBridgePerformance(
                    normalizationMicroseconds,
                    bindingMicroseconds,
                    0,
                    0,
                    ToMicroseconds(totalWatch.ElapsedTicks)));
        }

        var rangeWatch = Stopwatch.StartNew();
        var cursors = new List<DefinitionCursor>();
        var knownTotal = BigInteger.Zero;
        foreach (var definition in normalizedDefinitions)
        {
            var sourceAtPrevious =
                (definition.ExactSourceCoefficient * normalizedRequest.PreviousRootTurns) +
                definition.ExactSourcePhase;
            var sourceAtCurrent =
                (definition.ExactSourceCoefficient * normalizedRequest.CurrentRootTurns) +
                definition.ExactSourcePhase;
            var firstOrdinal = Floor(
                (sourceAtPrevious - definition.NormalizedCrossingPhaseTurns) /
                definition.PeriodTurns) + BigInteger.One;
            var lastOrdinal = Floor(
                (sourceAtCurrent - definition.NormalizedCrossingPhaseTurns) /
                definition.PeriodTurns);
            if (firstOrdinal > lastOrdinal)
            {
                continue;
            }

            var count = lastOrdinal - firstOrdinal + BigInteger.One;
            knownTotal += count;
            cursors.Add(new DefinitionCursor(definition, firstOrdinal, lastOrdinal));
        }

        rangeWatch.Stop();
        rangeMicroseconds = ToMicroseconds(rangeWatch.ElapsedTicks);

        var materializationWatch = Stopwatch.StartNew();
        var occurrences = new List<PeriodicEventOccurrence>();
        var cancelled = false;
        while (occurrences.Count < normalizedRequest.MaxEventOccurrences)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                diagnostics.Add(Error(
                    DiagnosticCodes.EventBridgeCancelled,
                    "Periodic event evaluation observed cancellation while emitting occurrences."));
                break;
            }

            DefinitionCursor? next = null;
            Rational nextRoot = default;
            foreach (var cursor in cursors.Where(item => item.HasValue))
            {
                var root = cursor.RootTurnsAtCurrent;
                if (next is null || root < nextRoot ||
                    (root == nextRoot && StringComparer.Ordinal.Compare(
                        cursor.Definition.PeriodicEventDefinitionId,
                        next.Definition.PeriodicEventDefinitionId) < 0) ||
                    (root == nextRoot && StringComparer.Ordinal.Equals(
                        cursor.Definition.PeriodicEventDefinitionId,
                        next.Definition.PeriodicEventDefinitionId) &&
                     cursor.CurrentOrdinal < next.CurrentOrdinal))
                {
                    next = cursor;
                    nextRoot = root;
                }
            }

            if (next is null)
            {
                break;
            }

            var definition = next.Definition;
            var sourceAtCrossing = next.SourceTurnsAtCurrent;
            var rootAtCrossing = next.RootTurnsAtCurrent;
            if (!(normalizedRequest.PreviousRootTurns < rootAtCrossing &&
                  rootAtCrossing <= normalizedRequest.CurrentRootTurns))
            {
                throw new InvalidOperationException("Exact event crossing escaped the normalized root interval.");
            }

            var occurrenceId = ContinuousEventBridgeIdentity.ComputeOccurrenceId(
                definition.PeriodicEventDefinitionId,
                next.CurrentOrdinal,
                PeriodicEventTraversalDirection.Forward);
            occurrences.Add(new PeriodicEventOccurrence(
                occurrenceId,
                definition.Key,
                definition.PeriodicEventDefinitionId,
                next.CurrentOrdinal,
                normalizedRequest.DriverId,
                rootAtCrossing,
                definition.SourceDofId,
                sourceAtCrossing,
                PeriodicEventTraversalDirection.Forward));
            next.Advance();
        }

        materializationWatch.Stop();
        materializationMicroseconds = ToMicroseconds(materializationWatch.ElapsedTicks);
        var emitted = new BigInteger(occurrences.Count);
        var omitted = BigInteger.Max(BigInteger.Zero, knownTotal - emitted);
        var status = cancelled
            ? ContinuousEventBridgeStatus.Cancelled
            : omitted > BigInteger.Zero
                ? ContinuousEventBridgeStatus.IncompleteBudget
                : ContinuousEventBridgeStatus.Complete;
        var searchComplete = status == ContinuousEventBridgeStatus.Complete;
        var truncated = omitted > BigInteger.Zero;
        if (status == ContinuousEventBridgeStatus.IncompleteBudget)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.EventBridgeResultTruncated,
                DiagnosticSeverity.Warning,
                "The globally ordered occurrence sequence exceeded maxEventOccurrences."));
        }

        totalWatch.Stop();
        return new ContinuousEventBridgeResult(
            source,
            normalizedRequest,
            requestCanonical,
            requestId,
            _fingerprint,
            status,
            searchComplete,
            truncated,
            normalizedDefinitions,
            occurrences,
            new ContinuousEventBridgeSearchSummary(
                normalizedDefinitions.Count,
                knownTotal,
                emitted,
                omitted),
            diagnostics,
            new ContinuousEventBridgePerformance(
                normalizationMicroseconds,
                bindingMicroseconds,
                rangeMicroseconds,
                materializationMicroseconds,
                ToMicroseconds(totalWatch.ElapsedTicks)));
    }

    public ContinuousEventBridgeResult InvalidArtifact(
        string sourceCandidateId,
        string sourceArtifactHash,
        ContinuousEventBridgeRequest request,
        IEnumerable<Diagnostic> trustBoundaryDiagnostics)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var diagnostics = new List<Diagnostic>(trustBoundaryDiagnostics ?? Array.Empty<Diagnostic>())
        {
            Error(
                DiagnosticCodes.EventBridgeArtifactInvalid,
                "The mechanism artifact failed identity or strict validation before event evaluation."),
        };
        return Terminal(
            new ContinuousEventBridgeSourceObservation(sourceCandidateId, sourceArtifactHash, request.DriverId),
            request,
            string.Empty,
            string.Empty,
            ContinuousEventBridgeStatus.InvalidInput,
            Array.Empty<NormalizedPeriodicEventDefinition>(),
            diagnostics,
            ContinuousEventBridgePerformance.Empty);
    }

    private ContinuousEventBridgeResult Terminal(
        ContinuousEventBridgeSourceObservation source,
        ContinuousEventBridgeRequest request,
        string requestCanonical,
        string requestId,
        ContinuousEventBridgeStatus status,
        IEnumerable<NormalizedPeriodicEventDefinition> definitions,
        IEnumerable<Diagnostic> diagnostics,
        ContinuousEventBridgePerformance performance)
    {
        var definitionList = definitions.ToList();
        return new ContinuousEventBridgeResult(
            source,
            request,
            requestCanonical,
            requestId,
            _fingerprint,
            status,
            false,
            false,
            definitionList,
            Array.Empty<PeriodicEventOccurrence>(),
            new ContinuousEventBridgeSearchSummary(
                definitionList.Count,
                BigInteger.Zero,
                BigInteger.Zero,
                BigInteger.Zero),
            diagnostics,
            performance);
    }

    private static Rational NormalizePhase(Rational phase, Rational period)
    {
        return phase - (new Rational(Floor(phase / period)) * period);
    }

    private static BigInteger Floor(Rational value)
    {
        var quotient = BigInteger.DivRem(value.Numerator, value.Denominator, out var remainder);
        return value.Numerator.Sign < 0 && !remainder.IsZero
            ? quotient - BigInteger.One
            : quotient;
    }

    private static long ToMicroseconds(long elapsedTicks)
    {
        return (long)((decimal)elapsedTicks * 1_000_000m / Stopwatch.Frequency);
    }

    private static Diagnostic Error(string code, string message, string? subjectId = null) =>
        new(code, DiagnosticSeverity.Error, message, subjectId);

    private sealed class DefinitionCursor
    {
        public DefinitionCursor(
            NormalizedPeriodicEventDefinition definition,
            BigInteger firstOrdinal,
            BigInteger lastOrdinal)
        {
            Definition = definition;
            CurrentOrdinal = firstOrdinal;
            LastOrdinal = lastOrdinal;
        }

        public NormalizedPeriodicEventDefinition Definition { get; }
        public BigInteger CurrentOrdinal { get; private set; }
        public BigInteger LastOrdinal { get; }
        public bool HasValue => CurrentOrdinal <= LastOrdinal;
        public Rational SourceTurnsAtCurrent =>
            Definition.NormalizedCrossingPhaseTurns +
            (new Rational(CurrentOrdinal) * Definition.PeriodTurns);
        public Rational RootTurnsAtCurrent =>
            (SourceTurnsAtCurrent - Definition.ExactSourcePhase) /
            Definition.ExactSourceCoefficient;

        public void Advance()
        {
            CurrentOrdinal += BigInteger.One;
        }
    }
}
