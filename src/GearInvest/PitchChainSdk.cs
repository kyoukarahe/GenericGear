using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum PitchChainFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class PitchChainFinalizationResult
{
    internal PitchChainFinalizationResult(PitchChainFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, PitchChainArtifactWriteResult? written = null)
    {
        if ((status == PitchChainFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized pitch chain results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public PitchChainFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public PitchChainArtifactWriteResult? Written { get; }
    public PitchChainArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == PitchChainFinalizationStatus.Finalized;
}
public sealed class PitchChainArtifactValidationResult
{
    internal PitchChainArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public PitchChainDraft ComposePitchChainDraft(PitchChainDefinition definition)
    { var draft = new PitchChainDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WritePitchChainDraft(PitchChainDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return PitchChainJson.WriteDraft(draft); }
    public PitchChainDraft ReadPitchChainDraft(byte[] bytes)
    { var draft = PitchChainJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public PitchChainEditResult ApplyPitchChainEdits(PitchChainDraft draft, PitchChainEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return PitchChainEditor.Apply(draft, batch); }
    public PitchChainAnalysis AnalyzePitchChainDraft(PitchChainDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return PitchChainAnalyzer.Analyze(draft); }
    public PitchChainCompatibilityResult QueryPitchChainCompatibility(PitchChainDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return PitchChainConnectionQuery.Query(draft); }
    /// <summary>Fresh geometry only produces an independent proposal. Explicit selected-chain editing remains a separate operation.</summary>
    public PitchChainSpecification CreatePitchChainForCurrentAssembly(PitchChainDraft draft, string chainId = "chain", BigInteger materialRegistration = default)
    {
        VerifyMechanicalImportProvenance(draft.Definition.Source);
        return PitchChainGeometry.CreateChainForCurrentAssembly(draft, chainId, materialRegistration);
    }
    public PitchChainEvaluation EvaluatePitchChainAnalysis(PitchChainAnalysis analysis, ExactQuantity rootTurns) => PitchChainAnalyzer.Evaluate(analysis, rootTurns);
    public PitchChainOutputEquivalenceResult ComparePitchChainOutputMotion(PitchChainAnalysis before, PitchChainAnalysis after, OutputComparisonRequest request) => PitchChainEquivalence.Compare(before, after, request);
    public byte[] WritePitchChainSpecification(PitchChainSpecification specification) => PitchChainJson.WriteSpecification(specification);
    public PitchChainSpecification ReadPitchChainSpecification(byte[] bytes) => PitchChainJson.ReadSpecification(bytes);
    public byte[] WritePitchChainEditBatch(PitchChainEditBatch batch) => PitchChainJson.WriteBatch(batch);
    public PitchChainEditBatch ReadPitchChainEditBatch(byte[] bytes) => PitchChainJson.ReadBatch(bytes);
    public byte[] WritePitchChainEditResult(PitchChainEditResult result) => PitchChainJson.WriteEditResult(result);
    public byte[] WritePitchChainAnalysis(PitchChainAnalysis analysis) => PitchChainJson.WriteAnalysis(analysis);
    public PitchChainAnalysis VerifyPitchChainAnalysis(PitchChainDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return PitchChainJson.VerifyAnalysis(draft, bytes); }
    public byte[] WritePitchChainEvaluation(PitchChainEvaluation evaluation) => PitchChainJson.WriteEvaluation(evaluation);
    public PitchChainEvaluation VerifyPitchChainEvaluation(PitchChainAnalysis analysis, ExactQuantity input, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return PitchChainJson.VerifyEvaluation(analysis, input, bytes); }
    public byte[] WritePitchChainCompatibilityResult(PitchChainCompatibilityResult result) => PitchChainJson.WriteCompatibility(result);
    public byte[] WritePitchChainOutputEquivalenceResult(PitchChainOutputEquivalenceResult result) => PitchChainJson.WriteComparison(result);
    public PitchChainEditSession CreatePitchChainEditSession(PitchChainDraft initial, IEnumerable<PitchChainEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return PitchChainJson.CreateSession(initial, batches, comparisons); }
    public byte[] WritePitchChainEditSession(PitchChainEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return PitchChainJson.WriteSession(session); }
    public PitchChainEditSession ReadPitchChainEditSession(byte[] bytes)
    { var session = PitchChainJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public PitchChainDraft ReapplyPitchChainEditSession(PitchChainEditSession session) => CreatePitchChainEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SavePitchChainDraft(PitchChainDraft draft, string path) => SaveMechanicalSidecar(WritePitchChainDraft(draft), path);
    public PitchChainDraft LoadPitchChainDraft(string path) => ReadPitchChainDraft(PortableProjectStorage.ReadBounded(path, PitchChainJson.MaxDocumentBytes));
    public void SavePitchChainSpecification(PitchChainSpecification specification, string path) => SaveMechanicalSidecar(WritePitchChainSpecification(specification), path);
    public PitchChainSpecification LoadPitchChainSpecification(string path) => ReadPitchChainSpecification(PortableProjectStorage.ReadBounded(path, PitchChainJson.MaxDocumentBytes));
    public void SavePitchChainEditBatch(PitchChainEditBatch batch, string path) => SaveMechanicalSidecar(WritePitchChainEditBatch(batch), path);
    public PitchChainEditBatch LoadPitchChainEditBatch(string path) => ReadPitchChainEditBatch(PortableProjectStorage.ReadBounded(path, PitchChainJson.MaxDocumentBytes));
    public void SavePitchChainEditResult(PitchChainEditResult result, string path) => SaveMechanicalSidecar(WritePitchChainEditResult(result), path);
    public void SavePitchChainAnalysis(PitchChainAnalysis analysis, string path) => SaveMechanicalSidecar(WritePitchChainAnalysis(analysis), path);
    public void SavePitchChainEvaluation(PitchChainEvaluation evaluation, string path) => SaveMechanicalSidecar(WritePitchChainEvaluation(evaluation), path);
    public void SavePitchChainCompatibilityResult(PitchChainCompatibilityResult result, string path) => SaveMechanicalSidecar(WritePitchChainCompatibilityResult(result), path);
    public void SavePitchChainOutputEquivalenceResult(PitchChainOutputEquivalenceResult result, string path) => SaveMechanicalSidecar(WritePitchChainOutputEquivalenceResult(result), path);
    public void SavePitchChainEditSession(PitchChainEditSession session, string path) => SaveMechanicalSidecar(WritePitchChainEditSession(session), path);
    public PitchChainEditSession LoadPitchChainEditSession(string path) => ReadPitchChainEditSession(PortableProjectStorage.ReadBounded(path, PitchChainJson.MaxDocumentBytes));

    /// <summary>Actual complete 21A source finalize/reimport, then exact current route, selected chain, graph, references, targets and required scope.</summary>
    public PitchChainFinalizationResult TryFinalizePitchChainDraft(PitchChainDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        PitchChainFinalizationResult Refuse(PitchChainFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "PitchChainFinalization", detail: detail, scope: PitchChainProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.InputShaftId, d.Device.InputPortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? PitchChainFinalizationStatus.ResourceLimitExceeded : MapPitchChainSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = PitchChainAnalyzer.Analyze(draft);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(issue => issue.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var resource = analysis.Diagnostics.Any(issue => issue.Code == "ChainResourceLimit");
                var unsupported = analysis.LocalCompatibility.Verdict == ConnectionCompatibilityVerdict.Unsupported;
                return Refuse(resource ? PitchChainFinalizationStatus.ResourceLimitExceeded : unsupported ? PitchChainFinalizationStatus.UnsupportedByExportProfile : target ? PitchChainFinalizationStatus.TargetMismatch :
                    unresolved ? PitchChainFinalizationStatus.UnresolvedRequiredValidation : PitchChainFinalizationStatus.InvalidMechanicalDefinition,
                    "PitchChainAdmissionFailed", "Complete current source, distinct sprocket mounting, regular polygon closure, explicit selected chain, synchronized shaft graph, references, targets and required validation must all pass.", analysis.Diagnostics);
            }
            var written = PitchChainJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved);
            var reread = PitchChainJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !PitchChainJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(PitchChainFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New pitch chain artifact identity or exact readback failed.");
            return new(PitchChainFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsPitchChainResourceRefusal(e))
        { return Refuse(PitchChainFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(PitchChainFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static bool IsPitchChainResourceRefusal(Exception exception)
    {
        if (IsAttachmentResourceRefusal(exception)) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if ((current is ArgumentException || current is ArtifactFormatException) && current.Message is
                "Pitch chain document byte limit exceeded." or "Pitch chain JSON node limit exceeded." or "Pitch chain integer digit bound exceeded.") return true;
        return false;
    }
    private static PitchChainFinalizationStatus MapPitchChainSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => PitchChainFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => PitchChainFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => PitchChainFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => PitchChainFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => PitchChainFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => PitchChainFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown original source finalization status.")
    };
    public byte[] WritePitchChainFinalizationResult(PitchChainFinalizationResult result) => PitchChainJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SavePitchChainFinalizationResult(PitchChainFinalizationResult result, string path) => SaveMechanicalSidecar(WritePitchChainFinalizationResult(result), path);
    public PitchChainArtifact ReadPitchChainArtifact(byte[] bytes)
    {
        var artifact = PitchChainJson.ReadArtifact(bytes); var validation = ValidatePitchChainArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh pitch chain source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WritePitchChainArtifact(PitchChainArtifact artifact)
    {
        var validation = ValidatePitchChainArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid pitch chain artifact: " + validation.Detail); return PitchChainJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyPitchChainIdentity(PitchChainArtifact artifact) => PitchChainJson.VerifyIdentity(artifact);
    public PitchChainArtifactValidationResult ValidatePitchChainArtifact(PitchChainArtifact artifact)
    {
        var identity = PitchChainJson.VerifyIdentity(artifact); var final = TryFinalizePitchChainDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(PitchChainJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current route/selected-chain/material request correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public PitchChainArtifactWriteResult RebuildPitchChainArtifact(PitchChainArtifact artifact)
    {
        var identity = VerifyPitchChainIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Pitch chain artifact identity mismatch.");
        var fresh = TryFinalizePitchChainDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(PitchChainJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and pitch chain reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SavePitchChainFinalization(PitchChainFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful pitch chain finalization has no normal artifact.");
        var bytes = WritePitchChainArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SavePitchChainArtifact(PitchChainArtifact artifact, string path) => SaveMechanicalSidecar(WritePitchChainArtifact(artifact), path);
    public PitchChainArtifact LoadPitchChainArtifact(string path) => ReadPitchChainArtifact(PortableProjectStorage.ReadBounded(path, PitchChainJson.MaxDocumentBytes));
}
