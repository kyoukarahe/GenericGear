using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class HybridStateSnapshotValidator
{
    public HybridStateSnapshotValidationResult Validate(
        HybridStateSnapshotDocument document,
        string expectedMechanismCandidateId,
        string expectedDriverId,
        ComputedGuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind,
        string expectedDeterminismProfile)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (compatiblePlan is null) throw new ArgumentNullException(nameof(compatiblePlan));

        var diagnostics = new List<Diagnostic>();
        var unsupported = false;
        var snapshot = document.Snapshot;

        ValidateDocumentAuthority(
            document,
            expectedMechanismCandidateId,
            expectedDriverId,
            compatiblePlan.GuardedIndexedTransitionPlanId,
            compatiblePlan.DeterminismProfile,
            requiredAuthorityKind,
            expectedDeterminismProfile,
            diagnostics,
            ref unsupported);
        ValidateComputedStates(snapshot, compatiblePlan, diagnostics);
        ValidateComputedCursors(snapshot, compatiblePlan, diagnostics);
        ValidateComputedConstraints(snapshot, compatiblePlan, diagnostics);

        var status = diagnostics.Count == 0
            ? HybridStateSnapshotValidationStatus.Valid
            : unsupported
                ? HybridStateSnapshotValidationStatus.Unsupported
                : HybridStateSnapshotValidationStatus.InvalidInput;
        return new HybridStateSnapshotValidationResult(status, diagnostics);
    }

    public HybridStateSnapshotValidationResult Validate(
        HybridStateSnapshotDocument document,
        string expectedMechanismCandidateId,
        string expectedDriverId,
        CompositeGuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind,
        string expectedDeterminismProfile)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (compatiblePlan is null) throw new ArgumentNullException(nameof(compatiblePlan));

        var diagnostics = new List<Diagnostic>();
        var unsupported = false;
        var snapshot = document.Snapshot;

        ValidateDocumentAuthority(
            document,
            expectedMechanismCandidateId,
            expectedDriverId,
            compatiblePlan.GuardedIndexedTransitionPlanId,
            compatiblePlan.DeterminismProfile,
            requiredAuthorityKind,
            expectedDeterminismProfile,
            diagnostics,
            ref unsupported);
        ValidateCompositeStates(snapshot, compatiblePlan, diagnostics);
        ValidateCompositeCursors(snapshot, compatiblePlan, diagnostics);
        ValidateCompositeConstraints(snapshot, compatiblePlan, diagnostics);

        var status = diagnostics.Count == 0
            ? HybridStateSnapshotValidationStatus.Valid
            : unsupported
                ? HybridStateSnapshotValidationStatus.Unsupported
                : HybridStateSnapshotValidationStatus.InvalidInput;
        return new HybridStateSnapshotValidationResult(status, diagnostics);
    }

    public HybridStateSnapshotValidationResult Validate(
        HybridStateSnapshotDocument document,
        string expectedMechanismCandidateId,
        string expectedDriverId,
        GuardedIndexedStateTransitionPlan compatiblePlan,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind,
        string expectedDeterminismProfile)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (compatiblePlan is null) throw new ArgumentNullException(nameof(compatiblePlan));

        var diagnostics = new List<Diagnostic>();
        var unsupported = false;
        var snapshot = document.Snapshot;

        if (!StringComparer.Ordinal.Equals(
                document.StoredHybridStateSnapshotId,
                snapshot.HybridStateSnapshotId))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotIdentityMismatch,
                "The stored hybrid snapshot identity does not match its canonical payload.",
                document.StoredHybridStateSnapshotId));
        }

        if (document.Authority.Kind != requiredAuthorityKind)
        {
            unsupported = true;
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotNotAuthoritative,
                "The snapshot authority is not accepted for this operation.",
                document.StoredHybridStateSnapshotId));
        }

        if (!StringComparer.Ordinal.Equals(
                document.Authority.CompatibleTransitionPlanId,
                compatiblePlan.GuardedIndexedTransitionPlanId))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotPlanMismatch,
                "The snapshot is bound to a different guarded transition plan.",
                document.Authority.CompatibleTransitionPlanId));
        }

        if (!StringComparer.Ordinal.Equals(
                document.Authority.DeterminismProfile,
                expectedDeterminismProfile) ||
            !StringComparer.Ordinal.Equals(compatiblePlan.DeterminismProfile, expectedDeterminismProfile))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotDeterminismProfileMismatch,
                "The snapshot determinism profile is incompatible with the requested plan.",
                document.Authority.DeterminismProfile));
        }

        if (!StringComparer.Ordinal.Equals(snapshot.SourceMechanismCandidateId, expectedMechanismCandidateId))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotCandidateMismatch,
                "The snapshot source mechanism candidate does not match the expected mechanism.",
                snapshot.SourceMechanismCandidateId));
        }

        if (!StringComparer.Ordinal.Equals(snapshot.DriverId, expectedDriverId))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotDriverMismatch,
                "The snapshot driver does not match the expected mechanism driver.",
                snapshot.DriverId));
        }

        ValidateStates(snapshot, compatiblePlan, diagnostics);
        ValidateCursors(snapshot, compatiblePlan, diagnostics);
        ValidateConstraints(snapshot, compatiblePlan, diagnostics);

        var status = diagnostics.Count == 0
            ? HybridStateSnapshotValidationStatus.Valid
            : unsupported
                ? HybridStateSnapshotValidationStatus.Unsupported
                : HybridStateSnapshotValidationStatus.InvalidInput;
        return new HybridStateSnapshotValidationResult(status, diagnostics);
    }

    private static void ValidateStates(
        HybridStateSnapshot snapshot,
        GuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var duplicate = snapshot.States
            .GroupBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateDuplicateSnapshotValue,
                "A persisted snapshot cannot contain duplicate state definitions.",
                duplicate.Key));
        }

        var expectedIds = plan.OrderedStateDefinitions
            .Select(item => item.CyclicStateDefinitionId)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var actualIds = snapshot.States
            .Select(item => item.StateDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotStateSetMismatch,
                "The persisted snapshot state-definition set is incompatible with the guarded plan.",
                plan.GuardedIndexedTransitionPlanId));
        }

        var definitions = plan.OrderedStateDefinitions.ToDictionary(
            item => item.CyclicStateDefinitionId,
            StringComparer.Ordinal);
        foreach (var state in snapshot.States)
        {
            if (!definitions.TryGetValue(state.StateDefinitionId, out var definition)) continue;
            if (state.CurrentIndex < 0 || state.CurrentIndex >= definition.CycleLength)
            {
                diagnostics.Add(Error(
                    DiagnosticCodes.StateSnapshotInvalidValue,
                    "A persisted state index is outside its cyclic definition range.",
                    state.StateDefinitionId));
            }
        }
    }

    private static void ValidateCursors(
        HybridStateSnapshot snapshot,
        GuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var duplicate = snapshot.EventCursors
            .GroupBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateDuplicateCursor,
                "A persisted snapshot cannot contain duplicate event cursors.",
                duplicate.Key));
        }

        var expectedIds = plan.OrderedUnconditionalTransitions
            .Select(item => item.SourcePeriodicEventDefinitionId)
            .Concat(plan.OrderedGuardedChoices.Select(item => item.SourcePeriodicEventDefinitionId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var actualIds = snapshot.EventCursors
            .Select(item => item.PeriodicEventDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotCursorSetMismatch,
                "The persisted event-cursor set is incompatible with the guarded plan.",
                plan.GuardedIndexedTransitionPlanId));
        }

        foreach (var cursor in snapshot.EventCursors.Where(item => item.LastAppliedOrdinal < 0))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.StateSnapshotInvalidValue,
                "A persisted event cursor cannot be negative.",
                cursor.PeriodicEventDefinitionId));
        }
    }

    private static void ValidateConstraints(
        HybridStateSnapshot snapshot,
        GuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var states = snapshot.States
            .GroupBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().CurrentIndex, StringComparer.Ordinal);
        var lookups = plan.OrderedLookupTables.ToDictionary(
            item => item.IndexedStateLookupTableId,
            StringComparer.Ordinal);

        foreach (var constraint in plan.OrderedConstraints)
        {
            if (!states.TryGetValue(constraint.SubjectStateDefinitionId, out var subject) ||
                !states.TryGetValue(constraint.SelectorStateDefinitionId, out var selector) ||
                !lookups.TryGetValue(constraint.LookupTableId, out var lookup) ||
                selector < 0 || selector >= lookup.Values.Count)
            {
                continue;
            }

            var bound = lookup.Values[(int)selector];
            var satisfied = constraint.ComparisonKind switch
            {
                IndexedStateLookupComparisonKind.LessThan => subject < bound,
                IndexedStateLookupComparisonKind.Equal => subject == bound,
                _ => false,
            };
            if (!satisfied)
            {
                diagnostics.Add(Error(
                    DiagnosticCodes.StateConstraintViolation,
                    "The persisted snapshot violates a guarded cross-state constraint.",
                    constraint.IndexedStateConstraintId));
            }
        }
    }

    private static Diagnostic Error(string code, string message, string? subjectId) =>
        new(code, DiagnosticSeverity.Error, message, subjectId);

    private static void ValidateDocumentAuthority(
        HybridStateSnapshotDocument document,
        string expectedMechanismCandidateId,
        string expectedDriverId,
        string compatiblePlanId,
        string compatiblePlanProfile,
        HybridStateSnapshotAuthorityKind requiredAuthorityKind,
        string expectedDeterminismProfile,
        ICollection<Diagnostic> diagnostics,
        ref bool unsupported)
    {
        var snapshot = document.Snapshot;
        if (!StringComparer.Ordinal.Equals(document.StoredHybridStateSnapshotId, snapshot.HybridStateSnapshotId))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotIdentityMismatch,
                "The stored hybrid snapshot identity does not match its canonical payload.",
                document.StoredHybridStateSnapshotId));
        }

        if (document.Authority.Kind != requiredAuthorityKind)
        {
            unsupported = true;
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotNotAuthoritative,
                "The snapshot authority is not accepted for this operation.",
                document.StoredHybridStateSnapshotId));
        }

        if (!StringComparer.Ordinal.Equals(document.Authority.CompatibleTransitionPlanId, compatiblePlanId))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotPlanMismatch,
                "The snapshot is bound to a different guarded transition plan.",
                document.Authority.CompatibleTransitionPlanId));
        }

        if (!StringComparer.Ordinal.Equals(document.Authority.DeterminismProfile, expectedDeterminismProfile) ||
            !StringComparer.Ordinal.Equals(compatiblePlanProfile, expectedDeterminismProfile))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotDeterminismProfileMismatch,
                "The snapshot determinism profile is incompatible with the requested plan.",
                document.Authority.DeterminismProfile));
        }

        if (!StringComparer.Ordinal.Equals(snapshot.SourceMechanismCandidateId, expectedMechanismCandidateId))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotCandidateMismatch,
                "The snapshot source mechanism candidate does not match the expected mechanism.",
                snapshot.SourceMechanismCandidateId));
        }

        if (!StringComparer.Ordinal.Equals(snapshot.DriverId, expectedDriverId))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotDriverMismatch,
                "The snapshot driver does not match the expected mechanism driver.",
                snapshot.DriverId));
        }
    }

    private static void ValidateCompositeStates(
        HybridStateSnapshot snapshot,
        CompositeGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var duplicate = snapshot.States
            .GroupBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            diagnostics.Add(Error(DiagnosticCodes.StateDuplicateSnapshotValue,
                "A persisted snapshot cannot contain duplicate state definitions.", duplicate.Key));
        }

        var expectedIds = plan.OrderedStateDefinitions.Select(item => item.CyclicStateDefinitionId)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var actualIds = snapshot.States.Select(item => item.StateDefinitionId)
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotStateSetMismatch,
                "The persisted snapshot state-definition set is incompatible with the guarded plan.",
                plan.GuardedIndexedTransitionPlanId));
        }

        var definitions = plan.OrderedStateDefinitions.ToDictionary(
            item => item.CyclicStateDefinitionId, StringComparer.Ordinal);
        foreach (var state in snapshot.States)
        {
            if (definitions.TryGetValue(state.StateDefinitionId, out var definition) &&
                (state.CurrentIndex < 0 || state.CurrentIndex >= definition.CycleLength))
            {
                diagnostics.Add(Error(DiagnosticCodes.StateSnapshotInvalidValue,
                    "A persisted state index is outside its cyclic definition range.", state.StateDefinitionId));
            }
        }
    }

    private static void ValidateCompositeCursors(
        HybridStateSnapshot snapshot,
        CompositeGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var duplicate = snapshot.EventCursors
            .GroupBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            diagnostics.Add(Error(DiagnosticCodes.StateDuplicateCursor,
                "A persisted snapshot cannot contain duplicate event cursors.", duplicate.Key));
        }

        var expectedIds = plan.OrderedUnconditionalTransitions.Select(item => item.SourcePeriodicEventDefinitionId)
            .Concat(plan.OrderedGuardedChoices.Select(item => item.SourcePeriodicEventDefinitionId))
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var actualIds = snapshot.EventCursors.Select(item => item.PeriodicEventDefinitionId)
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotCursorSetMismatch,
                "The persisted event-cursor set is incompatible with the guarded plan.",
                plan.GuardedIndexedTransitionPlanId));
        }

        foreach (var cursor in snapshot.EventCursors.Where(item => item.LastAppliedOrdinal < 0))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotInvalidValue,
                "A persisted event cursor cannot be negative.", cursor.PeriodicEventDefinitionId));
        }
    }

    private static void ValidateCompositeConstraints(
        HybridStateSnapshot snapshot,
        CompositeGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var states = snapshot.States.GroupBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().CurrentIndex, StringComparer.Ordinal);
        var lookups = plan.OrderedLookupTables.ToDictionary(
            item => item.CompositeIndexedStateLookupTableId, StringComparer.Ordinal);
        foreach (var constraint in plan.OrderedConstraints)
        {
            if (!states.TryGetValue(constraint.SubjectStateDefinitionId, out var subject) ||
                !lookups.TryGetValue(constraint.LookupTableId, out var lookup))
            {
                continue;
            }

            var expected = new Dictionary<string, System.Numerics.BigInteger>(StringComparer.Ordinal);
            var selectorsValid = true;
            foreach (var selector in lookup.OrderedSelectors)
            {
                if (!states.TryGetValue(selector.StateDefinitionId, out var index) ||
                    index < 0 || index >= selector.Extent)
                {
                    selectorsValid = false;
                    break;
                }

                expected.Add(selector.StateDefinitionId, index);
            }

            if (!selectorsValid) continue;
            var matches = lookup.OrderedEntries.Where(entry =>
                entry.OrderedCoordinates.Count == expected.Count &&
                entry.OrderedCoordinates.All(coordinate =>
                    expected.TryGetValue(coordinate.StateDefinitionId, out var index) && index == coordinate.Index)).ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(Error(DiagnosticCodes.StateCompositeLookupInvalidDefinition,
                    "The persisted snapshot cannot resolve one exact composite lookup tuple.",
                    lookup.CompositeIndexedStateLookupTableId));
                continue;
            }

            var satisfied = constraint.ComparisonKind switch
            {
                IndexedStateLookupComparisonKind.LessThan => subject < matches[0].Value,
                IndexedStateLookupComparisonKind.Equal => subject == matches[0].Value,
                _ => false,
            };
            if (!satisfied)
            {
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintViolation,
                    "The persisted snapshot violates a guarded composite cross-state constraint.",
                    constraint.CompositeIndexedStateConstraintId));
            }
        }
    }

    private static void ValidateComputedStates(
        HybridStateSnapshot snapshot,
        ComputedGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var duplicate = snapshot.States
            .GroupBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            diagnostics.Add(Error(DiagnosticCodes.StateDuplicateSnapshotValue,
                "A persisted snapshot cannot contain duplicate state definitions.", duplicate.Key));
        }

        var expectedIds = plan.OrderedStateDefinitions.Select(item => item.CyclicStateDefinitionId)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var actualIds = snapshot.States.Select(item => item.StateDefinitionId)
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotStateSetMismatch,
                "The persisted snapshot state-definition set is incompatible with the computed guarded plan.",
                plan.GuardedIndexedTransitionPlanId));
        }

        var definitions = plan.OrderedStateDefinitions.ToDictionary(
            item => item.CyclicStateDefinitionId, StringComparer.Ordinal);
        foreach (var state in snapshot.States)
        {
            if (definitions.TryGetValue(state.StateDefinitionId, out var definition) &&
                (state.CurrentIndex < 0 || state.CurrentIndex >= definition.CycleLength))
            {
                diagnostics.Add(Error(DiagnosticCodes.StateSnapshotInvalidValue,
                    "A persisted state index is outside its cyclic definition range.", state.StateDefinitionId));
            }
        }
    }

    private static void ValidateComputedCursors(
        HybridStateSnapshot snapshot,
        ComputedGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var duplicate = snapshot.EventCursors
            .GroupBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            diagnostics.Add(Error(DiagnosticCodes.StateDuplicateCursor,
                "A persisted snapshot cannot contain duplicate event cursors.", duplicate.Key));
        }

        var expectedIds = plan.OrderedUnconditionalTransitions.Select(item => item.SourcePeriodicEventDefinitionId)
            .Concat(plan.OrderedGuardedChoices.Select(item => item.SourcePeriodicEventDefinitionId))
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var actualIds = snapshot.EventCursors.Select(item => item.PeriodicEventDefinitionId)
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotCursorSetMismatch,
                "The persisted event-cursor set is incompatible with the computed guarded plan.",
                plan.GuardedIndexedTransitionPlanId));
        }

        foreach (var cursor in snapshot.EventCursors.Where(item => item.LastAppliedOrdinal < 0))
        {
            diagnostics.Add(Error(DiagnosticCodes.StateSnapshotInvalidValue,
                "A persisted event cursor cannot be negative.", cursor.PeriodicEventDefinitionId));
        }
    }

    private static void ValidateComputedConstraints(
        HybridStateSnapshot snapshot,
        ComputedGuardedIndexedStateTransitionPlan plan,
        ICollection<Diagnostic> diagnostics)
    {
        var states = snapshot.States.GroupBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().CurrentIndex, StringComparer.Ordinal);
        var computed = new Dictionary<string, BigInteger>(StringComparer.Ordinal);

        foreach (var definition in plan.OrderedComputedDefinitions)
        {
            var matches = definition.OrderedCases
                .Where(item => EvaluateComputedClassificationGuard(item.Guard, states))
                .ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(Error(
                    matches.Length == 0 ? DiagnosticCodes.StateComputedNoMatch : DiagnosticCodes.StateComputedAmbiguous,
                    matches.Length == 0
                        ? "The persisted snapshot does not match any computed indexed-value case."
                        : "The persisted snapshot matches multiple computed indexed-value cases.",
                    definition.ComputedIndexedValueDefinitionId));
                continue;
            }

            computed[definition.ComputedIndexedValueDefinitionId] = matches[0].OutputIndex;
        }

        var lookups = plan.OrderedLookupTables.ToDictionary(
            item => item.ComputedSelectorIndexedLookupTableId, StringComparer.Ordinal);
        foreach (var constraint in plan.OrderedConstraints)
        {
            if (!states.TryGetValue(constraint.SubjectStateDefinitionId, out var subject) ||
                !lookups.TryGetValue(constraint.LookupTableId, out var lookup))
            {
                continue;
            }

            var expected = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
            var selectorsValid = true;
            foreach (var selector in lookup.OrderedSelectors)
            {
                var found = selector.SelectorKind switch
                {
                    ComputedIndexedSelectorKind.State => states.TryGetValue(selector.ReferenceId, out var stateIndex) &&
                        AddValue(expected, selector.CanonicalReference, stateIndex),
                    ComputedIndexedSelectorKind.ComputedIndexedValue =>
                        computed.TryGetValue(selector.ReferenceId, out var computedIndex) &&
                        AddValue(expected, selector.CanonicalReference, computedIndex),
                    _ => false,
                };
                if (!found)
                {
                    selectorsValid = false;
                    break;
                }
            }

            if (!selectorsValid) continue;
            var matches = lookup.OrderedEntries.Where(entry =>
                entry.OrderedCoordinates.Count == expected.Count &&
                entry.OrderedCoordinates.All(coordinate =>
                    expected.TryGetValue(coordinate.CanonicalReference, out var index) && index == coordinate.Index))
                .ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(Error(DiagnosticCodes.StateComputedLookupInvalidSelector,
                    "The persisted snapshot cannot resolve one exact computed-selector lookup tuple.",
                    lookup.ComputedSelectorIndexedLookupTableId));
                continue;
            }

            var satisfied = constraint.ComparisonKind switch
            {
                IndexedStateLookupComparisonKind.LessThan => subject < matches[0].Value,
                IndexedStateLookupComparisonKind.Equal => subject == matches[0].Value,
                _ => false,
            };
            if (!satisfied)
            {
                diagnostics.Add(Error(DiagnosticCodes.StateConstraintViolation,
                    "The persisted snapshot violates a computed-selector cross-state constraint.",
                    constraint.ComputedSelectorIndexedStateConstraintId));
            }
        }
    }

    private static bool EvaluateComputedClassificationGuard(
        BoundedIndexedStateGuardDefinition guard,
        IReadOnlyDictionary<string, BigInteger> states)
    {
        switch (guard)
        {
            case IndexedStateModuloComparisonGuard modulo:
                if (modulo.Modulus <= 0 || modulo.Residue < 0 || modulo.Residue >= modulo.Modulus ||
                    !states.TryGetValue(modulo.SubjectStateDefinitionId, out var subject)) return false;
                var normalized = ((subject % modulo.Modulus) + modulo.Modulus) % modulo.Modulus;
                return modulo.ComparisonKind switch
                {
                    IndexedStateModuloComparisonKind.Equal => normalized == modulo.Residue,
                    IndexedStateModuloComparisonKind.NotEqual => normalized != modulo.Residue,
                    _ => false,
                };
            case IndexedStateConstantComparisonGuard constant:
                if (!states.TryGetValue(constant.SubjectStateDefinitionId, out var constantSubject)) return false;
                var candidate = constantSubject + constant.SignedSubjectOffset;
                return constant.ComparisonKind switch
                {
                    IndexedStateLookupComparisonKind.LessThan => candidate < constant.Constant,
                    IndexedStateLookupComparisonKind.Equal => candidate == constant.Constant,
                    _ => false,
                };
            case IndexedStateAllOfGuard allOf:
                return allOf.OrderedChildren.All(child => EvaluateComputedClassificationGuard(child, states));
            default:
                return false;
        }
    }

    private static bool AddValue(IDictionary<string, BigInteger> values, string key, BigInteger value)
    {
        values.Add(key, value);
        return true;
    }
}
