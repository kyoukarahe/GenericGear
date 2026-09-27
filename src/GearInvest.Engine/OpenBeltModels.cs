using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class OpenBeltProfile
{
    public const string Id = "cardinal-two-pulley-open-ideal-belt-v1";
    public const string NumericSemantics = "exact-rational-open-belt-construction-v1";
    public const string LengthSemantics = "derived-two-pulley-open-length-v1";
    public const string RoutingKind = "open";
    public const string MotionDomain = MechanicalAuthoringProfile.MotionDomain;
    public const string AnalysisPolicy = "exact-two-pulley-open-belt-v1";
    public const string StandaloneSource = "single-prescribed-rotary-shaft-v1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Q(ExactQuantity q) => Pack(q.Kind.ToString(), q.Unit, F(q.Value));
    internal static string Flag(bool value) => value ? "1" : "0";
    internal static void Quantity(ExactQuantity q) => MechanicalAuthoringProfile.Number(q.Value);
    internal static string Shaft(OrientedShaft s) => Pack(s.Id, Frame(s.Frame), Flag(s.IsPrescribed));
    internal static string Port(ShaftPort p) => Pack(p.Id, p.ShaftId, Frame(p.Frame), F(p.PhaseOffset), p.Kind.ToString());
    internal static string Output(MechanicalOutput o) => Pack(o.Key, o.ShaftId ?? "", o.BodyId ?? "", o.PortId ?? "",
        o.RequiredTransfer.HasValue ? F(o.RequiredTransfer.Value) : "", o.Role?.ToString() ?? "", o.UnresolvedReason ?? "",
        Pack(o.FormerEndpoint.Select(r => Pack(r.Kind, r.Id)).ToArray()));
}

/// <summary>Immutable selected ideal belt length, defined by reference geometry. Not current route identity or a rounded/catalogue length.</summary>
public sealed class OpenBeltLengthSpecification
{
    public OpenBeltLengthSpecification(ExactQuantity centerDistance, ExactQuantity inputPitchRadius, ExactQuantity outputPitchRadius,
        string semantics = OpenBeltProfile.LengthSemantics)
    {
        foreach (var q in new[] { centerDistance, inputPitchRadius, outputPitchRadius }) OpenBeltProfile.Quantity(q);
        Semantics = MechanicalAuthoringProfile.IdValue(semantics);
        CenterDistance = centerDistance; InputPitchRadius = inputPitchRadius; OutputPitchRadius = outputPitchRadius;
        SpecificationId = HashText(CanonicalRepresentation);
    }
    public ExactQuantity CenterDistance { get; }
    public ExactQuantity InputPitchRadius { get; }
    public ExactQuantity OutputPitchRadius { get; }
    public string Semantics { get; }
    public string SpecificationId { get; }
    public string CanonicalRepresentation => Pack(Semantics, OpenBeltProfile.Q(CenterDistance), OpenBeltProfile.Q(InputPitchRadius), OpenBeltProfile.Q(OutputPitchRadius));
}

