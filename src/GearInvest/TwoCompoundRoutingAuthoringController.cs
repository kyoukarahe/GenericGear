using System;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>Typed request/project projection over shared routing publication and playback lifecycle.</summary>
public sealed class TwoCompoundRoutingAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk; private readonly RoutingAuthoringSession<TwoCompoundRoutingProject> session;
    public TwoCompoundRoutingAuthoringController(GearInvestSdk? sdk = null,Func<Func<TwoCompoundRoutingProject>,Task<TwoCompoundRoutingProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault(); session = new RoutingAuthoringSession<TwoCompoundRoutingProject>(scheduler,
            p => this.sdk.NormalizeAnchoredTwoCompoundRoutingRequest(p.Request).RequestId,p => p.Generation?.Status,(p,id) => p.Generation?.Candidates.Any(c => c.CandidateId == id) == true,
            (p,id,b) => new TwoCompoundRoutingProject(p.Request,p.Generation,id,b),p => "SDK " + p.Generation!.Status + "; " + p.Generation.Summary.Work + " expansions; " + p.Generation.Summary.ValidMechanisms + " valid; " + p.Generation.Candidates.Count + " returned",p => p.SelectedId,p => p.Playback);
    }
    public long Revision => session.Revision;
    public LayoutAuthoringState State => session.State;
    public TwoCompoundRoutingNormalization? Normalized { get; private set; }
    public TwoCompoundGearRoutingResult? Generation => session.Published?.Generation;
    public string? SelectedId => session.SelectedId;
    public TwoCompoundGearRouteCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public GearRoutingPlaybackSetup Playback => session.Playback;
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ExpansionProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(AnchoredTwoCompoundGearRoutingRequest request)
    { var n = sdk.NormalizeAnchoredTwoCompoundRoutingRequest(request); session.Edit(n.IsValid ? new TwoCompoundRoutingProject(n.Request!) : null,n.IsValid ? "Draft changed; generate before use" : string.Join(" | ",n.Diagnostics.Select(d => d.Code + ": " + d.Message))); Normalized = n; }
    public void InvalidRawInput(string message) { session.Edit(null,message); Normalized = null; }
    public bool BeginGeneration() { var request = Normalized?.Request; return session.Start(false,(token,update) => new TwoCompoundRoutingProject(request!,sdk.GenerateAnchoredTwoCompoundGearRoutes(request!,token,update))); }
    public void Cancel() => session.Cancel();
    public void Select(string id) => session.Select(id);
    public void SetPlayback(GearRoutingPlaybackSetup setup) => session.SetPlayback(setup);
    public void Pump() => session.Pump();
    public TwoCompoundRoutingProject Snapshot() => session.Snapshot();
    public void AdoptLoaded(TwoCompoundRoutingProject p)
    { TwoCompoundRoutingProjects.Validate(sdk,p); session.AdoptLoaded(p,new TwoCompoundRoutingProject(p.Request)); Normalized = sdk.NormalizeAnchoredTwoCompoundRoutingRequest(p.Request); }
    public void Load(string directory) => AdoptLoaded(sdk.LoadTwoCompoundRoutingProject(directory));
    public void BeginRegeneration() { var saved = Snapshot(); session.Start(true,(token,update) => sdk.RegenerateTwoCompoundRoutingProject(saved,token,update)); }
    public void Dispose() { session.Dispose(); Normalized = null; }
}
