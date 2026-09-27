using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum CrankSliderOutputComparisonMode { SliderScalar, WorldSliderPath }
public enum CrankSliderComparisonVerdict { Equivalent, Different, Inconclusive, NotComparable }

/// <summary>Caller-owned correspondence; no inferred phase, scale, output or world alignment.</summary>
public sealed class CrankSliderOutputComparisonRequest
{
    public CrankSliderOutputComparisonRequest(string beforeOutputKey, string afterOutputKey, string beforeInputId, string afterInputId,
        ExactQuantity? alpha = null, ExactQuantity? beta = null, int sign = 1, ExactQuantity? delta = null,
        CrankSliderOutputComparisonMode mode = CrankSliderOutputComparisonMode.SliderScalar,
        string? beforeReferencePointId = null, string? afterReferencePointId = null, OrientedFrame? afterWorldToBeforeWorldMm = null,
        IEnumerable<ExactQuantity>? witnessRoots = null, CrankSliderNumericRequest? numericRequest = null,
        string domain = "AllRootTurns", string proofPolicy = CrankSliderOutputComparer.Policy)
    {
        BeforeOutputKey = MechanicalAuthoringProfile.IdValue(beforeOutputKey); AfterOutputKey = MechanicalAuthoringProfile.IdValue(afterOutputKey);
        BeforeInputId = MechanicalAuthoringProfile.IdValue(beforeInputId); AfterInputId = MechanicalAuthoringProfile.IdValue(afterInputId);
        Alpha = alpha ?? ExactQuantity.TurnsPerTurn(1); Beta = beta ?? ExactQuantity.Turns(0); Sign = sign; Delta = delta ?? ExactQuantity.Millimeters(0);
        foreach (var q in new[] { Alpha, Beta, Delta }) RotaryLinearProfile.Quantity(q);
        if (Alpha.Value.IsZero || (sign != -1 && sign != 1)) throw new ArgumentException("Explicit nonzero input mapping and output sign +/-1 required.");
        if (!Enum.IsDefined(typeof(CrankSliderOutputComparisonMode), mode)) throw new ArgumentException("Unsupported nonlinear comparison mode.");
        Mode = mode; Domain = MechanicalAuthoringProfile.IdValue(domain); ProofPolicy = MechanicalAuthoringProfile.IdValue(proofPolicy);
        BeforeReferencePointId = beforeReferencePointId is null ? null : MechanicalAuthoringProfile.IdValue(beforeReferencePointId);
        AfterReferencePointId = afterReferencePointId is null ? null : MechanicalAuthoringProfile.IdValue(afterReferencePointId);
        if (afterWorldToBeforeWorldMm is not null) MechanicalAuthoringProfile.FrameBound(afterWorldToBeforeWorldMm);
        AfterWorldToBeforeWorldMm = afterWorldToBeforeWorldMm; NumericRequest = numericRequest ?? CrankSliderNumericRequest.Default;
        var roots = (witnessRoots ?? new[] { ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(1, 4)), ExactQuantity.Turns(new Rational(1, 2)),
            ExactQuantity.Turns(new Rational(3, 4)), ExactQuantity.Turns(1), ExactQuantity.Turns(new Rational(-1, 4)) }).Take(33).ToArray();
        if (roots.Length > 32) throw new ArgumentException("At most32 explicit nonlinear comparison witnesses are supported.");
        foreach (var root in roots) RotaryLinearProfile.Quantity(root);
        WitnessRoots = Array.AsReadOnly(roots); RequestId = HashText(CanonicalRepresentation);
    }
    public string BeforeOutputKey { get; } public string AfterOutputKey { get; }
    public string BeforeInputId { get; } public string AfterInputId { get; }
    public ExactQuantity Alpha { get; } public ExactQuantity Beta { get; } public int Sign { get; } public ExactQuantity Delta { get; }
    public CrankSliderOutputComparisonMode Mode { get; } public string Domain { get; } public string ProofPolicy { get; }
    public string? BeforeReferencePointId { get; } public string? AfterReferencePointId { get; }
    public OrientedFrame? AfterWorldToBeforeWorldMm { get; }
    public ReadOnlyCollection<ExactQuantity> WitnessRoots { get; }
    public CrankSliderNumericRequest NumericRequest { get; }
    public string RequestId { get; }
    public string CanonicalRepresentation => Pack(BeforeOutputKey, AfterOutputKey, BeforeInputId, AfterInputId,
        CrankSliderProfile.Q(Alpha), CrankSliderProfile.Q(Beta), N(Sign), CrankSliderProfile.Q(Delta), Mode.ToString(), Domain, ProofPolicy,
        BeforeReferencePointId ?? "", AfterReferencePointId ?? "", AfterWorldToBeforeWorldMm is null ? "" : Frame(AfterWorldToBeforeWorldMm),
        Pack(WitnessRoots.Select(CrankSliderProfile.Q).ToArray()), NumericKey(NumericRequest));
    internal static string NumericKey(CrankSliderNumericRequest q) => q.CanonicalRepresentation;
}

