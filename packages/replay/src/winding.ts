import { assemblyFraction } from "./assemblyExact.js";
import { encodeUtf8 } from "./assemblyBytes.js";
import { limits, need, rec, arr, hash, integer, parse, raw, same, frame, sourceContext, scene } from "./connectedReplayInput.js";
import { deepFreeze } from "./immutable.js";

export const windingReplayCapabilities=Object.freeze({entry:"@gearinvest/replay/winding",format:"gear-invest.winding-connection-replay",version:"0.1",
  input:"C#-recorded-whole-snapshots",interpolation:"unsupported",windingAuthoring:"unsupported",mechanicalValidation:"notPerformed",sourceRebuild:"notPerformed"});
export interface WindingFraction {readonly numerator:string;readonly denominator:string}
export interface WindingValue {
  readonly kind:"ExactRational"|"NumericResidualOnly";readonly unit:"turn";readonly accumulation:"unwrapped";
  readonly exact:WindingFraction|null;readonly estimate:number|null;readonly solutionErrorBoundTurns:null;
  readonly precision:"exact-rational"|"binary64";readonly constant:WindingFraction;
  readonly terms:readonly {readonly latentId:string;readonly estimate:number;readonly coefficient:WindingFraction}[];
}
export interface WindingFrame {readonly definitionId:string;readonly snapshotId:string;readonly driverTurns:WindingFraction;
  readonly coordinates:readonly {readonly shaftId:string;readonly value:WindingValue}[];
  readonly ports:readonly {readonly portId:string;readonly value:WindingValue}[];
  readonly winding:{readonly driverContact:string;readonly outputContact:string;readonly outputEstimateTurns:number;readonly quality:"NumericResidualOnly";readonly solutionErrorBoundTurns:null;readonly pitchResidualMm:number;readonly totalLengthResidualMm:number;readonly attachmentResidualMm:number;readonly attachmentDirectionResidualMm:number;readonly guidePinResidualMm:number;readonly planarityResidualMm:number;readonly supportResidualMm:number;readonly maximumBendDegrees:number;readonly pins:readonly {readonly id:string;readonly positionMm:readonly number[]}[]};
  readonly matricesMm:readonly {readonly id:string;readonly matrix:readonly number[]}[];readonly displayUnavailableReason:string|null}
export interface WindingAttempt {readonly status:string;readonly appliedSegments:string;readonly lastValidSnapshotId:string;readonly remainder:readonly unknown[];readonly frames:readonly WindingFrame[]}
export interface WindingSceneNode {readonly id:string;readonly owner:string;readonly kind:"pitch-circle"|"pin"|"polyline";readonly radiusMm:number;readonly pointsMm:readonly (readonly number[])[]}
export interface WindingPayload {readonly profile:string;readonly sourceId:string;readonly sourceArtifactId:string;readonly recordingId:string;
  readonly scene:readonly WindingSceneNode[];readonly results:{readonly initial:WindingFrame;readonly attempts:readonly WindingAttempt[];readonly finalSnapshotId:string}}
