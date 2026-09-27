using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;

namespace GearInvest;

/// <summary>Owner-thread publication of request-only SDK workers. Playback edits never become search inputs.</summary>
public sealed class GearRoutingAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly Func<Func<GearRoutingProject>, Task<GearRoutingProject>> schedule;
    private CancellationTokenSource? cancellation; private Task<GearRoutingProject>? task;
    private long operation, capturedOperation, capturedRevision, playbackRevision, capturedPlayback; private bool disposed, regenerating;
    private int progress; private string? desiredSelection;
    public GearRoutingAuthoringController(GearInvestSdk? sdk = null, Func<Func<GearRoutingProject>, Task<GearRoutingProject>>? scheduler = null)
    { this.sdk = sdk ?? GearInvestSdk.CreateDefault(); schedule = scheduler ?? (work => Task.Run(work)); }
    public long Revision { get; private set; }
    public LayoutAuthoringState State { get; private set; } = LayoutAuthoringState.Draft;
    public GearRoutingNormalization? Normalized { get; private set; }
    public GearRoutingResult? Generation { get; private set; }
    public string? SelectedId { get; private set; }
    public GearRouteCandidate? Selected => Generation?.Candidates.SingleOrDefault(c => c.CandidateId == SelectedId);
    public GearRoutingPlaybackSetup Playback { get; private set; } = new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8));
    public bool IsCachedLoad { get; private set; }
    public bool Reproduced { get; private set; }
    public int ExpansionProgress => Volatile.Read(ref progress);
    public string Notice { get; private set; } = "Standalone continuous draft; no generation yet";
    private void Alive() { if (disposed) throw new ObjectDisposedException(nameof(GearRoutingAuthoringController)); }
    private void Invalidate()
    { Alive(); Revision++; operation++; AuthoringWork.Retire(ref cancellation, task); task = null; Generation = null; SelectedId = null; IsCachedLoad = false; Reproduced = false; progress = 0; }
    public void Edit(AnchoredGearRoutingRequest request)
    { Invalidate(); Normalized = sdk.NormalizeGearRoutingRequest(request); State = Normalized.IsValid ? LayoutAuthoringState.Draft : LayoutAuthoringState.Invalid; Notice = Normalized.IsValid ? "Draft changed; generate before use" : string.Join(" | ", Normalized.Diagnostics.Select(d => d.Code + ": " + d.Message)); }
    public void InvalidRawInput(string message) { Invalidate(); Normalized = null; State = LayoutAuthoringState.Invalid; Notice = message; }
    public bool BeginGeneration()
    {
        Alive(); if (Normalized?.IsValid != true) { State = LayoutAuthoringState.Invalid; Notice = "Valid finite request required"; return false; }
        var request = Normalized.Request!; Start(false, (token, update) => new GearRoutingProject(request, sdk.GenerateGearRoutes(request, token, update))); return true;
    }
    private void Start(bool regenerate, Func<CancellationToken, Action<int, int>, GearRoutingProject> work)
    {
        AuthoringWork.Retire(ref cancellation, task); Generation = null; SelectedId = null; IsCachedLoad = false; Reproduced = false;
        capturedRevision = Revision; capturedOperation = ++operation; capturedPlayback = playbackRevision; regenerating = regenerate;
        cancellation = new CancellationTokenSource(); var token = cancellation.Token; var epoch = capturedOperation;
        State = LayoutAuthoringState.Generating; progress = 0; Notice = regenerate ? "Fresh request-only regeneration; saved output is a comparator" : "SDK bounded routing worker";
        task = schedule(() => work(token, (value, _) => { if (!disposed && Volatile.Read(ref operation) == epoch) Volatile.Write(ref progress, value); }));
    }
    public void Cancel() { Invalidate(); State = LayoutAuthoringState.Cancelled; Notice = "Cancelled; no previous result restored"; }
    public void Select(string id)
    {
        Alive(); desiredSelection = id; SelectedId = Generation?.Candidates.Any(c => c.CandidateId == id) == true ? id : null;
        Notice = SelectedId == null ? "SelectionUnavailable: no rank-0 fallback" : "Selected exact mechanism by stable identity";
    }
    public void SetPlayback(GearRoutingPlaybackSetup setup)
    { Alive(); GearRoutingProjectJson.ValidatePlayback(setup); playbackRevision++; Playback = setup; Reproduced = false; }
    public void Pump()
    {
        Alive(); if (task?.IsCompleted != true) return; var completed = task; task = null;
        if (capturedOperation != operation || capturedRevision != Revision) { AuthoringWork.ObserveFault(completed); return; }
        try
        {
            var p = completed.GetAwaiter().GetResult(); if (p.Generation == null || p.Generation.RequestId != Normalized?.RequestId) throw new InvalidOperationException("StaleRequestCompletion");
            Generation = p.Generation; State = Map(Generation.Status);
            SelectedId = desiredSelection != null && Generation.Candidates.Any(c => c.CandidateId == desiredSelection) ? desiredSelection : null;
            Notice = "SDK " + Generation.Status + "; " + Generation.Summary.Expansions + " expansions; " + Generation.Summary.ValidRoutes + " valid; " + Generation.Candidates.Count + " returned";
            if (desiredSelection != null && SelectedId == null) Notice += "; SelectionUnavailable";
            if (regenerating) { SelectedId = desiredSelection = p.SelectedId; if (playbackRevision == capturedPlayback) { Playback = p.Playback; Reproduced = true; Notice += "; fresh reproduction PASS"; } else Notice += "; current playback edit retained"; }
        }
        catch (Exception e) { Generation = null; SelectedId = null; State = e.Message == "Cancelled" ? LayoutAuthoringState.Cancelled : LayoutAuthoringState.Failed; Notice = e.Message; }
    }
    private static LayoutAuthoringState Map(GearRoutingStatus s) => s == GearRoutingStatus.Complete ? LayoutAuthoringState.Ready : s == GearRoutingStatus.Infeasible ? LayoutAuthoringState.NoSolution : s == GearRoutingStatus.IncompleteBudget ? LayoutAuthoringState.Partial : s == GearRoutingStatus.Cancelled ? LayoutAuthoringState.Cancelled : LayoutAuthoringState.Invalid;
    public GearRoutingProject Snapshot()
    { Alive(); if (Normalized?.IsValid != true || State == LayoutAuthoringState.Generating) throw new InvalidOperationException("CannotSaveInvalidOrRunningDraft"); return new GearRoutingProject(Normalized.Request!, Generation, SelectedId, Playback); }
    public void AdoptLoaded(GearRoutingProject p)
    { GearRoutingProjects.Validate(sdk, p); Invalidate(); Normalized = sdk.NormalizeGearRoutingRequest(p.Request); Generation = p.Generation; SelectedId = desiredSelection = p.SelectedId; Playback = p.Playback; playbackRevision++; IsCachedLoad = true; State = Generation == null ? LayoutAuthoringState.Draft : Map(Generation.Status); Notice = "Cached load revalidated; not freshly regenerated"; }
    public void Load(string directory) { Alive(); AdoptLoaded(sdk.LoadGearRoutingProject(directory)); }
    public void BeginRegeneration() { var saved = Snapshot(); desiredSelection = saved.SelectedId; Start(true, (token, update) => sdk.RegenerateGearRoutingProject(saved, token, update)); }
    public void Dispose() { if (disposed) return; Invalidate(); disposed = true; Normalized = null; State = LayoutAuthoringState.Cancelled; }
}
