using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum PitchChainLinkVerdict
{
    ExactLinkCountMatch, TooFewLinksForDeclaredRoute, TooManyLinksForDeclaredRoute,
    SelectedChainPitchMismatch, UnsupportedLinkTopology, InvalidSpecification, GeometryUnavailable
}

public sealed class PitchChainLinkCompatibility
{
    internal PitchChainLinkCompatibility(PitchChainLinkVerdict verdict, PitchChainSpecification? selected,
        PitchChainGeometryDescriptor? geometry, IEnumerable<MechanicalExactFact> facts, string detail)
    {
        Verdict = verdict; SelectedSpecificationId = selected?.SpecificationId; RequiredLinkCount = geometry?.RequiredLinkCount;
        SelectedLinkCount = selected?.LinkCount; RequiredPitchMm = geometry?.Pitch.PitchMm; SelectedPitch = selected?.Pitch;
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly(); Detail = detail;
        CompatibilityId = HashText(Pack(verdict.ToString(), SelectedSpecificationId ?? "", geometry?.GeometryId ?? "", detail));
    }
    public string CompatibilityId { get; }
    public PitchChainLinkVerdict Verdict { get; }
    public string? SelectedSpecificationId { get; }
    public int? RequiredLinkCount { get; }
    public int? SelectedLinkCount { get; }
    public Rational? RequiredPitchMm { get; }
    public ExactQuantity? SelectedPitch { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public string Detail { get; }
}

public sealed class PitchChainCompatibilityResult
{
    internal PitchChainCompatibilityResult(string device, ConnectionCompatibilityVerdict verdict, bool present,
        ExactVector3? inputCenter, ExactVector3? inputAxis, ExactVector3? outputAxis,
        Rational? epsilon1, Rational? epsilon2, Rational? portSign, Rational? terminalSign,
        Rational? prospective, Rational? admitted, PitchChainGeometryDescriptor? geometry,
        PitchChainPhaseRegistration? phase, PitchChainLinkCompatibility links,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = device; Verdict = verdict; TransmissionPresent = present; InputCenterMm = inputCenter;
        InputPositiveAxis = inputAxis; OutputPositiveAxis = outputAxis; InputAxisRouteSign = epsilon1; OutputAxisRouteSign = epsilon2;
        SourcePortCoordinateSign = portSign; TerminalCoordinateSign = terminalSign; ProspectiveTransfer = prospective; AdmittedTransfer = admitted;
        Geometry = geometry; PhaseRegistration = phase; LinkCompatibility = links;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; }
    public string Scope => "LocalSynchronizedEqualSprocketChainOnly";
    public ConnectionCompatibilityVerdict Verdict { get; }
    public bool TransmissionPresent { get; }
    public bool IsAdmitted => TransmissionPresent && Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile && AdmittedTransfer.HasValue;
    public ExactVector3? InputCenterMm { get; }
    public ExactVector3? InputPositiveAxis { get; }
    public ExactVector3? OutputPositiveAxis { get; }
    public Rational? InputAxisRouteSign { get; }
    public Rational? OutputAxisRouteSign { get; }
    public Rational? SourcePortCoordinateSign { get; }
    public Rational? TerminalCoordinateSign { get; }
    /// <summary>Axes-only diagnostic, never a motion edge for an invalid phase, pitch or selected chain.</summary>
    public Rational? ProspectiveTransfer { get; }
    public Rational? AdmittedTransfer { get; }
    public PitchChainGeometryDescriptor? Geometry { get; }
    public PitchChainPhaseRegistration? PhaseRegistration { get; }
    public PitchChainLinkCompatibility LinkCompatibility { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class PitchChainAnalysis
{
    internal PitchChainAnalysis(PitchChainDraft draft, MechanicalAnalysis source, PitchChainCompatibilityResult local,
        IEnumerable<QuantityDof> nodes, IEnumerable<QuantityAffineCoupling> edges, IEnumerable<AffineComponentAnalysis> components,
        MechanicalOutputAnalysis output, ExactAffineRelation? retainedInputRelation,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Draft = draft; SourceAnalysis = source; LocalCompatibility = local; Output = output; RetainedInputRelation = retainedInputRelation;
        Nodes = nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        AdmittedEdges = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList().AsReadOnly(); AdmittedComponents = components.ToList().AsReadOnly();
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => string.Join("|", d.Related.Select(r => r.Key)), StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(draft.DraftId, Policy));
    }
    public PitchChainDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision;
    public string AnalysisId { get; }
    public string Policy => PitchChainProfile.AnalysisPolicy;
    public string MotionDomain => PitchChainProfile.MotionDomain;
    public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; }
    public PitchChainCompatibilityResult LocalCompatibility { get; }
    public ReadOnlyCollection<QuantityDof> Nodes { get; }
    public ReadOnlyCollection<QuantityAffineCoupling> AdmittedEdges { get; }
    public ReadOnlyCollection<AffineComponentAnalysis> AdmittedComponents { get; }
    public MechanicalOutputAnalysis Output { get; }
    public ExactAffineRelation? RetainedInputRelation { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public int RigidRotatingBodyCount => Draft.Definition.Source.Definition.Bodies.Count + 2;
    public int ShaftDofCount => Nodes.Count;
    public int RequiredPinCount => LocalCompatibility.Geometry?.RequiredLinkCount ?? 0;
    public int SelectedPinCount => Draft.Definition.Device.SelectedChain?.LinkCount ?? 0;
    public int AdmittedPinCount => LocalCompatibility.IsAdmitted ? LocalCompatibility.Geometry!.RequiredLinkCount : 0;
    public int AdmittedIdealLinkCount => AdmittedPinCount;
    public int SourceContactCount => SourceAnalysis.Edges.Count;
    public int ChainTransmissionConstraintCount => LocalCompatibility.IsAdmitted ? 1 : 0;
    public MechanicalAxisVerdict Geometry => LocalCompatibility.Geometry?.HasExactProof == true &&
        LocalCompatibility.Checks.Where(c => c.Domain != "SelectedLinkCount" && c.Domain != "SupportedPhaseRegistration")
            .All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && LocalCompatibility.IsAdmitted && Output.HasDeterminedMotion &&
        Output.Target == MechanicalAxisVerdict.Pass && AdmittedComponents.All(c => c.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) &&
        Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}

public enum PitchChainEvaluationStatus { Success, UndrivenOutput, InvalidDefinition, DimensionMismatch }

public sealed class PitchChainOutputEvaluation
{
    internal PitchChainOutputEvaluation(string output, string shaft, string body, string terminal,
        Rational inputTurns, Rational shaftTurns, Rational terminalTurns, ExactVector3 axis,
        ExactVector3 center, Rational shaftGain, Rational terminalGain)
    {
        OutputKey = output; ShaftId = shaft; SprocketBodyId = body; TerminalId = terminal;
        InputSprocketTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, inputTurns);
        ShaftTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, shaftTurns);
        TerminalTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, terminalTurns);
        ShaftPositiveAxis = axis; SprocketCenterMm = center; ShaftGain = shaftGain; TerminalGain = terminalGain;
    }
    public string OutputKey { get; }
    public string ShaftId { get; }
    public string SprocketBodyId { get; }
    public string TerminalId { get; }
    public ExactQuantity InputSprocketTurns { get; }
    public ExactQuantity ShaftTurns { get; }
    public ExactQuantity TerminalTurns { get; }
    public ExactVector3 ShaftPositiveAxis { get; }
    public ExactVector3 SprocketCenterMm { get; }
    public Rational ShaftGain { get; }
    public Rational TerminalGain { get; }
}

public sealed class PitchChainEvaluation
{
    internal PitchChainEvaluation(string analysisId, PitchChainEvaluationStatus status, ExactQuantity input,
        IEnumerable<OrientedShaftEvaluation> rotary, PitchChainOutputEvaluation? output, IndexedChainPoseDescriptor? pose,
        IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysisId; Status = status; Input = input;
        Rotary = rotary.OrderBy(r => r.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Output = output; ChainPose = pose; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string AnalysisId { get; }
    public PitchChainEvaluationStatus Status { get; }
    public ExactQuantity Input { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; }
    /// <summary>Scoped motion may exist despite a failed requested target; removed/invalid transmission has no output.</summary>
    public PitchChainOutputEvaluation? Output { get; }
    /// <summary>All persistent pin/link recipes, absent when the added transmission is invalid or undriven.</summary>
    public IndexedChainPoseDescriptor? ChainPose { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
