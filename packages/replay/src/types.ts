/** Original exact strings are data, not a claim that numeric replay is exact. */
export interface ExactFraction {
  readonly numerator: string;
  readonly denominator: string;
}

export interface ArtifactSource {
  readonly specificationId: string;
  readonly seed: string;
  readonly generationOptions: string;
}

export interface GeneratorFingerprint {
  readonly sdkVersion: string;
  readonly generatorVersion: string;
  readonly backendId: string;
  readonly determinismProfile: string;
}

export interface RotationalDof {
  readonly id: string;
  readonly kind: "rotational";
  readonly prescribed: boolean;
}

export interface ExternalGearCoupling {
  readonly id: string;
  readonly kind: "externalGear";
  readonly driverDofId: string;
  readonly drivenDofId: string;
  readonly driverTeeth: number;
  readonly drivenTeeth: number;
  readonly phaseOffset: ExactFraction;
}

export interface KinematicState {
  readonly dofId: string;
  readonly coefficient: ExactFraction;
  readonly phaseOffset: ExactFraction;
}

export interface SpatialAxis {
  readonly id: string;
  readonly x: string;
  readonly y: string;
}

export interface SpatialBody {
  readonly id: string;
  readonly kind: "gear";
  readonly axisId: string;
  readonly dofId: string;
  readonly layer: number;
  readonly toothCount: number;
  readonly pitchRadius: string;
  readonly exactMountingPhase: ExactFraction;
}

export interface SpatialContact {
  readonly id: string;
  readonly kind: "externalGearMesh";
  readonly constraintId: string;
  readonly bodyAId: string;
  readonly bodyBId: string;
}

export interface PlaybackDriver {
  readonly id: string;
  readonly rootDofId: string;
}

export interface PlaybackChannel {
  readonly dofId: string;
  readonly driverId: string;
  readonly exactCoefficient: ExactFraction;
  readonly exactPhaseOffset: ExactFraction;
}

export interface PlaybackBodyBinding {
  readonly bodyId: string;
  readonly dofId: string;
  readonly exactMountingPhase: ExactFraction;
}

export interface StoredDiagnostic {
  readonly code: string;
  readonly severity: string;
  readonly message: string;
  readonly subjectId?: string;
}

export interface KnownOptionalSectionObservation {
  readonly presence: "present" | "absent";
  readonly acceptedKnownOptional: boolean;
  readonly mechanicallyUsed: false;
}

export interface SemanticBindingObservation {
  readonly semanticNodeId: string;
  readonly dofId: string;
  readonly role: "root" | "intermediateOutput" | "finalOutput" | "sharedBranchOutput" | "leafOutput";
}

export interface CompositionMetadataObservation {
  readonly presence: "present" | "absent";
  readonly acceptedKnownOptional: boolean;
  readonly mechanicallyUsed: false;
  readonly candidateIdentityInput: false;
  readonly playbackSource: false;
  readonly referencesValid: boolean;
  readonly topologyKind?: "threeEdgeSingleSharedNodeBranch";
  readonly branchSelectionCount?: number;
  readonly sourceEdgeReferencesValid?: boolean;
  readonly semanticBindings: readonly SemanticBindingObservation[];
}

export interface MechanismArtifact {
  readonly format: "gear-invest.mechanism";
  readonly formatVersion: "0.1";
  readonly candidateId: string;
  readonly artifactHash: string;
  readonly source: ArtifactSource;
  readonly generator: GeneratorFingerprint;
  readonly kinematic: {
    readonly rootDofId: string;
    readonly dofs: readonly RotationalDof[];
    readonly couplings: readonly ExternalGearCoupling[];
    readonly solution: readonly KinematicState[];
  };
  readonly spatial: {
    readonly axes: readonly SpatialAxis[];
    readonly bodies: readonly SpatialBody[];
    readonly contacts: readonly SpatialContact[];
  };
  readonly resolvedPlayback: {
    readonly drivers: readonly PlaybackDriver[];
    readonly channels: readonly PlaybackChannel[];
    readonly bodyBindings: readonly PlaybackBodyBinding[];
  };
  readonly validation: {
    readonly status: "valid" | "invalid";
    readonly diagnostics: readonly StoredDiagnostic[];
  };
  readonly optionalSections: {
    readonly generation: KnownOptionalSectionObservation;
    readonly metrics: KnownOptionalSectionObservation;
    readonly composition: KnownOptionalSectionObservation;
  };
  readonly compositionMetadata: CompositionMetadataObservation;
}
