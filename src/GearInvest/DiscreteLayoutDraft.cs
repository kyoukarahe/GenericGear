using System;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest;

/// <summary>Application input text shared by CLI and Unity. All normalization/search remains in the SDK.
/// ExplicitOffsets selects explicit mode; otherwise Directions with Range or Distances selects grid mode.</summary>
public sealed class DiscreteLayoutDraft
{
    public string Anchor = "0,0,0", FrameQuarter = "0", Directions = "0;1;2;3", Range = "24:30:6", Distances = "", ExplicitOffsets = "";
    public string PrimaryRadii = "6;7", SecondaryRadii = "6", PrimaryProbes = "0,3/4,40,40", SecondaryProbes = "0,3/4,40,40";
    public string KeepOuts = "", RequiredRegion = "", PreferredOffset = "", MinimumClearance = "1/2", ExpansionBudget = "256", CandidateCap = "32";
    public string Profile = DiscreteGeometryContract.Profile, Backend = DiscreteLayoutContract.Backend, Ranking = DiscreteLayoutContract.Ranking;
    private static string[] Split(string value, char separator) => value.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToArray();
    public static GeometryPoint Point(string value) { var p = value.Split(','); if (p.Length != 3) throw new FormatException("Expected exact x,y,z"); return new GeometryPoint(Rational.Parse(p[0].Trim()), Rational.Parse(p[1].Trim()), Rational.Parse(p[2].Trim())); }
    public static string PointText(GeometryPoint p) => p.X + "," + p.Y + "," + p.Z;
    public static GeometryBox Box(string value) { var p = value.Split(':'); if (p.Length != 2) throw new FormatException("Expected minX,minY,minZ:maxX,maxY,maxZ"); return new GeometryBox(Point(p[0]), Point(p[1])); }
    private static LayoutProbeConfiguration Probe(string value) { var p = value.Split(','); if (p.Length != 4) throw new FormatException("Probe requires quarter,radialFraction,travel,retract"); return new LayoutProbeConfiguration(int.Parse(p[0], CultureInfo.InvariantCulture), Rational.Parse(p[1]), Rational.Parse(p[2]), Rational.Parse(p[3])); }
    private static string ProbeText(LayoutProbeConfiguration p) => p.BearingQuarter.ToString(CultureInfo.InvariantCulture) + "," + p.RadialFraction + "," + p.Travel + "," + p.Retract;
    public DiscreteLayoutRequest Parse(DiscreteLayoutSource source)
    {
        if (new[] { Anchor, FrameQuarter, Directions, Range, Distances, ExplicitOffsets, PrimaryRadii, SecondaryRadii, PrimaryProbes, SecondaryProbes, KeepOuts, RequiredRegion, PreferredOffset, MinimumClearance, ExpansionBudget, CandidateCap }.Any(s => s.Length > 8192)) throw new FormatException("DomainTooLarge: input text limit");
        bool explicitMode = ExplicitOffsets.Trim().Length > 0; LayoutDistanceRange? range = null;
        if (!explicitMode && Range.Trim().Length > 0) { var p = Range.Split(':'); if (p.Length != 3) throw new FormatException("range: expected min:max:step"); range = new LayoutDistanceRange(Rational.Parse(p[0]), Rational.Parse(p[1]), Rational.Parse(p[2])); }
        var keepouts = Split(KeepOuts, ';').Select(k => { var p = k.Split(':'); if (p.Length != 3) throw new FormatException("keepOuts: expected id:minX,minY,minZ:maxX,maxY,maxZ"); return new GeometryKeepOut(p[0], new GeometryBox(Point(p[1]), Point(p[2]))); });
        return new DiscreteLayoutRequest(source, Point(Anchor), directions: explicitMode ? null : Split(Directions, ';').Select(s => int.Parse(s, CultureInfo.InvariantCulture)),
            distances: explicitMode ? null : Split(Distances, ';').Select(Rational.Parse), range: range, offsets: explicitMode ? Split(ExplicitOffsets, ';').Select(Point) : null,
            primaryRadii: Split(PrimaryRadii, ';').Select(Rational.Parse), secondaryRadii: Split(SecondaryRadii, ';').Select(Rational.Parse),
            primaryProbes: Split(PrimaryProbes, ';').Select(Probe), secondaryProbes: Split(SecondaryProbes, ';').Select(Probe),
            frameQuarter: int.Parse(FrameQuarter, CultureInfo.InvariantCulture), keepOuts: keepouts, requiredRegion: RequiredRegion.Trim().Length == 0 ? null : Box(RequiredRegion),
            preferredOffset: PreferredOffset.Trim().Length == 0 ? null : Point(PreferredOffset), minimumClearance: Rational.Parse(MinimumClearance),
            expansionBudget: int.Parse(ExpansionBudget, CultureInfo.InvariantCulture), candidateCap: int.Parse(CandidateCap, CultureInfo.InvariantCulture), profile: Profile, backend: Backend, ranking: Ranking);
    }
    public static DiscreteLayoutDraft FromRequest(DiscreteLayoutRequest r) => new DiscreteLayoutDraft
    {
        Anchor = PointText(r.Anchor), FrameQuarter = r.FrameQuarter.ToString(CultureInfo.InvariantCulture), Directions = "", Range = "", Distances = "", ExplicitOffsets = string.Join(";", r.Offsets.Select(PointText)),
        PrimaryRadii = string.Join(";", r.PrimaryRadii), SecondaryRadii = string.Join(";", r.SecondaryRadii), PrimaryProbes = string.Join(";", r.PrimaryProbes.Select(ProbeText)), SecondaryProbes = string.Join(";", r.SecondaryProbes.Select(ProbeText)),
        KeepOuts = string.Join(";", r.KeepOuts.Select(k => k.Id + ":" + PointText(k.Bounds.Min) + ":" + PointText(k.Bounds.Max))),
        RequiredRegion = r.RequiredRegion == null ? "" : PointText(r.RequiredRegion.Min) + ":" + PointText(r.RequiredRegion.Max), PreferredOffset = r.PreferredOffset.HasValue ? PointText(r.PreferredOffset.Value) : "",
        MinimumClearance = r.MinimumClearance.ToString(), ExpansionBudget = r.ExpansionBudget.ToString(CultureInfo.InvariantCulture), CandidateCap = r.CandidateCap.ToString(CultureInfo.InvariantCulture), Profile = r.Profile, Backend = r.Backend, Ranking = r.Ranking
    };
}
