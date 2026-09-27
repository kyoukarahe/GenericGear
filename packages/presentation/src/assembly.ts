import { ASSEMBLY_LIMITS, assemblyReferenceKey, ArtifactInputError, ReplayInputError } from "@gearinvest/replay/assembly";
import type { AssemblyReference, AssemblyReplayInstance, AssemblyFrame } from "@gearinvest/replay/assembly";

export type Matrix4 = readonly number[];
export interface AssemblyAssetBinding {
  readonly bindingId: string; readonly instanceId: string; readonly replayId: string; readonly sourceArtifactId: string; readonly definitionId: string;
  readonly reference: AssemblyReference; readonly assetId: string; readonly nodeReference: string;
  /** Rigid asset-local pivot/axis correction. Asset geometry coordinates are millimetres. */
  readonly assetLocalCorrectionMm: Matrix4;
}
export interface AssemblyViewSettings {
  readonly format: "gear-invest.assembly-view"; readonly formatVersion: "0.1";
  readonly instanceId: string; readonly replayId: string; readonly sourceArtifactId: string; readonly definitionId: string;
  readonly displayUnitsPerMillimeter: number; readonly instancePlacement: Matrix4; readonly bindings: readonly AssemblyAssetBinding[];
}
export const assemblyIdentityMatrix: Matrix4 = Object.freeze([1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]);
function bad(message: string): never { throw new ArtifactInputError(message); }
function id(v: unknown): string { if (typeof v !== "string" || !v || v.trim() !== v || v.length > 256) return bad("Bounded binding identity required."); return v; }
function matrix(v: Matrix4, rigid = false): number[] {
  plain(v);
  if (!Array.isArray(v) || v.length !== 16 || Object.keys(v).length !== 16 || v.some(n => typeof n !== "number" || !Number.isFinite(n) || Math.abs(n) > 1e8)
    || v[3] !== 0 || v[7] !== 0 || v[11] !== 0 || v[15] !== 1) return bad("Bounded homogeneous binding matrix required.");
  const m = [...v], det = m[0]!*(m[5]!*m[10]!-m[9]!*m[6]!)-m[4]!*(m[1]!*m[10]!-m[9]!*m[2]!)+m[8]!*(m[1]!*m[6]!-m[5]!*m[2]!);
  if (!Number.isFinite(det) || det < 1e-12) return bad("Binding parent/correction cannot be singular or reflected.");
  for (let i=0;i<3;i++) for (let j=0;j<3;j++) {
    let dot = 0; for (let k=0;k<3;k++) dot += m[4*i+k]!*m[4*j+k]!;
    if (rigid && Math.abs(dot-(i===j?1:0)) > 1e-10 || !rigid && i===j && (dot < 1e-12 || dot > 1e8)) return bad("Binding basis/scale bound exceeded.");
  }
  return m;
}
export function multiplyAssemblyMatrices(a: Matrix4, b: Matrix4): Matrix4 {
  // Composition admits small positive scale; rigid/parent checks belong to their input contracts.
  for(const v of [a,b]) { plain(v); if(!Array.isArray(v)||v.length!==16||v.some(n=>typeof n!=="number"||!Number.isFinite(n))) bad("Finite complete composition matrices required."); }
  const result = new Array<number>(16).fill(0);
  for(let c=0;c<4;c++) for(let r=0;r<4;r++) for(let k=0;k<4;k++) result[c*4+r]! += a[k*4+r]!*b[c*4+k]!;
  if(result.some(n=>!Number.isFinite(n) || Math.abs(n)>1e12)) return bad("Display composition numeric bound exceeded.");
  return Object.freeze(result);
}
function inverse(v: Matrix4): Matrix4 {
  const m = matrix(v), a=m[0]!,b=m[4]!,c=m[8]!,d=m[1]!,e=m[5]!,f=m[9]!,g=m[2]!,h=m[6]!,i=m[10]!;
  const det=a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g);
  const r = [(e*i-f*h)/det,(f*g-d*i)/det,(d*h-e*g)/det,0,(c*h-b*i)/det,(a*i-c*g)/det,(b*g-a*h)/det,0,(b*f-c*e)/det,(c*d-a*f)/det,(a*e-b*d)/det,0,0,0,0,1];
  for(let k=0;k<3;k++) r[12+k] = -(r[k]!*m[12]!+r[4+k]!*m[13]!+r[8+k]!*m[14]!);
  return r;
}
/** No scene objects: renderer reads these matrices and owns every geometry, material, timer and camera. */
export class AssemblyAssetBindings {
  #instance: AssemblyReplayInstance | null; readonly #bindings = new Map<string,AssemblyAssetBinding>();
  #placement: Matrix4 = assemblyIdentityMatrix; #scale = 1;
  constructor(instance: AssemblyReplayInstance) { this.#instance = instance; instance.definition; }
  get #live(): AssemblyReplayInstance { if(!this.#instance) throw new ReplayInputError("BINDINGS_DISPOSED","Asset bindings disposed."); this.#instance.definition; return this.#instance; }
  get count(): number { return this.#bindings.size; }
  /** Transform stored producer-world millimetre features using the same placement/unit contract as bodies. */
  get displayFromWorldMillimeters(): Matrix4 {
    this.#live;
    return multiplyAssemblyMatrices(this.#placement,[this.#scale,0,0,0,0,this.#scale,0,0,0,0,this.#scale,0,0,0,0,1]);
  }
  setPlacement(placement: Matrix4, displayUnitsPerMillimeter: number): void {
    this.#live;
    if(!Number.isFinite(displayUnitsPerMillimeter) || displayUnitsPerMillimeter<1e-6 || displayUnitsPerMillimeter>1e4) bad("Display unit scale out of range.");
    const copy = matrix(placement,true); this.#placement=Object.freeze(copy); this.#scale=displayUnitsPerMillimeter;
  }
  bind(input: AssemblyAssetBinding): void {
    // Plain-data ownership check precedes every field read, including malicious getter rejection.
    const b = parseBinding(input), instance = this.#live, d = instance.definition;
    if(b.instanceId!==instance.instanceId || b.replayId!==d.replayId || b.sourceArtifactId!==d.payload.source.artifactId || b.definitionId!==d.payload.source.definitionId)
      bad("Stale or cross-instance/source asset binding.");
    if(!d.payload.bodies.some(body=>assemblyReferenceKey(body.reference)===assemblyReferenceKey(b.reference))) bad("Unknown body/owner binding.");
    if(this.#bindings.has(b.bindingId) || this.#bindings.size>=ASSEMBLY_LIMITS.externalBindings) bad("Duplicate binding ID or binding budget exceeded.");
    this.#bindings.set(b.bindingId,b);
  }
  unbind(bindingId: string): boolean { this.#live; return this.#bindings.delete(bindingId); }
  inspect(): readonly AssemblyAssetBinding[] { this.#live; return Object.freeze([...this.#bindings.values()]); }
  apply(frame: AssemblyFrame, parentWorld: Matrix4 = assemblyIdentityMatrix): readonly { readonly binding: AssemblyAssetBinding; readonly status: "displayApproximation" | "unavailable"; readonly reason: string | null; readonly worldMatrix: Matrix4 | null; readonly localMatrix: Matrix4 | null }[] {
    const instance = this.#live, d=instance.definition;
    if(frame.instanceId!==instance.instanceId || frame.replayId!==d.replayId || frame.sourceArtifactId!==d.payload.source.artifactId) bad("Stale/cross-instance frame.");
    const parentInverse = inverse(parentWorld);
    const prefix=this.displayFromWorldMillimeters, poses=new Map(frame.bodyFrames.map(p=>[assemblyReferenceKey(p.reference),p]));
    return Object.freeze([...this.#bindings.values()].map(binding=>{
      const pose=poses.get(assemblyReferenceKey(binding.reference));
      if(!pose || pose.status==="unavailable" || !pose.matrixMm) return Object.freeze({binding,status:"unavailable" as const,reason:pose?.reason??"No current body frame.",worldMatrix:null,localMatrix:null});
      const worldMatrix=multiplyAssemblyMatrices(multiplyAssemblyMatrices(prefix,pose.matrixMm),binding.assetLocalCorrectionMm);
      return Object.freeze({binding,status:"displayApproximation" as const,reason:null,worldMatrix,localMatrix:multiplyAssemblyMatrices(parentInverse,worldMatrix)});
    }));
  }
  saveView(): string {
    const instance=this.#live,d=instance.definition;
    return JSON.stringify({format:"gear-invest.assembly-view",formatVersion:"0.1",instanceId:instance.instanceId,replayId:d.replayId,sourceArtifactId:d.payload.source.artifactId,
      definitionId:d.payload.source.definitionId,displayUnitsPerMillimeter:this.#scale,instancePlacement:this.#placement,bindings:this.inspect()} satisfies AssemblyViewSettings);
  }
  dispose(): void { this.#bindings.clear(); this.#instance=null; }
}
function plain(v: unknown, depth=0): void {
  if(depth>16) bad("View data too deep.");
  if(v===null || typeof v==="boolean") return;
  if(typeof v==="number") { if(!Number.isFinite(v)) bad("Nonfinite view number."); return; }
  if(typeof v==="string") { if(v.length>4096) bad("View string too long."); return; }
  if(typeof v!=="object") bad("Plain view JSON required.");
  const keys=Reflect.ownKeys(v),proto=Object.getPrototypeOf(v),isArray=Array.isArray(v);
  if(isArray?proto!==Array.prototype || keys.length!==v.length+1:proto!==Object.prototype && proto!==null) bad("Exotic/sparse view input.");
  if(keys.length>ASSEMBLY_LIMITS.externalBindings+1) bad("View count limit.");
  for(const key of keys){if(isArray&&key==="length")continue;const d=Object.getOwnPropertyDescriptor(v,key)!;
    if(typeof key!=="string" || !Object.hasOwn(d,"value") || !d.enumerable)bad("Accessor/hidden/symbol view field.");plain(d.value,depth+1);}
}
function record(v: unknown,keys:string):Record<string,unknown>{if(!v||typeof v!=="object"||Array.isArray(v)||Object.keys(v).sort().join()!==keys.split(" ").sort().join())bad("Invalid view fields.");return v as Record<string,unknown>;}
function parseBinding(v: unknown):AssemblyAssetBinding{
  plain(v);const b=record(v,"bindingId instanceId replayId sourceArtifactId definitionId reference assetId nodeReference assetLocalCorrectionMm");
  for(const k of ["bindingId","instanceId","replayId","sourceArtifactId","definitionId","assetId","nodeReference"])id(b[k]);
  const ref=record(b.reference,"owner memberId kind localId");id(ref.localId);
  if(ref.kind!=="Body" || !(ref.owner==="Root"&&ref.memberId===null || ref.owner==="Member"&&typeof ref.memberId==="string"))bad("Body owner reference required.");
  if(ref.memberId!==null)id(ref.memberId);
  return Object.freeze({...b,reference:Object.freeze({...ref}),assetLocalCorrectionMm:Object.freeze(matrix(b.assetLocalCorrectionMm as Matrix4,true))}) as unknown as AssemblyAssetBinding;
}
/** Reject duplicate textual keys before JSON.parse can erase evidence. No fetch or artifact rewrite. */
export function restoreAssemblyView(instance: AssemblyReplayInstance,text:string):AssemblyAssetBindings{
  if(typeof text!=="string" || text.length>4194304)bad("View document too large.");
  // Our own writer emits only canonical JSON; rejecting other spelling also detects duplicated keys.
  const value:unknown=JSON.parse(text);plain(value);if(JSON.stringify(value)!==text)bad("Canonical view JSON required.");
  const v=record(value,"format formatVersion instanceId replayId sourceArtifactId definitionId displayUnitsPerMillimeter instancePlacement bindings"),d=instance.definition;
  if(v.format!=="gear-invest.assembly-view"||v.formatVersion!=="0.1"||v.instanceId!==instance.instanceId||v.replayId!==d.replayId||v.sourceArtifactId!==d.payload.source.artifactId||v.definitionId!==d.payload.source.definitionId)bad("Stale/unsupported view identity.");
  if(!Array.isArray(v.bindings)||v.bindings.length>ASSEMBLY_LIMITS.externalBindings)bad("Binding count exceeded.");
  const result=new AssemblyAssetBindings(instance);try{result.setPlacement(v.instancePlacement as Matrix4,v.displayUnitsPerMillimeter as number);for(const b of v.bindings)result.bind(b as AssemblyAssetBinding);return result;}catch(e){result.dispose();throw e;}
}
/** Host owns cancellation/disposal. A token is useful for loaders which cannot be interrupted. */
export class AssemblyLoadGate {
  #generation=0;#disposed=false;
  begin():number { if(this.#disposed)throw new ReplayInputError("LOAD_GATE_DISPOSED","Load gate disposed.");return ++this.#generation; }
  isCurrent(token:number):boolean{return !this.#disposed && Number.isSafeInteger(token) && token===this.#generation;}
  cancel():void{this.#generation++;}
  dispose():void{this.#disposed=true;this.#generation++;}
}
