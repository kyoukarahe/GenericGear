using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class GenevaEditOperation
{
    private protected GenevaEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => GenevaEditKeys.Operation(this);
}
public sealed class ApplyGenevaSourceEditsEdit : GenevaEditOperation
{
    public ApplyGenevaSourceEditsEdit(MechanicalEditBatch batch)
    { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); }
    public MechanicalEditBatch Batch { get; }
}
public sealed class SetGenevaSourceLengthMappingEdit : GenevaEditOperation
{
    public SetGenevaSourceLengthMappingEdit(SourceLengthMapping? mapping)
    { Mapping = mapping; }
    public SourceLengthMapping? Mapping { get; }
}
public sealed class SetGenevaOrbitEdit : GenevaEditOperation
{
    public SetGenevaOrbitEdit(string deviceId, GenevaLength orbitRadius)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; OrbitRadius = orbitRadius ?? throw new ArgumentNullException(nameof(orbitRadius)); }
    public string DeviceId { get; } public GenevaLength OrbitRadius { get; }
}
public sealed class SetGenevaWheelEdit : GenevaEditOperation
{
    public SetGenevaWheelEdit(string deviceId, GenevaWheelSpecification wheel)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; Wheel = wheel ?? throw new ArgumentNullException(nameof(wheel)); }
    public string DeviceId { get; } public GenevaWheelSpecification Wheel { get; }
}
public sealed class SetGenevaSlotRootEdit : GenevaEditOperation
{
    public SetGenevaSlotRootEdit(string deviceId, ExactQuantity slotRoot)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; RotaryLinearProfile.Quantity(slotRoot); SlotRoot = slotRoot; }
    public string DeviceId { get; } public ExactQuantity SlotRoot { get; }
}
public sealed class SetGenevaSlotMouthEdit : GenevaEditOperation
{
    public SetGenevaSlotMouthEdit(string deviceId, GenevaLength mouthRadius)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; MouthRadius = mouthRadius ?? throw new ArgumentNullException(nameof(mouthRadius)); }
    public string DeviceId { get; } public GenevaLength MouthRadius { get; }
}
public sealed class SetGenevaDriverCenterEdit : GenevaEditOperation
{
    public SetGenevaDriverCenterEdit(string deviceId, ExactVector3 centerMm, ExactQuantity station)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; MechanicalAuthoringProfile.VectorBound(centerMm); CenterMm = centerMm; RotaryLinearProfile.Quantity(station); Station = station; }
    public string DeviceId { get; } public ExactVector3 CenterMm { get; } public ExactQuantity Station { get; }
}
public sealed class SetGenevaOutputPlacementEdit : GenevaEditOperation
{
    public SetGenevaOutputPlacementEdit(string deviceId, OrientedFrame frameMm, ExactVector3 centerMm, ExactQuantity station)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; MechanicalAuthoringProfile.FrameBound(frameMm); FrameMm = frameMm; MechanicalAuthoringProfile.VectorBound(centerMm); CenterMm = centerMm; RotaryLinearProfile.Quantity(station); Station = station; }
    public string DeviceId { get; } public OrientedFrame FrameMm { get; } public ExactVector3 CenterMm { get; } public ExactQuantity Station { get; }
}
public sealed class SetGenevaDriverMountingPhaseEdit : GenevaEditOperation
{
    public SetGenevaDriverMountingPhaseEdit(string deviceId, ExactQuantity mountingTurns)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; RotaryLinearProfile.Quantity(mountingTurns); MountingTurns = mountingTurns; }
    public string DeviceId { get; } public ExactQuantity MountingTurns { get; }
}
public sealed class SetGenevaWheelMountingPhaseEdit : GenevaEditOperation
{
    public SetGenevaWheelMountingPhaseEdit(string deviceId, ExactQuantity mountingTurns)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; RotaryLinearProfile.Quantity(mountingTurns); MountingTurns = mountingTurns; }
    public string DeviceId { get; } public ExactQuantity MountingTurns { get; }
}
public sealed class SetGenevaRegistrationEdit : GenevaEditOperation
{
    public SetGenevaRegistrationEdit(string deviceId, ExactQuantity referenceTurns, int registrationSlot)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; RotaryLinearProfile.Quantity(referenceTurns); ReferenceTurns = referenceTurns; RegistrationSlot = registrationSlot; }
    public string DeviceId { get; } public ExactQuantity ReferenceTurns { get; } public int RegistrationSlot { get; }
}
public sealed class SetGenevaLockEdit : GenevaEditOperation
{
    public SetGenevaLockEdit(string deviceId, GenevaIdealLockSpecification idealLock)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; IdealLock = idealLock ?? throw new ArgumentNullException(nameof(idealLock)); }
    public string DeviceId { get; } public GenevaIdealLockSpecification IdealLock { get; }
}
public sealed class SetGenevaLockWindowEdit : GenevaEditOperation
{
    public SetGenevaLockWindowEdit(string deviceId, ExactQuantityInterval releaseWindow)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; RotaryLinearProfile.Interval(releaseWindow); ReleaseWindow = releaseWindow; }
    public string DeviceId { get; } public ExactQuantityInterval ReleaseWindow { get; }
}
public sealed class SetGenevaLockPresenceEdit : GenevaEditOperation
{
    public SetGenevaLockPresenceEdit(string deviceId, bool present)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; Present = present; }
    public string DeviceId { get; } public bool Present { get; }
}
public sealed class SetGenevaPinPresenceEdit : GenevaEditOperation
{
    public SetGenevaPinPresenceEdit(string deviceId, bool present)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; Present = present; }
    public string DeviceId { get; } public bool Present { get; }
}
public sealed class SetGenevaTerminalEdit : GenevaEditOperation
{
    public SetGenevaTerminalEdit(string outputKey, int sign, ExactQuantity datum)
    { MechanicalAuthoringProfile.IdValue(outputKey); OutputKey = outputKey; Sign = sign; RotaryLinearProfile.Quantity(datum); Datum = datum; }
    public string OutputKey { get; } public int Sign { get; } public ExactQuantity Datum { get; }
}
public sealed class ReverseGenevaTerminalEdit : GenevaEditOperation
{
    public ReverseGenevaTerminalEdit(string outputKey)
    { MechanicalAuthoringProfile.IdValue(outputKey); OutputKey = outputKey; }
    public string OutputKey { get; }
}
public sealed class ReverseGenevaOutputAxisEdit : GenevaEditOperation
{
    public ReverseGenevaOutputAxisEdit(string deviceId)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; }
    public string DeviceId { get; }
}
public sealed class SetGenevaRequirementsEdit : GenevaEditOperation
{
    public SetGenevaRequirementsEdit(string outputKey, GenevaRequirement requirement)
    { MechanicalAuthoringProfile.IdValue(outputKey); OutputKey = outputKey; Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement)); }
    public string OutputKey { get; } public GenevaRequirement Requirement { get; }
}
public sealed class SetGenevaBindingEdit : GenevaEditOperation
{
    public SetGenevaBindingEdit(string deviceId, string sourceShaftId, string? sourcePortId)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; MechanicalAuthoringProfile.IdValue(sourceShaftId); SourceShaftId = sourceShaftId; if (sourcePortId is not null) MechanicalAuthoringProfile.IdValue(sourcePortId); SourcePortId = sourcePortId; }
    public string DeviceId { get; } public string SourceShaftId { get; } public string? SourcePortId { get; }
}
public sealed class SetGenevaUnsupportedModeEdit : GenevaEditOperation
{
    public SetGenevaUnsupportedModeEdit(string deviceId, string mechanismKind, string pinKind, int pinCount, string slotKind, bool outputIsPrescribed)
    { MechanicalAuthoringProfile.IdValue(deviceId); DeviceId = deviceId; MechanicalAuthoringProfile.IdValue(mechanismKind); MechanismKind = mechanismKind; MechanicalAuthoringProfile.IdValue(pinKind); PinKind = pinKind; PinCount = pinCount; MechanicalAuthoringProfile.IdValue(slotKind); SlotKind = slotKind; OutputIsPrescribed = outputIsPrescribed; }
    public string DeviceId { get; } public string MechanismKind { get; } public string PinKind { get; } public int PinCount { get; } public string SlotKind { get; } public bool OutputIsPrescribed { get; }
}

