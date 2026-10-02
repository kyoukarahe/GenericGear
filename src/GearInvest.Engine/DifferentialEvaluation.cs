using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class DifferentialEvaluation
{
    internal DifferentialEvaluation(DifferentialAnalysis analysis, DifferentialInputSnapshot input)
    {
        RequestId = analysis.Request.RequestId; Input = input;
        Coordinates = new ReadOnlyDictionary<string, Rational>(analysis.Coordinates.Where(c => c.IsKnown).ToDictionary(c => c.ShaftId, c => c.Law!.Evaluate(input), StringComparer.Ordinal));
        var d = analysis.Request.Definition;
        Ports = new ReadOnlyDictionary<string, Rational>(d.Ports.Where(p => Coordinates.ContainsKey(p.ShaftId)).ToDictionary(p => p.Id, p => p.FrameInShaft.Z.Z * Coordinates[p.ShaftId] + p.ReadoutOffset.Value, StringComparer.Ordinal));
        CarrierCommonTurns = analysis.CarrierCommon?.Evaluate(input); PlanetCommonTurns = analysis.PlanetCommon?.Evaluate(input); PlanetRelativeTurns = analysis.PlanetRelative?.Evaluate(input);
    }
    public string RequestId { get; }
    public DifferentialInputSnapshot Input { get; }
    public IReadOnlyDictionary<string, Rational> Coordinates { get; }
    public IReadOnlyDictionary<string, Rational> Ports { get; }
    public Rational? CarrierCommonTurns { get; }
    public Rational? PlanetCommonTurns { get; }
    public Rational? PlanetRelativeTurns { get; }
}

public static class DifferentialEvaluator
{
    public static DifferentialEvaluation Evaluate(DifferentialAnalysis analysis, DifferentialInputSnapshot input)
    {
        if (analysis is null || !analysis.IsMechanicallyValid) throw new ArgumentException("Admitted consistent differential analysis required.");
        if (input is null || !analysis.Request.InputPortIds.SequenceEqual(input.Values.Keys.OrderBy(x => x, StringComparer.Ordinal)))
            throw new ArgumentException("Complete exact input vector required; missing/extra keys are not mechanics underdetermination.");
        return new(analysis, input);
    }
    public static CarrierDisplayPose Display(DifferentialAnalysis analysis, DifferentialInputSnapshot input)
    {
        _ = Evaluate(analysis, input);
        if (!analysis.IsFullyDetermined) return new(Array.Empty<KeyValuePair<string, ReadOnlyCollection<double>>>(), "Underdetermined coordinates: no complete mechanism pose.");
        return CarrierEvaluator.Project(analysis.PoseNodes.Select(n => new CarrierPoseNode(n.Id, n.ParentId, n.Frame, new ExactAffineRelation(0, n.Rotation.Evaluate(input)), n.ShaftId, n.BodyId, n.Teeth, n.PitchRadiusMm)), 0);
    }
}
