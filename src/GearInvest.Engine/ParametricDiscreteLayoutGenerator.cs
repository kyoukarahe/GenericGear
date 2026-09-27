using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class ParametricDiscreteLayoutGenerator
{
    private sealed class InputFailure : Exception
    {
        public InputFailure(string code, string field, string detail, bool unsupported = false) : base(detail) { Code = code; Field = field; Unsupported = unsupported; }
        public string Code { get; }
        public string Field { get; }
        public bool Unsupported { get; }
    }
    private static void Need(bool value, string field, string detail, string code = "InvalidInput", bool unsupported = false)
    { if (!value) throw new InputFailure(code, field, detail, unsupported); }
    private static bool Bounded(Rational r) => r.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture).Length <= 64 && r.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture).Length <= 64;
    private static bool PointBounded(GeometryPoint p) => new[] { p.X, p.Y, p.Z }.All(v => Bounded(v) && ExactGeometry.Abs(v) <= new Rational(10000));

    public DiscreteLayoutNormalization Normalize(DiscreteLayoutRequest input)
    {
        BigInteger domain = 0;
        try
        {
            if (input == null || input.Source == null) throw new InputFailure("InvalidInput", "source", "Source binding is required");
            var r = input;
            Need(new[] { r.Source.ModelId, r.Source.MechanismId, r.Source.DriverId, r.Source.EventDofId, r.Source.EventKey }.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 4096), "source", "Missing or oversized source reference");
            Need(r.Source.MaxEvents >= 0 && r.Source.MaxEvents <= 4096, "source.maxEvents", "Event resource bound is 0..4096");
            Need(r.Profile == DiscreteGeometryContract.Profile && r.Backend == DiscreteLayoutContract.Backend && r.Ranking == DiscreteLayoutContract.Ranking && r.Unit == DiscreteGeometryContract.LengthUnit,
                "profile/backend/ranking/unit", "Unsupported versioned profile, backend, ranking or length unit", "Unsupported", true);
            Need(r.FrameQuarter >= 0 && r.FrameQuarter < 4 && r.Directions.All(d => d >= 0 && d < 4), "directions/frameQuarter", "Only cardinal quarter-turn frames are supported", "Unsupported", true);
            Need(PointBounded(r.Anchor) && r.Anchor.Z == Rational.Zero, "anchor", "Anchor must be bounded XY at Z=0; no new layer freedom", "Unsupported", true);
            Need(r.ExpansionBudget >= 0 && r.ExpansionBudget <= DiscreteLayoutContract.BudgetCeiling && r.CandidateCap >= 0 && r.CandidateCap <= DiscreteLayoutContract.CandidateCeiling, "budget/cap", "Expansion budget 0..8192 and returned cap 0..128");
            Need(Bounded(r.MinimumClearance) && r.MinimumClearance > Rational.Zero && r.MinimumClearance <= Rational.One, "minimumClearance", "Clearance must be exact in (0,1]");
            Need(r.RequiredRegion == null || (ExactGeometry.BoxValid(r.RequiredRegion) && PointBounded(r.RequiredRegion.Min) && PointBounded(r.RequiredRegion.Max)), "requiredRegion", "Invalid bounded Required region");
            Need(!r.PreferredOffset.HasValue || PointBounded(r.PreferredOffset.Value), "preferredOffset", "Invalid bounded Preferred offset");
            Need(r.KeepOuts.All(k => !string.IsNullOrWhiteSpace(k.Id) && k.Id.Length <= 128 && ExactGeometry.BoxValid(k.Bounds) && PointBounded(k.Bounds.Min) && PointBounded(k.Bounds.Max)), "keepOuts", "Keep-outs need bounded boxes and nonempty IDs");
            Need(r.KeepOuts.GroupBy(k => k.Id, StringComparer.Ordinal).All(g => g.Select(k => k.Canonical).Distinct(StringComparer.Ordinal).Count() == 1), "keepOuts.id", "Conflicting duplicate keep-out identity");
            var keepouts = r.KeepOuts.GroupBy(k => k.Id, StringComparer.Ordinal).Select(g => g.First()).OrderBy(k => k.Id, StringComparer.Ordinal).ToArray();
            var pr = r.PrimaryRadii.Distinct().OrderBy(x => x).ToArray(); var sr = r.SecondaryRadii.Distinct().OrderBy(x => x).ToArray();
            Need(pr.Length > 0 && sr.Length > 0, "radii", "Empty required size domain");
            Need(pr.Concat(sr).All(x => Bounded(x) && x > Rational.Zero), "radii", "Radius must be positive exact rational");
            Need(pr.Concat(sr).All(x => x >= new Rational(4) && x <= new Rational(40)), "radii", "Supported outer track radius is 4..40 ticks", "Unsupported", true);
            LayoutProbeConfiguration[] Probes(IEnumerable<LayoutProbeConfiguration> values, string field)
            {
                var p = values.GroupBy(v => v.Canonical, StringComparer.Ordinal).Select(g => g.First()).OrderBy(v => v.Canonical, StringComparer.Ordinal).ToArray();
                Need(p.Length > 0, field, "Empty required probe domain");
                Need(p.All(v => v.BearingQuarter >= 0 && v.BearingQuarter < 4), field + ".bearing", "Cardinal probes only", "Unsupported", true);
                Need(p.All(v => Bounded(v.RadialFraction) && Bounded(v.Travel) && Bounded(v.Retract) && v.RadialFraction > Rational.Zero && v.Travel >= Rational.Zero && v.Retract > Rational.Zero), field, "Invalid exact probe parameters");
                Need(p.All(v => v.RadialFraction > new Rational(1, 2) && v.RadialFraction < Rational.One && v.Travel <= new Rational(2048) && v.Retract <= new Rational(2048)), field, "Point probe must lie inside annulus; travel/retract at most 2048", "Unsupported", true);
                return p;
            }
            var pp = Probes(r.PrimaryProbes, "primaryProbes"); var sp = Probes(r.SecondaryProbes, "secondaryProbes");
            var directions = r.Directions.Distinct().OrderBy(d => d).ToArray();
            var distances = r.Distances.Distinct().OrderBy(d => d).ToArray(); BigInteger distanceCount = distances.Length;
            Need(r.Offsets.Count == 0 || (r.Range == null && r.Directions.Count == 0 && r.Distances.Count == 0), "offsets", "Choose explicit offsets OR directions/distances, not both");
            if (r.Range != null)
            {
                Need(r.Distances.Count == 0, "range", "Choose a distance range OR a list");
                Need(Bounded(r.Range.Min) && Bounded(r.Range.Max) && Bounded(r.Range.Step) && r.Range.Step > Rational.Zero && r.Range.Min <= r.Range.Max, "range", "Require min <= max and step > 0");
                distanceCount = ExactGeometry.Floor((r.Range.Max - r.Range.Min) / r.Range.Step) + 1;
            }
            BigInteger offsetCount = r.Offsets.Count > 0 ? r.Offsets.Select(p => p.Canonical).Distinct(StringComparer.Ordinal).Count() : distanceCount * directions.Length;
            domain = offsetCount * pr.Length * sr.Length * pp.Length * sp.Length;
            Need(offsetCount <= DiscreteLayoutContract.OffsetCeiling && domain <= DiscreteLayoutContract.DomainCeiling, "domain", "Cartesian domain exceeds resource ceiling BEFORE expansion/materialization: " + domain, "DomainTooLarge");
            Need(domain > 0, "domain", "Empty required placement domain");
            if (r.Range != null) distances = Enumerable.Range(0, (int)distanceCount).Select(k => r.Range.Min + r.Range.Step * new Rational(k)).ToArray();
            Need(distances.All(d => Bounded(d) && d > Rational.Zero && d <= new Rational(10000)), "distances", "Positive distances at most 10000 ticks required");
            var offsets = (r.Offsets.Count > 0 ? r.Offsets : directions.SelectMany(d => distances.Select(x => ExactGeometry.Cardinal(d) * x)))
                .GroupBy(p => p.Canonical, StringComparer.Ordinal).Select(g => g.First()).OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToArray();
            Need(offsets.All(p => PointBounded(p) && p.Z == Rational.Zero && ExactGeometry.Bearing(p, out _)), "offsets", "Only bounded nonzero cardinal XY offsets supported", "Unsupported", true);
            domain = (BigInteger)offsets.Length * pr.Length * sr.Length * pp.Length * sp.Length;
            var normalized = new DiscreteLayoutRequest(r.Source, r.Anchor, offsets: offsets, primaryRadii: pr, secondaryRadii: sr, primaryProbes: pp, secondaryProbes: sp,
                frameQuarter: r.FrameQuarter, keepOuts: keepouts, requiredRegion: r.RequiredRegion, preferredOffset: r.PreferredOffset, minimumClearance: r.MinimumClearance,
                expansionBudget: r.ExpansionBudget, candidateCap: r.CandidateCap, profile: r.Profile, backend: r.Backend, ranking: r.Ranking, unit: r.Unit);
            return new DiscreteLayoutNormalization(normalized, domain, DiscreteLayoutStatus.Complete, Array.Empty<LayoutDiagnostic>());
        }
        catch (InputFailure e) { return new DiscreteLayoutNormalization(null, domain, e.Unsupported ? DiscreteLayoutStatus.Unsupported : DiscreteLayoutStatus.InvalidInput, new[] { new LayoutDiagnostic(e.Code, e.Field, "", "request", "", e.Message) }); }
    }

    public IEnumerable<DiscreteLayoutAssignment> Assignments(DiscreteLayoutRequest normalized)
    {
        foreach (var offset in normalized.Offsets) foreach (var p in normalized.PrimaryRadii) foreach (var s in normalized.SecondaryRadii)
            foreach (var pp in normalized.PrimaryProbes) foreach (var sp in normalized.SecondaryProbes) yield return new DiscreteLayoutAssignment(offset, p, s, pp, sp);
    }

    public DiscreteLayoutCandidate RealizeAssignment(DiscreteEmbodimentModel model, DiscreteLayoutRequest normalized, DiscreteLayoutAssignment a)
    {
        var r = normalized;
        if (model.ModelId != r.Source.ModelId || !r.Offsets.Any(x => x.Canonical == a.Offset.Canonical) || !r.PrimaryRadii.Contains(a.PrimaryRadius) || !r.SecondaryRadii.Contains(a.SecondaryRadius) ||
            !r.PrimaryProbes.Any(x => x.Canonical == a.PrimaryProbe.Canonical) || !r.SecondaryProbes.Any(x => x.Canonical == a.SecondaryProbe.Canonical)) throw new ArgumentException("Assignment not in normalized request");
        var secondary = r.Anchor + ExactGeometry.Rotate(a.Offset, r.FrameQuarter);
        if (r.RequiredRegion != null && !ExactGeometry.Contains(r.RequiredRegion, secondary)) throw new ArgumentException("RequiredRegion");
        DiscreteGeometryStation Station(string id, GeometryPoint at, Rational radius, LayoutProbeConfiguration probe) =>
            DiscreteGeometryGenerator.BuildStation(model, id, at, radius, r.FrameQuarter, probe.BearingQuarter, probe.RadialFraction, probe.Travel, probe.Retract);
        var stations = new[] { Station("primary", r.Anchor, a.PrimaryRadius, a.PrimaryProbe), Station("secondary", secondary, a.SecondaryRadius, a.SecondaryProbe) };
        var g = new DiscreteGeometryCandidate(model.ModelId, model.BindingId, stations, r.KeepOuts, DiscreteGeometrySpatialValidator.Contacts(stations), r.MinimumClearance);
        var validation = new DiscreteGeometryGenerator().Validate(model, g);
        var boxes = stations.SelectMany(s => new[] { s.WheelEnvelope, s.TrackEnvelope, s.ProbeGuide, s.ProbeSweep, s.ActuatorSweep }).ToArray();
        var width = boxes.Max(b => b.Max.X) - boxes.Min(b => b.Min.X); var height = boxes.Max(b => b.Max.Y) - boxes.Min(b => b.Min.Y);
        var penalty = r.PreferredOffset.HasValue ? DiscreteGeometryGenerator.Distance(a.Offset, r.PreferredOffset.Value) : Rational.Zero;
        return new DiscreteLayoutCandidate(g, a, penalty, width * height, a.PrimaryProbe.Travel + a.SecondaryProbe.Travel + a.PrimaryRadius + a.SecondaryRadius, validation);
    }

    public static IOrderedEnumerable<DiscreteLayoutCandidate> Rank(IEnumerable<DiscreteLayoutCandidate> values) => values.OrderBy(c => c.PreferredPenalty)
        .ThenBy(c => c.Footprint).ThenBy(c => c.TravelSize).ThenBy(c => c.Geometry.GeometryId, StringComparer.Ordinal);

    public DiscreteLayoutGeneration Generate(DiscreteEmbodimentModel model, DiscreteLayoutRequest input, CancellationToken token = default, Action<int, int>? progress = null)
    {
        var n = Normalize(input); var r = n.Request; int evaluated = 0, valid = 0, rejected = 0, inconclusive = 0, deduplicated = 0, omitted = 0;
        var kept = new List<DiscreteLayoutCandidate>(); var diagnostics = new List<LayoutDiagnostic>(n.Diagnostics); var identities = new HashSet<string>(StringComparer.Ordinal);
        void Diagnostic(LayoutDiagnostic d) { if (diagnostics.Count < DiscreteLayoutContract.DiagnosticCeiling) diagnostics.Add(d); else omitted++; }
        DiscreteLayoutGeneration Finish(DiscreteLayoutStatus status, bool complete) => new DiscreteLayoutGeneration(n, status, complete, evaluated, valid, rejected, inconclusive, deduplicated,
            Rank(kept).Take(r?.CandidateCap ?? 0), diagnostics, omitted);
        if (r == null) return Finish(n.Status, false);
        if (model.ModelId != r.Source.ModelId || !new DiscreteEmbodimentCompiler().Validate(model).IsValid)
        { Diagnostic(new LayoutDiagnostic("SourceMismatch", "source.modelId", "", "source-binding-coverage", "model", "Validated source model does not match request")); return Finish(DiscreteLayoutStatus.InvalidInput, false); }
        foreach (var a in Assignments(r))
        {
            if (token.IsCancellationRequested) return Finish(DiscreteLayoutStatus.Cancelled, false);
            if (evaluated >= r.ExpansionBudget) return Finish(DiscreteLayoutStatus.IncompleteBudget, false);
            evaluated++;
            var second = r.Anchor + ExactGeometry.Rotate(a.Offset, r.FrameQuarter);
            if (r.RequiredRegion != null && !ExactGeometry.Contains(r.RequiredRegion, second))
            { rejected++; Diagnostic(new LayoutDiagnostic("RequiredRegion", "requiredRegion", a.Key, "required-placement", "secondary", "anchor=" + second.Canonical)); }
            else
            {
                var candidate = RealizeAssignment(model, r, a);
                if (candidate.Validation.IsValid)
                {
                    valid++;
                    if (!identities.Add(candidate.Geometry.GeometryId)) deduplicated++;
                    else { kept.Add(candidate); kept = Rank(kept).Take(r.CandidateCap).ToList(); }
                }
                else
                {
                    rejected++;
                    var failed = candidate.Validation.Checks.Where(c => c.Required && c.Verdict != GeometryVerdict.Pass && c.Verdict != GeometryVerdict.ProvenClearWithinDeclaredEnvelope).ToArray();
                    if (failed.Any(c => c.Verdict == GeometryVerdict.Inconclusive)) inconclusive++;
                    foreach (var c in failed) Diagnostic(new LayoutDiagnostic(c.Verdict.ToString(), c.Domain == "settled-geometric-sensing" ? "probeConfigurations" : c.Domain == "declared-envelope-clearance" ? "keepOuts/placement/clearance" : "assignment", a.Key, c.Domain, c.Item, c.Detail));
                }
            }
            progress?.Invoke(evaluated, (int)n.DomainSize);
        }
        return Finish(valid > 0 ? DiscreteLayoutStatus.Complete : DiscreteLayoutStatus.Infeasible, true);
    }
}
