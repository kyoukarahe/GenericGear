using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public abstract class MechanicalAssemblyEditOperation
{
    private protected MechanicalAssemblyEditOperation() { }
    public string Kind => GetType().Name;
    public abstract string CanonicalRepresentation { get; }
}
public sealed class ApplyMechanicalAssemblyRootEditsEdit : MechanicalAssemblyEditOperation
{
    public ApplyMechanicalAssemblyRootEditsEdit(MechanicalEditBatch batch) { Batch = batch ?? throw new ArgumentNullException(nameof(batch)); }
    public MechanicalEditBatch Batch { get; }
    public override string CanonicalRepresentation => Pack(Kind, Batch.RequestId);
}
public sealed class SetMechanicalAssemblyRootMappingEdit : MechanicalAssemblyEditOperation
{
    public SetMechanicalAssemblyRootMappingEdit(SourceLengthMapping? mapping) { Mapping = mapping; }
    public SourceLengthMapping? Mapping { get; }
    public override string CanonicalRepresentation => Pack(Kind, Mapping?.CanonicalRepresentation ?? "");
}
public sealed class AddMechanicalAssemblyMemberEdit : MechanicalAssemblyEditOperation
{
    public AddMechanicalAssemblyMemberEdit(MechanicalAssemblyMember member) { Member = member ?? throw new ArgumentNullException(nameof(member)); }
    public MechanicalAssemblyMember Member { get; }
    public override string CanonicalRepresentation => Pack(Kind, Member.CanonicalRepresentation);
}
/// <summary>Explicitly replaces authored local parameters, requirements and binding together; no parameter repair or inferred remapping.</summary>
public sealed class ReplaceMechanicalAssemblyMemberEdit : MechanicalAssemblyEditOperation
{
    public ReplaceMechanicalAssemblyMemberEdit(MechanicalAssemblyMember member) { Member = member ?? throw new ArgumentNullException(nameof(member)); }
    public MechanicalAssemblyMember Member { get; }
    public override string CanonicalRepresentation => Pack(Kind, Member.CanonicalRepresentation);
}
/// <summary>Removes only the named physical member. Dependent bindings and observations remain explicit unresolved references.</summary>
public sealed class RemoveMechanicalAssemblyMemberEdit : MechanicalAssemblyEditOperation
{
    public RemoveMechanicalAssemblyMemberEdit(string instanceId) { InstanceId = MechanicalAuthoringProfile.IdValue(instanceId); }
    public string InstanceId { get; }
    public override string CanonicalRepresentation => Pack(Kind, InstanceId);
}
public sealed class SetMechanicalAssemblyBindingEdit : MechanicalAssemblyEditOperation
{
    public SetMechanicalAssemblyBindingEdit(string instanceId, UpstreamShaftBinding? binding)
    { InstanceId = MechanicalAuthoringProfile.IdValue(instanceId); Binding = binding; }
    public string InstanceId { get; }
    public UpstreamShaftBinding? Binding { get; }
    public override string CanonicalRepresentation => Pack(Kind, InstanceId, Binding?.CanonicalRepresentation ?? "");
}
/// <summary>Explicit upsert of an assembly observation including its independent global requirements.</summary>
public sealed class SetMechanicalAssemblyOutputEdit : MechanicalAssemblyEditOperation
{
    public SetMechanicalAssemblyOutputEdit(AssemblyOutputBinding output) { Output = output ?? throw new ArgumentNullException(nameof(output)); }
    public AssemblyOutputBinding Output { get; }
    public override string CanonicalRepresentation => Pack(Kind, Output.CanonicalRepresentation);
}
public sealed class RemoveMechanicalAssemblyOutputEdit : MechanicalAssemblyEditOperation
{
    public RemoveMechanicalAssemblyOutputEdit(string key) { Key = MechanicalAuthoringProfile.IdValue(key); }
    public string Key { get; }
    public override string CanonicalRepresentation => Pack(Kind, Key);
}
public sealed class SetMechanicalAssemblyRequiredValidationEdit : MechanicalAssemblyEditOperation
{
    public SetMechanicalAssemblyRequiredValidationEdit(IEnumerable<string> requiredDomains)
    { RequiredDomains = MechanicalAuthoringProfile.Set(requiredDomains, x => x, 32); }
    public ReadOnlyCollection<string> RequiredDomains { get; }
    public override string CanonicalRepresentation => Pack(Kind, Pack(RequiredDomains.ToArray()));
}

