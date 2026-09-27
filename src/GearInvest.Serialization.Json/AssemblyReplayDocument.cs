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

namespace GearInvest.Serialization.Json;

/// <summary>Owned consumption bytes. Envelope integrity is not mechanical reconstruction or author authentication.</summary>
public sealed class AssemblyReplayDocument
{
    private readonly byte[] bytes, payload, source;
    internal AssemblyReplayDocument(byte[] bytes, byte[] payload, byte[] source, string replayId,
        string artifactId, string definitionId, string analysisId, AssemblyReplayExportRequest request)
    {
        this.bytes = (byte[])bytes.Clone(); this.payload = (byte[])payload.Clone(); this.source = (byte[])source.Clone();
        ReplayId = replayId; SourceArtifactId = artifactId; DefinitionId = definitionId; AnalysisId = analysisId; Request = request;
    }
    public string ReplayId { get; }
    public string SourceArtifactId { get; }
    public string DefinitionId { get; }
    public string AnalysisId { get; }
    public AssemblyReplayExportRequest Request { get; }
    public byte[] OriginalBytes => (byte[])bytes.Clone();
    public byte[] CanonicalPayloadBytes => (byte[])payload.Clone();
    public byte[] OriginalArtifactBytes => (byte[])source.Clone();
    public string MechanicalValidation => "notPerformed";
}

