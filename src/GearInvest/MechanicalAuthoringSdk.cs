using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum MechanicalFinalizationStatus
{
    Finalized, InvalidMechanicalDefinition, UnresolvedRequiredValidation,
    UnsupportedByExportProfile, UnresolvedSourceProvenance, TargetMismatch
}

/// <summary>A final artifact is present only after current definition, source context and the existing profile validator agree.</summary>
public sealed class MechanicalFinalizationResult
{
    private readonly byte[]? artifactBytes;
    public MechanicalFinalizationResult(MechanicalFinalizationStatus status, string definitionId,
        IEnumerable<MechanicalDiagnostic> diagnostics, byte[]? artifactBytes = null, string? artifactIdentity = null,
        bool originalBytesPreserved = false)
    {
        if ((status == MechanicalFinalizationStatus.Finalized) != (artifactBytes is not null && artifactIdentity is not null))
            throw new ArgumentException("Only finalized results contain an artifact.");
        Status = status; DefinitionId = definitionId; Diagnostics = diagnostics.ToList().AsReadOnly();
        this.artifactBytes = artifactBytes is null ? null : (byte[])artifactBytes.Clone(); ArtifactIdentity = artifactIdentity; OriginalBytesPreserved = originalBytesPreserved;
    }
    public MechanicalFinalizationStatus Status { get; }
    public string DefinitionId { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public byte[]? ArtifactBytes => artifactBytes is null ? null : (byte[])artifactBytes.Clone();
    public string? ArtifactIdentity { get; }
    public bool OriginalBytesPreserved { get; }
    public bool IsFinalized => Status == MechanicalFinalizationStatus.Finalized;
}

public sealed partial class GearInvestSdk
{
    /// <summary>Imports a validated supported artifact. Historic solved channels and certificates are not copied into the editable definition.</summary>
    public MechanicalDraft ImportMechanicalArtifact(byte[] bytes)
    {
        if (bytes is null || bytes.Length > 4 * 1024 * 1024) throw new ArtifactFormatException("Bounded original mechanical artifact required.");
        string format;
        try { using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 40 }); format = doc.RootElement.GetProperty("format").GetString()!; }
        catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException) { throw new ArtifactFormatException("Malformed artifact header.", e); }
        if (format == ParallelCoaxialArtifactJson.Format)
        {
            var artifact = ParallelCoaxialArtifactJson.Read(bytes);
            return new MechanicalDraft(artifact.Request, originalArtifactBytes: bytes, originalArtifactIdentity: artifact.ArtifactHash,
                originalDefinitionId: artifact.DefinitionId, importedProfile: ParallelCoaxialLayout.Profile);
        }
        if (format == MechanismArtifact.ExpectedFormat)
        {
            var artifact = ReadArtifact(bytes);
            if (!Validate(artifact).IsValid || !WriteArtifact(artifact).Bytes.SequenceEqual(bytes)) throw new ArtifactFormatException("Invalid or noncanonical original planar artifact.");
            var definition = ImportPlanarArtifactDefinition(artifact);
            return new MechanicalDraft(definition, originalArtifactBytes: bytes, originalArtifactIdentity: artifact.ArtifactHash,
                originalDefinitionId: definition.DefinitionId, importedProfile: MechanicalAuthoringProfile.PlanarExport);
        }
        if (format == OrientedTwoOutputArtifact.Format)
        {
            var artifact = ReadOrientedTwoOutputArtifact(bytes);
            if (!ValidateOrientedTwoOutputArtifact(artifact).IsValid) throw new ArtifactFormatException("Invalid original two-output identity, sources, geometry or request context.");
            var definition = ImportOrientedDefinition(artifact.Mechanism, artifact.Request);
            return new MechanicalDraft(definition, originalArtifactBytes: bytes, originalArtifactIdentity: artifact.ArtifactHash,
                originalDefinitionId: definition.DefinitionId, importedProfile: artifact.Request.Profile);
        }
        throw new ArtifactFormatException("Unsupported mechanical import format. Only single-layer planar external chains and oriented two-output 0.1/0.2 are supported.");
    }

    public byte[] WriteMechanicalDraft(MechanicalDraft draft)
    { VerifyMechanicalImportProvenance(draft); return MechanicalAuthoringJson.WriteDraft(draft); }
    public MechanicalDraft ReadMechanicalDraft(byte[] bytes)
    {
        var draft = MechanicalAuthoringJson.ReadDraft(bytes); VerifyMechanicalImportProvenance(draft); return draft;
    }
    public MechanicalEditResult ApplyMechanicalEdits(MechanicalDraft draft, MechanicalEditBatch batch) => MechanicalEditor.Apply(draft, batch);
    public byte[] WriteMechanicalEditBatch(MechanicalEditBatch batch) => MechanicalAuthoringJson.WriteBatch(batch);
    public MechanicalEditBatch ReadMechanicalEditBatch(byte[] bytes) => MechanicalAuthoringJson.ReadBatch(bytes);
    public byte[] WriteMechanicalEditResult(MechanicalEditResult result) => MechanicalAuthoringJson.WriteEditResult(result);
    public MechanicalAnalysis AnalyzeMechanicalDraft(MechanicalDraft draft) => MechanicalAnalyzer.Analyze(draft);
    public ReadOnlyCollection<OrientedShaftEvaluation> EvaluateMechanicalAnalysis(MechanicalAnalysis analysis, Rational inputTurns) => MechanicalAnalyzer.Evaluate(analysis, inputTurns);
    public byte[] WriteMechanicalAnalysis(MechanicalAnalysis analysis) => MechanicalAuthoringJson.WriteAnalysis(analysis);
    public MechanicalAnalysis VerifyMechanicalAnalysis(MechanicalDraft draft, byte[] bytes) => MechanicalAuthoringJson.VerifyAnalysis(draft, bytes);
    public OutputEquivalenceResult CompareOutputMotion(MechanicalAnalysis before, MechanicalAnalysis after, OutputComparisonRequest request) => MechanicalOutputComparer.Compare(before, after, request);
    public OutputEquivalenceResult CompareAffineOutputRelations(ExactAffineRelation before, ExactAffineRelation after, OutputComparisonRequest request) => MechanicalOutputComparer.CompareAffineRelations(before, after, request);
    public byte[] WriteOutputComparisonRequest(OutputComparisonRequest request) => MechanicalAuthoringJson.WriteComparisonRequest(request);
    public OutputComparisonRequest ReadOutputComparisonRequest(byte[] bytes) => MechanicalAuthoringJson.ReadComparisonRequest(bytes);
    public byte[] WriteOutputEquivalenceResult(OutputEquivalenceResult result) => MechanicalAuthoringJson.WriteComparison(result);
    public ConnectionCompatibilityResult QueryConnectionCompatibility(MechanicalDraft draft, MechanicalContact contact) => MechanicalConnectionQuery.QueryContact(draft.Definition, contact);
    public ConnectionCompatibilityResult QueryConnectionCompatibility(MechanicalDraft draft, ShaftPortConnection connection) => MechanicalConnectionQuery.QueryPortConnection(draft.Definition, connection);
    public byte[] WriteConnectionCompatibilityResult(ConnectionCompatibilityResult result) => MechanicalAuthoringJson.WriteConnection(result);
    public byte[] WriteMechanicalFinalizationResult(MechanicalFinalizationResult result) => MechanicalAuthoringJson.WriteFinalization(result.Status.ToString(), result.DefinitionId,
        result.ArtifactIdentity, result.ArtifactBytes is null ? null : CanonicalOrientedJson.Hash(result.ArtifactBytes), result.OriginalBytesPreserved, result.Diagnostics);
    public MechanicalEditSession CreateMechanicalEditSession(MechanicalDraft initial, IEnumerable<MechanicalEditBatch> batches, IEnumerable<OutputComparisonRequest>? comparisons = null)
    { VerifyMechanicalImportProvenance(initial); return MechanicalAuthoringJson.CreateSession(initial, batches, comparisons); }
    public byte[] WriteMechanicalEditSession(MechanicalEditSession session)
    { VerifyMechanicalImportProvenance(session.InitialDraft); return MechanicalAuthoringJson.WriteSession(session); }
    public MechanicalEditSession ReadMechanicalEditSession(byte[] bytes)
    { var session = MechanicalAuthoringJson.ReadSession(bytes); VerifyMechanicalImportProvenance(session.InitialDraft); return session; }
    public MechanicalDraft ReapplyMechanicalEditSession(MechanicalEditSession session) => CreateMechanicalEditSession(session.InitialDraft, session.Batches, session.ComparisonRequests).CurrentDraft;
    public void SaveMechanicalDraft(MechanicalDraft draft, string path) => SaveMechanicalSidecar(WriteMechanicalDraft(draft), path);
    public MechanicalDraft LoadMechanicalDraft(string path) => ReadMechanicalDraft(PortableProjectStorage.ReadBounded(path, MechanicalAuthoringProfile.MaxDocumentBytes));
    public void SaveMechanicalEditSession(MechanicalEditSession session, string path) => SaveMechanicalSidecar(WriteMechanicalEditSession(session), path);
    public MechanicalEditSession LoadMechanicalEditSession(string path) => ReadMechanicalEditSession(PortableProjectStorage.ReadBounded(path, MechanicalAuthoringProfile.MaxDocumentBytes));
    public void SaveMechanicalFinalization(MechanicalFinalizationResult result, string path)
    {
        if (!result.IsFinalized || result.ArtifactBytes is null) throw new ArtifactFormatException("An unsuccessful finalization has no saveable artifact.");
        var imported = ImportMechanicalArtifact(result.ArtifactBytes);
        if (imported.DefinitionId != result.DefinitionId || imported.OriginalArtifactIdentity != result.ArtifactIdentity)
            throw new ArtifactFormatException("Final artifact bytes do not match the declared current definition and identity.");
        SaveMechanicalSidecar(result.ArtifactBytes, path);
    }
    private static void SaveMechanicalSidecar(byte[] bytes, string path)
    {
        var full = Path.GetFullPath(path); PortableProjectStorage.RejectLinks(full);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Save As destination already exists.");
        var parent = Path.GetDirectoryName(full)!; PortableProjectStorage.RejectLinks(parent); Directory.CreateDirectory(parent);
        var pending = full + ".pending-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
        if (!PortableProjectStorage.ReadBounded(pending, bytes.Length).SequenceEqual(bytes)) throw new IOException("Mechanical staged write readback mismatch.");
        PortableProjectStorage.RejectLinks(full); File.Move(pending, full);
        if (!PortableProjectStorage.ReadBounded(full, bytes.Length).SequenceEqual(bytes)) throw new IOException("Mechanical saved readback mismatch.");
    }

    /// <summary>No profile widening, stale proof reuse, topology pruning or implicit requirement change.</summary>
    public MechanicalFinalizationResult TryFinalizeMechanicalDraft(MechanicalDraft draft, string exportProfile)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        if (exportProfile == ParallelCoaxialLayout.Profile) return FinalizeParallelCoaxialSource(draft);
        if (draft.Definition.CoaxialLayout is not null) return new MechanicalFinalizationResult(MechanicalFinalizationStatus.UnsupportedByExportProfile,
            draft.DefinitionId, new[] { new MechanicalDiagnostic("ExplicitCoaxialProfileRequired", "Finalization") });
        MechanicalFinalizationResult Refuse(MechanicalFinalizationStatus status, string code, string detail,
            IEnumerable<MechanicalDiagnostic>? diagnostics = null) => new(status, draft.DefinitionId, diagnostics ?? new[]
            { new MechanicalDiagnostic(code, "Finalization", detail: detail, scope: "SelectedExportProfile") });
        if (exportProfile != MechanicalAuthoringProfile.PlanarExport && !OrientedTwoOutputProfile.IsSupported(exportProfile))
            return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ProfileUnsupported", "An existing explicitly supported export profile is required.");
        if (exportProfile == MechanicalAuthoringProfile.PlanarExport && !draft.Definition.RequireCrossComponentClearance)
            return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "RequiredPlanarClearanceCannotBeDisabled", "The existing planar profile always requires unrelated-body nonpenetration; optional clearance cannot be silently exported as that profile.");
        MechanicalDraft? original;
        try { original = VerifyMechanicalImportProvenance(draft); }
        catch (ArtifactFormatException e) { return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "SourceProvenanceMismatch", e.Message); }
        var gate = MechanicalFinalizationGate(draft);
        if (gate is not null) return gate;
        try
        {
            if (original is not null && draft.DefinitionId == original.DefinitionId && exportProfile == original.ImportedProfile)
            {
                var bytes = original.OriginalArtifactBytes!;
                if (exportProfile == MechanicalAuthoringProfile.PlanarExport)
                {
                    var artifact = ReadArtifact(bytes); var fresh = Generate(new LowLevelMechanicalSpecification(artifact.Candidate.SourceId, artifact.Candidate.Kinematic, artifact.Candidate.Spatial));
                    if (!fresh.IsSuccess || !WriteArtifact(fresh.Candidates.Single(), artifact.Metadata).Bytes.SequenceEqual(bytes))
                        return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "Original low-level source reconstruction did not preserve original bytes.");
                }
                else if (!RebuildOrientedTwoOutput(ReadOrientedTwoOutputArtifact(bytes)).Bytes.SequenceEqual(bytes))
                    return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "Original request-only reconstruction differs.");
                return new(MechanicalFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), bytes, original.OriginalArtifactIdentity, true);
            }
            if (exportProfile == MechanicalAuthoringProfile.PlanarExport)
            {
                if (original is null || original.ImportedProfile != MechanicalAuthoringProfile.PlanarExport)
                    return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ProfileUnsupported", "Planar finalization currently requires an imported supported chain namespace.");
                var source = ReadArtifact(original.OriginalArtifactBytes!);
                var frame = OrientedFrame.Identity.At(new ExactVector3(0, 0, source.Candidate.Spatial.Bodies[0].Layer));
                var bytes = RewriteMechanicalPlanarSource(source, draft.Definition, "", frame);
                var current = ImportMechanicalArtifact(bytes);
                if (current.DefinitionId != draft.DefinitionId)
                    return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ProfileDefinitionMismatch", "The planar artifact profile cannot retain the complete edited ports, targets, policy or topology.");
                return new(MechanicalFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), bytes, current.OriginalArtifactIdentity);
            }
            if (original is null || !OrientedTwoOutputProfile.IsSupported(original.ImportedProfile ?? ""))
                return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "SourceProvenanceUnavailable", "Oriented source rewrite requires imported explicit source mappings.");
            if (draft.Definition.ClearancePolicy != OrientedTwoOutputProfile.ClearancePolicy(exportProfile))
                return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ClearancePolicyProfileMismatch", "The selected artifact profile must exactly preserve the explicitly edited clearance policy.");
            var previous = ReadOrientedTwoOutputArtifact(original.OriginalArtifactBytes!);
            var request = RewriteMechanicalTwoOutputRequest(previous, draft.Definition, exportProfile);
            var composed = ComposeOrientedTwoOutput(request);
            if (!composed.IsSuccess)
            {
                var unresolved = composed.Validation.Checks.Any(c => c.Required && (c.Verdict == OrientedCheckVerdict.Inconclusive || c.Verdict == OrientedCheckVerdict.NotPerformed));
                return Refuse(unresolved ? MechanicalFinalizationStatus.UnresolvedRequiredValidation : MechanicalFinalizationStatus.InvalidMechanicalDefinition,
                    unresolved ? "UnresolvedRequiredValidation" : "ExistingComposerRejected", "The existing source-aware composer did not admit the complete edited definition.",
                    composed.Validation.Checks.Where(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass).Select(c =>
                        new MechanicalDiagnostic("ExistingProfileValidationFailed", c.Domain, related: new[] { new MechanicalReference("validation", c.Subject) }, detail: c.Detail)));
            }
            if (ImportOrientedDefinition(composed.Mechanism!, request).DefinitionId != draft.DefinitionId)
                return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ProfileDefinitionMismatch", "Reconstruction does not contain exactly the entire current definition; no subset or stale source adopted.");
            var written = WriteOrientedTwoOutputArtifact(composed.Mechanism!, request);
            var reread = ReadOrientedTwoOutputArtifact(written.Bytes);
            if (!ValidateOrientedTwoOutputArtifact(reread).IsValid || !RebuildOrientedTwoOutput(reread).Bytes.SequenceEqual(written.Bytes))
                return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "FreshRebuildMismatch", "Final reader, validator or request-only rebuild rejected the newly written source context.");
            return new(MechanicalFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), written.Bytes, written.Artifact.ArtifactHash);
        }
        catch (MechanicalProfileException e) { return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ProfileUnsupported", e.Message); }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException || e is DivideByZeroException)
        { return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "SourceReconstructionFailed", e.Message); }
    }

    private MechanicalDraft? VerifyMechanicalImportProvenance(MechanicalDraft draft)
    {
        if (draft.OriginalArtifactBytes is null)
        {
            if (draft.OriginalArtifactIdentity is not null || draft.OriginalDefinitionId is not null || draft.ImportedProfile is not null)
                throw new ArtifactFormatException("Incomplete original artifact provenance.");
            return null;
        }
        var imported = ImportMechanicalArtifact(draft.OriginalArtifactBytes);
        if (imported.OriginalArtifactIdentity != draft.OriginalArtifactIdentity || imported.DefinitionId != draft.OriginalDefinitionId || imported.ImportedProfile != draft.ImportedProfile)
            throw new ArtifactFormatException("Stored original artifact identity/profile/definition does not correspond to actual original bytes.");
        return imported;
    }

    private static MechanicalFinalizationResult? MechanicalFinalizationGate(MechanicalDraft draft)
    {
        var analysis = MechanicalAnalyzer.Analyze(draft);
        if (analysis.Outputs.Any(o => o.HasDeterminedMotion && o.Target == MechanicalAxisVerdict.Fail))
            return new MechanicalFinalizationResult(MechanicalFinalizationStatus.TargetMismatch, draft.DefinitionId,
                analysis.Diagnostics.Where(d => d.Code == "TargetMismatch"));
        if (analysis.IsMechanicallyValid) return null;
        var unresolved = analysis.ReferenceIntegrity == MechanicalAxisVerdict.Pass && analysis.ConstraintAdmission == MechanicalAxisVerdict.Pass &&
            analysis.Targets == MechanicalAxisVerdict.Pass && analysis.Outputs.All(o => o.HasDeterminedMotion) &&
            analysis.AdmittedComponents.All(c => c.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) &&
            analysis.Geometry is MechanicalAxisVerdict.Inconclusive or MechanicalAxisVerdict.NotAssessed;
        return new MechanicalFinalizationResult(unresolved ? MechanicalFinalizationStatus.UnresolvedRequiredValidation : MechanicalFinalizationStatus.InvalidMechanicalDefinition,
            draft.DefinitionId, analysis.Diagnostics);
    }

    private static MechanicalDefinition ImportOrientedDefinition(OrientedTwoOutputMechanism m, OrientedTwoOutputAssemblyRequest request) => new(
        m.RootShaftId, m.Shafts, m.Bodies, m.Contacts.Select(c => new MechanicalContact(c.Id, c.Kind, c.BodyAId, c.BodyBId, c.Cone)), m.Ports, m.Connections,
        m.Outputs.Select(o => new MechanicalOutput(o.Key, o.ShaftId, o.BodyId, o.PortId, request.Outputs.Single(r => r.Key == o.Key).RequestedTransfer, o.Role)),
        m.KeepOuts, OrientedTwoOutputProfile.ClearancePolicy(m.Profile), m.RequireCrossComponentClearance);

    private static MechanicalDefinition ImportPlanarDefinition(GenerationCandidate c, Rational? requiredTransfer = null,
        string clearancePolicy = MechanicalAuthoringProfile.PlanarClearance, bool requireCrossComponentClearance = true)
    {
        var k = c.Kinematic; var s = c.Spatial;
        void Need(bool condition) { if (!condition) throw new ArtifactFormatException("Unsupported planar import: one layer, one body per shaft and a zero-phase external serial chain are required."); }
        Need(k.Dofs.Count >= 2 && k.Dofs.Count <= MechanicalAuthoringProfile.MaxShafts && s.Bodies.Count == k.Dofs.Count && s.Axes.Count == k.Dofs.Count &&
            s.Bodies.Select(b => b.DofId).Distinct(StringComparer.Ordinal).Count() == k.Dofs.Count && s.Bodies.Select(b => b.AxisId).Distinct(StringComparer.Ordinal).Count() == k.Dofs.Count &&
            s.Bodies.Select(b => b.Layer).Distinct().Count() == 1 && s.Bodies.All(b => b.Kind == SpatialBodyKind.Gear && b.ExactMountingPhase == 0) &&
            k.Couplings.All(e => e.PhaseOffset == 0) && k.Couplings.Count == k.Dofs.Count - 1 && s.Contacts.Count == k.Couplings.Count &&
            s.Contacts.All(e => e.Kind == SpatialContactKind.ExternalGearMesh) && k.Dofs.Count(d => d.IsPrescribed) == 1 && k.Dofs.Single(d => d.IsPrescribed).Id == k.RootDofId);
        var degrees = k.Dofs.ToDictionary(d => d.Id, d => k.Couplings.Count(e => e.DriverDofId == d.Id || e.DrivenDofId == d.Id), StringComparer.Ordinal);
        Need(degrees[k.RootDofId] == 1 && degrees.Values.Count(n => n == 1) == 2 && degrees.Values.All(n => n is 1 or 2));
        var outputId = degrees.Single(p => p.Key != k.RootDofId && p.Value == 1).Key;
        OrientedFrame FrameOf(SpatialBody b) { var a = s.Axes.Single(a => a.Id == b.AxisId); return OrientedFrame.Identity.At(new ExactVector3(new Rational(a.X), new Rational(a.Y), b.Layer)); }
        var bodies = s.Bodies.Select(b => new OrientedGearBody(b.Id, b.DofId, OrientedGearKind.PlanarSpur, FrameOf(b), b.ToothCount, new Rational(b.PitchRadius), "planar")).ToArray();
        var terminal = bodies.Single(b => b.ShaftId == outputId); var port = new ShaftPort("authoring/output", outputId, terminal.MountingFrame);
        return new(k.RootDofId, k.Dofs.Select(d => new OrientedShaft(d.Id, bodies.Single(b => b.ShaftId == d.Id).MountingFrame, d.IsPrescribed)), bodies,
            s.Contacts.Select(e => new MechanicalContact(e.Id, OrientedContactKind.ExternalSpur, e.BodyAId, e.BodyBId)), new[] { port },
            outputs: new[] { new MechanicalOutput("output", outputId, terminal.Id, port.Id, requiredTransfer) },
            clearancePolicy: clearancePolicy, requireCrossComponentClearance: requireCrossComponentClearance);
    }

    private static MechanicalDefinition ImportPlanarArtifactDefinition(MechanismArtifact artifact)
    {
        // A serial mechanical shape is insufficient when its typed provenance owns more than one semantic relation.
        if (artifact.Metadata.Composed is not null || artifact.Metadata.BranchedComposed is not null)
            throw new ArtifactFormatException("Unsupported planar authoring import: composed semantic bindings require a multi-requirement adapter; they cannot be dropped into one synthetic terminal.");
        var generated = artifact.Metadata.Generated;
        if (generated is not null && generated.Provenance.GenerationRequest.LayoutRequest.ClearanceTicks != 0)
            throw new ArtifactFormatException("Unsupported planar authoring import: the original generated request has nonzero clearance; the current editable planar policy preserves zero-margin nonpenetration only.");
        if (artifact.Metadata.Generator.GeneratorVersion == "mechanical-authoring-v1")
        {
            var context = ReadPlanarAuthoringContext(artifact.Metadata.Source.GenerationOptions);
            if (!context.RequireCrossComponentClearance) throw new ArtifactFormatException("Planar authoring context cannot disable the existing profile's required nonpenetration validation.");
            var definition = ImportPlanarDefinition(artifact.Candidate, context.RequiredTransfer, context.ClearancePolicy, context.RequireCrossComponentClearance);
            var output = definition.Outputs.Single();
            if (definition.DefinitionId != context.DefinitionId || output.ShaftId != context.TerminalDofId ||
                artifact.Metadata.Source.SpecificationId != "mechanical-authoring/" + artifact.CandidateId ||
                !artifact.Candidate.Solution.TryGetState(output.ShaftId!, out var solved) ||
                context.RequiredTransfer.HasValue && solved!.Coefficient != context.RequiredTransfer.Value)
                throw new ArtifactFormatException("Planar authoring context does not match the actual current complete definition and solved target.");
            // This proof is deliberately fresh: a context hash alone is never a geometry/requirement verdict.
            if (!MechanicalAnalyzer.Analyze(new MechanicalDraft(definition)).IsMechanicallyValid)
                throw new ArtifactFormatException("Planar authoring source context failed current target or geometric policy validation.");
            return definition;
        }
        return ImportPlanarDefinition(artifact.Candidate, generated?.Provenance.GenerationRequest.SynthesisRequest.TargetTransfer);
    }

    private byte[] RewriteMechanicalPlanarSource(MechanismArtifact original, MechanicalDefinition d, string prefix, OrientedFrame worldPose)
    {
        var source = original.Candidate; var currentBodies = source.Spatial.Bodies.Select(b => d.Bodies.SingleOrDefault(x => x.Id == prefix + b.Id)).ToArray();
        if (currentBodies.Any(b => b is null)) throw new MechanicalProfileException("An imported source body is missing; no source subset is synthesized.");
        var axes = new List<SpatialAxis>(); var bodies = new List<SpatialBody>();
        foreach (var old in source.Spatial.Bodies)
        {
            var b = currentBodies.Single(x => x!.Id == prefix + old.Id)!;
            var p = InversePoint(worldPose, b.MountingFrame.Origin);
            if (b.Kind != OrientedGearKind.PlanarSpur || b.MountingFrame.X != worldPose.X || b.MountingFrame.Y != worldPose.Y || b.MountingFrame.Z != worldPose.Z || p.Z != 0)
                throw new MechanicalProfileException("Source rewrite supports its existing common planar basis, not per-body arbitrary spatial lifting.");
            axes.Add(new SpatialAxis(old.AxisId, Integral(p.X), Integral(p.Y)));
            bodies.Add(new SpatialBody(old.Id, SpatialBodyKind.Gear, old.AxisId, old.DofId, old.Layer, b.Teeth, Integral(b.OuterPitchRadius)));
        }
        var contacts = new List<SpatialContact>(); var couplings = new List<ExternalGearCoupling>();
        foreach (var old in source.Spatial.Contacts)
        {
            var c = d.Contacts.SingleOrDefault(c => c.Id == prefix + old.Id);
            if (c is null || c.Kind != OrientedContactKind.ExternalSpur || c.Cone is not null || !c.BodyAId.StartsWith(prefix, StringComparison.Ordinal) || !c.BodyBId.StartsWith(prefix, StringComparison.Ordinal))
                throw new MechanicalProfileException("The complete original source contact mapping is required.");
            var a = bodies.SingleOrDefault(b => prefix + b.Id == c.BodyAId); var b = bodies.SingleOrDefault(b => prefix + b.Id == c.BodyBId);
            if (a is null || b is null) throw new MechanicalProfileException("A rewritten source contact cannot reference another module.");
            contacts.Add(new SpatialContact(old.Id, SpatialContactKind.ExternalGearMesh, old.ConstraintId, a.Id, b.Id));
            couplings.Add(new ExternalGearCoupling(old.ConstraintId, a.DofId, b.DofId, a.ToothCount, b.ToothCount));
        }
        var kinematic = new KinematicSpecification(source.Kinematic.RootDofId, source.Kinematic.Dofs, couplings);
        var generated = Generate(new LowLevelMechanicalSpecification("mechanical-authoring-fixed-source-v1", kinematic, new SpatialMechanism(axes, bodies, contacts)));
        if (!generated.IsSuccess) throw new MechanicalProfileException("The existing low-level planar source validator rejected rewritten kinematics or geometry.");
        var candidate = generated.Candidates.Single(); var originalDefinition = ImportPlanarArtifactDefinition(original);
        var originalTarget = originalDefinition.Outputs.Single().RequiredTransfer;
        var target = prefix.Length == 0 ? d.Outputs.SingleOrDefault(o => o.Key == "output")?.RequiredTransfer : originalTarget;
        var policy = prefix.Length == 0 ? d.ClearancePolicy : originalDefinition.ClearancePolicy;
        var requireClearance = prefix.Length == 0 ? d.RequireCrossComponentClearance : originalDefinition.RequireCrossComponentClearance;
        var rewrittenDefinition = ImportPlanarDefinition(candidate, target, policy, requireClearance);
        var rewrittenOutput = rewrittenDefinition.Outputs.Single();
        candidate.Solution.TryGetState(rewrittenOutput.ShaftId!, out var rewrittenState);
        if (target.HasValue && rewrittenState!.Coefficient != target.Value)
            throw new MechanicalProfileException("The original source-local requested transfer remains required. This adapter cannot silently replace a relative source requirement with an unrelated assembly-root target.");
        if (new CanonicalMechanismJson().ComputeCandidateId(candidate) == original.CandidateId && rewrittenDefinition.DefinitionId == originalDefinition.DefinitionId)
            return WriteArtifact(original).Bytes;
        // A new low-level definition, not a forged continuation of the old goal/synthesis provenance.
        var sourceId = "mechanical-authoring/" + new CanonicalMechanismJson().ComputeCandidateId(candidate);
        candidate = new GenerationCandidate(sourceId, candidate.Kinematic, candidate.Solution, candidate.Spatial, candidate.ResolvedPlayback, candidate.Validation);
        var context = new PlanarAuthoringContext(original.ArtifactHash, rewrittenOutput.ShaftId!, target, policy, requireClearance, rewrittenDefinition.DefinitionId);
        var metadata = new ArtifactMetadata(new ArtifactSourceProvenance(sourceId, "0", WritePlanarAuthoringContext(context)),
            new GeneratorFingerprint("0.1.0-dev", "mechanical-authoring-v1", "managed-exact", "artifact-v0.1"));
        var written = WriteArtifact(candidate, metadata);
        var read = ReadArtifact(written.Bytes);
        if (!Validate(read).IsValid || ImportPlanarArtifactDefinition(read).DefinitionId != rewrittenDefinition.DefinitionId)
            throw new ArtifactFormatException("New rewritten planar source failed ordinary read/validation or current authoring context checks.");
        return written.Bytes;
    }

    private const string PlanarAuthoringContextFormat = "gear-invest.planar-authoring-source";
    private sealed class PlanarAuthoringContext
    {
        internal PlanarAuthoringContext(string originalArtifactIdentity, string terminalDofId, Rational? target, string clearancePolicy, bool required, string definitionId)
        { OriginalArtifactIdentity = originalArtifactIdentity; TerminalDofId = terminalDofId; RequiredTransfer = target; ClearancePolicy = clearancePolicy; RequireCrossComponentClearance = required; DefinitionId = definitionId; }
        internal string OriginalArtifactIdentity { get; }
        internal string TerminalDofId { get; }
        internal Rational? RequiredTransfer { get; }
        internal string ClearancePolicy { get; }
        internal bool RequireCrossComponentClearance { get; }
        internal string DefinitionId { get; }
    }
    private static string WritePlanarAuthoringContext(PlanarAuthoringContext c)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteString("format", PlanarAuthoringContextFormat); w.WriteString("formatVersion", "0.1");
            w.WriteString("authoringKind", "explicit-fixed-planar-authoring-v1"); w.WriteString("originalArtifactIdentity", c.OriginalArtifactIdentity);
            w.WriteString("terminalDofId", c.TerminalDofId); w.WritePropertyName("requiredTransfer");
            if (c.RequiredTransfer.HasValue)
            { w.WriteStartObject(); w.WriteString("numerator", c.RequiredTransfer.Value.Numerator.ToString(CultureInfo.InvariantCulture)); w.WriteString("denominator", c.RequiredTransfer.Value.Denominator.ToString(CultureInfo.InvariantCulture)); w.WriteEndObject(); }
            else w.WriteNullValue();
            w.WriteString("clearancePolicy", c.ClearancePolicy); w.WriteBoolean("requireCrossComponentClearance", c.RequireCrossComponentClearance);
            w.WriteString("definitionId", c.DefinitionId); w.WriteEndObject();
        }
        if (stream.Length > 4096) throw new ArtifactFormatException("Planar authoring context exceeds its 4096-byte bound.");
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static PlanarAuthoringContext ReadPlanarAuthoringContext(string text)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > 4096) throw new ArtifactFormatException("Planar authoring context exceeds its byte bound.");
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 }); var p = doc.RootElement;
            if (p.GetProperty("format").GetString() != PlanarAuthoringContextFormat || p.GetProperty("formatVersion").GetString() != "0.1" ||
                p.GetProperty("authoringKind").GetString() != "explicit-fixed-planar-authoring-v1") throw new ArtifactFormatException("Unsupported planar authoring context.");
            var f = p.GetProperty("requiredTransfer"); Rational? target = null;
            if (f.ValueKind != JsonValueKind.Null)
            {
                var n = f.GetProperty("numerator").GetString()!; var d = f.GetProperty("denominator").GetString()!;
                if (n.Length > MechanicalAuthoringProfile.MaxInputDigits || d.Length > MechanicalAuthoringProfile.MaxInputDigits) throw new ArtifactFormatException("Planar target fraction digit bound.");
                target = new Rational(BigInteger.Parse(n, CultureInfo.InvariantCulture), BigInteger.Parse(d, CultureInfo.InvariantCulture));
                if (target.Value.Numerator.ToString(CultureInfo.InvariantCulture) != n || target.Value.Denominator.ToString(CultureInfo.InvariantCulture) != d)
                    throw new ArtifactFormatException("Planar target must use exact reduced canonical fractions.");
            }
            var c = new PlanarAuthoringContext(p.GetProperty("originalArtifactIdentity").GetString()!, p.GetProperty("terminalDofId").GetString()!, target,
                p.GetProperty("clearancePolicy").GetString()!, p.GetProperty("requireCrossComponentClearance").GetBoolean(), p.GetProperty("definitionId").GetString()!);
            if (!OrientedGoalKeys.IsPlanarHash(c.OriginalArtifactIdentity) || !OrientedGoalKeys.IsHash(c.DefinitionId) ||
                string.IsNullOrWhiteSpace(c.TerminalDofId) || c.TerminalDofId.Length > 160 || c.TerminalDofId.Any(char.IsControl) ||
                text != WritePlanarAuthoringContext(c)) throw new ArtifactFormatException("Noncanonical or invalid planar authoring context.");
            return c;
        }
        catch (ArtifactFormatException) { throw; }
        catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is KeyNotFoundException || e is FormatException || e is OverflowException || e is DivideByZeroException)
        { throw new ArtifactFormatException("Malformed planar authoring source context.", e); }
    }

    private OrientedTwoOutputAssemblyRequest RewriteMechanicalTwoOutputRequest(OrientedTwoOutputArtifact old, MechanicalDefinition d, string profile)
    {
        var request = old.Request; var pose = request.AssemblyPose;
        PlanarArtifactPlacement Placement(PlanarArtifactPlacement placement, string module)
        {
            var bytes = RewriteMechanicalPlanarSource(ReadArtifact(placement.SourceBytes), d, module + "/", pose.Transform(placement.Pose));
            var active = d.Ports.SingleOrDefault(p => p.Id == module + "/" + placement.ConnectionPort.Id);
            if (active is null) throw new MechanicalProfileException("The explicit imported source connection port is missing.");
            return new PlanarArtifactPlacement(bytes, placement.Pose, placement.InputDofId, placement.OutputDofId,
                new ShaftPort(placement.ConnectionPort.Id, placement.InputDofId, InverseFrame(pose, active.Frame), active.PhaseOffset, active.Kind));
        }
        var contact = d.Contacts.SingleOrDefault(c => c.Id == "bevel/contact");
        if (contact?.Cone is null || contact.Kind != OrientedContactKind.RightAngleBevel || contact.BodyAId != "bevel/pinion" || contact.BodyBId != "bevel/wheel")
            throw new MechanicalProfileException("The existing explicit bevel contact and cone are required.");
        BevelGearMount Mount(BevelGearMount previous, string bodyId, ExactVector3 direction)
        {
            var b = d.Bodies.SingleOrDefault(b => b.Id == bodyId); var s = d.Shafts.SingleOrDefault(s => s.Id == previous.Shaft.Id); var p = d.Ports.SingleOrDefault(p => p.Id == previous.Port.Id);
            if (b is null || s is null || p is null || b.Teeth <= 0) throw new MechanicalProfileException("Required bevel body/shaft/port is missing or invalid.");
            return new BevelGearMount(new OrientedShaft(s.Id, InverseFrame(pose, s.Frame), s.IsPrescribed), InverseVector(pose, direction), b.Teeth,
                b.OuterPitchRadius / b.Teeth, InversePoint(pose, b.MountingFrame.Origin), new ShaftPort(p.Id, p.ShaftId, InverseFrame(pose, p.Frame), p.PhaseOffset, p.Kind));
        }
        var cone = contact.Cone;
        var bevel = new RightAngleBevelRequest(InversePoint(pose, cone.Apex), Mount(request.Bevel.Input, "bevel/pinion", cone.OutwardA),
            Mount(request.Bevel.Output, "bevel/wheel", cone.OutwardB), cone.InnerParameter, request.Bevel.RequestedTransfer, request.Bevel.Profile);
        var parallel = Placement(request.ParallelBranch, "parallel"); var turned = request.TurnedBranch is null ? null : Placement(request.TurnedBranch, "turned");
        var outputs = new List<OrientedOutputRequest>();
        foreach (var output in d.Outputs)
        {
            if (!output.IsResolved || !output.Role.HasValue) throw new MechanicalProfileException("Exactly two resolved role-bound output terminals are required.");
            var prefix = output.Role == OrientedOutputRole.ParallelBranch ? "parallel/" : turned is null ? "" : "turned/";
            if (!output.BodyId!.StartsWith(prefix, StringComparison.Ordinal) || !output.PortId!.StartsWith(prefix, StringComparison.Ordinal) || !output.ShaftId!.StartsWith(prefix, StringComparison.Ordinal))
                throw new MechanicalProfileException("Output terminal does not have the explicit source namespace for its role.");
            var port = d.Ports.Single(p => p.Id == output.PortId);
            outputs.Add(new OrientedOutputRequest(output.Key, output.Role.Value, output.BodyId.Substring(prefix.Length),
                new ShaftPort(output.PortId.Substring(prefix.Length), output.ShaftId.Substring(prefix.Length), InverseFrame(pose, port.Frame), port.PhaseOffset, port.Kind), output.RequiredTransfer));
        }
        return new(bevel, parallel, outputs, turned, pose, d.RequireCrossComponentClearance, d.KeepOuts, profile);
    }

    private static BigInteger Integral(Rational value)
    { if (value.Denominator != BigInteger.One) throw new MechanicalProfileException("Existing planar artifact geometry requires integral source-local ticks; no rounding applied."); return value.Numerator; }
    private static ExactVector3 InverseVector(OrientedFrame frame, ExactVector3 vector) => new(vector.Dot(frame.X), vector.Dot(frame.Y), vector.Dot(frame.Z));
    private static ExactVector3 InversePoint(OrientedFrame frame, ExactVector3 point) => InverseVector(frame, point - frame.Origin);
    private static OrientedFrame InverseFrame(OrientedFrame frame, OrientedFrame world) => new(InversePoint(frame, world.Origin), InverseVector(frame, world.X), InverseVector(frame, world.Y), InverseVector(frame, world.Z));
    private sealed class MechanicalProfileException : Exception { internal MechanicalProfileException(string message) : base(message) { } }
}
