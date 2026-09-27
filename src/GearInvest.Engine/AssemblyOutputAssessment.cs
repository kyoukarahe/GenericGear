using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Owned output resolution and independent reference requirements, without supplying or changing motion.</summary>
internal static class AssemblyOutputAssessment
{
    internal static AssemblyOutputAnalysis Analyze(AssemblyOutputBinding o, MechanicalAssemblyDefinition d, MechanicalAnalysis root,
        Dictionary<string, ExactAffineRelation> rootLaws, Dictionary<string, AssemblyMemberAnalysis> members, AssemblyNumericBudget budget)
    {
        ExactAffineRelation? law = null;
        AssemblyOutputCapability? capability = null;
        bool resolved = false;
        var issues = new List<AssemblyDiagnostic>();
        var target = MechanicalAxisVerdict.Pass;
        AssemblyMemberAnalysis? member = null;
        if (o.Target.Owner == AssemblyOwnerKind.Root)
        {
            if (o.Target.Kind == AssemblyComponentKind.Shaft && rootLaws.TryGetValue(o.Target.LocalId, out var r))
            {
                law = r;
                resolved = true;
            }
            if (o.Target.Kind == AssemblyComponentKind.Output)
            {
                var output = root.Outputs.FirstOrDefault(x => x.OutputKey == o.Target.LocalId);
                law = output?.PortRelation;
                resolved = output?.HasDeterminedMotion == true;
            }
            if (o.Target.Kind == AssemblyComponentKind.Port)
            {
                var p = d.Root.Definition.Ports.FirstOrDefault(x => x.Id == o.Target.LocalId);
                var s = d.Root.Definition.Shafts.FirstOrDefault(x => x.Id == p?.ShaftId);
                if (p is not null && s is not null && MechanicalConnectionPredicates.PortMounting(p, s) && rootLaws.TryGetValue(s.Id, out var portLaw))
                {
                    law = portLaw.Then(s.Frame.Z.Dot(p.Frame.Z), p.PhaseOffset);
                    resolved = true;
                }
            }
            capability = AssemblyOutputCapability.AffineRotaryShaft;
        }
        else if (members.TryGetValue(o.Target.MemberId!, out member))
        {
            capability = member.Capability;
            if (o.Target.Kind == AssemblyComponentKind.Shaft && MechanicalAssemblyComponents.Shaft(member.Member.Declaration)?.Id == o.Target.LocalId)
            {
                law = member.ShaftRelation;
                resolved = member.HasDeterminedMotion;
            }
            if (o.Target.Kind == AssemblyComponentKind.Output && member.Member.Declaration.OutputKey == o.Target.LocalId)
            {
                law = member.TerminalRelation;
                resolved = member.HasDeterminedMotion;
            }
            if (o.Target.Kind == AssemblyComponentKind.Port && MechanicalAssemblyComponents.Port(member.Member.Declaration)?.Id == o.Target.LocalId)
            {
                law = member.TerminalRelation;
                resolved = member.HasDeterminedMotion;
            }
            if (o.Target.Kind == AssemblyComponentKind.LinearDof && MechanicalAssemblyComponents.MemberInventory(member.Member).Any(x => x.Reference.Equals(o.Target))) resolved = member.HasDeterminedMotion;
        }
        if (!resolved)
        {
            target = MechanicalAxisVerdict.NotAssessed;
            issues.Add(new("UnresolvedAssemblyOutput", "AssemblyOutput", "No determined current motion exists for this exact owned output reference.", new[] { o.Target }, member?.Binding.DependencyPath));
        }
        if (o.RequiredGlobalTransfer.HasValue && (!law.HasValue || law.Value.Coefficient != o.RequiredGlobalTransfer.Value))
        {
            target = MechanicalAxisVerdict.Fail;
            issues.Add(new("TargetMismatch", "AssemblyOutput", "The requested exact affine transfer is not supplied by this current output; averages are not affine pose laws.", new[] { o.Target }));
        }
        if (o.RequiredReferenceValue.HasValue && resolved)
        {
            var reference = o.RequiredReferenceValue.Value;
            if (law.HasValue)
            {
                if (o.ReferenceRoot.Kind != QuantityKind.AngularPosition || reference.Kind != QuantityKind.AngularPosition || law.Value.Evaluate(o.ReferenceRoot.Value) != reference.Value)
                {
                    target = MechanicalAxisVerdict.Fail;
                    issues.Add(new("TargetMismatch", "AssemblyOutputReference", "Exact global reference requirement differs from the current terminal/shaft law.", new[] { o.Target }));
                }
            }
            else
            {
                if (member?.GenevaShaftMotion is not null)
                {
                    var motion = o.Target.Kind == AssemblyComponentKind.Shaft ? member.GenevaShaftMotion : member.GenevaTerminalMotion!;
                    var verdict = reference.Kind == QuantityKind.AngularPosition ? AssessGenevaReference(motion, o.ReferenceRoot, reference.Value, budget) : MechanicalAxisVerdict.Fail;
                    if (verdict != MechanicalAxisVerdict.Pass)
                    {
                        target = verdict;
                        issues.Add(new(verdict == MechanicalAxisVerdict.Fail ? "TargetMismatch" : "IncompleteNumericBudget", "AssemblyOutputReference", "The current exact affine-of-Geneva reference was not proved equal.", new[] { o.Target }));
                    }
                }
                else
                {
                    // No finite sample is accepted as a whole-function proof. This is a scoped certified reference requirement.
                    var value = member?.TerminalLocal is null ? null : AssemblyTerminalMechanics.Evaluate(member.TerminalLocal, o.ReferenceRoot, budget, false);
                    var verdict = value is null ? MechanicalAxisVerdict.NotAssessed : AssessTerminalReference(value, reference, o.Target.Kind);
                    if (verdict != MechanicalAxisVerdict.Pass)
                    {
                        target = verdict;
                        issues.Add(new(verdict == MechanicalAxisVerdict.Fail ? "TargetMismatch" : "IncompleteNumericBudget", "AssemblyOutputReference", "Certified requested reference value was not proved equal.", new[] { o.Target }));
                    }
                }
            }
        }
        return new(o, resolved, capability, law, target, issues);
    }

