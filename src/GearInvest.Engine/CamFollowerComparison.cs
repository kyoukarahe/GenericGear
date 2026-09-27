using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum CamFollowerOutputComparisonMode { FollowerScalar, WorldFollowerPath }
public enum CamFollowerComparisonVerdict { Equivalent, Different, Inconclusive, NotComparable }

/// <summary>Explicit caller mapping, not a search for phase or a silently inferred geometric correspondence.</summary>
public sealed class CamFollowerOutputComparisonRequest
{
    public CamFollowerOutputComparisonRequest(string beforeOutputKey, string afterOutputKey, string beforeInputId, string afterInputId,
        ExactQuantity? alpha = null, ExactQuantity? beta = null, int sign = 1, ExactQuantity? delta = null,
        CamFollowerOutputComparisonMode mode = CamFollowerOutputComparisonMode.FollowerScalar,
        string? beforeReferencePointId = null, string? afterReferencePointId = null, OrientedFrame? afterWorldToBeforeWorldMm = null,
        IEnumerable<ExactQuantity>? witnessRoots = null, CamNumericRequest? numericRequest = null,
        string domain = "AllRootTurns", string proofPolicy = CamFollowerOutputComparer.Policy)
    {
        BeforeOutputKey = MechanicalAuthoringProfile.IdValue(beforeOutputKey); AfterOutputKey = MechanicalAuthoringProfile.IdValue(afterOutputKey);
        BeforeInputId = MechanicalAuthoringProfile.IdValue(beforeInputId); AfterInputId = MechanicalAuthoringProfile.IdValue(afterInputId);
        Alpha = alpha ?? ExactQuantity.TurnsPerTurn(1); Beta = beta ?? ExactQuantity.Turns(0); Sign = sign; Delta = delta ?? ExactQuantity.Millimeters(0);
        foreach (var q in new[] { Alpha, Beta, Delta }) RotaryLinearProfile.Quantity(q);
        if (Alpha.Value.IsZero || sign != -1 && sign != 1) throw new ArgumentException("Nonzero explicit input mapping and terminal sign +/-1 required.");
        if (!Enum.IsDefined(typeof(CamFollowerOutputComparisonMode), mode)) throw new ArgumentException("Unsupported cam-follower comparison mode.");
        Mode = mode; Domain = MechanicalAuthoringProfile.IdValue(domain); ProofPolicy = MechanicalAuthoringProfile.IdValue(proofPolicy);
        BeforeReferencePointId = beforeReferencePointId is null ? null : MechanicalAuthoringProfile.IdValue(beforeReferencePointId);
        AfterReferencePointId = afterReferencePointId is null ? null : MechanicalAuthoringProfile.IdValue(afterReferencePointId);
        if (afterWorldToBeforeWorldMm is not null) MechanicalAuthoringProfile.FrameBound(afterWorldToBeforeWorldMm);
        AfterWorldToBeforeWorldMm = afterWorldToBeforeWorldMm; NumericRequest = numericRequest ?? CamNumericRequest.Default;
        var roots = (witnessRoots ?? new[] { ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(1,4)), ExactQuantity.Turns(new Rational(1,2)),
            ExactQuantity.Turns(new Rational(3,4)), ExactQuantity.Turns(1), ExactQuantity.Turns(new Rational(-1,4)) }).Take(33).ToArray();
        if (roots.Length > 32) throw new ArgumentException("At most32 explicit comparison witness roots are supported.");
        foreach (var root in roots) RotaryLinearProfile.Quantity(root);
        WitnessRoots = Array.AsReadOnly(roots); RequestId = HashText(CanonicalRepresentation);
    }
    public string BeforeOutputKey { get; } public string AfterOutputKey { get; } public string BeforeInputId { get; } public string AfterInputId { get; }
    public ExactQuantity Alpha { get; } public ExactQuantity Beta { get; } public int Sign { get; } public ExactQuantity Delta { get; }
    public CamFollowerOutputComparisonMode Mode { get; } public string Domain { get; } public string ProofPolicy { get; }
    public string? BeforeReferencePointId { get; } public string? AfterReferencePointId { get; } public OrientedFrame? AfterWorldToBeforeWorldMm { get; }
    public ReadOnlyCollection<ExactQuantity> WitnessRoots { get; } public CamNumericRequest NumericRequest { get; } public string RequestId { get; }
    public string CanonicalRepresentation => Pack(BeforeOutputKey, AfterOutputKey, BeforeInputId, AfterInputId,
        CamFollowerProfile.Q(Alpha), CamFollowerProfile.Q(Beta), N(Sign), CamFollowerProfile.Q(Delta), Mode.ToString(), Domain, ProofPolicy,
        BeforeReferencePointId ?? "", AfterReferencePointId ?? "", AfterWorldToBeforeWorldMm is null ? "" : Frame(AfterWorldToBeforeWorldMm),
        Pack(WitnessRoots.Select(CamFollowerProfile.Q).ToArray()), NumericRequest.CanonicalRepresentation);
}

