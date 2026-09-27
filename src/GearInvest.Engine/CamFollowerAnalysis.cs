using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class CamFollowerAnalyzer
{
    internal static readonly string[] UnperformedDomains = new[] { "BoundedNumericalEvaluation", "ContactLoss/Impact/Dynamics", "Friction/Wear/Stress/Strength", "FullAddedBody/SweptSolidClearance", "EdgeRounding/FaceThickness/Manufacturing", "Roller/Groove/ConjugateContact" };
    public static CamFollowerCompatibilityResult Query(CamFollowerDraft draft, CamGeometryProofRequest? proofRequest = null)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        proofRequest ??= CamGeometryProofRequest.Default;
        try { return QueryCore(draft, proofRequest); }
        catch (ArgumentException e) when (IsResource(e))
        {
            var d = draft.Definition.Device;
            var proof = new CamGeometryProofResult(d.SupportProfile.ProfileId, proofRequest, CamGeometryProofStatus.GeometryResourceLimit,
                0, 0, 0, 0, 0, null, null, e.Message);
            var issue = new MechanicalDiagnostic("GeometryResourceLimit", "CamFollowerGeometry", detail: e.Message);
            return new(d.Id, ConnectionCompatibilityVerdict.Inconclusive, d.ContactPresent, false, false, null, null, null, null, proof,
                new[] { new OrientedDomainCheck("ExactDerivedResourceBounds", d.Id, OrientedCheckVerdict.Inconclusive, true, e.Message) },
                Array.Empty<MechanicalExactFact>(), new[] { issue });
        }
    }

    private static CamFollowerCompatibilityResult QueryCore(CamFollowerDraft draft, CamGeometryProofRequest proofRequest)
    {
        var d = draft.Definition;
        return QueryLocal(d.Device, d.Output, BoundMechanicalInputContext.FromLegacy(d.Source, d.SourceMapping, d.Device.SourceShaftId, d.Device.SourcePortId), proofRequest);
    }

    internal static CamFollowerCompatibilityResult QueryLocal(FlatCamFollowerDefinition c, PrismaticOutputDefinition output, BoundMechanicalInputContext input, CamGeometryProofRequest proofRequest)
    {
        var source = input.LegacySource?.Definition;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var issues = new List<MechanicalDiagnostic>();
        bool unsupported = false;
        void Check(string domain, bool pass, string code, string detail, bool outside = false)
        {
            checks.Add(new(domain, c.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!pass)
            {
                unsupported |= outside;
                issues.Add(new(code, domain, related: new[] { new MechanicalReference("CamFollower", c.Id), new MechanicalReference("Shaft", c.SourceShaftId) },
                    facts: facts, affectedOutputs: new[] { output.Key }, scope: "CamFollowerLocal", detail: detail));
            }
        }
        var standalone = source is not null && source.Shafts.Count == 1 && source.Shafts[0].IsPrescribed && source.RootShaftId == source.Shafts[0].Id &&
            source.RootShaftId == c.SourceShaftId && source.Bodies.Count == 0 && source.Contacts.Count == 0 && source.Connections.Count == 0 &&
            source.Outputs.Count == 0 && source.KeepOuts.Count == 0 && source.Ports.All(p => p.ShaftId == source.RootShaftId);
        Check("SupportedSourceProfile", !input.IsLegacy ? input.BindingAdmitted : input.LegacySource!.OriginalArtifactBytes is not null ?
            input.LegacySource.ImportedProfile == MechanicalAuthoringProfile.PlanarExport || OrientedTwoOutputProfile.IsSupported(input.LegacySource.ImportedProfile ?? "") : standalone,
            "UnsupportedSourceProfile", "Complete supported planar/oriented source or a genuine one-shaft prescribed source, never a proxy scalar attachment.", true);
        Check("SinglePrescribedInput", (source is null ? input.RootInputId is not null : source.Shafts.Count(s => s.IsPrescribed) == 1 && source.Shafts.Any(s => s.Id == source.RootShaftId && s.IsPrescribed)) && !c.FollowerIsPrescribed,
            "UnsupportedInputTopology", "Source rotation is the sole driver; the follower is never independently prescribed.", true);
        var distinct = c.CamBodyId != c.FollowerBodyId && !input.HasBodyId(c.CamBodyId) && !input.HasBodyId(c.FollowerBodyId) && !input.HasShaftId(c.LinearDofId);
        Check("SeparateMovingBodies", distinct, "InvalidBodyIdentity", "A distinct cam and follower preserve all actual source bodies.");
        Check("OutputBinding", output.BodyId == c.FollowerBodyId && output.LinearDofId == c.LinearDofId &&
            output.ReferencePointId == c.FollowerReferenceId && (output.TerminalSign == -1 || output.TerminalSign == 1) && output.TerminalDatum.Kind == QuantityKind.LinearPosition,
            "MissingEndpoint", "Output binds the real follower reference; calibration does not move or reverse its body.");
        Check("FollowerKind", c.FollowerKind == "FlatTranslating", "UnsupportedFollowerKind", "This support contact law requires a flat translating follower.", true);
        var mappingValid = input.MappingValid;
        Check("SourceLengthMapping", mappingValid, "InvalidSourceLengthMapping", "Explicit positive source-to-mm scale and proper cardinal mapping applied once.");
        var shaft = input.Shaft;
        Check("SourceShaftBinding", shaft?.Frame.IsProperCardinal == true, "InvalidCamMounting", "Cam mounts to an actual retained shaft.");
        Check("GuideFrame", c.GuideFrameMm.IsProperCardinal, "InvalidGuideDirection", "Guide Z is the increasing linear coordinate.");
        var basis = c.PlaneNormal.IsCardinal && c.GuideFrameMm.Z.IsCardinal && c.PlaneNormal.Dot(c.GuideFrameMm.Z) == 0;
        Check("PlanarGuideCompatibility", basis, "InvalidGuideDirection", "Plane normal and increasing guide direction are perpendicular cardinal axes.");
        OrientedFrame? mapped = null; Rational? epsilon = null, gamma = null; var mounting = false; var coplanar = false;
        if (mappingValid && shaft?.Frame.IsProperCardinal == true)
        {
            mapped = input.FixedFrameMm!;
            mounting = c.CamStation.Kind == QuantityKind.LinearPosition && c.MountingTurns.Kind == QuantityKind.AngularPosition && c.CamAxisFixed &&
                c.CenterMm == mapped.Origin + mapped.Z * c.CamStation.Value;
            if (c.SourcePortId is not null)
            {
                var port = input.Port;
                var valid = port is not null && port.ShaftId == c.SourceShaftId && port.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(port, shaft);
                Check("SourcePortBinding", valid, "MissingEndpoint", "Port is mounting evidence only; no second multiplication by port sign.");
                mounting &= valid;
                if (valid) facts.Add(new("sourcePortCoordinateSign.evidenceOnly", null, shaft.Frame.Z.Dot(port!.Frame.Z)));
            }
            if (basis && mapped.Z.Cross(c.PlaneNormal) == ExactVector3.Zero)
            {
                coplanar = c.PlaneNormal.Dot(c.GuideFrameMm.Origin - c.CenterMm) == 0;
                if (c.MountingTurns.Kind == QuantityKind.AngularPosition)
                {
                    epsilon = mapped.Z.Dot(c.PlaneNormal);
                    gamma = PitchChainGeometry.QuarterTurn(mapped.X, c.GuideFrameMm.Z, c.PlaneNormal.Cross(c.GuideFrameMm.Z)) + epsilon.Value * c.MountingTurns.Value;
                    MechanicalDerivedNumbers.Check(gamma.Value);
                }
            }
        }
        Check("CamMounting", mounting, "InvalidCamMounting", "Cam center equals mapped shaft origin plus its explicit axial mm station; the axis is fixed.");
        Check("MechanismCoplanarity", coplanar, "NonCoplanarGuide", "Guide and cam center lie in the same plane, normal collinear with the shaft axis.");
        Check("ContactConstraint", c.ContactPresent, "MissingCamContact", "Removing contact releases follower position without releasing the retained cam.");
        Check("MaintainedContactPolicy", c.ContactPolicy == CamFollowerProfile.MaintainedContactIdeal, "UnsupportedContactPolicy",
            "MaintainedContactIdeal is an explicit ideal constraint; this is not a spring, gravity or contact-loss calculation.", true);
        Check("GroundedGuide", c.GuidePresent && c.FollowerRotationFixed && c.TransverseMotionFixed, "MissingGuideConstraint", "Grounded guide, anti-rotation and fixed transverse motion are necessary.");
        Check("GuideTravelDefinition", c.GuideTravel.Kind == QuantityKind.LinearPosition, "DimensionMismatch", "Guide limits are finite closed mm coordinates.");
        Check("FollowerFaceDefinition", c.FollowerFace.Kind == QuantityKind.LinearPosition, "DimensionMismatch", "Face endpoints are finite closed mm distances from the follower reference along f.");
        var shapeIssues = CamSupportGeometry.Validate(c.SupportProfile);
        Check("SupportProfileStructure", shapeIssues.Count == 0, "InvalidSupportProfile", shapeIssues.Count == 0 ? "Positive periodic C2 support, complete half-open segment partition." : string.Join(",", shapeIssues));
        CamFollowerEnvelope? envelope = null;
        if (basis && shapeIssues.Count == 0)
        {
            var delta = c.GuideFrameMm.Origin - c.CenterMm;
            var datum = delta.Dot(c.GuideFrameMm.Z); var offset = delta.Dot(c.PlaneNormal.Cross(c.GuideFrameMm.Z));
            MechanicalDerivedNumbers.Check(datum); MechanicalDerivedNumbers.Check(offset);
            envelope = new(CamSupportGeometry.GetEnvelope(c.SupportProfile), datum, offset);
            facts.Add(new("guideDatum.mm", null, datum)); facts.Add(new("guideOffset.mm", null, offset));
        }
        var dependencyValid = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass);
        var proof = CamGeometryProver.Prove(c.SupportProfile, proofRequest);
        var proofVerdict = proof.IsProved ? OrientedCheckVerdict.Pass : proof.IsRefuted || proof.Status == CamGeometryProofStatus.InvalidProfile ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
        checks.Add(new("GlobalConvexSupportContact", c.Id, proofVerdict, true, proof.Summary));
        if (!proof.IsProved) issues.Add(new(proof.Status.ToString(), "GlobalConvexSupportContact", affectedOutputs: new[] { output.Key }, detail: proof.Summary));
        var verdict = checks.All(k => k.Verdict == OrientedCheckVerdict.Pass) ? ConnectionCompatibilityVerdict.CompatibleWithinProfile :
            unsupported ? ConnectionCompatibilityVerdict.Unsupported : checks.Any(k => k.Verdict == OrientedCheckVerdict.Fail) ? ConnectionCompatibilityVerdict.Incompatible : ConnectionCompatibilityVerdict.Inconclusive;
        return new(c.Id, verdict, c.ContactPresent, mounting && distinct, dependencyValid, mapped, gamma, epsilon, envelope, proof, checks, facts, issues);
    }

    public static CamFollowerAnalysis Analyze(CamFollowerDraft draft, CamGeometryProofRequest? proofRequest = null)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft)); proofRequest ??= CamGeometryProofRequest.Default;
        var source = MechanicalAnalyzer.Analyze(draft.Definition.Source); var local = Query(draft, proofRequest);
        var d = draft.Definition; var c = d.Device; var issues = new List<MechanicalDiagnostic>(source.Diagnostics.Concat(local.Diagnostics));
        var checks = new List<OrientedDomainCheck>(local.Checks);
        var component = source.AdmittedComponents.FirstOrDefault(k => k.ShaftIds.Contains(c.SourceShaftId));
        var declared = source.DeclaredComponents.FirstOrDefault(k => k.ShaftIds.Contains(c.SourceShaftId));
        var node = component?.Affine?.Relations.FirstOrDefault(r => r.DofId == c.SourceShaftId);
        ExactAffineRelation? retained = component?.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput ? node?.Relation : null;
        var dependency = "CamFollower/" + c.Id;
        var declaredReachable = c.ContactPresent && declared?.IsSelectedInputReachable == true;
        var declaredPath = declaredReachable && source.SelectedInputId is not null ? MechanicalAnalyzer.Path(source.SelectedInputId, c.SourceShaftId,
            source.Edges.Where(e => e.IsDeclaredResolvable)).Concat(new[] { dependency }).ToArray() : Array.Empty<string>();
        var blocked = declaredPath.Where(key => key == dependency ? !local.IsAdmitted : !source.Edges.Single(e => e.ConstraintKey == key).IsAdmitted).ToList();
        if (!c.ContactPresent) blocked.Add(dependency);
        if (!c.GuidePresent || !c.FollowerRotationFixed || !c.TransverseMotionFixed) blocked.Add("Guide/" + c.GuideId);
        CamFollowerMotionDescriptor? descriptor = null; var dependencyResourceLimited = false;
        void Issue(string code, string stage, string detail) => issues.Add(new(code, stage, affectedOutputs: new[] { d.Output.Key }, blockedPrerequisites: blocked, detail: detail));
        try
        {
            if (local.HasValidDependency && retained.HasValue && !retained.Value.Coefficient.IsZero)
                descriptor = new(c, d.Output, retained.Value, local.Epsilon!.Value, local.GammaTurns!.Value);
        }
        catch (ArgumentException e) when (IsResource(e)) { dependencyResourceLimited = true; Issue("GeometryResourceLimit", "CamFollowerDependency", e.Message); }
        var dependencyInconclusive = dependencyResourceLimited || retained.HasValue && !retained.Value.Coefficient.IsZero &&
            local.Verdict == ConnectionCompatibilityVerdict.Inconclusive && IsProofAvailabilityFailure(local.GeometryProof.Status);
        var determinacy = component?.Affine?.Determinacy ?? MechanicalDeterminacy.BlockedByInvalidConstraint;
        if ((!local.IsAdmitted || descriptor is null) && !dependencyInconclusive)
            determinacy = !c.ContactPresent && local.HasValidCamMounting ? MechanicalDeterminacy.UndrivenRelativeMotion :
                local.Verdict == ConnectionCompatibilityVerdict.Unsupported ? MechanicalDeterminacy.UnsupportedConstraintDomain : MechanicalDeterminacy.BlockedByInvalidConstraint;
        if (descriptor is null) Issue(dependencyInconclusive ? "ContactDependencyIncomplete" : declared?.IsSelectedInputReachable != true ? "DisconnectedFromInput" : !c.ContactPresent ? "UndrivenLinearCoordinate" : "UnavailableContactDependency",
            "CamFollowerDependency", "No absolute follower recipe without valid current source, profile, contact and grounded guide prerequisites. Constant source gain is outside this full-turn input profile.");
        var env = local.Envelope;
        var guide = env is null || c.GuideTravel.Kind != QuantityKind.LinearPosition ? MechanicalAxisVerdict.NotAssessed :
            env.LowerMm >= c.GuideTravel.Lower.Value && env.UpperMm <= c.GuideTravel.Upper.Value ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
        var face = env is null || c.FollowerFace.Kind != QuantityKind.LinearPosition ? MechanicalAxisVerdict.NotAssessed :
            FaceCoverage(env.MinimumFaceCoordinateMm, env.MaximumFaceCoordinateMm, c.FollowerFace);
        if (guide == MechanicalAxisVerdict.Fail) Issue("GuideDoesNotCoverFullCycle", "GuideTravelCoverage", "Current guide does not contain the exact attained support-height envelope; no auto repair.");
        if (face == MechanicalAxisVerdict.Fail) Issue("FaceDoesNotCoverFullCycle", "FollowerFaceCoverage", "Finite face does not contain the attained derivative contact-footprint envelope; height is not face coverage.");
        var target = AssessRequirement(local, descriptor, d.Requirement, Issue);
        checks.Add(new("SourceValidity", "source", source.IsMechanicallyValid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Whole source is independently analyzed and finalized through21A."));
        checks.Add(new("SourceClearance", "source", Convert(source.Geometry), true, "Existing source clearance only, not added cam/follower solids."));
        checks.Add(new("GuideTravelCoverage", c.Id, Convert(guide), true, "Exact monotone endpoint envelope."));
        checks.Add(new("FollowerFaceCoverage", c.Id, Convert(face), true, "Exact attained derivative extrema and certified pi ordering."));
        checks.Add(new("NonlinearOutputRequirements", c.Id, Convert(target), true, "Independent requested stroke, positive-stroke policy and explicit-root position."));
        checks.Add(new("WholeMotionDeterminacy", c.Id, descriptor is not null && local.IsAdmitted ? OrientedCheckVerdict.Pass :
            dependencyInconclusive ? OrientedCheckVerdict.Inconclusive : OrientedCheckVerdict.Fail, true,
            dependencyInconclusive ? "Retained input determinacy is separate from unfinished contact proof or exact construction; no admitted follower pose." :
            "Source-driven contact computation, not a fixed-gain follower edge."));
        foreach (var domain in UnperformedDomains.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            if (checks.Any(k => k.Domain == domain)) continue;
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new(domain, c.Id, OrientedCheckVerdict.NotPerformed, required, "Not performed by this bounded geometric-ideal position model; numerical availability is per request."));
            if (required) Issue("RequiredValidationNotPerformed", domain, "An unperformed required validation blocks finalization.");
        }
        return new(draft, source, local, proofRequest, determinacy, retained, descriptor, guide, face, target, declaredReachable,
            descriptor is not null && local.IsAdmitted ? (node?.ConstraintPath.AsEnumerable() ?? Array.Empty<string>()).Concat(new[] { dependency }) : Array.Empty<string>(),
            declaredPath, blocked, checks, issues);
    }


    internal static MechanicalAxisVerdict AssessRequirement(CamFollowerCompatibilityResult local, CamFollowerMotionDescriptor? descriptor,
        CamFollowerRequirement r, Action<string, string, string> Issue)
    {
        var env = local.Envelope;
        var target = MechanicalAxisVerdict.Pass;
        if (r.RequiredStroke.HasValue)
        {
            if (r.RequiredStroke.Value.Kind != QuantityKind.LinearPosition || r.RequiredStroke.Value.Value < 0) { target = MechanicalAxisVerdict.Fail; Issue("DimensionMismatch", "NonlinearOutputRequirements", "Requested stroke is a nonnegative mm distance."); }
            else if (env is null) target = MechanicalAxisVerdict.NotAssessed;
            else if (env.StrokeMm != r.RequiredStroke.Value.Value) { target = MechanicalAxisVerdict.Fail; Issue("TargetMismatch", "NonlinearOutputRequirements", "Exact stroke differs from the independent target."); }
        }
        if (r.PositiveStrokeRequired && (env is null || env.StrokeMm <= 0)) { target = env is null ? MechanicalAxisVerdict.NotAssessed : MechanicalAxisVerdict.Fail; Issue("PositiveStrokeRequired", "NonlinearOutputRequirements", "A circle is normal zero-stroke geometry unless positive stroke is explicitly required."); }
        if (r.ReferenceRoot.Kind != QuantityKind.AngularPosition || r.RequiredReferencePosition.HasValue && r.RequiredReferencePosition.Value.Kind != QuantityKind.LinearPosition)
        { target = MechanicalAxisVerdict.Fail; Issue("DimensionMismatch", "NonlinearOutputRequirements", "Reference root uses turns and terminal position uses mm."); }
        else if (r.RequiredReferencePosition.HasValue)
        {
            try
            {
                if (descriptor is null) { if (target != MechanicalAxisVerdict.Fail) target = MechanicalAxisVerdict.NotAssessed; }
                else if (descriptor.At(r.ReferenceRoot).TerminalPosition.Value != r.RequiredReferencePosition.Value.Value)
                { target = MechanicalAxisVerdict.Fail; Issue("TargetMismatch", "NonlinearOutputRequirements", "Exact reference-root terminal position differs from target."); }
            }
            catch (ArgumentException e) when (IsResource(e)) { target = MechanicalAxisVerdict.Inconclusive; Issue("GeometryResourceLimit", "NonlinearOutputRequirements", e.Message); }
        }
        return target;
    }
    internal static MechanicalAxisVerdict FaceCoverage(ExactPiLength low, ExactPiLength high, ExactQuantityInterval face, CamNumericRequest? request = null)
    {
        var a = CamFollowerNumerics.Compare(low, ExactPiLength.FromMillimeters(face.Lower), request);
        var b = CamFollowerNumerics.Compare(high, ExactPiLength.FromMillimeters(face.Upper), request);
        if (a == CamPiComparison.Less || b == CamPiComparison.Greater) return MechanicalAxisVerdict.Fail;
        return a == CamPiComparison.Unresolved || b == CamPiComparison.Unresolved ? MechanicalAxisVerdict.Inconclusive : MechanicalAxisVerdict.Pass;
    }
    public static CamFollowerPoseRecipe? CreatePoseRecipe(CamFollowerAnalysis analysis, ExactQuantity root) =>
        (analysis ?? throw new ArgumentNullException(nameof(analysis))).CreatePoseRecipe(root);

    public static CamFollowerEvaluation Evaluate(CamFollowerAnalysis analysis, ExactQuantity rootTurns, CamNumericRequest? request = null)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); request ??= CamNumericRequest.Default;
        var d = analysis.Draft.Definition; var c = d.Device; var issues = new List<MechanicalDiagnostic>();
        var rotary = Array.Empty<OrientedShaftEvaluation>(); CamFollowerCamEvaluation? cam = null; CamFollowerPoseRecipe? recipe = null;
        CamNumericResult? contact = null, material = null; var face = CamFollowerPointDecision.NotAssessed; var travel = CamFollowerPointDecision.NotAssessed;
        CamPiComparisonResult? faceLower = null, faceUpper = null; var work = 0;
        CamFollowerEvaluation Result(CamFollowerEvaluationStatus status) => new(analysis, rootTurns, request, status, face, travel, rotary, cam, recipe, contact, material, faceLower, faceUpper, issues);
        CamNumericRequest RemainingRequest() => new(request.AbsoluteWidth, request.MaximumWork - work,
            request.MaximumPrecisionBits, request.MaximumRefinements, request.Policy);
        void Issue(string code, string detail) => issues.Add(new(code, "CamFollowerEvaluation", affectedOutputs: new[] { d.Output.Key }, detail: detail));
        if (rootTurns.Kind != QuantityKind.AngularPosition) { Issue("DimensionMismatch", "Root is unwrapped turns."); return Result(CamFollowerEvaluationStatus.DimensionMismatch); }
        try
        {
            MechanicalAuthoringProfile.Number(rootTurns.Value);
            rotary = PrismaticAlgebra.EvaluateSource(analysis.SourceAnalysis, d.SourceMapping, rootTurns.Value).ToArray();
            if (analysis.HasDeterminedCamMotion)
            {
                var local = analysis.LocalCompatibility; var turns = analysis.SourceRelation!.Value.Evaluate(rootTurns.Value); MechanicalDerivedNumbers.Check(turns);
                Rational? phi = local.GammaTurns.HasValue && local.Epsilon.HasValue ? local.GammaTurns.Value + local.Epsilon.Value * turns : null;
                if (phi.HasValue) MechanicalDerivedNumbers.Check(phi.Value);
                cam = new(c.CamBodyId, c.CenterMm, local.MappedShaftFrameMm!.Z, local.MappedShaftFrameMm.X, turns, phi);
            }
            if (analysis.Descriptor is null)
            {
                issues.AddRange(analysis.Diagnostics);
                return Result(analysis.Determinacy == MechanicalDeterminacy.UndrivenRelativeMotion ? CamFollowerEvaluationStatus.UndrivenLinearCoordinate :
                    analysis.LocalCompatibility.Verdict == ConnectionCompatibilityVerdict.Inconclusive && IsProofAvailabilityFailure(analysis.GeometryProof.Status)
                        ? ProofEvaluationStatus(analysis.GeometryProof.Status) : CamFollowerEvaluationStatus.InvalidDefinition);
            }
            recipe = analysis.Descriptor.At(rootTurns);
            travel = recipe.GuidePosition.Value >= c.GuideTravel.Lower.Value && recipe.GuidePosition.Value <= c.GuideTravel.Upper.Value ? CamFollowerPointDecision.InRange : CamFollowerPointDecision.OutOfRange;
            if (!CamNumericalPrimitives.Valid(request))
            { Issue("InvalidNumericRequest", "Invalid width, units, policy or bounded numerical options."); return Result(CamFollowerEvaluationStatus.InvalidNumericRequest); }
            // One caller budget covers all four numerical stages. A stage that cannot decide
            // still leaves independent stages their remaining budget and preserves diagnostic data.
            faceLower = CamFollowerNumerics.CompareDetailed(recipe.FaceCoordinateMm, ExactPiLength.FromMillimeters(c.FollowerFace.Lower), RemainingRequest());
            work += faceLower.Work;
            faceUpper = CamFollowerNumerics.CompareDetailed(recipe.FaceCoordinateMm, ExactPiLength.FromMillimeters(c.FollowerFace.Upper), RemainingRequest());
            work += faceUpper.Work;
            face = faceLower.Comparison == CamPiComparison.Less || faceUpper.Comparison == CamPiComparison.Greater ? CamFollowerPointDecision.OutOfRange :
                !faceLower.IsResolved || !faceUpper.IsResolved ? CamFollowerPointDecision.Unresolved : CamFollowerPointDecision.InRange;
            contact = CamFollowerNumerics.Evaluate(recipe.ContactPointMm, RemainingRequest()); work += contact.Work;
            material = CamFollowerNumerics.Evaluate(analysis.Descriptor.CreateContourPointRecipe(0, rootTurns), RemainingRequest()); work += material.Work;
            if (!analysis.GeometryProof.IsProved)
            { Issue(analysis.GeometryProof.Status.ToString(), analysis.GeometryProof.Summary); return Result(ProofEvaluationStatus(analysis.GeometryProof.Status)); }
            if (face == CamFollowerPointDecision.Unresolved)
            {
                var comparison = !faceLower.IsResolved ? faceLower : faceUpper;
                Issue(comparison.Status.ToString(), comparison.Detail);
                return Result(NumericEvaluationStatus(comparison.Status));
            }
            if (!contact.IsAvailable || !material.IsAvailable)
            {
                var numeric = !contact.IsAvailable ? contact : material;
                Issue(numeric.Status.ToString(), numeric.Detail);
                return Result(NumericEvaluationStatus(numeric.Status));
            }
            if (face == CamFollowerPointDecision.OutOfRange) { Issue("OutOfFollowerFace", "Actual contact is outside the finite face; no clamping or replacement contact."); return Result(CamFollowerEvaluationStatus.OutOfFollowerFace); }
            if (travel == CamFollowerPointDecision.OutOfRange) { Issue("OutOfGuideTravel", "Actual follower reference is outside current guide travel."); return Result(CamFollowerEvaluationStatus.OutOfGuideTravel); }
            if (face != CamFollowerPointDecision.InRange) { Issue("FaceDecisionUnresolved", "Certified ordering did not decide the face boundary."); return Result(CamFollowerEvaluationStatus.FaceDecisionUnresolved); }
            return Result(CamFollowerEvaluationStatus.Success);
        }
        catch (ArgumentException e) when (IsResource(e))
        {
            var inputLimit = e.Message == "Exact input digit bound exceeded.";
            Issue(inputLimit ? "NumericResourceLimit" : "GeometryResourceLimit", e.Message);
            return Result(inputLimit ? CamFollowerEvaluationStatus.NumericResourceLimit : CamFollowerEvaluationStatus.GeometryResourceLimit);
        }
    }
    private static bool IsResource(ArgumentException e) => e.InnerException is CrankSliderNumerics.Stop ||
        e.Message.StartsWith("Cam exact sample resource limit: ", StringComparison.Ordinal) ||
        e.Message == "Derived exact digit bound exceeded." || e.Message == "Exact quantity resource digit bound exceeded." || e.Message == "Exact input digit bound exceeded.";
    private static bool IsProofAvailabilityFailure(CamGeometryProofStatus status) => status == CamGeometryProofStatus.GeometryProofIncomplete ||
        status == CamGeometryProofStatus.InvalidProofRequest || status == CamGeometryProofStatus.GeometryResourceLimit;
    private static CamFollowerEvaluationStatus ProofEvaluationStatus(CamGeometryProofStatus status) => status == CamGeometryProofStatus.ConvexityRefuted ? CamFollowerEvaluationStatus.ConvexityRefuted :
        status == CamGeometryProofStatus.InvalidProofRequest ? CamFollowerEvaluationStatus.InvalidProofRequest :
        status == CamGeometryProofStatus.GeometryResourceLimit ? CamFollowerEvaluationStatus.GeometryResourceLimit :
        status == CamGeometryProofStatus.InvalidProfile ? CamFollowerEvaluationStatus.InvalidDefinition : CamFollowerEvaluationStatus.GeometryProofIncomplete;
    private static CamFollowerEvaluationStatus NumericEvaluationStatus(CamNumericStatus status) => status == CamNumericStatus.InvalidNumericRequest ? CamFollowerEvaluationStatus.InvalidNumericRequest :
        status == CamNumericStatus.NumericResourceLimit ? CamFollowerEvaluationStatus.NumericResourceLimit :
        status == CamNumericStatus.IncompleteNumericBudget ? CamFollowerEvaluationStatus.IncompleteNumericBudget : CamFollowerEvaluationStatus.FaceDecisionUnresolved;
    private static OrientedCheckVerdict Convert(MechanicalAxisVerdict verdict) => verdict == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass :
        verdict == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : verdict == MechanicalAxisVerdict.NotAssessed ? OrientedCheckVerdict.NotPerformed : OrientedCheckVerdict.Inconclusive;
}
