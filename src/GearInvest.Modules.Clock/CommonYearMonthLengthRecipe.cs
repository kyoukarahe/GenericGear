using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Modules.Clock;

public sealed class WeekdayCommonYearCalendarTransitionRecipe
{
    internal WeekdayCommonYearCalendarTransitionRecipe(
        Weekday7TransitionRecipe weekday7,
        CyclicIndexedStateDefinition monthStateDefinition,
        CyclicIndexedStateDefinition dateStateDefinition,
        IndexedStateLookupTableDefinition monthLengthLookup,
        IndexedStateLookupBoundConstraint dateConstraint,
        IndexedStateLookupComparisonGuard normalDayGuard,
        IndexedStateLookupComparisonGuard monthEndGuard,
        GuardedStateEffectDefinition dateIncrementEffect,
        GuardedStateEffectDefinition dateResetEffect,
        GuardedStateEffectDefinition monthIncrementEffect,
        GuardedStateTransitionBranch normalDayBranch,
        GuardedStateTransitionBranch monthEndBranch,
        GuardedStateTransitionChoice dayBoundaryChoice,
        GuardedIndexedStateTransitionPlan transitionPlan,
        IEnumerable<string> monthLabels)
    {
        Weekday7 = weekday7 ?? throw new ArgumentNullException(nameof(weekday7));
        MonthStateDefinition = monthStateDefinition ?? throw new ArgumentNullException(nameof(monthStateDefinition));
        DateStateDefinition = dateStateDefinition ?? throw new ArgumentNullException(nameof(dateStateDefinition));
        MonthLengthLookup = monthLengthLookup ?? throw new ArgumentNullException(nameof(monthLengthLookup));
        DateConstraint = dateConstraint ?? throw new ArgumentNullException(nameof(dateConstraint));
        NormalDayGuard = normalDayGuard ?? throw new ArgumentNullException(nameof(normalDayGuard));
        MonthEndGuard = monthEndGuard ?? throw new ArgumentNullException(nameof(monthEndGuard));
        DateIncrementEffect = dateIncrementEffect ?? throw new ArgumentNullException(nameof(dateIncrementEffect));
        DateResetEffect = dateResetEffect ?? throw new ArgumentNullException(nameof(dateResetEffect));
        MonthIncrementEffect = monthIncrementEffect ?? throw new ArgumentNullException(nameof(monthIncrementEffect));
        NormalDayBranch = normalDayBranch ?? throw new ArgumentNullException(nameof(normalDayBranch));
        MonthEndBranch = monthEndBranch ?? throw new ArgumentNullException(nameof(monthEndBranch));
        DayBoundaryChoice = dayBoundaryChoice ?? throw new ArgumentNullException(nameof(dayBoundaryChoice));
        TransitionPlan = transitionPlan ?? throw new ArgumentNullException(nameof(transitionPlan));
        MonthLabels = (monthLabels ?? throw new ArgumentNullException(nameof(monthLabels))).ToList().AsReadOnly();
    }

    public Weekday7TransitionRecipe Weekday7 { get; }
    public CyclicIndexedStateDefinition MonthStateDefinition { get; }
    public CyclicIndexedStateDefinition DateStateDefinition { get; }
    public IndexedStateLookupTableDefinition MonthLengthLookup { get; }
    public IndexedStateLookupBoundConstraint DateConstraint { get; }
    public IndexedStateLookupComparisonGuard NormalDayGuard { get; }
    public IndexedStateLookupComparisonGuard MonthEndGuard { get; }
    public GuardedStateEffectDefinition DateIncrementEffect { get; }
    public GuardedStateEffectDefinition DateResetEffect { get; }
    public GuardedStateEffectDefinition MonthIncrementEffect { get; }
    public GuardedStateTransitionBranch NormalDayBranch { get; }
    public GuardedStateTransitionBranch MonthEndBranch { get; }
    public GuardedStateTransitionChoice DayBoundaryChoice { get; }
    public GuardedIndexedStateTransitionPlan TransitionPlan { get; }
    public ReadOnlyCollection<string> MonthLabels { get; }

    public string GetMonthLabel(BigInteger index)
    {
        if (index < BigInteger.Zero || index >= MonthLabels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return MonthLabels[(int)index];
    }

    public BigInteger GetDisplayDay(BigInteger dateIndex)
    {
        if (dateIndex < BigInteger.Zero || dateIndex >= CommonYearMonthLengthRecipe.DateCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(dateIndex));
        }

        return dateIndex + BigInteger.One;
    }
}

