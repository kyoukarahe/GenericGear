using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed partial class AnchoredCompoundGearRouter
{
    internal static Diagnostic Error(string code, string subject, string message) => new Diagnostic("COMPOUND_ROUTING_" + code, DiagnosticSeverity.Error, message, subject);
    public static CompoundRoutingNormalization Normalize(AnchoredCompoundGearRoutingRequest? raw)
    {
        var diagnostics = new List<Diagnostic>(); var status = GearRoutingStatus.InvalidInput;
        void Bad(string field, string message) => diagnostics.Add(Error("INVALID_INPUT", field, message));
        void Limit(string field) => diagnostics.Add(Error("DOMAIN_LIMIT", field, "Finite ingestion/preparation ceiling exceeded; no search performed."));
        bool Box(GearRouteBox? b) => b != null && b.IsOrdered && new[] { b.MinX, b.MinY, b.MaxX, b.MaxY }.All(n => BigInteger.Abs(n) <= GearRoutingContract.MaxScalar);
        if (raw == null) return new CompoundRoutingNormalization(null, status, new[] { Error("INVALID_INPUT", "request", "Request required.") });
        if (raw.IngestionLimitExceeded || raw.InputLeg?.IngestionLimitExceeded == true || raw.OutputLeg?.IngestionLimitExceeded == true) Limit("rawCollections");
        if (raw.Profile != CompoundRoutingContract.Profile || raw.Backend != CompoundRoutingContract.Backend || raw.Ranking != CompoundRoutingContract.Ranking || raw.Unit != "tick" || raw.InputLayer != 0 || raw.OutputLayer != 1)
        { status = GearRoutingStatus.Unsupported; diagnostics.Add(Error("UNSUPPORTED", "profile/layers/unit", "Only the versioned one-compound input-0/output-1 profile is supported.")); }
        if (raw.MaximumTotalIdlers < 0 || raw.MaximumTotalIdlers > CompoundRoutingContract.MaxTotalIdlers) Bad("maximumTotalIdlers", "Total idler limit must be 0..10.");
        if (raw.Input == null || raw.Output == null || raw.InputLeg == null || raw.OutputLeg == null) Bad("anchors/legs", "Two fixed anchors and two leg domains required.");
        if (raw.RequiredCompoundRegion != null && !Box(raw.RequiredCompoundRegion) || raw.PreferredCompoundRegion != null && !Box(raw.PreferredCompoundRegion)) Bad("compoundRegions", "Finite ordered rectangle required.");
        int[] Teeth(IEnumerable<int> values, string field)
        {
            var result = values.Distinct().OrderBy(t => t).ToArray();
            if (result.Length == 0 || result.Any(t => t <= 0)) Bad(field, "Nonempty positive integer options required.");
            if (result.Length > GearRoutingContract.MaxToothOptions || result.Any(t => t > GearRoutingContract.MaxTeeth)) Limit(field);
            return result;
        }
        var receiving = Teeth(raw.ReceivingTeeth, "receivingTeeth"); var driving = Teeth(raw.DrivingTeeth, "drivingTeeth");
        if (raw.KeepOuts.Any(k => k == null || (int)k.Layers < 1 || (int)k.Layers > 3)) Bad("keepOuts", "Explicit layer mask must be Layer0, Layer1 or Both.");
        if (diagnostics.Count != 0) return new CompoundRoutingNormalization(null, status, diagnostics);
        // Reuse the established integer-site, option, region and resource checks, without running its router.
        GearRoutingNormalization Leg(CompoundRoutingLeg leg, IEnumerable<GearRouteRegion> obstacles) => AnchoredGearRouter.Normalize(new AnchoredGearRoutingRequest(
            raw.Input!, raw.Output!, raw.TargetTransfer, raw.PitchRadiusTicksPerTooth, leg.Bounds,
            leg.HasExplicitSites ? leg.Sites : null, leg.Grid, leg.IdlerTeeth, leg.MinIdlers, leg.MaxIdlers,
            leg.UnrelatedClearance, leg.KeepOutClearance, obstacles, leg.RequiredRegions, leg.PreferredRegion, raw.WorkBudget, raw.MaximumReturned));
        // Validate the global IDs even when two obstacles apply to disjoint layers.
        var allObstacles = raw.KeepOuts.Select(k => new GearRouteRegion(k.Id, k.Bounds)).ToArray();
        var left = Leg(raw.InputLeg!, allObstacles); var right = Leg(raw.OutputLeg!, allObstacles);
        diagnostics.AddRange(left.Diagnostics); diagnostics.AddRange(right.Diagnostics);
        if (left.Status == GearRoutingStatus.Unsupported || right.Status == GearRoutingStatus.Unsupported) status = GearRoutingStatus.Unsupported;
        if (raw.HasExplicitCompoundSites == (raw.CompoundGrid != null)) Bad("compoundSites/grid", "Specify exactly one finite list or grid.");
        if (raw.CompoundGrid != null && raw.CompoundGrid.Step.X > 0 && raw.CompoundGrid.Step.Y > 0 && Box(raw.CompoundGrid.Bounds))
        {
            var g = raw.CompoundGrid;
            BigInteger Floor(BigInteger n, BigInteger d) { var q = BigInteger.DivRem(n, d, out var rem); return rem.Sign < 0 ? q - 1 : q; }
            var nx = BigInteger.Max(0, Floor(g.Bounds.MaxX - g.Origin.X, g.Step.X) + Floor(g.Origin.X - g.Bounds.MinX, g.Step.X) + 1);
            var ny = BigInteger.Max(0, Floor(g.Bounds.MaxY - g.Origin.Y, g.Step.Y) + Floor(g.Origin.Y - g.Bounds.MinY, g.Step.Y) + 1);
            if (nx * ny > CompoundRoutingContract.MaxCompoundSites) Limit("compoundGridSites");
        }
        if (diagnostics.Count != 0) return new CompoundRoutingNormalization(null, status, diagnostics);
        var siteDomain = Leg(new CompoundRoutingLeg(raw.InputLeg!.Bounds, raw.HasExplicitCompoundSites ? raw.CompoundSites : null, raw.CompoundGrid, Array.Empty<int>(), maxIdlers: 0), Array.Empty<GearRouteRegion>());
        diagnostics.AddRange(siteDomain.Diagnostics);
        if (siteDomain.Request?.Sites.Count > CompoundRoutingContract.MaxCompoundSites) Limit("compoundSites");
        if (diagnostics.Count != 0) return new CompoundRoutingNormalization(null, status, diagnostics);
        CompoundRoutingLeg NormalLeg(AnchoredGearRoutingRequest r) => new CompoundRoutingLeg(r.Bounds, r.Sites, idlerTeeth: r.IdlerTeeth,
            minIdlers: r.MinIdlers, maxIdlers: r.MaxIdlerCount, unrelatedClearance: r.UnrelatedClearance, keepOutClearance: r.KeepOutClearance,
            requiredRegions: r.RequiredRegions, preferredRegion: r.PreferredRegion);
        var l = NormalLeg(left.Request!); var o = NormalLeg(right.Request!); var sites = siteDomain.Request!.Sites;
        var nl = new BigInteger(l.Sites.Count) * l.IdlerTeeth.Count + 2; var nr = new BigInteger(o.Sites.Count) * o.IdlerTeeth.Count + 2;
        // Includes node checks and both adjacency graphs per pair/site, even for ratio-invalid pairs.
        var preparation = receiving.Length * driving.Length * sites.Count * (nl * (nl + 1) / 2 + nr * (nr + 1) / 2);
        if (preparation > CompoundRoutingContract.MaxPreparationChecks) Limit("wholeRequestPreparation");
        if (diagnostics.Count != 0) return new CompoundRoutingNormalization(null, status, diagnostics);
        return new CompoundRoutingNormalization(new AnchoredCompoundGearRoutingRequest(raw.Input!, raw.Output!, raw.TargetTransfer, raw.PitchRadiusTicksPerTooth,
            receiving, driving, l, o, sites, maximumTotalIdlers: raw.MaximumTotalIdlers, keepOuts: raw.KeepOuts.OrderBy(k => k.Id, StringComparer.Ordinal),
            requiredCompoundRegion: raw.RequiredCompoundRegion, preferredCompoundRegion: raw.PreferredCompoundRegion, workBudget: raw.WorkBudget, maximumReturned: raw.MaximumReturned),
            GearRoutingStatus.Complete, diagnostics, (int)preparation);
    }

    internal static AnchoredGearRoutingRequest LocalRequest(AnchoredCompoundGearRoutingRequest r, AnchoredCompoundToothPair pair, GearRoutePoint site, int leg, int sign)
    {
        var d = leg == 0 ? r.InputLeg : r.OutputLeg;
        var input = leg == 0 ? r.Input : new GearRouteAnchor(site, pair.DrivingTeeth);
        var output = leg == 0 ? new GearRouteAnchor(site, pair.ReceivingTeeth) : r.Output;
        // A local contact graph is a 2D plane query. Physical layer labels are assigned only during whole-mechanism construction.
        return new AnchoredGearRoutingRequest(input, output, new Rational(sign * input.Teeth, output.Teeth), r.PitchRadiusTicksPerTooth, d.Bounds,
            d.Sites.Where(p => !p.Equals(site)), idlerTeeth: d.IdlerTeeth, minIdlers: d.MinIdlers, maxIdlers: d.MaxIdlers,
            unrelatedClearance: d.UnrelatedClearance, keepOutClearance: d.KeepOutClearance,
            keepOuts: r.KeepOuts.Where(k => k.AppliesTo(leg)).Select(k => new GearRouteRegion(k.Id, k.Bounds)), requiredRegions: d.RequiredRegions,
            preferredRegion: d.PreferredRegion, expansionBudget: r.WorkBudget, maximumReturned: r.MaximumReturned);
    }
}
