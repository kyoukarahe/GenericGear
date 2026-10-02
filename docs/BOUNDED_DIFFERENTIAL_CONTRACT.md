# Bounded differential E2b contract

## Scope and authority

New profile `parallel-free-sun-single-planet-v1` owns three distinct actual rotational coordinates: fixed-axis sun `s`, carrier `C`, carrier-local planet `p`. An optional admitted single external-spur prefix adds its actual root coordinate. This is geometric ideal kinematics, not dynamics or a general differential/gearbox solver. Existing E2a profile, formats, single-input solver guards and frozen artifacts keep their meanings.

One actual external contact satisfies `Ns(s_common-C_common)+Np(p_common-C_common)=K`. Signed cardinal frames, body mounting offsets and explicit reference positions establish common coordinates and integer registration K. K is immutable authored data, checked at the reference; evaluation never adjusts it to fit inputs. Angles are exact unwrapped turns; lengths are exact mm. The carrier is applied once in the planet pose hierarchy; relative spin and world spin are different outputs.

## Definition, boundaries and preparation

The public immutable definition declares actual shafts, contact, module/teeth, frame/reference/mount data, typed ports, optional static holds and optional actual prefix source. Ports name existing coordinate owners; aliases do not introduce DOFs. Port value = signed owner coordinate + explicit readout offset. Input conditions reference port IDs, static holds reference coordinate IDs. Unknown references, incompatible units and malformed frames are definition/input errors.

Preparation selects an ordinal-canonical set of at most two port IDs. Standalone supports all three independent two-coordinate bases. A prefix adds one constraint; its root plus sun is the demonstrated two-input basis. The same exact rank calculation handles other admitted port pairs; no synthetic drive is introduced. A selected basis must admit arbitrary independent values for executable finalization. A hold with one independent input is allowed. A redundant selected basis is not exported as two independent inputs.

Separate numeric boundary analysis accepts up to six explicit conditions and reports rank, known coordinates, free coordinates/relations and inconsistent row/condition witnesses. Consistent redundancy is permitted here. Three contradictory coordinate values produce inconsistency, not an arbitrarily selected pair. Existing `MechanicalDeterminacy` statuses describe the result; resource failures remain errors, not mathematical inconsistency. `DeterminedBySelectedInput` includes a selected vector of inputs.

Evaluation consumes one complete immutable vector snapshot for the prepared basis. Missing/extra/duplicate keys are input errors; zero is an explicit value. There is no previous-value or zero fallback. Changing basis/holds creates a new static preparation and source identity, not clutch capture or a mode transition. Underdetermined analysis exposes known coordinate laws and free relations but cannot supply a normal complete mechanism pose. Inconsistent analysis supplies no solution or pose.

## Persistence and consumers

New canonical `gear-invest.differential-{draft,mechanism,replay}` / `0.1` formats carry the definition, selected basis and compiled exact matrix `Q*u+b`, rows, reference-derived K, ownership, holds, frames, checks and source context. C# read/finalize/rebuild recomputes current mechanics and attached source admission and compares the complete canonical bytes. Rehashing modified caches does not bypass reconstruction. A hash is not a signature or mechanical proof.

Headless Web replays exported Q*u+b and generic typed frame recipes; it has no tooth-count or rank solver. It checks bounds, schema, references, source/compiled correspondence and optional host digest. Current C# mechanical rebuild remains `notPerformed`. Readonly boundary-analysis documents may show stored known/free/diagnostic data, but are not executable replay or a browser recomputation. View transforms, external asset pivots, playback time and UI state do not own exact motion.

## Admission and resource limits

Exact pitch tangency and separated prefix pitch planes are checked for all phases. Tooth solids, shaft/arm intersections, swept solids, load, friction, durability and dynamics are not certified; requiring an unsupported domain prevents finalization. No downstream worm/belt/nonlinear adapter, linear/radian conversion, interval midpoint or hidden E1/E3 state is added.

Per definition: 3–4 coordinates, one carrier contact, optional one-stage/two-shaft/two-body prefix, ≤6 ports, ≤6 static holds, ≤6 numeric boundary conditions, ≤2 prepared inputs, teeth 1–4096, IDs ≤160 chars, input rational components ≤128 decimal characters. Derived values ≤1024 characters; exact elimination ≤16 rows, ≤4 coordinate columns, ≤3 RHS columns and ≤50,000 scalar operations. Document ≤8 MiB, attached source ≤4 MiB. Browser additionally bounds pose/channel counts and instance count; no global SDK limits are raised. Overflow/resource rejection is distinct from `InconsistentConstraints`.

## Delivery boundary

This profile is available in the public source release. The [ordinary consumer](../examples/differential/README.md) builds against the six DLLs produced from this same source tree; the Web workspace exports `/differential`. This is not an npm/NuGet/UPM binary release. See [verification scope](VERIFICATION.md) for the selected public acceptance path and unverified platforms. No winding, conditional mode switch or general differential-network support is implied.
