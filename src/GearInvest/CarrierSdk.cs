using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum CarrierFinalizationStatus { Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation, SourceRejected }
public sealed class CarrierFinalizationResult
{
    internal CarrierFinalizationResult(CarrierFinalizationStatus status, CarrierAnalysis analysis, CarrierArtifact? artifact, IEnumerable<MechanicalDiagnostic> diagnostics)
    { Status = status; Analysis = analysis; Artifact = artifact; Diagnostics = diagnostics.ToList().AsReadOnly(); }
    public CarrierFinalizationStatus Status { get; }
    public CarrierAnalysis Analysis { get; }
    public CarrierArtifact? Artifact { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsFinalized => Status == CarrierFinalizationStatus.Finalized;
}

public sealed partial class GearInvestSdk
{
    public CarrierAnalysis AnalyzeCarrier(CarrierDefinition request)
    { VerifyMechanicalImportProvenance(request.Source); return CarrierAnalyzer.Analyze(request); }
    public byte[] WriteCarrierDraft(CarrierDefinition request)
    { VerifyMechanicalImportProvenance(request.Source); return CarrierJson.WriteDraft(request); }
    public CarrierDefinition ReadCarrierDraft(byte[] bytes)
    { var request = CarrierJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(request.Source); return request; }
    public CarrierFinalizationResult TryFinalizeCarrier(CarrierDefinition request)
    {
        var analysis = AnalyzeCarrier(request);
        if (!analysis.IsValid)
        {
            var status = analysis.Checks.Any(c => c.Required && c.Verdict == OrientedCheckVerdict.Fail) ? CarrierFinalizationStatus.InvalidMechanicalDefinition : CarrierFinalizationStatus.UnresolvedRequiredValidation;
            return new(status, analysis, null, analysis.Diagnostics);
        }
        var source = FinalizeAttachedMechanicalSource(request.Source, request.CarrierShaftId, null);
        if (source.Failure.HasValue) return new(CarrierFinalizationStatus.SourceRejected, analysis, null,
            source.Diagnostics ?? new[] { new MechanicalDiagnostic(source.Code, "CarrierSource", detail: source.Detail) });
        var artifact = CarrierJson.WriteArtifact(request, source.Bytes, source.Identity);
        return new(CarrierFinalizationStatus.Finalized, analysis, artifact, Array.Empty<MechanicalDiagnostic>());
    }
    public CarrierArtifact ReadCarrierArtifact(byte[] bytes)
    { var artifact = CarrierJson.ReadArtifact(bytes); return RebuildCarrierArtifact(artifact); }
    public CarrierArtifact RebuildCarrierArtifact(CarrierArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        var fresh = TryFinalizeCarrier(artifact.Request);
        if (!fresh.IsFinalized || !fresh.Artifact!.Bytes.SequenceEqual(artifact.Bytes))
            throw new ArtifactFormatException("Carrier current-source finalization/reconstruction differs; no artifact adopted.");
        return fresh.Artifact;
    }
    public CarrierEvaluation EvaluateCarrier(CarrierAnalysis analysis, ExactQuantity input) => CarrierEvaluator.Evaluate(analysis, input);
    public CarrierDisplayPose DisplayCarrier(CarrierAnalysis analysis, ExactQuantity input) => CarrierEvaluator.Display(analysis, input);
    public byte[] ExportCarrierReplay(CarrierArtifact artifact) => CarrierJson.WriteReplay(RebuildCarrierArtifact(artifact));
    public CarrierArtifact RebuildCarrierReplay(byte[] bytes)
    {
        var artifact = RebuildCarrierArtifact(CarrierJson.ReadReplaySource(bytes));
        if (!CarrierJson.WriteReplay(artifact).SequenceEqual(bytes)) throw new ArtifactFormatException("Fresh carrier replay differs.");
        return artifact;
    }
    public void SaveCarrierDraft(CarrierDefinition request, string path) => SaveMechanicalSidecar(WriteCarrierDraft(request), path);
    public CarrierDefinition LoadCarrierDraft(string path) => ReadCarrierDraft(PortableProjectStorage.ReadBounded(path, CarrierProfile.MaxDocumentBytes));
    public void SaveCarrierArtifact(CarrierArtifact artifact, string path) => SaveMechanicalSidecar(RebuildCarrierArtifact(artifact).Bytes, path);
    public CarrierArtifact LoadCarrierArtifact(string path) => ReadCarrierArtifact(PortableProjectStorage.ReadBounded(path, CarrierProfile.MaxDocumentBytes));
    public void SaveCarrierReplay(CarrierArtifact artifact, string path) => SaveMechanicalSidecar(ExportCarrierReplay(artifact), path);
}
