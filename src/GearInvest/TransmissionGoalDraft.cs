using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest;

/// <summary>One editable goal schema for CLI and Unity. Overrides are explicit; defaults contain domains, not routes.</summary>
public sealed class TransmissionGoalDraft
{
    public static readonly string[] LegKeys = { "bounds", "domain", "grid", "sites", "idler-teeth", "depth", "clearance", "keepout-clearance", "required", "preferred" };
    public static readonly string[] SlotKeys = { "receiving-teeth", "driving-teeth", "domain", "sites", "grid", "required", "preferred" };
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> Definitions = Define();
    private static IReadOnlyList<(string Key, string Label, string Default)> Define()
    {
        var d = new List<(string Key, string Label, string Default)> {
            ("input", "Fixed input XY", "0,0"), ("input-teeth", "Input teeth", "10"), ("output", "Fixed output XY", "110,0"), ("output-teeth", "Output teeth", "20"),
            ("target", "Exact signed output/input", "-1/2"), ("scale", "Pitch-radius ticks/tooth", "1"), ("max-compounds", "Maximum compounds 0..2", "2"), ("total-idlers", "Maximum total idlers", "15"),
            ("layer-policy", "Output policy AllowedOutputLayers/FixedOutputLayer", "AllowedOutputLayers"), ("output-layers", "Allowed output layers ; separated", "0;1;2"),
            ("available-layers", "Available logical layers", "0;1;2"), ("max-layers", "Maximum layer count", "3"), ("input-layer", "Input logical layer", "0"),
            ("keepouts", "Keep-outs id:0+1+2/all:minXY:maxXY", ""), ("budget", "Total child work pool", "10000"), ("cap", "Global returned cap", "128"),
            ("families", "Advanced allowed families", "SimpleIdler;OneCompound;TwoCompound") };
        foreach (var key in LegKeys)
        {
            var old = GearRoutingDraft.Definitions.Single(x => x.Key == key);
            d.Add((key, "Common: " + old.Label, key == "bounds" ? "-100,-80:300,80" : key == "domain" ? "sites" : key == "sites" ? "20,0;40,0;60,0;80,0" : key == "depth" ? "0:5" : old.Default));
        }
        foreach (var key in SlotKeys)
        {
            var old = TwoCompoundRoutingDraft.Definitions.Single(x => x.Key == "compound0-" + key);
            d.Add(("compound-" + key, "Common compound: " + key, key == "receiving-teeth" || key == "driving-teeth" ? "10;30" : key == "sites" ? "40,0;60,0" : old.Default));
        }
        for (int s = 0; s < 2; s++)
        {
            string p = "slot" + s + "-"; d.Add((p + "override", "Advanced slot " + s + " override true/false", "false"));
            foreach (var key in SlotKeys) d.Add((p + key, "Slot " + s + ": " + key, d.Single(x => x.Key == "compound-" + key).Default));
        }
        for (int l = 0; l < 3; l++)
        {
            string p = "leg" + l + "-"; d.Add((p + "override", "Advanced leg " + l + " override true/false", "false"));
            foreach (var key in LegKeys) d.Add((p + key, "Leg " + l + ": " + key, d.Single(x => x.Key == key).Default));
        }
        return d.AsReadOnly();
    }
    public Dictionary<string, string> Fields { get; } = Definitions.ToDictionary(x => x.Key, x => x.Default, StringComparer.Ordinal);
    private static int Int(string v) => int.Parse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static string[] Parts(string v) => v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    public TransmissionGoal Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(x => x == null || x.Length > 8192)) throw new FormatException("Goal input text limit or unknown field");
        string F(string key) => Fields[key].Trim();
        // Reuse the established bounded field parsers, not their solver or profile normalization.
        var raw = new TwoCompoundRoutingDraft();
        foreach (var k in new[] { "input", "input-teeth", "output", "output-teeth", "target", "scale", "total-idlers", "keepouts", "budget", "cap" }) raw.Fields[k] = F(k);
        for (int s = 0; s < 2; s++) foreach (var k in SlotKeys) raw.Fields["compound" + s + "-" + k] = F((bool.Parse(F("slot" + s + "-override")) ? "slot" + s + "-" : "compound-") + k);
        for (int l = 0; l < 3; l++) foreach (var k in LegKeys) raw.Fields["leg" + l + "-" + k] = F((bool.Parse(F("leg" + l + "-override")) ? "leg" + l + "-" : "") + k);
        var r = raw.Parse();
        return new TransmissionGoal(r.Input, r.Output, r.TargetTransfer, r.PitchRadiusTicksPerTooth, r.Legs[0], r.Compounds[0],
            Int(F("max-compounds")), r.MaximumTotalIdlers, Parts(F("available-layers")).Select(Int), Parts(F("output-layers")).Select(Int),
            Enum.Parse<TransmissionOutputLayerPolicy>(F("layer-policy")), Int(F("max-layers")), Parts(F("families")).Select(Enum.Parse<TransmissionFamily>), r.KeepOuts,
            slot1: r.Compounds[1], leg1: r.Legs[1], leg2: r.Legs[2], workBudget: r.WorkBudget, maximumReturned: r.MaximumReturned, inputLayer: Int(F("input-layer")));
    }
    public static TransmissionGoalDraft FromGoal(TransmissionGoal raw)
    {
        var n = Engine.TransmissionGoalCompiler.Normalize(raw); if (!n.IsValid) throw new FormatException("Cannot present invalid goal"); var g = n.Goal!; var d = new TransmissionGoalDraft(); var f = d.Fields;
        string Num(int v) => v.ToString(CultureInfo.InvariantCulture);
        string Box(GearRouteBox b) => new GearRoutePoint(b.MinX, b.MinY) + ":" + new GearRoutePoint(b.MaxX, b.MaxY);
        f["input"] = g.Input.Position.ToString(); f["input-teeth"] = Num(g.Input.Teeth); f["output"] = g.Output.Position.ToString(); f["output-teeth"] = Num(g.Output.Teeth);
        f["target"] = g.TargetTransfer.ToString(); f["scale"] = g.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture); f["max-compounds"] = Num(g.MaximumCompounds); f["total-idlers"] = Num(g.MaximumTotalIdlers);
        f["layer-policy"] = g.LayerPolicy.ToString(); f["output-layers"] = string.Join(";", g.OutputLayers); f["available-layers"] = string.Join(";", g.AvailableLayers); f["max-layers"] = Num(g.MaximumLayerCount); f["input-layer"] = Num(g.InputLayer);
        f["budget"] = Num(g.WorkBudget); f["cap"] = Num(g.MaximumReturned); f["families"] = string.Join(";", g.AllowedFamilies);
        f["keepouts"] = string.Join(";", g.KeepOuts.Select(k => k.Id + ":" + string.Join("+", Enumerable.Range(0, 3).Where(k.AppliesTo)) + ":" + Box(k.Bounds)));
        for (int s = 0; s < 2; s++)
        {
            var c = g.Slots[s]; string p = "slot" + s + "-";
            f[p + "receiving-teeth"] = string.Join(";", c.ReceivingTeeth); f[p + "driving-teeth"] = string.Join(";", c.DrivingTeeth); f[p + "domain"] = "sites"; f[p + "sites"] = string.Join(";", c.Sites);
            f[p + "required"] = c.RequiredRegion == null ? "" : Box(c.RequiredRegion); f[p + "preferred"] = c.PreferredRegion == null ? "" : Box(c.PreferredRegion);
            f[p + "override"] = (s != 0 && c.Canonical != g.Slots[0].Canonical).ToString().ToLowerInvariant();
            if (s == 0) foreach (var k in SlotKeys) f["compound-" + k] = f[p + k];
        }
        for (int l = 0; l < 3; l++)
        {
            var c = g.Legs[l]; string p = "leg" + l + "-";
            var old = GearRoutingDraft.FromRequest(new AnchoredGearRoutingRequest(g.Input, g.Output, g.TargetTransfer, g.PitchRadiusTicksPerTooth, c.Bounds, c.Sites, idlerTeeth: c.IdlerTeeth,
                minIdlers: c.MinIdlers, maxIdlers: c.MaxIdlers, unrelatedClearance: c.UnrelatedClearance, keepOutClearance: c.KeepOutClearance, requiredRegions: c.RequiredRegions, preferredRegion: c.PreferredRegion));
            foreach (var k in LegKeys) f[p + k] = old.Fields[k]; f[p + "sites"] = string.Join(";", c.Sites);
            f[p + "override"] = (l != 0 && c.Canonical != g.Legs[0].Canonical).ToString().ToLowerInvariant();
            if (l == 0) foreach (var k in LegKeys) f[k] = f[p + k];
        }
        return d;
    }
}
