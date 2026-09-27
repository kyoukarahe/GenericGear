# CI and repository operations

The checked-in verification workflow has read-only `contents` permission,
full-length official action commits and ephemeral GitHub-hosted runners.
Checkout credentials are not persisted. No secret, environment approval,
registry token, private feed, self-hosted runner, deployment, artifact upload
or publish step is needed. Untrusted PRs run ordinary verification under
`pull_request`, never privileged `pull_request_target` or a follow-up privileged
workflow executing their artifacts. Cache poisoning is avoided by not using
shared build caches in this initial workflow.

After explicit publication approval, the owner should separately review branch
protection/rulesets, required review/checks, workflow-edit permissions, private
vulnerability reporting and secret scanning/push protection availability. This
document does not claim those remote settings were changed. Keep release
credentials out of PR workflows. Future package publishing requires a reviewed,
separate trusted-ref workflow and independent approval.

Local verification of workflow commands is not a hosted Actions run. The first
hosted run must be read back after publication. Keep failed results visible;
never use `continue-on-error` to report a passing release. Action update PRs
must verify the actual upstream commit, not merely move a mutable tag.
See [GitHub secure use](https://docs.github.com/en/actions/reference/security/secure-use).
