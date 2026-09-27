using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Fresh local pin-pitch, mounting, integer assembly and selected-chain admission. No target supplies a relation.</summary>
public static class PitchChainConnectionQuery
{
    public static PitchChainCompatibilityResult Query(PitchChainDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition;
        return QueryLocal(d.Device, d.OutputTerminal, d.Output, BoundMechanicalInputContext.FromLegacy(d.Source, d.SourceMapping, d.Device.InputShaftId, d.Device.InputPortId));
    }

    internal static PitchChainCompatibilityResult QueryLocal(PitchChainTransmissionDefinition device,
        ShaftPort outputTerminal, MechanicalOutput output, BoundMechanicalInputContext context)
    {
        var c = device; var mapping = context.MappingEvidence;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var issues = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("PitchChain", c.Id), new MechanicalReference("Shaft", c.InputShaftId),
            new MechanicalReference("Shaft", c.OutputShaft.Id), new MechanicalReference("SprocketBody", c.InputSprocketBodyId),
            new MechanicalReference("SprocketBody", c.OutputSprocketBodyId), new MechanicalReference("Output", output.Key), new MechanicalReference("Port", outputTerminal.Id) };
        var unsupported = false; var invalid = false;
        void Check(string domain, bool pass, string code, string detail, bool unsupportedDomain = false, bool invalidInput = false,
            IEnumerable<MechanicalExactFact>? exact = null)
        {
            checks.Add(new OrientedDomainCheck(domain, c.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (pass) return;
            unsupported |= unsupportedDomain; invalid |= invalidInput;
            issues.Add(new MechanicalDiagnostic(code, "PitchChainCompatibility", related: refs, facts: exact,
                affectedOutputs: new[] { output.Key }, scope: "LocalSynchronizedEqualSprocketChainOnly", detail: detail));
        }
        var dimensions = new[] { c.InputPitch, c.OutputPitch, c.InputSprocketStation, c.OutputSprocketStation }.All(q => q.Kind == QuantityKind.LinearPosition) &&
            new[] { c.InputMountingPhase, c.OutputMountingPhase, c.InputReferenceTurns, c.OutputReferenceTurns }.All(q => q.Kind == QuantityKind.AngularPosition);
        Check("Dimensions", dimensions, "DimensionMismatch", "Pin pitches and signed axial stations are canonical mm; mounting and reference angles are unwrapped turns.", invalidInput: true);
        var mappingValid = context.MappingValid;
        Check("SourceLengthMapping", context.MappingScaleValid, "MissingSourceLengthMapping", "An explicit positive mm/source-unit mapping is applied once to every retained source point.");
        Check("SourcePose", context.MappingPoseValid, "InvalidSourcePose", "The source is placed by one proper cardinal rigid pose.");
        Check("OpenRouting", c.RoutingKind == PitchChainProfile.RoutingKind, "UnsupportedChainRouting", "Only the declared two-sprocket open route is supported.", true);
        Check("GroundedSprocketCentersAndAxes", c.InputCenterFixed && c.InputAxisFixed && c.OutputCenterFixed && c.OutputAxisFixed,
            "UnsupportedGrounding", "Both mounted centers and axes must remain ground fixed; slack and moving-center paths are not constructed.", true);
        var positivePitch = c.InputPitch.Kind == QuantityKind.LinearPosition && c.OutputPitch.Kind == QuantityKind.LinearPosition && c.InputPitch.Value > 0 && c.OutputPitch.Value > 0;
        Check("PositivePinPitch", positivePitch, "InvalidPitchSpecification", "Both independently declared pin-center pitches must be positive lengths.", invalidInput: true);
        var positiveTeeth = c.InputToothCount > 0 && c.OutputToothCount > 0;
        Check("PositiveToothCounts", positiveTeeth, "InvalidToothSpecification", "Tooth counts must be positive integers.", invalidInput: true);
        var supportedTeeth = PitchChainGeometry.SupportedTeeth(c.InputToothCount) && PitchChainGeometry.SupportedTeeth(c.OutputToothCount);
        Check("SupportedToothCounts", supportedTeeth, "UnsupportedToothCountProfile", "This version supports even tooth counts 6 through128; other counts are not declared physically impossible.", positiveTeeth);
        var equalTeeth = c.InputToothCount == c.OutputToothCount;
        Check("EqualSprockets", equalTeeth, "UnsupportedUnequalSprockets", "Unequal-tooth requests are not repaired or replaced with an average-ratio mechanism.", true);
        var equalPitch = positivePitch && c.InputPitch.Value == c.OutputPitch.Value;
        Check("PitchCompatibility", equalPitch, "PitchMismatch", "Both sprockets use exactly the same pin-center pitch.",
            exact: new[] { new MechanicalExactFact("outputPitch.mm", c.InputPitch.Value, c.OutputPitch.Value) });
        var distinctBodies = c.InputSprocketBodyId != c.OutputSprocketBodyId &&
            !context.HasBodyId(c.InputSprocketBodyId) && !context.HasBodyId(c.OutputSprocketBodyId);
        Check("DistinctSprocketBodies", distinctBodies, "InvalidSprocketMounting", "Two added sprocket bodies remain separate from every retained source gear body.");
        var input = context.Shaft;
        Check("InputShaftBinding", input?.Frame.IsProperCardinal == true, "MissingEndpoint", "The input body mounts on an actual retained source shaft with a proper frame.");
        var outputFrameValid = c.OutputShaft.Frame.IsProperCardinal;
        Check("OutputShaftFrame", outputFrameValid, "InvalidSprocketMounting", "The added shaft declares a proper cardinal frame.");
        Check("SinglePrescribedInput", !c.OutputShaft.IsPrescribed, "HiddenPrescribedOutput", "The new output shaft is not an extra prescribed driver.", true);
        var binding = output.IsResolved && output.ShaftId == c.OutputShaft.Id && output.BodyId == c.OutputSprocketBodyId && output.PortId == outputTerminal.Id &&
            outputTerminal.ShaftId == c.OutputShaft.Id && !context.HasOutputId(output.Key) && !context.HasPortId(outputTerminal.Id);
        Check("OutputBinding", binding, "MissingEndpoint", "The added output names its actual shaft, sprocket body and terminal without replacing retained source output identities.");
        var terminalValid = outputFrameValid && outputTerminal.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(outputTerminal, c.OutputShaft);
        Check("TerminalMounting", terminalValid, "InvalidOutputMounting", "The terminal is a proper zero-phase port on the output shaft line with the same zero ray; references belong to the chain assembly.");
        var normalValid = c.RouteNormal.IsCardinal;
        Check("RoutePlaneNormal", normalValid, "InvalidRoutePlaneNormal", "The explicit route normal is a unit cardinal direction independent of shaft-coordinate signs.");
        OrientedFrame? retained = null; ExactVector3? center1 = null; ExactVector3? a1 = null; ExactVector3? a2 = outputFrameValid ? c.OutputShaft.Frame.Z : null;
        Rational? eps1 = null, eps2 = null, sourcePortSign = null, terminalSign = terminalValid ? c.OutputShaft.Frame.Z.Dot(outputTerminal.Frame.Z) : null;
        if (mappingValid && input?.Frame.IsProperCardinal == true)
        {
            retained = context.FixedFrameMm!; a1 = retained.Z;
            center1 = retained.Origin + retained.Z * c.InputSprocketStation.Value; PitchChainGeometry.Bound(center1.Value);
            if (c.InputPortId is not null)
            {
                var port = context.Port;
                var validPort = port is not null && port.ShaftId == input.Id && port.Frame.IsProperCardinal && context.PortIsMounted;
                Check("SourcePortBinding", validPort, "InvalidSourcePortBinding", "Optional source-port evidence never supplies a second sign multiplier to retained shaft turns.");
                if (validPort) sourcePortSign = input.Frame.Z.Dot(port!.Frame.Z);
            }
        }
        else if (c.InputPortId is not null) Check("SourcePortBinding", false, "InvalidSourcePortBinding", "A valid mapped retained shaft is required for source-port evidence.");
        var aligned = normalValid && a1.HasValue && a2.HasValue && a1.Value.Cross(c.RouteNormal) == ExactVector3.Zero && a2.Value.Cross(c.RouteNormal) == ExactVector3.Zero;
        Check("Plane/AxisAlignment", aligned, "NonParallelSprocketAxes", "Both actual positive shaft axes must be collinear with the explicit route normal.");
        if (aligned) { eps1 = a1!.Value.Dot(c.RouteNormal); eps2 = a2!.Value.Dot(c.RouteNormal); }
        var center2 = c.OutputSprocketCenterMm; var delta = center1.HasValue ? center2 - center1.Value : ExactVector3.Zero;
        var distance = Abs(delta.X) + Abs(delta.Y) + Abs(delta.Z);
        var e = distance > 0 ? delta * (1 / distance) : ExactVector3.Zero;
        var coplanar = center1.HasValue && normalValid && delta.Dot(c.RouteNormal) == 0;
        var cardinal = center1.HasValue && distance > 0 && e.IsCardinal;
        Check("SprocketCoplanarity", coplanar, "NonCoplanarSprocketCenters", "The derived mounted centers must lie in one route plane.");
        Check("CardinalCenterDirection", cardinal, "UnsupportedCenterDirection", "A nonzero cardinal center direction is required; no snapping or placement repair is performed.", true);
        Rational? mExact = cardinal && positivePitch ? distance / c.InputPitch.Value : null;
        var integerM = mExact.HasValue && mExact.Value > 0 && mExact.Value.Denominator.IsOne;
        Check("IntegerCenterPitchRegistration", integerM, "UnsupportedCenterPitchRegistration", "D/p must be an exact positive integer; fractional center-pitch registration is unsupported.", true,
            exact: mExact.HasValue ? new[] { new MechanicalExactFact("centerPitchCount", null, mExact) } : Array.Empty<MechanicalExactFact>());
        var separated = integerM && supportedTeeth && equalTeeth && mExact!.Value >= c.InputToothCount / 2 + 1;
        Check("SeparationDomain", separated, "UnsupportedSeparationDomain", "M>=Z/2+1 is a conservative strict pitch-circle separation domain; failing it is not a collision proof.", true);
        var bounded = integerM && supportedTeeth && 2 * mExact!.Value + c.InputToothCount <= PitchChainProfile.MaxLinks;
        Check("ChainResourceBounds", bounded, "ChainResourceLimit", "The complete chain must fit at most4096pins and4096links; no links are omitted to fit a bound.", true);
        PitchChainGeometryDescriptor? geometry = null;
        if (dimensions && mappingValid && aligned && coplanar && cardinal && equalPitch && supportedTeeth && equalTeeth && separated && bounded)
            geometry = new PitchChainGeometryDescriptor(center1!.Value, center2, c.RouteNormal, e, c.InputToothCount, c.InputPitch.Value, (int)mExact!.Value.Numerator);
        Check("RegularPitchGeometry", geometry?.HasExactProof == true, "UnsupportedPolygonConstruction", "The typed positive pin-radius recipe and all exact polygon/translation/coverage premises are reconstructed from current inputs.");
        PitchChainPhaseRegistration? phase = null;
        if (dimensions && retained is not null && aligned && cardinal && coplanar && supportedTeeth && equalTeeth)
        {
            var f = c.RouteNormal.Cross(e);
            var gamma1 = PitchChainGeometry.QuarterTurn(retained.X, e, f) + eps1!.Value * c.InputMountingPhase.Value;
            var gamma2 = PitchChainGeometry.QuarterTurn(c.OutputShaft.Frame.X, e, f) + eps2!.Value * c.OutputMountingPhase.Value;
            phase = new PitchChainPhaseRegistration(c.InputToothCount, gamma1, gamma2, eps1.Value, eps2.Value,
                c.InputReferenceTurns.Value, c.OutputReferenceTurns.Value, c.ToothRegistration);
        }
        Check("SupportedPhaseRegistration", phase?.IsCompatible == true, "PhaseRegistrationMismatch", "Exact gamma/reference phases must satisfy phi2Reference-phi1Reference=H/Z; no reference or tooth-zero repair is performed.",
            exact: phase is null ? Array.Empty<MechanicalExactFact>() : phase.Proof.Facts);
        var links = CompareSelected(geometry, c.SelectedChain);
        Check("SelectedLinkCount", links.Verdict == PitchChainLinkVerdict.ExactLinkCountMatch, links.Verdict.ToString(), links.Detail,
            links.Verdict == PitchChainLinkVerdict.UnsupportedLinkTopology, links.Verdict == PitchChainLinkVerdict.InvalidSpecification, links.Facts);
        var construction = geometry?.HasExactProof == true && phase?.IsCompatible == true && links.Verdict == PitchChainLinkVerdict.ExactLinkCountMatch;
        Check("IdealClosedPinLinkConstruction", construction, "ChainConstructionNotAdmitted", "Only current compatible pitch, phase, integer route and independently selected closed chain prove all fixed-pitch links and the last-to-first closure.");
        Check("MaterialIdentityContinuity", construction, "MaterialConstructionNotAdmitted", "The exact tie shift Pnew[k]=Pold[k+1] is canceled by jnew=jold-1 in every persistent material pin index; no rendering array is material identity.");
        var mounting = mappingValid && input?.Frame.IsProperCardinal == true && outputFrameValid && distinctBodies && binding && terminalValid && dimensions;
        Check("SprocketMounting", mounting, "InvalidSprocketMounting", "Actual mapped shaft origins plus signed mm stations determine both separate sprocket centers; mounting angles derive from their actual zero rays.");
        Rational? prospective = aligned && supportedTeeth && equalTeeth ? eps1!.Value * eps2!.Value : null;
        if (mapping is not null) facts.Add(new MechanicalExactFact("millimetersPerSourceUnit", null, mapping.MillimetersPerSourceUnit));
        facts.Add(new MechanicalExactFact("inputPinPitch.mm", null, c.InputPitch.Value)); facts.Add(new MechanicalExactFact("outputPinPitch.mm", null, c.OutputPitch.Value));
        facts.Add(new MechanicalExactFact("inputToothCount", null, c.InputToothCount)); facts.Add(new MechanicalExactFact("outputToothCount", null, c.OutputToothCount));
        if (mExact.HasValue) facts.Add(new MechanicalExactFact("centerPitchCount", null, mExact));
        if (eps1.HasValue) facts.Add(new MechanicalExactFact("inputAxisDotRouteNormal", null, eps1));
        if (eps2.HasValue) facts.Add(new MechanicalExactFact("outputAxisDotRouteNormal", null, eps2));
        if (sourcePortSign.HasValue) facts.Add(new MechanicalExactFact("sourcePortCoordinateSign.evidenceOnly", null, sourcePortSign));
        if (terminalSign.HasValue) facts.Add(new MechanicalExactFact("terminalCoordinateSign", null, terminalSign));
        if (prospective.HasValue) facts.Add(new MechanicalExactFact("prospectiveTransfer.notAdmission", null, prospective));
        var all = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass);
        var verdict = all ? ConnectionCompatibilityVerdict.CompatibleWithinProfile : invalid ? ConnectionCompatibilityVerdict.InvalidInput :
            unsupported ? ConnectionCompatibilityVerdict.Unsupported : ConnectionCompatibilityVerdict.Incompatible;
        return new PitchChainCompatibilityResult(c.Id, verdict, c.TransmissionPresent, center1, a1, a2, eps1, eps2,
            sourcePortSign, terminalSign, prospective, all && c.TransmissionPresent ? prospective : null, geometry, phase, links, checks, facts, issues);
    }
    private static Rational Abs(Rational value) => value < 0 ? -value : value;
    private static PitchChainLinkCompatibility CompareSelected(PitchChainGeometryDescriptor? geometry, PitchChainSpecification? selected)
    {
        var facts = new List<MechanicalExactFact>();
        if (geometry is not null) { facts.Add(new MechanicalExactFact("requiredLinkCount", null, geometry.RequiredLinkCount)); facts.Add(new MechanicalExactFact("requiredPitch.mm", null, geometry.Pitch.PitchMm)); }
        if (selected is not null) { facts.Add(new MechanicalExactFact("selectedLinkCount", null, selected.LinkCount)); facts.Add(new MechanicalExactFact("selectedPitch.mm", null, selected.Pitch.Value)); }
        PitchChainLinkCompatibility Result(PitchChainLinkVerdict verdict, string detail) => new(verdict, selected, geometry, facts, detail);
        if (selected is null || selected.Pitch.Kind != QuantityKind.LinearPosition || selected.Pitch.Value <= 0 || selected.LinkCount <= 0 || selected.LinkCount > PitchChainProfile.MaxLinks)
            return Result(PitchChainLinkVerdict.InvalidSpecification, "Select an explicit positive-mm-pitch chain with a bounded positive integer link count; selection is never synthesized implicitly.");
        if (selected.Topology != PitchChainProfile.LinkTopology || selected.LinkCount % 2 != 0)
            return Result(PitchChainLinkVerdict.UnsupportedLinkTopology, "This version requires an even closed alternating inner/outer link loop; no offset link or slack is inserted.");
        if (geometry is null) return Result(PitchChainLinkVerdict.GeometryUnavailable, "A supported current integer-pitch polygon route is required for a count comparison.");
        if (selected.Pitch.Value != geometry.Pitch.PitchMm) return Result(PitchChainLinkVerdict.SelectedChainPitchMismatch, "Selected chain pitch differs from the current sprocket pin pitch.");
        if (selected.LinkCount < geometry.RequiredLinkCount) return Result(PitchChainLinkVerdict.TooFewLinksForDeclaredRoute, "Selected chain has too few links for N=2M+Z; pitch and centers were not stretched or moved.");
        if (selected.LinkCount > geometry.RequiredLinkCount) return Result(PitchChainLinkVerdict.TooManyLinksForDeclaredRoute, "Selected chain has too many links for the declared taut route; surplus links were not hidden as slack.");
        return Result(PitchChainLinkVerdict.ExactLinkCountMatch, "Selected pitch and even closed-link count exactly equal the current regular-polygon route requirements.");
    }
}

