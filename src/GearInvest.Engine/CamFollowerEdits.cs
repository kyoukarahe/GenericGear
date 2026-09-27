using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class CamFollowerEditOperation
{
    private protected CamFollowerEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => CamFollowerEditKeys.Operation(this);
}
public sealed class ApplyCamFollowerSourceEditsEdit : CamFollowerEditOperation
{ public ApplyCamFollowerSourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); } public MechanicalEditBatch Batch { get; } }
public sealed class SetCamFollowerSourceLengthMappingEdit : CamFollowerEditOperation
{ public SetCamFollowerSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; } public SourceLengthMapping? Mapping { get; } }
public sealed class SetCamSupportProfileEdit : CamFollowerEditOperation
{ public SetCamSupportProfileEdit(string deviceId, CamSupportProfile profile) { DeviceId = deviceId; Profile = profile ?? throw new ArgumentNullException(nameof(profile)); } public string DeviceId { get; } public CamSupportProfile Profile { get; } }
public sealed class SetCamSupportSegmentEdit : CamFollowerEditOperation
{ public SetCamSupportSegmentEdit(string deviceId, string segmentId, CamSupportSegment segment) { DeviceId = deviceId; SegmentId = segmentId; Segment = segment ?? throw new ArgumentNullException(nameof(segment)); } public string DeviceId { get; } public string SegmentId { get; } public CamSupportSegment Segment { get; } }
public sealed class SetCamFollowerGuidePlacementEdit : CamFollowerEditOperation
{ public SetCamFollowerGuidePlacementEdit(string deviceId, OrientedFrame guideFrameMm, ExactVector3 planeNormal) { DeviceId = deviceId; GuideFrameMm = guideFrameMm; PlaneNormal = planeNormal; } public string DeviceId { get; } public OrientedFrame GuideFrameMm { get; } public ExactVector3 PlaneNormal { get; } }
public sealed class SetCamFollowerCenterEdit : CamFollowerEditOperation
{ public SetCamFollowerCenterEdit(string deviceId, ExactVector3 centerMm, ExactQuantity camStation) { DeviceId = deviceId; CenterMm = centerMm; CamStation = camStation; } public string DeviceId { get; } public ExactVector3 CenterMm { get; } public ExactQuantity CamStation { get; } }
public sealed class SetCamFollowerMountingPhaseEdit : CamFollowerEditOperation
{ public SetCamFollowerMountingPhaseEdit(string deviceId, ExactQuantity mountingTurns) { DeviceId = deviceId; MountingTurns = mountingTurns; } public string DeviceId { get; } public ExactQuantity MountingTurns { get; } }
public sealed class SetCamFollowerGuideTravelEdit : CamFollowerEditOperation
{ public SetCamFollowerGuideTravelEdit(string deviceId, ExactQuantityInterval guideTravel) { DeviceId = deviceId; GuideTravel = guideTravel; } public string DeviceId { get; } public ExactQuantityInterval GuideTravel { get; } }
public sealed class SetCamFollowerFaceEdit : CamFollowerEditOperation
{ public SetCamFollowerFaceEdit(string deviceId, ExactQuantityInterval followerFace) { DeviceId = deviceId; FollowerFace = followerFace; } public string DeviceId { get; } public ExactQuantityInterval FollowerFace { get; } }
public sealed class SetCamFollowerTerminalEdit : CamFollowerEditOperation
{ public SetCamFollowerTerminalEdit(string outputKey, int sign, ExactQuantity datum) { OutputKey = outputKey; Sign = sign; Datum = datum; } public string OutputKey { get; } public int Sign { get; } public ExactQuantity Datum { get; } }
public sealed class SetCamFollowerRequirementsEdit : CamFollowerEditOperation
{ public SetCamFollowerRequirementsEdit(string outputKey, CamFollowerRequirement requirement) { OutputKey = outputKey; Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement)); } public string OutputKey { get; } public CamFollowerRequirement Requirement { get; } }
public sealed class SetCamFollowerContactEdit : CamFollowerEditOperation
{ public SetCamFollowerContactEdit(string deviceId, bool present, string? policy = CamFollowerProfile.MaintainedContactIdeal) { DeviceId = deviceId; Present = present; Policy = policy; } public string DeviceId { get; } public bool Present { get; } public string? Policy { get; } }
public sealed class SetCamFollowerGroundingEdit : CamFollowerEditOperation
{
    public SetCamFollowerGroundingEdit(string deviceId, bool guidePresent, bool followerRotationFixed, bool transverseMotionFixed, bool camAxisFixed)
    { DeviceId = deviceId; GuidePresent = guidePresent; FollowerRotationFixed = followerRotationFixed; TransverseMotionFixed = transverseMotionFixed; CamAxisFixed = camAxisFixed; }
    public string DeviceId { get; } public bool GuidePresent { get; } public bool FollowerRotationFixed { get; } public bool TransverseMotionFixed { get; } public bool CamAxisFixed { get; }
}
public sealed class SetCamFollowerBindingEdit : CamFollowerEditOperation
{ public SetCamFollowerBindingEdit(string deviceId, string sourceShaftId, string? sourcePortId) { DeviceId = deviceId; SourceShaftId = sourceShaftId; SourcePortId = sourcePortId; } public string DeviceId { get; } public string SourceShaftId { get; } public string? SourcePortId { get; } }
public sealed class SetCamFollowerInputTopologyEdit : CamFollowerEditOperation
{ public SetCamFollowerInputTopologyEdit(string deviceId, bool followerIsPrescribed) { DeviceId = deviceId; FollowerIsPrescribed = followerIsPrescribed; } public string DeviceId { get; } public bool FollowerIsPrescribed { get; } }
public sealed class SetCamFollowerRequiredValidationDomainsEdit : CamFollowerEditOperation
{ public SetCamFollowerRequiredValidationDomainsEdit(IEnumerable<string> domains) { Domains = MechanicalAuthoringProfile.Set(domains, s => s, 24); } public ReadOnlyCollection<string> Domains { get; } }

