import type { ExactFraction } from "./types.js";
import { parseFraction } from "./artifact.js";
import { ReplayInputError, UnsupportedArtifactError } from "./errors.js";
import { assertBoundedJson, utf8ByteLength } from "./bounded.js";
import { deepFreeze, SnapshotMap } from "./immutable.js";
import { checkedNumber, checkedProduct, fractionToNumber, roundoff, ResolvedPlaybackEvaluator } from "./playback.js";
import type { PlaybackSnapshot } from "./playback.js";

export const REPLAY_INSTANCE_LIMITS = Object.freeze({ bytes: 262144, artifacts: 32, instances: 128, totalBodies: 8192, totalDofs: 8192 });
export interface ExternalRootBinding {
  readonly kind: "externallyPrescribedInput";
  readonly sourceId: string;
  readonly sourceReferenceTurns: ExactFraction;
  readonly rootReferenceTurns: ExactFraction;
  readonly multiplier: ExactFraction;
}
export interface ReplayArtifactReference {
  readonly key: string;
  /** Stored identifier comparison, not authoritative identity recomputation. */
  readonly candidateId: string;
  readonly artifactHash: string;
  /** Optional expected original-byte digest. Resolver must supply its observed digest. */
  readonly rawSha256?: string;
}
export interface ReplayInstance {
  readonly instanceId: string;
  readonly artifactKey: string;
  readonly binding: ExternalRootBinding;
  /** Opaque caller-owned reference, not interpreted by replay. */
  readonly presentationRef?: string;
}
export interface ReplayInstancesDocument {
  readonly format: "gear-invest.replay-instances";
  readonly formatVersion: "0.1";
  readonly artifacts: readonly ReplayArtifactReference[];
  readonly instances: readonly ReplayInstance[];
}
export interface ReplayDefinitionResource {
  readonly definition: ResolvedPlaybackEvaluator;
  /** Observed by the caller on the original bytes, not computed from parsed summary JSON. */
  readonly rawSha256?: string;
}
export interface InstanceFrame {
  readonly instanceId: string;
  readonly artifactKey: string;
  readonly candidateId: string;
  readonly artifactHash: string;
  readonly inputSemantics: "externallyPrescribedInput";
  readonly playback: PlaybackSnapshot;
}
type RecordValue = Record<string, unknown>;
function record(value: unknown, keys: readonly string[], label: string): RecordValue {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new ReplayInputError("INVALID_DOCUMENT", `${label} must be an object.`);
  if (Object.keys(value).some(key => !keys.includes(key))) throw new ReplayInputError("UNKNOWN_FIELD", `${label} contains an unsupported field.`);
  return value as RecordValue;
}
function id(value: unknown, label: string): string {
  if (typeof value !== "string" || !value || value.length > 256 || value.trim() !== value)
    throw new ReplayInputError("INVALID_ID", `${label} must be a nonempty trimmed ID up to 256 characters.`);
  return value;
}
function collection(value: unknown, limit: number, label: string): readonly unknown[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > limit)
    throw new ReplayInputError("RESOURCE_LIMIT", `${label} must contain 1..${limit} entries.`);
  return value;
}
function unique(ids: readonly string[], label: string): void {
  if (new Set(ids).size !== ids.length) throw new ReplayInputError("DUPLICATE_ID", `Duplicate ${label}.`);
}
function ordinal(a: string, b: string): number { return a < b ? -1 : a > b ? 1 : 0; }

export function parseReplayInstancesDocument(value: unknown): ReplayInstancesDocument {
  assertBoundedJson(value, REPLAY_INSTANCE_LIMITS.bytes);
  const root = record(value, ["format", "formatVersion", "artifacts", "instances"], "document");
  if (root.format !== "gear-invest.replay-instances" || root.formatVersion !== "0.1")
    throw new UnsupportedArtifactError("Unsupported replay instance document format/version.");
  const artifacts = collection(root.artifacts, REPLAY_INSTANCE_LIMITS.artifacts, "artifacts").map(value => {
    const r = record(value, ["key", "candidateId", "artifactHash", "rawSha256"], "artifact reference");
    const reference: ReplayArtifactReference = { key: id(r.key, "artifact key"), candidateId: id(r.candidateId, "candidateId"), artifactHash: id(r.artifactHash, "artifactHash") };
    if (!Object.hasOwn(r, "rawSha256")) return reference;
    if (typeof r.rawSha256 !== "string" || !/^[0-9a-f]{64}$/.test(r.rawSha256))
      throw new ReplayInputError("INVALID_DIGEST", "rawSha256 must be 64 lowercase hexadecimal characters.");
    return { ...reference, rawSha256: r.rawSha256 };
  }).sort((a, b) => ordinal(a.key, b.key));
  unique(artifacts.map(a => a.key), "artifact key");
  const instances = collection(root.instances, REPLAY_INSTANCE_LIMITS.instances, "instances").map(value => {
    const r = record(value, ["instanceId", "artifactKey", "binding", "presentationRef"], "instance");
    const b = record(r.binding, ["kind", "sourceId", "sourceReferenceTurns", "rootReferenceTurns", "multiplier"], "external binding");
    if (b.kind !== "externallyPrescribedInput") throw new UnsupportedArtifactError("Only externallyPrescribedInput direct source bindings are supported.");
    const binding: ExternalRootBinding = { kind: b.kind, sourceId: id(b.sourceId, "sourceId"),
      sourceReferenceTurns: parseFraction(b.sourceReferenceTurns, "sourceReferenceTurns"),
      rootReferenceTurns: parseFraction(b.rootReferenceTurns, "rootReferenceTurns"), multiplier: parseFraction(b.multiplier, "multiplier") };
    const instance: ReplayInstance = { instanceId: id(r.instanceId, "instanceId"), artifactKey: id(r.artifactKey, "artifactKey"), binding };
    if (!artifacts.some(a => a.key === instance.artifactKey)) throw new ReplayInputError("MISSING_ARTIFACT_REFERENCE", `Unknown artifact key '${instance.artifactKey}'.`);
    return Object.hasOwn(r, "presentationRef") ? { ...instance, presentationRef: id(r.presentationRef, "presentationRef") } : instance;
  }).sort((a, b) => ordinal(a.instanceId, b.instanceId));
  unique(instances.map(i => i.instanceId), "instance ID");
  return deepFreeze({ format: root.format, formatVersion: root.formatVersion, artifacts, instances });
}
export function parseReplayInstancesDocumentText(text: string): ReplayInstancesDocument {
  if (typeof text !== "string" || utf8ByteLength(text) > REPLAY_INSTANCE_LIMITS.bytes)
    throw new ReplayInputError("RESOURCE_LIMIT", "Instance document text exceeds its input limit.");
  let value: unknown;
  try { value = JSON.parse(text); } catch { throw new ReplayInputError("INVALID_JSON", "Instance document is not valid JSON."); }
  return parseReplayInstancesDocument(value);
}
/** Stable versioned instance document writer, NOT a mechanical artifact writer. */
export function writeReplayInstancesDocument(value: ReplayInstancesDocument): string {
  return JSON.stringify(parseReplayInstancesDocument(value), null, 2) + "\n";
}

