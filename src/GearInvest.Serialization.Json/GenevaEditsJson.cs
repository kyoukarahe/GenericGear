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

/// <summary>Ordered actual replay, including rejected batches. Stored current drafts, analyses and comparisons are never replay authorities.</summary>
public sealed class GenevaEditSession
{
    internal GenevaEditSession(GenevaDraft initial, IEnumerable<GenevaEditBatch> batches, IEnumerable<GenevaMotionComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(GenevaProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > GenevaProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > GenevaProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<GenevaEditResult>();
        foreach (var batch in Batches) { var result = GenevaEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current; InitialAnalysis = GenevaAnalyzer.Analyze(initial); Analysis = GenevaAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<GenevaMotionComparisonRequest>()).Take(GenevaProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > GenevaProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => GenevaOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("geneva-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()), current.DraftId,
            Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public GenevaDraft InitialDraft { get; } public GenevaDraft CurrentDraft { get; }
    public ReadOnlyCollection<GenevaEditBatch> Batches { get; } public ReadOnlyCollection<GenevaEditResult> Results { get; }
    public GenevaAnalysis InitialAnalysis { get; } public GenevaAnalysis Analysis { get; }
    public ReadOnlyCollection<GenevaMotionComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<GenevaMotionComparisonResult> Comparisons { get; }
}

public static partial class GenevaJson
{
    public static byte[] WriteBatch(GenevaEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static GenevaEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var batch = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(batch)), "Noncanonical geneva batch."); return batch; });
    public static byte[] WriteEditResult(GenevaEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static GenevaEditSession CreateSession(GenevaDraft initial, IEnumerable<GenevaEditBatch> batches, IEnumerable<GenevaMotionComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(GenevaEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static GenevaEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var session = new GenevaEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", GenevaProfile.MaxBatches).Select(Batch), Items(p, "comparisonRequests", GenevaProfile.MaxComparisons).Select(ComparisonRequest));
        Require(session.SessionId == S(p, "sessionId"), "Geneva session identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(session)), "Fresh ordered atomic reapply, source/device reanalysis or nonlinear comparison differs from saved session."); return session;
    });
    private static void Session(Utf8JsonWriter w, GenevaEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("sessionId", s.SessionId); w.WriteString("exactSemantics", GenevaProfile.AnalysisPolicy);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, GenevaEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static GenevaEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new GenevaEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Geneva batch identity mismatch."); return b;
    }

    private static void Operation(Utf8JsonWriter w,GenevaEditOperation operation)
    {
        w.WriteStartObject();w.WriteString("kind",operation.Kind);
        switch(operation)
        {
            case ApplyGenevaSourceEditsEdit e: w.WritePropertyName("batch");MechanicalAuthoringJson.Batch(w,e.Batch); break;
            case SetGenevaSourceLengthMappingEdit e: w.WritePropertyName("mapping");R.Mapping(w,e.Mapping); break;
            case SetGenevaOrbitEdit e: w.WriteString("deviceId",e.DeviceId); w.WritePropertyName("orbitRadius");Length(w,e.OrbitRadius); break;
            case SetGenevaWheelEdit e: w.WriteString("deviceId",e.DeviceId); w.WritePropertyName("wheel");Wheel(w,e.Wheel); break;
            case SetGenevaSlotRootEdit e: w.WriteString("deviceId",e.DeviceId); R.Quantity(w,"slotRoot",e.SlotRoot); break;
            case SetGenevaSlotMouthEdit e: w.WriteString("deviceId",e.DeviceId); w.WritePropertyName("mouthRadius");Length(w,e.MouthRadius); break;
            case SetGenevaDriverCenterEdit e: w.WriteString("deviceId",e.DeviceId); R.LengthVector(w,"centerMm",e.CenterMm); R.Quantity(w,"station",e.Station); break;
            case SetGenevaOutputPlacementEdit e: w.WriteString("deviceId",e.DeviceId); LengthFrame(w,"frameMm",e.FrameMm); R.LengthVector(w,"centerMm",e.CenterMm); R.Quantity(w,"station",e.Station); break;
            case SetGenevaDriverMountingPhaseEdit e: w.WriteString("deviceId",e.DeviceId); R.Quantity(w,"mountingTurns",e.MountingTurns); break;
            case SetGenevaWheelMountingPhaseEdit e: w.WriteString("deviceId",e.DeviceId); R.Quantity(w,"mountingTurns",e.MountingTurns); break;
            case SetGenevaRegistrationEdit e: w.WriteString("deviceId",e.DeviceId); R.Quantity(w,"referenceTurns",e.ReferenceTurns); Integer(w,"registrationSlot",e.RegistrationSlot); break;
            case SetGenevaLockEdit e: w.WriteString("deviceId",e.DeviceId); w.WritePropertyName("idealLock");IdealLock(w,e.IdealLock); break;
            case SetGenevaLockWindowEdit e: w.WriteString("deviceId",e.DeviceId); R.Interval(w,"releaseWindow",e.ReleaseWindow); break;
            case SetGenevaLockPresenceEdit e: w.WriteString("deviceId",e.DeviceId); w.WriteBoolean("present",e.Present); break;
            case SetGenevaPinPresenceEdit e: w.WriteString("deviceId",e.DeviceId); w.WriteBoolean("present",e.Present); break;
            case SetGenevaTerminalEdit e: w.WriteString("outputKey",e.OutputKey); Integer(w,"sign",e.Sign); R.Quantity(w,"datum",e.Datum); break;
            case ReverseGenevaTerminalEdit e: w.WriteString("outputKey",e.OutputKey); break;
            case ReverseGenevaOutputAxisEdit e: w.WriteString("deviceId",e.DeviceId); break;
            case SetGenevaRequirementsEdit e: w.WriteString("outputKey",e.OutputKey); w.WritePropertyName("requirement");Requirement(w,e.Requirement); break;
            case SetGenevaBindingEdit e: w.WriteString("deviceId",e.DeviceId); w.WriteString("sourceShaftId",e.SourceShaftId); w.WriteString("sourcePortId",e.SourcePortId); break;
            case SetGenevaUnsupportedModeEdit e: w.WriteString("deviceId",e.DeviceId); w.WriteString("mechanismKind",e.MechanismKind); w.WriteString("pinKind",e.PinKind); Integer(w,"pinCount",e.PinCount); w.WriteString("slotKind",e.SlotKind); w.WriteBoolean("outputIsPrescribed",e.OutputIsPrescribed); break;
            case SetGenevaSlotCountEdit e:
                w.WriteString("deviceId",e.DeviceId);Integer(w,"slotCount",e.SlotCount);
                if(e.AddedSlotIds is null) w.WriteNull("addedSlotIds");else MechanicalAuthoringJson.Strings(w,"addedSlotIds",e.AddedSlotIds);
                if(e.AddedRecessIds is null) w.WriteNull("addedRecessIds");else MechanicalAuthoringJson.Strings(w,"addedRecessIds",e.AddedRecessIds);break;
            case SetGenevaRequiredValidationDomainsEdit e: MechanicalAuthoringJson.Strings(w,"domains",e.Domains);break;
            default:throw new ArtifactFormatException("Unsupported typed Geneva edit.");
        }
        w.WriteEndObject();
    }
    private static GenevaEditOperation Operation(JsonElement p) => S(p,"kind") switch
    {
        nameof(ApplyGenevaSourceEditsEdit) => new ApplyGenevaSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("batch"))),
        nameof(SetGenevaSourceLengthMappingEdit) => new SetGenevaSourceLengthMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(SetGenevaOrbitEdit) => new SetGenevaOrbitEdit(S(p,"deviceId"), Length(p.GetProperty("orbitRadius"))),
        nameof(SetGenevaWheelEdit) => new SetGenevaWheelEdit(S(p,"deviceId"), Wheel(p.GetProperty("wheel"))),
        nameof(SetGenevaSlotRootEdit) => new SetGenevaSlotRootEdit(S(p,"deviceId"), R.Quantity(p.GetProperty("slotRoot"))),
        nameof(SetGenevaSlotMouthEdit) => new SetGenevaSlotMouthEdit(S(p,"deviceId"), Length(p.GetProperty("mouthRadius"))),
        nameof(SetGenevaDriverCenterEdit) => new SetGenevaDriverCenterEdit(S(p,"deviceId"), R.LengthVector(p.GetProperty("centerMm")), R.Quantity(p.GetProperty("station"))),
        nameof(SetGenevaOutputPlacementEdit) => new SetGenevaOutputPlacementEdit(S(p,"deviceId"), LengthFrame(p.GetProperty("frameMm")), R.LengthVector(p.GetProperty("centerMm")), R.Quantity(p.GetProperty("station"))),
        nameof(SetGenevaDriverMountingPhaseEdit) => new SetGenevaDriverMountingPhaseEdit(S(p,"deviceId"), R.Quantity(p.GetProperty("mountingTurns"))),
        nameof(SetGenevaWheelMountingPhaseEdit) => new SetGenevaWheelMountingPhaseEdit(S(p,"deviceId"), R.Quantity(p.GetProperty("mountingTurns"))),
        nameof(SetGenevaRegistrationEdit) => new SetGenevaRegistrationEdit(S(p,"deviceId"), R.Quantity(p.GetProperty("referenceTurns")), I(p,"registrationSlot")),
        nameof(SetGenevaLockEdit) => new SetGenevaLockEdit(S(p,"deviceId"), IdealLock(p.GetProperty("idealLock"))),
        nameof(SetGenevaLockWindowEdit) => new SetGenevaLockWindowEdit(S(p,"deviceId"), R.Interval(p.GetProperty("releaseWindow"))),
        nameof(SetGenevaLockPresenceEdit) => new SetGenevaLockPresenceEdit(S(p,"deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetGenevaPinPresenceEdit) => new SetGenevaPinPresenceEdit(S(p,"deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetGenevaTerminalEdit) => new SetGenevaTerminalEdit(S(p,"outputKey"), I(p,"sign"), R.Quantity(p.GetProperty("datum"))),
        nameof(ReverseGenevaTerminalEdit) => new ReverseGenevaTerminalEdit(S(p,"outputKey")),
        nameof(ReverseGenevaOutputAxisEdit) => new ReverseGenevaOutputAxisEdit(S(p,"deviceId")),
        nameof(SetGenevaRequirementsEdit) => new SetGenevaRequirementsEdit(S(p,"outputKey"), Requirement(p.GetProperty("requirement"))),
        nameof(SetGenevaBindingEdit) => new SetGenevaBindingEdit(S(p,"deviceId"), S(p,"sourceShaftId"), MechanicalAuthoringJson.NullableString(p,"sourcePortId")),
        nameof(SetGenevaUnsupportedModeEdit) => new SetGenevaUnsupportedModeEdit(S(p,"deviceId"), S(p,"mechanismKind"), S(p,"pinKind"), I(p,"pinCount"), S(p,"slotKind"), p.GetProperty("outputIsPrescribed").GetBoolean()),
        nameof(SetGenevaSlotCountEdit) => new SetGenevaSlotCountEdit(S(p,"deviceId"),I(p,"slotCount"),
            p.GetProperty("addedSlotIds").ValueKind==JsonValueKind.Null?null:Items(p,"addedSlotIds",64).Select(x=>x.GetString()!),
            p.GetProperty("addedRecessIds").ValueKind==JsonValueKind.Null?null:Items(p,"addedRecessIds",64).Select(x=>x.GetString()!)),
        nameof(SetGenevaRequiredValidationDomainsEdit) => new SetGenevaRequiredValidationDomainsEdit(Items(p,"domains",32).Select(x=>x.GetString()!)),
        _=>throw new ArtifactFormatException("Unsupported typed Geneva edit.")
    };
    private static void EditResult(Utf8JsonWriter w, GenevaEditResult r)
    {
        Start(w, "gear-invest.geneva-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId); w.WriteString("baseDraftId", r.BaseDraftId);
        w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}

