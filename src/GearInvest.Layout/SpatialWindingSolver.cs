using System;
using System.Collections.Generic;
using System.Linq;

namespace GearInvest.Layout;

/// <summary>Ordered pin-guide sphere intersections and passive endpoint closure, not arc-length discretisation.</summary>
public static class SpatialWindingSolver
{
    public const long DefaultWorkLimit = 8000000;
    public const string RefinedPolicy = "binary64-chord-bracket-refined-v1";
    private sealed class Budget
    {
        public Budget(long limit) { Limit = limit; }
        public long Used;
        public readonly long Limit;
        public void Step() { if (++Used > Limit) throw new ArgumentException("ResourceLimit"); }
    }
    private sealed class Piece
    {
        public Piece(WindingPoint3 a, WindingPoint3 b) { A = a; B = b; End = (b - a).Length; }
        public Piece(HelicalPinGuide helix, double rotation, double start, double end)
        { Helix = helix; Rotation = rotation; Start = start; Sign = end >= start ? 1 : -1; End = Math.Abs(end - start); }
        public readonly HelicalPinGuide? Helix;
        public readonly WindingPoint3 A, B;
        public readonly double Rotation, Start, End, Sign;
        public WindingPoint3 Point(double t) => Helix is null ? A + (B - A) * (t / End) : Helix.Point(Start + Sign * t, Rotation);
        public WindingPoint3 Derivative(double t) => Helix is null ? (B - A) / End : Helix.Derivative(Start + Sign * t, Rotation) * Sign;
        public double Speed => Helix?.SpeedBound ?? 1;
        public double Acceleration => Helix?.AccelerationBound ?? 0;
    }
    private sealed class Walk
    {
        public Walk(double residual, List<SpatialWindingPin> pins, Piece[] pieces) { Residual = residual; Pins = pins; Pieces = pieces; }
        public readonly double Residual;
        public readonly List<SpatialWindingPin> Pins;
        public readonly Piece[] Pieces;
    }

