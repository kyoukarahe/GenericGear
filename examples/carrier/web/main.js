import {readCarrierReplay,CARRIER_LIMITS} from "../../../packages/replay/dist/carrier.js";
import {identity,assetLocal,translation,rendererParent} from "./asset-transform.js";
const $=id=>document.getElementById(id),ns="http://www.w3.org/2000/svg";
let replay=null,instance=null,current=null,loadGeneration=0,playGeneration=0,playing=false,playStart=0,base={numerator:"0",denominator:"1"};
const fractionText=f=>f.denominator==="1"?f.numerator:`${f.numerator}/${f.denominator}`;
function fraction(text){if(text.length>257||!/^(-?(?:0|[1-9][0-9]*))(?:\/([1-9][0-9]*))?$/.test(text))throw Error("정수 또는 정확한 분수를 입력하세요.");let[n,d="1"]=text.split("/");let a=BigInt(n),b=BigInt(d),x=a<0n?-a:a,y=b;while(y)[x,y]=[y,x%y];return{numerator:String(a/x),denominator:String(b/x)};}
const sha=async bytes=>[...new Uint8Array(await crypto.subtle.digest("SHA-256",bytes))].map(b=>b.toString(16).padStart(2,"0")).join("");
function status(message){$("status").textContent=message;}
function element(tag,attrs={}){const e=document.createElementNS(ns,tag);for(const[k,v]of Object.entries(attrs))e.setAttribute(k,String(v));return e;}
function transform(m){return `matrix(${m[0]} ${m[1]} ${m[4]} ${m[5]} ${m[12]} ${m[13]})`;}
function draw(frame){
  const compiled=replay.payload.compiled,matrices=new Map(frame.display.nodes.map(n=>[n.id,n.matrixMm]));
  const parent=$("parent").checked?rendererParent:identity;const group=$("renderer-parent");group.replaceChildren();group.setAttribute("transform",transform(parent));
  const p=frame.shafts.find(s=>s.id===compiled.planetShaftId),selected=compiled.poseNodes.find(n=>n.bodyId===$("body").value),m=selected?matrices.get(selected.id):null;
  $("state").textContent=`입력: ${fractionText(frame.input)}\ncarrier (공통): ${fractionText(frame.carrierCommon)}\nplanet world: ${fractionText(p.world)}\nplanet relative: ${fractionText(p.carrierRelative)}\nport: ${fractionText(frame.portReadout)}\nbody count: ${compiled.poseNodes.filter(n=>n.bodyId!==null).length}\n선택: ${$("body").value}\nworld center: ${m?m.slice(12,15).map(x=>x.toFixed(5)).join(", "):"—"}\nworld ray: ${m?m.slice(0,3).map(x=>x.toFixed(5)).join(", "):"—"}`;
  if(frame.display.status!=="displayApproximation"){status(frame.display.reason);return;}
  for(const node of compiled.poseNodes.filter(n=>n.bodyId!==null)){
    const body=matrices.get(node.id),g=element("g",{transform:transform(assetLocal(body,parent)),class:`part ${$("body").value===node.bodyId?"selected":""}`,"data-body-id":node.bodyId});
    g.addEventListener("click",()=>{$("body").value=node.bodyId;draw(current);});
    if(node.id==="carrier-body"){
      const distance=Number(compiled.poseNodes.find(n=>n.id==="planet-shaft").frame.origin[0].numerator)/Number(compiled.poseNodes.find(n=>n.id==="planet-shaft").frame.origin[0].denominator);
      g.append(element("line",{x1:0,y1:0,x2:distance,y2:0,stroke:"#e9eff8","stroke-width":2.5}));
    }else{
      const r=Number(node.pitchRadiusMm.numerator)/Number(node.pitchRadiusMm.denominator),color=node.id==="sun-body"?"#55d5c4":node.id==="planet-body"?"#ffc16c":"#7c91a8";
      g.append(element("circle",{r,fill:color,"fill-opacity":.09,stroke:color,"stroke-width":.8}),element("circle",{r:2,fill:color}));
      for(let j=0;j<Number(node.teeth);j++){const a=j/Number(node.teeth)*2*Math.PI;g.append(element("line",{x1:(r-1.1)*Math.cos(a),y1:(r-1.1)*Math.sin(a),x2:r*Math.cos(a),y2:r*Math.sin(a),stroke:color,"stroke-width":.35}));}
      const pivot=$("pivot").checked?7:0;
      // A separately authored pointer asset has its origin shifted +7 mm. Its correction is -7 mm.
      const marker=element("g",{transform:transform(translation(-pivot,0)),"data-asset-pivot":pivot});
      marker.append(element("path",{d:`M ${pivot} 0 L ${pivot+r*.75} 0 M ${pivot+r*.58} -1.5 L ${pivot+r*.75} 0 L ${pivot+r*.58} 1.5`,stroke:color,"stroke-width":1,fill:"none"}));g.append(marker);
    }
    group.append(g);
  }
}
function seek(input){const next=instance.evaluate(input);current=next;base=input;draw(next);status(next.display.status==="displayApproximation"?"새 exact input 평가 완료 · displayApproximation":`exact 상태 유지 · 표시 불가: ${next.display.reason}`);}
function stop(){playing=false;playGeneration++;$("play").disabled=false;$("turns").value=fractionText(base);}
async function load(getText){
  const generation=++loadGeneration;stop();let candidate=null;
  try{
    const text=await getText();if(generation!==loadGeneration)return;
    candidate=readCarrierReplay(text);await candidate.verifyIntegrity(sha);if(generation!==loadGeneration){candidate.dispose();return;}
    const nextInstance=candidate.createInstance("viewer"),next=nextInstance.evaluate({numerator:"0",denominator:"1"});
    if(next.display.status!=="displayApproximation")throw Error(next.display.reason);
    replay?.dispose();replay=candidate;candidate=null;instance=nextInstance;current=next;base=next.input;
    $("body").replaceChildren(...replay.payload.compiled.poseNodes.filter(n=>n.bodyId!==null).map(n=>{const o=document.createElement("option");o.value=n.bodyId;o.textContent=n.bodyId;return o;}));$("body").value=replay.payload.compiled.poseNodes.find(n=>n.id==="planet-body").bodyId;
    $("identity").textContent=`replay ${replay.replayId} | artifact ${replay.payload.artifactId}`;$("turns").value="0";draw(current);status("불러오기 완료 · digest PASS · 현재 기구 채택");
  }catch(e){candidate?.dispose();if(generation===loadGeneration)status(`거부 · 기존 기구 유지: ${e.message}`);}
}
const preset=name=>load(async()=>{const r=await fetch(`./samples/${name}/mechanism.carrier-replay.json`,{cache:"no-store"});if(!r.ok)throw Error(`HTTP ${r.status}`);return r.text();});
$("standalone").onclick=()=>preset("standalone");$("prefix").onclick=()=>preset("prefix");
$("seek").onsubmit=e=>{e.preventDefault();const requested=$("turns").value.trim();stop();try{seek(fraction(requested));$("turns").value=fractionText(base);}catch(error){status(`입력 거부 · 이전 상태 유지: ${error.message}`);}};
$("quarter").onclick=()=>{stop();seek(fraction("1/4"));$("turns").value="1/4";};$("stop").onclick=stop;
$("parent").onchange=$("pivot").onchange=$("body").onchange=()=>{if(current)draw(current);};
$("load-json").onclick=()=>load(async()=>$("json").value);
$("file").onchange=()=>load(async()=>{const f=$("file").files[0];if(!f||f.size>CARRIER_LIMITS.documentBytes)throw Error("파일 크기 제한");return f.text();});
$("play").onclick=()=>{if(!instance)return;playing=true;const generation=++playGeneration;$("play").disabled=true;playStart=performance.now();const start=base;
  function tick(now){if(!playing||generation!==playGeneration)return;try{const millis=BigInt(Math.floor(now-playStart)),n=BigInt(start.numerator)*8000n+millis*BigInt(start.denominator),d=BigInt(start.denominator)*8000n;seek(fraction(`${n}/${d}`));requestAnimationFrame(tick);}catch(e){stop();status(`재생 정지: ${e.message}`);}}requestAnimationFrame(tick);};
await preset("standalone");
