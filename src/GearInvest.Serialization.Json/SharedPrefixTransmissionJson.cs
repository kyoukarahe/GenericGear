using System;
using System.Collections.Generic;
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

public static partial class SharedPrefixTransmissionJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    private static void Ints(Utf8JsonWriter w, string name, IEnumerable<int> values) => Array(w, name, values, (a, n) => a.WriteNumberValue(n));
    private static int[] Ints(JsonElement p, string name, int max = 3) => A(p, name, max).Select(x => x.GetInt32()).ToArray();
    private static void Regions(Utf8JsonWriter w, string name, IEnumerable<GearRouteRegion> values) => Array(w, name, values, (a, r) => { a.WriteStartObject(); a.WriteString("id", r.Id); a.WritePropertyName("bounds"); Box(a, r.Bounds); a.WriteEndObject(); });
    private static GearRouteRegion[] Regions(JsonElement p, string name) => A(p, name, 16).Select(r => new GearRouteRegion(S(r, "id"), Box(r.GetProperty("bounds")))).ToArray();
    internal static void Output(Utf8JsonWriter w, SharedDriverTransmissionOutput o)
    {
        w.WriteStartObject(); w.WriteString("key", o.Key); Anchor(w, "anchor", o.Anchor); w.WritePropertyName("targetTransfer"); R(w, o.TargetTransfer);
        Ints(w, "outputLayers", o.OutputLayers); w.WriteString("layerPolicy", o.LayerPolicy.ToString()); Array(w, "allowedFamilies", o.AllowedFamilies, (a, f) => a.WriteStringValue(f.ToString()));
        w.WriteNumber("maximumCompounds", o.MaximumCompounds); w.WriteNumber("maximumIdlers", o.MaximumIdlers); Array(w, "slots", o.Slots, TransmissionGoalJson.Slot); Array(w, "legs", o.Legs, Leg); w.WriteEndObject();
    }
    internal static SharedDriverTransmissionOutput Output(JsonElement p)
    {
        var slots = A(p, "slots", 2).Select(TransmissionGoalJson.Slot).ToArray(); var legs = A(p, "legs", 3).Select(Leg).ToArray(); Check(slots.Length == 2 && legs.Length == 3, "resolved-output-domains");
        var families = A(p, "allowedFamilies", 3).Select(x => { string s = x.GetString()!; Check(Enum.TryParse<TransmissionFamily>(s, out var f) && Enum.IsDefined(typeof(TransmissionFamily), f) && f.ToString() == s, "family"); return f; });
        return new SharedDriverTransmissionOutput(S(p, "key"), Anchor(p.GetProperty("anchor")), R(p.GetProperty("targetTransfer")), legs[0], slots[0], Ints(p, "outputLayers"),
            E<TransmissionOutputLayerPolicy>(p, "layerPolicy"), families, I(p, "maximumCompounds"), I(p, "maximumIdlers"), slot1: slots[1], leg1: legs[1], leg2: legs[2]);
    }
    public static byte[] WriteGoal(SharedPrefixTransmissionGoal goal)
    {
        var n = SharedPrefixTransmissionCompiler.Normalize(goal); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code))); var g = n.Goal!;
        var bytes = Envelope(SharedPrefixTransmissionContract.GoalFormat, w => {
            w.WriteStartObject(); w.WriteString("goalId", n.GoalId); w.WriteString("profile", g.Profile); w.WriteString("lowering", g.Lowering); w.WriteString("allocation", g.Allocation);
            w.WriteString("join", g.Join); w.WriteString("ranking", g.Ranking); w.WriteString("resources", g.Resources); Anchor(w, "input", g.Input);
            Number(w, "pitchRadiusTicksPerTooth", g.PitchRadiusTicksPerTooth); w.WriteNumber("inputLayer", g.InputLayer); w.WriteString("driver", "single-actual-input"); w.WriteString("phaseConvention", "zero-turns"); w.WriteString("axisProfile", "fixed-parallel-xy");
            Array(w, "outputs", g.Outputs, Output); w.WritePropertyName("prefix"); Leg(w, g.Prefix); Ints(w, "splitterTeeth", g.SplitterTeeth);
            Array(w, "splitterSites", g.SplitterSites, (a, p) => { a.WriteStartObject(); Number(a, "x", p.X); Number(a, "y", p.Y); a.WriteEndObject(); });
            w.WriteNumber("splitterLayer", 0); OptionalBox(w, "requiredSplitterRegion", g.RequiredSplitterRegion); OptionalBox(w, "preferredSplitterRegion", g.PreferredSplitterRegion); Regions(w, "prefixKeepOuts", g.PrefixKeepOuts);
            w.WritePropertyName("bounds"); Box(w, g.Bounds); Ints(w, "availableLayers", g.AvailableLayers);
            Array(w, "keepOuts", g.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); a.WritePropertyName("bounds"); Box(a, k.Bounds); Ints(a, "layers", Enumerable.Range(0, 3).Where(k.AppliesTo)); a.WriteEndObject(); });
            Number(w, "unrelatedClearance", g.UnrelatedClearance); Number(w, "keepOutClearance", g.KeepOutClearance); w.WriteNumber("maximumTotalCompounds", g.MaximumTotalCompounds); w.WriteNumber("maximumTotalIdlers", g.MaximumTotalIdlers); w.WriteNumber("maximumBodies", g.MaximumBodies);
            OptionalBox(w, "preferredRegion", g.PreferredRegion); Regions(w, "requiredRegions", g.RequiredRegions);
            w.WriteNumber("totalWorkBudget", g.TotalWorkBudget); w.WriteNumber("reservedPrefixWork", g.ReservedPrefixWork); w.WriteNumber("reservedJoinWork", g.ReservedJoinWork); w.WriteNumber("maximumReturned", g.MaximumReturned);
            w.WriteNumber("hypothesisLimit", g.HypothesisLimit); w.WriteNumber("poolCountLimit", g.PoolCountLimit); w.WriteNumber("poolByteLimit", g.PoolByteLimit); w.WriteNumber("totalPoolByteLimit", g.TotalPoolByteLimit); w.WriteNumber("tripleDomainLimit", g.TripleDomainLimit); w.WriteEndObject();
        });
        Check(bytes.Length <= SharedPrefixTransmissionContract.MaxGoalBytes, "prefix-goal-byte-limit"); return bytes;
    }
    public static SharedPrefixTransmissionGoal ReadGoal(byte[] bytes)
    {
        using var doc = Open(bytes, SharedPrefixTransmissionContract.GoalFormat, SharedPrefixTransmissionContract.MaxGoalBytes); var p = doc.RootElement.GetProperty("payload");
        var keep = A(p, "keepOuts", 16).Select(k => { var layers = Ints(k, "layers"); Check(layers.Length > 0 && layers.All(l => l >= 0 && l <= 2), "keep-out-layer-set");
            return new ThreeLayerGearRouteKeepOut(S(k, "id"), Box(k.GetProperty("bounds")), (ThreeRouteLayers)layers.Aggregate(0, (mask, layer) => mask | (1 << layer))); });
        var g = new SharedPrefixTransmissionGoal(Anchor(p.GetProperty("input")), N(p.GetProperty("pitchRadiusTicksPerTooth")), A(p, "outputs", 2).Select(Output), Leg(p.GetProperty("prefix")),
            Ints(p, "splitterTeeth", 8), Box(p.GetProperty("bounds")), A(p, "splitterSites", 32).Select(s => new GearRoutePoint(N(s.GetProperty("x")), N(s.GetProperty("y")))),
            requiredSplitterRegion: OptionalBox(p, "requiredSplitterRegion"), preferredSplitterRegion: OptionalBox(p, "preferredSplitterRegion"), prefixKeepOuts: Regions(p, "prefixKeepOuts"),
            availableLayers: Ints(p, "availableLayers"), keepOuts: keep, unrelatedClearance: N(p.GetProperty("unrelatedClearance")), keepOutClearance: N(p.GetProperty("keepOutClearance")),
            maximumTotalCompounds: I(p, "maximumTotalCompounds"), maximumTotalIdlers: I(p, "maximumTotalIdlers"), maximumBodies: I(p, "maximumBodies"), preferredRegion: OptionalBox(p, "preferredRegion"), requiredRegions: Regions(p, "requiredRegions"),
            totalWorkBudget: I(p, "totalWorkBudget"), reservedPrefixWork: I(p, "reservedPrefixWork"), reservedJoinWork: I(p, "reservedJoinWork"), maximumReturned: I(p, "maximumReturned"), hypothesisLimit: I(p, "hypothesisLimit"),
            poolCountLimit: I(p, "poolCountLimit"), poolByteLimit: I(p, "poolByteLimit"), totalPoolByteLimit: I(p, "totalPoolByteLimit"), tripleDomainLimit: I(p, "tripleDomainLimit"), inputLayer: I(p, "inputLayer"),
            profile: S(p, "profile"), lowering: S(p, "lowering"), allocation: S(p, "allocation"), join: S(p, "join"), ranking: S(p, "ranking"), resources: S(p, "resources"));
        Check(bytes.SequenceEqual(WriteGoal(g)), "prefix-goal-noncanonical/identity/policy"); return SharedPrefixTransmissionCompiler.Normalize(g).Goal!;
    }
    private static byte[] ObjectBytes(Action<Utf8JsonWriter> write)
    { using var s = new MemoryStream(); using (var w = new Utf8JsonWriter(s)) { write(w); w.Flush(); } return s.ToArray(); }
    public static byte[] WritePlan(SharedPrefixTransmissionSearchPlan plan) => ObjectBytes(w => {
        w.WriteStartObject(); w.WriteString("goalId", plan.GoalId); w.WriteString("planId", plan.PlanId); w.WriteString("status", plan.Status.ToString()); w.WriteString("limitingReason", plan.LimitingReason);
        w.WriteNumber("potentialHypotheses", plan.PotentialHypotheses); w.WriteNumber("allocatedPrefixWork", plan.AllocatedPrefixWork); w.WriteNumber("allocatedSuffixWork", plan.AllocatedSuffixWork); w.WriteNumber("preparationCeiling", plan.PreparationCeiling);
        Array(w, "hypotheses", plan.Hypotheses, (a, h) => {
            a.WriteStartObject(); a.WriteString("hypothesisId", h.HypothesisId); Anchor(a, "splitter", h.Splitter); a.WritePropertyName("r"); R(a, h.Transfer); a.WriteString("exclusionProof", h.ExclusionProof);
            if (h.Prefix != null) { if (h.Prefix.Normalized.IsValid) Embed(a, "prefixGoal", TransmissionGoalJson.WriteGoal(h.Prefix.Normalized.Goal!)); Embed(a, "prefixPlan", TransmissionGoalJson.WritePlan(h.Prefix)); }
            if (h.Suffix != null) { if (h.Suffix.Normalized.IsValid) Embed(a, "suffixGoal", SharedDriverTransmissionJson.WriteGoal(h.Suffix.Normalized.Goal!)); Embed(a, "suffixPlan", SharedDriverTransmissionJson.WritePlan(h.Suffix)); }
            a.WriteEndObject();
        }); Array(w, "diagnostics", plan.Diagnostics, Issue); w.WriteEndObject();
    });
    public static ArtifactWriteResult WriteMechanism(SharedPrefixTransmissionCandidate c) => Mechanisms.Write(c.Mechanism);
}
