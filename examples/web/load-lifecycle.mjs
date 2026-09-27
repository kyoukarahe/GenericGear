import { AssemblyLoadGate } from '@gearinvest/presentation/assembly';

// Small example ownership rule: each pending candidate owns its definition/assets.
// The SDK gate remains the sole generation authority. A rejected promise owns no candidate.
export function modelLoads({ adopt, report, pending = () => {} }) {
  const gate = new AssemblyLoadGate();
  return {
    async load(prepare) {
      const token = gate.begin(); pending(token);
      let candidate;
      try {
        candidate = await prepare();
        if (!gate.isCurrent(token)) { candidate.dispose(); return 'stale'; }
        adopt(candidate); candidate = null; return 'adopted';
      } catch (error) {
        candidate?.dispose();
        if (gate.isCurrent(token)) { report(error); return 'failed'; }
        return 'stale-error';
      }
    },
    cancel() { gate.cancel(); },
    dispose() { gate.dispose(); }
  };
}
