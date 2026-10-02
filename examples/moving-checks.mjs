// Public package entries, actual exported sources and fresh source-built DLL observations.
import assert from 'node:assert/strict';
import {test} from 'node:test';
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {readCarrierReplay} from '@gearinvest/replay/carrier';
import {readDifferentialReplay,readDifferentialObservation,DIFFERENTIAL_LIMITS} from '@gearinvest/replay/differential';
import {readAssemblyReplay} from '@gearinvest/replay/assembly';
import {DifferentialSession} from './differential/web/session.js';
import {assetLocal,multiply,identity,translation,rendererParent} from './carrier/web/asset-transform.js';
const root=resolve(import.meta.dirname,'..');
const read=(profile,name='standalone',file=`mechanism.${profile}-replay.json`)=>readFileSync(resolve(root,'examples',profile,'web/samples',name,file));
const observation=(profile,name)=>JSON.parse(readFileSync(resolve(root,'generated/moving',profile,name,'reopen.json'),'utf8'));
const sha=b=>createHash('sha256').update(b).digest('hex');
const f=(n,d=1)=>({numerator:String(n),denominator:String(d)});
const fraction=s=>{const [n,d='1']=s.split('/');return f(n,d);};
const text=r=>r.denominator==='1'?r.numerator:`${r.numerator}/${r.denominator}`;
const close=(a,b,tol=1e-10)=>assert.ok(Math.abs(a-b)<=tol,`${a} != ${b}`);
function matrices(nodes,expected){
  assert.deepEqual(nodes.map(n=>n.id).sort(),Object.keys(expected).sort());
  for(const node of nodes)node.matrixMm.forEach((v,i)=>close(v,expected[node.id][i],i>=12?1e-8:1e-10));
}
function mutate(change,sync=false){
  const outer=JSON.parse(read('differential')),p=JSON.parse(Buffer.from(outer.payloadUtf8,'base64'));
  change(p);
  if(sync){const s=JSON.parse(Buffer.from(p.sourceArtifactUtf8,'base64'));s.compiled=p.compiled;
    const b=Buffer.from(JSON.stringify(s));p.sourceArtifactUtf8=b.toString('base64');p.sourceRawSha256=sha(b);}
  const b=Buffer.from(JSON.stringify(p));return JSON.stringify({...outer,replayId:sha(b),payloadUtf8:b.toString('base64')});
}
for(const name of ['standalone','prefix'])test(`carrier/${name}: source-built DLL and Web exact coordinates/world matrices`,async()=>{
  const r=readCarrierReplay(read('carrier',name)),i=r.createInstance('ordinary');
  assert.equal((await r.verifyIntegrity(sha)).browserMechanicalValidation,'notPerformed');
  const rebuilt=observation('carrier',name);assert.equal(rebuilt.status,'PASS');
  assert.equal(rebuilt.ArtifactHash,r.payload.artifactId);
  for(const s of rebuilt.samples){const got=i.evaluate(fraction(s.root));
    assert.equal(text(got.carrierCommon),s.carrier);assert.equal(text(got.planetCommon),s.planetCommon);
    for(const shaft of s.shafts){const v=got.shafts.find(x=>x.id===shaft.ShaftId);
      assert.equal(text(v.world),shaft.world);assert.equal(v.carrierRelative?text(v.carrierRelative):null,shaft.relative);}
    matrices(got.display.nodes,s.matrices);
  }
  assert.deepEqual(i.evaluate(f(1,13)).shafts.find(s=>s.id==='planet').world,name==='standalone'?f(11,13):f(-5,39));
  i.dispose();assert.throws(()=>i.evaluate(f(0)),/disposed/i);r.dispose();
});
for(const name of ['standalone','alternate','third-basis','prefix','held','custom','display-unavailable'])
  test(`differential/${name}: source-built DLL and Web vector/matrix correspondence`,async()=>{
    const r=readDifferentialReplay(read('differential',name)),i=r.createInstance('ordinary');
    assert.equal((await r.verifyIntegrity(sha)).currentSourceRebuild,'notPerformed');
    const rebuilt=observation('differential',name);assert.equal(rebuilt.status,'PASS');assert.equal(rebuilt.ArtifactHash,r.payload.artifactId);
    for(const s of rebuilt.observations){const input=Object.fromEntries(Object.entries(s.input).map(([id,v])=>[id,fraction(v)])),e=i.evaluate(input);
      assert.deepEqual(Object.fromEntries(Object.entries(e.coordinates).map(([id,v])=>[id,text(v)])),s.coordinates);
      assert.equal(text(e.carrierCommon),s.carrier);assert.equal(text(e.planetCommon),s.planet);assert.equal(text(e.planetRelative),s.relative);
      assert.equal(e.display.status,s.displayAvailable?'displayApproximation':'unavailable');matrices(e.display.nodes,s.matrices);
    }r.dispose();
  });
