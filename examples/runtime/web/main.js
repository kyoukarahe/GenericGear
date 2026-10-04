import { MechanicalRuntimeClient } from '../runtime-client.js';
import { readCheckpoint, publishCheckpoint } from '../checkpoint-store.js';
const $ = id => document.getElementById(id);
const runtime = new MechanicalRuntimeClient();
const slot = new URL(location.href).searchParams.get('slot') ?? 'genericgear-e123-example-v1';
document.querySelector('a[href="?restore=1"]').href = '?restore=1&slot=' + encodeURIComponent(slot);
let result, scene = [], sequence = 0, savedId = null, active, checkpoint;
const log = [];
function record(kind, data) { log.push({ kind, ...data }); if (log.length > 32) log.shift(); $('log').textContent = JSON.stringify(log, null, 2); }
function fraction(text) {
  const parts = text.trim().split('/'); if (!/^-?\d+$/.test(parts[0]) || parts.length > 2 || parts[1] !== undefined && !/^\d+$/.test(parts[1])) throw new Error('입력은 정수 또는 n/d 분수여야 합니다.');
  let n = BigInt(parts[0]), d = BigInt(parts[1] ?? '1'); if (d === 0n) throw new Error('분모 0은 허용되지 않습니다.');
  let a = n < 0n ? -n : n, b = d; while (b) [a, b] = [b, a % b]; n /= a; d /= a;
  return { numerator: String(n), denominator: String(d) };
}
function clearFailure(message) { $('status').textContent = message; $('scene').replaceChildren(); $('quality').textContent = '새 요청 실패 — 이전 화면을 새 성공 결과로 표시하지 않습니다. 아래 identity는 마지막 정상 상태입니다.'; }
function show(r) {
  if (!r.snapshot) { clearFailure(r.status + (r.detail ? ': ' + r.detail : '')); record('refusal', r); return; }
  result = r; if (r.snapshot.scene) scene = r.snapshot.scene; const s = r.snapshot.state;
  $('drive-boundary').textContent = `Prescribed: ${r.capabilities.prescribedShaft} · coupling rotor: ${r.capabilities.couplingShaft} · passive: ${r.capabilities.passiveShaft} · 축 native turn (+1, offset 0). 역할은 source 작성 시 고정됩니다.`;
  $('status').textContent = `${r.status} · ${s.mode} · revision ${s.revision} · epoch ${s.epoch}`;
  $('quality').textContent = 'C# source/active constraints/finite path 검산 · NumericResidualOnly · solution error bound: null · 삭제 이력: notPerformed';
  $('planet-label').hidden = !s.requiredInputPorts.includes(r.capabilities.planetPort);
  $('sun-label').hidden = !s.requiredInputPorts.includes(r.capabilities.sunPort);
  $('identity').replaceChildren();
  for (const [name, value] of Object.entries({ source: r.snapshot.sourceArtifactId, state: s.stateId, session: s.sessionId, eventCursor: s.eventCursor, liveProvenance: s.witnesses.length, ledger: s.ledger.length, checkpointTrust: 'current-state-source-revalidated' })) {
    const dt = document.createElement('dt'), dd = document.createElement('dd'); dt.textContent = name; dd.textContent = String(value); $('identity').append(dt, dd);
  }
  $('coordinates').textContent = s.frame.coordinates.map(c => `${c.shaftId}: ${c.value.exact ? c.value.exact.numerator + '/' + c.value.exact.denominator : c.value.estimate} (${c.value.kind})`).join('\n');
  $('scene').replaceChildren();
  if (s.frame.displayUnavailableReason) { $('quality').textContent += ' · DisplayUnavailable: ' + s.frame.displayUnavailableReason; return; }
  const spatial = s.frame.winding.profile === 'finite-spatial-guided-pin-chain-v1';
  $('projection-label').hidden = !spatial;
  if (spatial) {
    $('quality').textContent = `${s.frame.winding.pins.length - 1}개 고정 피치 링크 · 3D pin-guide · pitch residual ${s.frame.winding.pitchResidualMm} mm · NumericResidualOnly / error bound null · 구간은 수치 표본 검사(연속 증명 아님) · 링크 roll 미정`;
    drawSpatial(s.frame); return;
  }
  const ns = 'http://www.w3.org/2000/svg'; const planes = new Map(), bounds = new Map();
  for (const pose of s.frame.matricesMm) {
    const node = scene.find(n => n.id === pose.id); if (!node) continue; const m = pose.matrix, z = m[14];
    if (!planes.has(z)) { const svg = document.createElementNS(ns, 'svg'); svg.setAttribute('aria-label', '실제 부품 plane ' + z + ' mm · 독립 auto-fit'); planes.set(z, svg); bounds.set(z, []); }
    const points = node.kind === 'polyline' ? node.pointsMm : [[-node.radiusMm,-node.radiusMm],[node.radiusMm,node.radiusMm],[-node.radiusMm,node.radiusMm],[node.radiusMm,-node.radiusMm]];
    for (const [x,y] of points) bounds.get(z).push([m[0]*x+m[4]*y+m[12],-(m[1]*x+m[5]*y+m[13])]);
    const shape = document.createElementNS(ns, node.kind === 'polyline' ? 'polyline' : 'circle');
    shape.setAttribute('transform', `matrix(${m[0]} ${-m[1]} ${m[4]} ${-m[5]} ${m[12]} ${-m[13]})`);
    shape.setAttribute('stroke', node.kind === 'pin' ? '#f2c984' : '#83d8d0'); shape.setAttribute('stroke-width', '.35'); shape.setAttribute('fill', node.kind === 'pin' ? '#f2c984' : 'none');
    if (node.kind === 'polyline') shape.setAttribute('points', node.pointsMm.map(p => p.join(',')).join(' ')); else shape.setAttribute('r', String(node.radiusMm));
    const title = document.createElementNS(ns, 'title'); title.textContent = node.owner; shape.append(title); planes.get(z).append(shape);
  }
  for (const [z, svg] of [...planes].sort(([a], [b]) => a - b)) {
    const points=bounds.get(z), xs=points.map(p=>p[0]), ys=points.map(p=>p[1]);
    const xmin=Math.min(...xs),xmax=Math.max(...xs),ymin=Math.min(...ys),ymax=Math.max(...ys),size=Math.max(xmax-xmin,ymax-ymin,1)*1.15;
    svg.setAttribute('viewBox',`${(xmin+xmax-size)/2} ${(ymin+ymax-size)/2} ${size} ${size}`); $('scene').append(svg);
  }
}
function drawSpatial(frame) {
  const ns='http://www.w3.org/2000/svg', svg=document.createElementNS(ns,'svg'), points=[];
  const project=([x,y,z])=>$('projection').value==='xy'?[x,-y]:$('projection').value==='xz'?[x,-z]:[(x-y)*.866,(x+y)*.35-z];
  svg.setAttribute('aria-label','같은 snapshot의 전체 3D 링크·차동·기어·carrier');svg.style.gridColumn='1 / -1';
  for(const pose of frame.matricesMm) {
    const node=scene.find(n=>n.id===pose.id);if(!node)continue;const m=pose.matrix;
    const local=node.kind==='polyline'?node.pointsMm:Array.from({length:33},(_,i)=>[node.radiusMm*Math.cos(i*Math.PI/16),node.radiusMm*Math.sin(i*Math.PI/16),0]);
    const vertices=local.map(([x,y,z=0])=>project([m[0]*x+m[4]*y+m[8]*z+m[12],m[1]*x+m[5]*y+m[9]*z+m[13],m[2]*x+m[6]*y+m[10]*z+m[14]]));
    points.push(...vertices);const shape=document.createElementNS(ns,'polyline');shape.setAttribute('points',vertices.map(p=>p.join(',')).join(' '));
    shape.setAttribute('stroke',node.kind==='pin'?'#f2c984':node.owner.includes('drum')?'#496b8b':'#83d8d0');shape.setAttribute('stroke-width',node.kind==='pin'?'.13':'.22');shape.setAttribute('fill','none');
    const title=document.createElementNS(ns,'title');title.textContent=node.owner;shape.append(title);svg.append(shape);
  }
  if(!points.length)return;
  const xs=points.map(p=>p[0]),ys=points.map(p=>p[1]),xmin=Math.min(...xs)-3,xmax=Math.max(...xs)+3,ymin=Math.min(...ys)-3,ymax=Math.max(...ys)+3;
  svg.setAttribute('viewBox',`${xmin} ${ymin} ${xmax-xmin} ${ymax-ymin}`);$('scene').append(svg);
}
$('projection').onchange=()=>{if(result)show(result);};
function enabled(on) { for (const id of ['advance', 'save', 'restore', 'seal', 'download']) $(id).disabled = !on; }
async function boot() {
  const started = performance.now(); const stored = await readCheckpoint(slot); savedId = stored?.artifactId ?? null;
  const r = new URL(location.href).searchParams.has('restore') ? (stored ? await runtime.restore(stored.checkpointUtf8) : (() => { throw new Error('저장된 체크포인트가 없습니다.'); })()) :
    await runtime.load(await (await fetch('./source.json')).text(), 'web-' + crypto.randomUUID(), '3/10');
  show(r); enabled(true); $('loading').textContent = `로컬 Worker 준비 완료 (${(performance.now() - started).toFixed(0)} ms) · q domain [${r.capabilities.driverMinimumTurns}, ${r.capabilities.driverMaximumTurns}]`;
  if (stored) $('storage').textContent = '저장 slot: ' + stored.stateId;
  record('boot', { ms: performance.now() - started, userAgent: navigator.userAgent, resourceCount: performance.getEntriesByType('resource').length });
}
$('controls').addEventListener('submit', async event => {
  event.preventDefault(); if (!result) return; const s = result.snapshot.state; const id = 'ui-' + crypto.randomUUID(); const ports = {};
  try {
    for (const p of s.requiredInputPorts) ports[p] = fraction($(p === result.capabilities.sunPort ? 'sun' : 'planet').value);
    const kind = $('event').value;
    const request = { id, sessionId: s.sessionId, definitionId: s.definitionId, expectedStateId: s.stateId, epoch: s.epoch, revision: s.revision,
      segments: [{ driverTurns: fraction($('q').value), independentPorts: ports, observations: {}, events: kind ? [{ id: id + '/event', sequence: String(BigInt(s.eventCursor) + 1n), ordinal: '0', kind }] : [] }] };
    enabled(false); $('cancel').disabled = false; const started = performance.now(); active = runtime.advance(request); const r = await active.result;
    show(r); record('new-input', { request, status: r.status, stateId: r.snapshot?.state.stateId, roundTripMs: performance.now() - started, workerMs: r.computeAndPublishMs }); ++sequence;
  } catch (e) { clearFailure(String(e)); } finally { active = null; $('cancel').disabled = true; enabled(true); }
});
$('cancel').onclick = async () => { if (active) record('cancel', await active.cancel()); };
$('save').onclick = async () => {
  try { const started = performance.now(); checkpoint = await runtime.checkpoint(); const stored = await publishCheckpoint(slot, savedId, checkpoint); savedId = stored.artifactId;
    $('storage').textContent = `저장 완료 · ${stored.stateId} · ${new TextEncoder().encode(stored.checkpointUtf8).length} bytes`; record('save', { artifactId: savedId, ms: performance.now() - started }); }
  catch (e) { $('storage').textContent = '저장 실패 — 이전 checkpoint 유지: ' + e; }
};
$('restore').onclick = async () => { try { const stored = await readCheckpoint(slot); if (!stored) throw new Error('저장 상태 없음'); show(await runtime.restore(stored.checkpointUtf8)); savedId = stored.artifactId; $('network').textContent = '복원으로 새 Worker를 준비했습니다. 이 Worker의 계산 네트워크 차단은 다시 실행하세요.'; } catch (e) { clearFailure(String(e)); } };
$('seal').onclick = async () => {
  const receipt = await runtime.sealNetwork(); globalThis.fetch = () => Promise.reject(new Error('PageNetworkSealed'));
  $('network').textContent = '페이지 + Worker fetch 차단 완료. 새 입력/이벤트/로컬 저장은 계속 사용할 수 있습니다.'; record('network-sealed', receipt);
};
$('download').onclick = async () => {
  checkpoint = await runtime.checkpoint(); if (checkpoint.status !== 'CheckpointCreated') throw new Error(checkpoint.status);
  const url = URL.createObjectURL(new Blob([checkpoint.checkpointUtf8], { type: 'application/json' })); const a = document.createElement('a'); a.href = url; a.download = 'mechanism.runtime-checkpoint.json'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
};
addEventListener('pagehide', () => runtime.dispose());
boot().catch(e => clearFailure(String(e)));
