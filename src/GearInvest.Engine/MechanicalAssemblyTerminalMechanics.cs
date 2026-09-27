using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

internal sealed class AssemblyTerminalLocalResult
{
    internal AssemblyTerminalLocalResult(AssemblyMemberDeclaration declaration, BoundMechanicalInputContext input,
        object? compatibility, object? descriptor, bool localAdmitted, MechanicalAxisVerdict target,
        MechanicalAxisVerdict operatingCoverage, IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics, int work, string requestIdentity)
    {
        Declaration = declaration; Input = input; LocalCompatibility = compatibility; Descriptor = descriptor;
        LocalAdmitted = localAdmitted; Target = target; OperatingCoverage = operatingCoverage; NumericWork = work;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(c => c.Stage, StringComparer.Ordinal).ThenBy(c => c.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(declaration.DeclarationId, input.ContextId, MechanicalAssemblyProfile.AnalysisPolicy, requestIdentity, N(work)));
    }
    internal string AnalysisId { get; }
    internal AssemblyMemberDeclaration Declaration { get; }
    internal BoundMechanicalInputContext Input { get; }
    internal object? LocalCompatibility { get; }
    internal object? Descriptor { get; }
    internal GenevaCompatibilityResult? GenevaLocal => LocalCompatibility as GenevaCompatibilityResult;
    internal CrankSliderCompatibilityResult? CrankLocal => LocalCompatibility as CrankSliderCompatibilityResult;
    internal CamFollowerCompatibilityResult? CamLocal => LocalCompatibility as CamFollowerCompatibilityResult;
    internal GenevaMotionDescriptor? Geneva => Descriptor as GenevaMotionDescriptor;
    internal CrankSliderMotionDescriptor? Crank => Descriptor as CrankSliderMotionDescriptor;
    internal CamFollowerMotionDescriptor? Cam => Descriptor as CamFollowerMotionDescriptor;
    internal bool LocalAdmitted { get; }
    internal bool MotionDetermined => LocalAdmitted && Descriptor is not null && Input.BindingAdmitted && Input.GlobalRelation.HasValue;
    internal MechanicalAxisVerdict Target { get; }
    internal MechanicalAxisVerdict OperatingCoverage { get; }
    internal bool RequiredScopesSatisfied => Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    internal ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    internal ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    internal int NumericWork { get; }
}

