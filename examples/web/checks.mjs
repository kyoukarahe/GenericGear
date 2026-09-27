// Copied beside the ordinary example by prepare.mjs. All SDK imports use PUBLIC entries.
import assert from 'node:assert/strict';
import {test,after} from 'node:test';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {createHash} from 'node:crypto';
import * as THREE from 'three';
import {readAssemblyReplay,assemblyReferenceKey} from '@gearinvest/replay/assembly';
import {normalizeTurns} from '@gearinvest/replay';
import {parseOrientedArtifactText,compileOrientedPlayback} from '@gearinvest/replay/oriented';
import {AssemblyAssetBindings,assemblyIdentityMatrix,restoreAssemblyView} from '@gearinvest/presentation/assembly';
import {fraction,exactInput,exactText,outputPhase,displayRadians,arithmeticExample,rotationPeriod,displayRepresentative,evaluateOrientedDisplay} from './exact-display.mjs';
import {loadArrowAsset,scalarBodyMatrix,checkExampleAsset} from './external-assets.mjs';
import {modelLoads} from './load-lifecycle.mjs';
const root=import.meta.dirname,sha=b=>createHash('sha256').update(b).digest('hex');
const bytes=key=>readFileSync(resolve(root,'public/data/'+key+'.replay.json'));
const imageBytes=key=>readFileSync(resolve(root,'public/assets/'+key+'.glb'));
const f=(n,d='1')=>fraction(String(n),String(d));
let group='',assertions=0;const counts={},results=[],observations={};
const a=new Proxy(assert,{get(target,key){const fn=target[key];return typeof fn==='function'?(...args)=>{assertions++;counts[group]=(counts[group]??0)+1;return fn(...args);}:fn;}});
function check(name,fn){test(name,async()=>{group=name.slice(0,2);try{await fn();results.push({name,status:'pass'});}catch(error){results.push({name,status:'fail',error:error.message});throw error;}});}
const close=(actual,expected,tol=1e-10)=>{a.equal(actual.length,expected.length);actual.forEach((v,i)=>a.ok(Math.abs(v-expected[i])<=tol,`${i}: ${v} != ${expected[i]}`));};
const binding=(instance,asset,body=instance.definition.payload.bodies[0])=>({bindingId:'arrow',instanceId:instance.instanceId,replayId:instance.definition.replayId,
  sourceArtifactId:instance.definition.payload.source.artifactId,definitionId:instance.definition.payload.source.definitionId,reference:body.reference,
  assetId:asset.assetId,nodeReference:asset.nodeReference,assetLocalCorrectionMm:asset.correction});

