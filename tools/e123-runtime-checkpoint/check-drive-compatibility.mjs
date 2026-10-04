// Old reader is a separately built public04 binary; no source rewriting or expected regeneration.
import {readFile,writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
const [oldCli,newCli,oldSourcePath,newSourcePath,output]=process.argv.slice(2);
function host(cli){
  const p=spawn('dotnet',[cli],{stdio:['pipe','pipe','inherit'],windowsHide:true}),queue=[];
  createInterface({input:p.stdout}).on('line',line=>queue.shift()?.resolve(JSON.parse(line)));
  p.on('exit',code=>queue.splice(0).forEach(q=>q.reject(Error('native exit '+code))));
  return {send:c=>new Promise((resolve,reject)=>{queue.push({resolve,reject});p.stdin.write(JSON.stringify(c)+'\n');}),close:()=>p.stdin.end()};
}
const old=host(oldCli),current=host(newCli);
try {
  const oldSource=await readFile(oldSourcePath,'utf8'),selected=await readFile(newSourcePath,'utf8');
  const a=await old.send({op:'load',sourceUtf8:oldSource,sessionId:'legacy-reader',initialPlanet:'3/10'});assert.equal(a.status,'Ready');
  const ck=await old.send({op:'checkpoint'});assert.equal(ck.status,'CheckpointCreated');
  const b=await current.send({op:'restore',checkpointUtf8:ck.checkpointUtf8});assert.equal(b.status,'Restored');
  assert.deepEqual(a.snapshot,b.snapshot);
  const roundtrip=await current.send({op:'checkpoint'});assert.equal(ck.checkpointUtf8,roundtrip.checkpointUtf8,'Old canonical bytes must remain identical on this host');
  const d=await current.send({op:'load',sourceUtf8:selected,sessionId:'new-reader',initialPlanet:'3/10'});assert.equal(d.status,'Ready');
  const next=await current.send({op:'checkpoint'});assert.equal(JSON.parse(next.checkpointUtf8).formatVersion,'3.0');
  const refusedSource=await old.send({op:'load',sourceUtf8:selected,sessionId:'not-admitted',initialPlanet:'3/10'});assert.notEqual(refusedSource.status,'Ready');
  const refusedCheckpoint=await old.send({op:'restore',checkpointUtf8:next.checkpointUtf8});assert.notEqual(refusedCheckpoint.status,'Restored');
  assert.equal((await old.send({op:'snapshot'})).snapshot.state.stateId,a.snapshot.state.stateId);
  const report={status:'PASS',oldCli,newCli,oldSourceId:a.snapshot.sourceArtifactId,selectedSourceId:d.snapshot.sourceArtifactId,
    oldCheckpointSha256:createHash('sha256').update(ck.checkpointUtf8).digest('hex'),oldBytesPreserved:true,oldReaderRefusal:{source:refusedSource.status,checkpoint:refusedCheckpoint.status},oldStatePreserved:true};
  await writeFile(output,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify(report));
}finally{old.close();current.close();}
