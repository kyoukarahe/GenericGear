using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

/// <summary>Strict, bounded, reflection-free authored cam definitions. Derived proof and numeric requests are separate.</summary>
public static partial class CamFollowerJson
{
    // Local authored declaration only; the 30A engine supplies source context separately.
    internal static void AssemblyDeclaration(Utf8JsonWriter w, AssemblyCamFollowerDeclaration d)
    {
        w.WriteStartObject(); w.WriteString("profile", CamFollowerProfile.Id); w.WriteString("targetBasis", d.TargetBasis.ToString());
        w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("output"); Output(w, d.Output); w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static AssemblyCamFollowerDeclaration AssemblyDeclaration(JsonElement p)
    {
        Require(S(p, "profile") == CamFollowerProfile.Id, "Unsupported assembly member profile.");
        return new AssemblyCamFollowerDeclaration(Device(p.GetProperty("device")),
            Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!), E<AssemblyTargetBasis>(p, "targetBasis"));
    }

    public const string DraftFormat = "gear-invest.cam-follower-draft", BatchFormat = "gear-invest.cam-follower-edit-batch",
        SessionFormat = "gear-invest.cam-follower-edit-session", AnalysisFormat = "gear-invest.cam-follower-analysis";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxDocumentDepth = 48, MaxDocumentNodes = 131072, MaxObjectKeys = 64;
    public static byte[] WriteDraft(CamFollowerDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static CamFollowerDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown cam-follower draft fields."); return draft; });
    public static byte[] WriteDefinition(CamFollowerDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    public static byte[] WriteSupportProfile(CamSupportProfile profile) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.cam-support-profile"); w.WritePropertyName("supportProfile"); SupportProfile(w, profile); w.WriteEndObject(); }));
    public static CamSupportProfile ReadSupportProfile(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.cam-support-profile"); var p = SupportProfile(doc.RootElement.GetProperty("supportProfile"));
        Require(bytes.SequenceEqual(WriteSupportProfile(p)), "Noncanonical cam support profile."); return p;
    });
    internal static void Draft(Utf8JsonWriter w, CamFollowerDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static CamFollowerDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new CamFollowerDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Cam-follower draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, CamFollowerDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", CamFollowerProfile.Id); w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); R.Mapping(w, d.SourceMapping); w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("output"); Output(w, d.Output); w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static CamFollowerDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == CamFollowerProfile.Id, "Unsupported cam-follower profile.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), R.Mapping(p.GetProperty("sourceMapping")), Device(p.GetProperty("device")),
            Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")), Items(p, "requiredValidationDomains", 24).Select(x => x.GetString()!));
    }
    private static void SupportProfile(Utf8JsonWriter w, CamSupportProfile p)
    {
        w.WriteStartObject(); w.WriteString("geometryPolicy", CamSupportProfile.GeometryPolicy); w.WriteString("profileId", p.ProfileId);
        w.WriteString("parameter", "outward-material-normal-turns"); Array(w, "segments", p.Segments, Segment); w.WriteEndObject();
    }
    private static CamSupportProfile SupportProfile(JsonElement p)
    {
        Require(S(p, "geometryPolicy") == CamSupportProfile.GeometryPolicy && S(p, "parameter") == "outward-material-normal-turns", "Unsupported support semantics.");
        var profile = new CamSupportProfile(Items(p, "segments", CamSupportProfile.MaximumSegments).Select(Segment));
        Require(profile.ProfileId == S(p, "profileId"), "Support geometry identity mismatch."); return profile;
    }
    private static void Segment(Utf8JsonWriter w, CamSupportSegment s)
    {
        w.WriteStartObject(); w.WriteString("id", s.Id); Fraction(w, "startTurns", s.StartTurns); Fraction(w, "endTurns", s.EndTurns);
        R.Quantity(w, "startHeight", s.StartHeight); R.Quantity(w, "endHeight", s.EndHeight); w.WriteString("kind", s.Kind.ToString()); w.WriteEndObject();
    }
    private static CamSupportSegment Segment(JsonElement p) => new(S(p, "id"), F(p.GetProperty("startTurns")), F(p.GetProperty("endTurns")),
        R.Quantity(p.GetProperty("startHeight")), R.Quantity(p.GetProperty("endHeight")), E<CamSupportSegmentKind>(p, "kind"));
    private static void Device(Utf8JsonWriter w, FlatCamFollowerDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("sourceShaftId", d.SourceShaftId); w.WriteString("sourcePortId", d.SourcePortId);
        w.WriteString("camBodyId", d.CamBodyId); w.WriteString("followerBodyId", d.FollowerBodyId); w.WriteString("guideId", d.GuideId);
        w.WriteString("linearDofId", d.LinearDofId); w.WriteString("followerReferenceId", d.FollowerReferenceId);
        w.WritePropertyName("supportProfile"); SupportProfile(w, d.SupportProfile); R.LengthVector(w, "centerMm", d.CenterMm);
        R.Quantity(w, "camStation", d.CamStation); Vector(w, "planeNormal", d.PlaneNormal); LengthFrame(w, "guideFrameMm", d.GuideFrameMm);
        R.Quantity(w, "mountingTurns", d.MountingTurns); R.Interval(w, "guideTravel", d.GuideTravel); R.Interval(w, "followerFace", d.FollowerFace);
        w.WriteBoolean("contactPresent", d.ContactPresent); w.WriteString("contactPolicy", d.ContactPolicy); w.WriteBoolean("guidePresent", d.GuidePresent);
        w.WriteBoolean("followerRotationFixed", d.FollowerRotationFixed); w.WriteBoolean("transverseMotionFixed", d.TransverseMotionFixed);
        w.WriteBoolean("camAxisFixed", d.CamAxisFixed); w.WriteBoolean("followerIsPrescribed", d.FollowerIsPrescribed); w.WriteString("followerKind", d.FollowerKind); w.WriteEndObject();
    }
    private static FlatCamFollowerDefinition Device(JsonElement p) => new(S(p, "id"), S(p, "sourceShaftId"), S(p, "camBodyId"), S(p, "followerBodyId"),
        S(p, "guideId"), S(p, "linearDofId"), S(p, "followerReferenceId"), SupportProfile(p.GetProperty("supportProfile")), R.LengthVector(p.GetProperty("centerMm")),
        R.Quantity(p.GetProperty("camStation")), Vector(p.GetProperty("planeNormal")), LengthFrame(p.GetProperty("guideFrameMm")), R.Quantity(p.GetProperty("mountingTurns")),
        R.Interval(p.GetProperty("guideTravel")), R.Interval(p.GetProperty("followerFace")), p.GetProperty("contactPresent").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "contactPolicy"),
        p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("followerRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean(), p.GetProperty("camAxisFixed").GetBoolean(),
        MechanicalAuthoringJson.NullableString(p, "sourcePortId"), p.GetProperty("followerIsPrescribed").GetBoolean(), S(p, "followerKind"));
    private static void Output(Utf8JsonWriter w, PrismaticOutputDefinition o)
    {
        w.WriteStartObject(); w.WriteString("kind", "LinearPosition"); w.WriteString("key", o.Key); w.WriteString("linearDofId", o.LinearDofId);
        w.WriteString("bodyId", o.BodyId); w.WriteString("referencePointId", o.ReferencePointId); Integer(w, "terminalSign", o.TerminalSign); R.Quantity(w, "terminalDatum", o.TerminalDatum); w.WriteEndObject();
    }
    private static PrismaticOutputDefinition Output(JsonElement p)
    {
        Require(S(p, "kind") == "LinearPosition", "DimensionMismatch: prismatic output kind required.");
        return new(S(p, "key"), S(p, "linearDofId"), S(p, "bodyId"), S(p, "referencePointId"), I(p, "terminalSign"), R.Quantity(p.GetProperty("terminalDatum")));
    }
    private static void Requirement(Utf8JsonWriter w, CamFollowerRequirement r)
    {
        w.WriteStartObject(); OptionalQuantity(w, "requiredStroke", r.RequiredStroke); OptionalQuantity(w, "requiredReferencePosition", r.RequiredReferencePosition);
        R.Quantity(w, "referenceRoot", r.ReferenceRoot); w.WriteBoolean("positiveStrokeRequired", r.PositiveStrokeRequired); w.WriteEndObject();
    }
    private static CamFollowerRequirement Requirement(JsonElement p) => new(OptionalQuantity(p, "requiredStroke"), OptionalQuantity(p, "requiredReferencePosition"),
        R.Quantity(p.GetProperty("referenceRoot")), p.GetProperty("positiveStrokeRequired").GetBoolean());
    private static void OptionalQuantity(Utf8JsonWriter w, string key, ExactQuantity? q) { if (q.HasValue) R.Quantity(w, key, q.Value); else w.WriteNull(key); }
    private static ExactQuantity? OptionalQuantity(JsonElement p, string key) => p.GetProperty(key).ValueKind == JsonValueKind.Null ? (ExactQuantity?)null : R.Quantity(p.GetProperty(key));
    private static void LengthFrame(Utf8JsonWriter w, string key, OrientedFrame f)
    { w.WriteStartObject(key); w.WriteString("originUnit", "mm"); Frame(w, "frame", f); w.WriteEndObject(); }
    private static OrientedFrame LengthFrame(JsonElement p)
    { Require(S(p, "originUnit") == "mm", "Explicit mm frame origin required."); return MechanicalAuthoringJson.LooseFrame(p.GetProperty("frame")); }
    private static void Start(Utf8JsonWriter w, string format) => MechanicalAuthoringJson.Start(w, format);
    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) write(writer);
        var bytes = stream.ToArray(); Require(bytes.Length <= MaxDocumentBytes, "Cam-follower document byte limit exceeded."); using var parsed = Parse(bytes); return bytes;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length > 0 && bytes.Length <= MaxDocumentBytes, "Cam-follower document byte limit exceeded.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = MaxDocumentDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; CheckNodes(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void CheckNodes(JsonElement p, ref int nodes)
    {
        Require(++nodes <= MaxDocumentNodes, "Cam-follower JSON node limit exceeded.");
        if (p.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in p.EnumerateObject())
            { Require(keys.Count < MaxObjectKeys && keys.Add(property.Name), "Duplicate or excessive cam-follower object keys."); CheckNodes(property.Value, ref nodes); }
        }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var child in p.EnumerateArray()) CheckNodes(child, ref nodes);
    }
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
