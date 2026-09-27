import { ArtifactInputError } from "./errors.js";
// UTF-8 length, replacing lone UTF-16 surrogates just as TextEncoder does.
// No DOM declaration or Node runtime import is needed.
export function utf8ByteLength(text: string): number {
  let bytes = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c < 0x80) bytes++;
    else if (c < 0x800) bytes += 2;
    else if (c >= 0xd800 && c <= 0xdbff && i + 1 < text.length &&
        text.charCodeAt(i + 1) >= 0xdc00 && text.charCodeAt(i + 1) <= 0xdfff) { bytes += 4; i++; }
    else bytes += 3;
  }
  return bytes;
}
/** Bounds object input too, including ignored fields; never executes accessors/toJSON. */
export function assertBoundedJson(value: unknown, maxBytes: number): void {
  let bytes = 0, nodes = 0;
  const ancestors = new Set<object>();
  function visit(item: unknown, depth: number): void {
    if (++nodes > 65536 || depth > 32) throw new ArtifactInputError("Input exceeds the node/depth limit.");
    if (typeof item === "string") {
      if (item.length > 4096) throw new ArtifactInputError("Input string exceeds the 4096-character limit.");
      bytes += utf8ByteLength(JSON.stringify(item));
    } else if (item === null || typeof item === "boolean" || (typeof item === "number" && Number.isFinite(item))) {
      bytes += JSON.stringify(item).length;
    } else if (typeof item === "object") {
      if (ancestors.has(item)) throw new ArtifactInputError("Cyclic object input is not JSON.");
      const array = Array.isArray(item);
      if (!array && Object.getPrototypeOf(item) !== Object.prototype && Object.getPrototypeOf(item) !== null)
        throw new ArtifactInputError("Input must contain plain JSON records.");
      const keys = Reflect.ownKeys(item);
      if (array && (keys.length !== item.length + 1 || keys.some(key => key !== "length" &&
          (typeof key !== "string" || !/^(0|[1-9][0-9]*)$/.test(key) || Number(key) >= item.length))))
        throw new ArtifactInputError("Sparse arrays and extra array properties are not JSON.");
      if (keys.some(key => typeof key !== "string")) throw new ArtifactInputError("Symbol properties are not JSON.");
      if (array ? item.length > 4096 : keys.length > 256) throw new ArtifactInputError("Input collection/property limit exceeded.");
      ancestors.add(item); bytes += 2;
      for (const key of keys) {
        if (array && key === "length") continue;
        const descriptor = Object.getOwnPropertyDescriptor(item, key)!;
        if (!descriptor.enumerable || !("value" in descriptor)) throw new ArtifactInputError("Accessors and hidden fields are not JSON.");
        if (typeof key !== "string" || key.length > 256) throw new ArtifactInputError("Input property name limit exceeded.");
        bytes += array ? 1 : utf8ByteLength(JSON.stringify(key)) + 2;
        visit(descriptor.value, depth + 1);
      }
      ancestors.delete(item);
    } else throw new ArtifactInputError("Input contains a non-JSON value.");
    if (bytes > maxBytes) throw new ArtifactInputError("Input exceeds its byte limit.");
  }
  visit(value, 0);
}
