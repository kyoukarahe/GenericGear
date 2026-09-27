import type { ExactFraction } from './types.js';
import type { Vector3,ExactVector3 } from './orientedTypes.js';
import type { OrientedTwoOutputArtifact,OrientedOutputBinding } from './orientedTwoOutputTypes.js';
import { assertOrientedTwoOutputArtifact } from './orientedTwoOutputInput.js';
import { StoredShaftBodyKernel,exactVector3 } from './orientedKernel.js';
import type { OrientedPlaybackFrame } from './orientedKernel.js';
import { deepFreeze,SnapshotMap } from './immutable.js';
import { ReplayInputError } from './errors.js';
import { fractionToNumber,checkedProduct } from './playback.js';
import { parseReplayInstancesDocument,REPLAY_INSTANCE_LIMITS } from './instances.js';
import type { ReplayInstancesDocument,ReplayInstance } from './instances.js';
import { compileExternalBinding,sampleExternalBinding,sourceRate } from './orientedBinding.js';

export * from './orientedTwoOutputTypes.js';
export type { Vector3,Frame3,ExactVector3,ExactFrame3,OrientedBody,OrientedShaft,OrientedPort } from './orientedTypes.js';
export type { OrientedShaftFrame,OrientedBodyFrame,OrientedPlaybackFrame } from './orientedKernel.js';
export { parseOrientedTwoOutputArtifact,parseOrientedTwoOutputArtifactText,readOrientedTwoOutputSourceBytes,ORIENTED_TWO_OUTPUT_LIMITS,ORIENTED_TWO_OUTPUT_PROFILE } from './orientedTwoOutputInput.js';
export { ORIENTED_NUMERIC_PROFILE } from './orientedKernel.js';
export { ArtifactInputError,UnsupportedArtifactError,ReplayInputError } from './errors.js';
export { REPLAY_NUMERIC_PROFILE,normalizeTurns,formatFraction } from './playback.js';
export { parseReplayInstancesDocument,parseReplayInstancesDocumentText,writeReplayInstancesDocument } from './instances.js';
export type { ReplayInstancesDocument,ReplayInstance,ExternalRootBinding } from './instances.js';
export type { ExactFraction } from './types.js';

