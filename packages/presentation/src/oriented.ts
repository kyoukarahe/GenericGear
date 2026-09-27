import { OrientedPlaybackEvaluator } from "@gearinvest/replay/oriented";
import { PreparedOrientedGeometry,OrientedPresentationError } from "./orientedGeometry.js";
export { OrientedPresentationError,parseOrientedPlacement,parseOrientedCamera,DEFAULT_ORIENTED_PLACEMENT,DEFAULT_ORIENTED_CAMERA,DEFAULT_ORIENTED_ANNOTATIONS,ORIENTED_PRESENTATION_PROFILE,rotateDisplayVector,displayPoint,fitOrientedProjection,parseOrientedViewDocument,writeOrientedViewDocument,parseOrientedViewDocumentText } from "./orientedGeometry.js";
export type { Orientation3,OrientedPlacement,OrientedCamera,OrientedAnnotations,OrientedSelection,OrientedViewDocument,ProjectedPoint3,OrientedPrimitive,OrientedProjection } from "./orientedGeometry.js";
export class PreparedOrientedPresentation extends PreparedOrientedGeometry<OrientedPlaybackEvaluator> {
  constructor(definition:OrientedPlaybackEvaluator,segments=64){
    if(!(definition instanceof OrientedPlaybackEvaluator))throw new OrientedPresentationError("DEFINITION_MISMATCH","Supply a compiled oriented definition.");
    super(definition,segments);
  }
}
export function prepareOrientedPresentation(definition:OrientedPlaybackEvaluator,segments=64):PreparedOrientedPresentation{return new PreparedOrientedPresentation(definition,segments);}
export function inspectOrientedPresentationCompatibility(definition:OrientedPlaybackEvaluator){try{prepareOrientedPresentation(definition);return Object.freeze({renderingCompatibility:"supported",precision:"schematicDisplayOnly"});}catch(e){if(!(e instanceof Error))throw e;return Object.freeze({renderingCompatibility:"unsupported",diagnostic:e.message});}}
