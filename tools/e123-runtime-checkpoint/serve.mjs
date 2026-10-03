import http from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
const root = path.resolve(process.argv[2]); const port = Number(process.argv[3] ?? 5187);
const types = { '.js': 'text/javascript', '.json': 'application/json', '.html': 'text/html', '.css': 'text/css', '.wasm': 'application/wasm', '.pdb': 'application/octet-stream', '.dat': 'application/octet-stream' };
const server = http.createServer(async (req, res) => {
  try {
    if (req.method !== 'GET' && req.method !== 'HEAD') { res.writeHead(405).end(); return; }
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    let file = path.resolve(root, '.' + pathname); if (!file.startsWith(root + path.sep) && file !== root) throw new Error('path');
    if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
    const bytes = await readFile(file); res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-cache' }); res.end(req.method === 'HEAD' ? undefined : bytes);
    console.log(JSON.stringify({ method: req.method, path: pathname, bytes: bytes.length }));
  } catch { res.writeHead(404).end(); }
});
server.listen(port, '127.0.0.1', () => console.log('STATIC ONLY http://127.0.0.1:' + port + '/example/'));
