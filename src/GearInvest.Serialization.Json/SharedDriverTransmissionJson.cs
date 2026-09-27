using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using static GearInvest.Serialization.GearRoutingJson;
using static GearInvest.Serialization.CompoundRoutingJson;

namespace GearInvest.Serialization;

/// <summary>Additive goal/search sidecars; mechanism 0.1 remains the independent playback authority.</summary>
public static class SharedDriverTransmissionJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    private static void Ints(Utf8JsonWriter w, string name, System.Collections.Generic.IEnumerable<int> values) => Array(w, name, values, (a, v) => a.WriteNumberValue(v));
    private static int[] Ints(JsonElement e, string name, int bound = 3) => A(e, name, bound).Select(x => x.GetInt32()).ToArray();
    public static byte[] WriteGoal(SharedDriverTransmissionGoal goal)
    {
        var n = SharedDriverTransmissionCompiler.Normalize(goal); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code))); var g = n.Goal!;
        var bytes = Envelope(SharedDriverTransmissionContract.GoalFormat, w => {
            w.WriteStartObject(); w.WriteString("goalId", n.GoalId); w.WriteString("profile", g.Profile); w.WriteString("lowering", g.Lowering); w.WriteString("allocation", g.Allocation);
            w.WriteString("join", g.Join); w.WriteString("ranking", g.Ranking); w.WriteString("resources", g.Resources);
            Anchor(w, "input", g.Input); Number(w, "pitchRadiusTicksPerTooth", g.PitchRadiusTicksPerTooth); w.WriteNumber("inputLayer", g.InputLayer);
            w.WriteString("driver", "single-prescribed-root"); w.WriteString("phaseConvention", "zero-turns"); w.WriteString("axisProfile", "fixed-parallel-xy");
            Array(w, "outputs", g.Outputs, (a, o) => {
                a.WriteStartObject(); a.WriteString("key", o.Key); Anchor(a, "anchor", o.Anchor); a.WritePropertyName("targetTransfer"); R(a, o.TargetTransfer);
                Ints(a, "outputLayers", o.OutputLayers); a.WriteString("layerPolicy", o.LayerPolicy.ToString()); Array(a, "allowedFamilies", o.AllowedFamilies, (b, f) => b.WriteStringValue(f.ToString()));
                a.WriteNumber("maximumCompounds", o.MaximumCompounds); a.WriteNumber("maximumIdlers", o.MaximumIdlers); Array(a, "slots", o.Slots, TransmissionGoalJson.Slot); Array(a, "legs", o.Legs, Leg); a.WriteEndObject();
            });
            w.WritePropertyName("bounds"); Box(w, g.Bounds); Ints(w, "availableLayers", g.AvailableLayers);
            Array(w, "keepOuts", g.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); a.WritePropertyName("bounds"); Box(a, k.Bounds); Ints(a, "layers", Enumerable.Range(0, 3).Where(k.AppliesTo)); a.WriteEndObject(); });
            Number(w, "unrelatedClearance", g.UnrelatedClearance); Number(w, "keepOutClearance", g.KeepOutClearance); w.WriteNumber("maximumTotalCompounds", g.MaximumTotalCompounds); w.WriteNumber("maximumTotalIdlers", g.MaximumTotalIdlers);
            OptionalBox(w, "preferredRegion", g.PreferredRegion); Array(w, "requiredRegions", g.RequiredRegions, (a, r) => { a.WriteStartObject(); a.WriteString("id", r.Id); a.WritePropertyName("bounds"); Box(a, r.Bounds); a.WriteEndObject(); });
            w.WriteNumber("totalWorkBudget", g.TotalWorkBudget); w.WriteNumber("reservedCombinationWork", g.ReservedCombinationWork); w.WriteNumber("maximumReturned", g.MaximumReturned);
            w.WriteNumber("poolCountLimit", g.PoolCountLimit); w.WriteNumber("poolByteLimit", g.PoolByteLimit); w.WriteNumber("pairDomainLimit", g.PairDomainLimit); w.WriteEndObject();
        });
        Check(bytes.Length <= SharedDriverTransmissionContract.MaxGoalBytes, "shared-goal-byte-limit"); return bytes;
    }
    public static SharedDriverTransmissionGoal ReadGoal(byte[] bytes)
    {
        using var d = Open(bytes, SharedDriverTransmissionContract.GoalFormat, SharedDriverTransmissionContract.MaxGoalBytes); var p = d.RootElement.GetProperty("payload");
        var outputs = A(p, "outputs", 2).Select(o => {
            var slots = A(o, "slots", 2).Select(TransmissionGoalJson.Slot).ToArray(); var legs = A(o, "legs", 3).Select(Leg).ToArray(); Check(slots.Length == 2 && legs.Length == 3, "resolved-branch-domain-count");
            var families = A(o, "allowedFamilies", 3).Select(x => { var s = x.GetString(); Check(Enum.TryParse<TransmissionFamily>(s, out var f) && Enum.IsDefined(typeof(TransmissionFamily), f) && f.ToString() == s, "unknown-family"); return f; });
            return new SharedDriverTransmissionOutput(S(o, "key"), Anchor(o.GetProperty("anchor")), R(o.GetProperty("targetTransfer")), legs[0], slots[0], Ints(o, "outputLayers"),
                E<TransmissionOutputLayerPolicy>(o, "layerPolicy"), families, I(o, "maximumCompounds"), I(o, "maximumIdlers"), slot1: slots[1], leg1: legs[1], leg2: legs[2]);
        }).ToArray();
        var keepOuts = A(p, "keepOuts", 16).Select(k => { var layers = Ints(k, "layers"); Check(layers.Length > 0 && layers.All(l => l >= 0 && l <= 2), "shared-keepout-layer-set");
            return new ThreeLayerGearRouteKeepOut(S(k, "id"), Box(k.GetProperty("bounds")), (ThreeRouteLayers)layers.Aggregate(0, (m, l) => m | (1 << l))); });
        var goal = new SharedDriverTransmissionGoal(Anchor(p.GetProperty("input")), N(p.GetProperty("pitchRadiusTicksPerTooth")), outputs, Box(p.GetProperty("bounds")), Ints(p, "availableLayers"), keepOuts,
            N(p.GetProperty("unrelatedClearance")), N(p.GetProperty("keepOutClearance")), I(p, "maximumTotalCompounds"), I(p, "maximumTotalIdlers"), OptionalBox(p, "preferredRegion"),
            A(p, "requiredRegions", 16).Select(r => new GearRouteRegion(S(r, "id"), Box(r.GetProperty("bounds")))), I(p, "totalWorkBudget"), I(p, "reservedCombinationWork"), I(p, "maximumReturned"), I(p, "inputLayer"),
            S(p, "profile"), S(p, "lowering"), S(p, "allocation"), S(p, "join"), S(p, "ranking"), S(p, "resources"), I(p, "poolCountLimit"), I(p, "poolByteLimit"), I(p, "pairDomainLimit"));
        Check(bytes.SequenceEqual(WriteGoal(goal)), "shared-goal-noncanonical/identity/policy"); return SharedDriverTransmissionCompiler.Normalize(goal).Goal!;
    }

    public static byte[] WritePlan(SharedDriverTransmissionSearchPlan p) => WriteObject(w => {
        w.WriteStartObject(); w.WriteString("goalId", p.GoalId); w.WriteString("planId", p.PlanId); w.WriteString("status", p.Status.ToString()); w.WriteNumber("allocatedGenerationWork", p.AllocatedGenerationWork);
        w.WriteNumber("generationWorkPool", p.Normalized.Goal?.GenerationWorkPool ?? 0); w.WriteNumber("reservedCombinationWork", p.Normalized.Goal?.ReservedCombinationWork ?? 0);
        Array(w, "branches", p.Branches, (a, b) => { a.WriteStartObject(); a.WriteString("outputKey", b.OutputKey); Embed(a, "goal", TransmissionGoalJson.WriteGoal(b.Plan.Normalized.Goal!)); Embed(a, "plan", TransmissionGoalJson.WritePlan(b.Plan)); a.WriteEndObject(); });
        Array(w, "diagnostics", p.Diagnostics, Issue); w.WriteEndObject();
    });

    private static byte[] WriteObject(Action<Utf8JsonWriter> action)
    { using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { action(writer); writer.Flush(); } return stream.ToArray(); }
    internal static void PoolCandidate(Utf8JsonWriter w, TransmissionMechanismCandidate c)
    {
        w.WriteStartObject(); w.WriteString("candidateId", c.CandidateId); w.WritePropertyName("metrics"); TransmissionGoalJson.Metrics(w, c.Metrics); Array(w, "origins", c.Origins, TransmissionGoalJson.Origin); w.WriteEndObject();
    }
    public static int PoolCandidateBytes(TransmissionMechanismCandidate c) => WriteObject(w => PoolCandidate(w, c)).Length;
    internal static TransmissionMechanismCandidate PoolCandidate(JsonElement p, CompiledTransmissionSearchPlan plan) => new TransmissionMechanismCandidate(S(p, "candidateId"),
        A(p, "origins", 3).Select(o => TransmissionGoalJson.Origin(o, plan, S(p, "candidateId"))), TransmissionGoalJson.Metrics(p.GetProperty("metrics")));

    public static ArtifactWriteResult WriteMechanism(SharedDriverTransmissionCandidate candidate) => Mechanisms.Write(candidate.Mechanism);
    public static void ValidateCached(SharedDriverTransmissionResult r)
    {
        var validation = SharedDriverTransmissionResultValidator.Validate(r, Mechanisms.ComputeCandidateId, PoolCandidateBytes);
        Check(validation.IsValid, string.Join(";", validation.Diagnostics.Select(d => d.Code)));
        foreach (var branch in r.Branches) TransmissionGoalJson.ValidateCached(branch.Result);
        if (r.LimitingReason == "RESULT_BYTE_RESERVE_CEILING")
            Check(MeasureResultBytes(r) > SharedDriverTransmissionContract.MaxResultBytes - SharedDriverTransmissionContract.ResultReserveBytes, "shared-result-resource-stop-without-byte-evidence");
    }
    public static byte[] WriteResult(SharedDriverTransmissionResult r)
    {
        ValidateCached(r); return ResultBytes(r);
    }
    // Used only by the search resource meter: it measures actual transport bytes, never validates or repairs a search result.
    public static int MeasureResultBytes(SharedDriverTransmissionResult r) => ResultBytes(r).Length;
    private static byte[] ResultBytes(SharedDriverTransmissionResult r)
    {
        var bytes = Envelope(SharedDriverTransmissionContract.ResultFormat, w => {
            w.WriteStartObject(); Embed(w, "goal", WriteGoal(r.Plan.Normalized.Goal!)); Embed(w, "plan", WritePlan(r.Plan)); w.WriteString("status", r.Status.ToString());
            w.WriteBoolean("hasValidatedMultiOutputCandidates", r.HasValidatedMultiOutputCandidates); w.WriteBoolean("allBranchSearchesComplete", r.AllBranchSearchesComplete); w.WriteBoolean("observedPairDomainExhausted", r.ObservedPairDomainExhausted);
            w.WriteBoolean("searchComplete", r.SearchComplete); w.WriteBoolean("resultTruncated", r.ResultTruncated); w.WriteString("rankingGuarantee", r.RankingGuarantee); w.WriteString("limitingReason", r.LimitingReason); w.WriteString("exactEmptyProof", r.ExactEmptyProof);
            w.WriteNumber("consumedGenerationWork", r.ConsumedGenerationWork); w.WriteNumber("examinedPairs", r.ExaminedPairs); w.WriteNumber("approvedPairs", r.ApprovedPairs); w.WriteNumber("rejectedPairs", r.RejectedPairs); w.WriteNumber("uniqueMerged", r.UniqueMerged);
            w.WriteNumber("observedPairDomain", r.ObservedPairDomain); w.WriteNumber("consumedWork", r.ConsumedWork); w.WriteNumber("unusedAllocatedGenerationWork", r.Plan.AllocatedGenerationWork - r.ConsumedGenerationWork);
            w.WriteNumber("unallocatedGenerationWork", r.Plan.Normalized.Goal!.GenerationWorkPool - r.Plan.AllocatedGenerationWork); w.WriteNumber("unusedCombinationWork", r.Plan.Normalized.Goal!.ReservedCombinationWork - r.ExaminedPairs);
            Array(w, "branches", r.Branches, (a, b) => { a.WriteStartObject(); a.WriteString("outputKey", b.OutputKey); Embed(a, "generation", TransmissionGoalJson.WriteResult(b.Result)); a.WriteNumber("poolBytes", b.PoolBytes); a.WriteBoolean("resourceStopped", b.ResourceStopped);
                Array(a, "pool", b.Pool, PoolCandidate); a.WriteEndObject(); });
            Array(w, "pairDetails", r.PairDetails, (a, p) => { a.WriteStartObject(); a.WriteString("leftId", p.LeftId); a.WriteString("rightId", p.RightId); a.WriteString("candidateId", p.CandidateId); Array(a, "diagnostics", p.Diagnostics, Issue); a.WriteEndObject(); });
            w.WriteBoolean("pairDetailsTruncated", r.PairDetailsTruncated); w.WriteString("pairTranscriptHash", r.PairTranscriptHash);
            Array(w, "candidates", r.Candidates, (a, c) => {
                a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); a.WriteString("contextId", c.ContextId(r.GoalId)); a.WritePropertyName("metrics"); TransmissionGoalJson.Metrics(a, c.Metrics);
                Array(a, "outputs", c.Outputs, (b, o) => { b.WriteStartObject(); b.WriteString("outputKey", o.OutputKey); b.WriteString("childGoalId", o.ChildGoalId); b.WriteString("childCandidateId", o.Child.CandidateId);
                    Array(b, "childOrigins", o.Child.Origins, (e, origin) => e.WriteStringValue(origin.OriginId)); b.WriteString("axisId", o.AxisId); b.WriteString("dofId", o.DofId); b.WriteString("bodyId", o.BodyId);
                    b.WritePropertyName("requestedTransfer"); R(b, o.RequestedTransfer); b.WritePropertyName("actualTransfer"); R(b, o.ActualTransfer);
                    Array(b, "roleMappings", o.Mappings, (e, m) => { e.WriteStartObject(); e.WriteString("kind", m.Kind); e.WriteString("localId", m.LocalId); e.WriteString("mergedId", m.MergedId); e.WriteEndObject(); }); b.WriteEndObject(); });
                Embed(a, "mechanism", WriteMechanism(c).Bytes); a.WriteStartObject("validation"); a.WriteBoolean("isValid", true); a.WriteString("scope", "child goal; identical shared input; root-only remap; whole graph; both exact transfers; ideal pitch envelopes; cross-branch contacts; declared environment");
                a.WriteString("historicalSearchProof", "NotPerformed: cached structural validation is not fresh generation"); a.WriteString("toothAssemblyTorqueDynamicsShaftBearingManufacturing", "NotPerformed"); a.WriteEndObject(); a.WriteEndObject();
            }); Array(w, "diagnostics", r.Diagnostics, Issue); w.WriteEndObject();
        });
        Check(bytes.Length <= SharedDriverTransmissionContract.MaxResultBytes, "shared-result-byte-limit"); return bytes;
    }
    public static SharedDriverTransmissionResult ReadResult(byte[] bytes)
    {
        using var d = Open(bytes, SharedDriverTransmissionContract.ResultFormat, SharedDriverTransmissionContract.MaxResultBytes); var p = d.RootElement.GetProperty("payload");
        var goal = ReadGoal(Bytes(p.GetProperty("goal"))); var plan = SharedDriverTransmissionCompiler.Compile(goal);
        Check(Bytes(p.GetProperty("plan")).SequenceEqual(WritePlan(plan)), "shared-fresh-plan-binding");
        var branches = A(p, "branches", 2).Select(b => {
            var key = S(b, "outputKey"); var branch = plan.Branches.SingleOrDefault(x => x.OutputKey == key); Check(branch != null, "unknown-output-branch");
            return new SharedDriverBranchObservation(key, TransmissionGoalJson.ReadResult(Bytes(b.GetProperty("generation"))), A(b, "pool", goal.PoolCountLimit).Select(c => PoolCandidate(c, branch!.Plan)), I(b, "poolBytes"), b.GetProperty("resourceStopped").GetBoolean());
        }).ToArray();
        var candidates = A(p, "candidates", goal.MaximumReturned).Select(c => {
            var artifactBytes = Bytes(c.GetProperty("mechanism")); Check(artifactBytes.Length <= 512 * 1024, "shared-artifact-bound");
            var artifact = Mechanisms.Read(artifactBytes); var v = Mechanisms.VerifyIdentity(artifact); Check(v.ArtifactHashMatches && v.CandidateIdMatches, "shared-artifact-identity");
            var outputs = A(c, "outputs", 2).Select(o => {
                var child = branches.Single(b => b.OutputKey == S(o, "outputKey")).Pool.Single(x => x.CandidateId == S(o, "childCandidateId"));
                return new SharedDriverOutputBinding(S(o, "outputKey"), S(o, "childGoalId"), child, S(o, "axisId"), S(o, "dofId"), S(o, "bodyId"), R(o.GetProperty("requestedTransfer")), R(o.GetProperty("actualTransfer")),
                    A(o, "roleMappings", 128).Select(m => new SharedDriverRoleMapping(S(m, "kind"), S(m, "localId"), S(m, "mergedId"))));
            });
            return new SharedDriverTransmissionCandidate(S(c, "candidateId"), artifact.Candidate, outputs, TransmissionGoalJson.Metrics(c.GetProperty("metrics")));
        }).ToArray();
        var result = new SharedDriverTransmissionResult(plan, E<SharedDriverTransmissionStatus>(p, "status"), branches, candidates, I(p, "examinedPairs"), I(p, "approvedPairs"), I(p, "uniqueMerged"),
            A(p, "pairDetails", 64).Select(t => new SharedDriverPairObservation(S(t, "leftId"), S(t, "rightId"), t.GetProperty("candidateId").GetString(), A(t, "diagnostics", 16).Select(Issue))),
            S(p, "pairTranscriptHash"), S(p, "limitingReason"), S(p, "exactEmptyProof"), A(p, "diagnostics", 128).Select(Issue));
        Check(bytes.SequenceEqual(WriteResult(result)), "shared-result-noncanonical/derived-validation"); return result;
    }
}
