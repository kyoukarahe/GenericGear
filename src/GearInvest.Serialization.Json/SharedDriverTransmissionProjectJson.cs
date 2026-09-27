using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using static GearInvest.Serialization.DiscreteEmbodimentJson;

namespace GearInvest.Serialization;

public static class SharedDriverTransmissionProjectJson
{
    public static byte[] Write(SharedDriverTransmissionProjectManifest m)
    {
        Check(m.Profile == SharedDriverTransmissionContract.Profile && m.Lowering == SharedDriverTransmissionContract.Lowering && m.Allocation == SharedDriverTransmissionContract.Allocation &&
            m.Join == SharedDriverTransmissionContract.Join && m.Ranking == SharedDriverTransmissionContract.Ranking && m.Resources == SharedDriverTransmissionContract.Resources, "shared-project-policy/version");
        Check(m.BackendFingerprints.SequenceEqual(new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }.Select(TransmissionGoalContract.Fingerprint)), "shared-project-backend/version");
        Check(m.GoalId.StartsWith("shared-driver-goal-sha256:", StringComparison.Ordinal) && m.PlanId.StartsWith("shared-driver-plan-sha256:", StringComparison.Ordinal), "shared-project-identity");
        var roles = m.Files.Select(f => f.Role).ToArray(); Check(roles.Contains("goal") && roles.Distinct().Count() == roles.Length && roles.All(r => r == "goal" || r == "generation" || r == "mechanism"), "shared-project-member-roles");
        Check(m.Files.All(f => DiscreteLayoutProjectJson.SafeRelativePath(f.Path) && f.Bytes > 0 && f.Bytes <= (f.Role == "generation" ? SharedDriverTransmissionContract.MaxResultBytes : 512 * 1024) &&
            f.Sha256.Length == 64 && f.Sha256.All(c => "0123456789abcdef".Contains(c))) && m.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == m.Files.Count, "shared-project-path/bytes/hash");
        Check(m.Status.HasValue == roles.Contains("generation") && (!m.Status.HasValue || m.Status != SharedDriverTransmissionStatus.Cancelled && m.Status != SharedDriverTransmissionStatus.Failed && m.Status != SharedDriverTransmissionStatus.InvalidInput), "shared-project-saveable-status");
        Check(m.Status.HasValue || !m.SearchComplete && !m.ResultTruncated && m.OrderedIds.Count == 0 && m.SelectedId == null, "shared-project-draft-results");
        Check(m.OrderedIds.Distinct(StringComparer.Ordinal).Count() == m.OrderedIds.Count && m.OrderedIds.All(id => id.StartsWith("sha256:", StringComparison.Ordinal)), "shared-project-ordered-identities");
        Check((m.SelectedId != null) == roles.Contains("mechanism") && (m.SelectedId != null) == (m.SelectedContextId != null) && (m.SelectedId == null || m.OrderedIds.Contains(m.SelectedId)), "shared-project-selected-context");
        Check(m.Labels.Count == 2 && m.Labels.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == 2 && m.Labels.All(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.Length <= 64 && x.Value != null && x.Value.Length <= 128), "shared-project-presentation-labels");
        CompoundRoutingProjectJson.ValidatePlayback(m.Playback);
        return Envelope(SharedDriverTransmissionContract.ProjectFormat, w => {
            w.WriteStartObject(); w.WriteString("goalId", m.GoalId); w.WriteString("planId", m.PlanId); w.WriteString("profile", m.Profile); w.WriteString("lowering", m.Lowering); w.WriteString("allocation", m.Allocation);
            w.WriteString("join", m.Join); w.WriteString("ranking", m.Ranking); w.WriteString("resources", m.Resources); Array(w, "backendFingerprints", m.BackendFingerprints, (a, f) => a.WriteStringValue(f));
            if (m.Status.HasValue) w.WriteString("status", m.Status.ToString()); else w.WriteNull("status"); w.WriteBoolean("searchComplete", m.SearchComplete); w.WriteBoolean("resultTruncated", m.ResultTruncated);
            Array(w, "orderedIds", m.OrderedIds, (a, id) => a.WriteStringValue(id)); w.WriteString("selectedId", m.SelectedId); w.WriteString("selectedContextId", m.SelectedContextId);
            w.WriteStartObject("playback"); w.WritePropertyName("rootTurns"); R(w, m.Playback.RootTurns); w.WritePropertyName("turnsPerSecond"); R(w, m.Playback.TurnsPerSecond); w.WriteEndObject();
            Array(w, "labels", m.Labels, (a, l) => { a.WriteStartObject(); a.WriteString("outputKey", l.Key); a.WriteString("label", l.Value); a.WriteEndObject(); });
            Array(w, "files", m.Files, (a, f) => { a.WriteStartObject(); a.WriteString("role", f.Role); a.WriteString("path", f.Path); a.WriteNumber("bytes", f.Bytes); a.WriteString("sha256", f.Sha256); a.WriteEndObject(); }); w.WriteEndObject();
        });
    }
    public static SharedDriverTransmissionProjectManifest Read(byte[] bytes)
    {
        using var d = Open(bytes, SharedDriverTransmissionContract.ProjectFormat, 128 * 1024); var p = d.RootElement.GetProperty("payload"); var b = p.GetProperty("playback");
        var m = new SharedDriverTransmissionProjectManifest(S(p, "goalId"), S(p, "planId"), A(p, "files", 3).Select(f => new GearRoutingProjectFile(S(f, "role"), S(f, "path"), I(f, "bytes"), S(f, "sha256"))),
            p.GetProperty("status").ValueKind == JsonValueKind.Null ? null : E<SharedDriverTransmissionStatus>(p, "status"), p.GetProperty("searchComplete").GetBoolean(), p.GetProperty("resultTruncated").GetBoolean(),
            A(p, "orderedIds", 32).Select(x => x.GetString()!), p.GetProperty("selectedId").GetString(), p.GetProperty("selectedContextId").GetString(),
            new GearRoutingPlaybackSetup(R(b.GetProperty("rootTurns")), R(b.GetProperty("turnsPerSecond"))), A(p, "labels", 2).Select(l => new KeyValuePair<string, string>(S(l, "outputKey"), S(l, "label"))),
            S(p, "profile"), S(p, "lowering"), S(p, "allocation"), S(p, "join"), S(p, "ranking"), S(p, "resources"), A(p, "backendFingerprints", 3).Select(x => x.GetString()!));
        Check(bytes.SequenceEqual(Write(m)), "shared-project-noncanonical/context"); return m;
    }
}