    internal static MechanicalAxisVerdict AssessGenevaReference(AssemblyGenevaAffineMotion law,
        ExactQuantity root, Rational expected, AssemblyNumericBudget budget)
    {
        var pose = law.CreatePoseRecipe(root);
        if (pose.ExactTurns.HasValue) return pose.ExactTurns.Value == expected ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
        try
        {
            var scale = law.Transform.Coefficient.Sign < 0 ? -law.Transform.Coefficient : law.Transform.Coefficient;
            var sample = budget.GenevaSamples.Get(law.Geneva, root, budget.Request.AngularWidth.Value / (scale > 1 ? scale : 1), budget);
            if (!sample.Numeric.IsAvailable) return MechanicalAxisVerdict.Inconclusive;
            var interval = AssemblyGenevaNumerics.Transform(pose, sample.Numeric.Pose!.ResidualTurns, budget);
            return interval.Lower > expected || interval.Upper < expected ? MechanicalAxisVerdict.Fail :
                interval.IsExact ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Inconclusive;
        }
        catch (CrankSliderNumerics.Stop)
        {
            return MechanicalAxisVerdict.Inconclusive;
        }
    }

    private static MechanicalAxisVerdict AssessTerminalReference(AssemblyTerminalLocalEvaluation evaluated, ExactQuantity expected, AssemblyComponentKind kind)
    {
        if (!evaluated.NumericAvailable) return MechanicalAxisVerdict.Inconclusive;
        if (evaluated.Recipe is GenevaPoseRecipe geneva)
        {
            if (expected.Kind != QuantityKind.AngularPosition) return MechanicalAxisVerdict.Fail;
            var exact = kind == AssemblyComponentKind.Shaft ? geneva.ExactShaftTurns : geneva.ExactTerminalTurns;
            if (exact.HasValue) return exact.Value == expected.Value ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
            var pose = (evaluated.Numeric as GenevaNumericComputation)?.Pose;
            var interval = kind == AssemblyComponentKind.Shaft ? pose?.OutputShaftTurns : pose?.TerminalTurns;
            return interval is null ? MechanicalAxisVerdict.Inconclusive : Compare(interval.Lower, interval.Upper, expected.Value);
        }
        if (evaluated.Recipe is CamFollowerPoseRecipe cam)
        {
            return expected.Kind != QuantityKind.LinearPosition || (kind == AssemblyComponentKind.LinearDof ? cam.GuidePosition.Value : cam.TerminalPosition.Value) != expected.Value
                ? MechanicalAxisVerdict.Fail : MechanicalAxisVerdict.Pass;
        }
        if (evaluated.Numeric is CrankSliderNumericComputation crank)
        {
            if (expected.Kind != QuantityKind.LinearPosition) return MechanicalAxisVerdict.Fail;
            var interval = kind == AssemblyComponentKind.LinearDof ? crank.Pose?.GuidePositionMm : crank.Pose?.TerminalPositionMm;
            return interval is null ? MechanicalAxisVerdict.Inconclusive : Compare(interval.Lower, interval.Upper, expected.Value);
        }
        return MechanicalAxisVerdict.Inconclusive;
    }
    private static MechanicalAxisVerdict Compare(Rational lower, Rational upper, Rational expected) => lower > expected || upper < expected ? MechanicalAxisVerdict.Fail :
        lower == upper ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Inconclusive;
}
