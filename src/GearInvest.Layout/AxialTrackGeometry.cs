using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Layout;

public static class ExactGeometry
{
    public static Rational Min(Rational a, Rational b) => a < b ? a : b;
    public static Rational Max(Rational a, Rational b) => a > b ? a : b;
    public static Rational Abs(Rational a) => a < Rational.Zero ? -a : a;
    public static BigInteger Floor(Rational r) { var q = BigInteger.DivRem(r.Numerator, r.Denominator, out var rem); return rem.Sign < 0 ? q - 1 : q; }
    public static Rational Turn(Rational r) => r - new Rational(Floor(r));
    public static GeometryPoint Rotate(GeometryPoint p, int quarter)
    {
        switch ((quarter % 4 + 4) % 4)
        {
            case 0: return p;
            case 1: return new GeometryPoint(-p.Y, p.X, p.Z);
            case 2: return new GeometryPoint(-p.X, -p.Y, p.Z);
            default: return new GeometryPoint(p.Y, -p.X, p.Z);
        }
    }
    public static GeometryPoint Cardinal(int q) => Rotate(new GeometryPoint(Rational.One, Rational.Zero, Rational.Zero), q);
    public static bool Bearing(GeometryPoint p, out Rational bearing)
    {
        bearing = Rational.Zero;
        if (p.Y == Rational.Zero && p.X > Rational.Zero) return true;
        if (p.X == Rational.Zero && p.Y > Rational.Zero) { bearing = new Rational(1, 4); return true; }
        if (p.Y == Rational.Zero && p.X < Rational.Zero) { bearing = new Rational(1, 2); return true; }
        if (p.X == Rational.Zero && p.Y < Rational.Zero) { bearing = new Rational(3, 4); return true; }
        return false;
    }
    public static GeometryBox Bounds(GeometryPoint a, GeometryPoint b) => new GeometryBox(
        new GeometryPoint(Min(a.X, b.X), Min(a.Y, b.Y), Min(a.Z, b.Z)), new GeometryPoint(Max(a.X, b.X), Max(a.Y, b.Y), Max(a.Z, b.Z)));
    public static GeometryBox Radial(GeometryPoint c, Rational r, Rational bottom, Rational top) => new GeometryBox(
        new GeometryPoint(c.X - r, c.Y - r, bottom), new GeometryPoint(c.X + r, c.Y + r, top));
    public static bool BoxValid(GeometryBox b) => b.Min.X <= b.Max.X && b.Min.Y <= b.Max.Y && b.Min.Z <= b.Max.Z;
    public static bool Contains(GeometryBox b, GeometryPoint p) => p.X >= b.Min.X && p.X <= b.Max.X && p.Y >= b.Min.Y && p.Y <= b.Max.Y && p.Z >= b.Min.Z && p.Z <= b.Max.Z;
    public static bool Contains(GeometryBox a, GeometryBox b) => Contains(a, b.Min) && Contains(a, b.Max);
    public static bool Separated(GeometryBox a, GeometryBox b, Rational gap) =>
        a.Max.X + gap <= b.Min.X || b.Max.X + gap <= a.Min.X || a.Max.Y + gap <= b.Min.Y || b.Max.Y + gap <= a.Min.Y || a.Max.Z + gap <= b.Min.Z || b.Max.Z + gap <= a.Min.Z;
    public static bool SegmentHit(GeometryPoint a, GeometryPoint b, GeometryBox box, out Rational enter)
    {
        enter = Rational.Zero; var end = Rational.One;
        var av = new[] { a.X, a.Y, a.Z }; var bv = new[] { b.X, b.Y, b.Z };
        var lo = new[] { box.Min.X, box.Min.Y, box.Min.Z }; var hi = new[] { box.Max.X, box.Max.Y, box.Max.Z };
        for (int i = 0; i < 3; i++)
        {
            var d = bv[i] - av[i];
            if (d == Rational.Zero) { if (av[i] < lo[i] || av[i] > hi[i]) return false; continue; }
            var u = (lo[i] - av[i]) / d; var v = (hi[i] - av[i]) / d;
            enter = Max(enter, Min(u, v)); end = Min(end, Max(u, v)); if (enter > end) return false;
        }
        return true;
    }
}

