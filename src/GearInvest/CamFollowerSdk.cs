using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum CamFollowerFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class CamFollowerFinalizationResult
{
    internal CamFollowerFinalizationResult(CamFollowerFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, CamFollowerArtifactWriteResult? written = null)
    {
        if ((status == CamFollowerFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized cam-follower results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public CamFollowerFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public CamFollowerArtifactWriteResult? Written { get; }
    public CamFollowerArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == CamFollowerFinalizationStatus.Finalized;
}
public sealed class CamFollowerArtifactValidationResult
{
    internal CamFollowerArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public CamFollowerDraft ComposeCamFollowerDraft(CamFollowerDefinition definition)
    { var draft = new CamFollowerDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteCamFollowerDraft(CamFollowerDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerJson.WriteDraft(draft); }
    public CamFollowerDraft ReadCamFollowerDraft(byte[] bytes)
    { var draft = CamFollowerJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public void SaveCamFollowerDraft(CamFollowerDraft draft, string path) => SaveMechanicalSidecar(WriteCamFollowerDraft(draft), path);
    public CamFollowerDraft LoadCamFollowerDraft(string path) => ReadCamFollowerDraft(PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));

    public byte[] WriteCamSupportProfile(CamSupportProfile profile) => CamFollowerJson.WriteSupportProfile(profile);
    public CamSupportProfile ReadCamSupportProfile(byte[] bytes) => CamFollowerJson.ReadSupportProfile(bytes);
    public void SaveCamSupportProfile(CamSupportProfile profile, string path) => SaveMechanicalSidecar(WriteCamSupportProfile(profile), path);
    public CamSupportProfile LoadCamSupportProfile(string path) => ReadCamSupportProfile(PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));
    public CamGeometryProofResult ProveCamSupportGeometry(CamSupportProfile profile, CamGeometryProofRequest? request = null) => CamGeometryProver.Prove(profile, request);
    public byte[] WriteCamGeometryProofRequest(CamSupportProfile profile, CamGeometryProofRequest request) => CamFollowerJson.WriteProofRequest(profile, request);
    public CamGeometryProofRequest ReadCamGeometryProofRequest(CamSupportProfile profile, byte[] bytes) => CamFollowerJson.ReadProofRequest(profile, bytes);
    public void SaveCamGeometryProofRequest(CamSupportProfile profile, CamGeometryProofRequest request, string path) => SaveMechanicalSidecar(WriteCamGeometryProofRequest(profile, request), path);
    public CamGeometryProofRequest LoadCamGeometryProofRequest(CamSupportProfile profile, string path) => ReadCamGeometryProofRequest(profile, PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));
    public byte[] WriteCamGeometryProof(CamGeometryProofResult proof) => CamFollowerJson.WriteProof(proof);
    public CamGeometryProofResult VerifyCamGeometryProof(CamSupportProfile profile, CamGeometryProofRequest request, byte[] bytes) => CamFollowerJson.VerifyProof(profile, request, bytes);
    public void SaveCamGeometryProof(CamGeometryProofResult proof, string path) => SaveMechanicalSidecar(WriteCamGeometryProof(proof), path);
    public CamContourPointRecipe? CreateCamContourPointRecipe(CamFollowerAnalysis analysis, Rational materialNormalTurns, ExactQuantity rootTurns)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return analysis.Descriptor?.CreateContourPointRecipe(materialNormalTurns, rootTurns); }
    public CamNumericResult EvaluateCamContourPoint(CamContourPointRecipe recipe, CamNumericRequest? request = null) => CamFollowerNumerics.Evaluate(recipe, request);
    public byte[] WriteCamContourPointRecipe(CamContourPointRecipe recipe) => CamFollowerJson.WriteContourRecipe(recipe);
    public byte[] WriteCamContourPointEvaluation(CamContourPointRecipe recipe, CamNumericResult result) => CamFollowerJson.WriteContourEvaluation(recipe, result);
    public CamNumericResult VerifyCamContourPointEvaluation(CamContourPointRecipe recipe, CamNumericRequest request, byte[] bytes) => CamFollowerJson.VerifyContourEvaluation(recipe, request, bytes);
    public void SaveCamContourPointEvaluation(CamContourPointRecipe recipe, CamNumericResult result, string path) => SaveMechanicalSidecar(WriteCamContourPointEvaluation(recipe, result), path);

    public CamFollowerEditResult ApplyCamFollowerEdits(CamFollowerDraft draft, CamFollowerEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerEditor.Apply(draft, batch); }
    public CamFollowerAnalysis AnalyzeCamFollowerDraft(CamFollowerDraft draft, CamGeometryProofRequest? proofRequest = null)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerAnalyzer.Analyze(draft, proofRequest); }
    public CamFollowerCompatibilityResult QueryCamFollowerCompatibility(CamFollowerDraft draft, CamGeometryProofRequest? proofRequest = null)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerAnalyzer.Query(draft, proofRequest); }
    public CamFollowerEvaluation EvaluateCamFollowerAnalysis(CamFollowerAnalysis analysis, ExactQuantity rootTurns, CamNumericRequest? request = null)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CamFollowerAnalyzer.Evaluate(analysis, rootTurns, request ?? CamNumericRequest.Default); }
    public CamFollowerPoseRecipe? CreateCamFollowerPoseRecipe(CamFollowerAnalysis analysis, ExactQuantity rootTurns)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return analysis.CreatePoseRecipe(rootTurns); }
    public CamFollowerOutputEquivalenceResult CompareCamFollowerOutputMotion(CamFollowerAnalysis before, CamFollowerAnalysis after, CamFollowerOutputComparisonRequest request)
    { VerifyMechanicalImportProvenance(before.Draft.Definition.Source); VerifyMechanicalImportProvenance(after.Draft.Definition.Source); return CamFollowerOutputComparer.Compare(before, after, request); }
    public byte[] WriteCamFollowerEditBatch(CamFollowerEditBatch batch) => CamFollowerJson.WriteBatch(batch);
    public CamFollowerEditBatch ReadCamFollowerEditBatch(byte[] bytes) => CamFollowerJson.ReadBatch(bytes);
    public byte[] WriteCamFollowerEditResult(CamFollowerEditResult result) => CamFollowerJson.WriteEditResult(result);
    public byte[] WriteCamFollowerAnalysis(CamFollowerAnalysis analysis) => CamFollowerJson.WriteAnalysis(analysis);
    public CamFollowerAnalysis VerifyCamFollowerAnalysis(CamFollowerDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerJson.VerifyAnalysis(draft, bytes); }
    public CamFollowerAnalysis VerifyCamFollowerAnalysis(CamFollowerDraft draft, CamGeometryProofRequest proofRequest, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerJson.VerifyAnalysis(draft, proofRequest, bytes); }
    public byte[] WriteCamFollowerEvaluation(CamFollowerEvaluation evaluation) => CamFollowerJson.WriteEvaluation(evaluation);
    public CamFollowerEvaluation VerifyCamFollowerEvaluation(CamFollowerAnalysis analysis, ExactQuantity root, CamNumericRequest request, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CamFollowerJson.VerifyEvaluation(analysis, root, request, bytes); }
    public byte[] WriteCamFollowerCompatibilityResult(CamFollowerCompatibilityResult result) => CamFollowerJson.WriteCompatibility(result);
    public CamFollowerCompatibilityResult VerifyCamFollowerCompatibilityResult(CamFollowerDraft draft, CamGeometryProofRequest proofRequest, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return CamFollowerJson.VerifyCompatibility(draft, proofRequest, bytes); }
    public byte[] WriteCamFollowerOutputComparisonRequest(CamFollowerOutputComparisonRequest request) => CamFollowerJson.WriteComparisonRequest(request);
    public CamFollowerOutputComparisonRequest ReadCamFollowerOutputComparisonRequest(byte[] bytes) => CamFollowerJson.ReadComparisonRequest(bytes);
    public byte[] WriteCamFollowerOutputEquivalenceResult(CamFollowerOutputEquivalenceResult result) => CamFollowerJson.WriteComparison(result);
    public CamFollowerOutputEquivalenceResult VerifyCamFollowerOutputEquivalenceResult(CamFollowerAnalysis before, CamFollowerAnalysis after, CamFollowerOutputComparisonRequest request, byte[] bytes)
    { VerifyMechanicalImportProvenance(before.Draft.Definition.Source); VerifyMechanicalImportProvenance(after.Draft.Definition.Source); return CamFollowerJson.VerifyComparison(before, after, request, bytes); }
    public byte[] WriteCamFollowerNumericRequest(CamFollowerAnalysis analysis, ExactQuantity root, CamNumericRequest request)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CamFollowerJson.WriteNumericRequest(analysis, root, request); }
    public CamNumericRequest ReadCamFollowerNumericRequest(CamFollowerAnalysis analysis, ExactQuantity root, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return CamFollowerJson.ReadNumericRequest(analysis, root, bytes); }
    public CamFollowerEditSession CreateCamFollowerEditSession(CamFollowerDraft initial, IEnumerable<CamFollowerEditBatch> batches, IEnumerable<CamFollowerOutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return CamFollowerJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteCamFollowerEditSession(CamFollowerEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return CamFollowerJson.WriteSession(session); }
    public CamFollowerEditSession ReadCamFollowerEditSession(byte[] bytes)
    { var session = CamFollowerJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public CamFollowerDraft ReapplyCamFollowerEditSession(CamFollowerEditSession session) => CreateCamFollowerEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveCamFollowerEditBatch(CamFollowerEditBatch batch, string path) => SaveMechanicalSidecar(WriteCamFollowerEditBatch(batch), path);
    public CamFollowerEditBatch LoadCamFollowerEditBatch(string path) => ReadCamFollowerEditBatch(PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));
    public void SaveCamFollowerEditResult(CamFollowerEditResult result, string path) => SaveMechanicalSidecar(WriteCamFollowerEditResult(result), path);
    public void SaveCamFollowerAnalysis(CamFollowerAnalysis analysis, string path) => SaveMechanicalSidecar(WriteCamFollowerAnalysis(analysis), path);
    public void SaveCamFollowerEvaluation(CamFollowerEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteCamFollowerEvaluation(evaluation), path);
    public void SaveCamFollowerCompatibilityResult(CamFollowerCompatibilityResult result, string path) => SaveMechanicalSidecar(WriteCamFollowerCompatibilityResult(result), path);
    public void SaveCamFollowerOutputComparisonRequest(CamFollowerOutputComparisonRequest request, string path) => SaveMechanicalSidecar(WriteCamFollowerOutputComparisonRequest(request), path);
    public CamFollowerOutputComparisonRequest LoadCamFollowerOutputComparisonRequest(string path) => ReadCamFollowerOutputComparisonRequest(PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));
    public void SaveCamFollowerOutputEquivalenceResult(CamFollowerOutputEquivalenceResult result, string path) => SaveMechanicalSidecar(WriteCamFollowerOutputEquivalenceResult(result), path);
    public void SaveCamFollowerNumericRequest(CamFollowerAnalysis analysis, ExactQuantity root, CamNumericRequest request, string path) => SaveMechanicalSidecar(WriteCamFollowerNumericRequest(analysis, root, request), path);
    public CamNumericRequest LoadCamFollowerNumericRequest(CamFollowerAnalysis analysis, ExactQuantity root, string path) => ReadCamFollowerNumericRequest(analysis, root, PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));
    public void SaveCamFollowerEditSession(CamFollowerEditSession session, string path) => SaveMechanicalSidecar(WriteCamFollowerEditSession(session), path);
    public CamFollowerEditSession LoadCamFollowerEditSession(string path) => ReadCamFollowerEditSession(PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));

    /// <summary>Complete current source finalize/reimport followed by exact geometry, contact, range, target and required-scope admission.</summary>
    public CamFollowerFinalizationResult TryFinalizeCamFollowerDraft(CamFollowerDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        CamFollowerFinalizationResult Refuse(CamFollowerFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "CamFollowerFinalization", detail: detail, scope: CamFollowerProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.SourceShaftId, d.Device.SourcePortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? CamFollowerFinalizationStatus.ResourceLimitExceeded : MapCamFollowerSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = CamFollowerAnalyzer.Analyze(draft, CamGeometryProofRequest.Default);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(x => x.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var resource = analysis.Diagnostics.Any(x => x.Code == "NumericResourceLimit" || x.Code == "GeometryResourceLimit" || x.Code == "ResourceLimitExceeded");
                var unsupported = analysis.LocalCompatibility.Verdict == ConnectionCompatibilityVerdict.Unsupported;
                return Refuse(resource ? CamFollowerFinalizationStatus.ResourceLimitExceeded : unsupported ? CamFollowerFinalizationStatus.UnsupportedByExportProfile : target ? CamFollowerFinalizationStatus.TargetMismatch :
                    unresolved ? CamFollowerFinalizationStatus.UnresolvedRequiredValidation : CamFollowerFinalizationStatus.InvalidMechanicalDefinition,
                    "CamFollowerAdmissionFailed", "Current source, periodic support geometry, strict convexity, finite face, full-cycle guide coverage, independent targets and required validation must pass.", analysis.Diagnostics);
            }
            var written = CamFollowerJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved); var reread = CamFollowerJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !CamFollowerJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(CamFollowerFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New exact artifact identity or fresh readback failed.");
            return new(CamFollowerFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsCamFollowerResourceRefusal(e))
        { return Refuse(CamFollowerFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(CamFollowerFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static bool IsCamFollowerResourceRefusal(Exception exception)
    {
        if (IsAttachmentResourceRefusal(exception)) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if ((current is ArgumentException || current is ArtifactFormatException) && current.Message is "Cam-follower document byte limit exceeded." or "Cam-follower JSON node limit exceeded.") return true;
        return false;
    }
    private static CamFollowerFinalizationStatus MapCamFollowerSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => CamFollowerFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => CamFollowerFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => CamFollowerFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => CamFollowerFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => CamFollowerFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => CamFollowerFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown source finalization status.")
    };
    public byte[] WriteCamFollowerFinalizationResult(CamFollowerFinalizationResult result) => CamFollowerJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SaveCamFollowerFinalizationResult(CamFollowerFinalizationResult result, string path) => SaveMechanicalSidecar(WriteCamFollowerFinalizationResult(result), path);

    public CamFollowerArtifact ReadCamFollowerArtifact(byte[] bytes)
    {
        var artifact = CamFollowerJson.ReadArtifact(bytes); var validation = ValidateCamFollowerArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh cam-follower source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteCamFollowerArtifact(CamFollowerArtifact artifact)
    {
        var validation = ValidateCamFollowerArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid cam-follower artifact: " + validation.Detail); return CamFollowerJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyCamFollowerIdentity(CamFollowerArtifact artifact) => CamFollowerJson.VerifyIdentity(artifact);
    public CamFollowerArtifactValidationResult ValidateCamFollowerArtifact(CamFollowerArtifact artifact)
    {
        var identity = CamFollowerJson.VerifyIdentity(artifact); var final = TryFinalizeCamFollowerDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(CamFollowerJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current geometry/contact/function/reference correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public CamFollowerArtifactWriteResult RebuildCamFollowerArtifact(CamFollowerArtifact artifact)
    {
        var identity = VerifyCamFollowerIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Cam-follower artifact identity mismatch.");
        var fresh = TryFinalizeCamFollowerDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(CamFollowerJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and cam-follower reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveCamFollowerFinalization(CamFollowerFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful cam-follower finalization has no normal artifact.");
        var bytes = WriteCamFollowerArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveCamFollowerArtifact(CamFollowerArtifact artifact, string path) => SaveMechanicalSidecar(WriteCamFollowerArtifact(artifact), path);
    public CamFollowerArtifact LoadCamFollowerArtifact(string path) => ReadCamFollowerArtifact(PortableProjectStorage.ReadBounded(path, CamFollowerJson.MaxDocumentBytes));
}
