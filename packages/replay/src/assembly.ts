import { ReplayInputError, ArtifactInputError } from "./errors.js";
import { SnapshotMap } from "./immutable.js";
import { assemblyFraction, applyAffine, compareFraction, displayNumber, moduloTurn } from "./assemblyExact.js";
import { ASSEMBLY_LIMITS, assemblyReferenceKey, assemblyString, parseAssemblyReplay } from "./assemblyInput.js";
import type { ParsedAssembly } from "./assemblyInput.js";
import type { ExactFraction, AssemblyBody, AssemblyBodyPose, AssemblyFrame, AssemblyIntegrity, AssemblyReplayPayload } from "./assemblyTypes.js";
export type * from "./assemblyTypes.js";
export { ASSEMBLY_LIMITS, assemblyReferenceKey } from "./assemblyInput.js";
export { ArtifactInputError, UnsupportedArtifactError, ReplayInputError } from "./errors.js";

export const assemblyReplayCapabilities = Object.freeze({ readerVersion: "assembly-replay-reader/0.1", packageVersion: "0.1.0-rc.coaxial01.1", packageEntry: "@gearinvest/replay/assembly",
  format: "gear-invest.assembly-replay", formatVersion: "0.1", profile: "resolved-affine-and-sampled-assembly-v1",
  operations: Object.freeze(["read", "inspect", "verifyDigest", "evaluateResolvedAffine", "exactSample", "holdPrevious", "copyOriginalBytes", "dispose"]),
  unavailableOperations: Object.freeze(["synthesis", "mechanicalValidation", "sourceRebuild", "nonlinearSolve", "interpolation", "extrapolation", "automaticLoop"]),
  requiredFeatures: Object.freeze(["ESM", "BigInt", "ES2022", "Uint8Array"]), limits: ASSEMBLY_LIMITS });

const initialIntegrity: AssemblyIntegrity = Object.freeze({ schema: "Pass", references: "Pass", resourceBounds: "Pass", rawDigest: "notPerformed",
  envelopeSelfConsistency: "notPerformed", storedProducerValidation: "Finalized", browserMechanicalValidation: "notPerformed", currentSourceRebuild: "notPerformed" });
const constructionKey = Symbol("validated assembly replay");

