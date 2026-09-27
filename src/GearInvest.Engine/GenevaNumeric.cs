using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;
using static GearInvest.Engine.CamNumericalPrimitives;
using Context = GearInvest.Engine.CrankSliderNumerics.Context;
using Grid = GearInvest.Engine.CrankSliderNumerics.Grid;

namespace GearInvest.Engine;

public enum GenevaNumericStatus { ExactValue, EnclosedValue, IncompleteNumericBudget, NumericResourceLimit, InvalidNumericRequest }

/// <summary>Separate angular, linear and direction widths under one aggregate arithmetic-work ceiling.</summary>
public sealed class GenevaNumericRequest
{
    public const string CurrentPolicy = "geneva-dyadic-enclosure-v1";
    public GenevaNumericRequest(ExactQuantity? angularWidth = null, ExactQuantity? linearWidth = null,
        Rational? directionWidth = null, int maximumWork = 32768, int maximumPrecisionBits = 512,
        int maximumRefinements = 4, string? policy = CurrentPolicy)
    {
        AngularWidth = angularWidth ?? ExactQuantity.Turns(new Rational(1, 1000000000000L));
        LinearWidth = linearWidth ?? ExactQuantity.Millimeters(new Rational(1, 1000000000));
        DirectionWidth = directionWidth ?? new Rational(1, 1000000000000L);
        MaximumWork = maximumWork; MaximumPrecisionBits = maximumPrecisionBits; MaximumRefinements = maximumRefinements; Policy = policy;
    }
    public ExactQuantity AngularWidth { get; }
    public ExactQuantity LinearWidth { get; }
    public Rational DirectionWidth { get; }
    public int MaximumWork { get; }
    public int MaximumPrecisionBits { get; }
    public int MaximumRefinements { get; }
    public string? Policy { get; }
    public static GenevaNumericRequest Default => new();
    public string CanonicalRepresentation => Pack(Policy is null ? "0" : "1", Policy ?? "", RotaryLinearProfile.Q(AngularWidth),
        RotaryLinearProfile.Q(LinearWidth), F(DirectionWidth), N(MaximumWork), N(MaximumPrecisionBits), N(MaximumRefinements));
}

/// <summary>Certified rational endpoints; nonzero width does not denote an exact mechanical value.</summary>
public sealed class GenevaInterval
{
    internal GenevaInterval(CrankSliderInterval value) { Lower = value.Lower; Upper = value.Upper; }
    public Rational Lower { get; }
    public Rational Upper { get; }
    public Rational Width => Upper - Lower;
    public bool IsExact => Lower == Upper;
    public string CanonicalRepresentation => Pack(F(Lower), F(Upper));
}

public sealed class GenevaVectorInterval
{
    internal GenevaVectorInterval(CrankSliderVectorInterval value) { X = new(value.X); Y = new(value.Y); Z = new(value.Z); }
    public GenevaInterval X { get; }
    public GenevaInterval Y { get; }
    public GenevaInterval Z { get; }
    public string CanonicalRepresentation => Pack(X.CanonicalRepresentation, Y.CanonicalRepresentation, Z.CanonicalRepresentation);
}

/// <summary>A sampled point with the declaration's stable feature identity. All coordinates are millimeters.</summary>
public sealed class GenevaNumericFeaturePoint
{
    internal GenevaNumericFeaturePoint(string id, CrankSliderVectorInterval point)
    { Id = id; PointMm = new(point); RawPoint = point; }
    public string Id { get; }
    public GenevaVectorInterval PointMm { get; }
    internal CrankSliderVectorInterval RawPoint { get; }
    public string CanonicalRepresentation => Pack(Id, PointMm.CanonicalRepresentation);
}

