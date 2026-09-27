using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class TwoOutputTransmissionSearch
{
    public TwoOutputTransmissionResult Generate(TwoOutputTransmissionGoal goal, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolBytes, Func<SharedDriverTransmissionResult, int> rootResultBytes,
        Func<SharedPrefixTransmissionResult, int> prefixResultBytes, Func<TwoOutputWholeObservation, int> observationBytes,
        CancellationToken token = default, Action<int, int>? progress = null)
        => Run(goal, identity, poolBytes, rootResultBytes, prefixResultBytes, observationBytes, token, progress, null);

    // Test-only fault seam. Production dispatch is the concrete two-profile list, never a plug-in/mock registry.
    internal TwoOutputTransmissionResult Run(TwoOutputTransmissionGoal goal, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolBytes, Func<SharedDriverTransmissionResult, int> rootResultBytes,
        Func<SharedPrefixTransmissionResult, int> prefixResultBytes, Func<TwoOutputWholeObservation, int> observationBytes,
        CancellationToken token, Action<int, int>? progress, Action<TwoOutputSharingStrategy>? beforeStrategy)
    {
        var plan = TwoOutputTransmissionCompiler.Compile(goal); var ds = new List<Diagnostic>(plan.Diagnostics);
        var outcomes = new List<TwoOutputStrategyOutcome>(); var observations = new List<TwoOutputWholeObservation>();
        var unique = new Dictionary<string, TwoOutputTransmissionCandidate>(StringComparer.Ordinal);
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        string collectorStop = ""; int bytes = 0; bool collectorFailed = false; TwoOutputWholeObservation? stopObservation = null;
        TwoOutputTransmissionResult Result(TwoOutputTransmissionStatus status)
        {
            var ordered = unique.Values.ToList(); ordered.Sort(TwoOutputTransmissionValidator.Compare);
            return new TwoOutputTransmissionResult(plan, status, outcomes, observations, ordered.Take(plan.Normalized.Goal?.MaximumReturned ?? 1), unique.Count, collectorStop, ds, stopObservation);
        }
        if (!plan.Normalized.IsValid) return Result(plan.Normalized.Status);
        var g = plan.Normalized.Goal!;
        foreach (var strategy in plan.Strategies)
        {
            void Empty(TwoOutputStrategyStatus status, string reason) => outcomes.Add(new TwoOutputStrategyOutcome(strategy, status, 0, 0, 0, 0, reason));
            if (strategy.Applicability != TwoOutputStrategyApplicability.Eligible)
            {
                Empty((TwoOutputStrategyStatus)Enum.Parse(typeof(TwoOutputStrategyStatus), strategy.Applicability.ToString()), strategy.Reason); continue;
            }
            if (token.IsCancellationRequested) { Empty(TwoOutputStrategyStatus.Cancelled, "CANCELLED_BEFORE_STRATEGY"); continue; }
            int consumed = 0, observed = 0, admitted = 0, dropped = 0, alreadyConsumed = outcomes.Sum(o => o.ConsumedWork);
            void Work(int work, int limit)
            {
                if (work < consumed || work > strategy.Quota || limit != strategy.Quota) throw new InvalidOperationException("TwoOutputChildQuotaAccountingViolation");
                consumed = work; progress?.Invoke(alreadyConsumed + work, g.TotalWorkBudget);
            }
            void Observe(TwoOutputCandidateOrigin origin)
            {
                observed++;
                if (collectorStop.Length != 0 || collectorFailed) { dropped++; return; }
                // Stop admission, not the original child traversal. This keeps actual child status/count/transcript/bytes identical.
                // Unexamined common observations are counted as dropped and prohibit a Complete union, even if both children finish.
                try
                {
                    var metrics = TwoOutputTransmissionValidator.Metrics(g, origin);
                    var validation = TwoOutputTransmissionValidator.ValidateOrigin(plan, origin, identity);
                    int nextBytes = observationBytes(new TwoOutputWholeObservation(origin, metrics, validation.Diagnostics, 0));
                    if (nextBytes <= 0) throw new InvalidOperationException("InvalidCanonicalObservationByteMeasurement");
                    var row = new TwoOutputWholeObservation(origin, metrics, validation.Diagnostics, nextBytes);
                    void Stop(string reason) { collectorStop = reason; stopObservation = row; dropped++; }
                    if (observations.Count >= g.ObservationCountLimit) { Stop("COMMON_OBSERVATION_COUNT_CEILING"); return; }
                    if (nextBytes > g.ObservationByteLimit - bytes) { Stop("COMMON_OBSERVATION_BYTE_CEILING"); return; }
                    if (row.Accepted && unique.TryGetValue(origin.CandidateId, out var prior) && prior.Origins.Count == 32 && !prior.Origins.Any(o => o.OriginId == origin.OriginId))
                    { Stop("COMMON_ORIGIN_COUNT_CEILING"); return; }
                    observations.Add(row); bytes += nextBytes; admitted++;
                    if (!row.Accepted) return; // Common global rejection is not a child routing failure.
                    string signature = TransmissionGoalValidator.MechanicalSignature(origin.Mechanism);
                    if (unique.TryGetValue(origin.CandidateId, out var previous))
                    {
                        if (signatures[origin.CandidateId] != signature || previous.Origins[0].OutputBindingSignature != origin.OutputBindingSignature || previous.Metrics.Canonical != metrics.Canonical)
                            throw new InvalidOperationException("SameIdentityDifferentExactPayloadBindingOrScore");
                        var origins = previous.Origins.Concat(new[] { origin }).GroupBy(o => o.OriginId, StringComparer.Ordinal).Select(o => o.First()).ToArray();
                        unique[origin.CandidateId] = new TwoOutputTransmissionCandidate(origins, metrics);
                    }
                    else
                    {
                        signatures.Add(origin.CandidateId, signature);
                        unique.Add(origin.CandidateId, new TwoOutputTransmissionCandidate(new[] { origin }, metrics));
                    }
                }
                catch (Exception e)
                {
                    collectorFailed = true;
                    if (admitted + dropped < observed) dropped++;
                    ds.Add(TwoOutputTransmissionCompiler.Error("COLLECTOR_FAILURE", strategy.StrategyId, e.GetType().Name + ":" + e.Message));
                }
            }
            try
            {
                beforeStrategy?.Invoke(strategy.Strategy);
                if (strategy.Strategy == TwoOutputSharingStrategy.RootOnly)
                {
                    var child = new SharedDriverTransmissionSearch().GenerateObserved(strategy.RootOnly!.Normalized.Goal!, identity, poolBytes, rootResultBytes, token, Work,
                        c => Observe(new TwoOutputCandidateOrigin(strategy.ChildGoalId, rootOnly: c)));
                    if (child.Plan.PlanId != strategy.ChildPlanId || child.ConsumedWork != consumed || child.ApprovedPairs != observed)
                        throw new InvalidOperationException("RootOnlyPlanWorkOrWholeObservationMismatch");
                    outcomes.Add(new TwoOutputStrategyOutcome(strategy, Map(child.Status.ToString()), consumed, observed, admitted, dropped, child.LimitingReason, rootOnly: child));
                }
                else
                {
                    var child = new SharedPrefixTransmissionSearch().GenerateObserved(strategy.SharedPrefix!.Normalized.Goal!, identity, poolBytes, prefixResultBytes, token, Work,
                        c => Observe(new TwoOutputCandidateOrigin(strategy.ChildGoalId, sharedPrefix: c)));
                    if (child.Plan.PlanId != strategy.ChildPlanId || child.ConsumedWork != consumed || child.ApprovedTriples != observed)
                        throw new InvalidOperationException("SharedPrefixPlanWorkOrWholeObservationMismatch");
                    outcomes.Add(new TwoOutputStrategyOutcome(strategy, Map(child.Status.ToString()), consumed, observed, admitted, dropped, child.LimitingReason, sharedPrefix: child));
                }
            }
            catch (Exception e)
            {
                ds.Add(TwoOutputTransmissionCompiler.Error("CHILD_FAILURE", strategy.StrategyId, e.GetType().Name + ":" + e.Message));
                outcomes.Add(new TwoOutputStrategyOutcome(strategy, TwoOutputStrategyStatus.Failed, consumed, observed, admitted, dropped, "CHILD_FAILURE:" + e.GetType().Name));
            }
        }
        return Result(Aggregate(outcomes, unique.Count, collectorStop, token.IsCancellationRequested, ds.Any(d => d.Severity == DiagnosticSeverity.Error)));
    }

    private static TwoOutputStrategyStatus Map(string status) => (TwoOutputStrategyStatus)Enum.Parse(typeof(TwoOutputStrategyStatus), status);
    internal static TwoOutputTransmissionStatus Aggregate(IEnumerable<TwoOutputStrategyOutcome> outcomes, int candidates, string collectorStop, bool cancelled, bool failed)
    {
        var all = outcomes.ToArray();
        if (failed || all.Any(o => o.Status == TwoOutputStrategyStatus.Failed)) return TwoOutputTransmissionStatus.Failed;
        if (cancelled || all.Any(o => o.Status == TwoOutputStrategyStatus.Cancelled)) return TwoOutputTransmissionStatus.Cancelled;
        if (all.Any(o => o.Status == TwoOutputStrategyStatus.InvalidInput)) return TwoOutputTransmissionStatus.InvalidInput;
        if (all.Any(o => o.Status == TwoOutputStrategyStatus.Unsupported)) return TwoOutputTransmissionStatus.Unsupported;
        if (collectorStop.Length != 0 || all.Any(o => o.Status == TwoOutputStrategyStatus.IncompleteResource)) return TwoOutputTransmissionStatus.IncompleteResource;
        if (all.Length != 2 || all.Any(o => !o.Closed)) return TwoOutputTransmissionStatus.IncompleteBudget;
        return candidates > 0 ? TwoOutputTransmissionStatus.Complete : TwoOutputTransmissionStatus.Infeasible;
    }
}
