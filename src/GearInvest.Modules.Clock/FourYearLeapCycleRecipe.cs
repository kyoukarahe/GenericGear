using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Modules.Clock;

public sealed class WeekdayFourYearCalendarTransitionRecipe
{
    internal WeekdayFourYearCalendarTransitionRecipe(
        Weekday7TransitionRecipe weekday7,
        CyclicIndexedStateDefinition yearCycleStateDefinition,
        CyclicIndexedStateDefinition monthStateDefinition,
        CyclicIndexedStateDefinition dateStateDefinition,
        CompositeIndexedStateLookupTableDefinition monthLengthLookup,
        CompositeIndexedStateLookupBoundConstraint dateConstraint,
        CompositeIndexedStateLookupComparisonGuard normalDayGuard,
        CompositeIndexedStateLookupComparisonGuard dateAtEndGuard,
        IndexedStateConstantComparisonGuard monthBeforeDecemberGuard,
        IndexedStateConstantComparisonGuard monthIsDecemberGuard,
        IndexedStateAllOfGuard monthEndGuard,
        IndexedStateAllOfGuard yearEndGuard,
        GuardedStateEffectDefinition dateIncrementEffect,
        GuardedStateEffectDefinition dateResetEffect,
        GuardedStateEffectDefinition monthIncrementEffect,
        GuardedStateEffectDefinition yearIncrementEffect,
        CompositeGuardedStateTransitionBranch normalDayBranch,
        CompositeGuardedStateTransitionBranch monthEndBranch,
        CompositeGuardedStateTransitionBranch yearEndBranch,
        CompositeGuardedStateTransitionChoice dayBoundaryChoice,
        CompositeGuardedIndexedStateTransitionPlan transitionPlan,
        IEnumerable<string> yearCycleLabels,
        IEnumerable<string> monthLabels)
    {
        Weekday7 = weekday7;
        YearCycleStateDefinition = yearCycleStateDefinition;
        MonthStateDefinition = monthStateDefinition;
        DateStateDefinition = dateStateDefinition;
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
        YearCycleLabels = yearCycleLabels.ToList().AsReadOnly();
        MonthLabels = monthLabels.ToList().AsReadOnly();
    }

    public Weekday7TransitionRecipe Weekday7 { get; }
    public CyclicIndexedStateDefinition YearCycleStateDefinition { get; }
    public CyclicIndexedStateDefinition MonthStateDefinition { get; }
    public CyclicIndexedStateDefinition DateStateDefinition { get; }
    public CompositeIndexedStateLookupTableDefinition MonthLengthLookup { get; }
    public CompositeIndexedStateLookupBoundConstraint DateConstraint { get; }
    public CompositeIndexedStateLookupComparisonGuard NormalDayGuard { get; }
    public CompositeIndexedStateLookupComparisonGuard DateAtEndGuard { get; }
    public IndexedStateConstantComparisonGuard MonthBeforeDecemberGuard { get; }
    public IndexedStateConstantComparisonGuard MonthIsDecemberGuard { get; }
    public IndexedStateAllOfGuard MonthEndGuard { get; }
    public IndexedStateAllOfGuard YearEndGuard { get; }
    public GuardedStateEffectDefinition DateIncrementEffect { get; }
    public GuardedStateEffectDefinition DateResetEffect { get; }
    public GuardedStateEffectDefinition MonthIncrementEffect { get; }
    public GuardedStateEffectDefinition YearIncrementEffect { get; }
    public CompositeGuardedStateTransitionBranch NormalDayBranch { get; }
    public CompositeGuardedStateTransitionBranch MonthEndBranch { get; }
    public CompositeGuardedStateTransitionBranch YearEndBranch { get; }
    public CompositeGuardedStateTransitionChoice DayBoundaryChoice { get; }
    public CompositeGuardedIndexedStateTransitionPlan TransitionPlan { get; }
    public ReadOnlyCollection<string> YearCycleLabels { get; }
    public ReadOnlyCollection<string> MonthLabels { get; }

    public bool IsLeapYear(BigInteger index) => index == FourYearLeapCycleRecipe.LeapYearIndex;

    public BigInteger GetMonthLength(BigInteger yearCycleIndex, BigInteger monthIndex) =>
        FourYearLeapCycleRecipe.GetMonthLength(yearCycleIndex, monthIndex);
}

public static class FourYearLeapCycleRecipe
{
    public const string YearCycleStateKey = "state:year-cycle4";
    public const string LookupKey = "lookup:four-year-month-length";
    public const string ConstraintKey = "constraint:date-in-four-year-selected-month";
    public const string NormalDayGuardKey = "guard:four-year-normal-day";
    public const string DateAtEndGuardKey = "guard:four-year-date-at-end";
    public const string MonthBeforeDecemberGuardKey = "guard:month-before-december";
    public const string MonthIsDecemberGuardKey = "guard:month-is-december";
    public const string MonthEndGuardKey = "guard:four-year-month-end";
    public const string YearEndGuardKey = "guard:four-year-year-end";
    public const string YearIncrementEffectKey = "effect:year-cycle4-add-one";
    public const string NormalDayBranchKey = "branch:four-year-normal-day";
    public const string MonthEndBranchKey = "branch:four-year-month-end";
    public const string YearEndBranchKey = "branch:four-year-year-end";
    public const string DayBoundaryChoiceKey = "choice:four-year-day-boundary";
    public const int YearCycleLength = 4;
    public const int LeapYearIndex = 3;

    private static readonly BigInteger[] CommonMonthLengths =
    {
        31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31,
    };

    private static readonly string[] DefaultYearCycleLabels =
    {
        "Common A", "Common B", "Common C", "Leap",
    };

