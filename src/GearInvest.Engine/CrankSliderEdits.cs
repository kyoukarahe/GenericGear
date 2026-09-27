using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class CrankSliderEditOperation
{
    private protected CrankSliderEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => CrankSliderEditKeys.Operation(this);
}
public sealed class ApplyCrankSliderSourceEditsEdit : CrankSliderEditOperation
{ public ApplyCrankSliderSourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); } public MechanicalEditBatch Batch { get; } }
public sealed class SetCrankSliderSourceLengthMappingEdit : CrankSliderEditOperation
{ public SetCrankSliderSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; } public SourceLengthMapping? Mapping { get; } }
public sealed class SetCrankSliderRadiusEdit : CrankSliderEditOperation
{ public SetCrankSliderRadiusEdit(string deviceId, ExactQuantity radius) { DeviceId = deviceId; Radius = radius; } public string DeviceId { get; } public ExactQuantity Radius { get; } }
public sealed class SetCrankSliderRodLengthEdit : CrankSliderEditOperation
{ public SetCrankSliderRodLengthEdit(string deviceId, ExactQuantity rodLength) { DeviceId = deviceId; RodLength = rodLength; } public string DeviceId { get; } public ExactQuantity RodLength { get; } }
public sealed class SetCrankSliderGuidePlacementEdit : CrankSliderEditOperation
{ public SetCrankSliderGuidePlacementEdit(string deviceId, OrientedFrame guideFrameMm, ExactVector3 planeNormal) { DeviceId = deviceId; GuideFrameMm = guideFrameMm; PlaneNormal = planeNormal; } public string DeviceId { get; } public OrientedFrame GuideFrameMm { get; } public ExactVector3 PlaneNormal { get; } }
public sealed class SetCrankSliderPivotEdit : CrankSliderEditOperation
{ public SetCrankSliderPivotEdit(string deviceId, ExactVector3 pivotMm, ExactQuantity pivotStation) { DeviceId = deviceId; PivotMm = pivotMm; PivotStation = pivotStation; } public string DeviceId { get; } public ExactVector3 PivotMm { get; } public ExactQuantity PivotStation { get; } }
public sealed class SetCrankSliderMountingPhaseEdit : CrankSliderEditOperation
{ public SetCrankSliderMountingPhaseEdit(string deviceId, ExactQuantity mountingTurns) { DeviceId = deviceId; MountingTurns = mountingTurns; } public string DeviceId { get; } public ExactQuantity MountingTurns { get; } }
public sealed class SetCrankSliderBranchEdit : CrankSliderEditOperation
{ public SetCrankSliderBranchEdit(string deviceId, int? assemblyBranch) { DeviceId = deviceId; AssemblyBranch = assemblyBranch; } public string DeviceId { get; } public int? AssemblyBranch { get; } }
public sealed class SetCrankSliderGuideTravelEdit : CrankSliderEditOperation
{ public SetCrankSliderGuideTravelEdit(string deviceId, ExactQuantityInterval guideTravel) { DeviceId = deviceId; GuideTravel = guideTravel; } public string DeviceId { get; } public ExactQuantityInterval GuideTravel { get; } }
public sealed class SetCrankSliderTerminalEdit : CrankSliderEditOperation
{ public SetCrankSliderTerminalEdit(string outputKey, int sign, ExactQuantity datum) { OutputKey = outputKey; Sign = sign; Datum = datum; } public string OutputKey { get; } public int Sign { get; } public ExactQuantity Datum { get; } }
public sealed class SetCrankSliderRequirementsEdit : CrankSliderEditOperation
{ public SetCrankSliderRequirementsEdit(string outputKey, CrankSliderRequirement requirement) { OutputKey = outputKey; Requirement = requirement; } public string OutputKey { get; } public CrankSliderRequirement Requirement { get; } }
public sealed class SetCrankSliderTransmissionEdit : CrankSliderEditOperation
{ public SetCrankSliderTransmissionEdit(string deviceId, bool present) { DeviceId = deviceId; Present = present; } public string DeviceId { get; } public bool Present { get; } }
public sealed class SetCrankSliderGroundingEdit : CrankSliderEditOperation
{
    public SetCrankSliderGroundingEdit(string deviceId, bool guidePresent, bool sliderRotationFixed, bool transverseMotionFixed, bool crankAxisFixed)
    { DeviceId = deviceId; GuidePresent = guidePresent; SliderRotationFixed = sliderRotationFixed; TransverseMotionFixed = transverseMotionFixed; CrankAxisFixed = crankAxisFixed; }
    public string DeviceId { get; } public bool GuidePresent { get; } public bool SliderRotationFixed { get; } public bool TransverseMotionFixed { get; } public bool CrankAxisFixed { get; }
}
public sealed class SetCrankSliderBindingEdit : CrankSliderEditOperation
{ public SetCrankSliderBindingEdit(string deviceId, string sourceShaftId, string? sourcePortId) { DeviceId = deviceId; SourceShaftId = sourceShaftId; SourcePortId = sourcePortId; } public string DeviceId { get; } public string SourceShaftId { get; } public string? SourcePortId { get; } }
public sealed class SetCrankSliderJointPresenceEdit : CrankSliderEditOperation
{ public SetCrankSliderJointPresenceEdit(string deviceId, bool crankPinPresent, bool sliderPinPresent) { DeviceId = deviceId; CrankPinPresent = crankPinPresent; SliderPinPresent = sliderPinPresent; } public string DeviceId { get; } public bool CrankPinPresent { get; } public bool SliderPinPresent { get; } }
public sealed class SetCrankSliderInputTopologyEdit : CrankSliderEditOperation
{ public SetCrankSliderInputTopologyEdit(string deviceId, bool sliderIsPrescribed) { DeviceId = deviceId; SliderIsPrescribed = sliderIsPrescribed; } public string DeviceId { get; } public bool SliderIsPrescribed { get; } }
public sealed class SetCrankSliderRequiredValidationDomainsEdit : CrankSliderEditOperation
{ public SetCrankSliderRequiredValidationDomainsEdit(IEnumerable<string> domains) { Domains = MechanicalAuthoringProfile.Set(domains, s => s, 24); } public ReadOnlyCollection<string> Domains { get; } }

