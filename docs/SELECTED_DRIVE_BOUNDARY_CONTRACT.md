# Selected spatial drive boundary (DRV-01)

Role: normative contract for the additive selected-drive source and runtime.
Read with [spatial geometry](SPATIAL_WINDING_COMPOSITION_CONTRACT.md),
[runtime](MECHANICAL_RUNTIME_CONTRACT.md), [modes](MECHANICAL_CONNECTION_MODES_CONTRACT.md)
and [differential contacts](BOUNDED_DIFFERENTIAL_CONTRACT.md). This is source
implementation, not publication approval or a continuous-path certificate.

## Authoring and actual ownership

Approach A is used. `SpatialWindingDefinition.SelectDriveBoundary(actualShaftId,
initialPrescribedTurns)` creates a **new immutable source** selecting either real
reel. It is not an inverse command applied to an existing runtime. In this new
source `DriverShaft` is the selected prescribed rotor, `OutputShaft` the other
passive rotor; `driverTurns` always names that selected native coordinate.

The method maps the whole shaft/frame/body/guide/domain/branch/initial boundary.
For the opposite endpoint it reverses bridge traversal and swaps the two guides
without changing either guide's physical helix hand, phase or frame. The existing
solver still checks both attachment tangent cones and every neighbouring chord
and joint. `MaterialTraversalOrder` maps solver indices to stable material IDs:
in reverse, pin i names original N-i, link i names original N-1-i. Consecutive
material neighbours and physical endpoint attachments remain the same. The pose
array is in solver order; consumers must use its actual IDs, not infer IDs from
array indices. Direct explicit role authoring also accepts this order enum.

The new `WindingDifferentialDefinition` overload requires `couplingShaftId` as
well as the explicitly selected winding source. This must be one of the two real
reels, distinct from but coaxial with the E2 sun, with the same native +Z and zero
X orientation. It may be the prescribed reel. No proxy shaft or copied input is
created. The two independent coaxial rotors remain different owners.

Boundary input is shaft-native **unwrapped turns**, sign +1/readout offset0 in
the actual authored proper cardinal frame. A calibrated port is not accepted as
an alternative drive key. Differential sun/planet ports retain their existing
signed frame/readout calibration. Identity and serialized binding include the
actual owners, role, frame, material order, source/profile, geometry, finite
domains/branches and coupling target. UI names have no mechanical authority.

```csharp
var selected = winding.SelectDriveBoundary(winding.OutputShaft.Id, initialR);
var source = new WindingDifferentialDefinition(selected, selected.DriverShaft.Id,
    suffix, couplingId, sunPortId, planetPortId, initialH,
    transmission: transmission);
var artifact = sdk.FinalizeWindingConnection(source);
using var session = sdk.PrepareMechanicalRuntime(artifact.Bytes, sessionId, p0);
// Request: current session/source/state/revision/epoch, exact driverTurns=r,
// complete mode-required ports and ordered arrival events.
var result = session.Advance(request);
using var restored = sdk.RestoreMechanicalRuntime(session.Checkpoint().Bytes);
```

The ordinary factory `SpatialExample.CreateSelectedDrive` shows the same249-link
conical mechanism as `Create`, selecting its formerly passive coaxial reel.
It is a generic authored example, not manufacturer geometry or a regulated watch.
No arbitrary shaft/graph inverse solver or runtime boundary swapping is supplied.

## Same coordinate, exact law and modes

Let r be prescribed, b the numerically solved opposite reel, c the declared
coupling rotor (r **or** b), s the separate sun, p the independent planet port.
The exact input r is used once, for its actual chain attachment and coordinate.
When c=r, the downstream coupling uses that same exact value. b remains a named
numerical source; a small residual is not an exact angle. When c=b, numerical
terms propagate and cancel only by identical named cause and exact coefficient.
The latent source includes winding identity (including roles/branch) and exact r.

The selected profile binds `binary64-chord-bracket-refined-v1` into its source
identity and artifact numerical policy. It uses the same bounded spatial solver,
with passive closure residual stopping at1e-13 (dimensionless link-count
residual), bracket width2e-15 turns and at most52 bisections. Per-pin refinement,
acceptance residual1e-8mm and work caps remain unchanged. Legacy profiles keep
their original stopping policy and identities. This refinement addresses a
reproduced cross-host endpoint-angle discrepancy at r=80481/2000000; it is not
an angular error bound. Cross-host/restore tolerances remain1e-10 generally and
1e-8 for pose matrix components, with exact identifiers and rational values.

