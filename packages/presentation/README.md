# @gearinvest/presentation

Optional display projection/binding helpers for GenericGear replay. Source
candidate `0.1.0-rc.public01.1`; not published to npm. The root and assembly
entries are headless; explicit canvas entries own browser drawing. Three.js is
a dependency of the example application, not this package.

Exports: root, `/canvas2d`, `/oriented`, `/oriented-canvas2d`,
`/oriented-two-output`, `/oriented-two-output-canvas2d`, `/assembly`.
AssemblyAssetBindings attaches application-owned assets using instance/body
references and fixed correction matrices. Binding or moving a displayed asset
must not rewrite source ratios, ownership or artifact identity. Saved View data
is distinct from saved original mechanism bytes.

See source example and consumer documentation. AGPL-3.0-only; preserve included
LICENSE/scope/notices and the replay dependency's conditions.
