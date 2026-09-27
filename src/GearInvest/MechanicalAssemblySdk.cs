using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum MechanicalAssemblyFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation, UnsupportedByExportProfile,
    UnresolvedSourceProvenance, TargetMismatch, ResourceLimitExceeded, IncompleteNumericBudget
}
public sealed class MechanicalAssemblyFinalizationResult
{
    internal MechanicalAssemblyFinalizationResult(MechanicalAssemblyFinalizationStatus status, string definitionId,
        IEnumerable<MechanicalDiagnostic> diagnostics, MechanicalAssemblyArtifactWriteResult? written = null)
    {
        if ((status == MechanicalAssemblyFinalizationStatus.Finalized) != (written is not null)) throw new ArgumentException("Only a finalized complete assembly contains an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly(); Written = written;
    }
    public MechanicalAssemblyFinalizationStatus Status { get; }
    public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public MechanicalAssemblyArtifactWriteResult? Written { get; }
    public MechanicalAssemblyArtifact? Artifact => Written?.Artifact;
    public string? ArtifactIdentity => Artifact?.ArtifactHash;
    public byte[]? ArtifactBytes => Written?.Bytes;
    public bool IsFinalized => Status == MechanicalAssemblyFinalizationStatus.Finalized;
}
public sealed class MechanicalAssemblyArtifactValidationResult
{
    internal MechanicalAssemblyArtifactValidationResult(ArtifactIdentityVerification identity, bool sourceAndRequestCorrespond, string detail)
    { Identity = identity; SourceAndRequestCorrespond = sourceAndRequestCorrespond; Detail = detail; }
    public ArtifactIdentityVerification Identity { get; }
    public bool SourceAndRequestCorrespond { get; }
    public string Detail { get; }
    public bool IsValid => Identity.CandidateIdMatches && Identity.ArtifactHashMatches && SourceAndRequestCorrespond;
}

public sealed partial class GearInvestSdk
{
    public MechanicalAssemblyDraft ComposeMechanicalAssemblyDraft(MechanicalAssemblyDefinition definition)
    { var draft = new MechanicalAssemblyDraft(definition); VerifyMechanicalAssemblyProvenance(draft); return draft; }
    public MechanicalAssemblyDraft ComposeMechanicalAssemblyDraft(MechanicalDraft root, SourceLengthMapping? rootMapping,
        IEnumerable<MechanicalAssemblyMember> members, IEnumerable<AssemblyOutputBinding>? outputs = null,
        IEnumerable<string>? requiredValidationDomains = null) => ComposeMechanicalAssemblyDraft(new MechanicalAssemblyDefinition(root, rootMapping, members, outputs, requiredValidationDomains));
    /// <summary>Explicit adoption of validated original 30A draft/artifact/session bytes. No read automatically upgrades a profile.</summary>
    public MechanicalAssemblyDraft UpgradeMechanicalAssemblyToGenevaSuffixProfile(byte[] originalDocumentBytes)
    {
        var (original, identity) = ReadOriginalAssemblyDocument(originalDocumentBytes);
        var d = original.Definition;
        var origin = new AssemblyProfileOrigin(originalDocumentBytes, original.DraftId, identity);
        var upgraded = original.WithDefinition(new(d.Root, d.RootMapping, d.Members, d.Outputs,
            d.RequiredValidationDomains, d.SeedImports, MechanicalAssemblyProfile.GenevaAffineSuffixId, origin));
        VerifyMechanicalAssemblyProvenance(upgraded); return upgraded;
    }
    public MechanicalAssemblyDraft UpgradeMechanicalAssemblyToGenevaSuffixProfile(string originalDocumentPath) =>
        UpgradeMechanicalAssemblyToGenevaSuffixProfile(PortableProjectStorage.ReadBounded(originalDocumentPath, MechanicalAssemblyProfile.MaxSeedBytes));
    public MechanicalAssemblyAnalysis AnalyzeMechanicalAssembly(MechanicalAssemblyDraft draft, AssemblyNumericRequest? numericRequest = null)
    { VerifyMechanicalAssemblyProvenance(draft); return MechanicalAssemblyAnalyzer.Analyze(draft, numericRequest); }
    public MechanicalAssemblyEvaluation EvaluateMechanicalAssembly(MechanicalAssemblyAnalysis analysis, ExactQuantity root, AssemblyNumericRequest? numericRequest = null)
    { VerifyMechanicalAssemblyProvenance(analysis.Draft); return MechanicalAssemblyAnalyzer.Evaluate(analysis, root, numericRequest); }
    public AssemblyBindingAnalysis QueryMechanicalAssemblyBinding(MechanicalAssemblyDraft draft, string instanceId, AssemblyNumericRequest? numericRequest = null) =>
        AnalyzeMechanicalAssembly(draft, numericRequest).Members.Single(x => x.InstanceId == instanceId).Binding;
    public MechanicalAssemblyEditResult ApplyMechanicalAssemblyEdits(MechanicalAssemblyDraft draft, MechanicalAssemblyEditBatch batch)
    { VerifyMechanicalAssemblyProvenance(draft); return MechanicalAssemblyEditor.Apply(draft, batch); }
    public MechanicalAssemblyEditResult AddMechanicalAssemblyMember(MechanicalAssemblyDraft draft, MechanicalAssemblyMember member) =>
        ApplyMechanicalAssemblyEdits(draft, new MechanicalAssemblyEditBatch(draft.Revision, draft.DefinitionId, new[] { new AddMechanicalAssemblyMemberEdit(member) }));
    public byte[] WriteMechanicalAssemblyDraft(MechanicalAssemblyDraft draft)
    { VerifyMechanicalAssemblyProvenance(draft); return MechanicalAssemblyJson.WriteDraft(draft); }
    public MechanicalAssemblyDraft ReadMechanicalAssemblyDraft(byte[] bytes)
    { var draft = MechanicalAssemblyJson.ReadDraft(bytes); VerifyMechanicalAssemblyProvenance(draft); return draft; }
    public byte[] WriteMechanicalAssemblyMemberDeclaration(AssemblyMemberDeclaration declaration) => MechanicalAssemblyJson.WriteMemberDeclaration(declaration);
    public AssemblyMemberDeclaration ReadMechanicalAssemblyMemberDeclaration(byte[] bytes) => MechanicalAssemblyJson.ReadMemberDeclaration(bytes);
    public byte[] WriteMechanicalAssemblyEditBatch(MechanicalAssemblyEditBatch batch) => MechanicalAssemblyJson.WriteBatch(batch);
    public MechanicalAssemblyEditBatch ReadMechanicalAssemblyEditBatch(byte[] bytes) => MechanicalAssemblyJson.ReadBatch(bytes);
    /// <summary>Bounded link-checked root batch loading for atomic assembly source edits.</summary>
    public MechanicalEditBatch LoadMechanicalEditBatch(string path) => ReadMechanicalEditBatch(PortableProjectStorage.ReadBounded(path, MechanicalAuthoringProfile.MaxDocumentBytes));
    public byte[] WriteMechanicalAssemblyEditResult(MechanicalAssemblyEditResult result) => MechanicalAssemblyJson.WriteEditResult(result);
    public byte[] WriteMechanicalAssemblyAnalysis(MechanicalAssemblyAnalysis analysis) => MechanicalAssemblyJson.WriteAnalysis(analysis);
    public byte[] WriteMechanicalAssemblyEvaluation(MechanicalAssemblyEvaluation evaluation) => MechanicalAssemblyJson.WriteEvaluation(evaluation);
    public MechanicalAssemblyEditSession CreateMechanicalAssemblyEditSession(MechanicalAssemblyDraft initial, IEnumerable<MechanicalAssemblyEditBatch> batches, AssemblyNumericRequest? numericRequest = null,
        IEnumerable<MechanicalAssemblyOutputComparisonRequest>? comparisonRequests = null)
    { VerifyMechanicalAssemblyProvenance(initial); return MechanicalAssemblyJson.CreateSession(initial, batches, numericRequest, comparisonRequests); }
    public byte[] WriteMechanicalAssemblyEditSession(MechanicalAssemblyEditSession session)
    { VerifyMechanicalAssemblyProvenance(session.InitialDraft); return MechanicalAssemblyJson.WriteSession(session); }
    public MechanicalAssemblyEditSession ReadMechanicalAssemblyEditSession(byte[] bytes)
    { var session = MechanicalAssemblyJson.ReadSession(bytes); VerifyMechanicalAssemblyProvenance(session.InitialDraft); VerifyMechanicalAssemblyProvenance(session.CurrentDraft); return session; }
    public MechanicalAssemblyDraft ReapplyMechanicalAssemblyEditSession(MechanicalAssemblyEditSession session) =>
        CreateMechanicalAssemblyEditSession(session.InitialDraft, session.Batches, session.NumericRequest, session.ComparisonRequests).CurrentDraft;
    public MechanicalAssemblyOutputComparisonResult CompareMechanicalAssemblyOutputs(MechanicalAssemblyAnalysis before, MechanicalAssemblyAnalysis after,
        MechanicalAssemblyOutputComparisonRequest request)
    { VerifyMechanicalAssemblyProvenance(before.Draft); VerifyMechanicalAssemblyProvenance(after.Draft); return MechanicalAssemblyAnalyzer.CompareOutputs(before, after, request); }
    public byte[] WriteMechanicalAssemblyOutputComparisonRequest(MechanicalAssemblyOutputComparisonRequest request) => MechanicalAssemblyJson.WriteComparisonRequest(request);
    public MechanicalAssemblyOutputComparisonRequest ReadMechanicalAssemblyOutputComparisonRequest(byte[] bytes) => MechanicalAssemblyJson.ReadComparisonRequest(bytes);
    public byte[] WriteMechanicalAssemblyOutputEquivalenceResult(MechanicalAssemblyOutputComparisonResult result) => MechanicalAssemblyJson.WriteComparison(result);
    public MechanicalAssemblyOutputComparisonResult VerifyMechanicalAssemblyOutputEquivalenceResult(MechanicalAssemblyAnalysis before, MechanicalAssemblyAnalysis after,
        MechanicalAssemblyOutputComparisonRequest request, byte[] bytes)
    { VerifyMechanicalAssemblyProvenance(before.Draft); VerifyMechanicalAssemblyProvenance(after.Draft); return MechanicalAssemblyJson.VerifyComparison(before, after, request, bytes); }
    public byte[] WriteMechanicalAssemblyFinalizationResult(MechanicalAssemblyFinalizationResult result) => MechanicalAssemblyJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.Artifact?.OriginalRootBytesPreserved ?? false, result.Diagnostics);

