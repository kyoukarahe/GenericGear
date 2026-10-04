# Spatial runtime — real-device execution pack

Role: Development/acceptance procedure for the spatial winding composition
source release. This checklist does not imply a supported or tested mobile release.
Physical Android/iOS acceptance remains **UNVERIFIED** until this flow is executed.

## Selected candidate and hosting

Use only the selected source release with `PUBLIC_FILES.json`, project
license/notices, `examples/runtime` and the shared C# browser build. Record its
manifest SHA256, source artifact ID, resource inventory and actual SDK/browser
versions with the results. Do not use a Private DLL or paste a canned pose.

```powershell
./tools/e123-runtime-checkpoint/Build-Web.ps1 -Spatial -OutputDirectory artifacts/spatial/web
node tools/e123-runtime-checkpoint/serve.mjs artifacts/spatial/web/publish/wwwroot 5188
```

For PC open `http://127.0.0.1:5188/example/`. This helper deliberately binds only
loopback. A real phone must receive the **same static directory** from an approved
HTTPS host; another device's `127.0.0.1` is not this computer. No computation API,
private feed, paid device subscription or mandatory service worker is required.
Do not bypass TLS warnings. A secure context is needed for browser crypto.

Check a root deployment and a prefix such as `/sdk-preview/example/`. Preserve
the relative `_framework`, `runtime-worker.js`, `runtime-client.js`,
`checkpoint-store.js`, example and license directories. Serve `.wasm` with
`application/wasm`, `.js` as JavaScript, and use HTTPS for a remote device.
Subpath testing must load the Worker/framework under that same prefix, not fall
back to cached root resources. No cross-origin isolation/native threads are
required by the selected .NET interpreter profile.

## Required device record

Record each separately; do not sum them with test cases or process counts:

- PC: OS/build, physical machine, browser/version, source/resource IDs.
- Android: physical model, Android version, Chrome version, available memory,
  same selected source/resource IDs. Node/viewport emulation is not this row.
- iPhone/iPad: physical model, iOS/iPadOS version, Safari version, same identity.
  Desktop WebKit or Chrome on Windows is not this row.

Start with normal browsing, a new checkpoint slot and an empty application
history. Do not clear browser-wide data/caches. Report cold first load versus
warm repeat; an HTTP cache and a saved checkpoint are different kinds of reuse.

## Ordinary consumer flow

1. Load `/example/?slot=spatial-device-<unique-name>`. Observe Ready,
   249 links /250 pins, `NumericResidualOnly`, error bound null and free link roll.
   Confirm source ID and frame/owner readouts, not just an image.
2. Seal calculation network with the page control. Enter q=`-107/1000`, planet
   `11/31`, Release. Expect Accepted/Released; sun and planet inputs now required.
3. Enter q=`-91/1000`, sun=`5/17`, planet=`-7/19`, Capture. Expect those input
   values and DriveCapture without changing authored mounts/contact K.
4. Enter q=`-73/1000`, planet=`3/10`, LockWorldCarrier. Then q=`-61/1000`, no
   event. Carrier world coordinate must remain fixed; do not fill missing
   independent ports with zero. A later relative lock holds its own reference.
5. Save checkpoint; observe byte count/artifact ID. Use the new-page restore
   link. Confirm session/state/epoch/revision/cursor, then run q=`-49/1000`.
   This step is new calculation after restoration, not selection of a recording.
6. Try q=`2`. Expect WindingBoundary with previous state unchanged. Test
   cancellation during a request: CancelledBeforeCommit or TooLateCommitted
   with authoritative readback, never claim a rollback after commit.
7. Reload from saved state, then close the page during an outstanding request.
   Treat missing acknowledgement as outcome unknown. Restore the last complete
   checkpoint in a new page and continue. Pagehide/dispose terminates its Worker;
   a late response cannot be published into a new generation.
8. Check input usability and isometric/XY/XZ projections on the actual device.
   Readouts, save and restore must remain reachable; rendering does not solve
   mechanics. A provisional diagnostic link roll is not a unique physical pose.

For missing/extra input, stale/reused requests, load A→B→A, multiple instances,
clone/immutable results and forced Worker failure, run the accompanying SDK
lifecycle checks as **additional** evidence. They do not replace the ordinary UI
or device lifecycle observations. If device tooling cannot exercise a race,
record that subcase unverified rather than changing its expected semantics.

## Long state and storage

Open `/verification/` in that same browser and press the4352-request button.
This separate acceptance consumer uses the same production Worker APIs; the
ordinary product does not depend on it. It performs4352 new event/input requests,
17 checkpoint writes, retry, actual IndexedDB CAS conflict and aborted-write
preservation. Keep the foreground tab alive until the report is complete.
Copy/save the displayed report and final checkpoint. The final link must open
the ordinary example in a new page; continue from revision4352 to4353.

Compare a short same-source run with native .NET using `check-runtime.mjs` and
replay the actual ordinary UI request log with `check-ui-readback.mjs`. Exact
owner/mode/input/cursor/state fields must match; declared numerical tolerances
are1e−10 for numbers and1e−8 for display matrices. Raw checkpoint bytes may differ
between platforms; do not compare only hashes to infer mechanical equivalence.

## Cost and result reporting

Report preparation/initial resource bytes, warm calculation latency, message
and checkpoint size/latency, current retry/live-source count, cancellation and
dispose outcome. The long page reports page-to-Worker round trips and an optional
approximate **page JS heap**. That heap is not Worker/WASM/native/GPU memory.
Record unmeasured categories honestly; do not infer mobile smoothness from PC.
Separate renderer work from SDK calculation. No target FPS or consumer SLA has
been approved for this SDK diagnostic example.

Any hang, unexpected rejection, drifted owner/mode, state mutation on failure,
storage loss or failed restore is FAIL for that flow. Preserve the previous
complete checkpoint and record source identity + minimal request before retry.
The full request remains acceptance-incomplete while a required device or public
delivery gate is missing. A narrower PC/candidate PASS is not overall PASS.
