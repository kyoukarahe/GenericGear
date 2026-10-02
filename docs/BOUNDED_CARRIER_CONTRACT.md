# Bounded grounded-sun carrier — E2a

Role: Normative contract for the bounded E2a profile in this source release.
Read the [consumer guide](../examples/carrier/README.md) and
[supported scope](SUPPORTED_SCOPE.md). The complete attached mechanical source,
not a stored derived law or validation flag, owns the mechanism definition.

## Authoritative source and bounds

Profile `parallel-grounded-sun-single-planet-v1` contains one current mechanical
source, one explicit source-to-mm mapping, one borrowed actual carrier shaft,
one grounded sun shaft and one carrier-local planet shaft. The input is the
source's sole prescribed shaft. A source is either the existing single-shaft
standalone profile or a freshly finalized existing external-spur one-stage
source (two shafts/two bodies/one mesh). No synthetic prescribed carrier.
All original source objects/requirements survive; source admission is not
replaced by a successful local carrier equation.

Maximum four shafts, five bodies, two meshes, one hold and one carrier.
Sun/planet teeth are 1..4096; module is positive mm. Frames are proper cardinal
reference frames with parallel signed axes. The planet frame is explicitly
carrier-local, with center on positive local X at the sum of pitch radii.
Sun center and carrier plane origin coincide. A prefix's pitch planes must be
separate from the moving pair's plane. These are ideal zero-width pitch-plane
checks, not solid/swept clearance, tooth contact, bearings or dynamics.
Requested unsupported validation blocks finalization. No nested carrier,
internal gear, differential, additional input, winding, mode or downstream
fixed-frame device is admitted through this profile.

## Exact coordinates and phase

Turns are unwrapped rationals. In the common oriented plane, physical angles
are `psi = gamma + epsilon * (shaftTurns + bodyMountTurns)`; gamma is the exact
cardinal zero-ray offset and epsilon is the axis sign. Carrier angle C uses
the borrowed shaft coordinate and its actual mapped reference frame.

The external contact declares an integer tooth registration K:
`Ns*(psiSun-C) + Np*(psiPlanet-C) = K`.
The sun is explicitly held at its reference coordinate. Authored carrier/sun/
planet reference coordinates must satisfy this equality; arbitrary inconsistent
phases do not become a new derived offset. The compiler derives planet world
rotation from this contact. Planet relative shaft rotation is
`planetWorldTurns - epsilonPlanet*C`.
The local frame hierarchy applies C exactly once. Nonzero mounting and signed
frames remain explicit. A calibrated port readout is separately
`portAxisSign*planetWorldTurns + readoutOffset`; it never drives a body.

## Public responsibilities and persistence

`AnalyzeCarrier` reports source, ownership/domain, contact/reference,
determinacy and pitch-plane checks separately. Invalid/unsupported requests
remain serializable drafts but cannot return a normal artifact. Immutable
`CarrierDefinition` is authored directly; this slice adds no edit-session engine.
`TryFinalizeCarrier` freshly finalizes the complete attached source.
`ReadCarrierArtifact` / `RebuildCarrierArtifact` repeat that source finalization
and compare the entire canonical result, including derived laws/frame parents.
Stored hash/PASS/q,p never bypass the current request.

Separate formats `gear-invest.carrier-draft`, `gear-invest.carrier-mechanism`
and `gear-invest.carrier-replay`, each version `0.1`. Old formats/readers are
unchanged and must reject these. Exact fractions are reduced decimal strings,
never JSON floating-point angles. Stable IDs are ordinal, bounded and unique
within their typed namespace. Inputs: 128 integer characters; derived values:
1024; documents: 8 MiB; inherited strict duplicate/depth/node limits also apply.
Malformed/resource input throws; well-formed inadmissible mechanics produces
diagnostics. Writes use the existing create-only, link-checked file boundary.

## Evaluation and consumer boundary

`EvaluateCarrier` returns exact actual shaft, common-world and carrier-relative
coordinates and a separate optional display-pose projection. Analytic evaluation
has no clock, state advance, renderer or hidden period. Display trig uses exact
modulo before binary64 conversion; unavailable display cannot erase exact motion.

The new headless Web `/carrier` entry evaluates exported generic affine laws
and typed parent-frame recipes for arbitrary bounded new exact input. It does
not contain a sun/planet tooth solver or a hard-coded 11c. Source and replay
identity, schema/reference checks, digest verification and current C# mechanical
rebuild are distinct; browser mechanical validation stays `notPerformed`.
Parent/pivot correction follows `inverse(rendererParentWorld) * bodyWorld *
assetLocalCorrection`. Host owns timing, loading, meshes and selection. Exact
state is independent of display tolerance. No WebAssembly/server is required.

## Acceptance

Ordinary standalone and real-prefix creation, fresh-process source rebuild,
independent 100/10 and non-default/signed/nonzero-reference oracles, correctly
rehashed corruption rejection, old-reader refusal and failure preservation.
The static Web consumer must exercise new/negative/huge input, stop/seek,
selection/load/fresh reload and parent/pivot transformations. Node checks do not
stand in for that browser flow. Consumer-specific assets, Unity/native,
mobile/Safari and installed binary packages require separate acceptance.
