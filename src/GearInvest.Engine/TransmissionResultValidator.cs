using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Cached structural/goal/counter consistency, explicitly not proof of historical exhaustive search.</summary>
public static class TransmissionResultValidator
{
    public static ValidationBundle Validate(TransmissionGenerationResult result, Func<GenerationCandidate, string> identity)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code) { if (!ok) ds.Add(TransmissionGoalCompiler.Error("CACHED_" + code, "result", code)); }
        if (!result.Plan.Normalized.IsValid) { Check(false, "NORMALIZED_GOAL_REQUIRED"); return new ValidationBundle(ds); }
        var plan = TransmissionGoalCompiler.Compile(result.Plan.Normalized.Goal!); var g = plan.Normalized.Goal!;
        Check(result.Plan.PlanId == plan.PlanId && result.Plan.Families.Select(f => f.Canonical).SequenceEqual(plan.Families.Select(f => f.Canonical)), "FRESH_LOWERING_MISMATCH");
        Check(result.Outcomes.Select(o => o.Family).SequenceEqual(plan.Families.Select(p => p.Family)), "FAMILY_COVERAGE");
        Check(result.ObservedUnique >= result.Candidates.Count && result.ObservedUnique <= TransmissionGoalContract.MaxUniqueObservations && result.Candidates.Count <= g.MaximumReturned, "OBSERVATION_RETENTION_LIMIT");
        Check(result.Candidates.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == result.Candidates.Count, "DUPLICATE_CANDIDATE");
        Check(!result.SearchComplete || result.ObservationsExact, "INEXACT_OBSERVATIONS_CANNOT_COMPLETE");
        for (int i = 1; i < result.Candidates.Count; i++) Check(TransmissionGoalValidator.Compare(result.Candidates[i - 1], result.Candidates[i]) < 0, "COMMON_ORDER");
        foreach (var o in result.Outcomes)
        {
            var child = plan.Families.FirstOrDefault(p => p.Family == o.Family); if (child == null) { Check(false, "UNKNOWN_FAMILY"); continue; }
            Check(o.Allocated == child.Quota && o.Consumed >= 0 && o.Consumed <= o.Allocated && o.NodeChecks >= 0 && o.AdjacencyChecks >= 0 &&
                (long)o.NodeChecks + o.AdjacencyChecks <= child.PreparationCeiling && o.ValidatedObservations >= 0 && o.ValidatedObservations <= g.WorkBudget, "FAMILY_COUNTS");
            int childResults = (o.Simple != null ? 1 : 0) + (o.One != null ? 1 : 0) + (o.Two != null ? 1 : 0);
            if (child.Applicability == TransmissionApplicability.ExcludedByGoal || child.Applicability == TransmissionApplicability.ProvenInfeasible)
            {
                Check(o.Status == (child.Applicability == TransmissionApplicability.ExcludedByGoal ? TransmissionFamilyStatus.ExcludedByGoal : TransmissionFamilyStatus.ProvenInfeasible) &&
                    childResults == 0 && o.Consumed == 0 && o.ValidatedObservations == 0 && o.NodeChecks == 0 && o.AdjacencyChecks == 0 && o.Reason == child.Reason, "EXCLUSION_OR_EXACT_PROOF");
                continue;
            }
            if (!plan.IsSupported) { Check(o.Status == TransmissionFamilyStatus.Unsupported && childResults == 0 && o.Consumed == 0, "UNSUPPORTED_IS_NOT_SEARCH"); continue; }
            if (child.Quota == 0 && o.Status != TransmissionFamilyStatus.Cancelled)
            { Check(o.Status == TransmissionFamilyStatus.PendingBudget && childResults == 0 && o.Consumed == 0 && o.ValidatedObservations == 0, "ZERO_QUOTA_PENDING"); continue; }
            if (o.Status == TransmissionFamilyStatus.Failed || o.Status == TransmissionFamilyStatus.Cancelled && childResults == 0)
            { Check(childResults == 0, "FAILED_RESULT_NOT_SUCCESSFUL_CHILD"); continue; }
            string request = ""; GearRoutingStatus status = GearRoutingStatus.InvalidInput; int work = -1, nodes = -1, pairs = -1, valid = -1;
            if (o.Simple != null && o.Family == TransmissionFamily.SimpleIdler) { var r = o.Simple; request = r.RequestId; status = r.Status; work = r.Summary.Expansions; nodes = r.Summary.NodeChecks; pairs = r.Summary.AdjacencyChecks; valid = r.Summary.ValidRoutes; }
            if (o.One != null && o.Family == TransmissionFamily.OneCompound) { var r = o.One; request = r.RequestId; status = r.Status; work = r.Summary.Work; nodes = r.Summary.NodeChecks; pairs = r.Summary.AdjacencyChecks; valid = r.Summary.ValidMechanisms; }
            if (o.Two != null && o.Family == TransmissionFamily.TwoCompound) { var r = o.Two; request = r.RequestId; status = r.Status; work = r.Summary.Work; nodes = r.Summary.NodeChecks; pairs = r.Summary.AdjacencyChecks; valid = r.Summary.ValidMechanisms; }
            Check(childResults == 1 && request == child.RequestId && o.Status == TransmissionSearchOrchestrator.Map(status) && work == o.Consumed && nodes == o.NodeChecks && pairs == o.AdjacencyChecks && valid == o.ValidatedObservations, "ACTUAL_CHILD_OBSERVATION");
        }
        Check(result.AllocatedWork <= g.WorkBudget && result.ConsumedWork <= result.AllocatedWork, "GLOBAL_WORK_POOL");
        if (plan.IsSupported) Check(result.Status == TransmissionSearchOrchestrator.Aggregate(result.Outcomes, result.ObservedUnique, result.Status == TransmissionGoalStatus.Cancelled), "GLOBAL_STATUS");
        else Check(result.Status == plan.Status && result.Candidates.Count == 0, "UNSUPPORTED_NOT_PARTIAL_COMPLETE");
        Check(result.ObservedUnique <= result.Outcomes.Sum(o => o.ValidatedObservations) && result.Candidates.Count == Math.Min(result.ObservedUnique, g.MaximumReturned), "OBSERVED_UNIQUE_NOT_UNSEEN_TOTAL");
        foreach (var c in result.Candidates)
        {
            var v = TransmissionGoalValidator.Validate(plan, c, identity); ds.AddRange(v.Diagnostics); Check(v.IsValid, "GOAL_CANDIDATE");
            foreach (var origin in c.Origins) Check(result.Outcomes.Any(o => o.Family == origin.Family && o.ValidatedObservations > 0 &&
                (o.Status == TransmissionFamilyStatus.Complete || o.Status == TransmissionFamilyStatus.IncompleteBudget || o.Status == TransmissionFamilyStatus.Cancelled || o.Status == TransmissionFamilyStatus.Failed)), "UNOBSERVED_CANDIDATE_ORIGIN");
        }
        return new ValidationBundle(ds);
    }
}
