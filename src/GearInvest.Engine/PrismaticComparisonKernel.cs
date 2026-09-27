using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Read-only observation adapter. It never manufactures a fake screw/rack declaration or changes either stored representation.</summary>
internal sealed class PrismaticObservation
{
    private PrismaticObservation(string analysisId, string? selectedInputId, MechanicalAnalysis source, string outputKey,
        string referencePointId, bool determined, DimensionedAffineRelation? terminal, ExactVector3? worldGain,
        ExactPiVector3? worldOffset, ExactQuantityInterval? validInterval, IEnumerable<MechanicalDiagnostic> diagnostics, bool legacyRationalFacts)
    {
        AnalysisId = analysisId; SelectedInputId = selectedInputId; Source = source; OutputKey = outputKey; ReferencePointId = referencePointId;
        Determined = determined; Terminal = terminal; WorldGain = worldGain; WorldOffset = worldOffset;
        ValidInterval = validInterval; Diagnostics = diagnostics.ToArray(); LegacyRationalFacts = legacyRationalFacts;
    }
    internal string AnalysisId { get; }
    internal string? SelectedInputId { get; }
    internal MechanicalAnalysis Source { get; }
    internal string OutputKey { get; }
    internal string ReferencePointId { get; }
    internal bool Determined { get; }
    internal DimensionedAffineRelation? Terminal { get; }
    internal ExactVector3? WorldGain { get; }
    internal ExactPiVector3? WorldOffset { get; }
    internal ExactQuantityInterval? ValidInterval { get; }
    internal MechanicalDiagnostic[] Diagnostics { get; }
    internal bool LegacyRationalFacts { get; }
    internal static PrismaticObservation From(RotaryLinearAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); var o = analysis.LinearOutput;
        return new PrismaticObservation(analysis.AnalysisId, analysis.SelectedInputId, analysis.SourceAnalysis, o.OutputKey,
            o.Binding.ReferencePointId, o.HasDeterminedMotion, o.TerminalRelation, o.WorldGainMmPerRootTurn,
            o.WorldOffsetMm.HasValue ? ExactPiVector3.FromMillimeters(o.WorldOffsetMm.Value) : null, o.ValidRootInterval, o.Diagnostics, true);
    }
    internal static PrismaticObservation From(RackPinionAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); var o = analysis.LinearOutput;
        return new PrismaticObservation(analysis.AnalysisId, analysis.SelectedInputId, analysis.SourceAnalysis, o.OutputKey,
            o.Binding.ReferencePointId, o.HasDeterminedMotion, o.TerminalRelation, o.WorldGainMmPerRootTurn, o.WorldOffsetMm, o.ValidRootInterval, o.Diagnostics, false);
    }
}

