export function immutable(value) {
  if (value && typeof value === 'object') { for (const item of Object.values(value)) immutable(item); Object.freeze(value); }
  return value;
}

/** Thin client. Result objects are cloned by the Worker protocol, then recursively frozen. */
export class MechanicalRuntimeClient {
  #active; #pending = new Map(); #generation = 0n; #nextId = 0n; #disposed = false; #loading = new Set();
  constructor(workerUrl = new URL('./runtime-worker.js', import.meta.url), workerFactory = url => new Worker(url, { type: 'module' })) {
    this.workerUrl = workerUrl; this.workerFactory = workerFactory;
  }
  #channel() {
    const worker = this.workerFactory(this.workerUrl); const generation = String(++this.#generation);
    const channel = { worker, generation, dead: false }; this.#loading.add(channel);
    worker.addEventListener('message', ({ data }) => {
      if (channel.dead || data.generation !== generation) return;
      const key = generation + ':' + data.id; const p = this.#pending.get(key); if (!p) return;
      if (data.phase === 'prepared') { p.onPrepared?.(data); return; }
      this.#pending.delete(key); p.resolve(immutable(data));
    });
    worker.addEventListener('error', e => this.#end(channel, new Error('WorkerFailed: outcome unknown; restore last saved checkpoint. ' + e.message)));
    return channel;
  }
  #end(channel, error) {
    if (!channel || channel.dead) return; channel.dead = true; channel.worker.terminate(); this.#loading.delete(channel);
    for (const [key, p] of this.#pending) if (key.startsWith(channel.generation + ':')) { this.#pending.delete(key); p.reject(error); }
  }
  #send(channel, message, onPrepared) {
    if (this.#disposed || !channel || channel.dead) return { id: null, promise: Promise.reject(new Error('DisposedOrNotLoaded')) };
    const id = String(++this.#nextId); const key = channel.generation + ':' + id;
    // Clone NOW: edits to an application object after invoking advance cannot alter the queued request.
    const owned = structuredClone(message);
    const promise = new Promise((resolve, reject) => { this.#pending.set(key, { resolve, reject, onPrepared }); channel.worker.postMessage({ ...owned, generation: channel.generation, id }); });
    return { id, promise };
  }
  async #replace(command) {
    if (this.#disposed) throw new Error('Disposed');
    const channel = this.#channel(); const selected = this.#generation;
    try {
      const result = await this.#send(channel, { op: command.op, command }).promise;
      if (this.#disposed || selected !== this.#generation) throw new Error('SupersededLoad');
      if (!['Ready', 'Restored', 'ImportedRecording'].includes(result.status)) throw new Error(result.status + ': ' + (result.detail ?? ''));
      const previous = this.#active; this.#active = channel; this.#loading.delete(channel);
      this.#end(previous, new Error('SourceReplaced: any unacknowledged outcome is unknown'));
      return result;
    } catch (e) { this.#end(channel, e); throw e; }
  }
  load(sourceUtf8, sessionId, initialPlanet = '0') { return this.#replace({ op: 'load', sourceUtf8, sessionId, initialPlanet }); }
  restore(checkpointUtf8) { return this.#replace({ op: 'restore', checkpointUtf8 }); }
  importRecording(recordingUtf8, sessionId) { return this.#replace({ op: 'importRecording', recordingUtf8, sessionId }); }
  advance(request, { onPrepared } = {}) {
    const channel = this.#active; const task = this.#send(channel, { op: 'advance', request }, onPrepared);
    return { result: task.promise, cancel: () => this.#send(channel, { op: 'cancel', target: task.id }).promise };
  }
  snapshot() { return this.#send(this.#active, { op: 'snapshot' }).promise; }
  checkpoint() { return this.#send(this.#active, { op: 'checkpoint' }).promise; }
  sealNetwork() { return this.#send(this.#active, { op: 'sealNetwork' }).promise; }
  dispose() {
    if (this.#disposed) return; this.#disposed = true; ++this.#generation;
    this.#end(this.#active, new Error('Disposed: unacknowledged commit outcome unknown'));
    for (const channel of [...this.#loading]) this.#end(channel, new Error('Disposed'));
  }
}
