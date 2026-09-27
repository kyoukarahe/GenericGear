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

/// <summary>Strict, bounded, reflection-free authored Geneva definitions. Derived proof and numeric requests are separate.</summary>
public static partial class GenevaJson
{
    // Local authored declaration only; the 30A engine supplies source context separately.
    internal static void AssemblyDeclaration(Utf8JsonWriter w, AssemblyGenevaDeclaration d)
    {
        w.WriteStartObject(); w.WriteString("profile", GenevaProfile.Id); w.WriteString("targetBasis", d.TargetBasis.ToString());
        w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("output"); Output(w, d.Output); w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    internal static AssemblyGenevaDeclaration AssemblyDeclaration(JsonElement p)
    {
        Require(S(p, "profile") == GenevaProfile.Id, "Unsupported assembly member profile.");
        return new AssemblyGenevaDeclaration(Device(p.GetProperty("device")),
            Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!), E<AssemblyTargetBasis>(p, "targetBasis"));
    }

    public const string DraftFormat = "gear-invest.geneva-draft", BatchFormat = "gear-invest.geneva-edit-batch",
        SessionFormat = "gear-invest.geneva-edit-session", AnalysisFormat = "gear-invest.geneva-analysis";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxDocumentDepth = 48, MaxDocumentNodes = 131072, MaxObjectKeys = 64;
    public static byte[] WriteDraft(GenevaDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static GenevaDraft ReadDraft(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var draft = Draft(doc.RootElement); Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown geneva draft fields."); return draft; });
    public static byte[] WriteDefinition(GenevaDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    internal static void Draft(Utf8JsonWriter w, GenevaDraft d)
    {
        Start(w, DraftFormat); w.WriteString("draftId", d.DraftId); w.WriteString("definitionId", d.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", d.Revision); w.WritePropertyName("definition"); Definition(w, d.Definition); w.WriteEndObject();
    }
    internal static GenevaDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat); var d = new GenevaDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(d.DraftId == S(p, "draftId") && d.DefinitionId == S(p, "definitionId"), "Geneva draft/definition identity mismatch."); return d;
    }
    private static void Definition(Utf8JsonWriter w, GenevaDefinition d)
    {
        w.WriteStartObject(); w.WriteString("profile", GenevaProfile.Id); w.WritePropertyName("source"); MechanicalAuthoringJson.Draft(w, d.Source);
        w.WritePropertyName("sourceMapping"); R.Mapping(w, d.SourceMapping); w.WritePropertyName("device"); Device(w, d.Device);
        w.WritePropertyName("output"); Output(w, d.Output); w.WritePropertyName("requirement"); Requirement(w, d.Requirement);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", d.RequiredValidationDomains); w.WriteEndObject();
    }
    private static GenevaDefinition Definition(JsonElement p)
    {
        Require(S(p, "profile") == GenevaProfile.Id, "Unsupported geneva profile.");
        return new(MechanicalAuthoringJson.Draft(p.GetProperty("source")), R.Mapping(p.GetProperty("sourceMapping")), Device(p.GetProperty("device")),
            Output(p.GetProperty("output")), Requirement(p.GetProperty("requirement")), Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!));
    }

    private static void Length(Utf8JsonWriter w, GenevaLength l)
    { w.WriteStartObject(); w.WriteString("kind",l.Kind.ToString()); R.Derived(w,"coefficientMm",l.CoefficientMm); R.Derived(w,"angleTurns",l.AngleTurns);w.WriteEndObject(); }
    private static GenevaLength Length(JsonElement p) => GenevaLength.FromCanonical(E<GenevaLengthKind>(p,"kind"), ReadDerived(p.GetProperty("coefficientMm")),ReadDerived(p.GetProperty("angleTurns")));
    private static Rational ReadDerived(JsonElement p)
    {
        var n=p.GetProperty("numerator").GetString()!;var d=p.GetProperty("denominator").GetString()!;
        Require(n is not null && d is not null && n.Length<=8192 && d.Length<=8192,"Derived exact digit bound exceeded.");
        var culture=System.Globalization.CultureInfo.InvariantCulture;
        var r=new Rational(System.Numerics.BigInteger.Parse(n!,System.Globalization.NumberStyles.AllowLeadingSign,culture),System.Numerics.BigInteger.Parse(d!,System.Globalization.NumberStyles.AllowLeadingSign,culture));
        Require(r.Numerator.ToString(culture)==n && r.Denominator.ToString(culture)==d,"Canonical reduced derived fraction required.");return r;
    }
    private static void Wheel(Utf8JsonWriter w,GenevaWheelSpecification s)
    {
        w.WriteStartObject();Integer(w,"slotCount",s.SlotCount);w.WritePropertyName("mouthRadius");Length(w,s.MouthRadius);
        R.Quantity(w,"slotRoot",s.SlotRoot);MechanicalAuthoringJson.Strings(w,"slotIds",s.SlotIds);w.WriteString("slotKind",s.SlotKind);w.WriteEndObject();
    }
    private static GenevaWheelSpecification Wheel(JsonElement p) => new(I(p,"slotCount"),Length(p.GetProperty("mouthRadius")),R.Quantity(p.GetProperty("slotRoot")),Items(p,"slotIds",64).Select(x=>x.GetString()!),S(p,"slotKind"));
    private static void IdealLock(Utf8JsonWriter w,GenevaIdealLockSpecification l)
    {
        w.WriteStartObject();w.WriteString("driverFeatureId",l.DriverFeatureId);MechanicalAuthoringJson.Strings(w,"recessIds",l.RecessIds);Integer(w,"recessPitchCount",l.RecessPitchCount);
        R.Quantity(w,"recessCenterDistance",l.RecessCenterDistance);R.Quantity(w,"driverRadius",l.DriverRadius);R.Quantity(w,"recessRadius",l.RecessRadius);
        R.Quantity(w,"patchHalfWidth",l.PatchHalfWidth);R.Quantity(w,"recessMountingTurns",l.RecessMountingTurns);R.Interval(w,"releaseWindow",l.ReleaseWindow);
        w.WriteBoolean("present",l.Present);w.WriteString("policy",l.Policy);w.WriteString("driverSurfaceKind",l.DriverSurfaceKind);w.WriteEndObject();
    }
    private static GenevaIdealLockSpecification IdealLock(JsonElement p) => new(S(p,"driverFeatureId"),Items(p,"recessIds",64).Select(x=>x.GetString()!),I(p,"recessPitchCount"),
        R.Quantity(p.GetProperty("recessCenterDistance")),R.Quantity(p.GetProperty("driverRadius")),R.Quantity(p.GetProperty("recessRadius")),R.Quantity(p.GetProperty("patchHalfWidth")),
        R.Quantity(p.GetProperty("recessMountingTurns")),R.Interval(p.GetProperty("releaseWindow")),p.GetProperty("present").GetBoolean(),S(p,"policy"),S(p,"driverSurfaceKind"));
    private static void Device(Utf8JsonWriter w,GenevaDeviceDefinition d)
    {
        w.WriteStartObject();w.WriteString("id",d.Id);w.WriteString("sourceShaftId",d.SourceShaftId);w.WriteString("sourcePortId",d.SourcePortId);
        w.WriteString("driverBodyId",d.DriverBodyId);w.WriteString("pinId",d.PinId);w.WriteString("wheelBodyId",d.WheelBodyId);
        R.LengthVector(w,"driverCenterMm",d.DriverCenterMm);R.Quantity(w,"driverStation",d.DriverStation);Vector(w,"planeNormal",d.PlaneNormal);Vector(w,"centerDirection",d.CenterDirection);
        w.WriteStartObject("outputShaft");w.WriteString("id",d.OutputShaft.Id);LengthFrame(w,"frameMm",d.OutputShaft.Frame);w.WriteBoolean("isPrescribed",d.OutputShaft.IsPrescribed);w.WriteEndObject();
        R.LengthVector(w,"wheelCenterMm",d.WheelCenterMm);R.Quantity(w,"wheelStation",d.WheelStation);R.Quantity(w,"driverMountingTurns",d.DriverMountingTurns);
        R.Quantity(w,"wheelMountingTurns",d.WheelMountingTurns);R.Quantity(w,"outputReferenceTurns",d.OutputReferenceTurns);Integer(w,"registrationSlot",d.RegistrationSlot);
        w.WritePropertyName("orbitRadius");Length(w,d.OrbitRadius);w.WritePropertyName("wheel");Wheel(w,d.Wheel);w.WritePropertyName("idealLock");IdealLock(w,d.IdealLock);
        w.WriteBoolean("pinPresent",d.PinPresent);w.WriteBoolean("axesFixed",d.AxesFixed);w.WriteString("mechanismKind",d.MechanismKind);w.WriteString("pinKind",d.PinKind);Integer(w,"pinCount",d.PinCount);w.WriteEndObject();
    }
    private static GenevaDeviceDefinition Device(JsonElement p)
    {
        var shaft=p.GetProperty("outputShaft");
        return new(S(p,"id"),S(p,"sourceShaftId"),S(p,"driverBodyId"),S(p,"pinId"),S(p,"wheelBodyId"),
            R.LengthVector(p.GetProperty("driverCenterMm")),R.Quantity(p.GetProperty("driverStation")),Vector(p.GetProperty("planeNormal")),Vector(p.GetProperty("centerDirection")),
            new OrientedShaft(S(shaft,"id"),LengthFrame(shaft.GetProperty("frameMm")),shaft.GetProperty("isPrescribed").GetBoolean()),
            R.LengthVector(p.GetProperty("wheelCenterMm")),R.Quantity(p.GetProperty("wheelStation")),R.Quantity(p.GetProperty("driverMountingTurns")),
            R.Quantity(p.GetProperty("wheelMountingTurns")),R.Quantity(p.GetProperty("outputReferenceTurns")),I(p,"registrationSlot"),
            Length(p.GetProperty("orbitRadius")),Wheel(p.GetProperty("wheel")),IdealLock(p.GetProperty("idealLock")),p.GetProperty("pinPresent").GetBoolean(),p.GetProperty("axesFixed").GetBoolean(),
            MechanicalAuthoringJson.NullableString(p,"sourcePortId"),S(p,"mechanismKind"),S(p,"pinKind"),I(p,"pinCount"));
    }
    private static void Output(Utf8JsonWriter w,GenevaOutputDefinition o)
    {w.WriteStartObject();w.WriteString("kind","IntermittentRotary");w.WriteString("key",o.Key);w.WriteString("shaftId",o.ShaftId);w.WriteString("bodyId",o.BodyId);Integer(w,"terminalSign",o.TerminalSign);R.Quantity(w,"terminalDatum",o.TerminalDatum);w.WriteEndObject();}
    private static GenevaOutputDefinition Output(JsonElement p)
    {Require(S(p,"kind")=="IntermittentRotary","Intermittent rotary output required.");return new(S(p,"key"),S(p,"shaftId"),S(p,"bodyId"),I(p,"terminalSign"),R.Quantity(p.GetProperty("terminalDatum")));}
    private static void Requirement(Utf8JsonWriter w,GenevaRequirement r)
    {
        w.WriteStartObject();OptionalQuantity(w,"requiredIndexStep",r.RequiredIndexStep);
        if(r.RequiredDwellFraction.HasValue)Fraction(w,"requiredDwellFraction",r.RequiredDwellFraction.Value);else w.WriteNull("requiredDwellFraction");
        OptionalQuantity(w,"requiredReferenceOutput",r.RequiredReferenceOutput);R.Quantity(w,"referenceRoot",r.ReferenceRoot);w.WriteEndObject();
    }
    private static GenevaRequirement Requirement(JsonElement p) => new(OptionalQuantity(p,"requiredIndexStep"),p.GetProperty("requiredDwellFraction").ValueKind==JsonValueKind.Null?(Rational?)null:F(p.GetProperty("requiredDwellFraction")),OptionalQuantity(p,"requiredReferenceOutput"),R.Quantity(p.GetProperty("referenceRoot")));
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
        var bytes = stream.ToArray(); Require(bytes.Length <= MaxDocumentBytes, "Geneva document byte limit exceeded."); using var parsed = Parse(bytes); return bytes;
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length > 0 && bytes.Length <= MaxDocumentBytes, "Geneva document byte limit exceeded.");
        var doc = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = MaxDocumentDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try { var nodes = 0; CheckNodes(doc.RootElement, ref nodes); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void CheckNodes(JsonElement p, ref int nodes)
    {
        Require(++nodes <= MaxDocumentNodes, "Geneva JSON node limit exceeded.");
        if (p.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in p.EnumerateObject())
            { Require(keys.Count < MaxObjectKeys && keys.Add(property.Name), "Duplicate or excessive geneva object keys."); CheckNodes(property.Value, ref nodes); }
        }
        else if (p.ValueKind == JsonValueKind.Array) foreach (var child in p.EnumerateArray()) CheckNodes(child, ref nodes);
    }
    private static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
