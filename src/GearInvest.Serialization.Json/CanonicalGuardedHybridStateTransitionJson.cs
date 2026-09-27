using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalGuardedHybridStateTransitionJson
{
    public byte[] Write(ComputedGuardedHybridStateTransitionResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", ComputedGuardedHybridStateTransitionContract.ResultFormat);
            writer.WriteString("formatVersion", ComputedGuardedHybridStateTransitionContract.ResultFormatVersion);
            WriteSource(writer, result);
            WriteEventInput(writer, result);
            WritePlan(writer, result.TransitionPlan);
            WriteRequest(writer, result);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WriteBoolean("searchComplete", result.SearchComplete);
            writer.WriteBoolean("resultTruncated", result.ResultTruncated);
            WriteSummary(writer, result.Summary);
            writer.WritePropertyName("initialSnapshot");
            WriteSnapshot(writer, result.InitialSnapshot);
            WriteComputedEvaluations(writer, result.ComputedEvaluations);
            WriteOccurrenceGroups(writer, result.OccurrenceGroups);
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

    public byte[] Write(CompositeGuardedHybridStateTransitionResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", CompositeGuardedHybridStateTransitionContract.ResultFormat);
            writer.WriteString("formatVersion", CompositeGuardedHybridStateTransitionContract.ResultFormatVersion);
            WriteSource(writer, result);
            WriteEventInput(writer, result);
            WritePlan(writer, result.TransitionPlan);
            WriteRequest(writer, result);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WriteBoolean("searchComplete", result.SearchComplete);
            writer.WriteBoolean("resultTruncated", result.ResultTruncated);
            WriteSummary(writer, result.Summary);
            writer.WritePropertyName("initialSnapshot");
            WriteSnapshot(writer, result.InitialSnapshot);
            WriteOccurrenceGroups(writer, result.OccurrenceGroups);
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

    public byte[] Write(GuardedHybridStateTransitionResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", GuardedHybridStateTransitionContract.ResultFormat);
            writer.WriteString("formatVersion", GuardedHybridStateTransitionContract.ResultFormatVersion);
            WriteSource(writer, result);
            WriteEventInput(writer, result);
            WritePlan(writer, result.TransitionPlan);
            WriteRequest(writer, result);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WriteBoolean("searchComplete", result.SearchComplete);
            writer.WriteBoolean("resultTruncated", result.ResultTruncated);
            WriteSummary(writer, result.Summary);
            writer.WritePropertyName("initialSnapshot");
            WriteSnapshot(writer, result.InitialSnapshot);
            WriteOccurrenceGroups(writer, result.OccurrenceGroups);
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

    private static void WriteSource(Utf8JsonWriter writer, ComputedGuardedHybridStateTransitionResult result)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("mechanismCandidateId", result.EventInput.Source.CandidateId);
        writer.WriteString("mechanismArtifactHash", result.EventInput.Source.ArtifactHash);
        writer.WriteString("driverId", result.EventInput.Source.DriverId);
        writer.WriteEndObject();
    }

    private static void WriteSource(Utf8JsonWriter writer, GuardedHybridStateTransitionResult result)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("mechanismCandidateId", result.EventInput.Source.CandidateId);
        writer.WriteString("mechanismArtifactHash", result.EventInput.Source.ArtifactHash);
        writer.WriteString("driverId", result.EventInput.Source.DriverId);
        writer.WriteEndObject();
    }

    private static void WriteSource(Utf8JsonWriter writer, CompositeGuardedHybridStateTransitionResult result)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("mechanismCandidateId", result.EventInput.Source.CandidateId);
        writer.WriteString("mechanismArtifactHash", result.EventInput.Source.ArtifactHash);
        writer.WriteString("driverId", result.EventInput.Source.DriverId);
        writer.WriteEndObject();
    }

    private static void WriteEventInput(Utf8JsonWriter writer, ComputedGuardedHybridStateTransitionResult result)
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

    private static void WriteEventInput(Utf8JsonWriter writer, GuardedHybridStateTransitionResult result)
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

    private static void WriteEventInput(Utf8JsonWriter writer, CompositeGuardedHybridStateTransitionResult result)
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

    private static void WritePlan(Utf8JsonWriter writer, ComputedGuardedIndexedStateTransitionPlan plan)
    {
        writer.WritePropertyName("transitionPlan");
        writer.WriteStartObject();
        writer.WriteString("guardedIndexedTransitionPlanId", plan.GuardedIndexedTransitionPlanId);
        writer.WriteString("determinismProfile", plan.DeterminismProfile);
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
        writer.WritePropertyName("computedIndexedValueDefinitions");
        writer.WriteStartArray();
        foreach (var definition in plan.OrderedComputedDefinitions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", definition.Key);
            writer.WriteString("computedIndexedValueDefinitionId", definition.ComputedIndexedValueDefinitionId);
            writer.WriteString("outputCardinality", definition.OutputCardinality.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("matchPolicy", MatchPolicyToWire(definition.MatchPolicy));
            writer.WriteString("semanticProfile", definition.SemanticProfile);
            writer.WritePropertyName("cases");
            writer.WriteStartArray();
            foreach (var item in definition.OrderedCases)
            {
                writer.WriteStartObject();
                writer.WriteString("key", item.Key);
                writer.WriteString("computedIndexedValueCaseId", item.ComputedIndexedValueCaseId);
                writer.WriteString("guardId", item.Guard.IndexedStateGuardId);
                writer.WriteString("outputIndex", item.OutputIndex.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("lookupTables");
        writer.WriteStartArray();
        foreach (var table in plan.OrderedLookupTables)
        {
            writer.WriteStartObject();
            writer.WriteString("key", table.Key);
            writer.WriteString("computedSelectorIndexedLookupTableId", table.ComputedSelectorIndexedLookupTableId);
            writer.WriteString("semanticProfile", table.SemanticProfile);
            writer.WritePropertyName("selectors");
            writer.WriteStartArray();
            foreach (var selector in table.OrderedSelectors)
            {
                WriteComputedSelector(writer, selector);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (var entry in table.OrderedEntries)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("coordinates");
                writer.WriteStartArray();
                foreach (var coordinate in entry.OrderedCoordinates)
                {
                    writer.WriteStartObject();
                    writer.WriteString("selectorKind", SelectorKindToWire(coordinate.SelectorKind));
                    writer.WriteString("referenceId", coordinate.ReferenceId);
                    writer.WriteString("index", coordinate.Index.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteString("value", entry.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("constraints");
        writer.WriteStartArray();
        foreach (var constraint in plan.OrderedConstraints)
        {
            writer.WriteStartObject();
            writer.WriteString("key", constraint.Key);
            writer.WriteString("computedSelectorIndexedStateConstraintId",
                constraint.ComputedSelectorIndexedStateConstraintId);
            writer.WriteString("subjectStateDefinitionId", constraint.SubjectStateDefinitionId);
            writer.WriteString("lookupTableId", constraint.LookupTableId);
            writer.WritePropertyName("selectors");
            writer.WriteStartArray();
            foreach (var selector in constraint.OrderedSelectors)
            {
                WriteComputedSelector(writer, selector);
            }

            writer.WriteEndArray();
            writer.WriteString("comparisonKind", ComparisonToWire(constraint.ComparisonKind));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("unconditionalTransitions");
        writer.WriteStartArray();
        foreach (var transition in plan.OrderedUnconditionalTransitions)
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
        writer.WritePropertyName("guards");
        writer.WriteStartArray();
        foreach (var guard in plan.OrderedGuards)
        {
            writer.WriteStartObject();
            writer.WriteString("key", guard.Key);
            writer.WriteString("indexedStateGuardId", guard.IndexedStateGuardId);
            switch (guard)
            {
                case IndexedStateModuloComparisonGuard modulo:
                    writer.WriteString("guardKind", "moduloComparison");
                    writer.WriteString("subjectStateDefinitionId", modulo.SubjectStateDefinitionId);
                    writer.WriteString("modulus", modulo.Modulus.ToString(CultureInfo.InvariantCulture));
                    writer.WriteString("comparisonKind", ModuloComparisonToWire(modulo.ComparisonKind));
                    writer.WriteString("residue", modulo.Residue.ToString(CultureInfo.InvariantCulture));
                    writer.WriteString("semanticProfile", modulo.SemanticProfile);
                    break;
                case ComputedSelectorIndexedLookupComparisonGuard lookup:
                    writer.WriteString("guardKind", "computedSelectorLookupComparison");
                    writer.WriteString("subjectStateDefinitionId", lookup.SubjectStateDefinitionId);
                    writer.WriteString("signedSubjectOffset", lookup.SignedSubjectOffset.ToString(CultureInfo.InvariantCulture));
                    writer.WriteString("comparisonKind", ComparisonToWire(lookup.ComparisonKind));
                    writer.WriteString("lookupTableId", lookup.LookupTableId);
                    writer.WritePropertyName("selectors");
                    writer.WriteStartArray();
                    foreach (var selector in lookup.OrderedSelectors)
                    {
                        WriteComputedSelector(writer, selector);
                    }

                    writer.WriteEndArray();
                    break;
                case IndexedStateConstantComparisonGuard constant:
                    writer.WriteString("guardKind", "stateConstantComparison");
                    writer.WriteString("subjectStateDefinitionId", constant.SubjectStateDefinitionId);
                    writer.WriteString("signedSubjectOffset", constant.SignedSubjectOffset.ToString(CultureInfo.InvariantCulture));
                    writer.WriteString("comparisonKind", ComparisonToWire(constant.ComparisonKind));
                    writer.WriteString("constant", constant.Constant.ToString(CultureInfo.InvariantCulture));
                    break;
                case IndexedStateAllOfGuard allOf:
                    writer.WriteString("guardKind", "allOf");
                    writer.WritePropertyName("orderedChildGuardIds");
                    writer.WriteStartArray();
                    foreach (var child in allOf.OrderedChildren)
                    {
                        writer.WriteStringValue(child.IndexedStateGuardId);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    throw new InvalidOperationException("Unknown computed guarded-state guard kind.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("effects");
        writer.WriteStartArray();
        foreach (var effect in plan.OrderedEffects)
        {
            writer.WriteStartObject();
            writer.WriteString("key", effect.Key);
            writer.WriteString("guardedStateEffectId", effect.GuardedStateEffectId);
            writer.WriteString("targetStateDefinitionId", effect.TargetStateDefinitionId);
            writer.WriteString("effectKind", EffectToWire(effect.EffectKind));
            writer.WriteString("operand", effect.Operand.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("branches");
        writer.WriteStartArray();
        foreach (var branch in plan.OrderedBranches)
        {
            writer.WriteStartObject();
            writer.WriteString("key", branch.Key);
            writer.WriteString("guardedTransitionBranchId", branch.GuardedTransitionBranchId);
            writer.WriteString("guardId", branch.Guard.IndexedStateGuardId);
            writer.WritePropertyName("orderedEffectIds");
            writer.WriteStartArray();
            foreach (var effect in branch.OrderedEffects)
            {
                writer.WriteStringValue(effect.GuardedStateEffectId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("choices");
        writer.WriteStartArray();
        foreach (var choice in plan.OrderedGuardedChoices)
        {
            writer.WriteStartObject();
            writer.WriteString("key", choice.Key);
            writer.WriteString("guardedTransitionChoiceId", choice.GuardedTransitionChoiceId);
            writer.WriteString("sourceEventDefinitionId", choice.SourcePeriodicEventDefinitionId);
            writer.WriteString("matchPolicy", MatchPolicyToWire(choice.MatchPolicy));
            writer.WritePropertyName("orderedBranchIds");
            writer.WriteStartArray();
            foreach (var branch in choice.OrderedBranches)
            {
                writer.WriteStringValue(branch.GuardedTransitionBranchId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteComputedSelector(Utf8JsonWriter writer, ComputedIndexedSelectorDefinition selector)
    {
        writer.WriteStartObject();
        writer.WriteString("selectorKind", SelectorKindToWire(selector.SelectorKind));
        writer.WriteString("referenceId", selector.ReferenceId);
        writer.WriteString("extent", selector.Extent.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WritePlan(Utf8JsonWriter writer, CompositeGuardedIndexedStateTransitionPlan plan)
    {
        writer.WritePropertyName("transitionPlan");
        writer.WriteStartObject();
        writer.WriteString("guardedIndexedTransitionPlanId", plan.GuardedIndexedTransitionPlanId);
        writer.WriteString("determinismProfile", plan.DeterminismProfile);
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
        writer.WritePropertyName("lookupTables");
        writer.WriteStartArray();
        foreach (var table in plan.OrderedLookupTables)
        {
            writer.WriteStartObject();
            writer.WriteString("key", table.Key);
            writer.WriteString("compositeIndexedStateLookupTableId", table.CompositeIndexedStateLookupTableId);
            writer.WriteString("semanticProfile", table.SemanticProfile);
            writer.WritePropertyName("selectors");
            writer.WriteStartArray();
            foreach (var selector in table.OrderedSelectors)
            {
                writer.WriteStartObject();
                writer.WriteString("stateDefinitionId", selector.StateDefinitionId);
                writer.WriteString("extent", selector.Extent.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (var entry in table.OrderedEntries)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("coordinates");
                writer.WriteStartArray();
                foreach (var coordinate in entry.OrderedCoordinates)
                {
                    writer.WriteStartObject();
                    writer.WriteString("stateDefinitionId", coordinate.StateDefinitionId);
                    writer.WriteString("index", coordinate.Index.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteString("value", entry.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("constraints");
        writer.WriteStartArray();
        foreach (var constraint in plan.OrderedConstraints)
        {
            writer.WriteStartObject();
            writer.WriteString("key", constraint.Key);
            writer.WriteString("compositeIndexedStateConstraintId", constraint.CompositeIndexedStateConstraintId);
            writer.WriteString("subjectStateDefinitionId", constraint.SubjectStateDefinitionId);
            writer.WriteString("lookupTableId", constraint.LookupTableId);
            writer.WritePropertyName("selectorStateDefinitionIds");
            writer.WriteStartArray();
            foreach (var selectorId in constraint.OrderedSelectorStateDefinitionIds)
            {
                writer.WriteStringValue(selectorId);
            }

            writer.WriteEndArray();
            writer.WriteString("comparisonKind", ComparisonToWire(constraint.ComparisonKind));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("unconditionalTransitions");
        writer.WriteStartArray();
        foreach (var transition in plan.OrderedUnconditionalTransitions)
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
        writer.WritePropertyName("guards");
        writer.WriteStartArray();
        foreach (var guard in plan.OrderedGuards)
        {
            writer.WriteStartObject();
            writer.WriteString("key", guard.Key);
            writer.WriteString("indexedStateGuardId", guard.IndexedStateGuardId);
            switch (guard)
            {
                case CompositeIndexedStateLookupComparisonGuard lookup:
                    writer.WriteString("guardKind", "compositeLookupComparison");
                    writer.WriteString("subjectStateDefinitionId", lookup.SubjectStateDefinitionId);
                    writer.WriteString("signedSubjectOffset", lookup.SignedSubjectOffset.ToString(CultureInfo.InvariantCulture));
                    writer.WriteString("comparisonKind", ComparisonToWire(lookup.ComparisonKind));
                    writer.WriteString("lookupTableId", lookup.LookupTableId);
                    writer.WritePropertyName("selectorStateDefinitionIds");
                    writer.WriteStartArray();
                    foreach (var selectorId in lookup.OrderedSelectorStateDefinitionIds)
                    {
                        writer.WriteStringValue(selectorId);
                    }

                    writer.WriteEndArray();
                    break;
                case IndexedStateConstantComparisonGuard constant:
                    writer.WriteString("guardKind", "stateConstantComparison");
                    writer.WriteString("subjectStateDefinitionId", constant.SubjectStateDefinitionId);
                    writer.WriteString("signedSubjectOffset", constant.SignedSubjectOffset.ToString(CultureInfo.InvariantCulture));
                    writer.WriteString("comparisonKind", ComparisonToWire(constant.ComparisonKind));
                    writer.WriteString("constant", constant.Constant.ToString(CultureInfo.InvariantCulture));
                    break;
                case IndexedStateAllOfGuard allOf:
                    writer.WriteString("guardKind", "allOf");
                    writer.WritePropertyName("orderedChildGuardIds");
                    writer.WriteStartArray();
                    foreach (var child in allOf.OrderedChildren)
                    {
                        writer.WriteStringValue(child.IndexedStateGuardId);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    throw new InvalidOperationException("Unknown composite guarded-state guard kind.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("effects");
        writer.WriteStartArray();
        foreach (var effect in plan.OrderedEffects)
        {
            writer.WriteStartObject();
            writer.WriteString("key", effect.Key);
            writer.WriteString("guardedStateEffectId", effect.GuardedStateEffectId);
            writer.WriteString("targetStateDefinitionId", effect.TargetStateDefinitionId);
            writer.WriteString("effectKind", EffectToWire(effect.EffectKind));
            writer.WriteString("operand", effect.Operand.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("branches");
        writer.WriteStartArray();
        foreach (var branch in plan.OrderedBranches)
        {
            writer.WriteStartObject();
            writer.WriteString("key", branch.Key);
            writer.WriteString("guardedTransitionBranchId", branch.GuardedTransitionBranchId);
            writer.WriteString("guardId", branch.Guard.IndexedStateGuardId);
            writer.WritePropertyName("orderedEffectIds");
            writer.WriteStartArray();
            foreach (var effect in branch.OrderedEffects)
            {
                writer.WriteStringValue(effect.GuardedStateEffectId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("choices");
        writer.WriteStartArray();
        foreach (var choice in plan.OrderedGuardedChoices)
        {
            writer.WriteStartObject();
            writer.WriteString("key", choice.Key);
            writer.WriteString("guardedTransitionChoiceId", choice.GuardedTransitionChoiceId);
            writer.WriteString("sourceEventDefinitionId", choice.SourcePeriodicEventDefinitionId);
            writer.WriteString("matchPolicy", MatchPolicyToWire(choice.MatchPolicy));
            writer.WritePropertyName("orderedBranchIds");
            writer.WriteStartArray();
            foreach (var branch in choice.OrderedBranches)
            {
                writer.WriteStringValue(branch.GuardedTransitionBranchId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePlan(Utf8JsonWriter writer, GuardedIndexedStateTransitionPlan plan)
    {
        writer.WritePropertyName("transitionPlan");
        writer.WriteStartObject();
        writer.WriteString("guardedIndexedTransitionPlanId", plan.GuardedIndexedTransitionPlanId);
        writer.WriteString("determinismProfile", plan.DeterminismProfile);
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
        writer.WritePropertyName("lookupTables");
        writer.WriteStartArray();
        foreach (var table in plan.OrderedLookupTables)
        {
            writer.WriteStartObject();
            writer.WriteString("key", table.Key);
            writer.WriteString("indexedStateLookupTableId", table.IndexedStateLookupTableId);
            writer.WriteString("selectorStateDefinitionId", table.SelectorStateDefinitionId);
            writer.WriteString("semanticProfile", table.SemanticProfile);
            writer.WritePropertyName("values");
            writer.WriteStartArray();
            foreach (var value in table.Values)
            {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("constraints");
        writer.WriteStartArray();
        foreach (var constraint in plan.OrderedConstraints)
        {
            writer.WriteStartObject();
            writer.WriteString("key", constraint.Key);
            writer.WriteString("indexedStateConstraintId", constraint.IndexedStateConstraintId);
            writer.WriteString("subjectStateDefinitionId", constraint.SubjectStateDefinitionId);
            writer.WriteString("selectorStateDefinitionId", constraint.SelectorStateDefinitionId);
            writer.WriteString("lookupTableId", constraint.LookupTableId);
            writer.WriteString("comparisonKind", ComparisonToWire(constraint.ComparisonKind));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("unconditionalTransitions");
        writer.WriteStartArray();
        foreach (var transition in plan.OrderedUnconditionalTransitions)
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
        writer.WritePropertyName("guards");
        writer.WriteStartArray();
        foreach (var guard in plan.OrderedGuards)
        {
            writer.WriteStartObject();
            writer.WriteString("key", guard.Key);
            writer.WriteString("indexedStateGuardId", guard.IndexedStateGuardId);
            writer.WriteString("subjectStateDefinitionId", guard.SubjectStateDefinitionId);
            writer.WriteString("signedSubjectOffset", guard.SignedSubjectOffset.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("comparisonKind", ComparisonToWire(guard.ComparisonKind));
            writer.WriteString("selectorStateDefinitionId", guard.SelectorStateDefinitionId);
            writer.WriteString("lookupTableId", guard.LookupTableId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("effects");
        writer.WriteStartArray();
        foreach (var effect in plan.OrderedEffects)
        {
            writer.WriteStartObject();
            writer.WriteString("key", effect.Key);
            writer.WriteString("guardedStateEffectId", effect.GuardedStateEffectId);
            writer.WriteString("targetStateDefinitionId", effect.TargetStateDefinitionId);
            writer.WriteString("effectKind", EffectToWire(effect.EffectKind));
            writer.WriteString("operand", effect.Operand.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("branches");
        writer.WriteStartArray();
        foreach (var branch in plan.OrderedBranches)
        {
            writer.WriteStartObject();
            writer.WriteString("key", branch.Key);
            writer.WriteString("guardedTransitionBranchId", branch.GuardedTransitionBranchId);
            writer.WriteString("guardId", branch.Guard.IndexedStateGuardId);
            writer.WritePropertyName("orderedEffectIds");
            writer.WriteStartArray();
            foreach (var effect in branch.OrderedEffects)
            {
                writer.WriteStringValue(effect.GuardedStateEffectId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("choices");
        writer.WriteStartArray();
        foreach (var choice in plan.OrderedGuardedChoices)
        {
            writer.WriteStartObject();
            writer.WriteString("key", choice.Key);
            writer.WriteString("guardedTransitionChoiceId", choice.GuardedTransitionChoiceId);
            writer.WriteString("sourceEventDefinitionId", choice.SourcePeriodicEventDefinitionId);
            writer.WriteString("matchPolicy", MatchPolicyToWire(choice.MatchPolicy));
            writer.WritePropertyName("orderedBranchIds");
            writer.WriteStartArray();
            foreach (var branch in choice.OrderedBranches)
            {
                writer.WriteStringValue(branch.GuardedTransitionBranchId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRequest(Utf8JsonWriter writer, ComputedGuardedHybridStateTransitionResult result)
    {
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("guardedHybridStateAdvanceRequestId", result.GuardedHybridStateAdvanceRequestId);
        writer.WriteNumber("maxAppliedOccurrences", result.NormalizedRequest.MaxAppliedOccurrences);
        writer.WriteString("determinismProfile", result.NormalizedRequest.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteRequest(Utf8JsonWriter writer, GuardedHybridStateTransitionResult result)
    {
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("guardedHybridStateAdvanceRequestId", result.GuardedHybridStateAdvanceRequestId);
        writer.WriteNumber("maxAppliedOccurrences", result.NormalizedRequest.MaxAppliedOccurrences);
        writer.WriteString("determinismProfile", result.NormalizedRequest.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteRequest(Utf8JsonWriter writer, CompositeGuardedHybridStateTransitionResult result)
    {
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("guardedHybridStateAdvanceRequestId", result.GuardedHybridStateAdvanceRequestId);
        writer.WriteNumber("maxAppliedOccurrences", result.NormalizedRequest.MaxAppliedOccurrences);
        writer.WriteString("determinismProfile", result.NormalizedRequest.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteSummary(Utf8JsonWriter writer, GuardedHybridStateTransitionSummary summary)
    {
        writer.WritePropertyName("summary");
        writer.WriteStartObject();
        writer.WriteString("knownOccurrenceCount", summary.KnownOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("appliedOccurrenceCount", summary.AppliedOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("omittedOccurrenceCount", summary.OmittedOccurrenceCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("occurrenceGroupCount", summary.OccurrenceGroupCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("transitionApplicationCount", summary.TransitionApplicationCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteOccurrenceGroups(
        Utf8JsonWriter writer,
        IEnumerable<GuardedOccurrenceApplicationGroup> groups)
    {
        writer.WritePropertyName("occurrenceGroups");
        writer.WriteStartArray();
        foreach (var group in groups)
        {
            writer.WriteStartObject();
            writer.WriteString("guardedOccurrenceApplicationGroupId", group.GuardedOccurrenceApplicationGroupId);
            writer.WriteString("occurrenceId", group.PeriodicEventOccurrenceId);
            writer.WriteString("periodicEventDefinitionId", group.PeriodicEventDefinitionId);
            writer.WriteString("ordinal", group.EventOrdinal.ToString(CultureInfo.InvariantCulture));
            writer.WritePropertyName("rootTurnsAtApplication");
            WriteRational(writer, group.RootTurnsAtApplication);
            writer.WriteString("selectedBranchId", group.SelectedBranchId);
            writer.WritePropertyName("orderedApplicationIds");
            writer.WriteStartArray();
            foreach (var id in group.OrderedApplicationIds)
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
            writer.WriteString("cursorBefore", group.CursorBefore.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("cursorAfter", group.CursorAfter.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteComputedEvaluations(
        Utf8JsonWriter writer,
        IEnumerable<ComputedIndexedValueEvaluation> evaluations)
    {
        writer.WritePropertyName("computedIndexedValueEvaluations");
        writer.WriteStartArray();
        foreach (var evaluation in evaluations)
        {
            writer.WriteStartObject();
            writer.WriteString("occurrenceId", evaluation.PeriodicEventOccurrenceId);
            writer.WriteString("periodicEventDefinitionId", evaluation.PeriodicEventDefinitionId);
            writer.WriteString("ordinal", evaluation.EventOrdinal.ToString(CultureInfo.InvariantCulture));
            writer.WritePropertyName("rootTurnsAtEvaluation");
            WriteRational(writer, evaluation.RootTurnsAtEvaluation);
            writer.WriteString("computedIndexedValueDefinitionId",
                evaluation.ComputedIndexedValueDefinitionId);
            writer.WriteString("selectedCaseId", evaluation.SelectedCaseId);
            writer.WriteString("outputIndex", evaluation.OutputIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteApplications(Utf8JsonWriter writer, ComputedGuardedHybridStateTransitionResult result)
    {
        var applications = new List<ApplicationEnvelope>();
        applications.AddRange(result.UnconditionalApplications.Select(ApplicationEnvelope.Unconditional));
        applications.AddRange(result.GuardedEffectApplications.Select(ApplicationEnvelope.Guarded));
        applications.Sort(ApplicationEnvelope.Compare);
        writer.WritePropertyName("applications");
        writer.WriteStartArray();
        foreach (var envelope in applications)
        {
            if (envelope.UnconditionalApplication is not null)
            {
                WriteUnconditionalApplication(writer, envelope.UnconditionalApplication);
            }
            else
            {
                WriteGuardedApplication(writer, envelope.GuardedApplication!);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteApplications(Utf8JsonWriter writer, GuardedHybridStateTransitionResult result)
    {
        var applications = new List<ApplicationEnvelope>();
        applications.AddRange(result.UnconditionalApplications.Select(ApplicationEnvelope.Unconditional));
        applications.AddRange(result.GuardedEffectApplications.Select(ApplicationEnvelope.Guarded));
        applications.Sort(ApplicationEnvelope.Compare);
        writer.WritePropertyName("applications");
        writer.WriteStartArray();
        foreach (var envelope in applications)
        {
            if (envelope.UnconditionalApplication is not null)
            {
                WriteUnconditionalApplication(writer, envelope.UnconditionalApplication);
            }
            else
            {
                WriteGuardedApplication(writer, envelope.GuardedApplication!);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteApplications(Utf8JsonWriter writer, CompositeGuardedHybridStateTransitionResult result)
    {
        var applications = new List<ApplicationEnvelope>();
        applications.AddRange(result.UnconditionalApplications.Select(ApplicationEnvelope.Unconditional));
        applications.AddRange(result.GuardedEffectApplications.Select(ApplicationEnvelope.Guarded));
        applications.Sort(ApplicationEnvelope.Compare);
        writer.WritePropertyName("applications");
        writer.WriteStartArray();
        foreach (var envelope in applications)
        {
            if (envelope.UnconditionalApplication is not null)
            {
                WriteUnconditionalApplication(writer, envelope.UnconditionalApplication);
            }
            else
            {
                WriteGuardedApplication(writer, envelope.GuardedApplication!);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteUnconditionalApplication(Utf8JsonWriter writer, StateTransitionApplication application)
    {
        writer.WriteStartObject();
        writer.WriteString("applicationKind", "unconditionalTransition");
        writer.WriteString("applicationId", application.TransitionApplicationId);
        writer.WriteString("definitionId", application.TransitionDefinitionId);
        WriteApplicationCommon(
            writer,
            application.PeriodicEventOccurrenceId,
            application.PeriodicEventDefinitionId,
            application.EventOrdinal,
            application.RootTurnsAtApplication,
            application.TargetStateDefinitionId,
            application.BeforeIndex,
            application.AfterIndex);
        writer.WriteString("effectKind", "addModulo");
        writer.WriteString("operand", application.SignedDelta.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteGuardedApplication(Utf8JsonWriter writer, GuardedStateEffectApplication application)
    {
        writer.WriteStartObject();
        writer.WriteString("applicationKind", "guardedEffect");
        writer.WriteString("applicationId", application.GuardedStateEffectApplicationId);
        writer.WriteString("definitionId", application.EffectDefinitionId);
        WriteApplicationCommon(
            writer,
            application.PeriodicEventOccurrenceId,
            application.PeriodicEventDefinitionId,
            application.EventOrdinal,
            application.RootTurnsAtApplication,
            application.TargetStateDefinitionId,
            application.BeforeIndex,
            application.AfterIndex);
        writer.WriteString("effectKind", EffectToWire(application.EffectKind));
        writer.WriteString("operand", application.Operand.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteApplicationCommon(
        Utf8JsonWriter writer,
        string occurrenceId,
        string eventDefinitionId,
        System.Numerics.BigInteger ordinal,
        Rational root,
        string targetStateId,
        System.Numerics.BigInteger before,
        System.Numerics.BigInteger after)
    {
        writer.WriteString("occurrenceId", occurrenceId);
        writer.WriteString("periodicEventDefinitionId", eventDefinitionId);
        writer.WriteString("ordinal", ordinal.ToString(CultureInfo.InvariantCulture));
        writer.WritePropertyName("rootTurnsAtApplication");
        WriteRational(writer, root);
        writer.WriteString("targetStateDefinitionId", targetStateId);
        writer.WriteString("beforeIndex", before.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("afterIndex", after.ToString(CultureInfo.InvariantCulture));
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

    private static string StatusToWire(GuardedHybridStateTransitionStatus status) => status switch
    {
        GuardedHybridStateTransitionStatus.Complete => "complete",
        GuardedHybridStateTransitionStatus.IncompleteBudget => "incompleteBudget",
        GuardedHybridStateTransitionStatus.InvalidInput => "invalidInput",
        GuardedHybridStateTransitionStatus.Unsupported => "unsupported",
        GuardedHybridStateTransitionStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown guarded hybrid state status."),
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

    private static string ComparisonToWire(IndexedStateLookupComparisonKind value) => value switch
    {
        IndexedStateLookupComparisonKind.LessThan => "lessThan",
        IndexedStateLookupComparisonKind.Equal => "equal",
        _ => throw new InvalidOperationException("Unknown indexed-state comparison kind."),
    };

    private static string ModuloComparisonToWire(IndexedStateModuloComparisonKind value) => value switch
    {
        IndexedStateModuloComparisonKind.Equal => "equal",
        IndexedStateModuloComparisonKind.NotEqual => "notEqual",
        _ => throw new InvalidOperationException("Unknown indexed-state modulo comparison kind."),
    };

    private static string SelectorKindToWire(ComputedIndexedSelectorKind value) => value switch
    {
        ComputedIndexedSelectorKind.State => "state",
        ComputedIndexedSelectorKind.ComputedIndexedValue => "computedIndexedValue",
        _ => throw new InvalidOperationException("Unknown computed indexed selector kind."),
    };

    private static string EffectToWire(GuardedStateEffectKind value) => value switch
    {
        GuardedStateEffectKind.AddModulo => "addModulo",
        GuardedStateEffectKind.SetIndex => "setIndex",
        _ => throw new InvalidOperationException("Unknown guarded-state effect kind."),
    };

    private static string MatchPolicyToWire(GuardedStateTransitionMatchPolicy value) => value switch
    {
        GuardedStateTransitionMatchPolicy.ExactlyOne => "exactlyOne",
        _ => throw new InvalidOperationException("Unknown guarded-state match policy."),
    };

    private sealed class ApplicationEnvelope
    {
        private ApplicationEnvelope(
            string occurrenceId,
            Rational root,
            string eventDefinitionId,
            System.Numerics.BigInteger ordinal,
            string definitionId,
            StateTransitionApplication? unconditional,
            GuardedStateEffectApplication? guarded)
        {
            OccurrenceId = occurrenceId;
            Root = root;
            EventDefinitionId = eventDefinitionId;
            Ordinal = ordinal;
            DefinitionId = definitionId;
            UnconditionalApplication = unconditional;
            GuardedApplication = guarded;
        }

        public string OccurrenceId { get; }
        public Rational Root { get; }
        public string EventDefinitionId { get; }
        public System.Numerics.BigInteger Ordinal { get; }
        public string DefinitionId { get; }
        public StateTransitionApplication? UnconditionalApplication { get; }
        public GuardedStateEffectApplication? GuardedApplication { get; }

        public static ApplicationEnvelope Unconditional(StateTransitionApplication value) =>
            new(value.PeriodicEventOccurrenceId, value.RootTurnsAtApplication, value.PeriodicEventDefinitionId,
                value.EventOrdinal, value.TransitionDefinitionId, value, null);

        public static ApplicationEnvelope Guarded(GuardedStateEffectApplication value) =>
            new(value.PeriodicEventOccurrenceId, value.RootTurnsAtApplication, value.PeriodicEventDefinitionId,
                value.EventOrdinal, value.EffectDefinitionId, null, value);

        public static int Compare(ApplicationEnvelope left, ApplicationEnvelope right)
        {
            var byRoot = left.Root.CompareTo(right.Root);
            if (byRoot != 0) return byRoot;
            var byEvent = StringComparer.Ordinal.Compare(left.EventDefinitionId, right.EventDefinitionId);
            if (byEvent != 0) return byEvent;
            var byOrdinal = left.Ordinal.CompareTo(right.Ordinal);
            if (byOrdinal != 0) return byOrdinal;
            return StringComparer.Ordinal.Compare(left.DefinitionId, right.DefinitionId);
        }
    }
}
