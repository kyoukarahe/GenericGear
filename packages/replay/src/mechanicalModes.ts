import { assemblyFraction } from "./assemblyExact.js";
import { encodeUtf8 } from "./assemblyBytes.js";
import { deepFreeze } from "./immutable.js";
import { limits, need, rec, arr, id, hash, integer, parse, raw, same, frame, value, sourceContext, scene } from "./connectedReplayInput.js";
import type { WindingFrame, WindingValue, WindingSceneNode } from "./winding.js";

export const mechanicalModeReplayCapabilities=Object.freeze({entry:"@gearinvest/replay/mechanical-modes",format:"gear-invest.mechanical-mode-replay",version:"0.1",
  input:"C#-recorded-atomic-mode-path",modeTransitions:"producer-only",interpolation:"unsupported",mechanicalValidation:"notPerformed",sourceRebuild:"notPerformed"});
export interface MechanicalModeSample {readonly definitionId:string;readonly stateId:string;readonly historyId:string;readonly mode:string;readonly revision:string;readonly eventCursor:string;
  readonly allowedDirection:number;readonly requiredInputPorts:readonly string[];readonly couplingOffset:WindingValue;readonly lockReference:WindingValue|null;readonly frame:WindingFrame}
export interface MechanicalModeAttempt {readonly status:string;readonly appliedSegments:string;readonly lastValidStateId:string;readonly replayedStateId:string|null;
  readonly state:MechanicalModeSample|null;readonly samples:readonly MechanicalModeSample[];readonly remainingSegments:string}
export interface MechanicalModePayload {readonly profile:string;readonly connectionId:string;readonly definitionId:string;readonly sourceArtifactId:string;readonly recordingId:string;
  readonly scene:readonly WindingSceneNode[];readonly results:{readonly initial:MechanicalModeSample;readonly attempts:readonly MechanicalModeAttempt[];readonly finalStateId:string}}
