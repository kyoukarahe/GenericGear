using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using Xunit;

namespace GearInvest.Tests;

public sealed class ArtifactValidationTests
{
    [Fact]
    public void PublicFacadeDetectsCandidateAndArtifactIdentityTampering()
    {
        var sdk = GearInvestSdk.CreateDefault();
        var written = sdk.WriteArtifact(TestFixture.GenerateCandidate()).Artifact;
        var tampered = new MechanismArtifact(
            "sha256:wrong-candidate",
            "sha256:wrong-artifact",
            written.Metadata,
            written.Candidate);

        var validation = sdk.Validate(tampered);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Validation.Diagnostics, item => item.Code == DiagnosticCodes.CandidateIdMismatch);
        Assert.Contains(validation.Validation.Diagnostics, item => item.Code == DiagnosticCodes.ArtifactHashMismatch);
    }

    [Fact]
    public void PublicFacadeDetectsStaleStoredValidation()
    {
        var sdk = GearInvestSdk.CreateDefault();
        var candidate = TestFixture.GenerateCandidate();
        var stale = candidate.WithValidation(new ValidationBundle(new[]
        {
            new Diagnostic("STALE", DiagnosticSeverity.Info, "Stale stored observation."),
        }));
        var written = sdk.WriteArtifact(stale).Artifact;

        var validation = sdk.Validate(written);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Validation.Diagnostics, item => item.Code == DiagnosticCodes.StoredValidationMismatch);
        Assert.True(validation.Identity.CandidateIdMatches);
        Assert.True(validation.Identity.ArtifactHashMatches);
    }
}
