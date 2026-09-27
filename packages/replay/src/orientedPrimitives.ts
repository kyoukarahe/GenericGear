import type { ExactFraction } from "./types.js";
import type { ExactVector3, ExactFrame3, OrientedShaft, OrientedPort, OrientedBody, OrientedContact, PitchCone, OrientedKeepOut, OrientedSourcePlacement, BevelMount, OrientedStoredValidation, OrientedMechanismObservation } from "./orientedTypes.js";
import { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";
import { parseFraction } from "./artifact.js";
import type { ORIENTED_LIMITS } from "./orientedJson.js";

/** Internal bounded field readers, with independent format limits. No format adaptation or solving. */
export function orientedReaders(L: {readonly [K in keyof typeof ORIENTED_LIMITS]:number}) {
const bad = (message: string): never => { throw new ArtifactInputError(message); };
function rec(v: unknown, keys: string): Record<string, unknown> {
  if (!v || typeof v !== "object" || Array.isArray(v)) return bad("Expected oriented record.");
  const names = keys.split(" "), r = v as Record<string, unknown>;
  if (Object.keys(r).some(k => !names.includes(k))) throw new UnsupportedArtifactError("Unknown oriented field.");
  if (names.some(k => !Object.hasOwn(r, k))) return bad("Missing oriented field: " + names.filter(k => !Object.hasOwn(r, k)).join(","));
  return r;
}
function str(v: unknown, max: number = L.idCharacters): string {
  if (typeof v !== "string" || !v || v.trim() !== v || v.length > max) return bad("Expected bounded nonempty string."); return v;
}
function kind<T extends string>(v: unknown, values: readonly T[]): T {
  if (typeof v !== "string" || !values.includes(v as T)) throw new UnsupportedArtifactError(`Unsupported oriented kind/profile/version '${String(v)}'.`); return v as T;
}
const bool = (v: unknown): boolean => typeof v === "boolean" ? v : bad("Expected boolean.");
function array<T>(v: unknown, max: number, parse: (v: unknown) => T): T[] {
  if (!Array.isArray(v) || v.length > max) return bad(`Oriented collection exceeds ${max} items or is not an array.`); return v.map(parse);
}
function frac(v: unknown): ExactFraction { rec(v, "numerator denominator"); return parseFraction(v, "oriented fraction"); }
function positive(v: unknown): ExactFraction { const f = frac(v); if (BigInt(f.numerator) <= 0n) return bad("Expected positive exact size."); return f; }
function phase(v: unknown): ExactFraction { const f = frac(v); if (f.numerator !== "0") throw new UnsupportedArtifactError("Oriented profile supports zero phase only."); return f; }
const optional = (v: unknown) => v === null ? null : frac(v);
function teeth(v: unknown): string { const s = str(v, 128); if (!/^[1-9][0-9]*$/.test(s) || BigInt(s) > BigInt(L.teeth)) return bad("Teeth exceed the bounded positive decimal integer range."); return s; }
function vector(v: unknown): ExactVector3 { const a = array(v, 3, frac); if (a.length !== 3) return bad("Vector needs 3 fractions."); return a as unknown as ExactVector3; }
function cardinal(v: unknown): ExactVector3 {
  const a = vector(v); if (a.some(f => f.denominator !== "1" || !["-1", "0", "1"].includes(f.numerator)) || a.filter(f => f.numerator !== "0").length !== 1) return bad("Expected exact cardinal vector."); return a;
}
function frame(v: unknown): ExactFrame3 {
  const r = rec(v, "origin x y z"), x = cardinal(r.x), y = cardinal(r.y), z = cardinal(r.z);
  const [a,b,c] = [x,y,z].map(v => v.map(f => Number(f.numerator))) as [number[], number[], number[]];
  if (a[0]!*b[0]!+a[1]!*b[1]!+a[2]!*b[2]! !== 0 || [a[1]!*b[2]!-a[2]!*b[1]!, a[2]!*b[0]!-a[0]!*b[2]!, a[0]!*b[1]!-a[1]!*b[0]!].some((v,i) => v !== c[i])) return bad("Frame must be proper right-handed cardinal, not reflection.");
  return { origin: vector(r.origin), x, y, z };
}
function shaft(v: unknown): OrientedShaft { const r = rec(v, "id frame isPrescribed"); return { id: str(r.id), frame: frame(r.frame), isPrescribed: bool(r.isPrescribed) }; }
function port(v: unknown): OrientedPort { const r = rec(v, "id shaftId kind frame phaseOffset"); return { id: str(r.id), shaftId: str(r.shaftId), kind: kind(r.kind, ["RigidZeroPhase"]), frame: frame(r.frame), phaseOffset: phase(r.phaseOffset) }; }
function body(v: unknown): OrientedBody { const r = rec(v, "id shaftId kind mountingFrame teeth outerPitchRadius sourceModuleId"); return { id: str(r.id), shaftId: str(r.shaftId), kind: kind(r.kind, ["PlanarSpur", "RightAngleBevel"]), mountingFrame: frame(r.mountingFrame), teeth: teeth(r.teeth), outerPitchRadius: positive(r.outerPitchRadius), sourceModuleId: str(r.sourceModuleId) }; }
function lambda(v: unknown): ExactFraction { const f = positive(v); if (BigInt(f.numerator) >= BigInt(f.denominator)) return bad("Cone interval requires 0 < lambda < 1."); return f; }
function cone(v: unknown): PitchCone | null {
  if (v === null) return null; const r = rec(v, "apex outwardA outwardB outerContact innerParameter outerScaleA outerScaleB coneDistanceSquared");
  return { apex: vector(r.apex), outwardA: cardinal(r.outwardA), outwardB: cardinal(r.outwardB), outerContact: vector(r.outerContact), innerParameter: lambda(r.innerParameter), outerScaleA: positive(r.outerScaleA), outerScaleB: positive(r.outerScaleB), coneDistanceSquared: positive(r.coneDistanceSquared) };
}
function contact(v: unknown): OrientedContact {
  const r = rec(v, "id kind bodyAId bodyBId storedTransfer cone");
  const result = { id: str(r.id), kind: kind(r.kind, ["ExternalSpur", "RightAngleBevel"]), bodyAId: str(r.bodyAId), bodyBId: str(r.bodyBId), storedTransfer: frac(r.storedTransfer), cone: cone(r.cone) };
  if ((result.kind === "RightAngleBevel") !== (result.cone !== null)) return bad("Contact/cone kind mismatch."); return result;
}
function keepOut(v: unknown): OrientedKeepOut { const r = rec(v, "id min max"); const min = vector(r.min), max = vector(r.max);
  if (min.some((f,i) => BigInt(f.numerator)*BigInt(max[i]!.denominator) > BigInt(max[i]!.numerator)*BigInt(f.denominator))) return bad("Unordered keep-out bounds."); return { id: str(r.id), min, max }; }
const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
function base64(v: unknown): string {
  const s = str(v, 4 * Math.ceil(L.sourceBytes / 3));
  // Avoid a pathological regex stack for large source strings; validate in one bounded pass.
  if (s.length % 4 !== 0) return bad("Invalid source base64 length.");
  const padding = s.endsWith("==") ? 2 : s.endsWith("=") ? 1 : 0;
  if (s.length / 4 * 3 - padding > L.sourceBytes) return bad("Decoded source byte limit exceeded.");
  for (let i=0; i<s.length-padding; i++) if (!alphabet.includes(s[i]!)) return bad("Invalid source base64 alphabet.");
  if (padding && (alphabet.indexOf(s[s.length-padding-1]!) & (padding === 2 ? 15 : 3)) !== 0) return bad("Noncanonical source base64 pad bits.");
  return s;
}
function placement(v: unknown): OrientedSourcePlacement | null {
  if (v === null) return null; const r = rec(v, "sourceArtifactUtf8 pose inputDofId outputDofId connectionPort");
  return { sourceArtifactUtf8: base64(r.sourceArtifactUtf8), pose: frame(r.pose), inputDofId: str(r.inputDofId), outputDofId: str(r.outputDofId), connectionPort: port(r.connectionPort) };
}
function mount(v: unknown): BevelMount { const r = rec(v, "shaft coneDirection teeth outerPitchRadiusPerTooth fixedCenter port");
  const s = shaft(r.shaft), p = port(r.port); if (p.shaftId !== s.id) return bad("Request mount port shaft reference mismatch.");
  return { shaft: s, coneDirection: cardinal(r.coneDirection), teeth: teeth(r.teeth), outerPitchRadiusPerTooth: positive(r.outerPitchRadiusPerTooth), fixedCenter: vector(r.fixedCenter), port: p }; }
function storedValidation(v: unknown): OrientedStoredValidation {
  const r = rec(v, "isValid domains diagnostics");
  const domains = array(r.domains, L.domains, v => { const r = rec(v, "domain subject verdict required detail"); return { domain: str(r.domain), subject: str(r.subject, 4096), verdict: kind(r.verdict, ["Pass", "Fail", "Inconclusive", "NotPerformed"]), required: bool(r.required), detail: str(r.detail, 4096) }; });
  const diagnostics = array(r.diagnostics, L.diagnostics, v => { const r = rec(v, "code severity message subjectId"); return { code: str(r.code), severity: kind(r.severity, ["Info", "Warning", "Error"]), message: str(r.message, 4096), subjectId: r.subjectId === null ? null : str(r.subjectId, 4096) }; });
  const isValid = bool(r.isValid);
  if (isValid !== (domains.every(d => !d.required || d.verdict === "Pass") && diagnostics.every(d => d.severity !== "Error"))) return bad("Stored validation summary contradicts stored domains/diagnostics (not a local mechanical check).");
  return { isValid, domains, diagnostics };
}
function hash(v: unknown): string { const s = str(v); if (!/^[0-9a-f]{64}$/.test(s)) return bad("Expected bare lowercase 64-hex oriented identity."); return s; }
function unique<T>(values: readonly T[], id: (v:T) => string): Set<string> { const s = new Set(values.map(id)); if (s.size !== values.length) return bad("Duplicate oriented ID."); return s; }
function reference(ids: ReadonlySet<string>, id: string): void { if (!ids.has(id)) bad(`Unknown oriented reference '${id}'.`); }


function checkGraphReferences(a: Omit<OrientedMechanismObservation,"profile"|"outputShaftId">): void {
  const shafts = unique(a.shafts, s=>s.id), bodies = unique(a.bodies,b=>b.id), ports = unique(a.ports,p=>p.id), sources = unique(a.sources,s=>s.moduleId), states = unique(a.solution.states,s=>s.dofId);
  unique(a.contacts,c=>c.id); unique(a.connections,c=>c.id); unique(a.sourceMappings,s=>JSON.stringify([s.moduleId,s.sourceDofId])); unique(a.keepOuts,k=>k.id);
  reference(shafts,a.rootShaftId);
  if (a.shafts.filter(s=>s.isPrescribed).length !== 1 || !a.shafts.find(s=>s.id===a.rootShaftId)!.isPrescribed || a.solution.rootDofId !== a.rootShaftId) return bad("Expected one prescribed global root with matching solution.");
  for (const s of states) reference(shafts,s); for (const s of shafts) reference(states,s);
  const root = a.solution.states.find(s=>s.dofId===a.rootShaftId)!;
  if (root.coefficient.numerator !== "1" || root.coefficient.denominator !== "1") return bad("Root channel must be identity.");
  if (!a.bodies.length) return bad("Empty body collection.");
  for (const b of a.bodies) { reference(shafts,b.shaftId); if (b.sourceModuleId !== "bevel") reference(sources,b.sourceModuleId); }
  for (const c of a.contacts) { reference(bodies,c.bodyAId); reference(bodies,c.bodyBId); if (c.bodyAId===c.bodyBId) return bad("Self contact."); }
  for (const p of a.ports) reference(shafts,p.shaftId);
  for (const c of a.connections) { reference(ports,c.portAId); reference(ports,c.portBId); if(c.portAId===c.portBId) return bad("Self connection."); }
  for (const s of a.sourceMappings) { reference(sources,s.moduleId); reference(shafts,s.shaftId); }
  const bevels = a.contacts.filter(c=>c.kind==="RightAngleBevel");
  if (bevels.length !== 1 || a.bodies.filter(b=>b.kind==="RightAngleBevel").length !== 2) throw new UnsupportedArtifactError("Expected exactly one bounded bevel pair.");
  for (const b of a.bodies) if (b.kind==="RightAngleBevel" && !bevels.some(c=>c.bodyAId===b.id||c.bodyBId===b.id)) return bad("Missing body cone reference.");
}
return {bad,rec,str,kind,bool,array,frac,positive,phase,optional,teeth,vector,cardinal,frame,shaft,port,body,lambda,cone,contact,keepOut,base64,placement,mount,storedValidation,hash,unique,reference,checkGraphReferences};
}

/** Input has already passed bounded base64 validation. Returns fresh provenance bytes. */
export function decodeOrientedSource(s:string):Uint8Array {
  const alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  const pad=s.endsWith("==")?2:s.endsWith("=")?1:0,bytes=new Uint8Array(s.length/4*3-pad);
  let out=0;
  for(let i=0;i<s.length;i+=4){const n=(alphabet.indexOf(s[i]!)<<18)|(alphabet.indexOf(s[i+1]!)<<12)|((alphabet.indexOf(s[i+2]!)&63)<<6)|(alphabet.indexOf(s[i+3]!)&63);
    for(const shift of [16,8,0])if(out<bytes.length)bytes[out++]=(n>>>shift)&255;}
  return bytes;
}
