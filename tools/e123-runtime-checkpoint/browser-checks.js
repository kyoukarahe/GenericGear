import { MechanicalRuntimeClient } from '../runtime-client.js';
import { readCheckpoint, publishCheckpoint } from '../checkpoint-store.js';
const $=id=>document.getElementById(id);
function check(value,message){if(!value)throw new Error(message);}
function frac(n,d=1n){n=BigInt(n);d=BigInt(d);let a=n<0n?-n:n,b=d;while(b)[a,b]=[b,a%b];return {numerator:String(n/a),denominator:String(d/a)};}
const client=new MechanicalRuntimeClient();
$('run').onclick=async()=>{
  $('run').disabled=true;const times=[],saved=[];let maximum=0;
  try{
    const init=performance.now();let r=await client.load(await(await fetch('../example/source.json')).text(),'browser-long-'+crypto.randomUUID(),'3/10');const initializeMs=performance.now()-init;
    check(r.status==='Ready','load');const sourceId=r.snapshot.sourceArtifactId;const sealed=await client.sealNetwork();check(sealed.status==='NetworkSealed','network');globalThis.fetch=()=>Promise.reject(new Error('AcceptanceNetworkSealed'));
    let previous=null;const slot='acceptance-'+crypto.randomUUID();let latest;
    for(let i=0;i<4352;i++){
      const s=r.snapshot.state,id='r-'+i,ports=Object.fromEntries(s.requiredInputPorts.map(p=>[p,p===r.capabilities.sunPort?frac(2n,25n):frac(3n,10n)]));
      latest={id,sessionId:s.sessionId,definitionId:s.definitionId,expectedStateId:s.stateId,epoch:s.epoch,revision:s.revision,segments:[{driverTurns:frac(400000n+BigInt(i),10000000n),independentPorts:ports,observations:{},events:[{id:id+'/e0',sequence:String(BigInt(s.eventCursor)+1n),ordinal:'0',kind:i%2===0?'Release':'Capture'}]}]};
      const start=performance.now();r=await client.advance(latest).result;times.push(performance.now()-start);check(r.status==='Accepted','advance '+i+': '+r.status);maximum=Math.max(maximum,r.snapshot.state.witnesses.length);
      if(i%256===255){
        const t=performance.now(),checkpoint=await client.checkpoint();check(checkpoint.status==='CheckpointCreated','checkpoint');
        const stored=await publishCheckpoint(slot,previous,checkpoint);previous=stored.artifactId;
        // Worker remains loaded/network-sealed. Restore through its shared C# boundary is separately covered
        // by new-page ordinary UI; this long run verifies bounded persistence without repeated downloads.
        const read=await readCheckpoint(slot);check(read.artifactId===checkpoint.artifactId,'stored readback');
        saved.push({revision:r.snapshot.state.revision,epoch:r.snapshot.state.epoch,cursor:r.snapshot.state.eventCursor,live:r.snapshot.state.witnesses.length,bytes:new TextEncoder().encode(read.checkpointUtf8).length,createPublishReadMs:performance.now()-t});
        $('status').textContent=`${i+1}/4352 accepted · live ${maximum} · saved ${saved.length}`;
        if(i===4351)$('checkpoint').value=read.checkpointUtf8;
      }
    }
    const duplicate=await client.advance(latest).result;check(duplicate.status==='AlreadyApplied','retry');
    const checkpoint=await client.checkpoint();let conflict=false;try{await publishCheckpoint(slot,null,checkpoint);}catch(e){conflict=String(e).includes('CheckpointWriteConflict');}check(conflict,'CAS conflict');check((await readCheckpoint(slot)).artifactId===previous,'preserve last complete');
    // Abort an actual IDB publication transaction after put: readers must retain the previous checkpoint.
    const db=await new Promise((resolve,reject)=>{const o=indexedDB.open('gearinvest-runtime-checkpoints-v1',1);o.onsuccess=()=>resolve(o.result);o.onerror=()=>reject(o.error);});
    await new Promise(resolve=>{const tx=db.transaction('checkpoints','readwrite');tx.objectStore('checkpoints').put({artifactId:'partial',checkpointUtf8:'{'},slot);tx.onabort=resolve;tx.abort();});db.close();check((await readCheckpoint(slot)).artifactId===previous,'aborted write');
    const sorted=times.toSorted((a,b)=>a-b);
    const report={status:'PASS',environment:navigator.userAgent,execution:'actual browser dedicated Worker / shared C# WASM',sourceId,lifetimeRequests:4352,checkpoints:saved,maximumLiveProvenance:maximum,retainedLedger:r.snapshot.state.ledger.length,finalStateId:r.snapshot.state.stateId,initializeMs,roundTripP50Ms:sorted[Math.floor(sorted.length*.5)],roundTripP95Ms:sorted[Math.floor(sorted.length*.95)],numericQuality:'NumericResidualOnly; error bound null',network:'Worker fetch sealed after initialization; page fetch sealed',retry:'AlreadyApplied',CAS:'PASS',abortedIndexedDbWrite:'PASS',browserHeap:performance.memory?{used:performance.memory.usedJSHeapSize,total:performance.memory.totalJSHeapSize,scope:'nonstandard approximate page isolate; not Worker/WASM heap'}:null,resources:sealed.resources,limitations:['new-page restore is verified in ordinary UI separately','physical Android/iOS not tested','independent active provenance >64 refuses; not unlimited arbitrary lock history']};
    $('report').textContent=JSON.stringify(report,null,2);$('status').textContent='PASS · 4352 새 요청 · 17 체크포인트 · retry/CAS/중단 저장 검증';
    $('reopen').href='../example/?restore=1&slot='+encodeURIComponent(slot);$('reopen').hidden=false;
  }catch(e){$('status').textContent='FAIL: '+e; $('report').textContent=String(e.stack??e);}
};
addEventListener('pagehide',()=>client.dispose());
