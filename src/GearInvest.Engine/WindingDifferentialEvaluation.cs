using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Display recipe nodes are separate from authored body/material identities.</summary>
public static class ConnectedPoseIds
{
    public const string DriverGear="connected/suffix-driver";
    public const string OutputGear="connected/suffix-output";
    public const string WindingDriver="connected/winding-driver";
    public const string WindingOutput="connected/winding-output";
    public static string Pin(int i)=>"connected/pin-"+i.ToString("D2",System.Globalization.CultureInfo.InvariantCulture);
    public static string Link(int i)=>"connected/link-"+i.ToString("D2",System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ConnectedFrame
{
    private readonly WindingPose? planarWinding;
    internal ConnectedFrame(string definitionId, Rational q, WindingPose winding, IEnumerable<KeyValuePair<string, ConnectedMotionValue>> coordinates,
        IEnumerable<KeyValuePair<string, ConnectedMotionValue>> ports, IEnumerable<KeyValuePair<string, ReadOnlyCollection<double>>> matrices, string? displayError)
    {
        DefinitionId = definitionId; DriverTurns = q; planarWinding = winding;
        Coordinates = new ReadOnlyDictionary<string, ConnectedMotionValue>(coordinates.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        Ports = new ReadOnlyDictionary<string, ConnectedMotionValue>(ports.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        MatricesMm = new ReadOnlyDictionary<string, ReadOnlyCollection<double>>(matrices.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)); DisplayUnavailableReason = displayError;
        SnapshotId = HashText(Pack(definitionId, F(q), N(winding.DriverContact), N(winding.OutputContact), Pack(Coordinates.Select(p => Pack(p.Key, p.Value.Key)).ToArray())));
    }
    public string DefinitionId { get; }
    public string SnapshotId { get; }
    public Rational DriverTurns { get; }
    internal ConnectedFrame(string definitionId, Rational q, SpatialWindingPose winding, IEnumerable<KeyValuePair<string, ConnectedMotionValue>> coordinates,
        IEnumerable<KeyValuePair<string, ConnectedMotionValue>> ports, IEnumerable<KeyValuePair<string, ReadOnlyCollection<double>>> matrices, string? displayError)
    {
        DefinitionId = definitionId; DriverTurns = q; SpatialWinding = winding;
        Coordinates = new ReadOnlyDictionary<string, ConnectedMotionValue>(coordinates.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        Ports = new ReadOnlyDictionary<string, ConnectedMotionValue>(ports.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        MatricesMm = new ReadOnlyDictionary<string, ReadOnlyCollection<double>>(matrices.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        DisplayUnavailableReason = displayError;
        SnapshotId = HashText(Pack(definitionId, F(q), N(winding.DriverContact), N(winding.OutputContact), Pack(Coordinates.Select(p => Pack(p.Key, p.Value.Key)).ToArray())));
    }
    public WindingPose Winding => planarWinding ?? throw new InvalidOperationException("Spatial frame: use SpatialWinding.");
    public SpatialWindingPose? SpatialWinding { get; }
    public IReadOnlyDictionary<string, ConnectedMotionValue> Coordinates { get; }
    public IReadOnlyDictionary<string, ConnectedMotionValue> Ports { get; }
    /// <summary>Column-major world matrices, mm; optional approximate display, never mechanical truth.</summary>
    public IReadOnlyDictionary<string, ReadOnlyCollection<double>> MatricesMm { get; }
    public string? DisplayUnavailableReason { get; }
}

public sealed class ConnectedEvaluation
{
    internal ConnectedEvaluation(string status, ConnectedFrame? frame = null) { Status = status; Frame = frame; }
    public string Status { get; }
    public ConnectedFrame? Frame { get; }
    public bool IsAccepted => Frame is not null;
}

public sealed class WindingDriveInput
{
    public WindingDriveInput(Rational driverTurns, Rational planetPortTurns)
    { MechanicalAuthoringProfile.Number(driverTurns); MechanicalAuthoringProfile.Number(planetPortTurns); DriverTurns = driverTurns; PlanetPortTurns = planetPortTurns; }
    public Rational DriverTurns { get; }
    public Rational PlanetPortTurns { get; }
}

public sealed class ConnectedAdvance
{
    internal ConnectedAdvance(string status, ConnectedFrame last, IEnumerable<WindingDriveInput> requested, IEnumerable<ConnectedFrame>? frames = null)
    { Status = status; LastValidSnapshot = last; Requested = requested.ToList().AsReadOnly(); Frames = (frames ?? Array.Empty<ConnectedFrame>()).ToList().AsReadOnly(); }
    public string Status { get; }
    public ConnectedFrame LastValidSnapshot { get; }
    public ReadOnlyCollection<WindingDriveInput> Requested { get; }
    public ReadOnlyCollection<ConnectedFrame> Frames { get; }
    public bool IsAccepted => Status == "Accepted";
    public int AppliedSegments => IsAccepted ? Requested.Count : 0;
    public ReadOnlyCollection<WindingDriveInput> Remainder => IsAccepted ? Array.Empty<WindingDriveInput>().ToList().AsReadOnly() : Requested;
    public ConnectedFrame? Frame => IsAccepted ? Frames.LastOrDefault() ?? LastValidSnapshot : null;
}

public static partial class WindingDifferentialEngine
{
    public static ConnectedEvaluation Evaluate(WindingDifferentialAnalysis analysis, WindingDriveInput input)
    {
        if (!analysis.IsValid) return new("InvalidDefinition");
        return EvaluateMode(analysis, input.DriverTurns, (w, s) =>
        {
            var sun = w.Scale(1, s.InitialCouplingOffset);
            return (sun, ConnectedMotionValue.FromExact(input.PlanetPortTurns));
        });
    }

    // Mechanical-mode transitions reuse exactly this geometry/evaluation/pose path; no second E1 solver.
    internal static ConnectedEvaluation EvaluateMode(WindingDifferentialAnalysis a, Rational q,
        Func<ConnectedMotionValue, WindingDifferentialDefinition, (ConnectedMotionValue Sun, ConnectedMotionValue PlanetPort)> boundary)
    {
        try
        {
            var s = a.Source; var d = s.Suffix.Parent.Definition;
            var source = s.WindingSource;
            if (q < Binary64Boundary(source.DriverMinimumTurns) || q > Binary64Boundary(source.DriverMaximumTurns)) return new("WindingBoundary");
            WindingPose? pose = null; SpatialWindingPose? spatial = null;
            if (source is FiniteWindingDefinition planar)
            {
                var query = FiniteWindingSolver.Evaluate(planar.Geometry, ConnectedMotionValue.Number(q));
                if (!query.IsAccepted) return new(query.Status); pose = query.Pose!;
            }
            else
            {
                var query = a.SpatialQuery(ConnectedMotionValue.Number(q));
                if (!query.IsAccepted) return new(query.Status); spatial = query.Pose!;
            }
            var w = ConnectedMotionValue.FromResidual(HashText(Pack(source.DefinitionId, F(q), "passive-shaft")), spatial?.OutputTurns ?? pose!.OutputTurns);
            var selected = boundary(s.CouplingShaftId == s.WindingSource.DriverShaft.Id ? ConnectedMotionValue.FromExact(q) : w, s); var sunPort = d.Ports.Single(p => p.Id == s.SunPortId);
            var vector = new SortedDictionary<string, ConnectedMotionValue>(StringComparer.Ordinal)
            { [s.SunPortId] = selected.Sun.Scale(sunPort.FrameInShaft.Z.Z, sunPort.ReadoutOffset.Value), [s.PlanetPortId] = selected.PlanetPort };
            var coordinates = a.Suffix.Parent.Coordinates.ToDictionary(c => c.ShaftId, c => ConnectedMotionValue.Apply(c.Law!, vector), StringComparer.Ordinal);
            coordinates.Add(source.DriverShaft.Id, ConnectedMotionValue.FromExact(q)); coordinates.Add(source.OutputShaft.Id, w);
            coordinates.Add(s.Suffix.OutputShaft.Id, ConnectedMotionValue.Apply(a.Suffix.OutputLaw!, vector));
            var ports = d.Ports.ToDictionary(p => p.Id, p => coordinates[p.ShaftId].Scale(p.FrameInShaft.Z.Z, p.ReadoutOffset.Value), StringComparer.Ordinal);
            var outputPort = s.Suffix.OutputPort; ports.Add(outputPort.Id, coordinates[outputPort.ShaftId].Scale(outputPort.FrameInShaft.Z.Z, outputPort.ReadoutOffset.Value));
            ConnectedTransmissionCompiler.Evaluate(s, coordinates, ports);
            var display = Project(a, pose, spatial, vector, coordinates);
            return spatial is null ? new("ResidualChecked", new(s.DefinitionId, q, pose!, coordinates, ports, display.Matrices, display.UnavailableReason)) :
                new("ResidualChecked", new(s.DefinitionId, q, spatial, coordinates, ports, display.Matrices, display.UnavailableReason));
        }
        catch (ArgumentException e) { return new(e.Message); }
    }

    public static ConnectedAdvance Advance(WindingDifferentialAnalysis a, ConnectedFrame state, IEnumerable<WindingDriveInput> segments)
    {
        var path = segments.Take(17).ToArray();
        if (path.Length > 16) return new("ResourceLimit", state, path);
        if (!a.IsValid || state.DefinitionId != a.Source.DefinitionId) return new("ForeignSnapshot", state, path);
        var previousInput = state.Ports[a.Source.PlanetPortId];
        if (!previousInput.IsExact || Evaluate(a,new(state.DriverTurns,previousInput.Exact!.Value)).Frame?.SnapshotId != state.SnapshotId) return new("InvalidDriveSnapshot",state,path);
        var accepted = new List<ConnectedFrame>(); var current = state;
        foreach (var input in path)
        {
            var error = ConnectedWindingKinematics.ValidateSegment(a.Source.WindingSource, ConnectedMotionValue.Number(current.DriverTurns), ConnectedMotionValue.Number(input.DriverTurns), a);
            if (error is not null) return new(error, state, path);
            var next = Evaluate(a, input); if (!next.IsAccepted) return new(next.Status, state, path);
            accepted.Add(next.Frame!); current = next.Frame!;
        }
        return new("Accepted", state, path, accepted);
    }

    // Compare an exact input with the authored binary64 DOMAIN, not with a rounded input.
    // This conversion is never used to relabel an E1 solution as exact.
    internal static Rational Binary64Boundary(double value)
    {
        var bits=BitConverter.DoubleToInt64Bits(value);var negative=bits<0;var exponent=(int)((bits>>52)&0x7ff);var significand=new BigInteger(bits&0x000fffffffffffffL);
        if(exponent!=0)significand+=BigInteger.One<<52;var shift=(exponent==0?-1022:exponent-1023)-52;if(negative)significand=-significand;
        return shift>=0?new Rational(significand<<shift,BigInteger.One):new Rational(significand,BigInteger.One<<-shift);
    }

    private static CarrierDisplayPose Project(WindingDifferentialAnalysis a, WindingPose? winding, SpatialWindingPose? spatial, IReadOnlyDictionary<string, ConnectedMotionValue> vector,
        IReadOnlyDictionary<string, ConnectedMotionValue> coordinates)
    {
        var result = new Dictionary<string, ReadOnlyCollection<double>>(StringComparer.Ordinal);
        try
        {
            void Add(string id, OrientedFrame f, ConnectedMotionValue value, string? parent = null)
            {
                var angle = value.DisplayTurns() * 2 * Math.PI; var c = Math.Cos(angle); var sin = Math.Sin(angle);
                double N(Rational r) => ConnectedMotionValue.Number(r);
                var x = f.X; var y = f.Y; var z = f.Z; var o = f.Origin;
                var m = new[] { N(x.X)*c+N(y.X)*sin, N(x.Y)*c+N(y.Y)*sin, N(x.Z)*c+N(y.Z)*sin, 0,
                    N(y.X)*c-N(x.X)*sin, N(y.Y)*c-N(x.Y)*sin, N(y.Z)*c-N(x.Z)*sin, 0, N(z.X),N(z.Y),N(z.Z),0,N(o.X),N(o.Y),N(o.Z),1 };
                if (parent is not null)
                { var p = result[parent]; var product = new double[16]; for (var col = 0; col < 4; col++) for (var row = 0; row < 4; row++) for (var k = 0; k < 4; k++) product[4*col+row] += p[4*k+row]*m[4*col+k]; m = product; }
                if (m.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e8)) throw new ArgumentException("DisplayUnavailable");
                result.Add(id, Array.AsReadOnly(m));
            }
            foreach (var n in a.Suffix.Parent.PoseNodes) Add(n.Id, n.Frame, ConnectedMotionValue.Apply(n.Rotation, vector), n.ParentId);
            var s = a.Source; var suffix = s.Suffix;
            Add(ConnectedPoseIds.DriverGear, suffix.DriverGear.MountingFrame, coordinates[suffix.SourceShaftId].Scale(1, suffix.DriverMount.Value));
            Add(ConnectedPoseIds.OutputGear, suffix.OutputGear.MountingFrame, coordinates[suffix.OutputShaft.Id].Scale(1, suffix.OutputMount.Value));
            if (s.Transmission is not null)
            {
                var t = s.Transmission;
                foreach (var stage in t.Stages)
                {
                    Add(stage.DriverGear.Id, stage.DriverGear.MountingFrame, coordinates[stage.SourceShaftId].Scale(1, stage.DriverMountTurns));
                    Add(stage.OutputGear.Id, stage.OutputGear.MountingFrame, coordinates[stage.OutputShaft.Id].Scale(1, stage.OutputMountTurns));
                }
                Add(t.CarrierBodyId, t.CarrierMountingFrame, coordinates[t.CarrierShaftId].Scale(1, t.CarrierMountTurns));
                foreach (var mount in t.Attachments) Add(mount.BodyId, mount.FrameInCarrier, ConnectedMotionValue.FromExact(0), t.CarrierBodyId);
            }
            Add(ConnectedPoseIds.WindingDriver, s.WindingSource.DriverShaft.Frame, coordinates[s.WindingSource.DriverShaft.Id]);
            Add(ConnectedPoseIds.WindingOutput, s.WindingSource.OutputShaft.Frame, coordinates[s.WindingSource.OutputShaft.Id]);
            if (spatial is not null)
            {
                for (var i = 0; i < spatial.Pins.Count; i++)
                {
                    var p = spatial.Pins[i].PositionMm;
                    result.Add(ConnectedPoseIds.Pin(i), Array.AsReadOnly(new[] { 1d,0,0,0,0,1,0,0,0,0,1,0,p.X,p.Y,p.Z,1 }));
                    if (i == spatial.Pins.Count - 1) continue;
                    var x = spatial.Pins[i + 1].PositionMm - p; x /= x.Length;
                    // Display-only roll convention. The spherical-joint mechanical solution has free roll.
                    var reference = Math.Abs(x.Z) < .9 ? new WindingPoint3(0,0,1) : new WindingPoint3(0,1,0);
                    var y = reference.Cross(x); y /= y.Length; var z = x.Cross(y);
                    result.Add(ConnectedPoseIds.Link(i), Array.AsReadOnly(new[] { x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,p.X,p.Y,p.Z,1 }));
                }
                return new(result, null);
            }
            for (var i = 0; i < winding!.Pins.Count; i++)
            {
                var p = winding.Pins[i]; var angle = i < winding.LinkAnglesTurns.Count ? winding.LinkAnglesTurns[i] * 2 * Math.PI : 0;
                var c = Math.Cos(angle); var sin = Math.Sin(angle); var z = s.Winding.Geometry.PlaneZMm;
                result.Add(ConnectedPoseIds.Pin(i), Array.AsReadOnly(new[] { 1d,0,0,0,0,1,0,0,0,0,1,0,p.X,p.Y,z,1 }));
                if (i < winding.LinkAnglesTurns.Count) result.Add(ConnectedPoseIds.Link(i), Array.AsReadOnly(new[] { c,sin,0,0,-sin,c,0,0,0,0,1,0,p.X,p.Y,z,1 }));
            }
            return new(result, null);
        }
        catch (ArgumentException e) { return new(Array.Empty<KeyValuePair<string, ReadOnlyCollection<double>>>(), e.Message); }
    }
}
