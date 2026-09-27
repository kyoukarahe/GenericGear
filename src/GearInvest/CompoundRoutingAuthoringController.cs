using System;
using System.Linq;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

/// <summary>Typed request/project projection over shared routing publication and playback lifecycle.</summary>
public sealed class CompoundRoutingAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk; private readonly RoutingAuthoringSession<CompoundRoutingProject> session;
    public CompoundRoutingAuthoringController(GearInvestSdk? sdk = null,Func<Func<CompoundRoutingProject>,Task<CompoundRoutingProject>>? scheduler = null)
    {
        this.sdk = sdk ?? GearInvestSdk.CreateDefault(); session = new RoutingAuthoringSession<CompoundRoutingProject>(scheduler,
            p => this.sdk.NormalizeAnchoredCompoundRoutingRequest(p.Request).RequestId,p => p.Generation?.Status,(p,id) => p.Generation?.Candidates.Any(c => c.CandidateId == id) == true,
            (p,id,b) => new CompoundRoutingProject(p.Request,p.Generation,id,b),p => "SDK " + p.Generation!.Status + "; " + p.Generation.Summary.Work + " expansions; " + p.Generation.Summary.ValidMechanisms + " valid; " + p.Generation.Candidates.Count + " returned",p => p.SelectedId,p => p.Playback);
    }
    public long Revision => session.Revision;
    public LayoutAuthoringState State => session.State;
    public CompoundRoutingNormalization? Normalized { get; private set; }
    public CompoundGearRoutingResult? Generation => session.Published?.Generation;
    public string? SelectedId => session.SelectedId;
    public CompoundGearRouteCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public GearRoutingPlaybackSetup Playback => session.Playback;
    public bool IsCachedLoad => session.IsCachedLoad;
    public bool Reproduced => session.Reproduced;
    public int ExpansionProgress => session.ExpansionProgress;
    public string Notice => session.Notice;
    public void Edit(AnchoredCompoundGearRoutingRequest request)
    { var n = sdk.NormalizeAnchoredCompoundRoutingRequest(request); session.Edit(n.IsValid ? new CompoundRoutingProject(n.Request!) : null,n.IsValid ? "Draft changed; generate before use" : string.Join(" | ",n.Diagnostics.Select(d => d.Code + ": " + d.Message))); Normalized = n; }
    public void InvalidRawInput(string message) { session.Edit(null,message); Normalized = null; }
    public bool BeginGeneration() { var request = Normalized?.Request; return session.Start(false,(token,update) => new CompoundRoutingProject(request!,sdk.GenerateAnchoredCompoundGearRoutes(request!,token,update))); }
    public void Cancel() => session.Cancel();
    public void Select(string id) => session.Select(id);
    public void SetPlayback(GearRoutingPlaybackSetup setup) => session.SetPlayback(setup);
    public void Pump() => session.Pump();
    public CompoundRoutingProject Snapshot() => session.Snapshot();
    public void AdoptLoaded(CompoundRoutingProject p)
    { CompoundRoutingProjects.Validate(sdk,p); session.AdoptLoaded(p,new CompoundRoutingProject(p.Request)); Normalized = sdk.NormalizeAnchoredCompoundRoutingRequest(p.Request); }
    public void Load(string directory) => AdoptLoaded(sdk.LoadCompoundRoutingProject(directory));
    public void BeginRegeneration() { var saved = Snapshot(); session.Start(true,(token,update) => sdk.RegenerateCompoundRoutingProject(saved,token,update)); }
    public void Dispose() { session.Dispose(); Normalized = null; }
}
