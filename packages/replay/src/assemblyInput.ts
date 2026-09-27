import { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";
import { ORIENTED_LIMITS, assertOrientedJson, readOrientedJsonText } from "./orientedJson.js";
import { assemblyFraction, compareFraction, applyAffine } from "./assemblyExact.js";
import { decodeBase64, decodeUtf8, encodeUtf8 } from "./assemblyBytes.js";
import { deepFreeze } from "./immutable.js";
import type { AssemblyReplayPayload, AssemblyReference, AssemblyFixedFrame } from "./assemblyTypes.js";

export const ASSEMBLY_LIMITS = Object.freeze({ documentBytes: 33554432, payloadBytes: 25165824, sourceBytes: 8388608,
  depth: 64, nodes: 1048576, properties: 256, stringCharacters: 4096, idCharacters: 256,
  inputIntegerCharacters: 128, derivedIntegerCharacters: 1024, members: 8, inventory: 8192,
  shafts: 256, bodies: 256, features: 4096, samples: 512, sampleChannelProduct: 131072,
  geometryVertices: 262144, geometryIndices: 786432, maximumTotalWork: 32000000,
  instances: 32, expandedInventory: 65536, externalBindings: 8192, displayCoordinateMm: 1e8 });
export function assemblyNeed(ok: unknown, detail: string): asserts ok { if (!ok) throw new ArtifactInputError(detail); }
export function assemblyRecord(v: unknown, fields?: string): Record<string, unknown> {
  assemblyNeed(v && typeof v === "object" && !Array.isArray(v), "Replay object required.");
  const r = v as Record<string, unknown>;
  if (fields !== undefined) assemblyNeed(Object.keys(r).sort().join() === fields.split(" ").sort().join(), "Missing or unknown assembly replay fields.");
  return r;
}
export function assemblyString(v: unknown, max = 256): string {
  assemblyNeed(typeof v === "string" && v.length > 0 && v.length <= max && v.trim() === v, "Bounded nonempty string required."); return v;
}
function hash(v: unknown): string { const s = assemblyString(v); assemblyNeed(/^[0-9a-f]{64}$/.test(s), "Lowercase SHA-256 identity required."); return s; }
function array(v: unknown, max: number): unknown[] { assemblyNeed(Array.isArray(v) && v.length <= max, "Array/resource bound exceeded."); return v; }
function bool(v: unknown): boolean { assemblyNeed(typeof v === "boolean", "Boolean required."); return v; }
function integer(v: unknown, max: number): number { assemblyNeed(typeof v === "number" && Number.isSafeInteger(v) && v >= 0 && v <= max, "Bounded nonnegative integer required."); return v; }
export function assemblyReferenceKey(v: AssemblyReference): string { return JSON.stringify([v.owner, v.memberId, v.kind, v.localId]); }
function reference(v: unknown): AssemblyReference {
  const r = assemblyRecord(v, "owner memberId kind localId");
  assemblyNeed(r.owner === "Root" && r.memberId === null || r.owner === "Member" && typeof r.memberId === "string", "Invalid reference owner.");
  if (r.memberId !== null) assemblyString(r.memberId);
  assemblyNeed(["Shaft", "Body", "Port", "Output", "Feature", "LinearDof", "Constraint"].includes(assemblyString(r.kind)), "Unknown reference kind.");
  assemblyString(r.localId); return r as unknown as AssemblyReference;
}
function allValues(v: unknown, name = ""): void {
  if (typeof v === "string") {
    const max = name === "payloadUtf8" ? 4 * Math.ceil(ASSEMBLY_LIMITS.payloadBytes / 3) : name.endsWith("Utf8") ? 4 * Math.ceil(ASSEMBLY_LIMITS.sourceBytes / 3) : 4096;
    assemblyNeed(v.length <= max, "Replay string bound exceeded.");
    for (const c of v) { const n = c.codePointAt(0)!; assemblyNeed(n < 0xd800 || n > 0xdfff, "Lone surrogate is not canonical Unicode."); }
  } else if (Array.isArray(v)) for (const child of v) allValues(child, name);
  else if (v && typeof v === "object") {
    const r = v as Record<string, unknown>;
    if (Object.hasOwn(r, "numerator") || Object.hasOwn(r, "denominator")) assemblyFraction(r, 1024);
    if (r.representation === "certified-rational-enclosure") {
      const lo=assemblyFraction(r.lower,1024),hi=assemblyFraction(r.upper,1024),w=assemblyFraction(r.width,1024);
      const n=BigInt(hi.numerator)*BigInt(lo.denominator)-BigInt(lo.numerator)*BigInt(hi.denominator),d=BigInt(hi.denominator)*BigInt(lo.denominator);
      assemblyNeed(n>=0n && BigInt(w.numerator)*d===n*BigInt(w.denominator) && r.isExact===(n===0n) && r.boundary==="closed","Inconsistent stored interval metadata.");
    }
    for (const [k, x] of Object.entries(r)) allValues(x, k);
  }
}
function parse(text: string, maximum: number): Record<string, unknown> {
  const limits = { ...ORIENTED_LIMITS, ...ASSEMBLY_LIMITS, documentBytes: maximum, sourceBytes: ASSEMBLY_LIMITS.payloadBytes };
  const value = readOrientedJsonText(text, limits); assertOrientedJson(value, limits); allValues(value); return assemblyRecord(value);
}
function canonical(v: unknown): string {
  if (Array.isArray(v)) return "[" + v.map(canonical).join(",") + "]";
  if (v && typeof v === "object") return "{" + Object.keys(v).sort().map(k => JSON.stringify(k) + ":" + canonical((v as Record<string, unknown>)[k])).join(",") + "}";
  return JSON.stringify(v);
}
function vector(v: unknown, cardinal = false): number[] {
  const a = array(v, 3); assemblyNeed(a.length === 3, "Vector requires three fractions.");
  const fs = a.map(x => assemblyFraction(x));
  if (cardinal) assemblyNeed(fs.every(f => f.denominator === "1" && ["-1", "0", "1"].includes(f.numerator)) && fs.filter(f => f.numerator !== "0").length === 1, "Proper cardinal axis required.");
  return fs.map(f => Number(f.numerator) / Number(f.denominator));
}
function frame(v: unknown): AssemblyFixedFrame {
  const r = assemblyRecord(v, "origin x y z"), x = vector(r.x, true), y = vector(r.y, true), z = vector(r.z, true);
  vector(r.origin);
  assemblyNeed(x[0]! * y[0]! + x[1]! * y[1]! + x[2]! * y[2]! === 0 &&
    [x[1]! * y[2]! - x[2]! * y[1]!, x[2]! * y[0]! - x[0]! * y[2]!, x[0]! * y[1]! - x[1]! * y[0]!].every((v,i) => v === z[i]), "Reflected/nonorthogonal frame.");
  return r as unknown as AssemblyFixedFrame;
}
export function assemblyMatrix(v: unknown): readonly number[] {
  const a = array(v, 16); assemblyNeed(a.length === 16 && a.every(n => typeof n === "number" && Number.isFinite(n) && Math.abs(n) <= 1e8), "Bounded 4x4 matrix required.");
  const m = a as number[];
  assemblyNeed(m[3] === 0 && m[7] === 0 && m[11] === 0 && m[15] === 1, "Affine homogeneous matrix required.");
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) {
    let dot = 0; for (let k = 0; k < 3; k++) dot += m[4*i+k]! * m[4*j+k]!;
    assemblyNeed(Math.abs(dot - (i === j ? 1 : 0)) <= 1e-10, "Body frame must be rigid.");
  }
  const det = m[0]!*(m[5]!*m[10]!-m[9]!*m[6]!)-m[4]!*(m[1]!*m[10]!-m[9]!*m[2]!)+m[8]!*(m[1]!*m[6]!-m[5]!*m[2]!);
  assemblyNeed(Math.abs(det-1) <= 1e-10, "Body frame cannot reflect."); return m;
}
export interface ParsedAssembly { readonly bytes: Uint8Array; readonly payloadBytes: Uint8Array; readonly sourceBytes: Uint8Array; readonly replayId: string; readonly payload: AssemblyReplayPayload }
export function parseAssemblyReplay(input: string | Uint8Array): ParsedAssembly {
  assemblyNeed(typeof input === "string" || input instanceof Uint8Array, "Text or UTF-8 bytes required.");
  assemblyNeed(typeof input === "string" || input.byteLength <= ASSEMBLY_LIMITS.documentBytes, "Document byte bound exceeded before copy.");
  const bytes = typeof input === "string" ? encodeUtf8(input, ASSEMBLY_LIMITS.documentBytes) : new Uint8Array(input);
  const text = decodeUtf8(bytes, ASSEMBLY_LIMITS.documentBytes), outer = parse(text, ASSEMBLY_LIMITS.documentBytes);
  assemblyRecord(outer, "format formatVersion replayId payloadUtf8");
  if (outer.format !== "gear-invest.assembly-replay" || outer.formatVersion !== "0.1") throw new UnsupportedArtifactError("Unsupported assembly replay format/version.");
  const replayId = hash(outer.replayId), payloadBytes = decodeBase64(outer.payloadUtf8, ASSEMBLY_LIMITS.payloadBytes);
  const p = parse(decodeUtf8(payloadBytes, ASSEMBLY_LIMITS.payloadBytes), ASSEMBLY_LIMITS.payloadBytes);
  assemblyRecord(p, "profile source request producer definition analysis shafts bodies staticFeatures samples");
  if(p.profile !== "resolved-affine-and-sampled-assembly-v1") throw new UnsupportedArtifactError("Unsupported assembly replay profile.");
  const s = assemblyRecord(p.source, "format formatVersion profile artifactId definitionId draftId analysisId rawSha256 artifactUtf8");
  for (const k of ["artifactId", "definitionId", "draftId", "analysisId", "rawSha256"]) hash(s[k]);
  const sourceBytes = decodeBase64(s.artifactUtf8, ASSEMBLY_LIMITS.sourceBytes), original = parse(decodeUtf8(sourceBytes, ASSEMBLY_LIMITS.sourceBytes), ASSEMBLY_LIMITS.sourceBytes);
  const profiles = new Map([["0.1", "single-root-affine-prefix-terminal-device-assembly-v1"], ["0.2", "single-root-geneva-affine-suffix-assembly-v1"]]);
  assemblyNeed(s.format === "gear-invest.mechanical-assembly" && original.format === s.format && s.formatVersion === original.formatVersion
    && profiles.get(String(s.formatVersion)) === s.profile && original.profile === s.profile, "Source format/profile mismatch.");
  assemblyNeed(original.artifactHash === s.artifactId && original.candidateId === s.definitionId, "Source identity mismatch.");
  const a = assemblyRecord(p.analysis), d = assemblyRecord(p.definition);
  assemblyNeed(a.analysisId === s.analysisId && a.definitionId === s.definitionId && a.draftId === s.draftId && d.profile === s.profile, "Analysis/definition identity mismatch.");
  assemblyNeed(canonical(a) === canonical(original.analysis) && canonical(d) === canonical(assemblyRecord(assemblyRecord(assemblyRecord(original.request).draft).definition)), "Copied source tables differ from original artifact.");
  const producer = assemblyRecord(p.producer, "operation mechanicalValidation exporterVersion packageVersion policy numericPolicy toothSolidValidation");
  assemblyNeed(producer.exporterVersion === "assembly-web-export/0.1", "Unsupported exporter version."); assemblyString(producer.packageVersion,4096);
  assemblyNeed(producer.operation === "current-source-finalize-and-evaluate" && producer.mechanicalValidation === "Finalized" && producer.toothSolidValidation === "notPerformed" && producer.policy === a.policy
    && producer.numericPolicy === assemblyRecord(a.numericRequest).policy, "Invalid stored producer assertion.");
  const request = assemblyRecord(p.request, "sampleRoots includeExactGenevaBoundaries maximumTotalWork"); bool(request.includeExactGenevaBoundaries); integer(request.maximumTotalWork, ASSEMBLY_LIMITS.maximumTotalWork);
  const roots = array(request.sampleRoots, 512).map(x => assemblyFraction(x)); assemblyNeed(roots.length > 0, "Sample roots required.");
  roots.forEach((r,i) => assemblyNeed(!i || compareFraction(roots[i-1]!,r) < 0, "Requested roots must be strictly increasing."));
  const memberIds = new Set<string>();
  for (const m of array(d.members, 8)) {
    const member=assemblyRecord(m),id = assemblyString(member.instanceId);
    if(!["WormDrive","OpenBelt","PitchChain","Geneva","CrankSlider","CamFollower"].includes(String(assemblyRecord(member.declaration).kind))) throw new UnsupportedArtifactError("Unsupported assembly member family.");
    assemblyNeed(!memberIds.has(id), "Duplicate member ID."); memberIds.add(id);
  }
  const inventory = array(a.inventory, 8192), references = new Map<string, Record<string, unknown>>();
  const known = (v: unknown, kind?: string): AssemblyReference => { const r = reference(v); assemblyNeed(references.has(assemblyReferenceKey(r)) && (!kind || r.kind === kind), "Unknown/cross-owner/kind reference."); return r; };
  for (const entry of inventory) {
    const r = assemblyRecord(entry, "reference role mountedShaft isPrescribed isNonlinearDependent"), ref = reference(r.reference), key = assemblyReferenceKey(ref);
    assemblyNeed(!references.has(key) && (ref.owner === "Root" || memberIds.has(ref.memberId!)), "Duplicate/unknown owner inventory."); references.set(key,r);
    assemblyString(r.role); bool(r.isPrescribed); bool(r.isNonlinearDependent);
  }
  for (const r of references.values()) if (r.mountedShaft !== null) known(r.mountedShaft, "Shaft");
  const shafts = array(p.shafts, 256), shaftKeys = new Set<string>();
  for (const shaft of shafts) {
    const r = assemblyRecord(shaft, "reference mode unit q p fixedFrameMm"), key = assemblyReferenceKey(known(r.reference,"Shaft"));
    assemblyNeed(!shaftKeys.has(key) && r.mode === "exactAffine" && r.unit === "turn", "Duplicate/unsupported shaft."); shaftKeys.add(key);
    assemblyFraction(r.q); assemblyFraction(r.p); frame(r.fixedFrameMm);
  }
  const sourceShafts = array(assemblyRecord(original.referenceEvaluation).shafts, 256);
  assemblyNeed(sourceShafts.length === shafts.length, "Affine shaft inventory differs from source.");
  for (const raw of sourceShafts) {
    const rawShaft = assemblyRecord(raw), key = assemblyReferenceKey(reference(rawShaft.reference)), exported = shafts.map(x => assemblyRecord(x)).find(x => assemblyReferenceKey(reference(x.reference)) === key);
    const originalFrame = assemblyRecord(rawShaft.fixedFrameMm), projected: Record<string,unknown> = {};
    for (const k of ["origin","x","y","z"]) { const v = assemblyRecord(originalFrame[k]); projected[k] = [v.x,v.y,v.z]; }
    assemblyNeed(exported && canonical(exported.fixedFrameMm) === canonical(projected), "Shaft frame differs from source.");
    const relation = assemblyRecord(rawShaft.relation);
    assemblyNeed(canonical(exported.q) === canonical(relation.coefficient) && canonical(exported.p) === canonical(relation.phase), "Affine relation differs from source.");
  }
  const bodies = array(p.bodies, 256), bodyKeys = new Set<string>();
  for (const body of bodies) {
    const r = assemblyRecord(body, "reference mountedShaft mode q p fixedFrameMm positiveAxis specification"), key = assemblyReferenceKey(known(r.reference,"Body"));
    assemblyNeed(!bodyKeys.has(key), "Duplicate body identity."); bodyKeys.add(key);
    assemblyNeed(canonical(r.mountedShaft) === canonical(references.get(key)!.mountedShaft), "Body mounting ownership differs from inventory.");
    if (r.mountedShaft !== null) known(r.mountedShaft, "Shaft");
    if (r.mode === "exactAffine") { assemblyFraction(r.q); assemblyFraction(r.p); frame(r.fixedFrameMm); vector(r.positiveAxis,true); }
    else { assemblyNeed(r.mode === "sampled" && r.q === null && r.p === null, "Unknown/inconsistent body channel."); if (r.fixedFrameMm !== null) frame(r.fixedFrameMm); vector(r.positiveAxis); }
    const spec = assemblyRecord(r.specification,"family teeth pitchRadiusMm moduleMm missingSpecification toothSolidValidation"); assemblyString(spec.family);
    if (spec.teeth !== null) assemblyNeed(integer(spec.teeth, 2147483647) > 0, "Positive teeth required.");
    for (const field of [spec.pitchRadiusMm,spec.moduleMm]) if (field !== null) assemblyNeed(BigInt(assemblyFraction(field).numerator) > 0n,"Positive dimension required.");
    assemblyNeed(spec.missingSpecification === "notSpecified" && spec.toothSolidValidation === "notPerformed", "Unsupported geometry proof claim.");
  }
  assemblyNeed([...references].filter(([,v]) => reference(v.reference).kind === "Body").every(([key]) => bodyKeys.has(key)), "Whole body inventory required.");
  const samples = array(p.samples,512); assemblyNeed(samples.length > 0 && samples.length * Math.max(1, inventory.length) <= ASSEMBLY_LIMITS.sampleChannelProduct, "Sample/inventory product exceeded.");
  let previous = roots[0]!, vertices = 0; const sampled = new Set<string>();
  function features(value: unknown, isStatic: boolean): void {
    const featureIds = new Set<string>();
    for (const raw of array(value,4096)) {
      const feature = assemblyRecord(raw,"memberId localId mechanicalReference scope role closed pointsMm"), memberId = assemblyString(feature.memberId), localId = assemblyString(feature.localId);
      const ref = known(feature.mechanicalReference); const key = JSON.stringify([memberId,localId]);
      assemblyNeed(memberIds.has(memberId) && ref.memberId === memberId && !featureIds.has(key) && feature.scope === "displayApproximation", "Stale/duplicate/cross-owner display feature."); featureIds.add(key); assemblyString(feature.role); bool(feature.closed);
      assemblyNeed((feature.role === "fixedRoute") === isStatic,"Static/dynamic feature storage mismatch.");
      for (const point of array(feature.pointsMm,262144)) { const xyz = array(point,3); assemblyNeed(xyz.length === 3 && xyz.every(n => typeof n === "number" && Number.isFinite(n) && Math.abs(n) <= 1e8), "Invalid display point."); vertices++; }
      assemblyNeed(vertices <= ASSEMBLY_LIMITS.geometryVertices,"Whole-document geometry budget exceeded.");
    }
  }
  features(p.staticFeatures,true);
  const work=(value:unknown):number=>{assemblyNeed(typeof value==="string" && /^(0|[1-9][0-9]{0,7})$/.test(value),"Bounded work count required.");return integer(Number(value),Number(request.maximumTotalWork));};
  let totalWork=work(a.numericWork);
  for (const [i, sample] of samples.entries()) {
    const r = assemblyRecord(sample,"root status exactBoundaries originalEvaluationSha256 observation bodyFrames displayFeatures"), root = assemblyFraction(r.root);
    assemblyNeed((!i || compareFraction(previous,root) < 0) && compareFraction(roots[0]!,root) <= 0 && compareFraction(root,roots.at(-1)!) <= 0, "Sample order/range invalid."); previous = root; sampled.add(canonical(root)); hash(r.originalEvaluationSha256);
    const obs = assemblyRecord(r.observation), rootInput = assemblyRecord(obs.rootInput);
    totalWork+=work(obs.numericWork);assemblyNeed(totalWork<=Number(request.maximumTotalWork),"Stored sample work budget exceeded.");
    assemblyNeed(obs.format === "gear-invest.assembly-replay-observation" && obs.formatVersion === "0.1" && obs.analysisId === s.analysisId && canonical(rootInput.value) === canonical(root) && rootInput.kind === "AngularPosition" && rootInput.unit === "turn", "Mixed sample source/root.");
    assemblyNeed(r.status === (bool(obs.allRequestedNumericAvailable) && array(obs.diagnostics,8192).length === 0 ? "storedAvailable" : "storedPartial"), "Sample summary contradicts stored status.");
    const members = array(obs.members,8).map(x => assemblyRecord(x)), ids = members.map(m => assemblyString(m.instanceId));
    assemblyNeed(new Set(ids).size === memberIds.size && ids.length === memberIds.size && ids.every(id => memberIds.has(id)),"Sample member inventory mismatch.");
    for (const m of members) { bool(m.numericAvailable); bool(m.displayAvailable); assemblyString(m.status); }
    const sampleShafts = array(obs.shafts,256).map(x => assemblyRecord(x)); assemblyNeed(sampleShafts.length === shafts.length,"Sample shaft coverage mismatch."); const seenShafts = new Set<string>();
    for (const ss of sampleShafts) {
      const key = assemblyReferenceKey(known(ss.reference,"Shaft")), law = shafts.map(x => assemblyRecord(x)).find(x => assemblyReferenceKey(reference(x.reference)) === key);
      assemblyNeed(law && !seenShafts.has(key),"Duplicate or unsolved sample shaft."); seenShafts.add(key);
      assemblyNeed(canonical(applyAffine(assemblyFraction(law.q),assemblyFraction(law.p),root)) === canonical(assemblyRecord(ss.turns).value),"Sample shaft disagrees with exact resolved relation.");
    }
    const seen = new Set<string>();
    for (const raw of array(r.bodyFrames,256)) {
      const pose = assemblyRecord(raw,"reference status reason matrixMm"), key = assemblyReferenceKey(known(pose.reference,"Body"));
      assemblyNeed(bodyKeys.has(key) && !seen.has(key), "Duplicate/missing sampled body identity."); seen.add(key);
      if (pose.status === "displayApproximation") { assemblyNeed(pose.reason === null,"Available pose cannot hide failure."); assemblyMatrix(pose.matrixMm); }
      else { assemblyNeed(pose.status === "unavailable" && pose.matrixMm === null,"Unsupported pose status."); assemblyString(pose.reason,4096); }
    }
    assemblyNeed(seen.size === bodyKeys.size, "Sample drops body entries.");
    features(r.displayFeatures,false);
    const boundaries = new Set<string>();
    for (const raw of array(r.exactBoundaries,8)) {
      const b = assemblyRecord(raw,"memberId regime recipeId"), memberId = assemblyString(b.memberId), recipe = assemblyRecord(members.find(m => m.instanceId === memberId)?.recipe);
      assemblyNeed(!boundaries.has(memberId) && ["LowerPhaseBoundary","UpperPhaseBoundary"].includes(String(b.regime)) && b.recipeId === recipe.recipeId && b.regime === recipe.regime, "Unproved exact boundary label."); boundaries.add(memberId); hash(b.recipeId);
    }
  }
  assemblyNeed(roots.every(r => sampled.has(canonical(r))), "Requested sample missing.");
  return { bytes, payloadBytes, sourceBytes, replayId, payload: deepFreeze(p as unknown as AssemblyReplayPayload) };
}
