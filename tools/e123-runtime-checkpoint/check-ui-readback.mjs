// Replay requests read from the real ordinary UI into a NEW native process, starting at its saved checkpoint.
import { readFile, writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
const [cli,checkpointPath,logPath,output]=process.argv.slice(2);
const checkpointUtf8=await readFile(checkpointPath,'utf8'), log=JSON.parse(await readFile(logPath,'utf8'));
const p=spawn('dotnet',[cli],{stdio:['pipe','pipe','inherit'],windowsHide:true}), queue=[];
createInterface({input:p.stdout,crlfDelay:Infinity}).on('line',line=>queue.shift().resolve(JSON.parse(line)));
p.on('exit',code=>{for(const q of queue.splice(0))q.reject(new Error('Process exited '+code));});
const send=message=>new Promise((resolve,reject)=>{queue.push({resolve,reject});p.stdin.write(JSON.stringify(message)+'\n');});
const initial=await send({op:'restore',checkpointUtf8});assert.equal(initial.status,'Restored');let last=initial.snapshot.state;
const results=[];
for(const item of log.filter(i=>i.kind==='new-input')) {
  const prepared=await send({op:'prepare',request:item.request});
  if(item.status==='Accepted') {
    assert.equal(prepared.status,'Prepared');const actual=await send({op:'commit',token:prepared.token});assert.equal(actual.status,'Accepted');assert.equal(actual.snapshot.state.stateId,item.stateId);last=actual.snapshot.state;
  } else { assert.equal(prepared.status,item.status);assert.equal((await send({op:'snapshot'})).snapshot.state.stateId,last.stateId); }
  results.push({requestId:item.request.id,status:item.status,stateId:last.stateId});
}
p.stdin.end();const report={status:'PASS',surface:'actual browser UI request log replayed through fresh native SDK process',checkpointSha256:createHash('sha256').update(checkpointUtf8).digest('hex'),initialStateId:initial.snapshot.state.stateId,results,finalStateId:last.stateId,coordinates:last.frame.coordinates};
await writeFile(output,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify({status:report.status,requests:results.length,finalStateId:last.stateId}));
