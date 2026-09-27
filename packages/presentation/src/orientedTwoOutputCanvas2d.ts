import type { OrientedTwoOutputProjection,OrientedTwoOutputSelection } from './orientedTwoOutput.js';
import { drawOrientedPrimitives } from './orientedCanvasKernel.js';
import type { OrientedCanvasBaseView } from './orientedCanvasKernel.js';
export interface OrientedTwoOutputCanvasView extends OrientedCanvasBaseView {readonly selection?:OrientedTwoOutputSelection|null;}
/** Explicit opt-in: caller owns context, files, clock and clearing. */
export function drawOrientedTwoOutputCanvas2D(context:CanvasRenderingContext2D,frames:readonly OrientedTwoOutputProjection[],view:OrientedTwoOutputCanvasView):void {
  drawOrientedPrimitives(context,frames,view,p=>{
    const s=view.selection;if(!s||s.instanceId!==p.instanceId)return false;
    if(s.kind==='body')return s.id===p.bodyId;
    if(s.kind==='shaft')return s.id===p.shaftId;
    if(s.kind==='port')return p.kind==='port'&&s.id===p.id;
    const output=frames.find(f=>f.instanceId===s.instanceId)?.outputs.find(o=>o.key===s.id);
    return !!output&&(p.bodyId===output.bodyId||p.kind==='port'&&p.id===output.portId);
  });
}
