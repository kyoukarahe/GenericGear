using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest;

/// <summary>Product authoring text schema, shared by CLI and Unity. No reviewed-family lookup or stored path answers.</summary>
public sealed class CompoundRoutingDraft
{
    private static readonly string[] LegKeys = { "bounds", "domain", "grid", "sites", "idler-teeth", "depth", "clearance", "keepout-clearance", "required", "preferred" };
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> Definitions = Define();
    private static IReadOnlyList<(string Key, string Label, string Default)> Define()
    {
        var list = new List<(string Key, string Label, string Default)> {
            ("input", "Input XY (layer 0)", "0,0"), ("input-teeth", "Input teeth", "10"), ("output", "Output XY (layer 1)", "190,0"), ("output-teeth", "Output teeth", "20"),
            ("target", "Exact signed output/input", "1/6"), ("scale", "Pitch-radius ticks/tooth", "1"), ("receiving-teeth", "Compound A tooth options", "30"), ("driving-teeth", "Compound B tooth options", "10"),
            ("compound-domain", "Compound domain: grid/sites", "sites"), ("compound-sites", "Compound sites x,y;x,y", "100,-20;100,0;100,20"),
            ("compound-grid", "Compound origin:step:minXY:maxXY", "0,0:20,20:100,-20:100,20"), ("compound-required", "Required compound rectangle", ""), ("compound-preferred", "Preferred compound rectangle", ""),
            ("total-idlers", "Maximum total idlers (0..10)", "10"), ("keepouts", "Keep-outs id:0/1/both:minXY:maxXY", ""),
            ("budget", "One global work budget", "1000000"), ("cap", "Returned complete mechanisms", "128"), ("profile", "Bounded topology profile", CompoundRoutingContract.Profile)
        };
        foreach (var side in new[] { "left", "right" }) foreach (var key in LegKeys)
        {
            var old = GearRoutingDraft.Definitions.Single(x => x.Key == key);
            string value = key == "bounds" ? "-20,-80:220,80" : key == "grid" ? "0,0:20,20:0,-40:200,40" : old.Default;
            list.Add((side + "-" + key, (side == "left" ? "Input leg: " : "Output leg: ") + old.Label, value));
        }
        return list.AsReadOnly();
    }
    public Dictionary<string, string> Fields { get; } = Definitions.ToDictionary(x => x.Key, x => x.Default, StringComparer.Ordinal);
    private static string[] Parts(string value) => value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
    private static int Int(string value) => int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static GearRouteGrid Grid(string value) { var p = value.Split(':'); if (p.Length != 4) throw new FormatException("Grid requires origin:step:minXY:maxXY"); return new GearRouteGrid(GearRoutingDraft.Point(p[0]), GearRoutingDraft.Point(p[1]), GearRoutingDraft.Box(p[2] + ":" + p[3])); }
    private static string Box(GearRouteBox b) => new GearRoutePoint(b.MinX, b.MinY) + ":" + new GearRoutePoint(b.MaxX, b.MaxY);
    public AnchoredCompoundGearRoutingRequest Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(x => x == null || x.Length > 8192)) throw new FormatException("Compound input text limit or unknown field");
        string F(string key) => Fields[key].Trim();
        CompoundRoutingLeg Leg(string side)
        {
            var draft = new GearRoutingDraft(); foreach (var key in LegKeys) draft.Fields[key] = F(side + "-" + key);
            foreach (var key in new[] { "input", "input-teeth", "output", "output-teeth", "target", "scale" }) draft.Fields[key] = F(key);
            var r = draft.Parse(); return new CompoundRoutingLeg(r.Bounds, r.HasExplicitSites ? r.Sites : null, r.Grid, r.IdlerTeeth, r.MinIdlers, r.MaxIdlerCount, r.UnrelatedClearance, r.KeepOutClearance, r.RequiredRegions, r.PreferredRegion);
        }
        bool grid = F("compound-domain") == "grid"; if (!grid && F("compound-domain") != "sites") throw new FormatException("Compound domain must be grid or sites");
        var obstacles = Parts(F("keepouts")).Select(x => {
            var p = x.Split(':'); if (p.Length != 4) throw new FormatException("Keep-out requires id:0/1/both:minXY:maxXY");
            var layer = p[1] == "0" ? GearRouteLayers.Layer0 : p[1] == "1" ? GearRouteLayers.Layer1 : p[1] == "both" ? GearRouteLayers.Both : throw new FormatException("Keep-out scope must be 0, 1 or both");
            return new LayeredGearRouteKeepOut(p[0], GearRoutingDraft.Box(p[2] + ":" + p[3]), layer);
        });
        return new AnchoredCompoundGearRoutingRequest(new GearRouteAnchor(GearRoutingDraft.Point(F("input")), Int(F("input-teeth"))), new GearRouteAnchor(GearRoutingDraft.Point(F("output")), Int(F("output-teeth"))),
            Rational.Parse(F("target")), Int(F("scale")), Parts(F("receiving-teeth")).Select(Int), Parts(F("driving-teeth")).Select(Int), Leg("left"), Leg("right"),
            compoundSites: grid ? null : Parts(F("compound-sites")).Select(GearRoutingDraft.Point), compoundGrid: grid ? Grid(F("compound-grid")) : null,
            maximumTotalIdlers: Int(F("total-idlers")), keepOuts: obstacles,
            requiredCompoundRegion: F("compound-required").Length == 0 ? null : GearRoutingDraft.Box(F("compound-required")), preferredCompoundRegion: F("compound-preferred").Length == 0 ? null : GearRoutingDraft.Box(F("compound-preferred")),
            workBudget: Int(F("budget")), maximumReturned: Int(F("cap")), profile: F("profile"));
    }
    public static CompoundRoutingDraft FromRequest(AnchoredCompoundGearRoutingRequest raw)
    {
        var n = Engine.AnchoredCompoundGearRouter.Normalize(raw); if (!n.IsValid) throw new FormatException("Cannot present invalid compound request"); var r = n.Request!; var d = new CompoundRoutingDraft(); var f = d.Fields;
        f["input"] = r.Input.Position.ToString(); f["input-teeth"] = GearRoutingContract.Number(r.Input.Teeth); f["output"] = r.Output.Position.ToString(); f["output-teeth"] = GearRoutingContract.Number(r.Output.Teeth);
        f["target"] = r.TargetTransfer.ToString(); f["scale"] = GearRoutingContract.Number(r.PitchRadiusTicksPerTooth); f["receiving-teeth"] = string.Join(";", r.ReceivingTeeth.Select(x => GearRoutingContract.Number(x))); f["driving-teeth"] = string.Join(";", r.DrivingTeeth.Select(x => GearRoutingContract.Number(x)));
        f["compound-domain"] = "sites"; f["compound-sites"] = string.Join(";", r.CompoundSites); f["compound-required"] = r.RequiredCompoundRegion == null ? "" : Box(r.RequiredCompoundRegion); f["compound-preferred"] = r.PreferredCompoundRegion == null ? "" : Box(r.PreferredCompoundRegion);
        f["total-idlers"] = GearRoutingContract.Number(r.MaximumTotalIdlers); f["budget"] = GearRoutingContract.Number(r.WorkBudget); f["cap"] = GearRoutingContract.Number(r.MaximumReturned); f["profile"] = r.Profile;
        f["keepouts"] = string.Join(";", r.KeepOuts.Select(k => k.Id + ":" + (k.Layers == GearRouteLayers.Both ? "both" : k.Layers == GearRouteLayers.Layer0 ? "0" : "1") + ":" + Box(k.Bounds)));
        foreach (var side in new[] { "left", "right" })
        {
            var l = side == "left" ? r.InputLeg : r.OutputLeg;
            var old = GearRoutingDraft.FromRequest(new AnchoredGearRoutingRequest(r.Input, r.Output, r.TargetTransfer, r.PitchRadiusTicksPerTooth, l.Bounds, l.Sites, idlerTeeth: l.IdlerTeeth,
                minIdlers: l.MinIdlers, maxIdlers: l.MaxIdlers, unrelatedClearance: l.UnrelatedClearance, keepOutClearance: l.KeepOutClearance, requiredRegions: l.RequiredRegions, preferredRegion: l.PreferredRegion));
            foreach (var key in LegKeys) f[side + "-" + key] = old.Fields[key];
        }
        return d;
    }
}
