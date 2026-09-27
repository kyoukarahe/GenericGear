using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class CrankSliderCompatibilityResult
{
    internal CrankSliderCompatibilityResult(string deviceId, ConnectionCompatibilityVerdict verdict, bool transmissionPresent,
        bool crankMountingValid, OrientedFrame? mappedFrame, Rational? gamma, Rational? epsilon,
        CrankSliderEnvelope? envelope, IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalExactFact> facts,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = deviceId; Verdict = verdict; TransmissionPresent = transmissionPresent; HasValidCrankMounting = crankMountingValid;
        MappedShaftFrameMm = mappedFrame; GammaTurns = gamma; Epsilon = epsilon; Envelope = envelope;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; } public string Scope => "PlanarCrankSliderLocalClosure";
    public ConnectionCompatibilityVerdict Verdict { get; } public bool TransmissionPresent { get; }
    public bool IsAdmitted => Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile && TransmissionPresent;
    public bool HasValidCrankMounting { get; } public OrientedFrame? MappedShaftFrameMm { get; }
    public Rational? GammaTurns { get; } public Rational? Epsilon { get; } public CrankSliderEnvelope? Envelope { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; } public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class CrankSliderAnalysis
{
    internal CrankSliderAnalysis(CrankSliderDraft draft, MechanicalAnalysis source, CrankSliderCompatibilityResult local,
        MechanicalDeterminacy determinacy, ExactAffineRelation? retained, CrankSliderMotionDescriptor? descriptor,
        MechanicalAxisVerdict guideCoverage, MechanicalAxisVerdict target, bool declaredReachable, bool admittedReachable,
        IEnumerable<string> path, IEnumerable<string> declaredPath, IEnumerable<string> blocked,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Draft = draft; SourceAnalysis = source; LocalCompatibility = local; Determinacy = determinacy; SourceRelation = retained;
        Descriptor = descriptor; GuideTravelCoverage = guideCoverage; Target = target;
        IsDeclaredReachable = declaredReachable; IsAdmittedReachable = admittedReachable;
        ConstraintPath = path.ToList().AsReadOnly(); DeclaredConstraintPath = declaredPath.ToList().AsReadOnly();
        BlockedPrerequisites = blocked.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(DraftId, Policy));
    }
    public CrankSliderDraft Draft { get; } public string DefinitionId => Draft.DefinitionId; public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision; public string AnalysisId { get; } public string Policy => CrankSliderProfile.AnalysisPolicy;
    public string MotionDomain => CrankSliderProfile.MotionDomain; public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; } public CrankSliderCompatibilityResult LocalCompatibility { get; }
    public MechanicalDeterminacy Determinacy { get; } public ExactAffineRelation? SourceRelation { get; }
    public CrankSliderMotionDescriptor? Descriptor { get; } public CrankSliderEnvelope? Envelope => LocalCompatibility.Envelope;
    public bool HasDeterminedMotion => Descriptor is not null && Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput;
    public bool HasDeterminedCrankMotion => SourceRelation.HasValue && LocalCompatibility.HasValidCrankMounting;
    public MechanicalAxisVerdict GuideTravelCoverage { get; } public MechanicalAxisVerdict Target { get; }
    public bool IsDeclaredReachable { get; } public bool IsAdmittedReachable { get; }
    public ReadOnlyCollection<string> ConstraintPath { get; } public ReadOnlyCollection<string> DeclaredConstraintPath { get; }
    public ReadOnlyCollection<string> BlockedPrerequisites { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; } public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && HasDeterminedMotion && LocalCompatibility.IsAdmitted &&
        GuideTravelCoverage == MechanicalAxisVerdict.Pass && Target == MechanicalAxisVerdict.Pass && Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
    public CrankSliderPoseRecipe? CreatePoseRecipe(ExactQuantity root) => Descriptor?.At(root);
}

/// <summary>Independent retained crank state, available even with its rod transmission removed.</summary>
public sealed class CrankSliderCrankEvaluation
{
    internal CrankSliderCrankEvaluation(string bodyId, ExactVector3 pivot, ExactVector3 axis, ExactVector3 zeroRay,
        Rational sourceTurns, Rational mountingTurns, Rational? physicalPhase)
    {
        BodyId = bodyId; PivotMm = pivot; PositiveAxis = axis; ZeroRay = zeroRay; SourceTurns = sourceTurns;
        MountingTurns = mountingTurns; PhysicalPhaseTurns = physicalPhase;
    }
    public string BodyId { get; } public ExactVector3 PivotMm { get; } public ExactVector3 PositiveAxis { get; }
    public ExactVector3 ZeroRay { get; } public Rational SourceTurns { get; } public Rational MountingTurns { get; }
    public Rational? PhysicalPhaseTurns { get; }
}

public enum CrankSliderEvaluationStatus
{
    Success, UndrivenLinearCoordinate, InvalidDefinition, DimensionMismatch, OutOfGuideTravel, TravelDecisionUnresolved,
    IncompleteNumericBudget, NumericResourceLimit, InvalidNumericRequest
}
public enum CrankSliderTravelDecision { InRange, OutOfRange, Unresolved, NotAssessed }

public sealed class CrankSliderEvaluation
{
    internal CrankSliderEvaluation(CrankSliderAnalysis analysis, ExactQuantity root, CrankSliderNumericRequest request,
        CrankSliderEvaluationStatus status, CrankSliderTravelDecision travel, IEnumerable<OrientedShaftEvaluation> rotary,
        CrankSliderCrankEvaluation? crank, CrankSliderPoseRecipe? recipe, CrankSliderNumericComputation? numeric,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysis.AnalysisId; DefinitionId = analysis.DefinitionId; RootTurns = root; Request = request; Status = status;
        TravelDecision = travel; Rotary = rotary.OrderBy(r => r.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Crank = crank; Recipe = recipe; NumericComputation = numeric; Diagnostics = diagnostics.ToList().AsReadOnly();
        EvaluationId = HashText(Pack(AnalysisId, CrankSliderProfile.Q(root), request.CanonicalRepresentation, Status.ToString(),
            travel.ToString(), recipe?.RecipeId ?? "", numeric?.CanonicalRepresentation ?? ""));
    }
    public string AnalysisId { get; } public string DefinitionId { get; } public string EvaluationId { get; }
    public ExactQuantity RootTurns { get; } public CrankSliderNumericRequest Request { get; }
    public CrankSliderEvaluationStatus Status { get; } public CrankSliderTravelDecision TravelDecision { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; } public CrankSliderCrankEvaluation? Crank { get; }
    public CrankSliderPoseRecipe? Recipe { get; }
    /// <summary>Quantity enclosures only. Numerical availability does not assert admission by guide travel.</summary>
    public CrankSliderNumericComputation? NumericComputation { get; }
    /// <summary>Only a numerically available, proved in-range frame is a normal pose.</summary>
    public CrankSliderNumericPose? Pose => IsSuccess ? NumericComputation?.Pose : null;
    public CrankSliderNumericPose? DiagnosticPose => IsSuccess ? null : NumericComputation?.Pose ?? NumericComputation?.DiagnosticPose;
    public bool IsSuccess => Status == CrankSliderEvaluationStatus.Success;
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
