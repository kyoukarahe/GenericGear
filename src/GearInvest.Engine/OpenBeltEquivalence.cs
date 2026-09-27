using System;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Scoped motion comparison plus independent current length, route identity and whole-assembly admission readbacks.</summary>
public sealed class OpenBeltOutputEquivalenceResult
{
    internal OpenBeltOutputEquivalenceResult(OpenBeltAnalysis before, OpenBeltAnalysis after, OutputEquivalenceResult motion)
    {
        Motion = motion; BeforeLength = before.LocalCompatibility.LengthCompatibility.Verdict; AfterLength = after.LocalCompatibility.LengthCompatibility.Verdict;
        BeforeRouteId = before.LocalCompatibility.Route?.RouteId; AfterRouteId = after.LocalCompatibility.Route?.RouteId;
        SameRoute = BeforeRouteId is null || AfterRouteId is null ? null : BeforeRouteId == AfterRouteId;
        BeforeBeltSpecificationId = before.Draft.Definition.Device.SelectedBelt?.SpecificationId;
        AfterBeltSpecificationId = after.Draft.Definition.Device.SelectedBelt?.SpecificationId;
        SameBeltSpecification = BeforeBeltSpecificationId is not null && BeforeBeltSpecificationId == AfterBeltSpecificationId;
        BeforeWholeValid = before.IsMechanicallyValid; AfterWholeValid = after.IsMechanicallyValid;
        ComparisonId = HashText(Pack(motion.ComparisonId, BeforeLength.ToString(), AfterLength.ToString(), BeforeRouteId ?? "", AfterRouteId ?? "",
            BeforeBeltSpecificationId ?? "", AfterBeltSpecificationId ?? "", OpenBeltProfile.Flag(BeforeWholeValid), OpenBeltProfile.Flag(AfterWholeValid)));
    }
    public string ComparisonId { get; }
    public OutputComparisonRequest Request => Motion.Request;
    public OutputEquivalenceResult Motion { get; }
    public OutputEquivalenceVerdict Verdict => Motion.Verdict;
    public string Scope => Motion.Scope;
    public OpenBeltLengthVerdict BeforeLength { get; }
    public OpenBeltLengthVerdict AfterLength { get; }
    public string? BeforeRouteId { get; }
    public string? AfterRouteId { get; }
    public bool? SameRoute { get; }
    public string? BeforeBeltSpecificationId { get; }
    public string? AfterBeltSpecificationId { get; }
    public bool SameBeltSpecification { get; }
    public bool BeforeWholeValid { get; }
    public bool AfterWholeValid { get; }
}

public static class OpenBeltOutputComparer
{
    public static OpenBeltOutputEquivalenceResult Compare(OpenBeltAnalysis before, OpenBeltAnalysis after, OutputComparisonRequest request)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException();
        return new(before, after, RotationalComparisonKernel.Compare(RotationalObservation.From(before), RotationalObservation.From(after), request));
    }
}
