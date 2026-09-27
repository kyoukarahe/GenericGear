import { ArtifactInputError, ReplayInputError } from "./errors.js";
import type { ExactFraction } from "./types.js";

export function assemblyFraction(v: unknown, limit = 128): ExactFraction {
  if (!v || typeof v !== "object" || Array.isArray(v)) throw new ArtifactInputError("Exact fraction required.");
  const proto = Object.getPrototypeOf(v), descriptors = Object.getOwnPropertyDescriptors(v);
  if ((proto !== Object.prototype && proto !== null) || Reflect.ownKeys(v).length !== 2 ||
      Object.values(descriptors).some(d => !Object.hasOwn(d,"value") || !d.enumerable)) throw new ArtifactInputError("Owned plain fraction data required; no accessors.");
  const r = v as Record<string, unknown>;
  if (Object.keys(r).sort().join() !== "denominator,numerator" || typeof r.numerator !== "string" || typeof r.denominator !== "string"
      || r.numerator.length > limit || r.denominator.length > limit || !/^(0|-?[1-9][0-9]*)$/.test(r.numerator)
      || !/^[1-9][0-9]*$/.test(r.denominator)) throw new ArtifactInputError("Bounded canonical decimal fraction required.");
  if (gcd(BigInt(r.numerator), BigInt(r.denominator)) !== 1n) throw new ArtifactInputError("Fraction must be reduced.");
  return Object.freeze({ numerator: r.numerator, denominator: r.denominator });
}
function gcd(a: bigint, b: bigint): bigint { a = a < 0n ? -a : a; while (b) [a, b] = [b, a % b]; return a; }
function reduce(n: bigint, d: bigint): ExactFraction {
  const g = gcd(n, d); const r = { numerator: String(n / g), denominator: String(d / g) };
  if (r.numerator.length > 1024 || r.denominator.length > 1024) throw new ReplayInputError("ASSEMBLY_RESOURCE", "Derived rational resource bound exceeded.");
  return Object.freeze(r);
}
export function compareFraction(a: ExactFraction, b: ExactFraction): number {
  const diff = BigInt(a.numerator) * BigInt(b.denominator) - BigInt(b.numerator) * BigInt(a.denominator);
  return diff < 0n ? -1 : diff > 0n ? 1 : 0;
}
export function applyAffine(q: ExactFraction, p: ExactFraction, root: ExactFraction): ExactFraction {
  const n = BigInt(q.numerator) * BigInt(root.numerator), d = BigInt(q.denominator) * BigInt(root.denominator);
  return reduce(n * BigInt(p.denominator) + BigInt(p.numerator) * d, d * BigInt(p.denominator));
}
export function moduloTurn(a: ExactFraction): ExactFraction {
  const n = BigInt(a.numerator), d = BigInt(a.denominator); return reduce(((n % d) + d) % d, d);
}
/** Bounded display conversion only. Never use this to resolve a shaft relation. */
export function displayNumber(a: ExactFraction, maximum = 1e8): number {
  const n = Number(a.numerator), d = Number(a.denominator), v = n / d;
  if (!Number.isFinite(n) || !Number.isFinite(d) || !Number.isFinite(v) || Math.abs(v) > maximum || (v === 0 && a.numerator !== "0"))
    throw new ReplayInputError("DISPLAY_UNAVAILABLE", "Exact channel exists but display number is unavailable in the bounded binary64 profile.");
  return v;
}
