using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Additive, bounded AOT codec. It does not widen or embed a second output in the 18A model.</summary>
public static class CanonicalOrientedTwoOutputJson
{
    public const string RequestFormat = "gear-invest.oriented-two-output-request";
    public const int MaxDocumentBytes = CanonicalOrientedJson.MaxDocumentBytes;
    public static byte[] WriteRequest(OrientedTwoOutputAssemblyRequest request) => CheckedRead(() =>
    {
        var bytes = Bytes(w => Request(w, request));
        using var doc = Open(bytes); Request(doc.RootElement); // Same structural limits for typed writers and readers.
        return bytes;
    });
    public static OrientedTwoOutputAssemblyRequest ReadRequest(byte[] bytes) => CheckedRead(() =>
    {
        using var doc = Open(bytes); var request = Request(doc.RootElement);
        Require(bytes.SequenceEqual(Bytes(w => Request(w, request))), "Noncanonical/unknown two-output request fields."); return request;
    });
    public static OrientedTwoOutputArtifactWriteResult Write(OrientedTwoOutputMechanism model, OrientedTwoOutputAssemblyRequest request) => CheckedRead(() =>
    {
        WriteRequest(request); var modelBytes = Bytes(w => Model(w, model)); using (var parsed = Open(modelBytes)) Model(parsed.RootElement);
        var validation = OrientedTwoOutputValidator.ValidateContext(request.ToEngineRequest(), model);
        var candidate = Hash(modelBytes); var preliminary = new OrientedTwoOutputArtifact(candidate, "", request, model, validation);
        var artifact = new OrientedTwoOutputArtifact(candidate, Hash(Document(preliminary, false)), request, model, validation);
        var bytes = Document(artifact, true); using (var parsed = Open(bytes)) Validation(parsed.RootElement.GetProperty("validation"));
        return new OrientedTwoOutputArtifactWriteResult(artifact, bytes);
    });
    public static OrientedTwoOutputArtifact Read(byte[] bytes) => CheckedRead(() =>
    {
        using var doc = Open(bytes); var p = doc.RootElement; CheckHeader(p, OrientedTwoOutputArtifact.Format, S(p.GetProperty("request"), "profile"));
        var artifact = new OrientedTwoOutputArtifact(S(p, "candidateId"), S(p, "artifactHash"), Request(p.GetProperty("request")), Model(p.GetProperty("mechanism")), Validation(p.GetProperty("validation")));
        Require(bytes.SequenceEqual(Document(artifact, true)), "Noncanonical/unknown two-output artifact fields."); return artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(OrientedTwoOutputArtifact artifact)
    {
        var candidate = Hash(Bytes(w => Model(w, artifact.Mechanism))); var hash = Hash(Document(artifact, false));
        return new ArtifactIdentityVerification(candidate, hash, candidate == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteResult(OrientedTwoOutputResult result) => Bytes(w =>
    { w.WriteStartObject(); w.WriteString("status", result.Status.ToString()); w.WriteBoolean("isSuccess", result.IsSuccess); w.WritePropertyName("validation"); Validation(w, result.Validation); w.WriteEndObject(); });
    public static byte[] WriteEvaluation(OrientedTwoOutputMechanism model, Rational rootTurns) => Bytes(w =>
    {
        var evaluation = OrientedTwoOutputComposer.Evaluate(model, rootTurns);
        w.WriteStartObject(); Fraction(w, "rootTurns", rootTurns);
        Array(w, "shafts", evaluation.Shafts, (a, s) =>
        {
            model.Solution.TryGetState(s.ShaftId, out var state);
            a.WriteStartObject(); a.WriteString("shaftId", s.ShaftId); Fraction(a, "turns", s.Turns); Fraction(a, "coefficient", state!.Coefficient);
            Vector(a, "positiveAxis", s.PositiveAxis); Vector(a, "worldAngularVelocityPerRoot", s.WorldAngularVelocityPerRoot); a.WriteEndObject();
        });
        Array(w, "outputs", evaluation.Outputs, (a, o) =>
        {
            a.WriteStartObject(); a.WriteString("key", o.Binding.Key); a.WriteString("role", o.Binding.Role.ToString()); a.WriteString("shaftId", o.Binding.ShaftId); a.WriteString("bodyId", o.Binding.BodyId); a.WriteString("portId", o.Binding.PortId);
            Fraction(a, "shaftTurns", o.ShaftTurns); Fraction(a, "shaftCoefficient", o.ShaftCoefficient); Fraction(a, "portTurns", o.PortTurns); Fraction(a, "portCoefficient", o.PortCoefficient);
            Fraction(a, "portCoordinateSign", o.PortCoordinateSign); Vector(a, "shaftPositiveAxis", o.ShaftPositiveAxis); Vector(a, "portPositiveAxis", o.PortPositiveAxis); Vector(a, "worldAngularVelocityPerRoot", o.WorldAngularVelocityPerRoot); a.WriteEndObject();
        }); w.WriteEndObject();
    });
    private static byte[] Document(OrientedTwoOutputArtifact a, bool withHash) => Bytes(w =>
    {
        w.WriteStartObject(); w.WriteString("format", OrientedTwoOutputArtifact.Format); w.WriteString("formatVersion", WireVersion(a.Request.Profile));
        if (a.Request.Profile == OrientedTwoOutputProfile.RefinedId) PitchClearanceJson.Policy(w);
        w.WriteString("candidateId", a.CandidateId);
        if (withHash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Request(w, a.Request); w.WritePropertyName("mechanism"); Model(w, a.Mechanism); w.WritePropertyName("validation"); Validation(w, a.StoredValidation); w.WriteEndObject();
    });
    private static void Request(Utf8JsonWriter w, OrientedTwoOutputAssemblyRequest r)
    {
        w.WriteStartObject(); w.WriteString("format", RequestFormat); w.WriteString("formatVersion", WireVersion(r.Profile)); w.WriteString("profile", r.Profile);
        if (r.Profile == OrientedTwoOutputProfile.RefinedId) PitchClearanceJson.Policy(w);
        w.WriteStartObject("bevel"); w.WriteString("profile", r.Bevel.Profile); Vector(w, "apex", r.Bevel.Apex); Mount(w, "input", r.Bevel.Input); Mount(w, "output", r.Bevel.Output);
        Fraction(w, "innerParameter", r.Bevel.InnerParameter); OptionalFraction(w, "requestedTransfer", r.Bevel.RequestedTransfer); w.WriteEndObject();
        Placement(w, "parallelBranch", r.ParallelBranch); Placement(w, "turnedBranch", r.TurnedBranch);
        Array(w, "outputs", r.Outputs, (a, o) => { a.WriteStartObject(); a.WriteString("key", o.Key); a.WriteString("role", o.Role.ToString()); a.WriteString("terminalBodyId", o.TerminalBodyId); a.WritePropertyName("terminalPort"); Port(a, o.TerminalPort); OptionalFraction(a, "requestedTransfer", o.RequestedTransfer); a.WriteEndObject(); });
        Frame(w, "assemblyPose", r.AssemblyPose); w.WriteBoolean("requireCrossComponentClearance", r.RequireCrossComponentClearance); Array(w, "keepOuts", r.KeepOuts, KeepOut); w.WriteEndObject();
    }
    private static OrientedTwoOutputAssemblyRequest Request(JsonElement p)
    {
        CheckHeader(p, RequestFormat, S(p, "profile")); var b = p.GetProperty("bevel");
        var bevel = new RightAngleBevelRequest(Vector(b.GetProperty("apex")), Mount(b.GetProperty("input")), Mount(b.GetProperty("output")), F(b.GetProperty("innerParameter")), OptionalFraction(b, "requestedTransfer"), S(b, "profile"));
        Require(bevel.Profile == OrientedTransmissionProfile.Id, "Unsupported bevel profile.");
        var a = Placement(p.GetProperty("parallelBranch")); Require(a is not null, "Mandatory parallel branch is absent.");
        var outputs = Items(p, "outputs", 2).Select(o => new OrientedOutputRequest(S(o, "key"), E<OrientedOutputRole>(o, "role"), S(o, "terminalBodyId"), Port(o.GetProperty("terminalPort")), OptionalFraction(o, "requestedTransfer"))).ToArray();
        Require(outputs.Length == 2 && UniqueIds(outputs.Select(o => o.Key).ToArray()) && outputs.Select(o => o.Role).Distinct().Count() == 2, "Exactly two unique keys and distinct roles required.");
        var keepOuts = Items(p, "keepOuts", 16).Select(KeepOut).ToArray(); Require(UniqueIds(keepOuts.Select(k => k.Id).ToArray()), "Duplicate keep-out ID.");
        return new OrientedTwoOutputAssemblyRequest(bevel, a!, outputs, Placement(p.GetProperty("turnedBranch")), Frame(p.GetProperty("assemblyPose")), p.GetProperty("requireCrossComponentClearance").GetBoolean(), keepOuts, S(p, "profile"));
    }
    /// <summary>The existing candidate identity payload, without request/provenance. Does not change the 19A wire format.</summary>
    public static byte[] WriteMechanicalPayload(OrientedTwoOutputMechanism model) => Bytes(w => Model(w, model));

    private static void Model(Utf8JsonWriter w, OrientedTwoOutputMechanism m)
    {
        w.WriteStartObject(); w.WriteString("profile", m.Profile); w.WriteString("rootShaftId", m.RootShaftId);
        if (m.Profile == OrientedTwoOutputProfile.RefinedId) PitchClearanceJson.Policy(w);
        Array(w, "outputs", m.Outputs, (a, o) => { a.WriteStartObject(); a.WriteString("key", o.Key); a.WriteString("role", o.Role.ToString()); a.WriteString("shaftId", o.ShaftId); a.WriteString("bodyId", o.BodyId); a.WriteString("portId", o.PortId); a.WriteEndObject(); });
        WriteGraphFields(w, m.Shafts, m.Bodies, m.Contacts, m.Ports, m.Connections, m.SourceMappings, m.Sources, m.Solution, m.RequireCrossComponentClearance, m.KeepOuts); w.WriteEndObject();
    }
    private static OrientedTwoOutputMechanism Model(JsonElement p)
    {
        Require(OrientedTwoOutputProfile.IsSupported(S(p, "profile")), "Unsupported two-output profile.");
        if (S(p, "profile") == OrientedTwoOutputProfile.RefinedId) PitchClearanceJson.CheckPolicy(p);
        var outputs = Items(p, "outputs", 2).Select(o => new OrientedOutputBinding(S(o, "key"), E<OrientedOutputRole>(o, "role"), S(o, "shaftId"), S(o, "bodyId"), S(o, "portId"))).ToArray();
        var shafts = Items(p, "shafts", OrientedTwoOutputProfile.MaxShafts).Select(Shaft).ToArray(); var ports = Items(p, "ports", OrientedTwoOutputProfile.MaxPorts).Select(Port).ToArray();
        var bodies = Items(p, "bodies", OrientedTwoOutputProfile.MaxBodies).Select(b => new OrientedGearBody(S(b, "id"), S(b, "shaftId"), E<OrientedGearKind>(b, "kind"), Frame(b.GetProperty("mountingFrame")), I(b, "teeth"), F(b.GetProperty("outerPitchRadius")), S(b, "sourceModuleId"))).ToArray();
        var contacts = Items(p, "contacts", OrientedTwoOutputProfile.MaxContacts).Select(c => new OrientedGearContact(S(c, "id"), E<OrientedContactKind>(c, "kind"), S(c, "bodyAId"), S(c, "bodyBId"), F(c.GetProperty("storedTransfer")), c.GetProperty("cone").ValueKind == JsonValueKind.Null ? null : Cone(c.GetProperty("cone")))).ToArray();
        var links = Items(p, "connections", 2).Select(c => { Require(S(c, "kind") == "RigidZeroPhase", "Unsupported connection."); return new ShaftPortConnection(S(c, "id"), S(c, "portAId"), S(c, "portBId"), F(c.GetProperty("coordinateTransfer"))); }).ToArray();
        var maps = Items(p, "sourceMappings", 32).Select(m => new SourceShaftMapping(S(m, "moduleId"), S(m, "sourceDofId"), S(m, "shaftId"), F(m.GetProperty("coordinateTransfer")))).ToArray();
        var sources = Items(p, "sources", 2).Select(s => new OrientedSourceIdentity(S(s, "moduleId"), S(s, "candidateId"), S(s, "artifactHash"))).ToArray();
        var solution = p.GetProperty("solution"); var states = Items(solution, "states", OrientedTwoOutputProfile.MaxShafts).Select(s => new DofKinematicState(S(s, "dofId"), F(s.GetProperty("coefficient")), F(s.GetProperty("phaseOffset")))).ToArray();
        var keepOuts = Items(p, "keepOuts", 16).Select(KeepOut).ToArray();
        foreach (var ids in new[] { shafts.Select(s => s.Id), ports.Select(s => s.Id), bodies.Select(s => s.Id), contacts.Select(s => s.Id), links.Select(s => s.Id), maps.Select(m => m.ModuleId + "/" + m.SourceDofId), sources.Select(s => s.ModuleId), states.Select(s => s.DofId), outputs.Select(o => o.Key), keepOuts.Select(k => k.Id) })
            Require(UniqueIds(ids.ToArray()), "Duplicate or unbounded two-output identifier.");
        Require(outputs.Length == 2 && outputs.Select(o => o.Role).Distinct().Count() == 2, "Exactly two typed output bindings required.");
        Require(shafts.Any(s => s.Id == S(p, "rootShaftId")) && S(solution, "rootDofId") == S(p, "rootShaftId") && states.Length == shafts.Length && states.All(s => shafts.Any(a => a.Id == s.DofId)) &&
            bodies.All(b => shafts.Any(s => s.Id == b.ShaftId)) && contacts.All(c => bodies.Any(b => b.Id == c.BodyAId) && bodies.Any(b => b.Id == c.BodyBId)) && ports.All(a => shafts.Any(s => s.Id == a.ShaftId)) &&
            links.All(c => ports.Any(a => a.Id == c.PortAId) && ports.Any(a => a.Id == c.PortBId)) && maps.All(m => shafts.Any(s => s.Id == m.ShaftId) && sources.Any(s => s.ModuleId == m.ModuleId)) &&
            outputs.All(o => shafts.Any(s => s.Id == o.ShaftId) && bodies.Any(b => b.Id == o.BodyId) && ports.Any(a => a.Id == o.PortId)), "Unresolved reference or incomplete solution coverage.");
        return new OrientedTwoOutputMechanism(S(p, "rootShaftId"), outputs, shafts, bodies, contacts, ports, links, maps, sources, new KinematicSolution(S(solution, "rootDofId"), states), p.GetProperty("requireCrossComponentClearance").GetBoolean(), keepOuts, S(p, "profile"));
    }
    private static bool UniqueIds(string[] ids) => ids.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 256) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length;
    public static string WireVersion(string profile) => profile == OrientedTwoOutputProfile.Id ? "0.1" : profile == OrientedTwoOutputProfile.RefinedId ? "0.2" : throw new ArtifactFormatException("Unsupported two-output profile.");
    private static void CheckHeader(JsonElement p, string format, string profile)
    {
        Require(S(p, "format") == format && S(p, "formatVersion") == WireVersion(profile), "Unsupported/mismatched two-output version/profile.");
        if (profile == OrientedTwoOutputProfile.RefinedId) PitchClearanceJson.CheckPolicy(p);
    }
    private static void Validation(Utf8JsonWriter w, OrientedValidation v) => PitchClearanceJson.Validation(w, v);
    private static OrientedValidation Validation(JsonElement p) => PitchClearanceJson.Validation(p);
}
