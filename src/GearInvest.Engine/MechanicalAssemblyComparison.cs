using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum AssemblyOutputComparisonMode { UnwrappedAngular, WrappedAngular, PrismaticScalar, WorldPrismaticPath }
public enum AssemblyOutputComparisonVerdict { Equivalent, Different, Inconclusive, NotComparable }

/// <summary>Explicit correspondence between two actual owned observations and their selected global roots.</summary>
public sealed class MechanicalAssemblyOutputComparisonRequest
{
    public MechanicalAssemblyOutputComparisonRequest(AssemblyComponentReference beforeOutput, AssemblyComponentReference afterOutput,
        AssemblyOutputComparisonMode mode, ExactQuantity? inputAlpha = null, ExactQuantity? inputBeta = null,
        int outputSign = 1, ExactQuantity? outputDatum = null, AssemblyNumericRequest? numericRequest = null,
        OrientedFrame? afterWorldToBeforeWorldMm = null, IEnumerable<ExactQuantity>? witnessRoots = null,
        string? beforeInputId = null, string? afterInputId = null)
    {
        BeforeOutput = beforeOutput ?? throw new ArgumentNullException(nameof(beforeOutput)); AfterOutput = afterOutput ?? throw new ArgumentNullException(nameof(afterOutput));
        if (!Enum.IsDefined(typeof(AssemblyOutputComparisonMode), mode)) throw new ArgumentException("Unknown assembly output comparison scope.");
        Mode = mode; InputAlpha = inputAlpha ?? ExactQuantity.TurnsPerTurn(1); InputBeta = inputBeta ?? ExactQuantity.Turns(0);
        OutputSign = outputSign; OutputDatum = outputDatum ?? (IsAngular ? ExactQuantity.Turns(0) : ExactQuantity.Millimeters(0));
        foreach (var q in new[] { InputAlpha, InputBeta, OutputDatum }) RotaryLinearProfile.Quantity(q);
        if (InputAlpha.Value.IsZero || outputSign != -1 && outputSign != 1) throw new ArgumentException("Nonzero root mapping and output sign +/-1 are required.");
        NumericRequest = numericRequest ?? AssemblyNumericRequest.Default; AfterWorldToBeforeWorldMm = afterWorldToBeforeWorldMm;
        if (afterWorldToBeforeWorldMm is not null) MechanicalAuthoringProfile.FrameBound(afterWorldToBeforeWorldMm);
        BeforeInputId = beforeInputId is null ? null : MechanicalAuthoringProfile.IdValue(beforeInputId);
        AfterInputId = afterInputId is null ? null : MechanicalAuthoringProfile.IdValue(afterInputId);
        var roots = (witnessRoots ?? new[] { ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(1, 3)), ExactQuantity.Turns(new Rational(1, 4)),
            ExactQuantity.Turns(new Rational(-1, 4)), ExactQuantity.Turns(new Rational(1, 2)), ExactQuantity.Turns(1), ExactQuantity.Turns(-1) }).Take(33).ToArray();
        if (roots.Length > 32) throw new ArgumentException("At most 32 explicit comparison witnesses are supported.");
        foreach (var root in roots) RotaryLinearProfile.Quantity(root);
        WitnessRoots = Array.AsReadOnly(roots); RequestId = HashText(CanonicalRepresentation);
    }
    public AssemblyComponentReference BeforeOutput { get; }
    public AssemblyComponentReference AfterOutput { get; }
    public AssemblyOutputComparisonMode Mode { get; }
    public bool IsAngular => Mode == AssemblyOutputComparisonMode.UnwrappedAngular || Mode == AssemblyOutputComparisonMode.WrappedAngular;
    public ExactQuantity InputAlpha { get; }
    public ExactQuantity InputBeta { get; }
    public int OutputSign { get; }
    public ExactQuantity OutputDatum { get; }
    public AssemblyNumericRequest NumericRequest { get; }
    public OrientedFrame? AfterWorldToBeforeWorldMm { get; }
    public string? BeforeInputId { get; }
    public string? AfterInputId { get; }
    public ReadOnlyCollection<ExactQuantity> WitnessRoots { get; }
    public string RequestId { get; }
    public string CanonicalRepresentation => Pack(BeforeOutput.CanonicalRepresentation, AfterOutput.CanonicalRepresentation, Mode.ToString(),
        MechanicalAssemblyProfile.Q(InputAlpha), MechanicalAssemblyProfile.Q(InputBeta), N(OutputSign), MechanicalAssemblyProfile.Q(OutputDatum),
        NumericRequest.CanonicalRepresentation, AfterWorldToBeforeWorldMm is null ? "" : Frame(AfterWorldToBeforeWorldMm),
        BeforeInputId ?? "", AfterInputId ?? "", Pack(WitnessRoots.Select(MechanicalAssemblyProfile.Q).ToArray()));
}

