using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class ArtifactSourceProvenance
{
    public ArtifactSourceProvenance(string specificationId, string seed, string generationOptions)
    {
        SpecificationId = Require(specificationId, nameof(specificationId));
        Seed = Require(seed, nameof(seed));
        GenerationOptions = Require(generationOptions, nameof(generationOptions));
    }

    public string SpecificationId { get; }

    public string Seed { get; }

    public string GenerationOptions { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty canonical value is required.", parameterName);
        }

        return value;
    }
}

public sealed class GeneratorFingerprint
{
    public GeneratorFingerprint(
        string sdkVersion,
        string generatorVersion,
        string backendId,
        string determinismProfile)
    {
        SdkVersion = Require(sdkVersion, nameof(sdkVersion));
        GeneratorVersion = Require(generatorVersion, nameof(generatorVersion));
        BackendId = Require(backendId, nameof(backendId));
        DeterminismProfile = Require(determinismProfile, nameof(determinismProfile));
    }

    public string SdkVersion { get; }

    public string GeneratorVersion { get; }

    public string BackendId { get; }

    public string DeterminismProfile { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty fingerprint value is required.", parameterName);
        }

        return value;
    }
}

public sealed class ArtifactMetadata
{
    public ArtifactMetadata(
        ArtifactSourceProvenance source,
        GeneratorFingerprint generator,
        GeneratedMechanismMetadata? generated = null,
        ComposedMechanismMetadata? composed = null,
        BranchedComposedMechanismMetadata? branchedComposed = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Generator = generator ?? throw new ArgumentNullException(nameof(generator));
        var metadataKindCount = (generated is null ? 0 : 1) +
            (composed is null ? 0 : 1) +
            (branchedComposed is null ? 0 : 1);
        if (metadataKindCount > 1)
        {
            throw new ArgumentException("An artifact cannot contain more than one generated or composed provenance kind.");
        }

        Generated = generated;
        Composed = composed;
        BranchedComposed = branchedComposed;
    }

    public ArtifactSourceProvenance Source { get; }

    public GeneratorFingerprint Generator { get; }

    public GeneratedMechanismMetadata? Generated { get; }

    public ComposedMechanismMetadata? Composed { get; }

    public BranchedComposedMechanismMetadata? BranchedComposed { get; }

    public static ArtifactMetadata CreateDefault(GenerationCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return new ArtifactMetadata(
            new ArtifactSourceProvenance(candidate.SourceId, "0", "low-level-fixed-spatial-v1"),
            new GeneratorFingerprint("0.1.0-dev", "vertical-slice-v1", "managed-exact", "artifact-v0.1"));
    }

    public static ArtifactMetadata CreateGenerated(ExactRatioMechanismCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        var provenance = candidate.Provenance;
        return new ArtifactMetadata(
            new ArtifactSourceProvenance(
                provenance.GenerationRequestId,
                "0",
                provenance.GenerationRequestCanonicalRepresentation),
            new GeneratorFingerprint(
                "0.1.0-dev",
                provenance.GenerationFingerprint.SemanticVersion,
                provenance.GenerationFingerprint.BackendId,
                provenance.GenerationFingerprint.DeterminismProfile),
            new GeneratedMechanismMetadata(provenance, candidate.Metrics));
    }

    public static ArtifactMetadata CreateComposed(ContinuousPlanCompositionCandidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        var provenance = candidate.Provenance;
        return new ArtifactMetadata(
            new ArtifactSourceProvenance(
                provenance.CompositionRequestId,
                "0",
                provenance.RequestCanonicalRepresentation),
            new GeneratorFingerprint(
                "0.1.0-dev",
                provenance.BackendFingerprint.SemanticVersion,
                provenance.BackendFingerprint.BackendId,
                provenance.BackendFingerprint.DeterminismProfile),
            composed: new ComposedMechanismMetadata(
                candidate.ComposedKinematicCandidateId,
                candidate.ComposedSpatialCandidateId,
                provenance,
                candidate.SemanticBindings,
                candidate.Metrics));
    }

    public static ArtifactMetadata CreateBranchedComposed(BranchedContinuousCompositionCandidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        var provenance = candidate.Provenance;
        return new ArtifactMetadata(
            new ArtifactSourceProvenance(
                provenance.BranchedCompositionRequestId,
                "0",
                provenance.RequestCanonicalRepresentation),
            new GeneratorFingerprint(
                "0.1.0-dev",
                provenance.BackendFingerprint.SemanticVersion,
                provenance.BackendFingerprint.BackendId,
                provenance.BackendFingerprint.DeterminismProfile),
            branchedComposed: new BranchedComposedMechanismMetadata(
                candidate.BranchedComposedKinematicCandidateId,
                candidate.BranchedComposedSpatialCandidateId,
                provenance,
                candidate.SemanticBindings,
                candidate.Metrics));
    }
}

