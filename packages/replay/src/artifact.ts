import type {
  ExactFraction,
  ExternalGearCoupling,
  GeneratorFingerprint,
  KinematicState,
  MechanismArtifact,
  PlaybackBodyBinding,
  PlaybackChannel,
  PlaybackDriver,
  RotationalDof,
  SpatialAxis,
  SpatialBody,
  SpatialContact,
  StoredDiagnostic,
  CompositionMetadataObservation,
} from "./types.js";
import { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";
import { assertBoundedJson, utf8ByteLength } from "./bounded.js";
import { deepFreeze } from "./immutable.js";
export { ArtifactInputError, UnsupportedArtifactError } from "./errors.js";

const MAX_ARTIFACT_BYTES = 2 * 1024 * 1024;
const MAX_COLLECTION_ITEMS = 4096;
const MAX_STRING_LENGTH = 4096;
const MAX_ID_LENGTH = 256;
const MAX_INTEGER_DIGITS = 128;
const MAX_OPTIONAL_DEPTH = 12;
const MAX_OPTIONAL_NODES = 4096;
const MAX_OPTIONAL_PROPERTIES = 256;
const CANONICAL_INTEGER = /^(?:0|-?[1-9][0-9]*)$/;

type JsonRecord = Record<string, unknown>;

const parsedArtifacts = new WeakSet<MechanismArtifact>();

/** Compilers accept only owned, validated snapshots from this package instance. */
export function assertParsedArtifact(value: MechanismArtifact): void {
  if (!parsedArtifacts.has(value)) throw new ArtifactInputError("Parse the original artifact before compiling playback.");
}

export function parseArtifactText(text: string): MechanismArtifact {
  if (typeof text !== "string") throw new ArtifactInputError("Artifact text must be a string.");
  const byteLength = utf8ByteLength(text);
  if (byteLength > MAX_ARTIFACT_BYTES) {
    throw new ArtifactInputError(`Artifact exceeds the ${MAX_ARTIFACT_BYTES}-byte browser input limit.`);
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(text) as unknown;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new ArtifactInputError(`Artifact is not valid JSON: ${message}`);
  }
  return parseArtifact(parsed);
}

export function parseArtifact(value: unknown): MechanismArtifact {
  assertBoundedJson(value, MAX_ARTIFACT_BYTES);
  const root = asRecord(value, "$" );
  rejectDiscretePlayback(root);

  const format = readString(root, "format", "$", MAX_ID_LENGTH);
  if (format !== "gear-invest.mechanism") {
    throw new UnsupportedArtifactError(`Unsupported artifact format '${format}'.`);
  }
  const formatVersion = readString(root, "formatVersion", "$", MAX_ID_LENGTH);
  if (formatVersion !== "0.1") {
    throw new UnsupportedArtifactError(`Unsupported mechanism artifact version '${formatVersion}'.`);
  }

  const sourceRecord = readRecord(root, "source", "$" );
  const generatorRecord = readRecord(root, "generator", "$" );
  const kinematicRecord = readRecord(root, "kinematic", "$" );
  const spatialRecord = readRecord(root, "spatial", "$" );
  const playbackRecord = readRecord(root, "resolvedPlayback", "$" );
  const validationRecord = readRecord(root, "validation", "$" );
  rejectDiscretePlayback(playbackRecord);

  const source = {
    specificationId: readId(sourceRecord, "specificationId", "$.source"),
    seed: readString(sourceRecord, "seed", "$.source"),
    generationOptions: readString(sourceRecord, "generationOptions", "$.source"),
  };
  const generator: GeneratorFingerprint = {
    sdkVersion: readString(generatorRecord, "sdkVersion", "$.generator"),
    generatorVersion: readString(generatorRecord, "generatorVersion", "$.generator"),
    backendId: readId(generatorRecord, "backendId", "$.generator"),
    determinismProfile: readId(generatorRecord, "determinismProfile", "$.generator"),
  };

  const dofs = readArray(kinematicRecord, "dofs", "$.kinematic").map((item, index) =>
    parseDof(item, `$.kinematic.dofs[${index}]`),
  );
  const couplings = readArray(kinematicRecord, "couplings", "$.kinematic").map((item, index) =>
    parseCoupling(item, `$.kinematic.couplings[${index}]`),
  );
  const solution = readArray(kinematicRecord, "solution", "$.kinematic").map((item, index) =>
    parseKinematicState(item, `$.kinematic.solution[${index}]`),
  );
  const axes = readArray(spatialRecord, "axes", "$.spatial").map((item, index) =>
    parseAxis(item, `$.spatial.axes[${index}]`),
  );
  const bodies = readArray(spatialRecord, "bodies", "$.spatial").map((item, index) =>
    parseBody(item, `$.spatial.bodies[${index}]`),
  );
  const contacts = readArray(spatialRecord, "contacts", "$.spatial").map((item, index) =>
    parseContact(item, `$.spatial.contacts[${index}]`),
  );
  const drivers = readArray(playbackRecord, "drivers", "$.resolvedPlayback").map((item, index) =>
    parseDriver(item, `$.resolvedPlayback.drivers[${index}]`),
  );
  if (drivers.length !== 1) {
    throw new UnsupportedArtifactError(
      `Static Web playback supports exactly one continuous driver; artifact contains ${drivers.length}.`,
    );
  }
  const channels = readArray(playbackRecord, "channels", "$.resolvedPlayback").map((item, index) =>
    parseChannel(item, `$.resolvedPlayback.channels[${index}]`),
  );
  const bodyBindings = readArray(playbackRecord, "bodyBindings", "$.resolvedPlayback").map((item, index) =>
    parseBodyBinding(item, `$.resolvedPlayback.bodyBindings[${index}]`),
  );
  const diagnostics = readArray(validationRecord, "diagnostics", "$.validation").map((item, index) =>
    parseStoredDiagnostic(item, `$.validation.diagnostics[${index}]`),
  );
  const validationStatus = readString(validationRecord, "status", "$.validation", MAX_ID_LENGTH);
  if (validationStatus !== "valid" && validationStatus !== "invalid") {
    throw new ArtifactInputError(`$.validation.status must be 'valid' or 'invalid'.`);
  }
  const optionalSections = {
    generation: inspectKnownOptionalSection(root, "generation"),
    metrics: inspectKnownOptionalSection(root, "metrics"),
    composition: inspectKnownOptionalSection(root, "composition"),
  };
  const compositionMetadata = parseCompositionMetadata(root, dofs);

  const artifact: MechanismArtifact = {
    format,
    formatVersion,
    candidateId: readId(root, "candidateId", "$"),
    artifactHash: readId(root, "artifactHash", "$"),
    source,
    generator,
    kinematic: {
      rootDofId: readId(kinematicRecord, "rootDofId", "$.kinematic"),
      dofs,
      couplings,
      solution,
    },
    spatial: { axes, bodies, contacts },
    resolvedPlayback: { drivers, channels, bodyBindings },
    validation: { status: validationStatus, diagnostics },
    optionalSections,
    compositionMetadata,
  };

  validateReferences(artifact);
  deepFreeze(artifact);
  parsedArtifacts.add(artifact);
  return artifact;
}

function parseDof(value: unknown, path: string): RotationalDof {
  const record = asRecord(value, path);
  const kind = readString(record, "kind", path, MAX_ID_LENGTH);
  if (kind !== "rotational") {
    throw new UnsupportedArtifactError(`${path}.kind '${kind}' is not supported by the rotational viewer.`);
  }
  return {
    id: readId(record, "id", path),
    kind,
    prescribed: readBoolean(record, "prescribed", path),
  };
}

function parseCoupling(value: unknown, path: string): ExternalGearCoupling {
  const record = asRecord(value, path);
  const kind = readString(record, "kind", path, MAX_ID_LENGTH);
  if (kind !== "externalGear") {
    throw new UnsupportedArtifactError(`${path}.kind '${kind}' is not supported.`);
  }
  return {
    id: readId(record, "id", path),
    kind,
    driverDofId: readId(record, "driverDofId", path),
    drivenDofId: readId(record, "drivenDofId", path),
    driverTeeth: readInteger(record, "driverTeeth", path),
    drivenTeeth: readInteger(record, "drivenTeeth", path),
    phaseOffset: parseFraction(readValue(record, "phaseOffset", path), `${path}.phaseOffset`),
  };
}

function parseKinematicState(value: unknown, path: string): KinematicState {
  const record = asRecord(value, path);
  return {
    dofId: readId(record, "dofId", path),
    coefficient: parseFraction(readValue(record, "coefficient", path), `${path}.coefficient`),
    phaseOffset: parseFraction(readValue(record, "phaseOffset", path), `${path}.phaseOffset`),
  };
}

function parseAxis(value: unknown, path: string): SpatialAxis {
  const record = asRecord(value, path);
  return {
    id: readId(record, "id", path),
    x: readCanonicalInteger(record, "x", path),
    y: readCanonicalInteger(record, "y", path),
  };
}

function parseBody(value: unknown, path: string): SpatialBody {
  const record = asRecord(value, path);
  const kind = readString(record, "kind", path, MAX_ID_LENGTH);
  if (kind !== "gear") {
    throw new UnsupportedArtifactError(`${path}.kind '${kind}' is not supported by the pitch-circle renderer.`);
  }
  return {
    id: readId(record, "id", path),
    kind,
    axisId: readId(record, "axisId", path),
    dofId: readId(record, "dofId", path),
    layer: readInteger(record, "layer", path),
    toothCount: readInteger(record, "toothCount", path),
    pitchRadius: readCanonicalInteger(record, "pitchRadius", path),
    exactMountingPhase: parseFraction(
      readValue(record, "exactMountingPhase", path),
      `${path}.exactMountingPhase`,
    ),
  };
}

function parseContact(value: unknown, path: string): SpatialContact {
  const record = asRecord(value, path);
  const kind = readString(record, "kind", path, MAX_ID_LENGTH);
  if (kind !== "externalGearMesh") {
    throw new UnsupportedArtifactError(`${path}.kind '${kind}' is not supported.`);
  }
  return {
    id: readId(record, "id", path),
    kind,
    constraintId: readId(record, "constraintId", path),
    bodyAId: readId(record, "bodyAId", path),
    bodyBId: readId(record, "bodyBId", path),
  };
}

function parseDriver(value: unknown, path: string): PlaybackDriver {
  const record = asRecord(value, path);
  return { id: readId(record, "id", path), rootDofId: readId(record, "rootDofId", path) };
}

function parseChannel(value: unknown, path: string): PlaybackChannel {
  const record = asRecord(value, path);
  return {
    dofId: readId(record, "dofId", path),
    driverId: readId(record, "driverId", path),
    exactCoefficient: parseFraction(
      readValue(record, "exactCoefficient", path),
      `${path}.exactCoefficient`,
    ),
    exactPhaseOffset: parseFraction(
      readValue(record, "exactPhaseOffset", path),
      `${path}.exactPhaseOffset`,
    ),
  };
}

function parseBodyBinding(value: unknown, path: string): PlaybackBodyBinding {
  const record = asRecord(value, path);
  return {
    bodyId: readId(record, "bodyId", path),
    dofId: readId(record, "dofId", path),
    exactMountingPhase: parseFraction(
      readValue(record, "exactMountingPhase", path),
      `${path}.exactMountingPhase`,
    ),
  };
}

function parseStoredDiagnostic(value: unknown, path: string): StoredDiagnostic {
  const record = asRecord(value, path);
  const diagnostic: StoredDiagnostic = {
    code: readId(record, "code", path),
    severity: readId(record, "severity", path),
    message: readString(record, "message", path),
  };
  if (Object.hasOwn(record, "subjectId")) {
    return { ...diagnostic, subjectId: readId(record, "subjectId", path) };
  }
  return diagnostic;
}

export function parseFraction(value: unknown, path: string): ExactFraction {
  const record = asRecord(value, path);
  const numerator = readCanonicalInteger(record, "numerator", path);
  const denominator = readCanonicalInteger(record, "denominator", path);
  const numeratorValue = BigInt(numerator);
  const denominatorValue = BigInt(denominator);
  if (denominatorValue === 0n) {
    throw new ArtifactInputError(`${path}.denominator must not be zero.`);
  }
  if (denominatorValue < 0n) {
    throw new ArtifactInputError(`${path} must use a positive canonical denominator.`);
  }
  if (greatestCommonDivisor(absBigInt(numeratorValue), denominatorValue) !== 1n) {
    throw new ArtifactInputError(`${path} is not in canonical reduced form.`);
  }
  return { numerator, denominator };
}

function validateReferences(artifact: MechanismArtifact): void {
  const diagnostics: string[] = [];
  const dofIds = uniqueIds(artifact.kinematic.dofs, (item) => item.id, "kinematic DOF", diagnostics);
  const couplingIds = uniqueIds(
    artifact.kinematic.couplings,
    (item) => item.id,
    "kinematic coupling",
    diagnostics,
  );
  const axisIds = uniqueIds(artifact.spatial.axes, (item) => item.id, "spatial axis", diagnostics);
  const bodyIds = uniqueIds(artifact.spatial.bodies, (item) => item.id, "spatial body", diagnostics);
  uniqueIds(artifact.spatial.contacts, (item) => item.id, "spatial contact", diagnostics);
  const driverIds = uniqueIds(
    artifact.resolvedPlayback.drivers,
    (item) => item.id,
    "playback driver",
    diagnostics,
  );
  const solutionDofIds = uniqueIds(
    artifact.kinematic.solution,
    (item) => item.dofId,
    "kinematic solution DOF",
    diagnostics,
  );
  const channelDofIds = uniqueIds(
    artifact.resolvedPlayback.channels,
    (item) => item.dofId,
    "playback channel DOF",
    diagnostics,
  );
  const bindingBodyIds = uniqueIds(
    artifact.resolvedPlayback.bodyBindings,
    (item) => item.bodyId,
    "playback body binding",
    diagnostics,
  );

  requireReference(dofIds, artifact.kinematic.rootDofId, "kinematic rootDofId", diagnostics);
  for (const coupling of artifact.kinematic.couplings) {
    requireReference(dofIds, coupling.driverDofId, `coupling '${coupling.id}' driverDofId`, diagnostics);
    requireReference(dofIds, coupling.drivenDofId, `coupling '${coupling.id}' drivenDofId`, diagnostics);
  }
  for (const state of artifact.kinematic.solution) {
    requireReference(dofIds, state.dofId, "kinematic solution dofId", diagnostics);
  }
  for (const body of artifact.spatial.bodies) {
    requireReference(axisIds, body.axisId, `body '${body.id}' axisId`, diagnostics);
    requireReference(dofIds, body.dofId, `body '${body.id}' dofId`, diagnostics);
  }
  for (const contact of artifact.spatial.contacts) {
    requireReference(couplingIds, contact.constraintId, `contact '${contact.id}' constraintId`, diagnostics);
    requireReference(bodyIds, contact.bodyAId, `contact '${contact.id}' bodyAId`, diagnostics);
    requireReference(bodyIds, contact.bodyBId, `contact '${contact.id}' bodyBId`, diagnostics);
  }
  for (const driver of artifact.resolvedPlayback.drivers) {
    requireReference(dofIds, driver.rootDofId, `driver '${driver.id}' rootDofId`, diagnostics);
    if (driver.rootDofId !== artifact.kinematic.rootDofId) diagnostics.push("Playback driver and kinematic root bindings disagree.");
  }
  for (const channel of artifact.resolvedPlayback.channels) {
    requireReference(dofIds, channel.dofId, "playback channel dofId", diagnostics);
    requireReference(driverIds, channel.driverId, `channel '${channel.dofId}' driverId`, diagnostics);
  }
  const bodiesById = new Map(artifact.spatial.bodies.map((body) => [body.id, body]));
  for (const binding of artifact.resolvedPlayback.bodyBindings) {
    requireReference(bodyIds, binding.bodyId, "playback binding bodyId", diagnostics);
    requireReference(dofIds, binding.dofId, `binding '${binding.bodyId}' dofId`, diagnostics);
    const body = bodiesById.get(binding.bodyId);
    if (body && body.dofId !== binding.dofId) {
      diagnostics.push(
        `Playback binding '${binding.bodyId}' uses DOF '${binding.dofId}', but the body references '${body.dofId}'.`,
      );
    }
    if (body && (body.exactMountingPhase.numerator !== binding.exactMountingPhase.numerator ||
        body.exactMountingPhase.denominator !== binding.exactMountingPhase.denominator)) {
      diagnostics.push(`Playback binding '${binding.bodyId}' mounting phase disagrees with its body.`);
    }
  }

  requireCompleteSet(dofIds, solutionDofIds, "kinematic solution", diagnostics);
  requireCompleteSet(dofIds, channelDofIds, "playback channels", diagnostics);
  requireCompleteSet(bodyIds, bindingBodyIds, "playback body bindings", diagnostics);

  if (diagnostics.length > 0) {
    throw new ArtifactInputError("Artifact reference check failed.", diagnostics);
  }
}

function readValue(record: JsonRecord, key: string, path: string): unknown {
  if (!Object.hasOwn(record, key)) {
    throw new ArtifactInputError(`${path}.${key} is required.`);
  }
  return record[key];
}

function readRecord(record: JsonRecord, key: string, path: string): JsonRecord {
  return asRecord(readValue(record, key, path), `${path}.${key}`);
}

function readArray(record: JsonRecord, key: string, path: string): readonly unknown[] {
  const value = readValue(record, key, path);
  if (!Array.isArray(value)) {
    throw new ArtifactInputError(`${path}.${key} must be an array.`);
  }
  if (value.length > MAX_COLLECTION_ITEMS) {
    throw new ArtifactInputError(`${path}.${key} exceeds the ${MAX_COLLECTION_ITEMS}-item limit.`);
  }
  return value;
}

function readString(record: JsonRecord, key: string, path: string, maxLength = MAX_STRING_LENGTH): string {
  const value = readValue(record, key, path);
  if (typeof value !== "string" || value.length === 0 || value.length > maxLength) {
    throw new ArtifactInputError(`${path}.${key} must be a non-empty string up to ${maxLength} characters.`);
  }
  return value;
}

function readId(record: JsonRecord, key: string, path: string): string {
  const value = readString(record, key, path, MAX_ID_LENGTH);
  if (value.trim() !== value) {
    throw new ArtifactInputError(`${path}.${key} must not have surrounding whitespace.`);
  }
  return value;
}

function readBoolean(record: JsonRecord, key: string, path: string): boolean {
  const value = readValue(record, key, path);
  if (typeof value !== "boolean") {
    throw new ArtifactInputError(`${path}.${key} must be a boolean.`);
  }
  return value;
}

function readInteger(record: JsonRecord, key: string, path: string): number {
  const value = readValue(record, key, path);
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw new ArtifactInputError(`${path}.${key} must be a safe integer.`);
  }
  return value;
}