/// <summary>One exact input/terminal/world/domain comparison kernel for legacy screw, CP rack and explicit cross-device pairs.</summary>
internal static class PrismaticComparisonKernel
{
    internal static PrismaticOutputEquivalenceResult Compare(PrismaticObservation before, PrismaticObservation after, LinearOutputComparisonRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var diagnostics = new List<MechanicalDiagnostic>(); DimensionedAffineRelation? a = null, b = null;
        ExactVector3? ag = null, bg = null; ExactPiVector3? ab = null, bb = null; ExactQuantityInterval? ad = null, bd = null;
        var domainsEqual = false; bool? feasible = null;
        PrismaticOutputEquivalenceResult Result(OutputEquivalenceVerdict verdict) => new(request, verdict, before.AnalysisId, after.AnalysisId,
            a, b, ag, bg, ab, bb, ad, bd, domainsEqual, feasible, diagnostics);
        void Issue(string code, IEnumerable<MechanicalExactFact>? facts = null) => diagnostics.Add(new MechanicalDiagnostic(code, "LinearOutputComparison",
            related: new[] { new MechanicalReference("BeforeOutput", request.BeforeOutputKey), new MechanicalReference("AfterOutput", request.AfterOutputKey) },
            facts: facts, scope: request.Mode.ToString()));
        if (request.Alpha.Kind != QuantityKind.AngularPerAngular || request.Beta.Kind != QuantityKind.AngularPosition || request.Delta.Kind != QuantityKind.LinearPosition ||
            request.ComparisonInterval is not null && request.ComparisonInterval.Kind != QuantityKind.AngularPosition || request.Mode == LinearOutputComparisonMode.AngleModuloOne)
        { Issue("DimensionMismatch"); return Result(OutputEquivalenceVerdict.NotComparable); }
        if (request.BeforeInputId != before.SelectedInputId || request.AfterInputId != after.SelectedInputId)
        { Issue("InputMappingUnavailable"); return Result(OutputEquivalenceVerdict.NotComparable); }
        if (before.OutputKey != request.BeforeOutputKey || after.OutputKey != request.AfterOutputKey)
        {
            var rotation = before.Source.Outputs.Any(o => o.OutputKey == request.BeforeOutputKey) || after.Source.Outputs.Any(o => o.OutputKey == request.AfterOutputKey);
            Issue(rotation ? "DimensionMismatch" : "MissingEndpoint"); return Result(OutputEquivalenceVerdict.NotComparable);
        }
        if (!before.Determined || !after.Determined)
        { Issue("LinearMotionNotDetermined"); diagnostics.AddRange(!before.Determined ? before.Diagnostics : after.Diagnostics); return Result(OutputEquivalenceVerdict.NotComparable); }
        if (request.Mode == LinearOutputComparisonMode.WorldLinearPath &&
            (request.BeforeReferencePointId != before.ReferencePointId || request.AfterReferencePointId != after.ReferencePointId))
        { Issue("ReferencePointMappingUnavailable"); return Result(OutputEquivalenceVerdict.NotComparable); }
        a = before.Terminal;
        var raw = after.Terminal!.Value;
        b = PrismaticAlgebra.LinearRelation(request.Sign * raw.Gain.Value * request.Alpha.Value,
            request.Sign * (raw.Gain.Value * request.Beta.Value + raw.Offset.Value) + request.Delta.Value);
        ag = before.WorldGain; bg = after.WorldGain!.Value * request.Alpha.Value;
        ab = before.WorldOffset;
        bb = ExactPiVector3.FromCanonical(after.WorldOffset!.Value.RationalPartMm + after.WorldGain.Value * request.Beta.Value,
            after.WorldOffset.Value.InversePiCoefficientMm);
        PrismaticAlgebra.Bound(bg.Value); PrismaticAlgebra.Bound(bb.Value);
        ad = before.ValidInterval;
        bd = after.ValidInterval?.InverseAffine(request.Alpha.Value, request.Beta.Value, QuantityKind.AngularPosition);
        // A determined law with no interval has an empty executable set, not unknown motion.
        domainsEqual = ad is null ? bd is null : ad.Equals(bd);
        if (request.ComparisonInterval is not null)
        {
            feasible = ad is not null && bd is not null && ad.Contains(request.ComparisonInterval) && bd.Contains(request.ComparisonInterval);
            if (!feasible.Value) Issue("ComparisonOperatingRangeMismatch");
        }
        var equal = request.Mode == LinearOutputComparisonMode.WorldLinearRate ? ag == bg
            : request.Mode == LinearOutputComparisonMode.WorldLinearPath ? ag == bg && ab == bb : a!.Value.Equals(b!.Value);
        if (!equal)
        {
            IEnumerable<MechanicalExactFact> facts;
            if (request.Mode == LinearOutputComparisonMode.FullLinearAffineOutput)
                facts = new[] { new MechanicalExactFact("terminalGain.mmPerRootTurn", a!.Value.Gain.Value, b!.Value.Gain.Value),
                    new MechanicalExactFact("terminalOffset.mm", a.Value.Offset.Value, b.Value.Offset.Value) };
            else
            {
                var worldFacts = new List<MechanicalExactFact> { new("worldGainX.mmPerRootTurn", ag!.Value.X, bg!.Value.X),
                    new("worldGainY.mmPerRootTurn", ag.Value.Y, bg.Value.Y), new("worldGainZ.mmPerRootTurn", ag.Value.Z, bg.Value.Z) };
                if (before.LegacyRationalFacts && after.LegacyRationalFacts)
                {
                    // Preserve the public/wire 22A diagnostic keys and coefficient meanings byte-for-byte.
                    worldFacts.Add(new MechanicalExactFact("worldOffsetX.mm", ab!.Value.RationalPartMm.X, bb!.Value.RationalPartMm.X));
                    worldFacts.Add(new MechanicalExactFact("worldOffsetY.mm", ab.Value.RationalPartMm.Y, bb.Value.RationalPartMm.Y));
                    worldFacts.Add(new MechanicalExactFact("worldOffsetZ.mm", ab.Value.RationalPartMm.Z, bb.Value.RationalPartMm.Z));
                }
                else
                {
                    AddPiFacts(worldFacts, "worldOffsetX.mm", ab!.Value.X, bb!.Value.X);
                    AddPiFacts(worldFacts, "worldOffsetY.mm", ab.Value.Y, bb.Value.Y);
                    AddPiFacts(worldFacts, "worldOffsetZ.mm", ab.Value.Z, bb.Value.Z);
                }
                facts = worldFacts;
            }
            Issue("LinearOutputMotionDifferent", facts);
        }
        if (!domainsEqual) diagnostics.Add(new MechanicalDiagnostic("OperatingDomainsDifferent", "LinearOutputComparison", DiagnosticSeverity.Info,
            required: false, scope: "OperatingDomainOnly", detail: "Motion-law equivalence does not imply equal executable intervals."));
        return Result(equal ? OutputEquivalenceVerdict.Equivalent : OutputEquivalenceVerdict.Different);
    }
    private static void AddPiFacts(ICollection<MechanicalExactFact> facts, string key, ExactPiLength a, ExactPiLength b)
    {
        facts.Add(new MechanicalExactFact(key + ".rationalPart", a.RationalPartMm, b.RationalPartMm));
        facts.Add(new MechanicalExactFact(key + ".inversePiCoefficient", a.InversePiCoefficientMm, b.InversePiCoefficientMm));
    }
}
