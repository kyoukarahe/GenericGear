using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;
using static GearInvest.PortableProjectStorage;

namespace GearInvest;

/// <summary>Validated portable source content, with raw source bytes preserved. Integrity is not authorship.</summary>
public sealed class DiscreteLayoutAssets
{
    private readonly byte[] modelBytes, mechanismBytes;
    public DiscreteLayoutAssets(GearInvestSdk sdk, byte[] modelBytes, byte[] mechanismBytes, DiscreteLayoutSource binding)
    {
        this.modelBytes = (byte[])modelBytes.Clone(); this.mechanismBytes = (byte[])mechanismBytes.Clone(); Binding = binding;
        Model = sdk.ReadDiscreteEmbodiment(this.modelBytes); Mechanism = sdk.ReadArtifact(this.mechanismBytes);
        if (Model.ModelId != binding.ModelId || Mechanism.CandidateId != binding.MechanismId || !sdk.Validate(Mechanism).IsValid) throw new ArgumentException("SourceMismatch");
        var events = sdk.EvaluatePeriodicEvents(Mechanism, EventRequest(Rational.Zero, Rational.Zero));
        if (!events.IsSuccess || events.NormalizedDefinitions.Count != 1 || events.NormalizedDefinitions[0].PeriodicEventDefinitionId != Model.Projection.EventDefinitionId) throw new ArgumentException("MissingComponentOrEventBinding");
        ContentId = DiscreteLayoutContract.Id("layout-source-content", DiscreteLayoutProjects.Sha(this.modelBytes), DiscreteLayoutProjects.Sha(this.mechanismBytes), binding.Canonical);
    }
    public DiscreteEmbodimentModel Model { get; }
    public MechanismArtifact Mechanism { get; }
    public DiscreteLayoutSource Binding { get; }
    public string ContentId { get; }
    public byte[] ModelBytes => (byte[])modelBytes.Clone();
    public byte[] MechanismBytes => (byte[])mechanismBytes.Clone();
    public ContinuousEventBridgeRequest EventRequest(Rational from, Rational to) => new ContinuousEventBridgeRequest(Binding.DriverId, from, to,
        new[] { new PeriodicPhaseEventDefinition(Binding.EventKey, Binding.EventDofId, Rational.One, Rational.Zero) }, Binding.MaxEvents);
}

public sealed class DiscreteLayoutProject
{
    public DiscreteLayoutProject(DiscreteLayoutAssets assets, DiscreteLayoutRequest request, DiscreteLayoutGeneration? generation = null,
        string? selectedId = null, DiscreteLayoutEvaluation? evaluation = null, GeometricDiscreteResult? execution = null)
    { Assets = assets; Request = request; Generation = generation; SelectedId = selectedId; Evaluation = evaluation; Execution = execution; }
    public DiscreteLayoutAssets Assets { get; }
    public DiscreteLayoutRequest Request { get; }
    public DiscreteLayoutGeneration? Generation { get; }
    public string? SelectedId { get; }
    public DiscreteLayoutEvaluation? Evaluation { get; }
    public GeometricDiscreteResult? Execution { get; }
    public DiscreteGeometryCandidate? SelectedGeometry => Generation?.Candidates.SingleOrDefault(c => c.Geometry.GeometryId == SelectedId)?.Geometry;
}

