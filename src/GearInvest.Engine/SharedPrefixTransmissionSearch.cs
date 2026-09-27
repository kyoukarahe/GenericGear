using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class SharedPrefixTransmissionSearch
{
    public SharedPrefixTransmissionResult Generate(SharedPrefixTransmissionGoal goal, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolBytes, Func<SharedPrefixTransmissionResult, int> resultBytes,
        CancellationToken token = default, Action<int, int>? progress = null)
        => GenerateObserved(goal, identity, poolBytes, resultBytes, token, progress, null);

    // The observer does not replace the original collector, counts, transcript or retained cap.
    internal SharedPrefixTransmissionResult GenerateObserved(SharedPrefixTransmissionGoal goal, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolBytes, Func<SharedPrefixTransmissionResult, int> resultBytes,
        CancellationToken token, Action<int, int>? progress, Action<SharedPrefixTransmissionCandidate>? observer)
    {
        var plan = SharedPrefixTransmissionCompiler.Compile(goal); var observations = new List<SharedPrefixHypothesisObservation>();
        var kept = new List<SharedPrefixTransmissionCandidate>(); var unique = new Dictionary<string, string>(StringComparer.Ordinal);
        var details = new List<SharedPrefixTripleObservation>(); var ds = new List<Diagnostic>(plan.Diagnostics);
        string transcript = GearRoutingContract.Sha(Array.Empty<byte>()), resource = ""; int indexBytes = 0;
        SharedPrefixTransmissionResult Snapshot(SharedPrefixTransmissionStatus s = SharedPrefixTransmissionStatus.IncompleteBudget, string reason = "")
            => new SharedPrefixTransmissionResult(plan, s, observations, kept, unique.Count, details, transcript, reason, ds);
        if (!plan.IsSupported) return Snapshot(plan.Status, plan.LimitingReason);
        var g = plan.Normalized.Goal!;
        foreach (var h in plan.Hypotheses)
        {
            var stages = new List<SharedPrefixStageObservation>(); int hi = observations.Count;
            observations.Add(new SharedPrefixHypothesisObservation(h.HypothesisId, stages, 0, 0, h.ExclusionProof));
            if (h.ExclusionProof.Length != 0) continue;
            var stagePlans = new[] { new SharedDriverBranchPlan("prefix", h.Prefix!) }
                .Concat(h.Suffix!.Branches.Select(b => new SharedDriverBranchPlan("output:" + b.OutputKey, b.Plan))).ToArray();
            foreach (var stage in stagePlans)
            {
                var empty = stages.FirstOrDefault(s => s.ProvenEmpty);
                string skipped = empty != null ? "EXACT_EMPTY_FACTOR:" + empty.Role : token.IsCancellationRequested ? "CANCELLED_BEFORE_STAGE" : "";
                int usedBytes = observations.Sum(o => o.Stages.Sum(s => s.Observation?.PoolBytes ?? 0));
                int allowance = Math.Min(g.PoolByteLimit, g.TotalPoolByteLimit - usedBytes);
                if (skipped.Length == 0 && (allowance <= 0 || resource.Length != 0)) skipped = "RESOURCE_BEFORE_STAGE";
                if (skipped.Length == 0 && resultBytes(Snapshot()) > SharedPrefixTransmissionContract.MaxResultBytes - SharedPrefixTransmissionContract.ResultReserveBytes)
                { resource = "RESULT_BYTE_RESERVE_CEILING"; skipped = "RESOURCE_BEFORE_STAGE"; }
                if (skipped == "RESOURCE_BEFORE_STAGE" && resource.Length == 0) resource = "TOTAL_POOL_BYTE_CEILING";
                if (skipped.Length != 0) stages.Add(new SharedPrefixStageObservation(stage.OutputKey, null, skipped));
                else
                {
                    var branch = SharedDriverTransmissionSearch.ObserveBranch(stage, identity, poolBytes, g.PoolCountLimit, allowance,
                        g.TotalWorkBudget, observations.Sum(o => o.Stages.Sum(s => s.Consumed)), token, progress);
                    stages.Add(new SharedPrefixStageObservation(stage.OutputKey, branch));
                }
                observations[hi] = new SharedPrefixHypothesisObservation(h.HypothesisId, stages, 0, 0, EmptyProof(stages));
            }
        }
        int examined = 0; bool stopped = false; int priorStamp = -1, measured = 0;
        for (int hi = 0; hi < observations.Count; hi++)
        {
            var current = observations[hi]; if (current.ClosureProof.Length != 0 || current.Stages.Count != 3 || current.Stages.Any(s => s.Observation == null)) continue;
            int he = 0, ha = 0;
            foreach (var p in current.Stages[0].Observation!.Pool)
            {
                foreach (var a in current.Stages[1].Observation!.Pool)
                {
                    foreach (var b in current.Stages[2].Observation!.Pool)
                    {
                        if (token.IsCancellationRequested || examined >= g.ReservedJoinWork || examined >= g.TripleDomainLimit) { stopped = true; break; }
                        if (unique.Count >= SharedPrefixTransmissionContract.MaxMergedIndex || indexBytes > SharedDriverTransmissionContract.MaxMergedIndexBytes - SharedDriverTransmissionContract.MaxMergedSignatureBytes)
                        { resource = "MERGED_INDEX_CEILING"; stopped = true; break; }
                        int stamp = unique.Count * 65 + details.Count;
                        if (stamp != priorStamp) { measured = resultBytes(Snapshot()); priorStamp = stamp; }
                        if (measured > SharedPrefixTransmissionContract.MaxResultBytes - SharedPrefixTransmissionContract.ResultReserveBytes)
                        { resource = "RESULT_BYTE_RESERVE_CEILING"; stopped = true; break; }
                        examined++; he++; SharedPrefixCompositionResult joined;
                        try { joined = SharedPrefixTransmissionComposer.Compose(plan, current.HypothesisId, p, a, b, identity); }
                        catch (Exception e)
                        {
                            var d = SharedPrefixTransmissionCompiler.Error("TRIPLE_FAILURE", current.HypothesisId, e.GetType().Name + ":" + e.Message); ds.Add(d);
                            joined = new SharedPrefixCompositionResult(null, new[] { d }); stopped = true;
                        }
                        var entry = new SharedPrefixTripleObservation(current.HypothesisId, p.CandidateId, a.CandidateId, b.CandidateId, joined.Candidate?.CandidateId, joined.Diagnostics);
                        transcript = NextTranscript(transcript, entry); if (details.Count < SharedPrefixTransmissionContract.MaxDetails) details.Add(entry);
                        if (joined.IsValid)
                        {
                            ha++; var c = joined.Candidate!; var signature = TransmissionGoalValidator.MechanicalSignature(c.Mechanism);
                            observer?.Invoke(c);
                            if (unique.TryGetValue(c.CandidateId, out var prior))
                            { if (prior != signature) { ds.Add(SharedPrefixTransmissionCompiler.Error("IDENTITY_COLLISION", c.CandidateId, "Different whole primitive payloads have the same identity.")); stopped = true; } }
                            else
                            {
                                unique.Add(c.CandidateId, signature); indexBytes += Encoding.UTF8.GetByteCount(signature) + c.CandidateId.Length;
                                kept.Add(c); kept.Sort(SharedPrefixTransmissionComposer.Compare); if (kept.Count > g.MaximumReturned) kept.RemoveAt(kept.Count - 1);
                            }
                        }
                        observations[hi] = new SharedPrefixHypothesisObservation(current.HypothesisId, current.Stages, he, ha, current.ClosureProof);
                        progress?.Invoke(observations.Sum(o => o.Stages.Sum(s => s.Consumed)) + examined, g.TotalWorkBudget);
                        if (stopped) break;
                    }
                    if (stopped) break;
                }
                if (stopped) break;
            }
            if (stopped) break;
        }
        var verdict = Aggregate(plan, observations, token.IsCancellationRequested, ds.Any(d => d.Severity == DiagnosticSeverity.Error), resource);
        return Snapshot(verdict.Status, verdict.Reason);
    }
    internal static string EmptyProof(IEnumerable<SharedPrefixStageObservation> stages)
    {
        var empty = stages.FirstOrDefault(s => s.ProvenEmpty);
        return empty == null ? "" : empty.Role + ":ALL_INCLUDED_FAMILIES_CLOSED_WITH_NO_CANDIDATE";
    }
    internal static string NextTranscript(string previous, SharedPrefixTripleObservation t) => GearRoutingContract.Sha(Encoding.UTF8.GetBytes(GearRoutingContract.Pack(previous, t.Canonical)));
    internal static (SharedPrefixTransmissionStatus Status, string Reason) Aggregate(SharedPrefixTransmissionSearchPlan plan, IReadOnlyList<SharedPrefixHypothesisObservation> hypotheses,
        bool cancelled, bool failed, string resource)
    {
        var stages = hypotheses.SelectMany(h => h.Stages).ToArray(); var actual = stages.Where(s => s.Observation != null).Select(s => s.Observation!).ToArray();
        if (failed || actual.Any(o => o.Result.Status == TransmissionGoalStatus.Failed && !SharedDriverTransmissionSearch.IsOnlyKnownResourceStop(o)))
            return (SharedPrefixTransmissionStatus.Failed, "BACKEND_OR_TRIPLE_FAILURE");
        if (cancelled || actual.Any(o => o.Result.Status == TransmissionGoalStatus.Cancelled && !o.ResourceStopped)) return (SharedPrefixTransmissionStatus.Cancelled, "USER_CANCELLED");
        if (hypotheses.Count == plan.Hypotheses.Count && hypotheses.All(h => h.Closed))
            return (hypotheses.Any(h => h.Approved > 0) ? SharedPrefixTransmissionStatus.Complete : SharedPrefixTransmissionStatus.Infeasible, "");
        // A proof closes only its hypothesis, not the union. Failed/cancelled evidence above is never masked by a proof.
        var relevant = hypotheses.Where(h => !h.Closed).ToArray();
        if (relevant.Any(h => h.Stages.Any(s => s.Observation?.ResourceStopped == true || s.NotExecutedReason == "RESOURCE_BEFORE_STAGE")))
            return (SharedPrefixTransmissionStatus.IncompleteResource, resource.Length != 0 ? resource : "STAGE_POOL_RESOURCE_CEILING");
        if (resource.Length != 0) return (SharedPrefixTransmissionStatus.IncompleteResource, resource);
        if (hypotheses.Sum(h => h.ObservedProduct) > plan.Normalized.Goal!.TripleDomainLimit) return (SharedPrefixTransmissionStatus.IncompleteResource, "OBSERVED_TRIPLE_DOMAIN_CEILING");
        if (relevant.Any(h => !h.AllStagesComplete)) return (SharedPrefixTransmissionStatus.IncompleteBudget, "RELEVANT_STAGE_SEARCH_UNFINISHED");
        return (SharedPrefixTransmissionStatus.IncompleteBudget, "JOIN_QUOTA_EXHAUSTED");
    }
}
