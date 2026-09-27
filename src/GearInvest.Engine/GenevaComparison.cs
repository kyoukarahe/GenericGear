using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum GenevaComparisonVerdict { Equivalent, Different, Inconclusive, NotComparable }
public enum GenevaComparisonMode { UnwrappedAngularOutput, WrappedOrientation }

/// <summary>Caller-declared root and terminal correspondence. No implicit phase or wrapped-angle search.</summary>
public sealed class GenevaMotionComparisonRequest
{
    public GenevaMotionComparisonRequest(string beforeOutputKey, string afterOutputKey, string beforeInputId, string afterInputId,
        ExactQuantity? alpha = null, ExactQuantity? beta = null, int sign = 1, ExactQuantity? delta = null,
        GenevaComparisonMode mode = GenevaComparisonMode.UnwrappedAngularOutput, IEnumerable<ExactQuantity>? witnessRoots = null,
        GenevaNumericRequest? numericRequest = null, string domain = "AllRootTurns", string proofPolicy = GenevaOutputComparer.Policy)
    {
        BeforeOutputKey = MechanicalAuthoringProfile.IdValue(beforeOutputKey); AfterOutputKey = MechanicalAuthoringProfile.IdValue(afterOutputKey);
        BeforeInputId = MechanicalAuthoringProfile.IdValue(beforeInputId); AfterInputId = MechanicalAuthoringProfile.IdValue(afterInputId);
        Alpha = alpha ?? ExactQuantity.TurnsPerTurn(1); Beta = beta ?? ExactQuantity.Turns(0); Sign = sign; Delta = delta ?? ExactQuantity.Turns(0);
        foreach (var q in new[] { Alpha, Beta, Delta }) RotaryLinearProfile.Quantity(q);
        if (Alpha.Value.IsZero || sign != -1 && sign != 1) throw new ArgumentException("Nonzero explicit root mapping and terminal sign +/-1 required.");
        if (!Enum.IsDefined(typeof(GenevaComparisonMode), mode)) throw new ArgumentException("Unknown comparison scope.");
        Mode = mode; Domain = MechanicalAuthoringProfile.IdValue(domain); ProofPolicy = MechanicalAuthoringProfile.IdValue(proofPolicy);
        NumericRequest = numericRequest ?? GenevaNumericRequest.Default;
        var roots = (witnessRoots ?? new[] { ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(1,3)), ExactQuantity.Turns(new Rational(1,4)),
            ExactQuantity.Turns(new Rational(-1,4)), ExactQuantity.Turns(new Rational(1,2)), ExactQuantity.Turns(1), ExactQuantity.Turns(-1) }).Take(33).ToArray();
        if (roots.Length > 32) throw new ArgumentException("At most 32 explicit comparison witness roots are supported.");
        foreach (var root in roots) RotaryLinearProfile.Quantity(root);
        WitnessRoots = Array.AsReadOnly(roots); RequestId = HashText(CanonicalRepresentation);
    }
    public string BeforeOutputKey { get; } public string AfterOutputKey { get; } public string BeforeInputId { get; } public string AfterInputId { get; }
    public ExactQuantity Alpha { get; } public ExactQuantity Beta { get; } public int Sign { get; } public ExactQuantity Delta { get; }
    public GenevaComparisonMode Mode { get; } public string Domain { get; } public string ProofPolicy { get; }
    public ReadOnlyCollection<ExactQuantity> WitnessRoots { get; } public GenevaNumericRequest NumericRequest { get; } public string RequestId { get; }
    public string CanonicalRepresentation => Pack(BeforeOutputKey, AfterOutputKey, BeforeInputId, AfterInputId, GenevaProfile.Q(Alpha),
        GenevaProfile.Q(Beta), N(Sign), GenevaProfile.Q(Delta), Mode.ToString(), Domain, ProofPolicy,
        Pack(WitnessRoots.Select(GenevaProfile.Q).ToArray()), NumericRequest.CanonicalRepresentation);
}

