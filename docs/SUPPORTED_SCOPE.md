# Supported scope and known limits

This snapshot includes the existing bounded SDK implementation: rational
rotational/linear relationships; bounded synthesis, spatial checks and routing;
oriented spur/bevel paths; selected belt, chain, worm, lead-screw and rack
models; bounded crank-slider, cam and Geneva models; explicit mechanical
assembly composition, calendar/event/state logic; and explicit parallel
independent coaxial rotors. Support is profile-specific, not an arbitrary
mechanism simulator. A type's existence does not imply every composition is
accepted. Check diagnostics and the profile at the public entry.

The coaxial example contains one real root, a real compound intermediate shaft
and independent coaxial input/output ownership, not unrelated visual spinners.
Same geometric axis is not a rigid coupling. Contact, ownership, layer and
clearance refusals are tested. Source reconstruction is same-version and does
not promise indefinite forward compatibility with future algorithms.

The moving-frame profiles add one grounded-sun carrier (E2a) and one free-sun
single-planet differential (E2b), optionally attached to one real external-spur
prefix. E2b supports at most two independent prepared port inputs, explicit
static holds, exact rank/partial/inconsistent boundary analysis and canonical
source reconstruction. Web consumes exported affine vectors; it does not solve
rank/contact or rebuild current mechanics. See the [carrier contract](BOUNDED_CARRIER_CONTRACT.md)
and [differential contract](BOUNDED_DIFFERENTIAL_CONTRACT.md).
No arbitrary differential network, ring gear, nested carrier or downstream
worm/belt/nonlinear bridge is implied. Pitch-plane checks do not certify tooth,
arm, shaft or swept solids.

The added [finite winding profile](FINITE_WINDING_CONNECTION_CONTRACT.md) has
4–21 material links, planar convex pin seats and finite lifts narrower than one
turn. It connects actual E2b owners to one external-spur suffix. The separate
[mode profile](MECHANICAL_CONNECTION_MODES_CONTRACT.md) supports explicit
release/capture, world/relative locks and prescribed direction restrictions.
The [C# WASM runtime](MECHANICAL_RUNTIME_CONTRACT.md) computes new inputs locally
and source-revalidates compact checkpoints; old replay packages remain readonly.
Current provenance is limited to 64 independent causes, the retry ledger to 16,
and lifetime counters to 128 digits. Compaction does not prove deleted history.
The separate [spatial winding/composition profile](SPATIAL_WINDING_COMPOSITION_CONTRACT.md)
adds8–512 fixed-pitch links on variable-radius/height helical pin guides, finite
unwrapped multi-turn motion, guided transfer and spherical free-twist joints.
One actual conditional winding/differential connection supports1–4 extra serial
spur stages and a terminal carrier with0–8 rigid attachments. The ordinary
example uses249 links and also accepts other documented authoring parameters.
Spatial checkpoints use explicit format2.0; old planar1.0 remains readable.
Unguided slack chains, arbitrary moving-axis graphs and whole watch networks are
not implied. Link roll is underdetermined; numeric residuals and sampled path
checks are not certified solution bounds or continuous-path proofs.

Nonlinear evaluation is bounded and can refuse requests at numeric/resource
limits. Approximate stored playback is not exact analytic evaluation. Exact
arithmetic does not make continuous geometry or floating display bit-identical
across all platforms. No forces, friction, stress, durability, lubrication,
tolerances, manufacturing certification or global routing optimum is claimed.

The current public candidate includes selected regression tests, not every
historical platform/performance campaign. Windows .NET/Node verification is
recorded separately for this candidate. Linux/macOS, Firefox, real Android
Chrome/iPhone Safari/macOS Safari and a fresh Unity integration are not validated
by those checks. Prior private evidence is not presented as a public CI run.
The retained .NET shared-Geneva short-warm measurement was about 84.94% slower
than its comparison baseline and failed the +5% target. This source-release
packaging does not fix that issue or make a new performance/SLA guarantee.

The public source does not require private conformance catalogs or historical
reports. The older private CLI and its broad acceptance catalog are not shipped
in this source release; the console and carrier/differential consumers are the small .NET
entry. SDK implementation coverage is not reduced by that packaging choice.
