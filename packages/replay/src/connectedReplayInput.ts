import { assemblyFraction } from "./assemblyExact.js";
import { decodeBase64, decodeUtf8 } from "./assemblyBytes.js";
import { assertOrientedJson, ORIENTED_LIMITS, readOrientedJsonText } from "./orientedJson.js";
import { ArtifactInputError } from "./errors.js";
export const limits={...ORIENTED_LIMITS,documentBytes:4194304,sourceBytes:4194304,nodes:131072,depth:40,properties:64};
export type Obj=Record<string,unknown>;
export function need(ok:unknown,message:string):asserts ok {if(!ok)throw new ArtifactInputError(message);}
export function rec(v:unknown,fields?:string):Obj {need(v&&typeof v==="object"&&!Array.isArray(v),"Winding object required.");if(fields)need(Object.keys(v).sort().join()==fields.split(" ").sort().join(),"Unknown/missing winding fields.");return v as Obj;}
export function arr(v:unknown,max:number):unknown[]{need(Array.isArray(v)&&v.length<=max,"Winding array limit.");return v;}
export function id(v:unknown):string {need(typeof v==="string"&&v.length>0&&v.length<=320&&!/[\u0000-\u001f\u007f-\u009f]/.test(v),"Bounded ID required.");return v;}
export function hash(v:unknown):string {const s=id(v);need(/^[0-9a-f]{64}$/.test(s),"SHA-256 required.");return s;}
export function number(v:unknown,max=1e8):number {need(typeof v==="number"&&Number.isFinite(v)&&Math.abs(v)<=max,"Finite bounded binary64 required.");return v;}
export function integer(v:unknown,min:number,max:number):number {need(typeof v==="string"&&/^(0|[1-9][0-9]{0,3})$/.test(v),"Integer string required.");const n=Number(v);need(n>=min&&n<=max,"Integer bound.");return n;}
export function parse(b:Uint8Array):Obj {const v=readOrientedJsonText(decodeUtf8(b,limits.documentBytes),limits);assertOrientedJson(v,limits);return rec(v);}
export function raw(v:unknown):Uint8Array {need(typeof v==="string","Base64 required.");return decodeBase64(v,limits.documentBytes);}
export function same(a:unknown,b:unknown):boolean {return JSON.stringify(a)===JSON.stringify(b);}
export function value(v:unknown):void {
  const p=rec(v,"kind unit accumulation exact estimate solutionErrorBoundTurns precision constant terms");
  need(p.unit==="turn"&&p.accumulation==="unwrapped"&&p.solutionErrorBoundTurns===null,"No invented numerical guarantee.");assemblyFraction(p.constant,1024);
  const terms=arr(p.terms,64),ids=new Set<string>();for(const term of terms){const t=rec(term,"latentId estimate coefficient"),key=hash(t.latentId);need(!ids.has(key),"Duplicate latent.");ids.add(key);number(t.estimate,1000);assemblyFraction(t.coefficient,1024);}
  if(p.kind==="ExactRational"){need(p.estimate===null&&p.precision==="exact-rational"&&terms.length===0,"Exact value mislabelled.");assemblyFraction(p.exact,1024);need(same(p.exact,p.constant),"Exact constant differs.");}
  else {need(p.kind==="NumericResidualOnly"&&p.exact===null&&p.precision==="binary64"&&terms.length>0,"Numerical quality required.");number(p.estimate,Number.MAX_VALUE);}
}
export function frame(v:unknown,sourceId:string,owners:string[],ports:string[],pins:string[],nodeIds?:string[]):string[] {
  const f=rec(v,"definitionId snapshotId driverTurns coordinates ports winding displayUnavailableReason matricesMm");need(f.definitionId===sourceId,"Foreign frame.");hash(f.snapshotId);assemblyFraction(f.driverTurns);
  const coords=arr(f.coordinates,6);need(same(coords.map(v=>rec(v).shaftId),owners),"Actual shaft set differs.");for(const v of coords){const c=rec(v,"shaftId value");value(c.value);}
  const pp=arr(f.ports,8);need(same(pp.map(v=>rec(v).portId),ports),"Port set differs.");for(const v of pp){const c=rec(v,"portId value");value(c.value);}
  const w=rec(f.winding,"driverContact outputContact outputEstimateTurns quality solutionErrorBoundTurns pitchResidualMm totalLengthResidualMm attachmentResidualMm attachmentDirectionResidualMm guidePinResidualMm planarityResidualMm supportResidualMm maximumBendDegrees pins");
  integer(w.driverContact,1,5);integer(w.outputContact,1,5);number(w.outputEstimateTurns,1000);need(w.quality==="NumericResidualOnly"&&w.solutionErrorBoundTurns===null,"Winding is not exact.");
  for(const name of ["pitchResidualMm","totalLengthResidualMm","attachmentResidualMm","supportResidualMm","maximumBendDegrees"])need(number(w[name])>=0,"Negative residual.");
  need(Number(w.pitchResidualMm)<=1e-9&&Number(w.totalLengthResidualMm)<=21e-9&&Number(w.attachmentResidualMm)<=1e-9&&Number(w.supportResidualMm)<=1e-9&&Number(w.maximumBendDegrees)<=45.0000001,"Stored residual outside profile.");
  for(const key of ["attachmentDirectionResidualMm","guidePinResidualMm","planarityResidualMm"])need(number(w[key])>=0&&Number(w[key])<=1e-9,"Stored guide/attachment residual outside policy.");
  const pinArray=arr(w.pins,22);need(same(pinArray.map(v=>rec(v).id),pins),"Material pin identities differ.");for(const v of pinArray){const p=rec(v,"id positionMm");need(arr(p.positionMm,2).length===2,"Planar pin required.");for(const n of p.positionMm as unknown[])number(n);}
  const nodes=arr(f.matricesMm,64),seen=new Set<string>();for(const v of nodes){const n=rec(v,"id matrix"),key=id(n.id);need(!seen.has(key),"Duplicate display node.");seen.add(key);const m=arr(n.matrix,16);need(m.length===16,"4x4 matrix required.");for(const x of m)number(x);need(m[3]===0&&m[7]===0&&m[11]===0&&m[15]===1,"Affine homogeneous matrix required.");}
  need(f.displayUnavailableReason===null||typeof f.displayUnavailableReason==="string"&&f.displayUnavailableReason.length<4096,"Display status required.");
  if(f.displayUnavailableReason!==null)need(nodes.length===0,"Unavailable pose must be empty.");else if(nodeIds)need(same([...seen],nodeIds),"Pose set changed between snapshots.");return [...seen];
}
export function sourceContext(sourceBytes:Uint8Array){
  const source=rec(parse(sourceBytes),"format formatVersion profile definitionId sourceDraftUtf8 parentArtifactUtf8 parentArtifactId outputLaw validation numericalPolicy unperformed");
  need(source.format==="gear-invest.winding-connection-mechanism"&&source.formatVersion==="0.1"&&source.profile==="finite-winding-differential-spur-v1","Unsupported connection source.");
  const sourceId=hash(source.definitionId),draft=parse(raw(source.sourceDraftUtf8)),w=rec(draft.winding),suffix=rec(draft.suffix),parent=parse(raw(source.parentArtifactUtf8)),compiled=rec(parent.compiled);
  need(draft.format==="gear-invest.winding-connection-draft"&&draft.definitionId===sourceId&&source.numericalPolicy==="binary64-residual-upper-support-v1","Source policy differs.");
  need(parent.format==="gear-invest.differential-mechanism"&&parent.formatVersion==="0.1"&&parent.artifactHash===source.parentArtifactId&&compiled.canExport===true,"Stored parent admission/context differs.");
  const g=rec(w.geometry),count=integer(g.linkCount,4,21),chain=id(w.chainId),pinIds=Array.from({length:count+1},(_,i)=>chain+"/pin-"+String(i).padStart(2,"0"));
  const owners=[...arr(compiled.coordinateIds,3).map(id),id(rec(w.driverShaft).id),id(rec(w.outputShaft).id),id(rec(suffix.outputShaft).id)].sort();need(owners.length===6&&new Set(owners).size===6,"Six independent actual owners required.");
  const ports=[...arr(compiled.ports,6).map(v=>id(rec(v).id)),id(rec(suffix.outputPort).id)].sort();need(new Set(ports).size===ports.length,"Duplicate port.");
  const poseIds=[...arr(compiled.poseNodes,16).map(v=>id(rec(v).id)),"connected/suffix-driver","connected/suffix-output","connected/winding-driver","connected/winding-output"];
  for(let i=0;i<=count;i++){poseIds.push("connected/pin-"+String(i).padStart(2,"0"));if(i<count)poseIds.push("connected/link-"+String(i).padStart(2,"0"));}
  return {source,sourceId,draft,w,suffix,owners,ports,pinIds,poseIds};
}
export function scene(v:unknown,nodes:string[]):void{
  const seen=new Set<string>();for(const item of arr(v,64)){const n=rec(item,"id owner kind radiusMm pointsMm"),key=id(n.id);id(n.owner);need(nodes.includes(key)&&!seen.has(key),"Unknown/duplicate scene node.");seen.add(key);need(["pitch-circle","pin","polyline"].includes(String(n.kind))&&number(n.radiusMm)>=0,"Scene primitive invalid.");for(const point of arr(n.pointsMm,13)){need(arr(point,2).length===2,"Planar point required.");for(const x of point as unknown[])number(x);}}
}
