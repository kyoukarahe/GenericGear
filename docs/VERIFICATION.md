# Verification scope

## public03.1 runtime addition

Local Windows verification used .NET SDK 10.0.401, .NET 8 host, Mono browser
runtime 10.0.12 and Node 24.15.0. The selected public suite passed **163 .NET
tests** and **40 existing Node tests**, zero failures/skips. TypeScript, both
SDK targets, static Vite and the untrimmed WASM interpreter builds passed.
The retained Vite large-chunk and unoptimized-WASM notices are not suppressed.

The public source now includes finite winding, actual E2b spur suffix, explicit
mechanical modes and the separate runtime/checkpoint path. New ordinary DLL
consumers generate three winding inputs and a mode recording, reconstruct them
in separate processes and continue the restored mode. The exported readers
passed 225 recorded-frame comparisons and 96 rejection checks. Their mechanical
rebuild claim remains `notPerformed`; recording selection is not runtime execution.

The source-built C#/WASM comparison ran **4,352 new requests**, **17 checkpoints**
and **8,756 response comparisons**, including a fresh native restore. Semantic
identities matched; maximum numeric difference was 4.973799150320701e-14, within
the stated reconstruction policy, not a certified solution bound. Retained retry
history stayed at 16; the observed maximum live provenance was 3. Ten lifecycle
checks used nine real Node workers, including cancellation before commit, late
cancellation, isolation, owned snapshots, worker failure and fresh restoration.

Actual Chrome used the public build through the ordinary `/example/` page:
q=13/200 + Release, q=11/200 with independent sun=2/25 and planet=-1/10 + Capture,
then WorldCarrierLock. Saving and opening the new-page restore link preserved
the same state identity; q=9/200 then continued to revision 4. Carrier=9/55 and
suffix=-93/880 stayed exact under the world lock. Page/Worker fetch sealing
still allowed these new inputs and local storage. A q=1 boundary refusal kept
the last state and cleared all SVGs; restore brought back 3 planes / 50 shapes.
No console errors were observed. The actual UI request logs also matched a fresh
native SDK process. This is a desktop smoke, not a new mobile acceptance claim.

Reproduce the new checks with `scripts/prepare-winding.mjs`,
`tools/e123-implementation/check-replay.mjs` and the [runtime tool guide](../tools/e123-runtime-checkpoint/README.md).
Public packaging does not change any production mechanical equation. The one
new test analyzer adjustment preserves the same `Assert.Single` predicate.
Explicit untrimmed/runtime settings and locked dependencies make this build
profile reproducible; they are not an AOT, trimming or mobile-size optimization.

Clean source-ZIP consumption and hosted Actions are separate release gates;
their actual result, commit and archive hash belong to the GitHub Release.
Private full-history reports, receipts and user data are not published or
required to reproduce this selected acceptance path.

## Existing source-release acceptance and public02.1 observations

The source candidate has a small, reproducible acceptance path rather than the
entire private development history. The root quick start uses only the source
tree and public dependency registries. SDK computational implementation and
web runtime implementation are unchanged by source-release packaging.

The selected .NET suite contains 127 tests: exact rational operations,
compound/idler kinematics, canonical serialization, validation tampering,
playback and coaxial authoring/boundary/persistence refusals. One existing
assertion uses the equivalent `Assert.Single(collection, predicate)` overload
for the newer test-tool analyzer; no expected mechanical value was changed.
The additional 27 carrier and 35 differential cases preserve their original
expectations: actual source admission/rebuild, signed/reference/contact/rank
oracles, corruption rejection, partial/inconsistent boundaries and exact/display
separation. The underlying C#/TypeScript implementation was not rewritten for
the public release.

The original JavaScript acceptance has 18 tests / 754 assertions through public package
exports. It exercises actual SDK-generated affine ratios including 1/960,
nonzero phase, negative/large roots, source integrity, independent GLB point
oracles, presentation-equivalence mutants, async disposal and View restoration.
Its source-workspace export-resolution assertion intentionally differs from
an installed-tarball check. It is not a claim of published npm installation.

An additional 22 Node tests exercise `/carrier`, `/differential` and the actual
example session controller. They cover independent input oracles, alternative
bases, real prefix, static hold, signed nonzero references, large exact input,
display failure, structural mutations, byte ownership, instance isolation and
stale asynchronous work. No new test-runner dependency is required.

`node scripts/prepare-moving.mjs` builds the two ordinary consumers against
the six DLLs produced from this source tree. It creates two carrier and seven
differential examples and reopens each in a separate process. Generated bytes
are compared with the shipped examples only after generation; they are never
substituted for authoring/rebuild. The Node tests compare 31 new input snapshots
(10 carrier, 21 differential), exact coordinates and all returned world matrices
against those fresh C# observations. Display-unavailable has no matrices.

The ordinary console generates three distinct inputs, finalizes and reconstructs
their source artifacts and assembly replay. A separate C# producer authors
additional exact channels through the same SDK. No stored expected artifact
is substituted for those operations.

For `0.1.0-rc.public02.1`, local Windows source builds passed 127 .NET and 40 Node
tests with no failures or skips. Both .NET target frameworks and the TypeScript
packages built successfully. The existing Vite example built with its retained
large-chunk advisory; that is not a new performance guarantee.

Actual Chrome verification used the public source build: E2a `1/4` gave world
`11/4` and relative `5/2`; E2b `carrier=-1/4, sun=-1/5` gave world `-3/4` and
relative `-1/2`, including parent/pivot controls. The actual prefix displayed
five bodies, invalid input cleared the current pose, and no console errors were
observed. This release smoke is not a repeat of every earlier browser campaign:
OS file dialogs/download completion, session reopen and continuous play were
not re-exercised through the UI. Headless session tests are separately described above.

Release acceptance also uses a separate source-ZIP extraction with no prior
generated files or SDK binaries, then executes the root quick start using only
public dependency registries (download caches may be reused). Consult the
GitHub Release for the exact ZIP SHA-256, commit and actual acceptance result;
hosted Actions is a separate run, not inferred from local commands.
Linux/macOS, real mobile/Safari, fresh Unity integration, installed binary
packages, physical mechanics and legal compatibility are not certified.
Source changes or a different toolchain require appropriate new verification.

The published-file inventory is `PUBLIC_FILES.json` at the repository root.
It excludes its own hash to avoid a self-referential digest. Private audit
logs and authorizations are not required to build or interpret this source.
