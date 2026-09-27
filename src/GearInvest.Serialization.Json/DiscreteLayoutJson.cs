using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.DiscreteEmbodimentJson;

namespace GearInvest.Serialization;

/// <summary>Normalized explicit-domain requests and cached generation records. Reading revalidates returned
/// assignments/geometry but does not search missing assignments; fresh Generate is a separate operation.</summary>
public static class DiscreteLayoutJson
{
    public const int MaximumGenerationBytes = 32 * 1024 * 1024;
    public static byte[] WriteRequest(DiscreteLayoutRequest input)
    {
        var n = new ParametricDiscreteLayoutGenerator().Normalize(input); Check(n.IsValid, string.Join(";", n.Diagnostics.Select(d => d.Code + ":" + d.Detail)));
        return Envelope(DiscreteLayoutContract.RequestFormat, w => Request(w, n));
    }
    public static DiscreteLayoutRequest ReadRequest(byte[] bytes)
    {
        using var d = Open(bytes, DiscreteLayoutContract.RequestFormat); var p = d.RootElement.GetProperty("payload");
        var s = p.GetProperty("source");
        var source = new DiscreteLayoutSource(S(s, "modelId"), S(s, "mechanismId"), S(s, "driverId"), S(s, "eventDofId"), S(s, "eventKey"), I(s, "maxEvents"));
        var r = new DiscreteLayoutRequest(source, Point(p.GetProperty("anchor")), offsets: A(p, "offsets", 256).Select(Point),
            primaryRadii: A(p, "primaryRadii", 16).Select(R), secondaryRadii: A(p, "secondaryRadii", 16).Select(R),
            primaryProbes: A(p, "primaryProbes", 16).Select(Probe), secondaryProbes: A(p, "secondaryProbes", 16).Select(Probe),
            frameQuarter: I(p, "frameQuarter"), keepOuts: A(p, "keepOuts", 32).Select(k => new GeometryKeepOut(S(k, "id"), Box(k.GetProperty("bounds")))),
            requiredRegion: p.GetProperty("requiredRegion").ValueKind == JsonValueKind.Null ? null : Box(p.GetProperty("requiredRegion")),
            preferredOffset: p.GetProperty("preferredOffset").ValueKind == JsonValueKind.Null ? null : Point(p.GetProperty("preferredOffset")),
            minimumClearance: R(p.GetProperty("minimumClearance")), expansionBudget: I(p, "expansionBudget"), candidateCap: I(p, "candidateCap"),
            profile: S(p, "profile"), backend: S(p, "backend"), ranking: S(p, "ranking"), unit: S(p, "unit"));
        Check(bytes.SequenceEqual(WriteRequest(r)), "request-identity/noncanonical-request"); return r;
    }
    private static void Request(Utf8JsonWriter w, DiscreteLayoutNormalization n)
    {
        var r = n.Request!; w.WriteStartObject(); w.WriteString("requestId", n.RequestId); w.WriteString("domainSize", n.DomainSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteString("profile", r.Profile); w.WriteString("backend", r.Backend); w.WriteString("ranking", r.Ranking); w.WriteString("unit", r.Unit);
        w.WriteStartObject("source"); w.WriteString("modelId", r.Source.ModelId); w.WriteString("mechanismId", r.Source.MechanismId); w.WriteString("driverId", r.Source.DriverId);
        w.WriteString("eventDofId", r.Source.EventDofId); w.WriteString("eventKey", r.Source.EventKey); w.WriteNumber("maxEvents", r.Source.MaxEvents); w.WriteEndObject();
        Point(w, "anchor", r.Anchor); w.WriteNumber("frameQuarter", r.FrameQuarter);
        Array(w, "offsets", r.Offsets, Point); Array(w, "primaryRadii", r.PrimaryRadii, R); Array(w, "secondaryRadii", r.SecondaryRadii, R);
        Array(w, "primaryProbes", r.PrimaryProbes, Probe); Array(w, "secondaryProbes", r.SecondaryProbes, Probe);
        Array(w, "keepOuts", r.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); a.WritePropertyName("bounds"); Box(a, k.Bounds); a.WriteEndObject(); });
        w.WritePropertyName("requiredRegion"); if (r.RequiredRegion == null) w.WriteNullValue(); else Box(w, r.RequiredRegion);
        w.WritePropertyName("preferredOffset"); if (r.PreferredOffset.HasValue) Point(w, r.PreferredOffset.Value); else w.WriteNullValue();
        Rat(w, "minimumClearance", r.MinimumClearance); w.WriteNumber("expansionBudget", r.ExpansionBudget); w.WriteNumber("candidateCap", r.CandidateCap); w.WriteEndObject();
    }

    public static byte[] WriteGeneration(DiscreteEmbodimentModel model, DiscreteLayoutGeneration g)
    {
        ValidateCachedGeneration(model, g);
        var bytes = Envelope(DiscreteLayoutContract.GenerationFormat, w =>
        {
            w.WriteStartObject(); w.WriteString("requestId", g.RequestId); Embed(w, "request", WriteRequest(g.Normalized.Request!));
            w.WriteString("status", g.Status.ToString()); w.WriteBoolean("searchComplete", g.SearchComplete); w.WriteBoolean("resultTruncated", g.ResultTruncated);
            w.WriteNumber("evaluated", g.Evaluated); w.WriteNumber("valid", g.Valid); w.WriteNumber("rejected", g.Rejected); w.WriteNumber("inconclusive", g.Inconclusive);
            w.WriteNumber("deduplicated", g.Deduplicated); w.WriteNumber("omittedDiagnostics", g.OmittedDiagnostics);
            Array(w, "candidates", g.Candidates, (a, c) =>
            {
                a.WriteStartObject(); a.WriteString("assignmentKey", c.Assignment.Key); a.WritePropertyName("assignment"); Assignment(a, c.Assignment);
                Rat(a, "preferredPenalty", c.PreferredPenalty); Rat(a, "footprint", c.Footprint); Rat(a, "travelSize", c.TravelSize);
                Embed(a, "geometry", DiscreteGeometryJson.WriteGeometry(model, c.Geometry)); a.WriteEndObject();
            });
            Array(w, "diagnostics", g.Diagnostics, Diagnostic); w.WriteEndObject();
        });
        Check(bytes.Length <= MaximumGenerationBytes, "generation-byte-limit"); return bytes;
    }
    public static DiscreteLayoutGeneration ReadGeneration(DiscreteEmbodimentModel model, byte[] bytes)
    {
        using var d = Open(bytes, DiscreteLayoutContract.GenerationFormat, MaximumGenerationBytes); var p = d.RootElement.GetProperty("payload");
        var request = ReadRequest(Raw(p.GetProperty("request"))); var n = new ParametricDiscreteLayoutGenerator().Normalize(request);
        var candidates = A(p, "candidates", 128).Select(c =>
        {
            var g = DiscreteGeometryJson.ReadGeometry(model, Raw(c.GetProperty("geometry"))); var assignment = Assignment(c.GetProperty("assignment"));
            Check(assignment.Key == S(c, "assignmentKey"), "assignment-key");
            return new DiscreteLayoutCandidate(g, assignment, R(c.GetProperty("preferredPenalty")), R(c.GetProperty("footprint")), R(c.GetProperty("travelSize")), new DiscreteGeometryGenerator().Validate(model, g));
        });
        var result = new DiscreteLayoutGeneration(n, E<DiscreteLayoutStatus>(p, "status"), p.GetProperty("searchComplete").GetBoolean(), I(p, "evaluated"), I(p, "valid"), I(p, "rejected"),
            I(p, "inconclusive"), I(p, "deduplicated"), candidates, A(p, "diagnostics", 64).Select(Diagnostic), I(p, "omittedDiagnostics"));
        Check(bytes.SequenceEqual(WriteGeneration(model, result)), "noncanonical-generation-or-claimed-summary"); return result;
    }
    private static void ValidateCachedGeneration(DiscreteEmbodimentModel model, DiscreteLayoutGeneration g)
    {
        Check(g.Normalized.IsValid && g.Normalized.Request!.Source.ModelId == model.ModelId, "generation-source/request");
        var r = g.Normalized.Request!; var normalized = new ParametricDiscreteLayoutGenerator().Normalize(r);
        Check(normalized.IsValid && normalized.RequestId == g.RequestId && normalized.DomainSize == g.Normalized.DomainSize, "generation-normalization");
        Check(g.Status == DiscreteLayoutStatus.Complete || g.Status == DiscreteLayoutStatus.Infeasible || g.Status == DiscreteLayoutStatus.IncompleteBudget, "only-deterministic-supported-results-persisted");
        Check(g.Evaluated >= 0 && g.Evaluated <= r.ExpansionBudget && g.Evaluated <= normalized.DomainSize && g.Valid >= 0 && g.Rejected >= 0 && g.Valid + g.Rejected == g.Evaluated &&
            g.Inconclusive >= 0 && g.Inconclusive <= g.Rejected && g.Deduplicated >= 0 && g.Deduplicated <= g.Valid && g.OmittedDiagnostics >= 0, "generation-counts");
        Check(g.SearchComplete == (g.Evaluated == normalized.DomainSize) && (g.Status == DiscreteLayoutStatus.IncompleteBudget ? !g.SearchComplete && g.Evaluated == r.ExpansionBudget : g.SearchComplete) &&
            (g.Status != DiscreteLayoutStatus.Complete || g.Valid > 0) && (g.Status != DiscreteLayoutStatus.Infeasible || g.Valid == 0), "generation-completeness");
        Check(g.Candidates.Count == Math.Min(r.CandidateCap, g.Valid - g.Deduplicated) && g.Candidates.Select(c => c.Geometry.GeometryId).Distinct(StringComparer.Ordinal).Count() == g.Candidates.Count, "generation-returned-count");
        Check(ParametricDiscreteLayoutGenerator.Rank(g.Candidates).Select(c => c.Canonical).SequenceEqual(g.Candidates.Select(c => c.Canonical)), "generation-rank");
        var generator = new ParametricDiscreteLayoutGenerator();
        var prefix = new System.Collections.Generic.HashSet<string>(generator.Assignments(r).Take(g.Evaluated).Select(a => a.Key), StringComparer.Ordinal);
        foreach (var c in g.Candidates)
        {
            Check(prefix.Contains(c.Assignment.Key), "candidate-outside-evaluated-prefix");
            var derived = generator.RealizeAssignment(model, r, c.Assignment);
            Check(derived.Validation.IsValid && c.Validation.IsValid && derived.Canonical == c.Canonical && derived.Geometry.GeometryCanonical == c.Geometry.GeometryCanonical && derived.Geometry.RealizationBindingId == c.Geometry.RealizationBindingId, "candidate-does-not-realize-request");
        }
        // These checks certify cached members, not exhaustive coverage. Regenerate is needed to verify the saved full search claim.
    }
    internal static void Embed(Utf8JsonWriter w, string key, byte[] bytes) { w.WritePropertyName(key); using var d = JsonDocument.Parse(bytes); d.RootElement.WriteTo(w); }
    internal static byte[] Raw(JsonElement value) => Encoding.UTF8.GetBytes(value.GetRawText());
    internal static void Rat(Utf8JsonWriter w, string key, Rational value) { w.WritePropertyName(key); R(w, value); }
    internal static void Point(Utf8JsonWriter w, string key, GeometryPoint point) { w.WritePropertyName(key); Point(w, point); }
    internal static void Point(Utf8JsonWriter w, GeometryPoint p) { w.WriteStartObject(); Rat(w, "x", p.X); Rat(w, "y", p.Y); Rat(w, "z", p.Z); w.WriteEndObject(); }
    internal static GeometryPoint Point(JsonElement p) => new GeometryPoint(R(p.GetProperty("x")), R(p.GetProperty("y")), R(p.GetProperty("z")));
    internal static void Box(Utf8JsonWriter w, GeometryBox b) { w.WriteStartObject(); Point(w, "min", b.Min); Point(w, "max", b.Max); w.WriteEndObject(); }
    internal static GeometryBox Box(JsonElement p) => new GeometryBox(Point(p.GetProperty("min")), Point(p.GetProperty("max")));
    internal static void Probe(Utf8JsonWriter w, LayoutProbeConfiguration p) { w.WriteStartObject(); w.WriteNumber("bearingQuarter", p.BearingQuarter); Rat(w, "radialFraction", p.RadialFraction); Rat(w, "travel", p.Travel); Rat(w, "retract", p.Retract); w.WriteEndObject(); }
    internal static LayoutProbeConfiguration Probe(JsonElement p) => new LayoutProbeConfiguration(I(p, "bearingQuarter"), R(p.GetProperty("radialFraction")), R(p.GetProperty("travel")), R(p.GetProperty("retract")));
    internal static void Assignment(Utf8JsonWriter w, DiscreteLayoutAssignment a) { w.WriteStartObject(); Point(w, "offset", a.Offset); Rat(w, "primaryRadius", a.PrimaryRadius); Rat(w, "secondaryRadius", a.SecondaryRadius); w.WritePropertyName("primaryProbe"); Probe(w, a.PrimaryProbe); w.WritePropertyName("secondaryProbe"); Probe(w, a.SecondaryProbe); w.WriteEndObject(); }
    internal static DiscreteLayoutAssignment Assignment(JsonElement a) => new DiscreteLayoutAssignment(Point(a.GetProperty("offset")), R(a.GetProperty("primaryRadius")), R(a.GetProperty("secondaryRadius")), Probe(a.GetProperty("primaryProbe")), Probe(a.GetProperty("secondaryProbe")));
    private static void Diagnostic(Utf8JsonWriter w, LayoutDiagnostic d) { w.WriteStartObject(); w.WriteString("code", d.Code); w.WriteString("field", d.Field); w.WriteString("assignment", d.Assignment); w.WriteString("domain", d.Domain); w.WriteString("item", d.Item); w.WriteString("detail", d.Detail); w.WriteEndObject(); }
    private static LayoutDiagnostic Diagnostic(JsonElement d) => new LayoutDiagnostic(S(d, "code"), S(d, "field"), S(d, "assignment"), S(d, "domain"), S(d, "item"), S(d, "detail"));
    internal static void Evaluation(Utf8JsonWriter w, DiscreteLayoutEvaluation? e)
    {
        if (e == null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteNumber("primaryIndex", e.PrimaryIndex); w.WriteNumber("secondaryIndex", e.SecondaryIndex); Rat(w, "fromRoot", e.FromRoot); Rat(w, "toRoot", e.ToRoot);
        w.WriteString("cursor", e.Cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)); w.WriteNumber("maxOccurrences", e.MaxOccurrences);
        w.WritePropertyName("primaryTurns"); if (e.PrimaryTurns.HasValue) R(w, e.PrimaryTurns.Value); else w.WriteNullValue();
        w.WritePropertyName("secondaryTurns"); if (e.SecondaryTurns.HasValue) R(w, e.SecondaryTurns.Value); else w.WriteNullValue(); w.WriteEndObject();
    }
    internal static DiscreteLayoutEvaluation? Evaluation(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : new DiscreteLayoutEvaluation(I(e, "primaryIndex"), I(e, "secondaryIndex"), R(e.GetProperty("fromRoot")), R(e.GetProperty("toRoot")), N(e.GetProperty("cursor")), I(e, "maxOccurrences"),
        e.GetProperty("primaryTurns").ValueKind == JsonValueKind.Null ? null : R(e.GetProperty("primaryTurns")), e.GetProperty("secondaryTurns").ValueKind == JsonValueKind.Null ? null : R(e.GetProperty("secondaryTurns")));
}
