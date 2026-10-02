# @gearinvest/replay

Headless consumption of bounded canonical GenericGear artifacts; not synthesis
or authoritative mechanical validation. Source candidate `0.1.0-rc.public02.1`;
not published to npm. Build from the source workspace.

Exports: root (planar), `/oriented`, `/oriented-two-output`, `/assembly`,
`/carrier` and `/differential`. The last two consume bounded single-input and
multi-input carrier profiles, respectively. They evaluate unwrapped exact
fractions with optional display; they do not solve tooth contact or rank.
Assembly `readAssemblyReplay(bytes)` creates a document whose original bytes,
integrity checks and instance state are separate. `createInstance(id)` gives
independent playback state; `evaluateAffine(exactRoot)` preserves exact integer
fractions for supported affine channels. Unsupported formats/evaluations are
refused. Stored numeric playback and exact analytic evaluation are not synonyms.

No DOM, canvas, WebGL, Unity or Three.js dependency. Optional rendering belongs
to the companion presentation package. See the source example and consumer
documentation in the repository. AGPL-3.0-only; see included LICENSE/scope/notices.