public sealed class MechanicalAssemblyEditBatch
{
    public MechanicalAssemblyEditBatch(long expectedRevision, string expectedDefinitionId, IEnumerable<MechanicalAssemblyEditOperation> operations)
    {
        if (expectedRevision < 0 || !IsHash(expectedDefinitionId)) throw new ArgumentException("Exact assembly revision and definition identity required.");
        ExpectedRevision = expectedRevision; ExpectedDefinitionId = expectedDefinitionId;
        Operations = (operations ?? throw new ArgumentNullException(nameof(operations))).Take(MechanicalAuthoringProfile.MaxOperations + 1).ToList().AsReadOnly();
        if (Operations.Count > MechanicalAuthoringProfile.MaxOperations || Operations.Any(x => x is null)) throw new ArgumentException("Assembly ordered operation bound exceeded.");
        OperationCount = Operations.Sum(x => x is ApplyMechanicalAssemblyRootEditsEdit root ? 1 + root.Batch.Operations.Count : 1);
        if (OperationCount > MechanicalAssemblyProfile.MaxTotalOperations) throw new ArgumentException("Assembly nested operation bound exceeded.");
        RequestId = HashText(Pack("mechanical-assembly-edit-batch-v1", expectedRevision.ToString(CultureInfo.InvariantCulture), expectedDefinitionId,
            Pack(Operations.Select(x => x.CanonicalRepresentation).ToArray())));
    }
    public long ExpectedRevision { get; }
    public string ExpectedDefinitionId { get; }
    public ReadOnlyCollection<MechanicalAssemblyEditOperation> Operations { get; }
    public int OperationCount { get; }
    public string RequestId { get; }
}

public sealed class MechanicalAssemblyChange
{
    internal MechanicalAssemblyChange(int operationIndex, string operationKind, string beforeDefinitionId, string afterDefinitionId)
    { OperationIndex = operationIndex; OperationKind = operationKind; BeforeDefinitionId = beforeDefinitionId; AfterDefinitionId = afterDefinitionId; }
    public int OperationIndex { get; }
    public string OperationKind { get; }
    public string BeforeDefinitionId { get; }
    public string AfterDefinitionId { get; }
    public bool IsUnchanged => BeforeDefinitionId == AfterDefinitionId;
}

