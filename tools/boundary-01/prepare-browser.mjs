// Stage an ordinary static consumer of supplied immutable source bytes. No UI/solver changes.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const [repoArg, sourceArg, webArg, receiptArg] = process.argv.slice(2);
const repo = path.resolve(repoArg), web = path.resolve(webArg), target = path.join(web, 'reported');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
fs.mkdirSync(target, { recursive: true });
function copy(from, to) { const bytes = fs.readFileSync(from); if (fs.existsSync(to)) assert.deepEqual(fs.readFileSync(to), bytes); else fs.writeFileSync(to, bytes, { flag: 'wx' }); }
for (const file of ['index.html', 'main.js', 'style.css']) copy(path.join(repo, 'examples/runtime/web', file), path.join(target, file));
copy(path.resolve(sourceArg), path.join(target, 'source.json'));
function files(dir) { return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? files(path.join(dir, e.name)) : [path.join(dir, e.name)]); }
const resources = files(web).sort().map(file => { const bytes = fs.readFileSync(file); return { file: path.relative(web, file).replaceAll('\\', '/'), bytes: bytes.length, sha256: sha(bytes) }; });
const engine = path.join(repo, 'src/GearInvest.Engine/ConnectedWindingSource.cs');
const receipt = { preparedUtc: new Date().toISOString(), root: web, entry: '/reported/', sourceSha256: sha(fs.readFileSync(sourceArg)), engineSourceSha256: sha(fs.readFileSync(engine)), resources,
  resourceBytes: resources.reduce((n, r) => n + r.bytes, 0), method: 'Same ordinary example files; supplied source copied byte-for-byte; same freshly published shared C# WASM; no answer injection' };
fs.writeFileSync(path.resolve(receiptArg), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ sourceSha256: receipt.sourceSha256, engineSourceSha256: receipt.engineSourceSha256, resources: resources.length, resourceBytes: receipt.resourceBytes }));