public sealed class CamFollowerComparisonWitness
{
    internal CamFollowerComparisonWitness(ExactQuantity beforeRoot, ExactQuantity afterRoot, string coordinate, Rational before, Rational after)
    { BeforeRoot = beforeRoot; AfterRoot = afterRoot; Coordinate = coordinate; Before = new(before, before); MappedAfter = new(after, after); }
    public ExactQuantity BeforeRoot { get; } public ExactQuantity AfterRoot { get; } public string Coordinate { get; }
    public CamInterval Before { get; } public CamInterval MappedAfter { get; }
    public bool IsSeparating => Before.Upper < MappedAfter.Lower || MappedAfter.Upper < Before.Lower;
    public string CanonicalRepresentation => Pack(CamFollowerProfile.Q(BeforeRoot), CamFollowerProfile.Q(AfterRoot), Coordinate, Before.CanonicalRepresentation, MappedAfter.CanonicalRepresentation);
}
public sealed class CamFollowerOutputEquivalenceResult
{
    internal CamFollowerOutputEquivalenceResult(CamFollowerOutputComparisonRequest request, TerminalComparisonView<CamFollowerMotionDescriptor, PrismaticOutputDefinition, FlatCamFollowerDefinition> before,
        TerminalComparisonView<CamFollowerMotionDescriptor, PrismaticOutputDefinition, FlatCamFollowerDefinition> after,
        CamFollowerComparisonVerdict verdict, string rule, IEnumerable<CamFollowerComparisonWitness> witnesses, IEnumerable<MechanicalDiagnostic> diagnostics,
        IEnumerable<string>? proofCells = null)
    {
        Request = request; BeforeAnalysisId = before.AnalysisId; AfterAnalysisId = after.AnalysisId; Verdict = verdict; ProofRule = rule;
        BeforeFullCycleTravel = before.GuideTravelCoverage; AfterFullCycleTravel = after.GuideTravelCoverage;
        BeforeFullCycleFace = before.FaceCoverage; AfterFullCycleFace = after.FaceCoverage;
        BeforeExportAdmission = before.ExportAdmission; AfterExportAdmission = after.ExportAdmission;
        var ba = BeforeFullCycleTravel == MechanicalAxisVerdict.Pass && BeforeFullCycleFace == MechanicalAxisVerdict.Pass;
        var aa = AfterFullCycleTravel == MechanicalAxisVerdict.Pass && AfterFullCycleFace == MechanicalAxisVerdict.Pass;
        OperatingDomainsEqual = ba && aa ? true : ba && (AfterFullCycleTravel == MechanicalAxisVerdict.Fail || AfterFullCycleFace == MechanicalAxisVerdict.Fail) ||
            aa && (BeforeFullCycleTravel == MechanicalAxisVerdict.Fail || BeforeFullCycleFace == MechanicalAxisVerdict.Fail) ? false : (bool?)null;
        Witnesses = witnesses.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly();
        ProofCells = (proofCells ?? Array.Empty<string>()).ToList().AsReadOnly();
        ResultId = HashText(Pack(request.RequestId, BeforeAnalysisId, AfterAnalysisId, Verdict.ToString(), ProofRule, OperatingDomainsEqual?.ToString() ?? "",
            BeforeFullCycleTravel.ToString(), AfterFullCycleTravel.ToString(), BeforeFullCycleFace.ToString(), AfterFullCycleFace.ToString(),
            BeforeExportAdmission.ToString(), AfterExportAdmission.ToString(), Pack(Witnesses.Select(w => w.CanonicalRepresentation).ToArray()), Pack(ProofCells.ToArray())));
    }
    public CamFollowerOutputComparisonRequest Request { get; } public string BeforeAnalysisId { get; } public string AfterAnalysisId { get; } public string ResultId { get; }
    public CamFollowerComparisonVerdict Verdict { get; } public string ProofRule { get; } public string Policy => CamFollowerOutputComparer.Policy;
    public string Scope => "FollowerReferenceOutputOnly"; public bool? OperatingDomainsEqual { get; }
    public MechanicalAxisVerdict BeforeFullCycleTravel { get; } public MechanicalAxisVerdict AfterFullCycleTravel { get; }
    public MechanicalAxisVerdict BeforeFullCycleFace { get; } public MechanicalAxisVerdict AfterFullCycleFace { get; }
    public MechanicalExportAdmission BeforeExportAdmission { get; } public MechanicalExportAdmission AfterExportAdmission { get; }
    public ReadOnlyCollection<CamFollowerComparisonWitness> Witnesses { get; } public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    /// <summary>Exact common-cell coefficient identities, not samples or authored segment IDs.</summary>
    public ReadOnlyCollection<string> ProofCells { get; }
}

