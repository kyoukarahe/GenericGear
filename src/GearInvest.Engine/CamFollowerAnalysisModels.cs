using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Full profile phase envelope, not a sampled or currently visited range.</summary>
public sealed class CamFollowerEnvelope
{
    internal CamFollowerEnvelope(CamSupportEnvelope support, Rational datum, Rational offset)
    {
        Support = support; LowerMm = support.MinimumHeightMm - datum; UpperMm = support.MaximumHeightMm - datum;
        GuideDatumMm = datum; GuideOffsetMm = offset;
        MinimumFaceCoordinateMm = support.MinimumTangentOffsetMm - ExactPiLength.FromCanonical(offset, 0);
        MaximumFaceCoordinateMm = support.MaximumTangentOffsetMm - ExactPiLength.FromCanonical(offset, 0);
        MechanicalDerivedNumbers.Check(LowerMm); MechanicalDerivedNumbers.Check(UpperMm);
    }
    public CamSupportEnvelope Support { get; } public Rational LowerMm { get; } public Rational UpperMm { get; }
    public Rational StrokeMm => Support.StrokeMm; public Rational GuideDatumMm { get; } public Rational GuideOffsetMm { get; }
    public ExactPiLength MinimumFaceCoordinateMm { get; } public ExactPiLength MaximumFaceCoordinateMm { get; }
    public string CanonicalRepresentation => Pack(Support.CanonicalRepresentation, F(GuideDatumMm), F(GuideOffsetMm), F(LowerMm), F(UpperMm),
        CamFollowerProfile.Pi(MinimumFaceCoordinateMm), CamFollowerProfile.Pi(MaximumFaceCoordinateMm));
}

