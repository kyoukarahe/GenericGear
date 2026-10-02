using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class CarrierShaftState
{
    internal CarrierShaftState(string id, Rational world, Rational? relative) { ShaftId = id; WorldTurns = world; CarrierRelativeTurns = relative; }
    public string ShaftId { get; }
    public Rational WorldTurns { get; }
    public Rational? CarrierRelativeTurns { get; }
}
public sealed class CarrierEvaluation
{
    internal CarrierEvaluation(CarrierAnalysis analysis, Rational input, IEnumerable<CarrierShaftState> shafts, Rational carrierCommon, Rational planetCommon, Rational port)
    { DefinitionId = analysis.Request.DefinitionId; InputTurns = input; Shafts = shafts.ToList().AsReadOnly(); CarrierCommonTurns = carrierCommon; PlanetBodyCommonTurns = planetCommon; PortReadoutTurns = port; }
    public string DefinitionId { get; }
    public Rational InputTurns { get; }
    public ReadOnlyCollection<CarrierShaftState> Shafts { get; }
    public Rational CarrierCommonTurns { get; }
    public Rational PlanetBodyCommonTurns { get; }
    public Rational PortReadoutTurns { get; }
}
public sealed class CarrierDisplayPose
{
    internal CarrierDisplayPose(IEnumerable<KeyValuePair<string, ReadOnlyCollection<double>>> matrices, string? error)
    { Matrices = new ReadOnlyDictionary<string, ReadOnlyCollection<double>>(matrices.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)); UnavailableReason = error; }
    public IReadOnlyDictionary<string, ReadOnlyCollection<double>> Matrices { get; }
    public string? UnavailableReason { get; }
    public bool IsAvailable => UnavailableReason is null;
}

public static class CarrierEvaluator
{
    public static CarrierEvaluation Evaluate(CarrierAnalysis analysis, ExactQuantity input)
    {
        if (analysis is null || !analysis.IsValid) throw new ArgumentException("Admitted carrier analysis required.");
        if (input.Kind != QuantityKind.AngularPosition) throw new ArgumentException("Carrier input requires unwrapped turns.");
        MechanicalAuthoringProfile.Number(input.Value);
        Rational Eval(ExactAffineRelation law) { var v = law.Evaluate(input.Value); CarrierProfile.Derived(v); return v; }
        return new(analysis, input.Value, analysis.Shafts.Select(s => new CarrierShaftState(s.ShaftId, Eval(s.World), s.CarrierRelative.HasValue ? Eval(s.CarrierRelative.Value) : null)),
            Eval(analysis.CarrierCommon!.Value), Eval(analysis.PlanetCommon!.Value), Eval(analysis.PortReadout!.Value));
    }

    /// <summary>Optional display only; exact evaluation is separate and never reduced to binary64.</summary>
    public static CarrierDisplayPose Display(CarrierAnalysis analysis, ExactQuantity input)
    {
        _ = Evaluate(analysis, input);
        return Project(analysis.PoseNodes, input.Value);
    }

    internal static CarrierDisplayPose Project(IEnumerable<CarrierPoseNode> nodes, Rational input)
    {
        var matrices = new Dictionary<string, ReadOnlyCollection<double>>(StringComparer.Ordinal);
        try
        {
            foreach (var node in nodes)
            {
                var f = node.Frame; var turn = node.Rotation.Evaluate(input);
                var wrapped = new Rational((turn.Numerator % turn.Denominator + turn.Denominator) % turn.Denominator, turn.Denominator);
                var angle = Number(wrapped) * 2 * Math.PI; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
                double[] V(ExactVector3 v) => new[] { Number(v.X), Number(v.Y), Number(v.Z) };
                var x = V(f.X); var y = V(f.Y); var z = V(f.Z); var o = V(f.Origin);
                var m = new[] { x[0]*cos+y[0]*sin, x[1]*cos+y[1]*sin, x[2]*cos+y[2]*sin, 0,
                    y[0]*cos-x[0]*sin, y[1]*cos-x[1]*sin, y[2]*cos-x[2]*sin, 0,
                    z[0],z[1],z[2],0,o[0],o[1],o[2],1 };
                if (node.ParentId is not null)
                {
                    var p = matrices[node.ParentId]; var product = new double[16];
                    for (int col = 0; col < 4; col++) for (int row = 0; row < 4; row++) for (int k = 0; k < 4; k++) product[col*4+row] += p[k*4+row]*m[col*4+k];
                    m = product;
                }
                if (m.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e8)) throw new ArgumentException("Display coordinate bound exceeded.");
                matrices.Add(node.Id, Array.AsReadOnly(m));
            }
            return new(matrices, null);
        }
        catch (ArgumentException e) { return new(Array.Empty<KeyValuePair<string, ReadOnlyCollection<double>>>(), e.Message); }
    }
    private static double Number(Rational r)
    {
        var n = (double)r.Numerator; var d = (double)r.Denominator; var v = n / d;
        if (!double.IsFinite(n) || !double.IsFinite(d) || !double.IsFinite(v) || Math.Abs(v) > 1e8 || v == 0 && r != 0)
            throw new ArgumentException("Exact motion exists; display conversion is unavailable.");
        return v;
    }
}
