using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class WormDriveEditOperation
{
    private protected WormDriveEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => WormDriveEditKeys.Operation(this);
}
public sealed class ApplyWormDriveSourceEditsEdit : WormDriveEditOperation
{
    public ApplyWormDriveSourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); }
    public MechanicalEditBatch Batch { get; }
}
public sealed class SetWormDriveSourceLengthMappingEdit : WormDriveEditOperation
{
    public SetWormDriveSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; }
    public SourceLengthMapping? Mapping { get; }
}
public sealed class SetWormDriveStartsEdit : WormDriveEditOperation
{
    public SetWormDriveStartsEdit(string deviceId, int starts) { DeviceId = deviceId; Starts = starts; }
    public string DeviceId { get; } public int Starts { get; }
}
public sealed class SetWormDriveAxialModuleEdit : WormDriveEditOperation
{
    public SetWormDriveAxialModuleEdit(string deviceId, ExactQuantity module) { DeviceId = deviceId; Module = module; }
    public string DeviceId { get; } public ExactQuantity Module { get; }
}
public sealed class SetWormDrivePitchRadiusEdit : WormDriveEditOperation
{
    public SetWormDrivePitchRadiusEdit(string deviceId, ExactQuantity radius) { DeviceId = deviceId; Radius = radius; }
    public string DeviceId { get; } public ExactQuantity Radius { get; }
}
public sealed class SetWormDriveHandednessEdit : WormDriveEditOperation
{
    public SetWormDriveHandednessEdit(string deviceId, int handedness) { DeviceId = deviceId; Handedness = handedness; }
    public string DeviceId { get; } public int Handedness { get; }
}
public sealed class SetWormDriveWormSpecificationEdit : WormDriveEditOperation
{
    public SetWormDriveWormSpecificationEdit(string deviceId, CylindricalWormSpecification specification) { DeviceId = deviceId; Specification = specification ?? throw new ArgumentNullException(nameof(specification)); }
    public string DeviceId { get; } public CylindricalWormSpecification Specification { get; }
}
public sealed class SetWormDriveWheelSpecificationEdit : WormDriveEditOperation
{
    public SetWormDriveWheelSpecificationEdit(string deviceId, IdealWormWheelSpecification? specification) { DeviceId = deviceId; Specification = specification; }
    public string DeviceId { get; } public IdealWormWheelSpecification? Specification { get; }
}
public sealed class SetWormDriveWheelToothCountEdit : WormDriveEditOperation
{
    public SetWormDriveWheelToothCountEdit(string deviceId, int toothCount) { DeviceId = deviceId; ToothCount = toothCount; }
    public string DeviceId { get; } public int ToothCount { get; }
}
public sealed class SetWormDriveWheelModuleEdit : WormDriveEditOperation
{
    public SetWormDriveWheelModuleEdit(string deviceId, ExactQuantity module) { DeviceId = deviceId; Module = module; }
    public string DeviceId { get; } public ExactQuantity Module { get; }
}
public sealed class SetWormDriveWheelHandednessEdit : WormDriveEditOperation
{
    public SetWormDriveWheelHandednessEdit(string deviceId, int handedness) { DeviceId = deviceId; Handedness = handedness; }
    public string DeviceId { get; } public int Handedness { get; }
}
public sealed class SetWormDriveWheelTraceSlopeEdit : WormDriveEditOperation
{
    public SetWormDriveWheelTraceSlopeEdit(string deviceId, Rational traceSlope) { DeviceId = deviceId; TraceSlope = traceSlope; }
    public string DeviceId { get; } public Rational TraceSlope { get; }
}
public sealed class SetWormDriveInputPitchStationEdit : WormDriveEditOperation
{
    public SetWormDriveInputPitchStationEdit(string deviceId, ExactQuantity station) { DeviceId = deviceId; Station = station; }
    public string DeviceId { get; } public ExactQuantity Station { get; }
}
public sealed class SetWormDriveInputBindingEdit : WormDriveEditOperation
{
    public SetWormDriveInputBindingEdit(string deviceId, string shaftId, string? portId = null) { DeviceId = deviceId; ShaftId = shaftId; PortId = portId; }
    public string DeviceId { get; } public string ShaftId { get; } public string? PortId { get; }
}
public sealed class SetWormDriveOutputPitchStationEdit : WormDriveEditOperation
{
    public SetWormDriveOutputPitchStationEdit(string deviceId, ExactQuantity station) { DeviceId = deviceId; Station = station; }
    public string DeviceId { get; } public ExactQuantity Station { get; }
}
public sealed class SetWormDrivePhysicalHelixAxisEdit : WormDriveEditOperation
{
    public SetWormDrivePhysicalHelixAxisEdit(string deviceId, ExactVector3 axis) { DeviceId = deviceId; Axis = axis; }
    public string DeviceId { get; } public ExactVector3 Axis { get; }
}
public sealed class SetWormDriveContactSideEdit : WormDriveEditOperation
{
    public SetWormDriveContactSideEdit(string deviceId, ExactVector3 contactSide) { DeviceId = deviceId; ContactSide = contactSide; }
    public string DeviceId { get; } public ExactVector3 ContactSide { get; }
}
public sealed class SetWormDriveOutputShaftFrameEdit : WormDriveEditOperation
{
    public SetWormDriveOutputShaftFrameEdit(string shaftId, OrientedFrame frame) { ShaftId = shaftId; Frame = frame; }
    public string ShaftId { get; } public OrientedFrame Frame { get; }
}
public sealed class MoveWormDriveOutputShaftGroupEdit : WormDriveEditOperation
{
    public MoveWormDriveOutputShaftGroupEdit(string shaftId, OrientedFrame worldRigidTransform) { ShaftId = shaftId; WorldRigidTransform = worldRigidTransform; }
    public string ShaftId { get; } public OrientedFrame WorldRigidTransform { get; }
}
public sealed class SetWormDriveOutputTerminalEdit : WormDriveEditOperation
{
    public SetWormDriveOutputTerminalEdit(string outputKey, OrientedFrame frame, Rational? phaseOffset = null) { OutputKey = outputKey; Frame = frame; PhaseOffset = phaseOffset; }
    public string OutputKey { get; } public OrientedFrame Frame { get; } public Rational? PhaseOffset { get; }
}
public sealed class SetWormDriveReferenceEdit : WormDriveEditOperation
{
    public SetWormDriveReferenceEdit(string deviceId, ExactQuantity inputTurns, ExactQuantity outputTurns) { DeviceId = deviceId; InputTurns = inputTurns; OutputTurns = outputTurns; }
    public string DeviceId { get; } public ExactQuantity InputTurns { get; } public ExactQuantity OutputTurns { get; }
}
public sealed class SetWormDriveMountingPhaseEdit : WormDriveEditOperation
{
    public SetWormDriveMountingPhaseEdit(string deviceId, ExactQuantity inputTurns, ExactQuantity outputTurns) { DeviceId = deviceId; InputTurns = inputTurns; OutputTurns = outputTurns; }
    public string DeviceId { get; } public ExactQuantity InputTurns { get; } public ExactQuantity OutputTurns { get; }
}
public sealed class SetWormDriveTransmissionEdit : WormDriveEditOperation
{
    public SetWormDriveTransmissionEdit(string deviceId, bool present) { DeviceId = deviceId; Present = present; }
    public string DeviceId { get; } public bool Present { get; }
}
public sealed class SetWormDriveRequirementsEdit : WormDriveEditOperation
{
    public SetWormDriveRequirementsEdit(string outputKey, Rational? transfer, Rational? phase) { OutputKey = outputKey; Transfer = transfer; Phase = phase; }
    public string OutputKey { get; } public Rational? Transfer { get; } public Rational? Phase { get; }
}
public sealed class SetWormDriveRequiredValidationEdit : WormDriveEditOperation
{
    public SetWormDriveRequiredValidationEdit(IEnumerable<string> requiredDomains) { RequiredDomains = MechanicalAuthoringProfile.Set(requiredDomains, x => x, 24); }
    public ReadOnlyCollection<string> RequiredDomains { get; }
}
public sealed class SetWormDriveGroundingEdit : WormDriveEditOperation
{
    public SetWormDriveGroundingEdit(string deviceId, bool inputCenterFixed, bool inputAxisFixed, bool outputCenterFixed, bool outputAxisFixed) { DeviceId = deviceId; InputCenterFixed = inputCenterFixed; InputAxisFixed = inputAxisFixed; OutputCenterFixed = outputCenterFixed; OutputAxisFixed = outputAxisFixed; }
    public string DeviceId { get; } public bool InputCenterFixed { get; } public bool InputAxisFixed { get; } public bool OutputCenterFixed { get; } public bool OutputAxisFixed { get; }
}
public sealed class SetWormDriveProfileEdit : WormDriveEditOperation
{
    public SetWormDriveProfileEdit(string deviceId, string profile) { DeviceId = deviceId; Profile = profile; }
    public string DeviceId { get; } public string Profile { get; }
}

