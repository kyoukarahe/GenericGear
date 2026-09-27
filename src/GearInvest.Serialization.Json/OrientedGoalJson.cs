using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit bounded AOT codec. Goal/result sidecars never extend or masquerade as a mechanical artifact.</summary>
public static class OrientedGoalJson
{
    public const string GoalFormat = "gear-invest.oriented-two-output-goal";
    public const string PlanFormat = "gear-invest.oriented-two-output-search-plan";
    public const string ResultFormat = "gear-invest.oriented-two-output-search-result";
    public const string ProjectFormat = "gear-invest.oriented-two-output-goal-project";

    public static byte[] WriteGoal(OrientedGoalPlan plan) => WriteGoal(plan.Goal, plan.GoalId);
    public static byte[] WriteGoal(OrientedTwoOutputGoal g, string goalId) => Document(w =>
    {
        Start(w, GoalFormat, g.Profile); w.WriteString("goalId", goalId); w.WriteString("profile", g.Profile);
        Frame(w, "rootShaftFrame", g.RootShaftFrame); Frame(w, "bevelOutputShaftFrame", g.BevelOutputShaftFrame); Vector(w, "unattachedTurnedMatingStation", g.UnattachedTurnedMatingStation);
        Vector(w, "apex", g.Apex); Vector(w, "inputConeDirection", g.InputConeDirection); Vector(w, "outputConeDirection", g.OutputConeDirection); Fraction(w, "innerParameter", g.InnerParameter);
        Array(w, "outputs", g.Outputs, (a, o) => { a.WriteStartObject(); a.WriteString("key", o.Key); a.WriteString("role", o.Role.ToString()); Frame(a, "terminalFrame", o.TerminalFrame);
            Fraction(a, "requiredTransfer", o.RequiredTransfer); if (o.FixedBodyCenter.HasValue) Vector(a, "fixedBodyCenter", o.FixedBodyCenter.Value); else a.WriteNull("fixedBodyCenter");
            if (o.FixedBodyTeeth.HasValue) Integer(a, "fixedBodyTeeth", o.FixedBodyTeeth.Value); else a.WriteNull("fixedBodyTeeth"); a.WriteEndObject(); });
        Array(w, "inputTeeth", g.InputTeeth, (a, v) => a.WriteStringValue(OrientedGoalKeys.N(v)));
        Array(w, "outputTeeth", g.OutputTeeth, (a, v) => a.WriteStringValue(OrientedGoalKeys.N(v)));
        Array(w, "pitchScales", g.PitchScales, (a, v) => Fraction(a, v));
        Array(w, "parallelSources", g.ParallelSources, Source); w.WriteString("turnedPolicy", g.TurnedPolicy.ToString()); Array(w, "turnedSources", g.TurnedSources, Source);
        w.WriteBoolean("requireCrossComponentClearance", g.RequireCrossComponentClearance); Array(w, "keepOuts", g.KeepOuts, KeepOut);
        w.WriteStartObject("limits"); Integer(w, "maxBodies", g.Limits.MaxBodies); Integer(w, "maxShafts", g.Limits.MaxShafts); Integer(w, "maxTotalTeeth", g.Limits.MaxTotalTeeth);
        w.WritePropertyName("wholeBounds"); if (g.Limits.WholeBounds.HasValue) Envelope(w, g.Limits.WholeBounds.Value); else w.WriteNullValue(); w.WriteEndObject();
        w.WriteStartObject("options"); var o = g.Options; Integer(w, "workBudget", o.WorkBudget); Integer(w, "returnedCandidateCap", o.ReturnedCandidateCap);
        Integer(w, "maxUniqueCandidates", o.MaxUniqueCandidates); Integer(w, "maxRetainedBytes", o.MaxRetainedBytes); Integer(w, "maxOrigins", o.MaxOrigins); Integer(w, "maxDetails", o.MaxDetails);
        w.WriteString("resourceProfile", o.ResourceProfile); w.WriteEndObject(); w.WriteEndObject();
    });

