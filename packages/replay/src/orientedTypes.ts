import type { ExactFraction } from "./types.js";

export type ExactVector3 = readonly [ExactFraction, ExactFraction, ExactFraction];
export type Vector3 = readonly [number, number, number];
/** Column basis, right-handed; Z is the positive shaft axis, not a cone's outward ray. */
export interface ExactFrame3 { readonly origin: ExactVector3; readonly x: ExactVector3; readonly y: ExactVector3; readonly z: ExactVector3; }
export interface Frame3 { readonly origin: Vector3; readonly x: Vector3; readonly y: Vector3; readonly z: Vector3; }
export interface OrientedShaft { readonly id: string; readonly frame: ExactFrame3; readonly isPrescribed: boolean; }
export interface OrientedPort { readonly id: string; readonly shaftId: string; readonly kind: "RigidZeroPhase"; readonly frame: ExactFrame3; readonly phaseOffset: ExactFraction; }
export interface OrientedBody {
  readonly id: string; readonly shaftId: string; readonly kind: "PlanarSpur" | "RightAngleBevel";
  readonly mountingFrame: ExactFrame3; readonly teeth: string; readonly outerPitchRadius: ExactFraction; readonly sourceModuleId: string;
}
export interface PitchCone {
  readonly apex: ExactVector3; readonly outwardA: ExactVector3; readonly outwardB: ExactVector3; readonly outerContact: ExactVector3;
  readonly innerParameter: ExactFraction; readonly outerScaleA: ExactFraction; readonly outerScaleB: ExactFraction; readonly coneDistanceSquared: ExactFraction;
}
export interface OrientedContact {
  readonly id: string; readonly kind: "ExternalSpur" | "RightAngleBevel"; readonly bodyAId: string; readonly bodyBId: string;
  readonly storedTransfer: ExactFraction; readonly cone: PitchCone | null;
}
export interface OrientedKeepOut { readonly id: string; readonly min: ExactVector3; readonly max: ExactVector3; }
export interface OrientedSourcePlacement {
  /** Opaque bounded base64 of the original bytes. Never decoded into a local solution by replay. */
  readonly sourceArtifactUtf8: string; readonly pose: ExactFrame3; readonly inputDofId: string; readonly outputDofId: string; readonly connectionPort: OrientedPort;
}
export interface BevelMount {
  readonly shaft: OrientedShaft; readonly coneDirection: ExactVector3; readonly teeth: string;
  readonly outerPitchRadiusPerTooth: ExactFraction; readonly fixedCenter: ExactVector3; readonly port: OrientedPort;
}
export interface OrientedRequestObservation {
  readonly format: "gear-invest.oriented-transmission-request"; readonly formatVersion: "0.1";
  readonly bevel: { readonly profile: string; readonly apex: ExactVector3; readonly input: BevelMount; readonly output: BevelMount;
    readonly innerParameter: ExactFraction; readonly requestedTransfer: ExactFraction | null; };
  readonly upstream: OrientedSourcePlacement | null; readonly downstream: OrientedSourcePlacement | null;
  readonly assemblyPose: ExactFrame3; readonly requestedTransfer: ExactFraction | null;
  readonly requireCrossComponentClearance: boolean; readonly keepOuts: readonly OrientedKeepOut[];
}
export interface OrientedMechanismObservation {
  readonly profile: string; readonly rootShaftId: string; readonly outputShaftId: string;
  readonly shafts: readonly OrientedShaft[]; readonly bodies: readonly OrientedBody[]; readonly contacts: readonly OrientedContact[];
  readonly ports: readonly OrientedPort[];
  readonly connections: readonly { readonly id: string; readonly kind: "RigidZeroPhase"; readonly portAId: string; readonly portBId: string; readonly coordinateTransfer: ExactFraction }[];
  readonly sourceMappings: readonly { readonly moduleId: string; readonly sourceDofId: string; readonly shaftId: string; readonly coordinateTransfer: ExactFraction }[];
  readonly sources: readonly { readonly moduleId: string; readonly candidateId: string; readonly artifactHash: string }[];
  readonly solution: { readonly rootDofId: string; readonly states: readonly { readonly dofId: string; readonly coefficient: ExactFraction; readonly phaseOffset: ExactFraction }[] };
  readonly requireCrossComponentClearance: boolean; readonly keepOuts: readonly OrientedKeepOut[];
}
export interface OrientedStoredValidation {
  readonly isValid: boolean;
  readonly domains: readonly { readonly domain: string; readonly subject: string; readonly verdict: "Pass" | "Fail" | "Inconclusive" | "NotPerformed"; readonly required: boolean; readonly detail: string }[];
  readonly diagnostics: readonly { readonly code: string; readonly severity: "Info" | "Warning" | "Error"; readonly message: string; readonly subjectId: string | null }[];
}
export interface OrientedArtifact {
  readonly format: "gear-invest.oriented-mechanism"; readonly formatVersion: "0.1";
  /** Stored observations, NOT verified identity. */
  readonly candidateId: string; readonly artifactHash: string;
  readonly request: OrientedRequestObservation; readonly mechanism: OrientedMechanismObservation; readonly validation: OrientedStoredValidation;
}
