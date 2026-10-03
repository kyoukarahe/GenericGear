import { parentPort, workerData } from 'node:worker_threads';
// Acceptance-only fault injection: an uncaught worker error, never a substituted solver result.
parentPort.on('message', value => { if (value?.acceptanceFault === true) throw new Error('Injected worker termination'); });
globalThis.postMessage = value => parentPort.postMessage(value);
globalThis.addEventListener = (kind, callback) => { if (kind === 'message') parentPort.on('message', data => callback({ data })); };
await import(workerData);
