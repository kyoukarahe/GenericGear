using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

/// <summary>Initial assembly and ordered atomic edits are authoritative. Rejected transactions remain in history.</summary>
public sealed class MechanicalAssemblyEditSession
{
    internal MechanicalAssemblyEditSession(MechanicalAssemblyDraft initial, IEnumerable<MechanicalAssemblyEditBatch> batches, AssemblyNumericRequest? numericRequest = null,
        IEnumerable<MechanicalAssemblyOutputComparisonRequest>? comparisonRequests = null)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(MechanicalAssemblyProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > MechanicalAssemblyProfile.MaxBatches || Batches.Any(x => x is null) || Batches.Sum(x => x.OperationCount) > MechanicalAssemblyProfile.MaxTotalOperations)
            throw new ArgumentException("Assembly session batch or nested operation bound exceeded.");
        var current = initial; var results = new List<MechanicalAssemblyEditResult>();
        foreach (var batch in Batches)
        { var result = MechanicalAssemblyEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        CurrentDraft = current; Results = results.AsReadOnly();
        NumericRequest = numericRequest ?? AssemblyNumericRequest.Default;
        InitialAnalysis = MechanicalAssemblyAnalyzer.Analyze(initial, NumericRequest);
        Analysis = current.DraftId == initial.DraftId ? InitialAnalysis : MechanicalAssemblyAnalyzer.Analyze(current, NumericRequest);
        ComparisonRequests = (comparisonRequests ?? System.Array.Empty<MechanicalAssemblyOutputComparisonRequest>()).Take(MechanicalAssemblyProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > MechanicalAssemblyProfile.MaxComparisons || ComparisonRequests.Any(x => x is null)) throw new ArgumentException("Bounded assembly comparison requests required.");
        Comparisons = ComparisonRequests.Select(x => MechanicalAssemblyAnalyzer.CompareOutputs(InitialAnalysis, Analysis, x)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("mechanical-assembly-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(x => x.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(x => x.ResultId).ToArray()), current.DraftId, NumericRequest.CanonicalRepresentation,
            OrientedGoalKeys.Pack(ComparisonRequests.Select(x => x.RequestId).ToArray()))));
    }
    public MechanicalAssemblyDraft InitialDraft { get; }
    public MechanicalAssemblyDraft CurrentDraft { get; }
    public ReadOnlyCollection<MechanicalAssemblyEditBatch> Batches { get; }
    public ReadOnlyCollection<MechanicalAssemblyEditResult> Results { get; }
    public AssemblyNumericRequest NumericRequest { get; }
    public MechanicalAssemblyAnalysis InitialAnalysis { get; }
    public MechanicalAssemblyAnalysis Analysis { get; }
    public ReadOnlyCollection<MechanicalAssemblyOutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<MechanicalAssemblyOutputComparisonResult> Comparisons { get; }
    public string SessionId { get; }
}

