import { ArtifactInputError } from "./errors.js";
const bad = (): never => { throw new ArtifactInputError("Invalid bounded UTF-8/base64 bytes."); };
const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
export function decodeBase64(v: unknown, maximum: number): Uint8Array {
  if (typeof v !== "string" || v.length % 4 || v.length > 4 * Math.ceil(maximum / 3)) return bad();
  const padding = v.endsWith("==") ? 2 : v.endsWith("=") ? 1 : 0, length = v.length / 4 * 3 - padding;
  if (length < 0 || length > maximum) return bad();
  const result = new Uint8Array(length); let bits = 0, count = 0, offset = 0;
  for (let i = 0; i < v.length - padding; i++) {
    const n = alphabet.indexOf(v[i]!); if (n < 0) return bad();
    bits = (bits << 6) | n; count += 6;
    if (count >= 8) { count -= 8; result[offset++] = (bits >> count) & 255; }
  }
  if ((bits & ((1 << count) - 1)) !== 0 || offset !== length) return bad();
  return result;
}
export function encodeUtf8(text: string, maximum: number): Uint8Array {
  // Allocate once with a checked conservative bound; no host globals or locale encodings.
  let length = 0;
  for (const c of text) { const n = c.codePointAt(0)!; if (n >= 0xd800 && n <= 0xdfff) return bad(); length += n < 128 ? 1 : n < 2048 ? 2 : n < 65536 ? 3 : 4; }
  if (length > maximum) return bad(); const bytes = new Uint8Array(length); let i = 0;
  for (const c of text) {
    const n = c.codePointAt(0)!;
    if (n < 128) bytes[i++] = n;
    else if (n < 2048) { bytes[i++] = 192 | (n >> 6); bytes[i++] = 128 | (n & 63); }
    else if (n < 65536) { bytes[i++] = 224 | (n >> 12); bytes[i++] = 128 | ((n >> 6) & 63); bytes[i++] = 128 | (n & 63); }
    else { bytes[i++] = 240 | (n >> 18); bytes[i++] = 128 | ((n >> 12) & 63); bytes[i++] = 128 | ((n >> 6) & 63); bytes[i++] = 128 | (n & 63); }
  }
  return bytes;
}
export function decodeUtf8(bytes: Uint8Array, maximum: number): string {
  if (!(bytes instanceof Uint8Array) || bytes.length > maximum) return bad();
  const chunks: string[] = []; let current = "";
  for (let i = 0; i < bytes.length;) {
    const b = bytes[i++]!; let n: number, follow: number, minimum: number;
    if (b < 128) { n = b; follow = 0; minimum = 0; }
    else if (b >= 194 && b <= 223) { n = b & 31; follow = 1; minimum = 128; }
    else if (b >= 224 && b <= 239) { n = b & 15; follow = 2; minimum = 2048; }
    else if (b >= 240 && b <= 244) { n = b & 7; follow = 3; minimum = 65536; }
    else return bad();
    for (let j = 0; j < follow; j++) { const c = bytes[i++]; if (c === undefined || c < 128 || c > 191) return bad(); n = (n << 6) | (c & 63); }
    if (n < minimum || n > 0x10ffff || (n >= 0xd800 && n <= 0xdfff)) return bad();
    current += String.fromCodePoint(n); if (current.length >= 8192) { chunks.push(current); current = ""; }
  }
  chunks.push(current); return chunks.join("");
}
