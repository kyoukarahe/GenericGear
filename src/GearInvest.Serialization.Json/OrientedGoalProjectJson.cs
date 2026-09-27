using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public sealed class OrientedGoalProjectFile
{
    public OrientedGoalProjectFile(string role, string path, int bytes, string sha256) { Role = role; Path = path; Bytes = bytes; Sha256 = sha256; }
    public string Role { get; }
    public string Path { get; }
    public int Bytes { get; }
    public string Sha256 { get; }
}

public sealed class OrientedGoalProjectManifest
{
    public OrientedGoalProjectManifest(string goalId, string revision, IEnumerable<OrientedGoalProjectFile> files,
        string? selectedCandidateId, string? selectedOriginId, Rational rootTurns, string profile = OrientedGoalProfile.Id)
    { GoalId = goalId; Revision = revision; Files = files.OrderBy(f => f.Role, StringComparer.Ordinal).ToList().AsReadOnly(); SelectedCandidateId = selectedCandidateId; SelectedOriginId = selectedOriginId; RootTurns = rootTurns; Profile = profile; }
    public string GoalId { get; }
    public string Revision { get; }
    public ReadOnlyCollection<OrientedGoalProjectFile> Files { get; }
    public string? SelectedCandidateId { get; }
    public string? SelectedOriginId { get; }
    public Rational RootTurns { get; }
    public string Profile { get; }
}

public static class OrientedGoalProjectJson
{
    public static byte[] Write(OrientedGoalProjectManifest m) => OrientedGoalJson.Document(w => {
        OrientedGoalJson.Start(w, OrientedGoalJson.ProjectFormat, m.Profile); w.WriteString("goalId", m.GoalId); w.WriteString("revision", m.Revision);
        Array(w, "files", m.Files, (a, f) => { a.WriteStartObject(); a.WriteString("role", f.Role); a.WriteString("path", f.Path); Integer(a, "bytes", f.Bytes); a.WriteString("sha256", f.Sha256); a.WriteEndObject(); });
        if (m.SelectedCandidateId is null) w.WriteNull("selectedCandidateId"); else w.WriteString("selectedCandidateId", m.SelectedCandidateId);
        if (m.SelectedOriginId is null) w.WriteNull("selectedOriginId"); else w.WriteString("selectedOriginId", m.SelectedOriginId);
        Fraction(w, "rootTurns", m.RootTurns); w.WriteEndObject();
    });
    public static OrientedGoalProjectManifest Read(byte[] bytes) => CheckedRead(() => {
        using var doc = Open(bytes); var p = doc.RootElement; var profile = OrientedGoalJson.CheckStart(p, OrientedGoalJson.ProjectFormat);
        var files = Items(p, "files", 3).Select(f => new OrientedGoalProjectFile(S(f, "role"), S(f, "path"), I(f, "bytes"), S(f, "sha256"))).ToArray();
        Require(files.Length >= 1 && files.Select(f => f.Role).Distinct(StringComparer.Ordinal).Count() == files.Length && files.Any(f => f.Role == "goal") &&
            files.All(f => new[] { "goal", "result", "mechanism" }.Contains(f.Role) && f.Bytes > 0 && f.Bytes <= Engine.OrientedGoalProfile.MaxDocumentBytes && Engine.OrientedGoalKeys.IsHash(f.Sha256)), "Invalid project file inventory.");
        var m = new OrientedGoalProjectManifest(S(p, "goalId"), S(p, "revision"), files,
            p.GetProperty("selectedCandidateId").ValueKind == JsonValueKind.Null ? null : S(p, "selectedCandidateId"), p.GetProperty("selectedOriginId").ValueKind == JsonValueKind.Null ? null : S(p, "selectedOriginId"), F(p.GetProperty("rootTurns")), profile);
        Require(bytes.SequenceEqual(Write(m)), "Noncanonical/unknown project fields."); return m;
    });
}
