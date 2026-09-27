using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed partial class AnchoredGearRouter
{
    internal static Diagnostic Error(string code, string subject, string detail) => new Diagnostic("ROUTING_" + code, DiagnosticSeverity.Error, detail, subject);
    public static GearRoutingNormalization Normalize(AnchoredGearRoutingRequest? raw) => NormalizeDomain(raw, false);
    // Serial profiles retain declared global-anchor sites so occupied-context pruning remains observable.
    internal static GearRoutingNormalization NormalizeDomain(AnchoredGearRoutingRequest? raw, bool preserveAnchorSites)
    {
        var diagnostics = new List<Diagnostic>(); var status = GearRoutingStatus.InvalidInput;
        void Invalid(string field, string detail) => diagnostics.Add(Error("INVALID_INPUT", field, detail));
        void Limit(string field) => diagnostics.Add(Error("DOMAIN_LIMIT", field, "Finite input exceeds the versioned resource ceiling; no allocation/search performed."));
        void Unsupported(string field) { status = GearRoutingStatus.Unsupported; diagnostics.Add(Error("UNSUPPORTED", field, "Outside the declared single-layer simple external-idler profile.")); }
        bool Scalar(BigInteger x) => BigInteger.Abs(x) <= GearRoutingContract.MaxScalar;
        bool Point(GearRoutePoint p) => Scalar(p.X) && Scalar(p.Y);
        bool Box(GearRouteBox? b) => b != null && b.IsOrdered && Scalar(b.MinX) && Scalar(b.MinY) && Scalar(b.MaxX) && Scalar(b.MaxY);
        if (raw == null) return new GearRoutingNormalization(null, status, new[] { Error("INVALID_INPUT", "request", "A request is required.") });
        if (raw.IngestionLimitExceeded) Limit("rawCollections");
        if (raw.Profile != GearRoutingContract.Profile || raw.Backend != GearRoutingContract.Backend || raw.Ranking != GearRoutingContract.Ranking) Unsupported("profile/backend/ranking");
        if (raw.Unit != "tick" || raw.Topology != "simple-external-idler-chain" || raw.Layer != 0) Unsupported("unit/topology/layer");
        if (raw.Input == null || raw.Output == null) Invalid("anchors", "Both endpoint anchors are required.");
        else
        {
            if (!Point(raw.Input.Position) || !Point(raw.Output.Position)) Limit("anchors");
            if (raw.Input.Position.Equals(raw.Output.Position)) Invalid("anchors", "Endpoint axis positions must be distinct.");
            if (raw.Input.Teeth <= 0 || raw.Output.Teeth <= 0) Invalid("endpointTeeth", "Positive integer tooth counts are required.");
            if (raw.Input.Teeth > GearRoutingContract.MaxTeeth || raw.Output.Teeth > GearRoutingContract.MaxTeeth) Limit("endpointTeeth");
        }
        if (!Box(raw.Bounds)) Invalid("bounds", "Ordered finite integer bounds within the coordinate ceiling are required.");
        if (raw.TargetTransfer == Rational.Zero) Invalid("targetTransfer", "A nonzero signed transfer is required.");
        if (!Scalar(raw.TargetTransfer.Numerator) || !Scalar(raw.TargetTransfer.Denominator)) Limit("targetTransfer");
        if (raw.PitchRadiusTicksPerTooth <= 0) Invalid("pitchRadiusTicksPerTooth", "Positive integer pitch-radius ticks per tooth are required.");
        if (raw.PitchRadiusTicksPerTooth > GearRoutingContract.MaxPitchScale) Limit("pitchRadiusTicksPerTooth");
        if (raw.MinIdlers < 0 || raw.MaxIdlerCount < raw.MinIdlers) Invalid("idlerCount", "An ordered nonnegative idler interval is required.");
        if (raw.MaxIdlerCount > GearRoutingContract.MaxIdlers) Unsupported("idlerCount");
        if (raw.UnrelatedClearance < 0 || raw.KeepOutClearance < 0) Invalid("clearance", "Clearance must be nonnegative.");
        if (!Scalar(raw.UnrelatedClearance) || !Scalar(raw.KeepOutClearance)) Limit("clearance");
        if (raw.ExpansionBudget < 0 || raw.ExpansionBudget > GearRoutingContract.MaxExpansions) Limit("expansionBudget");
        if (raw.MaximumReturned < 1 || raw.MaximumReturned > GearRoutingContract.MaxReturned) Limit("maximumReturned");
        if (raw.HasExplicitSites == (raw.Grid != null)) Invalid("sites/grid", "Specify exactly one explicit finite site list or grid.");
        if (raw.IdlerTeeth.Any(t => t <= 0)) Invalid("idlerTeeth", "Every tooth option must be a positive integer.");
        if (raw.IdlerTeeth.Any(t => t > GearRoutingContract.MaxTeeth)) Limit("idlerTeeth");
        var teeth = raw.IdlerTeeth.Distinct().OrderBy(t => t).ToArray();
        if (teeth.Length > GearRoutingContract.MaxToothOptions) Limit("idlerTeeth");
        if (teeth.Length == 0 && raw.MaxIdlerCount != 0) Invalid("idlerTeeth", "Idlers require at least one tooth option.");
        foreach (var regions in new[] { raw.KeepOuts, raw.RequiredRegions })
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in regions)
            {
                if (region == null || string.IsNullOrWhiteSpace(region.Id) || region.Id.Length > 64 || !region.Id.All(c => char.IsLetterOrDigit(c) && c < 128 || c == '-' || c == '_' || c == ':') || !Box(region.Bounds))
                { Invalid("region", "Stable ASCII ID and ordered finite rectangle required."); continue; }
                if (!ids.Add(region.Id)) Invalid("region", "Duplicate region identity.");
            }
        }
        if (raw.PreferredRegion != null && !Box(raw.PreferredRegion)) Invalid("preferredRegion", "Invalid preferred rectangle.");
        var sites = new List<GearRoutePoint>();
        if (raw.Grid != null)
        {
            var g = raw.Grid;
            if (!Point(g.Origin) || !Point(g.Step) || g.Step.X <= 0 || g.Step.Y <= 0 || !Box(g.Bounds)) Invalid("grid", "Finite origin/bounds and positive exact steps are required.");
            else
            {
                var minX = Ceil(g.Bounds.MinX - g.Origin.X, g.Step.X); var maxX = Floor(g.Bounds.MaxX - g.Origin.X, g.Step.X);
                var minY = Ceil(g.Bounds.MinY - g.Origin.Y, g.Step.Y); var maxY = Floor(g.Bounds.MaxY - g.Origin.Y, g.Step.Y);
                var count = BigInteger.Max(0, maxX - minX + 1) * BigInteger.Max(0, maxY - minY + 1);
                if (count > GearRoutingContract.MaxSites) Limit("gridSites");
                else if (count > 0 && diagnostics.Count == 0) for (var x = minX; x <= maxX; x++) for (var y = minY; y <= maxY; y++) sites.Add(new GearRoutePoint(g.Origin.X + x * g.Step.X, g.Origin.Y + y * g.Step.Y));
            }
        }
        else sites.AddRange(raw.Sites);
        if (sites.Any(p => !Point(p))) Limit("sites");
        var normalizedSites = sites.Distinct().Where(p => preserveAnchorSites || raw.Input == null || raw.Output == null || !p.Equals(raw.Input.Position) && !p.Equals(raw.Output.Position)).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (normalizedSites.Length > GearRoutingContract.MaxSites) Limit("sites");
        var nodes = new BigInteger(normalizedSites.Length) * teeth.Length + 2;
        if (nodes > GearRoutingContract.MaxNodes || nodes * (nodes - 1) / 2 > GearRoutingContract.MaxPairChecks) Limit("potentialNodes/adjacencyChecks");
        if (diagnostics.Count != 0) return new GearRoutingNormalization(null, status, diagnostics);
        var request = new AnchoredGearRoutingRequest(raw.Input!, raw.Output!, raw.TargetTransfer, raw.PitchRadiusTicksPerTooth, raw.Bounds,
            sites: normalizedSites, idlerTeeth: teeth, minIdlers: raw.MinIdlers, maxIdlers: raw.MaxIdlerCount,
            unrelatedClearance: raw.UnrelatedClearance, keepOutClearance: raw.KeepOutClearance,
            keepOuts: raw.KeepOuts.OrderBy(r => r.Id, StringComparer.Ordinal), requiredRegions: raw.RequiredRegions.OrderBy(r => r.Id, StringComparer.Ordinal),
            preferredRegion: raw.PreferredRegion, expansionBudget: raw.ExpansionBudget, maximumReturned: raw.MaximumReturned);
        return new GearRoutingNormalization(request, GearRoutingStatus.Complete, diagnostics);
    }
    private static BigInteger Floor(BigInteger x, BigInteger positiveDivisor)
    { var q = BigInteger.DivRem(x, positiveDivisor, out var rem); return rem.Sign < 0 ? q - 1 : q; }
    private static BigInteger Ceil(BigInteger x, BigInteger positiveDivisor) => -Floor(-x, positiveDivisor);
}
