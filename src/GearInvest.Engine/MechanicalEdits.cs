using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum MechanicalRemovalPolicy { RejectDependencies, RemoveIncidentContactsAndUnresolveOutputs }
public enum ToothDimensionPolicy { PreservePitchRadiusPerTooth }
public enum MechanicalEditStatus { Applied, Rejected }

public abstract class MechanicalEditOperation
{
    private protected MechanicalEditOperation() { }
    public string Kind => GetType().Name;
    public string CanonicalRepresentation => MechanicalEditKeys.Operation(this);
}
public sealed class AddShaftEdit : MechanicalEditOperation { public AddShaftEdit(OrientedShaft shaft) { Shaft = shaft; } public OrientedShaft Shaft { get; } }
public sealed class RemoveShaftEdit : MechanicalEditOperation
{
    public RemoveShaftEdit(string shaftId, bool removePrescribedInput = false) { ShaftId = shaftId; RemovePrescribedInput = removePrescribedInput; }
    public string ShaftId { get; } public bool RemovePrescribedInput { get; }
}
public sealed class AddBodyEdit : MechanicalEditOperation { public AddBodyEdit(OrientedGearBody body) { Body = body; } public OrientedGearBody Body { get; } }
public sealed class RemoveBodyEdit : MechanicalEditOperation
{
    public RemoveBodyEdit(string bodyId, MechanicalRemovalPolicy policy = MechanicalRemovalPolicy.RejectDependencies) { BodyId = bodyId; Policy = policy; }
    public string BodyId { get; } public MechanicalRemovalPolicy Policy { get; }
}
public sealed class AddContactEdit : MechanicalEditOperation { public AddContactEdit(MechanicalContact contact) { Contact = contact; } public MechanicalContact Contact { get; } }
public sealed class RemoveContactEdit : MechanicalEditOperation { public RemoveContactEdit(string contactId) { ContactId = contactId; } public string ContactId { get; } }
public sealed class RebindContactEdit : MechanicalEditOperation
{
    public RebindContactEdit(string contactId, string bodyAId, string bodyBId) { ContactId = contactId; BodyAId = bodyAId; BodyBId = bodyBId; }
    public string ContactId { get; } public string BodyAId { get; } public string BodyBId { get; }
}
public sealed class SetToothCountEdit : MechanicalEditOperation
{
    public SetToothCountEdit(string bodyId, int teeth, ToothDimensionPolicy policy) { BodyId = bodyId; Teeth = teeth; Policy = policy; }
    public string BodyId { get; } public int Teeth { get; } public ToothDimensionPolicy Policy { get; }
}
public sealed class SetBodyMountingEdit : MechanicalEditOperation
{
    public SetBodyMountingEdit(string bodyId, OrientedFrame frame) { BodyId = bodyId; Frame = frame; }
    public string BodyId { get; } public OrientedFrame Frame { get; }
}
public sealed class RebindBodyShaftEdit : MechanicalEditOperation
{
    public RebindBodyShaftEdit(string bodyId, string shaftId) { BodyId = bodyId; ShaftId = shaftId; }
    public string BodyId { get; } public string ShaftId { get; }
}
/// <summary>Replaces explicit placement declarations atomically; cannot implicitly upgrade a legacy definition.</summary>
public sealed class SetParallelCoaxialLayoutEdit : MechanicalEditOperation
{
    public SetParallelCoaxialLayoutEdit(ParallelCoaxialLayout layout) { Layout = layout ?? throw new ArgumentNullException(nameof(layout)); }
    public ParallelCoaxialLayout Layout { get; }
}
/// <summary>Explicit rigid world transform of one shaft and ALL its current bodies/ports. World obstacles stay fixed.</summary>
public sealed class MoveShaftGroupEdit : MechanicalEditOperation
{
    public MoveShaftGroupEdit(string shaftId, OrientedFrame worldRigidTransform) { ShaftId = shaftId; WorldRigidTransform = worldRigidTransform; }
    public string ShaftId { get; } public OrientedFrame WorldRigidTransform { get; }
}
public sealed class SetPortEdit : MechanicalEditOperation { public SetPortEdit(ShaftPort port) { Port = port; } public ShaftPort Port { get; } }
public sealed class SetPortConnectionEdit : MechanicalEditOperation { public SetPortConnectionEdit(ShaftPortConnection connection) { Connection = connection; } public ShaftPortConnection Connection { get; } }
public sealed class RemovePortConnectionEdit : MechanicalEditOperation { public RemovePortConnectionEdit(string connectionId) { ConnectionId = connectionId; } public string ConnectionId { get; } }
public sealed class BindOutputEdit : MechanicalEditOperation
{
    public BindOutputEdit(string key, string shaftId, string bodyId, string portId, OrientedOutputRole? role = null)
    { Key = key; ShaftId = shaftId; BodyId = bodyId; PortId = portId; Role = role; }
    public string Key { get; } public string ShaftId { get; } public string BodyId { get; } public string PortId { get; } public OrientedOutputRole? Role { get; }
}
public sealed class SetOutputTerminalEdit : MechanicalEditOperation
{
    public SetOutputTerminalEdit(string key, OrientedFrame frame) { Key = key; Frame = frame; }
    public string Key { get; } public OrientedFrame Frame { get; }
}
public sealed class SetRequestedTransferEdit : MechanicalEditOperation
{
    public SetRequestedTransferEdit(string key, Rational? requiredTransfer) { Key = key; RequiredTransfer = requiredTransfer; }
    public string Key { get; } public Rational? RequiredTransfer { get; }
}
public sealed class AddKeepOutEdit : MechanicalEditOperation { public AddKeepOutEdit(OrientedKeepOut keepOut) { KeepOut = keepOut; } public OrientedKeepOut KeepOut { get; } }
public sealed class RemoveKeepOutEdit : MechanicalEditOperation { public RemoveKeepOutEdit(string keepOutId) { KeepOutId = keepOutId; } public string KeepOutId { get; } }
public sealed class SetClearancePolicyEdit : MechanicalEditOperation
{
    public SetClearancePolicyEdit(string clearancePolicy, bool requireCrossComponentClearance) { ClearancePolicy = clearancePolicy; RequireCrossComponentClearance = requireCrossComponentClearance; }
    public string ClearancePolicy { get; } public bool RequireCrossComponentClearance { get; }
}

