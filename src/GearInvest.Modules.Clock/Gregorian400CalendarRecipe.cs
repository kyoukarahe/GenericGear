using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Modules.Clock;

public sealed class WeekdayGregorian400CalendarTransitionRecipe
{
    internal WeekdayGregorian400CalendarTransitionRecipe(
        Weekday7TransitionRecipe weekday7,
        CyclicIndexedStateDefinition yearResidueStateDefinition,
        CyclicIndexedStateDefinition monthStateDefinition,
        CyclicIndexedStateDefinition dateStateDefinition,
        IEnumerable<IndexedStateModuloComparisonGuard> moduloGuards,
        IEnumerable<IndexedStateAllOfGuard> classificationConjunctions,
        ComputedIndexedValueDefinition leapClassDefinition,
        ComputedSelectorIndexedLookupTableDefinition monthLengthLookup,
        ComputedSelectorIndexedLookupBoundConstraint dateConstraint,
        ComputedSelectorIndexedLookupComparisonGuard normalDayGuard,
        ComputedSelectorIndexedLookupComparisonGuard dateAtEndGuard,
        IndexedStateConstantComparisonGuard monthBeforeDecemberGuard,
        IndexedStateConstantComparisonGuard monthIsDecemberGuard,
        IndexedStateAllOfGuard monthEndGuard,
        IndexedStateAllOfGuard yearEndGuard,
        GuardedStateEffectDefinition dateIncrementEffect,
        GuardedStateEffectDefinition dateResetEffect,
        GuardedStateEffectDefinition monthIncrementEffect,
        GuardedStateEffectDefinition yearIncrementEffect,
        ComputedGuardedStateTransitionBranch normalDayBranch,
        ComputedGuardedStateTransitionBranch monthEndBranch,
        ComputedGuardedStateTransitionBranch yearEndBranch,
        ComputedGuardedStateTransitionChoice dayBoundaryChoice,
        ComputedGuardedIndexedStateTransitionPlan transitionPlan,
        IEnumerable<string> monthLabels)
    {
        Weekday7 = weekday7;
        YearResidueStateDefinition = yearResidueStateDefinition;
        MonthStateDefinition = monthStateDefinition;
        DateStateDefinition = dateStateDefinition;
        ModuloGuards = moduloGuards.OrderBy(item => item.IndexedStateGuardId, StringComparer.Ordinal)
            .ToList().AsReadOnly();
        ClassificationConjunctions = classificationConjunctions
            .OrderBy(item => item.IndexedStateGuardId, StringComparer.Ordinal).ToList().AsReadOnly();
        LeapClassDefinition = leapClassDefinition;
        MonthLengthLookup = monthLengthLookup;
        DateConstraint = dateConstraint;
        NormalDayGuard = normalDayGuard;
        DateAtEndGuard = dateAtEndGuard;
        MonthBeforeDecemberGuard = monthBeforeDecemberGuard;
        MonthIsDecemberGuard = monthIsDecemberGuard;
        MonthEndGuard = monthEndGuard;
        YearEndGuard = yearEndGuard;
        DateIncrementEffect = dateIncrementEffect;
        DateResetEffect = dateResetEffect;
        MonthIncrementEffect = monthIncrementEffect;
        YearIncrementEffect = yearIncrementEffect;
        NormalDayBranch = normalDayBranch;
        MonthEndBranch = monthEndBranch;
        YearEndBranch = yearEndBranch;
        DayBoundaryChoice = dayBoundaryChoice;
        TransitionPlan = transitionPlan;
        MonthLabels = monthLabels.ToList().AsReadOnly();
    }

    public Weekday7TransitionRecipe Weekday7 { get; }
    public CyclicIndexedStateDefinition YearResidueStateDefinition { get; }
    public CyclicIndexedStateDefinition MonthStateDefinition { get; }
    public CyclicIndexedStateDefinition DateStateDefinition { get; }
    public ReadOnlyCollection<IndexedStateModuloComparisonGuard> ModuloGuards { get; }
    public ReadOnlyCollection<IndexedStateAllOfGuard> ClassificationConjunctions { get; }
    public ComputedIndexedValueDefinition LeapClassDefinition { get; }
    public ComputedSelectorIndexedLookupTableDefinition MonthLengthLookup { get; }
    public ComputedSelectorIndexedLookupBoundConstraint DateConstraint { get; }
    public ComputedSelectorIndexedLookupComparisonGuard NormalDayGuard { get; }
    public ComputedSelectorIndexedLookupComparisonGuard DateAtEndGuard { get; }
    public IndexedStateConstantComparisonGuard MonthBeforeDecemberGuard { get; }
    public IndexedStateConstantComparisonGuard MonthIsDecemberGuard { get; }
    public IndexedStateAllOfGuard MonthEndGuard { get; }
    public IndexedStateAllOfGuard YearEndGuard { get; }
    public GuardedStateEffectDefinition DateIncrementEffect { get; }
    public GuardedStateEffectDefinition DateResetEffect { get; }
    public GuardedStateEffectDefinition MonthIncrementEffect { get; }
    public GuardedStateEffectDefinition YearIncrementEffect { get; }
    public ComputedGuardedStateTransitionBranch NormalDayBranch { get; }
    public ComputedGuardedStateTransitionBranch MonthEndBranch { get; }
    public ComputedGuardedStateTransitionBranch YearEndBranch { get; }
    public ComputedGuardedStateTransitionChoice DayBoundaryChoice { get; }
    public ComputedGuardedIndexedStateTransitionPlan TransitionPlan { get; }
    public ReadOnlyCollection<string> MonthLabels { get; }
}

