using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GearInvest.Core;

public static class GuardedIndexedStateContract
{
    public const string DefaultDeterminismProfile = "portable-managed-guarded-indexed-state-v1";
    public const string LookupSemanticProfile = "exact-integer-indexed-lookup-v1";
    public const string LookupSemanticVersion = "indexed-state-lookup-table-v1";
    public const string ConstraintSemanticVersion = "indexed-state-lookup-bound-constraint-v1";
    public const string GuardSemanticVersion = "indexed-state-lookup-comparison-guard-v1";
    public const string EffectSemanticVersion = "guarded-state-effect-v1";
    public const string BranchSemanticVersion = "guarded-state-transition-branch-v1";
    public const string ChoiceSemanticVersion = "guarded-state-transition-choice-v1";
    public const string PlanSemanticVersion = "guarded-indexed-state-transition-plan-v1";
    public const string EffectApplicationSemanticVersion = "guarded-state-effect-application-v1";
    public const string OccurrenceGroupSemanticVersion = "guarded-occurrence-application-group-v1";
}

public enum IndexedStateLookupComparisonKind
{
    LessThan,
    Equal,
}

public enum GuardedStateEffectKind
{
    AddModulo,
    SetIndex,
}

public enum GuardedStateTransitionMatchPolicy
{
    ExactlyOne,
}

public sealed class IndexedStateLookupTableDefinition
{
    public IndexedStateLookupTableDefinition(
        string key,
        string selectorStateDefinitionId,
        IEnumerable<BigInteger> values,
        string semanticProfile = GuardedIndexedStateContract.LookupSemanticProfile)
    {
        Key = key ?? string.Empty;
        SelectorStateDefinitionId = selectorStateDefinitionId ?? string.Empty;
        Values = (values ?? throw new ArgumentNullException(nameof(values))).ToList().AsReadOnly();
        SemanticProfile = semanticProfile ?? string.Empty;
        IndexedStateLookupTableId = GuardedIndexedStateIdentity.ComputeLookupTableId(
            SelectorStateDefinitionId,
            Values,
            SemanticProfile);
    }

    public string Key { get; }
    public string SelectorStateDefinitionId { get; }
    public ReadOnlyCollection<BigInteger> Values { get; }
    public string SemanticProfile { get; }
    public string IndexedStateLookupTableId { get; }
}

public sealed class IndexedStateLookupBoundConstraint
{
    public IndexedStateLookupBoundConstraint(
        string key,
        string subjectStateDefinitionId,
        string selectorStateDefinitionId,
        string lookupTableId,
        IndexedStateLookupComparisonKind comparisonKind)
    {
        Key = key ?? string.Empty;
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        SelectorStateDefinitionId = selectorStateDefinitionId ?? string.Empty;
        LookupTableId = lookupTableId ?? string.Empty;
        ComparisonKind = comparisonKind;
        IndexedStateConstraintId = GuardedIndexedStateIdentity.ComputeConstraintId(
            SubjectStateDefinitionId,
            SelectorStateDefinitionId,
            LookupTableId,
            ComparisonKind);
    }

    public string Key { get; }
    public string SubjectStateDefinitionId { get; }
    public string SelectorStateDefinitionId { get; }
    public string LookupTableId { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public string IndexedStateConstraintId { get; }
}

public sealed class IndexedStateLookupComparisonGuard
{
    public IndexedStateLookupComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string selectorStateDefinitionId,
        string lookupTableId)
    {
        Key = key ?? string.Empty;
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        SignedSubjectOffset = signedSubjectOffset;
        ComparisonKind = comparisonKind;
        SelectorStateDefinitionId = selectorStateDefinitionId ?? string.Empty;
        LookupTableId = lookupTableId ?? string.Empty;
        IndexedStateGuardId = GuardedIndexedStateIdentity.ComputeGuardId(
            SubjectStateDefinitionId,
            SignedSubjectOffset,
            ComparisonKind,
            SelectorStateDefinitionId,
            LookupTableId);
    }

