using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using static GearInvest.PortableProjectStorage;

namespace GearInvest;

public sealed class SharedPrefixTransmissionProject
{
    public SharedPrefixTransmissionProject(SharedPrefixTransmissionGoal goal, SharedPrefixTransmissionResult? generation = null, string? selectedId = null,
        GearRoutingPlaybackSetup? playback = null, IEnumerable<KeyValuePair<string, string>>? labels = null)
    {
        Goal = goal; Generation = generation; SelectedId = selectedId; Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8));
        var l = (labels ?? goal.Outputs.Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel))).Take(3).ToList(); if (l.Count != 2) throw new ArgumentException("Exactly two presentation labels.");
        Labels = l.OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public SharedPrefixTransmissionGoal Goal { get; }
    public SharedPrefixTransmissionResult? Generation { get; }
    public string? SelectedId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public ReadOnlyCollection<KeyValuePair<string, string>> Labels { get; }
    public SharedPrefixTransmissionCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
}

public static class SharedPrefixTransmissionProjects
{
    private static void Need(bool ok, string message) { if (!ok) throw new FormatException(message); }
    public static void Validate(GearInvestSdk sdk, SharedPrefixTransmissionProject p)
    {
        var n = sdk.NormalizeSharedPrefixTransmissionGoal(p.Goal); Need(n.IsValid, "InvalidSharedPrefixGoalProject"); CompoundRoutingProjectJson.ValidatePlayback(p.Playback);
        Need(p.Labels.Select(l => l.Key).SequenceEqual(n.Goal!.Outputs.Select(o => o.Key)) && p.Labels.All(l => l.Value != null && l.Value.Length <= 128), "SharedPrefixPresentationLabels");
        Need(p.SelectedId == null || p.Selected != null, "SharedPrefixSelectionUnavailable");
        if (p.Generation != null)
        {
            Need(p.Generation.GoalId == n.GoalId && p.Generation.Status != SharedPrefixTransmissionStatus.Cancelled && p.Generation.Status != SharedPrefixTransmissionStatus.Failed, "StaleCancelledOrFailedSharedPrefixResult");
            SharedPrefixTransmissionJson.ValidateCached(p.Generation);
        }
        if (p.Selected != null) Need(sdk.ValidateSharedPrefixTransmissionCandidate(p.Goal, p.Selected).IsValid && sdk.Validate(sdk.WriteSharedPrefixTransmissionMechanism(p.Selected).Artifact).IsValid, "InvalidSelectedWholeSharedPrefixMechanism");
    }
    public static SharedPrefixTransmissionProjectManifest Save(GearInvestSdk sdk, SharedPrefixTransmissionProject p, string directory, bool overwrite = false)
    {
        Validate(sdk, p); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root); var path = Resolve(root, "project.json");
        if (File.Exists(path)) { Need(overwrite, "ProjectExists: explicit Save required"); Load(sdk, root); } else Need(!Directory.EnumerateFileSystemEntries(root).Any(), "SaveAsRequiresEmptyDirectory");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["goal"] = sdk.WriteSharedPrefixTransmissionGoal(p.Goal) };
        if (p.Generation != null) files.Add("generation", sdk.WriteSharedPrefixTransmissionResult(p.Generation));
        if (p.Selected != null) files.Add("mechanism", sdk.WriteSharedPrefixTransmissionMechanism(p.Selected).Bytes);
        var revision = Revision(files); var plan = sdk.CompileSharedPrefixTransmissionSearchPlan(p.Goal);
        var m = new SharedPrefixTransmissionProjectManifest(plan.GoalId, plan.PlanId,
            files.Select(f => new GearRoutingProjectFile(f.Key, "revisions/" + revision + "/" + f.Key + ".json", f.Value.Length, GearRoutingContract.Sha(f.Value))),
            p.Generation?.Status, p.Generation?.SearchComplete ?? false, p.Generation?.ResultTruncated ?? false, p.Generation?.Candidates.Select(c => c.CandidateId), p.SelectedId, p.Selected?.ContextId(plan.GoalId), p.Playback, p.Labels);
        Commit(root, revision, files, SharedPrefixTransmissionProjectJson.Write(m)); return m;
    }
    public static SharedPrefixTransmissionProject Load(GearInvestSdk sdk, string directory)
    {
        var root = Root(directory); var m = SharedPrefixTransmissionProjectJson.Read(ReadBounded(Resolve(root, "project.json"), 128 * 1024)); var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var f in m.Files) { var bytes = ReadBounded(Resolve(root, f.Path), f.Bytes); Need(bytes.Length == f.Bytes && GearRoutingContract.Sha(bytes) == f.Sha256, "SharedPrefixFileIntegrity:" + f.Role); files.Add(f.Role, bytes); }
        var revision = Revision(files); Need(m.Files.All(f => f.Path == "revisions/" + revision + "/" + f.Role + ".json"), "SharedPrefixRevisionMismatch");
        var goal = sdk.ReadSharedPrefixTransmissionGoal(files["goal"]); var plan = sdk.CompileSharedPrefixTransmissionSearchPlan(goal); Need(plan.GoalId == m.GoalId && plan.PlanId == m.PlanId, "SharedPrefixFreshLoweringMismatch");
        var result = files.ContainsKey("generation") ? sdk.ReadSharedPrefixTransmissionResult(files["generation"]) : null;
        Need(result?.Status == m.Status && (result?.SearchComplete ?? false) == m.SearchComplete && (result?.ResultTruncated ?? false) == m.ResultTruncated &&
            (result?.Candidates.Select(c => c.CandidateId) ?? Array.Empty<string>()).SequenceEqual(m.OrderedIds), "SharedPrefixProjectSummaryMismatch");
        var p = new SharedPrefixTransmissionProject(goal, result, m.SelectedId, m.Playback, m.Labels); Validate(sdk, p);
        if (p.Selected != null) Need(m.SelectedContextId == p.Selected.ContextId(plan.GoalId) && files["mechanism"].SequenceEqual(sdk.WriteSharedPrefixTransmissionMechanism(p.Selected).Bytes), "SharedPrefixSelectedContextOrArtifactMismatch");
        return p;
    }
    public static SharedPrefixTransmissionProject Regenerate(GearInvestSdk sdk, SharedPrefixTransmissionProject saved, CancellationToken token = default, Action<int, int>? progress = null)
    {
        Validate(sdk, saved);
        // Stored goal only. No cached hypothesis, pool, triple, binding, rank or coefficient feeds the actual search.
        var result = sdk.GenerateSharedPrefixTransmissions(saved.Goal, token, progress); Need(result.Status != SharedPrefixTransmissionStatus.Cancelled && result.Status != SharedPrefixTransmissionStatus.Failed, "CancelledOrFailedSharedPrefixRegeneration");
        if (saved.Generation != null) Need(sdk.WriteSharedPrefixTransmissionResult(result).SequenceEqual(sdk.WriteSharedPrefixTransmissionResult(saved.Generation)), "SharedPrefixRegenerationDrift: full hypothesis/prefix/suffix/triple/result bytes");
        var fresh = new SharedPrefixTransmissionProject(result.Plan.Normalized.Goal!, result, saved.SelectedId, saved.Playback, saved.Labels); Validate(sdk, fresh);
        if (saved.Selected != null)
        {
            Need(fresh.Selected != null && sdk.WriteSharedPrefixTransmissionMechanism(saved.Selected).Bytes.SequenceEqual(sdk.WriteSharedPrefixTransmissionMechanism(fresh.Selected).Bytes), "SharedPrefixRegenerationDrift: selected ID/artifact");
            Need(sdk.Evaluate(saved.Selected.Mechanism, saved.Playback.RootTurns).Values.Select(v => v.DofId + "=" + v.UnwrappedTurns).SequenceEqual(
                sdk.Evaluate(fresh.Selected!.Mechanism, fresh.Playback.RootTurns).Values.Select(v => v.DofId + "=" + v.UnwrappedTurns)), "SharedPrefixRegenerationDrift: all exact channels");
        }
        return fresh;
    }
}
