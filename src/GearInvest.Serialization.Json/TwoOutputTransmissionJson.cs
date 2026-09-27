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

public static partial class TwoOutputTransmissionJson
{
    private static void Ints(Utf8JsonWriter w, string name, IEnumerable<int> values) => Array(w, name, values, (a, n) => a.WriteNumberValue(n));
    private static int[] Ints(JsonElement p, string name, int limit = 3) => A(p, name, limit).Select(x => x.GetInt32()).ToArray();
    private static void Regions(Utf8JsonWriter w, string name, IEnumerable<GearRouteRegion> values) => Array(w, name, values, (a, r) => { a.WriteStartObject(); a.WriteString("id", r.Id); a.WritePropertyName("bounds"); Box(a, r.Bounds); a.WriteEndObject(); });
    private static GearRouteRegion[] Regions(JsonElement p, string name) => A(p, name, 16).Select(r => new GearRouteRegion(S(r, "id"), Box(r.GetProperty("bounds")))).ToArray();
    private static void Grid(Utf8JsonWriter w, string name, GearRouteGrid? grid)
    {
        if (grid == null) { w.WriteNull(name); return; }
        w.WriteStartObject(name); w.WritePropertyName("origin"); Point(w, grid.Origin); w.WritePropertyName("step"); Point(w, grid.Step); w.WritePropertyName("bounds"); Box(w, grid.Bounds); w.WriteEndObject();
    }
    private static GearRouteGrid? Grid(JsonElement p) => p.ValueKind == JsonValueKind.Null ? null : new GearRouteGrid(Point(p.GetProperty("origin")), Point(p.GetProperty("step")), Box(p.GetProperty("bounds")));
    public static byte[] WriteGoal(TwoOutputTransmissionGoal raw)
    {
        var n = TwoOutputTransmissionCompiler.Normalize(raw); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code))); var g = n.Goal!;
        var bytes = Envelope(TwoOutputTransmissionContract.GoalFormat, w => {
            w.WriteStartObject(); w.WriteString("goalId", n.GoalId); w.WriteString("profile", g.Profile); w.WriteString("lowering", g.Lowering);
            w.WriteString("allocation", g.Allocation); w.WriteString("ranking", g.Ranking); w.WriteString("resources", g.Resources); w.WriteString("sharingPolicy", g.SharingPolicy.ToString());
            Anchor(w, "input", g.Input); Number(w, "pitchRadiusTicksPerTooth", g.PitchRadiusTicksPerTooth); w.WriteNumber("inputLayer", g.InputLayer);
            w.WriteString("driver", "single-actual-input"); w.WriteString("phaseConvention", "zero-turns");
            Array(w, "outputs", g.Outputs, SharedPrefixTransmissionJson.Output); w.WritePropertyName("bounds"); Box(w, g.Bounds); Ints(w, "availableLayers", g.AvailableLayers);
            Array(w, "keepOuts", g.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); a.WritePropertyName("bounds"); Box(a, k.Bounds); Ints(a, "layers", Enumerable.Range(0, 3).Where(k.AppliesTo)); a.WriteEndObject(); });
            Number(w, "unrelatedClearance", g.UnrelatedClearance); Number(w, "keepOutClearance", g.KeepOutClearance); Regions(w, "requiredRegions", g.RequiredRegions); OptionalBox(w, "preferredRegion", g.PreferredRegion);
            w.WriteNumber("maximumTotalCompounds", g.MaximumTotalCompounds); w.WriteNumber("maximumBodies", g.MaximumBodies); w.WriteNumber("maximumTransmissionGears", g.MaximumTransmissionGears);
            w.WriteNumber("totalWorkBudget", g.TotalWorkBudget); w.WriteNumber("maximumReturned", g.MaximumReturned); w.WriteNumber("observationCountLimit", g.ObservationCountLimit); w.WriteNumber("observationByteLimit", g.ObservationByteLimit);
            if (g.SharedPrefix == null) w.WriteNull("sharedPrefix");
            else
            {
                var p = g.SharedPrefix; w.WriteStartObject("sharedPrefix"); w.WritePropertyName("routing"); Leg(w, p.Routing);
                w.WriteBoolean("routingHasExplicitSites", p.Routing.HasExplicitSites); Grid(w, "routingGrid", p.Routing.HasExplicitSites ? null : p.Routing.Grid);
                Ints(w, "splitterTeeth", p.SplitterTeeth); Array(w, "splitterSites", p.SplitterSites, Point); w.WriteBoolean("splitterHasExplicitSites", p.HasExplicitSites); Grid(w, "splitterGrid", p.HasExplicitSites ? null : p.SplitterGrid);
                OptionalBox(w, "requiredSplitterRegion", p.RequiredSplitterRegion); OptionalBox(w, "preferredSplitterRegion", p.PreferredSplitterRegion); Regions(w, "keepOuts", p.KeepOuts);
                w.WriteNumber("hypothesisLimit", p.HypothesisLimit); w.WriteString("localPreferenceScope", "source-profile-only; common global and output-domain preference are scored separately"); w.WriteEndObject();
            }
            w.WriteEndObject();
        });
        Check(bytes.Length <= TwoOutputTransmissionContract.MaxGoalBytes, "two-output-goal-byte-limit"); return bytes;
    }
    public static TwoOutputTransmissionGoal ReadGoal(byte[] bytes)
    {
        using var doc = Open(bytes, TwoOutputTransmissionContract.GoalFormat, TwoOutputTransmissionContract.MaxGoalBytes); var p = doc.RootElement.GetProperty("payload");
        TwoOutputPrefixDomain? prefix = null;
        if (p.GetProperty("sharedPrefix").ValueKind != JsonValueKind.Null)
        {
            var s = p.GetProperty("sharedPrefix"); var l = Leg(s.GetProperty("routing"));
            if (!s.GetProperty("routingHasExplicitSites").GetBoolean()) l = new CompoundRoutingLeg(l.Bounds, grid: Grid(s.GetProperty("routingGrid")), idlerTeeth: l.IdlerTeeth,
                minIdlers: l.MinIdlers, maxIdlers: l.MaxIdlers, unrelatedClearance: l.UnrelatedClearance, keepOutClearance: l.KeepOutClearance, requiredRegions: l.RequiredRegions, preferredRegion: l.PreferredRegion);
            prefix = new TwoOutputPrefixDomain(l, Ints(s, "splitterTeeth", 16), s.GetProperty("splitterHasExplicitSites").GetBoolean() ? A(s, "splitterSites", 64).Select(Point) : null,
                Grid(s.GetProperty("splitterGrid")), OptionalBox(s, "requiredSplitterRegion"), OptionalBox(s, "preferredSplitterRegion"), Regions(s, "keepOuts"), I(s, "hypothesisLimit"));
        }
        var keep = A(p, "keepOuts", 16).Select(k => {
            var layers = Ints(k, "layers"); Check(layers.Length != 0 && layers.All(x => x >= 0 && x <= 2), "keepout-layer-set");
            return new ThreeLayerGearRouteKeepOut(S(k, "id"), Box(k.GetProperty("bounds")), (ThreeRouteLayers)layers.Aggregate(0, (mask, layer) => mask | (1 << layer)));
        });
        var g = new TwoOutputTransmissionGoal(Anchor(p.GetProperty("input")), N(p.GetProperty("pitchRadiusTicksPerTooth")), A(p, "outputs", 2).Select(SharedPrefixTransmissionJson.Output),
            Box(p.GetProperty("bounds")), E<TwoOutputSharingPolicy>(p, "sharingPolicy"), prefix, Ints(p, "availableLayers"), keep,
            N(p.GetProperty("unrelatedClearance")), N(p.GetProperty("keepOutClearance")), I(p, "maximumTotalCompounds"), I(p, "maximumBodies"), I(p, "maximumTransmissionGears"),
            OptionalBox(p, "preferredRegion"), Regions(p, "requiredRegions"), I(p, "totalWorkBudget"), I(p, "maximumReturned"), I(p, "inputLayer"), I(p, "observationCountLimit"), I(p, "observationByteLimit"),
            S(p, "profile"), S(p, "lowering"), S(p, "allocation"), S(p, "ranking"), S(p, "resources"));
        Check(bytes.SequenceEqual(WriteGoal(g)), "two-output-goal-noncanonical/identity/active-policy"); return TwoOutputTransmissionCompiler.Normalize(g).Goal!;
    }

    public static byte[] WritePlan(TwoOutputSharingSearchPlan plan)
    {
        Check(plan.Normalized.IsValid, "normalized-common-goal-required");
        return Envelope(TwoOutputTransmissionContract.PlanFormat, w => {
            w.WriteStartObject(); Embed(w, "goal", WriteGoal(plan.Normalized.Goal!)); w.WriteString("goalId", plan.GoalId); w.WriteString("planId", plan.PlanId);
            w.WriteNumber("allocatedWork", plan.AllocatedWork); w.WriteNumber("unallocatedWork", plan.Normalized.Goal!.TotalWorkBudget - plan.AllocatedWork);
            Array(w, "strategies", plan.Strategies, (a, s) => {
                a.WriteStartObject(); a.WriteString("strategy", s.Strategy.ToString()); a.WriteString("strategyId", s.StrategyId); a.WriteBoolean("included", s.Included); a.WriteString("applicability", s.Applicability.ToString());
                a.WriteString("reason", s.Reason); a.WriteNumber("quota", s.Quota); a.WriteString("childGoalId", s.ChildGoalId); a.WriteString("childPlanId", s.ChildPlanId);
                a.WriteString("backendFingerprint", s.BackendFingerprint); a.WriteString("fieldMapping", s.FieldMapping); a.WriteString("canonicalChildContext", s.RootOnly?.Normalized.Goal?.Canonical ?? s.SharedPrefix?.Normalized.Goal?.Canonical ?? "");
                if (s.RootOnly?.Normalized.IsValid == true) Embed(a, "childGoal", SharedDriverTransmissionJson.WriteGoal(s.RootOnly.Normalized.Goal!));
                else if (s.SharedPrefix?.Normalized.IsValid == true) Embed(a, "childGoal", SharedPrefixTransmissionJson.WriteGoal(s.SharedPrefix.Normalized.Goal!));
                else a.WriteNull("childGoal");
                if (s.RootOnly?.IsSupported == true) Embed(a, "childPlan", SharedDriverTransmissionJson.WritePlan(s.RootOnly));
                else if (s.SharedPrefix?.IsSupported == true) Embed(a, "childPlan", SharedPrefixTransmissionJson.WritePlan(s.SharedPrefix));
                else a.WriteNull("childPlan");
                Array(a, "coverageDiagnostics", s.RootOnly?.Diagnostics ?? s.SharedPrefix?.Diagnostics ?? new List<Diagnostic>().AsReadOnly(), Issue);
                a.WriteEndObject();
            }); Array(w, "diagnostics", plan.Diagnostics, Issue); w.WriteEndObject();
        });
    }
    public static TwoOutputSharingSearchPlan ReadPlan(byte[] bytes)
    {
        using var doc = Open(bytes, TwoOutputTransmissionContract.PlanFormat, TwoOutputTransmissionContract.MaxResultBytes);
        var goal = ReadGoal(Bytes(doc.RootElement.GetProperty("payload").GetProperty("goal"))); var plan = TwoOutputTransmissionCompiler.Compile(goal);
        Check(bytes.SequenceEqual(WritePlan(plan)), "two-output-fresh-lowering-allocation-plan"); return plan;
    }
    private static void Metrics(Utf8JsonWriter w, TwoOutputCommonMetrics m)
    {
        w.WriteStartObject(); w.WriteNumber("compounds", m.Compounds); w.WriteNumber("bodyCount", m.BodyCount); w.WriteNumber("nonEndpointBodies", m.NonEndpointBodies);
        w.WriteNumber("transmissionGears", m.TransmissionGears); Number(w, "footprint", m.Footprint); Number(w, "preferredPenalty", m.PreferredPenalty); Number(w, "totalTeeth", m.TotalTeeth); Ints(w, "layers", m.Layers); w.WriteEndObject();
    }
    private static TwoOutputCommonMetrics Metrics(JsonElement p) => new TwoOutputCommonMetrics(I(p, "compounds"), I(p, "bodyCount"), I(p, "nonEndpointBodies"), I(p, "transmissionGears"),
        N(p.GetProperty("footprint")), N(p.GetProperty("preferredPenalty")), N(p.GetProperty("totalTeeth")), Ints(p, "layers"));
}
