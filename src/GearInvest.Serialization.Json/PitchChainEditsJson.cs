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

/// <summary>Actual ordered replay, including source edits and explicit chain selection. Saved derived motion and length verdicts are never authorities.</summary>
public sealed class PitchChainEditSession
{
    internal PitchChainEditSession(PitchChainDraft initial, IEnumerable<PitchChainEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(PitchChainProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > PitchChainProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > PitchChainProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<PitchChainEditResult>();
        foreach (var batch in Batches) { var result = PitchChainEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current;
        InitialAnalysis = PitchChainAnalyzer.Analyze(initial); Analysis = PitchChainAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<OutputComparisonRequest>()).Take(PitchChainProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > PitchChainProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => PitchChainEquivalence.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("pitch-chain-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()),
            current.DraftId, Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public PitchChainDraft InitialDraft { get; } public PitchChainDraft CurrentDraft { get; }
    public ReadOnlyCollection<PitchChainEditBatch> Batches { get; } public ReadOnlyCollection<PitchChainEditResult> Results { get; }
    public PitchChainAnalysis InitialAnalysis { get; } public PitchChainAnalysis Analysis { get; }
    public ReadOnlyCollection<OutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<PitchChainOutputEquivalenceResult> Comparisons { get; }
}

public static partial class PitchChainJson
{
    public static byte[] WriteBatch(PitchChainEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static PitchChainEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var b = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(b)), "Noncanonical pitch chain batch."); return b; });
    public static byte[] WriteEditResult(PitchChainEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static PitchChainEditSession CreateSession(PitchChainDraft initial, IEnumerable<PitchChainEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(PitchChainEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static PitchChainEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var s = new PitchChainEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", PitchChainProfile.MaxBatches).Select(Batch),
            Items(p, "comparisonRequests", PitchChainProfile.MaxComparisons).Select(MechanicalAuthoringJson.ComparisonRequest));
        Require(s.SessionId == S(p, "sessionId"), "Open chain session request/current identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(s)), "Fresh atomic reapply, pitch chain reanalysis or comparison differs from stored session."); return s;
    });
    private static void Session(Utf8JsonWriter w, PitchChainEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("geometrySemantics", PitchChainProfile.GeometrySemantics); w.WriteString("poseSemantics", PitchChainProfile.PoseSemantics); w.WriteString("sessionId", s.SessionId);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, MechanicalAuthoringJson.ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, PitchChainEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static PitchChainEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new PitchChainEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Open chain batch identity mismatch."); return b;
    }
    private static void Operation(Utf8JsonWriter w, PitchChainEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyPitchChainSourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetPitchChainSourceLengthMappingEdit e: w.WritePropertyName("mapping"); R.Mapping(w, e.Mapping); break;
            case SetPitchChainInputToothCountEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "toothCount", e.ToothCount); break;
            case SetPitchChainOutputToothCountEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "toothCount", e.ToothCount); break;
            case SetPitchChainInputPitchEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "pitch", e.Pitch); break;
            case SetPitchChainOutputPitchEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "pitch", e.Pitch); break;
            case SetPitchChainInputSprocketStationEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "station", e.Station); break;
            case SetPitchChainInputBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("shaftId", e.ShaftId); w.WriteString("portId", e.PortId); break;
            case SetPitchChainOutputSprocketStationEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "station", e.Station); break;
            case SetPitchChainOutputShaftFrameEdit e: w.WriteString("shaftId", e.ShaftId); LengthFrame(w, "frameMm", e.Frame); break;
            case MovePitchChainOutputShaftGroupEdit e: w.WriteString("shaftId", e.ShaftId); LengthFrame(w, "worldRigidTransformMm", e.WorldRigidTransform); break;
            case SetPitchChainOutputTerminalEdit e: w.WriteString("outputKey", e.OutputKey); LengthFrame(w, "frameMm", e.Frame); break;
            case SetPitchChainRouteEdit e: w.WriteString("deviceId", e.DeviceId); Vector(w, "normal", e.Normal); w.WriteString("routingKind", e.RoutingKind); break;
            case SetPitchChainReferenceEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "inputTurns", e.InputTurns); R.Quantity(w, "outputTurns", e.OutputTurns); break;
            case SetPitchChainMountingPhaseEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "inputTurns", e.InputTurns); R.Quantity(w, "outputTurns", e.OutputTurns); break;
            case SetPitchChainToothRegistrationEdit e: w.WriteString("deviceId", e.DeviceId); ExactInteger(w, "toothRegistration", e.ToothRegistration, false); break;
            case SetPitchChainTransmissionEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); break;
            case SetPitchChainSpecificationEdit e: w.WriteString("deviceId", e.DeviceId); w.WritePropertyName("specification"); Specification(w, e.Specification); break;
            case SetPitchChainRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); OptionalFraction(w, "transfer", e.Transfer); OptionalFraction(w, "phase", e.Phase); break;
            case SetPitchChainRequiredValidationEdit e: MechanicalAuthoringJson.Strings(w, "requiredDomains", e.RequiredDomains); break;
            case SetPitchChainGroundingEdit e:
                w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("inputCenterFixed", e.InputCenterFixed); w.WriteBoolean("inputAxisFixed", e.InputAxisFixed);
                w.WriteBoolean("outputCenterFixed", e.OutputCenterFixed); w.WriteBoolean("outputAxisFixed", e.OutputAxisFixed); break;
            default: throw new ArtifactFormatException("Unsupported typed pitch chain operation.");
        }
        w.WriteEndObject();
    }
    private static PitchChainEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyPitchChainSourceEditsEdit) => new ApplyPitchChainSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetPitchChainSourceLengthMappingEdit) => new SetPitchChainSourceLengthMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(SetPitchChainInputToothCountEdit) => new SetPitchChainInputToothCountEdit(S(p, "deviceId"), I(p, "toothCount")),
        nameof(SetPitchChainOutputToothCountEdit) => new SetPitchChainOutputToothCountEdit(S(p, "deviceId"), I(p, "toothCount")),
        nameof(SetPitchChainInputPitchEdit) => new SetPitchChainInputPitchEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("pitch"))),
        nameof(SetPitchChainOutputPitchEdit) => new SetPitchChainOutputPitchEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("pitch"))),
        nameof(SetPitchChainInputSprocketStationEdit) => new SetPitchChainInputSprocketStationEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("station"))),
        nameof(SetPitchChainInputBindingEdit) => new SetPitchChainInputBindingEdit(S(p, "deviceId"), S(p, "shaftId"), MechanicalAuthoringJson.NullableString(p, "portId")),
        nameof(SetPitchChainOutputSprocketStationEdit) => new SetPitchChainOutputSprocketStationEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("station"))),
        nameof(SetPitchChainOutputShaftFrameEdit) => new SetPitchChainOutputShaftFrameEdit(S(p, "shaftId"), LengthFrame(p.GetProperty("frameMm"))),
        nameof(MovePitchChainOutputShaftGroupEdit) => new MovePitchChainOutputShaftGroupEdit(S(p, "shaftId"), LengthFrame(p.GetProperty("worldRigidTransformMm"))),
        nameof(SetPitchChainOutputTerminalEdit) => new SetPitchChainOutputTerminalEdit(S(p, "outputKey"), LengthFrame(p.GetProperty("frameMm"))),
        nameof(SetPitchChainRouteEdit) => new SetPitchChainRouteEdit(S(p, "deviceId"), Vector(p.GetProperty("normal")), S(p, "routingKind")),
        nameof(SetPitchChainReferenceEdit) => new SetPitchChainReferenceEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("inputTurns")), R.Quantity(p.GetProperty("outputTurns"))),
        nameof(SetPitchChainMountingPhaseEdit) => new SetPitchChainMountingPhaseEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("inputTurns")), R.Quantity(p.GetProperty("outputTurns"))),
        nameof(SetPitchChainToothRegistrationEdit) => new SetPitchChainToothRegistrationEdit(S(p, "deviceId"), ExactInteger(p, "toothRegistration", false)),
        nameof(SetPitchChainTransmissionEdit) => new SetPitchChainTransmissionEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetPitchChainSpecificationEdit) => new SetPitchChainSpecificationEdit(S(p, "deviceId"), Specification(p.GetProperty("specification"))),
        nameof(SetPitchChainRequirementsEdit) => new SetPitchChainRequirementsEdit(S(p, "outputKey"), OptionalFraction(p, "transfer"), OptionalFraction(p, "phase")),
        nameof(SetPitchChainRequiredValidationEdit) => new SetPitchChainRequiredValidationEdit(Items(p, "requiredDomains", 24).Select(x => x.GetString()!)),
        nameof(SetPitchChainGroundingEdit) => new SetPitchChainGroundingEdit(S(p, "deviceId"), p.GetProperty("inputCenterFixed").GetBoolean(), p.GetProperty("inputAxisFixed").GetBoolean(), p.GetProperty("outputCenterFixed").GetBoolean(), p.GetProperty("outputAxisFixed").GetBoolean()),
        _ => throw new ArtifactFormatException("Unsupported typed pitch chain edit operation.")
    };
    private static void EditResult(Utf8JsonWriter w, PitchChainEditResult r)
    {
        Start(w, "gear-invest.pitch-chain-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId);
        w.WriteString("baseDraftId", r.BaseDraftId); w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}
