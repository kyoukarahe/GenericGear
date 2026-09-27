using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum GenevaFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class GenevaFinalizationResult
{
    internal GenevaFinalizationResult(GenevaFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, GenevaArtifactWriteResult? written = null)
    {
        if ((status == GenevaFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized Geneva results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public GenevaFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public GenevaArtifactWriteResult? Written { get; }
    public GenevaArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == GenevaFinalizationStatus.Finalized;
}
public sealed class GenevaArtifactValidationResult
{
    internal GenevaArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public GenevaDraft ComposeGenevaDraft(GenevaDefinition definition)
    { var draft = new GenevaDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteGenevaDraft(GenevaDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaJson.WriteDraft(draft); }
    public GenevaDraft ReadGenevaDraft(byte[] bytes)
    { var draft = GenevaJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public void SaveGenevaDraft(GenevaDraft draft, string path) => SaveMechanicalSidecar(WriteGenevaDraft(draft), path);
    public GenevaDraft LoadGenevaDraft(string path) => ReadGenevaDraft(PortableProjectStorage.ReadBounded(path, GenevaJson.MaxDocumentBytes));

    public GenevaEditResult ApplyGenevaEdits(GenevaDraft draft, GenevaEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaEditor.Apply(draft, batch); }
    public GenevaAnalysis AnalyzeGenevaDraft(GenevaDraft draft, GenevaNumericRequest? geometryRequest = null)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaAnalyzer.Analyze(draft, geometryRequest); }
    public GenevaCompatibilityResult QueryGenevaCompatibility(GenevaDraft draft, GenevaNumericRequest? geometryRequest = null)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaAnalyzer.Query(draft, geometryRequest); }
    public GenevaEvaluation EvaluateGenevaAnalysis(GenevaAnalysis analysis, ExactQuantity rootTurns, GenevaNumericRequest? request = null)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return GenevaAnalyzer.Evaluate(analysis, rootTurns, request ?? GenevaNumericRequest.Default); }
    public GenevaMatchingGeometryProposal CreateMatchingGenevaGeometry(GenevaDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaAnalyzer.CreateMatchingGeometry(draft); }
    public GenevaPoseRecipe? CreateGenevaPoseRecipe(GenevaAnalysis analysis, ExactQuantity rootTurns)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return analysis.CreatePoseRecipe(rootTurns); }
    public GenevaMotionComparisonResult CompareGenevaOutputMotion(GenevaAnalysis before, GenevaAnalysis after, GenevaMotionComparisonRequest request)
    { VerifyMechanicalImportProvenance(before.Draft.Definition.Source); VerifyMechanicalImportProvenance(after.Draft.Definition.Source); return GenevaOutputComparer.Compare(before, after, request); }
    public byte[] WriteGenevaEditBatch(GenevaEditBatch batch) => GenevaJson.WriteBatch(batch);
    public GenevaEditBatch ReadGenevaEditBatch(byte[] bytes) => GenevaJson.ReadBatch(bytes);
    public byte[] WriteGenevaEditResult(GenevaEditResult result) => GenevaJson.WriteEditResult(result);
    public byte[] WriteGenevaAnalysis(GenevaAnalysis analysis) => GenevaJson.WriteAnalysis(analysis);
    public GenevaAnalysis VerifyGenevaAnalysis(GenevaDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaJson.VerifyAnalysis(draft, bytes); }
    public GenevaAnalysis VerifyGenevaAnalysis(GenevaDraft draft, GenevaNumericRequest geometryRequest, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaJson.VerifyAnalysis(draft, geometryRequest, bytes); }
    public byte[] WriteGenevaEvaluation(GenevaEvaluation evaluation) => GenevaJson.WriteEvaluation(evaluation);
    public GenevaEvaluation VerifyGenevaEvaluation(GenevaAnalysis analysis, ExactQuantity root, GenevaNumericRequest request, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return GenevaJson.VerifyEvaluation(analysis, root, request, bytes); }
    public byte[] WriteGenevaCompatibilityResult(GenevaCompatibilityResult result) => GenevaJson.WriteCompatibility(result);
    public GenevaCompatibilityResult VerifyGenevaCompatibilityResult(GenevaDraft draft, GenevaNumericRequest geometryRequest, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return GenevaJson.VerifyCompatibility(draft, geometryRequest, bytes); }
    public byte[] WriteGenevaMotionComparisonRequest(GenevaMotionComparisonRequest request) => GenevaJson.WriteComparisonRequest(request);
    public GenevaMotionComparisonRequest ReadGenevaMotionComparisonRequest(byte[] bytes) => GenevaJson.ReadComparisonRequest(bytes);
    public byte[] WriteGenevaMotionComparisonResult(GenevaMotionComparisonResult result) => GenevaJson.WriteComparison(result);
    public GenevaMotionComparisonResult VerifyGenevaMotionComparisonResult(GenevaAnalysis before, GenevaAnalysis after, GenevaMotionComparisonRequest request, byte[] bytes)
    { VerifyMechanicalImportProvenance(before.Draft.Definition.Source); VerifyMechanicalImportProvenance(after.Draft.Definition.Source); return GenevaJson.VerifyComparison(before, after, request, bytes); }
    public byte[] WriteGenevaNumericRequest(GenevaAnalysis analysis, ExactQuantity root, GenevaNumericRequest request)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return GenevaJson.WriteNumericRequest(analysis, root, request); }
    public GenevaNumericRequest ReadGenevaNumericRequest(GenevaAnalysis analysis, ExactQuantity root, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return GenevaJson.ReadNumericRequest(analysis, root, bytes); }
    public GenevaEditSession CreateGenevaEditSession(GenevaDraft initial, IEnumerable<GenevaEditBatch> batches, IEnumerable<GenevaMotionComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return GenevaJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteGenevaEditSession(GenevaEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return GenevaJson.WriteSession(session); }
    public GenevaEditSession ReadGenevaEditSession(byte[] bytes)
    { var session = GenevaJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public GenevaDraft ReapplyGenevaEditSession(GenevaEditSession session) => CreateGenevaEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveGenevaEditBatch(GenevaEditBatch batch, string path) => SaveMechanicalSidecar(WriteGenevaEditBatch(batch), path);
    public GenevaEditBatch LoadGenevaEditBatch(string path) => ReadGenevaEditBatch(PortableProjectStorage.ReadBounded(path, GenevaJson.MaxDocumentBytes));
    public void SaveGenevaEditResult(GenevaEditResult result, string path) => SaveMechanicalSidecar(WriteGenevaEditResult(result), path);
    public void SaveGenevaAnalysis(GenevaAnalysis analysis, string path) => SaveMechanicalSidecar(WriteGenevaAnalysis(analysis), path);
    public void SaveGenevaEvaluation(GenevaEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteGenevaEvaluation(evaluation), path);
    public void SaveGenevaCompatibilityResult(GenevaCompatibilityResult result, string path) => SaveMechanicalSidecar(WriteGenevaCompatibilityResult(result), path);
    public void SaveGenevaMotionComparisonRequest(GenevaMotionComparisonRequest request, string path) => SaveMechanicalSidecar(WriteGenevaMotionComparisonRequest(request), path);
    public GenevaMotionComparisonRequest LoadGenevaMotionComparisonRequest(string path) => ReadGenevaMotionComparisonRequest(PortableProjectStorage.ReadBounded(path, GenevaJson.MaxDocumentBytes));
    public void SaveGenevaMotionComparisonResult(GenevaMotionComparisonResult result, string path) => SaveMechanicalSidecar(WriteGenevaMotionComparisonResult(result), path);
    public void SaveGenevaNumericRequest(GenevaAnalysis analysis, ExactQuantity root, GenevaNumericRequest request, string path) => SaveMechanicalSidecar(WriteGenevaNumericRequest(analysis, root, request), path);
    public GenevaNumericRequest LoadGenevaNumericRequest(GenevaAnalysis analysis, ExactQuantity root, string path) => ReadGenevaNumericRequest(analysis, root, PortableProjectStorage.ReadBounded(path, GenevaJson.MaxDocumentBytes));
    public void SaveGenevaEditSession(GenevaEditSession session, string path) => SaveMechanicalSidecar(WriteGenevaEditSession(session), path);
    public GenevaEditSession LoadGenevaEditSession(string path) => ReadGenevaEditSession(PortableProjectStorage.ReadBounded(path, GenevaJson.MaxDocumentBytes));

    /// <summary>Complete current source finalize/reimport followed by exact geometry, contact, range, target and required-scope admission.</summary>
    public GenevaFinalizationResult TryFinalizeGenevaDraft(GenevaDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        GenevaFinalizationResult Refuse(GenevaFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "GenevaFinalization", detail: detail, scope: GenevaProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.SourceShaftId, d.Device.SourcePortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? GenevaFinalizationStatus.ResourceLimitExceeded : MapGenevaSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = GenevaAnalyzer.Analyze(draft, GenevaNumericRequest.Default);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(x => x.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var resource = analysis.Diagnostics.Any(x => x.Code == "NumericResourceLimit" || x.Code == "GeometryResourceLimit" || x.Code == "ResourceLimitExceeded");
                var unsupported = analysis.LocalCompatibility.Verdict == ConnectionCompatibilityVerdict.Unsupported;
                return Refuse(resource ? GenevaFinalizationStatus.ResourceLimitExceeded : unsupported ? GenevaFinalizationStatus.UnsupportedByExportProfile : target ? GenevaFinalizationStatus.TargetMismatch :
                    unresolved ? GenevaFinalizationStatus.UnresolvedRequiredValidation : GenevaFinalizationStatus.InvalidMechanicalDefinition,
                    "GenevaAdmissionFailed", "Current source, tangent-entry geometry, material registration, complete pin-slot indexing, ideal dwell lock, independent requirements and required validation must pass.", analysis.Diagnostics);
            }
            var written = GenevaJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved); var reread = GenevaJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !GenevaJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(GenevaFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New exact artifact identity or fresh readback failed.");
            return new(GenevaFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsGenevaResourceRefusal(e))
        { return Refuse(GenevaFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(GenevaFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static bool IsGenevaResourceRefusal(Exception exception)
    {
        if (IsAttachmentResourceRefusal(exception)) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if ((current is ArgumentException || current is ArtifactFormatException) && current.Message is "Geneva document byte limit exceeded." or "Geneva JSON node limit exceeded.") return true;
        return false;
    }
    private static GenevaFinalizationStatus MapGenevaSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => GenevaFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => GenevaFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => GenevaFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => GenevaFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => GenevaFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => GenevaFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown source finalization status.")
    };
    public byte[] WriteGenevaFinalizationResult(GenevaFinalizationResult result) => GenevaJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SaveGenevaFinalizationResult(GenevaFinalizationResult result, string path) => SaveMechanicalSidecar(WriteGenevaFinalizationResult(result), path);

    public GenevaArtifact ReadGenevaArtifact(byte[] bytes)
    {
        var artifact = GenevaJson.ReadArtifact(bytes); var validation = ValidateGenevaArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh Geneva source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteGenevaArtifact(GenevaArtifact artifact)
    {
        var validation = ValidateGenevaArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid Geneva artifact: " + validation.Detail); return GenevaJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyGenevaIdentity(GenevaArtifact artifact) => GenevaJson.VerifyIdentity(artifact);
    public GenevaArtifactValidationResult ValidateGenevaArtifact(GenevaArtifact artifact)
    {
        var identity = GenevaJson.VerifyIdentity(artifact); var final = TryFinalizeGenevaDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(GenevaJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current geometry/registration/pin-slot/ideal-lock/unwrapped-law/reference correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public GenevaArtifactWriteResult RebuildGenevaArtifact(GenevaArtifact artifact)
    {
        var identity = VerifyGenevaIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Geneva artifact identity mismatch.");
        var fresh = TryFinalizeGenevaDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(GenevaJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and Geneva reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveGenevaFinalization(GenevaFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful Geneva finalization has no normal artifact.");
        var bytes = WriteGenevaArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveGenevaArtifact(GenevaArtifact artifact, string path) => SaveMechanicalSidecar(WriteGenevaArtifact(artifact), path);
    public GenevaArtifact LoadGenevaArtifact(string path) => ReadGenevaArtifact(PortableProjectStorage.ReadBounded(path, GenevaJson.MaxDocumentBytes));
}

