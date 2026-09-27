import { exactVector3, exactWorldNumber, vector3, add3, sub3, scale3, dot3,
  parseReplayInstancesDocument, normalizeTurns } from "@gearinvest/replay/oriented";
import type { Vector3, OrientedInstanceFrame, OrientedBodyFrame, ReplayInstancesDocument, OrientedMechanismObservation, OrientedPlaybackFrame } from "@gearinvest/replay/oriented";

export interface Orientation3 { readonly yawTurns:number; readonly pitchTurns:number; readonly rollTurns:number; }
export interface OrientedPlacement { readonly translation:Vector3; readonly rotation:Orientation3; readonly scale:number; }
export interface OrientedCamera { readonly target:Vector3; readonly rotation:Orientation3; }
export interface OrientedAnnotations { readonly shafts:boolean; readonly ports:boolean; readonly generators:boolean; }
export interface OrientedSelection { readonly instanceId:string; readonly kind:"body"|"shaft"; readonly id:string; }
export interface OrientedViewDocument {
  readonly format:"gear-invest.oriented-replay-view"; readonly formatVersion:"0.1"; readonly originalRawSha256:string;
  readonly instances:ReplayInstancesDocument;
  readonly sourceValues:readonly {readonly id:string;readonly turns:number}[];
  readonly placements:readonly {readonly instanceId:string;readonly placement:OrientedPlacement}[];
  readonly camera:OrientedCamera; readonly annotations:OrientedAnnotations; readonly selection:OrientedSelection|null;
}
export class OrientedPresentationError extends Error { constructor(readonly code:string,message:string){super(message);this.name="OrientedPresentationError";} }
function fail(code:string,message:string):never {throw new OrientedPresentationError(code,message);}
export function freeze<T>(value:T):T {if(value&&typeof value==="object"&&!Object.isFrozen(value)){Object.values(value).forEach(freeze);Object.freeze(value);}return value;}
export function rec(value:unknown,keys:string):Record<string,unknown>{
  if(!value||typeof value!=="object"||Array.isArray(value)||(Object.getPrototypeOf(value)!==Object.prototype&&Object.getPrototypeOf(value)!==null))return fail("INVALID_VIEW","Expected a plain view record.");
  const names=keys.split(" "),actual=Reflect.ownKeys(value);
  if(actual.length!==names.length||actual.some(k=>typeof k!=="string"||!names.includes(k)||!Object.hasOwn(Object.getOwnPropertyDescriptor(value,k)!,"value")))return fail("INVALID_VIEW","Unknown/missing/accessor view field.");
  return value as Record<string,unknown>;
}
function number(v:unknown,max=1e8):number {if(typeof v!=="number"||!Number.isFinite(v)||Math.abs(v)>max)return fail("PROJECTION_NUMERIC_RANGE","Expected finite bounded display number.");return Object.is(v,-0)?0:v;}
function array(v:unknown,max:number):readonly unknown[]{
  if(!Array.isArray(v)||Object.getPrototypeOf(v)!==Array.prototype||v.length>max||Reflect.ownKeys(v).length!==v.length+1||
    Reflect.ownKeys(v).some(k=>k!=="length"&&(typeof k!=="string"||!/^(0|[1-9][0-9]*)$/.test(k)||Number(k)>=v.length||!Object.hasOwn(Object.getOwnPropertyDescriptor(v,k)!,"value"))))return fail("RESOURCE_LIMIT","Expected bounded dense data array.");return v;
}
function id(v:unknown):string{if(typeof v!=="string"||!v||v.trim()!==v||v.length>256)return fail("INVALID_VIEW","Invalid view ID.");return v;}
function vec(v:unknown):Vector3{const a=array(v,3);if(a.length!==3)return fail("INVALID_VIEW","Expected 3-vector.");return vector3(number(a[0]),number(a[1]),number(a[2]));}
function orientation(v:unknown):Orientation3{const r=rec(v,"yawTurns pitchTurns rollTurns");return freeze({yawTurns:number(r.yawTurns,2**26),pitchTurns:number(r.pitchTurns,2**26),rollTurns:number(r.rollTurns,2**26)});}
export function parseOrientedPlacement(v:unknown):OrientedPlacement{const r=rec(v,"translation rotation scale"),scale=number(r.scale,1e4);if(scale<1e-6)return fail("INVALID_VIEW","Display scale must be 1e-6..1e4.");return freeze({translation:vec(r.translation),rotation:orientation(r.rotation),scale});}
export function parseOrientedCamera(v:unknown):OrientedCamera{const r=rec(v,"target rotation");return freeze({target:vec(r.target),rotation:orientation(r.rotation)});}
function annotations(v:unknown):OrientedAnnotations {const r=rec(v,"shafts ports generators");if(Object.values(r).some(v=>typeof v!=="boolean"))return fail("INVALID_VIEW","Annotation flags must be boolean.");return freeze({shafts:r.shafts as boolean,ports:r.ports as boolean,generators:r.generators as boolean});}
export const DEFAULT_ORIENTED_PLACEMENT:OrientedPlacement=freeze({translation:[0,0,0],rotation:{yawTurns:0,pitchTurns:0,rollTurns:0},scale:1});
export const DEFAULT_ORIENTED_CAMERA:OrientedCamera=freeze({target:[0,0,0],rotation:{yawTurns:0.05,pitchTurns:0.11,rollTurns:0.08}});
export const DEFAULT_ORIENTED_ANNOTATIONS:OrientedAnnotations=freeze({shafts:true,ports:true,generators:true});
export const ORIENTED_PRESENTATION_PROFILE=freeze({id:"oriented-orthographic-pitch-sheets/0.1",segments:64,maxSegments:128,maxInstances:128,maxVertices:262144,
  surface:"twoSidedSheets",occlusion:"perTrianglePainterOrderNotSolidVisibility",worldUnit:"artifactTicks",certifiedPixelError:false});

