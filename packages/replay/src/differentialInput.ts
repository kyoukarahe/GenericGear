import { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";
import { ORIENTED_LIMITS, assertOrientedJson, readOrientedJsonText } from "./orientedJson.js";
import { assemblyFraction } from "./assemblyExact.js";
import { decodeBase64, decodeUtf8, encodeUtf8 } from "./assemblyBytes.js";
import { deepFreeze } from "./immutable.js";
import type { DifferentialPayload, DifferentialCompiled } from "./differentialTypes.js";

export const DIFFERENTIAL_LIMITS=Object.freeze({documentBytes:8388608,nodes:131072,depth:40,coordinates:4,inputs:2,ports:6,conditions:6,rows:16,poseNodes:16,bodies:5,instances:32,inputDigits:128,derivedDigits:1024});
const limits={...ORIENTED_LIMITS,...DIFFERENTIAL_LIMITS,sourceBytes:8388608};
export function need(ok:unknown,message:string):asserts ok {if(!ok)throw new ArtifactInputError(message);}
function rec(v:unknown,fields?:string):Record<string,unknown> {
  need(v&&typeof v==="object"&&!Array.isArray(v),"Differential object required.");
  if(fields)need(Object.keys(v).sort().join()===fields.split(" ").sort().join(),"Unknown/missing differential fields.");return v as Record<string,unknown>;
}
export function differentialId(v:unknown):string {need(typeof v==="string"&&v.length>0&&v.trim().length>0&&v.length<=160&&!/[\u0000-\u001f\u007f-\u009f]/.test(v),"Bounded differential ID required.");return v;}
function derivedId(v:unknown):string {need(typeof v==="string"&&v.length>0&&v.trim().length>0&&v.length<=320&&!/[\u0000-\u001f\u007f-\u009f]/.test(v),"Bounded derived ID required.");return v;}
function hash(v:unknown):string {const s=differentialId(v);need(/^[a-f0-9]{64}$/.test(s),"SHA-256 required.");return s;}
function arr(v:unknown,max:number):unknown[]{need(Array.isArray(v)&&v.length<=max,"Differential array bound exceeded.");return v;}
function strings(v:unknown,max:number):string[]{const a=arr(v,max).map(differentialId);need(a.join("\0")===[...new Set(a)].sort().join("\0"),"Unique ordinal-canonical IDs required.");return a;}
function integer(v:unknown,min:number,max:number):number {need(typeof v==="string"&&/^(0|-?[1-9][0-9]*)$/.test(v)&&v.length<=6,"Bounded decimal integer required.");const n=Number(v);need(n>=min&&n<=max,"Integer outside profile.");return n;}
function vector(v:unknown,digits:number,cardinal=false):number[] {
  const a=arr(v,3);need(a.length===3,"Three vector components required.");const f=a.map(x=>assemblyFraction(x,digits));
  if(cardinal)need(f.every(x=>x.denominator==="1"&&["-1","0","1"].includes(x.numerator))&&f.filter(x=>x.numerator!=="0").length===1,"Cardinal basis required.");
  return f.map(x=>Number(x.numerator)/Number(x.denominator));
}
function frame(v:unknown,digits=1024):void {
  const f=rec(v,"origin x y z"),x=vector(f.x,digits,true),y=vector(f.y,digits,true),z=vector(f.z,digits,true);vector(f.origin,digits);
  const cross=[x[1]!*y[2]!-x[2]!*y[1]!,x[2]!*y[0]!-x[0]!*y[2]!,x[0]!*y[1]!-x[1]!*y[0]!];
  need(x.reduce((s,v,i)=>s+v*y[i]!,0)===0&&cross.every((v,i)=>v===z[i]),"Proper cardinal frame required.");
}
function quantity(v:unknown,kind="AngularPosition",unit="turn"):void {const q=rec(v,"kind unit value");need(q.kind===kind&&q.unit===unit,"Typed canonical quantity required.");assemblyFraction(q.value);}
function same(a:unknown,b:unknown):boolean{return canonical(a)===canonical(b);}
function canonical(v:unknown):string {
  if(Array.isArray(v))return "["+v.map(canonical).join(",")+"]";
  if(v&&typeof v==="object")return "{"+Object.keys(v).sort().map(k=>JSON.stringify(k)+":"+canonical((v as Record<string,unknown>)[k])).join(",")+"}";return JSON.stringify(v);
}
function parse(bytes:Uint8Array):Record<string,unknown>{const v=readOrientedJsonText(decodeUtf8(bytes,limits.documentBytes),limits);assertOrientedJson(v,limits);return rec(v);}
function owned(input:string|Uint8Array):Uint8Array {need(typeof input==="string"||input instanceof Uint8Array,"Differential UTF-8 bytes required.");need(typeof input==="string"||input.length<=limits.documentBytes,"Document bound before copy.");return typeof input==="string"?encodeUtf8(input,limits.documentBytes):new Uint8Array(input);}
function law(v:unknown,inputs:readonly string[]):void {const l=rec(v,"q b");const q=rec(l.q);need(same(Object.keys(q).sort(),inputs),"Matrix columns must equal declared input basis.");for(const id of inputs)assemblyFraction(q[id],1024);assemblyFraction(l.b,1024);}

function request(v:unknown):{r:Record<string,unknown>;d:Record<string,unknown>;ids:string[];inputs:string[];ports:Map<string,Record<string,unknown>>} {
  const r=rec(v,"requestId inputPortIds definition");hash(r.requestId);const inputs=strings(r.inputPortIds,2);
  const d=rec(r.definition,"definitionId carrierShaft sunShaft planetShaft planeMm sunTeeth planetTeeth module carrierReference sunReference planetReference sunMount planetMount toothRegistration ports holds prefix prefixMapping carrierBodyId sunBodyId planetBodyId contactPresent requiredValidationDomains");
  hash(d.definitionId);frame(d.planeMm,128);quantity(d.module,"LinearPosition","mm");for(const k of ["carrierReference","sunReference","planetReference","sunMount","planetMount"])quantity(d[k]);
  integer(d.sunTeeth,1,4096);integer(d.planetTeeth,1,4096);need(assemblyFraction(d.toothRegistration).denominator==="1"&&d.contactPresent===true,"Contact registration declaration required.");
  const c=rec(d.carrierShaft,"id frame isPrescribed"),s=rec(d.sunShaft,"id frame isPrescribed"),p=rec(d.planetShaft,"id carrierShaftId frameInCarrier isPrescribed");
  const ids=[differentialId(c.id),differentialId(s.id),differentialId(p.id)];need(new Set(ids).size===3&&p.carrierShaftId===c.id,"Distinct coordinate owners required.");
  need(c.isPrescribed===false&&s.isPrescribed===false&&p.isPrescribed===false,"Inputs are explicit ports, not hidden grounding.");frame(c.frame,128);frame(s.frame,128);frame(p.frameInCarrier,128);
  const bodies=[differentialId(d.carrierBodyId),differentialId(d.sunBodyId),differentialId(d.planetBodyId)];need(new Set(bodies).size===3,"Distinct bodies required.");
  if(d.prefix===null)need(d.prefixMapping===null,"No mapping without prefix.");
  else {
    const prefix=rec(d.prefix,"format formatVersion draftId definitionId revision definition importProvenance");need(prefix.format==="gear-invest.mechanical-draft"&&prefix.formatVersion==="0.1","Unsupported prefix source envelope.");hash(prefix.draftId);hash(prefix.definitionId);
    need(typeof prefix.revision==="string"&&/^(0|[1-9][0-9]{0,18})$/.test(prefix.revision),"Prefix revision bound.");
    const source=rec(prefix.definition),shafts=arr(source.shafts,2);need(shafts.length===2,"One-stage source requires two actual shafts.");
    for(const sh of shafts){const x=rec(sh,"id frame isPrescribed");differentialId(x.id);frame(x.frame,128);need(typeof x.isPrescribed==="boolean","Prescribed flag required.");if(x.id!==c.id)ids.push(x.id as string);}
    need(ids.length===4&&new Set(ids).size===4,"Prefix carrier owner must exist once.");
    const m=rec(d.prefixMapping,"sourceCoordinates lengthUnit millimetersPerSourceUnit poseMm");need(m.sourceCoordinates==="original-unitless"&&m.lengthUnit==="mm"&&BigInt(assemblyFraction(m.millimetersPerSourceUnit).numerator)>0n,"Positive typed source mapping required.");frame(m.poseMm,128);
    // Prefix remains source context. Current mechanical/provenance rebuild is C# only.
  }
  ids.sort();const ports=new Map<string,Record<string,unknown>>();
  for(const item of arr(d.ports,6)){const port=rec(item,"id shaftId frameInShaft readoutOffset"),id=differentialId(port.id);need(!ports.has(id)&&ids.includes(differentialId(port.shaftId)),"Duplicate/unknown port owner.");frame(port.frameInShaft,128);quantity(port.readoutOffset);ports.set(id,port);}
  for(const id of inputs)need(ports.has(id),"Unknown input port.");
  const holds=new Set<string>();for(const item of arr(d.holds,6)){const h=rec(item,"id shaftId position"),id=differentialId(h.id);need(!holds.has(id)&&ids.includes(differentialId(h.shaftId)),"Duplicate/unknown static hold owner.");holds.add(id);quantity(h.position);}
  strings(d.requiredValidationDomains,16);return {r,d,ids,inputs,ports};
}

function compiled(v:unknown,ctx:ReturnType<typeof request>,executable:boolean):DifferentialCompiled {
  const c=rec(v,"policy definitionId requestId status canExport isFullyDetermined carrierShaftId sunShaftId planetShaftId inputPortIds coordinateIds rank rows reducedRows checks diagnostics coordinates carrierCommon planetCommon planetRelative ports poseNodes");
  need(c.policy==="exact-bounded-multi-input-carrier-v1"&&c.requestId===ctx.r.requestId&&c.definitionId===ctx.d.definitionId,"Producer identity/policy differs.");
  need(same(strings(c.coordinateIds,4),ctx.ids)&&same(strings(c.inputPortIds,2),ctx.inputs),"Declared coordinate/input set differs.");
  for(const [key,shaft] of [["carrierShaftId","carrierShaft"],["sunShaftId","sunShaft"],["planetShaftId","planetShaft"]])need(c[key!]===rec(ctx.d[shaft!]).id,"Compiled owner differs from source.");
  need(typeof c.canExport==="boolean"&&typeof c.isFullyDetermined==="boolean"&&["DeterminedBySelectedInput","UndrivenRelativeMotion","PinnedByConstraints","InconsistentWithPrescribedInput","InconsistentConstraints","BlockedByInvalidConstraint","UnsupportedConstraintDomain"].includes(String(c.status)),"Unknown determinacy status.");
  if(executable)need(c.canExport===true&&c.isFullyDetermined===true&&["DeterminedBySelectedInput","PinnedByConstraints"].includes(String(c.status)),"Underdetermined/inconsistent source is not executable.");
  if(c.rank!==null)integer(c.rank,0,ctx.ids.length);if(executable)need(Number(c.rank)===ctx.ids.length,"Finalized rank required.");
  const rowIds=new Set<string>();for(const v of arr(c.rows,16)){const r=rec(v,"id a rhs"),id=derivedId(r.id);need(!rowIds.has(id),"Duplicate row ID.");rowIds.add(id);const a=arr(r.a,4),b=arr(r.rhs,3);need(a.length===ctx.ids.length&&b.length===ctx.inputs.length+1,"Row dimensions differ.");for(const n of [...a,...b])assemblyFraction(n,1024);}
  for(const v of arr(c.reducedRows,16)){const r=rec(v,"pivot a rhs witnessRows");integer(r.pivot,-1,ctx.ids.length-1);need(arr(r.a,4).length===ctx.ids.length&&arr(r.rhs,3).length===ctx.inputs.length+1,"Reduced dimensions differ.");for(const n of [...arr(r.a,4),...arr(r.rhs,3)])assemblyFraction(n,1024);for(const id of arr(r.witnessRows,16).map(derivedId))need(rowIds.has(id),"Unknown witness row.");}
  const domains=new Set<string>();for(const v of arr(c.checks,32)){const d=rec(v,"domain verdict required detail"),id=differentialId(d.domain);need(!domains.has(id),"Duplicate scope.");domains.add(id);need(typeof d.required==="boolean"&&typeof d.detail==="string"&&d.detail.length<=4096&&["Pass","Fail","Inconclusive","NotPerformed"].includes(String(d.verdict)),"Unknown validation scope.");if(executable)need(!d.required||d.verdict==="Pass","Required producer scope failed.");}
  if(executable)for(const domain of ["Ownership","SourceMechanics","UnitsAndFrames","ContactAndReference","PitchPlanes"])need(arr(c.checks,32).some(v=>{const d=rec(v);return d.domain===domain&&d.required===true&&d.verdict==="Pass";}),"Required producer scope missing.");
  for(const v of arr(c.diagnostics,32)){const d=rec(v,"code stage detail related");differentialId(d.code);differentialId(d.stage);need(typeof d.detail==="string"&&d.detail.length<=4096,"Bounded diagnostic detail required.");for(const id of arr(d.related,16))need(typeof id==="string"&&id.length<=320,"Bounded diagnostic reference required.");}
  if(executable)need(arr(c.diagnostics,0).length===0,"Finalized errors refused.");
  const coordinateIds=new Set<string>();for(const v of arr(c.coordinates,4)){const r=rec(v,"shaftId isKnown law freeTerms"),id=differentialId(r.shaftId);need(ctx.ids.includes(id)&&!coordinateIds.has(id)&&typeof r.isKnown==="boolean","Coordinate binding invalid.");coordinateIds.add(id);
    if(r.law!==null)law(r.law,ctx.inputs);const free=rec(r.freeTerms);need(Object.keys(free).length<=4,"Free relation bound.");for(const [k,n] of Object.entries(free)){need(ctx.ids.includes(k),"Unknown free coordinate.");assemblyFraction(n,1024);}need(r.isKnown===(r.law!==null&&Object.keys(free).length===0),"Knownness disagrees with expression.");if(executable)need(r.isKnown,"Unknown coordinate cannot drive poses.");}
  if(executable)need(coordinateIds.size===ctx.ids.length,"Missing coordinate.");for(const key of ["carrierCommon","planetCommon","planetRelative"]){if(c[key]!==null)law(c[key],ctx.inputs);else need(!executable,"Compiled world/relative channel missing.");}
  const ports=new Set<string>();for(const v of arr(c.ports,6)){const p=rec(v,"id shaftId sign readoutOffset"),id=differentialId(p.id),source=ctx.ports.get(id);need(source&&!ports.has(id)&&p.shaftId===source.shaftId,"Port owner binding differs.");ports.add(id);const sign=assemblyFraction(p.sign);need(sign.denominator==="1"&&["1","-1"].includes(sign.numerator),"Signed port required.");quantity(p.readoutOffset);need(same(p.readoutOffset,source.readoutOffset)&&same(sign,arr(rec(source.frameInShaft).z,3)[2]),"Port readout differs from source.");}
  need(ports.size===ctx.ports.size,"Missing readout port.");
  const nodes=new Map<string,Record<string,unknown>>(),depths=new Map<string,number>(),bodies=new Set<string>();
  for(const v of arr(c.poseNodes,16)){const n=rec(v,"id parentId frame rotation shaftId bodyId teeth pitchRadiusMm"),id=derivedId(n.id);need(!nodes.has(id)&&(n.parentId===null||typeof n.parentId==="string"&&nodes.has(n.parentId)),"Duplicate/cyclic/non-topological frame parent.");
    need(ctx.ids.includes(differentialId(n.shaftId)),"Unknown pose owner.");frame(n.frame);law(n.rotation,ctx.inputs);const depth=n.parentId===null?0:depths.get(n.parentId as string)!+1;need(depth<=3,"Frame depth exceeded.");depths.set(id,depth);nodes.set(id,n);
    if(n.bodyId!==null){const b=differentialId(n.bodyId);need(!bodies.has(b),"Duplicate body.");bodies.add(b);}need((n.teeth===null)===(n.pitchRadiusMm===null),"Pitch metadata must be paired.");if(n.teeth!==null){integer(n.teeth,1,4096);need(n.bodyId!==null&&BigInt(assemblyFraction(n.pitchRadiusMm,1024).numerator)>0n,"Positive body pitch radius required.");}
  }
  if(executable){need(bodies.size>=(ctx.d.prefix===null?3:5)&&bodies.size<=5,"Body bound.");for(const [id,parent,owner] of [["carrier-frame",null,c.carrierShaftId],["sun-shaft",null,c.sunShaftId],["planet-shaft","carrier-frame",c.planetShaftId],["planet-body","planet-shaft",c.planetShaftId],["sun-body","sun-shaft",c.sunShaftId],["carrier-body","carrier-frame",c.carrierShaftId]]){const n=nodes.get(id as string);need(n&&n.parentId===parent&&n.shaftId===owner,"Typed carrier frame recipe differs.");}
    for(const [id,p] of ctx.ports){const n=nodes.get("port/"+id);need(n&&n.shaftId===p.shaftId&&same(n.frame,p.frameInShaft),"Port pose differs from source.");}}
  else need(c.isFullyDetermined===true||nodes.size===0,"Undetermined analysis cannot carry normal poses.");
  return c as unknown as DifferentialCompiled;
}

export interface ParsedDifferential {readonly bytes:Uint8Array;readonly payloadBytes:Uint8Array;readonly sourceBytes:Uint8Array;readonly replayId:string;readonly payload:DifferentialPayload}
export function parseDifferential(input:string|Uint8Array):ParsedDifferential {
  const bytes=owned(input),outer=parse(bytes);rec(outer,"format formatVersion replayId payloadUtf8");
  if(outer.format!=="gear-invest.differential-replay"||outer.formatVersion!=="0.1")throw new UnsupportedArtifactError("Unsupported differential replay format/version.");
  const replayId=hash(outer.replayId),payloadBytes=decodeBase64(outer.payloadUtf8,limits.documentBytes),p=parse(payloadBytes);rec(p,"profile artifactId requestId sourceRawSha256 sourceArtifactUtf8 compiled");
  need(p.profile==="parallel-free-sun-single-planet-v1","Unsupported differential profile.");hash(p.artifactId);hash(p.requestId);hash(p.sourceRawSha256);
  const sourceBytes=decodeBase64(p.sourceArtifactUtf8,limits.documentBytes),s=parse(sourceBytes);rec(s,"format formatVersion profile requestId artifactHash request attachedSource compiled");
  need(s.format==="gear-invest.differential-mechanism"&&s.formatVersion==="0.1"&&s.profile===p.profile&&s.artifactHash===p.artifactId&&s.requestId===p.requestId,"Source envelope/identity differs.");
  const ctx=request(s.request);need(ctx.r.requestId===p.requestId&&same(p.compiled,s.compiled),"Source/compiled correspondence differs, not mechanical rebuild.");
  const attached=rec(s.attachedSource,"identity artifactUtf8");if(attached.artifactUtf8===null)need(attached.identity===null&&ctx.d.prefix===null,"Prefix source missing.");else {need(typeof attached.identity==="string"&&/^sha256:[a-f0-9]{64}$/.test(attached.identity),"Typed attached source identity required.");need(ctx.d.prefix!==null,"Unexpected attached source.");const original=parse(decodeBase64(attached.artifactUtf8,4194304));need(original.format==="gear-invest.mechanism"&&original.formatVersion==="0.1","Unsupported attached prefix format.");}
  compiled(p.compiled,ctx,true);return {bytes,payloadBytes,sourceBytes,replayId,payload:deepFreeze(p) as unknown as DifferentialPayload};
}

/** Readonly stored boundary observation, not an executable replay or a browser rank solver. */
export function readDifferentialObservation(input:string|Uint8Array):Readonly<{requestId:string;compiled:DifferentialCompiled;authority:"storedObservationOnly"}> {
  const p=parse(owned(input));rec(p,"format formatVersion request numericBoundaryAnalysis boundary compiled");
  need(p.format==="gear-invest.differential-analysis"&&p.formatVersion==="0.1"&&typeof p.numericBoundaryAnalysis==="boolean","Unsupported observation envelope.");const ctx=request(p.request);
  const ids=new Set<string>();for(const v of arr(p.boundary,6)){const b=rec(v,"id portId position"),id=differentialId(b.id);need(!ids.has(id)&&ctx.ports.has(differentialId(b.portId)),"Observation boundary reference invalid.");ids.add(id);quantity(b.position);}
  const c=compiled(p.compiled,ctx,false);return deepFreeze({requestId:ctx.r.requestId as string,compiled:c,authority:"storedObservationOnly" as const});
}
