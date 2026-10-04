# BOUNDARY-01 endpoint-preserving segment checks

Role: focused development/consumer guide. Read the
[selected-drive contract](../../docs/SELECTED_DRIVE_BOUNDARY_CONTRACT.md) and
[runtime contract](../../docs/MECHANICAL_RUNTIME_CONTRACT.md) first.
The code fixes internally generated spatial checkpoints, not external input admission.
No new mechanism/schema, global tolerance, continuous-path proof or release is implied.

## Build and reproduce

Use the existing .NET/NuGet and Web caches. Do not build against outputs while a
consumer or test is using them. Use new receipt paths for each run.

In a Git checkout the verifier records HEAD and actual source hashes. In an
extracted source release it verifies production bytes against `PUBLIC_FILES.json`
and records that manifest's hash instead; it never borrows a parent checkout's HEAD.

```powershell
dotnet test tests/GearInvest.Tests/GearInvest.Tests.csproj -c Release --filter FullyQualifiedName~SpatialSegmentBoundaryTests
dotnet build src/GearInvest/GearInvest.csproj -c Release
dotnet build examples/runtime/dotnet/Runtime.Consumer.csproj -c Release
./tools/e123-runtime-checkpoint/Build-Web.ps1 -SelectedDrive -OutputDirectory artifacts/boundary-01/web
# <source.json> is the original consumer source, not a rewritten generated answer.
node tools/boundary-01/check-runtime.mjs . <source.json> <new-receipt-directory> after artifacts/boundary-01/web/publish/wwwroot
node tools/boundary-01/prepare-browser.mjs . <source.json> artifacts/boundary-01/web/publish/wwwroot <new-resource-manifest.json>
node tools/e123-runtime-checkpoint/serve.mjs artifacts/boundary-01/web/publish/wwwroot 5192
```

The reported source SHA-256 is
`e0715fc4afe469d1e06eafedb94b500749cb85799de9978c8083da29f0920be9`.
Retrieve it with explicit permission from the consumer repository; it is not
bundled into a public SDK package. `check-runtime.mjs` expects the reproduction
contract (initial1/2, domain[-1,1/2], independent planet3/10), not an arbitrary
mechanism's entire domain. Generic legacy/selected authoring variants are tested
separately by `SpatialSegmentBoundaryTests`.

`before` mode explicitly expects the old runtime failure and writes
`CONFIRMED_RUNTIME_BOUNDARY_FAILURE`, never a fixed-capability PASS. Use it only
with a separately built unfixed checkout; do not reset current work to reproduce.
`after` uses the ordinary host's Prepare/Commit/snapshot, checks both directions,
true outside values (including fractions that round onto a binary64 boundary),
atomic out-and-back refusal, malformed input, checkpoint and fresh-process resume.
With a Web root it also invokes the same C# WASM under **Node, not a browser**.
Numeric comparisons retain1e-10 generally and1e-8 for matrix components.

## Actual browser and native readback

Open `/reported/?slot=<fresh-slot>`. This is the unchanged ordinary runtime UI,
with only its `source.json` replaced **in the new staged consumer directory** by
the supplied immutable bytes. Existing sources and evidence are not overwritten.
Keep planet3/10 and no event. Enter4562/11000, then-1, as two consecutive requests
in the same session. Both must be Accepted and the second actual shaft must read
exactly-1/1. Save the checkpoint, download it, then use the actual new-page restore
link. Restore must retain the same state and a new-3/4 request must commit.

Send real outside values without clipping, e.g.
`-100000000000000000001/100000000000000000000` and
`4503599627370497/9007199254740992`; both must refuse without changing state.
Read and preserve the ordinary UI's request log rather than synthesizing UI results.
For the recorded two-success/two-refusal flow and one fresh-page continuation:

```powershell
node tools/boundary-01/check-browser-readback.mjs <Runtime.Consumer.dll> <source.json> <browser-ui.json> <actual-downloaded-checkpoint.json> <new-native-readback.json>
```

The native readback replays the actual UI requests, then restores only the downloaded
boundary checkpoint in a second native process and executes the actual continuation.
It checks exact identities and the same inherited numeric tolerances. No checkpoint
editing or new source/session substitution is allowed to repair a failure.

## Compatibility and delivery

The product change is private/internal to `ConnectedWindingKinematics`. Existing
public APIs, sources, valid state identities and checkpoint formats remain unchanged.
The changed behavior is that a valid endpoint is no longer rejected because the
SDK's last interpolated sample overshoots it. `WindingBoundary`, other geometry/mode
failures, budgets and whole-request atomicity remain in force.

Publish the runtime `_framework`, bridge/client/store and ordinary consumer together;
never mix the fixed DLL with old WASM or an old Worker. Manifest hashes identify the
candidate, not a version-looking folder name. Public05.1 is an immutable unfixed
baseline; a later approved release must use a new version. Private reports, downloaded
consumer source, checkpoints and raw evidence are not public-selection inputs.

PATH-GAP is still OPEN; mobile real-device acceptance is still UNVERIFIED.
