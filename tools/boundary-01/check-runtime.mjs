// Ordinary MechanicalRuntimeHost consumer. No mechanical results are supplied by this verifier.
import fs from 'node:fs';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const [repoArg, sourceArg, outputArg, phase = 'after', webArg] = process.argv.slice(2);
assert(['before', 'after'].includes(phase));
const repo = path.resolve(repoArg), output = path.resolve(outputArg);
const cli = path.join(repo, 'examples/runtime/dotnet/bin/Release/net8.0/Runtime.Consumer.dll');
const sourceBytes = fs.readFileSync(sourceArg), sourceUtf8 = sourceBytes.toString('utf8');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
fs.mkdirSync(output, { recursive: true });
const save = (file, value) => fs.writeFileSync(path.join(output, file), typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const git = (...args) => { const r = spawnSync('git', args, { cwd: repo, encoding: 'utf8', windowsHide: true }); assert.equal(r.status, 0); return r.stdout.trim(); };
// A source ZIP has no .git. Never inherit the identity of a containing checkout.
const checkout = fs.existsSync(path.join(repo, '.git'));
const inventoryBytes = checkout ? null : fs.readFileSync(path.join(repo, 'PUBLIC_FILES.json'));
const inventory = inventoryBytes === null ? null : JSON.parse(inventoryBytes);
const sourcePaths = checkout ? git('ls-files', 'src').split('\n') : inventory.files.map(f => f.path);
const sourceFiles = sourcePaths.filter(f => /^src\/GearInvest(?:\.Core|\.Layout|\.Engine|\.Modules\.Clock|\.Serialization\.Json)?\/[^/]+\.cs$/.test(f)).map(file => ({ file, sha256: hash(fs.readFileSync(path.join(repo, file))) }));
if (inventory) for (const f of sourceFiles) assert.equal(f.sha256, inventory.files.find(p => p.path === f.file).sha256, f.file);
assert(sourceFiles.length > 0, 'No production source inventory');
const build = { repository: repo, head: checkout ? git('rev-parse', 'HEAD') : null, sourceDiff: checkout ? git('diff', '--', 'src') : null,
  sourceSnapshot: inventoryBytes === null ? null : { candidate: inventory.candidate, publicInventorySha256: hash(inventoryBytes) },
  assemblies: fs.readdirSync(path.dirname(cli)).filter(f => /^(GearInvest.*|Runtime.Consumer)\.dll$/.test(f)).map(file => ({ file, sha256: hash(fs.readFileSync(path.join(path.dirname(cli), file))) })),
  sourceFiles };
save('build.json', build);
function managed() {
  const child = spawn('dotnet', [cli], { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true }), pending = [];
  createInterface({ input: child.stdout, crlfDelay: Infinity }).on('line', line => { const item = pending.shift(); assert(item, 'Unsolicited native result'); try { item.resolve(JSON.parse(line)); } catch (e) { item.reject(e); } });
  child.on('exit', code => { for (const item of pending.splice(0)) item.reject(new Error('Native exited ' + code)); });
  return { pid: child.pid, send: value => new Promise((resolve, reject) => { pending.push({ resolve, reject }); child.stdin.write(JSON.stringify(value) + '\n'); }), close: () => child.stdin.end() };
}
let native = managed(), wasm, comparisons = 0, maxNumericDifference = 0;
const processIds = [native.pid], observations = [];
if (webArg) {
  const { dotnet } = await import(pathToFileURL(path.join(path.resolve(webArg), '_framework/dotnet.js')));
  const runtime = await dotnet.create(), exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  wasm = command => JSON.parse(exports.BrowserRuntime.Dispatch(JSON.stringify(command)));
}
function same(a, b, key = '') {
  if (typeof a === 'number' && typeof b === 'number') {
    const delta = Math.abs(a - b); maxNumericDifference = Math.max(maxNumericDifference, delta);
    assert(delta <= (key.includes('matrix') ? 1e-8 : 1e-10), `${key}: ${a} != ${b}`); return;
  }
  if (a !== null && b !== null && typeof a === 'object' && typeof b === 'object') {
    assert.deepEqual(Object.keys(a), Object.keys(b), key); for (const k of Object.keys(a)) same(a[k], b[k], key + '/' + k); return;
  }
  assert.equal(a, b, key);
}
async function send(command) {
  const a = await native.send(command); if (wasm) { same(a, wasm(command)); comparisons++; } return a;
}
function fraction(text) {
  const [ns, ds = '1'] = text.split('/'); let n = BigInt(ns), d = BigInt(ds), a = n < 0n ? -n : n, b = d;
  while (b) [a, b] = [b, a % b]; return { numerator: String(n / a), denominator: String(d / a) };
}
let current, initial, serial = 0;
function request(values) {
  const s = current.snapshot.state;
  return { id: 'boundary-' + (++serial), sessionId: s.sessionId, definitionId: s.definitionId, expectedStateId: s.stateId, epoch: s.epoch, revision: s.revision,
    segments: values.map(value => ({ driverTurns: typeof value === 'string' ? fraction(value) : value,
      independentPorts: { [current.capabilities.planetPort]: fraction('3/10') }, observations: {}, events: [] })) };
}
function invariant(result, value) {
  const s = result.snapshot.state;
  assert.equal(result.snapshot.sourceArtifactId, initial.snapshot.sourceArtifactId);
  assert.deepEqual(result.capabilities, initial.capabilities);
  assert.equal(s.mode, initial.snapshot.state.mode);
  assert.deepEqual(s.driveBoundary, initial.snapshot.state.driveBoundary);
  assert.deepEqual(s.frame.driverTurns, fraction(value));
  assert.deepEqual(s.frame.coordinates.find(c => c.shaftId === result.capabilities.prescribedShaft).value.exact, fraction(value));
  assert.deepEqual(s.frame.winding.pins.map(p => p.id), initial.snapshot.state.frame.winding.pins.map(p => p.id));
  assert.deepEqual(s.frame.ports.find(p => p.portId === result.capabilities.planetPort).value.exact, fraction('3/10'));
}
async function step(values, expected = 'Accepted') {
  if (!Array.isArray(values)) values = [values];
  const before = (await send({ op: 'snapshot' })).snapshot, r = request(values);
  let response = await send({ op: 'prepare', request: r });
  // Prepare is not publication, including the successful case.
  assert.deepEqual((await send({ op: 'snapshot' })).snapshot, before);
  if (expected === 'Accepted') { assert.equal(response.status, 'Prepared'); response = await send({ op: 'commit', token: response.token }); }
  assert.equal(response.status, expected, JSON.stringify(values));
  const readback = await send({ op: 'snapshot' });
  if (expected === 'Accepted') {
    assert.equal(response.committed, true); same(response.snapshot.state, readback.snapshot.state);
    current = response; invariant(response, values.at(-1));
  } else {
    assert.equal(response.committed, false); assert.deepEqual(readback.snapshot, before);
    if (response.lastValidStateId) assert.equal(response.lastValidStateId, before.state.stateId);
  }
  observations.push({ request: r, status: response.status, stateId: readback.snapshot.state.stateId, revision: readback.snapshot.state.revision,
    eventCursor: readback.snapshot.state.eventCursor, mode: readback.snapshot.state.mode, driverTurns: readback.snapshot.state.frame.driverTurns, atomic: expected === 'Accepted' ? 'committed-once' : 'full-snapshot-unchanged' });
  return response;
}
try {
  initial = current = await send({ op: 'load', sourceUtf8, sessionId: 'boundary-01-original', initialPlanet: '3/10' });
  assert.equal(current.status, 'Ready'); assert.equal(current.capabilities.driverMinimumTurns, -1); assert.equal(current.capabilities.driverMaximumTurns, .5);
  invariant(current, '1/2'); save('initial.json', current);
  await step('4562/11000');
  await step('-1', phase === 'before' ? 'WindingBoundary' : 'Accepted');
  if (phase === 'after') {
    save('boundary.json', current);
    const ck = await native.send({ op: 'checkpoint' }); assert.equal(ck.status, 'CheckpointCreated'); save('boundary.checkpoint.json', ck.checkpointUtf8);
    if (wasm) { const wck = wasm({ op: 'checkpoint' }); assert.equal(wck.status, 'CheckpointCreated'); save('wasm-boundary.checkpoint.json', wck.checkpointUtf8); }
    native.close(); native = managed(); processIds.push(native.pid);
    current = await send({ op: 'restore', checkpointUtf8: ck.checkpointUtf8 }); assert.equal(current.status, 'Restored'); invariant(current, '-1');
    save('restored.json', current);
    await step('-3/4'); save('continued.json', current);
    for (const value of ['123/1000', '1/2', '9/37', '-1', '-1']) await step(value);
    for (const value of [
      '-4503599627370497/4503599627370496', '4503599627370497/9007199254740992',
      '-100000000000000000001/100000000000000000000', '50000000000000000001/100000000000000000000'
    ]) await step(value, 'WindingBoundary');
    await step(['-3/4', '-100000000000000000001/100000000000000000000', '-1'], 'WindingBoundary');
    await step({ numerator: 'NaN', denominator: '1' }, 'InvalidInput');
    await step({ numerator: '1', denominator: '0' }, 'InvalidInput');
    await step({ numerator: '1' + '0'.repeat(100), denominator: '1' }, 'WindingBoundary');
    // 129 digits violates the 128-digit input contract. The unchanged host maps
    // "Exact input digit bound exceeded" to InvalidInput, not ResourceLimit.
    await step({ numerator: '1' + '0'.repeat(128), denominator: '1' }, 'InvalidInput');
  }
  const from = 4562 / 11000, to = -1, count = Math.ceil(Math.abs(to - from) * 128);
  const report = { verdict: phase === 'before' ? 'CONFIRMED_RUNTIME_BOUNDARY_FAILURE' : 'PASS', phase, sourceSha256: hash(sourceBytes), sdkHead: build.head,
    sourceArtifactId: initial.snapshot.sourceArtifactId, host: webArg ? 'native-and-Node-WASM (not a real browser)' : 'native MechanicalRuntimeHost',
    arithmeticOnly: { from, to, count, oldFormulaEndpoint: from + (to - from) * count / count },
    processIds, comparisons, maxNumericDifference, observations, finalStateId: current.snapshot.state.stateId, mobile: 'UNVERIFIED', pathGap: 'OPEN' };
  save('result.json', report); console.log(JSON.stringify({ ...report, observations: observations.map(o => ({ status: o.status, q: o.driverTurns, revision: o.revision })) }, null, 2));
} catch (error) {
  save('failure.json', { error: String(error), stack: error.stack, observations }); throw error;
} finally { native.close(); }
