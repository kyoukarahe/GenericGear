using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Selected linear law/path equality with exact r+c/pi world offsets, not mechanical replacement approval.</summary>
public sealed class PrismaticOutputEquivalenceResult
{
    internal PrismaticOutputEquivalenceResult(LinearOutputComparisonRequest request, OutputEquivalenceVerdict verdict,
        string beforeAnalysisId, string afterAnalysisId, DimensionedAffineRelation? before, DimensionedAffineRelation? mappedAfter,
        ExactVector3? beforeGain, ExactVector3? afterGain, ExactPiVector3? beforeOffset, ExactPiVector3? afterOffset,
        ExactQuantityInterval? beforeDomain, ExactQuantityInterval? afterDomain, bool domainsEqual, bool? bothFeasible,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Request = request; Verdict = verdict; BeforeAnalysisId = beforeAnalysisId; AfterAnalysisId = afterAnalysisId;
        BeforeRelation = before; MappedAfterRelation = mappedAfter; BeforeWorldGainMmPerRootTurn = beforeGain; MappedAfterWorldGainMmPerRootTurn = afterGain;
        BeforeWorldOffsetMm = beforeOffset; MappedAfterWorldOffsetMm = afterOffset; BeforeOperatingDomain = beforeDomain; MappedAfterOperatingDomain = afterDomain;
        OperatingDomainsEqual = domainsEqual; BothFeasibleOnComparisonInterval = bothFeasible; Diagnostics = diagnostics.ToList().AsReadOnly();
        ComparisonId = HashText(Pack(request.RequestId, beforeAnalysisId, afterAnalysisId, verdict.ToString(), Scope));
    }
    public LinearOutputComparisonRequest Request { get; }
    public string ComparisonId { get; }
    public OutputEquivalenceVerdict Verdict { get; }
    public string Scope => Request.Mode == LinearOutputComparisonMode.WorldLinearRate ? "WorldLinearRateEquivalentOnly" :
        Request.Mode == LinearOutputComparisonMode.WorldLinearPath ? "WorldLinearReferencePointPathEquivalentOnly" : "FullLinearAffineOutputEquivalent";
    public string BeforeAnalysisId { get; }
    public string AfterAnalysisId { get; }
    public DimensionedAffineRelation? BeforeRelation { get; }
    public DimensionedAffineRelation? MappedAfterRelation { get; }
    public ExactVector3? BeforeWorldGainMmPerRootTurn { get; }
    public ExactVector3? MappedAfterWorldGainMmPerRootTurn { get; }
    public ExactPiVector3? BeforeWorldOffsetMm { get; }
    public ExactPiVector3? MappedAfterWorldOffsetMm { get; }
    public ExactQuantityInterval? BeforeOperatingDomain { get; }
    public ExactQuantityInterval? MappedAfterOperatingDomain { get; }
    public bool OperatingDomainsEqual { get; }
    public bool? BothFeasibleOnComparisonInterval { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public static class RackPinionOutputComparer
{
    public static PrismaticOutputEquivalenceResult Compare(RackPinionAnalysis before, RackPinionAnalysis after, LinearOutputComparisonRequest request) =>
        PrismaticComparisonKernel.Compare(PrismaticObservation.From(before), PrismaticObservation.From(after), request);
    public static PrismaticOutputEquivalenceResult Compare(RackPinionAnalysis before, RotaryLinearAnalysis after, LinearOutputComparisonRequest request) =>
        PrismaticComparisonKernel.Compare(PrismaticObservation.From(before), PrismaticObservation.From(after), request);
    public static PrismaticOutputEquivalenceResult Compare(RotaryLinearAnalysis before, RackPinionAnalysis after, LinearOutputComparisonRequest request) =>
        PrismaticComparisonKernel.Compare(PrismaticObservation.From(before), PrismaticObservation.From(after), request);
}