/// <summary>Two separate pulley bodies, one retained source shaft and one declared output shaft. No stored transfer or implicit driver.</summary>
public sealed class OpenBeltTransmissionDefinition
{
    public OpenBeltTransmissionDefinition(string id, string inputShaftId, string inputPulleyBodyId,
        ExactVector3 inputPulleyCenterMm, ExactQuantity inputPitchRadius, OrientedShaft outputShaft,
        string outputPulleyBodyId, ExactQuantity outputPulleyStation, ExactQuantity outputPitchRadius,
        ExactVector3 routeNormal, ExactQuantity inputReferenceTurns, ExactQuantity outputReferenceTurns,
        OpenBeltLengthSpecification? selectedBelt, bool transmissionPresent = true,
        bool inputCenterFixed = true, bool inputAxisFixed = true, bool outputCenterFixed = true, bool outputAxisFixed = true,
        string? inputPortId = null, string routingKind = OpenBeltProfile.RoutingKind)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); InputShaftId = MechanicalAuthoringProfile.IdValue(inputShaftId);
        InputPulleyBodyId = MechanicalAuthoringProfile.IdValue(inputPulleyBodyId); OutputPulleyBodyId = MechanicalAuthoringProfile.IdValue(outputPulleyBodyId);
        OutputShaft = outputShaft ?? throw new ArgumentNullException(nameof(outputShaft));
        MechanicalAuthoringProfile.IdValue(outputShaft.Id); MechanicalAuthoringProfile.FrameBound(outputShaft.Frame);
        MechanicalAuthoringProfile.VectorBound(inputPulleyCenterMm); MechanicalAuthoringProfile.VectorBound(routeNormal);
        foreach (var q in new[] { inputPitchRadius, outputPitchRadius, outputPulleyStation, inputReferenceTurns, outputReferenceTurns }) OpenBeltProfile.Quantity(q);
        InputPortId = inputPortId is null ? null : MechanicalAuthoringProfile.IdValue(inputPortId);
        RoutingKind = MechanicalAuthoringProfile.IdValue(routingKind);
        InputPulleyCenterMm = inputPulleyCenterMm; InputPitchRadius = inputPitchRadius;
        OutputPulleyStation = outputPulleyStation; OutputPitchRadius = outputPitchRadius; RouteNormal = routeNormal;
        InputReferenceTurns = inputReferenceTurns; OutputReferenceTurns = outputReferenceTurns;
        SelectedBelt = selectedBelt; TransmissionPresent = transmissionPresent;
        InputCenterFixed = inputCenterFixed; InputAxisFixed = inputAxisFixed; OutputCenterFixed = outputCenterFixed; OutputAxisFixed = outputAxisFixed;
    }
    public string Id { get; }
    public string InputShaftId { get; }
    public string InputPulleyBodyId { get; }
    public ExactVector3 InputPulleyCenterMm { get; }
    public ExactQuantity InputPitchRadius { get; }
    public OrientedShaft OutputShaft { get; }
    public string OutputPulleyBodyId { get; }
    public ExactQuantity OutputPulleyStation { get; }
    public ExactQuantity OutputPitchRadius { get; }
    public ExactVector3 RouteNormal { get; }
    public ExactQuantity InputReferenceTurns { get; }
    public ExactQuantity OutputReferenceTurns { get; }
    public OpenBeltLengthSpecification? SelectedBelt { get; }
    public bool TransmissionPresent { get; }
    public bool InputCenterFixed { get; }
    public bool InputAxisFixed { get; }
    public bool OutputCenterFixed { get; }
    public bool OutputAxisFixed { get; }
    public string? InputPortId { get; }
    public string RoutingKind { get; }
    public ExactVector3 OutputPulleyCenterMm
    {
        get
        {
            var p = OutputShaft.Frame.Origin + OutputShaft.Frame.Z * OutputPulleyStation.Value;
            MechanicalDerivedNumbers.Check(p.X); MechanicalDerivedNumbers.Check(p.Y); MechanicalDerivedNumbers.Check(p.Z); return p;
        }
    }
    public string CanonicalRepresentation => Pack(Id, InputShaftId, InputPulleyBodyId, V(InputPulleyCenterMm), OpenBeltProfile.Q(InputPitchRadius),
        OpenBeltProfile.Shaft(OutputShaft), OutputPulleyBodyId, OpenBeltProfile.Q(OutputPulleyStation), OpenBeltProfile.Q(OutputPitchRadius),
        V(RouteNormal), OpenBeltProfile.Q(InputReferenceTurns), OpenBeltProfile.Q(OutputReferenceTurns), SelectedBelt?.CanonicalRepresentation ?? "",
        OpenBeltProfile.Flag(TransmissionPresent), OpenBeltProfile.Flag(InputCenterFixed), OpenBeltProfile.Flag(InputAxisFixed),
        OpenBeltProfile.Flag(OutputCenterFixed), OpenBeltProfile.Flag(OutputAxisFixed), InputPortId ?? "", RoutingKind);
}

public sealed class OpenBeltDefinition
{
    public OpenBeltDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, OpenBeltTransmissionDefinition device,
        ShaftPort outputTerminal, MechanicalOutput output, Rational? requiredOutputPhase = null, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); OutputTerminal = outputTerminal ?? throw new ArgumentNullException(nameof(outputTerminal));
        Output = output ?? throw new ArgumentNullException(nameof(output)); RequiredOutputPhase = requiredOutputPhase;
        MechanicalAuthoringProfile.IdValue(outputTerminal.Id); MechanicalAuthoringProfile.IdValue(outputTerminal.ShaftId);
        MechanicalAuthoringProfile.FrameBound(outputTerminal.Frame); MechanicalAuthoringProfile.Number(outputTerminal.PhaseOffset);
        if (requiredOutputPhase.HasValue) MechanicalAuthoringProfile.Number(requiredOutputPhase.Value);
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 24);
        if (source.Definition.Shafts.Any(s => s.Id == device.OutputShaft.Id)) throw new ArgumentException("The added output shaft must have a distinct stable ID; existing source loops are unsupported.");
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; }
    public SourceLengthMapping? SourceMapping { get; }
    public OpenBeltTransmissionDefinition Device { get; }
    public ShaftPort OutputTerminal { get; }
    public MechanicalOutput Output { get; }
    public Rational? RequiredOutputPhase { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(OpenBeltProfile.Id, OpenBeltProfile.NumericSemantics, Source.DefinitionId,
        SourceMapping?.CanonicalRepresentation ?? "", Device.CanonicalRepresentation, OpenBeltProfile.Port(OutputTerminal),
        OpenBeltProfile.Output(Output), RequiredOutputPhase.HasValue ? F(RequiredOutputPhase.Value) : "", Pack(RequiredValidationDomains.ToArray()));
}

public sealed class OpenBeltDraft
{
    public OpenBeltDraft(OpenBeltDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public OpenBeltDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public OpenBeltDraft WithDefinition(OpenBeltDefinition definition) => new(definition, checked(Revision + 1));
}
