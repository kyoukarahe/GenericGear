import { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";
import { ORIENTED_LIMITS, assertOrientedJson, readOrientedJsonText } from "./orientedJson.js";
import { assemblyFraction } from "./assemblyExact.js";
import { decodeBase64, decodeUtf8, encodeUtf8 } from "./assemblyBytes.js";
import { deepFreeze } from "./immutable.js";
import type { CarrierPayload } from "./carrierTypes.js";

export const CARRIER_LIMITS = Object.freeze({ documentBytes: 8388608, nodes: 131072, depth: 40, shafts: 4, bodies: 5, poseNodes: 16, instances: 32, inputDigits: 128, derivedDigits: 1024 });
export function carrierNeed(ok: unknown, message: string): asserts ok { if (!ok) throw new ArtifactInputError(message); }
function record(v: unknown, fields?: string): Record<string, unknown> {
  carrierNeed(v && typeof v === "object" && !Array.isArray(v), "Carrier object required.");
  if (fields) carrierNeed(Object.keys(v).sort().join() === fields.split(" ").sort().join(), "Unknown/missing carrier fields.");
  return v as Record<string, unknown>;
}
export function carrierId(v: unknown): string {
  carrierNeed(typeof v === "string" && v.length > 0 && v.trim().length > 0 && v.length <= 256 && !/[\u0000-\u001f\u007f-\u009f]/.test(v), "Bounded carrier ID required."); return v;
}
function hash(v: unknown): string { const s = carrierId(v); carrierNeed(/^[a-f0-9]{64}$/.test(s), "SHA-256 required."); return s; }
function array(v: unknown, max: number): unknown[] { carrierNeed(Array.isArray(v) && v.length <= max, "Carrier array limit exceeded."); return v; }
function law(v: unknown): void { const r = record(v, "q p"); assemblyFraction(r.q,1024); assemblyFraction(r.p,1024); }
function vector(v: unknown, cardinal = false): number[] {
  const a = array(v,3); carrierNeed(a.length === 3, "Three vector components required."); const f = a.map(x => assemblyFraction(x,1024));
  if(cardinal) carrierNeed(f.every(x => x.denominator === "1" && ["-1","0","1"].includes(x.numerator)) && f.filter(x => x.numerator !== "0").length === 1, "Cardinal basis required.");
  return f.map(x => Number(x.numerator)/Number(x.denominator));
}
function frame(v: unknown): void {
  const f = record(v,"origin x y z"), x=vector(f.x,true),y=vector(f.y,true),z=vector(f.z,true); vector(f.origin);
  const cross=[x[1]!*y[2]!-x[2]!*y[1]!,x[2]!*y[0]!-x[0]!*y[2]!,x[0]!*y[1]!-x[1]!*y[0]!];
  carrierNeed(x.reduce((s,v,i)=>s+v*y[i]!,0)===0 && cross.every((v,i)=>v===z[i]),"Proper cardinal frame required.");
}
function parse(bytes: Uint8Array): Record<string,unknown> {
  const limits={...ORIENTED_LIMITS,...CARRIER_LIMITS,sourceBytes:CARRIER_LIMITS.documentBytes};
  const value=readOrientedJsonText(decodeUtf8(bytes,CARRIER_LIMITS.documentBytes),limits); assertOrientedJson(value,limits); return record(value);
}
function canonical(v: unknown): string {
  if(Array.isArray(v)) return "["+v.map(canonical).join(",")+"]";
  if(v && typeof v === "object") return "{"+Object.keys(v).sort().map(k=>JSON.stringify(k)+":"+canonical((v as Record<string,unknown>)[k])).join(",")+"}";
  return JSON.stringify(v);
}
export interface ParsedCarrier { readonly bytes: Uint8Array; readonly payloadBytes: Uint8Array; readonly sourceBytes: Uint8Array; readonly replayId: string; readonly payload: CarrierPayload }
export function parseCarrier(input: string | Uint8Array): ParsedCarrier {
  carrierNeed(typeof input === "string" || input instanceof Uint8Array,"UTF-8 carrier replay required.");
  carrierNeed(typeof input === "string" || input.length <= CARRIER_LIMITS.documentBytes,"Carrier byte bound exceeded before copy.");
  // Buffer is a Uint8Array subclass whose slice() aliases memory. Always mint a plain owned copy.
  const bytes=typeof input === "string" ? encodeUtf8(input,CARRIER_LIMITS.documentBytes) : new Uint8Array(input), outer=parse(bytes);
  record(outer,"format formatVersion replayId payloadUtf8");
  if(outer.format !== "gear-invest.carrier-replay" || outer.formatVersion !== "0.1") throw new UnsupportedArtifactError("Unsupported carrier replay format/version.");
  const replayId=hash(outer.replayId),payloadBytes=decodeBase64(outer.payloadUtf8,CARRIER_LIMITS.documentBytes),p=parse(payloadBytes);
  record(p,"profile artifactId definitionId sourceRawSha256 sourceArtifactUtf8 compiled");
  if(p.profile !== "parallel-grounded-sun-single-planet-v1") throw new UnsupportedArtifactError("Unsupported carrier profile.");
  hash(p.artifactId);hash(p.definitionId);hash(p.sourceRawSha256);
  const sourceBytes=decodeBase64(p.sourceArtifactUtf8,CARRIER_LIMITS.documentBytes),s=parse(sourceBytes);
  record(s,"format formatVersion profile definitionId artifactHash request attachedSource compiled");
  carrierNeed(s.format === "gear-invest.carrier-mechanism" && s.formatVersion === "0.1" && s.profile === p.profile && s.artifactHash === p.artifactId && s.definitionId === p.definitionId,"Carrier source identity association differs.");
  carrierNeed(record(s.request).definitionId === p.definitionId && canonical(p.compiled) === canonical(s.compiled),"Stored compiled/source correspondence differs (not a mechanical rebuild).");
  const c=record(p.compiled,"policy rootShaftId carrierShaftId sunShaftId planetShaftId portId isValid checks diagnostics carrierCommon planetCommon portReadout shafts poseNodes");
  carrierNeed(c.policy === "exact-carrier-contact-and-pitch-planes-v1" && c.isValid === true,"Unsupported producer policy or unfinalized carrier.");
  for(const key of ["rootShaftId","carrierShaftId","sunShaftId","planetShaftId","portId"]) carrierId(c[key]);
  const domains=new Set<string>();
  for(const x of array(c.checks,32)) {
    const d=record(x,"domain verdict required detail"); const name=carrierId(d.domain); carrierNeed(!domains.has(name),"Duplicate validation scope.");domains.add(name);
    carrierNeed(["Pass","Fail","NotPerformed","Inconclusive"].includes(String(d.verdict)) && typeof d.required === "boolean" && typeof d.detail === "string" && d.detail.length<=4096 && (!d.required || d.verdict === "Pass"),"Failed required producer scope.");
  }
  for(const name of ["Ownership","SourceMechanics","UnitsAndFrames","ContactAndReference","Determinacy","PitchPlanes"])
    carrierNeed(array(c.checks,32).some(x=>{const r=record(x);return r.domain===name && r.required===true && r.verdict==="Pass";}),"Missing required producer scope.");
  carrierNeed(array(c.diagnostics,0).length === 0,"Finalized carrier cannot have errors.");law(c.carrierCommon);law(c.planetCommon);law(c.portReadout);
  const shaftIds=new Set<string>(), shaftPose=new Map<string,string>(), orbitIds:string[]=[];
  for(const x of array(c.shafts,4)) {
    const sh=record(x,"id motion poseNodeId world carrierRelative"),id=carrierId(sh.id);carrierNeed(!shaftIds.has(id),"Duplicate shaft.");shaftIds.add(id);shaftPose.set(id,carrierId(sh.poseNodeId));law(sh.world);
    carrierNeed(["FixedAxis","GroundHeld","Orbiting"].includes(String(sh.motion)),"Unknown shaft motion.");
    if(sh.motion === "Orbiting") { law(sh.carrierRelative);orbitIds.push(id); } else carrierNeed(sh.carrierRelative === null,"Fixed/held shaft is not carrier-relative.");
  }
  for(const id of [c.rootShaftId,c.carrierShaftId,c.sunShaftId,c.planetShaftId]) carrierNeed(shaftIds.has(String(id)),"Missing actual shaft.");
  for(const x of array(c.shafts,4)) {const sh=record(x);
    carrierNeed(sh.motion===(sh.id===c.sunShaftId?"GroundHeld":sh.id===c.planetShaftId?"Orbiting":"FixedAxis"),"Unexpected shaft motion/owner.");
    if(sh.motion==="GroundHeld")carrierNeed(record(record(sh.world).q).numerator==="0","Held shaft has a nonzero rate.");
  }
  carrierNeed(orbitIds.length===1 && orbitIds[0]===c.planetShaftId && c.sunShaftId!==c.planetShaftId && c.carrierShaftId!==c.sunShaftId && c.carrierShaftId!==c.planetShaftId,"Invalid carrier ownership.");
  const nodes=new Map<string,Record<string,unknown>>(),bodyIds=new Set<string>(),depths=new Map<string,number>();
  for(const x of array(c.poseNodes,16)) {
    const n=record(x,"id parentId frame rotation shaftId bodyId teeth pitchRadiusMm"),id=carrierId(n.id),parent=n.parentId;
    carrierNeed(!nodes.has(id) && (parent===null || typeof parent === "string" && nodes.has(parent)),"Duplicate, cyclic or non-topological frame parent.");
    const depth=parent===null?0:depths.get(parent as string)!+1;carrierNeed(depth<=3,"Frame depth exceeded.");depths.set(id,depth);nodes.set(id,n);
    carrierNeed(shaftIds.has(carrierId(n.shaftId)),"Pose has unknown owner.");frame(n.frame);law(n.rotation);
    if(n.bodyId!==null) { const b=carrierId(n.bodyId);carrierNeed(!bodyIds.has(b),"Duplicate body.");bodyIds.add(b); }
    carrierNeed((n.teeth===null)===(n.pitchRadiusMm===null),"Pitch metadata must be paired.");
    if(n.teeth!==null) {carrierNeed(n.bodyId!==null && typeof n.teeth==="string" && /^[1-9][0-9]{0,3}$/.test(n.teeth) && Number(n.teeth)<=4096,"Invalid teeth.");carrierNeed(BigInt(assemblyFraction(n.pitchRadiusMm,1024).numerator)>0n,"Positive pitch radius required.");}
  }
  carrierNeed(bodyIds.size>=3 && bodyIds.size<=5,"Carrier body bound.");
  for(const [id,nodeId] of shaftPose) carrierNeed(nodes.get(nodeId)?.shaftId===id,"Shaft pose ownership differs.");
  carrierNeed(nodes.get("carrier-frame")?.parentId===null && nodes.get("carrier-frame")?.shaftId===c.carrierShaftId &&
    nodes.get("planet-shaft")?.parentId==="carrier-frame" && nodes.get("planet-shaft")?.shaftId===c.planetShaftId &&
    nodes.get("planet-body")?.parentId==="planet-shaft" && nodes.get("sun-shaft")?.parentId===null &&
    nodes.get("sun-body")?.parentId==="sun-shaft" && nodes.get("carrier-body")?.parentId==="carrier-frame" && nodes.get("output-port")?.parentId==="planet-shaft","Invalid declared carrier frame recipe.");
  for(const [node,owner] of [["planet-body",c.planetShaftId],["sun-shaft",c.sunShaftId],["sun-body",c.sunShaftId],["carrier-body",c.carrierShaftId],["output-port",c.planetShaftId]])
    carrierNeed(nodes.get(String(node))?.shaftId===owner,"Body/port frame owner differs.");
  return {bytes,payloadBytes,sourceBytes,replayId,payload:deepFreeze(p) as unknown as CarrierPayload};
}