/** Same explicit right-handed column-vector Rz(yaw) Ry(pitch) Rx(roll) for view rotation and display placement. */
export function rotateDisplayVector(v:Vector3,r:Orientation3):Vector3 {
  const trig=(t:number)=>[Math.cos(normalizeTurns(t)*2*Math.PI),Math.sin(normalizeTurns(t)*2*Math.PI)] as const;
  const [cx,sx]=trig(r.rollTurns),[cy,sy]=trig(r.pitchTurns),[cz,sz]=trig(r.yawTurns);
  const y=cx*v[1]-sx*v[2],z=sx*v[1]+cx*v[2],x=cy*v[0]+sy*z;
  return vector3(cz*x-sz*y,sz*x+cz*y,-sy*v[0]+cy*z);
}
export function displayPoint(point:Vector3,placement:OrientedPlacement):Vector3{return add3(placement.translation,rotateDisplayVector(scale3(point,placement.scale),placement.rotation));}
export interface ProjectedPoint3 {readonly x:number;readonly y:number;readonly depth:number;}
export interface OrientedPrimitive {
  readonly instanceId:string; readonly kind:"surface"|"ring"|"marker"|"rib"|"shaft"|"port"|"generator"|"apex";
  readonly id:string; readonly bodyId:string|null; readonly shaftId:string|null;
  readonly points:readonly ProjectedPoint3[]; readonly depth:number; readonly color:string;
}
export interface OrientedProjection {
  readonly instanceId:string; readonly primitives:readonly OrientedPrimitive[];
  readonly bodies:readonly {readonly bodyId:string;readonly shaftId:string;readonly center:ProjectedPoint3;readonly marker:ProjectedPoint3}[];
  readonly bounds:{readonly minX:number;readonly minY:number;readonly maxX:number;readonly maxY:number};
  readonly semantics:"schematicDisplayOnly";
}
interface GeometryBody {id:string;shaftId:string;radius:number;outer:readonly Vector3[];inner:readonly Vector3[]|null;color:string;}
interface Annotation {kind:"shaft"|"port"|"generator"|"apex";id:string;shaftId:string|null;points:readonly Vector3[];color:string;}
const framePoint=(f:OrientedBodyFrame,v:Vector3)=>add3(f.origin,add3(add3(scale3(f.x,v[0]),scale3(f.y,v[1])),scale3(f.z,v[2])));
/** Internal geometry kernel. Format-specific public constructors guard definition ownership. */
export interface OrientedGeometryDefinition {
  readonly artifact:{readonly candidateId:string;readonly artifactHash:string;readonly mechanism:Pick<OrientedMechanismObservation,"bodies"|"shafts"|"ports"|"contacts">};
  ownsFrame(frame:OrientedPlaybackFrame):boolean;
}
export class PreparedOrientedGeometry<D extends OrientedGeometryDefinition> {
  readonly definition:D; readonly segments:number;
  readonly #bodies:readonly GeometryBody[]; readonly #annotations:readonly Annotation[];
  constructor(definition:D,segments=64,colors:ReadonlyMap<string,string>=new Map()){
    if(!Number.isInteger(segments)||segments<12||segments>128)fail("TESSELLATION_LIMIT","Use 12..128 segments; no clamping.");
    this.definition=definition;this.segments=segments; const m=definition.artifact.mechanism;
    this.#bodies=m.bodies.map(b=>{
      const r=exactWorldNumber(b.outerPitchRadius),c=m.contacts.find(c=>c.cone&&(c.bodyAId===b.id||c.bodyBId===b.id))?.cone;
      const ring=(radius:number,center:Vector3)=>Array.from({length:segments},(_,i)=>add3(center,vector3(radius*Math.cos(i/segments*2*Math.PI),radius*Math.sin(i/segments*2*Math.PI),0)));
      let inner:readonly Vector3[]|null=null;
      if(c){const lambda=exactWorldNumber(c.innerParameter),apex=exactVector3(c.apex),origin=exactVector3(b.mountingFrame.origin);
        const delta=sub3(add3(apex,scale3(sub3(origin,apex),lambda)),origin);
        const center=vector3(dot3(delta,exactVector3(b.mountingFrame.x)),dot3(delta,exactVector3(b.mountingFrame.y)),dot3(delta,exactVector3(b.mountingFrame.z)));
        if(r*lambda<1e-8)fail("GEOMETRY_FEATURE_RANGE","Inner pitch radius is below 1e-8 ticks.");inner=ring(r*lambda,center);}
      return {id:b.id,shaftId:b.shaftId,radius:r,outer:ring(r,vector3(0,0,0)),inner,
        color:colors.get(b.id)??(b.sourceModuleId==="pre"?"#3d92ba":b.sourceModuleId==="post"?"#6d984c":b.id===m.contacts.find(c=>c.cone)!.bodyAId?"#c0803f":"#8f63b0")};
    });
    const lines:Annotation[]=[];
    for(const s of m.shafts){const o=exactVector3(s.frame.origin),a=exactVector3(s.frame.z),x=exactVector3(s.frame.x);
      const stations=[o,...m.bodies.filter(b=>b.shaftId===s.id).map(b=>exactVector3(b.mountingFrame.origin)),...m.ports.filter(p=>p.shaftId===s.id).map(p=>exactVector3(p.frame.origin))];
      const ts=stations.map(p=>dot3(sub3(p,o),a)),end=add3(o,scale3(a,Math.max(...ts)+9));
      lines.push({kind:"shaft",id:s.id,shaftId:s.id,color:"#a5bdce",points:[add3(o,scale3(a,Math.min(...ts)-6)),end]},
        {kind:"shaft",id:s.id+"/arrow",shaftId:s.id,color:"#a5bdce",points:[add3(sub3(end,scale3(a,4)),scale3(x,2)),end,sub3(sub3(end,scale3(a,4)),scale3(x,2))]});
    }
    for(const p of m.ports){const o=exactVector3(p.frame.origin),x=scale3(exactVector3(p.frame.x),1.5),y=scale3(exactVector3(p.frame.y),1.5);
      lines.push({kind:"port",id:p.id,shaftId:p.shaftId,color:"#63dcdf",points:[sub3(o,x),add3(o,x),o,sub3(o,y),add3(o,y)]});}
    for(const c of m.contacts)if(c.cone){const a=exactVector3(c.cone.apex),q=exactVector3(c.cone.outerContact);
      lines.push({kind:"generator",id:c.id,shaftId:null,color:"#f1d965",points:[add3(a,scale3(sub3(q,a),exactWorldNumber(c.cone.innerParameter))),q]},
        {kind:"apex",id:c.id+"/apex",shaftId:null,color:"#e985c7",points:[a]});}
    this.#annotations=lines;Object.freeze(this);
  }
  project(frame:OrientedInstanceFrame,placement:OrientedPlacement=DEFAULT_ORIENTED_PLACEMENT,camera:OrientedCamera=DEFAULT_ORIENTED_CAMERA,flags:OrientedAnnotations=DEFAULT_ORIENTED_ANNOTATIONS):OrientedProjection{
    if(!this.definition.ownsFrame(frame.playback)||frame.candidateId!==this.definition.artifact.candidateId||frame.artifactHash!==this.definition.artifact.artifactHash)return fail("FRAME_MISMATCH","Frame is not from this oriented definition.");
    placement=parseOrientedPlacement(placement);camera=parseOrientedCamera(camera);flags=annotations(flags);
    const point=(p:Vector3):ProjectedPoint3=>{const v=rotateDisplayVector(sub3(displayPoint(p,placement),camera.target),camera.rotation);return freeze({x:v[0],y:-v[1],depth:v[2]});};
    const primitives:OrientedPrimitive[]=[],bodies:OrientedProjection["bodies"][number][]=[];
    const emit=(kind:OrientedPrimitive["kind"],id:string,bodyId:string|null,shaftId:string|null,points:readonly Vector3[],color:string)=>{
      const projected=points.map(point);primitives.push(freeze({instanceId:frame.instanceId,kind,id,bodyId,shaftId,points:projected,depth:projected.reduce((s,p)=>s+p.depth,0)/projected.length,color}));};
    for(const b of this.#bodies){const f=frame.playback.bodies.get(b.id)!;
      const outer=b.outer.map(v=>framePoint(f,v)),inner=b.inner?.map(v=>framePoint(f,v));
      for(let i=0;i<this.segments;i++){const j=(i+1)%this.segments;
        if(inner){emit("surface",b.id+"/a/"+i,b.id,b.shaftId,[outer[i]!,outer[j]!,inner[i]!],b.color);emit("surface",b.id+"/b/"+i,b.id,b.shaftId,[outer[j]!,inner[j]!,inner[i]!],b.color);}
        else emit("surface",b.id+"/"+i,b.id,b.shaftId,[f.origin,outer[i]!,outer[j]!],b.color);
      }
      emit("ring",b.id,b.id,b.shaftId,[...outer,outer[0]!],"#b8cedc");
      if(inner){emit("ring",b.id+"/inner",b.id,b.shaftId,[...inner,inner[0]!],"#aec5d4");emit("rib",b.id+"/rib",b.id,b.shaftId,[outer[0]!,inner[0]!],"#fff1bf");}
      emit("marker",b.id+"/phase",b.id,b.shaftId,[f.origin,f.outerMarker],"#fff1bf");
      bodies.push(freeze({bodyId:b.id,shaftId:b.shaftId,center:point(f.origin),marker:point(f.outerMarker)}));
    }
    for(const a of this.#annotations)if(a.kind==="shaft"?flags.shafts:a.kind==="port"?flags.ports:flags.generators)emit(a.kind,a.id,null,a.shaftId,a.points,a.color);
    const points=primitives.flatMap(p=>p.points); if(points.length>ORIENTED_PRESENTATION_PROFILE.maxVertices)return fail("RESOURCE_LIMIT","Projected vertex budget exceeded.");
    const xs=points.map(p=>p.x),ys=points.map(p=>p.y);
    return freeze({instanceId:frame.instanceId,primitives,bodies,bounds:{minX:Math.min(...xs),minY:Math.min(...ys),maxX:Math.max(...xs),maxY:Math.max(...ys)},semantics:"schematicDisplayOnly"});
  }
}
export function fitOrientedProjection(frames:readonly OrientedProjection[],width:number,height:number,padding=28){
  width=number(width,32768);height=number(height,32768);padding=number(padding,1024);
  if(width<=2*padding||height<=2*padding||padding<0||!frames.length||frames.length>128)return fail("INVALID_CAMERA_VIEWPORT","Nonempty scene and usable viewport required.");
  const minX=Math.min(...frames.map(f=>f.bounds.minX)),minY=Math.min(...frames.map(f=>f.bounds.minY)),maxX=Math.max(...frames.map(f=>f.bounds.maxX)),maxY=Math.max(...frames.map(f=>f.bounds.maxY));
  // Individual bodies may be edge-on. Only an entirely degenerate scene has no 2D fit extent.
  const extentX=maxX-minX,extentY=maxY-minY;if(Math.max(extentX,extentY)<1e-8)return fail("DEGENERATE_PROJECTION","Entire projection is degenerate.");
  const scale=Math.min((width-2*padding)/(extentX||1e-8),(height-2*padding)/(extentY||1e-8));
  if(!Number.isFinite(scale)||scale>1e6||scale<1e-8)return fail("PROJECTION_NUMERIC_RANGE","Fit scale outside 1e-8..1e6 pixels/tick.");
  return freeze({scale,offsetX:width/2-(minX+maxX)/2*scale,offsetY:height/2-(minY+maxY)/2*scale});
}
/** Internal view/input/placement lifecycle fields shared without changing either document discriminator. */
export function readOrientedViewFields<K extends string>(r:Record<string,unknown>,kinds:readonly K[]){
  const digest=id(r.originalRawSha256);if(!/^[a-f0-9]{64}$/.test(digest))return fail("INVALID_VIEW","Original raw SHA-256 required.");
  const instances=parseReplayInstancesDocument(r.instances),ids=new Set(instances.instances.map(i=>i.instanceId));
  const placements=array(r.placements,128).map(v=>{const p=rec(v,"instanceId placement");return {instanceId:id(p.instanceId),placement:parseOrientedPlacement(p.placement)};});
  if(placements.length!==ids.size||new Set(placements.map(p=>p.instanceId)).size!==ids.size||placements.some(p=>!ids.has(p.instanceId)))return fail("VIEW_REFERENCE_MISMATCH","Each instance needs one display placement.");
  const sourceValues=array(r.sourceValues,128).map(v=>{const s=rec(v,"id turns");return {id:id(s.id),turns:number(s.turns,2**26)};});
  if(new Set(sourceValues.map(s=>s.id)).size!==sourceValues.length)return fail("INVALID_VIEW","Duplicate source ID.");
  let selection:{readonly instanceId:string;readonly kind:K;readonly id:string}|null=null;
  if(r.selection!==null){const s=rec(r.selection,"instanceId kind id");if(typeof s.kind!=="string"||!kinds.includes(s.kind as K))return fail("INVALID_VIEW","Unknown selection kind.");selection={instanceId:id(s.instanceId),kind:s.kind as K,id:id(s.id)};if(!ids.has(selection.instanceId))return fail("VIEW_REFERENCE_MISMATCH","Unknown selected instance.");}
  return freeze({originalRawSha256:digest,instances,sourceValues,placements,camera:parseOrientedCamera(r.camera),annotations:annotations(r.annotations),selection});
}
export function parseOrientedViewDocument(value:unknown):OrientedViewDocument{
  const r=rec(value,"format formatVersion originalRawSha256 instances sourceValues placements camera annotations selection");
  if(r.format!=="gear-invest.oriented-replay-view"||r.formatVersion!=="0.1")return fail("UNSUPPORTED_VIEW_VERSION","Unsupported oriented view version.");
  return freeze({format:"gear-invest.oriented-replay-view",formatVersion:"0.1",...readOrientedViewFields(r,["body","shaft"] as const)});
}
export function writeOrientedViewDocument(value:OrientedViewDocument):string{return JSON.stringify(parseOrientedViewDocument(value),null,2)+"\n";}
export function parseOrientedViewDocumentText(text:string):OrientedViewDocument{
  if(typeof text!=="string"||text.length>262144)return fail("RESOURCE_LIMIT","View text limit exceeded.");let v:unknown;
  try{v=JSON.parse(text);}catch{return fail("INVALID_VIEW_JSON","Invalid view JSON.");}return parseOrientedViewDocument(v);
}
