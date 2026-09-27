using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum OutputComparisonMode { FullAffineOutput, WorldAngularRate, AngleModuloOne }
public enum OutputEquivalenceVerdict { Equivalent, Different, NotComparable }

public sealed class OutputComparisonRequest
{
    public OutputComparisonRequest(string beforeOutputKey, string afterOutputKey, string beforeInputId, string afterInputId,
        Rational? alpha = null, Rational? beta = null, Rational? sign = null, Rational? delta = null,
        OutputComparisonMode mode = OutputComparisonMode.FullAffineOutput, string motionDomain = MechanicalAuthoringProfile.MotionDomain)
    {
        BeforeOutputKey = MechanicalAuthoringProfile.IdValue(beforeOutputKey); AfterOutputKey = MechanicalAuthoringProfile.IdValue(afterOutputKey);
        BeforeInputId = MechanicalAuthoringProfile.IdValue(beforeInputId); AfterInputId = MechanicalAuthoringProfile.IdValue(afterInputId);
        Alpha = alpha ?? Rational.One; Beta = beta ?? Rational.Zero; Sign = sign ?? Rational.One; Delta = delta ?? Rational.Zero;
        foreach (var n in new[] { Alpha, Beta, Sign, Delta }) MechanicalAuthoringProfile.Number(n);
        if (Alpha.IsZero || (Sign != 1 && Sign != -1)) throw new ArgumentException("Explicit nonzero input mapping and output sign +/-1 required.");
        if (!Enum.IsDefined(typeof(OutputComparisonMode), mode)) throw new ArgumentException("Unknown comparison mode.");
        Mode = mode; MotionDomain = MechanicalAuthoringProfile.IdValue(motionDomain);
        RequestId = HashText(Pack(BeforeOutputKey, AfterOutputKey, BeforeInputId, AfterInputId, F(Alpha), F(Beta), F(Sign), F(Delta), Mode.ToString(), MotionDomain));
    }
    public string BeforeOutputKey { get; }
    public string AfterOutputKey { get; }
    public string BeforeInputId { get; }
    public string AfterInputId { get; }
    public Rational Alpha { get; }
    public Rational Beta { get; }
    public Rational Sign { get; }
    public Rational Delta { get; }
    public OutputComparisonMode Mode { get; }
    public string MotionDomain { get; }
    public string RequestId { get; }
}

