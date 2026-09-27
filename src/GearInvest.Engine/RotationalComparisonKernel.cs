using System;
using System.Collections.ObjectModel;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Read-only observation, not a fabricated mechanical definition or a second admission path.</summary>
internal sealed class RotationalObservation
{
    private RotationalObservation(string analysisId, string? selectedInputId, ReadOnlyCollection<MechanicalOutputAnalysis> outputs,
        MechanicalAxisVerdict geometry, MechanicalExportAdmission export)
    { AnalysisId = analysisId; SelectedInputId = selectedInputId; Outputs = outputs; Geometry = geometry; ExportAdmission = export; }
    internal string AnalysisId { get; }
    internal string? SelectedInputId { get; }
    internal ReadOnlyCollection<MechanicalOutputAnalysis> Outputs { get; }
    internal MechanicalAxisVerdict Geometry { get; }
    internal MechanicalExportAdmission ExportAdmission { get; }
    internal static RotationalObservation From(MechanicalAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        return new(analysis.AnalysisId, analysis.SelectedInputId, analysis.Outputs, analysis.Geometry, analysis.ExportAdmission);
    }
    internal static RotationalObservation From(OpenBeltAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        return new(analysis.AnalysisId, analysis.SelectedInputId, Array.AsReadOnly(new[] { analysis.Output }), analysis.Geometry, analysis.ExportAdmission);
    }
    internal static RotationalObservation From(PitchChainAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        return new(analysis.AnalysisId, analysis.SelectedInputId, Array.AsReadOnly(new[] { analysis.Output }), analysis.Geometry, analysis.ExportAdmission);
    }
    internal static RotationalObservation From(WormDriveAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        return new(analysis.AnalysisId, analysis.SelectedInputId, Array.AsReadOnly(new[] { analysis.Output }), analysis.Geometry, analysis.ExportAdmission);
    }
}

internal static class RotationalComparisonKernel
{
    internal static OutputEquivalenceResult Compare(RotationalObservation before, RotationalObservation after, OutputComparisonRequest request) =>
        MechanicalOutputComparer.CompareObservations(before, after, request);
}
