import type { ExactFraction } from "./types.js";
export type { ExactFraction } from "./types.js";
export interface CarrierLaw { readonly q: ExactFraction; readonly p: ExactFraction }
export interface CarrierFrame { readonly origin: readonly ExactFraction[]; readonly x: readonly ExactFraction[]; readonly y: readonly ExactFraction[]; readonly z: readonly ExactFraction[] }
export interface CarrierShaft { readonly id: string; readonly motion: "FixedAxis" | "GroundHeld" | "Orbiting"; readonly poseNodeId: string; readonly world: CarrierLaw; readonly carrierRelative: CarrierLaw | null }
export interface CarrierNode { readonly id: string; readonly parentId: string | null; readonly frame: CarrierFrame; readonly rotation: CarrierLaw;
  readonly shaftId: string; readonly bodyId: string | null; readonly teeth: string | null; readonly pitchRadiusMm: ExactFraction | null }
export interface CarrierCompiled { readonly policy: string; readonly rootShaftId: string; readonly carrierShaftId: string; readonly sunShaftId: string; readonly planetShaftId: string; readonly portId: string;
  readonly isValid: true; readonly checks: readonly { readonly domain: string; readonly verdict: string; readonly required: boolean; readonly detail: string }[];
  readonly diagnostics: readonly unknown[]; readonly carrierCommon: CarrierLaw; readonly planetCommon: CarrierLaw; readonly portReadout: CarrierLaw;
  readonly shafts: readonly CarrierShaft[]; readonly poseNodes: readonly CarrierNode[] }
export interface CarrierPayload { readonly profile: string; readonly artifactId: string; readonly definitionId: string; readonly sourceRawSha256: string; readonly sourceArtifactUtf8: string; readonly compiled: CarrierCompiled }
export interface CarrierEvaluation { readonly instanceId: string; readonly replayId: string; readonly input: ExactFraction;
  readonly carrierCommon: ExactFraction; readonly planetCommon: ExactFraction; readonly portReadout: ExactFraction;
  readonly shafts: readonly { readonly id: string; readonly world: ExactFraction; readonly carrierRelative: ExactFraction | null }[];
  readonly display: { readonly status: "displayApproximation" | "unavailable"; readonly reason: string | null;
    readonly nodes: readonly { readonly id: string; readonly matrixMm: readonly number[] }[] } }
