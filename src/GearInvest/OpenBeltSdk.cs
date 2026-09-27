using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum OpenBeltFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class OpenBeltFinalizationResult
{
    internal OpenBeltFinalizationResult(OpenBeltFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, OpenBeltArtifactWriteResult? written = null)
    {
        if ((status == OpenBeltFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized open belt results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public OpenBeltFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public OpenBeltArtifactWriteResult? Written { get; }
    public OpenBeltArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == OpenBeltFinalizationStatus.Finalized;
}
public sealed class OpenBeltArtifactValidationResult
{
    internal OpenBeltArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public OpenBeltDraft ComposeOpenBeltDraft(OpenBeltDefinition definition)
    { var draft = new OpenBeltDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteOpenBeltDraft(OpenBeltDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return OpenBeltJson.WriteDraft(draft); }
    public OpenBeltDraft ReadOpenBeltDraft(byte[] bytes)
    { var draft = OpenBeltJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public OpenBeltEditResult ApplyOpenBeltEdits(OpenBeltDraft draft, OpenBeltEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return OpenBeltEditor.Apply(draft, batch); }
    public OpenBeltAnalysis AnalyzeOpenBeltDraft(OpenBeltDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return OpenBeltAnalyzer.Analyze(draft); }
    public OpenBeltCompatibilityResult QueryOpenBeltCompatibility(OpenBeltDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return OpenBeltConnectionQuery.Query(draft); }
    /// <summary>Proposes a replacement specification from fresh valid geometry; never edits the draft or selected belt.</summary>
    public OpenBeltLengthSpecification CreateOpenBeltForCurrentRoute(OpenBeltDraft draft)
    {
        var query = QueryOpenBeltCompatibility(draft);
        if (query.Route is null) throw new ArgumentException("A current exact open route is required before proposing a belt length.", nameof(draft));
        return OpenBeltGeometry.CreateBeltForCurrentRoute(query.Route);
    }
    public OpenBeltEvaluation EvaluateOpenBeltAnalysis(OpenBeltAnalysis analysis, ExactQuantity rootTurns) => OpenBeltAnalyzer.Evaluate(analysis, rootTurns);
    public OpenBeltOutputEquivalenceResult CompareOpenBeltOutputMotion(OpenBeltAnalysis before, OpenBeltAnalysis after, OutputComparisonRequest request) => OpenBeltOutputComparer.Compare(before, after, request);
    public byte[] WriteOpenBeltLengthSpecification(OpenBeltLengthSpecification specification) => OpenBeltJson.WriteLengthSpecification(specification);
    public OpenBeltLengthSpecification ReadOpenBeltLengthSpecification(byte[] bytes) => OpenBeltJson.ReadLengthSpecification(bytes);
    public byte[] WriteOpenBeltEditBatch(OpenBeltEditBatch batch) => OpenBeltJson.WriteBatch(batch);
    public OpenBeltEditBatch ReadOpenBeltEditBatch(byte[] bytes) => OpenBeltJson.ReadBatch(bytes);
    public byte[] WriteOpenBeltEditResult(OpenBeltEditResult result) => OpenBeltJson.WriteEditResult(result);
    public byte[] WriteOpenBeltAnalysis(OpenBeltAnalysis analysis) => OpenBeltJson.WriteAnalysis(analysis);
    public OpenBeltAnalysis VerifyOpenBeltAnalysis(OpenBeltDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return OpenBeltJson.VerifyAnalysis(draft, bytes); }
    public byte[] WriteOpenBeltEvaluation(OpenBeltEvaluation evaluation) => OpenBeltJson.WriteEvaluation(evaluation);
    public OpenBeltEvaluation VerifyOpenBeltEvaluation(OpenBeltAnalysis analysis, ExactQuantity input, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return OpenBeltJson.VerifyEvaluation(analysis, input, bytes); }
    public byte[] WriteOpenBeltCompatibilityResult(OpenBeltCompatibilityResult result) => OpenBeltJson.WriteCompatibility(result);
    public byte[] WriteOpenBeltOutputEquivalenceResult(OpenBeltOutputEquivalenceResult result) => OpenBeltJson.WriteComparison(result);
    public OpenBeltEditSession CreateOpenBeltEditSession(OpenBeltDraft initial, IEnumerable<OpenBeltEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return OpenBeltJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteOpenBeltEditSession(OpenBeltEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return OpenBeltJson.WriteSession(session); }
    public OpenBeltEditSession ReadOpenBeltEditSession(byte[] bytes)
    { var session = OpenBeltJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public OpenBeltDraft ReapplyOpenBeltEditSession(OpenBeltEditSession session) => CreateOpenBeltEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveOpenBeltDraft(OpenBeltDraft draft, string path) => SaveMechanicalSidecar(WriteOpenBeltDraft(draft), path);
    public OpenBeltDraft LoadOpenBeltDraft(string path) => ReadOpenBeltDraft(PortableProjectStorage.ReadBounded(path, OpenBeltJson.MaxDocumentBytes));
    public void SaveOpenBeltLengthSpecification(OpenBeltLengthSpecification specification, string path) => SaveMechanicalSidecar(WriteOpenBeltLengthSpecification(specification), path);
    public OpenBeltLengthSpecification LoadOpenBeltLengthSpecification(string path) => ReadOpenBeltLengthSpecification(PortableProjectStorage.ReadBounded(path, OpenBeltJson.MaxDocumentBytes));
    public void SaveOpenBeltEditBatch(OpenBeltEditBatch batch, string path) => SaveMechanicalSidecar(WriteOpenBeltEditBatch(batch), path);
    public OpenBeltEditBatch LoadOpenBeltEditBatch(string path) => ReadOpenBeltEditBatch(PortableProjectStorage.ReadBounded(path, OpenBeltJson.MaxDocumentBytes));
    public void SaveOpenBeltEditResult(OpenBeltEditResult result, string path) => SaveMechanicalSidecar(WriteOpenBeltEditResult(result), path);
    public void SaveOpenBeltAnalysis(OpenBeltAnalysis analysis, string path) => SaveMechanicalSidecar(WriteOpenBeltAnalysis(analysis), path);
    public void SaveOpenBeltEvaluation(OpenBeltEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteOpenBeltEvaluation(evaluation), path);
    public void SaveOpenBeltCompatibilityResult(OpenBeltCompatibilityResult result, string path) => SaveMechanicalSidecar(WriteOpenBeltCompatibilityResult(result), path);
    public void SaveOpenBeltOutputEquivalenceResult(OpenBeltOutputEquivalenceResult result, string path) => SaveMechanicalSidecar(WriteOpenBeltOutputEquivalenceResult(result), path);
    public void SaveOutputComparisonRequest(OutputComparisonRequest request, string path) => SaveMechanicalSidecar(WriteOutputComparisonRequest(request), path);
    public void SaveOpenBeltEditSession(OpenBeltEditSession session, string path) => SaveMechanicalSidecar(WriteOpenBeltEditSession(session), path);
    public OpenBeltEditSession LoadOpenBeltEditSession(string path) => ReadOpenBeltEditSession(PortableProjectStorage.ReadBounded(path, OpenBeltJson.MaxDocumentBytes));

    /// <summary>Actual complete 21A source finalize/reimport, then exact current route, selected belt, graph, references, targets and required scope.</summary>
    public OpenBeltFinalizationResult TryFinalizeOpenBeltDraft(OpenBeltDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        OpenBeltFinalizationResult Refuse(OpenBeltFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "OpenBeltFinalization", detail: detail, scope: OpenBeltProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.InputShaftId, d.Device.InputPortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? OpenBeltFinalizationStatus.ResourceLimitExceeded : MapOpenBeltSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = OpenBeltAnalyzer.Analyze(draft);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(issue => issue.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var unsupported = analysis.Diagnostics.Any(issue => issue.Code == "UnsupportedBeltRouting");
                return Refuse(unsupported ? OpenBeltFinalizationStatus.UnsupportedByExportProfile : target ? OpenBeltFinalizationStatus.TargetMismatch :
                    unresolved ? OpenBeltFinalizationStatus.UnresolvedRequiredValidation : OpenBeltFinalizationStatus.InvalidMechanicalDefinition,
                    "OpenBeltAdmissionFailed", "Complete current source, distinct pulley mounting, strict open route, explicit belt length, no-slip graph, references, targets and required validation must all pass.", analysis.Diagnostics);
            }
            var written = OpenBeltJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved);
            var reread = OpenBeltJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !OpenBeltJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(OpenBeltFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New open belt artifact identity or exact readback failed.");
            return new(OpenBeltFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsAttachmentResourceRefusal(e))
        { return Refuse(OpenBeltFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(OpenBeltFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static OpenBeltFinalizationStatus MapOpenBeltSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => OpenBeltFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => OpenBeltFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => OpenBeltFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => OpenBeltFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => OpenBeltFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => OpenBeltFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown original source finalization status.")
    };
    public byte[] WriteOpenBeltFinalizationResult(OpenBeltFinalizationResult result) => OpenBeltJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SaveOpenBeltFinalizationResult(OpenBeltFinalizationResult result, string path) => SaveMechanicalSidecar(WriteOpenBeltFinalizationResult(result), path);
    public OpenBeltArtifact ReadOpenBeltArtifact(byte[] bytes)
    {
        var artifact = OpenBeltJson.ReadArtifact(bytes); var validation = ValidateOpenBeltArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh open belt source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteOpenBeltArtifact(OpenBeltArtifact artifact)
    {
        var validation = ValidateOpenBeltArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid open belt artifact: " + validation.Detail); return OpenBeltJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyOpenBeltIdentity(OpenBeltArtifact artifact) => OpenBeltJson.VerifyIdentity(artifact);
    public OpenBeltArtifactValidationResult ValidateOpenBeltArtifact(OpenBeltArtifact artifact)
    {
        var identity = OpenBeltJson.VerifyIdentity(artifact); var final = TryFinalizeOpenBeltDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(OpenBeltJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current route/selected-belt/no-slip request correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public OpenBeltArtifactWriteResult RebuildOpenBeltArtifact(OpenBeltArtifact artifact)
    {
        var identity = VerifyOpenBeltIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Open belt artifact identity mismatch.");
        var fresh = TryFinalizeOpenBeltDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(OpenBeltJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and open belt reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveOpenBeltFinalization(OpenBeltFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful open belt finalization has no normal artifact.");
        var bytes = WriteOpenBeltArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveOpenBeltArtifact(OpenBeltArtifact artifact, string path) => SaveMechanicalSidecar(WriteOpenBeltArtifact(artifact), path);
    public OpenBeltArtifact LoadOpenBeltArtifact(string path) => ReadOpenBeltArtifact(PortableProjectStorage.ReadBounded(path, OpenBeltJson.MaxDocumentBytes));
}
