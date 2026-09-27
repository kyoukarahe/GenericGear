using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GearInvest.Core;

public static class ComputedGuardedIndexedStateContract
{
    public const string DefaultDeterminismProfile = "portable-managed-gregorian400-v1";
    public const string ModuloGuardSemanticProfile = "exact-big-integer-modulo-comparison-v1";
    public const string ModuloGuardSemanticVersion = "indexed-state-modulo-comparison-guard-v1";
    public const string ComputedCaseSemanticVersion = "computed-indexed-value-case-v1";
    public const string ComputedDefinitionSemanticProfile = "exactly-one-finite-classification-v1";
    public const string ComputedDefinitionSemanticVersion = "computed-indexed-value-definition-v1";
    public const string LookupSemanticProfile = "exact-integer-state-computed-two-selector-dense-lookup-v1";
    public const string LookupSemanticVersion = "computed-selector-indexed-lookup-table-v1";
    public const string ConstraintSemanticVersion = "computed-selector-indexed-lookup-bound-constraint-v1";
    public const string LookupGuardSemanticVersion = "computed-selector-indexed-lookup-comparison-guard-v1";
    public const string BranchSemanticVersion = "computed-guarded-state-transition-branch-v1";
    public const string ChoiceSemanticVersion = "computed-guarded-state-transition-choice-v1";
    public const string PlanSemanticVersion = "computed-guarded-indexed-state-transition-plan-v1";
}

public enum IndexedStateModuloComparisonKind
{
    Equal,
    NotEqual,
}

public enum ComputedIndexedSelectorKind
{
    State,
    ComputedIndexedValue,
}

public sealed class IndexedStateModuloComparisonGuard : BoundedIndexedStateGuardDefinition
{
    public IndexedStateModuloComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger modulus,
        IndexedStateModuloComparisonKind comparisonKind,
        BigInteger residue,
        string semanticProfile = ComputedGuardedIndexedStateContract.ModuloGuardSemanticProfile)
        : base(
            key,
            BoundedIndexedStateGuardKind.ModuloComparison,
            ComputedGuardedIndexedStateIdentity.ComputeModuloGuardId(
                subjectStateDefinitionId,
                modulus,
                comparisonKind,
                residue,
                semanticProfile))
    {
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        Modulus = modulus;
        ComparisonKind = comparisonKind;
        Residue = residue;
        SemanticProfile = semanticProfile ?? string.Empty;
    }

    public string SubjectStateDefinitionId { get; }
    public BigInteger Modulus { get; }
    public IndexedStateModuloComparisonKind ComparisonKind { get; }
    public BigInteger Residue { get; }
    public string SemanticProfile { get; }
}

public sealed class ComputedIndexedValueCase
{
    public ComputedIndexedValueCase(
        string key,
        BoundedIndexedStateGuardDefinition guard,
        BigInteger outputIndex)
    {
        Key = key ?? string.Empty;
        Guard = guard ?? throw new ArgumentNullException(nameof(guard));
        OutputIndex = outputIndex;
        ComputedIndexedValueCaseId = ComputedGuardedIndexedStateIdentity.ComputeComputedCaseId(
            Guard.IndexedStateGuardId,
            OutputIndex);
    }

    public string Key { get; }
    public BoundedIndexedStateGuardDefinition Guard { get; }
    public BigInteger OutputIndex { get; }
    public string ComputedIndexedValueCaseId { get; }
}