public sealed class GenevaComparisonWitness
{
    internal GenevaComparisonWitness(ExactQuantity beforeRoot, ExactQuantity afterRoot, GenevaInterval before, GenevaInterval mappedAfter, GenevaComparisonMode mode)
    {
        BeforeRoot = beforeRoot; AfterRoot = afterRoot; Before = before; MappedAfter = mappedAfter;
        var lo = before.Lower - mappedAfter.Upper; var hi = before.Upper - mappedAfter.Lower; GenevaProfile.Derived(lo, hi);
        IsSeparating = mode == GenevaComparisonMode.UnwrappedAngularOutput ? hi < 0 || lo > 0 :
            -GenevaProfile.Floor(-lo) > GenevaProfile.Floor(hi);
    }
    public ExactQuantity BeforeRoot { get; } public ExactQuantity AfterRoot { get; } public GenevaInterval Before { get; } public GenevaInterval MappedAfter { get; }
    public bool IsSeparating { get; }
    public string CanonicalRepresentation => Pack(GenevaProfile.Q(BeforeRoot), GenevaProfile.Q(AfterRoot), Before.CanonicalRepresentation, MappedAfter.CanonicalRepresentation, GenevaProfile.Flag(IsSeparating));
}

public sealed class GenevaMotionComparisonResult
{
    internal GenevaMotionComparisonResult(GenevaMotionComparisonRequest request, TerminalComparisonView<GenevaMotionDescriptor, GenevaOutputDefinition, GenevaDeviceDefinition> before,
        TerminalComparisonView<GenevaMotionDescriptor, GenevaOutputDefinition, GenevaDeviceDefinition> after,
        GenevaComparisonVerdict verdict, string rule, IEnumerable<GenevaComparisonWitness> witnesses, IEnumerable<MechanicalDiagnostic> diagnostics, int numericWork)
    {
        Request = request; BeforeAnalysisId = before.AnalysisId; AfterAnalysisId = after.AnalysisId; Verdict = verdict; ProofRule = rule;
        BeforeExportAdmission = before.ExportAdmission; AfterExportAdmission = after.ExportAdmission;
        BeforeFullCycleMotion = before.HasFullCycleMotion; AfterFullCycleMotion = after.HasFullCycleMotion; NumericWork = numericWork;
        var a = before.Device; var b = after.Device;
        var ma = before.Descriptor; var mb = after.Descriptor;
        MaterialRegistrationEqual = ma is not null && mb is not null && a.Wheel.SlotCount == b.Wheel.SlotCount && a.RegistrationSlot == b.RegistrationSlot &&
            a.Wheel.SlotIds.SequenceEqual(b.Wheel.SlotIds) && a.IdealLock.RecessIds.SequenceEqual(b.IdealLock.RecessIds) &&
            GenevaProfile.ModOne(ma.GammaOut + ma.EpsilonOut * a.OutputReferenceTurns.Value) == GenevaProfile.ModOne(mb.GammaOut + mb.EpsilonOut * b.OutputReferenceTurns.Value) &&
            GenevaProfile.ModOne(a.IdealLock.RecessMountingTurns.Value) == GenevaProfile.ModOne(b.IdealLock.RecessMountingTurns.Value);
        GeometryEqual = a.CanonicalRepresentation == b.CanonicalRepresentation && before.MappingCanonical == after.MappingCanonical;
        Witnesses = witnesses.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly();
        ResultId = HashText(Pack(request.RequestId, BeforeAnalysisId, AfterAnalysisId, verdict.ToString(), rule, N(NumericWork),
            BeforeExportAdmission.ToString(), AfterExportAdmission.ToString(), GenevaProfile.Flag(BeforeFullCycleMotion), GenevaProfile.Flag(AfterFullCycleMotion),
            GenevaProfile.Flag(MaterialRegistrationEqual), GenevaProfile.Flag(GeometryEqual), Pack(Witnesses.Select(w => w.CanonicalRepresentation).ToArray())));
    }
    public GenevaMotionComparisonRequest Request { get; } public string BeforeAnalysisId { get; } public string AfterAnalysisId { get; } public string ResultId { get; }
    public GenevaComparisonVerdict Verdict { get; } public string ProofRule { get; } public string Policy => GenevaOutputComparer.Policy; public string Scope => "OutputAngleOnly";
    public MechanicalExportAdmission BeforeExportAdmission { get; } public MechanicalExportAdmission AfterExportAdmission { get; }
    public bool BeforeFullCycleMotion { get; } public bool AfterFullCycleMotion { get; } public bool MaterialRegistrationEqual { get; } public bool GeometryEqual { get; }
    public int NumericWork { get; } public ReadOnlyCollection<GenevaComparisonWitness> Witnesses { get; } public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public static class GenevaOutputComparer
{
    public const string Policy = "geneva-exact-phase-cycle-correspondence-v1";
    // Shared whole-function theorem. Output transforms act on actual shaft coordinates.
    internal static bool TryMappedAngularIdentity(GenevaMotionDescriptor a, GenevaMotionDescriptor b,
        ExactAffineRelation leftOutput, ExactAffineRelation rightOutput, Rational alpha, Rational beta, out Rational difference)
    {
        var q = b.PhysicalPhase.Coefficient * alpha;
        var p = b.PhysicalPhase.Coefficient * beta + b.PhysicalPhase.Phase;
        var cycles = p - a.PhysicalPhase.Phase; GenevaProfile.Derived(q, p, cycles);
        difference = 0;
        if (a.SlotCount != b.SlotCount || q != a.PhysicalPhase.Coefficient || !cycles.Denominator.IsOne ||
            leftOutput.Coefficient * a.EpsilonOut != rightOutput.Coefficient * b.EpsilonOut) return false;
        var left = leftOutput.Evaluate(a.Device.OutputReferenceTurns.Value);
        var right = rightOutput.Evaluate(b.Device.OutputReferenceTurns.Value - b.EpsilonOut * cycles / b.SlotCount);
        difference = left - right; GenevaProfile.Derived(left, right, difference); return true;
    }
    public static GenevaMotionComparisonResult Compare(GenevaAnalysis before, GenevaAnalysis after, GenevaMotionComparisonRequest request)
    {
        if (before is null || after is null) throw new ArgumentNullException("Both current analyses are required.");
        return CompareCore(TerminalComparisonViews.From(before), TerminalComparisonViews.From(after), request);
    }

    internal static GenevaMotionComparisonResult CompareCore(TerminalComparisonView<GenevaMotionDescriptor, GenevaOutputDefinition, GenevaDeviceDefinition> before,
        TerminalComparisonView<GenevaMotionDescriptor, GenevaOutputDefinition, GenevaDeviceDefinition> after, GenevaMotionComparisonRequest request, AssemblyNumericBudget? budget = null)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException("Two current analyses and an explicit correspondence are required.");
        var issues = new List<MechanicalDiagnostic>(); var witnesses = new List<GenevaComparisonWitness>(); var work = 0;
        GenevaMotionComparisonResult Result(GenevaComparisonVerdict verdict, string rule, string detail)
        { issues.Add(new(rule, "GenevaOutputComparison", scope: "OutputAngleOnly", detail: detail)); return new(request, before, after, verdict, rule, witnesses, issues, work); }
        if (request.Domain != "AllRootTurns" || request.ProofPolicy != Policy)
            return Result(GenevaComparisonVerdict.NotComparable, "UnsupportedPolicyOrDomain", "Only the explicitly named bounded all-root function proof policy is supported.");
        if (request.Alpha.Kind != QuantityKind.AngularPerAngular || request.Beta.Kind != QuantityKind.AngularPosition || request.Delta.Kind != QuantityKind.AngularPosition || request.WitnessRoots.Any(r => r.Kind != QuantityKind.AngularPosition))
            return Result(GenevaComparisonVerdict.NotComparable, "DimensionMismatch", "Root scale is turn/turn; root offset and terminal calibration are turns.");
        if (before.SelectedInputId != request.BeforeInputId || after.SelectedInputId != request.AfterInputId ||
            before.Output.Key != request.BeforeOutputKey || after.Output.Key != request.AfterOutputKey)
            return Result(GenevaComparisonVerdict.NotComparable, "MissingCorrespondence", "Explicit IDs must identify the actual selected roots and bound output terminals.");
        if (!before.HasFullCycleMotion || !after.HasFullCycleMotion)
            return Result(GenevaComparisonVerdict.NotComparable, "UnavailableFullCycleMotion", "Missing pin, lock or determined source does not define a full-cycle function. Requirements and physical export admission remain separate.");
        if (!GenevaNumerics.Valid(request.NumericRequest))
            return Result(GenevaComparisonVerdict.Inconclusive, "InvalidNumericRequest", "No numeric fallback or implicit policy repair is used.");
        var a = before.Descriptor!; var b = after.Descriptor!;
        try
        {
            if (TryMappedAngularIdentity(a, b, new(a.Output.TerminalSign, a.Output.TerminalDatum.Value),
                new(request.Sign * b.Output.TerminalSign, request.Sign * b.Output.TerminalDatum.Value + request.Delta.Value),
                request.Alpha.Value, request.Beta.Value, out var difference))
            {
                if (difference.IsZero || request.Mode == GenevaComparisonMode.WrappedOrientation && difference.Denominator.IsOne)
                    return Result(GenevaComparisonVerdict.Equivalent, request.Mode == GenevaComparisonMode.WrappedOrientation ? "ExactMappedPhaseWrappedIdentity" : "ExactMappedPhaseUnwrappedIdentity",
                        "Same N, mapped physical phase and signed output law match for every root. Integer input cycles contribute -m/N before terminal calibration; wrapped equivalence is an explicitly separate scope.");
            }
            GenevaInterval? Enclose(GenevaPoseRecipe recipe)
            {
                if (recipe.ExactTerminalTurns.HasValue) return new(new CrankSliderInterval(recipe.ExactTerminalTurns.Value, recipe.ExactTerminalTurns.Value));
                var options = budget?.GenevaRequest ?? request.NumericRequest; var remaining = budget?.RemainingWork ?? options.MaximumWork - work;
                if (remaining <= 0) return null;
                var numericBudget = new GenevaNumericRequest(options.AngularWidth, options.LinearWidth, options.DirectionWidth, remaining, options.MaximumPrecisionBits, options.MaximumRefinements, options.Policy);
                var numeric = GenevaNumerics.Evaluate(GenevaAnalyzer.NumericInput(recipe, false), numericBudget); work += numeric.Work; budget?.Debit(numeric.Work);
                if (numeric.Pose is null) issues.Add(new(numeric.Status.ToString(), "GenevaOutputComparison", detail: numeric.Detail));
                return numeric.Pose?.TerminalTurns;
            }
            foreach (var root in request.WitnessRoots)
            {
                var otherValue = request.Alpha.Value * root.Value + request.Beta.Value; GenevaProfile.Derived(otherValue);
                var otherRoot = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, otherValue);
                var left = Enclose(a.At(root)); var right = Enclose(b.At(otherRoot));
                if (left is null || right is null) continue;
                var lo = request.Sign * (request.Sign == 1 ? right.Lower : right.Upper) + request.Delta.Value;
                var hi = request.Sign * (request.Sign == 1 ? right.Upper : right.Lower) + request.Delta.Value; GenevaProfile.Derived(lo, hi);
                var witness = new GenevaComparisonWitness(root, otherRoot, left, new(new CrankSliderInterval(lo, hi)), request.Mode); witnesses.Add(witness);
                if (witness.IsSeparating) return Result(GenevaComparisonVerdict.Different, left.IsExact && right.IsExact ? "ExactSeparatingWitness" : "CertifiedSeparatingWitness",
                    "The recorded mapped root gives different angles in the selected scope. Agreement of mean ratios or finite anchor samples is not a function proof.");
            }
            return Result(GenevaComparisonVerdict.Inconclusive, "BoundedProofRulesExhausted", "No supported all-root identity or certified counterexample. Witness agreement is not equivalence; no phase search, floating tolerance equality or mean-ratio substitution.");
        }
        catch (Exception e) when (e is CrankSliderNumerics.Stop || e is ArgumentException && (e.Message.Contains("bound") || e.Message.Contains("digit")))
        { return Result(GenevaComparisonVerdict.Inconclusive, "ComparisonResourceLimit", "Bounded exact arithmetic or enclosure limit reached: " + e.Message); }
    }
}
