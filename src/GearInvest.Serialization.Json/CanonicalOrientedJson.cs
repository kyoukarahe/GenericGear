using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit bounded, reflection-free/AOT-friendly codec. Identity is independent from validation and physical feasibility.</summary>
public static class CanonicalOrientedJson
{
    public const string RequestFormat = "gear-invest.oriented-transmission-request";
    public const int MaxDocumentBytes = 4 * 1024 * 1024;
    public static byte[] WriteRequest(OrientedAssemblyRequest request) => Bytes(w => Request(w, request));
    public static OrientedAssemblyRequest ReadRequest(byte[] bytes) => CheckedRead(() => ReadRequestDocument(bytes));
    private static OrientedAssemblyRequest ReadRequestDocument(byte[] bytes)
    {
        using var doc = Open(bytes); var request = Request(doc.RootElement);
        Require(bytes.SequenceEqual(WriteRequest(request)), "Noncanonical/unknown request fields."); return request;
    }
    public static OrientedArtifactWriteResult Write(OrientedMechanism mechanism, OrientedAssemblyRequest request)
    {
        var validation = OrientedTransmissionComposer.ValidateContext(request.ToEngineRequest(), mechanism);
        var candidateId = Hash(Bytes(w => Model(w, mechanism)));
        var provisional = new OrientedArtifact(candidateId, "", request, mechanism, validation);
        var artifact = new OrientedArtifact(candidateId, Hash(Document(provisional, false)), request, mechanism, validation);
        return new OrientedArtifactWriteResult(artifact, Document(artifact, true));
    }
    public static OrientedArtifact Read(byte[] bytes) => CheckedRead(() => ReadDocument(bytes));
    private static OrientedArtifact ReadDocument(byte[] bytes)
    {
        using var doc = Open(bytes); var p = doc.RootElement; Header(p, OrientedArtifact.Format);
        var artifact = new OrientedArtifact(S(p, "candidateId"), S(p, "artifactHash"), Request(p.GetProperty("request")), Model(p.GetProperty("mechanism")), Validation(p.GetProperty("validation")));
        Require(bytes.SequenceEqual(Document(artifact, true)), "Noncanonical/unknown oriented artifact fields.");
        return artifact;
    }
    public static ArtifactIdentityVerification VerifyIdentity(OrientedArtifact artifact)
    {
        var candidate = Hash(Bytes(w => Model(w, artifact.Mechanism))); var hash = Hash(Document(artifact, false));
        return new ArtifactIdentityVerification(candidate, hash, candidate == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteValidation(OrientedValidation validation) => Bytes(w => Validation(w, validation));
    public static byte[] WriteEvaluation(OrientedMechanism mechanism, Rational rootTurns) => Bytes(w =>
    {
        w.WriteStartObject(); Fraction(w, "rootTurns", rootTurns);
        Array(w, "shafts", OrientedTransmissionComposer.Evaluate(mechanism, rootTurns), (a, s) =>
        { a.WriteStartObject(); a.WriteString("shaftId", s.ShaftId); Fraction(a, "turns", s.Turns); Vector(a, "positiveAxis", s.PositiveAxis); Vector(a, "worldAngularVelocityPerRoot", s.WorldAngularVelocityPerRoot); a.WriteEndObject(); }); w.WriteEndObject();
    });
    public static byte[] WriteResult(OrientedTransmissionResult result) => Bytes(w =>
    { w.WriteStartObject(); w.WriteString("status", result.Status.ToString()); w.WriteBoolean("isSuccess", result.IsSuccess); w.WritePropertyName("validation"); Validation(w, result.Validation); w.WriteEndObject(); });
    public static string Hash(byte[] bytes)
    { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))); }

    private static byte[] Document(OrientedArtifact a, bool includeHash) => Bytes(w =>
    {
        w.WriteStartObject(); w.WriteString("format", OrientedArtifact.Format); w.WriteString("formatVersion", OrientedArtifact.Version); w.WriteString("candidateId", a.CandidateId);
        if (includeHash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Request(w, a.Request); w.WritePropertyName("mechanism"); Model(w, a.Mechanism);
        w.WritePropertyName("validation"); Validation(w, a.StoredValidation); w.WriteEndObject();
    });
    private static void Request(Utf8JsonWriter w, OrientedAssemblyRequest r)
    {
        w.WriteStartObject(); w.WriteString("format", RequestFormat); w.WriteString("formatVersion", "0.1");
        w.WriteStartObject("bevel"); w.WriteString("profile", r.Bevel.Profile); Vector(w, "apex", r.Bevel.Apex); Mount(w, "input", r.Bevel.Input); Mount(w, "output", r.Bevel.Output);
        Fraction(w, "innerParameter", r.Bevel.InnerParameter); OptionalFraction(w, "requestedTransfer", r.Bevel.RequestedTransfer); w.WriteEndObject();
        Placement(w, "upstream", r.Upstream); Placement(w, "downstream", r.Downstream); Frame(w, "assemblyPose", r.AssemblyPose);
        OptionalFraction(w, "requestedTransfer", r.RequestedTransfer); w.WriteBoolean("requireCrossComponentClearance", r.RequireCrossComponentClearance);
        Array(w, "keepOuts", r.KeepOuts, KeepOut); w.WriteEndObject();
    }
    private static OrientedAssemblyRequest Request(JsonElement p)
    {
        Header(p, RequestFormat); var b = p.GetProperty("bevel");
        var pair = new RightAngleBevelRequest(Vector(b.GetProperty("apex")), Mount(b.GetProperty("input")), Mount(b.GetProperty("output")), F(b.GetProperty("innerParameter")), OptionalFraction(b, "requestedTransfer"), S(b, "profile"));
        return new OrientedAssemblyRequest(pair, Placement(p.GetProperty("upstream")), Placement(p.GetProperty("downstream")), Frame(p.GetProperty("assemblyPose")),
            OptionalFraction(p, "requestedTransfer"), p.GetProperty("requireCrossComponentClearance").GetBoolean(), Items(p, "keepOuts", 16).Select(KeepOut));
    }
    internal static void Mount(Utf8JsonWriter w, string key, BevelGearMount m)
    {
        w.WriteStartObject(key); w.WritePropertyName("shaft"); Shaft(w, m.Shaft); Vector(w, "coneDirection", m.ConeDirection); Integer(w, "teeth", m.Teeth);
        Fraction(w, "outerPitchRadiusPerTooth", m.OuterPitchRadiusPerTooth); Vector(w, "fixedCenter", m.FixedCenter); w.WritePropertyName("port"); Port(w, m.Port); w.WriteEndObject();
    }
    internal static BevelGearMount Mount(JsonElement p) => new(Shaft(p.GetProperty("shaft")), Vector(p.GetProperty("coneDirection")), I(p, "teeth"), F(p.GetProperty("outerPitchRadiusPerTooth")), Vector(p.GetProperty("fixedCenter")), Port(p.GetProperty("port")));
    internal static void Placement(Utf8JsonWriter w, string key, PlanarArtifactPlacement? m)
    {
        if (m is null) { w.WriteNull(key); return; }
        w.WriteStartObject(key); w.WriteBase64String("sourceArtifactUtf8", m.SourceBytes); Frame(w, "pose", m.Pose);
        w.WriteString("inputDofId", m.InputDofId); w.WriteString("outputDofId", m.OutputDofId); w.WritePropertyName("connectionPort"); Port(w, m.ConnectionPort); w.WriteEndObject();
    }
    internal static PlanarArtifactPlacement? Placement(JsonElement p) => p.ValueKind == JsonValueKind.Null ? null :
        new PlanarArtifactPlacement(p.GetProperty("sourceArtifactUtf8").GetBytesFromBase64(), Frame(p.GetProperty("pose")), S(p, "inputDofId"), S(p, "outputDofId"), Port(p.GetProperty("connectionPort")));

    private static void Model(Utf8JsonWriter w, OrientedMechanism m)
    {
        w.WriteStartObject(); w.WriteString("profile", m.Profile); w.WriteString("rootShaftId", m.RootShaftId); w.WriteString("outputShaftId", m.OutputShaftId);
        WriteGraphFields(w, m.Shafts, m.Bodies, m.Contacts, m.Ports, m.Connections, m.SourceMappings, m.Sources, m.Solution, m.RequireCrossComponentClearance, m.KeepOuts);
        w.WriteEndObject();
    }
    internal static void WriteGraphFields(Utf8JsonWriter w, IEnumerable<OrientedShaft> shafts, IEnumerable<OrientedGearBody> bodies,
        IEnumerable<OrientedGearContact> contacts, IEnumerable<ShaftPort> ports, IEnumerable<ShaftPortConnection> connections,
        IEnumerable<SourceShaftMapping> sourceMappings, IEnumerable<OrientedSourceIdentity> sources, KinematicSolution solution,
        bool requireCrossComponentClearance, IEnumerable<OrientedKeepOut> keepOuts)
    {
        Array(w, "shafts", shafts, Shaft);
        Array(w, "bodies", bodies, (a, b) => { a.WriteStartObject(); a.WriteString("id", b.Id); a.WriteString("shaftId", b.ShaftId); a.WriteString("kind", b.Kind.ToString());
            Frame(a, "mountingFrame", b.MountingFrame); Integer(a, "teeth", b.Teeth); Fraction(a, "outerPitchRadius", b.OuterPitchRadius); a.WriteString("sourceModuleId", b.SourceModuleId); a.WriteEndObject(); });
        Array(w, "contacts", contacts, (a, c) => { a.WriteStartObject(); a.WriteString("id", c.Id); a.WriteString("kind", c.Kind.ToString()); a.WriteString("bodyAId", c.BodyAId); a.WriteString("bodyBId", c.BodyBId);
            Fraction(a, "storedTransfer", c.StoredTransfer); a.WritePropertyName("cone"); if (c.Cone is null) a.WriteNullValue(); else Cone(a, c.Cone); a.WriteEndObject(); });
        Array(w, "ports", ports, Port);
        Array(w, "connections", connections, (a, c) => { a.WriteStartObject(); a.WriteString("id", c.Id); a.WriteString("kind", "RigidZeroPhase"); a.WriteString("portAId", c.PortAId); a.WriteString("portBId", c.PortBId); Fraction(a, "coordinateTransfer", c.CoordinateTransfer); a.WriteEndObject(); });
        Array(w, "sourceMappings", sourceMappings, (a, s) => { a.WriteStartObject(); a.WriteString("moduleId", s.ModuleId); a.WriteString("sourceDofId", s.SourceDofId); a.WriteString("shaftId", s.ShaftId); Fraction(a, "coordinateTransfer", s.CoordinateTransfer); a.WriteEndObject(); });
        Array(w, "sources", sources, (a, s) => { a.WriteStartObject(); a.WriteString("moduleId", s.ModuleId); a.WriteString("candidateId", s.CandidateId); a.WriteString("artifactHash", s.ArtifactHash); a.WriteEndObject(); });
        w.WriteStartObject("solution"); w.WriteString("rootDofId", solution.RootDofId);
        Array(w, "states", solution.States, (a, s) => { a.WriteStartObject(); a.WriteString("dofId", s.DofId); Fraction(a, "coefficient", s.Coefficient); Fraction(a, "phaseOffset", s.PhaseOffset); a.WriteEndObject(); }); w.WriteEndObject();
        w.WriteBoolean("requireCrossComponentClearance", requireCrossComponentClearance); Array(w, "keepOuts", keepOuts, KeepOut);
    }
    private static OrientedMechanism Model(JsonElement p)
    {
        var shafts = Items(p, "shafts", 34).Select(Shaft).ToArray(); var ports = Items(p, "ports", 4).Select(Port).ToArray();
        var bodies = Items(p, "bodies", 34).Select(b => new OrientedGearBody(S(b, "id"), S(b, "shaftId"), E<OrientedGearKind>(b, "kind"), Frame(b.GetProperty("mountingFrame")), I(b, "teeth"), F(b.GetProperty("outerPitchRadius")), S(b, "sourceModuleId"))).ToArray();
        var contacts = Items(p, "contacts", 33).Select(c => new OrientedGearContact(S(c, "id"), E<OrientedContactKind>(c, "kind"), S(c, "bodyAId"), S(c, "bodyBId"), F(c.GetProperty("storedTransfer")), c.GetProperty("cone").ValueKind == JsonValueKind.Null ? null : Cone(c.GetProperty("cone")))).ToArray();
        var connections = Items(p, "connections", 2).Select(c => { Require(S(c, "kind") == "RigidZeroPhase", "Unsupported connection kind."); return new ShaftPortConnection(S(c, "id"), S(c, "portAId"), S(c, "portBId"), F(c.GetProperty("coordinateTransfer"))); }).ToArray();
        var mappings = Items(p, "sourceMappings", 32).Select(s => new SourceShaftMapping(S(s, "moduleId"), S(s, "sourceDofId"), S(s, "shaftId"), F(s.GetProperty("coordinateTransfer")))).ToArray();
        var sources = Items(p, "sources", 2).Select(s => new OrientedSourceIdentity(S(s, "moduleId"), S(s, "candidateId"), S(s, "artifactHash"))).ToArray();
        var solution = p.GetProperty("solution"); var states = Items(solution, "states", 34).Select(s => new DofKinematicState(S(s, "dofId"), F(s.GetProperty("coefficient")), F(s.GetProperty("phaseOffset")))).ToArray();
        foreach (var ids in new[] { shafts.Select(s => s.Id), bodies.Select(b => b.Id), contacts.Select(c => c.Id), ports.Select(a => a.Id), connections.Select(c => c.Id), states.Select(s => s.DofId), sources.Select(s => s.ModuleId), mappings.Select(m => m.ModuleId + "/" + m.SourceDofId) })
            Require(ids.Count() == ids.Distinct(StringComparer.Ordinal).Count(), "Duplicate oriented identifier.");
        Require(bodies.All(b => shafts.Any(s => s.Id == b.ShaftId)) && contacts.All(c => bodies.Any(b => b.Id == c.BodyAId) && bodies.Any(b => b.Id == c.BodyBId)) &&
            ports.All(a => shafts.Any(s => s.Id == a.ShaftId)) && connections.All(c => ports.Any(a => a.Id == c.PortAId) && ports.Any(a => a.Id == c.PortBId)), "Unresolved oriented reference.");
        Require(S(p, "profile") == OrientedTransmissionProfile.Id, "Unsupported oriented profile.");
        return new OrientedMechanism(S(p, "rootShaftId"), S(p, "outputShaftId"), shafts, bodies, contacts, ports, connections, mappings, sources,
            new KinematicSolution(S(solution, "rootDofId"), states), p.GetProperty("requireCrossComponentClearance").GetBoolean(), Items(p, "keepOuts", 16).Select(KeepOut), S(p, "profile"));
    }
    internal static void Cone(Utf8JsonWriter w, RightAnglePitchCone c)
    { w.WriteStartObject(); Vector(w, "apex", c.Apex); Vector(w, "outwardA", c.OutwardA); Vector(w, "outwardB", c.OutwardB); Vector(w, "outerContact", c.OuterContact); Fraction(w, "innerParameter", c.InnerParameter); Fraction(w, "outerScaleA", c.OuterScaleA); Fraction(w, "outerScaleB", c.OuterScaleB); Fraction(w, "coneDistanceSquared", c.ConeDistanceSquared); w.WriteEndObject(); }
    internal static RightAnglePitchCone Cone(JsonElement p)
    {
        var c = new RightAnglePitchCone(Vector(p.GetProperty("apex")), Vector(p.GetProperty("outwardA")), Vector(p.GetProperty("outwardB")), Vector(p.GetProperty("outerContact")), F(p.GetProperty("innerParameter")), F(p.GetProperty("outerScaleA")), F(p.GetProperty("outerScaleB")));
        Require(c.ConeDistanceSquared == F(p.GetProperty("coneDistanceSquared")), "Inconsistent derived cone distance squared."); return c;
    }
    internal static void Shaft(Utf8JsonWriter w, OrientedShaft s) { w.WriteStartObject(); w.WriteString("id", s.Id); Frame(w, "frame", s.Frame); w.WriteBoolean("isPrescribed", s.IsPrescribed); w.WriteEndObject(); }
    internal static OrientedShaft Shaft(JsonElement p) => new(S(p, "id"), Frame(p.GetProperty("frame")), p.GetProperty("isPrescribed").GetBoolean());
    internal static void Port(Utf8JsonWriter w, ShaftPort p) { w.WriteStartObject(); w.WriteString("id", p.Id); w.WriteString("shaftId", p.ShaftId); w.WriteString("kind", p.Kind.ToString()); Frame(w, "frame", p.Frame); Fraction(w, "phaseOffset", p.PhaseOffset); w.WriteEndObject(); }
    internal static ShaftPort Port(JsonElement p) => new(S(p, "id"), S(p, "shaftId"), Frame(p.GetProperty("frame")), F(p.GetProperty("phaseOffset")), E<ShaftConnectionKind>(p, "kind"));
    internal static void Frame(Utf8JsonWriter w, string key, OrientedFrame f) { w.WriteStartObject(key); Vector(w, "origin", f.Origin); Vector(w, "x", f.X); Vector(w, "y", f.Y); Vector(w, "z", f.Z); w.WriteEndObject(); }
    internal static OrientedFrame Frame(JsonElement p)
    { var f = new OrientedFrame(Vector(p.GetProperty("origin")), Vector(p.GetProperty("x")), Vector(p.GetProperty("y")), Vector(p.GetProperty("z"))); Require(f.IsProperCardinal, "Unsupported/malformed non-proper cardinal frame."); return f; }
    internal static void Vector(Utf8JsonWriter w, string key, ExactVector3 v) { w.WriteStartArray(key); Fraction(w, v.X); Fraction(w, v.Y); Fraction(w, v.Z); w.WriteEndArray(); }
    internal static ExactVector3 Vector(JsonElement p) { Require(p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 3, "Expected three exact vector components."); return new(F(p[0]), F(p[1]), F(p[2])); }
    internal static void KeepOut(Utf8JsonWriter w, OrientedKeepOut k) { w.WriteStartObject(); w.WriteString("id", k.Id); Vector(w, "min", k.Envelope.Min); Vector(w, "max", k.Envelope.Max); w.WriteEndObject(); }
    internal static OrientedKeepOut KeepOut(JsonElement p) => new(S(p, "id"), new ExactEnvelope3(Vector(p.GetProperty("min")), Vector(p.GetProperty("max"))));
    internal static void Validation(Utf8JsonWriter w, OrientedValidation v)
    {
        w.WriteStartObject(); w.WriteBoolean("isValid", v.IsValid);
        Array(w, "domains", v.Checks, (a, c) => { a.WriteStartObject(); a.WriteString("domain", c.Domain); a.WriteString("subject", c.Subject); a.WriteString("verdict", c.Verdict.ToString()); a.WriteBoolean("required", c.Required); a.WriteString("detail", c.Detail); a.WriteEndObject(); });
        Array(w, "diagnostics", v.Diagnostics, (a, d) => { a.WriteStartObject(); a.WriteString("code", d.Code); a.WriteString("severity", d.Severity.ToString()); a.WriteString("message", d.Message); a.WriteString("subjectId", d.SubjectId); a.WriteEndObject(); }); w.WriteEndObject();
    }
    internal static OrientedValidation Validation(JsonElement p)
    {
        var v = new OrientedValidation(Items(p, "domains", 2048).Select(c => new OrientedDomainCheck(S(c, "domain"), S(c, "subject"), E<OrientedCheckVerdict>(c, "verdict"), c.GetProperty("required").GetBoolean(), S(c, "detail"))),
            Items(p, "diagnostics", 256).Select(d => new Diagnostic(S(d, "code"), E<DiagnosticSeverity>(d, "severity"), S(d, "message"), d.GetProperty("subjectId").ValueKind == JsonValueKind.Null ? null : S(d, "subjectId"))));
        Require(v.IsValid == p.GetProperty("isValid").GetBoolean(), "Stored validation summary is internally inconsistent."); return v;
    }
    internal static void Fraction(Utf8JsonWriter w, string key, Rational value) { w.WritePropertyName(key); Fraction(w, value); }
    internal static void Fraction(Utf8JsonWriter w, Rational v)
    {
        var n = v.Numerator.ToString(CultureInfo.InvariantCulture); var d = v.Denominator.ToString(CultureInfo.InvariantCulture);
        Require(n.Length <= 128 && d.Length <= 128, "Fraction digit limit.");
        w.WriteStartObject(); w.WriteString("numerator", n); w.WriteString("denominator", d); w.WriteEndObject();
    }
    internal static Rational F(JsonElement p)
    {
        var n = S(p, "numerator"); var d = S(p, "denominator"); Require(n.Length <= 128 && d.Length <= 128, "Fraction digit limit.");
        var value = new Rational(BigInteger.Parse(n, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), BigInteger.Parse(d, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
        Require(value.Numerator.ToString(CultureInfo.InvariantCulture) == n && value.Denominator.ToString(CultureInfo.InvariantCulture) == d, "Fraction must be reduced, denominator positive, canonical decimal strings."); return value;
    }
    internal static void OptionalFraction(Utf8JsonWriter w, string key, Rational? f) { if (f.HasValue) Fraction(w, key, f.Value); else w.WriteNull(key); }
    internal static Rational? OptionalFraction(JsonElement p, string key) => p.GetProperty(key).ValueKind == JsonValueKind.Null ? null : F(p.GetProperty(key));
    internal static void Integer(Utf8JsonWriter w, string key, int value) => w.WriteString(key, value.ToString(CultureInfo.InvariantCulture));
    internal static int I(JsonElement p, string key) { var s = S(p, key); Require(s.Length <= 11, "Integer size limit."); var v = int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); Require(v.ToString(CultureInfo.InvariantCulture) == s, "Noncanonical integer."); return v; }
    internal static string S(JsonElement p, string key) { var v = p.GetProperty(key).GetString(); Require(v is not null && v.Length <= 4096, "Bounded string required."); return v!; }
    internal static T E<T>(JsonElement p, string key) where T : struct { var s = S(p, key); Require(Enum.TryParse<T>(s, false, out var v) && Enum.IsDefined(typeof(T), v) && v.ToString() == s, "Unsupported required kind: " + s); return v; }
    internal static JsonElement[] Items(JsonElement p, string key, int max) { var a = p.GetProperty(key); Require(a.ValueKind == JsonValueKind.Array && a.GetArrayLength() <= max, "Bounded array required: " + key); return a.EnumerateArray().ToArray(); }
    internal static void Array<T>(Utf8JsonWriter w, string key, IEnumerable<T> values, Action<Utf8JsonWriter, T> write) { w.WriteStartArray(key); foreach (var v in values) write(w, v); w.WriteEndArray(); }
    internal static void Header(JsonElement p, string format) { Require(S(p, "format") == format && S(p, "formatVersion") == "0.1", "Unsupported artifact format/version."); }
    internal static byte[] Bytes(Action<Utf8JsonWriter> write)
    { using var stream = new MemoryStream(); using (var w = new Utf8JsonWriter(stream)) write(w); var bytes = stream.ToArray(); Require(bytes.Length <= MaxDocumentBytes, "Oriented document size limit."); return bytes; }
    internal static JsonDocument Open(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length <= MaxDocumentBytes, "Bounded oriented UTF-8 JSON required.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; Duplicates(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    internal static void Duplicates(JsonElement p, ref int nodes)
    {
        Require(++nodes <= 65536, "JSON node limit.");
        if (p.ValueKind == JsonValueKind.Object) { var keys = new HashSet<string>(StringComparer.Ordinal); foreach (var x in p.EnumerateObject()) { Require(keys.Count < 64 && keys.Add(x.Name), "Duplicate/too many JSON properties."); Duplicates(x.Value, ref nodes); } }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var item in p.EnumerateArray()) Duplicates(item, ref nodes);
    }
    internal static T CheckedRead<T>(Func<T> read)
    {
        try { return read(); }
        catch (ArtifactFormatException) { throw; }
        catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidOperationException || e is KeyNotFoundException || e is ArgumentException || e is OverflowException || e is DivideByZeroException)
        { throw new ArtifactFormatException("Malformed oriented document: " + e.Message, e); }
    }
    internal static void Require(bool condition, string message) { if (!condition) throw new ArtifactFormatException(message); }
}
