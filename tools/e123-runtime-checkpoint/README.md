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

`serve.mjs <static-root> <port>` serves only GET/HEAD resources on loopback.
`/example/` is the ordinary UI. `/verification/` is optional browser acceptance
tooling, not a prerequisite for loading, executing or restoring a mechanism.
Read [verification scope](../../docs/VERIFICATION.md) for actual observed platforms.
