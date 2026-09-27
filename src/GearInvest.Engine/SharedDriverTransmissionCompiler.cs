using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class SharedDriverTransmissionCompiler
{
    internal static Diagnostic Error(string code, string subject, string message) => new Diagnostic("SHARED_DRIVER_" + code, DiagnosticSeverity.Error, message, subject);
    private static bool KeyValid(string? key) => key != null && key.Length > 0 && key.Length <= 64 && key.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == ':' || c == '-' || c == '_');
    public static SharedDriverGoalNormalization Normalize(SharedDriverTransmissionGoal? raw)
    {
        var ds = new List<Diagnostic>();
        void Bad(string s, string m) => ds.Add(Error("INVALID_INPUT", s, m));
        void Unsupported(string s, string m) => ds.Add(Error("UNSUPPORTED", s, m));
        SharedDriverGoalNormalization Invalid() => new SharedDriverGoalNormalization(null,
            ds.Any(d => d.Code.EndsWith("INVALID_INPUT", StringComparison.Ordinal)) ? SharedDriverTransmissionStatus.InvalidInput : SharedDriverTransmissionStatus.Unsupported, ds);
        if (raw == null) { Bad("goal", "A shared-driver goal is required."); return Invalid(); }
        if (raw.Input == null || raw.Bounds == null || raw.Outputs.Count != 2 || raw.Outputs.Any(o => o == null))
        { Bad("root/outputs", "One shared input, global bounds and exactly two required outputs are mandatory."); return Invalid(); }
        if (raw.Outputs.Any(o => !KeyValid(o.Key)) || raw.Outputs.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count() != 2) Bad("outputKeys", "Two distinct ASCII stable keys of length 1..64 required.");
        if (raw.Outputs.Any(o => o.Anchor == null || o.Legs.Any(l => l == null) || o.Slots.Any(s => s == null)))
        { Bad("outputs", "All anchors and active domains required."); return Invalid(); }
        if (raw.Outputs.Select(o => o.Anchor.Position).Concat(new[] { raw.Input.Position }).Distinct().Count() != 3) Bad("anchors", "Input and two output axes must be distinct.");
        if (raw.TotalWorkBudget < 0 || raw.ReservedCombinationWork < 0 || raw.ReservedCombinationWork > raw.TotalWorkBudget || raw.MaximumReturned < 1 ||
            raw.MaximumTotalCompounds < 0 || raw.MaximumTotalIdlers < 0 || raw.PoolCountLimit < 1 || raw.PoolByteLimit < 1 || raw.PairDomainLimit < 1 ||
            raw.UnrelatedClearance.Sign < 0 || raw.KeepOutClearance.Sign < 0) Bad("limits", "Invalid work, return, topology, resource or clearance limits.");
        if (raw.TotalWorkBudget > TransmissionGoalContract.MaxWork || raw.MaximumReturned > SharedDriverTransmissionContract.MaxReturned || raw.MaximumTotalCompounds > 4 || raw.MaximumTotalIdlers > 30 ||
            raw.PoolCountLimit > SharedDriverTransmissionContract.MaxPoolCount || raw.PoolByteLimit > SharedDriverTransmissionContract.MaxPoolBytes || raw.PairDomainLimit > SharedDriverTransmissionContract.MaxPairDomain)
            Unsupported("limits", "The versioned finite two-branch resource envelope was exceeded.");
        if (raw.IngestionLimitExceeded || raw.Outputs.Any(o => o.IngestionLimitExceeded)) Unsupported("ingestion", "Bounded ingestion exceeded; no input domain was silently truncated.");
        if (raw.Profile != SharedDriverTransmissionContract.Profile || raw.Lowering != SharedDriverTransmissionContract.Lowering || raw.Allocation != SharedDriverTransmissionContract.Allocation ||
            raw.Join != SharedDriverTransmissionContract.Join || raw.Ranking != SharedDriverTransmissionContract.Ranking || raw.Resources != SharedDriverTransmissionContract.Resources)
            Unsupported("policy", "Unknown shared-driver policy/version; no automatic migration.");

        // Reuse the existing normalizers for exact geometry, active domains, layers and malformed-before-unsupported priority.
        var normalized = new List<SharedDriverTransmissionOutput>();
        foreach (var output in raw.Outputs.OrderBy(o => o.Key, StringComparer.Ordinal))
        {
            var child = TransmissionGoalCompiler.Normalize(Lower(raw, output, 0));
            foreach (var d in child.Diagnostics) ds.Add(Error(child.Status == TransmissionGoalStatus.InvalidInput ? "INVALID_INPUT" : "UNSUPPORTED", output.Key + "/" + d.SubjectId, d.Message));
            if (child.Goal != null) normalized.Add(FromChild(output.Key, child.Goal));
        }
        // The global envelope is additional to branch domains. It never relocates endpoints or treats other branches as keep-outs.
        if (!ValidBox(raw.Bounds) || raw.PreferredRegion != null && !ValidBox(raw.PreferredRegion) || raw.RequiredRegions.Any(r => r == null || !KeyValid(r.Id) || !ValidBox(r.Bounds)) ||
            raw.RequiredRegions.Select(r => r?.Id).Distinct(StringComparer.Ordinal).Count() != raw.RequiredRegions.Count)
            Bad("globalRegions", "Valid bounded rectangles and unique required-region IDs are mandatory.");
        if (raw.UnrelatedClearance > GearRoutingContract.MaxScalar || raw.KeepOutClearance > GearRoutingContract.MaxScalar) Unsupported("clearance", "Exact scalar ceiling exceeded.");
        if (ds.Count != 0) return Invalid();
        var canonical = Copy(raw, normalized, raw.AvailableLayers.Distinct().OrderBy(x => x), raw.KeepOuts.OrderBy(k => k.Id, StringComparer.Ordinal));
        if (canonical.Canonical.Length > SharedDriverTransmissionContract.MaxCanonicalGoalChars)
        { Unsupported("preparation", "Canonical resolved goal character ceiling exceeded; no domain was truncated."); return Invalid(); }
        return new SharedDriverGoalNormalization(canonical, SharedDriverTransmissionStatus.Complete, ds);
    }
    private static bool ValidBox(GearRouteBox b) => b.MinX <= b.MaxX && b.MinY <= b.MaxY && new[] { b.MinX, b.MinY, b.MaxX, b.MaxY }.All(x => BigInteger.Abs(x) <= GearRoutingContract.MaxScalar);

    internal static SharedDriverTransmissionGoal Copy(SharedDriverTransmissionGoal g, IEnumerable<SharedDriverTransmissionOutput> outputs, IEnumerable<int>? layers = null, IEnumerable<ThreeLayerGearRouteKeepOut>? keepOuts = null) =>
        new SharedDriverTransmissionGoal(g.Input, g.PitchRadiusTicksPerTooth, outputs, g.Bounds, layers ?? g.AvailableLayers, keepOuts ?? g.KeepOuts,
            g.UnrelatedClearance, g.KeepOutClearance, g.MaximumTotalCompounds, g.MaximumTotalIdlers, g.PreferredRegion, g.RequiredRegions.OrderBy(r => r.Id, StringComparer.Ordinal),
            g.TotalWorkBudget, g.ReservedCombinationWork, g.MaximumReturned, g.InputLayer, g.Profile, g.Lowering, g.Allocation, g.Join, g.Ranking, g.Resources, g.PoolCountLimit, g.PoolByteLimit, g.PairDomainLimit);

    internal static SharedDriverTransmissionOutput FromChild(string key, TransmissionGoal g) => new SharedDriverTransmissionOutput(key, g.Output, g.TargetTransfer,
        g.Legs[0], g.Slots[0], g.OutputLayers, g.LayerPolicy, g.AllowedFamilies, g.MaximumCompounds, g.MaximumTotalIdlers, slot1: g.Slots[1], leg1: g.Legs[1], leg2: g.Legs[2]);

    internal static TransmissionGoal Lower(SharedDriverTransmissionGoal g, SharedDriverTransmissionOutput o, int budget)
    {
        CompoundRoutingLeg Leg(CompoundRoutingLeg l) => new CompoundRoutingLeg(l.Bounds, l.HasExplicitSites ? l.Sites : null, l.Grid, l.IdlerTeeth, l.MinIdlers, l.MaxIdlers,
            BigInteger.Max(l.UnrelatedClearance, g.UnrelatedClearance), BigInteger.Max(l.KeepOutClearance, g.KeepOutClearance), l.RequiredRegions, l.PreferredRegion);
        return new TransmissionGoal(g.Input, o.Anchor, o.TargetTransfer, g.PitchRadiusTicksPerTooth, Leg(o.Legs[0]), o.Slots[0],
            o.MaximumCompounds, o.MaximumIdlers, g.AvailableLayers, o.OutputLayers, o.LayerPolicy, 3, o.AllowedFamilies, g.KeepOuts,
            slot1: o.Slots[1], leg1: Leg(o.Legs[1]), leg2: Leg(o.Legs[2]), workBudget: budget, maximumReturned: 1, inputLayer: g.InputLayer);
    }

    public static SharedDriverTransmissionSearchPlan Compile(SharedDriverTransmissionGoal raw)
    {
        var n = Normalize(raw); var ds = new List<Diagnostic>(n.Diagnostics);
        if (!n.IsValid) return new SharedDriverTransmissionSearchPlan(n, n.Status, Array.Empty<SharedDriverBranchPlan>(), ds);
        var g = n.Goal!;
        var probes = g.Outputs.Select(o => new SharedDriverBranchPlan(o.Key, TransmissionGoalCompiler.Compile(Lower(g, o, 0)))).ToArray();
        if (probes.Any(p => !p.Plan.IsSupported))
        {
            foreach (var b in probes) ds.AddRange(b.Plan.Diagnostics.Select(d => Error("UNSUPPORTED", b.OutputKey + "/" + d.SubjectId, d.Message)));
            return new SharedDriverTransmissionSearchPlan(n, SharedDriverTransmissionStatus.Unsupported, probes, ds);
        }
        // The global ordinal remainder is allocated across (stable output key, family), not one full pool per output.
        int count = probes.Sum(b => b.Plan.Families.Count(f => f.Applicability == TransmissionApplicability.Eligible)), ordinal = 0;
        var branches = new List<SharedDriverBranchPlan>();
        foreach (var probe in probes)
        {
            var quotas = probe.Plan.Families.Select(f => f.Applicability != TransmissionApplicability.Eligible ? 0 :
                g.GenerationWorkPool / count + (ordinal++ < g.GenerationWorkPool % count ? 1 : 0)).ToArray();
            // Consecutive key-major quotas are themselves balanced, so the unchanged 16A compiler gives precisely this allocation.
            var plan = TransmissionGoalCompiler.Compile(Lower(g, g.Outputs.Single(o => o.Key == probe.OutputKey), quotas.Sum()));
            if (!plan.Families.Select(f => f.Quota).SequenceEqual(quotas)) throw new InvalidOperationException("SharedDriverBalancedQuotaDisagreement");
            branches.Add(new SharedDriverBranchPlan(probe.OutputKey, plan));
        }
        return new SharedDriverTransmissionSearchPlan(n, SharedDriverTransmissionStatus.Complete, branches, ds);
    }
}
