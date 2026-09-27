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

/// <summary>Three-leg sidecars, separate from mechanism 0.1. Cached context is not historical search certification.</summary>
public static class TwoCompoundRoutingJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    public static byte[] WriteRequest(AnchoredTwoCompoundGearRoutingRequest raw)
    {
        var n = AnchoredTwoCompoundGearRouter.Normalize(raw); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code)));
        var bytes = Envelope(TwoCompoundRoutingContract.RequestFormat, w => {
            var r = n.Request!; w.WriteStartObject(); w.WriteString("requestId", n.RequestId); w.WriteString("profile", r.Profile); w.WriteString("backend", r.Backend);
            w.WriteString("normalization", r.Normalization); w.WriteString("ranking", r.Ranking); w.WriteString("unit", r.Unit);
            w.WriteNumber("inputLayer", r.InputLayer); w.WriteNumber("outputLayer", r.OutputLayer); Anchor(w, "input", r.Input); Anchor(w, "output", r.Output);
            w.WritePropertyName("targetTransfer"); R(w, r.TargetTransfer); Number(w, "pitchRadiusTicksPerTooth", r.PitchRadiusTicksPerTooth);
            Array(w, "compounds", r.Compounds, (a, s) => { a.WriteStartObject(); Array(a,"receivingTeeth",s.ReceivingTeeth,(b,t) => b.WriteNumberValue(t));
                Array(a,"drivingTeeth",s.DrivingTeeth,(b,t) => b.WriteNumberValue(t)); Array(a,"sites",s.Sites,Point);
                OptionalBox(a,"requiredRegion",s.RequiredRegion); OptionalBox(a,"preferredRegion",s.PreferredRegion); a.WriteEndObject(); });
            Array(w,"legs",r.Legs,Leg); w.WriteNumber("maximumTotalIdlers",r.MaximumTotalIdlers);
            Array(w,"keepOuts",r.KeepOuts,(a,k) => { a.WriteStartObject(); a.WriteString("id",k.Id); a.WritePropertyName("bounds"); Box(a,k.Bounds);
                Array(a,"layers",Enumerable.Range(0,3).Where(k.AppliesTo),(b,l) => b.WriteNumberValue(l)); a.WriteEndObject(); });
            w.WriteNumber("workBudget",r.WorkBudget); w.WriteNumber("maximumReturned",r.MaximumReturned); w.WriteEndObject();
        });
        Check(bytes.Length <= TwoCompoundRoutingContract.MaxRequestBytes,"two-compound-request-byte-limit"); return bytes;
    }
    public static AnchoredTwoCompoundGearRoutingRequest ReadRequest(byte[] bytes)
    {
        using var d = Open(bytes,TwoCompoundRoutingContract.RequestFormat,TwoCompoundRoutingContract.MaxRequestBytes); var p = d.RootElement.GetProperty("payload");
        ThreeRouteLayers Layers(JsonElement k)
        {
            var layers = A(k,"layers",3).Select(x => x.GetInt32()).ToArray(); Check(layers.Length > 0 && layers.All(l => l >= 0 && l <= 2),"two-compound-layer-subset");
            return (ThreeRouteLayers)layers.Aggregate(0,(mask,l) => mask | (1 << l));
        }
        var r = new AnchoredTwoCompoundGearRoutingRequest(Anchor(p.GetProperty("input")),Anchor(p.GetProperty("output")),R(p.GetProperty("targetTransfer")),N(p.GetProperty("pitchRadiusTicksPerTooth")),
            A(p,"compounds",2).Select(s => new CompoundRoutingSlot(A(s,"receivingTeeth",8).Select(x => x.GetInt32()),A(s,"drivingTeeth",8).Select(x => x.GetInt32()),
                A(s,"sites",16).Select(Point),requiredRegion:OptionalBox(s,"requiredRegion"),preferredRegion:OptionalBox(s,"preferredRegion"))), A(p,"legs",3).Select(Leg),
            I(p,"maximumTotalIdlers"),A(p,"keepOuts",GearRoutingContract.MaxKeepOuts).Select(k => new ThreeLayerGearRouteKeepOut(S(k,"id"),Box(k.GetProperty("bounds")),Layers(k))),
            I(p,"workBudget"),I(p,"maximumReturned"),I(p,"inputLayer"),I(p,"outputLayer"),S(p,"unit"),S(p,"profile"),S(p,"backend"),S(p,"normalization"),S(p,"ranking"));
        Check(bytes.SequenceEqual(WriteRequest(r)),"two-compound-request-noncanonical/identity"); return AnchoredTwoCompoundGearRouter.Normalize(r).Request!;
    }
    private static ArtifactMetadata Metadata() => new ArtifactMetadata(new ArtifactSourceProvenance(TwoCompoundRoutingContract.Profile,"0",TwoCompoundRoutingContract.Profile),
        new GeneratorFingerprint("0.1.0-dev","two-compound-routing-v1",TwoCompoundRoutingContract.Backend,TwoCompoundRoutingContract.Profile));
    public static ArtifactWriteResult WriteMechanism(TwoCompoundGearRouteCandidate c)
    { Check(Mechanisms.ComputeCandidateId(c.Mechanism) == c.CandidateId,"two-compound-mechanical-identity"); return Mechanisms.Write(c.Mechanism,Metadata()); }

    private static void Counts(Utf8JsonWriter w,string key,IEnumerable<int> values) => Array(w,key,values,(a,x) => a.WriteNumberValue(x));
    private static int[] Counts(JsonElement p,string key) { var a = A(p,key,3).Select(x => x.GetInt32()).ToArray(); Check(a.Length == 3,"three-leg-counts"); return a; }
    public static byte[] WriteResult(TwoCompoundGearRoutingResult g)
    {
        ValidateCached(g);
        var bytes = Envelope(TwoCompoundRoutingContract.ResultFormat,w => {
            w.WriteStartObject(); w.WriteString("requestId",g.RequestId); Embed(w,"request",WriteRequest(g.Normalized.Request!));
            w.WriteString("status",g.Status.ToString()); w.WriteBoolean("searchComplete",g.SearchComplete); w.WriteBoolean("resultTruncated",g.ResultTruncated);
            var s = g.Summary; w.WriteStartObject("summary"); w.WriteNumber("examinedAssignments",s.ExaminedAssignments); w.WriteNumber("ratioValidAssignments",s.RatioValidAssignments);
            w.WriteNumber("examinedSitePairs",s.ExaminedSitePairs); w.WriteNumber("nodeChecks",s.NodeChecks); w.WriteNumber("adjacencyChecks",s.AdjacencyChecks);
            Counts(w,"legExpansions",s.LegExpansions); Counts(w,"observedRoutes",s.ObservedRoutes); w.WriteNumber("earlyRejections",s.EarlyRejections);
            w.WriteNumber("mergeAttempts",s.MergeAttempts); w.WriteNumber("globalRejections",s.GlobalRejections); w.WriteNumber("validMechanisms",s.ValidMechanisms);
            w.WriteNumber("deduplicatedMechanisms",s.DeduplicatedMechanisms); w.WriteNumber("returnedCount",g.Candidates.Count); w.WriteNumber("work",s.Work);
            w.WriteBoolean("workRemaining",s.WorkRemaining); w.WriteString("stopStage",s.StopStage); w.WriteString("frontier",s.Frontier); w.WriteEndObject();
            Array(w,"candidates",g.Candidates,(a,c) => {
                a.WriteStartObject(); a.WriteString("candidateId",c.CandidateId); a.WriteStartObject("assignment"); var t = c.Assignment;
                a.WriteString("assignmentId",t.AssignmentId); a.WriteNumber("inputTeeth",t.InputTeeth); a.WriteNumber("outputTeeth",t.OutputTeeth); Counts(a,"teeth",t.Teeth);
                a.WritePropertyName("magnitude"); R(a,t.Magnitude); a.WriteEndObject(); Array(a,"paths",c.Paths,(b,p) => { b.WriteStartArray(); foreach(var x in p) Assignment(b,x); b.WriteEndArray(); });
                a.WriteStartObject("metrics"); a.WriteNumber("idlers",c.Metrics.Idlers); Number(a,"preferredPenalty",c.Metrics.PreferredPenalty); Number(a,"footprint",c.Metrics.Footprint);
                Number(a,"totalTeeth",c.Metrics.TotalTeeth); a.WriteString("signature",c.Metrics.Signature); a.WriteEndObject();
                Embed(a,"mechanism",WriteMechanism(c).Bytes); Certificate(a,TwoCompoundGearRouteValidator.Validate(g.Normalized.Request!,c.Assignment,c.Paths,c.Mechanism,c.CandidateId)); a.WriteEndObject();
            });
            Array(w,"attempts",g.Attempts,(a,x) => { a.WriteStartObject(); a.WriteString("assignmentId",x.AssignmentId); Array(a,"sites",x.Sites,Point);
                a.WriteNumber("workStart",x.WorkStart); a.WriteNumber("workEnd",x.WorkEnd); Counts(a,"observedRoutes",x.ObservedRoutes); a.WriteNumber("earlyRejections",x.EarlyRejections);
                a.WriteNumber("merges",x.Merges); a.WriteNumber("rejections",x.Rejections); a.WriteNumber("valid",x.Valid); a.WriteBoolean("complete",x.Complete); a.WriteEndObject(); });
            w.WriteNumber("omittedAttempts",g.OmittedAttempts); Array(w,"rejections",g.Rejections,(a,x) => { a.WriteStartObject(); a.WriteString("code",x.Code); a.WriteNumber("count",x.Count); a.WriteEndObject(); });
            Array(w,"details",g.Details,Issue); w.WriteEndObject();
        });
        Check(bytes.Length <= TwoCompoundRoutingContract.MaxResultBytes,"two-compound-result-byte-limit"); return bytes;
    }
    public static TwoCompoundGearRoutingResult ReadResult(byte[] bytes)
    {
        using var d = Open(bytes,TwoCompoundRoutingContract.ResultFormat,TwoCompoundRoutingContract.MaxResultBytes); var p = d.RootElement.GetProperty("payload");
        var r = ReadRequest(Bytes(p.GetProperty("request"))); var n = AnchoredTwoCompoundGearRouter.Normalize(r); var s = p.GetProperty("summary");
        var candidates = A(p,"candidates",GearRoutingContract.MaxReturned).Select(c => {
            var t = c.GetProperty("assignment"); var teeth = A(t,"teeth",4).Select(x => x.GetInt32()).ToArray(); Check(teeth.Length == 4,"two-compound-assignment-length");
            var assignment = new TwoCompoundToothAssignment(I(t,"inputTeeth"),I(t,"outputTeeth"),teeth[0],teeth[1],teeth[2],teeth[3]);
            var paths = A(c,"paths",3).Select(x => { Check(x.ValueKind == JsonValueKind.Array && x.GetArrayLength() <= 7,"two-compound-path-limit"); return x.EnumerateArray().Select(Assignment).ToArray(); }).ToArray();
            var a = c.GetProperty("mechanism"); var k = a.GetProperty("kinematic"); A(k,"dofs",19); A(k,"couplings",18); A(k,"solution",19);
            var e = a.GetProperty("spatial"); A(e,"axes",19); A(e,"bodies",21); A(e,"contacts",18);
            var playback = a.GetProperty("resolvedPlayback"); A(playback,"drivers",1); A(playback,"channels",19); A(playback,"bodyBindings",21);
            var artifact = Mechanisms.Read(Bytes(a)); var identity = Mechanisms.VerifyIdentity(artifact); Check(identity.CandidateIdMatches && identity.ArtifactHashMatches,"two-compound-artifact-identity");
            var fresh = new GenerationEngine().Generate(new LowLevelMechanicalSpecification(TwoCompoundRoutingContract.Profile,artifact.Candidate.Kinematic,artifact.Candidate.Spatial));
            Check(fresh.IsSuccess,"two-compound-generic-invalid"); Check(Bytes(a).SequenceEqual(Mechanisms.Write(fresh.Candidates.Single(),Metadata()).Bytes),"two-compound-derived-data/metadata-mismatch");
            var id = S(c,"candidateId"); var m = c.GetProperty("metrics");
            return new TwoCompoundGearRouteCandidate(id,assignment,paths,artifact.Candidate,new GearRouteMetrics(I(m,"idlers"),N(m.GetProperty("preferredPenalty")),N(m.GetProperty("footprint")),N(m.GetProperty("totalTeeth")),S(m,"signature")),
                TwoCompoundGearRouteValidator.Validate(r,assignment,paths,artifact.Candidate,id));
        }).ToArray();
        var g = new TwoCompoundGearRoutingResult(n,E<GearRoutingStatus>(p,"status"),new TwoCompoundRoutingSearchSummary(I(s,"examinedAssignments"),I(s,"ratioValidAssignments"),I(s,"examinedSitePairs"),I(s,"nodeChecks"),I(s,"adjacencyChecks"),
            Counts(s,"legExpansions"),Counts(s,"observedRoutes"),I(s,"earlyRejections"),I(s,"mergeAttempts"),I(s,"globalRejections"),I(s,"validMechanisms"),s.GetProperty("workRemaining").GetBoolean(),S(s,"stopStage"),S(s,"frontier")),
            candidates,A(p,"rejections",128).Select(x => new GearRoutingRejection(S(x,"code"),I(x,"count"))),
            A(p,"attempts",GearRoutingContract.MaxDetails).Select(x => new TwoCompoundRoutingPlacementAttempt(S(x,"assignmentId"),A(x,"sites",2).Select(Point),I(x,"workStart"),I(x,"workEnd"),Counts(x,"observedRoutes"),I(x,"earlyRejections"),I(x,"merges"),I(x,"rejections"),I(x,"valid"),x.GetProperty("complete").GetBoolean())),
            I(p,"omittedAttempts"),A(p,"details",GearRoutingContract.MaxDetails).Select(Issue));
        Check(bytes.SequenceEqual(WriteResult(g)),"two-compound-result-noncanonical/context"); return g;
    }
    public static void ValidateCached(TwoCompoundGearRoutingResult g)
    {
        var n = AnchoredTwoCompoundGearRouter.Normalize(g.Normalized.Request); Check(n.IsValid && n.RequestId == g.RequestId,"two-compound-result-request"); var r = n.Request!; var s = g.Summary;
        Check(g.Status == GearRoutingStatus.Complete || g.Status == GearRoutingStatus.Infeasible || g.Status == GearRoutingStatus.IncompleteBudget || g.Status == GearRoutingStatus.Cancelled,"two-compound-result-status");
        Check(new[] {s.ExaminedAssignments,s.RatioValidAssignments,s.ExaminedSitePairs,s.NodeChecks,s.AdjacencyChecks,s.EarlyRejections,s.MergeAttempts,s.GlobalRejections,s.ValidMechanisms}.Concat(s.LegExpansions).Concat(s.ObservedRoutes).All(x => x >= 0 && x <= TwoCompoundRoutingContract.MaxPreparationChecks),"two-compound-count-bounds");
        Check(s.ExaminedAssignments <= r.Compounds.Aggregate(1,(count,c) => count * c.ReceivingTeeth.Count * c.DrivingTeeth.Count) && s.RatioValidAssignments <= s.ExaminedAssignments &&
            s.ExaminedSitePairs <= s.RatioValidAssignments * r.Compounds[0].Sites.Count * r.Compounds[1].Sites.Count && s.NodeChecks + s.AdjacencyChecks <= n.PreparationCeiling,"two-compound-preparation-counts");
        Check(s.Work <= r.WorkBudget && s.ObservedRoutes.Select((x,i) => x <= s.LegExpansions[i]).All(x => x) && s.EarlyRejections <= s.ObservedRoutes.Sum() && s.MergeAttempts <= s.ObservedRoutes[2] && s.GlobalRejections + s.ValidMechanisms == s.MergeAttempts,"two-compound-work-counts");
        Check(g.Candidates.Count == Math.Min(s.ValidMechanisms,r.MaximumReturned) && g.Candidates.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == g.Candidates.Count,"two-compound-retention-counts");
        Check(g.SearchComplete != s.WorkRemaining && (g.Status != GearRoutingStatus.Complete || s.ValidMechanisms > 0) && (g.Status != GearRoutingStatus.Infeasible || s.ValidMechanisms == 0) && (g.Status != GearRoutingStatus.IncompleteBudget || s.Work == r.WorkBudget),"two-compound-completeness");
        Check(s.Frontier.Length <= 8192 && new[] {"none","beforeSearch","assignment","sitePair","preparation","leg0Successor","leg1Successor","leg2Successor","merge","afterSearch"}.Contains(s.StopStage) && (g.SearchComplete == (s.StopStage == "none")),"two-compound-frontier");
        Check(g.OmittedAttempts >= 0 && g.OmittedAttempts <= s.ExaminedSitePairs && g.Attempts.Count + g.OmittedAttempts == s.ExaminedSitePairs && (g.OmittedAttempts == 0 || g.Attempts.Count == GearRoutingContract.MaxDetails),"two-compound-attempt-retention");
        Check(g.Rejections.Count <= 128 && g.Rejections.All(x => x.Count > 0 && x.Count <= TwoCompoundRoutingContract.MaxPreparationChecks + GearRoutingContract.MaxExpansions) && g.Rejections.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() == g.Rejections.Count,"two-compound-rejection-counts");
        for (int i = 0; i < g.Attempts.Count; i++)
        {
            var a = g.Attempts[i]; Check(a.WorkStart >= 1 && a.WorkEnd > a.WorkStart && a.WorkEnd <= s.Work && (i == 0 || g.Attempts[i-1].WorkEnd <= a.WorkStart) &&
                a.ObservedRoutes.All(x => x >= 0) && a.EarlyRejections >= 0 && a.EarlyRejections <= a.ObservedRoutes.Sum() && a.Merges >= 0 && a.Rejections >= 0 && a.Valid >= 0 && a.Rejections + a.Valid == a.Merges && a.Merges <= a.ObservedRoutes[2] &&
                (!g.SearchComplete || a.Complete) && r.Compounds[0].Sites.Contains(a.Sites[0]) && r.Compounds[1].Sites.Contains(a.Sites[1]),"two-compound-attempt-shape");
        }
        for (int i = 0; i < g.Candidates.Count; i++)
        {
            var c = g.Candidates[i]; Check(c.CandidateId == Mechanisms.ComputeCandidateId(c.Mechanism),"two-compound-mechanical-identity");
            var v = TwoCompoundGearRouteValidator.Validate(r,c.Assignment,c.Paths,c.Mechanism,c.CandidateId); Check(v.IsValid,"two-compound-context:" + string.Join("|",v.Diagnostics.Select(x => x.Code)));
            Check(c.Validation.IsValid && c.Validation.RequestId == v.RequestId && c.Validation.CandidateId == v.CandidateId && c.Validation.ContextId == v.ContextId && c.Validation.LegTransfers.SequenceEqual(v.LegTransfers) && c.Validation.TotalTransfer == v.TotalTransfer,"two-compound-attached-context");
            var m = TwoCompoundGearRouteValidator.Metrics(r,c.Paths); Check(m.Idlers == c.Metrics.Idlers && m.PreferredPenalty == c.Metrics.PreferredPenalty && m.Footprint == c.Metrics.Footprint && m.TotalTeeth == c.Metrics.TotalTeeth && m.Signature == c.Metrics.Signature,"two-compound-metrics");
            Check(i == 0 || TwoCompoundGearRouteValidator.Compare(g.Candidates[i-1],c) < 0,"two-compound-ranking");
        }
    }
    private static void Certificate(Utf8JsonWriter w,TwoCompoundRouteValidation v)
    {
        w.WriteStartObject("validation"); w.WriteString("requestId",v.RequestId); w.WriteString("candidateId",v.CandidateId); w.WriteString("contextId",v.ContextId);
        w.WriteBoolean("genericValid",v.GenericValidation.IsValid); w.WriteBoolean("twoCompoundContextValid",v.IsValid); Array(w,"legTransfers",v.LegTransfers,(a,q) => R(a,q!.Value));
        w.WritePropertyName("totalTransfer"); R(w,v.TotalTransfer!.Value); w.WriteNumber("sharedCompoundDofs",2); w.WriteNumber("unrelatedPairChecks",v.Spatial.UnrelatedSameLayerPairChecks);
        Array(w,"contacts",v.Spatial.ContactReadbacks,(a,c) => { a.WriteStartObject(); a.WriteString("id",c.ContactId); Number(a,"actualSquared",c.ActualCenterDistanceSquared); Number(a,"expectedSquared",c.ExpectedCenterDistanceSquared); Number(a,"residualSquared",c.ResidualSquared); a.WriteEndObject(); }); w.WriteEndObject();
    }
}
