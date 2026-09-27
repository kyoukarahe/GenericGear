using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class BranchedComposedMechanismArtifactRecord
{
    public BranchedComposedMechanismArtifactRecord(
        int index,
        string fileName,
        BranchedContinuousCompositionCandidate compositionCandidate,
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
    public BranchedContinuousCompositionCandidate CompositionCandidate { get; }
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

public sealed class CanonicalBranchedContinuousCompositionJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(
        BranchedContinuousCompositionResult result,
        IEnumerable<BranchedComposedMechanismArtifactRecord> artifactRecords)
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
            writer.WriteString("format", BranchedContinuousCompositionContract.ResultFormat);
            writer.WriteString("formatVersion", BranchedContinuousCompositionContract.ResultFormatVersion);
            writer.WriteString("topologyKind", BranchedContinuousCompositionContract.TopologyKind);
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
        BranchedContinuousCompositionResult result,
        IReadOnlyList<BranchedComposedMechanismArtifactRecord> records)
    {
        if (records.Count != result.Candidates.Count ||
            records.Where((record, index) => record.Index != index).Any())
        {
            throw new ArgumentException(
                "Artifact records must cover every returned branched composition candidate in zero-based order.",
                nameof(records));
        }

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var expected = result.Candidates[index];
            if (!StringComparer.Ordinal.Equals(
                    record.CompositionCandidate.BranchedComposedKinematicCandidateId,
                    expected.BranchedComposedKinematicCandidateId) ||
                !StringComparer.Ordinal.Equals(
                    record.CompositionCandidate.BranchedComposedSpatialCandidateId,
                    expected.BranchedComposedSpatialCandidateId) ||
                !StringComparer.Ordinal.Equals(record.CompositionCandidate.CandidateId, expected.CandidateId) ||
                !StringComparer.Ordinal.Equals(record.Artifact.Artifact.CandidateId, expected.CandidateId))
            {
                throw new ArgumentException(
                    "Each artifact record must package the corresponding branched composition candidate.",
                    nameof(records));
            }
        }
    }

    private static void WriteRequest(
        Utf8JsonWriter writer,
        BranchedContinuousCompositionResult result)
    {
        var request = result.NormalizedRequest;
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("branchedCompositionRequestId", result.BranchedCompositionRequestId);
        writer.WriteString("canonicalRepresentation", result.RequestCanonicalRepresentation);
        writer.WriteString("anchorRequirementId", request.AnchorRequirementId);
        writer.WriteNumber("maxLayers", request.MaxLayers);
        writer.WriteString("clearanceTicks", request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxCompositionExpansions", request.MaxCompositionExpansions);
        writer.WriteNumber("maxReturnedCompositions", request.MaxReturnedCompositions);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WritePropertyName("branchHints");
        writer.WriteStartArray();
        foreach (var hint in request.BranchHints)
        {
            writer.WriteStartObject();
            writer.WriteString("compiledRequirementId", hint.CompiledRequirementId);
            writer.WriteString("semanticOutputNodeId", hint.SemanticOutputNodeId);
            writer.WriteString("strength", StrengthToWire(hint.Strength));
            if (hint.PreferredDirection.HasValue)
            {
                writer.WriteString("preferredDirection", DirectionToWire(hint.PreferredDirection.Value));
            }
            if (hint.PreferredSharedInputLayer.HasValue)
            {
                writer.WriteNumber("preferredSharedInputLayer", hint.PreferredSharedInputLayer.Value);
            }
            if (hint.PreferredOutputAnchor is not null)
            {
                writer.WritePropertyName("preferredOutputAnchor");
                WriteAnchor(writer, hint.PreferredOutputAnchor);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteBackend(
        Utf8JsonWriter writer,
        BranchedContinuousCompositionFingerprint fingerprint)
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
        BranchedContinuousCompositionSearchSummary summary)
    {
        writer.WritePropertyName("searchSummary");
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

    private static void WriteCandidates(
        Utf8JsonWriter writer,
        IEnumerable<BranchedComposedMechanismArtifactRecord> records)
    {
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();
        foreach (var record in records)
        {
            var candidate = record.CompositionCandidate;
            writer.WriteStartObject();
            writer.WriteNumber("index", record.Index);
            writer.WriteString(
                "branchedComposedKinematicCandidateId",
                candidate.BranchedComposedKinematicCandidateId);
            writer.WriteString(
                "branchedComposedSpatialCandidateId",
                candidate.BranchedComposedSpatialCandidateId);
            writer.WriteString("candidateId", record.Artifact.Artifact.CandidateId);
            writer.WriteString("artifactHash", record.Artifact.Artifact.ArtifactHash);
            writer.WritePropertyName("branchPlacements");
            writer.WriteStartArray();
            foreach (var branch in candidate.Provenance.SelectedBranches)
            {
                writer.WriteStartObject();
                writer.WriteString("compiledRequirementId", branch.CompiledRequirementId);
                writer.WriteString("semanticOutputNodeId", branch.SemanticOutputNodeId);
                writer.WriteNumber("rotationDegrees", (int)branch.Rotation);
                writer.WriteString("actualDirection", DirectionToWire(branch.ActualDirection));
                writer.WriteNumber("sharedInputGlobalLayer", branch.SharedInputGlobalLayer);
                writer.WritePropertyName("outputAnchor");
                WriteAnchor(writer, branch.OutputAnchor);
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
            writer.WritePropertyName("exactSemanticChannels");
            writer.WriteStartArray();
            foreach (var binding in candidate.SemanticBindings)
            {
                var state = candidate.Candidate.Solution.States.Single(item =>
                    StringComparer.Ordinal.Equals(item.DofId, binding.DofId));
                writer.WriteStartObject();
                writer.WriteString("semanticNodeId", binding.SemanticNodeId);
                writer.WriteString("dofId", binding.DofId);
                writer.WriteString("coefficient", state.Coefficient.ToString());
                writer.WriteString("phase", state.PhaseOffset.ToString());
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

    private static void WriteMetrics(Utf8JsonWriter writer, BranchedContinuousCompositionMetrics metrics)
    {
        var mechanical = metrics.Mechanical;
        writer.WritePropertyName("metrics");
        writer.WriteStartObject();
        writer.WriteNumber("requiredHintSatisfactionCount", metrics.RequiredHintSatisfactionCount);
        writer.WriteNumber("softDirectionMissCount", metrics.SoftDirectionMissCount);
        writer.WriteNumber("softLayerMissCount", metrics.SoftLayerMissCount);
        writer.WriteString("outputAnchorPenalty", metrics.OutputAnchorPenalty.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("usedLayerCount", mechanical.UsedLayerCount);
        writer.WriteNumber("dofCount", mechanical.DofCount);
        writer.WriteNumber("axisCount", mechanical.AxisCount);
        writer.WriteNumber("bodyCount", mechanical.BodyCount);
        writer.WriteNumber("contactCount", mechanical.ContactCount);
        writer.WriteString("boundingWidth", mechanical.BoundingWidth.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingHeight", mechanical.BoundingHeight.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingArea", mechanical.BoundingArea.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("maximumExtent", mechanical.MaximumExtent.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("unrelatedSameLayerPairChecks", mechanical.UnrelatedSameLayerPairChecks);
        writer.WriteEndObject();
    }

    private static void WriteAnchor(Utf8JsonWriter writer, BranchOutputAnchor anchor)
    {
        writer.WriteStartObject();
        writer.WriteString("x", anchor.X.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("y", anchor.Y.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static string StatusToWire(BranchedContinuousCompositionStatus status) => status switch
    {
        BranchedContinuousCompositionStatus.Complete => "complete",
        BranchedContinuousCompositionStatus.IncompleteBudget => "incompleteBudget",
        BranchedContinuousCompositionStatus.Infeasible => "infeasible",
        BranchedContinuousCompositionStatus.InvalidInput => "invalidInput",
        BranchedContinuousCompositionStatus.Unsupported => "unsupported",
        BranchedContinuousCompositionStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported branched composition status '" + status + "'."),
    };

    private static string StrengthToWire(BranchHintStrength strength) => strength switch
    {
        BranchHintStrength.Preferred => "preferred",
        BranchHintStrength.Required => "required",
        _ => throw new InvalidOperationException("Unsupported branch hint strength '" + strength + "'."),
    };

    private static string DirectionToWire(CardinalDirection direction) => direction switch
    {
        CardinalDirection.East => "east",
        CardinalDirection.North => "north",
        CardinalDirection.West => "west",
        CardinalDirection.South => "south",
        _ => throw new InvalidOperationException("Unsupported cardinal direction '" + direction + "'."),
    };

    private static string RoleToWire(SemanticBindingRole role) => role switch
    {
        SemanticBindingRole.Root => "root",
        SemanticBindingRole.SharedBranchOutput => "sharedBranchOutput",
        SemanticBindingRole.LeafOutput => "leafOutput",
        _ => throw new InvalidOperationException("Unsupported branched semantic binding role '" + role + "'."),
    };
}
