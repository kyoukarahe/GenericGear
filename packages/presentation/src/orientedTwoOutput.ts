import { OrientedTwoOutputPlaybackEvaluator } from '@gearinvest/replay/oriented-two-output';
import type { OrientedTwoOutputInstanceFrame,OrientedOutputBinding,ReplayInstancesDocument } from '@gearinvest/replay/oriented-two-output';
import { exactVector3,add3,sub3,scale3 } from '@gearinvest/replay/oriented';
import { PreparedOrientedGeometry,OrientedPresentationError,freeze,rec,readOrientedViewFields,parseOrientedPlacement,parseOrientedCamera,rotateDisplayVector,displayPoint,DEFAULT_ORIENTED_PLACEMENT,DEFAULT_ORIENTED_CAMERA,DEFAULT_ORIENTED_ANNOTATIONS } from './orientedGeometry.js';
import type { OrientedProjection,OrientedPrimitive,OrientedPlacement,OrientedCamera,OrientedAnnotations,ProjectedPoint3 } from './orientedGeometry.js';

export { OrientedPresentationError,parseOrientedPlacement,parseOrientedCamera,DEFAULT_ORIENTED_PLACEMENT,DEFAULT_ORIENTED_CAMERA,DEFAULT_ORIENTED_ANNOTATIONS,ORIENTED_PRESENTATION_PROFILE,fitOrientedProjection,rotateDisplayVector,displayPoint } from './orientedGeometry.js';
export type { Orientation3,OrientedPlacement,OrientedCamera,OrientedAnnotations,ProjectedPoint3,OrientedPrimitive,OrientedProjection } from './orientedGeometry.js';
export interface OrientedTwoOutputSelection {readonly instanceId:string;readonly kind:'body'|'shaft'|'port'|'output';readonly id:string;}
export interface OrientedTwoOutputViewDocument {
  readonly format:'gear-invest.oriented-two-output-replay-view';readonly formatVersion:'0.1';
  readonly originalArtifactFormat:'gear-invest.oriented-two-output-mechanism';readonly originalRawSha256:string;
  readonly instances:ReplayInstancesDocument;
  readonly sourceValues:readonly {readonly id:string;readonly turns:number}[];
  readonly placements:readonly {readonly instanceId:string;readonly placement:OrientedPlacement}[];
  readonly camera:OrientedCamera;readonly annotations:OrientedAnnotations;readonly selection:OrientedTwoOutputSelection|null;
}
export interface ProjectedOrientedOutput extends OrientedOutputBinding {readonly station:ProjectedPoint3;readonly axisEnd:ProjectedPoint3;readonly zeroRayEnd:ProjectedPoint3;}
export interface OrientedTwoOutputProjection extends OrientedProjection {readonly outputs:readonly ProjectedOrientedOutput[];}
export class PreparedOrientedTwoOutputPresentation extends PreparedOrientedGeometry<OrientedTwoOutputPlaybackEvaluator> {
  constructor(definition:OrientedTwoOutputPlaybackEvaluator,segments=64){
    if(!(definition instanceof OrientedTwoOutputPlaybackEvaluator))throw new OrientedPresentationError('DEFINITION_MISMATCH','Supply a compiled two-output definition.');
    const m=definition.artifact.mechanism,colors=new Map<string,string>();
    for(const o of m.outputs){const terminal=m.bodies.find(b=>b.id===o.bodyId)!;for(const b of m.bodies)if(b.kind==='PlanarSpur'&&b.sourceModuleId===terminal.sourceModuleId)colors.set(b.id,o.role==='ParallelBranch'?'#3d92ba':'#6d984c');}
    if(m.bodies.some(b=>Number(b.teeth)>4096))throw new OrientedPresentationError('DISPLAY_TEETH_LIMIT','Display supports up to 4096 teeth; this does not invalidate the mechanism.');
    super(definition,segments,colors);
  }
  override project(frame:OrientedTwoOutputInstanceFrame,placement:OrientedPlacement=DEFAULT_ORIENTED_PLACEMENT,camera:OrientedCamera=DEFAULT_ORIENTED_CAMERA,flags:OrientedAnnotations=DEFAULT_ORIENTED_ANNOTATIONS):OrientedTwoOutputProjection {
    const base=super.project(frame,placement,camera,flags);placement=parseOrientedPlacement(placement);camera=parseOrientedCamera(camera);
    const point=(v:readonly [number,number,number]):ProjectedPoint3=>{const p=rotateDisplayVector(sub3(displayPoint(v,placement),camera.target),camera.rotation);return freeze({x:p[0],y:-p[1],depth:p[2]});};
    const primitives:OrientedPrimitive[]=[...base.primitives];
    const outputs=this.definition.artifact.mechanism.outputs.map(o=>{
      const port=this.definition.artifact.mechanism.ports.find(p=>p.id===o.portId)!,origin=exactVector3(port.frame.origin);
      const axis=exactVector3(port.frame.z),zero=exactVector3(port.frame.x);
      const station=point(origin),axisEnd=point(add3(origin,scale3(axis,12))),zeroRayEnd=point(add3(origin,scale3(zero,8)));
      if(flags.ports){const end=add3(origin,scale3(axis,12));const pts=[station,axisEnd,point(add3(sub3(end,scale3(axis,4)),scale3(zero,2))),axisEnd,point(sub3(sub3(end,scale3(axis,4)),scale3(zero,2))),station,zeroRayEnd];
        primitives.push(freeze({instanceId:frame.instanceId,kind:'port',id:o.portId,bodyId:o.bodyId,shaftId:o.shaftId,points:pts,depth:pts.reduce((s,p)=>s+p.depth,0)/pts.length,color:o.role==='ParallelBranch'?'#63dcdf':'#ff94c2'}));}
      return freeze({...o,station,axisEnd,zeroRayEnd});
    });
    const points=primitives.flatMap(p=>p.points),xs=points.map(p=>p.x),ys=points.map(p=>p.y);
    return freeze({...base,primitives,outputs,bounds:{minX:Math.min(...xs),minY:Math.min(...ys),maxX:Math.max(...xs),maxY:Math.max(...ys)}});
  }
}
export function prepareOrientedTwoOutputPresentation(definition:OrientedTwoOutputPlaybackEvaluator,segments=64){return new PreparedOrientedTwoOutputPresentation(definition,segments);}
export function inspectOrientedTwoOutputPresentationCompatibility(definition:OrientedTwoOutputPlaybackEvaluator){try{prepareOrientedTwoOutputPresentation(definition);return freeze({renderingCompatibility:'supported',precision:'schematicDisplayOnly'});}catch(e){if(!(e instanceof Error))throw e;return freeze({renderingCompatibility:'unsupported',diagnostic:e.message});}}
export function parseOrientedTwoOutputViewDocument(value:unknown):OrientedTwoOutputViewDocument{
  const r=rec(value,'format formatVersion originalArtifactFormat originalRawSha256 instances sourceValues placements camera annotations selection');
  if(r.format!=='gear-invest.oriented-two-output-replay-view'||r.formatVersion!=='0.1'||r.originalArtifactFormat!=='gear-invest.oriented-two-output-mechanism')throw new OrientedPresentationError('UNSUPPORTED_VIEW_VERSION','Use the explicit two-output view format and matching original format.');
  return freeze({format:'gear-invest.oriented-two-output-replay-view',formatVersion:'0.1',originalArtifactFormat:'gear-invest.oriented-two-output-mechanism',...readOrientedViewFields(r,['body','shaft','port','output'] as const)});
}
export function parseOrientedTwoOutputViewDocumentText(text:string):OrientedTwoOutputViewDocument{
  if(typeof text!=='string'||text.length>262144)throw new OrientedPresentationError('RESOURCE_LIMIT','View text limit exceeded.');let v:unknown;
  try{v=JSON.parse(text);}catch{throw new OrientedPresentationError('INVALID_VIEW_JSON','Invalid view JSON.');}return parseOrientedTwoOutputViewDocument(v);
}
export function writeOrientedTwoOutputViewDocument(value:OrientedTwoOutputViewDocument):string{return JSON.stringify(parseOrientedTwoOutputViewDocument(value),null,2)+'\n';}

