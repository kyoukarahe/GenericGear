using System;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Rotational motion equivalence is independent of chain selection and structural material-path identity.</summary>
public sealed class PitchChainOutputEquivalenceResult
{
    internal PitchChainOutputEquivalenceResult(PitchChainAnalysis before, PitchChainAnalysis after, OutputEquivalenceResult motion)
    {
        Motion = motion;
        BeforeLinkVerdict = before.LocalCompatibility.LinkCompatibility.Verdict; AfterLinkVerdict = after.LocalCompatibility.LinkCompatibility.Verdict;
        BeforeGeometryId = before.LocalCompatibility.Geometry?.GeometryId; AfterGeometryId = after.LocalCompatibility.Geometry?.GeometryId;
        SameGeometry = BeforeGeometryId is null || AfterGeometryId is null ? null : BeforeGeometryId == AfterGeometryId;
        BeforePhaseRegistrationId = before.LocalCompatibility.PhaseRegistration?.RegistrationId;
        AfterPhaseRegistrationId = after.LocalCompatibility.PhaseRegistration?.RegistrationId;
        BeforeChainSpecificationId = before.Draft.Definition.Device.SelectedChain?.SpecificationId;
        AfterChainSpecificationId = after.Draft.Definition.Device.SelectedChain?.SpecificationId;
        SameChainSpecification = BeforeChainSpecificationId is not null && BeforeChainSpecificationId == AfterChainSpecificationId;
        BeforeMaterialConstructionId = MaterialKey(before); AfterMaterialConstructionId = MaterialKey(after);
        SameMaterialConstruction = BeforeMaterialConstructionId is null || AfterMaterialConstructionId is null ? null : BeforeMaterialConstructionId == AfterMaterialConstructionId;
        BeforeWholeValid = before.IsMechanicallyValid; AfterWholeValid = after.IsMechanicallyValid;
        ComparisonId = HashText(Pack(motion.ComparisonId, BeforeLinkVerdict.ToString(), AfterLinkVerdict.ToString(), BeforeGeometryId ?? "", AfterGeometryId ?? "",
            BeforePhaseRegistrationId ?? "", AfterPhaseRegistrationId ?? "", BeforeChainSpecificationId ?? "", AfterChainSpecificationId ?? "",
            BeforeMaterialConstructionId ?? "", AfterMaterialConstructionId ?? "", PitchChainProfile.Flag(BeforeWholeValid), PitchChainProfile.Flag(AfterWholeValid)));
    }
    public string ComparisonId { get; }
    public OutputComparisonRequest Request => Motion.Request;
    public OutputEquivalenceResult Motion { get; }
    public OutputEquivalenceVerdict Verdict => Motion.Verdict;
    public string Scope => Motion.Scope;
    public PitchChainLinkVerdict BeforeLinkVerdict { get; }
    public PitchChainLinkVerdict AfterLinkVerdict { get; }
    public string? BeforeGeometryId { get; }
    public string? AfterGeometryId { get; }
    public bool? SameGeometry { get; }
    public string? BeforePhaseRegistrationId { get; }
    public string? AfterPhaseRegistrationId { get; }
    public string? BeforeChainSpecificationId { get; }
    public string? AfterChainSpecificationId { get; }
    public bool SameChainSpecification { get; }
    public string? BeforeMaterialConstructionId { get; }
    public string? AfterMaterialConstructionId { get; }
    /// <summary>Exact structural recipe and physical root-law equality, not sample-based link-path certification. False is not a proof of path inequivalence.</summary>
    public bool? SameMaterialConstruction { get; }
    public bool BeforeWholeValid { get; }
    public bool AfterWholeValid { get; }
    private static string? MaterialKey(PitchChainAnalysis a)
    {
        var local = a.LocalCompatibility; var selected = a.Draft.Definition.Device.SelectedChain;
        if (!local.IsAdmitted || local.Geometry is null || local.PhaseRegistration is null || selected is null || !a.RetainedInputRelation.HasValue) return null;
        var phase = local.PhaseRegistration; var relation = a.RetainedInputRelation.Value;
        return HashText(Pack(PitchChainGeometry.MaterialRegistrationKey(local.Geometry, phase, selected),
            F(phase.InputAxisRouteSign * relation.Coefficient), F(phase.InputGammaTurns + phase.InputAxisRouteSign * relation.Phase)));
    }
}

public static class PitchChainEquivalence
{
    public static PitchChainOutputEquivalenceResult Compare(PitchChainAnalysis before, PitchChainAnalysis after, OutputComparisonRequest request)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException();
        return new PitchChainOutputEquivalenceResult(before, after,
            RotationalComparisonKernel.Compare(RotationalObservation.From(before), RotationalObservation.From(after), request));
    }
}
