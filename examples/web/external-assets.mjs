import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { assemblyReferenceKey } from '@gearinvest/replay/assembly';
import { displayRadians } from './exact-display.mjs';

// One model candidate owns one bundle; its two instances share these resources.
// Instances only remove nodes. The owning candidate disposes after ALL instances.
export async function loadArrowAsset(bytes, sha256) {
  const assetId = 'sha256:' + await sha256(bytes);
  const gltf = await new GLTFLoader().parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
  const node = gltf.scene.getObjectByName('mounted-arrow');
  try {
    if (!node?.isMesh || node.userData.vertexUnits !== 'millimeter' || !Array.isArray(node.userData.pivotMm)
        || node.userData.pivotMm.length !== 3 || !node.userData.pivotMm.every(Number.isFinite)
        || node.position.lengthSq() !== 0 || !node.scale.toArray().every(v => v === .001)) throw Error('Unsupported example GLB.');
    node.updateMatrix();
    // Extract the declared mm geometry, WITHOUT its native glTF mm->meter node scale.
    // AssemblyAssetBindings owns that conversion once in the destination scene.
    const initialMount = new THREE.Matrix4().makeRotationFromQuaternion(node.quaternion);
    const pivot = node.userData.pivotMm;
    const correction = initialMount.clone().multiply(new THREE.Matrix4().makeTranslation(...pivot.map(x => -x)));
    let disposed = false;
    return {
      assetId, nodeReference: 'mounted-arrow', initialMount, pivot: [...pivot], correction: correction.toArray(),
      geometry: node.geometry, material: node.material, nativeNodeMatrixMeters: node.matrix.toArray(),
      makeNode() { if (disposed) throw Error('Asset disposed.'); const mesh = new THREE.Mesh(node.geometry, node.material); mesh.matrixAutoUpdate = false; return mesh; },
      dispose() { if (disposed) return; disposed = true; disposeScene(gltf.scene); },
      get disposed() { return disposed; }
    };
  } catch (error) { disposeScene(gltf.scene); throw error; }
}
function disposeScene(scene) {
  const geometries = new Set(), materials = new Set(), textures = new Set();
  scene.traverse(node => {
    if (node.geometry) geometries.add(node.geometry);
    for (const material of node.material ? (Array.isArray(node.material) ? node.material : [node.material]) : []) {
      materials.add(material); for (const value of Object.values(material)) if (value?.isTexture) textures.add(value);
    }
  });
  for (const texture of textures) texture.dispose();
  for (const material of materials) material.dispose();
  for (const geometry of geometries) geometry.dispose();
}
const value = f => Number(f.numerator) / Number(f.denominator);
export function checkExampleAsset(binding, asset) {
  if (['pitch-ring','long-hand','short-hand'].includes(binding.assetId) && binding.nodeReference === binding.bindingId) return;
  if (binding.assetId === asset.assetId && binding.nodeReference === asset.nodeReference) return;
  throw Error('Unsupported or mismatched external asset/node identity.');
}
export function scalarBodyMatrix(body, shaft, exactShaftTurns) {
  // This alternate example covers rigid affine bodies with the SAME calibrated law.
  // Other terminal/body phase relations require their declared calibration, not a sign patch.
  if (body.mode !== 'exactAffine' || JSON.stringify(body.q) !== JSON.stringify(shaft.q) || JSON.stringify(body.p) !== JSON.stringify(shaft.p)
      || assemblyReferenceKey(body.mountedShaft) !== assemblyReferenceKey(shaft.reference)) throw Error('Scalar demonstration requires matching body/shaft calibration. Use the SDK body pose.');
  const f = body.fixedFrameMm, axis = new THREE.Vector3(...body.positiveAxis.map(value));
  const fixedBasis = new THREE.Matrix4().makeBasis(new THREE.Vector3(...f.x.map(value)), new THREE.Vector3(...f.y.map(value)), new THREE.Vector3(...f.z.map(value)));
  const drive = new THREE.Matrix4().makeRotationFromQuaternion(new THREE.Quaternion().setFromAxisAngle(axis, displayRadians(exactShaftTurns)));
  // World-axis drive PREmultiplies the zero mounting. Origin is on the fixed shaft line.
  return new THREE.Matrix4().makeTranslation(...f.origin.map(value)).multiply(drive).multiply(fixedBasis);
}
