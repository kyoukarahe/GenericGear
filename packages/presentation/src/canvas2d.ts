import { PresentationInputError } from "./index.js";
import type { Point2, ProjectedInstance } from "./index.js";
export interface CanvasView {
  readonly offsetX: number; readonly offsetY: number; readonly scale: number; readonly devicePixelRatio: number;
  readonly selected?: { readonly instanceId: string; readonly bodyId: string };
}
/** Caller owns canvas creation, sizing/clearing, CSS pixels, clock and input. Always restores context state. */
export function drawCanvas2D(context: CanvasRenderingContext2D, frames: readonly ProjectedInstance[], view: CanvasView): void {
  if (![view.offsetX, view.offsetY, view.scale, view.devicePixelRatio].every(Number.isFinite) || view.scale <= 0 ||
      Math.abs(view.offsetX) > 1e9 || Math.abs(view.offsetY) > 1e9 || view.scale > 1e6 || view.devicePixelRatio <= 0 || view.devicePixelRatio > 16)
    throw new PresentationInputError("INVALID_VIEW", "Canvas view must have finite offsets and positive bounded scale/DPR.");
  if (frames.length > 128 || frames.reduce((n, f) => n + f.bodies.length, 0) > 8192 ||
      frames.reduce((n, f) => n + f.bodies.reduce((v, b) => v + b.front.length + b.back.length, 0), 0) > 1048576)
    throw new PresentationInputError("RESOURCE_LIMIT", "Canvas scene exceeds its instance/body/vertex budget.");
  function path(points: readonly Point2[], closed: boolean): void {
    context.beginPath(); if (!points.length) return;
    context.moveTo(points[0]!.x, points[0]!.y);
    for (const point of points.slice(1)) context.lineTo(point.x, point.y);
    if (closed) context.closePath();
  }
  function line(a: Point2, b: Point2): void { path([a, b], false); context.stroke(); }
  context.save();
  try {
    const dpr = view.devicePixelRatio;
    context.setTransform(dpr * view.scale, 0, 0, dpr * view.scale, dpr * view.offsetX, dpr * view.offsetY);
    context.globalAlpha = 1; context.globalCompositeOperation = "source-over"; context.setLineDash([]);
    context.lineJoin = "round"; context.lineCap = "round";
    for (const frame of frames) {
      context.strokeStyle = "#647687"; context.lineWidth = 0.6;
      for (const [a, b] of frame.contacts) line(a, b);
    }
    const bodies = frames.flatMap(f => f.bodies).sort((a, b) => a.depth - b.depth ||
      (a.instanceId < b.instanceId ? -1 : a.instanceId > b.instanceId ? 1 : a.bodyId < b.bodyId ? -1 : a.bodyId > b.bodyId ? 1 : 0));
    for (const body of bodies) {
      const s = body.style; context.lineWidth = s.strokeWidth; context.fillStyle = s.sideFill; context.strokeStyle = s.sideFill;
      path(body.back, true); context.fill();
      for (let i = 0; i < body.front.length; i++) {
        const next = (i + 1) % body.front.length;
        path([body.back[i]!, body.back[next]!, body.front[next]!, body.front[i]!], true); context.fill();
      }
      context.fillStyle = s.fill; context.strokeStyle = s.stroke;
      path(body.front, true); context.fill(); context.stroke();
      if (s.showReferences) { context.strokeStyle = "#dce8ef"; context.setLineDash([1.5, 1.5]); line(body.center, body.referenceMarker); context.setLineDash([]); }
      context.strokeStyle = s.marker; context.lineWidth = s.strokeWidth * 1.8; line(body.center, body.phaseMarker);
      if (s.showAxes) { context.strokeStyle = "#111d2d"; context.lineWidth = s.strokeWidth; for (const [a, b] of body.axes) line(a, b); }
      if (view.selected?.instanceId === body.instanceId && view.selected.bodyId === body.bodyId) {
        context.strokeStyle = "#ffffff"; context.lineWidth = s.strokeWidth * 2.5; path(body.front, true); context.stroke();
      }
    }
  } finally { context.restore(); }
}