public static partial class MechanicalAssemblyJson
{
    public static byte[] WriteBatch(MechanicalAssemblyEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static MechanicalAssemblyEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    {
        using var document = Parse(bytes); var batch = Batch(document.RootElement);
        Require(bytes.SequenceEqual(WriteBatch(batch)), "Noncanonical or unknown assembly edit batch fields."); return batch;
    });
    public static byte[] WriteEditResult(MechanicalAssemblyEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static MechanicalAssemblyEditSession CreateSession(MechanicalAssemblyDraft initial, IEnumerable<MechanicalAssemblyEditBatch> batches, AssemblyNumericRequest? numericRequest = null,
        IEnumerable<MechanicalAssemblyOutputComparisonRequest>? comparisonRequests = null) => new(initial, batches, numericRequest, comparisonRequests);
    public static byte[] WriteSession(MechanicalAssemblyEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static MechanicalAssemblyEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var document = Parse(bytes); var p = document.RootElement; Header(p, SessionFormat, true);
        var session = new MechanicalAssemblyEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", MechanicalAssemblyProfile.MaxBatches).Select(Batch), NumericRequest(p.GetProperty("numericRequest")),
            Items(p, "comparisonRequests", MechanicalAssemblyProfile.MaxComparisons).Select(ComparisonRequest));
        Require(bytes.SequenceEqual(WriteSession(session)), "Fresh atomic assembly session replay or current identity differs from stored session."); return session;
    });
    internal static void Session(Utf8JsonWriter w, MechanicalAssemblyEditSession session)
    {
        Start(w, SessionFormat, VersionFor(session.InitialDraft.Definition.Profile)); w.WriteString("analysisPolicy", session.Analysis.Policy); w.WriteString("sessionId", session.SessionId);
        w.WritePropertyName("numericRequest"); NumericRequest(w, session.NumericRequest);
        w.WritePropertyName("initialDraft"); Draft(w, session.InitialDraft); Array(w, "batches", session.Batches, Batch); Array(w, "results", session.Results, EditResult);
        // Current state is reconstructed, so original root and seed bytes need appear only in initialDraft.
        w.WriteStartObject("current"); w.WriteString("draftId", session.CurrentDraft.DraftId); w.WriteString("definitionId", session.CurrentDraft.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", session.CurrentDraft.Revision); w.WriteEndObject();
        w.WritePropertyName("initialAnalysis"); Analysis(w, session.InitialAnalysis); w.WritePropertyName("analysis"); Analysis(w, session.Analysis);
        Array(w, "comparisonRequests", session.ComparisonRequests, ComparisonRequest); Array(w, "comparisons", session.Comparisons, Comparison); w.WriteEndObject();
    }
    internal static void Batch(Utf8JsonWriter w, MechanicalAssemblyEditBatch batch)
    {
        Start(w, BatchFormat); w.WriteString("requestId", batch.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", batch.ExpectedRevision);
        w.WriteString("expectedDefinitionId", batch.ExpectedDefinitionId); Integer(w, "operationCount", batch.OperationCount);
        Array(w, "operations", batch.Operations, Operation); w.WriteEndObject();
    }
    internal static MechanicalAssemblyEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var batch = new MechanicalAssemblyEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"),
            Items(p, "operations", MechanicalAuthoringProfile.MaxOperations).Select(Operation));
        Require(batch.RequestId == S(p, "requestId") && batch.OperationCount == I(p, "operationCount"), "Assembly batch identity or nested operation count mismatch."); return batch;
    }
    internal static void Operation(Utf8JsonWriter w, MechanicalAssemblyEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyMechanicalAssemblyRootEditsEdit edit: w.WritePropertyName("rootBatch"); MechanicalAuthoringJson.Batch(w, edit.Batch); break;
            case SetMechanicalAssemblyRootMappingEdit edit: w.WritePropertyName("mapping"); R.Mapping(w, edit.Mapping); break;
            case AddMechanicalAssemblyMemberEdit edit: w.WritePropertyName("member"); Member(w, edit.Member); break;
            case ReplaceMechanicalAssemblyMemberEdit edit: w.WritePropertyName("member"); Member(w, edit.Member); break;
            case RemoveMechanicalAssemblyMemberEdit edit: w.WriteString("instanceId", edit.InstanceId); break;
            case SetMechanicalAssemblyBindingEdit edit: w.WriteString("instanceId", edit.InstanceId); w.WritePropertyName("binding"); Binding(w, edit.Binding); break;
            case SetMechanicalAssemblyOutputEdit edit: w.WritePropertyName("output"); Output(w, edit.Output); break;
            case RemoveMechanicalAssemblyOutputEdit edit: w.WriteString("key", edit.Key); break;
            case SetMechanicalAssemblyRequiredValidationEdit edit: MechanicalAuthoringJson.Strings(w, "requiredDomains", edit.RequiredDomains); break;
            default: throw new ArtifactFormatException("Unsupported typed assembly edit operation.");
        }
        w.WriteEndObject();
    }
    internal static MechanicalAssemblyEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyMechanicalAssemblyRootEditsEdit) => new ApplyMechanicalAssemblyRootEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("rootBatch"))),
        nameof(SetMechanicalAssemblyRootMappingEdit) => new SetMechanicalAssemblyRootMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(AddMechanicalAssemblyMemberEdit) => new AddMechanicalAssemblyMemberEdit(Member(p.GetProperty("member"))),
        nameof(ReplaceMechanicalAssemblyMemberEdit) => new ReplaceMechanicalAssemblyMemberEdit(Member(p.GetProperty("member"))),
        nameof(RemoveMechanicalAssemblyMemberEdit) => new RemoveMechanicalAssemblyMemberEdit(S(p, "instanceId")),
        nameof(SetMechanicalAssemblyBindingEdit) => new SetMechanicalAssemblyBindingEdit(S(p, "instanceId"), Binding(p.GetProperty("binding"))),
        nameof(SetMechanicalAssemblyOutputEdit) => new SetMechanicalAssemblyOutputEdit(Output(p.GetProperty("output"))),
        nameof(RemoveMechanicalAssemblyOutputEdit) => new RemoveMechanicalAssemblyOutputEdit(S(p, "key")),
        nameof(SetMechanicalAssemblyRequiredValidationEdit) => new SetMechanicalAssemblyRequiredValidationEdit(Items(p, "requiredDomains", 32).Select(x => x.GetString()!)),
        _ => throw new ArtifactFormatException("Unsupported typed assembly edit operation.")
    };
    internal static void EditResult(Utf8JsonWriter w, MechanicalAssemblyEditResult result)
    {
        Start(w, "gear-invest.mechanical-assembly-edit-result"); w.WriteString("resultId", result.ResultId); w.WriteString("requestId", result.RequestId);
        w.WriteString("baseDraftId", result.BaseDraftId); w.WriteString("status", result.Status.ToString()); w.WriteString("draftId", result.Draft?.DraftId);
        w.WriteString("definitionId", result.Draft?.DefinitionId);
        if (result.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", result.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", result.IsNetEmpty); w.WriteBoolean("requiresReanalysis", result.RequiresReanalysis);
        Array(w, "changes", result.Changes, (writer, change) =>
        {
            writer.WriteStartObject(); Integer(writer, "operationIndex", change.OperationIndex); writer.WriteString("operationKind", change.OperationKind);
            writer.WriteString("beforeDefinitionId", change.BeforeDefinitionId); writer.WriteString("afterDefinitionId", change.AfterDefinitionId);
            writer.WriteBoolean("isUnchanged", change.IsUnchanged); writer.WriteEndObject();
        });
        Array(w, "sourceResults", result.SourceResults, MechanicalAuthoringJson.EditResult);
        Array(w, "diagnostics", result.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
