using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Bounded reflection-free draft sidecars. Parsing does not approve mechanics or cached analysis.</summary>
public static partial class MechanicalAuthoringJson
{
    public const string DraftFormat = "gear-invest.mechanical-draft";
    public const string BatchFormat = "gear-invest.mechanical-edit-batch";
    public const string SessionFormat = "gear-invest.mechanical-edit-session";
    public const string AnalysisFormat = "gear-invest.mechanical-analysis";
    public const string ComparisonFormat = "gear-invest.output-motion-comparison";
    public const string EditResultFormat = "gear-invest.mechanical-edit-result";
    public const string Version = "0.1";
    public const string CoaxialVersion = "0.2";
    internal static string VersionFor(MechanicalDefinition d) => d.CoaxialLayout is null ? Version : CoaxialVersion;

    public static byte[] WriteDraft(MechanicalDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static MechanicalDraft ReadDraft(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var draft = Draft(doc.RootElement);
        Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown mechanical draft fields."); return draft;
    });
    public static byte[] WriteDefinition(MechanicalDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));

    internal static void Draft(Utf8JsonWriter w, MechanicalDraft draft)
    {
        Start(w, DraftFormat, VersionFor(draft.Definition)); w.WriteString("draftId", draft.DraftId); w.WriteString("definitionId", draft.DefinitionId);
        Long(w, "revision", draft.Revision); w.WritePropertyName("definition"); Definition(w, draft.Definition);
        w.WriteStartObject("importProvenance");
        if (draft.OriginalArtifactBytes is null) w.WriteNull("originalArtifactUtf8"); else w.WriteBase64String("originalArtifactUtf8", draft.OriginalArtifactBytes);
        w.WriteString("originalArtifactIdentity", draft.OriginalArtifactIdentity); w.WriteString("originalDefinitionId", draft.OriginalDefinitionId);
        w.WriteString("importedProfile", draft.ImportedProfile); w.WriteEndObject(); w.WriteEndObject();
    }
    internal static MechanicalDraft Draft(JsonElement p)
    {
        var definition = Definition(p.GetProperty("definition"));
        Require(S(p, "format") == DraftFormat && S(p, "formatVersion") == VersionFor(definition), "Unsupported mechanical draft format/profile version.");
        var provenance = p.GetProperty("importProvenance");
        var raw = provenance.GetProperty("originalArtifactUtf8");
        var draft = new MechanicalDraft(definition, Long(p, "revision"),
            raw.ValueKind == JsonValueKind.Null ? null : raw.GetBytesFromBase64(), NullableString(provenance, "originalArtifactIdentity"),
            NullableString(provenance, "originalDefinitionId"), NullableString(provenance, "importedProfile"));
        Require(draft.DraftId == S(p, "draftId") && draft.DefinitionId == S(p, "definitionId"), "Draft or definition identity mismatch."); return draft;
    }
    internal static void Definition(Utf8JsonWriter w, MechanicalDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", d.Profile); w.WriteString("rootShaftId", d.RootShaftId);
        Array(w, "shafts", d.Shafts, WriteShaft); Array(w, "bodies", d.Bodies, Body); Array(w, "contacts", d.Contacts, Contact);
        Array(w, "ports", d.Ports, WritePort); Array(w, "connections", d.Connections, Connection); Array(w, "outputs", d.Outputs, Output);
        Array(w, "keepOuts", d.KeepOuts, KeepOut); w.WriteString("clearancePolicy", d.ClearancePolicy);
        w.WriteBoolean("requireCrossComponentClearance", d.RequireCrossComponentClearance);
        if (d.CoaxialLayout is not null) { w.WritePropertyName("coaxialLayout"); CoaxialLayout(w, d.CoaxialLayout); }
        w.WriteEndObject();
    }
    internal static MechanicalDefinition Definition(JsonElement p)
    {
        if (S(p, "profile") == ParallelCoaxialLayout.Profile)
            return new MechanicalDefinition(CoaxialLayout(p.GetProperty("coaxialLayout")), NullableString(p, "rootShaftId"),
                Items(p, "shafts", MechanicalAuthoringProfile.MaxShafts).Select(ReadShaft), Items(p, "bodies", MechanicalAuthoringProfile.MaxBodies).Select(Body),
                Items(p, "contacts", MechanicalAuthoringProfile.MaxContacts).Select(Contact), Items(p, "ports", MechanicalAuthoringProfile.MaxPorts).Select(ReadPort),
                Items(p, "connections", MechanicalAuthoringProfile.MaxConnections).Select(Connection), Items(p, "outputs", MechanicalAuthoringProfile.MaxOutputs).Select(Output),
                Items(p, "keepOuts", MechanicalAuthoringProfile.MaxKeepOuts).Select(KeepOut), S(p, "clearancePolicy"), p.GetProperty("requireCrossComponentClearance").GetBoolean());
        Require(S(p, "profile") == MechanicalAuthoringProfile.Id, "Unsupported mechanical authoring profile.");
        return new MechanicalDefinition(NullableString(p, "rootShaftId"), Items(p, "shafts", MechanicalAuthoringProfile.MaxShafts).Select(ReadShaft),
            Items(p, "bodies", MechanicalAuthoringProfile.MaxBodies).Select(Body), Items(p, "contacts", MechanicalAuthoringProfile.MaxContacts).Select(Contact),
            Items(p, "ports", MechanicalAuthoringProfile.MaxPorts).Select(ReadPort), Items(p, "connections", MechanicalAuthoringProfile.MaxConnections).Select(Connection),
            Items(p, "outputs", MechanicalAuthoringProfile.MaxOutputs).Select(Output), Items(p, "keepOuts", MechanicalAuthoringProfile.MaxKeepOuts).Select(KeepOut),
            S(p, "clearancePolicy"), p.GetProperty("requireCrossComponentClearance").GetBoolean());
    }
    internal static void CoaxialLayout(Utf8JsonWriter w, ParallelCoaxialLayout layout)
    {
        w.WriteStartObject(); Frame(w, "planeFrame", layout.PlaneFrame);
        Fraction(w, "pitchRadiusPerTooth", layout.PitchRadiusPerTooth); Fraction(w, "layerSpacing", layout.LayerSpacing); Integer(w, "layerCount", layout.LayerCount);
        Array(w, "groups", layout.Groups, (x, g) => { x.WriteStartObject(); x.WriteString("id", g.Id); Strings(x, "shaftIds", g.ShaftIds); x.WriteEndObject(); });
        w.WriteEndObject();
    }
    internal static ParallelCoaxialLayout CoaxialLayout(JsonElement p) => new(LooseFrame(p.GetProperty("planeFrame")),
        F(p.GetProperty("pitchRadiusPerTooth")), F(p.GetProperty("layerSpacing")), I(p, "layerCount"),
        Items(p, "groups", ParallelCoaxialLayout.MaxGroups).Select(g => new CoaxialPlacementGroup(S(g, "id"),
            Items(g, "shaftIds", ParallelCoaxialLayout.MaxRotors).Select(x => x.GetString()!))));
    internal static void Body(Utf8JsonWriter w, OrientedGearBody b)
    {
        w.WriteStartObject(); w.WriteString("id", b.Id); w.WriteString("shaftId", b.ShaftId); w.WriteString("kind", b.Kind.ToString());
        Frame(w, "mountingFrame", b.MountingFrame); Integer(w, "teeth", b.Teeth); Fraction(w, "outerPitchRadius", b.OuterPitchRadius);
        w.WriteString("sourceModuleId", b.SourceModuleId); w.WriteEndObject();
    }
    internal static OrientedGearBody Body(JsonElement p) => new(S(p, "id"), S(p, "shaftId"), E<OrientedGearKind>(p, "kind"), LooseFrame(p.GetProperty("mountingFrame")), I(p, "teeth"), F(p.GetProperty("outerPitchRadius")), S(p, "sourceModuleId"));
    internal static void Contact(Utf8JsonWriter w, MechanicalContact c)
    {
        w.WriteStartObject(); w.WriteString("id", c.Id); w.WriteString("kind", c.Kind.ToString()); w.WriteString("bodyAId", c.BodyAId); w.WriteString("bodyBId", c.BodyBId);
        w.WritePropertyName("cone"); if (c.Cone is null) w.WriteNullValue(); else DraftCone(w, c.Cone); w.WriteEndObject();
    }
    internal static MechanicalContact Contact(JsonElement p) => new(S(p, "id"), E<OrientedContactKind>(p, "kind"), S(p, "bodyAId"), S(p, "bodyBId"), p.GetProperty("cone").ValueKind == JsonValueKind.Null ? null : DraftCone(p.GetProperty("cone")));
    private static void DraftCone(Utf8JsonWriter w, RightAnglePitchCone c)
    { w.WriteStartObject(); Vector(w, "apex", c.Apex); Vector(w, "outwardA", c.OutwardA); Vector(w, "outwardB", c.OutwardB); Vector(w, "outerContact", c.OuterContact); Fraction(w, "innerParameter", c.InnerParameter); Fraction(w, "outerScaleA", c.OuterScaleA); Fraction(w, "outerScaleB", c.OuterScaleB); w.WriteEndObject(); }
    private static RightAnglePitchCone DraftCone(JsonElement p) => new(Vector(p.GetProperty("apex")), Vector(p.GetProperty("outwardA")), Vector(p.GetProperty("outwardB")), Vector(p.GetProperty("outerContact")), F(p.GetProperty("innerParameter")), F(p.GetProperty("outerScaleA")), F(p.GetProperty("outerScaleB")));
    internal static void Connection(Utf8JsonWriter w, ShaftPortConnection c)
    { w.WriteStartObject(); w.WriteString("id", c.Id); w.WriteString("kind", "RigidZeroPhase"); w.WriteString("portAId", c.PortAId); w.WriteString("portBId", c.PortBId); Fraction(w, "coordinateTransfer", c.CoordinateTransfer); w.WriteEndObject(); }
    internal static ShaftPortConnection Connection(JsonElement p)
    { Require(S(p, "kind") == "RigidZeroPhase", "Unsupported port connection kind."); return new(S(p, "id"), S(p, "portAId"), S(p, "portBId"), F(p.GetProperty("coordinateTransfer"))); }
    internal static void Output(Utf8JsonWriter w, MechanicalOutput o)
    {
        w.WriteStartObject(); w.WriteString("key", o.Key); w.WriteString("shaftId", o.ShaftId); w.WriteString("bodyId", o.BodyId); w.WriteString("portId", o.PortId);
        OptionalFraction(w, "requiredTransfer", o.RequiredTransfer); w.WriteString("role", o.Role?.ToString()); w.WriteString("unresolvedReason", o.UnresolvedReason);
        Array(w, "formerEndpoint", o.FormerEndpoint, Reference); w.WriteEndObject();
    }
    internal static MechanicalOutput Output(JsonElement p) => new(S(p, "key"), NullableString(p, "shaftId"), NullableString(p, "bodyId"), NullableString(p, "portId"), OptionalFraction(p, "requiredTransfer"),
        p.GetProperty("role").ValueKind == JsonValueKind.Null ? null : E<OrientedOutputRole>(p, "role"), NullableString(p, "unresolvedReason"), Items(p, "formerEndpoint", 3).Select(Reference));
    internal static void Reference(Utf8JsonWriter w, MechanicalReference r) { w.WriteStartObject(); w.WriteString("kind", r.Kind); w.WriteString("id", r.Id); w.WriteEndObject(); }
    internal static MechanicalReference Reference(JsonElement p) => new(S(p, "kind"), S(p, "id"));
    internal static void WriteShaft(Utf8JsonWriter w, OrientedShaft s) => Shaft(w, s);
    internal static OrientedShaft ReadShaft(JsonElement p) => new(S(p, "id"), LooseFrame(p.GetProperty("frame")), p.GetProperty("isPrescribed").GetBoolean());
    internal static void WritePort(Utf8JsonWriter w, ShaftPort p) => Port(w, p);
    internal static ShaftPort ReadPort(JsonElement p) => new(S(p, "id"), S(p, "shaftId"), LooseFrame(p.GetProperty("frame")), F(p.GetProperty("phaseOffset")), E<ShaftConnectionKind>(p, "kind"));
    // Bad mechanical frames are typed analyzable input, unlike malformed vector/fraction encoding.
    internal static OrientedFrame LooseFrame(JsonElement p) => new(Vector(p.GetProperty("origin")), Vector(p.GetProperty("x")), Vector(p.GetProperty("y")), Vector(p.GetProperty("z")));
    internal static string? NullableString(JsonElement p, string key) => p.GetProperty(key).ValueKind == JsonValueKind.Null ? null : S(p, key);
    internal static void Long(Utf8JsonWriter w, string key, long value) => w.WriteString(key, value.ToString(CultureInfo.InvariantCulture));
    internal static long Long(JsonElement p, string key)
    { var s = S(p, key); Require(s.Length <= 20, "Integer bound exceeded."); var n = long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); Require(n.ToString(CultureInfo.InvariantCulture) == s, "Noncanonical integer."); return n; }
    internal static void Start(Utf8JsonWriter w, string format, string version = Version) { w.WriteStartObject(); w.WriteString("format", format); w.WriteString("formatVersion", version); }
    internal static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); using (var w = new Utf8JsonWriter(stream)) write(w); var bytes = stream.ToArray();
        Require(bytes.Length <= MechanicalAuthoringProfile.MaxDocumentBytes, "Mechanical sidecar document byte limit exceeded.");
        // Typed writers must not emit a sidecar that the bounded reader cannot admit.
        using var parsed = Parse(bytes); return bytes;
    }
    internal static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length <= MechanicalAuthoringProfile.MaxDocumentBytes, "Bounded mechanical sidecar required.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = 40, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; CheckNodes(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void CheckNodes(JsonElement p, ref int nodes)
    {
        Require(++nodes <= 131072, "Mechanical sidecar JSON node limit exceeded.");
        if (p.ValueKind == JsonValueKind.Object)
        { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var property in p.EnumerateObject()) { Require(seen.Count < 64 && seen.Add(property.Name), "Duplicate or excessive object keys."); CheckNodes(property.Value, ref nodes); } }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var child in p.EnumerateArray()) CheckNodes(child, ref nodes);
    }
    internal static T Guard<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArtifactFormatException) { throw; }
        catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidOperationException || e is KeyNotFoundException || e is ArgumentException || e is OverflowException || e is DivideByZeroException)
        { throw new ArtifactFormatException("Malformed mechanical authoring sidecar: " + e.Message, e); }
    }
}
