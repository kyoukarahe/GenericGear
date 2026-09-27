using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>One ordinary functional form for CLI and Unity. Disabled prefix text is not a typed constraint.</summary>
public sealed class TwoOutputTransmissionDraft
{
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> Definitions = Define();
    public static bool IsPrefixField(string key) => key.StartsWith("prefix-", StringComparison.Ordinal) || key.StartsWith("splitter-", StringComparison.Ordinal) || key == "hypothesis-limit";
    private static IReadOnlyList<(string Key, string Label, string Default)> Define()
    {
        var removed = new[] { "prefix-budget", "join-budget", "global-idlers", "total-pool-bytes", "triple-domain-limit", "pool-limit", "pool-bytes" };
        var defs = new List<(string Key, string Label, string Default)> { ("sharing-policy", "Sharing policy (AutoWithinAllowedProfiles / RootOnly / RequiredSharedPrefix)", "AutoWithinAllowedProfiles") };
        foreach (var d in SharedPrefixTransmissionDraft.Definitions.Where(d => !removed.Contains(d.Key)))
        {
            var value = d.Key == "a-output" ? "90,0" : d.Key == "b-output" ? "40,60" : d.Key == "b-domains-override" ? "true" :
                d.Key == "available-layers" ? "0;1;2" : d.Key == "domain" || d.Key == "b-domain" || d.Key == "prefix-domain" ? "sites" :
                d.Key == "global-bounds" || d.Key == "bounds" || d.Key == "b-bounds" || d.Key == "prefix-bounds" ? "-40,-60:150,110" :
                d.Key == "sites" ? "0,-20;20,-20;40,-20;60,-20;60,0" : d.Key == "depth" ? "0:5" :
                d.Key == "b-sites" ? "0,20;20,20;40,20" : d.Key == "b-depth" ? "0:3" :
                d.Key == "prefix-sites" ? "" : d.Key == "prefix-depth" ? "0:0" : d.Key == "splitter-sites" ? "40,0" : d.Default;
            var label = d.Key == "budget" ? "One total work budget (fixed profile and stage quotas)" :
                d.Key == "global-bodies" ? "Whole body ceiling (natural profile bounds intersected)" : d.Key == "global-compounds" ? "Whole compound ceiling" :
                d.Key == "global-preferred" ? "Common Preferred (after whole footprint)" : d.Label.Replace("Suffix domains:", "Common branch/suffix domains:");
            defs.Add((d.Key, label, value));
        }
        defs.AddRange(new[] { ("global-transmission-gears", "Non-endpoint single-body transmission DOF ceiling (splitter once)", "36"),
            ("observation-limit", "Common whole observation count ceiling", "4096"), ("observation-bytes", "Common whole observation byte ceiling (origin + metrics + diagnostics)", "8388608") });
        return defs.AsReadOnly();
    }
    public Dictionary<string, string> Fields { get; } = Definitions.ToDictionary(d => d.Key, d => d.Default, StringComparer.Ordinal);
    private static int Int(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    public TwoOutputTransmissionGoal Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(v => v == null || v.Length > 8192)) throw new FormatException("Two-output draft unknown field/text ceiling");
        string F(string key) => Fields[key].Trim();
        if (!Enum.TryParse<TwoOutputSharingPolicy>(F("sharing-policy"), out var policy) || !Enum.IsDefined(typeof(TwoOutputSharingPolicy), policy)) throw new FormatException("Unknown sharing policy");
        // Reuse field parsers only. There are no independently authored child goals or child results in this form.
        var common = new SharedDriverTransmissionDraft();
        foreach (var key in common.Fields.Keys.ToArray()) if (Fields.ContainsKey(key)) common.Fields[key] = F(key);
        common.Fields["join-budget"] = "0"; common.Fields["global-idlers"] = "30";
        var e = common.Parse(); TwoOutputPrefixDomain? prefix = null;
        if (policy != TwoOutputSharingPolicy.RootOnly)
        {
            var active = new SharedPrefixTransmissionDraft();
            foreach (var key in active.Fields.Keys.ToArray()) if (Fields.ContainsKey(key)) active.Fields[key] = F(key);
            active.Fields["prefix-budget"] = "0"; active.Fields["join-budget"] = "0"; active.Fields["global-idlers"] = "35";
            var p = active.Parse(); prefix = new TwoOutputPrefixDomain(p.Prefix, p.SplitterTeeth, p.HasExplicitSplitterSites ? p.SplitterSites : null,
                p.SplitterGrid, p.RequiredSplitterRegion, p.PreferredSplitterRegion, p.PrefixKeepOuts, p.HypothesisLimit);
        }
        return new TwoOutputTransmissionGoal(e.Input, e.PitchRadiusTicksPerTooth, e.Outputs, e.Bounds, policy, prefix, e.AvailableLayers, e.KeepOuts,
            e.UnrelatedClearance, e.KeepOutClearance, e.MaximumTotalCompounds, Int(F("global-bodies")), Int(F("global-transmission-gears")),
            e.PreferredRegion, e.RequiredRegions, e.TotalWorkBudget, e.MaximumReturned, e.InputLayer, Int(F("observation-limit")), Int(F("observation-bytes")));
    }
    public static TwoOutputTransmissionDraft FromGoal(TwoOutputTransmissionGoal goal, IEnumerable<KeyValuePair<string, string>>? labels = null)
    {
        var n = TwoOutputTransmissionCompiler.Normalize(goal); if (!n.IsValid) throw new FormatException("Cannot present invalid two-output goal"); var g = n.Goal!;
        var d = new TwoOutputTransmissionDraft(); var f = d.Fields;
        string Num(int i) => i.ToString(CultureInfo.InvariantCulture);
        string Box(GearRouteBox b) => new GearRoutePoint(b.MinX, b.MinY) + ":" + new GearRoutePoint(b.MaxX, b.MaxY);
        string Regions(IEnumerable<GearRouteRegion> rs) => string.Join(";", rs.Select(r => r.Id + ":" + Box(r.Bounds)));
        string Grid(GearRouteGrid? grid) => grid == null ? "" : grid.Origin + ":" + grid.Step + ":" + Box(grid.Bounds);
        var presentation = new SharedDriverTransmissionGoal(g.Input, g.PitchRadiusTicksPerTooth, g.Outputs, g.Bounds, g.AvailableLayers, g.KeepOuts,
            g.UnrelatedClearance, g.KeepOutClearance, Math.Min(4, g.MaximumTotalCompounds), Math.Min(30, g.MaximumTransmissionGears),
            g.PreferredRegion, g.RequiredRegions, g.TotalWorkBudget, 0, g.MaximumReturned, g.InputLayer);
        foreach (var item in SharedDriverTransmissionDraft.FromGoal(presentation, labels ?? goal.Outputs.Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel))).Fields)
            if (f.ContainsKey(item.Key)) f[item.Key] = item.Value;
        f["sharing-policy"] = g.SharingPolicy.ToString(); f["global-compounds"] = Num(g.MaximumTotalCompounds); f["global-bodies"] = Num(g.MaximumBodies);
        f["global-transmission-gears"] = Num(g.MaximumTransmissionGears); f["observation-limit"] = Num(g.ObservationCountLimit); f["observation-bytes"] = Num(g.ObservationByteLimit);
        if (g.SharedPrefix != null)
        {
            var p = g.SharedPrefix; var r = p.Routing;
            f["prefix-bounds"] = Box(r.Bounds); f["prefix-domain"] = r.HasExplicitSites ? "sites" : "grid"; f["prefix-sites"] = string.Join(";", r.Sites); f["prefix-grid"] = Grid(r.Grid);
            f["prefix-idler-teeth"] = string.Join(";", r.IdlerTeeth.Select(Num)); f["prefix-depth"] = Num(r.MinIdlers) + ":" + Num(r.MaxIdlers);
            f["prefix-clearance"] = GearRoutingContract.Number(r.UnrelatedClearance); f["prefix-keepout-clearance"] = GearRoutingContract.Number(r.KeepOutClearance);
            f["prefix-required"] = Regions(r.RequiredRegions); f["prefix-preferred"] = r.PreferredRegion == null ? "" : Box(r.PreferredRegion); f["prefix-keepouts"] = Regions(p.KeepOuts);
            f["splitter-domain"] = p.HasExplicitSites ? "sites" : "grid"; f["splitter-sites"] = string.Join(";", p.SplitterSites); f["splitter-grid"] = Grid(p.SplitterGrid);
            f["splitter-teeth"] = string.Join(";", p.SplitterTeeth.Select(Num)); f["splitter-required"] = p.RequiredSplitterRegion == null ? "" : Box(p.RequiredSplitterRegion);
            f["splitter-preferred"] = p.PreferredSplitterRegion == null ? "" : Box(p.PreferredSplitterRegion); f["hypothesis-limit"] = Num(p.HypothesisLimit);
        }
        return d;
    }
}