/// <summary>Versioned assembly replay envelope. Reads validate consumption structure, not source mechanics.</summary>
public static partial class AssemblyReplayJson
{
    public const string Format = "gear-invest.assembly-replay", Version = "0.1", Profile = "resolved-affine-and-sampled-assembly-v1";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Write(AssemblyReplayDocument document) => document?.OriginalBytes ?? throw new ArgumentNullException(nameof(document));
    public static AssemblyReplayDocument Read(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        try
        {
            using var outer = ParseBounded(bytes, AssemblyReplayLimits.DocumentBytes);
            var e = outer.RootElement; Fields(e, "format", "formatVersion", "replayId", "payloadUtf8");
            Need(Text(e, "format") == Format && Text(e, "formatVersion") == Version, "Unsupported assembly replay format/version.");
            var payload = Decode(e, "payloadUtf8", AssemblyReplayLimits.PayloadBytes);
            var id = Text(e, "replayId"); Need(Hash(payload) == id, "Replay payload identity mismatch.");
            using var parsed = ParseBounded(payload, AssemblyReplayLimits.PayloadBytes);
            var p = parsed.RootElement;
            Fields(p, "profile", "source", "request", "producer", "definition", "analysis", "shafts", "bodies", "staticFeatures", "samples");
            Need(Text(p, "profile") == Profile, "Unsupported replay profile.");
            var s = p.GetProperty("source");
            Fields(s, "format", "formatVersion", "profile", "artifactId", "definitionId", "draftId", "analysisId", "rawSha256", "artifactUtf8");
            var source = Decode(s, "artifactUtf8", AssemblyReplayLimits.SourceBytes);
            Need(Hash(source) == Text(s, "rawSha256"), "Original source raw digest mismatch.");
            using var sourceDoc = ParseBounded(source, AssemblyReplayLimits.SourceBytes);
            var original = sourceDoc.RootElement;
            Need(Text(s, "format") == MechanicalAssemblyArtifact.Format && Text(original, "format") == MechanicalAssemblyArtifact.Format,
                "An original mechanical assembly artifact is required.");
            Need(Text(s, "formatVersion") == Text(original, "formatVersion") && Text(s, "formatVersion") == MechanicalAssemblyJson.VersionFor(Text(s,"profile")) && Text(s, "profile") == Text(original, "profile") &&
                MechanicalAssemblyProfile.IsSupported(Text(s, "profile")), "Source format/profile mismatch.");
            var analysis = p.GetProperty("analysis"); var definition = p.GetProperty("definition");
            Need(Text(original, "artifactHash") == Text(s, "artifactId") && Text(original, "candidateId") == Text(s, "definitionId"), "Source identity mismatch.");
            Need(Text(analysis, "analysisId") == Text(s, "analysisId") && Text(analysis, "definitionId") == Text(s, "definitionId") &&
                Text(analysis, "draftId") == Text(s, "draftId") && Text(definition, "profile") == Text(s, "profile"), "Source/analysis/definition mismatch.");
            Need(Canonical(analysis).SequenceEqual(Canonical(original.GetProperty("analysis"))) &&
                Canonical(definition).SequenceEqual(Canonical(original.GetProperty("request").GetProperty("draft").GetProperty("definition"))),
                "Stored structure is not the source artifact structure.");
            var request = ReadRequest(p.GetProperty("request"));
            ValidateTables(p, request);
            ValidateConsumption(p, original, request);
            Need(bytes.SequenceEqual(Envelope(payload)), "Noncanonical assembly replay envelope.");
            return new(bytes, payload, source, id, Text(s, "artifactId"), Text(s, "definitionId"), Text(s, "analysisId"), request);
        }
        catch (ArtifactFormatException) { throw; }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is JsonException || ex is FormatException || ex is OverflowException || ex is KeyNotFoundException)
        { throw new ArtifactFormatException("Invalid assembly replay: " + ex.Message); }
    }

    internal static string Hash(byte[] bytes)
    { using var hash = SHA256.Create(); return string.Concat(hash.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))); }
    internal static byte[] Encode(Action<Utf8JsonWriter> write, int maximumBytes = AssemblyReplayLimits.PayloadBytes)
    {
        using var stream = new ReplayBoundedStream(maximumBytes);
        using (var writer = new Utf8JsonWriter(stream)) { write(writer); writer.Flush(); }
        return stream.ToArray();
    }
    private static byte[] Envelope(byte[] payload) => Encode(w =>
    {
        w.WriteStartObject(); w.WriteString("format", Format); w.WriteString("formatVersion", Version);
        w.WriteString("replayId", Hash(payload)); w.WriteBase64String("payloadUtf8", payload); w.WriteEndObject();
    }, AssemblyReplayLimits.DocumentBytes);
    internal static void Need(bool ok, string detail) { if (!ok) throw new ArtifactFormatException(detail); }
    internal static string Text(JsonElement p, string name) => p.GetProperty(name).GetString() ?? throw new ArtifactFormatException("String required: " + name);
    internal static void Fields(JsonElement e, params string[] expected)
    { Need(e.ValueKind == JsonValueKind.Object && e.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(x => x, StringComparer.Ordinal)), "Missing/unknown replay fields."); }
    internal static JsonDocument ParseBounded(byte[] bytes, int maximumBytes)
    {
        Need(bytes.Length <= maximumBytes, "Replay document byte bound exceeded.");
        // Validate UTF-8, duplicate decoded keys and token budgets before materializing a tree.
        _ = Utf8.GetCharCount(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = AssemblyReplayLimits.JsonDepth });
        var names = new Stack<HashSet<string>>(); var nodes = 0;
        while (reader.Read())
        {
            Need(++nodes <= AssemblyReplayLimits.JsonNodes, "Replay JSON node bound exceeded.");
            if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
            if (reader.TokenType == JsonTokenType.PropertyName)
            { var key = reader.GetString()!; Need(key.Length <= AssemblyReplayLimits.IdCharacters && names.Peek().Add(key) && names.Peek().Count <= AssemblyReplayLimits.JsonProperties, "Duplicate/oversized replay JSON property."); }
            if (reader.TokenType == JsonTokenType.Number) Need(reader.TryGetDouble(out var n) && !double.IsNaN(n) && !double.IsInfinity(n), "Nonfinite JSON number.");
        }
        var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = AssemblyReplayLimits.JsonDepth });
        try { ValidateValues(doc.RootElement, ""); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void ValidateValues(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("numerator", out _) && e.TryGetProperty("denominator", out _)) _ = Fraction(e);
            if (e.TryGetProperty("representation", out var representation) && representation.GetString() == "certified-rational-enclosure")
            {
                var lower = Fraction(e.GetProperty("lower")); var upper = Fraction(e.GetProperty("upper"));
                Need(lower <= upper && Fraction(e.GetProperty("width")) == upper - lower &&
                    e.GetProperty("isExact").GetBoolean() == (lower == upper) && Text(e,"boundary") == "closed",
                    "Inconsistent stored interval; no enclosure is inferred by the reader.");
            }
            foreach (var p in e.EnumerateObject()) ValidateValues(p.Value, p.Name);
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) ValidateValues(item, name);
        else if (e.ValueKind == JsonValueKind.String)
        {
            var count = e.GetString()!.Length;
            var max = name is "payloadUtf8" ? 4 * ((AssemblyReplayLimits.PayloadBytes + 2) / 3) :
                name.EndsWith("Utf8", StringComparison.Ordinal) ? 4 * ((AssemblyReplayLimits.SourceBytes + 2) / 3) : AssemblyReplayLimits.StringCharacters;
            Need(count <= max, "Replay string bound exceeded.");
        }
    }
    internal static Rational Fraction(JsonElement e, int characters = AssemblyReplayLimits.DerivedIntegerCharacters)
    {
        Fields(e, "numerator", "denominator"); var n = Text(e, "numerator"); var d = Text(e, "denominator");
        Need(n.Length <= characters && d.Length <= characters, "Replay fraction bound exceeded.");
        var validN = BigInteger.TryParse(n, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var nn);
        var validD = BigInteger.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out var dd);
        Need(validN && validD && dd > 0 &&
            nn.ToString(CultureInfo.InvariantCulture) == n && dd.ToString(CultureInfo.InvariantCulture) == d && BigInteger.GreatestCommonDivisor(nn, dd) == 1,
            "Noncanonical replay fraction.");
        return new(nn, dd);
    }
    internal static void Fraction(Utf8JsonWriter w, Rational r)
    {
        AssemblyReplayLimits.CheckFraction(r); w.WriteStartObject(); w.WriteString("numerator", r.Numerator.ToString(CultureInfo.InvariantCulture));
        w.WriteString("denominator", r.Denominator.ToString(CultureInfo.InvariantCulture)); w.WriteEndObject();
    }
    internal static byte[] Decode(JsonElement e, string name, int maximumBytes)
    {
        var text = Text(e, name); Need(text.Length <= 4L * ((maximumBytes + 2L) / 3), "Replay encoded byte bound exceeded.");
        var bytes = Convert.FromBase64String(text); Need(bytes.Length <= maximumBytes && Convert.ToBase64String(bytes) == text, "Invalid/oversized canonical base64."); return bytes;
    }
    internal static byte[] Canonical(JsonElement e) => Encode(e.WriteTo);
    internal static void Request(Utf8JsonWriter w, AssemblyReplayExportRequest r)
    {
        w.WriteStartObject(); w.WriteStartArray("sampleRoots"); foreach (var root in r.SampleRoots) Fraction(w, root); w.WriteEndArray();
        w.WriteBoolean("includeExactGenevaBoundaries", r.IncludeExactGenevaBoundaries); w.WriteNumber("maximumTotalWork", r.MaximumTotalWork); w.WriteEndObject();
    }
    private static AssemblyReplayExportRequest ReadRequest(JsonElement p)
    {
        Fields(p, "sampleRoots", "includeExactGenevaBoundaries", "maximumTotalWork");
        return new(p.GetProperty("sampleRoots").EnumerateArray().Select(x => Fraction(x, AssemblyReplayLimits.InputIntegerCharacters)),
            p.GetProperty("includeExactGenevaBoundaries").GetBoolean(), p.GetProperty("maximumTotalWork").GetInt32());
    }
    private static string ReferenceKey(JsonElement r)
    {
        Fields(r, "owner", "memberId", "kind", "localId");
        var owner = Text(r, "owner"); var member = r.GetProperty("memberId"); var kind = Text(r, "kind"); var id = Text(r, "localId");
        Need(owner == "Root" && member.ValueKind == JsonValueKind.Null || owner == "Member" && member.ValueKind == JsonValueKind.String && member.GetString()!.Length > 0, "Invalid structured owner.");
        Need(Enum.TryParse<AssemblyComponentKind>(kind, out var value) && value.ToString() == kind && id.Length > 0 && id.Length <= AssemblyReplayLimits.IdCharacters, "Invalid component reference.");
        Need(member.ValueKind == JsonValueKind.Null || member.GetString()!.Length <= AssemblyReplayLimits.IdCharacters, "Member ID limit exceeded.");
        return Encoding.UTF8.GetString(Encode(w => { w.WriteStartArray(); w.WriteStringValue(owner); member.WriteTo(w); w.WriteStringValue(kind); w.WriteStringValue(id); w.WriteEndArray(); }));
    }
    private static void ValidateTables(JsonElement p, AssemblyReplayExportRequest request)
    {
        var a = p.GetProperty("analysis"); var memberIds = p.GetProperty("definition").GetProperty("members").EnumerateArray().Select(x => Text(x, "instanceId")).ToArray();
        Need(memberIds.Length <= AssemblyReplayLimits.Members && memberIds.Distinct(StringComparer.Ordinal).Count() == memberIds.Length, "Duplicate/oversized member inventory.");
        foreach(var member in p.GetProperty("definition").GetProperty("members").EnumerateArray())
        { var kind=Text(member.GetProperty("declaration"),"kind"); Need(Enum.TryParse<AssemblyDeviceKind>(kind,out var parsedKind) && Enum.IsDefined(typeof(AssemblyDeviceKind),parsedKind) && parsedKind.ToString()==kind,"Unsupported assembly member family."); }
        var inventory = a.GetProperty("inventory").EnumerateArray().ToArray(); Need(inventory.Length <= AssemblyReplayLimits.Inventory, "Inventory limit exceeded.");
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inventory)
        {
            var r = item.GetProperty("reference"); Need(refs.Add(ReferenceKey(r)), "Duplicate inventory identity.");
            Need(Text(r, "owner") == "Root" || memberIds.Contains(Text(r, "memberId"), StringComparer.Ordinal), "Unknown owner.");
        }
        void Known(JsonElement r) => Need(refs.Contains(ReferenceKey(r)), "Missing/cross-owner reference.");
        foreach (var item in inventory) if (item.GetProperty("mountedShaft").ValueKind != JsonValueKind.Null) Known(item.GetProperty("mountedShaft"));
        var shafts = p.GetProperty("shafts").EnumerateArray().ToArray(); var bodies = p.GetProperty("bodies").EnumerateArray().ToArray();
        Need(shafts.Length <= AssemblyReplayLimits.Shafts && bodies.Length <= AssemblyReplayLimits.Bodies, "Body/shaft limit exceeded.");
        var bodyKeys = new HashSet<string>(StringComparer.Ordinal); var shaftKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in shafts)
        {
            Known(s.GetProperty("reference")); Need(Text(s.GetProperty("reference"), "kind") == "Shaft" && shaftKeys.Add(ReferenceKey(s.GetProperty("reference"))), "Duplicate/non-shaft channel.");
            Need(Text(s, "mode") == "exactAffine", "Unknown shaft channel mode.");
            _ = Fraction(s.GetProperty("q"), AssemblyReplayLimits.InputIntegerCharacters); _ = Fraction(s.GetProperty("p"), AssemblyReplayLimits.InputIntegerCharacters);
        }
        foreach (var b in bodies)
        {
            Known(b.GetProperty("reference")); Need(Text(b.GetProperty("reference"), "kind") == "Body" && bodyKeys.Add(ReferenceKey(b.GetProperty("reference"))), "Duplicate/non-body channel.");
            if (b.GetProperty("mountedShaft").ValueKind != JsonValueKind.Null) Known(b.GetProperty("mountedShaft"));
            Need(Text(b, "mode") is "exactAffine" or "sampled", "Unknown body mode.");
            if (Text(b, "mode") == "exactAffine") { _ = Fraction(b.GetProperty("q"), AssemblyReplayLimits.InputIntegerCharacters); _ = Fraction(b.GetProperty("p"), AssemblyReplayLimits.InputIntegerCharacters); }
        }
        Need(bodyKeys.SetEquals(inventory.Where(x => Text(x.GetProperty("reference"), "kind") == "Body").Select(x => ReferenceKey(x.GetProperty("reference")))), "Whole body inventory required.");
        var samples = p.GetProperty("samples").EnumerateArray().ToArray();
        Need(samples.Length > 0 && samples.Length <= AssemblyReplayLimits.Samples && (long)samples.Length * Math.Max(1, inventory.Length) <= AssemblyReplayLimits.SampleChannelProduct, "Sample/channel resource bound exceeded.");
        long ReadWork(JsonElement value) { var text=Text(value,"numericWork"); Need(text.Length<=8 && long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var _),"Bounded work count required."); var work=long.Parse(text,CultureInfo.InvariantCulture); Need(work.ToString(CultureInfo.InvariantCulture)==text && work<=request.MaximumTotalWork,"Numeric work bound exceeded."); return work; }
        var totalWork=ReadWork(a);
        Rational? previous = null; var sampledRoots = new HashSet<Rational>();
        foreach (var sample in samples)
        {
            var root = Fraction(sample.GetProperty("root"), AssemblyReplayLimits.InputIntegerCharacters);
            Need((!previous.HasValue || previous.Value < root) && root >= request.MinimumRoot && root <= request.MaximumRoot, "Invalid sample order/range.");
            previous = root; sampledRoots.Add(root);
            var obs = sample.GetProperty("observation"); Need(Text(obs, "analysisId") == Text(a, "analysisId"), "Mixed source/frame identity.");
            totalWork+=ReadWork(obs); Need(totalWork<=request.MaximumTotalWork,"Stored sample work budget exceeded.");
            Need(Fraction(obs.GetProperty("rootInput").GetProperty("value")) == root, "Sample root differs from observation.");
            var local = new HashSet<string>(StringComparer.Ordinal);
            foreach (var body in sample.GetProperty("bodyFrames").EnumerateArray())
            { var r = body.GetProperty("reference"); Known(r); Need(bodyKeys.Contains(ReferenceKey(r)) && local.Add(ReferenceKey(r)), "Duplicate/unknown sampled body."); }
            Need(local.SetEquals(bodyKeys), "Sample must preserve whole body inventory, including unavailable entries.");
        }
        Need(request.SampleRoots.All(sampledRoots.Contains), "Requested sample roots missing.");
    }

    private sealed class ReplayBoundedStream : MemoryStream
    {
        private readonly int maximum;
        internal ReplayBoundedStream(int maximum) => this.maximum = maximum;
        public override void Write(byte[] buffer, int offset, int count)
        { Need(Position + count <= maximum, "Replay writer byte limit exceeded."); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Need(Position + buffer.Length <= maximum, "Replay writer byte limit exceeded."); base.Write(buffer); }
        public override void WriteByte(byte value)
        { Need(Position < maximum, "Replay writer byte limit exceeded."); base.WriteByte(value); }
    }
}