    public string Key { get; }
    public string SubjectStateDefinitionId { get; }
    public BigInteger SignedSubjectOffset { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public string SelectorStateDefinitionId { get; }
    public string LookupTableId { get; }
    public string IndexedStateGuardId { get; }
}

public sealed class GuardedStateEffectDefinition
{
    public GuardedStateEffectDefinition(
        string key,
        string targetStateDefinitionId,
        GuardedStateEffectKind effectKind,
        BigInteger operand)
    {
        Key = key ?? string.Empty;
        TargetStateDefinitionId = targetStateDefinitionId ?? string.Empty;
        EffectKind = effectKind;
        Operand = operand;
        GuardedStateEffectId = GuardedIndexedStateIdentity.ComputeEffectId(
            TargetStateDefinitionId,
            EffectKind,
            Operand);
    }

    public string Key { get; }
    public string TargetStateDefinitionId { get; }
    public GuardedStateEffectKind EffectKind { get; }
    public BigInteger Operand { get; }
    public string GuardedStateEffectId { get; }
}

public sealed class GuardedStateTransitionBranch
{
    public GuardedStateTransitionBranch(
        string key,
        IndexedStateLookupComparisonGuard guard,
        IEnumerable<GuardedStateEffectDefinition> effects)
    {
        Key = key ?? string.Empty;
        Guard = guard ?? throw new ArgumentNullException(nameof(guard));
        OrderedEffects = (effects ?? throw new ArgumentNullException(nameof(effects)))
            .OrderBy(item => item.GuardedStateEffectId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        GuardedTransitionBranchId = GuardedIndexedStateIdentity.ComputeBranchId(
            Guard.IndexedStateGuardId,
            OrderedEffects.Select(item => item.GuardedStateEffectId));
    }

    public string Key { get; }
    public IndexedStateLookupComparisonGuard Guard { get; }
    public ReadOnlyCollection<GuardedStateEffectDefinition> OrderedEffects { get; }
    public string GuardedTransitionBranchId { get; }
}

public sealed class GuardedStateTransitionChoice
{
    public GuardedStateTransitionChoice(
        string key,
        string sourcePeriodicEventDefinitionId,
        IEnumerable<GuardedStateTransitionBranch> branches,
        GuardedStateTransitionMatchPolicy matchPolicy)
    {
        Key = key ?? string.Empty;
        SourcePeriodicEventDefinitionId = sourcePeriodicEventDefinitionId ?? string.Empty;
        OrderedBranches = (branches ?? throw new ArgumentNullException(nameof(branches)))
            .OrderBy(item => item.GuardedTransitionBranchId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        MatchPolicy = matchPolicy;
        GuardedTransitionChoiceId = GuardedIndexedStateIdentity.ComputeChoiceId(
            SourcePeriodicEventDefinitionId,
            OrderedBranches.Select(item => item.GuardedTransitionBranchId),
            MatchPolicy);
    }

    public string Key { get; }
    public string SourcePeriodicEventDefinitionId { get; }
    public ReadOnlyCollection<GuardedStateTransitionBranch> OrderedBranches { get; }
    public GuardedStateTransitionMatchPolicy MatchPolicy { get; }
    public string GuardedTransitionChoiceId { get; }
}

public sealed class GuardedIndexedStateTransitionPlan
{
    public GuardedIndexedStateTransitionPlan(
        IEnumerable<CyclicIndexedStateDefinition> stateDefinitions,
        IEnumerable<IndexedStateLookupTableDefinition> lookupTables,
        IEnumerable<IndexedStateLookupBoundConstraint> constraints,
        IEnumerable<IndexedStateTransitionDefinition> unconditionalTransitions,
        IEnumerable<GuardedStateTransitionChoice> guardedChoices,
        string determinismProfile = GuardedIndexedStateContract.DefaultDeterminismProfile)
    {
        OrderedStateDefinitions = Canonicalize(
            stateDefinitions,
            item => item.CyclicStateDefinitionId,
            item => item.Key);
        OrderedLookupTables = Canonicalize(
            lookupTables,
            item => item.IndexedStateLookupTableId,
            item => item.Key);
        OrderedConstraints = Canonicalize(
            constraints,
            item => item.IndexedStateConstraintId,
            item => item.Key);
        OrderedUnconditionalTransitions = Canonicalize(
            unconditionalTransitions,
            item => item.IndexedStateTransitionDefinitionId,
            item => item.Key);
        OrderedGuardedChoices = Canonicalize(
            guardedChoices,
            item => item.GuardedTransitionChoiceId,
            item => item.Key);
        OrderedBranches = OrderedGuardedChoices
            .SelectMany(item => item.OrderedBranches)
            .OrderBy(item => item.GuardedTransitionBranchId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        OrderedGuards = OrderedBranches
            .Select(item => item.Guard)
            .OrderBy(item => item.IndexedStateGuardId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        OrderedEffects = OrderedBranches
            .SelectMany(item => item.OrderedEffects)
            .OrderBy(item => item.GuardedStateEffectId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        DeterminismProfile = determinismProfile ?? string.Empty;
        GuardedIndexedTransitionPlanId = GuardedIndexedStateIdentity.ComputePlanId(this);
    }

    public ReadOnlyCollection<CyclicIndexedStateDefinition> OrderedStateDefinitions { get; }
    public ReadOnlyCollection<IndexedStateLookupTableDefinition> OrderedLookupTables { get; }
    public ReadOnlyCollection<IndexedStateLookupBoundConstraint> OrderedConstraints { get; }
    public ReadOnlyCollection<IndexedStateTransitionDefinition> OrderedUnconditionalTransitions { get; }
    public ReadOnlyCollection<GuardedStateTransitionChoice> OrderedGuardedChoices { get; }
    public ReadOnlyCollection<GuardedStateTransitionBranch> OrderedBranches { get; }
    public ReadOnlyCollection<IndexedStateLookupComparisonGuard> OrderedGuards { get; }
    public ReadOnlyCollection<GuardedStateEffectDefinition> OrderedEffects { get; }
    public string DeterminismProfile { get; }
    public string GuardedIndexedTransitionPlanId { get; }

    private static ReadOnlyCollection<T> Canonicalize<T>(
        IEnumerable<T> values,
        Func<T, string> id,
        Func<T, string> key) =>
        (values ?? throw new ArgumentNullException(nameof(values)))
            .OrderBy(id, StringComparer.Ordinal)
            .ThenBy(key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
}

public sealed class GuardedStateEffectApplication
{
    public GuardedStateEffectApplication(
        string effectDefinitionId,
        string periodicEventOccurrenceId,
        string periodicEventDefinitionId,
        BigInteger eventOrdinal,
        Rational rootTurnsAtApplication,
        string targetStateDefinitionId,
        GuardedStateEffectKind effectKind,
        BigInteger operand,
        BigInteger beforeIndex,
        BigInteger afterIndex)
    {
        EffectDefinitionId = effectDefinitionId ?? string.Empty;
        PeriodicEventOccurrenceId = periodicEventOccurrenceId ?? string.Empty;
        PeriodicEventDefinitionId = periodicEventDefinitionId ?? string.Empty;
        EventOrdinal = eventOrdinal;
        RootTurnsAtApplication = rootTurnsAtApplication;
        TargetStateDefinitionId = targetStateDefinitionId ?? string.Empty;
        EffectKind = effectKind;
        Operand = operand;
        BeforeIndex = beforeIndex;
        AfterIndex = afterIndex;
        GuardedStateEffectApplicationId = GuardedIndexedStateIdentity.ComputeEffectApplicationId(
            EffectDefinitionId,
            PeriodicEventOccurrenceId,
            BeforeIndex,
            AfterIndex);
    }

    public string GuardedStateEffectApplicationId { get; }
    public string EffectDefinitionId { get; }
    public string PeriodicEventOccurrenceId { get; }
    public string PeriodicEventDefinitionId { get; }
    public BigInteger EventOrdinal { get; }
    public Rational RootTurnsAtApplication { get; }
    public string TargetStateDefinitionId { get; }
    public GuardedStateEffectKind EffectKind { get; }
    public BigInteger Operand { get; }
    public BigInteger BeforeIndex { get; }
    public BigInteger AfterIndex { get; }
}

public sealed class GuardedOccurrenceApplicationGroup
{
    public GuardedOccurrenceApplicationGroup(
        string periodicEventOccurrenceId,
        string periodicEventDefinitionId,
        BigInteger eventOrdinal,
        Rational rootTurnsAtApplication,
        string selectedBranchId,
        IEnumerable<string> orderedApplicationIds,
        BigInteger cursorBefore,
        BigInteger cursorAfter)
    {
        PeriodicEventOccurrenceId = periodicEventOccurrenceId ?? string.Empty;
        PeriodicEventDefinitionId = periodicEventDefinitionId ?? string.Empty;
        EventOrdinal = eventOrdinal;
        RootTurnsAtApplication = rootTurnsAtApplication;
        SelectedBranchId = selectedBranchId ?? string.Empty;
        OrderedApplicationIds = (orderedApplicationIds ?? throw new ArgumentNullException(nameof(orderedApplicationIds)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        CursorBefore = cursorBefore;
        CursorAfter = cursorAfter;
        GuardedOccurrenceApplicationGroupId = GuardedIndexedStateIdentity.ComputeOccurrenceGroupId(
            PeriodicEventOccurrenceId,
            SelectedBranchId,
            OrderedApplicationIds,
            CursorBefore,
            CursorAfter);
    }

    public string GuardedOccurrenceApplicationGroupId { get; }
    public string PeriodicEventOccurrenceId { get; }
    public string PeriodicEventDefinitionId { get; }
    public BigInteger EventOrdinal { get; }
    public Rational RootTurnsAtApplication { get; }
    public string SelectedBranchId { get; }
    public ReadOnlyCollection<string> OrderedApplicationIds { get; }
    public BigInteger CursorBefore { get; }
    public BigInteger CursorAfter { get; }
}

public static class GuardedIndexedStateIdentity
{
    public static string ComputeLookupTableId(
        string selectorStateDefinitionId,
        IEnumerable<BigInteger> values,
        string semanticProfile)
    {
        var builder = new StringBuilder(GuardedIndexedStateContract.LookupSemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(semanticProfile ?? string.Empty));
        builder.Append("|selectorStateDefinitionId=").Append(LengthPrefixed(selectorStateDefinitionId ?? string.Empty));
        foreach (var value in values)
        {
            builder.Append("|value=").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        return PeriodicSemanticIdentity.Hash("indexed-state-lookup-table-sha256:", builder.ToString());
    }

    public static string ComputeConstraintId(
        string subjectStateDefinitionId,
        string selectorStateDefinitionId,
        string lookupTableId,
        IndexedStateLookupComparisonKind comparisonKind) =>
        Hash(
            "indexed-state-constraint-sha256:",
            GuardedIndexedStateContract.ConstraintSemanticVersion,
            ("subjectStateDefinitionId", subjectStateDefinitionId),
            ("selectorStateDefinitionId", selectorStateDefinitionId),
            ("lookupTableId", lookupTableId),
            ("comparisonKind", comparisonKind.ToString()));

    public static string ComputeGuardId(
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string selectorStateDefinitionId,
        string lookupTableId) =>
        Hash(
            "indexed-state-guard-sha256:",
            GuardedIndexedStateContract.GuardSemanticVersion,
            ("subjectStateDefinitionId", subjectStateDefinitionId),
            ("signedSubjectOffset", signedSubjectOffset.ToString(CultureInfo.InvariantCulture)),
            ("comparisonKind", comparisonKind.ToString()),
            ("selectorStateDefinitionId", selectorStateDefinitionId),
            ("lookupTableId", lookupTableId));

    public static string ComputeEffectId(
        string targetStateDefinitionId,
        GuardedStateEffectKind effectKind,
        BigInteger operand) =>
        Hash(
            "guarded-state-effect-sha256:",
            GuardedIndexedStateContract.EffectSemanticVersion,
            ("targetStateDefinitionId", targetStateDefinitionId),
            ("effectKind", effectKind.ToString()),
            ("operand", operand.ToString(CultureInfo.InvariantCulture)));

    public static string ComputeBranchId(string guardId, IEnumerable<string> effectIds)
    {
        var builder = new StringBuilder(GuardedIndexedStateContract.BranchSemanticVersion);
        builder.Append("|guardId=").Append(LengthPrefixed(guardId ?? string.Empty));
        foreach (var id in effectIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append("|effectId=").Append(LengthPrefixed(id));
        }

        return PeriodicSemanticIdentity.Hash("guarded-transition-branch-sha256:", builder.ToString());
    }

    public static string ComputeChoiceId(
        string sourcePeriodicEventDefinitionId,
        IEnumerable<string> branchIds,
        GuardedStateTransitionMatchPolicy matchPolicy)
    {
        var builder = new StringBuilder(GuardedIndexedStateContract.ChoiceSemanticVersion);
        builder.Append("|sourcePeriodicEventDefinitionId=")
            .Append(LengthPrefixed(sourcePeriodicEventDefinitionId ?? string.Empty));
        foreach (var id in branchIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append("|branchId=").Append(LengthPrefixed(id));
        }

        builder.Append("|matchPolicy=").Append(LengthPrefixed(matchPolicy.ToString()));
        return PeriodicSemanticIdentity.Hash("guarded-transition-choice-sha256:", builder.ToString());
    }

    public static string ComputePlanId(GuardedIndexedStateTransitionPlan plan)
    {
        var builder = new StringBuilder(GuardedIndexedStateContract.PlanSemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(plan.DeterminismProfile));
        AppendIds(builder, "stateDefinitionId", plan.OrderedStateDefinitions.Select(item => item.CyclicStateDefinitionId));
        AppendIds(builder, "lookupTableId", plan.OrderedLookupTables.Select(item => item.IndexedStateLookupTableId));
        AppendIds(builder, "constraintId", plan.OrderedConstraints.Select(item => item.IndexedStateConstraintId));
        AppendIds(builder, "unconditionalTransitionId", plan.OrderedUnconditionalTransitions.Select(item => item.IndexedStateTransitionDefinitionId));
        AppendIds(builder, "choiceId", plan.OrderedGuardedChoices.Select(item => item.GuardedTransitionChoiceId));
        return PeriodicSemanticIdentity.Hash("guarded-indexed-transition-plan-sha256:", builder.ToString());
    }

    public static string ComputeEffectApplicationId(
        string effectDefinitionId,
        string periodicEventOccurrenceId,
        BigInteger beforeIndex,
        BigInteger afterIndex) =>
        Hash(
            "guarded-state-effect-application-sha256:",
            GuardedIndexedStateContract.EffectApplicationSemanticVersion,
            ("effectDefinitionId", effectDefinitionId),
            ("periodicEventOccurrenceId", periodicEventOccurrenceId),
            ("beforeIndex", beforeIndex.ToString(CultureInfo.InvariantCulture)),
            ("afterIndex", afterIndex.ToString(CultureInfo.InvariantCulture)));

    public static string ComputeOccurrenceGroupId(
        string periodicEventOccurrenceId,
        string selectedBranchId,
        IEnumerable<string> orderedApplicationIds,
        BigInteger cursorBefore,
        BigInteger cursorAfter)
    {
        var builder = new StringBuilder(GuardedIndexedStateContract.OccurrenceGroupSemanticVersion);
        builder.Append("|periodicEventOccurrenceId=").Append(LengthPrefixed(periodicEventOccurrenceId ?? string.Empty));
        builder.Append("|selectedBranchId=").Append(LengthPrefixed(selectedBranchId ?? string.Empty));
        foreach (var id in orderedApplicationIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append("|applicationId=").Append(LengthPrefixed(id));
        }

        builder.Append("|cursorBefore=").Append(cursorBefore.ToString(CultureInfo.InvariantCulture));
        builder.Append("|cursorAfter=").Append(cursorAfter.ToString(CultureInfo.InvariantCulture));
        return PeriodicSemanticIdentity.Hash("guarded-occurrence-application-group-sha256:", builder.ToString());
    }

    private static string Hash(string prefix, string version, params (string Name, string? Value)[] fields)
    {
        var builder = new StringBuilder(version);
        foreach (var field in fields)
        {
            builder.Append('|').Append(field.Name).Append('=').Append(LengthPrefixed(field.Value ?? string.Empty));
        }

        return PeriodicSemanticIdentity.Hash(prefix, builder.ToString());
    }

    private static void AppendIds(StringBuilder builder, string name, IEnumerable<string> ids)
    {
        foreach (var id in ids.OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append('|').Append(name).Append('=').Append(LengthPrefixed(id));
        }
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