public static class CamFollowerOutputComparer
{
    public const string Policy = "cam-common-partition-polynomial-proof-v1";
    public static CamFollowerOutputEquivalenceResult Compare(CamFollowerAnalysis before, CamFollowerAnalysis after, CamFollowerOutputComparisonRequest request)
    {
        if (before is null || after is null) throw new ArgumentNullException("Both current analyses are required.");
        return CompareCore(TerminalComparisonViews.From(before), TerminalComparisonViews.From(after), request);
    }

    internal static CamFollowerOutputEquivalenceResult CompareCore(TerminalComparisonView<CamFollowerMotionDescriptor, PrismaticOutputDefinition, FlatCamFollowerDefinition> before,
        TerminalComparisonView<CamFollowerMotionDescriptor, PrismaticOutputDefinition, FlatCamFollowerDefinition> after, CamFollowerOutputComparisonRequest request, AssemblyNumericBudget? budget = null)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException("Both current analyses and explicit mapping are required.");
        var diagnostics = new List<MechanicalDiagnostic>(); var witnesses = new List<CamFollowerComparisonWitness>(); var cells = new List<string>();
        CamFollowerOutputEquivalenceResult Result(CamFollowerComparisonVerdict verdict, string rule, string detail)
        {
            diagnostics.Add(new(rule, "CamFollowerComparison", scope: "FollowerReferenceOutputOnly", detail: detail));
            return new(request, before, after, verdict, rule, witnesses, diagnostics, verdict == CamFollowerComparisonVerdict.Equivalent ? cells : null);
        }
        if (request.Domain != "AllRootTurns" || request.ProofPolicy != Policy)
            return Result(CamFollowerComparisonVerdict.NotComparable, "UnsupportedPolicyOrDomain", "This bounded policy compares functions over all root turns.");
        if (request.Alpha.Kind != QuantityKind.AngularPerAngular || request.Beta.Kind != QuantityKind.AngularPosition || request.Delta.Kind != QuantityKind.LinearPosition || request.WitnessRoots.Any(r => r.Kind != QuantityKind.AngularPosition))
            return Result(CamFollowerComparisonVerdict.NotComparable, "DimensionMismatch", "Root correspondence uses turns and terminal calibration uses mm.");
        if (before.SelectedInputId != request.BeforeInputId || after.SelectedInputId != request.AfterInputId)
            return Result(CamFollowerComparisonVerdict.NotComparable, "MissingInputCorrespondence", "Input IDs must name the actual prescribed source roots.");
        if (before.Output.Key != request.BeforeOutputKey || after.Output.Key != request.AfterOutputKey)
            return Result(CamFollowerComparisonVerdict.NotComparable, "MissingOutputCorrespondence", "Only actual bound nonlinear follower outputs are supported.");
        if (!before.HasDeterminedMotion || !after.HasDeterminedMotion)
            return Result(CamFollowerComparisonVerdict.NotComparable, "UnavailableMotion", "Current source, strict global support contact and grounding must establish both functions.");
        if (request.BeforeReferencePointId is not null && request.BeforeReferencePointId != before.Output.ReferencePointId ||
            request.AfterReferencePointId is not null && request.AfterReferencePointId != after.Output.ReferencePointId)
            return Result(CamFollowerComparisonVerdict.NotComparable, "MissingReferenceCorrespondence", "Supplied reference IDs must identify the actual follower references.");
        if (request.Mode == CamFollowerOutputComparisonMode.WorldFollowerPath &&
            (request.BeforeReferencePointId != before.Output.ReferencePointId || request.AfterReferencePointId != after.Output.ReferencePointId || request.AfterWorldToBeforeWorldMm?.IsProperCardinal != true))
            return Result(CamFollowerComparisonVerdict.NotComparable, "MissingWorldCorrespondence", "World comparison requires explicit reference IDs and a proper cardinal after-to-before map.");
        var a = before.Descriptor!; var b = after.Descriptor!;
        try
        {
            var q = b.PhysicalPhaseRelation.Coefficient * request.Alpha.Value;
            var p = b.PhysicalPhaseRelation.Coefficient * request.Beta.Value + b.PhysicalPhaseRelation.Phase;
            MechanicalDerivedNumbers.Check(q); MechanicalDerivedNumbers.Check(p);
            if (a.PhysicalPhaseRelation.Coefficient == q && Reduce(a.PhysicalPhaseRelation.Phase) == Reduce(p))
            {
                var boundaries = a.Device.SupportProfile.Segments.SelectMany(s => new[] { s.StartTurns, s.EndTurns })
                    .Concat(b.Device.SupportProfile.Segments.SelectMany(s => new[] { s.StartTurns, s.EndTurns })).Distinct().OrderBy(x => x).ToArray();
                var identical = true;
                for (var i = 0; i + 1 < boundaries.Length; i++)
                {
                    var start = boundaries[i]; var end = boundaries[i + 1]; var mid = (start + end) / 2;
                    var sa = a.Device.SupportProfile.Segments.Single(s => s.StartTurns <= mid && mid < s.EndTurns);
                    var sb = b.Device.SupportProfile.Segments.Single(s => s.StartTurns <= mid && mid < s.EndTurns);
                    var ha = PolynomialOnCell(sa, start, end, budget); var hb = PolynomialOnCell(sb, start, end, budget);
                    var pa = OutputPolynomials(a, ha, request, false, budget); var pb = OutputPolynomials(b, hb, request, true, budget);
                    if (!pa.SelectMany(x => x).SequenceEqual(pb.SelectMany(x => x))) { identical = false; break; }
                    cells.Add(Pack(F(start), F(end), Pack(pa.Select(x => Pack(x.Select(F).ToArray())).ToArray())));
                }
                if (identical) return Result(CamFollowerComparisonVerdict.Equivalent, "ExactCommonPartitionPolynomialIdentity",
                    "Explicit mapped physical phases and every coefficient agree on the complete partition. This does not compare contact footprints, material bodies, forces or export admission.");
            }
            foreach (var root in request.WitnessRoots)
            {
                var otherRoot = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, request.Alpha.Value * root.Value + request.Beta.Value);
                var left = a.At(root); var right = b.At(otherRoot);
                if (request.Mode == CamFollowerOutputComparisonMode.FollowerScalar)
                {
                    var value = request.Sign * right.TerminalPosition.Value + request.Delta.Value; MechanicalDerivedNumbers.Check(value);
                    if (left.TerminalPosition.Value != value) witnesses.Add(new(root, otherRoot, "terminal.mm", left.TerminalPosition.Value, value));
                }
                else
                {
                    var actual = left.FollowerReferencePointMm; var mapped = request.AfterWorldToBeforeWorldMm!.Point(right.FollowerReferencePointMm);
                    var av = new[] { actual.X, actual.Y, actual.Z }; var bv = new[] { mapped.X, mapped.Y, mapped.Z };
                    for (var i = 0; i < 3; i++)
                    { MechanicalDerivedNumbers.Check(bv[i]); if (av[i] != bv[i]) witnesses.Add(new(root, otherRoot, "world." + new[] { "X", "Y", "Z" }[i] + ".mm", av[i], bv[i])); }
                }
                if (witnesses.Count > 0) return Result(CamFollowerComparisonVerdict.Different, "ExactSeparatingWitness", "Exact positions differ at the recorded explicitly mapped roots. Operating-domain admission remains separately reported.");
            }
            return Result(CamFollowerComparisonVerdict.Inconclusive, "BoundedProofRulesExhausted", "No supported function proof or exact counterexample; finite sample agreement is not equivalence. No reflection or hidden phase search is assumed.");
        }
        catch (Exception e) when (e is ArgumentException || e is CrankSliderNumerics.Stop)
        {
            if (e is ArgumentException && e.Message != "Derived exact digit bound exceeded." && e.Message != "Exact quantity resource digit bound exceeded." && e.Message != "Exact input digit bound exceeded." && e.InnerException is not CrankSliderNumerics.Stop) throw;
            return Result(CamFollowerComparisonVerdict.Inconclusive, "ComparisonResourceLimit", "The bounded exact coefficient or mapped-root representation limit was reached.");
        }
    }

    private static Rational Reduce(Rational x)
    { var n = x.Numerator % x.Denominator; if (n.Sign < 0) n += x.Denominator; return new(n, x.Denominator); }
    private static Rational[] PolynomialOnCell(CamSupportSegment segment, Rational start, Rational end, AssemblyNumericBudget? budget = null)
    {
        var c = new CrankSliderNumerics.Context(budget?.RemainingWork ?? 8192);
        try
        {
        var width = c.Subtract(segment.EndTurns, segment.StartTurns); var s = c.Divide(c.Subtract(start, segment.StartTurns), width); var w = c.Divide(c.Subtract(end, start), width);
        var delta = c.Subtract(segment.EndHeight.Value, segment.StartHeight.Value);
        var coefficients = segment.Kind == CamSupportSegmentKind.Dwell ? new Rational[] { segment.StartHeight.Value, 0, 0, 0, 0, 0 } :
            new Rational[] { segment.StartHeight.Value, 0, 0, c.Multiply(10, delta), c.Multiply(-15, delta), c.Multiply(6, delta) };
        var result = new Rational[6]; var power = new Rational[] { 1, 0, 0, 0, 0, 0 };
        for (var degree = 0; degree <= 5; degree++)
        {
            for (var j = 0; j <= degree; j++) result[j] = c.Add(result[j], c.Multiply(coefficients[degree], power[j]));
            if (degree == 5) break;
            var next = new Rational[6];
            for (var j = 0; j <= degree; j++) { next[j] = c.Add(next[j], c.Multiply(power[j], s)); next[j + 1] = c.Add(next[j + 1], c.Multiply(power[j], w)); }
            power = next;
        }
        return result;
        }
        finally { budget?.Debit(c.Work); }
    }
    private static Rational[][] OutputPolynomials(CamFollowerMotionDescriptor d, Rational[] height, CamFollowerOutputComparisonRequest request, bool after, AssemblyNumericBudget? budget = null)
    {
        if (request.Mode == CamFollowerOutputComparisonMode.FollowerScalar)
        {
            var sign = d.TerminalSign * (after ? request.Sign : 1);
            var offset = (after ? request.Sign : 1) * (d.TerminalDatumMm - d.TerminalSign * d.GuideDatumMm) + (after ? request.Delta.Value : 0);
            return new[] { Affine(height, sign, offset, budget) };
        }
        var g = d.GuideDirection; var p = d.CenterMm + d.InPlanePerpendicular * d.GuideOffsetMm;
        if (after) { g = request.AfterWorldToBeforeWorldMm!.Vector(g); p = request.AfterWorldToBeforeWorldMm.Point(p); }
        return new[] { Affine(height, g.X, p.X, budget), Affine(height, g.Y, p.Y, budget), Affine(height, g.Z, p.Z, budget) };
    }
    private static Rational[] Affine(Rational[] polynomial, Rational coefficient, Rational offset, AssemblyNumericBudget? budget = null)
    {
        var c = new CrankSliderNumerics.Context(budget?.RemainingWork ?? 8192);
        try { var result = polynomial.Select(x => c.Multiply(x, coefficient)).ToArray(); result[0] = c.Add(result[0], offset); return result; }
        finally { budget?.Debit(c.Work); }
    }
}
