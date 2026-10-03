using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>A residual-checked numerical source, never an inferred solution error bound.</summary>
public sealed class MotionLatent
{
    internal MotionLatent(string id, double estimate) { Id = id; Estimate = estimate; }
    public string Id { get; }
    public double Estimate { get; }
}

/// <summary>Exact affine transport of named numerical sources. Shared aliases retain correlation.</summary>
public sealed class ConnectedMotionValue
{
    private ConnectedMotionValue(Rational constant, IEnumerable<(MotionLatent Source, Rational Coefficient)> terms)
    {
        CarrierProfile.Derived(constant); Constant = constant;
        var input = terms.ToArray(); var grouped = new List<(MotionLatent Source, Rational Coefficient)>();
        foreach (var group in input.GroupBy(t => t.Source.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var source = group.First().Source;
            if (group.Any(t => t.Source.Estimate != source.Estimate)) throw new ArgumentException("Conflicting latent identity.");
            var coefficient = group.Aggregate(Rational.Zero, (v, t) => v + t.Coefficient); CarrierProfile.Derived(coefficient);
            if (!coefficient.IsZero) grouped.Add((source, coefficient));
        }
        if (grouped.Count > 64) throw new ArgumentException("Motion provenance resource limit.");
        Terms = grouped.AsReadOnly();
    }
    public Rational Constant { get; }
    public ReadOnlyCollection<(MotionLatent Source, Rational Coefficient)> Terms { get; }
    public bool IsExact => Terms.Count == 0;
    public string Kind => IsExact ? "ExactRational" : "NumericResidualOnly";
    public string Unit => "turn";
    public Rational? Exact => IsExact ? Constant : null;
    public double? SolutionErrorBoundTurns => null;
    public double Estimate => Number(Constant) + Terms.Sum(t => Number(t.Coefficient) * t.Source.Estimate);
    public static ConnectedMotionValue FromExact(Rational value) => new(value, Array.Empty<(MotionLatent, Rational)>());
    internal static ConnectedMotionValue FromResidual(string identity, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Non-finite numerical motion.");
        return new(0, new[] { (new MotionLatent(identity, value), Rational.One) });
    }
    public ConnectedMotionValue Scale(Rational coefficient, Rational offset) => new(Constant * coefficient + offset, Terms.Select(t => (t.Source, t.Coefficient * coefficient)));
    public ConnectedMotionValue Plus(ConnectedMotionValue other) => new(Constant + other.Constant, Terms.Concat(other.Terms));
    public ConnectedMotionValue Minus(ConnectedMotionValue other) => Plus(other.Scale(-1, 0));
    internal static ConnectedMotionValue Apply(DifferentialLaw law, IReadOnlyDictionary<string, ConnectedMotionValue> vector)
    {
        if (!law.Coefficients.Keys.SequenceEqual(vector.Keys.OrderBy(x => x, StringComparer.Ordinal))) throw new ArgumentException("Complete same-snapshot vector required.");
        var value = FromExact(law.Offset); foreach (var p in law.Coefficients) value = value.Plus(vector[p.Key].Scale(p.Value, 0)); return value;
    }
    // Semantic state identity excludes platform-dependent residual-only estimates; payload bytes have a separate digest.
    internal string Key => Pack(F(Constant), Pack(Terms.Select(t => Pack(t.Source.Id, F(t.Coefficient))).ToArray()));
    internal static string Numeric(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    internal static double Number(Rational r)
    {
        var v = (double)r.Numerator / (double)r.Denominator;
        if (!double.IsFinite(v) || v == 0 && !r.IsZero) throw new ArgumentException("PrecisionLimit"); return v;
    }
    internal double DisplayTurns()
    {
        // Preserve huge exact accumulation; only this display projection reduces the exact constant.
        var v = Number(new Rational(Constant.Numerator % Constant.Denominator, Constant.Denominator));
        foreach (var t in Terms) v += Number(t.Coefficient) * t.Source.Estimate;
        if (!double.IsFinite(v) || Math.Abs(v) > 1e8) throw new ArgumentException("DisplayUnavailable"); return v % 1;
    }
}
