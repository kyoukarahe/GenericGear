using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Ordered replayable edit requests and derived caches. No old proof or result substitutes for reapplication.</summary>
public sealed class MechanicalEditSession
{
    internal MechanicalEditSession(MechanicalDraft initial, IEnumerable<MechanicalEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(129).ToList().AsReadOnly();
        if (Batches.Count > 128 || Batches.Any(b => b is null) || Batches.Sum(b => b.Operations.Count) > 512) throw new ArgumentException("Session batch/total operation bound exceeded.");
        var current = initial; var results = new List<MechanicalEditResult>();
        foreach (var batch in Batches) { var result = MechanicalEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current; InitialAnalysis = MechanicalAnalyzer.Analyze(initial); Analysis = MechanicalAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<OutputComparisonRequest>()).Take(65).ToList().AsReadOnly();
        if (ComparisonRequests.Count > 64 || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => MechanicalOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(System.Text.Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("mechanical-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()), current.DraftId, Analysis.AnalysisId,
            OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; }
    public MechanicalDraft InitialDraft { get; }
    public ReadOnlyCollection<MechanicalEditBatch> Batches { get; }
    public ReadOnlyCollection<MechanicalEditResult> Results { get; }
    public MechanicalDraft CurrentDraft { get; }
    public MechanicalAnalysis Analysis { get; }
    public MechanicalAnalysis InitialAnalysis { get; }
    public ReadOnlyCollection<OutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<OutputEquivalenceResult> Comparisons { get; }
}

public static partial class MechanicalAuthoringJson
{
    public static byte[] WriteBatch(MechanicalEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static MechanicalEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var batch = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(batch)), "Noncanonical or unknown ordered batch fields."); return batch; });
    public static byte[] WriteEditResult(MechanicalEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static MechanicalEditSession CreateSession(MechanicalDraft initial, IEnumerable<MechanicalEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(MechanicalEditSession session) => Guard(() => Encode(w => Session(w, session)));
    /// <summary>Fresh actual Apply and Analyze. A valid hash cannot bless forged operation results/current definition/analysis caches.</summary>
    public static MechanicalEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement;
        var session = new MechanicalEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", 128).Select(Batch), Items(p, "comparisonRequests", 64).Select(ComparisonRequest));
        Require(S(p, "format") == SessionFormat && S(p, "formatVersion") == VersionFor(session.InitialDraft.Definition), "Unsupported mechanical session format/profile version.");
        Require(session.SessionId == S(p, "sessionId"), "Session request/current binding identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(session)), "Reapplied edit results, current definition or reanalyzed cache differ from the stored session."); return session;
    });
    internal static void Session(Utf8JsonWriter w, MechanicalEditSession session)
    {
        Start(w, SessionFormat, VersionFor(session.InitialDraft.Definition)); w.WriteString("sessionId", session.SessionId); w.WritePropertyName("initialDraft"); Draft(w, session.InitialDraft);
        Array(w, "batches", session.Batches, Batch); Array(w, "results", session.Results, EditResult);
        w.WriteStartObject("current"); w.WriteString("draftId", session.CurrentDraft.DraftId); w.WriteString("definitionId", session.CurrentDraft.DefinitionId);
        Long(w, "revision", session.CurrentDraft.Revision); w.WritePropertyName("definition"); Definition(w, session.CurrentDraft.Definition); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, session.Analysis); Array(w, "comparisonRequests", session.ComparisonRequests, ComparisonRequest);
        Array(w, "comparisons", session.Comparisons, Comparison); w.WriteEndObject();
    }
    internal static void Batch(Utf8JsonWriter w, MechanicalEditBatch batch)
    {
        Start(w, BatchFormat); w.WriteString("requestId", batch.RequestId); Long(w, "expectedRevision", batch.ExpectedRevision);
        w.WriteString("expectedDefinitionId", batch.ExpectedDefinitionId); Array(w, "operations", batch.Operations, Operation); w.WriteEndObject();
    }
    internal static MechanicalEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var batch = new MechanicalEditBatch(Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", MechanicalAuthoringProfile.MaxOperations).Select(Operation));
        Require(batch.RequestId == S(p, "requestId"), "Ordered edit request identity mismatch."); return batch;
    }
    internal static void Operation(Utf8JsonWriter w, MechanicalEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case AddShaftEdit e: w.WritePropertyName("shaft"); WriteShaft(w, e.Shaft); break;
            case RemoveShaftEdit e: w.WriteString("shaftId", e.ShaftId); w.WriteBoolean("removePrescribedInput", e.RemovePrescribedInput); break;
            case AddBodyEdit e: w.WritePropertyName("body"); Body(w, e.Body); break;
            case RemoveBodyEdit e: w.WriteString("bodyId", e.BodyId); w.WriteString("policy", e.Policy.ToString()); break;
            case AddContactEdit e: w.WritePropertyName("contact"); Contact(w, e.Contact); break;
            case RemoveContactEdit e: w.WriteString("contactId", e.ContactId); break;
            case RebindContactEdit e: w.WriteString("contactId", e.ContactId); w.WriteString("bodyAId", e.BodyAId); w.WriteString("bodyBId", e.BodyBId); break;
            case SetToothCountEdit e: w.WriteString("bodyId", e.BodyId); Integer(w, "teeth", e.Teeth); w.WriteString("policy", e.Policy.ToString()); break;
            case SetBodyMountingEdit e: w.WriteString("bodyId", e.BodyId); Frame(w, "frame", e.Frame); break;
            case RebindBodyShaftEdit e: w.WriteString("bodyId", e.BodyId); w.WriteString("shaftId", e.ShaftId); break;
            case SetParallelCoaxialLayoutEdit e: w.WritePropertyName("layout"); CoaxialLayout(w, e.Layout); break;
            case MoveShaftGroupEdit e: w.WriteString("shaftId", e.ShaftId); Frame(w, "worldRigidTransform", e.WorldRigidTransform); break;
            case SetPortEdit e: w.WritePropertyName("port"); WritePort(w, e.Port); break;
            case SetPortConnectionEdit e: w.WritePropertyName("connection"); Connection(w, e.Connection); break;
            case RemovePortConnectionEdit e: w.WriteString("connectionId", e.ConnectionId); break;
            case BindOutputEdit e: w.WriteString("key", e.Key); w.WriteString("shaftId", e.ShaftId); w.WriteString("bodyId", e.BodyId); w.WriteString("portId", e.PortId); w.WriteString("role", e.Role?.ToString()); break;
            case SetOutputTerminalEdit e: w.WriteString("key", e.Key); Frame(w, "frame", e.Frame); break;
            case SetRequestedTransferEdit e: w.WriteString("key", e.Key); OptionalFraction(w, "requiredTransfer", e.RequiredTransfer); break;
            case AddKeepOutEdit e: w.WritePropertyName("keepOut"); KeepOut(w, e.KeepOut); break;
            case RemoveKeepOutEdit e: w.WriteString("keepOutId", e.KeepOutId); break;
            case SetClearancePolicyEdit e: w.WriteString("clearancePolicy", e.ClearancePolicy); w.WriteBoolean("requireCrossComponentClearance", e.RequireCrossComponentClearance); break;
            default: throw new ArtifactFormatException("Unsupported typed edit operation.");
        }
        w.WriteEndObject();
    }
    internal static MechanicalEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(AddShaftEdit) => new AddShaftEdit(ReadShaft(p.GetProperty("shaft"))),
        nameof(RemoveShaftEdit) => new RemoveShaftEdit(S(p, "shaftId"), p.GetProperty("removePrescribedInput").GetBoolean()),
        nameof(AddBodyEdit) => new AddBodyEdit(Body(p.GetProperty("body"))),
        nameof(RemoveBodyEdit) => new RemoveBodyEdit(S(p, "bodyId"), E<MechanicalRemovalPolicy>(p, "policy")),
        nameof(AddContactEdit) => new AddContactEdit(Contact(p.GetProperty("contact"))),
        nameof(RemoveContactEdit) => new RemoveContactEdit(S(p, "contactId")),
        nameof(RebindContactEdit) => new RebindContactEdit(S(p, "contactId"), S(p, "bodyAId"), S(p, "bodyBId")),
        nameof(SetToothCountEdit) => new SetToothCountEdit(S(p, "bodyId"), I(p, "teeth"), E<ToothDimensionPolicy>(p, "policy")),
        nameof(SetBodyMountingEdit) => new SetBodyMountingEdit(S(p, "bodyId"), LooseFrame(p.GetProperty("frame"))),
        nameof(RebindBodyShaftEdit) => new RebindBodyShaftEdit(S(p, "bodyId"), S(p, "shaftId")),
        nameof(SetParallelCoaxialLayoutEdit) => new SetParallelCoaxialLayoutEdit(CoaxialLayout(p.GetProperty("layout"))),
        nameof(MoveShaftGroupEdit) => new MoveShaftGroupEdit(S(p, "shaftId"), LooseFrame(p.GetProperty("worldRigidTransform"))),
        nameof(SetPortEdit) => new SetPortEdit(ReadPort(p.GetProperty("port"))),
        nameof(SetPortConnectionEdit) => new SetPortConnectionEdit(Connection(p.GetProperty("connection"))),
        nameof(RemovePortConnectionEdit) => new RemovePortConnectionEdit(S(p, "connectionId")),
        nameof(BindOutputEdit) => new BindOutputEdit(S(p, "key"), S(p, "shaftId"), S(p, "bodyId"), S(p, "portId"), p.GetProperty("role").ValueKind == JsonValueKind.Null ? null : E<OrientedOutputRole>(p, "role")),
        nameof(SetOutputTerminalEdit) => new SetOutputTerminalEdit(S(p, "key"), LooseFrame(p.GetProperty("frame"))),
        nameof(SetRequestedTransferEdit) => new SetRequestedTransferEdit(S(p, "key"), OptionalFraction(p, "requiredTransfer")),
        nameof(AddKeepOutEdit) => new AddKeepOutEdit(KeepOut(p.GetProperty("keepOut"))),
        nameof(RemoveKeepOutEdit) => new RemoveKeepOutEdit(S(p, "keepOutId")),
        nameof(SetClearancePolicyEdit) => new SetClearancePolicyEdit(S(p, "clearancePolicy"), p.GetProperty("requireCrossComponentClearance").GetBoolean()),
        _ => throw new ArtifactFormatException("Unsupported typed mechanical edit operation.")
    };
    internal static void EditResult(Utf8JsonWriter w, MechanicalEditResult r)
    {
        Start(w, EditResultFormat); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId); w.WriteString("baseDraftId", r.BaseDraftId);
        w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        Array(w, "changes", r.Changes, (a, c) => { a.WriteStartObject(); Integer(a, "operationIndex", c.OperationIndex); a.WritePropertyName("subject"); Reference(a, c.Subject); a.WriteString("action", c.Action);
            a.WriteString("before", c.Before); a.WriteString("after", c.After); a.WriteBoolean("dependent", c.Dependent); a.WriteEndObject(); });
        w.WriteStartObject("netChanges"); Array(w, "added", r.NetChanges.Added, Reference); Array(w, "removed", r.NetChanges.Removed, Reference); Array(w, "modified", r.NetChanges.Modified, Reference); w.WriteBoolean("isEmpty", r.NetChanges.IsEmpty); w.WriteEndObject();
        Array(w, "directlyChanged", r.DirectlyChanged, Reference); Strings(w, "potentiallyAffectedOutputs", r.PotentiallyAffectedOutputs);
        w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis); w.WriteBoolean("potentialWholeSpatialImpact", r.PotentialWholeSpatialImpact);
        Array(w, "diagnostics", r.Diagnostics, Diagnostic); w.WriteEndObject();
    }
    internal static void Strings(Utf8JsonWriter w, string key, IEnumerable<string> strings) => Array(w, key, strings, (a, s) => a.WriteStringValue(s));
}
