using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class PitchChainEditOperation
{
    private protected PitchChainEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => PitchChainEditKeys.Operation(this);
}
public sealed class ApplyPitchChainSourceEditsEdit : PitchChainEditOperation
{ public ApplyPitchChainSourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); } public MechanicalEditBatch Batch { get; } }
public sealed class SetPitchChainSourceLengthMappingEdit : PitchChainEditOperation
{ public SetPitchChainSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; } public SourceLengthMapping? Mapping { get; } }
public sealed class SetPitchChainInputToothCountEdit : PitchChainEditOperation
{ public SetPitchChainInputToothCountEdit(string deviceId, int toothCount) { DeviceId = deviceId; ToothCount = toothCount; } public string DeviceId { get; } public int ToothCount { get; } }
public sealed class SetPitchChainOutputToothCountEdit : PitchChainEditOperation
{ public SetPitchChainOutputToothCountEdit(string deviceId, int toothCount) { DeviceId = deviceId; ToothCount = toothCount; } public string DeviceId { get; } public int ToothCount { get; } }
public sealed class SetPitchChainInputPitchEdit : PitchChainEditOperation
{ public SetPitchChainInputPitchEdit(string deviceId, ExactQuantity pitch) { DeviceId = deviceId; Pitch = pitch; } public string DeviceId { get; } public ExactQuantity Pitch { get; } }
public sealed class SetPitchChainOutputPitchEdit : PitchChainEditOperation
{ public SetPitchChainOutputPitchEdit(string deviceId, ExactQuantity pitch) { DeviceId = deviceId; Pitch = pitch; } public string DeviceId { get; } public ExactQuantity Pitch { get; } }
public sealed class SetPitchChainInputSprocketStationEdit : PitchChainEditOperation
{ public SetPitchChainInputSprocketStationEdit(string deviceId, ExactQuantity station) { DeviceId = deviceId; Station = station; } public string DeviceId { get; } public ExactQuantity Station { get; } }
public sealed class SetPitchChainInputBindingEdit : PitchChainEditOperation
{
    public SetPitchChainInputBindingEdit(string deviceId, string shaftId, string? portId = null) { DeviceId = deviceId; ShaftId = shaftId; PortId = portId; }
    public string DeviceId { get; } public string ShaftId { get; } public string? PortId { get; }
}
public sealed class SetPitchChainOutputSprocketStationEdit : PitchChainEditOperation
{ public SetPitchChainOutputSprocketStationEdit(string deviceId, ExactQuantity station) { DeviceId = deviceId; Station = station; } public string DeviceId { get; } public ExactQuantity Station { get; } }
/// <summary>Changes the positive shaft frame. A pure same-origin/zero-ray axis reversal re-expresses the signed mounting station, mounting phase and output reference; it does not move material. The terminal, route, chain and targets stay unchanged.</summary>
public sealed class SetPitchChainOutputShaftFrameEdit : PitchChainEditOperation
{ public SetPitchChainOutputShaftFrameEdit(string shaftId, OrientedFrame frame) { ShaftId = shaftId; Frame = frame; } public string ShaftId { get; } public OrientedFrame Frame { get; } }
/// <summary>Explicit world rigid transform of the output shaft, mounted sprocket and terminal only. Source/input/route normal/selected chain stay fixed.</summary>
public sealed class MovePitchChainOutputShaftGroupEdit : PitchChainEditOperation
{ public MovePitchChainOutputShaftGroupEdit(string shaftId, OrientedFrame worldRigidTransform) { ShaftId = shaftId; WorldRigidTransform = worldRigidTransform; } public string ShaftId { get; } public OrientedFrame WorldRigidTransform { get; } }
public sealed class SetPitchChainOutputTerminalEdit : PitchChainEditOperation
{ public SetPitchChainOutputTerminalEdit(string outputKey, OrientedFrame frame) { OutputKey = outputKey; Frame = frame; } public string OutputKey { get; } public OrientedFrame Frame { get; } }
public sealed class SetPitchChainRouteEdit : PitchChainEditOperation
{
    public SetPitchChainRouteEdit(string deviceId, ExactVector3 normal, string routingKind = PitchChainProfile.RoutingKind) { DeviceId = deviceId; Normal = normal; RoutingKind = routingKind; }
    public string DeviceId { get; } public ExactVector3 Normal { get; } public string RoutingKind { get; }
}
public sealed class SetPitchChainReferenceEdit : PitchChainEditOperation
{
    public SetPitchChainReferenceEdit(string deviceId, ExactQuantity inputTurns, ExactQuantity outputTurns) { DeviceId = deviceId; InputTurns = inputTurns; OutputTurns = outputTurns; }
    public string DeviceId { get; } public ExactQuantity InputTurns { get; } public ExactQuantity OutputTurns { get; }
}
public sealed class SetPitchChainMountingPhaseEdit : PitchChainEditOperation
{
    public SetPitchChainMountingPhaseEdit(string deviceId, ExactQuantity inputTurns, ExactQuantity outputTurns) { DeviceId = deviceId; InputTurns = inputTurns; OutputTurns = outputTurns; }
    public string DeviceId { get; } public ExactQuantity InputTurns { get; } public ExactQuantity OutputTurns { get; }
}
public sealed class SetPitchChainToothRegistrationEdit : PitchChainEditOperation
{ public SetPitchChainToothRegistrationEdit(string deviceId, BigInteger toothRegistration) { DeviceId = deviceId; ToothRegistration = toothRegistration; } public string DeviceId { get; } public BigInteger ToothRegistration { get; } }
public sealed class SetPitchChainTransmissionEdit : PitchChainEditOperation
{ public SetPitchChainTransmissionEdit(string deviceId, bool present) { DeviceId = deviceId; Present = present; } public string DeviceId { get; } public bool Present { get; } }
/// <summary>Explicitly selects/replaces/removes a chain specification. Tooth, pitch and placement edits never perform this operation implicitly.</summary>
public sealed class SetPitchChainSpecificationEdit : PitchChainEditOperation
{ public SetPitchChainSpecificationEdit(string deviceId, PitchChainSpecification? specification) { DeviceId = deviceId; Specification = specification; } public string DeviceId { get; } public PitchChainSpecification? Specification { get; } }
public sealed class SetPitchChainRequirementsEdit : PitchChainEditOperation
{
    public SetPitchChainRequirementsEdit(string outputKey, Rational? transfer, Rational? phase) { OutputKey = outputKey; Transfer = transfer; Phase = phase; }
    public string OutputKey { get; } public Rational? Transfer { get; } public Rational? Phase { get; }
}
public sealed class SetPitchChainRequiredValidationEdit : PitchChainEditOperation
{
    public SetPitchChainRequiredValidationEdit(IEnumerable<string> requiredDomains)
    { RequiredDomains = MechanicalAuthoringProfile.Set(requiredDomains, x => x, 24); }
    public ReadOnlyCollection<string> RequiredDomains { get; }
}
public sealed class SetPitchChainGroundingEdit : PitchChainEditOperation
{
    public SetPitchChainGroundingEdit(string deviceId, bool inputCenterFixed, bool inputAxisFixed, bool outputCenterFixed, bool outputAxisFixed)
    { DeviceId = deviceId; InputCenterFixed = inputCenterFixed; InputAxisFixed = inputAxisFixed; OutputCenterFixed = outputCenterFixed; OutputAxisFixed = outputAxisFixed; }
    public string DeviceId { get; } public bool InputCenterFixed { get; } public bool InputAxisFixed { get; } public bool OutputCenterFixed { get; } public bool OutputAxisFixed { get; }
}

