using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GearInvest.Core;

public static class CompositeGuardedIndexedStateContract
{
    public const string DefaultDeterminismProfile = "portable-managed-four-year-leap-cycle-v1";
    public const string LookupSemanticProfile = "exact-integer-two-selector-dense-lookup-v1";
    public const string LookupSemanticVersion = "composite-indexed-state-lookup-table-v1";
    public const string ConstraintSemanticVersion = "composite-indexed-state-lookup-bound-constraint-v1";
    public const string LookupGuardSemanticVersion = "composite-indexed-state-lookup-comparison-guard-v1";
    public const string ConstantGuardSemanticVersion = "indexed-state-constant-comparison-guard-v1";
    public const string AllOfGuardSemanticVersion = "indexed-state-flat-all-of-guard-v1";
    public const string BranchSemanticVersion = "composite-guarded-state-transition-branch-v1";
    public const string ChoiceSemanticVersion = "composite-guarded-state-transition-choice-v1";
    public const string PlanSemanticVersion = "composite-guarded-indexed-state-transition-plan-v1";
}

public enum BoundedIndexedStateGuardKind
{
    CompositeLookupComparison,
    ConstantComparison,
    AllOf,
    ModuloComparison,
    ComputedLookupComparison,
}

public sealed class CompositeIndexedStateSelectorDefinition
{
    public CompositeIndexedStateSelectorDefinition(string stateDefinitionId, BigInteger extent)
    {
        StateDefinitionId = stateDefinitionId ?? string.Empty;
        Extent = extent;
    }

    public string StateDefinitionId { get; }
    public BigInteger Extent { get; }
}

public sealed class CompositeIndexedStateLookupCoordinate
{
    public CompositeIndexedStateLookupCoordinate(string stateDefinitionId, BigInteger index)
    {
        StateDefinitionId = stateDefinitionId ?? string.Empty;
        Index = index;
    }

    public string StateDefinitionId { get; }
    public BigInteger Index { get; }
}

public sealed class CompositeIndexedStateLookupEntry
{
    public CompositeIndexedStateLookupEntry(
        IEnumerable<CompositeIndexedStateLookupCoordinate> coordinates,
        BigInteger value)
    {
        OrderedCoordinates = (coordinates ?? throw new ArgumentNullException(nameof(coordinates)))
            .OrderBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Index)
            .ToList()
            .AsReadOnly();
        Value = value;
    }

    public ReadOnlyCollection<CompositeIndexedStateLookupCoordinate> OrderedCoordinates { get; }
    public BigInteger Value { get; }

    internal string CanonicalTuple
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var coordinate in OrderedCoordinates)
            {
                builder.Append(LengthPrefixed(coordinate.StateDefinitionId));
                builder.Append('=').Append(coordinate.Index.ToString(CultureInfo.InvariantCulture)).Append(';');
            }

            return builder.ToString();
        }
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}

public sealed class CompositeIndexedStateLookupTableDefinition
{
    public CompositeIndexedStateLookupTableDefinition(
        string key,
        IEnumerable<CompositeIndexedStateSelectorDefinition> selectors,
        IEnumerable<CompositeIndexedStateLookupEntry> entries,
        string semanticProfile = CompositeGuardedIndexedStateContract.LookupSemanticProfile)
    {
        Key = key ?? string.Empty;
        OrderedSelectors = (selectors ?? throw new ArgumentNullException(nameof(selectors)))
            .OrderBy(item => item.StateDefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Extent)
            .ToList()
            .AsReadOnly();
        OrderedEntries = (entries ?? throw new ArgumentNullException(nameof(entries)))
            .OrderBy(item => item.CanonicalTuple, StringComparer.Ordinal)
            .ThenBy(item => item.Value)
            .ToList()
            .AsReadOnly();
        SemanticProfile = semanticProfile ?? string.Empty;
        CompositeIndexedStateLookupTableId = CompositeGuardedIndexedStateIdentity.ComputeLookupTableId(
            OrderedSelectors,
            OrderedEntries,
            SemanticProfile);
    }

