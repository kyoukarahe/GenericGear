import type { ExactFraction } from "./types.js";
import type { OrientedArtifact, Vector3, Frame3, ExactVector3, ExactFrame3 } from "./orientedTypes.js";
import type { ReplayInstancesDocument, ReplayInstance } from "./instances.js";
import { parseReplayInstancesDocument, REPLAY_INSTANCE_LIMITS } from "./instances.js";
import { compileExternalBinding,sampleExternalBinding,sourceRate } from "./orientedBinding.js";
import { assertOrientedArtifact } from "./orientedInput.js";
import { deepFreeze, SnapshotMap } from "./immutable.js";
import { ReplayInputError } from "./errors.js";
import { checkedNumber, checkedProduct, fractionToNumber, roundoff, normalizeTurns, REPLAY_NUMERIC_PROFILE } from "./playback.js";

export * from "./orientedTypes.js";
export { parseOrientedArtifact, parseOrientedArtifactText, readOrientedSourceBytes, ORIENTED_LIMITS, ORIENTED_PROFILE } from "./orientedInput.js";
export { ArtifactInputError, UnsupportedArtifactError, ReplayInputError } from "./errors.js";
export { REPLAY_NUMERIC_PROFILE, normalizeTurns, formatFraction } from "./playback.js";
export { parseReplayInstancesDocument, parseReplayInstancesDocumentText, writeReplayInstancesDocument } from "./instances.js";
export type { ExactFraction } from "./types.js";
export type { ReplayInstancesDocument, ReplayInstance, ExternalRootBinding } from "./instances.js";

export { ORIENTED_NUMERIC_PROFILE,worldNumber,exactWorldNumber,vector3,add3,sub3,scale3,dot3,cross3,exactVector3 } from "./orientedKernel.js";
export type { OrientedShaftFrame,OrientedBodyFrame,OrientedPlaybackFrame } from "./orientedKernel.js";
import { StoredShaftBodyKernel,vector3 } from "./orientedKernel.js";
import type { OrientedPlaybackFrame } from "./orientedKernel.js";
export class OrientedPlaybackEvaluator {
  readonly artifact:OrientedArtifact;
  readonly #kernel:StoredShaftBodyKernel;
  constructor(artifact:OrientedArtifact){assertOrientedArtifact(artifact);this.artifact=artifact;this.#kernel=new StoredShaftBodyKernel(artifact.mechanism);Object.freeze(this);}
  ownsFrame(frame:OrientedPlaybackFrame):boolean{return this.#kernel.ownsFrame(frame);}
  evaluate(rootTurns:number,absoluteInputErrorTurns=0):OrientedPlaybackFrame{return this.#kernel.evaluate(rootTurns,absoluteInputErrorTurns);}
}
export function compileOrientedPlayback(artifact:OrientedArtifact):OrientedPlaybackEvaluator { return new OrientedPlaybackEvaluator(artifact); }
export function sampleOrientedPlayback(definition:OrientedPlaybackEvaluator,rootTurns:number):OrientedPlaybackFrame { return definition.evaluate(rootTurns); }
export function inspectOrientedCompatibility(artifact:OrientedArtifact) {
  assertOrientedArtifact(artifact); let numericCompatibility:"supported"|"unsupported"="supported"; const diagnostics:string[]=[];
  try { compileOrientedPlayback(artifact).evaluate(0); } catch(e) { if(!(e instanceof ReplayInputError))throw e; numericCompatibility="unsupported";diagnostics.push(e.code+": "+e.message); }
  if(!artifact.validation.isValid)diagnostics.push("Producer stored INVALID; replay does not approve mechanical validity.");
  return deepFreeze({storedValidation:artifact.validation,consumptionValidation:"accepted" as const,numericCompatibility,
    renderingCompatibility:"notAssessed" as const,mechanicalValidation:"notPerformed" as const,identityVerification:"notPerformed" as const,sourceRebuild:"notPerformed" as const,diagnostics});
}

export interface OrientedDefinitionResource { readonly definition:OrientedPlaybackEvaluator; readonly rawSha256?:string; }
export interface OrientedInstanceFrame {
  readonly instanceId:string; readonly artifactKey:string; readonly candidateId:string; readonly artifactHash:string;
  readonly inputSemantics:"externallyPrescribedInput"; readonly playback:OrientedPlaybackFrame;
  /** Derivative with respect to this binding's external source turns; includes its multiplier once. */
  readonly worldAngularVelocityPerSource:ReadonlyMap<string,Vector3>;
}
export class CompiledOrientedInstances {
  readonly document:ReplayInstancesDocument;
  readonly #entries:readonly {instance:ReplayInstance; definition:OrientedPlaybackEvaluator; binding:ReturnType<typeof compileExternalBinding>}[];
  constructor(document:ReplayInstancesDocument,resources:ReadonlyMap<string,OrientedDefinitionResource>) {
    this.document=parseReplayInstancesDocument(document); const definitions=new Map<string,OrientedPlaybackEvaluator>();
    for(const ref of this.document.artifacts) {
      const r=resources.get(ref.key);
      if(!r || !(r.definition instanceof OrientedPlaybackEvaluator))throw new ReplayInputError("MISSING_ARTIFACT","Resolve an oriented definition for "+ref.key);
      if(r.definition.artifact.candidateId!==ref.candidateId||r.definition.artifact.artifactHash!==ref.artifactHash||ref.rawSha256!==undefined&&ref.rawSha256!==r.rawSha256)
        throw new ReplayInputError("ARTIFACT_REFERENCE_MISMATCH","Stored identity/raw-byte observation mismatch.");
      definitions.set(ref.key,r.definition);
    }
    let count=0;
    this.#entries=this.document.instances.map(instance=>{const definition=definitions.get(instance.artifactKey)!;count+=definition.artifact.mechanism.bodies.length;
      return {instance,definition,binding:compileExternalBinding(instance.binding)};});
    if(count>REPLAY_INSTANCE_LIMITS.totalBodies)throw new ReplayInputError("RESOURCE_LIMIT","Expanded oriented scene body budget exceeded.");
    Object.freeze(this);
  }
  sample(sources:ReadonlyMap<string,number>):ReadonlyMap<string,OrientedInstanceFrame> {
    const frames=this.#entries.map(e=>{
      const {root,rootError}=sampleExternalBinding(e.binding,sources);
      const playback=e.definition.evaluate(root,rootError);
      const rate=new SnapshotMap([...playback.shafts].map(([id,s])=>[id,sourceRate(s.worldAngularVelocityPerRoot,e.binding.multiplier)] as const));
      return [e.instance.instanceId,Object.freeze({instanceId:e.instance.instanceId,artifactKey:e.instance.artifactKey,candidateId:e.definition.artifact.candidateId,artifactHash:e.definition.artifact.artifactHash,inputSemantics:"externallyPrescribedInput" as const,playback,worldAngularVelocityPerSource:rate})] as const;
    });
    return new SnapshotMap(frames);
  }
}
export function compileOrientedInstances(document:ReplayInstancesDocument,resources:ReadonlyMap<string,OrientedDefinitionResource>):CompiledOrientedInstances { return new CompiledOrientedInstances(document,resources); }
