import type { ExactFraction, MechanismArtifact } from "./types.js";
import { assertParsedArtifact, parseFraction } from "./artifact.js";
import { ReplayInputError } from "./errors.js";
import { SnapshotMap } from "./immutable.js";

export const REPLAY_NUMERIC_PROFILE = Object.freeze({
  id: "bounded-binary64/0.1", maxMagnitude: 2 ** 26,
  precision: "presentationApproximation", absoluteToleranceTurns: 1e-6,
} as const);
export function checkedNumber(value: number, label: string): number {
  if (!Number.isFinite(value) || Math.abs(value) > REPLAY_NUMERIC_PROFILE.maxMagnitude)
    throw new ReplayInputError("NUMERIC_RANGE", `${label} must be finite with magnitude <= 2^26.`, label);
  return Object.is(value, -0) ? 0 : value;
}
export function checkedProduct(a: number, b: number, label: string): number {
  const result = checkedNumber(a * b, label);
  if (a !== 0 && b !== 0 && (result === 0 || Math.abs(result) < 2 ** -1022))
    throw new ReplayInputError("NUMERIC_UNDERFLOW", `${label} underflows the supported normal binary64 range.`, label);
  return result;
}
export function fractionToNumber(fraction: ExactFraction): number {
  const exact = parseFraction(fraction, "fraction");
  return checkedNumber(Number(BigInt(exact.numerator)) / Number(BigInt(exact.denominator)), "fraction");
}
// Conservative binary64 roundoff allowance (includes integer conversion/division).
// Not a second rational evaluator: this only bounds presentation arithmetic loss.
export function roundoff(value: number): number { return value === 0 ? 0 : 4 * Number.EPSILON * Math.abs(value) + Number.MIN_VALUE; }
function errorBound(value: number): number {
  if (!Number.isFinite(value) || value < 0 || value > REPLAY_NUMERIC_PROFILE.absoluteToleranceTurns)
    throw new ReplayInputError("PRECISION_LOSS", "Presentation error bound exceeds 1e-6 turns; use a smaller input/binding amplification.");
  return value;
}
export function formatFraction(fraction: ExactFraction): string {
  return fraction.denominator === "1" ? fraction.numerator : `${fraction.numerator}/${fraction.denominator}`;
}
export function normalizeTurns(turns: number): number {
  checkedNumber(turns, "display turns"); const result = turns % 1;
  return result < 0 ? result + 1 : Object.is(result, -0) ? 0 : result;
}
export function turnsToDegrees(turns: number): number { return normalizeTurns(turns) * 360; }
export interface PlaybackSnapshot {
  readonly rootTurns: number;
  readonly dofTurns: ReadonlyMap<string, number>;
  readonly bodyTurns: ReadonlyMap<string, number>;
  readonly precision: "presentationApproximation";
  /** Conservative arithmetic bound, relative to caller's binary64 input and exact stored fractions. */
  readonly absoluteErrorBoundTurns: number;
}
/** Immutable compiled definition. No clock, accumulated phase, mesh solving or renderer. */
export class ResolvedPlaybackEvaluator {
  readonly artifact: MechanismArtifact;
  readonly #channels: readonly { id: string; coefficient: number; phase: number }[];
  readonly #bindings: readonly { id: string; dofId: string; phase: number }[];
  constructor(artifact: MechanismArtifact) {
    assertParsedArtifact(artifact); this.artifact = artifact;
    this.#channels = artifact.resolvedPlayback.channels.map(c => ({ id: c.dofId,
      coefficient: fractionToNumber(c.exactCoefficient), phase: fractionToNumber(c.exactPhaseOffset) }));
    this.#bindings = artifact.resolvedPlayback.bodyBindings.map(b => ({ id: b.bodyId,
      dofId: b.dofId, phase: fractionToNumber(b.exactMountingPhase) }));
    Object.freeze(this);
  }
  /** Optional input uncertainty is propagated by external bindings, not accumulated over time. */
  evaluate(rootTurns: number, absoluteInputErrorTurns = 0): PlaybackSnapshot {
    rootTurns = checkedNumber(rootTurns, "rootTurns");
    errorBound(absoluteInputErrorTurns);
    const dofs = new Map<string, number>();
    const errors = new Map<string, number>();
    let maximumError = absoluteInputErrorTurns;
    for (const c of this.#channels) {
      const product = checkedProduct(rootTurns, c.coefficient, c.id), value = checkedNumber(product + c.phase, c.id);
      const error = errorBound(Math.abs(c.coefficient) * absoluteInputErrorTurns +
        (Math.abs(rootTurns) + absoluteInputErrorTurns) * roundoff(c.coefficient) + roundoff(product) + roundoff(c.phase) + roundoff(value));
      dofs.set(c.id, value); errors.set(c.id, error); maximumError = Math.max(maximumError, error);
    }
    const bodies = this.#bindings.map(b => {
      const value = checkedNumber(dofs.get(b.dofId)! + b.phase, b.id);
      const error = errorBound(errors.get(b.dofId)! + roundoff(b.phase) + roundoff(value));
      maximumError = Math.max(maximumError, error); return [b.id, value] as const;
    });
    return Object.freeze({ rootTurns, dofTurns: new SnapshotMap(dofs), bodyTurns: new SnapshotMap(bodies),
      precision: "presentationApproximation" as const, absoluteErrorBoundTurns: maximumError });
  }
}
export function compileResolvedPlayback(artifact: MechanismArtifact): ResolvedPlaybackEvaluator {
  return new ResolvedPlaybackEvaluator(artifact);
}
export function sampleResolvedPlayback(definition: ResolvedPlaybackEvaluator, rootTurns: number): PlaybackSnapshot {
  return definition.evaluate(rootTurns);
}
export function inspectReplayCompatibility(artifact: MechanismArtifact) {
  assertParsedArtifact(artifact);
  let numeric: "supported" | "unsupported" = "supported";
  const diagnostics: string[] = [];
  try { compileResolvedPlayback(artifact); } catch (e) {
    if (!(e instanceof ReplayInputError)) throw e;
    numeric = "unsupported"; diagnostics.push(e.message);
  }
  if (artifact.validation.status === "invalid") diagnostics.push("Producer stored INVALID; inspection/replay is not mechanical approval.");
  return Object.freeze({ consumptionValidation: "accepted" as const, numericCompatibility: numeric,
    storedValidation: artifact.validation.status, identityVerification: "notPerformed" as const,
    mechanicalValidation: "notPerformed" as const, canonicalRoundTrip: "notPerformed" as const,
    diagnostics: Object.freeze(diagnostics) });
}
