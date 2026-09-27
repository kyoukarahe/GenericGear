using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum RackPinionFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded
}
public sealed class RackPinionFinalizationResult
{
    internal RackPinionFinalizationResult(RackPinionFinalizationStatus status, string definitionId, IEnumerable<MechanicalDiagnostic> diagnostics, RackPinionArtifactWriteResult? written = null)
    {
        if ((status == RackPinionFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only finalized rack results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public RackPinionFinalizationStatus Status { get; } public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; } public RackPinionArtifactWriteResult? Written { get; }
    public RackPinionArtifact? Artifact => Written?.Artifact; public string? ArtifactIdentity => Written?.Artifact.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes; public bool IsFinalized => Status == RackPinionFinalizationStatus.Finalized;
}
public sealed class RackPinionArtifactValidationResult
{
    internal RackPinionArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; } public bool SourceAndRequestCorrespond { get; } public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public RackPinionDraft ComposeRackPinionDraft(RackPinionDefinition definition)
    { var draft = new RackPinionDraft(definition); VerifyMechanicalImportProvenance(definition.Source); return draft; }
    public byte[] WriteRackPinionDraft(RackPinionDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RackPinionJson.WriteDraft(draft); }
    public RackPinionDraft ReadRackPinionDraft(byte[] bytes)
    { var draft = RackPinionJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft.Definition.Source); return draft; }
    public RackPinionEditResult ApplyRackPinionEdits(RackPinionDraft draft, RackPinionEditBatch batch)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RackPinionEditor.Apply(draft, batch); }
    public RackPinionAnalysis AnalyzeRackPinionDraft(RackPinionDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RackPinionAnalyzer.Analyze(draft); }
    public RackPinionCompatibilityResult QueryRackPinionCompatibility(RackPinionDraft draft)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RackPinionConnectionQuery.Query(draft); }
    public RackPinionEvaluation EvaluateRackPinionAnalysis(RackPinionAnalysis analysis, ExactQuantity rootTurns) => RackPinionAnalyzer.Evaluate(analysis, rootTurns);
    public PrismaticOutputEquivalenceResult CompareLinearOutputMotion(RackPinionAnalysis before, RackPinionAnalysis after, LinearOutputComparisonRequest request) => RackPinionOutputComparer.Compare(before, after, request);
    public PrismaticOutputEquivalenceResult CompareLinearOutputMotion(RackPinionAnalysis before, RotaryLinearAnalysis after, LinearOutputComparisonRequest request) => RackPinionOutputComparer.Compare(before, after, request);
    public PrismaticOutputEquivalenceResult CompareLinearOutputMotion(RotaryLinearAnalysis before, RackPinionAnalysis after, LinearOutputComparisonRequest request) => RackPinionOutputComparer.Compare(before, after, request);
    public byte[] WriteRackPinionEditBatch(RackPinionEditBatch batch) => RackPinionJson.WriteBatch(batch);
    public RackPinionEditBatch ReadRackPinionEditBatch(byte[] bytes) => RackPinionJson.ReadBatch(bytes);
    public byte[] WriteRackPinionEditResult(RackPinionEditResult result) => RackPinionJson.WriteEditResult(result);
    public byte[] WriteRackPinionAnalysis(RackPinionAnalysis analysis) => RackPinionJson.WriteAnalysis(analysis);
    public RackPinionAnalysis VerifyRackPinionAnalysis(RackPinionDraft draft, byte[] bytes)
    { VerifyMechanicalImportProvenance(draft.Definition.Source); return RackPinionJson.VerifyAnalysis(draft, bytes); }
    public byte[] WriteRackPinionEvaluation(RackPinionEvaluation evaluation) => RackPinionJson.WriteEvaluation(evaluation);
    public RackPinionEvaluation VerifyRackPinionEvaluation(RackPinionAnalysis analysis, ExactQuantity input, byte[] bytes)
    { VerifyMechanicalImportProvenance(analysis.Draft.Definition.Source); return RackPinionJson.VerifyEvaluation(analysis, input, bytes); }
    public byte[] WriteRackPinionCompatibilityResult(RackPinionCompatibilityResult result) => RackPinionJson.WriteCompatibility(result);
    public byte[] WritePrismaticOutputEquivalenceResult(PrismaticOutputEquivalenceResult result) => RackPinionJson.WriteComparison(result);
    public RackPinionEditSession CreateRackPinionEditSession(RackPinionDraft initial, IEnumerable<RackPinionEditBatch> batches, IEnumerable<LinearOutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial.Definition.Source); return RackPinionJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteRackPinionEditSession(RackPinionEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return RackPinionJson.WriteSession(session); }
    public RackPinionEditSession ReadRackPinionEditSession(byte[] bytes)
    { var session = RackPinionJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft.Definition.Source); return session; }
    public RackPinionDraft ReapplyRackPinionEditSession(RackPinionEditSession session) => CreateRackPinionEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveRackPinionDraft(RackPinionDraft draft, string path) => SaveMechanicalSidecar(WriteRackPinionDraft(draft), path);
    public RackPinionDraft LoadRackPinionDraft(string path) => ReadRackPinionDraft(PortableProjectStorage.ReadBounded(path, RackPinionJson.MaxDocumentBytes));
    public void SaveRackPinionEditBatch(RackPinionEditBatch batch, string path) => SaveMechanicalSidecar(WriteRackPinionEditBatch(batch), path);
    public void SaveRackPinionEditResult(RackPinionEditResult result, string path) => SaveMechanicalSidecar(WriteRackPinionEditResult(result), path);
    public void SaveRackPinionAnalysis(RackPinionAnalysis analysis, string path) => SaveMechanicalSidecar(WriteRackPinionAnalysis(analysis), path);
    public void SaveRackPinionEvaluation(RackPinionEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteRackPinionEvaluation(evaluation), path);
    public void SaveRackPinionCompatibilityResult(RackPinionCompatibilityResult result, string path) => SaveMechanicalSidecar(WriteRackPinionCompatibilityResult(result), path);
    public void SavePrismaticOutputEquivalenceResult(PrismaticOutputEquivalenceResult result, string path) => SaveMechanicalSidecar(WritePrismaticOutputEquivalenceResult(result), path);
    public void SaveLinearOutputComparisonRequest(LinearOutputComparisonRequest request, string path) => SaveMechanicalSidecar(WriteLinearOutputComparisonRequest(request), path);
    public void SaveRackPinionEditSession(RackPinionEditSession session, string path) => SaveMechanicalSidecar(WriteRackPinionEditSession(session), path);
    public RackPinionEditSession LoadRackPinionEditSession(string path) => ReadRackPinionEditSession(PortableProjectStorage.ReadBounded(path, RackPinionJson.MaxDocumentBytes));

    /// <summary>Actual 21A whole-source finalize/reimport, then fresh local CP/tangency/law/domain/requirements. No inferred body or shaft remapping.</summary>
    public RackPinionFinalizationResult TryFinalizeRackPinionDraft(RackPinionDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        RackPinionFinalizationResult Refuse(RackPinionFinalizationStatus status, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null) =>
            new(status, draft.DefinitionId, diagnostics ?? new[] { new MechanicalDiagnostic(code, "RackPinionFinalization", detail: detail, scope: RackPinionProfile.Id) });
        try
        {
            var d = draft.Definition; var source = FinalizeAttachedMechanicalSource(d.Source, d.Device.PinionShaftId, d.Device.PinionPortId);
            if (source.Failure.HasValue) return Refuse(source.ResourceLimited ? RackPinionFinalizationStatus.ResourceLimitExceeded : MapRackSourceStatus(source.Failure.Value), source.Code, source.Detail, source.Diagnostics);
            var analysis = RackPinionAnalyzer.Analyze(draft);
            if (!analysis.IsMechanicallyValid)
            {
                var target = analysis.Diagnostics.Any(issue => issue.Code == "TargetMismatch");
                var unresolved = analysis.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.NotPerformed || c.Verdict == OrientedCheckVerdict.Inconclusive));
                var unsupported = analysis.Diagnostics.Any(issue => issue.Code == "UnsupportedParameterization");
                return Refuse(unsupported ? RackPinionFinalizationStatus.UnsupportedByExportProfile : target ? RackPinionFinalizationStatus.TargetMismatch :
                    unresolved ? RackPinionFinalizationStatus.UnresolvedRequiredValidation : RackPinionFinalizationStatus.InvalidMechanicalDefinition,
                    "RackAdmissionFailed", "Current source, pitch, tangent geometry, grounding, law, active material segment, guide travel and independent requirements must all pass.", analysis.Diagnostics);
            }
            var written = RackPinionJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved);
            var reread = RackPinionJson.ReadArtifact(written.Bytes);
            if (reread.CandidateId != draft.DefinitionId || !RackPinionJson.VerifyIdentity(reread).ArtifactHashMatches)
                return Refuse(RackPinionFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "New rack artifact identity or readback failed.");
            return new(RackPinionFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (Exception e) when (IsAttachmentResourceRefusal(e))
        { return Refuse(RackPinionFinalizationStatus.ResourceLimitExceeded, "ResourceLimitExceeded", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
        { return Refuse(RackPinionFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", e.Message); }
    }
    private static RackPinionFinalizationStatus MapRackSourceStatus(MechanicalFinalizationStatus status) => status switch
    {
        MechanicalFinalizationStatus.Finalized => RackPinionFinalizationStatus.Finalized,
        MechanicalFinalizationStatus.InvalidMechanicalDefinition => RackPinionFinalizationStatus.InvalidMechanicalDefinition,
        MechanicalFinalizationStatus.UnresolvedRequiredValidation => RackPinionFinalizationStatus.UnresolvedRequiredValidation,
        MechanicalFinalizationStatus.UnsupportedByExportProfile => RackPinionFinalizationStatus.UnsupportedByExportProfile,
        MechanicalFinalizationStatus.UnresolvedSourceProvenance => RackPinionFinalizationStatus.UnresolvedSourceProvenance,
        MechanicalFinalizationStatus.TargetMismatch => RackPinionFinalizationStatus.TargetMismatch,
        _ => throw new ArgumentException("Unknown original source finalization status.")
    };
    public byte[] WriteRackPinionFinalizationResult(RackPinionFinalizationResult result) => RackPinionJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.Diagnostics);
    public void SaveRackPinionFinalizationResult(RackPinionFinalizationResult result, string path) => SaveMechanicalSidecar(WriteRackPinionFinalizationResult(result), path);
    public RackPinionArtifact ReadRackPinionArtifact(byte[] bytes)
    {
        var artifact = RackPinionJson.ReadArtifact(bytes); var validation = ValidateRackPinionArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Fresh rack source/request validation failed: " + validation.Detail); return artifact;
    }
    public byte[] WriteRackPinionArtifact(RackPinionArtifact artifact)
    {
        var validation = ValidateRackPinionArtifact(artifact);
        if (!validation.IsValid) throw new ArtifactFormatException("Invalid rack artifact: " + validation.Detail); return RackPinionJson.WriteArtifact(artifact);
    }
    public ArtifactIdentityVerification VerifyRackPinionIdentity(RackPinionArtifact artifact) => RackPinionJson.VerifyIdentity(artifact);
    public RackPinionArtifactValidationResult ValidateRackPinionArtifact(RackPinionArtifact artifact)
    {
        var identity = RackPinionJson.VerifyIdentity(artifact); var final = TryFinalizeRackPinionDraft(artifact.Request);
        var matches = final.IsFinalized && final.ArtifactBytes!.SequenceEqual(RackPinionJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete source finalization and current CP/tangent/material request correspondence." : "Current request-only finalization differs from stored artifact or is inadmissible.");
    }
    public RackPinionArtifactWriteResult RebuildRackPinionArtifact(RackPinionArtifact artifact)
    {
        var identity = VerifyRackPinionIdentity(artifact);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches) throw new ArtifactFormatException("Rack artifact identity mismatch.");
        var fresh = TryFinalizeRackPinionDraft(artifact.Request);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(RackPinionJson.WriteArtifact(artifact)))
            throw new ArtifactFormatException("Request-only source finalization and rack reconstruction did not reproduce the complete artifact; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveRackPinionFinalization(RackPinionFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null) throw new ArtifactFormatException("Unsuccessful rack finalization has no normal artifact.");
        var bytes = WriteRackPinionArtifact(result.Artifact);
        if (result.DefinitionId != result.Artifact.Request.DefinitionId) throw new ArtifactFormatException("Final definition identity mismatch.");
        SaveMechanicalSidecar(bytes, path);
    }
    public void SaveRackPinionArtifact(RackPinionArtifact artifact, string path) => SaveMechanicalSidecar(WriteRackPinionArtifact(artifact), path);
    public RackPinionArtifact LoadRackPinionArtifact(string path) => ReadRackPinionArtifact(PortableProjectStorage.ReadBounded(path, RackPinionJson.MaxDocumentBytes));
}
