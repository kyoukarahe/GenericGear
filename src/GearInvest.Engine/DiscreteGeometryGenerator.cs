using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.DiscreteEmbodimentCompiler;

namespace GearInvest.Engine;

/// <summary>Finite enumeration over declared relative offsets/radii. Required filters precede exact preferred ranking.</summary>
public sealed class DiscreteGeometryGenerator
{
    public DiscreteGeometryGenerationResult Generate(DiscreteEmbodimentModel model, DiscreteGeometryRequest request, CancellationToken cancellationToken = default)
    {
        string requestId = DiscreteGeometryContract.Id("geometry-generation", model.ModelId, DiscreteGeometryContract.Backend, request.Canonical);
        var valid = new List<DiscreteGeometryCandidate>(); var diagnostics = new List<string>(); int expanded = 0;
        DiscreteGeometryGenerationResult Finish(GeometryGenerationStatus status, bool complete) => new DiscreteGeometryGenerationResult(requestId, status, complete, expanded, valid.Count,
            valid.OrderBy(g => request.PreferredOffset.HasValue ? Distance(Offset(g), ExactGeometry.Rotate(request.PreferredOffset.Value, request.FrameQuarter)) : Rational.Zero)
                .ThenBy(g => Distance(Offset(g), default)).ThenBy(g => g.GeometryId, StringComparer.Ordinal).Take(Math.Max(0, request.CandidateCap)), diagnostics);
        try
        {
            if (request.Profile != DiscreteGeometryContract.Profile) return Finish(GeometryGenerationStatus.Unsupported, false);
            Need(new DiscreteEmbodimentCompiler().Validate(model).IsValid, "invalid-source-model");
            Need(request.SecondaryOffsets.Count > 0 && request.SecondaryOffsets.Count <= 32 && request.TrackRadii.Count > 0 && request.TrackRadii.Count <= 4, "finite-domain-bound");
            Need(request.SecondaryOffsets.Select(p => p.Canonical).Distinct(StringComparer.Ordinal).Count() == request.SecondaryOffsets.Count && request.TrackRadii.Distinct().Count() == request.TrackRadii.Count, "duplicate-domain-member");
            Need(request.ExpansionBudget >= 0 && request.ExpansionBudget <= 512 && request.CandidateCap >= 0 && request.CandidateCap <= 64, "budget-or-cap-bound");
            Need(request.FrameQuarter >= 0 && request.FrameQuarter < 4 && request.ProbeBearingQuarter >= 0 && request.ProbeBearingQuarter < 4, "unsupported-cardinal-frame");
            Need(request.TrackRadii.All(r => r >= new Rational(4) && r <= new Rational(40)) && request.ProbeTravel >= Rational.Zero && request.ProbeTravel <= new Rational(2048) &&
                request.RetractHeight > Rational.Zero && request.RetractHeight <= new Rational(2048) && request.MinimumClearance > Rational.Zero && request.MinimumClearance <= Rational.One, "geometry-parameter-bound");
            Need(request.MinimumBaseZ <= request.MaximumBaseZ && (request.RequiredSecondaryRegion == null || ExactGeometry.BoxValid(request.RequiredSecondaryRegion)), "invalid-required-region-or-band");
            Need(request.KeepOuts.Count <= 32 && request.KeepOuts.Select(k => k.Id).Distinct(StringComparer.Ordinal).Count() == request.KeepOuts.Count && request.KeepOuts.All(k => ExactGeometry.BoxValid(k.Bounds)), "invalid-keepouts");
            foreach (var offset in request.SecondaryOffsets) foreach (var radius in request.TrackRadii)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (expanded == request.ExpansionBudget) return Finish(GeometryGenerationStatus.BudgetExhausted, false);
                expanded++;
                var second = request.PrimaryAnchor + ExactGeometry.Rotate(offset, request.FrameQuarter);
                if (request.PrimaryAnchor.Z < request.MinimumBaseZ || request.PrimaryAnchor.Z > request.MaximumBaseZ || second.Z < request.MinimumBaseZ || second.Z > request.MaximumBaseZ ||
                    (request.RequiredSecondaryRegion != null && !ExactGeometry.Contains(request.RequiredSecondaryRegion, second)))
                { diagnostics.Add("required-placement-rejected:" + DiscreteGeometryContract.Int(expanded)); continue; }
                var stations = new[] { Build(model, request, "primary", request.PrimaryAnchor, radius), Build(model, request, "secondary", second, radius) };
                var candidate = new DiscreteGeometryCandidate(model.ModelId, model.BindingId, stations, request.KeepOuts, DiscreteGeometrySpatialValidator.Contacts(stations), request.MinimumClearance);
                var validation = Validate(model, candidate);
                if (validation.IsValid) valid.Add(candidate);
                else diagnostics.Add("geometry-rejected:" + DiscreteGeometryContract.Int(expanded) + ":" + string.Join(",", validation.Checks.Where(c => c.Required && c.Verdict != GeometryVerdict.Pass && c.Verdict != GeometryVerdict.ProvenClearWithinDeclaredEnvelope).Select(c => c.Domain + "/" + c.Item + "/" + c.Verdict)));
            }
            return Finish(valid.Count > 0 ? GeometryGenerationStatus.Complete : GeometryGenerationStatus.Exhausted, true);
        }
        catch (OperationCanceledException) { return Finish(GeometryGenerationStatus.Cancelled, false); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { diagnostics.Add(e.Message); return Finish(GeometryGenerationStatus.InvalidInput, false); }
    }

