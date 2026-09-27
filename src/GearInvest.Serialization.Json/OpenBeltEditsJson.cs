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

/// <summary>Actual ordered replay, including source edits and explicit belt selection. Saved derived motion and length verdicts are never authorities.</summary>
public sealed class OpenBeltEditSession
{
    internal OpenBeltEditSession(OpenBeltDraft initial, IEnumerable<OpenBeltEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(OpenBeltProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > OpenBeltProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > OpenBeltProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<OpenBeltEditResult>();
        foreach (var batch in Batches) { var result = OpenBeltEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current;
        InitialAnalysis = OpenBeltAnalyzer.Analyze(initial); Analysis = OpenBeltAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<OutputComparisonRequest>()).Take(OpenBeltProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > OpenBeltProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => OpenBeltOutputComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("open-belt-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()),
            current.DraftId, Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public OpenBeltDraft InitialDraft { get; } public OpenBeltDraft CurrentDraft { get; }
    public ReadOnlyCollection<OpenBeltEditBatch> Batches { get; } public ReadOnlyCollection<OpenBeltEditResult> Results { get; }
    public OpenBeltAnalysis InitialAnalysis { get; } public OpenBeltAnalysis Analysis { get; }
    public ReadOnlyCollection<OutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<OpenBeltOutputEquivalenceResult> Comparisons { get; }
}

public static partial class OpenBeltJson
{
    public static byte[] WriteBatch(OpenBeltEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static OpenBeltEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var b = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(b)), "Noncanonical open belt batch."); return b; });
    public static byte[] WriteEditResult(OpenBeltEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static OpenBeltEditSession CreateSession(OpenBeltDraft initial, IEnumerable<OpenBeltEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(OpenBeltEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static OpenBeltEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var s = new OpenBeltEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", OpenBeltProfile.MaxBatches).Select(Batch),
            Items(p, "comparisonRequests", OpenBeltProfile.MaxComparisons).Select(MechanicalAuthoringJson.ComparisonRequest));
        Require(s.SessionId == S(p, "sessionId"), "Open belt session request/current identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(s)), "Fresh atomic reapply, open belt reanalysis or comparison differs from stored session."); return s;
    });
    private static void Session(Utf8JsonWriter w, OpenBeltEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("numericSemantics", OpenBeltProfile.NumericSemantics); w.WriteString("sessionId", s.SessionId);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, MechanicalAuthoringJson.ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, OpenBeltEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static OpenBeltEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new OpenBeltEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Open belt batch identity mismatch."); return b;
    }
    private static void Operation(Utf8JsonWriter w, OpenBeltEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyOpenBeltSourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetOpenBeltSourceLengthMappingEdit e: w.WritePropertyName("mapping"); R.Mapping(w, e.Mapping); break;
            case SetOpenBeltInputPitchRadiusEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "radius", e.Radius); break;
            case SetOpenBeltOutputPitchRadiusEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "radius", e.Radius); break;
            case SetOpenBeltInputPulleyCenterEdit e: w.WriteString("deviceId", e.DeviceId); R.LengthVector(w, "centerMm", e.CenterMm); break;
            case SetOpenBeltInputBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("shaftId", e.ShaftId); w.WriteString("portId", e.PortId); break;
            case SetOpenBeltOutputPulleyStationEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "station", e.Station); break;
            case SetOpenBeltOutputShaftFrameEdit e: w.WriteString("shaftId", e.ShaftId); LengthFrame(w, "frameMm", e.Frame); break;
            case MoveOpenBeltOutputShaftGroupEdit e: w.WriteString("shaftId", e.ShaftId); LengthFrame(w, "worldRigidTransformMm", e.WorldRigidTransform); break;
            case SetOpenBeltOutputTerminalEdit e: w.WriteString("outputKey", e.OutputKey); LengthFrame(w, "frameMm", e.Frame); break;
            case SetOpenBeltRouteEdit e: w.WriteString("deviceId", e.DeviceId); Vector(w, "normal", e.Normal); w.WriteString("routingKind", e.RoutingKind); break;
            case SetOpenBeltReferenceEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "inputTurns", e.InputTurns); R.Quantity(w, "outputTurns", e.OutputTurns); break;
            case SetOpenBeltTransmissionEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); break;
            case SetOpenBeltLengthSpecificationEdit e: w.WriteString("deviceId", e.DeviceId); w.WritePropertyName("specification"); LengthSpecification(w, e.Specification); break;
            case SetOpenBeltRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); OptionalFraction(w, "transfer", e.Transfer); OptionalFraction(w, "phase", e.Phase); break;
            case SetOpenBeltRequiredValidationEdit e: MechanicalAuthoringJson.Strings(w, "requiredDomains", e.RequiredDomains); break;
            case SetOpenBeltGroundingEdit e:
                w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("inputCenterFixed", e.InputCenterFixed); w.WriteBoolean("inputAxisFixed", e.InputAxisFixed);
                w.WriteBoolean("outputCenterFixed", e.OutputCenterFixed); w.WriteBoolean("outputAxisFixed", e.OutputAxisFixed); break;
            default: throw new ArtifactFormatException("Unsupported typed open belt operation.");
        }
        w.WriteEndObject();
    }
    private static OpenBeltEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyOpenBeltSourceEditsEdit) => new ApplyOpenBeltSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetOpenBeltSourceLengthMappingEdit) => new SetOpenBeltSourceLengthMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(SetOpenBeltInputPitchRadiusEdit) => new SetOpenBeltInputPitchRadiusEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("radius"))),
        nameof(SetOpenBeltOutputPitchRadiusEdit) => new SetOpenBeltOutputPitchRadiusEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("radius"))),
        nameof(SetOpenBeltInputPulleyCenterEdit) => new SetOpenBeltInputPulleyCenterEdit(S(p, "deviceId"), R.LengthVector(p.GetProperty("centerMm"))),
        nameof(SetOpenBeltInputBindingEdit) => new SetOpenBeltInputBindingEdit(S(p, "deviceId"), S(p, "shaftId"), MechanicalAuthoringJson.NullableString(p, "portId")),
        nameof(SetOpenBeltOutputPulleyStationEdit) => new SetOpenBeltOutputPulleyStationEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("station"))),
        nameof(SetOpenBeltOutputShaftFrameEdit) => new SetOpenBeltOutputShaftFrameEdit(S(p, "shaftId"), LengthFrame(p.GetProperty("frameMm"))),
        nameof(MoveOpenBeltOutputShaftGroupEdit) => new MoveOpenBeltOutputShaftGroupEdit(S(p, "shaftId"), LengthFrame(p.GetProperty("worldRigidTransformMm"))),
        nameof(SetOpenBeltOutputTerminalEdit) => new SetOpenBeltOutputTerminalEdit(S(p, "outputKey"), LengthFrame(p.GetProperty("frameMm"))),
        nameof(SetOpenBeltRouteEdit) => new SetOpenBeltRouteEdit(S(p, "deviceId"), Vector(p.GetProperty("normal")), S(p, "routingKind")),
        nameof(SetOpenBeltReferenceEdit) => new SetOpenBeltReferenceEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("inputTurns")), R.Quantity(p.GetProperty("outputTurns"))),
        nameof(SetOpenBeltTransmissionEdit) => new SetOpenBeltTransmissionEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetOpenBeltLengthSpecificationEdit) => new SetOpenBeltLengthSpecificationEdit(S(p, "deviceId"), LengthSpecification(p.GetProperty("specification"))),
        nameof(SetOpenBeltRequirementsEdit) => new SetOpenBeltRequirementsEdit(S(p, "outputKey"), OptionalFraction(p, "transfer"), OptionalFraction(p, "phase")),
        nameof(SetOpenBeltRequiredValidationEdit) => new SetOpenBeltRequiredValidationEdit(Items(p, "requiredDomains", 24).Select(x => x.GetString()!)),
        nameof(SetOpenBeltGroundingEdit) => new SetOpenBeltGroundingEdit(S(p, "deviceId"), p.GetProperty("inputCenterFixed").GetBoolean(), p.GetProperty("inputAxisFixed").GetBoolean(), p.GetProperty("outputCenterFixed").GetBoolean(), p.GetProperty("outputAxisFixed").GetBoolean()),
        _ => throw new ArtifactFormatException("Unsupported typed open belt edit operation.")
    };
    private static void EditResult(Utf8JsonWriter w, OpenBeltEditResult r)
    {
        Start(w, "gear-invest.open-belt-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId);
        w.WriteString("baseDraftId", r.BaseDraftId); w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
