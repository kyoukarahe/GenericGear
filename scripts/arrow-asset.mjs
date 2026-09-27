// Self-authored diagnostic solid, not a watch/caliber or precision tooth mesh.
// Build-time only: creates a small external GLB; final users need no Blender.
import { writeFileSync } from 'node:fs';
export function writeArrowGlb(path, alternate = false) {
  const pivot = [7, 3, 2];
  const points = [[0,-2,0],[20,-2,0],[20,-5,0],[30,0,0],[20,5,0],[20,2,0],[0,2,0], [0,-2,4],[20,-2,4],[20,-5,4],[30,0,4],[20,5,4],[20,2,4],[0,2,4]];
  const triangles = [0,1,6,1,5,6,2,3,4,7,13,8,8,13,12,9,11,10];
  for (let i=0;i<7;i++) { const j=(i+1)%7; triangles.push(i,j,i+7,j,j+7,i+7); }
  const positions = new Float32Array(points.flatMap(p => p.map((x,i) => x+pivot[i])));
  const indices = new Uint16Array(triangles);
  const binary = Buffer.concat([Buffer.from(positions.buffer),Buffer.from(indices.buffer)]);
  const sin = Math.sin(75*Math.PI/180), cos = Math.cos(75*Math.PI/180);
  // Second case also has a different fixed X rotation; never inferred from tooth count.
  const rotation = alternate ? [cos*Math.SQRT1_2,sin*Math.SQRT1_2,sin*Math.SQRT1_2,cos*Math.SQRT1_2] : [0,sin,0,cos];
  const json = { asset:{version:'2.0',generator:'GenericGear consumer guidance self-authored diagnostic asset'}, scene:0,scenes:[{nodes:[0]}],
    // glTF scene distances are meters. Integer-mm accessors use an explicit 0.001 node scale.
    nodes:[{name:'mounted-arrow',mesh:0,rotation,scale:[.001,.001,.001],extras:{vertexUnits:'millimeter',pivotMm:pivot,landmarksMm:{pivot,tip:[37,3,2],side:[27,8,2],top:[7,1,6]}}}],
    meshes:[{primitives:[{attributes:{POSITION:0},indices:1,material:0}]}],
    materials:[{pbrMetallicRoughness:{baseColorFactor:alternate?[1,.28,.3,1]:[.2,.8,1,1],metallicFactor:.1,roughnessFactor:.65},doubleSided:true}],
    buffers:[{byteLength:binary.length}],bufferViews:[{buffer:0,byteOffset:0,byteLength:positions.byteLength,target:34962},{buffer:0,byteOffset:positions.byteLength,byteLength:indices.byteLength,target:34963}],
    accessors:[{bufferView:0,componentType:5126,count:points.length,type:'VEC3',min:[7,-2,2],max:[37,8,6]},{bufferView:1,componentType:5123,count:indices.length,type:'SCALAR'}] };
  const raw=Buffer.from(JSON.stringify(json)), padded=Buffer.concat([raw,Buffer.alloc((4-raw.length%4)%4,32)]), bin=Buffer.concat([binary,Buffer.alloc((4-binary.length%4)%4)]);
  const header=Buffer.alloc(12),j=Buffer.alloc(8),b=Buffer.alloc(8);
  header.writeUInt32LE(0x46546c67);header.writeUInt32LE(2,4);header.writeUInt32LE(12+8+padded.length+8+bin.length,8);
  j.writeUInt32LE(padded.length);j.writeUInt32LE(0x4e4f534a,4);b.writeUInt32LE(bin.length);b.writeUInt32LE(0x004e4942,4);
  writeFileSync(path,Buffer.concat([header,j,padded,b,bin]),{flag:'wx'});
}