/// <summary>One or two exact polar terms in the selected body's material frame; no consumer-side Geneva law.</summary>
internal sealed class GenevaNumericFeatureInput
{
    internal GenevaNumericFeatureInput(string id, bool rotatesWithWheel, GenevaLength radius, Rational materialTurns,
        GenevaLength? secondRadius = null, Rational? secondMaterialTurns = null)
    {
        // Authored mechanical IDs permit 160 characters; a sample adds a bounded role suffix.
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl)) throw new ArgumentException("Feature sample ID must be nonempty and bounded to 256 printable characters.");
        if (radius is null) throw new ArgumentNullException(nameof(radius));
        if ((secondRadius is null) != (secondMaterialTurns is null)) throw new ArgumentException("A second polar term requires both radius and direction.");
        Id = id; RotatesWithWheel = rotatesWithWheel; Radius = radius; MaterialTurns = materialTurns;
        SecondRadius = secondRadius; SecondMaterialTurns = secondMaterialTurns;
    }
    internal string Id { get; }
    internal bool RotatesWithWheel { get; }
    internal GenevaLength Radius { get; }
    internal Rational MaterialTurns { get; }
    internal GenevaLength? SecondRadius { get; }
    internal Rational? SecondMaterialTurns { get; }
}

public sealed class GenevaNumericPose
{
    internal GenevaNumericPose(CrankSliderInterval residual, CrankSliderInterval shaft, CrankSliderInterval terminal,
        CrankSliderVectorInterval pin, CrankSliderVectorInterval radial, CrankSliderVectorInterval wheelE,
        CrankSliderVectorInterval wheelF, CrankSliderInterval radius, CrankSliderInterval orbit,
        CrankSliderVectorInterval driverE, CrankSliderVectorInterval driverF,
        IReadOnlyList<GenevaNumericFeaturePoint>? featurePoints = null)
    {
        ResidualTurns = new(residual); OutputShaftTurns = new(shaft); TerminalTurns = new(terminal);
        PinMm = new(pin); PinRadialUnit = new(radial); WheelE = new(wheelE); WheelF = new(wheelF);
        DriverE = new(driverE); DriverF = new(driverF);
        PinRadiusMm = new(radius); PinOrbitRadiusMm = new(orbit);
        FeaturePoints = (featurePoints ?? Array.Empty<GenevaNumericFeaturePoint>()).ToList().AsReadOnly();
    }
    public GenevaInterval ResidualTurns { get; }
    public GenevaInterval OutputShaftTurns { get; }
    public GenevaInterval TerminalTurns { get; }
    public GenevaVectorInterval PinMm { get; }
    /// <summary>Unit direction from wheel center to pin. An active-slot direction only when pin engagement is admitted.</summary>
    public GenevaVectorInterval PinRadialUnit { get; }
    /// <summary>Actual driver material basis at the pin orbit phase, distinct from the wheel-center-to-pin radial direction.</summary>
    public GenevaVectorInterval DriverE { get; }
    public GenevaVectorInterval DriverF { get; }
    public GenevaVectorInterval WheelE { get; }
    public GenevaVectorInterval WheelF { get; }
    public GenevaInterval PinRadiusMm { get; }
    public GenevaInterval PinOrbitRadiusMm { get; }
    public ReadOnlyCollection<GenevaNumericFeaturePoint> FeaturePoints { get; }
    internal static CrankSliderInterval Raw(GenevaInterval value) => new(value.Lower, value.Upper);
    internal static CrankSliderVectorInterval Raw(GenevaVectorInterval value) => new(Raw(value.X), Raw(value.Y), Raw(value.Z));
    internal GenevaNumericPose WithFeatures(IReadOnlyList<GenevaNumericFeaturePoint> points) => new(Raw(ResidualTurns), Raw(OutputShaftTurns), Raw(TerminalTurns),
        Raw(PinMm), Raw(PinRadialUnit), Raw(WheelE), Raw(WheelF), Raw(PinRadiusMm), Raw(PinOrbitRadiusMm), Raw(DriverE), Raw(DriverF), points);
    public string CanonicalRepresentation => Pack(ResidualTurns.CanonicalRepresentation, OutputShaftTurns.CanonicalRepresentation,
        TerminalTurns.CanonicalRepresentation, PinMm.CanonicalRepresentation, PinRadialUnit.CanonicalRepresentation,
        DriverE.CanonicalRepresentation, DriverF.CanonicalRepresentation, WheelE.CanonicalRepresentation, WheelF.CanonicalRepresentation, PinRadiusMm.CanonicalRepresentation, PinOrbitRadiusMm.CanonicalRepresentation,
        Pack(FeaturePoints.Select(p => p.CanonicalRepresentation).ToArray()));
}

