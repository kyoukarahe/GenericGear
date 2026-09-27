using System;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.CamNumericalPrimitives;

namespace GearInvest.Engine;

/// <summary>Current nonlinear shaft/material observations. No affine-root velocity fields are populated.</summary>
public sealed class AssemblyGenevaSuffixEvaluation
{
    internal AssemblyGenevaSuffixEvaluation(AssemblyGenevaAffinePose input, AssemblyGenevaAffinePose shaft, AssemblyGenevaAffinePose terminal)
    { Input = input; Shaft = shaft; Terminal = terminal; }
    public AssemblyGenevaAffinePose Input { get; }
    public AssemblyGenevaAffinePose Shaft { get; }
    public AssemblyGenevaAffinePose Terminal { get; }
    public ExactAffineRelation? WormInputPhaseOverGenevaShaft { get; internal set; }
    public ExactAffineRelation? WormWheelPhaseOverGenevaShaft { get; internal set; }
    public bool? ExactWormPhaseIdentity { get; internal set; }
    public AssemblyGenevaBeltTravel? BeltTravel { get; internal set; }
    public CrankSliderInterval? InputTurns { get; internal set; }
    public CrankSliderInterval? ShaftTurns { get; internal set; }
    public CrankSliderInterval? TerminalTurns { get; internal set; }
    public CrankSliderInterval? BeltTravelMm { get; internal set; }
    public GenevaNumericStatus Status { get; internal set; } = GenevaNumericStatus.IncompleteNumericBudget;
    public int PrecisionBits { get; internal set; }
    public int NumericWork { get; internal set; }
    public int UpstreamEvaluations { get; internal set; }
    public int UpstreamReuses { get; internal set; }
    public int UpstreamRefinements { get; internal set; }
    public bool IsAvailable => Status is GenevaNumericStatus.ExactValue or GenevaNumericStatus.EnclosedValue;
    public string Detail { get; internal set; } = "";
}

/// <summary>Optional finite projection of current certified observations, never a motion input.</summary>
public sealed class AssemblyGenevaSuffixDisplay
{
    internal AssemblyGenevaSuffixDisplay(double inputTurns, double shaftTurns, WormDriveDisplayPose? worm,
        OpenBeltDisplayRoute? belt, OpenBeltDisplayPoint[] marks, int numericWork)
    { InputMaterialTurnsModuloOne = inputTurns; ShaftMaterialTurnsModuloOne = shaftTurns;
        Worm = worm; Belt = belt; BeltMaterialMarksMm = Array.AsReadOnly(marks); NumericWork = numericWork; }
    public double InputMaterialTurnsModuloOne { get; }
    public double ShaftMaterialTurnsModuloOne { get; }
    public WormDriveDisplayPose? Worm { get; }
    public OpenBeltDisplayRoute? Belt { get; }
    public ReadOnlyCollection<OpenBeltDisplayPoint> BeltMaterialMarksMm { get; }
    public int NumericWork { get; }
}