/** Owns parsed definition and bytes; no load, clock, renderer or solver. Instances have independent disposal. */
export class AssemblyReplay {
  #parsed: ParsedAssembly | null;
  readonly #instances = new Map<string, AssemblyReplayInstance>();
  constructor(key: symbol, parsed: ParsedAssembly) {
    if (key !== constructionKey) throw new ArtifactInputError("Use readAssemblyReplay."); this.#parsed = parsed; Object.freeze(this);
  }
  get #data(): ParsedAssembly { if (!this.#parsed) throw new ReplayInputError("ASSEMBLY_DISPOSED", "Assembly replay disposed."); return this.#parsed; }
  get replayId(): string { return this.#data.replayId; }
  get payload(): AssemblyReplayPayload { return this.#data.payload; }
  get integrity(): AssemblyIntegrity { this.#data; return initialIntegrity; }
  get instanceCount(): number { return this.#instances.size; }
  originalBytes(): Uint8Array { return this.#data.bytes.slice(); }
  originalArtifactBytes(): Uint8Array { return this.#data.sourceBytes.slice(); }
  canonicalPayloadBytes(): Uint8Array { return this.#data.payloadBytes.slice(); }
  /** Host injects digest; results are returned, never upgraded into a mechanical-validation flag. */
  async verifyIntegrity(sha256: (bytes: Uint8Array) => string | Promise<string>): Promise<AssemblyIntegrity> {
    const p = this.#data;
    if (typeof sha256 !== "function") throw new ArtifactInputError("An explicit SHA-256 provider is required.");
    const payloadHash = await sha256(p.payloadBytes.slice()), sourceHash = await sha256(p.sourceBytes.slice());
    if (payloadHash !== p.replayId || sourceHash !== p.payload.source.rawSha256) throw new ArtifactInputError("Replay/source raw digest mismatch.");
    this.#data; return Object.freeze({ ...initialIntegrity, rawDigest: "Pass", envelopeSelfConsistency: "Pass" });
  }
  createInstance(instanceId: string): AssemblyReplayInstance {
    const p = this.#data; assemblyString(instanceId);
    if (this.#instances.has(instanceId) || this.#instances.size >= ASSEMBLY_LIMITS.instances || (this.#instances.size+1) * p.payload.analysis.inventory.length > ASSEMBLY_LIMITS.expandedInventory)
      throw new ReplayInputError("ASSEMBLY_RESOURCE", "Duplicate instance or aggregate instance/inventory limit.");
    const instance = new AssemblyReplayInstance(constructionKey, instanceId, this, () => this.#instances.delete(instanceId));
    this.#instances.set(instanceId,instance); return instance;
  }
  dispose(): void { for (const instance of this.#instances.values()) instance.dispose(); this.#instances.clear(); this.#parsed = null; }
}
export function readAssemblyReplay(input: string | Uint8Array): AssemblyReplay { return new AssemblyReplay(constructionKey,parseAssemblyReplay(input)); }

function pose(body: AssemblyBody, root: ExactFraction): AssemblyBodyPose {
  const unavailable = (reason: string): AssemblyBodyPose => Object.freeze({ reference: body.reference, status: "unavailable", reason, matrixMm: null });
  if (body.mode !== "exactAffine" || !body.q || !body.p || !body.fixedFrameMm) return unavailable("Sampled body requires an in-range stored observation.");
  try {
    const f = body.fixedFrameMm, angle = displayNumber(moduloTurn(applyAffine(body.q,body.p,root)),1) * 2 * Math.PI;
    const axis = body.positiveAxis.map(v => displayNumber(v)), c = Math.cos(angle), s = Math.sin(angle);
    const rotate = (v: readonly ExactFraction[]) => {
      const a = v.map(v => displayNumber(v)), dot = axis.reduce((sum,z,i) => sum+z*a[i]!,0);
      const cross = [axis[1]!*a[2]!-axis[2]!*a[1]!,axis[2]!*a[0]!-axis[0]!*a[2]!,axis[0]!*a[1]!-axis[1]!*a[0]!];
      return a.map((n,i) => n*c + cross[i]!*s + axis[i]!*dot*(1-c));
    };
    const matrixMm = Object.freeze([...rotate(f.x),0,...rotate(f.y),0,...rotate(f.z),0,...f.origin.map(v => displayNumber(v)),1]);
    return Object.freeze({ reference: body.reference, status: "displayApproximation", reason: null, matrixMm });
  } catch (e) { if (e instanceof ReplayInputError) return unavailable(e.message); throw e; }
}

export class AssemblyReplayInstance {
  #definition: AssemblyReplay | null; #release: (() => void) | null;
  readonly instanceId: string;
  constructor(key: symbol, instanceId: string, definition: AssemblyReplay, release: () => void) {
    if (key !== constructionKey) throw new ReplayInputError("ASSEMBLY_CONSTRUCTION", "Use replay.createInstance.");
    this.instanceId = instanceId; this.#definition = definition; this.#release = release; Object.freeze(this);
  }
  get definition(): AssemblyReplay { if (!this.#definition) throw new ReplayInputError("ASSEMBLY_DISPOSED", "Assembly instance disposed."); return this.#definition; }
  /** Independent affine-only query, including outside sampled range. Nonlinear bodies stay unavailable. */
  evaluateAffine(rootValue: ExactFraction): AssemblyFrame {
    const definition = this.definition, root = assemblyFraction(rootValue), p = definition.payload;
    return Object.freeze({ instanceId: this.instanceId, replayId: definition.replayId, sourceArtifactId: p.source.artifactId,
      requestedRoot: root, sampledRoot: null, mode: "affineOnly", sample: null,
      shafts: new SnapshotMap(p.shafts.map(s => [assemblyReferenceKey(s.reference), applyAffine(s.q,s.p,root)] as const)),
      bodyFrames: Object.freeze(p.bodies.map(b => pose(b,root))) });
  }
  /** Held frames consistently evaluate all parts at sampledRoot, not a mixed requested-root pose. */
  sample(rootValue: ExactFraction, policy: "exact" | "holdPrevious" = "exact"): AssemblyFrame {
    if (policy !== "exact" && policy !== "holdPrevious") throw new ReplayInputError("ASSEMBLY_POLICY", "No interpolation/extrapolation/loop policy is supported.");
    const definition = this.definition, root = assemblyFraction(rootValue), p = definition.payload, samples = p.samples;
    if (compareFraction(root,samples[0]!.root) < 0 || compareFraction(root,samples.at(-1)!.root) > 0) throw new ReplayInputError("ASSEMBLY_SAMPLE_RANGE", "Requested root is outside the finite sample range.");
    let lo = 0, hi = samples.length-1;
    while (lo < hi) { const mid = Math.ceil((lo+hi)/2); if (compareFraction(samples[mid]!.root,root) <= 0) lo = mid; else hi = mid-1; }
    const sample = samples[lo]!, exact = compareFraction(sample.root,root) === 0;
    if (!exact && policy === "exact") throw new ReplayInputError("ASSEMBLY_SAMPLE_MISSING", "No exact stored observation at requested root; explicitly select holdPrevious or evaluateAffine.");
    return Object.freeze({ instanceId: this.instanceId, replayId: definition.replayId, sourceArtifactId: p.source.artifactId,
      requestedRoot: root, sampledRoot: sample.root, mode: exact ? "exactSample" : "heldSample", sample,
      shafts: new SnapshotMap(p.shafts.map(s => [assemblyReferenceKey(s.reference),applyAffine(s.q,s.p,sample.root)] as const)), bodyFrames: sample.bodyFrames });
  }
  dispose(): void { this.#release?.(); this.#release = null; this.#definition = null; }
}
