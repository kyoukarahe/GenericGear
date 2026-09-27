using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed partial class AnchoredTwoCompoundGearRouter
{
    internal static Diagnostic Error(string code, string subject, string message) => new Diagnostic("TWO_COMPOUND_ROUTING_" + code, DiagnosticSeverity.Error, message, subject);
    public static TwoCompoundRoutingNormalization Normalize(AnchoredTwoCompoundGearRoutingRequest? raw)
    {
        var ds = new List<Diagnostic>(); var status = GearRoutingStatus.InvalidInput;
        void Bad(string field, string why) => ds.Add(Error("INVALID_INPUT", field, why));
        void Limit(string field) => ds.Add(Error("DOMAIN_LIMIT", field, "Bounded ingestion/preparation ceiling exceeded; no search performed."));
        bool Box(GearRouteBox b) => b.IsOrdered && new[] { b.MinX, b.MinY, b.MaxX, b.MaxY }.All(x => BigInteger.Abs(x) <= GearRoutingContract.MaxScalar);
        if (raw == null) return new TwoCompoundRoutingNormalization(null, status, new[] { Error("INVALID_INPUT", "request", "Request required.") });
        if (raw.IngestionLimitExceeded || raw.Compounds.Any(c => c?.IngestionLimitExceeded == true) || raw.Legs.Any(l => l?.IngestionLimitExceeded == true)) Limit("rawCollections");
        if (raw.Profile != TwoCompoundRoutingContract.Profile || raw.Backend != TwoCompoundRoutingContract.Backend || raw.Normalization != TwoCompoundRoutingContract.Normalization || raw.Ranking != TwoCompoundRoutingContract.Ranking || raw.Unit != "tick" || raw.InputLayer != 0 || raw.OutputLayer != 2)
        { status = GearRoutingStatus.Unsupported; ds.Add(Error("UNSUPPORTED", "profile", "Only the ordered two-compound/three-plane versioned profile is supported.")); }
        if (raw.Input == null || raw.Output == null || raw.Compounds.Count != 2 || raw.Legs.Count != 3 || raw.Compounds.Any(c => c == null) || raw.Legs.Any(l => l == null)) Bad("roles", "Two anchors, two ordered compound slots and three ordered leg domains required.");
        if (raw.MaximumTotalIdlers < 0 || raw.MaximumTotalIdlers > 15) Bad("maximumTotalIdlers", "Total idlers must be bounded by 0..15.");
        if (raw.KeepOuts.Any(k => k == null || (int)k.Layers < 1 || (int)k.Layers > 7)) Bad("keepOuts", "Nonempty subset of logical planes 0/1/2 required.");
        if (ds.Count != 0) return new TwoCompoundRoutingNormalization(null, status, ds);
        var obstacles = raw.KeepOuts.Select(k => new GearRouteRegion(k.Id, k.Bounds)).ToArray();
        // Reuse 15A's finite integer domains, overflow-safe grid expansion, ID/rectangle and per-leg ceilings.
        GearRoutingNormalization Plane(CompoundRoutingLeg l) => AnchoredGearRouter.NormalizeDomain(new AnchoredGearRoutingRequest(raw.Input!, raw.Output!, raw.TargetTransfer,
            raw.PitchRadiusTicksPerTooth, l.Bounds, l.HasExplicitSites ? l.Sites : null, l.Grid, l.IdlerTeeth, l.MinIdlers, l.MaxIdlers,
            l.UnrelatedClearance, l.KeepOutClearance, obstacles, l.RequiredRegions, l.PreferredRegion, raw.WorkBudget, raw.MaximumReturned), true);
        var legs = new List<CompoundRoutingLeg>(); var slots = new List<CompoundRoutingSlot>();
        foreach (var l in raw.Legs)
        {
            var n = Plane(l); ds.AddRange(n.Diagnostics); if (n.Status == GearRoutingStatus.Unsupported) status = GearRoutingStatus.Unsupported;
            if (n.Request != null) { var p = n.Request; legs.Add(new CompoundRoutingLeg(p.Bounds, p.Sites, idlerTeeth: p.IdlerTeeth, minIdlers: p.MinIdlers, maxIdlers: p.MaxIdlerCount,
                unrelatedClearance: p.UnrelatedClearance, keepOutClearance: p.KeepOutClearance, requiredRegions: p.RequiredRegions, preferredRegion: p.PreferredRegion)); }
        }
        foreach (var s in raw.Compounds)
        {
            int[] Teeth(IEnumerable<int> ts)
            {
                var t = ts.Distinct().OrderBy(x => x).ToArray(); if (t.Length == 0 || t.Any(x => x <= 0)) Bad("slotTeeth", "Nonempty positive options required.");
                if (t.Length > TwoCompoundRoutingContract.MaxSlotTeeth || t.Any(x => x > GearRoutingContract.MaxTeeth)) Limit("slotTeeth"); return t;
            }
            var a = Teeth(s.ReceivingTeeth); var b = Teeth(s.DrivingTeeth);
            if (s.RequiredRegion != null && !Box(s.RequiredRegion) || s.PreferredRegion != null && !Box(s.PreferredRegion)) Bad("slotRegions", "Ordered finite rectangle required.");
            var n = Plane(new CompoundRoutingLeg(raw.Legs[0].Bounds, s.HasExplicitSites ? s.Sites : null, s.Grid, Array.Empty<int>(), maxIdlers: 0));
            ds.AddRange(n.Diagnostics); if (n.Request?.Sites.Count > TwoCompoundRoutingContract.MaxSlotSites) Limit("slotSites");
            if (n.Request != null) slots.Add(new CompoundRoutingSlot(a, b, n.Request.Sites, requiredRegion: s.RequiredRegion, preferredRegion: s.PreferredRegion));
        }
        if (ds.Count != 0) return new TwoCompoundRoutingNormalization(null, status, ds);
        var assignments = new BigInteger(slots[0].ReceivingTeeth.Count) * slots[0].DrivingTeeth.Count * slots[1].ReceivingTeeth.Count * slots[1].DrivingTeeth.Count;
        var graphChecks = legs.Aggregate(BigInteger.Zero, (sum, l) => { var nodes = new BigInteger(l.Sites.Count) * l.IdlerTeeth.Count + 2; return sum + nodes * (nodes + 1) / 2; });
        var prep = assignments * slots[0].Sites.Count * slots[1].Sites.Count * graphChecks;
        if (prep > TwoCompoundRoutingContract.MaxPreparationChecks) { Limit("wholeRequestPreparation"); return new TwoCompoundRoutingNormalization(null, status, ds); }
        return new TwoCompoundRoutingNormalization(new AnchoredTwoCompoundGearRoutingRequest(raw.Input!, raw.Output!, raw.TargetTransfer, raw.PitchRadiusTicksPerTooth,
            slots, legs, raw.MaximumTotalIdlers, raw.KeepOuts.OrderBy(k => k.Id, StringComparer.Ordinal), raw.WorkBudget, raw.MaximumReturned), GearRoutingStatus.Complete, ds, (int)prep);
    }

    internal static AnchoredGearRoutingRequest LocalRequest(AnchoredTwoCompoundGearRoutingRequest r, TwoCompoundToothAssignment a, IReadOnlyList<GearRoutePoint> sites, int leg)
    {
        var l = r.Legs[leg]; var start = leg == 0 ? r.Input : new GearRouteAnchor(sites[leg - 1], a.Driving(leg - 1));
        var end = leg == 2 ? r.Output : new GearRouteAnchor(sites[leg], a.Receiving(leg));
        // Graphs contain no occupied-history pruning. The serial traversal supplies global context after each local route.
        return new AnchoredGearRoutingRequest(start, end, new Rational(start.Teeth, end.Teeth), r.PitchRadiusTicksPerTooth, l.Bounds,
            l.Sites.Where(p => !p.Equals(start.Position) && !p.Equals(end.Position)), idlerTeeth: l.IdlerTeeth, minIdlers: l.MinIdlers, maxIdlers: l.MaxIdlers,
            unrelatedClearance: l.UnrelatedClearance, keepOutClearance: l.KeepOutClearance,
            keepOuts: r.KeepOuts.Where(k => k.AppliesTo(leg)).Select(k => new GearRouteRegion(k.Id, k.Bounds)), requiredRegions: l.RequiredRegions,
            preferredRegion: l.PreferredRegion, expansionBudget: r.WorkBudget, maximumReturned: r.MaximumReturned);
    }
}