internal static class AssemblyGenevaSuffixEvaluator
{
    private static Rational Abs(Rational value) => value.Sign < 0 ? -value : value;
    internal static AssemblyGenevaSuffixEvaluation Evaluate(AssemblyMemberAnalysis node, ExactQuantity root, AssemblyNumericBudget budget)
    {
        var start = budget.UsedWork; var p = budget.Request; var cache = budget.GenevaSamples;
        var evaluations = cache.Evaluations; var reuses = cache.Reuses; var refinements = cache.Refinements;
        var inputLaw = node.Binding.GenevaInputMotion!; var outputLaw = node.GenevaShaftMotion!; var terminalLaw = node.GenevaTerminalMotion!;
        var width = p.AngularWidth.Value;
        foreach (var motion in new[] { inputLaw, outputLaw, terminalLaw })
        { var scale = Abs(motion.Transform.Coefficient); if (scale > 1 && p.AngularWidth.Value / scale < width) width = p.AngularWidth.Value / scale; }
        if (node.Member.Declaration is AssemblyOpenBeltDeclaration belt)
        {
            var factor = Abs(LocalTransmissionRelations.BeltTravelPiCoefficient(belt.Device, node.BeltCompatibility!).Coefficient * inputLaw.Transform.Coefficient);
            if (factor > 0 && p.LinearWidth.Value / (8 * factor) < width) width = p.LinearWidth.Value / (8 * factor);
        }
        var shared = cache.Get(inputLaw.Geneva, root, width, budget);
        var result = new AssemblyGenevaSuffixEvaluation(inputLaw.At(shared.Recipe), outputLaw.At(shared.Recipe), terminalLaw.At(shared.Recipe)) { PrecisionBits = shared.Numeric.PrecisionBits };
        if (node.Member.Declaration is AssemblyWormDeclaration worm)
        {
            var wi = LocalTransmissionRelations.WormInputPhase(worm.Device, node.WormCompatibility!);
            var wo = LocalTransmissionRelations.WormWheelPhase(worm.Device, node.WormCompatibility!);
            var a = inputLaw.Transform.Then(wi.Coefficient, wi.Phase);
            var b = outputLaw.Transform.Then(wo.Coefficient, wo.Phase);
            result.WormInputPhaseOverGenevaShaft = a; result.WormWheelPhaseOverGenevaShaft = b;
            result.ExactWormPhaseIdentity = a.Coefficient == b.Coefficient && a.Phase == b.Phase;
            if (result.ExactWormPhaseIdentity != true) throw new ArgumentException("Exact common-Geneva worm phase coefficients disagree.");
        }
        if (node.Member.Declaration is AssemblyOpenBeltDeclaration beltDeclaration)
            result.BeltTravel = new(result.Input, LocalTransmissionRelations.BeltTravelPiCoefficient(beltDeclaration.Device, node.BeltCompatibility!));
        var c = new CrankSliderNumerics.Context(budget.RemainingWork);
        try
        {
            if (!shared.Numeric.IsAvailable && !shared.Recipe.ExactResidualTurns.HasValue)
            { result.Status = shared.Numeric.Status; result.Detail = shared.Numeric.Detail; return result; }
            var residual = shared.Recipe.ExactResidualTurns.HasValue ? Point(shared.Recipe.ExactResidualTurns.Value) : GenevaNumericPose.Raw(shared.Numeric.Pose!.ResidualTurns);
            CrankSliderInterval Angle(AssemblyGenevaAffinePose value) => c.Add(Point(value.AccumulatedTurns), c.Scale(residual, value.ResidualCoefficient));
            result.InputTurns = Angle(result.Input); result.ShaftTurns = Angle(result.Shaft); result.TerminalTurns = Angle(result.Terminal);
            var angular = new[] { result.InputTurns, result.ShaftTurns, result.TerminalTurns };
            if (angular.Any(x => c.Width(x!) > p.AngularWidth.Value))
            { result.Detail = "Propagated upstream angular enclosure exceeds the requested width."; return result; }
            if (result.BeltTravel is not null)
            {
                var travel = result.BeltTravel;
                var coefficient = c.Add(Point(travel.AccumulatedPiCoefficientMm), c.Scale(residual, travel.ResidualPiCoefficientMm));
                var passes = 0;
                foreach (var bits in Precisions)
                {
                    if (bits > p.MaximumPrecisionBits || passes++ >= p.MaximumRefinements) break;
                    c.SetPrecision(bits); result.PrecisionBits = bits;
                    var mm = Multiply(c, coefficient, Pi(c));
                    if (c.Width(mm) <= p.LinearWidth.Value) { result.BeltTravelMm = mm; break; }
                }
                if (result.BeltTravelMm is null) { result.Detail = "Material displacement width was not proved within the precision/refinement budget."; return result; }
            }
            result.Status = angular.All(x => x!.IsExact) && (result.BeltTravelMm is null || result.BeltTravelMm.IsExact) ? GenevaNumericStatus.ExactValue : GenevaNumericStatus.EnclosedValue;
            result.Detail = "Exact accumulated turns and the shared residual propagated with signed endpoint ordering; no midpoint becomes mechanical truth.";
            return result;
        }
        catch (CrankSliderNumerics.Stop stop) { result.Status = GenevaNumerics.Status(stop); result.Detail = stop.Message; return result; }
        finally
        {
            budget.Debit(c.Work); result.NumericWork = budget.UsedWork - start;
            result.UpstreamEvaluations = cache.Evaluations - evaluations; result.UpstreamReuses = cache.Reuses - reuses;
            result.UpstreamRefinements = cache.Refinements - refinements;
        }
    }

