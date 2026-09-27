using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization;

/// <summary>Canonical, bounded, reflection-free 0.1 transport in the shared JSON assembly. Noncanonical encodings are rejected,
/// including unknown/duplicate properties, non-normalized fractions and unrecognized enum values.</summary>
public static class DiscreteEmbodimentJson
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static byte[] WriteModel(DiscreteEmbodimentModel model)
    {
        Check(new DiscreteEmbodimentCompiler().Validate(model).IsValid, "invalid-model");
        return Envelope(DiscreteEmbodimentContract.ModelFormat, w => Model(w, model));
    }
    public static DiscreteEmbodimentModel ReadModel(byte[] bytes)
    {
        using var document = Open(bytes, DiscreteEmbodimentContract.ModelFormat);
        var p = document.RootElement.GetProperty("payload");
        var components = A(p, "components", 32).Select(c => new DiscreteComponent(S(c, "id"), E<DiscreteComponentKind>(c, "kind"), I(c, "detents"), I(c, "reference"), I(c, "capacity"),
            A(c, "sectors", 512).Select(N), R(c.GetProperty("scale")), R(c.GetProperty("offset")))).ToArray();
        var connections = A(p, "connections", 64).Select(c => new DiscreteConnection(S(c, "from"), S(c, "output"), S(c, "to"), S(c, "input"), E<DiscretePortKind>(c, "kind"))).ToArray();
        var motion = p.GetProperty("motion"); var projection = p.GetProperty("projection");
        var request = new DiscreteProjectionRequest(Strings(projection, "requiredStates"), Strings(projection, "requiredEffects"), Strings(projection, "omittedStates"), Strings(projection, "omittedEffects"),
            S(projection, "eventDefinitionId"), I(projection, "primaryPulseCapacity"), S(projection, "profile"));
        var model = new DiscreteEmbodimentModel(components, connections, new DiscreteMotionProfile(A(motion, "boundaries", 9).Select(R), S(motion, "id")), S(p, "sourcePlanId"), request,
            A(p, "bindings", 64).Select(b => new DiscreteSourceBinding(S(b, "kind"), S(b, "sourceId"), S(b, "componentId"))));
        Check(model.ModelId == S(p, "modelId") && model.BindingId == S(p, "bindingId"), "model-or-binding-identity");
        Check(bytes.SequenceEqual(WriteModel(model)), "noncanonical-model"); return model;
    }
    public static byte[] WriteResult(DiscreteEmbodimentModel model, DiscreteActuationResult result)
    {
        Check(new DiscreteEmbodimentEvaluator().ValidateResult(model, result).IsValid, "invalid-actuation-result");
        return Envelope(DiscreteEmbodimentContract.ResultFormat, w => Result(w, result));
    }
    public static DiscreteActuationResult ReadResult(DiscreteEmbodimentModel model, byte[] bytes)
    {
        using var document = Open(bytes, DiscreteEmbodimentContract.ResultFormat);
        var p = document.RootElement.GetProperty("payload");
        var result = new DiscreteActuationResult(S(p, "modelId"), S(p, "bindingId"), S(p, "eventRequestId"), I(p, "budget"), E<DiscreteStatus>(p, "status"), I(p, "knownOccurrences"), R(p.GetProperty("toRoot")),
            State(p.GetProperty("initial")), A(p, "cycles", 4096).Select(Cycle), NullableState(p.GetProperty("final")), NullableState(p.GetProperty("checkpoint")), Validation(p.GetProperty("validation")));
        Check(result.RequestId == S(p, "requestId"), "request-identity");
        Check(bytes.SequenceEqual(WriteResult(model, result)), "noncanonical-result"); return result;
    }
    public static string RawSha256(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
    internal static byte[] Envelope(string format, Action<Utf8JsonWriter> write)
    {
        var payload = Bytes(write); var hash = EnvelopeHash(format, payload);
        return Bytes(w =>
        {
            w.WriteStartObject(); w.WriteString("format", format); w.WriteString("version", DiscreteEmbodimentContract.Version); w.WriteString("artifactHash", hash);
            w.WritePropertyName("payload"); using var doc = JsonDocument.Parse(payload); doc.RootElement.WriteTo(w); w.WriteEndObject();
        });
    }
    private static string EnvelopeHash(string format, byte[] payload) => "sha256:" + RawSha256(Bytes(w =>
    {
        w.WriteStartObject(); w.WriteString("format", format); w.WriteString("version", DiscreteEmbodimentContract.Version); w.WritePropertyName("payload");
        using var doc = JsonDocument.Parse(payload); doc.RootElement.WriteTo(w); w.WriteEndObject();
    }));
    internal static JsonDocument Open(byte[] bytes, string format, int maximumBytes = MaximumBytes)
    {
        Check(bytes.Length > 0 && bytes.Length <= maximumBytes, "byte-limit");
        var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try
        {
            Walk(doc.RootElement);
            Check(S(doc.RootElement, "format") == format && S(doc.RootElement, "version") == DiscreteEmbodimentContract.Version, "unsupported-format-version");
            var payload = Bytes(w => doc.RootElement.GetProperty("payload").WriteTo(w));
            Check(S(doc.RootElement, "artifactHash") == EnvelopeHash(format, payload), "artifact-hash-mismatch");
            return doc;
        }
        catch { doc.Dispose(); throw; }
    }
    private static void Walk(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) { Check(names.Add(p.Name), "duplicate-property"); Walk(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) { Check(e.GetArrayLength() <= 32768, "array-limit"); foreach (var x in e.EnumerateArray()) Walk(x); }
        else if (e.ValueKind == JsonValueKind.String) Check((e.GetString() ?? "").Length <= 4096, "string-limit");
    }
    private static byte[] Bytes(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) write(writer);
        var bytes = stream.ToArray(); Check(bytes.Length <= MaximumBytes, "byte-limit"); return bytes;
    }
    private static void Model(Utf8JsonWriter w, DiscreteEmbodimentModel m)
    {
        w.WriteStartObject(); w.WriteString("modelId", m.ModelId); w.WriteString("bindingId", m.BindingId); w.WriteString("sourcePlanId", m.SourcePlanId);
        w.WriteStartObject("projection"); var p = m.Projection; w.WriteString("profile", p.Profile); w.WriteString("eventDefinitionId", p.EventDefinitionId); w.WriteNumber("primaryPulseCapacity", p.PrimaryPulseCapacity);
        Strings(w, "requiredStates", p.RequiredStates); Strings(w, "requiredEffects", p.RequiredEffects); Strings(w, "omittedStates", p.OmittedStates); Strings(w, "omittedEffects", p.OmittedEffects); w.WriteEndObject();
        w.WriteStartObject("motion"); w.WriteString("id", m.Motion.Id); Array(w, "boundaries", m.Motion.Boundaries, (a, b) => R(a, b)); w.WriteEndObject();
        Array(w, "components", m.Components, (a, c) =>
        {
            a.WriteStartObject(); a.WriteString("id", c.Id); a.WriteString("kind", c.Kind.ToString()); a.WriteNumber("detents", c.Detents); a.WriteNumber("reference", c.Reference); a.WriteNumber("capacity", c.Capacity);
            a.WritePropertyName("scale"); R(a, c.Scale); a.WritePropertyName("offset"); R(a, c.Offset); Array(a, "sectors", c.Sectors, (x, v) => x.WriteStringValue(DiscreteEmbodimentContract.Integer(v))); a.WriteEndObject();
        });
        Array(w, "connections", m.Connections, (a, c) => { a.WriteStartObject(); a.WriteString("from", c.From); a.WriteString("output", c.Output); a.WriteString("to", c.To); a.WriteString("input", c.Input); a.WriteString("kind", c.Kind.ToString()); a.WriteEndObject(); });
        Array(w, "bindings", m.Bindings, (a, b) => { a.WriteStartObject(); a.WriteString("kind", b.Kind); a.WriteString("sourceId", b.SourceId); a.WriteString("componentId", b.ComponentId); a.WriteEndObject(); }); w.WriteEndObject();
    }
    private static void Result(Utf8JsonWriter w, DiscreteActuationResult r)
    {
        w.WriteStartObject(); w.WriteString("modelId", r.ModelId); w.WriteString("bindingId", r.BindingId); w.WriteString("requestId", r.RequestId); w.WriteString("eventRequestId", r.EventRequestId); w.WriteNumber("budget", r.Budget); w.WriteString("status", r.Status.ToString());
        w.WriteNumber("knownOccurrences", r.KnownOccurrences); w.WriteNumber("appliedOccurrences", r.AppliedOccurrences); w.WriteNumber("omittedOccurrences", r.OmittedOccurrences); w.WriteBoolean("searchComplete", r.SearchComplete); w.WriteBoolean("resultTruncated", r.ResultTruncated);
        w.WritePropertyName("toRoot"); R(w, r.ToRoot); w.WritePropertyName("initial"); State(w, r.Initial);
        Array(w, "cycles", r.Cycles, Cycle); w.WritePropertyName("final"); State(w, r.Final); w.WritePropertyName("checkpoint"); State(w, r.Checkpoint);
        w.WritePropertyName("validation"); Validation(w, r.Validation); w.WriteEndObject();
    }
    internal static void State(Utf8JsonWriter w, DiscreteEmbodimentState? s)
    {
        if (s == null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("stateId", s.StateId); w.WriteString("modelId", s.ModelId); w.WriteString("candidateId", s.CandidateId); w.WriteString("driverId", s.DriverId); w.WriteString("cursor", DiscreteEmbodimentContract.Integer(s.Cursor));
        w.WritePropertyName("rootTurns"); R(w, s.RootTurns); Array(w, "positions", s.Positions, (a, p) => { a.WriteStartObject(); a.WriteString("wheelId", p.WheelId); a.WriteNumber("index", p.Index); a.WritePropertyName("unwrappedTurns"); R(a, p.UnwrappedTurns); a.WriteEndObject(); }); w.WriteEndObject();
    }
    internal static DiscreteEmbodimentState? NullableState(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : State(e);
    internal static DiscreteEmbodimentState State(JsonElement e)
    {
        var s = new DiscreteEmbodimentState(S(e, "modelId"), S(e, "candidateId"), S(e, "driverId"), R(e.GetProperty("rootTurns")), N(e.GetProperty("cursor")),
            A(e, "positions", 2).Select(p => new DiscreteWheelPosition(S(p, "wheelId"), I(p, "index"), R(p.GetProperty("unwrappedTurns")))));
        Check(S(e, "stateId") == s.StateId, "state-identity"); return s;
    }
    internal static void Cycle(Utf8JsonWriter w, DiscreteActuationCycle c)
    {
        w.WriteStartObject(); w.WriteString("cycleId", c.CycleId); w.WriteString("modelId", c.ModelId); w.WriteString("occurrenceId", c.OccurrenceId); w.WriteString("ordinal", DiscreteEmbodimentContract.Integer(c.Ordinal));
        w.WritePropertyName("eventRoot"); R(w, c.EventRoot); w.WritePropertyName("before"); State(w, c.Before); w.WritePropertyName("after"); State(w, c.After);
        w.WritePropertyName("positionReading"); R(w, c.PositionReading); w.WritePropertyName("limitReading"); R(w, c.LimitReading); w.WriteString("route", c.Route.ToString());
        Array(w, "actions", c.Actions, (a, x) =>
        {
            a.WriteStartObject(); a.WriteString("kind", x.Kind.ToString()); a.WriteString("componentId", x.ComponentId); a.WriteString("wheelId", x.WheelId);
            a.WritePropertyName("begin"); R(a, x.Begin); a.WritePropertyName("end"); R(a, x.End); a.WritePropertyName("fromTurns"); R(a, x.FromTurns); a.WritePropertyName("toTurns"); R(a, x.ToTurns); a.WriteEndObject();
        }); w.WriteEndObject();
    }
    internal static DiscreteActuationCycle Cycle(JsonElement e)
    {
        var c = new DiscreteActuationCycle(S(e, "modelId"), S(e, "occurrenceId"), N(e.GetProperty("ordinal")), R(e.GetProperty("eventRoot")), State(e.GetProperty("before")), State(e.GetProperty("after")),
            R(e.GetProperty("positionReading")), R(e.GetProperty("limitReading")), E<DiscreteRoute>(e, "route"), A(e, "actions", 2048).Select(a => new DiscreteMicroAction(E<DiscreteActionKind>(a, "kind"), S(a, "componentId"), S(a, "wheelId"), R(a.GetProperty("begin")), R(a.GetProperty("end")), R(a.GetProperty("fromTurns")), R(a.GetProperty("toTurns")))));
        Check(c.CycleId == S(e, "cycleId"), "cycle-identity"); return c;
    }
    private static void Validation(Utf8JsonWriter w, DiscreteValidation v)
    {
        w.WriteStartObject(); Strings(w, "diagnostics", v.Diagnostics); Array(w, "levels", v.Levels, (a, l) => { a.WriteStartObject(); a.WriteString("level", l.Level); a.WriteString("status", l.Status); a.WriteString("detail", l.Detail); a.WriteEndObject(); }); w.WriteEndObject();
    }
    private static DiscreteValidation Validation(JsonElement e) => new DiscreteValidation(Strings(e, "diagnostics"), A(e, "levels", 16).Select(l => new DiscreteValidationLevel(S(l, "level"), S(l, "status"), S(l, "detail"))));
    internal static void R(Utf8JsonWriter w, Rational r) { w.WriteStartObject(); w.WriteString("numerator", DiscreteEmbodimentContract.Integer(r.Numerator)); w.WriteString("denominator", DiscreteEmbodimentContract.Integer(r.Denominator)); w.WriteEndObject(); }
    internal static Rational R(JsonElement e) { var n = N(e.GetProperty("numerator")); var d = N(e.GetProperty("denominator")); Check(d > 0, "fraction-denominator"); return new Rational(n, d); }
    internal static BigInteger N(JsonElement e) { var s = e.GetString() ?? ""; Check(s.Length > 0 && s.Length <= 128, "integer-bound"); Check(BigInteger.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && DiscreteEmbodimentContract.Integer(n) == s, "noncanonical-integer"); return n; }
    internal static string S(JsonElement e, string key) => e.GetProperty(key).GetString() ?? throw new FormatException("null-string");
    internal static int I(JsonElement e, string key) => e.GetProperty(key).GetInt32();
    internal static T E<T>(JsonElement e, string key) where T : struct { var s = S(e, key); Check(Enum.TryParse<T>(s, out var v) && Enum.IsDefined(typeof(T), v) && v.ToString() == s, "unknown-kind"); return v; }
    internal static JsonElement[] A(JsonElement e, string key, int max) { var a = e.GetProperty(key); Check(a.GetArrayLength() <= max, "collection-bound:" + key); return a.EnumerateArray().ToArray(); }
    private static string[] Strings(JsonElement e, string key) => A(e, key, 64).Select(v => v.GetString() ?? throw new FormatException("null-string")).ToArray();
    private static void Strings(Utf8JsonWriter w, string key, IEnumerable<string> values) => Array(w, key, values, (a, s) => a.WriteStringValue(s));
    internal static void Array<T>(Utf8JsonWriter w, string key, IEnumerable<T> values, Action<Utf8JsonWriter, T> write) { w.WriteStartArray(key); foreach (var v in values) write(w, v); w.WriteEndArray(); }
    internal static void Check(bool ok, string message) { if (!ok) throw new FormatException(message); }
}
