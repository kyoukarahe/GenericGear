// Example-local policy, NOT a new SDK API or a general mechanism-period solver.
import { normalizeTurns, REPLAY_NUMERIC_PROFILE } from '@gearinvest/replay';

const fail = message => { throw new RangeError(message); };
const digits = (n, limit) => { if (String(n).length > limit) fail('Example integer budget exceeded.'); return n; };
const gcd = (a, b) => { a = a < 0n ? -a : a; while (b) [a, b] = [b, a % b]; return a; };
export function fraction(n, d = 1n, limit = 128) {
  for (const part of [n,d]) if (!['string','bigint'].includes(typeof part) || String(part).length > 1024 || !/^-?\d+$/.test(String(part))) fail('Bounded integer text/BigInt required, not Number.');
  n = digits(BigInt(n), 1024); d = digits(BigInt(d), 1024);
  if (d <= 0n) fail('Positive denominator required.');
  const g = gcd(n, d); n /= g; d /= g;
  digits(n, limit); digits(d, limit);
  return Object.freeze({ numerator: String(n), denominator: String(d) });
}
export function exactInput(text) {
  if (typeof text !== 'string' || text.length > 257) fail('Bounded exact text required.');
  // No Number round-trip, exponent notation, or silent whitespace coercion.
  if (/^-?\d+\/\d+$/.test(text)) {
    const [n, d] = text.split('/'); if (n.length > 128 || d.length > 128) fail('Input digit limit.');
    return fraction(n, d);
  }
  if (!/^-?\d+(\.\d+)?$/.test(text)) fail('Use an integer, fraction, or finite decimal string.');
  const negative = text.startsWith('-'), [whole, tail = ''] = text.replace(/^-/, '').split('.');
  if (whole.length + tail.length + Number(negative) > 128 || tail.length > 127) fail('Input digit limit.');
  return fraction((negative ? -1n : 1n) * BigInt(whole + tail), 10n ** BigInt(tail.length));
}
export const exactText = f => f.denominator === '1' ? f.numerator : f.numerator + '/' + f.denominator;
export function outputPhase(exactTurns) {
  const f = fraction(exactTurns.numerator, exactTurns.denominator, 1024);
  const n = BigInt(f.numerator), d = BigInt(f.denominator);
  return fraction(((n % d) + d) % d, d, 1024); // Euclidean remainder, exact [0,1).
}
export function displayRadians(exactTurns) {
  const phase = outputPhase(exactTurns), n = Number(phase.numerator), d = Number(phase.denominator);
  const value = n / d;
  if (!Number.isFinite(n) || !Number.isFinite(d) || !Number.isFinite(value) || (n !== 0 && value === 0))
    fail('DISPLAY_UNAVAILABLE: exact state exists, bounded display conversion does not.');
  // normalizeTurns accepts Number only; it never owns exact accumulated state.
  return normalizeTurns(value) * 2 * Math.PI;
}

export const PERIOD_LIMITS = Object.freeze({ channels: 256, integerDigits: 128, intermediateDigits: 256, period: 1048576n });
export function rotationPeriod(channels, observationKind) {
  if (observationKind !== 'fixed-affine-rotations-only') fail('Periodic display reduction is inapplicable to this observation.');
  if (!Array.isArray(channels) || !channels.length || channels.length > PERIOD_LIMITS.channels) fail('Channel-count limit.');
  const ids = new Set(); let period = 1n;
  for (const { id, q } of channels) {
    if (typeof id !== 'string' || !id || id.length > 256 || ids.has(id)) fail('Distinct bounded channel identities required.');
    ids.add(id);
    if (String(q.numerator).length > PERIOD_LIMITS.integerDigits || String(q.denominator).length > PERIOD_LIMITS.integerDigits) fail('Example coefficient integer budget exceeded.');
    const f = fraction(q.numerator, q.denominator); // reduced, signed; q=0 -> denominator1
    const d = BigInt(f.denominator), factor = d / gcd(period, d);
    // Check before multiplying, including the intermediate size budget.
    if (String(period).length + String(factor).length > PERIOD_LIMITS.intermediateDigits || period > PERIOD_LIMITS.period / factor)
      fail('No safe representative: example common-period budget exceeded.');
    period *= factor;
  }
  // LCM of denominators is a sufficient INTEGER period, not necessarily minimal.
  return period;
}
export function displayRepresentative(exactRoot, period) {
  if (typeof period !== 'bigint' || period <= 0n || period > PERIOD_LIMITS.period) fail('Invalid/big display period.');
  const root = fraction(exactRoot.numerator, exactRoot.denominator), d = BigInt(root.denominator);
  const modulus = digits(period * d, PERIOD_LIMITS.intermediateDigits);
  const n = BigInt(root.numerator), reduced = fraction(((n % modulus) + modulus) % modulus, d);
  const a = Number(reduced.numerator), b = Number(reduced.denominator), value = a / b;
  const uncertainty = 8 * Number.EPSILON * Math.abs(value) + Number.MIN_VALUE;
  if (!Number.isFinite(value) || Math.abs(value) > REPLAY_NUMERIC_PROFILE.maxMagnitude || (a !== 0 && value === 0))
    fail('No safe bounded numeric representative.');
  // The legacy evaluator must STILL enforce coefficient/intermediate/error budgets.
  return Object.freeze({ exactRoot: root, displayRoot: reduced, number: value, inputUncertainty: uncertainty });
}

// Teaching arithmetic only. The viewer uses instance.evaluateAffine, not this function.
export function arithmeticExample(q, p, u) {
  [q,p,u] = [q,p,u].map(f => fraction(f.numerator,f.denominator));
  const [qn, qd, pn, pd, un, ud] = [q.numerator, q.denominator, p.numerator, p.denominator, u.numerator, u.denominator].map(BigInt);
  return fraction(qn * un * pd + pn * qd * ud, qd * ud * pd, 1024);
}
export function evaluateOrientedDisplay(definition, exactRoot) {
  const artifact = definition.artifact;
  if (artifact?.format !== 'gear-invest.oriented-mechanism' || artifact.formatVersion !== '0.1'
      || artifact.mechanism.profile !== 'cardinal-right-angle-pitch-cone-transmission-v1') fail('This example only reduces the fixed oriented0.1 rotation view.');
  const channels = artifact.mechanism.solution.states.map(s => ({ id:s.dofId, q:s.coefficient }));
  const period = rotationPeriod(channels, 'fixed-affine-rotations-only');
  const input = displayRepresentative(exactRoot, period);
  return { ...input, period: String(period), frame: definition.evaluate(input.number, input.inputUncertainty) };
}
