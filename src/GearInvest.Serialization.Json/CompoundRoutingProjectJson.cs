using System;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using System.Text.Json;

namespace GearInvest.Serialization;

public static class CompoundRoutingProjectJson
{
    public static byte[] Write(CompoundRoutingProjectManifest m)
    {
        Check(m.Profile == CompoundRoutingContract.Profile && m.Backend == CompoundRoutingContract.Backend && m.Ranking == CompoundRoutingContract.Ranking, "routing-project-profile/version");
        Check(m.RequestId.StartsWith("anchored-compound-routing-request-sha256:", StringComparison.Ordinal), "routing-project-request-id");
        var roles = m.Files.Select(f => f.Role).ToArray(); Check(roles.Contains("request") && roles.Distinct().Count() == roles.Length && roles.All(x => x == "request" || x == "generation" || x == "mechanism"), "routing-project-roles");
        Check(m.Files.All(f => DiscreteLayoutProjectJson.SafeRelativePath(f.Path) && f.Bytes > 0 && f.Bytes <= (f.Role == "request" ? CompoundRoutingContract.MaxRequestBytes : f.Role == "mechanism" ? 512 * 1024 : CompoundRoutingContract.MaxResultBytes) && f.Sha256.Length == 64 && f.Sha256.All(c => "0123456789abcdef".Contains(c))) && m.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == m.Files.Count, "routing-project-path/size/hash");
        Check(m.Status.HasValue == roles.Contains("generation") && (!m.Status.HasValue || m.Status == GearRoutingStatus.Complete || m.Status == GearRoutingStatus.IncompleteBudget || m.Status == GearRoutingStatus.Infeasible), "routing-project-cancelled/invalid-not-reproduction-baseline");
        Check(m.Status.HasValue || (!m.SearchComplete && !m.ResultTruncated && m.OrderedIds.Count == 0 && m.SelectedId == null), "routing-project-draft-results");
        Check(m.OrderedIds.Distinct(StringComparer.Ordinal).Count() == m.OrderedIds.Count && m.OrderedIds.All(x => x.StartsWith("sha256:", StringComparison.Ordinal)) && (m.SelectedId != null) == roles.Contains("mechanism") && (m.SelectedId == null || m.OrderedIds.Contains(m.SelectedId)), "routing-project-selection");
        ValidatePlayback(m.Playback);
        return Envelope(CompoundRoutingContract.ProjectFormat, w => {
            w.WriteStartObject(); w.WriteString("requestId", m.RequestId); w.WriteString("profile", m.Profile); w.WriteString("backend", m.Backend); w.WriteString("ranking", m.Ranking);
            if (m.Status.HasValue) w.WriteString("status", m.Status.ToString()); else w.WriteNull("status"); w.WriteBoolean("searchComplete", m.SearchComplete); w.WriteBoolean("resultTruncated", m.ResultTruncated);
            Array(w, "orderedIds", m.OrderedIds, (a, id) => a.WriteStringValue(id)); w.WriteString("selectedId", m.SelectedId); w.WriteString("contextId", m.ContextId);
            w.WriteStartObject("playback"); w.WritePropertyName("rootTurns"); R(w, m.Playback.RootTurns); w.WritePropertyName("turnsPerSecond"); R(w, m.Playback.TurnsPerSecond); w.WriteEndObject();
            Array(w, "files", m.Files, (a, f) => { a.WriteStartObject(); a.WriteString("role", f.Role); a.WriteString("path", f.Path); a.WriteNumber("bytes", f.Bytes); a.WriteString("sha256", f.Sha256); a.WriteEndObject(); }); w.WriteEndObject();
        });
    }
    public static CompoundRoutingProjectManifest Read(byte[] bytes)
    {
        using var d = Open(bytes, CompoundRoutingContract.ProjectFormat, 128 * 1024); var p = d.RootElement.GetProperty("payload"); var b = p.GetProperty("playback");
        var m = new CompoundRoutingProjectManifest(S(p, "requestId"), A(p, "files", 3).Select(f => new GearRoutingProjectFile(S(f, "role"), S(f, "path"), I(f, "bytes"), S(f, "sha256"))),
            p.GetProperty("status").ValueKind == JsonValueKind.Null ? null : E<GearRoutingStatus>(p, "status"), p.GetProperty("searchComplete").GetBoolean(), p.GetProperty("resultTruncated").GetBoolean(), A(p, "orderedIds", GearRoutingContract.MaxReturned).Select(x => x.GetString()!), p.GetProperty("selectedId").GetString(), new GearRoutingPlaybackSetup(R(b.GetProperty("rootTurns")), R(b.GetProperty("turnsPerSecond"))), S(p, "profile"), S(p, "backend"), S(p, "ranking"));
        Check(bytes.SequenceEqual(Write(m)), "routing-project-noncanonical/context"); return m;
    }
    public static void ValidatePlayback(GearRoutingPlaybackSetup p)
    {
        Check(new[] { p.RootTurns, p.TurnsPerSecond }.All(r => BigInteger.Abs(r.Numerator) <= GearRoutingContract.MaxScalar && r.Denominator <= GearRoutingContract.MaxScalar) && p.TurnsPerSecond >= new Rational(-1000) && p.TurnsPerSecond <= new Rational(1000), "routing-playback-bounds");
    }
}
