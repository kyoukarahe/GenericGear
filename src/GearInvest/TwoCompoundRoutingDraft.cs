using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest;

/// <summary>Product field schema shared by native authoring and CLI. Defaults are editable domains, never precomputed routes.</summary>
public sealed class TwoCompoundRoutingDraft
{
    private static readonly string[] LegKeys = {"bounds","domain","grid","sites","idler-teeth","depth","clearance","keepout-clearance","required","preferred"};
    public static readonly IReadOnlyList<(string Key,string Label,string Default)> Definitions = Define();
    private static IReadOnlyList<(string Key,string Label,string Default)> Define()
    {
        var d = new List<(string Key,string Label,string Default)> {
            ("input","Input XY / layer 0","0,0"),("input-teeth","Input teeth","10"),("output","Output XY / layer 2","150,0"),("output-teeth","Output teeth","20"),
            ("target","Exact signed output/input","-1/18"),("scale","Pitch-radius ticks/tooth","1"),("total-idlers","Total idlers (0..15)","15"),
            ("keepouts","Keep-outs id:0+1+2/all:minXY:maxXY",""),("budget","One global work budget","100000"),("cap","Returned complete mechanisms","128"),
            ("profile","Bounded serial profile",TwoCompoundRoutingContract.Profile)
        };
        for (int s = 0; s < 2; s++)
        {
            string p = "compound" + s + "-", label = "C" + s + ": ";
            d.Add((p + "receiving-teeth",label + "receiving tooth options","30")); d.Add((p + "driving-teeth",label + "driving tooth options","10"));
            d.Add((p + "domain",label + "domain grid/sites","sites")); d.Add((p + "sites",label + "sites x,y;x,y",s == 0 ? "40,0" : "120,0"));
            d.Add((p + "grid",label + "origin:step:minXY:maxXY","0,0:20,20:0,0:120,0")); d.Add((p + "required",label + "Required rectangle","")); d.Add((p + "preferred",label + "Preferred rectangle",""));
        }
        for (int leg = 0; leg < 3; leg++) foreach (var key in LegKeys)
        {
            var original = GearRoutingDraft.Definitions.Single(x => x.Key == key);
            string value = key == "bounds" ? "-100,-80:300,80" : key == "domain" ? "sites" : key == "sites" ? leg == 1 ? "40,20;60,0;60,20;80,0;80,20" : "" : key == "depth" ? leg == 1 ? "0:5" : "0:0" : original.Default;
            d.Add(("leg" + leg + "-" + key,"L" + leg + ": " + original.Label,value));
        }
        return d.AsReadOnly();
    }
    public Dictionary<string,string> Fields { get; } = Definitions.ToDictionary(x => x.Key,x => x.Default,StringComparer.Ordinal);
    private static string[] Parts(string s) => s.Split(new[] {';'},StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
    private static int Int(string s) => int.Parse(s,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture);
    private static string Box(GearRouteBox b) => new GearRoutePoint(b.MinX,b.MinY) + ":" + new GearRoutePoint(b.MaxX,b.MaxY);
    private static GearRouteGrid Grid(string s) { var p = s.Split(':'); if (p.Length != 4) throw new FormatException("Grid requires origin:step:minXY:maxXY"); return new GearRouteGrid(GearRoutingDraft.Point(p[0]),GearRoutingDraft.Point(p[1]),GearRoutingDraft.Box(p[2] + ":" + p[3])); }
    public AnchoredTwoCompoundGearRoutingRequest Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(x => x == null || x.Length > 8192)) throw new FormatException("Two-compound input text limit or unknown field");
        string F(string key) => Fields[key].Trim(); var legs = new List<CompoundRoutingLeg>(); var slots = new List<CompoundRoutingSlot>();
        for (int leg = 0; leg < 3; leg++)
        {
            var d = new GearRoutingDraft(); foreach (var key in LegKeys) d.Fields[key] = F("leg" + leg + "-" + key);
            foreach (var key in new[] {"input","input-teeth","output","output-teeth","target","scale"}) d.Fields[key] = F(key);
            var l = d.Parse(); legs.Add(new CompoundRoutingLeg(l.Bounds,l.HasExplicitSites ? l.Sites : null,l.Grid,l.IdlerTeeth,l.MinIdlers,l.MaxIdlerCount,l.UnrelatedClearance,l.KeepOutClearance,l.RequiredRegions,l.PreferredRegion));
        }
        for (int s = 0; s < 2; s++)
        {
            string p = "compound" + s + "-"; bool grid = F(p + "domain") == "grid"; if (!grid && F(p + "domain") != "sites") throw new FormatException("Compound domain must be grid or sites");
            slots.Add(new CompoundRoutingSlot(Parts(F(p + "receiving-teeth")).Select(Int),Parts(F(p + "driving-teeth")).Select(Int),grid ? null : Parts(F(p + "sites")).Select(GearRoutingDraft.Point),grid ? Grid(F(p + "grid")) : null,
                F(p + "required").Length == 0 ? null : GearRoutingDraft.Box(F(p + "required")),F(p + "preferred").Length == 0 ? null : GearRoutingDraft.Box(F(p + "preferred"))));
        }
        var obstacles = Parts(F("keepouts")).Select(x => {
            var p = x.Split(':'); if (p.Length != 4) throw new FormatException("Keep-out requires id:0+1+2/all:minXY:maxXY");
            var values = p[1] == "all" ? new[] {0,1,2} : p[1].Split('+').Select(Int).ToArray(); if (values.Length == 0 || values.Length > 3 || values.Any(v => v < 0 || v > 2) || values.Distinct().Count() != values.Length) throw new FormatException("Explicit nonempty layer subset required");
            return new ThreeLayerGearRouteKeepOut(p[0],GearRoutingDraft.Box(p[2] + ":" + p[3]),(ThreeRouteLayers)values.Aggregate(0,(mask,l) => mask | (1 << l)));
        });
        return new AnchoredTwoCompoundGearRoutingRequest(new GearRouteAnchor(GearRoutingDraft.Point(F("input")),Int(F("input-teeth"))),new GearRouteAnchor(GearRoutingDraft.Point(F("output")),Int(F("output-teeth"))),
            Rational.Parse(F("target")),Int(F("scale")),slots,legs,Int(F("total-idlers")),obstacles,Int(F("budget")),Int(F("cap")),profile:F("profile"));
    }
    public static TwoCompoundRoutingDraft FromRequest(AnchoredTwoCompoundGearRoutingRequest raw)
    {
        var n = Engine.AnchoredTwoCompoundGearRouter.Normalize(raw); if (!n.IsValid) throw new FormatException("Cannot present invalid two-compound request"); var r = n.Request!; var d = new TwoCompoundRoutingDraft(); var f = d.Fields;
        f["input"] = r.Input.Position.ToString(); f["input-teeth"] = GearRoutingContract.Number(r.Input.Teeth); f["output"] = r.Output.Position.ToString(); f["output-teeth"] = GearRoutingContract.Number(r.Output.Teeth);
        f["target"] = r.TargetTransfer.ToString(); f["scale"] = GearRoutingContract.Number(r.PitchRadiusTicksPerTooth); f["total-idlers"] = GearRoutingContract.Number(r.MaximumTotalIdlers); f["budget"] = GearRoutingContract.Number(r.WorkBudget); f["cap"] = GearRoutingContract.Number(r.MaximumReturned); f["profile"] = r.Profile;
        f["keepouts"] = string.Join(";",r.KeepOuts.Select(k => k.Id + ":" + string.Join("+",Enumerable.Range(0,3).Where(k.AppliesTo)) + ":" + Box(k.Bounds)));
        for (int s = 0; s < 2; s++)
        {
            var c = r.Compounds[s]; string p = "compound" + s + "-";
            f[p + "receiving-teeth"] = string.Join(";",c.ReceivingTeeth.Select(t => GearRoutingContract.Number(t))); f[p + "driving-teeth"] = string.Join(";",c.DrivingTeeth.Select(t => GearRoutingContract.Number(t)));
            f[p + "domain"] = "sites"; f[p + "sites"] = string.Join(";",c.Sites); f[p + "required"] = c.RequiredRegion == null ? "" : Box(c.RequiredRegion); f[p + "preferred"] = c.PreferredRegion == null ? "" : Box(c.PreferredRegion);
        }
        for (int leg = 0; leg < 3; leg++)
        {
            var l = r.Legs[leg]; var old = GearRoutingDraft.FromRequest(new AnchoredGearRoutingRequest(r.Input,r.Output,r.TargetTransfer,r.PitchRadiusTicksPerTooth,l.Bounds,l.Sites,idlerTeeth:l.IdlerTeeth,
                minIdlers:l.MinIdlers,maxIdlers:l.MaxIdlers,unrelatedClearance:l.UnrelatedClearance,keepOutClearance:l.KeepOutClearance,requiredRegions:l.RequiredRegions,preferredRegion:l.PreferredRegion));
            foreach (var key in LegKeys) f["leg" + leg + "-" + key] = old.Fields[key];
            f["leg" + leg + "-sites"] = string.Join(";",l.Sites);
        }
        return d;
    }
}
