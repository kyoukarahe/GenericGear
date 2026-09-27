using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Bounded lowering of a two-state lookup comparison into an ideal indexed pair.
/// Names below are mechanical roles in this fixed topology, never calendar labels.</summary>
public sealed class DiscreteEmbodimentCompiler
{
    public static DiscreteConnection[] Connections() => new[] {
        Link("primary", "position", "position-track", "position", DiscretePortKind.WheelPosition),
        Link("secondary", "position", "limit-track", "position", DiscretePortKind.WheelPosition),
        Link("position-track", "height", "position-probe", "height", DiscretePortKind.DisplacementTicks),
        Link("limit-track", "height", "limit-probe", "height", DiscretePortKind.DisplacementTicks),
        Link("position-probe", "displacement", "selector", "position", DiscretePortKind.DisplacementTicks),
        Link("limit-probe", "displacement", "selector", "limit", DiscretePortKind.DisplacementTicks),
        Link("selector", "selection", "latch", "selection", DiscretePortKind.Selection),
        Link("drive", "stroke", "latch", "drive", DiscretePortKind.DriveStroke),
        Link("latch", "selection", "primary-indexer", "selection", DiscretePortKind.Selection),
        Link("latch", "selection", "secondary-indexer", "selection", DiscretePortKind.Selection),
        Link("latch", "stroke", "primary-indexer", "drive", DiscretePortKind.DriveStroke),
        Link("latch", "stroke", "secondary-indexer", "drive", DiscretePortKind.DriveStroke),
        Link("primary-indexer", "position", "primary", "index", DiscretePortKind.WheelPosition),
        Link("secondary-indexer", "position", "secondary", "index", DiscretePortKind.WheelPosition),
        Link("primary", "position", "stop", "position", DiscretePortKind.WheelPosition),
        Link("primary", "position", "primary-lock", "position", DiscretePortKind.WheelPosition),
        Link("secondary", "position", "secondary-lock", "position", DiscretePortKind.WheelPosition),
        Link("primary-lock", "settled", "stop", "settled", DiscretePortKind.DetentState),
        Link("stop", "home-settled", "primary-indexer", "inhibit", DiscretePortKind.ReferenceSettled),
        Link("stop", "home-settled", "secondary-indexer", "enable", DiscretePortKind.ReferenceSettled),
        Link("primary-lock", "settled", "sequence", "primary-settled", DiscretePortKind.DetentState),
        Link("secondary-lock", "settled", "sequence", "secondary-settled", DiscretePortKind.DetentState),
    };
    private static DiscreteConnection Link(string f, string o, string t, string i, DiscretePortKind k) => new DiscreteConnection(f, o, t, i, k);

