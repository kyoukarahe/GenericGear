using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Modules.Clock;

public sealed class Weekday7TransitionRecipe
{
    internal Weekday7TransitionRecipe(
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

    public string GetDisplayLabel(BigInteger index)
    {
        if (index < BigInteger.Zero || index >= Labels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Labels[(int)index];
    }
}

public static class Weekday7Recipe
{
    public const string StateKey = "state:weekday7";
    public const string TransitionKey = "transition:day-boundary-to-weekday7";
    public const int CycleLength = 7;

    private static readonly string[] DefaultLabels =
    {
        "Monday",
        "Tuesday",
        "Wednesday",
        "Thursday",
        "Friday",
        "Saturday",
        "Sunday",
    };

    public static Weekday7TransitionRecipe Create(
        string dayBoundaryPeriodicEventDefinitionId,
        IEnumerable<string>? displayLabels = null)
    {
        var labels = (displayLabels ?? DefaultLabels).ToArray();
        if (labels.Length != CycleLength || labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Weekday7 display metadata requires exactly seven non-empty labels.", nameof(displayLabels));
        }

        var state = new CyclicIndexedStateDefinition(StateKey, new BigInteger(CycleLength));
        var transition = new IndexedStateTransitionDefinition(
            TransitionKey,
            dayBoundaryPeriodicEventDefinitionId,
            state.CyclicStateDefinitionId,
            BigInteger.One);
        return new Weekday7TransitionRecipe(
            state,
            transition,
            new IndexedStateTransitionPlan(new[] { state }, new[] { transition }),
            labels);
    }
}
