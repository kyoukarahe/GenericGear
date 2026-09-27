using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using static GearInvest.Serialization.GearRoutingJson;

namespace GearInvest.Serialization;

/// <summary>Additive bounded sidecars; integrity/context validation is not historical search certification.</summary>
public static class CompoundRoutingJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    public static byte[] WriteRequest(AnchoredCompoundGearRoutingRequest raw)
    {
        var n = AnchoredCompoundGearRouter.Normalize(raw); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code)));
        var bytes = Envelope(CompoundRoutingContract.RequestFormat, w => {
            var r = n.Request!; w.WriteStartObject(); w.WriteString("requestId", n.RequestId); w.WriteString("profile", r.Profile); w.WriteString("backend", r.Backend); w.WriteString("ranking", r.Ranking);
            w.WriteString("unit", r.Unit); w.WriteNumber("inputLayer", r.InputLayer); w.WriteNumber("outputLayer", r.OutputLayer);
            Anchor(w, "input", r.Input); Anchor(w, "output", r.Output); w.WritePropertyName("targetTransfer"); R(w, r.TargetTransfer); Number(w, "pitchRadiusTicksPerTooth", r.PitchRadiusTicksPerTooth);
            Array(w, "receivingTeeth", r.ReceivingTeeth, (a, x) => a.WriteNumberValue(x)); Array(w, "drivingTeeth", r.DrivingTeeth, (a, x) => a.WriteNumberValue(x));
            Array(w, "compoundSites", r.CompoundSites, Point); w.WritePropertyName("inputLeg"); Leg(w, r.InputLeg); w.WritePropertyName("outputLeg"); Leg(w, r.OutputLeg);
            w.WriteNumber("maximumTotalIdlers", r.MaximumTotalIdlers);
            Array(w, "keepOuts", r.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); a.WritePropertyName("bounds"); Box(a, k.Bounds); a.WriteString("layers", k.Layers.ToString()); a.WriteEndObject(); });
            OptionalBox(w, "requiredCompoundRegion", r.RequiredCompoundRegion); OptionalBox(w, "preferredCompoundRegion", r.PreferredCompoundRegion);
            w.WriteNumber("workBudget", r.WorkBudget); w.WriteNumber("maximumReturned", r.MaximumReturned); w.WriteEndObject();
        });
        Check(bytes.Length <= CompoundRoutingContract.MaxRequestBytes, "compound-request-byte-limit"); return bytes;
    }
    public static AnchoredCompoundGearRoutingRequest ReadRequest(byte[] bytes)
    {
        using var d = Open(bytes, CompoundRoutingContract.RequestFormat, CompoundRoutingContract.MaxRequestBytes); var p = d.RootElement.GetProperty("payload");
        var r = new AnchoredCompoundGearRoutingRequest(Anchor(p.GetProperty("input")), Anchor(p.GetProperty("output")), R(p.GetProperty("targetTransfer")), N(p.GetProperty("pitchRadiusTicksPerTooth")),
            A(p, "receivingTeeth", GearRoutingContract.MaxToothOptions).Select(x => x.GetInt32()), A(p, "drivingTeeth", GearRoutingContract.MaxToothOptions).Select(x => x.GetInt32()),
            Leg(p.GetProperty("inputLeg")), Leg(p.GetProperty("outputLeg")), A(p, "compoundSites", CompoundRoutingContract.MaxCompoundSites).Select(Point),
            maximumTotalIdlers: I(p, "maximumTotalIdlers"), keepOuts: A(p, "keepOuts", GearRoutingContract.MaxKeepOuts).Select(k => new LayeredGearRouteKeepOut(S(k, "id"), Box(k.GetProperty("bounds")), E<GearRouteLayers>(k, "layers"))),
            requiredCompoundRegion: OptionalBox(p, "requiredCompoundRegion"), preferredCompoundRegion: OptionalBox(p, "preferredCompoundRegion"), workBudget: I(p, "workBudget"), maximumReturned: I(p, "maximumReturned"),
            inputLayer: I(p, "inputLayer"), outputLayer: I(p, "outputLayer"), unit: S(p, "unit"), profile: S(p, "profile"), backend: S(p, "backend"), ranking: S(p, "ranking"));
        Check(bytes.SequenceEqual(WriteRequest(r)), "compound-request-noncanonical/identity"); return AnchoredCompoundGearRouter.Normalize(r).Request!;
    }
    internal static void Leg(Utf8JsonWriter w, CompoundRoutingLeg l)
    {
        w.WriteStartObject(); w.WritePropertyName("bounds"); Box(w, l.Bounds); Array(w, "sites", l.Sites, Point); Array(w, "idlerTeeth", l.IdlerTeeth, (a, t) => a.WriteNumberValue(t));
        w.WriteNumber("minIdlers", l.MinIdlers); w.WriteNumber("maxIdlers", l.MaxIdlers); Number(w, "unrelatedClearance", l.UnrelatedClearance); Number(w, "keepOutClearance", l.KeepOutClearance);
        Array(w, "requiredRegions", l.RequiredRegions, Region); OptionalBox(w, "preferredRegion", l.PreferredRegion); w.WriteEndObject();
    }
    internal static CompoundRoutingLeg Leg(JsonElement p) => new CompoundRoutingLeg(Box(p.GetProperty("bounds")), A(p, "sites", GearRoutingContract.MaxSites).Select(Point),
        idlerTeeth: A(p, "idlerTeeth", GearRoutingContract.MaxToothOptions).Select(x => x.GetInt32()), minIdlers: I(p, "minIdlers"), maxIdlers: I(p, "maxIdlers"),
        unrelatedClearance: N(p.GetProperty("unrelatedClearance")), keepOutClearance: N(p.GetProperty("keepOutClearance")), requiredRegions: A(p, "requiredRegions", GearRoutingContract.MaxRequired).Select(Region), preferredRegion: OptionalBox(p, "preferredRegion"));

    private static ArtifactMetadata Metadata() => new ArtifactMetadata(new ArtifactSourceProvenance(CompoundRoutingContract.Profile, "0", CompoundRoutingContract.Profile),
        new GeneratorFingerprint("0.1.0-dev", "compound-routing-v1", CompoundRoutingContract.Backend, CompoundRoutingContract.Profile));
    public static ArtifactWriteResult WriteMechanism(CompoundGearRouteCandidate c)
    { Check(Mechanisms.ComputeCandidateId(c.Mechanism) == c.CandidateId, "compound-mechanical-identity"); return Mechanisms.Write(c.Mechanism, Metadata()); }

    public static byte[] WriteResult(CompoundGearRoutingResult g)
    {
        ValidateCached(g);
        var bytes = Envelope(CompoundRoutingContract.ResultFormat, w => {
            w.WriteStartObject(); w.WriteString("requestId", g.RequestId); Embed(w, "request", WriteRequest(g.Normalized.Request!));
            w.WriteString("status", g.Status.ToString()); w.WriteBoolean("searchComplete", g.SearchComplete); w.WriteBoolean("resultTruncated", g.ResultTruncated);
            var s = g.Summary; w.WriteStartObject("summary"); w.WriteNumber("examinedPairs", s.ExaminedPairs); w.WriteNumber("ratioValidPairs", s.RatioValidPairs); w.WriteNumber("examinedSites", s.ExaminedSites);
            w.WriteNumber("nodeChecks", s.NodeChecks); w.WriteNumber("adjacencyChecks", s.AdjacencyChecks); w.WriteNumber("inputExpansions", s.InputExpansions); w.WriteNumber("outputExpansions", s.OutputExpansions);
            w.WriteNumber("inputRoutes", s.InputRoutes); w.WriteNumber("outputRoutes", s.OutputRoutes); w.WriteNumber("mergeAttempts", s.MergeAttempts); w.WriteNumber("globalRejections", s.GlobalRejections);
            w.WriteNumber("validMechanisms", s.ValidMechanisms); w.WriteNumber("deduplicatedMechanisms", s.DeduplicatedMechanisms); w.WriteNumber("returnedCount", g.Candidates.Count); w.WriteNumber("work", s.Work);
            w.WriteBoolean("workRemaining", s.WorkRemaining); w.WriteString("stopStage", s.StopStage); w.WriteString("frontier", s.Frontier); w.WriteEndObject();
            Array(w, "candidates", g.Candidates, (a, c) => {
                a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); a.WriteStartObject("pair"); Pair(a, c.Pair); a.WriteEndObject();
                Array(a, "inputPath", c.InputPath, Assignment); Array(a, "outputPath", c.OutputPath, Assignment);
                a.WriteStartObject("metrics"); a.WriteNumber("idlers", c.Metrics.Idlers); Number(a, "preferredPenalty", c.Metrics.PreferredPenalty); Number(a, "footprint", c.Metrics.Footprint); Number(a, "totalTeeth", c.Metrics.TotalTeeth); a.WriteString("signature", c.Metrics.Signature); a.WriteEndObject();
                Embed(a, "mechanism", WriteMechanism(c).Bytes); Certificate(a, CompoundGearRouteValidator.Validate(g.Normalized.Request!, c.Pair, c.InputPath, c.OutputPath, c.Mechanism, c.CandidateId)); a.WriteEndObject();
            });
            Array(w, "attempts", g.Attempts, (a, x) => { a.WriteStartObject(); a.WriteString("pairId", x.PairId); a.WritePropertyName("site"); Point(a, x.Site);
                a.WriteNumber("workStart", x.WorkStart); a.WriteNumber("workEnd", x.WorkEnd); a.WriteNumber("inputRoutes", x.InputRoutes); a.WriteNumber("outputRoutes", x.OutputRoutes);
                a.WriteNumber("merges", x.Merges); a.WriteNumber("rejections", x.Rejections); a.WriteNumber("valid", x.Valid); a.WriteBoolean("complete", x.Complete); a.WriteEndObject(); });
            w.WriteNumber("omittedAttempts", g.OmittedAttempts);
            Array(w, "rejections", g.Rejections, (a, x) => { a.WriteStartObject(); a.WriteString("code", x.Code); a.WriteNumber("count", x.Count); a.WriteEndObject(); });
            Array(w, "details", g.Details, Issue); w.WriteEndObject();
        });
        Check(bytes.Length <= CompoundRoutingContract.MaxResultBytes, "compound-result-byte-limit"); return bytes;
    }
    public static CompoundGearRoutingResult ReadResult(byte[] bytes)
    {
        using var d = Open(bytes, CompoundRoutingContract.ResultFormat, CompoundRoutingContract.MaxResultBytes); var p = d.RootElement.GetProperty("payload");
        var r = ReadRequest(Bytes(p.GetProperty("request"))); var n = AnchoredCompoundGearRouter.Normalize(r); var s = p.GetProperty("summary");
        var candidates = A(p, "candidates", GearRoutingContract.MaxReturned).Select(c => {
            var pair = Pair(c.GetProperty("pair")); var lp = A(c, "inputPath", 7).Select(Assignment).ToArray(); var rp = A(c, "outputPath", 7).Select(Assignment).ToArray();
            var a = c.GetProperty("mechanism"); CheckArtifactBounds(a); var artifact = Mechanisms.Read(Bytes(a)); var identity = Mechanisms.VerifyIdentity(artifact);
            Check(identity.CandidateIdMatches && identity.ArtifactHashMatches, "compound-artifact-identity");
            var fresh = new GenerationEngine().Generate(new LowLevelMechanicalSpecification(CompoundRoutingContract.Profile, artifact.Candidate.Kinematic, artifact.Candidate.Spatial));
            Check(fresh.IsSuccess, "compound-generic-invalid"); Check(Bytes(a).SequenceEqual(Mechanisms.Write(fresh.Candidates.Single(), Metadata()).Bytes), "compound-derived-data/metadata-mismatch");
            var id = S(c, "candidateId"); var m = c.GetProperty("metrics");
            return new CompoundGearRouteCandidate(id, pair, lp, rp, artifact.Candidate, new GearRouteMetrics(I(m, "idlers"), N(m.GetProperty("preferredPenalty")), N(m.GetProperty("footprint")), N(m.GetProperty("totalTeeth")), S(m, "signature")),
                CompoundGearRouteValidator.Validate(r, pair, lp, rp, artifact.Candidate, id));
        }).ToArray();
        var g = new CompoundGearRoutingResult(n, E<GearRoutingStatus>(p, "status"), new CompoundRoutingSearchSummary(I(s, "examinedPairs"), I(s, "ratioValidPairs"), I(s, "examinedSites"), I(s, "nodeChecks"), I(s, "adjacencyChecks"),
            I(s, "inputExpansions"), I(s, "outputExpansions"), I(s, "inputRoutes"), I(s, "outputRoutes"), I(s, "mergeAttempts"), I(s, "globalRejections"), I(s, "validMechanisms"), s.GetProperty("workRemaining").GetBoolean(), S(s, "stopStage"), S(s, "frontier")),
            candidates, A(p, "rejections", 128).Select(x => new GearRoutingRejection(S(x, "code"), I(x, "count"))),
            A(p, "attempts", GearRoutingContract.MaxDetails).Select(x => new CompoundRoutingPlacementAttempt(S(x, "pairId"), Point(x.GetProperty("site")), I(x, "workStart"), I(x, "workEnd"), I(x, "inputRoutes"), I(x, "outputRoutes"), I(x, "merges"), I(x, "rejections"), I(x, "valid"), x.GetProperty("complete").GetBoolean())),
            I(p, "omittedAttempts"), A(p, "details", GearRoutingContract.MaxDetails).Select(Issue));
        Check(bytes.SequenceEqual(WriteResult(g)), "compound-result-noncanonical/context"); return g;
    }
    public static void ValidateCached(CompoundGearRoutingResult g)
    {
        var n = AnchoredCompoundGearRouter.Normalize(g.Normalized.Request); Check(n.IsValid && n.RequestId == g.RequestId, "compound-result-request"); var r = n.Request!; var s = g.Summary;
        Check(g.Status == GearRoutingStatus.Complete || g.Status == GearRoutingStatus.Infeasible || g.Status == GearRoutingStatus.IncompleteBudget || g.Status == GearRoutingStatus.Cancelled, "compound-result-status");
        Check(new[] { s.ExaminedPairs, s.RatioValidPairs, s.ExaminedSites, s.NodeChecks, s.AdjacencyChecks, s.InputExpansions, s.OutputExpansions, s.InputRoutes, s.OutputRoutes, s.MergeAttempts, s.GlobalRejections, s.ValidMechanisms }.All(x => x >= 0 && x <= CompoundRoutingContract.MaxPreparationChecks), "compound-nonnegative-counts");
        Check(s.ExaminedPairs <= r.ReceivingTeeth.Count * r.DrivingTeeth.Count && s.RatioValidPairs <= s.ExaminedPairs && s.ExaminedSites <= s.RatioValidPairs * r.CompoundSites.Count && s.NodeChecks + s.AdjacencyChecks <= n.PreparationCeiling, "compound-preparation-counts");
        Check(s.Work <= r.WorkBudget && s.InputRoutes <= s.InputExpansions && s.OutputRoutes <= s.OutputExpansions && s.MergeAttempts <= s.OutputRoutes && s.GlobalRejections + s.ValidMechanisms == s.MergeAttempts, "compound-work-counts");
        Check(g.Candidates.Count == Math.Min(s.ValidMechanisms, r.MaximumReturned) && g.Candidates.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == g.Candidates.Count, "compound-retention-counts");
        Check(g.SearchComplete != s.WorkRemaining && (g.Status != GearRoutingStatus.Complete || s.ValidMechanisms > 0) && (g.Status != GearRoutingStatus.Infeasible || s.ValidMechanisms == 0) && (g.Status != GearRoutingStatus.IncompleteBudget || s.Work == r.WorkBudget), "compound-completeness");
        Check(s.Frontier.Length <= 8192 && new[] { "none", "beforeSearch", "toothPair", "compoundSite", "preparation", "inputSuccessor", "outputSuccessor", "merge", "afterSearch" }.Contains(s.StopStage) && (g.SearchComplete == (s.StopStage == "none")), "compound-frontier");
        Check(g.OmittedAttempts >= 0 && g.OmittedAttempts <= s.ExaminedSites && g.Attempts.Count + g.OmittedAttempts == s.ExaminedSites && (g.OmittedAttempts == 0 || g.Attempts.Count == GearRoutingContract.MaxDetails), "compound-attempt-retention");
        Check(g.Rejections.Count <= 128 && g.Rejections.All(x => x.Count > 0 && x.Count <= CompoundRoutingContract.MaxPreparationChecks + GearRoutingContract.MaxExpansions) && g.Rejections.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() == g.Rejections.Count, "compound-rejection-counts");
        for (int i = 0; i < g.Attempts.Count; i++)
        {
            var a = g.Attempts[i]; Check(a.WorkStart >= 1 && a.WorkEnd > a.WorkStart && a.WorkEnd <= s.Work && (i == 0 || g.Attempts[i - 1].WorkEnd <= a.WorkStart) &&
                a.InputRoutes >= 0 && a.OutputRoutes >= 0 && a.Merges >= 0 && a.Rejections >= 0 && a.Valid >= 0 && a.Rejections + a.Valid == a.Merges && a.Merges <= a.OutputRoutes &&
                (!g.SearchComplete || a.Complete) && r.CompoundSites.Contains(a.Site), "compound-attempt-shape");
        }
        for (int i = 0; i < g.Candidates.Count; i++)
        {
            var c = g.Candidates[i]; Check(c.CandidateId == Mechanisms.ComputeCandidateId(c.Mechanism), "compound-mechanical-identity");
            var v = CompoundGearRouteValidator.Validate(r, c.Pair, c.InputPath, c.OutputPath, c.Mechanism, c.CandidateId); Check(v.IsValid, "compound-context:" + string.Join("|", v.Diagnostics.Select(d => d.Code)));
            Check(c.Validation.IsValid && c.Validation.RequestId == v.RequestId && c.Validation.CandidateId == v.CandidateId && c.Validation.ContextId == v.ContextId && c.Validation.InputTransfer == v.InputTransfer && c.Validation.OutputTransfer == v.OutputTransfer && c.Validation.TotalTransfer == v.TotalTransfer, "compound-attached-context");
            var m = CompoundGearRouteValidator.Metrics(r, c.InputPath, c.OutputPath);
            Check(m.Idlers == c.Metrics.Idlers && m.PreferredPenalty == c.Metrics.PreferredPenalty && m.Footprint == c.Metrics.Footprint && m.TotalTeeth == c.Metrics.TotalTeeth && m.Signature == c.Metrics.Signature, "compound-metrics");
            Check(i == 0 || CompoundGearRouteValidator.Compare(g.Candidates[i - 1], c) < 0, "compound-ranking");
        }
    }
    private static void CheckArtifactBounds(JsonElement a)
    {
        var k = a.GetProperty("kinematic"); A(k, "dofs", 13); A(k, "couplings", 12); A(k, "solution", 13);
        var s = a.GetProperty("spatial"); A(s, "axes", 13); A(s, "bodies", 14); A(s, "contacts", 12);
        var p = a.GetProperty("resolvedPlayback"); A(p, "drivers", 1); A(p, "channels", 13); A(p, "bodyBindings", 14);
    }
    private static void Certificate(Utf8JsonWriter w, CompoundRouteValidation v)
    {
        w.WriteStartObject("validation"); w.WriteString("requestId", v.RequestId); w.WriteString("candidateId", v.CandidateId); w.WriteString("contextId", v.ContextId);
        w.WriteBoolean("genericValid", v.GenericValidation.IsValid); w.WriteBoolean("compoundContextValid", v.IsValid);
        w.WritePropertyName("inputTransfer"); R(w, v.InputTransfer!.Value); w.WritePropertyName("outputTransfer"); R(w, v.OutputTransfer!.Value); w.WritePropertyName("totalTransfer"); R(w, v.TotalTransfer!.Value);
        w.WriteNumber("sharedCompoundDofs", 1); w.WriteNumber("unrelatedPairChecks", v.Spatial.UnrelatedSameLayerPairChecks);
        Array(w, "contacts", v.Spatial.ContactReadbacks, (a, c) => { a.WriteStartObject(); a.WriteString("id", c.ContactId); Number(a, "actualSquared", c.ActualCenterDistanceSquared); Number(a, "expectedSquared", c.ExpectedCenterDistanceSquared); Number(a, "residualSquared", c.ResidualSquared); a.WriteEndObject(); }); w.WriteEndObject();
    }
    private static void Pair(Utf8JsonWriter w, AnchoredCompoundToothPair p) { w.WriteString("pairId", p.PairId); w.WriteNumber("inputTeeth", p.InputTeeth); w.WriteNumber("outputTeeth", p.OutputTeeth); w.WriteNumber("receivingTeeth", p.ReceivingTeeth); w.WriteNumber("drivingTeeth", p.DrivingTeeth); w.WritePropertyName("magnitude"); R(w, p.Magnitude); }
    private static AnchoredCompoundToothPair Pair(JsonElement p) => new AnchoredCompoundToothPair(I(p, "inputTeeth"), I(p, "outputTeeth"), I(p, "receivingTeeth"), I(p, "drivingTeeth"));
    internal static void Assignment(Utf8JsonWriter w, GearRouteAssignment a) { w.WriteStartObject(); w.WritePropertyName("position"); Point(w, a.Position); w.WriteNumber("teeth", a.Teeth); w.WriteEndObject(); }
    internal static GearRouteAssignment Assignment(JsonElement a) => new GearRouteAssignment(Point(a.GetProperty("position")), I(a, "teeth"));
    internal static void OptionalBox(Utf8JsonWriter w, string name, GearRouteBox? b) { w.WritePropertyName(name); if (b == null) w.WriteNullValue(); else Box(w, b); }
    internal static GearRouteBox? OptionalBox(JsonElement p, string name) => p.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Box(p.GetProperty(name));
    internal static void Anchor(Utf8JsonWriter w, string key, GearRouteAnchor a) { w.WriteStartObject(key); w.WritePropertyName("position"); Point(w, a.Position); w.WriteNumber("teeth", a.Teeth); w.WriteEndObject(); }
    internal static GearRouteAnchor Anchor(JsonElement p) => new GearRouteAnchor(Point(p.GetProperty("position")), I(p, "teeth"));
    private static void Region(Utf8JsonWriter w, GearRouteRegion r) { w.WriteStartObject(); w.WriteString("id", r.Id); w.WritePropertyName("bounds"); Box(w, r.Bounds); w.WriteEndObject(); }
    private static GearRouteRegion Region(JsonElement p) => new GearRouteRegion(S(p, "id"), Box(p.GetProperty("bounds")));
    internal static void Issue(Utf8JsonWriter w, Diagnostic d) { w.WriteStartObject(); w.WriteString("code", d.Code); w.WriteString("severity", d.Severity.ToString()); w.WriteString("message", d.Message); w.WriteString("subject", d.SubjectId); w.WriteEndObject(); }
    internal static Diagnostic Issue(JsonElement p) => new Diagnostic(S(p, "code"), E<DiagnosticSeverity>(p, "severity"), S(p, "message"), p.GetProperty("subject").GetString());
}
