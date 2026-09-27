using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.Serialization.Json;

public sealed class CanonicalSpatialLayoutJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        SkipValidation = false,
    };

    public byte[] Write(SpatialLayoutResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", SpatialLayoutContract.ResultFormat);
            writer.WriteString("formatVersion", SpatialLayoutContract.ResultFormatVersion);
            writer.WriteString("kinematicCandidateId", result.SourceCandidate.KinematicCandidateId);
            WriteRequest(writer, result);
            WriteBackend(writer, result.BackendFingerprint);
            writer.WriteString("status", StatusToWire(result.Status));
            WriteSearchSummary(writer, result.SearchSummary);
            WriteCandidates(writer, result.Candidates);
            WriteDiagnostics(writer, result.Diagnostics);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteRequest(Utf8JsonWriter writer, SpatialLayoutResult result)
    {
        var request = result.NormalizedRequest;
        writer.WritePropertyName("request");
        writer.WriteStartObject();
        writer.WriteString("requestId", result.RequestId);
        writer.WriteString("canonicalRepresentation", result.RequestCanonicalRepresentation);
        writer.WritePropertyName("rootAxisPosition");
        writer.WriteStartObject();
        writer.WriteString("x", request.RootAxisX.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("y", request.RootAxisY.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
        writer.WriteString(
            "pitchRadiusTicksPerTooth",
            request.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("maxLayers", request.MaxLayers);
        writer.WriteNumber("maxReturnedLayouts", request.MaxReturnedLayouts);
        writer.WriteNumber("maxPlacementExpansions", request.MaxPlacementExpansions);
        writer.WriteString("clearanceTicks", request.ClearanceTicks.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("determinismProfile", request.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteBackend(Utf8JsonWriter writer, SpatialLayoutFingerprint fingerprint)
    {
        writer.WritePropertyName("backend");
        writer.WriteStartObject();
        writer.WriteString("backendId", fingerprint.BackendId);
        writer.WriteString("backendVersion", fingerprint.BackendVersion);
        writer.WriteString("semanticVersion", fingerprint.SemanticVersion);
        writer.WriteString("determinismProfile", fingerprint.DeterminismProfile);
        writer.WriteEndObject();
    }

    private static void WriteSearchSummary(Utf8JsonWriter writer, SpatialLayoutSearchSummary summary)
    {
        writer.WritePropertyName("searchSummary");
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

    private static void WriteCandidates(
        Utf8JsonWriter writer,
        IEnumerable<SpatialLayoutCandidate> candidates)
    {
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();
        foreach (var candidate in candidates)
        {
            writer.WriteStartObject();
            writer.WriteString("kinematicCandidateId", candidate.KinematicCandidateId);
            writer.WriteString("spatialCandidateId", candidate.SpatialCandidateId);
            writer.WriteString("canonicalSignature", candidate.CanonicalSignature);
            writer.WritePropertyName("orderedDirections");
            writer.WriteStartArray();
            foreach (var direction in candidate.OrderedDirections)
            {
                writer.WriteStringValue(DirectionToWire(direction));
            }

            writer.WriteEndArray();
            writer.WritePropertyName("stageLayers");
            writer.WriteStartArray();
            foreach (var layer in candidate.StageLayers)
            {
                writer.WriteNumberValue(layer);
            }

            writer.WriteEndArray();
            WriteSpatial(writer, candidate.SpatialMechanism);
            WriteValidation(writer, candidate.Validation);
            WriteMetrics(writer, candidate.Metrics);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSpatial(Utf8JsonWriter writer, SpatialMechanism spatial)
    {
        writer.WritePropertyName("spatial");
        writer.WriteStartObject();
        writer.WritePropertyName("axes");
        writer.WriteStartArray();
        foreach (var axis in spatial.Axes)
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
        foreach (var body in spatial.Bodies)
        {
            writer.WriteStartObject();
            writer.WriteString("id", body.Id);
            writer.WriteString("kind", "gear");
            writer.WriteString("axisId", body.AxisId);
            writer.WriteString("dofId", body.DofId);
            writer.WriteNumber("layer", body.Layer);
            writer.WriteNumber("toothCount", body.ToothCount);
            writer.WriteString("pitchRadiusTicks", body.PitchRadius.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("exactMountingPhase", body.ExactMountingPhase.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("contacts");
        writer.WriteStartArray();
        foreach (var contact in spatial.Contacts)
        {
            writer.WriteStartObject();
            writer.WriteString("id", contact.Id);
            writer.WriteString("kind", "externalGearMesh");
            writer.WriteString("constraintId", contact.ConstraintId);
            writer.WriteString("bodyAId", contact.BodyAId);
            writer.WriteString("bodyBId", contact.BodyBId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValidation(Utf8JsonWriter writer, SpatialValidationBundle validation)
    {
        writer.WritePropertyName("validation");
        writer.WriteStartObject();
        writer.WriteString("status", validation.IsValid ? "valid" : "invalid");
        writer.WriteNumber("unrelatedSameLayerPairChecks", validation.UnrelatedSameLayerPairChecks);
        writer.WritePropertyName("contactReadbacks");
        writer.WriteStartArray();
        foreach (var readback in validation.ContactReadbacks)
        {
            writer.WriteStartObject();
            writer.WriteString("contactId", readback.ContactId);
            writer.WriteString(
                "actualCenterDistanceSquared",
                readback.ActualCenterDistanceSquared.ToString(CultureInfo.InvariantCulture));
            writer.WriteString(
                "expectedCenterDistanceSquared",
                readback.ExpectedCenterDistanceSquared.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("residualSquared", readback.ResidualSquared.ToString(CultureInfo.InvariantCulture));
            writer.WriteBoolean("exact", readback.IsExact);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteDiagnostics(writer, validation.Diagnostics);
        writer.WriteEndObject();
    }

    private static void WriteMetrics(Utf8JsonWriter writer, SpatialLayoutMetrics metrics)
    {
        writer.WritePropertyName("metrics");
        writer.WriteStartObject();
        writer.WriteNumber("usedLayerCount", metrics.UsedLayerCount);
        writer.WriteNumber("bodyCount", metrics.BodyCount);
        writer.WriteNumber("contactCount", metrics.ContactCount);
        writer.WriteString("boundingWidth", metrics.BoundingWidth.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingHeight", metrics.BoundingHeight.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("boundingArea", metrics.BoundingArea.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("directionChangeCount", metrics.DirectionChangeCount);
        writer.WriteString("maximumExtent", metrics.MaximumExtent.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(
        Utf8JsonWriter writer,
        IEnumerable<Diagnostic> diagnostics)
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

    private static string StatusToWire(SpatialLayoutStatus status)
    {
        return status switch
        {
            SpatialLayoutStatus.Complete => "complete",
            SpatialLayoutStatus.IncompleteBudget => "incompleteBudget",
            SpatialLayoutStatus.Infeasible => "infeasible",
            SpatialLayoutStatus.InvalidInput => "invalidInput",
            SpatialLayoutStatus.Cancelled => "cancelled",
            _ => throw new InvalidOperationException("Unsupported layout status '" + status + "'."),
        };
    }

    private static string DirectionToWire(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.East => "east",
            CardinalDirection.North => "north",
            CardinalDirection.West => "west",
            CardinalDirection.South => "south",
            _ => throw new InvalidOperationException("Unsupported cardinal direction '" + direction + "'."),
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
