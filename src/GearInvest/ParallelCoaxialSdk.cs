using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public ParallelCoaxialArtifact ReadParallelCoaxialArtifact(byte[] bytes) => ParallelCoaxialArtifactJson.Read(bytes);
    public byte[] RebuildParallelCoaxialArtifact(ParallelCoaxialArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        var bytes = ParallelCoaxialArtifactJson.Write(artifact.Request);
        if (ParallelCoaxialArtifactJson.Read(bytes).ArtifactHash != artifact.ArtifactHash)
            throw new ArtifactFormatException("Current coaxial source reconstruction differs.");
        return bytes;
    }
    private MechanicalFinalizationResult FinalizeParallelCoaxialSource(MechanicalDraft draft)
    {
        MechanicalFinalizationResult Refuse(MechanicalFinalizationStatus status, string code, string detail) =>
            new(status, draft.DefinitionId, new[] { new MechanicalDiagnostic(code, "Finalization", detail: detail, scope: ParallelCoaxialLayout.Profile) });
        if (draft.Definition.CoaxialLayout is null)
            return Refuse(MechanicalFinalizationStatus.UnsupportedByExportProfile, "ExplicitCoaxialProfileRequired", "Legacy sources are never implicitly upgraded.");
        try
        {
            var original = VerifyMechanicalImportProvenance(draft);
            var gate = MechanicalFinalizationGate(draft);
            if (gate is not null) return gate;
            var bytes = ParallelCoaxialArtifactJson.Write(draft.Definition);
            var current = ParallelCoaxialArtifactJson.Read(bytes);
            if (current.DefinitionId != draft.DefinitionId) throw new ArtifactFormatException("Complete current coaxial definition mismatch.");
            var preserved = original is not null && original.ImportedProfile == ParallelCoaxialLayout.Profile &&
                original.OriginalArtifactBytes!.SequenceEqual(bytes);
            return new(MechanicalFinalizationStatus.Finalized, draft.DefinitionId, Array.Empty<MechanicalDiagnostic>(), bytes, current.ArtifactHash, preserved);
        }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is OverflowException)
        { return Refuse(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "CoaxialSourceReconstructionFailed", e.Message); }
    }
}