export function readWindingReplay(input:string|Uint8Array):WindingReplay {
  need(typeof input==="string"||input instanceof Uint8Array,"UTF-8 replay required.");need(typeof input==="string"||input.length<=limits.documentBytes,"Document limit before copy.");
  const bytes=typeof input==="string"?encodeUtf8(input,limits.documentBytes):new Uint8Array(input),outer=rec(parse(bytes),"format formatVersion replayId payloadUtf8");
  need(outer.format===windingReplayCapabilities.format&&outer.formatVersion==="0.1","Unsupported winding replay format/version.");const replayId=hash(outer.replayId),payloadBytes=raw(outer.payloadUtf8);
  const p=rec(parse(payloadBytes),"profile sourceId sourceArtifactId recordingId recordingUtf8 scene results");need(p.profile==="finite-winding-differential-spur-v1","Unsupported connected profile.");hash(p.sourceId);hash(p.sourceArtifactId);hash(p.recordingId);
  const recordingBytes=raw(p.recordingUtf8),recording=rec(parse(recordingBytes),"format formatVersion sourceArtifactId sourceArtifactUtf8 initialPlanetPortTurns requests results");
  need(recording.format==="gear-invest.winding-drive-recording"&&recording.formatVersion==="0.1"&&recording.sourceArtifactId===p.sourceArtifactId,"Recording/source mismatch.");assemblyFraction(recording.initialPlanetPortTurns);
  const sourceBytes=raw(recording.sourceArtifactUtf8),context=sourceContext(sourceBytes);need(context.sourceId===p.sourceId,"Foreign connection source.");
  const {owners,ports,pinIds}=context;const nodes=context.poseIds;
  const results=rec(p.results,"initial attempts finalSnapshotId");need(same(results,recording.results),"Replay differs from recorded snapshots.");frame(results.initial,String(p.sourceId),owners,ports,pinIds,nodes);
  const requests=arr(recording.requests,16),attempts=arr(results.attempts,16);need(requests.length===attempts.length,"Request/result count differs.");let total=0,last=rec(results.initial).snapshotId;
  for(let i=0;i<attempts.length;i++){
    const a=rec(attempts[i],"status appliedSegments lastValidSnapshotId remainder frames"),path=arr(requests[i],16);total+=path.length;
    for(const v of path){const x=rec(v,"driverTurns planetPortTurns");assemblyFraction(x.driverTurns);assemblyFraction(x.planetPortTurns);}
    need(a.lastValidSnapshotId===last,"Atomic snapshot chain broken.");const applied=integer(a.appliedSegments,0,16),frames=arr(a.frames,16);
    if(a.status==="Accepted") {need(applied===path.length&&frames.length===path.length&&arr(a.remainder,0).length===0,"Partial accepted batch refused.");for(let j=0;j<frames.length;j++){frame(frames[j],String(p.sourceId),owners,ports,pinIds,nodes);need(same(rec(frames[j]).driverTurns,rec(path[j]).driverTurns),"Sample/input differs.");last=rec(frames[j]).snapshotId;}}
    else {need(["WindingBoundary","GuardIndeterminate","AmbiguousBranch","NoAdmissibleBranch","NumericalUnresolved","PrecisionLimit","ResourceLimit"].includes(String(a.status)),"Unknown failed outcome.");need(applied===0&&frames.length===0&&same(a.remainder,path),"Failure cannot expose partial success.");}
  }
  need(total<=64&&results.finalSnapshotId===last,"Final checkpoint/resource mismatch.");
  scene(p.scene,nodes);
  return new WindingReplay(token,bytes,payloadBytes,recordingBytes,sourceBytes,replayId,deepFreeze(p) as unknown as WindingPayload);
}
const token=Symbol("winding-reader");
export class WindingReplay {
  readonly #bytes:Uint8Array;readonly #payloadBytes:Uint8Array;readonly #recordingBytes:Uint8Array;readonly #sourceBytes:Uint8Array;
  readonly payload:WindingPayload;readonly replayId:string;readonly samples:readonly WindingFrame[];
  constructor(key:symbol,bytes:Uint8Array,payload:Uint8Array,recording:Uint8Array,source:Uint8Array,id:string,data:WindingPayload){need(key===token,"Use readWindingReplay.");this.#bytes=bytes;this.#payloadBytes=payload;this.#recordingBytes=recording;this.#sourceBytes=source;this.payload=data;this.replayId=id;this.samples=Object.freeze([data.results.initial,...data.results.attempts.flatMap(a=>a.frames)]);Object.freeze(this);}
  originalBytes():Uint8Array{return this.#bytes.slice();}
  selectSample(index:number):WindingFrame {need(Number.isInteger(index)&&index>=0&&index<this.samples.length,"Recorded sample index required; no interpolation or new winding input.");return this.samples[index]!;}
  selectAttempt(index:number):WindingAttempt {need(Number.isInteger(index)&&index>=0&&index<this.payload.results.attempts.length,"Recorded attempt required.");return this.payload.results.attempts[index]!;}
  async verifyIntegrity(sha256:(bytes:Uint8Array)=>string|Promise<string>){need(typeof sha256==="function","SHA-256 provider required.");need(await sha256(this.#payloadBytes.slice())===this.replayId&&await sha256(this.#recordingBytes.slice())===this.payload.recordingId&&await sha256(this.#sourceBytes.slice())===this.payload.sourceArtifactId,"Raw digest mismatch.");return Object.freeze({rawDigest:"Pass",storedProducerValidation:"Finalized",browserMechanicalValidation:"notPerformed",currentSourceRebuild:"notPerformed"});}
}