const modes=["DriveCapture","Released","WorldCarrierLock","PlanetRelativeLock","DirectionRestrictedDrive"],events=["Release","Capture","AlignCapture","LockWorldCarrier","LockPlanetRelative","CapturePositive","CaptureNegative"];
export function readMechanicalModeReplay(input:string|Uint8Array):MechanicalModeReplay{
  need(typeof input==="string"||input instanceof Uint8Array,"UTF-8 replay required.");need(typeof input==="string"||input.length<=limits.documentBytes,"Document limit before copy.");
  const bytes=typeof input==="string"?encodeUtf8(input,limits.documentBytes):new Uint8Array(input),outer=rec(parse(bytes),"format formatVersion replayId payloadUtf8");
  need(outer.format===mechanicalModeReplayCapabilities.format&&outer.formatVersion==="0.1","Unsupported mode replay format/version.");const replayId=hash(outer.replayId),payloadBytes=raw(outer.payloadUtf8);
  const p=rec(parse(payloadBytes),"profile connectionId definitionId sourceArtifactId recordingId recordingUtf8 scene results");need(p.profile==="bounded-winding-mechanical-modes-v1","Unsupported mode profile.");for(const key of ["connectionId","definitionId","sourceArtifactId","recordingId"])hash(p[key]);
  const recordingBytes=raw(p.recordingUtf8),recording=rec(parse(recordingBytes),"format formatVersion profile sourceArtifactId sourceArtifactUtf8 policy initialPlanetPortTurns requests results");
  need(recording.format==="gear-invest.mechanical-mode-recording"&&recording.formatVersion==="0.1"&&recording.profile===p.profile&&recording.sourceArtifactId===p.sourceArtifactId,"Recording context differs.");assemblyFraction(recording.initialPlanetPortTurns);
  const sourceBytes=raw(recording.sourceArtifactUtf8),context=sourceContext(sourceBytes);need(context.sourceId===p.connectionId,"Foreign connection.");
  const policy=rec(recording.policy,"definitionId allowedModes alignmentOffset");need(policy.definitionId===p.definitionId,"Foreign mode policy.");assemblyFraction(policy.alignmentOffset);
  const allowed=arr(policy.allowedModes,5).map(id);need(allowed.includes("DriveCapture")&&allowed.every(m=>modes.includes(m))&&same([...new Set(allowed)].sort(),allowed),"Invalid mode policy.");
  const nodes=context.poseIds;
  function state(v:unknown):void{
    const s=rec(v,"definitionId stateId historyId mode revision eventCursor allowedDirection requiredInputPorts couplingOffset lockReference ledger frame");need(s.definitionId===p.definitionId&&allowed.includes(String(s.mode)),"Unknown state mode/source.");hash(s.stateId);hash(s.historyId);integer(s.revision,0,4096);integer(s.eventCursor,0,9999);
    const required=s.mode==="Released"?[context.draft.sunPortId,context.draft.planetPortId].sort():s.mode==="DriveCapture"||s.mode==="DirectionRestrictedDrive"?[context.draft.planetPortId]:[];
    need(same(s.requiredInputPorts,required),"Mode input ownership differs.");value(s.couplingOffset);
    const locked=s.mode==="WorldCarrierLock"||s.mode==="PlanetRelativeLock";if(locked){need(s.lockReference!==null,"Lock reference missing.");value(s.lockReference);}else need(s.lockReference===null,"Unexpected lock reference.");
    need(s.mode==="DirectionRestrictedDrive"?s.allowedDirection===1||s.allowedDirection===-1:s.allowedDirection===0,"Direction declaration differs.");
    const ledgerIds=new Set<string>();for(const item of arr(s.ledger,16)){const e=rec(item,"requestId payloadId resultStateId eventIds"),key=id(e.requestId);need(!ledgerIds.has(key),"Duplicate ledger request.");ledgerIds.add(key);hash(e.payloadId);hash(e.resultStateId);for(const idValue of arr(e.eventIds,16))id(idValue);}
    frame(s.frame,context.sourceId,context.owners,context.ports,context.pinIds,nodes);
  }
  const results=rec(p.results,"initial attempts finalStateId");need(same(results,recording.results),"Mode replay differs from recorded results.");state(results.initial);
  const requests=arr(recording.requests,32),attempts=arr(results.attempts,32);need(requests.length===attempts.length,"Request/result count differs.");let last=rec(results.initial),cost=0;
  for(let i=0;i<attempts.length;i++){
    const r=rec(requests[i],"id sourceId expectedStateId expectedRevision payloadId segments");id(r.id);id(r.sourceId);id(r.expectedStateId);integer(r.expectedRevision,0,4096);hash(r.payloadId);
    const segments=arr(r.segments,16);need(segments.length>0,"Empty request.");cost+=segments.length;
    for(const item of segments){const s=rec(item,"driverTurns independentPorts observations events");assemblyFraction(s.driverTurns);for(const name of ["independentPorts","observations"]){const values=rec(s[name]);need(Object.keys(values).length<=6,"Port bound.");for(const [key,v]of Object.entries(values)){id(key);assemblyFraction(v);}}
      const ee=arr(s.events,16);cost+=ee.length;for(const item of ee){const e=rec(item,"id sequence ordinal kind");id(e.id);integer(e.sequence,1,9999);integer(e.ordinal,0,15);need(events.includes(String(e.kind)),"Unsupported event.");}}
    const a=rec(attempts[i],"status appliedSegments lastValidStateId replayedStateId state samples remainingSegments");need(a.lastValidStateId===last.stateId,"Broken atomic state chain.");const samples=arr(a.samples,32);
    if(a.status==="Accepted"){
      need(r.sourceId===p.definitionId&&r.expectedStateId===last.stateId&&r.expectedRevision===last.revision,"Accepted stale/foreign request.");need(integer(a.appliedSegments,0,16)===segments.length&&a.remainingSegments==="0"&&a.replayedStateId===null&&a.state!==null,"Partial accepted mode batch.");
      for(const v of samples)state(v);state(a.state);const next=rec(a.state);need(Number(next.revision)===Number(last.revision)+1&&Number(next.eventCursor)>=Number(last.eventCursor),"Revision/cursor did not advance monotonically.");last=next;
    }else{
      const failures=["AlreadyApplied","ForeignSnapshot","StaleSnapshot","IdempotencyConflict","ResourceLimit","InvalidDefinition","Underdetermined","ModeInputOwnershipConflict","InvalidReference","DirectionConflict","WindingBoundary","GuardIndeterminate","AmbiguousBranch","NoAdmissibleBranch","NumericalUnresolved","PrecisionLimit","InconsistentObservation","InvalidEventOrder","StaleEventSequence","DuplicateEventIdentity","AlignmentConflict","MissingCouplingBoundary","UnsupportedMode"];
      need(failures.includes(String(a.status))&&a.state===null&&samples.length===0&&a.appliedSegments==="0","Invalid failed/idempotent result.");
      if(a.status==="AlreadyApplied"){hash(a.replayedStateId);need(a.remainingSegments==="0","Retry remainder differs.");}else need(a.replayedStateId===null&&Number(a.remainingSegments)===segments.length,"Failure lost remainder.");
    }
  }
  need(cost<=64&&results.finalStateId===last.stateId,"Final mode checkpoint/resource mismatch.");scene(p.scene,nodes);
  return new MechanicalModeReplay(token,bytes,payloadBytes,recordingBytes,sourceBytes,replayId,deepFreeze(p) as unknown as MechanicalModePayload);
}
const token=Symbol("mechanical-mode-reader");
export class MechanicalModeReplay{
  readonly #bytes:Uint8Array;readonly #payloadBytes:Uint8Array;readonly #recordingBytes:Uint8Array;readonly #sourceBytes:Uint8Array;
  readonly payload:MechanicalModePayload;readonly replayId:string;readonly samples:readonly MechanicalModeSample[];
  constructor(key:symbol,bytes:Uint8Array,payload:Uint8Array,recording:Uint8Array,source:Uint8Array,id:string,data:MechanicalModePayload){need(key===token,"Use readMechanicalModeReplay.");this.#bytes=bytes;this.#payloadBytes=payload;this.#recordingBytes=recording;this.#sourceBytes=source;this.payload=data;this.replayId=id;this.samples=Object.freeze([data.results.initial,...data.results.attempts.flatMap(a=>a.state?[...a.samples,a.state]:[])]);Object.freeze(this);}
  originalBytes():Uint8Array{return this.#bytes.slice();}
  selectSample(index:number):MechanicalModeSample{need(Number.isInteger(index)&&index>=0&&index<this.samples.length,"Recorded mode sample required; no inverse/new events.");return this.samples[index]!;}
  selectAttempt(index:number):MechanicalModeAttempt{need(Number.isInteger(index)&&index>=0&&index<this.payload.results.attempts.length,"Recorded request required.");return this.payload.results.attempts[index]!;}
  async verifyIntegrity(sha256:(bytes:Uint8Array)=>string|Promise<string>){need(typeof sha256==="function","SHA-256 provider required.");need(await sha256(this.#payloadBytes.slice())===this.replayId&&await sha256(this.#recordingBytes.slice())===this.payload.recordingId&&await sha256(this.#sourceBytes.slice())===this.payload.sourceArtifactId,"Raw digest mismatch.");return Object.freeze({rawDigest:"Pass",storedProducerValidation:"Finalized",browserMechanicalValidation:"notPerformed",currentSourceRebuild:"notPerformed"});}
}
