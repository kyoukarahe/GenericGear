# Bounded carrier — ordinary SDK and Web consumer

Role: Usage guide for the **public source-only E2a** path. This does not
claim an installed NuGet/npm/UPM package. Read the
[profile contract](../../docs/BOUNDED_CARRIER_CONTRACT.md) before authoring.
The [verification scope](../../docs/VERIFICATION.md) separates source consumption
from package/platform acceptance.

## Create and reopen with DLLs

From the public repository root, with the toolchain in the root README:

```powershell
dotnet build src/GearInvest/GearInvest.csproj -c Release -p:RestoreLockedMode=true
dotnet build examples/carrier/dotnet/Carrier.Consumer.csproj -c Release -p:RestoreLockedMode=true
dotnet examples/carrier/dotnet/bin/Release/net8.0/Carrier.Consumer.dll create generated/my-first-carrier 100 10
dotnet examples/carrier/dotnet/bin/Release/net8.0/Carrier.Consumer.dll reopen generated/my-first-carrier
```

Choose a new output directory for `create`; an existing directory is refused,
not cleaned or overwritten. `reopen` runs in another process, reads the source
draft, freshly finalizes it and compares the complete artifact/replay bytes.
It also evaluates new exact inputs; saved q/p and PASS flags are not authority.
The example references six built DLLs, not a test assembly or ProjectReference.
To use another verified build, pass `-p:GearInvestSdkBin=<absolute DLL directory>`
when building the consumer. Copy all six matching DLLs together; do not mix builds.

For a real upstream 20:40 external spur stage and a non-default moving pair:

```powershell
dotnet examples/carrier/dotnet/bin/Release/net8.0/Carrier.Consumer.dll create generated/my-prefix-carrier 42 18 prefix
dotnet examples/carrier/dotnet/bin/Release/net8.0/Carrier.Consumer.dll reopen generated/my-prefix-carrier
```

The prefix has one actual input, four shaft coordinates and five bodies. Its
carrier is the existing output shaft: it is not a second prescribed input.
Prefix gears stay on their own plane; the moving pair is 20 mm above it.

## Author your definition

[CarrierExample.cs](CarrierExample.cs) is editable application code showing
`CarrierDefinition`, not a generator hidden in the SDK. It exposes different
teeth, prefix, signed planet axis, explicit nonzero reference phases and rigid
relocation. Production accepts the immutable definition through
`GearInvestSdk.TryFinalizeCarrier`; invalid mechanics returns diagnostics and
no normal artifact. Unsupported required checks also prevent finalization.

The main authoring inputs are the complete mechanical source, explicit
source-to-mm mapping, actual carrier shaft ID, reference plane, held sun shaft,
carrier-local planet shaft, teeth/module, references/mounts, integer tooth
registration and an output port. Readout calibration does not rotate a body.
Frames are proper cardinal and axes parallel; this is not arbitrary 3D motion.

The public flow is:

```csharp
var sdk = GearInvestSdk.CreateDefault();
var request = GearInvest.CarrierExample.Example.Create(100, 10);
var result = sdk.TryFinalizeCarrier(request);
if (!result.IsFinalized) throw new InvalidOperationException(result.Status.ToString());
var exact = sdk.EvaluateCarrier(result.Analysis, ExactQuantity.Turns(new Rational(1, 4)));
var display = sdk.DisplayCarrier(result.Analysis, ExactQuantity.Turns(new Rational(1, 4)));
var bytes = sdk.ExportCarrierReplay(result.Artifact!);
```

Import namespaces `GearInvest` and `GearInvest.Core`; include the ordinary example
source only for its `Example.Create` helper, or construct `CarrierDefinition`
yourself. `EvaluateCarrier` returns unwrapped rational coordinates. Display is
optional, approximate and may be unavailable while exact evaluation succeeds.

## Open the same result in a static browser

Build the public workspace dependencies incrementally (run `npm ci --ignore-scripts`
at the repository root if dependencies have not been installed), then serve the root:

```powershell
npm run build
npm run labs
```

Open `/examples/carrier/web/index.html` on the loopback address printed by Vite.
The checked-in samples are real ordinary SDK exports, not frozen conformance
fixtures. Use the two load buttons, or select your new
`mechanism.carrier-replay.json` with **내 replay 열기**. The collapsible JSON field
is another load path. Do not pass a draft or mechanism to the replay reader.
Digest verification uses Web Crypto on localhost/HTTPS. No remote service,
WebAssembly or browser-side tooth solver is used.

Enter an integer or exact fraction, choose **적용 / Seek**, then play/stop or
select a body. Negative and accumulated inputs remain unwrapped. The input
limit is 128 integer characters; the host reduces entered fractions before
calling the strict SDK. The host clock supplies absolute input; it does not
increment the mechanical state or modulo-reduce the saved turns.

In a consuming app use `@gearinvest/replay/carrier` (the local built workspace
entry for now), `readCarrierReplay(bytes)`, `verifyIntegrity(sha256)`,
`createInstance(id)` and `instance.evaluate({numerator:"1",denominator:"4"})`.
Dispose superseded instances/replays. A digest PASS is not current mechanical
validation: source rebuild is a C# responsibility and remains `notPerformed`
in the browser integrity result.

The view owns SVG, selection, timing and asset correction. Its circles/ticks
illustrate pitch geometry, not generated tooth solids. **변환된 renderer parent**
and **off-center pivot 보정** exercise
`inverse(parentWorld) * bodyWorld * assetLocalCorrection`. The SDK body pose
already includes carrier motion exactly once. Use world/relative scalars for
readback, not a second rotation on the returned matrix.

## Limits and failures

- One grounded sun, one orbiting planet and one input; at most one fixed-axis
  prefix stage. No ring gear, free differential, nested carrier or downstream
  Worm/OpenBelt connection through a fixed-frame API.
- Pitch contact and separate prefix planes are checked. Tooth/swept solids,
  bearings, arm clearance, force and dynamics are not certified.
- Malformed/resource input throws; validly encoded but inadmissible source
  remains a draft. Failed Web loads preserve the previous adopted mechanism.
- The sample accepts only an initially displayable replay. Later display
  unavailability is reported separately from its exact evaluated state.
- Unity/IL2CPP, real mobile/Safari, installed binary packages and consumer-specific
  external assets need separate verification.

The root quick start and [verification scope](../../docs/VERIFICATION.md) cover
this source snapshot. `node scripts/prepare-moving.mjs` regenerates both samples
and reopens them in separate DLL consumer processes; no private evidence is needed.
