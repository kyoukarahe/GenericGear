using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using static GearInvest.Serialization.GearRoutingJson;
using static GearInvest.Serialization.CompoundRoutingJson;

namespace GearInvest.Serialization;

/// <summary>Additive goal sidecars. The original per-family mechanism writer owns artifact bytes.</summary>
public static class TransmissionGoalJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    private static void Counts(Utf8JsonWriter w, string name, IEnumerable<int> values) => Array(w, name, values, (a, x) => a.WriteNumberValue(x));
    private static int[] Counts(JsonElement e, string name, int maximum) => A(e, name, maximum).Select(x => x.GetInt32()).ToArray();
    internal static void Slot(Utf8JsonWriter w, CompoundRoutingSlot s)
    { w.WriteStartObject(); Counts(w, "receivingTeeth", s.ReceivingTeeth); Counts(w, "drivingTeeth", s.DrivingTeeth); Array(w, "sites", s.Sites, Point); OptionalBox(w, "requiredRegion", s.RequiredRegion); OptionalBox(w, "preferredRegion", s.PreferredRegion); w.WriteEndObject(); }
    internal static CompoundRoutingSlot Slot(JsonElement s) => new CompoundRoutingSlot(Counts(s, "receivingTeeth", 16), Counts(s, "drivingTeeth", 16), A(s, "sites", 32).Select(Point), requiredRegion: OptionalBox(s, "requiredRegion"), preferredRegion: OptionalBox(s, "preferredRegion"));

    public static byte[] WriteGoal(TransmissionGoal raw)
    {
        var n = TransmissionGoalCompiler.Normalize(raw); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code)));
        var g = n.Goal!; var bytes = Envelope(TransmissionGoalContract.GoalFormat, w => {
            w.WriteStartObject(); w.WriteString("goalId", n.GoalId); w.WriteString("profile", g.Profile); w.WriteString("lowering", g.Lowering);
            w.WriteString("allocation", g.Allocation); w.WriteString("ranking", g.Ranking); w.WriteString("resources", g.Resources); w.WriteString("unit", g.Unit);
            Anchor(w, "input", g.Input); Anchor(w, "output", g.Output); w.WritePropertyName("targetTransfer"); R(w, g.TargetTransfer); Number(w, "pitchRadiusTicksPerTooth", g.PitchRadiusTicksPerTooth);
            w.WriteNumber("inputLayer", g.InputLayer); w.WriteNumber("maximumCompounds", g.MaximumCompounds); w.WriteNumber("maximumTotalIdlers", g.MaximumTotalIdlers);
            w.WriteString("layerPolicy", g.LayerPolicy.ToString()); w.WriteNumber("maximumLayerCount", g.MaximumLayerCount); Counts(w, "availableLayers", g.AvailableLayers); Counts(w, "outputLayers", g.OutputLayers);
            Array(w, "allowedFamilies", g.AllowedFamilies, (a, f) => a.WriteStringValue(f.ToString())); Array(w, "slots", g.Slots, Slot); Array(w, "legs", g.Legs, Leg);
            Array(w, "keepOuts", g.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); a.WritePropertyName("bounds"); Box(a, k.Bounds); Counts(a, "layers", Enumerable.Range(0, 3).Where(k.AppliesTo)); a.WriteEndObject(); });
            w.WriteNumber("workBudget", g.WorkBudget); w.WriteNumber("maximumReturned", g.MaximumReturned); w.WriteEndObject();
        });
        Check(bytes.Length <= TransmissionGoalContract.MaxGoalBytes, "goal-byte-limit"); return bytes;
    }
    public static TransmissionGoal ReadGoal(byte[] bytes)
    {
        using var d = Open(bytes, TransmissionGoalContract.GoalFormat, TransmissionGoalContract.MaxGoalBytes); var p = d.RootElement.GetProperty("payload");
        var slots = A(p, "slots", 2).Select(Slot).ToArray(); var legs = A(p, "legs", 3).Select(Leg).ToArray(); Check(slots.Length == 2 && legs.Length == 3, "resolved-domain-count");
        var families = A(p, "allowedFamilies", 3).Select(x => { var s = x.GetString(); Check(Enum.TryParse<TransmissionFamily>(s, out var f) && Enum.IsDefined(typeof(TransmissionFamily), f) && f.ToString() == s, "unknown-family"); return f; });
        var keepOuts = A(p, "keepOuts", 16).Select(k => {
            var layers = Counts(k, "layers", 3); Check(layers.Length > 0 && layers.All(l => l >= 0 && l <= 2), "keepout-layer-set");
            return new ThreeLayerGearRouteKeepOut(S(k, "id"), Box(k.GetProperty("bounds")), (ThreeRouteLayers)layers.Aggregate(0, (mask, l) => mask | (1 << l)));
        });
        var g = new TransmissionGoal(Anchor(p.GetProperty("input")), Anchor(p.GetProperty("output")), R(p.GetProperty("targetTransfer")), N(p.GetProperty("pitchRadiusTicksPerTooth")),
            legs[0], slots[0], I(p, "maximumCompounds"), I(p, "maximumTotalIdlers"), Counts(p, "availableLayers", 3), Counts(p, "outputLayers", 3),
            E<TransmissionOutputLayerPolicy>(p, "layerPolicy"), I(p, "maximumLayerCount"), families, keepOuts, slot1: slots[1], leg1: legs[1], leg2: legs[2],
            workBudget: I(p, "workBudget"), maximumReturned: I(p, "maximumReturned"), inputLayer: I(p, "inputLayer"), profile: S(p, "profile"), lowering: S(p, "lowering"),
            allocation: S(p, "allocation"), ranking: S(p, "ranking"), resources: S(p, "resources"), unit: S(p, "unit"));
        Check(bytes.SequenceEqual(WriteGoal(g)), "goal-noncanonical/identity/policy"); return TransmissionGoalCompiler.Normalize(g).Goal!;
    }

    public static byte[] WritePlan(CompiledTransmissionSearchPlan plan)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { Plan(writer, plan); writer.Flush(); }
        return stream.ToArray();
    }
    private static void Plan(Utf8JsonWriter w, CompiledTransmissionSearchPlan p)
    {
        w.WriteStartObject(); w.WriteString("goalId", p.GoalId); w.WriteString("planId", p.PlanId); w.WriteString("status", p.Status.ToString());
        w.WriteNumber("allocatedWork", p.AllocatedWork); w.WriteNumber("proofWork", p.ProofWork); w.WriteNumber("preparationCeiling", p.PreparationCeiling);
        Array(w, "families", p.Families, (a, f) => {
            a.WriteStartObject(); a.WriteString("family", f.Family.ToString()); a.WriteString("familyId", f.FamilyId); a.WriteString("backendFingerprint", f.BackendFingerprint);
            a.WriteString("applicability", f.Applicability.ToString()); a.WriteString("reason", f.Reason); a.WriteString("childRequestId", f.RequestId);
            a.WriteNumber("quota", f.Quota); a.WriteNumber("proofWork", f.ProofWork); a.WriteNumber("preparationCeiling", f.PreparationCeiling);
            Counts(a, "canonicalLayers", Enumerable.Range(0, f.OutputLayer + 1)); Array(a, "fieldMappings", f.FieldMappings, (b, m) => b.WriteStringValue(m));
            if (f.Simple != null) Embed(a, "request", GearRoutingJson.WriteRequest(f.Simple)); else if (f.One != null) Embed(a, "request", CompoundRoutingJson.WriteRequest(f.One));
            else if (f.Two != null) Embed(a, "request", TwoCompoundRoutingJson.WriteRequest(f.Two)); else a.WriteNull("request"); a.WriteEndObject();
        }); Array(w, "diagnostics", p.Diagnostics, Issue); w.WriteEndObject();
    }
    public static ArtifactWriteResult WriteMechanism(TransmissionCandidateOrigin o) => o.Simple != null ? GearRoutingJson.WriteMechanism(o.Simple) :
        o.One != null ? CompoundRoutingJson.WriteMechanism(o.One) : TwoCompoundRoutingJson.WriteMechanism(o.Two!);

    private static IEnumerable<IEnumerable<GearRouteAssignment>> Paths(TransmissionCandidateOrigin o) => o.Simple != null ? new[] { o.Simple.Path } :
        o.One != null ? new[] { o.One.InputPath, o.One.OutputPath } : o.Two!.Paths;
    internal static void Origin(Utf8JsonWriter w, TransmissionCandidateOrigin o)
    {
        w.WriteStartObject(); w.WriteString("family", o.Family.ToString()); w.WriteString("originId", o.OriginId); w.WriteString("childRequestId", o.RequestId); w.WriteString("childContextId", o.ContextId);
        Array(w, "paths", Paths(o), (a, path) => { a.WriteStartArray(); foreach (var p in path) Assignment(a, p); a.WriteEndArray(); });
        Embed(w, "mechanism", WriteMechanism(o).Bytes); w.WriteEndObject();
    }
    internal static TransmissionCandidateOrigin Origin(JsonElement p, CompiledTransmissionSearchPlan plan, string id)
    {
        var family = E<TransmissionFamily>(p, "family"); var child = plan.Families.Single(f => f.Family == family);
        var paths = A(p, "paths", 3).Select(a => { Check(a.ValueKind == JsonValueKind.Array && a.GetArrayLength() >= 2 && a.GetArrayLength() <= 7, "origin-path-bound"); return a.EnumerateArray().Select(Assignment).ToArray(); }).ToArray();
        Check(paths.Length == (int)family + 1, "origin-leg-count");
        var artifactBytes = Bytes(p.GetProperty("mechanism")); Check(artifactBytes.Length <= 512 * 1024, "origin-artifact-bound");
        var a = p.GetProperty("mechanism"); var k = a.GetProperty("kinematic"); A(k, "dofs", 19); A(k, "couplings", 18); A(k, "solution", 19);
        var spatial = a.GetProperty("spatial"); A(spatial, "axes", 19); A(spatial, "bodies", 21); A(spatial, "contacts", 18);
        var stored = Mechanisms.Read(artifactBytes); var verified = Mechanisms.VerifyIdentity(stored); Check(verified.CandidateIdMatches && verified.ArtifactHashMatches, "origin-artifact-identity");
        var profile = child.Simple?.Profile ?? child.One?.Profile ?? child.Two?.Profile; Check(profile != null, "origin-has-no-child-request");
        var fresh = new GenerationEngine().Generate(new LowLevelMechanicalSpecification(profile!, stored.Candidate.Kinematic, stored.Candidate.Spatial));
        Check(fresh.IsSuccess, "origin-whole-graph-invalid"); var mechanism = fresh.Candidates.Single(); TransmissionCandidateOrigin origin;
        if (family == TransmissionFamily.SimpleIdler)
        {
            var r = child.Simple!; origin = new TransmissionCandidateOrigin(new GearRouteCandidate(id, paths[0], mechanism, GearRouteValidator.Metrics(r, paths[0]), GearRouteValidator.Validate(r, paths[0], mechanism, id)));
        }
        else if (family == TransmissionFamily.OneCompound)
        {
            var r = child.One!; var pair = new AnchoredCompoundToothPair(r.Input.Teeth, r.Output.Teeth, paths[0].Last().Teeth, paths[1][0].Teeth);
            origin = new TransmissionCandidateOrigin(new CompoundGearRouteCandidate(id, pair, paths[0], paths[1], mechanism, CompoundGearRouteValidator.Metrics(r, paths[0], paths[1]), CompoundGearRouteValidator.Validate(r, pair, paths[0], paths[1], mechanism, id)));
        }
        else
        {
            var r = child.Two!; var assignment = new TwoCompoundToothAssignment(r.Input.Teeth, r.Output.Teeth, paths[0].Last().Teeth, paths[1][0].Teeth, paths[1].Last().Teeth, paths[2][0].Teeth);
            origin = new TransmissionCandidateOrigin(new TwoCompoundGearRouteCandidate(id, assignment, paths, mechanism, TwoCompoundGearRouteValidator.Metrics(r, paths), TwoCompoundGearRouteValidator.Validate(r, assignment, paths, mechanism, id)));
        }
        Check(artifactBytes.SequenceEqual(WriteMechanism(origin).Bytes), "origin-derived-artifact/metadata-mismatch"); return origin;
    }
    internal static void Metrics(Utf8JsonWriter w, TransmissionCommonMetrics m)
    { w.WriteStartObject(); w.WriteNumber("compounds", m.Compounds); w.WriteNumber("idlers", m.Idlers); Number(w, "preferredPenalty", m.PreferredPenalty); Number(w, "footprint", m.Footprint); Number(w, "totalTeeth", m.TotalTeeth); Counts(w, "layers", m.Layers); w.WriteEndObject(); }
    internal static TransmissionCommonMetrics Metrics(JsonElement p) => new TransmissionCommonMetrics(I(p, "compounds"), I(p, "idlers"), N(p.GetProperty("preferredPenalty")), N(p.GetProperty("footprint")), N(p.GetProperty("totalTeeth")), Counts(p, "layers", 3));

    public static void ValidateCached(TransmissionGenerationResult g)
    {
        var validation = TransmissionResultValidator.Validate(g, Mechanisms.ComputeCandidateId); Check(validation.IsValid, string.Join(";", validation.Diagnostics.Select(d => d.Code)));
        foreach (var o in g.Outcomes) { if (o.Simple != null) GearRoutingJson.ValidateCached(o.Simple); if (o.One != null) CompoundRoutingJson.ValidateCached(o.One); if (o.Two != null) TwoCompoundRoutingJson.ValidateCached(o.Two); }
    }
    public static byte[] WriteResult(TransmissionGenerationResult g)
    {
        ValidateCached(g); var bytes = Envelope(TransmissionGoalContract.ResultFormat, w => {
            w.WriteStartObject(); Embed(w, "goal", WriteGoal(g.Plan.Normalized.Goal!)); w.WritePropertyName("plan"); Plan(w, g.Plan);
            w.WriteString("status", g.Status.ToString()); w.WriteBoolean("hasValidatedCandidates", g.HasValidatedCandidates); w.WriteBoolean("searchComplete", g.SearchComplete);
            w.WriteBoolean("resultTruncated", g.ResultTruncated); w.WriteString("rankingGuarantee", g.RankingGuarantee);
            w.WriteNumber("observedUnique", g.ObservedUnique); w.WriteBoolean("observationsExact", g.ObservationsExact);
            if (g.TotalUnique.HasValue) w.WriteNumber("totalUnique", g.TotalUnique.Value); else w.WriteNull("totalUnique");
            w.WriteNumber("allocatedWork", g.AllocatedWork); w.WriteNumber("consumedWork", g.ConsumedWork); w.WriteNumber("unusedAllocatedWork", g.UnusedAllocatedWork); w.WriteNumber("unallocatedWork", g.UnallocatedWork);
            Array(w, "outcomes", g.Outcomes, (a, o) => {
                a.WriteStartObject(); a.WriteString("family", o.Family.ToString()); a.WriteString("status", o.Status.ToString()); a.WriteNumber("allocated", o.Allocated); a.WriteNumber("consumed", o.Consumed); a.WriteNumber("unused", o.Unused);
                a.WriteBoolean("preparationObservationAvailable", o.Status != TransmissionFamilyStatus.Failed); a.WriteNumber("nodeChecks", o.NodeChecks); a.WriteNumber("adjacencyChecks", o.AdjacencyChecks); a.WriteNumber("validatedObservations", o.ValidatedObservations); a.WriteString("reason", o.Reason);
                if (o.Simple != null) Embed(a, "childResult", GearRoutingJson.WriteResult(o.Simple)); else if (o.One != null) Embed(a, "childResult", CompoundRoutingJson.WriteResult(o.One));
                else if (o.Two != null) Embed(a, "childResult", TwoCompoundRoutingJson.WriteResult(o.Two)); else a.WriteNull("childResult"); a.WriteEndObject();
            });
            Array(w, "candidates", g.Candidates, (a, c) => {
                a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); a.WritePropertyName("metrics"); Metrics(a, c.Metrics); Array(a, "origins", c.Origins, Origin);
                var v = TransmissionGoalValidator.Validate(g.Plan.Normalized.Goal!, c, Mechanisms.ComputeCandidateId);
                a.WriteStartObject("validation"); a.WriteBoolean("isValid", v.IsValid); a.WriteString("goalId", v.GoalId); a.WriteString("candidateId", v.CandidateId);
                a.WritePropertyName("actualTransfer"); R(a, v.ActualTransfer!.Value); Array(a, "contextIds", v.ContextIds, (b, s) => b.WriteStringValue(s)); a.WriteString("scope", v.Scope); a.WriteEndObject(); a.WriteEndObject();
            }); Array(w, "diagnostics", g.Diagnostics, Issue); w.WriteEndObject();
        });
        Check(bytes.Length <= TransmissionGoalContract.MaxResultBytes, "goal-result-byte-limit"); return bytes;
    }
    public static TransmissionGenerationResult ReadResult(byte[] bytes)
    {
        using var d = Open(bytes, TransmissionGoalContract.ResultFormat, TransmissionGoalContract.MaxResultBytes); var p = d.RootElement.GetProperty("payload");
        var goal = ReadGoal(Bytes(p.GetProperty("goal"))); var plan = TransmissionGoalCompiler.Compile(goal);
        Check(Bytes(p.GetProperty("plan")).SequenceEqual(WritePlan(plan)), "goal-stored-plan-versus-fresh-lowering/policy/proof");
        var outcomes = A(p, "outcomes", 3).Select(o => {
            var family = E<TransmissionFamily>(o, "family"); var child = o.GetProperty("childResult"); GearRoutingResult? simple = null; CompoundGearRoutingResult? one = null; TwoCompoundGearRoutingResult? two = null;
            if (child.ValueKind != JsonValueKind.Null) { var b = Bytes(child); if (family == TransmissionFamily.SimpleIdler) simple = GearRoutingJson.ReadResult(b); else if (family == TransmissionFamily.OneCompound) one = CompoundRoutingJson.ReadResult(b); else two = TwoCompoundRoutingJson.ReadResult(b); }
            return new TransmissionFamilyOutcome(family, E<TransmissionFamilyStatus>(o, "status"), I(o, "allocated"), I(o, "consumed"), I(o, "nodeChecks"), I(o, "adjacencyChecks"), I(o, "validatedObservations"), S(o, "reason"), simple, one, two);
        }).ToArray();
        var candidates = A(p, "candidates", 128).Select(c => new TransmissionMechanismCandidate(S(c, "candidateId"), A(c, "origins", 3).Select(o => Origin(o, plan, S(c, "candidateId"))), Metrics(c.GetProperty("metrics")))).ToArray();
        var result = new TransmissionGenerationResult(plan, E<TransmissionGoalStatus>(p, "status"), outcomes, candidates, I(p, "observedUnique"), p.GetProperty("observationsExact").GetBoolean(), A(p, "diagnostics", 128).Select(Issue));
        Check(bytes.SequenceEqual(WriteResult(result)), "goal-result-noncanonical/derived-observation/validation"); return result;
    }
}
