import type { ExactFraction } from './types.js';
import type { OrientedRequestObservation, OrientedMechanismObservation, OrientedPort, OrientedStoredValidation, OrientedSourcePlacement } from './orientedTypes.js';

export type OrientedOutputRole = 'ParallelBranch' | 'TurnedBranch';
export interface OrientedOutputBinding {
  readonly key:string; readonly role:OrientedOutputRole;
  readonly shaftId:string; readonly bodyId:string; readonly portId:string;
}
/** Request references are SOURCE-LOCAL. They are not aliases for the remapped final binding. */
export interface OrientedOutputRequestObservation {
  readonly key:string; readonly role:OrientedOutputRole; readonly terminalBodyId:string;
  readonly terminalPort:OrientedPort; readonly requestedTransfer:ExactFraction|null;
}
export interface OrientedTwoOutputRequestObservation {
  readonly format:'gear-invest.oriented-two-output-request'; readonly formatVersion:'0.1'; readonly profile:string;
  readonly bevel:OrientedRequestObservation['bevel'];
  readonly parallelBranch:OrientedSourcePlacement; readonly turnedBranch:OrientedSourcePlacement|null;
  readonly outputs:readonly OrientedOutputRequestObservation[];
  readonly assemblyPose:OrientedRequestObservation['assemblyPose'];
  readonly requireCrossComponentClearance:boolean; readonly keepOuts:OrientedRequestObservation['keepOuts'];
}
export interface OrientedTwoOutputMechanismObservation extends Omit<OrientedMechanismObservation,'outputShaftId'> {
  readonly outputs:readonly OrientedOutputBinding[];
}
export interface OrientedTwoOutputArtifact {
  readonly format:'gear-invest.oriented-two-output-mechanism'; readonly formatVersion:'0.1';
  /** Stored observations; not locally verified canonical identities. */
  readonly candidateId:string; readonly artifactHash:string;
  readonly request:OrientedTwoOutputRequestObservation; readonly mechanism:OrientedTwoOutputMechanismObservation;
  readonly validation:OrientedStoredValidation;
}
