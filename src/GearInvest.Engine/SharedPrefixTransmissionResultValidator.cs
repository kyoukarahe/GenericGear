using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Reconstructs cached context and bounded evidence. Historical exhaustive execution requires Fresh.</summary>
public static class SharedPrefixTransmissionResultValidator
{
    public static ValidationBundle Validate(SharedPrefixTransmissionResult r, Func<GenerationCandidate, string> identity, Func<TransmissionMechanismCandidate, int> bytes)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code) { if (!ok && ds.Count < 128) ds.Add(SharedPrefixTransmissionCompiler.Error("CACHED_" + code, "result", code)); }
        if (!r.Plan.Normalized.IsValid) { Check(false, "NORMALIZED_GOAL"); return new ValidationBundle(ds); }
        var plan = SharedPrefixTransmissionCompiler.Compile(r.Plan.Normalized.Goal!); var g = plan.Normalized.Goal!;
        Check(r.Plan.PlanId == plan.PlanId && r.Plan.Hypotheses.Select(h => h.Canonical).SequenceEqual(plan.Hypotheses.Select(h => h.Canonical)), "FRESH_LOWERING");
        if (!plan.IsSupported)
        { Check(r.Status == plan.Status && r.LimitingReason == plan.LimitingReason && r.Hypotheses.Count == 0 && r.Candidates.Count == 0 && r.UniqueMerged == 0 && r.Details.Count == 0 && r.Transcript == GearRoutingContract.Sha(Array.Empty<byte>()), "UNEXECUTED_PLAN"); return new ValidationBundle(ds); }
        Check(r.Hypotheses.Select(h => h.HypothesisId).SequenceEqual(plan.Hypotheses.Select(h => h.HypothesisId)), "ALL_HYPOTHESES_ORDER");
        if (ds.Count > 0) return new ValidationBundle(ds);
        int usedBytes = 0;
        foreach (var h in r.Hypotheses)
        {
            var expected = plan.Hypotheses.Single(x => x.HypothesisId == h.HypothesisId);
            if (expected.ExclusionProof.Length != 0)
            { Check(h.ClosureProof == expected.ExclusionProof && h.Stages.Count == 0 && h.Examined == 0 && h.Approved == 0, "EXACT_ROLE_REGION_EXCLUSION"); continue; }
            var plans = new[] { new SharedDriverBranchPlan("prefix", expected.Prefix!) }.Concat(expected.Suffix!.Branches.Select(b => new SharedDriverBranchPlan("output:" + b.OutputKey, b.Plan))).ToArray();
            Check(h.Stages.Select(s => s.Role).SequenceEqual(plans.Select(p => p.OutputKey)), "THREE_STAGE_ROLES");
            if (h.Stages.Count != 3) continue;
            for (int i = 0; i < 3; i++)
            {
                var s = h.Stages[i]; var o = s.Observation;
                if (o == null)
                {
                    bool empty = h.Stages.Take(i).Any(x => x.ProvenEmpty && s.NotExecutedReason == "EXACT_EMPTY_FACTOR:" + x.Role);
                    Check(empty || s.NotExecutedReason == "CANCELLED_BEFORE_STAGE" && r.Status == SharedPrefixTransmissionStatus.Cancelled ||
                        s.NotExecutedReason == "RESOURCE_BEFORE_STAGE" && (r.Status == SharedPrefixTransmissionStatus.IncompleteResource || r.Status == SharedPrefixTransmissionStatus.Failed || r.Status == SharedPrefixTransmissionStatus.Cancelled), "SKIPPED_NOT_COMPLETE");
                    if (s.NotExecutedReason == "RESOURCE_BEFORE_STAGE") Check(g.TotalPoolByteLimit <= usedBytes || r.LimitingReason == "RESULT_BYTE_RESERVE_CEILING", "SKIPPED_RESOURCE_EVIDENCE");
                    continue;
                }
                Check(s.NotExecutedReason.Length == 0 && o.OutputKey == s.Role && o.Result.Plan.PlanId == plans[i].Plan.PlanId, "STAGE_CONTEXT_QUOTA");
                ds.AddRange(TransmissionResultValidator.Validate(o.Result, identity).Diagnostics);
                int allowed = Math.Min(g.PoolByteLimit, g.TotalPoolByteLimit - usedBytes);
                Check(o.PoolBytes == o.Pool.Sum(bytes) && o.PoolBytes <= allowed && o.Pool.Count <= g.PoolCountLimit, "OBSERVED_POOL_BYTES"); usedBytes += o.PoolBytes;
                Check(o.Pool.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == o.Pool.Count && o.Pool.Select(c => c.CandidateId).SequenceEqual(o.Pool.Select(c => c.CandidateId).OrderBy(x => x, StringComparer.Ordinal)), "CANONICAL_POOL");
                Check(o.Result.ObservedUnique == o.Pool.Count, "PRE_TOP_K_OBSERVATION_COVERAGE");
                Check(!o.ResourceStopped || SharedDriverTransmissionSearch.IsOnlyKnownResourceStop(o), "RESOURCE_FAILURE_NOT_MASKED");
                foreach (var c in o.Pool) ds.AddRange(TransmissionGoalValidator.Validate(plans[i].Plan, c, identity).Diagnostics);
                var ordered = o.Pool.ToList(); ordered.Sort(TransmissionGoalValidator.Compare);
                Check(o.Result.Candidates.Select(c => c.CandidateId).SequenceEqual(ordered.Take(1).Select(c => c.CandidateId)), "LOCAL_CAP_IS_NOT_DOMAIN");
            }
            Check(h.ClosureProof == SharedPrefixTransmissionSearch.EmptyProof(h.Stages), "EMPTY_FACTOR_PROOF");
            Check(h.Examined >= 0 && h.Examined <= h.ObservedProduct && h.Approved >= 0 && h.Approved <= h.Examined && (h.ClosureProof.Length == 0 || h.Examined == 0), "HYPOTHESIS_WORK");
        }
        Check(r.ConsumedPrefixWork <= plan.AllocatedPrefixWork && r.ConsumedSuffixWork <= plan.AllocatedSuffixWork && r.ConsumedWork <= g.TotalWorkBudget &&
            r.ExaminedTriples <= g.ReservedJoinWork && r.ExaminedTriples <= g.TripleDomainLimit && r.PoolBytes <= g.TotalPoolByteLimit, "GLOBAL_DISJOINT_BUDGETS");
        Check(r.UniqueMerged >= 0 && r.UniqueMerged <= r.ApprovedTriples && r.UniqueMerged <= SharedPrefixTransmissionContract.MaxMergedIndex && r.Candidates.Count == Math.Min(r.UniqueMerged, g.MaximumReturned), "RETURN_VS_SEARCH");
        string resource = new[] { "RESULT_BYTE_RESERVE_CEILING", "MERGED_INDEX_CEILING", "TOTAL_POOL_BYTE_CEILING" }.Contains(r.LimitingReason) ? r.LimitingReason : "";
        var verdict = SharedPrefixTransmissionSearch.Aggregate(plan, r.Hypotheses, r.Status == SharedPrefixTransmissionStatus.Cancelled, r.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), resource);
        Check(r.Status == verdict.Status && r.LimitingReason == verdict.Reason, "UNION_COMPLETENESS");
        Check(r.Candidates.Select(c => c.CandidateId).Distinct().Count() == r.Candidates.Count, "WHOLE_UNIQUE");
        for (int i = 1; i < r.Candidates.Count; i++) Check(SharedPrefixTransmissionComposer.Compare(r.Candidates[i - 1], r.Candidates[i]) < 0, "WHOLE_RANK_ORDER");
        foreach (var c in r.Candidates)
        {
            ds.AddRange(SharedPrefixTransmissionComposer.Validate(plan, c, identity).Diagnostics);
            var h = r.Hypotheses.SingleOrDefault(x => x.HypothesisId == c.HypothesisId);
            Check(h?.Stages.Count == 3, "SELECTED_HYPOTHESIS"); if (h?.Stages.Count != 3) continue;
            var children = new[] { c.Prefix }.Concat(c.Outputs.Select(o => o.Child)).ToArray();
            for (int i = 0; i < 3; i++) Check(h.Stages[i].Observation?.Pool.Any(p => p.CandidateId == children[i].CandidateId && p.Origins.Select(o => o.OriginId).SequenceEqual(children[i].Origins.Select(o => o.OriginId))) == true, "SELECTED_POOL_ORIGIN");
        }
        Check(r.Details.Count == Math.Min(r.ExaminedTriples, 64), "TRIPLE_DETAIL_COVERAGE");
        var sequence = r.Hypotheses.Where(h => h.Stages.Count == 3 && h.Stages.All(s => s.Observation != null)).SelectMany(h =>
            (from p in h.Stages[0].Observation!.Pool from a in h.Stages[1].Observation!.Pool from b in h.Stages[2].Observation!.Pool select (h.HypothesisId, p, a, b)).Take(h.Examined)).Take(r.Details.Count).ToArray();
        Check(sequence.Length == r.Details.Count, "TRIPLE_OBSERVED_DOMAIN"); string transcript = GearRoutingContract.Sha(Array.Empty<byte>());
        var rebuiltCandidates = new List<SharedPrefixTransmissionCandidate>();
        for (int i = 0; i < sequence.Length; i++)
        {
            var s = sequence[i]; var entry = r.Details[i];
            Check(entry.HypothesisId == s.HypothesisId && entry.PrefixId == s.p.CandidateId && entry.LeftId == s.a.CandidateId && entry.RightId == s.b.CandidateId, "ORDINAL_TRIPLE_ORDER");
            var actual = SharedPrefixTransmissionComposer.Compose(plan, s.HypothesisId, s.p, s.a, s.b, identity);
            Check(entry.Canonical == new SharedPrefixTripleObservation(s.HypothesisId, s.p.CandidateId, s.a.CandidateId, s.b.CandidateId, actual.Candidate?.CandidateId, actual.Diagnostics).Canonical, "ACTUAL_TRIPLE_RECHECK");
            if (actual.IsValid) rebuiltCandidates.Add(actual.Candidate!); transcript = SharedPrefixTransmissionSearch.NextTranscript(transcript, entry);
        }
        if (r.ExaminedTriples == r.Details.Count)
        {
            var unique = rebuiltCandidates.GroupBy(c => c.CandidateId, StringComparer.Ordinal).Select(x => x.First()).ToList(); unique.Sort(SharedPrefixTransmissionComposer.Compare);
            Check(r.Transcript == transcript && rebuiltCandidates.Count == r.ApprovedTriples && unique.Count == r.UniqueMerged &&
                r.Candidates.Select(c => c.CandidateId).SequenceEqual(unique.Take(g.MaximumReturned).Select(c => c.CandidateId)), "SMALL_FULL_TRANSCRIPT_AND_TOP_K");
        }
        else Check(r.Transcript.Length == 64 && r.Transcript.All(c => "0123456789abcdef".Contains(c)), "BOUNDED_TRANSCRIPT_HASH");
        return new ValidationBundle(ds.Take(128));
    }
}
