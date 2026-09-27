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

public sealed class TwoCompoundRoutingProject
{
    public TwoCompoundRoutingProject(AnchoredTwoCompoundGearRoutingRequest request,TwoCompoundGearRoutingResult? generation = null,string? selectedId = null,GearRoutingPlaybackSetup? playback = null)
    { Request = request; Generation = generation; SelectedId = selectedId; Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero,new Rational(1,8)); }
    public AnchoredTwoCompoundGearRoutingRequest Request { get; }
    public TwoCompoundGearRoutingResult? Generation { get; }
    public string? SelectedId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public TwoCompoundGearRouteCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
}
public static class TwoCompoundRoutingProjects
{
    private static void Need(bool yes,string message) { if (!yes) throw new FormatException(message); }
    public static void Validate(GearInvestSdk sdk,TwoCompoundRoutingProject p)
    {
        var n = sdk.NormalizeAnchoredTwoCompoundRoutingRequest(p.Request); Need(n.IsValid,"InvalidTwoCompoundRoutingProjectRequest"); CompoundRoutingProjectJson.ValidatePlayback(p.Playback);
        Need(p.SelectedId == null || p.Selected != null,"SelectionUnavailable");
        if (p.Generation != null) { Need(n.RequestId == p.Generation.RequestId && p.Generation.Status != GearRoutingStatus.Cancelled,"StaleOrCancelledRoutingGeneration"); TwoCompoundRoutingJson.ValidateCached(p.Generation); }
        if (p.Selected != null) Need(sdk.Validate(sdk.WriteTwoCompoundGearRouteArtifact(p.Selected).Artifact).IsValid,"SelectedMechanismInvalid");
    }
    public static TwoCompoundRoutingProjectManifest Save(GearInvestSdk sdk,TwoCompoundRoutingProject p,string directory,bool overwrite = false)
    {
        Validate(sdk,p); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root); var path = Resolve(root,"project.json");
        if (File.Exists(path)) { Need(overwrite,"ProjectExists: explicit Save overwrite required"); Load(sdk,root); } else Need(!Directory.EnumerateFileSystemEntries(root).Any(),"SaveAsRequiresEmptyDirectory");
        var files = new SortedDictionary<string,byte[]>(StringComparer.Ordinal) {["request"] = sdk.WriteTwoCompoundRoutingRequest(p.Request)};
        if (p.Generation != null) files.Add("generation",sdk.WriteTwoCompoundRoutingResult(p.Generation));
        if (p.Selected != null) files.Add("mechanism",sdk.WriteTwoCompoundGearRouteArtifact(p.Selected).Bytes);
        var revision = Revision(files); var records = files.Select(f => new GearRoutingProjectFile(f.Key,"revisions/" + revision + "/" + f.Key + ".json",f.Value.Length,GearRoutingContract.Sha(f.Value)));
        var manifest = new TwoCompoundRoutingProjectManifest(sdk.NormalizeAnchoredTwoCompoundRoutingRequest(p.Request).RequestId,records,p.Generation?.Status,p.Generation?.SearchComplete ?? false,p.Generation?.ResultTruncated ?? false,p.Generation?.Candidates.Select(c => c.CandidateId),p.SelectedId,p.Playback);
        Commit(root,revision,files,TwoCompoundRoutingProjectJson.Write(manifest)); return manifest;
    }
    public static TwoCompoundRoutingProject Load(GearInvestSdk sdk,string directory)
    {
        var root = Root(directory); var m = TwoCompoundRoutingProjectJson.Read(ReadBounded(Resolve(root,"project.json"),128*1024)); var files = new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
        foreach (var f in m.Files) { var bytes = ReadBounded(Resolve(root,f.Path),f.Bytes); Need(bytes.Length == f.Bytes && GearRoutingContract.Sha(bytes) == f.Sha256,"TwoCompoundProjectFileIntegrity:" + f.Role); files.Add(f.Role,bytes); }
        var revision = Revision(files); Need(m.Files.All(f => f.Path == "revisions/" + revision + "/" + f.Role + ".json"),"TwoCompoundProjectRevisionMismatch");
        var r = sdk.ReadTwoCompoundRoutingRequest(files["request"]); Need(sdk.NormalizeAnchoredTwoCompoundRoutingRequest(r).RequestId == m.RequestId,"TwoCompoundProjectRequestIdentity");
        var g = files.ContainsKey("generation") ? sdk.ReadTwoCompoundRoutingResult(files["generation"]) : null;
        Need(g?.Status == m.Status && (g?.SearchComplete ?? false) == m.SearchComplete && (g?.ResultTruncated ?? false) == m.ResultTruncated && (g?.Candidates.Select(c => c.CandidateId) ?? Array.Empty<string>()).SequenceEqual(m.OrderedIds),"TwoCompoundProjectSummaryMismatch");
        var p = new TwoCompoundRoutingProject(r,g,m.SelectedId,m.Playback); Validate(sdk,p);
        if (p.Selected != null) Need(files["mechanism"].SequenceEqual(sdk.WriteTwoCompoundGearRouteArtifact(p.Selected).Bytes),"TwoCompoundProjectSelectedArtifactMismatch"); return p;
    }
    public static TwoCompoundRoutingProject Regenerate(GearInvestSdk sdk,TwoCompoundRoutingProject saved,CancellationToken token = default,Action<int,int>? progress = null)
    {
        Validate(sdk,saved);
        // Only the request reaches the producer. Stored outputs/selection remain comparators, never search hints.
        var g = sdk.GenerateAnchoredTwoCompoundGearRoutes(saved.Request,token,progress); Need(g.Status != GearRoutingStatus.Cancelled,"Cancelled");
        if (saved.Generation != null) Need(sdk.WriteTwoCompoundRoutingResult(g).SequenceEqual(sdk.WriteTwoCompoundRoutingResult(saved.Generation)),"RegenerationDrift: ordered mechanisms/search/metrics/bytes");
        var fresh = new TwoCompoundRoutingProject(g.Normalized.Request!,g,saved.SelectedId,saved.Playback); Validate(sdk,fresh);
        if (saved.Selected != null)
        {
            Need(fresh.Selected != null && sdk.WriteTwoCompoundGearRouteArtifact(saved.Selected).Bytes.SequenceEqual(sdk.WriteTwoCompoundGearRouteArtifact(fresh.Selected).Bytes),"RegenerationDrift: selected artifact");
            var before = sdk.Evaluate(saved.Selected.Mechanism,saved.Playback.RootTurns); var after = sdk.Evaluate(fresh.Selected!.Mechanism,fresh.Playback.RootTurns);
            Need(before.Values.Select(v => v.DofId + "=" + v.UnwrappedTurns).SequenceEqual(after.Values.Select(v => v.DofId + "=" + v.UnwrappedTurns)),"RegenerationDrift: exact playback");
        }
        return fresh;
    }
}
