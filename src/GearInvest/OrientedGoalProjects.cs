using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using static GearInvest.PortableProjectStorage;

namespace GearInvest;

public sealed class OrientedGoalProject
{
    public OrientedGoalProject(OrientedTwoOutputGoal goal, OrientedGoalGeneration? generation = null, string? selectedCandidateId = null,
        string? selectedOriginId = null, Rational rootTurns = default)
    { Goal = goal; Generation = generation; SelectedCandidateId = selectedCandidateId; SelectedOriginId = selectedOriginId; RootTurns = rootTurns; }
    public OrientedTwoOutputGoal Goal { get; }
    public OrientedGoalGeneration? Generation { get; }
    public string? SelectedCandidateId { get; }
    public string? SelectedOriginId { get; }
    public Rational RootTurns { get; }
    public OrientedGoalCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedCandidateId);
    public OrientedGoalOrigin? SelectedOrigin => Selected?.Origins.SingleOrDefault(o => o.Id == SelectedOriginId);
}

public sealed partial class GearInvestSdk
{
    public void ValidateOrientedGoalProject(OrientedGoalProject p)
    {
        RequireOrientedGoalPlan(p.Goal);
        NeedOrientedGoal(p.SelectedCandidateId is null ? p.SelectedOriginId is null : p.Selected is not null && p.SelectedOrigin is not null, "SelectionOrOriginUnavailable: no rank fallback.");
        if (p.Generation is not null) ValidateOrientedGoalResult(p.Goal, p.Generation);
        NeedOrientedGoal(OrientedGoalKeys.F(p.RootTurns).Length <= 257, "Playback exact number bound exceeded.");
    }

    /// <summary>Exports the selected origin's original 19A artifact without running search. Validates its exact context first.</summary>
    public OrientedTwoOutputArtifactWriteResult ExportOrientedGoalSelection(OrientedGoalProject p)
    {
        ValidateOrientedGoalProject(p); NeedOrientedGoal(p.Selected is not null && p.SelectedOrigin is not null, "SelectionOrOriginUnavailable.");
        var plan = RequireOrientedGoalPlan(p.Goal); var model = ReadOrientedTwoOutputArtifact(p.Selected!.Artifact.Bytes).Mechanism;
        var written = WriteOrientedTwoOutputArtifact(model, LowerOrientedGoalTuple(plan, p.SelectedOrigin!.Tuple));
        NeedOrientedGoal(written.Artifact.CandidateId == p.SelectedCandidateId && written.Artifact.ArtifactHash == p.SelectedOrigin.ArtifactHash, "Selected artifact/context drift."); return written;
    }

    /// <summary>Rebuilds ONLY the selected 19A request. Does not prove search completeness or rerank cached candidates.</summary>
    public OrientedTwoOutputArtifactWriteResult RebuildOrientedGoalSelection(OrientedGoalProject p) => RebuildOrientedTwoOutput(ExportOrientedGoalSelection(p).Artifact);

    public OrientedGoalProjectManifest SaveOrientedGoalProject(OrientedGoalProject p, string directory, bool overwrite = false)
    {
        ValidateOrientedGoalProject(p); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root); var manifestPath = Resolve(root, "project.json");
        if (File.Exists(manifestPath)) { NeedOrientedGoal(overwrite, "ProjectExists: explicit Save required."); LoadOrientedGoalProject(root); }
        else NeedOrientedGoal(!Directory.EnumerateFileSystemEntries(root).Any(), "SaveAsRequiresEmptyDirectory.");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["goal"] = WriteOrientedGoal(p.Goal) };
        if (p.Generation is not null) files.Add("result", WriteOrientedGoalResult(p.Generation));
        if (p.Selected is not null) files.Add("mechanism", ExportOrientedGoalSelection(p).Bytes);
        var revision = Revision(files); var plan = RequireOrientedGoalPlan(p.Goal);
        var records = files.Select(f => new OrientedGoalProjectFile(f.Key, "revisions/" + revision + "/" + f.Key + ".json", f.Value.Length, OrientedGoalKeys.Hash(f.Value)));
        var manifest = new OrientedGoalProjectManifest(plan.GoalId, revision, records, p.SelectedCandidateId, p.SelectedOriginId, p.RootTurns, p.Goal.Profile);
        Commit(root, revision, files, OrientedGoalProjectJson.Write(manifest)); LoadOrientedGoalProject(root); return manifest;
    }

    public OrientedGoalProject LoadOrientedGoalProject(string directory)
    {
        var root = Root(directory); var m = OrientedGoalProjectJson.Read(ReadBounded(Resolve(root, "project.json"), 128 * 1024));
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var f in m.Files)
        {
            var bytes = ReadBounded(Resolve(root, f.Path), f.Bytes);
            NeedOrientedGoal(bytes.Length == f.Bytes && OrientedGoalKeys.Hash(bytes) == f.Sha256, "Project file integrity mismatch: " + f.Role); files.Add(f.Role, bytes);
        }
        var revision = Revision(files); NeedOrientedGoal(m.Revision == revision && m.Files.All(f => f.Path == "revisions/" + revision + "/" + f.Role + ".json"), "Project revision/path mismatch.");
        var goal = ReadOrientedGoal(files["goal"]); NeedOrientedGoal(RequireOrientedGoalPlan(goal).GoalId == m.GoalId && goal.Profile == m.Profile, "Project goal identity/policy mismatch.");
        var result = files.TryGetValue("result", out var resultBytes) ? ReadOrientedGoalResult(goal, resultBytes) : null;
        var project = new OrientedGoalProject(goal, result, m.SelectedCandidateId, m.SelectedOriginId, m.RootTurns); ValidateOrientedGoalProject(project);
        NeedOrientedGoal(files.ContainsKey("mechanism") == (project.Selected is not null), "Selected mechanism file inventory mismatch.");
        if (project.Selected is not null) NeedOrientedGoal(ExportOrientedGoalSelection(project).Bytes.SequenceEqual(files["mechanism"]), "Selected mechanism/origin bytes mismatch.");
        return project;
    }

    /// <summary>Fresh finite search from saved goal only, then compares complete status/ledger/order/contexts and preserves explicit selection.</summary>
    public OrientedGoalProject RegenerateOrientedGoalProject(OrientedGoalProject saved, CancellationToken token = default, Action<int, int>? progress = null)
    {
        ValidateOrientedGoalProject(saved);
        var fresh = GenerateOrientedTwoOutput(saved.Goal, token, progress);
        NeedOrientedGoal(fresh.Status != OrientedGoalStatus.Cancelled && fresh.Status != OrientedGoalStatus.Failed, "Goal regeneration interrupted or failed.");
        if (saved.Generation is not null) NeedOrientedGoal(WriteOrientedGoalResult(fresh).SequenceEqual(WriteOrientedGoalResult(saved.Generation)), "GoalRegenerationDrift: fresh search status/coverage/ranking/origins differ from cached claim.");
        var result = new OrientedGoalProject(saved.Goal, fresh, saved.SelectedCandidateId, saved.SelectedOriginId, saved.RootTurns); ValidateOrientedGoalProject(result);
        if (saved.Selected is not null) NeedOrientedGoal(ExportOrientedGoalSelection(result).Bytes.SequenceEqual(ExportOrientedGoalSelection(saved).Bytes), "GoalRegenerationDrift: selected canonical bytes.");
        return result;
    }
}
