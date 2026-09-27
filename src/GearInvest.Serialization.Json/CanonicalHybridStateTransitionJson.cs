using System;
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalHybridStateTransitionJson
{
    public byte[] Write(HybridStateTransitionResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", HybridStateTransitionContract.ResultFormat);
            writer.WriteString("formatVersion", HybridStateTransitionContract.ResultFormatVersion);
            WriteSource(writer, result);
            WriteEventInput(writer, result);
            WriteTransitionPlan(writer, result.TransitionPlan);
            WriteRequest(writer, result);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WriteBoolean("searchComplete", result.SearchComplete);
            writer.WriteBoolean("resultTruncated", result.ResultTruncated);
            WriteSummary(writer, result.Summary);
            writer.WritePropertyName("initialSnapshot");
            WriteSnapshot(writer, result.InitialSnapshot);
            WriteApplications(writer, result);
            writer.WritePropertyName("checkpointSnapshot");
            if (result.CheckpointSnapshot is null) writer.WriteNullValue();
            else WriteSnapshot(writer, result.CheckpointSnapshot);
            writer.WritePropertyName("finalSnapshot");
            if (result.FinalSnapshot is null) writer.WriteNullValue();
            else WriteSnapshot(writer, result.FinalSnapshot);
            CanonicalSemanticCompilationJson.WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSource(Utf8JsonWriter writer, HybridStateTransitionResult result)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("mechanismCandidateId", result.EventInput.Source.CandidateId);
        writer.WriteString("mechanismArtifactHash", result.EventInput.Source.ArtifactHash);
        writer.WriteString("driverId", result.EventInput.Source.DriverId);
        writer.WriteEndObject();
    }

    private static void WriteEventInput(Utf8JsonWriter writer, HybridStateTransitionResult result)
    {
        writer.WritePropertyName("eventInput");
        writer.WriteStartObject();
        writer.WriteString("eventBridgeRequestId", result.EventInput.EventBridgeRequestId);
        writer.WritePropertyName("eventDefinitionIds");
        writer.WriteStartArray();
        foreach (var definition in result.EventInput.NormalizedDefinitions)
        {
            writer.WriteStringValue(definition.PeriodicEventDefinitionId);
        }

        writer.WriteEndArray();
        writer.WriteString("eventResultStatus", EventStatusToWire(result.EventInput.Status));
        writer.WritePropertyName("previousRootTurns");
        WriteRational(writer, result.EventInput.NormalizedRequest.PreviousRootTurns);
        writer.WritePropertyName("currentRootTurns");
        WriteRational(writer, result.EventInput.NormalizedRequest.CurrentRootTurns);
        writer.WriteString("occurrenceCount", result.EventInput.Occurrences.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteTransitionPlan(Utf8JsonWriter writer, IndexedStateTransitionPlan plan)
    {
        writer.WritePropertyName("transitionPlan");
        writer.WriteStartObject();
        writer.WriteString("indexedStateTransitionPlanId", plan.IndexedStateTransitionPlanId);
        writer.WritePropertyName("stateDefinitions");
        writer.WriteStartArray();
        foreach (var definition in plan.OrderedStateDefinitions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", definition.Key);
            writer.WriteString("stateDefinitionId", definition.CyclicStateDefinitionId);
            writer.WriteString("cycleLength", definition.CycleLength.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("transitions");
        writer.WriteStartArray();
        foreach (var transition in plan.OrderedTransitionDefinitions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", transition.Key);
            writer.WriteString("transitionDefinitionId", transition.IndexedStateTransitionDefinitionId);
            writer.WriteString("sourceEventDefinitionId", transition.SourcePeriodicEventDefinitionId);
            writer.WriteString("targetStateDefinitionId", transition.TargetCyclicStateDefinitionId);
            writer.WriteString("signedDelta", transition.SignedDelta.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRequest(Utf8JsonWriter writer, HybridStateTransitionResult result)
    {
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("hybridStateAdvanceRequestId", result.HybridStateAdvanceRequestId);
        writer.WriteNumber("maxAppliedOccurrences", result.NormalizedRequest.MaxAppliedOccurrences);
        writer.WriteString("determinismProfile", result.NormalizedRequest.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteSummary(Utf8JsonWriter writer, HybridStateTransitionSummary summary)
    {
        writer.WritePropertyName("summary");
        writer.WriteStartObject();
        writer.WriteString("knownOccurrenceCount", summary.KnownOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("appliedOccurrenceCount", summary.AppliedOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("omittedOccurrenceCount", summary.OmittedOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("transitionApplicationCount", summary.TransitionApplicationCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteApplications(Utf8JsonWriter writer, HybridStateTransitionResult result)
    {
        writer.WritePropertyName("applications");
        writer.WriteStartArray();
        foreach (var application in result.Applications)
        {
            writer.WriteStartObject();
            writer.WriteString("transitionApplicationId", application.TransitionApplicationId);
            writer.WriteString("occurrenceId", application.PeriodicEventOccurrenceId);
            writer.WriteString("periodicEventDefinitionId", application.PeriodicEventDefinitionId);
            writer.WriteString("transitionDefinitionId", application.TransitionDefinitionId);
            writer.WriteString("ordinal", application.EventOrdinal.ToString(CultureInfo.InvariantCulture));
            writer.WritePropertyName("rootTurnsAtApplication");
            WriteRational(writer, application.RootTurnsAtApplication);
            writer.WriteString("targetStateDefinitionId", application.TargetStateDefinitionId);
            writer.WriteString("beforeIndex", application.BeforeIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("signedDelta", application.SignedDelta.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("afterIndex", application.AfterIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSnapshot(Utf8JsonWriter writer, HybridStateSnapshot snapshot)
    {
        writer.WriteStartObject();
        writer.WriteString("hybridStateSnapshotId", snapshot.HybridStateSnapshotId);
        writer.WriteString("sourceMechanismCandidateId", snapshot.SourceMechanismCandidateId);
        writer.WriteString("driverId", snapshot.DriverId);
        writer.WritePropertyName("rootTurns");
        WriteRational(writer, snapshot.ExactRootTurns);
        writer.WritePropertyName("states");
        writer.WriteStartArray();
        foreach (var state in snapshot.States)
        {
            writer.WriteStartObject();
            writer.WriteString("stateDefinitionId", state.StateDefinitionId);
            writer.WriteString("currentIndex", state.CurrentIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("cursors");
        writer.WriteStartArray();
        foreach (var cursor in snapshot.EventCursors)
        {
            writer.WriteStartObject();
            writer.WriteString("periodicEventDefinitionId", cursor.PeriodicEventDefinitionId);
            writer.WriteString("lastAppliedOrdinal", cursor.LastAppliedOrdinal.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRational(Utf8JsonWriter writer, Rational value)
    {
        writer.WriteStartObject();
        writer.WriteString("numerator", value.Numerator.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("denominator", value.Denominator.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static string StatusToWire(HybridStateTransitionStatus status) => status switch
    {
        HybridStateTransitionStatus.Complete => "complete",
        HybridStateTransitionStatus.IncompleteBudget => "incompleteBudget",
        HybridStateTransitionStatus.InvalidInput => "invalidInput",
        HybridStateTransitionStatus.Unsupported => "unsupported",
        HybridStateTransitionStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown hybrid state transition status."),
    };

    private static string EventStatusToWire(ContinuousEventBridgeStatus status) => status switch
    {
        ContinuousEventBridgeStatus.Complete => "complete",
        ContinuousEventBridgeStatus.IncompleteBudget => "incompleteBudget",
        ContinuousEventBridgeStatus.InvalidInput => "invalidInput",
        ContinuousEventBridgeStatus.Unsupported => "unsupported",
        ContinuousEventBridgeStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown continuous event bridge status."),
    };
}
