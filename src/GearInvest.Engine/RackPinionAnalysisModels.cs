using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class RackPinionCompatibilityResult
{
    internal RackPinionCompatibilityResult(string deviceId, ConnectionCompatibilityVerdict verdict, bool transmissionPresent,
        ExactVector3? pinionPositiveAxis, Rational? contactSign, Rational? sourcePortCoordinateSign,
        ExactQuantity? localGain, ExactPiLength pitchRadius, ExactPiVector3? fixedContactPointMm,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = deviceId; Verdict = verdict; TransmissionPresent = transmissionPresent; PinionPositiveAxis = pinionPositiveAxis;
        ContactSign = contactSign; SourcePortCoordinateSign = sourcePortCoordinateSign; LocalGain = localGain;
        PitchRadius = pitchRadius; FixedContactPointMm = fixedContactPointMm;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; }
    public string Scope => "LocalCircularPitchRackConnectionOnly";
    public ConnectionCompatibilityVerdict Verdict { get; }
    public bool TransmissionPresent { get; }
    public bool IsAdmitted => TransmissionPresent && Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile;
    public ExactVector3? PinionPositiveAxis { get; }
    public Rational? ContactSign { get; }
    /// <summary>Original mating-port evidence only; retained shaft turns already own their coordinate sign.</summary>
    public Rational? SourcePortCoordinateSign { get; }
    /// <summary>Guide mm per retained pinion turn, (a cross b) dot g times Z times circular pitch.</summary>
    public ExactQuantity? LocalGain { get; }
    /// <summary>Algebraic descriptor from the declaration, not independent input or admission of invalid dimensions/pitch.</summary>
    public ExactPiLength PitchRadius { get; }
    public ExactPiVector3? FixedContactPointMm { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

/// <summary>Neutral scalar and world observation of one determined or undetermined prismatic coordinate.</summary>
public sealed class PrismaticOutputAnalysis
{
    internal PrismaticOutputAnalysis(PrismaticOutputDefinition binding, MechanicalDeterminacy determinacy,
        ExactAffineRelation? sourceRelation, DimensionedAffineRelation? guideRelation, DimensionedAffineRelation? terminalRelation,
        ExactVector3? sourceAxis, ExactVector3 guideAxis, ExactVector3? worldGain, ExactPiVector3? worldOffset,
        ExactQuantityInterval? validRootInterval, MechanicalAxisVerdict requiredRange, MechanicalAxisVerdict target,
        bool declaredReachable, bool admittedReachable, IEnumerable<string> path, IEnumerable<string> declaredPath,
        IEnumerable<string> blocked, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Binding = binding; Determinacy = determinacy; SourceRelation = sourceRelation; GuideRelation = guideRelation;
        TerminalRelation = terminalRelation; SourcePositiveAxis = sourceAxis; GuidePositiveAxis = guideAxis;
        WorldGainMmPerRootTurn = worldGain; WorldOffsetMm = worldOffset; ValidRootInterval = validRootInterval;
        RequiredRange = requiredRange; Target = target; IsDeclaredReachable = declaredReachable; IsAdmittedReachable = admittedReachable;
        ConstraintPath = path.ToList().AsReadOnly(); DeclaredConstraintPath = declaredPath.ToList().AsReadOnly();
        BlockedPrerequisites = blocked.OrderBy(s => s, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public PrismaticOutputDefinition Binding { get; }
    public string OutputKey => Binding.Key;
    public MechanicalDeterminacy Determinacy { get; }
    public bool HasDeterminedMotion => Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput && GuideRelation.HasValue && TerminalRelation.HasValue;
    public ExactAffineRelation? SourceRelation { get; }
    public DimensionedAffineRelation? GuideRelation { get; }
    public DimensionedAffineRelation? TerminalRelation { get; }
    public ExactVector3? SourcePositiveAxis { get; }
    public ExactVector3 GuidePositiveAxis { get; }
    public ExactVector3? WorldGainMmPerRootTurn { get; }
    public ExactPiVector3? WorldOffsetMm { get; }
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

public sealed class RackPinionAnalysis
{
    internal RackPinionAnalysis(RackPinionDraft draft, MechanicalAnalysis source, RackPinionCompatibilityResult local,
        IEnumerable<QuantityDof> nodes, IEnumerable<QuantityAffineCoupling> edges,
        IEnumerable<AffineComponentAnalysis> components, PrismaticOutputAnalysis linear,
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
    public RackPinionDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision;
    public string AnalysisId { get; }
    public string Policy => RackPinionProfile.AnalysisPolicy;
    public string MotionDomain => RackPinionProfile.MotionDomain;
    public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; }
    public RackPinionCompatibilityResult LocalCompatibility { get; }
    public ReadOnlyCollection<QuantityDof> Nodes { get; }
    public ReadOnlyCollection<QuantityAffineCoupling> AdmittedEdges { get; }
    public ReadOnlyCollection<AffineComponentAnalysis> AdmittedComponents { get; }
    public PrismaticOutputAnalysis LinearOutput { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && LocalCompatibility.IsAdmitted &&
        LinearOutput.HasDeterminedMotion && LinearOutput.ValidRootInterval is not null &&
        LinearOutput.RequiredRange == MechanicalAxisVerdict.Pass && LinearOutput.Target == MechanicalAxisVerdict.Pass &&
        Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}

public enum RackPinionEvaluationStatus { Success, UndrivenLinearCoordinate, InvalidDefinition, DimensionMismatch, OutOfOperatingRange }

public sealed class RackPositionEvaluation
{
    internal RackPositionEvaluation(PrismaticOutputDefinition output, ExactQuantity pinionTurns, ExactQuantity guide, ExactQuantity terminal,
        ExactPiVector3 worldPosition, ExactPiFrame orientation, ExactQuantity guideGain, ExactQuantity terminalGain,
        ExactQuantity materialContactCoordinate, ExactPiVector3 fixedContactPointMm)
    {
        OutputKey = output.Key; LinearDofId = output.LinearDofId; RackBodyId = output.BodyId;
        PinionTurns = pinionTurns; GuidePosition = guide; TerminalPosition = terminal; WorldPositionMm = worldPosition;
        RackOrientation = orientation; GuideGain = guideGain; TerminalGain = terminalGain;
        MaterialContactCoordinate = materialContactCoordinate; FixedContactPointMm = fixedContactPointMm;
    }
    public string OutputKey { get; }
    public string LinearDofId { get; }
    public string RackBodyId { get; }
    public ExactQuantity PinionTurns { get; }
    public ExactQuantity GuidePosition { get; }
    public ExactQuantity TerminalPosition { get; }
    public ExactPiVector3 WorldPositionMm { get; }
    public ExactPiFrame RackOrientation { get; }
    public ExactQuantity GuideGain { get; }
    public ExactQuantity TerminalGain { get; }
    public ExactQuantity MaterialContactCoordinate { get; }
    /// <summary>Ground-fixed pitch contact; never rotated with the pinion's material marker.</summary>
    public ExactPiVector3 FixedContactPointMm { get; }
}

public sealed class RackPinionEvaluation
{
    internal RackPinionEvaluation(string analysisId, RackPinionEvaluationStatus status, ExactQuantity input,
        IEnumerable<OrientedShaftEvaluation> rotary, RackPositionEvaluation? linear, RackPositionEvaluation? diagnosticLinear,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysisId; Status = status; Input = input; Rotary = rotary.OrderBy(r => r.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Linear = linear; DiagnosticLinear = diagnosticLinear; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string AnalysisId { get; }
    public RackPinionEvaluationStatus Status { get; }
    public ExactQuantity Input { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; }
    /// <summary>Successful frame only. Invalid, undriven and outside-domain evaluation never returns one.</summary>
    public RackPositionEvaluation? Linear { get; }
    /// <summary>Explicit exact extrapolation outside the operating domain; not an admitted or clamped pose.</summary>
    public RackPositionEvaluation? DiagnosticLinear { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
