using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class TransmissionSearchOrchestrator
{
    public TransmissionGenerationResult Generate(TransmissionGoal goal, Func<GenerationCandidate, string> identity,
        CancellationToken token = default, Action<int, int>? progress = null) => Run(goal, identity, token, progress, null);

    // 16B consumes the same validated stream before either retained top-K. Null preserves the public 16A path byte-for-byte.
    internal TransmissionGenerationResult GenerateObserved(TransmissionGoal goal, Func<GenerationCandidate, string> identity,
        CancellationToken token, Action<int, int>? progress, Action<TransmissionMechanismCandidate> observer)
        => Run(goal, identity, token, progress, null, observer);

    // A narrow internal fault-injection seam for isolated failure aggregation tests. Product calls always
    // use the concrete static three-family registry below; no catalog/plugin/mock supplies mechanisms.
    internal TransmissionGenerationResult Run(TransmissionGoal goal, Func<GenerationCandidate, string> identity,
        CancellationToken token, Action<int, int>? progress, Action<TransmissionFamily>? beforeBackend, Action<TransmissionMechanismCandidate>? observer = null)
    {
        if (identity == null) throw new ArgumentNullException(nameof(identity));
        var plan = TransmissionGoalCompiler.Compile(goal); var outcomes = new List<TransmissionFamilyOutcome>(); var ds = new List<Diagnostic>(plan.Diagnostics);
        var collector = new TransmissionCandidateCollector(plan.Normalized.Goal?.MaximumReturned ?? 1); bool observationsExact = true;
        foreach (var child in plan.Families)
        {
            TransmissionFamilyOutcome Empty(TransmissionFamilyStatus s, string reason) => new TransmissionFamilyOutcome(child.Family, s, child.Quota, 0, 0, 0, 0, reason);
            if (child.Applicability == TransmissionApplicability.ExcludedByGoal) { outcomes.Add(Empty(TransmissionFamilyStatus.ExcludedByGoal, child.Reason)); continue; }
            if (child.Applicability == TransmissionApplicability.ProvenInfeasible) { outcomes.Add(Empty(TransmissionFamilyStatus.ProvenInfeasible, child.Reason)); continue; }
            if (!plan.IsSupported) { outcomes.Add(Empty(TransmissionFamilyStatus.Unsupported, "PLAN_NOT_EXECUTED:" + child.Reason)); continue; }
            if (token.IsCancellationRequested) { outcomes.Add(Empty(TransmissionFamilyStatus.Cancelled, "CANCELLED_BEFORE_CHILD")); continue; }
            if (child.Quota == 0) { outcomes.Add(Empty(TransmissionFamilyStatus.PendingBudget, "ZERO_FIXED_QUOTA_NO_BACKEND_CALL")); continue; }
            int consumed = 0, observed = 0, alreadyConsumed = outcomes.Sum(o => o.Consumed);
            void ObserveWork(int work, int limit)
            {
                if (work < consumed || work > child.Quota || limit != child.Quota) throw new InvalidOperationException("ChildQuotaAccountingViolation");
                consumed = work; progress?.Invoke(alreadyConsumed + work, plan.Normalized.Goal!.WorkBudget);
            }
            void Observe(TransmissionCandidateOrigin origin)
            {
                var candidate = new TransmissionMechanismCandidate(origin.CandidateId, new[] { origin }, TransmissionGoalValidator.Metrics(plan.Normalized.Goal!, origin.Mechanism));
                var validation = TransmissionGoalValidator.Validate(plan, candidate, identity);
                if (!validation.IsValid) throw new InvalidOperationException("LoweredCandidateGoalValidationFailed:" + string.Join(";", validation.Diagnostics.Select(d => d.Code)));
                observer?.Invoke(candidate);
                collector.Add(candidate); observed++;
            }
            try
            {
                beforeBackend?.Invoke(child.Family);
                GearRoutingStatus state; int actual, nodes, pairs, valid; string reason;
                GearRoutingResult? simple = null; CompoundGearRoutingResult? one = null; TwoCompoundGearRoutingResult? two = null;
                if (child.Family == TransmissionFamily.SimpleIdler)
                {
                    simple = new AnchoredGearRouter().GenerateObserved(child.Simple!, identity, token, ObserveWork, c => Observe(new TransmissionCandidateOrigin(c)));
                    state = simple.Status; actual = simple.Summary.Expansions; nodes = simple.Summary.NodeChecks; pairs = simple.Summary.AdjacencyChecks; valid = simple.Summary.ValidRoutes;
                    reason = string.Join(";", simple.Rejections.Select(r => r.Code));
                }
                else if (child.Family == TransmissionFamily.OneCompound)
                {
                    one = new AnchoredCompoundGearRouter().GenerateObserved(child.One!, identity, token, ObserveWork, c => Observe(new TransmissionCandidateOrigin(c)));
                    state = one.Status; actual = one.Summary.Work; nodes = one.Summary.NodeChecks; pairs = one.Summary.AdjacencyChecks; valid = one.Summary.ValidMechanisms;
                    reason = string.Join(";", one.Rejections.Select(r => r.Code));
                }
                else
                {
                    two = new AnchoredTwoCompoundGearRouter().GenerateObserved(child.Two!, identity, token, ObserveWork, c => Observe(new TransmissionCandidateOrigin(c)));
                    state = two.Status; actual = two.Summary.Work; nodes = two.Summary.NodeChecks; pairs = two.Summary.AdjacencyChecks; valid = two.Summary.ValidMechanisms;
                    reason = string.Join(";", two.Rejections.Select(r => r.Code));
                }
                if (actual != consumed || valid != observed || state == GearRoutingStatus.InvalidInput || state == GearRoutingStatus.Unsupported)
                    throw new InvalidOperationException("LowererOrChildContractViolation: validated child request/counters disagreed");
                outcomes.Add(new TransmissionFamilyOutcome(child.Family, Map(state), child.Quota, actual, nodes, pairs, valid,
                    reason.Length == 0 ? "DOMAIN_SEARCH_FINISHED" : reason, simple, one, two));
            }
            catch (Exception e)
            {
                // Failure is not empty/infeasible. Other independent children may still supply valid candidates.
                observationsExact = false;
                ds.Add(TransmissionGoalCompiler.Error("BACKEND_FAILURE", child.FamilyId, e.GetType().Name + ": " + e.Message));
                outcomes.Add(new TransmissionFamilyOutcome(child.Family, TransmissionFamilyStatus.Failed, child.Quota, consumed, 0, 0, observed,
                    "BACKEND_FAILURE:" + e.GetType().Name));
            }
        }
        var status = !plan.IsSupported ? plan.Status : Aggregate(outcomes, collector.UniqueCount, token.IsCancellationRequested);
        return new TransmissionGenerationResult(plan, status, outcomes, collector.Candidates, collector.UniqueCount, observationsExact, ds);
    }

    internal static TransmissionFamilyStatus Map(GearRoutingStatus status) => status switch {
        GearRoutingStatus.Complete => TransmissionFamilyStatus.Complete, GearRoutingStatus.Infeasible => TransmissionFamilyStatus.Infeasible,
        GearRoutingStatus.IncompleteBudget => TransmissionFamilyStatus.IncompleteBudget, GearRoutingStatus.Cancelled => TransmissionFamilyStatus.Cancelled,
        _ => TransmissionFamilyStatus.Failed };

    internal static TransmissionGoalStatus Aggregate(IEnumerable<TransmissionFamilyOutcome> outcomes, int candidates, bool cancelled)
    {
        var all = outcomes.ToArray();
        if (all.Any(o => o.Status == TransmissionFamilyStatus.Failed)) return TransmissionGoalStatus.Failed;
        if (cancelled || all.Any(o => o.Status == TransmissionFamilyStatus.Cancelled)) return TransmissionGoalStatus.Cancelled;
        if (all.Any(o => o.Status == TransmissionFamilyStatus.Unsupported)) return TransmissionGoalStatus.Unsupported;
        if (all.Any(o => o.Status == TransmissionFamilyStatus.PendingBudget || o.Status == TransmissionFamilyStatus.IncompleteBudget)) return TransmissionGoalStatus.IncompleteBudget;
        if (all.Length != 3 || all.All(o => o.Status == TransmissionFamilyStatus.ExcludedByGoal)) return TransmissionGoalStatus.Unsupported;
        return candidates > 0 ? TransmissionGoalStatus.Complete : TransmissionGoalStatus.Infeasible;
    }
}
