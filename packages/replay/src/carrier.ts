import { ReplayInputError } from "./errors.js";
import { assemblyFraction, applyAffine, displayNumber, moduloTurn } from "./assemblyExact.js";
import { CARRIER_LIMITS, carrierId, carrierNeed, parseCarrier } from "./carrierInput.js";
import type { ParsedCarrier } from "./carrierInput.js";
import type { CarrierEvaluation, CarrierLaw, ExactFraction } from "./carrierTypes.js";
export type * from "./carrierTypes.js";
export { CARRIER_LIMITS } from "./carrierInput.js";
export const carrierReplayCapabilities=Object.freeze({entry:"@gearinvest/replay/carrier",format:"gear-invest.carrier-replay",version:"0.1",
  profile:"parallel-grounded-sun-single-planet-v1",mechanicalValidation:"notPerformed",sourceRebuild:"notPerformed",dynamics:"unsupported"});
const key=Symbol("carrier-reader");
export function readCarrierReplay(input:string|Uint8Array):CarrierReplay { return new CarrierReplay(key,parseCarrier(input)); }
export class CarrierReplay {
  #data:ParsedCarrier|null;readonly #instances=new Map<string,CarrierInstance>();
  constructor(token:symbol,data:ParsedCarrier) {carrierNeed(token===key,"Use readCarrierReplay.");this.#data=data;Object.freeze(this);}
  get #live():ParsedCarrier {if(!this.#data) throw new ReplayInputError("CARRIER_DISPOSED","Carrier replay disposed.");return this.#data;}
  get replayId():string{return this.#live.replayId;}
  get payload(){return this.#live.payload;}
  originalBytes():Uint8Array{return this.#live.bytes.slice();}
  originalArtifactBytes():Uint8Array{return this.#live.sourceBytes.slice();}
  async verifyIntegrity(sha256:(bytes:Uint8Array)=>string|Promise<string>) {
    const p=this.#live;carrierNeed(typeof sha256==="function","Explicit SHA-256 provider required.");
    carrierNeed(await sha256(p.payloadBytes.slice())===p.replayId && await sha256(p.sourceBytes.slice())===p.payload.sourceRawSha256,"Carrier raw digest mismatch.");
    this.#live;return Object.freeze({rawDigest:"Pass",storedProducerValidation:"Finalized",browserMechanicalValidation:"notPerformed",currentSourceRebuild:"notPerformed"});
  }
  createInstance(id:string):CarrierInstance {
    this.#live;carrierId(id);carrierNeed(!this.#instances.has(id)&&this.#instances.size<CARRIER_LIMITS.instances,"Duplicate instance or carrier instance bound.");
    const instance=new CarrierInstance(key,id,this,()=>this.#instances.delete(id));this.#instances.set(id,instance);return instance;
  }
  dispose():void {for(const instance of this.#instances.values())instance.dispose();this.#instances.clear();this.#data=null;}
}
export class CarrierInstance {
  readonly instanceId:string;#definition:CarrierReplay|null;#release:(()=>void)|null;
  constructor(token:symbol,id:string,definition:CarrierReplay,release:()=>void) {carrierNeed(token===key,"Use createInstance.");this.instanceId=id;this.#definition=definition;this.#release=release;Object.freeze(this);}
  get definition():CarrierReplay {if(!this.#definition)throw new ReplayInputError("CARRIER_DISPOSED","Carrier instance disposed.");return this.#definition;}
  evaluate(value:ExactFraction):CarrierEvaluation {
    const root=assemblyFraction(value),def=this.definition,c=def.payload.compiled;
    const evalLaw=(l:CarrierLaw)=>applyAffine(l.q,l.p,root);
    const shafts=Object.freeze(c.shafts.map(s=>Object.freeze({id:s.id,world:evalLaw(s.world),carrierRelative:s.carrierRelative?evalLaw(s.carrierRelative):null})));
    const exact={instanceId:this.instanceId,replayId:def.replayId,input:root,carrierCommon:evalLaw(c.carrierCommon),planetCommon:evalLaw(c.planetCommon),portReadout:evalLaw(c.portReadout),shafts};
    try {
      const matrices=new Map<string,readonly number[]>();
      const nodes=c.poseNodes.map(n=>{
        const f=n.frame,angle=displayNumber(moduloTurn(evalLaw(n.rotation)),1)*2*Math.PI,co=Math.cos(angle),si=Math.sin(angle);
        const x=f.x.map(v=>displayNumber(v)),y=f.y.map(v=>displayNumber(v)),z=f.z.map(v=>displayNumber(v)),o=f.origin.map(v=>displayNumber(v));
        let m=[x[0]!*co+y[0]!*si,x[1]!*co+y[1]!*si,x[2]!*co+y[2]!*si,0,y[0]!*co-x[0]!*si,y[1]!*co-x[1]!*si,y[2]!*co-x[2]!*si,0,...z,0,...o,1];
        if(n.parentId!==null){const p=matrices.get(n.parentId)!;const product=new Array<number>(16).fill(0);for(let col=0;col<4;col++)for(let row=0;row<4;row++)for(let k=0;k<4;k++)product[col*4+row]!+=p[k*4+row]!*m[col*4+k]!;m=product;}
        if(m.some(v=>!Number.isFinite(v)||Math.abs(v)>1e8))throw new ReplayInputError("CARRIER_DISPLAY","Bounded display is unavailable.");
        const matrixMm=Object.freeze(m);matrices.set(n.id,matrixMm);return Object.freeze({id:n.id,matrixMm});
      });
      return Object.freeze({...exact,display:Object.freeze({status:"displayApproximation",reason:null,nodes:Object.freeze(nodes)})});
    }catch(e){if(!(e instanceof ReplayInputError))throw e;return Object.freeze({...exact,display:Object.freeze({status:"unavailable",reason:e.message,nodes:Object.freeze([])})});}
  }
  dispose():void {this.#release?.();this.#release=null;this.#definition=null;}
}