    public static SpatialWindingQuery Evaluate(SpatialWindingGeometry g, double q, long workLimit = DefaultWorkLimit)
        => EvaluateCore(g, q, workLimit, false);
    /// <summary>The same bounded algorithm with tighter passive-endpoint stopping criteria.
    /// Used by the additive selected-drive profile; legacy numerical output is unchanged.
    /// This is not a solution-error bound or continuous-path certificate.</summary>
    public static SpatialWindingQuery EvaluateRefined(SpatialWindingGeometry g, double q, long workLimit = DefaultWorkLimit)
        => EvaluateCore(g, q, workLimit, true);
    private static SpatialWindingQuery EvaluateCore(SpatialWindingGeometry g, double q, long workLimit, bool refined)
    {
        var budget = new Budget(Math.Min(DefaultWorkLimit, workLimit));
        var error = g.Validate(); if (error is not null) return new(error, 0);
        if (!double.IsFinite(q) || q < g.DriverMinimumTurns || q > g.DriverMaximumTurns) return new("WindingBoundary", 0);
        if (workLimit < 1) return new("ResourceLimit", 0);
        try
        {
            // Scan the declared finite branch, not modulo-one angles or a baked pose table.
            // A found numerical bracket is not a proof that a general curve has a unique root.
            const int scans = 32;
            var brackets = new List<(double Lo, double Hi)>();
            var lo = g.OutputMinimumTurns; var previous = Trace(g, q, lo, budget).Residual;
            var trend = 0;
            for (var i = 1; i <= scans; i++)
            {
                var hi = g.OutputMinimumTurns + (g.OutputMaximumTurns - g.OutputMinimumTurns) * i / scans;
                var residual = Trace(g, q, hi, budget).Residual;
                var difference = residual - previous;
                if (Math.Abs(difference) > 1e-7 && Math.Abs(previous) < 1 && Math.Abs(residual) < 1)
                {
                    var direction = Math.Sign(difference);
                    if (trend != 0 && trend != direction) return new("NumericalUnresolved", budget.Used);
                    trend = direction;
                }
                if (previous == 0) brackets.Add((lo, lo));
                else if (residual == 0 || previous * residual < 0) brackets.Add((lo, hi));
                previous = residual; lo = hi;
            }
            if (brackets.Count == 0) return new("NoBracketInDeclaredBranch", budget.Used);
            // Shared scan endpoints represent one root, not distinct branches.
            for (var i = brackets.Count - 1; i > 0; i--)
                if (brackets[i].Lo == brackets[i].Hi && brackets[i - 1].Hi == brackets[i].Lo) brackets.RemoveAt(i);
            if (brackets.Count != 1) return new("AmbiguousBranch", budget.Used);
            var left = brackets[0].Lo; var right = brackets[0].Hi;
            var fl = Trace(g, q, left, budget).Residual;
            for (var i = 0; i < (refined ? 52 : 48) && right - left > (refined ? 2e-15 : 2e-13); i++)
            {
                var mid = (left + right) * .5; var fm = Trace(g, q, mid, budget).Residual;
                if (Math.Abs(fm) < (refined ? 1e-13 : 2e-11)) { left = right = mid; break; }
                if (fm * fl > 0) { left = mid; fl = fm; } else right = mid;
            }
            var w = (left + right) * .5; var walk = Trace(g, q, w, budget); var pins = walk.Pins;
            var last = walk.Pieces[walk.Pieces.Length - 1]; var end = last.Point(last.End);
            if (pins.Count == g.LinkCount) pins.Add(new(end, walk.Pieces.Length - 1, last.End));
            else if (pins.Count == g.LinkCount + 1 && (pins[pins.Count - 1].PositionMm - end).Length < SpatialWindingGeometry.LengthToleranceMm)
                pins[pins.Count - 1] = new(end, walk.Pieces.Length - 1, last.End);
            else return new("NumericalUnresolved", budget.Used);
            var pitch = 0.0; var bend = 0.0;
            for (var i = 1; i < pins.Count; i++)
            {
                var d = pins[i].PositionMm - pins[i - 1].PositionMm; pitch = Math.Max(pitch, Math.Abs(d.Length - g.PitchMm));
                if (i > 1) bend = Math.Max(bend, Angle(pins[i - 1].PositionMm - pins[i - 2].PositionMm, d));
            }
            var attachment = Math.Max((pins[0].PositionMm - g.Driver.Point(0, q)).Length, (pins[pins.Count - 1].PositionMm - g.Output.Point(0, w)).Length);
            var attachmentBend = Math.Max(Angle(pins[1].PositionMm - pins[0].PositionMm, g.Driver.Derivative(0, q)),
                Angle(pins[pins.Count - 2].PositionMm - pins[pins.Count - 1].PositionMm, g.Output.Derivative(0, w)));
            if (pitch > SpatialWindingGeometry.LengthToleranceMm || attachment > SpatialWindingGeometry.LengthToleranceMm) return new("NumericalUnresolved", budget.Used);
            if (bend > g.MaxBendDegrees || attachmentBend > g.MaxAttachmentBendDegrees) return new("JointLimit", budget.Used);
            return new("Accepted", budget.Used, new(q, w, pins, walk.Pieces.Length - 1, pitch, attachment, bend, attachmentBend, budget.Used, brackets.Count));
        }
        catch (ArgumentException e) { return new(e.Message, budget.Used); }
    }