public sealed class MechanicalEditBatch
{
    public MechanicalEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<MechanicalEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(MechanicalAuthoringProfile.MaxOperations + 1).ToList().AsReadOnly();
        if (Operations.Count > MechanicalAuthoringProfile.MaxOperations || Operations.Any(o => o is null)) throw new ArgumentException("Ordered operation bound exceeded or null operation.");
        foreach (var operation in Operations) MechanicalEditKeys.Validate(operation);
        RequestId = HashText(Pack("mechanical-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId,
            Pack(Operations.Select(o => o.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; }
    public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<MechanicalEditOperation> Operations { get; }
    public string RequestId { get; }
}

public sealed class MechanicalChange
{
    public MechanicalChange(int operationIndex, MechanicalReference subject, string action, string? before, string? after, bool dependent)
    { OperationIndex = operationIndex; Subject = subject; Action = action; Before = before; After = after; Dependent = dependent; }
    public int OperationIndex { get; }
    public MechanicalReference Subject { get; }
    public string Action { get; }
    public string? Before { get; }
    public string? After { get; }
    public bool Dependent { get; }
}

public sealed class MechanicalNetChanges
{
    public MechanicalNetChanges(IEnumerable<MechanicalReference> added, IEnumerable<MechanicalReference> removed, IEnumerable<MechanicalReference> modified)
    { Added = Order(added); Removed = Order(removed); Modified = Order(modified); }
    private static ReadOnlyCollection<MechanicalReference> Order(IEnumerable<MechanicalReference> values) => values.OrderBy(v => v.Key, StringComparer.Ordinal).ToList().AsReadOnly();
    public ReadOnlyCollection<MechanicalReference> Added { get; }
    public ReadOnlyCollection<MechanicalReference> Removed { get; }
    public ReadOnlyCollection<MechanicalReference> Modified { get; }
    public bool IsEmpty => Added.Count + Removed.Count + Modified.Count == 0;
}

public sealed class MechanicalEditResult
{
    internal MechanicalEditResult(MechanicalDraft before, MechanicalEditBatch batch, MechanicalDraft? draft,
        IEnumerable<MechanicalChange> changes, IEnumerable<MechanicalDiagnostic> diagnostics, int? failingOperationIndex)
    {
        Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied;
        Draft = draft; RequestId = batch.RequestId; BaseDraftId = before.DraftId; FailingOperationIndex = failingOperationIndex;
        Changes = changes.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly();
        NetChanges = MechanicalEditor.Diff(before.Definition, draft?.Definition ?? before.Definition);
        DirectlyChanged = Changes.Where(c => !c.Dependent).Select(c => c.Subject).GroupBy(r => r.Key, StringComparer.Ordinal).Select(g => g.First()).OrderBy(r => r.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        PotentiallyAffectedOutputs = (draft is null || batch.Operations.Count == 0 ? Array.Empty<string>() :
            before.Definition.Outputs.Select(o => o.Key).Concat(draft.Definition.Outputs.Select(o => o.Key))).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        ResultId = HashText(Pack("mechanical-edit-result-v1", BaseDraftId, RequestId, Status.ToString(), draft?.DraftId ?? "",
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Changes.Select(c => Pack(N(c.OperationIndex), c.Subject.Kind, c.Subject.Id, c.Action, c.Before ?? "", c.After ?? "", c.Dependent ? "1" : "0")).ToArray()),
            Pack(Diagnostics.Select(d => Pack(d.Code, Pack(d.Related.Select(r => r.Key).ToArray()))).ToArray())));
    }
    public MechanicalEditStatus Status { get; }
    public MechanicalDraft? Draft { get; }
    public string BaseDraftId { get; }
    public string RequestId { get; }
    public string ResultId { get; }
    public int? FailingOperationIndex { get; }
    public ReadOnlyCollection<MechanicalChange> Changes { get; }
    public MechanicalNetChanges NetChanges { get; }
    public ReadOnlyCollection<MechanicalReference> DirectlyChanged { get; }
    public ReadOnlyCollection<string> PotentiallyAffectedOutputs { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
    public bool PotentialWholeSpatialImpact => Status == MechanicalEditStatus.Applied && Changes.Count != 0;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public static class MechanicalEditor
{
    public static MechanicalEditResult Apply(MechanicalDraft draft, MechanicalEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        MechanicalEditResult Reject(string code, int? index, IEnumerable<MechanicalReference>? related = null, string detail = "") =>
            new(draft, batch, null, Array.Empty<MechanicalChange>(), new[] { new MechanicalDiagnostic(code, "Edit", related: related, detail: detail) }, index);
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId)
            return Reject("StaleRevision", null, detail: "Both revision and current definition identity must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null);
        var current = draft.Definition; var log = new List<MechanicalChange>();
        for (var index = 0; index < batch.Operations.Count; index++)
        {
            try
            {
                var operation = batch.Operations[index]; var next = ApplyOne(current, operation);
                var direct = Direct(operation);
                var a = Objects(current); var b = Objects(next);
                foreach (var key in a.Keys.Concat(b.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
                {
                    a.TryGetValue(key, out var old); b.TryGetValue(key, out var fresh);
                    if (old?.value == fresh?.value) continue;
                    var subject = (fresh ?? old)!.reference;
                    log.Add(new MechanicalChange(index, subject, old is null ? "Added" : fresh is null ? "Removed" : "Modified",
                        old?.value, fresh?.value, !direct.Any(r => r.Key == subject.Key)));
                }
                // Every command is retained, even if it caused no net mechanical mutation.
                if (!log.Any(c => c.OperationIndex == index))
                    foreach (var subject in direct) log.Add(new MechanicalChange(index, subject, "Unchanged", null, null, false));
                current = next;
            }
            catch (EditFailure e) { return Reject(e.Code, index, e.Related, e.Message); }
            catch (ArgumentException e) { return Reject("InvalidOperationInput", index, Direct(batch.Operations[index]), e.Message); }
            catch (OverflowException e) { return Reject("InputLimit", index, Direct(batch.Operations[index]), e.Message); }
        }
        return new MechanicalEditResult(draft, batch, draft.WithDefinition(current), log, Array.Empty<MechanicalDiagnostic>(), null);
    }

    private static MechanicalDefinition ApplyOne(MechanicalDefinition d, MechanicalEditOperation operation)
    {
        var shafts = d.Shafts.ToDictionary(x => x.Id, StringComparer.Ordinal); var bodies = d.Bodies.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var contacts = d.Contacts.ToDictionary(x => x.Id, StringComparer.Ordinal); var ports = d.Ports.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var connections = d.Connections.ToDictionary(x => x.Id, StringComparer.Ordinal); var outputs = d.Outputs.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var keepouts = d.KeepOuts.ToDictionary(x => x.Id, StringComparer.Ordinal); var root = d.RootShaftId;
        var policy = d.ClearancePolicy; var clearance = d.RequireCrossComponentClearance; var coaxial = d.CoaxialLayout;
        switch (operation)
        {
            case AddShaftEdit e: Add(shafts, e.Shaft.Id, e.Shaft, "Shaft"); break;
            case RemoveShaftEdit e:
                var shaft = Require(shafts, e.ShaftId, "Shaft");
                var dependencies = bodies.Values.Where(b => b.ShaftId == shaft.Id).Select(b => new MechanicalReference("Body", b.Id))
                    .Concat(ports.Values.Where(p => p.ShaftId == shaft.Id).Select(p => new MechanicalReference("Port", p.Id)))
                    .Concat(outputs.Values.Where(o => o.ShaftId == shaft.Id).Select(o => new MechanicalReference("Output", o.Key))).ToArray();
                if (dependencies.Length != 0 || (shaft.IsPrescribed || root == shaft.Id) && !e.RemovePrescribedInput)
                    throw new EditFailure("DependencyRemovalRequired", dependencies.Concat(new[] { new MechanicalReference("Shaft", shaft.Id) }));
                shafts.Remove(shaft.Id); if (root == shaft.Id) root = null; break;
            case AddBodyEdit e:
                Require(shafts, e.Body.ShaftId, "Shaft"); Add(bodies, e.Body.Id, e.Body, "Body"); break;
            case RemoveBodyEdit e:
                var body = Require(bodies, e.BodyId, "Body");
                if (!Enum.IsDefined(typeof(MechanicalRemovalPolicy), e.Policy)) throw new EditFailure("UnsupportedOperation", Direct(e));
                var incident = contacts.Values.Where(c => c.BodyAId == body.Id || c.BodyBId == body.Id).ToArray();
                var boundOutputs = outputs.Values.Where(o => o.BodyId == body.Id).ToArray();
                if (e.Policy == MechanicalRemovalPolicy.RejectDependencies && (incident.Length > 0 || boundOutputs.Length > 0))
                    throw new EditFailure("DependencyRemovalRequired", incident.Select(c => new MechanicalReference("Contact", c.Id)).Concat(boundOutputs.Select(o => new MechanicalReference("Output", o.Key))));
                foreach (var c in incident) contacts.Remove(c.Id);
                foreach (var o in boundOutputs) outputs[o.Key] = new MechanicalOutput(o.Key, null, null, null, o.RequiredTransfer, o.Role,
                    "BodyRemoved", new[] { new MechanicalReference("Body", o.BodyId!), new MechanicalReference("Shaft", o.ShaftId!), new MechanicalReference("Port", o.PortId!) });
                bodies.Remove(body.Id); break;
            case AddContactEdit e:
                Require(bodies, e.Contact.BodyAId, "Body"); Require(bodies, e.Contact.BodyBId, "Body"); Add(contacts, e.Contact.Id, e.Contact, "Contact"); break;
            case RemoveContactEdit e: Require(contacts, e.ContactId, "Contact"); contacts.Remove(e.ContactId); break;
            case RebindContactEdit e:
                var contact = Require(contacts, e.ContactId, "Contact"); Require(bodies, e.BodyAId, "Body"); Require(bodies, e.BodyBId, "Body");
                contacts[e.ContactId] = new MechanicalContact(contact.Id, contact.Kind, e.BodyAId, e.BodyBId, contact.Cone); break;
            case SetToothCountEdit e:
                var changed = Require(bodies, e.BodyId, "Body");
                if (e.Policy != ToothDimensionPolicy.PreservePitchRadiusPerTooth) throw new EditFailure("UnsupportedOperation", Direct(e));
                if (e.Teeth < 1 || e.Teeth > OrientedTransmissionProfile.MaxTeeth || changed.Teeth <= 0)
                    throw new EditFailure("InvalidToothCount", Direct(e));
                bodies[changed.Id] = CopyBody(changed, changed.MountingFrame, e.Teeth, changed.OuterPitchRadius / changed.Teeth * e.Teeth); break;
            case SetBodyMountingEdit e:
                var mounted = Require(bodies, e.BodyId, "Body"); bodies[mounted.Id] = CopyBody(mounted, e.Frame, mounted.Teeth, mounted.OuterPitchRadius); break;
            case RebindBodyShaftEdit e:
                var rebound = Require(bodies, e.BodyId, "Body"); Require(shafts, e.ShaftId, "Shaft");
                bodies[rebound.Id] = new OrientedGearBody(rebound.Id, e.ShaftId, rebound.Kind, rebound.MountingFrame, rebound.Teeth, rebound.OuterPitchRadius, rebound.SourceModuleId); break;
            case SetParallelCoaxialLayoutEdit e:
                if (coaxial is null) throw new EditFailure("ExplicitCoaxialProfileRequired", Direct(e));
                coaxial = e.Layout; break;
            case MoveShaftGroupEdit e:
                var moved = Require(shafts, e.ShaftId, "Shaft");
                if (!e.WorldRigidTransform.IsProperCardinal) throw new EditFailure("UnsupportedRigidTransform", Direct(e));
                shafts[moved.Id] = new OrientedShaft(moved.Id, e.WorldRigidTransform.Transform(moved.Frame), moved.IsPrescribed);
                foreach (var b in bodies.Values.Where(b => b.ShaftId == moved.Id).ToArray()) bodies[b.Id] = CopyBody(b, e.WorldRigidTransform.Transform(b.MountingFrame), b.Teeth, b.OuterPitchRadius);
                foreach (var p in ports.Values.Where(p => p.ShaftId == moved.Id).ToArray()) ports[p.Id] = new ShaftPort(p.Id, p.ShaftId, e.WorldRigidTransform.Transform(p.Frame), p.PhaseOffset, p.Kind);
                break;
            case SetPortEdit e: Require(shafts, e.Port.ShaftId, "Shaft"); ports[e.Port.Id] = e.Port; break;
            case SetPortConnectionEdit e:
                Require(ports, e.Connection.PortAId, "Port"); Require(ports, e.Connection.PortBId, "Port"); connections[e.Connection.Id] = e.Connection; break;
            case RemovePortConnectionEdit e: Require(connections, e.ConnectionId, "Connection"); connections.Remove(e.ConnectionId); break;
            case BindOutputEdit e:
                Require(shafts, e.ShaftId, "Shaft"); Require(bodies, e.BodyId, "Body"); Require(ports, e.PortId, "Port"); outputs.TryGetValue(e.Key, out var previous);
                outputs[e.Key] = new MechanicalOutput(e.Key, e.ShaftId, e.BodyId, e.PortId, previous?.RequiredTransfer, e.Role ?? previous?.Role); break;
            case SetOutputTerminalEdit e:
                var output = Require(outputs, e.Key, "Output");
                if (!output.IsResolved) throw new EditFailure("MissingEndpoint", Direct(e));
                var terminal = Require(ports, output.PortId!, "Port"); ports[terminal.Id] = new ShaftPort(terminal.Id, terminal.ShaftId, e.Frame, terminal.PhaseOffset, terminal.Kind); break;
            case SetRequestedTransferEdit e:
                var target = Require(outputs, e.Key, "Output"); outputs[e.Key] = new MechanicalOutput(target.Key, target.ShaftId, target.BodyId, target.PortId, e.RequiredTransfer, target.Role, target.UnresolvedReason, target.FormerEndpoint); break;
            case AddKeepOutEdit e: Add(keepouts, e.KeepOut.Id, e.KeepOut, "KeepOut"); break;
            case RemoveKeepOutEdit e: Require(keepouts, e.KeepOutId, "KeepOut"); keepouts.Remove(e.KeepOutId); break;
            case SetClearancePolicyEdit e: policy = e.ClearancePolicy; clearance = e.RequireCrossComponentClearance; break;
            default: throw new EditFailure("UnsupportedOperation", Array.Empty<MechanicalReference>());
        }
        return coaxial is null
            ? new MechanicalDefinition(root, shafts.Values, bodies.Values, contacts.Values, ports.Values, connections.Values, outputs.Values, keepouts.Values, policy, clearance)
            : new MechanicalDefinition(coaxial, root, shafts.Values, bodies.Values, contacts.Values, ports.Values, connections.Values, outputs.Values, keepouts.Values, policy, clearance);
    }

    private static OrientedGearBody CopyBody(OrientedGearBody b, OrientedFrame frame, int teeth, Rational radius) => new(b.Id, b.ShaftId, b.Kind, frame, teeth, radius, b.SourceModuleId);
    private static T Require<T>(Dictionary<string, T> map, string id, string kind)
    { if (id is null || !map.TryGetValue(id, out var value)) throw new EditFailure("UnknownId", new[] { new MechanicalReference(kind, id ?? "<null>") }); return value; }
    private static void Add<T>(Dictionary<string, T> map, string id, T value, string kind)
    { if (map.ContainsKey(id)) throw new EditFailure("DuplicateId", new[] { new MechanicalReference(kind, id) }); map.Add(id, value); }
    private sealed class EditFailure : Exception
    {
        internal EditFailure(string code, IEnumerable<MechanicalReference> related) : base(code) { Code = code; Related = related.ToArray(); }
        internal string Code { get; } internal MechanicalReference[] Related { get; }
    }

    public static MechanicalNetChanges Diff(MechanicalDefinition before, MechanicalDefinition after)
    {
        var a = Objects(before); var b = Objects(after);
        return new MechanicalNetChanges(b.Where(x => !a.ContainsKey(x.Key)).Select(x => x.Value.reference),
            a.Where(x => !b.ContainsKey(x.Key)).Select(x => x.Value.reference),
            b.Where(x => a.TryGetValue(x.Key, out var old) && old.value != x.Value.value).Select(x => x.Value.reference));
    }
    private sealed class ObjectValue
    {
        internal ObjectValue(string kind, string id, string value) { reference = new MechanicalReference(kind, id); this.value = value; }
        internal MechanicalReference reference; internal string value;
    }
    private static Dictionary<string, ObjectValue> Objects(MechanicalDefinition d)
    {
        var values = d.Shafts.Select(s => new ObjectValue("Shaft", s.Id, MechanicalDefinition.ShaftKey(s)))
            .Concat(d.Bodies.Select(b => new ObjectValue("Body", b.Id, MechanicalDefinition.BodyKey(b))))
            .Concat(d.Contacts.Select(c => new ObjectValue("Contact", c.Id, MechanicalDefinition.ContactKey(c))))
            .Concat(d.Ports.Select(p => new ObjectValue("Port", p.Id, MechanicalDefinition.PortKey(p))))
            .Concat(d.Connections.Select(c => new ObjectValue("Connection", c.Id, MechanicalDefinition.ConnectionKey(c))))
            .Concat(d.Outputs.Select(o => new ObjectValue("Output", o.Key, MechanicalDefinition.OutputKey(o))))
            .Concat(d.KeepOuts.Select(k => new ObjectValue("KeepOut", k.Id, MechanicalDefinition.KeepOutKey(k))))
            .Concat(new[] { new ObjectValue("Definition", "input", d.RootShaftId ?? ""), new ObjectValue("Definition", "clearance", Pack(d.ClearancePolicy, d.RequireCrossComponentClearance ? "1" : "0")) });
        if (d.CoaxialLayout is not null) values = values.Concat(new[] { new ObjectValue("Definition", "coaxialLayout", MechanicalDefinition.CoaxialLayoutKey(d.CoaxialLayout)) });
        return values.ToDictionary(x => x.reference.Key, StringComparer.Ordinal);
    }
    private static MechanicalReference[] Direct(MechanicalEditOperation operation)
    {
        MechanicalReference R(string kind, string id) => new(kind, id);
        return operation switch
        {
            AddShaftEdit e => new[] { R("Shaft", e.Shaft.Id) }, RemoveShaftEdit e => new[] { R("Shaft", e.ShaftId) },
            AddBodyEdit e => new[] { R("Body", e.Body.Id) }, RemoveBodyEdit e => new[] { R("Body", e.BodyId) },
            AddContactEdit e => new[] { R("Contact", e.Contact.Id) }, RemoveContactEdit e => new[] { R("Contact", e.ContactId) },
            RebindContactEdit e => new[] { R("Contact", e.ContactId) }, SetToothCountEdit e => new[] { R("Body", e.BodyId) },
            SetBodyMountingEdit e => new[] { R("Body", e.BodyId) }, MoveShaftGroupEdit e => new[] { R("Shaft", e.ShaftId) },
            RebindBodyShaftEdit e => new[] { R("Body", e.BodyId) }, SetParallelCoaxialLayoutEdit => new[] { R("Definition", "coaxialLayout") },
            SetPortEdit e => new[] { R("Port", e.Port.Id) }, SetPortConnectionEdit e => new[] { R("Connection", e.Connection.Id) },
            RemovePortConnectionEdit e => new[] { R("Connection", e.ConnectionId) }, BindOutputEdit e => new[] { R("Output", e.Key) },
            SetOutputTerminalEdit e => new[] { R("Output", e.Key) }, SetRequestedTransferEdit e => new[] { R("Output", e.Key) },
            AddKeepOutEdit e => new[] { R("KeepOut", e.KeepOut.Id) }, RemoveKeepOutEdit e => new[] { R("KeepOut", e.KeepOutId) },
            SetClearancePolicyEdit => new[] { R("Definition", "clearance") }, _ => Array.Empty<MechanicalReference>()
        };
    }
}

public static class MechanicalEditKeys
{
    /// <summary>Resource/encoding input bounds only. Mechanical validity and unknown operation policies are not silently repaired.</summary>
    public static void Validate(MechanicalEditOperation operation)
    {
        void Id(string value) => MechanicalAuthoringProfile.IdValue(value);
        void Shaft(OrientedShaft s) { if (s is null) throw new ArgumentNullException(nameof(operation)); Id(s.Id); MechanicalAuthoringProfile.FrameBound(s.Frame); }
        void Body(OrientedGearBody b)
        { if (b is null) throw new ArgumentNullException(nameof(operation)); Id(b.Id); Id(b.ShaftId); Id(b.SourceModuleId); MechanicalAuthoringProfile.FrameBound(b.MountingFrame); MechanicalAuthoringProfile.Number(b.OuterPitchRadius); }
        void Port(ShaftPort p)
        { if (p is null) throw new ArgumentNullException(nameof(operation)); Id(p.Id); Id(p.ShaftId); MechanicalAuthoringProfile.FrameBound(p.Frame); MechanicalAuthoringProfile.Number(p.PhaseOffset); }
        switch (operation)
        {
            case AddShaftEdit e: Shaft(e.Shaft); break;
            case RemoveShaftEdit e: Id(e.ShaftId); break;
            case AddBodyEdit e: Body(e.Body); break;
            case RemoveBodyEdit e: Id(e.BodyId); break;
            case AddContactEdit e:
                if (e.Contact is null) throw new ArgumentNullException(nameof(operation)); Id(e.Contact.Id); Id(e.Contact.BodyAId); Id(e.Contact.BodyBId);
                if (e.Contact.Cone is not null)
                {
                    var c = e.Contact.Cone; foreach (var v in new[] { c.Apex, c.OutwardA, c.OutwardB, c.OuterContact }) MechanicalAuthoringProfile.VectorBound(v);
                    MechanicalAuthoringProfile.Number(c.InnerParameter); MechanicalAuthoringProfile.Number(c.OuterScaleA); MechanicalAuthoringProfile.Number(c.OuterScaleB);
                }
                break;
            case RemoveContactEdit e: Id(e.ContactId); break;
            case RebindContactEdit e: Id(e.ContactId); Id(e.BodyAId); Id(e.BodyBId); break;
            case SetToothCountEdit e: Id(e.BodyId); break;
            case SetBodyMountingEdit e: Id(e.BodyId); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case RebindBodyShaftEdit e: Id(e.BodyId); Id(e.ShaftId); break;
            case SetParallelCoaxialLayoutEdit e: _ = MechanicalDefinition.CoaxialLayoutKey(e.Layout); break;
            case MoveShaftGroupEdit e: Id(e.ShaftId); MechanicalAuthoringProfile.FrameBound(e.WorldRigidTransform); break;
            case SetPortEdit e: Port(e.Port); break;
            case SetPortConnectionEdit e:
                if (e.Connection is null) throw new ArgumentNullException(nameof(operation)); Id(e.Connection.Id); Id(e.Connection.PortAId); Id(e.Connection.PortBId); MechanicalAuthoringProfile.Number(e.Connection.CoordinateTransfer); break;
            case RemovePortConnectionEdit e: Id(e.ConnectionId); break;
            case BindOutputEdit e: Id(e.Key); Id(e.ShaftId); Id(e.BodyId); Id(e.PortId); break;
            case SetOutputTerminalEdit e: Id(e.Key); MechanicalAuthoringProfile.FrameBound(e.Frame); break;
            case SetRequestedTransferEdit e: Id(e.Key); if (e.RequiredTransfer.HasValue) MechanicalAuthoringProfile.Number(e.RequiredTransfer.Value); break;
            case AddKeepOutEdit e:
                if (e.KeepOut is null) throw new ArgumentNullException(nameof(operation)); Id(e.KeepOut.Id); MechanicalAuthoringProfile.VectorBound(e.KeepOut.Envelope.Min); MechanicalAuthoringProfile.VectorBound(e.KeepOut.Envelope.Max); break;
            case RemoveKeepOutEdit e: Id(e.KeepOutId); break;
            case SetClearancePolicyEdit e: Id(e.ClearancePolicy); break;
            default: throw new ArgumentException("Unsupported edit operation.");
        }
    }
    public static string Operation(MechanicalEditOperation operation)
    {
        var payload = operation switch
        {
            AddShaftEdit e => MechanicalDefinition.ShaftKey(e.Shaft), RemoveShaftEdit e => Pack(e.ShaftId, e.RemovePrescribedInput ? "1" : "0"),
            AddBodyEdit e => MechanicalDefinition.BodyKey(e.Body), RemoveBodyEdit e => Pack(e.BodyId, e.Policy.ToString()),
            AddContactEdit e => MechanicalDefinition.ContactKey(e.Contact), RemoveContactEdit e => Pack(e.ContactId),
            RebindContactEdit e => Pack(e.ContactId, e.BodyAId, e.BodyBId), SetToothCountEdit e => Pack(e.BodyId, N(e.Teeth), e.Policy.ToString()),
            SetBodyMountingEdit e => Pack(e.BodyId, Frame(e.Frame)), MoveShaftGroupEdit e => Pack(e.ShaftId, Frame(e.WorldRigidTransform)),
            RebindBodyShaftEdit e => Pack(e.BodyId, e.ShaftId), SetParallelCoaxialLayoutEdit e => MechanicalDefinition.CoaxialLayoutKey(e.Layout),
            SetPortEdit e => MechanicalDefinition.PortKey(e.Port), SetPortConnectionEdit e => MechanicalDefinition.ConnectionKey(e.Connection),
            RemovePortConnectionEdit e => Pack(e.ConnectionId), BindOutputEdit e => Pack(e.Key, e.ShaftId, e.BodyId, e.PortId, e.Role?.ToString() ?? ""),
            SetOutputTerminalEdit e => Pack(e.Key, Frame(e.Frame)), SetRequestedTransferEdit e => Pack(e.Key, e.RequiredTransfer.HasValue ? F(e.RequiredTransfer.Value) : ""),
            AddKeepOutEdit e => MechanicalDefinition.KeepOutKey(e.KeepOut), RemoveKeepOutEdit e => Pack(e.KeepOutId),
            SetClearancePolicyEdit e => Pack(e.ClearancePolicy, e.RequireCrossComponentClearance ? "1" : "0"),
            _ => throw new ArgumentException("Unsupported edit operation.")
        };
        return Pack(operation.Kind, payload);
    }
}
