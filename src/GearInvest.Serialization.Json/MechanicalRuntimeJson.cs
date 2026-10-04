using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using static GearInvest.Serialization.Json.WindingConnectionJson;

namespace GearInvest.Serialization.Json;

public sealed class MechanicalCheckpointArtifact
{
    private readonly byte[] bytes;
    internal MechanicalCheckpointArtifact(WindingConnectionArtifact source, RuntimeSnapshot snapshot, byte[] bytes)
    { Source = source; Snapshot = snapshot; this.bytes = (byte[])bytes.Clone(); ArtifactId = Hash(bytes); }
    public WindingConnectionArtifact Source { get; }
    public RuntimeSnapshot Snapshot { get; }
    public string ArtifactId { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

/// <summary>Separate compact current-state format. The historical recording codec is unchanged.</summary>
public static class MechanicalRuntimeJson
{
    public const string Format = "gear-invest.mechanical-runtime-checkpoint";
    public const string Version = "1.0";
    public const string SpatialVersion = "2.0";
    private static string VersionFor(WindingConnectionArtifact source) => source.Source.WindingSource is SpatialWindingDefinition ? SpatialVersion : Version;
    public static MechanicalCheckpointArtifact Write(WindingConnectionArtifact source, RuntimeSnapshot state)
    {
        Require(source.Source.DefinitionId == state.Definition.Connection.DefinitionId, "ForeignSnapshot");
        var payload = Encode(w =>
        {
            w.WriteStartObject(); w.WriteString("runtimeProfile", MechanicalRuntime.ProfileFor(state.Definition)); w.WriteString("runtimeVersion", MechanicalRuntime.Version);
            w.WriteString("stateSchema", VersionFor(source)); w.WriteString("sourceArtifactId", source.ArtifactId); w.WriteBase64String("sourceArtifactUtf8", source.Bytes);
            w.WriteStartObject("policy"); w.WriteString("definitionId", state.Definition.DefinitionId);
            MechanicalAuthoringJson.Strings(w, "allowedModes", state.Definition.AllowedModes.Select(m => m.ToString())); Fraction(w, "alignmentOffset", state.Definition.AlignmentOffset); w.WriteEndObject();
            w.WriteString("validation", "current-state-source-revalidated"); w.WriteString("deletedHistoryValidation", "notPerformed");
            w.WritePropertyName("state"); State(w, state); w.WriteEndObject();
        });
        var bytes = Encode(w => { MechanicalAuthoringJson.Start(w, Format, VersionFor(source)); w.WriteString("payloadId", Hash(payload)); w.WriteBase64String("payloadUtf8", payload); w.WriteEndObject(); });
        return new(source, state, bytes);
    }
    public static MechanicalCheckpointArtifact Read(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var envelope = Parse(bytes); var root = envelope.RootElement;
        var schema = S(root, "formatVersion");
        Require(S(root, "format") == Format && (schema == Version || schema == SpatialVersion), "UnsupportedProfile");
        var raw = Raw(root, "payloadUtf8"); Require(Hash(raw) == S(root, "payloadId"), "CheckpointDigestMismatch");
        using var doc = Parse(raw); var p = doc.RootElement;
        Require(S(p, "runtimeProfile") == (schema == Version ? MechanicalRuntime.Profile : MechanicalRuntime.SpatialProfile) && S(p, "runtimeVersion") == MechanicalRuntime.Version && S(p, "stateSchema") == schema, "UnsupportedProfile");
        var source = ReadRuntimeSource(Raw(p, "sourceArtifactUtf8")); Require(source.ArtifactId == S(p, "sourceArtifactId") && VersionFor(source) == schema, "ForeignSnapshot");
        var policy = p.GetProperty("policy");
        var definition = new MechanicalModeDefinition(source.Source, Items(policy, "allowedModes", 5).Select(e => Enum.Parse<MechanicalConnectionMode>(e.GetString()!)), F(policy.GetProperty("alignmentOffset")));
        Require(definition.DefinitionId == S(policy, "definitionId"), "ForeignPolicy");
        var s = p.GetProperty("state"); var frame = s.GetProperty("frame"); var q = F(frame.GetProperty("driverTurns"));
        var pd = definition.Connection.Suffix.Parent.Definition;
        var sun = Items(frame, "coordinates", schema == SpatialVersion ? 10 : 6).Single(e => S(e, "shaftId") == pd.SunShaft.Id).GetProperty("value");
        var planet = Items(frame, "ports", schema == SpatialVersion ? 11 : 7).Single(e => S(e, "portId") == definition.Connection.PlanetPortId).GetProperty("value");
        var capture = s.GetProperty("captureWitness"); var locked = s.GetProperty("lockWitness");
        var state = new MechanicalRuntime(definition).Restore(S(s, "sessionId"), Count(s, "revision"), Count(s, "eventCursor"), S(s, "historyId"), E<MechanicalConnectionMode>(s, "mode"), s.GetProperty("allowedDirection").GetInt32(), q,
            Affine(s.GetProperty("couplingOffset")), s.GetProperty("lockReference").ValueKind == JsonValueKind.Null ? null : Affine(s.GetProperty("lockReference")), Affine(sun), Affine(planet),
            Items(s, "witnesses", 64).Select(w => new RuntimeWitness(S(w, "latentId"), F(w.GetProperty("driverTurns")))),
            capture.ValueKind == JsonValueKind.Null ? null : new RuntimeCaptureWitness(F(capture.GetProperty("driverTurns")), F(capture.GetProperty("sunNativeTurns"))),
            locked.ValueKind == JsonValueKind.Null ? null : new RuntimeLockWitness(F(locked.GetProperty("driverTurns")), Affine(locked.GetProperty("planetPort"))),
            Items(s, "ledger", 16).Select(e => new RuntimeLedgerEntry(S(e, "requestId"), S(e, "payloadId"), S(e, "resultStateId"), Count(e, "revision"), Count(e, "eventCursor"), Items(e, "eventIds", 16).Select(id => id.GetString()!))));
        var rebuilt = Write(source, state); using var freshEnvelope = Parse(rebuilt.Bytes); using var fresh = Parse(Raw(freshEnvelope.RootElement, "payloadUtf8"));
        Compare(p, fresh.RootElement, "runtime-checkpoint");
        var canonicalEnvelope = Encode(w => { MechanicalAuthoringJson.Start(w, Format, schema); w.WriteString("payloadId", Hash(raw)); w.WriteBase64String("payloadUtf8", raw); w.WriteEndObject(); });
        Require(bytes.SequenceEqual(canonicalEnvelope), "NoncanonicalCheckpointEnvelope");
        return new MechanicalCheckpointArtifact(source, state, bytes);
    });
    public static byte[] Snapshot(WindingConnectionArtifact source, RuntimeSnapshot state, bool includeScene = false) => Encode(w =>
    {
        w.WriteStartObject(); w.WriteString("profile", MechanicalRuntime.ProfileFor(state.Definition)); w.WriteString("runtimeVersion", MechanicalRuntime.Version);
        w.WriteString("sourceArtifactId", source.ArtifactId); w.WritePropertyName("state"); State(w, state);
        if (includeScene) { w.WritePropertyName("scene"); Scene(w, source); }
        w.WriteEndObject();
    });
    internal static void State(Utf8JsonWriter w, RuntimeSnapshot s)
    {
        var m = s.Mechanical; w.WriteStartObject(); w.WriteString("sessionId", s.SessionId); w.WriteString("definitionId", s.Definition.DefinitionId);
        w.WriteString("stateId", s.StateId); w.WriteString("historyId", s.HistoryId); Count(w, "revision", s.Revision); Count(w, "epoch", s.Epoch);
        Count(w, "eventCursor", s.EventCursor); Count(w, "staleBeforeRevision", s.StaleBeforeRevision); w.WriteString("mode", m.Mode.ToString()); w.WriteNumber("allowedDirection", m.AllowedDirection);
        MechanicalAuthoringJson.Strings(w, "requiredInputPorts", MechanicalModeEngine.RequiredInputPorts(s.Definition, m.Mode));
        w.WritePropertyName("couplingOffset"); Value(w, m.CouplingOffset); w.WritePropertyName("lockReference"); if (m.LockReference is null) w.WriteNullValue(); else Value(w, m.LockReference);
        w.WritePropertyName("captureWitness");
        if (s.CaptureWitness is null) w.WriteNullValue(); else { w.WriteStartObject(); Fraction(w, "driverTurns", s.CaptureWitness.DriverTurns); Fraction(w, "sunNativeTurns", s.CaptureWitness.SunNativeTurns); w.WriteEndObject(); }
        w.WritePropertyName("lockWitness");
        if (s.LockWitness is null) w.WriteNullValue(); else { w.WriteStartObject(); Fraction(w, "driverTurns", s.LockWitness.DriverTurns); Affine(w, "planetPort", s.LockWitness.PlanetPort); w.WriteEndObject(); }
        Array(w, "witnesses", s.Witnesses, (x, o) => { x.WriteStartObject(); x.WriteString("latentId", o.LatentId); Fraction(x, "driverTurns", o.DriverTurns); x.WriteEndObject(); });
        Array(w, "ledger", s.Ledger, (x, e) =>
        {
            x.WriteStartObject(); x.WriteString("requestId", e.RequestId); x.WriteString("payloadId", e.PayloadId); x.WriteString("resultStateId", e.ResultStateId);
            Count(x, "revision", e.Revision); Count(x, "eventCursor", e.EventCursor); MechanicalAuthoringJson.Strings(x, "eventIds", e.EventIds); x.WriteEndObject();
        });
        w.WritePropertyName("frame"); Frame(w, m.Frame, s.Definition.Connection.WindingSource); w.WriteEndObject();
    }
    private static RuntimeAffine Affine(JsonElement p) => new(F(p.GetProperty("constant")), Items(p, "terms", 64).Select(t => new KeyValuePair<string, Rational>(S(t, "latentId"), F(t.GetProperty("coefficient")))));
    private static void Affine(Utf8JsonWriter w, string name, RuntimeAffine value)
    { w.WriteStartObject(name); Fraction(w, "constant", value.Constant); Array(w, "terms", value.Terms, (x, t) => { x.WriteStartObject(); x.WriteString("latentId", t.Key); Fraction(x, "coefficient", t.Value); x.WriteEndObject(); }); w.WriteEndObject(); }
    public static BigInteger Count(JsonElement p, string key)
    {
        var text = S(p, key); Require(text.Length > 0 && text.Length <= 128 && text.All(c => c >= '0' && c <= '9') && (text.Length == 1 || text[0] != '0'), "InvalidCounter");
        return BigInteger.Parse(text, CultureInfo.InvariantCulture);
    }
    private static void Count(Utf8JsonWriter w, string key, BigInteger value) => w.WriteString(key, MechanicalRuntime.Text(value));
    public static RuntimeRequest ReadRequest(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement;
        var request = new RuntimeRequest(S(p, "id"), S(p, "sessionId"), S(p, "definitionId"), S(p, "expectedStateId"), Count(p, "epoch"), Count(p, "revision"), Items(p, "segments", 16).Select(seg =>
            new RuntimeSegment(new MechanicalModeInput(F(seg.GetProperty("driverTurns")), Ports(seg, "independentPorts"), Ports(seg, "observations")),
                Items(seg, "events", 16).Select(e => new RuntimeEvent(S(e, "id"), Count(e, "sequence"), I(e, "ordinal"), E<MechanicalConnectionEventKind>(e, "kind"))))));
        using var fresh = Parse(WriteRequest(request)); Compare(p, fresh.RootElement, "runtime-request"); return request;
    });
    private static IEnumerable<KeyValuePair<string, Rational>> Ports(JsonElement p, string key) => p.GetProperty(key).EnumerateObject().Select(v => new KeyValuePair<string, Rational>(v.Name, F(v.Value)));
    public static byte[] WriteRequest(RuntimeRequest r) => Encode(w =>
    {
        w.WriteStartObject(); w.WriteString("id", r.Id); w.WriteString("sessionId", r.SessionId); w.WriteString("definitionId", r.DefinitionId); w.WriteString("expectedStateId", r.ExpectedStateId);
        Count(w, "epoch", r.Epoch); Count(w, "revision", r.Revision);
        Array(w, "segments", r.Segments, (x, seg) =>
        {
            x.WriteStartObject(); Fraction(x, "driverTurns", seg.Input.DriverTurns); x.WriteStartObject("independentPorts"); foreach (var p in seg.Input.IndependentPorts) Fraction(x, p.Key, p.Value); x.WriteEndObject();
            x.WriteStartObject("observations"); foreach (var p in seg.Input.Observations) Fraction(x, p.Key, p.Value); x.WriteEndObject();
            Array(x, "events", seg.Events, (y, e) => { y.WriteStartObject(); y.WriteString("id", e.Id); Count(y, "sequence", e.Sequence); Integer(y, "ordinal", e.Ordinal); y.WriteString("kind", e.Kind.ToString()); y.WriteEndObject(); }); x.WriteEndObject();
        }); w.WriteEndObject();
    });
}