/** Resolve an instance-qualified selection against its OWN sampled definition; equal keys are not scene IDs. */
export function readOrientedTwoOutputSelection(definition:OrientedTwoOutputPlaybackEvaluator,frame:OrientedTwoOutputInstanceFrame,selection:OrientedTwoOutputSelection){
  if(selection.instanceId!==frame.instanceId||!definition.ownsFrame(frame.playback)||frame.candidateId!==definition.artifact.candidateId||frame.artifactHash!==definition.artifact.artifactHash)throw new OrientedPresentationError('SELECTION_REFERENCE_MISMATCH','Selection belongs to another instance/definition.');
  const m=definition.artifact.mechanism;
  const observation=selection.kind==='output'?frame.playback.outputs.get(selection.id):selection.kind==='body'?m.bodies.find(b=>b.id===selection.id):selection.kind==='shaft'?m.shafts.find(s=>s.id===selection.id):m.ports.find(p=>p.id===selection.id);
  if(!observation)throw new OrientedPresentationError('SELECTION_REFERENCE_MISMATCH','Unknown original output/body/shaft/port reference.');
  return freeze({...selection,observation,worldAngularVelocityPerSource:selection.kind==='output'?frame.outputWorldAngularVelocityPerSource.get(selection.id):null,rateUnit:'axis-times-turns-per-external-source-turn'});
}