    public static (string GoalId, OrientedTwoOutputGoal Goal) ReadGoal(byte[] bytes) => CheckedRead(() =>
    {
        using var doc = OpenDocument(bytes); var p = doc.RootElement; CheckStart(p, GoalFormat);
        var policy = E<OrientedTurnedPolicy>(p, "turnedPolicy"); var l = p.GetProperty("limits"); var o = p.GetProperty("options");
        var goal = new OrientedTwoOutputGoal(Frame(p.GetProperty("rootShaftFrame")), Frame(p.GetProperty("bevelOutputShaftFrame")), Vector(p.GetProperty("apex")),
            Vector(p.GetProperty("inputConeDirection")), Vector(p.GetProperty("outputConeDirection")), F(p.GetProperty("innerParameter")),
            Items(p, "outputs", 2).Select(a => new OrientedGoalOutput(S(a, "key"), E<OrientedOutputRole>(a, "role"), Frame(a.GetProperty("terminalFrame")), F(a.GetProperty("requiredTransfer")),
                a.GetProperty("fixedBodyCenter").ValueKind == JsonValueKind.Null ? null : Vector(a.GetProperty("fixedBodyCenter")), a.GetProperty("fixedBodyTeeth").ValueKind == JsonValueKind.Null ? null : I(a, "fixedBodyTeeth"))),
            Items(p, "inputTeeth", OrientedGoalProfile.MaxToothChoices).Select(Number), Items(p, "outputTeeth", OrientedGoalProfile.MaxToothChoices).Select(Number), Items(p, "pitchScales", OrientedGoalProfile.MaxScaleChoices).Select(F),
            Items(p, "parallelSources", OrientedGoalProfile.MaxSourcesPerBranch).Select(Source), policy,
            policy == OrientedTurnedPolicy.NoneOnly ? System.Array.Empty<OrientedGoalSource>() : Items(p, "turnedSources", OrientedGoalProfile.MaxSourcesPerBranch).Select(Source),
            p.GetProperty("requireCrossComponentClearance").GetBoolean(), Items(p, "keepOuts", 16).Select(KeepOut),
            new OrientedGoalLimits(I(l, "maxBodies"), I(l, "maxShafts"), I(l, "maxTotalTeeth"), l.GetProperty("wholeBounds").ValueKind == JsonValueKind.Null ? null : Envelope(l.GetProperty("wholeBounds"))),
            new OrientedGoalSearchOptions(I(o, "workBudget"), I(o, "returnedCandidateCap"), I(o, "maxUniqueCandidates"), I(o, "maxRetainedBytes"), I(o, "maxOrigins"), I(o, "maxDetails"), S(o, "resourceProfile")), S(p, "profile"), Vector(p.GetProperty("unattachedTurnedMatingStation")));
        var id = S(p, "goalId"); Require(bytes.SequenceEqual(WriteGoal(goal, id)), "Noncanonical/unknown goal fields."); return (id, goal);
    });

    public static byte[] WritePlan(OrientedGoalPlan plan) => Document(w =>
    {
        Start(w, PlanFormat, plan.Goal.Profile); w.WriteString("goalId", plan.GoalId); w.WriteString("theoreticalTuples", plan.TupleCount.ToString(CultureInfo.InvariantCulture));
        w.WriteString("dimensionOrder", "inputTeeth/outputTeeth/pitchScale/parallelSource/parallelPlacement/turnedChoice/turnedPlacement");
        Array(w, "inputTeeth", plan.Goal.InputTeeth, (a, n) => a.WriteStringValue(OrientedGoalKeys.N(n)));
        Array(w, "outputTeeth", plan.Goal.OutputTeeth, (a, n) => a.WriteStringValue(OrientedGoalKeys.N(n))); Array(w, "pitchScales", plan.Goal.PitchScales, (a, n) => Fraction(a, n));
        w.WriteString("turnedPolicy", plan.Goal.TurnedPolicy.ToString());
        void Summary(Utf8JsonWriter a, OrientedGoalResolvedSource s) { a.WriteStartObject(); a.WriteString("sourceId", s.Id); a.WriteString("candidateId", s.Definition.CandidateId);
            a.WriteString("artifactHash", s.Definition.ArtifactHash); a.WriteString("rawSha256", Hash(s.Definition.SourceBytes)); Integer(a, "sourceBytes", s.Definition.ByteLength);
            a.WriteString("inputDofId", s.Definition.InputDofId); a.WriteString("outputDofId", s.Definition.OutputDofId); Array(a, "placements", s.Definition.Placements, Placement); a.WriteEndObject(); }
        Array(w, "parallelSources", plan.ParallelSources, Summary); Array(w, "turnedSources", plan.TurnedSources, Summary);
        Integer(w, "maximumDomain", OrientedGoalProfile.MaxDomain); Integer(w, "maximumSourceBytes", OrientedGoalProfile.MaxSourceBytes);
        Integer(w, "maximumTotalSourceBytes", OrientedGoalProfile.MaxTotalSourceBytes); Integer(w, "maximumDocumentBytes", OrientedGoalProfile.MaxDocumentBytes); Integer(w, "maximumInputDigits", OrientedGoalProfile.MaxInputDigits); w.WriteEndObject();
    });

