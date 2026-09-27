using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum WormDriveFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class WormDriveFinalizationResult
{
    internal WormDriveFinalizationResult(WormDriveFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, WormDriveArtifactWriteResult? written = null)
    {
        if ((status == WormDriveFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized worm drive results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public WormDriveFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public WormDriveArtifactWriteResult? Written { get; }
    public WormDriveArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == WormDriveFinalizationStatus.Finalized;
}
public sealed class WormDriveArtifactValidationResult
{
    internal WormDriveArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public WormDriveDraft ComposeWormDriveDraft(WormDriveDefinition definition)
    { var draft = new WormDriveDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteWormDriveDraft(WormDriveDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return WormDriveJson.WriteDraft(draft); }
    public WormDriveDraft ReadWormDriveDraft(byte[] bytes)
    { var draft = WormDriveJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public WormDriveEditResult ApplyWormDriveEdits(WormDriveDraft draft, WormDriveEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return WormDriveEditor.Apply(draft, batch); }
    public WormDriveAnalysis AnalyzeWormDriveDraft(WormDriveDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return WormDriveAnalyzer.Analyze(draft); }
    public WormDriveCompatibilityResult QueryWormDriveCompatibility(WormDriveDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return WormDriveAnalyzer.Query(draft); }
    /// <summary>Independent matching-wheel proposal; selecting it remains an explicit edit.</summary>
    public IdealWormWheelSpecification CreateMatchingIdealWormWheel(CylindricalWormSpecification worm, int wheelToothCount) =>
        WormDriveBuilders.CreateMatchingIdealWormWheel(worm, wheelToothCount);
    public IdealWormWheelSpecification CreateMatchingIdealWormWheel(WormDriveDraft draft, int wheelToothCount)
    {
        VerifyMechanicalImportProvenance(draft.Definition.Source);
        return WormDriveBuilders.CreateMatchingIdealWormWheel(draft.Definition.Device.Worm, wheelToothCount);
    }
    /// <summary>Returns the required output pitch-center proposal; does not move the shaft or edit the draft.</summary>
    public ExactVector3 CreateCompatibleWormPlacement(WormDriveDraft draft)
    {
        VerifyMechanicalImportProvenance(draft.Definition.Source);
        return WormDriveBuilders.CreateCompatibleWormPlacement(draft);
    }
    public WormDriveEvaluation EvaluateWormDriveAnalysis(WormDriveAnalysis analysis, ExactQuantity rootTurns) => WormDriveAnalyzer.Evaluate(analysis, rootTurns);
    public WormDriveOutputEquivalenceResult CompareWormDriveOutputMotion(WormDriveAnalysis before, WormDriveAnalysis after, OutputComparisonRequest request) => WormDriveComparer.Compare(before, after, request);
    public byte[] WriteWormDriveSpecification(IdealWormWheelSpecification specification) => WormDriveJson.WriteSpecification(specification);
    public IdealWormWheelSpecification ReadWormDriveSpecification(byte[] bytes) => WormDriveJson.ReadSpecification(bytes);
    public byte[] WriteCylindricalWormSpecification(CylindricalWormSpecification specification) => WormDriveJson.WriteWormSpecification(specification);
    public CylindricalWormSpecification ReadCylindricalWormSpecification(byte[] bytes) => WormDriveJson.ReadWormSpecification(bytes);
    public void SaveCylindricalWormSpecification(CylindricalWormSpecification specification, string path) => SaveMechanicalSidecar(WriteCylindricalWormSpecification(specification), path);
    public CylindricalWormSpecification LoadCylindricalWormSpecification(string path) => ReadCylindricalWormSpecification(PortableProjectStorage.ReadBounded(path, WormDriveJson.MaxDocumentBytes));
    public byte[] WriteWormDriveEditBatch(WormDriveEditBatch batch) => WormDriveJson.WriteBatch(batch);
    public WormDriveEditBatch ReadWormDriveEditBatch(byte[] bytes) => WormDriveJson.ReadBatch(bytes);
    public byte[] WriteWormDriveEditResult(WormDriveEditResult result) => WormDriveJson.WriteEditResult(result);
    public byte[] WriteWormDriveAnalysis(WormDriveAnalysis analysis) => WormDriveJson.WriteAnalysis(analysis);
    public WormDriveAnalysis VerifyWormDriveAnalysis(WormDriveDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return WormDriveJson.VerifyAnalysis(draft, bytes); }
    public byte[] WriteWormDriveEvaluation(WormDriveEvaluation evaluation) => WormDriveJson.WriteEvaluation(evaluation);
    public WormDriveEvaluation VerifyWormDriveEvaluation(WormDriveAnalysis analysis, ExactQuantity input, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return WormDriveJson.VerifyEvaluation(analysis, input, bytes); }
    public byte[] WriteWormDriveCompatibilityResult(WormDriveCompatibilityResult result) => WormDriveJson.WriteCompatibility(result);
    public byte[] WriteWormDriveOutputEquivalenceResult(WormDriveOutputEquivalenceResult result) => WormDriveJson.WriteComparison(result);
    public WormDriveEditSession CreateWormDriveEditSession(WormDriveDraft initial, IEnumerable<WormDriveEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return WormDriveJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteWormDriveEditSession(WormDriveEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return WormDriveJson.WriteSession(session); }
    public WormDriveEditSession ReadWormDriveEditSession(byte[] bytes)
    { var session = WormDriveJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public WormDriveDraft ReapplyWormDriveEditSession(WormDriveEditSession session) => CreateWormDriveEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveWormDriveDraft(WormDriveDraft draft, string path) => SaveMechanicalSidecar(WriteWormDriveDraft(draft), path);
    public WormDriveDraft LoadWormDriveDraft(string path) => ReadWormDriveDraft(PortableProjectStorage.ReadBounded(path, WormDriveJson.MaxDocumentBytes));
    public void SaveWormDriveSpecification(IdealWormWheelSpecification specification, string path) => SaveMechanicalSidecar(WriteWormDriveSpecification(specification), path);
    public IdealWormWheelSpecification LoadWormDriveSpecification(string path) => ReadWormDriveSpecification(PortableProjectStorage.ReadBounded(path, WormDriveJson.MaxDocumentBytes));
    public void SaveWormDriveEditBatch(WormDriveEditBatch batch, string path) => SaveMechanicalSidecar(WriteWormDriveEditBatch(batch), path);
    public WormDriveEditBatch LoadWormDriveEditBatch(string path) => ReadWormDriveEditBatch(PortableProjectStorage.ReadBounded(path, WormDriveJson.MaxDocumentBytes));
    public void SaveWormDriveEditResult(WormDriveEditResult result, string path) => SaveMechanicalSidecar(WriteWormDriveEditResult(result), path);
    public void SaveWormDriveAnalysis(WormDriveAnalysis analysis, string path) => SaveMechanicalSidecar(WriteWormDriveAnalysis(analysis), path);
    public void SaveWormDriveEvaluation(WormDriveEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteWormDriveEvaluation(evaluation), path);
    public void SaveWormDriveCompatibilityResult(WormDriveCompatibilityResult result, string path) => SaveMechanicalSidecar(WriteWormDriveCompatibilityResult(result), path);
    public void SaveWormDriveOutputEquivalenceResult(WormDriveOutputEquivalenceResult result, string path) => SaveMechanicalSidecar(WriteWormDriveOutputEquivalenceResult(result), path);
    public void SaveWormDriveEditSession(WormDriveEditSession session, string path) => SaveMechanicalSidecar(WriteWormDriveEditSession(session), path);
    public WormDriveEditSession LoadWormDriveEditSession(string path) => ReadWormDriveEditSession(PortableProjectStorage.ReadBounded(path, WormDriveJson.MaxDocumentBytes));

    /// <summary>Actual complete 21A source finalize/reimport, then exact current geometry, independent wheel, graph, references, targets and required scope.</summary>
    public WormDriveFinalizationResult TryFinalizeWormDriveDraft(WormDriveDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        WormDriveFinalizationResult Refuse(WormDriveFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "WormDriveFinalization", detail: detail, scope: WormDriveProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.InputShaftId, d.Device.InputPortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? WormDriveFinalizationStatus.ResourceLimitExceeded : MapWormDriveSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = WormDriveAnalyzer.Analyze(draft);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(issue => issue.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var resource = analysis.Diagnostics.Any(issue => issue.Code == "WormResourceLimit" || issue.Code == "ResourceLimitExceeded");
                var unsupported = analysis.LocalCompatibility.Verdict == ConnectionCompatibilityVerdict.Unsupported;
                return Refuse(resource ? WormDriveFinalizationStatus.ResourceLimitExceeded : unsupported ? WormDriveFinalizationStatus.UnsupportedByExportProfile : target ? WormDriveFinalizationStatus.TargetMismatch :
                    unresolved ? WormDriveFinalizationStatus.UnresolvedRequiredValidation : WormDriveFinalizationStatus.InvalidMechanicalDefinition,
                    "WormDriveAdmissionFailed", "Complete current source, separate worm/wheel bodies, module/trace/station compatibility, ideal helical graph, references, targets and required validation must all pass.", analysis.Diagnostics);
            }
            var written = WormDriveJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved);
            var reread = WormDriveJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !WormDriveJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(WormDriveFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New worm drive artifact identity or exact readback failed.");
            return new(WormDriveFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsWormDriveResourceRefusal(e))
        { return Refuse(WormDriveFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(WormDriveFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static bool IsWormDriveResourceRefusal(Exception exception)
    {
        if (IsAttachmentResourceRefusal(exception)) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if ((current is ArgumentException || current is ArtifactFormatException) && current.Message is
                "Worm drive document byte limit exceeded." or "Worm drive JSON node limit exceeded." or "Worm drive integer digit bound exceeded.") return true;
        return false;
    }
    private static WormDriveFinalizationStatus MapWormDriveSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => WormDriveFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => WormDriveFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => WormDriveFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => WormDriveFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => WormDriveFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => WormDriveFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown original source finalization status.")
    };
    public byte[] WriteWormDriveFinalizationResult(WormDriveFinalizationResult result) => WormDriveJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SaveWormDriveFinalizationResult(WormDriveFinalizationResult result, string path) => SaveMechanicalSidecar(WriteWormDriveFinalizationResult(result), path);
    public WormDriveArtifact ReadWormDriveArtifact(byte[] bytes)
    {
        var artifact = WormDriveJson.ReadArtifact(bytes); var validation = ValidateWormDriveArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh worm drive source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteWormDriveArtifact(WormDriveArtifact artifact)
    {
        var validation = ValidateWormDriveArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid worm drive artifact: " + validation.Detail); return WormDriveJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyWormDriveIdentity(WormDriveArtifact artifact) => WormDriveJson.VerifyIdentity(artifact);
    public WormDriveArtifactValidationResult ValidateWormDriveArtifact(WormDriveArtifact artifact)
    {
        var identity = WormDriveJson.VerifyIdentity(artifact); var final = TryFinalizeWormDriveDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(WormDriveJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current worm/wheel/geometry/reference request correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public WormDriveArtifactWriteResult RebuildWormDriveArtifact(WormDriveArtifact artifact)
    {
        var identity = VerifyWormDriveIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Worm drive artifact identity mismatch.");
        var fresh = TryFinalizeWormDriveDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(WormDriveJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and worm drive reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveWormDriveFinalization(WormDriveFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful worm drive finalization has no normal artifact.");
        var bytes = WriteWormDriveArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveWormDriveArtifact(WormDriveArtifact artifact, string path) => SaveMechanicalSidecar(WriteWormDriveArtifact(artifact), path);
    public WormDriveArtifact LoadWormDriveArtifact(string path) => ReadWormDriveArtifact(PortableProjectStorage.ReadBounded(path, WormDriveJson.MaxDocumentBytes));
}
