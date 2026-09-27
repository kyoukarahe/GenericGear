using System;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Scoped affine/world motion comparison with distinct geometry, independent specimen and whole-admission observations.</summary>
public sealed class WormDriveOutputEquivalenceResult
{
    internal WormDriveOutputEquivalenceResult(WormDriveAnalysis before, WormDriveAnalysis after, OutputEquivalenceResult motion)
    {
        Motion = motion; BeforeCompatibility = before.LocalCompatibility.Verdict; AfterCompatibility = after.LocalCompatibility.Verdict;
        BeforeGeometryId = before.LocalCompatibility.Geometry?.GeometryId; AfterGeometryId = after.LocalCompatibility.Geometry?.GeometryId;
        SameGeometry = BeforeGeometryId is null || AfterGeometryId is null ? null : BeforeGeometryId == AfterGeometryId;
        BeforeWormSpecificationId = before.Draft.Definition.Device.Worm.SpecificationId; AfterWormSpecificationId = after.Draft.Definition.Device.Worm.SpecificationId;
        BeforeWheelSpecificationId = before.Draft.Definition.Device.SelectedWheel?.SpecificationId; AfterWheelSpecificationId = after.Draft.Definition.Device.SelectedWheel?.SpecificationId;
        SameWormSpecification = BeforeWormSpecificationId == AfterWormSpecificationId;
        SameWheelSpecification = BeforeWheelSpecificationId is not null && BeforeWheelSpecificationId == AfterWheelSpecificationId;
        BeforeWholeValid = before.IsMechanicallyValid; AfterWholeValid = after.IsMechanicallyValid;
        ComparisonId = HashText(Pack(motion.ComparisonId, BeforeCompatibility.ToString(), AfterCompatibility.ToString(), BeforeGeometryId ?? "", AfterGeometryId ?? "",
            BeforeWormSpecificationId, AfterWormSpecificationId, BeforeWheelSpecificationId ?? "", AfterWheelSpecificationId ?? "",
            WormDriveProfile.Flag(BeforeWholeValid), WormDriveProfile.Flag(AfterWholeValid)));
    }
    public string ComparisonId { get; }
    public OutputComparisonRequest Request => Motion.Request;
    public OutputEquivalenceResult Motion { get; }
    public OutputEquivalenceVerdict Verdict => Motion.Verdict;
    public string Scope => Motion.Scope;
    public ConnectionCompatibilityVerdict BeforeCompatibility { get; }
    public ConnectionCompatibilityVerdict AfterCompatibility { get; }
    public string? BeforeGeometryId { get; }
    public string? AfterGeometryId { get; }
    public bool? SameGeometry { get; }
    public string BeforeWormSpecificationId { get; }
    public string AfterWormSpecificationId { get; }
    public string? BeforeWheelSpecificationId { get; }
    public string? AfterWheelSpecificationId { get; }
    public bool SameWormSpecification { get; }
    public bool SameWheelSpecification { get; }
    public bool BeforeWholeValid { get; }
    public bool AfterWholeValid { get; }
}

public static class WormDriveComparer
{
    public static WormDriveOutputEquivalenceResult Compare(WormDriveAnalysis before, WormDriveAnalysis after, OutputComparisonRequest request)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException();
        return new(before, after, RotationalComparisonKernel.Compare(RotationalObservation.From(before), RotationalObservation.From(after), request));
    }
}
