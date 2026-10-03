# Browser mechanical runtime and compact checkpoints

역할: **Normative contract — CR-E123-RUNTIME-CHECKPOINT-01의 새 source 경로**.
필수 선행: [finite winding](FINITE_WINDING_CONNECTION_CONTRACT.md)의 source·기하·수치·소유권,
[mechanical modes](MECHANICAL_CONNECTION_MODES_CONTRACT.md)의 구속·이벤트·입력 소유권.
이 문서는 기존 recording/replay의 의미나 한도를 바꾸지 않는다. 사용은
[새 일반 소비 예제](../examples/runtime/README.md). Source release와 binary 배포는 구분한다.

## Runtime choice and responsibility

Production C# Layout/Engine/Serialization/facade를 **.NET 10 WASM**으로 실행한다.
추가 browser 프로젝트는 `JSExport Dispatch(string)`만 제공한다. Worker가 C# host를 소유한다.
JS bridge는 request 전달·세대·수명만, 예제는 입력과 SVG/IndexedDB만 담당한다.
기어비·권취식·mode law·정답 sample을 JS에 복제하지 않는다. 서버 계산 경로도 없다.
여섯 production assembly의 참조 방향은 유지된다. Core/Engine에는 DOM/UI/Worker 의존성이 없다.

설치된 SDK10.0.401의 `Microsoft.NET.Sdk.WebAssembly`에서 interpreter build/publish를 확인했다.
`WasmBuildNative=false`, trim/AOT/threading 없음. 별도 wasm workload 설치는 하지 않았다.
공식 [standalone interop](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0)과
[Worker integration](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-on-webworkers?view=aspnetcore-10.0)을 근거로,
실제 source admission와 새 q 실행 spike 뒤 이 경로 하나를 선택했다.
템플릿 설치나 최신 .NET11 예제를 현재 설치 환경의 실행 증거로 사용하지 않았다.

현재 profile은 `bounded-winding-runtime-checkpoint-v1`, runtime semantic version `1.0`이다.
기하 profile은 그대로 `finite-planar-chord-station-winding-v1`이다. C# source read와 기존 E2
admission도 실제 실행한다. Source admission, 현재 path/active constraints 검사, checkpoint 복원,
미수행 tooth/swept-solid/dynamics, 삭제 이력 검증을 서로 구분한다.
임의 새 기구·3D groove를 이 profile에 넣어 승인할 수 없다.

## Ordinary API and transaction

```text
GearInvestSdk.PrepareMechanicalRuntime(sourceBytes, sessionId, initialPlanet, optionalPolicy)
  → MechanicalRuntimeSession
  → Prepare(request) → immutable candidate, no publication
  → Commit(candidate) → once, only against the same current snapshot
  → Checkpoint() / Snapshot()
GearInvestSdk.RestoreMechanicalRuntime(checkpointBytes) → new session
GearInvestSdk.ImportMechanicalRecording(oldRecordingBytes, sessionId) → explicit migration
GearInvestSdk.SaveMechanicalCheckpoint(checkpoint, path, expectedArtifactId)
```

`Advance` is synchronous Prepare+Commit for ordinary C#. Browser Worker uses the same pair.
Source is prepared once per immutable session, not parsed/rebuilt for every input. Geometry queries
and full segment guards still run. No cache bypasses current q, mode or source validation.
Input is a complete vector; all inherited missing/conflicting input and event-order refusals remain.
One failed segment/event returns no new state; host retains the prior immutable snapshot.
Display failure is separate. A failed UI request clears its new-result display rather than disguising
the last valid snapshot as new success. The explicit last-valid identity remains available.

`RuntimeRequest` binds session, definition, expected StateId, epoch, revision, ID and ordered segments.
Request IDs are bounded authored identifiers, not JavaScript counters. Lifetime revision/event cursor
are canonical decimal **BigInteger strings**, nonnegative, at most128 digits. Rational fields remain
canonical numerator/denominator strings. No Number conversion of exact counters or unwrapped turns.
Per request16 segments/16 events, input/derived exact component bounds and finite q domains remain.
Beyond those capacities return explicit resource failure, never truncate a valid prefix.

## Epoch, active provenance and honest bounds

The SDK starts a new internal epoch after each256 accepted requests. Lifetime revision/cursor,
mechanism identity, physical phase, material IDs, H/L and history digest do not reset. Epoch is
`floor(revision/256)`, not an application-supplied new source or restart trick. Restore compares it.
Local legacy counters are private implementation details; the old4096-revision API is unchanged.

There is **no retained full request/pose history**. Only current frame, H/L, bounded reconstruction
witnesses, a fixed-size history digest and last16 retry entries survive. Exact affine normalization
drops zero coefficients and unused q witnesses. Current source q witnesses are re-evaluated on restore.
Capture witness stores an exact prescribed sun coordinate and finite q; H must reconstruct as s−w.
An unchanged capture retains its cause. Lock witness records its q and affine planet boundary; the
SDK recomputes that same-source frame and both the captured and current world/relative lock relation.