public sealed class ComputedIndexedValueDefinition
{
    public ComputedIndexedValueDefinition(
        string key,
        BigInteger outputCardinality,
        IEnumerable<ComputedIndexedValueCase> cases,
        GuardedStateTransitionMatchPolicy matchPolicy,
        string semanticProfile = ComputedGuardedIndexedStateContract.ComputedDefinitionSemanticProfile)
    {
        Key = key ?? string.Empty;
        OutputCardinality = outputCardinality;
        OrderedCases = (cases ?? throw new ArgumentNullException(nameof(cases)))
            .OrderBy(item => item.ComputedIndexedValueCaseId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        MatchPolicy = matchPolicy;
        SemanticProfile = semanticProfile ?? string.Empty;
        ComputedIndexedValueDefinitionId = ComputedGuardedIndexedStateIdentity.ComputeComputedDefinitionId(
            Key,
            OutputCardinality,
            OrderedCases.Select(item => item.ComputedIndexedValueCaseId),
            MatchPolicy,
            SemanticProfile);
    }

    public string Key { get; }
    public BigInteger OutputCardinality { get; }
    public ReadOnlyCollection<ComputedIndexedValueCase> OrderedCases { get; }
    public GuardedStateTransitionMatchPolicy MatchPolicy { get; }
    public string SemanticProfile { get; }
    public string ComputedIndexedValueDefinitionId { get; }
}

public sealed class ComputedIndexedValueEvaluation
{
    public ComputedIndexedValueEvaluation(
        string periodicEventOccurrenceId,
        string periodicEventDefinitionId,
        BigInteger eventOrdinal,
        Rational rootTurnsAtEvaluation,
        string computedIndexedValueDefinitionId,
        string selectedCaseId,
        BigInteger outputIndex)
    {
        PeriodicEventOccurrenceId = periodicEventOccurrenceId ?? string.Empty;
        PeriodicEventDefinitionId = periodicEventDefinitionId ?? string.Empty;
        EventOrdinal = eventOrdinal;
        RootTurnsAtEvaluation = rootTurnsAtEvaluation;
        ComputedIndexedValueDefinitionId = computedIndexedValueDefinitionId ?? string.Empty;
        SelectedCaseId = selectedCaseId ?? string.Empty;
        OutputIndex = outputIndex;
    }

    public string PeriodicEventOccurrenceId { get; }
    public string PeriodicEventDefinitionId { get; }
    public BigInteger EventOrdinal { get; }
    public Rational RootTurnsAtEvaluation { get; }
    public string ComputedIndexedValueDefinitionId { get; }
    public string SelectedCaseId { get; }
    public BigInteger OutputIndex { get; }
}

public sealed class ComputedIndexedSelectorDefinition
{
    public ComputedIndexedSelectorDefinition(
        ComputedIndexedSelectorKind selectorKind,
        string referenceId,
        BigInteger extent)
    {
        SelectorKind = selectorKind;
        ReferenceId = referenceId ?? string.Empty;
        Extent = extent;
    }

    public ComputedIndexedSelectorKind SelectorKind { get; }
    public string ReferenceId { get; }
    public BigInteger Extent { get; }
    public string CanonicalReference => ComputedGuardedIndexedStateIdentity.SelectorKindToWire(SelectorKind) +
        ":" + ReferenceId;
}

public sealed class ComputedIndexedLookupCoordinate
{
    public ComputedIndexedLookupCoordinate(
        ComputedIndexedSelectorKind selectorKind,
        string referenceId,
        BigInteger index)
    {
        SelectorKind = selectorKind;
        ReferenceId = referenceId ?? string.Empty;
        Index = index;
    }

    public ComputedIndexedSelectorKind SelectorKind { get; }
    public string ReferenceId { get; }
    public BigInteger Index { get; }
    public string CanonicalReference => ComputedGuardedIndexedStateIdentity.SelectorKindToWire(SelectorKind) +
        ":" + ReferenceId;
}

public sealed class ComputedSelectorIndexedLookupEntry
{
    public ComputedSelectorIndexedLookupEntry(
        IEnumerable<ComputedIndexedLookupCoordinate> coordinates,
        BigInteger value)
    {
        OrderedCoordinates = (coordinates ?? throw new ArgumentNullException(nameof(coordinates)))
            .OrderBy(item => item.CanonicalReference, StringComparer.Ordinal)
            .ThenBy(item => item.Index)
            .ToList()
            .AsReadOnly();
        Value = value;
    }

    public ReadOnlyCollection<ComputedIndexedLookupCoordinate> OrderedCoordinates { get; }
    public BigInteger Value { get; }

    internal string CanonicalTuple
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var coordinate in OrderedCoordinates)
            {
                builder.Append(LengthPrefixed(coordinate.CanonicalReference));
                builder.Append('=').Append(coordinate.Index.ToString(CultureInfo.InvariantCulture)).Append(';');
            }

            return builder.ToString();
        }
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}

