// Ordinary static consumer: installed /assembly APIs own exact playback and binding validation.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { readAssemblyReplay, assemblyReferenceKey } from '@gearinvest/replay/assembly';
import { AssemblyAssetBindings, assemblyIdentityMatrix, restoreAssemblyView } from '@gearinvest/presentation/assembly';
import { exactInput, exactText, outputPhase, displayRadians } from './exact-display.mjs';
import { loadArrowAsset, scalarBodyMatrix, checkExampleAsset } from './external-assets.mjs';
import { modelLoads } from './load-lifecycle.mjs';
import './style.css';

const $=id=>document.getElementById(id), scene=new THREE.Scene(), viewport=$('viewport');
scene.background=new THREE.Color(0x101c2a);
const renderer=new THREE.WebGLRenderer({antialias:true});renderer.setPixelRatio(Math.min(devicePixelRatio,2));viewport.append(renderer.domElement);
const camera=new THREE.PerspectiveCamera(38,1,.1,3000);camera.up.set(0,1,0);
const controls=new OrbitControls(camera,renderer.domElement);
scene.add(new THREE.HemisphereLight(0xe8f2ff,0x456070,3));
const light=new THREE.DirectionalLight(0xffffff,3);light.position.set(-100,150,400);scene.add(light);
// A normal, nonidentity renderer parent. Each binding supplies its corresponding local transform.
const parent=new THREE.Group();parent.position.set(.017,-.011,.004);parent.scale.setScalar(1.25);parent.rotation.x=Math.PI/6;scene.add(parent);
let definition=null, active=null, sequence=0, items=[], lastError=null, candidate=null;
const text=exactText, fraction=exactInput;
const pendingTimers=new Map(),blobUrls=new Set();
function wait(ms){return new Promise((resolve,reject)=>{const id=setTimeout(()=>{pendingTimers.delete(id);resolve();},ms);pendingTimers.set(id,reject);});}
function status(message,error=false){$('status').textContent=message;$('status').dataset.error=String(error);lastError=error?message:null;}
const guard=fn=>async()=>{try{await fn();}catch(e){status(e.message,true);}};
async function digest(bytes){return [...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(n=>n.toString(16).padStart(2,'0')).join('');}
function release(group){group.traverse(o=>{if(!o.userData.sharedAsset){o.geometry?.dispose();o.material?.dispose();}});group.clear();group.removeFromParent();}
function clear(){for(const item of items){item.bindings.dispose();item.instance.dispose();release(item.group);}items=[];active=null;sequence=0;candidate?.dispose();candidate=null;definition=null;$('instance').replaceChildren();}
function asset(assetId,body){
  // Consumer-authored meshes with deliberately different nonzero pivots. They never become source bodies.
  const long=assetId==='long-hand', short=assetId==='short-hand', pivot=long?[7,3,2]:short?[-5,4,1]:[3,-2,1];
  let geometry;
  if(long||short){const length=long?70:43;geometry=new THREE.BoxGeometry(length,long?3:7,2);geometry.translate(length/2,0,long?9:8);}
  else {const r=body.specification.pitchRadiusMm,radius=r?Number(r.numerator)/Number(r.denominator):8;geometry=new THREE.RingGeometry(radius*.82,radius,Math.min(256,Math.max(24,body.specification.teeth??24)));}
  geometry.translate(...pivot);
  const mesh=new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({color:long?0x49ccef:short?0xff6d79:0xccab68,side:THREE.DoubleSide,roughness:.5,metalness:.35}));
  mesh.matrixAutoUpdate=false;mesh.userData={pivot,assetId};return {mesh,pivot};
}
function bindAsset(item,id,assetId,body){const {mesh,pivot}=asset(assetId,body),correction=[...assemblyIdentityMatrix];
  correction[12]=-pivot[0];correction[13]=-pivot[1];correction[14]=-pivot[2];
  item.bindings.bind({bindingId:id,instanceId:item.instance.instanceId,replayId:definition.replayId,sourceArtifactId:definition.payload.source.artifactId,
    definitionId:definition.payload.source.definitionId,reference:body.reference,assetId,nodeReference:id,assetLocalCorrectionMm:correction});
  item.group.add(mesh);item.meshes.set(id,mesh);
}
function add(requestedId=null){if(items.length>=2)throw Error('이 작은 예제의 인스턴스 한도는 2개입니다.');
  if(requestedId===null){do{requestedId='instance-'+(++sequence);}while(items.some(item=>item.instance.instanceId===requestedId));}
  const instance=definition.createInstance(requestedId),bindings=new AssemblyAssetBindings(instance),placement=[...assemblyIdentityMatrix];
  placement[12]=items.length*.235;bindings.setPlacement(placement,.001);const group=new THREE.Group();parent.add(group);
  const item={instance,bindings,group,meshes:new Map(),root:'0',frame:null};items.push(item);
  definition.payload.bodies.forEach((body,i)=>bindAsset(item,'gear-'+i,'pitch-ring',body));
  const option=document.createElement('option');option.value=instance.instanceId;option.textContent=instance.instanceId;$('instance').append(option);activate(item);fit();return item;
}
function fit(){scene.updateMatrixWorld(true);const box=new THREE.Box3().setFromObject(parent);if(box.isEmpty())return;const center=box.getCenter(new THREE.Vector3()),size=box.getSize(new THREE.Vector3());camera.near=.0001;camera.far=100;camera.updateProjectionMatrix();const span=Math.max(size.y,size.z,size.x/camera.aspect,.02),distance=1.4*span/(2*Math.tan(THREE.MathUtils.degToRad(camera.fov/2)))+size.z;camera.position.copy(center).add(new THREE.Vector3(0,-.24,1).normalize().multiplyScalar(distance));controls.target.copy(center);controls.update();}
function activate(item){active=item;$('instance').value=item.instance.instanceId;$('root').value=item.root;seek();}
function inspect(){return {replayId:definition?.replayId,sourceArtifactId:definition?.payload.source.artifactId,lastError,assetId:candidate?.asset.assetId,assetKey:candidate?.assetKey,route:$('route').value,
  parentWorld:[...parent.matrixWorld.elements],instances:items.map(item=>({id:item.instance.instanceId,root:item.root,shafts:item.frame?[...item.frame.shafts].map(([key,value])=>({reference:JSON.parse(key),turns:value})):[],
    bindings:item.bindings.inspect(),matrices:[...item.meshes].map(([id,mesh])=>({id,pivot:mesh.userData.pivot,sharedAsset:!!mesh.userData.sharedAsset,world:[...mesh.matrixWorld.elements]}))}))};}
