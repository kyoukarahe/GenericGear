using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class GeneratedPlanEdgeArtifactRecord
{
    public GeneratedPlanEdgeArtifactRecord(
        int edgeIndex,
        string edgeDirectory,
        IEnumerable<GeneratedMechanismArtifactRecord> candidateArtifacts)
    {
        if (edgeIndex < 0) throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        EdgeIndex = edgeIndex;
        EdgeDirectory = string.IsNullOrWhiteSpace(edgeDirectory)
            ? throw new ArgumentException("A relative edge directory is required.", nameof(edgeDirectory))
            : edgeDirectory;
        CandidateArtifacts = (candidateArtifacts ?? throw new ArgumentNullException(nameof(candidateArtifacts)))
            .OrderBy(item => item.Index).ToList().AsReadOnly();
    }

    public int EdgeIndex { get; }
    public string EdgeDirectory { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<GeneratedMechanismArtifactRecord> CandidateArtifacts { get; }
}

public sealed class CanonicalCompiledPlanGenerationJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(
        CompiledPlanGenerationResult result,
        IEnumerable<GeneratedPlanEdgeArtifactRecord> artifactRecords)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var records = (artifactRecords ?? throw new ArgumentNullException(nameof(artifactRecords)))
            .OrderBy(item => item.EdgeIndex).ToList();
        if (records.Count != result.EdgeResults.Count ||
            records.Where((record, index) => record.EdgeIndex != index).Any())
        {
            throw new ArgumentException(
                "Artifact records must cover every generated edge in zero-based order.",
                nameof(artifactRecords));
        }

        for (var index = 0; index < records.Count; index++)
        {
            var candidateCount = result.EdgeResults[index].GenerationResult.Candidates.Count;
            if (records[index].CandidateArtifacts.Count != candidateCount ||
                records[index].CandidateArtifacts.Where((record, candidateIndex) => record.Index != candidateIndex).Any())
            {
                throw new ArgumentException(
                    "Each edge artifact record must cover every returned candidate in zero-based order.",
                    nameof(artifactRecords));
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", CompiledPlanGenerationContract.ResultFormat);
            writer.WriteString("formatVersion", CompiledPlanGenerationContract.ResultFormatVersion);
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("kind", result.SourceKind);
            writer.WriteString("catalogId", result.SourceCatalogId);
            writer.WriteString("semanticSpecificationId", result.SemanticSpecificationId);
            writer.WriteEndObject();
            CanonicalSemanticCompilationJson.WriteCompiler(writer, result.CompilerFingerprint);
            writer.WriteString("compiledPlanId", result.CompiledPlanId);
            WritePolicy(writer, result);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WritePropertyName("edgeResults");
            writer.WriteStartArray();
            for (var index = 0; index < result.EdgeResults.Count; index++)
            {
                WriteEdge(writer, result.EdgeResults[index], records[index]);
            }

            writer.WriteEndArray();
            CanonicalSemanticCompilationJson.WriteDerivedRelations(writer, result.DerivedRelations);
            CanonicalSemanticCompilationJson.WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WritePolicy(Utf8JsonWriter writer, CompiledPlanGenerationResult result)
    {
        var policy = result.Policy;
        writer.WritePropertyName("generationPolicy");
        writer.WriteStartObject();
        writer.WriteString("generationPolicyId", result.GenerationPolicyId);
        writer.WriteString("canonicalRepresentation", ExactRatioPlanGenerationPolicyIdentity.BuildCanonicalRepresentation(policy));
        writer.WriteNumber("minTeeth", policy.MinTeeth);
        writer.WriteNumber("maxTeeth", policy.MaxTeeth);
        writer.WriteNumber("minStages", policy.MinStages);
        writer.WriteNumber("maxStages", policy.MaxStages);
        writer.WriteBoolean("allowCompound", policy.AllowCompound);
        writer.WriteNumber("maxKinematicCandidates", policy.MaxKinematicCandidates);
        writer.WriteNumber("maxSynthesisExpansions", policy.MaxSynthesisExpansions);
        writer.WriteString("rootAxisX", policy.RootAxisX.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("rootAxisY", policy.RootAxisY.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("pitchRadiusTicksPerTooth", policy.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxLayers", policy.MaxLayers);
        writer.WriteNumber("maxLayoutsPerKinematicCandidate", policy.MaxLayoutsPerKinematicCandidate);
        writer.WriteNumber("maxPlacementExpansions", policy.MaxPlacementExpansions);
        writer.WriteString("clearanceTicks", policy.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxReturnedMechanisms", policy.MaxReturnedMechanisms);
        writer.WriteString("synthesisDeterminismProfile", policy.SynthesisDeterminismProfile);
        writer.WriteString("layoutDeterminismProfile", policy.LayoutDeterminismProfile);
        writer.WriteString("generationDeterminismProfile", policy.GenerationDeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteEdge(
        Utf8JsonWriter writer,
        CompiledPlanGenerationEdgeResult result,
        GeneratedPlanEdgeArtifactRecord record)
    {
        var edge = result.Edge;
        var generation = result.GenerationResult;
        writer.WriteStartObject();
        writer.WriteNumber("index", edge.Index);
        writer.WriteString("compiledRequirementId", edge.Requirement.CompiledRequirementId);
        writer.WriteString("sourceRequirementId", edge.Requirement.SourceRequirementId);
        writer.WriteString("fromSemanticNodeId", edge.Requirement.FromSemanticNodeId);
        writer.WriteString("toSemanticNodeId", edge.Requirement.ToSemanticNodeId);
        writer.WriteString("signedTargetTransfer", edge.Requirement.SignedTargetTransfer.ToString());
        writer.WriteString("exactPhaseRelation", edge.Requirement.ExactPhaseRelation.ToString());
        writer.WriteString("generationRequestId", generation.RequestId);
        writer.WriteString("generationStatus", GenerationStatusToWire(generation.Status));
        writer.WriteString("edgeDirectory", record.EdgeDirectory);
        writer.WritePropertyName("artifacts");
        writer.WriteStartArray();
        foreach (var artifact in record.CandidateArtifacts)
        {
            var provenance = artifact.GeneratedCandidate.Provenance;
            writer.WriteStartObject();
            writer.WriteNumber("index", artifact.Index);
            writer.WriteString("artifactFile", record.EdgeDirectory + "/" + artifact.FileName);
            writer.WriteNumber("byteLength", artifact.ByteLength);
            writer.WriteString("rawSha256", artifact.RawSha256);
            writer.WriteString("kinematicCandidateId", provenance.KinematicCandidateId);
            writer.WriteString("spatialCandidateId", provenance.SpatialCandidateId);
            writer.WriteString("candidateId", artifact.Artifact.Artifact.CandidateId);
            writer.WriteString("artifactHash", artifact.Artifact.Artifact.ArtifactHash);
            writer.WriteBoolean("validationValid", artifact.GeneratedCandidate.Candidate.Validation.IsValid);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string StatusToWire(CompiledPlanGenerationStatus status) => status switch
    {
        CompiledPlanGenerationStatus.Complete => "complete",
        CompiledPlanGenerationStatus.IncompleteBudget => "incompleteBudget",
        CompiledPlanGenerationStatus.Infeasible => "infeasible",
        CompiledPlanGenerationStatus.InvalidInput => "invalidInput",
        CompiledPlanGenerationStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported plan generation status '" + status + "'."),
    };

    private static string GenerationStatusToWire(ExactRatioMechanismGenerationStatus status) => status switch
    {
        ExactRatioMechanismGenerationStatus.Complete => "complete",
        ExactRatioMechanismGenerationStatus.IncompleteBudget => "incompleteBudget",
        ExactRatioMechanismGenerationStatus.Infeasible => "infeasible",
        ExactRatioMechanismGenerationStatus.InvalidInput => "invalidInput",
        ExactRatioMechanismGenerationStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported edge generation status '" + status + "'."),
    };
}
