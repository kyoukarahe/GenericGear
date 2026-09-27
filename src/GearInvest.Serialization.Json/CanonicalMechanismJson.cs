using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalMechanismJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    public ArtifactWriteResult Write(GenerationCandidate candidate, ArtifactMetadata? metadata = null)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        metadata ??= ArtifactMetadata.CreateDefault(candidate);
        var candidateId = ComputeCandidateId(candidate);
        var provisional = new MechanismArtifact(candidateId, string.Empty, metadata, candidate);
        var hashInput = WriteDocument(provisional, includeArtifactHash: false);
        var artifactHash = Hash(hashInput);
        var artifact = new MechanismArtifact(candidateId, artifactHash, metadata, candidate);
        return new ArtifactWriteResult(artifact, WriteDocument(artifact, includeArtifactHash: true));
    }

    public ArtifactWriteResult Write(MechanismArtifact artifact)
    {
        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        return Write(artifact.Candidate, artifact.Metadata);
    }

    public MechanismArtifact Read(byte[] utf8Json)
    {
        if (utf8Json is null)
        {
            throw new ArgumentNullException(nameof(utf8Json));
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
            });
            EnsureNoDuplicateProperties(document.RootElement, "$" );
            var root = RequireObject(document.RootElement, "$" );

            var format = RequireString(root, "format", "$" );
            if (!StringComparer.Ordinal.Equals(format, MechanismArtifact.ExpectedFormat))
            {
                throw new ArtifactFormatException($"Unsupported artifact format '{format}'.");
            }

            var formatVersion = RequireString(root, "formatVersion", "$" );
            ValidateVersion(formatVersion);

            var candidateId = RequireString(root, "candidateId", "$" );
            var artifactHash = RequireString(root, "artifactHash", "$" );
            var metadata = ReadMetadata(root);
            var kinematicData = ReadKinematic(RequireProperty(root, "kinematic", "$"));
            var spatial = ReadSpatial(RequireProperty(root, "spatial", "$"));
            var playback = ReadPlayback(RequireProperty(root, "resolvedPlayback", "$"));
            var validation = ReadValidation(RequireProperty(root, "validation", "$"));
            var candidate = new GenerationCandidate(
                metadata.Source.SpecificationId,
                kinematicData.Specification,
                kinematicData.Solution,
                spatial,
                playback,
                validation);
            return new MechanismArtifact(candidateId, artifactHash, metadata, candidate);
        }
        catch (ArtifactFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ArtifactFormatException("The mechanism artifact is not valid canonical JSON.", exception);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or DivideByZeroException)
        {
            throw new ArtifactFormatException("The mechanism artifact contains an invalid canonical value.", exception);
        }
    }

    public string ComputeCandidateId(GenerationCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("kinematic");
            WriteKinematic(writer, candidate.Kinematic, candidate.Solution);
            writer.WritePropertyName("spatial");
            WriteSpatial(writer, candidate.Spatial);
            writer.WriteEndObject();
        }

        return Hash(stream.ToArray());
    }

    public string ComputeArtifactHash(MechanismArtifact artifact)
    {
        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        return Hash(WriteDocument(artifact, includeArtifactHash: false));
    }

    public ArtifactIdentityVerification VerifyIdentity(MechanismArtifact artifact)
    {
        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        var candidateId = ComputeCandidateId(artifact.Candidate);
        var artifactHash = ComputeArtifactHash(artifact);
        return new ArtifactIdentityVerification(
            candidateId,
            artifactHash,
            StringComparer.Ordinal.Equals(candidateId, artifact.CandidateId),
            StringComparer.Ordinal.Equals(artifactHash, artifact.ArtifactHash));
    }

    private static byte[] WriteDocument(MechanismArtifact artifact, bool includeArtifactHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", MechanismArtifact.ExpectedFormat);
            writer.WriteString("formatVersion", MechanismArtifact.CurrentFormatVersion);
            writer.WriteString("candidateId", artifact.CandidateId);
            if (includeArtifactHash)
            {
                writer.WriteString("artifactHash", artifact.ArtifactHash);
            }

            WriteSource(writer, artifact.Metadata.Source);
            WriteGenerator(writer, artifact.Metadata.Generator);
            if (artifact.Metadata.Generated is not null)
            {
                WriteGeneration(writer, artifact.Metadata.Generated.Provenance);
            }
            if (artifact.Metadata.Composed is not null)
            {
                WriteComposition(writer, artifact.Metadata.Composed);
            }
            if (artifact.Metadata.BranchedComposed is not null)
            {
                WriteBranchedComposition(writer, artifact.Metadata.BranchedComposed);
            }
            writer.WritePropertyName("kinematic");
            WriteKinematic(writer, artifact.Candidate.Kinematic, artifact.Candidate.Solution);
            writer.WritePropertyName("spatial");
            WriteSpatial(writer, artifact.Candidate.Spatial);
            writer.WritePropertyName("resolvedPlayback");
            WritePlayback(writer, artifact.Candidate.ResolvedPlayback);
            if (artifact.Metadata.Generated is not null)
            {
                WriteMetrics(writer, artifact.Metadata.Generated.Metrics);
            }
            writer.WritePropertyName("validation");
            WriteValidation(writer, artifact.Candidate.Validation);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteSource(Utf8JsonWriter writer, ArtifactSourceProvenance source)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("specificationId", source.SpecificationId);
        writer.WriteString("seed", source.Seed);
        writer.WriteString("generationOptions", source.GenerationOptions);
        writer.WriteEndObject();
    }

    private static void WriteGenerator(Utf8JsonWriter writer, GeneratorFingerprint generator)
    {
        writer.WritePropertyName("generator");
        writer.WriteStartObject();
        writer.WriteString("sdkVersion", generator.SdkVersion);
        writer.WriteString("generatorVersion", generator.GeneratorVersion);
        writer.WriteString("backendId", generator.BackendId);
        writer.WriteString("determinismProfile", generator.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteGeneration(
        Utf8JsonWriter writer,
        ExactRatioMechanismCandidateProvenance provenance)
    {
        var request = provenance.GenerationRequest;
        var synthesis = request.SynthesisRequest;
        var layout = request.LayoutRequest;
        writer.WritePropertyName("generation");
        writer.WriteStartObject();
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("requestId", provenance.GenerationRequestId);
        writer.WriteString("canonicalRepresentation", provenance.GenerationRequestCanonicalRepresentation);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WritePropertyName("caps");
        writer.WriteStartObject();
        writer.WriteNumber("maxKinematicCandidatesToLayout", request.MaxKinematicCandidatesToLayout);
        writer.WriteNumber("maxLayoutsPerKinematicCandidate", request.MaxLayoutsPerKinematicCandidate);
        writer.WriteNumber("maxReturnedMechanisms", request.MaxReturnedMechanisms);
        writer.WriteEndObject();
        writer.WritePropertyName("synthesis");
        writer.WriteStartObject();
        writer.WritePropertyName("targetTransfer");
        WriteRational(writer, synthesis.TargetTransfer);
        writer.WriteNumber("minTeeth", synthesis.MinTeeth);
        writer.WriteNumber("maxTeeth", synthesis.MaxTeeth);
        writer.WriteNumber("minStages", synthesis.MinStages);
        writer.WriteNumber("maxStages", synthesis.MaxStages);
        writer.WriteBoolean("allowCompound", synthesis.AllowCompound);
        writer.WriteNumber("maxReturnedCandidates", synthesis.MaxReturnedCandidates);
        writer.WriteNumber("maxSearchExpansions", synthesis.MaxSearchExpansions);
        writer.WriteString("determinismProfile", synthesis.DeterminismProfile);
        writer.WriteEndObject();
        writer.WritePropertyName("layout");
        writer.WriteStartObject();
        writer.WriteString("rootAxisX", layout.RootAxisX.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("rootAxisY", layout.RootAxisY.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("pitchRadiusTicksPerTooth", layout.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxLayers", layout.MaxLayers);
        writer.WriteNumber("maxReturnedLayouts", layout.MaxReturnedLayouts);
        writer.WriteNumber("maxPlacementExpansions", layout.MaxPlacementExpansions);
        writer.WriteString("clearanceTicks", layout.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("determinismProfile", layout.DeterminismProfile);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("selection");
        writer.WriteStartObject();
        writer.WriteString("kinematicCandidateId", provenance.KinematicCandidateId);
        writer.WriteNumber("kinematicRank", provenance.KinematicRank);
        writer.WriteString("spatialCandidateId", provenance.SpatialCandidateId);
        writer.WriteNumber("spatialRank", provenance.SpatialRank);
        writer.WriteEndObject();

        writer.WritePropertyName("orchestrator");
        WriteBackendFingerprint(writer, provenance.GenerationFingerprint.BackendId,
            provenance.GenerationFingerprint.BackendVersion,
            provenance.GenerationFingerprint.DeterminismProfile);
        writer.WritePropertyName("synthesis");
        writer.WriteStartObject();
        writer.WritePropertyName("backend");
        WriteBackendFingerprint(writer, provenance.SynthesisFingerprint.BackendId,
            provenance.SynthesisFingerprint.BackendVersion,
            provenance.SynthesisFingerprint.DeterminismProfile);
        writer.WritePropertyName("searchSummary");
        WriteKinematicSearchSummary(writer, provenance.SynthesisSearchSummary);
        writer.WriteEndObject();
        writer.WritePropertyName("layout");
        writer.WriteStartObject();
        writer.WriteString("requestId", provenance.LayoutRequestId);
        writer.WriteString("canonicalRepresentation", provenance.LayoutRequestCanonicalRepresentation);
        writer.WritePropertyName("backend");
        WriteBackendFingerprint(writer, provenance.LayoutFingerprint.BackendId,
            provenance.LayoutFingerprint.BackendVersion,
            provenance.LayoutFingerprint.DeterminismProfile);
        writer.WritePropertyName("searchSummary");
        WriteSpatialSearchSummary(writer, provenance.LayoutSearchSummary);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteBackendFingerprint(
        Utf8JsonWriter writer,
        string backendId,
        string backendVersion,
        string determinismProfile)
    {
        writer.WriteStartObject();
        writer.WriteString("backendId", backendId);
        writer.WriteString("backendVersion", backendVersion);
        writer.WriteString("determinismProfile", determinismProfile);
        writer.WriteEndObject();
    }

    private static void WriteComposition(Utf8JsonWriter writer, ComposedMechanismMetadata metadata)
    {
        var provenance = metadata.Provenance;
        var request = provenance.Request;
        writer.WritePropertyName("composition");
        writer.WriteStartObject();
        writer.WriteString("compiledPlanId", provenance.CompiledPlanId);
        writer.WriteString("composedKinematicCandidateId", metadata.ComposedKinematicCandidateId);
        writer.WriteString("composedSpatialCandidateId", metadata.ComposedSpatialCandidateId);
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("requestId", provenance.CompositionRequestId);
        writer.WriteString("canonicalRepresentation", provenance.RequestCanonicalRepresentation);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WriteNumber("maxLayers", request.MaxLayers);
        writer.WriteString("clearanceTicks", request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxCompositionExpansions", request.MaxCompositionExpansions);
        writer.WriteNumber("maxReturnedCompositions", request.MaxReturnedCompositions);
        writer.WriteString("pitchRadiusTicksPerTooth", provenance.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
        writer.WritePropertyName("backend");
        WriteBackendFingerprint(
            writer,
            provenance.BackendFingerprint.BackendId,
            provenance.BackendFingerprint.BackendVersion,
            provenance.BackendFingerprint.DeterminismProfile);
        writer.WritePropertyName("inputEdges");
        writer.WriteStartArray();
        foreach (var edge in provenance.InputEdges.OrderBy(item => item.Index))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", edge.Index);
            writer.WriteString("compiledRequirementId", edge.CompiledRequirementId);
            writer.WriteString("semanticFromNodeId", edge.SemanticFromNodeId);
            writer.WriteString("semanticToNodeId", edge.SemanticToNodeId);
            writer.WritePropertyName("signedTargetTransfer");
            WriteRational(writer, edge.SignedTargetTransfer);
            writer.WritePropertyName("exactPhaseRelation");
            WriteRational(writer, edge.ExactPhaseRelation);
            writer.WriteString("generationRequestId", edge.GenerationRequestId);
            writer.WriteString("kinematicCandidateId", edge.KinematicCandidateId);
            writer.WriteString("spatialCandidateId", edge.SpatialCandidateId);
            writer.WriteString("candidateId", edge.CandidateId);
            writer.WriteString("artifactHash", edge.ArtifactHash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("selection");
        writer.WriteStartObject();
        writer.WriteNumber("rotationDegrees", (int)provenance.SelectedRotation);
        writer.WritePropertyName("layerMapping");
        writer.WriteStartArray();
        foreach (var mapping in provenance.SelectedLayerMapping.OrderBy(item => item.LocalLayer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("localLayer", mapping.LocalLayer);
            writer.WriteNumber("globalLayer", mapping.GlobalLayer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WritePropertyName("searchSummary");
        WriteCompositionSearchSummary(writer, provenance.SearchSummary);
        writer.WritePropertyName("semanticBindings");
        writer.WriteStartArray();
        foreach (var binding in metadata.SemanticBindings.OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("semanticNodeId", binding.SemanticNodeId);
            writer.WriteString("dofId", binding.DofId);
            writer.WriteString("role", SemanticBindingRoleToWire(binding.Role));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("metrics");
        WriteCompositionMetrics(writer, metadata.Metrics);
        writer.WriteEndObject();
    }

    private static void WriteBranchedComposition(
        Utf8JsonWriter writer,
        BranchedComposedMechanismMetadata metadata)
    {
        var provenance = metadata.Provenance;
        var request = provenance.Request;
        writer.WritePropertyName("composition");
        writer.WriteStartObject();
        writer.WriteString("topologyKind", provenance.TopologyKind);
        writer.WriteString("compiledPlanId", provenance.CompiledPlanId);
        writer.WriteString(
            "branchedComposedKinematicCandidateId",
            metadata.BranchedComposedKinematicCandidateId);
        writer.WriteString(
            "branchedComposedSpatialCandidateId",
            metadata.BranchedComposedSpatialCandidateId);
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("requestId", provenance.BranchedCompositionRequestId);
        writer.WriteString("canonicalRepresentation", provenance.RequestCanonicalRepresentation);
        writer.WriteString("anchorRequirementId", request.AnchorRequirementId);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WriteNumber("maxLayers", request.MaxLayers);
        writer.WriteString("clearanceTicks", request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxCompositionExpansions", request.MaxCompositionExpansions);
        writer.WriteNumber("maxReturnedCompositions", request.MaxReturnedCompositions);
        writer.WriteString("pitchRadiusTicksPerTooth", provenance.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture));
        writer.WritePropertyName("branchHints");
        writer.WriteStartArray();
        foreach (var hint in request.BranchHints)
        {
            writer.WriteStartObject();
            writer.WriteString("compiledRequirementId", hint.CompiledRequirementId);
            writer.WriteString("semanticOutputNodeId", hint.SemanticOutputNodeId);
            writer.WriteString("strength", BranchHintStrengthToWire(hint.Strength));
            if (hint.PreferredDirection.HasValue)
            {
                writer.WriteString("preferredDirection", CardinalDirectionToWire(hint.PreferredDirection.Value));
            }
            if (hint.PreferredSharedInputLayer.HasValue)
            {
                writer.WriteNumber("preferredSharedInputLayer", hint.PreferredSharedInputLayer.Value);
            }
            if (hint.PreferredOutputAnchor is not null)
            {
                writer.WritePropertyName("preferredOutputAnchor");
                WriteBranchOutputAnchor(writer, hint.PreferredOutputAnchor);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WritePropertyName("backend");
        WriteBackendFingerprint(
            writer,
            provenance.BackendFingerprint.BackendId,
            provenance.BackendFingerprint.BackendVersion,
            provenance.BackendFingerprint.DeterminismProfile);
        writer.WritePropertyName("inputEdges");
        writer.WriteStartArray();
        foreach (var edge in provenance.InputEdges.OrderBy(item => item.Index))
        {
            WriteCompositionInputEdge(writer, edge);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("selection");
        writer.WriteStartObject();
        writer.WritePropertyName("branches");
        writer.WriteStartArray();
        foreach (var branch in provenance.SelectedBranches)
        {
            writer.WriteStartObject();
            writer.WriteString("compiledRequirementId", branch.CompiledRequirementId);
            writer.WriteString("semanticOutputNodeId", branch.SemanticOutputNodeId);
            writer.WriteNumber("rotationDegrees", (int)branch.Rotation);
            writer.WriteString("actualDirection", CardinalDirectionToWire(branch.ActualDirection));
            writer.WriteNumber("sharedInputGlobalLayer", branch.SharedInputGlobalLayer);
            writer.WritePropertyName("outputAnchor");
            WriteBranchOutputAnchor(writer, branch.OutputAnchor);
            writer.WritePropertyName("layerMapping");
            writer.WriteStartArray();
            foreach (var mapping in branch.LayerMapping)
            {
                writer.WriteStartObject();
                writer.WriteNumber("localLayer", mapping.LocalLayer);
                writer.WriteNumber("globalLayer", mapping.GlobalLayer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WritePropertyName("searchSummary");
        WriteBranchedCompositionSearchSummary(writer, provenance.SearchSummary);
        writer.WritePropertyName("semanticBindings");
        writer.WriteStartArray();
        foreach (var binding in metadata.SemanticBindings.OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("semanticNodeId", binding.SemanticNodeId);
            writer.WriteString("dofId", binding.DofId);
            writer.WriteString("role", SemanticBindingRoleToWire(binding.Role));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("metrics");
        WriteBranchedCompositionMetrics(writer, metadata.Metrics);
        writer.WriteEndObject();
    }

    private static void WriteCompositionInputEdge(
        Utf8JsonWriter writer,
        CompositionInputEdgeProvenance edge)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", edge.Index);
        writer.WriteString("compiledRequirementId", edge.CompiledRequirementId);
        writer.WriteString("semanticFromNodeId", edge.SemanticFromNodeId);
        writer.WriteString("semanticToNodeId", edge.SemanticToNodeId);
        writer.WritePropertyName("signedTargetTransfer");
        WriteRational(writer, edge.SignedTargetTransfer);
        writer.WritePropertyName("exactPhaseRelation");
        WriteRational(writer, edge.ExactPhaseRelation);
        writer.WriteString("generationRequestId", edge.GenerationRequestId);
        writer.WriteString("kinematicCandidateId", edge.KinematicCandidateId);
        writer.WriteString("spatialCandidateId", edge.SpatialCandidateId);
        writer.WriteString("candidateId", edge.CandidateId);
        writer.WriteString("artifactHash", edge.ArtifactHash);
        writer.WriteEndObject();
    }

    private static void WriteBranchOutputAnchor(Utf8JsonWriter writer, BranchOutputAnchor anchor)
    {
        writer.WriteStartObject();
        writer.WriteString("x", anchor.X.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("y", anchor.Y.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteBranchedCompositionSearchSummary(
        Utf8JsonWriter writer,
        BranchedContinuousCompositionSearchSummary summary)
    {
        writer.WriteStartObject();
        writer.WriteNumber("branchCount", summary.BranchCount);
        writer.WriteNumber("rotationTuples", summary.RotationTuples);
        writer.WriteNumber("layerMappingTuples", summary.LayerMappingTuples);
        writer.WriteNumber("compositionExpansions", summary.CompositionExpansions);
        writer.WriteNumber("rawFeasibleCandidates", summary.RawFeasibleCandidates);
        writer.WriteNumber("hintFilteredCandidates", summary.HintFilteredCandidates);
        writer.WriteNumber("hintRankedCandidates", summary.HintRankedCandidates);
        writer.WriteNumber("deduplicatedCandidates", summary.DeduplicatedCandidates);
        writer.WriteNumber("returnedCandidates", summary.ReturnedCandidates);
        writer.WriteBoolean("resultTruncated", summary.ResultTruncated);
        writer.WriteBoolean("searchComplete", summary.SearchComplete);
        writer.WriteEndObject();
    }

    private static void WriteBranchedCompositionMetrics(
        Utf8JsonWriter writer,
        BranchedContinuousCompositionMetrics metrics)
    {
        writer.WriteStartObject();
        writer.WriteNumber("requiredHintSatisfactionCount", metrics.RequiredHintSatisfactionCount);
        writer.WriteNumber("softDirectionMissCount", metrics.SoftDirectionMissCount);
        writer.WriteNumber("softLayerMissCount", metrics.SoftLayerMissCount);
        writer.WriteString("outputAnchorPenalty", metrics.OutputAnchorPenalty.ToString(CultureInfo.InvariantCulture));
        writer.WritePropertyName("mechanical");
        WriteCompositionMetrics(writer, metrics.Mechanical);
        writer.WriteEndObject();
    }

    private static void WriteCompositionSearchSummary(
        Utf8JsonWriter writer,
        ContinuousPlanCompositionSearchSummary summary)
    {
        writer.WriteStartObject();
        writer.WriteNumber("rotationAssignments", summary.RotationAssignments);
        writer.WriteNumber("layerMappings", summary.LayerMappings);
        writer.WriteNumber("compositionExpansions", summary.CompositionExpansions);
        writer.WriteNumber("rawFeasibleCandidates", summary.RawFeasibleCandidates);
        writer.WriteNumber("deduplicatedCandidates", summary.DeduplicatedCandidates);
        writer.WriteNumber("returnedCandidates", summary.ReturnedCandidates);
        writer.WriteBoolean("resultTruncated", summary.ResultTruncated);
        writer.WriteBoolean("searchComplete", summary.SearchComplete);
        writer.WriteEndObject();
    }

    private static void WriteCompositionMetrics(Utf8JsonWriter writer, ContinuousPlanCompositionMetrics metrics)
    {
        writer.WriteStartObject();
        writer.WriteNumber("usedLayerCount", metrics.UsedLayerCount);
        writer.WriteNumber("dofCount", metrics.DofCount);
        writer.WriteNumber("axisCount", metrics.AxisCount);
        writer.WriteNumber("bodyCount", metrics.BodyCount);
        writer.WriteNumber("contactCount", metrics.ContactCount);
        writer.WriteString("boundingWidth", metrics.BoundingWidth.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingHeight", metrics.BoundingHeight.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("unrelatedSameLayerPairChecks", metrics.UnrelatedSameLayerPairChecks);
        writer.WriteEndObject();
    }

    private static void WriteKinematicSearchSummary(Utf8JsonWriter writer, KinematicSearchSummary summary)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("stageOptionsConsidered");
        writer.WriteStartArray();
        foreach (var value in summary.StageOptionsConsidered)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
        writer.WriteNumber("searchExpansions", summary.SearchExpansions);
        writer.WriteNumber("rawMatches", summary.RawMatches);
        writer.WriteNumber("deduplicatedCandidates", summary.DeduplicatedCandidates);
        writer.WriteNumber("returnedCandidates", summary.ReturnedCandidates);
        writer.WriteBoolean("resultTruncated", summary.ResultTruncated);
        writer.WriteBoolean("searchComplete", summary.SearchComplete);
        writer.WriteEndObject();
    }

    private static void WriteSpatialSearchSummary(Utf8JsonWriter writer, SpatialLayoutSearchSummary summary)
    {
        writer.WriteStartObject();
        writer.WriteNumber("directionAssignmentsConsidered", summary.DirectionAssignmentsConsidered);
        writer.WriteNumber("layerAssignmentsConsidered", summary.LayerAssignmentsConsidered);
        writer.WriteNumber("placementExpansions", summary.PlacementExpansions);
        writer.WriteNumber("rawFeasibleLayouts", summary.RawFeasibleLayouts);
        writer.WriteNumber("deduplicatedLayouts", summary.DeduplicatedLayouts);
        writer.WriteNumber("returnedLayouts", summary.ReturnedLayouts);
        writer.WriteBoolean("resultTruncated", summary.ResultTruncated);
        writer.WriteBoolean("searchComplete", summary.SearchComplete);
        writer.WriteEndObject();
    }

    private static void WriteKinematic(
        Utf8JsonWriter writer,
        KinematicSpecification specification,
        KinematicSolution solution)
    {
        writer.WriteStartObject();
        writer.WriteString("rootDofId", specification.RootDofId);
        writer.WritePropertyName("dofs");
        writer.WriteStartArray();
        foreach (var dof in specification.Dofs.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", dof.Id);
            writer.WriteString("kind", "rotational");
            writer.WriteBoolean("prescribed", dof.IsPrescribed);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("couplings");
        writer.WriteStartArray();
        foreach (var coupling in specification.Couplings.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", coupling.Id);
            writer.WriteString("kind", "externalGear");
            writer.WriteString("driverDofId", coupling.DriverDofId);
            writer.WriteString("drivenDofId", coupling.DrivenDofId);
            writer.WriteNumber("driverTeeth", coupling.DriverTeeth);
            writer.WriteNumber("drivenTeeth", coupling.DrivenTeeth);
            writer.WritePropertyName("phaseOffset");
            WriteRational(writer, coupling.PhaseOffset);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("solution");
        writer.WriteStartArray();
        foreach (var state in solution.States.OrderBy(item => item.DofId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("dofId", state.DofId);
            writer.WritePropertyName("coefficient");
            WriteRational(writer, state.Coefficient);
            writer.WritePropertyName("phaseOffset");
            WriteRational(writer, state.PhaseOffset);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSpatial(Utf8JsonWriter writer, SpatialMechanism spatial)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("axes");
        writer.WriteStartArray();
        foreach (var axis in spatial.Axes.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", axis.Id);
            writer.WriteString("x", axis.X.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("y", axis.Y.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("bodies");
        writer.WriteStartArray();
        foreach (var body in spatial.Bodies.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", body.Id);
            writer.WriteString("kind", BodyKindToWire(body.Kind));
            writer.WriteString("axisId", body.AxisId);
            writer.WriteString("dofId", body.DofId);
            writer.WriteNumber("layer", body.Layer);
            writer.WriteNumber("toothCount", body.ToothCount);
            writer.WriteString("pitchRadius", body.PitchRadius.ToString(CultureInfo.InvariantCulture));
            writer.WritePropertyName("exactMountingPhase");
            WriteRational(writer, body.ExactMountingPhase);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("contacts");
        writer.WriteStartArray();
        foreach (var contact in spatial.Contacts.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", contact.Id);
            writer.WriteString("kind", ContactKindToWire(contact.Kind));
            writer.WriteString("constraintId", contact.ConstraintId);
            writer.WriteString("bodyAId", contact.BodyAId);
            writer.WriteString("bodyBId", contact.BodyBId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePlayback(Utf8JsonWriter writer, ResolvedPlayback playback)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("drivers");
        writer.WriteStartArray();
        foreach (var driver in playback.Drivers.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", driver.Id);
            writer.WriteString("rootDofId", driver.RootDofId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("channels");
        writer.WriteStartArray();
        foreach (var channel in playback.Channels.OrderBy(item => item.DofId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("dofId", channel.DofId);
            writer.WriteString("driverId", channel.DriverId);
            writer.WritePropertyName("exactCoefficient");
            WriteRational(writer, channel.ExactCoefficient);
            writer.WritePropertyName("exactPhaseOffset");
            WriteRational(writer, channel.ExactPhaseOffset);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("bodyBindings");
        writer.WriteStartArray();
        foreach (var binding in playback.BodyBindings.OrderBy(item => item.BodyId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("bodyId", binding.BodyId);
            writer.WriteString("dofId", binding.DofId);
            writer.WritePropertyName("exactMountingPhase");
            WriteRational(writer, binding.ExactMountingPhase);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValidation(Utf8JsonWriter writer, ValidationBundle validation)
    {
        writer.WriteStartObject();
        writer.WriteString("status", validation.IsValid ? "valid" : "invalid");
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in validation.Diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", SeverityToWire(diagnostic.Severity));
            writer.WriteString("message", diagnostic.Message);
            if (diagnostic.SubjectId is not null)
            {
                writer.WriteString("subjectId", diagnostic.SubjectId);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMetrics(Utf8JsonWriter writer, ExactRatioMechanismMetrics metrics)
    {
        writer.WritePropertyName("metrics");
        writer.WriteStartObject();
        writer.WritePropertyName("kinematic");
        writer.WriteStartObject();
        writer.WriteNumber("stageCount", metrics.Kinematic.StageCount);
        writer.WriteNumber("intermediateDofCount", metrics.Kinematic.IntermediateDofCount);
        writer.WriteNumber("compoundIntermediateCount", metrics.Kinematic.CompoundIntermediateCount);
        writer.WriteNumber("idlerCompatibleIntermediateCount", metrics.Kinematic.IdlerCompatibleIntermediateCount);
        writer.WriteNumber("estimatedGearBodyCount", metrics.Kinematic.EstimatedGearBodyCount);
        writer.WriteNumber("maximumToothCount", metrics.Kinematic.MaximumToothCount);
        writer.WriteNumber("minimumToothCount", metrics.Kinematic.MinimumToothCount);
        writer.WriteNumber("totalToothCount", metrics.Kinematic.TotalToothCount);
        writer.WriteEndObject();
        writer.WritePropertyName("spatial");
        writer.WriteStartObject();
        writer.WriteNumber("usedLayerCount", metrics.Spatial.UsedLayerCount);
        writer.WriteNumber("bodyCount", metrics.Spatial.BodyCount);
        writer.WriteNumber("contactCount", metrics.Spatial.ContactCount);
        writer.WriteString("boundingWidth", metrics.Spatial.BoundingWidth.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingHeight", metrics.Spatial.BoundingHeight.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("directionChangeCount", metrics.Spatial.DirectionChangeCount);
        writer.WriteEndObject();
        writer.WritePropertyName("orchestration");
        writer.WriteStartObject();
        writer.WriteNumber("kinematicRank", metrics.KinematicRank);
        writer.WriteNumber("spatialRank", metrics.SpatialRank);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteRational(Utf8JsonWriter writer, Rational value)
    {
        writer.WriteStartObject();
        writer.WriteString("numerator", value.Numerator.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("denominator", value.Denominator.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static ArtifactMetadata ReadMetadata(JsonElement root)
    {
        var sourceElement = RequireProperty(root, "source", "$");
        var generatorElement = RequireProperty(root, "generator", "$");
        var source = RequireObject(sourceElement, "$.source");
        var generator = RequireObject(generatorElement, "$.generator");
        GeneratedMechanismMetadata? generated = null;
        ComposedMechanismMetadata? composed = null;
        BranchedComposedMechanismMetadata? branchedComposed = null;
        var hasGeneration = root.TryGetProperty("generation", out var generationElement);
        var hasMetrics = root.TryGetProperty("metrics", out var metricsElement);
        var hasComposition = root.TryGetProperty("composition", out var compositionElement);
        if (hasGeneration != hasMetrics)
        {
            throw new ArtifactFormatException("Generated mechanism artifacts must contain both generation and metrics sections.");
        }

        if (hasGeneration)
        {
            generated = ReadGeneratedMetadata(generationElement, metricsElement);
        }

        if (hasGeneration && hasComposition)
        {
            throw new ArtifactFormatException("An artifact cannot contain both generation and composition metadata.");
        }

        if (hasComposition)
        {
            var composition = RequireObject(compositionElement, "$.composition");
            if (composition.TryGetProperty("topologyKind", out var topologyKindElement))
            {
                var topologyKind = RequireStringValue(topologyKindElement, "$.composition.topologyKind");
                if (!StringComparer.Ordinal.Equals(
                        topologyKind,
                        BranchedContinuousCompositionContract.TopologyKind))
                {
                    throw new ArtifactFormatException(
                        "Unsupported composition topologyKind '" + topologyKind + "'.");
                }

                branchedComposed = ReadBranchedComposedMetadata(composition);
            }
            else
            {
                composed = ReadComposedMetadata(composition);
            }
        }

        return new ArtifactMetadata(
            new ArtifactSourceProvenance(
                RequireString(source, "specificationId", "$.source"),
                RequireString(source, "seed", "$.source"),
                RequireString(source, "generationOptions", "$.source")),
            new GeneratorFingerprint(
                RequireString(generator, "sdkVersion", "$.generator"),
                RequireString(generator, "generatorVersion", "$.generator"),
                RequireString(generator, "backendId", "$.generator"),
                RequireString(generator, "determinismProfile", "$.generator")),
            generated,
            composed,
            branchedComposed);
    }

    private static GeneratedMechanismMetadata ReadGeneratedMetadata(
        JsonElement generationElement,
        JsonElement metricsElement)
    {
        var generation = RequireObject(generationElement, "$.generation");
        var requestValue = RequireObject(RequireProperty(generation, "request", "$.generation"), "$.generation.request");
        var caps = RequireObject(RequireProperty(requestValue, "caps", "$.generation.request"), "$.generation.request.caps");
        var synthesisValue = RequireObject(RequireProperty(requestValue, "synthesis", "$.generation.request"), "$.generation.request.synthesis");
        var layoutValue = RequireObject(RequireProperty(requestValue, "layout", "$.generation.request"), "$.generation.request.layout");
        var synthesisRequest = new ExactRatioSynthesisRequest(
            ReadRational(RequireProperty(synthesisValue, "targetTransfer", "$.generation.request.synthesis"), "$.generation.request.synthesis.targetTransfer"),
            RequireInt32(synthesisValue, "minTeeth", "$.generation.request.synthesis"),
            RequireInt32(synthesisValue, "maxTeeth", "$.generation.request.synthesis"),
            RequireInt32(synthesisValue, "minStages", "$.generation.request.synthesis"),
            RequireInt32(synthesisValue, "maxStages", "$.generation.request.synthesis"),
            RequireBoolean(synthesisValue, "allowCompound", "$.generation.request.synthesis"),
            RequireInt32(synthesisValue, "maxReturnedCandidates", "$.generation.request.synthesis"),
            RequireInt64(synthesisValue, "maxSearchExpansions", "$.generation.request.synthesis"),
            RequireString(synthesisValue, "determinismProfile", "$.generation.request.synthesis"));
        var layoutRequest = new SpatialLayoutRequest(
            ParseBigInteger(RequireString(layoutValue, "rootAxisX", "$.generation.request.layout"), "$.generation.request.layout.rootAxisX"),
            ParseBigInteger(RequireString(layoutValue, "rootAxisY", "$.generation.request.layout"), "$.generation.request.layout.rootAxisY"),
            ParseBigInteger(RequireString(layoutValue, "pitchRadiusTicksPerTooth", "$.generation.request.layout"), "$.generation.request.layout.pitchRadiusTicksPerTooth"),
            RequireInt32(layoutValue, "maxLayers", "$.generation.request.layout"),
            RequireInt32(layoutValue, "maxReturnedLayouts", "$.generation.request.layout"),
            RequireInt64(layoutValue, "maxPlacementExpansions", "$.generation.request.layout"),
            ParseBigInteger(RequireString(layoutValue, "clearanceTicks", "$.generation.request.layout"), "$.generation.request.layout.clearanceTicks"),
            RequireString(layoutValue, "determinismProfile", "$.generation.request.layout"));
        var request = new ExactRatioMechanismGenerationRequest(
            synthesisRequest,
            layoutRequest,
            RequireInt32(caps, "maxKinematicCandidatesToLayout", "$.generation.request.caps"),
            RequireInt32(caps, "maxLayoutsPerKinematicCandidate", "$.generation.request.caps"),
            RequireInt32(caps, "maxReturnedMechanisms", "$.generation.request.caps"),
            RequireString(requestValue, "determinismProfile", "$.generation.request"));

        var selection = RequireObject(RequireProperty(generation, "selection", "$.generation"), "$.generation.selection");
        var orchestrator = RequireObject(RequireProperty(generation, "orchestrator", "$.generation"), "$.generation.orchestrator");
        var synthesis = RequireObject(RequireProperty(generation, "synthesis", "$.generation"), "$.generation.synthesis");
        var synthesisBackend = RequireObject(RequireProperty(synthesis, "backend", "$.generation.synthesis"), "$.generation.synthesis.backend");
        var layout = RequireObject(RequireProperty(generation, "layout", "$.generation"), "$.generation.layout");
        var layoutBackend = RequireObject(RequireProperty(layout, "backend", "$.generation.layout"), "$.generation.layout.backend");
        var provenance = new ExactRatioMechanismCandidateProvenance(
            request,
            RequireString(requestValue, "canonicalRepresentation", "$.generation.request"),
            RequireString(requestValue, "requestId", "$.generation.request"),
            ReadGenerationFingerprint(orchestrator, "$.generation.orchestrator"),
            ReadKinematicFingerprint(synthesisBackend, "$.generation.synthesis.backend"),
            ReadKinematicSearchSummary(RequireProperty(synthesis, "searchSummary", "$.generation.synthesis")),
            RequireString(selection, "kinematicCandidateId", "$.generation.selection"),
            RequireInt32(selection, "kinematicRank", "$.generation.selection"),
            ReadSpatialFingerprint(layoutBackend, "$.generation.layout.backend"),
            RequireString(layout, "canonicalRepresentation", "$.generation.layout"),
            RequireString(layout, "requestId", "$.generation.layout"),
            ReadSpatialSearchSummary(RequireProperty(layout, "searchSummary", "$.generation.layout")),
            RequireString(selection, "spatialCandidateId", "$.generation.selection"),
            RequireInt32(selection, "spatialRank", "$.generation.selection"));
        return new GeneratedMechanismMetadata(provenance, ReadMetrics(metricsElement));
    }

    private static ComposedMechanismMetadata ReadComposedMetadata(JsonElement compositionElement)
    {
        var composition = RequireObject(compositionElement, "$.composition");
        var requestValue = RequireObject(
            RequireProperty(composition, "request", "$.composition"),
            "$.composition.request");
        var request = new ContinuousPlanCompositionRequest(
            RequireInt32(requestValue, "maxLayers", "$.composition.request"),
            ParseBigInteger(
                RequireString(requestValue, "clearanceTicks", "$.composition.request"),
                "$.composition.request.clearanceTicks"),
            RequireInt64(requestValue, "maxCompositionExpansions", "$.composition.request"),
            RequireInt32(requestValue, "maxReturnedCompositions", "$.composition.request"),
            RequireString(requestValue, "determinismProfile", "$.composition.request"));
        var backendValue = RequireObject(
            RequireProperty(composition, "backend", "$.composition"),
            "$.composition.backend");
        var backend = new ContinuousPlanCompositionFingerprint(
            RequireString(backendValue, "backendId", "$.composition.backend"),
            RequireString(backendValue, "backendVersion", "$.composition.backend"),
            RequireString(backendValue, "determinismProfile", "$.composition.backend"));
        var inputs = RequireArray(
                RequireProperty(composition, "inputEdges", "$.composition"),
                "$.composition.inputEdges")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var path = "$.composition.inputEdges[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                var value = RequireObject(item, path);
                return new CompositionInputEdgeProvenance(
                    RequireInt32(value, "index", path),
                    RequireString(value, "compiledRequirementId", path),
                    RequireString(value, "semanticFromNodeId", path),
                    RequireString(value, "semanticToNodeId", path),
                    ReadRational(RequireProperty(value, "signedTargetTransfer", path), path + ".signedTargetTransfer"),
                    ReadRational(RequireProperty(value, "exactPhaseRelation", path), path + ".exactPhaseRelation"),
                    RequireString(value, "generationRequestId", path),
                    RequireString(value, "kinematicCandidateId", path),
                    RequireString(value, "spatialCandidateId", path),
                    RequireString(value, "candidateId", path),
                    RequireString(value, "artifactHash", path));
            })
            .ToList();
        var selection = RequireObject(
            RequireProperty(composition, "selection", "$.composition"),
            "$.composition.selection");
        var rotation = ParseCardinalRotation(
            RequireInt32(selection, "rotationDegrees", "$.composition.selection"));
        var layerMappings = RequireArray(
                RequireProperty(selection, "layerMapping", "$.composition.selection"),
                "$.composition.selection.layerMapping")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var path = "$.composition.selection.layerMapping[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                var value = RequireObject(item, path);
                return new CompositionLayerMapping(
                    RequireInt32(value, "localLayer", path),
                    RequireInt32(value, "globalLayer", path));
            })
            .ToList();
        var searchSummary = ReadCompositionSearchSummary(
            RequireProperty(composition, "searchSummary", "$.composition"));
        var bindings = RequireArray(
                RequireProperty(composition, "semanticBindings", "$.composition"),
                "$.composition.semanticBindings")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var path = "$.composition.semanticBindings[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                var value = RequireObject(item, path);
                return new SemanticDofBinding(
                    RequireString(value, "semanticNodeId", path),
                    RequireString(value, "dofId", path),
                    ParseSemanticBindingRole(RequireString(value, "role", path)));
            })
            .ToList();
        var provenance = new ContinuousPlanCompositionProvenance(
            RequireString(composition, "compiledPlanId", "$.composition"),
            request,
            RequireString(requestValue, "canonicalRepresentation", "$.composition.request"),
            RequireString(requestValue, "requestId", "$.composition.request"),
            backend,
            ParseBigInteger(
                RequireString(requestValue, "pitchRadiusTicksPerTooth", "$.composition.request"),
                "$.composition.request.pitchRadiusTicksPerTooth"),
            inputs,
            rotation,
            layerMappings,
            searchSummary);
        return new ComposedMechanismMetadata(
            RequireString(composition, "composedKinematicCandidateId", "$.composition"),
            RequireString(composition, "composedSpatialCandidateId", "$.composition"),
            provenance,
            bindings,
            ReadCompositionMetrics(RequireProperty(composition, "metrics", "$.composition")));
    }

    private static BranchedComposedMechanismMetadata ReadBranchedComposedMetadata(
        JsonElement compositionElement)
    {
        var composition = RequireObject(compositionElement, "$.composition");
        var requestValue = RequireObject(
            RequireProperty(composition, "request", "$.composition"),
            "$.composition.request");
        var hints = RequireArray(
                RequireProperty(requestValue, "branchHints", "$.composition.request"),
                "$.composition.request.branchHints")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var path = "$.composition.request.branchHints[" +
                    index.ToString(CultureInfo.InvariantCulture) + "]";
                var value = RequireObject(item, path);
                CardinalDirection? direction = null;
                if (value.TryGetProperty("preferredDirection", out var directionElement))
                {
                    direction = ParseCardinalDirection(RequireStringValue(directionElement, path + ".preferredDirection"));
                }

                int? inputLayer = null;
                if (value.TryGetProperty("preferredSharedInputLayer", out var inputLayerElement))
                {
                    inputLayer = RequireInt32Value(inputLayerElement, path + ".preferredSharedInputLayer");
                }

                BranchOutputAnchor? outputAnchor = null;
                if (value.TryGetProperty("preferredOutputAnchor", out var outputAnchorElement))
                {
                    outputAnchor = ReadBranchOutputAnchor(outputAnchorElement, path + ".preferredOutputAnchor");
                }

                return new BranchPlacementHint(
                    RequireString(value, "compiledRequirementId", path),
                    RequireString(value, "semanticOutputNodeId", path),
                    direction,
                    inputLayer,
                    outputAnchor,
                    ParseBranchHintStrength(RequireString(value, "strength", path)));
            })
            .ToList();
        var request = new BranchedContinuousCompositionRequest(
            RequireString(requestValue, "anchorRequirementId", "$.composition.request"),
            hints,
            RequireInt32(requestValue, "maxLayers", "$.composition.request"),
            ParseBigInteger(
                RequireString(requestValue, "clearanceTicks", "$.composition.request"),
                "$.composition.request.clearanceTicks"),
            RequireInt64(requestValue, "maxCompositionExpansions", "$.composition.request"),
            RequireInt32(requestValue, "maxReturnedCompositions", "$.composition.request"),
            RequireString(requestValue, "determinismProfile", "$.composition.request"));
        var backendValue = RequireObject(
            RequireProperty(composition, "backend", "$.composition"),
            "$.composition.backend");
        var backend = new BranchedContinuousCompositionFingerprint(
            RequireString(backendValue, "backendId", "$.composition.backend"),
            RequireString(backendValue, "backendVersion", "$.composition.backend"),
            RequireString(backendValue, "determinismProfile", "$.composition.backend"));
        var inputs = RequireArray(
                RequireProperty(composition, "inputEdges", "$.composition"),
                "$.composition.inputEdges")
            .EnumerateArray()
            .Select((item, index) => ReadCompositionInputEdge(
                item,
                "$.composition.inputEdges[" + index.ToString(CultureInfo.InvariantCulture) + "]"))
            .ToList();
        var selection = RequireObject(
            RequireProperty(composition, "selection", "$.composition"),
            "$.composition.selection");
        var branches = RequireArray(
                RequireProperty(selection, "branches", "$.composition.selection"),
                "$.composition.selection.branches")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var path = "$.composition.selection.branches[" +
                    index.ToString(CultureInfo.InvariantCulture) + "]";
                var value = RequireObject(item, path);
                var mappings = RequireArray(
                        RequireProperty(value, "layerMapping", path),
                        path + ".layerMapping")
                    .EnumerateArray()
                    .Select((mappingItem, mappingIndex) =>
                    {
                        var mappingPath = path + ".layerMapping[" +
                            mappingIndex.ToString(CultureInfo.InvariantCulture) + "]";
                        var mapping = RequireObject(mappingItem, mappingPath);
                        return new CompositionLayerMapping(
                            RequireInt32(mapping, "localLayer", mappingPath),
                            RequireInt32(mapping, "globalLayer", mappingPath));
                    })
                    .ToList();
                return new BranchedBranchPlacement(
                    RequireString(value, "compiledRequirementId", path),
                    RequireString(value, "semanticOutputNodeId", path),
                    ParseCardinalRotation(RequireInt32(value, "rotationDegrees", path)),
                    ParseCardinalDirection(RequireString(value, "actualDirection", path)),
                    RequireInt32(value, "sharedInputGlobalLayer", path),
                    ReadBranchOutputAnchor(
                        RequireProperty(value, "outputAnchor", path),
                        path + ".outputAnchor"),
                    mappings);
            })
            .ToList();
        var searchSummary = ReadBranchedCompositionSearchSummary(
            RequireProperty(composition, "searchSummary", "$.composition"));
        var bindings = RequireArray(
                RequireProperty(composition, "semanticBindings", "$.composition"),
                "$.composition.semanticBindings")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var path = "$.composition.semanticBindings[" +
                    index.ToString(CultureInfo.InvariantCulture) + "]";
                var value = RequireObject(item, path);
                return new SemanticDofBinding(
                    RequireString(value, "semanticNodeId", path),
                    RequireString(value, "dofId", path),
                    ParseSemanticBindingRole(RequireString(value, "role", path)));
            })
            .ToList();
        var provenance = new BranchedContinuousCompositionProvenance(
            RequireString(composition, "topologyKind", "$.composition"),
            RequireString(composition, "compiledPlanId", "$.composition"),
            request,
            RequireString(requestValue, "canonicalRepresentation", "$.composition.request"),
            RequireString(requestValue, "requestId", "$.composition.request"),
            backend,
            ParseBigInteger(
                RequireString(requestValue, "pitchRadiusTicksPerTooth", "$.composition.request"),
                "$.composition.request.pitchRadiusTicksPerTooth"),
            inputs,
            branches,
            searchSummary);
        return new BranchedComposedMechanismMetadata(
            RequireString(composition, "branchedComposedKinematicCandidateId", "$.composition"),
            RequireString(composition, "branchedComposedSpatialCandidateId", "$.composition"),
            provenance,
            bindings,
            ReadBranchedCompositionMetrics(RequireProperty(composition, "metrics", "$.composition")));
    }

    private static CompositionInputEdgeProvenance ReadCompositionInputEdge(
        JsonElement element,
        string path)
    {
        var value = RequireObject(element, path);
        return new CompositionInputEdgeProvenance(
            RequireInt32(value, "index", path),
            RequireString(value, "compiledRequirementId", path),
            RequireString(value, "semanticFromNodeId", path),
            RequireString(value, "semanticToNodeId", path),
            ReadRational(RequireProperty(value, "signedTargetTransfer", path), path + ".signedTargetTransfer"),
            ReadRational(RequireProperty(value, "exactPhaseRelation", path), path + ".exactPhaseRelation"),
            RequireString(value, "generationRequestId", path),
            RequireString(value, "kinematicCandidateId", path),
            RequireString(value, "spatialCandidateId", path),
            RequireString(value, "candidateId", path),
            RequireString(value, "artifactHash", path));
    }

    private static BranchOutputAnchor ReadBranchOutputAnchor(JsonElement element, string path)
    {
        var value = RequireObject(element, path);
        return new BranchOutputAnchor(
            ParseBigInteger(RequireString(value, "x", path), path + ".x"),
            ParseBigInteger(RequireString(value, "y", path), path + ".y"));
    }

    private static BranchedContinuousCompositionSearchSummary ReadBranchedCompositionSearchSummary(
        JsonElement element)
    {
        const string path = "$.composition.searchSummary";
        var value = RequireObject(element, path);
        return new BranchedContinuousCompositionSearchSummary(
            RequireInt32(value, "branchCount", path),
            RequireInt64(value, "rotationTuples", path),
            RequireInt64(value, "layerMappingTuples", path),
            RequireInt64(value, "compositionExpansions", path),
            RequireInt64(value, "rawFeasibleCandidates", path),
            RequireInt64(value, "hintFilteredCandidates", path),
            RequireInt64(value, "hintRankedCandidates", path),
            RequireInt64(value, "deduplicatedCandidates", path),
            RequireInt32(value, "returnedCandidates", path),
            RequireBoolean(value, "resultTruncated", path),
            RequireBoolean(value, "searchComplete", path));
    }

    private static BranchedContinuousCompositionMetrics ReadBranchedCompositionMetrics(JsonElement element)
    {
        const string path = "$.composition.metrics";
        var value = RequireObject(element, path);
        return new BranchedContinuousCompositionMetrics(
            RequireInt32(value, "requiredHintSatisfactionCount", path),
            RequireInt32(value, "softDirectionMissCount", path),
            RequireInt32(value, "softLayerMissCount", path),
            ParseBigInteger(RequireString(value, "outputAnchorPenalty", path), path + ".outputAnchorPenalty"),
            ReadCompositionMetrics(RequireProperty(value, "mechanical", path)));
    }

    private static ContinuousPlanCompositionSearchSummary ReadCompositionSearchSummary(JsonElement element)
    {
        var value = RequireObject(element, "$.composition.searchSummary");
        return new ContinuousPlanCompositionSearchSummary(
            RequireInt64(value, "rotationAssignments", "$.composition.searchSummary"),
            RequireInt64(value, "layerMappings", "$.composition.searchSummary"),
            RequireInt64(value, "compositionExpansions", "$.composition.searchSummary"),
            RequireInt64(value, "rawFeasibleCandidates", "$.composition.searchSummary"),
            RequireInt64(value, "deduplicatedCandidates", "$.composition.searchSummary"),
            RequireInt32(value, "returnedCandidates", "$.composition.searchSummary"),
            RequireBoolean(value, "resultTruncated", "$.composition.searchSummary"),
            RequireBoolean(value, "searchComplete", "$.composition.searchSummary"));
    }

    private static ContinuousPlanCompositionMetrics ReadCompositionMetrics(JsonElement element)
    {
        var value = RequireObject(element, "$.composition.metrics");
        return new ContinuousPlanCompositionMetrics(
            RequireInt32(value, "usedLayerCount", "$.composition.metrics"),
            RequireInt32(value, "dofCount", "$.composition.metrics"),
            RequireInt32(value, "axisCount", "$.composition.metrics"),
            RequireInt32(value, "bodyCount", "$.composition.metrics"),
            RequireInt32(value, "contactCount", "$.composition.metrics"),
            ParseBigInteger(
                RequireString(value, "boundingWidth", "$.composition.metrics"),
                "$.composition.metrics.boundingWidth"),
            ParseBigInteger(
                RequireString(value, "boundingHeight", "$.composition.metrics"),
                "$.composition.metrics.boundingHeight"),
            RequireInt32(value, "unrelatedSameLayerPairChecks", "$.composition.metrics"));
    }

    private static ExactRatioMechanismGenerationFingerprint ReadGenerationFingerprint(JsonElement value, string path) =>
        new(
            RequireString(value, "backendId", path),
            RequireString(value, "backendVersion", path),
            RequireString(value, "determinismProfile", path));

    private static KinematicSynthesisFingerprint ReadKinematicFingerprint(JsonElement value, string path) =>
        new(
            RequireString(value, "backendId", path),
            RequireString(value, "backendVersion", path),
            RequireString(value, "determinismProfile", path));

    private static SpatialLayoutFingerprint ReadSpatialFingerprint(JsonElement value, string path) =>
        new(
            RequireString(value, "backendId", path),
            RequireString(value, "backendVersion", path),
            RequireString(value, "determinismProfile", path));

    private static KinematicSearchSummary ReadKinematicSearchSummary(JsonElement element)
    {
        var value = RequireObject(element, "$.generation.synthesis.searchSummary");
        var stages = RequireArray(RequireProperty(value, "stageOptionsConsidered", "$.generation.synthesis.searchSummary"), "$.generation.synthesis.searchSummary.stageOptionsConsidered")
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var stage)
                ? stage
                : throw new ArtifactFormatException("stageOptionsConsidered must contain 32-bit integers."));
        return new KinematicSearchSummary(
            stages,
            RequireInt64(value, "searchExpansions", "$.generation.synthesis.searchSummary"),
            RequireInt64(value, "rawMatches", "$.generation.synthesis.searchSummary"),
            RequireInt64(value, "deduplicatedCandidates", "$.generation.synthesis.searchSummary"),
            RequireInt32(value, "returnedCandidates", "$.generation.synthesis.searchSummary"),
            RequireBoolean(value, "resultTruncated", "$.generation.synthesis.searchSummary"),
            RequireBoolean(value, "searchComplete", "$.generation.synthesis.searchSummary"));
    }

    private static SpatialLayoutSearchSummary ReadSpatialSearchSummary(JsonElement element)
    {
        var value = RequireObject(element, "$.generation.layout.searchSummary");
        return new SpatialLayoutSearchSummary(
            RequireInt64(value, "directionAssignmentsConsidered", "$.generation.layout.searchSummary"),
            RequireInt64(value, "layerAssignmentsConsidered", "$.generation.layout.searchSummary"),
            RequireInt64(value, "placementExpansions", "$.generation.layout.searchSummary"),
            RequireInt64(value, "rawFeasibleLayouts", "$.generation.layout.searchSummary"),
            RequireInt64(value, "deduplicatedLayouts", "$.generation.layout.searchSummary"),
            RequireInt32(value, "returnedLayouts", "$.generation.layout.searchSummary"),
            RequireBoolean(value, "resultTruncated", "$.generation.layout.searchSummary"),
            RequireBoolean(value, "searchComplete", "$.generation.layout.searchSummary"));
    }

    private static ExactRatioMechanismMetrics ReadMetrics(JsonElement element)
    {
        var value = RequireObject(element, "$.metrics");
        var kinematic = RequireObject(RequireProperty(value, "kinematic", "$.metrics"), "$.metrics.kinematic");
        var spatial = RequireObject(RequireProperty(value, "spatial", "$.metrics"), "$.metrics.spatial");
        var orchestration = RequireObject(RequireProperty(value, "orchestration", "$.metrics"), "$.metrics.orchestration");
        return new ExactRatioMechanismMetrics(
            new KinematicSynthesisMetrics(
                RequireInt32(kinematic, "stageCount", "$.metrics.kinematic"),
                RequireInt32(kinematic, "intermediateDofCount", "$.metrics.kinematic"),
                RequireInt32(kinematic, "compoundIntermediateCount", "$.metrics.kinematic"),
                RequireInt32(kinematic, "idlerCompatibleIntermediateCount", "$.metrics.kinematic"),
                RequireInt32(kinematic, "estimatedGearBodyCount", "$.metrics.kinematic"),
                RequireInt32(kinematic, "maximumToothCount", "$.metrics.kinematic"),
                RequireInt32(kinematic, "minimumToothCount", "$.metrics.kinematic"),
                RequireInt64(kinematic, "totalToothCount", "$.metrics.kinematic")),
            new SpatialLayoutMetrics(
                RequireInt32(spatial, "usedLayerCount", "$.metrics.spatial"),
                RequireInt32(spatial, "bodyCount", "$.metrics.spatial"),
                RequireInt32(spatial, "contactCount", "$.metrics.spatial"),
                ParseBigInteger(RequireString(spatial, "boundingWidth", "$.metrics.spatial"), "$.metrics.spatial.boundingWidth"),
                ParseBigInteger(RequireString(spatial, "boundingHeight", "$.metrics.spatial"), "$.metrics.spatial.boundingHeight"),
                RequireInt32(spatial, "directionChangeCount", "$.metrics.spatial")),
            RequireInt32(orchestration, "kinematicRank", "$.metrics.orchestration"),
            RequireInt32(orchestration, "spatialRank", "$.metrics.orchestration"));
    }

    private static KinematicData ReadKinematic(JsonElement element)
    {
        var value = RequireObject(element, "$.kinematic");
        var rootDofId = RequireString(value, "rootDofId", "$.kinematic");
        var dofs = new List<RotationalDof>();
        foreach (var item in RequireArray(RequireProperty(value, "dofs", "$.kinematic"), "$.kinematic.dofs").EnumerateArray())
        {
            var dof = RequireObject(item, "$.kinematic.dofs[]");
            RequireDiscriminator(dof, "kind", "rotational", "$.kinematic.dofs[]");
            dofs.Add(new RotationalDof(
                RequireString(dof, "id", "$.kinematic.dofs[]"),
                RequireBoolean(dof, "prescribed", "$.kinematic.dofs[]")));
        }

        var couplings = new List<ExternalGearCoupling>();
        foreach (var item in RequireArray(RequireProperty(value, "couplings", "$.kinematic"), "$.kinematic.couplings").EnumerateArray())
        {
            var coupling = RequireObject(item, "$.kinematic.couplings[]");
            RequireDiscriminator(coupling, "kind", "externalGear", "$.kinematic.couplings[]");
            couplings.Add(new ExternalGearCoupling(
                RequireString(coupling, "id", "$.kinematic.couplings[]"),
                RequireString(coupling, "driverDofId", "$.kinematic.couplings[]"),
                RequireString(coupling, "drivenDofId", "$.kinematic.couplings[]"),
                RequireInt32(coupling, "driverTeeth", "$.kinematic.couplings[]"),
                RequireInt32(coupling, "drivenTeeth", "$.kinematic.couplings[]"),
                ReadRational(RequireProperty(coupling, "phaseOffset", "$.kinematic.couplings[]"), "$.kinematic.couplings[].phaseOffset")));
        }

        var states = new List<DofKinematicState>();
        foreach (var item in RequireArray(RequireProperty(value, "solution", "$.kinematic"), "$.kinematic.solution").EnumerateArray())
        {
            var state = RequireObject(item, "$.kinematic.solution[]");
            states.Add(new DofKinematicState(
                RequireString(state, "dofId", "$.kinematic.solution[]"),
                ReadRational(RequireProperty(state, "coefficient", "$.kinematic.solution[]"), "$.kinematic.solution[].coefficient"),
                ReadRational(RequireProperty(state, "phaseOffset", "$.kinematic.solution[]"), "$.kinematic.solution[].phaseOffset")));
        }

        return new KinematicData(
            new KinematicSpecification(rootDofId, dofs, couplings),
            new KinematicSolution(rootDofId, states));
    }

    private static SpatialMechanism ReadSpatial(JsonElement element)
    {
        var value = RequireObject(element, "$.spatial");
        var axes = new List<SpatialAxis>();
        foreach (var item in RequireArray(RequireProperty(value, "axes", "$.spatial"), "$.spatial.axes").EnumerateArray())
        {
            var axis = RequireObject(item, "$.spatial.axes[]");
            axes.Add(new SpatialAxis(
                RequireString(axis, "id", "$.spatial.axes[]"),
                ParseBigInteger(RequireString(axis, "x", "$.spatial.axes[]"), "$.spatial.axes[].x"),
                ParseBigInteger(RequireString(axis, "y", "$.spatial.axes[]"), "$.spatial.axes[].y")));
        }

        var bodies = new List<SpatialBody>();
        foreach (var item in RequireArray(RequireProperty(value, "bodies", "$.spatial"), "$.spatial.bodies").EnumerateArray())
        {
            var body = RequireObject(item, "$.spatial.bodies[]");
            var kind = RequireString(body, "kind", "$.spatial.bodies[]");
            if (!StringComparer.Ordinal.Equals(kind, "gear"))
            {
                throw new ArtifactFormatException($"Unknown required body discriminator '{kind}'.");
            }

            bodies.Add(new SpatialBody(
                RequireString(body, "id", "$.spatial.bodies[]"),
                SpatialBodyKind.Gear,
                RequireString(body, "axisId", "$.spatial.bodies[]"),
                RequireString(body, "dofId", "$.spatial.bodies[]"),
                RequireInt32(body, "layer", "$.spatial.bodies[]"),
                RequireInt32(body, "toothCount", "$.spatial.bodies[]"),
                ParseBigInteger(RequireString(body, "pitchRadius", "$.spatial.bodies[]"), "$.spatial.bodies[].pitchRadius"),
                ReadRational(RequireProperty(body, "exactMountingPhase", "$.spatial.bodies[]"), "$.spatial.bodies[].exactMountingPhase")));
        }

        var contacts = new List<SpatialContact>();
        foreach (var item in RequireArray(RequireProperty(value, "contacts", "$.spatial"), "$.spatial.contacts").EnumerateArray())
        {
            var contact = RequireObject(item, "$.spatial.contacts[]");
            var kind = RequireString(contact, "kind", "$.spatial.contacts[]");
            if (!StringComparer.Ordinal.Equals(kind, "externalGearMesh"))
            {
                throw new ArtifactFormatException($"Unknown required contact discriminator '{kind}'.");
            }

            contacts.Add(new SpatialContact(
                RequireString(contact, "id", "$.spatial.contacts[]"),
                SpatialContactKind.ExternalGearMesh,
                RequireString(contact, "constraintId", "$.spatial.contacts[]"),
                RequireString(contact, "bodyAId", "$.spatial.contacts[]"),
                RequireString(contact, "bodyBId", "$.spatial.contacts[]")));
        }

        return new SpatialMechanism(axes, bodies, contacts);
    }

    private static ResolvedPlayback ReadPlayback(JsonElement element)
    {
        var value = RequireObject(element, "$.resolvedPlayback");
        var drivers = new List<PlaybackDriver>();
        foreach (var item in RequireArray(RequireProperty(value, "drivers", "$.resolvedPlayback"), "$.resolvedPlayback.drivers").EnumerateArray())
        {
            var driver = RequireObject(item, "$.resolvedPlayback.drivers[]");
            drivers.Add(new PlaybackDriver(
                RequireString(driver, "id", "$.resolvedPlayback.drivers[]"),
                RequireString(driver, "rootDofId", "$.resolvedPlayback.drivers[]")));
        }

        var channels = new List<PlaybackChannel>();
        foreach (var item in RequireArray(RequireProperty(value, "channels", "$.resolvedPlayback"), "$.resolvedPlayback.channels").EnumerateArray())
        {
            var channel = RequireObject(item, "$.resolvedPlayback.channels[]");
            channels.Add(new PlaybackChannel(
                RequireString(channel, "dofId", "$.resolvedPlayback.channels[]"),
                RequireString(channel, "driverId", "$.resolvedPlayback.channels[]"),
                ReadRational(RequireProperty(channel, "exactCoefficient", "$.resolvedPlayback.channels[]"), "$.resolvedPlayback.channels[].exactCoefficient"),
                ReadRational(RequireProperty(channel, "exactPhaseOffset", "$.resolvedPlayback.channels[]"), "$.resolvedPlayback.channels[].exactPhaseOffset")));
        }

        var bindings = new List<PlaybackBodyBinding>();
        foreach (var item in RequireArray(RequireProperty(value, "bodyBindings", "$.resolvedPlayback"), "$.resolvedPlayback.bodyBindings").EnumerateArray())
        {
            var binding = RequireObject(item, "$.resolvedPlayback.bodyBindings[]");
            bindings.Add(new PlaybackBodyBinding(
                RequireString(binding, "bodyId", "$.resolvedPlayback.bodyBindings[]"),
                RequireString(binding, "dofId", "$.resolvedPlayback.bodyBindings[]"),
                ReadRational(RequireProperty(binding, "exactMountingPhase", "$.resolvedPlayback.bodyBindings[]"), "$.resolvedPlayback.bodyBindings[].exactMountingPhase")));
        }

        return new ResolvedPlayback(drivers, channels, bindings);
    }

    private static ValidationBundle ReadValidation(JsonElement element)
    {
        var value = RequireObject(element, "$.validation");
        var storedStatus = RequireString(value, "status", "$.validation");
        if (!StringComparer.Ordinal.Equals(storedStatus, "valid") &&
            !StringComparer.Ordinal.Equals(storedStatus, "invalid"))
        {
            throw new ArtifactFormatException($"Unknown validation status '{storedStatus}'.");
        }

        var diagnostics = new List<Diagnostic>();
        foreach (var item in RequireArray(RequireProperty(value, "diagnostics", "$.validation"), "$.validation.diagnostics").EnumerateArray())
        {
            var diagnostic = RequireObject(item, "$.validation.diagnostics[]");
            diagnostics.Add(new Diagnostic(
                RequireString(diagnostic, "code", "$.validation.diagnostics[]"),
                ParseSeverity(RequireString(diagnostic, "severity", "$.validation.diagnostics[]")),
                RequireString(diagnostic, "message", "$.validation.diagnostics[]"),
                OptionalString(diagnostic, "subjectId", "$.validation.diagnostics[]")));
        }

        var bundle = new ValidationBundle(diagnostics);
        if (bundle.IsValid != StringComparer.Ordinal.Equals(storedStatus, "valid"))
        {
            throw new ArtifactFormatException("Stored validation status does not match stored diagnostics.");
        }

        return bundle;
    }

    private static Rational ReadRational(JsonElement element, string path)
    {
        var value = RequireObject(element, path);
        var numerator = ParseBigInteger(RequireString(value, "numerator", path), path + ".numerator");
        var denominator = ParseBigInteger(RequireString(value, "denominator", path), path + ".denominator");
        var rational = new Rational(numerator, denominator);
        if (!StringComparer.Ordinal.Equals(numerator.ToString(CultureInfo.InvariantCulture), RequireString(value, "numerator", path)) ||
            !StringComparer.Ordinal.Equals(denominator.ToString(CultureInfo.InvariantCulture), RequireString(value, "denominator", path)) ||
            rational.Numerator != numerator || rational.Denominator != denominator)
        {
            throw new ArtifactFormatException($"Rational at '{path}' is not in canonical normalized form.");
        }

        return rational;
    }

    private static BigInteger ParseBigInteger(string value, string path)
    {
        if (!BigInteger.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) ||
            !StringComparer.Ordinal.Equals(result.ToString(CultureInfo.InvariantCulture), value))
        {
            throw new ArtifactFormatException($"Value at '{path}' is not a canonical decimal integer string.");
        }

        return result;
    }

    private static JsonElement RequireObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArtifactFormatException($"Expected object at '{path}'.");
        }

        return element;
    }

    private static JsonElement RequireArray(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArtifactFormatException($"Expected array at '{path}'.");
        }

        return element;
    }

    private static JsonElement RequireProperty(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new ArtifactFormatException($"Required property '{path}.{name}' is missing.");
        }

        return value;
    }

    private static string RequireString(JsonElement element, string name, string path)
    {
        var value = RequireProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArtifactFormatException($"Property '{path}.{name}' must be a string.");
        }

        return value.GetString() ?? throw new ArtifactFormatException($"Property '{path}.{name}' cannot be null.");
    }

    private static string RequireStringValue(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ArtifactFormatException($"Value at '{path}' must be a string.");
        }

        return element.GetString() ?? throw new ArtifactFormatException($"Value at '{path}' cannot be null.");
    }

    private static string? OptionalString(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArtifactFormatException($"Property '{path}.{name}' must be a string when present.");
        }

        return value.GetString();
    }

    private static bool RequireBoolean(JsonElement element, string name, string path)
    {
        var value = RequireProperty(element, name, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArtifactFormatException($"Property '{path}.{name}' must be a Boolean.");
        }

        return value.GetBoolean();
    }

    private static int RequireInt32(JsonElement element, string name, string path)
    {
        var value = RequireProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new ArtifactFormatException($"Property '{path}.{name}' must be a 32-bit integer.");
        }

        return result;
    }

    private static int RequireInt32Value(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var result))
        {
            throw new ArtifactFormatException($"Value at '{path}' must be a 32-bit integer.");
        }

        return result;
    }

    private static long RequireInt64(JsonElement element, string name, string path)
    {
        var value = RequireProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new ArtifactFormatException($"Property '{path}.{name}' must be a 64-bit integer.");
        }

        return result;
    }

    private static void RequireDiscriminator(JsonElement element, string name, string expected, string path)
    {
        var actual = RequireString(element, name, path);
        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            throw new ArtifactFormatException($"Unknown required discriminator '{actual}' at '{path}.{name}'.");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ArtifactFormatException($"Duplicate property '{property.Name}' at '{path}'.");
                }

                EnsureNoDuplicateProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                index++;
            }
        }
    }

    private static void ValidateVersion(string formatVersion)
    {
        var separator = formatVersion.IndexOf('.');
        if (separator <= 0 ||
            !int.TryParse(formatVersion.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            throw new ArtifactFormatException($"Invalid format version '{formatVersion}'.");
        }

        if (major != 0)
        {
            throw new ArtifactFormatException($"Unsupported mechanism artifact major version '{major}'.");
        }

        if (!StringComparer.Ordinal.Equals(formatVersion, MechanismArtifact.CurrentFormatVersion))
        {
            throw new ArtifactFormatException($"Unsupported mechanism artifact version '{formatVersion}'.");
        }
    }

    private static string BodyKindToWire(SpatialBodyKind kind)
    {
        return kind == SpatialBodyKind.Gear
            ? "gear"
            : throw new InvalidOperationException($"Unsupported body kind '{kind}'.");
    }

    private static string ContactKindToWire(SpatialContactKind kind)
    {
        return kind == SpatialContactKind.ExternalGearMesh
            ? "externalGearMesh"
            : throw new InvalidOperationException($"Unsupported contact kind '{kind}'.");
    }

    private static string SeverityToWire(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Info => "info",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Error => "error",
            _ => throw new InvalidOperationException($"Unsupported diagnostic severity '{severity}'."),
        };
    }

    private static DiagnosticSeverity ParseSeverity(string value)
    {
        return value switch
        {
            "info" => DiagnosticSeverity.Info,
            "warning" => DiagnosticSeverity.Warning,
            "error" => DiagnosticSeverity.Error,
            _ => throw new ArtifactFormatException($"Unknown diagnostic severity '{value}'."),
        };
    }

    private static string SemanticBindingRoleToWire(SemanticBindingRole role) =>
        role switch
        {
            SemanticBindingRole.Root => "root",
            SemanticBindingRole.IntermediateOutput => "intermediateOutput",
            SemanticBindingRole.FinalOutput => "finalOutput",
            SemanticBindingRole.SharedBranchOutput => "sharedBranchOutput",
            SemanticBindingRole.LeafOutput => "leafOutput",
            _ => throw new InvalidOperationException("Unsupported semantic binding role '" + role + "'."),
        };

    private static SemanticBindingRole ParseSemanticBindingRole(string value) =>
        value switch
        {
            "root" => SemanticBindingRole.Root,
            "intermediateOutput" => SemanticBindingRole.IntermediateOutput,
            "finalOutput" => SemanticBindingRole.FinalOutput,
            "sharedBranchOutput" => SemanticBindingRole.SharedBranchOutput,
            "leafOutput" => SemanticBindingRole.LeafOutput,
            _ => throw new ArtifactFormatException("Unknown semantic binding role '" + value + "'."),
        };

    private static string BranchHintStrengthToWire(BranchHintStrength strength) =>
        strength switch
        {
            BranchHintStrength.Preferred => "preferred",
            BranchHintStrength.Required => "required",
            _ => throw new InvalidOperationException("Unsupported branch hint strength '" + strength + "'."),
        };

    private static BranchHintStrength ParseBranchHintStrength(string value) =>
        value switch
        {
            "preferred" => BranchHintStrength.Preferred,
            "required" => BranchHintStrength.Required,
            _ => throw new ArtifactFormatException("Unknown branch hint strength '" + value + "'."),
        };

    private static string CardinalDirectionToWire(CardinalDirection direction) =>
        direction switch
        {
            CardinalDirection.East => "east",
            CardinalDirection.North => "north",
            CardinalDirection.West => "west",
            CardinalDirection.South => "south",
            _ => throw new InvalidOperationException("Unsupported cardinal direction '" + direction + "'."),
        };

    private static CardinalDirection ParseCardinalDirection(string value) =>
        value switch
        {
            "east" => CardinalDirection.East,
            "north" => CardinalDirection.North,
            "west" => CardinalDirection.West,
            "south" => CardinalDirection.South,
            _ => throw new ArtifactFormatException("Unknown cardinal direction '" + value + "'."),
        };

    private static CardinalRotation ParseCardinalRotation(int value) =>
        value switch
        {
            0 => CardinalRotation.Degrees0,
            90 => CardinalRotation.Degrees90,
            180 => CardinalRotation.Degrees180,
            270 => CardinalRotation.Degrees270,
            _ => throw new ArtifactFormatException("Unsupported cardinal rotation '" +
                value.ToString(CultureInfo.InvariantCulture) + "'."),
        };

    private static string Hash(byte[] bytes)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(bytes);
        }

        var builder = new StringBuilder("sha256:".Length + (digest.Length * 2));
        builder.Append("sha256:");
        foreach (var value in digest)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed class KinematicData
    {
        public KinematicData(KinematicSpecification specification, KinematicSolution solution)
        {
            Specification = specification;
            Solution = solution;
        }

        public KinematicSpecification Specification { get; }

        public KinematicSolution Solution { get; }
    }
}