    public static byte[] WriteResult(OrientedGoalGeneration r) => Document(w =>
    {
        Start(w, ResultFormat, r.Profile); w.WriteString("goalId", r.GoalId); w.WriteString("status", r.Status.ToString());
        if (r.Profile == OrientedGoalProfile.RefinedId) Integer(w, "pairPredicateCount", r.PairPredicateCount);
        w.WriteString("theoreticalTuples", r.TheoreticalTuples.ToString(CultureInfo.InvariantCulture)); Integer(w, "processedTuples", r.ProcessedTuples);
        w.WriteString("remainingTuples", r.RemainingTuples.ToString(CultureInfo.InvariantCulture)); Integer(w, "acceptedTuples", r.AcceptedTuples); Integer(w, "rejectedTuples", r.RejectedTuples);
        Integer(w, "inconclusiveTuples", r.InconclusiveTuples); Integer(w, "unretainedTuples", r.UnretainedTuples); Integer(w, "uniqueAcceptedCount", r.UniqueAcceptedCount);
        Integer(w, "retainedUniqueCount", r.RetainedUniqueCount); Integer(w, "retainedOriginCount", r.RetainedOriginCount);
        w.WriteBoolean("hasValidatedCandidates", r.HasValidatedCandidates); w.WriteBoolean("enumerationComplete", r.EnumerationComplete); w.WriteBoolean("resultTruncated", r.ResultTruncated);
        w.WriteString("rankingGuarantee", r.RankingGuarantee.ToString()); w.WriteString("limitingReason", r.LimitingReason); w.WriteString("transcriptDigest", r.TranscriptDigest);
        Array(w, "candidates", r.Candidates, (a, c) => {
            a.WriteStartObject(); a.WriteString("candidateId", c.CandidateId); a.WriteString("artifactHash", c.Artifact.ArtifactHash); a.WriteBase64String("mechanismArtifactUtf8", c.Artifact.Bytes);
            a.WriteString("representativeOriginId", c.RepresentativeOriginId); a.WritePropertyName("metrics"); Metrics(a, c.Metrics);
            Array(a, "origins", c.Origins, (z, origin) => { z.WriteStartObject(); z.WriteString("originId", origin.Id); z.WritePropertyName("tuple"); Tuple(z, origin.Tuple);
                z.WriteString("artifactHash", origin.ArtifactHash); Fraction(z, "preferencePenalty", origin.PreferencePenalty); z.WriteEndObject(); }); a.WriteEndObject(); });
        Array(w, "details", r.Details, (a, d) => { a.WriteStartObject(); a.WritePropertyName("tuple"); Tuple(a, d.Tuple); a.WriteString("verdict", d.Verdict.ToString()); a.WriteString("reason", d.Reason);
            if (d.CandidateId is null) a.WriteNull("candidateId"); else a.WriteString("candidateId", d.CandidateId);
            if (r.Profile == OrientedGoalProfile.RefinedId) { Integer(a, "pairPredicateCount", d.PairPredicateCount); a.WriteString("clearanceDigest", d.ClearanceDigest); }
            else Require(d.PairPredicateCount == 0 && d.ClearanceDigest is null && r.PairPredicateCount == 0, "Legacy result cannot carry refined observations.");
            a.WriteEndObject(); }); w.WriteEndObject();
    });

