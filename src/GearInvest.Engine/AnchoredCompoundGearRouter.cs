using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed partial class AnchoredCompoundGearRouter
{
    private static bool RatioMatches(AnchoredCompoundGearRoutingRequest r, int a, int b) => a != b &&
        BigInteger.Abs(r.TargetTransfer.Numerator) * a * r.Output.Teeth == r.TargetTransfer.Denominator * r.Input.Teeth * b;

    public CompoundPairSynthesisResult SynthesizePairs(AnchoredCompoundGearRoutingRequest request, CancellationToken token = default)
    {
        var n = Normalize(request); var pairs = new List<AnchoredCompoundToothPair>(); int examined = 0;
        if (!n.IsValid) return new CompoundPairSynthesisResult(n, n.Status, examined, pairs);
        var r = n.Request!;
        foreach (int a in r.ReceivingTeeth) foreach (int b in r.DrivingTeeth)
        {
            if (token.IsCancellationRequested || examined == r.WorkBudget) return new CompoundPairSynthesisResult(n, token.IsCancellationRequested ? GearRoutingStatus.Cancelled : GearRoutingStatus.IncompleteBudget, examined, pairs);
            examined++; if (RatioMatches(r, a, b)) pairs.Add(new AnchoredCompoundToothPair(r.Input.Teeth, r.Output.Teeth, a, b));
        }
        return new CompoundPairSynthesisResult(n, pairs.Count == 0 ? GearRoutingStatus.Infeasible : GearRoutingStatus.Complete, examined, pairs);
    }

    public CompoundGearRoutingResult Generate(AnchoredCompoundGearRoutingRequest request, Func<GenerationCandidate, string> candidateIdFactory,
        CancellationToken token = default, Action<int, int>? progress = null)
        => GenerateObserved(request, candidateIdFactory, token, progress, null);

    internal CompoundGearRoutingResult GenerateObserved(AnchoredCompoundGearRoutingRequest request, Func<GenerationCandidate, string> candidateIdFactory,
        CancellationToken token, Action<int, int>? progress, Action<CompoundGearRouteCandidate>? candidateSink)
    {
        if (candidateIdFactory == null) throw new ArgumentNullException(nameof(candidateIdFactory));
        var n = Normalize(request); var kept = new List<CompoundGearRouteCandidate>(); var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempts = new List<CompoundRoutingPlacementAttempt>(); var details = new List<Diagnostic>(); int omitted = 0;
        int pairs = 0, ratioPairs = 0, sites = 0, nodes = 0, adjacency = 0, leftExp = 0, rightExp = 0, leftGoals = 0, rightGoals = 0, merges = 0, globalRejects = 0, valid = 0;
        bool remaining = false; string stopStage = "none", frontier = "";
        int Work() => pairs + sites + leftExp + rightExp + merges;
        void Reject(string code) { rejected.TryGetValue(code, out int count); rejected[code] = count + 1; }
        CompoundGearRoutingResult Finish(GearRoutingStatus status) => new CompoundGearRoutingResult(n, status,
            new CompoundRoutingSearchSummary(pairs, ratioPairs, sites, nodes, adjacency, leftExp, rightExp, leftGoals, rightGoals, merges, globalRejects, valid, remaining, stopStage, frontier),
            kept, rejected.Select(x => new GearRoutingRejection(x.Key, x.Value)), attempts, omitted, details);
        if (!n.IsValid) { details.AddRange(n.Diagnostics); return Finish(n.Status); }
        var r = n.Request!; var engine = new GenerationEngine();
        bool Charge(string stage, string where)
        {
            if (token.IsCancellationRequested || Work() == r.WorkBudget) { remaining = true; stopStage = stage; frontier = where; return false; }
            switch (stage) { case "toothPair": pairs++; break; case "compoundSite": sites++; break; case "inputSuccessor": leftExp++; break; case "outputSuccessor": rightExp++; break; case "merge": merges++; break; default: throw new InvalidOperationException("Unversioned work unit"); }
            progress?.Invoke(Work(), r.WorkBudget); return true;
        }
        bool Search()
        {
            foreach (int a in r.ReceivingTeeth) foreach (int b in r.DrivingTeeth)
            {
                var pair = new AnchoredCompoundToothPair(r.Input.Teeth, r.Output.Teeth, a, b);
                if (!Charge("toothPair", pair.PairId)) return false;
                if (!RatioMatches(r, a, b)) { Reject("ALGEBRAIC_PAIR"); continue; }
                ratioPairs++;
                foreach (var site in r.CompoundSites)
                {
                    int start = Work(), gl = leftGoals, gr = rightGoals, gm = merges, gj = globalRejects, gv = valid;
                    if (!Charge("compoundSite", GearRoutingContract.Pack(pair.PairId, site.Canonical))) return false;
                    bool complete = SearchSite(pair, site);
                    var attempt = new CompoundRoutingPlacementAttempt(pair.PairId, site, start, Work(), leftGoals - gl, rightGoals - gr, merges - gm, globalRejects - gj, valid - gv, complete);
                    if (attempts.Count < GearRoutingContract.MaxDetails) attempts.Add(attempt); else omitted++;
                    if (!complete) return false;
                }
            }
            return true;
        }
        bool SearchSite(AnchoredCompoundToothPair pair, GearRoutePoint site)
        {
            if (r.RequiredCompoundRegion != null && !GearRoutingGeometry.Contains(r.RequiredCompoundRegion, site)) { Reject("REQUIRED_COMPOUND_REGION"); return true; }
            FiniteGearContactGraph? Prepare(int leg) => FiniteGearContactGraph.Prepare(LocalRequest(r, pair, site, leg, 1), token,
                (_, issue) => { nodes++; if (issue != null) Reject((leg == 0 ? "INPUT_STATIC:" : "OUTPUT_STATIC:") + issue); }, _ => adjacency++);
            var left = Prepare(0); var right = Prepare(1);
            if (left == null || right == null) { remaining = true; stopStage = "preparation"; frontier = GearRoutingContract.Pack(pair.PairId, site.Canonical); return false; }
            if (!left.EndpointsAvailable || !right.EndpointsAvailable) { Reject("ENDPOINT_OR_COMPOUND_ENVIRONMENT"); return true; }
            foreach (int sign in new[] { -1, 1 })
            {
                int rightSign = sign * r.TargetTransfer.Sign;
                bool Permitted(CompoundRoutingLeg leg, int s) => Enumerable.Range(leg.MinIdlers, leg.MaxIdlers - leg.MinIdlers + 1).Any(k => ((k + 1) % 2 == 0 ? 1 : -1) == s);
                if (!Permitted(r.InputLeg, sign) || !Permitted(r.OutputLeg, rightSign)) { Reject("LEG_PARITY_INVARIANT"); continue; }
                if (!left.VisitRoutes(sign, (path, successor) => Charge("inputSuccessor", GearRoutingContract.Pack(pair.PairId, site.Canonical, GearRoutingContract.Number(sign),
                        GearRoutingContract.List(path.Select(i => left.Nodes[i].Canonical)), left.Nodes[successor].Canonical)), code => Reject("INPUT:" + code), lp => {
                    leftGoals++;
                    return right.VisitRoutes(rightSign, (path, successor) => Charge("outputSuccessor", GearRoutingContract.Pack(pair.PairId, site.Canonical, GearRoutingContract.List(lp.Select(x => x.Canonical)),
                            GearRoutingContract.List(path.Select(i => right.Nodes[i].Canonical)), right.Nodes[successor].Canonical)), code => Reject("OUTPUT:" + code), rp => {
                        rightGoals++;
                        if (!Charge("merge", GearRoutingContract.Pack(pair.PairId, GearRoutingContract.List(lp.Select(x => x.Canonical)), GearRoutingContract.List(rp.Select(x => x.Canonical))))) return false;
                        if (lp.Count + rp.Count - 4 > r.MaximumTotalIdlers) { globalRejects++; Reject("TOTAL_IDLERS"); return true; }
                        // Independent global generation rejects coincident axes across legs, even on different planes.
                        var built = engine.Generate(CompoundGearRouteValidator.Build(lp, rp, r.PitchRadiusTicksPerTooth));
                        if (!built.IsSuccess) { globalRejects++; foreach (var code in built.Diagnostics.Select(d => d.Code).Distinct(StringComparer.Ordinal)) Reject("GLOBAL:" + code); return true; }
                        var mechanism = built.Candidates.Single(); var id = candidateIdFactory(mechanism);
                        var validation = CompoundGearRouteValidator.Validate(r, pair, lp, rp, mechanism, id);
                        if (!validation.IsValid) { globalRejects++; foreach (var code in validation.Diagnostics.Select(d => d.Code).Distinct(StringComparer.Ordinal)) Reject("CONTEXT:" + code); return true; }
                        valid++; var candidate = new CompoundGearRouteCandidate(id, pair, lp, rp, mechanism, CompoundGearRouteValidator.Metrics(r, lp, rp), validation);
                        candidateSink?.Invoke(candidate); kept.Add(candidate);
                        kept.Sort(CompoundGearRouteValidator.Compare); if (kept.Count > r.MaximumReturned) kept.RemoveAt(kept.Count - 1);
                        return true;
                    });
                })) return false;
            }
            return true;
        }
        if (token.IsCancellationRequested) { remaining = true; stopStage = "beforeSearch"; return Finish(GearRoutingStatus.Cancelled); }
        Search();
        if (token.IsCancellationRequested) { remaining = true; if (stopStage == "none") stopStage = "afterSearch"; return Finish(GearRoutingStatus.Cancelled); }
        if (remaining) return Finish(GearRoutingStatus.IncompleteBudget);
        if (valid == 0) details.Add(Error(ratioPairs == 0 ? "ALGEBRAIC_DOMAIN_EXHAUSTED" : "SPATIAL_DOMAIN_EXHAUSTED", "domain",
            ratioPairs == 0 ? "No endpoint-conditioned magnitude pair in the complete finite options; routing was not run." : "Ratio-valid pairs exist but no merged mechanism survives the complete declared spatial domain."));
        return Finish(valid == 0 ? GearRoutingStatus.Infeasible : GearRoutingStatus.Complete);
    }
}
