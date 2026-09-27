using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Current CP declaration, retained mounting and exact pitch-circle/tangent-line admission. No shape approximation enters this path.</summary>
public static class RackPinionConnectionQuery
{
    public static RackPinionCompatibilityResult Query(RackPinionDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var o = d.Output; var mapping = d.SourceMapping;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var diagnostics = new List<MechanicalDiagnostic>();
        var refs = new[] { new MechanicalReference("RackPinion", c.Id), new MechanicalReference("Shaft", c.PinionShaftId),
            new MechanicalReference("PinionBody", c.PinionBodyId), new MechanicalReference("RackBody", c.RackBodyId),
            new MechanicalReference("Guide", c.GuideId), new MechanicalReference("LinearDof", c.LinearDofId), new MechanicalReference("LinearOutput", o.Key) };
        var unsupported = false;
        void Check(string domain, bool pass, string code, string detail, bool isUnsupported = false, IEnumerable<MechanicalExactFact>? evidence = null)
        {
            checks.Add(new OrientedDomainCheck(domain, c.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!pass)
            {
                unsupported |= isUnsupported;
                diagnostics.Add(new MechanicalDiagnostic(code, "RackPinionCompatibility", related: refs, facts: evidence,
                    affectedOutputs: new[] { o.Key }, scope: "LocalCircularPitchRackConnectionOnly", detail: detail));
            }
        }
        var dimensions = c.PinionCircularPitch.Kind == QuantityKind.LinearPosition && c.RackPitch.Kind == QuantityKind.LinearPosition &&
            c.LongitudinalOffset.Kind == QuantityKind.LinearPosition && c.PinionReferenceTurns.Kind == QuantityKind.AngularPosition &&
            c.RackReferencePosition.Kind == QuantityKind.LinearPosition && c.ActiveMaterialInterval.Kind == QuantityKind.LinearPosition &&
            c.GuideInterval.Kind == QuantityKind.LinearPosition && o.TerminalDatum.Kind == QuantityKind.LinearPosition;
        Check("Dimensions", dimensions, "DimensionMismatch", "Circular pitch, rack spacing, material/guide/reference/terminal positions are mm; pinion reference is unwrapped turns.");
        Check("Parameterization", c.Parameterization == RackPinionProfile.Parameterization, "UnsupportedParameterization",
            "Only circular-pitch is admitted. Rational radius/module inputs are not converted to a different mechanical definition.", true);
        Check("SourceLengthMapping", mapping is not null && mapping.MillimetersPerSourceUnit > 0, "MissingSourceLengthMapping",
            "Every original source coordinate uses one explicit positive mm/source-unit mapping.");
        Check("SourcePose", mapping is not null && mapping.PoseMm.IsProperCardinal, "InvalidSourcePose",
            "Source placement is a proper cardinal rigid pose; shaft turns and port signs are not rescaled.");
        var teethValid = c.PinionToothCount > 0 && c.PinionToothCount <= RackPinionProfile.MaxTeeth;
        Check("PinionToothCount", teethValid, "InvalidPinionToothCount", "CP pinion tooth count must be a positive integer within this bounded profile.",
            evidence: new[] { new MechanicalExactFact("pinionToothCount", null, c.PinionToothCount), new MechanicalExactFact("maximumSupportedTeeth", RackPinionProfile.MaxTeeth, c.PinionToothCount) });
        var positivePitch = c.PinionCircularPitch.Kind == QuantityKind.LinearPosition && c.RackPitch.Kind == QuantityKind.LinearPosition && c.PinionCircularPitch.Value > 0 && c.RackPitch.Value > 0;
        Check("PositiveCircularPitch", positivePitch, "InvalidCircularPitch", "Both declared tooth spacings must be positive mm, not module or circumference.",
            evidence: new[] { new MechanicalExactFact("pinionCircularPitch.mm", null, c.PinionCircularPitch.Value), new MechanicalExactFact("rackPitch.mm", null, c.RackPitch.Value) });
        var pitchesEqual = c.PinionCircularPitch.Kind == QuantityKind.LinearPosition && c.RackPitch.Kind == QuantityKind.LinearPosition && c.PinionCircularPitch == c.RackPitch;
        Check("LocalPitchCompatibility", pitchesEqual, "CircularPitchMismatch", "An equal circumference does not replace equality of the two tooth spacings.",
            evidence: new[] { new MechanicalExactFact("rackPitch.mm", c.PinionCircularPitch.Value, c.RackPitch.Value) });
        Check("FixedPinion", c.PinionCenterFixed && c.PinionAxisFixed, "UnsupportedGrounding", "The pinion center and axis are ground fixed; a moving pinion carriage is unsupported.", true);
        Check("GuideConstraint", c.GuidePresent, "MissingGuideConstraint", "The prismatic guide must be explicit and present.", true);
        Check("RackGrounding", c.RackRotationFixed && c.TransverseMotionFixed, "MissingGuideConstraint", "Rack anti-rotation and transverse constraints are explicit; no free body is reduced by assumption.", true);
        Check("GuideFrame", c.GuideFrameMm.IsProperCardinal, "InvalidRackDirection", "The explicit rack orientation is one proper cardinal frame.");
        Check("SeparateBodies", c.PinionBodyId != c.RackBodyId && !d.Source.Definition.Bodies.Any(b => b.Id == c.PinionBodyId || b.Id == c.RackBodyId),
            "InvalidBodyBinding", "Pinion and rack are distinct added bodies. Original spur/bevel bodies remain separate and unchanged.");
        Check("LinearOutputBinding", o.LinearDofId == c.LinearDofId && o.BodyId == c.RackBodyId && (o.TerminalSign == 1 || o.TerminalSign == -1) &&
            !d.Source.Definition.Outputs.Any(s => s.Key == o.Key), "InvalidLinearOutputBinding", "The terminal binds the rack reference point with sign +/-1, separately from the source outputs.");
        var shaft = d.Source.Definition.Shafts.FirstOrDefault(s => s.Id == c.PinionShaftId);
        Check("SourceShaftBinding", shaft is not null && shaft.Frame.IsProperCardinal, "InvalidPinionMounting", "The added pinion mounts to an actual retained source shaft.");
        ExactVector3? axis = null; Rational? sign = null, portSign = null; ExactQuantity? gain = null;
        var radius = c.PitchRadius; ExactPiVector3? contact = null;
        if (mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal && shaft is not null && shaft.Frame.IsProperCardinal)
        {
            var sourceFrame = mapping.FrameMm(shaft.Frame); axis = sourceFrame.Z;
            var mountingError = (c.PinionCenterMm - sourceFrame.Origin).Cross(sourceFrame.Z);
            Check("PinionMounting", mountingError == ExactVector3.Zero, "InvalidPinionMounting", "Pinion center lies on the complete mapped retained shaft line; its axial station need not equal an original gear center.",
                evidence: Components("centerToShaftCrossAxis.mm", ExactVector3.Zero, mountingError));
            var a = sourceFrame.Z; var b = c.ContactNormal; var g = c.GuideFrameMm.Z;
            var directions = a.IsCardinal && b.IsCardinal && g.IsCardinal && a.Dot(b) == 0 && a.Dot(g) == 0 && b.Dot(g) == 0;
            Check("RackDirection", directions, "InvalidRackDirection", "Retained rotation axis, contact-side normal and guide tangent are orthogonal unit cardinal directions.",
                evidence: new[] { new MechanicalExactFact("axisDotContactNormal", 0, a.Dot(b)), new MechanicalExactFact("axisDotGuide", 0, a.Dot(g)), new MechanicalExactFact("contactNormalDotGuide", 0, b.Dot(g)) });
            if (directions)
            {
                sign = a.Cross(b).Dot(g); facts.Add(new MechanicalExactFact("contactSign.axisCrossNormalDotGuide", null, sign));
                var relative = c.GuideFrameMm.Origin - ExactPiVector3.FromMillimeters(c.PinionCenterMm);
                var plane = relative.Dot(a); var normal = relative.Dot(b); var longitudinal = relative.Dot(g);
                var planeValid = plane == ExactPiLength.FromCanonical(0, 0);
                var normalValid = normal == radius;
                var longitudinalValid = c.LongitudinalOffset.Kind == QuantityKind.LinearPosition && longitudinal == ExactPiLength.FromCanonical(c.LongitudinalOffset.Value, 0);
                Check("PinionPlane", planeValid, "PinionPlaneMismatch", "Guide pitch line and fixed pitch contact lie in the pinion plane at its declared axial station.",
                    evidence: Coefficients("planeOffset.mm", ExactPiLength.FromCanonical(0, 0), plane));
                Check("RackTangentOffset", normalValid, "NonTangentRackGuide", "The explicit normal offset equals the exact derived CP radius, not a displayed decimal radius.",
                    evidence: Coefficients("normalOffset.mm", radius, normal));
                Check("RackLongitudinalOffset", longitudinalValid, "NonTangentRackGuide", "The explicit origin also preserves the declared rational longitudinal coordinate d.",
                    evidence: Coefficients("longitudinalOffset.mm", ExactPiLength.FromCanonical(c.LongitudinalOffset.Value, 0), longitudinal));
                checks.Add(new OrientedDomainCheck("PitchCircleTangentLine", c.Id,
                    planeValid && normalValid && longitudinalValid && c.GuideFrameMm.IsProperCardinal ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail,
                    true, "Exact coefficient-wise cardinal plane/normal/longitudinal conditions; no tooth-flank or solid claim."));
                if (positivePitch && teethValid)
                {
                    contact = ExactPiVector3.FromCanonical(c.PinionCenterMm, b * radius.InversePiCoefficientMm);
                    if (dimensions)
                    {
                        gain = ExactQuantity.FromCanonical(QuantityKind.LinearPerAngular, sign.Value * c.PinionToothCount * c.PinionCircularPitch.Value);
                        facts.Add(new MechanicalExactFact("localGain.mmPerRetainedTurn", null, gain.Value.Value));
                    }
                }
            }
            else checks.Add(new OrientedDomainCheck("PitchCircleTangentLine", c.Id, OrientedCheckVerdict.Fail, true, "A valid tangent direction must precede local geometric admission."));
            if (c.PinionPortId is not null)
            {
                var port = d.Source.Definition.Ports.FirstOrDefault(p => p.Id == c.PinionPortId);
                var valid = port is not null && port.ShaftId == shaft.Id && port.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(port, shaft);
                Check("SourcePortBinding", valid, "InvalidSourcePortBinding", "The optional original port belongs to the retained shaft; its coordinate sign is evidence, not a second transfer.");
                if (valid) { portSign = shaft.Frame.Z.Dot(port!.Frame.Z); facts.Add(new MechanicalExactFact("sourcePortCoordinateSign.evidenceOnly", null, portSign)); }
            }
        }
        else
        {
            checks.Add(new OrientedDomainCheck("PitchCircleTangentLine", c.Id, OrientedCheckVerdict.Inconclusive, true, "Actual retained shaft mounting and explicit length mapping are prerequisites."));
            if (c.PinionPortId is not null) Check("SourcePortBinding", false, "InvalidSourcePortBinding", "No original mating-port binding exists without a valid retained shaft and mapping.");
        }
        if (mapping is not null) facts.Add(new MechanicalExactFact("millimetersPerSourceUnit", null, mapping.MillimetersPerSourceUnit));
        facts.Add(new MechanicalExactFact("pinionToothCount", null, c.PinionToothCount));
        facts.Add(new MechanicalExactFact("pinionCircularPitch.mm", null, c.PinionCircularPitch.Value));
        facts.Add(new MechanicalExactFact("rackPitch.mm", null, c.RackPitch.Value));
        facts.Add(new MechanicalExactFact("advancePerPinionTurn.mm", null, c.AdvancePerTurn.Value));
        facts.Add(new MechanicalExactFact("pitchRadius.inversePiCoefficientMm", null, radius.InversePiCoefficientMm));
        var okay = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass);
        return new RackPinionCompatibilityResult(c.Id, okay ? ConnectionCompatibilityVerdict.CompatibleWithinProfile : unsupported ? ConnectionCompatibilityVerdict.Unsupported : ConnectionCompatibilityVerdict.Incompatible,
            c.TransmissionPresent, axis, sign, portSign, okay ? gain : null, radius, contact, checks, facts, diagnostics);
    }

