using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Exact current independent specimens and declared shaft stations; no requested ratio supplies a relation.</summary>
public static class WormDriveConnectionQuery
{
    public static WormDriveCompatibilityResult Query(WormDriveDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition;
        return QueryLocal(d.Device, d.OutputTerminal, d.Output, BoundMechanicalInputContext.FromLegacy(d.Source, d.SourceMapping, d.Device.InputShaftId, d.Device.InputPortId));
    }

    internal static WormDriveCompatibilityResult QueryLocal(WormDriveTransmissionDefinition device,
        ShaftPort outputTerminal, MechanicalOutput output, BoundMechanicalInputContext context)
    {
        var c = device; var w = c.Worm; var g = c.SelectedWheel; var mapping = context.MappingEvidence;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var issues = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("WormDrive", c.Id), new MechanicalReference("Shaft", c.InputShaftId),
            new MechanicalReference("Shaft", c.OutputShaft.Id), new MechanicalReference("WormBody", c.InputWormBodyId),
            new MechanicalReference("WormWheelBody", c.OutputWheelBodyId), new MechanicalReference("Output", output.Key), new MechanicalReference("Port", outputTerminal.Id) };
        var unsupported = false;
        void Check(string domain, bool pass, string code, string detail, bool unsupportedDomain = false, IEnumerable<MechanicalExactFact>? exact = null)
        {
            checks.Add(new OrientedDomainCheck(domain, c.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (pass) return;
            unsupported |= unsupportedDomain;
            issues.Add(new MechanicalDiagnostic(code, "WormDriveCompatibility", related: refs, facts: exact,
                affectedOutputs: new[] { output.Key }, scope: "LocalIdealCylindricalWormOnly", detail: detail));
        }
        var dimensions = w.AxialModule.Kind == QuantityKind.LinearPosition && w.PitchRadius.Kind == QuantityKind.LinearPosition &&
            (g is null || g.TransverseModule.Kind == QuantityKind.LinearPosition) &&
            c.InputPitchStation.Kind == QuantityKind.LinearPosition && c.OutputPitchStation.Kind == QuantityKind.LinearPosition &&
            new[] { c.InputReferenceTurns, c.OutputReferenceTurns, c.InputMountingPhase, c.OutputMountingPhase }.All(v => v.Kind == QuantityKind.AngularPosition);
        Check("Dimensions", dimensions, "DimensionMismatch", "Module/radius/stations are canonical mm; mounting/reference coordinates are unwrapped turns.");
        Check("WormProfile", c.Profile == WormDriveProfile.Id && w.GeometryKind == WormDriveProfile.WormGeometry,
            "UnsupportedProfile", "Only the cylindrical fixed orthogonal cardinal worm profile is supported.", true);
        Check("AxialModuleParameterization", w.Parameterization == WormDriveProfile.AxialParameterization &&
            (g is null || g.Parameterization == WormDriveProfile.WheelParameterization),
            "UnsupportedParameterization", "Axial worm module and transverse wheel module must be declared explicitly; normal module is not interchangeable.", true);
        Check("SelectedIdealWheel", g is not null, "MissingWheelSpecification", "An independent ideal wheel specification must be explicitly selected.");
        Check("WheelTraceSemantics", g is null || g.TraceSemantics == WormDriveProfile.WheelTraceSemantics,
            "UnsupportedWheelTraceSemantics", "Only the declared ideal local tooth-trace semantics are supported.", true);
        var wormNumbers = w.Starts >= WormDriveProfile.MinStarts && w.Starts <= WormDriveProfile.MaxStarts &&
            w.AxialModule.Kind == QuantityKind.LinearPosition && w.PitchRadius.Kind == QuantityKind.LinearPosition &&
            w.AxialModule.Value > 0 && w.PitchRadius.Value > 0 && (w.Handedness == 1 || w.Handedness == -1);
        Check("WormSpecification", wormNumbers, "InvalidDefinition", "Worm starts are 1..8, axial module and independent pitch radius positive lengths, physical hand +/-1.");
        var wheelNumbers = g is not null && g.ToothCount >= WormDriveProfile.MinTeeth && g.ToothCount <= WormDriveProfile.MaxTeeth &&
            g.ToothCount > w.Starts && g.TransverseModule.Kind == QuantityKind.LinearPosition && g.TransverseModule.Value > 0 &&
            (g.Handedness == 1 || g.Handedness == -1) && g.TraceSlope > 0;
        Check("WheelSpecification", wheelNumbers, "InvalidDefinition", "Wheel teeth are 8..4096 and exceed starts; module and trace slope are positive, physical hand +/-1.");
        var pitch = wormNumbers && wheelNumbers && w.AxialModule.Value == g!.TransverseModule.Value;
        Check("AxialTransversePitchCompatibility", pitch, "PitchMismatch", "pi*mW equals pi*mG exactly; a matching requested ratio cannot override mismatched modules.",
            exact: new[] { new MechanicalExactFact("axialVsTransverseModule.mm", w.AxialModule.Value, g?.TransverseModule.Value) });
        var slope = wormNumbers ? (Rational?)(w.Starts * w.AxialModule.Value / (2 * w.PitchRadius.Value)) : null;
        if (slope.HasValue) MechanicalDerivedNumbers.Check(slope.Value);
        var handMatch = wormNumbers && wheelNumbers && g!.Handedness == w.Handedness;
        var slopeMatch = slope.HasValue && wheelNumbers && g!.TraceSlope == slope.Value;
        Check("WheelHandCompatibility", handMatch, "WheelHandMismatch", "Independent wheel and worm physical local hands must agree.");
        Check("WheelSlopeCompatibility", slopeMatch, "WheelHelixSlopeMismatch", "Independent selected wheel trace slope must equal the current derived worm lead slope.",
            exact: new[] { new MechanicalExactFact("requiredVsSelectedTraceSlope", slope, g?.TraceSlope) });
        var mappingValid = context.MappingValid;
        Check("SourceLengthMapping", context.MappingScaleValid, "MissingSourceLengthMapping", "Source geometry receives one explicit positive mm/source-unit scale; new stations already use mm.");
        Check("SourcePose", context.MappingPoseValid, "InvalidSourcePose", "Source pose is a proper cardinal rigid transform.");
        Check("GroundedShaftLines", c.InputCenterFixed && c.InputAxisFixed && c.OutputCenterFixed && c.OutputAxisFixed,
            "UnsupportedGrounding", "Both shaft centerlines are explicitly ground-fixed.", true);
        var distinctBodies = c.InputWormBodyId != c.OutputWheelBodyId && !context.HasBodyId(c.InputWormBodyId) && !context.HasBodyId(c.OutputWheelBodyId);
        Check("DistinctWormAndWheelBodies", distinctBodies, "InvalidWormMounting", "Separate worm/wheel bodies must not replace each other or any retained source gear.");
        var input = context.Shaft;
        Check("InputShaftBinding", input is not null && input.Frame.IsProperCardinal, "MissingEndpoint", "Worm binds the actual retained source shaft.");
        var outputFrameValid = c.OutputShaft.Frame.IsProperCardinal;
        Check("OutputShaftFrame", outputFrameValid, "InvalidWheelMounting", "The new shaft has a proper cardinal frame.");
        Check("SinglePrescribedInput", !c.OutputShaft.IsPrescribed, "HiddenPrescribedOutput", "The added shaft is non-prescribed; its reference is not a driver.", true);
        var binding = output.IsResolved && output.ShaftId == c.OutputShaft.Id && output.BodyId == c.OutputWheelBodyId &&
            output.PortId == outputTerminal.Id && outputTerminal.ShaftId == c.OutputShaft.Id &&
            !context.HasOutputId(output.Key) && !context.HasPortId(outputTerminal.Id);
        Check("OutputBinding", binding, "MissingEndpoint", "Output binds the distinct shaft, wheel and terminal while preserving all source outputs.");
        // PhaseOffset is a worm-local terminal calibration, not a source rigid-port phase constraint.
        var zeroDatumPort = new ShaftPort(outputTerminal.Id, outputTerminal.ShaftId, outputTerminal.Frame, 0, outputTerminal.Kind);
        var terminalValid = outputFrameValid && outputTerminal.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(zeroDatumPort, c.OutputShaft);
        Check("TerminalMounting", terminalValid, "InvalidOutputMounting", "Terminal has proper same-zero-ray shaft-line geometry; its explicit unwrapped calibration is applied once.");
        ExactVector3? aw = null, ag = outputFrameValid ? c.OutputShaft.Frame.Z : null, cw = null;
        Rational? epsilon = null, sigma = null, portSign = null, terminalSign = terminalValid ? c.OutputShaft.Frame.Z.Dot(outputTerminal.Frame.Z) : null;
        if (mappingValid && input is not null && input.Frame.IsProperCardinal)
        {
            var frame = context.FixedFrameMm!; aw = frame.Z; cw = frame.Origin + frame.Z * c.InputPitchStation.Value;
            Check("WormMounting", distinctBodies && c.InputPitchStation.Kind == QuantityKind.LinearPosition &&
                (cw.Value - frame.Origin).Cross(frame.Z) == ExactVector3.Zero, "InvalidWormMounting", "The separate worm pitch station lies on the mapped retained shaft; signed station is already mm.");
            if (c.InputPortId is not null)
            {
                var p = context.Port;
                var valid = p is not null && p.ShaftId == input.Id && p.Frame.IsProperCardinal && context.PortIsMounted;
                Check("SourcePortBinding", valid, "InvalidSourcePortBinding", "Original retained port sign is evidence only, not another angular multiplier.");
                if (valid) portSign = input.Frame.Z.Dot(p!.Frame.Z);
            }
        }
        else
        {
            Check("WormMounting", false, "InvalidWormMounting", "A proper actual input shaft and positive source mapping are required.");
            if (c.InputPortId is not null) Check("SourcePortBinding", false, "InvalidSourcePortBinding", "Cannot bind a port without a valid retained shaft and mapping.");
        }
        Check("WheelMounting", distinctBodies && outputFrameValid && c.OutputPitchStation.Kind == QuantityKind.LinearPosition && binding && terminalValid,
            "InvalidWheelMounting", "The wheel mounts by its explicit signed pitch station on its own shaft and terminal.");
        var n = c.PhysicalHelixAxis; var b = c.ContactSide;
        var nValid = n.IsCardinal; var bValid = b.IsCardinal;
        Check("PhysicalHelixAxis", nValid && aw.HasValue && aw.Value.Cross(n) == ExactVector3.Zero, "InvalidPhysicalHelixAxis", "Physical material helix axis is cardinal and collinear with the retained shaft; it is independent of shaft coordinate sign.");
        var orthogonal = nValid && ag.HasValue && n.Dot(ag.Value) == 0 && aw.HasValue && aw.Value.Cross(n) == ExactVector3.Zero;
        Check("OrthogonalAxes", orthogonal, "NonOrthogonalAxes", "Actual input/output shaft directions must be orthogonal.");
        var sideValid = bValid && nValid && ag.HasValue && b.Dot(n) == 0 && b.Dot(ag.Value) == 0;
        Check("ContactSide", sideValid, "InvalidContactSide", "Declared cardinal side must be perpendicular to both shaft lines.");
        if (orthogonal && sideValid)
        { epsilon = aw!.Value.Dot(n); sigma = ag!.Value.Cross(b * -1).Dot(n); }
        var stationReady = cw.HasValue && outputFrameValid && dimensions;
        var cg = c.OutputPitchCenterMm; var delta = cw.HasValue ? cg - cw.Value : ExactVector3.Zero;
        var offsetOkay = stationReady && orthogonal && delta.Dot(n) == 0 && delta.Dot(ag!.Value) == 0;
        Check("PitchStationOffsets", offsetOkay, "PitchStationOffsetMismatch", "The declared station displacement is perpendicular to both axes; no alternate axial contact is sought.");
        var lineSeparation = orthogonal && bValid ? delta.Dot(n.Cross(ag!.Value)) : Rational.Zero;
        var nonintersecting = orthogonal && stationReady && lineSeparation != 0;
        Check("OrthogonalNonIntersectingAxes", nonintersecting, "UnsupportedIntersectingAxes", "Orthogonal centerlines have a nonzero common-perpendicular distance, not an intersection.");
        var requiredDistance = wormNumbers && wheelNumbers ? (Rational?)(w.PitchRadius.Value + g!.TransverseModule.Value * g.ToothCount / 2) : null;
        var distanceOkay = stationReady && sideValid && requiredDistance.HasValue && delta == b * requiredDistance.Value;
        Check("PitchStationDistance", distanceOkay, "PitchStationDistanceMismatch", "Declared displacement must equal (Rw+Rg)*b with its exact sign and distance.",
            exact: new[] { new MechanicalExactFact("requiredVsDeclaredSideDistance.mm", requiredDistance, bValid ? delta.Dot(b) : null) });
        var qW = cw.HasValue ? cw.Value + b * w.PitchRadius.Value : ExactVector3.Zero;
        var qG = g is not null ? cg - b * (g.TransverseModule.Value * g.ToothCount / 2) : ExactVector3.Zero;
        Check("CommonPitchStation", distanceOkay && offsetOkay && nonintersecting && qW == qG, "CommonPitchPointMismatch", "Both actual pitch geometries reconstruct the identical reference pitch point Q.");
        Rational? prospective = null;
        if (epsilon.HasValue && sigma.HasValue && wormNumbers && wheelNumbers)
        { prospective = -w.Handedness * epsilon.Value * sigma.Value * w.Starts / g!.ToothCount; MechanicalDerivedNumbers.Check(prospective.Value); }
        WormDriveGeometryDescriptor? geometry = null;
        if (orthogonal && sideValid && stationReady && wormNumbers && wheelNumbers)
            geometry = new WormDriveGeometryDescriptor(c, cw!.Value, aw!.Value);
        Check("LocalPitchTraceCompatibility", handMatch && slopeMatch && geometry is not null && geometry.WormTrace.Cross(geometry.WheelTrace) == ExactVector3.Zero,
            "LocalPitchTraceMismatch", "Current independent worm/wheel local traces are parallel at Q; this is not conjugate-flank validation.");
        Check("IdealHelicalPhaseProof", geometry?.HasExactProof == true && pitch && handMatch && slopeMatch,
            "InvalidHelicalPhaseRelation", "Independent axial-phase and material-velocity covector derivations must agree, with nonzero trace-parallel material sliding.");
        if (mapping is not null) facts.Add(new MechanicalExactFact("millimetersPerSourceUnit", null, mapping.MillimetersPerSourceUnit));
        facts.Add(new MechanicalExactFact("wormStarts", null, w.Starts)); facts.Add(new MechanicalExactFact("physicalHandedness", null, w.Handedness));
        facts.Add(new MechanicalExactFact("wormPitchRadius.mm", null, w.PitchRadius.Value));
        if (slope.HasValue) facts.Add(new MechanicalExactFact("leadSlope", null, slope));
        if (epsilon.HasValue) facts.Add(new MechanicalExactFact("epsilon.retainedAxisDotPhysicalAxis", null, epsilon));
        if (sigma.HasValue) facts.Add(new MechanicalExactFact("sigma.wheelTangentDotPhysicalAxis", null, sigma));
        if (portSign.HasValue) facts.Add(new MechanicalExactFact("sourcePortCoordinateSign.evidenceOnly", null, portSign));
        if (terminalSign.HasValue) facts.Add(new MechanicalExactFact("terminalCoordinateSign", null, terminalSign));
        facts.Add(new MechanicalExactFact("terminalCalibration.turn", null, outputTerminal.PhaseOffset));
        if (prospective.HasValue) facts.Add(new MechanicalExactFact("prospectiveTransfer.notAdmission", null, prospective));
        var all = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass);
        return new(c.Id, all ? ConnectionCompatibilityVerdict.CompatibleWithinProfile : unsupported ? ConnectionCompatibilityVerdict.Unsupported : ConnectionCompatibilityVerdict.Incompatible,
            c.TransmissionPresent, aw, ag, epsilon, sigma, portSign, terminalSign, prospective,
            all && c.TransmissionPresent ? geometry!.CovectorTransfer : null, geometry, checks, facts, issues);
    }
}

