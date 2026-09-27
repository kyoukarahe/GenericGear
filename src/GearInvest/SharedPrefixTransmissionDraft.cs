using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>Ordinary text authoring. Global output transfers remain relative to the actual input.</summary>
public sealed class SharedPrefixTransmissionDraft
{
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> Definitions = Define();
    private static IReadOnlyList<(string Key, string Label, string Default)> Define()
    {
        var defs = SharedDriverTransmissionDraft.Definitions.Where(d => d.Key != "pair-domain-limit").Select(d =>
        {
            var value = d.Key == "input" ? "0,0" : d.Key == "a-output" ? "130,0" : d.Key == "b-output" ? "80,60" :
                d.Key == "b-domains-override" ? "false" : d.Key == "global-idlers" ? "35" : d.Key == "budget" ? "100000" :
                d.Key == "global-bounds" || d.Key == "bounds" ? "-60,-100:210,180" :
                d.Key == "sites" ? "" : d.Key == "depth" ? "0:0" : d.Default;
            var label = d.Key == "input" ? "Actual input XY" : d.Key == "input-teeth" ? "Actual input teeth" :
                d.Key == "input-layer" ? "Actual input layer (0)" : d.Key == "global-idlers" ? "Whole idlers 0..35 (splitter excluded)" :
                d.Key == "budget" ? "Total prefix + suffix + triple work" : d.Key == "join-budget" ? "Reserved triple work" :
                d.Label.Replace("Shared domains:", "Suffix domains:");
            return (d.Key, label, value);
        }).ToList();
        foreach (var key in TransmissionGoalDraft.LegKeys.Concat(new[] { "keepouts" }))
        {
            var d = GearRoutingDraft.Definitions.Single(x => x.Key == key);
            defs.Add(("prefix-" + key, "Prefix " + d.Label, key == "domain" ? "sites" : key == "bounds" ? "-60,-100:210,180" : key == "depth" ? "2:4" :
                key == "sites" ? "20,0;40,0;0,20;20,20;40,20;0,-20;20,-20;40,-20" : key == "idler-teeth" ? "10" : d.Default));
        }
        defs.AddRange(new[] {
            ("splitter-teeth", "Splitter tooth domain", "30"), ("splitter-domain", "Splitter sites/grid", "sites"),
            ("splitter-sites", "Splitter XY domain", "80,0"), ("splitter-grid", "Splitter grid origin:step:min:max", "0,0:20:0,0:4,0"),
            ("splitter-required", "Required splitter rectangle", ""), ("splitter-preferred", "Preferred splitter rectangle", ""),
            ("prefix-budget", "Reserved prefix work", "30000"), ("hypothesis-limit", "Hypothesis resource ceiling (1..8)", "8"),
            ("total-pool-bytes", "All observed pools byte ceiling", "524288"), ("triple-domain-limit", "Triple domain resource ceiling", "1000000"),
            ("global-bodies", "Whole body ceiling (1..47)", "47") });
        return defs.AsReadOnly();
    }
    public Dictionary<string, string> Fields { get; } = Definitions.ToDictionary(d => d.Key, d => d.Default, StringComparer.Ordinal);
    private static int Int(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    public SharedPrefixTransmissionGoal Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(v => v == null || v.Length > 8192)) throw new FormatException("Shared-prefix draft unknown field/text ceiling");
        string F(string key) => Fields[key].Trim();
        // Reuse the ordinary two-output field parser, not its generator. No root-only result enters this pipeline.
        var endpoints = new SharedDriverTransmissionDraft();
        foreach (var key in endpoints.Fields.Keys.ToArray()) if (Fields.ContainsKey(key)) endpoints.Fields[key] = F(key);
        endpoints.Fields["pair-domain-limit"] = F("triple-domain-limit"); var e = endpoints.Parse();
        var p = new GearRoutingDraft();
        foreach (var key in new[] { "input", "input-teeth", "scale" }) p.Fields[key] = F(key);
        p.Fields["output"] = F("a-output"); p.Fields["output-teeth"] = F("a-output-teeth");
        foreach (var key in TransmissionGoalDraft.LegKeys.Concat(new[] { "keepouts" })) p.Fields[key] = F("prefix-" + key);
        var pr = p.Parse();
        var domain = new GearRoutingDraft(); domain.Fields["depth"] = "0:0";
        foreach (var key in new[] { "domain", "sites", "grid" }) domain.Fields[key] = F("splitter-" + key);
        var s = domain.Parse();
        return new SharedPrefixTransmissionGoal(e.Input, e.PitchRadiusTicksPerTooth, e.Outputs,
            new CompoundRoutingLeg(pr.Bounds, pr.HasExplicitSites ? pr.Sites : null, pr.Grid, pr.IdlerTeeth, pr.MinIdlers, pr.MaxIdlerCount,
                pr.UnrelatedClearance, pr.KeepOutClearance, pr.RequiredRegions, pr.PreferredRegion),
            F("splitter-teeth").Split(';').Select(Int), e.Bounds, s.HasExplicitSites ? s.Sites : null, s.Grid,
            F("splitter-required") == "" ? null : GearRoutingDraft.Box(F("splitter-required")), F("splitter-preferred") == "" ? null : GearRoutingDraft.Box(F("splitter-preferred")),
            pr.KeepOuts, e.AvailableLayers, e.KeepOuts, e.UnrelatedClearance, e.KeepOutClearance, e.MaximumTotalCompounds, e.MaximumTotalIdlers, Int(F("global-bodies")),
            e.PreferredRegion, e.RequiredRegions, e.TotalWorkBudget, Int(F("prefix-budget")), e.ReservedCombinationWork, e.MaximumReturned,
            Int(F("hypothesis-limit")), e.PoolCountLimit, e.PoolByteLimit, Int(F("total-pool-bytes")), e.PairDomainLimit, e.InputLayer);
    }
    public static SharedPrefixTransmissionDraft FromGoal(SharedPrefixTransmissionGoal goal, IEnumerable<KeyValuePair<string, string>>? labels = null)
    {
        var n = SharedPrefixTransmissionCompiler.Normalize(goal); if (!n.IsValid) throw new FormatException("Cannot present invalid shared-prefix goal"); var g = n.Goal!;
        var draft = new SharedPrefixTransmissionDraft(); var f = draft.Fields;
        string Num(int i) => i.ToString(CultureInfo.InvariantCulture);
        string Box(GearRouteBox b) => new GearRoutePoint(b.MinX, b.MinY) + ":" + new GearRoutePoint(b.MaxX, b.MaxY);
        string Regions(IEnumerable<GearRouteRegion> rs) => string.Join(";", rs.Select(r => r.Id + ":" + Box(r.Bounds)));
        var presentation = new SharedDriverTransmissionGoal(g.Input, g.PitchRadiusTicksPerTooth, g.Outputs, g.Bounds, g.AvailableLayers, g.KeepOuts,
            g.UnrelatedClearance, g.KeepOutClearance, g.MaximumTotalCompounds, Math.Min(30, g.MaximumTotalIdlers), g.PreferredRegion, g.RequiredRegions,
            g.TotalWorkBudget, g.ReservedJoinWork, g.MaximumReturned, g.InputLayer, poolCountLimit: g.PoolCountLimit, poolByteLimit: g.PoolByteLimit, pairDomainLimit: g.TripleDomainLimit);
        foreach (var item in SharedDriverTransmissionDraft.FromGoal(presentation, labels ?? goal.Outputs.Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel))).Fields)
            if (f.ContainsKey(item.Key)) f[item.Key] = item.Value;
        var p = g.Prefix; f["prefix-bounds"] = Box(p.Bounds); f["prefix-domain"] = "sites"; f["prefix-sites"] = string.Join(";", p.Sites);
        f["prefix-idler-teeth"] = string.Join(";", p.IdlerTeeth.Select(Num)); f["prefix-depth"] = Num(p.MinIdlers) + ":" + Num(p.MaxIdlers);
        f["prefix-clearance"] = p.UnrelatedClearance.ToString(CultureInfo.InvariantCulture); f["prefix-keepout-clearance"] = p.KeepOutClearance.ToString(CultureInfo.InvariantCulture);
        f["prefix-required"] = Regions(p.RequiredRegions); f["prefix-preferred"] = p.PreferredRegion == null ? "" : Box(p.PreferredRegion); f["prefix-keepouts"] = Regions(g.PrefixKeepOuts);
        f["splitter-domain"] = "sites"; f["splitter-sites"] = string.Join(";", g.SplitterSites); f["splitter-teeth"] = string.Join(";", g.SplitterTeeth.Select(Num));
        f["splitter-required"] = g.RequiredSplitterRegion == null ? "" : Box(g.RequiredSplitterRegion); f["splitter-preferred"] = g.PreferredSplitterRegion == null ? "" : Box(g.PreferredSplitterRegion);
        f["prefix-budget"] = Num(g.ReservedPrefixWork); f["hypothesis-limit"] = Num(g.HypothesisLimit); f["total-pool-bytes"] = Num(g.TotalPoolByteLimit);
        f["triple-domain-limit"] = Num(g.TripleDomainLimit); f["global-bodies"] = Num(g.MaximumBodies); f["global-idlers"] = Num(g.MaximumTotalIdlers);
        return draft;
    }
}
