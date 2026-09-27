using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Small shared lowering of an admitted rotary-to-prismatic relation. Device geometry/admission stay outside this kernel.</summary>
internal sealed class PrismaticGraphAnalysis
{
    private PrismaticGraphAnalysis(QuantityDof[] nodes, List<QuantityAffineCoupling> edges,
        IEnumerable<AffineComponentAnalysis> components, MechanicalDeterminacy determinacy,
        bool declaredReachable, bool admittedReachable, string[] declaredPath, string[] blocked,
        ExactAffineRelation? sourceRelation, DimensionedAffineRelation? guideRelation, string[] path)
    {
        Nodes = nodes; Edges = edges; Components = components.ToArray(); Determinacy = determinacy;
        DeclaredReachable = declaredReachable; AdmittedReachable = admittedReachable; DeclaredPath = declaredPath; Blocked = blocked;
        SourceRelation = sourceRelation; GuideRelation = guideRelation; Path = path;
    }
    internal QuantityDof[] Nodes { get; }
    internal List<QuantityAffineCoupling> Edges { get; }
    internal AffineComponentAnalysis[] Components { get; }
    internal MechanicalDeterminacy Determinacy { get; }
    internal bool DeclaredReachable { get; }
    internal bool AdmittedReachable { get; }
    internal string[] DeclaredPath { get; }
    internal string[] Blocked { get; }
    internal ExactAffineRelation? SourceRelation { get; }
    internal DimensionedAffineRelation? GuideRelation { get; }
    internal string[] Path { get; }

    internal static PrismaticGraphAnalysis Build(MechanicalDraft sourceDraft, MechanicalAnalysis source, string sourceShaftId,
        string linearDofId, string edgeKey, bool transmissionPresent, ConnectionCompatibilityVerdict localVerdict,
        ExactQuantity? localGain, ExactQuantity referenceTurns, ExactQuantity referencePosition)
    {
        var admitted = transmissionPresent && localVerdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile;
        var nodes = sourceDraft.Definition.Shafts.Select(s => new QuantityDof(s.Id, QuantityKind.AngularPosition, s.IsPrescribed))
            .Concat(new[] { new QuantityDof(linearDofId, QuantityKind.LinearPosition) }).ToArray();
        var edges = MechanicalAnalyzer.LowerAdmitted(sourceDraft.Definition, source.Edges).Select(e => new QuantityAffineCoupling(e.Id, e.DriverDofId, e.DrivenDofId,
            ExactQuantity.FromCanonical(QuantityKind.AngularPerAngular, e.Transfer), ExactQuantity.FromCanonical(QuantityKind.AngularPosition, e.PhaseOffset))).ToList();
        if (admitted)
        {
            if (!localGain.HasValue || localGain.Value.Kind != QuantityKind.LinearPerAngular ||
                referenceTurns.Kind != QuantityKind.AngularPosition || referencePosition.Kind != QuantityKind.LinearPosition)
                throw new ArgumentException("Admitted prismatic relation requires typed local gain and reference configuration.");
            var offset = referencePosition.Value - localGain.Value.Value * referenceTurns.Value;
            MechanicalDerivedNumbers.Check(offset);
            edges.Add(new QuantityAffineCoupling(edgeKey, sourceShaftId, linearDofId, localGain.Value, ExactQuantity.FromCanonical(QuantityKind.LinearPosition, offset)));
        }
        var selected = source.SelectedInputId is not null && nodes.Any(n => n.Id == source.SelectedInputId) ? source.SelectedInputId : null;
        var components = QuantityAffineComponentAnalyzer.Analyze(selected, nodes, edges);
        var linearComponent = components.Single(k => k.MemberIds.Contains(linearDofId));
        var sourceDeclared = source.DeclaredComponents.FirstOrDefault(k => k.ShaftIds.Contains(sourceShaftId));
        var declaredReachable = transmissionPresent && sourceDeclared?.IsSelectedInputReachable == true;
        var admittedReachable = selected is not null && linearComponent.MemberIds.Contains(selected);
        var declaredPath = declaredReachable ? MechanicalAnalyzer.Path(selected!, sourceShaftId, source.Edges.Where(e => e.IsDeclaredResolvable)).Concat(new[] { edgeKey }).ToArray() : Array.Empty<string>();
        var blocked = declaredPath.Where(key => key == edgeKey ? !admitted : !source.Edges.Single(e => e.ConstraintKey == key).IsAdmitted).ToArray();
        var status = localVerdict == ConnectionCompatibilityVerdict.Unsupported ? MechanicalDeterminacy.UnsupportedConstraintDomain
            : localVerdict != ConnectionCompatibilityVerdict.CompatibleWithinProfile ? MechanicalDeterminacy.BlockedByInvalidConstraint
            : declaredReachable && !admittedReachable ? MechanicalDeterminacy.BlockedByInvalidConstraint : linearComponent.Determinacy;
        ExactAffineRelation? retained = null; DimensionedAffineRelation? guide = null; var path = Array.Empty<string>();
        if (status == MechanicalDeterminacy.DeterminedBySelectedInput)
        {
            retained = linearComponent.Relations.Single(n => n.DofId == sourceShaftId).Relation;
            var relation = linearComponent.Relations.Single(n => n.DofId == linearDofId);
            guide = PrismaticAlgebra.LinearRelation(relation.Relation.Coefficient, relation.Relation.Phase); path = relation.ConstraintPath.ToArray();
        }
        return new PrismaticGraphAnalysis(nodes, edges, components, status, declaredReachable, admittedReachable, declaredPath, blocked, retained, guide, path);
    }
}

