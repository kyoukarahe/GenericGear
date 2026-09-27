using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>A display-only failure; it does not change exact mechanical admission, coordinates or identity.</summary>
public sealed class OpenBeltDisplayUnavailableException : Exception
{
    public OpenBeltDisplayUnavailableException(string message) : base(message) { }
}

public readonly struct OpenBeltDisplayPoint
{
    internal OpenBeltDisplayPoint(double x, double y, double z)
    { OpenBeltDisplay.Finite(x); OpenBeltDisplay.Finite(y); OpenBeltDisplay.Finite(z); X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    internal static OpenBeltDisplayPoint From(ExactVector3 p) => new(OpenBeltDisplay.Number(p.X), OpenBeltDisplay.Number(p.Y), OpenBeltDisplay.Number(p.Z));
    internal static OpenBeltDisplayPoint Add(OpenBeltDisplayPoint a, OpenBeltDisplayPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    internal static OpenBeltDisplayPoint Scale(OpenBeltDisplayPoint a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    internal static OpenBeltDisplayPoint Lerp(OpenBeltDisplayPoint a, OpenBeltDisplayPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}

/// <summary>Finite double sampling of the exact clockwise construction. Never canonical data or a tangency/length proof.</summary>
public sealed class OpenBeltDisplayRoute
{
    private readonly OpenBeltDisplayPoint c1, c2, e, f;
    private readonly double r1, r2, phi;
    internal OpenBeltDisplayRoute(OpenBeltRouteDescriptor route)
    {
        RouteId = route.RouteId; c1 = OpenBeltDisplayPoint.From(route.InputCenterMm); c2 = OpenBeltDisplayPoint.From(route.OutputCenterMm);
        e = OpenBeltDisplayPoint.From(route.CenterDirection); f = OpenBeltDisplayPoint.From(route.SideDirection);
        r1 = OpenBeltDisplay.Number(route.InputPitchRadius.Value); r2 = OpenBeltDisplay.Number(route.OutputPitchRadius.Value);
        var d = OpenBeltDisplay.Number(route.CenterDistance); var h = OpenBeltDisplay.Number(route.H);
        var w = OpenBeltDisplay.Number(route.Radicand);
        if (!(r1 > 0 && r2 > 0 && d > 0 && w > 0 && h > -1 && h < 1)) throw new OpenBeltDisplayUnavailableException("Exact route is outside representable positive display geometry.");
        var k = Math.Sqrt(w); phi = Math.Atan2(k, h);
        SpanLengthMm = d * k;
        InputWrapRadians = Math.PI + 2 * Math.Asin(h); OutputWrapRadians = Math.PI - 2 * Math.Asin(h);
        LoopLengthMm = 2 * SpanLengthMm + r1 * InputWrapRadians + r2 * OutputWrapRadians;
        OpenBeltDisplay.Finite(LoopLengthMm);
        if (!(SpanLengthMm > 0 && InputWrapRadians > 0 && OutputWrapRadians > 0 && LoopLengthMm > 0)) throw new OpenBeltDisplayUnavailableException("Display precision cannot represent this route without degeneracy.");
        P1plus = Point(c1, r1, phi); P2plus = Point(c2, r2, phi);
        P2minus = Point(c2, r2, -phi); P1minus = Point(c1, r1, -phi);
    }
    public string RouteId { get; }
    public string Scope => "ApproximateDisplayOnly";
    public double SpanLengthMm { get; }
    public double InputWrapRadians { get; }
    public double OutputWrapRadians { get; }
    public double LoopLengthMm { get; }
    public OpenBeltDisplayPoint P1plus { get; }
    public OpenBeltDisplayPoint P2plus { get; }
    public OpenBeltDisplayPoint P2minus { get; }
    public OpenBeltDisplayPoint P1minus { get; }
    private OpenBeltDisplayPoint Point(OpenBeltDisplayPoint center, double radius, double angle) =>
        OpenBeltDisplayPoint.Add(center, OpenBeltDisplayPoint.Add(OpenBeltDisplayPoint.Scale(e, radius * Math.Cos(angle)), OpenBeltDisplayPoint.Scale(f, radius * Math.Sin(angle))));
    /// <summary>Distance is along the clockwise loop starting at P1plus. This modulo is belt-loop distance, never pulley turns.</summary>
    public OpenBeltDisplayPoint SampleDistance(double distanceMm)
    {
        OpenBeltDisplay.Finite(distanceMm);
        var s = distanceMm % LoopLengthMm; if (s < 0) s += LoopLengthMm;
        if (s <= SpanLengthMm) return OpenBeltDisplayPoint.Lerp(P1plus, P2plus, s / SpanLengthMm);
        s -= SpanLengthMm;
        if (s <= r2 * OutputWrapRadians) return Point(c2, r2, phi - s / r2);
        s -= r2 * OutputWrapRadians;
        if (s <= SpanLengthMm) return OpenBeltDisplayPoint.Lerp(P2minus, P1minus, s / SpanLengthMm);
        s -= SpanLengthMm; return Point(c1, r1, -phi - s / r1);
    }
    public ReadOnlyCollection<OpenBeltDisplayPoint> SampleLoop(int segments = 128)
    {
        if (segments < 8 || segments > 4096) throw new ArgumentOutOfRangeException(nameof(segments), "Display sampling supports8..4096segments.");
        var points = new List<OpenBeltDisplayPoint>(segments + 1);
        for (var i = 0; i <= segments; i++) points.Add(SampleDistance(LoopLengthMm * i / segments));
        return points.AsReadOnly();
    }
    public OpenBeltDisplayPoint SampleMaterialMark(Rational travelPiCoefficientMm, Rational initialLoopFraction)
    {
        var travel = OpenBeltDisplay.Number(travelPiCoefficientMm) * Math.PI;
        var initial = OpenBeltDisplay.Number(initialLoopFraction) * LoopLengthMm;
        OpenBeltDisplay.Finite(travel); OpenBeltDisplay.Finite(initial);
        return SampleDistance(initial + travel);
    }
}

public static class OpenBeltDisplay
{
    public static OpenBeltDisplayRoute Approximate(OpenBeltRouteDescriptor route) => new(route ?? throw new ArgumentNullException(nameof(route)));
    /// <summary>Bounded visual projection. No mechanical fallback, clipping or tolerance is applied.</summary>
    public static double ApproximateScalar(Rational value) => Number(value);
    internal static double Number(Rational value)
    {
        var result = (double)value.Numerator / (double)value.Denominator; Finite(result);
        if (!value.IsZero && result == 0) throw new OpenBeltDisplayUnavailableException("Exact nonzero value underflows display precision.");
        return result;
    }
    internal static void Finite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1e12)
            throw new OpenBeltDisplayUnavailableException("Display numeric resource bound exceeded; exact mechanics were not clamped.");
    }
}
