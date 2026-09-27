import type { ExactFraction } from "./types.js";
import type { ExactVector3, ExactFrame3, OrientedArtifact, OrientedShaft, OrientedPort, OrientedBody, OrientedContact, PitchCone, OrientedKeepOut, OrientedSourcePlacement, BevelMount, OrientedRequestObservation, OrientedStoredValidation } from "./orientedTypes.js";
import { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";
import { orientedReaders, decodeOrientedSource } from "./orientedPrimitives.js";
import { deepFreeze } from "./immutable.js";
import { ORIENTED_LIMITS as L, assertOrientedJson, readOrientedJsonText } from "./orientedJson.js";
export { ORIENTED_LIMITS } from "./orientedJson.js";
export const ORIENTED_PROFILE = "cardinal-right-angle-pitch-cone-transmission-v1";
const owned = new WeakSet<OrientedArtifact>();
const {bad,rec,str,kind,bool,array,frac,positive,phase,optional,teeth,vector,cardinal,frame,shaft,port,body,lambda,cone,contact,keepOut,base64,placement,mount,storedValidation,hash,unique,reference,checkGraphReferences} = orientedReaders(L);
function request(v: unknown): OrientedRequestObservation {
  const r = rec(v, "format formatVersion bevel upstream downstream assemblyPose requestedTransfer requireCrossComponentClearance keepOuts");
  const b = rec(r.bevel, "profile apex input output innerParameter requestedTransfer");
  return { format: kind(r.format, ["gear-invest.oriented-transmission-request"]), formatVersion: kind(r.formatVersion, ["0.1"]),
    bevel: { profile: kind(b.profile, [ORIENTED_PROFILE]), apex: vector(b.apex), input: mount(b.input), output: mount(b.output), innerParameter: lambda(b.innerParameter), requestedTransfer: optional(b.requestedTransfer) },
    upstream: placement(r.upstream), downstream: placement(r.downstream), assemblyPose: frame(r.assemblyPose), requestedTransfer: optional(r.requestedTransfer), requireCrossComponentClearance: bool(r.requireCrossComponentClearance), keepOuts: array(r.keepOuts, L.keepOuts, keepOut) };
}
export function parseOrientedArtifactText(text: string): OrientedArtifact { return parseOrientedArtifact(readOrientedJsonText(text)); }
export function parseOrientedArtifact(value: unknown): OrientedArtifact {
  assertOrientedJson(value);
  // Dispatch before looking for format-specific fields so planar entry rejection is explicit.
  if (!value || typeof value !== "object" || Array.isArray(value)) return bad("Expected oriented artifact.");
  kind((value as Record<string, unknown>).format, ["gear-invest.oriented-mechanism"]);
  const r = rec(value, "format formatVersion candidateId artifactHash request mechanism validation");
  const m = rec(r.mechanism, "profile rootShaftId outputShaftId shafts bodies contacts ports connections sourceMappings sources solution requireCrossComponentClearance keepOuts");
  const sol = rec(m.solution, "rootDofId states");
  const result: OrientedArtifact = { format: "gear-invest.oriented-mechanism", formatVersion: kind(r.formatVersion, ["0.1"]), candidateId: hash(r.candidateId), artifactHash: hash(r.artifactHash), request: request(r.request), validation: storedValidation(r.validation),
    mechanism: { profile: kind(m.profile, [ORIENTED_PROFILE]), rootShaftId: str(m.rootShaftId), outputShaftId: str(m.outputShaftId),
      shafts: array(m.shafts, L.shafts, shaft), bodies: array(m.bodies, L.bodies, body), contacts: array(m.contacts, L.contacts, contact), ports: array(m.ports, L.ports, port),
      connections: array(m.connections, L.connections, v => { const r = rec(v, "id kind portAId portBId coordinateTransfer"); return { id: str(r.id), kind: kind(r.kind, ["RigidZeroPhase"]), portAId: str(r.portAId), portBId: str(r.portBId), coordinateTransfer: frac(r.coordinateTransfer) }; }),
      sourceMappings: array(m.sourceMappings, L.mappings, v => { const r = rec(v, "moduleId sourceDofId shaftId coordinateTransfer"); return { moduleId: str(r.moduleId), sourceDofId: str(r.sourceDofId), shaftId: str(r.shaftId), coordinateTransfer: frac(r.coordinateTransfer) }; }),
      sources: array(m.sources, L.sources, v => { const r = rec(v, "moduleId candidateId artifactHash"); return { moduleId: str(r.moduleId), candidateId: str(r.candidateId, 256), artifactHash: str(r.artifactHash, 256) }; }),
      solution: { rootDofId: str(sol.rootDofId), states: array(sol.states, L.shafts, v => { const r = rec(v, "dofId coefficient phaseOffset"); return { dofId: str(r.dofId), coefficient: frac(r.coefficient), phaseOffset: phase(r.phaseOffset) }; }) },
      requireCrossComponentClearance: bool(m.requireCrossComponentClearance), keepOuts: array(m.keepOuts, L.keepOuts, keepOut) } };
  checkGraphReferences(result.mechanism);
  reference(new Set(result.mechanism.shafts.map(s=>s.id)),result.mechanism.outputShaftId);
  unique(result.request.keepOuts,k=>k.id);
  deepFreeze(result); owned.add(result); return result;
}
export function assertOrientedArtifact(a: OrientedArtifact): void { if (!owned.has(a)) bad("Parse the original oriented artifact before compilation."); }
/** Fresh copy on every call. Opaque provenance only; no embedded source parser/solver. */
export function readOrientedSourceBytes(a: OrientedArtifact, side: "upstream" | "downstream"): Uint8Array | null {
  assertOrientedArtifact(a); const p = a.request[side]; if (!p) return null;
  return decodeOrientedSource(p.sourceArtifactUtf8);
}