export interface OrientedOutputFrame extends OrientedOutputBinding {
  readonly shaftTurns:number; readonly portTurns:number;
  readonly shaftCoefficient:number; readonly portCoefficient:number; readonly portCoordinateSign:1|-1;
  readonly shaftPositiveAxis:Vector3; readonly portPositiveAxis:Vector3; readonly portStation:Vector3; readonly portZeroRay:Vector3;
  /** Axis-times-turns per ROOT turn. Not radians/second or multiplied by the current root input. */
  readonly worldAngularVelocityPerRoot:Vector3;
  readonly requestedTransfer:ExactFraction|null;
  readonly exact:{readonly shaftCoefficient:ExactFraction;readonly portCoefficient:ExactFraction;readonly portCoordinateSign:ExactFraction;readonly portStation:ExactVector3;readonly portPositiveAxis:ExactVector3;readonly shaftPositiveAxis:ExactVector3;readonly portZeroRay:ExactVector3};
}
export interface OrientedTwoOutputPlaybackFrame extends OrientedPlaybackFrame {readonly outputs:ReadonlyMap<string,OrientedOutputFrame>;}
/** Separate format ownership; the internal shaft/body kernel sees no fabricated single-output artifact. */
export class OrientedTwoOutputPlaybackEvaluator {
  readonly artifact:OrientedTwoOutputArtifact;
  readonly #kernel:StoredShaftBodyKernel;
  readonly #outputs:readonly Omit<OrientedOutputFrame,'shaftTurns'|'portTurns'|'worldAngularVelocityPerRoot'>[];
  readonly #frames=new WeakSet<OrientedPlaybackFrame>();
  constructor(artifact:OrientedTwoOutputArtifact){
    assertOrientedTwoOutputArtifact(artifact);this.artifact=artifact;this.#kernel=new StoredShaftBodyKernel(artifact.mechanism);
    const m=artifact.mechanism;
    this.#outputs=m.outputs.map(o=>{
      const shaft=m.shafts.find(s=>s.id===o.shaftId)!,port=m.ports.find(p=>p.id===o.portId)!,state=m.solution.states.find(s=>s.dofId===o.shaftId)!;
      const sign=port.frame.z.reduce<number>((v,f,i)=>v+Number(f.numerator)*Number(shaft.frame.z[i]!.numerator),0);
      if(sign!==1&&sign!==-1)throw new ReplayInputError('PORT_COORDINATE','Noncollinear port coordinate.');
      const qPort={numerator:(BigInt(state.coefficient.numerator)*BigInt(sign)).toString(),denominator:state.coefficient.denominator};
      return deepFreeze({...o,shaftCoefficient:fractionToNumber(state.coefficient),portCoefficient:fractionToNumber(qPort),portCoordinateSign:sign,
        shaftPositiveAxis:exactVector3(shaft.frame.z),portPositiveAxis:exactVector3(port.frame.z),portStation:exactVector3(port.frame.origin),portZeroRay:exactVector3(port.frame.x),
        requestedTransfer:artifact.request.outputs.find(r=>r.key===o.key)!.requestedTransfer,
        exact:{shaftCoefficient:state.coefficient,portCoefficient:qPort,portCoordinateSign:{numerator:String(sign),denominator:'1'},portStation:port.frame.origin,portPositiveAxis:port.frame.z,shaftPositiveAxis:shaft.frame.z,portZeroRay:port.frame.x}});
    });Object.freeze(this);
  }
  ownsFrame(frame:OrientedPlaybackFrame):boolean{return this.#frames.has(frame);}
  evaluate(rootTurns:number,absoluteInputErrorTurns=0):OrientedTwoOutputPlaybackFrame{
    const base=this.#kernel.evaluate(rootTurns,absoluteInputErrorTurns);
    const outputs=new SnapshotMap(this.#outputs.map(o=>{const shaft=base.shafts.get(o.shaftId)!;return [o.key,deepFreeze({...o,shaftTurns:shaft.turns,portTurns:checkedProduct(shaft.turns,o.portCoordinateSign,'port coordinate'),worldAngularVelocityPerRoot:shaft.worldAngularVelocityPerRoot})] as const;}));
    const result=Object.freeze({...base,outputs});this.#frames.add(result);return result;
  }
}
export function compileOrientedTwoOutputPlayback(a:OrientedTwoOutputArtifact):OrientedTwoOutputPlaybackEvaluator{return new OrientedTwoOutputPlaybackEvaluator(a);}
export function sampleOrientedTwoOutputPlayback(d:OrientedTwoOutputPlaybackEvaluator,rootTurns:number):OrientedTwoOutputPlaybackFrame{return d.evaluate(rootTurns);}
export function inspectOrientedTwoOutputCompatibility(a:OrientedTwoOutputArtifact){
  assertOrientedTwoOutputArtifact(a);let numericCompatibility:'supported'|'unsupported'='supported';const diagnostics:string[]=[];
  try{compileOrientedTwoOutputPlayback(a).evaluate(0);}catch(e){if(!(e instanceof ReplayInputError))throw e;numericCompatibility='unsupported';diagnostics.push(e.code+': '+e.message);}
  if(!a.validation.isValid)diagnostics.push('Producer stored INVALID; replay does not approve mechanical validity.');
  return deepFreeze({storedValidation:a.validation,consumptionValidation:'accepted' as const,numericCompatibility,renderingCompatibility:'notAssessed' as const,
    mechanicalValidation:'notPerformed' as const,identityVerification:'notPerformed' as const,sourceRebuild:'notPerformed' as const,diagnostics});
}
export interface OrientedTwoOutputDefinitionResource {readonly definition:OrientedTwoOutputPlaybackEvaluator;readonly rawSha256?:string;}
export interface OrientedTwoOutputInstanceFrame {
  readonly instanceId:string;readonly artifactKey:string;readonly candidateId:string;readonly artifactHash:string;
  readonly inputSemantics:'externallyPrescribedInput';readonly playback:OrientedTwoOutputPlaybackFrame;
  readonly worldAngularVelocityPerSource:ReadonlyMap<string,Vector3>;
  readonly outputWorldAngularVelocityPerSource:ReadonlyMap<string,Vector3>;
}
export class CompiledOrientedTwoOutputInstances {
  readonly document:ReplayInstancesDocument;
  readonly #entries:readonly {instance:ReplayInstance;definition:OrientedTwoOutputPlaybackEvaluator;binding:ReturnType<typeof compileExternalBinding>}[];
  constructor(document:ReplayInstancesDocument,resources:ReadonlyMap<string,OrientedTwoOutputDefinitionResource>){
    this.document=parseReplayInstancesDocument(document);const definitions=new Map<string,OrientedTwoOutputPlaybackEvaluator>();
    for(const ref of this.document.artifacts){const r=resources.get(ref.key);
      if(!r||!(r.definition instanceof OrientedTwoOutputPlaybackEvaluator))throw new ReplayInputError('MISSING_ARTIFACT','Resolve a two-output definition for '+ref.key);
      if(r.definition.artifact.candidateId!==ref.candidateId||r.definition.artifact.artifactHash!==ref.artifactHash||ref.rawSha256!==undefined&&ref.rawSha256!==r.rawSha256)throw new ReplayInputError('ARTIFACT_REFERENCE_MISMATCH','Stored identity/raw-byte observation mismatch.');
      definitions.set(ref.key,r.definition);
    }
    let bodies=0,shafts=0;
    this.#entries=this.document.instances.map(instance=>{const definition=definitions.get(instance.artifactKey)!;bodies+=definition.artifact.mechanism.bodies.length;shafts+=definition.artifact.mechanism.shafts.length;return {instance,definition,binding:compileExternalBinding(instance.binding)};});
    if(bodies>REPLAY_INSTANCE_LIMITS.totalBodies||shafts>REPLAY_INSTANCE_LIMITS.totalDofs)throw new ReplayInputError('RESOURCE_LIMIT','Expanded two-output scene budget exceeded.');Object.freeze(this);
  }
  sample(sources:ReadonlyMap<string,number>):ReadonlyMap<string,OrientedTwoOutputInstanceFrame>{
    return new SnapshotMap(this.#entries.map(e=>{const {root,rootError}=sampleExternalBinding(e.binding,sources),playback=e.definition.evaluate(root,rootError);
      return [e.instance.instanceId,Object.freeze({instanceId:e.instance.instanceId,artifactKey:e.instance.artifactKey,candidateId:e.definition.artifact.candidateId,artifactHash:e.definition.artifact.artifactHash,inputSemantics:'externallyPrescribedInput' as const,playback,
        worldAngularVelocityPerSource:new SnapshotMap([...playback.shafts].map(([id,s])=>[id,sourceRate(s.worldAngularVelocityPerRoot,e.binding.multiplier)] as const)),
        outputWorldAngularVelocityPerSource:new SnapshotMap([...playback.outputs].map(([id,o])=>[id,sourceRate(o.worldAngularVelocityPerRoot,e.binding.multiplier)] as const))})] as const;
    }));
  }
}
export function compileOrientedTwoOutputInstances(document:ReplayInstancesDocument,resources:ReadonlyMap<string,OrientedTwoOutputDefinitionResource>):CompiledOrientedTwoOutputInstances{return new CompiledOrientedTwoOutputInstances(document,resources);}
