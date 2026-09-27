using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class CrankSliderAnalyzer
{
    internal static readonly string[] UnperformedDomains = new[] { "BoundedNumericalEvaluation", "FullAddedBody/SweptSolidClearance", "Pin/Bearing/Guide physical contact", "Force/Torque/MechanicalStrength",
            "Inertia/Flywheel/Dynamics", "Friction/Elasticity/Impact", "Slider-driven inverse actuation", "Manufacturing" };
    public static CrankSliderCompatibilityResult Query(CrankSliderDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition;
        return QueryLocal(d.Device, d.Output, BoundMechanicalInputContext.FromLegacy(d.Source, d.SourceMapping, d.Device.SourceShaftId, d.Device.SourcePortId));
    }

    internal static CrankSliderCompatibilityResult QueryLocal(PlanarCrankSliderDefinition c, PrismaticOutputDefinition output, BoundMechanicalInputContext input)
    {
        var source = input.LegacySource?.Definition;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var issues = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("CrankSlider", c.Id), new MechanicalReference("Shaft", c.SourceShaftId),
            new MechanicalReference("CrankBody", c.CrankBodyId), new MechanicalReference("RodBody", c.RodBodyId),
            new MechanicalReference("SliderBody", c.SliderBodyId), new MechanicalReference("Guide", c.GuideId),
            new MechanicalReference("CrankPin", c.CrankPinId), new MechanicalReference("SliderPin", c.SliderPinId) };
        bool unsupported = false;
        void Check(string domain, bool pass, string code, string detail, bool outsideProfile = false)
        {
            checks.Add(new(domain, c.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!pass) { unsupported |= outsideProfile; issues.Add(new MechanicalDiagnostic(code, domain, related: refs,
                facts: facts, affectedOutputs: new[] { output.Key }, scope: "CrankSliderLocal", detail: detail)); }
        }
        var imported = input.LegacySource?.OriginalArtifactBytes is not null;
        var standalone = source is not null && source.Shafts.Count == 1 && source.Shafts[0].IsPrescribed && source.RootShaftId == source.Shafts[0].Id &&
            source.RootShaftId == c.SourceShaftId && source.Bodies.Count == 0 && source.Contacts.Count == 0 && source.Connections.Count == 0 &&
            source.Outputs.Count == 0 && source.KeepOuts.Count == 0 && source.Ports.All(p => p.ShaftId == source.RootShaftId);
        Check("SupportedSourceProfile", !input.IsLegacy ? input.BindingAdmitted : imported ? input.LegacySource!.ImportedProfile == MechanicalAuthoringProfile.PlanarExport ||
            OrientedTwoOutputProfile.IsSupported(input.LegacySource!.ImportedProfile ?? "") : standalone,
            "UnsupportedSourceProfile", "Only the complete supported planar/oriented source or one genuine standalone prescribed retained shaft is accepted.", true);
        var prescribed = (source is null ? input.RootInputId is not null : source.Shafts.Count(s => s.IsPrescribed) == 1 && source.Shafts.Any(s => s.Id == source.RootShaftId && s.IsPrescribed)) && !c.SliderIsPrescribed;
        Check("SinglePrescribedInput", prescribed, "UnsupportedInputTopology", "The retained source has one input; the slider is never a second prescribed input.", true);
        var bodies = new[] { c.CrankBodyId, c.RodBodyId, c.SliderBodyId };
        var distinct = bodies.Distinct(StringComparer.Ordinal).Count() == 3 && bodies.All(id => !input.HasBodyId(id)) &&
            !input.HasShaftId(c.LinearDofId) && c.CrankPinId != c.SliderPinId;
        Check("SeparateMovingBodies", distinct, "InvalidBodyIdentity", "Three distinct added bodies and one distinct slider coordinate preserve all source bodies.");
        var outputValid = output.BodyId == c.SliderBodyId && output.LinearDofId == c.LinearDofId && output.ReferencePointId == c.SliderPinId &&
            (output.TerminalSign == -1 || output.TerminalSign == 1) && output.TerminalDatum.Kind == QuantityKind.LinearPosition;
        Check("OutputBinding", outputValid, "MissingEndpoint", "Output must bind the declared slider body, coordinate and slider pin with a dimensioned terminal calibration.");
        var radiusValid = c.CrankRadius.Kind == QuantityKind.LinearPosition && c.CrankRadius.Value > 0;
        var rodValid = c.RodLength.Kind == QuantityKind.LinearPosition && c.RodLength.Value > 0;
        Check("CrankRadius", radiusValid, "InvalidCrankRadius", "Crank radius is a positive pin-center distance in mm.");
        Check("RodLength", rodValid, "InvalidRodLength", "Rod length is a positive pin-to-pin distance in mm.");
        var mappingValid = input.MappingValid;
        Check("SourceLengthMapping", mappingValid, "InvalidSourceLengthMapping", "One positive explicit source-unit scale and proper cardinal pose map retained geometry to mm.");
        var shaft = input.Shaft;
        Check("SourceShaftBinding", shaft?.Frame.IsProperCardinal == true, "InvalidCrankMounting", "Crank mounts to the actual retained shaft.");
        OrientedFrame? mapped = null; Rational? epsilon = null, gamma = null; CrankSliderEnvelope? envelope = null;
        var mounting = false; var plane = false; var frameValid = c.GuideFrameMm.IsProperCardinal;
        Check("GuideFrame", frameValid, "InvalidGuideDirection", "Guide orientation is proper cardinal; Z is increasing guide position.");
        var basisValid = c.PlaneNormal.IsCardinal && c.GuideFrameMm.Z.IsCardinal && c.PlaneNormal.Dot(c.GuideFrameMm.Z) == 0;
        Check("PlanarJoint/GuideCompatibility", basisValid, "InvalidGuideDirection", "Plane normal and guide direction are cardinal and perpendicular.");
        try
        {
            if (mappingValid && shaft?.Frame.IsProperCardinal == true)
            {
                mapped = input.FixedFrameMm!;
                mounting = c.PivotStation.Kind == QuantityKind.LinearPosition && c.MountingTurns.Kind == QuantityKind.AngularPosition && c.CrankAxisFixed &&
                    c.PivotMm == mapped.Origin + mapped.Z * c.PivotStation.Value;
                if (c.SourcePortId is not null)
                {
                    var port = input.Port;
                    var portValid = port is not null && port.ShaftId == c.SourceShaftId && port.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(port, shaft);
                    Check("SourcePortBinding", portValid, "MissingEndpoint", "An optional source port is mounting evidence only; its sign is never applied twice.");
                    mounting &= portValid;
                    if (portValid) facts.Add(new("sourcePortCoordinateSign.evidenceOnly", null, shaft.Frame.Z.Dot(port!.Frame.Z)));
                }
                if (basisValid)
                {
                    plane = mapped.Z.Cross(c.PlaneNormal) == ExactVector3.Zero && c.PlaneNormal.Dot(c.GuideFrameMm.Origin - c.PivotMm) == 0;
                    if (mapped.Z.Cross(c.PlaneNormal) == ExactVector3.Zero && c.MountingTurns.Kind == QuantityKind.AngularPosition)
                    {
                        epsilon = mapped.Z.Dot(c.PlaneNormal);
                        gamma = PitchChainGeometry.QuarterTurn(mapped.X, c.GuideFrameMm.Z, c.PlaneNormal.Cross(c.GuideFrameMm.Z)) + epsilon.Value * c.MountingTurns.Value;
                        MechanicalDerivedNumbers.Check(gamma.Value);
                    }
                }
            }
            Check("CrankMounting", mounting, "InvalidCrankMounting", "The explicit pivot equals the actual mapped shaft origin plus its mm station; axis and mounting phase are valid.");
            Check("MechanismCoplanarity", plane, "NonCoplanarGuide", "Guide and pivot occupy one plane whose normal is collinear with the retained positive shaft axis.");
            if (basisValid)
            {
                var relative = c.GuideFrameMm.Origin - c.PivotMm; var e = relative.Dot(c.PlaneNormal.Cross(c.GuideFrameMm.Z)); var datum = relative.Dot(c.GuideFrameMm.Z);
                MechanicalDerivedNumbers.Check(e); MechanicalDerivedNumbers.Check(datum);
                facts.Add(new("guideOffset.mm", null, e)); facts.Add(new("guideDatum.mm", null, datum));
                if (radiusValid && rodValid)
                {
                    var margin = c.RodLength.Value - c.CrankRadius.Value - CrankSliderGeometry.Abs(e); MechanicalDerivedNumbers.Check(margin);
                    facts.Add(new("strictFullCycleMargin.mm", null, margin));
                    Check("StrictFullCycleAssembly", margin > 0, margin == 0 ? "BranchMergerExcluded" : "UnsupportedFullCycleClosure",
                        "Strict l>r+abs(e) separates both branches at every phase. Refusal does not assert impossibility at every input.", true);
                    if (margin > 0 && c.AssemblyBranch is -1 or 1) envelope = new(c.CrankRadius.Value, c.RodLength.Value, datum, e, c.AssemblyBranch.Value);
                }
            }
        }
        catch (ArgumentException exception) when (exception.Message == "Derived exact digit bound exceeded." || exception.Message == "Exact quantity resource digit bound exceeded.")
        { Check("GeometryResourceBounds", false, "NumericResourceLimit", exception.Message, true); mounting = false; envelope = null; }
        Check("BranchSelection", c.AssemblyBranch is -1 or 1, c.AssemblyBranch.HasValue ? "InvalidAssemblyBranch" : "MissingAssemblyBranch", "The signed assembly branch is explicitly authored.");
        Check("FixedLengthClosure", c.TransmissionPresent && c.CrankPinPresent && c.SliderPinPresent,
            !c.CrankPinPresent || !c.SliderPinPresent ? "MissingEndpoint" : "MissingRodConstraint", "Both rod pin joints and the connecting-rod constraint must be present.");
        Check("GuideConstraint", c.GuidePresent && c.SliderRotationFixed && c.TransverseMotionFixed,
            "MissingGuideConstraint", "Grounded guide, anti-rotation and transverse constraints define the reduced slider coordinate.");
        Check("GuideTravelDefinition", c.GuideTravel.Kind == QuantityKind.LinearPosition, "DimensionMismatch", "Finite closed guide travel is expressed in mm.");
        return new(c.Id, checks.All(k => k.Verdict == OrientedCheckVerdict.Pass) ? ConnectionCompatibilityVerdict.CompatibleWithinProfile :
            unsupported ? ConnectionCompatibilityVerdict.Unsupported : ConnectionCompatibilityVerdict.Incompatible,
            c.TransmissionPresent, mounting && radiusValid && distinct, mapped, gamma, epsilon, envelope, checks, facts, issues);
    }

    public static CrankSliderAnalysis Analyze(CrankSliderDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var source = MechanicalAnalyzer.Analyze(draft.Definition.Source);
        try { return AnalyzeCore(draft, source); }
        catch (ArgumentException exception) when (exception.Message == "Derived exact digit bound exceeded." || exception.Message == "Exact quantity resource digit bound exceeded.")
        {
            var issue = new MechanicalDiagnostic("NumericResourceLimit", "CrankSliderAnalysis", affectedOutputs: new[] { draft.Definition.Output.Key },
                detail: "The exact derived representation limit was reached. This is not a proof of physical impossibility. " + exception.Message);
            var check = new OrientedDomainCheck("ExactDerivedResourceBounds", draft.Definition.Device.Id, OrientedCheckVerdict.Inconclusive, true, issue.Detail);
            var local = new CrankSliderCompatibilityResult(draft.Definition.Device.Id, ConnectionCompatibilityVerdict.Inconclusive,
                draft.Definition.Device.TransmissionPresent, false, null, null, null, null, new[] { check }, Array.Empty<MechanicalExactFact>(), new[] { issue });
            return new(draft, source, local, MechanicalDeterminacy.UnsupportedConstraintDomain, null, null,
                MechanicalAxisVerdict.NotAssessed, MechanicalAxisVerdict.NotAssessed, false, false, Array.Empty<string>(), Array.Empty<string>(),
                new[] { "ExactDerivedResourceBounds" }, new[] { check }, source.Diagnostics.Concat(new[] { issue }));
        }
    }

    private static CrankSliderAnalysis AnalyzeCore(CrankSliderDraft draft, MechanicalAnalysis source)
    {
        var d = draft.Definition; var c = d.Device; var local = Query(draft);
        var issues = new List<MechanicalDiagnostic>(source.Diagnostics.Concat(local.Diagnostics)); var checks = new List<OrientedDomainCheck>(local.Checks);
        var component = source.AdmittedComponents.FirstOrDefault(k => k.ShaftIds.Contains(c.SourceShaftId));
        var declared = source.DeclaredComponents.FirstOrDefault(k => k.ShaftIds.Contains(c.SourceShaftId));
        var retainedNode = component?.Affine?.Relations.FirstOrDefault(k => k.DofId == c.SourceShaftId);
        ExactAffineRelation? retained = component?.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput ? retainedNode?.Relation : null;
        var sourcePath = retainedNode?.ConstraintPath.ToArray() ?? Array.Empty<string>();
        var dependency = "CrankSlider/" + c.Id;
        var declaredReachable = c.TransmissionPresent && declared?.IsSelectedInputReachable == true;
        var admittedReachable = retained.HasValue && local.IsAdmitted;
        var declaredPath = declaredReachable && source.SelectedInputId is not null ? MechanicalAnalyzer.Path(source.SelectedInputId, c.SourceShaftId,
            source.Edges.Where(e => e.IsDeclaredResolvable)).Concat(new[] { dependency }).ToArray() : Array.Empty<string>();
        var blocked = declaredPath.Where(key => key == dependency ? !local.IsAdmitted : !source.Edges.Single(e => e.ConstraintKey == key).IsAdmitted).ToList();
        if (!c.TransmissionPresent) blocked.Add(dependency); if (!c.GuidePresent || !c.SliderRotationFixed || !c.TransverseMotionFixed) blocked.Add("Guide/" + c.GuideId);
        var determinacy = component?.Affine?.Determinacy ?? MechanicalDeterminacy.BlockedByInvalidConstraint;
        var disconnected = declared?.IsSelectedInputReachable != true;
        if (declared?.IsSelectedInputReachable == true && !retained.HasValue) determinacy = MechanicalDeterminacy.BlockedByInvalidConstraint;
        if (!local.IsAdmitted) determinacy = !c.TransmissionPresent && local.HasValidCrankMounting && c.GuidePresent && c.CrankPinPresent && c.SliderPinPresent ?
            MechanicalDeterminacy.UndrivenRelativeMotion : local.Verdict == ConnectionCompatibilityVerdict.Unsupported ? MechanicalDeterminacy.UnsupportedConstraintDomain : MechanicalDeterminacy.BlockedByInvalidConstraint;
        CrankSliderMotionDescriptor? descriptor = null;
        if (local.IsAdmitted && retained.HasValue) descriptor = new(c, d.Output, local.MappedShaftFrameMm!, retained.Value);
        void Issue(string code, string stage, string detail) => issues.Add(new MechanicalDiagnostic(code, stage,
            related: new[] { new MechanicalReference("Output", d.Output.Key), new MechanicalReference("CrankSlider", c.Id) },
            affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: blocked, detail: detail));
        if (descriptor is null) Issue(disconnected ? "DisconnectedFromInput" : !c.TransmissionPresent ? "UndrivenLinearCoordinate" : determinacy.ToString(),
            "NonlinearDependency", "No absolute slider position or connected rod pose is supplied without all current source and joint prerequisites.");
        var coverage = local.Envelope is null || c.GuideTravel.Kind != QuantityKind.LinearPosition ? MechanicalAxisVerdict.NotAssessed :
            local.Envelope.Covers(c.GuideTravel) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
        if (coverage == MechanicalAxisVerdict.Fail) Issue("GuideDoesNotCoverFullCycle", "GuideTravelCoverage", "The current finite guide interval does not contain the attained full-cycle envelope; no range repair is performed.");
        var target = AssessRequirement(local, descriptor, d.Requirement, Issue);
        checks.Add(new("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Complete retained source is analyzed independently and must finalize through21A."));
        checks.Add(new("SourceClearance", "source", Convert(source.Geometry), true, "Existing source clearance only; added moving solids are not assessed."));
        checks.Add(new("FullCycleSliderEnvelope", c.Id, local.Envelope is not null ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Inconclusive, true, "Exact triangle bounds with explicit attainment witnesses."));
        checks.Add(new("GuideTravelCoverage", c.Id, Convert(coverage), true, "Sign-aware radical comparison against both finite guide limits."));
        checks.Add(new("NonlinearOutputRequirements", c.Id, Convert(target), true, "Stroke and explicit-root terminal requirements do not alter geometry."));
        checks.Add(new("WholeMotionDeterminacy", c.Id, descriptor is not null ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "One source-driven crank with derived rod and slider, no affine slider edge."));
        foreach (var domain in UnperformedDomains.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            var existing = checks.FindIndex(k => k.Domain == domain);
            if (existing >= 0) continue;
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new(domain, c.Id, OrientedCheckVerdict.NotPerformed, required, domain == "BoundedNumericalEvaluation" ?
                "Numerical availability is request-specific and is assessed only by Evaluate; it is not mechanical artifact identity." : "NotPerformed by this bounded position-kinematics profile."));
            if (required) Issue("RequiredValidationNotPerformed", domain, "Requested validation domain was not performed; finalization is blocked.");
        }
        return new(draft, source, local, determinacy, retained, descriptor, coverage, target, declaredReachable, admittedReachable,
            descriptor is null ? Array.Empty<string>() : sourcePath.Concat(new[] { dependency }), declaredPath, blocked, checks, issues);
    }


    internal static MechanicalAxisVerdict AssessRequirement(CrankSliderCompatibilityResult local, CrankSliderMotionDescriptor? descriptor,
        CrankSliderRequirement requirement, Action<string, string, string> Issue)
    {
        var target = MechanicalAxisVerdict.Pass;
        if (requirement.RequiredStroke.HasValue)
        {
            if (requirement.RequiredStroke.Value.Kind != QuantityKind.LinearPosition || requirement.RequiredStroke.Value.Value <= 0)
            { target = MechanicalAxisVerdict.Fail; Issue("DimensionMismatch", "NonlinearOutputRequirements", "Required stroke must be a positive mm quantity."); }
            else if (local.Envelope is null) target = MechanicalAxisVerdict.NotAssessed;
            else if (local.Envelope.CompareStroke(requirement.RequiredStroke.Value.Value) != 0)
            { target = MechanicalAxisVerdict.Fail; Issue("TargetMismatch", "NonlinearOutputRequirements", "Exact radical stroke differs from the independent requested stroke."); }
        }
        if (requirement.ReferenceRoot.Kind != QuantityKind.AngularPosition || requirement.RequiredReferencePosition.HasValue && requirement.RequiredReferencePosition.Value.Kind != QuantityKind.LinearPosition)
        { target = MechanicalAxisVerdict.Fail; Issue("DimensionMismatch", "NonlinearOutputRequirements", "Reference root uses turns and requested terminal position uses mm."); }
        else if (requirement.RequiredReferencePosition.HasValue)
        {
            var exact = descriptor?.At(requirement.ReferenceRoot).ExactTerminalPosition;
            if (exact is null)
            { if (target != MechanicalAxisVerdict.Fail) target = MechanicalAxisVerdict.Inconclusive; Issue("ReferencePositionInconclusive", "NonlinearOutputRequirements", "Current bounded exact rules cannot prove this reference-position equality; numeric proximity is not equality."); }
            else if (exact.CompareTo(requirement.RequiredReferencePosition.Value.Value) != 0)
            { target = MechanicalAxisVerdict.Fail; Issue("TargetMismatch", "NonlinearOutputRequirements", "Exact terminal position at the explicit reference root differs from the requirement."); }
        }
        return target;
    }
    public static CrankSliderPoseRecipe? CreatePoseRecipe(CrankSliderAnalysis analysis, ExactQuantity rootTurns)
    { if (analysis is null) throw new ArgumentNullException(nameof(analysis)); return analysis.CreatePoseRecipe(rootTurns); }

    public static CrankSliderEvaluation Evaluate(CrankSliderAnalysis analysis, ExactQuantity rootTurns, CrankSliderNumericRequest? request = null)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); request ??= CrankSliderNumericRequest.Default;
        var d = analysis.Draft.Definition; var c = d.Device; var issues = new List<MechanicalDiagnostic>();
        var rotary = Array.Empty<OrientedShaftEvaluation>(); CrankSliderCrankEvaluation? crank = null;
        CrankSliderPoseRecipe? recipe = null; CrankSliderNumericComputation? numeric = null;
        CrankSliderEvaluation Result(CrankSliderEvaluationStatus status, CrankSliderTravelDecision travel = CrankSliderTravelDecision.NotAssessed) =>
            new(analysis, rootTurns, request, status, travel, rotary, crank, recipe, numeric, issues);
        void Issue(string code, string detail) => issues.Add(new MechanicalDiagnostic(code, "CrankSliderEvaluation", affectedOutputs: new[] { d.Output.Key }, detail: detail));
        if (rootTurns.Kind != QuantityKind.AngularPosition)
        { Issue("DimensionMismatch", "Absolute input must be unwrapped turns."); return Result(CrankSliderEvaluationStatus.DimensionMismatch); }
        try
        {
            MechanicalAuthoringProfile.Number(rootTurns.Value);
            rotary = PrismaticAlgebra.EvaluateSource(analysis.SourceAnalysis, d.SourceMapping, rootTurns.Value).ToArray();
            if (analysis.HasDeterminedCrankMotion)
            {
                var local = analysis.LocalCompatibility; var turns = analysis.SourceRelation!.Value.Evaluate(rootTurns.Value); MechanicalDerivedNumbers.Check(turns);
                Rational? phi = local.GammaTurns.HasValue && local.Epsilon.HasValue ? local.GammaTurns.Value + local.Epsilon.Value * turns : null;
                if (phi.HasValue) MechanicalDerivedNumbers.Check(phi.Value);
                crank = new(c.CrankBodyId, c.PivotMm, local.MappedShaftFrameMm!.Z, local.MappedShaftFrameMm.X, turns, c.MountingTurns.Value, phi);
            }
            if (analysis.Descriptor is null)
            {
                issues.AddRange(analysis.Diagnostics); return Result(analysis.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ?
                    CrankSliderEvaluationStatus.UndrivenLinearCoordinate : CrankSliderEvaluationStatus.InvalidDefinition);
            }
            recipe = analysis.Descriptor.At(rootTurns); var m = analysis.Descriptor;
            numeric = CrankSliderNumerics.Evaluate(recipe, request);
            var travel = AssessTravel(recipe, numeric, c.GuideTravel, analysis.GuideTravelCoverage);
            if (!numeric.IsAvailable)
            {
                Issue(numeric.Status.ToString(), numeric.Detail);
                return Result(numeric.Status == CrankSliderNumericStatus.InvalidNumericRequest ? CrankSliderEvaluationStatus.InvalidNumericRequest :
                    numeric.Status == CrankSliderNumericStatus.IncompleteNumericBudget ? CrankSliderEvaluationStatus.IncompleteNumericBudget : CrankSliderEvaluationStatus.NumericResourceLimit, travel);
            }
            if (travel == CrankSliderTravelDecision.OutOfRange)
            { Issue("OutOfGuideTravel", "The calculated slider pin is outside the current guide. Numeric pose is diagnostic only."); return Result(CrankSliderEvaluationStatus.OutOfGuideTravel, travel); }
            if (travel != CrankSliderTravelDecision.InRange)
            { Issue("TravelDecisionUnresolved", "The certified interval overlaps a travel boundary; no admitted frame is available."); return Result(CrankSliderEvaluationStatus.TravelDecisionUnresolved, travel); }
            return Result(CrankSliderEvaluationStatus.Success, travel);
        }
        catch (ArgumentException exception) when (exception.Message == "Exact input digit bound exceeded." || exception.Message == "Derived exact digit bound exceeded." || exception.Message == "Exact quantity resource digit bound exceeded.")
        { Issue("NumericResourceLimit", exception.Message); return Result(CrankSliderEvaluationStatus.NumericResourceLimit); }
    }

    internal static CrankSliderTravelDecision AssessTravel(CrankSliderPoseRecipe recipe, CrankSliderNumericComputation numeric,
        ExactQuantityInterval guideTravel, MechanicalAxisVerdict coverage)
    {
        var travel = CrankSliderTravelDecision.Unresolved;
        var exact = recipe.ExactGuidePosition;
        if (exact is not null) travel = exact.CompareTo(guideTravel.Lower.Value) >= 0 && exact.CompareTo(guideTravel.Upper.Value) <= 0 ? CrankSliderTravelDecision.InRange : CrankSliderTravelDecision.OutOfRange;
        else if (coverage == MechanicalAxisVerdict.Pass) travel = CrankSliderTravelDecision.InRange;
        else if ((numeric.Pose ?? numeric.DiagnosticPose) is { } p)
        {
            var interval = p.GuidePositionMm;
            if (interval.Lower >= guideTravel.Lower.Value && interval.Upper <= guideTravel.Upper.Value) travel = CrankSliderTravelDecision.InRange;
            else if (interval.Upper < guideTravel.Lower.Value || interval.Lower > guideTravel.Upper.Value) travel = CrankSliderTravelDecision.OutOfRange;
        }
        return travel;
    }
    private static OrientedCheckVerdict Convert(MechanicalAxisVerdict verdict) => verdict == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        verdict == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : verdict == MechanicalAxisVerdict.NotAssessed ? OrientedCheckVerdict.NotPerformed : OrientedCheckVerdict.Inconclusive;
}
