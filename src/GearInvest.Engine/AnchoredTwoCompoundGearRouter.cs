using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed partial class AnchoredTwoCompoundGearRouter
{
    private static IEnumerable<TwoCompoundToothAssignment> Assignments(AnchoredTwoCompoundGearRoutingRequest r)
    {
        foreach (int a1 in r.Compounds[0].ReceivingTeeth) foreach (int b1 in r.Compounds[0].DrivingTeeth)
        foreach (int a2 in r.Compounds[1].ReceivingTeeth) foreach (int b2 in r.Compounds[1].DrivingTeeth)
            yield return new TwoCompoundToothAssignment(r.Input.Teeth, r.Output.Teeth, a1, b1, a2, b2);
    }
    private static bool RatioMatches(AnchoredTwoCompoundGearRoutingRequest r, TwoCompoundToothAssignment a) =>
        a.Receiving(0) != a.Driving(0) && a.Receiving(1) != a.Driving(1) &&
        BigInteger.Abs(r.TargetTransfer.Numerator) * a.Receiving(0) * a.Receiving(1) * r.Output.Teeth ==
        r.TargetTransfer.Denominator * r.Input.Teeth * a.Driving(0) * a.Driving(1);

    public TwoCompoundAssignmentSynthesisResult SynthesizeAssignments(AnchoredTwoCompoundGearRoutingRequest request, CancellationToken token = default)
    {
        var n = Normalize(request); var found = new List<TwoCompoundToothAssignment>(); int examined = 0;
        if (!n.IsValid) return new TwoCompoundAssignmentSynthesisResult(n, n.Status, examined, found);
        foreach (var a in Assignments(n.Request!))
        {
            if (token.IsCancellationRequested || examined == n.Request!.WorkBudget)
                return new TwoCompoundAssignmentSynthesisResult(n, token.IsCancellationRequested ? GearRoutingStatus.Cancelled : GearRoutingStatus.IncompleteBudget, examined, found);
            examined++; if (RatioMatches(n.Request!, a)) found.Add(a);
        }
        return new TwoCompoundAssignmentSynthesisResult(n, found.Count == 0 ? GearRoutingStatus.Infeasible : GearRoutingStatus.Complete, examined, found);
    }

    public TwoCompoundGearRoutingResult Generate(AnchoredTwoCompoundGearRoutingRequest request, Func<GenerationCandidate, string> candidateIdFactory,
        CancellationToken token = default, Action<int, int>? progress = null)
        => GenerateObserved(request, candidateIdFactory, token, progress, null);

    internal TwoCompoundGearRoutingResult GenerateObserved(AnchoredTwoCompoundGearRoutingRequest request, Func<GenerationCandidate, string> candidateIdFactory,
        CancellationToken token, Action<int, int>? progress, Action<TwoCompoundGearRouteCandidate>? candidateSink)
    {
        if (candidateIdFactory == null) throw new ArgumentNullException(nameof(candidateIdFactory));
        var n = Normalize(request); var kept = new List<TwoCompoundGearRouteCandidate>(); var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempts = new List<TwoCompoundRoutingPlacementAttempt>(); var details = new List<Diagnostic>(); int omitted = 0;
        int assignments = 0, ratioValid = 0, sitePairs = 0, nodes = 0, adjacency = 0, early = 0, merges = 0, globalRejects = 0, valid = 0;
        var expansions = new int[3]; var observed = new int[3]; bool remaining = false; string stopStage = "none", frontier = "";
        int Work() => assignments + sitePairs + expansions.Sum() + merges;
        void Reject(string code) { rejected.TryGetValue(code, out int count); rejected[code] = count + 1; }
        TwoCompoundGearRoutingResult Finish(GearRoutingStatus status) => new TwoCompoundGearRoutingResult(n, status,
            new TwoCompoundRoutingSearchSummary(assignments, ratioValid, sitePairs, nodes, adjacency, expansions, observed, early, merges, globalRejects, valid, remaining, stopStage, frontier),
            kept, rejected.Select(x => new GearRoutingRejection(x.Key, x.Value)), attempts, omitted, details);
        if (!n.IsValid) { details.AddRange(n.Diagnostics); return Finish(n.Status); }
        var r = n.Request!; var engine = new GenerationEngine();
        bool Charge(string stage, string where, int leg = -1)
        {
            if (token.IsCancellationRequested || Work() == r.WorkBudget) { remaining = true; stopStage = stage; frontier = where; return false; }
            if (leg >= 0) expansions[leg]++;
            else switch (stage) { case "assignment": assignments++; break; case "sitePair": sitePairs++; break; case "merge": merges++; break; default: throw new InvalidOperationException("Unversioned work unit"); }
            progress?.Invoke(Work(), r.WorkBudget); return true;
        }
        bool SearchPlacement(TwoCompoundToothAssignment a, GearRoutePoint[] sites)
        {
            string context = GearRoutingContract.Pack(a.AssignmentId, sites[0].Canonical, sites[1].Canonical);
            var reserved = new[] { r.Input.Position, sites[0], sites[1], r.Output.Position };
            if (reserved.Distinct().Count() != 4) { Reject("RESERVED_AXIS_COLLISION"); return true; }
            for (int slot = 0; slot < 2; slot++)
                if (r.Compounds[slot].RequiredRegion != null && !GearRoutingGeometry.Contains(r.Compounds[slot].RequiredRegion!, sites[slot])) { Reject("REQUIRED_SLOT_REGION"); return true; }
            var graphs = new FiniteGearContactGraph[3];
            for (int leg = 0; leg < 3; leg++)
            {
                var graph = FiniteGearContactGraph.Prepare(LocalRequest(r, a, sites, leg), token,
                    (_, issue) => { nodes++; if (issue != null) Reject("LEG" + leg + "_STATIC:" + issue); }, _ => adjacency++);
                if (graph == null) { remaining = true; stopStage = "preparation"; frontier = context; return false; }
                graphs[leg] = graph;
            }
            if (graphs.Any(g => !g.EndpointsAvailable)) { Reject("ENDPOINT_OR_COMPOUND_ENVIRONMENT"); return true; }
            var selected = new IReadOnlyList<GearRouteAssignment>[3];
            bool Merge()
            {
                if (!Charge("merge", GearRoutingContract.Pack(context, GearRoutingContract.List(selected.Select(p => GearRoutingContract.List(p.Select(x => x.Canonical))))))) return false;
                if (selected.Sum(p => p.Count - 2) > r.MaximumTotalIdlers) { globalRejects++; Reject("TOTAL_IDLERS"); return true; }
                var generated = engine.Generate(TwoCompoundGearRouteValidator.Build(selected, r.PitchRadiusTicksPerTooth));
                if (!generated.IsSuccess) { globalRejects++; foreach (var code in generated.Diagnostics.Select(d => d.Code).Distinct(StringComparer.Ordinal)) Reject("GLOBAL:" + code); return true; }
                var mechanism = generated.Candidates.Single(); var id = candidateIdFactory(mechanism);
                var validation = TwoCompoundGearRouteValidator.Validate(r, a, selected, mechanism, id);
                if (!validation.IsValid) { globalRejects++; foreach (var code in validation.Diagnostics.Select(d => d.Code).Distinct(StringComparer.Ordinal)) Reject("CONTEXT:" + code); return true; }
                valid++; var candidate = new TwoCompoundGearRouteCandidate(id, a, selected, mechanism, TwoCompoundGearRouteValidator.Metrics(r, selected), validation);
                candidateSink?.Invoke(candidate); kept.Add(candidate);
                kept.Sort(TwoCompoundGearRouteValidator.Compare); if (kept.Count > r.MaximumReturned) kept.RemoveAt(kept.Count - 1);
                return true;
            }
            bool VisitLeg(int leg, int[] signs)
            {
                if (leg == 3) return Merge();
                var graph = graphs[leg];
                return graph.VisitRoutes(signs[leg], (path, next) => Charge("leg" + leg + "Successor", GearRoutingContract.Pack(context,
                        GearRoutingContract.List(signs.Select(x => GearRoutingContract.Number(x))),
                        GearRoutingContract.List(selected.Take(leg).Select(p => GearRoutingContract.List(p.Select(x => x.Canonical)))),
                        GearRoutingContract.List(path.Select(i => graph.Nodes[i].Canonical)), graph.Nodes[next].Canonical), leg),
                    code => Reject("LEG" + leg + ":" + code), path => {
                        observed[leg]++;
                        var occupied = new HashSet<GearRoutePoint>(reserved);
                        foreach (var prior in selected.Take(leg)) foreach (var x in prior.Skip(1).Take(prior.Count - 2)) occupied.Add(x.Position);
                        if (path.Skip(1).Take(path.Count - 2).Any(x => occupied.Contains(x.Position))) { early++; Reject("GLOBAL_OCCUPIED_AXIS"); return true; }
                        selected[leg] = path;
                        // No local top-K: a later local route may be the only globally valid continuation.
                        return VisitLeg(leg + 1, signs);
                    });
            }
            bool Permitted(int leg, int sign) => Enumerable.Range(r.Legs[leg].MinIdlers, r.Legs[leg].MaxIdlers - r.Legs[leg].MinIdlers + 1)
                .Any(k => ((k + 1) % 2 == 0 ? 1 : -1) == sign);
            foreach (int s0 in new[] { -1, 1 }) foreach (int s1 in new[] { -1, 1 }) foreach (int s2 in new[] { -1, 1 })
            {
                if (s0 * s1 * s2 != r.TargetTransfer.Sign) continue;
                if (!Permitted(0, s0) || !Permitted(1, s1) || !Permitted(2, s2)) { Reject("LEG_PARITY_INVARIANT"); continue; }
                if (!VisitLeg(0, new[] { s0, s1, s2 })) return false;
            }
            return true;
        }
        bool Search()
        {
            foreach (var a in Assignments(r))
            {
                if (!Charge("assignment", a.AssignmentId)) return false;
                if (!RatioMatches(r, a)) { Reject("ALGEBRAIC_ASSIGNMENT"); continue; } ratioValid++;
                foreach (var s0 in r.Compounds[0].Sites) foreach (var s1 in r.Compounds[1].Sites)
                {
                    var sites = new[] { s0, s1 }; int start = Work(), ge = early, gm = merges, gj = globalRejects, gv = valid; var goals = observed.ToArray();
                    if (!Charge("sitePair", GearRoutingContract.Pack(a.AssignmentId, s0.Canonical, s1.Canonical))) return false;
                    bool complete = SearchPlacement(a, sites);
                    var attempt = new TwoCompoundRoutingPlacementAttempt(a.AssignmentId, sites, start, Work(), observed.Select((x, i) => x - goals[i]), early - ge, merges - gm, globalRejects - gj, valid - gv, complete);
                    if (attempts.Count < GearRoutingContract.MaxDetails) attempts.Add(attempt); else omitted++;
                    if (!complete) return false;
                }
            }
            return true;
        }
        if (token.IsCancellationRequested) { remaining = true; stopStage = "beforeSearch"; return Finish(GearRoutingStatus.Cancelled); }
        Search();
        if (token.IsCancellationRequested) { remaining = true; if (stopStage == "none") stopStage = "afterSearch"; return Finish(GearRoutingStatus.Cancelled); }
        if (remaining) return Finish(GearRoutingStatus.IncompleteBudget);
        if (valid == 0) details.Add(Error(ratioValid == 0 ? "ALGEBRAIC_DOMAIN_EXHAUSTED" : "SPATIAL_DOMAIN_EXHAUSTED", "domain",
            ratioValid == 0 ? "No exact two-ratio-changing assignment in the complete finite options; routing was not run." : "Exact assignments exist, but no whole mechanism survives the complete declared spatial domain."));
        return Finish(valid == 0 ? GearRoutingStatus.Infeasible : GearRoutingStatus.Complete);
    }
}
