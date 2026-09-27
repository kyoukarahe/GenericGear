using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>One ordinary CLI/Unity form. A shared input exists only at top-level; branch domains inherit or explicitly override.</summary>
public sealed class SharedDriverTransmissionDraft
{
    private static readonly string[] OutputFields = { "output", "output-teeth", "target", "layer-policy", "output-layers", "families", "max-compounds", "total-idlers" };
    public static readonly IReadOnlyList<string> DomainFields = TransmissionGoalDraft.Definitions.Where(d => TransmissionGoalDraft.LegKeys.Contains(d.Key) || d.Key.StartsWith("compound-", StringComparison.Ordinal) ||
        new[] { "slot0-", "slot1-", "leg0-", "leg1-", "leg2-" }.Any(p => d.Key.StartsWith(p, StringComparison.Ordinal))).Select(d => d.Key).ToArray();
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> Definitions = Define();
    private static IReadOnlyList<(string Key, string Label, string Default)> Define()
    {
        var defs = new List<(string Key, string Label, string Default)> {
            ("input", "Shared input XY", "0,0"), ("input-teeth", "Shared input teeth", "10"), ("scale", "Pitch radius ticks/tooth", "1"), ("input-layer", "Shared input layer (0)", "0"),
            ("available-layers", "Shared physical layers", "0;1;2"), ("global-bounds", "Global full-disk bounds", "-100,-100:300,140"), ("global-clearance", "Global unrelated clearance", "0"),
            ("global-keepout-clearance", "Global keep-out clearance", "0"), ("keepouts", "Global keepouts id:layers:minXY:maxXY", ""), ("global-required", "Global Required id:minXY:maxXY", ""), ("global-preferred", "Joint Preferred rectangle", ""),
            ("global-compounds", "Total compound shafts 0..4", "4"), ("global-idlers", "Total simple idlers 0..30", "30"), ("budget", "Total generation + pair work", "30000"),
            ("join-budget", "Reserved pair work", "10000"), ("cap", "Returned whole mechanisms (1..32)", "32"), ("pool-limit", "Observed pool count ceiling", "4096"), ("pool-bytes", "Observed pool byte ceiling", "262144"), ("pair-domain-limit", "Pair domain resource ceiling", "1000000") };
        foreach (var key in DomainFields)
        {
            var def = TransmissionGoalDraft.Definitions.Single(d => d.Key == key);
            var value = key == "bounds" ? "-100,-100:300,140" : key == "sites" ? "20,0;40,0;60,0;0,-20;20,-20;40,-20;60,-20" :
                key == "compound-sites" ? "" : key == "compound-receiving-teeth" ? "10" : key == "compound-driving-teeth" ? "30" : def.Default;
            defs.Add((key, "Shared domains: " + def.Label, value));
        }
        foreach (var prefix in new[] { "a-", "b-" })
        {
            bool b = prefix == "b-";
            defs.Add((prefix + "key", "Stable output key", b ? "output:B" : "output:A")); defs.Add((prefix + "label", "Display label (presentation only)", b ? "Output B" : "Output A"));
            foreach (var key in OutputFields)
            {
                var d = TransmissionGoalDraft.Definitions.Single(x => x.Key == key);
                var value = key == "output" ? b ? "0,60" : "90,0" : key == "output-teeth" ? b ? "30" : "20" : key == "target" ? b ? "1/3" : "1/2" :
                    key == "output-layers" ? "0" : key == "families" ? "SimpleIdler" : d.Default;
                defs.Add((prefix + key, d.Label, value));
            }
            defs.Add((prefix + "domains-override", "Override shared finite domains true/false", b ? "true" : "false"));
            foreach (var key in DomainFields) defs.Add((prefix + key, key, b && key == "sites" ? "0,20" : defs.Single(x => x.Key == key).Default));
        }
        return defs.AsReadOnly();
    }
    public Dictionary<string, string> Fields { get; } = Definitions.ToDictionary(d => d.Key, d => d.Default, StringComparer.Ordinal);
    private static int Int(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    public SharedDriverTransmissionGoal Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(v => v == null || v.Length > 8192)) throw new FormatException("Shared draft unknown field/text ceiling");
        string F(string key) => Fields[key].Trim();
        TransmissionGoal ParseChild(string prefix)
        {
            var d = new TransmissionGoalDraft();
            foreach (var key in new[] { "input", "input-teeth", "scale", "available-layers", "input-layer", "keepouts" }) d.Fields[key] = F(key);
            foreach (var key in OutputFields) d.Fields[key] = F(prefix + key);
            foreach (var key in DomainFields) d.Fields[key] = F((bool.Parse(F(prefix + "domains-override")) ? prefix : "") + key);
            d.Fields["budget"] = "0"; d.Fields["cap"] = "1"; return d.Parse();
        }
        var a = ParseChild("a-"); var b = ParseChild("b-");
        SharedDriverTransmissionOutput Output(string prefix, TransmissionGoal g) => new SharedDriverTransmissionOutput(F(prefix + "key"), g.Output, g.TargetTransfer,
            g.Legs[0], g.Slots[0], g.OutputLayers, g.LayerPolicy, g.AllowedFamilies, g.MaximumCompounds, g.MaximumTotalIdlers, slot1: g.Slots[1], leg1: g.Legs[1], leg2: g.Legs[2], displayLabel: F(prefix + "label"));
        // Reuse bounded existing geometric field parsers for global boxes/clearances/required regions as well.
        var env = new GearRoutingDraft(); env.Fields["bounds"] = F("global-bounds"); env.Fields["clearance"] = F("global-clearance"); env.Fields["keepout-clearance"] = F("global-keepout-clearance");
        env.Fields["required"] = F("global-required"); env.Fields["preferred"] = F("global-preferred"); var e = env.Parse();
        return new SharedDriverTransmissionGoal(a.Input, a.PitchRadiusTicksPerTooth, new[] { Output("a-", a), Output("b-", b) }, e.Bounds, a.AvailableLayers, a.KeepOuts,
            e.UnrelatedClearance, e.KeepOutClearance, Int(F("global-compounds")), Int(F("global-idlers")), e.PreferredRegion, e.RequiredRegions,
            Int(F("budget")), Int(F("join-budget")), Int(F("cap")), a.InputLayer, poolCountLimit: Int(F("pool-limit")), poolByteLimit: Int(F("pool-bytes")), pairDomainLimit: Int(F("pair-domain-limit")));
    }
    public static SharedDriverTransmissionDraft FromGoal(SharedDriverTransmissionGoal goal, IEnumerable<KeyValuePair<string, string>>? labels = null)
    {
        var n = SharedDriverTransmissionCompiler.Normalize(goal); if (!n.IsValid) throw new FormatException("Cannot present invalid shared-driver goal"); var g = n.Goal!; var draft = new SharedDriverTransmissionDraft(); var f = draft.Fields;
        string Num(int i) => i.ToString(CultureInfo.InvariantCulture);
        string Box(GearRouteBox b) => new GearRoutePoint(b.MinX, b.MinY) + ":" + new GearRoutePoint(b.MaxX, b.MaxY);
        var plan = SharedDriverTransmissionCompiler.Compile(g);
        for (int i = 0; i < 2; i++)
        {
            string prefix = i == 0 ? "a-" : "b-"; var child = TransmissionGoalDraft.FromGoal(plan.Branches[i].Plan.Normalized.Goal!); var o = g.Outputs[i];
            if (i == 0) { foreach (var key in new[] { "input", "input-teeth", "scale", "available-layers", "input-layer", "keepouts" }) f[key] = child.Fields[key]; foreach (var key in DomainFields) f[key] = child.Fields[key]; }
            foreach (var key in OutputFields) f[prefix + key] = child.Fields[key]; foreach (var key in DomainFields) f[prefix + key] = child.Fields[key];
            f[prefix + "key"] = o.Key; f[prefix + "label"] = labels?.FirstOrDefault(x => x.Key == o.Key).Value ?? goal.Outputs.Single(x => x.Key == o.Key).DisplayLabel;
            f[prefix + "domains-override"] = (i != 0 && DomainFields.Any(k => f[k] != child.Fields[k])).ToString().ToLowerInvariant();
        }
        f["global-bounds"] = Box(g.Bounds); f["global-clearance"] = g.UnrelatedClearance.ToString(CultureInfo.InvariantCulture); f["global-keepout-clearance"] = g.KeepOutClearance.ToString(CultureInfo.InvariantCulture);
        f["global-required"] = string.Join(";", g.RequiredRegions.Select(r => r.Id + ":" + Box(r.Bounds))); f["global-preferred"] = g.PreferredRegion == null ? "" : Box(g.PreferredRegion);
        f["global-compounds"] = Num(g.MaximumTotalCompounds); f["global-idlers"] = Num(g.MaximumTotalIdlers); f["budget"] = Num(g.TotalWorkBudget); f["join-budget"] = Num(g.ReservedCombinationWork); f["cap"] = Num(g.MaximumReturned);
        f["pool-limit"] = Num(g.PoolCountLimit); f["pool-bytes"] = Num(g.PoolByteLimit); f["pair-domain-limit"] = Num(g.PairDomainLimit); return draft;
    }
}
