using System;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>
/// A closed, engine-derived A*G(u)+B law. G is one actual, full-cycle-admitted
/// Geneva shaft, not its calibrated terminal, mean advance, or sampled pose.
/// This is deliberately not an authorable expression tree.
/// </summary>
public sealed class AssemblyGenevaAffineMotion
{
    internal AssemblyGenevaAffineMotion(AssemblyComponentReference genevaShaft, GenevaMotionDescriptor geneva,
        ExactAffineRelation transform)
    {
        MechanicalDerivedNumbers.Check(transform.Coefficient); MechanicalDerivedNumbers.Check(transform.Phase);
        GenevaShaft = genevaShaft; Geneva = geneva; Transform = transform;
        MotionId = HashText(CanonicalRepresentation);
    }
    public AssemblyComponentReference GenevaShaft { get; }
    public GenevaMotionDescriptor Geneva { get; }
    /// <summary>Coefficient and datum relative to G, never a global-root velocity.</summary>
    public ExactAffineRelation Transform { get; }
    public string MotionKind => "ExactAffineOfActualGenevaShaft";
    public string MotionId { get; }
    public string CanonicalRepresentation => Pack(MotionKind, GenevaShaft.CanonicalRepresentation,
        Geneva.DescriptorId, F(Transform.Coefficient), F(Transform.Phase));
    internal AssemblyGenevaAffineMotion Then(Rational coefficient, Rational datum) =>
        new(GenevaShaft, Geneva, Transform.Then(coefficient, datum));
    public AssemblyGenevaAffinePose CreatePoseRecipe(ExactQuantity root) => At(Geneva.At(root));
    internal AssemblyGenevaAffinePose At(GenevaPoseRecipe pose)
    {
        if (!ReferenceEquals(pose.Motion, Geneva)) throw new ArgumentException("Recipe must belong to the same actual current Geneva instance.");
        return new(this, pose);
    }
}

/// <summary>Exact accumulated turns plus a rational coefficient of the unchanged Geneva residual.</summary>
public sealed class AssemblyGenevaAffinePose
{
    internal AssemblyGenevaAffinePose(AssemblyGenevaAffineMotion motion, GenevaPoseRecipe geneva)
    {
        Motion = motion; Geneva = geneva;
        AccumulatedTurns = motion.Transform.Evaluate(geneva.AccumulatedShaftTurns);
        ResidualCoefficient = motion.Transform.Coefficient * motion.Geneva.EpsilonOut;
        ExactTurns = geneva.ExactResidualTurns.HasValue ? AccumulatedTurns + ResidualCoefficient * geneva.ExactResidualTurns.Value : (Rational?)null;
        MechanicalDerivedNumbers.Check(AccumulatedTurns); MechanicalDerivedNumbers.Check(ResidualCoefficient);
        if (ExactTurns.HasValue) MechanicalDerivedNumbers.Check(ExactTurns.Value);
        RecipeId = HashText(CanonicalRepresentation);
    }
    public AssemblyGenevaAffineMotion Motion { get; }
    public GenevaPoseRecipe Geneva { get; }
    public Rational AccumulatedTurns { get; }
    public Rational ResidualCoefficient { get; }
    public Rational? ExactTurns { get; }
    public string RecipeId { get; }
    public string CanonicalRepresentation => Pack(Motion.MotionId, Geneva.RecipeId, F(AccumulatedTurns),
        F(ResidualCoefficient), ExactTurns.HasValue ? F(ExactTurns.Value) : "");
}

/// <summary>Signed belt material displacement: pi*(A*G(u)+B) mm, not a rational length.</summary>
public sealed class AssemblyGenevaBeltTravel
{
    internal AssemblyGenevaBeltTravel(AssemblyGenevaAffinePose input, ExactAffineRelation localTravel)
    {
        Input = input; PiCoefficientOverGenevaShaft = input.Motion.Transform.Then(localTravel.Coefficient, localTravel.Phase);
        AccumulatedPiCoefficientMm = PiCoefficientOverGenevaShaft.Evaluate(input.Geneva.AccumulatedShaftTurns);
        ResidualPiCoefficientMm = PiCoefficientOverGenevaShaft.Coefficient * input.Geneva.Motion.EpsilonOut;
        ExactPiCoefficientMm = input.Geneva.ExactResidualTurns.HasValue ?
            AccumulatedPiCoefficientMm + ResidualPiCoefficientMm * input.Geneva.ExactResidualTurns.Value : (Rational?)null;
        foreach (var value in new[] { PiCoefficientOverGenevaShaft.Coefficient, PiCoefficientOverGenevaShaft.Phase,
            AccumulatedPiCoefficientMm, ResidualPiCoefficientMm }) MechanicalDerivedNumbers.Check(value);
    }
    public AssemblyGenevaAffinePose Input { get; }
    public string Kind => "PiTimesAffineOfActualGenevaShaft";
    public string Unit => "mm";
    public ExactAffineRelation PiCoefficientOverGenevaShaft { get; }
    public Rational AccumulatedPiCoefficientMm { get; }
    public Rational ResidualPiCoefficientMm { get; }
    public Rational? ExactPiCoefficientMm { get; }
}
