using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>One actual external-spur stage on an existing fixed-axis E2 owner; no surrogate shaft.</summary>
public sealed class DifferentialSuffixDefinition
{
    public const string Profile = "one-e2-fixed-axis-external-spur-suffix-v1";
    public DifferentialSuffixDefinition(DifferentialRequest parent, string sourceShaftId, OrientedShaft outputShaft,
        OrientedGearBody driverGear, OrientedGearBody outputGear, string contactId, ExactQuantity driverMount,
        ExactQuantity outputMount, Rational toothRegistration, ExactQuantity outputReference, CarrierOutputPort outputPort)
    {
        Parent = parent; SourceShaftId = MechanicalAuthoringProfile.IdValue(sourceShaftId); OutputShaft = outputShaft;
        DriverGear = driverGear; OutputGear = outputGear; ContactId = MechanicalAuthoringProfile.IdValue(contactId);
        foreach (var q in new[] { driverMount, outputMount, outputReference }) DifferentialProfile.Angular(q);
        MechanicalAuthoringProfile.Number(toothRegistration);
        DriverMount = driverMount; OutputMount = outputMount; ToothRegistration = toothRegistration; OutputReference = outputReference; OutputPort = outputPort;
        MechanicalAuthoringProfile.FrameBound(outputShaft.Frame); MechanicalAuthoringProfile.IdValue(outputShaft.Id);
        foreach (var body in new[] { driverGear, outputGear }) { MechanicalAuthoringProfile.IdValue(body.Id); MechanicalAuthoringProfile.FrameBound(body.MountingFrame); MechanicalAuthoringProfile.Number(body.OuterPitchRadius); }
        DefinitionId = HashText(Pack(Profile, parent.RequestId, sourceShaftId, outputShaft.Id, Frame(outputShaft.Frame), outputShaft.IsPrescribed.ToString(),
            Body(driverGear), Body(outputGear), contactId, F(driverMount.Value), F(outputMount.Value), F(toothRegistration), F(outputReference.Value),
            outputPort.Id, outputPort.ShaftId, Frame(outputPort.FrameInShaft), F(outputPort.ReadoutOffset.Value)));
    }
    private static string Body(OrientedGearBody b) => Pack(b.Id, b.ShaftId, b.Kind.ToString(), Frame(b.MountingFrame), N(b.Teeth), F(b.OuterPitchRadius), b.SourceModuleId ?? "");
    public DifferentialRequest Parent { get; }
    public string SourceShaftId { get; }
    public OrientedShaft OutputShaft { get; }
    public OrientedGearBody DriverGear { get; }
    public OrientedGearBody OutputGear { get; }
    public string ContactId { get; }
    public ExactQuantity DriverMount { get; }
    public ExactQuantity OutputMount { get; }
    public Rational ToothRegistration { get; }
    public ExactQuantity OutputReference { get; }
    public CarrierOutputPort OutputPort { get; }
    public string DefinitionId { get; }
}

public sealed class DifferentialSuffixAnalysis
{
    internal DifferentialSuffixAnalysis(DifferentialSuffixDefinition source, DifferentialAnalysis parent, IEnumerable<string> errors, DifferentialLaw? law, Rational transfer, Rational offset)
    { Source = source; Parent = parent; Diagnostics = errors.ToList().AsReadOnly(); OutputLaw = law; Transfer = transfer; Offset = offset; }
    public DifferentialSuffixDefinition Source { get; }
    public DifferentialAnalysis Parent { get; }
    public ReadOnlyCollection<string> Diagnostics { get; }
    public DifferentialLaw? OutputLaw { get; }
    public Rational Transfer { get; }
    public Rational Offset { get; }
    public bool IsValid => Diagnostics.Count == 0 && OutputLaw is not null;
}

