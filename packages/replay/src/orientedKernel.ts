import type { ExactFraction } from "./types.js";
import type { Vector3, Frame3, ExactVector3, ExactFrame3, OrientedMechanismObservation } from "./orientedTypes.js";
import { deepFreeze, SnapshotMap } from "./immutable.js";
import { ReplayInputError } from "./errors.js";
import { checkedNumber, checkedProduct, fractionToNumber, roundoff, normalizeTurns, REPLAY_NUMERIC_PROFILE } from "./playback.js";

// Internal stored shaft/body evaluator. Format-specific parsers and compilers own their own snapshots/frames.
export const ORIENTED_NUMERIC_PROFILE = Object.freeze({ id: "oriented-presentation-binary64/0.1", scalar: REPLAY_NUMERIC_PROFILE,
  maxWorldMagnitude: 1e8, minFeatureLength: 1e-8,
  worldUnit: "artifactTicks", angularRateUnit: "axis-times-turns-per-root-turn",
  trigWorldPixelCertified: false, precision: "presentationApproximation" } as const);
export function worldNumber(value: number): number {
  if (!Number.isFinite(value) || Math.abs(value)>ORIENTED_NUMERIC_PROFILE.maxWorldMagnitude) throw new ReplayInputError("WORLD_NUMERIC_RANGE", "World coordinate must be finite and within 1e8 artifact ticks; no clamping.");
  return Object.is(value,-0) ? 0 : value;
}
export function exactWorldNumber(f: ExactFraction): number {
  const n = worldNumber(Number(BigInt(f.numerator))/Number(BigInt(f.denominator)));
  if (f.numerator !== "0" && (n===0 || Math.abs(n)<2**-1022)) throw new ReplayInputError("NUMERIC_UNDERFLOW", "World fraction underflow."); return n;
}
export const vector3 = (x:number,y:number,z:number): Vector3 => Object.freeze([worldNumber(x),worldNumber(y),worldNumber(z)]);
export const add3 = (a:Vector3,b:Vector3): Vector3 => vector3(a[0]+b[0],a[1]+b[1],a[2]+b[2]);
export const sub3 = (a:Vector3,b:Vector3): Vector3 => vector3(a[0]-b[0],a[1]-b[1],a[2]-b[2]);
export const scale3 = (a:Vector3,n:number): Vector3 => vector3(a[0]*n,a[1]*n,a[2]*n);
export const dot3 = (a:Vector3,b:Vector3): number => a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
export const cross3 = (a:Vector3,b:Vector3): Vector3 => vector3(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]);
export const exactVector3 = (v:ExactVector3): Vector3 => vector3(exactWorldNumber(v[0]),exactWorldNumber(v[1]),exactWorldNumber(v[2]));
const numericFrame = (f:ExactFrame3): Frame3 => deepFreeze({origin:exactVector3(f.origin),x:exactVector3(f.x),y:exactVector3(f.y),z:exactVector3(f.z)});
/** Right-hand Rodrigues rotation; no translation for a direction. Axis comes from a checked cardinal frame. */
function rotate(a:Vector3,v:Vector3,c:number,s:number): Vector3 {
  const d=dot3(a,v), x=cross3(a,v);
  return vector3(v[0]*c+x[0]*s+a[0]*d*(1-c),v[1]*c+x[1]*s+a[1]*d*(1-c),v[2]*c+x[2]*s+a[2]*d*(1-c));
}
function errorBound(n:number): number {
  if (!Number.isFinite(n)||n<0||n>REPLAY_NUMERIC_PROFILE.absoluteToleranceTurns) throw new ReplayInputError("PRECISION_LOSS", "Scalar arithmetic bound exceeds 1e-6 turns."); return n;
}
export interface OrientedShaftFrame {
  readonly shaftId:string; readonly turns:number; readonly positiveAxis:Vector3;
  /** NOT multiplied by rootTurns and NOT radians/second. */
  readonly worldAngularVelocityPerRoot:Vector3;
}
export interface OrientedBodyFrame extends Frame3 { readonly bodyId:string; readonly shaftId:string; readonly turns:number; readonly outerMarker:Vector3; }
export interface OrientedPlaybackFrame {
  readonly rootTurns:number; readonly shafts:ReadonlyMap<string,OrientedShaftFrame>; readonly bodies:ReadonlyMap<string,OrientedBodyFrame>;
  readonly precision:"presentationApproximation"; readonly absoluteErrorBoundTurns:number;
}
/** Stateless stored GLOBAL solution evaluator. No source-local solution, assembly-pose replay, solver or lifecycle. */
export class StoredShaftBodyKernel {
  readonly #shafts:readonly {id:string; frame:Frame3; coefficient:number; phase:number}[];
  readonly #bodies:readonly {id:string; shaftId:string; frame:Frame3; radius:number}[];
  readonly #frames=new WeakSet<OrientedPlaybackFrame>();
  constructor(mechanism: Pick<OrientedMechanismObservation,'shafts'|'bodies'|'solution'>) {
    this.#shafts=mechanism.shafts.map(s=>{ const state=mechanism.solution.states.find(c=>c.dofId===s.id)!;
      return {id:s.id,frame:numericFrame(s.frame),coefficient:fractionToNumber(state.coefficient),phase:fractionToNumber(state.phaseOffset)}; });
    this.#bodies=mechanism.bodies.map(b=>{ const radius=exactWorldNumber(b.outerPitchRadius);
      if (radius<ORIENTED_NUMERIC_PROFILE.minFeatureLength) throw new ReplayInputError("WORLD_FEATURE_RANGE", "Pitch radius below 1e-8 artifact ticks is not displayable.",b.id);
      return {id:b.id,shaftId:b.shaftId,frame:numericFrame(b.mountingFrame),radius}; });
    Object.freeze(this);
  }
  ownsFrame(frame:OrientedPlaybackFrame):boolean { return this.#frames.has(frame); }
  evaluate(rootTurns:number,absoluteInputErrorTurns=0):OrientedPlaybackFrame {
    rootTurns=checkedNumber(rootTurns,"rootTurns"); let bound=errorBound(absoluteInputErrorTurns);
    const shafts=new Map<string,OrientedShaftFrame>();
    const rotations=new Map<string,{axis:Vector3; origin:Vector3; c:number; s:number}>();
    for(const s of this.#shafts) {
      const product=checkedProduct(rootTurns,s.coefficient,s.id), turns=checkedNumber(product+s.phase,s.id);
      bound=Math.max(bound,errorBound(Math.abs(s.coefficient)*absoluteInputErrorTurns+(Math.abs(rootTurns)+absoluteInputErrorTurns)*roundoff(s.coefficient)+roundoff(product)+roundoff(s.phase)+roundoff(turns)));
      shafts.set(s.id,deepFreeze({shaftId:s.id,turns,positiveAxis:s.frame.z,worldAngularVelocityPerRoot:scale3(s.frame.z,s.coefficient)}));
      // Only the final shaft angle is wrapped for trig. Exact stored strings stay untouched.
      const angle=normalizeTurns(turns)*2*Math.PI;
      rotations.set(s.id,{axis:s.frame.z,origin:s.frame.origin,c:Math.cos(angle),s:Math.sin(angle)});
    }
    const bodies=this.#bodies.map(b=>{
      const r=rotations.get(b.shaftId)!, direction=(v:Vector3)=>rotate(r.axis,v,r.c,r.s);
      const point=(v:Vector3)=>add3(r.origin,direction(sub3(v,r.origin)));
      const frame=deepFreeze({bodyId:b.id,shaftId:b.shaftId,turns:shafts.get(b.shaftId)!.turns,
        origin:point(b.frame.origin),x:direction(b.frame.x),y:direction(b.frame.y),z:direction(b.frame.z),
        outerMarker:point(add3(b.frame.origin,scale3(b.frame.x,b.radius)))});
      return [b.id,frame] as const;
    });
    const result=Object.freeze({rootTurns,shafts:new SnapshotMap(shafts),bodies:new SnapshotMap(bodies),precision:"presentationApproximation" as const,absoluteErrorBoundTurns:bound});
    this.#frames.add(result); return result;
  }
}
