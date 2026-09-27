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

public sealed class TwoOutputTransmissionProject
{
    public TwoOutputTransmissionProject(TwoOutputTransmissionGoal goal, TwoOutputTransmissionResult? generation = null, string? selectedId = null,
        GearRoutingPlaybackSetup? playback = null, IEnumerable<KeyValuePair<string, string>>? labels = null)
    {
        Goal = goal; Generation = generation; SelectedId = selectedId; Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8));
        var l = (labels ?? goal.Outputs.Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel))).Take(3).ToList(); if (l.Count != 2) throw new ArgumentException("Exactly two presentation labels.");
        Labels = l.OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public TwoOutputTransmissionGoal Goal { get; }
    public TwoOutputTransmissionResult? Generation { get; }
    public string? SelectedId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public ReadOnlyCollection<KeyValuePair<string, string>> Labels { get; }
    public TwoOutputTransmissionCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
}

public static class TwoOutputTransmissionProjects
{
    private static void Need(bool ok, string message) { if (!ok) throw new FormatException(message); }
    public static void Validate(GearInvestSdk sdk, TwoOutputTransmissionProject p)
    {
        var n = sdk.NormalizeTwoOutputTransmissionGoal(p.Goal); Need(n.IsValid, "InvalidTwoOutputGoalProject"); CompoundRoutingProjectJson.ValidatePlayback(p.Playback);
        Need(p.Labels.Select(l => l.Key).SequenceEqual(n.Goal!.Outputs.Select(o => o.Key)) && p.Labels.All(l => l.Value != null && l.Value.Length <= 128), "TwoOutputPresentationLabels");
        Need(p.SelectedId == null || p.Selected != null, "TwoOutputSelectionUnavailable");
        if (p.Generation != null)
        {
            Need(p.Generation.GoalId == n.GoalId && p.Generation.Status != TwoOutputTransmissionStatus.Cancelled && p.Generation.Status != TwoOutputTransmissionStatus.Failed, "StaleCancelledOrFailedTwoOutputResult");
            TwoOutputTransmissionJson.ValidateCached(p.Generation);
        }
        if (p.Selected != null) Need(sdk.ValidateTwoOutputTransmissionCandidate(p.Goal, p.Selected).IsValid && sdk.Validate(sdk.WriteTwoOutputTransmissionMechanism(p.Selected).Artifact).IsValid, "InvalidSelectedWholeTwoOutputMechanism");
    }
    public static TwoOutputTransmissionProjectManifest Save(GearInvestSdk sdk, TwoOutputTransmissionProject p, string directory, bool overwrite = false)
    {
        Validate(sdk, p); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root); var path = Resolve(root, "project.json");
        if (File.Exists(path)) { Need(overwrite, "ProjectExists: explicit Save required"); Load(sdk, root); } else Need(!Directory.EnumerateFileSystemEntries(root).Any(), "SaveAsRequiresEmptyDirectory");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["goal"] = sdk.WriteTwoOutputTransmissionGoal(p.Goal) };
        if (p.Generation != null) files.Add("generation", sdk.WriteTwoOutputTransmissionResult(p.Generation));
        if (p.Selected != null) files.Add("mechanism", sdk.WriteTwoOutputTransmissionMechanism(p.Selected).Bytes);
        var revision = Revision(files); var plan = sdk.CompileTwoOutputSharingSearchPlan(p.Goal);
        var m = new TwoOutputTransmissionProjectManifest(plan.GoalId, plan.PlanId,
            files.Select(f => new GearRoutingProjectFile(f.Key, "revisions/" + revision + "/" + f.Key + ".json", f.Value.Length, GearRoutingContract.Sha(f.Value))),
            p.Generation?.Status, p.Generation?.SearchComplete ?? false, p.Generation?.ResultTruncated ?? false, p.Generation?.Candidates.Select(c => c.CandidateId), p.SelectedId, p.Selected?.ContextId(plan.GoalId), p.Playback, p.Labels, sharingPolicy: p.Goal.SharingPolicy);
        Commit(root, revision, files, TwoOutputTransmissionProjectJson.Write(m)); return m;
    }
    public static TwoOutputTransmissionProject Load(GearInvestSdk sdk, string directory)
    {
        var root = Root(directory); var m = TwoOutputTransmissionProjectJson.Read(ReadBounded(Resolve(root, "project.json"), 128 * 1024)); var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var f in m.Files) { var bytes = ReadBounded(Resolve(root, f.Path), f.Bytes); Need(bytes.Length == f.Bytes && GearRoutingContract.Sha(bytes) == f.Sha256, "TwoOutputFileIntegrity:" + f.Role); files.Add(f.Role, bytes); }
        var revision = Revision(files); Need(m.Files.All(f => f.Path == "revisions/" + revision + "/" + f.Role + ".json"), "TwoOutputRevisionMismatch");
        var goal = sdk.ReadTwoOutputTransmissionGoal(files["goal"]); var plan = sdk.CompileTwoOutputSharingSearchPlan(goal); Need(goal.SharingPolicy == m.SharingPolicy && plan.GoalId == m.GoalId && plan.PlanId == m.PlanId, "TwoOutputFreshLoweringMismatch");
        var result = files.ContainsKey("generation") ? sdk.ReadTwoOutputTransmissionResult(files["generation"]) : null;
        Need(result?.Status == m.Status && (result?.SearchComplete ?? false) == m.SearchComplete && (result?.ResultTruncated ?? false) == m.ResultTruncated &&
            (result?.Candidates.Select(c => c.CandidateId) ?? Array.Empty<string>()).SequenceEqual(m.OrderedIds), "TwoOutputProjectSummaryMismatch");
        var p = new TwoOutputTransmissionProject(goal, result, m.SelectedId, m.Playback, m.Labels); Validate(sdk, p);
        if (p.Selected != null) Need(m.SelectedContextId == p.Selected.ContextId(plan.GoalId) && files["mechanism"].SequenceEqual(sdk.WriteTwoOutputTransmissionMechanism(p.Selected).Bytes), "TwoOutputSelectedContextOrArtifactMismatch");
        return p;
    }
    public static TwoOutputTransmissionProject Regenerate(GearInvestSdk sdk, TwoOutputTransmissionProject saved, CancellationToken token = default, Action<int, int>? progress = null)
    {
        Validate(sdk, saved);
        // Stored common goal only. Fresh repeats policy analysis, both child lowerings/searches and the whole common collector.
        var result = sdk.GenerateTwoOutputTransmissions(saved.Goal, token, progress); Need(result.Status != TwoOutputTransmissionStatus.Cancelled && result.Status != TwoOutputTransmissionStatus.Failed, "CancelledOrFailedTwoOutputRegeneration");
        if (saved.Generation != null) Need(sdk.WriteTwoOutputTransmissionResult(result).SequenceEqual(sdk.WriteTwoOutputTransmissionResult(saved.Generation)), "TwoOutputRegenerationDrift: full policy/child/allocation/observation/common-result bytes");
        var fresh = new TwoOutputTransmissionProject(result.Plan.Normalized.Goal!, result, saved.SelectedId, saved.Playback, saved.Labels); Validate(sdk, fresh);
        if (saved.Selected != null)
        {
            Need(fresh.Selected != null && sdk.WriteTwoOutputTransmissionMechanism(saved.Selected).Bytes.SequenceEqual(sdk.WriteTwoOutputTransmissionMechanism(fresh.Selected).Bytes), "TwoOutputRegenerationDrift: selected ID/artifact");
            Need(sdk.Evaluate(saved.Selected.Mechanism, saved.Playback.RootTurns).Values.Select(v => v.DofId + "=" + v.UnwrappedTurns).SequenceEqual(
                sdk.Evaluate(fresh.Selected!.Mechanism, fresh.Playback.RootTurns).Values.Select(v => v.DofId + "=" + v.UnwrappedTurns)), "TwoOutputRegenerationDrift: all exact channels");
        }
        return fresh;
    }
}
