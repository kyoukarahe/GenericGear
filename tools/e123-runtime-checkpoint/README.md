# Runtime acceptance tools

Run the [ordinary consumer](../../examples/runtime/README.md) first. These tools
verify that same SDK/Worker path; they are not the product execution engine.
Use .NET SDK 10.0.401, .NET 8, Node 24.15.0 and PowerShell 7. No AOT workload,
private feed, private binary, historical fixture or computation server is needed.

```powershell
pwsh -NoProfile -File tools/e123-runtime-checkpoint/Build-Web.ps1
node tools/e123-runtime-checkpoint/check-runtime.mjs artifacts/e123-runtime-checkpoint/web/publish/wwwroot examples/runtime/dotnet/bin/Release/net8.0/Runtime.Consumer.dll artifacts/e123-runtime-checkpoint/web/publish/wwwroot/example/source.json generated/runtime-cross-host 4352
node tools/e123-runtime-checkpoint/check-lifecycle.mjs artifacts/e123-runtime-checkpoint/web/publish/wwwroot artifacts/e123-runtime-checkpoint/web/publish/wwwroot/example/source.json generated/runtime-lifecycle.json
```

The first check compares new C#/WASM requests and restores checkpoints in fresh
native processes. The second uses real Node workers to check isolation, cancellation,
error/disposal and superseded loads. Neither proves real browser/mobile acceptance.
Outputs are create-only. Reuse valid build/dependency caches; select a new output
path for deliberate repeated checks rather than removing previous receipts.

The publish script explicitly disables workload resolution for the prebuilt
interpreter profile; an installed AOT workload must not change its build path.
The browser project disables implicit SDK/workload package sources and keeps
NuGet locked-mode validation enabled. Some Visual Studio workload packages differ
from the same-version NuGet.org package (see [upstream issue](https://github.com/dotnet/sdk/issues/51675)).
CI uses a job-local package cache. If an existing local cache reports `NU1403`,
inspect the package/source first; do not regenerate the lock file or disable its
validation just to accept that cache. A new `NUGET_PACKAGES` directory can restore
the locked NuGet.org packages without deleting the shared cache. Reuse that new
directory for subsequent builds.

`serve.mjs <static-root> <port>` serves only GET/HEAD resources on loopback.
`/example/` is the ordinary UI. `/verification/` is optional browser acceptance
tooling, not a prerequisite for loading, executing or restoring a mechanism.
Read [verification scope](../../docs/VERIFICATION.md) for actual observed platforms.

For spatial winding, pass `-Spatial -OutputDirectory artifacts/spatial/web` to
the build script and use that output's `publish/wwwroot` and `example/source.json`
in the same commands. Keep planar and spatial sources separate: source authoring
is create-only, while current compiled SDK/Worker resources are incrementally
rebuilt. The new [profile](../../docs/SPATIAL_WINDING_COMPOSITION_CONTRACT.md) has
sampled path checks and underdetermined link roll, not continuous certification.

For an explicitly selected real-reel boundary use `-SelectedDrive -OutputDirectory
artifacts/selected-drive/web`, then point both checks at that output's
`publish/wwwroot` and `example/source.json`. The ordinary multi-turn check is
`dotnet examples/runtime/dotnet/bin/Release/net8.0/Runtime.Consumer.dll inspect-spatial artifacts/selected-drive/web/publish/wwwroot/example/source.json -1 23/20`.
Do not reuse the legacy example's lower branch bound as a validity claim.
`check-drive-compatibility.mjs <old-public04-consumer.dll> <new-consumer.dll>
<old-source.json> <selected-source.json> <new-receipt.json>` runs the actual two
readers; obtain the old DLL by building the unchanged public04 source separately.
It does not rewrite old source/checkpoint bytes. Real-device work follows the
[selected-drive pack](../../docs/DRV_DEVICE_ACCEPTANCE.md).
