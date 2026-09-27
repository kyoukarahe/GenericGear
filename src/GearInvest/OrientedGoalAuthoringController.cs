using System;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>Owner-thread, revision/ABA-safe publication over the existing session helper. No renderer or local solver.</summary>
public sealed class OrientedGoalAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly RoutingAuthoringSession<OrientedGoalProject> session;
    public OrientedGoalAuthoringController(GearInvestSdk? sdk = null, Func<Func<OrientedGoalProject>, Task<OrientedGoalProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault();
        session = new RoutingAuthoringSession<OrientedGoalProject>(scheduler, p => this.sdk.RequireOrientedGoalPlan(p.Goal).GoalId,
            p => UiStatus(p.Generation?.Status), (p, key) => p.Generation?.Candidates.Any(c => c.Origins.Any(o => SelectionKey(c.CandidateId, o.Id) == key)) == true,
            (p, key, playback) => new OrientedGoalProject(p.Goal, p.Generation, key?.Split('/')[0], key?.Split('/')[1], playback.RootTurns),
            p => "SDK " + p.Generation!.Status + "; " + p.Generation.ProcessedTuples + "/" + p.Generation.TheoreticalTuples + " tuples; " + p.Generation.Candidates.Count +
                " returned; " + p.Generation.RankingGuarantee + "; " + p.Generation.LimitingReason,
            p => p.SelectedCandidateId is null ? null : SelectionKey(p.SelectedCandidateId, p.SelectedOriginId!), p => new GearRoutingPlaybackSetup(p.RootTurns, new Rational(1, 8)));
    }
    // These are presentation states only. The original richer goal status is exposed unchanged in Generation and Notice.
    private static GearRoutingStatus? UiStatus(OrientedGoalStatus? s) => s is null ? null : s == OrientedGoalStatus.Complete ? GearRoutingStatus.Complete :
        s == OrientedGoalStatus.Infeasible || s == OrientedGoalStatus.NoValidatedCandidate ? GearRoutingStatus.Infeasible :
        s == OrientedGoalStatus.IncompleteBudget || s == OrientedGoalStatus.IncompleteResource ? GearRoutingStatus.IncompleteBudget : s == OrientedGoalStatus.Cancelled ? GearRoutingStatus.Cancelled : GearRoutingStatus.InvalidInput;
    public static string SelectionKey(string candidate, string origin) => candidate + "/" + origin;
    public long Revision => session.Revision;
    public LayoutAuthoringState State => Generation?.Status == OrientedGoalStatus.Failed ? LayoutAuthoringState.Failed : session.State;
    public OrientedGoalPlan? Preview { get; private set; }
    public OrientedGoalGeneration? Generation => session.Published?.Generation;
    public OrientedGoalCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.Origins.Any(o => SelectionKey(c.CandidateId, o.Id) == session.SelectedId));
    public OrientedGoalOrigin? SelectedOrigin => Selected?.Origins.SingleOrDefault(o => SelectionKey(Selected.CandidateId, o.Id) == session.SelectedId);
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ProcessedProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(OrientedTwoOutputGoal goal)
    {
        var compile = sdk.CompileOrientedTwoOutputSearchPlan(goal); Preview = compile.Plan;
        session.Edit(compile.IsSuccess ? new OrientedGoalProject(compile.Plan!.Goal) : null, compile.IsSuccess ? "Goal changed; prior result and selection invalidated." : compile.Status + ": " + compile.Detail);
    }
    public void InvalidRawInput(string reason) { session.Edit(null, reason); Preview = null; }
    public bool BeginGeneration() { var goal = Preview?.Goal; return session.Start(false, (token, progress) => new OrientedGoalProject(goal!, sdk.GenerateOrientedTwoOutput(goal!, token, progress))); }
    public void Cancel() => session.Cancel();
    public void Pump() => session.Pump();
    public void Select(string candidateId, string originId) => session.Select(SelectionKey(candidateId, originId));
    public void SetRootTurns(Rational turns) => session.SetPlayback(new GearRoutingPlaybackSetup(turns, new Rational(1, 8)));
    public OrientedGoalProject Snapshot() => session.Snapshot();
    public void Load(string directory)
    {
        var loaded = sdk.LoadOrientedGoalProject(directory); Preview = sdk.RequireOrientedGoalPlan(loaded.Goal);
        session.AdoptLoaded(loaded, new OrientedGoalProject(loaded.Goal));
    }
    public bool BeginRegeneration() { var saved = Snapshot(); return session.Start(true, (token, progress) => sdk.RegenerateOrientedGoalProject(saved, token, progress)); }
    public void Dispose() { session.Dispose(); Preview = null; }
}
