using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed class DifferentialFinalizationResult
{
    internal DifferentialFinalizationResult(MechanicalFinalizationStatus status, DifferentialAnalysis analysis, DifferentialArtifact? artifact, IEnumerable<MechanicalDiagnostic> diagnostics)
    { Status = status; Analysis = analysis; Artifact = artifact; Diagnostics = diagnostics.ToList().AsReadOnly(); }
    public MechanicalFinalizationStatus Status { get; }
    public DifferentialAnalysis Analysis { get; }
    public DifferentialArtifact? Artifact { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsFinalized => Status == MechanicalFinalizationStatus.Finalized;
}

public sealed partial class GearInvestSdk
{
    private void VerifyDifferentialSource(DifferentialDefinition d) { if (d.Prefix is not null) VerifyMechanicalImportProvenance(d.Prefix); }
    public DifferentialAnalysis PrepareDifferential(DifferentialRequest request) { VerifyDifferentialSource(request.Definition); return DifferentialAnalyzer.Prepare(request); }
    public DifferentialAnalysis AnalyzeDifferentialBoundary(DifferentialDefinition definition, IEnumerable<DifferentialBoundary> conditions)
    { VerifyDifferentialSource(definition); return DifferentialAnalyzer.AnalyzeBoundary(definition, conditions); }
    public byte[] WriteDifferentialDraft(DifferentialRequest request) { VerifyDifferentialSource(request.Definition); return DifferentialJson.WriteDraft(request); }
    public DifferentialRequest ReadDifferentialDraft(byte[] bytes) { var r = DifferentialJson.ReadDraft(bytes); VerifyDifferentialSource(r.Definition); return r; }
    public DifferentialFinalizationResult TryFinalizeDifferential(DifferentialRequest request)
    {
        var a = PrepareDifferential(request);
        if (!a.CanExport) return new(a.Checks.Any(c => c.Required && c.Verdict == OrientedCheckVerdict.NotPerformed) ? MechanicalFinalizationStatus.UnresolvedRequiredValidation : MechanicalFinalizationStatus.InvalidMechanicalDefinition, a, null, a.Diagnostics);
        byte[]? bytes = null; string? identity = null;
        if (request.Definition.Prefix is not null)
        {
            var source = FinalizeAttachedMechanicalSource(request.Definition.Prefix, request.Definition.CarrierShaft.Id, null);
            if (source.Failure.HasValue) return new(source.Failure.Value, a, null, source.Diagnostics ?? new[] { new MechanicalDiagnostic(source.Code, "DifferentialSource", detail: source.Detail) });
            bytes = source.Bytes; identity = source.Identity;
        }
        return new(MechanicalFinalizationStatus.Finalized, a, DifferentialJson.WriteArtifact(request, bytes, identity), Array.Empty<MechanicalDiagnostic>());
    }
    public DifferentialArtifact ReadDifferentialArtifact(byte[] bytes) => RebuildDifferentialArtifact(DifferentialJson.ReadArtifact(bytes));
    public DifferentialArtifact RebuildDifferentialArtifact(DifferentialArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact)); var fresh = TryFinalizeDifferential(artifact.Request);
        if (!fresh.IsFinalized || !fresh.Artifact!.Bytes.SequenceEqual(artifact.Bytes)) throw new ArtifactFormatException("Differential current source admission/rebuild differs."); return fresh.Artifact;
    }
    public byte[] ExportDifferentialReplay(DifferentialArtifact artifact) => DifferentialJson.WriteReplay(RebuildDifferentialArtifact(artifact));
    public DifferentialArtifact RebuildDifferentialReplay(byte[] bytes)
    {
        var a = RebuildDifferentialArtifact(DifferentialJson.ReadReplaySource(bytes));
        if (!ExportDifferentialReplay(a).SequenceEqual(bytes)) throw new ArtifactFormatException("Fresh differential replay differs."); return a;
    }
    public byte[] WriteDifferentialAnalysis(DifferentialAnalysis analysis) => DifferentialJson.WriteAnalysis(analysis);
    public DifferentialEvaluation EvaluateDifferential(DifferentialAnalysis analysis, DifferentialInputSnapshot input) => DifferentialEvaluator.Evaluate(analysis, input);
    public CarrierDisplayPose DisplayDifferential(DifferentialAnalysis analysis, DifferentialInputSnapshot input) => DifferentialEvaluator.Display(analysis, input);
    public void SaveDifferentialDraft(DifferentialRequest request, string path) => SaveMechanicalSidecar(WriteDifferentialDraft(request), path);
    public DifferentialRequest LoadDifferentialDraft(string path) => ReadDifferentialDraft(PortableProjectStorage.ReadBounded(path, DifferentialProfile.MaxDocumentBytes));
    public void SaveDifferentialArtifact(DifferentialArtifact artifact, string path) => SaveMechanicalSidecar(RebuildDifferentialArtifact(artifact).Bytes, path);
    public DifferentialArtifact LoadDifferentialArtifact(string path) => ReadDifferentialArtifact(PortableProjectStorage.ReadBounded(path, DifferentialProfile.MaxDocumentBytes));
    public void SaveDifferentialReplay(DifferentialArtifact artifact, string path) => SaveMechanicalSidecar(ExportDifferentialReplay(artifact), path);
}
