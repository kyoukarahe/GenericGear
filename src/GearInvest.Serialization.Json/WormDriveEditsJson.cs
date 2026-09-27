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

/// <summary>Actual ordered replay, including source edits and explicit wheel selection. Saved derived motion and helix verdicts are never authorities.</summary>
public sealed class WormDriveEditSession
{
    internal WormDriveEditSession(WormDriveDraft initial, IEnumerable<WormDriveEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons)
    {
        InitialDraft = initial ?? throw new ArgumentNullException(nameof(initial));
        Batches = (batches ?? throw new ArgumentNullException(nameof(batches))).Take(WormDriveProfile.MaxBatches + 1).ToList().AsReadOnly();
        if (Batches.Count > WormDriveProfile.MaxBatches || Batches.Any(b => b is null) || Batches.Sum(b => b.OperationCount) > WormDriveProfile.MaxTotalOperations)
            throw new ArgumentException("Session batch or total operation bound exceeded.");
        var current = initial; var results = new List<WormDriveEditResult>();
        foreach (var batch in Batches) { var result = WormDriveEditor.Apply(current, batch); results.Add(result); if (result.Draft is not null) current = result.Draft; }
        Results = results.AsReadOnly(); CurrentDraft = current;
        InitialAnalysis = WormDriveAnalyzer.Analyze(initial); Analysis = WormDriveAnalyzer.Analyze(current);
        ComparisonRequests = (comparisons ?? System.Array.Empty<OutputComparisonRequest>()).Take(WormDriveProfile.MaxComparisons + 1).ToList().AsReadOnly();
        if (ComparisonRequests.Count > WormDriveProfile.MaxComparisons || ComparisonRequests.Any(c => c is null)) throw new ArgumentException("Bounded explicit comparison requests required.");
        Comparisons = ComparisonRequests.Select(c => WormDriveComparer.Compare(InitialAnalysis, Analysis, c)).ToList().AsReadOnly();
        SessionId = Hash(Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("worm-drive-edit-session-v1", initial.DraftId,
            OrientedGoalKeys.Pack(Batches.Select(b => b.RequestId).ToArray()), OrientedGoalKeys.Pack(Results.Select(r => r.ResultId).ToArray()),
            current.DraftId, Analysis.AnalysisId, OrientedGoalKeys.Pack(ComparisonRequests.Select(c => c.RequestId).ToArray()))));
    }
    public string SessionId { get; } public WormDriveDraft InitialDraft { get; } public WormDriveDraft CurrentDraft { get; }
    public ReadOnlyCollection<WormDriveEditBatch> Batches { get; } public ReadOnlyCollection<WormDriveEditResult> Results { get; }
    public WormDriveAnalysis InitialAnalysis { get; } public WormDriveAnalysis Analysis { get; }
    public ReadOnlyCollection<OutputComparisonRequest> ComparisonRequests { get; }
    public ReadOnlyCollection<WormDriveOutputEquivalenceResult> Comparisons { get; }
}

