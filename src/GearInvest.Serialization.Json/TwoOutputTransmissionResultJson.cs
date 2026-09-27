using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using static GearInvest.Serialization.GearRoutingJson;
using static GearInvest.Serialization.CompoundRoutingJson;

namespace GearInvest.Serialization;

public static partial class TwoOutputTransmissionJson
{
    private static void Observation(Utf8JsonWriter w, TwoOutputWholeObservation o)
    {
        w.WriteStartObject(); w.WritePropertyName("origin"); Origin(w, o.Origin); w.WritePropertyName("commonMetrics"); Metrics(w, o.Metrics);
        w.WriteBoolean("accepted", o.Accepted); Array(w, "diagnostics", o.Diagnostics, Issue); w.WriteEndObject();
    }
    public static int MeasureObservationBytes(TwoOutputWholeObservation observation) => ObjectBytes(w => Observation(w, observation)).Length;
    private static TwoOutputWholeObservation Observation(JsonElement p, TwoOutputSharingSearchPlan plan)
    {
        var row = new TwoOutputWholeObservation(Origin(p.GetProperty("origin"), plan), Metrics(p.GetProperty("commonMetrics")), A(p, "diagnostics", 64).Select(Issue), 0);
        return new TwoOutputWholeObservation(row.Origin, row.Metrics, row.Diagnostics, MeasureObservationBytes(row));
    }
    public static void ValidateCached(TwoOutputTransmissionResult result)
    {
        var v = TwoOutputTransmissionResultValidator.Validate(result, Mechanisms.ComputeCandidateId, SharedDriverTransmissionJson.PoolCandidateBytes, MeasureObservationBytes);
        Check(v.IsValid, string.Join(";", v.Diagnostics.Select(d => d.Code)));
        foreach (var o in result.Outcomes)
        {
            if (o.RootOnly != null) SharedDriverTransmissionJson.ValidateCached(o.RootOnly);
            if (o.SharedPrefix != null) SharedPrefixTransmissionJson.ValidateCached(o.SharedPrefix);
        }
    }
    public static byte[] WriteResult(TwoOutputTransmissionResult r)
    {
        ValidateCached(r);
        var bytes = Envelope(TwoOutputTransmissionContract.ResultFormat, w => {
            w.WriteStartObject(); Embed(w, "plan", WritePlan(r.Plan)); w.WriteString("status", r.Status.ToString()); w.WriteString("goalId", r.GoalId);
            w.WriteBoolean("hasValidatedCandidates", r.HasValidatedCandidates); w.WriteBoolean("searchComplete", r.SearchComplete); w.WriteBoolean("resultTruncated", r.ResultTruncated); w.WriteString("rankingGuarantee", r.RankingGuarantee);
            w.WriteNumber("consumedWork", r.ConsumedWork); w.WriteNumber("unusedAllocatedWork", r.UnusedAllocatedWork); w.WriteNumber("unallocatedWork", r.UnallocatedWork);
            w.WriteNumber("observedWholes", r.ObservedWholes); w.WriteNumber("acceptedObservations", r.AcceptedObservations); w.WriteNumber("rejectedObservations", r.RejectedObservations);
            w.WriteNumber("droppedObservations", r.DroppedObservations); w.WriteNumber("observationBytes", r.ObservationBytes); w.WriteNumber("uniqueAccepted", r.UniqueAccepted); w.WriteString("collectorStop", r.CollectorStop);
            Array(w, "perStrategyOutcomes", r.Outcomes, (a, o) => {
                a.WriteStartObject(); a.WriteString("strategy", o.Plan.Strategy.ToString()); a.WriteString("status", o.Status.ToString()); a.WriteString("reason", o.Reason); a.WriteBoolean("closed", o.Closed);
                a.WriteNumber("allocated", o.Plan.Quota); a.WriteNumber("consumed", o.ConsumedWork); a.WriteNumber("unused", o.UnusedWork); a.WriteNumber("observedWholes", o.ObservedWholes);
                a.WriteNumber("admittedWholes", o.AdmittedWholes); a.WriteNumber("droppedWholes", o.DroppedWholes);
                if (o.RootOnly != null) Embed(a, "originalChildResult", SharedDriverTransmissionJson.WriteResult(o.RootOnly));
                else if (o.SharedPrefix != null) Embed(a, "originalChildResult", SharedPrefixTransmissionJson.WriteResult(o.SharedPrefix));
                else a.WriteNull("originalChildResult"); a.WriteEndObject();
            });
            Array(w, "wholeObservations", r.Observations, Observation);
            w.WritePropertyName("collectorStopObservation"); if (r.CollectorStopObservation == null) w.WriteNullValue(); else Observation(w, r.CollectorStopObservation);
            Array(w, "candidates", r.Candidates, (a, c) => {
                a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); a.WriteString("contextId", c.ContextId(r.GoalId)); Array(a, "originIds", c.Origins, (b, o) => b.WriteStringValue(o.OriginId));
                a.WritePropertyName("metrics"); Metrics(a, c.Metrics); a.WriteEndObject();
            });
            Array(w, "diagnostics", r.Diagnostics, Issue); w.WriteString("cachedSearchClaims", "Producer claims, not historical exhaustive execution; Fresh repeats goal lowering and both active searches.");
            w.WriteString("physicalTorqueContactShaftBearingManufacturing", "NotPerformed"); w.WriteEndObject();
        });
        Check(bytes.Length <= TwoOutputTransmissionContract.MaxResultBytes, "two-output-result-byte-ceiling"); return bytes;
    }
    public static TwoOutputTransmissionResult ReadResult(byte[] bytes)
    {
        using var doc = Open(bytes, TwoOutputTransmissionContract.ResultFormat, TwoOutputTransmissionContract.MaxResultBytes); var p = doc.RootElement.GetProperty("payload");
        var plan = ReadPlan(Bytes(p.GetProperty("plan"))); var g = plan.Normalized.Goal!;
        var outcomes = A(p, "perStrategyOutcomes", 2).Select(o => {
            var s = E<TwoOutputSharingStrategy>(o, "strategy"); var strategy = plan.Strategies.Single(x => x.Strategy == s); var child = o.GetProperty("originalChildResult");
            return new TwoOutputStrategyOutcome(strategy, E<TwoOutputStrategyStatus>(o, "status"), I(o, "consumed"), I(o, "observedWholes"), I(o, "admittedWholes"), I(o, "droppedWholes"), S(o, "reason"),
                rootOnly: child.ValueKind != JsonValueKind.Null && s == TwoOutputSharingStrategy.RootOnly ? SharedDriverTransmissionJson.ReadResult(Bytes(child)) : null,
                sharedPrefix: child.ValueKind != JsonValueKind.Null && s == TwoOutputSharingStrategy.SharedPrefix ? SharedPrefixTransmissionJson.ReadResult(Bytes(child)) : null);
        }).ToArray();
        var observations = A(p, "wholeObservations", g.ObservationCountLimit).Select(o => Observation(o, plan)).ToArray();
        var stop = p.GetProperty("collectorStopObservation");
        var origins = observations.Select(o => o.Origin).GroupBy(o => o.OriginId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var candidates = A(p, "candidates", g.MaximumReturned).Select(c => new TwoOutputTransmissionCandidate(A(c, "originIds", 32).Select(o => origins[o.GetString()!]), Metrics(c.GetProperty("metrics"))));
        var r = new TwoOutputTransmissionResult(plan, E<TwoOutputTransmissionStatus>(p, "status"), outcomes, observations, candidates, I(p, "uniqueAccepted"), S(p, "collectorStop"), A(p, "diagnostics", 128).Select(Issue), stop.ValueKind == JsonValueKind.Null ? null : Observation(stop, plan));
        Check(bytes.SequenceEqual(WriteResult(r)), "two-output-result-canonical-context-ledger"); return r;
    }
}
