import {DifferentialSession} from "./session.js";
import {assetLocal,multiply,identity,translation,rendererParent} from "../../carrier/web/asset-transform.js";
const $=id=>document.getElementById(id),ns="http://www.w3.org/2000/svg",storageKey="gearinvest-differential-e2b-view-v1";
const digest=async bytes=>[...new Uint8Array(await crypto.subtle.digest("SHA-256",bytes))].map(x=>x.toString(16).padStart(2,"0")).join("");
const session=new DifferentialSession(digest);let requestGeneration=0,playing=false,sample="standalone",marker=null,disposed=false;
const text=f=>f.denominator==="1"?f.numerator:`${f.numerator}/${f.denominator}`;
function fraction(value){if(typeof value!=="string"||value.length>258||! /^-?\d+(\/\d+)?$/.test(value.trim()))throw Error("정수 또는 분수를 입력하세요.");let [n,d="1"]=value.trim().split("/");let a=BigInt(n),b=BigInt(d);if(b<=0n)throw Error("분모는 양수여야 합니다.");let x=a<0n?-a:a,y=b;while(y)[x,y]=[y,x%y];return {numerator:String(a/x),denominator:String(b/x)};}
function stop(){playing=false;requestGeneration++;$("play").textContent="이 입력만 재생";}
function clearPose(){ $("renderer-parent").replaceChildren();$("readback").textContent="현재 pose 없음";}
function fail(error){stop();session.clearEvaluation();clearPose();$("status").textContent="REFUSED · "+error.message;$("status").style.color="#ffb4a4";}
function svg(tag,attrs,parent){const node=document.createElementNS(ns,tag);for(const [key,value]of Object.entries(attrs))node.setAttribute(key,String(value));parent.append(node);return node;}
function matrix2(m){return `matrix(${m[0]} ${m[1]} ${m[4]} ${m[5]} ${m[12]} ${m[13]})`;}
function render(e){
  clearPose();$("readback").textContent=Object.entries(e.coordinates).map(([id,v])=>`${id} = ${text(v)} turn`).join("\n")+`\nplanet body/common = ${text(e.planetCommon)}\nplanet/carrier-relative = ${text(e.planetRelative)}`;
  if(e.display.status!=="displayApproximation"){$("status").textContent="Exact 값 유효 / display unavailable · "+e.display.reason;return;}
  const parent=$("parent").checked?rendererParent:identity,root=$("renderer-parent");root.setAttribute("transform",matrix2(parent));
  const compiled=session.definition.payload.compiled,matrices=new Map(e.display.nodes.map(n=>[n.id,n.matrixMm]));
  const carrier=matrices.get("carrier-frame"),planet=matrices.get("planet-shaft");
  const lineWorld=[...identity];lineWorld[12]=carrier[12];lineWorld[13]=carrier[13];
  const local=assetLocal(lineWorld,parent),arm=svg("g",{transform:matrix2(local)},root);svg("line",{x1:0,y1:0,x2:planet[12]-carrier[12],y2:planet[13]-carrier[13],stroke:"#e5eded","stroke-width":3},arm);
  for(const node of compiled.poseNodes.filter(n=>n.bodyId!==null&&n.pitchRadiusMm!==null)){
    const m=assetLocal(matrices.get(node.id),parent),g=svg("g",{transform:matrix2(m)},root),radius=Number(node.pitchRadiusMm.numerator)/Number(node.pitchRadiusMm.denominator);
    const color=node.shaftId===compiled.sunShaftId?"#68d4bc":node.shaftId===compiled.planetShaftId?"#f4a65e":"#8096a0";
    svg("circle",{r:radius,fill:color+"12",stroke:color,"stroke-width":2},g);svg("line",{x1:0,y1:0,x2:radius,y2:0,stroke:color,"stroke-width":3},g);svg("circle",{r:1.8,fill:color},g);
  }
  const selected=compiled.poseNodes.find(n=>n.bodyId===$("body").value);
  if(marker&&selected){const correction=$("pivot").checked?translation(-marker.pivotMm[0],-marker.pivotMm[1],-marker.pivotMm[2]):identity;
    const localAsset=assetLocal(matrices.get(selected.id),parent,correction);const g=svg("g",{transform:matrix2(localAsset)},root);svg("polyline",{points:marker.verticesMm.map(p=>p.slice(0,2).join(",")).join(" "),fill:"#e5f190",stroke:"#f6ffb5","stroke-width":1.5},g);
    const world=multiply(parent,localAsset);$("readback").textContent+=`\nasset owner = ${selected.bodyId}\nasset origin mm = ${world[12].toFixed(4)}, ${world[13].toFixed(4)}, ${world[14].toFixed(4)}`;
  }
  $("status").style.color="#9be1b8";$("status").textContent=`Digest PASS · ${compiled.poseNodes.filter(n=>n.bodyId!==null).length} bodies · exact vector applied`;
}
function snapshot(){return Object.fromEntries(session.definition.payload.compiled.inputPortIds.map((id,index)=>[id,fraction($("input-"+index).value)]));}
function apply(){stop();try{render(session.apply(snapshot()));}catch(e){fail(e);}}
function controls(){const c=session.definition.payload.compiled;$("inputs").replaceChildren();$("play-axis").replaceChildren();$("body").replaceChildren();$("play").disabled=c.inputPortIds.length===0;
  c.inputPortIds.forEach((id,index)=>{const label=document.createElement("label");label.textContent=id;const input=document.createElement("input");input.id="input-"+index;input.name=id;input.value=index===0?"1/4":"1/10";input.autocomplete="off";label.append(input);$("inputs").append(label);$("play-axis").add(new Option(id,id));});
  c.poseNodes.filter(n=>n.bodyId!==null).forEach(n=>$("body").add(new Option(n.bodyId,n.bodyId)));$("body").value=c.poseNodes.find(n=>n.id==="planet-body").bodyId;
  $("identity").textContent=`basis [${c.inputPortIds.join(", ")}] · request ${c.requestId} · source ${session.definition.payload.artifactId}`;
}
async function load(getBytes,observation=false){stop();clearPose();$("identity").textContent="New source pending · no current pose";$("status").textContent="불러오기 / digest 검사…";
  try{if(!await session.load(getBytes,observation)||disposed)return;
    if(observation){$("inputs").replaceChildren();$("play-axis").replaceChildren();$("body").replaceChildren();const o=session.current.value,c=o.compiled;
      $("play").disabled=true;$("identity").textContent=`stored observation only · ${o.requestId}`;$("readback").textContent=`${c.status}\nrank = ${c.rank}\n`+c.coordinates.map(x=>`${x.shaftId}: ${x.isKnown?"known = "+text(x.law.b):"known part "+text(x.law.b)+" + free terms "+JSON.stringify(x.freeTerms)}`).join("\n")+c.diagnostics.map(x=>"\n"+x.code+" "+x.related.join(", ")).join("");
      $("status").textContent="저장된 C# 관측만 표시 · 실행 가능한 pose 없음";return;}
    controls();apply();
  }catch(e){fail(e);}}
