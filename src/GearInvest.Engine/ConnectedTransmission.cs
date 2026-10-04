using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class FixedAxisSpurStage
{
    public FixedAxisSpurStage(string sourceShaftId, OrientedShaft outputShaft, OrientedGearBody driverGear,
        OrientedGearBody outputGear, string contactId, Rational driverMountTurns, Rational outputMountTurns,
        Rational toothRegistration, Rational outputReferenceTurns, CarrierOutputPort outputPort)
    {
        SourceShaftId = MechanicalAuthoringProfile.IdValue(sourceShaftId); OutputShaft = outputShaft; DriverGear = driverGear; OutputGear = outputGear;
        ContactId = MechanicalAuthoringProfile.IdValue(contactId); DriverMountTurns = driverMountTurns; OutputMountTurns = outputMountTurns;
        ToothRegistration = toothRegistration; OutputReferenceTurns = outputReferenceTurns; OutputPort = outputPort;
        foreach (var v in new[] { driverMountTurns, outputMountTurns, toothRegistration, outputReferenceTurns }) MechanicalAuthoringProfile.Number(v);
        foreach (var id in new[] { outputShaft.Id, driverGear.Id, outputGear.Id }) MechanicalAuthoringProfile.IdValue(id);
        foreach (var f in new[] { outputShaft.Frame, driverGear.MountingFrame, outputGear.MountingFrame }) MechanicalAuthoringProfile.FrameBound(f);
        DefinitionId = HashText(Pack("fixed-axis-spur-stage-v1", sourceShaftId, outputShaft.Id, Frame(outputShaft.Frame), outputShaft.IsPrescribed.ToString(),
            Body(driverGear), Body(outputGear), contactId, F(driverMountTurns), F(outputMountTurns), F(toothRegistration), F(outputReferenceTurns),
            outputPort.Id, outputPort.ShaftId, Frame(outputPort.FrameInShaft), F(outputPort.ReadoutOffset.Value)));
    }
    private static string Body(OrientedGearBody b) => Pack(b.Id, b.ShaftId, b.Kind.ToString(), Frame(b.MountingFrame), N(b.Teeth), F(b.OuterPitchRadius), b.SourceModuleId ?? "");
    public string SourceShaftId { get; }
    public OrientedShaft OutputShaft { get; }
    public OrientedGearBody DriverGear { get; }
    public OrientedGearBody OutputGear { get; }
    public string ContactId { get; }
    public Rational DriverMountTurns { get; }
    public Rational OutputMountTurns { get; }
    public Rational ToothRegistration { get; }
    public Rational OutputReferenceTurns { get; }
    public CarrierOutputPort OutputPort { get; }
    public string DefinitionId { get; }
}

/// <summary>A rigid member in the terminal carrier frame, not another independently rotating shaft.</summary>
public sealed class CarrierRigidAttachment
{
    public CarrierRigidAttachment(string bodyId, OrientedFrame frameInCarrier)
    { BodyId = MechanicalAuthoringProfile.IdValue(bodyId); MechanicalAuthoringProfile.FrameBound(frameInCarrier); FrameInCarrier = frameInCarrier; }
    public string BodyId { get; }
    public OrientedFrame FrameInCarrier { get; }
}

public sealed class ConnectedTransmission
{
    public ConnectedTransmission(IEnumerable<FixedAxisSpurStage> stages, string carrierShaftId, string carrierBodyId,
        OrientedFrame carrierMountingFrame, Rational carrierMountTurns, IEnumerable<CarrierRigidAttachment> attachments)
    {
        var list = stages.Take(5).ToArray(); if (list.Length < 1 || list.Length > 4) throw new ArgumentException("ResourceLimit");
        Stages = Array.AsReadOnly(list); CarrierShaftId = MechanicalAuthoringProfile.IdValue(carrierShaftId); CarrierBodyId = MechanicalAuthoringProfile.IdValue(carrierBodyId);
        MechanicalAuthoringProfile.FrameBound(carrierMountingFrame); CarrierMountingFrame = carrierMountingFrame; MechanicalAuthoringProfile.Number(carrierMountTurns); CarrierMountTurns = carrierMountTurns;
        var mounts = attachments.Take(9).ToArray(); if (mounts.Length > 8) throw new ArgumentException("ResourceLimit"); Attachments = Array.AsReadOnly(mounts);
        DefinitionId = HashText(Pack("bounded-connected-spur-carrier-v1", Pack(list.Select(s => s.DefinitionId).ToArray()), carrierShaftId, carrierBodyId,
            Frame(carrierMountingFrame), F(carrierMountTurns), Pack(mounts.Select(m => Pack(m.BodyId, Frame(m.FrameInCarrier))).ToArray())));
    }
    public ReadOnlyCollection<FixedAxisSpurStage> Stages { get; }
    public string CarrierShaftId { get; }
    public string CarrierBodyId { get; }
    public OrientedFrame CarrierMountingFrame { get; }
    public Rational CarrierMountTurns { get; }
    public ReadOnlyCollection<CarrierRigidAttachment> Attachments { get; }
    public string DefinitionId { get; }
}

