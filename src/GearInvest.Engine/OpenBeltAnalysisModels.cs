using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum OpenBeltLengthVerdict { ExactMatch, ProvenTooShort, ProvenTooLong, Inconclusive, InvalidSpecification }

/// <summary>Selected belt length relative to the current required route length, not route or material identity.</summary>
public sealed class OpenBeltLengthCompatibility
{
    internal OpenBeltLengthCompatibility(OpenBeltLengthVerdict verdict, string proofKind, OpenBeltLengthSpecification? selected,
        OpenBeltLengthDescriptor? required, OpenBeltLengthDescriptor? reference, IEnumerable<MechanicalExactFact> facts, string detail)
    {
        Verdict = verdict; ProofKind = proofKind; SelectedSpecificationId = selected?.SpecificationId;
        RequiredLength = required; ReferenceLength = reference; Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly(); Detail = detail;
        CompatibilityId = HashText(Pack(verdict.ToString(), proofKind, SelectedSpecificationId ?? "", required?.CanonicalRepresentation ?? "", reference?.CanonicalRepresentation ?? ""));
    }
    public string CompatibilityId { get; }
    public OpenBeltLengthVerdict Verdict { get; }
    public string ProofKind { get; }
    public string? SelectedSpecificationId { get; }
    public OpenBeltLengthDescriptor? RequiredLength { get; }
    public OpenBeltLengthDescriptor? ReferenceLength { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public string Detail { get; }
}

public sealed class OpenBeltCompatibilityResult
{
    internal OpenBeltCompatibilityResult(string deviceId, ConnectionCompatibilityVerdict verdict, bool present,
        ExactVector3? inputAxis, ExactVector3? outputAxis, Rational? epsilon1, Rational? epsilon2,
        Rational? sourcePortSign, Rational? terminalSign, Rational? prospectiveTransfer, Rational? admittedTransfer,
        OpenBeltRouteDescriptor? route, OpenBeltLengthCompatibility length, IEnumerable<OpenBeltExactProof> velocityProofs,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DeviceId = deviceId; Verdict = verdict; TransmissionPresent = present; InputPositiveAxis = inputAxis; OutputPositiveAxis = outputAxis;
        InputAxisRouteSign = epsilon1; OutputAxisRouteSign = epsilon2; SourcePortCoordinateSign = sourcePortSign;
        TerminalCoordinateSign = terminalSign; ProspectiveTransfer = prospectiveTransfer; AdmittedTransfer = admittedTransfer;
        Route = route; LengthCompatibility = length; ContactVelocityProofs = velocityProofs.OrderBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string DeviceId { get; }
    public string Scope => "LocalTwoPulleyOpenBeltOnly";
    public ConnectionCompatibilityVerdict Verdict { get; }
    public bool TransmissionPresent { get; }
    public bool IsAdmitted => TransmissionPresent && Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile && AdmittedTransfer.HasValue;
    public ExactVector3? InputPositiveAxis { get; }
    public ExactVector3? OutputPositiveAxis { get; }
    public Rational? InputAxisRouteSign { get; }
    public Rational? OutputAxisRouteSign { get; }
    /// <summary>Evidence of the original port coordinate, never a second transfer applied to retained shaft turns.</summary>
    public Rational? SourcePortCoordinateSign { get; }
    public Rational? TerminalCoordinateSign { get; }
    /// <summary>Geometry/axes-only diagnostic. It is not an admitted motion channel when length or another prerequisite fails.</summary>
    public Rational? ProspectiveTransfer { get; }
    public Rational? AdmittedTransfer { get; }
    public OpenBeltRouteDescriptor? Route { get; }
    public OpenBeltLengthCompatibility LengthCompatibility { get; }
    public ReadOnlyCollection<OpenBeltExactProof> ContactVelocityProofs { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class OpenBeltAnalysis
{
    internal OpenBeltAnalysis(OpenBeltDraft draft, MechanicalAnalysis source, OpenBeltCompatibilityResult local,
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
    public OpenBeltDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision;
    public string AnalysisId { get; }
    public string Policy => OpenBeltProfile.AnalysisPolicy;
    public string MotionDomain => OpenBeltProfile.MotionDomain;
    public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; }
    public OpenBeltCompatibilityResult LocalCompatibility { get; }
    public ReadOnlyCollection<QuantityDof> Nodes { get; }
    public ReadOnlyCollection<QuantityAffineCoupling> AdmittedEdges { get; }
    public ReadOnlyCollection<AffineComponentAnalysis> AdmittedComponents { get; }
    public MechanicalOutputAnalysis Output { get; }
    public ExactAffineRelation? RetainedInputRelation { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public MechanicalAxisVerdict Geometry => LocalCompatibility.Route is null ||
        LocalCompatibility.Checks.Any(c => c.Domain != "BeltLengthCompatibility" && c.Required && c.Verdict == OrientedCheckVerdict.Fail)
        ? MechanicalAxisVerdict.Fail : MechanicalAxisVerdict.Pass;
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && LocalCompatibility.IsAdmitted && Output.HasDeterminedMotion &&
        Output.Target == MechanicalAxisVerdict.Pass && AdmittedComponents.All(c => c.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) &&
        Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}

public enum OpenBeltEvaluationStatus { Success, UndrivenOutput, InvalidDefinition, DimensionMismatch }

public sealed class OpenBeltOutputEvaluation
{
    internal OpenBeltOutputEvaluation(string outputKey, string shaftId, string bodyId, string terminalId,
        Rational input, Rational shaft, Rational terminal, ExactVector3 axis, ExactVector3 center,
        Rational shaftGain, Rational terminalGain, Rational travelPiCoefficient)
    {
        OutputKey = outputKey; ShaftId = shaftId; PulleyBodyId = bodyId; TerminalId = terminalId;
        InputPulleyTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, input);
        ShaftTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, shaft);
        TerminalTurns = ExactQuantity.FromCanonical(QuantityKind.AngularPosition, terminal);
        ShaftPositiveAxis = axis; PulleyCenterMm = center; ShaftGain = shaftGain; TerminalGain = terminalGain;
        BeltMaterialTravelPiCoefficientMm = travelPiCoefficient;
    }
    public string OutputKey { get; }
    public string ShaftId { get; }
    public string PulleyBodyId { get; }
    public string TerminalId { get; }
    public ExactQuantity InputPulleyTurns { get; }
    public ExactQuantity ShaftTurns { get; }
    public ExactQuantity TerminalTurns { get; }
    public ExactVector3 ShaftPositiveAxis { get; }
    public ExactVector3 PulleyCenterMm { get; }
    public Rational ShaftGain { get; }
    public Rational TerminalGain { get; }
    /// <summary>Signed clockwise travel is this exact coefficient times pi millimetres; it is not rational mm or modulo pulley turns.</summary>
    public Rational BeltMaterialTravelPiCoefficientMm { get; }
}

public sealed class OpenBeltEvaluation
{
    internal OpenBeltEvaluation(string analysisId, OpenBeltEvaluationStatus status, ExactQuantity input,
        IEnumerable<OrientedShaftEvaluation> rotary, OpenBeltOutputEvaluation? output, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysisId; Status = status; Input = input; Rotary = rotary.OrderBy(r => r.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        Output = output; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string AnalysisId { get; }
    public OpenBeltEvaluationStatus Status { get; }
    public ExactQuantity Input { get; }
    /// <summary>All independently determined retained source shafts, plus the added shaft only when Output is present.</summary>
    public ReadOnlyCollection<OrientedShaftEvaluation> Rotary { get; }
    /// <summary>Scoped admitted output motion, not approval of unrelated source clearance, targets or whole finalization.</summary>
    public OpenBeltOutputEvaluation? Output { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