export class CompiledReplayInstances {
  readonly document: ReplayInstancesDocument;
  readonly #entries: readonly { instance: ReplayInstance; definition: ResolvedPlaybackEvaluator; source: number; root: number; multiplier: number }[];
  constructor(document: ReplayInstancesDocument, resources: ReadonlyMap<string, ReplayDefinitionResource>) {
    this.document = parseReplayInstancesDocument(document);
    const definitions = new Map<string, ResolvedPlaybackEvaluator>();
    for (const reference of this.document.artifacts) {
      const resource = resources.get(reference.key);
      if (!resource || !(resource.definition instanceof ResolvedPlaybackEvaluator))
        throw new ReplayInputError("MISSING_ARTIFACT_RESOURCE", `Caller must resolve artifact '${reference.key}'.`, reference.key);
      const artifact = resource.definition.artifact;
      if (artifact.candidateId !== reference.candidateId || artifact.artifactHash !== reference.artifactHash)
        throw new ReplayInputError("ARTIFACT_REFERENCE_MISMATCH", `Stored identifiers differ for '${reference.key}'.`, reference.key);
      if (reference.rawSha256 !== undefined && resource.rawSha256 !== reference.rawSha256)
        throw new ReplayInputError("RAW_DIGEST_MISMATCH", `Caller-observed original-byte digest differs for '${reference.key}'.`, reference.key);
      definitions.set(reference.key, resource.definition);
    }
    let bodies = 0, dofs = 0;
    this.#entries = this.document.instances.map(instance => {
      const definition = definitions.get(instance.artifactKey)!;
      bodies += definition.artifact.spatial.bodies.length; dofs += definition.artifact.kinematic.dofs.length;
      return { instance, definition, source: fractionToNumber(instance.binding.sourceReferenceTurns),
        root: fractionToNumber(instance.binding.rootReferenceTurns), multiplier: fractionToNumber(instance.binding.multiplier) };
    });
    if (bodies > REPLAY_INSTANCE_LIMITS.totalBodies || dofs > REPLAY_INSTANCE_LIMITS.totalDofs)
      throw new ReplayInputError("RESOURCE_LIMIT", "Expanded instance body/DOF resource limit exceeded.");
    Object.freeze(this);
  }
  sample(sources: ReadonlyMap<string, number>): ReadonlyMap<string, InstanceFrame> {
    // Atomic result: an error never returns a partial scene or a previous frame.
    const frames = this.#entries.map(e => {
      const sourceValue = sources.get(e.instance.binding.sourceId);
      if (sourceValue === undefined) throw new ReplayInputError("MISSING_SOURCE", `Missing external source '${e.instance.binding.sourceId}'.`, e.instance.binding.sourceId);
      const source = checkedNumber(sourceValue, e.instance.binding.sourceId);
      const delta = checkedNumber(source - e.source, "source delta");
      const product = checkedProduct(e.multiplier, delta, "external binding");
      const root = checkedNumber(e.root + product, "bound root");
      const deltaError = roundoff(e.source) + roundoff(delta);
      const rootError = roundoff(e.root) + Math.abs(e.multiplier) * deltaError +
        roundoff(e.multiplier) * (Math.abs(delta) + deltaError) + roundoff(product) + roundoff(root);
      return [e.instance.instanceId, Object.freeze({ instanceId: e.instance.instanceId, artifactKey: e.instance.artifactKey,
        candidateId: e.definition.artifact.candidateId, artifactHash: e.definition.artifact.artifactHash,
        inputSemantics: "externallyPrescribedInput" as const, playback: e.definition.evaluate(root, rootError) })] as const;
    });
    return new SnapshotMap(frames);
  }
}
export function compileReplayInstances(document: ReplayInstancesDocument, resources: ReadonlyMap<string, ReplayDefinitionResource>): CompiledReplayInstances {
  return new CompiledReplayInstances(document, resources);
}
export function sampleReplayInstances(instances: CompiledReplayInstances, sources: ReadonlyMap<string, number>): ReadonlyMap<string, InstanceFrame> {
  return instances.sample(sources);
}
