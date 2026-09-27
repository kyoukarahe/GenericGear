import type { ExactFraction } from "./types.js";
export type { ExactFraction } from "./types.js";
export type AssemblyJson = null | string | number | boolean | readonly AssemblyJson[] | { readonly [key: string]: AssemblyJson };
export interface AssemblyReference { readonly owner: "Root" | "Member"; readonly memberId: string | null; readonly kind: "Shaft" | "Body" | "Port" | "Output" | "Feature" | "LinearDof" | "Constraint"; readonly localId: string }
export interface AssemblyInventoryEntry { readonly reference: AssemblyReference; readonly role: string; readonly mountedShaft: AssemblyReference | null; readonly isPrescribed: boolean; readonly isNonlinearDependent: boolean }
export type AssemblyVector = readonly [ExactFraction, ExactFraction, ExactFraction];
export interface AssemblyFixedFrame { readonly origin: AssemblyVector; readonly x: AssemblyVector; readonly y: AssemblyVector; readonly z: AssemblyVector }
export interface AssemblyShaft { readonly reference: AssemblyReference; readonly mode: "exactAffine"; readonly unit: "turn"; readonly q: ExactFraction; readonly p: ExactFraction; readonly fixedFrameMm: AssemblyFixedFrame }
export interface AssemblyBody {
  readonly reference: AssemblyReference; readonly mountedShaft: AssemblyReference | null; readonly mode: "exactAffine" | "sampled";
  readonly q: ExactFraction | null; readonly p: ExactFraction | null; readonly fixedFrameMm: AssemblyFixedFrame | null; readonly positiveAxis: AssemblyVector;
  readonly specification: { readonly family: string; readonly teeth: number | null; readonly pitchRadiusMm: ExactFraction | null; readonly moduleMm: ExactFraction | null; readonly missingSpecification: "notSpecified"; readonly toothSolidValidation: "notPerformed" };
}
export interface AssemblyBodyPose { readonly reference: AssemblyReference; readonly status: "displayApproximation" | "unavailable"; readonly reason: string | null; readonly matrixMm: readonly number[] | null }
export interface AssemblyFeature { readonly memberId: string; readonly localId: string; readonly mechanicalReference: AssemblyReference; readonly scope: "displayApproximation"; readonly role: string; readonly closed: boolean; readonly pointsMm: readonly (readonly number[])[] }
export interface AssemblySample {
  readonly root: ExactFraction; readonly status: "storedAvailable" | "storedPartial";
  readonly exactBoundaries: readonly { readonly memberId: string; readonly regime: "LowerPhaseBoundary" | "UpperPhaseBoundary"; readonly recipeId: string }[];
  readonly originalEvaluationSha256: string; readonly observation: Readonly<Record<string, AssemblyJson>>;
  readonly bodyFrames: readonly AssemblyBodyPose[]; readonly displayFeatures: readonly AssemblyFeature[];
}
export interface AssemblyReplayPayload {
  readonly profile: "resolved-affine-and-sampled-assembly-v1";
  readonly source: { readonly format: "gear-invest.mechanical-assembly"; readonly formatVersion: string; readonly profile: string; readonly artifactId: string; readonly definitionId: string; readonly draftId: string; readonly analysisId: string; readonly rawSha256: string; readonly artifactUtf8: string };
  readonly request: { readonly sampleRoots: readonly ExactFraction[]; readonly includeExactGenevaBoundaries: boolean; readonly maximumTotalWork: number };
  readonly producer: { readonly operation: "current-source-finalize-and-evaluate"; readonly mechanicalValidation: "Finalized"; readonly exporterVersion: "assembly-web-export/0.1"; readonly packageVersion: string; readonly policy: string; readonly numericPolicy: string; readonly toothSolidValidation: "notPerformed" };
  readonly definition: Readonly<Record<string, AssemblyJson>>;
  readonly analysis: Readonly<Record<string, AssemblyJson>> & { readonly inventory: readonly AssemblyInventoryEntry[] };
  readonly shafts: readonly AssemblyShaft[]; readonly bodies: readonly AssemblyBody[]; readonly staticFeatures: readonly AssemblyFeature[]; readonly samples: readonly AssemblySample[];
}
export interface AssemblyIntegrity {
  readonly schema: "Pass"; readonly references: "Pass"; readonly resourceBounds: "Pass";
  readonly rawDigest: "notPerformed" | "Pass"; readonly envelopeSelfConsistency: "notPerformed" | "Pass";
  readonly storedProducerValidation: "Finalized"; readonly browserMechanicalValidation: "notPerformed"; readonly currentSourceRebuild: "notPerformed";
}
export interface AssemblyFrame {
  readonly instanceId: string; readonly replayId: string; readonly sourceArtifactId: string;
  readonly requestedRoot: ExactFraction; readonly sampledRoot: ExactFraction | null;
  readonly mode: "exactSample" | "heldSample" | "affineOnly";
  readonly sample: AssemblySample | null; readonly shafts: ReadonlyMap<string, ExactFraction>;
  readonly bodyFrames: readonly AssemblyBodyPose[];
}