public sealed class MechanicalAssemblyOutputComparisonResult
{
    internal MechanicalAssemblyOutputComparisonResult(MechanicalAssemblyOutputComparisonRequest request, MechanicalAssemblyAnalysis before,
        MechanicalAssemblyAnalysis after, AssemblyOutputComparisonVerdict verdict, string rule, object? familyResult,
        int numericWork, int analysisCount, IEnumerable<AssemblyDiagnostic> diagnostics)
    {
        Request = request; BeforeAnalysisId = before.AnalysisId; AfterAnalysisId = after.AnalysisId;
        BeforeDefinitionId = before.DefinitionId; AfterDefinitionId = after.DefinitionId;
        BeforeExportAdmission = before.ExportAdmission; AfterExportAdmission = after.ExportAdmission;
        Verdict = verdict; ProofRule = rule; FamilyResult = familyResult; NumericWork = numericWork; FreshAnalysisCount = analysisCount;
        Diagnostics = diagnostics.ToList().AsReadOnly();
        ResultId = HashText(Pack(request.RequestId, BeforeAnalysisId, AfterAnalysisId, verdict.ToString(), rule, N(numericWork), N(analysisCount),
            BeforeExportAdmission.ToString(), AfterExportAdmission.ToString(), FamilyResultIdentity, Pack(Diagnostics.Select(x => x.CanonicalRepresentation).ToArray())));
    }
    public MechanicalAssemblyOutputComparisonRequest Request { get; }
    public string BeforeAnalysisId { get; }
    public string AfterAnalysisId { get; }
    public string BeforeDefinitionId { get; }
    public string AfterDefinitionId { get; }
    public MechanicalExportAdmission BeforeExportAdmission { get; }
    public MechanicalExportAdmission AfterExportAdmission { get; }
    public AssemblyOutputComparisonVerdict Verdict { get; }
    public string ProofRule { get; }
    public string Scope => Request.Mode.ToString() + "OutputFunctionOnly";
    public object? FamilyResult { get; }
    public OutputEquivalenceResult? AffineResult => FamilyResult as OutputEquivalenceResult;
    public GenevaMotionComparisonResult? GenevaResult => FamilyResult as GenevaMotionComparisonResult;
    public CrankSliderOutputEquivalenceResult? CrankSliderResult => FamilyResult as CrankSliderOutputEquivalenceResult;
    public CamFollowerOutputEquivalenceResult? CamFollowerResult => FamilyResult as CamFollowerOutputEquivalenceResult;
    public int NumericWork { get; }
    public int FreshAnalysisCount { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
    public string ResultId { get; }
    public string FamilyResultIdentity => FamilyResult switch
    { OutputEquivalenceResult r => r.ComparisonId, GenevaMotionComparisonResult r => r.ResultId,
        CrankSliderOutputEquivalenceResult r => r.ResultId, CamFollowerOutputEquivalenceResult r => r.ResultId,
        AssemblyGenevaComparisonEvidence r => r.EvidenceId, _ => "" };
}

public static partial class MechanicalAssemblyAnalyzer
{
    public static MechanicalAssemblyOutputComparisonResult CompareOutputs(MechanicalAssemblyAnalysis before, MechanicalAssemblyAnalysis after,
        MechanicalAssemblyOutputComparisonRequest request)
    {
        if (before is null || after is null || request is null) throw new ArgumentNullException("Two current assembly analyses and explicit correspondence are required.");
        var budget = new AssemblyNumericBudget(request.NumericRequest); var issues = new List<AssemblyDiagnostic>(); object? family = null; var analysisCount = 0;
        MechanicalAssemblyOutputComparisonResult Result(AssemblyOutputComparisonVerdict verdict, string rule, string detail)
        {
            issues.Add(new(rule, "AssemblyOutputComparison", detail, new[] { request.BeforeOutput, request.AfterOutput }));
            return new(request, before, after, verdict, rule, family, budget.UsedWork, analysisCount, issues);
        }
        if (!request.NumericRequest.IsValid) return Result(AssemblyOutputComparisonVerdict.Inconclusive, "InvalidNumericRequest", "Invalid aggregate numerical policy; no default replacement is applied.");
        if (request.InputAlpha.Kind != QuantityKind.AngularPerAngular || request.InputBeta.Kind != QuantityKind.AngularPosition ||
            request.OutputDatum.Kind != (request.IsAngular ? QuantityKind.AngularPosition : QuantityKind.LinearPosition) || request.WitnessRoots.Any(x => x.Kind != QuantityKind.AngularPosition))
            return Result(AssemblyOutputComparisonVerdict.NotComparable, "DimensionMismatch", "Root mapping and selected angular/prismatic observation units must match explicitly.");
        try
        {
            AssemblyNumericRequest Remaining()
            {
                var p = request.NumericRequest;
                return new(budget.RemainingWork, p.AngularWidth, p.LinearWidth, p.DirectionWidth, p.MaximumPrecisionBits, p.MaximumRefinements, p.IncludeDisplay, p.Policy, p.CamContourSamples);
            }
            var sameDraft = before.Draft.DraftId == after.Draft.DraftId;
            before = Analyze(before.Draft, Remaining()); budget.Debit(before.NumericWork); analysisCount++;
            if (sameDraft) after = before;
            else { after = Analyze(after.Draft, Remaining()); budget.Debit(after.NumericWork); analysisCount++; }
            var beforeRoot = before.RootAnalysis.SelectedInputId; var afterRoot = after.RootAnalysis.SelectedInputId;
            if (beforeRoot is null || afterRoot is null || request.BeforeInputId is not null && request.BeforeInputId != beforeRoot ||
                request.AfterInputId is not null && request.AfterInputId != afterRoot)
                return Result(AssemblyOutputComparisonVerdict.NotComparable, "MissingInputCorrespondence", "Requested roots must be the actual single selected prescribed inputs.");
            AssemblyOutputAnalysis Resolve(MechanicalAssemblyAnalysis a, AssemblyComponentReference reference) => AssemblyOutputAssessment.Analyze(
                new AssemblyOutputBinding("comparison-observation", reference), a.Draft.Definition, a.RootAnalysis, RootLaws(a.RootAnalysis),
                a.Members.ToDictionary(m => m.InstanceId, StringComparer.Ordinal), budget);
            var left = Resolve(before, request.BeforeOutput); var right = Resolve(after, request.AfterOutput);
            if (!left.IsResolved || !right.IsResolved)
            {
                issues.AddRange(left.Diagnostics); issues.AddRange(right.Diagnostics);
                return Result(AssemblyOutputComparisonVerdict.NotComparable, "UnavailableMotion", "The exact owned references must resolve to current determined observations; independent branch failures remain separate.");
            }
            AssemblyGenevaAffineMotion? GenevaMotion(MechanicalAssemblyAnalysis a, AssemblyComponentReference reference)
            {
                if (reference.Owner != AssemblyOwnerKind.Member) return null;
                var member = a.Members.Single(m => m.InstanceId == reference.MemberId);
                var current = reference.Kind == AssemblyComponentKind.Shaft ? member.GenevaShaftMotion : member.GenevaTerminalMotion;
                if (current is not null) return current;
                // Comparison-only view of an old terminal. This never enables old-profile chaining.
                if ((before.Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId ||
                    after.Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId) &&
                    member.TerminalLocal?.MotionDetermined == true && member.TerminalLocal.Geneva is GenevaMotionDescriptor g)
                    return new(AssemblyComponentReference.Member(member.InstanceId, AssemblyComponentKind.Shaft, g.Device.OutputShaft.Id), g,
                        reference.Kind == AssemblyComponentKind.Shaft ? new ExactAffineRelation(1, 0) : new ExactAffineRelation(g.Output.TerminalSign, g.Output.TerminalDatum.Value));
                return null;
            }
            var genevaBefore = GenevaMotion(before, request.BeforeOutput); var genevaAfter = GenevaMotion(after, request.AfterOutput);
            if (genevaBefore is not null && genevaAfter is not null)
            {
                if (!request.IsAngular) return Result(AssemblyOutputComparisonVerdict.NotComparable, "DimensionMismatch", "Geneva-dependent shafts are angular functions.");
                var compared = AssemblyGenevaComparer.Compare(genevaBefore, genevaAfter, request, budget); family = compared.Evidence;
                return Result(compared.Verdict, compared.Rule, "Exact affine normalization uses the existing Geneva all-root phase/cycle theorem; sample agreement and overlapping enclosures are not equality proofs.");
            }
            if (left.AffineRelation.HasValue && right.AffineRelation.HasValue)
            {
                if (!request.IsAngular) return Result(AssemblyOutputComparisonVerdict.NotComparable, "DimensionMismatch", "Selected affine shafts and terminals are angular observations.");
                var mapped = new OutputComparisonRequest(request.BeforeOutput.LocalId, request.AfterOutput.LocalId, beforeRoot, afterRoot,
                    request.InputAlpha.Value, request.InputBeta.Value, request.OutputSign, request.OutputDatum.Value,
                    request.Mode == AssemblyOutputComparisonMode.WrappedAngular ? OutputComparisonMode.AngleModuloOne : OutputComparisonMode.FullAffineOutput);
                var compared = MechanicalOutputComparer.CompareDerivedAffineRelations(left.AffineRelation.Value, right.AffineRelation.Value, mapped); family = compared;
                return Result(ToVerdict(compared.Verdict.ToString()), "ExactMappedAffineRelation", "Existing exact full-affine or explicitly wrapped equality rules compare the actual resolved observations; this is not whole-geometry equality.");
            }
            if (left.AffineRelation.HasValue || right.AffineRelation.HasValue || request.BeforeOutput.Owner != AssemblyOwnerKind.Member || request.AfterOutput.Owner != AssemblyOwnerKind.Member)
                return Result(AssemblyOutputComparisonVerdict.NotComparable, "UnsupportedCrossFamilyComparison", "Cross-family affine/nonlinear function comparison is outside this bounded profile.");
            var a = before.Members.Single(m => m.InstanceId == request.BeforeOutput.MemberId).TerminalLocal;
            var b = after.Members.Single(m => m.InstanceId == request.AfterOutput.MemberId).TerminalLocal;
            if (a is null || b is null || a.Declaration.Kind != b.Declaration.Kind)
                return Result(AssemblyOutputComparisonVerdict.NotComparable, "UnsupportedCrossFamilyComparison", "Current bounded rules require the same nonlinear terminal family.");
            if (request.IsAngular != (a.Declaration.Kind == AssemblyDeviceKind.Geneva))
                return Result(AssemblyOutputComparisonVerdict.NotComparable, "DimensionMismatch", "The actual terminal family and requested observation dimension differ.");
            if (request.Mode == AssemblyOutputComparisonMode.WorldPrismaticPath && request.AfterWorldToBeforeWorldMm?.IsProperCardinal != true)
                return Result(AssemblyOutputComparisonVerdict.NotComparable, "MissingWorldCorrespondence", "World reference-point paths require an explicit proper cardinal after-to-before frame.");
            // Convert coordinate observations only for comparison. No terminal sign/datum enters a physical input binding.
            (int Scale, Rational Offset) Coordinate(AssemblyTerminalLocalResult local, AssemblyComponentReference reference)
            {
                if (reference.Kind == AssemblyComponentKind.Output) return (1, 0);
                var calibration = local.Declaration switch
                {
                    AssemblyGenevaDeclaration d when reference.Kind == AssemblyComponentKind.Shaft => (d.Output.TerminalSign, d.Output.TerminalDatum.Value),
                    AssemblyCrankSliderDeclaration d when reference.Kind == AssemblyComponentKind.LinearDof => (d.Output.TerminalSign, d.Output.TerminalDatum.Value),
                    AssemblyCamFollowerDeclaration d when reference.Kind == AssemblyComponentKind.LinearDof => (d.Output.TerminalSign, d.Output.TerminalDatum.Value),
                    _ => throw new ArgumentException("The selected reference does not identify a supported nonlinear coordinate observation.")
                };
                return (calibration.Item1, -calibration.Item1 * calibration.Item2);
            }
            var ca = Coordinate(a, request.BeforeOutput); var cb = Coordinate(b, request.AfterOutput);
            var sign = ca.Scale * request.OutputSign * cb.Scale;
            var datum = ca.Scale * (request.OutputSign * cb.Offset + request.OutputDatum.Value - ca.Offset); MechanicalDerivedNumbers.Check(datum);
            object localRequest = a.Declaration switch
            {
                AssemblyGenevaDeclaration => new GenevaMotionComparisonRequest(a.Declaration.OutputKey, b.Declaration.OutputKey, beforeRoot, afterRoot,
                    request.InputAlpha, request.InputBeta, sign, ExactQuantity.FromCanonical(QuantityKind.AngularPosition, datum),
                    request.Mode == AssemblyOutputComparisonMode.WrappedAngular ? GenevaComparisonMode.WrappedOrientation : GenevaComparisonMode.UnwrappedAngularOutput,
                    request.WitnessRoots, budget.GenevaRequest),
                AssemblyCrankSliderDeclaration c => new CrankSliderOutputComparisonRequest(a.Declaration.OutputKey, b.Declaration.OutputKey, beforeRoot, afterRoot,
                    request.InputAlpha, request.InputBeta, sign, ExactQuantity.FromCanonical(QuantityKind.LinearPosition, datum),
                    request.Mode == AssemblyOutputComparisonMode.WorldPrismaticPath ? CrankSliderOutputComparisonMode.WorldSliderPath : CrankSliderOutputComparisonMode.SliderScalar,
                    c.Output.ReferencePointId, ((AssemblyCrankSliderDeclaration)b.Declaration).Output.ReferencePointId, request.AfterWorldToBeforeWorldMm, request.WitnessRoots, budget.CrankRequest),
                AssemblyCamFollowerDeclaration c => new CamFollowerOutputComparisonRequest(a.Declaration.OutputKey, b.Declaration.OutputKey, beforeRoot, afterRoot,
                    request.InputAlpha, request.InputBeta, sign, ExactQuantity.FromCanonical(QuantityKind.LinearPosition, datum),
                    request.Mode == AssemblyOutputComparisonMode.WorldPrismaticPath ? CamFollowerOutputComparisonMode.WorldFollowerPath : CamFollowerOutputComparisonMode.FollowerScalar,
                    c.Output.ReferencePointId, ((AssemblyCamFollowerDeclaration)b.Declaration).Output.ReferencePointId, request.AfterWorldToBeforeWorldMm, request.WitnessRoots, budget.CamRequest),
                _ => throw new ArgumentException("Unsupported terminal family.")
            };
            family = AssemblyTerminalMechanics.Compare(a, b, localRequest, budget);
            return family switch
            {
                GenevaMotionComparisonResult r => Result(ToVerdict(r.Verdict.ToString()), r.ProofRule, "The existing Geneva full-function rule preserves accumulated -m/N cycle correction. Wrapped scope is explicit."),
                CrankSliderOutputEquivalenceResult r => Result(ToVerdict(r.Verdict.ToString()), r.ProofRule, "The existing bounded exact closure proof or certified counterexample compares only the selected slider observation."),
                CamFollowerOutputEquivalenceResult r => Result(ToVerdict(r.Verdict.ToString()), r.ProofRule, "The existing complete-partition polynomial proof compares the selected follower function; finite sample agreement cannot prove equality."),
                _ => Result(AssemblyOutputComparisonVerdict.NotComparable, "UnsupportedOutputCapability", "No supported comparison result was produced.")
            };
        }
        catch (Exception e) when (e is ArgumentException || e is CrankSliderNumerics.Stop)
        { return Result(AssemblyOutputComparisonVerdict.Inconclusive, "ComparisonResourceLimit", e.Message); }
    }

    private static AssemblyOutputComparisonVerdict ToVerdict(string value) => Enum.TryParse<AssemblyOutputComparisonVerdict>(value, out var verdict) ? verdict : AssemblyOutputComparisonVerdict.Inconclusive;
}