public sealed class CamFollowerEditBatch
{
    public CamFollowerEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<CamFollowerEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > CamFollowerProfile.MaxTotalOperations) throw new ArgumentException("Cam-follower ordered operation bound exceeded.");
        foreach (var op in Operations) CamFollowerEditKeys.Validate(op);
        RequestId = HashText(Pack("cam-follower-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; } public string RequestId { get; }
    public ReadOnlyCollection<CamFollowerEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyCamFollowerSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
}
public sealed class CamFollowerEditChange
{
    internal CamFollowerEditChange(int index, CamFollowerEditOperation operation)
    { OperationIndex = index; OperationKind = operation.Kind; DependentMetrics = new[] { "CurrentDefinition", "SupportGeometryProof", "ContactAndMotion", "FaceAndGuideCoverage", "RequirementsAndExport" }.ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}
public sealed class CamFollowerEditResult
{
    internal CamFollowerEditResult(CamFollowerDraft before, CamFollowerEditBatch batch, CamFollowerDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft; BaseDraftId = before.DraftId;
        RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex; Diagnostics = diagnostics.ToList().AsReadOnly();
        SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<CamFollowerEditChange>() : batch.Operations.Select((o, i) => new CamFollowerEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("cam-follower-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public CamFollowerDraft? Draft { get; } public string BaseDraftId { get; }
    public string RequestId { get; } public string ResultId { get; } public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<CamFollowerEditChange> Changes { get; }
}

public static class CamFollowerEditor
{
    public static CamFollowerEditResult Apply(CamFollowerDraft draft, CamFollowerEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        CamFollowerEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? issues = null) =>
            new(draft, batch, null, index, issues ?? new[] { new MechanicalDiagnostic(code, "CamFollowerEdit", detail: detail) });
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId) return Reject("StaleRevision", null, "Both revision and definition must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null, "Revision bound exceeded.");
        var current = draft.Definition; var sources = new List<MechanicalEditResult>();
        for (var i = 0; i < batch.Operations.Count; i++)
        {
            try
            {
                var d = current.Device; var output = current.Output; var source = current.Source; var mapping = current.SourceMapping;
                var requirement = current.Requirement; IEnumerable<string> domains = current.RequiredValidationDomains;
                void Device(string id) { if (id != d.Id) throw new ArgumentException("UnknownDeviceId: " + id); }
                void Output(string id) { if (id != output.Key) throw new ArgumentException("UnknownOutputId: " + id); }
                switch (batch.Operations[i])
                {
                    case ApplyCamFollowerSourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source edit rejected; entire device batch rolls back.", result.Diagnostics);
                        source = result.Draft; sources.Add(result); break;
                    case SetCamFollowerSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetCamSupportProfileEdit e: Device(e.DeviceId); d = Copy(d, profile: e.Profile); break;
                    case SetCamSupportSegmentEdit e:
                        Device(e.DeviceId);
                        if (d.SupportProfile.Segments.Count(s => s.Id == e.SegmentId) != 1 || e.Segment.Id != e.SegmentId) throw new ArgumentException("UnknownOrAmbiguousSegmentId: " + e.SegmentId);
                        d = Copy(d, profile: new CamSupportProfile(d.SupportProfile.Segments.Select(s => s.Id == e.SegmentId ? e.Segment : s))); break;
                    case SetCamFollowerGuidePlacementEdit e: Device(e.DeviceId); d = Copy(d, guide: e.GuideFrameMm, normal: e.PlaneNormal); break;
                    case SetCamFollowerCenterEdit e: Device(e.DeviceId); d = Copy(d, center: e.CenterMm, station: e.CamStation); break;
                    case SetCamFollowerMountingPhaseEdit e: Device(e.DeviceId); d = Copy(d, mounting: e.MountingTurns); break;
                    case SetCamFollowerGuideTravelEdit e: Device(e.DeviceId); d = Copy(d, travel: e.GuideTravel); break;
                    case SetCamFollowerFaceEdit e: Device(e.DeviceId); d = Copy(d, face: e.FollowerFace); break;
                    case SetCamFollowerContactEdit e: Device(e.DeviceId); d = Copy(d, contact: e.Present, policy: e.Policy, replacePolicy: true); break;
                    case SetCamFollowerGroundingEdit e: Device(e.DeviceId); d = Copy(d, guidePresent: e.GuidePresent, rotationFixed: e.FollowerRotationFixed, transverse: e.TransverseMotionFixed, axisFixed: e.CamAxisFixed); break;
                    case SetCamFollowerBindingEdit e: Device(e.DeviceId); d = Copy(d, shaft: e.SourceShaftId, port: e.SourcePortId, replacePort: true); break;
                    case SetCamFollowerInputTopologyEdit e: Device(e.DeviceId); d = Copy(d, prescribed: e.FollowerIsPrescribed); break;
                    case SetCamFollowerTerminalEdit e: Output(e.OutputKey); output = new(output.Key, output.LinearDofId, output.BodyId, output.ReferencePointId, e.Sign, e.Datum); break;
                    case SetCamFollowerRequirementsEdit e: Output(e.OutputKey); requirement = e.Requirement; break;
                    case SetCamFollowerRequiredValidationDomainsEdit e: domains = e.Domains; break;
                    default: throw new ArgumentException("Unsupported typed cam-follower edit operation.");
                }
                current = new(source, mapping, d, output, requirement, domains);
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, e.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sources);
    }
    internal static FlatCamFollowerDefinition Copy(FlatCamFollowerDefinition d, CamSupportProfile? profile = null,
        ExactVector3? center = null, ExactQuantity? station = null, ExactVector3? normal = null, OrientedFrame? guide = null,
        ExactQuantity? mounting = null, ExactQuantityInterval? travel = null, ExactQuantityInterval? face = null,
        bool? contact = null, string? policy = null, bool replacePolicy = false, bool? guidePresent = null,
        bool? rotationFixed = null, bool? transverse = null, bool? axisFixed = null, string? shaft = null,
        string? port = null, bool replacePort = false, bool? prescribed = null) =>
        new(d.Id, shaft ?? d.SourceShaftId, d.CamBodyId, d.FollowerBodyId, d.GuideId, d.LinearDofId, d.FollowerReferenceId,
            profile ?? d.SupportProfile, center ?? d.CenterMm, station ?? d.CamStation, normal ?? d.PlaneNormal, guide ?? d.GuideFrameMm,
            mounting ?? d.MountingTurns, travel ?? d.GuideTravel, face ?? d.FollowerFace, contact ?? d.ContactPresent,
            replacePolicy ? policy : d.ContactPolicy, guidePresent ?? d.GuidePresent, rotationFixed ?? d.FollowerRotationFixed,
            transverse ?? d.TransverseMotionFixed, axisFixed ?? d.CamAxisFixed, replacePort ? port : d.SourcePortId,
            prescribed ?? d.FollowerIsPrescribed, d.FollowerKind);
}

internal static class CamFollowerEditKeys
{
    internal static void Validate(CamFollowerEditOperation op)
    {
        void Id(string id) => MechanicalAuthoringProfile.IdValue(id);
        switch (op)
        {
            case ApplyCamFollowerSourceEditsEdit _: break;
            case SetCamFollowerSourceLengthMappingEdit _: break;
            case SetCamSupportProfileEdit e: Id(e.DeviceId); break;
            case SetCamSupportSegmentEdit e: Id(e.DeviceId); Id(e.SegmentId); break;
            case SetCamFollowerGuidePlacementEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.FrameBound(e.GuideFrameMm); MechanicalAuthoringProfile.VectorBound(e.PlaneNormal); break;
            case SetCamFollowerCenterEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.CenterMm); RotaryLinearProfile.Quantity(e.CamStation); break;
            case SetCamFollowerMountingPhaseEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.MountingTurns); break;
            case SetCamFollowerGuideTravelEdit e: Id(e.DeviceId); RotaryLinearProfile.Interval(e.GuideTravel); break;
            case SetCamFollowerFaceEdit e: Id(e.DeviceId); RotaryLinearProfile.Interval(e.FollowerFace); break;
            case SetCamFollowerTerminalEdit e: Id(e.OutputKey); RotaryLinearProfile.Quantity(e.Datum); break;
            case SetCamFollowerRequirementsEdit e: Id(e.OutputKey); break;
            case SetCamFollowerContactEdit e: Id(e.DeviceId); if (e.Policy is not null) Id(e.Policy); break;
            case SetCamFollowerGroundingEdit e: Id(e.DeviceId); break;
            case SetCamFollowerBindingEdit e: Id(e.DeviceId); Id(e.SourceShaftId); if (e.SourcePortId is not null) Id(e.SourcePortId); break;
            case SetCamFollowerInputTopologyEdit e: Id(e.DeviceId); break;
            case SetCamFollowerRequiredValidationDomainsEdit _: break;
            default: throw new ArgumentException("Unsupported typed cam-follower edit operation.");
        }
    }
    internal static string Operation(CamFollowerEditOperation op) => Pack(op.Kind, op switch
    {
        ApplyCamFollowerSourceEditsEdit e => e.Batch.RequestId,
        SetCamFollowerSourceLengthMappingEdit e => e.Mapping?.CanonicalRepresentation ?? "",
        SetCamSupportProfileEdit e => Pack(e.DeviceId, e.Profile.CanonicalRepresentation),
        SetCamSupportSegmentEdit e => Pack(e.DeviceId, e.SegmentId, e.Segment.CanonicalRepresentation),
        SetCamFollowerGuidePlacementEdit e => Pack(e.DeviceId, Frame(e.GuideFrameMm), V(e.PlaneNormal)),
        SetCamFollowerCenterEdit e => Pack(e.DeviceId, V(e.CenterMm), CamFollowerProfile.Q(e.CamStation)),
        SetCamFollowerMountingPhaseEdit e => Pack(e.DeviceId, CamFollowerProfile.Q(e.MountingTurns)),
        SetCamFollowerGuideTravelEdit e => Pack(e.DeviceId, CamFollowerProfile.I(e.GuideTravel)),
        SetCamFollowerFaceEdit e => Pack(e.DeviceId, CamFollowerProfile.I(e.FollowerFace)),
        SetCamFollowerTerminalEdit e => Pack(e.OutputKey, N(e.Sign), CamFollowerProfile.Q(e.Datum)),
        SetCamFollowerRequirementsEdit e => Pack(e.OutputKey, e.Requirement.CanonicalRepresentation),
        SetCamFollowerContactEdit e => Pack(e.DeviceId, CamFollowerProfile.Flag(e.Present), e.Policy ?? ""),
        SetCamFollowerGroundingEdit e => Pack(e.DeviceId, CamFollowerProfile.Flag(e.GuidePresent), CamFollowerProfile.Flag(e.FollowerRotationFixed), CamFollowerProfile.Flag(e.TransverseMotionFixed), CamFollowerProfile.Flag(e.CamAxisFixed)),
        SetCamFollowerBindingEdit e => Pack(e.DeviceId, e.SourceShaftId, e.SourcePortId ?? ""),
        SetCamFollowerInputTopologyEdit e => Pack(e.DeviceId, CamFollowerProfile.Flag(e.FollowerIsPrescribed)),
        SetCamFollowerRequiredValidationDomainsEdit e => Pack(e.Domains.ToArray()),
        _ => throw new ArgumentException("Unsupported typed cam-follower edit operation.")
    });
}