public sealed class CamFollowerCompatibilityResult
{
    internal CamFollowerCompatibilityResult(string deviceId, ConnectionCompatibilityVerdict verdict, bool contactPresent,
        bool mountingValid, bool dependencyValid, OrientedFrame? mapped, Rational? gamma, Rational? epsilon,
        CamFollowerEnvelope? envelope, CamGeometryProofResult proof, IEnumerable<OrientedDomainCheck> checks,
        IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = deviceId; Verdict = verdict; ContactPresent = contactPresent; HasValidCamMounting = mountingValid;
        HasValidDependency = dependencyValid; MappedShaftFrameMm = mapped; GammaTurns = gamma; Epsilon = epsilon; Envelope = envelope; GeometryProof = proof;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; } public string Scope => "ConvexCamFlatFollowerLocalContact";
    public ConnectionCompatibilityVerdict Verdict { get; } public bool ContactPresent { get; }
    public bool HasValidCamMounting { get; } public bool HasValidDependency { get; }
    public bool IsAdmitted => Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile && ContactPresent;
    public OrientedFrame? MappedShaftFrameMm { get; } public Rational? GammaTurns { get; } public Rational? Epsilon { get; }
    public CamFollowerEnvelope? Envelope { get; } public CamGeometryProofResult GeometryProof { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; } public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

/// <summary>Exact algebraic recipe is distinct from global-contact and pointwise admission.</summary>
public sealed class CamFollowerMotionDescriptor
{
    internal CamFollowerMotionDescriptor(FlatCamFollowerDefinition device, PrismaticOutputDefinition output,
        ExactAffineRelation source, Rational epsilon, Rational gamma)
    {
        Device = device; Output = output; SourceRelation = source; PhysicalPhaseRelation = source.Then(epsilon, gamma);
        GuideDirection = device.GuideFrameMm.Z; PlaneNormal = device.PlaneNormal; InPlanePerpendicular = PlaneNormal.Cross(GuideDirection);
        GuideDatumMm = (device.GuideFrameMm.Origin - device.CenterMm).Dot(GuideDirection);
        GuideOffsetMm = (device.GuideFrameMm.Origin - device.CenterMm).Dot(InPlanePerpendicular);
        foreach (var n in new[] { GuideDatumMm, GuideOffsetMm, PhysicalPhaseRelation.Coefficient, PhysicalPhaseRelation.Phase }) MechanicalDerivedNumbers.Check(n);
        DescriptorId = HashText(CanonicalRepresentation);
    }
    public FlatCamFollowerDefinition Device { get; } public PrismaticOutputDefinition Output { get; }
    public ExactAffineRelation SourceRelation { get; } public ExactAffineRelation PhysicalPhaseRelation { get; }
    public ExactVector3 CenterMm => Device.CenterMm; public ExactVector3 GuideOriginMm => Device.GuideFrameMm.Origin;
    public ExactVector3 GuideDirection { get; } public ExactVector3 InPlanePerpendicular { get; } public ExactVector3 PlaneNormal { get; }
    public Rational GuideDatumMm { get; } public Rational GuideOffsetMm { get; }
    public int TerminalSign => Output.TerminalSign; public Rational TerminalDatumMm => Output.TerminalDatum.Value;
    public string DescriptorId { get; }
    public string CanonicalRepresentation => Pack(Device.CanonicalRepresentation, Output.CanonicalRepresentation,
        F(SourceRelation.Coefficient), F(SourceRelation.Phase), F(PhysicalPhaseRelation.Coefficient), F(PhysicalPhaseRelation.Phase),
        F(GuideDatumMm), F(GuideOffsetMm));
    public CamFollowerPoseRecipe At(ExactQuantity root)
    {
        if (root.Kind != QuantityKind.AngularPosition) throw new ArgumentException("DimensionMismatch: root uses turns.");
        MechanicalDerivedNumbers.Check(root.Value);
        return new(this, root);
    }
    public CamContourPointRecipe CreateContourPointRecipe(Rational materialNormalTurns, ExactQuantity root)
    {
        if (root.Kind != QuantityKind.AngularPosition) throw new ArgumentException("DimensionMismatch: root uses turns.");
        return CamContourPointRecipe.Create(Device.SupportProfile, materialNormalTurns, PhysicalPhaseRelation.Evaluate(root.Value),
            CenterMm, GuideDirection, InPlanePerpendicular);
    }
}

public sealed class CamFollowerPoseRecipe
{
    internal CamFollowerPoseRecipe(CamFollowerMotionDescriptor descriptor, ExactQuantity root)
    {
        Descriptor = descriptor; RootTurns = root; SourceTurns = descriptor.SourceRelation.Evaluate(root.Value);
        PhysicalPhaseTurns = descriptor.PhysicalPhaseRelation.Evaluate(root.Value);
        MechanicalDerivedNumbers.Check(SourceTurns); MechanicalDerivedNumbers.Check(PhysicalPhaseTurns);
        Support = CamSupportGeometry.At(descriptor.Device.SupportProfile, -PhysicalPhaseTurns);
        GuidePosition = ExactQuantity.FromCanonical(QuantityKind.LinearPosition, Support.HeightMm - descriptor.GuideDatumMm);
        TerminalPosition = ExactQuantity.FromCanonical(QuantityKind.LinearPosition, descriptor.TerminalSign * GuidePosition.Value + descriptor.TerminalDatumMm);
        FollowerReferencePointMm = descriptor.CenterMm + descriptor.GuideDirection * Support.HeightMm + descriptor.InPlanePerpendicular * descriptor.GuideOffsetMm;
        ContactPointMm = ExactPiVector3.FromCanonical(descriptor.CenterMm + descriptor.GuideDirection * Support.HeightMm,
            descriptor.InPlanePerpendicular * (Support.FirstDerivativeMmPerTurn / 2));
        FaceCoordinateMm = Support.TangentOffsetMm - ExactPiLength.FromCanonical(descriptor.GuideOffsetMm, 0);
        foreach (var x in new[] { FollowerReferencePointMm.X, FollowerReferencePointMm.Y, FollowerReferencePointMm.Z }) MechanicalDerivedNumbers.Check(x);
        RecipeId = HashText(CanonicalRepresentation);
    }
    public CamFollowerMotionDescriptor Descriptor { get; } public ExactQuantity RootTurns { get; }
    public Rational SourceTurns { get; } public Rational PhysicalPhaseTurns { get; } public CamSupportSample Support { get; }
    public Rational ContactParameterTurns => Support.ReducedTurns;
    public ExactQuantity GuidePosition { get; } public ExactQuantity TerminalPosition { get; }
    public ExactQuantity ExactFollowerPosition => GuidePosition;
    public ExactVector3 FollowerReferencePointMm { get; } public ExactPiVector3 ContactPointMm { get; }
    public ExactPiLength FaceCoordinateMm { get; } public string RecipeId { get; }
    public string CanonicalRepresentation => Pack(Descriptor.DescriptorId, CamFollowerProfile.Q(RootTurns), F(SourceTurns), F(PhysicalPhaseTurns),
        Support.CanonicalRepresentation, CamFollowerProfile.Q(GuidePosition), CamFollowerProfile.Q(TerminalPosition), V(FollowerReferencePointMm),
        CamFollowerProfile.PiVector(ContactPointMm), CamFollowerProfile.Pi(FaceCoordinateMm));
}

public sealed class CamFollowerAnalysis
{
    internal CamFollowerAnalysis(CamFollowerDraft draft, MechanicalAnalysis source, CamFollowerCompatibilityResult local,
        CamGeometryProofRequest proofRequest, MechanicalDeterminacy determinacy, ExactAffineRelation? retained,
        CamFollowerMotionDescriptor? descriptor, MechanicalAxisVerdict guide, MechanicalAxisVerdict face, MechanicalAxisVerdict target,
        bool declaredReachable, IEnumerable<string> path, IEnumerable<string> declaredPath, IEnumerable<string> blocked,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Draft = draft; SourceAnalysis = source; LocalCompatibility = local; ProofRequest = proofRequest; Determinacy = determinacy;
        SourceRelation = retained; Descriptor = descriptor; GuideTravelCoverage = guide; FaceCoverage = face; Target = target;
        IsDeclaredReachable = declaredReachable; ConstraintPath = path.ToList().AsReadOnly(); DeclaredConstraintPath = declaredPath.ToList().AsReadOnly();
        BlockedPrerequisites = blocked.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList().AsReadOnly();
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(DraftId, Policy, proofRequest.CanonicalRepresentation));
    }
    public CamFollowerDraft Draft { get; } public string DefinitionId => Draft.DefinitionId; public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision; public string AnalysisId { get; } public string Policy => CamFollowerProfile.AnalysisPolicy;
    public string MotionDomain => CamFollowerProfile.MotionDomain; public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; } public CamFollowerCompatibilityResult LocalCompatibility { get; }
    public CamGeometryProofRequest ProofRequest { get; } public CamGeometryProofResult GeometryProof => LocalCompatibility.GeometryProof;
    public MechanicalDeterminacy Determinacy { get; } public ExactAffineRelation? SourceRelation { get; }
    public CamFollowerMotionDescriptor? Descriptor { get; } public CamFollowerEnvelope? Envelope => LocalCompatibility.Envelope;
    public bool HasDeterminedMotion => Descriptor is not null && LocalCompatibility.IsAdmitted && Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput;
    public bool HasDeterminedCamMotion => SourceRelation.HasValue && LocalCompatibility.HasValidCamMounting;
    public MechanicalAxisVerdict GuideTravelCoverage { get; } public MechanicalAxisVerdict FaceCoverage { get; } public MechanicalAxisVerdict Target { get; }
    public bool IsDeclaredReachable { get; } public bool IsAdmittedReachable => HasDeterminedMotion;
    public ReadOnlyCollection<string> ConstraintPath { get; } public ReadOnlyCollection<string> DeclaredConstraintPath { get; }
    public ReadOnlyCollection<string> BlockedPrerequisites { get; } public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && HasDeterminedMotion && GuideTravelCoverage == MechanicalAxisVerdict.Pass &&
        FaceCoverage == MechanicalAxisVerdict.Pass && Target == MechanicalAxisVerdict.Pass && Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
    public CamFollowerPoseRecipe? CreatePoseRecipe(ExactQuantity root) => Descriptor?.At(root);
}

