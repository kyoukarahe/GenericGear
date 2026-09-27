using System;
using System.Globalization;
using System.Linq;
using System.Text;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;
using Xunit;

namespace GearInvest.Tests;

public sealed class SerializationTests
{
    private readonly CanonicalMechanismJson _serializer = new();

    [Fact]
    public void SameCandidateProducesByteIdenticalArtifactAndStableIdentity()
    {
        var candidate = TestFixture.GenerateCandidate();

        var first = _serializer.Write(candidate);
        var second = _serializer.Write(candidate);

        Assert.Equal(first.Artifact.CandidateId, second.Artifact.CandidateId);
        Assert.Equal(first.Artifact.ArtifactHash, second.Artifact.ArtifactHash);
        Assert.Equal(first.Bytes, second.Bytes);
    }

    [Fact]
    public void CanonicalRoundTripIsByteIdentical()
    {
        var first = _serializer.Write(TestFixture.GenerateCandidate());

        var read = _serializer.Read(first.Bytes);
        var second = _serializer.Write(read);

        Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal(first.Artifact.CandidateId, second.Artifact.CandidateId);
        Assert.Equal(first.Artifact.ArtifactHash, second.Artifact.ArtifactHash);
        var identity = _serializer.VerifyIdentity(read);
        Assert.True(identity.CandidateIdMatches);
        Assert.True(identity.ArtifactHashMatches);
    }

    [Fact]
    public void NonIdentityMetadataChangesArtifactHashButNotCandidateId()
    {
        var candidate = TestFixture.GenerateCandidate();
        var first = _serializer.Write(candidate);
        var changedMetadata = new ArtifactMetadata(
            new ArtifactSourceProvenance(candidate.SourceId, "0", "same-mechanism-different-note"),
            first.Artifact.Metadata.Generator);

        var second = _serializer.Write(candidate, changedMetadata);

        Assert.Equal(first.Artifact.CandidateId, second.Artifact.CandidateId);
        Assert.NotEqual(first.Artifact.ArtifactHash, second.Artifact.ArtifactHash);
        Assert.NotEqual(first.Bytes, second.Bytes);
    }

    [Fact]
    public void StoredValidationChangesArtifactHashButNotCandidateId()
    {
        var candidate = TestFixture.GenerateCandidate();
        var first = _serializer.Write(candidate);
        var changedValidation = new ValidationBundle(new[]
        {
            new Diagnostic("TEST_INFORMATION", DiagnosticSeverity.Info, "Non-identity observation."),
        });
        var changedCandidate = candidate.WithValidation(changedValidation);

        var second = _serializer.Write(changedCandidate, first.Artifact.Metadata);

        Assert.Equal(first.Artifact.CandidateId, second.Artifact.CandidateId);
        Assert.NotEqual(first.Artifact.ArtifactHash, second.Artifact.ArtifactHash);
    }

    [Fact]
    public void SpatialPositionChangesCandidateId()
    {
        var candidate = TestFixture.GenerateCandidate();
        var changedAxes = candidate.Spatial.Axes
            .Where(axis => axis.Id != "axis:D")
            .Concat(new[] { new SpatialAxis("axis:D", 116, 0) });
        var changedSpatial = new SpatialMechanism(changedAxes, candidate.Spatial.Bodies, candidate.Spatial.Contacts);
        var changedCandidate = new GenerationCandidate(
            candidate.SourceId,
            candidate.Kinematic,
            candidate.Solution,
            changedSpatial,
            ResolvedPlaybackBuilder.Build(candidate.Solution, changedSpatial),
            candidate.Validation);

        Assert.NotEqual(
            _serializer.ComputeCandidateId(candidate),
            _serializer.ComputeCandidateId(changedCandidate));
    }

    [Fact]
    public void KinematicTransferChangesCandidateId()
    {
        var candidate = TestFixture.GenerateCandidate();
        var changedCouplings = candidate.Kinematic.Couplings
            .Where(coupling => coupling.Id != "mesh:C1-D1")
            .Concat(new[] { new ExternalGearCoupling("mesh:C1-D1", "C", "D", 20, 100) });
        var changedKinematic = new KinematicSpecification(
            candidate.Kinematic.RootDofId,
            candidate.Kinematic.Dofs,
            changedCouplings);
        var changedSolution = KinematicSolver.Solve(changedKinematic).Solution!;
        var changedCandidate = new GenerationCandidate(
            candidate.SourceId,
            changedKinematic,
            changedSolution,
            candidate.Spatial,
            ResolvedPlaybackBuilder.Build(changedSolution, candidate.Spatial),
            candidate.Validation);

        Assert.NotEqual(
            _serializer.ComputeCandidateId(candidate),
            _serializer.ComputeCandidateId(changedCandidate));
    }

    [Fact]
    public void ArtifactHashExcludesItsOwnField()
    {
        var written = _serializer.Write(TestFixture.GenerateCandidate());
        var changedStoredHash = new MechanismArtifact(
            written.Artifact.CandidateId,
            "sha256:tampered",
            written.Artifact.Metadata,
            written.Artifact.Candidate);

        Assert.Equal(written.Artifact.ArtifactHash, _serializer.ComputeArtifactHash(changedStoredHash));
        Assert.False(_serializer.VerifyIdentity(changedStoredHash).ArtifactHashMatches);
    }

    [Fact]
    public void RejectsUnknownMajorVersion()
    {
        var text = Encoding.UTF8.GetString(_serializer.Write(TestFixture.GenerateCandidate()).Bytes)
            .Replace("\"formatVersion\":\"0.1\"", "\"formatVersion\":\"1.0\"", StringComparison.Ordinal);

        Assert.Throws<ArtifactFormatException>(() => _serializer.Read(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void RejectsUnknownRequiredDiscriminator()
    {
        var text = Encoding.UTF8.GetString(_serializer.Write(TestFixture.GenerateCandidate()).Bytes)
            .Replace("\"kind\":\"externalGear\"", "\"kind\":\"planetary\"", StringComparison.Ordinal);

        Assert.Throws<ArtifactFormatException>(() => _serializer.Read(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void RejectsDuplicateProperty()
    {
        var text = Encoding.UTF8.GetString(_serializer.Write(TestFixture.GenerateCandidate()).Bytes);
        var duplicate = "{\"format\":\"gear-invest.mechanism\"," + text.Substring(1);

        Assert.Throws<ArtifactFormatException>(() => _serializer.Read(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void CanonicalBytesIgnoreCultureTimezoneAndFilesystemPath()
    {
        var candidate = TestFixture.GenerateCandidate();
        var baseline = _serializer.Write(candidate).Bytes;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousTimezone = Environment.GetEnvironmentVariable("TZ");
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            Environment.SetEnvironmentVariable("TZ", "Pacific/Honolulu");
            var changedEnvironment = _serializer.Write(candidate).Bytes;

            Assert.Equal(baseline, changedEnvironment);
            var json = Encoding.UTF8.GetString(changedEnvironment);
            Assert.DoesNotContain(Environment.CurrentDirectory, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pacific/Honolulu", json, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            Environment.SetEnvironmentVariable("TZ", previousTimezone);
        }
    }
}