function seek(){if(!active)throw Error('기구를 먼저 불러오세요.');const root=fraction($('root').value),frame=active.instance.evaluateAffine(root);parent.updateMatrixWorld(true);
  for(const pose of active.bindings.apply(frame,parent.matrixWorld.elements)){const mesh=active.meshes.get(pose.binding.bindingId);mesh.visible=pose.status==='displayApproximation';if(pose.localMatrix){
    let matrix=pose.localMatrix;
    if($('route').value==='scalar'&&mesh.userData.sharedAsset){const body=definition.payload.bodies.find(b=>assemblyReferenceKey(b.reference)===assemblyReferenceKey(pose.binding.reference)),shaft=definition.payload.shafts.find(s=>assemblyReferenceKey(s.reference)===assemblyReferenceKey(body.mountedShaft));
      const world=new THREE.Matrix4().fromArray(active.bindings.displayFromWorldMillimeters).multiply(scalarBodyMatrix(body,shaft,frame.shafts.get(assemblyReferenceKey(shaft.reference)))).multiply(new THREE.Matrix4().fromArray(pose.binding.assetLocalCorrectionMm));
      matrix=parent.matrixWorld.clone().invert().multiply(world).toArray();}
    mesh.matrix.fromArray(matrix);mesh.matrixWorldNeedsUpdate=true;}}
  scene.updateMatrixWorld(true);active.frame=frame;active.root=text(root);
  const shafts=[...frame.shafts].map(([key,value])=>JSON.parse(key).join('/')+' = '+text(value)+' turn; display phase '+text(outputPhase(value))+'; '+displayRadians(value).toFixed(6)+' rad');
  $('readback').textContent='instance: '+active.instance.instanceId+'\nu = '+text(root)+' (unwrapped)\n'+shafts.join('\n');
  $('inventory').textContent=JSON.stringify(inspect(),null,2);
  status(`${definition.payload.shafts.length} shafts · ${definition.payload.bodies.length} bodies · ${items.length} instances\nexact affine playback / browser mechanical validation: notPerformed\n표시만 mm → m (×0.001). 표시하지 않은 material/sample의 주기는 추정하지 않습니다.`);
}
function attachGlb(){if(!active)throw Error('기구를 먼저 불러오세요.');const body=definition.payload.bodies.find(b=>assemblyReferenceKey(b.reference)===$('long-body').value),id='external-glb';
  if(active.meshes.has(id)){active.bindings.unbind(id);active.meshes.get(id).removeFromParent();active.meshes.delete(id);}
  const a=candidate.asset,mesh=a.makeNode();mesh.userData={sharedAsset:true,pivot:a.pivot};
  active.bindings.bind({bindingId:id,instanceId:active.instance.instanceId,replayId:definition.replayId,sourceArtifactId:definition.payload.source.artifactId,definitionId:definition.payload.source.definitionId,reference:body.reference,assetId:a.assetId,nodeReference:a.nodeReference,assetLocalCorrectionMm:a.correction});
  active.group.add(mesh);active.meshes.set(id,mesh);seek();fit();}
