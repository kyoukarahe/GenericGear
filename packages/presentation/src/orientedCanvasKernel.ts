import { OrientedPresentationError, ORIENTED_PRESENTATION_PROFILE } from "./orientedGeometry.js";
import type { OrientedProjection, OrientedPrimitive, OrientedSelection } from "./orientedGeometry.js";
export interface OrientedCanvasBaseView {readonly scale:number;readonly offsetX:number;readonly offsetY:number;readonly devicePixelRatio:number;}
/** Optional explicit renderer. Caller owns DOM/context, clearing, file IO, input, clock, lifecycle. */
export function drawOrientedPrimitives(context:CanvasRenderingContext2D,frames:readonly OrientedProjection[],view:OrientedCanvasBaseView,selected:(primitive:OrientedPrimitive)=>boolean):void{
  if(![view.scale,view.offsetX,view.offsetY,view.devicePixelRatio].every(Number.isFinite)||view.scale<1e-8||view.scale>1e6||Math.abs(view.offsetX)>1e9||Math.abs(view.offsetY)>1e9||view.devicePixelRatio<=0||view.devicePixelRatio>16)
    throw new OrientedPresentationError("INVALID_CANVAS_VIEW","Invalid bounded Canvas view.");
  const all=frames.flatMap(f=>f.primitives);
  if(frames.length>128||all.reduce((n,p)=>n+p.points.length,0)>ORIENTED_PRESENTATION_PROFILE.maxVertices)throw new OrientedPresentationError("RESOURCE_LIMIT","Canvas scene budget exceeded.");
  // Global per-triangle/line sorting across all bodies/instances. Both sides filled;
  // painter ordering is schematic and cannot guarantee arbitrary intersecting solid occlusion.
  all.sort((a,b)=>a.depth-b.depth||(a.kind==="surface"?0:1)-(b.kind==="surface"?0:1)||(a.instanceId<b.instanceId?-1:a.instanceId>b.instanceId?1:a.id<b.id?-1:a.id>b.id?1:0));
  const path=(p:OrientedPrimitive)=>{context.beginPath();const first=p.points[0]!;context.moveTo(first.x,first.y);for(const v of p.points.slice(1))context.lineTo(v.x,v.y);};
  context.save();
  try{
    const d=view.devicePixelRatio;context.setTransform(d*view.scale,0,0,d*view.scale,d*view.offsetX,d*view.offsetY);
    context.globalAlpha=1;context.globalCompositeOperation="source-over";context.setLineDash([]);context.lineJoin="round";context.lineCap="round";
    for(const p of all){path(p);context.strokeStyle=selected(p)?"#ffffff":p.color;context.fillStyle=p.color;
      const width=p.kind==="marker"||p.kind==="rib"?2.8:p.kind==="generator"?2.4:selected(p)?2:1.1;context.lineWidth=width/view.scale;
      if(p.kind==="surface"){context.closePath();context.fill();}else if(p.kind==="apex"){const v=p.points[0]!;context.arc(v.x,v.y,3/view.scale,0,Math.PI*2);context.fill();}else context.stroke();
    }
  }finally{context.restore();}
}