    private static readonly string[] DefaultMonthLabels =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    public static WeekdayFourYearCalendarTransitionRecipe Create(
        string dayBoundaryPeriodicEventDefinitionId,
        IEnumerable<string>? weekdayDisplayLabels = null,
        IEnumerable<string>? yearCycleDisplayLabels = null,
        IEnumerable<string>? monthDisplayLabels = null)
    {
        var yearLabels = ValidateLabels(
            yearCycleDisplayLabels ?? DefaultYearCycleLabels,
            YearCycleLength,
            nameof(yearCycleDisplayLabels));
        var monthLabels = ValidateLabels(
            monthDisplayLabels ?? DefaultMonthLabels,
            CommonYearMonthLengthRecipe.MonthCycleLength,
            nameof(monthDisplayLabels));
        var weekday = Weekday7Recipe.Create(dayBoundaryPeriodicEventDefinitionId, weekdayDisplayLabels);
        var year = new CyclicIndexedStateDefinition(YearCycleStateKey, YearCycleLength);
        var month = new CyclicIndexedStateDefinition(
            CommonYearMonthLengthRecipe.MonthStateKey,
            CommonYearMonthLengthRecipe.MonthCycleLength);
        var date = new CyclicIndexedStateDefinition(
            CommonYearMonthLengthRecipe.DateStateKey,
            CommonYearMonthLengthRecipe.DateCapacity);
        var selectors = new[]
        {
            new CompositeIndexedStateSelectorDefinition(year.CyclicStateDefinitionId, YearCycleLength),
            new CompositeIndexedStateSelectorDefinition(month.CyclicStateDefinitionId, CommonYearMonthLengthRecipe.MonthCycleLength),
        };
        var entries = Enumerable.Range(0, YearCycleLength)
            .SelectMany(yearIndex => Enumerable.Range(0, CommonYearMonthLengthRecipe.MonthCycleLength)
                .Select(monthIndex => new CompositeIndexedStateLookupEntry(
                    new[]
                    {
                        new CompositeIndexedStateLookupCoordinate(year.CyclicStateDefinitionId, yearIndex),
                        new CompositeIndexedStateLookupCoordinate(month.CyclicStateDefinitionId, monthIndex),
                    },
                    GetMonthLength(yearIndex, monthIndex))))
            .ToArray();
        var lookup = new CompositeIndexedStateLookupTableDefinition(LookupKey, selectors, entries);
        var selectorIds = selectors.Select(item => item.StateDefinitionId).ToArray();
        var constraint = new CompositeIndexedStateLookupBoundConstraint(
            ConstraintKey,
            date.CyclicStateDefinitionId,
            lookup.CompositeIndexedStateLookupTableId,
            selectorIds,
            IndexedStateLookupComparisonKind.LessThan);
        var normalDay = new CompositeIndexedStateLookupComparisonGuard(
            NormalDayGuardKey,
            date.CyclicStateDefinitionId,
            BigInteger.One,
            IndexedStateLookupComparisonKind.LessThan,
            lookup.CompositeIndexedStateLookupTableId,
            selectorIds);
        var dateAtEnd = new CompositeIndexedStateLookupComparisonGuard(
            DateAtEndGuardKey,
            date.CyclicStateDefinitionId,
            BigInteger.One,
            IndexedStateLookupComparisonKind.Equal,
            lookup.CompositeIndexedStateLookupTableId,
            selectorIds);
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
        var monthEnd = new IndexedStateAllOfGuard(MonthEndGuardKey, new BoundedIndexedStateGuardDefinition[]
        {
            dateAtEnd, monthBeforeDecember,
        });
        var yearEnd = new IndexedStateAllOfGuard(YearEndGuardKey, new BoundedIndexedStateGuardDefinition[]
        {
            dateAtEnd, monthIsDecember,
        });
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
        var normalBranch = new CompositeGuardedStateTransitionBranch(
            NormalDayBranchKey, normalDay, new[] { dateIncrement });
        var monthEndBranch = new CompositeGuardedStateTransitionBranch(
            MonthEndBranchKey, monthEnd, new[] { dateReset, monthIncrement });
        var yearEndBranch = new CompositeGuardedStateTransitionBranch(
            YearEndBranchKey, yearEnd, new[] { dateReset, monthIncrement, yearIncrement });
        var choice = new CompositeGuardedStateTransitionChoice(
            DayBoundaryChoiceKey,
            dayBoundaryPeriodicEventDefinitionId,
            new[] { normalBranch, monthEndBranch, yearEndBranch },
            GuardedStateTransitionMatchPolicy.ExactlyOne);
        var guards = new BoundedIndexedStateGuardDefinition[]
        {
            normalDay, dateAtEnd, monthBeforeDecember, monthIsDecember, monthEnd, yearEnd,
        };
        var effects = new[] { dateIncrement, dateReset, monthIncrement, yearIncrement };
        var plan = new CompositeGuardedIndexedStateTransitionPlan(
            new[] { weekday.StateDefinition, year, month, date },
            new[] { lookup },
            new[] { constraint },
            new[] { weekday.TransitionDefinition },
            guards,
            effects,
            new[] { choice });
        return new WeekdayFourYearCalendarTransitionRecipe(
            weekday,
            year,
            month,
            date,
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
            yearLabels,
            monthLabels);
    }

    public static BigInteger GetMonthLength(BigInteger yearCycleIndex, BigInteger monthIndex)
    {
        if (yearCycleIndex < BigInteger.Zero || yearCycleIndex >= YearCycleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(yearCycleIndex));
        }

        if (monthIndex < BigInteger.Zero || monthIndex >= CommonYearMonthLengthRecipe.MonthCycleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(monthIndex));
        }

        return monthIndex == 1 && yearCycleIndex == LeapYearIndex
            ? 29
            : CommonMonthLengths[(int)monthIndex];
    }

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
