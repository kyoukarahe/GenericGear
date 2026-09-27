using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class SharedPrefixTransmissionCompiler
{
    internal static Diagnostic Error(string code, string subject, string message) => new Diagnostic("SHARED_PREFIX_" + code, DiagnosticSeverity.Error, message, subject);
    // Reuse endpoint/domain normalization and environment predicates, not the root-only generation semantics.
    internal static SharedDriverTransmissionGoal EndpointDomain(SharedPrefixTransmissionGoal g, GearRouteAnchor? input = null, IEnumerable<SharedDriverTransmissionOutput>? outputs = null, int work = 0, bool wholeRegions = true)
        => new SharedDriverTransmissionGoal(input ?? g.Input, g.PitchRadiusTicksPerTooth, outputs ?? g.Outputs, g.Bounds, g.AvailableLayers, g.KeepOuts,
            g.UnrelatedClearance, g.KeepOutClearance, g.MaximumTotalCompounds, Math.Min(30, g.MaximumTotalIdlers), wholeRegions ? g.PreferredRegion : null, wholeRegions ? g.RequiredRegions : null,
            work, 0, 1, g.InputLayer);
    public static SharedPrefixGoalNormalization Normalize(SharedPrefixTransmissionGoal? raw)
    {
        var ds = new List<Diagnostic>();
        void Bad(string field, string text) => ds.Add(Error("INVALID_INPUT", field, text));
        void Limit(string field, string text) => ds.Add(Error("UNSUPPORTED", field, text));
        SharedPrefixGoalNormalization Invalid() => new SharedPrefixGoalNormalization(null,
            ds.Any(d => d.Code.EndsWith("INVALID_INPUT", StringComparison.Ordinal)) ? SharedPrefixTransmissionStatus.InvalidInput : SharedPrefixTransmissionStatus.Unsupported, ds);
        if (raw == null || raw.Input == null || raw.Prefix == null || raw.Bounds == null) { Bad("goal", "Actual input, prefix domain and global bounds required."); return Invalid(); }
        if (raw.IngestionLimitExceeded || raw.Prefix.IngestionLimitExceeded) Limit("ingestion", "Bounded ingestion exceeded; no truncated domain is accepted.");
        if (raw.Profile != SharedPrefixTransmissionContract.Profile || raw.Lowering != SharedPrefixTransmissionContract.Lowering || raw.Allocation != SharedPrefixTransmissionContract.Allocation ||
            raw.Join != SharedPrefixTransmissionContract.Join || raw.Ranking != SharedPrefixTransmissionContract.Ranking || raw.Resources != SharedPrefixTransmissionContract.Resources) Limit("policy", "Unknown shared-prefix policy/version.");
        if (raw.TotalWorkBudget < 0 || raw.ReservedPrefixWork < 0 || raw.ReservedJoinWork < 0 || (long)raw.ReservedPrefixWork + raw.ReservedJoinWork > raw.TotalWorkBudget ||
            raw.MaximumReturned < 1 || raw.MaximumTotalIdlers < 0 || raw.MaximumBodies < 1 || raw.HypothesisLimit < 1 || raw.PoolCountLimit < 1 || raw.PoolByteLimit < 1 || raw.TotalPoolByteLimit < 1 || raw.TripleDomainLimit < 1)
            Bad("limits", "Nonnegative disjoint work pools and positive resource/return limits required.");
        if (raw.TotalWorkBudget > 1000000 || raw.MaximumReturned > 32 || raw.MaximumTotalIdlers > 35 || raw.MaximumBodies > 47 || raw.HypothesisLimit > 8 || raw.PoolCountLimit > 4096 ||
            raw.PoolByteLimit > SharedPrefixTransmissionContract.MaxPoolBytes || raw.TotalPoolByteLimit > SharedPrefixTransmissionContract.MaxTotalPoolBytes || raw.TripleDomainLimit > 1000000)
            Limit("limits", "Versioned bounded profile exceeded.");
        if (raw.InputLayer != 0) Limit("inputLayer", "Actual input, prefix and splitter use physical layer 0.");
        var teeth = raw.SplitterTeeth.Distinct().OrderBy(t => t).ToArray();
        if (teeth.Length == 0 || teeth.Any(t => t <= 0)) Bad("splitterTeeth", "Nonempty positive splitter tooth options required.");
        if (teeth.Length > 8 || teeth.Any(t => t > GearRoutingContract.MaxTeeth)) Limit("splitterTeeth", "Splitter tooth domain ceiling exceeded.");
        var endpoint = SharedDriverTransmissionCompiler.Normalize(EndpointDomain(raw));
        ds.AddRange(endpoint.Diagnostics.Select(d => Error(endpoint.Status == SharedDriverTransmissionStatus.InvalidInput ? "INVALID_INPUT" : "UNSUPPORTED", d.SubjectId ?? "endpoints", d.Message)));
        if (!endpoint.IsValid) return Invalid();
        // Normalization only. The actual input and an existing distinct output provide scalar validation context;
        // preserveAnchorSites retains every declared site, including role-conflicting hypotheses for explicit proof.
        GearRoutingNormalization Domain(CompoundRoutingLeg l, IEnumerable<GearRouteRegion> keep) => AnchoredGearRouter.NormalizeDomain(new AnchoredGearRoutingRequest(
            raw.Input, endpoint.Goal!.Outputs[0].Anchor, new Rational(1, 1), raw.PitchRadiusTicksPerTooth, l.Bounds, l.HasExplicitSites ? l.Sites : null, l.Grid,
            l.IdlerTeeth, l.MinIdlers, l.MaxIdlers, BigInteger.Max(l.UnrelatedClearance, raw.UnrelatedClearance), BigInteger.Max(l.KeepOutClearance, raw.KeepOutClearance), keep,
            l.RequiredRegions, l.PreferredRegion, 0, 1), true);
        var prefix = Domain(raw.Prefix, raw.PrefixKeepOuts);
        var splitter = Domain(new CompoundRoutingLeg(raw.Bounds, raw.HasExplicitSplitterSites ? raw.SplitterSites : null, raw.SplitterGrid, Array.Empty<int>(), maxIdlers: 0,
            requiredRegions: raw.RequiredSplitterRegion == null ? null : new[] { new GearRouteRegion("splitter", raw.RequiredSplitterRegion) }, preferredRegion: raw.PreferredSplitterRegion), Array.Empty<GearRouteRegion>());
        foreach (var n in new[] { prefix, splitter }) ds.AddRange(n.Diagnostics.Select(d => Error(d.Code.Contains("INVALID_INPUT") ? "INVALID_INPUT" : "UNSUPPORTED", d.SubjectId ?? "domain", d.Message)));
        if (splitter.Request != null && (splitter.Request.Sites.Count == 0 || splitter.Request.Sites.Count > SharedPrefixTransmissionContract.MaxSplitterSites)) Limit("splitterSites", "Declare 1..32 exact finite splitter sites.");
        if (ds.Count != 0) return Invalid();
        var p = prefix.Request!; var e = endpoint.Goal!;
        var normalizedPrefix = new CompoundRoutingLeg(p.Bounds, p.Sites, idlerTeeth: p.IdlerTeeth, minIdlers: p.MinIdlers, maxIdlers: p.MaxIdlerCount,
            unrelatedClearance: p.UnrelatedClearance, keepOutClearance: p.KeepOutClearance, requiredRegions: p.RequiredRegions, preferredRegion: p.PreferredRegion);
        var g = new SharedPrefixTransmissionGoal(e.Input, e.PitchRadiusTicksPerTooth, e.Outputs, normalizedPrefix, teeth, e.Bounds, splitter.Request!.Sites,
            requiredSplitterRegion: raw.RequiredSplitterRegion, preferredSplitterRegion: raw.PreferredSplitterRegion, prefixKeepOuts: p.KeepOuts,
            availableLayers: e.AvailableLayers, keepOuts: e.KeepOuts, unrelatedClearance: e.UnrelatedClearance, keepOutClearance: e.KeepOutClearance,
            maximumTotalCompounds: e.MaximumTotalCompounds, maximumTotalIdlers: raw.MaximumTotalIdlers, maximumBodies: raw.MaximumBodies,
            preferredRegion: e.PreferredRegion, requiredRegions: e.RequiredRegions, totalWorkBudget: raw.TotalWorkBudget, reservedPrefixWork: raw.ReservedPrefixWork,
            reservedJoinWork: raw.ReservedJoinWork, maximumReturned: raw.MaximumReturned, hypothesisLimit: raw.HypothesisLimit, poolCountLimit: raw.PoolCountLimit,
            poolByteLimit: raw.PoolByteLimit, totalPoolByteLimit: raw.TotalPoolByteLimit, tripleDomainLimit: raw.TripleDomainLimit);
        if (g.Canonical.Length > SharedPrefixTransmissionContract.MaxCanonicalGoalChars) { Limit("canonicalGoal", "Resolved canonical input character ceiling exceeded."); return Invalid(); }
        return new SharedPrefixGoalNormalization(g, SharedPrefixTransmissionStatus.Complete, ds);
    }

    internal static TransmissionGoal PrefixGoal(SharedPrefixTransmissionGoal g, GearRouteAnchor splitter, Rational r, int quota)
    {
        var unused = new CompoundRoutingSlot(new[] { 10 }, new[] { 20 }, Array.Empty<GearRoutePoint>());
        // Prefix-specific and global layer-0 obstacles both constrain the actual router. Namespaces prevent accidental ID aliasing.
        var keep = g.KeepOuts.Where(k => k.AppliesTo(0)).Select((k, i) => new ThreeLayerGearRouteKeepOut("global:" + GearRoutingContract.Number(i), k.Bounds, ThreeRouteLayers.Layer0))
            .Concat(g.PrefixKeepOuts.Select((k, i) => new ThreeLayerGearRouteKeepOut("prefix:" + GearRoutingContract.Number(i), k.Bounds, ThreeRouteLayers.Layer0))).ToArray();
        return new TransmissionGoal(g.Input, splitter, r, g.PitchRadiusTicksPerTooth, g.Prefix, unused, 0, g.Prefix.MaxIdlers,
            new[] { 0 }, new[] { 0 }, TransmissionOutputLayerPolicy.FixedOutputLayer, 1, new[] { TransmissionFamily.SimpleIdler }, keep, workBudget: quota, maximumReturned: 1);
    }
    internal static SharedDriverTransmissionGoal SuffixGoal(SharedPrefixTransmissionGoal g, GearRouteAnchor splitter, Rational r, int quota)
        => EndpointDomain(g, splitter, g.Outputs.Select(o => new SharedDriverTransmissionOutput(o.Key, o.Anchor, o.TargetTransfer / r, o.Legs[0], o.Slots[0], o.OutputLayers,
            o.LayerPolicy, o.AllowedFamilies, o.MaximumCompounds, o.MaximumIdlers, slot1: o.Slots[1], leg1: o.Legs[1], leg2: o.Legs[2])), quota, false);

    public static SharedPrefixTransmissionSearchPlan Compile(SharedPrefixTransmissionGoal raw)
    {
        var n = Normalize(raw); var ds = new List<Diagnostic>(n.Diagnostics); var hypotheses = new List<SharedPrefixHypothesis>();
        if (!n.IsValid) return new SharedPrefixTransmissionSearchPlan(n, n.Status, hypotheses, 0, "INVALID_OR_UNSUPPORTED", ds);
        var g = n.Goal!; var signs = Enumerable.Range(g.Prefix.MinIdlers, g.Prefix.MaxIdlers - g.Prefix.MinIdlers + 1).Select(k => (k + 1) % 2 == 0 ? 1 : -1).Distinct().OrderBy(s => s).ToArray();
        int potential = checked(g.SplitterTeeth.Count * g.SplitterSites.Count * signs.Length);
        if (potential > g.HypothesisLimit) return new SharedPrefixTransmissionSearchPlan(n, SharedPrefixTransmissionStatus.IncompleteResource, hypotheses, potential, "HYPOTHESIS_PREPARATION_CEILING", ds);
        foreach (int teeth in g.SplitterTeeth) foreach (var site in g.SplitterSites) foreach (int sign in signs)
        {
            var s = new GearRouteAnchor(site, teeth); var r = new Rational(sign * g.Input.Teeth, teeth);
            string exclusion = site.Equals(g.Input.Position) || g.Outputs.Any(o => o.Anchor.Position.Equals(site)) ? "EXACT_DISTINCT_SPLITTER_ROLE" :
                g.RequiredSplitterRegion != null && !GearRoutingGeometry.Contains(g.RequiredSplitterRegion, site) ? "EXACT_REQUIRED_SPLITTER_REGION" : "";
            if (exclusion.Length != 0) { hypotheses.Add(new SharedPrefixHypothesis(s, r, null, null, exclusion)); continue; }
            var prefix = TransmissionGoalCompiler.Compile(PrefixGoal(g, s, r, 0)); var suffix = SharedDriverTransmissionCompiler.Compile(SuffixGoal(g, s, r, 0));
            if (!prefix.IsSupported || !suffix.IsSupported)
                ds.AddRange(prefix.Diagnostics.Concat(suffix.Diagnostics).Select(d => Error("UNSUPPORTED", s.Canonical + "/" + d.SubjectId, d.Message)));
            hypotheses.Add(new SharedPrefixHypothesis(s, r, prefix, suffix));
        }
        if (hypotheses.Any(h => h.ExclusionProof.Length == 0 && (!h.Prefix!.IsSupported || !h.Suffix!.IsSupported)))
            return new SharedPrefixTransmissionSearchPlan(n, SharedPrefixTransmissionStatus.Unsupported, hypotheses, potential, "INCLUDED_CHILD_DOMAIN_UNSUPPORTED", ds);
        long preparation = hypotheses.Sum(h => (long)(h.Prefix?.PreparationCeiling ?? 0) + (h.Suffix?.Branches.Sum(b => (long)b.Plan.PreparationCeiling) ?? 0));
        if (preparation > SharedPrefixTransmissionContract.MaxPreparationChecks)
            return new SharedPrefixTransmissionSearchPlan(n, SharedPrefixTransmissionStatus.IncompleteResource, hypotheses, potential, "COMBINED_PREPARATION_CEILING", ds);
        int pCount = hypotheses.Count(h => h.Prefix?.Families.Any(f => f.Applicability == TransmissionApplicability.Eligible) == true);
        int sCount = hypotheses.Sum(h => h.Suffix?.Branches.Sum(b => b.Plan.Families.Count(f => f.Applicability == TransmissionApplicability.Eligible)) ?? 0), pi = 0, si = 0;
        int Quota(int total, int count, ref int ordinal) => count == 0 ? 0 : total / count + (ordinal++ < total % count ? 1 : 0);
        var allocated = new List<SharedPrefixHypothesis>();
        foreach (var h in hypotheses)
        {
            if (h.ExclusionProof.Length != 0) { allocated.Add(h); continue; }
            int pq = h.Prefix!.Families.Any(f => f.Applicability == TransmissionApplicability.Eligible) ? Quota(g.ReservedPrefixWork, pCount, ref pi) : 0;
            var quotas = h.Suffix!.Branches.SelectMany(b => b.Plan.Families).Select(f => f.Applicability == TransmissionApplicability.Eligible ? Quota(g.SuffixWorkPool, sCount, ref si) : 0).ToArray();
            var suffix = SharedDriverTransmissionCompiler.Compile(SuffixGoal(g, h.Splitter, h.Transfer, quotas.Sum()));
            if (!suffix.Branches.SelectMany(b => b.Plan.Families).Select(f => f.Quota).SequenceEqual(quotas)) throw new InvalidOperationException("SharedPrefixGlobalQuotaDisagreement");
            allocated.Add(new SharedPrefixHypothesis(h.Splitter, h.Transfer, TransmissionGoalCompiler.Compile(PrefixGoal(g, h.Splitter, h.Transfer, pq)), suffix));
        }
        return new SharedPrefixTransmissionSearchPlan(n, SharedPrefixTransmissionStatus.Complete, allocated, potential, "", ds);
    }
}