public static class DifferentialSuffixAnalyzer
{
    public static CarrierDisplayPose Display(DifferentialSuffixAnalysis a, DifferentialInputSnapshot input)
    {
        if (!a.IsValid) throw new ArgumentException("Admitted suffix required.");
        var parent = DifferentialEvaluator.Evaluate(a.Parent, input); var s = a.Source;
        var nodes = a.Parent.PoseNodes.Select(n => new CarrierPoseNode(n.Id,n.ParentId,n.Frame,new ExactAffineRelation(0,n.Rotation.Evaluate(input)),n.ShaftId,n.BodyId,n.Teeth,n.PitchRadiusMm)).ToList();
        nodes.Add(new(ConnectedPoseIds.DriverGear,null,s.DriverGear.MountingFrame,new ExactAffineRelation(0,parent.Coordinates[s.SourceShaftId]+s.DriverMount.Value),s.SourceShaftId,s.DriverGear.Id,s.DriverGear.Teeth,s.DriverGear.OuterPitchRadius));
        nodes.Add(new(ConnectedPoseIds.OutputGear,null,s.OutputGear.MountingFrame,new ExactAffineRelation(0,a.OutputLaw!.Evaluate(input)+s.OutputMount.Value),s.OutputShaft.Id,s.OutputGear.Id,s.OutputGear.Teeth,s.OutputGear.OuterPitchRadius));
        return CarrierEvaluator.Project(nodes,0);
    }
    public static DifferentialSuffixAnalysis Prepare(DifferentialSuffixDefinition s)
    {
        var a = DifferentialAnalyzer.Prepare(s.Parent); var d = s.Parent.Definition; var errors = new List<string>();
        if (!a.CanExport) errors.Add("ParentNotFinalizable");
        var shaft = s.SourceShaftId == d.CarrierShaft.Id ? d.CarrierShaft : s.SourceShaftId == d.SunShaft.Id ? d.SunShaft : null;
        if (shaft is null) errors.Add("UnsupportedMovingOrUnknownSourceOwner");
        var ids = new[] { s.OutputShaft.Id, s.DriverGear.Id, s.OutputGear.Id, s.ContactId, s.OutputPort.Id };
        var old = a.CoordinateIds.Concat(new[] { d.CarrierBodyId, d.SunBodyId, d.PlanetBodyId }).Concat(d.Ports.Select(p => p.Id))
            .Concat(d.Prefix?.Definition.Bodies.Select(b => b.Id) ?? Array.Empty<string>()).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(old.Contains)) errors.Add("DuplicateIdentity");
        if (s.DriverGear.ShaftId != s.SourceShaftId || s.OutputGear.ShaftId != s.OutputShaft.Id || s.OutputShaft.IsPrescribed) errors.Add("InvalidActualOwner");
        if (!s.OutputShaft.Frame.IsProperCardinal || !s.DriverGear.MountingFrame.IsProperCardinal || !s.OutputGear.MountingFrame.IsProperCardinal) errors.Add("UnsupportedFrame");
        Rational transfer = 0, offset = 0; DifferentialLaw? law = null;
        if (shaft is not null && errors.Count == 0)
        {
            if (!MechanicalConnectionPredicates.BodyMounting(s.DriverGear, shaft, 4096) || !MechanicalConnectionPredicates.BodyMounting(s.OutputGear, s.OutputShaft, 4096) || shaft.Frame.X != s.OutputShaft.Frame.X || s.DriverGear.MountingFrame.Z != shaft.Frame.Z || s.OutputGear.MountingFrame.Z != s.OutputShaft.Frame.Z)
                errors.Add("InvalidBodyMounting");
            else
            {
                var geometry = MechanicalConnectionPredicates.Contact(s.ContactId, OrientedContactKind.ExternalSpur, s.DriverGear, s.OutputGear, shaft, s.OutputShaft, null);
                if (!geometry.IsCompatible) return new(s,a,new[]{"InvalidPitchContact"},null,0,0);
                var sign = shaft.Frame.Z.Dot(s.OutputShaft.Frame.Z); transfer = geometry.Transfer;
                offset = (s.ToothRegistration - s.DriverGear.Teeth * s.DriverMount.Value) / (s.OutputGear.Teeth * sign) - s.OutputMount.Value;
                var reference = s.SourceShaftId == d.CarrierShaft.Id ? d.CarrierReference.Value : d.SunReference.Value;
                if (s.ToothRegistration.Denominator != 1 || transfer * reference + offset != s.OutputReference.Value) errors.Add("InconsistentReferenceRegistration");
                // Same-plane interference with unrelated parent pitch circles is not silently waived.
                var station = (s.DriverGear.MountingFrame.Origin - d.PlaneMm.Origin).Dot(d.PlaneMm.Z);
                if (station == 0) errors.Add("UnsupportedOverlappingParentPitchPlane");
                if (d.Prefix is not null && s.DriverGear.MountingFrame.Origin.Dot(shaft.Frame.Z) == d.PrefixMapping!.FrameMm(OrientedFrame.Identity).Origin.Dot(shaft.Frame.Z)) errors.Add("UnsupportedOverlappingPrefixPitchPlane");
            }
            var p = s.OutputPort;
            if (p.ShaftId != s.OutputShaft.Id || !p.FrameInShaft.IsProperCardinal || p.FrameInShaft.Origin.X != 0 || p.FrameInShaft.Origin.Y != 0 || p.FrameInShaft.Z.X != 0 || p.FrameInShaft.Z.Y != 0 || p.ReadoutOffset.Kind != QuantityKind.AngularPosition) errors.Add("InvalidOutputPort");
            if (errors.Count == 0) law = a.Coordinates.Single(c => c.ShaftId == shaft.Id).Law!.Then(transfer, offset);
        }
        return new(s, a, errors, law, transfer, offset);
    }
}
