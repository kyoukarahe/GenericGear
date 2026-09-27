using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.DiscreteEmbodimentJson;

namespace GearInvest.Serialization;

/// <summary>Additive bounded transport. The old 14A cycle/state encoding is embedded unchanged, never used as a producer oracle.</summary>
public static class DiscreteGeometryJson
{
    public static byte[] WriteGeometry(DiscreteEmbodimentModel model, DiscreteGeometryCandidate g) => Envelope(DiscreteGeometryContract.GeometryFormat, w =>
    {
        w.WriteStartObject(); w.WriteString("geometryId", g.GeometryId); w.WriteString("realizationBindingId", g.RealizationBindingId);
        w.WriteString("modelId", g.ModelId); w.WriteString("sourceBindingId", g.SourceBindingId); w.WriteString("profile", g.Profile); w.WriteString("lengthUnit", g.LengthUnit);
        Rat(w, "millimetersPerTick", g.MillimetersPerTick); Rat(w, "minimumClearance", g.MinimumClearance);
        Array(w, "stations", g.Stations, Station);
        Array(w, "keepOuts", g.KeepOuts, (a, k) => { a.WriteStartObject(); a.WriteString("id", k.Id); Box(a, "bounds", k.Bounds); a.WriteEndObject(); });
        Array(w, "contacts", g.Contacts, (a, c) => { a.WriteStartObject(); a.WriteString("mover", c.Mover); a.WriteString("target", c.Target); a.WriteString("port", c.Port); a.WriteString("kind", c.Kind); Rat(a, "begin", c.Begin); Rat(a, "end", c.End); a.WriteString("condition", c.Condition); a.WriteEndObject(); });
        w.WritePropertyName("validation"); Validation(w, new DiscreteGeometryGenerator().Validate(model, g)); w.WriteEndObject();
    });

    /// <summary>Returns canonical geometry, including a diagnostically invalid realization. Approval is a separate Validate call; execution rejects invalid geometry.</summary>
    public static DiscreteGeometryCandidate ReadGeometry(DiscreteEmbodimentModel model, byte[] bytes)
    {
        using var d = Open(bytes, DiscreteGeometryContract.GeometryFormat); var p = d.RootElement.GetProperty("payload");
        Check(S(p, "profile") == DiscreteGeometryContract.Profile && S(p, "lengthUnit") == DiscreteGeometryContract.LengthUnit, "unsupported-geometry-kind");
        var g = new DiscreteGeometryCandidate(S(p, "modelId"), S(p, "sourceBindingId"), A(p, "stations", 2).Select(Station),
            A(p, "keepOuts", 32).Select(k => new GeometryKeepOut(S(k, "id"), Box(k.GetProperty("bounds")))),
            A(p, "contacts", 16).Select(c => new GeometryContactPermission(S(c, "mover"), S(c, "target"), S(c, "port"), S(c, "kind"), R(c.GetProperty("begin")), R(c.GetProperty("end")), S(c, "condition"))),
            R(p.GetProperty("minimumClearance")), S(p, "profile"), S(p, "lengthUnit"), R(p.GetProperty("millimetersPerTick")));
        Check(g.ModelId == model.ModelId && g.SourceBindingId == model.BindingId, "geometry-source-binding");
        Check(g.Contacts.All(c => c.Kind == "rigid-attachment" || c.Kind == "settled-sensing" || c.Kind == "ideal-positive-indexing"), "unsupported-contact-kind");
        Check(g.GeometryId == S(p, "geometryId") && g.RealizationBindingId == S(p, "realizationBindingId"), "geometry-identity");
        Check(g.Stations.Count == 2 && g.Stations.Select(s => s.WheelId).SequenceEqual(new[] { "primary", "secondary" }) &&
            g.Stations.Select(s => s.ProbeId).Distinct(StringComparer.Ordinal).Count() == 2 && g.Stations.Select(s => s.TrackId).Distinct(StringComparer.Ordinal).Count() == 2 &&
            g.Stations.All(s => s.Patches.Count > 0 && s.Patches.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() == s.Patches.Count) &&
            g.KeepOuts.Select(k => k.Id).Distinct(StringComparer.Ordinal).Count() == g.KeepOuts.Count, "duplicate-or-missing-geometry-reference");
        Check(bytes.SequenceEqual(WriteGeometry(model, g)), "noncanonical-geometry-or-unverified-certificate"); return g;
    }

