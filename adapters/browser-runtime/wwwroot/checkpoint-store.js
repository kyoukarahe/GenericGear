// IndexedDB publishes a complete checkpoint in one compare-and-swap transaction.
// The caller obtains validated bytes from runtime.checkpoint/restore, never a partial stream.
const databaseName = 'gearinvest-runtime-checkpoints-v1';
function open() {
  return new Promise((resolve, reject) => {
    const r = indexedDB.open(databaseName, 1);
    r.onupgradeneeded = () => r.result.createObjectStore('checkpoints');
    r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error);
  });
}
export async function readCheckpoint(key) {
  const db = await open();
  try { return await new Promise((resolve, reject) => { const r = db.transaction('checkpoints').objectStore('checkpoints').get(key); r.onsuccess = () => resolve(r.result ?? null); r.onerror = () => reject(r.error); }); }
  finally { db.close(); }
}
export async function publishCheckpoint(key, expectedArtifactId, checkpoint) {
  if (checkpoint.status !== 'CheckpointCreated' || typeof checkpoint.artifactId !== 'string' || typeof checkpoint.checkpointUtf8 !== 'string') throw new Error('CompleteRuntimeCheckpointRequired');
  const owned = structuredClone({ artifactId: checkpoint.artifactId, checkpointUtf8: checkpoint.checkpointUtf8, stateId: checkpoint.stateId });
  const db = await open();
  try {
    return await new Promise((resolve, reject) => {
      const tx = db.transaction('checkpoints', 'readwrite'); const store = tx.objectStore('checkpoints'); const read = store.get(key); let conflict = false;
      read.onsuccess = () => { if ((read.result?.artifactId ?? null) !== expectedArtifactId) { conflict = true; tx.abort(); } else store.put(owned, key); };
      tx.oncomplete = () => resolve(owned); tx.onabort = () => reject(new Error(conflict ? 'CheckpointWriteConflict' : 'CheckpointWriteAborted')); tx.onerror = () => reject(tx.error);
    });
  } finally { db.close(); }
}
