using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class RotaryLinearConnectionQuery
{
    public static LeadScrewCompatibilityResult Query(RotaryLinearDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var o = d.Output; var mapping = d.SourceMapping;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var diagnostics = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("LeadScrew", c.Id), new MechanicalReference("Shaft", c.ScrewShaftId),
            new MechanicalReference("Guide", c.GuideId), new MechanicalReference("LinearDof", c.LinearDofId), new MechanicalReference("LinearOutput", o.Key) };
        var unsupported = false;
        void Check(string domain, bool pass, string code, string detail, bool isUnsupported = false)
        {
            checks.Add(new OrientedDomainCheck(domain, c.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!pass) { unsupported |= isUnsupported; diagnostics.Add(new MechanicalDiagnostic(code, "LeadScrewCompatibility", related: refs, affectedOutputs: new[] { o.Key }, scope: "LocalLeadScrewConnectionOnly", detail: detail)); }
        }
        var dimensions = c.Lead.Kind == QuantityKind.LinearPerAngular && c.ScrewReferenceTurns.Kind == QuantityKind.AngularPosition &&
            c.NutReferencePosition.Kind == QuantityKind.LinearPosition && c.GuideInterval.Kind == QuantityKind.LinearPosition &&
            c.EngagementInterval.Kind == QuantityKind.LinearPosition && o.TerminalDatum.Kind == QuantityKind.LinearPosition;
        Check("Dimensions", dimensions, "DimensionMismatch", "Lead mm/turn, reference turns, guide/reference/terminal mm; original ticks are not mm.");
        Check("SourceLengthMapping", mapping is not null && mapping.MillimetersPerSourceUnit > 0, "MissingLengthScale", "A positive explicit mm/source-unit mapping is required for every source coordinate.");
        Check("SourcePose", mapping is not null && mapping.PoseMm.IsProperCardinal, "InvalidSourcePose", "Source pose is one proper cardinal basis and a mm translation.");
        Check("Lead", c.Lead.Kind == QuantityKind.LinearPerAngular && c.Lead.Value > 0, "InvalidLead", "Positive lead per relative revolution, not pitch.");
        Check("Handedness", c.Handedness == 1 || c.Handedness == -1, "InvalidHandedness", "H is +1 or -1 in z=z0+H*L*t.");
        Check("GuideConstraint", c.GuidePresent, "MissingGuideConstraint", "No guide does not reduce to one prismatic freedom.", true);
        Check("Grounding", c.ScrewAxiallyFixed && c.NutRotationFixed && c.TransverseMotionFixed, "UnsupportedGrounding", "Screw axial support and nut anti-rotation/transverse grounding must be explicit.", true);
        Check("GuideFrame", c.GuideFrameMm.IsProperCardinal && c.PhysicalAxis.IsCardinal, "NonCoaxialScrewAndGuide", "Guide orientation and physical helix axis are proper cardinal declarations.");
        Check("SeparateBodies", c.ScrewBodyId != c.NutBodyId && !d.Source.Definition.Bodies.Any(b => b.Id == c.ScrewBodyId || b.Id == c.NutBodyId),
            "InvalidBodyBinding", "Screw and nut are distinct added bodies, not gear bodies or fake tooth counts.");
        Check("LinearOutputBinding", o.LinearDofId == c.LinearDofId && o.NutBodyId == c.NutBodyId && (o.TerminalSign == 1 || o.TerminalSign == -1) &&
            !d.Source.Definition.Outputs.Any(s => s.Key == o.Key), "InvalidLinearOutputBinding", "Linear terminal binds the declared nut/reference point and has a sign of +/-1.");
        var shaft = d.Source.Definition.Shafts.FirstOrDefault(s => s.Id == c.ScrewShaftId);
        Check("SourceShaftBinding", shaft is not null && shaft.Frame.IsProperCardinal, "MissingEndpoint", "The screw mounts to an actual retained source shaft.");
        ExactVector3? axis = null; Rational? epsilon = null, sigma = null, portSign = null; ExactQuantity? gain = null;
        if (mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal && shaft is not null && shaft.Frame.IsProperCardinal)
        {
            var frame = mapping.FrameMm(shaft.Frame); axis = frame.Z;
            var axisValid = c.PhysicalAxis.IsCardinal && frame.Z.Cross(c.PhysicalAxis) == ExactVector3.Zero &&
                c.GuideFrameMm.IsProperCardinal && c.GuideFrameMm.Z.Cross(c.PhysicalAxis) == ExactVector3.Zero;
            var linesValid = (c.ScrewAxialDatumMm - frame.Origin).Cross(frame.Z) == ExactVector3.Zero &&
                (c.GuideFrameMm.Origin - c.ScrewAxialDatumMm).Cross(frame.Z) == ExactVector3.Zero;
            Check("CoaxialAlignment", axisValid && linesValid, "NonCoaxialScrewAndGuide", "Physical helix, retained rotation, screw datum and guide share one line; skew is never transfer zero.");
            if (axisValid)
            {
                epsilon = frame.Z.Dot(c.PhysicalAxis); sigma = c.GuideFrameMm.Z.Dot(c.PhysicalAxis);
                facts.Add(new MechanicalExactFact("epsilon.retainedAxisDotPhysicalAxis", null, epsilon));
                facts.Add(new MechanicalExactFact("sigma.guideAxisDotPhysicalAxis", null, sigma));
                if (dimensions && c.Lead.Value > 0 && (c.Handedness == 1 || c.Handedness == -1))
                { gain = ExactQuantity.FromCanonical(QuantityKind.LinearPerAngular, -c.Handedness * c.Lead.Value * epsilon.Value * sigma.Value); facts.Add(new MechanicalExactFact("localGain.mmPerRetainedTurn", null, gain.Value.Value)); }
            }
            if (c.ScrewPortId is not null)
            {
                var port = d.Source.Definition.Ports.FirstOrDefault(p => p.Id == c.ScrewPortId);
                var valid = port is not null && port.ShaftId == shaft.Id && port.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(port, shaft);
                Check("SourcePortBinding", valid, "InvalidSourcePortBinding", "Optional mating port belongs to the retained shaft; its coordinate sign is not applied a second time.");
                if (valid) { portSign = shaft.Frame.Z.Dot(port!.Frame.Z); facts.Add(new MechanicalExactFact("sourcePortCoordinateSign.evidenceOnly", null, portSign)); }
            }
        }
        else if (c.ScrewPortId is not null) Check("SourcePortBinding", false, "InvalidSourcePortBinding", "Mating port cannot bind without a valid source shaft and mapping.");
        if (mapping is not null) facts.Add(new MechanicalExactFact("millimetersPerSourceUnit", null, mapping.MillimetersPerSourceUnit));
        facts.Add(new MechanicalExactFact("physicalHandedness", null, c.Handedness));
        facts.Add(new MechanicalExactFact("lead.mmPerRelativeTurn", null, c.Lead.Value));
        var okay = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass);
        return new LeadScrewCompatibilityResult(c.Id, okay ? ConnectionCompatibilityVerdict.CompatibleWithinProfile : unsupported ? ConnectionCompatibilityVerdict.Unsupported : ConnectionCompatibilityVerdict.Incompatible,
            c.TransmissionPresent, axis, epsilon, sigma, portSign, okay ? gain : null, checks, facts, diagnostics);
    }
}

