using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public static partial class SpatialWindingJson
{
    private static void Transmission(Utf8JsonWriter w, ConnectedTransmission? t)
    {
        if (t is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("definitionId", t.DefinitionId);
        Array(w, "stages", t.Stages, (x, s) =>
        {
            x.WriteStartObject(); x.WriteString("definitionId", s.DefinitionId); x.WriteString("sourceShaftId", s.SourceShaftId);
            x.WritePropertyName("outputShaft"); MechanicalAuthoringJson.WriteShaft(x, s.OutputShaft);
            x.WritePropertyName("driverGear"); MechanicalAuthoringJson.Body(x, s.DriverGear); x.WritePropertyName("outputGear"); MechanicalAuthoringJson.Body(x, s.OutputGear);
            x.WriteString("contactId", s.ContactId); Fraction(x, "driverMountTurns", s.DriverMountTurns); Fraction(x, "outputMountTurns", s.OutputMountTurns);
            Fraction(x, "toothRegistration", s.ToothRegistration); Fraction(x, "outputReferenceTurns", s.OutputReferenceTurns);
            x.WriteStartObject("outputPort"); x.WriteString("id", s.OutputPort.Id); x.WriteString("shaftId", s.OutputPort.ShaftId); Frame(x, "frameInShaft", s.OutputPort.FrameInShaft);
            RotaryLinearJson.Quantity(x, "readoutOffset", s.OutputPort.ReadoutOffset); x.WriteEndObject(); x.WriteEndObject();
        });
        w.WriteString("carrierShaftId", t.CarrierShaftId); w.WriteString("carrierBodyId", t.CarrierBodyId); Frame(w, "carrierMountingFrame", t.CarrierMountingFrame);
        Fraction(w, "carrierMountTurns", t.CarrierMountTurns);
        Array(w, "attachments", t.Attachments, (x, m) => { x.WriteStartObject(); x.WriteString("bodyId", m.BodyId); Frame(x, "frameInCarrier", m.FrameInCarrier); x.WriteEndObject(); });
        w.WriteEndObject();
    }
    private static ConnectedTransmission? Transmission(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Null) return null;
        var stages = Items(p, "stages", 4).Select(s =>
        {
            var port = s.GetProperty("outputPort");
            return new FixedAxisSpurStage(S(s, "sourceShaftId"), MechanicalAuthoringJson.ReadShaft(s.GetProperty("outputShaft")), MechanicalAuthoringJson.Body(s.GetProperty("driverGear")),
                MechanicalAuthoringJson.Body(s.GetProperty("outputGear")), S(s, "contactId"), F(s.GetProperty("driverMountTurns")), F(s.GetProperty("outputMountTurns")),
                F(s.GetProperty("toothRegistration")), F(s.GetProperty("outputReferenceTurns")),
                new(S(port, "id"), S(port, "shaftId"), MechanicalAuthoringJson.LooseFrame(port.GetProperty("frameInShaft")), RotaryLinearJson.Quantity(port.GetProperty("readoutOffset"))));
        });
        return new(stages, S(p, "carrierShaftId"), S(p, "carrierBodyId"), MechanicalAuthoringJson.LooseFrame(p.GetProperty("carrierMountingFrame")), F(p.GetProperty("carrierMountTurns")),
            Items(p, "attachments", 8).Select(m => new CarrierRigidAttachment(S(m, "bodyId"), MechanicalAuthoringJson.LooseFrame(m.GetProperty("frameInCarrier")))));
    }
    internal static void Scene(Utf8JsonWriter w, WindingConnectionArtifact artifact)
    {
        var s = artifact.Source; var source = (SpatialWindingDefinition)s.WindingSource; var g = source.Geometry;
        w.WriteStartArray();
        void Node(string id, string owner, string kind, double radius, System.Collections.Generic.IEnumerable<WindingPoint3>? points = null)
        {
            w.WriteStartObject(); w.WriteString("id", id); w.WriteString("owner", owner); w.WriteString("kind", kind); w.WriteNumber("radiusMm", radius);
            Array(w, "pointsMm", points ?? System.Array.Empty<WindingPoint3>(), Point); w.WriteEndObject();
        }
        double D(Rational r) => (double)r.Numerator / (double)r.Denominator;
        foreach (var n in artifact.Analysis.Suffix.Parent.PoseNodes.Where(n => n.BodyId is not null))
            if (n.PitchRadiusMm.HasValue) Node(n.Id, n.BodyId!, "pitch-circle", D(n.PitchRadiusMm.Value));
            else Node(n.Id, n.BodyId!, "polyline", 0, new[] { new WindingPoint3(0,0,0), WindingPoint3.Of(s.Suffix.Parent.Definition.PlanetShaft.FrameInCarrier.Origin) });
        Node(ConnectedPoseIds.DriverGear, s.Suffix.DriverGear.Id, "pitch-circle", D(s.Suffix.DriverGear.OuterPitchRadius));
        Node(ConnectedPoseIds.OutputGear, s.Suffix.OutputGear.Id, "pitch-circle", D(s.Suffix.OutputGear.OuterPitchRadius));
        WindingPoint3[] LocalGuide(HelicalPinGuide h) => Enumerable.Range(0, 257).Select(i =>
        {
            var t = h.MaximumGuideTurns * i / 256; var radius = h.RadiusMm + h.RadiusChangeMmPerTurn * t; var a = 2 * Math.PI * (h.PhaseTurns + h.Hand * t);
            return new WindingPoint3(radius * Math.Cos(a), radius * Math.Sin(a), h.HeightChangeMmPerTurn * t);
        }).ToArray();
        Node(ConnectedPoseIds.WindingDriver, source.DriverBodyId, "polyline", 0, LocalGuide(g.Driver));
        Node(ConnectedPoseIds.WindingOutput, source.OutputBodyId, "polyline", 0, LocalGuide(g.Output));
        for (var i = 0; i < g.LinkCount; i++) Node(ConnectedPoseIds.Link(i), source.LinkId(i), "polyline", 0, new[] { new WindingPoint3(0,0,0), new WindingPoint3(g.PitchMm,0,0) });
        for (var i = 0; i <= g.LinkCount; i++) Node(ConnectedPoseIds.Pin(i), source.PinId(i), "pin", g.PitchMm * .08);
        if (s.Transmission is not null)
        {
            var t = s.Transmission;
            foreach (var stage in t.Stages)
            { Node(stage.DriverGear.Id, stage.DriverGear.Id, "pitch-circle", D(stage.DriverGear.OuterPitchRadius)); Node(stage.OutputGear.Id, stage.OutputGear.Id, "pitch-circle", D(stage.OutputGear.OuterPitchRadius)); }
            Node(t.CarrierBodyId, t.CarrierBodyId, "polyline", 0, new[] { new WindingPoint3(-5,0,0), new WindingPoint3(5,0,0), new WindingPoint3(0,0,0), new WindingPoint3(0,0,3) });
            foreach (var m in t.Attachments) Node(m.BodyId, m.BodyId, "polyline", 0, new[] { new WindingPoint3(-1,0,0), new WindingPoint3(1,0,0), new WindingPoint3(0,0,0), new WindingPoint3(0,0,2) });
        }
        w.WriteEndArray();
    }
}
