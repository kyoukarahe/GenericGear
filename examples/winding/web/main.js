import { readWindingReplay } from "../../../packages/replay/dist/winding.js";
import { readMechanicalModeReplay } from "../../../packages/replay/dist/mechanicalModes.js";
const $=id=>document.getElementById(id),ns="http://www.w3.org/2000/svg";let replay,index=0,timer=null;
const sha=async b=>Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256",b)),v=>v.toString(16).padStart(2,"0")).join("");
function element(name,attrs={}){const e=document.createElementNS(ns,name);for(const [k,v]of Object.entries(attrs))e.setAttribute(k,String(v));return e;}
function showError(e){stop();$("views").replaceChildren();$("status").textContent=String(e.message??e);$("status").className="failed";$("readback").textContent="새 성공 frame 없음";}
function stop(){if(timer!==null)clearInterval(timer);timer=null;}
function draw(frame){
  $("views").replaceChildren();if(frame.displayUnavailableReason){$("views").textContent="Display unavailable: "+frame.displayUnavailableReason;return;}
  const matrices=new Map(frame.matricesMm.map(n=>[n.id,n.matrix]));const layers=new Map();
  for(const n of replay.payload.scene){const m=matrices.get(n.id),z=m[14];if(!layers.has(z))layers.set(z,[]);layers.get(z).push({n,m});}
  for(const [z,entries]of [...layers].sort((a,b)=>a[0]-b[0])){
    const section=document.createElement("section");section.className="layer";const title=document.createElement("h2");title.textContent=`실제 pitch plane / z = ${z} mm`;section.append(title);
    const svg=element("svg",{role:"img","aria-label":`snapshot ${index}, z ${z} mm`}),container=element("g",{transform:"scale(1,-1)"});svg.append(container);let xmin=Infinity,xmax=-Infinity,ymin=Infinity,ymax=-Infinity;
    for(const {n,m}of entries){
      const group=element("g",{class:"mechanism",transform:`matrix(${m[0]},${m[1]},${m[4]},${m[5]},${m[12]},${m[13]})`,"data-node":n.id});const label=element("title");label.textContent=`${n.id} / owner ${n.owner}`;group.append(label);
      if(n.kind==="polyline")group.append(element("polyline",{points:n.pointsMm.map(p=>p.join(",")).join(" ")}));
      else{group.append(element("circle",{r:n.radiusMm,class:n.kind==="pin"?"pin":""}));if(n.kind==="pitch-circle")group.append(element("line",{x1:0,y1:0,x2:n.radiusMm,y2:0}));}
      container.append(group);const points=n.kind==="polyline"?n.pointsMm:[[-n.radiusMm,-n.radiusMm],[-n.radiusMm,n.radiusMm],[n.radiusMm,-n.radiusMm],[n.radiusMm,n.radiusMm]];
      for(const [x,y]of points){const wx=m[0]*x+m[4]*y+m[12],wy=m[1]*x+m[5]*y+m[13];xmin=Math.min(xmin,wx);xmax=Math.max(xmax,wx);ymin=Math.min(ymin,wy);ymax=Math.max(ymax,wy);}
    }
    const pad=Math.max(xmax-xmin,ymax-ymin)*.12+1;svg.setAttribute("viewBox",`${xmin-pad} ${-ymax-pad} ${xmax-xmin+2*pad} ${ymax-ymin+2*pad}`);section.append(svg);$("views").append(section);
  }
}
function select(i){
  index=i;const sample=replay.selectSample(index),frame=sample.frame??sample;$("sample").value=String(index);$("cursor").textContent=`${index} / ${replay.samples.length-1}`;$("status").className="";
  $("status").textContent=`기록된 snapshot ${index} · 모든 부품 같은 상태 · 보간 없음`;
  const f=v=>v.kind==="ExactRational"?`${v.exact.numerator}/${v.exact.denominator} turn [exact]`:`${v.estimate} turn [NumericResidualOnly; error bound=null]`;
  $("readback").textContent=`snapshot: ${frame.snapshotId}\nq: ${frame.driverTurns.numerator}/${frame.driverTurns.denominator} turn\n`+frame.coordinates.map(c=>`${c.shaftId}: ${f(c.value)}`).join("\n")+`\nmaterial pins: ${frame.winding.pins.length}; contacts: ${frame.winding.driverContact}/${frame.winding.outputContact}\npitch residual: ${frame.winding.pitchResidualMm} mm; total residual: ${frame.winding.totalLengthResidualMm} mm`;
  if(sample.frame)$("readback").textContent=`mode: ${sample.mode}; revision: ${sample.revision}; event cursor: ${sample.eventCursor}\nstate: ${sample.stateId}\nnext independent ports: ${sample.requiredInputPorts.join(", ")||"none (q only)"}\nH: ${f(sample.couplingOffset)}\nlock: ${sample.lockReference?f(sample.lockReference):"none"}\n`+$("readback").textContent;
  draw(frame);
}
async function load(bytes,restoreIndex=0){stop();replay=undefined;$("identity").textContent="";if(bytes.length>4194304)throw Error("4 MiB limit");const format=JSON.parse(typeof bytes==="string"?bytes:new TextDecoder().decode(bytes)).format;const next=format==="gear-invest.mechanical-mode-replay"?readMechanicalModeReplay(bytes):readWindingReplay(bytes);const integrity=await next.verifyIntegrity(sha);replay=next;$("sample").max=String(replay.samples.length-1);$("attempt").replaceChildren();
  replay.payload.results.attempts.forEach((a,i)=>{const o=document.createElement("option");o.value=String(i);o.textContent=`요청 ${i}: ${a.status}`;$("attempt").append(o);});
  $("identity").textContent=`source: ${replay.payload.sourceId??replay.payload.connectionId}\nreplay: ${replay.replayId}\nraw digest: ${integrity.rawDigest}\nbrowser mechanical validation: ${integrity.browserMechanicalValidation}\ncurrent source rebuild: ${integrity.currentSourceRebuild}`;select(restoreIndex);}