public sealed class CrankSliderEditBatch
{
    public CrankSliderEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<CrankSliderEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > CrankSliderProfile.MaxTotalOperations) throw new ArgumentException("Crank-slider ordered operation bound exceeded.");
        foreach (var op in Operations) CrankSliderEditKeys.Validate(op);
        RequestId = HashText(Pack("crank-slider-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; } public string RequestId { get; }
    public ReadOnlyCollection<CrankSliderEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyCrankSliderSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
}

public sealed class CrankSliderEditChange
{
    internal CrankSliderEditChange(int index, CrankSliderEditOperation operation)
    { OperationIndex = index; OperationKind = operation.Kind; DependentMetrics = new[] { "CurrentDefinition", "NonlinearAnalysis", "FunctionAndPose", "EnvelopeAndRequirements", "ExportAdmission" }.ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}

public sealed class CrankSliderEditResult
{
    internal CrankSliderEditResult(CrankSliderDraft before, CrankSliderEditBatch batch, CrankSliderDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft; BaseDraftId = before.DraftId;
        RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex; Diagnostics = diagnostics.ToList().AsReadOnly();
        SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<CrankSliderEditChange>() : batch.Operations.Select((o, i) => new CrankSliderEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("crank-slider-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public CrankSliderDraft? Draft { get; } public string BaseDraftId { get; }
    public string RequestId { get; } public string ResultId { get; } public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<CrankSliderEditChange> Changes { get; }
}

public static class CrankSliderEditor
{
    public static CrankSliderEditResult Apply(CrankSliderDraft draft, CrankSliderEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        CrankSliderEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(draft, batch, null, index, diagnostics ?? new[] { new MechanicalDiagnostic(code, "CrankSliderEdit", detail: detail) });
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId) return Reject("StaleRevision", null, "Both revision and definition must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null, "Revision bound exceeded.");
        var current = draft.Definition; var sourceResults = new List<MechanicalEditResult>();
        for (var i = 0; i < batch.Operations.Count; i++)
        {
            try
            {
                var op = batch.Operations[i]; var d = current.Device; var output = current.Output; var source = current.Source;
                var mapping = current.SourceMapping; var requirement = current.Requirement; IEnumerable<string> domains = current.RequiredValidationDomains;
                void Device(string id) { if (id != d.Id) throw new ArgumentException("UnknownDeviceId: " + id); }
                void Output(string key) { if (key != output.Key) throw new ArgumentException("UnknownOutputId: " + key); }
                switch (op)
                {
                    case ApplyCrankSliderSourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source transaction rejected; no device changes publish.", result.Diagnostics);
                        source = result.Draft; sourceResults.Add(result); break;
                    case SetCrankSliderSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetCrankSliderRadiusEdit e: Device(e.DeviceId); d = Copy(d, radius: e.Radius); break;
                    case SetCrankSliderRodLengthEdit e: Device(e.DeviceId); d = Copy(d, rod: e.RodLength); break;
                    case SetCrankSliderGuidePlacementEdit e: Device(e.DeviceId); d = Copy(d, guide: e.GuideFrameMm, normal: e.PlaneNormal); break;
                    case SetCrankSliderPivotEdit e: Device(e.DeviceId); d = Copy(d, pivot: e.PivotMm, station: e.PivotStation); break;
                    case SetCrankSliderMountingPhaseEdit e: Device(e.DeviceId); d = Copy(d, mounting: e.MountingTurns); break;
                    case SetCrankSliderBranchEdit e: Device(e.DeviceId); d = Copy(d, branch: e.AssemblyBranch, replaceBranch: true); break;
                    case SetCrankSliderGuideTravelEdit e: Device(e.DeviceId); d = Copy(d, travel: e.GuideTravel); break;
                    case SetCrankSliderTransmissionEdit e: Device(e.DeviceId); d = Copy(d, transmission: e.Present); break;
                    case SetCrankSliderGroundingEdit e: Device(e.DeviceId); d = Copy(d, guidePresent: e.GuidePresent, rotationFixed: e.SliderRotationFixed, transverse: e.TransverseMotionFixed, axisFixed: e.CrankAxisFixed); break;
                    case SetCrankSliderBindingEdit e: Device(e.DeviceId); d = Copy(d, shaft: e.SourceShaftId, port: e.SourcePortId, replacePort: true); break;
                    case SetCrankSliderJointPresenceEdit e: Device(e.DeviceId); d = Copy(d, crankPin: e.CrankPinPresent, sliderPin: e.SliderPinPresent); break;
                    case SetCrankSliderInputTopologyEdit e: Device(e.DeviceId); d = Copy(d, prescribed: e.SliderIsPrescribed); break;
                    case SetCrankSliderTerminalEdit e: Output(e.OutputKey); output = new(output.Key, output.LinearDofId, output.BodyId, output.ReferencePointId, e.Sign, e.Datum); break;
                    case SetCrankSliderRequirementsEdit e: Output(e.OutputKey); requirement = e.Requirement; break;
                    case SetCrankSliderRequiredValidationDomainsEdit e: domains = e.Domains; break;
                    default: throw new ArgumentException("Unsupported typed crank-slider edit operation.");
                }
                current = new(source, mapping, d, output, requirement, domains);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            { return Reject(exception.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, exception.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sourceResults);
    }
    private static PlanarCrankSliderDefinition Copy(PlanarCrankSliderDefinition d, ExactQuantity? radius = null, ExactQuantity? rod = null,
        ExactVector3? pivot = null, ExactQuantity? station = null, ExactVector3? normal = null, OrientedFrame? guide = null,
        ExactQuantity? mounting = null, int? branch = null, bool replaceBranch = false, ExactQuantityInterval? travel = null,
        bool? transmission = null, bool? guidePresent = null, bool? rotationFixed = null, bool? transverse = null, bool? axisFixed = null,
        string? shaft = null, string? port = null, bool replacePort = false, bool? prescribed = null, bool? crankPin = null, bool? sliderPin = null) =>
        new(d.Id, shaft ?? d.SourceShaftId, d.CrankBodyId, d.RodBodyId, d.SliderBodyId, d.GuideId, d.LinearDofId, d.CrankPinId, d.SliderPinId,
            radius ?? d.CrankRadius, rod ?? d.RodLength, pivot ?? d.PivotMm, station ?? d.PivotStation, normal ?? d.PlaneNormal,
            guide ?? d.GuideFrameMm, mounting ?? d.MountingTurns, replaceBranch ? branch : d.AssemblyBranch, travel ?? d.GuideTravel,
            transmission ?? d.TransmissionPresent, guidePresent ?? d.GuidePresent, rotationFixed ?? d.SliderRotationFixed, transverse ?? d.TransverseMotionFixed,
            axisFixed ?? d.CrankAxisFixed, replacePort ? port : d.SourcePortId, prescribed ?? d.SliderIsPrescribed, crankPin ?? d.CrankPinPresent, sliderPin ?? d.SliderPinPresent);
}

internal static class CrankSliderEditKeys
{
    internal static void Validate(CrankSliderEditOperation op)
    {
        void Id(string id) => MechanicalAuthoringProfile.IdValue(id);
        switch (op)
        {
            case ApplyCrankSliderSourceEditsEdit _: break;
            case SetCrankSliderSourceLengthMappingEdit _: break;
            case SetCrankSliderRadiusEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.Radius); break;
            case SetCrankSliderRodLengthEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.RodLength); break;
            case SetCrankSliderGuidePlacementEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.FrameBound(e.GuideFrameMm); MechanicalAuthoringProfile.VectorBound(e.PlaneNormal); break;
            case SetCrankSliderPivotEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.PivotMm); RotaryLinearProfile.Quantity(e.PivotStation); break;
            case SetCrankSliderMountingPhaseEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.MountingTurns); break;
            case SetCrankSliderBranchEdit e: Id(e.DeviceId); break;
            case SetCrankSliderGuideTravelEdit e: Id(e.DeviceId); RotaryLinearProfile.Interval(e.GuideTravel); break;
            case SetCrankSliderTerminalEdit e: Id(e.OutputKey); RotaryLinearProfile.Quantity(e.Datum); break;
            case SetCrankSliderRequirementsEdit e: Id(e.OutputKey); if (e.Requirement is null) throw new ArgumentNullException(nameof(e.Requirement)); break;
            case SetCrankSliderTransmissionEdit e: Id(e.DeviceId); break;
            case SetCrankSliderGroundingEdit e: Id(e.DeviceId); break;
            case SetCrankSliderBindingEdit e: Id(e.DeviceId); Id(e.SourceShaftId); if (e.SourcePortId is not null) Id(e.SourcePortId); break;
            case SetCrankSliderJointPresenceEdit e: Id(e.DeviceId); break;
            case SetCrankSliderInputTopologyEdit e: Id(e.DeviceId); break;
            case SetCrankSliderRequiredValidationDomainsEdit _: break;
            default: throw new ArgumentException("Unsupported typed crank-slider edit operation.");
        }
    }
    internal static string Operation(CrankSliderEditOperation op)
    {
        string Q(ExactQuantity value) => CrankSliderProfile.Q(value); string B(bool value) => CrankSliderProfile.Flag(value);
        var body = op switch
        {
            ApplyCrankSliderSourceEditsEdit e => e.Batch.RequestId,
            SetCrankSliderSourceLengthMappingEdit e => e.Mapping?.CanonicalRepresentation ?? "",
            SetCrankSliderRadiusEdit e => Pack(e.DeviceId, Q(e.Radius)),
            SetCrankSliderRodLengthEdit e => Pack(e.DeviceId, Q(e.RodLength)),
            SetCrankSliderGuidePlacementEdit e => Pack(e.DeviceId, Frame(e.GuideFrameMm), V(e.PlaneNormal)),
            SetCrankSliderPivotEdit e => Pack(e.DeviceId, V(e.PivotMm), Q(e.PivotStation)),
            SetCrankSliderMountingPhaseEdit e => Pack(e.DeviceId, Q(e.MountingTurns)),
            SetCrankSliderBranchEdit e => Pack(e.DeviceId, e.AssemblyBranch?.ToString(CultureInfo.InvariantCulture) ?? ""),
            SetCrankSliderGuideTravelEdit e => Pack(e.DeviceId, CrankSliderProfile.I(e.GuideTravel)),
            SetCrankSliderTerminalEdit e => Pack(e.OutputKey, N(e.Sign), Q(e.Datum)),
            SetCrankSliderRequirementsEdit e => Pack(e.OutputKey, e.Requirement.CanonicalRepresentation),
            SetCrankSliderTransmissionEdit e => Pack(e.DeviceId, B(e.Present)),
            SetCrankSliderGroundingEdit e => Pack(e.DeviceId, B(e.GuidePresent), B(e.SliderRotationFixed), B(e.TransverseMotionFixed), B(e.CrankAxisFixed)),
            SetCrankSliderBindingEdit e => Pack(e.DeviceId, e.SourceShaftId, e.SourcePortId ?? ""),
            SetCrankSliderJointPresenceEdit e => Pack(e.DeviceId, B(e.CrankPinPresent), B(e.SliderPinPresent)),
            SetCrankSliderInputTopologyEdit e => Pack(e.DeviceId, B(e.SliderIsPrescribed)),
            SetCrankSliderRequiredValidationDomainsEdit e => Pack(e.Domains.ToArray()),
            _ => throw new ArgumentException("Unsupported typed crank-slider edit operation.")
        };
        return Pack(op.Kind, body);
    }
}