public sealed class OutputEquivalenceResult
{
    internal OutputEquivalenceResult(OutputComparisonRequest request, OutputEquivalenceVerdict verdict, string scope,
        ExactAffineRelation? before, ExactAffineRelation? after, ExactVector3? beforeRate, ExactVector3? afterRate,
        string? beforeAnalysisId, string? afterAnalysisId, MechanicalAxisVerdict beforeGeometry, MechanicalAxisVerdict afterGeometry,
        MechanicalExportAdmission? beforeExport, MechanicalExportAdmission? afterExport, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Request = request; Verdict = verdict; Scope = scope; BeforeRelation = before; MappedAfterRelation = after;
        BeforeWorldAngularRate = beforeRate; MappedAfterWorldAngularRate = afterRate; BeforeAnalysisId = beforeAnalysisId; AfterAnalysisId = afterAnalysisId;
        BeforeGeometry = beforeGeometry; AfterGeometry = afterGeometry; BeforeExportAdmission = beforeExport; AfterExportAdmission = afterExport;
        Diagnostics = diagnostics.ToList().AsReadOnly();
        ComparisonId = HashText(Pack(request.RequestId, beforeAnalysisId ?? "", afterAnalysisId ?? "", scope,
            before.HasValue ? Pack(F(before.Value.Coefficient), F(before.Value.Phase)) : "", after.HasValue ? Pack(F(after.Value.Coefficient), F(after.Value.Phase)) : "", verdict.ToString()));
    }
    public OutputComparisonRequest Request { get; }
    public string ComparisonId { get; }
    public OutputEquivalenceVerdict Verdict { get; }
    /// <summary>FullAffineOutputEquivalent is a coordinate relation, not equal body identity/location or full mechanism validity.</summary>
    public string Scope { get; }
    public ExactAffineRelation? BeforeRelation { get; }
    public ExactAffineRelation? MappedAfterRelation { get; }
    public ExactVector3? BeforeWorldAngularRate { get; }
    public ExactVector3? MappedAfterWorldAngularRate { get; }
    public string? BeforeAnalysisId { get; }
    public string? AfterAnalysisId { get; }
    public MechanicalAxisVerdict BeforeGeometry { get; }
    public MechanicalAxisVerdict AfterGeometry { get; }
    public MechanicalExportAdmission? BeforeExportAdmission { get; }
    public MechanicalExportAdmission? AfterExportAdmission { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public static class MechanicalOutputComparer
{
    public static OutputEquivalenceResult Compare(MechanicalAnalysis before, MechanicalAnalysis after, OutputComparisonRequest request) =>
        RotationalComparisonKernel.Compare(RotationalObservation.From(before), RotationalObservation.From(after), request);

    internal static OutputEquivalenceResult CompareObservations(RotationalObservation before, RotationalObservation after, OutputComparisonRequest request)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException();
        var diagnostics = new List<MechanicalDiagnostic>(); var scope = Scope(request.Mode); ExactAffineRelation? a = null, b = null; ExactVector3? ar = null, br = null;
        OutputEquivalenceResult Result(OutputEquivalenceVerdict verdict) => new(request, verdict, scope, a, b, ar, br, before.AnalysisId, after.AnalysisId,
            before.Geometry, after.Geometry, before.ExportAdmission, after.ExportAdmission, diagnostics);
        void Block(string code, IEnumerable<string>? prerequisite = null) => diagnostics.Add(new MechanicalDiagnostic(code, "OutputComparison",
            related: new[] { new MechanicalReference("BeforeOutput", request.BeforeOutputKey), new MechanicalReference("AfterOutput", request.AfterOutputKey) },
            blockedPrerequisites: prerequisite, scope: scope));
        if (request.MotionDomain != MechanicalAuthoringProfile.MotionDomain) { Block("UnsupportedMotionDomain"); return Result(OutputEquivalenceVerdict.NotComparable); }
        if (request.BeforeInputId != before.SelectedInputId || request.AfterInputId != after.SelectedInputId)
        { Block("InputMappingUnavailable"); return Result(OutputEquivalenceVerdict.NotComparable); }
        var oa = before.Outputs.FirstOrDefault(o => o.OutputKey == request.BeforeOutputKey); var ob = after.Outputs.FirstOrDefault(o => o.OutputKey == request.AfterOutputKey);
        if (oa is null || ob is null || !oa.Binding.IsResolved || !ob.Binding.IsResolved)
        { Block("MissingEndpoint"); return Result(OutputEquivalenceVerdict.NotComparable); }
        if (!oa.HasDeterminedMotion || !ob.HasDeterminedMotion)
        {
            var failed = !oa.HasDeterminedMotion ? oa : ob;
            Block(failed.Determinacy == MechanicalDeterminacy.BlockedByInvalidConstraint ? "InvalidPrerequisiteContact" : failed.Determinacy.ToString(), failed.BlockedPrerequisites);
            diagnostics.AddRange(failed.Diagnostics); return Result(OutputEquivalenceVerdict.NotComparable);
        }
        a = oa.PortRelation; b = Map(ob.PortRelation!.Value, request);
        bool equal;
        if (request.Mode == OutputComparisonMode.WorldAngularRate)
        {
            // Coordinate sign and phase offsets are scalar basis choices; physical rate uses shaft axes in the explicitly shared world basis.
            ar = oa.ShaftPositiveAxis!.Value * oa.ShaftRelation!.Value.Coefficient;
            br = ob.ShaftPositiveAxis!.Value * ob.ShaftRelation!.Value.Coefficient * request.Alpha;
            equal = ar == br;
        }
        else equal = Equal(a!.Value, b!.Value, request.Mode);
        if (!equal) diagnostics.Add(request.Mode == OutputComparisonMode.WorldAngularRate
            ? new MechanicalDiagnostic("WorldAngularRateDifferent", "OutputComparison", related: new[] { new MechanicalReference("BeforeOutput", oa.OutputKey), new MechanicalReference("AfterOutput", ob.OutputKey) },
                facts: new[] { new MechanicalExactFact("worldRateX", ar!.Value.X, br!.Value.X), new MechanicalExactFact("worldRateY", ar.Value.Y, br.Value.Y), new MechanicalExactFact("worldRateZ", ar.Value.Z, br.Value.Z) }, scope: scope)
            : Difference(a!.Value, b!.Value, scope, request, oa, ob));
        return Result(equal ? OutputEquivalenceVerdict.Equivalent : OutputEquivalenceVerdict.Different);
    }

    /// <summary>Explicit scalar mathematics only. This does not admit nonzero tooth phase into a mechanical profile.</summary>
    public static OutputEquivalenceResult CompareAffineRelations(ExactAffineRelation before, ExactAffineRelation after, OutputComparisonRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        foreach (var v in new[] { before.Coefficient, before.Phase, after.Coefficient, after.Phase }) MechanicalAuthoringProfile.Number(v);
        return CompareAffineCore(before, after, request);
    }

    internal static OutputEquivalenceResult CompareDerivedAffineRelations(ExactAffineRelation before, ExactAffineRelation after, OutputComparisonRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        foreach (var v in new[] { before.Coefficient, before.Phase, after.Coefficient, after.Phase }) MechanicalDerivedNumbers.Check(v);
        return CompareAffineCore(before, after, request);
    }

    private static OutputEquivalenceResult CompareAffineCore(ExactAffineRelation before, ExactAffineRelation after, OutputComparisonRequest request)
    {
        var mapped = Map(after, request); var scope = "ScalarAffineMathematics/" + Scope(request.Mode);
        var unsupported = request.Mode == OutputComparisonMode.WorldAngularRate || request.MotionDomain != MechanicalAuthoringProfile.MotionDomain;
        var verdict = unsupported ? OutputEquivalenceVerdict.NotComparable : Equal(before, mapped, request.Mode) ? OutputEquivalenceVerdict.Equivalent : OutputEquivalenceVerdict.Different;
        var diagnostics = unsupported ? new[] { new MechanicalDiagnostic("UnsupportedComparisonDomain", "OutputComparison", scope: scope) }
            : verdict == OutputEquivalenceVerdict.Different ? new[] { Difference(before, mapped, scope, request) } : Array.Empty<MechanicalDiagnostic>();
        return new OutputEquivalenceResult(request, verdict, scope, before, mapped, null, null, null, null,
            MechanicalAxisVerdict.NotAssessed, MechanicalAxisVerdict.NotAssessed, null, null, diagnostics);
    }
    private static ExactAffineRelation Map(ExactAffineRelation after, OutputComparisonRequest request)
    {
        var mapped = new ExactAffineRelation(request.Sign * after.Coefficient * request.Alpha, request.Sign * (after.Coefficient * request.Beta + after.Phase) + request.Delta);
        MechanicalDerivedNumbers.Check(mapped.Coefficient); MechanicalDerivedNumbers.Check(mapped.Phase); return mapped;
    }
    private static bool Equal(ExactAffineRelation a, ExactAffineRelation b, OutputComparisonMode mode) => a.Coefficient == b.Coefficient &&
        (mode == OutputComparisonMode.AngleModuloOne ? (a.Phase - b.Phase).Denominator.IsOne : a.Phase == b.Phase);
    private static string Scope(OutputComparisonMode mode) => mode == OutputComparisonMode.WorldAngularRate ? "WorldAngularRateEquivalentOnly" : mode == OutputComparisonMode.AngleModuloOne ? "AngleModuloOneEquivalentOnly" : "FullAffineOutputEquivalent";
    private static MechanicalDiagnostic Difference(ExactAffineRelation a, ExactAffineRelation b, string scope, OutputComparisonRequest request,
        MechanicalOutputAnalysis? oa = null, MechanicalOutputAnalysis? ob = null)
    {
        var related = new List<MechanicalReference> { new("BeforeOutput", request.BeforeOutputKey), new("AfterOutput", request.AfterOutputKey) };
        var facts = new List<MechanicalExactFact> { new("coefficient", a.Coefficient, b.Coefficient), new("phase", a.Phase, b.Phase),
            new("inputMappingAlpha", null, request.Alpha), new("inputMappingBeta", null, request.Beta),
            new("outputMappingSign", null, request.Sign), new("outputMappingDelta", null, request.Delta) };
        var paths = new List<MechanicalPathWitness>();
        if (oa is not null && ob is not null)
        {
            related.Add(new MechanicalReference("BeforePort", oa.Binding.PortId!)); related.Add(new MechanicalReference("AfterPort", ob.Binding.PortId!));
            facts.Add(new MechanicalExactFact("beforePortCoordinateSign", null, oa.ShaftPositiveAxis!.Value.Dot(oa.PortFrame!.Z)));
            facts.Add(new MechanicalExactFact("afterPortCoordinateSign", null, ob.ShaftPositiveAxis!.Value.Dot(ob.PortFrame!.Z)));
            facts.Add(new MechanicalExactFact("unmappedPortCoefficient", oa.PortRelation!.Value.Coefficient, ob.PortRelation!.Value.Coefficient));
            facts.Add(new MechanicalExactFact("unmappedPortPhase", oa.PortRelation.Value.Phase, ob.PortRelation.Value.Phase));
            // These paths are independently substitutable in the original shaft graphs. Port/reference mappings are separate facts, not fictitious path edges.
            paths.Add(new MechanicalPathWitness(request.BeforeInputId, oa.Binding.ShaftId!, oa.ShaftRelation!.Value.Coefficient, oa.ShaftRelation.Value.Phase, oa.ConstraintPath));
            paths.Add(new MechanicalPathWitness(request.AfterInputId, ob.Binding.ShaftId!, ob.ShaftRelation!.Value.Coefficient, ob.ShaftRelation.Value.Phase, ob.ConstraintPath));
        }
        return new MechanicalDiagnostic("OutputMotionDifferent", "OutputComparison", related: related, facts: facts,
            affectedOutputs: new[] { request.BeforeOutputKey, request.AfterOutputKey }, paths: paths, scope: scope);
    }
}
