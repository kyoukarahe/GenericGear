using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

public sealed class SharedDriverTransmissionAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly RoutingAuthoringSession<SharedDriverTransmissionProject> session;
    private KeyValuePair<string, string>[] labels = Array.Empty<KeyValuePair<string, string>>();
    public SharedDriverTransmissionAuthoringController(GearInvestSdk? sdk = null, Func<Func<SharedDriverTransmissionProject>, Task<SharedDriverTransmissionProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault(); session = new RoutingAuthoringSession<SharedDriverTransmissionProject>(scheduler,
            p => this.sdk.NormalizeSharedDriverTransmissionGoal(p.Goal).GoalId, p => Map(p.Generation?.Status), (p, id) => p.Generation?.Candidates.Any(c => c.CandidateId == id) == true,
            (p, id, b) => new SharedDriverTransmissionProject(p.Goal, p.Generation, id, b, labels),
            p => p.Generation!.Status + "; generation=" + p.Generation.ConsumedGenerationWork + "; pair=" + p.Generation.ExaminedPairs + "/" + p.Generation.ObservedPairDomain + "; " + p.Generation.LimitingReason + "; " + p.Generation.RankingGuarantee,
            p => p.SelectedId, p => p.Playback);
    }
    private static GearRoutingStatus? Map(SharedDriverTransmissionStatus? s) => s == null ? null : s == SharedDriverTransmissionStatus.Failed ? GearRoutingStatus.InvalidInput :
        s == SharedDriverTransmissionStatus.IncompleteResource ? GearRoutingStatus.IncompleteBudget : Enum.Parse<GearRoutingStatus>(s.Value.ToString());
    public long Revision => session.Revision;
    public LayoutAuthoringState State => Generation?.Status == SharedDriverTransmissionStatus.Failed ? LayoutAuthoringState.Failed : session.State;
    public SharedDriverGoalNormalization? Normalized { get; private set; }
    public SharedDriverTransmissionSearchPlan? Preview { get; private set; }
    public SharedDriverTransmissionResult? Generation => session.Published?.Generation;
    public string? SelectedId => session.SelectedId;
    public SharedDriverTransmissionCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public GearRoutingPlaybackSetup Playback => session.Playback;
    public IReadOnlyList<KeyValuePair<string, string>> Labels => labels;
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ExpansionProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(SharedDriverTransmissionGoal goal)
    {
        var n = sdk.NormalizeSharedDriverTransmissionGoal(goal);
        labels = goal.Outputs.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel)).ToArray();
        session.Edit(n.IsValid ? new SharedDriverTransmissionProject(n.Goal!, labels: labels) : null, n.IsValid ? "Shared goal changed: previous whole result/selection invalidated" : string.Join(" | ", n.Diagnostics.Select(d => d.Code + ":" + d.Message)));
        Normalized = n; Preview = n.IsValid ? sdk.CompileSharedDriverTransmissionSearchPlan(n.Goal!) : null;
    }
    public void SetLabel(string outputKey, string label)
    {
        if (label == null || label.Length > 128 || !labels.Any(l => l.Key == outputKey)) throw new ArgumentException("Unknown output key or label ceiling");
        labels = labels.Select(l => l.Key == outputKey ? new KeyValuePair<string, string>(l.Key, label) : l).ToArray();
    }
    public void InvalidRawInput(string message) { session.Edit(null, message); Normalized = null; Preview = null; }
    public bool BeginGeneration() { var goal = Normalized?.Goal; return session.Start(false, (token, update) => new SharedDriverTransmissionProject(goal!, sdk.GenerateSharedDriverTransmissions(goal!, token, update))); }
    public void Cancel() => session.Cancel();
    public void Select(string id) => session.Select(id);
    public void SetPlayback(GearRoutingPlaybackSetup setup) => session.SetPlayback(setup);
    public void Pump() => session.Pump();
    public SharedDriverTransmissionProject Snapshot() => session.Snapshot();
    public void AdoptLoaded(SharedDriverTransmissionProject p)
    {
        SharedDriverTransmissionProjects.Validate(sdk, p); labels = p.Labels.ToArray(); session.AdoptLoaded(p, new SharedDriverTransmissionProject(p.Goal, labels: labels));
        Normalized = sdk.NormalizeSharedDriverTransmissionGoal(p.Goal); Preview = sdk.CompileSharedDriverTransmissionSearchPlan(p.Goal);
    }
    public void Load(string directory) => AdoptLoaded(sdk.LoadSharedDriverTransmissionProject(directory));
    public void BeginRegeneration() { var p = Snapshot(); session.Start(true, (token, update) => sdk.RegenerateSharedDriverTransmissionProject(p, token, update)); }
    public void Dispose() { session.Dispose(); Normalized = null; Preview = null; }
}