public sealed class MechanicalAssemblyEditResult
{
    internal MechanicalAssemblyEditResult(MechanicalAssemblyDraft before, MechanicalAssemblyEditBatch batch, MechanicalAssemblyDraft? draft,
        IEnumerable<MechanicalAssemblyChange> changes, IEnumerable<MechanicalEditResult> sourceResults,
        IEnumerable<MechanicalDiagnostic> diagnostics, int? failingOperationIndex)
    {
        BaseDraftId = before.DraftId; RequestId = batch.RequestId; Draft = draft;
        Changes = changes.ToList().AsReadOnly(); SourceResults = sourceResults.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly();
        FailingOperationIndex = failingOperationIndex; Status = draft is null ? MechanicalEditStatus.Rejected : MechanicalEditStatus.Applied;
        IsNetEmpty = draft is not null && before.DefinitionId == draft.DefinitionId;
        ResultId = HashText(Pack("mechanical-assembly-edit-result-v1", BaseDraftId, RequestId, draft?.DraftId ?? "", Status.ToString(),
            failingOperationIndex?.ToString(CultureInfo.InvariantCulture) ?? "", Pack(Diagnostics.Select(x => Pack(x.Code, x.Detail)).ToArray()),
            Pack(SourceResults.Select(x => x.ResultId).ToArray())));
    }
    public string BaseDraftId { get; }
    public string RequestId { get; }
    public string ResultId { get; }
    public MechanicalAssemblyDraft? Draft { get; }
    public MechanicalEditStatus Status { get; }
    public ReadOnlyCollection<MechanicalAssemblyChange> Changes { get; }
    /// <summary>Attempted root operations; on outer rejection none of these tentative changes were committed.</summary>
    public ReadOnlyCollection<MechanicalEditResult> SourceResults { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public int? FailingOperationIndex { get; }
    public bool IsNetEmpty { get; }
    public bool RequiresReanalysis => Status == MechanicalEditStatus.Applied;
}

public static class MechanicalAssemblyEditor
{
    public static MechanicalAssemblyEditResult Apply(MechanicalAssemblyDraft draft, MechanicalAssemblyEditBatch batch)
    {
        if (draft is null || batch is null) throw new ArgumentNullException();
        var sourceResults = new List<MechanicalEditResult>();
        MechanicalAssemblyEditResult Reject(string code, int? index, string detail) => new(draft, batch, null,
            Array.Empty<MechanicalAssemblyChange>(), sourceResults, new[] { new MechanicalDiagnostic(code, "AssemblyEdit", detail: detail) }, index);
        if (draft.Revision != batch.ExpectedRevision || draft.DefinitionId != batch.ExpectedDefinitionId)
            return Reject("StaleRevision", null, "Both assembly revision and current definition identity must match; no partial application.");
        if (draft.Revision == long.MaxValue) return Reject("RevisionLimit", null, "Assembly revision limit reached.");
        var current = draft.Definition; var changes = new List<MechanicalAssemblyChange>();
        for (var index = 0; index < batch.Operations.Count; index++)
        {
            try
            {
                var operation = batch.Operations[index]; var root = current.Root; var mapping = current.RootMapping;
                var members = current.Members.ToDictionary(x => x.InstanceId, StringComparer.Ordinal);
                var outputs = current.Outputs.ToDictionary(x => x.Key, StringComparer.Ordinal);
                IEnumerable<string> required = current.RequiredValidationDomains;
                switch (operation)
                {
                    case ApplyMechanicalAssemblyRootEditsEdit edit:
                        var result = MechanicalEditor.Apply(root, edit.Batch); sourceResults.Add(result);
                        if (result.Draft is null) return Reject("RootEditRejected", index, "Nested root batch was rejected; the complete assembly transaction was discarded.");
                        root = result.Draft; break;
                    case SetMechanicalAssemblyRootMappingEdit edit: mapping = edit.Mapping; break;
                    case AddMechanicalAssemblyMemberEdit edit:
                        if (members.ContainsKey(edit.Member.InstanceId)) return Reject("DuplicateMember", index, edit.Member.InstanceId);
                        members.Add(edit.Member.InstanceId, edit.Member); break;
                    case ReplaceMechanicalAssemblyMemberEdit edit:
                        if (!members.ContainsKey(edit.Member.InstanceId)) return Reject("MissingMember", index, edit.Member.InstanceId);
                        members[edit.Member.InstanceId] = edit.Member; break;
                    case RemoveMechanicalAssemblyMemberEdit edit:
                        if (!members.Remove(edit.InstanceId)) return Reject("MissingMember", index, edit.InstanceId); break;
                    case SetMechanicalAssemblyBindingEdit edit:
                        if (!members.TryGetValue(edit.InstanceId, out var member)) return Reject("MissingMember", index, edit.InstanceId);
                        members[edit.InstanceId] = new MechanicalAssemblyMember(member.InstanceId, member.Declaration, edit.Binding); break;
                    case SetMechanicalAssemblyOutputEdit edit: outputs[edit.Output.Key] = edit.Output; break;
                    case RemoveMechanicalAssemblyOutputEdit edit:
                        if (!outputs.Remove(edit.Key)) return Reject("MissingOutput", index, edit.Key); break;
                    case SetMechanicalAssemblyRequiredValidationEdit edit: required = edit.RequiredDomains; break;
                    default: return Reject("UnsupportedOperation", index, operation.Kind);
                }
                var next = new MechanicalAssemblyDefinition(root, mapping, members.Values, outputs.Values, required, current.SeedImports, current.Profile, current.ProfileOrigin);
                changes.Add(new MechanicalAssemblyChange(index, operation.Kind, current.DefinitionId, next.DefinitionId)); current = next;
            }
            catch (ArgumentException exception) { return Reject("InvalidOperationInput", index, exception.Message); }
            catch (OverflowException exception) { return Reject("ResourceLimitExceeded", index, exception.Message); }
        }
        return new MechanicalAssemblyEditResult(draft, batch, draft.WithDefinition(current), changes, sourceResults, Array.Empty<MechanicalDiagnostic>(), null);
    }
}
