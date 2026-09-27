using System;
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalContinuousEventBridgeJson
{
    public byte[] Write(ContinuousEventBridgeResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", ContinuousEventBridgeContract.ResultFormat);
            writer.WriteString("formatVersion", ContinuousEventBridgeContract.ResultFormatVersion);
            WriteSource(writer, result);
            WriteRequest(writer, result);
            WriteBackend(writer, result.BackendFingerprint);
            WriteDefinitions(writer, result);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WriteBoolean("searchComplete", result.SearchComplete);
            writer.WriteBoolean("resultTruncated", result.ResultTruncated);
            WriteSearchSummary(writer, result.SearchSummary);
            WriteOccurrences(writer, result);
            CanonicalSemanticCompilationJson.WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSource(Utf8JsonWriter writer, ContinuousEventBridgeResult result)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("candidateId", result.Source.CandidateId);
        writer.WriteString("artifactHash", result.Source.ArtifactHash);
        writer.WriteString("driverId", result.Source.DriverId);
        writer.WriteEndObject();
    }

    private static void WriteRequest(Utf8JsonWriter writer, ContinuousEventBridgeResult result)
    {
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("eventBridgeRequestId", result.EventBridgeRequestId);
        writer.WritePropertyName("previousRootTurns");
        WriteRational(writer, result.NormalizedRequest.PreviousRootTurns);
        writer.WritePropertyName("currentRootTurns");
        WriteRational(writer, result.NormalizedRequest.CurrentRootTurns);
        writer.WriteNumber("maxEventOccurrences", result.NormalizedRequest.MaxEventOccurrences);
        writer.WriteString("determinismProfile", result.NormalizedRequest.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteBackend(Utf8JsonWriter writer, ContinuousEventBridgeFingerprint fingerprint)
    {
        writer.WritePropertyName("backend");
        writer.WriteStartObject();
        writer.WriteString("id", fingerprint.BackendId);
        writer.WriteString("version", fingerprint.BackendVersion);
        writer.WriteString("semanticVersion", fingerprint.SemanticVersion);
        writer.WriteString("determinismProfile", fingerprint.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteDefinitions(Utf8JsonWriter writer, ContinuousEventBridgeResult result)
    {
        writer.WritePropertyName("definitions");
        writer.WriteStartArray();
        foreach (var definition in result.NormalizedDefinitions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", definition.Key);
            writer.WriteString("periodicEventDefinitionId", definition.PeriodicEventDefinitionId);
            writer.WriteString("sourceDofId", definition.SourceDofId);
            writer.WritePropertyName("exactSourceCoefficient");
            WriteRational(writer, definition.ExactSourceCoefficient);
            writer.WritePropertyName("exactSourcePhase");
            WriteRational(writer, definition.ExactSourcePhase);
            writer.WritePropertyName("periodTurns");
            WriteRational(writer, definition.PeriodTurns);
            writer.WritePropertyName("normalizedCrossingPhaseTurns");
            WriteRational(writer, definition.NormalizedCrossingPhaseTurns);
            writer.WriteString("traversalPolicy", PolicyToWire(definition.TraversalPolicy));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSearchSummary(
        Utf8JsonWriter writer,
        ContinuousEventBridgeSearchSummary summary)
    {
        writer.WritePropertyName("searchSummary");
        writer.WriteStartObject();
        writer.WriteNumber("definitionCount", summary.DefinitionCount);
        writer.WriteString(
            "knownTotalOccurrenceCount",
            summary.KnownTotalOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(
            "emittedOccurrenceCount",
            summary.EmittedOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(
            "omittedOccurrenceCount",
            summary.OmittedOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteOccurrences(Utf8JsonWriter writer, ContinuousEventBridgeResult result)
    {
        writer.WritePropertyName("occurrences");
        writer.WriteStartArray();
        foreach (var occurrence in result.Occurrences)
        {
            writer.WriteStartObject();
            writer.WriteString("occurrenceId", occurrence.OccurrenceId);
            writer.WriteString("key", occurrence.EventKey);
            writer.WriteString("definitionId", occurrence.PeriodicEventDefinitionId);
            writer.WriteString("ordinal", occurrence.EventOrdinal.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("driverId", occurrence.DriverId);
            writer.WritePropertyName("rootTurnsAtCrossing");
            WriteRational(writer, occurrence.RootTurnsAtCrossing);
            writer.WriteString("sourceDofId", occurrence.SourceDofId);
            writer.WritePropertyName("sourceTurnsAtCrossing");
            WriteRational(writer, occurrence.SourceTurnsAtCrossing);
            writer.WriteString("direction", DirectionToWire(occurrence.TraversalDirection));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRational(Utf8JsonWriter writer, Rational value)
    {
        writer.WriteStartObject();
        writer.WriteString("numerator", value.Numerator.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("denominator", value.Denominator.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static string StatusToWire(ContinuousEventBridgeStatus status) => status switch
    {
        ContinuousEventBridgeStatus.Complete => "complete",
        ContinuousEventBridgeStatus.IncompleteBudget => "incompleteBudget",
        ContinuousEventBridgeStatus.InvalidInput => "invalidInput",
        ContinuousEventBridgeStatus.Unsupported => "unsupported",
        ContinuousEventBridgeStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown continuous event bridge status."),
    };

    private static string PolicyToWire(PeriodicEventTraversalPolicy policy) => policy switch
    {
        PeriodicEventTraversalPolicy.ForwardOnly => "forwardOnly",
        _ => throw new InvalidOperationException("Unknown periodic event traversal policy."),
    };

    private static string DirectionToWire(PeriodicEventTraversalDirection direction) => direction switch
    {
        PeriodicEventTraversalDirection.Forward => "forward",
        _ => throw new InvalidOperationException("Unknown periodic event traversal direction."),
    };
}