public sealed class GenevaNumericComputation
{
    internal GenevaNumericComputation(GenevaNumericStatus status, int work, int bits, int passes,
        GenevaNumericPose? pose, GenevaNumericPose? diagnostic, string detail)
    { Status = status; Work = work; PrecisionBits = bits; Refinements = passes; Pose = pose; DiagnosticPose = diagnostic; Detail = detail; }
    public GenevaNumericStatus Status { get; }
    public int Work { get; }
    public int PrecisionBits { get; }
    public int Refinements { get; }
    public GenevaNumericPose? Pose { get; }
    public GenevaNumericPose? DiagnosticPose { get; }
    public string Detail { get; }
    public bool IsAvailable => Status == GenevaNumericStatus.ExactValue || Status == GenevaNumericStatus.EnclosedValue;
    public string CanonicalRepresentation => Pack(Status.ToString(), N(Work), N(PrecisionBits), N(Refinements),
        Pose?.CanonicalRepresentation ?? "none", DiagnosticPose?.CanonicalRepresentation ?? "none", Detail);
}

/// <summary>Independent driver observation. It never grants a Geneva output pose.</summary>
public sealed class GenevaDriverNumericComputation
{
    internal GenevaDriverNumericComputation(GenevaNumericStatus status, int work, int bits, int passes,
        CrankSliderVectorInterval? pin, CrankSliderVectorInterval? e, CrankSliderVectorInterval? f, string detail,
        IReadOnlyList<GenevaNumericFeaturePoint>? featurePoints = null)
    {
        Status = status; Work = work; PrecisionBits = bits; Refinements = passes; Detail = detail;
        PinMm = pin is null ? null : new GenevaVectorInterval(pin);
        DriverE = e is null ? null : new GenevaVectorInterval(e); DriverF = f is null ? null : new GenevaVectorInterval(f);
        FeaturePoints = (featurePoints ?? Array.Empty<GenevaNumericFeaturePoint>()).ToList().AsReadOnly();
    }
    public GenevaNumericStatus Status { get; }
    public int Work { get; }
    public int PrecisionBits { get; }
    public int Refinements { get; }
    public GenevaVectorInterval? PinMm { get; }
    public GenevaVectorInterval? DriverE { get; }
    public GenevaVectorInterval? DriverF { get; }
    public ReadOnlyCollection<GenevaNumericFeaturePoint> FeaturePoints { get; }
    public string Detail { get; }
    public bool IsAvailable => Status == GenevaNumericStatus.ExactValue || Status == GenevaNumericStatus.EnclosedValue;
    public string CanonicalRepresentation => Pack(Status.ToString(), N(Work), N(PrecisionBits), N(Refinements),
        PinMm?.CanonicalRepresentation ?? "none", DriverE?.CanonicalRepresentation ?? "none", DriverF?.CanonicalRepresentation ?? "none", Detail,
        Pack(FeaturePoints.Select(p => p.CanonicalRepresentation).ToArray()));
}

/// <summary>Already admitted geometry and exactly selected phase/residual, supplied by the mechanism evaluator.</summary>
internal sealed class GenevaNumericInput
{
    internal GenevaNumericInput(Rational localPhaseTurns, Rational centerDistanceMm, GenevaLength orbitRadius,
        Rational exactAccumulatedTurns, Rational? exactResidualTurns, int residualCoefficient, int terminalSign,
        Rational terminalDatumTurns, Rational wheelMaterialPhaseTurns, ExactVector3 driverCenterMm,
        ExactVector3 wheelCenterMm, ExactVector3 e, ExactVector3 f, IReadOnlyList<GenevaNumericFeatureInput>? features = null)
    {
        LocalPhaseTurns = localPhaseTurns; CenterDistanceMm = centerDistanceMm; OrbitRadius = orbitRadius;
        ExactAccumulatedTurns = exactAccumulatedTurns; ExactResidualTurns = exactResidualTurns; ResidualCoefficient = residualCoefficient;
        TerminalSign = terminalSign; TerminalDatumTurns = terminalDatumTurns; WheelMaterialPhaseTurns = wheelMaterialPhaseTurns;
        DriverCenterMm = driverCenterMm; WheelCenterMm = wheelCenterMm; E = e; F = f;
        if (!GenevaNumerics.ValidFeatures(features, false)) throw new ArgumentException("Feature samples require at most 128 unique stable IDs.");
        Features = (features ?? Array.Empty<GenevaNumericFeatureInput>()).ToList().AsReadOnly();
    }
    internal Rational LocalPhaseTurns { get; }
    internal Rational CenterDistanceMm { get; }
    internal GenevaLength OrbitRadius { get; }
    internal Rational ExactAccumulatedTurns { get; }
    internal Rational? ExactResidualTurns { get; }
    internal int ResidualCoefficient { get; }
    internal int TerminalSign { get; }
    internal Rational TerminalDatumTurns { get; }
    internal Rational WheelMaterialPhaseTurns { get; }
    internal ExactVector3 DriverCenterMm { get; }
    internal ExactVector3 WheelCenterMm { get; }
    internal ExactVector3 E { get; }
    internal ExactVector3 F { get; }
    internal IReadOnlyList<GenevaNumericFeatureInput> Features { get; }
}