public sealed class GeneratedMechanismMetadata
{
    public GeneratedMechanismMetadata(
        ExactRatioMechanismCandidateProvenance provenance,
        ExactRatioMechanismMetrics metrics)
    {
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public ExactRatioMechanismCandidateProvenance Provenance { get; }

    public ExactRatioMechanismMetrics Metrics { get; }
}

public sealed class ComposedMechanismMetadata
{
    public ComposedMechanismMetadata(
        string composedKinematicCandidateId,
        string composedSpatialCandidateId,
        ContinuousPlanCompositionProvenance provenance,
        IEnumerable<SemanticDofBinding> semanticBindings,
        ContinuousPlanCompositionMetrics metrics)
    {
        ComposedKinematicCandidateId = composedKinematicCandidateId ??
            throw new ArgumentNullException(nameof(composedKinematicCandidateId));
        ComposedSpatialCandidateId = composedSpatialCandidateId ??
            throw new ArgumentNullException(nameof(composedSpatialCandidateId));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        SemanticBindings = (semanticBindings ?? throw new ArgumentNullException(nameof(semanticBindings)))
            .OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public string ComposedKinematicCandidateId { get; }
    public string ComposedSpatialCandidateId { get; }
    public ContinuousPlanCompositionProvenance Provenance { get; }
    public ReadOnlyCollection<SemanticDofBinding> SemanticBindings { get; }
    public ContinuousPlanCompositionMetrics Metrics { get; }
}

public sealed class BranchedComposedMechanismMetadata
{
    public BranchedComposedMechanismMetadata(
        string branchedComposedKinematicCandidateId,
        string branchedComposedSpatialCandidateId,
        BranchedContinuousCompositionProvenance provenance,
        IEnumerable<SemanticDofBinding> semanticBindings,
        BranchedContinuousCompositionMetrics metrics)
    {
        BranchedComposedKinematicCandidateId = branchedComposedKinematicCandidateId ??
            throw new ArgumentNullException(nameof(branchedComposedKinematicCandidateId));
        BranchedComposedSpatialCandidateId = branchedComposedSpatialCandidateId ??
            throw new ArgumentNullException(nameof(branchedComposedSpatialCandidateId));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        SemanticBindings = (semanticBindings ?? throw new ArgumentNullException(nameof(semanticBindings)))
            .OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public string BranchedComposedKinematicCandidateId { get; }
    public string BranchedComposedSpatialCandidateId { get; }
    public BranchedContinuousCompositionProvenance Provenance { get; }
    public ReadOnlyCollection<SemanticDofBinding> SemanticBindings { get; }
    public BranchedContinuousCompositionMetrics Metrics { get; }
}

public sealed class MechanismArtifact
{
    public const string ExpectedFormat = "gear-invest.mechanism";
    public const string CurrentFormatVersion = "0.1";

    public MechanismArtifact(
        string candidateId,
        string artifactHash,
        ArtifactMetadata metadata,
        GenerationCandidate candidate)
    {
        CandidateId = candidateId ?? throw new ArgumentNullException(nameof(candidateId));
        ArtifactHash = artifactHash ?? throw new ArgumentNullException(nameof(artifactHash));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    }

    public string Format => ExpectedFormat;

    public string FormatVersion => CurrentFormatVersion;

    public string CandidateId { get; }

    public string ArtifactHash { get; }

    public ArtifactMetadata Metadata { get; }

    public GenerationCandidate Candidate { get; }
}

public sealed class ArtifactWriteResult
{
    public ArtifactWriteResult(MechanismArtifact artifact, byte[] bytes)
    {
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public MechanismArtifact Artifact { get; }

    public byte[] Bytes { get; }
}

public sealed class ArtifactIdentityVerification
{
    public ArtifactIdentityVerification(
        string computedCandidateId,
        string computedArtifactHash,
        bool candidateIdMatches,
        bool artifactHashMatches)
    {
        ComputedCandidateId = computedCandidateId;
        ComputedArtifactHash = computedArtifactHash;
        CandidateIdMatches = candidateIdMatches;
        ArtifactHashMatches = artifactHashMatches;
    }

    public string ComputedCandidateId { get; }

    public string ComputedArtifactHash { get; }

    public bool CandidateIdMatches { get; }

    public bool ArtifactHashMatches { get; }
}

public sealed class ArtifactFormatException : FormatException
{
    public ArtifactFormatException(string message)
        : base(message)
    {
    }

    public ArtifactFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