public sealed class ComputedSelectorIndexedLookupTableDefinition
{
    public ComputedSelectorIndexedLookupTableDefinition(
        string key,
        IEnumerable<ComputedIndexedSelectorDefinition> selectors,
        IEnumerable<ComputedSelectorIndexedLookupEntry> entries,
        string semanticProfile = ComputedGuardedIndexedStateContract.LookupSemanticProfile)
    {
        Key = key ?? string.Empty;
        OrderedSelectors = (selectors ?? throw new ArgumentNullException(nameof(selectors)))
            .OrderBy(item => item.CanonicalReference, StringComparer.Ordinal)
            .ThenBy(item => item.Extent)
            .ToList()
            .AsReadOnly();
        OrderedEntries = (entries ?? throw new ArgumentNullException(nameof(entries)))
            .OrderBy(item => item.CanonicalTuple, StringComparer.Ordinal)
            .ThenBy(item => item.Value)
            .ToList()
            .AsReadOnly();
        SemanticProfile = semanticProfile ?? string.Empty;
        ComputedSelectorIndexedLookupTableId = ComputedGuardedIndexedStateIdentity.ComputeLookupTableId(
            OrderedSelectors,
            OrderedEntries,
            SemanticProfile);
    }

    public string Key { get; }
    public ReadOnlyCollection<ComputedIndexedSelectorDefinition> OrderedSelectors { get; }
    public ReadOnlyCollection<ComputedSelectorIndexedLookupEntry> OrderedEntries { get; }
    public string SemanticProfile { get; }
    public string ComputedSelectorIndexedLookupTableId { get; }
}

public sealed class ComputedSelectorIndexedLookupBoundConstraint
{
    public ComputedSelectorIndexedLookupBoundConstraint(
        string key,
        string subjectStateDefinitionId,
        string lookupTableId,
        IEnumerable<ComputedIndexedSelectorDefinition> selectors,
        IndexedStateLookupComparisonKind comparisonKind)
    {
        Key = key ?? string.Empty;
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        LookupTableId = lookupTableId ?? string.Empty;
        OrderedSelectors = (selectors ?? throw new ArgumentNullException(nameof(selectors)))
            .OrderBy(item => item.CanonicalReference, StringComparer.Ordinal)
            .ThenBy(item => item.Extent)
            .ToList()
            .AsReadOnly();
        ComparisonKind = comparisonKind;
        ComputedSelectorIndexedStateConstraintId = ComputedGuardedIndexedStateIdentity.ComputeConstraintId(
            SubjectStateDefinitionId,
            LookupTableId,
            OrderedSelectors,
            ComparisonKind);
    }

    public string Key { get; }
    public string SubjectStateDefinitionId { get; }
    public string LookupTableId { get; }
    public ReadOnlyCollection<ComputedIndexedSelectorDefinition> OrderedSelectors { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public string ComputedSelectorIndexedStateConstraintId { get; }
}

public sealed class ComputedSelectorIndexedLookupComparisonGuard : BoundedIndexedStateGuardDefinition
{
    public ComputedSelectorIndexedLookupComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string lookupTableId,
        IEnumerable<ComputedIndexedSelectorDefinition> selectors)
        : this(
            key,
            subjectStateDefinitionId,
            signedSubjectOffset,
            comparisonKind,
            lookupTableId,
            Canonicalize(selectors))
    {
    }

    private ComputedSelectorIndexedLookupComparisonGuard(
        string key,
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string lookupTableId,
        ReadOnlyCollection<ComputedIndexedSelectorDefinition> orderedSelectors)
        : base(
            key,
            BoundedIndexedStateGuardKind.ComputedLookupComparison,
            ComputedGuardedIndexedStateIdentity.ComputeLookupGuardId(
                subjectStateDefinitionId,
                signedSubjectOffset,
                comparisonKind,
                lookupTableId,
                orderedSelectors))
    {
        SubjectStateDefinitionId = subjectStateDefinitionId ?? string.Empty;
        SignedSubjectOffset = signedSubjectOffset;
        ComparisonKind = comparisonKind;
        LookupTableId = lookupTableId ?? string.Empty;
        OrderedSelectors = orderedSelectors;
    }

    public string SubjectStateDefinitionId { get; }
    public BigInteger SignedSubjectOffset { get; }
    public IndexedStateLookupComparisonKind ComparisonKind { get; }
    public string LookupTableId { get; }
    public ReadOnlyCollection<ComputedIndexedSelectorDefinition> OrderedSelectors { get; }

    private static ReadOnlyCollection<ComputedIndexedSelectorDefinition> Canonicalize(
        IEnumerable<ComputedIndexedSelectorDefinition> selectors) =>
        (selectors ?? throw new ArgumentNullException(nameof(selectors)))
            .OrderBy(item => item.CanonicalReference, StringComparer.Ordinal)
            .ThenBy(item => item.Extent)
            .ToList()
            .AsReadOnly();
}

