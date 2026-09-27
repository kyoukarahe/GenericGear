using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    /// <summary>Shared complete 21A source finalize/reimport boundary for one attached device.
    /// Device admission remains in its own analyzer; no source IDs, bodies, outputs or coordinates are repaired here.</summary>
    private AttachedMechanicalSourceFinalization FinalizeAttachedMechanicalSource(MechanicalDraft source, string retainedShaftId, string? retainedPortId)
    {
        VerifyMechanicalImportProvenance(source);
        if (source.OriginalArtifactBytes is null && source.Definition.CoaxialLayout is null)
        {
            var d = source.Definition;
            if (d.Shafts.Count != 1 || !d.Shafts[0].IsPrescribed || d.RootShaftId != d.Shafts[0].Id || retainedShaftId != d.RootShaftId ||
                d.Bodies.Count != 0 || d.Contacts.Count != 0 || d.Connections.Count != 0 || d.Outputs.Count != 0 || d.KeepOuts.Count != 0 || d.Ports.Any(p => p.ShaftId != d.RootShaftId))
                return new(MechanicalFinalizationStatus.UnsupportedByExportProfile, "UnsupportedStandaloneSource", "Standalone is exactly one prescribed retained shaft, optional shaft ports and no gears, contacts, outputs or obstacles.");
            return new(null, null, false);
        }
        var finalSource = TryFinalizeMechanicalDraft(source, source.Definition.CoaxialLayout is null ? source.ImportedProfile ?? "" : GearInvest.Layout.ParallelCoaxialLayout.Profile);
        if (!finalSource.IsFinalized)
            return new(finalSource.Status, "CurrentSourceFinalizationFailed", "The complete current source did not finalize through the existing 21A profile.", finalSource.Diagnostics,
                finalSource.Diagnostics.Any(d => IsKnownAttachmentResourceMessage(d.Detail)));
        var reimported = ImportMechanicalArtifact(finalSource.ArtifactBytes!);
        if (reimported.DefinitionId != source.DefinitionId ||
            !source.Definition.Shafts.Select(s => s.Id).SequenceEqual(reimported.Definition.Shafts.Select(s => s.Id)) ||
            !reimported.Definition.Shafts.Any(s => s.Id == retainedShaftId) ||
            retainedPortId is not null && !reimported.Definition.Ports.Any(p => p.Id == retainedPortId && p.ShaftId == retainedShaftId))
            return new(MechanicalFinalizationStatus.UnresolvedSourceProvenance, "SourceContextMismatch", "Complete reimported definition and exact retained shaft/port identities must match. No inferred remapping is supported.");
        return new(finalSource.ArtifactBytes, finalSource.ArtifactIdentity, finalSource.OriginalBytesPreserved);
    }
    private sealed class AttachedMechanicalSourceFinalization
    {
        public AttachedMechanicalSourceFinalization(byte[]? bytes, string? identity, bool originalPreserved)
        { Bytes = bytes; Identity = identity; OriginalPreserved = originalPreserved; }
        public AttachedMechanicalSourceFinalization(MechanicalFinalizationStatus failure, string code, string detail, IEnumerable<MechanicalDiagnostic>? diagnostics = null, bool resourceLimited = false)
        { Failure = failure; Code = code; Detail = detail; Diagnostics = diagnostics; ResourceLimited = resourceLimited; }
        public byte[]? Bytes { get; } public string? Identity { get; } public bool OriginalPreserved { get; }
        public MechanicalFinalizationStatus? Failure { get; } public bool ResourceLimited { get; }
        public string Code { get; } = ""; public string Detail { get; } = ""; public IEnumerable<MechanicalDiagnostic>? Diagnostics { get; }
    }
    // Exact refusal whitelist, not arbitrary substring matching. Existing22A/23A classification is unchanged.
    private static bool IsKnownAttachmentResourceMessage(string message) => message is
        "Exact quantity resource digit bound exceeded." or "Exact input digit bound exceeded." or "Affine exact digit bound exceeded." or
        "Derived exact digit bound exceeded." or "Mechanical sidecar document byte limit exceeded." or "Mechanical sidecar JSON node limit exceeded." or
        "Planar authoring context exceeds its 4096-byte bound." or "Planar authoring context exceeds its byte bound." or "Planar target fraction digit bound." or
        "Final source byte bound exceeded.";
    private static bool IsAttachmentResourceRefusal(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if ((current is ArgumentException || current is GearInvest.Serialization.Json.ArtifactFormatException) && IsKnownAttachmentResourceMessage(current.Message)) return true;
        return false;
    }
}
