import type { ExternalRootBinding } from './instances.js';
import type { Vector3 } from './orientedTypes.js';
import { checkedNumber,checkedProduct,fractionToNumber,roundoff } from './playback.js';
import { vector3 } from './orientedKernel.js';
import { ReplayInputError } from './errors.js';

export function compileExternalBinding(b:ExternalRootBinding){return Object.freeze({sourceId:b.sourceId,source:fractionToNumber(b.sourceReferenceTurns),root:fractionToNumber(b.rootReferenceTurns),multiplier:fractionToNumber(b.multiplier)});}
export function sampleExternalBinding(e:ReturnType<typeof compileExternalBinding>,sources:ReadonlyMap<string,number>){
  const input=sources.get(e.sourceId);
  if(input===undefined)throw new ReplayInputError('MISSING_SOURCE',"Missing external source '"+e.sourceId+"'.");
  const source=checkedNumber(input,'source'),delta=checkedNumber(source-e.source,'source delta'),product=checkedProduct(e.multiplier,delta,'external binding'),root=checkedNumber(e.root+product,'bound root');
  const deltaError=roundoff(e.source)+roundoff(delta);
  const rootError=roundoff(e.root)+Math.abs(e.multiplier)*deltaError+roundoff(e.multiplier)*(Math.abs(delta)+deltaError)+roundoff(product)+roundoff(root);
  return {root,rootError};
}
export function sourceRate(v:Vector3,multiplier:number):Vector3{return vector3(checkedProduct(v[0],multiplier,'source derivative'),checkedProduct(v[1],multiplier,'source derivative'),checkedProduct(v[2],multiplier,'source derivative'));}
