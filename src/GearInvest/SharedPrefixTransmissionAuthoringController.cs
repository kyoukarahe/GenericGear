using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

public sealed class SharedPrefixTransmissionAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly RoutingAuthoringSession<SharedPrefixTransmissionProject> session;
    private KeyValuePair<string, string>[] labels = Array.Empty<KeyValuePair<string, string>>();
    public SharedPrefixTransmissionAuthoringController(GearInvestSdk? sdk = null, Func<Func<SharedPrefixTransmissionProject>, Task<SharedPrefixTransmissionProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault(); session = new RoutingAuthoringSession<SharedPrefixTransmissionProject>(scheduler,
            p => this.sdk.NormalizeSharedPrefixTransmissionGoal(p.Goal).GoalId, p => Map(p.Generation?.Status), (p, id) => p.Generation?.Candidates.Any(c => c.CandidateId == id) == true,
            (p, id, b) => new SharedPrefixTransmissionProject(p.Goal, p.Generation, id, b, labels),
            p => p.Generation!.Status + "; generation=" + (p.Generation.ConsumedPrefixWork + p.Generation.ConsumedSuffixWork) + "; triple=" + p.Generation.ExaminedTriples + "/" + p.Generation.ObservedTripleDomain + "; " + p.Generation.LimitingReason + "; " + p.Generation.RankingGuarantee,
            p => p.SelectedId, p => p.Playback);
    }
    private static GearRoutingStatus? Map(SharedPrefixTransmissionStatus? s) => s == null ? null : s == SharedPrefixTransmissionStatus.Failed ? GearRoutingStatus.InvalidInput :
        s == SharedPrefixTransmissionStatus.IncompleteResource ? GearRoutingStatus.IncompleteBudget : Enum.Parse<GearRoutingStatus>(s.Value.ToString());
    public long Revision => session.Revision;
    public LayoutAuthoringState State => Generation?.Status == SharedPrefixTransmissionStatus.Failed ? LayoutAuthoringState.Failed : session.State;
    public SharedPrefixGoalNormalization? Normalized { get; private set; }
    public SharedPrefixTransmissionSearchPlan? Preview { get; private set; }
    public SharedPrefixTransmissionResult? Generation => session.Published?.Generation;
    public string? SelectedId => session.SelectedId;
    public SharedPrefixTransmissionCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public GearRoutingPlaybackSetup Playback => session.Playback;
    public IReadOnlyList<KeyValuePair<string, string>> Labels => labels;
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ExpansionProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(SharedPrefixTransmissionGoal goal)
    {
        var n = sdk.NormalizeSharedPrefixTransmissionGoal(goal);
        labels = goal.Outputs.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel)).ToArray();
        session.Edit(n.IsValid ? new SharedPrefixTransmissionProject(n.Goal!, labels: labels) : null, n.IsValid ? "Shared goal changed: previous whole result/selection invalidated" : string.Join(" | ", n.Diagnostics.Select(d => d.Code + ":" + d.Message)));
        Normalized = n; Preview = n.IsValid ? sdk.CompileSharedPrefixTransmissionSearchPlan(n.Goal!) : null;
    }
    public void SetLabel(string outputKey, string label)
    {
        if (label == null || label.Length > 128 || !labels.Any(l => l.Key == outputKey)) throw new ArgumentException("Unknown output key or label ceiling");
        labels = labels.Select(l => l.Key == outputKey ? new KeyValuePair<string, string>(l.Key, label) : l).ToArray();
    }
    public void InvalidRawInput(string message) { session.Edit(null, message); Normalized = null; Preview = null; }
    public bool BeginGeneration() { var goal = Normalized?.Goal; return session.Start(false, (token, update) => new SharedPrefixTransmissionProject(goal!, sdk.GenerateSharedPrefixTransmissions(goal!, token, update))); }
    public void Cancel() => session.Cancel();
    public void Select(string id) => session.Select(id);
    public void SetPlayback(GearRoutingPlaybackSetup setup) => session.SetPlayback(setup);
    public void Pump() => session.Pump();
    public SharedPrefixTransmissionProject Snapshot() => session.Snapshot();
    public void AdoptLoaded(SharedPrefixTransmissionProject p)
    {
        SharedPrefixTransmissionProjects.Validate(sdk, p); labels = p.Labels.ToArray(); session.AdoptLoaded(p, new SharedPrefixTransmissionProject(p.Goal, labels: labels));
        Normalized = sdk.NormalizeSharedPrefixTransmissionGoal(p.Goal); Preview = sdk.CompileSharedPrefixTransmissionSearchPlan(p.Goal);
    }
    public void Load(string directory) => AdoptLoaded(sdk.LoadSharedPrefixTransmissionProject(directory));
    public void BeginRegeneration() { var p = Snapshot(); session.Start(true, (token, update) => sdk.RegenerateSharedPrefixTransmissionProject(p, token, update)); }
    public void Dispose() { session.Dispose(); Normalized = null; Preview = null; }
}
