using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class OpenBeltEditOperation
{
    private protected OpenBeltEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => OpenBeltEditKeys.Operation(this);
}
public sealed class ApplyOpenBeltSourceEditsEdit : OpenBeltEditOperation
{ public ApplyOpenBeltSourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); } public MechanicalEditBatch Batch { get; } }
public sealed class SetOpenBeltSourceLengthMappingEdit : OpenBeltEditOperation
{ public SetOpenBeltSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; } public SourceLengthMapping? Mapping { get; } }
public sealed class SetOpenBeltInputPitchRadiusEdit : OpenBeltEditOperation
{ public SetOpenBeltInputPitchRadiusEdit(string deviceId, ExactQuantity radius) { DeviceId = deviceId; Radius = radius; } public string DeviceId { get; } public ExactQuantity Radius { get; } }
public sealed class SetOpenBeltOutputPitchRadiusEdit : OpenBeltEditOperation
{ public SetOpenBeltOutputPitchRadiusEdit(string deviceId, ExactQuantity radius) { DeviceId = deviceId; Radius = radius; } public string DeviceId { get; } public ExactQuantity Radius { get; } }
public sealed class SetOpenBeltInputPulleyCenterEdit : OpenBeltEditOperation
{ public SetOpenBeltInputPulleyCenterEdit(string deviceId, ExactVector3 centerMm) { DeviceId = deviceId; CenterMm = centerMm; } public string DeviceId { get; } public ExactVector3 CenterMm { get; } }
public sealed class SetOpenBeltInputBindingEdit : OpenBeltEditOperation
{
    public SetOpenBeltInputBindingEdit(string deviceId, string shaftId, string? portId = null) { DeviceId = deviceId; ShaftId = shaftId; PortId = portId; }
    public string DeviceId { get; } public string ShaftId { get; } public string? PortId { get; }
}
public sealed class SetOpenBeltOutputPulleyStationEdit : OpenBeltEditOperation
{ public SetOpenBeltOutputPulleyStationEdit(string deviceId, ExactQuantity station) { DeviceId = deviceId; Station = station; } public string DeviceId { get; } public ExactQuantity Station { get; } }
/// <summary>Changes the positive shaft frame. A pure same-origin/zero-ray axis reversal re-expresses the signed mounting station and output reference; it does not move material. The terminal, route, belt and targets stay unchanged.</summary>
public sealed class SetOpenBeltOutputShaftFrameEdit : OpenBeltEditOperation
{ public SetOpenBeltOutputShaftFrameEdit(string shaftId, OrientedFrame frame) { ShaftId = shaftId; Frame = frame; } public string ShaftId { get; } public OrientedFrame Frame { get; } }
/// <summary>Explicit world rigid transform of the output shaft, mounted pulley and terminal only. Source/input/route normal/selected belt stay fixed.</summary>
public sealed class MoveOpenBeltOutputShaftGroupEdit : OpenBeltEditOperation
{ public MoveOpenBeltOutputShaftGroupEdit(string shaftId, OrientedFrame worldRigidTransform) { ShaftId = shaftId; WorldRigidTransform = worldRigidTransform; } public string ShaftId { get; } public OrientedFrame WorldRigidTransform { get; } }
public sealed class SetOpenBeltOutputTerminalEdit : OpenBeltEditOperation
{ public SetOpenBeltOutputTerminalEdit(string outputKey, OrientedFrame frame) { OutputKey = outputKey; Frame = frame; } public string OutputKey { get; } public OrientedFrame Frame { get; } }
public sealed class SetOpenBeltRouteEdit : OpenBeltEditOperation
{
    public SetOpenBeltRouteEdit(string deviceId, ExactVector3 normal, string routingKind = OpenBeltProfile.RoutingKind) { DeviceId = deviceId; Normal = normal; RoutingKind = routingKind; }
    public string DeviceId { get; } public ExactVector3 Normal { get; } public string RoutingKind { get; }
}
public sealed class SetOpenBeltReferenceEdit : OpenBeltEditOperation
{
    public SetOpenBeltReferenceEdit(string deviceId, ExactQuantity inputTurns, ExactQuantity outputTurns) { DeviceId = deviceId; InputTurns = inputTurns; OutputTurns = outputTurns; }
    public string DeviceId { get; } public ExactQuantity InputTurns { get; } public ExactQuantity OutputTurns { get; }
}
public sealed class SetOpenBeltTransmissionEdit : OpenBeltEditOperation
{ public SetOpenBeltTransmissionEdit(string deviceId, bool present) { DeviceId = deviceId; Present = present; } public string DeviceId { get; } public bool Present { get; } }
/// <summary>Explicitly selects/replaces/removes a belt specification. Radius and placement edits never perform this operation implicitly.</summary>
public sealed class SetOpenBeltLengthSpecificationEdit : OpenBeltEditOperation
{ public SetOpenBeltLengthSpecificationEdit(string deviceId, OpenBeltLengthSpecification? specification) { DeviceId = deviceId; Specification = specification; } public string DeviceId { get; } public OpenBeltLengthSpecification? Specification { get; } }
public sealed class SetOpenBeltRequirementsEdit : OpenBeltEditOperation
{
    public SetOpenBeltRequirementsEdit(string outputKey, Rational? transfer, Rational? phase) { OutputKey = outputKey; Transfer = transfer; Phase = phase; }
    public string OutputKey { get; } public Rational? Transfer { get; } public Rational? Phase { get; }
}
public sealed class SetOpenBeltRequiredValidationEdit : OpenBeltEditOperation
{
    public SetOpenBeltRequiredValidationEdit(IEnumerable<string> requiredDomains)
    { RequiredDomains = MechanicalAuthoringProfile.Set(requiredDomains, x => x, 24); }
    public ReadOnlyCollection<string> RequiredDomains { get; }
}
public sealed class SetOpenBeltGroundingEdit : OpenBeltEditOperation
{
    public SetOpenBeltGroundingEdit(string deviceId, bool inputCenterFixed, bool inputAxisFixed, bool outputCenterFixed, bool outputAxisFixed)
    { DeviceId = deviceId; InputCenterFixed = inputCenterFixed; InputAxisFixed = inputAxisFixed; OutputCenterFixed = outputCenterFixed; OutputAxisFixed = outputAxisFixed; }
    public string DeviceId { get; } public bool InputCenterFixed { get; } public bool InputAxisFixed { get; } public bool OutputCenterFixed { get; } public bool OutputAxisFixed { get; }
}

