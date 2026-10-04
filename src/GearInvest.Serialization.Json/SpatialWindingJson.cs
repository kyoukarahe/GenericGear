using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Separate source format. Old planar readers must reject rather than reinterpret it.</summary>
public static partial class SpatialWindingJson
{
    public const string DraftFormat = "gear-invest.spatial-winding-connection-draft";
    public const string ArtifactFormat = "gear-invest.spatial-winding-connection-mechanism";
    public static byte[] WriteDraft(WindingDifferentialDefinition s) => WindingConnectionJson.Encode(w =>
    {
        Require(s.WindingSource is SpatialWindingDefinition, "UnsupportedProfile");
        WindingConnectionJson.Start(w, DraftFormat); w.WriteString("profile", s.SourceProfile); w.WriteString("definitionId", s.DefinitionId);
        var source = (SpatialWindingDefinition)s.WindingSource; var g = source.Geometry;
        w.WriteStartObject("winding"); w.WriteString("definitionId", source.DefinitionId); w.WriteString("profile", SpatialWindingGeometry.Profile);
        w.WritePropertyName("driverShaft"); MechanicalAuthoringJson.WriteShaft(w, source.DriverShaft);
        w.WritePropertyName("outputShaft"); MechanicalAuthoringJson.WriteShaft(w, source.OutputShaft);
        w.WriteString("driverBodyId", source.DriverBodyId); w.WriteString("outputBodyId", source.OutputBodyId); w.WriteString("chainId", source.ChainId);
        Fraction(w, "initialDriverTurns", source.InitialDriverTurns);
        w.WriteStartObject("geometry"); Guide(w, "driver", g.Driver); Guide(w, "output", g.Output);
        Array(w, "bridgeMm", g.Bridge, Point); Integer(w, "linkCount", g.LinkCount); w.WriteNumber("pitchMm", g.PitchMm);
        w.WriteString("jointModel", g.JointModel); w.WriteString("linkRoll", g.LinkRoll);
        w.WriteNumber("maxBendDegrees", g.MaxBendDegrees); w.WriteNumber("maxAttachmentBendDegrees", g.MaxAttachmentBendDegrees);
        w.WriteNumber("driverMinimumTurns", g.DriverMinimumTurns); w.WriteNumber("driverMaximumTurns", g.DriverMaximumTurns);
        w.WriteNumber("outputMinimumTurns", g.OutputMinimumTurns); w.WriteNumber("outputMaximumTurns", g.OutputMaximumTurns); w.WriteEndObject(); w.WriteEndObject();
        w.WritePropertyName("suffix"); WindingConnectionJson.Suffix(w, s.Suffix);
        w.WritePropertyName("transmission"); Transmission(w, s.Transmission);
        w.WriteString("couplingId", s.CouplingId); w.WriteString("sunPortId", s.SunPortId); w.WriteString("planetPortId", s.PlanetPortId);
        Fraction(w, "initialCouplingOffset", s.InitialCouplingOffset); MechanicalAuthoringJson.Strings(w, "requiredDomains", s.RequiredDomains); w.WriteEndObject();
    });
    public static WindingDifferentialDefinition ReadDraft(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = WindingConnectionJson.Parse(bytes); var root = doc.RootElement; Header(root, DraftFormat);
        Require(S(root, "profile") == WindingDifferentialDefinition.SpatialProfile, "UnsupportedProfile");
        var p = root.GetProperty("winding"); var g = p.GetProperty("geometry");
        Require(S(g, "jointModel") == "spherical-free-twist" && S(g, "linkRoll") == "Underdetermined", "UnsupportedJointModel");
        var geometry = new SpatialWindingGeometry(Guide(g.GetProperty("driver")), Guide(g.GetProperty("output")), Items(g, "bridgeMm", 16).Select(Point),
            I(g, "linkCount"), D(g, "pitchMm"), D(g, "maxBendDegrees"), D(g, "maxAttachmentBendDegrees"),
            D(g, "driverMinimumTurns"), D(g, "driverMaximumTurns"), D(g, "outputMinimumTurns"), D(g, "outputMaximumTurns"));
        var winding = new SpatialWindingDefinition(MechanicalAuthoringJson.ReadShaft(p.GetProperty("driverShaft")), MechanicalAuthoringJson.ReadShaft(p.GetProperty("outputShaft")),
            S(p, "driverBodyId"), S(p, "outputBodyId"), S(p, "chainId"), geometry, F(p.GetProperty("initialDriverTurns")));
        var s = new WindingDifferentialDefinition(winding, WindingConnectionJson.Suffix(root.GetProperty("suffix")), S(root, "couplingId"), S(root, "sunPortId"), S(root, "planetPortId"),
            F(root.GetProperty("initialCouplingOffset")), Items(root, "requiredDomains", 16).Select(x => x.GetString()!), Transmission(root.GetProperty("transmission")));
        Require(bytes.SequenceEqual(WriteDraft(s)), "NoncanonicalSpatialSource"); return s;
    });
    public static WindingConnectionArtifact WriteArtifact(WindingDifferentialDefinition s, DifferentialArtifact parent)
    {
        var a = WindingDifferentialEngine.Prepare(s); Require(a.IsValid, "Connected admission: " + string.Join(",", a.Diagnostics));
        Require(parent.Request.RequestId == s.Suffix.Parent.RequestId, "Parent source mismatch.");
        var bytes = WindingConnectionJson.Encode(w =>
        {
            WindingConnectionJson.Start(w, ArtifactFormat); w.WriteString("profile", s.SourceProfile); w.WriteString("definitionId", s.DefinitionId);
            w.WriteBase64String("sourceDraftUtf8", WriteDraft(s)); w.WriteBase64String("parentArtifactUtf8", parent.Bytes); w.WriteString("parentArtifactId", parent.ArtifactHash);
            w.WritePropertyName("outputLaw"); WindingConnectionJson.Law(w, a.Suffix.OutputLaw!);
            w.WriteString("validation", "source-admission+initial-all-pin-chords+exact-relations+pitch-placement"); w.WriteString("numericalPolicy", SpatialWindingGeometry.Policy);
            w.WriteString("pathPolicy", "bounded-numerical-sampling-128-per-input-turn");
            w.WriteString("unperformed", "continuous-path-proof;tooth-solids;swept-solids;dynamics;unique-link-roll"); w.WriteEndObject();
        });
        return new(s, a, bytes);
    }
    public static WindingConnectionArtifact ReadArtifact(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        using var doc = WindingConnectionJson.Parse(bytes); var p = doc.RootElement; Header(p, ArtifactFormat);
        var s = ReadDraft(WindingConnectionJson.Raw(p, "sourceDraftUtf8")); var parent = DifferentialJson.ReadArtifact(WindingConnectionJson.Raw(p, "parentArtifactUtf8"));
        var fresh = WriteArtifact(s, parent); Require(bytes.SequenceEqual(fresh.Bytes), "Spatial source/parent/law mismatch."); return fresh;
    });
    internal static bool IsFormat(byte[] bytes, string format)
    { using var doc = WindingConnectionJson.Parse(bytes); return S(doc.RootElement, "format") == format; }
    private static void Guide(Utf8JsonWriter w, string name, HelicalPinGuide h)
    {
        w.WriteStartObject(name); Frame(w, "frameMm", h.Frame); w.WriteNumber("radiusMm", h.RadiusMm); w.WriteNumber("radiusChangeMmPerTurn", h.RadiusChangeMmPerTurn);
        w.WriteNumber("heightChangeMmPerTurn", h.HeightChangeMmPerTurn); Integer(w, "hand", h.Hand); w.WriteNumber("phaseTurns", h.PhaseTurns);
        w.WriteNumber("exitAzimuthTurns", h.ExitAzimuthTurns); Integer(w, "windingBranch", h.WindingBranch); w.WriteNumber("maximumGuideTurns", h.MaximumGuideTurns); w.WriteEndObject();
    }
    private static HelicalPinGuide Guide(JsonElement p) => new(MechanicalAuthoringJson.LooseFrame(p.GetProperty("frameMm")), D(p, "radiusMm"), D(p, "radiusChangeMmPerTurn"),
        D(p, "heightChangeMmPerTurn"), I(p, "hand"), D(p, "phaseTurns"), D(p, "exitAzimuthTurns"), I(p, "windingBranch"), D(p, "maximumGuideTurns"));
    private static double D(JsonElement p, string name) { var d = p.GetProperty(name).GetDouble(); Require(double.IsFinite(d), "InvalidGeometry"); return d; }
    internal static void Point(Utf8JsonWriter w, WindingPoint3 p) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteNumberValue(p.Z); w.WriteEndArray(); }
    private static WindingPoint3 Point(JsonElement p) { Require(p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 3, "InvalidPoint"); return new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()); }

    internal static void WriteFrame(Utf8JsonWriter w, ConnectedFrame f, SpatialWindingDefinition source)
    {
        var g = f.SpatialWinding ?? throw new ArgumentException("MissingSpatialFrame");
        w.WriteStartObject(); w.WriteString("definitionId", f.DefinitionId); w.WriteString("snapshotId", f.SnapshotId); Fraction(w, "driverTurns", f.DriverTurns);
        Array(w, "coordinates", f.Coordinates, (x, p) => { x.WriteStartObject(); x.WriteString("shaftId", p.Key); x.WritePropertyName("value"); WindingConnectionJson.Value(x, p.Value); x.WriteEndObject(); });
        Array(w, "ports", f.Ports, (x, p) => { x.WriteStartObject(); x.WriteString("portId", p.Key); x.WritePropertyName("value"); WindingConnectionJson.Value(x, p.Value); x.WriteEndObject(); });
        w.WriteStartObject("winding"); w.WriteString("profile", SpatialWindingGeometry.Profile); w.WriteString("quality", g.Quality); w.WriteNull("solutionErrorBoundTurns");
        w.WriteNumber("outputEstimateTurns", g.OutputTurns); Integer(w, "driverContact", g.DriverContact); Integer(w, "outputContact", g.OutputContact);
        w.WriteNumber("pitchResidualMm", g.PitchResidualMm); w.WriteNumber("attachmentResidualMm", g.AttachmentResidualMm);
        w.WriteNumber("maximumBendDegrees", g.MaximumBendDegrees); w.WriteNumber("maximumAttachmentBendDegrees", g.MaximumAttachmentBendDegrees);
        w.WriteString("linkRoll", g.LinkRoll);
        Array(w, "pins", g.Pins.Select((p, i) => (p, i)), (x, item) =>
        {
            x.WriteStartObject(); x.WriteString("id", source.PinId(item.i)); x.WritePropertyName("positionMm"); Point(x, item.p.PositionMm);
            Integer(x, "guidePiece", item.p.GuidePiece); x.WriteNumber("parameter", item.p.Parameter); x.WriteEndObject();
        });
        w.WriteEndObject(); w.WriteString("displayUnavailableReason", f.DisplayUnavailableReason); w.WriteString("linkDisplayFrame", "axis-plus-presentation-roll-not-unique-mechanics");
        Array(w, "matricesMm", f.MatricesMm, (x, p) => { x.WriteStartObject(); x.WriteString("id", p.Key); x.WriteStartArray("matrix"); foreach (var v in p.Value) x.WriteNumberValue(v); x.WriteEndArray(); x.WriteEndObject(); }); w.WriteEndObject();
    }
}
