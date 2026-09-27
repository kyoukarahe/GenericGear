using System;
using System.IO;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalKinematicSynthesisJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(KinematicSynthesisResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", ExactRatioSynthesisContract.ResultFormat);
            writer.WriteString("formatVersion", ExactRatioSynthesisContract.ResultFormatVersion);
            WriteRequest(writer, result);
            WriteGenerator(writer, result.GeneratorFingerprint);
            writer.WriteString("status", StatusToWire(result.Status));
            WriteSearchSummary(writer, result.SearchSummary);
            WriteCandidates(writer, result);
            WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteRequest(Utf8JsonWriter writer, KinematicSynthesisResult result)
    {
        var request = result.NormalizedRequest;
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("requestId", result.RequestId);
        writer.WriteString("canonicalRepresentation", result.RequestCanonicalRepresentation);
        writer.WriteString("targetTransfer", request.TargetTransfer.ToString());
        writer.WriteNumber("minTeeth", request.MinTeeth);
        writer.WriteNumber("maxTeeth", request.MaxTeeth);
        writer.WriteNumber("minStages", request.MinStages);
        writer.WriteNumber("maxStages", request.MaxStages);
        writer.WriteBoolean("allowCompound", request.AllowCompound);
        writer.WriteNumber("maxReturnedCandidates", request.MaxReturnedCandidates);
        writer.WriteNumber("maxSearchExpansions", request.MaxSearchExpansions);
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteGenerator(Utf8JsonWriter writer, KinematicSynthesisFingerprint fingerprint)
    {
        writer.WritePropertyName("generator");
        writer.WriteStartObject();
        writer.WriteString("backendId", fingerprint.BackendId);
        writer.WriteString("backendVersion", fingerprint.BackendVersion);
        writer.WriteString("semanticVersion", fingerprint.SemanticVersion);
        writer.WriteString("determinismProfile", fingerprint.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteSearchSummary(Utf8JsonWriter writer, KinematicSearchSummary summary)
    {
        writer.WritePropertyName("searchSummary");
        writer.WriteStartObject();
        writer.WritePropertyName("stageOptionsConsidered");
        writer.WriteStartArray();
        foreach (var stageCount in summary.StageOptionsConsidered)
        {
            writer.WriteNumberValue(stageCount);
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

    private static void WriteCandidates(Utf8JsonWriter writer, KinematicSynthesisResult result)
    {
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();
        foreach (var candidate in result.Candidates)
        {
            writer.WriteStartObject();
            writer.WriteString("kinematicCandidateId", candidate.KinematicCandidateId);
            writer.WriteString("canonicalSignature", candidate.CanonicalSignature);
            writer.WriteString("exactTransfer", candidate.ExactTransfer.ToString());

            writer.WritePropertyName("orderedStages");
            writer.WriteStartArray();
            foreach (var stage in candidate.OrderedStages)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", stage.Index);
                writer.WriteNumber("driverTeeth", stage.DriverTeeth);
                writer.WriteNumber("drivenTeeth", stage.DrivenTeeth);
                writer.WriteString("exactTransfer", stage.Transfer.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("intermediateRoles");
            writer.WriteStartArray();
            foreach (var role in candidate.IntermediateRoles)
            {
                writer.WriteStringValue(RoleToWire(role));
            }

            writer.WriteEndArray();
            WriteKinematic(writer, candidate);
            WriteMetrics(writer, candidate.Metrics);
            writer.WriteString("validationStatus", candidate.Validation.IsValid ? "valid" : "invalid");
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteKinematic(Utf8JsonWriter writer, KinematicSynthesisCandidate candidate)
    {
        writer.WritePropertyName("kinematic");
        writer.WriteStartObject();
        writer.WriteString("rootDofId", candidate.KinematicMechanism.RootDofId);
        writer.WritePropertyName("dofs");
        writer.WriteStartArray();
        foreach (var dof in candidate.KinematicMechanism.Dofs)
        {
            writer.WriteStartObject();
            writer.WriteString("id", dof.Id);
            writer.WriteBoolean("prescribed", dof.IsPrescribed);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("couplings");
        writer.WriteStartArray();
        foreach (var coupling in candidate.KinematicMechanism.Couplings)
        {
            writer.WriteStartObject();
            writer.WriteString("id", coupling.Id);
            writer.WriteString("driverDofId", coupling.DriverDofId);
            writer.WriteString("drivenDofId", coupling.DrivenDofId);
            writer.WriteNumber("driverTeeth", coupling.DriverTeeth);
            writer.WriteNumber("drivenTeeth", coupling.DrivenTeeth);
            writer.WriteString("phaseOffset", coupling.PhaseOffset.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("solution");
        writer.WriteStartArray();
        foreach (var state in candidate.ExactSolution.States)
        {
            writer.WriteStartObject();
            writer.WriteString("dofId", state.DofId);
            writer.WriteString("coefficient", state.Coefficient.ToString());
            writer.WriteString("phaseOffset", state.PhaseOffset.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMetrics(Utf8JsonWriter writer, KinematicSynthesisMetrics metrics)
    {
        writer.WritePropertyName("metrics");
        writer.WriteStartObject();
        writer.WriteNumber("stageCount", metrics.StageCount);
        writer.WriteNumber("intermediateDofCount", metrics.IntermediateDofCount);
        writer.WriteNumber("compoundIntermediateCount", metrics.CompoundIntermediateCount);
        writer.WriteNumber("idlerCompatibleIntermediateCount", metrics.IdlerCompatibleIntermediateCount);
        writer.WriteNumber("estimatedGearBodyCount", metrics.EstimatedGearBodyCount);
        writer.WriteNumber("maximumToothCount", metrics.MaximumToothCount);
        writer.WriteNumber("minimumToothCount", metrics.MinimumToothCount);
        writer.WriteNumber("toothSpan", metrics.ToothSpan);
        writer.WriteNumber("totalToothCount", metrics.TotalToothCount);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(
        Utf8JsonWriter writer,
        System.Collections.Generic.IEnumerable<Diagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in diagnostics)
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
    }

    private static string StatusToWire(KinematicSynthesisStatus status)
    {
        return status switch
        {
            KinematicSynthesisStatus.Complete => "complete",
            KinematicSynthesisStatus.IncompleteBudget => "incompleteBudget",
            KinematicSynthesisStatus.Infeasible => "infeasible",
            KinematicSynthesisStatus.InvalidInput => "invalidInput",
            KinematicSynthesisStatus.Cancelled => "cancelled",
            _ => throw new InvalidOperationException("Unsupported synthesis status '" + status + "'."),
        };
    }

    private static string RoleToWire(IntermediateShaftRole role)
    {
        return role switch
        {
            IntermediateShaftRole.Compound => "compound",
            IntermediateShaftRole.SimpleIdlerCompatible => "simple-idler-compatible",
            _ => throw new InvalidOperationException("Unsupported intermediate role '" + role + "'."),
        };
    }

    private static string SeverityToWire(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Info => "info",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Error => "error",
            _ => throw new InvalidOperationException("Unsupported diagnostic severity '" + severity + "'."),
        };
    }
}
