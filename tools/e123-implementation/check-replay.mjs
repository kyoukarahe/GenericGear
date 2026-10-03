import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';
import { readWindingReplay } from '../../packages/replay/dist/winding.js';
import { readMechanicalModeReplay } from '../../packages/replay/dist/mechanicalModes.js';
import { readDifferentialReplay } from '../../packages/replay/dist/differential.js';

const root=resolve(process.argv[2]??'artifacts/e123-implementation/example');
const sha=b=>createHash('sha256').update(b).digest('hex');
const parse=b=>JSON.parse(Buffer.from(b).toString('utf8'));
const cases=[];
let negativeChecks=0,framesCompared=0;
function reject(label,fn){assert.throws(fn,undefined,label);negativeChecks++;}
function frameCheck(f){
  assert.equal(f.coordinates.length,6);
  assert.equal(f.winding.quality,'NumericResidualOnly');
  assert.equal(f.winding.solutionErrorBoundTurns,null);
  const pins=f.winding.pins,matrices=new Map(f.matricesMm.map(n=>[n.id,n.matrix]));
  for(let i=0;i<pins.length;i++){
    const p=pins[i].positionMm,m=matrices.get('connected/pin-'+String(i).padStart(2,'0'));
    assert.deepEqual(m.slice(12,14),p);
    if(i+1<pins.length){
      const next=pins[i+1].positionMm,link=matrices.get('connected/link-'+String(i).padStart(2,'0'));
      const length=Math.hypot(next[0]-p[0],next[1]-p[1]);
      assert.ok(Math.abs(link[0]*length+p[0]-next[0])<1e-8);
      assert.ok(Math.abs(link[1]*length+p[1]-next[1])<1e-8);
    }
  }
  const estimate=id=>{const v=f.coordinates.find(c=>c.shaftId===id).value;return v.kind==='ExactRational'?Number(v.exact.numerator)/Number(v.exact.denominator):v.estimate;};
  // Independent numerical check of the example's exact authored suffix, not a consumer solver.
  assert.ok(Math.abs(estimate('suffix/out')-(estimate('carrier')/2-3/16))<1e-12);
  framesCompared++;
}
function rewrite(bytes,edit){
  const outer=parse(bytes),payload=parse(Buffer.from(outer.payloadUtf8,'base64'));
  const recording=parse(Buffer.from(payload.recordingUtf8,'base64'));
  edit(payload,recording);recording.results=payload.results;
  const record=Buffer.from(JSON.stringify(recording));payload.recordingUtf8=record.toString('base64');payload.recordingId=sha(record);
  const body=Buffer.from(JSON.stringify(payload));outer.payloadUtf8=body.toString('base64');outer.replayId=sha(body);
  return Buffer.from(JSON.stringify(outer));
}
async function inspect(folder,mode=false,resumed=false){
  const prefix=resumed?'resumed-':'',bytes=await readFile(join(root,folder,prefix+'replay.json'));
  const recordBytes=await readFile(join(root,folder,prefix+'recording.json')),recording=parse(recordBytes);
  const reader=mode?readMechanicalModeReplay:readWindingReplay;
  const original=Buffer.from(bytes),r=reader(bytes);
  bytes[0]=0;assert.deepEqual(Buffer.from(r.originalBytes()),original,'Buffer must not alias reader storage');
  const copy=r.originalBytes();copy.fill(0);assert.deepEqual(Buffer.from(r.originalBytes()),original);
  const verdict=await r.verifyIntegrity(sha);
  assert.equal(verdict.rawDigest,'Pass');assert.equal(verdict.currentSourceRebuild,'notPerformed');assert.equal(verdict.browserMechanicalValidation,'notPerformed');
  assert.equal(r.payload.recordingId,sha(recordBytes));assert.deepEqual(r.payload.results,recording.results);
  const expected=mode?[recording.results.initial,...recording.results.attempts.flatMap(a=>a.state?[...a.samples,a.state]:[])]:[recording.results.initial,...recording.results.attempts.flatMap(a=>a.frames)];
  assert.equal(r.samples.length,expected.length);
  r.samples.forEach((sample,i)=>{assert.deepEqual(r.selectSample(i),expected[i]);frameCheck(sample.frame??sample);});
  assert.ok(Object.isFrozen(r.samples)&&Object.isFrozen(r.samples[0]));
  reject('immutable sample',()=>{r.samples[0].foreign=true;});
  reject('unrecorded sample',()=>r.selectSample(.5));reject('outside sample',()=>r.selectSample(r.samples.length));
  reject('unknown attempt',()=>r.selectAttempt(-1));reject('old E2 reader must not silently accept new profile',()=>readDifferentialReplay(original));
  reject('other new reader must refuse',()=>(mode?readWindingReplay:readMechanicalModeReplay)(original));
  reject('duplicate JSON field',()=>reader(Buffer.from(original.toString().replace('{','{"format":"duplicate",'))));
  reject('unknown outer field',()=>reader(Buffer.from(JSON.stringify({...parse(original),foreign:true}))));
  reject('oversized input',()=>reader(new Uint8Array(4194305)));
  const badDigest=parse(original);badDigest.replayId='0'.repeat(64);
  await assert.rejects(reader(JSON.stringify(badDigest)).verifyIntegrity(sha));negativeChecks++;
  const first=p=>mode?p.results.initial.frame:p.results.initial;
  const edits=[
    ['invented exact',p=>{const v=first(p).coordinates.find(c=>c.value.kind==='NumericResidualOnly').value;v.kind='ExactRational';}],
    ['invented error bound',p=>{first(p).winding.solutionErrorBoundTurns=1e-8;}],
    ['foreign owner',p=>{first(p).coordinates[0].shaftId='foreign';}],
    ['missing material pin',p=>{first(p).winding.pins.pop();}],
    ['failed residual',p=>{first(p).winding.pitchResidualMm=.01;}],
    ['non-affine matrix',p=>{first(p).matricesMm[0].matrix[15]=0;}],
    ['partial success',p=>{const a=p.results.attempts.find(a=>a.status!=='Accepted');a.appliedSegments='1';}],
    ['broken source',p=>{first(p).definitionId='0'.repeat(64);}],
  ];
  if(mode)edits.push(['wrong required input',p=>{p.results.initial.requiredInputPorts=[];}],['lock without reference',p=>{const a=p.results.attempts.find(a=>a.state?.mode==='WorldCarrierLock');a.state.lockReference=null;}],['lost atomic revision',p=>{p.results.attempts.find(a=>a.state).state.revision='0';}]);
  for(const [label,edit]of edits)reject(label,()=>reader(rewrite(original,edit)));
  cases.push({folder,prefix,recordingId:r.payload.recordingId,replayId:r.replayId,replaySha256:sha(original),samples:r.samples.length,
    finalIdentity:mode?r.payload.results.finalStateId:r.payload.results.finalSnapshotId,pins:(r.samples[0].frame??r.samples[0]).winding.pins.length,
    modes:mode?[...new Set(r.samples.map(s=>s.mode))]:undefined,integrity:verdict});
}
for(const folder of ['drive','scaled','circular'])await inspect(folder);
await inspect('modes',true);await inspect('modes',true,true);
console.log(JSON.stringify({verdict:'PASS',framesCompared,negativeChecks,cases}));