| Mode / event | Actual constraint | Complete input / condition |
|---|---|---|
| DriveCapture | s=c+H | r and planet port |
| Released | no c/s coupling | r, sun port, planet port |
| WorldCarrierLock | s=c+H; actual carrier=L | r; derive planet through admitted exact law |
| PlanetRelativeLock | s=c+H; declared relative coordinate=L | r; not a world planet lock |
| DirectionRestrictedDrive | s=c+H and sign of unwrapped r increment | r and planet port; no monotonicity assumption about b |
| Capture / Positive / Negative | H=current s-c; optional r direction | preserves arrival pose and source contact K/mounts |
| AlignCapture | s-c equals authored alignmentOffset | exact zero only; nonzero conflict, remaining numerical cause indeterminate |

Missing runtime keys are `Underdetermined`; extra keys are
`ModeInputOwnershipConflict`. They are not static algebraic redundancy handling.
Existing static E2 rank/consistency diagnostics remain separate. Observations
verify, never replace independent keys or command inverse solving. Numeric
observation equality without exact cancellation is `GuardIndeterminate`;
provably unequal exact values are `InconsistentObservation`. Lock constraints
do not silently override additional prescribed keys. No missing input is zeroed.
Release/capture are ideal position policies, not velocity/energy synchronisation.

For the default example, an independent contact expansion gives
`100*s-10*p-110*C=-9`, opposite-axis suffix `o=C/2-3/16`, two equal-tooth
stages and terminal readout +1/7. Hence fixed p,H gives delta y=5/11 delta r.
That equation belongs to the example/oracle; production compiles actual contacts.

## Formats, restore and compatibility

| Surface | Additive identity |
|---|---|
| Selected winding | `selected-drive-spatial-guided-pin-chain-v1` |
| Composition | `selected-drive-spatial-winding-composition-v1` |
| Draft / mechanism | `gear-invest.selected-drive-spatial-winding-connection-draft` / `gear-invest.selected-drive-spatial-winding-connection-mechanism`, version0.1 |
| Runtime profile | `bounded-selected-drive-spatial-runtime-checkpoint-v1` |
| Checkpoint schema | `gear-invest.mechanical-runtime-checkpoint`, version3.0 |

Old planar1.0/spatial2.0 checkpoints and old source bytes/identities retain their
semantics. Old readers reject new formats. This is explicit re-authoring, not a
silent migration of a stored old driver direction. The C# reader reconstructs
source, selected roles, H/L capture/lock witnesses, active constraints and numeric
causes; digest rehash alone cannot bless changed owners or metadata. Format3.0
also stores the binding in state and compares it to the rebuilt source.

Checkpoint is current-state validation, not a proof of discarded history.
Epoch256, retry16, live sources64 and128-digit lifetime counters remain; normal
runtime progress is not the old4096 recording limit. Publication is still
Prepare/Commit, whole-request atomic, with actual LastValidState on refusal.
New page/process restoration needs only the complete checkpoint and same SDK.
Cancellation/lifetime/CAS semantics are unchanged. C# and WASM share the engine;
their agreement is not independent physical proof.

## Failure and remaining guarantees

Geometry/domain/resource limits are unchanged and apply to the **selected**
driver and opposite passive ranges after role mapping. Both ends and sampled
interiors are evaluated; multi-segment out-and-back is not endpoint-only.
`NoBracketInDeclaredBranch` is search failure, not proven impossibility.
Numerically unresolved/ambiguous/event guards, joint failure, domain boundary,
unsupported profile and resource refusal remain distinct. There is no invented
global singularity or branch-uniqueness certificate.

**PATH-GAP remains open**:128 intervals per input turn do not exclude a narrower
invalid interval. More samples, successful role selection and source admission
do not close this pre-existing guarantee gap. No continuous-path proof, swept
solids, dynamics, manufacturing or unique link roll is claimed.

Run the [ordinary consumer](../examples/runtime/README.md) with `-SelectedDrive`.
The [device pack](DRV_DEVICE_ACCEPTANCE.md) records physical Android/iOS as
UNVERIFIED until actual execution; desktop/WASM tests do not replace them.
