import type { OrientedProjection,OrientedSelection } from "./oriented.js";
import { drawOrientedPrimitives } from "./orientedCanvasKernel.js";
import type { OrientedCanvasBaseView } from "./orientedCanvasKernel.js";
export interface OrientedCanvasView extends OrientedCanvasBaseView {readonly selection?:OrientedSelection|null;}
export function drawOrientedCanvas2D(context:CanvasRenderingContext2D,frames:readonly OrientedProjection[],view:OrientedCanvasView):void {
  drawOrientedPrimitives(context,frames,view,p=>view.selection?.instanceId===p.instanceId&&(view.selection.kind==="body"?view.selection.id===p.bodyId:view.selection.id===p.shaftId));
}