    private static IEnumerable<MechanicalExactFact> Coefficients(string key, ExactPiLength expected, ExactPiLength actual) => new[] {
        new MechanicalExactFact(key + ".rationalPart", expected.RationalPartMm, actual.RationalPartMm),
        new MechanicalExactFact(key + ".inversePiCoefficient", expected.InversePiCoefficientMm, actual.InversePiCoefficientMm) };
    private static IEnumerable<MechanicalExactFact> Components(string key, ExactVector3 expected, ExactVector3 actual) => new[] {
        new MechanicalExactFact(key + ".x", expected.X, actual.X), new MechanicalExactFact(key + ".y", expected.Y, actual.Y), new MechanicalExactFact(key + ".z", expected.Z, actual.Z) };
}

/// <summary>Rack-specific admission and material geometry around the same typed graph, affine and observation algebra used by 22A.</summary>
public static class RackPinionAnalyzer
{
    public static RackPinionAnalysis Analyze(RackPinionDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var source = MechanicalAnalyzer.Analyze(d.Source); var local = RackPinionConnectionQuery.Query(draft);
        var graph = PrismaticGraphAnalysis.Build(d.Source, source, c.PinionShaftId, c.LinearDofId, "RackPinion/" + c.Id,
            c.TransmissionPresent, local.Verdict, local.LocalGain, c.PinionReferenceTurns, c.RackReferencePosition);
        var issues = new List<MechanicalDiagnostic>(local.Diagnostics);
        var refs = new[] { new MechanicalReference("LinearOutput", d.Output.Key), new MechanicalReference("LinearDof", c.LinearDofId),
            new MechanicalReference("RackPinion", c.Id), new MechanicalReference("Shaft", c.PinionShaftId), new MechanicalReference("RackBody", c.RackBodyId), new MechanicalReference("Guide", c.GuideId) };
        void Issue(string code, string stage, IEnumerable<MechanicalExactFact>? facts = null, string detail = "") => issues.Add(new MechanicalDiagnostic(code, stage,
            related: refs, facts: facts, affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: graph.Blocked, scope: "LinearOutput", detail: detail));
        if (graph.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion)
            Issue("UndrivenLinearCoordinate", "MixedDeterminacy", detail: "Rack reference position calibrates a relation; the guide and reference do not prescribe a current rack position.");
        else if (graph.Determinacy != MechanicalDeterminacy.DeterminedBySelectedInput) Issue(graph.Determinacy.ToString(), "MixedDeterminacy");
        DimensionedAffineRelation? terminal = null; ExactVector3? worldGain = null; ExactPiVector3? worldOffset = null;
        ExactQuantityInterval? valid = null, guideDomain = null, materialDomain = null;
        var target = MechanicalAxisVerdict.NotAssessed; var required = MechanicalAxisVerdict.NotAssessed;
        if (graph.GuideRelation.HasValue)
        {
            var guide = graph.GuideRelation.Value;
            terminal = PrismaticAlgebra.Terminal(guide, d.Output.TerminalSign, d.Output.TerminalDatum);
            worldGain = c.GuideFrameMm.Z * guide.Gain.Value;
            worldOffset = c.GuideFrameMm.Origin + ExactPiVector3.FromMillimeters(c.GuideFrameMm.Z * guide.Offset.Value);
            PrismaticAlgebra.Bound(worldGain.Value); PrismaticAlgebra.Bound(worldOffset.Value);
            var coverage = MaterialCoverage(c);
            valid = PrismaticAlgebra.OperatingDomain(c.GuideInterval, coverage, guide);
            guideDomain = c.GuideInterval.InverseAffine(guide.Gain.Value, guide.Offset.Value, QuantityKind.AngularPosition);
            materialDomain = coverage.InverseAffine(guide.Gain.Value, guide.Offset.Value, QuantityKind.AngularPosition);
            if (valid is null) Issue("EmptyOperatingDomain", "OperatingDomain", detail: "Finite guide travel and moving material pitch-point coverage have an empty exact intersection.");
            var requirements = PrismaticRequirementAssessment.Assess(d.Requirement, terminal.Value, valid, Issue);
            target = requirements.Target; required = requirements.RequiredRange;
        }
        var checks = new List<OrientedDomainCheck>(local.Checks)
        {
            new("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true,
                "Fresh complete original rotary source, including all retained outputs; source-aware finalization is still required."),
            new("SourceClearance", "source", source.Geometry == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
                source.Geometry == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive, true,
                "Original source policy only. No rational approximation of the CP radius is supplied to a source disk classifier."),
            new("IdealRollingRelation", c.Id, local.IsAdmitted ? OrientedCheckVerdict.Pass :
                local.Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile ? OrientedCheckVerdict.Inconclusive : OrientedCheckVerdict.Fail, true,
                "Only an admitted fixed pitch-circle/tangent-line relation supplies s*Z*p mm per retained pinion turn."),
            new("GuideTravel", c.Id, RequiredDomain(guideDomain, d.Requirement.RequiredRootInterval), true,
                "Whole required input interval is contained in the exact guide-travel inverse, independently of material coverage."),
            new("PitchPointWithinActiveSegment", c.Id, RequiredDomain(materialDomain, d.Requirement.RequiredRootInterval), true,
                "xiContact=-d-x remains in the moving finite material interval at every required input; closed ideal point coverage only."),
            new("RequiredInputDomain", d.Output.Key, Verdict(required), true,
                "Requested operation is contained in the exact intersection of guide travel and active pitch-point coverage."),
            new("OutputRequirements", d.Output.Key, Verdict(target), true,
                "Independent terminal gain and root-zero reference targets never repair the current device or required interval.")
        };
        foreach (var domain in new[] { "ActualToothFlankContact", "PressureAngle/ContactRatio", "AddedBodySolidClearance", "SweptClearance",
            "Shaft/Bearing/GuideStrength", "Backlash/Force/Friction/Dynamics", "Manufacturing" }.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(k => k.Domain == domain)) continue;
            var isRequired = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new OrientedDomainCheck(domain, c.Id, OrientedCheckVerdict.NotPerformed, isRequired,
                "Not implemented by the bounded exact ideal-pitch profile; display geometry is not mechanical proof."));
            if (isRequired) Issue("RequiredValidationNotPerformed", "ValidationDomain", detail: domain);
        }
        var output = new PrismaticOutputAnalysis(d.Output, graph.Determinacy, graph.SourceRelation, graph.GuideRelation, terminal,
            local.PinionPositiveAxis, c.GuideFrameMm.Z, worldGain, worldOffset, valid, required, target,
            graph.DeclaredReachable, graph.AdmittedReachable, graph.Path, graph.DeclaredPath, graph.Blocked, issues);
        return new RackPinionAnalysis(draft, source, local, graph.Nodes, graph.Edges, graph.Components, output, checks,
            source.Diagnostics.Concat(issues).Concat(graph.Components.SelectMany(k => k.Diagnostics)));
    }

    public static RackPinionEvaluation Evaluate(RackPinionAnalysis analysis, ExactQuantity rootTurns)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); RotaryLinearProfile.Quantity(rootTurns);
        var diagnostics = new List<MechanicalDiagnostic>(); var rotary = new List<OrientedShaftEvaluation>();
        RackPinionEvaluation Result(RackPinionEvaluationStatus status, RackPositionEvaluation? linear = null, RackPositionEvaluation? diagnostic = null) =>
            new(analysis.AnalysisId, status, rootTurns, rotary, linear, diagnostic, diagnostics);
        var d = analysis.Draft.Definition; var c = d.Device; var output = analysis.LinearOutput;
        void Issue(string code, IEnumerable<MechanicalExactFact>? facts = null) => diagnostics.Add(new MechanicalDiagnostic(code, "RackPinionEvaluation",
            related: new[] { new MechanicalReference("LinearOutput", output.OutputKey), new MechanicalReference("RackBody", c.RackBodyId),
                new MechanicalReference("Guide", c.GuideId), new MechanicalReference("RackPinion", c.Id) }, facts: facts, scope: "RequestedInput"));
        if (rootTurns.Kind != QuantityKind.AngularPosition) { Issue("DimensionMismatch"); return Result(RackPinionEvaluationStatus.DimensionMismatch); }
        rotary.AddRange(PrismaticAlgebra.EvaluateSource(analysis.SourceAnalysis, d.SourceMapping, rootTurns.Value));
        if (!output.HasDeterminedMotion)
        {
            diagnostics.AddRange(output.Diagnostics);
            return Result(output.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ? RackPinionEvaluationStatus.UndrivenLinearCoordinate : RackPinionEvaluationStatus.InvalidDefinition);
        }
        var guide = output.GuideRelation!.Value.Evaluate(rootTurns); var terminal = output.TerminalRelation!.Value.Evaluate(rootTurns);
        var pinion = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, output.SourceRelation!.Value.Evaluate(rootTurns.Value));
        var world = c.GuideFrameMm.Origin + ExactPiVector3.FromMillimeters(c.GuideFrameMm.Z * guide.Value); PrismaticAlgebra.Bound(world);
        var material = ExactQuantity.FromCanonical(QuantityKind.LinearPosition, -c.LongitudinalOffset.Value - guide.Value);
        var frame = new RackPositionEvaluation(d.Output, pinion, guide, terminal, world, c.GuideFrameMm,
            output.GuideRelation.Value.Gain, output.TerminalRelation.Value.Gain, material, analysis.LocalCompatibility.FixedContactPointMm!.Value);
        var guideInside = c.GuideInterval.Contains(guide); var materialInside = c.ActiveMaterialInterval.Contains(material);
        if (!guideInside) Issue("OutOfGuideTravel", new[] {
            new MechanicalExactFact("requestedInput.turn", null, rootTurns.Value), new MechanicalExactFact("diagnosticGuidePosition.mm", null, guide.Value),
            new MechanicalExactFact("allowedGuideLower.mm", c.GuideInterval.Lower.Value, guide.Value), new MechanicalExactFact("allowedGuideUpper.mm", c.GuideInterval.Upper.Value, guide.Value) });
        if (!materialInside) Issue("OutOfRackEngagement", new[] {
            new MechanicalExactFact("requestedInput.turn", null, rootTurns.Value), new MechanicalExactFact("materialContactCoordinate.mm", null, material.Value),
            new MechanicalExactFact("activeMaterialLower.mm", c.ActiveMaterialInterval.Lower.Value, material.Value), new MechanicalExactFact("activeMaterialUpper.mm", c.ActiveMaterialInterval.Upper.Value, material.Value) });
        if (!guideInside || !materialInside || output.ValidRootInterval is null || !output.ValidRootInterval.Contains(rootTurns))
            return Result(RackPinionEvaluationStatus.OutOfOperatingRange, diagnostic: frame);
        return Result(RackPinionEvaluationStatus.Success, frame);
    }

    private static ExactQuantityInterval MaterialCoverage(CircularPitchRackDefinition c) => new(
        ExactQuantity.FromCanonical(QuantityKind.LinearPosition, -c.LongitudinalOffset.Value - c.ActiveMaterialInterval.Upper.Value),
        ExactQuantity.FromCanonical(QuantityKind.LinearPosition, -c.LongitudinalOffset.Value - c.ActiveMaterialInterval.Lower.Value));
    private static OrientedCheckVerdict RequiredDomain(ExactQuantityInterval? actual, ExactQuantityInterval required) =>
        actual is null || required.Kind != QuantityKind.AngularPosition ? OrientedCheckVerdict.Inconclusive :
        actual.Contains(required) ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail;
    private static OrientedCheckVerdict Verdict(MechanicalAxisVerdict verdict) => verdict == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        verdict == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
}
