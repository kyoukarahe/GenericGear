using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class LeadScrewCompatibilityResult
{
    internal LeadScrewCompatibilityResult(string deviceId, ConnectionCompatibilityVerdict verdict, bool transmissionPresent,
        ExactVector3? screwAxis, Rational? epsilon, Rational? sigma, Rational? sourcePortCoordinateSign,
        ExactQuantity? localGain, IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalExactFact> facts,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = deviceId; Verdict = verdict; TransmissionPresent = transmissionPresent; ScrewPositiveAxis = screwAxis;
        Epsilon = epsilon; Sigma = sigma; SourcePortCoordinateSign = sourcePortCoordinateSign; LocalGain = localGain;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; }
    public string Scope => "LocalLeadScrewConnectionOnly";
    public ConnectionCompatibilityVerdict Verdict { get; }
    public bool TransmissionPresent { get; }
    public bool IsAdmitted => TransmissionPresent && Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile;
    public ExactVector3? ScrewPositiveAxis { get; }
    public Rational? Epsilon { get; }
    public Rational? Sigma { get; }
    /// <summary>Evidence only. The device relation uses retained shaft turns, not this port coordinate.</summary>
    public Rational? SourcePortCoordinateSign { get; }
    /// <summary>Guide mm per retained screw turn, K=-H*L*epsilon*sigma.</summary>
    public ExactQuantity? LocalGain { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class LinearOutputAnalysis
{
    internal LinearOutputAnalysis(LinearOutputDefinition binding, MechanicalDeterminacy determinacy,
        ExactAffineRelation? screwRelation, DimensionedAffineRelation? guideRelation, DimensionedAffineRelation? terminalRelation,
        ExactVector3? screwAxis, ExactVector3 guideAxis, ExactVector3? worldGain, ExactVector3? worldOffset,
        ExactQuantityInterval? validRootInterval, MechanicalAxisVerdict requiredRange, MechanicalAxisVerdict target,
        bool declaredReachable, bool admittedReachable, IEnumerable<string> path, IEnumerable<string> declaredPath,
        IEnumerable<string> blocked, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Binding = binding; Determinacy = determinacy; ScrewRelation = screwRelation; GuideRelation = guideRelation;
        TerminalRelation = terminalRelation; ScrewPositiveAxis = screwAxis; GuidePositiveAxis = guideAxis;
        WorldGainMmPerRootTurn = worldGain; WorldOffsetMm = worldOffset; ValidRootInterval = validRootInterval;
        RequiredRange = requiredRange; Target = target; IsDeclaredReachable = declaredReachable; IsAdmittedReachable = admittedReachable;
        ConstraintPath = path.ToList().AsReadOnly(); DeclaredConstraintPath = declaredPath.ToList().AsReadOnly();
        BlockedPrerequisites = blocked.OrderBy(s => s, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public LinearOutputDefinition Binding { get; }
    public string OutputKey => Binding.Key;
    public MechanicalDeterminacy Determinacy { get; }
    public bool HasDeterminedMotion => Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput && GuideRelation.HasValue && TerminalRelation.HasValue;
    public ExactAffineRelation? ScrewRelation { get; }
    public DimensionedAffineRelation? GuideRelation { get; }
    public DimensionedAffineRelation? TerminalRelation { get; }
    public ExactVector3? ScrewPositiveAxis { get; }
    public ExactVector3 GuidePositiveAxis { get; }
    public ExactVector3? WorldGainMmPerRootTurn { get; }
    public ExactVector3? WorldOffsetMm { get; }
    public ExactQuantityInterval? ValidRootInterval { get; }
    public MechanicalAxisVerdict RequiredRange { get; }
    public MechanicalAxisVerdict Target { get; }
    public bool IsDeclaredReachable { get; }
    public bool IsAdmittedReachable { get; }
    public ReadOnlyCollection<string> ConstraintPath { get; }
    public ReadOnlyCollection<string> DeclaredConstraintPath { get; }
    public ReadOnlyCollection<string> BlockedPrerequisites { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class RotaryLinearAnalysis
{
    internal RotaryLinearAnalysis(RotaryLinearDraft draft, MechanicalAnalysis source, LeadScrewCompatibilityResult local,
        IEnumerable<QuantityDof> nodes, IEnumerable<QuantityAffineCoupling> edges,
        IEnumerable<AffineComponentAnalysis> components, LinearOutputAnalysis linear,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Draft = draft; SourceAnalysis = source; LocalCompatibility = local;
        Nodes = nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        AdmittedEdges = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        AdmittedComponents = components.ToList().AsReadOnly(); LinearOutput = linear;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => string.Join("|", d.Related.Select(r => r.Key)), StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(draft.DraftId, Policy));
    }
    public RotaryLinearDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision;
    public string AnalysisId { get; }
    public string Policy => RotaryLinearProfile.AnalysisPolicy;
    public string MotionDomain => RotaryLinearProfile.MotionDomain;
    public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; }
    public LeadScrewCompatibilityResult LocalCompatibility { get; }
    public ReadOnlyCollection<QuantityDof> Nodes { get; }
    public ReadOnlyCollection<QuantityAffineCoupling> AdmittedEdges { get; }
    public ReadOnlyCollection<AffineComponentAnalysis> AdmittedComponents { get; }
    public LinearOutputAnalysis LinearOutput { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && LocalCompatibility.IsAdmitted &&
        LinearOutput.HasDeterminedMotion && LinearOutput.ValidRootInterval is not null &&
        LinearOutput.RequiredRange == MechanicalAxisVerdict.Pass && LinearOutput.Target == MechanicalAxisVerdict.Pass &&
        Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}

public enum RotaryLinearEvaluationStatus { Success, UndrivenLinearCoordinate, InvalidDefinition, DimensionMismatch, OutOfTravelRange }

public sealed class LinearPositionEvaluation
{
    internal LinearPositionEvaluation(LinearOutputDefinition output, ExactQuantity screwTurns, ExactQuantity guide, ExactQuantity terminal,
        ExactVector3 worldPosition, OrientedFrame orientation, ExactQuantity guideGain, ExactQuantity terminalGain)
    {
        OutputKey = output.Key; LinearDofId = output.LinearDofId; NutBodyId = output.NutBodyId;
        ScrewTurns = screwTurns; GuidePosition = guide; TerminalPosition = terminal; WorldPositionMm = worldPosition;
        NutOrientation = orientation; GuideGain = guideGain; TerminalGain = terminalGain;
    }
    public string OutputKey { get; }
    public string LinearDofId { get; }
    public string NutBodyId { get; }
    public ExactQuantity ScrewTurns { get; }
    public ExactQuantity GuidePosition { get; }
    public ExactQuantity TerminalPosition { get; }
    public ExactVector3 WorldPositionMm { get; }
    public OrientedFrame NutOrientation { get; }
    public ExactQuantity GuideGain { get; }
    public ExactQuantity TerminalGain { get; }
}

public sealed class RotaryLinearEvaluation
{
    internal RotaryLinearEvaluation(string analysisId, RotaryLinearEvaluationStatus status, ExactQuantity input,
        IEnumerable<OrientedShaftEvaluation> rotary, LinearPositionEvaluation? linear, LinearPositionEvaluation? diagnosticLinear,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysisId; Status = status; Input = input; Rotary = rotary.OrderBy(r => r.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Linear = linear; DiagnosticLinear = diagnosticLinear; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string AnalysisId { get; }
    public RotaryLinearEvaluationStatus Status { get; }
    public ExactQuantity Input { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; }
    /// <summary>Successful linear frame only. Never a clamped or cached previous pose.</summary>
    public LinearPositionEvaluation? Linear { get; }
    /// <summary>Explicitly diagnostic extrapolated position outside the declared operating interval.</summary>
    public LinearPositionEvaluation? DiagnosticLinear { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
