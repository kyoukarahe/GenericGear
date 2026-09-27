using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Actual ordered replay of source/device requests. Cached changes, laws, domains and comparisons are verified, never imported as authority.</summary>
public sealed class RackPinionEditSession
{
    internal RackPinionEditSession(RackPinionDraft initial, IEnumerable<RackPinionEditBatch> batches, IEnumerable<LinearOutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(RackPinionProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > RackPinionProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > RackPinionProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<RackPinionEditResult>();
        foreach (var batch in Batches) { var result = RackPinionEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current;
        InitialAnalysis = RackPinionAnalyzer.Analyze(initial); Analysis = RackPinionAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<LinearOutputComparisonRequest>()).Take(RackPinionProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > RackPinionProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => RackPinionOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("rack-pinion-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()),
            current.DraftId, Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public RackPinionDraft InitialDraft { get; } public RackPinionDraft CurrentDraft { get; }
    public ReadOnlyCollection<RackPinionEditBatch> Batches { get; } public ReadOnlyCollection<RackPinionEditResult> Results { get; }
    public RackPinionAnalysis InitialAnalysis { get; } public RackPinionAnalysis Analysis { get; }
    public ReadOnlyCollection<LinearOutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<PrismaticOutputEquivalenceResult> Comparisons { get; }
}

public static partial class RackPinionJson
{
    public static byte[] WriteBatch(RackPinionEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static RackPinionEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var b = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(b)), "Noncanonical rack-pinion batch."); return b; });
    public static byte[] WriteEditResult(RackPinionEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static RackPinionEditSession CreateSession(RackPinionDraft initial, IEnumerable<RackPinionEditBatch> batches, IEnumerable<LinearOutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(RackPinionEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static RackPinionEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var s = new RackPinionEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", RackPinionProfile.MaxBatches).Select(Batch),
            Items(p, "comparisonRequests", RackPinionProfile.MaxComparisons).Select(RotaryLinearJson.ComparisonRequest));
        Require(s.SessionId == S(p, "sessionId"), "Rack session request/current identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(s)), "Fresh atomic reapply, rack reanalysis or comparison differs from stored session."); return s;
    });
    private static void Session(Utf8JsonWriter w, RackPinionEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("numericSemantics", RackPinionProfile.NumericSemantics); w.WriteString("sessionId", s.SessionId);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, RotaryLinearJson.ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, RackPinionEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static RackPinionEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new RackPinionEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Rack batch identity mismatch."); return b;
    }
    private static void Operation(Utf8JsonWriter w, RackPinionEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyRackSourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetRackSourceLengthMappingEdit e: w.WritePropertyName("mapping"); RotaryLinearJson.Mapping(w, e.Mapping); break;
            case SetRackPinionTeethEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "toothCount", e.ToothCount); break;
            case SetRackPinionCircularPitchEdit e: w.WriteString("deviceId", e.DeviceId); RotaryLinearJson.Quantity(w, "circularPitch", e.CircularPitch); break;
            case SetRackPitchEdit e: w.WriteString("deviceId", e.DeviceId); RotaryLinearJson.Quantity(w, "rackPitch", e.RackPitch); break;
            case SetRackPinionCenterEdit e: w.WriteString("deviceId", e.DeviceId); RotaryLinearJson.LengthVector(w, "centerMm", e.CenterMm); break;
            case SetRackContactNormalEdit e: w.WriteString("deviceId", e.DeviceId); Vector(w, "contactNormal", e.ContactNormal); break;
            case SetRackGuidePlacementEdit e: w.WriteString("deviceId", e.DeviceId); PiFrame(w, "guideFrameMm", e.GuideFrameMm); RotaryLinearJson.Quantity(w, "longitudinalOffset", e.LongitudinalOffset); break;
            case SetRackReferenceEdit e: w.WriteString("deviceId", e.DeviceId); RotaryLinearJson.Quantity(w, "pinionReferenceTurns", e.PinionReferenceTurns); RotaryLinearJson.Quantity(w, "rackReferencePosition", e.RackReferencePosition); break;
            case SetRackActiveMaterialIntervalEdit e: w.WriteString("deviceId", e.DeviceId); RotaryLinearJson.Interval(w, "interval", e.Interval); break;
            case SetRackGuideIntervalEdit e: w.WriteString("deviceId", e.DeviceId); RotaryLinearJson.Interval(w, "interval", e.Interval); break;
            case SetRackGroundingEdit e:
                w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("pinionCenterFixed", e.PinionCenterFixed); w.WriteBoolean("pinionAxisFixed", e.PinionAxisFixed);
                w.WriteBoolean("guidePresent", e.GuidePresent); w.WriteBoolean("rackRotationFixed", e.RackRotationFixed); w.WriteBoolean("transverseMotionFixed", e.TransverseMotionFixed); break;
            case SetRackTransmissionEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); break;
            case SetRackPinionBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("shaftId", e.ShaftId); w.WriteString("portId", e.PortId); break;
            case SetRackTerminalEdit e: w.WriteString("outputKey", e.OutputKey); Integer(w, "sign", e.Sign); RotaryLinearJson.Quantity(w, "datum", e.Datum); break;
            case SetRackRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); w.WritePropertyName("requirement"); RotaryLinearJson.Requirement(w, e.Requirement); break;
            default: throw new ArtifactFormatException("Unsupported typed rack operation.");
        }
        w.WriteEndObject();
    }
    private static RackPinionEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyRackSourceEditsEdit) => new ApplyRackSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetRackSourceLengthMappingEdit) => new SetRackSourceLengthMappingEdit(RotaryLinearJson.Mapping(p.GetProperty("mapping"))),
        nameof(SetRackPinionTeethEdit) => new SetRackPinionTeethEdit(S(p, "deviceId"), I(p, "toothCount")),
        nameof(SetRackPinionCircularPitchEdit) => new SetRackPinionCircularPitchEdit(S(p, "deviceId"), RotaryLinearJson.Quantity(p.GetProperty("circularPitch"))),
        nameof(SetRackPitchEdit) => new SetRackPitchEdit(S(p, "deviceId"), RotaryLinearJson.Quantity(p.GetProperty("rackPitch"))),
        nameof(SetRackPinionCenterEdit) => new SetRackPinionCenterEdit(S(p, "deviceId"), RotaryLinearJson.LengthVector(p.GetProperty("centerMm"))),
        nameof(SetRackContactNormalEdit) => new SetRackContactNormalEdit(S(p, "deviceId"), Vector(p.GetProperty("contactNormal"))),
        nameof(SetRackGuidePlacementEdit) => new SetRackGuidePlacementEdit(S(p, "deviceId"), PiFrame(p.GetProperty("guideFrameMm")), RotaryLinearJson.Quantity(p.GetProperty("longitudinalOffset"))),
        nameof(SetRackReferenceEdit) => new SetRackReferenceEdit(S(p, "deviceId"), RotaryLinearJson.Quantity(p.GetProperty("pinionReferenceTurns")), RotaryLinearJson.Quantity(p.GetProperty("rackReferencePosition"))),
        nameof(SetRackActiveMaterialIntervalEdit) => new SetRackActiveMaterialIntervalEdit(S(p, "deviceId"), RotaryLinearJson.Interval(p.GetProperty("interval"))),
        nameof(SetRackGuideIntervalEdit) => new SetRackGuideIntervalEdit(S(p, "deviceId"), RotaryLinearJson.Interval(p.GetProperty("interval"))),
        nameof(SetRackGroundingEdit) => new SetRackGroundingEdit(S(p, "deviceId"), p.GetProperty("pinionCenterFixed").GetBoolean(), p.GetProperty("pinionAxisFixed").GetBoolean(), p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("rackRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean()),
        nameof(SetRackTransmissionEdit) => new SetRackTransmissionEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetRackPinionBindingEdit) => new SetRackPinionBindingEdit(S(p, "deviceId"), S(p, "shaftId"), MechanicalAuthoringJson.NullableString(p, "portId")),
        nameof(SetRackTerminalEdit) => new SetRackTerminalEdit(S(p, "outputKey"), I(p, "sign"), RotaryLinearJson.Quantity(p.GetProperty("datum"))),
        nameof(SetRackRequirementsEdit) => new SetRackRequirementsEdit(S(p, "outputKey"), RotaryLinearJson.Requirement(p.GetProperty("requirement"))),
        _ => throw new ArtifactFormatException("Unsupported typed rack edit operation.")
    };
    private static void EditResult(Utf8JsonWriter w, RackPinionEditResult r)
    {
        Start(w, "gear-invest.rack-pinion-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId);
        w.WriteString("baseDraftId", r.BaseDraftId); w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