public sealed class SetGenevaSlotCountEdit : GenevaEditOperation
{
    public SetGenevaSlotCountEdit(string deviceId, int slotCount, IEnumerable<string>? addedSlotIds = null, IEnumerable<string>? addedRecessIds = null)
    { DeviceId = MechanicalAuthoringProfile.IdValue(deviceId); SlotCount = slotCount; AddedSlotIds = addedSlotIds is null ? null : GenevaProfile.OrderedIds(addedSlotIds); AddedRecessIds = addedRecessIds is null ? null : GenevaProfile.OrderedIds(addedRecessIds); }
    public string DeviceId { get; } public int SlotCount { get; } public ReadOnlyCollection<string>? AddedSlotIds { get; } public ReadOnlyCollection<string>? AddedRecessIds { get; }
}
public sealed class SetGenevaRequiredValidationDomainsEdit : GenevaEditOperation
{
    public SetGenevaRequiredValidationDomainsEdit(IEnumerable<string> domains) { Domains = MechanicalAuthoringProfile.Set(domains,s=>s,32); }
    public ReadOnlyCollection<string> Domains { get; }
}
public sealed class GenevaEditBatch
{
    public GenevaEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<GenevaEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(129).ToList().AsReadOnly();
        if (Operations.Count > 128 || Operations.Any(o => o is null) || OperationCount > GenevaProfile.MaxTotalOperations) throw new ArgumentException("Geneva ordered operation bound exceeded.");
        foreach (var op in Operations) _ = op.CanonicalRepresentation;
        RequestId = HashText(Pack("geneva-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId, Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; } public string ExpectedDefinitionId { get; } public string RequestId { get; }
    public ReadOnlyCollection<GenevaEditOperation> Operations { get; }
    public int OperationCount => Operations.Sum(o => o is ApplyGenevaSourceEditsEdit e ? Math.Max(1, e.Batch.Operations.Count) : 1);
}
public sealed class GenevaEditChange
{
    internal GenevaEditChange(int index, GenevaEditOperation operation)
    { OperationIndex = index; OperationKind = operation.Kind; DependentMetrics = new[] { "CurrentDefinition", "TangentEntryGeometry", "PinSlotAndIdealLock", "RegistrationAndUnwrappedMotion", "RequirementsAndExport" }.ToList().AsReadOnly(); }
    public int OperationIndex { get; } public string OperationKind { get; } public ReadOnlyCollection<string> DependentMetrics { get; }
}
public sealed class GenevaEditResult
{
    internal GenevaEditResult(GenevaDraft before, GenevaEditBatch batch, GenevaDraft? draft, int? failingOperationIndex,
        IEnumerable<MechanicalDiagnostic> diagnostics, IEnumerable<MechanicalEditResult>? sourceResults = null)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied; Draft = draft; BaseDraftId = before.DraftId;
        RequestId = batch.RequestId; FailingOperationIndex = failingOperationIndex; Diagnostics = diagnostics.ToList().AsReadOnly();
        SourceResults = (draft is null ? Array.Empty<MechanicalEditResult>() : sourceResults ?? Array.Empty<MechanicalEditResult>()).ToList().AsReadOnly();
        Changes = (draft is null ? Array.Empty<GenevaEditChange>() : batch.Operations.Select((o, i) => new GenevaEditChange(i, o))).ToList().AsReadOnly();
        IsNetEmpty = draft is null || draft.DefinitionId == before.DefinitionId;
        ResultId = HashText(Pack("geneva-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(d => d.Code).ToArray()), Pack(SourceResults.Select(r => r.ResultId).ToArray())));
    }
    public MechanicalEditStatus Status { get; } public GenevaDraft? Draft { get; } public string BaseDraftId { get; }
    public string RequestId { get; } public string ResultId { get; } public int? FailingOperationIndex { get; } public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<GenevaEditChange> Changes { get; }
}


public static class GenevaEditor
{
    public static GenevaEditResult Apply(GenevaDraft draft, GenevaEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        GenevaEditResult Reject(string code, int? index, string detail, IEnumerable<MechanicalDiagnostic>? issues = null) =>
            new(draft,batch,null,index,issues ?? new[] {new MechanicalDiagnostic(code,"GenevaEdit",detail:detail)});
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId) return Reject("StaleRevision",null,"Exact revision and definition required; no partial publication.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit",null,"Revision bound exceeded.");
        var current = draft.Definition; var sources = new List<MechanicalEditResult>();
        for (var i=0;i<batch.Operations.Count;i++)
        {
            try
            {
                var d=current.Device; var output=current.Output; var source=current.Source; var mapping=current.SourceMapping;
                var requirement=current.Requirement; IEnumerable<string> domains=current.RequiredValidationDomains;
                void Device(string id) { if(id!=d.Id) throw new ArgumentException("UnknownDeviceId: "+id); }
                void Output(string id) { if(id!=output.Key) throw new ArgumentException("UnknownOutputId: "+id); }
                switch(batch.Operations[i])
                {
                    case ApplyGenevaSourceEditsEdit e:
                        var result=MechanicalEditor.Apply(source,e.Batch);
                        if(result.Draft is null) return Reject("SourceEditRejected",i,"Entire source/device batch rejected.",result.Diagnostics);
                        source=result.Draft;sources.Add(result);break;
                    case SetGenevaSourceLengthMappingEdit e: mapping=e.Mapping;break;
                    case SetGenevaSlotCountEdit e:
                        Device(e.DeviceId);
                        if(e.SlotCount<1 || e.SlotCount>64) throw new ArgumentException("Bounded material inventory requires 1..64 slots.");
                        d=Copy(d,wheel:new(e.SlotCount,d.Wheel.MouthRadius,d.Wheel.SlotRoot,ResizeIds(d.Wheel.SlotIds,e.SlotCount,e.AddedSlotIds,"geneva-slot-"+HashText(d.WheelBodyId).Substring(0,16)),d.Wheel.SlotKind),
                            idealLock:CopyLock(d.IdealLock,recessIds:ResizeIds(d.IdealLock.RecessIds,e.SlotCount,e.AddedRecessIds,"geneva-recess-"+HashText(d.WheelBodyId).Substring(0,16))));break;
                    case SetGenevaOrbitEdit e: Device(e.DeviceId);d=Copy(d,orbit:e.OrbitRadius);break;
                    case SetGenevaWheelEdit e: Device(e.DeviceId);d=Copy(d,wheel:e.Wheel);break;
                    case SetGenevaSlotRootEdit e: Device(e.DeviceId);d=Copy(d,wheel:new(d.Wheel.SlotCount,d.Wheel.MouthRadius,e.SlotRoot,d.Wheel.SlotIds,d.Wheel.SlotKind));break;
                    case SetGenevaSlotMouthEdit e: Device(e.DeviceId);d=Copy(d,wheel:new(d.Wheel.SlotCount,e.MouthRadius,d.Wheel.SlotRoot,d.Wheel.SlotIds,d.Wheel.SlotKind));break;
                    case SetGenevaDriverCenterEdit e: Device(e.DeviceId);d=Copy(d,driverCenter:e.CenterMm,driverStation:e.Station);break;
                    case SetGenevaOutputPlacementEdit e: Device(e.DeviceId);d=Copy(d,outputShaft:new(d.OutputShaft.Id,e.FrameMm,d.OutputShaft.IsPrescribed),wheelCenter:e.CenterMm,wheelStation:e.Station);break;
                    case SetGenevaDriverMountingPhaseEdit e: Device(e.DeviceId);d=Copy(d,driverMounting:e.MountingTurns);break;
                    case SetGenevaWheelMountingPhaseEdit e: Device(e.DeviceId);d=Copy(d,wheelMounting:e.MountingTurns);break;
                    case SetGenevaRegistrationEdit e: Device(e.DeviceId);d=Copy(d,reference:e.ReferenceTurns,registration:e.RegistrationSlot);break;
                    case SetGenevaLockEdit e: Device(e.DeviceId);d=Copy(d,idealLock:e.IdealLock);break;
                    case SetGenevaLockWindowEdit e: Device(e.DeviceId);d=Copy(d,idealLock:CopyLock(d.IdealLock,window:e.ReleaseWindow));break;
                    case SetGenevaLockPresenceEdit e: Device(e.DeviceId);d=Copy(d,idealLock:CopyLock(d.IdealLock,present:e.Present));break;
                    case SetGenevaPinPresenceEdit e: Device(e.DeviceId);d=Copy(d,pinPresent:e.Present);break;
                    case SetGenevaTerminalEdit e: Output(e.OutputKey);output=new(output.Key,output.ShaftId,output.BodyId,e.Sign,e.Datum);break;
                    case ReverseGenevaTerminalEdit e: Output(e.OutputKey);output=new(output.Key,output.ShaftId,output.BodyId,checked(-output.TerminalSign),output.TerminalDatum);break;
                    case ReverseGenevaOutputAxisEdit e:
                        Device(e.DeviceId);var frame=d.OutputShaft.Frame;
                        d=Copy(d,outputShaft:new(d.OutputShaft.Id,new OrientedFrame(frame.Origin,frame.X,-frame.Y,-frame.Z),d.OutputShaft.IsPrescribed),
                            wheelStation:new ExactQuantity(d.WheelStation.Kind,-d.WheelStation.Value,d.WheelStation.Unit),reference:new ExactQuantity(d.OutputReferenceTurns.Kind,-d.OutputReferenceTurns.Value,d.OutputReferenceTurns.Unit),
                            wheelMounting:new ExactQuantity(d.WheelMountingTurns.Kind,-d.WheelMountingTurns.Value,d.WheelMountingTurns.Unit));
                        output=new(output.Key,output.ShaftId,output.BodyId,checked(-output.TerminalSign),output.TerminalDatum);break;
                    case SetGenevaRequirementsEdit e: Output(e.OutputKey);requirement=e.Requirement;break;
                    case SetGenevaRequiredValidationDomainsEdit e: domains=e.Domains;break;
                    case SetGenevaBindingEdit e:
                        Device(e.DeviceId);
                        if(!source.Definition.Shafts.Any(s=>s.Id==e.SourceShaftId) || (e.SourcePortId is not null && !source.Definition.Ports.Any(p=>p.Id==e.SourcePortId))) throw new ArgumentException("UnknownSourceBinding");
                        d=Copy(d,sourceShaft:e.SourceShaftId,sourcePort:e.SourcePortId,replaceSourcePort:true);break;
                    case SetGenevaUnsupportedModeEdit e: Device(e.DeviceId);d=Copy(d,mechanismKind:e.MechanismKind,pinKind:e.PinKind,pinCount:e.PinCount,
                        wheel:new(d.Wheel.SlotCount,d.Wheel.MouthRadius,d.Wheel.SlotRoot,d.Wheel.SlotIds,e.SlotKind),outputShaft:new(d.OutputShaft.Id,d.OutputShaft.Frame,e.OutputIsPrescribed));break;
                    default: throw new ArgumentException("Unsupported Geneva edit.");
                }
                current=new(source,mapping,d,output,requirement,domains);
            }
            catch(Exception e) when(e is ArgumentException || e is OverflowException)
            { return Reject(e.Message.StartsWith("Unknown",StringComparison.Ordinal)?"UnknownIdentifier":"InvalidOperationInput",i,e.Message); }
        }
        return new(draft,batch,draft.WithDefinition(current),null,System.Array.Empty<MechanicalDiagnostic>(),sources);
    }
    private static IEnumerable<string> ResizeIds(IReadOnlyList<string> existing,int count,IReadOnlyList<string>? added,string prefix)
    {
        var needed=Math.Max(0,count-existing.Count);
        if(added is not null && added.Count!=needed) throw new ArgumentException("Explicit new material IDs must match added count.");
        var result=existing.Take(count).ToList();var next=0;
        while(result.Count<count)
        {
            string id;
            if(added is not null) id=added[result.Count-existing.Count];
            else { do { id=prefix+"-"+(next++).ToString(CultureInfo.InvariantCulture); } while(existing.Contains(id) || result.Contains(id)); }
            if(result.Contains(id)) throw new ArgumentException("Duplicate material ID.");
            result.Add(id);
        }
        return result;
    }
    internal static GenevaIdealLockSpecification CopyLock(GenevaIdealLockSpecification l,IEnumerable<string>? recessIds=null,ExactQuantityInterval? window=null,bool? present=null) =>
        new(l.DriverFeatureId,recessIds??l.RecessIds,l.RecessPitchCount,l.RecessCenterDistance,l.DriverRadius,l.RecessRadius,l.PatchHalfWidth,l.RecessMountingTurns,window??l.ReleaseWindow,present??l.Present,l.Policy,l.DriverSurfaceKind);
    internal static GenevaDeviceDefinition Copy(GenevaDeviceDefinition d,GenevaLength? orbit=null,GenevaWheelSpecification? wheel=null,GenevaIdealLockSpecification? idealLock=null,
        ExactVector3? driverCenter=null,ExactQuantity? driverStation=null,OrientedShaft? outputShaft=null,ExactVector3? wheelCenter=null,ExactQuantity? wheelStation=null,
        ExactQuantity? driverMounting=null,ExactQuantity? wheelMounting=null,ExactQuantity? reference=null,int? registration=null,bool? pinPresent=null,
        string? sourceShaft=null,string? sourcePort=null,bool replaceSourcePort=false,string? mechanismKind=null,string? pinKind=null,int? pinCount=null) =>
        new(d.Id,sourceShaft??d.SourceShaftId,d.DriverBodyId,d.PinId,d.WheelBodyId,driverCenter??d.DriverCenterMm,driverStation??d.DriverStation,d.PlaneNormal,d.CenterDirection,
            outputShaft??d.OutputShaft,wheelCenter??d.WheelCenterMm,wheelStation??d.WheelStation,driverMounting??d.DriverMountingTurns,wheelMounting??d.WheelMountingTurns,
            reference??d.OutputReferenceTurns,registration??d.RegistrationSlot,orbit??d.OrbitRadius,wheel??d.Wheel,idealLock??d.IdealLock,pinPresent??d.PinPresent,d.AxesFixed,
            replaceSourcePort?sourcePort:d.SourcePortId,mechanismKind??d.MechanismKind,pinKind??d.PinKind,pinCount??d.PinCount);
}
internal static class GenevaEditKeys
{
    internal static string Operation(GenevaEditOperation op) => Pack(op.Kind,op switch
    {
        ApplyGenevaSourceEditsEdit e => e.Batch.RequestId,
        SetGenevaSourceLengthMappingEdit e => e.Mapping?.CanonicalRepresentation ?? "",
        SetGenevaOrbitEdit e => Pack(e.DeviceId,e.OrbitRadius.CanonicalRepresentation),
        SetGenevaWheelEdit e => Pack(e.DeviceId,e.Wheel.CanonicalRepresentation),
        SetGenevaSlotRootEdit e => Pack(e.DeviceId,GenevaProfile.Q(e.SlotRoot)),
        SetGenevaSlotMouthEdit e => Pack(e.DeviceId,e.MouthRadius.CanonicalRepresentation),
        SetGenevaDriverCenterEdit e => Pack(e.DeviceId,V(e.CenterMm),GenevaProfile.Q(e.Station)),
        SetGenevaOutputPlacementEdit e => Pack(e.DeviceId,Frame(e.FrameMm),V(e.CenterMm),GenevaProfile.Q(e.Station)),
        SetGenevaDriverMountingPhaseEdit e => Pack(e.DeviceId,GenevaProfile.Q(e.MountingTurns)),
        SetGenevaWheelMountingPhaseEdit e => Pack(e.DeviceId,GenevaProfile.Q(e.MountingTurns)),
        SetGenevaRegistrationEdit e => Pack(e.DeviceId,GenevaProfile.Q(e.ReferenceTurns),N(e.RegistrationSlot)),
        SetGenevaLockEdit e => Pack(e.DeviceId,e.IdealLock.CanonicalRepresentation),
        SetGenevaLockWindowEdit e => Pack(e.DeviceId,RotaryLinearProfile.I(e.ReleaseWindow)),
        SetGenevaLockPresenceEdit e => Pack(e.DeviceId,GenevaProfile.Flag(e.Present)),
        SetGenevaPinPresenceEdit e => Pack(e.DeviceId,GenevaProfile.Flag(e.Present)),
        SetGenevaTerminalEdit e => Pack(e.OutputKey,N(e.Sign),GenevaProfile.Q(e.Datum)),
        ReverseGenevaTerminalEdit e => e.OutputKey,
        ReverseGenevaOutputAxisEdit e => e.DeviceId,
        SetGenevaRequirementsEdit e => Pack(e.OutputKey,e.Requirement.CanonicalRepresentation),
        SetGenevaBindingEdit e => Pack(e.DeviceId,e.SourceShaftId,e.SourcePortId ?? ""),
        SetGenevaUnsupportedModeEdit e => Pack(e.DeviceId,e.MechanismKind,e.PinKind,N(e.PinCount),e.SlotKind,GenevaProfile.Flag(e.OutputIsPrescribed)),
        SetGenevaSlotCountEdit e => Pack(e.DeviceId,N(e.SlotCount),e.AddedSlotIds is null?"auto":Pack(e.AddedSlotIds.ToArray()),e.AddedRecessIds is null?"auto":Pack(e.AddedRecessIds.ToArray())),
        SetGenevaRequiredValidationDomainsEdit e => Pack(e.Domains.ToArray()),
        _ => throw new ArgumentException("Unsupported Geneva edit.")
    });
}
