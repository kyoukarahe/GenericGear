# Spatial winding and connected composition — implementation contract

Role: new implementation contract for CR-E123-3D-COMPOSITION-IMPLEMENTATION-01.
Status: selected source profile in `0.1.0-rc.public04.1`; real mobile acceptance remains unverified. The old planar
winding, E2, modes and runtime contracts remain unchanged. Read this together
with [the runtime contract](MECHANICAL_RUNTIME_CONTRACT.md).

## Mechanical boundary

The new bounded model uses **pin-centre guides**, not a cable-length law. Two
single-layer conical/cylindrical helical guides have actual rotating shafts.
Their pin-centre paths are `r(t)=r0+dr*t`, `z(t)=z0+dz*t`,
`azimuth(t)=phase+hand*t+shaftTurns` (turn, mm). A fixed meridian exit and an
explicit finite winding branch determine the helix-to-transfer junction.
Fixed meridian rails and an authored fixed bridge join the two junctions.
These rails are mechanical constraints supplied by the author, not a renderer
curve. An unguided slack/free 3D chain is NOT silently assigned this boundary.

The material chain is finite: N rigid links, N+1 identified pins, fixed Euclidean
pin-centre pitch. Pins follow the declared ordered guide branch; links are the
straight chords between neighbours. The first and last pins are attached to
the two bodies. The last attachment closure determines passive output rotation.
The solver must never use curve arc length as a replacement for these chords.
Contact labels change when a material pin crosses a guide junction. No pin is
deleted, wrapped modulo N, or assigned another material identity.

