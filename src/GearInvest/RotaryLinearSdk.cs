using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

/// <summary>Additive mixed-finalization outcomes; no change to the existing rotational 21A enum.</summary>
public enum RotaryLinearFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}

public sealed class RotaryLinearFinalizationResult
{
    internal RotaryLinearFinalizationResult(RotaryLinearFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics,
        RotaryLinearArtifactWriteResult? written = null)
    {
        if ((status == RotaryLinearFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized mixed results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public RotaryLinearFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public RotaryLinearArtifactWriteResult? Written { get; }
    public RotaryLinearArtifact? Artifact => Written?.Artifact;
    public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes;
    public bool IsFinalized => Status == RotaryLinearFinalizationStatus.Finalized;
}
public sealed class RotaryLinearArtifactValidationResult
{
    internal RotaryLinearArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public RotaryLinearDraft ComposeRotaryLinearDraft(RotaryLinearDefinition definition)
    { var draft = new RotaryLinearDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteRotaryLinearDraft(RotaryLinearDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RotaryLinearJson.WriteDraft(draft); }
    public RotaryLinearDraft ReadRotaryLinearDraft(byte[] bytes)
    { var draft = RotaryLinearJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public RotaryLinearEditResult ApplyRotaryLinearEdits(RotaryLinearDraft draft, RotaryLinearEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RotaryLinearEditor.Apply(draft, batch); }
    public RotaryLinearAnalysis AnalyzeRotaryLinearDraft(RotaryLinearDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RotaryLinearAnalyzer.Analyze(draft); }
    public LeadScrewCompatibilityResult QueryLeadScrewCompatibility(RotaryLinearDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RotaryLinearConnectionQuery.Query(draft); }
    public RotaryLinearEvaluation EvaluateRotaryLinearAnalysis(RotaryLinearAnalysis analysis, ExactQuantity rootTurns) => RotaryLinearAnalyzer.Evaluate(analysis, rootTurns);
    public LinearOutputEquivalenceResult CompareLinearOutputMotion(RotaryLinearAnalysis before, RotaryLinearAnalysis after, LinearOutputComparisonRequest request) => RotaryLinearOutputComparer.Compare(before, after, request);
    public byte[] WriteRotaryLinearEditBatch(RotaryLinearEditBatch batch) => RotaryLinearJson.WriteBatch(batch);
    public RotaryLinearEditBatch ReadRotaryLinearEditBatch(byte[] bytes) => RotaryLinearJson.ReadBatch(bytes);
    public byte[] WriteRotaryLinearEditResult(RotaryLinearEditResult result) => RotaryLinearJson.WriteEditResult(result);
    public byte[] WriteRotaryLinearAnalysis(RotaryLinearAnalysis analysis) => RotaryLinearJson.WriteAnalysis(analysis);
    public RotaryLinearAnalysis VerifyRotaryLinearAnalysis(RotaryLinearDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RotaryLinearJson.VerifyAnalysis(draft, bytes); }
    public byte[] WriteRotaryLinearEvaluation(RotaryLinearEvaluation evaluation) => RotaryLinearJson.WriteEvaluation(evaluation);
    public byte[] WriteLeadScrewCompatibilityResult(LeadScrewCompatibilityResult result) => RotaryLinearJson.WriteCompatibility(result);
    public byte[] WriteLinearOutputComparisonRequest(LinearOutputComparisonRequest request) => RotaryLinearJson.WriteComparisonRequest(request);
    public LinearOutputComparisonRequest ReadLinearOutputComparisonRequest(byte[] bytes) => RotaryLinearJson.ReadComparisonRequest(bytes);
    public byte[] WriteLinearOutputEquivalenceResult(LinearOutputEquivalenceResult result) => RotaryLinearJson.WriteComparison(result);
    public RotaryLinearEditSession CreateRotaryLinearEditSession(RotaryLinearDraft initial, IEnumerable<RotaryLinearEditBatch> batches, IEnumerable<LinearOutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return RotaryLinearJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteRotaryLinearEditSession(RotaryLinearEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return RotaryLinearJson.WriteSession(session); }
    public RotaryLinearEditSession ReadRotaryLinearEditSession(byte[] bytes)
    { var session = RotaryLinearJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public RotaryLinearDraft ReapplyRotaryLinearEditSession(RotaryLinearEditSession session) => CreateRotaryLinearEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveRotaryLinearDraft(RotaryLinearDraft draft, string path) => SaveMechanicalSidecar(WriteRotaryLinearDraft(draft), path);
    public RotaryLinearDraft LoadRotaryLinearDraft(string path) => ReadRotaryLinearDraft(PortableProjectStorage.ReadBounded(path, RotaryLinearJson.MaxDocumentBytes));
    public void SaveRotaryLinearEditSession(RotaryLinearEditSession session, string path) => SaveMechanicalSidecar(WriteRotaryLinearEditSession(session), path);
    public RotaryLinearEditSession LoadRotaryLinearEditSession(string path) => ReadRotaryLinearEditSession(PortableProjectStorage.ReadBounded(path, RotaryLinearJson.MaxDocumentBytes));

    /// <summary>Fresh complete 21A source finalization, exact retained ID check, then current dimensioned reanalysis. No cached gain or implicit mapping.</summary>
    public RotaryLinearFinalizationResult TryFinalizeRotaryLinearDraft(RotaryLinearDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        RotaryLinearFinalizationResult Refuse(RotaryLinearFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "MixedFinalization", detail: detail, scope: RotaryLinearProfile.Id) });
        try
        {
            var definition = draft.Definition;
            var finalSource = FinalizeAttachedMechanicalSource(definition.Source, definition.Device.ScrewShaftId, definition.Device.ScrewPortId);
            if (finalSource.Failure.HasValue)
                return Refuse(finalSource.ResourceLimited ? RotaryLinearFinalizationStatus.ResourceLimitExceeded : MapRotarySourceStatus(finalSource.Failure.Value), finalSource.Code, finalSource.Detail, finalSource.Diagnostics);
            var analysis = RotaryLinearAnalyzer.Analyze(draft);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(d => d.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                return Refuse(target ? RotaryLinearFinalizationStatus.TargetMismatch : unresolved ? RotaryLinearFinalizationStatus.UnresolvedRequiredValidation : RotaryLinearFinalizationStatus.InvalidMechanicalDefinition,
                    "MixedAdmissionFailed", "Current source, grounding, typed law, operating interval and independent requirements must all pass.", analysis.Diagnostics);
            }
            var written = RotaryLinearJson.WriteArtifact(draft, finalSource.Bytes, finalSource.Identity, finalSource.OriginalPreserved);
            var reread = RotaryLinearJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !RotaryLinearJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(RotaryLinearFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New mixed artifact identity or readback failed.");
            return new(RotaryLinearFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsAttachmentResourceRefusal(e))
        { return Refuse(RotaryLinearFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(RotaryLinearFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static RotaryLinearFinalizationStatus MapRotarySourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => RotaryLinearFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => RotaryLinearFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => RotaryLinearFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => RotaryLinearFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => RotaryLinearFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => RotaryLinearFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown original source finalization status.")
    };
    // Exact known resource failures only. Geometry, unknown formats and arbitrary messages are not reclassified by substring.
    public byte[] WriteRotaryLinearFinalizationResult(RotaryLinearFinalizationResult result) => RotaryLinearJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public RotaryLinearArtifact ReadRotaryLinearArtifact(byte[] bytes)
    {
        var artifact = RotaryLinearJson.ReadArtifact(bytes); var validation = ValidateRotaryLinearArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh mixed source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteRotaryLinearArtifact(RotaryLinearArtifact artifact)
    {
        var validation = ValidateRotaryLinearArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid mixed artifact: " + validation.Detail); return RotaryLinearJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyRotaryLinearIdentity(RotaryLinearArtifact artifact) => RotaryLinearJson.VerifyIdentity(artifact);
    public RotaryLinearArtifactValidationResult ValidateRotaryLinearArtifact(RotaryLinearArtifact artifact)
    {
        var identity = RotaryLinearJson.VerifyIdentity(artifact); var final = TryFinalizeRotaryLinearDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(RotaryLinearJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh source finalization and complete current request/analysis correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public RotaryLinearArtifactWriteResult RebuildRotaryLinearArtifact(RotaryLinearArtifact artifact)
    {
        var identity = VerifyRotaryLinearIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Mixed artifact identity mismatch.");
        var fresh = TryFinalizeRotaryLinearDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(RotaryLinearJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and mixed reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveRotaryLinearFinalization(RotaryLinearFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful mixed finalization has no normal artifact.");
        var bytes = WriteRotaryLinearArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveRotaryLinearArtifact(RotaryLinearArtifact artifact, string path) => SaveMechanicalSidecar(WriteRotaryLinearArtifact(artifact), path);
    public RotaryLinearArtifact LoadRotaryLinearArtifact(string path) => ReadRotaryLinearArtifact(PortableProjectStorage.ReadBounded(path, RotaryLinearJson.MaxDocumentBytes));
}