$("sample").oninput=()=>{stop();try{select(Number($("sample").value));}catch(e){showError(e);}};
$("previous").onclick=()=>{stop();if(replay)select(Math.max(0,index-1));};$("next").onclick=()=>{stop();if(replay)select(Math.min(replay.samples.length-1,index+1));};
$("play").onclick=()=>{stop();if(replay)timer=setInterval(()=>{if(index>=replay.samples.length-1)stop();else select(index+1);},180);};$("stop").onclick=stop;
$("show-attempt").onclick=()=>{stop();if(!replay)return;const a=replay.selectAttempt(Number($("attempt").value));if(a.status==="Accepted"){const frame=a.state??a.frames.at(-1);if(frame)select(replay.samples.indexOf(frame));return;}
  $("views").replaceChildren();$("status").className="failed";$("status").textContent=`${a.status} · applied=${a.appliedSegments} · 새 frame 없음`;
  $("readback").textContent=`lastValidSnapshot (요청 결과와 별개): ${a.lastValidStateId??a.lastValidSnapshotId}\n미적용 요청: ${JSON.stringify(a.remainder??{remainingSegments:a.remainingSegments,replayedStateId:a.replayedStateId},null,2)}\nslider에서 기록된 성공 상태를 다시 선택할 수 있습니다.`;};
$("save").onclick=()=>{if(!replay)return;localStorage.setItem("genericgear-winding-session-v1",JSON.stringify({replay:new TextDecoder().decode(replay.originalBytes()),index}));$("status").textContent=`재생 위치 ${index} 저장 완료 · 새 페이지에서 동일 replay 복원 가능`;};
$("download").onclick=()=>{if(!replay)return;const url=URL.createObjectURL(new Blob([replay.originalBytes()],{type:"application/json"}));const a=document.createElement("a");a.href=url;a.download="mechanism.winding-replay.json";a.click();setTimeout(()=>URL.revokeObjectURL(url),5000);};
$("file").onchange=async()=>{try{const file=$("file").files[0];if(file){if(file.size>4194304)throw Error("4 MiB limit");await load(new Uint8Array(await file.arrayBuffer()));}}catch(e){showError(e);}};
try{const params=new URLSearchParams(location.search);if(params.has("restore")){const saved=JSON.parse(localStorage.getItem("genericgear-winding-session-v1")??"null");if(!saved)throw Error("저장된 세션 없음");await load(saved.replay,saved.index);}else{const path=params.get("replay")??"../../../artifacts/e123-implementation/example/replay.json";const response=await fetch(path);if(!response.ok)throw Error("C# 예제를 먼저 produce하거나 내 replay를 선택하세요.");await load(new Uint8Array(await response.arrayBuffer()));}}catch(e){showError(e);}
