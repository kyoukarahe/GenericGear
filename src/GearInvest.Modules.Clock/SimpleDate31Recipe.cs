using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Modules.Clock;

public sealed class SimpleDate31TransitionRecipe
{
    internal SimpleDate31TransitionRecipe(
        CyclicIndexedStateDefinition stateDefinition,
        IndexedStateTransitionDefinition transitionDefinition,
        IndexedStateTransitionPlan transitionPlan,
        IEnumerable<string> labels)
    {
        StateDefinition = stateDefinition ?? throw new ArgumentNullException(nameof(stateDefinition));
        TransitionDefinition = transitionDefinition ?? throw new ArgumentNullException(nameof(transitionDefinition));
        TransitionPlan = transitionPlan ?? throw new ArgumentNullException(nameof(transitionPlan));
        Labels = (labels ?? throw new ArgumentNullException(nameof(labels))).ToList().AsReadOnly();
    }

    public CyclicIndexedStateDefinition StateDefinition { get; }
    public IndexedStateTransitionDefinition TransitionDefinition { get; }
    public IndexedStateTransitionPlan TransitionPlan { get; }
    public ReadOnlyCollection<string> Labels { get; }

    public BigInteger GetDisplayDay(BigInteger index)
    {
        ValidateIndex(index);
        return index + BigInteger.One;
    }

    public string GetDisplayLabel(BigInteger index)
    {
        ValidateIndex(index);
        return Labels[(int)index];
    }

    private void ValidateIndex(BigInteger index)
    {
        if (index < BigInteger.Zero || index >= Labels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

public static class SimpleDate31Recipe
{
    public const string StateKey = "state:simple-date31";
    public const string TransitionKey = "transition:day-boundary-to-simple-date31";
    public const int CycleLength = 31;

    private static readonly string[] DefaultLabels = Enumerable
        .Range(1, CycleLength)
        .Select(value => value.ToString(CultureInfo.InvariantCulture))
        .ToArray();

    public static SimpleDate31TransitionRecipe Create(
        string dayBoundaryPeriodicEventDefinitionId,
        IEnumerable<string>? displayLabels = null)
    {
        var labels = (displayLabels ?? DefaultLabels).ToArray();
        if (labels.Length != CycleLength || labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "SimpleDate31 display metadata requires exactly 31 non-empty labels.",
                nameof(displayLabels));
        }

        var state = new CyclicIndexedStateDefinition(StateKey, new BigInteger(CycleLength));
        var transition = new IndexedStateTransitionDefinition(
            TransitionKey,
            dayBoundaryPeriodicEventDefinitionId,
            state.CyclicStateDefinitionId,
            BigInteger.One);
        return new SimpleDate31TransitionRecipe(
            state,
            transition,
            new IndexedStateTransitionPlan(new[] { state }, new[] { transition }),
            labels);
    }
}

public sealed class Weekday7SimpleDate31TransitionRecipe
{
    internal Weekday7SimpleDate31TransitionRecipe(
        Weekday7TransitionRecipe weekday7,
        SimpleDate31TransitionRecipe simpleDate31)
    {
        Weekday7 = weekday7 ?? throw new ArgumentNullException(nameof(weekday7));
        SimpleDate31 = simpleDate31 ?? throw new ArgumentNullException(nameof(simpleDate31));
        TransitionPlan = new IndexedStateTransitionPlan(
            new[] { Weekday7.StateDefinition, SimpleDate31.StateDefinition },
            new[] { Weekday7.TransitionDefinition, SimpleDate31.TransitionDefinition });
    }

    public Weekday7TransitionRecipe Weekday7 { get; }
    public SimpleDate31TransitionRecipe SimpleDate31 { get; }
    public IndexedStateTransitionPlan TransitionPlan { get; }
}

public static class Weekday7SimpleDate31Recipe
{
    public static Weekday7SimpleDate31TransitionRecipe Create(
        string dayBoundaryPeriodicEventDefinitionId,
        IEnumerable<string>? weekdayDisplayLabels = null,
        IEnumerable<string>? dateDisplayLabels = null) =>
        new(
            Weekday7Recipe.Create(dayBoundaryPeriodicEventDefinitionId, weekdayDisplayLabels),
            SimpleDate31Recipe.Create(dayBoundaryPeriodicEventDefinitionId, dateDisplayLabels));
}
