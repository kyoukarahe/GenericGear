using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class WormDriveCompatibilityResult
{
    internal WormDriveCompatibilityResult(string deviceId, ConnectionCompatibilityVerdict verdict, bool present,
        ExactVector3? inputAxis, ExactVector3? outputAxis, Rational? epsilon, Rational? sigma,
        Rational? sourcePortSign, Rational? terminalSign, Rational? prospectiveTransfer, Rational? admittedTransfer,
        WormDriveGeometryDescriptor? geometry, IEnumerable<OrientedDomainCheck> checks,
        IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = deviceId; Verdict = verdict; TransmissionPresent = present; InputPositiveAxis = inputAxis; OutputPositiveAxis = outputAxis;
        Epsilon = epsilon; Sigma = sigma; SourcePortCoordinateSign = sourcePortSign; TerminalCoordinateSign = terminalSign;
        ProspectiveTransfer = prospectiveTransfer; AdmittedTransfer = admittedTransfer; Geometry = geometry;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; }
    public string Scope => "LocalIdealCylindricalWormOnly";
    public ConnectionCompatibilityVerdict Verdict { get; }
    public bool TransmissionPresent { get; }
    public bool IsAdmitted => TransmissionPresent && Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile && AdmittedTransfer.HasValue;
    public ExactVector3? InputPositiveAxis { get; }
    public ExactVector3? OutputPositiveAxis { get; }
    public Rational? Epsilon { get; }
    public Rational? Sigma { get; }
    /// <summary>Original port-coordinate evidence only. Never multiply the retained shaft relation by this sign.</summary>
    public Rational? SourcePortCoordinateSign { get; }
    public Rational? TerminalCoordinateSign { get; }
    /// <summary>Unadmitted local starts/teeth candidate; incompatible specimens, geometry or references do not gain a motion edge.</summary>
    public Rational? ProspectiveTransfer { get; }
    public Rational? AdmittedTransfer { get; }
    public WormDriveGeometryDescriptor? Geometry { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class WormDriveAnalysis
{
    internal WormDriveAnalysis(WormDriveDraft draft, MechanicalAnalysis source, WormDriveCompatibilityResult local,
        IEnumerable<QuantityDof> nodes, IEnumerable<QuantityAffineCoupling> edges, IEnumerable<AffineComponentAnalysis> components,
        MechanicalOutputAnalysis output, ExactAffineRelation? retainedInputRelation, IEnumerable<OrientedDomainCheck> checks,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Draft = draft; SourceAnalysis = source; LocalCompatibility = local; Output = output; RetainedInputRelation = retainedInputRelation;
        Nodes = nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        AdmittedEdges = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList().AsReadOnly(); AdmittedComponents = components.ToList().AsReadOnly();
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => string.Join("|", d.Related.Select(r => r.Key)), StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(draft.DraftId, Policy));
    }
    public WormDriveDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision;
    public string AnalysisId { get; }
    public string Policy => WormDriveProfile.AnalysisPolicy;
    public string MotionDomain => WormDriveProfile.MotionDomain;
    public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; }
    public WormDriveCompatibilityResult LocalCompatibility { get; }
    public ReadOnlyCollection<QuantityDof> Nodes { get; }
    public ReadOnlyCollection<QuantityAffineCoupling> AdmittedEdges { get; }
    public ReadOnlyCollection<AffineComponentAnalysis> AdmittedComponents { get; }
    public MechanicalOutputAnalysis Output { get; }
    public ExactAffineRelation? RetainedInputRelation { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public MechanicalAxisVerdict Geometry => LocalCompatibility.Geometry?.HasExactProof == true &&
        LocalCompatibility.Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && LocalCompatibility.IsAdmitted && Output.HasDeterminedMotion &&
        Output.Target == MechanicalAxisVerdict.Pass && AdmittedComponents.All(c => c.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) &&
        Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}

public enum WormDriveEvaluationStatus { Success, UndrivenOutput, InvalidDefinition, DimensionMismatch }

public sealed class WormDriveOutputEvaluation
{
    internal WormDriveOutputEvaluation(string outputKey, string shaftId, string bodyId, string terminalId,
        Rational input, Rational shaft, Rational terminal, ExactVector3 axis, ExactVector3 center,
        Rational shaftGain, Rational terminalGain, Rational wormPhaseIndex, Rational wheelPhaseIndex)
    {
        OutputKey = outputKey; ShaftId = shaftId; WheelBodyId = bodyId; TerminalId = terminalId;
        InputWormTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, input);
        ShaftTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, shaft);
        TerminalTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, terminal);
        ShaftPositiveAxis = axis; WheelCenterMm = center; ShaftGain = shaftGain; TerminalGain = terminalGain;
        WormPhaseIndex = wormPhaseIndex; WheelPhaseIndex = wheelPhaseIndex;
    }
    public string OutputKey { get; }
    public string ShaftId { get; }
    public string WheelBodyId { get; }
    public string TerminalId { get; }
    public ExactQuantity InputWormTurns { get; }
    public ExactQuantity ShaftTurns { get; }
    public ExactQuantity TerminalTurns { get; }
    public ExactVector3 ShaftPositiveAxis { get; }
    public ExactVector3 WheelCenterMm { get; }
    public Rational ShaftGain { get; }
    public Rational TerminalGain { get; }
    public Rational WormPhaseIndex { get; }
    public Rational WheelPhaseIndex { get; }
    public bool PhaseConstraintSatisfied => WormPhaseIndex == WheelPhaseIndex;
}

public sealed class WormDriveEvaluation
{
    internal WormDriveEvaluation(string analysisId, WormDriveEvaluationStatus status, ExactQuantity input,
        IEnumerable<OrientedShaftEvaluation> rotary, WormDriveOutputEvaluation? output, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysisId; Status = status; Input = input; Rotary = rotary.OrderBy(r => r.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Output = output; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string AnalysisId { get; }
    public WormDriveEvaluationStatus Status { get; }
    public ExactQuantity Input { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; }
    /// <summary>Scoped admitted motion only, not whole-source, target, flank, physics or export approval.</summary>
    public WormDriveOutputEvaluation? Output { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