/// <summary>Surface scan and finite axial ray query. Intentionally has NO model, program table or index argument.</summary>
public static class AxialTrackQuery
{
    public static DiscreteGeometryHit Read(DiscreteGeometryCandidate geometry, string probeId, Rational wheelTurns)
    {
        var intended = geometry.Stations.Single(s => s.ProbeId == probeId);
        var bearing = Rational.Zero; var local = Rational.Zero;
        DiscreteGeometryHit Result(GeometryHitStatus status, string surface = "", GeometryPoint point = default,
            Rational travel = default, Rational displacement = default, Rational value = default, string diagnostic = "") =>
            new DiscreteGeometryHit(geometry.GeometryId, intended.WheelId, intended.TrackId, probeId, surface, status, wheelTurns, local, point, travel, displacement, value, diagnostic);
        if (intended.ProbeDirection.Canonical != new GeometryPoint(Rational.Zero, Rational.Zero, new Rational(-1)).Canonical ||
            intended.Detents < 2 || intended.Detents > 512 || (wheelTurns * new Rational(intended.Detents)).Denominator != BigInteger.One || intended.Travel < Rational.Zero)
            return Result(GeometryHitStatus.Unsupported, diagnostic: "requires-settled-negative-axial-finite-probe");
        if (!ExactGeometry.Bearing(intended.ProbeOrigin - intended.TrackCenter, out bearing))
            return Result(GeometryHitStatus.Unsupported, diagnostic: "probe-bearing-not-cardinal");
        local = ExactGeometry.Turn(bearing - new Rational(intended.FrameQuarter, 4) - wheelTurns - intended.MountingTurns);
        var hits = new List<(Rational t, string id, GeometryPoint point, bool intended, Rational height)>();
        bool boundary = false, beyondTravel = false;
        // Only the intended wheel's pose is supplied. Other station bodies are conservatively treated as swept occluders.
        foreach (var station in geometry.Stations)
        {
            if (station.WheelId != intended.WheelId)
            {
                foreach (var box in new[] { station.WheelEnvelope, station.TrackEnvelope, station.ProbeGuide }) Occluder(station.WheelId + ":swept-occluder", box);
                continue;
            }
            var delta = intended.ProbeOrigin - station.TrackCenter;
            var radiusSquared = delta.X * delta.X + delta.Y * delta.Y;
            bool radialInterior = radiusSquared > station.InnerRadius * station.InnerRadius && radiusSquared < station.OuterRadius * station.OuterRadius;
            if (radiusSquared == station.InnerRadius * station.InnerRadius || radiusSquared == station.OuterRadius * station.OuterRadius) boundary = true;
            foreach (var patch in station.Patches)
            {
                var angular = ExactGeometry.Turn(local - patch.StartTurns);
                if (!radialInterior || angular > patch.SpanTurns) continue;
                if (angular <= station.AngularMargin || angular >= patch.SpanTurns - station.AngularMargin) { boundary = true; continue; }
                Add(station.TrackCenter.Z + patch.Height, patch.Id, true, patch.Height);
            }
            var d = intended.ProbeOrigin - station.Center;
            if (d.X * d.X + d.Y * d.Y <= station.WheelRadius * station.WheelRadius)
                Add(station.Center.Z + station.Thickness, station.WheelId + ":wheel-top", false, Rational.Zero);
        }
        foreach (var keepOut in geometry.KeepOuts) Occluder(keepOut.Id, keepOut.Bounds);
        if (boundary) return Result(GeometryHitStatus.Boundary, diagnostic: "sector-or-radial-edge-margin");
        if (hits.Count == 0) return Result(beyondTravel ? GeometryHitStatus.TravelLimit : GeometryHitStatus.Missing, diagnostic: "no-reachable-intended-surface");
        var ordered = hits.OrderBy(h => h.t).ThenBy(h => h.id, StringComparer.Ordinal).ToArray(); var first = ordered[0];
        if (ordered.Count(h => h.t == first.t) > 1) return Result(GeometryHitStatus.Ambiguous, first.id, first.point, first.t, diagnostic: "multiple-first-hits");
        if (!first.intended) return Result(GeometryHitStatus.Occluded, first.id, first.point, first.t, diagnostic: "first-hit-is-not-intended-sector-top");
        if (intended.Scale <= Rational.Zero) return Result(GeometryHitStatus.InvalidCalibration, first.id, first.point, first.t, first.height, diagnostic: "non-positive-calibration-scale");
        var decoded = (first.point.Z - intended.TrackCenter.Z - intended.Datum) / intended.Scale;
        return Result(decoded.Denominator == BigInteger.One && decoded >= Rational.One ? GeometryHitStatus.Hit : GeometryHitStatus.InvalidCalibration,
            first.id, first.point, first.t, first.point.Z - intended.TrackCenter.Z, decoded, decoded.Denominator == BigInteger.One ? "" : "non-integral-readback-no-rounding");

        void Add(Rational z, string id, bool isIntended, Rational height)
        {
            var t = intended.ProbeOrigin.Z - z;
            if (t < Rational.Zero) return;
            if (t > intended.Travel) { if (isIntended) beyondTravel = true; return; }
            hits.Add((t, id, new GeometryPoint(intended.ProbeOrigin.X, intended.ProbeOrigin.Y, z), isIntended, height));
        }
        void Occluder(string id, GeometryBox box)
        {
            var end = intended.ProbeOrigin + intended.ProbeDirection * intended.Travel;
            if (ExactGeometry.SegmentHit(intended.ProbeOrigin, end, box, out var t))
            {
                var point = intended.ProbeOrigin + (end - intended.ProbeOrigin) * t;
                hits.Add((t * intended.Travel, id, point, false, Rational.Zero));
            }
        }
    }
}