public sealed class PitchChainEditBatch
{
    public PitchChainEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<PitchChainEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > PitchChainProfile.MaxTotalOperations) throw new ArgumentException("Open chain ordered operation bound exceeded.");
        foreach (var operation in Operations) PitchChainEditKeys.Validate(operation);
        RequestId = HashText(Pack("pitch-chain-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<PitchChainEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyPitchChainSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
    public string RequestId { get; }
}
public sealed class PitchChainEditChange
{
    internal PitchChainEditChange(int index, PitchChainEditOperation operation)
    { OperationIndex = index; OperationKind = operation.Kind; DependentMetrics = PitchChainEditKeys.Dependencies(operation).ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}
public sealed class PitchChainEditResult
{
    internal PitchChainEditResult(PitchChainDraft before, PitchChainEditBatch batch, PitchChainDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft;
        BaseDraftId = before.DraftId; RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex;
        Diagnostics = diagnostics.ToList().AsReadOnly();
        SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<PitchChainEditChange>() : batch.Operations.Select((o, i) => new PitchChainEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("pitch-chain-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public PitchChainDraft? Draft { get; }
    public string BaseDraftId { get; } public string RequestId { get; } public string ResultId { get; }
    public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<PitchChainEditChange> Changes { get; }
}

public static class PitchChainEditor
{
    public static PitchChainEditResult Apply(PitchChainDraft draft, PitchChainEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        PitchChainEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(draft, batch, null, index, diagnostics ?? new[] { new MechanicalDiagnostic(code, "PitchChainEdit", detail: detail) });
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId)
            return Reject("StaleRevision", null, "Both revision and definition must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null, "Revision bound exceeded.");
        var current = draft.Definition; var sourceResults = new List<MechanicalEditResult>();
        for (var i = 0; i < batch.Operations.Count; i++)
        {
            try
            {
                var op = batch.Operations[i]; var d = current.Device; var output = current.Output; var terminal = current.OutputTerminal;
                var source = current.Source; var mapping = current.SourceMapping; var phase = current.RequiredOutputPhase; var domains = current.RequiredValidationDomains;
                void Device(string id) { if (id != d.Id) throw new ArgumentException("UnknownDeviceId: " + id); }
                void Shaft(string id) { if (id != d.OutputShaft.Id) throw new ArgumentException("UnknownShaftId: " + id); }
                void Output(string key) { if (key != output.Key) throw new ArgumentException("UnknownOutputId: " + key); }
                switch (op)
                {
                    case ApplyPitchChainSourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source transaction rejected; no source/device/chain changes publish.", result.Diagnostics);
                        source = result.Draft; sourceResults.Add(result); break;
                    case SetPitchChainSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetPitchChainInputToothCountEdit e: Device(e.DeviceId); d = Copy(d, inputTeeth: e.ToothCount); break;
                    case SetPitchChainOutputToothCountEdit e: Device(e.DeviceId); d = Copy(d, outputTeeth: e.ToothCount); break;
                    case SetPitchChainInputPitchEdit e: Device(e.DeviceId); d = Copy(d, inputPitch: e.Pitch); break;
                    case SetPitchChainOutputPitchEdit e: Device(e.DeviceId); d = Copy(d, outputPitch: e.Pitch); break;
                    case SetPitchChainInputSprocketStationEdit e: Device(e.DeviceId); d = Copy(d, inputStation: e.Station); break;
                    case SetPitchChainInputBindingEdit e: Device(e.DeviceId); d = Copy(d, inputShaft: e.ShaftId, inputPort: e.PortId, replacePort: true); break;
                    case SetPitchChainOutputSprocketStationEdit e: Device(e.DeviceId); d = Copy(d, station: e.Station); break;
                    case SetPitchChainOutputShaftFrameEdit e:
                        Shaft(e.ShaftId);
                        var oldFrame = d.OutputShaft.Frame;
                        var coordinateReversal = oldFrame.IsProperCardinal && e.Frame.IsProperCardinal &&
                            e.Frame.Origin == oldFrame.Origin && e.Frame.X == oldFrame.X && e.Frame.Z == oldFrame.Z * -1;
                        // A coordinate sign change is not an automatic chain/placement repair:
                        // the signed station, reference and mounting declarations transform to preserve the same physical mounting and mark phase.
                        d = Copy(d, outputShaft: new OrientedShaft(d.OutputShaft.Id, e.Frame, d.OutputShaft.IsPrescribed),
                            station: coordinateReversal ? ExactQuantity.FromCanonical(d.OutputSprocketStation.Kind, -d.OutputSprocketStation.Value) : d.OutputSprocketStation,
                            outputReference: coordinateReversal ? ExactQuantity.FromCanonical(d.OutputReferenceTurns.Kind, -d.OutputReferenceTurns.Value) : d.OutputReferenceTurns,
                            outputMounting: coordinateReversal ? ExactQuantity.FromCanonical(d.OutputMountingPhase.Kind, -d.OutputMountingPhase.Value) : d.OutputMountingPhase); break;
                    case MovePitchChainOutputShaftGroupEdit e:
                        Shaft(e.ShaftId);
                        if (!e.WorldRigidTransform.IsProperCardinal) return Reject("UnsupportedRigidTransform", i, "An explicit proper cardinal world rigid transform is required.");
                        d = Copy(d, outputShaft: new OrientedShaft(d.OutputShaft.Id, e.WorldRigidTransform.Transform(d.OutputShaft.Frame), d.OutputShaft.IsPrescribed));
                        terminal = new ShaftPort(terminal.Id, terminal.ShaftId, e.WorldRigidTransform.Transform(terminal.Frame), terminal.PhaseOffset, terminal.Kind); break;
                    case SetPitchChainOutputTerminalEdit e:
                        Output(e.OutputKey); terminal = new ShaftPort(terminal.Id, terminal.ShaftId, e.Frame, terminal.PhaseOffset, terminal.Kind); break;
                    case SetPitchChainRouteEdit e: Device(e.DeviceId); d = Copy(d, normal: e.Normal, routing: e.RoutingKind); break;
                    case SetPitchChainReferenceEdit e: Device(e.DeviceId); d = Copy(d, inputReference: e.InputTurns, outputReference: e.OutputTurns); break;
                    case SetPitchChainMountingPhaseEdit e: Device(e.DeviceId); d = Copy(d, inputMounting: e.InputTurns, outputMounting: e.OutputTurns); break;
                    case SetPitchChainToothRegistrationEdit e: Device(e.DeviceId); d = Copy(d, registration: e.ToothRegistration); break;
                    case SetPitchChainTransmissionEdit e: Device(e.DeviceId); d = Copy(d, present: e.Present); break;
                    case SetPitchChainSpecificationEdit e: Device(e.DeviceId); d = Copy(d, chain: e.Specification, replaceChain: true); break;
                    case SetPitchChainRequirementsEdit e:
                        Output(e.OutputKey); output = new MechanicalOutput(output.Key, output.ShaftId, output.BodyId, output.PortId, e.Transfer, output.Role, output.UnresolvedReason, output.FormerEndpoint); phase = e.Phase; break;
                    case SetPitchChainRequiredValidationEdit e: domains = e.RequiredDomains; break;
                    case SetPitchChainGroundingEdit e:
                        Device(e.DeviceId); d = Copy(d, inputCenterFixed: e.InputCenterFixed, inputAxisFixed: e.InputAxisFixed, outputCenterFixed: e.OutputCenterFixed, outputAxisFixed: e.OutputAxisFixed); break;
                    default: throw new ArgumentException("Unsupported typed pitch chain edit operation.");
                }
                current = new PitchChainDefinition(source, mapping, d, terminal, output, phase, domains);
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, e.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sourceResults);
    }
    private static PitchChainTransmissionDefinition Copy(PitchChainTransmissionDefinition d, int? inputTeeth = null, int? outputTeeth = null,
        ExactQuantity? inputPitch = null, ExactQuantity? outputPitch = null, ExactQuantity? inputStation = null,
        string? inputShaft = null, string? inputPort = null, bool replacePort = false,
        OrientedShaft? outputShaft = null, ExactQuantity? station = null, ExactVector3? normal = null, string? routing = null,
        ExactQuantity? inputReference = null, ExactQuantity? outputReference = null, ExactQuantity? inputMounting = null, ExactQuantity? outputMounting = null,
        BigInteger? registration = null, bool? present = null, PitchChainSpecification? chain = null, bool replaceChain = false,
        bool? inputCenterFixed = null, bool? inputAxisFixed = null, bool? outputCenterFixed = null, bool? outputAxisFixed = null) =>
        new(d.Id, inputShaft ?? d.InputShaftId, d.InputSprocketBodyId, inputTeeth ?? d.InputToothCount, inputPitch ?? d.InputPitch, inputStation ?? d.InputSprocketStation,
            outputShaft ?? d.OutputShaft, d.OutputSprocketBodyId, outputTeeth ?? d.OutputToothCount, outputPitch ?? d.OutputPitch, station ?? d.OutputSprocketStation,
            normal ?? d.RouteNormal, inputMounting ?? d.InputMountingPhase, outputMounting ?? d.OutputMountingPhase,
            inputReference ?? d.InputReferenceTurns, outputReference ?? d.OutputReferenceTurns, registration ?? d.ToothRegistration,
            replaceChain ? chain : d.SelectedChain, present ?? d.TransmissionPresent, inputCenterFixed ?? d.InputCenterFixed,
            inputAxisFixed ?? d.InputAxisFixed, outputCenterFixed ?? d.OutputCenterFixed, outputAxisFixed ?? d.OutputAxisFixed,
            replacePort ? inputPort : d.InputPortId, routing ?? d.RoutingKind);
}

internal static class PitchChainEditKeys
{
    public static void Validate(PitchChainEditOperation o)
    {
        void Id(string id) => MechanicalAuthoringProfile.IdValue(id);
        switch (o)
        {
            case ApplyPitchChainSourceEditsEdit _: break;
            case SetPitchChainSourceLengthMappingEdit _: break;
            case SetPitchChainInputToothCountEdit e: Id(e.DeviceId); break;
            case SetPitchChainOutputToothCountEdit e: Id(e.DeviceId); break;
            case SetPitchChainInputPitchEdit e: Id(e.DeviceId); PitchChainProfile.Quantity(e.Pitch); break;
            case SetPitchChainOutputPitchEdit e: Id(e.DeviceId); PitchChainProfile.Quantity(e.Pitch); break;
            case SetPitchChainInputSprocketStationEdit e: Id(e.DeviceId); PitchChainProfile.Quantity(e.Station); break;
            case SetPitchChainInputBindingEdit e: Id(e.DeviceId); Id(e.ShaftId); if (e.PortId is not null) Id(e.PortId); break;
            case SetPitchChainOutputSprocketStationEdit e: Id(e.DeviceId); PitchChainProfile.Quantity(e.Station); break;
            case SetPitchChainOutputShaftFrameEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case MovePitchChainOutputShaftGroupEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.WorldRigidTransform); break;
            case SetPitchChainOutputTerminalEdit e: Id(e.OutputKey); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case SetPitchChainRouteEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.Normal); Id(e.RoutingKind); break;
            case SetPitchChainReferenceEdit e: Id(e.DeviceId); PitchChainProfile.Quantity(e.InputTurns); PitchChainProfile.Quantity(e.OutputTurns); break;
            case SetPitchChainMountingPhaseEdit e: Id(e.DeviceId); PitchChainProfile.Quantity(e.InputTurns); PitchChainProfile.Quantity(e.OutputTurns); break;
            case SetPitchChainToothRegistrationEdit e: Id(e.DeviceId); PitchChainProfile.Integer(e.ToothRegistration); break;
            case SetPitchChainTransmissionEdit e: Id(e.DeviceId); break;
            case SetPitchChainSpecificationEdit e: Id(e.DeviceId); break;
            case SetPitchChainRequirementsEdit e: Id(e.OutputKey); if (e.Transfer.HasValue) MechanicalAuthoringProfile.Number(e.Transfer.Value); if (e.Phase.HasValue) MechanicalAuthoringProfile.Number(e.Phase.Value); break;
            case SetPitchChainRequiredValidationEdit _: break;
            case SetPitchChainGroundingEdit e: Id(e.DeviceId); break;
            default: throw new ArgumentException("Unsupported typed pitch chain edit.");
        }
    }
    public static string Operation(PitchChainEditOperation o)
    {
        string value = o switch
        {
            ApplyPitchChainSourceEditsEdit e => e.Batch.RequestId,
            SetPitchChainSourceLengthMappingEdit e => e.Mapping?.CanonicalRepresentation ?? "",
            SetPitchChainInputToothCountEdit e => Pack(e.DeviceId, e.ToothCount.ToString(CultureInfo.InvariantCulture)),
            SetPitchChainOutputToothCountEdit e => Pack(e.DeviceId, e.ToothCount.ToString(CultureInfo.InvariantCulture)),
            SetPitchChainInputPitchEdit e => Pack(e.DeviceId, PitchChainProfile.Q(e.Pitch)),
            SetPitchChainOutputPitchEdit e => Pack(e.DeviceId, PitchChainProfile.Q(e.Pitch)),
            SetPitchChainInputSprocketStationEdit e => Pack(e.DeviceId, PitchChainProfile.Q(e.Station)),
            SetPitchChainInputBindingEdit e => Pack(e.DeviceId, e.ShaftId, e.PortId ?? ""),
            SetPitchChainOutputSprocketStationEdit e => Pack(e.DeviceId, PitchChainProfile.Q(e.Station)),
            SetPitchChainOutputShaftFrameEdit e => Pack(e.ShaftId, Frame(e.Frame)),
            MovePitchChainOutputShaftGroupEdit e => Pack(e.ShaftId, Frame(e.WorldRigidTransform)),
            SetPitchChainOutputTerminalEdit e => Pack(e.OutputKey, Frame(e.Frame)),
            SetPitchChainRouteEdit e => Pack(e.DeviceId, V(e.Normal), e.RoutingKind),
            SetPitchChainReferenceEdit e => Pack(e.DeviceId, PitchChainProfile.Q(e.InputTurns), PitchChainProfile.Q(e.OutputTurns)),
            SetPitchChainMountingPhaseEdit e => Pack(e.DeviceId, PitchChainProfile.Q(e.InputTurns), PitchChainProfile.Q(e.OutputTurns)),
            SetPitchChainToothRegistrationEdit e => Pack(e.DeviceId, PitchChainProfile.I(e.ToothRegistration)),
            SetPitchChainTransmissionEdit e => Pack(e.DeviceId, PitchChainProfile.Flag(e.Present)),
            SetPitchChainSpecificationEdit e => Pack(e.DeviceId, e.Specification?.CanonicalRepresentation ?? ""),
            SetPitchChainRequirementsEdit e => Pack(e.OutputKey, e.Transfer.HasValue ? F(e.Transfer.Value) : "", e.Phase.HasValue ? F(e.Phase.Value) : ""),
            SetPitchChainRequiredValidationEdit e => Pack(e.RequiredDomains.ToArray()),
            SetPitchChainGroundingEdit e => Pack(e.DeviceId, PitchChainProfile.Flag(e.InputCenterFixed), PitchChainProfile.Flag(e.InputAxisFixed), PitchChainProfile.Flag(e.OutputCenterFixed), PitchChainProfile.Flag(e.OutputAxisFixed)),
            _ => throw new ArgumentException("Unsupported typed pitch chain edit.")
        };
        return Pack(o.Kind, value);
    }
    public static IEnumerable<string> Dependencies(PitchChainEditOperation o) => o switch
    {
        SetPitchChainInputToothCountEdit _ or SetPitchChainOutputToothCountEdit _ or SetPitchChainInputPitchEdit _ or SetPitchChainOutputPitchEdit _ => new[] { "RegularPitchPolygonConstruction", "SelectedLinkCount", "SynchronizedShaftRelation", "WholeMotionDeterminacy", "OutputRequirements" },
        SetPitchChainSpecificationEdit _ => new[] { "SelectedLinkCount", "TransmissionAdmission", "WholeMotionDeterminacy", "OutputRequirements" },
        SetPitchChainOutputTerminalEdit _ => new[] { "TerminalLaw", "OutputRequirements" },
        SetPitchChainRequirementsEdit _ => new[] { "OutputRequirements" },
        SetPitchChainRequiredValidationEdit _ => new[] { "RequiredValidation", "ExportAdmission" },
        _ => new[] { "LocalCompatibility", "RegularPitchPolygonConstruction", "SelectedLinkCount", "Connectivity", "SynchronizedShaftRelation", "WholeMotionDeterminacy", "OutputRequirements", "ExportAdmission" }
    };
}
