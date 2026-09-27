using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using static GearInvest.PortableProjectStorage;

namespace GearInvest;

public sealed class GearRoutingProject
{
    public GearRoutingProject(AnchoredGearRoutingRequest request, GearRoutingResult? generation = null, string? selectedId = null, GearRoutingPlaybackSetup? playback = null)
    { Request = request; Generation = generation; SelectedId = selectedId; Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8)); }
    public AnchoredGearRoutingRequest Request { get; }
    public GearRoutingResult? Generation { get; }
    public string? SelectedId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public GearRouteCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
}
public static class GearRoutingProjects
{
    private static void Need(bool yes, string message) { if (!yes) throw new FormatException(message); }
    public static void Validate(GearInvestSdk sdk, GearRoutingProject p)
    {
        var n = sdk.NormalizeGearRoutingRequest(p.Request); Need(n.IsValid, "InvalidRoutingProjectRequest"); GearRoutingProjectJson.ValidatePlayback(p.Playback);
        Need(p.SelectedId == null || p.Selected != null, "SelectionUnavailable");
        if (p.Generation != null) { Need(n.RequestId == p.Generation.RequestId && p.Generation.Status != GearRoutingStatus.Cancelled, "StaleOrCancelledRoutingGeneration"); GearRoutingJson.ValidateCached(p.Generation); }
        if (p.Selected != null) Need(sdk.Validate(sdk.WriteGearRouteArtifact(p.Selected).Artifact).IsValid, "SelectedMechanismInvalid");
    }
    public static GearRoutingProjectManifest Save(GearInvestSdk sdk, GearRoutingProject p, string directory, bool overwrite = false)
    {
        Validate(sdk, p); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root); var path = Resolve(root, "project.json");
        if (File.Exists(path)) { Need(overwrite, "ProjectExists: explicit Save overwrite required"); Load(sdk, root); } else Need(!Directory.EnumerateFileSystemEntries(root).Any(), "SaveAsRequiresEmptyDirectory");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["request"] = sdk.WriteGearRoutingRequest(p.Request) };
        if (p.Generation != null) files.Add("generation", sdk.WriteGearRoutingResult(p.Generation));
        if (p.Selected != null) files.Add("mechanism", sdk.WriteGearRouteArtifact(p.Selected).Bytes);
        var revision = Revision(files); var records = files.Select(f => new GearRoutingProjectFile(f.Key, "revisions/" + revision + "/" + f.Key + ".json", f.Value.Length, GearRoutingContract.Sha(f.Value)));
        var manifest = new GearRoutingProjectManifest(sdk.NormalizeGearRoutingRequest(p.Request).RequestId, records, p.Generation?.Status, p.Generation?.SearchComplete ?? false, p.Generation?.ResultTruncated ?? false, p.Generation?.Candidates.Select(c => c.CandidateId), p.SelectedId, p.Playback);
        Commit(root, revision, files, GearRoutingProjectJson.Write(manifest)); return manifest;
    }
    public static GearRoutingProject Load(GearInvestSdk sdk, string directory)
    {
        var root = Root(directory); var manifest = GearRoutingProjectJson.Read(ReadBounded(Resolve(root, "project.json"), 128 * 1024)); var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var f in manifest.Files) { var bytes = ReadBounded(Resolve(root, f.Path), f.Bytes); Need(bytes.Length == f.Bytes && GearRoutingContract.Sha(bytes) == f.Sha256, "RoutingProjectFileIntegrity:" + f.Role); files.Add(f.Role, bytes); }
        var revision = Revision(files); Need(manifest.Files.All(f => f.Path == "revisions/" + revision + "/" + f.Role + ".json"), "RoutingProjectRevisionMismatch");
        var r = sdk.ReadGearRoutingRequest(files["request"]); Need(sdk.NormalizeGearRoutingRequest(r).RequestId == manifest.RequestId, "RoutingProjectRequestIdentity");
        var g = files.ContainsKey("generation") ? sdk.ReadGearRoutingResult(files["generation"]) : null;
        Need(g?.Status == manifest.Status && (g?.SearchComplete ?? false) == manifest.SearchComplete && (g?.ResultTruncated ?? false) == manifest.ResultTruncated && (g?.Candidates.Select(c => c.CandidateId) ?? Array.Empty<string>()).SequenceEqual(manifest.OrderedIds), "RoutingProjectSummaryMismatch");
        var p = new GearRoutingProject(r, g, manifest.SelectedId, manifest.Playback); Validate(sdk, p);
        if (p.Selected != null) Need(files["mechanism"].SequenceEqual(sdk.WriteGearRouteArtifact(p.Selected).Bytes), "RoutingProjectSelectedArtifactMismatch"); return p;
    }
    public static GearRoutingProject Regenerate(GearInvestSdk sdk, GearRoutingProject saved, CancellationToken token = default, Action<int, int>? progress = null)
    {
        Validate(sdk, saved);
        // The producer receives only the normalized request. Stored results and selection are comparators, not search hints.
        var g = sdk.GenerateGearRoutes(saved.Request, token, progress); Need(g.Status != GearRoutingStatus.Cancelled, "Cancelled");
        if (saved.Generation != null) Need(sdk.WriteGearRoutingResult(g).SequenceEqual(sdk.WriteGearRoutingResult(saved.Generation)), "RegenerationDrift: ordered mechanisms/search/metrics/bytes");
        var fresh = new GearRoutingProject(g.Normalized.Request!, g, saved.SelectedId, saved.Playback); Validate(sdk, fresh);
        if (saved.Selected != null)
        {
            Need(fresh.Selected != null && sdk.WriteGearRouteArtifact(saved.Selected).Bytes.SequenceEqual(sdk.WriteGearRouteArtifact(fresh.Selected).Bytes), "RegenerationDrift: selected artifact");
            var before = sdk.Evaluate(saved.Selected.Mechanism, saved.Playback.RootTurns); var after = sdk.Evaluate(fresh.Selected!.Mechanism, fresh.Playback.RootTurns);
            Need(before.Values.Select(v => v.DofId + "=" + v.UnwrappedTurns).SequenceEqual(after.Values.Select(v => v.DofId + "=" + v.UnwrappedTurns)), "RegenerationDrift: exact playback");
        }
        return fresh;
    }
}
