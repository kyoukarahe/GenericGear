import { ReplayInputError } from "./errors.js";
import { assemblyFraction, applyAffine, displayNumber, moduloTurn } from "./assemblyExact.js";
import { assertOrientedJson, ORIENTED_LIMITS } from "./orientedJson.js";
import { DIFFERENTIAL_LIMITS, differentialId, need, parseDifferential } from "./differentialInput.js";
import type { ParsedDifferential } from "./differentialInput.js";
import type { DifferentialEvaluation, DifferentialLaw, ExactFraction } from "./differentialTypes.js";
export type * from "./differentialTypes.js";
export { DIFFERENTIAL_LIMITS, readDifferentialObservation } from "./differentialInput.js";
export const differentialReplayCapabilities=Object.freeze({entry:"@gearinvest/replay/differential",format:"gear-invest.differential-replay",version:"0.1",profile:"parallel-free-sun-single-planet-v1",
  exactEvaluation:"exported-vector-affine",mechanicalValidation:"notPerformed",sourceRebuild:"notPerformed",rankSolving:"notPerformed",dynamics:"unsupported"});
const token=Symbol("differential-reader");
export function readDifferentialReplay(input:string|Uint8Array):DifferentialReplay {return new DifferentialReplay(token,parseDifferential(input));}
export class DifferentialReplay {
  #data:ParsedDifferential|null;readonly #instances=new Map<string,DifferentialInstance>();
  constructor(key:symbol,data:ParsedDifferential){need(key===token,"Use readDifferentialReplay.");this.#data=data;Object.freeze(this);}
  get #live():ParsedDifferential {if(!this.#data)throw new ReplayInputError("DIFFERENTIAL_DISPOSED","Differential replay disposed.");return this.#data;}
  get replayId(){return this.#live.replayId;}
  get payload(){return this.#live.payload;}
  originalBytes():Uint8Array{return this.#live.bytes.slice();}
  originalArtifactBytes():Uint8Array{return this.#live.sourceBytes.slice();}
  async verifyIntegrity(sha256:(bytes:Uint8Array)=>string|Promise<string>){
    const d=this.#live;need(typeof sha256==="function","Explicit SHA-256 provider required.");
    need(await sha256(d.payloadBytes.slice())===d.replayId&&await sha256(d.sourceBytes.slice())===d.payload.sourceRawSha256,"Differential raw digest mismatch.");
    this.#live;return Object.freeze({rawDigest:"Pass",storedProducerValidation:"Finalized",browserMechanicalValidation:"notPerformed",currentSourceRebuild:"notPerformed"});
  }
  createInstance(id:string):DifferentialInstance {this.#live;differentialId(id);need(!this.#instances.has(id)&&this.#instances.size<DIFFERENTIAL_LIMITS.instances,"Duplicate instance or instance limit.");
    const instance=new DifferentialInstance(token,id,this,()=>this.#instances.delete(id));this.#instances.set(id,instance);return instance;}
  dispose():void {for(const instance of this.#instances.values())instance.dispose();this.#instances.clear();this.#data=null;}
}
export class DifferentialInstance {
  readonly instanceId:string;#definition:DifferentialReplay|null;#release:(()=>void)|null;
  constructor(key:symbol,id:string,definition:DifferentialReplay,release:()=>void){need(key===token,"Use createInstance.");this.instanceId=id;this.#definition=definition;this.#release=release;Object.freeze(this);}
  get definition():DifferentialReplay {if(!this.#definition)throw new ReplayInputError("DIFFERENTIAL_DISPOSED","Differential instance disposed.");return this.#definition;}
  evaluate(snapshot:Readonly<Record<string,ExactFraction>>):DifferentialEvaluation {
    const def=this.definition,c=def.payload.compiled;
    // Validate plain owned data before invoking any property getter; snapshot is never retained by reference.
    assertOrientedJson(snapshot,{...ORIENTED_LIMITS,documentBytes:2048,nodes:20,depth:3,properties:2,sourceBytes:2048});
    need(snapshot&&typeof snapshot==="object"&&!Array.isArray(snapshot)&&Object.keys(snapshot).sort().join("\0")===c.inputPortIds.join("\0"),"Complete exact input vector required; no previous/zero defaults.");
    const input=Object.freeze(Object.fromEntries(c.inputPortIds.map(id=>[id,assemblyFraction(snapshot[id])])));
    const evalLaw=(l:DifferentialLaw)=>{let value=l.b;for(const id of c.inputPortIds)value=applyAffine(l.q[id]!,value,input[id]!);return value;};
    const coordinates=Object.freeze(Object.fromEntries(c.coordinates.map(s=>[s.shaftId,evalLaw(s.law!)])));
    const ports=Object.freeze(Object.fromEntries(c.ports.map(p=>[p.id,applyAffine(p.sign,p.readoutOffset.value,coordinates[p.shaftId]!)])));
    const exact={instanceId:this.instanceId,replayId:def.replayId,requestId:c.requestId,input,coordinates,ports,carrierCommon:evalLaw(c.carrierCommon!),planetCommon:evalLaw(c.planetCommon!),planetRelative:evalLaw(c.planetRelative!)};
    try{
      const matrices=new Map<string,readonly number[]>();
      const nodes=c.poseNodes.map(n=>{
        const f=n.frame,angle=displayNumber(moduloTurn(evalLaw(n.rotation)),1)*2*Math.PI,co=Math.cos(angle),si=Math.sin(angle);
        const x=f.x.map(v=>displayNumber(v)),y=f.y.map(v=>displayNumber(v)),z=f.z.map(v=>displayNumber(v)),o=f.origin.map(v=>displayNumber(v));
        let m=[x[0]!*co+y[0]!*si,x[1]!*co+y[1]!*si,x[2]!*co+y[2]!*si,0,y[0]!*co-x[0]!*si,y[1]!*co-x[1]!*si,y[2]!*co-x[2]!*si,0,...z,0,...o,1];
        if(n.parentId!==null){const parent=matrices.get(n.parentId)!;const product=new Array<number>(16).fill(0);for(let col=0;col<4;col++)for(let row=0;row<4;row++)for(let k=0;k<4;k++)product[col*4+row]!+=parent[k*4+row]!*m[col*4+k]!;m=product;}
        if(m.some(v=>!Number.isFinite(v)||Math.abs(v)>1e8))throw new ReplayInputError("DIFFERENTIAL_DISPLAY","Bounded display unavailable.");
        const matrixMm=Object.freeze(m);matrices.set(n.id,matrixMm);return Object.freeze({id:n.id,matrixMm});
      });return Object.freeze({...exact,display:Object.freeze({status:"displayApproximation",reason:null,nodes:Object.freeze(nodes)})});
    }catch(e){if(!(e instanceof ReplayInputError))throw e;return Object.freeze({...exact,display:Object.freeze({status:"unavailable",reason:e.message,nodes:Object.freeze([])})});}
  }
  dispose():void {this.#release?.();this.#release=null;this.#definition=null;}
}