/// <summary>A separating certified interval pair at the explicitly recorded, valid corresponding inputs.</summary>
public sealed class CrankSliderComparisonWitness
{
    internal CrankSliderComparisonWitness(ExactQuantity beforeRoot, ExactQuantity afterRoot, string coordinate,
        CrankSliderInterval before, CrankSliderInterval mappedAfter)
    { BeforeRoot = beforeRoot; AfterRoot = afterRoot; Coordinate = coordinate; Before = before; MappedAfter = mappedAfter; }
    public ExactQuantity BeforeRoot { get; } public ExactQuantity AfterRoot { get; }
    public string Coordinate { get; } public CrankSliderInterval Before { get; } public CrankSliderInterval MappedAfter { get; }
    public bool IsSeparating => Before.Upper < MappedAfter.Lower || MappedAfter.Upper < Before.Lower;
    public string CanonicalRepresentation => Pack(CrankSliderProfile.Q(BeforeRoot), CrankSliderProfile.Q(AfterRoot), Coordinate,
        F(Before.Lower), F(Before.Upper), F(MappedAfter.Lower), F(MappedAfter.Upper));
}

public sealed class CrankSliderOutputEquivalenceResult
{
    internal CrankSliderOutputEquivalenceResult(CrankSliderOutputComparisonRequest request, string beforeAnalysisId, string afterAnalysisId,
        CrankSliderComparisonVerdict verdict, string proofRule, bool? operatingDomainsEqual,
        MechanicalAxisVerdict beforeFullCycleTravel, MechanicalAxisVerdict afterFullCycleTravel,
        MechanicalExportAdmission beforeExportAdmission, MechanicalExportAdmission afterExportAdmission,
        IEnumerable<CrankSliderComparisonWitness> witnesses, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Request = request; BeforeAnalysisId = beforeAnalysisId; AfterAnalysisId = afterAnalysisId; Verdict = verdict; ProofRule = proofRule;
        OperatingDomainsEqual = operatingDomainsEqual; BeforeFullCycleTravel = beforeFullCycleTravel; AfterFullCycleTravel = afterFullCycleTravel;
        BeforeExportAdmission = beforeExportAdmission; AfterExportAdmission = afterExportAdmission;
        Witnesses = witnesses.ToList().AsReadOnly(); Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        ResultId = HashText(Pack(request.RequestId, beforeAnalysisId, afterAnalysisId, verdict.ToString(), proofRule,
            operatingDomainsEqual?.ToString() ?? "", beforeFullCycleTravel.ToString(), afterFullCycleTravel.ToString(), beforeExportAdmission.ToString(),
            afterExportAdmission.ToString(), Pack(Witnesses.Select(w => w.CanonicalRepresentation).ToArray())));
    }
    public CrankSliderOutputComparisonRequest Request { get; }
    public string BeforeAnalysisId { get; } public string AfterAnalysisId { get; } public string ResultId { get; }
    public CrankSliderComparisonVerdict Verdict { get; } public string ProofRule { get; }
    public string Policy => CrankSliderOutputComparer.Policy;
    public string Scope => "SliderReferenceOutputOnly";
    public bool? OperatingDomainsEqual { get; }
    public MechanicalAxisVerdict BeforeFullCycleTravel { get; } public MechanicalAxisVerdict AfterFullCycleTravel { get; }
    public MechanicalExportAdmission BeforeExportAdmission { get; } public MechanicalExportAdmission AfterExportAdmission { get; }
    public ReadOnlyCollection<CrankSliderComparisonWitness> Witnesses { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public static partial class CrankSliderOutputComparer
{
    public const string Policy = "crank-slider-output-proof-v1";
}