public sealed class WormDriveEditBatch
{
    public WormDriveEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<WormDriveEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > WormDriveProfile.MaxTotalOperations) throw new ArgumentException("Worm drive ordered operation bound exceeded.");
        foreach (var operation in Operations) WormDriveEditKeys.Validate(operation);
        RequestId = HashText(Pack("worm-drive-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<WormDriveEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyWormDriveSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
    public string RequestId { get; }
}
public sealed class WormDriveEditChange
{
    internal WormDriveEditChange(int index, WormDriveEditOperation operation)
    { OperationIndex = index; OperationKind = operation.Kind; DependentMetrics = WormDriveEditKeys.Dependencies(operation).ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}
public sealed class WormDriveEditResult
{
    internal WormDriveEditResult(WormDriveDraft before, WormDriveEditBatch batch, WormDriveDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft;
        BaseDraftId = before.DraftId; RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex;
        Diagnostics = diagnostics.ToList().AsReadOnly();
        SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<WormDriveEditChange>() : batch.Operations.Select((o, i) => new WormDriveEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("worm-drive-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public WormDriveDraft? Draft { get; }
    public string BaseDraftId { get; } public string RequestId { get; } public string ResultId { get; }
    public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<WormDriveEditChange> Changes { get; }
}

public static class WormDriveEditor
{
    public static WormDriveEditResult Apply(WormDriveDraft draft, WormDriveEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        WormDriveEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(draft, batch, null, index, diagnostics ?? new[] { new MechanicalDiagnostic(code, "WormDriveEdit", detail: detail) });
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId)
            return Reject("StaleRevision", null, "Both revision and definition must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null, "Revision bound exceeded.");
        var current = draft.Definition; var sourceResults = new List<MechanicalEditResult>();
        for (var i = 0; i < batch.Operations.Count; i++)
        {
            try
            {
                var d = current.Device; var worm = d.Worm; var wheel = d.SelectedWheel;
                var output = current.Output; var terminal = current.OutputTerminal; var source = current.Source;
                var mapping = current.SourceMapping; var phase = current.RequiredOutputPhase; var domains = current.RequiredValidationDomains;
                void Device(string id) { if (id != d.Id) throw new ArgumentException("UnknownDeviceId: " + id); }
                void Shaft(string id) { if (id != d.OutputShaft.Id) throw new ArgumentException("UnknownShaftId: " + id); }
                void Output(string id) { if (id != output.Key) throw new ArgumentException("UnknownOutputId: " + id); }
                IdealWormWheelSpecification Wheel() => wheel ?? throw new ArgumentException("MissingSelectedWheel: explicitly select an independent wheel before editing it.");
                switch (batch.Operations[i])
                {
                    case ApplyWormDriveSourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source transaction rejected; no source or worm changes publish.", result.Diagnostics);
                        source = result.Draft; sourceResults.Add(result); break;
                    case SetWormDriveSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetWormDriveStartsEdit e: Device(e.DeviceId); d = Copy(d, worm: CopyWorm(worm, starts: e.Starts)); break;
                    case SetWormDriveAxialModuleEdit e: Device(e.DeviceId); d = Copy(d, worm: CopyWorm(worm, module: e.Module)); break;
                    case SetWormDrivePitchRadiusEdit e: Device(e.DeviceId); d = Copy(d, worm: CopyWorm(worm, radius: e.Radius)); break;
                    case SetWormDriveHandednessEdit e: Device(e.DeviceId); d = Copy(d, worm: CopyWorm(worm, hand: e.Handedness)); break;
                    case SetWormDriveWormSpecificationEdit e: Device(e.DeviceId); d = Copy(d, worm: e.Specification); break;
                    case SetWormDriveWheelSpecificationEdit e: Device(e.DeviceId); d = Copy(d, wheel: e.Specification, replaceWheel: true); break;
                    case SetWormDriveWheelToothCountEdit e: Device(e.DeviceId); d = Copy(d, wheel: CopyWheel(Wheel(), teeth: e.ToothCount), replaceWheel: true); break;
                    case SetWormDriveWheelModuleEdit e: Device(e.DeviceId); d = Copy(d, wheel: CopyWheel(Wheel(), module: e.Module), replaceWheel: true); break;
                    case SetWormDriveWheelHandednessEdit e: Device(e.DeviceId); d = Copy(d, wheel: CopyWheel(Wheel(), hand: e.Handedness), replaceWheel: true); break;
                    case SetWormDriveWheelTraceSlopeEdit e: Device(e.DeviceId); d = Copy(d, wheel: CopyWheel(Wheel(), slope: e.TraceSlope), replaceWheel: true); break;
                    case SetWormDriveInputPitchStationEdit e: Device(e.DeviceId); d = Copy(d, inputStation: e.Station); break;
                    case SetWormDriveInputBindingEdit e: Device(e.DeviceId); d = Copy(d, inputShaft: e.ShaftId, inputPort: e.PortId, replacePort: true); break;
                    case SetWormDriveOutputPitchStationEdit e: Device(e.DeviceId); d = Copy(d, outputStation: e.Station); break;
                    case SetWormDrivePhysicalHelixAxisEdit e: Device(e.DeviceId); d = Copy(d, physicalAxis: e.Axis); break;
                    case SetWormDriveContactSideEdit e: Device(e.DeviceId); d = Copy(d, contactSide: e.ContactSide); break;
                    case SetWormDriveOutputShaftFrameEdit e:
                        Shaft(e.ShaftId);
                        var oldFrame = d.OutputShaft.Frame;
                        var reversal = oldFrame.IsProperCardinal && e.Frame.IsProperCardinal &&
                            e.Frame.Origin == oldFrame.Origin && e.Frame.X == oldFrame.X && e.Frame.Z == oldFrame.Z * -1;
                        d = Copy(d, outputShaft: new OrientedShaft(d.OutputShaft.Id, e.Frame, d.OutputShaft.IsPrescribed),
                            outputStation: reversal ? Negate(d.OutputPitchStation) : d.OutputPitchStation,
                            outputReference: reversal ? Negate(d.OutputReferenceTurns) : d.OutputReferenceTurns,
                            outputMounting: reversal ? Negate(d.OutputMountingPhase) : d.OutputMountingPhase);
                        break;
                    case MoveWormDriveOutputShaftGroupEdit e:
                        Shaft(e.ShaftId);
                        if (!e.WorldRigidTransform.IsProperCardinal) return Reject("UnsupportedRigidTransform", i, "An explicit proper cardinal world rigid transform is required.");
                        d = Copy(d, outputShaft: new OrientedShaft(d.OutputShaft.Id, e.WorldRigidTransform.Transform(d.OutputShaft.Frame), d.OutputShaft.IsPrescribed));
                        terminal = new ShaftPort(terminal.Id, terminal.ShaftId, e.WorldRigidTransform.Transform(terminal.Frame), terminal.PhaseOffset, terminal.Kind); break;
                    case SetWormDriveOutputTerminalEdit e:
                        Output(e.OutputKey); terminal = new ShaftPort(terminal.Id, terminal.ShaftId, e.Frame, e.PhaseOffset ?? terminal.PhaseOffset, terminal.Kind); break;
                    case SetWormDriveReferenceEdit e: Device(e.DeviceId); d = Copy(d, inputReference: e.InputTurns, outputReference: e.OutputTurns); break;
                    case SetWormDriveMountingPhaseEdit e: Device(e.DeviceId); d = Copy(d, inputMounting: e.InputTurns, outputMounting: e.OutputTurns); break;
                    case SetWormDriveTransmissionEdit e: Device(e.DeviceId); d = Copy(d, present: e.Present); break;
                    case SetWormDriveRequirementsEdit e:
                        Output(e.OutputKey); output = new MechanicalOutput(output.Key, output.ShaftId, output.BodyId, output.PortId, e.Transfer, output.Role, output.UnresolvedReason, output.FormerEndpoint); phase = e.Phase; break;
                    case SetWormDriveRequiredValidationEdit e: domains = e.RequiredDomains; break;
                    case SetWormDriveGroundingEdit e:
                        Device(e.DeviceId); d = Copy(d, inputCenterFixed: e.InputCenterFixed, inputAxisFixed: e.InputAxisFixed, outputCenterFixed: e.OutputCenterFixed, outputAxisFixed: e.OutputAxisFixed); break;
                    case SetWormDriveProfileEdit e: Device(e.DeviceId); d = Copy(d, profile: e.Profile); break;
                    default: throw new ArgumentException("Unsupported typed worm drive edit operation.");
                }
                current = new WormDriveDefinition(source, mapping, d, terminal, output, phase, domains);
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, e.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sourceResults);
    }
    private static ExactQuantity Negate(ExactQuantity q) => ExactQuantity.FromCanonical(q.Kind, -q.Value);
    private static CylindricalWormSpecification CopyWorm(CylindricalWormSpecification w, int? starts = null, ExactQuantity? module = null, ExactQuantity? radius = null, int? hand = null) =>
        new(starts ?? w.Starts, module ?? w.AxialModule, radius ?? w.PitchRadius, hand ?? w.Handedness, w.Parameterization, w.GeometryKind);
    private static IdealWormWheelSpecification CopyWheel(IdealWormWheelSpecification w, int? teeth = null, ExactQuantity? module = null, int? hand = null, Rational? slope = null) =>
        new(teeth ?? w.ToothCount, module ?? w.TransverseModule, hand ?? w.Handedness, slope ?? w.TraceSlope, w.Parameterization, w.TraceSemantics);
    private static WormDriveTransmissionDefinition Copy(WormDriveTransmissionDefinition d, CylindricalWormSpecification? worm = null,
        IdealWormWheelSpecification? wheel = null, bool replaceWheel = false, ExactVector3? physicalAxis = null, ExactVector3? contactSide = null,
        ExactQuantity? inputStation = null, ExactQuantity? outputStation = null, string? inputShaft = null, string? inputPort = null, bool replacePort = false,
        OrientedShaft? outputShaft = null, ExactQuantity? inputReference = null, ExactQuantity? outputReference = null,
        ExactQuantity? inputMounting = null, ExactQuantity? outputMounting = null, bool? present = null,
        bool? inputCenterFixed = null, bool? inputAxisFixed = null, bool? outputCenterFixed = null, bool? outputAxisFixed = null, string? profile = null) =>
        new(d.Id, inputShaft ?? d.InputShaftId, d.InputWormBodyId, worm ?? d.Worm, physicalAxis ?? d.PhysicalHelixAxis, inputStation ?? d.InputPitchStation,
            outputShaft ?? d.OutputShaft, d.OutputWheelBodyId, replaceWheel ? wheel : d.SelectedWheel, outputStation ?? d.OutputPitchStation,
            contactSide ?? d.ContactSide, inputMounting ?? d.InputMountingPhase, outputMounting ?? d.OutputMountingPhase,
            inputReference ?? d.InputReferenceTurns, outputReference ?? d.OutputReferenceTurns, present ?? d.TransmissionPresent,
            inputCenterFixed ?? d.InputCenterFixed, inputAxisFixed ?? d.InputAxisFixed, outputCenterFixed ?? d.OutputCenterFixed, outputAxisFixed ?? d.OutputAxisFixed,
            replacePort ? inputPort : d.InputPortId, profile ?? d.Profile);
}

internal static class WormDriveEditKeys
{
    public static void Validate(WormDriveEditOperation o)
    {
        void Id(string id) => MechanicalAuthoringProfile.IdValue(id);
        switch (o)
        {
            case ApplyWormDriveSourceEditsEdit e:  break;
            case SetWormDriveSourceLengthMappingEdit e:  break;
            case SetWormDriveStartsEdit e: Id(e.DeviceId); break;
            case SetWormDriveAxialModuleEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.Module); break;
            case SetWormDrivePitchRadiusEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.Radius); break;
            case SetWormDriveHandednessEdit e: Id(e.DeviceId); break;
            case SetWormDriveWormSpecificationEdit e: Id(e.DeviceId); break;
            case SetWormDriveWheelSpecificationEdit e: Id(e.DeviceId); break;
            case SetWormDriveWheelToothCountEdit e: Id(e.DeviceId); break;
            case SetWormDriveWheelModuleEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.Module); break;
            case SetWormDriveWheelHandednessEdit e: Id(e.DeviceId); break;
            case SetWormDriveWheelTraceSlopeEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.Number(e.TraceSlope); break;
            case SetWormDriveInputPitchStationEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.Station); break;
            case SetWormDriveInputBindingEdit e: Id(e.DeviceId); Id(e.ShaftId); if (e.PortId is not null) Id(e.PortId); break;
            case SetWormDriveOutputPitchStationEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.Station); break;
            case SetWormDrivePhysicalHelixAxisEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.Axis); break;
            case SetWormDriveContactSideEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.ContactSide); break;
            case SetWormDriveOutputShaftFrameEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case MoveWormDriveOutputShaftGroupEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.WorldRigidTransform); break;
            case SetWormDriveOutputTerminalEdit e: Id(e.OutputKey); MechanicalAuthoringProfile.FrameBound(e.Frame); if (e.PhaseOffset.HasValue) MechanicalAuthoringProfile.Number(e.PhaseOffset.Value); break;
            case SetWormDriveReferenceEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.InputTurns); WormDriveProfile.Quantity(e.OutputTurns); break;
            case SetWormDriveMountingPhaseEdit e: Id(e.DeviceId); WormDriveProfile.Quantity(e.InputTurns); WormDriveProfile.Quantity(e.OutputTurns); break;
            case SetWormDriveTransmissionEdit e: Id(e.DeviceId); break;
            case SetWormDriveRequirementsEdit e: Id(e.OutputKey); if (e.Transfer.HasValue) MechanicalAuthoringProfile.Number(e.Transfer.Value); if (e.Phase.HasValue) MechanicalAuthoringProfile.Number(e.Phase.Value); break;
            case SetWormDriveRequiredValidationEdit e:  break;
            case SetWormDriveGroundingEdit e: Id(e.DeviceId); break;
            case SetWormDriveProfileEdit e: Id(e.DeviceId); Id(e.Profile); break;
            default: throw new ArgumentException("Unsupported typed worm drive edit.");
        }
    }
    public static string Operation(WormDriveEditOperation o)
    {
        string value = o switch
        {
            ApplyWormDriveSourceEditsEdit e => Pack(e.Batch.RequestId),
            SetWormDriveSourceLengthMappingEdit e => Pack(e.Mapping?.CanonicalRepresentation ?? ""),
            SetWormDriveStartsEdit e => Pack(e.DeviceId, e.Starts.ToString(CultureInfo.InvariantCulture)),
            SetWormDriveAxialModuleEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.Module)),
            SetWormDrivePitchRadiusEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.Radius)),
            SetWormDriveHandednessEdit e => Pack(e.DeviceId, e.Handedness.ToString(CultureInfo.InvariantCulture)),
            SetWormDriveWormSpecificationEdit e => Pack(e.DeviceId, e.Specification.CanonicalRepresentation),
            SetWormDriveWheelSpecificationEdit e => Pack(e.DeviceId, e.Specification?.CanonicalRepresentation ?? ""),
            SetWormDriveWheelToothCountEdit e => Pack(e.DeviceId, e.ToothCount.ToString(CultureInfo.InvariantCulture)),
            SetWormDriveWheelModuleEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.Module)),
            SetWormDriveWheelHandednessEdit e => Pack(e.DeviceId, e.Handedness.ToString(CultureInfo.InvariantCulture)),
            SetWormDriveWheelTraceSlopeEdit e => Pack(e.DeviceId, F(e.TraceSlope)),
            SetWormDriveInputPitchStationEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.Station)),
            SetWormDriveInputBindingEdit e => Pack(e.DeviceId, e.ShaftId, e.PortId ?? ""),
            SetWormDriveOutputPitchStationEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.Station)),
            SetWormDrivePhysicalHelixAxisEdit e => Pack(e.DeviceId, V(e.Axis)),
            SetWormDriveContactSideEdit e => Pack(e.DeviceId, V(e.ContactSide)),
            SetWormDriveOutputShaftFrameEdit e => Pack(e.ShaftId, Frame(e.Frame)),
            MoveWormDriveOutputShaftGroupEdit e => Pack(e.ShaftId, Frame(e.WorldRigidTransform)),
            SetWormDriveOutputTerminalEdit e => Pack(e.OutputKey, Frame(e.Frame), e.PhaseOffset.HasValue ? F(e.PhaseOffset.Value) : ""),
            SetWormDriveReferenceEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.InputTurns), WormDriveProfile.Q(e.OutputTurns)),
            SetWormDriveMountingPhaseEdit e => Pack(e.DeviceId, WormDriveProfile.Q(e.InputTurns), WormDriveProfile.Q(e.OutputTurns)),
            SetWormDriveTransmissionEdit e => Pack(e.DeviceId, WormDriveProfile.Flag(e.Present)),
            SetWormDriveRequirementsEdit e => Pack(e.OutputKey, e.Transfer.HasValue ? F(e.Transfer.Value) : "", e.Phase.HasValue ? F(e.Phase.Value) : ""),
            SetWormDriveRequiredValidationEdit e => Pack(Pack(e.RequiredDomains.ToArray())),
            SetWormDriveGroundingEdit e => Pack(e.DeviceId, WormDriveProfile.Flag(e.InputCenterFixed), WormDriveProfile.Flag(e.InputAxisFixed), WormDriveProfile.Flag(e.OutputCenterFixed), WormDriveProfile.Flag(e.OutputAxisFixed)),
            SetWormDriveProfileEdit e => Pack(e.DeviceId, e.Profile),
            _ => throw new ArgumentException("Unsupported typed worm drive edit.")
        };
        return Pack(o.Kind, value);
    }
    public static IEnumerable<string> Dependencies(WormDriveEditOperation o) => o switch
    {
        SetWormDriveOutputTerminalEdit _ => new[] { "TerminalLaw", "OutputRequirements" },
        SetWormDriveRequirementsEdit _ => new[] { "OutputRequirements" },
        SetWormDriveRequiredValidationEdit _ => new[] { "RequiredValidation", "ExportAdmission" },
        _ => new[] { "WormPitchAndLead", "WheelSpecification", "LocalPitchTraceCompatibility", "CommonPitchStation", "Connectivity", "IdealHelicalPhaseConstraint", "WholeMotionDeterminacy", "OutputRequirements", "ExportAdmission" }
    };
}