    public static OrientedGoalGeneration ReadResult(byte[] bytes) => CheckedRead(() =>
    {
        using var doc = OpenDocument(bytes); var p = doc.RootElement; var profile = CheckStart(p, ResultFormat); var refined = profile == OrientedGoalProfile.RefinedId;
        var candidates = Items(p, "candidates", OrientedGoalProfile.MaxUniqueCandidates).Select(c => {
            var raw = c.GetProperty("mechanismArtifactUtf8").GetBytesFromBase64(); var artifact = CanonicalOrientedTwoOutputJson.Read(raw);
            Require(artifact.CandidateId == S(c, "candidateId") && artifact.ArtifactHash == S(c, "artifactHash"), "Result artifact identity binding mismatch.");
            return new OrientedGoalCandidate(new OrientedGoalArtifact(artifact.CandidateId, artifact.ArtifactHash, raw, CanonicalOrientedTwoOutputJson.WriteMechanicalPayload(artifact.Mechanism)), Metrics(c.GetProperty("metrics")),
                Items(c, "origins", OrientedGoalProfile.MaxOrigins).Select(o => new OrientedGoalOrigin(S(o, "originId"), Tuple(o.GetProperty("tuple")), S(o, "artifactHash"), F(o.GetProperty("preferencePenalty")))), S(c, "representativeOriginId"));
        }).ToArray();
        var r = new OrientedGoalGeneration(S(p, "goalId"), E<OrientedGoalStatus>(p, "status"), Big(p, "theoreticalTuples"), I(p, "processedTuples"), I(p, "acceptedTuples"), I(p, "rejectedTuples"),
            I(p, "inconclusiveTuples"), I(p, "unretainedTuples"), I(p, "uniqueAcceptedCount"), I(p, "retainedUniqueCount"), I(p, "retainedOriginCount"), p.GetProperty("enumerationComplete").GetBoolean(), p.GetProperty("resultTruncated").GetBoolean(),
            E<OrientedRankingGuarantee>(p, "rankingGuarantee"), S(p, "limitingReason"), S(p, "transcriptDigest"), candidates,
            Items(p, "details", OrientedGoalProfile.MaxDetails).Select(d => new OrientedGoalTupleDetail(Tuple(d.GetProperty("tuple")), E<OrientedTupleVerdict>(d, "verdict"), S(d, "reason"), d.GetProperty("candidateId").ValueKind == JsonValueKind.Null ? null : S(d, "candidateId"),
                refined ? I(d, "pairPredicateCount") : 0, refined ? S(d, "clearanceDigest") : null)), profile, refined ? I(p, "pairPredicateCount") : 0);
        Require(r.RemainingTuples == Big(p, "remainingTuples") && r.HasValidatedCandidates == p.GetProperty("hasValidatedCandidates").GetBoolean(), "Inconsistent derived result fields.");
        Require(bytes.SequenceEqual(WriteResult(r)), "Noncanonical/unknown result fields."); return r;
    });

