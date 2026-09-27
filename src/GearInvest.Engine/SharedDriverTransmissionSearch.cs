using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class SharedDriverTransmissionSearch
{
    public SharedDriverTransmissionResult Generate(SharedDriverTransmissionGoal goal, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolCandidateBytes, Func<SharedDriverTransmissionResult, int> resultBytes,
        CancellationToken token = default, Action<int, int>? progress = null)
        => GenerateObserved(goal, identity, poolCandidateBytes, resultBytes, token, progress, null);

    // 16D observes validated wholes before local retention. Null preserves all specialized semantics/bytes.
    internal SharedDriverTransmissionResult GenerateObserved(SharedDriverTransmissionGoal goal, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolCandidateBytes, Func<SharedDriverTransmissionResult, int> resultBytes,
        CancellationToken token, Action<int, int>? progress, Action<SharedDriverTransmissionCandidate>? observer)
    {
        var plan = SharedDriverTransmissionCompiler.Compile(goal); var branches = new List<SharedDriverBranchObservation>(); var ds = new List<Diagnostic>(plan.Diagnostics);
        var kept = new List<SharedDriverTransmissionCandidate>(); var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var details = new List<SharedDriverPairObservation>(); int examined = 0, approved = 0, indexBytes = 0;
        var transcript = GearRoutingContract.Sha(Array.Empty<byte>());
        if (!plan.IsSupported) return new SharedDriverTransmissionResult(plan, plan.Status, branches, kept, 0, 0, 0, details, transcript, "UNSUPPORTED_OR_INVALID", diagnostics: ds);
        var g = plan.Normalized.Goal!;
        foreach (var branch in plan.Branches)
        {
            branches.Add(ObserveBranch(branch, identity, poolCandidateBytes, g.PoolCountLimit, g.PoolByteLimit, g.TotalWorkBudget,
                branches.Sum(b => b.Result.ConsumedWork), token, progress));
        }
        long product = (long)branches[0].Pool.Count * branches[1].Pool.Count;
        bool pairResource = product > g.PairDomainLimit;
        int allowedPairs = Math.Min(g.ReservedCombinationWork, g.PairDomainLimit);
        bool stop = false; string joinResource = ""; int priorMeasureStamp = -1, measuredBytes = 0;
        SharedDriverTransmissionResult Snapshot() => new SharedDriverTransmissionResult(plan, SharedDriverTransmissionStatus.IncompleteBudget,
            branches, kept, examined, approved, seen.Count, details, transcript);
        foreach (var left in branches[0].Pool)
        {
            foreach (var right in branches[1].Pool)
            {
                if (token.IsCancellationRequested || examined >= allowedPairs) { stop = true; break; }
                if (seen.Count >= SharedDriverTransmissionContract.MaxMergedObservations || indexBytes > SharedDriverTransmissionContract.MaxMergedIndexBytes - SharedDriverTransmissionContract.MaxMergedSignatureBytes)
                { joinResource = "MERGED_OBSERVATION_MEMORY_CEILING"; stop = true; break; }
                // Measured at each retained change / diagnostic-prefix change. Reserve covers one bounded whole artifact,
                // both role maps, one diagnostic record and counter/status growth. No observation or top-K is silently dropped.
                int stamp = seen.Count * (SharedDriverTransmissionContract.MaxPairDetails + 1) + details.Count;
                if (stamp != priorMeasureStamp) { measuredBytes = resultBytes(Snapshot()); priorMeasureStamp = stamp; }
                if (measuredBytes > SharedDriverTransmissionContract.MaxResultBytes - SharedDriverTransmissionContract.ResultReserveBytes)
                { joinResource = "RESULT_BYTE_RESERVE_CEILING"; stop = true; break; }
                examined++; // Early root, axis, environment and contact failures consume the same one pair unit.
                SharedDriverPairResult pair;
                try { pair = SharedDriverTransmissionComposer.Compose(plan, left, right, identity); }
                catch (Exception e)
                {
                    var failure = SharedDriverTransmissionCompiler.Error("PAIR_FAILURE", left.CandidateId + "|" + right.CandidateId, e.GetType().Name + ":" + e.Message);
                    ds.Add(failure); var failedPair = new SharedDriverPairObservation(left.CandidateId, right.CandidateId, null, new[] { failure });
                    transcript = TranscriptNext(transcript, failedPair); if (details.Count < SharedDriverTransmissionContract.MaxPairDetails) details.Add(failedPair);
                    stop = true; break;
                }
                var observation = new SharedDriverPairObservation(left.CandidateId, right.CandidateId, pair.Candidate?.CandidateId, pair.Diagnostics);
                transcript = TranscriptNext(transcript, observation); if (details.Count < SharedDriverTransmissionContract.MaxPairDetails) details.Add(observation);
                if (pair.IsValid)
                {
                    approved++; var c = pair.Candidate!; var signature = TransmissionGoalValidator.MechanicalSignature(c.Mechanism);
                    observer?.Invoke(c);
                    if (seen.TryGetValue(c.CandidateId, out var previous))
                    {
                        if (previous != signature) { ds.Add(SharedDriverTransmissionCompiler.Error("IDENTITY_COLLISION", c.CandidateId, "Different exact merged payloads have the same ID.")); stop = true; break; }
                    }
                    else
                    {
                        seen.Add(c.CandidateId, signature); indexBytes += Encoding.UTF8.GetByteCount(signature) + c.CandidateId.Length;
                        kept.Add(c); kept.Sort(SharedDriverTransmissionComposer.Compare);
                        if (kept.Count > g.MaximumReturned) kept.RemoveAt(kept.Count - 1);
                    }
                }
                progress?.Invoke(branches.Sum(b => b.Result.ConsumedWork) + examined, g.TotalWorkBudget);
            }
            if (stop) break;
        }
        var verdict = Aggregate(branches, examined, approved, pairResource, token.IsCancellationRequested, ds.Count > 0, joinResource);
        return new SharedDriverTransmissionResult(plan, verdict.Status, branches, kept, examined, approved, seen.Count, details, transcript, verdict.Reason, verdict.Proof, ds);
    }

    // Same validated observer and exact resource exception for 16B and 16C; neither consumes retained local top-K.
    internal static SharedDriverBranchObservation ObserveBranch(SharedDriverBranchPlan branch, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolCandidateBytes, int poolCountLimit, int poolByteLimit, int totalWork, int priorWork,
        CancellationToken token, Action<int, int>? progress)
    {
        var pool = new SortedDictionary<string, TransmissionMechanismCandidate>(StringComparer.Ordinal);
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal); int bytes = 0; bool resource = false;
        using var generationStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Observe(TransmissionMechanismCandidate c)
        {
            var signature = TransmissionGoalValidator.MechanicalSignature(c.Mechanism);
            var next = c; int oldBytes = 0;
            if (pool.TryGetValue(c.CandidateId, out var prior))
            {
                if (signatures[c.CandidateId] != signature) throw new InvalidOperationException("SameCandidateIdDifferentMechanicalPayload");
                next = new TransmissionMechanismCandidate(c.CandidateId, prior.Origins.Concat(c.Origins).GroupBy(o => o.OriginId, StringComparer.Ordinal).Select(x => x.First()), prior.Metrics);
                oldBytes = poolCandidateBytes(prior);
            }
            int candidateBytes = poolCandidateBytes(next);
            if ((!pool.ContainsKey(c.CandidateId) && pool.Count >= poolCountLimit) || candidateBytes > poolByteLimit || bytes - oldBytes + candidateBytes > poolByteLimit)
            {
                resource = true; generationStop.Cancel();
                // The unchanged 16A runner preserves this interrupted observation and consumed work as a Failed child.
                // 16B explicitly classifies this known sink-resource stop, not an arbitrary backend failure, as IncompleteResource.
                throw new SharedDriverPoolResourceLimitException();
            }
            pool[c.CandidateId] = next; signatures[c.CandidateId] = signature; bytes += candidateBytes - oldBytes;
        }
        var result = new TransmissionSearchOrchestrator().GenerateObserved(branch.Plan.Normalized.Goal!, identity, generationStop.Token,
            (work, _) => progress?.Invoke(priorWork + work, totalWork), Observe);
        if (result.Plan.PlanId != branch.Plan.PlanId) throw new InvalidOperationException("SharedDriverFreshBranchPlanDrift");
        return new SharedDriverBranchObservation(branch.OutputKey, result, pool.Values, bytes, resource);
    }

    internal static string TranscriptNext(string previous, SharedDriverPairObservation p) => GearRoutingContract.Sha(Encoding.UTF8.GetBytes(GearRoutingContract.Pack(previous, p.Canonical)));

    internal static (SharedDriverTransmissionStatus Status, string Reason, string Proof) Aggregate(IReadOnlyList<SharedDriverBranchObservation> branches,
        int examined, int approved, bool pairResource, bool cancelled, bool failed, string joinResource = "")
    {
        if (failed || branches.Any(b => b.Result.Status == TransmissionGoalStatus.Failed && !IsOnlyKnownResourceStop(b))) return (SharedDriverTransmissionStatus.Failed, "BACKEND_OR_PAIR_FAILURE", "");
        if (cancelled || branches.Any(b => b.Result.Status == TransmissionGoalStatus.Cancelled && !b.ResourceStopped)) return (SharedDriverTransmissionStatus.Cancelled, "USER_CANCELLED", "");
        var empty = branches.FirstOrDefault(b => b.SearchComplete && b.Pool.Count == 0);
        if (empty != null) return (SharedDriverTransmissionStatus.Infeasible, "EXACT_EMPTY_REQUIRED_FACTOR", empty.OutputKey + ":ALL_INCLUDED_FAMILIES_CLOSED_WITH_NO_CANDIDATE");
        if (branches.Any(b => b.ResourceStopped) || pairResource) return (SharedDriverTransmissionStatus.IncompleteResource, pairResource ? "OBSERVED_PAIR_DOMAIN_CEILING" : "BRANCH_POOL_RESOURCE_CEILING", "");
        if (joinResource.Length != 0) return (SharedDriverTransmissionStatus.IncompleteResource, joinResource, "");
        if (branches.Count != 2 || branches.Any(b => !b.SearchComplete)) return (SharedDriverTransmissionStatus.IncompleteBudget, "REQUIRED_BRANCH_SEARCH_UNFINISHED", "");
        if (examined < (long)branches[0].Pool.Count * branches[1].Pool.Count) return (SharedDriverTransmissionStatus.IncompleteBudget, "COMBINATION_QUOTA_EXHAUSTED", "");
        return (approved > 0 ? SharedDriverTransmissionStatus.Complete : SharedDriverTransmissionStatus.Infeasible, "", "");
    }

    internal static bool IsOnlyKnownResourceStop(SharedDriverBranchObservation b) => b.ResourceStopped &&
        b.Result.Diagnostics.Any(d => d.Code == "TRANSMISSION_GOAL_BACKEND_FAILURE") &&
        b.Result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).All(d => d.Code == "TRANSMISSION_GOAL_BACKEND_FAILURE" && d.Message.StartsWith("SharedDriverPoolResourceLimitException:", StringComparison.Ordinal));

    private sealed class SharedDriverPoolResourceLimitException : Exception
    { internal SharedDriverPoolResourceLimitException() : base("SharedDriverPoolResourceCeiling: observations stopped; this is not a feasible-domain top-K.") { } }
}