public sealed class CamFollowerCamEvaluation
{
    internal CamFollowerCamEvaluation(string bodyId, ExactVector3 center, ExactVector3 axis, ExactVector3 zeroRay, Rational source, Rational? phi)
    { BodyId = bodyId; CenterMm = center; PositiveAxis = axis; ZeroRay = zeroRay; SourceTurns = source; PhysicalPhaseTurns = phi; }
    public string BodyId { get; } public ExactVector3 CenterMm { get; } public ExactVector3 PositiveAxis { get; } public ExactVector3 ZeroRay { get; }
    public Rational SourceTurns { get; } public Rational? PhysicalPhaseTurns { get; }
}
public enum CamFollowerEvaluationStatus
{
    Success, UndrivenLinearCoordinate, InvalidDefinition, DimensionMismatch, GeometryProofIncomplete, ConvexityRefuted,
    OutOfFollowerFace, OutOfGuideTravel, FaceDecisionUnresolved, IncompleteNumericBudget, NumericResourceLimit, InvalidNumericRequest,
    InvalidProofRequest, GeometryResourceLimit
}
public enum CamFollowerPointDecision { InRange, OutOfRange, Unresolved, NotAssessed }

/// <summary>Only the top-level evaluation can admit this pose. Contact numerics alone do not.</summary>
public sealed class CamFollowerPose
{
    internal CamFollowerPose(CamFollowerPoseRecipe recipe, CamVectorInterval contact, CamVectorInterval materialZero)
    { Recipe = recipe; ContactPointMm = contact; MaterialZeroPointMm = materialZero; }
    public CamFollowerPoseRecipe Recipe { get; } public ExactVector3 FollowerReferencePointMm => Recipe.FollowerReferencePointMm;
    public CamVectorInterval ContactPointMm { get; } public CamVectorInterval MaterialZeroPointMm { get; }
}
public sealed class CamFollowerEvaluation
{
    internal CamFollowerEvaluation(CamFollowerAnalysis analysis, ExactQuantity root, CamNumericRequest request, CamFollowerEvaluationStatus status,
        CamFollowerPointDecision face, CamFollowerPointDecision travel, IEnumerable<OrientedShaftEvaluation> rotary, CamFollowerCamEvaluation? cam,
        CamFollowerPoseRecipe? recipe, CamNumericResult? contactNumeric, CamNumericResult? materialNumeric,
        CamPiComparisonResult? faceLowerComparison, CamPiComparisonResult? faceUpperComparison, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysis.AnalysisId; DefinitionId = analysis.DefinitionId; RootTurns = root; Request = request; Status = status;
        FaceDecision = face; TravelDecision = travel; Rotary = rotary.OrderBy(s => s.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Cam = cam; Recipe = recipe; ContactNumeric = contactNumeric; MaterialNumeric = materialNumeric; Diagnostics = diagnostics.ToList().AsReadOnly();
        FaceLowerComparison = faceLowerComparison; FaceUpperComparison = faceUpperComparison;
        NumericWork = checked((faceLowerComparison?.Work ?? 0) + (faceUpperComparison?.Work ?? 0) + (contactNumeric?.Work ?? 0) + (materialNumeric?.Work ?? 0));
        if (request.MaximumWork >= 0 && NumericWork > request.MaximumWork) throw new InvalidOperationException("Cam evaluation exceeded its aggregate numerical work budget.");
        var contact = contactNumeric?.Point ?? contactNumeric?.DiagnosticPoint; var material = materialNumeric?.Point ?? materialNumeric?.DiagnosticPoint;
        var p = recipe is not null && contact is not null && material is not null ? new CamFollowerPose(recipe, contact, material) : null;
        Pose = IsSuccess ? p : null; DiagnosticPose = IsSuccess ? null : p;
        EvaluationId = HashText(Pack(AnalysisId, CamFollowerProfile.Q(root), request.CanonicalRepresentation, status.ToString(), face.ToString(), travel.ToString(),
            recipe?.RecipeId ?? "", faceLowerComparison?.CanonicalRepresentation ?? "", faceUpperComparison?.CanonicalRepresentation ?? "",
            contactNumeric?.CanonicalRepresentation ?? "", materialNumeric?.CanonicalRepresentation ?? "", N(NumericWork)));
    }
    public string AnalysisId { get; } public string DefinitionId { get; } public string EvaluationId { get; }
    public ExactQuantity RootTurns { get; } public CamNumericRequest Request { get; } public CamFollowerEvaluationStatus Status { get; }
    public CamFollowerPointDecision FaceDecision { get; } public CamFollowerPointDecision TravelDecision { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; } public CamFollowerCamEvaluation? Cam { get; }
    public CamFollowerPoseRecipe? Recipe { get; } public ExactQuantity? ExactFollowerPosition => Recipe?.GuidePosition;
    public CamNumericResult? ContactNumeric { get; } public CamNumericResult? MaterialNumeric { get; }
    public CamPiComparisonResult? FaceLowerComparison { get; } public CamPiComparisonResult? FaceUpperComparison { get; }
    /// <summary>Total work of both face comparisons and both point evaluations under the caller's single budget.</summary>
    public int NumericWork { get; }
    public CamFollowerPose? Pose { get; } public CamFollowerPose? DiagnosticPose { get; }
    public bool IsSuccess => Status == CamFollowerEvaluationStatus.Success;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
