using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest;

public enum AssemblyReplayExportStatus { Exported, SourceFinalizationFailed, ResourceLimitExceeded, InvalidRequest }
public sealed class AssemblyReplayExportResult
{
    internal AssemblyReplayExportResult(AssemblyReplayExportStatus status, string detail, AssemblyReplayDocument? document = null,
        MechanicalAssemblyFinalizationResult? finalization = null, int completedSamples = 0, long numericWork = 0)
    { Status = status; Detail = detail; Document = document; SourceFinalization = finalization; CompletedSamples = completedSamples; NumericWork = numericWork; }
    public AssemblyReplayExportStatus Status { get; }
    public string Detail { get; }
    public AssemblyReplayDocument? Document { get; }
    public MechanicalAssemblyFinalizationResult? SourceFinalization { get; }
    public int CompletedSamples { get; }
    public long NumericWork { get; }
    public bool IsExported => Status == AssemblyReplayExportStatus.Exported;
}

public sealed partial class GearInvestSdk
{
    /// <summary>Fresh source finalization, owned fixed-policy evaluation, bounded consumption document. Never repairs the source.</summary>
    public AssemblyReplayExportResult ExportMechanicalAssemblyReplay(MechanicalAssemblyDraft draft, AssemblyReplayExportRequest request,
        AssemblyNumericRequest? numericRequest = null)
    {
        if (draft is null || request is null) throw new ArgumentNullException();
        var policy = numericRequest ?? AssemblyNumericRequest.Default;
        if (!policy.IsValid) return new(AssemblyReplayExportStatus.InvalidRequest, "A valid explicit numeric policy is required.");
        if (draft.Definition.Members.Count > AssemblyReplayLimits.Members)
            return new(AssemblyReplayExportStatus.ResourceLimitExceeded, "Replay member bound exceeded before evaluation.");
        // Worst-case work reservation before any source/numeric work. Actual charged work remains separate below.
        if ((long)(request.SampleRoots.Count + 3) * policy.MaximumWork > request.MaximumTotalWork)
            return new(AssemblyReplayExportStatus.ResourceLimitExceeded, "Declared sample/source work reservation exceeds export budget.");
        var count = 0; long work = 0;
        try
        {
            // Own first, verify the exact owned copy, then finalize it through the established public boundary.
            var prepared = PrepareMechanicalAssemblyEvaluation(draft, policy); work += prepared.Analysis.NumericWork;
            var roots = AssemblyReplayJson.ResolveSampleRoots(prepared.Analysis, request);
            if ((long)(roots.Count + 3) * policy.MaximumWork > request.MaximumTotalWork)
                return new(AssemblyReplayExportStatus.ResourceLimitExceeded, "Expanded exact-boundary reservation exceeds export budget.", numericWork: work);
            var finalized = TryFinalizeMechanicalAssembly(prepared.Analysis.Draft, policy);
            if (!finalized.IsFinalized) return new(AssemblyReplayExportStatus.SourceFinalizationFailed, finalized.Status.ToString(), finalization: finalized, numericWork: work);
            var source = finalized.Artifact!; var bytes = finalized.ArtifactBytes!;
            work += source.Analysis.NumericWork + source.ReferenceEvaluation.NumericWork;
            if (bytes.Length > AssemblyReplayLimits.SourceBytes) return new(AssemblyReplayExportStatus.ResourceLimitExceeded, "Replay source byte bound exceeded.", numericWork: work);
            var values = new List<MechanicalAssemblyEvaluation>();
            foreach (var root in roots)
            {
                var current = prepared.Evaluate(ExactQuantity.Turns(root)); work += current.NumericWork; count++;
                if (work > request.MaximumTotalWork) return new(AssemblyReplayExportStatus.ResourceLimitExceeded, "Actual aggregate work bound exceeded.", completedSamples: count, numericWork: work);
                values.Add(current);
            }
            var document = AssemblyReplayJson.Create(source, bytes, request, values);
            return new(AssemblyReplayExportStatus.Exported, "Current source finalized; exact relations and finite C# observations exported. Browser mechanical validation not performed.", document, finalized, count, work);
        }
        catch (Exception ex) when (ex is ArtifactFormatException || ex is ArgumentException || ex is ArithmeticException)
        {
            var resource = ex.Message.IndexOf("bound", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("budget", StringComparison.OrdinalIgnoreCase) >= 0;
            return new(resource ? AssemblyReplayExportStatus.ResourceLimitExceeded : AssemblyReplayExportStatus.InvalidRequest, ex.Message, completedSamples: count, numericWork: work);
        }
    }
    public AssemblyReplayDocument ReadMechanicalAssemblyReplay(byte[] bytes) => AssemblyReplayJson.Read(bytes);
    public byte[] WriteMechanicalAssemblyReplay(AssemblyReplayDocument document) => AssemblyReplayJson.Write(document);
    /// <summary>Explicit fresh mechanical reconstruction and same-request export; no cached validation flag is trusted.</summary>
    public AssemblyReplayDocument RebuildMechanicalAssemblyReplay(AssemblyReplayDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var original = ReadMechanicalAssemblyArtifact(document.OriginalArtifactBytes);
        var result = ExportMechanicalAssemblyReplay(original.Request, document.Request, original.NumericRequest);
        if (!result.IsExported || !result.Document!.OriginalBytes.SequenceEqual(document.OriginalBytes))
            throw new ArtifactFormatException("Current source/request replay reconstruction differs: " + result.Detail);
        return result.Document;
    }
}