async function responseBytes(url){const r=await fetch(url);if(!r.ok)throw Error(`HTTP ${r.status}`);return new Uint8Array(await r.arrayBuffer());}
document.querySelectorAll("[data-sample]").forEach(button=>button.addEventListener("click",()=>{sample=button.dataset.sample;load(()=>responseBytes(`samples/${sample}/mechanism.differential-replay.json`));}));
$("apply").addEventListener("submit",e=>{e.preventDefault();apply();});$("stop").onclick=stop;
$("play").onclick=()=>{stop();try{const base=snapshot(),axis=$("play-axis").value,epoch=session.epoch,request=requestGeneration;session.apply(base);playing=true;$("play").textContent="재생 중";let frame=0;
  const tick=()=>{if(!playing||disposed||epoch!==session.epoch||request!==requestGeneration)return;try{frame++;const v=base[axis],n=BigInt(v.numerator)*120n+BigInt(frame)*BigInt(v.denominator),d=BigInt(v.denominator)*120n;const values={...base,[axis]:fraction(`${n}/${d}`)};render(session.apply(values));const index=session.definition.payload.compiled.inputPortIds.indexOf(axis);$("input-"+index).value=text(values[axis]);requestAnimationFrame(tick);}catch(e){fail(e);}};requestAnimationFrame(tick);
  }catch(e){fail(e);}};
for(const id of ["parent","pivot","body"])$(id).addEventListener("change",()=>{if(session.current?.kind==="evaluation")render(session.current.value);});
for(const id of ["partial","inconsistent"])$(id).onclick=()=>load(()=>responseBytes(`samples/standalone/${id}.differential-analysis.json`),true);
$("display-unavailable").onclick=()=>load(()=>responseBytes("samples/display-unavailable/mechanism.differential-replay.json"));
$("file").onchange=()=>{const file=$("file").files[0];if(file)load(async()=>{if(file.size>8388608)throw Error("Document bound exceeded.");return new Uint8Array(await file.arrayBuffer());},file.name.endsWith("-analysis.json"));};
$("load-json").onclick=()=>load(async()=>$("json").value);
$("save").onclick=()=>{stop();try{if(session.current?.kind!=="evaluation")throw Error("먼저 유효한 입력을 적용하세요.");localStorage.setItem(storageKey,JSON.stringify({format:"differential-example-view",version:1,replay:new TextDecoder().decode(session.definition.originalBytes()),input:session.current.value.input,parent:$("parent").checked,pivot:$("pivot").checked,body:$("body").value}));$("status").textContent="세션 저장됨 · 새 페이지에서 원본 검증 후 재생";}catch(e){fail(e);}};
$("export").onclick=()=>{try{if(!session.definition)throw Error("원본 replay 없음");const url=URL.createObjectURL(new Blob([session.definition.originalBytes()],{type:"application/json"})),a=document.createElement("a");a.href=url;a.download="mechanism.differential-replay.json";a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}catch(e){fail(e);}};
window.addEventListener("pagehide",()=>{disposed=true;stop();session.dispose();});
try{const response=await fetch("marker.json");if(!response.ok)throw Error("External marker load failed.");marker=await response.json();
  if(new URLSearchParams(location.search).has("restore")){const saved=JSON.parse(localStorage.getItem(storageKey)||"null");if(!saved||saved.format!=="differential-example-view"||saved.version!==1)throw Error("저장 세션 없음");const pending=load(async()=>saved.replay),ticket=session.epoch;await pending;
    if(ticket===session.epoch&&!disposed){if(!session.definition)throw Error("저장 원본 검증 실패");session.definition.payload.compiled.inputPortIds.forEach((id,index)=>$("input-"+index).value=text(saved.input[id]));$("parent").checked=saved.parent===true;$("pivot").checked=saved.pivot===true;$("body").value=saved.body;render(session.apply(saved.input));}}
  else await load(()=>responseBytes("samples/standalone/mechanism.differential-replay.json"));
}catch(e){fail(e);}