    public DiscreteCompilationResult Compile(GuardedIndexedStateTransitionPlan plan, DiscreteProjectionRequest request)
    {
        try
        {
            Need(request.Profile == DiscreteEmbodimentContract.Profile, "unsupported-profile");
            var allStates = plan.OrderedStateDefinitions.Select(s => s.CyclicStateDefinitionId).ToArray();
            var allEffects = plan.OrderedEffects.Select(e => e.GuardedStateEffectId)
                .Concat(plan.OrderedUnconditionalTransitions.Select(t => t.IndexedStateTransitionDefinitionId)).ToArray();
            Partition(allStates, request.RequiredStates, request.OmittedStates, "state-coverage");
            Partition(allEffects, request.RequiredEffects, request.OmittedEffects, "effect-coverage");
            Need(plan.OrderedGuardedChoices.Count == 1 && plan.OrderedLookupTables.Count == 1 &&
                plan.OrderedConstraints.Count == 1 && request.RequiredStates.Count == 2, "unsupported-source-shape");
            var choice = plan.OrderedGuardedChoices[0];
            Need(choice.SourcePeriodicEventDefinitionId == request.EventDefinitionId &&
                choice.MatchPolicy == GuardedStateTransitionMatchPolicy.ExactlyOne && choice.OrderedBranches.Count == 2, "event-or-choice-contract");
            var normal = choice.OrderedBranches.Single(b => b.Guard.ComparisonKind == IndexedStateLookupComparisonKind.LessThan);
            var end = choice.OrderedBranches.Single(b => b.Guard.ComparisonKind == IndexedStateLookupComparisonKind.Equal);
            var g = normal.Guard;
            var lookup = plan.OrderedLookupTables[0];
            Need(lookup.IndexedStateLookupTableId == g.LookupTableId && lookup.SelectorStateDefinitionId == g.SelectorStateDefinitionId &&
                lookup.SemanticProfile == GuardedIndexedStateContract.LookupSemanticProfile, "lookup-dependency");
            Need(end.Guard.SubjectStateDefinitionId == g.SubjectStateDefinitionId && end.Guard.SelectorStateDefinitionId == g.SelectorStateDefinitionId &&
                end.Guard.LookupTableId == g.LookupTableId && g.SignedSubjectOffset == 1 && end.Guard.SignedSubjectOffset == 1, "unsupported-sensing");
            Need(request.RequiredStates.Contains(g.SubjectStateDefinitionId) && request.RequiredStates.Contains(g.SelectorStateDefinitionId) &&
                g.SubjectStateDefinitionId != g.SelectorStateDefinitionId, "unembodied-read-dependency");
            var primary = plan.OrderedStateDefinitions.Single(s => s.CyclicStateDefinitionId == g.SubjectStateDefinitionId);
            var secondary = plan.OrderedStateDefinitions.Single(s => s.CyclicStateDefinitionId == g.SelectorStateDefinitionId);
            Need(primary.CycleLength >= 2 && primary.CycleLength <= 512 && secondary.CycleLength >= 2 && secondary.CycleLength <= 512, "wheel-capacity");
            var constraint = plan.OrderedConstraints[0];
            Need(constraint.SubjectStateDefinitionId == g.SubjectStateDefinitionId && constraint.SelectorStateDefinitionId == g.SelectorStateDefinitionId &&
                constraint.LookupTableId == g.LookupTableId && constraint.ComparisonKind == IndexedStateLookupComparisonKind.LessThan, "unsupported-admission-constraint");
            Need(normal.OrderedEffects.Count == 1 && end.OrderedEffects.Count == 2, "unsupported-effects");
            var increment = normal.OrderedEffects[0];
            var reset = end.OrderedEffects.Single(e => e.TargetStateDefinitionId == g.SubjectStateDefinitionId);
            var advance = end.OrderedEffects.Single(e => e.TargetStateDefinitionId == g.SelectorStateDefinitionId);
            Need(increment.TargetStateDefinitionId == g.SubjectStateDefinitionId && increment.EffectKind == GuardedStateEffectKind.AddModulo && increment.Operand == 1 &&
                reset.EffectKind == GuardedStateEffectKind.SetIndex && reset.Operand == 0 && advance.EffectKind == GuardedStateEffectKind.AddModulo && advance.Operand == 1,
                "unsupported-non-home-or-non-unit-effect");
            Need(request.RequiredEffects.SequenceEqual(new[] { increment.GuardedStateEffectId, reset.GuardedStateEffectId, advance.GuardedStateEffectId }.OrderBy(x => x, StringComparer.Ordinal)), "required-effect-coverage");
            Need(plan.OrderedUnconditionalTransitions.All(t => request.OmittedStates.Contains(t.TargetCyclicStateDefinitionId) && request.OmittedEffects.Contains(t.IndexedStateTransitionDefinitionId)), "omitted-write-dependency");
            var components = CreateComponents((int)primary.CycleLength, (int)secondary.CycleLength, lookup.Values, request.PrimaryPulseCapacity);
            var bindings = new[] {
                new DiscreteSourceBinding("state",g.SubjectStateDefinitionId,"primary"), new DiscreteSourceBinding("state",g.SelectorStateDefinitionId,"secondary"),
                new DiscreteSourceBinding("effect",increment.GuardedStateEffectId,"primary-indexer"),new DiscreteSourceBinding("effect",reset.GuardedStateEffectId,"stop"),
                new DiscreteSourceBinding("effect",advance.GuardedStateEffectId,"secondary-indexer"), new DiscreteSourceBinding("lookup",g.LookupTableId,"limit-track"),
                new DiscreteSourceBinding("guard",g.IndexedStateGuardId,"selector"),new DiscreteSourceBinding("guard",end.Guard.IndexedStateGuardId,"selector"),
                new DiscreteSourceBinding("constraint",constraint.IndexedStateConstraintId,"position-probe"),
                new DiscreteSourceBinding("event",request.EventDefinitionId,"drive") };
            var model = new DiscreteEmbodimentModel(components, Connections(), new DiscreteMotionProfile(), plan.GuardedIndexedTransitionPlanId, request, bindings);
            var validation = Validate(model);
            return new DiscreteCompilationResult(validation.IsValid ? DiscreteStatus.Complete : DiscreteStatus.Unsupported, request,
                validation.IsValid ? model : null, validation);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
        { return new DiscreteCompilationResult(DiscreteStatus.Unsupported, request, null, new DiscreteValidation(new[] { ex.Message })); }
    }

    public static DiscreteComponent[] CreateComponents(int primary, int secondary, IEnumerable<BigInteger> limits, int capacity) => new[] {
        new DiscreteComponent("primary",DiscreteComponentKind.IndexedWheel,detents:primary),
        new DiscreteComponent("secondary",DiscreteComponentKind.IndexedWheel,detents:secondary),
        new DiscreteComponent("position-track",DiscreteComponentKind.IndexedProgramTrack,sectors:Enumerable.Range(1,primary).Select(x=>(BigInteger)x)),
        new DiscreteComponent("limit-track",DiscreteComponentKind.IndexedProgramTrack,sectors:limits),
        new DiscreteComponent("position-probe",DiscreteComponentKind.FollowerProbe),new DiscreteComponent("limit-probe",DiscreteComponentKind.FollowerProbe),
        new DiscreteComponent("selector",DiscreteComponentKind.DifferentialSelector),new DiscreteComponent("latch",DiscreteComponentKind.SelectionLatch),
        new DiscreteComponent("primary-indexer",DiscreteComponentKind.PositiveIndexer,capacity:capacity),
        new DiscreteComponent("secondary-indexer",DiscreteComponentKind.PositiveIndexer,capacity:1),
        new DiscreteComponent("stop",DiscreteComponentKind.ReferenceStop),new DiscreteComponent("primary-lock",DiscreteComponentKind.DetentLock),
        new DiscreteComponent("secondary-lock",DiscreteComponentKind.DetentLock),new DiscreteComponent("drive",DiscreteComponentKind.ExternalDrive),
        new DiscreteComponent("sequence",DiscreteComponentKind.ActuationSequence) };

    public DiscreteValidation Validate(DiscreteEmbodimentModel model)
    {
        var errors = new List<string>();
        try
        {
            Need(model.Components.Count == 15 && model.Components.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == 15, "component-role-count-or-duplicate");
            var c = model.Components.ToDictionary(x => x.Id, StringComparer.Ordinal);
            Need(c.ContainsKey("primary") && c.ContainsKey("secondary") && c.ContainsKey("limit-track") && c.ContainsKey("primary-indexer"), "missing-role");
            int n = c["primary"].Detents, m = c["secondary"].Detents, cap = c["primary-indexer"].Capacity;
            Need(n >= 2 && n <= 512 && m >= 2 && m <= 512 && cap >= 1 && cap <= 512, "primitive-bound");
            var expected = CreateComponents(n, m, c["limit-track"].Sectors, cap);
            // This version admits a calibrated unit-tick profile only. Unexpected parameters cannot be ignored.
            Need(expected.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Canonical).SequenceEqual(model.Components.Select(x => x.Canonical)), "unsupported-primitive-parameters");
            Need(c["limit-track"].Sectors.Count == m && c["limit-track"].Sectors.All(v => v >= 1 && v <= n), "sector-domain");
            Need(c["limit-track"].Sectors.All(v => v == 1 || n - (int)v + 1 <= cap), "insufficient-reference-seek-capacity");
            Need(Connections().OrderBy(x => x.Canonical, StringComparer.Ordinal).Select(x => x.Canonical).SequenceEqual(model.Connections.Select(x => x.Canonical)), "typed-topology-missing-conflicting-drive-or-interlock");
            var b = model.Motion.Boundaries;
            Need(model.Motion.Id == DiscreteEmbodimentContract.Profile && b.Count == 9 && b[0] == Rational.Zero && b[8] == Rational.One &&
                Enumerable.Range(1, 8).All(i => b[i] > b[i - 1]), "motion-profile-boundaries");
            Need(model.Projection.Profile == model.Motion.Id && model.Projection.PrimaryPulseCapacity == cap, "projection-profile");
            Need(model.Projection.RequiredStates.Count == 2 && model.Projection.RequiredEffects.Count == 3 &&
                !model.Projection.RequiredStates.Intersect(model.Projection.OmittedStates).Any() && !model.Projection.RequiredEffects.Intersect(model.Projection.OmittedEffects).Any(), "projection-partition");
            Need(model.Bindings.All(x => c.ContainsKey(x.ComponentId) && !string.IsNullOrWhiteSpace(x.SourceId)) &&
                model.Bindings.Select(x => x.Kind + ":" + x.SourceId).Distinct(StringComparer.Ordinal).Count() == model.Bindings.Count, "duplicate-or-dangling-binding");
            Need(model.Bindings.Where(x => x.Kind == "state").Select(x => x.SourceId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(model.Projection.RequiredStates) &&
                model.Bindings.Where(x => x.Kind == "state").Select(x => x.ComponentId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "primary", "secondary" }), "state-binding-coverage");
            Need(model.Bindings.Where(x => x.Kind == "effect").Select(x => x.SourceId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(model.Projection.RequiredEffects), "effect-binding-coverage");
            Need(model.Bindings.Count(x => x.Kind == "event" && x.SourceId == model.Projection.EventDefinitionId && x.ComponentId == "drive") == 1, "external-drive-binding");
            Need(model.Bindings.Count == 10 && model.Bindings.Count(x => x.Kind == "lookup" && x.ComponentId == "limit-track") == 1 &&
                model.Bindings.Count(x => x.Kind == "guard" && x.ComponentId == "selector") == 2 && model.Bindings.Count(x => x.Kind == "constraint" && x.ComponentId == "position-probe") == 1, "read-binding-coverage");
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException) { errors.Add(ex.Message); }
        return Certificate(errors, "topology-and-ports");
    }

    internal static DiscreteValidation Certificate(IEnumerable<string> errors, string checkedLevel)
    {
        var e = errors.ToArray();
        return new DiscreteValidation(e, new[] {
            new DiscreteValidationLevel("source-coverage-binding",e.Length==0?"pass":"fail","explicit mapping/omission declarations; source lowering checked during compile"),
            new DiscreteValidationLevel("topology-and-ports",e.Length==0?"pass":"fail","fixed typed indexed-pair topology"),
            new DiscreteValidationLevel("indexed-motion",checkedLevel=="indexed-motion"?(e.Length==0?"pass":"fail"):"notPerformed","capacity checked at model validation; execution checked separately"),
            new DiscreteValidationLevel("logical-equivalence","notPerformed","requires independent source-plan reference execution"),
            new DiscreteValidationLevel("visual-projection","notPerformed","requires actual consumer observation"),
            new DiscreteValidationLevel("spatial","notPerformed","schematic display only"),
            new DiscreteValidationLevel("contact-dynamics-manufacturing","notPerformed","outside idealized profile") });
    }
    internal static void Need(bool condition, string diagnostic) { if (!condition) throw new InvalidOperationException(diagnostic); }
    private static void Partition(IEnumerable<string> all, IEnumerable<string> required, IEnumerable<string> omitted, string error)
    {
        var actual = required.Concat(omitted).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Need(actual.Distinct(StringComparer.Ordinal).Count() == actual.Length && actual.SequenceEqual(all.OrderBy(x => x, StringComparer.Ordinal)), error);
    }
}
