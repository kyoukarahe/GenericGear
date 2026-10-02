import type { ExactFraction } from "./types.js";
import type { CarrierFrame } from "./carrierTypes.js";
export type { ExactFraction } from "./types.js";
export interface DifferentialLaw { readonly q: Readonly<Record<string, ExactFraction>>; readonly b: ExactFraction }
export interface DifferentialNode { readonly id: string; readonly parentId: string | null; readonly frame: CarrierFrame; readonly rotation: DifferentialLaw;
  readonly shaftId: string; readonly bodyId: string | null; readonly teeth: string | null; readonly pitchRadiusMm: ExactFraction | null }
export interface DifferentialCoordinate { readonly shaftId: string; readonly isKnown: boolean; readonly law: DifferentialLaw | null; readonly freeTerms: Readonly<Record<string, ExactFraction>> }
export interface DifferentialCompiled { readonly policy: string; readonly definitionId: string; readonly requestId: string; readonly status: string; readonly canExport: boolean; readonly isFullyDetermined: boolean;
  readonly carrierShaftId: string; readonly sunShaftId: string; readonly planetShaftId: string; readonly inputPortIds: readonly string[]; readonly coordinateIds: readonly string[]; readonly rank: string | null;
  readonly rows: readonly { readonly id: string; readonly a: readonly ExactFraction[]; readonly rhs: readonly ExactFraction[] }[];
  readonly reducedRows: readonly { readonly pivot: string; readonly a: readonly ExactFraction[]; readonly rhs: readonly ExactFraction[]; readonly witnessRows: readonly string[] }[];
  readonly checks: readonly { readonly domain: string; readonly verdict: string; readonly required: boolean; readonly detail: string }[];
  readonly diagnostics: readonly { readonly code: string; readonly stage: string; readonly detail: string; readonly related: readonly string[] }[];
  readonly coordinates: readonly DifferentialCoordinate[]; readonly carrierCommon: DifferentialLaw | null; readonly planetCommon: DifferentialLaw | null; readonly planetRelative: DifferentialLaw | null;
  readonly ports: readonly { readonly id: string; readonly shaftId: string; readonly sign: ExactFraction; readonly readoutOffset: { readonly kind: string; readonly unit: string; readonly value: ExactFraction } }[];
  readonly poseNodes: readonly DifferentialNode[] }
export interface DifferentialPayload { readonly profile: string; readonly artifactId: string; readonly requestId: string; readonly sourceRawSha256: string; readonly sourceArtifactUtf8: string; readonly compiled: DifferentialCompiled }
export interface DifferentialEvaluation { readonly instanceId: string; readonly replayId: string; readonly requestId: string; readonly input: Readonly<Record<string, ExactFraction>>;
  readonly coordinates: Readonly<Record<string, ExactFraction>>; readonly ports: Readonly<Record<string, ExactFraction>>;
  readonly carrierCommon: ExactFraction; readonly planetCommon: ExactFraction; readonly planetRelative: ExactFraction;
  readonly display: { readonly status: "displayApproximation" | "unavailable"; readonly reason: string | null; readonly nodes: readonly { readonly id: string; readonly matrixMm: readonly number[] }[] } }
