using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class WormDriveDisplayUnavailableException : Exception
{ public WormDriveDisplayUnavailableException(string message) : base(message) { } }

public readonly struct WormDriveDisplayPoint
{
    internal WormDriveDisplayPoint(double x, double y, double z)
    { WormDriveDisplay.Finite(x); WormDriveDisplay.Finite(y); WormDriveDisplay.Finite(z); X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

public sealed class WormDriveDisplayPolyline
{
    internal WormDriveDisplayPolyline(string id, IEnumerable<WormDriveDisplayPoint> points)
    { Id = id; Points = points.ToList().AsReadOnly(); }
    public string Id { get; }
    public ReadOnlyCollection<WormDriveDisplayPoint> Points { get; }
}

/// <summary>Schematic pitch-cylinder helix and wheel local traces, not manufactured tooth flanks or collision geometry.</summary>
public sealed class WormDriveDisplayPose
{
    internal WormDriveDisplayPose(string analysisId, WormDriveGeometryDescriptor geometry, ExactQuantity input, IEnumerable<WormDriveDisplayPolyline> helices,
        IEnumerable<WormDriveDisplayPolyline> teeth, WormDriveDisplayPoint wormMark, WormDriveDisplayPoint wheelMark)
    {
        AnalysisId = analysisId; GeometryId = geometry.GeometryId; Input = input;
        var g = geometry;
        InputCenterMm = WormDriveDisplay.ApproximatePoint(g.InputPitchCenterMm); OutputCenterMm = WormDriveDisplay.ApproximatePoint(g.OutputPitchCenterMm);
        PitchPointMm = WormDriveDisplay.ApproximatePoint(g.PitchPointMm);
        WormPitchRadiusMm = WormDriveDisplay.ApproximateScalar(g.WormPitchRadiusMm); WheelPitchRadiusMm = WormDriveDisplay.ApproximateScalar(g.WheelPitchRadiusMm);
        AxialPitchMm = Math.PI * WormDriveDisplay.ApproximateScalar(g.AxialPitchPiCoefficientMm);
        LeadMm = Math.PI * WormDriveDisplay.ApproximateScalar(g.LeadPiCoefficientMm);
        LeadAngleRadians = Math.Atan(WormDriveDisplay.ApproximateScalar(g.LeadSlope));
        WormDriveDisplay.Finite(AxialPitchMm); WormDriveDisplay.Finite(LeadMm); WormDriveDisplay.Finite(LeadAngleRadians);
        WormHelices = helices.ToList().AsReadOnly(); WheelToothTraces = teeth.ToList().AsReadOnly();
        WormMaterialMarker = wormMark; WheelMaterialMarker = wheelMark;
    }
    public string AnalysisId { get; }
    public string GeometryId { get; }
    public ExactQuantity Input { get; }
    public string Scope => "ApproximateDisplayOnly";
    public string GeometryClaim => "SchematicPitchHelixAndLocalWheelTraceOnly";
    public WormDriveDisplayPoint InputCenterMm { get; }
    public WormDriveDisplayPoint OutputCenterMm { get; }
    public WormDriveDisplayPoint PitchPointMm { get; }
    public double WormPitchRadiusMm { get; }
    public double WheelPitchRadiusMm { get; }
    public double AxialPitchMm { get; }
    public double LeadMm { get; }
    public double LeadAngleRadians { get; }
    public ReadOnlyCollection<WormDriveDisplayPolyline> WormHelices { get; }
    public ReadOnlyCollection<WormDriveDisplayPolyline> WheelToothTraces { get; }
    public WormDriveDisplayPoint WormMaterialMarker { get; }
    public WormDriveDisplayPoint WheelMaterialMarker { get; }
}

public static class WormDriveDisplay
{
    /// <summary>Stateless optional sampling of a current successful evaluation; exact input is reduced only for periodic display angles.</summary>
    public static WormDriveDisplayPose Approximate(WormDriveAnalysis analysis, WormDriveEvaluation evaluation,
        int helixSegments = 96, int wheelTraceSegments = 8)
    {
        if (analysis is null || evaluation is null) throw new ArgumentNullException();
        if (analysis.AnalysisId != evaluation.AnalysisId || !analysis.LocalCompatibility.IsAdmitted ||
            analysis.LocalCompatibility.Geometry?.HasExactProof != true || evaluation.Status != WormDriveEvaluationStatus.Success || evaluation.Output is null)
            throw new WormDriveDisplayUnavailableException("A current admitted analysis and matching successful output evaluation are required; no stale or zero pose is substituted.");
        var d = analysis.Draft.Definition;
        return ApproximateCore(d.Device, () => d.SourceMapping!.FrameMm(d.Source.Definition.Shafts.Single(s => s.Id == d.Device.InputShaftId).Frame), analysis.LocalCompatibility,
            evaluation.Output, analysis.AnalysisId, evaluation.Input, helixSegments, wheelTraceSegments);
    }

    internal static WormDriveDisplayPose ApproximateLocal(WormDriveTransmissionDefinition device, OrientedFrame inputFrameMm,
        WormDriveCompatibilityResult local, WormDriveOutputEvaluation output, string analysisId, ExactQuantity rootInput,
        int helixSegments = 96, int wheelTraceSegments = 8) =>
        ApproximateCore(device, () => inputFrameMm, local, output, analysisId, rootInput, helixSegments, wheelTraceSegments);

    private static WormDriveDisplayPose ApproximateCore(WormDriveTransmissionDefinition device, Func<OrientedFrame> getInputFrameMm,
        WormDriveCompatibilityResult local, WormDriveOutputEvaluation output, string analysisId, ExactQuantity rootInput,
        int helixSegments, int wheelTraceSegments)
        => ApproximateMaterialCore(device, getInputFrameMm, local,
            Angle(local.Epsilon!.Value * (output.InputWormTurns.Value + device.InputMountingPhase.Value)),
            Angle(local.Sigma!.Value * (output.ShaftTurns.Value + device.OutputMountingPhase.Value)), analysisId, rootInput, helixSegments, wheelTraceSegments);

    internal static WormDriveDisplayPose ApproximateMaterialLocal(WormDriveTransmissionDefinition device, OrientedFrame inputFrameMm,
        WormDriveCompatibilityResult local, double wormPhase, double wheelPhase, string analysisId, ExactQuantity rootInput,
        CrankSliderNumerics.Context work) =>
        ApproximateMaterialCore(device, () => inputFrameMm, local, wormPhase, wheelPhase, analysisId, rootInput, 96, 8, work);

    private static WormDriveDisplayPose ApproximateMaterialCore(WormDriveTransmissionDefinition device, Func<OrientedFrame> getInputFrameMm,
        WormDriveCompatibilityResult local, double wormPhase, double wheelPhase, string analysisId, ExactQuantity rootInput,
        int helixSegments, int wheelTraceSegments, CrankSliderNumerics.Context? work = null)
    {
        if (!local.IsAdmitted || local.Geometry?.HasExactProof != true)
            throw new WormDriveDisplayUnavailableException("A current admitted analysis and matching successful output evaluation are required; no stale or zero pose is substituted.");
        if (helixSegments < 16 || helixSegments > 1024 || wheelTraceSegments < 2 || wheelTraceSegments > 32)
            throw new ArgumentOutOfRangeException(nameof(helixSegments), "Display supports 16..1024 helix and 2..32 wheel-trace segments.");
        var c = device; var geometry = local.Geometry;
        var w = c.Worm; var g = c.SelectedWheel!;
        if ((long)w.Starts * (helixSegments + 1) + (long)g.ToothCount * (wheelTraceSegments + 1) > 131072)
            throw new WormDriveDisplayUnavailableException("Display point resource bound exceeded; no geometry is truncated.");
        var inputFrame = getInputFrameMm();
        var n = c.PhysicalHelixAxis; var inputX = inputFrame.X; var inputY = n.Cross(inputX);
        var outputFrame = c.OutputShaft.Frame;
        // Stable schematic tooth labels use this physical axis, independent of the shaft coordinate sign.
        var wheelMaterialAxis = n.Cross(c.ContactSide); var wheelMaterialX = outputFrame.X;
        var wheelMaterialY = wheelMaterialAxis.Cross(wheelMaterialX);
        var rw = Number(geometry.WormPitchRadiusMm); var rg = Number(geometry.WheelPitchRadiusMm);
        var lead = Math.PI * Number(geometry.LeadPiCoefficientMm); Finite(lead);
        var tauG = Number(g.TraceSlope);
        if (!(rw > 0 && rg > 0 && lead > 0 && tauG > 0)) throw new WormDriveDisplayUnavailableException("Positive dimensions cannot be represented without display degeneracy.");
        var helices = new List<WormDriveDisplayPolyline>(); var traces = new List<WormDriveDisplayPolyline>();
        for (var start = 0; start < w.Starts; start++)
        {
            var points = new List<WormDriveDisplayPoint>();
            for (var i = 0; i <= helixSegments; i++)
            {
                work?.Step(); // Actual optional material sample, not another mechanical evaluation.
                var t = -1.0 + 2.0 * i / helixSegments;
                // Material rotates about its fixed center; axial position never depends on shaft angle.
                var angle = 2 * Math.PI * (t + (double)start / w.Starts) + wormPhase;
                points.Add(Point(geometry.InputPitchCenterMm, inputX, inputY, n, rw, angle, w.Handedness * lead * t));
            }
            CheckSegments(points);
            helices.Add(new(c.InputWormBodyId + "/start-" + start, points));
        }
        for (var tooth = 0; tooth < g.ToothCount; tooth++)
        {
            var points = new List<WormDriveDisplayPoint>();
            for (var i = 0; i <= wheelTraceSegments; i++)
            {
                work?.Step();
                var axial = rw * (-0.5 + (double)i / wheelTraceSegments);
                var traceAngle = g.Handedness * tauG * axial / rg; Finite(traceAngle);
                var angle = 2 * Math.PI * tooth / g.ToothCount + wheelPhase + traceAngle;
                points.Add(Point(geometry.OutputPitchCenterMm, wheelMaterialX, wheelMaterialY, wheelMaterialAxis, rg, angle, axial));
            }
            CheckSegments(points);
            traces.Add(new(c.OutputWheelBodyId + "/tooth-" + tooth, points));
        }
        work?.Step(); var wormMark = Point(geometry.InputPitchCenterMm, inputX, inputY, n, rw, wormPhase, 0);
        work?.Step(); var wheelMark = Point(geometry.OutputPitchCenterMm, wheelMaterialX, wheelMaterialY, wheelMaterialAxis, rg, wheelPhase, 0);
        return new(analysisId, geometry, rootInput, helices, traces, wormMark, wheelMark);
    }
    private static WormDriveDisplayPoint Point(ExactVector3 center, ExactVector3 x, ExactVector3 y, ExactVector3 axis,
        double radius, double angle, double station)
    {
        Finite(angle); Finite(station); var c = ApproximatePoint(center); var r = radius * Math.Cos(angle); var s = radius * Math.Sin(angle);
        return new(c.X + Number(x.X) * r + Number(y.X) * s + Number(axis.X) * station,
            c.Y + Number(x.Y) * r + Number(y.Y) * s + Number(axis.Y) * station,
            c.Z + Number(x.Z) * r + Number(y.Z) * s + Number(axis.Z) * station);
    }
    private static void CheckSegments(IReadOnlyList<WormDriveDisplayPoint> points)
    {
        for (var i = 1; i < points.Count; i++)
            if (points[i].X == points[i - 1].X && points[i].Y == points[i - 1].Y && points[i].Z == points[i - 1].Z)
                throw new WormDriveDisplayUnavailableException("Display precision merges distinct trace points; exact mechanics were not repaired.");
    }
    private static double Angle(Rational turns)
    {
        var reduced = new Rational(turns.Numerator % turns.Denominator, turns.Denominator);
        return Number(reduced) * (2 * Math.PI);
    }
    public static WormDriveDisplayPoint ApproximatePoint(ExactVector3 point) => new(Number(point.X), Number(point.Y), Number(point.Z));
    public static double ApproximateScalar(Rational value) => Number(value);
    internal static double Number(Rational value)
    {
        var result = (double)value.Numerator / (double)value.Denominator; Finite(result);
        if (!value.IsZero && result == 0) throw new WormDriveDisplayUnavailableException("An exact nonzero scalar underflows display precision.");
        return result;
    }
    internal static void Finite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1e12)
            throw new WormDriveDisplayUnavailableException("Display numeric resource bound exceeded; exact mechanics remain unchanged.");
    }
}
