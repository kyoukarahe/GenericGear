using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GearInvest.Core;

namespace GearInvest;

public enum LayoutAuthoringState { Draft, Invalid, Generating, Ready, Partial, NoSolution, Cancelled, Failed }

/// <summary>Application state outside Core. Owner-thread commands/Pump publish immutable worker results.
/// The optional scheduler controls completion in race tests; production always executes the supplied SDK work.</summary>
public sealed class DiscreteLayoutAuthoringController : IDisposable
{
    private readonly GearInvestSdk sdk;
    private readonly Func<Func<DiscreteLayoutProject>, Task<DiscreteLayoutProject>> schedule;
    private CancellationTokenSource? generationCancellation, evaluationCancellation;
    private Task<DiscreteLayoutProject>? generationTask;
    private Task<GeometricDiscreteResult>? evaluationTask;
    private long operation, evaluationOperation, generationRevision, generationToken, generationEvaluationToken, evaluationRevision, evaluationToken;
    private int progressed, domain; private bool disposed, regenerating; private string? desiredSelection;

    public DiscreteLayoutAuthoringController(GearInvestSdk? sdk = null, Func<Func<DiscreteLayoutProject>, Task<DiscreteLayoutProject>>? scheduler = null)
    { this.sdk = sdk ?? GearInvestSdk.CreateDefault(); schedule = scheduler ?? (work => Task.Run(work)); }
    public long Revision { get; private set; }
    public LayoutAuthoringState State { get; private set; } = LayoutAuthoringState.Draft;
    public DiscreteLayoutAssets? Assets { get; private set; }
    public DiscreteLayoutRequest? Draft { get; private set; }
    public DiscreteLayoutNormalization? Normalized { get; private set; }
    public DiscreteLayoutGeneration? Generation { get; private set; }
    public DiscreteLayoutEvaluation? EvaluationSetup { get; private set; }
    public string? SelectedId { get; private set; }
    public GeometricDiscreteResult? Execution { get; private set; }
    public DiscreteGeometryCandidate? SelectedGeometry => Generation?.Candidates.SingleOrDefault(c => c.Geometry.GeometryId == SelectedId)?.Geometry;
    public bool IsCachedLoad { get; private set; }
    public bool Reproduced { get; private set; }
    public bool EvaluationPending => evaluationTask != null;
    public string Notice { get; private set; } = "New draft; no current generated result";
    public int EvaluatedProgress => Volatile.Read(ref progressed);
    public int DomainProgress => Volatile.Read(ref domain);

