using System;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>Goal-specific projection over shared owner-thread/ABA-safe publication. No UI or solver duplication.</summary>
public sealed class TransmissionGoalAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly RoutingAuthoringSession<TransmissionGoalProject> session;
    public TransmissionGoalAuthoringController(GearInvestSdk? sdk = null, Func<Func<TransmissionGoalProject>, Task<TransmissionGoalProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault(); session = new RoutingAuthoringSession<TransmissionGoalProject>(scheduler,
            p => this.sdk.NormalizeTransmissionGoal(p.Goal).GoalId, p => Map(p.Generation?.Status), (p, id) => p.Generation?.Candidates.Any(c => c.CandidateId == id) == true,
            (p, id, b) => new TransmissionGoalProject(p.Goal, p.Generation, id, b, id == p.SelectedId ? p.SelectedOriginId : null),
            p => "SDK " + p.Generation!.Status + "; " + p.Generation.ConsumedWork + "/" + p.Generation.AllocatedWork + " child work; " + p.Generation.Candidates.Count + " returned; " + p.Generation.RankingGuarantee,
            p => p.SelectedId, p => p.Playback);
    }
    private static GearRoutingStatus? Map(TransmissionGoalStatus? status) => status == null ? null : status == TransmissionGoalStatus.Failed ? GearRoutingStatus.InvalidInput : Enum.Parse<GearRoutingStatus>(status.Value.ToString());
    public long Revision => session.Revision;
    public LayoutAuthoringState State => Generation?.Status == TransmissionGoalStatus.Failed ? LayoutAuthoringState.Failed : session.State;
    public TransmissionGoalNormalization? Normalized { get; private set; }
    public CompiledTransmissionSearchPlan? Preview { get; private set; }
    public TransmissionGenerationResult? Generation => session.Published?.Generation;
    public string? SelectedId => session.SelectedId;
    public TransmissionMechanismCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public TransmissionCandidateOrigin? SelectedOrigin => session.Published?.SelectedId == SelectedId && session.Published?.SelectedOrigin != null ? session.Published.SelectedOrigin : Selected?.Origins[0];
    public GearRoutingPlaybackSetup Playback => session.Playback;
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ExpansionProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(TransmissionGoal goal)
    {
        var n = sdk.NormalizeTransmissionGoal(goal); session.Edit(n.IsValid ? new TransmissionGoalProject(n.Goal!) : null,
            n.IsValid ? "Goal changed; old result and selection invalidated" : string.Join(" | ", n.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Normalized = n; Preview = n.IsValid ? sdk.CompileTransmissionSearchPlan(n.Goal!) : null;
    }
    public void InvalidRawInput(string message) { session.Edit(null, message); Normalized = null; Preview = null; }
    public bool BeginGeneration() { var goal = Normalized?.Goal; return session.Start(false, (token, update) => new TransmissionGoalProject(goal!, sdk.GenerateTransmissionMechanisms(goal!, token, update))); }
    public void Cancel() => session.Cancel();
    public void Select(string id) => session.Select(id);
    public void SetPlayback(GearRoutingPlaybackSetup setup) => session.SetPlayback(setup);
    public void Pump() => session.Pump();
    public TransmissionGoalProject Snapshot() => session.Snapshot();
    public void AdoptLoaded(TransmissionGoalProject p)
    {
        TransmissionGoalProjects.Validate(sdk, p); session.AdoptLoaded(p, new TransmissionGoalProject(p.Goal));
        Normalized = sdk.NormalizeTransmissionGoal(p.Goal); Preview = sdk.CompileTransmissionSearchPlan(p.Goal);
    }
    public void Load(string directory) => AdoptLoaded(sdk.LoadTransmissionGoalProject(directory));
    public void BeginRegeneration() { var p = Snapshot(); session.Start(true, (token, update) => sdk.RegenerateTransmissionGoalProject(p, token, update)); }
    public void Dispose() { session.Dispose(); Normalized = null; Preview = null; }
}