function readCanonicalInteger(record: JsonRecord, key: string, path: string): string {
  const value = readString(record, key, path, MAX_INTEGER_DIGITS);
  if (!CANONICAL_INTEGER.test(value)) {
    throw new ArtifactInputError(`${path}.${key} must be a canonical decimal integer string.`);
  }
  return value;
}

function asRecord(value: unknown, path: string): JsonRecord {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new ArtifactInputError(`${path} must be an object.`);
  }
  return value as JsonRecord;
}

function inspectKnownOptionalSection(record: JsonRecord, key: "generation" | "metrics" | "composition") {
  if (!Object.hasOwn(record, key)) {
    return { presence: "absent" as const, acceptedKnownOptional: false, mechanicallyUsed: false as const };
  }

  const value = asRecord(record[key], `$.${key}`);
  const budget = { nodes: 0 };
  validateBoundedOptionalValue(value, `$.${key}`, 0, budget);
  return { presence: "present" as const, acceptedKnownOptional: true, mechanicallyUsed: false as const };
}

function parseCompositionMetadata(
  root: JsonRecord,
  dofs: readonly RotationalDof[],
): CompositionMetadataObservation {
  if (!Object.hasOwn(root, "composition")) {
    return {
      presence: "absent",
      acceptedKnownOptional: false,
      mechanicallyUsed: false,
      candidateIdentityInput: false,
      playbackSource: false,
      referencesValid: true,
      semanticBindings: [],
    };
  }

  const composition = asRecord(root.composition, "$.composition");
  const semanticBindings = readArray(composition, "semanticBindings", "$.composition").map((value, index) => {
    const path = `$.composition.semanticBindings[${index}]`;
    const record = asRecord(value, path);
    const role = readString(record, "role", path, MAX_ID_LENGTH);
    if (
      role !== "root" && role !== "intermediateOutput" && role !== "finalOutput" &&
      role !== "sharedBranchOutput" && role !== "leafOutput"
    ) {
      throw new ArtifactInputError(`${path}.role '${role}' is not a known continuous semantic binding role.`);
    }
    const typedRole:
      "root" | "intermediateOutput" | "finalOutput" | "sharedBranchOutput" | "leafOutput" = role;
    return {
      semanticNodeId: readId(record, "semanticNodeId", path),
      dofId: readId(record, "dofId", path),
      role: typedRole,
    };
  });
  const diagnostics: string[] = [];
  const dofIds = new Set(dofs.map((dof) => dof.id));
  uniqueIds(semanticBindings, (binding) => binding.semanticNodeId, "composition semantic node", diagnostics);
  for (const binding of semanticBindings) {
    requireReference(
      dofIds,
      binding.dofId,
      `composition semantic binding '${binding.semanticNodeId}' dofId`,
      diagnostics,
    );
  }
  if (diagnostics.length > 0) {
    throw new ArtifactInputError("Composition metadata reference check failed.", diagnostics);
  }
  let topologyObservation: {
    topologyKind?: "threeEdgeSingleSharedNodeBranch";
    branchSelectionCount?: number;
    sourceEdgeReferencesValid?: boolean;
  } = {};
  if (Object.hasOwn(composition, "topologyKind")) {
    const topologyKind = readString(composition, "topologyKind", "$.composition", MAX_ID_LENGTH);
    if (topologyKind !== "threeEdgeSingleSharedNodeBranch") {
      throw new ArtifactInputError(`$.composition.topologyKind '${topologyKind}' is not a supported bounded topology.`);
    }
    const inputEdges = readArray(composition, "inputEdges", "$.composition");
    const inputRequirementIds = new Set(inputEdges.map((value, index) =>
      readId(asRecord(value, `$.composition.inputEdges[${index}]`), "compiledRequirementId",
        `$.composition.inputEdges[${index}]`)));
    const selection = asRecord(composition.selection, "$.composition.selection");
    const branches = readArray(selection, "branches", "$.composition.selection");
    if (branches.length !== 2) {
      throw new ArtifactInputError("$.composition.selection.branches must contain exactly two bounded branches.");
    }
    const branchRequirementIds = branches.map((value, index) =>
      readId(asRecord(value, `$.composition.selection.branches[${index}]`), "compiledRequirementId",
        `$.composition.selection.branches[${index}]`));
    const sourceEdgeReferencesValid = branchRequirementIds.every((id) => inputRequirementIds.has(id));
    if (!sourceEdgeReferencesValid) {
      throw new ArtifactInputError("Branched composition selection references an unknown input edge.");
    }
    topologyObservation = {
      topologyKind,
      branchSelectionCount: branches.length,
      sourceEdgeReferencesValid,
    };
  }
  return {
    presence: "present",
    acceptedKnownOptional: true,
    mechanicallyUsed: false,
    candidateIdentityInput: false,
    playbackSource: false,
    referencesValid: true,
    ...topologyObservation,
    semanticBindings,
  };
}

