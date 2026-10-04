using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Layout;

/// <summary>Binary64 geometry in mm. This is not an exact kinematic coordinate.</summary>
public readonly struct WindingPoint3
{
    public WindingPoint3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public double Length => Math.Sqrt(Dot(this));
    public double Dot(WindingPoint3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public WindingPoint3 Cross(WindingPoint3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public static WindingPoint3 operator +(WindingPoint3 a, WindingPoint3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static WindingPoint3 operator -(WindingPoint3 a, WindingPoint3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static WindingPoint3 operator *(WindingPoint3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static WindingPoint3 operator /(WindingPoint3 a, double k) => a * (1 / k);
    public static WindingPoint3 Of(ExactVector3 p) => new(Number(p.X), Number(p.Y), Number(p.Z));
    private static double Number(Rational r) => (double)r.Numerator / (double)r.Denominator;
}

/// <summary>A rotating pin-centre helix and its fixed exit meridian. Parameter t is unwrapped turns.</summary>
public sealed class HelicalPinGuide
{
    private readonly WindingPoint3 origin, x, y, z;
    public HelicalPinGuide(OrientedFrame frame, double radiusMm, double radiusChangeMmPerTurn,
        double heightChangeMmPerTurn, int hand, double phaseTurns, double exitAzimuthTurns,
        int windingBranch, double maximumGuideTurns)
    {
        Frame = frame; RadiusMm = radiusMm; RadiusChangeMmPerTurn = radiusChangeMmPerTurn;
        HeightChangeMmPerTurn = heightChangeMmPerTurn; Hand = hand; PhaseTurns = phaseTurns;
        ExitAzimuthTurns = exitAzimuthTurns; WindingBranch = windingBranch; MaximumGuideTurns = maximumGuideTurns;
        origin = WindingPoint3.Of(frame.Origin); x = WindingPoint3.Of(frame.X); y = WindingPoint3.Of(frame.Y); z = WindingPoint3.Of(frame.Z);
    }
    public OrientedFrame Frame { get; }
    public double RadiusMm { get; }
    public double RadiusChangeMmPerTurn { get; }
    public double HeightChangeMmPerTurn { get; }
    public int Hand { get; }
    public double PhaseTurns { get; }
    public double ExitAzimuthTurns { get; }
    public int WindingBranch { get; }
    public double MaximumGuideTurns { get; }
    public double ExitParameter(double rotorTurns) => (ExitAzimuthTurns + WindingBranch - PhaseTurns - rotorTurns) / Hand;
    public WindingPoint3 Point(double t, double rotorTurns)
    {
        var a = 2 * Math.PI * (PhaseTurns + Hand * t + rotorTurns); var r = RadiusMm + RadiusChangeMmPerTurn * t;
        return World(new(r * Math.Cos(a), r * Math.Sin(a), HeightChangeMmPerTurn * t));
    }
    public WindingPoint3 Meridian(double t)
    {
        var a = 2 * Math.PI * ExitAzimuthTurns; var r = RadiusMm + RadiusChangeMmPerTurn * t;
        return World(new(r * Math.Cos(a), r * Math.Sin(a), HeightChangeMmPerTurn * t));
    }
    public WindingPoint3 Derivative(double t, double rotorTurns)
    {
        var a = 2 * Math.PI * (PhaseTurns + Hand * t + rotorTurns); var r = RadiusMm + RadiusChangeMmPerTurn * t;
        var w = 2 * Math.PI * Hand; var dr = RadiusChangeMmPerTurn;
        return Direction(new(dr * Math.Cos(a) - w * r * Math.Sin(a), dr * Math.Sin(a) + w * r * Math.Cos(a), HeightChangeMmPerTurn));
    }
    internal double SpeedBound => Math.Sqrt(Math.Pow(2 * Math.PI * (RadiusMm + Math.Abs(RadiusChangeMmPerTurn) * MaximumGuideTurns), 2) +
        RadiusChangeMmPerTurn * RadiusChangeMmPerTurn + HeightChangeMmPerTurn * HeightChangeMmPerTurn);
    internal double AccelerationBound => 4 * Math.PI * Math.PI * (RadiusMm + Math.Abs(RadiusChangeMmPerTurn) * MaximumGuideTurns) + 4 * Math.PI * Math.Abs(RadiusChangeMmPerTurn);
    private WindingPoint3 Direction(WindingPoint3 p) => x * p.X + y * p.Y + z * p.Z;
    private WindingPoint3 World(WindingPoint3 p) => origin + Direction(p);
    internal string? Validate(double pitch)
    {
        if (!Frame.IsProperCardinal || !origin.IsFinite || Math.Max(Math.Abs(origin.X), Math.Max(Math.Abs(origin.Y), Math.Abs(origin.Z))) > 10000) return "UnsupportedWindingFrame";
        var numbers = new[] { RadiusMm, RadiusChangeMmPerTurn, HeightChangeMmPerTurn, PhaseTurns, ExitAzimuthTurns, MaximumGuideTurns };
        if (numbers.Any(v => !double.IsFinite(v) || Math.Abs(v) > 10000) || Math.Abs(WindingBranch) > 32) return "InvalidGuide";
        if (Hand != 1 && Hand != -1 || MaximumGuideTurns < .25 || MaximumGuideTurns > 16 ||
            RadiusMm < 4 * pitch || RadiusMm + RadiusChangeMmPerTurn * MaximumGuideTurns < 4 * pitch ||
            Math.Abs(HeightChangeMmPerTurn) < pitch || Math.Abs(HeightChangeMmPerTurn) > 16 * pitch ||
            Math.Abs(RadiusChangeMmPerTurn) > 4 * pitch) return "UnsupportedGuideBounds";
        return null;
    }
}

/// <summary>Explicitly guided transfer; no implicit slack, tension or unique roll assumption.</summary>
public sealed class SpatialWindingGeometry
{
    public const string Profile = "finite-spatial-guided-pin-chain-v1";
    public const string Policy = "binary64-chord-bracket-residual-v1";
    public const int MaximumLinks = 512;
    public const double LengthToleranceMm = 1e-8;
    public SpatialWindingGeometry(HelicalPinGuide driver, HelicalPinGuide output, IEnumerable<WindingPoint3> bridge,
        int linkCount, double pitchMm, double maxBendDegrees, double maxAttachmentBendDegrees,
        double driverMinimumTurns, double driverMaximumTurns, double outputMinimumTurns, double outputMaximumTurns)
    {
        Driver = driver; Output = output; Bridge = bridge.Take(17).ToList().AsReadOnly(); LinkCount = linkCount; PitchMm = pitchMm;
        MaxBendDegrees = maxBendDegrees; MaxAttachmentBendDegrees = maxAttachmentBendDegrees;
        DriverMinimumTurns = driverMinimumTurns; DriverMaximumTurns = driverMaximumTurns;
        OutputMinimumTurns = outputMinimumTurns; OutputMaximumTurns = outputMaximumTurns;
    }
    public HelicalPinGuide Driver { get; }
    public HelicalPinGuide Output { get; }
    public ReadOnlyCollection<WindingPoint3> Bridge { get; }
    public int LinkCount { get; }
    public double PitchMm { get; }
    public double MaxBendDegrees { get; }
    public double MaxAttachmentBendDegrees { get; }
    public double DriverMinimumTurns { get; }
    public double DriverMaximumTurns { get; }
    public double OutputMinimumTurns { get; }
    public double OutputMaximumTurns { get; }
    public string JointModel => "spherical-free-twist";
    public string LinkRoll => "Underdetermined";
    public string? Validate()
    {
        if (LinkCount < 8 || LinkCount > MaximumLinks || Bridge.Count < 2 || Bridge.Count > 16) return "ResourceLimit";
        var numbers = new[] { PitchMm, MaxBendDegrees, MaxAttachmentBendDegrees, DriverMinimumTurns, DriverMaximumTurns, OutputMinimumTurns, OutputMaximumTurns };
        if (numbers.Any(n => !double.IsFinite(n)) || Bridge.Any(p => !p.IsFinite)) return "InvalidGeometry";
        if (PitchMm < .01 || PitchMm > 100 || MaxBendDegrees <= 0 || MaxBendDegrees > 150 ||
            MaxAttachmentBendDegrees <= 0 || MaxAttachmentBendDegrees > 90) return "UnsupportedJointBounds";
        foreach (var item in new[] { (Driver, DriverMinimumTurns, DriverMaximumTurns), (Output, OutputMinimumTurns, OutputMaximumTurns) })
        {
            var (guide, min, max) = item; var error = guide.Validate(PitchMm); if (error is not null) return error;
            if (min >= max || Math.Abs(min) > 32 || Math.Abs(max) > 32 || max - min > 12) return "UnsupportedFiniteDomain";
            foreach (var q in new[] { min, max })
                if (guide.ExitParameter(q) < .125 || guide.ExitParameter(q) > guide.MaximumGuideTurns - .125) return "InvalidWindingBranch";
        }
        if ((Bridge[0] - Driver.Meridian(Driver.MaximumGuideTurns)).Length > LengthToleranceMm ||
            (Bridge[Bridge.Count - 1] - Output.Meridian(Output.MaximumGuideTurns)).Length > LengthToleranceMm) return "DisconnectedTransferGuide";
        if (Bridge.Zip(Bridge.Skip(1), (a, b) => (b - a).Length).Any(d => d < PitchMm || d > 10000)) return "InvalidTransferGuide";
        return null;
    }
}

public sealed class SpatialWindingPin
{
    internal SpatialWindingPin(WindingPoint3 position, int guidePiece, double parameter)
    { PositionMm = position; GuidePiece = guidePiece; Parameter = parameter; }
    public WindingPoint3 PositionMm { get; }
    public int GuidePiece { get; }
    public double Parameter { get; }
}

public sealed class SpatialWindingPose
{
    internal SpatialWindingPose(double q, double w, IEnumerable<SpatialWindingPin> pins, int lastPiece, double pitchResidual,
        double attachmentResidual, double bend, double attachmentBend, long work, int roots)
    {
        DriverTurns = q; OutputTurns = w; Pins = pins.ToList().AsReadOnly();
        DriverContact = Pins.Count(p => p.GuidePiece == 0); OutputContact = Pins.Count(p => p.GuidePiece == lastPiece);
        PitchResidualMm = pitchResidual; AttachmentResidualMm = attachmentResidual; MaximumBendDegrees = bend;
        MaximumAttachmentBendDegrees = attachmentBend; NumericWork = work; RootBrackets = roots;
    }
    public double DriverTurns { get; }
    public double OutputTurns { get; }
    public ReadOnlyCollection<SpatialWindingPin> Pins { get; }
    public int DriverContact { get; }
    public int OutputContact { get; }
    public double PitchResidualMm { get; }
    public double AttachmentResidualMm { get; }
    public double MaximumBendDegrees { get; }
    public double MaximumAttachmentBendDegrees { get; }
    public long NumericWork { get; }
    public int RootBrackets { get; }
    public string Quality => "NumericResidualOnly";
    public double? SolutionErrorBoundTurns => null;
    public string LinkRoll => "Underdetermined";
}

public sealed class SpatialWindingQuery
{
    internal SpatialWindingQuery(string status, long work, SpatialWindingPose? pose = null) { Status = status; NumericWork = work; Pose = pose; }
    public string Status { get; }
    public long NumericWork { get; }
    public SpatialWindingPose? Pose { get; }
    public bool IsAccepted => Status == "Accepted";
}