/// <summary>29A reuses immutable 27A/28A primitives and adds an arbitrary-argument certified atan.</summary>
internal static class GenevaNumerics
{
    internal static bool ValidFeatures(IReadOnlyList<GenevaNumericFeatureInput>? features, bool driverOnly)
    {
        if (features is null) return true;
        if (features.Count > 128) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in features)
            if (feature is null || !ids.Add(feature.Id) || (driverOnly && feature.RotatesWithWheel)) return false;
        return true;
    }
    internal static bool Valid(GenevaNumericRequest? r) => r is not null && r.Policy == GenevaNumericRequest.CurrentPolicy &&
        r.AngularWidth.Kind == QuantityKind.AngularPosition && r.LinearWidth.Kind == QuantityKind.LinearPosition &&
        r.AngularWidth.Value > 0 && r.LinearWidth.Value > 0 && r.DirectionWidth > 0 &&
        InputBound(r.AngularWidth.Value) && InputBound(r.LinearWidth.Value) && InputBound(r.DirectionWidth) &&
        r.MaximumWork >= 0 && r.MaximumWork <= 262144 && Precisions.Contains(r.MaximumPrecisionBits) &&
        r.MaximumRefinements >= 1 && r.MaximumRefinements <= 7;

    internal static GenevaNumericStatus Status(CrankSliderNumerics.Stop stop) =>
        stop.Status == CrankSliderNumericStatus.NumericResourceLimit ? GenevaNumericStatus.NumericResourceLimit : GenevaNumericStatus.IncompleteNumericBudget;

    internal static (CrankSliderInterval Sin, CrankSliderInterval Cos) SinCosTurns(Context c, Rational turns)
    {
        c.Check(turns); c.Step();
        var remainder = turns.Numerator % turns.Denominator;
        if (remainder.Sign < 0) remainder += turns.Denominator;
        var phase = new Rational(remainder, turns.Denominator);
        var quarters = c.Multiply(phase, 4);
        if (quarters.Denominator.IsOne)
        {
            var q = (int)quarters.Numerator;
            return (Point(q == 1 ? 1 : q == 3 ? -1 : 0), Point(q == 0 ? 1 : q == 2 ? -1 : 0));
        }
        return CrankSliderNumerics.SinCos(c, phase);
    }

    internal static CrankSliderInterval EncloseLength(Context c, GenevaLength length) => length.ExactMillimeters is Rational exact
        ? c.Interval(exact, exact) : c.Scale(SinCosTurns(c, length.AngleTurns).Sin, length.CoefficientMm);

    internal static CrankSliderInterval LengthDifference(Context c, GenevaLength left, GenevaLength right) =>
        left.Equals(right) ? Point(0) : c.Subtract(EncloseLength(c, left), EncloseLength(c, right));

    internal static CrankSliderInterval Divide(Context c, CrankSliderInterval numerator, CrankSliderInterval positiveDenominator) =>
        Multiply(c, numerator, Reciprocal(c, positiveDenominator));

    private static Grid GridBounds(Context c, CrankSliderInterval value) => new(c.ToGrid(value.Lower).L, c.ToGrid(value.Upper).H);

    /// <summary>Monotonic endpoint evaluation with sign and reciprocal reduction, then one half-angle reduction.</summary>
    internal static CrankSliderInterval Atan(Context c, CrankSliderInterval argument)
    {
        c.Check(argument.Lower); c.Check(argument.Upper);
        var lower = AtanPoint(c, argument.Lower);
        if (argument.IsExact) return lower;
        var upper = AtanPoint(c, argument.Upper);
        return c.Interval(lower.Lower, upper.Upper);
    }

    private static CrankSliderInterval AtanPoint(Context c, Rational z)
    {
        if (z.IsZero) return Point(0);
        if (z.Sign < 0) return c.Scale(AtanPoint(c, -z), -1);
        if (z > 1) return c.Subtract(c.Scale(Pi(c), new Rational(1, 2)), AtanPoint(c, c.Divide(1, z)));
        // For 0<z<=1, t=z/(1+sqrt(1+z*z)) lies in [0,sqrt(2)-1], a strict subset of [0,1/2].
        // All range-reduction operations are outward. atan(z)=2*atan(t) exactly.
        var root = c.Sqrt(c.Add(Point(1), c.Square(Point(z))), true);
        var reduced = Divide(c, Point(z), c.Add(Point(1), root));
        var t = GridBounds(c, reduced);
        if (t.L < 0 || 2 * t.H > c.ScaleGrid) throw new InvalidOperationException("Geneva atan reduction escaped its proved [0,1/2] domain.");
        var square = c.GridMultiply(t, t); var power = t; var sum = new Grid(0, 0);
        for (var k = 0; ; k++)
        {
            c.Step();
            var term = c.GridDivide(power, 2 * k + 1);
            sum = k % 2 == 0 ? c.GridAdd(sum, term) : c.GridSubtract(sum, term);
            power = c.GridMultiply(power, square);
            var next = c.GridDivide(power, 2 * k + 3);
            if (next.H <= 1)
            {
                // On [0,1/2] the alternating odd-power terms decrease. The first omitted term
                // bounds the signed tail for every t in the input interval; rounding only widens it.
                sum = c.GridAdd(sum, k % 2 == 0 ? new Grid(-next.H, 0) : new Grid(0, next.H));
                return c.FromGrid(c.GridScale(sum, 2));
            }
        }
    }

    internal static CrankSliderVectorInterval Vector(Context c, ExactVector3 origin, ExactVector3 e,
        CrankSliderInterval x, ExactVector3 f, CrankSliderInterval y) => new(
        c.Add(Point(origin.X), c.Add(c.Scale(x, e.X), c.Scale(y, f.X))),
        c.Add(Point(origin.Y), c.Add(c.Scale(x, e.Y), c.Scale(y, f.Y))),
        c.Add(Point(origin.Z), c.Add(c.Scale(x, e.Z), c.Scale(y, f.Z))));

    private static GenevaNumericFeaturePoint[] SampleFeatures(Context c, IReadOnlyList<GenevaNumericFeatureInput> features,
        ExactVector3 driverOrigin, CrankSliderVectorInterval? driverE, CrankSliderVectorInterval? driverF,
        ExactVector3 wheelOrigin, CrankSliderVectorInterval? wheelE, CrankSliderVectorInterval? wheelF)
    {
        var points = new GenevaNumericFeaturePoint[features.Count];
        // Reuse identical declared polar terms within this pass only. No cache survives a precision pass or evaluation.
        var lengths = new Dictionary<GenevaLength, CrankSliderInterval>();
        var directions = new Dictionary<Rational, (CrankSliderInterval Sin, CrankSliderInterval Cos)>();
        CrankSliderInterval Length(GenevaLength value)
        {
            if (!lengths.TryGetValue(value, out var enclosure)) { enclosure = EncloseLength(c, value); lengths.Add(value, enclosure); }
            return enclosure;
        }
        (CrankSliderInterval Sin, CrankSliderInterval Cos) Direction(Rational value)
        {
            if (!directions.TryGetValue(value, out var enclosure)) { enclosure = SinCosTurns(c, value); directions.Add(value, enclosure); }
            return enclosure;
        }
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i]; c.Step();
            var origin = feature.RotatesWithWheel ? wheelOrigin : driverOrigin;
            var e = feature.RotatesWithWheel ? wheelE : driverE;
            var f = feature.RotatesWithWheel ? wheelF : driverF;
            if (e is null || f is null) throw new InvalidOperationException("A feature sample requires its actually evaluated body basis.");
            var (sin, cos) = Direction(feature.MaterialTurns); var radius = Length(feature.Radius);
            var x = Multiply(c, radius, cos); var y = Multiply(c, radius, sin);
            if (feature.SecondRadius is not null)
            {
                var (secondSin, secondCos) = Direction(feature.SecondMaterialTurns!.Value);
                var secondRadius = Length(feature.SecondRadius);
                x = c.Add(x, Multiply(c, secondRadius, secondCos)); y = c.Add(y, Multiply(c, secondRadius, secondSin));
            }
            var point = new CrankSliderVectorInterval(
                c.Add(Point(origin.X), c.Add(Multiply(c, e.X, x), Multiply(c, f.X, y))),
                c.Add(Point(origin.Y), c.Add(Multiply(c, e.Y, x), Multiply(c, f.Y, y))),
                c.Add(Point(origin.Z), c.Add(Multiply(c, e.Z, x), Multiply(c, f.Z, y))));
            points[i] = new(feature.Id, point);
        }
        return points;
    }

    private static IEnumerable<CrankSliderInterval> FeatureCoordinates(IEnumerable<GenevaNumericFeaturePoint> features) =>
        features.SelectMany(p => new[] { p.RawPoint.X, p.RawPoint.Y, p.RawPoint.Z });

    internal static GenevaDriverNumericComputation EvaluateDriver(Rational localPhaseTurns, GenevaLength selectedOrbitRadius,
        ExactVector3 driverCenterMm, ExactVector3 e, ExactVector3 f, GenevaNumericRequest request,
        IReadOnlyList<GenevaNumericFeatureInput>? features = null)
    {
        if (!Valid(request)) return new(GenevaNumericStatus.InvalidNumericRequest, 0, 0, 0, null, null, null, "Invalid width, units, policy or bounded numerical options.");
        if (!ValidFeatures(features, true)) return new(GenevaNumericStatus.InvalidNumericRequest, 0, 0, 0, null, null, null, "Driver feature samples require at most 128 unique IDs and cannot use a wheel frame.");
        var inputs = (features ?? Array.Empty<GenevaNumericFeatureInput>()).ToArray();
        var c = new Context(request.MaximumWork); int bits = 0, passes = 0;
        try
        {
            foreach (var value in new[] { localPhaseTurns, driverCenterMm.X, driverCenterMm.Y, driverCenterMm.Z,
                e.X, e.Y, e.Z, f.X, f.Y, f.Z }) c.Check(value);
            foreach (var precision in Precisions)
            {
                if (precision > request.MaximumPrecisionBits) break;
                if (passes >= request.MaximumRefinements) throw new CrankSliderNumerics.Stop(CrankSliderNumericStatus.IncompleteNumericBudget, "Refinement-pass budget exhausted.");
                c.Step(); bits = precision; passes++; c.SetPrecision(precision);
                var (sin, cos) = SinCosTurns(c, localPhaseTurns); var radius = EncloseLength(c, selectedOrbitRadius);
                var pin = Vector(c, driverCenterMm, e, Multiply(c, radius, cos), f, Multiply(c, radius, sin));
                var driverE = Vector(c, ExactVector3.Zero, e, cos, f, sin);
                var driverF = Vector(c, ExactVector3.Zero, e, c.Scale(sin, -1), f, cos);
                var points = SampleFeatures(c, inputs, driverCenterMm, driverE, driverF, ExactVector3.Zero, null, null);
                var positions = new[] { pin.X, pin.Y, pin.Z, radius }.Concat(FeatureCoordinates(points)).ToArray();
                var directions = new[] { driverE.X, driverE.Y, driverE.Z, driverF.X, driverF.Y, driverF.Z };
                if (positions.All(i => c.Width(i) <= request.LinearWidth.Value) && directions.All(i => c.Width(i) <= request.DirectionWidth))
                    return new(positions.Concat(directions).All(i => i.IsExact) ? GenevaNumericStatus.ExactValue : GenevaNumericStatus.EnclosedValue,
                        c.Work, bits, passes, pin, driverE, driverF, "Driver observation widths satisfied; no output pose is authorized.", points);
            }
            return new(GenevaNumericStatus.NumericResourceLimit, c.Work, bits, passes, null, null, null, "Maximum precision reached before driver widths were proved.");
        }
        catch (CrankSliderNumerics.Stop stop) { return new(Status(stop), c.Work, bits, passes, null, null, null, stop.Message); }
    }

    internal static GenevaNumericComputation Evaluate(GenevaNumericInput input, GenevaNumericRequest request)
    {
        if (!Valid(request)) return new(GenevaNumericStatus.InvalidNumericRequest, 0, 0, 0, null, null, "Invalid width, units, policy or bounded numerical options.");
        var c = new Context(request.MaximumWork); GenevaNumericPose? last = null; int bits = 0, passes = 0;
        try
        {
            foreach (var value in new[] { input.LocalPhaseTurns, input.CenterDistanceMm, input.ExactAccumulatedTurns,
                input.ExactResidualTurns ?? 0, input.TerminalDatumTurns, input.WheelMaterialPhaseTurns,
                input.DriverCenterMm.X, input.DriverCenterMm.Y, input.DriverCenterMm.Z,
                input.WheelCenterMm.X, input.WheelCenterMm.Y, input.WheelCenterMm.Z,
                input.E.X, input.E.Y, input.E.Z, input.F.X, input.F.Y, input.F.Z }) c.Check(value);
            if (input.CenterDistanceMm <= 0 || (input.ResidualCoefficient != 1 && input.ResidualCoefficient != -1) ||
                (input.TerminalSign != 1 && input.TerminalSign != -1)) throw new ArgumentException("Geneva numeric kernel requires admitted geometry and explicit scalar signs.");
            foreach (var precision in Precisions)
            {
                if (precision > request.MaximumPrecisionBits) break;
                if (passes >= request.MaximumRefinements) throw new CrankSliderNumerics.Stop(CrankSliderNumericStatus.IncompleteNumericBudget, "Refinement-pass budget exhausted.");
                c.Step(); bits = precision; passes++; c.SetPrecision(precision);
                var (sin, cos) = SinCosTurns(c, input.LocalPhaseTurns);
                var orbit = EncloseLength(c, input.OrbitRadius);
                var x = Multiply(c, orbit, cos); var y = Multiply(c, orbit, sin);
                var denominator = c.Subtract(Point(input.CenterDistanceMm), x);
                if (denominator.Lower <= 0) continue; // Insufficient enclosure, never a guessed division sign.
                var radial = c.Sqrt(c.Add(c.Square(denominator), c.Square(y)), true);
                if (radial.Lower <= 0) continue;
                var radialCos = Divide(c, denominator, radial); var radialSin = c.Scale(Divide(c, y, radial), -1);
                CrankSliderInterval residual, xiCos, xiSin;
                if (input.ExactResidualTurns is Rational exact)
                { residual = Point(exact); (xiSin, xiCos) = SinCosTurns(c, exact); }
                else
                {
                    residual = Divide(c, Atan(c, Divide(c, c.Scale(y, -1), denominator)), c.Scale(Pi(c), 2));
                    xiCos = radialCos; xiSin = radialSin;
                }
                var shaft = c.Add(Point(input.ExactAccumulatedTurns), c.Scale(residual, input.ResidualCoefficient));
                var terminal = c.Add(c.Scale(shaft, input.TerminalSign), Point(input.TerminalDatumTurns));
                var pin = Vector(c, input.DriverCenterMm, input.E, x, input.F, y);
                var pinDirection = Vector(c, ExactVector3.Zero, input.E, c.Scale(radialCos, -1), input.F, c.Scale(radialSin, -1));
                var (phaseSin, phaseCos) = SinCosTurns(c, input.WheelMaterialPhaseTurns);
                var wheelCos = c.Subtract(Multiply(c, phaseCos, xiCos), Multiply(c, phaseSin, xiSin));
                var wheelSin = c.Add(Multiply(c, phaseSin, xiCos), Multiply(c, phaseCos, xiSin));
                var wheelE = Vector(c, ExactVector3.Zero, input.E, wheelCos, input.F, wheelSin);
                var wheelF = Vector(c, ExactVector3.Zero, input.E, c.Scale(wheelSin, -1), input.F, wheelCos);
                var driverE = Vector(c, ExactVector3.Zero, input.E, cos, input.F, sin);
                var driverF = Vector(c, ExactVector3.Zero, input.E, c.Scale(sin, -1), input.F, cos);
                var points = SampleFeatures(c, input.Features, input.DriverCenterMm, driverE, driverF, input.WheelCenterMm, wheelE, wheelF);
                last = new(residual, shaft, terminal, pin, pinDirection, wheelE, wheelF, radial, orbit, driverE, driverF, points);
                var angles = new[] { residual, shaft, terminal };
                var lengths = new[] { pin.X, pin.Y, pin.Z, radial, orbit }.Concat(FeatureCoordinates(points)).ToArray();
                var directions = new[] { pinDirection.X, pinDirection.Y, pinDirection.Z, driverE.X, driverE.Y, driverE.Z, driverF.X, driverF.Y, driverF.Z, wheelE.X, wheelE.Y, wheelE.Z, wheelF.X, wheelF.Y, wheelF.Z };
                if (angles.All(i => c.Width(i) <= request.AngularWidth.Value) && lengths.All(i => c.Width(i) <= request.LinearWidth.Value) &&
                    directions.All(i => c.Width(i) <= request.DirectionWidth))
                {
                    var allExact = angles.Concat(lengths).Concat(directions).All(i => i.IsExact);
                    return new(allExact ? GenevaNumericStatus.ExactValue : GenevaNumericStatus.EnclosedValue, c.Work, bits, passes, last, null,
                        "Separate angular, linear and direction widths satisfied under one arithmetic budget.");
                }
            }
            return new(GenevaNumericStatus.NumericResourceLimit, c.Work, bits, passes, null, last, "Maximum precision reached before all requested widths were proved.");
        }
        catch (CrankSliderNumerics.Stop stop) { return new(Status(stop), c.Work, bits, passes, null, last, stop.Message); }
    }

    // Optional features reuse the actual already-computed body bases; they do not rerun the leaf pose or phase kernel.
    internal static GenevaNumericComputation EvaluateFeatures(GenevaNumericInput input, GenevaNumericComputation evaluated, GenevaNumericRequest request)
    {
        if (!Valid(request)) return new(GenevaNumericStatus.InvalidNumericRequest, 0, 0, 0, null, null, "Invalid feature request.");
        if (!evaluated.IsAvailable || evaluated.Pose is null) throw new ArgumentException("Feature sampling requires an available actual body pose.");
        var source = evaluated.Pose; var c = new Context(request.MaximumWork); var bits = 0; var passes = 0; GenevaNumericPose? last = null;
        try
        {
            foreach (var precision in Precisions)
            {
                if (precision > request.MaximumPrecisionBits) break;
                if (precision < evaluated.PrecisionBits) continue;
                if (passes >= request.MaximumRefinements) throw new CrankSliderNumerics.Stop(CrankSliderNumericStatus.IncompleteNumericBudget, "Feature refinement-pass budget exhausted.");
                c.Step(); c.SetPrecision(precision); bits = precision; passes++;
                var points = SampleFeatures(c, input.Features, input.DriverCenterMm, GenevaNumericPose.Raw(source.DriverE), GenevaNumericPose.Raw(source.DriverF),
                    input.WheelCenterMm, GenevaNumericPose.Raw(source.WheelE), GenevaNumericPose.Raw(source.WheelF));
                last = source.WithFeatures(points);
                if (FeatureCoordinates(points).All(x => c.Width(x) <= request.LinearWidth.Value))
                    return new(FeatureCoordinates(points).All(x => x.IsExact) ? GenevaNumericStatus.ExactValue : GenevaNumericStatus.EnclosedValue,
                        c.Work, bits, passes, last, null, "Feature widths certified from the actual evaluated body bases.");
            }
            return new(GenevaNumericStatus.NumericResourceLimit, c.Work, bits, passes, null, last, "Feature precision or inherited pose enclosure did not meet the requested widths.");
        }
        catch (CrankSliderNumerics.Stop stop) { return new(Status(stop), c.Work, bits, passes, null, last, stop.Message); }
    }
}