/// <summary>Complete retained-source quantity graph plus one admitted synchronized angular edge.</summary>
public static class PitchChainAnalyzer
{
    public static PitchChainAnalysis Analyze(PitchChainDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var source = MechanicalAnalyzer.Analyze(d.Source); var local = PitchChainConnectionQuery.Query(draft);
        var edgeKey = "PitchChain/" + c.Id;
        var nodes = d.Source.Definition.Shafts.Select(s => new QuantityDof(s.Id, QuantityKind.AngularPosition, s.IsPrescribed))
            .Concat(new[] { new QuantityDof(c.OutputShaft.Id, QuantityKind.AngularPosition, c.OutputShaft.IsPrescribed) }).ToArray();
        var edges = MechanicalAnalyzer.LowerAdmitted(d.Source.Definition, source.Edges).Select(e => new QuantityAffineCoupling(e.Id, e.DriverDofId, e.DrivenDofId,
            ExactQuantity.FromCanonical(QuantityKind.AngularPerAngular, e.Transfer), ExactQuantity.FromCanonical(QuantityKind.AngularPosition, e.PhaseOffset))).ToList();
        if (local.IsAdmitted)
        {
            var q = local.AdmittedTransfer!.Value; var p = c.OutputReferenceTurns.Value - q * c.InputReferenceTurns.Value;
            MechanicalDerivedNumbers.Check(p);
            edges.Add(new QuantityAffineCoupling(edgeKey, c.InputShaftId, c.OutputShaft.Id,
                ExactQuantity.FromCanonical(QuantityKind.AngularPerAngular, q), ExactQuantity.FromCanonical(QuantityKind.AngularPosition, p)));
        }
        var selected = source.SelectedInputId is not null && nodes.Any(n => n.Id == source.SelectedInputId) ? source.SelectedInputId : null;
        var components = QuantityAffineComponentAnalyzer.Analyze(selected, nodes, edges);
        var outputComponent = components.Single(k => k.MemberIds.Contains(c.OutputShaft.Id));
        var sourceDeclared = source.DeclaredComponents.FirstOrDefault(k => k.ShaftIds.Contains(c.InputShaftId));
        var declaredReachable = c.TransmissionPresent && sourceDeclared?.IsSelectedInputReachable == true;
        var admittedReachable = selected is not null && outputComponent.MemberIds.Contains(selected);
        var declaredPath = declaredReachable ? MechanicalAnalyzer.Path(selected!, c.InputShaftId, source.Edges.Where(e => e.IsDeclaredResolvable)).Concat(new[] { edgeKey }).ToArray() : Array.Empty<string>();
        var blocked = declaredPath.Where(key => key == edgeKey ? !local.IsAdmitted : !source.Edges.Single(e => e.ConstraintKey == key).IsAdmitted).ToArray();
        var status = outputComponent.Determinacy;
        if (c.TransmissionPresent && local.Verdict != ConnectionCompatibilityVerdict.CompatibleWithinProfile)
            status = local.Verdict == ConnectionCompatibilityVerdict.Unsupported ? MechanicalDeterminacy.UnsupportedConstraintDomain : MechanicalDeterminacy.BlockedByInvalidConstraint;
        else if (declaredReachable && !admittedReachable) status = MechanicalDeterminacy.BlockedByInvalidConstraint;
        var endpointDomains = new[] { "OutputBinding", "OutputShaftFrame", "TerminalMounting", "SinglePrescribedInput" };
        var endpointOkay = local.Checks.Where(k => endpointDomains.Contains(k.Domain)).All(k => k.Verdict == OrientedCheckVerdict.Pass);
        if (!endpointOkay) status = MechanicalDeterminacy.BlockedByInvalidConstraint;
        var issues = new List<MechanicalDiagnostic>(local.Diagnostics); var outputIssues = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("Output", d.Output.Key), new MechanicalReference("PitchChain", c.Id), new MechanicalReference("Shaft", c.InputShaftId),
            new MechanicalReference("Shaft", c.OutputShaft.Id), new MechanicalReference("SprocketBody", c.OutputSprocketBodyId), new MechanicalReference("Port", d.OutputTerminal.Id) };
        void Issue(string code, string stage, IEnumerable<MechanicalExactFact>? exact = null, string detail = "") => outputIssues.Add(new MechanicalDiagnostic(code, stage,
            related: refs, facts: exact, affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: blocked, scope: "PitchChainOutput", detail: detail));
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
                new MechanicalExactFact("requiredTerminalPhase.turn", d.RequiredOutputPhase, terminal.Value.Phase) },
                "Required motion never changes sprocket teeth, selected chain, tooth registration or actual reference-derived coefficients.");
        }
        else if (status == MechanicalDeterminacy.UndrivenRelativeMotion) Issue("UndrivenOutput", "WholeMotionDeterminacy", detail:
            "The added shaft is an unprescribed relative coordinate; fixed centers and assembly reference angles are not a driver or cached pose.");
        else Issue(status.ToString(), "WholeMotionDeterminacy");
        outputIssues.AddRange(outputComponent.Diagnostics); issues.AddRange(outputIssues);
        var output = new MechanicalOutputAnalysis(d.Output, declaredReachable, admittedReachable, status, shaft, terminal,
            local.OutputPositiveAxis, d.OutputTerminal.Frame, path, declaredPath, blocked, target, outputIssues);
        var checks = new List<OrientedDomainCheck>(local.Checks)
        {
            new("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true,
                "The complete current source and all its existing bodies, constraints and outputs are freshly analyzed; original source finalization remains required."),
            new("SourceClearance", "source", Verdict(source.Geometry), true, "Original source geometry policy only; no added sprocket/link solids or swept clearance are implied."),
            new("SynchronizedShaftRelation", c.Id, local.IsAdmitted ? OrientedCheckVerdict.Pass : local.TransmissionPresent ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true,
                "Only the present equal-pitch, equal-tooth, phase-registered and selected-count-compatible construction supplies a signed1:1 edge."),
            new("WholeMotionDeterminacy", d.Output.Key, output.HasDeterminedMotion && components.All(k => k.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail,
                true, "All retained source nodes plus the one non-prescribed output shaft are solved together by exact quantity-component algebra."),
            new("OutputRequirements", d.Output.Key, Verdict(target), true, "Exact requested terminal ratio and optional unwrapped phase are independent requirements.")
        };
        foreach (var domain in new[] { "ActualToothPocket/RollerContact", "LinkPlate/SolidInterference", "FullAddedBody/SweptClearance", "Slack/Tension/Traction",
            "Backlash/Elasticity/Friction", "Force/Inertia/Impact/Dynamics", "Wear/Lubrication/Strength/Manufacturing" }.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(k => k.Domain == domain)) continue;
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new OrientedDomainCheck(domain, c.Id, OrientedCheckVerdict.NotPerformed, required, "NotPerformed by the bounded ideal pin-joint skeleton profile."));
            if (required) issues.Add(new MechanicalDiagnostic("RequiredValidationNotPerformed", "ValidationScope", related: refs,
                affectedOutputs: new[] { d.Output.Key }, scope: domain, detail: "A requested unsupported validation domain blocks finalization; it is not silently removed."));
        }
        return new PitchChainAnalysis(draft, source, local, nodes, edges, components, output, retained, checks, issues);
    }

    public static PitchChainEvaluation Evaluate(PitchChainAnalysis analysis, ExactQuantity rootTurns)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); MechanicalAuthoringProfile.Number(rootTurns.Value);
        var d = analysis.Draft.Definition; var c = d.Device; var rotary = new List<OrientedShaftEvaluation>(); var issues = new List<MechanicalDiagnostic>();
        PitchChainEvaluation Result(PitchChainEvaluationStatus status, PitchChainOutputEvaluation? output = null, IndexedChainPoseDescriptor? pose = null) =>
            new(analysis.AnalysisId, status, rootTurns, rotary, output, pose, issues);
        if (rootTurns.Kind != QuantityKind.AngularPosition)
        {
            issues.Add(new MechanicalDiagnostic("DimensionMismatch", "PitchChainEvaluation", scope: "RequestedInput", detail: "Absolute unwrapped root turns are required."));
            return Result(PitchChainEvaluationStatus.DimensionMismatch);
        }
        var mapping = d.SourceMapping;
        if (mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal)
            rotary.AddRange(MechanicalAnalyzer.Evaluate(analysis.SourceAnalysis, rootTurns.Value).Select(r => new OrientedShaftEvaluation(r.ShaftId, r.Turns,
                mapping.Direction(r.PositiveAxis), r.PositiveAxis.Dot(r.WorldAngularVelocityPerRoot))));
        if (!analysis.Output.HasDeterminedMotion || !analysis.RetainedInputRelation.HasValue || !analysis.LocalCompatibility.IsAdmitted)
        {
            issues.AddRange(analysis.Output.Diagnostics);
            return Result(analysis.Output.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ? PitchChainEvaluationStatus.UndrivenOutput : PitchChainEvaluationStatus.InvalidDefinition);
        }
        var output = EvaluateLocal(c, d.OutputTerminal, d.Output, analysis.LocalCompatibility,
            analysis.RetainedInputRelation.Value, analysis.Output.ShaftRelation!.Value, analysis.Output.PortRelation!.Value, rootTurns.Value, out var pose);
        rotary.Add(new OrientedShaftEvaluation(c.OutputShaft.Id, output.ShaftTurns.Value, c.OutputShaft.Frame.Z, analysis.Output.ShaftRelation.Value.Coefficient));
        return Result(PitchChainEvaluationStatus.Success, output, pose);
    }

    internal static PitchChainOutputEvaluation EvaluateLocal(PitchChainTransmissionDefinition device,
        ShaftPort terminal, MechanicalOutput outputDefinition, PitchChainCompatibilityResult local,
        ExactAffineRelation inputLaw, ExactAffineRelation shaftLaw, ExactAffineRelation terminalLaw,
        Rational absoluteRootTurns, out IndexedChainPoseDescriptor materialPose)
    {
        var c = device;
        var inputTurns = inputLaw.Evaluate(absoluteRootTurns);
        var shaftTurns = shaftLaw.Evaluate(absoluteRootTurns); var terminalTurns = terminalLaw.Evaluate(absoluteRootTurns);
        foreach (var value in new[] { inputTurns, shaftTurns, terminalTurns }) MechanicalDerivedNumbers.Check(value);
        materialPose = PitchChainGeometry.CreatePose(c, local, inputTurns, shaftTurns);
        if (!materialPose.HasExactProof) throw new InvalidOperationException("Current exact chain construction failed its structural proof; no material pose may be published.");
        var output = new PitchChainOutputEvaluation(outputDefinition.Key, c.OutputShaft.Id, c.OutputSprocketBodyId, terminal.Id,
            inputTurns, shaftTurns, terminalTurns, c.OutputShaft.Frame.Z, c.OutputSprocketCenterMm,
            shaftLaw.Coefficient, terminalLaw.Coefficient);
        return output;
    }
    private static OrientedCheckVerdict Verdict(MechanicalAxisVerdict value) => value == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        value == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
}
