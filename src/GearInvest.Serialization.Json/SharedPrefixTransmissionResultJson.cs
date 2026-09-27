using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using static GearInvest.Serialization.GearRoutingJson;
using static GearInvest.Serialization.CompoundRoutingJson;

namespace GearInvest.Serialization;

public static partial class SharedPrefixTransmissionJson
{
    private static void Mappings(Utf8JsonWriter w, string name, IEnumerable<SharedDriverRoleMapping> values) => Array(w, name, values, (a, m) => {
        a.WriteStartObject(); a.WriteString("kind", m.Kind); a.WriteString("localId", m.LocalId); a.WriteString("mergedId", m.MergedId); a.WriteEndObject(); });
    private static SharedDriverRoleMapping[] Mappings(JsonElement p, string name) => A(p, name, 128).Select(m => new SharedDriverRoleMapping(S(m, "kind"), S(m, "localId"), S(m, "mergedId"))).ToArray();
    public static void ValidateCached(SharedPrefixTransmissionResult r)
    {
        var v = SharedPrefixTransmissionResultValidator.Validate(r, Mechanisms.ComputeCandidateId, SharedDriverTransmissionJson.PoolCandidateBytes);
        Check(v.IsValid, string.Join(";", v.Diagnostics.Select(d => d.Code)));
        foreach (var s in r.Hypotheses.SelectMany(h => h.Stages).Where(s => s.Observation != null)) TransmissionGoalJson.ValidateCached(s.Observation!.Result);
        if (r.LimitingReason == "RESULT_BYTE_RESERVE_CEILING") Check(MeasureResultBytes(r) > SharedPrefixTransmissionContract.MaxResultBytes - SharedPrefixTransmissionContract.ResultReserveBytes, "prefix-resource-stop-byte-evidence");
    }
    public static byte[] WriteResult(SharedPrefixTransmissionResult r) { ValidateCached(r); return ResultBytes(r); }
    public static int MeasureResultBytes(SharedPrefixTransmissionResult r) => ResultBytes(r).Length;
    private static byte[] ResultBytes(SharedPrefixTransmissionResult r) => Envelope(SharedPrefixTransmissionContract.ResultFormat, w => {
        w.WriteStartObject(); Embed(w, "goal", WriteGoal(r.Plan.Normalized.Goal!)); Embed(w, "plan", WritePlan(r.Plan)); w.WriteString("status", r.Status.ToString()); w.WriteString("limitingReason", r.LimitingReason);
        w.WriteBoolean("hasValidatedCandidates", r.HasValidatedCandidates); w.WriteBoolean("allRelevantSearchesClosed", r.AllRelevantSearchesClosed); w.WriteBoolean("observedJoinExhausted", r.ObservedJoinExhausted);
        w.WriteBoolean("searchComplete", r.SearchComplete); w.WriteBoolean("resultTruncated", r.ResultTruncated); w.WriteString("rankingGuarantee", r.RankingGuarantee);
        w.WriteNumber("consumedPrefixWork", r.ConsumedPrefixWork); w.WriteNumber("consumedSuffixWork", r.ConsumedSuffixWork); w.WriteNumber("consumedWork", r.ConsumedWork);
        w.WriteNumber("unusedAllocatedPrefixWork", r.Plan.AllocatedPrefixWork - r.ConsumedPrefixWork); w.WriteNumber("unusedAllocatedSuffixWork", r.Plan.AllocatedSuffixWork - r.ConsumedSuffixWork);
        w.WriteNumber("unallocatedPrefixWork", r.Plan.Normalized.Goal!.ReservedPrefixWork - r.Plan.AllocatedPrefixWork); w.WriteNumber("unallocatedSuffixWork", r.Plan.Normalized.Goal!.SuffixWorkPool - r.Plan.AllocatedSuffixWork);
        w.WriteNumber("unusedJoinWork", r.Plan.Normalized.Goal!.ReservedJoinWork - r.ExaminedTriples); w.WriteNumber("poolBytes", r.PoolBytes);
        w.WriteNumber("examinedTriples", r.ExaminedTriples); w.WriteNumber("approvedTriples", r.ApprovedTriples); w.WriteNumber("rejectedTriples", r.RejectedTriples); w.WriteNumber("observedTripleDomain", r.ObservedTripleDomain); w.WriteNumber("uniqueMerged", r.UniqueMerged);
        Array(w, "hypotheses", r.Hypotheses, (a, h) => {
            a.WriteStartObject(); a.WriteString("hypothesisId", h.HypothesisId); a.WriteString("closureProof", h.ClosureProof); a.WriteBoolean("closed", h.Closed); a.WriteBoolean("allStagesComplete", h.AllStagesComplete);
            a.WriteNumber("examined", h.Examined); a.WriteNumber("approved", h.Approved); a.WriteNumber("rejected", h.Rejected); a.WriteNumber("observedProduct", h.ObservedProduct);
            Array(a, "stages", h.Stages, (b, s) => {
                b.WriteStartObject(); b.WriteString("role", s.Role); b.WriteString("notExecutedReason", s.NotExecutedReason); b.WriteBoolean("searchComplete", s.SearchComplete); b.WriteBoolean("provenEmpty", s.ProvenEmpty);
                if (s.Observation == null) b.WriteNull("generation"); else
                {
                    Embed(b, "generation", TransmissionGoalJson.WriteResult(s.Observation.Result)); b.WriteNumber("poolBytes", s.Observation.PoolBytes); b.WriteBoolean("resourceStopped", s.Observation.ResourceStopped);
                    Array(b, "pool", s.Observation.Pool, SharedDriverTransmissionJson.PoolCandidate);
                }
                b.WriteEndObject();
            }); a.WriteEndObject();
        });
        Array(w, "tripleDetails", r.Details, (a, t) => { a.WriteStartObject(); a.WriteString("hypothesisId", t.HypothesisId); a.WriteString("prefixId", t.PrefixId); a.WriteString("leftId", t.LeftId); a.WriteString("rightId", t.RightId); a.WriteString("candidateId", t.CandidateId); Array(a, "diagnostics", t.Diagnostics, Issue); a.WriteEndObject(); });
        w.WriteBoolean("tripleDetailsTruncated", r.ExaminedTriples > r.Details.Count); w.WriteString("tripleTranscriptHash", r.Transcript);
        Array(w, "candidates", r.Candidates, (a, c) => {
            a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); a.WriteString("contextId", c.ContextId(r.GoalId)); a.WriteString("hypothesisId", c.HypothesisId); a.WriteString("prefixCandidateId", c.Prefix.CandidateId);
            a.WritePropertyName("splitterTransfer"); R(a, c.SplitterTransfer); a.WriteString("actualInputDof", SharedPrefixTransmissionContract.RootDof); a.WriteString("splitterDof", SharedPrefixTransmissionContract.SplitterDof); Mappings(a, "prefixMappings", c.PrefixMappings);
            a.WritePropertyName("metrics"); TransmissionGoalJson.Metrics(a, c.Metrics); a.WriteNumber("prefixIdlers", c.PrefixIdlers); a.WriteNumber("splitterCount", c.SplitterCount); a.WriteNumber("suffixIdlers", c.SuffixIdlers);
            Array(a, "outputs", c.Outputs, (b, o) => {
                b.WriteStartObject(); b.WriteString("outputKey", o.OutputKey); b.WriteString("childGoalId", o.ChildGoalId); b.WriteString("childCandidateId", o.Child.CandidateId); Array(b, "childOrigins", o.Child.Origins, (x, origin) => x.WriteStringValue(origin.OriginId));
                b.WriteString("axisId", o.AxisId); b.WriteString("dofId", o.DofId); b.WriteString("bodyId", o.BodyId); b.WritePropertyName("requestedGlobalTransfer"); R(b, o.RequestedTransfer);
                b.WritePropertyName("actualGlobalTransfer"); R(b, o.ActualTransfer); b.WritePropertyName("actualLocalTransfer"); R(b, o.ActualTransfer / c.SplitterTransfer); Mappings(b, "roleMappings", o.Mappings); b.WriteEndObject();
            });
            Embed(a, "mechanism", WriteMechanism(c).Bytes); a.WriteStartObject("validation"); a.WriteBoolean("isValid", true);
            a.WriteString("scope", "part context; exact prefix and local/global targets; single original driver; one prefix/splitter primitive union; whole graph/playback; cross-part pitch envelopes; scoped environment; global limits");
            a.WriteString("historicalSearchProof", "NotPerformed: cached structure is not fresh exhaustive execution"); a.WriteString("physicalTorqueContactShaftBearingManufacturing", "NotPerformed"); a.WriteEndObject(); a.WriteEndObject();
        }); Array(w, "diagnostics", r.Diagnostics, Issue); w.WriteEndObject();
    });
    public static SharedPrefixTransmissionResult ReadResult(byte[] bytes)
    {
        using var doc = Open(bytes, SharedPrefixTransmissionContract.ResultFormat, SharedPrefixTransmissionContract.MaxResultBytes); var p = doc.RootElement.GetProperty("payload");
        var g = ReadGoal(Bytes(p.GetProperty("goal"))); var plan = SharedPrefixTransmissionCompiler.Compile(g); Check(Bytes(p.GetProperty("plan")).SequenceEqual(WritePlan(plan)), "prefix-fresh-hypotheses-quotas");
        var observations = A(p, "hypotheses", 8).Select(h => {
            var id = S(h, "hypothesisId"); var hp = plan.Hypotheses.Single(x => x.HypothesisId == id);
            var stages = A(h, "stages", 3).Select(s => {
                string role = S(s, "role"); SharedDriverBranchObservation? observation = null;
                if (s.GetProperty("generation").ValueKind != JsonValueKind.Null)
                {
                    var childPlan = role == "prefix" ? hp.Prefix! : hp.Suffix!.Branches.Single(b => "output:" + b.OutputKey == role).Plan;
                    observation = new SharedDriverBranchObservation(role, TransmissionGoalJson.ReadResult(Bytes(s.GetProperty("generation"))), A(s, "pool", g.PoolCountLimit).Select(c => SharedDriverTransmissionJson.PoolCandidate(c, childPlan)), I(s, "poolBytes"), s.GetProperty("resourceStopped").GetBoolean());
                }
                return new SharedPrefixStageObservation(role, observation, S(s, "notExecutedReason"));
            });
            return new SharedPrefixHypothesisObservation(id, stages, I(h, "examined"), I(h, "approved"), S(h, "closureProof"));
        }).ToArray();
        var candidates = A(p, "candidates", g.MaximumReturned).Select(c => {
            var h = observations.Single(x => x.HypothesisId == S(c, "hypothesisId")); var prefix = h.Stages[0].Observation!.Pool.Single(x => x.CandidateId == S(c, "prefixCandidateId"));
            var artifactBytes = Bytes(c.GetProperty("mechanism")); Check(artifactBytes.Length <= 512 * 1024, "prefix-mechanism-byte-limit"); var artifact = Mechanisms.Read(artifactBytes); var identity = Mechanisms.VerifyIdentity(artifact);
            Check(identity.ArtifactHashMatches && identity.CandidateIdMatches, "prefix-artifact-identity");
            var outputs = A(c, "outputs", 2).Select(o => new SharedDriverOutputBinding(S(o, "outputKey"), S(o, "childGoalId"),
                h.Stages.Single(s => s.Role == "output:" + S(o, "outputKey")).Observation!.Pool.Single(x => x.CandidateId == S(o, "childCandidateId")),
                S(o, "axisId"), S(o, "dofId"), S(o, "bodyId"), R(o.GetProperty("requestedGlobalTransfer")), R(o.GetProperty("actualGlobalTransfer")), Mappings(o, "roleMappings")));
            return new SharedPrefixTransmissionCandidate(S(c, "candidateId"), h.HypothesisId, prefix, artifact.Candidate, R(c.GetProperty("splitterTransfer")), Mappings(c, "prefixMappings"), outputs, TransmissionGoalJson.Metrics(c.GetProperty("metrics")));
        }).ToArray();
        var details = A(p, "tripleDetails", 64).Select(t => new SharedPrefixTripleObservation(S(t, "hypothesisId"), S(t, "prefixId"), S(t, "leftId"), S(t, "rightId"), t.GetProperty("candidateId").GetString(), A(t, "diagnostics", 16).Select(Issue)));
        var r = new SharedPrefixTransmissionResult(plan, E<SharedPrefixTransmissionStatus>(p, "status"), observations, candidates, I(p, "uniqueMerged"), details, S(p, "tripleTranscriptHash"), S(p, "limitingReason"), A(p, "diagnostics", 128).Select(Issue));
        Check(bytes.SequenceEqual(WriteResult(r)), "prefix-result-noncanonical/context/derived-fields"); return r;
    }
}
