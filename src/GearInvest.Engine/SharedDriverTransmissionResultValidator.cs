using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Cached evidence integrity/structural validation, not a claim that historical search was executed.</summary>
public static class SharedDriverTransmissionResultValidator
{
    public static ValidationBundle Validate(SharedDriverTransmissionResult r, Func<GenerationCandidate, string> identity, Func<TransmissionMechanismCandidate, int> poolCandidateBytes)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code) { if (!ok && ds.Count < 128) ds.Add(SharedDriverTransmissionCompiler.Error("CACHED_" + code, "result", code)); }
        if (!r.Plan.Normalized.IsValid) { Check(false, "NORMALIZED_GOAL_REQUIRED"); return new ValidationBundle(ds); }
        var plan = SharedDriverTransmissionCompiler.Compile(r.Plan.Normalized.Goal!); var g = plan.Normalized.Goal!;
        Check(r.Plan.PlanId == plan.PlanId && r.Plan.Branches.Select(b => b.Canonical).SequenceEqual(plan.Branches.Select(b => b.Canonical)), "FRESH_LOWERING");
        if (!plan.IsSupported) { Check(r.Status == plan.Status && r.Branches.Count == 0 && r.Candidates.Count == 0 && r.ExaminedPairs == 0, "UNSUPPORTED_NO_EXECUTION"); return new ValidationBundle(ds); }
        Check(r.Branches.Select(b => b.OutputKey).SequenceEqual(plan.Branches.Select(b => b.OutputKey)), "TWO_REQUIRED_BRANCHES");
        if (r.Branches.Count != 2) return new ValidationBundle(ds);
        foreach (var branch in r.Branches)
        {
            var expected = plan.Branches.FirstOrDefault(b => b.OutputKey == branch.OutputKey);
            Check(expected != null && branch.Result.Plan.PlanId == expected.Plan.PlanId, "BRANCH_PLAN_OR_QUOTA");
            if (expected == null) continue;
            ds.AddRange(TransmissionResultValidator.Validate(branch.Result, identity).Diagnostics);
            Check(branch.Pool.Count <= g.PoolCountLimit && branch.PoolBytes <= g.PoolByteLimit && branch.PoolBytes == branch.Pool.Sum(poolCandidateBytes), "POOL_RESOURCE_ACCOUNTING");
            Check(branch.Pool.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == branch.Pool.Count &&
                branch.Pool.Select(c => c.CandidateId).SequenceEqual(branch.Pool.Select(c => c.CandidateId).OrderBy(x => x, StringComparer.Ordinal)), "POOL_CANONICAL_ORDER");
            Check(branch.Result.ObservedUnique == branch.Pool.Count, "ALL_OBSERVED_NOT_LOCAL_TOP_K");
            Check(!branch.ResourceStopped || branch.Result.Status == TransmissionGoalStatus.Failed && branch.Result.Diagnostics.Any(d => d.Message.Contains("SharedDriverPoolResourceLimitException")), "KNOWN_RESOURCE_STOP_NOT_BACKEND_MASK");
            foreach (var c in branch.Pool) ds.AddRange(TransmissionGoalValidator.Validate(expected.Plan, c, identity).Diagnostics);
            var ordered = branch.Pool.ToList(); ordered.Sort(TransmissionGoalValidator.Compare);
            Check(branch.Result.Candidates.Select(c => c.CandidateId).SequenceEqual(ordered.Take(1).Select(c => c.CandidateId)), "LOCAL_COMMON_RETENTION");
        }
        Check(r.ExaminedPairs >= 0 && r.ExaminedPairs <= r.ObservedPairDomain && r.ExaminedPairs <= g.ReservedCombinationWork && r.ExaminedPairs <= g.PairDomainLimit && r.ConsumedWork <= g.TotalWorkBudget, "PAIR_WORK_ALLOCATION");
        Check(r.ApprovedPairs >= 0 && r.ApprovedPairs <= r.ExaminedPairs && r.UniqueMerged >= 0 && r.UniqueMerged <= r.ApprovedPairs && r.Candidates.Count == Math.Min(r.UniqueMerged, g.MaximumReturned), "MERGED_COUNTERS");
        Check(r.Candidates.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == r.Candidates.Count, "MERGED_ID_UNIQUENESS");
        var verdict = SharedDriverTransmissionSearch.Aggregate(r.Branches, r.ExaminedPairs, r.ApprovedPairs, r.ObservedPairDomain > g.PairDomainLimit,
            r.Status == SharedDriverTransmissionStatus.Cancelled, r.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            r.LimitingReason == "RESULT_BYTE_RESERVE_CEILING" || r.LimitingReason == "MERGED_OBSERVATION_MEMORY_CEILING" ? r.LimitingReason : "");
        Check(r.UniqueMerged <= SharedDriverTransmissionContract.MaxMergedObservations, "MERGED_OBSERVATION_CEILING");
        if (r.LimitingReason == "RESULT_BYTE_RESERVE_CEILING" || r.LimitingReason == "MERGED_OBSERVATION_MEMORY_CEILING")
            Check(r.ExaminedPairs < r.ObservedPairDomain && r.ExaminedPairs < g.ReservedCombinationWork && r.ExaminedPairs < g.PairDomainLimit, "RESOURCE_STOP_HAS_UNVISITED_PAIR");
        Check(r.Status == verdict.Status && r.LimitingReason == verdict.Reason && r.ExactEmptyProof == verdict.Proof, "COMPLETENESS_AND_EXACT_PROOF");
        for (int i = 1; i < r.Candidates.Count; i++) Check(SharedDriverTransmissionComposer.Compare(r.Candidates[i - 1], r.Candidates[i]) < 0, "GLOBAL_RANK");
        foreach (var c in r.Candidates)
        {
            ds.AddRange(SharedDriverTransmissionComposer.Validate(plan, c, identity).Diagnostics);
            foreach (var o in c.Outputs) Check(r.Branches.Any(b => b.OutputKey == o.OutputKey && b.Pool.Any(p => p.CandidateId == o.Child.CandidateId &&
                p.Origins.Select(x => x.OriginId).SequenceEqual(o.Child.Origins.Select(x => x.OriginId)))), "SELECTED_PAIR_POOL_ORIGIN");
        }
        Check(r.PairDetails.Count == Math.Min(r.ExaminedPairs, SharedDriverTransmissionContract.MaxPairDetails), "PAIR_DETAIL_COVERAGE");
        var prefix = (from a in r.Branches[0].Pool from b in r.Branches[1].Pool select (a, b)).Take(r.PairDetails.Count).ToArray();
        var transcript = GearRoutingContract.Sha(Array.Empty<byte>());
        for (int i = 0; i < prefix.Length; i++)
        {
            var entry = r.PairDetails[i]; Check(entry.LeftId == prefix[i].a.CandidateId && entry.RightId == prefix[i].b.CandidateId, "IDENTITY_PRODUCT_ORDER");
            var actual = SharedDriverTransmissionComposer.Compose(plan, prefix[i].a, prefix[i].b, identity);
            Check(entry.Canonical == new SharedDriverPairObservation(entry.LeftId, entry.RightId, actual.Candidate?.CandidateId, actual.Diagnostics).Canonical, "PAIR_VALIDATION_READBACK");
            transcript = SharedDriverTransmissionSearch.TranscriptNext(transcript, entry);
        }
        if (!r.PairDetailsTruncated)
        {
            Check(r.PairTranscriptHash == transcript && r.PairDetails.Count(p => p.Approved) == r.ApprovedPairs && r.PairDetails.Where(p => p.Approved).Select(p => p.CandidateId).Distinct(StringComparer.Ordinal).Count() == r.UniqueMerged, "FULL_SMALL_TRANSCRIPT");
        }
        else Check(r.PairTranscriptHash.Length == 64 && r.PairTranscriptHash.All(c => "0123456789abcdef".Contains(c)), "BOUNDED_TRANSCRIPT_HASH");
        return new ValidationBundle(ds.Take(128));
    }
}