public static class CommonYearMonthLengthRecipe
{
    public const string MonthStateKey = "state:month12";
    public const string DateStateKey = "state:date-in-month31";
    public const string LookupKey = "lookup:common-year-month-length";
    public const string ConstraintKey = "constraint:date-in-selected-month";
    public const string NormalDayGuardKey = "guard:common-year-normal-day";
    public const string MonthEndGuardKey = "guard:common-year-month-end";
    public const string DateIncrementEffectKey = "effect:date-in-month-add-one";
    public const string DateResetEffectKey = "effect:date-in-month-set-zero";
    public const string MonthIncrementEffectKey = "effect:month12-add-one";
    public const string NormalDayBranchKey = "branch:common-year-normal-day";
    public const string MonthEndBranchKey = "branch:common-year-month-end";
    public const string DayBoundaryChoiceKey = "choice:common-year-day-boundary";
    public const int MonthCycleLength = 12;
    public const int DateCapacity = 31;

    private static readonly BigInteger[] MonthLengths =
    {
        31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31,
    };

    private static readonly string[] DefaultMonthLabels =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    public static WeekdayCommonYearCalendarTransitionRecipe Create(
        string dayBoundaryPeriodicEventDefinitionId,
        IEnumerable<string>? weekdayDisplayLabels = null,
        IEnumerable<string>? monthDisplayLabels = null)
    {
        var labels = (monthDisplayLabels ?? DefaultMonthLabels).ToArray();
        if (labels.Length != MonthCycleLength || labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Common-year month display metadata requires exactly twelve non-empty labels.",
                nameof(monthDisplayLabels));
        }

        var weekday = Weekday7Recipe.Create(dayBoundaryPeriodicEventDefinitionId, weekdayDisplayLabels);
        var month = new CyclicIndexedStateDefinition(MonthStateKey, MonthCycleLength);
        var date = new CyclicIndexedStateDefinition(DateStateKey, DateCapacity);
        var lookup = new IndexedStateLookupTableDefinition(
            LookupKey,
            month.CyclicStateDefinitionId,
            MonthLengths);
        var constraint = new IndexedStateLookupBoundConstraint(
            ConstraintKey,
            date.CyclicStateDefinitionId,
            month.CyclicStateDefinitionId,
            lookup.IndexedStateLookupTableId,
            IndexedStateLookupComparisonKind.LessThan);
        var normalGuard = new IndexedStateLookupComparisonGuard(
            NormalDayGuardKey,
            date.CyclicStateDefinitionId,
            BigInteger.One,
            IndexedStateLookupComparisonKind.LessThan,
            month.CyclicStateDefinitionId,
            lookup.IndexedStateLookupTableId);
        var monthEndGuard = new IndexedStateLookupComparisonGuard(
            MonthEndGuardKey,
            date.CyclicStateDefinitionId,
            BigInteger.One,
            IndexedStateLookupComparisonKind.Equal,
            month.CyclicStateDefinitionId,
            lookup.IndexedStateLookupTableId);
        var dateIncrement = new GuardedStateEffectDefinition(
            DateIncrementEffectKey,
            date.CyclicStateDefinitionId,
            GuardedStateEffectKind.AddModulo,
            BigInteger.One);
        var dateReset = new GuardedStateEffectDefinition(
            DateResetEffectKey,
            date.CyclicStateDefinitionId,
            GuardedStateEffectKind.SetIndex,
            BigInteger.Zero);
        var monthIncrement = new GuardedStateEffectDefinition(
            MonthIncrementEffectKey,
            month.CyclicStateDefinitionId,
            GuardedStateEffectKind.AddModulo,
            BigInteger.One);
        var normalBranch = new GuardedStateTransitionBranch(
            NormalDayBranchKey,
            normalGuard,
            new[] { dateIncrement });
        var monthEndBranch = new GuardedStateTransitionBranch(
            MonthEndBranchKey,
            monthEndGuard,
            new[] { dateReset, monthIncrement });
        var choice = new GuardedStateTransitionChoice(
            DayBoundaryChoiceKey,
            dayBoundaryPeriodicEventDefinitionId,
            new[] { normalBranch, monthEndBranch },
            GuardedStateTransitionMatchPolicy.ExactlyOne);
        var plan = new GuardedIndexedStateTransitionPlan(
            new[] { weekday.StateDefinition, month, date },
            new[] { lookup },
            new[] { constraint },
            new[] { weekday.TransitionDefinition },
            new[] { choice });
        return new WeekdayCommonYearCalendarTransitionRecipe(
            weekday,
            month,
            date,
            lookup,
            constraint,
            normalGuard,
            monthEndGuard,
            dateIncrement,
            dateReset,
            monthIncrement,
            normalBranch,
            monthEndBranch,
            choice,
            plan,
            labels);
    }
}
