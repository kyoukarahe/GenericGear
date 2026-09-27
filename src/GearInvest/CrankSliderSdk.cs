using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum CrankSliderFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class CrankSliderFinalizationResult
{
    internal CrankSliderFinalizationResult(CrankSliderFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, CrankSliderArtifactWriteResult? written = null)
    {
        if ((status == CrankSliderFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized crank-slider results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public CrankSliderFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public CrankSliderArtifactWriteResult? Written { get; }
    public CrankSliderArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == CrankSliderFinalizationStatus.Finalized;
}
public sealed class CrankSliderArtifactValidationResult
{
    internal CrankSliderArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public CrankSliderDraft ComposeCrankSliderDraft(CrankSliderDefinition definition)
    { var draft = new CrankSliderDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteCrankSliderDraft(CrankSliderDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CrankSliderJson.WriteDraft(draft); }
    public CrankSliderDraft ReadCrankSliderDraft(byte[] bytes)
    { var draft = CrankSliderJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public void SaveCrankSliderDraft(CrankSliderDraft draft, string path) => SaveMechanicalSidecar(WriteCrankSliderDraft(draft), path);
    public CrankSliderDraft LoadCrankSliderDraft(string path) => ReadCrankSliderDraft(PortableProjectStorage.ReadBounded(path, CrankSliderJson.MaxDocumentBytes));

    public CrankSliderEditResult ApplyCrankSliderEdits(CrankSliderDraft draft, CrankSliderEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CrankSliderEditor.Apply(draft, batch); }
    public CrankSliderAnalysis AnalyzeCrankSliderDraft(CrankSliderDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CrankSliderAnalyzer.Analyze(draft); }
    public CrankSliderCompatibilityResult QueryCrankSliderCompatibility(CrankSliderDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CrankSliderAnalyzer.Query(draft); }
    public CrankSliderEvaluation EvaluateCrankSliderAnalysis(CrankSliderAnalysis analysis, ExactQuantity rootTurns, CrankSliderNumericRequest? request = null)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CrankSliderAnalyzer.Evaluate(analysis, rootTurns, request ?? CrankSliderNumericRequest.Default); }
    public CrankSliderPoseRecipe? CreateCrankSliderPoseRecipe(CrankSliderAnalysis analysis, ExactQuantity rootTurns)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return analysis.CreatePoseRecipe(rootTurns); }
    public CrankSliderOutputEquivalenceResult CompareCrankSliderOutputMotion(CrankSliderAnalysis before, CrankSliderAnalysis after, CrankSliderOutputComparisonRequest request)
    { VerifyMechanicalImportProvenance(before.Draft.Definition.Source); VerifyMechanicalImportProvenance(after.Draft.Definition.Source); return CrankSliderOutputComparer.Compare(before, after, request); }
    public byte[] WriteCrankSliderEditBatch(CrankSliderEditBatch batch) => CrankSliderJson.WriteBatch(batch);
    public CrankSliderEditBatch ReadCrankSliderEditBatch(byte[] bytes) => CrankSliderJson.ReadBatch(bytes);
    public byte[] WriteCrankSliderEditResult(CrankSliderEditResult result) => CrankSliderJson.WriteEditResult(result);
    public byte[] WriteCrankSliderAnalysis(CrankSliderAnalysis analysis) => CrankSliderJson.WriteAnalysis(analysis);
    public CrankSliderAnalysis VerifyCrankSliderAnalysis(CrankSliderDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CrankSliderJson.VerifyAnalysis(draft, bytes); }
    public byte[] WriteCrankSliderEvaluation(CrankSliderEvaluation evaluation) => CrankSliderJson.WriteEvaluation(evaluation);
    public CrankSliderEvaluation VerifyCrankSliderEvaluation(CrankSliderAnalysis analysis, ExactQuantity root, CrankSliderNumericRequest request, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CrankSliderJson.VerifyEvaluation(analysis, root, request, bytes); }
    public byte[] WriteCrankSliderCompatibilityResult(CrankSliderCompatibilityResult result) => CrankSliderJson.WriteCompatibility(result);
    public byte[] WriteCrankSliderOutputComparisonRequest(CrankSliderOutputComparisonRequest request) => CrankSliderJson.WriteComparisonRequest(request);
    public CrankSliderOutputComparisonRequest ReadCrankSliderOutputComparisonRequest(byte[] bytes) => CrankSliderJson.ReadComparisonRequest(bytes);
    public byte[] WriteCrankSliderOutputEquivalenceResult(CrankSliderOutputEquivalenceResult result) => CrankSliderJson.WriteComparison(result);
    public byte[] WriteCrankSliderNumericRequest(CrankSliderAnalysis analysis, ExactQuantity root, CrankSliderNumericRequest request)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CrankSliderJson.WriteNumericRequest(analysis, root, request); }
    public CrankSliderNumericRequest ReadCrankSliderNumericRequest(CrankSliderAnalysis analysis, ExactQuantity root, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CrankSliderJson.ReadNumericRequest(analysis, root, bytes); }
    public CrankSliderEditSession CreateCrankSliderEditSession(CrankSliderDraft initial, IEnumerable<CrankSliderEditBatch> batches, IEnumerable<CrankSliderOutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return CrankSliderJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteCrankSliderEditSession(CrankSliderEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return CrankSliderJson.WriteSession(session); }
    public CrankSliderEditSession ReadCrankSliderEditSession(byte[] bytes)
    { var session = CrankSliderJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public CrankSliderDraft ReapplyCrankSliderEditSession(CrankSliderEditSession session) => CreateCrankSliderEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveCrankSliderEditBatch(CrankSliderEditBatch batch, string path) => SaveMechanicalSidecar(WriteCrankSliderEditBatch(batch), path);
    public CrankSliderEditBatch LoadCrankSliderEditBatch(string path) => ReadCrankSliderEditBatch(PortableProjectStorage.ReadBounded(path, CrankSliderJson.MaxDocumentBytes));
    public void SaveCrankSliderEditResult(CrankSliderEditResult result, string path) => SaveMechanicalSidecar(WriteCrankSliderEditResult(result), path);
    public void SaveCrankSliderAnalysis(CrankSliderAnalysis analysis, string path) => SaveMechanicalSidecar(WriteCrankSliderAnalysis(analysis), path);
    public void SaveCrankSliderEvaluation(CrankSliderEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteCrankSliderEvaluation(evaluation), path);
    public void SaveCrankSliderCompatibilityResult(CrankSliderCompatibilityResult result, string path) => SaveMechanicalSidecar(WriteCrankSliderCompatibilityResult(result), path);
    public void SaveCrankSliderOutputComparisonRequest(CrankSliderOutputComparisonRequest request, string path) => SaveMechanicalSidecar(WriteCrankSliderOutputComparisonRequest(request), path);
    public CrankSliderOutputComparisonRequest LoadCrankSliderOutputComparisonRequest(string path) => ReadCrankSliderOutputComparisonRequest(PortableProjectStorage.ReadBounded(path, CrankSliderJson.MaxDocumentBytes));
    public void SaveCrankSliderOutputEquivalenceResult(CrankSliderOutputEquivalenceResult result, string path) => SaveMechanicalSidecar(WriteCrankSliderOutputEquivalenceResult(result), path);
    public void SaveCrankSliderNumericRequest(CrankSliderAnalysis analysis, ExactQuantity root, CrankSliderNumericRequest request, string path) => SaveMechanicalSidecar(WriteCrankSliderNumericRequest(analysis, root, request), path);
    public CrankSliderNumericRequest LoadCrankSliderNumericRequest(CrankSliderAnalysis analysis, ExactQuantity root, string path) => ReadCrankSliderNumericRequest(analysis, root, PortableProjectStorage.ReadBounded(path, CrankSliderJson.MaxDocumentBytes));
    public void SaveCrankSliderEditSession(CrankSliderEditSession session, string path) => SaveMechanicalSidecar(WriteCrankSliderEditSession(session), path);
    public CrankSliderEditSession LoadCrankSliderEditSession(string path) => ReadCrankSliderEditSession(PortableProjectStorage.ReadBounded(path, CrankSliderJson.MaxDocumentBytes));

    /// <summary>Complete current source finalize/reimport followed by exact geometry, branch, range, target and required-scope admission.</summary>
    public CrankSliderFinalizationResult TryFinalizeCrankSliderDraft(CrankSliderDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        CrankSliderFinalizationResult Refuse(CrankSliderFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "CrankSliderFinalization", detail: detail, scope: CrankSliderProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.SourceShaftId, d.Device.SourcePortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? CrankSliderFinalizationStatus.ResourceLimitExceeded : MapCrankSliderSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = CrankSliderAnalyzer.Analyze(draft);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(x => x.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var resource = analysis.Diagnostics.Any(x => x.Code == "NumericResourceLimit" || x.Code == "ResourceLimitExceeded");
                var unsupported = analysis.LocalCompatibility.Verdict == ConnectionCompatibilityVerdict.Unsupported;
                return Refuse(resource ? CrankSliderFinalizationStatus.ResourceLimitExceeded : unsupported ? CrankSliderFinalizationStatus.UnsupportedByExportProfile : target ? CrankSliderFinalizationStatus.TargetMismatch :
                    unresolved ? CrankSliderFinalizationStatus.UnresolvedRequiredValidation : CrankSliderFinalizationStatus.InvalidMechanicalDefinition,
                    "CrankSliderAdmissionFailed", "Current source, physical joints, strict branch closure, full-cycle guide coverage, independent targets and required validation must pass.", analysis.Diagnostics);
            }
            var written = CrankSliderJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved); var reread = CrankSliderJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !CrankSliderJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(CrankSliderFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New exact artifact identity or fresh readback failed.");
            return new(CrankSliderFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsCrankSliderResourceRefusal(e))
        { return Refuse(CrankSliderFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(CrankSliderFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static bool IsCrankSliderResourceRefusal(Exception exception)
    {
        if (IsAttachmentResourceRefusal(exception)) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if ((current is ArgumentException || current is ArtifactFormatException) && current.Message is "Crank-slider document byte limit exceeded." or "Crank-slider JSON node limit exceeded.") return true;
        return false;
    }
    private static CrankSliderFinalizationStatus MapCrankSliderSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => CrankSliderFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => CrankSliderFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => CrankSliderFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => CrankSliderFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => CrankSliderFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => CrankSliderFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown source finalization status.")
    };
    public byte[] WriteCrankSliderFinalizationResult(CrankSliderFinalizationResult result) => CrankSliderJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SaveCrankSliderFinalizationResult(CrankSliderFinalizationResult result, string path) => SaveMechanicalSidecar(WriteCrankSliderFinalizationResult(result), path);

    public CrankSliderArtifact ReadCrankSliderArtifact(byte[] bytes)
    {
        var artifact = CrankSliderJson.ReadArtifact(bytes); var validation = ValidateCrankSliderArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh crank-slider source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteCrankSliderArtifact(CrankSliderArtifact artifact)
    {
        var validation = ValidateCrankSliderArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid crank-slider artifact: " + validation.Detail); return CrankSliderJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyCrankSliderIdentity(CrankSliderArtifact artifact) => CrankSliderJson.VerifyIdentity(artifact);
    public CrankSliderArtifactValidationResult ValidateCrankSliderArtifact(CrankSliderArtifact artifact)
    {
        var identity = CrankSliderJson.VerifyIdentity(artifact); var final = TryFinalizeCrankSliderDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(CrankSliderJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current geometry/branch/function/reference correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public CrankSliderArtifactWriteResult RebuildCrankSliderArtifact(CrankSliderArtifact artifact)
    {
        var identity = VerifyCrankSliderIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Crank-slider artifact identity mismatch.");
        var fresh = TryFinalizeCrankSliderDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(CrankSliderJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and crank-slider reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveCrankSliderFinalization(CrankSliderFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful crank-slider finalization has no normal artifact.");
        var bytes = WriteCrankSliderArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveCrankSliderArtifact(CrankSliderArtifact artifact, string path) => SaveMechanicalSidecar(WriteCrankSliderArtifact(artifact), path);
    public CrankSliderArtifact LoadCrankSliderArtifact(string path) => ReadCrankSliderArtifact(PortableProjectStorage.ReadBounded(path, CrankSliderJson.MaxDocumentBytes));
}
