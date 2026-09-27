using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class TransmissionGoalCompiler
{
    private static readonly TransmissionFamily[] Registry = { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound };
    internal static Diagnostic Error(string code, string field, string message) => new Diagnostic("TRANSMISSION_GOAL_" + code, DiagnosticSeverity.Error, message, field);

    public static TransmissionGoalNormalization Normalize(TransmissionGoal? raw)
    {
        var ds = new List<Diagnostic>(); var status = TransmissionGoalStatus.InvalidInput;
        void Bad(string field, string message) => ds.Add(Error("INVALID_INPUT", field, message));
        void Unsupported(string field, string message) { status = TransmissionGoalStatus.Unsupported; ds.Add(Error("UNSUPPORTED", field, message)); }
        if (raw == null) return new TransmissionGoalNormalization(null, status, new[] { Error("INVALID_INPUT", "goal", "A transmission goal is required.") });
        if (raw.Input == null || raw.Output == null || raw.Slots.Any(s => s == null) || raw.Legs.Any(l => l == null)) Bad("function/domains", "Anchors and all active inherited/overridden domains are required.");
        if (raw.IngestionLimitExceeded || raw.Slots.Any(s => s?.IngestionLimitExceeded == true) || raw.Legs.Any(l => l?.IngestionLimitExceeded == true)) Unsupported("ingestion", "Versioned bounded ingestion exceeded; no domain is silently truncated.");
        if (raw.Profile != TransmissionGoalContract.Profile || raw.Lowering != TransmissionGoalContract.Lowering || raw.Allocation != TransmissionGoalContract.Allocation ||
            raw.Ranking != TransmissionGoalContract.Ranking || raw.Resources != TransmissionGoalContract.Resources || raw.Unit != "tick") Unsupported("policies", "Unknown goal/lowering/allocation/ranking/resource/unit version.");
        if (raw.MaximumCompounds < 0 || raw.MaximumTotalIdlers < 0 || raw.MaximumLayerCount < 1 || raw.WorkBudget < 0 || raw.MaximumReturned < 1) Bad("limits", "Nonnegative work/compound/idler limits and positive layer/return limits required.");
        if (raw.MaximumCompounds > 2 || raw.MaximumTotalIdlers > 15 || raw.MaximumLayerCount > 3 || raw.WorkBudget > TransmissionGoalContract.MaxWork || raw.MaximumReturned > GearRoutingContract.MaxReturned)
            Unsupported("limits", "Only the bounded 0..2 compound, 0..15 total-idler, at most three-layer union is supported; work <= 1000000 and return <= 128.");
        if (!Enum.IsDefined(typeof(TransmissionOutputLayerPolicy), raw.LayerPolicy)) Bad("layerPolicy", "Unknown layer policy.");
        var available = raw.AvailableLayers.Distinct().OrderBy(x => x).ToArray(); var output = raw.OutputLayers.Distinct().OrderBy(x => x).ToArray();
        if (available.Length == 0 || output.Length == 0 || available.Concat(output).Any(x => x < 0 || x > 31) || raw.InputLayer < 0 || raw.InputLayer > 31) Bad("layers", "Nonempty finite nonnegative layer sets required.");
        if (raw.LayerPolicy == TransmissionOutputLayerPolicy.FixedOutputLayer && output.Length != 1) Bad("fixedOutputLayer", "FixedOutputLayer requires exactly one output layer.");
        if (output.Any(x => !available.Contains(x)) || !available.Contains(raw.InputLayer)) Bad("layers", "Input and every allowed/fixed output layer must belong to availableLayers.");
        if (available.Concat(output).Any(x => x > 2) || raw.InputLayer > 2) Unsupported("layers", "A requested layer lies outside canonical planes 0/1/2; partial supported coverage is not Complete.");
        var families = raw.AllowedFamilies.Distinct().OrderBy(x => x).ToArray();
        if (families.Length == 0 || families.Any(x => !Enum.IsDefined(typeof(TransmissionFamily), x))) Bad("allowedFamilies", "A nonempty subset of the three versioned families is required.");
        if (raw.KeepOuts.Any(k => k == null || (int)k.Layers < 1 || (int)k.Layers > 7)) Bad("keepOuts", "Keep-outs require a nonempty explicit subset of 0/1/2.");
        if (ds.Count != 0) return new TransmissionGoalNormalization(null, ds.Any(d => d.Code.EndsWith("INVALID_INPUT", StringComparison.Ordinal)) ? TransmissionGoalStatus.InvalidInput : status, ds);

        // Geometry normalization is reused, not route search. Preserve the source site domain; each child
        // subsequently applies its own existing endpoint-site rule (recorded in the lowering mapping).
        GearRoutingNormalization Plane(CompoundRoutingLeg l) => AnchoredGearRouter.NormalizeDomain(new AnchoredGearRoutingRequest(raw.Input!, raw.Output!,
            raw.TargetTransfer, raw.PitchRadiusTicksPerTooth, l.Bounds, l.HasExplicitSites ? l.Sites : null, l.Grid, l.IdlerTeeth, l.MinIdlers, l.MaxIdlers,
            l.UnrelatedClearance, l.KeepOutClearance, raw.KeepOuts.Select(k => new GearRouteRegion(k.Id, k.Bounds)), l.RequiredRegions, l.PreferredRegion, 0, 1), true);
        void Add(GearRoutingNormalization n)
        {
            ds.AddRange(n.Diagnostics);
            if (n.Status == GearRoutingStatus.Unsupported || n.Diagnostics.Any(d => d.Code.Contains("DOMAIN_LIMIT"))) status = TransmissionGoalStatus.Unsupported;
        }
        var legs = new List<CompoundRoutingLeg>(); var slots = new List<CompoundRoutingSlot>();
        foreach (var l in raw.Legs)
        {
            var n = Plane(l); Add(n); if (n.Request == null) continue; var p = n.Request;
            legs.Add(new CompoundRoutingLeg(p.Bounds, p.Sites, idlerTeeth: p.IdlerTeeth, minIdlers: p.MinIdlers, maxIdlers: p.MaxIdlerCount,
                unrelatedClearance: p.UnrelatedClearance, keepOutClearance: p.KeepOutClearance, requiredRegions: p.RequiredRegions, preferredRegion: p.PreferredRegion));
        }
        foreach (var s in raw.Slots)
        {
            var a = s.ReceivingTeeth.Distinct().OrderBy(x => x).ToArray(); var b = s.DrivingTeeth.Distinct().OrderBy(x => x).ToArray();
            if (a.Length == 0 || b.Length == 0 || a.Concat(b).Any(x => x <= 0)) Bad("slotTeeth", "Nonempty positive receiving/driving tooth domains required.");
            if (a.Length > 16 || b.Length > 16 || a.Concat(b).Any(x => x > GearRoutingContract.MaxTeeth)) Unsupported("slotTeeth", "No supported family can represent this complete tooth domain.");
            var n = Plane(new CompoundRoutingLeg(raw.Legs[0].Bounds, s.HasExplicitSites ? s.Sites : null, s.Grid, Array.Empty<int>(), maxIdlers: 0,
                requiredRegions: s.RequiredRegion == null ? null : new[] { new GearRouteRegion("slot-required", s.RequiredRegion) }, preferredRegion: s.PreferredRegion));
            Add(n); if (n.Request == null) continue;
            if (n.Request.Sites.Count > CompoundRoutingContract.MaxCompoundSites) Unsupported("slotSites", "Compound-axis sites exceed all existing profiles.");
            slots.Add(new CompoundRoutingSlot(a, b, n.Request.Sites, requiredRegion: s.RequiredRegion, preferredRegion: s.PreferredRegion));
        }
        if (ds.Count != 0) return new TransmissionGoalNormalization(null,
            ds.Any(d => d.Code.EndsWith("INVALID_INPUT", StringComparison.Ordinal)) ? TransmissionGoalStatus.InvalidInput : status, ds);
        return new TransmissionGoalNormalization(new TransmissionGoal(raw.Input!, raw.Output!, raw.TargetTransfer, raw.PitchRadiusTicksPerTooth,
            legs[0], slots[0], raw.MaximumCompounds, raw.MaximumTotalIdlers, available, output, raw.LayerPolicy, raw.MaximumLayerCount, families,
            raw.KeepOuts.OrderBy(k => k.Id, StringComparer.Ordinal), slot1: slots[1], leg1: legs[1], leg2: legs[2], workBudget: raw.WorkBudget,
            maximumReturned: raw.MaximumReturned, inputLayer: raw.InputLayer), TransmissionGoalStatus.Complete, ds);
    }

    public static CompiledTransmissionSearchPlan Compile(TransmissionGoal raw)
    {
        var normalized = Normalize(raw); var ds = new List<Diagnostic>(); var plans = new List<TransmissionChildPlan>();
        if (!normalized.IsValid) return new CompiledTransmissionSearchPlan(normalized, normalized.Status, Registry.Select(f => new TransmissionChildPlan(f,
            normalized.Status == TransmissionGoalStatus.Unsupported ? TransmissionApplicability.Unsupported : TransmissionApplicability.Malformed,
            string.Join(";", normalized.Diagnostics.Select(d => d.Code + ":" + d.SubjectId)), 0, 0, 0)), normalized.Diagnostics);
        var g = normalized.Goal!;
        foreach (var family in Registry)
        {
            int compounds = (int)family;
            string? excluded = !g.AllowedFamilies.Contains(family) ? "FAMILY_RESTRICTION" : compounds > g.MaximumCompounds ? "MAXIMUM_COMPOUNDS" :
                !g.OutputLayers.Contains(compounds) ? "OUTPUT_LAYER_RESTRICTION" : compounds + 1 > g.MaximumLayerCount ? "MAXIMUM_LAYER_COUNT" :
                Enumerable.Range(0, compounds + 1).Any(l => !g.AvailableLayers.Contains(l)) ? "REQUIRED_CANONICAL_LAYER_UNAVAILABLE" : null;
            if (excluded != null) { plans.Add(new TransmissionChildPlan(family, TransmissionApplicability.ExcludedByGoal, excluded, 0, 0, 0)); continue; }
            if (g.InputLayer != 0) { plans.Add(new TransmissionChildPlan(family, TransmissionApplicability.Unsupported, "CANONICAL_INPUT_LAYER_MUST_BE_ZERO", 0, 0, 0)); continue; }
            // A minimum that contradicts the envelope has an exact proof; never construct min > max child input.
            if (g.Legs.Take(compounds + 1).Sum(l => l.MinIdlers) > g.MaximumTotalIdlers)
            { plans.Add(new TransmissionChildPlan(family, TransmissionApplicability.ProvenInfeasible, "EXACT_MINIMUM_IDLERS_EXCEEDS_TOTAL", 0, 1, 0)); continue; }
            var probe = Lower(g, family, 0, TransmissionApplicability.Eligible, "ROUTING_REQUIRED", 0);
            if (probe.Applicability == TransmissionApplicability.Unsupported) { plans.Add(probe); continue; }
            int proofWork; bool impossible;
            if (family == TransmissionFamily.SimpleIdler)
            {
                proofWork = 1; var r = probe.Simple!;
                impossible = new Rational(BigInteger.Abs(g.TargetTransfer.Numerator), g.TargetTransfer.Denominator) != new Rational(g.Input.Teeth, g.Output.Teeth);
                if (impossible) { plans.Add(Lower(g, family, 0, TransmissionApplicability.ProvenInfeasible, "EXACT_SIMPLE_MAGNITUDE_INVARIANT", proofWork)); continue; }
                impossible = !Enumerable.Range(r.MinIdlers, r.MaxIdlerCount - r.MinIdlers + 1).Any(k => ((k + 1) % 2 == 0 ? 1 : -1) == g.TargetTransfer.Sign);
                if (impossible) { plans.Add(Lower(g, family, 0, TransmissionApplicability.ProvenInfeasible, "EXACT_SIMPLE_PARITY_INVARIANT", proofWork)); continue; }
            }
            else if (family == TransmissionFamily.OneCompound)
            {
                int completeQuota = g.Slots[0].ReceivingTeeth.Count * g.Slots[0].DrivingTeeth.Count;
                var query = new AnchoredCompoundGearRouter().SynthesizePairs(Lower(g, family, completeQuota, TransmissionApplicability.Eligible, "proof", 0).One!);
                if (!query.SearchComplete) throw new InvalidOperationException("Bounded exact pair query did not exhaust the declared tooth domain.");
                proofWork = query.ExaminedPairs; impossible = query.Pairs.Count == 0;
            }
            else
            {
                int completeQuota = g.Slots[0].ReceivingTeeth.Count * g.Slots[0].DrivingTeeth.Count * g.Slots[1].ReceivingTeeth.Count * g.Slots[1].DrivingTeeth.Count;
                var query = new AnchoredTwoCompoundGearRouter().SynthesizeAssignments(Lower(g, family, completeQuota, TransmissionApplicability.Eligible, "proof", 0).Two!);
                if (!query.SearchComplete) throw new InvalidOperationException("Bounded exact assignment query did not exhaust the declared tooth domain.");
                proofWork = query.ExaminedAssignments; impossible = query.Assignments.Count == 0;
            }
            plans.Add(Lower(g, family, 0, impossible ? TransmissionApplicability.ProvenInfeasible : TransmissionApplicability.Eligible,
                impossible ? "COMPLETE_EXACT_TOOTH_DOMAIN_EMPTY" : "EXACT_TOOTH_DOMAIN_NONEMPTY_ROUTING_REQUIRED", proofWork));
        }
        var status = TransmissionGoalStatus.Complete;
        if (plans.All(p => p.Applicability == TransmissionApplicability.ExcludedByGoal))
        { status = TransmissionGoalStatus.Unsupported; ds.Add(Error("NO_SUPPORTED_COVERAGE", "family/layers", "No supported canonical layout satisfies this combination of restrictions; an empty registry is not Infeasible search.")); }
        if (plans.Any(p => p.Applicability == TransmissionApplicability.Unsupported))
        { status = TransmissionGoalStatus.Unsupported; ds.Add(Error("PARTIAL_UNSUPPORTED_COVERAGE", "domain", "An included family cannot represent the complete requested domain; no child search is run.")); }
        if (plans.Sum(p => p.PreparationCeiling) > TransmissionGoalContract.MaxPreparationChecks)
        { status = TransmissionGoalStatus.Unsupported; ds.Add(Error("PREPARATION_LIMIT", "domain", "Combined graph-preparation ceiling exceeded.")); }
        int count = plans.Count(p => p.Applicability == TransmissionApplicability.Eligible), ordinal = 0;
        if (status == TransmissionGoalStatus.Complete && count > 0)
            plans = plans.Select(p => p.Applicability != TransmissionApplicability.Eligible ? p : Lower(g, p.Family,
                g.WorkBudget / count + (ordinal++ < g.WorkBudget % count ? 1 : 0), p.Applicability, p.Reason, p.ProofWork)).ToList();
        return new CompiledTransmissionSearchPlan(normalized, status, plans, ds);
    }

    private static TransmissionChildPlan Lower(TransmissionGoal g, TransmissionFamily f, int quota, TransmissionApplicability applicability, string reason, int proofWork)
    {
        int c = (int)f; var s = g.Slots[0]; var l = g.Legs[0];
        TransmissionChildPlan Unsupported(IEnumerable<Diagnostic> ds) => new TransmissionChildPlan(f, TransmissionApplicability.Unsupported,
            "CHILD_DOMAIN_UNREPRESENTABLE:" + string.Join(";", ds.Select(d => d.Code + ":" + d.SubjectId)), 0, proofWork, 0);
        if (f == TransmissionFamily.SimpleIdler)
        {
            var n = AnchoredGearRouter.Normalize(new AnchoredGearRoutingRequest(g.Input, g.Output, g.TargetTransfer, g.PitchRadiusTicksPerTooth, l.Bounds,
                l.Sites, idlerTeeth: l.IdlerTeeth, minIdlers: l.MinIdlers, maxIdlers: Math.Min(l.MaxIdlers, g.MaximumTotalIdlers),
                unrelatedClearance: l.UnrelatedClearance, keepOutClearance: l.KeepOutClearance, keepOuts: g.KeepOuts.Where(k => k.AppliesTo(0)).Select(k => new GearRouteRegion(k.Id, k.Bounds)),
                requiredRegions: l.RequiredRegions, preferredRegion: l.PreferredRegion, expansionBudget: quota, maximumReturned: 1));
            if (!n.IsValid) return Unsupported(n.Diagnostics);
            return new TransmissionChildPlan(f, applicability, reason, quota, proofWork, n.PotentialNodes * (n.PotentialNodes + 1) / 2, simple: n.Request);
        }
        if (f == TransmissionFamily.OneCompound)
        {
            var n = AnchoredCompoundGearRouter.Normalize(new AnchoredCompoundGearRoutingRequest(g.Input, g.Output, g.TargetTransfer, g.PitchRadiusTicksPerTooth,
                s.ReceivingTeeth, s.DrivingTeeth, l, g.Legs[1], s.Sites, maximumTotalIdlers: Math.Min(g.MaximumTotalIdlers, g.Legs.Take(2).Sum(x => x.MaxIdlers)),
                keepOuts: g.KeepOuts.Where(k => ((int)k.Layers & 3) != 0).Select(k => new LayeredGearRouteKeepOut(k.Id, k.Bounds, (GearRouteLayers)((int)k.Layers & 3))),
                requiredCompoundRegion: s.RequiredRegion, preferredCompoundRegion: s.PreferredRegion, workBudget: quota, maximumReturned: 1));
            if (!n.IsValid) return Unsupported(n.Diagnostics);
            return new TransmissionChildPlan(f, applicability, reason, quota, proofWork, n.PreparationCeiling, one: n.Request);
        }
        var two = AnchoredTwoCompoundGearRouter.Normalize(new AnchoredTwoCompoundGearRoutingRequest(g.Input, g.Output, g.TargetTransfer, g.PitchRadiusTicksPerTooth,
            g.Slots, g.Legs, Math.Min(g.MaximumTotalIdlers, g.Legs.Sum(x => x.MaxIdlers)), g.KeepOuts, quota, 1));
        if (!two.IsValid) return Unsupported(two.Diagnostics);
        return new TransmissionChildPlan(f, applicability, reason, quota, proofWork, two.PreparationCeiling, two: two.Request);
    }
}
