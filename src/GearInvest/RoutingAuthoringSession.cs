using System;
using System.Threading;
using System.Threading.Tasks;
using GearInvest.Core;
using GearInvest.Serialization;

namespace GearInvest;

/// <summary>Shared owner-thread publication, ABA/cancel safety and playback-edit policy. No synthesis or domain schema.</summary>
internal sealed class RoutingAuthoringSession<TProject> : IDisposable where TProject : class
{
    private readonly Func<Func<TProject>,Task<TProject>> schedule;
    private readonly Func<TProject,string> requestId;
    private readonly Func<TProject,GearRoutingStatus?> status;
    private readonly Func<TProject,string,bool> contains;
    private readonly Func<TProject,string?,GearRoutingPlaybackSetup,TProject> withPresentation;
    private readonly Func<TProject,string> summary;
    private readonly Func<TProject,string?> selected;
    private readonly Func<TProject,GearRoutingPlaybackSetup> playback;
    private CancellationTokenSource? cancellation; private Task<TProject>? task;
    private long operation,capturedOperation,capturedRevision,playbackRevision,capturedPlayback;
    private bool disposed,regenerating; private int progress; private string? desiredSelection;
    internal RoutingAuthoringSession(Func<Func<TProject>,Task<TProject>>? scheduler,Func<TProject,string> requestId,Func<TProject,GearRoutingStatus?> status,
        Func<TProject,string,bool> contains,Func<TProject,string?,GearRoutingPlaybackSetup,TProject> withPresentation,Func<TProject,string> summary,
        Func<TProject,string?> selected,Func<TProject,GearRoutingPlaybackSetup> playback)
    { schedule = scheduler ?? (work => Task.Run(work)); this.requestId = requestId; this.status = status; this.contains = contains; this.withPresentation = withPresentation; this.summary = summary; this.selected = selected; this.playback = playback; }
    internal long Revision { get; private set; }
    internal LayoutAuthoringState State { get; private set; } = LayoutAuthoringState.Draft;
    internal TProject? Draft { get; private set; }
    internal TProject? Published { get; private set; }
    internal string? SelectedId { get; private set; }
    internal GearRoutingPlaybackSetup Playback { get; private set; } = new GearRoutingPlaybackSetup(Rational.Zero,new Rational(1,8));
    internal bool IsCachedLoad { get; private set; }
    internal bool Reproduced { get; private set; }
    internal int ExpansionProgress => Volatile.Read(ref progress);
    internal string Notice { get; private set; } = "Standalone continuous draft; no generation yet";
    private void Alive() { if (disposed) throw new ObjectDisposedException("RoutingAuthoringController"); }
    private void Invalidate()
    { Alive(); Revision++; operation++; AuthoringWork.Retire(ref cancellation,task); task = null; Published = null; SelectedId = null; IsCachedLoad = false; Reproduced = false; progress = 0; }
    internal void Edit(TProject? draft,string notice)
    { Invalidate(); Draft = draft; State = draft == null ? LayoutAuthoringState.Invalid : LayoutAuthoringState.Draft; Notice = notice; }
    internal bool Start(bool regenerate,Func<CancellationToken,Action<int,int>,TProject> work)
    {
        Alive(); if (Draft == null) { State = LayoutAuthoringState.Invalid; Notice = "Valid finite request required"; return false; }
        AuthoringWork.Retire(ref cancellation,task); Published = null; SelectedId = null; IsCachedLoad = false; Reproduced = false;
        capturedRevision = Revision; capturedOperation = ++operation; capturedPlayback = playbackRevision; regenerating = regenerate;
        cancellation = new CancellationTokenSource(); var token = cancellation.Token; var epoch = capturedOperation;
        State = LayoutAuthoringState.Generating; progress = 0; Notice = regenerate ? "Fresh request-only regeneration; saved output is a comparator" : "SDK bounded routing worker";
        task = schedule(() => work(token,(value,_) => { if (!disposed && Volatile.Read(ref operation) == epoch) Volatile.Write(ref progress,value); })); return true;
    }
    internal void Cancel() { Invalidate(); State = LayoutAuthoringState.Cancelled; Notice = "Cancelled; no previous result restored"; }
    internal void Select(string id)
    { Alive(); desiredSelection = id; SelectedId = Published != null && contains(Published,id) ? id : null; Notice = SelectedId == null ? "SelectionUnavailable: no rank-0 fallback" : "Selected exact mechanism by stable identity"; }
    internal void SetPlayback(GearRoutingPlaybackSetup setup)
    { Alive(); CompoundRoutingProjectJson.ValidatePlayback(setup); playbackRevision++; Playback = setup; Reproduced = false; }
    internal void Pump()
    {
        Alive(); if (task?.IsCompleted != true) return; var completed = task; task = null;
        if (capturedOperation != operation || capturedRevision != Revision) { AuthoringWork.ObserveFault(completed); return; }
        try
        {
            var p = completed.GetAwaiter().GetResult(); if (!status(p).HasValue || Draft == null || requestId(p) != requestId(Draft)) throw new InvalidOperationException("StaleRequestCompletion");
            Published = p; State = Map(status(p)); SelectedId = desiredSelection != null && contains(p,desiredSelection) ? desiredSelection : null;
            Notice = summary(p); if (desiredSelection != null && SelectedId == null) Notice += "; SelectionUnavailable";
            if (regenerating) { SelectedId = desiredSelection = selected(p); if (playbackRevision == capturedPlayback) { Playback = playback(p); Reproduced = true; Notice += "; fresh reproduction PASS"; } else Notice += "; current playback edit retained"; }
        }
        catch (Exception e) { Published = null; SelectedId = null; State = e.Message == "Cancelled" ? LayoutAuthoringState.Cancelled : LayoutAuthoringState.Failed; Notice = e.Message; }
    }
    private static LayoutAuthoringState Map(GearRoutingStatus? s) => !s.HasValue ? LayoutAuthoringState.Draft : s == GearRoutingStatus.Complete ? LayoutAuthoringState.Ready : s == GearRoutingStatus.Infeasible ? LayoutAuthoringState.NoSolution : s == GearRoutingStatus.IncompleteBudget ? LayoutAuthoringState.Partial : s == GearRoutingStatus.Cancelled ? LayoutAuthoringState.Cancelled : LayoutAuthoringState.Invalid;
    internal TProject Snapshot()
    { Alive(); if (Draft == null || State == LayoutAuthoringState.Generating) throw new InvalidOperationException("CannotSaveInvalidOrRunningDraft"); return withPresentation(Published ?? Draft,SelectedId,Playback); }
    internal void AdoptLoaded(TProject p,TProject draft)
    { Invalidate(); Draft = draft; Published = status(p).HasValue ? p : null; SelectedId = desiredSelection = selected(p); Playback = playback(p); playbackRevision++; IsCachedLoad = true; State = Map(status(p)); Notice = "Cached load revalidated; not freshly regenerated"; }
    public void Dispose() { if (disposed) return; Invalidate(); disposed = true; Draft = null; State = LayoutAuthoringState.Cancelled; }
}
