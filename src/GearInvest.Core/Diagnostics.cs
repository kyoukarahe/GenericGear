using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public static class DiagnosticCodes
{
    public const string SemanticDuplicateNodeId = "SEMANTIC_DUPLICATE_NODE_ID";
    public const string SemanticDuplicateRequirementId = "SEMANTIC_DUPLICATE_REQUIREMENT_ID";
    public const string SemanticUnknownNodeReference = "SEMANTIC_UNKNOWN_NODE_REFERENCE";
    public const string SemanticSelfReference = "SEMANTIC_SELF_REFERENCE";
    public const string SemanticNonPositivePeriod = "SEMANTIC_NON_POSITIVE_PERIOD";
    public const string SemanticResourceLimitExceeded = "SEMANTIC_RESOURCE_LIMIT_EXCEEDED";
    public const string SemanticTransferContradiction = "SEMANTIC_TRANSFER_CONTRADICTION";
    public const string SemanticInconsistentCycle = "SEMANTIC_INCONSISTENT_CYCLE";
    public const string SemanticUnsupportedDomain = "SEMANTIC_UNSUPPORTED_DOMAIN";
    public const string SemanticUnsupportedPhase = "SEMANTIC_UNSUPPORTED_PHASE";
    public const string SemanticUnreachableNode = "SEMANTIC_UNREACHABLE_NODE";
    public const string SemanticCancelled = "SEMANTIC_CANCELLED";

    public const string DuplicateDofId = "KINEMATIC_DUPLICATE_DOF_ID";
    public const string DuplicateConstraintId = "KINEMATIC_DUPLICATE_CONSTRAINT_ID";
    public const string InvalidToothCount = "KINEMATIC_INVALID_TOOTH_COUNT";
    public const string UnknownRootDof = "KINEMATIC_UNKNOWN_ROOT_DOF";
    public const string RootNotPrescribed = "KINEMATIC_ROOT_NOT_PRESCRIBED";
    public const string UnknownDofReference = "KINEMATIC_UNKNOWN_DOF_REFERENCE";
    public const string Contradiction = "KINEMATIC_CONTRADICTION";
    public const string UnreachableDof = "KINEMATIC_UNREACHABLE_DOF";
    public const string StoredSolutionMismatch = "KINEMATIC_STORED_SOLUTION_MISMATCH";

    public const string SynthesisInvalidTarget = "SYNTHESIS_INVALID_TARGET";
    public const string SynthesisInvalidToothBounds = "SYNTHESIS_INVALID_TOOTH_BOUNDS";
    public const string SynthesisInvalidStageBounds = "SYNTHESIS_INVALID_STAGE_BOUNDS";
    public const string SynthesisInvalidCandidateLimit = "SYNTHESIS_INVALID_CANDIDATE_LIMIT";
    public const string SynthesisInvalidSearchBudget = "SYNTHESIS_INVALID_SEARCH_BUDGET";
    public const string SynthesisUnsupportedDeterminismProfile = "SYNTHESIS_UNSUPPORTED_DETERMINISM_PROFILE";
    public const string SynthesisInfeasible = "SYNTHESIS_INFEASIBLE";
    public const string SynthesisBudgetExhausted = "SYNTHESIS_BUDGET_EXHAUSTED";
    public const string SynthesisCancelled = "SYNTHESIS_CANCELLED";
    public const string SynthesisResultTruncated = "SYNTHESIS_RESULT_TRUNCATED";
    public const string SynthesisCandidateValidationFailed = "SYNTHESIS_CANDIDATE_VALIDATION_FAILED";
    public const string SynthesisTargetMismatch = "SYNTHESIS_TARGET_MISMATCH";

    public const string DuplicateAxisId = "SPATIAL_DUPLICATE_AXIS_ID";
    public const string DuplicateBodyId = "SPATIAL_DUPLICATE_BODY_ID";
    public const string DuplicateContactId = "SPATIAL_DUPLICATE_CONTACT_ID";
    public const string UnknownAxisReference = "SPATIAL_UNKNOWN_AXIS_REFERENCE";
    public const string UnknownBodyDofReference = "SPATIAL_UNKNOWN_DOF_REFERENCE";
    public const string UnknownBodyReference = "SPATIAL_UNKNOWN_BODY_REFERENCE";
    public const string UnknownConstraintReference = "SPATIAL_UNKNOWN_CONSTRAINT_REFERENCE";
    public const string ContactDofMismatch = "SPATIAL_CONTACT_DOF_MISMATCH";
    public const string ToothCountMismatch = "SPATIAL_TOOTH_COUNT_MISMATCH";
    public const string PitchRatioMismatch = "SPATIAL_PITCH_RATIO_MISMATCH";
    public const string CenterDistanceMismatch = "SPATIAL_CENTER_DISTANCE_MISMATCH";
    public const string ContactLayerMismatch = "SPATIAL_CONTACT_LAYER_MISMATCH";
    public const string ExternalMeshDirectionMismatch = "SPATIAL_EXTERNAL_MESH_DIRECTION_MISMATCH";
    public const string CompoundDofMismatch = "SPATIAL_COMPOUND_DOF_MISMATCH";
    public const string InvalidSpatialValue = "SPATIAL_INVALID_VALUE";
    public const string MissingConstraintContact = "SPATIAL_MISSING_CONSTRAINT_CONTACT";
    public const string DuplicateConstraintContact = "SPATIAL_DUPLICATE_CONSTRAINT_CONTACT";
    public const string DanglingBody = "SPATIAL_DANGLING_BODY";
    public const string PitchScaleMismatch = "SPATIAL_PITCH_SCALE_MISMATCH";
    public const string LayerLimitExceeded = "SPATIAL_LAYER_LIMIT_EXCEEDED";
    public const string CoincidentAxes = "SPATIAL_COINCIDENT_AXES";
    public const string IntermediateRealizationMismatch = "SPATIAL_INTERMEDIATE_REALIZATION_MISMATCH";
    public const string SameLayerCollision = "SPATIAL_SAME_LAYER_COLLISION";

    public const string LayoutInvalidPitchScale = "LAYOUT_INVALID_PITCH_SCALE";
    public const string LayoutInvalidLayerLimit = "LAYOUT_INVALID_LAYER_LIMIT";
    public const string LayoutInvalidReturnLimit = "LAYOUT_INVALID_RETURN_LIMIT";
    public const string LayoutInvalidSearchBudget = "LAYOUT_INVALID_SEARCH_BUDGET";
    public const string LayoutInvalidClearance = "LAYOUT_INVALID_CLEARANCE";
    public const string LayoutUnsupportedDeterminismProfile = "LAYOUT_UNSUPPORTED_DETERMINISM_PROFILE";
    public const string LayoutInvalidKinematicCandidate = "LAYOUT_INVALID_KINEMATIC_CANDIDATE";
    public const string LayoutInsufficientLayers = "LAYOUT_INSUFFICIENT_LAYERS";
    public const string LayoutInfeasible = "LAYOUT_INFEASIBLE";
    public const string LayoutBudgetExhausted = "LAYOUT_BUDGET_EXHAUSTED";
    public const string LayoutCancelled = "LAYOUT_CANCELLED";
    public const string LayoutResultTruncated = "LAYOUT_RESULT_TRUNCATED";
    public const string LayoutCandidateValidationFailed = "LAYOUT_CANDIDATE_VALIDATION_FAILED";

    public const string GenerationInvalidKinematicLimit = "GENERATION_INVALID_KINEMATIC_LIMIT";
    public const string GenerationInvalidLayoutLimit = "GENERATION_INVALID_LAYOUT_LIMIT";
    public const string GenerationInvalidMechanismLimit = "GENERATION_INVALID_MECHANISM_LIMIT";
    public const string GenerationUnsupportedDeterminismProfile = "GENERATION_UNSUPPORTED_DETERMINISM_PROFILE";
    public const string GenerationCandidateValidationFailed = "GENERATION_CANDIDATE_VALIDATION_FAILED";
    public const string GenerationTargetMismatch = "GENERATION_TARGET_MISMATCH";
    public const string GenerationCancelled = "GENERATION_CANCELLED";
    public const string GenerationInfeasible = "GENERATION_INFEASIBLE";
    public const string GenerationResultTruncated = "GENERATION_RESULT_TRUNCATED";
    public const string GenerationKinematicCandidatesSkipped = "GENERATION_KINEMATIC_CANDIDATES_SKIPPED";
    public const string GenerationProvenanceMismatch = "GENERATION_PROVENANCE_MISMATCH";
    public const string GenerationRequestBoundsMismatch = "GENERATION_REQUEST_BOUNDS_MISMATCH";
    public const string GenerationObjectIdMismatch = "GENERATION_OBJECT_ID_MISMATCH";
    public const string GenerationInvalidCompiledPlan = "GENERATION_INVALID_COMPILED_PLAN";

    public const string CompositionInvalidRequest = "COMPOSITION_INVALID_REQUEST";
    public const string CompositionUnsupportedProfile = "COMPOSITION_UNSUPPORTED_PROFILE";
    public const string CompositionInvalidPlan = "COMPOSITION_INVALID_PLAN";
    public const string CompositionUnsupportedTopology = "COMPOSITION_UNSUPPORTED_TOPOLOGY";
    public const string CompositionEdgeMappingMismatch = "COMPOSITION_EDGE_MAPPING_MISMATCH";
    public const string CompositionEdgeCandidateMismatch = "COMPOSITION_EDGE_CANDIDATE_MISMATCH";
    public const string CompositionPitchScaleMismatch = "COMPOSITION_PITCH_SCALE_MISMATCH";
    public const string CompositionPhaseUnsupported = "COMPOSITION_PHASE_UNSUPPORTED";
    public const string CompositionBudgetExhausted = "COMPOSITION_BUDGET_EXHAUSTED";
    public const string CompositionCancelled = "COMPOSITION_CANCELLED";
    public const string CompositionInfeasible = "COMPOSITION_INFEASIBLE";
    public const string CompositionResultTruncated = "COMPOSITION_RESULT_TRUNCATED";
    public const string CompositionCandidateValidationFailed = "COMPOSITION_CANDIDATE_VALIDATION_FAILED";
    public const string CompositionProvenanceMismatch = "COMPOSITION_PROVENANCE_MISMATCH";
    public const string CompositionKinematicIdMismatch = "COMPOSITION_KINEMATIC_ID_MISMATCH";
    public const string CompositionSpatialIdMismatch = "COMPOSITION_SPATIAL_ID_MISMATCH";
    public const string CompositionSemanticBindingMismatch = "COMPOSITION_SEMANTIC_BINDING_MISMATCH";
    public const string BranchedCompositionInvalidRequest = "BRANCHED_COMPOSITION_INVALID_REQUEST";
    public const string BranchedCompositionUnsupportedProfile = "BRANCHED_COMPOSITION_UNSUPPORTED_PROFILE";
    public const string BranchedCompositionInvalidPlan = "BRANCHED_COMPOSITION_INVALID_PLAN";
    public const string BranchedCompositionUnsupportedTopology = "BRANCHED_COMPOSITION_UNSUPPORTED_TOPOLOGY";
    public const string BranchedCompositionEdgeMappingMismatch = "BRANCHED_COMPOSITION_EDGE_MAPPING_MISMATCH";
    public const string BranchedCompositionEdgeCandidateMismatch = "BRANCHED_COMPOSITION_EDGE_CANDIDATE_MISMATCH";
    public const string BranchedCompositionPitchScaleMismatch = "BRANCHED_COMPOSITION_PITCH_SCALE_MISMATCH";
    public const string BranchedCompositionPhaseUnsupported = "BRANCHED_COMPOSITION_PHASE_UNSUPPORTED";
    public const string BranchedCompositionHintMismatch = "BRANCHED_COMPOSITION_HINT_MISMATCH";
    public const string BranchedCompositionBudgetExhausted = "BRANCHED_COMPOSITION_BUDGET_EXHAUSTED";
    public const string BranchedCompositionCancelled = "BRANCHED_COMPOSITION_CANCELLED";
    public const string BranchedCompositionInfeasible = "BRANCHED_COMPOSITION_INFEASIBLE";
    public const string BranchedCompositionResultTruncated = "BRANCHED_COMPOSITION_RESULT_TRUNCATED";
    public const string BranchedCompositionCandidateValidationFailed = "BRANCHED_COMPOSITION_CANDIDATE_VALIDATION_FAILED";
    public const string BranchedCompositionProvenanceMismatch = "BRANCHED_COMPOSITION_PROVENANCE_MISMATCH";
    public const string BranchedCompositionKinematicIdMismatch = "BRANCHED_COMPOSITION_KINEMATIC_ID_MISMATCH";
    public const string BranchedCompositionSpatialIdMismatch = "BRANCHED_COMPOSITION_SPATIAL_ID_MISMATCH";
    public const string BranchedCompositionSemanticBindingMismatch = "BRANCHED_COMPOSITION_SEMANTIC_BINDING_MISMATCH";

    public const string EventBridgeArtifactInvalid = "EVENT_BRIDGE_ARTIFACT_INVALID";
    public const string EventBridgeUnsupportedProfile = "EVENT_BRIDGE_UNSUPPORTED_PROFILE";
    public const string EventBridgeUnknownDriver = "EVENT_BRIDGE_UNKNOWN_DRIVER";
    public const string EventBridgeMultipleDriversUnsupported = "EVENT_BRIDGE_MULTIPLE_DRIVERS_UNSUPPORTED";
    public const string EventBridgeUnknownSourceDof = "EVENT_BRIDGE_UNKNOWN_SOURCE_DOF";
    public const string EventBridgeInvalidDefinition = "EVENT_BRIDGE_INVALID_DEFINITION";
    public const string EventBridgeDuplicateDefinitionKey = "EVENT_BRIDGE_DUPLICATE_DEFINITION_KEY";
    public const string EventBridgeDuplicateNormalizedDefinition = "EVENT_BRIDGE_DUPLICATE_NORMALIZED_DEFINITION";
    public const string EventBridgeNonPositivePeriod = "EVENT_BRIDGE_NON_POSITIVE_PERIOD";
    public const string EventBridgeInvalidBudget = "EVENT_BRIDGE_INVALID_BUDGET";
    public const string EventBridgeReverseUnsupported = "EVENT_BRIDGE_REVERSE_UNSUPPORTED";
    public const string EventBridgeTraversalUnsupported = "EVENT_BRIDGE_TRAVERSAL_UNSUPPORTED";
    public const string EventBridgeSourceCoefficientUnsupported = "EVENT_BRIDGE_SOURCE_COEFFICIENT_UNSUPPORTED";
    public const string EventBridgeCancelled = "EVENT_BRIDGE_CANCELLED";
    public const string EventBridgeResultTruncated = "EVENT_BRIDGE_RESULT_TRUNCATED";

    public const string StateInvalidDefinition = "STATE_INVALID_DEFINITION";
    public const string StateDuplicateDefinition = "STATE_DUPLICATE_DEFINITION";
    public const string StateInvalidTransition = "STATE_INVALID_TRANSITION";
    public const string StateDuplicateTransition = "STATE_DUPLICATE_TRANSITION";
    public const string StateAmbiguousTransition = "STATE_AMBIGUOUS_TRANSITION";
    public const string StateUnknownTarget = "STATE_UNKNOWN_TARGET";
    public const string StateInvalidSnapshot = "STATE_INVALID_SNAPSHOT";
    public const string StateDuplicateSnapshotValue = "STATE_DUPLICATE_SNAPSHOT_VALUE";
    public const string StateMissingSnapshotValue = "STATE_MISSING_SNAPSHOT_VALUE";
    public const string StateDuplicateCursor = "STATE_DUPLICATE_CURSOR";
    public const string StateMissingCursor = "STATE_MISSING_CURSOR";
    public const string StateUnsupportedProfile = "STATE_UNSUPPORTED_PROFILE";
    public const string StateInvalidBudget = "STATE_INVALID_BUDGET";
    public const string StateEventStreamIncomplete = "STATE_EVENT_STREAM_INCOMPLETE";
    public const string StateSourceMismatch = "STATE_SOURCE_MISMATCH";
    public const string StateDriverMismatch = "STATE_DRIVER_MISMATCH";
    public const string StateRootIntervalMismatch = "STATE_ROOT_INTERVAL_MISMATCH";
    public const string StateUnknownEventDefinition = "STATE_UNKNOWN_EVENT_DEFINITION";
    public const string StateOccurrenceIdentityMismatch = "STATE_OCCURRENCE_IDENTITY_MISMATCH";
    public const string StateOccurrenceIntervalMismatch = "STATE_OCCURRENCE_INTERVAL_MISMATCH";
    public const string StateOccurrenceDuplicate = "STATE_OCCURRENCE_DUPLICATE";
    public const string StateOccurrenceStale = "STATE_OCCURRENCE_STALE";
    public const string StateOccurrenceGap = "STATE_OCCURRENCE_GAP";
    public const string StateResultTruncated = "STATE_RESULT_TRUNCATED";
    public const string StateCancelled = "STATE_CANCELLED";

    public const string StateLookupInvalidDefinition = "STATE_LOOKUP_INVALID_DEFINITION";
    public const string StateLookupDuplicateDefinition = "STATE_LOOKUP_DUPLICATE_DEFINITION";
    public const string StateLookupSelectorOutOfRange = "STATE_LOOKUP_SELECTOR_OUT_OF_RANGE";
    public const string StateLookupValueInvalid = "STATE_LOOKUP_VALUE_INVALID";
    public const string StateCompositeLookupInvalidDefinition = "STATE_COMPOSITE_LOOKUP_INVALID_DEFINITION";
    public const string StateCompositeLookupDuplicateTuple = "STATE_COMPOSITE_LOOKUP_DUPLICATE_TUPLE";
    public const string StateCompositeLookupMissingTuple = "STATE_COMPOSITE_LOOKUP_MISSING_TUPLE";
    public const string StateConstraintInvalidDefinition = "STATE_CONSTRAINT_INVALID_DEFINITION";
    public const string StateConstraintViolation = "STATE_CONSTRAINT_VIOLATION";
    public const string StateGuardInvalidDefinition = "STATE_GUARD_INVALID_DEFINITION";
    public const string StateGuardUnsupportedNesting = "STATE_GUARD_UNSUPPORTED_NESTING";
    public const string StateGuardNoMatch = "STATE_GUARD_NO_MATCH";
    public const string StateGuardAmbiguous = "STATE_GUARD_AMBIGUOUS";
    public const string StateModuloGuardInvalid = "STATE_MODULO_GUARD_INVALID";
    public const string StateComputedDefinitionInvalid = "STATE_COMPUTED_DEFINITION_INVALID";
    public const string StateComputedNoMatch = "STATE_COMPUTED_NO_MATCH";
    public const string StateComputedAmbiguous = "STATE_COMPUTED_AMBIGUOUS";
    public const string StateComputedLookupInvalidSelector = "STATE_COMPUTED_LOOKUP_INVALID_SELECTOR";
    public const string StateEffectInvalidDefinition = "STATE_EFFECT_INVALID_DEFINITION";
    public const string StateEffectTargetConflict = "STATE_EFFECT_TARGET_CONFLICT";
    public const string StateEffectInvalidIndex = "STATE_EFFECT_INVALID_INDEX";
    public const string StateGuardedPlanInvalid = "STATE_GUARDED_PLAN_INVALID";
    public const string StateGuardedEventUnsupported = "STATE_GUARDED_EVENT_UNSUPPORTED";
    public const string StateGuardedUnsupportedProfile = "STATE_GUARDED_UNSUPPORTED_PROFILE";
    public const string StateGuardedInvalidBudget = "STATE_GUARDED_INVALID_BUDGET";
    public const string StateGuardedResultTruncated = "STATE_GUARDED_RESULT_TRUNCATED";
    public const string StateGuardedCancelled = "STATE_GUARDED_CANCELLED";

    public const string StateSnapshotFormatInvalid = "STATE_SNAPSHOT_FORMAT_INVALID";
    public const string StateSnapshotUnsupportedVersion = "STATE_SNAPSHOT_UNSUPPORTED_VERSION";
    public const string StateSnapshotIdentityMismatch = "STATE_SNAPSHOT_IDENTITY_MISMATCH";
    public const string StateSnapshotPlanMismatch = "STATE_SNAPSHOT_PLAN_MISMATCH";
    public const string StateSnapshotCandidateMismatch = "STATE_SNAPSHOT_CANDIDATE_MISMATCH";
    public const string StateSnapshotDriverMismatch = "STATE_SNAPSHOT_DRIVER_MISMATCH";
    public const string StateSnapshotNotAuthoritative = "STATE_SNAPSHOT_NOT_AUTHORITATIVE";
    public const string StateSnapshotDeterminismProfileMismatch = "STATE_SNAPSHOT_DETERMINISM_PROFILE_MISMATCH";
    public const string StateSnapshotStateSetMismatch = "STATE_SNAPSHOT_STATE_SET_MISMATCH";
    public const string StateSnapshotCursorSetMismatch = "STATE_SNAPSHOT_CURSOR_SET_MISMATCH";
    public const string StateSnapshotInvalidValue = "STATE_SNAPSHOT_INVALID_VALUE";

    public const string PlaybackDriverMismatch = "PLAYBACK_DRIVER_MISMATCH";
    public const string PlaybackChannelMismatch = "PLAYBACK_CHANNEL_MISMATCH";
    public const string PlaybackBodyBindingMismatch = "PLAYBACK_BODY_BINDING_MISMATCH";
    public const string CandidateIdMismatch = "ARTIFACT_CANDIDATE_ID_MISMATCH";
    public const string ArtifactHashMismatch = "ARTIFACT_HASH_MISMATCH";
    public const string StoredValidationMismatch = "ARTIFACT_STORED_VALIDATION_MISMATCH";
}

public sealed class Diagnostic
{
    public Diagnostic(string code, DiagnosticSeverity severity, string message, string? subjectId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A diagnostic code is required.", nameof(code));
        }

        Code = code;
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        SubjectId = subjectId;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Message { get; }

    public string? SubjectId { get; }
}

public sealed class ValidationReport
{
    public ValidationReport(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }

    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
}

public static class DiagnosticOrdering
{
    public static ReadOnlyCollection<Diagnostic> Canonicalize(IEnumerable<Diagnostic> diagnostics)
    {
        if (diagnostics is null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        var items = diagnostics.ToList();
        items.Sort(Compare);
        return items.AsReadOnly();
    }

    private static int Compare(Diagnostic? left, Diagnostic? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var byCode = StringComparer.Ordinal.Compare(left.Code, right.Code);
        if (byCode != 0)
        {
            return byCode;
        }

        var bySubject = StringComparer.Ordinal.Compare(left.SubjectId, right.SubjectId);
        if (bySubject != 0)
        {
            return bySubject;
        }

        var bySeverity = left.Severity.CompareTo(right.Severity);
        return bySeverity != 0
            ? bySeverity
            : StringComparer.Ordinal.Compare(left.Message, right.Message);
    }
}
