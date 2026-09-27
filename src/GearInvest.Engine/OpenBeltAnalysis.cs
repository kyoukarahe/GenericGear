using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Current mounted pulley geometry and selected length are admission inputs; targets and historic motion are not.</summary>
public static class OpenBeltConnectionQuery
{
    public static OpenBeltCompatibilityResult Query(OpenBeltDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition;
        return QueryLocal(d.Device, d.OutputTerminal, d.Output, BoundMechanicalInputContext.FromLegacy(d.Source, d.SourceMapping, d.Device.InputShaftId, d.Device.InputPortId));
    }

    internal static OpenBeltCompatibilityResult QueryLocal(OpenBeltTransmissionDefinition device,
        ShaftPort outputTerminal, MechanicalOutput output, BoundMechanicalInputContext context)
    {
        var c = device; var mapping = context.MappingEvidence;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var issues = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("OpenBelt", c.Id), new MechanicalReference("Shaft", c.InputShaftId),
            new MechanicalReference("Shaft", c.OutputShaft.Id), new MechanicalReference("PulleyBody", c.InputPulleyBodyId),
            new MechanicalReference("PulleyBody", c.OutputPulleyBodyId), new MechanicalReference("Output", output.Key), new MechanicalReference("Port", outputTerminal.Id) };
        var unsupported = false;
        void Check(string domain, bool pass, string code, string detail, bool unsupportedDomain = false,
            IEnumerable<MechanicalExactFact>? exact = null, OrientedCheckVerdict? failedVerdict = null)
        {
            checks.Add(new OrientedDomainCheck(domain, c.Id, pass ? OrientedCheckVerdict.Pass : failedVerdict ?? OrientedCheckVerdict.Fail, true, detail));
            if (pass) return;
            unsupported |= unsupportedDomain;
            issues.Add(new MechanicalDiagnostic(code, "OpenBeltCompatibility", related: refs, facts: exact, affectedOutputs: new[] { output.Key },
                scope: "LocalTwoPulleyOpenBeltOnly", detail: detail));
        }
        var dimensions = c.InputPitchRadius.Kind == QuantityKind.LinearPosition && c.OutputPitchRadius.Kind == QuantityKind.LinearPosition &&
            c.OutputPulleyStation.Kind == QuantityKind.LinearPosition && c.InputReferenceTurns.Kind == QuantityKind.AngularPosition && c.OutputReferenceTurns.Kind == QuantityKind.AngularPosition;
        Check("Dimensions", dimensions, "DimensionMismatch", "Radii and mounting station are exact mm; both assembly references are unwrapped turns.");
        var mappingValid = context.MappingValid;
        Check("SourceLengthMapping", context.MappingScaleValid, "MissingSourceLengthMapping", "A positive explicit mm/source-unit mapping is required exactly once for source geometry.");
        Check("SourcePose", context.MappingPoseValid, "InvalidSourcePose", "Source placement must be a proper cardinal rigid pose.");
        Check("OpenRouting", c.RoutingKind == OpenBeltProfile.RoutingKind, "UnsupportedBeltRouting", "Only the declared open route is supported; crossed requests are not relabeled.", true);
        Check("GroundedPulleyCentersAndAxes", c.InputCenterFixed && c.InputAxisFixed && c.OutputCenterFixed && c.OutputAxisFixed,
            "UnsupportedGrounding", "Both pulley centers and axes must be ground fixed; this is not a moving-center model.", true);
        var positiveRadii = c.InputPitchRadius.Kind == QuantityKind.LinearPosition && c.OutputPitchRadius.Kind == QuantityKind.LinearPosition && c.InputPitchRadius.Value > 0 && c.OutputPitchRadius.Value > 0;
        Check("PositivePitchRadii", positiveRadii, "InvalidPulleyRadius", "Pulley radii are positive pitch-line lengths, never artificial tooth counts.",
            exact: new[] { new MechanicalExactFact("inputPitchRadius.mm", null, c.InputPitchRadius.Value), new MechanicalExactFact("outputPitchRadius.mm", null, c.OutputPitchRadius.Value) });
        Check("DistinctPulleyBodies", c.InputPulleyBodyId != c.OutputPulleyBodyId &&
            !context.HasBodyId(c.InputPulleyBodyId) && !context.HasBodyId(c.OutputPulleyBodyId),
            "InvalidPulleyMounting", "Two added pulley bodies remain distinct from every complete retained source gear body.");
        var input = context.Shaft;
        Check("InputShaftBinding", input is not null && input.Frame.IsProperCardinal, "MissingEndpoint", "Input pulley binds an existing proper retained source shaft, not a stored solution or inferred port.");
        var outputFrameValid = c.OutputShaft.Frame.IsProperCardinal;
        Check("OutputShaftFrame", outputFrameValid, "InvalidPulleyMounting", "Output shaft has an explicit proper cardinal frame.");
        Check("SinglePrescribedInput", !c.OutputShaft.IsPrescribed, "HiddenPrescribedOutput", "The new shaft must not declare a second prescribed input; output reference is not a driver.", true);
        var binding = output.IsResolved && output.ShaftId == c.OutputShaft.Id && output.BodyId == c.OutputPulleyBodyId && output.PortId == outputTerminal.Id &&
            outputTerminal.ShaftId == c.OutputShaft.Id && !context.HasOutputId(output.Key) && !context.HasPortId(outputTerminal.Id);
        Check("OutputBinding", binding, "MissingEndpoint", "The new output names its actual shaft, distinct pulley body and separate terminal; source output identities are preserved.");
        var terminalValid = outputFrameValid && outputTerminal.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(outputTerminal, c.OutputShaft);
        Check("TerminalMounting", terminalValid, "InvalidOutputMounting", "The terminal is a proper zero-phase port on the output shaft line with the same zero ray; assembly references belong to the belt.");
        var normalValid = c.RouteNormal.IsCardinal;
        Check("RoutePlaneNormal", normalValid, "InvalidRoutePlaneNormal", "The route normal is an explicit unit cardinal direction, independent of shaft sign and camera.");
        ExactVector3? a1 = null; ExactVector3? a2 = outputFrameValid ? c.OutputShaft.Frame.Z : null;
        Rational? eps1 = null, eps2 = null, portSign = null, terminalSign = terminalValid ? c.OutputShaft.Frame.Z.Dot(outputTerminal.Frame.Z) : null;
        if (mappingValid && input is not null && input.Frame.IsProperCardinal)
        {
            var retained = context.FixedFrameMm!; a1 = retained.Z;
            var mountResidual = (c.InputPulleyCenterMm - retained.Origin).Cross(retained.Z);
            Check("InputPulleyMounting", mountResidual == ExactVector3.Zero, "InvalidPulleyMounting",
                "Input pulley center lies on the complete mapped retained shaft line; original source gears and their axial stations remain unchanged.",
                exact: VectorFacts("inputCenterToRetainedAxisCross.mm", ExactVector3.Zero, mountResidual));
            if (c.InputPortId is not null)
            {
                var p = context.Port;
                var valid = p is not null && p.ShaftId == input.Id && p.Frame.IsProperCardinal && context.PortIsMounted;
                Check("SourcePortBinding", valid, "InvalidSourcePortBinding", "Optional input port is original retained-shaft evidence; its sign is not another motion multiplier.");
                if (valid) portSign = input.Frame.Z.Dot(p!.Frame.Z);
            }
        }
        else if (c.InputPortId is not null) Check("SourcePortBinding", false, "InvalidSourcePortBinding", "A valid retained shaft and mapping are prerequisites for the optional port evidence.");
        var aligned = normalValid && a1.HasValue && a2.HasValue && a1.Value.Cross(c.RouteNormal) == ExactVector3.Zero && a2.Value.Cross(c.RouteNormal) == ExactVector3.Zero;
        Check("Plane/AxisAlignment", aligned, "NonParallelPulleyAxes", "Both actual shaft positive axes must be collinear with the explicit route normal.");
        if (aligned) { eps1 = a1!.Value.Dot(c.RouteNormal); eps2 = a2!.Value.Dot(c.RouteNormal); }
        var center2 = c.OutputPulleyCenterMm; var delta = center2 - c.InputPulleyCenterMm;
        var coplanar = normalValid && delta.Dot(c.RouteNormal) == 0;
        Check("PulleyCoplanarity", coplanar, "NonCoplanarPulleyCenters", "Both pitch circle centers belong to one fixed route plane.",
            exact: new[] { new MechanicalExactFact("centerDeltaDotNormal.mm", 0, delta.Dot(c.RouteNormal)) });
        var distance = OpenBeltGeometry.Abs(delta.X) + OpenBeltGeometry.Abs(delta.Y) + OpenBeltGeometry.Abs(delta.Z);
        var cardinal = distance > 0 && (delta * (Rational.One / distance)).IsCardinal;
        Check("CenterDirection", cardinal, distance == 0 ? "CoincidentPulleyCenters" : "UnsupportedCenterDirection", "Nonzero center-to-center direction is cardinal in the first profile.", distance != 0);
        var separation = cardinal && positiveRadii && distance > c.InputPitchRadius.Value + c.OutputPitchRadius.Value;
        Check("PulleyPitchDiskSeparation", separation, "PulleyPitchDisksOverlap", "Strict D>R1+R2 is required; external tangent existence alone and equality/direct contact are insufficient.",
            exact: new[] { new MechanicalExactFact("strictCenterDistanceLowerBound.mm", c.InputPitchRadius.Value + c.OutputPitchRadius.Value, distance) });
        OpenBeltRouteDescriptor? route = null;
        if (normalValid && cardinal && coplanar && separation)
            route = OpenBeltGeometry.CreateRoute(c.InputPulleyCenterMm, center2, c.InputPitchRadius, c.OutputPitchRadius, c.RouteNormal);
        Check("OpenTangentRoute", route?.HasExactProof == true, "InvalidTangentDescriptor", "Positive-radical tangent and circle polynomial identities are reconstructed from current exact centers and radii.");
        Check("ClosedRoute", route?.HasExactProof == true, "InvalidTangentDescriptor", "Two fixed spans and the selected outer clockwise arcs share exact endpoints in one closed loop.");
        var length = OpenBeltGeometry.CompareLength(route, c.SelectedBelt);
        Check("BeltLengthCompatibility", length.Verdict == OpenBeltLengthVerdict.ExactMatch,
            length.Verdict == OpenBeltLengthVerdict.Inconclusive ? "InconclusiveBeltLengthCompatibility" : length.Verdict == OpenBeltLengthVerdict.InvalidSpecification ? "InvalidBeltLengthSpecification" : "BeltLengthMismatch",
            length.Detail, exact: length.Facts, failedVerdict: length.Verdict == OpenBeltLengthVerdict.Inconclusive ? OrientedCheckVerdict.Inconclusive : OrientedCheckVerdict.Fail);
        Rational? prospective = null; var velocity = Array.Empty<OpenBeltExactProof>();
        if (aligned && positiveRadii && route is not null)
        {
            prospective = eps1!.Value * eps2!.Value * c.InputPitchRadius.Value / c.OutputPitchRadius.Value;
            MechanicalDerivedNumbers.Check(prospective.Value);
            velocity = OpenBeltGeometry.ContactVelocityProofs(route, a1!.Value, a2!.Value, prospective.Value).ToArray();
        }
        Check("IdealContactVelocity", velocity.Length > 0 && velocity.All(p => p.IsVerified), "InvalidNoSlipRelation",
            "At both free spans and their four wrap endpoints, actual axis cross radius velocities agree coefficient-wise after the derived radius/sign transfer; friction/traction is not assessed.");
        var mountingDomains = new[] { "InputShaftBinding", "InputPulleyMounting", "OutputShaftFrame", "DistinctPulleyBodies", "OutputBinding", "TerminalMounting" };
        Check("PulleyMounting", mountingDomains.All(domain => checks.Any(k => k.Domain == domain && k.Verdict == OrientedCheckVerdict.Pass)) &&
            c.OutputPulleyStation.Kind == QuantityKind.LinearPosition, "InvalidPulleyMounting", "Both separate pulley bodies have actual valid fixed shaft mountings and an explicit length-valued output axial station.");
        if (mapping is not null) facts.Add(new MechanicalExactFact("millimetersPerSourceUnit", null, mapping.MillimetersPerSourceUnit));
        facts.Add(new MechanicalExactFact("inputPitchRadius.mm", null, c.InputPitchRadius.Value)); facts.Add(new MechanicalExactFact("outputPitchRadius.mm", null, c.OutputPitchRadius.Value));
        if (eps1.HasValue) facts.Add(new MechanicalExactFact("inputAxisDotRouteNormal", null, eps1));
        if (eps2.HasValue) facts.Add(new MechanicalExactFact("outputAxisDotRouteNormal", null, eps2));
        if (portSign.HasValue) facts.Add(new MechanicalExactFact("sourcePortCoordinateSign.evidenceOnly", null, portSign));
        if (terminalSign.HasValue) facts.Add(new MechanicalExactFact("terminalCoordinateSign", null, terminalSign));
        if (prospective.HasValue) facts.Add(new MechanicalExactFact("prospectiveTransfer.notAdmission", null, prospective));
        var all = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass);
        var verdict = all ? ConnectionCompatibilityVerdict.CompatibleWithinProfile : unsupported ? ConnectionCompatibilityVerdict.Unsupported :
            checks.All(k => k.Verdict == OrientedCheckVerdict.Pass || k.Verdict == OrientedCheckVerdict.Inconclusive) ? ConnectionCompatibilityVerdict.Inconclusive : ConnectionCompatibilityVerdict.Incompatible;
        return new OpenBeltCompatibilityResult(c.Id, verdict, c.TransmissionPresent, a1, a2, eps1, eps2, portSign, terminalSign,
            prospective, all && c.TransmissionPresent ? prospective : null, route, length, velocity, checks, facts, issues);
    }
    private static IEnumerable<MechanicalExactFact> VectorFacts(string key, ExactVector3 expected, ExactVector3 actual) => new[] {
        new MechanicalExactFact(key + ".x", expected.X, actual.X), new MechanicalExactFact(key + ".y", expected.Y, actual.Y), new MechanicalExactFact(key + ".z", expected.Z, actual.Z) };
}

