import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { resolve, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';

const [rootArg, cliArg, sourceArg, outputArg, countArg, firstNumeratorArg] = process.argv.slice(2);
const root = resolve(rootArg), cli = resolve(cliArg), sourceUtf8 = await readFile(sourceArg, 'utf8'), output = resolve(outputArg), count = Number(countArg ?? 4352);
const firstNumerator=Number(firstNumeratorArg??400000);
assert.ok(Number.isSafeInteger(firstNumerator),'Exact schedule start required.');
assert.ok(Number.isInteger(count) && count >= 17, 'This protocol checks the 16-entry stale window; use at least 17 requests.');
await mkdir(output, { recursive: true });
function managed() {
  const child = spawn('dotnet', [cli], { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true }); const pending = [];
  createInterface({ input: child.stdout, crlfDelay: Infinity }).on('line', line => { const next = pending.shift(); if (!next) throw new Error('Unsolicited managed result'); try { next.resolve(JSON.parse(line)); } catch(e) { next.reject(e); } });
  child.on('exit', code => { for (const p of pending.splice(0)) p.reject(new Error('Managed process exited ' + code)); });
  return { send: command => new Promise((resolve, reject) => { pending.push({ resolve, reject }); child.stdin.write(JSON.stringify(command) + '\n'); }), close: () => child.stdin.end() };
}
const started = performance.now();
const { dotnet } = await import(pathToFileURL(join(root, '_framework/dotnet.js')));
const runtime = await dotnet.create(); const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
const wasm = command => JSON.parse(exports.BrowserRuntime.Dispatch(JSON.stringify(command)));
const initMs = performance.now() - started;
let native = managed(); let comparisons = 0, maxNumericDifference = 0;
function same(a, b, path = '') {
  if (typeof a === 'number' && typeof b === 'number') {
    const delta = Math.abs(a - b); maxNumericDifference = Math.max(delta, maxNumericDifference);
    assert.ok(delta <= (path.includes('matrix') ? 1e-8 : 1e-10), `${path}: ${a} != ${b}`); return;
  }
  if (a !== null && b !== null && typeof a === 'object' && typeof b === 'object') {
    assert.deepEqual(Object.keys(a), Object.keys(b), path); for (const k of Object.keys(a)) same(a[k], b[k], path + '/' + k); return;
  }
  assert.equal(a, b, path);
}
async function both(command) {
  const b = wasm(command); const a = await native.send(command);
  try { same(a, b); } catch(error) {
    await writeFile(join(output,'cross-host-failure.json'),JSON.stringify({comparisons,command,error:String(error),native:a,wasm:b},null,2),{flag:'wx'});
    native.close(); throw error;
  }
  ++comparisons; return b;
}
function fraction(n, d = 1) { n = BigInt(n); d = BigInt(d); let a = n < 0n ? -n : n, b = d; while (b) [a,b] = [b,a%b]; return { numerator: String(n/a), denominator: String(d/a) }; }
function request(snapshot, id, q, kinds = []) {
  const s = snapshot.state, c = loaded.capabilities;
  const ports = Object.fromEntries(s.requiredInputPorts.map(p => [p, p === c.sunPort ? fraction(2,25) : fraction(3,10)]));
  return { id, sessionId: s.sessionId, definitionId: s.definitionId, expectedStateId: s.stateId, epoch: s.epoch, revision: s.revision, segments: [{ driverTurns: q, independentPorts: ports, observations: {}, events: kinds.map((kind,i)=>({ id:id+'/e'+i, sequence:String(BigInt(s.eventCursor)+BigInt(i)+1n), ordinal:String(i), kind })) }] };
}
let loaded = await both({ op: 'load', sourceUtf8, sessionId: 'cross-host-long', initialPlanet: '3/10' }); assert.equal(loaded.status, 'Ready');
for(const op of ['cancel','commit']) { const r=await both({op,token:null});assert.equal(r.status,'InvalidInput');assert.equal(r.committed,false); }
let state = loaded.snapshot; const samples = [], saved = [], timings = []; let firstRequest, latestRequest, maxLive = 0, maxSnapshotBytes = 0;
async function advance(r, expected = 'Accepted') {
  const t = performance.now(); const prepared = await both({ op: 'prepare', request: r });
  if (expected !== 'Accepted') { assert.equal(prepared.status, expected); return; }
  assert.equal(prepared.status, 'Prepared'); const result = await both({ op:'commit', token:prepared.token }); assert.equal(result.status,'Accepted'); state = result.snapshot;
  timings.push(performance.now()-t); maxLive=Math.max(maxLive,state.state.witnesses.length);maxSnapshotBytes=Math.max(maxSnapshotBytes,Buffer.byteLength(JSON.stringify(result))); return result;
}
for (let i=0;i<count;i++) {
  const selectedDrive=loaded.capabilities.profile==='bounded-selected-drive-spatial-runtime-checkpoint-v1';
  // Keep actual capture/lock witnesses live across epoch boundaries in the selected-role path.
  const events=selectedDrive&&i===250?['LockWorldCarrier']:selectedDrive&&i>250&&i<260?[]:selectedDrive&&i===260?['Capture']:
    selectedDrive&&i===506?['LockPlanetRelative']:selectedDrive&&i>506&&i<516?[]:selectedDrive&&i===516?['Capture']:[i%2===0?'Release':'Capture'];
  const r = request(state,'r-'+i,fraction(firstNumerator+i,10000000),events); firstRequest??=r; latestRequest=r;
  await advance(r);
  if (i%256===255 || i===count-1) {
    const t=performance.now();const ck=wasm({op:'checkpoint'});const nck=await native.send({op:'checkpoint'});assert.equal(ck.status,'CheckpointCreated');assert.equal(nck.status,'CheckpointCreated');
    // Raw numerical bytes are not the semantic contract. Cross-restore each host's independently produced bytes.
    const restoredWasm=wasm({op:'restore',checkpointUtf8:nck.checkpointUtf8}); const restoredNative=await native.send({op:'restore',checkpointUtf8:ck.checkpointUtf8}); same(restoredNative,restoredWasm);assert.equal(restoredWasm.status,'Restored');state=restoredWasm.snapshot;
    saved.push({ revision:state.state.revision, epoch:state.state.epoch, cursor:state.state.eventCursor, mode:state.state.mode, lockWitnessPresent:state.state.lockWitness!==null, liveProvenance:state.state.witnesses.length, retainedLedger:state.state.ledger.length, bytes:Buffer.byteLength(ck.checkpointUtf8), createAndTwoHostRestoreMs:performance.now()-t, artifactId:ck.artifactId });
    await advance(r,'AlreadyApplied');
    if(i===count-1) await writeFile(join(output,'long.checkpoint.json'),ck.checkpointUtf8,{flag:'wx'});
  }
}
const negativeBefore=state.state.stateId;
const under=request(state,'missing',fraction(1,20));under.segments[0].independentPorts={};await advance(under,'Underdetermined');
await advance({...request(state,'foreign',fraction(1,20)),definitionId:'foreign'},'ForeignSnapshot');
const unresolved=request(state,'uncertain-observation',fraction(1,20));unresolved.segments[0].observations[loaded.capabilities.sunPort]=fraction(0);
await advance(unresolved,loaded.capabilities.couplingShaft===loaded.capabilities.prescribedShaft?'InconsistentObservation':'GuardIndeterminate');
const inconsistent=request(state,'conflicting-observation',fraction(1,20));inconsistent.segments[0].observations[loaded.capabilities.planetPort]=fraction(1);await advance(inconsistent,'InconsistentObservation');
assert.equal((await both({op:'snapshot'})).snapshot.state.stateId,negativeBefore);
await advance(firstRequest,'StaleSnapshot');
const conflict={...latestRequest,segments:[{...latestRequest.segments[0],driverTurns:fraction(1,20)}]};await advance(conflict,'IdempotencyConflict');
// Multiple genuine new modes, forward/backward input and atomic rejection. No answer table supplies coordinates.
for (const [id,q,kinds] of [['release',fraction(13,200),['Release']],['independent',fraction(7,100),[]],['capture',fraction(7,100),['Capture']],['world',fraction(3,50),['LockWorldCarrier']],['world-move',fraction(13,200),[]],['relative',fraction(13,200),['LockPlanetRelative']],['relative-move',fraction(7,100),[]]]) {
  const r=request(state,id,q,kinds);const result=await advance(r);samples.push({request:r,state:result.snapshot.state});
}
const before=state.state.stateId;await advance(request(state,'outside',fraction(Math.ceil(loaded.capabilities.driverMaximumTurns)+1)),'WindingBoundary');assert.equal((await both({op:'snapshot'})).snapshot.state.stateId,before);
const missing=request(state,'extra',fraction(7,100));missing.segments[0].independentPorts[loaded.capabilities.planetPort]=fraction(0);await advance(missing,'ModeInputOwnershipConflict');
// Exact same candidate after compaction, plus native fresh-process restore (no full history input).
const checkpoint=wasm({op:'checkpoint'});await writeFile(join(output,'locked.checkpoint.json'),checkpoint.checkpointUtf8,{flag:'wx'});
native.close();native=managed();const restored=await native.send({op:'restore',checkpointUtf8:checkpoint.checkpointUtf8});assert.equal(restored.status,'Restored');same(restored,wasm({op:'restore',checkpointUtf8:checkpoint.checkpointUtf8}));
const continued=await advance(request(state,'fresh-process-continue',fraction(3,50)));assert.equal(continued.status,'Accepted');
// Candidate cancellation and late commit acknowledgement exercise the real shared host, not a mocked engine.
const pending=await both({op:'prepare',request:request(state,'cancelled',fraction(7,100))});assert.equal(pending.status,'Prepared');
const cancelled=await both({op:'cancel',token:pending.token});assert.equal(cancelled.status,'CancelledBeforeCommit');assert.equal((await both({op:'snapshot'})).snapshot.state.stateId,state.state.stateId);
const finalRequest=request(state,'last-commit',fraction(7,100));const p=await both({op:'prepare',request:finalRequest});const final=await both({op:'commit',token:p.token});assert.equal((await both({op:'cancel',token:p.token})).status,'TooLateCommitted');
const report={profile:loaded.capabilities.profile,sourceArtifactId:loaded.snapshot.sourceArtifactId,node:process.version,wasmRuntime:'Microsoft.NETCore.App.Runtime.Mono.browser-wasm 10.0.12',managedRuntime:'.NET 8 host',initMs,lifetimeRequests:count,comparisons,maxNumericDifference,maxLiveProvenance:maxLive,maxSnapshotBytes,checkpoints:saved,latency:{metric:'sequential native IPC plus synchronous WASM prepare and commit, includes JSON',count:timings.length,p50:timings.toSorted((a,b)=>a-b)[Math.floor(timings.length*.5)],p95:timings.toSorted((a,b)=>a-b)[Math.floor(timings.length*.95)]},finalStateId:final.snapshot.state.stateId,nodeMemory:process.memoryUsage(),unmeasured:['browser heap','mobile memory','GPU','WASM reserved/used linear memory separate from process'],freshManagedRestore:'PASS',checkpointSha256:createHash('sha256').update(checkpoint.checkpointUtf8).digest('hex')};
await writeFile(join(output,'cross-host.json'),JSON.stringify(report,null,2),{flag:'wx'});await writeFile(join(output,'mode-samples.json'),JSON.stringify(samples),{flag:'wx'});native.close();console.log(JSON.stringify(report,null,2));