check('G1 actual SDK-authored q=1,1/12,1/960 with nonzero p; exact inputs and immutable bytes',async()=>{
  const original=bytes('exact-phases'),replay=readAssemblyReplay(original),i=replay.createInstance('exact');
  a.equal((await replay.verifyIntegrity(sha)).rawDigest,'Pass');
  const required=replay.payload.shafts.filter(s=>s.reference.owner==='Member');a.equal(required.length,3);
  a.deepEqual(required.map(s=>exactText(s.q)).sort(),['1','1/12','1/960']);required.forEach(s=>a.deepEqual(s.p,f(1,7)));
  const roots=['0','1','-1','-1/4','5/7','0.1','-2.75','960000000000000000000000000000000000000000000000000000000000001/4'];
  for(const text of roots){const u=exactInput(text),frame=i.evaluateAffine(u);a.deepEqual(frame.requestedRoot,u);
    for(const s of replay.payload.shafts){const got=frame.shafts.get(assemblyReferenceKey(s.reference));
      // Independent cross-product equality against source coefficients, not another evaluateAffine call.
      const n=BigInt(s.q.numerator)*BigInt(u.numerator)*BigInt(s.p.denominator)+BigInt(s.p.numerator)*BigInt(s.q.denominator)*BigInt(u.denominator);
      const d=BigInt(s.q.denominator)*BigInt(u.denominator)*BigInt(s.p.denominator);
      a.equal(BigInt(got.numerator)*d,n*BigInt(got.denominator));const before=JSON.stringify(got);outputPhase(got);displayRadians(got);a.equal(JSON.stringify(got),before);
    }
  }
  a.deepEqual(replay.originalBytes(),new Uint8Array(original));observations.exactProducer={replayId:replay.replayId,source:replay.payload.source.artifactId,roots:roots.length,shafts:replay.payload.shafts.length};replay.dispose();
});
check('G1 deliberate root-mod1 loses actual 1/12 and 1/960 progress',()=>{
  const d=readAssemblyReplay(bytes('exact-phases')),i=d.createInstance('mutant');const good=i.evaluateAffine(f(1)),bad=i.evaluateAffine(f(0));
  for(const den of ['12','960']){const s=d.payload.shafts.find(s=>s.q.denominator===den&&s.p.numerator!=='0'),key=assemblyReferenceKey(s.reference);
    a.notDeepEqual(good.shafts.get(key),bad.shafts.get(key));a.notDeepEqual(outputPhase(good.shafts.get(key)),outputPhase(bad.shafts.get(key)));}
  d.dispose();
});
check('G1 negative and finite-decimal construction; Number/underflow/resource refusal',()=>{
  a.deepEqual(exactInput('-0.25'),f(-1,4));a.deepEqual(exactInput('0.1'),f(1,10));a.deepEqual(exactInput('02/08'),f(1,4));
  a.deepEqual(outputPhase(f(-1,4)),f(3,4));a.equal(normalizeTurns(-.25),.75);a.equal(normalizeTurns(-0),0);
  for(const text of ['1e60','0.1 ','1/0','9'.repeat(129)])a.throws(()=>exactInput(text));
  a.throws(()=>fraction(9007199254740993));a.throws(()=>displayRadians(fraction('1','1'+'0'.repeat(400),1024)),/DISPLAY_UNAVAILABLE/);
  a.notEqual(BigInt(Number('9007199254740993')),BigInt(exactInput('9007199254740993').numerator));
});
check('G2 sufficient integer period and distinct exact state; signs, zero and reductions',()=>{
  const channels=[['driver',f(1)],['middle',f(-2,24)],['output',f(1,960)],['fixed',f(0)]].map(([id,q])=>({id,q}));
  const T=rotationPeriod(channels,'fixed-affine-rotations-only');a.equal(T,960n);
  for(const {q} of channels){a.equal(BigInt(q.numerator)*T%BigInt(q.denominator),0n);
    const x=arithmeticExample(q,f(1,7),f(-1,4)),y=arithmeticExample(q,f(1,7),f(3839,4));a.deepEqual(outputPhase(x),outputPhase(y));if(q.numerator!=='0')a.notDeepEqual(x,y);}
  const r=displayRepresentative(exactInput('-960000000000000000000000000000000000000000000000001/4'),T);
  a.notDeepEqual(r.exactRoot,r.displayRoot);a.ok(r.number>=0&&r.number<960);observations.period={period:String(T),original:r.exactRoot,display:r.displayRoot};
  a.equal(rotationPeriod([{id:'twice',q:f(2)}],'fixed-affine-rotations-only'),1n); // valid but NOT minimal (1/2 is smaller)
});
check('G2 an omitted marked intermediate invalidates a partial display period',()=>{
  const partial=[{id:'input',q:f(1)},{id:'output',q:f(1,12)}],idler={id:'marked-intermediate',q:f(1,7)};
  const wrong=rotationPeriod(partial,'fixed-affine-rotations-only');a.equal(wrong,12n);a.notEqual(wrong%7n,0n);
  a.notDeepEqual(outputPhase(arithmeticExample(idler.q,f(1,7),f(0))),outputPhase(arithmeticExample(idler.q,f(1,7),f(wrong))));
  a.equal(rotationPeriod([...partial,idler],'fixed-affine-rotations-only'),84n);
});
check('G2 unsupported observations, budgets and actual legacy numeric API refusal',()=>{
  for(const kind of ['translation','counter','event','conditional','finite-samples','holdPrevious','nonlinear'])a.throws(()=>rotationPeriod([{id:'x',q:f(1)}],kind),/inapplicable/);
  a.throws(()=>rotationPeriod([{id:'x',q:f(1,1048577)}],'fixed-affine-rotations-only'),/budget/);
  a.throws(()=>rotationPeriod(Array.from({length:257},(_,i)=>({id:String(i),q:f(1)})),'fixed-affine-rotations-only'),/count/);
  a.throws(()=>rotationPeriod([{id:'x',q:{numerator:'9'.repeat(129),denominator:'1'}}],'fixed-affine-rotations-only'),/budget/);
  a.throws(()=>displayRepresentative(f(1),2n**27n));
  const definition=compileOrientedPlayback(parseOrientedArtifactText(readFileSync(resolve(root,'public/data/mixed.oriented.json'),'utf8')));
  a.throws(()=>definition.evaluate(2**27),/2\^26/);
  const displayed=evaluateOrientedDisplay(definition,exactInput('120000000000000000000000000001.25'));
  const again=definition.evaluate(displayed.number+Number(displayed.period),displayed.inputUncertainty);
  for(const [id,b] of displayed.frame.bodies){const other=again.bodies.get(id);close([...b.origin,...b.x,...b.y,...b.z],[...other.origin,...other.x,...other.y,...other.z],1e-8);}
  a.throws(()=>evaluateOrientedDisplay({artifact:{format:'gear-invest.assembly-replay'}},f(1)),/only/);
  observations.oriented={period:displayed.period,exactRoot:displayed.exactRoot,displayRoot:displayed.displayRoot,channels:definition.artifact.mechanism.solution.states.length,allowance:displayed.frame.absoluteErrorBoundTurns};
});

