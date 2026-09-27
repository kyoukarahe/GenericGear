using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

// Source-neutral comparison metadata. The mechanics still live in the existing family comparers.
internal sealed class TerminalComparisonView<TDescriptor, TOutput, TDevice> where TDescriptor : class
{
    internal TerminalComparisonView(string id, string? root, TOutput output, TDevice device, TDescriptor? descriptor,
        bool determined, MechanicalAxisVerdict guide, MechanicalAxisVerdict face, MechanicalExportAdmission admission,
        IEnumerable<string>? sourceOutputKeys = null, string? mapping = null)
    { AnalysisId = id; SelectedInputId = root; Output = output; Device = device; Descriptor = descriptor; HasDeterminedMotion = determined;
        GuideTravelCoverage = guide; FaceCoverage = face; ExportAdmission = admission; SourceOutputKeys = (sourceOutputKeys ?? Array.Empty<string>()).ToArray(); MappingCanonical = mapping; }
    internal string AnalysisId { get; }
    internal string? SelectedInputId { get; }
    internal TOutput Output { get; }
    internal TDevice Device { get; }
    internal TDescriptor? Descriptor { get; }
    internal bool HasDeterminedMotion { get; }
    internal bool HasFullCycleMotion => HasDeterminedMotion;
    internal MechanicalAxisVerdict GuideTravelCoverage { get; }
    internal MechanicalAxisVerdict FaceCoverage { get; }
    internal MechanicalExportAdmission ExportAdmission { get; }
    internal IReadOnlyList<string> SourceOutputKeys { get; }
    internal string? MappingCanonical { get; }
    internal Func<ExactQuantity, CrankSliderNumericRequest, (bool Admitted, CrankSliderNumericComputation? Numeric)>? CrankEvaluation { get; set; }
}

internal static class TerminalComparisonViews
{
    internal static TerminalComparisonView<GenevaMotionDescriptor, GenevaOutputDefinition, GenevaDeviceDefinition> From(GenevaAnalysis a) =>
        new(a.AnalysisId, a.SelectedInputId, a.Draft.Definition.Output, a.Draft.Definition.Device, a.Descriptor, a.HasFullCycleMotion,
            MechanicalAxisVerdict.NotAssessed, MechanicalAxisVerdict.NotAssessed, a.ExportAdmission, mapping: a.Draft.Definition.SourceMapping?.CanonicalRepresentation);
    internal static TerminalComparisonView<CamFollowerMotionDescriptor, PrismaticOutputDefinition, FlatCamFollowerDefinition> From(CamFollowerAnalysis a) =>
        new(a.AnalysisId, a.SelectedInputId, a.Draft.Definition.Output, a.Draft.Definition.Device, a.Descriptor, a.HasDeterminedMotion,
            a.GuideTravelCoverage, a.FaceCoverage, a.ExportAdmission);
    internal static TerminalComparisonView<CrankSliderMotionDescriptor, PrismaticOutputDefinition, PlanarCrankSliderDefinition> From(CrankSliderAnalysis a)
    {
        var view = new TerminalComparisonView<CrankSliderMotionDescriptor, PrismaticOutputDefinition, PlanarCrankSliderDefinition>(a.AnalysisId,
            a.SelectedInputId, a.Draft.Definition.Output, a.Draft.Definition.Device, a.Descriptor, a.HasDeterminedMotion,
            a.GuideTravelCoverage, MechanicalAxisVerdict.NotAssessed, a.ExportAdmission, a.SourceAnalysis.Outputs.Select(o => o.OutputKey));
        view.CrankEvaluation = (root, request) => { var evaluated = CrankSliderAnalyzer.Evaluate(a, root, request); return (evaluated.IsSuccess, evaluated.NumericComputation); };
        return view;
    }
}
