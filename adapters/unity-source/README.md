# Unity adapter source — distribution hold for binaries

These are the existing first-party runtime adapter source files, not an
installable UPM package. No UnityEngine DLL, editor license, Unity runtime,
SDK DLL or third-party DLL is bundled. They are optional and are not part of
the .NET/web quick-start dependency graph.

The intended technical integration uses the SDK's netstandard2.1 output and
the adapter's ordinary view/session types. It leaves solving and validation
in the SDK; MonoBehaviour/GameObject/Transform belong only to this adapter.
This candidate does not claim fresh Mono, IL2CPP, Editor or player verification.

Before distributing an executable/UPM package, review AGPL combined-work and
source obligations with the actual [Unity software terms](https://unity.com/legal/editor-terms-of-service/software).
Do not infer permission from a successful compile. No AGPL linking exception
or proprietary Unity sublicensing right is included. The first public release
recommendation is .NET/web source only, with this adapter source for inspection.