public sealed class ComputedGuardedStateTransitionBranch
{
    public ComputedGuardedStateTransitionBranch(
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
        GuardedTransitionBranchId = ComputedGuardedIndexedStateIdentity.ComputeBranchId(
            Guard.IndexedStateGuardId,
            OrderedEffects.Select(item => item.GuardedStateEffectId));
    }

    public string Key { get; }
    public BoundedIndexedStateGuardDefinition Guard { get; }
    public ReadOnlyCollection<GuardedStateEffectDefinition> OrderedEffects { get; }
    public string GuardedTransitionBranchId { get; }
}

public sealed class ComputedGuardedStateTransitionChoice
{
    public ComputedGuardedStateTransitionChoice(
        string key,
        string sourcePeriodicEventDefinitionId,
        IEnumerable<ComputedGuardedStateTransitionBranch> branches,
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
        GuardedTransitionChoiceId = ComputedGuardedIndexedStateIdentity.ComputeChoiceId(
            SourcePeriodicEventDefinitionId,
            OrderedBranches.Select(item => item.GuardedTransitionBranchId),
            MatchPolicy);
    }

    public string Key { get; }
    public string SourcePeriodicEventDefinitionId { get; }
    public ReadOnlyCollection<ComputedGuardedStateTransitionBranch> OrderedBranches { get; }
    public GuardedStateTransitionMatchPolicy MatchPolicy { get; }
    public string GuardedTransitionChoiceId { get; }
}

