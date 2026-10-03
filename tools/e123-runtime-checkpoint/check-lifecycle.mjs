import { Worker } from 'node:worker_threads';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
const [rootArg,sourceArg,outputArg]=process.argv.slice(2),root=resolve(rootArg);
const {MechanicalRuntimeClient}=await import(pathToFileURL(join(root,'runtime-client.js')));
const source=await readFile(sourceArg,'utf8'); const workerUrl=pathToFileURL(join(root,'runtime-worker.js')); const workers=[];
function factory(url){const w=new Worker(new URL('./node-worker.mjs',import.meta.url),{workerData:url.href});workers.push(w);return {postMessage:v=>w.postMessage(v),terminate:()=>w.terminate(),addEventListener:(kind,cb)=>w.on(kind,kind==='message'?data=>cb({data}):error=>cb({message:error.message}))};}
const a=new MechanicalRuntimeClient(workerUrl,factory),b=new MechanicalRuntimeClient(workerUrl,factory);let cases=0;
const [a0,b0]=await Promise.all([a.load(source,'instance-a'),b.load(source,'instance-b')]);assert.equal(a0.status,'Ready');assert.notEqual(a0.snapshot.state.stateId,b0.snapshot.state.stateId);++cases;
function request(result,id){const s=result.snapshot.state;return {id,sessionId:s.sessionId,definitionId:s.definitionId,expectedStateId:s.stateId,epoch:s.epoch,revision:s.revision,segments:[{driverTurns:{numerator:'13',denominator:'200'},independentPorts:{[result.capabilities.planetPort]:{numerator:'0',denominator:'1'}},observations:{},events:[]}]};}
const input=request(a0,'advance-owned');const action=a.advance(input);input.segments[0].driverTurns.numerator='99';const advanced=await action.result;assert.equal(advanced.status,'Accepted');assert.equal(advanced.snapshot.state.frame.driverTurns.numerator,'13');assert.ok(Object.isFrozen(advanced.snapshot.state.frame));++cases;
assert.equal((await action.cancel()).status,'TooLateCommitted');assert.equal((await b.snapshot()).snapshot.state.stateId,b0.snapshot.state.stateId);++cases;
const before=(await a.snapshot()).snapshot.state.stateId;let pending;
pending=a.advance(request(advanced,'cancel-at-prepare'),{onPrepared:()=>pending.cancel()});const cancellation=await pending.result;
assert.ok(['CancelledBeforeCommit','Accepted'].includes(cancellation.status));const readback=await a.snapshot();
if(cancellation.status==='CancelledBeforeCommit')assert.equal(readback.snapshot.state.stateId,before);else assert.equal((await pending.cancel()).status,'TooLateCommitted');++cases;
const oldLoads=await Promise.allSettled([a.load(source,'load-a'),a.load(source,'load-b'),a.load(source,'load-a-again')]);assert.equal(oldLoads[2].status,'fulfilled');assert.equal(oldLoads[0].status,'rejected');assert.equal(oldLoads[1].status,'rejected');assert.equal((await a.snapshot()).snapshot.state.sessionId,'load-a-again');++cases;
const checkpoint=await a.checkpoint();const bRestored=await b.restore(checkpoint.checkpointUtf8);assert.equal(bRestored.snapshot.state.sessionId,'load-a-again');++cases;
assert.equal((await b.sealNetwork()).status,'NetworkSealed');assert.equal((await b.advance(request(bRestored,'after-seal')).result).status,'Accepted');++cases;
const delayed=a.load(source,'dispose-load');a.dispose();await assert.rejects(delayed,/Disposed/);await assert.rejects(a.snapshot(),/Disposed/);assert.equal((await b.snapshot()).snapshot.state.revision,'1');++cases;
// A runtime is reusable only after a fresh host is explicitly created; termination is not rollback.
b.dispose();const c=new MechanicalRuntimeClient(workerUrl,factory);const afterCrash=await c.restore(checkpoint.checkpointUtf8);assert.equal(afterCrash.snapshot.state.revision,'0');const final=await c.advance(request(afterCrash,'restart-new')).result;assert.equal(final.status,'Accepted');++cases;
const faultedWorker=workers.at(-1), fault=new Promise(resolve=>faultedWorker.once('error',resolve));
faultedWorker.postMessage({acceptanceFault:true});await fault;await assert.rejects(c.snapshot(),/DisposedOrNotLoaded/);c.dispose();
const d=new MechanicalRuntimeClient(workerUrl,factory);const recovered=await d.restore(checkpoint.checkpointUtf8);assert.equal(recovered.snapshot.state.stateId,afterCrash.snapshot.state.stateId);assert.equal((await d.advance(request(recovered,'after-real-worker-error')).result).status,'Accepted');d.dispose();++cases;
const report={execution:'real .NET WASM in Node Worker threads; not a browser UI substitute',cases,workerInstances:workers.length,cancellationRaceOutcome:cancellation.status,lateCancellation:'TooLateCommitted',loadRace:'latest generation only',independentInstance:'PASS',ownedInputsAndFrozenSnapshots:'PASS',sealedNetwork:'PASS',disposeAndFreshRestore:'PASS',uncaughtWorkerErrorAndRestore:'PASS'};
await writeFile(outputArg,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify(report,null,2));
