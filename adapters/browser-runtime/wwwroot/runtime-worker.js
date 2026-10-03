// One isolated C# host per Worker. No synthesis or mechanical equations in this bridge.
import { dotnet } from './_framework/dotnet.js';
let dispatch, generation, active = null, last = null;
const ready = (async () => {
  const runtime = await dotnet.create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  dispatch = command => JSON.parse(exports.BrowserRuntime.Dispatch(JSON.stringify(command)));
  return runtime;
})();
const send = (id, value) => postMessage({ generation, id, ...value });
const yieldToMessages = () => new Promise(resolve => setTimeout(resolve, 0));
addEventListener('message', async ({ data: m }) => {
  if (!m || typeof m.id !== 'string' || typeof m.generation !== 'string') return;
  if (generation === undefined) generation = m.generation;
  if (generation !== m.generation) return;
  if (m.op === 'cancel') {
    if (active?.id === m.target) { active.cancelled = true; send(m.id, { status: 'CancellationRequested' }); }
    else if (last?.id === m.target) send(m.id, { status: last.committed ? 'TooLateCommitted' : last.status, committed: last.committed, stateId: last.stateId });
    else send(m.id, { status: 'UnknownRequestOutcome' });
    return;
  }
  if (active) { send(m.id, { status: 'Busy', committed: false }); return; }
  const operation = { id: m.id, cancelled: false }; active = operation;
  try {
    const started = performance.now(); await ready;
    if (operation.cancelled) { send(m.id, { status: 'CancelledBeforeCommit', committed: false }); return; }
    if (m.op === 'sealNetwork') {
      // Optional local-compute mode. All runtime/source assets must already be loaded.
      globalThis.fetch = () => Promise.reject(new Error('RuntimeNetworkSealed'));
      send(m.id, { status: 'NetworkSealed', resources: typeof performance.getEntriesByType === 'function' ? performance.getEntriesByType('resource').map(e => ({ name: e.name, transferSize: e.transferSize })) : [] });
      return;
    }
    if (m.op === 'advance') {
      const prepared = dispatch({ op: 'prepare', request: m.request });
      if (prepared.status !== 'Prepared') { send(m.id, prepared); return; }
      postMessage({ generation, id: m.id, phase: 'prepared', token: prepared.token });
      await yieldToMessages(); // Cancellation can arrive while C# was computing. Nothing is committed yet.
      const result = dispatch({ op: operation.cancelled ? 'cancel' : 'commit', token: prepared.token });
      last = { id: m.id, committed: result.committed === true, status: result.status, stateId: result.snapshot?.state.stateId };
      send(m.id, { ...result, computeAndPublishMs: performance.now() - started });
    } else {
      const result = dispatch(m.command ?? { op: m.op });
      // load/restore are isolated to this new Worker; the client only installs the selected generation.
      last = { id: m.id, committed: false, status: result.status };
      send(m.id, { ...result, workerMs: performance.now() - started });
    }
  } catch (error) { send(m.id, { status: 'WorkerError', detail: String(error), outcome: 'unknown-unless-state-readback' }); }
  finally { if (active === operation) active = null; }
});