public sealed class ComputedGuardedIndexedStateTransitionPlan
{
    public ComputedGuardedIndexedStateTransitionPlan(
        IEnumerable<CyclicIndexedStateDefinition> stateDefinitions,
        IEnumerable<BoundedIndexedStateGuardDefinition> guards,
        IEnumerable<ComputedIndexedValueDefinition> computedDefinitions,
        IEnumerable<ComputedSelectorIndexedLookupTableDefinition> lookupTables,
        IEnumerable<ComputedSelectorIndexedLookupBoundConstraint> constraints,
        IEnumerable<IndexedStateTransitionDefinition> unconditionalTransitions,
        IEnumerable<GuardedStateEffectDefinition> effects,
        IEnumerable<ComputedGuardedStateTransitionChoice> guardedChoices,
        string determinismProfile = ComputedGuardedIndexedStateContract.DefaultDeterminismProfile)
    {
        OrderedStateDefinitions = Canonicalize(stateDefinitions, item => item.CyclicStateDefinitionId, item => item.Key);
        OrderedGuards = Canonicalize(guards, item => item.IndexedStateGuardId, item => item.Key);
        OrderedComputedDefinitions = Canonicalize(
            computedDefinitions,
            item => item.ComputedIndexedValueDefinitionId,
            item => item.Key);
        OrderedComputedCases = OrderedComputedDefinitions
            .SelectMany(item => item.OrderedCases)
            .OrderBy(item => item.ComputedIndexedValueCaseId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        OrderedLookupTables = Canonicalize(
            lookupTables,
            item => item.ComputedSelectorIndexedLookupTableId,
            item => item.Key);
        OrderedConstraints = Canonicalize(
            constraints,
            item => item.ComputedSelectorIndexedStateConstraintId,
            item => item.Key);
        OrderedUnconditionalTransitions = Canonicalize(
            unconditionalTransitions,
            item => item.IndexedStateTransitionDefinitionId,
            item => item.Key);
        OrderedEffects = Canonicalize(effects, item => item.GuardedStateEffectId, item => item.Key);
        OrderedGuardedChoices = Canonicalize(
            guardedChoices,
            item => item.GuardedTransitionChoiceId,
            item => item.Key);
        OrderedBranches = OrderedGuardedChoices.SelectMany(item => item.OrderedBranches)
            .OrderBy(item => item.GuardedTransitionBranchId, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        DeterminismProfile = determinismProfile ?? string.Empty;
        GuardedIndexedTransitionPlanId = ComputedGuardedIndexedStateIdentity.ComputePlanId(this);
    }

    public ReadOnlyCollection<CyclicIndexedStateDefinition> OrderedStateDefinitions { get; }
    public ReadOnlyCollection<BoundedIndexedStateGuardDefinition> OrderedGuards { get; }
    public ReadOnlyCollection<ComputedIndexedValueDefinition> OrderedComputedDefinitions { get; }
    public ReadOnlyCollection<ComputedIndexedValueCase> OrderedComputedCases { get; }
    public ReadOnlyCollection<ComputedSelectorIndexedLookupTableDefinition> OrderedLookupTables { get; }
    public ReadOnlyCollection<ComputedSelectorIndexedLookupBoundConstraint> OrderedConstraints { get; }
    public ReadOnlyCollection<IndexedStateTransitionDefinition> OrderedUnconditionalTransitions { get; }
    public ReadOnlyCollection<GuardedStateEffectDefinition> OrderedEffects { get; }
    public ReadOnlyCollection<ComputedGuardedStateTransitionChoice> OrderedGuardedChoices { get; }
    public ReadOnlyCollection<ComputedGuardedStateTransitionBranch> OrderedBranches { get; }
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

public static class ComputedGuardedIndexedStateIdentity
{
    public static string ComputeModuloGuardId(
        string subjectStateDefinitionId,
        BigInteger modulus,
        IndexedStateModuloComparisonKind comparisonKind,
        BigInteger residue,
        string semanticProfile) =>
        PeriodicSemanticIdentity.Hash(
            "indexed-state-guard-sha256:",
            Start(
                ComputedGuardedIndexedStateContract.ModuloGuardSemanticVersion,
                ("profile", semanticProfile),
                ("subjectStateDefinitionId", subjectStateDefinitionId),
                ("modulus", modulus.ToString(CultureInfo.InvariantCulture)),
                ("comparisonKind", comparisonKind.ToString()),
                ("residue", residue.ToString(CultureInfo.InvariantCulture))).ToString());

    public static string ComputeComputedCaseId(string guardId, BigInteger outputIndex) =>
        PeriodicSemanticIdentity.Hash(
            "computed-indexed-value-case-sha256:",
            Start(
                ComputedGuardedIndexedStateContract.ComputedCaseSemanticVersion,
                ("guardId", guardId),
                ("outputIndex", outputIndex.ToString(CultureInfo.InvariantCulture))).ToString());

    public static string ComputeComputedDefinitionId(
        string key,
        BigInteger outputCardinality,
        IEnumerable<string> caseIds,
        GuardedStateTransitionMatchPolicy matchPolicy,
        string semanticProfile)
    {
        var builder = Start(
            ComputedGuardedIndexedStateContract.ComputedDefinitionSemanticVersion,
            ("profile", semanticProfile),
            ("key", key),
            ("outputCardinality", outputCardinality.ToString(CultureInfo.InvariantCulture)),
            ("matchPolicy", matchPolicy.ToString()));
        AppendIds(builder, "caseId", caseIds);
        return PeriodicSemanticIdentity.Hash("computed-indexed-value-definition-sha256:", builder.ToString());
    }

    public static string ComputeLookupTableId(
        IEnumerable<ComputedIndexedSelectorDefinition> selectors,
        IEnumerable<ComputedSelectorIndexedLookupEntry> entries,
        string semanticProfile)
    {
        var builder = Start(ComputedGuardedIndexedStateContract.LookupSemanticVersion, ("profile", semanticProfile));
        foreach (var selector in selectors.OrderBy(item => item.CanonicalReference, StringComparer.Ordinal))
        {
            builder.Append("|selector=").Append(LengthPrefixed(selector.CanonicalReference));
            builder.Append(',').Append(selector.Extent.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var entry in entries.OrderBy(item => item.CanonicalTuple, StringComparer.Ordinal).ThenBy(item => item.Value))
        {
            builder.Append("|entry=").Append(LengthPrefixed(entry.CanonicalTuple));
            builder.Append(',').Append(entry.Value.ToString(CultureInfo.InvariantCulture));
        }

        return PeriodicSemanticIdentity.Hash("computed-selector-indexed-lookup-table-sha256:", builder.ToString());
    }

    public static string ComputeConstraintId(
        string subjectStateDefinitionId,
        string lookupTableId,
        IEnumerable<ComputedIndexedSelectorDefinition> selectors,
        IndexedStateLookupComparisonKind comparisonKind)
    {
        var builder = Start(
            ComputedGuardedIndexedStateContract.ConstraintSemanticVersion,
            ("subjectStateDefinitionId", subjectStateDefinitionId),
            ("lookupTableId", lookupTableId),
            ("comparisonKind", comparisonKind.ToString()));
        AppendSelectors(builder, selectors);
        return PeriodicSemanticIdentity.Hash("computed-selector-indexed-state-constraint-sha256:", builder.ToString());
    }

    public static string ComputeLookupGuardId(
        string subjectStateDefinitionId,
        BigInteger signedSubjectOffset,
        IndexedStateLookupComparisonKind comparisonKind,
        string lookupTableId,
        IEnumerable<ComputedIndexedSelectorDefinition> selectors)
    {
        var builder = Start(
            ComputedGuardedIndexedStateContract.LookupGuardSemanticVersion,
            ("subjectStateDefinitionId", subjectStateDefinitionId),
            ("signedSubjectOffset", signedSubjectOffset.ToString(CultureInfo.InvariantCulture)),
            ("comparisonKind", comparisonKind.ToString()),
            ("lookupTableId", lookupTableId));
        AppendSelectors(builder, selectors);
        return PeriodicSemanticIdentity.Hash("indexed-state-guard-sha256:", builder.ToString());
    }

    public static string ComputeBranchId(string guardId, IEnumerable<string> effectIds)
    {
        var builder = Start(ComputedGuardedIndexedStateContract.BranchSemanticVersion, ("guardId", guardId));
        AppendIds(builder, "effectId", effectIds);
        return PeriodicSemanticIdentity.Hash("guarded-transition-branch-sha256:", builder.ToString());
    }

    public static string ComputeChoiceId(
        string sourcePeriodicEventDefinitionId,
        IEnumerable<string> branchIds,
        GuardedStateTransitionMatchPolicy matchPolicy)
    {
        var builder = Start(
            ComputedGuardedIndexedStateContract.ChoiceSemanticVersion,
            ("sourcePeriodicEventDefinitionId", sourcePeriodicEventDefinitionId),
            ("matchPolicy", matchPolicy.ToString()));
        AppendIds(builder, "branchId", branchIds);
        return PeriodicSemanticIdentity.Hash("guarded-transition-choice-sha256:", builder.ToString());
    }

    public static string ComputePlanId(ComputedGuardedIndexedStateTransitionPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        var builder = Start(
            ComputedGuardedIndexedStateContract.PlanSemanticVersion,
            ("profile", plan.DeterminismProfile));
        AppendIds(builder, "stateDefinitionId", plan.OrderedStateDefinitions.Select(item => item.CyclicStateDefinitionId));
        AppendIds(builder, "guardId", plan.OrderedGuards.Select(item => item.IndexedStateGuardId));
        AppendIds(builder, "computedCaseId", plan.OrderedComputedCases.Select(item => item.ComputedIndexedValueCaseId));
        AppendIds(builder, "computedDefinitionId", plan.OrderedComputedDefinitions.Select(item => item.ComputedIndexedValueDefinitionId));
        AppendIds(builder, "lookupTableId", plan.OrderedLookupTables.Select(item => item.ComputedSelectorIndexedLookupTableId));
        AppendIds(builder, "constraintId", plan.OrderedConstraints.Select(item => item.ComputedSelectorIndexedStateConstraintId));
        AppendIds(builder, "unconditionalTransitionId", plan.OrderedUnconditionalTransitions.Select(item => item.IndexedStateTransitionDefinitionId));
        AppendIds(builder, "effectId", plan.OrderedEffects.Select(item => item.GuardedStateEffectId));
        AppendIds(builder, "branchId", plan.OrderedBranches.Select(item => item.GuardedTransitionBranchId));
        AppendIds(builder, "choiceId", plan.OrderedGuardedChoices.Select(item => item.GuardedTransitionChoiceId));
        return PeriodicSemanticIdentity.Hash("guarded-indexed-transition-plan-sha256:", builder.ToString());
    }

    public static string SelectorKindToWire(ComputedIndexedSelectorKind kind) =>
        kind switch
        {
            ComputedIndexedSelectorKind.State => "state",
            ComputedIndexedSelectorKind.ComputedIndexedValue => "computedIndexedValue",
            _ => "unknown",
        };

    private static void AppendSelectors(
        StringBuilder builder,
        IEnumerable<ComputedIndexedSelectorDefinition> selectors)
    {
        foreach (var selector in selectors.OrderBy(item => item.CanonicalReference, StringComparer.Ordinal))
        {
            builder.Append("|selector=").Append(LengthPrefixed(selector.CanonicalReference));
            builder.Append(',').Append(selector.Extent.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendIds(StringBuilder builder, string name, IEnumerable<string> ids)
    {
        foreach (var id in (ids ?? throw new ArgumentNullException(nameof(ids))).OrderBy(item => item, StringComparer.Ordinal))
        {
            builder.Append('|').Append(name).Append('=').Append(LengthPrefixed(id ?? string.Empty));
        }
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

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
