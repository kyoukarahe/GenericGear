using GearInvest.Core;

namespace GearInvest.Modules.Clock;

/// <summary>Clock-owned projection policy. Weekday remains logical; it is explicitly not embodied.</summary>
public static class CommonYearDiscreteProjection
{
    public static DiscreteProjectionRequest Create(WeekdayCommonYearCalendarTransitionRecipe recipe, int pulseCapacity = 4) => new DiscreteProjectionRequest(
        new[] { recipe.DateStateDefinition.CyclicStateDefinitionId, recipe.MonthStateDefinition.CyclicStateDefinitionId },
        new[] { recipe.DateIncrementEffect.GuardedStateEffectId, recipe.DateResetEffect.GuardedStateEffectId, recipe.MonthIncrementEffect.GuardedStateEffectId },
        new[] { recipe.Weekday7.StateDefinition.CyclicStateDefinitionId }, new[] { recipe.Weekday7.TransitionDefinition.IndexedStateTransitionDefinitionId },
        recipe.DayBoundaryChoice.SourcePeriodicEventDefinitionId, pulseCapacity);
}
