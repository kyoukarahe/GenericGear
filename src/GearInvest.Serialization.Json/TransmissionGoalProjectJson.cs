using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using static GearInvest.Serialization.DiscreteEmbodimentJson;

namespace GearInvest.Serialization;

public static class TransmissionGoalProjectJson
{
    public static byte[] Write(TransmissionGoalProjectManifest m)
    {
        Check(m.Profile == TransmissionGoalContract.Profile && m.Lowering == TransmissionGoalContract.Lowering && m.Allocation == TransmissionGoalContract.Allocation &&
            m.Ranking == TransmissionGoalContract.Ranking && m.Resources == TransmissionGoalContract.Resources, "goal-project-policy/version");
        Check(m.BackendFingerprints.SequenceEqual(new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }.Select(TransmissionGoalContract.Fingerprint)), "goal-project-backend-version");
        Check(m.GoalId.StartsWith("transmission-goal-sha256:", StringComparison.Ordinal) && m.PlanId.StartsWith("transmission-search-plan-sha256:", StringComparison.Ordinal), "goal-project-identities");
        var roles = m.Files.Select(f => f.Role).ToArray(); Check(roles.Contains("goal") && roles.Distinct().Count() == roles.Length && roles.All(r => r == "goal" || r == "generation" || r == "mechanism"), "goal-project-member-roles");
        Check(m.Files.All(f => DiscreteLayoutProjectJson.SafeRelativePath(f.Path) && f.Bytes > 0 && f.Bytes <= (f.Role == "goal" ? TransmissionGoalContract.MaxGoalBytes : f.Role == "mechanism" ? 512 * 1024 : TransmissionGoalContract.MaxResultBytes) &&
            f.Sha256.Length == 64 && f.Sha256.All(c => "0123456789abcdef".Contains(c))) && m.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == m.Files.Count, "goal-project-path/size/hash");
        Check(m.Status.HasValue == roles.Contains("generation") && (!m.Status.HasValue || m.Status == TransmissionGoalStatus.Complete || m.Status == TransmissionGoalStatus.IncompleteBudget || m.Status == TransmissionGoalStatus.Infeasible || m.Status == TransmissionGoalStatus.Unsupported), "goal-project-saveable-status");
        Check(m.Status.HasValue || (!m.SearchComplete && !m.ResultTruncated && m.OrderedIds.Count == 0 && m.SelectedId == null), "goal-project-draft-results");
        Check(m.OrderedIds.Distinct(StringComparer.Ordinal).Count() == m.OrderedIds.Count && m.OrderedIds.All(id => id.StartsWith("sha256:", StringComparison.Ordinal)), "goal-project-ordered-identities");
        Check((m.SelectedId != null) == roles.Contains("mechanism") && (m.SelectedId != null) == (m.SelectedOriginId != null) && (m.SelectedId != null) == (m.SelectedContextId != null) &&
            (m.SelectedId == null || m.OrderedIds.Contains(m.SelectedId)), "goal-project-selection-origin-context");
        CompoundRoutingProjectJson.ValidatePlayback(m.Playback);
        return Envelope(TransmissionGoalContract.ProjectFormat, w => {
            w.WriteStartObject(); w.WriteString("goalId", m.GoalId); w.WriteString("planId", m.PlanId); w.WriteString("profile", m.Profile); w.WriteString("lowering", m.Lowering);
            w.WriteString("allocation", m.Allocation); w.WriteString("ranking", m.Ranking); w.WriteString("resources", m.Resources); Array(w, "backendFingerprints", m.BackendFingerprints, (a, f) => a.WriteStringValue(f));
            if (m.Status.HasValue) w.WriteString("status", m.Status.ToString()); else w.WriteNull("status"); w.WriteBoolean("searchComplete", m.SearchComplete); w.WriteBoolean("resultTruncated", m.ResultTruncated);
            Array(w, "orderedIds", m.OrderedIds, (a, id) => a.WriteStringValue(id)); w.WriteString("selectedId", m.SelectedId); w.WriteString("selectedOriginId", m.SelectedOriginId); w.WriteString("selectedContextId", m.SelectedContextId);
            w.WriteStartObject("playback"); w.WritePropertyName("rootTurns"); R(w, m.Playback.RootTurns); w.WritePropertyName("turnsPerSecond"); R(w, m.Playback.TurnsPerSecond); w.WriteEndObject();
            Array(w, "files", m.Files, (a, f) => { a.WriteStartObject(); a.WriteString("role", f.Role); a.WriteString("path", f.Path); a.WriteNumber("bytes", f.Bytes); a.WriteString("sha256", f.Sha256); a.WriteEndObject(); }); w.WriteEndObject();
        });
    }
    public static TransmissionGoalProjectManifest Read(byte[] bytes)
    {
        using var d = Open(bytes, TransmissionGoalContract.ProjectFormat, 128 * 1024); var p = d.RootElement.GetProperty("payload"); var b = p.GetProperty("playback");
        var m = new TransmissionGoalProjectManifest(S(p, "goalId"), S(p, "planId"), A(p, "files", 3).Select(f => new GearRoutingProjectFile(S(f, "role"), S(f, "path"), I(f, "bytes"), S(f, "sha256"))),
            p.GetProperty("status").ValueKind == JsonValueKind.Null ? null : E<TransmissionGoalStatus>(p, "status"), p.GetProperty("searchComplete").GetBoolean(), p.GetProperty("resultTruncated").GetBoolean(),
            A(p, "orderedIds", 128).Select(x => x.GetString()!), p.GetProperty("selectedId").GetString(), p.GetProperty("selectedOriginId").GetString(), p.GetProperty("selectedContextId").GetString(),
            new GearRoutingPlaybackSetup(R(b.GetProperty("rootTurns")), R(b.GetProperty("turnsPerSecond"))), S(p, "profile"), S(p, "lowering"), S(p, "allocation"), S(p, "ranking"), S(p, "resources"), A(p, "backendFingerprints", 3).Select(x => x.GetString()!));
        Check(bytes.SequenceEqual(Write(m)), "goal-project-noncanonical/context"); return m;
    }
}
