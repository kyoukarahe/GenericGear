using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Ordered typed changes. Source and added-device changes publish together or not at all.</summary>
public abstract class RotaryLinearEditOperation
{
    private protected RotaryLinearEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => RotaryLinearEditKeys.Operation(this);
}
public sealed class ApplyRotarySourceEditsEdit : RotaryLinearEditOperation
{ public ApplyRotarySourceEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); } public MechanicalEditBatch Batch { get; } }
public sealed class SetSourceLengthMappingEdit : RotaryLinearEditOperation
{ public SetSourceLengthMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; } public SourceLengthMapping? Mapping { get; } }
public sealed class SetLeadScrewLeadEdit : RotaryLinearEditOperation
{ public SetLeadScrewLeadEdit(string deviceId, ExactQuantity lead) { DeviceId = deviceId; Lead = lead; } public string DeviceId { get; } public ExactQuantity Lead { get; } }
public sealed class SetLeadScrewHandednessEdit : RotaryLinearEditOperation
{ public SetLeadScrewHandednessEdit(string deviceId, int handedness) { DeviceId = deviceId; Handedness = handedness; } public string DeviceId { get; } public int Handedness { get; } }
public sealed class SetLeadScrewReferenceEdit : RotaryLinearEditOperation
{
    public SetLeadScrewReferenceEdit(string deviceId, ExactQuantity screwReferenceTurns, ExactQuantity nutReferencePosition, ExactVector3 screwAxialDatumMm)
    { DeviceId = deviceId; ScrewReferenceTurns = screwReferenceTurns; NutReferencePosition = nutReferencePosition; ScrewAxialDatumMm = screwAxialDatumMm; }
    public string DeviceId { get; } public ExactQuantity ScrewReferenceTurns { get; } public ExactQuantity NutReferencePosition { get; } public ExactVector3 ScrewAxialDatumMm { get; }
}
public sealed class SetLeadScrewGuideEdit : RotaryLinearEditOperation
{
    public SetLeadScrewGuideEdit(string deviceId, OrientedFrame guideFrameMm, ExactQuantityInterval guideInterval, ExactQuantityInterval engagementInterval)
    { DeviceId = deviceId; GuideFrameMm = guideFrameMm; GuideInterval = guideInterval; EngagementInterval = engagementInterval; }
    public string DeviceId { get; } public OrientedFrame GuideFrameMm { get; } public ExactQuantityInterval GuideInterval { get; } public ExactQuantityInterval EngagementInterval { get; }
}
public sealed class SetLeadScrewGroundingEdit : RotaryLinearEditOperation
{
    public SetLeadScrewGroundingEdit(string deviceId, bool screwAxiallyFixed, bool guidePresent, bool nutRotationFixed, bool transverseMotionFixed)
    { DeviceId = deviceId; ScrewAxiallyFixed = screwAxiallyFixed; GuidePresent = guidePresent; NutRotationFixed = nutRotationFixed; TransverseMotionFixed = transverseMotionFixed; }
    public string DeviceId { get; } public bool ScrewAxiallyFixed { get; } public bool GuidePresent { get; } public bool NutRotationFixed { get; } public bool TransverseMotionFixed { get; }
}
public sealed class SetLeadScrewTransmissionEdit : RotaryLinearEditOperation
{ public SetLeadScrewTransmissionEdit(string deviceId, bool present) { DeviceId = deviceId; Present = present; } public string DeviceId { get; } public bool Present { get; } }
public sealed class SetLeadScrewBindingEdit : RotaryLinearEditOperation
{
    public SetLeadScrewBindingEdit(string deviceId, string shaftId, string? portId, ExactVector3 physicalAxis)
    { DeviceId = deviceId; ShaftId = shaftId; PortId = portId; PhysicalAxis = physicalAxis; }
    public string DeviceId { get; } public string ShaftId { get; } public string? PortId { get; } public ExactVector3 PhysicalAxis { get; }
}
public sealed class SetLinearTerminalEdit : RotaryLinearEditOperation
{ public SetLinearTerminalEdit(string outputKey, int sign, ExactQuantity datum) { OutputKey = outputKey; Sign = sign; Datum = datum; } public string OutputKey { get; } public int Sign { get; } public ExactQuantity Datum { get; } }
public sealed class SetLinearRequirementsEdit : RotaryLinearEditOperation
{ public SetLinearRequirementsEdit(string outputKey, LinearOutputRequirement requirement) { OutputKey = outputKey; Requirement = requirement; } public string OutputKey { get; } public LinearOutputRequirement Requirement { get; } }

