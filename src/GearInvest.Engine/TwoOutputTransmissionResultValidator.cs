using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Bounded cached structural/context validation. Fresh execution, not this ledger, proves reproducibility.</summary>
public static class TwoOutputTransmissionResultValidator
{
    public static ValidationBundle Validate(TwoOutputTransmissionResult r, Func<GenerationCandidate, string> identity,
        Func<TransmissionMechanismCandidate, int> poolBytes, Func<TwoOutputWholeObservation, int> observationBytes)
    {
        var ds = new List<Diagnostic>();
        void Check(bool ok, string code) { if (!ok && ds.Count < 128) ds.Add(TwoOutputTransmissionCompiler.Error("CACHED_" + code, "result", code)); }
        if (!r.Plan.Normalized.IsValid) { Check(false, "NORMALIZED_GOAL_REQUIRED"); return new ValidationBundle(ds); }
        var p = TwoOutputTransmissionCompiler.Compile(r.Plan.Normalized.Goal!); var g = p.Normalized.Goal!;
        Check(r.Plan.PlanId == p.PlanId && r.Plan.Strategies.Select(s => s.Canonical).SequenceEqual(p.Strategies.Select(s => s.Canonical)), "FRESH_COMMON_PLAN_AND_QUOTAS");
        Check(r.Outcomes.Count == 2 && r.Outcomes.Select(o => o.Plan.Canonical).SequenceEqual(p.Strategies.Select(s => s.Canonical)), "EXACT_STRATEGY_COVERAGE");
        if (r.Outcomes.Count != 2) return new ValidationBundle(ds);
        Check(p.AllocatedWork <= g.TotalWorkBudget && r.ConsumedWork <= p.AllocatedWork, "ONE_TOTAL_BUDGET");
        foreach (var outcome in r.Outcomes)
        {
            var strategy = p.Strategies.Single(s => s.Strategy == outcome.Plan.Strategy);
            Check(outcome.ConsumedWork >= 0 && outcome.ConsumedWork <= strategy.Quota && outcome.ObservedWholes >= 0 && outcome.AdmittedWholes >= 0 && outcome.DroppedWholes >= 0 &&
                outcome.ObservedWholes == outcome.AdmittedWholes + outcome.DroppedWholes, "STRATEGY_WORK_AND_OBSERVATION_COUNTS");
            var rows = r.Observations.Where(o => o.Origin.Strategy == strategy.Strategy).ToArray();
            var sourceRows = r.CollectorStopObservation?.Origin.Strategy == strategy.Strategy ? rows.Concat(new[] { r.CollectorStopObservation! }).ToArray() : rows;
            Check(rows.Length == outcome.AdmittedWholes, "PRE_RETENTION_WHOLE_STREAM_COUNT");
            if (strategy.Applicability != TwoOutputStrategyApplicability.Eligible)
            {
                Check(outcome.Status.ToString() == strategy.Applicability.ToString() && outcome.ConsumedWork == 0 && outcome.ObservedWholes == 0 &&
                    outcome.RootOnly == null && outcome.SharedPrefix == null && outcome.Reason == strategy.Reason, "NONEXECUTED_NOT_COMPLETED_OR_FALLBACK");
                continue;
            }
            if (outcome.RootOnly == null && outcome.SharedPrefix == null)
            {
                Check(outcome.Status == TwoOutputStrategyStatus.Cancelled || outcome.Status == TwoOutputStrategyStatus.Failed &&
                    r.Diagnostics.Any(d => d.Code == "TWO_OUTPUT_CHILD_FAILURE" && d.SubjectId == strategy.StrategyId), "MISSING_CHILD_IS_NOT_COMPLETE_OR_INFEASIBLE");
                continue;
            }
            if (strategy.Strategy == TwoOutputSharingStrategy.RootOnly)
            {
                Check(outcome.RootOnly != null && outcome.SharedPrefix == null, "ROOT_ONLY_TYPED_CHILD"); if (outcome.RootOnly == null) continue;
                var child = outcome.RootOnly; ds.AddRange(SharedDriverTransmissionResultValidator.Validate(child, identity, poolBytes).Diagnostics);
                Check(child.Plan.PlanId == strategy.ChildPlanId && outcome.ConsumedWork == child.ConsumedWork && outcome.Status.ToString() == child.Status.ToString() &&
                    outcome.ObservedWholes == child.ApprovedPairs && outcome.Reason == child.LimitingReason, "ORIGINAL_ROOT_CHILD_LEDGER");
                foreach (var row in sourceRows)
                {
                    var c = row.Origin.RootOnly; Check(c != null, "ROOT_TYPED_ORIGIN"); if (c == null) continue;
                    foreach (var o in c.Outputs) Check(child.Branches.Any(b => b.OutputKey == o.OutputKey && b.Pool.Any(v => v.CandidateId == o.Child.CandidateId &&
                        v.Origins.Select(x => x.OriginId).SequenceEqual(o.Child.Origins.Select(x => x.OriginId)))), "OBSERVED_ROOT_ORIGIN_IN_SOURCE_POOL");
                }
                if (!child.PairDetailsTruncated) Check(sourceRows.Select(o => o.Origin.CandidateId).SequenceEqual(child.PairDetails.Where(x => x.Approved).Take(sourceRows.Length).Select(x => x.CandidateId)), "ACTUAL_SMALL_ROOT_STREAM_ORDER");
            }
            else
            {
                Check(outcome.SharedPrefix != null && outcome.RootOnly == null, "PREFIX_TYPED_CHILD"); if (outcome.SharedPrefix == null) continue;
                var child = outcome.SharedPrefix; ds.AddRange(SharedPrefixTransmissionResultValidator.Validate(child, identity, poolBytes).Diagnostics);
                Check(child.Plan.PlanId == strategy.ChildPlanId && outcome.ConsumedWork == child.ConsumedWork && outcome.Status.ToString() == child.Status.ToString() &&
                    outcome.ObservedWholes == child.ApprovedTriples && outcome.Reason == child.LimitingReason, "ORIGINAL_PREFIX_CHILD_LEDGER");
                foreach (var row in sourceRows)
                {
                    var c = row.Origin.SharedPrefix; Check(c != null, "PREFIX_TYPED_ORIGIN"); if (c == null) continue;
                    var h = child.Hypotheses.SingleOrDefault(x => x.HypothesisId == c.HypothesisId); Check(h != null, "OBSERVED_HYPOTHESIS"); if (h == null) continue;
                    Check(h.Stages.Any(s => s.Role == "prefix" && s.Observation!.Pool.Any(v => v.CandidateId == c.Prefix.CandidateId)), "OBSERVED_PREFIX_IN_SOURCE_POOL");
                    foreach (var o in c.Outputs) Check(h.Stages.Any(s => s.Role == "output:" + o.OutputKey && s.Observation!.Pool.Any(v => v.CandidateId == o.Child.CandidateId &&
                        v.Origins.Select(x => x.OriginId).SequenceEqual(o.Child.Origins.Select(x => x.OriginId)))), "OBSERVED_SUFFIX_IN_SOURCE_POOL");
                }
                if (child.ExaminedTriples == child.Details.Count) Check(sourceRows.Select(o => o.Origin.CandidateId).SequenceEqual(child.Details.Where(x => x.CandidateId != null).Take(sourceRows.Length).Select(x => x.CandidateId)), "ACTUAL_SMALL_PREFIX_STREAM_ORDER");
            }
        }
        Check(r.Observations.Count <= g.ObservationCountLimit && r.ObservationBytes <= g.ObservationByteLimit, "COMMON_OBSERVATION_RESOURCE_BOUND");
        Check(r.Observations.Select(o => o.Origin.Strategy).SequenceEqual(r.Observations.Select(o => o.Origin.Strategy).OrderBy(s => s)), "CANONICAL_STRATEGY_OBSERVATION_ORDER");
        var grouped = new Dictionary<string, List<TwoOutputCandidateOrigin>>(StringComparer.Ordinal); var metrics = new Dictionary<string, TwoOutputCommonMetrics>(StringComparer.Ordinal);
        foreach (var row in r.Observations)
        {
            var v = TwoOutputTransmissionValidator.ValidateOrigin(p, row.Origin, identity);
            string D(IEnumerable<Diagnostic> d) => GearRoutingContract.List(DiagnosticOrdering.Canonicalize(d).Select(x => GearRoutingContract.Pack(x.Code, x.SubjectId ?? "", x.Message)));
            Check(D(row.Diagnostics) == D(v.Diagnostics) && row.Metrics.Canonical == TwoOutputTransmissionValidator.Metrics(g, row.Origin).Canonical && row.CanonicalBytes == observationBytes(row), "OBSERVATION_CONTEXT_METRICS_BYTES");
            if (!row.Accepted) continue;
            if (!grouped.TryGetValue(row.Origin.CandidateId, out var origins)) { grouped.Add(row.Origin.CandidateId, origins = new List<TwoOutputCandidateOrigin>()); metrics.Add(row.Origin.CandidateId, row.Metrics); }
            if (!origins.Any(o => o.OriginId == row.Origin.OriginId)) origins.Add(row.Origin);
        }
        var expected = grouped.Select(x => new TwoOutputTransmissionCandidate(x.Value, metrics[x.Key])).ToList(); expected.Sort(TwoOutputTransmissionValidator.Compare);
        Check(r.UniqueAccepted == expected.Count && r.Candidates.Select(c => c.ContextId(r.GoalId)).SequenceEqual(expected.Take(g.MaximumReturned).Select(c => c.ContextId(p.GoalId))), "GLOBAL_TOP_K_NOT_CHILD_RETENTION");
        foreach (var c in expected) ds.AddRange(TwoOutputTransmissionValidator.Validate(p, c, identity).Diagnostics);
        Check(r.DroppedObservations == 0 || r.CollectorStop.Length != 0 || r.Diagnostics.Any(d => d.Code == "TWO_OUTPUT_COLLECTOR_FAILURE"), "DROPPED_OBSERVATION_NOT_HIDDEN");
        Check((r.CollectorStop.Length == 0) == (r.CollectorStopObservation == null), "STOP_HAS_ACTUAL_DENIED_OBSERVATION");
        if (r.CollectorStopObservation is { } stop)
        {
            Check(stop.CanonicalBytes == observationBytes(stop) && stop.CanonicalBytes > 0 && r.DroppedObservations > 0, "EXACT_STOP_OBSERVATION_BYTES");
            var stopValidation = TwoOutputTransmissionValidator.ValidateOrigin(p, stop.Origin, identity);
            string Issues(IEnumerable<Diagnostic> d) => GearRoutingContract.List(DiagnosticOrdering.Canonicalize(d).Select(x => GearRoutingContract.Pack(x.Code, x.SubjectId ?? "", x.Message)));
            Check(Issues(stop.Diagnostics) == Issues(stopValidation.Diagnostics) && stop.Metrics.Canonical == TwoOutputTransmissionValidator.Metrics(g, stop.Origin).Canonical, "STOP_CONTEXT_METRICS_DIAGNOSTICS");
            // Source validity is independent of common admission. A denied observation may itself fail a common hard constraint.
            var source = stop.Origin; var strategy = p.Strategies.Single(s => s.Strategy == source.Strategy);
            Check(source.ChildGoalId == strategy.ChildGoalId && strategy.Applicability == TwoOutputStrategyApplicability.Eligible, "STOP_SOURCE_CONTEXT");
            if (source.RootOnly != null && strategy.RootOnly?.Normalized.Goal is { } root) ds.AddRange(SharedDriverTransmissionComposer.Validate(root, source.RootOnly, identity).Diagnostics);
            else if (source.SharedPrefix != null && strategy.SharedPrefix?.Normalized.Goal is { } prefix) ds.AddRange(SharedPrefixTransmissionComposer.Validate(prefix, source.SharedPrefix, identity).Diagnostics);
            else Check(false, "STOP_SOURCE_TYPE");
            if (r.CollectorStop == "COMMON_OBSERVATION_COUNT_CEILING") Check(r.Observations.Count == g.ObservationCountLimit, "COUNT_STOP_EVIDENCE");
            else if (r.CollectorStop == "COMMON_OBSERVATION_BYTE_CEILING") Check(r.ObservationBytes + stop.CanonicalBytes > g.ObservationByteLimit, "BYTE_STOP_EVIDENCE");
            else if (r.CollectorStop == "COMMON_ORIGIN_COUNT_CEILING") Check(grouped.TryGetValue(source.CandidateId, out var prior) && prior.Count == 32 && prior.All(o => o.OriginId != source.OriginId), "ORIGIN_STOP_EVIDENCE");
            else Check(false, "KNOWN_COLLECTOR_RESOURCE_REASON");
        }
        var status = TwoOutputTransmissionSearch.Aggregate(r.Outcomes, r.UniqueAccepted, r.CollectorStop, r.Status == TwoOutputTransmissionStatus.Cancelled, r.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        Check(r.Status == status && (!r.SearchComplete || r.DroppedObservations == 0 && r.CollectorStop.Length == 0), "GLOBAL_UNION_CLOSURE_NOT_ONE_PROFILE_PROOF");
        return new ValidationBundle(ds.Take(128));
    }
}