    private static Walk Trace(SpatialWindingGeometry g, double q, double w, Budget budget)
    {
        var ae = g.Driver.ExitParameter(q); var be = g.Output.ExitParameter(w);
        var list = new List<Piece> { new(g.Driver, q, 0, ae), new(g.Driver.Meridian(ae), g.Bridge[0]) };
        for (var i = 1; i < g.Bridge.Count; i++) list.Add(new(g.Bridge[i - 1], g.Bridge[i]));
        list.Add(new(g.Bridge[g.Bridge.Count - 1], g.Output.Meridian(be))); list.Add(new(g.Output, w, be, 0));
        var pieces = list.ToArray(); var pieceIndex = 0; var parameter = 0.0; var current = pieces[0].Point(0);
        var pins = new List<SpatialWindingPin>(g.LinkCount + 2) { new(current, 0, 0) };
        while (pins.Count <= g.LinkCount + 1)
        {
            var found = false;
            while (pieceIndex < pieces.Length)
            {
                var p = pieces[pieceIndex]; var next = Next(p, parameter, current, g.PitchMm, budget);
                if (next.HasValue)
                {
                    parameter = next.Value; current = p.Point(parameter); pins.Add(new(current, pieceIndex, parameter)); found = true; break;
                }
                pieceIndex++; parameter = 0;
            }
            if (!found)
            {
                var final = pieces[pieces.Length - 1]; var remaining = (final.Point(final.End) - current).Length / g.PitchMm;
                return new(pins.Count - 1 + remaining - g.LinkCount, pins, pieces);
            }
        }
        return new(1, pins, pieces);
    }

    private static double? Next(Piece piece, double start, WindingPoint3 center, double pitch, Budget budget)
    {
        if (piece.End - start < 1e-14) return null;
        if (piece.Helix is null)
        {
            budget.Step(); var direction = piece.Derivative(0); var offset = piece.Point(start) - center;
            var b = offset.Dot(direction); var discriminant = b * b + pitch * pitch - offset.Dot(offset);
            if (discriminant < 0) return null;
            var distance = -b + Math.Sqrt(discriminant);
            if (distance <= 1e-13) throw new ArgumentException("NumericalUnresolved");
            return start + distance <= piece.End ? start + distance : (double?)null;
        }
        var delta = Math.Min(.0625, pitch / piece.Speed * .75); var a = start;
        while (a < piece.End)
        {
            var b = Math.Min(piece.End, a + delta); var found = Crossing(piece, a, b, center, pitch, budget, 0);
            if (found.HasValue) return found; a = b;
        }
        return null;
    }

    // Bounds use analytic speed/acceleration with a conservative floating margin. They are
    // numerical guards, not directed-rounding interval certificates or a formal uniqueness proof.
    private static double? Crossing(Piece p, double a, double b, WindingPoint3 center, double pitch, Budget budget, int depth)
    {
        budget.Step(); var pa = p.Point(a) - center; var pb = p.Point(b) - center; var fa = pa.Dot(pa) - pitch * pitch; var fb = pb.Dot(pb) - pitch * pitch;
        var width = b - a; var distanceBound = Math.Max(pa.Length, pb.Length) + p.Speed * width;
        if (Math.Max(pa.Length, pb.Length) + p.Speed * width * .5 < pitch - 1e-12) return null;
        var mid = (a + b) * .5; var pm = p.Point(mid) - center;
        var derivative = 2 * pm.Dot(p.Derivative(mid));
        var variation = (p.Speed * p.Speed + distanceBound * p.Acceleration) * width * (1 + 1e-12) + 1e-12;
        if (derivative - variation > 0)
        {
            if (fb < 0) return null;
            if (fa > 1e-10) throw new ArgumentException("AmbiguousBranch");
            var left = a; var right = b; var x = mid;
            for (var i = 0; i < 40; i++)
            {
                budget.Step(); var difference = p.Point(x) - center; var f = difference.Dot(difference) - pitch * pitch;
                if (Math.Abs(f) < 1e-12 * pitch * pitch) return x;
                if (f > 0) right = x; else left = x;
                var slope = 2 * difference.Dot(p.Derivative(x)); var trial = x - f / slope;
                x = trial > left && trial < right ? trial : (left + right) * .5;
            }
            throw new ArgumentException("NumericalUnresolved");
        }
        if (derivative + variation < 0 && fa < 0 && fb < 0) return null;
        if (depth >= 24 || width < 1e-13) throw new ArgumentException("NumericalUnresolved");
        return Crossing(p, a, mid, center, pitch, budget, depth + 1) ?? Crossing(p, mid, b, center, pitch, budget, depth + 1);
    }
    private static double Angle(WindingPoint3 a, WindingPoint3 b) => Math.Atan2(a.Cross(b).Length, a.Dot(b)) * 180 / Math.PI;
}