    /// <summary>Adopts one actual finalized worm seed. Existing-root admission requires complete current root and mapping equality.</summary>
    public MechanicalAssemblyDraft ImportWormDriveAssemblySeed(byte[] bytes, string instanceId = "worm", MechanicalAssemblyDraft? existing = null)
    {
        var artifact = ReadWormDriveArtifact(bytes); var d = artifact.Request.Definition;
        var member = new MechanicalAssemblyMember(instanceId, new AssemblyWormDeclaration(d.Device, d.OutputTerminal, d.Output, d.RequiredOutputPhase, d.RequiredValidationDomains),
            new UpstreamShaftBinding(AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, d.Device.InputShaftId), d.Device.InputPitchStation, d.Device.InputPortId));
        var seed = new AssemblySeedImport(instanceId, bytes, artifact.ArtifactHash, d.Source.DefinitionId, WormSeedCorrespondence(d, instanceId));
        if (existing is null) return new MechanicalAssemblyDraft(new MechanicalAssemblyDefinition(d.Source, d.SourceMapping, new[] { member },
            new[] { new AssemblyOutputBinding(d.Output.Key, AssemblyComponentReference.Member(instanceId, AssemblyComponentKind.Output, d.Output.Key)) }, seedImports: new[] { seed }));
        VerifyMechanicalAssemblyProvenance(existing);
        if (existing.Definition.Root.DraftId != d.Source.DraftId || existing.Definition.RootMapping?.CanonicalRepresentation != d.SourceMapping?.CanonicalRepresentation)
            throw new ArtifactFormatException("Seed import root/current provenance or explicit length mapping mismatch; multiple roots cannot be merged.");
        var current = existing.Definition;
        return existing.WithDefinition(new MechanicalAssemblyDefinition(current.Root, current.RootMapping, current.Members.Concat(new[] { member }),
            current.Outputs, current.RequiredValidationDomains, current.SeedImports.Concat(new[] { seed }), current.Profile, current.ProfileOrigin));
    }

    public MechanicalAssemblyFinalizationResult TryFinalizeMechanicalAssembly(MechanicalAssemblyDraft draft, AssemblyNumericRequest? numericRequest = null)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        MechanicalAssemblyFinalizationResult Refuse(MechanicalAssemblyFinalizationStatus status, string code, string detail,
            IEnumerable<MechanicalDiagnostic>? diagnostics = null) => new(status, draft.DefinitionId,
                diagnostics ?? new[] { new MechanicalDiagnostic(code, "AssemblyFinalization", detail: detail, scope: MechanicalAssemblyProfile.Id) });
        try
        {
            VerifyMechanicalAssemblyProvenance(draft);
            if (!MechanicalAssemblyProfile.IsSupported(draft.Definition.Profile)) return Refuse(MechanicalAssemblyFinalizationStatus.UnsupportedByExportProfile, "UnsupportedAssemblyProfile", "The selected assembly profile is unsupported.");
            var rootId = draft.Definition.Root.Definition.RootShaftId;
            if (rootId is null) return Refuse(MechanicalAssemblyFinalizationStatus.InvalidMechanicalDefinition, "MissingRootInput", "One actual prescribed root shaft is required.");
            // One complete live-root finalization/reimport; local members never become legacy source artifacts.
            var source = FinalizeAttachedMechanicalSource(draft.Definition.Root, rootId, null);
            if (source.Failure.HasValue)
            {
                var status = source.ResourceLimited ? MechanicalAssemblyFinalizationStatus.ResourceLimitExceeded : source.Failure.Value switch
                {
                    MechanicalFinalizationStatus.TargetMismatch => MechanicalAssemblyFinalizationStatus.TargetMismatch,
                    MechanicalFinalizationStatus.UnsupportedByExportProfile => MechanicalAssemblyFinalizationStatus.UnsupportedByExportProfile,
                    MechanicalFinalizationStatus.UnresolvedRequiredValidation => MechanicalAssemblyFinalizationStatus.UnresolvedRequiredValidation,
                    _ => MechanicalAssemblyFinalizationStatus.UnresolvedSourceProvenance
                };
                return Refuse(status, source.Code, source.Detail, source.Diagnostics);
            }
            var analysis = MechanicalAssemblyAnalyzer.Analyze(draft, numericRequest);
            if (!MechanicalAssemblyJson.IsReadyForReconstruction(analysis))
            {
                var target = analysis.Members.Any(x => x.Target == MechanicalAxisVerdict.Fail) || analysis.Outputs.Any(x => x.Target == MechanicalAxisVerdict.Fail);
                var numeric = analysis.Diagnostics.Any(x => x.Code.Contains("Numeric") || x.Code.Contains("Budget"));
                var unresolved = analysis.Checks.Any(x => x.Required && (x.Verdict == OrientedCheckVerdict.Inconclusive || x.Verdict == OrientedCheckVerdict.NotPerformed)) ||
                    analysis.Members.Any(x => !x.RequiredScopesSatisfied);
                return Refuse(target ? MechanicalAssemblyFinalizationStatus.TargetMismatch : numeric ? MechanicalAssemblyFinalizationStatus.IncompleteNumericBudget :
                    unresolved ? MechanicalAssemblyFinalizationStatus.UnresolvedRequiredValidation : MechanicalAssemblyFinalizationStatus.InvalidMechanicalDefinition,
                    "AssemblyAdmissionFailed", "Every declared member, binding, output and required scope must pass.",
                    analysis.Diagnostics.Select(x => new MechanicalDiagnostic(x.Code, x.Stage, detail: x.Detail)));
            }
            var reference = MechanicalAssemblyAnalyzer.Evaluate(analysis, ExactQuantity.Turns(0), analysis.NumericRequest);
            if (!reference.AllRequestedNumericAvailable) return Refuse(MechanicalAssemblyFinalizationStatus.IncompleteNumericBudget,
                "ReferenceEvaluationUnavailable", "The aggregate root-zero numerical evaluation is incomplete; no partial assembly artifact was produced.");
            var written = MechanicalAssemblyJson.WriteArtifact(draft, source.Bytes, source.Identity, source.OriginalPreserved, analysis, reference);
            return new MechanicalAssemblyFinalizationResult(MechanicalAssemblyFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written);
        }
        catch (ArtifactFormatException exception)
        {
            var resource = IsMechanicalAssemblyResourceRefusal(exception);
            return Refuse(resource ? MechanicalAssemblyFinalizationStatus.ResourceLimitExceeded : MechanicalAssemblyFinalizationStatus.UnresolvedSourceProvenance,
                resource ? "AssemblyResourceLimit" : "AssemblyReconstructionFailed", exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is DivideByZeroException)
        { return Refuse(MechanicalAssemblyFinalizationStatus.ResourceLimitExceeded, "AssemblyResourceLimit", exception.Message); }
    }
    public MechanicalAssemblyArtifact ReadMechanicalAssemblyArtifact(byte[] bytes)
    {
        var request = MechanicalAssemblyJson.ReadArtifactRequest(bytes); var fresh = TryFinalizeMechanicalAssembly(request.Draft, request.NumericRequest);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(bytes))
            throw new ArtifactFormatException("Fresh complete root, topology, member mechanics, numeric reference or canonical artifact reconstruction differs: " + fresh.Status);
        return fresh.Artifact!;
    }
    public byte[] WriteMechanicalAssemblyArtifact(MechanicalAssemblyArtifact artifact)
    {
        var fresh = TryFinalizeMechanicalAssembly(artifact.Request, artifact.NumericRequest);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(MechanicalAssemblyJson.WriteArtifact(artifact))) throw new ArtifactFormatException("Current complete assembly artifact reconstruction differs.");
        return fresh.ArtifactBytes!;
    }
    public ArtifactIdentityVerification VerifyMechanicalAssemblyIdentity(MechanicalAssemblyArtifact artifact) => MechanicalAssemblyJson.VerifyIdentity(artifact);
    public MechanicalAssemblyArtifactValidationResult ValidateMechanicalAssemblyArtifact(MechanicalAssemblyArtifact artifact)
    {
        var identity = MechanicalAssemblyJson.VerifyIdentity(artifact); var fresh = TryFinalizeMechanicalAssembly(artifact.Request, artifact.NumericRequest);
        var matches = fresh.IsFinalized && fresh.ArtifactBytes!.SequenceEqual(MechanicalAssemblyJson.WriteArtifact(artifact));
        return new(identity, matches, matches ? "Fresh complete root and every member/binding/reference correspond." : "Request-only assembly reconstruction failed or differs.");
    }
    public MechanicalAssemblyArtifactWriteResult RebuildMechanicalAssemblyArtifact(MechanicalAssemblyArtifact artifact)
    {
        var fresh = TryFinalizeMechanicalAssembly(artifact.Request, artifact.NumericRequest);
        if (!fresh.IsFinalized || !fresh.ArtifactBytes!.SequenceEqual(MechanicalAssemblyJson.WriteArtifact(artifact))) throw new ArtifactFormatException("Fresh request-only assembly rebuild differs; no replacement adopted.");
        return fresh.Written!;
    }
    public void SaveMechanicalAssemblyDraft(MechanicalAssemblyDraft draft, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyDraft(draft), path);
    public MechanicalAssemblyDraft LoadMechanicalAssemblyDraft(string path) => ReadMechanicalAssemblyDraft(PortableProjectStorage.ReadBounded(path, MechanicalAssemblyJson.MaxDocumentBytes));
    public void SaveMechanicalAssemblyEditSession(MechanicalAssemblyEditSession session, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyEditSession(session), path);
    public MechanicalAssemblyEditSession LoadMechanicalAssemblyEditSession(string path) => ReadMechanicalAssemblyEditSession(PortableProjectStorage.ReadBounded(path, MechanicalAssemblyJson.MaxDocumentBytes));
    public void SaveMechanicalAssemblyArtifact(MechanicalAssemblyArtifact artifact, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyArtifact(artifact), path);
    public MechanicalAssemblyArtifact LoadMechanicalAssemblyArtifact(string path) => ReadMechanicalAssemblyArtifact(PortableProjectStorage.ReadBounded(path, MechanicalAssemblyJson.MaxDocumentBytes));
    public void SaveMechanicalAssemblyFinalizationResult(MechanicalAssemblyFinalizationResult result, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyFinalizationResult(result), path);
    public void SaveMechanicalAssemblyEditBatch(MechanicalAssemblyEditBatch batch, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyEditBatch(batch), path);
    public MechanicalAssemblyEditBatch LoadMechanicalAssemblyEditBatch(string path) => ReadMechanicalAssemblyEditBatch(PortableProjectStorage.ReadBounded(path, MechanicalAssemblyJson.MaxDocumentBytes));
    public void SaveMechanicalAssemblyEditResult(MechanicalAssemblyEditResult result, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyEditResult(result), path);
    public void SaveMechanicalAssemblyMemberDeclaration(AssemblyMemberDeclaration declaration, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyMemberDeclaration(declaration), path);
    public AssemblyMemberDeclaration LoadMechanicalAssemblyMemberDeclaration(string path) => ReadMechanicalAssemblyMemberDeclaration(PortableProjectStorage.ReadBounded(path, MechanicalAssemblyJson.MaxDocumentBytes));
    public void SaveMechanicalAssemblyAnalysis(MechanicalAssemblyAnalysis analysis, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyAnalysis(analysis), path);
    public void SaveMechanicalAssemblyEvaluation(MechanicalAssemblyEvaluation evaluation, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyEvaluation(evaluation), path);
    public void SaveMechanicalAssemblyOutputComparisonRequest(MechanicalAssemblyOutputComparisonRequest request, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyOutputComparisonRequest(request), path);
    public MechanicalAssemblyOutputComparisonRequest LoadMechanicalAssemblyOutputComparisonRequest(string path) => ReadMechanicalAssemblyOutputComparisonRequest(PortableProjectStorage.ReadBounded(path, MechanicalAssemblyJson.MaxDocumentBytes));
    public void SaveMechanicalAssemblyOutputEquivalenceResult(MechanicalAssemblyOutputComparisonResult result, string path) => SaveMechanicalSidecar(WriteMechanicalAssemblyOutputEquivalenceResult(result), path);
    public void SaveMechanicalAssemblyFinalization(MechanicalAssemblyFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.Artifact is null || result.DefinitionId != result.Artifact.CandidateId) throw new ArtifactFormatException("Only the complete finalized assembly may be saved as a normal artifact.");
        SaveMechanicalAssemblyArtifact(result.Artifact, path);
    }
    private void VerifyMechanicalAssemblyProvenance(MechanicalAssemblyDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft)); VerifyMechanicalImportProvenance(draft.Definition.Root);
        if (draft.Definition.ProfileOrigin is not null)
        {
            var origin = draft.Definition.ProfileOrigin;
            var (original, identity) = ReadOriginalAssemblyDocument(origin.OriginalDocumentBytes);
            if (original.DraftId != origin.OriginalDraftId || identity != origin.OriginalDocumentIdentity ||
                original.Definition.Root.OriginalArtifactIdentity != draft.Definition.Root.OriginalArtifactIdentity ||
                original.Definition.Root.OriginalDefinitionId != draft.Definition.Root.OriginalDefinitionId)
                throw new ArtifactFormatException("Assembly profile origin bytes, identity or retained root provenance mismatch.");
        }
        foreach (var seed in draft.Definition.SeedImports)
        {
            var original = ReadWormDriveArtifact(seed.OriginalArtifactBytes); var source = original.Request.Definition.Source;
            if (original.ArtifactHash != seed.OriginalArtifactIdentity || source.DefinitionId != seed.OriginalRootDefinitionId ||
                source.OriginalArtifactIdentity != draft.Definition.Root.OriginalArtifactIdentity || source.OriginalDefinitionId != draft.Definition.Root.OriginalDefinitionId ||
                !WormSeedCorrespondence(original.Request.Definition, seed.InstanceId).Select(x => x.CanonicalRepresentation).OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(seed.Correspondence.Select(x => x.CanonicalRepresentation)))
                throw new ArtifactFormatException("Assembly seed bytes, complete root import provenance or explicit original identity correspondence mismatch.");
        }
    }
    private (MechanicalAssemblyDraft Draft, string Identity) ReadOriginalAssemblyDocument(byte[] bytes)
    {
        // Inspect the old wire boundary before invoking readers, preventing nested new-profile provenance.
        var format = MechanicalAssemblyJson.ReadLegacyOriginFormat(bytes);
        if (format == MechanicalAssemblyArtifact.Format)
        { var artifact = ReadMechanicalAssemblyArtifact(bytes); return (artifact.Request, artifact.ArtifactHash); }
        if (format == MechanicalAssemblyJson.SessionFormat)
        { var session = ReadMechanicalAssemblyEditSession(bytes); return (session.CurrentDraft, session.SessionId); }
        var draft = ReadMechanicalAssemblyDraft(bytes); return (draft, draft.DraftId);
    }
    private static bool IsMechanicalAssemblyResourceRefusal(Exception exception)
    {
        if (IsAttachmentResourceRefusal(exception)) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current.Message is "Assembly document byte bound exceeded." or "Assembly JSON node bound exceeded." or
                "Assembly JSON pending node bound exceeded." or "Assembly seed encoded byte bound exceeded." or
                "Aggregate assembly seed byte bound exceeded." or "Assembly root original byte bound exceeded." or
                "Bounded finalized root bytes and identity must be supplied together.") return true;
        return false;
    }
    private static IEnumerable<AssemblyImportCorrespondence> WormSeedCorrespondence(WormDriveDefinition definition, string memberId)
    {
        AssemblyImportCorrespondence Root(string kind, AssemblyComponentKind targetKind, string id) => new(new MechanicalReference(kind, id), AssemblyComponentReference.Root(targetKind, id));
        AssemblyImportCorrespondence Member(string kind, AssemblyComponentKind targetKind, string id) => new(new MechanicalReference(kind, id), AssemblyComponentReference.Member(memberId, targetKind, id));
        var source = definition.Source.Definition;
        foreach (var item in source.Shafts) yield return Root("Shaft", AssemblyComponentKind.Shaft, item.Id);
        foreach (var item in source.Bodies) yield return Root("Body", AssemblyComponentKind.Body, item.Id);
        foreach (var item in source.Ports) yield return Root("Port", AssemblyComponentKind.Port, item.Id);
        foreach (var item in source.Contacts) yield return Root("Contact", AssemblyComponentKind.Constraint, item.Id);
        foreach (var item in source.Connections) yield return Root("Connection", AssemblyComponentKind.Constraint, item.Id);
        foreach (var item in source.Outputs) yield return Root("Output", AssemblyComponentKind.Output, item.Key);
        yield return Member("Shaft", AssemblyComponentKind.Shaft, definition.Device.OutputShaft.Id);
        yield return Member("Body", AssemblyComponentKind.Body, definition.Device.InputWormBodyId);
        yield return Member("Body", AssemblyComponentKind.Body, definition.Device.OutputWheelBodyId);
        yield return Member("Port", AssemblyComponentKind.Port, definition.OutputTerminal.Id);
        yield return Member("Output", AssemblyComponentKind.Output, definition.Output.Key);
        yield return Member("Device", AssemblyComponentKind.Constraint, definition.Device.Id);
    }
}
