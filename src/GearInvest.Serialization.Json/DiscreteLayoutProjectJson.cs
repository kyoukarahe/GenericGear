using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using static GearInvest.Serialization.DiscreteEmbodimentJson;

namespace GearInvest.Serialization;

public static class DiscreteLayoutProjectJson
{
    public static byte[] Write(DiscreteLayoutProjectManifest m)
    {
        Validate(m);
        return Envelope(DiscreteLayoutContract.ProjectFormat, w =>
        {
            w.WriteStartObject(); w.WriteString("modelId", m.ModelId); w.WriteString("mechanismId", m.MechanismId); w.WriteString("requestId", m.RequestId);
            w.WriteString("backend", m.Backend); w.WriteString("ranking", m.Ranking); w.WriteString("profile", m.Profile);
            if (m.GenerationStatus.HasValue) w.WriteString("generationStatus", m.GenerationStatus.ToString()); else w.WriteNull("generationStatus");
            w.WriteBoolean("searchComplete", m.SearchComplete); w.WriteBoolean("resultTruncated", m.ResultTruncated);
            Array(w, "orderedGeometryIds", m.OrderedGeometryIds, (a, id) => a.WriteStringValue(id));
            if (m.SelectedGeometryId == null) w.WriteNull("selectedGeometryId"); else w.WriteString("selectedGeometryId", m.SelectedGeometryId);
            w.WritePropertyName("evaluation"); DiscreteLayoutJson.Evaluation(w, m.Evaluation);
            if (m.ExecutionId == null) w.WriteNull("executionId"); else w.WriteString("executionId", m.ExecutionId);
            Array(w, "files", m.Files, (a, f) => { a.WriteStartObject(); a.WriteString("role", f.Role); a.WriteString("path", f.Path); a.WriteNumber("bytes", f.Bytes); a.WriteString("sha256", f.Sha256); a.WriteEndObject(); });
            w.WriteEndObject();
        });
    }
    public static DiscreteLayoutProjectManifest Read(byte[] bytes)
    {
        using var d = Open(bytes, DiscreteLayoutContract.ProjectFormat, 128 * 1024); var p = d.RootElement.GetProperty("payload");
        var m = new DiscreteLayoutProjectManifest(S(p, "modelId"), S(p, "mechanismId"), S(p, "requestId"),
            A(p, "files", 6).Select(f => new DiscreteLayoutProjectFile(S(f, "role"), S(f, "path"), I(f, "bytes"), S(f, "sha256"))),
            p.GetProperty("generationStatus").ValueKind == JsonValueKind.Null ? null : E<DiscreteLayoutStatus>(p, "generationStatus"),
            p.GetProperty("searchComplete").GetBoolean(), p.GetProperty("resultTruncated").GetBoolean(), A(p, "orderedGeometryIds", 128).Select(id => id.GetString()!),
            Nullable(p, "selectedGeometryId"), DiscreteLayoutJson.Evaluation(p.GetProperty("evaluation")), Nullable(p, "executionId"), S(p, "backend"), S(p, "ranking"), S(p, "profile"));
        Check(bytes.SequenceEqual(Write(m)), "noncanonical-project"); return m;
    }
    private static string? Nullable(JsonElement p, string key) => p.GetProperty(key).ValueKind == JsonValueKind.Null ? null : S(p, key);
    private static void Validate(DiscreteLayoutProjectManifest m)
    {
        Check(m.Backend == DiscreteLayoutContract.Backend && m.Ranking == DiscreteLayoutContract.Ranking && m.Profile == DiscreteGeometryContract.Profile, "project-profile/version-mismatch");
        Check(new[] { m.ModelId, m.MechanismId, m.RequestId }.All(id => !string.IsNullOrWhiteSpace(id)), "missing-project-identity");
        var roles = m.Files.Select(f => f.Role).ToArray(); var allowed = new[] { "model", "mechanism", "request", "generation", "geometry", "execution" };
        Check(roles.Distinct(StringComparer.Ordinal).Count() == roles.Length && roles.All(allowed.Contains) && new[] { "model", "mechanism", "request" }.All(roles.Contains), "project-file-role-coverage");
        Check(m.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == m.Files.Count && m.Files.All(f => f.Bytes > 0 && f.Bytes <= DiscreteLayoutJson.MaximumGenerationBytes &&
            f.Sha256.Length == 64 && f.Sha256.All(c => "0123456789abcdef".Contains(c)) && SafeRelativePath(f.Path)), "unsafe-or-invalid-project-file");
        Check(m.Files.Sum(f => (long)f.Bytes) <= 64L * 1024 * 1024, "project-total-byte-limit");
        Check(m.OrderedGeometryIds.All(id => !string.IsNullOrWhiteSpace(id)) && m.OrderedGeometryIds.Distinct(StringComparer.Ordinal).Count() == m.OrderedGeometryIds.Count, "project-ordered-identity");
        Check(m.GenerationStatus.HasValue == roles.Contains("generation") && (m.GenerationStatus.HasValue || (!m.SearchComplete && !m.ResultTruncated && m.OrderedGeometryIds.Count == 0 && m.SelectedGeometryId == null)), "project-generation-presence");
        Check(!m.GenerationStatus.HasValue || new[] { DiscreteLayoutStatus.Complete, DiscreteLayoutStatus.Infeasible, DiscreteLayoutStatus.IncompleteBudget }.Contains(m.GenerationStatus.Value), "cancelled-or-invalid-result-not-a-reproduction-baseline");
        Check((m.SelectedGeometryId != null) == roles.Contains("geometry") && (m.SelectedGeometryId == null || m.OrderedGeometryIds.Contains(m.SelectedGeometryId, StringComparer.Ordinal)), "SelectionUnavailable");
        Check((m.ExecutionId != null) == roles.Contains("execution") && (m.ExecutionId == null || (m.SelectedGeometryId != null && m.Evaluation != null)), "project-execution-binding");
    }
    public static bool SafeRelativePath(string path) => !string.IsNullOrEmpty(path) && path.Length <= 240 && !path.StartsWith("/", StringComparison.Ordinal) &&
        path.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '/' || c == '-' || c == '_' || c == '.') &&
        path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != ".." && !segment.EndsWith(".", StringComparison.Ordinal));
}
