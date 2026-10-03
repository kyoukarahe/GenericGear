# GenericGear

An engine-independent, bounded **ideal-kinematics** SDK. The C# core authors,
analyzes, finalizes and reconstructs mechanical artifacts. The TypeScript
packages consume those artifacts; they do not synthesize or validate a new
mechanism. The separate browser runtime executes the shared C# implementation
locally for the bounded winding/mode profile. Unity and browser display objects
never own the mechanical truth.

This is a **source release**, version `0.1.0-rc.public03.1`.
Build and use it from this source tree. No public NuGet/npm package, binary
download or Unity package is claimed here.

## Quick start from this source snapshot

Use .NET SDK **10.0.401**, a .NET 8 runtime, Node **24.15.0** and npm **11.12.1**.
Dependencies are restored only from nuget.org / registry.npmjs.org. Existing
global download caches may be reused. Do not use a private feed or older SDK DLL.
Run these commands at the repository root, starting with no `generated` folder:

```sh
dotnet build src/GearInvest/GearInvest.csproj -c Release -p:RestoreLockedMode=true
dotnet test tests/GearInvest.Tests/GearInvest.Tests.csproj -c Release -p:RestoreLockedMode=true
dotnet run --project examples/console/Consumer.csproj -c Release -p:RestoreLockedMode=true -- generated/mechanisms
dotnet run --project examples/exact-producer/Guidance.csproj -c Release -p:RestoreLockedMode=true -- generated/mechanisms/coaxial.artifact.json generated/exact
node scripts/prepare-web.mjs
node scripts/prepare-moving.mjs
npm ci --ignore-scripts
npm run build
npm run build --workspace @gearinvest/public-example
npm test
npm run dev --workspace @gearinvest/public-example
```

Open the loopback URL printed by Vite. Select a mechanism, enter an exact root
such as `1`, `-1/4` or a large integer fraction, and use **기구 불러오기** / **이동**.
Use **외부 GLB 연결** to attach the generated diagnostic arrow to a chosen body.
No fixture-specific loader or private evidence server is required. The console
uses the ordinary public SDK API and writes new artifacts, not stored answers.
Its output includes `GENERICGEAR_PUBLIC_CONSUMER_PASS`; the second producer
includes `GUIDANCE_PUBLIC_PRODUCER_PASS`. Reusing an output directory is refused:
retain it and use another directory for a new console run. For a second web
preparation, use a fresh checkout instead of overwriting user artifacts.

For a static build: `npm run build --workspace @gearinvest/public-example` from
the repository root. The generated web build is local verification output, not a
published service. Before distributing it, follow [distribution guidance](docs/DISTRIBUTION.md).

## Where to start

- [Browser-local execution and compact checkpoints](examples/runtime/README.md):
  new input/events in a dedicated C# WASM Worker, not stored-frame selection.
  Run `pwsh -NoProfile -File tools/e123-runtime-checkpoint/Build-Web.ps1`, then
  `node tools/e123-runtime-checkpoint/serve.mjs artifacts/e123-runtime-checkpoint/web/publish/wwwroot 5187`
  and open `http://127.0.0.1:5187/example/`. No computation server is used.
- [Finite planar chain, actual differential suffix and recorded modes](examples/winding/README.md)
- [Grounded-sun carrier: one actual input](examples/carrier/README.md)
- [Free-sun differential: two independent inputs](examples/differential/README.md)
- For their interactive SVG labs: `npm run labs`, then open
  `/examples/carrier/web/index.html` or `/examples/differential/web/index.html`
  on the printed loopback address. These pages consume exact exported laws;
  browser mechanical/source rebuild remains `notPerformed`.
- [Consumer contracts and exact replay](docs/CONSUMER.md)
- [Verification scope](docs/VERIFICATION.md)
- [Supported scope and limits](docs/SUPPORTED_SCOPE.md)
- [License scope](LICENSE_SCOPE.md), [commercial inquiries](COMMERCIAL_LICENSE.md)
- [Third-party dependencies](THIRD_PARTY_NOTICES.md)
- [Security reporting](SECURITY.md), [contribution policy](CONTRIBUTING.md)
- [Unity source-only boundary](adapters/unity-source/README.md)

First-party material identified in the scope document uses **AGPL-3.0-only**.
AGPL permits commercial use; paying for a separate agreement is not a condition
of exercising AGPL rights. A separate license may be negotiated only for rights
the licensor can actually grant. Contact: **kyoukarahe@gmail.com**.

There is no warranty or claim of manufacturing safety, dynamics, precision tooth
contact, or global mechanism synthesis. Tests demonstrate bounded contracts,
not physical realizability or legal certification.
