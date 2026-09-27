using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static partial class GenevaAnalyzer
{
    internal static readonly string[] UnperformedDomains = new[] { "FinitePinSlotClearance", "Backlash", "LockReleaseReliefSolid", "FullAddedBodyClearance", "FullSweptClearance", "ContactForce", "Retention", "Impact", "Friction", "Inertia", "Dynamics", "Strength", "Wear", "Manufacturing" };
    public static GenevaAnalysis Analyze(GenevaDraft draft, GenevaNumericRequest? geometryRequest = null)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var source = MechanicalAnalyzer.Analyze(draft.Definition.Source); var local = Query(draft, geometryRequest);
        var d = draft.Definition; var g = d.Device; var checks = local.Checks.ToList(); var issues = source.Diagnostics.Concat(local.Diagnostics).ToList();
        var component = source.AdmittedComponents.FirstOrDefault(c => c.ShaftIds.Contains(g.SourceShaftId));
        var relation = component?.Affine?.Relations.FirstOrDefault(r => r.DofId == g.SourceShaftId);
        ExactAffineRelation? retained = component?.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput ? relation?.Relation : null;
        GenevaMotionDescriptor? descriptor = null;
        try
        {
            if (local.PinSlotGeometryAdmitted && retained.HasValue && !retained.Value.Coefficient.IsZero)
                descriptor = new(d, local, retained.Value);
        }
        catch (ArgumentException e) when (e.Message.Contains("bound") || e.Message.Contains("digit"))
        { issues.Add(new("NumericResourceLimit", "GenevaMotionConstruction", affectedOutputs: new[] { d.Output.Key }, detail: e.Message)); }
        var determinacy = retained.HasValue && local.IsAdmitted && descriptor is not null ? MechanicalDeterminacy.DeterminedBySelectedInput :
            (!g.PinPresent || !g.IdealLock.Present) && local.HasValidDriverMounting ? MechanicalDeterminacy.UndrivenRelativeMotion :
            local.Verdict == ConnectionCompatibilityVerdict.Unsupported ? MechanicalDeterminacy.UnsupportedConstraintDomain : MechanicalDeterminacy.BlockedByInvalidConstraint;
        var (target, referenceNumeric) = AssessRequirement(g, d.Output.Key, local, descriptor, d.Requirement, issues);
        checks.Add(new("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Current complete retained source analysis, not a copied gain."));
        checks.Add(new("SourceClearance", "source", ConvertGeneva(source.Geometry), true, "Existing retained source clearance only; added-body swept clearance is separate and not performed."));
        checks.Add(new("PinSlotCenterlineClosure", g.Id, local.PinSlotGeometryAdmitted ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Inconclusive, true,
            "Positive-denominator pin-vector direction defines radial-slot output on the admitted tangent-entry profile."));
        checks.Add(new("IdealArcLockMate", g.Id, local.LockGeometryAdmitted ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true,
            "Matched finite concave recess and rotation-invariant convex circle with bilateral witness normals; phase release remains ideal."));
        checks.Add(new("IndexDwellBoundaryContinuity", g.Id, local.PinSlotGeometryAdmitted ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Inconclusive, true,
            "At +/-a exact residual is +/-b and the derivative is zero; adjacent accumulated dwell expressions match, including the centered cycle seam."));
        checks.Add(new("UnwrappedAngularMotion", g.Id, descriptor is not null && local.IsAdmitted ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true,
            "Absolute BigInteger cycle and registered material IDs; no event counter or previous-pose memory."));
        checks.Add(new("OutputRequirements", d.Output.Key, ConvertGeneva(target), true, "Independent index, driver-angle dwell fraction and exact-root output requirements."));
        if (descriptor is null || !local.IsAdmitted)
            issues.Add(new("UndeterminedIntermittentOutput", "GenevaDeterminacy", affectedOutputs: new[] { d.Output.Key },
                detail: "No admitted full-cycle output without the current source, pin-slot geometry, assembly and maintained ideal lock. Nominal recipes are conditional only."));
        var unperformed = UnperformedDomains;
        foreach (var domain in unperformed.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(c => c.Domain == domain)) continue;
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new(domain, g.Id, OrientedCheckVerdict.NotPerformed, required, "Outside the bounded point-pin/ideal-phase-released-lock position profile."));
            if (required) issues.Add(new("RequiredValidationNotPerformed", domain, affectedOutputs: new[] { d.Output.Key }, detail: "The caller requires a domain not performed by this profile."));
        }
        return new(draft, source, local, retained, descriptor, determinacy, target, referenceNumeric, checks, issues);
    }


    internal static (MechanicalAxisVerdict Target, GenevaNumericComputation? ReferenceNumeric) AssessRequirement(GenevaDeviceDefinition g,
        string outputKey, GenevaCompatibilityResult local, GenevaMotionDescriptor? descriptor, GenevaRequirement requirement,
        List<MechanicalDiagnostic> issues, GenevaNumericRequest? remainingRequest = null)
    {
        var target = MechanicalAxisVerdict.Pass; GenevaNumericComputation? referenceNumeric = null;
        void TargetFailure(string code, string detail)
        { target = MechanicalAxisVerdict.Fail; issues.Add(new(code, "OutputRequirements", affectedOutputs: new[] { outputKey }, detail: detail)); }
        if (requirement.RequiredIndexStep.HasValue)
        {
            var step = requirement.RequiredIndexStep.Value;
            if (step.Kind != QuantityKind.AngularPosition || step.Value <= 0) TargetFailure("DimensionMismatch", "Index-step requirement is a positive angular magnitude in turns.");
            else if (g.Wheel.SlotCount < 4 || g.Wheel.SlotCount > 12) target = MechanicalAxisVerdict.NotAssessed;
            else if (step.Value != new Rational(1, g.Wheel.SlotCount)) TargetFailure("TargetMismatch", "Selected slot count does not satisfy independently required index-step magnitude.");
        }
        if (requirement.RequiredDwellFraction.HasValue)
        {
            var dwell = requirement.RequiredDwellFraction.Value;
            if (dwell < 0 || dwell > 1) TargetFailure("DimensionMismatch", "Dwell fraction is dimensionless and between zero and one.");
            else if (g.Wheel.SlotCount < 4 || g.Wheel.SlotCount > 12) { if (target != MechanicalAxisVerdict.Fail) target = MechanicalAxisVerdict.NotAssessed; }
            else if (dwell != new Rational(1, 2) + new Rational(1, g.Wheel.SlotCount)) TargetFailure("TargetMismatch", "Derived driver-angle dwell fraction differs from its independent target.");
        }
        if (requirement.ReferenceRoot.Kind != QuantityKind.AngularPosition || requirement.RequiredReferenceOutput.HasValue && requirement.RequiredReferenceOutput.Value.Kind != QuantityKind.AngularPosition)
            TargetFailure("DimensionMismatch", "Reference root and output are angular turns.");
        else if (requirement.RequiredReferenceOutput.HasValue)
        {
            if (descriptor is null) { if (target != MechanicalAxisVerdict.Fail) target = MechanicalAxisVerdict.NotAssessed; }
            else
            {
                var reference = descriptor.At(requirement.ReferenceRoot);
                if (reference.ExactTerminalTurns.HasValue)
                { if (reference.ExactTerminalTurns.Value != requirement.RequiredReferenceOutput.Value.Value) TargetFailure("TargetMismatch", "Exact reference output differs from the selected requirement."); }
                else
                {
                    var options = remainingRequest ?? local.Request;
                    var remaining = remainingRequest is null ? Math.Max(0, options.MaximumWork - local.NumericWork) : options.MaximumWork;
                    var referenceRequest = new GenevaNumericRequest(options.AngularWidth, options.LinearWidth, options.DirectionWidth, remaining,
                        options.MaximumPrecisionBits, options.MaximumRefinements, options.Policy);
                    referenceNumeric = GenevaNumerics.Evaluate(NumericInput(reference, false), referenceRequest);
                    var interval = referenceNumeric.Pose?.TerminalTurns;
                    if (interval is not null && (requirement.RequiredReferenceOutput.Value.Value < interval.Lower || requirement.RequiredReferenceOutput.Value.Value > interval.Upper))
                        TargetFailure("TargetMismatch", "Certified reference angle excludes the exact requested value.");
                    else if (target != MechanicalAxisVerdict.Fail) target = MechanicalAxisVerdict.Inconclusive;
                    if (interval is null) issues.Add(new(referenceNumeric.Status.ToString(), "OutputRequirements", affectedOutputs: new[] { outputKey }, detail: referenceNumeric.Detail));
                }
            }
        }
        return (target, referenceNumeric);
    }
    private static OrientedCheckVerdict ConvertGeneva(MechanicalAxisVerdict verdict) => verdict == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        verdict == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : verdict == MechanicalAxisVerdict.Inconclusive ? OrientedCheckVerdict.Inconclusive : OrientedCheckVerdict.NotPerformed;

    public static GenevaPoseRecipe? CreatePoseRecipe(GenevaAnalysis analysis, ExactQuantity root) =>
        (analysis ?? throw new ArgumentNullException(nameof(analysis))).CreatePoseRecipe(root);

    public static GenevaEvaluation Evaluate(GenevaAnalysis analysis, ExactQuantity root, GenevaNumericRequest? numericRequest = null)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        var request = numericRequest ?? GenevaNumericRequest.Default; var d = analysis.Draft.Definition; var g = d.Device;
        var issues = new List<MechanicalDiagnostic>(); var source = Array.Empty<OrientedShaftEvaluation>();
        GenevaDriverObservation? driver = null; GenevaPoseRecipe? recipe = null; GenevaNumericComputation? numeric = null; GenevaDriverNumericComputation? driverNumeric = null;
        GenevaEvaluation Result(GenevaEvaluationStatus status) => new(analysis, root, request, status, source, driver, recipe, numeric, driverNumeric, issues);
        void Issue(string code, string detail) => issues.Add(new(code, "GenevaEvaluation", affectedOutputs: new[] { d.Output.Key }, detail: detail));
        if (root.Kind != QuantityKind.AngularPosition) { Issue("DimensionMismatch", "Absolute root uses angular turns."); return Result(GenevaEvaluationStatus.DimensionMismatch); }
        try
        {
            MechanicalAuthoringProfile.Number(root.Value);
            source = PrismaticAlgebra.EvaluateSource(analysis.SourceAnalysis, d.SourceMapping, root.Value).ToArray();
            if (analysis.HasDeterminedDriverMotion)
            {
                var local = analysis.Local; var turns = analysis.SourceRelation!.Value.Evaluate(root.Value);
                Rational? phi = local.GammaIn.HasValue && local.EpsilonIn.HasValue ? local.GammaIn.Value + local.EpsilonIn.Value * turns : null;
                GenevaProfile.Derived(turns); if (phi.HasValue) GenevaProfile.Derived(phi.Value);
                driver = new(g.DriverBodyId, g.DriverCenterMm, local.MappedSourceFrameMm!.Z, local.MappedSourceFrameMm.X, turns, phi);
            }
            recipe = analysis.Descriptor?.At(root);
            if (!GenevaNumerics.Valid(request)) { Issue("InvalidNumericRequest", "Separate turn/mm/direction widths and bounded numerical policy are required."); return Result(GenevaEvaluationStatus.InvalidNumericRequest); }
            if (!analysis.HasFullCycleMotion || recipe is null)
            {
                if (driver?.PhysicalPhaseTurns is Rational phi)
                    driverNumeric = GenevaNumerics.EvaluateDriver(phi, g.OrbitRadius, g.DriverCenterMm, g.CenterDirection, g.TransverseDirection, request, FeatureInputs(g, null, false));
                issues.AddRange(analysis.Diagnostics);
                return Result(analysis.Local.Verdict == ConnectionCompatibilityVerdict.Inconclusive ? GenevaEvaluationStatus.GeometryInconclusive :
                    !g.PinPresent || !g.IdealLock.Present || driver is null ? GenevaEvaluationStatus.UndeterminedIntermittentOutput : GenevaEvaluationStatus.InvalidDefinition);
            }
            numeric = GenevaNumerics.Evaluate(NumericInput(recipe, true), request);
            if (numeric.IsAvailable) return Result(GenevaEvaluationStatus.Success);
            Issue(numeric.Status.ToString(), numeric.Detail);
            return Result(NumericStatus(numeric.Status));
        }
        catch (CrankSliderNumerics.Stop stop)
        { Issue("NumericResourceLimit", stop.Message); return Result(NumericStatus(GenevaNumerics.Status(stop))); }
        catch (ArgumentException e) when (e.Message.Contains("bound") || e.Message.Contains("digit"))
        { Issue("NumericResourceLimit", e.Message); return Result(GenevaEvaluationStatus.NumericResourceLimit); }
    }

    private static GenevaEvaluationStatus NumericStatus(GenevaNumericStatus status) => status == GenevaNumericStatus.InvalidNumericRequest ? GenevaEvaluationStatus.InvalidNumericRequest :
        status == GenevaNumericStatus.NumericResourceLimit ? GenevaEvaluationStatus.NumericResourceLimit : GenevaEvaluationStatus.IncompleteNumericBudget;

    internal static GenevaNumericInput NumericInput(GenevaPoseRecipe recipe, bool samples)
    {
        var m = recipe.Motion; var g = m.Device;
        return new(recipe.CenteredPhaseTurns, m.CenterDistanceMm, g.OrbitRadius, recipe.AccumulatedShaftTurns, recipe.ExactResidualTurns,
            (int)m.EpsilonOut.Numerator, m.Output.TerminalSign, m.Output.TerminalDatum.Value, recipe.WheelMaterialStepTurns,
            g.DriverCenterMm, g.WheelCenterMm, g.CenterDirection, g.TransverseDirection, samples ? FeatureInputs(g, recipe, true) : null);
    }

    internal static IReadOnlyList<GenevaNumericFeatureInput> FeatureInputs(GenevaDeviceDefinition g, GenevaPoseRecipe? recipe, bool wheel)
    {
        var values = new List<GenevaNumericFeatureInput>(); var arc = g.IdealLock;
        if (arc.DriverRadius.Kind == QuantityKind.LinearPosition && arc.DriverRadius.Value > 0)
            for (int i = 0; i < 16; i++) values.Add(new(arc.DriverFeatureId + "/circle-" + i.ToString("D2"), false, GenevaLength.Millimeters(arc.DriverRadius.Value), new Rational(i, 16)));
        if (!wheel) return values;
        var count = g.Wheel.SlotCount;
        for (int i = 0; i < count; i++)
        {
            var slotTurns = new Rational(1, 2) + new Rational(i, count);
            values.Add(new(g.Wheel.SlotIds[i] + "/root", true, GenevaLength.Millimeters(g.Wheel.SlotRoot.Value), slotTurns));
            values.Add(new(g.Wheel.SlotIds[i] + "/mouth", true, g.Wheel.MouthRadius, slotTurns));
            var center = new Rational(1, 2) + new Rational(2 * i + 1, 2 * count) + arc.RecessMountingTurns.Value;
            var radius = GenevaLength.Millimeters(arc.RecessCenterDistance.Value); var lockRadius = GenevaLength.Millimeters(arc.RecessRadius.Value);
            values.Add(new(arc.RecessIds[i] + "/center", true, radius, center));
            var offsets = new[] { -arc.PatchHalfWidth.Value, Rational.Zero, arc.PatchHalfWidth.Value };
            for (int j = 0; j < offsets.Length; j++) values.Add(new(arc.RecessIds[i] + "/arc-" + j, true, radius, center, lockRadius, center + new Rational(1, 2) + offsets[j]));
            if (recipe?.RecessIndex == i)
            {
                values.Add(new(arc.RecessIds[i] + "/normal-witness-minus", true, radius, center, lockRadius, center + new Rational(1, 2) - arc.PatchHalfWidth.Value / 2));
                values.Add(new(arc.RecessIds[i] + "/normal-witness-plus", true, radius, center, lockRadius, center + new Rational(1, 2) + arc.PatchHalfWidth.Value / 2));
            }
        }
        for (int i = 0; i < 24; i++) values.Add(new(g.WheelBodyId + "/schematic-mouth-" + i.ToString("D2"), true, g.Wheel.MouthRadius, new Rational(i, 24)));
        return values;
    }
}