public static partial class WormDriveJson
{
    public static byte[] WriteBatch(WormDriveEditBatch batch) => Guard(() => Encode(w => Batch(w, batch)));
    public static WormDriveEditBatch ReadBatch(byte[] bytes) => Guard(() =>
    { using var doc = Parse(bytes); var b = Batch(doc.RootElement); Require(bytes.SequenceEqual(WriteBatch(b)), "Noncanonical worm drive batch."); return b; });
    public static byte[] WriteEditResult(WormDriveEditResult result) => Guard(() => Encode(w => EditResult(w, result)));
    public static WormDriveEditSession CreateSession(WormDriveDraft initial, IEnumerable<WormDriveEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null) => new(initial, batches, comparisons);
    public static byte[] WriteSession(WormDriveEditSession session) => Guard(() => Encode(w => Session(w, session)));
    public static WormDriveEditSession ReadSession(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, SessionFormat);
        var s = new WormDriveEditSession(Draft(p.GetProperty("initialDraft")), Items(p, "batches", WormDriveProfile.MaxBatches).Select(Batch),
            Items(p, "comparisonRequests", WormDriveProfile.MaxComparisons).Select(MechanicalAuthoringJson.ComparisonRequest));
        Require(s.SessionId == S(p, "sessionId"), "Worm drive session request/current identity mismatch.");
        Require(bytes.SequenceEqual(WriteSession(s)), "Fresh atomic reapply, worm drive reanalysis or comparison differs from stored session."); return s;
    });
    private static void Session(Utf8JsonWriter w, WormDriveEditSession s)
    {
        Start(w, SessionFormat); w.WriteString("geometrySemantics", WormDriveProfile.GeometrySemantics); w.WriteString("poseSemantics", WormDriveProfile.PoseSemantics); w.WriteString("sessionId", s.SessionId);
        w.WritePropertyName("initialDraft"); Draft(w, s.InitialDraft); Array(w, "batches", s.Batches, Batch); Array(w, "results", s.Results, EditResult);
        w.WritePropertyName("currentDraft"); Draft(w, s.CurrentDraft); w.WritePropertyName("analysis"); Analysis(w, s.Analysis);
        Array(w, "comparisonRequests", s.ComparisonRequests, MechanicalAuthoringJson.ComparisonRequest); Array(w, "comparisons", s.Comparisons, Comparison); w.WriteEndObject();
    }
    private static void Batch(Utf8JsonWriter w, WormDriveEditBatch b)
    {
        Start(w, BatchFormat); w.WriteString("requestId", b.RequestId); MechanicalAuthoringJson.Long(w, "expectedRevision", b.ExpectedRevision);
        w.WriteString("expectedDefinitionId", b.ExpectedDefinitionId); Array(w, "operations", b.Operations, Operation); w.WriteEndObject();
    }
    private static WormDriveEditBatch Batch(JsonElement p)
    {
        Header(p, BatchFormat); var b = new WormDriveEditBatch(MechanicalAuthoringJson.Long(p, "expectedRevision"), S(p, "expectedDefinitionId"), Items(p, "operations", 128).Select(Operation));
        Require(b.RequestId == S(p, "requestId"), "Worm drive batch identity mismatch."); return b;
    }
    private static void Operation(Utf8JsonWriter w, WormDriveEditOperation operation)
    {
        w.WriteStartObject(); w.WriteString("kind", operation.Kind);
        switch (operation)
        {
            case ApplyWormDriveSourceEditsEdit e: w.WritePropertyName("sourceBatch"); MechanicalAuthoringJson.Batch(w, e.Batch); break;
            case SetWormDriveSourceLengthMappingEdit e: w.WritePropertyName("mapping"); R.Mapping(w, e.Mapping); break;
            case SetWormDriveStartsEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "starts", e.Starts); break;
            case SetWormDriveAxialModuleEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "module", e.Module); break;
            case SetWormDrivePitchRadiusEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "radius", e.Radius); break;
            case SetWormDriveHandednessEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "handedness", e.Handedness); break;
            case SetWormDriveWormSpecificationEdit e: w.WriteString("deviceId", e.DeviceId); w.WritePropertyName("specification"); Worm(w, e.Specification); break;
            case SetWormDriveWheelSpecificationEdit e: w.WriteString("deviceId", e.DeviceId); w.WritePropertyName("specification"); Wheel(w, e.Specification); break;
            case SetWormDriveWheelToothCountEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "toothCount", e.ToothCount); break;
            case SetWormDriveWheelModuleEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "module", e.Module); break;
            case SetWormDriveWheelHandednessEdit e: w.WriteString("deviceId", e.DeviceId); Integer(w, "handedness", e.Handedness); break;
            case SetWormDriveWheelTraceSlopeEdit e: w.WriteString("deviceId", e.DeviceId); Fraction(w, "traceSlope", e.TraceSlope); break;
            case SetWormDriveInputPitchStationEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "station", e.Station); break;
            case SetWormDriveInputBindingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("shaftId", e.ShaftId); w.WriteString("portId", e.PortId); break;
            case SetWormDriveOutputPitchStationEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "station", e.Station); break;
            case SetWormDrivePhysicalHelixAxisEdit e: w.WriteString("deviceId", e.DeviceId); Vector(w, "axis", e.Axis); break;
            case SetWormDriveContactSideEdit e: w.WriteString("deviceId", e.DeviceId); Vector(w, "contactSide", e.ContactSide); break;
            case SetWormDriveOutputShaftFrameEdit e: w.WriteString("shaftId", e.ShaftId); LengthFrame(w, "frameMm", e.Frame); break;
            case MoveWormDriveOutputShaftGroupEdit e: w.WriteString("shaftId", e.ShaftId); LengthFrame(w, "worldRigidTransformMm", e.WorldRigidTransform); break;
            case SetWormDriveOutputTerminalEdit e: w.WriteString("outputKey", e.OutputKey); LengthFrame(w, "frameMm", e.Frame); OptionalFraction(w, "phaseOffset", e.PhaseOffset); break;
            case SetWormDriveReferenceEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "inputTurns", e.InputTurns); R.Quantity(w, "outputTurns", e.OutputTurns); break;
            case SetWormDriveMountingPhaseEdit e: w.WriteString("deviceId", e.DeviceId); R.Quantity(w, "inputTurns", e.InputTurns); R.Quantity(w, "outputTurns", e.OutputTurns); break;
            case SetWormDriveTransmissionEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("present", e.Present); break;
            case SetWormDriveRequirementsEdit e: w.WriteString("outputKey", e.OutputKey); OptionalFraction(w, "transfer", e.Transfer); OptionalFraction(w, "phase", e.Phase); break;
            case SetWormDriveRequiredValidationEdit e: MechanicalAuthoringJson.Strings(w, "requiredDomains", e.RequiredDomains); break;
            case SetWormDriveGroundingEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteBoolean("inputCenterFixed", e.InputCenterFixed); w.WriteBoolean("inputAxisFixed", e.InputAxisFixed); w.WriteBoolean("outputCenterFixed", e.OutputCenterFixed); w.WriteBoolean("outputAxisFixed", e.OutputAxisFixed); break;
            case SetWormDriveProfileEdit e: w.WriteString("deviceId", e.DeviceId); w.WriteString("profile", e.Profile); break;
            default: throw new ArtifactFormatException("Unsupported typed worm drive operation.");
        }
        w.WriteEndObject();
    }
    private static WormDriveEditOperation Operation(JsonElement p) => S(p, "kind") switch
    {
        nameof(ApplyWormDriveSourceEditsEdit) => new ApplyWormDriveSourceEditsEdit(MechanicalAuthoringJson.Batch(p.GetProperty("sourceBatch"))),
        nameof(SetWormDriveSourceLengthMappingEdit) => new SetWormDriveSourceLengthMappingEdit(R.Mapping(p.GetProperty("mapping"))),
        nameof(SetWormDriveStartsEdit) => new SetWormDriveStartsEdit(S(p, "deviceId"), I(p, "starts")),
        nameof(SetWormDriveAxialModuleEdit) => new SetWormDriveAxialModuleEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("module"))),
        nameof(SetWormDrivePitchRadiusEdit) => new SetWormDrivePitchRadiusEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("radius"))),
        nameof(SetWormDriveHandednessEdit) => new SetWormDriveHandednessEdit(S(p, "deviceId"), I(p, "handedness")),
        nameof(SetWormDriveWormSpecificationEdit) => new SetWormDriveWormSpecificationEdit(S(p, "deviceId"), Worm(p.GetProperty("specification"))),
        nameof(SetWormDriveWheelSpecificationEdit) => new SetWormDriveWheelSpecificationEdit(S(p, "deviceId"), Wheel(p.GetProperty("specification"))),
        nameof(SetWormDriveWheelToothCountEdit) => new SetWormDriveWheelToothCountEdit(S(p, "deviceId"), I(p, "toothCount")),
        nameof(SetWormDriveWheelModuleEdit) => new SetWormDriveWheelModuleEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("module"))),
        nameof(SetWormDriveWheelHandednessEdit) => new SetWormDriveWheelHandednessEdit(S(p, "deviceId"), I(p, "handedness")),
        nameof(SetWormDriveWheelTraceSlopeEdit) => new SetWormDriveWheelTraceSlopeEdit(S(p, "deviceId"), F(p.GetProperty("traceSlope"))),
        nameof(SetWormDriveInputPitchStationEdit) => new SetWormDriveInputPitchStationEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("station"))),
        nameof(SetWormDriveInputBindingEdit) => new SetWormDriveInputBindingEdit(S(p, "deviceId"), S(p, "shaftId"), MechanicalAuthoringJson.NullableString(p, "portId")),
        nameof(SetWormDriveOutputPitchStationEdit) => new SetWormDriveOutputPitchStationEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("station"))),
        nameof(SetWormDrivePhysicalHelixAxisEdit) => new SetWormDrivePhysicalHelixAxisEdit(S(p, "deviceId"), Vector(p.GetProperty("axis"))),
        nameof(SetWormDriveContactSideEdit) => new SetWormDriveContactSideEdit(S(p, "deviceId"), Vector(p.GetProperty("contactSide"))),
        nameof(SetWormDriveOutputShaftFrameEdit) => new SetWormDriveOutputShaftFrameEdit(S(p, "shaftId"), LengthFrame(p.GetProperty("frameMm"))),
        nameof(MoveWormDriveOutputShaftGroupEdit) => new MoveWormDriveOutputShaftGroupEdit(S(p, "shaftId"), LengthFrame(p.GetProperty("worldRigidTransformMm"))),
        nameof(SetWormDriveOutputTerminalEdit) => new SetWormDriveOutputTerminalEdit(S(p, "outputKey"), LengthFrame(p.GetProperty("frameMm")), OptionalFraction(p, "phaseOffset")),
        nameof(SetWormDriveReferenceEdit) => new SetWormDriveReferenceEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("inputTurns")), R.Quantity(p.GetProperty("outputTurns"))),
        nameof(SetWormDriveMountingPhaseEdit) => new SetWormDriveMountingPhaseEdit(S(p, "deviceId"), R.Quantity(p.GetProperty("inputTurns")), R.Quantity(p.GetProperty("outputTurns"))),
        nameof(SetWormDriveTransmissionEdit) => new SetWormDriveTransmissionEdit(S(p, "deviceId"), p.GetProperty("present").GetBoolean()),
        nameof(SetWormDriveRequirementsEdit) => new SetWormDriveRequirementsEdit(S(p, "outputKey"), OptionalFraction(p, "transfer"), OptionalFraction(p, "phase")),
        nameof(SetWormDriveRequiredValidationEdit) => new SetWormDriveRequiredValidationEdit(Items(p, "requiredDomains", 24).Select(x => x.GetString()!)),
        nameof(SetWormDriveGroundingEdit) => new SetWormDriveGroundingEdit(S(p, "deviceId"), p.GetProperty("inputCenterFixed").GetBoolean(), p.GetProperty("inputAxisFixed").GetBoolean(), p.GetProperty("outputCenterFixed").GetBoolean(), p.GetProperty("outputAxisFixed").GetBoolean()),
        nameof(SetWormDriveProfileEdit) => new SetWormDriveProfileEdit(S(p, "deviceId"), S(p, "profile")),
        _ => throw new ArtifactFormatException("Unsupported typed worm drive edit operation.")
    };
    private static void EditResult(Utf8JsonWriter w, WormDriveEditResult r)
    {
        Start(w, "gear-invest.worm-drive-edit-result"); w.WriteString("resultId", r.ResultId); w.WriteString("requestId", r.RequestId);
        w.WriteString("baseDraftId", r.BaseDraftId); w.WriteString("status", r.Status.ToString()); w.WriteString("draftId", r.Draft?.DraftId); w.WriteString("definitionId", r.Draft?.DefinitionId);
        if (r.FailingOperationIndex.HasValue) Integer(w, "failingOperationIndex", r.FailingOperationIndex.Value); else w.WriteNull("failingOperationIndex");
        w.WriteBoolean("isNetEmpty", r.IsNetEmpty); w.WriteBoolean("requiresReanalysis", r.RequiresReanalysis);
        Array(w, "changes", r.Changes, (x, c) => { x.WriteStartObject(); Integer(x, "operationIndex", c.OperationIndex); x.WriteString("operationKind", c.OperationKind); MechanicalAuthoringJson.Strings(x, "dependentMetrics", c.DependentMetrics); x.WriteEndObject(); });
        Array(w, "sourceResults", r.SourceResults, MechanicalAuthoringJson.EditResult); Array(w, "diagnostics", r.Diagnostics, MechanicalAuthoringJson.Diagnostic); w.WriteEndObject();
    }
}