internal sealed class AssemblyTerminalLocalEvaluation
{
    internal AssemblyTerminalLocalEvaluation(object? recipe, object? numeric, object? display, object? inputObservation,
        string status, bool available, bool displayAvailable, int work, IEnumerable<MechanicalDiagnostic> diagnostics)
    { Recipe = recipe; Numeric = numeric; Display = display; InputObservation = inputObservation; Status = status;
        NumericAvailable = available; DisplayAvailable = displayAvailable; NumericWork = work; Diagnostics = diagnostics.ToList().AsReadOnly(); }
    internal object? Recipe { get; }
    internal object? Numeric { get; }
    internal object? Display { get; }
    internal object? InputObservation { get; }
    internal string Status { get; }
    internal bool NumericAvailable { get; }
    internal bool DisplayAvailable { get; }
    internal int NumericWork { get; }
    internal ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

internal static class AssemblyTerminalMechanics
{
    internal static object Compare(AssemblyTerminalLocalResult before, AssemblyTerminalLocalResult after, object request, AssemblyNumericBudget budget)
    {
        MechanicalExportAdmission Admission(AssemblyTerminalLocalResult r) => r.MotionDetermined && r.Target == MechanicalAxisVerdict.Pass &&
            r.OperatingCoverage == MechanicalAxisVerdict.Pass && r.RequiredScopesSatisfied ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
        MechanicalAxisVerdict Coverage(AssemblyTerminalLocalResult r, string domain)
        {
            var check = r.Checks.FirstOrDefault(c => c.Domain == domain);
            return check?.Verdict == OrientedCheckVerdict.Pass ? MechanicalAxisVerdict.Pass : check?.Verdict == OrientedCheckVerdict.Fail ?
                MechanicalAxisVerdict.Fail : check?.Verdict == OrientedCheckVerdict.Inconclusive ? MechanicalAxisVerdict.Inconclusive : MechanicalAxisVerdict.NotAssessed;
        }
        TerminalComparisonView<GenevaMotionDescriptor, GenevaOutputDefinition, GenevaDeviceDefinition> Geneva(AssemblyTerminalLocalResult r)
        {
            var d = (AssemblyGenevaDeclaration)r.Declaration;
            return new(r.AnalysisId, r.Input.RootInputId, d.Output, d.Device, r.Geneva, r.MotionDetermined,
                r.OperatingCoverage, MechanicalAxisVerdict.NotAssessed, Admission(r));
        }
        TerminalComparisonView<CamFollowerMotionDescriptor, PrismaticOutputDefinition, FlatCamFollowerDefinition> Cam(AssemblyTerminalLocalResult r)
        {
            var d = (AssemblyCamFollowerDeclaration)r.Declaration;
            return new(r.AnalysisId, r.Input.RootInputId, d.Output, d.Device, r.Cam, r.MotionDetermined,
                Coverage(r, "GuideTravelCoverage"), Coverage(r, "FollowerFaceCoverage"), Admission(r));
        }
        TerminalComparisonView<CrankSliderMotionDescriptor, PrismaticOutputDefinition, PlanarCrankSliderDefinition> Crank(AssemblyTerminalLocalResult r)
        {
            var d = (AssemblyCrankSliderDeclaration)r.Declaration;
            var view = new TerminalComparisonView<CrankSliderMotionDescriptor, PrismaticOutputDefinition, PlanarCrankSliderDefinition>(
                r.AnalysisId, r.Input.RootInputId, d.Output, d.Device, r.Crank, r.MotionDetermined, r.OperatingCoverage, MechanicalAxisVerdict.NotAssessed, Admission(r));
            view.CrankEvaluation = (root, options) =>
            {
                if (!r.MotionDetermined || r.Crank is null) return (false, null);
                var p = r.Crank.At(root); var n = CrankSliderNumerics.Evaluate(p, options);
                return (n.IsAvailable && CrankSliderAnalyzer.AssessTravel(p, n, d.Device.GuideTravel, r.OperatingCoverage) == CrankSliderTravelDecision.InRange, n);
            };
            return view;
        }
        if (before.Declaration.Kind != after.Declaration.Kind) throw new ArgumentException("Cross-family terminal comparison is outside this profile.");
        return request switch
        {
            GenevaMotionComparisonRequest r when before.Declaration is AssemblyGenevaDeclaration => GenevaOutputComparer.CompareCore(Geneva(before), Geneva(after), r, budget),
            CrankSliderOutputComparisonRequest r when before.Declaration is AssemblyCrankSliderDeclaration => CrankSliderOutputComparer.CompareCore(Crank(before), Crank(after), r, budget),
            CamFollowerOutputComparisonRequest r when before.Declaration is AssemblyCamFollowerDeclaration => CamFollowerOutputComparer.CompareCore(Cam(before), Cam(after), r, budget),
            _ => throw new ArgumentException("Explicit family-compatible terminal comparison request is required.")
        };
    }

