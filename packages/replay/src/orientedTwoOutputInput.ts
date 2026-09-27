import type { OrientedTwoOutputArtifact, OrientedTwoOutputRequestObservation, OrientedOutputRole, OrientedOutputBinding } from './orientedTwoOutputTypes.js';
import { ORIENTED_LIMITS, assertOrientedJson, readOrientedJsonText } from './orientedJson.js';
import { orientedReaders, decodeOrientedSource } from './orientedPrimitives.js';
import { deepFreeze } from './immutable.js';

export const ORIENTED_TWO_OUTPUT_PROFILE='cardinal-shared-shaft-two-output-transmission-v1';
// C#19A bounded codec integers are Int32; tessellation/display has a separate budget.
export const ORIENTED_TWO_OUTPUT_LIMITS=Object.freeze({...ORIENTED_LIMITS,shafts:32,bodies:34,contacts:31,ports:6,outputs:2,idCharacters:256,teeth:2147483647});
const L=ORIENTED_TWO_OUTPUT_LIMITS;
const {bad,rec,str,kind,bool,array,frac,phase,optional,vector,frame,shaft,port,body,contact,keepOut,placement,mount,lambda,storedValidation,hash,unique,reference,checkGraphReferences}=orientedReaders(L);
const owned=new WeakSet<OrientedTwoOutputArtifact>();
const role=(v:unknown):OrientedOutputRole=>kind(v,['ParallelBranch','TurnedBranch']);
function outputs<T extends {readonly key:string;readonly role:OrientedOutputRole}>(v:unknown,parse:(v:unknown)=>T):readonly T[]{
  const a=array(v,L.outputs,parse);
  if(a.length!==2||new Set(a.map(o=>o.key)).size!==2||new Set(a.map(o=>o.role)).size!==2)return bad('Exactly two independent output keys and one of each role required.');
  return a;
}
function request(v:unknown):OrientedTwoOutputRequestObservation {
  const r=rec(v,'format formatVersion profile bevel parallelBranch turnedBranch outputs assemblyPose requireCrossComponentClearance keepOuts');
  const b=rec(r.bevel,'profile apex input output innerParameter requestedTransfer');
  const parallelBranch=placement(r.parallelBranch);if(!parallelBranch)return bad('Mandatory parallel source placement absent.');
  return {format:kind(r.format,['gear-invest.oriented-two-output-request']),formatVersion:kind(r.formatVersion,['0.1']),profile:kind(r.profile,[ORIENTED_TWO_OUTPUT_PROFILE]),
    bevel:{profile:kind(b.profile,['cardinal-right-angle-pitch-cone-transmission-v1']),apex:vector(b.apex),input:mount(b.input),output:mount(b.output),innerParameter:lambda(b.innerParameter),requestedTransfer:optional(b.requestedTransfer)},
    parallelBranch,turnedBranch:placement(r.turnedBranch),outputs:outputs(r.outputs,v=>{const o=rec(v,'key role terminalBodyId terminalPort requestedTransfer');return {key:str(o.key),role:role(o.role),terminalBodyId:str(o.terminalBodyId),terminalPort:port(o.terminalPort),requestedTransfer:optional(o.requestedTransfer)};}),
    assemblyPose:frame(r.assemblyPose),requireCrossComponentClearance:bool(r.requireCrossComponentClearance),keepOuts:array(r.keepOuts,L.keepOuts,keepOut)};
}
export function parseOrientedTwoOutputArtifactText(text:string):OrientedTwoOutputArtifact{return parseOrientedTwoOutputArtifact(readOrientedJsonText(text,L));}
export function parseOrientedTwoOutputArtifact(value:unknown):OrientedTwoOutputArtifact {
  assertOrientedJson(value,L);
  if(!value||typeof value!=='object'||Array.isArray(value))return bad('Expected two-output artifact.');
  kind((value as Record<string,unknown>).format,['gear-invest.oriented-two-output-mechanism']);
  const r=rec(value,'format formatVersion candidateId artifactHash request mechanism validation');
  const m=rec(r.mechanism,'profile rootShaftId outputs shafts bodies contacts ports connections sourceMappings sources solution requireCrossComponentClearance keepOuts');
  const sol=rec(m.solution,'rootDofId states');
  const result:OrientedTwoOutputArtifact={format:'gear-invest.oriented-two-output-mechanism',formatVersion:kind(r.formatVersion,['0.1']),candidateId:hash(r.candidateId),artifactHash:hash(r.artifactHash),request:request(r.request),validation:storedValidation(r.validation),
    mechanism:{profile:kind(m.profile,[ORIENTED_TWO_OUTPUT_PROFILE]),rootShaftId:str(m.rootShaftId),
      outputs:outputs<OrientedOutputBinding>(m.outputs,v=>{const o=rec(v,'key role shaftId bodyId portId');return {key:str(o.key),role:role(o.role),shaftId:str(o.shaftId),bodyId:str(o.bodyId),portId:str(o.portId)};}),
      shafts:array(m.shafts,L.shafts,shaft),bodies:array(m.bodies,L.bodies,body),contacts:array(m.contacts,L.contacts,contact),ports:array(m.ports,L.ports,port),
      connections:array(m.connections,L.connections,v=>{const c=rec(v,'id kind portAId portBId coordinateTransfer');return {id:str(c.id),kind:kind(c.kind,['RigidZeroPhase']),portAId:str(c.portAId),portBId:str(c.portBId),coordinateTransfer:frac(c.coordinateTransfer)};}),
      sourceMappings:array(m.sourceMappings,L.mappings,v=>{const s=rec(v,'moduleId sourceDofId shaftId coordinateTransfer');return {moduleId:str(s.moduleId),sourceDofId:str(s.sourceDofId),shaftId:str(s.shaftId),coordinateTransfer:frac(s.coordinateTransfer)};}),
      sources:array(m.sources,L.sources,v=>{const s=rec(v,'moduleId candidateId artifactHash');return {moduleId:str(s.moduleId),candidateId:str(s.candidateId,256),artifactHash:str(s.artifactHash,256)};}),
      solution:{rootDofId:str(sol.rootDofId),states:array(sol.states,L.shafts,v=>{const s=rec(v,'dofId coefficient phaseOffset');return {dofId:str(s.dofId),coefficient:frac(s.coefficient),phaseOffset:phase(s.phaseOffset)};})},
      requireCrossComponentClearance:bool(m.requireCrossComponentClearance),keepOuts:array(m.keepOuts,L.keepOuts,keepOut)}};
  const a=result.mechanism;
  checkGraphReferences(a);unique(result.request.keepOuts,k=>k.id);
  unique(a.outputs,o=>o.shaftId);unique(a.outputs,o=>o.bodyId);unique(a.outputs,o=>o.portId);
  for(const o of a.outputs){
    reference(new Set(a.shafts.map(s=>s.id)),o.shaftId);reference(new Set(a.bodies.map(b=>b.id)),o.bodyId);reference(new Set(a.ports.map(p=>p.id)),o.portId);
    const s=a.shafts.find(s=>s.id===o.shaftId)!,b=a.bodies.find(b=>b.id===o.bodyId)!,p=a.ports.find(p=>p.id===o.portId)!;
    if(b.shaftId!==o.shaftId||p.shaftId!==o.shaftId||o.shaftId===a.rootShaftId)return bad('Output body/port/terminal shaft references disagree.');
    // Exact signed-cardinal dot product is a coordinate conversion, NOT contact validation.
    const sign=p.frame.z.reduce((sum,f,i)=>sum+Number(f.numerator)*Number(s.frame.z[i]!.numerator),0);
    if(sign!==1&&sign!==-1)return bad('Output port coordinate must be collinear with its shaft axis.');
    if(!result.request.outputs.some(r=>r.key===o.key&&r.role===o.role))return bad('Request and final output key/role coverage disagree.');
  }
  deepFreeze(result);owned.add(result);return result;
}
export function assertOrientedTwoOutputArtifact(a:OrientedTwoOutputArtifact):void{if(!owned.has(a))bad('Parse the original two-output artifact before compilation.');}
export function readOrientedTwoOutputSourceBytes(a:OrientedTwoOutputArtifact,branch:'parallelBranch'|'turnedBranch'):Uint8Array|null {
  assertOrientedTwoOutputArtifact(a);const p=a.request[branch];return p?decodeOrientedSource(p.sourceArtifactUtf8):null;
}
