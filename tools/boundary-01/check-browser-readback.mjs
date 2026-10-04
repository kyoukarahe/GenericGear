// Replay actual ordinary-UI requests; restore the actual downloaded checkpoint in a new process.
import fs from 'node:fs';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
const [cli, sourcePath, uiPath, checkpointPath, output] = process.argv.slice(2);
const sourceUtf8 = fs.readFileSync(sourcePath, 'utf8'), ui = JSON.parse(fs.readFileSync(uiPath, 'utf8'));
const checkpointUtf8 = fs.readFileSync(checkpointPath, 'utf8');
const hash = b => createHash('sha256').update(b).digest('hex');
const payload = JSON.parse(Buffer.from(JSON.parse(checkpointUtf8).payloadUtf8, 'base64').toString('utf8'));
function managed() {
  const child = spawn('dotnet', [cli], { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true }), queue = [];
  createInterface({ input: child.stdout, crlfDelay: Infinity }).on('line', line => { const item = queue.shift(); try { item.resolve(JSON.parse(line)); } catch(e) { item.reject(e); } });
  child.on('exit', code => { for (const q of queue.splice(0)) q.reject(new Error('Exited ' + code)); });
  return { pid: child.pid, send: command => new Promise((resolve,reject) => { queue.push({resolve,reject}); child.stdin.write(JSON.stringify(command) + '\n'); }), close: () => child.stdin.end() };
}
const original = ui.beforeRestoreLog.filter(x => x.kind === 'new-input'), continued = ui.restoredLog.filter(x => x.kind === 'new-input');
assert.equal(original.length, 4); assert.equal(continued.length, 1);
assert.equal(ui.boundary.identity, ui.lowerRefusal.identity); assert.equal(ui.boundary.identity, ui.upperRefusal.identity); assert.equal(ui.boundary.identity, ui.restored.identity);
assert.equal(ui.lowerRefusal.status, 'WindingBoundary'); assert.equal(ui.upperRefusal.status, 'WindingBoundary');
let native = managed(), current, maxNumericDifference = 0;
const processIds = [native.pid], results = [];
function same(a,b,key='') {
  if(typeof a==='number' && typeof b==='number') { const delta=Math.abs(a-b); maxNumericDifference=Math.max(maxNumericDifference,delta); assert(delta <= (key.includes('matrix') ? 1e-8 : 1e-10),key); return; }
  if(a!==null && b!==null && typeof a==='object' && typeof b==='object') { assert.deepEqual(Object.keys(a),Object.keys(b),key); for(const k of Object.keys(a)) same(a[k],b[k],key+'/'+k); return; }
  assert.equal(a,b,key);
}
async function replay(item) {
  const before = (await native.send({op:'snapshot'})).snapshot;
  let response = await native.send({op:'prepare',request:item.request});
  if (item.status === 'Accepted') { assert.equal(response.status,'Prepared'); response=await native.send({op:'commit',token:response.token}); assert.equal(response.snapshot.state.stateId,item.stateId); }
  assert.equal(response.status,item.status); const readback=await native.send({op:'snapshot'});
  if(item.status!=='Accepted') assert.deepEqual(readback.snapshot,before);
  current=readback; results.push({requestId:item.request.id,status:response.status,stateId:current.snapshot.state.stateId,revision:current.snapshot.state.revision,driverTurns:current.snapshot.state.frame.driverTurns});
}
try {
  current=await native.send({op:'load',sourceUtf8,sessionId:original[0].request.sessionId,initialPlanet:'3/10'}); assert.equal(current.status,'Ready');
  for(const item of original) await replay(item);
  assert.equal(current.snapshot.state.stateId,payload.state.stateId);
  same(current.snapshot.state,payload.state);
  native.close(); native=managed(); processIds.push(native.pid);
  current=await native.send({op:'restore',checkpointUtf8}); assert.equal(current.status,'Restored');
  same(current.snapshot.state,payload.state);
  for(const item of continued) await replay(item);
  const boot = ui.beforeRestoreLog.find(x=>x.kind==='boot');
  const resources = ui.beforeRestoreLog.find(x=>x.kind==='network-sealed').resources.filter(x=>/GearInvest/.test(x.name));
  const report={status:'PASS',surface:'real Chrome UI source requests plus downloaded boundary checkpoint, replayed/restored by two native processes',userAgent:boot.userAgent,
    sourceSha256:hash(sourceUtf8),checkpointPath,checkpointSha256:hash(checkpointUtf8),checkpointBytes:Buffer.byteLength(checkpointUtf8),boundaryStateId:payload.state.stateId,
    processIds,maxNumericDifference,resources,results,finalStateId:current.snapshot.state.stateId,coordinates:current.snapshot.state.frame.coordinates,mode:current.snapshot.state.mode,
    materialIdsPreserved:true,method:'full checkpoint state comparison (1e-10 numeric, 1e-8 matrix), unchanged full snapshot on rejected requests, exact IDs and fractions'};
  fs.writeFileSync(output,JSON.stringify(report,null,2)+'\n',{flag:'wx'});
  console.log(JSON.stringify({status:report.status,requests:results.length,processIds,maxNumericDifference,boundaryStateId:report.boundaryStateId,finalStateId:report.finalStateId}));
} finally {native.close();}