Spherical joints permit bending and free twist. The source declares a maximum
bend and attachment-direction cone. The returned link axis is constrained;
roll about that axis is **undetermined**, not a unique mechanical frame. Any
display frame is explicitly a presentation convention. Restricted hinges or
finite twist limits require another joint model; they cannot pass this profile.
The spherical-joint DOF interpretation follows the [Simscape joint definition](https://www.mathworks.com/help/sm/ref/sphericaljoint.html).

The first pin is `driver.Point(0,q)`, the last `output.Point(0,w)`. Each end
attachment has a declared tangent-direction cone (`MaxAttachmentBendDegrees`).
There is no separate end-roll constraint. The passive end rotation `w` is solved,
not copied from a drive ratio or inferred to be stationary when input is missing.
The fixed bridge is **guided transfer**, not a tension-selected unguided free
span. Authors of a slack/unconstrained chain must supply a different supported
boundary model; this profile must not be applied as an automatic fallback.

### Source fields and bounds

All fields below are authored inputs, included in the definition identity. No
model name, link count249 or sampled-pose catalog selects a solver implementation.

| Input | Units / bounded admission |
|---|---|
| Actual shafts and guide frames | distinct driver/passive shaft and body IDs; prescribed driver, passive output; guide frame equals its shaft frame; proper cardinal right-handed bases |
| Guide origin | exact mm frame converted for geometry; each numeric component magnitude≤10000mm |
| Guide helix | radius `r0`, radial change `dr` and height change `dz` in mm/turn; hand±1; angular phase and fixed exit azimuth in turns; finite winding-branch integer with magnitude≤32 |
| Guide span | 0.25…16 unwrapped guide turns; endpoint radii≥4×pitch; `pitch≤abs(dz)≤16×pitch`, `abs(dr)≤4×pitch` |
| Driver/output domain | finite binary64 bounds in turns, min<max, each magnitude≤32, width≤12; both endpoint exit parameters inside `[0.125, maximumGuideTurns−0.125]` |
| Material | 8…512 rigid links, N+1 material pins; positive fixed pitch0.01…100mm; chain ID plus stable three-digit pin/link suffixes |
| Transfer bridge | 2…16 finite world points; first/last equal the respective maximum-guide meridian within1e−8mm; consecutive segment length≥pitch and≤10000mm |
| Spherical joints | bend limit `(0,150]` degrees; attachment-direction cone `(0,90]` degrees; free twist, no invented hinge axis/roll restriction |
| Initial state | exact rational driver turns inside the declared binary64 domain; complete declared differential independent input at runtime |

Bounds are resource/profile limits, not manufacturing recommendations. They do
not imply every admitted domain has a solution at every point. Binary64 domain
boundaries are compared to exact inputs as the **exact binary64 value**, not a
rounded rational input: e.g. exact6/5 exceeds a binary64 upper bound1.2 slightly.
Use an interior exact input or an exactly representable authored bound when the
endpoint itself is required. No silent clamp is applied.

## Numerical and admission boundary

Input turns remain unwrapped. Declared finite bounds, helix branch, contact
ordering, pitch, attachments and joint limits are checked. Geometry uses
binary64. Bracket subdivision/root refinement checks 3D chord closure; exact
affine downstream laws retain named correlated numerical sources. A small
geometric residual is not an angular solution-error certificate.

Source admission, one evaluated pose, sampled/refined path checks, and a proof
over every point of a continuous path are distinct. Neither a source digest
nor endpoint agreement certifies a whole path. Resource exhaustion and a
numerically unresolved/ambiguous branch are not `NoSolution`.

### Numerical algorithm and quality

The path is ordered: driver helix → driver fixed meridian rail → authored bridge
segments → passive fixed meridian rail → reversed passive helix. A next pin is
the first forward guide/sphere intersection at the declared Euclidean pitch from
the previous pin. Straight pieces use a quadratic intersection. Helix pieces use
bounded subdivision, analytic speed/acceleration guards and safeguarded
Newton/bisection. Passive attachment closure scans32 bins of the declared output
branch and refines a detected root with at most48 bisections. Multiple detected
brackets or a locally inconsistent trend return an unresolved/ambiguous result.

These are ordinary binary64 numerical guards, **not directed-rounding interval
certificates**. A single detected bracket is not a proof that no unsampled root
exists anywhere in an arbitrary authored branch. A declared branch should be
narrow enough to identify the intended continuous motion; broad or near-tangent
inputs may be refused. No angular solution-error bound is supplied.

For an accepted pose every adjacent pin distance and both endpoint attachments
must have residual≤`1e−8mm`; every interior bend and both attachment-direction
cones are checked. Each pin retains its guide-piece/parameter/contact label.
The returned quality is `NumericResidualOnly`, `solutionErrorBoundTurns:null`,
`linkRoll:Underdetermined`. The length tolerance is this implementation's
numerical acceptance policy, **not an approved consumer SLA** or tooth-solid test.

An advance validates all authored segments, including an out-and-back request's
intermediate endpoints. The spatial path policy additionally evaluates
`ceil(abs(deltaQ)*128)+1` samples per segment (at least2, at most1537), including
both endpoints. Each sampled pose performs the full local refinement above.
This is a **sampled path check, not continuous swept certification**: a very
narrow interior invalid interval can remain undetected. Explicit event guard
evaluation refuses a pin within1e−7mm of a guide junction as `GuardIndeterminate`
when event ordering cannot be established. A user requiring certified continuous
path/solid clearance cannot infer it from this result.

| Resource | Contract |
|---|---|
| Numerical query | ≤8,000,000 charged subdivision/intersection/refinement steps; caller may request a smaller budget |
| One path segment | ≤1536 intervals and40,000,000 summed query steps; resource refusal is atomic |
| Immutable admitted analysis | bounded cache of8 query results keyed by binary64 q; no checkpoint/history list; cached queries retain the original work debit |
| Runtime request | existing≤16 segments and bounded event list; existing prepare/commit semantics |
| State retention | latest16 retry records, live numerical sources≤64, epoch rollover every256 accepted requests; lifetime counters≤128 decimal digits |
| Source/transport | existing4MiB codec/bridge cap and bridge JSON depth40; no unbounded geometry upload |

`NoBracketInDeclaredBranch` reports numerical search coverage, not geometric
impossibility. `AmbiguousBranch`/`NumericalUnresolved`/`GuardIndeterminate`,
`JointLimit`/`WindingBoundary`, invalid input/owner/guide and `ResourceLimit`
remain distinct. A failed request publishes no arrival, partial pose or new
normal state. Display projection failure is separate from mechanical state.

## Integration boundary

The required product chain is a spatial winding source, explicit conditional
coaxial coupling, existing free-sun differential, bounded actual fixed-axis spur
stages, and a terminal rotating carrier. Shared shaft identity, independent
coaxial rotors, contact, body mount and carrier-local attachment stay distinct.
Moving planet coordinates cannot be renamed into fixed shaft owners.

The existing runtime prepare/commit, epoch, retry ledger, cancellation and
checkpoint verification are the integration target. New source/checkpoint
profiles must not change interpretation or bytes of old accepted artifacts.
The browser runs the same production C# implementation; JavaScript only owns
input, presentation, Worker lifetime and persistence.

### Typed composition API

`SpatialWindingDefinition` owns the new geometry and actual shaft/body/material
identities. `WindingDifferentialDefinition` accepts it through a new overload;
`WindingSource` is the shared planar/spatial view. The legacy `Winding` property
continues to mean planar geometry and refuses spatial access explicitly.

`ConnectedTransmission` adds1…4 serial `FixedAxisSpurStage` objects **after** the
existing differential fixed-axis suffix. A stage declares its real source shaft,
new output shaft, two attached gear bodies, external-spur contact, tooth counts,
pitch radii, two body mounts, integer registration K, output reference and
calibrated output port. Shared driver shaft is not a fresh independent input.
Pitch planes, centre distance, mounting, tooth/module compatibility, owner IDs
and the reference equation are validated. Distinct stage pitch planes and
separation from the bounded winding volume are required by this profile.

For axis sign `s = driver.Z dot output.Z` the stage relation is:

`thetaOut = -zDriver/(s*zOutput)*thetaIn + (K-zDriver*mountDriver)/(s*zOutput)-mountOutput`.

Port readout sign/offset is applied only when reading the port; it is never
applied again as a gear-body mount. Exact coefficients act on
`ConnectedMotionValue = exactConstant + sum(exactCoefficient * namedNumericSource)`.
The same source cancels exactly; independent sources never become the same
cause merely because their estimates are close.

The terminal carrier is a rigid body on the last actual output shaft, with its
own frame/mount and0…8 `CarrierRigidAttachment` local frames. Those carried
bodies/axes move with the carrier; they are **not independent rotational DOFs**.
This does not create a general moving-axis gear network, differential dynamics,
or a particular escapement. Existing E2 world/relative motion remains in its
actual carrier frame, not a fake fixed-axis copy.

### Public author / run / restore flow

See `examples/winding/SpatialWindingExample.cs` for fully specified ordinary
authoring, with link-count, scale, material-ID and stage-count variants. The
following is the shared API sequence, not a second solver:

```csharp
var sdk = GearInvestSdk.CreateDefault();
var definition = SpatialExample.Create(); // replace with your own typed inputs
var artifact = sdk.FinalizeWindingConnection(definition);
using var session = sdk.PrepareMechanicalRuntime(artifact.Bytes, "my-session", initialPlanet);
// RuntimeRequest contains current session/definition/state/epoch/revision,
// unwrapped driverTurns, the COMPLETE mode-required input vector, and ordered events.
var result = session.Advance(request);
var checkpoint = session.Checkpoint();
using var restored = sdk.RestoreMechanicalRuntime(checkpoint.Bytes);
var continued = restored.Advance(nextRequest); // based on restored.Current
```

The old runtime owns source/generation, prepare/commit/cancel, retry, mode input
ownership and transaction semantics. New geometry is evaluated in those same
operations. Browser `MechanicalRuntimeClient` runs the shared C# in a dedicated
Worker; no JavaScript kinematics or server computation is required. Renderer
frames, advance frequency and checkpoint frequency remain separate choices.

### Serialization and compatibility

| Surface | New contract / compatibility |
|---|---|
| Geometry/numerics | `finite-spatial-guided-pin-chain-v1` / `binary64-chord-bracket-residual-v1` |
| Composition | `spatial-winding-differential-composition-v1` |
| Draft/artifact | `gear-invest.spatial-winding-connection-draft` and `gear-invest.spatial-winding-connection-mechanism`, version0.1 |
| Runtime | `bounded-spatial-winding-runtime-checkpoint-v1`, existing runtime semantic version1.0 |
| Checkpoint | `gear-invest.mechanical-runtime-checkpoint`, explicit formatVersion2.0; current reader also retains old1.0, old reader refuses2.0 |
| Planar source/recording/checkpoint | no migration required; original profile, meaning and identity retained; no frozen fixture rewritten |

Exact fractions/counters remain canonical integer strings. Geometry parameters
and residual estimates stay explicitly binary64 values. Raw payload digests and
semantic identities are separate: native/WASM estimates may differ while
owner/constraint/coefficient/mode/cursor semantics must agree. Checkpoint restore
reconstructs the source and current geometry, all coordinates, active captures,
locks, numerical causes and retained retry ledger. Rehashing JSON alone cannot
bypass those checks. This does not prove deleted history or prevent a coordinated
valid rollback without an external authority.

## Environment and acceptance instructions

Build the shared runtime with
`tools/e123-runtime-checkpoint/Build-Web.ps1 -Spatial -OutputDirectory artifacts/e123-spatial/web`.
The output is static resources plus an ordinary interactive example; see the
[ordinary runtime guide](../examples/runtime/README.md) for root/subpath hosting and fresh-page continuation. The
source example file is create-only and reused on rebuild. A changed authored
input needs a new explicit output path, not silent source replacement.

Browser proof is layered: ordinary UI fresh input/persistence; a separate long
acceptance page; cross-host semantic checks; independent scalar pin/affine
checks. The acceptance page is not a required route for using the product.
Use [the real-device checklist](SPATIAL_RUNTIME_DEVICE_ACCEPTANCE.md) on a selected
Android Chrome and iPhone/iPad Safari. Node, viewport emulation and desktop
WebKit do not satisfy those device entries.

## Acceptance and publication

The [public verification scope](VERIFICATION.md) separates implementation,
native/WASM comparison, actual PC usage and publication evidence. A lower-layer
check is not full SDK acceptance. The real-device execution pack is included;
Android/iOS/Safari acceptance remains unverified. Consult the exact Release for
its commit/archive/CI evidence. Private reports and approvals are not build inputs.
