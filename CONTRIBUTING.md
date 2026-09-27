# Contribution policy — initial source release

Issues, documentation feedback and minimal sanitized bug reports are welcome.
**Outside code and asset PRs are not yet accepted for merge** while the dual-
license contribution agreement is being reviewed. Opening a PR does not grant
us ownership or alternate commercial licensing permission. Do not infer a
relicensing grant from GitHub's default terms or a DCO sign-off.

Before code acceptance, the maintainer must choose and publish an explicit
inbound-rights policy reviewed for this project. Options include a voluntary
CLA granting nonexclusive commercial sublicensing rights, a properly reviewed
assignment, or keeping the contribution AGPL-only and excluding it from
commercial offerings. No contributor is automatically bound to an unwritten
agreement. The initial policy is this temporary code-merge hold,
not an invented CLA.

The [DCO](https://developercertificate.org/) can record provenance/certification;
it does not by itself give the maintainer unrestricted proprietary relicensing
rights. Disclose copied snippets, dependency changes, asset sources, employment
obligations and material AI assistance. Preserve upstream attribution.

CI uses hosted ephemeral runners and read-only repository permission. It does
not run untrusted PR code with repository secrets, use `pull_request_target`,
publish packages, or access the private development checkout. Do not weaken
those boundaries to make a contribution pass. See [CI policy](docs/CI_POLICY.md).