public static class DiscreteGeometrySpatialValidator
{
    public static GeometryContactPermission[] Contacts(IEnumerable<DiscreteGeometryStation> stations) => stations.SelectMany(s => new[] {
        new GeometryContactPermission(s.TrackId,s.WheelId,"base-face","rigid-attachment",Rational.Zero,Rational.One,"coaxial-base-at-wheel-top"),
        new GeometryContactPermission(s.ProbeId,s.TrackId,"sector-top","settled-sensing",Rational.Zero,DiscreteGeometryContract.ClearDrive,"first-hit-approach-and-retract-above-surface"),
        new GeometryContactPermission(s.WheelId+"-actuator",s.WheelId,"engagement-region","ideal-positive-indexing",DiscreteGeometryContract.ClearDrive,new Rational(9,10),"point-path-inside-declared-engagement-region")
    }).ToArray();

    public static DiscreteGeometryValidation Validate(DiscreteEmbodimentModel model, DiscreteGeometryCandidate g)
    {
        var checks = new List<GeometryCheck>();
        void Add(string domain, string item, bool pass, string detail, string method = "exact-analytic") => checks.Add(new GeometryCheck(domain, item,
            pass ? GeometryVerdict.Pass : GeometryVerdict.ProvenViolation, method, Rational.Zero, Rational.One, detail));
        void Clear(string item, GeometryVerdict verdict, string detail, Rational from, Rational to, string method = "conservative-whole-interval") =>
            checks.Add(new GeometryCheck("declared-envelope-clearance", item, verdict, method, from, to, detail));
        try
        {
            Require(g.Profile == DiscreteGeometryContract.Profile && g.LengthUnit == DiscreteGeometryContract.LengthUnit && g.MillimetersPerTick == DiscreteGeometryContract.MillimetersPerTick, "unsupported-geometry-profile-or-unit");
            Require(g.MinimumClearance > Rational.Zero && g.MinimumClearance <= new Rational(10), "clearance-bound");
            Require(g.Stations.Count == 2 && g.Stations.Select(s => s.WheelId).SequenceEqual(new[] { "primary", "secondary" }), "station-coverage");
            Require(g.KeepOuts.Count <= 32 && g.KeepOuts.Select(k => k.Id).Distinct(StringComparer.Ordinal).Count() == g.KeepOuts.Count, "keepout-bound-or-duplicate");
            Require(g.KeepOuts.All(k => !string.IsNullOrWhiteSpace(k.Id) && ExactGeometry.BoxValid(k.Bounds)), "keepout-shape");
            Add("source-binding-coverage", "model", model.ModelId == g.ModelId && model.BindingId == g.SourceBindingId, "14A identity and explicit projection binding; not source authentication");
            Add("geometry-structure-attachment", "contact-permissions", Contacts(g.Stations).Select(c => c.Canonical).OrderBy(c => c, StringComparer.Ordinal).SequenceEqual(g.Contacts.Select(c => c.Canonical)), "exact surface/port, phase, condition and contact kind; no whole-pair blanket exemption");
            foreach (var s in g.Stations)
            {
                Require(s.Detents >= 2 && s.Detents <= 512 && s.Patches.Count == s.Detents && s.Patches.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() == s.Patches.Count, "surface-count-or-duplicate");
                Require(s.FrameQuarter >= 0 && s.FrameQuarter <= 3 && s.WheelRadius > s.OuterRadius && s.OuterRadius > s.InnerRadius && s.InnerRadius > Rational.Zero && s.WheelRadius <= new Rational(100), "radius-or-frame-bound");
                Require(s.Thickness > Rational.Zero && s.Travel >= Rational.Zero && s.Travel <= new Rational(2048) && s.Scale > Rational.Zero, "primitive-parameter-bound");
                Require(s.Patches.All(p => p.StartTurns >= Rational.Zero && p.StartTurns < Rational.One && p.SpanTurns > Rational.Zero && p.SpanTurns <= Rational.One && p.Height > Rational.Zero && p.Height <= new Rational(1024)), "surface-domain");
                Require(s.AngularMargin > Rational.Zero && s.AngularMargin < new Rational(1, 4 * s.Detents), "angular-margin-bound");
                foreach (var box in new[] { s.Engagement, s.WheelEnvelope, s.TrackEnvelope, s.ProbeSweep, s.ProbeGuide, s.ActuatorSweep }) Require(ExactGeometry.BoxValid(box), "invalid-box");
                var expectedTrack = s.WheelId == "primary" ? "position-track" : "limit-track";
                var expectedProbe = s.WheelId == "primary" ? "position-probe" : "limit-probe";
                Add("source-binding-coverage", s.WheelId, s.TrackId == expectedTrack && s.ProbeId == expectedProbe && model.Components.Single(c => c.Id == s.WheelId).Detents == s.Detents, "wheel/track/probe role binding");
                Add("geometry-structure-attachment", s.TrackId, s.Center.X == s.TrackCenter.X && s.Center.Y == s.TrackCenter.Y && s.Center.Z + s.Thickness == s.TrackCenter.Z,
                    "parallel +Z axes, rigid coaxial track base at wheel top, exact mounting phase");
                // Calibrations are profile constants, not knobs fitted to an expected answer on every query.
                Add("source-refinement", s.ProbeId + ":calibration", s.Datum == new Rational(2) && s.Scale == Rational.One, "fixed height=2+tick; calibration identity cannot compensate a wrong surface");
                var table = model.Components.Single(c => c.Id == expectedTrack).Sectors;
                var hits = Enumerable.Range(0, s.Detents).Select(i => AxialTrackQuery.Read(g, s.ProbeId, new Rational(i, s.Detents))).ToArray();
                Add("settled-geometric-sensing", s.ProbeId, hits.All(h => h.Status == GeometryHitStatus.Hit), string.Join(";", hits.Where(h => h.Status != GeometryHitStatus.Hit).Select(h => h.WheelTurns + ":" + h.Status + ":" + h.Diagnostic)));
                Add("source-refinement", s.TrackId, hits.Length == table.Count && hits.Select((h, i) => h.Status == GeometryHitStatus.Hit && h.Value == new Rational(table[i])).All(x => x), "all detents queried from surfaces, THEN compared with source table; never used as runtime sensing input");
                // Check the full partition, independently of declaration IDs/order.
                var patches = s.Patches.OrderBy(p => p.StartTurns).ToArray();
                Add("geometry-structure-attachment", s.TrackId + ":partition", patches.Select(p => p.StartTurns).Distinct().Count() == patches.Length &&
                    patches.Aggregate(Rational.Zero, (sum, p) => sum + p.SpanTurns) == Rational.One &&
                    Enumerable.Range(0, patches.Length).All(i => ExactGeometry.Turn(patches[i].StartTurns + patches[i].SpanTurns) == patches[(i + 1) % patches.Length].StartTurns), "closed periodic partition, open edge-margin sensing");
                var minZ = s.TrackCenter.Z + s.Patches.Min(p => p.Height); var maxZ = s.TrackCenter.Z + s.Patches.Max(p => p.Height);
                Add("geometry-structure-attachment", s.WheelId + ":envelope-containment",
                    ExactGeometry.Contains(s.WheelEnvelope, ExactGeometry.Radial(s.Center, s.WheelRadius, s.Center.Z, s.Center.Z + s.Thickness)) &&
                    ExactGeometry.Contains(s.TrackEnvelope, ExactGeometry.Radial(s.TrackCenter, s.OuterRadius, s.TrackCenter.Z, maxZ)) &&
                    ExactGeometry.Contains(s.ProbeSweep, ExactGeometry.Bounds(new GeometryPoint(s.ProbeOrigin.X, s.ProbeOrigin.Y, minZ), s.ProbeOrigin)) &&
                    ExactGeometry.Contains(s.ActuatorSweep, ExactGeometry.Bounds(s.ActuatorStart, s.ActuatorEnd)), "declared bounds must contain every supported shape/motion, not just endpoints of rotating geometry");
                Add("geometry-structure-attachment", s.ProbeId + ":guide", ExactGeometry.Contains(s.ProbeGuide, s.ProbeOrigin + new GeometryPoint(Rational.Zero, Rational.Zero, new Rational(2))) && s.ProbeGuide.Min.Z >= s.ProbeOrigin.Z + g.MinimumClearance,
                    "occupied guide above retract tip; ideal axial point probe, not a finite-radius follower body");
                var clear = s.ProbeOrigin.Z >= maxZ + g.MinimumClearance;
                Clear(s.ProbeId + "|" + s.TrackId + ":sense-retract", hits.All(h => h.Status == GeometryHitStatus.Hit) && clear ? GeometryVerdict.ProvenClearWithinDeclaredEnvelope : GeometryVerdict.Inconclusive,
                    "all settled states: exact first-hit segment stops at intended top, then retracts; contact allowed only in this phase", Rational.Zero, DiscreteGeometryContract.ClearDrive, "analytic-first-hit/all-detents");
                Clear(s.ProbeId + "|" + s.TrackId + ":wheel-drive", clear ? GeometryVerdict.ProvenClearWithinDeclaredEnvelope : GeometryVerdict.Inconclusive,
                    "retracted point above full rotational track envelope plus minimum clearance", DiscreteGeometryContract.ClearDrive, Rational.One);
                var start = s.ActuatorStart - s.Center; var end = s.ActuatorEnd - s.Center;
                bool aligned = ExactGeometry.Bearing(start, out var bearingStart) && ExactGeometry.Bearing(end, out var bearingEnd) && bearingStart == bearingEnd;
                var radiusStart = ExactGeometry.Abs(start.X) + ExactGeometry.Abs(start.Y); var radiusEnd = ExactGeometry.Abs(end.X) + ExactGeometry.Abs(end.Y);
                var entry = s.Center + ExactGeometry.Cardinal((int)(bearingStart * new Rational(4)).Numerator) * s.WheelRadius + new GeometryPoint(Rational.Zero, Rational.Zero, start.Z);
                bool reach = aligned && start.Z == end.Z && start.Z > Rational.Zero && start.Z < s.Thickness && radiusStart >= s.WheelRadius + g.MinimumClearance && radiusEnd >= s.WheelRadius - Rational.One && radiusEnd < s.WheelRadius &&
                    ExactGeometry.Contains(s.Engagement, entry) && ExactGeometry.Contains(s.Engagement, s.ActuatorEnd) && ExactGeometry.Contains(s.ActuatorSweep, s.ActuatorStart) && ExactGeometry.Contains(s.ActuatorSweep, s.ActuatorEnd);
                Add("actuator-reach", s.WheelId, reach, "finite cardinal point stroke crosses rim and ends in intended engagement region; no tooth/force claim");
                Clear(s.WheelId + "-actuator|" + s.WheelId, reach ? GeometryVerdict.ProvenClearWithinDeclaredEnvelope : GeometryVerdict.Inconclusive,
                    "only radial segment inside engagement region may enter wheel during ideal indexing; retracted at other phases", Rational.Zero, Rational.One, "analytic-segment/phase-restricted-engagement");
            }
            // Rotational bodies use full-angle conservative boxes, point translations use full segment boxes.
            var items = g.Stations.SelectMany(s => new[] {
                (id:s.WheelId+":body",owner:s.WheelId,kind:"wheel",box:s.WheelEnvelope),
                (id:s.TrackId+":body",owner:s.WheelId,kind:"track",box:s.TrackEnvelope),
                (id:s.ProbeId+":sweep",owner:s.WheelId,kind:"probe",box:s.ProbeSweep),
                (id:s.ProbeId+":guide",owner:s.WheelId,kind:"guide",box:s.ProbeGuide),
                (id:s.WheelId+":actuator-sweep",owner:s.WheelId,kind:"actuator",box:s.ActuatorSweep)
            }).ToArray();
            for (int i = 0; i < items.Length; i++) for (int j = i + 1; j < items.Length; j++)
            {
                var a = items[i]; var b = items[j]; var pair = a.id + "|" + b.id;
                if (a.owner == b.owner && ((a.kind == "wheel" && b.kind == "track") || (a.kind == "track" && b.kind == "probe") || (a.kind == "wheel" && b.kind == "actuator")))
                {
                    if (a.kind == "wheel" && b.kind == "track")
                    {
                        var station = g.Stations.Single(s => s.WheelId == a.owner);
                        bool attached = station.Center.X == station.TrackCenter.X && station.Center.Y == station.TrackCenter.Y && station.Center.Z + station.Thickness == station.TrackCenter.Z;
                        Clear(pair, attached ? GeometryVerdict.ProvenClearWithinDeclaredEnvelope : GeometryVerdict.Inconclusive, "coaxial base-face rigid attachment checked; no volumetric overlap only if attached", Rational.Zero, Rational.One, "analytic-attachment-interface");
                    }
                    continue; // Probe/track and actuator/wheel have explicit phase-specific analytical checks above.
                }
                Clear(pair, ExactGeometry.Separated(a.box, b.box, g.MinimumClearance) ? GeometryVerdict.ProvenClearWithinDeclaredEnvelope : GeometryVerdict.Inconclusive,
                    "full-motion envelope separation; overlap alone is NOT proof of actual collision", Rational.Zero, Rational.One);
            }
            foreach (var keepOut in g.KeepOuts) foreach (var item in items)
            {
                var verdict = ExactGeometry.Separated(keepOut.Bounds, item.box, g.MinimumClearance) ? GeometryVerdict.ProvenClearWithinDeclaredEnvelope : GeometryVerdict.Inconclusive;
                var station = g.Stations.Single(s => s.WheelId == item.owner);
                if (item.kind == "actuator" && ExactGeometry.SegmentHit(station.ActuatorStart, station.ActuatorEnd, keepOut.Bounds, out _)) verdict = GeometryVerdict.ProvenViolation;
                if (item.kind == "probe" && ExactGeometry.SegmentHit(item.box.Min, item.box.Max, keepOut.Bounds, out _)) verdict = GeometryVerdict.ProvenViolation;
                Clear(item.id + "|" + keepOut.Id, verdict, "occupied keep-out; exact moving-point segment intersection can prove violation, otherwise overlapping conservative bounds remain inconclusive", Rational.Zero, Rational.One, "whole-segment/slab+conservative-envelope");
            }
        }
        catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is OverflowException)
        { Add("geometry-structure-attachment", "input", false, e.Message); }
        foreach (var domain in new[] { "indexed-motion", "logical-equivalence", "visual-projection", "continuous-contact", "dynamics-manufacturing" })
            checks.Add(new GeometryCheck(domain, "scope", GeometryVerdict.NotPerformed, "not-performed", Rational.Zero, Rational.One, "separate execution/consumer evidence required; selector/indexer/drive remain idealized", false));
        return new DiscreteGeometryValidation(checks);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
