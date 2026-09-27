using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

public sealed class TwoOutputTransmissionAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly RoutingAuthoringSession<TwoOutputTransmissionProject> session;
    private KeyValuePair<string, string>[] labels = Array.Empty<KeyValuePair<string, string>>();
    public TwoOutputTransmissionAuthoringController(GearInvestSdk? sdk = null, Func<Func<TwoOutputTransmissionProject>, Task<TwoOutputTransmissionProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault(); session = new RoutingAuthoringSession<TwoOutputTransmissionProject>(scheduler,
            p => this.sdk.NormalizeTwoOutputTransmissionGoal(p.Goal).GoalId, p => Map(p.Generation?.Status), (p, id) => p.Generation?.Candidates.Any(c => c.CandidateId == id) == true,
            (p, id, b) => new TwoOutputTransmissionProject(p.Goal, p.Generation, id, b, labels),
            p => p.Generation!.Status + "; generation=" + (p.Generation.ConsumedWork) + "; profiles=" + string.Join(",", p.Generation.Outcomes.Select(o => o.Plan.StrategyId + ":" + o.Status + ":" + o.ConsumedWork + "/" + o.Plan.Quota)) + "; collector=" + p.Generation.CollectorStop + "; " + p.Generation.RankingGuarantee,
            p => p.SelectedId, p => p.Playback);
    }
    private static GearRoutingStatus? Map(TwoOutputTransmissionStatus? s) => s == null ? null : s == TwoOutputTransmissionStatus.Failed ? GearRoutingStatus.InvalidInput :
        s == TwoOutputTransmissionStatus.IncompleteResource ? GearRoutingStatus.IncompleteBudget : Enum.Parse<GearRoutingStatus>(s.Value.ToString());
    public long Revision => session.Revision;
    public LayoutAuthoringState State => Generation?.Status == TwoOutputTransmissionStatus.Failed ? LayoutAuthoringState.Failed : session.State;
    public TwoOutputGoalNormalization? Normalized { get; private set; }
    public TwoOutputSharingSearchPlan? Preview { get; private set; }
    public TwoOutputTransmissionResult? Generation => session.Published?.Generation;
    public string? SelectedId => session.SelectedId;
    public TwoOutputTransmissionCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public GearRoutingPlaybackSetup Playback => session.Playback;
    public IReadOnlyList<KeyValuePair<string, string>> Labels => labels;
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ExpansionProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(TwoOutputTransmissionGoal goal)
    {
        var n = sdk.NormalizeTwoOutputTransmissionGoal(goal);
        labels = goal.Outputs.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new KeyValuePair<string, string>(o.Key, o.DisplayLabel)).ToArray();
        session.Edit(n.IsValid ? new TwoOutputTransmissionProject(n.Goal!, labels: labels) : null, n.IsValid ? "Shared goal changed: previous whole result/selection invalidated" : string.Join(" | ", n.Diagnostics.Select(d => d.Code + ":" + d.Message)));
        Normalized = n; Preview = n.IsValid ? sdk.CompileTwoOutputSharingSearchPlan(n.Goal!) : null;
    }
    public void SetLabel(string outputKey, string label)
    {
        if (label == null || label.Length > 128 || !labels.Any(l => l.Key == outputKey)) throw new ArgumentException("Unknown output key or label ceiling");
        labels = labels.Select(l => l.Key == outputKey ? new KeyValuePair<string, string>(l.Key, label) : l).ToArray();
    }
    public void InvalidRawInput(string message) { session.Edit(null, message); Normalized = null; Preview = null; }
    public bool BeginGeneration() { var goal = Normalized?.Goal; return session.Start(false, (token, update) => new TwoOutputTransmissionProject(goal!, sdk.GenerateTwoOutputTransmissions(goal!, token, update))); }
    public void Cancel() => session.Cancel();
    public void Select(string id) => session.Select(id);
    public void SetPlayback(GearRoutingPlaybackSetup setup) => session.SetPlayback(setup);
    public void Pump() => session.Pump();
    public TwoOutputTransmissionProject Snapshot() => session.Snapshot();
    public void AdoptLoaded(TwoOutputTransmissionProject p)
    {
        TwoOutputTransmissionProjects.Validate(sdk, p); labels = p.Labels.ToArray(); session.AdoptLoaded(p, new TwoOutputTransmissionProject(p.Goal, labels: labels));
        Normalized = sdk.NormalizeTwoOutputTransmissionGoal(p.Goal); Preview = sdk.CompileTwoOutputSharingSearchPlan(p.Goal);
    }
    public void Load(string directory) => AdoptLoaded(sdk.LoadTwoOutputTransmissionProject(directory));
    public void BeginRegeneration() { var p = Snapshot(); session.Start(true, (token, update) => sdk.RegenerateTwoOutputTransmissionProject(p, token, update)); }
    public void Dispose() { session.Dispose(); Normalized = null; Preview = null; }
}