    public string Key { get; }
    public ReadOnlyCollection<CompositeIndexedStateSelectorDefinition> OrderedSelectors { get; }
    public ReadOnlyCollection<CompositeIndexedStateLookupEntry> OrderedEntries { get; }
    public string SemanticProfile { get; }
    public string CompositeIndexedStateLookupTableId { get; }
}

public sealed class CompositeIndexedStateLookupBoundConstraint
{
    public CompositeIndexedStateLookupBoundConstraint(
        string key,
        string subjectStateDefinitionId,
        string lookupTableId,
        IEnumerable<string> selectorStateDefinitionIds,
        IndexedStateLookupComparisonKind comparisonKind)
    {
        Key = key ?? string.Empty;
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        LookupTableId = lookupTableId ?? string.Empty;
        OrderedSelectorStateDefinitionIds = (selectorStateDefinitionIds ??
            throw new ArgumentNullException(nameof(selectorStateDefinitionIds)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        ComparisonKind = comparisonKind;
        CompositeIndexedStateConstraintId = CompositeGuardedIndexedStateIdentity.ComputeConstraintId(
            SubjectStateDefinitionId,
            LookupTableId,
            OrderedSelectorStateDefinitionIds,
            ComparisonKind);
    }

    public string Key { get; }
    public string SubjectStateDefinitionId { get; }
    public string LookupTableId { get; }
    public ReadOnlyCollection<string> OrderedSelectorStateDefinitionIds { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public string CompositeIndexedStateConstraintId { get; }
}

public abstract class BoundedIndexedStateGuardDefinition
{
    protected BoundedIndexedStateGuardDefinition(
        string key,
        BoundedIndexedStateGuardKind guardKind,
        string indexedStateGuardId)
    {
        Key = key ?? string.Empty;
        GuardKind = guardKind;
        IndexedStateGuardId = indexedStateGuardId ?? string.Empty;
    }

    public string Key { get; }
    public BoundedIndexedStateGuardKind GuardKind { get; }
    public string IndexedStateGuardId { get; }
}

public sealed class CompositeIndexedStateLookupComparisonGuard : BoundedIndexedStateGuardDefinition
{
    public CompositeIndexedStateLookupComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string lookupTableId,
        IEnumerable<string> selectorStateDefinitionIds)
        : this(
            key,
            subjectStateDefinitionId,
            signedSubjectOffset,
            comparisonKind,
            lookupTableId,
            CanonicalizeSelectorIds(selectorStateDefinitionIds))
    {
    }

    private CompositeIndexedStateLookupComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string lookupTableId,
        ReadOnlyCollection<string> orderedSelectorStateDefinitionIds)
        : base(
            key,
            BoundedIndexedStateGuardKind.CompositeLookupComparison,
            CompositeGuardedIndexedStateIdentity.ComputeLookupGuardId(
                subjectStateDefinitionId,
                signedSubjectOffset,
                comparisonKind,
                lookupTableId,
                orderedSelectorStateDefinitionIds))
    {
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        SignedSubjectOffset = signedSubjectOffset;
        ComparisonKind = comparisonKind;
        LookupTableId = lookupTableId ?? string.Empty;
        OrderedSelectorStateDefinitionIds = orderedSelectorStateDefinitionIds;
    }

    public string SubjectStateDefinitionId { get; }
    public BigInteger SignedSubjectOffset { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public string LookupTableId { get; }
    public ReadOnlyCollection<string> OrderedSelectorStateDefinitionIds { get; }

    private static ReadOnlyCollection<string> CanonicalizeSelectorIds(IEnumerable<string> values) =>
        (values ?? throw new ArgumentNullException(nameof(values)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
}

public sealed class IndexedStateConstantComparisonGuard : BoundedIndexedStateGuardDefinition
{
    public IndexedStateConstantComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        BigInteger constant)
        : base(
            key,
            BoundedIndexedStateGuardKind.ConstantComparison,
            CompositeGuardedIndexedStateIdentity.ComputeConstantGuardId(
                subjectStateDefinitionId,
                signedSubjectOffset,
                comparisonKind,
                constant))
    {
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        SignedSubjectOffset = signedSubjectOffset;
        ComparisonKind = comparisonKind;
        Constant = constant;
    }

    public string SubjectStateDefinitionId { get; }
    public BigInteger SignedSubjectOffset { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public BigInteger Constant { get; }
}

public sealed class IndexedStateAllOfGuard : BoundedIndexedStateGuardDefinition
{
    public IndexedStateAllOfGuard(
        string key,
        IEnumerable<BoundedIndexedStateGuardDefinition> children)
        : this(key, Canonicalize(children))
    {
    }

    private IndexedStateAllOfGuard(
        string key,
        ReadOnlyCollection<BoundedIndexedStateGuardDefinition> orderedChildren)
        : base(
            key,
            BoundedIndexedStateGuardKind.AllOf,
            CompositeGuardedIndexedStateIdentity.ComputeAllOfGuardId(
                orderedChildren.Select(item => item.IndexedStateGuardId)))
    {
        OrderedChildren = orderedChildren;
    }

    public ReadOnlyCollection<BoundedIndexedStateGuardDefinition> OrderedChildren { get; }

    private static ReadOnlyCollection<BoundedIndexedStateGuardDefinition> Canonicalize(
        IEnumerable<BoundedIndexedStateGuardDefinition> children) =>
        (children ?? throw new ArgumentNullException(nameof(children)))
            .OrderBy(item => item.IndexedStateGuardId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
}

public sealed class CompositeGuardedStateTransitionBranch
{
    public CompositeGuardedStateTransitionBranch(
        string key,
        BoundedIndexedStateGuardDefinition guard,
        IEnumerable<GuardedStateEffectDefinition> effects)
    {
        Key = key ?? string.Empty;
        Guard = guard ?? throw new ArgumentNullException(nameof(guard));
        OrderedEffects = (effects ?? throw new ArgumentNullException(nameof(effects)))
            .OrderBy(item => item.GuardedStateEffectId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        GuardedTransitionBranchId = CompositeGuardedIndexedStateIdentity.ComputeBranchId(
            Guard.IndexedStateGuardId,
            OrderedEffects.Select(item => item.GuardedStateEffectId));
    }

    public string Key { get; }
    public BoundedIndexedStateGuardDefinition Guard { get; }
    public ReadOnlyCollection<GuardedStateEffectDefinition> OrderedEffects { get; }
    public string GuardedTransitionBranchId { get; }
}

public sealed class CompositeGuardedStateTransitionChoice
{
    public CompositeGuardedStateTransitionChoice(
        string key,
        string sourcePeriodicEventDefinitionId,
        IEnumerable<CompositeGuardedStateTransitionBranch> branches,
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
        GuardedTransitionChoiceId = CompositeGuardedIndexedStateIdentity.ComputeChoiceId(
            SourcePeriodicEventDefinitionId,
            OrderedBranches.Select(item => item.GuardedTransitionBranchId),
            MatchPolicy);
    }

    public string Key { get; }
    public string SourcePeriodicEventDefinitionId { get; }
    public ReadOnlyCollection<CompositeGuardedStateTransitionBranch> OrderedBranches { get; }
    public GuardedStateTransitionMatchPolicy MatchPolicy { get; }
    public string GuardedTransitionChoiceId { get; }
}

public sealed class CompositeGuardedIndexedStateTransitionPlan
{
    public CompositeGuardedIndexedStateTransitionPlan(
        IEnumerable<CyclicIndexedStateDefinition> stateDefinitions,
        IEnumerable<CompositeIndexedStateLookupTableDefinition> lookupTables,
        IEnumerable<CompositeIndexedStateLookupBoundConstraint> constraints,
        IEnumerable<IndexedStateTransitionDefinition> unconditionalTransitions,
        IEnumerable<BoundedIndexedStateGuardDefinition> guards,
        IEnumerable<GuardedStateEffectDefinition> effects,
        IEnumerable<CompositeGuardedStateTransitionChoice> guardedChoices,
        string determinismProfile = CompositeGuardedIndexedStateContract.DefaultDeterminismProfile)
    {
        OrderedStateDefinitions = Canonicalize(stateDefinitions, item => item.CyclicStateDefinitionId, item => item.Key);
        OrderedLookupTables = Canonicalize(lookupTables, item => item.CompositeIndexedStateLookupTableId, item => item.Key);
        OrderedConstraints = Canonicalize(constraints, item => item.CompositeIndexedStateConstraintId, item => item.Key);
        OrderedUnconditionalTransitions = Canonicalize(
            unconditionalTransitions,
            item => item.IndexedStateTransitionDefinitionId,
            item => item.Key);
        OrderedGuards = Canonicalize(guards, item => item.IndexedStateGuardId, item => item.Key);
        OrderedEffects = Canonicalize(effects, item => item.GuardedStateEffectId, item => item.Key);
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
        DeterminismProfile = determinismProfile ?? string.Empty;
        GuardedIndexedTransitionPlanId = CompositeGuardedIndexedStateIdentity.ComputePlanId(this);
    }

    public ReadOnlyCollection<CyclicIndexedStateDefinition> OrderedStateDefinitions { get; }
    public ReadOnlyCollection<CompositeIndexedStateLookupTableDefinition> OrderedLookupTables { get; }
    public ReadOnlyCollection<CompositeIndexedStateLookupBoundConstraint> OrderedConstraints { get; }
    public ReadOnlyCollection<IndexedStateTransitionDefinition> OrderedUnconditionalTransitions { get; }
    public ReadOnlyCollection<BoundedIndexedStateGuardDefinition> OrderedGuards { get; }
    public ReadOnlyCollection<GuardedStateEffectDefinition> OrderedEffects { get; }
    public ReadOnlyCollection<CompositeGuardedStateTransitionChoice> OrderedGuardedChoices { get; }
    public ReadOnlyCollection<CompositeGuardedStateTransitionBranch> OrderedBranches { get; }
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

public static class CompositeGuardedIndexedStateIdentity
{
    public static string ComputeLookupTableId(
        IEnumerable<CompositeIndexedStateSelectorDefinition> selectors,
        IEnumerable<CompositeIndexedStateLookupEntry> entries,
        string semanticProfile)
    {
        var builder = new StringBuilder(CompositeGuardedIndexedStateContract.LookupSemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(semanticProfile ?? string.Empty));
        foreach (var selector in selectors.OrderBy(item => item.StateDefinitionId, StringComparer.Ordinal))
        {
            builder.Append("|selectorStateDefinitionId=").Append(LengthPrefixed(selector.StateDefinitionId));
            builder.Append(',').Append(selector.Extent.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var entry in entries.OrderBy(item => item.CanonicalTuple, StringComparer.Ordinal).ThenBy(item => item.Value))
        {
            builder.Append("|entry=").Append(LengthPrefixed(entry.CanonicalTuple));
            builder.Append(',').Append(entry.Value.ToString(CultureInfo.InvariantCulture));
        }

        return PeriodicSemanticIdentity.Hash("composite-indexed-state-lookup-table-sha256:", builder.ToString());
    }

    public static string ComputeConstraintId(
        string subjectStateDefinitionId,
        string lookupTableId,
        IEnumerable<string> selectorStateDefinitionIds,
        IndexedStateLookupComparisonKind comparisonKind)
    {
        var builder = Start(CompositeGuardedIndexedStateContract.ConstraintSemanticVersion,
            ("subjectStateDefinitionId", subjectStateDefinitionId),
            ("lookupTableId", lookupTableId),
            ("comparisonKind", comparisonKind.ToString()));
        AppendIds(builder, "selectorStateDefinitionId", selectorStateDefinitionIds);
        return PeriodicSemanticIdentity.Hash("composite-indexed-state-constraint-sha256:", builder.ToString());
    }

    public static string ComputeLookupGuardId(
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string lookupTableId,
        IEnumerable<string> selectorStateDefinitionIds)
    {
        var builder = Start(CompositeGuardedIndexedStateContract.LookupGuardSemanticVersion,
            ("subjectStateDefinitionId", subjectStateDefinitionId),
            ("signedSubjectOffset", signedSubjectOffset.ToString(CultureInfo.InvariantCulture)),
            ("comparisonKind", comparisonKind.ToString()),
            ("lookupTableId", lookupTableId));
        AppendIds(builder, "selectorStateDefinitionId", selectorStateDefinitionIds);
        return PeriodicSemanticIdentity.Hash("indexed-state-guard-sha256:", builder.ToString());
    }

    public static string ComputeConstantGuardId(
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        BigInteger constant) =>
        PeriodicSemanticIdentity.Hash(
            "indexed-state-guard-sha256:",
            Start(CompositeGuardedIndexedStateContract.ConstantGuardSemanticVersion,
                ("subjectStateDefinitionId", subjectStateDefinitionId),
                ("signedSubjectOffset", signedSubjectOffset.ToString(CultureInfo.InvariantCulture)),
                ("comparisonKind", comparisonKind.ToString()),
                ("constant", constant.ToString(CultureInfo.InvariantCulture))).ToString());

    public static string ComputeAllOfGuardId(IEnumerable<string> childGuardIds)
    {
        var builder = new StringBuilder(CompositeGuardedIndexedStateContract.AllOfGuardSemanticVersion);
        AppendIds(builder, "childGuardId", childGuardIds);
        return PeriodicSemanticIdentity.Hash("indexed-state-guard-sha256:", builder.ToString());
    }

    public static string ComputeBranchId(string guardId, IEnumerable<string> effectIds)
    {
        var builder = new StringBuilder(CompositeGuardedIndexedStateContract.BranchSemanticVersion);
        builder.Append("|guardId=").Append(LengthPrefixed(guardId ?? string.Empty));
        AppendIds(builder, "effectId", effectIds);
        return PeriodicSemanticIdentity.Hash("guarded-transition-branch-sha256:", builder.ToString());
    }

    public static string ComputeChoiceId(
        string sourcePeriodicEventDefinitionId,
        IEnumerable<string> branchIds,
        GuardedStateTransitionMatchPolicy matchPolicy)
    {
        var builder = Start(CompositeGuardedIndexedStateContract.ChoiceSemanticVersion,
            ("sourcePeriodicEventDefinitionId", sourcePeriodicEventDefinitionId),
            ("matchPolicy", matchPolicy.ToString()));
        AppendIds(builder, "branchId", branchIds);
        return PeriodicSemanticIdentity.Hash("guarded-transition-choice-sha256:", builder.ToString());
    }

    public static string ComputePlanId(CompositeGuardedIndexedStateTransitionPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        var builder = new StringBuilder(CompositeGuardedIndexedStateContract.PlanSemanticVersion);
        builder.Append("|profile=").Append(LengthPrefixed(plan.DeterminismProfile));
        AppendIds(builder, "stateDefinitionId", plan.OrderedStateDefinitions.Select(item => item.CyclicStateDefinitionId));
        AppendIds(builder, "lookupTableId", plan.OrderedLookupTables.Select(item => item.CompositeIndexedStateLookupTableId));
        AppendIds(builder, "constraintId", plan.OrderedConstraints.Select(item => item.CompositeIndexedStateConstraintId));
        AppendIds(builder, "unconditionalTransitionId", plan.OrderedUnconditionalTransitions.Select(item => item.IndexedStateTransitionDefinitionId));
        AppendIds(builder, "guardId", plan.OrderedGuards.Select(item => item.IndexedStateGuardId));
        AppendIds(builder, "effectId", plan.OrderedEffects.Select(item => item.GuardedStateEffectId));
        AppendIds(builder, "branchId", plan.OrderedBranches.Select(item => item.GuardedTransitionBranchId));
        AppendIds(builder, "choiceId", plan.OrderedGuardedChoices.Select(item => item.GuardedTransitionChoiceId));
        return PeriodicSemanticIdentity.Hash("guarded-indexed-transition-plan-sha256:", builder.ToString());
    }

    private static StringBuilder Start(string version, params (string Name, string? Value)[] fields)
    {
        var builder = new StringBuilder(version);
        foreach (var field in fields)
        {
            builder.Append('|').Append(field.Name).Append('=').Append(LengthPrefixed(field.Value ?? string.Empty));
        }

        return builder;
    }

    private static void AppendIds(StringBuilder builder, string name, IEnumerable<string> ids)
    {
        foreach (var id in (ids ?? throw new ArgumentNullException(nameof(ids))).OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append('|').Append(name).Append('=').Append(LengthPrefixed(id ?? string.Empty));
        }
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
