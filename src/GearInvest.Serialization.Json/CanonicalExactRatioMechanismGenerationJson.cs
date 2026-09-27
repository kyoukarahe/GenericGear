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

public sealed class GeneratedMechanismArtifactRecord
{
    public GeneratedMechanismArtifactRecord(
        int index,
        string fileName,
        ExactRatioMechanismCandidate generatedCandidate,
        ArtifactWriteResult artifact)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
        FileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("A relative artifact filename is required.", nameof(fileName))
            : fileName;
        GeneratedCandidate = generatedCandidate ?? throw new ArgumentNullException(nameof(generatedCandidate));
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        RawSha256 = Hash(artifact.Bytes);
    }

    public int Index { get; }
    public string FileName { get; }
    public ExactRatioMechanismCandidate GeneratedCandidate { get; }
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

public sealed class CanonicalExactRatioMechanismGenerationJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(
        ExactRatioMechanismGenerationResult result,
        IEnumerable<GeneratedMechanismArtifactRecord> artifactRecords)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var records = (artifactRecords ?? throw new ArgumentNullException(nameof(artifactRecords)))
            .OrderBy(record => record.Index)
            .ToList();
        if (records.Count != result.Candidates.Count ||
            records.Where((record, index) => record.Index != index).Any())
        {
            throw new ArgumentException("Artifact records must cover every returned candidate in zero-based order.", nameof(artifactRecords));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", ExactRatioMechanismGenerationContract.ResultFormat);
            writer.WriteString("formatVersion", ExactRatioMechanismGenerationContract.ResultFormatVersion);
            WriteRequest(writer, result);
            writer.WritePropertyName("generator");
            writer.WriteStartObject();
            writer.WriteString("backendId", result.GenerationFingerprint.BackendId);
            writer.WriteString("backendVersion", result.GenerationFingerprint.BackendVersion);
            writer.WriteString("semanticVersion", result.GenerationFingerprint.SemanticVersion);
            writer.WriteString("determinismProfile", result.GenerationFingerprint.DeterminismProfile);
            writer.WriteEndObject();
            WriteSynthesis(writer, result.SynthesisResult);
            writer.WriteString("status", StatusToWire(result.Status));
            WriteSearchSummary(writer, result.SearchSummary);
            WriteLayoutAttempts(writer, result.LayoutAttempts);
            WriteCandidates(writer, records);
            WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteRequest(Utf8JsonWriter writer, ExactRatioMechanismGenerationResult result)
    {
        var request = result.NormalizedRequest;
        var synthesis = request.SynthesisRequest;
        var layout = request.LayoutRequest;
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("requestId", result.RequestId);
        writer.WriteString("canonicalRepresentation", result.RequestCanonicalRepresentation);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WritePropertyName("synthesis");
        writer.WriteStartObject();
        writer.WriteString("targetTransfer", synthesis.TargetTransfer.ToString());
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
        writer.WriteNumber("maxKinematicCandidatesToLayout", request.MaxKinematicCandidatesToLayout);
        writer.WriteNumber("maxLayoutsPerKinematicCandidate", request.MaxLayoutsPerKinematicCandidate);
        writer.WriteNumber("maxReturnedMechanisms", request.MaxReturnedMechanisms);
        writer.WriteEndObject();
    }

    private static void WriteSearchSummary(Utf8JsonWriter writer, ExactRatioMechanismSearchSummary summary)
    {
        writer.WritePropertyName("searchSummary");
        writer.WriteStartObject();
        writer.WriteBoolean("synthesisResultTruncated", summary.SynthesisResultTruncated);
        writer.WriteNumber("kinematicCandidatesAvailable", summary.KinematicCandidatesAvailable);
        writer.WriteNumber("kinematicCandidatesSelected", summary.KinematicCandidatesSelected);
        writer.WriteNumber("kinematicCandidatesSkippedByOrchestrationCap", summary.KinematicCandidatesSkippedByOrchestrationCap);
        writer.WriteNumber("layoutAttempts", summary.LayoutAttempts);
        writer.WriteNumber("incompleteLayoutAttempts", summary.IncompleteLayoutAttempts);
        writer.WriteNumber("infeasibleLayoutAttempts", summary.InfeasibleLayoutAttempts);
        writer.WriteNumber("truncatedLayoutAttempts", summary.TruncatedLayoutAttempts);
        writer.WriteNumber("layoutCandidatesSelected", summary.LayoutCandidatesSelected);
        writer.WriteNumber("composedCandidates", summary.ComposedCandidates);
        writer.WriteNumber("returnedCandidates", summary.ReturnedCandidates);
        writer.WriteBoolean("finalResultTruncated", summary.FinalResultTruncated);
        writer.WriteBoolean("searchComplete", summary.SearchComplete);
        writer.WriteEndObject();
    }

    private static void WriteSynthesis(Utf8JsonWriter writer, KinematicSynthesisResult? synthesis)
    {
        writer.WritePropertyName("synthesis");
        if (synthesis is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("status", SynthesisStatusToWire(synthesis.Status));
        writer.WriteString("requestId", synthesis.RequestId);
        writer.WritePropertyName("backend");
        writer.WriteStartObject();
        writer.WriteString("backendId", synthesis.GeneratorFingerprint.BackendId);
        writer.WriteString("backendVersion", synthesis.GeneratorFingerprint.BackendVersion);
        writer.WriteString("semanticVersion", synthesis.GeneratorFingerprint.SemanticVersion);
        writer.WriteString("determinismProfile", synthesis.GeneratorFingerprint.DeterminismProfile);
        writer.WriteEndObject();
        writer.WritePropertyName("searchSummary");
        writer.WriteStartObject();
        writer.WritePropertyName("stageOptionsConsidered");
        writer.WriteStartArray();
        foreach (var stage in synthesis.SearchSummary.StageOptionsConsidered)
        {
            writer.WriteNumberValue(stage);
        }

        writer.WriteEndArray();
        writer.WriteNumber("searchExpansions", synthesis.SearchSummary.SearchExpansions);
        writer.WriteNumber("rawMatches", synthesis.SearchSummary.RawMatches);
        writer.WriteNumber("deduplicatedCandidates", synthesis.SearchSummary.DeduplicatedCandidates);
        writer.WriteNumber("returnedCandidates", synthesis.SearchSummary.ReturnedCandidates);
        writer.WriteBoolean("resultTruncated", synthesis.SearchSummary.ResultTruncated);
        writer.WriteBoolean("searchComplete", synthesis.SearchSummary.SearchComplete);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteLayoutAttempts(
        Utf8JsonWriter writer,
        IEnumerable<ExactRatioMechanismLayoutAttempt> attempts)
    {
        writer.WritePropertyName("layoutAttempts");
        writer.WriteStartArray();
        foreach (var attempt in attempts.OrderBy(item => item.KinematicRank))
        {
            writer.WriteStartObject();
            writer.WriteNumber("kinematicRank", attempt.KinematicRank);
            writer.WriteString("kinematicCandidateId", attempt.KinematicCandidateId);
            writer.WriteString("status", SpatialStatusToWire(attempt.Result.Status));
            writer.WriteString("requestId", attempt.Result.RequestId);
            writer.WritePropertyName("backend");
            writer.WriteStartObject();
            writer.WriteString("backendId", attempt.Result.BackendFingerprint.BackendId);
            writer.WriteString("backendVersion", attempt.Result.BackendFingerprint.BackendVersion);
            writer.WriteString("semanticVersion", attempt.Result.BackendFingerprint.SemanticVersion);
            writer.WriteString("determinismProfile", attempt.Result.BackendFingerprint.DeterminismProfile);
            writer.WriteEndObject();
            writer.WriteNumber("selectedLayoutCount", attempt.SelectedLayoutCount);
            writer.WriteNumber("directionAssignmentsConsidered", attempt.Result.SearchSummary.DirectionAssignmentsConsidered);
            writer.WriteNumber("layerAssignmentsConsidered", attempt.Result.SearchSummary.LayerAssignmentsConsidered);
            writer.WriteNumber("placementExpansions", attempt.Result.SearchSummary.PlacementExpansions);
            writer.WriteNumber("rawFeasibleLayouts", attempt.Result.SearchSummary.RawFeasibleLayouts);
            writer.WriteNumber("deduplicatedLayouts", attempt.Result.SearchSummary.DeduplicatedLayouts);
            writer.WriteNumber("returnedLayouts", attempt.Result.SearchSummary.ReturnedLayouts);
            writer.WriteBoolean("resultTruncated", attempt.Result.SearchSummary.ResultTruncated);
            writer.WriteBoolean("searchComplete", attempt.Result.SearchSummary.SearchComplete);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCandidates(
        Utf8JsonWriter writer,
        IEnumerable<GeneratedMechanismArtifactRecord> records)
    {
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();
        foreach (var record in records)
        {
            var provenance = record.GeneratedCandidate.Provenance;
            writer.WriteStartObject();
            writer.WriteNumber("index", record.Index);
            writer.WriteString("candidateId", record.Artifact.Artifact.CandidateId);
            writer.WriteString("kinematicCandidateId", provenance.KinematicCandidateId);
            writer.WriteNumber("kinematicRank", provenance.KinematicRank);
            writer.WriteString("spatialCandidateId", provenance.SpatialCandidateId);
            writer.WriteNumber("spatialRank", provenance.SpatialRank);
            writer.WriteString("artifactFile", record.FileName);
            writer.WriteNumber("byteLength", record.ByteLength);
            writer.WriteString("rawSha256", record.RawSha256);
            writer.WriteString("artifactHash", record.Artifact.Artifact.ArtifactHash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, IEnumerable<Diagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity.ToString().ToLowerInvariant());
            writer.WriteString("message", diagnostic.Message);
            if (diagnostic.SubjectId is not null) writer.WriteString("subjectId", diagnostic.SubjectId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string StatusToWire(ExactRatioMechanismGenerationStatus status) => status switch
    {
        ExactRatioMechanismGenerationStatus.Complete => "complete",
        ExactRatioMechanismGenerationStatus.IncompleteBudget => "incompleteBudget",
        ExactRatioMechanismGenerationStatus.Infeasible => "infeasible",
        ExactRatioMechanismGenerationStatus.InvalidInput => "invalidInput",
        ExactRatioMechanismGenerationStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported generation status '" + status + "'."),
    };

    private static string SynthesisStatusToWire(KinematicSynthesisStatus status) => status switch
    {
        KinematicSynthesisStatus.Complete => "complete",
        KinematicSynthesisStatus.IncompleteBudget => "incompleteBudget",
        KinematicSynthesisStatus.Infeasible => "infeasible",
        KinematicSynthesisStatus.InvalidInput => "invalidInput",
        KinematicSynthesisStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported synthesis status '" + status + "'."),
    };

    private static string SpatialStatusToWire(SpatialLayoutStatus status) => status switch
    {
        SpatialLayoutStatus.Complete => "complete",
        SpatialLayoutStatus.IncompleteBudget => "incompleteBudget",
        SpatialLayoutStatus.Infeasible => "infeasible",
        SpatialLayoutStatus.InvalidInput => "invalidInput",
        SpatialLayoutStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported layout status '" + status + "'."),
    };
}