public sealed class RotaryLinearEditBatch
{
    public RotaryLinearEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<RotaryLinearEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > 512) throw new ArgumentException("Mixed ordered operation bound exceeded.");
        foreach (var operation in Operations) RotaryLinearEditKeys.Validate(operation);
        RequestId = HashText(Pack("rotary-linear-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId,
            Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<RotaryLinearEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyRotarySourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
    public string RequestId { get; }
}

public sealed class RotaryLinearEditResult
{
    internal RotaryLinearEditResult(RotaryLinearDraft before, RotaryLinearEditBatch batch, RotaryLinearDraft? draft,
        int? failingOperationIndex, IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied;
        Draft = draft; BaseDraftId = before.DraftId; RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex;
        Diagnostics = diagnostics.ToList().AsReadOnly(); SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("rotary-linear-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public RotaryLinearDraft? Draft { get; }
    public string BaseDraftId { get; } public string RequestId { get; } public string ResultId { get; }
    public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
}

public static class RotaryLinearEditor
{
    public static RotaryLinearEditResult Apply(RotaryLinearDraft draft, RotaryLinearEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        RotaryLinearEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(draft, batch, null, index, diagnostics ?? new[] { new MechanicalDiagnostic(code, "MixedEdit", detail: detail) });
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
                    case ApplyRotarySourceEditsEdit e:
                        var result = MechanicalEditor.Apply(source, e.Batch);
                        if (result.Draft is null) return Reject("SourceEditRejected", i, "Nested source transaction rejected; no mixed changes publish.", result.Diagnostics);
                        source = result.Draft; sourceResults.Add(result); break;
                    case SetSourceLengthMappingEdit e: mapping = e.Mapping; break;
                    case SetLeadScrewLeadEdit e: Device(e.DeviceId); d = Copy(d, lead: e.Lead); break;
                    case SetLeadScrewHandednessEdit e: Device(e.DeviceId); d = Copy(d, handedness: e.Handedness); break;
                    case SetLeadScrewReferenceEdit e: Device(e.DeviceId); d = Copy(d, screwReference: e.ScrewReferenceTurns, nutReference: e.NutReferencePosition, datum: e.ScrewAxialDatumMm); break;
                    case SetLeadScrewGuideEdit e: Device(e.DeviceId); d = Copy(d, guide: e.GuideFrameMm, guideInterval: e.GuideInterval, engagementInterval: e.EngagementInterval); break;
                    case SetLeadScrewGroundingEdit e: Device(e.DeviceId); d = Copy(d, axial: e.ScrewAxiallyFixed, guidePresent: e.GuidePresent, nutFixed: e.NutRotationFixed, transverse: e.TransverseMotionFixed); break;
                    case SetLeadScrewTransmissionEdit e: Device(e.DeviceId); d = Copy(d, present: e.Present); break;
                    case SetLeadScrewBindingEdit e: Device(e.DeviceId); d = Copy(d, shaft: e.ShaftId, port: e.PortId, replacePort: true, axis: e.PhysicalAxis); break;
                    case SetLinearTerminalEdit e: Output(e.OutputKey); output = new(output.Key, output.LinearDofId, output.NutBodyId, output.ReferencePointId, e.Sign, e.Datum); break;
                    case SetLinearRequirementsEdit e: Output(e.OutputKey); requirement = e.Requirement; break;
                    default: throw new ArgumentException("Unsupported typed mixed edit operation.");
                }
                current = new RotaryLinearDefinition(source, mapping, d, output, requirement, current.RequiredValidationDomains);
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown", StringComparison.Ordinal) ? "UnknownIdentifier" : "InvalidOperationInput", i, e.Message); }
        }
        return new(draft, batch, draft.WithDefinition(current), null, Array.Empty<MechanicalDiagnostic>(), sourceResults);
    }
    private static GroundedLeadScrewDefinition Copy(GroundedLeadScrewDefinition d, ExactQuantity? lead = null, int? handedness = null,
        ExactQuantity? screwReference = null, ExactQuantity? nutReference = null, ExactVector3? datum = null,
        OrientedFrame? guide = null, ExactQuantityInterval? guideInterval = null, ExactQuantityInterval? engagementInterval = null,
        bool? present = null, bool? axial = null, bool? guidePresent = null, bool? nutFixed = null, bool? transverse = null,
        string? shaft = null, string? port = null, bool replacePort = false, ExactVector3? axis = null) =>
        new(d.Id, shaft ?? d.ScrewShaftId, d.ScrewBodyId, d.NutBodyId, d.GuideId, d.LinearDofId, axis ?? d.PhysicalAxis,
            datum ?? d.ScrewAxialDatumMm, guide ?? d.GuideFrameMm, lead ?? d.Lead, screwReference ?? d.ScrewReferenceTurns,
            nutReference ?? d.NutReferencePosition, guideInterval ?? d.GuideInterval, engagementInterval ?? d.EngagementInterval,
            handedness ?? d.Handedness, present ?? d.TransmissionPresent, axial ?? d.ScrewAxiallyFixed, guidePresent ?? d.GuidePresent,
            nutFixed ?? d.NutRotationFixed, transverse ?? d.TransverseMotionFixed, replacePort ? port : d.ScrewPortId);
}

internal static class RotaryLinearEditKeys
{
    public static void Validate(RotaryLinearEditOperation operation)
    {
        switch (operation)
        {
            case ApplyRotarySourceEditsEdit _: break;
            case SetSourceLengthMappingEdit _: break;
            case SetLeadScrewLeadEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); RotaryLinearProfile.Quantity(e.Lead); break;
            case SetLeadScrewHandednessEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); break;
            case SetLeadScrewReferenceEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); RotaryLinearProfile.Quantity(e.ScrewReferenceTurns); RotaryLinearProfile.Quantity(e.NutReferencePosition); MechanicalAuthoringProfile.VectorBound(e.ScrewAxialDatumMm); break;
            case SetLeadScrewGuideEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); MechanicalAuthoringProfile.FrameBound(e.GuideFrameMm); RotaryLinearProfile.Interval(e.GuideInterval); RotaryLinearProfile.Interval(e.EngagementInterval); break;
            case SetLeadScrewGroundingEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); break;
            case SetLeadScrewTransmissionEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); break;
            case SetLeadScrewBindingEdit e: MechanicalAuthoringProfile.IdValue(e.DeviceId); MechanicalAuthoringProfile.IdValue(e.ShaftId); if (e.PortId is not null) MechanicalAuthoringProfile.IdValue(e.PortId); MechanicalAuthoringProfile.VectorBound(e.PhysicalAxis); break;
            case SetLinearTerminalEdit e: MechanicalAuthoringProfile.IdValue(e.OutputKey); RotaryLinearProfile.Quantity(e.Datum); break;
            case SetLinearRequirementsEdit e: MechanicalAuthoringProfile.IdValue(e.OutputKey); if (e.Requirement is null) throw new ArgumentNullException(nameof(e.Requirement)); break;
            default: throw new ArgumentException("Unsupported typed mixed edit.");
        }
    }
    private static string Q(ExactQuantity q) => Pack(q.Kind.ToString(), q.Unit, F(q.Value));
    private static string I(ExactQuantityInterval i) => Pack(Q(i.Lower), Q(i.Upper));
    private static string B(bool b) => b ? "1" : "0";
    public static string Operation(RotaryLinearEditOperation o)
    {
        string value = o switch
        {
            ApplyRotarySourceEditsEdit e => e.Batch.RequestId,
            SetSourceLengthMappingEdit e => e.Mapping is null ? "" : Pack(F(e.Mapping.MillimetersPerSourceUnit), Frame(e.Mapping.PoseMm)),
            SetLeadScrewLeadEdit e => Pack(e.DeviceId, Q(e.Lead)),
            SetLeadScrewHandednessEdit e => Pack(e.DeviceId, N(e.Handedness)),
            SetLeadScrewReferenceEdit e => Pack(e.DeviceId, Q(e.ScrewReferenceTurns), Q(e.NutReferencePosition), V(e.ScrewAxialDatumMm)),
            SetLeadScrewGuideEdit e => Pack(e.DeviceId, Frame(e.GuideFrameMm), I(e.GuideInterval), I(e.EngagementInterval)),
            SetLeadScrewGroundingEdit e => Pack(e.DeviceId, B(e.ScrewAxiallyFixed), B(e.GuidePresent), B(e.NutRotationFixed), B(e.TransverseMotionFixed)),
            SetLeadScrewTransmissionEdit e => Pack(e.DeviceId, B(e.Present)),
            SetLeadScrewBindingEdit e => Pack(e.DeviceId, e.ShaftId, e.PortId ?? "", V(e.PhysicalAxis)),
            SetLinearTerminalEdit e => Pack(e.OutputKey, N(e.Sign), Q(e.Datum)),
            SetLinearRequirementsEdit e => Pack(e.OutputKey, e.Requirement.RequiredGain is null ? "" : Q(e.Requirement.RequiredGain.Value),
                e.Requirement.RequiredReferencePosition is null ? "" : Q(e.Requirement.RequiredReferencePosition.Value), I(e.Requirement.RequiredRootInterval)),
            _ => throw new ArgumentException("Unsupported typed mixed edit.")
        };
        return Pack(o.Kind, value);
    }
}