    internal static AssemblyGenevaSuffixDisplay Display(AssemblyMemberAnalysis node, AssemblyGenevaSuffixEvaluation value,
        string analysisId, ExactQuantity root, AssemblyNumericBudget budget)
    {
        if (!value.IsAvailable) throw new ArgumentException("Current successful suffix numeric observations are required for optional display.");
        var c = new CrankSliderNumerics.Context(budget.RemainingWork);
        var p = budget.Request;
        try
        {
            double Reduced(CrankSliderInterval angle, Rational sign, Rational mounting)
            {
                var transformed = c.Scale(c.Add(angle, Point(mounting)), sign);
                // Do not convert a huge unwrapped value to double before reduction.
                var lower = transformed.Lower - new Rational(GenevaProfile.Floor(transformed.Lower));
                return OpenBeltDisplay.Number(lower + transformed.Width / 2);
            }
            if (node.Member.Declaration is AssemblyWormDeclaration w)
            {
                var input = Reduced(value.InputTurns!, node.WormCompatibility!.Epsilon!.Value, w.Device.InputMountingPhase.Value);
                var output = Reduced(value.ShaftTurns!, node.WormCompatibility.Sigma!.Value, w.Device.OutputMountingPhase.Value);
                var display = WormDriveDisplay.ApproximateMaterialLocal(w.Device, node.Binding.FixedInputFrameMm!, node.WormCompatibility,
                    input * 2 * Math.PI, output * 2 * Math.PI, analysisId, root, c);
                // Rotor scalars use shaft coordinates; the helical sampler uses physical material-axis signs.
                return new(Reduced(value.InputTurns!, 1, w.Device.InputMountingPhase.Value), Reduced(value.ShaftTurns!, 1, w.Device.OutputMountingPhase.Value),
                    display, null, Array.Empty<OpenBeltDisplayPoint>(), c.Work);
            }
            var belt = node.BeltCompatibility!.Route!; var route = OpenBeltDisplay.Approximate(belt);
            var length = belt.Length; var passes = 0;
            foreach (var bits in Precisions)
            {
                if (bits > p.MaximumPrecisionBits || passes++ >= p.MaximumRefinements) break;
                c.SetPrecision(bits);
                var span = c.Sqrt(Point(length.SpanLengthSquared), true);
                var cosine = c.Sqrt(Point(1 - length.AsinArgument * length.AsinArgument), true);
                var asin = GenevaNumerics.Atan(c, GenevaNumerics.Divide(c, Point(length.AsinArgument), cosine));
                var loop = c.Add(c.Scale(span, 2), c.Add(c.Scale(Pi(c), length.PiCoefficient), c.Scale(asin, length.AsinCoefficient)));
                var quotient = GenevaNumerics.Divide(c, value.BeltTravelMm!, loop);
                var low = GenevaProfile.Floor(quotient.Lower); var high = GenevaProfile.Floor(quotient.Upper);
                if (low != high) continue;
                var remainder = c.Subtract(value.BeltTravelMm!, c.Scale(loop, new Rational(low)));
                if (c.Width(remainder) > p.LinearWidth.Value * 4) continue;
                var boundedDistance = OpenBeltDisplay.Number((remainder.Lower + remainder.Upper) / 2);
                var marks = Enumerable.Range(0, 12).Select(i => { c.Step(); return route.SampleDistance(boundedDistance + route.LoopLengthMm * i / 12); }).ToArray();
                return new(Reduced(value.InputTurns!, 1, 0), Reduced(value.ShaftTurns!, 1, 0), null, route, marks, c.Work);
            }
            throw new OpenBeltDisplayUnavailableException("A unique material loop remainder was not enclosed within the optional display budget.");
        }
        finally { budget.Debit(c.Work); }
    }
}