    public static byte[] WriteResult(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, GeometricDiscreteResult r)
    {
        Check(new GeometricDiscreteEvaluator().ValidateResult(model, geometry, r).IsValid, "invalid-geometric-result");
        return Envelope(DiscreteGeometryContract.ResultFormat, w =>
        {
            w.WriteStartObject(); w.WriteString("geometryId", r.GeometryId); w.WriteString("realizationBindingId", r.RealizationBindingId); w.WriteString("modelId", r.ModelId); w.WriteString("geometricExecutionId", r.ExecutionId); w.WriteString("eventRequestId", r.EventRequestId);
            w.WriteNumber("budget", r.Budget); w.WriteNumber("known", r.Known); w.WriteNumber("applied", r.Applied); w.WriteNumber("omitted", r.Omitted); w.WriteString("status", r.Status.ToString());
            Rat(w, "toRoot", r.ToRoot); w.WritePropertyName("initial"); State(w, r.Initial);
            Array(w, "cycles", r.Cycles, (a, c) => { a.WriteStartObject(); a.WriteString("geometryId", c.GeometryId); a.WriteString("cycleId", c.CycleId); a.WritePropertyName("motion"); Cycle(a, c.Motion); Array(a, "hits", c.Hits, Hit); a.WriteEndObject(); });
            w.WritePropertyName("final"); State(w, r.Final); w.WritePropertyName("checkpoint"); State(w, r.Checkpoint); w.WritePropertyName("validation"); Validation(w, r.Validation); w.WriteEndObject();
        });
    }
    public static GeometricDiscreteResult ReadResult(DiscreteEmbodimentModel model, DiscreteGeometryCandidate geometry, byte[] bytes)
    {
        using var d = Open(bytes, DiscreteGeometryContract.ResultFormat); var p = d.RootElement.GetProperty("payload");
        var r = new GeometricDiscreteResult(S(p, "geometryId"), S(p, "realizationBindingId"), S(p, "modelId"), S(p, "eventRequestId"), State(p.GetProperty("initial")), R(p.GetProperty("toRoot")), I(p, "budget"), I(p, "known"), E<DiscreteStatus>(p, "status"),
            A(p, "cycles", 4096).Select(c => { var cycle = new GeometricDiscreteCycle(S(c, "geometryId"), Cycle(c.GetProperty("motion")), A(c, "hits", 2).Select(Hit)); Check(cycle.CycleId == S(c, "cycleId"), "geometric-cycle-identity"); return cycle; }),
            NullableState(p.GetProperty("final")), NullableState(p.GetProperty("checkpoint")), Validation(p.GetProperty("validation")));
        Check(r.ExecutionId == S(p, "geometricExecutionId"), "geometric-execution-identity");
        Check(bytes.SequenceEqual(WriteResult(model, geometry, r)), "noncanonical-geometric-result"); return r;
    }
    private static void Station(Utf8JsonWriter w, DiscreteGeometryStation s)
    {
        w.WriteStartObject(); w.WriteString("wheelId", s.WheelId); w.WriteString("trackId", s.TrackId); w.WriteString("probeId", s.ProbeId); w.WriteNumber("detents", s.Detents);
        Point(w, "center", s.Center); Point(w, "trackCenter", s.TrackCenter); w.WriteNumber("frameQuarter", s.FrameQuarter); Rat(w, "mountingTurns", s.MountingTurns);
        Rat(w, "wheelRadius", s.WheelRadius); Rat(w, "innerRadius", s.InnerRadius); Rat(w, "outerRadius", s.OuterRadius); Rat(w, "thickness", s.Thickness);
        Array(w, "patches", s.Patches, (a, p) => { a.WriteStartObject(); a.WriteString("id", p.Id); Rat(a, "startTurns", p.StartTurns); Rat(a, "spanTurns", p.SpanTurns); Rat(a, "height", p.Height); a.WriteEndObject(); });
        Point(w, "probeOrigin", s.ProbeOrigin); Point(w, "probeDirection", s.ProbeDirection); Rat(w, "travel", s.Travel); Rat(w, "datum", s.Datum); Rat(w, "scale", s.Scale); Rat(w, "angularMargin", s.AngularMargin);
        Point(w, "actuatorStart", s.ActuatorStart); Point(w, "actuatorEnd", s.ActuatorEnd); Box(w, "engagement", s.Engagement); Box(w, "wheelEnvelope", s.WheelEnvelope); Box(w, "trackEnvelope", s.TrackEnvelope);
        Box(w, "probeSweep", s.ProbeSweep); Box(w, "probeGuide", s.ProbeGuide); Box(w, "actuatorSweep", s.ActuatorSweep); w.WriteEndObject();
    }
    private static DiscreteGeometryStation Station(JsonElement s) => new DiscreteGeometryStation(S(s, "wheelId"), S(s, "trackId"), S(s, "probeId"), I(s, "detents"),
        Point(s.GetProperty("center")), Point(s.GetProperty("trackCenter")), I(s, "frameQuarter"), R(s.GetProperty("mountingTurns")), R(s.GetProperty("wheelRadius")), R(s.GetProperty("innerRadius")), R(s.GetProperty("outerRadius")), R(s.GetProperty("thickness")),
        A(s, "patches", 512).Select(p => new AxialSurfacePatch(S(p, "id"), R(p.GetProperty("startTurns")), R(p.GetProperty("spanTurns")), R(p.GetProperty("height")))),
        Point(s.GetProperty("probeOrigin")), Point(s.GetProperty("probeDirection")), R(s.GetProperty("travel")), R(s.GetProperty("datum")), R(s.GetProperty("scale")), R(s.GetProperty("angularMargin")),
        Point(s.GetProperty("actuatorStart")), Point(s.GetProperty("actuatorEnd")), Box(s.GetProperty("engagement")), Box(s.GetProperty("wheelEnvelope")), Box(s.GetProperty("trackEnvelope")), Box(s.GetProperty("probeSweep")), Box(s.GetProperty("probeGuide")), Box(s.GetProperty("actuatorSweep")));
    private static void Hit(Utf8JsonWriter w, DiscreteGeometryHit h)
    {
        w.WriteStartObject(); w.WriteString("witnessId", h.WitnessId); w.WriteString("geometryId", h.GeometryId); w.WriteString("wheelId", h.WheelId); w.WriteString("trackId", h.TrackId); w.WriteString("probeId", h.ProbeId); w.WriteString("surfaceId", h.SurfaceId); w.WriteString("status", h.Status.ToString());
        Rat(w, "wheelTurns", h.WheelTurns); Rat(w, "localBearing", h.LocalBearing); Point(w, "point", h.Point); Rat(w, "travel", h.Travel); Rat(w, "displacement", h.Displacement); Rat(w, "value", h.Value); w.WriteBoolean("interiorMember", h.InteriorMember); w.WriteString("diagnostic", h.Diagnostic); w.WriteEndObject();
    }
    private static DiscreteGeometryHit Hit(JsonElement h)
    {
        var hit = new DiscreteGeometryHit(S(h, "geometryId"), S(h, "wheelId"), S(h, "trackId"), S(h, "probeId"), S(h, "surfaceId"), E<GeometryHitStatus>(h, "status"), R(h.GetProperty("wheelTurns")), R(h.GetProperty("localBearing")), Point(h.GetProperty("point")), R(h.GetProperty("travel")), R(h.GetProperty("displacement")), R(h.GetProperty("value")), S(h, "diagnostic"));
        Check(hit.WitnessId == S(h, "witnessId"), "witness-identity"); return hit;
    }
    private static void Validation(Utf8JsonWriter w, DiscreteGeometryValidation v)
    {
        w.WriteStartObject(); w.WriteBoolean("isValid", v.IsValid); Array(w, "checks", v.Checks, (a, c) => { a.WriteStartObject(); a.WriteString("domain", c.Domain); a.WriteString("item", c.Item); a.WriteString("verdict", c.Verdict.ToString()); a.WriteString("method", c.Method); Rat(a, "begin", c.Begin); Rat(a, "end", c.End); a.WriteString("detail", c.Detail); a.WriteBoolean("required", c.Required); a.WriteEndObject(); }); w.WriteEndObject();
    }
    private static DiscreteGeometryValidation Validation(JsonElement v) => new DiscreteGeometryValidation(A(v, "checks", 512).Select(c => new GeometryCheck(S(c, "domain"), S(c, "item"), E<GeometryVerdict>(c, "verdict"), S(c, "method"), R(c.GetProperty("begin")), R(c.GetProperty("end")), S(c, "detail"), c.GetProperty("required").GetBoolean())));
    private static void Rat(Utf8JsonWriter w, string key, Rational r) { w.WritePropertyName(key); R(w, r); }
    private static void Point(Utf8JsonWriter w, string key, GeometryPoint p) { w.WriteStartObject(key); Rat(w, "x", p.X); Rat(w, "y", p.Y); Rat(w, "z", p.Z); w.WriteEndObject(); }
    private static GeometryPoint Point(JsonElement p) => new GeometryPoint(R(p.GetProperty("x")), R(p.GetProperty("y")), R(p.GetProperty("z")));
    private static void Box(Utf8JsonWriter w, string key, GeometryBox b) { w.WriteStartObject(key); Point(w, "min", b.Min); Point(w, "max", b.Max); w.WriteEndObject(); }
    private static GeometryBox Box(JsonElement b) => new GeometryBox(Point(b.GetProperty("min")), Point(b.GetProperty("max")));
}