/// <summary>One fresh retained-source graph plus an admitted typed open-belt relation. Geometry never supplies a hidden driver.</summary>
public static class OpenBeltAnalyzer
{
    public static OpenBeltAnalysis Analyze(OpenBeltDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var source = MechanicalAnalyzer.Analyze(d.Source); var local = OpenBeltConnectionQuery.Query(draft);
        var edgeKey = "OpenBelt/" + c.Id;
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
        var refs = new[] { new MechanicalReference("Output", d.Output.Key), new MechanicalReference("OpenBelt", c.Id), new MechanicalReference("Shaft", c.InputShaftId),
            new MechanicalReference("Shaft", c.OutputShaft.Id), new MechanicalReference("PulleyBody", c.OutputPulleyBodyId), new MechanicalReference("Port", d.OutputTerminal.Id) };
        void Issue(string code, string stage, IEnumerable<MechanicalExactFact>? exact = null, string detail = "") => outputIssues.Add(new MechanicalDiagnostic(code, stage,
            related: refs, facts: exact, affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: blocked, scope: "OpenBeltOutput", detail: detail));
        ExactAffineRelation? shaft = null, terminal = null, retained = null; var path = Array.Empty<string>(); var target = MechanicalAxisVerdict.NotAssessed;
        if (status == MechanicalDeterminacy.DeterminedBySelectedInput && endpointOkay)
        {
            var relation = outputComponent.Relations.Single(r => r.DofId == c.OutputShaft.Id); shaft = relation.Relation;
            retained = outputComponent.Relations.Single(r => r.DofId == c.InputShaftId).Relation; path = relation.ConstraintPath.ToArray();
            terminal = shaft.Value.Then(local.TerminalCoordinateSign!.Value, 0);
            MechanicalDerivedNumbers.Check(terminal.Value.Coefficient); MechanicalDerivedNumbers.Check(terminal.Value.Phase);
            target = (!d.Output.RequiredTransfer.HasValue || d.Output.RequiredTransfer.Value == terminal.Value.Coefficient) &&
                (!d.RequiredOutputPhase.HasValue || d.RequiredOutputPhase.Value == terminal.Value.Phase) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
            if (target == MechanicalAxisVerdict.Fail) Issue("TargetMismatch", "OutputRequirement", new[] {
                new MechanicalExactFact("requiredTerminalTransfer", d.Output.RequiredTransfer, terminal.Value.Coefficient),
                new MechanicalExactFact("requiredTerminalPhase.turn", d.RequiredOutputPhase, terminal.Value.Phase) }, "Targets do not alter radii, belt length, assembly references or the admitted motion law.");
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
            new("SourceClearance", "source", Verdict(source.Geometry), true, "Original source geometry policy only; added pulley/belt solids and swept clearance are not included."),
            new("IdealNoSlipRelation", c.Id, local.IsAdmitted ? OrientedCheckVerdict.Pass : local.TransmissionPresent ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true,
                "Only present, geometrically admitted and exact-length-compatible belt supplies the radius/sign/reference edge."),
            new("WholeMotionDeterminacy", d.Output.Key, output.HasDeterminedMotion && components.All(k => k.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail,
                true, "All original source nodes and the new non-prescribed shaft are solved together by exact component algebra."),
            new("OutputRequirements", d.Output.Key, Verdict(target), true, "Exact requested terminal ratio and optional unwrapped phase remain independent requirements.")
        };
        foreach (var domain in new[] { "PhysicalTraction/Tension", "Slip/Creep/Stretch", "BeltWidth/Thickness/SolidSelfContact", "AddedBody/SweptClearance",
            "Bearing/Fatigue/Strength", "Dynamics/Efficiency/Manufacturing" }.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(k => k.Domain == domain)) continue;
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new OrientedDomainCheck(domain, c.Id, OrientedCheckVerdict.NotPerformed, required, "NotPerformed by this bounded ideal open-belt profile."));
            if (required) issues.Add(new MechanicalDiagnostic("RequiredValidationNotPerformed", "ValidationScope", related: refs,
                affectedOutputs: new[] { d.Output.Key }, scope: domain, detail: "A required unsupported validation domain blocks finalization; no fallback approval is applied."));
        }
        return new OpenBeltAnalysis(draft, source, local, nodes, edges, components, output, retained, checks, issues);
    }

    public static OpenBeltEvaluation Evaluate(OpenBeltAnalysis analysis, ExactQuantity rootTurns)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); MechanicalAuthoringProfile.Number(rootTurns.Value);
        var rotary = new List<OrientedShaftEvaluation>(); var issues = new List<MechanicalDiagnostic>(); var d = analysis.Draft.Definition; var c = d.Device;
        OpenBeltEvaluation Result(OpenBeltEvaluationStatus status, OpenBeltOutputEvaluation? output = null) => new(analysis.AnalysisId, status, rootTurns, rotary, output, issues);
        if (rootTurns.Kind != QuantityKind.AngularPosition)
        {
            issues.Add(new MechanicalDiagnostic("DimensionMismatch", "OpenBeltEvaluation", scope: "RequestedInput", detail: "Absolute unwrapped root turns are required."));
            return Result(OpenBeltEvaluationStatus.DimensionMismatch);
        }
        // Even invalid/removed belt connections must not erase independently known source motion.
        var mapping = d.SourceMapping;
        if (mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal)
            rotary.AddRange(MechanicalAnalyzer.Evaluate(analysis.SourceAnalysis, rootTurns.Value).Select(r => new OrientedShaftEvaluation(r.ShaftId, r.Turns,
                mapping.Direction(r.PositiveAxis), r.PositiveAxis.Dot(r.WorldAngularVelocityPerRoot))));
        if (!analysis.Output.HasDeterminedMotion || !analysis.RetainedInputRelation.HasValue || !analysis.LocalCompatibility.IsAdmitted)
        {
            issues.AddRange(analysis.Output.Diagnostics);
            return Result(analysis.Output.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ? OpenBeltEvaluationStatus.UndrivenOutput : OpenBeltEvaluationStatus.InvalidDefinition);
        }
        var output = EvaluateLocal(c, d.OutputTerminal, d.Output, analysis.LocalCompatibility,
            analysis.RetainedInputRelation.Value, analysis.Output.ShaftRelation!.Value, analysis.Output.PortRelation!.Value, rootTurns.Value);
        rotary.Add(new OrientedShaftEvaluation(c.OutputShaft.Id, output.ShaftTurns.Value, c.OutputShaft.Frame.Z, analysis.Output.ShaftRelation.Value.Coefficient));
        return Result(OpenBeltEvaluationStatus.Success, output);
    }

    internal static OpenBeltOutputEvaluation EvaluateLocal(OpenBeltTransmissionDefinition device,
        ShaftPort terminal, MechanicalOutput outputDefinition, OpenBeltCompatibilityResult local,
        ExactAffineRelation inputLaw, ExactAffineRelation shaftLaw, ExactAffineRelation terminalLaw,
        Rational absoluteRootTurns)
    {
        var c = device;
        var sourceTurns = inputLaw.Evaluate(absoluteRootTurns);
        var shaftTurns = shaftLaw.Evaluate(absoluteRootTurns); var terminalTurns = terminalLaw.Evaluate(absoluteRootTurns);
        var travel = LocalTransmissionRelations.BeltTravelPiCoefficient(c, local).Evaluate(sourceTurns);
        foreach (var v in new[] { sourceTurns, shaftTurns, terminalTurns, travel }) MechanicalDerivedNumbers.Check(v);
        var output = new OpenBeltOutputEvaluation(outputDefinition.Key, c.OutputShaft.Id, c.OutputPulleyBodyId, terminal.Id,
            sourceTurns, shaftTurns, terminalTurns, c.OutputShaft.Frame.Z, c.OutputPulleyCenterMm,
            shaftLaw.Coefficient, terminalLaw.Coefficient, travel);
        return output;
    }
    private static OrientedCheckVerdict Verdict(MechanicalAxisVerdict value) => value == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        value == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
}