internal static class ConnectedTransmissionCompiler
{
    internal static string? Validate(WindingDifferentialDefinition source, ISet<string> existingIds)
    {
        var t = source.Transmission; if (t is null) return null;
        var shaft = source.Suffix.OutputShaft; var reference = source.Suffix.OutputReference.Value;
        var planeNormal = shaft.Frame.Z;
        var planes = new HashSet<Rational> { source.Suffix.DriverGear.MountingFrame.Origin.Dot(planeNormal), source.Suffix.Parent.Definition.PlaneMm.Origin.Dot(planeNormal) };
        foreach (var stage in t.Stages)
        {
            foreach (var id in new[] { stage.OutputShaft.Id, stage.DriverGear.Id, stage.OutputGear.Id, stage.ContactId, stage.OutputPort.Id })
                if (!existingIds.Add(id)) return "DuplicateIdentity";
            if (stage.SourceShaftId != shaft.Id || stage.DriverGear.ShaftId != shaft.Id || stage.OutputGear.ShaftId != stage.OutputShaft.Id || stage.OutputShaft.IsPrescribed) return "InvalidActualOwner";
            if (!stage.OutputShaft.Frame.IsProperCardinal || !stage.DriverGear.MountingFrame.IsProperCardinal || !stage.OutputGear.MountingFrame.IsProperCardinal) return "UnsupportedFrame";
            if (!MechanicalConnectionPredicates.BodyMounting(stage.DriverGear, shaft, 4096) || !MechanicalConnectionPredicates.BodyMounting(stage.OutputGear, stage.OutputShaft, 4096) ||
                shaft.Frame.X != stage.OutputShaft.Frame.X || stage.DriverGear.MountingFrame.Z != shaft.Frame.Z || stage.OutputGear.MountingFrame.Z != stage.OutputShaft.Frame.Z) return "InvalidBodyMounting";
            var contact = MechanicalConnectionPredicates.Contact(stage.ContactId, OrientedContactKind.ExternalSpur, stage.DriverGear, stage.OutputGear, shaft, stage.OutputShaft, null);
            if (!contact.IsCompatible) return "InvalidPitchContact";
            var (_, offset) = Relation(stage, shaft);
            if (stage.ToothRegistration.Denominator != 1 || contact.Transfer * reference + offset != stage.OutputReferenceTurns) return "InconsistentReferenceRegistration";
            if (!planes.Add(stage.DriverGear.MountingFrame.Origin.Dot(planeNormal))) return "UnsupportedOverlappingPitchPlane";
            var p = stage.OutputPort;
            if (p.ShaftId != stage.OutputShaft.Id || !p.FrameInShaft.IsProperCardinal || p.FrameInShaft.Origin.X != 0 || p.FrameInShaft.Origin.Y != 0 ||
                p.FrameInShaft.Z.X != 0 || p.FrameInShaft.Z.Y != 0 || p.ReadoutOffset.Kind != QuantityKind.AngularPosition) return "InvalidOutputPort";
            shaft = stage.OutputShaft; reference = stage.OutputReferenceTurns;
        }
        if (t.CarrierShaftId != shaft.Id || !t.CarrierMountingFrame.IsProperCardinal || !shaft.Contains(t.CarrierMountingFrame.Origin) ||
            t.CarrierMountingFrame.X != shaft.Frame.X || t.CarrierMountingFrame.Z != shaft.Frame.Z) return "InvalidCarrierAttachment";
        if (!existingIds.Add(t.CarrierBodyId)) return "DuplicateIdentity";
        foreach (var mount in t.Attachments)
            if (!mount.FrameInCarrier.IsProperCardinal) return "UnsupportedFrame"; else if (!existingIds.Add(mount.BodyId)) return "DuplicateIdentity";
        return null;
    }
    internal static (Rational Transfer, Rational Offset) Relation(FixedAxisSpurStage s, OrientedShaft driver)
    {
        var sign = driver.Frame.Z.Dot(s.OutputShaft.Frame.Z);
        return (-new Rational(s.DriverGear.Teeth, s.OutputGear.Teeth) / sign,
            (s.ToothRegistration - s.DriverGear.Teeth * s.DriverMountTurns) / (s.OutputGear.Teeth * sign) - s.OutputMountTurns);
    }
    internal static void Evaluate(WindingDifferentialDefinition s, IDictionary<string, ConnectedMotionValue> coordinates, IDictionary<string, ConnectedMotionValue> ports)
    {
        if (s.Transmission is null) return;
        var shaft = s.Suffix.OutputShaft;
        foreach (var stage in s.Transmission.Stages)
        {
            var (ratio, offset) = Relation(stage, shaft); var value = coordinates[shaft.Id].Scale(ratio, offset);
            coordinates.Add(stage.OutputShaft.Id, value); ports.Add(stage.OutputPort.Id, value.Scale(stage.OutputPort.FrameInShaft.Z.Z, stage.OutputPort.ReadoutOffset.Value));
            shaft = stage.OutputShaft;
        }
    }
}
