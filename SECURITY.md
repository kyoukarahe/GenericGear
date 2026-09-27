# Security policy

Report suspected vulnerabilities privately to **kyoukarahe@gmail.com** with the
snapshot/version, affected entry, impact and a minimal sanitized reproduction.
Do not post working credentials, exploit payloads containing personal data, or
private customer artifacts in a public issue. No response-time guarantee is
made. Repository private vulnerability reporting, when enabled, is another
channel; this document does not claim the setting has been enabled.

Treat arbitrary mechanism JSON, GLB, paths and archive contents as untrusted.
Exact arithmetic still has resource limits. Verification of a digest is not
verification of a mechanical model. Refuse unsupported formats/profiles rather
than claiming validation from an approximate preview. The example is a local
tool, not a hardened multi-tenant upload service.

The current source candidate is the only line under review. Old pre-release
artifacts are not silently relabeled or covered by this release's evidence.

If a secret is exposed, quarantine the affected publication and notify its
owner; history edits alone do not revoke it. Rotation requires the owner's
authority. Security findings and scanner raw data must not be pasted into
public CI logs. Automated scans reduce risk; they do not prove absence of
unknown secrets or security defects.