for(const [s,c,p,rel] of [[f(0),f(1,4),f(11,4),f(5,2)],[f(1,10),f(1,4),f(7,4),f(3,2)],
  [f(1,10),f(0),f(-1),f(-1)],[f(1,4),f(1,4),f(1,4),f(0)],[f(-1,5),f(1,4),f(19,4),f(9,2)]])
  test(`independent two-input oracle: sun ${text(s)}, carrier ${text(c)}`,()=>{
    const r=readDifferentialReplay(read('differential')),e=r.createInstance('oracle').evaluate({'sun-port':s,'carrier-port':c});
    assert.deepEqual(e.coordinates.planet,p);assert.deepEqual(e.planetRelative,rel);r.dispose();
  });
test('alternate bases, actual prefix, held sun and nondefault signed reference',()=>{
  const cases=[['alternate',{'sun-port':f(0),'planet-port':f(1)},'carrier',f(1,11)],
    ['third-basis',{'carrier-port':f(1,4),'planet-port':f(7,4)},'sun',f(1,10)],
    ['prefix',{'drive-port':f(1,13),'sun-port':f(1,7)},'planet',f(-6,13)],
    ['held',{'carrier-port':f(1,4)},'planet',f(11,4)],
    ['custom',{'custom-carrier-port':f(1,4),'custom-sun-port':f(1,10)},'custom-planet',f(-537,920)]];
  for(const [name,input,key,want] of cases){const r=readDifferentialReplay(read('differential',name));
    assert.deepEqual(r.createInstance('oracle').evaluate(input).coordinates[key],want);r.dispose();}
});
test('huge exact turns and unavailable display remain separate',()=>{
  const r=readDifferentialReplay(read('differential')),large=10n**60n;
  assert.deepEqual(r.createInstance('large').evaluate({'carrier-port':f(4n*large+1n,4),'sun-port':f(1,10)}).coordinates.planet,f(44n*large+7n,4));r.dispose();
  const u=readDifferentialReplay(read('differential','display-unavailable')),e=u.createInstance('large').evaluate({'carrier-port':f(1,4),'sun-port':f(1,10)});
  assert.deepEqual(e.coordinates.planet,f(7,4));assert.equal(e.display.status,'unavailable');assert.deepEqual(e.display.nodes,[]);u.dispose();
});
test('one carrier application and host parent/pivot correction have independent point oracles',()=>{
  for(const profile of ['carrier','differential']){
    const r=profile==='carrier'?readCarrierReplay(read(profile)):readDifferentialReplay(read(profile));
    const e=r.createInstance('asset').evaluate(profile==='carrier'?f(1,4):{'sun-port':f(1,10),'carrier-port':f(1,4)});
    const m=e.display.nodes.find(n=>n.id==='planet-body').matrixMm;
    close(m[12],0);close(m[13],55);close(m[0],0);close(m[1],-1);
    for(const parent of [identity,rendererParent])for(const pivot of [identity,translation(-7,0)]){
      const actual=multiply(parent,assetLocal(m,parent,pivot)),want=multiply(m,pivot);actual.forEach((v,k)=>close(v,want[k]));}
    r.dispose();
  }
});
test('partial/inconsistent documents are readonly observations, never executable replay',()=>{
  const p=readDifferentialObservation(read('differential','standalone','partial.differential-analysis.json'));
  assert.equal(p.authority,'storedObservationOnly');assert.equal(p.compiled.status,'UndrivenRelativeMotion');
  assert.deepEqual(p.compiled.coordinates.filter(c=>c.isKnown).map(c=>c.shaftId),['sun']);assert.deepEqual(p.compiled.poseNodes,[]);
  const b=readDifferentialObservation(read('differential','standalone','inconsistent.differential-analysis.json'));
  assert.equal(b.compiled.status,'InconsistentConstraints');assert.ok(b.compiled.diagnostics[0].related.includes('ConstraintRow/boundary/c'));
  assert.throws(()=>readDifferentialReplay(read('differential','standalone','partial.differential-analysis.json')));
});
test('atomic input, byte ownership, instance isolation and disposal reject stale evaluation',async()=>{
  const bytes=read('differential'),padded=Buffer.concat([Buffer.from('unrelated'),bytes,Buffer.from('tail')]);
  const r=readDifferentialReplay(padded.subarray(9,9+bytes.length));padded.fill(0);assert.deepEqual(Buffer.from(r.originalBytes()),bytes);
  const a=r.createInstance('a'),b=r.createInstance('b'),input={'carrier-port':f(1,4),'sun-port':f(0)},before=a.evaluate(input);
  input['sun-port'].numerator='1';assert.deepEqual(before.input['sun-port'],f(0));
  for(const bad of [{'carrier-port':f(0)},{'carrier-port':f(0),wrong:f(0)},{'carrier-port':f(0),'sun-port':f(2,4)},
    {'carrier-port':f(0),'sun-port':f('1'.repeat(129))}])assert.throws(()=>a.evaluate(bad));
  let gets=0;assert.throws(()=>a.evaluate({get 'carrier-port'(){gets++;return f(0);},'sun-port':f(0)}));assert.equal(gets,0);
  assert.deepEqual(b.evaluate({'carrier-port':f(0),'sun-port':f(0)}).coordinates.planet,f(0));a.dispose();assert.throws(()=>a.evaluate(input),/disposed/i);
  let resume;const gate=new Promise(r=>resume=r),pending=r.verifyIntegrity(async bytes=>{await gate;return sha(bytes);});
  r.dispose();resume();await assert.rejects(pending,/disposed/i);
});
test('old format entry refusal, bounds and correctly rehashed structural corruption',()=>{
  assert.throws(()=>readCarrierReplay(read('differential')));assert.throws(()=>readAssemblyReplay(read('differential')));
  assert.throws(()=>readDifferentialReplay(read('differential','standalone','mechanism.differential.json')));
  assert.throws(()=>readDifferentialReplay('{"format":"x","format":"y"}'),/Duplicate/);
  assert.throws(()=>readDifferentialReplay(new Uint8Array(DIFFERENTIAL_LIMITS.documentBytes+1)));
  assert.throws(()=>readDifferentialReplay(mutate(p=>p.compiled.planetCommon.q['sun-port']=f(-11))),/correspondence/);
  assert.throws(()=>readDifferentialReplay(mutate(p=>p.compiled.poseNodes.find(n=>n.id==='planet-shaft').parentId='sun-shaft',true)),/recipe/);
  assert.throws(()=>readDifferentialReplay(mutate(p=>p.compiled.poseNodes[0].shaftId='foreign',true)),/owner/);
  assert.throws(()=>readDifferentialReplay(mutate(p=>p.compiled.planetCommon.q.extra=f(1),true)),/columns/);
  assert.throws(()=>readDifferentialReplay(mutate(p=>p.compiled.coordinates[0].isKnown=false,true)),/Knownness/);
  const r=readDifferentialReplay(read('differential'));for(let n=0;n<32;n++)r.createInstance('i'+n);assert.throws(()=>r.createInstance('overflow'),/limit/);r.dispose();
});
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {resolve,promise};};
test('actual page controller: A-B-A, invalid input/source and late digest never retain stale pose',async()=>{
  const s=new DifferentialSession(sha),old=deferred(),b=deferred(),fresh=deferred();
  const oldLoad=s.load(()=>old.promise),bLoad=s.load(()=>b.promise),newLoad=s.load(()=>fresh.promise);
  fresh.resolve(read('differential'));assert.equal(await newLoad,true);const id=s.definition.replayId;
  old.resolve(Buffer.from('invalid old A'));b.resolve(read('differential','alternate'));
  assert.equal(await oldLoad,false);assert.equal(await bLoad,false);assert.equal(s.definition.replayId,id);
  s.apply({'carrier-port':f(1,4),'sun-port':f(1,10)});assert.deepEqual(s.current.value.coordinates.planet,f(7,4));
  assert.throws(()=>s.apply({'sun-port':f(0)}));assert.equal(s.current,null);
  await assert.rejects(s.load(async()=>Buffer.from('invalid current source')));assert.equal(s.definition,null);s.dispose();
  const gate=deferred(),pending=new DifferentialSession(async bytes=>{await gate.promise;return sha(bytes);});
  const loading=pending.load(async()=>read('differential'));await Promise.resolve();pending.dispose();gate.resolve();
  assert.equal(await loading,false);assert.equal(pending.definition,null);
});
test('actual page controller: observation invalidates pose and sessions have no shared input',async()=>{
  const a=new DifferentialSession(sha),b=new DifferentialSession(sha);
  await a.load(async()=>read('differential'));await b.load(async()=>read('differential'));
  a.apply({'carrier-port':f(1,4),'sun-port':f(0)});b.apply({'carrier-port':f(0),'sun-port':f(0)});
  assert.deepEqual(a.current.value.coordinates.planet,f(11,4));assert.deepEqual(b.current.value.coordinates.planet,f(0));
  const epoch=a.epoch;await a.load(async()=>read('differential','standalone','partial.differential-analysis.json'),true);
  assert.notEqual(a.epoch,epoch);assert.equal(a.definition,null);assert.equal(a.current.kind,'observation');
  assert.throws(()=>a.apply({}),/Executable/);assert.equal(a.current,null);assert.deepEqual(b.current.value.coordinates.planet,f(0));a.dispose();b.dispose();
});