/// <summary>Fresh source constraints and one typed worm edge in the existing quantity affine component analyzer.</summary>
public static class WormDriveAnalyzer
{
    public static WormDriveCompatibilityResult Query(WormDriveDraft draft) => WormDriveConnectionQuery.Query(draft);
    public static WormDriveAnalysis Analyze(WormDriveDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var source = MechanicalAnalyzer.Analyze(d.Source); var local = WormDriveConnectionQuery.Query(draft);
        var edgeKey = "WormDrive/" + c.Id;
        var nodes = d.Source.Definition.Shafts.Select(s => new QuantityDof(s.Id, QuantityKind.AngularPosition, s.IsPrescribed))
            .Concat(new[] { new QuantityDof(c.OutputShaft.Id, QuantityKind.AngularPosition, c.OutputShaft.IsPrescribed) }).ToArray();
        var edges = MechanicalAnalyzer.LowerAdmitted(d.Source.Definition, source.Edges).Select(e => new QuantityAffineCoupling(e.Id, e.DriverDofId, e.DrivenDofId,
            ExactQuantity.FromCanonical(QuantityKind.AngularPerAngular, e.Transfer), ExactQuantity.FromCanonical(QuantityKind.AngularPosition, e.PhaseOffset))).ToList();
        if (local.IsAdmitted)
        {
            var q = local.AdmittedTransfer!.Value; var p = c.OutputReferenceTurns.Value - q * c.InputReferenceTurns.Value; MechanicalDerivedNumbers.Check(p);
            edges.Add(new QuantityAffineCoupling(edgeKey, c.InputShaftId, c.OutputShaft.Id,
                ExactQuantity.FromCanonical(QuantityKind.AngularPerAngular, q), ExactQuantity.FromCanonical(QuantityKind.AngularPosition, p)));
        }
        var selected = source.SelectedInputId is not null && nodes.Any(n => n.Id == source.SelectedInputId) ? source.SelectedInputId : null;
        var components = QuantityAffineComponentAnalyzer.Analyze(selected, nodes, edges); var outputComponent = components.Single(k => k.MemberIds.Contains(c.OutputShaft.Id));
        var sourceDeclared = source.DeclaredComponents.FirstOrDefault(k => k.ShaftIds.Contains(c.InputShaftId));
        var declaredReachable = c.TransmissionPresent && sourceDeclared?.IsSelectedInputReachable == true;
        var admittedReachable = selected is not null && outputComponent.MemberIds.Contains(selected);
        var declaredPath = declaredReachable ? MechanicalAnalyzer.Path(selected!, c.InputShaftId, source.Edges.Where(e => e.IsDeclaredResolvable)).Concat(new[] { edgeKey }).ToArray() : Array.Empty<string>();
        var blocked = declaredPath.Where(key => key == edgeKey ? !local.IsAdmitted : !source.Edges.Single(e => e.ConstraintKey == key).IsAdmitted).ToArray();
        var status = outputComponent.Determinacy;
        if (c.TransmissionPresent && local.Verdict != ConnectionCompatibilityVerdict.CompatibleWithinProfile)
            status = local.Verdict == ConnectionCompatibilityVerdict.Unsupported ? MechanicalDeterminacy.UnsupportedConstraintDomain : MechanicalDeterminacy.BlockedByInvalidConstraint;
        else if (declaredReachable && !admittedReachable) status = MechanicalDeterminacy.BlockedByInvalidConstraint;
        var endpointOkay = local.Checks.Where(k => new[] { "OutputBinding", "OutputShaftFrame", "TerminalMounting", "SinglePrescribedInput" }.Contains(k.Domain)).All(k => k.Verdict == OrientedCheckVerdict.Pass);
        if (!endpointOkay) status = MechanicalDeterminacy.BlockedByInvalidConstraint;
        var issues = new List<MechanicalDiagnostic>(local.Diagnostics); var outputIssues = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("Output", d.Output.Key), new MechanicalReference("WormDrive", c.Id), new MechanicalReference("Shaft", c.InputShaftId),
            new MechanicalReference("Shaft", c.OutputShaft.Id), new MechanicalReference("WormWheelBody", c.OutputWheelBodyId), new MechanicalReference("Port", d.OutputTerminal.Id) };
        void Issue(string code, string stage, IEnumerable<MechanicalExactFact>? exact = null, string detail = "") => outputIssues.Add(new MechanicalDiagnostic(code, stage,
            related: refs, facts: exact, affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: blocked, scope: "WormDriveOutput", detail: detail));
        ExactAffineRelation? shaft = null, terminal = null, retained = null; var path = Array.Empty<string>(); var target = MechanicalAxisVerdict.NotAssessed;
        if (status == MechanicalDeterminacy.DeterminedBySelectedInput && endpointOkay)
        {
            var relation = outputComponent.Relations.Single(r => r.DofId == c.OutputShaft.Id); shaft = relation.Relation;
            retained = outputComponent.Relations.Single(r => r.DofId == c.InputShaftId).Relation; path = relation.ConstraintPath.ToArray();
            terminal = shaft.Value.Then(local.TerminalCoordinateSign!.Value, d.OutputTerminal.PhaseOffset);
            MechanicalDerivedNumbers.Check(terminal.Value.Coefficient); MechanicalDerivedNumbers.Check(terminal.Value.Phase);
            target = (!d.Output.RequiredTransfer.HasValue || d.Output.RequiredTransfer.Value == terminal.Value.Coefficient) &&
                (!d.RequiredOutputPhase.HasValue || d.RequiredOutputPhase.Value == terminal.Value.Phase) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
            if (target == MechanicalAxisVerdict.Fail) Issue("TargetMismatch", "OutputRequirement", new[] {
                new MechanicalExactFact("requiredTerminalTransfer", d.Output.RequiredTransfer, terminal.Value.Coefficient),
                new MechanicalExactFact("requiredTerminalPhase.turn", d.RequiredOutputPhase, terminal.Value.Phase) }, "Targets never alter worm/wheel specimens, placement, references or the admitted helical relation.");
        }
        else if (status == MechanicalDeterminacy.UndrivenRelativeMotion) Issue("UndrivenOutput", "WholeMotionDeterminacy", detail:
            "The output is an unprescribed relative coordinate; its fixed center and assembly reference are not a driver, cached phase or zero-speed result.");
        else Issue(status.ToString(), "WholeMotionDeterminacy");
        outputIssues.AddRange(outputComponent.Diagnostics); issues.AddRange(outputIssues);
        var output = new MechanicalOutputAnalysis(d.Output, declaredReachable, admittedReachable, status, shaft, terminal,
            local.OutputPositiveAxis, d.OutputTerminal.Frame, path, declaredPath, blocked, target, outputIssues);
        var checks = new List<OrientedDomainCheck>(local.Checks)
        {
            new("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true,
                "Complete current source, all retained bodies/constraints/outputs and targets are analyzed fresh; original source finalization is still required."),
            new("SourceClearance", "source", Verdict(source.Geometry), true, "Original source geometry policy only; added worm/wheel solids and swept clearance are not included."),
            new("IdealHelicalPhaseConstraint", c.Id, local.IsAdmitted ? OrientedCheckVerdict.Pass : local.TransmissionPresent ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true,
                "Only a present admitted module/trace/geometry-compatible worm supplies the helical phase edge."),
            new("WholeMotionDeterminacy", d.Output.Key, output.HasDeterminedMotion && components.All(k => k.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail,
                true, "All original source nodes and the new non-prescribed shaft are solved together by exact component algebra."),
            new("OutputRequirements", d.Output.Key, Verdict(target), true, "Exact requested terminal ratio and optional unwrapped phase remain independent requirements.")
        };
        foreach (var domain in new[] { "ConjugateFlankGeneration", "ActualToothContact/PressureAngle/ContactRatio", "FullAddedBody/SweptClearance",
            "Backlash/Elasticity", "SelfLocking/BackdriveResistance", "Friction/Torque/Efficiency/Thermal", "Force/Inertia/Dynamics", "Bearing/Strength/Wear/Manufacturing" }.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(k => k.Domain == domain)) continue;
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new OrientedDomainCheck(domain, c.Id, OrientedCheckVerdict.NotPerformed, required, "NotPerformed by this bounded ideal cylindrical worm profile."));
            if (required) issues.Add(new MechanicalDiagnostic("RequiredValidationNotPerformed", "ValidationScope", related: refs,
                affectedOutputs: new[] { d.Output.Key }, scope: domain, detail: "A required unsupported validation domain blocks finalization; no fallback approval is applied."));
        }
        return new WormDriveAnalysis(draft, source, local, nodes, edges, components, output, retained, checks, issues);
    }

    public static WormDriveEvaluation Evaluate(WormDriveAnalysis analysis, ExactQuantity rootTurns)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); MechanicalAuthoringProfile.Number(rootTurns.Value);
        var rotary = new List<OrientedShaftEvaluation>(); var issues = new List<MechanicalDiagnostic>(); var d = analysis.Draft.Definition; var c = d.Device;
        WormDriveEvaluation Result(WormDriveEvaluationStatus status, WormDriveOutputEvaluation? output = null) => new(analysis.AnalysisId, status, rootTurns, rotary, output, issues);
        if (rootTurns.Kind != QuantityKind.AngularPosition)
        {
            issues.Add(new MechanicalDiagnostic("DimensionMismatch", "WormDriveEvaluation", scope: "RequestedInput", detail: "Absolute unwrapped root turns are required."));
            return Result(WormDriveEvaluationStatus.DimensionMismatch);
        }
        // Invalid/removed worm connections preserve independently known source motion.
        var mapping = d.SourceMapping;
        if (mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal)
            rotary.AddRange(MechanicalAnalyzer.Evaluate(analysis.SourceAnalysis, rootTurns.Value).Select(r => new OrientedShaftEvaluation(r.ShaftId, r.Turns,
                mapping.Direction(r.PositiveAxis), r.PositiveAxis.Dot(r.WorldAngularVelocityPerRoot))));
        if (!analysis.Output.HasDeterminedMotion || !analysis.RetainedInputRelation.HasValue || !analysis.LocalCompatibility.IsAdmitted)
        {
            issues.AddRange(analysis.Output.Diagnostics);
            return Result(analysis.Output.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ? WormDriveEvaluationStatus.UndrivenOutput : WormDriveEvaluationStatus.InvalidDefinition);
        }
        var output = EvaluateLocal(c, d.OutputTerminal, d.Output, analysis.LocalCompatibility,
            analysis.RetainedInputRelation.Value, analysis.Output.ShaftRelation!.Value, analysis.Output.PortRelation!.Value, rootTurns.Value, issues);
        if (output is null) return Result(WormDriveEvaluationStatus.InvalidDefinition);
        rotary.Add(new OrientedShaftEvaluation(c.OutputShaft.Id, output.ShaftTurns.Value, c.OutputShaft.Frame.Z, analysis.Output.ShaftRelation.Value.Coefficient));
        return Result(WormDriveEvaluationStatus.Success, output);
    }

    internal static WormDriveOutputEvaluation? EvaluateLocal(WormDriveTransmissionDefinition device,
        ShaftPort terminal, MechanicalOutput outputDefinition, WormDriveCompatibilityResult local,
        ExactAffineRelation inputLaw, ExactAffineRelation shaftLaw, ExactAffineRelation terminalLaw,
        Rational absoluteRootTurns, ICollection<MechanicalDiagnostic> diagnostics)
    {
        var c = device;
        var sourceTurns = inputLaw.Evaluate(absoluteRootTurns);
        var shaftTurns = shaftLaw.Evaluate(absoluteRootTurns); var terminalTurns = terminalLaw.Evaluate(absoluteRootTurns);
        var jw = LocalTransmissionRelations.WormInputPhase(c, local).Evaluate(sourceTurns);
        var jg = LocalTransmissionRelations.WormWheelPhase(c, local).Evaluate(shaftTurns);
        foreach (var v in new[] { sourceTurns, shaftTurns, terminalTurns, jw, jg }) MechanicalDerivedNumbers.Check(v);
        if (jw != jg)
        {
            diagnostics.Add(new MechanicalDiagnostic("IdealPhaseConstraintMismatch", "WormDriveEvaluation", scope: "WormDriveOutput",
                detail: "The independently evaluated exact reference phase indices disagree."));
            return null;
        }
        var output = new WormDriveOutputEvaluation(outputDefinition.Key, c.OutputShaft.Id, c.OutputWheelBodyId, terminal.Id,
            sourceTurns, shaftTurns, terminalTurns, c.OutputShaft.Frame.Z, c.OutputPitchCenterMm,
            shaftLaw.Coefficient, terminalLaw.Coefficient, jw, jg);
        return output;
    }
    private static OrientedCheckVerdict Verdict(MechanicalAxisVerdict value) => value == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        value == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
}