function validateBoundedOptionalValue(
  value: unknown,
  path: string,
  depth: number,
  budget: { nodes: number },
): void {
  budget.nodes += 1;
  if (budget.nodes > MAX_OPTIONAL_NODES) {
    throw new ArtifactInputError(`${path} exceeds the ${MAX_OPTIONAL_NODES}-node optional-section limit.`);
  }
  if (depth > MAX_OPTIONAL_DEPTH) {
    throw new ArtifactInputError(`${path} exceeds the ${MAX_OPTIONAL_DEPTH}-level optional-section depth limit.`);
  }
  if (value === null || typeof value === "boolean") return;
  if (typeof value === "string") {
    if (value.length > MAX_STRING_LENGTH) {
      throw new ArtifactInputError(`${path} exceeds the ${MAX_STRING_LENGTH}-character string limit.`);
    }
    return;
  }
  if (typeof value === "number") {
    if (!Number.isSafeInteger(value)) {
      throw new ArtifactInputError(`${path} must be a safe integer in known optional metadata.`);
    }
    return;
  }
  if (Array.isArray(value)) {
    if (value.length > MAX_COLLECTION_ITEMS) {
      throw new ArtifactInputError(`${path} exceeds the ${MAX_COLLECTION_ITEMS}-item limit.`);
    }
    value.forEach((item, index) => validateBoundedOptionalValue(item, `${path}[${index}]`, depth + 1, budget));
    return;
  }
  if (typeof value === "object") {
    const entries = Object.entries(value as JsonRecord);
    if (entries.length > MAX_OPTIONAL_PROPERTIES) {
      throw new ArtifactInputError(`${path} exceeds the ${MAX_OPTIONAL_PROPERTIES}-property limit.`);
    }
    for (const [name, item] of entries) {
      if (name.length === 0 || name.length > MAX_ID_LENGTH) {
        throw new ArtifactInputError(`${path} contains an invalid optional metadata property name.`);
      }
      validateBoundedOptionalValue(item, `${path}.${name}`, depth + 1, budget);
    }
    return;
  }
  throw new ArtifactInputError(`${path} contains an unsupported optional metadata value.`);
}

