# Consumer contracts

The C# façade (`GearInvestSdk.CreateDefault()`) is the authoring and validation
entry. `examples/console/Program.cs` shows typed input → analysis → finalization
→ source reconstruction → assembly replay export. Refused finalization is not
an artifact. Preserve both diagnostics and accepted source bytes.

The web packages use public exports (`@gearinvest/replay/assembly`,
`@gearinvest/presentation/assembly`). `readAssemblyReplay` accepts canonical
bytes; integrity verification, structural acceptance, same-version .NET source
reconstruction, and physical validation are distinct claims. Browser replay is
not a second mechanical solver. Unsupported versions/profiles must be refused.

For affine rotation, keep `q`, `p`, the absolute root `u`, and `q*u+p` as exact
integer fractions. The phase is in turns. Normalize each **output's** result
only when converting to display radians; never replace the absolute input by
`u mod 1` before applying a reduction ratio. That loses, for example, the
progress of a `1/960` output after one input turn. The example accepts exact
integer/fraction/finite-decimal text with explicit size bounds, not a floating
`Number` as a lossless state carrier. The fixed-affine rotation display-period
helper is not a general solution for nonlinear/event/state mechanisms.

Attach outside meshes/GLBs to a resolved instance + body reference. Apply the
SDK body pose and fixed local asset correction, or an explicitly equivalent
shaft-scalar/mount transform. The comparison tests exercise both routes.
glTF units, pivot, axis and correction remain presentation metadata. A parent
transform must not change ratios, owner IDs, source identity or canonical bytes.
Keep instance-specific bindings separate from mechanism identity. Cancel stale
asynchronous loads and dispose replaced assets. Imported user assets are not
implicitly licensed or mechanically validated.

Saving original replay must preserve bytes. Saving View stores only bindings
and display state. Reopening View requires the matching source identity and
known asset references. The example uses local browser state and ordinary file
inputs/downloads; it does not upload user mechanisms to a service.