/// <summary>Fresh declared/admitted mixed analysis. The old source remains rotary and its original checks/outputs are retained.</summary>
public static class RotaryLinearAnalyzer
{
    public static RotaryLinearAnalysis Analyze(RotaryLinearDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var source = MechanicalAnalyzer.Analyze(d.Source); var local = RotaryLinearConnectionQuery.Query(draft);
        var edgeKey = "LeadScrew/" + c.Id;
        var graph = PrismaticGraphAnalysis.Build(d.Source, source, c.ScrewShaftId, c.LinearDofId, edgeKey,
            c.TransmissionPresent, local.Verdict, local.LocalGain, c.ScrewReferenceTurns, c.NutReferencePosition);
        var nodes = graph.Nodes; var edges = graph.Edges; var components = graph.Components;
        var declaredReachable = graph.DeclaredReachable; var admittedReachable = graph.AdmittedReachable;
        var declaredPath = graph.DeclaredPath; var blocked = graph.Blocked; var status = graph.Determinacy;
        var issues = new List<MechanicalDiagnostic>(local.Diagnostics);
        var related = new[] { new MechanicalReference("LinearOutput", d.Output.Key), new MechanicalReference("LinearDof", c.LinearDofId), new MechanicalReference("LeadScrew", c.Id), new MechanicalReference("Shaft", c.ScrewShaftId) };
        void Issue(string code, string stage, IEnumerable<MechanicalExactFact>? facts = null, string detail = "") => issues.Add(new MechanicalDiagnostic(code, stage,
            related: related, facts: facts, affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: blocked, scope: "LinearOutput", detail: detail));
        if (status == MechanicalDeterminacy.UndrivenRelativeMotion) Issue("UndrivenLinearCoordinate", "MixedDeterminacy", detail: "Reference position is calibration, not a prescribed slider drive.");
        else if (status != MechanicalDeterminacy.DeterminedBySelectedInput) Issue(status.ToString(), "MixedDeterminacy");
        ExactAffineRelation? screw = null; DimensionedAffineRelation? guide = null, terminal = null;
        ExactVector3? worldGain = null, worldOffset = null; ExactQuantityInterval? validInterval = null;
        var target = MechanicalAxisVerdict.NotAssessed; var required = MechanicalAxisVerdict.NotAssessed; var path = Array.Empty<string>();
        if (status == MechanicalDeterminacy.DeterminedBySelectedInput)
        {
            screw = graph.SourceRelation; guide = graph.GuideRelation; path = graph.Path;
            terminal = PrismaticAlgebra.Terminal(guide!.Value, d.Output.TerminalSign, d.Output.TerminalDatum);
            worldGain = c.GuideFrameMm.Z * guide.Value.Gain.Value;
            worldOffset = c.GuideFrameMm.Origin + c.GuideFrameMm.Z * guide.Value.Offset.Value;
            Bound(worldGain.Value); Bound(worldOffset.Value);
            validInterval = PrismaticAlgebra.OperatingDomain(c.GuideInterval, c.EngagementInterval, guide.Value);
            if (validInterval is null) Issue("EmptyAxialDomain", "OperatingDomain");
            var requirements = PrismaticRequirementAssessment.Assess(d.Requirement, terminal.Value, validInterval, Issue);
            target = requirements.Target; required = requirements.RequiredRange;
        }
        var output = new LinearOutputAnalysis(d.Output, status, screw, guide, terminal, local.ScrewPositiveAxis, c.GuideFrameMm.Z,
            worldGain, worldOffset, validInterval, required, target, declaredReachable, admittedReachable, path, declaredPath, blocked, issues);
        var checks = new List<OrientedDomainCheck>(local.Checks);
        checks.Add(new OrientedDomainCheck("SourceClearance", "source", source.Geometry == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
            source.Geometry == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true, "Retained source geometry/clearance policy only; no added body clearance."));
        checks.Add(new OrientedDomainCheck("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Fresh complete 21A source analysis; source-aware finalization is still required."));
        checks.Add(new OrientedDomainCheck("AxialDomain", c.Id, validInterval is not null && required == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
            output.HasDeterminedMotion ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true, "Exact closed guide/engagement reference-point intervals and whole required root interval; no full nut solid margin."));
        checks.Add(new OrientedDomainCheck("LinearTarget", d.Output.Key, target == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
            target == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true, "Exact terminal gain and position at global root zero, independently requested."));
        var notPerformed = new[] { "AddedBodyClearance", "SweptClearance", "ThreadContact", "Force/Friction/Dynamics" };
        foreach (var domain in notPerformed.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(k => k.Domain == domain)) continue;
            checks.Add(new OrientedDomainCheck(domain, c.Id, OrientedCheckVerdict.NotPerformed, d.RequiredValidationDomains.Contains(domain), "Not implemented or performed by this bounded ideal profile."));
            if (d.RequiredValidationDomains.Contains(domain)) Issue("RequiredValidationNotPerformed", "ValidationDomain", detail: domain);
        }
        var diagnostics = source.Diagnostics.Concat(issues).Concat(components.SelectMany(k => k.Diagnostics));
        return new RotaryLinearAnalysis(draft, source, local, nodes, edges, components, output, checks, diagnostics);
    }

    /// <summary>Stateless absolute-input evaluation. Stale targets do not erase scoped motion; travel violations never return a successful linear frame.</summary>
    public static RotaryLinearEvaluation Evaluate(RotaryLinearAnalysis analysis, ExactQuantity rootTurns)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); RotaryLinearProfile.Quantity(rootTurns);
        var diagnostics = new List<MechanicalDiagnostic>(); var rotary = new List<OrientedShaftEvaluation>();
        RotaryLinearEvaluation Result(RotaryLinearEvaluationStatus status, LinearPositionEvaluation? linear = null, LinearPositionEvaluation? diagnostic = null) =>
            new(analysis.AnalysisId, status, rootTurns, rotary, linear, diagnostic, diagnostics);
        void Issue(string code, IEnumerable<MechanicalExactFact>? facts = null) => diagnostics.Add(new MechanicalDiagnostic(code, "RotaryLinearEvaluation",
            related: new[] { new MechanicalReference("LinearOutput", analysis.LinearOutput.OutputKey) }, facts: facts, scope: "RequestedInput"));
        if (rootTurns.Kind != QuantityKind.AngularPosition) { Issue("DimensionMismatch"); return Result(RotaryLinearEvaluationStatus.DimensionMismatch); }
        var d = analysis.Draft.Definition; var mapping = d.SourceMapping;
        rotary.AddRange(PrismaticAlgebra.EvaluateSource(analysis.SourceAnalysis, mapping, rootTurns.Value));
        var output = analysis.LinearOutput;
        if (!output.HasDeterminedMotion)
        {
            diagnostics.AddRange(output.Diagnostics);
            return Result(output.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ? RotaryLinearEvaluationStatus.UndrivenLinearCoordinate : RotaryLinearEvaluationStatus.InvalidDefinition);
        }
        var guide = output.GuideRelation!.Value.Evaluate(rootTurns); var terminal = output.TerminalRelation!.Value.Evaluate(rootTurns);
        var screw = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, output.ScrewRelation!.Value.Evaluate(rootTurns.Value));
        var world = d.Device.GuideFrameMm.Origin + d.Device.GuideFrameMm.Z * guide.Value; Bound(world);
        var frame = new LinearPositionEvaluation(d.Output, screw, guide, terminal, world, d.Device.GuideFrameMm,
            output.GuideRelation.Value.Gain, output.TerminalRelation.Value.Gain);
        if (output.ValidRootInterval is null || !output.ValidRootInterval.Contains(rootTurns))
        {
            Issue("OutOfTravelRange", new[] { new MechanicalExactFact("requestedInput.turn", null, rootTurns.Value), new MechanicalExactFact("diagnosticGuidePosition.mm", null, guide.Value),
                new MechanicalExactFact("allowedGuideLower.mm", d.Device.GuideInterval.Lower.Value, guide.Value), new MechanicalExactFact("allowedGuideUpper.mm", d.Device.GuideInterval.Upper.Value, guide.Value),
                new MechanicalExactFact("allowedEngagementLower.mm", d.Device.EngagementInterval.Lower.Value, guide.Value), new MechanicalExactFact("allowedEngagementUpper.mm", d.Device.EngagementInterval.Upper.Value, guide.Value) });
            return Result(RotaryLinearEvaluationStatus.OutOfTravelRange, diagnostic: frame);
        }
        return Result(RotaryLinearEvaluationStatus.Success, frame);
    }
    internal static DimensionedAffineRelation LinearRelation(Rational gain, Rational offset) => PrismaticAlgebra.LinearRelation(gain, offset);
    internal static void Bound(ExactVector3 vector) => PrismaticAlgebra.Bound(vector);
}
