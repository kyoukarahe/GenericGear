using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using static GearInvest.Serialization.DiscreteEmbodimentJson;

namespace GearInvest.Serialization;

/// <summary>Bounded canonical sidecars. Cached load verifies members, not historical search exhaustiveness.</summary>
public static class GearRoutingJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    public static byte[] WriteRequest(AnchoredGearRoutingRequest input)
    {
        var n = AnchoredGearRouter.Normalize(input); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code)));
        return Envelope(GearRoutingContract.RequestFormat, w => {
            var r = n.Request!; w.WriteStartObject(); w.WriteString("requestId", n.RequestId);
            w.WriteString("profile", r.Profile); w.WriteString("backend", r.Backend); w.WriteString("ranking", r.Ranking);
            w.WriteString("unit", r.Unit); w.WriteString("topology", r.Topology); w.WriteNumber("layer", r.Layer);
            Anchor(w, "input", r.Input); Anchor(w, "output", r.Output); w.WritePropertyName("targetTransfer"); R(w, r.TargetTransfer);
            Number(w, "pitchRadiusTicksPerTooth", r.PitchRadiusTicksPerTooth); w.WritePropertyName("bounds"); Box(w, r.Bounds);
            Array(w, "sites", r.Sites, Point); Array(w, "idlerTeeth", r.IdlerTeeth, (a, t) => a.WriteNumberValue(t));
            w.WriteNumber("minIdlers", r.MinIdlers); w.WriteNumber("maxIdlers", r.MaxIdlerCount);
            Number(w, "unrelatedClearance", r.UnrelatedClearance); Number(w, "keepOutClearance", r.KeepOutClearance);
            Array(w, "keepOuts", r.KeepOuts, Region); Array(w, "requiredRegions", r.RequiredRegions, Region);
            w.WritePropertyName("preferredRegion"); if (r.PreferredRegion == null) w.WriteNullValue(); else Box(w, r.PreferredRegion);
            w.WriteNumber("expansionBudget", r.ExpansionBudget); w.WriteNumber("maximumReturned", r.MaximumReturned); w.WriteEndObject();
        });
    }
    public static AnchoredGearRoutingRequest ReadRequest(byte[] bytes)
    {
        using var d = Open(bytes, GearRoutingContract.RequestFormat, GearRoutingContract.MaxRequestBytes); var p = d.RootElement.GetProperty("payload");
        var r = new AnchoredGearRoutingRequest(Anchor(p.GetProperty("input")), Anchor(p.GetProperty("output")), R(p.GetProperty("targetTransfer")),
            N(p.GetProperty("pitchRadiusTicksPerTooth")), Box(p.GetProperty("bounds")), A(p, "sites", GearRoutingContract.MaxSites).Select(Point),
            idlerTeeth: A(p, "idlerTeeth", GearRoutingContract.MaxToothOptions).Select(t => t.GetInt32()), minIdlers: I(p, "minIdlers"), maxIdlers: I(p, "maxIdlers"),
            unrelatedClearance: N(p.GetProperty("unrelatedClearance")), keepOutClearance: N(p.GetProperty("keepOutClearance")),
            keepOuts: A(p, "keepOuts", GearRoutingContract.MaxKeepOuts).Select(Region), requiredRegions: A(p, "requiredRegions", GearRoutingContract.MaxRequired).Select(Region),
            preferredRegion: p.GetProperty("preferredRegion").ValueKind == JsonValueKind.Null ? null : Box(p.GetProperty("preferredRegion")),
            expansionBudget: I(p, "expansionBudget"), maximumReturned: I(p, "maximumReturned"), layer: I(p, "layer"), unit: S(p, "unit"), topology: S(p, "topology"),
            profile: S(p, "profile"), backend: S(p, "backend"), ranking: S(p, "ranking"));
        Check(bytes.SequenceEqual(WriteRequest(r)), "routing-request-identity/noncanonical-request"); return AnchoredGearRouter.Normalize(r).Request!;
    }
    public static ArtifactWriteResult WriteMechanism(GearRouteCandidate c)
    {
        Check(Mechanisms.ComputeCandidateId(c.Mechanism) == c.CandidateId, "routing-candidate-identity");
        return Mechanisms.Write(c.Mechanism, Metadata());
    }
    private static ArtifactMetadata Metadata() => new ArtifactMetadata(new ArtifactSourceProvenance(GearRoutingContract.Profile, "0", GearRoutingContract.Profile),
        new GeneratorFingerprint("0.1.0-dev", "anchored-routing-v1", GearRoutingContract.Backend, GearRoutingContract.Profile));

    public static byte[] WriteResult(GearRoutingResult g)
    {
        ValidateCached(g);
        var bytes = Envelope(GearRoutingContract.ResultFormat, w => {
            w.WriteStartObject(); w.WriteString("requestId", g.RequestId); Embed(w, "request", WriteRequest(g.Normalized.Request!));
            w.WriteString("status", g.Status.ToString()); w.WriteBoolean("searchComplete", g.SearchComplete); w.WriteBoolean("resultTruncated", g.ResultTruncated);
            var s = g.Summary; w.WriteStartObject("summary");
            w.WriteNumber("potentialNodes", s.PotentialNodes); w.WriteNumber("nodeChecks", s.NodeChecks); w.WriteNumber("staticRejectedNodes", s.StaticRejectedNodes);
            w.WriteNumber("adjacencyChecks", s.AdjacencyChecks); w.WriteNumber("edges", s.Edges); w.WriteNumber("expansions", s.Expansions);
            w.WriteNumber("rejectedSuccessors", s.RejectedSuccessors); w.WriteNumber("goalChecks", s.GoalChecks); w.WriteNumber("validRoutes", s.ValidRoutes);
            w.WriteBoolean("workRemaining", s.WorkRemaining); w.WriteEndObject();
            Array(w, "candidates", g.Candidates, (a, c) => {
                a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); Array(a, "path", c.Path, (b, x) => { b.WriteStartObject(); b.WritePropertyName("position"); Point(b, x.Position); b.WriteNumber("teeth", x.Teeth); b.WriteEndObject(); });
                a.WriteStartObject("metrics"); a.WriteNumber("idlers", c.Metrics.Idlers); Number(a, "preferredPenalty", c.Metrics.PreferredPenalty);
                Number(a, "footprint", c.Metrics.Footprint); Number(a, "totalTeeth", c.Metrics.TotalTeeth); a.WriteString("signature", c.Metrics.Signature); a.WriteEndObject();
                Embed(a, "mechanism", WriteMechanism(c).Bytes); Certificate(a, GearRouteValidator.Validate(g.Normalized.Request!, c.Path, c.Mechanism, c.CandidateId)); a.WriteEndObject();
            });
            Array(w, "rejections", g.Rejections, (a, r) => { a.WriteStartObject(); a.WriteString("code", r.Code); a.WriteNumber("count", r.Count); a.WriteEndObject(); });
            Array(w, "details", g.Details, Diagnostic); w.WriteNumber("omittedDetails", g.OmittedDetails); w.WriteEndObject();
        });
        Check(bytes.Length <= GearRoutingContract.MaxResultBytes, "routing-result-byte-limit"); return bytes;
    }
    public static GearRoutingResult ReadResult(byte[] bytes)
    {
        using var d = Open(bytes, GearRoutingContract.ResultFormat, GearRoutingContract.MaxResultBytes); var p = d.RootElement.GetProperty("payload");
        var request = ReadRequest(Bytes(p.GetProperty("request"))); var normalized = AnchoredGearRouter.Normalize(request); var s = p.GetProperty("summary");
        var candidates = A(p, "candidates", GearRoutingContract.MaxReturned).Select(c => {
            var path = A(c, "path", 7).Select(x => new GearRouteAssignment(Point(x.GetProperty("position")), I(x, "teeth"))).ToArray();
            var a = c.GetProperty("mechanism"); CheckArtifactBounds(a);
            var artifact = Mechanisms.Read(Bytes(a)); var identity = Mechanisms.VerifyIdentity(artifact);
            Check(identity.CandidateIdMatches && identity.ArtifactHashMatches, "routing-artifact-identity");
            // Reconstruct through the ordinary producer, so a rehashed false solution, playback or certificate is not trusted.
            var fresh = new GenerationEngine().Generate(new LowLevelMechanicalSpecification(GearRoutingContract.Profile, artifact.Candidate.Kinematic, artifact.Candidate.Spatial));
            Check(fresh.IsSuccess, "routing-generic-mechanism-invalid");
            Check(Bytes(a).SequenceEqual(Mechanisms.Write(fresh.Candidates.Single(), Metadata()).Bytes), "routing-artifact-derived-data/metadata-mismatch");
            var id = S(c, "candidateId"); var m = c.GetProperty("metrics");
            return new GearRouteCandidate(id, path, artifact.Candidate, new GearRouteMetrics(I(m, "idlers"), N(m.GetProperty("preferredPenalty")), N(m.GetProperty("footprint")), N(m.GetProperty("totalTeeth")), S(m, "signature")), GearRouteValidator.Validate(request, path, artifact.Candidate, id));
        }).ToArray();
        var g = new GearRoutingResult(normalized, E<GearRoutingStatus>(p, "status"), new GearRoutingSearchSummary(I(s, "potentialNodes"), I(s, "nodeChecks"), I(s, "staticRejectedNodes"), I(s, "adjacencyChecks"), I(s, "edges"), I(s, "expansions"), I(s, "rejectedSuccessors"), I(s, "goalChecks"), I(s, "validRoutes"), s.GetProperty("workRemaining").GetBoolean()), candidates,
            A(p, "rejections", 64).Select(r => new GearRoutingRejection(S(r, "code"), I(r, "count"))), A(p, "details", GearRoutingContract.MaxDetails).Select(Diagnostic), I(p, "omittedDetails"));
        Check(bytes.SequenceEqual(WriteResult(g)), "routing-result/context-noncanonical"); return g;
    }
    public static void ValidateCached(GearRoutingResult g)
    {
        var n = AnchoredGearRouter.Normalize(g.Normalized.Request); Check(n.IsValid && n.RequestId == g.RequestId, "routing-result-request"); var r = n.Request!; var s = g.Summary;
        Check(g.Status == GearRoutingStatus.Complete || g.Status == GearRoutingStatus.Infeasible || g.Status == GearRoutingStatus.IncompleteBudget || g.Status == GearRoutingStatus.Cancelled, "routing-result-status");
        Check(s.PotentialNodes == n.PotentialNodes && s.NodeChecks >= 0 && s.NodeChecks <= s.PotentialNodes && s.StaticRejectedNodes >= 0 && s.StaticRejectedNodes <= s.NodeChecks && s.AdjacencyChecks >= 0 && s.AdjacencyChecks <= s.PotentialNodes * (s.PotentialNodes - 1) / 2 && s.Edges >= 0 && s.Edges <= s.AdjacencyChecks, "routing-preparation-counts");
        Check(s.Expansions >= 0 && s.Expansions <= r.ExpansionBudget && s.RejectedSuccessors >= 0 && s.RejectedSuccessors <= s.Expansions && s.GoalChecks >= 0 && s.GoalChecks <= s.Expansions && s.ValidRoutes >= 0 && s.ValidRoutes <= s.GoalChecks, "routing-search-counts");
        Check(g.Candidates.Count == Math.Min(s.ValidRoutes, r.MaximumReturned) && g.Candidates.Select(c => c.CandidateId).Distinct(StringComparer.Ordinal).Count() == g.Candidates.Count, "routing-candidate-count/duplicate");
        Check(g.SearchComplete != s.WorkRemaining && (g.Status != GearRoutingStatus.Complete || s.ValidRoutes > 0) && (g.Status != GearRoutingStatus.Infeasible || s.ValidRoutes == 0) && (g.Status != GearRoutingStatus.IncompleteBudget || s.Expansions == r.ExpansionBudget), "routing-status-completeness");
        Check(g.OmittedDetails >= 0 && (g.OmittedDetails == 0 || g.Details.Count == GearRoutingContract.MaxDetails) && g.Rejections.Count <= 64 && g.Rejections.All(x => x.Count > 0) && g.Rejections.Select(x => x.Code).Distinct().Count() == g.Rejections.Count, "routing-rejection-counts");
        for (int i = 0; i < g.Candidates.Count; i++)
        {
            var c = g.Candidates[i]; Check(c.CandidateId == Mechanisms.ComputeCandidateId(c.Mechanism), "routing-mechanical-identity");
            var v = GearRouteValidator.Validate(r, c.Path, c.Mechanism, c.CandidateId); Check(v.IsValid, "routing-context:" + string.Join("|", v.Diagnostics.Select(d => d.Code)));
            Check(c.Validation.IsValid && c.Validation.RequestId == v.RequestId && c.Validation.ContextId == v.ContextId && c.Validation.CandidateId == v.CandidateId && c.Validation.ActualTransfer == v.ActualTransfer, "routing-attached-context");
            var m = GearRouteValidator.Metrics(r, c.Path); Check(m.Idlers == c.Metrics.Idlers && m.PreferredPenalty == c.Metrics.PreferredPenalty && m.Footprint == c.Metrics.Footprint && m.TotalTeeth == c.Metrics.TotalTeeth && m.Signature == c.Metrics.Signature, "routing-metrics");
            Check(i == 0 || GearRouteValidator.Compare(g.Candidates[i - 1], c) < 0, "routing-ranking");
        }
    }
    private static void CheckArtifactBounds(JsonElement a)
    {
        var k = a.GetProperty("kinematic"); A(k, "dofs", 7); A(k, "couplings", 6); A(k, "solution", 7);
        var s = a.GetProperty("spatial"); A(s, "axes", 7); A(s, "bodies", 7); A(s, "contacts", 6);
        var p = a.GetProperty("resolvedPlayback"); A(p, "drivers", 1); A(p, "channels", 7); A(p, "bodyBindings", 7);
    }
    private static void Certificate(Utf8JsonWriter w, GearRouteValidation v)
    {
        w.WriteStartObject("validation"); w.WriteString("requestId", v.RequestId); w.WriteString("candidateId", v.CandidateId); w.WriteString("contextId", v.ContextId);
        w.WriteBoolean("genericValid", v.GenericValidation.IsValid); w.WriteBoolean("routingContextValid", v.IsValid); w.WritePropertyName("actualTransfer"); R(w, v.ActualTransfer!.Value);
        w.WriteNumber("unrelatedPairChecks", v.Spatial.UnrelatedSameLayerPairChecks);
        Array(w, "contacts", v.Spatial.ContactReadbacks, (a, c) => { a.WriteStartObject(); a.WriteString("id", c.ContactId); Number(a, "actualSquared", c.ActualCenterDistanceSquared); Number(a, "expectedSquared", c.ExpectedCenterDistanceSquared); Number(a, "residualSquared", c.ResidualSquared); a.WriteEndObject(); });
        w.WriteEndObject();
    }
    internal static byte[] Bytes(JsonElement e) => Encoding.UTF8.GetBytes(e.GetRawText());
    internal static void Embed(Utf8JsonWriter w, string key, byte[] bytes) { using var d = JsonDocument.Parse(bytes); w.WritePropertyName(key); d.RootElement.WriteTo(w); }
    internal static void Number(Utf8JsonWriter w, string key, BigInteger n) => w.WriteString(key, GearRoutingContract.Number(n));
    internal static void Point(Utf8JsonWriter w, GearRoutePoint p) { w.WriteStartObject(); Number(w, "x", p.X); Number(w, "y", p.Y); w.WriteEndObject(); }
    internal static GearRoutePoint Point(JsonElement e) => new GearRoutePoint(N(e.GetProperty("x")), N(e.GetProperty("y")));
    internal static void Box(Utf8JsonWriter w, GearRouteBox b) { w.WriteStartObject(); Number(w, "minX", b.MinX); Number(w, "minY", b.MinY); Number(w, "maxX", b.MaxX); Number(w, "maxY", b.MaxY); w.WriteEndObject(); }
    internal static GearRouteBox Box(JsonElement e) => new GearRouteBox(N(e.GetProperty("minX")), N(e.GetProperty("minY")), N(e.GetProperty("maxX")), N(e.GetProperty("maxY")));
    private static void Anchor(Utf8JsonWriter w, string key, GearRouteAnchor a) { w.WriteStartObject(key); w.WritePropertyName("position"); Point(w, a.Position); w.WriteNumber("teeth", a.Teeth); w.WriteEndObject(); }
    private static GearRouteAnchor Anchor(JsonElement e) => new GearRouteAnchor(Point(e.GetProperty("position")), I(e, "teeth"));
    private static void Region(Utf8JsonWriter w, GearRouteRegion r) { w.WriteStartObject(); w.WriteString("id", r.Id); w.WritePropertyName("bounds"); Box(w, r.Bounds); w.WriteEndObject(); }
    private static GearRouteRegion Region(JsonElement e) => new GearRouteRegion(S(e, "id"), Box(e.GetProperty("bounds")));
    private static void Diagnostic(Utf8JsonWriter w, Diagnostic d) { w.WriteStartObject(); w.WriteString("code", d.Code); w.WriteString("severity", d.Severity.ToString()); w.WriteString("message", d.Message); w.WriteString("subject", d.SubjectId); w.WriteEndObject(); }
    private static Diagnostic Diagnostic(JsonElement e) => new Diagnostic(S(e, "code"), E<DiagnosticSeverity>(e, "severity"), S(e, "message"), e.GetProperty("subject").GetString());
}
