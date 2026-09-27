using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class PitchChainProfile
{
    public const string Id = "cardinal-equal-even-sprocket-pitch-chain-v1";
    public const string GeometrySemantics = "regular-sprocket-pitch-polygon-v1";
    public const string PoseSemantics = "clockwise-equal-sprocket-material-index-v1";
    public const string AnalysisPolicy = "exact-synchronized-pitch-chain-v1";
    public const string MotionDomain = MechanicalAuthoringProfile.MotionDomain;
    public const string StandaloneSource = "single-prescribed-rotary-shaft-v1";
    public const string RoutingKind = "open";
    public const string LinkTopology = "closed-alternating";
    public const int MinTeeth = 6, MaxTeeth = 128, MaxLinks = 4096, MaxMaterialPoses = 4096;
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Q(ExactQuantity q) => Pack(q.Kind.ToString(), q.Unit, F(q.Value));
    internal static string Flag(bool value) => value ? "1" : "0";
    internal static string I(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
    internal static void Quantity(ExactQuantity value) => MechanicalAuthoringProfile.Number(value.Value);
    internal static void Integer(BigInteger value) => MechanicalAuthoringProfile.Number(new Rational(value));
    internal static string Shaft(OrientedShaft shaft) => Pack(shaft.Id, Frame(shaft.Frame), Flag(shaft.IsPrescribed));
    internal static string Port(ShaftPort port) => Pack(port.Id, port.ShaftId, Frame(port.Frame), F(port.PhaseOffset), port.Kind.ToString());
    internal static string Output(MechanicalOutput output) => Pack(output.Key, output.ShaftId ?? "", output.BodyId ?? "", output.PortId ?? "",
        output.RequiredTransfer.HasValue ? F(output.RequiredTransfer.Value) : "", output.Role?.ToString() ?? "", output.UnresolvedReason ?? "",
        Pack(output.FormerEndpoint.Select(r => Pack(r.Kind, r.Id)).ToArray()));
}

/// <summary>An independently selected immutable chain, not a geometry-dependent resized belt.
/// Material registration is an exact signed integer; only bounded lookup uses Euclidean modulo.</summary>
public sealed class PitchChainSpecification
{
    public PitchChainSpecification(string chainId, ExactQuantity pitch, int linkCount, BigInteger materialRegistration = default,
        string topology = PitchChainProfile.LinkTopology)
    {
        ChainId = MechanicalAuthoringProfile.IdValue(chainId); Topology = MechanicalAuthoringProfile.IdValue(topology);
        PitchChainProfile.Quantity(pitch); PitchChainProfile.Integer(materialRegistration);
        Pitch = pitch; LinkCount = linkCount; MaterialRegistration = materialRegistration;
        SpecificationId = HashText(CanonicalRepresentation);
    }
    public string ChainId { get; }
    public ExactQuantity Pitch { get; }
    public int LinkCount { get; }
    public BigInteger MaterialRegistration { get; }
    public string Topology { get; }
    public string SpecificationId { get; }
    public string CanonicalRepresentation => Pack(ChainId, PitchChainProfile.Q(Pitch), LinkCount.ToString(CultureInfo.InvariantCulture),
        PitchChainProfile.I(MaterialRegistration), Topology);
    public string PinId(int materialIndex)
    {
        CheckMaterialIndex(materialIndex);
        return "chain-pin-" + HashText(Pack(ChainId, materialIndex.ToString(CultureInfo.InvariantCulture)));
    }
    public string LinkId(int materialIndex)
    {
        CheckMaterialIndex(materialIndex);
        return "chain-link-" + HashText(Pack(ChainId, materialIndex.ToString(CultureInfo.InvariantCulture)));
    }
    private void CheckMaterialIndex(int value)
    {
        if (LinkCount <= 0 || LinkCount > PitchChainProfile.MaxLinks || value < 0 || value >= LinkCount)
            throw new ArgumentOutOfRangeException(nameof(value), "A supported bounded material index is required.");
    }
}

/// <summary>Two separately mounted sprockets and one new non-prescribed shaft.
/// Signed mounting angles are relative to each actual shaft zero ray, not a requested output target.</summary>
public sealed class PitchChainTransmissionDefinition
{
    public PitchChainTransmissionDefinition(string id, string inputShaftId, string inputSprocketBodyId,
        int inputToothCount, ExactQuantity inputPitch, ExactQuantity inputSprocketStation,
        OrientedShaft outputShaft, string outputSprocketBodyId, int outputToothCount,
        ExactQuantity outputPitch, ExactQuantity outputSprocketStation, ExactVector3 routeNormal,
        ExactQuantity inputMountingPhase, ExactQuantity outputMountingPhase,
        ExactQuantity inputReferenceTurns, ExactQuantity outputReferenceTurns, BigInteger toothRegistration,
        PitchChainSpecification? selectedChain, bool transmissionPresent = true,
        bool inputCenterFixed = true, bool inputAxisFixed = true, bool outputCenterFixed = true, bool outputAxisFixed = true,
        string? inputPortId = null, string routingKind = PitchChainProfile.RoutingKind)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); InputShaftId = MechanicalAuthoringProfile.IdValue(inputShaftId);
        InputSprocketBodyId = MechanicalAuthoringProfile.IdValue(inputSprocketBodyId); OutputSprocketBodyId = MechanicalAuthoringProfile.IdValue(outputSprocketBodyId);
        OutputShaft = outputShaft ?? throw new ArgumentNullException(nameof(outputShaft));
        MechanicalAuthoringProfile.IdValue(outputShaft.Id); MechanicalAuthoringProfile.FrameBound(outputShaft.Frame);
        MechanicalAuthoringProfile.VectorBound(routeNormal); PitchChainProfile.Integer(toothRegistration);
        foreach (var value in new[] { inputPitch, outputPitch, inputSprocketStation, outputSprocketStation,
            inputMountingPhase, outputMountingPhase, inputReferenceTurns, outputReferenceTurns }) PitchChainProfile.Quantity(value);
        InputPortId = inputPortId is null ? null : MechanicalAuthoringProfile.IdValue(inputPortId);
        RoutingKind = MechanicalAuthoringProfile.IdValue(routingKind);
        InputToothCount = inputToothCount; InputPitch = inputPitch; InputSprocketStation = inputSprocketStation;
        OutputToothCount = outputToothCount; OutputPitch = outputPitch; OutputSprocketStation = outputSprocketStation;
        RouteNormal = routeNormal; InputMountingPhase = inputMountingPhase; OutputMountingPhase = outputMountingPhase;
        InputReferenceTurns = inputReferenceTurns; OutputReferenceTurns = outputReferenceTurns; ToothRegistration = toothRegistration;
        SelectedChain = selectedChain; TransmissionPresent = transmissionPresent;
        InputCenterFixed = inputCenterFixed; InputAxisFixed = inputAxisFixed; OutputCenterFixed = outputCenterFixed; OutputAxisFixed = outputAxisFixed;
    }
    public string Id { get; }
    public string InputShaftId { get; }
    public string InputSprocketBodyId { get; }
    public int InputToothCount { get; }
    public ExactQuantity InputPitch { get; }
    public ExactQuantity InputSprocketStation { get; }
    public OrientedShaft OutputShaft { get; }
    public string OutputSprocketBodyId { get; }
    public int OutputToothCount { get; }
    public ExactQuantity OutputPitch { get; }
    public ExactQuantity OutputSprocketStation { get; }
    public ExactVector3 RouteNormal { get; }
    public ExactQuantity InputMountingPhase { get; }
    public ExactQuantity OutputMountingPhase { get; }
    public ExactQuantity InputReferenceTurns { get; }
    public ExactQuantity OutputReferenceTurns { get; }
    public BigInteger ToothRegistration { get; }
    public PitchChainSpecification? SelectedChain { get; }
    public bool TransmissionPresent { get; }
    public bool InputCenterFixed { get; }
    public bool InputAxisFixed { get; }
    public bool OutputCenterFixed { get; }
    public bool OutputAxisFixed { get; }
    public string? InputPortId { get; }
    public string RoutingKind { get; }
    public ExactVector3 OutputSprocketCenterMm
    {
        get
        {
            var point = OutputShaft.Frame.Origin + OutputShaft.Frame.Z * OutputSprocketStation.Value;
            MechanicalDerivedNumbers.Check(point.X); MechanicalDerivedNumbers.Check(point.Y); MechanicalDerivedNumbers.Check(point.Z); return point;
        }
    }
    public string CanonicalRepresentation => Pack(Id, InputShaftId, InputSprocketBodyId, InputToothCount.ToString(CultureInfo.InvariantCulture),
        PitchChainProfile.Q(InputPitch), PitchChainProfile.Q(InputSprocketStation), PitchChainProfile.Shaft(OutputShaft), OutputSprocketBodyId,
        OutputToothCount.ToString(CultureInfo.InvariantCulture), PitchChainProfile.Q(OutputPitch), PitchChainProfile.Q(OutputSprocketStation), V(RouteNormal),
        PitchChainProfile.Q(InputMountingPhase), PitchChainProfile.Q(OutputMountingPhase), PitchChainProfile.Q(InputReferenceTurns),
        PitchChainProfile.Q(OutputReferenceTurns), PitchChainProfile.I(ToothRegistration), SelectedChain?.CanonicalRepresentation ?? "",
        PitchChainProfile.Flag(TransmissionPresent), PitchChainProfile.Flag(InputCenterFixed), PitchChainProfile.Flag(InputAxisFixed),
        PitchChainProfile.Flag(OutputCenterFixed), PitchChainProfile.Flag(OutputAxisFixed), InputPortId ?? "", RoutingKind);
}