    private void Alive() { if (disposed) throw new ObjectDisposedException(nameof(DiscreteLayoutAuthoringController)); }
    private static void ObserveFault(Task? task) => AuthoringWork.ObserveFault(task);
    private void StopEvaluation()
    {
        evaluationOperation++; AuthoringWork.Retire(ref evaluationCancellation, evaluationTask); evaluationTask = null; Execution = null;
    }
    private void Invalidate()
    {
        Alive(); Revision++; operation++; AuthoringWork.Retire(ref generationCancellation, generationTask);
        generationTask = null; StopEvaluation(); Generation = null; SelectedId = null;
        IsCachedLoad = false; Reproduced = false; progressed = 0; domain = 0;
    }
    public void SetSource(DiscreteLayoutAssets assets)
    { Invalidate(); Assets = assets; Draft = null; Normalized = null; EvaluationSetup = null; desiredSelection = null; State = LayoutAuthoringState.Draft; Notice = "Source replaced; generate a new request"; }
    public void Edit(DiscreteLayoutRequest request)
    {
        Invalidate(); Draft = request;
        Normalized = sdk.NormalizeDiscreteLayoutRequest(request);
        if (Assets == null || request.Source.Canonical != Assets.Binding.Canonical)
        { State = LayoutAuthoringState.Invalid; Notice = "SourceMismatch: draft is not bound to the current source"; return; }
        State = Normalized.IsValid ? LayoutAuthoringState.Draft : LayoutAuthoringState.Invalid;
        Notice = Normalized.IsValid ? "Draft changed; previous result is not current" : string.Join(" | ", Normalized.Diagnostics.Select(d => d.Code + " " + d.Field + ": " + d.Detail));
    }
    public void InvalidRawInput(string message)
    { Invalidate(); Draft = null; Normalized = null; State = LayoutAuthoringState.Invalid; Notice = message; }
    public bool BeginGeneration()
    {
        Alive();
        if (Assets == null || Normalized?.Request == null || Draft == null || Draft.Source.Canonical != Assets.Binding.Canonical)
        { State = LayoutAuthoringState.Invalid; Notice = "A valid normalized request and matching source are required"; return false; }
        var assets = Assets; var request = Normalized.Request; var setup = EvaluationSetup;
        StartWorker(false, (token, progress) => new DiscreteLayoutProject(assets, request, sdk.GenerateDiscreteLayouts(assets.Model, request, token, progress), evaluation: setup)); return true;
    }
    private Action<int, int> Progress(long token) => (evaluated, size) => { if (Volatile.Read(ref operation) == token && !disposed) { Volatile.Write(ref progressed, evaluated); Volatile.Write(ref domain, size); } };
    private void StartWorker(bool regenerate, Func<CancellationToken, Action<int, int>, DiscreteLayoutProject> work)
    {
        generationCancellation?.Cancel(); generationCancellation?.Dispose(); ObserveFault(generationTask); StopEvaluation();
        Generation = null; SelectedId = null; IsCachedLoad = false; Reproduced = false;
        generationCancellation = new CancellationTokenSource(); var token = generationCancellation.Token;
        generationRevision = Revision; generationToken = ++operation; generationEvaluationToken = evaluationOperation; regenerating = regenerate;
        State = LayoutAuthoringState.Generating; Notice = regenerate ? "Fresh regeneration from saved source/request; cache is only the comparator" : "Generating via SDK worker";
        progressed = 0; domain = Normalized == null ? 0 : (int)Normalized.DomainSize;
        var capturedProgress = Progress(generationToken);
        generationTask = schedule(() => work(token, capturedProgress));
    }
    public void Cancel()
    {
        Invalidate(); State = LayoutAuthoringState.Cancelled; Notice = "Cancelled; no old result restored as current";
    }
    public void Select(string geometryId)
    {
        Alive(); StopEvaluation(); desiredSelection = geometryId;
        if (Generation == null || !Generation.Candidates.Any(c => c.Geometry.GeometryId == geometryId))
        { SelectedId = null; Notice = "SelectionUnavailable: requested geometry ID is absent; no rank-0 fallback"; return; }
        SelectedId = geometryId; Notice = "Selected by stable geometry ID; Evaluate to produce this candidate's motion";
    }
    public void SetEvaluation(DiscreteLayoutEvaluation setup)
    {
        Alive(); StopEvaluation(); Reproduced = false; EvaluationSetup = setup;
        try { if (Assets != null) DiscreteLayoutProjects.ValidateSetup(Assets.Model, setup); Notice = "Evaluation setup changed; generation retained, old execution invalidated"; }
        catch (Exception e) { Notice = e.Message; }
    }
    public bool BeginEvaluation()
    {
        Alive(); StopEvaluation();
        if (Assets == null || Generation == null || SelectedId == null || EvaluationSetup == null) { Notice = "Select a current candidate and valid evaluation input"; return false; }
        var assets = Assets; var generated = Generation; var selected = SelectedId; var setup = EvaluationSetup;
        evaluationCancellation = new CancellationTokenSource(); var token = evaluationCancellation.Token;
        evaluationRevision = Revision; evaluationToken = evaluationOperation;
        evaluationTask = Task.Run(() => sdk.EvaluateDiscreteLayout(assets, generated, selected, setup, token)); Notice = "Fresh geometry-aware evaluation running"; return true;
    }
    public void Pump()
    {
        Alive();
        if (generationTask?.IsCompleted == true)
        {
            var task = generationTask; generationTask = null;
            if (generationRevision == Revision && generationToken == operation)
            {
                try
                {
                    var project = task.GetAwaiter().GetResult();
                    if (Assets?.ContentId != project.Assets.ContentId || Normalized?.RequestId != sdk.NormalizeDiscreteLayoutRequest(project.Request).RequestId) throw new InvalidOperationException("StaleSourceRequestCompletion");
                    Generation = project.Generation ?? throw new InvalidOperationException("MissingGenerationResult");
                    State = Generation.Status == DiscreteLayoutStatus.Complete ? LayoutAuthoringState.Ready : Generation.Status == DiscreteLayoutStatus.IncompleteBudget ? LayoutAuthoringState.Partial :
                        Generation.Status == DiscreteLayoutStatus.Infeasible ? LayoutAuthoringState.NoSolution : Generation.Status == DiscreteLayoutStatus.Cancelled ? LayoutAuthoringState.Cancelled : LayoutAuthoringState.Invalid;
                    Notice = "SDK " + Generation.Status + "; " + Generation.Evaluated + "/" + Generation.Normalized.DomainSize + " assignments; " + Generation.Candidates.Count + " returned";
                    if (desiredSelection != null)
                    {
                        SelectedId = Generation.Candidates.Any(c => c.Geometry.GeometryId == desiredSelection) ? desiredSelection : null;
                        if (SelectedId == null) Notice += "; SelectionUnavailable (no automatic replacement)";
                    }
                    if (regenerating)
                    {
                        SelectedId = project.SelectedId; desiredSelection = SelectedId;
                        // Evaluation edits do not invalidate a useful geometry search. They do invalidate
                        // the old setup/execution carried by that search's reproduction operation.
                        if (generationEvaluationToken == evaluationOperation)
                        { EvaluationSetup = project.Evaluation; Execution = project.Execution; Reproduced = true; Notice += "; fresh reproduction PASS"; }
                        else
                        { Execution = null; Reproduced = false; Notice += "; evaluation changed during regeneration; Evaluate current input"; }
                    }
                }
                catch (Exception e) { Generation = null; SelectedId = null; Execution = null; State = e.Message == "Cancelled" ? LayoutAuthoringState.Cancelled : LayoutAuthoringState.Failed; Notice = e.Message; }
            }
            else ObserveFault(task);
        }
        if (evaluationTask?.IsCompleted == true)
        {
            var task = evaluationTask; evaluationTask = null;
            if (evaluationRevision == Revision && evaluationToken == evaluationOperation)
            {
                try { Execution = task.GetAwaiter().GetResult(); Notice = "Evaluated current geometry: " + Execution.Status; }
                catch (Exception e) { Execution = null; Notice = "Evaluation failed: " + e.Message; }
            }
            else ObserveFault(task);
        }
    }
    public DiscreteLayoutProject Snapshot()
    {
        Alive(); if (Assets == null || Normalized?.Request == null || Draft == null || Assets.Binding.Canonical != Draft.Source.Canonical) throw new InvalidOperationException("CannotSaveInvalidRawDraft");
        if (State == LayoutAuthoringState.Generating) throw new InvalidOperationException("Finish or cancel generation before Save");
        return new DiscreteLayoutProject(Assets, Normalized.Request, Generation, SelectedId, EvaluationSetup, Execution);
    }
    public void Load(string directory)
    {
        Alive(); var project = sdk.LoadDiscreteLayoutProject(directory); // All validation completes before replacing any current state.
        AdoptLoaded(project);
    }
    public void AdoptLoaded(DiscreteLayoutProject project)
    {
        DiscreteLayoutProjects.Validate(sdk, project); Invalidate(); Assets = project.Assets; Draft = project.Request; Normalized = sdk.NormalizeDiscreteLayoutRequest(project.Request);
        Generation = project.Generation; SelectedId = desiredSelection = project.SelectedId; EvaluationSetup = project.Evaluation; Execution = project.Execution; IsCachedLoad = true;
        State = Generation == null ? LayoutAuthoringState.Draft : Generation.Status == DiscreteLayoutStatus.Complete ? LayoutAuthoringState.Ready : Generation.Status == DiscreteLayoutStatus.IncompleteBudget ? LayoutAuthoringState.Partial : LayoutAuthoringState.NoSolution;
        Notice = "Loaded and revalidated stored artifacts; NOT freshly regenerated";
    }
    public void BeginRegeneration()
    {
        var saved = Snapshot(); desiredSelection = saved.SelectedId;
        StartWorker(true, (token, progress) => sdk.RegenerateDiscreteLayoutProject(saved, token, progress));
    }
    public void Dispose()
    { if (disposed) return; Invalidate(); disposed = true; Assets = null; Draft = null; Normalized = null; State = LayoutAuthoringState.Cancelled; }
}
