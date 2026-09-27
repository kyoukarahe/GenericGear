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

/// <summary>Bounded additive crank-slider declarations. Caller numerical precision is not a mechanical declaration.</summary>
public static partial class CrankSliderJson
{
    // Local authored declaration only; the 30A engine supplies source context separately.
    internal static void AssemblyDeclaration(Utf8JsonWriter w, AssemblyCrankSliderDeclaration d)
    {
        w.WriteStartObject(); w.WriteString("profile", CrankSliderProfile.Id); w.WriteString("targetBasis", d.TargetBasis.ToString());
        w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("output"); Output(w, d.Output); w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static AssemblyCrankSliderDeclaration AssemblyDeclaration(JsonElement p)
    {
        Require(S(p, "profile") == CrankSliderProfile.Id, "Unsupported assembly member profile.");
        return new AssemblyCrankSliderDeclaration(Device(p.GetProperty("device")),
            Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!), E<AssemblyTargetBasis>(p, "targetBasis"));
    }

    public const string DraftFormat = "gear-invest.crank-slider-draft", BatchFormat = "gear-invest.crank-slider-edit-batch",
        SessionFormat = "gear-invest.crank-slider-edit-session", AnalysisFormat = "gear-invest.crank-slider-analysis";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxDocumentDepth = 48, MaxDocumentNodes = 131072, MaxObjectKeys = 64;

    public static byte[] WriteDraft(CrankSliderDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static CrankSliderDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown crank-slider draft fields."); return draft; });
    public static byte[] WriteDefinition(CrankSliderDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    internal static void Draft(Utf8JsonWriter w, CrankSliderDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static CrankSliderDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new CrankSliderDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Crank-slider draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, CrankSliderDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", CrankSliderProfile.Id);
        w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); R.Mapping(w, d.SourceMapping);
        w.WritePropertyName("device"); Device(w, d.Device); w.WritePropertyName("output"); Output(w, d.Output);
        w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static CrankSliderDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == CrankSliderProfile.Id, "Unsupported crank-slider profile.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), R.Mapping(p.GetProperty("sourceMapping")),
            Device(p.GetProperty("device")), Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")),
            Items(p, "requiredValidationDomains", 24).Select(x => x.GetString()!));
    }
    private static void Device(Utf8JsonWriter w, PlanarCrankSliderDefinition d)
    {
        w.WriteStartObject(); w.WriteString("id", d.Id); w.WriteString("sourceShaftId", d.SourceShaftId); w.WriteString("sourcePortId", d.SourcePortId);
        w.WriteString("crankBodyId", d.CrankBodyId); w.WriteString("rodBodyId", d.RodBodyId); w.WriteString("sliderBodyId", d.SliderBodyId);
        w.WriteString("guideId", d.GuideId); w.WriteString("linearDofId", d.LinearDofId); w.WriteString("crankPinId", d.CrankPinId); w.WriteString("sliderPinId", d.SliderPinId);
        R.Quantity(w, "crankRadius", d.CrankRadius); R.Quantity(w, "rodLength", d.RodLength); R.LengthVector(w, "pivotMm", d.PivotMm);
        R.Quantity(w, "pivotStation", d.PivotStation); Vector(w, "planeNormal", d.PlaneNormal); LengthFrame(w, "guideFrameMm", d.GuideFrameMm);
        R.Quantity(w, "mountingTurns", d.MountingTurns); if (d.AssemblyBranch.HasValue) Integer(w, "assemblyBranch", d.AssemblyBranch.Value); else w.WriteNull("assemblyBranch");
        R.Interval(w, "guideTravel", d.GuideTravel); w.WriteBoolean("transmissionPresent", d.TransmissionPresent); w.WriteBoolean("guidePresent", d.GuidePresent);
        w.WriteBoolean("sliderRotationFixed", d.SliderRotationFixed); w.WriteBoolean("transverseMotionFixed", d.TransverseMotionFixed); w.WriteBoolean("crankAxisFixed", d.CrankAxisFixed);
        w.WriteBoolean("sliderIsPrescribed", d.SliderIsPrescribed); w.WriteBoolean("crankPinPresent", d.CrankPinPresent); w.WriteBoolean("sliderPinPresent", d.SliderPinPresent); w.WriteEndObject();
    }
    private static PlanarCrankSliderDefinition Device(JsonElement p) => new(S(p, "id"), S(p, "sourceShaftId"), S(p, "crankBodyId"), S(p, "rodBodyId"), S(p, "sliderBodyId"),
        S(p, "guideId"), S(p, "linearDofId"), S(p, "crankPinId"), S(p, "sliderPinId"), R.Quantity(p.GetProperty("crankRadius")), R.Quantity(p.GetProperty("rodLength")),
        R.LengthVector(p.GetProperty("pivotMm")), R.Quantity(p.GetProperty("pivotStation")), Vector(p.GetProperty("planeNormal")), LengthFrame(p.GetProperty("guideFrameMm")),
        R.Quantity(p.GetProperty("mountingTurns")), p.GetProperty("assemblyBranch").ValueKind == JsonValueKind.Null ? (int?)null : I(p, "assemblyBranch"), R.Interval(p.GetProperty("guideTravel")),
        p.GetProperty("transmissionPresent").GetBoolean(), p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("sliderRotationFixed").GetBoolean(),
        p.GetProperty("transverseMotionFixed").GetBoolean(), p.GetProperty("crankAxisFixed").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "sourcePortId"),
        p.GetProperty("sliderIsPrescribed").GetBoolean(), p.GetProperty("crankPinPresent").GetBoolean(), p.GetProperty("sliderPinPresent").GetBoolean());
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
    private static void Requirement(Utf8JsonWriter w, CrankSliderRequirement r)
    {
        w.WriteStartObject(); OptionalQuantity(w, "requiredStroke", r.RequiredStroke); OptionalQuantity(w, "requiredReferencePosition", r.RequiredReferencePosition);
        R.Quantity(w, "referenceRoot", r.ReferenceRoot); w.WriteEndObject();
    }
    private static CrankSliderRequirement Requirement(JsonElement p) => new(OptionalQuantity(p, "requiredStroke"), OptionalQuantity(p, "requiredReferencePosition"), R.Quantity(p.GetProperty("referenceRoot")));
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
        var bytes = stream.ToArray(); Require(bytes.Length <= MaxDocumentBytes, "Crank-slider document byte limit exceeded."); using var parsed = Parse(bytes); return bytes;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length > 0 && bytes.Length <= MaxDocumentBytes, "Crank-slider document byte limit exceeded.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = MaxDocumentDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; CheckNodes(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void CheckNodes(JsonElement p, ref int nodes)
    {
        Require(++nodes <= MaxDocumentNodes, "Crank-slider JSON node limit exceeded.");
        if (p.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in p.EnumerateObject())
            { Require(keys.Count < MaxObjectKeys && keys.Add(property.Name), "Duplicate or excessive crank-slider object keys."); CheckNodes(property.Value, ref nodes); }
        }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var child in p.EnumerateArray()) CheckNodes(child, ref nodes);
    }
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