**Active independent provenance remains bounded at64.** This is not a promise of unlimited arbitrary
lock alternation. Alternating world/relative locks at endlessly different q can keep adding genuinely
independent causes. Such a request is rejected as `ProvenanceResourceLimit`, with unchanged state.
It is not solved by merging unrelated causes, storing a history-sized opaque blob, rounding H/L to
exact Rational, or pretending a rehashed aggregate can be regenerated from source. Release followed
by newly prescribed independent motion/capture can legitimately make old causes dead; the SDK then
collects them. Lifetime distinct causes may far exceed64 while current live causes remain bounded.
This limitation and measured growing-lock refusal must accompany any long-run completion claim.
NumericResidualOnly/error-bound-null remains numerical even when its estimated value is small.

## Checkpoint contents and trust

New envelope `gear-invest.mechanical-runtime-checkpoint`, `formatVersion:"1.0"`:
`payloadId` is SHA-256 of `payloadUtf8` bytes, not a signature. Payload includes runtime/profile/schema,
original source artifact bytes/hash, policy, session, revision, epoch, event cursor, history digest,
stale floor, ledger, current full frame/actual owner/material references, H/L/direction, capture/lock
witnesses and live latent-ID→source-q witnesses. One document ≤4 MiB; existing depth40/node131072
and duplicate-key limits apply. Source bytes are repeated once per saved checkpoint, not per advance.

Restore performs source/compiled-law readmission, re-evaluates every retained numerical source,
checks canonical exact coefficients and same-cause IDs, rebuilds current geometry/contact/poses,
checks active coupling/lock, capture witness and lock-boundary relation, mode/direction/knownness,
counter/epoch/retry structure and generated StateId, then compares the stored result to the rebuilt one.
Numeric tolerance is inherited:1e-10 generally,1e-8 pose matrix components; neither is a solution bound.
Source/semantic IDs/exact coefficients are exact; raw numerical bytes can differ across hosts.

Three distinct claims:

| Claim | Guarantee |
|---|---|
| Bytes integrity | Raw envelope/payload SHA; accidental corruption detection, not external authenticity |
| Current state | `current-state-source-revalidated`; actual source geometry and active equations reconstructed |
| Deleted history occurred | `notPerformed`; digest/last16 ledger are continuity metadata, not a full replay proof |

A lock's retained affine boundary is a locally saved boundary condition. Rebuilding it does not
prove every discarded request that led to it. Coordinated edits that form another internally valid
source-compatible state, or rolling back to a valid old checkpoint, cannot be ruled out without an
external trusted authority. No server, signature key, security anti-rollback or historical-path
certification is silently added. The old bounded recording reader still fully replays its history.

## Retry, cancellation, storage and disposal

The last16 accepted request IDs/payload IDs/result IDs/event IDs survive epochs and saves. Same ID/
same payload returns `AlreadyApplied` before stale checks; same ID/different payload conflicts.
Evicted requests with their original expected revision/state are stale. This is bounded idempotency,
not lifetime ID uniqueness/exactly-once. A caller rewriting the expected state creates a new request;
it must not use that as a retry. Cursor differences and ledger result-state linkage are checked.

One browser client owns one Worker and mutable C# host. Each load/restore attempt creates an isolated
generation; only the latest successful requested generation replaces the selected source. A→B→A
completion inversion cannot publish old A or destroy current A through old cleanup. Independent
clients remain isolated. PostMessage owns cloned inputs; returned plain-data snapshots are deep-frozen.
No transferred buffer is aliased to runtime state. Busy requests fail explicitly.

Worker computes a candidate, yields to queued cancellation, then commits once. Cancellation before
commit reports `CancelledBeforeCommit`. After commit it reports `TooLateCommitted`, with actual state
readback. A `CancellationRequested` acknowledgement alone is not rollback. Termination/error/dispose
with an unacknowledged operation is **outcome unknown**, not successful cancellation. Restore the last
saved checkpoint explicitly; unsaved work is not claimed to survive process death. No own game loop.

Browser storage uses one IndexedDB readwrite transaction and expected-artifact-ID CAS. Two tabs with
stale reads cannot overwrite the current saved state. C# storage uses exclusive writer lock, bounded
verified old read, same-directory flushed pending file and atomic replacement. An interrupted pending
file is ignored on load. Failed save leaves the last complete checkpoint. Filesystem/OS power-loss
durability beyond those primitives is not certified. Save frequency is independent of render/advance.
Past exploration is limited to retained checkpoint/recording files; there is no inverse Capture event.

## Compatibility and future geometry

Old winding/mode `0.1` readers and stored samples remain unchanged. Explicit recording import rebuilds
accepted requests into a new runtime session while preserving original artifact bytes and mechanical
frame; runtime session/state identities are intentionally new. Unknown future checkpoint profiles/
major schemas are refused rather than guessed. No bulk regeneration of old consumer artifacts.

R2/R4 preparation is [the missing-input template](../examples/runtime/future-inputs.json) and
[input guide](../examples/runtime/FUTURE_INPUTS.md). Geometry admission/evaluation remains in Layout/
Engine; the Worker only transports opaque versioned source/checkpoint and generic whole-frame data.
Future3D profile selection requires a new validated geometry implementation and codec dispatch, not
flattening 3D input or silently raising the old21-link limit. Current SVG is an optional planar viewer,
not a restriction that Core must acquire a renderer. General3D/whole7047 support remains outside this slice.