function attachHands(){if(!active)throw Error('기구를 먼저 불러오세요.');for(const [id,select] of [['long-hand','long-body'],['short-hand','short-body']]){
  const body=definition.payload.bodies.find(b=>assemblyReferenceKey(b.reference)===$(select).value);if(!body)throw Error('실제 body를 선택하세요.');
  if(active.meshes.has(id)){active.bindings.unbind(id);const old=active.meshes.get(id);old.geometry.dispose();old.material.dispose();old.removeFromParent();active.meshes.delete(id);}
  bindAsset(active,id,id,body);
}seek();}
async function prepare(bytes,assetKey,initialInstanceId=null){let next=null,asset=null;try{next=readAssemblyReplay(bytes);await next.verifyIntegrity(digest);
  if(!['arrow','alternate'].includes(assetKey))throw Error('알 수 없는 외부 자산 키');
  const r=await fetch(new URL('./assets/'+assetKey+'.glb',document.baseURI));if(!r.ok)throw Error('GLB HTTP '+r.status);asset=await loadArrowAsset(new Uint8Array(await r.arrayBuffer()),digest);
  return {definition:next,asset,assetKey,initialInstanceId,dispose(){asset.dispose();next.dispose();}};
}catch(e){asset?.dispose();next?.dispose();throw e;}}
function adopt(next){clear();candidate=next;definition=next.definition;
  for(const id of ['long-body','short-body']){$(id).replaceChildren();for(const body of definition.payload.bodies){const option=document.createElement('option');option.value=assemblyReferenceKey(body.reference);option.textContent=body.reference.localId+' → '+body.mountedShaft.localId;$(id).append(option);}}
  $('short-body').selectedIndex=Math.max(0,definition.payload.bodies.length-1);$('asset').value=next.assetKey;add(next.initialInstanceId);
}
const loads=modelLoads({adopt,report:e=>status(e.message,true),pending:()=>status('새 모델 준비 중 — 이전 모델은 별도 상태로 유지됩니다.')});
function restore(textValue){if(!active)throw Error('원본을 먼저 불러오세요.');const header=JSON.parse(textValue),existing=items.find(i=>i.instance.instanceId===header.instanceId);
  const target=existing?.instance??definition.createInstance(header.instanceId);let checked;
  try{checked=restoreAssemblyView(target,textValue);for(const b of checked.inspect())checkExampleAsset(b,candidate.asset);}
  catch(e){checked?.dispose();if(!existing)target.dispose();throw e;}
  let item=existing;if(!item){checked.dispose();target.dispose();item=add(header.instanceId);checked=restoreAssemblyView(item.instance,textValue);}
  item.bindings.dispose();release(item.group);item.group=new THREE.Group();parent.add(item.group);item.meshes.clear();item.bindings=checked;
  for(const b of checked.inspect()){const body=definition.payload.bodies.find(x=>assemblyReferenceKey(x.reference)===assemblyReferenceKey(b.reference));let mesh;if(b.assetId===candidate.asset.assetId){mesh=candidate.asset.makeNode();mesh.userData={sharedAsset:true,pivot:candidate.asset.pivot};}else mesh=asset(b.assetId,body).mesh;item.group.add(mesh);item.meshes.set(b.bindingId,mesh);}
  activate(item);fit();
}
function download(bytes,name){const url=URL.createObjectURL(new Blob([bytes],{type:'application/json'})),a=document.createElement('a');blobUrls.add(url);a.href=url;a.download=name;a.click();wait(1000).finally(()=>{URL.revokeObjectURL(url);blobUrls.delete(url);}).catch(()=>{});}
$('load').onclick=()=>{const key=$('model').value,assetKey=$('asset').value,delay=$('delay').checked?10000:0;return loads.load(async()=>{const r=await fetch(new URL('./data/'+key+'.replay.json',document.baseURI));if(!r.ok)throw Error('Replay HTTP '+r.status);const bytes=new Uint8Array(await r.arrayBuffer());if(delay)await wait(delay);return prepare(bytes,assetKey);});};
$('file').onchange=()=>{const file=$('file').files[0],assetKey=$('asset').value;return loads.load(async()=>{if(!file||file.size>33554432)throw Error('문서 크기 초과/파일 없음');return prepare(new Uint8Array(await file.arrayBuffer()),assetKey);});};
$('cancel').onclick=()=>{loads.cancel();status('로드 취소 — 기존 모델 유지');};
$('bind-glb').onclick=guard(attachGlb);$('route').onchange=guard(seek);
$('remove').onclick=guard(()=>{if(!active)throw Error('활성 인스턴스 없음');const old=active;old.bindings.dispose();old.instance.dispose();release(old.group);items=items.filter(i=>i!==old);for(const option of [...$('instance').options])if(option.value===old.instance.instanceId)option.remove();active=items[0]??null;if(active)activate(active);else {$('readback').textContent='현재 인스턴스 없음';status('인스턴스 해제; 공유 asset은 모델 소유자가 유지합니다.');}});
$('close').onclick=()=>{loads.cancel();clear();$('readback').textContent='현재 모델 없음';$('inventory').textContent='';status('모델·instance·asset 해제');};
$('bind').onclick=guard(attachHands);$('seek').onclick=guard(seek);$('clone').onclick=guard(()=>add());
$('instance').onchange=guard(()=>activate(items.find(x=>x.instance.instanceId===$('instance').value)));
for(const b of document.querySelectorAll('[data-root]'))b.onclick=guard(()=>{$('root').value=b.dataset.root;seek();});
function save(){if(!active)throw Error('기구를 먼저 불러오세요.');const saved={original:new TextDecoder().decode(definition.originalBytes()),view:active.bindings.saveView(),root:active.root,assetKey:candidate.assetKey};localStorage.setItem('gearinvest-coaxial-example',JSON.stringify(saved));return saved;}
// One explicit gesture per download avoids the browser's multiple-download block.
$('save-local').onclick=guard(()=>{save();status('원본 bytes · canonical View · 정확 누적 입력 · GLB 키를 브라우저에 저장했습니다. 새 페이지에서 다시 열 수 있습니다.');});
$('save').onclick=guard(()=>{download(save().original,'coaxial.assembly-replay.json');status('원본 다운로드 요청 · 원본/현재 View/입력은 브라우저에 저장됨');});
$('save-view').onclick=guard(()=>{download(save().view,'coaxial.assembly-view.json');status('View 다운로드 요청 · 원본/현재 View/입력은 브라우저에 저장됨');});
$('restore').onclick=guard(()=>{const saved=JSON.parse(localStorage.getItem('gearinvest-coaxial-example'));if(!saved)throw Error('저장된 View가 없습니다.');restore(saved.view);});
$('reopen').onclick=guard(async()=>{const saved=JSON.parse(localStorage.getItem('gearinvest-coaxial-example'));if(!saved)throw Error('저장된 원본이 없습니다.');
  const outcome=await loads.load(async()=>{const next=await prepare(new TextEncoder().encode(saved.original),saved.assetKey??'arrow',JSON.parse(saved.view).instanceId);let instance,view;try{instance=next.definition.createInstance(next.initialInstanceId);view=restoreAssemblyView(instance,saved.view);fraction(saved.root);for(const b of view.inspect())checkExampleAsset(b,next.asset);return next;}catch(e){next.dispose();throw e;}finally{view?.dispose();instance?.dispose();}});
  if(outcome==='adopted'){restore(saved.view);$('root').value=saved.root;seek();}});
$('view-file').onchange=async()=>{const expected=candidate;try{const file=$('view-file').files[0];if(!file||file.size>4194304)throw Error('View 크기 초과/파일 없음');const view=await file.text();if(candidate===expected)restore(view);}catch(e){if(candidate===expected)status(e.message,true);}};
Object.defineProperty(window,'coaxialObservation',{get:inspect}); // Read-only state, never a hidden action path.
const resize=new ResizeObserver(()=>{const r=viewport.getBoundingClientRect();renderer.setSize(r.width,r.height,false);camera.aspect=r.width/r.height;camera.updateProjectionMatrix();});resize.observe(viewport);
let raf;function render(){raf=requestAnimationFrame(render);renderer.render(scene,camera);}render();
window.addEventListener('pagehide',()=>{loads.dispose();cancelAnimationFrame(raf);for(const control of document.querySelectorAll('button,input,select')){control.onclick=null;control.onchange=null;}for(const [id,reject] of pendingTimers){clearTimeout(id);reject(Error('Page closed'));}pendingTimers.clear();for(const url of blobUrls)URL.revokeObjectURL(url);blobUrls.clear();clear();resize.disconnect();controls.dispose();renderer.dispose();},{once:true});
$('load').click();
