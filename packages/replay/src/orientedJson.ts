import { ArtifactInputError } from "./errors.js";
import { utf8ByteLength } from "./bounded.js";

export const ORIENTED_LIMITS = Object.freeze({ documentBytes: 4194304, sourceBytes: 524288, depth: 32, nodes: 65536, properties: 64,
  shafts: 34, bodies: 34, contacts: 33, ports: 4, connections: 2, mappings: 32, sources: 2, keepOuts: 16,
  fractionCharacters: 128, idCharacters: 160, domains: 2048, diagnostics: 256, teeth: 4096 });
function fail(message: string): never { throw new ArtifactInputError(message); }

/** Validate before traversing values or stringifying: no accessor/toJSON execution. */
export function assertOrientedJson(value: unknown, limits: {readonly [K in keyof typeof ORIENTED_LIMITS]:number} = ORIENTED_LIMITS): void {
  const active = new Set<object>(); let nodes = 0, bytes = 0;
  function visit(v: unknown, depth: number): void {
    if (++nodes > limits.nodes || depth > limits.depth) fail("Oriented JSON node/depth limit exceeded.");
    if (v === null || typeof v === "boolean") bytes += 5;
    else if (typeof v === "number") { if (!Number.isFinite(v)) fail("Non-JSON number."); bytes += 24; }
    else if (typeof v === "string") { bytes += utf8ByteLength(v) + 2; if (v.length > 4 * Math.ceil(limits.sourceBytes / 3)) fail("Oriented string limit exceeded."); }
    else if (typeof v === "object") {
      if (active.has(v)) fail("Cyclic input."); active.add(v);
      const array = Array.isArray(v), proto = Object.getPrototypeOf(v);
      if (array ? proto !== Array.prototype : proto !== Object.prototype && proto !== null) fail("Expected plain JSON data.");
      const keys = Reflect.ownKeys(v);
      if (array ? v.length > limits.nodes || keys.length !== v.length + 1 : keys.length > limits.properties) fail("Oriented array/property limit or sparse array.");
      for (const key of keys) {
        if (typeof key !== "string") fail("Symbol keys are not JSON.");
        if (array && key === "length") continue;
        if (key.length > limits.idCharacters || (array && (!/^(0|[1-9][0-9]*)$/.test(key) || Number(key) >= v.length))) fail("Extended array or invalid key.");
        const descriptor = Object.getOwnPropertyDescriptor(v, key)!;
        if (!Object.hasOwn(descriptor, "value") || !descriptor.enumerable) fail("Accessors/hidden properties are not JSON data.");
        bytes += utf8ByteLength(key) + 4; visit(descriptor.value, depth + 1);
      }
      active.delete(v); bytes += 2;
    } else fail("Non-JSON input value.");
    if (bytes > limits.documentBytes) fail("Oriented document byte budget exceeded.");
  }
  visit(value, 0);
}

/** Text-level pass detects escaped-equivalent duplicate keys BEFORE JSON.parse erases them. */
export function readOrientedJsonText(text: string, limits: {readonly [K in keyof typeof ORIENTED_LIMITS]:number} = ORIENTED_LIMITS): unknown {
  if (typeof text !== "string" || utf8ByteLength(text) > limits.documentBytes) fail("Oriented document byte limit exceeded.");
  let i = 0, nodes = 0;
  const ws = () => { while (i < text.length && /[ \t\r\n]/.test(text[i]!)) i++; };
  function string(): string {
    const start = i++; if (text[start] !== '"') fail("Expected JSON string.");
    let closed = false;
    while (i < text.length) { const c = text[i++]; if (c === "\\") i++; else if (c === '"') { closed = true; break; } }
    if (!closed) fail("Unterminated JSON string.");
    try { return JSON.parse(text.slice(start, i)) as string; } catch { return fail("Invalid JSON string."); }
  }
  function value(depth: number): void {
    ws(); if (++nodes > limits.nodes || depth > limits.depth) fail("Oriented JSON node/depth limit exceeded.");
    const c = text[i];
    if (c === '"') { string(); return; }
    if (c === "{" || c === "[") {
      i++; ws(); const end = c === "{" ? "}" : "]", keys = new Set<string>();
      if (text[i] === end) { i++; return; }
      for (;;) {
        if (c === "{") { ws(); const key = string(); if (keys.has(key)) fail(`Duplicate JSON key '${key}'.`); keys.add(key);
          if (keys.size > limits.properties) fail("Oriented property limit exceeded."); ws(); if (text[i++] !== ":") fail("Expected colon."); }
        value(depth + 1); ws(); if (text[i] === end) { i++; return; } if (text[i++] !== ",") fail("Expected JSON separator.");
      }
    }
    const match = /^(?:true|false|null|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)/.exec(text.slice(i));
    if (!match) fail("Invalid JSON value."); i += match[0].length;
  }
  value(0); ws(); if (i !== text.length) fail("Trailing JSON data.");
  try { return JSON.parse(text) as unknown; } catch { return fail("Invalid JSON."); }
}
