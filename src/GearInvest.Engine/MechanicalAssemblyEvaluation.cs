using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.MechanicalAssemblyComponents;

namespace GearInvest.Engine;

public static partial class MechanicalAssemblyAnalyzer
{
    public static MechanicalAssemblyEvaluation Evaluate(MechanicalAssemblyAnalysis analysis, ExactQuantity root,
        AssemblyNumericRequest? numericRequest = null)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        if (numericRequest is not null && numericRequest.CanonicalRepresentation != analysis.NumericRequest.CanonicalRepresentation)
            analysis = Analyze(analysis.Draft, numericRequest);
        var request = analysis.NumericRequest; var budget = new AssemblyNumericBudget(request); budget.Debit(analysis.NumericWork);
        budget.ShareGenevaSamples = analysis.Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId;
        var shafts = new List<AssemblyShaftEvaluation>(); var results = new List<AssemblyMemberEvaluation>(); var issues = new List<AssemblyDiagnostic>();
        if (root.Kind != QuantityKind.AngularPosition)
        { issues.Add(new("DimensionMismatch", "AssemblyEvaluation", "Absolute global input must be unwrapped turns.")); return new(analysis, root, shafts, results, issues, budget.UsedWork, 0); }
        try { MechanicalAuthoringProfile.Number(root.Value); }
        catch (ArgumentException e) { issues.Add(new("NumericResourceLimit", "AssemblyEvaluation", e.Message)); return new(analysis, root, shafts, results, issues, budget.UsedWork, 0); }
        var rootLaws = RootLaws(analysis.RootAnalysis); var mapping = analysis.Draft.Definition.RootMapping;
        if (mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal)
        {
            // One actual root evaluation. Local device kernels consume the already compiled context, not another root source.
            foreach (var value in MechanicalAnalyzer.Evaluate(analysis.RootAnalysis, root.Value))
            {
                var frame = mapping.FrameMm(analysis.Draft.Definition.Root.Definition.Shafts.Single(s => s.Id == value.ShaftId).Frame);
                shafts.Add(new(AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, value.ShaftId), frame, value.Turns, rootLaws[value.ShaftId]));
            }
        }
        foreach (var id in analysis.TopologicalOrder)
        {
            var node = analysis.Members.Single(m => m.InstanceId == id); var localIssues = new List<AssemblyDiagnostic>();
            ExactQuantity? input = null; object? output = null, material = null, display = null, recipe = null, numeric = null, observation = null;
            AssemblyGenevaSuffixEvaluation? suffix = null;
            var status = "UndeterminedInput"; var available = false; var displayAvailable = !request.IncludeDisplay; var start = budget.UsedWork;
            try
            {
                if (node.Binding.HasDeterminedInput && node.InputRelation.HasValue) { var turns = node.InputRelation.Value.Evaluate(root.Value); MechanicalDerivedNumbers.Check(turns); input = ExactQuantity.Turns(turns); }
                if (node.TerminalLocal is not null)
                {
                    var evaluated = AssemblyTerminalMechanics.Evaluate(node.TerminalLocal, root, budget, request.IncludeDisplay && !budget.ShareGenevaSamples);
                    recipe = evaluated.Recipe; numeric = evaluated.Numeric; observation = evaluated.InputObservation; display = evaluated.Display;
                    status = evaluated.Status; available = evaluated.NumericAvailable; displayAvailable = evaluated.DisplayAvailable;
                    localIssues.AddRange(evaluated.Diagnostics.Select(d => LocalIssue(node.Member, node.Binding.DependencyPath, d)));
                }
                else if (node.HasDeterminedMotion && node.Binding.GenevaInputMotion is not null)
                {
                    suffix = AssemblyGenevaSuffixEvaluator.Evaluate(node, root, budget);
                    input = suffix.Input.ExactTurns.HasValue ? ExactQuantity.Turns(suffix.Input.ExactTurns.Value) : (ExactQuantity?)null;
                    available = suffix.IsAvailable; status = available ? "Success" : suffix.Status.ToString();
                    material = (object?)node.WormCompatibility?.Geometry ?? node.BeltCompatibility?.Route;
                    if (available && request.IncludeDisplay && !budget.ShareGenevaSamples)
                    {
                        try
                        {
                            display = AssemblyGenevaSuffixEvaluator.Display(node, suffix, analysis.AnalysisId, root, budget);
                            displayAvailable = true;
                        }
                        catch (Exception e) when (e is ArgumentException || e is ArithmeticException || e is InvalidOperationException ||
                            e is CrankSliderNumerics.Stop || e is WormDriveDisplayUnavailableException || e is OpenBeltDisplayUnavailableException)
                        { displayAvailable = false; localIssues.Add(new("DisplayUnavailable", "OptionalMaterialDisplay", e.Message, new[] { MemberConstraint(node.Member) }, node.Binding.DependencyPath)); }
                    }
                }
                else if (node.HasDeterminedMotion && node.ShaftRelation.HasValue && node.TerminalRelation.HasValue)
                {
                    switch (node.Member.Declaration)
                    {
                        case AssemblyWormDeclaration w:
                        {
                            var ds = new List<MechanicalDiagnostic>(); output = WormDriveAnalyzer.EvaluateLocal(w.Device, w.OutputTerminal, w.Output, node.WormCompatibility!,
                                node.InputRelation!.Value, node.ShaftRelation.Value, node.TerminalRelation.Value, root.Value, ds);
                            material = node.WormCompatibility!.Geometry; localIssues.AddRange(ds.Select(d => LocalIssue(node.Member, node.Binding.DependencyPath, d))); break;
                        }
                        case AssemblyOpenBeltDeclaration b:
                            output = OpenBeltAnalyzer.EvaluateLocal(b.Device, b.OutputTerminal, b.Output, node.BeltCompatibility!, node.InputRelation!.Value, node.ShaftRelation.Value, node.TerminalRelation.Value, root.Value);
                            material = node.BeltCompatibility!.Route; break;
                        case AssemblyPitchChainDeclaration c:
                            output = PitchChainAnalyzer.EvaluateLocal(c.Device, c.OutputTerminal, c.Output, node.ChainCompatibility!, node.InputRelation!.Value, node.ShaftRelation.Value, node.TerminalRelation.Value, root.Value, out var chainMaterial);
                            material = chainMaterial; break;
                    }
                    available = output is not null; status = available ? "Success" : "InvalidDefinition";
                    if (available)
                    {
                        var s = MechanicalAssemblyComponents.Shaft(node.Member.Declaration)!;
                        shafts.Add(new(AssemblyComponentReference.Member(id, AssemblyComponentKind.Shaft, s.Id), s.Frame, node.ShaftRelation.Value.Evaluate(root.Value), node.ShaftRelation.Value));
                    }
                    // Optional approximate display is isolated after exact material evaluation. A failure cannot delete current motion or a downstream pose.
                    if (available && request.IncludeDisplay && !budget.ShareGenevaSamples)
                    {
                        try
                        {
                            display = node.Member.Declaration switch
                            {
                                AssemblyWormDeclaration w => WormDriveDisplay.ApproximateLocal(w.Device, node.Binding.FixedInputFrameMm!, node.WormCompatibility!, (WormDriveOutputEvaluation)output!, analysis.AnalysisId, root),
                                AssemblyOpenBeltDeclaration => OpenBeltDisplay.Approximate(node.BeltCompatibility!.Route!),
                                AssemblyPitchChainDeclaration => PitchChainDisplay.Approximate((IndexedChainPoseDescriptor)material!),
                                _ => null
                            };
                            displayAvailable = display is not null;
                        }
                        catch (Exception e) when (e is ArgumentException || e is ArithmeticException || e is InvalidOperationException ||
                            e is WormDriveDisplayUnavailableException || e is OpenBeltDisplayUnavailableException || e is PitchChainDisplayUnavailableException)
                        { display = null; displayAvailable = false; localIssues.Add(new("DisplayUnavailable", "OptionalMaterialDisplay", e.Message, new[] { MemberConstraint(node.Member) }, node.Binding.DependencyPath)); }
                    }
                }
                else if (input.HasValue) status = "InvalidLocalConstraint";
            }
            catch (ArgumentException e)
            {
                output = null; recipe = null; numeric = null; display = null; available = displayAvailable = false; status = "NumericResourceLimit";
                localIssues.Add(new("NumericResourceLimit", "AssemblyMemberEvaluation", e.Message, new[] { MemberConstraint(node.Member) }, node.Binding.DependencyPath));
            }
            if (!available && localIssues.Count == 0) localIssues.Add(new(status, "AssemblyMemberEvaluation", "Current member output is unavailable; no previous pose or reference position substitutes for it.", new[] { MemberOutput(node.Member) }, node.Binding.DependencyPath));
            results.Add(new(id, status, input, output, material, recipe, numeric, observation, display, available, displayAvailable, budget.UsedWork - start, localIssues, suffix)); issues.AddRange(localIssues);
        }
        if (budget.ShareGenevaSamples && request.IncludeDisplay)
        {
            // In the new profile all independent numeric observations precede optional presentation work.
            for (var index = 0; index < results.Count; index++)
            {
                var node = analysis.Members.Single(m => m.InstanceId == results[index].InstanceId);
                results[index] = AssemblyDeferredDisplay.Apply(analysis.AnalysisId, node, results[index], root, budget);
            }
            issues = results.SelectMany(x => x.Diagnostics).ToList();
        }
        return new(analysis, root, shafts, results, issues, budget.UsedWork, 1);
    }
}
