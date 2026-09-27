using System;
using System.Collections.Generic;
using GearInvest.Core;
using static GearInvest.Engine.CamNumericalPrimitives;

namespace GearInvest.Engine;

internal sealed class AssemblyGenevaSample
{
    internal AssemblyGenevaSample(GenevaPoseRecipe recipe, GenevaNumericComputation numeric)
    { Recipe = recipe; Numeric = numeric; }
    internal GenevaPoseRecipe Recipe { get; }
    internal GenevaNumericComputation Numeric { get; }
}

internal sealed class AssemblyGenevaSampleCache
{
    private readonly Dictionary<string, AssemblyGenevaSample> samples = new(StringComparer.Ordinal);
    internal int Evaluations { get; private set; }
    internal int Reuses { get; private set; }
    internal int Refinements { get; private set; }
    internal AssemblyGenevaSample Get(GenevaMotionDescriptor motion, ExactQuantity root,
        Rational width, AssemblyNumericBudget budget)
    {
        var key = OrientedGoalKeys.Pack(motion.DescriptorId, MechanicalAssemblyProfile.Q(root));
        samples.TryGetValue(key, out var prior);
        if (prior?.Numeric.IsAvailable == true && prior.Numeric.Pose!.ResidualTurns.Width <= width)
        { Reuses++; return prior; }
        var recipe = prior?.Recipe ?? motion.At(root);
        var p = budget.Request;
        var request = new GenevaNumericRequest(ExactQuantity.Turns(width), p.LinearWidth, p.DirectionWidth,
            budget.RemainingWork, p.MaximumPrecisionBits, p.MaximumRefinements);
        var numeric = GenevaNumerics.Evaluate(GenevaAnalyzer.NumericInput(recipe, false), request);
        budget.Debit(numeric.Work); Evaluations++; if (prior is not null) Refinements++;
        var current = new AssemblyGenevaSample(recipe, numeric);
        // A failed stricter refinement never replaces a successful looser observation.
        if (numeric.IsAvailable || prior is null) samples[key] = current;
        return current;
    }
}

internal static class AssemblyGenevaNumerics
{
    internal static CrankSliderInterval Transform(AssemblyGenevaAffinePose pose, GenevaInterval residual, AssemblyNumericBudget budget)
    {
        var c = new CrankSliderNumerics.Context(budget.RemainingWork);
        try { return c.Add(Point(pose.AccumulatedTurns), c.Scale(GenevaNumericPose.Raw(residual), pose.ResidualCoefficient)); }
        finally { budget.Debit(c.Work); }
    }

    // Endpoints are reduced only after exact multiplication of all accumulated turns.
    // The 2*pi < 8 Lipschitz bound encloses sin/cos across extrema and modulo seams.
    internal static (CrankSliderInterval Sin, CrankSliderInterval Cos) SinCosEnclosed(
        CrankSliderNumerics.Context c, CrankSliderInterval angle)
    {
        var phase = angle.Lower - new Rational(GenevaProfile.Floor(angle.Lower));
        var pair = CrankSliderNumerics.SinCos(c, phase);
        var radius = c.Multiply(8, c.Width(angle));
        CrankSliderInterval Widen(CrankSliderInterval x)
        {
            var lower = c.Subtract(x.Lower, radius); var upper = c.Add(x.Upper, radius);
            return c.Interval(lower < -1 ? -1 : lower, upper > 1 ? 1 : upper);
        }
        return (Widen(pair.Sin), Widen(pair.Cos));
    }
}
