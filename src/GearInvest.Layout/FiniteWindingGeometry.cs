using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Layout;

/// <summary>Binary64 authored pin-seat geometry, in millimetres. Not exact CAD geometry.</summary>
public readonly struct WindingPoint
{
    public WindingPoint(double x, double y) { X = x; Y = y; }
    public double X { get; }
    public double Y { get; }
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(WindingPoint b) => X * b.X + Y * b.Y;
    public double Cross(WindingPoint b) => X * b.Y - Y * b.X;
    public WindingPoint Rotate(double turns)
    { var a = turns * 2 * Math.PI; var c = Math.Cos(a); var s = Math.Sin(a); return new(c * X - s * Y, s * X + c * Y); }
    public static WindingPoint operator +(WindingPoint a, WindingPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static WindingPoint operator -(WindingPoint a, WindingPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static WindingPoint operator *(WindingPoint a, double b) => new(a.X * b, a.Y * b);
}

/// <summary>Explicit finite, planar, equal-chord pin seats and a straight free-span boundary.</summary>
public sealed class FiniteWindingGeometry
{
    public const string Profile = "finite-planar-chord-station-winding-v1";
    public const string Policy = "binary64-residual-upper-support-v1";
    public const double LengthToleranceMm = 1e-9;
    public const double PoseTieToleranceMm = 1e-7;
    public const double GuardSeparationTurns = 1e-10;
    public FiniteWindingGeometry(WindingPoint driverCenter, WindingPoint outputCenter, double planeZMm,
        IEnumerable<WindingPoint> driverSeats, IEnumerable<WindingPoint> outputSeats, int linkCount, double pitchMm,
        int maxDriverContact, int maxOutputContact, double maxBendDegrees,
        double driverMinimumTurns, double driverMaximumTurns, double outputMinimumTurns, double outputMaximumTurns)
    {
        DriverCenter = driverCenter; OutputCenter = outputCenter; PlaneZMm = planeZMm;
        DriverSeats = Bounded(driverSeats, 12); OutputSeats = Bounded(outputSeats, 12);
        LinkCount = linkCount; PitchMm = pitchMm; MaxDriverContact = maxDriverContact; MaxOutputContact = maxOutputContact;
        MaxBendDegrees = maxBendDegrees; DriverMinimumTurns = driverMinimumTurns; DriverMaximumTurns = driverMaximumTurns;
        OutputMinimumTurns = outputMinimumTurns; OutputMaximumTurns = outputMaximumTurns;
    }
    private static ReadOnlyCollection<WindingPoint> Bounded(IEnumerable<WindingPoint> points, int max)
    { var a = points.Take(max + 1).ToArray(); if (a.Length > max) throw new ArgumentException("Winding guide resource limit."); return Array.AsReadOnly(a); }
    public WindingPoint DriverCenter { get; }
    public WindingPoint OutputCenter { get; }
    public double PlaneZMm { get; }
    public ReadOnlyCollection<WindingPoint> DriverSeats { get; }
    public ReadOnlyCollection<WindingPoint> OutputSeats { get; }
    public int LinkCount { get; }
    public double PitchMm { get; }
    public int MaxDriverContact { get; }
    public int MaxOutputContact { get; }
    public double MaxBendDegrees { get; }
    public double DriverMinimumTurns { get; }
    public double DriverMaximumTurns { get; }
    public double OutputMinimumTurns { get; }
    public double OutputMaximumTurns { get; }
    public string? Validate()
    {
        var values = new[] { DriverCenter.X, DriverCenter.Y, OutputCenter.X, OutputCenter.Y, PlaneZMm,
            PitchMm, MaxBendDegrees, DriverMinimumTurns, DriverMaximumTurns, OutputMinimumTurns, OutputMaximumTurns }
            .Concat(DriverSeats.Concat(OutputSeats).SelectMany(p => new[] { p.X, p.Y }));
        if (values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e6)) return "InvalidInput";
        if (LinkCount < 4 || LinkCount > 21 || MaxDriverContact < 1 || MaxDriverContact > 5 || MaxOutputContact < 1 || MaxOutputContact > 5) return "ResourceLimit";
        if (PitchMm < 0.001 || PitchMm > 1000 || MaxBendDegrees <= 0 || MaxBendDegrees > 45) return "UnsupportedProfile";
        if (!(DriverMinimumTurns < DriverMaximumTurns) || DriverMaximumTurns - DriverMinimumTurns >= 1 ||
            !(OutputMinimumTurns < OutputMaximumTurns) || OutputMaximumTurns - OutputMinimumTurns >= 1 ||
            Math.Max(Math.Abs(DriverMinimumTurns), Math.Abs(DriverMaximumTurns)) > 1000 ||
            Math.Max(Math.Abs(OutputMinimumTurns), Math.Abs(OutputMaximumTurns)) > 1000) return "UnsupportedFiniteBranch";
        foreach (var item in new[] { (DriverSeats, MaxDriverContact, -1), (OutputSeats, MaxOutputContact, 1) })
        {
            var (poly, max, sign) = item;
            if (poly.Count < 4 || max + 1 >= poly.Count) return "MissingBoundary";
            for (var i = 0; i < poly.Count; i++)
            {
                var edge = poly[(i + 1) % poly.Count] - poly[i]; var next = poly[(i + 2) % poly.Count] - poly[(i + 1) % poly.Count];
                if (Math.Abs(edge.Length - PitchMm) > LengthToleranceMm) return "InconsistentPitch";
                if (sign * edge.Cross(poly[i] * -1) <= LengthToleranceMm * PitchMm) return "UnsupportedGuideAxisOutsideInterior";
                // Every seat must be strictly inside every other edge half-plane: rejects star polygons too.
                for (var j = 0; j < poly.Count; j++)
                    if (j != i && j != (i + 1) % poly.Count && sign * edge.Cross(poly[j] - poly[i]) <= LengthToleranceMm * PitchMm)
                        return "UnsupportedGuideOrientationOrConvexity";
                if (Math.Abs(Math.Atan2(edge.Cross(next), edge.Dot(next))) * 180 / Math.PI > MaxBendDegrees) return "JointLimit";
            }
        }
        if ((OutputCenter - DriverCenter).Length <= DriverSeats.Max(p => p.Length) + OutputSeats.Max(p => p.Length)) return "UnsupportedOverlappingGuideEnvelopes";
        if (LinkCount * PitchMm < (OutputCenter - DriverCenter).Length - DriverSeats.Max(p => p.Length) - OutputSeats.Max(p => p.Length)) return "InfeasibleProvedByEndpointDistanceBound";
        return null;
    }
}

public sealed class WindingPose
{
    internal WindingPose(double q, double w, int a, int b, IEnumerable<WindingPoint> pins, IEnumerable<double> angles,
        double pitch, double total, double attachment, double direction, double guide, double support, double bend)
    { DriverTurns = q; OutputTurns = w; DriverContact = a; OutputContact = b; Pins = pins.ToList().AsReadOnly(); LinkAnglesTurns = angles.ToList().AsReadOnly();
        PitchResidualMm = pitch; TotalLengthResidualMm = total; AttachmentResidualMm = attachment; AttachmentDirectionResidualMm = direction; GuidePinResidualMm = guide; SupportResidualMm = support; MaximumBendDegrees = bend; }
    public double DriverTurns { get; }
    public double OutputTurns { get; }
    public int DriverContact { get; }
    public int OutputContact { get; }
    public ReadOnlyCollection<WindingPoint> Pins { get; }
    public ReadOnlyCollection<double> LinkAnglesTurns { get; }
    public double PitchResidualMm { get; }
    public double TotalLengthResidualMm { get; }
    public double AttachmentResidualMm { get; }
    public double AttachmentDirectionResidualMm { get; }
    public double GuidePinResidualMm { get; }
    public double PlanarityResidualMm => 0;
    public double SupportResidualMm { get; }
    public double MaximumBendDegrees { get; }
    public string Quality => "NumericResidualOnly";
    public double? SolutionErrorBoundTurns => null;
}

public sealed class WindingQuery
{
    internal WindingQuery(string status, WindingPose? pose = null) { Status = status; Pose = pose; }
    public string Status { get; }
    public WindingPose? Pose { get; }
    public bool IsAccepted => Pose is not null;
}

/// <summary>Finite analytic candidates; ordinary binary64 roots/residuals, NOT certified enclosures.</summary>
public static class FiniteWindingSolver
{
    public static WindingQuery Evaluate(FiniteWindingGeometry g, double q)
    {
        var error = g.Validate(); if (error is not null) return new(error);
        if (!double.IsFinite(q)) return new("InvalidInput");
        if (q < g.DriverMinimumTurns || q > g.DriverMaximumTurns) return new("WindingBoundary");
        var at = g.DriverSeats.Select(p => g.DriverCenter + p.Rotate(q)).ToArray(); var candidates = new List<WindingPose>(); var nearSingularity = false;
        for (var ma = 1; ma <= g.MaxDriverContact; ma++) for (var mb = 1; mb <= g.MaxOutputContact; mb++)
        {
            var free = g.LinkCount - ma - mb; if (free < 2) continue;
            var a = at[ma]; var vec = a - g.OutputCenter; var d = vec.Length; var r = g.OutputSeats[mb].Length; var length = free * g.PitchMm;
            if (d <= 1e-12 || r <= 1e-12) continue;
            var c = (d * d + r * r - length * length) / (2 * d * r);
            if (Math.Abs(Math.Abs(c) - 1) < 1e-12) nearSingularity = true;
            if (Math.Abs(c) >= 1) continue; // Singular tangency is not repaired by clamping acos.
            var phi = Math.Atan2(vec.Y, vec.X); var offset = Math.Acos(c);
            foreach (var angle in new[] { phi - offset, phi + offset })
            {
                var raw = (angle - Math.Atan2(g.OutputSeats[mb].Y, g.OutputSeats[mb].X)) / (2 * Math.PI);
                var w = raw + Math.Ceiling(g.OutputMinimumTurns - raw);
                if (w > g.OutputMaximumTurns) continue;
                var bt = g.OutputSeats.Select(p => g.OutputCenter + p.Rotate(w)).ToArray(); var b = bt[mb]; var dir = (b - a) * (1 / length);
                var support = at.Concat(bt).Max(p => dir.Cross(p - a));
                if (support > FiniteWindingGeometry.LengthToleranceMm) continue;
                var pins = at.Take(ma + 1).Concat(Enumerable.Range(1, free - 1).Select(i => a + (b - a) * ((double)i / free)))
                    .Concat(bt.Take(mb + 1).Reverse()).ToArray();
                var edges = Enumerable.Range(0, g.LinkCount).Select(i => pins[i + 1] - pins[i]).ToArray();
                var bend = Enumerable.Range(0, edges.Length - 1).Max(i => Math.Abs(Math.Atan2(edges[i].Cross(edges[i + 1]), edges[i].Dot(edges[i + 1]))) * 180 / Math.PI);
                if (bend > g.MaxBendDegrees + 1e-7) continue;
                var pitch = edges.Max(p => Math.Abs(p.Length - g.PitchMm)); var total = Math.Abs(edges.Sum(p => p.Length) - g.LinkCount * g.PitchMm);
                var attachment = Math.Max((pins[0] - at[0]).Length, (pins[pins.Length - 1] - bt[0]).Length);
                var direction = Math.Max((edges[0] - (at[1] - at[0])).Length, (edges[edges.Length - 1] - (bt[0] - bt[1])).Length);
                var guide = Math.Max(Enumerable.Range(0,ma+1).Max(i=>(pins[i]-g.DriverCenter-g.DriverSeats[i].Rotate(q)).Length),Enumerable.Range(0,mb+1).Max(i=>(pins[g.LinkCount-i]-g.OutputCenter-g.OutputSeats[i].Rotate(w)).Length));
                if (pitch > FiniteWindingGeometry.LengthToleranceMm || total > g.LinkCount * FiniteWindingGeometry.LengthToleranceMm || Math.Max(Math.Max(attachment,direction),guide) > FiniteWindingGeometry.LengthToleranceMm) continue;
                candidates.Add(new(q, w, ma, mb, pins, edges.Select(p => Math.Atan2(p.Y, p.X) / (2 * Math.PI)), pitch, total, attachment, direction, guide, Math.Max(0, support), bend));
            }
        }
        if (candidates.Count == 0) return new(nearSingularity ? "NumericalUnresolved" : "NoAdmissibleBranch");
        var first = candidates[0];
        if (candidates.Skip(1).Any(p => p.Pins.Zip(first.Pins, (a, b) => (a - b).Length).Max() > FiniteWindingGeometry.PoseTieToleranceMm || Math.Abs(p.OutputTurns - first.OutputTurns) > 1e-8)) return new("AmbiguousBranch");
        // Only numerically coincident material poses share labels at an adjacent handoff.
        return new("ResidualChecked", first);
    }

    public static ReadOnlyCollection<double> GuardCandidates(FiniteWindingGeometry g)
    {
        var error = g.Validate(); if (error is not null) throw new ArgumentException(error);
        var roots = new List<double>(); var equations = 0;
        void Equation(WindingPoint delta, WindingPoint h, double r2)
        {
            if (++equations > 200) throw new ArgumentException("ResourceLimit");
            var aa = 2 * delta.Dot(h); var bb = 2 * (-delta.X * h.Y + delta.Y * h.X);
            var cc = delta.Dot(delta) + h.Dot(h) - r2; var amplitude = Math.Sqrt(aa * aa + bb * bb);
            if (amplitude < 1e-15) { if (Math.Abs(cc) < 1e-12) throw new ArgumentException("GuardIndeterminate"); return; }
            var ratio = -cc / amplitude; if (Math.Abs(ratio) > 1) return;
            var phi = Math.Atan2(bb, aa); var half = Math.Acos(ratio);
            foreach (var angle in new[] { phi - half, phi + half })
            {
                var raw = angle / (2 * Math.PI); var root = raw + Math.Ceiling(g.DriverMinimumTurns - raw);
                if (root > g.DriverMinimumTurns && root < g.DriverMaximumTurns) roots.Add(root);
            }
        }
        var delta = g.DriverCenter - g.OutputCenter;
        for (var ma = 1; ma <= g.MaxDriverContact; ma++) for (var mb = 1; mb <= g.MaxOutputContact; mb++)
        {
            var free = g.LinkCount - ma - mb; if (free < 2) continue;
            var length = free * g.PitchMm; var a = g.DriverSeats[ma]; var b = g.OutputSeats[mb];
            foreach (var neighbor in new[] { -1, 1 })
            {
                // CW A and CCW B with the upper supporting span fix these signs.
                var ea = (g.DriverSeats[ma + neighbor] - a) * (neighbor / g.PitchMm);
                var eb = (g.OutputSeats[mb + neighbor] - b) * (-neighbor / g.PitchMm);
                Equation(delta, a + ea * length, b.Dot(b));
                var h = b - eb * length; Equation(delta, a, h.Dot(h));
            }
            // Root existence/tangency and finite output-lift boundaries cannot be skipped either.
            Equation(delta, a, Math.Pow(b.Length + length, 2)); Equation(delta, a, Math.Pow(b.Length - length, 2));
            Equation(delta - b.Rotate(g.OutputMinimumTurns), a, length * length);
            Equation(delta - b.Rotate(g.OutputMaximumTurns), a, length * length);
        }
        if (roots.Count > 400) throw new ArgumentException("ResourceLimit");
        roots.Sort(); return roots.AsReadOnly();
    }

    /// <summary>Validates the complete monotone segment, including all analytic candidate partitions.</summary>
    public static string? ValidateSegment(FiniteWindingGeometry g, double start, double end)
    {
        var a = Evaluate(g, start); if (!a.IsAccepted) return a.Status;
        var b = Evaluate(g, end); if (!b.IsAccepted) return b.Status;
        ReadOnlyCollection<double> roots;
        try { roots = GuardCandidates(g); } catch (ArgumentException ex) { return ex.Message; }
        var points = new List<double> { Math.Min(start, end) };
        foreach (var root in roots.Where(x => x > Math.Min(start, end) && x < Math.Max(start, end)))
        {
            var last = points[points.Count - 1];
            if (root - last < 1e-13) continue; // Same numerical equation root, not a distinct physical event.
            if (root - last < FiniteWindingGeometry.GuardSeparationTurns) return "GuardIndeterminate";
            points.Add(root);
        }
        points.Add(Math.Max(start, end));
        for (var i = 1; i < points.Count; i++)
        {
            foreach (var q in new[] { (points[i - 1] + points[i]) / 2, points[i] })
            { var query = Evaluate(g, q); if (!query.IsAccepted) return query.Status; }
        }
        return null;
    }

    public static bool HasUnresolvedEventOrder(FiniteWindingGeometry g,double q)
    {
        foreach(var root in GuardCandidates(g).Where(r=>Math.Abs(r-q)<FiniteWindingGeometry.GuardSeparationTurns))
        {
            var left=Evaluate(g,Math.Max(g.DriverMinimumTurns,root-1e-8));var right=Evaluate(g,Math.Min(g.DriverMaximumTurns,root+1e-8));
            if(!left.IsAccepted||!right.IsAccepted||left.Pose!.DriverContact!=right.Pose!.DriverContact||left.Pose.OutputContact!=right.Pose.OutputContact)return true;
        }
        return false;
    }
}