    private static void Source(Utf8JsonWriter w, OrientedGoalSource s)
    { w.WriteStartObject(); w.WriteString("sourceId", OrientedGoalKeys.Source(s)); w.WriteString("candidateId", s.CandidateId); w.WriteString("artifactHash", s.ArtifactHash);
      w.WriteBase64String("sourceArtifactUtf8", s.SourceBytes); w.WriteString("inputDofId", s.InputDofId); w.WriteString("outputDofId", s.OutputDofId); Array(w, "placements", s.Placements, Placement); w.WriteEndObject(); }
    private static OrientedGoalSource Source(JsonElement p)
    { var s = new OrientedGoalSource(p.GetProperty("sourceArtifactUtf8").GetBytesFromBase64(), S(p, "candidateId"), S(p, "artifactHash"), S(p, "inputDofId"), S(p, "outputDofId"), Items(p, "placements", OrientedGoalProfile.MaxPlacements).Select(Placement));
      Require(OrientedGoalKeys.Source(s) == S(p, "sourceId"), "Source definition integrity mismatch."); return s; }
    private static void Placement(Utf8JsonWriter w, OrientedGoalPlacement p) { w.WriteStartObject(); Frame(w, "pose", p.Pose); Frame(w, "inputMatingFrame", p.InputMatingFrame); Fraction(w, "preferencePenalty", p.PreferencePenalty); w.WriteEndObject(); }
    private static OrientedGoalPlacement Placement(JsonElement p) => new(Frame(p.GetProperty("pose")), Frame(p.GetProperty("inputMatingFrame")), F(p.GetProperty("preferencePenalty")));
    private static void Tuple(Utf8JsonWriter w, OrientedGoalTuple t)
    { w.WriteStartObject(); Integer(w, "ordinal", t.Ordinal); Integer(w, "inputTeeth", t.InputTeeth); Integer(w, "outputTeeth", t.OutputTeeth); Fraction(w, "pitchScale", t.Scale);
      w.WriteString("parallelSourceId", t.ParallelSourceId); Integer(w, "parallelPlacement", t.ParallelPlacement); if (t.TurnedSourceId is null) w.WriteNull("turnedSourceId"); else w.WriteString("turnedSourceId", t.TurnedSourceId); Integer(w, "turnedPlacement", t.TurnedPlacement); w.WriteEndObject(); }
    private static OrientedGoalTuple Tuple(JsonElement p) => new(I(p, "ordinal"), I(p, "inputTeeth"), I(p, "outputTeeth"), F(p.GetProperty("pitchScale")), S(p, "parallelSourceId"), I(p, "parallelPlacement"), p.GetProperty("turnedSourceId").ValueKind == JsonValueKind.Null ? null : S(p, "turnedSourceId"), I(p, "turnedPlacement"));
    private static void Envelope(Utf8JsonWriter w, ExactEnvelope3 e) { w.WriteStartObject(); Vector(w, "min", e.Min); Vector(w, "max", e.Max); w.WriteEndObject(); }
    private static ExactEnvelope3 Envelope(JsonElement p) => new(Vector(p.GetProperty("min")), Vector(p.GetProperty("max")));
    private static void Metrics(Utf8JsonWriter w, OrientedGoalMetrics m) { w.WriteStartObject(); Integer(w, "bodyCount", m.Bodies); w.WritePropertyName("pitchEnvelope"); Envelope(w, m.Envelope);
        Fraction(w, "aabbVolume", m.Volume); Fraction(w, "maximumExtent", m.MaximumExtent); Fraction(w, "preferencePenalty", m.PreferencePenalty); Integer(w, "totalBodyTeeth", m.TotalTeeth); w.WriteEndObject(); }
    private static OrientedGoalMetrics Metrics(JsonElement p) { var m = new OrientedGoalMetrics(I(p, "bodyCount"), Envelope(p.GetProperty("pitchEnvelope")), F(p.GetProperty("preferencePenalty")), I(p, "totalBodyTeeth"));
        Require(m.Volume == DerivedFraction(p.GetProperty("aabbVolume")) && m.MaximumExtent == DerivedFraction(p.GetProperty("maximumExtent")), "Forged derived metrics."); return m; }
    private static Rational DerivedFraction(JsonElement p) { var n = S(p, "numerator"); var d = S(p, "denominator"); Require(n.Length <= 1024 && d.Length <= 1024, "Derived metric digit bound.");
        var v = new Rational(BigInteger.Parse(n, CultureInfo.InvariantCulture), BigInteger.Parse(d, CultureInfo.InvariantCulture)); Require(v.Numerator.ToString(CultureInfo.InvariantCulture) == n && v.Denominator.ToString(CultureInfo.InvariantCulture) == d, "Noncanonical derived fraction."); return v; }
    private static int Number(JsonElement p) { var s = p.GetString(); Require(s is not null && s.Length <= 11, "Bounded decimal integer required."); var n = int.Parse(s!, CultureInfo.InvariantCulture); Require(OrientedGoalKeys.N(n) == s, "Noncanonical integer."); return n; }
    private static BigInteger Big(JsonElement p, string key) { var s = S(p, key); Require(s.Length <= 16, "Bounded tuple counter required."); var n = BigInteger.Parse(s, CultureInfo.InvariantCulture); Require(n >= 0 && n.ToString(CultureInfo.InvariantCulture) == s, "Invalid tuple counter."); return n; }
    internal static void Start(Utf8JsonWriter w, string format, string profile = OrientedGoalProfile.Id) { w.WriteStartObject(); w.WriteString("format", format); w.WriteString("formatVersion", CanonicalOrientedTwoOutputJson.WireVersion(OrientedGoalProfile.MechanicalProfile(profile)));
        if (profile == OrientedGoalProfile.RefinedId) PitchClearanceJson.Policy(w);
        w.WriteString("loweringPolicy", OrientedGoalProfile.Lowering); w.WriteString("traversalPolicy", OrientedGoalProfile.Traversal); w.WriteString("rankingPolicy", OrientedGoalProfile.Ranking); }
    internal static string CheckStart(JsonElement p, string format)
    {
        Require(S(p, "format") == format && (S(p, "formatVersion") == "0.1" || S(p, "formatVersion") == "0.2"), "Unsupported goal format/version.");
        var profile = S(p, "formatVersion") == "0.2" ? OrientedGoalProfile.RefinedId : OrientedGoalProfile.Id;
        if (profile == OrientedGoalProfile.RefinedId) PitchClearanceJson.CheckPolicy(p);
        Require(S(p, "loweringPolicy") == OrientedGoalProfile.Lowering && S(p, "traversalPolicy") == OrientedGoalProfile.Traversal && S(p, "rankingPolicy") == OrientedGoalProfile.Ranking, "Unsupported goal policy version."); return profile;
    }
    internal static byte[] Document(Action<Utf8JsonWriter> write)
    { using var stream = new MemoryStream(); using (var w = new Utf8JsonWriter(stream)) write(w); Require(stream.Length <= OrientedGoalProfile.MaxDocumentBytes, "Goal document resource limit."); return stream.ToArray(); }
    internal static JsonDocument OpenDocument(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length > 0 && bytes.Length <= OrientedGoalProfile.MaxDocumentBytes, "Bounded goal JSON required.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = 32 });
        try { var nodes = 0; Duplicates(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
}
