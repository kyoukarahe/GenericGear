using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;

namespace GearInvest.Serialization.Json;

public sealed class HybridStateSnapshotFormatException : Exception
{
    public HybridStateSnapshotFormatException(string message, bool unsupportedVersion = false, Exception? inner = null)
        : base(message, inner)
    {
        UnsupportedVersion = unsupportedVersion;
    }

    public bool UnsupportedVersion { get; }
}

public sealed class CanonicalHybridStateSnapshotJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    public byte[] Write(HybridStateSnapshotDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", HybridStateSnapshotDocumentContract.Format);
            writer.WriteString("formatVersion", HybridStateSnapshotDocumentContract.FormatVersion);
            writer.WritePropertyName("authority");
            writer.WriteStartObject();
            writer.WriteString("kind", AuthorityToWire(document.Authority.Kind));
            writer.WriteString("compatibleTransitionPlanId", document.Authority.CompatibleTransitionPlanId);
            writer.WriteString("determinismProfile", document.Authority.DeterminismProfile);
            writer.WriteEndObject();
            writer.WritePropertyName("snapshot");
            writer.WriteStartObject();
            writer.WriteString("hybridStateSnapshotId", document.StoredHybridStateSnapshotId);
            writer.WriteString("sourceMechanismCandidateId", document.Snapshot.SourceMechanismCandidateId);
            writer.WriteString("driverId", document.Snapshot.DriverId);
            writer.WritePropertyName("exactRootTurns");
            WriteRational(writer, document.Snapshot.ExactRootTurns);
            writer.WritePropertyName("states");
            writer.WriteStartArray();
            foreach (var state in document.Snapshot.States
                         .OrderBy(item => item.StateDefinitionId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("stateDefinitionId", state.StateDefinitionId);
                writer.WriteString("currentIndex", state.CurrentIndex.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("eventCursors");
            writer.WriteStartArray();
            foreach (var cursor in document.Snapshot.EventCursors
                         .OrderBy(item => item.PeriodicEventDefinitionId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("periodicEventDefinitionId", cursor.PeriodicEventDefinitionId);
                writer.WriteString("lastAppliedOrdinal", cursor.LastAppliedOrdinal.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            if (document.Provenance is not null)
            {
                writer.WritePropertyName("provenance");
                writer.WriteStartObject();
                writer.WriteString("sourceResultFormat", document.Provenance.SourceResultFormat);
                writer.WriteString("sourceResultRequestId", document.Provenance.SourceResultRequestId);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public HybridStateSnapshotDocument Read(byte[] utf8Json)
    {
        if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
        try
        {
            using var json = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            EnsureNoDuplicateProperties(json.RootElement, "$" );
            var root = RequireObject(json.RootElement, "$" );
            EnsureOnlyProperties(root, "$", "format", "formatVersion", "authority", "snapshot", "provenance");

            var format = RequireString(root, "format", "$" );
            if (!StringComparer.Ordinal.Equals(format, HybridStateSnapshotDocumentContract.Format))
            {
                throw new HybridStateSnapshotFormatException("The hybrid snapshot discriminator is missing or unsupported.");
            }

            var version = RequireString(root, "formatVersion", "$" );
            if (!StringComparer.Ordinal.Equals(version, HybridStateSnapshotDocumentContract.FormatVersion))
            {
                throw new HybridStateSnapshotFormatException(
                    "Unsupported hybrid snapshot format version '" + version + "'.",
                    unsupportedVersion: true);
            }

            var authorityElement = RequireObject(RequireProperty(root, "authority", "$"), "$.authority");
            EnsureOnlyProperties(
                authorityElement,
                "$.authority",
                "kind",
                "compatibleTransitionPlanId",
                "determinismProfile");
            var authority = new HybridStateSnapshotAuthority(
                ParseAuthority(RequireString(authorityElement, "kind", "$.authority")),
                RequireString(authorityElement, "compatibleTransitionPlanId", "$.authority"),
                RequireString(authorityElement, "determinismProfile", "$.authority"));

            var snapshotElement = RequireObject(RequireProperty(root, "snapshot", "$"), "$.snapshot");
            EnsureOnlyProperties(
                snapshotElement,
                "$.snapshot",
                "hybridStateSnapshotId",
                "sourceMechanismCandidateId",
                "driverId",
                "exactRootTurns",
                "states",
                "eventCursors");
            var storedId = RequireString(snapshotElement, "hybridStateSnapshotId", "$.snapshot");
            var states = ReadStates(RequireProperty(snapshotElement, "states", "$.snapshot"));
            var cursors = ReadCursors(RequireProperty(snapshotElement, "eventCursors", "$.snapshot"));
            var snapshot = new HybridStateSnapshot(
                RequireString(snapshotElement, "sourceMechanismCandidateId", "$.snapshot"),
                RequireString(snapshotElement, "driverId", "$.snapshot"),
                ReadRational(RequireProperty(snapshotElement, "exactRootTurns", "$.snapshot"), "$.snapshot.exactRootTurns"),
                states,
                cursors);

            HybridStateSnapshotProvenance? provenance = null;
            if (root.TryGetProperty("provenance", out var provenanceValue))
            {
                var provenanceElement = RequireObject(provenanceValue, "$.provenance");
                EnsureOnlyProperties(
                    provenanceElement,
                    "$.provenance",
                    "sourceResultFormat",
                    "sourceResultRequestId");
                provenance = new HybridStateSnapshotProvenance(
                    RequireString(provenanceElement, "sourceResultFormat", "$.provenance"),
                    RequireString(provenanceElement, "sourceResultRequestId", "$.provenance"));
            }

            return new HybridStateSnapshotDocument(authority, snapshot, provenance, storedId);
        }
        catch (HybridStateSnapshotFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new HybridStateSnapshotFormatException("The hybrid snapshot document is not valid JSON.", inner: exception);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or DivideByZeroException)
        {
            throw new HybridStateSnapshotFormatException("The hybrid snapshot document contains an invalid canonical value.", inner: exception);
        }
    }

    private static IReadOnlyList<CyclicIndexedStateValue> ReadStates(JsonElement element)
    {
        var array = RequireArray(element, "$.snapshot.states");
        var values = new List<CyclicIndexedStateValue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var path = "$.snapshot.states[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            var value = RequireObject(item, path);
            EnsureOnlyProperties(value, path, "stateDefinitionId", "currentIndex");
            var id = RequireString(value, "stateDefinitionId", path);
            if (!ids.Add(id))
            {
                throw new HybridStateSnapshotFormatException("Duplicate state definition '" + id + "'.");
            }
            values.Add(new CyclicIndexedStateValue(
                id,
                ParseBigInteger(RequireString(value, "currentIndex", path), path + ".currentIndex")));
            index++;
        }
        return values;
    }

    private static IReadOnlyList<HybridEventCursor> ReadCursors(JsonElement element)
    {
        var array = RequireArray(element, "$.snapshot.eventCursors");
        var values = new List<HybridEventCursor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var path = "$.snapshot.eventCursors[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            var value = RequireObject(item, path);
            EnsureOnlyProperties(value, path, "periodicEventDefinitionId", "lastAppliedOrdinal");
            var id = RequireString(value, "periodicEventDefinitionId", path);
            if (!ids.Add(id))
            {
                throw new HybridStateSnapshotFormatException("Duplicate event cursor '" + id + "'.");
            }
            values.Add(new HybridEventCursor(
                id,
                ParseBigInteger(RequireString(value, "lastAppliedOrdinal", path), path + ".lastAppliedOrdinal")));
            index++;
        }
        return values;
    }

    private static Rational ReadRational(JsonElement element, string path)
    {
        var value = RequireObject(element, path);
        EnsureOnlyProperties(value, path, "numerator", "denominator");
        var numerator = ParseBigInteger(RequireString(value, "numerator", path), path + ".numerator");
        var denominator = ParseBigInteger(RequireString(value, "denominator", path), path + ".denominator");
        if (denominator.IsZero)
        {
            throw new HybridStateSnapshotFormatException(path + " denominator cannot be zero.");
        }
        return new Rational(numerator, denominator);
    }

    private static void WriteRational(Utf8JsonWriter writer, Rational value)
    {
        writer.WriteStartObject();
        writer.WriteString("numerator", value.Numerator.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("denominator", value.Denominator.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static BigInteger ParseBigInteger(string value, string path)
    {
        if (!BigInteger.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
        {
            throw new HybridStateSnapshotFormatException(path + " is not an exact integer string.");
        }
        return result;
    }

    private static HybridStateSnapshotAuthorityKind ParseAuthority(string value) => value switch
    {
        "final" => HybridStateSnapshotAuthorityKind.Final,
        "checkpoint" => HybridStateSnapshotAuthorityKind.Checkpoint,
        _ => throw new HybridStateSnapshotFormatException("Unsupported snapshot authority kind '" + value + "'."),
    };

    private static string AuthorityToWire(HybridStateSnapshotAuthorityKind value) => value switch
    {
        HybridStateSnapshotAuthorityKind.Final => "final",
        HybridStateSnapshotAuthorityKind.Checkpoint => "checkpoint",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static JsonElement RequireObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new HybridStateSnapshotFormatException(path + " must be an object.");
        }
        return element;
    }

    private static JsonElement RequireArray(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new HybridStateSnapshotFormatException(path + " must be an array.");
        }
        return element;
    }

    private static JsonElement RequireProperty(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new HybridStateSnapshotFormatException(path + "." + name + " is required.");
        }
        return value;
    }

    private static string RequireString(JsonElement element, string name, string path)
    {
        var value = RequireProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new HybridStateSnapshotFormatException(path + "." + name + " must be a non-empty string.");
        }
        return value.GetString()!;
    }

    private static void EnsureOnlyProperties(JsonElement element, string path, params string[] allowed)
    {
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!set.Contains(property.Name))
            {
                throw new HybridStateSnapshotFormatException(path + "." + property.Name + " is not supported.");
            }
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
                    throw new HybridStateSnapshotFormatException(path + " contains duplicate property '" + property.Name + "'.");
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
}