/// <summary>Product filesystem boundary. Immutable revision files are committed by one atomic manifest replacement.
/// Unfinished staging files are never loaded, and an old project continues to reference its complete old revision.</summary>
public static class DiscreteLayoutProjects
{
    public static string Sha(byte[] bytes) { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))); }
    private static void Need(bool b, string message) { if (!b) throw new FormatException(message); }
    public static GeometricDiscreteResult Evaluate(GearInvestSdk sdk, DiscreteLayoutAssets assets, DiscreteLayoutGeneration generation, string selectedId, DiscreteLayoutEvaluation setup, CancellationToken token = default)
    {
        Need(generation.Normalized.IsValid && generation.Normalized.Request!.Source.Canonical == assets.Binding.Canonical, "CurrentSourceRequestMismatch");
        Need(generation.Status == DiscreteLayoutStatus.Complete || generation.Status == DiscreteLayoutStatus.IncompleteBudget, "GenerationNotEvaluable");
        var candidate = generation.Candidates.SingleOrDefault(c => c.Geometry.GeometryId == selectedId); Need(candidate != null && candidate.Validation.IsValid, "SelectionUnavailable");
        ValidateSetup(assets.Model, setup);
        var m = assets.Model;
        var initial = new DiscreteEmbodimentState(m.ModelId, assets.Mechanism.CandidateId, assets.Binding.DriverId, setup.FromRoot, setup.Cursor,
            new[] { new DiscreteWheelPosition("primary", setup.PrimaryIndex, setup.PrimaryTurns ?? new Rational(setup.PrimaryIndex, m.Components.Single(c => c.Id == "primary").Detents)),
                new DiscreteWheelPosition("secondary", setup.SecondaryIndex, setup.SecondaryTurns ?? new Rational(setup.SecondaryIndex, m.Components.Single(c => c.Id == "secondary").Detents)) });
        var events = sdk.EvaluatePeriodicEvents(assets.Mechanism, assets.EventRequest(setup.FromRoot, setup.ToRoot));
        Need(events.IsSuccess && events.SearchComplete && !events.ResultTruncated, "FreshEventInputIncomplete");
        var result = sdk.EvaluateGeometricDiscreteEmbodiment(m, candidate!.Geometry, initial, events, setup.MaxOccurrences, token);
        Need(result.Validation.IsValid && (result.Status == DiscreteStatus.Complete || result.Status == DiscreteStatus.IncompleteBudget), "GeometricEvaluationRejected"); return result;
    }
    public static void ValidateSetup(DiscreteEmbodimentModel model, DiscreteLayoutEvaluation setup)
    {
        Need(setup.PrimaryIndex >= 0 && setup.PrimaryIndex < model.Components.Single(c => c.Id == "primary").Detents && setup.SecondaryIndex >= 0 && setup.SecondaryIndex < model.Components.Single(c => c.Id == "secondary").Detents,
            "EvaluationIndexOutOfRange");
        Need(setup.FromRoot >= Rational.Zero && setup.ToRoot >= setup.FromRoot && setup.Cursor >= 0 && setup.MaxOccurrences >= 0 && setup.MaxOccurrences <= 4096, "EvaluationInputBounds");
    }
    public static DiscreteLayoutProject Regenerate(GearInvestSdk sdk, DiscreteLayoutProject saved, CancellationToken token = default, Action<int, int>? progress = null)
    {
        Validate(sdk, saved);
        // No saved candidate or execution is fed to the producer. Only source bytes, request and explicit selection/setup.
        var assets = new DiscreteLayoutAssets(sdk, saved.Assets.ModelBytes, saved.Assets.MechanismBytes, saved.Assets.Binding);
        var generated = sdk.GenerateDiscreteLayouts(assets.Model, saved.Request, token, progress);
        Need(generated.Status != DiscreteLayoutStatus.Cancelled, "Cancelled");
        if (saved.Generation != null) Need(sdk.WriteDiscreteLayoutGeneration(assets.Model, generated).SequenceEqual(sdk.WriteDiscreteLayoutGeneration(assets.Model, saved.Generation)), "RegenerationDrift: ordered geometry/search/metrics/bytes");
        Need(saved.SelectedId == null || generated.Candidates.Any(c => c.Geometry.GeometryId == saved.SelectedId), "SelectionUnavailable");
        var execution = saved.SelectedId != null && saved.Evaluation != null ? Evaluate(sdk, assets, generated, saved.SelectedId, saved.Evaluation, token) : null;
        if (saved.Execution != null) Need(execution != null && sdk.WriteGeometricDiscreteResult(assets.Model, generated.Candidates.Single(c => c.Geometry.GeometryId == saved.SelectedId).Geometry, execution)
            .SequenceEqual(sdk.WriteGeometricDiscreteResult(saved.Assets.Model, saved.SelectedGeometry!, saved.Execution)), "RegenerationDrift: execution bytes");
        return new DiscreteLayoutProject(assets, generated.Normalized.Request!, generated, saved.SelectedId, saved.Evaluation, execution);
    }
    public static void Validate(GearInvestSdk sdk, DiscreteLayoutProject p)
    {
        var n = sdk.NormalizeDiscreteLayoutRequest(p.Request); Need(n.IsValid && p.Request.Source.Canonical == p.Assets.Binding.Canonical, "ProjectSourceRequestMismatch");
        if (p.Evaluation != null) ValidateSetup(p.Assets.Model, p.Evaluation);
        if (p.Generation == null) Need(p.SelectedId == null && p.Execution == null, "DraftCannotOwnResults");
        else
        {
            Need(p.Generation.RequestId == n.RequestId, "StaleGenerationRequest"); sdk.WriteDiscreteLayoutGeneration(p.Assets.Model, p.Generation);
            Need(p.SelectedId == null || p.SelectedGeometry != null, "SelectionUnavailable");
        }
        if (p.Execution != null)
        {
            Need(p.SelectedGeometry != null && p.Evaluation != null, "MissingSelectedEvaluation"); var e = p.Evaluation!; var x = p.Execution;
            Need(x.Initial.CandidateId == p.Assets.Mechanism.CandidateId && x.Initial.DriverId == p.Assets.Binding.DriverId && x.Initial.RootTurns == e.FromRoot && x.ToRoot == e.ToRoot && x.Initial.Cursor == e.Cursor && x.Budget == e.MaxOccurrences &&
                x.Initial.Positions[0].Index == e.PrimaryIndex && x.Initial.Positions[1].Index == e.SecondaryIndex, "ExecutionSetupMismatch");
            Need(x.Initial.Positions[0].UnwrappedTurns == (e.PrimaryTurns ?? new Rational(e.PrimaryIndex, p.Assets.Model.Components.Single(c => c.Id == "primary").Detents)) &&
                x.Initial.Positions[1].UnwrappedTurns == (e.SecondaryTurns ?? new Rational(e.SecondaryIndex, p.Assets.Model.Components.Single(c => c.Id == "secondary").Detents)), "ExecutionPoseMismatch");
            var eventInput = sdk.EvaluatePeriodicEvents(p.Assets.Mechanism, p.Assets.EventRequest(e.FromRoot, e.ToRoot));
            Need(eventInput.IsSuccess && eventInput.SearchComplete && !eventInput.ResultTruncated && eventInput.EventBridgeRequestId == x.EventRequestId && eventInput.Occurrences.Count == x.Known, "ExecutionEventInputMismatch");
            sdk.WriteGeometricDiscreteResult(p.Assets.Model, p.SelectedGeometry!, x);
        }
    }
    public static DiscreteLayoutProjectManifest Save(GearInvestSdk sdk, DiscreteLayoutProject project, string directory, bool overwrite = false)
    {
        Validate(sdk, project); var root = Root(directory); Directory.CreateDirectory(root); RejectLinks(root);
        var manifestPath = Path.Combine(root, "project.json"); RejectLinks(manifestPath);
        if (File.Exists(manifestPath)) { Need(overwrite, "ProjectExists: explicit Save overwrite required"); Load(sdk, root); }
        else Need(!Directory.EnumerateFileSystemEntries(root).Any(), "SaveAsRequiresEmptyDirectory");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["model"] = project.Assets.ModelBytes, ["mechanism"] = project.Assets.MechanismBytes, ["request"] = sdk.WriteDiscreteLayoutRequest(project.Request) };
        if (project.Generation != null) files.Add("generation", sdk.WriteDiscreteLayoutGeneration(project.Assets.Model, project.Generation));
        if (project.SelectedGeometry != null) files.Add("geometry", sdk.WriteDiscreteGeometry(project.Assets.Model, project.SelectedGeometry));
        if (project.Execution != null) files.Add("execution", sdk.WriteGeometricDiscreteResult(project.Assets.Model, project.SelectedGeometry!, project.Execution));
        var revision = PortableProjectStorage.Revision(files);
        var prefix = "revisions/" + revision + "/";
        var records = files.Select(f => new DiscreteLayoutProjectFile(f.Key, prefix + f.Key + ".json", f.Value.Length, Sha(f.Value))).ToArray();
        var n = sdk.NormalizeDiscreteLayoutRequest(project.Request);
        var m = new DiscreteLayoutProjectManifest(project.Assets.Model.ModelId, project.Assets.Mechanism.CandidateId, n.RequestId, records,
            project.Generation?.Status, project.Generation?.SearchComplete ?? false, project.Generation?.ResultTruncated ?? false,
            project.Generation?.Candidates.Select(c => c.Geometry.GeometryId), project.SelectedId, project.Evaluation, project.Execution?.ExecutionId);
        var manifest = DiscreteLayoutProjectJson.Write(m);
        PortableProjectStorage.Commit(root, revision, files, manifest);
        return m;
    }
    public static DiscreteLayoutProject Load(GearInvestSdk sdk, string directory)
    {
        var root = Root(directory); var m = DiscreteLayoutProjectJson.Read(ReadBounded(Resolve(root, "project.json"), 128 * 1024));
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var f in m.Files) { var content = ReadBounded(Resolve(root, f.Path), f.Bytes); Need(content.Length == f.Bytes && Sha(content) == f.Sha256, "ProjectFileIntegrity:" + f.Role); bytes.Add(f.Role, content); }
        var request = sdk.ReadDiscreteLayoutRequest(bytes["request"]); var assets = new DiscreteLayoutAssets(sdk, bytes["model"], bytes["mechanism"], request.Source);
        Need(assets.Model.ModelId == m.ModelId && assets.Mechanism.CandidateId == m.MechanismId && sdk.NormalizeDiscreteLayoutRequest(request).RequestId == m.RequestId, "ProjectIdentityMismatch");
        var generation = bytes.ContainsKey("generation") ? sdk.ReadDiscreteLayoutGeneration(assets.Model, bytes["generation"]) : null;
        Need(generation?.Status == m.GenerationStatus && (generation?.SearchComplete ?? false) == m.SearchComplete && (generation?.ResultTruncated ?? false) == m.ResultTruncated &&
            (generation?.Candidates.Select(c => c.Geometry.GeometryId) ?? Array.Empty<string>()).SequenceEqual(m.OrderedGeometryIds), "ProjectSearchSummaryMismatch");
        var selected = m.SelectedGeometryId == null ? null : generation?.Candidates.SingleOrDefault(c => c.Geometry.GeometryId == m.SelectedGeometryId)?.Geometry;
        if (selected != null) Need(bytes["geometry"].SequenceEqual(sdk.WriteDiscreteGeometry(assets.Model, selected)), "SelectedGeometryMismatch");
        var execution = bytes.ContainsKey("execution") ? sdk.ReadGeometricDiscreteResult(assets.Model, selected!, bytes["execution"]) : null;
        Need(execution?.ExecutionId == m.ExecutionId, "SelectedExecutionMismatch");
        var project = new DiscreteLayoutProject(assets, request, generation, m.SelectedGeometryId, m.Evaluation, execution); Validate(sdk, project); return project;
    }
}