    public DiscreteGeometryValidation Validate(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry)
    {
        var spatial = DiscreteGeometrySpatialValidator.Validate(model, geometry);
        return new DiscreteGeometryValidation(spatial.Checks.Concat(new[] { new GeometryCheck("source-binding-coverage", "source-model-validation",
            new DiscreteEmbodimentCompiler().Validate(model).IsValid ? GeometryVerdict.Pass : GeometryVerdict.ProvenViolation,
            "14A-contract", Rational.Zero, Rational.One, "unchanged idealized model and Date/Month projection; omitted source states remain omitted") }));
    }

    public static GeometryPoint Offset(DiscreteGeometryCandidate g) => g.Stations[1].Center - g.Stations[0].Center;
    public static Rational Distance(GeometryPoint a, GeometryPoint b) { var d = a - b; return d.X * d.X + d.Y * d.Y + d.Z * d.Z; }

    private static DiscreteGeometryStation Build(DiscreteEmbodimentModel model, DiscreteGeometryRequest r, string wheel, GeometryPoint center, Rational outer)
        => BuildStation(model, wheel, center, outer, r.FrameQuarter, r.ProbeBearingQuarter, new Rational(3, 4), r.ProbeTravel, r.RetractHeight);

    // Shared materialization only. The 14B enumeration, ranking, defaults and wire identities remain unchanged.
    internal static DiscreteGeometryStation BuildStation(DiscreteEmbodimentModel model, string wheel, GeometryPoint center, Rational outer,
        int frameQuarter, int probeQuarter, Rational radialFraction, Rational travel, Rational retractHeight)
    {
        string track = wheel == "primary" ? "position-track" : "limit-track", probe = wheel == "primary" ? "position-probe" : "limit-probe";
        var values = model.Components.Single(c => c.Id == track).Sectors; int n = values.Count;
        var thickness = new Rational(2); var trackCenter = center + new GeometryPoint(0, 0, thickness);
        // Only realization reads source program values. Runtime queries intersect these surfaces without this table.
        var patches = values.Select((value, i) => new AxialSurfacePatch(track + ":surface:" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
            ExactGeometry.Turn(new Rational(-i, n) - new Rational(1, 2 * n)), new Rational(1, n), new Rational(value) + new Rational(2))).ToArray();
        var minZ = trackCenter.Z + patches.Min(p => p.Height); var maxZ = trackCenter.Z + patches.Max(p => p.Height);
        var radial = ExactGeometry.Cardinal(frameQuarter + probeQuarter);
        var origin = trackCenter + radial * (outer * radialFraction) + new GeometryPoint(0, 0, retractHeight);
        var wheelRadius = outer + Rational.One; var act = ExactGeometry.Cardinal(frameQuarter + probeQuarter + 2);
        var actuatorStart = center + act * (wheelRadius + new Rational(3)) + new GeometryPoint(0, 0, Rational.One);
        var actuatorEnd = center + act * (wheelRadius - new Rational(1, 2)) + new GeometryPoint(0, 0, Rational.One);
        var rim = center + act * wheelRadius;
        var engagement = new GeometryBox(rim - new GeometryPoint(1, 1, 0), rim + new GeometryPoint(1, 1, thickness));
        return new DiscreteGeometryStation(wheel, track, probe, n, center, trackCenter, frameQuarter, new Rational(probeQuarter, 4),
            wheelRadius, outer / new Rational(2), outer, thickness, patches, origin, new GeometryPoint(0, 0, -1), travel, new Rational(2), Rational.One, new Rational(1, 100 * n),
            actuatorStart, actuatorEnd, engagement, ExactGeometry.Radial(center, wheelRadius, center.Z, center.Z + thickness),
            ExactGeometry.Radial(trackCenter, outer, trackCenter.Z, maxZ), ExactGeometry.Bounds(new GeometryPoint(origin.X, origin.Y, minZ), origin),
            new GeometryBox(origin + new GeometryPoint(new Rational(-1, 4), new Rational(-1, 4), 1), origin + new GeometryPoint(new Rational(1, 4), new Rational(1, 4), 3)),
            ExactGeometry.Bounds(actuatorStart, actuatorEnd));
    }
}
