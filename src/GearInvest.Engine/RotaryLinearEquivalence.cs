using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum LinearOutputComparisonMode { FullLinearAffineOutput, WorldLinearRate, WorldLinearPath, AngleModuloOne }

public sealed class LinearOutputComparisonRequest
{
    public LinearOutputComparisonRequest(string beforeOutputKey, string afterOutputKey, string beforeInputId, string afterInputId,
        ExactQuantity? alpha = null, ExactQuantity? beta = null, int sign = 1, ExactQuantity? delta = null,
        LinearOutputComparisonMode mode = LinearOutputComparisonMode.FullLinearAffineOutput,
        ExactQuantityInterval? comparisonInterval = null, string? beforeReferencePointId = null, string? afterReferencePointId = null)
    {
        BeforeOutputKey = MechanicalAuthoringProfile.IdValue(beforeOutputKey); AfterOutputKey = MechanicalAuthoringProfile.IdValue(afterOutputKey);
        BeforeInputId = MechanicalAuthoringProfile.IdValue(beforeInputId); AfterInputId = MechanicalAuthoringProfile.IdValue(afterInputId);
        Alpha = alpha ?? ExactQuantity.TurnsPerTurn(1); Beta = beta ?? ExactQuantity.Turns(0); Sign = sign; Delta = delta ?? ExactQuantity.Millimeters(0);
        foreach (var q in new[] { Alpha, Beta, Delta }) RotaryLinearProfile.Quantity(q);
        if (Alpha.Value.IsZero || (sign != 1 && sign != -1)) throw new ArgumentException("Nonzero explicit input mapping and output sign +/-1 required.");
        if (!Enum.IsDefined(typeof(LinearOutputComparisonMode), mode)) throw new ArgumentException("Unknown comparison mode.");
        if (comparisonInterval is not null) RotaryLinearProfile.Interval(comparisonInterval);
        Mode = mode; ComparisonInterval = comparisonInterval;
        BeforeReferencePointId = beforeReferencePointId is null ? null : MechanicalAuthoringProfile.IdValue(beforeReferencePointId);
        AfterReferencePointId = afterReferencePointId is null ? null : MechanicalAuthoringProfile.IdValue(afterReferencePointId);
        RequestId = HashText(Pack(BeforeOutputKey, AfterOutputKey, BeforeInputId, AfterInputId,
            RotaryLinearProfile.Q(Alpha), RotaryLinearProfile.Q(Beta), N(Sign), RotaryLinearProfile.Q(Delta), Mode.ToString(),
            comparisonInterval is null ? "" : RotaryLinearProfile.I(comparisonInterval), BeforeReferencePointId ?? "", AfterReferencePointId ?? ""));
    }
    public string BeforeOutputKey { get; }
    public string AfterOutputKey { get; }
    public string BeforeInputId { get; }
    public string AfterInputId { get; }
    public ExactQuantity Alpha { get; }
    public ExactQuantity Beta { get; }
    public int Sign { get; }
    public ExactQuantity Delta { get; }
    public LinearOutputComparisonMode Mode { get; }
    public ExactQuantityInterval? ComparisonInterval { get; }
    public string? BeforeReferencePointId { get; }
    public string? AfterReferencePointId { get; }
    public string RequestId { get; }
}

public sealed class LinearOutputEquivalenceResult
{
    internal LinearOutputEquivalenceResult(LinearOutputComparisonRequest request, OutputEquivalenceVerdict verdict,
        string beforeAnalysisId, string afterAnalysisId, DimensionedAffineRelation? before, DimensionedAffineRelation? mappedAfter,
        ExactVector3? beforeGain, ExactVector3? afterGain, ExactVector3? beforeOffset, ExactVector3? afterOffset,
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
    /// <summary>Equality of the selected motion law only, never automatic complete assembly/domain approval.</summary>
    public OutputEquivalenceVerdict Verdict { get; }
    public string Scope => Request.Mode == LinearOutputComparisonMode.WorldLinearRate ? "WorldLinearRateEquivalentOnly" :
        Request.Mode == LinearOutputComparisonMode.WorldLinearPath ? "WorldLinearReferencePointPathEquivalentOnly" : "FullLinearAffineOutputEquivalent";
    public string BeforeAnalysisId { get; }
    public string AfterAnalysisId { get; }
    public DimensionedAffineRelation? BeforeRelation { get; }
    public DimensionedAffineRelation? MappedAfterRelation { get; }
    public ExactVector3? BeforeWorldGainMmPerRootTurn { get; }
    public ExactVector3? MappedAfterWorldGainMmPerRootTurn { get; }
    public ExactVector3? BeforeWorldOffsetMm { get; }
    public ExactVector3? MappedAfterWorldOffsetMm { get; }
    public ExactQuantityInterval? BeforeOperatingDomain { get; }
    public ExactQuantityInterval? MappedAfterOperatingDomain { get; }
    public bool OperatingDomainsEqual { get; }
    /// <summary>Null means no explicit comparison interval was supplied or comparison could not be established.</summary>
    public bool? BothFeasibleOnComparisonInterval { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public static class RotaryLinearOutputComparer
{
    public static LinearOutputEquivalenceResult Compare(RotaryLinearAnalysis before, RotaryLinearAnalysis after, LinearOutputComparisonRequest request)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException();
        var result = PrismaticComparisonKernel.Compare(PrismaticObservation.From(before), PrismaticObservation.From(after), request);
        return new LinearOutputEquivalenceResult(request, result.Verdict, before.AnalysisId, after.AnalysisId,
            result.BeforeRelation, result.MappedAfterRelation, result.BeforeWorldGainMmPerRootTurn, result.MappedAfterWorldGainMmPerRootTurn,
            result.BeforeWorldOffsetMm?.RationalPartMm, result.MappedAfterWorldOffsetMm?.RationalPartMm,
            result.BeforeOperatingDomain, result.MappedAfterOperatingDomain, result.OperatingDomainsEqual,
            result.BothFeasibleOnComparisonInterval, result.Diagnostics);
    }
}
