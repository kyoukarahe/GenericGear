using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public static partial class CrankSliderOutputComparer
{
    public static CrankSliderOutputEquivalenceResult Compare(CrankSliderAnalysis before, CrankSliderAnalysis after, CrankSliderOutputComparisonRequest request)
    {
        if (before is null || after is null) throw new ArgumentNullException("Both current analyses are required.");
        return CompareCore(TerminalComparisonViews.From(before), TerminalComparisonViews.From(after), request);
    }

    internal static CrankSliderOutputEquivalenceResult CompareCore(TerminalComparisonView<CrankSliderMotionDescriptor, PrismaticOutputDefinition, PlanarCrankSliderDefinition> before,
        TerminalComparisonView<CrankSliderMotionDescriptor, PrismaticOutputDefinition, PlanarCrankSliderDefinition> after, CrankSliderOutputComparisonRequest request, AssemblyNumericBudget? budget = null)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException("Both current analyses and explicit correspondence are required.");
        var issues = new List<MechanicalDiagnostic>(); var witnesses = new List<CrankSliderComparisonWitness>();
        bool? domains = before.HasDeterminedMotion && after.HasDeterminedMotion &&
            before.GuideTravelCoverage == MechanicalAxisVerdict.Pass && after.GuideTravelCoverage == MechanicalAxisVerdict.Pass ? true : (bool?)null;
        if (before.HasDeterminedMotion && after.HasDeterminedMotion &&
            (before.GuideTravelCoverage == MechanicalAxisVerdict.Pass && after.GuideTravelCoverage == MechanicalAxisVerdict.Fail ||
             after.GuideTravelCoverage == MechanicalAxisVerdict.Pass && before.GuideTravelCoverage == MechanicalAxisVerdict.Fail)) domains = false;
        CrankSliderOutputEquivalenceResult Result(CrankSliderComparisonVerdict verdict, string rule) => new(request,
            before.AnalysisId, after.AnalysisId, verdict, rule, domains, before.GuideTravelCoverage, after.GuideTravelCoverage,
            before.ExportAdmission, after.ExportAdmission, witnesses, issues);
        void Issue(string code, string detail) => issues.Add(new MechanicalDiagnostic(code, "CrankSliderComparison",
            related: new[] { new MechanicalReference("BeforeOutput", request.BeforeOutputKey), new MechanicalReference("AfterOutput", request.AfterOutputKey) },
            scope: "SliderReferenceOutputOnly", detail: detail));
        if (request.Alpha.Kind != QuantityKind.AngularPerAngular || request.Beta.Kind != QuantityKind.AngularPosition ||
            request.Delta.Kind != QuantityKind.LinearPosition || request.WitnessRoots.Any(r => r.Kind != QuantityKind.AngularPosition))
        { Issue("DimensionMismatch", "Input mapping is turns, output mapping is mm; rotary/affine output contracts are not substituted."); return Result(CrankSliderComparisonVerdict.NotComparable, "UnsupportedDimensions"); }
        if (request.Domain != "AllRootTurns" || request.ProofPolicy != Policy)
        { Issue("UnsupportedComparisonDomain", "This version proves full functions only under its explicit bounded policy."); return Result(CrankSliderComparisonVerdict.NotComparable, "UnsupportedPolicyOrDomain"); }
        if (before.SelectedInputId != request.BeforeInputId || after.SelectedInputId != request.AfterInputId)
        { Issue("InputMappingUnavailable", "Caller input IDs must name the actually selected prescribed source inputs."); return Result(CrankSliderComparisonVerdict.NotComparable, "MissingInputCorrespondence"); }
        if (before.Output.Key != request.BeforeOutputKey || after.Output.Key != request.AfterOutputKey)
        {
            var rotary = before.SourceOutputKeys.Contains(request.BeforeOutputKey) || after.SourceOutputKeys.Contains(request.AfterOutputKey);
            Issue(rotary ? "DimensionMismatch" : "MissingEndpoint", "Only the explicitly bound nonlinear slider output is supported.");
            return Result(CrankSliderComparisonVerdict.NotComparable, "MissingOutputCorrespondence");
        }
        if (!before.HasDeterminedMotion || !after.HasDeterminedMotion)
        { Issue("NonlinearMotionNotDetermined", "Missing or invalid prerequisites do not define an absolute slider function."); return Result(CrankSliderComparisonVerdict.NotComparable, "UnavailableMotion"); }
        if (request.BeforeReferencePointId is not null && request.BeforeReferencePointId != before.Output.ReferencePointId ||
            request.AfterReferencePointId is not null && request.AfterReferencePointId != after.Output.ReferencePointId)
        { Issue("ReferencePointMappingUnavailable", "Any supplied reference ID must identify the actual slider reference, including scalar comparisons."); return Result(CrankSliderComparisonVerdict.NotComparable, "MissingReferenceCorrespondence"); }
        if (request.Mode == CrankSliderOutputComparisonMode.WorldSliderPath &&
            (request.BeforeReferencePointId != before.Output.ReferencePointId ||
             request.AfterReferencePointId != after.Output.ReferencePointId ||
             request.AfterWorldToBeforeWorldMm?.IsProperCardinal != true))
        { Issue("ReferencePointMappingUnavailable", "World slider-pin comparison requires both reference IDs and an explicit proper cardinal world mapping."); return Result(CrankSliderComparisonVerdict.NotComparable, "MissingWorldCorrespondence"); }

        var a = before.Descriptor!; var b = after.Descriptor!;
        try
        {
            var qa = a.PhysicalPhaseRelation.Coefficient; var pa = a.PhysicalPhaseRelation.Phase;
            var qb = b.PhysicalPhaseRelation.Coefficient * request.Alpha.Value;
            var pb = b.PhysicalPhaseRelation.Coefficient * request.Beta.Value + b.PhysicalPhaseRelation.Phase;
            MechanicalDerivedNumbers.Check(qb); MechanicalDerivedNumbers.Check(pb);
            var directPhase = qa == qb && Reduced(pa) == Reduced(pb);
            NormalizePhase(a.GuideOffsetMm, ref qa, ref pa); NormalizePhase(b.GuideOffsetMm, ref qb, ref pb);
            var sameShapePhase = a.CrankRadiusMm == b.CrankRadiusMm && a.RodLengthMm == b.RodLengthMm &&
                a.GuideOffsetMm == b.GuideOffsetMm && qa == qb && pa == pb;
            bool same;
            if (request.Mode == CrankSliderOutputComparisonMode.SliderScalar)
            {
                var sa = a.TerminalSign; var sb = request.Sign * b.TerminalSign;
                var ca = a.TerminalDatumMm - sa * a.GuideDatumMm;
                var cb = request.Sign * (b.TerminalDatumMm - b.TerminalSign * b.GuideDatumMm) + request.Delta.Value;
                MechanicalDerivedNumbers.Check(ca); MechanicalDerivedNumbers.Check(cb);
                same = sameShapePhase && sa == sb && sa * a.Branch == sb * b.Branch && ca == cb;
            }
            else
            {
                var mapping = request.AfterWorldToBeforeWorldMm!;
                var ga = a.GuideDirection; var gb = mapping.Vector(b.GuideDirection);
                var ca = a.GuideOriginMm - a.GuideDirection * a.GuideDatumMm;
                var cb = mapping.Point(b.GuideOriginMm) - gb * b.GuideDatumMm;
                foreach (var value in new[] { ca.X, ca.Y, ca.Z, cb.X, cb.Y, cb.Z }) MechanicalDerivedNumbers.Check(value);
                same = sameShapePhase && ga == gb && a.Branch == b.Branch && ca == cb;
            }
            if (same)
            {
                Issue("SliderOutputEquivalent", "Exact normalized position function; this does not compare crank/rod motion, materials, clearance or export admission.");
                return Result(CrankSliderComparisonVerdict.Equivalent, directPhase ? "NormalizedLengthClosureFunction" : "InlinePhaseReflection");
            }
            // Lack of a normalization proof is not a counterexample. Test only the
            // caller's ordered finite witness set; agreement cannot yield Equivalent.
            foreach (var root in request.WitnessRoots)
            {
                var otherRoot = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, request.Alpha.Value * root.Value + request.Beta.Value);
                var ea = before.CrankEvaluation!(root, budget?.CrankRequest ?? request.NumericRequest);
                budget?.Debit(ea.Numeric?.Work ?? 0);
                var eb = after.CrankEvaluation!(otherRoot, budget?.CrankRequest ?? request.NumericRequest);
                budget?.Debit(eb.Numeric?.Work ?? 0);
                if (!ea.Admitted || !eb.Admitted || ea.Numeric?.Pose is null || eb.Numeric?.Pose is null) continue;
                var va = ea.Numeric.Pose; var vb = eb.Numeric.Pose;
                if (request.Mode == CrankSliderOutputComparisonMode.SliderScalar)
                {
                    var mapped = Affine(vb.TerminalPositionMm, request.Sign, request.Delta.Value);
                    var witness = new CrankSliderComparisonWitness(root, otherRoot, "terminal.mm", va.TerminalPositionMm, mapped);
                    if (witness.IsSeparating) witnesses.Add(witness);
                }
                else
                {
                    var mapped = MapPoint(vb.SliderPinMm, request.AfterWorldToBeforeWorldMm!);
                    var left = new[] { va.SliderPinMm.X, va.SliderPinMm.Y, va.SliderPinMm.Z };
                    var right = new[] { mapped.X, mapped.Y, mapped.Z };
                    var names = new[] { "world.X.mm", "world.Y.mm", "world.Z.mm" };
                    for (var i = 0; i < 3; i++)
                    {
                        var witness = new CrankSliderComparisonWitness(root, otherRoot, names[i], left[i], right[i]);
                        if (witness.IsSeparating) witnesses.Add(witness);
                    }
                }
                if (witnesses.Count != 0)
                {
                    Issue("NonlinearOutputDifferent", "Nonoverlapping certified intervals at valid explicitly mapped inputs establish a counterexample.");
                    return Result(CrankSliderComparisonVerdict.Different, "CertifiedSeparatingWitness");
                }
            }
            Issue("ComparisonInconclusive", "No supported normalized-function proof or separating valid witness was established. Sample agreement is not a function proof.");
            return Result(CrankSliderComparisonVerdict.Inconclusive, "BoundedProofRulesExhausted");
        }
        catch (ArgumentException error) when (error.Message == "Derived exact digit bound exceeded." || error.Message == "Exact quantity resource digit bound exceeded." ||
            error.Message == "Exact input digit bound exceeded.")
        {
            Issue("ComparisonNumericResourceLimit", "Exact mapped comparison representation exceeded the unchanged resource ceiling.");
            return Result(CrankSliderComparisonVerdict.Inconclusive, "ComparisonResourceLimit");
        }
    }

    private static Rational Reduced(Rational value)
    {
        var n = value.Numerator % value.Denominator;
        if (n.Sign < 0) n += value.Denominator;
        return new Rational(n, value.Denominator);
    }
    private static void NormalizePhase(Rational offset, ref Rational q, ref Rational p)
    {
        if (offset.IsZero && q.Sign < 0) { q = -q; p = -p; }
        p = Reduced(p);
    }
    private static CrankSliderInterval Affine(CrankSliderInterval source, Rational coefficient, Rational offset)
    {
        var a = source.Lower * coefficient + offset; var b = source.Upper * coefficient + offset;
        return a <= b ? new(a, b) : new(b, a);
    }
    private static CrankSliderVectorInterval MapPoint(CrankSliderVectorInterval value, OrientedFrame frame)
    {
        CrankSliderInterval Component(Rational origin, Rational x, Rational y, Rational z)
        {
            var a = Affine(value.X, x, 0); var b = Affine(value.Y, y, 0); var c = Affine(value.Z, z, 0);
            return new(origin + a.Lower + b.Lower + c.Lower, origin + a.Upper + b.Upper + c.Upper);
        }
        return new(Component(frame.Origin.X, frame.X.X, frame.Y.X, frame.Z.X),
            Component(frame.Origin.Y, frame.X.Y, frame.Y.Y, frame.Z.Y), Component(frame.Origin.Z, frame.X.Z, frame.Y.Z, frame.Z.Z));
    }
}
