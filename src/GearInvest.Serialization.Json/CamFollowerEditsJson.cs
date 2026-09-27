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
public sealed class CamFollowerEditSession
{
    internal CamFollowerEditSession(CamFollowerDraft initial, IEnumerable<CamFollowerEditBatch> batches, IEnumerable<CamFollowerOutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(CamFollowerProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > CamFollowerProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > CamFollowerProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<CamFollowerEditResult>();
        foreach (var batch in Batches) { var result = CamFollowerEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current; InitialAnalysis = CamFollowerAnalyzer.Analyze(initial); Analysis = CamFollowerAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<CamFollowerOutputComparisonRequest>()).Take(CamFollowerProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > CamFollowerProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => CamFollowerOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("cam-follower-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()), current.DraftId,
            Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public CamFollowerDraft InitialDraft { get; } public CamFollowerDraft CurrentDraft { get; }
    public ReadOnlyCollection<CamFollowerEditBatch> Batches { get; } public ReadOnlyCollection<CamFollowerEditResult> Results { get; }
    public CamFollowerAnalysis InitialAnalysis { get; } public CamFollowerAnalysis Analysis { get; }
    public ReadOnlyCollection<CamFollowerOutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<CamFollowerOutputEquivalenceResult> Comparisons { get; }
}

public static partial class CamFollowerJson
{
    public static byte[] WriteBatch(CamFollowerEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static CamFollowerEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var batch = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(batch)), "Noncanonical cam-follower batch."); return batch; });
    public static byte[] WriteEditResult(CamFollowerEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static CamFollowerEditSession CreateSession(CamFollowerDraft initial, IEnumerable<CamFollowerEditBatch> batches, IEnumerable<CamFollowerOutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(CamFollowerEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static CamFollowerEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var session = new CamFollowerEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", CamFollowerProfile.MaxBatches).Select(Batch), Items(p, "comparisonRequests", CamFollowerProfile.MaxComparisons).Select(ComparisonRequest));
        Require(session.SessionId == S(p, "sessionId"), "Cam-follower session identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(session)), "Fresh ordered atomic reapply, source/device reanalysis or nonlinear comparison differs from saved session."); return session;
    });
    private static void Session(Utf8JsonWriter w, CamFollowerEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("sessionId", s.SessionId); w.WriteString("exactSemantics", CamFollowerProfile.AnalysisPolicy);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, CamFollowerEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static CamFollowerEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new CamFollowerEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Cam-follower batch identity mismatch."); return b;
    }
    private static void Operation(Utf8JsonWriter w, CamFollowerEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyCamFollowerSourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetCamFollowerSourceLengthMappingEdit e: w.WritePropertyName("mapping"); R.Mapping(w, e.Mapping); break;
            case SetCamFollowerGuidePlacementEdit e: w.WriteString("deviceId", e.DeviceId); LengthFrame(w, "guideFrameMm", e.GuideFrameMm); Vector(w, "planeNormal", e.PlaneNormal); break;
            case SetCamFollowerCenterEdit e: w.WriteString("deviceId", e.DeviceId); R.LengthVector(w, "centerMm", e.CenterMm); R.Quantity(w, "camStation", e.CamStation); break;
            case SetCamFollowerMountingPhaseEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "mountingTurns", e.MountingTurns); break;
            case SetCamFollowerGuideTravelEdit e: w.WriteString("deviceId", e.DeviceId); R.Interval(w, "guideTravel", e.GuideTravel); break;
            case SetCamFollowerTerminalEdit e: w.WriteString("outputKey", e.OutputKey); Integer(w, "sign", e.Sign); R.Quantity(w, "datum", e.Datum); break;
            case SetCamFollowerRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); w.WritePropertyName("requirement"); Requirement(w, e.Requirement); break;
            case SetCamSupportProfileEdit e: w.WriteString("deviceId", e.DeviceId); w.WritePropertyName("profile"); SupportProfile(w, e.Profile); break;
            case SetCamSupportSegmentEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("segmentId", e.SegmentId); w.WritePropertyName("segment"); Segment(w, e.Segment); break;
            case SetCamFollowerFaceEdit e: w.WriteString("deviceId", e.DeviceId); R.Interval(w, "followerFace", e.FollowerFace); break;
            case SetCamFollowerContactEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); w.WriteString("policy", e.Policy); break;
            case SetCamFollowerGroundingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("guidePresent", e.GuidePresent); w.WriteBoolean("followerRotationFixed", e.FollowerRotationFixed); w.WriteBoolean("transverseMotionFixed", e.TransverseMotionFixed); w.WriteBoolean("camAxisFixed", e.CamAxisFixed); break;
            case SetCamFollowerBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("sourceShaftId", e.SourceShaftId); w.WriteString("sourcePortId", e.SourcePortId); break;
            case SetCamFollowerInputTopologyEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("followerIsPrescribed", e.FollowerIsPrescribed); break;
            case SetCamFollowerRequiredValidationDomainsEdit e: MechanicalAuthoringJson.Strings(w, "domains", e.Domains); break;
            default: throw new ArtifactFormatException("Unsupported typed cam-follower operation.");
        }
        w.WriteEndObject();
    }
    private static CamFollowerEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyCamFollowerSourceEditsEdit) => new ApplyCamFollowerSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetCamFollowerSourceLengthMappingEdit) => new SetCamFollowerSourceLengthMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(SetCamFollowerGuidePlacementEdit) => new SetCamFollowerGuidePlacementEdit(S(p, "deviceId"), LengthFrame(p.GetProperty("guideFrameMm")), Vector(p.GetProperty("planeNormal"))),
        nameof(SetCamFollowerCenterEdit) => new SetCamFollowerCenterEdit(S(p, "deviceId"), R.LengthVector(p.GetProperty("centerMm")), R.Quantity(p.GetProperty("camStation"))),
        nameof(SetCamFollowerMountingPhaseEdit) => new SetCamFollowerMountingPhaseEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("mountingTurns"))),
        nameof(SetCamFollowerGuideTravelEdit) => new SetCamFollowerGuideTravelEdit(S(p, "deviceId"), R.Interval(p.GetProperty("guideTravel"))),
        nameof(SetCamFollowerTerminalEdit) => new SetCamFollowerTerminalEdit(S(p, "outputKey"), I(p, "sign"), R.Quantity(p.GetProperty("datum"))),
        nameof(SetCamFollowerRequirementsEdit) => new SetCamFollowerRequirementsEdit(S(p, "outputKey"), Requirement(p.GetProperty("requirement"))),
        nameof(SetCamSupportProfileEdit) => new SetCamSupportProfileEdit(S(p, "deviceId"), SupportProfile(p.GetProperty("profile"))),
        nameof(SetCamSupportSegmentEdit) => new SetCamSupportSegmentEdit(S(p, "deviceId"), S(p, "segmentId"), Segment(p.GetProperty("segment"))),
        nameof(SetCamFollowerFaceEdit) => new SetCamFollowerFaceEdit(S(p, "deviceId"), R.Interval(p.GetProperty("followerFace"))),
        nameof(SetCamFollowerContactEdit) => new SetCamFollowerContactEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean(), MechanicalAuthoringJson.NullableString(p, "policy")),
        nameof(SetCamFollowerGroundingEdit) => new SetCamFollowerGroundingEdit(S(p, "deviceId"), p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("followerRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean(), p.GetProperty("camAxisFixed").GetBoolean()),
        nameof(SetCamFollowerBindingEdit) => new SetCamFollowerBindingEdit(S(p, "deviceId"), S(p, "sourceShaftId"), MechanicalAuthoringJson.NullableString(p, "sourcePortId")),
        nameof(SetCamFollowerInputTopologyEdit) => new SetCamFollowerInputTopologyEdit(S(p, "deviceId"), p.GetProperty("followerIsPrescribed").GetBoolean()),
        nameof(SetCamFollowerRequiredValidationDomainsEdit) => new SetCamFollowerRequiredValidationDomainsEdit(Items(p, "domains", 24).Select(x => x.GetString()!)),
        _ => throw new ArtifactFormatException("Unsupported typed cam-follower edit operation.")
    };
    private static void EditResult(Utf8JsonWriter w, CamFollowerEditResult r)
    {
        Start(w, "gear-invest.cam-follower-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId); w.WriteString("baseDraftId", r.BaseDraftId);
        w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
