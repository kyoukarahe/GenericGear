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
public sealed class CrankSliderEditSession
{
    internal CrankSliderEditSession(CrankSliderDraft initial, IEnumerable<CrankSliderEditBatch> batches, IEnumerable<CrankSliderOutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(CrankSliderProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > CrankSliderProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > CrankSliderProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<CrankSliderEditResult>();
        foreach (var batch in Batches) { var result = CrankSliderEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current; InitialAnalysis = CrankSliderAnalyzer.Analyze(initial); Analysis = CrankSliderAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<CrankSliderOutputComparisonRequest>()).Take(CrankSliderProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > CrankSliderProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => CrankSliderOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("crank-slider-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()), current.DraftId,
            Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public CrankSliderDraft InitialDraft { get; } public CrankSliderDraft CurrentDraft { get; }
    public ReadOnlyCollection<CrankSliderEditBatch> Batches { get; } public ReadOnlyCollection<CrankSliderEditResult> Results { get; }
    public CrankSliderAnalysis InitialAnalysis { get; } public CrankSliderAnalysis Analysis { get; }
    public ReadOnlyCollection<CrankSliderOutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<CrankSliderOutputEquivalenceResult> Comparisons { get; }
}

public static partial class CrankSliderJson
{
    public static byte[] WriteBatch(CrankSliderEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static CrankSliderEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var batch = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(batch)), "Noncanonical crank-slider batch."); return batch; });
    public static byte[] WriteEditResult(CrankSliderEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static CrankSliderEditSession CreateSession(CrankSliderDraft initial, IEnumerable<CrankSliderEditBatch> batches, IEnumerable<CrankSliderOutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(CrankSliderEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static CrankSliderEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var session = new CrankSliderEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", CrankSliderProfile.MaxBatches).Select(Batch), Items(p, "comparisonRequests", CrankSliderProfile.MaxComparisons).Select(ComparisonRequest));
        Require(session.SessionId == S(p, "sessionId"), "Crank-slider session identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(session)), "Fresh ordered atomic reapply, source/device reanalysis or nonlinear comparison differs from saved session."); return session;
    });
    private static void Session(Utf8JsonWriter w, CrankSliderEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("sessionId", s.SessionId); w.WriteString("exactSemantics", CrankSliderProfile.AnalysisPolicy);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, CrankSliderEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static CrankSliderEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new CrankSliderEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Crank-slider batch identity mismatch."); return b;
    }
    private static void Operation(Utf8JsonWriter w, CrankSliderEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyCrankSliderSourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetCrankSliderSourceLengthMappingEdit e: w.WritePropertyName("mapping"); R.Mapping(w, e.Mapping); break;
            case SetCrankSliderRadiusEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "radius", e.Radius); break;
            case SetCrankSliderRodLengthEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "rodLength", e.RodLength); break;
            case SetCrankSliderGuidePlacementEdit e: w.WriteString("deviceId", e.DeviceId); LengthFrame(w, "guideFrameMm", e.GuideFrameMm); Vector(w, "planeNormal", e.PlaneNormal); break;
            case SetCrankSliderPivotEdit e: w.WriteString("deviceId", e.DeviceId); R.LengthVector(w, "pivotMm", e.PivotMm); R.Quantity(w, "pivotStation", e.PivotStation); break;
            case SetCrankSliderMountingPhaseEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "mountingTurns", e.MountingTurns); break;
            case SetCrankSliderBranchEdit e: w.WriteString("deviceId", e.DeviceId); if (e.AssemblyBranch.HasValue) Integer(w, "assemblyBranch", e.AssemblyBranch.Value); else w.WriteNull("assemblyBranch"); break;
            case SetCrankSliderGuideTravelEdit e: w.WriteString("deviceId", e.DeviceId); R.Interval(w, "guideTravel", e.GuideTravel); break;
            case SetCrankSliderTerminalEdit e: w.WriteString("outputKey", e.OutputKey); Integer(w, "sign", e.Sign); R.Quantity(w, "datum", e.Datum); break;
            case SetCrankSliderRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); w.WritePropertyName("requirement"); Requirement(w, e.Requirement); break;
            case SetCrankSliderTransmissionEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); break;
            case SetCrankSliderGroundingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("guidePresent", e.GuidePresent); w.WriteBoolean("sliderRotationFixed", e.SliderRotationFixed); w.WriteBoolean("transverseMotionFixed", e.TransverseMotionFixed); w.WriteBoolean("crankAxisFixed", e.CrankAxisFixed); break;
            case SetCrankSliderBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("sourceShaftId", e.SourceShaftId); w.WriteString("sourcePortId", e.SourcePortId); break;
            case SetCrankSliderJointPresenceEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("crankPinPresent", e.CrankPinPresent); w.WriteBoolean("sliderPinPresent", e.SliderPinPresent); break;
            case SetCrankSliderInputTopologyEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("sliderIsPrescribed", e.SliderIsPrescribed); break;
            case SetCrankSliderRequiredValidationDomainsEdit e: MechanicalAuthoringJson.Strings(w, "domains", e.Domains); break;
            default: throw new ArtifactFormatException("Unsupported typed crank-slider operation.");
        }
        w.WriteEndObject();
    }
    private static CrankSliderEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyCrankSliderSourceEditsEdit) => new ApplyCrankSliderSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetCrankSliderSourceLengthMappingEdit) => new SetCrankSliderSourceLengthMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(SetCrankSliderRadiusEdit) => new SetCrankSliderRadiusEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("radius"))),
        nameof(SetCrankSliderRodLengthEdit) => new SetCrankSliderRodLengthEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("rodLength"))),
        nameof(SetCrankSliderGuidePlacementEdit) => new SetCrankSliderGuidePlacementEdit(S(p, "deviceId"), LengthFrame(p.GetProperty("guideFrameMm")), Vector(p.GetProperty("planeNormal"))),
        nameof(SetCrankSliderPivotEdit) => new SetCrankSliderPivotEdit(S(p, "deviceId"), R.LengthVector(p.GetProperty("pivotMm")), R.Quantity(p.GetProperty("pivotStation"))),
        nameof(SetCrankSliderMountingPhaseEdit) => new SetCrankSliderMountingPhaseEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("mountingTurns"))),
        nameof(SetCrankSliderBranchEdit) => new SetCrankSliderBranchEdit(S(p, "deviceId"), p.GetProperty("assemblyBranch").ValueKind == JsonValueKind.Null ? (int?)null : I(p, "assemblyBranch")),
        nameof(SetCrankSliderGuideTravelEdit) => new SetCrankSliderGuideTravelEdit(S(p, "deviceId"), R.Interval(p.GetProperty("guideTravel"))),
        nameof(SetCrankSliderTerminalEdit) => new SetCrankSliderTerminalEdit(S(p, "outputKey"), I(p, "sign"), R.Quantity(p.GetProperty("datum"))),
        nameof(SetCrankSliderRequirementsEdit) => new SetCrankSliderRequirementsEdit(S(p, "outputKey"), Requirement(p.GetProperty("requirement"))),
        nameof(SetCrankSliderTransmissionEdit) => new SetCrankSliderTransmissionEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetCrankSliderGroundingEdit) => new SetCrankSliderGroundingEdit(S(p, "deviceId"), p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("sliderRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean(), p.GetProperty("crankAxisFixed").GetBoolean()),
        nameof(SetCrankSliderBindingEdit) => new SetCrankSliderBindingEdit(S(p, "deviceId"), S(p, "sourceShaftId"), MechanicalAuthoringJson.NullableString(p, "sourcePortId")),
        nameof(SetCrankSliderJointPresenceEdit) => new SetCrankSliderJointPresenceEdit(S(p, "deviceId"), p.GetProperty("crankPinPresent").GetBoolean(), p.GetProperty("sliderPinPresent").GetBoolean()),
        nameof(SetCrankSliderInputTopologyEdit) => new SetCrankSliderInputTopologyEdit(S(p, "deviceId"), p.GetProperty("sliderIsPrescribed").GetBoolean()),
        nameof(SetCrankSliderRequiredValidationDomainsEdit) => new SetCrankSliderRequiredValidationDomainsEdit(Items(p, "domains", 24).Select(x => x.GetString()!)),
        _ => throw new ArtifactFormatException("Unsupported typed crank-slider edit operation.")
    };
    private static void EditResult(Utf8JsonWriter w, CrankSliderEditResult r)
    {
        Start(w, "gear-invest.crank-slider-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId); w.WriteString("baseDraftId", r.BaseDraftId);
        w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
