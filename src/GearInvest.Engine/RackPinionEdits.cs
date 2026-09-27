using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Ordered explicit CP-device and existing source edits. No edit repairs a dependent guide or requirement implicitly.</summary>
public abstract class RackPinionEditOperation
{
    private protected RackPinionEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => RackPinionEditKeys.Operation(this);
}
public sealed class ApplyRackSourceEditsEdit : RackPinionEditOperation
{ public ApplyRackSourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); } public MechanicalEditBatch Batch { get; } }
public sealed class SetRackSourceLengthMappingEdit : RackPinionEditOperation
{ public SetRackSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; } public SourceLengthMapping? Mapping { get; } }
public sealed class SetRackPinionTeethEdit : RackPinionEditOperation
{ public SetRackPinionTeethEdit(string deviceId, int toothCount) { DeviceId = deviceId; ToothCount = toothCount; } public string DeviceId { get; } public int ToothCount { get; } }
public sealed class SetRackPinionCircularPitchEdit : RackPinionEditOperation
{ public SetRackPinionCircularPitchEdit(string deviceId, ExactQuantity circularPitch) { DeviceId = deviceId; CircularPitch = circularPitch; } public string DeviceId { get; } public ExactQuantity CircularPitch { get; } }
public sealed class SetRackPitchEdit : RackPinionEditOperation
{ public SetRackPitchEdit(string deviceId, ExactQuantity rackPitch) { DeviceId = deviceId; RackPitch = rackPitch; } public string DeviceId { get; } public ExactQuantity RackPitch { get; } }
public sealed class SetRackPinionCenterEdit : RackPinionEditOperation
{ public SetRackPinionCenterEdit(string deviceId, ExactVector3 centerMm) { DeviceId = deviceId; CenterMm = centerMm; } public string DeviceId { get; } public ExactVector3 CenterMm { get; } }
public sealed class SetRackContactNormalEdit : RackPinionEditOperation
{ public SetRackContactNormalEdit(string deviceId, ExactVector3 contactNormal) { DeviceId = deviceId; ContactNormal = contactNormal; } public string DeviceId { get; } public ExactVector3 ContactNormal { get; } }
public sealed class SetRackGuidePlacementEdit : RackPinionEditOperation
{
    public SetRackGuidePlacementEdit(string deviceId, ExactPiFrame guideFrameMm, ExactQuantity longitudinalOffset)
    { DeviceId = deviceId; GuideFrameMm = guideFrameMm; LongitudinalOffset = longitudinalOffset; }
    public string DeviceId { get; } public ExactPiFrame GuideFrameMm { get; } public ExactQuantity LongitudinalOffset { get; }
}
public sealed class SetRackReferenceEdit : RackPinionEditOperation
{
    public SetRackReferenceEdit(string deviceId, ExactQuantity pinionReferenceTurns, ExactQuantity rackReferencePosition)
    { DeviceId = deviceId; PinionReferenceTurns = pinionReferenceTurns; RackReferencePosition = rackReferencePosition; }
    public string DeviceId { get; } public ExactQuantity PinionReferenceTurns { get; } public ExactQuantity RackReferencePosition { get; }
}
public sealed class SetRackActiveMaterialIntervalEdit : RackPinionEditOperation
{ public SetRackActiveMaterialIntervalEdit(string deviceId, ExactQuantityInterval interval) { DeviceId = deviceId; Interval = interval; } public string DeviceId { get; } public ExactQuantityInterval Interval { get; } }
public sealed class SetRackGuideIntervalEdit : RackPinionEditOperation
{ public SetRackGuideIntervalEdit(string deviceId, ExactQuantityInterval interval) { DeviceId = deviceId; Interval = interval; } public string DeviceId { get; } public ExactQuantityInterval Interval { get; } }
public sealed class SetRackGroundingEdit : RackPinionEditOperation
{
    public SetRackGroundingEdit(string deviceId, bool pinionCenterFixed, bool pinionAxisFixed, bool guidePresent, bool rackRotationFixed, bool transverseMotionFixed)
    { DeviceId = deviceId; PinionCenterFixed = pinionCenterFixed; PinionAxisFixed = pinionAxisFixed; GuidePresent = guidePresent; RackRotationFixed = rackRotationFixed; TransverseMotionFixed = transverseMotionFixed; }
    public string DeviceId { get; } public bool PinionCenterFixed { get; } public bool PinionAxisFixed { get; } public bool GuidePresent { get; } public bool RackRotationFixed { get; } public bool TransverseMotionFixed { get; }
}
public sealed class SetRackTransmissionEdit : RackPinionEditOperation
{ public SetRackTransmissionEdit(string deviceId, bool present) { DeviceId = deviceId; Present = present; } public string DeviceId { get; } public bool Present { get; } }
public sealed class SetRackPinionBindingEdit : RackPinionEditOperation
{
    public SetRackPinionBindingEdit(string deviceId, string shaftId, string? portId) { DeviceId = deviceId; ShaftId = shaftId; PortId = portId; }
    public string DeviceId { get; } public string ShaftId { get; } public string? PortId { get; }
}
public sealed class SetRackTerminalEdit : RackPinionEditOperation
{ public SetRackTerminalEdit(string outputKey, int sign, ExactQuantity datum) { OutputKey = outputKey; Sign = sign; Datum = datum; } public string OutputKey { get; } public int Sign { get; } public ExactQuantity Datum { get; } }
public sealed class SetRackRequirementsEdit : RackPinionEditOperation
{ public SetRackRequirementsEdit(string outputKey, LinearOutputRequirement requirement) { OutputKey = outputKey; Requirement = requirement; } public string OutputKey { get; } public LinearOutputRequirement Requirement { get; } }