function rejectDiscretePlayback(record: JsonRecord): void {
  const unsupportedKeys = ["discretePlayback", "events", "states", "transitions", "program"];
  const found = unsupportedKeys.find((key) => Object.hasOwn(record, key));
  if (found) {
    throw new UnsupportedArtifactError(`Discrete playback section '${found}' is not supported by this viewer.`);
  }
}

function uniqueIds<T>(
  items: readonly T[],
  select: (item: T) => string,
  label: string,
  diagnostics: string[],
): Set<string> {
  const result = new Set<string>();
  for (const item of items) {
    const id = select(item);
    if (result.has(id)) {
      diagnostics.push(`Duplicate ${label} ID '${id}'.`);
    }
    result.add(id);
  }
  return result;
}

function requireReference(ids: ReadonlySet<string>, id: string, label: string, diagnostics: string[]): void {
  if (!ids.has(id)) {
    diagnostics.push(`${label} references unknown ID '${id}'.`);
  }
}

function requireCompleteSet(
  expected: ReadonlySet<string>,
  actual: ReadonlySet<string>,
  label: string,
  diagnostics: string[],
): void {
  for (const id of expected) {
    if (!actual.has(id)) {
      diagnostics.push(`${label} is missing '${id}'.`);
    }
  }
  for (const id of actual) {
    if (!expected.has(id)) {
      diagnostics.push(`${label} contains unknown ID '${id}'.`);
    }
  }
}

function greatestCommonDivisor(left: bigint, right: bigint): bigint {
  let a = left;
  let b = right;
  while (b !== 0n) {
    const remainder = a % b;
    a = b;
    b = remainder;
  }
  return a;
}

function absBigInt(value: bigint): bigint {
  return value < 0n ? -value : value;
}
