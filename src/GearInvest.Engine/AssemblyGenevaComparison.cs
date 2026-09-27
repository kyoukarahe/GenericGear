using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class AssemblyGenevaComparisonEvidence
{
    internal AssemblyGenevaComparisonEvidence(AssemblyGenevaAffineMotion before, AssemblyGenevaAffineMotion after,
        Rational? constantDifference, IEnumerable<GenevaComparisonWitness> witnesses)
    {
        BeforeMotionId = before.MotionId; AfterMotionId = after.MotionId; ConstantDifference = constantDifference;
        Witnesses = witnesses.ToList().AsReadOnly();
        EvidenceId = HashText(Pack(BeforeMotionId, AfterMotionId, constantDifference.HasValue ? F(constantDifference.Value) : "",
            Pack(Witnesses.Select(w => w.CanonicalRepresentation).ToArray())));
    }
    public string BeforeMotionId { get; }
    public string AfterMotionId { get; }
    public Rational? ConstantDifference { get; }
    public ReadOnlyCollection<GenevaComparisonWitness> Witnesses { get; }
    public string EvidenceId { get; }
}

internal static class AssemblyGenevaComparer
{
    internal static (AssemblyOutputComparisonVerdict Verdict, string Rule, AssemblyGenevaComparisonEvidence Evidence) Compare(
        AssemblyGenevaAffineMotion before, AssemblyGenevaAffineMotion after, MechanicalAssemblyOutputComparisonRequest request, AssemblyNumericBudget budget)
    {
        var witnesses = new List<GenevaComparisonWitness>(); Rational? constant = null;
        (AssemblyOutputComparisonVerdict, string, AssemblyGenevaComparisonEvidence) Result(AssemblyOutputComparisonVerdict verdict, string rule) =>
            (verdict, rule, new(before, after, constant, witnesses));
        var mappedAfter = after.Transform.Then(request.OutputSign, request.OutputDatum.Value);
        if (GenevaOutputComparer.TryMappedAngularIdentity(before.Geneva, after.Geneva, before.Transform, mappedAfter,
            request.InputAlpha.Value, request.InputBeta.Value, out var difference))
        {
            constant = difference;
            if (difference.IsZero || request.Mode == AssemblyOutputComparisonMode.WrappedAngular && difference.Denominator.IsOne)
                return Result(AssemblyOutputComparisonVerdict.Equivalent, "ExactAffineGenevaMappedPhaseCycleIdentity");
        }
        GenevaInterval? Enclose(AssemblyGenevaAffineMotion law, ExactQuantity root)
        {
            var recipe = law.CreatePoseRecipe(root);
            if (recipe.ExactTurns.HasValue) return new(new CrankSliderInterval(recipe.ExactTurns.Value, recipe.ExactTurns.Value));
            var scale = law.Transform.Coefficient.Sign < 0 ? -law.Transform.Coefficient : law.Transform.Coefficient;
            var sample = budget.GenevaSamples.Get(law.Geneva, root, budget.Request.AngularWidth.Value / (scale > 1 ? scale : 1), budget);
            return !sample.Numeric.IsAvailable ? null : new(AssemblyGenevaNumerics.Transform(recipe, sample.Numeric.Pose!.ResidualTurns, budget));
        }
        foreach (var root in request.WitnessRoots)
        {
            var mappedRoot = ExactQuantity.Turns(request.InputAlpha.Value * root.Value + request.InputBeta.Value);
            var left = Enclose(before, root); var right = Enclose(after.Then(request.OutputSign, request.OutputDatum.Value), mappedRoot);
            if (left is null || right is null) continue;
            var witness = new GenevaComparisonWitness(root, mappedRoot, left, right,
                request.Mode == AssemblyOutputComparisonMode.WrappedAngular ? GenevaComparisonMode.WrappedOrientation : GenevaComparisonMode.UnwrappedAngularOutput);
            witnesses.Add(witness);
            if (witness.IsSeparating) return Result(AssemblyOutputComparisonVerdict.Different, left.IsExact && right.IsExact ? "ExactAffineGenevaSeparatingWitness" : "CertifiedAffineGenevaSeparatingWitness");
        }
        return Result(AssemblyOutputComparisonVerdict.Inconclusive, "BoundedAffineGenevaProofRulesExhausted");
    }
}
