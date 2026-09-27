import { ResolvedPlaybackEvaluator, normalizeTurns } from "@gearinvest/replay";
import type { InstanceFrame } from "@gearinvest/replay";

export interface Point2 { readonly x: number; readonly y: number; }
export interface Point3 extends Point2 { readonly z: number; }
export interface Bounds2 { readonly minX: number; readonly minY: number; readonly maxX: number; readonly maxY: number; }
export interface PlanePresentationSettings {
  readonly kind: "orthographic-plane";
  readonly origin: Point3;
  /** Right-handed local-to-world Rz(yaw) Ry(pitch) Rx(roll), in turns. */
  readonly orientation: { readonly yawTurns: number; readonly pitchTurns: number; readonly rollTurns: number; };
  readonly scale: number;
  /** Schematic local-normal lengths, not physical thickness or shaft clearance. */
  readonly visualThickness: number;
  readonly layerSeparation: number;
  readonly outline: "pitchCircle" | "schematicTeeth";
  readonly style: {
    readonly fill: string; readonly sideFill: string; readonly stroke: string; readonly marker: string;
    readonly strokeWidth: number; readonly showAxes: boolean; readonly showReferences: boolean; readonly showContacts: boolean;
  };
}
export interface PresentationDocument {
  readonly format: "gear-invest.replay-presentation";
  readonly formatVersion: "0.1";
  readonly presets: readonly { readonly id: string; readonly settings: PlanePresentationSettings }[];
}
export class PresentationInputError extends Error {
  constructor(readonly code: string, message: string) { super(message); this.name = "PresentationInputError"; }
}
function freeze<T>(value: T): T {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.values(value).forEach(freeze); Object.freeze(value);
  }
  return value;
}
function rec(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      (Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null))
    throw new PresentationInputError("INVALID_SETTINGS", "Expected a plain presentation record.");
  const names = Reflect.ownKeys(value);
  if (names.length !== keys.length || names.some(key => typeof key !== "string" || !keys.includes(key) ||
      !Object.hasOwn(Object.getOwnPropertyDescriptor(value, key)!, "value")))
    throw new PresentationInputError("INVALID_SETTINGS", "Missing, accessor, or unknown presentation field.");
  return value as Record<string, unknown>;
}
function number(value: unknown, label: string, max = 1e9): number {
  if (typeof value !== "number" || !Number.isFinite(value) || Math.abs(value) > max)
    throw new PresentationInputError("NUMERIC_RANGE", `${label} must be finite with magnitude <= ${max}.`);
  return Object.is(value, -0) ? 0 : value;
}
function bool(value: unknown): boolean {
  if (typeof value !== "boolean") throw new PresentationInputError("INVALID_SETTINGS", "Expected a boolean display setting.");
  return value;
}
function color(value: unknown): string {
  if (typeof value !== "string" || !/^#[0-9a-fA-F]{6}$/.test(value))
    throw new PresentationInputError("INVALID_STYLE", "Colors must be opaque #RRGGBB values.");
  return value.toLowerCase();
}
export const DEFAULT_PRESENTATION: PlanePresentationSettings = freeze({
  kind: "orthographic-plane", origin: { x: 0, y: 0, z: 0 },
  orientation: { yawTurns: 0, pitchTurns: 0, rollTurns: 0 }, scale: 1,
  visualThickness: 4, layerSeparation: 8, outline: "schematicTeeth",
  style: { fill: "#59c8ba", sideFill: "#245f69", stroke: "#b6eee3", marker: "#ffcf73", strokeWidth: 0.7,
    showAxes: true, showReferences: true, showContacts: true },
});
export function parsePresentationSettings(value: unknown): PlanePresentationSettings {
  const r = rec(value, ["kind", "origin", "orientation", "scale", "visualThickness", "layerSeparation", "outline", "style"]);
  if (r.kind !== "orthographic-plane" || (r.outline !== "pitchCircle" && r.outline !== "schematicTeeth"))
    throw new PresentationInputError("UNSUPPORTED_PROFILE", "Unsupported presentation kind/outline.");
  const o = rec(r.origin, ["x", "y", "z"]), a = rec(r.orientation, ["yawTurns", "pitchTurns", "rollTurns"]);
  const s = rec(r.style, ["fill", "sideFill", "stroke", "marker", "strokeWidth", "showAxes", "showReferences", "showContacts"]);
  const scale = number(r.scale, "scale", 1e4), thickness = number(r.visualThickness, "visualThickness", 1e6);
  const separation = number(r.layerSeparation, "layerSeparation", 1e6), width = number(s.strokeWidth, "strokeWidth", 100);
  if (scale <= 0 || thickness < 0 || separation < 0 || width <= 0)
    throw new PresentationInputError("INVALID_SETTINGS", "Scale/stroke must be positive; visual lengths must be nonnegative.");
  const result: PlanePresentationSettings = {
    kind: r.kind, origin: { x: number(o.x, "origin.x"), y: number(o.y, "origin.y"), z: number(o.z, "origin.z") },
    orientation: { yawTurns: number(a.yawTurns, "yawTurns", 2 ** 26), pitchTurns: number(a.pitchTurns, "pitchTurns", 2 ** 26), rollTurns: number(a.rollTurns, "rollTurns", 2 ** 26) },
    scale, visualThickness: thickness, layerSeparation: separation, outline: r.outline,
    style: { fill: color(s.fill), sideFill: color(s.sideFill), stroke: color(s.stroke), marker: color(s.marker), strokeWidth: width,
      showAxes: bool(s.showAxes), showReferences: bool(s.showReferences), showContacts: bool(s.showContacts) },
  };
  const normal = rotation(result)(0, 0, 1);
  if (Math.abs(normal.z) < 1e-3) throw new PresentationInputError("EDGE_ON_PROJECTION", "Edge-on/singular plane projection is unsupported (|normal.z| < 0.001).");
  return freeze(result);
}
export function parsePresentationDocument(value: unknown): PresentationDocument {
  const r = rec(value, ["format", "formatVersion", "presets"]);
  if (r.format !== "gear-invest.replay-presentation" || r.formatVersion !== "0.1")
    throw new PresentationInputError("UNSUPPORTED_VERSION", "Unsupported presentation document.");
  if (!Array.isArray(r.presets) || r.presets.length === 0 || r.presets.length > 128)
    throw new PresentationInputError("RESOURCE_LIMIT", "Presentation document needs 1..128 presets.");
  if (Reflect.ownKeys(r.presets).length !== r.presets.length + 1 ||
      Reflect.ownKeys(r.presets).some(k => k !== "length" && (typeof k !== "string" || !/^(0|[1-9][0-9]*)$/.test(k) || Number(k) >= (r.presets as unknown[]).length)))
    throw new PresentationInputError("INVALID_SETTINGS", "Sparse/extended presentation arrays are unsupported.");
  const presets = r.presets.map(value => {
    const p = rec(value, ["id", "settings"]);
    if (typeof p.id !== "string" || !p.id || p.id.length > 256 || p.id.trim() !== p.id)
      throw new PresentationInputError("INVALID_ID", "Invalid presentation reference ID.");
    return { id: p.id, settings: parsePresentationSettings(p.settings) };
  }).sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
  if (new Set(presets.map(p => p.id)).size !== presets.length) throw new PresentationInputError("DUPLICATE_ID", "Duplicate presentation reference.");
  return freeze({ format: r.format, formatVersion: r.formatVersion, presets });
}
export function writePresentationDocument(value: PresentationDocument): string {
  return JSON.stringify(parsePresentationDocument(value), null, 2) + "\n";
}
export function parsePresentationDocumentText(text: string): PresentationDocument {
  if (typeof text !== "string" || text.length > 262144) throw new PresentationInputError("RESOURCE_LIMIT", "Presentation document text is too large.");
  let value: unknown;
  try { value = JSON.parse(text); } catch { throw new PresentationInputError("INVALID_JSON", "Presentation document is not JSON."); }
  return parsePresentationDocument(value);
}

function rotation(settings: PlanePresentationSettings) {
  const trig = (v: number) => [Math.cos(normalizeTurns(v) * 2 * Math.PI), Math.sin(normalizeTurns(v) * 2 * Math.PI)] as const;
  const [cz, sz] = trig(settings.orientation.yawTurns), [cy, sy] = trig(settings.orientation.pitchTurns), [cx, sx] = trig(settings.orientation.rollTurns);
  return (x: number, y: number, z: number): Point3 => {
    const y1 = cx * y - sx * z, z1 = sx * y + cx * z;
    const x2 = cy * x + sy * z1, z2 = -sy * x + cy * z1;
    return { x: cz * x2 - sz * y1, y: sz * x2 + cz * y1, z: z2 };
  };
}
interface LocalBody { readonly id: string; readonly dofId: string; readonly layer: number; readonly x: number; readonly y: number; readonly radius: number; readonly outline: readonly Point2[]; }
export interface ProjectedBody {
  readonly instanceId: string; readonly bodyId: string; readonly dofId: string; readonly layer: number; readonly unwrappedTurns: number;
  readonly center: Point2; readonly backCenter: Point2; readonly depth: number;
  readonly front: readonly Point2[]; readonly back: readonly Point2[];
  readonly phaseMarker: Point2; readonly referenceMarker: Point2;
  readonly axes: readonly (readonly [Point2, Point2])[];
  readonly style: PlanePresentationSettings["style"];
}
export interface ProjectedInstance {
  readonly instanceId: string; readonly bodies: readonly ProjectedBody[]; readonly contacts: readonly (readonly [Point2, Point2])[];
  readonly bounds: Bounds2; readonly projectedNormal: Point2; readonly facing: "front" | "back";
  readonly semantics: "schematicDisplayOnly";
}
export class PreparedPresentation {
  readonly settings: PlanePresentationSettings;
  readonly definition: ResolvedPlaybackEvaluator;
  readonly #bodies: readonly LocalBody[];
  constructor(definition: ResolvedPlaybackEvaluator, settings: PlanePresentationSettings) {
    if (!(definition instanceof ResolvedPlaybackEvaluator)) throw new PresentationInputError("UNPARSED_DEFINITION", "Supply a compiled replay definition.");
    this.definition = definition; this.settings = parsePresentationSettings(settings);
    const axes = new Map(definition.artifact.spatial.axes.map(a => [a.id, a]));
    let vertices = 0;
    this.#bodies = definition.artifact.spatial.bodies.map(body => {
      const axis = axes.get(body.axisId)!;
      const radius = number(Number(BigInt(body.pitchRadius)), "pitch radius", 1e6);
      if (radius <= 0 || body.toothCount < 3 || body.toothCount > 1024)
        throw new PresentationInputError("UNSUPPORTED_GEOMETRY", "Presentation requires positive pitch radius and 3..1024 teeth; no radius/tooth clamping.");
      const segments = this.settings.outline === "pitchCircle" ? 64 : body.toothCount * 4;
      vertices += segments;
      if (vertices > 131072) throw new PresentationInputError("RESOURCE_LIMIT", "Prepared outline vertex limit exceeded.");
      const outline = Array.from({ length: segments }, (_, i) => {
        const angle = i / segments * Math.PI * 2;
        const r = radius * (this.settings.outline === "pitchCircle" ? 1 : i % 4 === 1 || i % 4 === 2 ? 1.06 : 0.92);
        return { x: r * Math.cos(angle), y: r * Math.sin(angle) };
      });
      return freeze({ id: body.id, dofId: body.dofId, layer: body.layer, radius, outline,
        x: number(Number(BigInt(axis.x)), "axis.x", 1e8), y: number(Number(BigInt(axis.y)), "axis.y", 1e8) });
    });
    if (!this.#bodies.length) throw new PresentationInputError("EMPTY_GEOMETRY", "No bodies to present.");
    Object.freeze(this);
  }
  project(frame: InstanceFrame): ProjectedInstance {
    const a = this.definition.artifact;
    if (frame.candidateId !== a.candidateId || frame.artifactHash !== a.artifactHash || frame.playback.bodyTurns.size !== this.#bodies.length)
      throw new PresentationInputError("FRAME_MISMATCH", "Instance frame and presentation definition differ.");
    const settings = this.settings, rotate = rotation(settings);
    const world = (x: number, y: number, z: number): Point3 => {
      const p = rotate(x * settings.scale, y * settings.scale, z * settings.scale);
      return { x: number(p.x + settings.origin.x, "projected x"), y: number(p.y + settings.origin.y, "projected y"), z: number(p.z + settings.origin.z, "depth") };
    };
    // Screen convention is +X right, +Y down, viewed along -world Z. No coefficient sign changes.
    const screen = (x: number, y: number, z: number): Point2 => { const p = world(x, y, z); return { x: p.x, y: -p.y }; };
    const bodies = this.#bodies.map(body => {
      const turns = frame.playback.bodyTurns.get(body.id);
      if (turns === undefined) throw new PresentationInputError("FRAME_MISMATCH", `Missing body '${body.id}'.`);
      const angle = normalizeTurns(turns) * 2 * Math.PI, cos = Math.cos(angle), sin = Math.sin(angle);
      const z = number(body.layer * settings.layerSeparation, "visual layer offset");
      const point = (p: Point2, height: number) => screen(body.x + cos * p.x - sin * p.y, body.y + sin * p.x + cos * p.y, height);
      const top = z + settings.visualThickness, marker = Math.min(body.radius * 0.2, 2);
      return freeze({ instanceId: frame.instanceId, bodyId: body.id, dofId: body.dofId, layer: body.layer, unwrappedTurns: turns,
        center: screen(body.x, body.y, top), backCenter: screen(body.x, body.y, z), depth: world(body.x, body.y, top).z,
        front: body.outline.map(p => point(p, top)), back: body.outline.map(p => point(p, z)),
        phaseMarker: point({ x: body.radius, y: 0 }, top), referenceMarker: screen(body.x + body.radius, body.y, top),
        axes: [[screen(body.x - marker, body.y, top), screen(body.x + marker, body.y, top)],
          [screen(body.x, body.y - marker, top), screen(body.x, body.y + marker, top)]] as const, style: settings.style });
    }).sort((a, b) => a.depth - b.depth || (a.bodyId < b.bodyId ? -1 : a.bodyId > b.bodyId ? 1 : 0));
    const byId = new Map(bodies.map(b => [b.bodyId, b]));
    const contacts = settings.style.showContacts ? a.spatial.contacts.map(c => [byId.get(c.bodyAId)!.center, byId.get(c.bodyBId)!.center] as const) : [];
    const bounds = boundsOfPoints(bodies.flatMap(b => [...b.front, ...b.back]));
    const n = rotate(0, 0, settings.visualThickness * settings.scale);
    return freeze({ instanceId: frame.instanceId, bodies, contacts, bounds, projectedNormal: { x: n.x, y: -n.y },
      facing: rotate(0, 0, 1).z >= 0 ? "front" : "back", semantics: "schematicDisplayOnly" });
  }
}
export function preparePresentation(definition: ResolvedPlaybackEvaluator, settings: PlanePresentationSettings = DEFAULT_PRESENTATION): PreparedPresentation {
  return new PreparedPresentation(definition, settings);
}
export function projectPresentation(prepared: PreparedPresentation, frame: InstanceFrame): ProjectedInstance { return prepared.project(frame); }
export function boundsOfPoints(points: readonly Point2[]): Bounds2 {
  if (!points.length) throw new PresentationInputError("EMPTY_GEOMETRY", "Cannot bound an empty point set.");
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const point of points) { const x = number(point.x, "bounds x"), y = number(point.y, "bounds y");
    minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y); }
  return Object.freeze({ minX, minY, maxX, maxY });
}
export function hitTestPresentation(frames: readonly ProjectedInstance[], point: Point2): { readonly instanceId: string; readonly bodyId: string } | undefined {
  number(point.x, "hit x"); number(point.y, "hit y");
  const bodies = frames.flatMap(f => f.bodies).sort((a, b) => a.depth - b.depth ||
    (a.instanceId < b.instanceId ? -1 : a.instanceId > b.instanceId ? 1 : a.bodyId < b.bodyId ? -1 : a.bodyId > b.bodyId ? 1 : 0));
  for (const body of bodies.reverse()) {
    let inside = false;
    for (let i = 0, j = body.front.length - 1; i < body.front.length; j = i++) {
      const a = body.front[i]!, b = body.front[j]!;
      if ((a.y > point.y) !== (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
    }
    if (inside) return Object.freeze({ instanceId: body.instanceId, bodyId: body.bodyId });
  }
  return undefined;
}