public sealed class RackPinionEditBatch
{
    public RackPinionEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<RackPinionEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > RackPinionProfile.MaxTotalOperations) throw new ArgumentException("Rack ordered operation bound exceeded.");
        foreach (var operation in Operations) RackPinionEditKeys.Validate(operation);
        RequestId = HashText(Pack("rack-pinion-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<RackPinionEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyRackSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
    public string RequestId { get; }
}

/// <summary>Per-operation invalidation information, not a claim that reanalysis passed. Nested results publish only with the whole transaction.</summary>
public sealed class RackPinionEditChange
{
    internal RackPinionEditChange(int operationIndex, RackPinionEditOperation operation)
    { OperationIndex = operationIndex; OperationKind = operation.Kind; DependentMetrics = RackPinionEditKeys.Dependencies(operation).ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}
public sealed class RackPinionEditResult
{
    internal RackPinionEditResult(RackPinionDraft before, RackPinionEditBatch batch, RackPinionDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft;
        BaseDraftId = before.DraftId; RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex;
        Diagnostics = diagnostics.ToList().AsReadOnly(); SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<RackPinionEditChange>() : batch.Operations.Select((o, i) => new RackPinionEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("rack-pinion-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public RackPinionDraft? Draft { get; }
    public string BaseDraftId { get; } public string RequestId { get; } public string ResultId { get; }
    public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<RackPinionEditChange> Changes { get; }
}

public static class RackPinionEditor
{
    public static RackPinionEditResult Apply(RackPinionDraft draft, RackPinionEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        RackPinionEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(draft, batch, null, index, diagnostics ?? new[] { new MechanicalDiagnostic(code, "RackPinionEdit", detail: detail) });
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId)
            return Reject("StaleRevision", null, "Both revision and definition must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null, "Revision bound exceeded.");
        var current = draft.Definition; var sourceResults = new List<MechanicalEditResult>();
        for (var i = 0; i < batch.Operations.Count; i++)
        {
            try
            {
                var op = batch.Operations[i]; var d = current.Device; var output = current.Output;
                var source = current.Source; var mapping = current.SourceMapping; var requirement = current.Requirement;
                void Device(string id) { if (id != d.Id) throw new ArgumentException("UnknownDeviceId: " + id); }
                void Output(string key) { if (key != output.Key) throw new ArgumentException("UnknownOutputId: " + key); }
                switch (op)
                {
                    case ApplyRackSourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source transaction rejected; no rack changes publish.", result.Diagnostics);
                        source = result.Draft; sourceResults.Add(result); break;
                    case SetRackSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetRackPinionTeethEdit e: Device(e.DeviceId); d = Copy(d, teeth: e.ToothCount); break;
                    case SetRackPinionCircularPitchEdit e: Device(e.DeviceId); d = Copy(d, cp: e.CircularPitch); break;
                    case SetRackPitchEdit e: Device(e.DeviceId); d = Copy(d, pitch: e.RackPitch); break;
                    case SetRackPinionCenterEdit e: Device(e.DeviceId); d = Copy(d, center: e.CenterMm); break;
                    case SetRackContactNormalEdit e: Device(e.DeviceId); d = Copy(d, normal: e.ContactNormal); break;
                    case SetRackGuidePlacementEdit e: Device(e.DeviceId); d = Copy(d, guide: e.GuideFrameMm, longitudinal: e.LongitudinalOffset); break;
                    case SetRackReferenceEdit e: Device(e.DeviceId); d = Copy(d, pinionReference: e.PinionReferenceTurns, rackReference: e.RackReferencePosition); break;
                    case SetRackActiveMaterialIntervalEdit e: Device(e.DeviceId); d = Copy(d, material: e.Interval); break;
                    case SetRackGuideIntervalEdit e: Device(e.DeviceId); d = Copy(d, guideInterval: e.Interval); break;
                    case SetRackGroundingEdit e: Device(e.DeviceId); d = Copy(d, centerFixed: e.PinionCenterFixed, axisFixed: e.PinionAxisFixed, guidePresent: e.GuidePresent, rackFixed: e.RackRotationFixed, transverse: e.TransverseMotionFixed); break;
                    case SetRackTransmissionEdit e: Device(e.DeviceId); d = Copy(d, present: e.Present); break;
                    case SetRackPinionBindingEdit e: Device(e.DeviceId); d = Copy(d, shaft: e.ShaftId, port: e.PortId, replacePort: true); break;
                    case SetRackTerminalEdit e: Output(e.OutputKey); output = new(output.Key, output.LinearDofId, output.BodyId, output.ReferencePointId, e.Sign, e.Datum); break;
                    case SetRackRequirementsEdit e: Output(e.OutputKey); requirement = e.Requirement; break;
                    default: throw new ArgumentException("Unsupported typed rack edit operation.");
                }
                current = new RackPinionDefinition(source, mapping, d, output, requirement, current.RequiredValidationDomains);
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, e.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sourceResults);
    }
    private static CircularPitchRackDefinition Copy(CircularPitchRackDefinition d, int? teeth = null, ExactQuantity? cp = null, ExactQuantity? pitch = null,
        ExactVector3? center = null, ExactVector3? normal = null, ExactPiFrame? guide = null, ExactQuantity? longitudinal = null,
        ExactQuantity? pinionReference = null, ExactQuantity? rackReference = null, ExactQuantityInterval? material = null, ExactQuantityInterval? guideInterval = null,
        bool? present = null, bool? centerFixed = null, bool? axisFixed = null, bool? guidePresent = null, bool? rackFixed = null, bool? transverse = null,
        string? shaft = null, string? port = null, bool replacePort = false) =>
        new(d.Id, shaft ?? d.PinionShaftId, d.PinionBodyId, d.RackBodyId, d.GuideId, d.LinearDofId, teeth ?? d.PinionToothCount,
            cp ?? d.PinionCircularPitch, pitch ?? d.RackPitch, center ?? d.PinionCenterMm, normal ?? d.ContactNormal, guide ?? d.GuideFrameMm,
            longitudinal ?? d.LongitudinalOffset, pinionReference ?? d.PinionReferenceTurns, rackReference ?? d.RackReferencePosition,
            material ?? d.ActiveMaterialInterval, guideInterval ?? d.GuideInterval, present ?? d.TransmissionPresent, centerFixed ?? d.PinionCenterFixed,
            axisFixed ?? d.PinionAxisFixed, guidePresent ?? d.GuidePresent, rackFixed ?? d.RackRotationFixed, transverse ?? d.TransverseMotionFixed,
            replacePort ? port : d.PinionPortId, d.Parameterization);
}

internal static class RackPinionEditKeys
{
    public static void Validate(RackPinionEditOperation o)
    {
        void Id(string id) => MechanicalAuthoringProfile.IdValue(id);
        switch (o)
        {
            case ApplyRackSourceEditsEdit _: break;
            case SetRackSourceLengthMappingEdit _: break;
            case SetRackPinionTeethEdit e: Id(e.DeviceId); break;
            case SetRackPinionCircularPitchEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.CircularPitch); break;
            case SetRackPitchEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.RackPitch); break;
            case SetRackPinionCenterEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.CenterMm); break;
            case SetRackContactNormalEdit e: Id(e.DeviceId); MechanicalAuthoringProfile.VectorBound(e.ContactNormal); break;
            case SetRackGuidePlacementEdit e: Id(e.DeviceId); RackPinionProfile.FrameBound(e.GuideFrameMm); RotaryLinearProfile.Quantity(e.LongitudinalOffset); break;
            case SetRackReferenceEdit e: Id(e.DeviceId); RotaryLinearProfile.Quantity(e.PinionReferenceTurns); RotaryLinearProfile.Quantity(e.RackReferencePosition); break;
            case SetRackActiveMaterialIntervalEdit e: Id(e.DeviceId); RotaryLinearProfile.Interval(e.Interval); break;
            case SetRackGuideIntervalEdit e: Id(e.DeviceId); RotaryLinearProfile.Interval(e.Interval); break;
            case SetRackGroundingEdit e: Id(e.DeviceId); break;
            case SetRackTransmissionEdit e: Id(e.DeviceId); break;
            case SetRackPinionBindingEdit e: Id(e.DeviceId); Id(e.ShaftId); if (e.PortId is not null) Id(e.PortId); break;
            case SetRackTerminalEdit e: Id(e.OutputKey); RotaryLinearProfile.Quantity(e.Datum); break;
            case SetRackRequirementsEdit e: Id(e.OutputKey); if (e.Requirement is null) throw new ArgumentNullException(nameof(e.Requirement)); break;
            default: throw new ArgumentException("Unsupported typed rack edit.");
        }
    }
    public static string Operation(RackPinionEditOperation o)
    {
        string value = o switch
        {
            ApplyRackSourceEditsEdit e => e.Batch.RequestId,
            SetRackSourceLengthMappingEdit e => e.Mapping?.CanonicalRepresentation ?? "",
            SetRackPinionTeethEdit e => Pack(e.DeviceId, N(e.ToothCount)),
            SetRackPinionCircularPitchEdit e => Pack(e.DeviceId, RotaryLinearProfile.Q(e.CircularPitch)),
            SetRackPitchEdit e => Pack(e.DeviceId, RotaryLinearProfile.Q(e.RackPitch)),
            SetRackPinionCenterEdit e => Pack(e.DeviceId, V(e.CenterMm)),
            SetRackContactNormalEdit e => Pack(e.DeviceId, V(e.ContactNormal)),
            SetRackGuidePlacementEdit e => Pack(e.DeviceId, RackPinionProfile.PiFrame(e.GuideFrameMm), RotaryLinearProfile.Q(e.LongitudinalOffset)),
            SetRackReferenceEdit e => Pack(e.DeviceId, RotaryLinearProfile.Q(e.PinionReferenceTurns), RotaryLinearProfile.Q(e.RackReferencePosition)),
            SetRackActiveMaterialIntervalEdit e => Pack(e.DeviceId, RotaryLinearProfile.I(e.Interval)),
            SetRackGuideIntervalEdit e => Pack(e.DeviceId, RotaryLinearProfile.I(e.Interval)),
            SetRackGroundingEdit e => Pack(e.DeviceId, RotaryLinearProfile.Flag(e.PinionCenterFixed), RotaryLinearProfile.Flag(e.PinionAxisFixed), RotaryLinearProfile.Flag(e.GuidePresent), RotaryLinearProfile.Flag(e.RackRotationFixed), RotaryLinearProfile.Flag(e.TransverseMotionFixed)),
            SetRackTransmissionEdit e => Pack(e.DeviceId, RotaryLinearProfile.Flag(e.Present)),
            SetRackPinionBindingEdit e => Pack(e.DeviceId, e.ShaftId, e.PortId ?? ""),
            SetRackTerminalEdit e => Pack(e.OutputKey, N(e.Sign), RotaryLinearProfile.Q(e.Datum)),
            SetRackRequirementsEdit e => Pack(e.OutputKey, e.Requirement.CanonicalRepresentation),
            _ => throw new ArgumentException("Unsupported typed rack edit.")
        };
        return Pack(o.Kind, value);
    }
    public static IEnumerable<string> Dependencies(RackPinionEditOperation o) => o switch
    {
        SetRackPinionTeethEdit _ or SetRackPinionCircularPitchEdit _ => new[] { "PitchRadius", "PitchCompatibility", "Tangency", "LinearLaw", "WorldPath", "ActualRootInterval", "OutputRequirements", "RequiredInputDomain" },
        SetRackPitchEdit _ => new[] { "PitchCompatibility", "TransmissionAdmission", "LinearLaw", "ActualRootInterval" },
        SetRackActiveMaterialIntervalEdit _ or SetRackGuideIntervalEdit _ => new[] { "ActualRootInterval", "RequiredInputDomain" },
        SetRackTerminalEdit _ => new[] { "TerminalLaw", "OutputRequirements" },
        SetRackRequirementsEdit _ => new[] { "OutputRequirements", "RequiredInputDomain" },
        _ => new[] { "LocalCompatibility", "Connectivity", "LinearLaw", "WorldPath", "ActualRootInterval", "OutputRequirements", "RequiredInputDomain" }
    };
}