public static class Gregorian400CalendarRecipe
{
    public const string YearResidueStateKey = "state:gregorian-year-residue400";
    public const string LeapClassKey = "computed:gregorian-leap-class2";
    public const string LookupKey = "lookup:gregorian400-derived-month-length";
    public const string ConstraintKey = "constraint:date-in-gregorian400-derived-month";
    public const string Mod400EqualGuardKey = "guard:gregorian-year-mod400-equal-zero";
    public const string Mod400NotEqualGuardKey = "guard:gregorian-year-mod400-not-equal-zero";
    public const string Mod100EqualGuardKey = "guard:gregorian-year-mod100-equal-zero";
    public const string Mod100NotEqualGuardKey = "guard:gregorian-year-mod100-not-equal-zero";
    public const string Mod4EqualGuardKey = "guard:gregorian-year-mod4-equal-zero";
    public const string Mod4NotEqualGuardKey = "guard:gregorian-year-mod4-not-equal-zero";
    public const string CenturyCommonGuardKey = "guard:gregorian-century-common";
    public const string OrdinaryLeapGuardKey = "guard:gregorian-ordinary-leap";
    public const string DivisibleBy400LeapCaseKey = "case:gregorian-divisible-by-400-leap";
    public const string CenturyCommonCaseKey = "case:gregorian-century-common";
    public const string OrdinaryLeapCaseKey = "case:gregorian-ordinary-leap";
    public const string OrdinaryCommonCaseKey = "case:gregorian-ordinary-common";
    public const string NormalDayGuardKey = "guard:gregorian400-normal-day";
    public const string DateAtEndGuardKey = "guard:gregorian400-date-at-end";
    public const string MonthBeforeDecemberGuardKey = "guard:gregorian400-month-before-december";
    public const string MonthIsDecemberGuardKey = "guard:gregorian400-month-is-december";
    public const string MonthEndGuardKey = "guard:gregorian400-month-end";
    public const string YearEndGuardKey = "guard:gregorian400-year-end";
    public const string YearIncrementEffectKey = "effect:gregorian-year-residue400-add-one";
    public const string NormalDayBranchKey = "branch:gregorian400-normal-day";
    public const string MonthEndBranchKey = "branch:gregorian400-month-end";
    public const string YearEndBranchKey = "branch:gregorian400-year-end";
    public const string DayBoundaryChoiceKey = "choice:gregorian400-day-boundary";
    public const int YearResidueCycleLength = 400;
    public const int LeapClassCardinality = 2;
    public const int CommonClassIndex = 0;
    public const int LeapClassIndex = 1;

    private static readonly BigInteger[] CommonMonthLengths =
    {
        31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31,
    };