public sealed class PitchChainDefinition
{
    public PitchChainDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, PitchChainTransmissionDefinition device,
        ShaftPort outputTerminal, MechanicalOutput output, Rational? requiredOutputPhase = null, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); OutputTerminal = outputTerminal ?? throw new ArgumentNullException(nameof(outputTerminal));
        Output = output ?? throw new ArgumentNullException(nameof(output)); RequiredOutputPhase = requiredOutputPhase;
        MechanicalAuthoringProfile.IdValue(outputTerminal.Id); MechanicalAuthoringProfile.IdValue(outputTerminal.ShaftId);
        MechanicalAuthoringProfile.FrameBound(outputTerminal.Frame); MechanicalAuthoringProfile.Number(outputTerminal.PhaseOffset);
        if (requiredOutputPhase.HasValue) MechanicalAuthoringProfile.Number(requiredOutputPhase.Value);
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 24);
        if (source.Definition.Shafts.Any(s => s.Id == device.OutputShaft.Id))
            throw new ArgumentException("The added output shaft must have a distinct stable ID; arbitrary source loops are unsupported.");
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; }
    public SourceLengthMapping? SourceMapping { get; }
    public PitchChainTransmissionDefinition Device { get; }
    public ShaftPort OutputTerminal { get; }
    public MechanicalOutput Output { get; }
    public Rational? RequiredOutputPhase { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(PitchChainProfile.Id, PitchChainProfile.GeometrySemantics, PitchChainProfile.PoseSemantics,
        Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "", Device.CanonicalRepresentation, PitchChainProfile.Port(OutputTerminal),
        PitchChainProfile.Output(Output), RequiredOutputPhase.HasValue ? F(RequiredOutputPhase.Value) : "", Pack(RequiredValidationDomains.ToArray()));
}

public sealed class PitchChainDraft
{
    public PitchChainDraft(PitchChainDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public PitchChainDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public PitchChainDraft WithDefinition(PitchChainDefinition definition) => new(definition, checked(Revision + 1));
}
