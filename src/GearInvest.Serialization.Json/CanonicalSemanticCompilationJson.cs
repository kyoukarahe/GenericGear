using System;
using System.IO;
using System.Text.Json;
using GearInvest.Core;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalSemanticCompilationJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(SemanticCompilationResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", PeriodicSemanticContract.CompilationResultFormat);
            writer.WriteString("formatVersion", PeriodicSemanticContract.CompilationResultFormatVersion);
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("kind", result.SourceKind);
            writer.WriteString("catalogId", result.SourceCatalogId);
            writer.WriteString("semanticSpecificationId", result.SemanticSpecificationId);
            writer.WriteEndObject();
            WriteCompiler(writer, result.CompilerFingerprint);
            writer.WriteString("status", StatusToWire(result.Status));
            writer.WritePropertyName("compiledPlan");
            if (result.CompiledPlan is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WritePlan(writer, result.CompiledPlan);
            }

            WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static void WriteCompiler(Utf8JsonWriter writer, SemanticCompilerFingerprint fingerprint)
    {
        writer.WritePropertyName("compiler");
        writer.WriteStartObject();
        writer.WriteString("compilerId", fingerprint.CompilerId);
        writer.WriteString("compilerVersion", fingerprint.CompilerVersion);
        writer.WriteString("semanticVersion", fingerprint.SemanticVersion);
        writer.WriteString("determinismProfile", fingerprint.DeterminismProfile);
        writer.WriteEndObject();
    }

    internal static void WritePlan(Utf8JsonWriter writer, CompiledMechanicalRequirementPlan plan)
    {
        writer.WriteStartObject();
        writer.WriteString("compiledPlanId", plan.CompiledPlanId);
        writer.WriteString("baseTimeUnit", plan.BaseTimeUnit);
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();
        foreach (var node in plan.Nodes)
        {
            writer.WriteStartObject();
            writer.WriteString("id", node.Id);
            writer.WriteString("exactPeriod", node.ExactPeriodInBaseTimeUnit.ToString());
            writer.WriteString("exactInitialPhaseTurns", node.ExactInitialPhaseTurns.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("orderedRequirements");
        writer.WriteStartArray();
        foreach (var requirement in plan.OrderedRequirements)
        {
            writer.WriteStartObject();
            writer.WriteString("compiledRequirementId", requirement.CompiledRequirementId);
            writer.WriteString("sourceRequirementId", requirement.SourceRequirementId);
            writer.WriteString("fromSemanticNodeId", requirement.FromSemanticNodeId);
            writer.WriteString("toSemanticNodeId", requirement.ToSemanticNodeId);
            writer.WriteString("signedTargetTransfer", requirement.SignedTargetTransfer.ToString());
            writer.WriteString("exactPhaseRelation", requirement.ExactPhaseRelation.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteDerivedRelations(writer, plan.DerivedRelations);
        writer.WriteEndObject();
    }

    internal static void WriteDerivedRelations(
        Utf8JsonWriter writer,
        System.Collections.Generic.IEnumerable<DerivedContinuousTransferRelation> relations)
    {
        writer.WritePropertyName("derivedRelations");
        writer.WriteStartArray();
        foreach (var relation in relations)
        {
            writer.WriteStartObject();
            writer.WriteString("relationId", relation.RelationId);
            writer.WriteString("fromSemanticNodeId", relation.FromSemanticNodeId);
            writer.WriteString("toSemanticNodeId", relation.ToSemanticNodeId);
            writer.WriteString("signedTransfer", relation.SignedTransfer.ToString());
            writer.WriteString("exactPhaseRelation", relation.ExactPhaseRelation.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    internal static void WriteDiagnostics(
        Utf8JsonWriter writer,
        System.Collections.Generic.IEnumerable<Diagnostic> diagnostics)
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

    internal static string StatusToWire(SemanticCompilationStatus status) => status switch
    {
        SemanticCompilationStatus.Complete => "complete",
        SemanticCompilationStatus.InvalidInput => "invalidInput",
        SemanticCompilationStatus.Unsupported => "unsupported",
        SemanticCompilationStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported semantic compilation status '" + status + "'."),
    };
}