internal delegate void PrismaticIssue(string code, string stage, IEnumerable<MechanicalExactFact>? facts, string detail);

internal readonly struct PrismaticRequirementAssessment
{
    private PrismaticRequirementAssessment(MechanicalAxisVerdict target, MechanicalAxisVerdict requiredRange)
    { Target = target; RequiredRange = requiredRange; }
    internal MechanicalAxisVerdict Target { get; }
    internal MechanicalAxisVerdict RequiredRange { get; }

    internal static PrismaticRequirementAssessment Assess(LinearOutputRequirement requirement, DimensionedAffineRelation terminal,
        ExactQuantityInterval? validInterval, PrismaticIssue issue)
    {
        var dimensions = (!requirement.RequiredGain.HasValue || requirement.RequiredGain.Value.Kind == QuantityKind.LinearPerAngular) &&
            (!requirement.RequiredReferencePosition.HasValue || requirement.RequiredReferencePosition.Value.Kind == QuantityKind.LinearPosition);
        var target = dimensions && (!requirement.RequiredGain.HasValue || requirement.RequiredGain.Value == terminal.Gain) &&
            (!requirement.RequiredReferencePosition.HasValue || requirement.RequiredReferencePosition.Value == terminal.Offset) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
        if (!dimensions) issue("DimensionMismatch", "LinearRequirement", null, "");
        else if (target == MechanicalAxisVerdict.Fail) issue("TargetMismatch", "LinearRequirement", new[] {
            new MechanicalExactFact("terminalGain.mmPerRootTurn", requirement.RequiredGain?.Value, terminal.Gain.Value),
            new MechanicalExactFact("terminalReferenceAtRootZero.mm", requirement.RequiredReferencePosition?.Value, terminal.Offset.Value) }, "");
        MechanicalAxisVerdict required;
        if (requirement.RequiredRootInterval.Kind != QuantityKind.AngularPosition)
        { required = MechanicalAxisVerdict.Fail; issue("DimensionMismatch", "RequiredOperatingDomain", null, ""); }
        else
        {
            required = validInterval is not null && validInterval.Contains(requirement.RequiredRootInterval) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
            if (required == MechanicalAxisVerdict.Fail) issue("OperatingRangeMismatch", "RequiredOperatingDomain", new[] {
                new MechanicalExactFact("requiredLowerRoot.turn", requirement.RequiredRootInterval.Lower.Value, validInterval?.Lower.Value),
                new MechanicalExactFact("requiredUpperRoot.turn", requirement.RequiredRootInterval.Upper.Value, validInterval?.Upper.Value) }, "");
        }
        return new PrismaticRequirementAssessment(target, required);
    }
}

internal static class PrismaticAlgebra
{
    internal static DimensionedAffineRelation LinearRelation(Rational gain, Rational offset) => new(
        ExactQuantity.FromCanonical(QuantityKind.LinearPerAngular, gain), ExactQuantity.FromCanonical(QuantityKind.LinearPosition, offset));
    internal static DimensionedAffineRelation Terminal(DimensionedAffineRelation guide, int sign, ExactQuantity datum) =>
        LinearRelation(sign * guide.Gain.Value, sign * guide.Offset.Value + datum.Value);
    internal static ExactQuantityInterval? OperatingDomain(ExactQuantityInterval guideTravel, ExactQuantityInterval contactTravel, DimensionedAffineRelation guide) =>
        guideTravel.Intersect(contactTravel)?.InverseAffine(guide.Gain.Value, guide.Offset.Value, QuantityKind.AngularPosition);
    internal static void Bound(ExactVector3 vector)
    { MechanicalDerivedNumbers.Check(vector.X); MechanicalDerivedNumbers.Check(vector.Y); MechanicalDerivedNumbers.Check(vector.Z); }
    internal static void Bound(ExactPiVector3 vector) { Bound(vector.RationalPartMm); Bound(vector.InversePiCoefficientMm); }

    internal static IEnumerable<OrientedShaftEvaluation> EvaluateSource(MechanicalAnalysis source, SourceLengthMapping? mapping, Rational root)
    {
        if (mapping is null || mapping.MillimetersPerSourceUnit <= 0 || !mapping.PoseMm.IsProperCardinal) return Array.Empty<OrientedShaftEvaluation>();
        return MechanicalAnalyzer.Evaluate(source, root).Select(item => new OrientedShaftEvaluation(item.ShaftId, item.Turns,
            mapping.Direction(item.PositiveAxis), item.PositiveAxis.Dot(item.WorldAngularVelocityPerRoot))).ToArray();
    }
}