    private static readonly string[] DefaultMonthLabels =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    public static WeekdayGregorian400CalendarTransitionRecipe Create(
        string dayBoundaryPeriodicEventDefinitionId,
        IEnumerable<string>? weekdayDisplayLabels = null,
        IEnumerable<string>? monthDisplayLabels = null)
    {
        var monthLabels = ValidateLabels(
            monthDisplayLabels ?? DefaultMonthLabels,
            CommonYearMonthLengthRecipe.MonthCycleLength,
            nameof(monthDisplayLabels));
        var weekday = Weekday7Recipe.Create(dayBoundaryPeriodicEventDefinitionId, weekdayDisplayLabels);
        var year = new CyclicIndexedStateDefinition(YearResidueStateKey, YearResidueCycleLength);
        var month = new CyclicIndexedStateDefinition(
            CommonYearMonthLengthRecipe.MonthStateKey,
            CommonYearMonthLengthRecipe.MonthCycleLength);
        var date = new CyclicIndexedStateDefinition(
            CommonYearMonthLengthRecipe.DateStateKey,
            CommonYearMonthLengthRecipe.DateCapacity);

        var mod400Equal = Modulo(Mod400EqualGuardKey, year, 400, IndexedStateModuloComparisonKind.Equal);
        var mod400NotEqual = Modulo(
            Mod400NotEqualGuardKey, year, 400, IndexedStateModuloComparisonKind.NotEqual);
        var mod100Equal = Modulo(Mod100EqualGuardKey, year, 100, IndexedStateModuloComparisonKind.Equal);
        var mod100NotEqual = Modulo(
            Mod100NotEqualGuardKey, year, 100, IndexedStateModuloComparisonKind.NotEqual);
        var mod4Equal = Modulo(Mod4EqualGuardKey, year, 4, IndexedStateModuloComparisonKind.Equal);
        var mod4NotEqual = Modulo(
            Mod4NotEqualGuardKey, year, 4, IndexedStateModuloComparisonKind.NotEqual);
        var centuryCommon = new IndexedStateAllOfGuard(
            CenturyCommonGuardKey,
            new BoundedIndexedStateGuardDefinition[] { mod100Equal, mod400NotEqual });
        var ordinaryLeap = new IndexedStateAllOfGuard(
            OrdinaryLeapGuardKey,
            new BoundedIndexedStateGuardDefinition[] { mod4Equal, mod100NotEqual });
        var leapCases = new[]
        {
            new ComputedIndexedValueCase(DivisibleBy400LeapCaseKey, mod400Equal, LeapClassIndex),
            new ComputedIndexedValueCase(CenturyCommonCaseKey, centuryCommon, CommonClassIndex),
            new ComputedIndexedValueCase(OrdinaryLeapCaseKey, ordinaryLeap, LeapClassIndex),
            new ComputedIndexedValueCase(OrdinaryCommonCaseKey, mod4NotEqual, CommonClassIndex),
        };
        var leapClass = new ComputedIndexedValueDefinition(
            LeapClassKey,
            LeapClassCardinality,
            leapCases,
            GuardedStateTransitionMatchPolicy.ExactlyOne);
        var selectors = new[]
        {
            new ComputedIndexedSelectorDefinition(
                ComputedIndexedSelectorKind.ComputedIndexedValue,
                leapClass.ComputedIndexedValueDefinitionId,
                LeapClassCardinality),
            new ComputedIndexedSelectorDefinition(
                ComputedIndexedSelectorKind.State,
                month.CyclicStateDefinitionId,
                CommonYearMonthLengthRecipe.MonthCycleLength),
        };
        var entries = Enumerable.Range(0, LeapClassCardinality)
            .SelectMany(leapIndex => Enumerable.Range(0, CommonYearMonthLengthRecipe.MonthCycleLength)
                .Select(monthIndex => new ComputedSelectorIndexedLookupEntry(
                    new[]
                    {
                        new ComputedIndexedLookupCoordinate(
                            ComputedIndexedSelectorKind.ComputedIndexedValue,
                            leapClass.ComputedIndexedValueDefinitionId,
                            leapIndex),
                        new ComputedIndexedLookupCoordinate(
                            ComputedIndexedSelectorKind.State,
                            month.CyclicStateDefinitionId,
                            monthIndex),
                    },
                    GetMonthLength(leapIndex, monthIndex))))
            .ToArray();
        var lookup = new ComputedSelectorIndexedLookupTableDefinition(LookupKey, selectors, entries);
        var constraint = new ComputedSelectorIndexedLookupBoundConstraint(
            ConstraintKey,
            date.CyclicStateDefinitionId,
            lookup.ComputedSelectorIndexedLookupTableId,
            selectors,
            IndexedStateLookupComparisonKind.LessThan);
        var normalDay = new ComputedSelectorIndexedLookupComparisonGuard(
            NormalDayGuardKey,
            date.CyclicStateDefinitionId,
            BigInteger.One,
            IndexedStateLookupComparisonKind.LessThan,
            lookup.ComputedSelectorIndexedLookupTableId,
            selectors);
        var dateAtEnd = new ComputedSelectorIndexedLookupComparisonGuard(
            DateAtEndGuardKey,
            date.CyclicStateDefinitionId,
            BigInteger.One,
            IndexedStateLookupComparisonKind.Equal,
            lookup.ComputedSelectorIndexedLookupTableId,
            selectors);
        var monthBeforeDecember = new IndexedStateConstantComparisonGuard(
            MonthBeforeDecemberGuardKey,
            month.CyclicStateDefinitionId,
            BigInteger.Zero,
            IndexedStateLookupComparisonKind.LessThan,
            11);
        var monthIsDecember = new IndexedStateConstantComparisonGuard(
            MonthIsDecemberGuardKey,
            month.CyclicStateDefinitionId,
            BigInteger.Zero,
            IndexedStateLookupComparisonKind.Equal,
            11);
        var monthEnd = new IndexedStateAllOfGuard(
            MonthEndGuardKey,
            new BoundedIndexedStateGuardDefinition[] { dateAtEnd, monthBeforeDecember });
        var yearEnd = new IndexedStateAllOfGuard(
            YearEndGuardKey,
            new BoundedIndexedStateGuardDefinition[] { dateAtEnd, monthIsDecember });

        var dateIncrement = new GuardedStateEffectDefinition(
            CommonYearMonthLengthRecipe.DateIncrementEffectKey,
            date.CyclicStateDefinitionId,
            GuardedStateEffectKind.AddModulo,
            BigInteger.One);
        var dateReset = new GuardedStateEffectDefinition(
            CommonYearMonthLengthRecipe.DateResetEffectKey,
            date.CyclicStateDefinitionId,
            GuardedStateEffectKind.SetIndex,
            BigInteger.Zero);
        var monthIncrement = new GuardedStateEffectDefinition(
            CommonYearMonthLengthRecipe.MonthIncrementEffectKey,
            month.CyclicStateDefinitionId,
            GuardedStateEffectKind.AddModulo,
            BigInteger.One);
        var yearIncrement = new GuardedStateEffectDefinition(
            YearIncrementEffectKey,
            year.CyclicStateDefinitionId,
            GuardedStateEffectKind.AddModulo,
            BigInteger.One);
        var normalBranch = new ComputedGuardedStateTransitionBranch(
            NormalDayBranchKey, normalDay, new[] { dateIncrement });
        var monthEndBranch = new ComputedGuardedStateTransitionBranch(
            MonthEndBranchKey, monthEnd, new[] { dateReset, monthIncrement });
        var yearEndBranch = new ComputedGuardedStateTransitionBranch(
            YearEndBranchKey, yearEnd, new[] { dateReset, monthIncrement, yearIncrement });
        var choice = new ComputedGuardedStateTransitionChoice(
            DayBoundaryChoiceKey,
            dayBoundaryPeriodicEventDefinitionId,
            new[] { normalBranch, monthEndBranch, yearEndBranch },
            GuardedStateTransitionMatchPolicy.ExactlyOne);
        var moduloGuards = new[]
        {
            mod400Equal, mod400NotEqual, mod100Equal, mod100NotEqual, mod4Equal, mod4NotEqual,
        };
        var classificationConjunctions = new[] { centuryCommon, ordinaryLeap };
        var guards = moduloGuards.Cast<BoundedIndexedStateGuardDefinition>()
            .Concat(classificationConjunctions)
            .Concat(new BoundedIndexedStateGuardDefinition[]
            {
                normalDay, dateAtEnd, monthBeforeDecember, monthIsDecember, monthEnd, yearEnd,
            })
            .ToArray();
        var plan = new ComputedGuardedIndexedStateTransitionPlan(
            new[] { weekday.StateDefinition, year, month, date },
            guards,
            new[] { leapClass },
            new[] { lookup },
            new[] { constraint },
            new[] { weekday.TransitionDefinition },
            new[] { dateIncrement, dateReset, monthIncrement, yearIncrement },
            new[] { choice });

        return new WeekdayGregorian400CalendarTransitionRecipe(
            weekday,
            year,
            month,
            date,
            moduloGuards,
            classificationConjunctions,
            leapClass,
            lookup,
            constraint,
            normalDay,
            dateAtEnd,
            monthBeforeDecember,
            monthIsDecember,
            monthEnd,
            yearEnd,
            dateIncrement,
            dateReset,
            monthIncrement,
            yearIncrement,
            normalBranch,
            monthEndBranch,
            yearEndBranch,
            choice,
            plan,
            monthLabels);
    }

    public static BigInteger GetMonthLength(BigInteger leapClassIndex, BigInteger monthIndex)
    {
        if (leapClassIndex < BigInteger.Zero || leapClassIndex >= LeapClassCardinality)
        {
            throw new ArgumentOutOfRangeException(nameof(leapClassIndex));
        }

        if (monthIndex < BigInteger.Zero || monthIndex >= CommonYearMonthLengthRecipe.MonthCycleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(monthIndex));
        }

        return monthIndex == 1 && leapClassIndex == LeapClassIndex
            ? 29
            : CommonMonthLengths[(int)monthIndex];
    }

    private static IndexedStateModuloComparisonGuard Modulo(
        string key,
        CyclicIndexedStateDefinition year,
        BigInteger modulus,
        IndexedStateModuloComparisonKind comparison) =>
        new(key, year.CyclicStateDefinitionId, modulus, comparison, BigInteger.Zero);

    private static string[] ValidateLabels(IEnumerable<string> labels, int count, string parameterName)
    {
        var values = labels.ToArray();
        if (values.Length != count || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"Display metadata requires exactly {count} non-empty labels.", parameterName);
        }

        return values;
    }
}
