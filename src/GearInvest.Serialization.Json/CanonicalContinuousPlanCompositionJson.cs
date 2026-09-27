using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class ComposedMechanismArtifactRecord
{
    public ComposedMechanismArtifactRecord(
        int index,
        string fileName,
        ContinuousPlanCompositionCandidate compositionCandidate,
        ArtifactWriteResult artifact)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
        FileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("A relative artifact filename is required.", nameof(fileName))
            : fileName;
        CompositionCandidate = compositionCandidate ?? throw new ArgumentNullException(nameof(compositionCandidate));
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        RawSha256 = Hash(artifact.Bytes);
    }

    public int Index { get; }
    public string FileName { get; }
    public ContinuousPlanCompositionCandidate CompositionCandidate { get; }
    public ArtifactWriteResult Artifact { get; }
    public long ByteLength => Artifact.Bytes.LongLength;
    public string RawSha256 { get; }

    private static string Hash(byte[] bytes)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(bytes);
        }

        var builder = new StringBuilder("sha256:");
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

public sealed class CanonicalContinuousPlanCompositionJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(
        ContinuousPlanCompositionResult result,
        IEnumerable<ComposedMechanismArtifactRecord> artifactRecords)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var records = (artifactRecords ?? throw new ArgumentNullException(nameof(artifactRecords)))
            .OrderBy(record => record.Index)
            .ToList();
        ValidateRecords(result, records);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", ContinuousPlanCompositionContract.ResultFormat);
            writer.WriteString("formatVersion", ContinuousPlanCompositionContract.ResultFormatVersion);
            writer.WriteString("compiledPlanId", result.CompiledPlanId);
            WriteRequest(writer, result);
            WriteBackend(writer, result.BackendFingerprint);
            writer.WriteString("status", StatusToWire(result.Status));
            WriteInputEdges(writer, result.OrderedInputEdges);
            WriteSearchSummary(writer, result.SearchSummary);
            WriteCandidates(writer, records);
            CanonicalSemanticCompilationJson.WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void ValidateRecords(
        ContinuousPlanCompositionResult result,
        IReadOnlyList<ComposedMechanismArtifactRecord> records)
    {
        if (records.Count != result.Candidates.Count ||
            records.Where((record, index) => record.Index != index).Any())
        {
            throw new ArgumentException(
                "Artifact records must cover every returned composition candidate in zero-based order.",
                nameof(records));
        }

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var expected = result.Candidates[index];
            if (!StringComparer.Ordinal.Equals(
                    record.CompositionCandidate.ComposedKinematicCandidateId,
                    expected.ComposedKinematicCandidateId) ||
                !StringComparer.Ordinal.Equals(
                    record.CompositionCandidate.ComposedSpatialCandidateId,
                    expected.ComposedSpatialCandidateId) ||
                !StringComparer.Ordinal.Equals(record.CompositionCandidate.CandidateId, expected.CandidateId) ||
                !StringComparer.Ordinal.Equals(record.Artifact.Artifact.CandidateId, expected.CandidateId))
            {
                throw new ArgumentException(
                    "Each artifact record must package the corresponding composition candidate.",
                    nameof(records));
            }
        }
    }

    private static void WriteRequest(Utf8JsonWriter writer, ContinuousPlanCompositionResult result)
    {
        var request = result.NormalizedRequest;
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("compositionRequestId", result.CompositionRequestId);
        writer.WriteString("canonicalRepresentation", result.RequestCanonicalRepresentation);
        writer.WriteNumber("maxLayers", request.MaxLayers);
        writer.WriteString("clearanceTicks", request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxCompositionExpansions", request.MaxCompositionExpansions);
        writer.WriteNumber("maxReturnedCompositions", request.MaxReturnedCompositions);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteBackend(
        Utf8JsonWriter writer,
        ContinuousPlanCompositionFingerprint fingerprint)
    {
        writer.WritePropertyName("backend");
        writer.WriteStartObject();
        writer.WriteString("backendId", fingerprint.BackendId);
        writer.WriteString("backendVersion", fingerprint.BackendVersion);
        writer.WriteString("semanticVersion", fingerprint.SemanticVersion);
        writer.WriteString("determinismProfile", fingerprint.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteInputEdges(
        Utf8JsonWriter writer,
        IEnumerable<CompositionInputEdgeProvenance> inputEdges)
    {
        writer.WritePropertyName("inputEdges");
        writer.WriteStartArray();
        foreach (var edge in inputEdges.OrderBy(item => item.Index))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", edge.Index);
            writer.WriteString("compiledRequirementId", edge.CompiledRequirementId);
            writer.WriteString("semanticFromNodeId", edge.SemanticFromNodeId);
            writer.WriteString("semanticToNodeId", edge.SemanticToNodeId);
            writer.WriteString("signedTargetTransfer", edge.SignedTargetTransfer.ToString());
            writer.WriteString("exactPhaseRelation", edge.ExactPhaseRelation.ToString());
            writer.WriteString("generationRequestId", edge.GenerationRequestId);
            writer.WriteString("kinematicCandidateId", edge.KinematicCandidateId);
            writer.WriteString("spatialCandidateId", edge.SpatialCandidateId);
            writer.WriteString("candidateId", edge.CandidateId);
            writer.WriteString("artifactHash", edge.ArtifactHash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSearchSummary(
        Utf8JsonWriter writer,
        ContinuousPlanCompositionSearchSummary summary)
    {
        writer.WritePropertyName("searchSummary");
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

    private static void WriteCandidates(
        Utf8JsonWriter writer,
        IEnumerable<ComposedMechanismArtifactRecord> records)
    {
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();
        foreach (var record in records)
        {
            var candidate = record.CompositionCandidate;
            writer.WriteStartObject();
            writer.WriteNumber("index", record.Index);
            writer.WriteString("composedKinematicCandidateId", candidate.ComposedKinematicCandidateId);
            writer.WriteString("composedSpatialCandidateId", candidate.ComposedSpatialCandidateId);
            writer.WriteString("candidateId", record.Artifact.Artifact.CandidateId);
            writer.WriteString("artifactHash", record.Artifact.Artifact.ArtifactHash);
            writer.WriteString("selectedRotation", RotationToWire(candidate.Provenance.SelectedRotation));
            writer.WritePropertyName("selectedLayerMapping");
            writer.WriteStartArray();
            foreach (var mapping in candidate.Provenance.SelectedLayerMapping)
            {
                writer.WriteStartObject();
                writer.WriteNumber("localLayer", mapping.LocalLayer);
                writer.WriteNumber("globalLayer", mapping.GlobalLayer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteMetrics(writer, candidate.Metrics);
            writer.WritePropertyName("semanticBindings");
            writer.WriteStartArray();
            foreach (var binding in candidate.SemanticBindings)
            {
                writer.WriteStartObject();
                writer.WriteString("semanticNodeId", binding.SemanticNodeId);
                writer.WriteString("dofId", binding.DofId);
                writer.WriteString("role", RoleToWire(binding.Role));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("validation");
            writer.WriteStartObject();
            writer.WriteBoolean("valid", candidate.Candidate.Validation.IsValid);
            CanonicalSemanticCompilationJson.WriteDiagnostics(writer, candidate.Candidate.Validation.Diagnostics);
            writer.WriteEndObject();
            writer.WriteString("artifactFile", record.FileName);
            writer.WriteNumber("byteLength", record.ByteLength);
            writer.WriteString("rawSha256", record.RawSha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMetrics(Utf8JsonWriter writer, ContinuousPlanCompositionMetrics metrics)
    {
        writer.WritePropertyName("metrics");
        writer.WriteStartObject();
        writer.WriteNumber("usedLayerCount", metrics.UsedLayerCount);
        writer.WriteNumber("dofCount", metrics.DofCount);
        writer.WriteNumber("axisCount", metrics.AxisCount);
        writer.WriteNumber("bodyCount", metrics.BodyCount);
        writer.WriteNumber("contactCount", metrics.ContactCount);
        writer.WriteString("boundingWidth", metrics.BoundingWidth.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingHeight", metrics.BoundingHeight.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingArea", metrics.BoundingArea.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("maximumExtent", metrics.MaximumExtent.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("unrelatedSameLayerPairChecks", metrics.UnrelatedSameLayerPairChecks);
        writer.WriteEndObject();
    }

    private static string StatusToWire(ContinuousPlanCompositionStatus status) => status switch
    {
        ContinuousPlanCompositionStatus.Complete => "complete",
        ContinuousPlanCompositionStatus.IncompleteBudget => "incompleteBudget",
        ContinuousPlanCompositionStatus.Infeasible => "infeasible",
        ContinuousPlanCompositionStatus.InvalidInput => "invalidInput",
        ContinuousPlanCompositionStatus.Unsupported => "unsupported",
        ContinuousPlanCompositionStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported composition status '" + status + "'."),
    };

    private static string RotationToWire(CardinalRotation rotation) => rotation switch
    {
        CardinalRotation.Degrees0 => "0",
        CardinalRotation.Degrees90 => "90",
        CardinalRotation.Degrees180 => "180",
        CardinalRotation.Degrees270 => "270",
        _ => throw new InvalidOperationException("Unsupported cardinal rotation '" + rotation + "'."),
    };

    private static string RoleToWire(SemanticBindingRole role) => role switch
    {
        SemanticBindingRole.Root => "root",
        SemanticBindingRole.IntermediateOutput => "intermediateOutput",
        SemanticBindingRole.FinalOutput => "finalOutput",
        _ => throw new InvalidOperationException("Unsupported semantic binding role '" + role + "'."),
    };
}
