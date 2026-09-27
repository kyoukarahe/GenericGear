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

public sealed class TransmissionGoalProject
{
    public TransmissionGoalProject(TransmissionGoal goal, TransmissionGenerationResult? generation = null, string? selectedId = null,
        GearRoutingPlaybackSetup? playback = null, string? selectedOriginId = null)
    {
        Goal = goal; Generation = generation; SelectedId = selectedId; Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8));
        SelectedOriginId = selectedOriginId ?? (selectedId == null ? null : generation?.Candidates.SingleOrDefault(c => c.CandidateId == selectedId)?.Origins[0].OriginId);
    }
    public TransmissionGoal Goal { get; }
    public TransmissionGenerationResult? Generation { get; }
    public string? SelectedId { get; }
    public string? SelectedOriginId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public TransmissionMechanismCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public TransmissionCandidateOrigin? SelectedOrigin => Selected?.Origins.SingleOrDefault(o => o.OriginId == SelectedOriginId);
}

public static class TransmissionGoalProjects
{
    private static void Need(bool ok, string message) { if (!ok) throw new FormatException(message); }
    public static void Validate(GearInvestSdk sdk, TransmissionGoalProject p)
    {
        var n = sdk.NormalizeTransmissionGoal(p.Goal); Need(n.IsValid, "InvalidTransmissionGoalProjectInput"); CompoundRoutingProjectJson.ValidatePlayback(p.Playback);
        Need(p.SelectedId == null ? p.SelectedOriginId == null : p.Selected != null && p.SelectedOrigin != null, "SelectionOrOriginUnavailable");
        if (p.Generation != null)
        {
            Need(n.GoalId == p.Generation.GoalId && p.Generation.Status != TransmissionGoalStatus.Cancelled && p.Generation.Status != TransmissionGoalStatus.Failed, "StaleCancelledOrFailedGoalGeneration");
            TransmissionGoalJson.ValidateCached(p.Generation);
        }
        if (p.Selected != null) Need(sdk.ValidateGoalTransmissionCandidate(p.Goal, p.Selected).IsValid && sdk.Validate(sdk.WriteTransmissionMechanismArtifact(p.SelectedOrigin!).Artifact).IsValid, "SelectedGoalMechanismInvalid");
    }
    public static TransmissionGoalProjectManifest Save(GearInvestSdk sdk, TransmissionGoalProject p, string directory, bool overwrite = false)
    {
        Validate(sdk, p); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root); var path = Resolve(root, "project.json");
        if (File.Exists(path)) { Need(overwrite, "ProjectExists: explicit Save required"); Load(sdk, root); } else Need(!Directory.EnumerateFileSystemEntries(root).Any(), "SaveAsRequiresEmptyDirectory");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["goal"] = sdk.WriteTransmissionGoal(p.Goal) };
        if (p.Generation != null) files.Add("generation", sdk.WriteTransmissionGenerationResult(p.Generation));
        if (p.SelectedOrigin != null) files.Add("mechanism", sdk.WriteTransmissionMechanismArtifact(p.SelectedOrigin).Bytes);
        var revision = Revision(files); var plan = sdk.CompileTransmissionSearchPlan(p.Goal);
        var records = files.Select(f => new GearRoutingProjectFile(f.Key, "revisions/" + revision + "/" + f.Key + ".json", f.Value.Length, GearRoutingContract.Sha(f.Value)));
        var m = new TransmissionGoalProjectManifest(plan.GoalId, plan.PlanId, records, p.Generation?.Status, p.Generation?.SearchComplete ?? false, p.Generation?.ResultTruncated ?? false,
            p.Generation?.Candidates.Select(c => c.CandidateId), p.SelectedId, p.SelectedOriginId, p.SelectedOrigin == null ? null : TransmissionGoalContract.ContextId(plan.GoalId, p.SelectedOrigin.RequestId, p.SelectedId!), p.Playback);
        Commit(root, revision, files, TransmissionGoalProjectJson.Write(m)); return m;
    }
    public static TransmissionGoalProject Load(GearInvestSdk sdk, string directory)
    {
        var root = Root(directory); var m = TransmissionGoalProjectJson.Read(ReadBounded(Resolve(root, "project.json"), 128 * 1024)); var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var f in m.Files) { var b = ReadBounded(Resolve(root, f.Path), f.Bytes); Need(b.Length == f.Bytes && GearRoutingContract.Sha(b) == f.Sha256, "GoalProjectFileIntegrity:" + f.Role); files.Add(f.Role, b); }
        var revision = Revision(files); Need(m.Files.All(f => f.Path == "revisions/" + revision + "/" + f.Role + ".json"), "GoalProjectRevisionMismatch");
        var goal = sdk.ReadTransmissionGoal(files["goal"]); var plan = sdk.CompileTransmissionSearchPlan(goal); Need(plan.GoalId == m.GoalId && plan.PlanId == m.PlanId, "GoalProjectFreshLoweringMismatch");
        var result = files.ContainsKey("generation") ? sdk.ReadTransmissionGenerationResult(files["generation"]) : null;
        Need(result?.Status == m.Status && (result?.SearchComplete ?? false) == m.SearchComplete && (result?.ResultTruncated ?? false) == m.ResultTruncated &&
            (result?.Candidates.Select(c => c.CandidateId) ?? Array.Empty<string>()).SequenceEqual(m.OrderedIds), "GoalProjectSummaryMismatch");
        var p = new TransmissionGoalProject(goal, result, m.SelectedId, m.Playback, m.SelectedOriginId); Validate(sdk, p);
        if (p.SelectedOrigin != null)
        {
            Need(m.SelectedContextId == TransmissionGoalContract.ContextId(plan.GoalId, p.SelectedOrigin.RequestId, p.SelectedId!), "GoalProjectSelectedContextMismatch");
            Need(files["mechanism"].SequenceEqual(sdk.WriteTransmissionMechanismArtifact(p.SelectedOrigin).Bytes), "GoalProjectSelectedArtifactMismatch");
        }
        return p;
    }
    public static TransmissionGoalProject Regenerate(GearInvestSdk sdk, TransmissionGoalProject saved, CancellationToken token = default, Action<int, int>? progress = null)
    {
        Validate(sdk, saved);
        // Only the saved goal reaches fresh normalization/proof/lowering/allocation/search/common ranking.
        var result = sdk.GenerateTransmissionMechanisms(saved.Goal, token, progress); Need(result.Status != TransmissionGoalStatus.Cancelled && result.Status != TransmissionGoalStatus.Failed, "CancelledOrFailedGoalRegeneration");
        if (saved.Generation != null) Need(sdk.WriteTransmissionGenerationResult(result).SequenceEqual(sdk.WriteTransmissionGenerationResult(saved.Generation)), "GoalRegenerationDrift: lowering/family outcomes/work/union/ranking/full bytes");
        var fresh = new TransmissionGoalProject(result.Plan.Normalized.Goal!, result, saved.SelectedId, saved.Playback, saved.SelectedOriginId); Validate(sdk, fresh);
        if (saved.SelectedOrigin != null)
        {
            Need(fresh.SelectedOrigin != null && sdk.WriteTransmissionMechanismArtifact(saved.SelectedOrigin).Bytes.SequenceEqual(sdk.WriteTransmissionMechanismArtifact(fresh.SelectedOrigin).Bytes), "GoalRegenerationDrift: selected origin/artifact");
            var before = sdk.Evaluate(saved.SelectedOrigin.Mechanism, saved.Playback.RootTurns); var after = sdk.Evaluate(fresh.SelectedOrigin!.Mechanism, fresh.Playback.RootTurns);
            Need(before.Values.Select(v => v.DofId + "=" + v.UnwrappedTurns).SequenceEqual(after.Values.Select(v => v.DofId + "=" + v.UnwrappedTurns)), "GoalRegenerationDrift: exact playback");
        }
        return fresh;
    }
}