// Independent point oracle: explicit axis rotations; no SDK binding or THREE matrix expected values.
const rx=(v,t)=>[v[0],v[1]*Math.cos(t)-v[2]*Math.sin(t),v[1]*Math.sin(t)+v[2]*Math.cos(t)];
const ry=(v,t)=>[v[0]*Math.cos(t)+v[2]*Math.sin(t),v[1],-v[0]*Math.sin(t)+v[2]*Math.cos(t)];
const rz=(v,t)=>[v[0]*Math.cos(t)-v[1]*Math.sin(t),v[0]*Math.sin(t)+v[1]*Math.cos(t),v[2]];
const num=f=>Number(f.numerator)/Number(f.denominator);
function oracle(v,body,angle,alternate,local=false){
  let p=ry(v.map((n,i)=>n-[7,3,2][i]),5*Math.PI/6);if(alternate)p=rx(p,Math.PI/2);
  const m=body.fixedFrameMm;p=[0,1,2].map(i=>num(m.x[i])*p[0]+num(m.y[i])*p[1]+num(m.z[i])*p[2]);p=rz(p,angle);
  p=p.map((n,i)=>(n+num(m.origin[i]))*.001+[.08,-.03,.02][i]);
  return local?rx(p.map((n,i)=>(n-[.017,-.011,.004][i])/1.25),-Math.PI/6):p;
}
const transform=(m,p)=>p.map((_,r)=>m[r]*p[0]+m[4+r]*p[1]+m[8+r]*p[2]+m[12+r]);
check('G3 actual GLB points/directions/full matrices: Y150 and noncommuting X90/Y150 mount',async()=>{
  const d=readAssemblyReplay(bytes('coaxial')),i=d.createInstance('geometry'),body=d.payload.bodies.find(b=>b.reference.localId==='rotor/A'),shaft=d.payload.shafts.find(s=>s.reference.localId==='rotor/input');
  const parent=new THREE.Matrix4().makeRotationX(Math.PI/6).scale(new THREE.Vector3(1.25,1.25,1.25));parent.setPosition(.017,-.011,.004);
  const placement=[...assemblyIdentityMatrix];placement[12]=.08;placement[13]=-.03;placement[14]=.02;
  for(const alternate of [false,true]){const asset=await loadArrowAsset(imageBytes(alternate?'alternate':'arrow'),sha),bindings=new AssemblyAssetBindings(i);bindings.setPlacement(placement,.001);bindings.bind(binding(i,asset,body));
    const positions=asset.geometry.attributes.position;a.ok(positions.count>=14);const landmarks=[[7,1,2],[37,3,2],[27,8,2],[7,1,6]];
    // Actual GLTFLoader node is already in meters; the adapter extracts its mm accessors.
    for(const p of landmarks){let native=ry(p.map(n=>n*.001),5*Math.PI/6);if(alternate)native=rx(native,Math.PI/2);close(transform(asset.nativeNodeMatrixMeters,p),native);}
    for(const p of landmarks)a.ok(Array.from({length:positions.count},(_,k)=>[positions.getX(k),positions.getY(k),positions.getZ(k)]).some(v=>JSON.stringify(v)===JSON.stringify(p)),'Landmark is a REAL vertex');
    for(const root of ['1/4','-1/4','12000000000000000000000000000000000000000000000000000000000001/4']){
      const u=exactInput(root),frame=i.evaluateAffine(u),pose=bindings.apply(frame,parent.toArray())[0];a.equal(pose.status,'displayApproximation');
      const n=BigInt(u.numerator),den=BigInt(u.denominator),angle=Number(((n%den)+den)%den)/Number(den)*2*Math.PI;
      for(const p of landmarks){close(transform(pose.worldMatrix,p),oracle(p,body,angle,alternate));close(transform(pose.localMatrix,p),oracle(p,body,angle,alternate,true));}
      const origin=oracle([0,0,0],body,angle,alternate,true),basis=[[1,0,0],[0,1,0],[0,0,1]].flatMap(p=>[...oracle(p,body,angle,alternate,true).map((n,j)=>n-origin[j]),0]);
      close(pose.localMatrix,[...basis,...origin,1]);
      const scalar=scalarBodyMatrix(body,shaft,frame.shafts.get(assemblyReferenceKey(shaft.reference)));
      const scalarWorld=new THREE.Matrix4().fromArray(bindings.displayFromWorldMillimeters).multiply(scalar).multiply(new THREE.Matrix4().fromArray(asset.correction));
      close(scalarWorld.toArray(),pose.worldMatrix);
    }
    bindings.dispose();asset.dispose();
  }d.dispose();
});
check('G3 deliberate sign/mounting/parent/unit/Euler mutations fail the point oracle',async()=>{
  const d=readAssemblyReplay(bytes('coaxial')),i=d.createInstance('mutations'),body=d.payload.bodies.find(b=>b.reference.localId==='rotor/A'),asset=await loadArrowAsset(imageBytes('arrow'),sha);
  const b=new AssemblyAssetBindings(i),placement=[...assemblyIdentityMatrix];placement[12]=.08;placement[13]=-.03;placement[14]=.02;b.setPlacement(placement,.001);b.bind(binding(i,asset,body));
  const parent=new THREE.Matrix4().makeRotationX(Math.PI/6).scale(new THREE.Vector3(1.25,1.25,1.25));parent.setPosition(.017,-.011,.004);
  const frame=i.evaluateAffine(f(1,4)),pose=b.apply(frame,parent.toArray())[0],v=[37,3,2],expected=oracle(v,body,Math.PI/2,false);
  const differs=p=>a.ok(p.some((n,j)=>Math.abs(n-expected[j])>1e-5));
  differs(oracle(v,body,-Math.PI/2,false));
  differs(transform(new THREE.Matrix4().fromArray(pose.worldMatrix).multiply(asset.initialMount).toArray(),v));
  differs(transform(pose.localMatrix,v)); // local mistaken for world (parent skipped)
  differs(transform(parent.clone().multiply(new THREE.Matrix4().fromArray(pose.worldMatrix)).toArray(),v));
  differs(expected.map(n=>n*.001));
  const euler=new THREE.Euler().setFromRotationMatrix(asset.initialMount);euler.z=Math.PI/2;
  const wrong=new THREE.Matrix4().fromArray(b.displayFromWorldMillimeters).multiply(new THREE.Matrix4().makeRotationFromEuler(euler)).multiply(new THREE.Matrix4().makeTranslation(-7,-3,-2));differs(transform(wrong.toArray(),v));
  a.throws(()=>scalarBodyMatrix({...body,p:f(1,7)},d.payload.shafts.find(s=>s.reference.localId==='rotor/input'),f(1,4)),/calibration/);
  b.dispose();asset.dispose();d.dispose();
});
check('G4 one source, two instances, shared GLB survives one instance disposal',async()=>{
  const d=readAssemblyReplay(bytes('coaxial')),left=d.createInstance('left'),right=d.createInstance('right'),asset=await loadArrowAsset(imageBytes('arrow'),sha);
  const x=asset.makeNode(),y=asset.makeNode();let disposed=0;asset.geometry.addEventListener('dispose',()=>disposed++);
  a.equal(x.geometry,y.geometry);left.dispose();x.removeFromParent();a.equal(disposed,0);a.equal(right.evaluateAffine(f(1)).instanceId,'right');
  right.dispose();y.removeFromParent();asset.dispose();asset.dispose();a.equal(disposed,1);a.throws(()=>asset.makeNode(),/disposed/);d.dispose();
});
const deferred=()=>{let resolve,reject;const promise=new Promise((r,j)=>{resolve=r;reject=j;});return {promise,resolve,reject};};
function candidate(key){const definition=readAssemblyReplay(bytes(key)),instance=definition.createInstance('same-local-id');let disposed=false;return {definition,instance,key,get disposed(){return disposed;},dispose(){disposed=true;definition.dispose();}};}
for(const pattern of ['B2-before-A1','A3-before-A1','B2-before-A1-failure','cancel-late-success','cancel-late-failure','retry-success','shutdown-late-success'])check('G4 controlled '+pattern,async()=>{
  let current=null,errors=[];const adopted=[];const controller=modelLoads({adopt:c=>{current?.dispose();current=c;adopted.push(c.key);},report:e=>errors.push(e.message)});
  const old=deferred(),oldRun=controller.load(()=>old.promise),A1=candidate('coaxial');
  if(pattern==='cancel-late-success'||pattern==='cancel-late-failure'){controller.cancel();if(pattern.endsWith('failure')){A1.dispose();old.reject(Error('old failure'));}else old.resolve(A1);await oldRun;a.equal(current,null);a.equal(errors.length,0);a.ok(A1.disposed);}
  else if(pattern==='shutdown-late-success'){controller.dispose();old.resolve(A1);a.equal(await oldRun,'stale');a.ok(A1.disposed);a.equal(current,null);await a.rejects(()=>controller.load(()=>Promise.resolve(candidate('variant'))),/disposed/);}
  else if(pattern==='A3-before-A1'){
    // ALL THREE requests start before the first completion; B2 completes last.
    const middle=deferred(),B2=candidate('variant'),middleRun=controller.load(()=>middle.promise),A3=candidate('coaxial');
    await controller.load(()=>Promise.resolve(A3));a.equal(A3.definition.replayId,A1.definition.replayId);
    old.resolve(A1);a.equal(await oldRun,'stale');a.ok(A1.disposed);
    middle.resolve(B2);a.equal(await middleRun,'stale');a.ok(B2.disposed);a.equal(current,A3);a.ok(!A3.disposed);a.equal(errors.length,0);
    a.equal(current.instance.evaluateAffine(f(1)).instanceId,'same-local-id');
  }
  else {
    const B2=candidate('variant');await controller.load(()=>Promise.resolve(B2));
    if(pattern==='retry-success'){controller.cancel();await controller.load(()=>Promise.reject(Error('latest failure')));a.deepEqual(errors,['latest failure']);await controller.load(()=>Promise.resolve(candidate('renamed')));}
    const selected=current;
    if(pattern==='B2-before-A1-failure'){A1.dispose();old.reject(Error('A1 failed after B2 adopted'));a.equal(await oldRun,'stale-error');a.equal(errors.length,0);}
    else {old.resolve(A1);a.equal(await oldRun,'stale');}
    a.equal(current,selected);a.ok(!current.disposed);a.ok(A1.disposed);a.equal(current.instance.evaluateAffine(f(1)).instanceId,'same-local-id');
  }
  current?.dispose();controller.dispose();
});
check('G5 fresh definitions restore exact root and View; same local ID in different source is refused',async()=>{
  const original=bytes('coaxial'),asset=await loadArrowAsset(imageBytes('arrow'),sha),d=readAssemblyReplay(original),i=d.createInstance('saved'),b=new AssemblyAssetBindings(i);b.bind(binding(i,asset));
  const u=exactInput('-120000000000000000000001/4'),before=i.evaluateAffine(u),saved=JSON.stringify({original:Buffer.from(d.originalBytes()).toString('base64'),view:b.saveView(),exactRoot:u,assetId:asset.assetId});
  b.dispose();d.dispose();const loaded=JSON.parse(saved),fresh=readAssemblyReplay(Buffer.from(loaded.original,'base64')),j=fresh.createInstance('saved'),view=restoreAssemblyView(j,loaded.view);
  a.equal((await fresh.verifyIntegrity(sha)).rawDigest,'Pass');const after=j.evaluateAffine(loaded.exactRoot);a.deepEqual(after,before);a.deepEqual([...after.shafts],[...before.shafts]);view.inspect().forEach(x=>checkExampleAsset(x,asset));
  const different=readAssemblyReplay(bytes('variant')),k=different.createInstance('saved');a.deepEqual(fresh.payload.bodies[0].reference,different.payload.bodies[0].reference);a.throws(()=>restoreAssemblyView(k,loaded.view),/Stale/);
  a.throws(()=>view.apply(k.evaluateAffine(f(0))),/Stale/);a.throws(()=>checkExampleAsset({...view.inspect()[0],assetId:'sha256:'+'0'.repeat(64)},asset),/identity/);
  a.deepEqual(fresh.originalBytes(),new Uint8Array(original));view.dispose();fresh.dispose();different.dispose();asset.dispose();
});
check('G6 ordinary public imports and statically built example controls exist',()=>{
  for(const entry of ['@gearinvest/replay','@gearinvest/replay/assembly','@gearinvest/replay/oriented','@gearinvest/presentation/assembly']){ const paths={'@gearinvest/replay':'index','@gearinvest/replay/assembly':'assembly','@gearinvest/replay/oriented':'oriented','@gearinvest/presentation/assembly':'assembly'}; const pkg=entry.startsWith('@gearinvest/presentation')?'presentation':'replay'; a.equal(import.meta.resolve(entry),new URL('../../packages/'+pkg+'/dist/'+paths[entry]+'.js',import.meta.url).href); }
  const html=readFileSync(resolve(root,'dist/index.html'),'utf8');for(const id of ['load','cancel','bind-glb','route','root','save-local','save','save-view','reopen','remove','close'])a.ok(html.includes('id="'+id+'"'));
  a.ok(!html.includes('http://cdn'));observations.surface='Node headless only; no browser/pixel/device claim from this test';
});
after(()=>{writeFileSync(resolve(root,`headless-${process.pid}.json`),JSON.stringify({status:results.every(r=>r.status==='pass')?'pass':'fail',processId:process.pid,node:process.version,tests:results.length,assertions,assertionsByGroup:counts,results,observations},null,2)+'\n',{flag:'wx'});});
