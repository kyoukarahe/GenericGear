export class ArtifactInputError extends Error {
  readonly diagnostics: readonly string[];
  constructor(message: string, diagnostics: readonly string[] = [message]) {
    super(message); this.name = "ArtifactInputError";
    this.diagnostics = Object.freeze([...diagnostics]);
  }
}
export class UnsupportedArtifactError extends ArtifactInputError {
  constructor(message: string) { super(message); this.name = "UnsupportedArtifactError"; }
}
export class ReplayInputError extends ArtifactInputError {
  constructor(readonly code: string, message: string, readonly subjectId?: string) {
    super(message); this.name = "ReplayInputError";
  }
}