    internal static AssemblyTerminalLocalResult Analyze(AssemblyMemberDeclaration declaration, BoundMechanicalInputContext input, AssemblyNumericBudget budget)
    {
        var start = budget.UsedWork; object? compatibility = null, descriptor = null; bool admitted = false;
        var target = MechanicalAxisVerdict.NotAssessed; var operating = MechanicalAxisVerdict.NotAssessed;
        var checks = new List<OrientedDomainCheck>(); var issues = new List<MechanicalDiagnostic>();
        void Issue(string code, string stage, string detail) => issues.Add(new(code, stage, affectedOutputs: new[] { declaration.OutputKey }, detail: detail));
        void Check(string domain, MechanicalAxisVerdict verdict, string detail) => checks.Add(new(domain, declaration.LocalDeviceId, Convert(verdict), true, detail));
        AssemblyTerminalLocalResult Result() => new(declaration, input, compatibility, descriptor, admitted, target, operating, checks, issues, budget.UsedWork - start, budget.Request.CanonicalRepresentation);
        if (!budget.IsValid) { Issue("InvalidNumericRequest", "AssemblyTerminalAnalysis", "Invalid aggregate arithmetic policy."); return Result(); }
        try
        {
            var relation = input.BindingAdmitted ? input.GlobalRelation : null;
            ExactQuantity Reference(ExactQuantity value)
            {
                if (declaration.TargetBasis == AssemblyTargetBasis.GlobalRoot || value.Kind != QuantityKind.AngularPosition) return value;
                    if (!relation.HasValue || relation.Value.Coefficient.IsZero) throw new AssemblyReferenceUnavailableException("MemberInput reference requires a determined nonzero actual input relation.");
                var root = (value.Value - relation.Value.Phase) / relation.Value.Coefficient; MechanicalDerivedNumbers.Check(root);
                return ExactQuantity.FromCanonical(QuantityKind.AngularPosition, root);
            }
            switch (declaration)
            {
                case AssemblyCrankSliderDeclaration d:
                {
                    var local = CrankSliderAnalyzer.QueryLocal(d.Device, d.Output, input); compatibility = local;
                    checks.AddRange(local.Checks); issues.AddRange(local.Diagnostics); admitted = local.IsAdmitted;
                    var motion = local.IsAdmitted && relation.HasValue ? new CrankSliderMotionDescriptor(d.Device, d.Output, local.MappedShaftFrameMm!, relation.Value) : null;
                    descriptor = motion;
                    operating = local.Envelope is null || d.Device.GuideTravel.Kind != QuantityKind.LinearPosition ? MechanicalAxisVerdict.NotAssessed :
                        local.Envelope.Covers(d.Device.GuideTravel) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
                    var r = d.Requirement;
                    target = CrankSliderAnalyzer.AssessRequirement(local, motion, declaration.TargetBasis == AssemblyTargetBasis.GlobalRoot || !r.RequiredReferencePosition.HasValue ? r :
                        new CrankSliderRequirement(r.RequiredStroke, r.RequiredReferencePosition, Reference(r.ReferenceRoot)), Issue);
                    Check("FullCycleSliderEnvelope", local.Envelope is null ? MechanicalAxisVerdict.Inconclusive : MechanicalAxisVerdict.Pass, "Exact branch-specific full-cycle envelope.");
                    Check("GuideTravelCoverage", operating, "Current finite guide must cover the exact envelope.");
                    break;
                }
                case AssemblyCamFollowerDeclaration d:
                {
                    var local = CamFollowerAnalyzer.QueryLocal(d.Device, d.Output, input, budget.ProofRequest); compatibility = local;
                    budget.Debit(local.GeometryProof.Work); checks.AddRange(local.Checks); issues.AddRange(local.Diagnostics); admitted = local.IsAdmitted;
                    var motion = local.HasValidDependency && relation.HasValue && !relation.Value.Coefficient.IsZero ?
                        new CamFollowerMotionDescriptor(d.Device, d.Output, relation.Value, local.Epsilon!.Value, local.GammaTurns!.Value) : null;
                    descriptor = motion; var env = local.Envelope;
                    var guide = env is null || d.Device.GuideTravel.Kind != QuantityKind.LinearPosition ? MechanicalAxisVerdict.NotAssessed :
                        env.LowerMm >= d.Device.GuideTravel.Lower.Value && env.UpperMm <= d.Device.GuideTravel.Upper.Value ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
                    var face = MechanicalAxisVerdict.NotAssessed;
                    if (env is not null && d.Device.FollowerFace.Kind == QuantityKind.LinearPosition)
                    {
                        var low = CamFollowerNumerics.CompareDetailed(env.MinimumFaceCoordinateMm, ExactPiLength.FromMillimeters(d.Device.FollowerFace.Lower), budget.CamRequest); budget.Debit(low.Work);
                        var high = CamFollowerNumerics.CompareDetailed(env.MaximumFaceCoordinateMm, ExactPiLength.FromMillimeters(d.Device.FollowerFace.Upper), budget.CamRequest); budget.Debit(high.Work);
                        face = low.Comparison == CamPiComparison.Less || high.Comparison == CamPiComparison.Greater ? MechanicalAxisVerdict.Fail :
                            !low.IsResolved || !high.IsResolved ? MechanicalAxisVerdict.Inconclusive : MechanicalAxisVerdict.Pass;
                    }
                    operating = Combine(guide, face); var r = d.Requirement;
                    target = CamFollowerAnalyzer.AssessRequirement(local, motion, declaration.TargetBasis == AssemblyTargetBasis.GlobalRoot || !r.RequiredReferencePosition.HasValue ? r :
                        new CamFollowerRequirement(r.RequiredStroke, r.RequiredReferencePosition, Reference(r.ReferenceRoot), r.PositiveStrokeRequired), Issue);
                    Check("GuideTravelCoverage", guide, "Exact support-height envelope in the current guide.");
                    Check("FollowerFaceCoverage", face, "Counted certified ordering of the whole-cycle contact footprint.");
                    break;
                }
                case AssemblyGenevaDeclaration d:
                {
                    var local = GenevaAnalyzer.QueryLocal(d.Device, d.Output, input, budget.GenevaRequest, input.ContextId); compatibility = local;
                    budget.Debit(local.NumericWork); checks.AddRange(local.Checks); issues.AddRange(local.Diagnostics); admitted = local.IsAdmitted;
                    var motion = local.PinSlotGeometryAdmitted && relation.HasValue && !relation.Value.Coefficient.IsZero ?
                        new GenevaMotionDescriptor(HashText(Pack(declaration.DeclarationId, input.ContextId)), d.Device, d.Output, local, relation.Value) : null;
                    descriptor = motion; var r = d.Requirement;
                    var assessment = GenevaAnalyzer.AssessRequirement(d.Device, d.Output.Key, local, motion, declaration.TargetBasis == AssemblyTargetBasis.GlobalRoot || !r.RequiredReferenceOutput.HasValue ? r :
                        new GenevaRequirement(r.RequiredIndexStep, r.RequiredDwellFraction, r.RequiredReferenceOutput, Reference(r.ReferenceRoot)), issues, budget.GenevaRequest);
                    target = assessment.Target; budget.Debit(assessment.ReferenceNumeric?.Work ?? 0);
                    operating = local.IsAdmitted && motion is not null ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.NotAssessed;
                    Check("PinSlotCenterlineClosure", local.PinSlotGeometryAdmitted ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Inconclusive, "Admitted exact pin-slot construction.");
                    Check("IdealArcLockMate", local.LockGeometryAdmitted ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail, "Selected finite ideal arc lock proof.");
                    Check("IndexDwellBoundaryContinuity", local.PinSlotGeometryAdmitted ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Inconclusive, "Exact accumulated cycle boundaries.");
                    Check("UnwrappedAngularMotion", operating, "One global root, exact unwrapped cycle.");
                    break;
                }
                default: throw new ArgumentException("A supported nonlinear terminal declaration is required.");
            }
            Check(declaration.Kind == AssemblyDeviceKind.Geneva ? "OutputRequirements" : "NonlinearOutputRequirements", target, "Independent requirements retain their declared input basis.");
            Check("WholeMotionDeterminacy", admitted && descriptor is not null && relation.HasValue ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.NotAssessed, "Actual admitted upstream shaft and all terminal constraints.");
            var unperformed = declaration.Kind == AssemblyDeviceKind.Geneva ? GenevaAnalyzer.UnperformedDomains :
                declaration.Kind == AssemblyDeviceKind.CrankSlider ? CrankSliderAnalyzer.UnperformedDomains : CamFollowerAnalyzer.UnperformedDomains;
            foreach (var domain in unperformed.Concat(declaration.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
            {
                if (checks.Any(c => c.Domain == domain)) continue;
                var required = declaration.RequiredValidationDomains.Contains(domain);
                checks.Add(new(domain, declaration.LocalDeviceId, OrientedCheckVerdict.NotPerformed, required, "Not performed by this bounded terminal profile."));
                if (required) Issue("RequiredValidationNotPerformed", domain, "Required unperformed domain blocks whole-assembly finalization.");
            }
        }
        catch (Exception e) when (e is ArgumentException || e is CrankSliderNumerics.Stop || e is AssemblyReferenceUnavailableException)
        {
            // Resource or unavailable-reference failure never becomes a successful descriptor admission.
            Issue(e is AssemblyReferenceUnavailableException ? "UndeterminedInput" : "NumericResourceLimit", "AssemblyTerminalAnalysis", e.Message);
            target = MechanicalAxisVerdict.Inconclusive;
            checks.Add(new("TerminalAnalysisAvailability", declaration.LocalDeviceId, OrientedCheckVerdict.Inconclusive, true, e.Message));
        }
        return Result();
    }

    internal static AssemblyTerminalLocalEvaluation Evaluate(AssemblyTerminalLocalResult local, ExactQuantity globalRoot,
        AssemblyNumericBudget budget, bool includeDisplay = true)
    {
        var start = budget.UsedWork; object? recipe = null, numeric = null, display = null, inputObservation = null;
        var issues = new List<MechanicalDiagnostic>();
        AssemblyTerminalLocalEvaluation Result(string status, bool available = false, bool displayAvailable = false) =>
            new(recipe, numeric, display, inputObservation, status, available, displayAvailable, budget.UsedWork - start, issues);
        void Issue(string code, string detail) => issues.Add(new(code, "AssemblyTerminalEvaluation", affectedOutputs: new[] { local.Declaration.OutputKey }, detail: detail));
        if (globalRoot.Kind != QuantityKind.AngularPosition) return Result("DimensionMismatch");
        if (!budget.IsValid) return Result("InvalidNumericRequest");
        try
        {
            MechanicalAuthoringProfile.Number(globalRoot.Value);
            var input = local.Input; var frame = input.FixedFrameMm;
            var turns = input.GlobalRelation?.Evaluate(globalRoot.Value);
            if (turns.HasValue) MechanicalDerivedNumbers.Check(turns.Value);
            switch (local.Declaration)
            {
                case AssemblyCrankSliderDeclaration d:
                {
                    var c = local.CrankLocal;
                    if (turns.HasValue && frame is not null && c?.HasValidCrankMounting == true)
                        inputObservation = new CrankSliderCrankEvaluation(d.Device.CrankBodyId, d.Device.PivotMm, frame.Z, frame.X, turns.Value,
                            d.Device.MountingTurns.Value, c.GammaTurns + c.Epsilon * turns.Value);
                    if (!local.MotionDetermined || local.Crank is null) return Result("UndeterminedInput");
                    var p = local.Crank.At(globalRoot); recipe = p;
                    var n = CrankSliderNumerics.Evaluate(p, budget.CrankRequest); budget.Debit(n.Work); numeric = n;
                    var travel = CrankSliderAnalyzer.AssessTravel(p, n, d.Device.GuideTravel, local.OperatingCoverage);
                    if (!n.IsAvailable) return Result(n.Status.ToString());
                    if (travel != CrankSliderTravelDecision.InRange) return Result(travel == CrankSliderTravelDecision.OutOfRange ? "OutOfGuideTravel" : "TravelDecisionUnresolved");
                    return Result("Success", true, true);
                }
                case AssemblyCamFollowerDeclaration d:
                {
                    var c = local.CamLocal;
                    if (turns.HasValue && frame is not null && c?.HasValidCamMounting == true)
                        inputObservation = new CamFollowerCamEvaluation(d.Device.CamBodyId, d.Device.CenterMm, frame.Z, frame.X, turns.Value, c.GammaTurns + c.Epsilon * turns.Value);
                    if (!local.MotionDetermined || local.Cam is null) return Result(c?.Verdict == ConnectionCompatibilityVerdict.Inconclusive ? "IncompleteNumericBudget" : "UndeterminedInput");
                    var p = local.Cam.At(globalRoot); recipe = p;
                    var low = CamFollowerNumerics.CompareDetailed(p.FaceCoordinateMm, ExactPiLength.FromMillimeters(d.Device.FollowerFace.Lower), budget.CamRequest); budget.Debit(low.Work);
                    var high = CamFollowerNumerics.CompareDetailed(p.FaceCoordinateMm, ExactPiLength.FromMillimeters(d.Device.FollowerFace.Upper), budget.CamRequest); budget.Debit(high.Work);
                    var n = CamFollowerNumerics.Evaluate(p.ContactPointMm, budget.CamRequest); budget.Debit(n.Work); numeric = n;
                    var displayAvailable = !includeDisplay || !budget.Request.IncludeDisplay;
                    if (includeDisplay && budget.Request.IncludeDisplay)
                    {
                        var material = AssemblyCamDisplay.Evaluate(local.AnalysisId, local.Cam, globalRoot, budget);
                        display = material; displayAvailable = material.IsAvailable;
                    }
                    if (!low.IsResolved || !high.IsResolved) return Result((!low.IsResolved ? low : high).Status.ToString());
                    if (!n.IsAvailable) return Result(n.Status.ToString());
                    if (low.Comparison == CamPiComparison.Less || high.Comparison == CamPiComparison.Greater) return Result("OutOfFollowerFace");
                    if (p.GuidePosition.Value < d.Device.GuideTravel.Lower.Value || p.GuidePosition.Value > d.Device.GuideTravel.Upper.Value) return Result("OutOfGuideTravel");
                    if (!displayAvailable) Issue("DisplayUnavailable", "The admitted contact pose remains available; the complete requested cam contour is unavailable.");
                    return Result("Success", true, displayAvailable);
                }
                case AssemblyGenevaDeclaration d:
                {
                    var c = local.GenevaLocal;
                    if (turns.HasValue && frame is not null && c?.HasValidDriverMounting == true)
                        inputObservation = new GenevaDriverObservation(d.Device.DriverBodyId, d.Device.DriverCenterMm, frame.Z, frame.X, turns.Value, c.GammaIn + c.EpsilonIn * turns.Value);
                    if (!local.MotionDetermined || local.Geneva is null)
                    {
                        var driverDisplayAvailable = !includeDisplay || !budget.Request.IncludeDisplay;
                        if (includeDisplay && budget.Request.IncludeDisplay && inputObservation is GenevaDriverObservation driver && driver.PhysicalPhaseTurns.HasValue)
                        {
                            var driverNumeric = GenevaNumerics.EvaluateDriver(driver.PhysicalPhaseTurns.Value, d.Device.OrbitRadius, d.Device.DriverCenterMm,
                                d.Device.CenterDirection, d.Device.TransverseDirection, budget.GenevaRequest, GenevaAnalyzer.FeatureInputs(d.Device, null, false));
                            budget.Debit(driverNumeric.Work); display = driverNumeric; driverDisplayAvailable = driverNumeric.IsAvailable;
                            if (!driverDisplayAvailable) Issue("DisplayUnavailable", "The independently mounted Geneva driver material display is unavailable; no wheel pose is authorized.");
                        }
                        return Result(c?.Verdict == ConnectionCompatibilityVerdict.Inconclusive ? "IncompleteNumericBudget" : "UndeterminedInput", false, driverDisplayAvailable);
                    }
                    GenevaPoseRecipe p; GenevaNumericComputation n;
                    if (budget.ShareGenevaSamples)
                    {
                        var shared = budget.GenevaSamples.Get(local.Geneva, globalRoot, budget.Request.AngularWidth.Value, budget);
                        p = shared.Recipe; n = shared.Numeric;
                    }
                    else { p = local.Geneva.At(globalRoot); n = GenevaNumerics.Evaluate(GenevaAnalyzer.NumericInput(p, false), budget.GenevaRequest); budget.Debit(n.Work); }
                    recipe = p; numeric = n;
                    if (!n.IsAvailable) return Result(n.Status.ToString());
                    var displayAvailable = !includeDisplay || !budget.Request.IncludeDisplay;
                    if (includeDisplay && budget.Request.IncludeDisplay)
                    {
                        var material = GenevaNumerics.EvaluateFeatures(GenevaAnalyzer.NumericInput(p, true), n, budget.GenevaRequest);
                        budget.Debit(material.Work); display = material; displayAvailable = material.IsAvailable;
                        if (!displayAvailable) Issue("DisplayUnavailable", "Exact cycle and admitted pose remain available; optional feature samples are unavailable.");
                    }
                    return Result("Success", true, displayAvailable);
                }
                default: return Result("UnsupportedOutputCapability");
            }
        }
        catch (Exception e) when (e is ArgumentException || e is CrankSliderNumerics.Stop)
        { Issue("NumericResourceLimit", e.Message); return Result("NumericResourceLimit"); }
    }

    private static OrientedCheckVerdict Convert(MechanicalAxisVerdict value) => value == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        value == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : value == MechanicalAxisVerdict.NotAssessed ? OrientedCheckVerdict.NotPerformed : OrientedCheckVerdict.Inconclusive;
    private static MechanicalAxisVerdict Combine(MechanicalAxisVerdict a, MechanicalAxisVerdict b) =>
        a == MechanicalAxisVerdict.Fail || b == MechanicalAxisVerdict.Fail ? MechanicalAxisVerdict.Fail :
        a == MechanicalAxisVerdict.Pass && b == MechanicalAxisVerdict.Pass ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Inconclusive;

    private sealed class AssemblyReferenceUnavailableException : Exception
    { internal AssemblyReferenceUnavailableException(string message) : base(message) { } }
}
