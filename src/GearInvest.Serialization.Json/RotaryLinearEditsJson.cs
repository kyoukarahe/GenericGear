using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Original draft plus actual ordered source/device transactions; analyses and comparisons are recomputed.</summary>
public sealed class RotaryLinearEditSession
{
    internal RotaryLinearEditSession(RotaryLinearDraft initial, IEnumerable<RotaryLinearEditBatch> batches, IEnumerable<LinearOutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(129).ToList().AsReadOnly();
        if (Batches.Count > 128 || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > 512) throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<RotaryLinearEditResult>();
        foreach (var batch in Batches) { var result = RotaryLinearEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current;
        InitialAnalysis = RotaryLinearAnalyzer.Analyze(initial); Analysis = RotaryLinearAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<LinearOutputComparisonRequest>()).Take(65).ToList().AsReadOnly();
        if (ComparisonRequests.Count > 64 || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => RotaryLinearOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("rotary-linear-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()),
            current.DraftId, Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public RotaryLinearDraft InitialDraft { get; } public RotaryLinearDraft CurrentDraft { get; }
    public ReadOnlyCollection<RotaryLinearEditBatch> Batches { get; } public ReadOnlyCollection<RotaryLinearEditResult> Results { get; }
    public RotaryLinearAnalysis InitialAnalysis { get; } public RotaryLinearAnalysis Analysis { get; }
    public ReadOnlyCollection<LinearOutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<LinearOutputEquivalenceResult> Comparisons { get; }
}

public static partial class RotaryLinearJson
{
    public static byte[] WriteBatch(RotaryLinearEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static RotaryLinearEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var b = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(b)), "Noncanonical mixed batch."); return b; });
    public static byte[] WriteEditResult(RotaryLinearEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static RotaryLinearEditSession CreateSession(RotaryLinearDraft initial, IEnumerable<RotaryLinearEditBatch> batches, IEnumerable<LinearOutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(RotaryLinearEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static RotaryLinearEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var s = new RotaryLinearEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", 128).Select(Batch), Items(p, "comparisonRequests", 64).Select(ComparisonRequest));
        Require(s.SessionId == S(p, "sessionId"), "Session request/current identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(s)), "Fresh atomic reapply, mixed reanalysis or comparison differs from stored session."); return s;
    });
    private static void Session(Utf8JsonWriter w, RotaryLinearEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("sessionId", s.SessionId); w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft);
        Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult); w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft);
        w.WritePropertyName("analysis"); Analysis(w, s.Analysis); Array(w, "comparisonRequests", s.ComparisonRequests, ComparisonRequest);
        Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    internal static void Batch(Utf8JsonWriter w, RotaryLinearEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    internal static RotaryLinearEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new RotaryLinearEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Mixed batch identity mismatch."); return b;
    }
    internal static void Operation(Utf8JsonWriter w, RotaryLinearEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyRotarySourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetSourceLengthMappingEdit e: w.WritePropertyName("mapping"); Mapping(w, e.Mapping); break;
            case SetLeadScrewLeadEdit e: w.WriteString("deviceId", e.DeviceId); Quantity(w, "lead", e.Lead); break;
            case SetLeadScrewHandednessEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "handedness", e.Handedness); break;
            case SetLeadScrewReferenceEdit e: w.WriteString("deviceId", e.DeviceId); Quantity(w, "screwReferenceTurns", e.ScrewReferenceTurns); Quantity(w, "nutReferencePosition", e.NutReferencePosition); LengthVector(w, "screwAxialDatumMm", e.ScrewAxialDatumMm); break;
            case SetLeadScrewGuideEdit e: w.WriteString("deviceId", e.DeviceId); LengthFrame(w, "guideFrameMm", e.GuideFrameMm); Interval(w, "guideInterval", e.GuideInterval); Interval(w, "engagementInterval", e.EngagementInterval); break;
            case SetLeadScrewGroundingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("screwAxiallyFixed", e.ScrewAxiallyFixed); w.WriteBoolean("guidePresent", e.GuidePresent); w.WriteBoolean("nutRotationFixed", e.NutRotationFixed); w.WriteBoolean("transverseMotionFixed", e.TransverseMotionFixed); break;
            case SetLeadScrewTransmissionEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); break;
            case SetLeadScrewBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("shaftId", e.ShaftId); w.WriteString("portId", e.PortId); Vector(w, "physicalAxis", e.PhysicalAxis); break;
            case SetLinearTerminalEdit e: w.WriteString("outputKey", e.OutputKey); Integer(w, "sign", e.Sign); Quantity(w, "datum", e.Datum); break;
            case SetLinearRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); w.WritePropertyName("requirement"); Requirement(w, e.Requirement); break;
            default: throw new ArtifactFormatException("Unsupported typed mixed operation.");
        }
        w.WriteEndObject();
    }
    internal static RotaryLinearEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyRotarySourceEditsEdit) => new ApplyRotarySourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetSourceLengthMappingEdit) => new SetSourceLengthMappingEdit(Mapping(p.GetProperty("mapping"))),
        nameof(SetLeadScrewLeadEdit) => new SetLeadScrewLeadEdit(S(p, "deviceId"), Quantity(p.GetProperty("lead"))),
        nameof(SetLeadScrewHandednessEdit) => new SetLeadScrewHandednessEdit(S(p, "deviceId"), I(p, "handedness")),
        nameof(SetLeadScrewReferenceEdit) => new SetLeadScrewReferenceEdit(S(p, "deviceId"), Quantity(p.GetProperty("screwReferenceTurns")), Quantity(p.GetProperty("nutReferencePosition")), LengthVector(p.GetProperty("screwAxialDatumMm"))),
        nameof(SetLeadScrewGuideEdit) => new SetLeadScrewGuideEdit(S(p, "deviceId"), LengthFrame(p.GetProperty("guideFrameMm")), Interval(p.GetProperty("guideInterval")), Interval(p.GetProperty("engagementInterval"))),
        nameof(SetLeadScrewGroundingEdit) => new SetLeadScrewGroundingEdit(S(p, "deviceId"), p.GetProperty("screwAxiallyFixed").GetBoolean(), p.GetProperty("guidePresent").GetBoolean(), p.GetProperty("nutRotationFixed").GetBoolean(), p.GetProperty("transverseMotionFixed").GetBoolean()),
        nameof(SetLeadScrewTransmissionEdit) => new SetLeadScrewTransmissionEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetLeadScrewBindingEdit) => new SetLeadScrewBindingEdit(S(p, "deviceId"), S(p, "shaftId"), MechanicalAuthoringJson.NullableString(p, "portId"), Vector(p.GetProperty("physicalAxis"))),
        nameof(SetLinearTerminalEdit) => new SetLinearTerminalEdit(S(p, "outputKey"), I(p, "sign"), Quantity(p.GetProperty("datum"))),
        nameof(SetLinearRequirementsEdit) => new SetLinearRequirementsEdit(S(p, "outputKey"), Requirement(p.GetProperty("requirement"))),
        _ => throw new ArtifactFormatException("Unsupported typed mixed edit operation.")
    };
    internal static void EditResult(Utf8JsonWriter w, RotaryLinearEditResult r)
    {
        Start(w, "gear-invest.rotary-linear-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId);
        w.WriteString("baseDraftId", r.BaseDraftId); w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