public sealed class OpenBeltEditBatch
{
    public OpenBeltEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<OpenBeltEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > OpenBeltProfile.MaxTotalOperations) throw new ArgumentException("Open belt ordered operation bound exceeded.");
        foreach (var operation in Operations) OpenBeltEditKeys.Validate(operation);
        RequestId = HashText(Pack("open-belt-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<OpenBeltEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyOpenBeltSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
    public string RequestId { get; }
}
public sealed class OpenBeltEditChange
{
    internal OpenBeltEditChange(int index, OpenBeltEditOperation operation)
    { OperationIndex = index; OperationKind = operation.Kind; DependentMetrics = OpenBeltEditKeys.Dependencies(operation).ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}
public sealed class OpenBeltEditResult
{
    internal OpenBeltEditResult(OpenBeltDraft before, OpenBeltEditBatch batch, OpenBeltDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft;
        BaseDraftId = before.DraftId; RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex;
        Diagnostics = diagnostics.ToList().AsReadOnly();
        SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<OpenBeltEditChange>() : batch.Operations.Select((o, i) => new OpenBeltEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("open-belt-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public OpenBeltDraft? Draft { get; }
    public string BaseDraftId { get; } public string RequestId { get; } public string ResultId { get; }
    public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<OpenBeltEditChange> Changes { get; }
}

public static class OpenBeltEditor
{
    public static OpenBeltEditResult Apply(OpenBeltDraft draft, OpenBeltEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        OpenBeltEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(draft, batch, null, index, diagnostics ?? new[] { new MechanicalDiagnostic(code, "OpenBeltEdit", detail: detail) });
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
                    case ApplyOpenBeltSourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source transaction rejected; no source/device/belt changes publish.", result.Diagnostics);
                        source = result.Draft; sourceResults.Add(result); break;
                    case SetOpenBeltSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetOpenBeltInputPitchRadiusEdit e: Device(e.DeviceId); d = Copy(d, inputRadius: e.Radius); break;
                    case SetOpenBeltOutputPitchRadiusEdit e: Device(e.DeviceId); d = Copy(d, outputRadius: e.Radius); break;
                    case SetOpenBeltInputPulleyCenterEdit e: Device(e.DeviceId); d = Copy(d, inputCenter: e.CenterMm); break;
                    case SetOpenBeltInputBindingEdit e: Device(e.DeviceId); d = Copy(d, inputShaft: e.ShaftId, inputPort: e.PortId, replacePort: true); break;
                    case SetOpenBeltOutputPulleyStationEdit e: Device(e.DeviceId); d = Copy(d, station: e.Station); break;
                    case SetOpenBeltOutputShaftFrameEdit e:
                        Shaft(e.ShaftId);
                        var oldFrame = d.OutputShaft.Frame;
                        var coordinateReversal = oldFrame.IsProperCardinal && e.Frame.IsProperCardinal &&
                            e.Frame.Origin == oldFrame.Origin && e.Frame.X == oldFrame.X && e.Frame.Z == oldFrame.Z * -1;
                        // A coordinate sign change is not an automatic belt/placement repair:
                        // both signed declarations transform to preserve the same physical mounting and mark phase.
                        d = Copy(d, outputShaft: new OrientedShaft(d.OutputShaft.Id, e.Frame, d.OutputShaft.IsPrescribed),
                            station: coordinateReversal ? ExactQuantity.FromCanonical(d.OutputPulleyStation.Kind, -d.OutputPulleyStation.Value) : d.OutputPulleyStation,
                            outputReference: coordinateReversal ? ExactQuantity.FromCanonical(d.OutputReferenceTurns.Kind, -d.OutputReferenceTurns.Value) : d.OutputReferenceTurns); break;
                    case MoveOpenBeltOutputShaftGroupEdit e:
                        Shaft(e.ShaftId);
                        if (!e.WorldRigidTransform.IsProperCardinal) return Reject("UnsupportedRigidTransform", i, "An explicit proper cardinal world rigid transform is required.");
                        d = Copy(d, outputShaft: new OrientedShaft(d.OutputShaft.Id, e.WorldRigidTransform.Transform(d.OutputShaft.Frame), d.OutputShaft.IsPrescribed));
                        terminal = new ShaftPort(terminal.Id, terminal.ShaftId, e.WorldRigidTransform.Transform(terminal.Frame), terminal.PhaseOffset, terminal.Kind); break;
                    case SetOpenBeltOutputTerminalEdit e:
                        Output(e.OutputKey); terminal = new ShaftPort(terminal.Id, terminal.ShaftId, e.Frame, terminal.PhaseOffset, terminal.Kind); break;
                    case SetOpenBeltRouteEdit e: Device(e.DeviceId); d = Copy(d, normal: e.Normal, routing: e.RoutingKind); break;
                    case SetOpenBeltReferenceEdit e: Device(e.DeviceId); d = Copy(d, inputReference: e.InputTurns, outputReference: e.OutputTurns); break;
                    case SetOpenBeltTransmissionEdit e: Device(e.DeviceId); d = Copy(d, present: e.Present); break;
                    case SetOpenBeltLengthSpecificationEdit e: Device(e.DeviceId); d = Copy(d, belt: e.Specification, replaceBelt: true); break;
                    case SetOpenBeltRequirementsEdit e:
                        Output(e.OutputKey); output = new MechanicalOutput(output.Key, output.ShaftId, output.BodyId, output.PortId, e.Transfer, output.Role, output.UnresolvedReason, output.FormerEndpoint); phase = e.Phase; break;
                    case SetOpenBeltRequiredValidationEdit e: domains = e.RequiredDomains; break;
                    case SetOpenBeltGroundingEdit e:
                        Device(e.DeviceId); d = Copy(d, inputCenterFixed: e.InputCenterFixed, inputAxisFixed: e.InputAxisFixed, outputCenterFixed: e.OutputCenterFixed, outputAxisFixed: e.OutputAxisFixed); break;
                    default: throw new ArgumentException("Unsupported typed open belt edit operation.");
                }
                current = new OpenBeltDefinition(source, mapping, d, terminal, output, phase, domains);
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, e.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sourceResults);
    }
    private static OpenBeltTransmissionDefinition Copy(OpenBeltTransmissionDefinition d, ExactQuantity? inputRadius = null, ExactQuantity? outputRadius = null,
        ExactVector3? inputCenter = null, string? inputShaft = null, string? inputPort = null, bool replacePort = false,
        OrientedShaft? outputShaft = null, ExactQuantity? station = null, ExactVector3? normal = null, string? routing = null,
        ExactQuantity? inputReference = null, ExactQuantity? outputReference = null, bool? present = null,
        OpenBeltLengthSpecification? belt = null, bool replaceBelt = false,
        bool? inputCenterFixed = null, bool? inputAxisFixed = null, bool? outputCenterFixed = null, bool? outputAxisFixed = null) =>
        new(d.Id, inputShaft ?? d.InputShaftId, d.InputPulleyBodyId, inputCenter ?? d.InputPulleyCenterMm, inputRadius ?? d.InputPitchRadius,
            outputShaft ?? d.OutputShaft, d.OutputPulleyBodyId, station ?? d.OutputPulleyStation, outputRadius ?? d.OutputPitchRadius,
            normal ?? d.RouteNormal, inputReference ?? d.InputReferenceTurns, outputReference ?? d.OutputReferenceTurns,
            replaceBelt ? belt : d.SelectedBelt, present ?? d.TransmissionPresent, inputCenterFixed ?? d.InputCenterFixed,
            inputAxisFixed ?? d.InputAxisFixed, outputCenterFixed ?? d.OutputCenterFixed, outputAxisFixed ?? d.OutputAxisFixed,
            replacePort ? inputPort : d.InputPortId, routing ?? d.RoutingKind);
}

internal static class OpenBeltEditKeys
{
    public static void Validate(OpenBeltEditOperation o)
    {
        void Id(string id) => MechanicalAuthoringProfile.IdValue(id);
        switch (o)
        {
            case ApplyOpenBeltSourceEditsEdit _: break;
            case SetOpenBeltSourceLengthMappingEdit _: break;
            case SetOpenBeltInputPitchRadiusEdit e: Id(e.DeviceId); OpenBeltProfile.Quantity(e.Radius); break;
            case SetOpenBeltOutputPitchRadiusEdit e: Id(e.DeviceId); OpenBeltProfile.Quantity(e.Radius); break;
            case SetOpenBeltInputPulleyCenterEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.CenterMm); break;
            case SetOpenBeltInputBindingEdit e: Id(e.DeviceId); Id(e.ShaftId); if (e.PortId is not null) Id(e.PortId); break;
            case SetOpenBeltOutputPulleyStationEdit e: Id(e.DeviceId); OpenBeltProfile.Quantity(e.Station); break;
            case SetOpenBeltOutputShaftFrameEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case MoveOpenBeltOutputShaftGroupEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.WorldRigidTransform); break;
            case SetOpenBeltOutputTerminalEdit e: Id(e.OutputKey); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case SetOpenBeltRouteEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.Normal); Id(e.RoutingKind); break;
            case SetOpenBeltReferenceEdit e: Id(e.DeviceId); OpenBeltProfile.Quantity(e.InputTurns); OpenBeltProfile.Quantity(e.OutputTurns); break;
            case SetOpenBeltTransmissionEdit e: Id(e.DeviceId); break;
            case SetOpenBeltLengthSpecificationEdit e: Id(e.DeviceId); break;
            case SetOpenBeltRequirementsEdit e: Id(e.OutputKey); if (e.Transfer.HasValue) MechanicalAuthoringProfile.Number(e.Transfer.Value); if (e.Phase.HasValue) MechanicalAuthoringProfile.Number(e.Phase.Value); break;
            case SetOpenBeltRequiredValidationEdit _: break;
            case SetOpenBeltGroundingEdit e: Id(e.DeviceId); break;
            default: throw new ArgumentException("Unsupported typed open belt edit.");
        }
    }
    public static string Operation(OpenBeltEditOperation o)
    {
        string value = o switch
        {
            ApplyOpenBeltSourceEditsEdit e => e.Batch.RequestId,
            SetOpenBeltSourceLengthMappingEdit e => e.Mapping?.CanonicalRepresentation ?? "",
            SetOpenBeltInputPitchRadiusEdit e => Pack(e.DeviceId, OpenBeltProfile.Q(e.Radius)),
            SetOpenBeltOutputPitchRadiusEdit e => Pack(e.DeviceId, OpenBeltProfile.Q(e.Radius)),
            SetOpenBeltInputPulleyCenterEdit e => Pack(e.DeviceId, V(e.CenterMm)),
            SetOpenBeltInputBindingEdit e => Pack(e.DeviceId, e.ShaftId, e.PortId ?? ""),
            SetOpenBeltOutputPulleyStationEdit e => Pack(e.DeviceId, OpenBeltProfile.Q(e.Station)),
            SetOpenBeltOutputShaftFrameEdit e => Pack(e.ShaftId, Frame(e.Frame)),
            MoveOpenBeltOutputShaftGroupEdit e => Pack(e.ShaftId, Frame(e.WorldRigidTransform)),
            SetOpenBeltOutputTerminalEdit e => Pack(e.OutputKey, Frame(e.Frame)),
            SetOpenBeltRouteEdit e => Pack(e.DeviceId, V(e.Normal), e.RoutingKind),
            SetOpenBeltReferenceEdit e => Pack(e.DeviceId, OpenBeltProfile.Q(e.InputTurns), OpenBeltProfile.Q(e.OutputTurns)),
            SetOpenBeltTransmissionEdit e => Pack(e.DeviceId, OpenBeltProfile.Flag(e.Present)),
            SetOpenBeltLengthSpecificationEdit e => Pack(e.DeviceId, e.Specification?.CanonicalRepresentation ?? ""),
            SetOpenBeltRequirementsEdit e => Pack(e.OutputKey, e.Transfer.HasValue ? F(e.Transfer.Value) : "", e.Phase.HasValue ? F(e.Phase.Value) : ""),
            SetOpenBeltRequiredValidationEdit e => Pack(e.RequiredDomains.ToArray()),
            SetOpenBeltGroundingEdit e => Pack(e.DeviceId, OpenBeltProfile.Flag(e.InputCenterFixed), OpenBeltProfile.Flag(e.InputAxisFixed), OpenBeltProfile.Flag(e.OutputCenterFixed), OpenBeltProfile.Flag(e.OutputAxisFixed)),
            _ => throw new ArgumentException("Unsupported typed open belt edit.")
        };
        return Pack(o.Kind, value);
    }
    public static IEnumerable<string> Dependencies(OpenBeltEditOperation o) => o switch
    {
        SetOpenBeltInputPitchRadiusEdit _ or SetOpenBeltOutputPitchRadiusEdit _ => new[] { "OpenTangentRoute", "BeltLengthCompatibility", "IdealNoSlipRelation", "WholeMotionDeterminacy", "OutputRequirements" },
        SetOpenBeltLengthSpecificationEdit _ => new[] { "BeltLengthCompatibility", "TransmissionAdmission", "WholeMotionDeterminacy", "OutputRequirements" },
        SetOpenBeltOutputTerminalEdit _ => new[] { "TerminalLaw", "OutputRequirements" },
        SetOpenBeltRequirementsEdit _ => new[] { "OutputRequirements" },
        SetOpenBeltRequiredValidationEdit _ => new[] { "RequiredValidation", "ExportAdmission" },
        _ => new[] { "LocalCompatibility", "OpenTangentRoute", "BeltLengthCompatibility", "Connectivity", "IdealNoSlipRelation", "WholeMotionDeterminacy", "OutputRequirements", "ExportAdmission" }
    };
}
