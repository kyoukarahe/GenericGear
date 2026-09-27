using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public byte[] WriteOrientedGoal(OrientedTwoOutputGoal goal) => OrientedGoalJson.WriteGoal(RequireOrientedGoalPlan(goal));
    public OrientedTwoOutputGoal ReadOrientedGoal(byte[] bytes)
    {
        var document = OrientedGoalJson.ReadGoal(bytes); var plan = RequireOrientedGoalPlan(document.Goal);
        NeedOrientedGoal(document.GoalId == plan.GoalId && bytes.SequenceEqual(OrientedGoalJson.WriteGoal(plan)), "Goal identity or canonical domain order mismatch.");
        return plan.Goal;
    }
    public byte[] WriteOrientedGoalPlan(OrientedGoalPlan plan) => OrientedGoalJson.WritePlan(plan);
    public byte[] WriteOrientedGoalResult(OrientedGoalGeneration result) => OrientedGoalJson.WriteResult(result);
    public OrientedGoalGeneration ReadOrientedGoalResult(OrientedTwoOutputGoal goal, byte[] bytes)
    { var r = OrientedGoalJson.ReadResult(bytes); ValidateOrientedGoalResult(goal, r); return r; }

    /// <summary>Validates cache structure and declared candidate contexts, not historical exhaustive traversal. Regenerate separately for that check.</summary>
    public void ValidateOrientedGoalResult(OrientedTwoOutputGoal goal, OrientedGoalGeneration r)
    {
        var plan = RequireOrientedGoalPlan(goal); var o = plan.Goal.Options;
        NeedOrientedGoal(r.GoalId == plan.GoalId && r.Profile == plan.Goal.Profile && r.TheoreticalTuples == plan.TupleCount && r.ProcessedTuples >= 0 && r.ProcessedTuples <= plan.TupleCount && r.ProcessedTuples <= o.WorkBudget,
            "Goal/result coverage identity mismatch.");
        NeedOrientedGoal(new[] { r.AcceptedTuples, r.RejectedTuples, r.InconclusiveTuples, r.UnretainedTuples, r.UniqueAcceptedCount }.All(n => n >= 0) &&
            r.AcceptedTuples + r.RejectedTuples + r.InconclusiveTuples == r.ProcessedTuples && r.UnretainedTuples <= r.AcceptedTuples && r.UnretainedTuples <= 1 &&
            r.UniqueAcceptedCount <= r.AcceptedTuples && r.UniqueAcceptedCount <= o.MaxUniqueCandidates + 1, "Inconsistent exact tuple counters.");
        // Failed/preflight results remain diagnostic outputs, not persistable successful search claims.
        NeedOrientedGoal(r.Status != OrientedGoalStatus.InvalidInput && r.Status != OrientedGoalStatus.Unsupported && r.Status != OrientedGoalStatus.Failed, "Nonpersistable failed/preflight result.");
        var closed = r.Status == OrientedGoalStatus.Complete || r.Status == OrientedGoalStatus.Infeasible || r.Status == OrientedGoalStatus.NoValidatedCandidate;
        NeedOrientedGoal(r.EnumerationComplete == closed && (!closed || r.RemainingTuples == 0 && r.UnretainedTuples == 0), "Forged enumeration completeness.");
        NeedOrientedGoal(r.Status != OrientedGoalStatus.Complete || r.AcceptedTuples > 0, "Complete requires accepted candidates.");
        NeedOrientedGoal(r.Status != OrientedGoalStatus.Infeasible || r.AcceptedTuples == 0 && r.InconclusiveTuples == 0 && r.RejectedTuples == r.TheoreticalTuples, "Infeasible requires exhaustive proven mismatch.");
        NeedOrientedGoal(r.Status != OrientedGoalStatus.NoValidatedCandidate || r.AcceptedTuples == 0 && r.InconclusiveTuples > 0, "Unresolved clearance status mismatch.");
        NeedOrientedGoal(r.Status != OrientedGoalStatus.IncompleteBudget || r.ProcessedTuples == o.WorkBudget && r.RemainingTuples > 0 && r.UnretainedTuples == 0 && r.LimitingReason == "work-budget", "Budget coverage mismatch.");
        NeedOrientedGoal(r.Status != OrientedGoalStatus.IncompleteResource || r.UnretainedTuples == 1 && new[] { "unique-candidate-retention", "origin-retention", "artifact-byte-retention" }.Contains(r.LimitingReason), "Resource cause mismatch.");
        NeedOrientedGoal(r.UnretainedTuples == 0 || r.Status == OrientedGoalStatus.IncompleteResource, "Unretained accepted tuple requires explicit resource interruption.");
        NeedOrientedGoal(!closed || r.LimitingReason == "declared-domain-exhausted", "Closed search limiting reason mismatch.");
        NeedOrientedGoal(r.RetainedUniqueCount >= 0 && r.RetainedUniqueCount <= o.MaxUniqueCandidates && r.RetainedUniqueCount <= r.UniqueAcceptedCount &&
            r.RetainedUniqueCount >= r.UniqueAcceptedCount - r.UnretainedTuples && r.RetainedOriginCount == r.AcceptedTuples - r.UnretainedTuples &&
            r.RetainedOriginCount <= o.MaxOrigins && r.RetainedUniqueCount <= r.RetainedOriginCount && r.Candidates.Count == Math.Min(r.RetainedUniqueCount, o.ReturnedCandidateCap), "Omitted or excess retained candidates/origins.");
        NeedOrientedGoal(r.ResultTruncated == (r.RetainedUniqueCount > o.ReturnedCandidateCap), "Return cap/truncation mismatch.");
        var guarantee = r.Candidates.Count == 0 || r.UnretainedTuples != 0 ? OrientedRankingGuarantee.None : closed ? OrientedRankingGuarantee.CompleteDeclaredDomain : OrientedRankingGuarantee.BestAmongExplored;
        NeedOrientedGoal(r.RankingGuarantee == guarantee && OrientedGoalKeys.IsHash(r.TranscriptDigest), "Ranking guarantee/transcript mismatch.");
        NeedOrientedGoal(r.Candidates.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == r.Candidates.Count &&
            r.Candidates.SequenceEqual(r.Candidates.OrderBy(c => c, OrientedGoalSearch.CandidateComparer)), "Duplicate candidate or forged ranking order.");
        NeedOrientedGoal(r.Candidates.Sum(c => c.Artifact.ByteLength) <= o.MaxRetainedBytes && r.Candidates.Sum(c => c.Origins.Count) <= o.MaxOrigins, "Retained resource bounds exceeded.");
        var visitedOrigins = new HashSet<int>();
        foreach (var c in r.Candidates)
        {
            NeedOrientedGoal(c.Origins.Count > 0 && c.Origins.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() == c.Origins.Count, "Missing or duplicate origin.");
            var artifact = ReadOrientedTwoOutputArtifact(c.Artifact.Bytes);
            NeedOrientedGoal(ValidateOrientedTwoOutputArtifact(artifact).IsValid && c.CandidateId == artifact.CandidateId && c.Artifact.ArtifactHash == artifact.ArtifactHash &&
                c.Artifact.MechanicalPayload.SequenceEqual(CanonicalOrientedTwoOutputJson.WriteMechanicalPayload(artifact.Mechanism)), "Candidate artifact integrity mismatch.");
            foreach (var origin in c.Origins)
            {
                CheckTuple(plan, origin.Tuple, r.ProcessedTuples); NeedOrientedGoal(visitedOrigins.Add(origin.Tuple.Ordinal), "Origin tuple duplicated across candidates.");
                NeedOrientedGoal(origin.Id == OrientedGoalKeys.Origin(plan, origin.Tuple) && origin.PreferencePenalty == OrientedGoalSearch.Preference(plan, origin.Tuple), "Origin/domain binding mismatch.");
                NeedOrientedGoal(OrientedGoalSearch.ValidateContext(plan, origin.Tuple, artifact.Mechanism).IsValid, "Undeclared candidate goal context.");
                var rewritten = WriteOrientedTwoOutputArtifact(artifact.Mechanism, LowerOrientedGoalTuple(plan, origin.Tuple));
                NeedOrientedGoal(rewritten.Artifact.CandidateId == c.CandidateId && rewritten.Artifact.ArtifactHash == origin.ArtifactHash, "Origin artifact provenance mismatch.");
                if (origin.Id == c.RepresentativeOriginId) NeedOrientedGoal(rewritten.Bytes.SequenceEqual(c.Artifact.Bytes), "Selected representative artifact/context mismatch.");
            }
            var rep = c.Origins.OrderBy(x => x.PreferencePenalty).ThenBy(x => x.Id, StringComparer.Ordinal).First();
            NeedOrientedGoal(rep.Id == c.RepresentativeOriginId, "Noncanonical representative origin.");
            var m = OrientedGoalSearch.Measure(artifact.Mechanism, rep.PreferencePenalty);
            NeedOrientedGoal(m.Bodies == c.Metrics.Bodies && m.Envelope.Min == c.Metrics.Envelope.Min && m.Envelope.Max == c.Metrics.Envelope.Max &&
                m.PreferencePenalty == c.Metrics.PreferencePenalty && m.TotalTeeth == c.Metrics.TotalTeeth, "Forged whole-mechanism metrics.");
        }
        if (!r.ResultTruncated && r.UnretainedTuples == 0) NeedOrientedGoal(visitedOrigins.Count == r.AcceptedTuples, "Accepted origin coverage mismatch.");
        NeedOrientedGoal(r.Details.Count == Math.Min(r.ProcessedTuples, o.MaxDetails), "Bounded prefix detail coverage mismatch.");
        var digest = OrientedGoalKeys.HashText("oriented-goal-transcript-v1");
        var refined = r.Profile == OrientedGoalProfile.RefinedId;
        NeedOrientedGoal(r.PairPredicateCount >= 0 && r.PairPredicateCount <= (long)r.ProcessedTuples * PitchClearancePolicy.MaxPairs &&
            (refined || r.PairPredicateCount == 0), "Pair predicate observation bounds/policy mismatch.");
        for (var index = 0; index < r.Details.Count; index++)
        {
            var d = r.Details[index]; NeedOrientedGoal(d.Tuple.Ordinal == index, "Details are not the exact visited prefix."); CheckTuple(plan, d.Tuple, r.ProcessedTuples);
            NeedOrientedGoal(d.Reason.Length > 0 && d.Reason.Length <= 4096 && Enum.IsDefined(typeof(OrientedTupleVerdict), d.Verdict), "Invalid tuple diagnostic.");
            NeedOrientedGoal((d.Verdict == OrientedTupleVerdict.Accepted || d.Verdict == OrientedTupleVerdict.Unretained) == (d.CandidateId is not null) &&
                (d.CandidateId is null || OrientedGoalKeys.IsHash(d.CandidateId)), "Tuple verdict/identity mismatch.");
            if (d.Verdict == OrientedTupleVerdict.Accepted && !r.ResultTruncated)
                NeedOrientedGoal(r.Candidates.Any(c => c.CandidateId == d.CandidateId && c.Origins.Any(x => x.Tuple.Ordinal == index)), "Accepted tuple omitted from retained candidates.");
            if (refined)
            {
                // Resolve only the retained bounded detail prefix. This is NOT historical exhaustive-search verification.
                var fresh = InspectOrientedGoalTuple(plan, d.Tuple); var proofs = fresh.Validation.PitchProofs;
                NeedOrientedGoal(d.PairPredicateCount == proofs.Count && d.ClearanceDigest == PitchProofKeys.CoverageDigest(proofs), "Original tuple/policy/proof coverage mismatch.");
                var failures = fresh.Validation.Checks.Where(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass).ToArray();
                var verdict = failures.Any(c => c.Verdict == OrientedCheckVerdict.Fail) ? OrientedTupleVerdict.Rejected :
                    failures.Any(c => c.Verdict == OrientedCheckVerdict.Inconclusive) ? OrientedTupleVerdict.Inconclusive : OrientedTupleVerdict.Accepted;
                NeedOrientedGoal(d.Verdict == verdict || d.Verdict == OrientedTupleVerdict.Unretained && verdict == OrientedTupleVerdict.Accepted, "Tuple verdict does not match original supported geometry/context.");
            }
            else NeedOrientedGoal(d.PairPredicateCount == 0 && d.ClearanceDigest is null, "Legacy tuple cannot claim refined proof.");
            digest = OrientedGoalSearch.ExtendTranscript(digest, d);
        }
        if (r.Details.Count == r.ProcessedTuples)
        {
            NeedOrientedGoal(r.PairPredicateCount == r.Details.Sum(d => d.PairPredicateCount), "Pair observation total mismatch.");
            NeedOrientedGoal(digest == r.TranscriptDigest && r.Details.Count(d => d.Verdict == OrientedTupleVerdict.Accepted || d.Verdict == OrientedTupleVerdict.Unretained) == r.AcceptedTuples &&
                r.Details.Count(d => d.Verdict == OrientedTupleVerdict.Rejected) == r.RejectedTuples && r.Details.Count(d => d.Verdict == OrientedTupleVerdict.Inconclusive) == r.InconclusiveTuples &&
                r.Details.Count(d => d.Verdict == OrientedTupleVerdict.Unretained) == r.UnretainedTuples, "Transcript/counter mismatch.");
        }
    }

    internal OrientedGoalPlan RequireOrientedGoalPlan(OrientedTwoOutputGoal goal)
    { var c = CompileOrientedTwoOutputSearchPlan(goal); NeedOrientedGoal(c.IsSuccess, c.Status + ": " + c.Detail); return c.Plan!; }
    private static void CheckTuple(OrientedGoalPlan plan, OrientedGoalTuple t, int processed)
    { NeedOrientedGoal(t.Ordinal >= 0 && t.Ordinal < processed && OrientedGoalKeys.Tuple(t) == OrientedGoalKeys.Tuple(OrientedGoalCompiler.Decode(plan, t.Ordinal)), "Unvisited or undeclared tuple."); }
    internal static void NeedOrientedGoal(bool ok, string message) { if (!ok) throw new ArtifactFormatException(message); }
}
