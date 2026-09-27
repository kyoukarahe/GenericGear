using System;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Optional new-profile projection, after every independent numeric observation has been evaluated.</summary>
internal static class AssemblyDeferredDisplay
{
    internal static AssemblyMemberEvaluation Apply(string analysisId, AssemblyMemberAnalysis node,
        AssemblyMemberEvaluation current, ExactQuantity root, AssemblyNumericBudget budget)
    {
        var start = budget.UsedWork; var diagnostics = current.Diagnostics.ToList(); object? display = null; var available = false;
        try
        {
            if (current.GenevaSuffix is not null && current.NumericAvailable)
            { display = AssemblyGenevaSuffixEvaluator.Display(node, current.GenevaSuffix, analysisId, root, budget); available = true; }
            else if (current.NumericAvailable)
            {
                switch (node.Member.Declaration)
                {
                    case AssemblyWormDeclaration w:
                        display = WormDriveDisplay.ApproximateLocal(w.Device, node.Binding.FixedInputFrameMm!, node.WormCompatibility!, current.WormOutput!, analysisId, root); available = true; break;
                    case AssemblyOpenBeltDeclaration:
                        display = OpenBeltDisplay.Approximate(node.BeltCompatibility!.Route!); available = true; break;
                    case AssemblyPitchChainDeclaration:
                        display = PitchChainDisplay.Approximate(current.ChainMaterial!); available = true; break;
                    case AssemblyGenevaDeclaration:
                        var gn = GenevaNumerics.EvaluateFeatures(GenevaAnalyzer.NumericInput((GenevaPoseRecipe)current.Recipe!, true),
                            (GenevaNumericComputation)current.Numeric!, budget.GenevaRequest);
                        budget.Debit(gn.Work); display = gn; available = gn.IsAvailable; break;
                    case AssemblyCrankSliderDeclaration:
                        display = current.Numeric; available = true; break;
                    case AssemblyCamFollowerDeclaration:
                        var cam = AssemblyCamDisplay.Evaluate(node.TerminalLocal!.AnalysisId, node.TerminalLocal.Cam!, root, budget);
                        display = cam; available = cam.IsAvailable; break;
                }
            }
            else if (node.Member.Declaration is AssemblyGenevaDeclaration g && current.InputObservation is GenevaDriverObservation driver && driver.PhysicalPhaseTurns.HasValue)
            {
                var gn = GenevaNumerics.EvaluateDriver(driver.PhysicalPhaseTurns.Value, g.Device.OrbitRadius,
                    g.Device.DriverCenterMm, g.Device.CenterDirection, g.Device.TransverseDirection,
                    budget.GenevaRequest, GenevaAnalyzer.FeatureInputs(g.Device, null, false));
                budget.Debit(gn.Work); display = gn; available = gn.IsAvailable;
            }
        }
        catch (Exception e) when (e is ArgumentException || e is ArithmeticException || e is InvalidOperationException || e is CrankSliderNumerics.Stop ||
            e is WormDriveDisplayUnavailableException || e is OpenBeltDisplayUnavailableException || e is PitchChainDisplayUnavailableException)
        { diagnostics.Add(new("DisplayUnavailable", "OptionalMaterialDisplay", e.Message, new[] { MechanicalAssemblyComponents.MemberConstraint(node.Member) }, node.Binding.DependencyPath)); }
        if (!available && diagnostics.All(x => x.Code != "DisplayUnavailable"))
            diagnostics.Add(new("DisplayUnavailable", "OptionalMaterialDisplay", "Optional current material geometry is unavailable; existing numeric observations remain unchanged.",
                new[] { MechanicalAssemblyComponents.MemberConstraint(node.Member) }, node.Binding.DependencyPath));
        return new(current.InstanceId, current.Status, current.InputTurns, current.AffineOutput, current.Material, current.Recipe,
            current.Numeric, current.InputObservation, display, current.NumericAvailable, available, current.NumericWork + budget.UsedWork - start, diagnostics, current.GenevaSuffix);
    }
}
