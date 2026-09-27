using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest;

/// <summary>Typed continuous-domain text input shared by CLI/Unity; no source model, event cursor or indexed state.</summary>
public sealed class GearRoutingDraft
{
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> Definitions = Array.AsReadOnly(new[] {
        ("input", "Input anchor x,y", "0,0"), ("input-teeth", "Input teeth", "10"), ("output", "Output anchor x,y", "90,0"), ("output-teeth", "Output teeth", "20"),
        ("scale", "Pitch radius ticks / tooth", "1"), ("target", "Exact signed output / input", "1/2"), ("idler-teeth", "Allowed idler teeth", "10"), ("depth", "Min:max idlers (0..5)", "0:5"),
        ("bounds", "Envelope bounds minXY:maxXY", "-20,-40:110,40"), ("domain", "Domain mode grid or sites", "grid"), ("grid", "Origin:step:minXY:maxXY", "0,0:10,10:-20,-40:110,40"),
        ("sites", "Explicit sites x,y;x,y", ""), ("keepouts", "Keep-outs id:minXY:maxXY", ""), ("required", "Required id:minXY:maxXY", ""), ("preferred", "Preferred minXY:maxXY", ""),
        ("clearance", "Non-mesh clearance", "0"), ("keepout-clearance", "Keep-out clearance", "0"), ("budget", "Global expansion budget", "1000000"), ("cap", "Maximum returned candidates", "128"), ("profile", "Bounded profile", GearRoutingContract.Profile)
    });
    public Dictionary<string, string> Fields { get; } = Definitions.ToDictionary(x => x.Key, x => x.Default, StringComparer.Ordinal);
    private static string[] Split(string value, char separator) => value.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToArray();
    private static BigInteger Integer(string s) { if (s.Trim().Length > 16) throw new FormatException("Integer text limit"); return BigInteger.Parse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); }
    public static GearRoutePoint Point(string value) { var p = value.Split(','); if (p.Length != 2) throw new FormatException("Expected integer x,y"); return new GearRoutePoint(Integer(p[0]), Integer(p[1])); }
    public static GearRouteBox Box(string value) { var p = value.Split(':'); if (p.Length != 2) throw new FormatException("Expected minX,minY:maxX,maxY"); var a = Point(p[0]); var b = Point(p[1]); return new GearRouteBox(a.X, a.Y, b.X, b.Y); }
    private static string BoxText(GearRouteBox b) => new GearRoutePoint(b.MinX, b.MinY) + ":" + new GearRoutePoint(b.MaxX, b.MaxY);
    private static GearRouteRegion Region(string value) { var p = value.Split(':'); if (p.Length != 3) throw new FormatException("Expected id:minX,minY:maxX,maxY"); return new GearRouteRegion(p[0], Box(p[1] + ":" + p[2])); }
    public AnchoredGearRoutingRequest Parse()
    {
        if (Fields.Count != Definitions.Count || Fields.Keys.Any(k => !Definitions.Any(d => d.Key == k)) || Fields.Values.Any(s => s == null || s.Length > 8192)) throw new FormatException("Routing input text limit or unknown field");
        string F(string k) => Fields[k].Trim(); int I(string k) => int.Parse(F(k), CultureInfo.InvariantCulture);
        bool gridMode = F("domain") == "grid"; if (!gridMode && F("domain") != "sites") throw new FormatException("Domain must be grid or sites");
        GearRouteGrid? grid = null;
        if (gridMode) { var p = F("grid").Split(':'); if (p.Length != 4) throw new FormatException("Grid requires origin:step:minXY:maxXY"); grid = new GearRouteGrid(Point(p[0]), Point(p[1]), Box(p[2] + ":" + p[3])); }
        var depth = F("depth").Split(':'); if (depth.Length != 2) throw new FormatException("Depth requires min:max");
        return new AnchoredGearRoutingRequest(new GearRouteAnchor(Point(F("input")), I("input-teeth")), new GearRouteAnchor(Point(F("output")), I("output-teeth")), Rational.Parse(F("target")), Integer(F("scale")), Box(F("bounds")),
            sites: gridMode ? null : Split(F("sites"), ';').Select(Point), grid: grid, idlerTeeth: Split(F("idler-teeth"), ';').Select(x => int.Parse(x, CultureInfo.InvariantCulture)),
            minIdlers: int.Parse(depth[0], CultureInfo.InvariantCulture), maxIdlers: int.Parse(depth[1], CultureInfo.InvariantCulture), unrelatedClearance: Integer(F("clearance")), keepOutClearance: Integer(F("keepout-clearance")),
            keepOuts: Split(F("keepouts"), ';').Select(Region), requiredRegions: Split(F("required"), ';').Select(Region), preferredRegion: F("preferred").Length == 0 ? null : Box(F("preferred")),
            expansionBudget: I("budget"), maximumReturned: I("cap"), profile: F("profile"));
    }
    public static GearRoutingDraft FromRequest(AnchoredGearRoutingRequest raw)
    {
        var n = Engine.AnchoredGearRouter.Normalize(raw); if (!n.IsValid) throw new FormatException("Cannot present invalid normalized request"); var r = n.Request!; var d = new GearRoutingDraft(); var f = d.Fields;
        f["input"] = r.Input.Position.ToString(); f["input-teeth"] = GearRoutingContract.Number(r.Input.Teeth); f["output"] = r.Output.Position.ToString(); f["output-teeth"] = GearRoutingContract.Number(r.Output.Teeth);
        f["target"] = r.TargetTransfer.ToString(); f["scale"] = GearRoutingContract.Number(r.PitchRadiusTicksPerTooth); f["idler-teeth"] = string.Join(";", r.IdlerTeeth.Select(t => GearRoutingContract.Number(t)));
        f["depth"] = GearRoutingContract.Number(r.MinIdlers) + ":" + GearRoutingContract.Number(r.MaxIdlerCount); f["bounds"] = BoxText(r.Bounds); f["domain"] = "sites"; f["sites"] = string.Join(";", r.Sites);
        f["keepouts"] = string.Join(";", r.KeepOuts.Select(k => k.Id + ":" + BoxText(k.Bounds))); f["required"] = string.Join(";", r.RequiredRegions.Select(k => k.Id + ":" + BoxText(k.Bounds)));
        f["preferred"] = r.PreferredRegion == null ? "" : BoxText(r.PreferredRegion); f["clearance"] = GearRoutingContract.Number(r.UnrelatedClearance); f["keepout-clearance"] = GearRoutingContract.Number(r.KeepOutClearance);
        f["budget"] = GearRoutingContract.Number(r.ExpansionBudget); f["cap"] = GearRoutingContract.Number(r.MaximumReturned); f["profile"] = r.Profile; return d;
    }
}
