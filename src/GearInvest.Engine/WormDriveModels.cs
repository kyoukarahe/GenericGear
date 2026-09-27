using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class WormDriveProfile
{
    public const string Id = "cardinal-cylindrical-worm-axial-module-v1";
    public const string GeometrySemantics = "ideal-cylindrical-worm-pitch-trace-v1";
    public const string PoseSemantics = "fixed-center-worm-material-turns-v1";
    public const string AnalysisPolicy = "exact-ideal-worm-helical-phase-v1";
    public const string MotionDomain = MechanicalAuthoringProfile.MotionDomain;
    public const string StandaloneSource = "single-prescribed-rotary-shaft-v1";
    public const string WormGeometry = "cylindrical";
    public const string AxialParameterization = "axial-module-v1";
    public const string WheelParameterization = "transverse-module-v1";
    public const string WheelTraceSemantics = "ideal-local-worm-wheel-trace-v1";
    public const int MinStarts = 1, MaxStarts = 8, MinTeeth = 8, MaxTeeth = 4096;
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxDocumentDepth = 48, MaxJsonNodes = 131072;
    public const int MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Q(ExactQuantity q) => Pack(q.Kind.ToString(), q.Unit, F(q.Value));
    internal static string Flag(bool value) => value ? "1" : "0";
    internal static void Quantity(ExactQuantity value) => MechanicalAuthoringProfile.Number(value.Value);
    internal static string Shaft(OrientedShaft shaft) => Pack(shaft.Id, Frame(shaft.Frame), Flag(shaft.IsPrescribed));
    internal static string Port(ShaftPort port) => Pack(port.Id, port.ShaftId, Frame(port.Frame), F(port.PhaseOffset), port.Kind.ToString());
    internal static string Output(MechanicalOutput output) => Pack(output.Key, output.ShaftId ?? "", output.BodyId ?? "", output.PortId ?? "",
        output.RequiredTransfer.HasValue ? F(output.RequiredTransfer.Value) : "", output.Role?.ToString() ?? "", output.UnresolvedReason ?? "",
        Pack(output.FormerEndpoint.Select(r => Pack(r.Kind, r.Id)).ToArray()));
}

/// <summary>Physical handedness refers to z=z0+H*L*t around the declared physical helix axis.
/// Pitch radius is an independent length, never a spur tooth-radius inference.</summary>
public sealed class CylindricalWormSpecification
{
    public CylindricalWormSpecification(int starts, ExactQuantity axialModule, ExactQuantity pitchRadius, int handedness,
        string parameterization = WormDriveProfile.AxialParameterization, string geometryKind = WormDriveProfile.WormGeometry)
    {
        WormDriveProfile.Quantity(axialModule); WormDriveProfile.Quantity(pitchRadius);
        Parameterization = MechanicalAuthoringProfile.IdValue(parameterization); GeometryKind = MechanicalAuthoringProfile.IdValue(geometryKind);
        Starts = starts; AxialModule = axialModule; PitchRadius = pitchRadius; Handedness = handedness;
        SpecificationId = HashText(CanonicalRepresentation);
    }
    public int Starts { get; }
    public ExactQuantity AxialModule { get; }
    public ExactQuantity PitchRadius { get; }
    public int Handedness { get; }
    public string Parameterization { get; }
    public string GeometryKind { get; }
    public string SpecificationId { get; }
    public string CanonicalRepresentation => Pack(Starts.ToString(CultureInfo.InvariantCulture), WormDriveProfile.Q(AxialModule),
        WormDriveProfile.Q(PitchRadius), Handedness.ToString(CultureInfo.InvariantCulture), Parameterization, GeometryKind);
}

/// <summary>Independently selected ideal local wheel tooth trace, not an automatically repaired spur gear.
/// A positive dimensionless trace slope and physical hand are declared inputs.</summary>
public sealed class IdealWormWheelSpecification
{
    public IdealWormWheelSpecification(int toothCount, ExactQuantity transverseModule, int handedness, Rational traceSlope,
        string parameterization = WormDriveProfile.WheelParameterization, string traceSemantics = WormDriveProfile.WheelTraceSemantics)
    {
        WormDriveProfile.Quantity(transverseModule); MechanicalAuthoringProfile.Number(traceSlope);
        Parameterization = MechanicalAuthoringProfile.IdValue(parameterization); TraceSemantics = MechanicalAuthoringProfile.IdValue(traceSemantics);
        ToothCount = toothCount; TransverseModule = transverseModule; Handedness = handedness; TraceSlope = traceSlope;
        SpecificationId = HashText(CanonicalRepresentation);
    }
    public int ToothCount { get; }
    public ExactQuantity TransverseModule { get; }
    public int Handedness { get; }
    public Rational TraceSlope { get; }
    public string Parameterization { get; }
    public string TraceSemantics { get; }
    public string SpecificationId { get; }
    public string CanonicalRepresentation => Pack(ToothCount.ToString(CultureInfo.InvariantCulture), WormDriveProfile.Q(TransverseModule),
        Handedness.ToString(CultureInfo.InvariantCulture), F(TraceSlope), Parameterization, TraceSemantics);
}

/// <summary>Two distinct mounted bodies on skew orthogonal fixed shafts. Stations are signed canonical mm.
/// Physical axis and contact side are world directions independent of positive shaft coordinates.</summary>
public sealed class WormDriveTransmissionDefinition
{
    public WormDriveTransmissionDefinition(string id, string inputShaftId, string inputWormBodyId,
        CylindricalWormSpecification worm, ExactVector3 physicalHelixAxis, ExactQuantity inputPitchStation,
        OrientedShaft outputShaft, string outputWheelBodyId, IdealWormWheelSpecification? selectedWheel,
        ExactQuantity outputPitchStation, ExactVector3 contactSide,
        ExactQuantity inputMountingPhase, ExactQuantity outputMountingPhase,
        ExactQuantity inputReferenceTurns, ExactQuantity outputReferenceTurns,
        bool transmissionPresent = true, bool inputCenterFixed = true, bool inputAxisFixed = true,
        bool outputCenterFixed = true, bool outputAxisFixed = true, string? inputPortId = null,
        string profile = WormDriveProfile.Id)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); InputShaftId = MechanicalAuthoringProfile.IdValue(inputShaftId);
        InputWormBodyId = MechanicalAuthoringProfile.IdValue(inputWormBodyId); OutputWheelBodyId = MechanicalAuthoringProfile.IdValue(outputWheelBodyId);
        Worm = worm ?? throw new ArgumentNullException(nameof(worm)); SelectedWheel = selectedWheel;
        OutputShaft = outputShaft ?? throw new ArgumentNullException(nameof(outputShaft));
        MechanicalAuthoringProfile.IdValue(outputShaft.Id); MechanicalAuthoringProfile.FrameBound(outputShaft.Frame);
        MechanicalAuthoringProfile.VectorBound(physicalHelixAxis); MechanicalAuthoringProfile.VectorBound(contactSide);
        foreach (var value in new[] { inputPitchStation, outputPitchStation, inputMountingPhase, outputMountingPhase,
            inputReferenceTurns, outputReferenceTurns }) WormDriveProfile.Quantity(value);
        InputPortId = inputPortId is null ? null : MechanicalAuthoringProfile.IdValue(inputPortId);
        Profile = MechanicalAuthoringProfile.IdValue(profile);
        PhysicalHelixAxis = physicalHelixAxis; InputPitchStation = inputPitchStation; OutputPitchStation = outputPitchStation;
        ContactSide = contactSide; InputMountingPhase = inputMountingPhase; OutputMountingPhase = outputMountingPhase;
        InputReferenceTurns = inputReferenceTurns; OutputReferenceTurns = outputReferenceTurns;
        TransmissionPresent = transmissionPresent; InputCenterFixed = inputCenterFixed; InputAxisFixed = inputAxisFixed;
        OutputCenterFixed = outputCenterFixed; OutputAxisFixed = outputAxisFixed;
    }
    public string Id { get; }
    public string InputShaftId { get; }
    public string InputWormBodyId { get; }
    public CylindricalWormSpecification Worm { get; }
    public ExactVector3 PhysicalHelixAxis { get; }
    public ExactQuantity InputPitchStation { get; }
    public OrientedShaft OutputShaft { get; }
    public string OutputWheelBodyId { get; }
    public IdealWormWheelSpecification? SelectedWheel { get; }
    public ExactQuantity OutputPitchStation { get; }
    public ExactVector3 ContactSide { get; }
    public ExactQuantity InputMountingPhase { get; }
    public ExactQuantity OutputMountingPhase { get; }
    public ExactQuantity InputReferenceTurns { get; }
    public ExactQuantity OutputReferenceTurns { get; }
    public bool TransmissionPresent { get; }
    public bool InputCenterFixed { get; }
    public bool InputAxisFixed { get; }
    public bool OutputCenterFixed { get; }
    public bool OutputAxisFixed { get; }
    public string? InputPortId { get; }
    public string Profile { get; }
    public ExactVector3 OutputPitchCenterMm
    {
        get
        {
            var point = OutputShaft.Frame.Origin + OutputShaft.Frame.Z * OutputPitchStation.Value;
            MechanicalDerivedNumbers.Check(point.X); MechanicalDerivedNumbers.Check(point.Y); MechanicalDerivedNumbers.Check(point.Z); return point;
        }
    }
    public string CanonicalRepresentation => Pack(Id, InputShaftId, InputWormBodyId, Worm.CanonicalRepresentation, V(PhysicalHelixAxis),
        WormDriveProfile.Q(InputPitchStation), WormDriveProfile.Shaft(OutputShaft), OutputWheelBodyId, SelectedWheel?.CanonicalRepresentation ?? "",
        WormDriveProfile.Q(OutputPitchStation), V(ContactSide), WormDriveProfile.Q(InputMountingPhase), WormDriveProfile.Q(OutputMountingPhase),
        WormDriveProfile.Q(InputReferenceTurns), WormDriveProfile.Q(OutputReferenceTurns), WormDriveProfile.Flag(TransmissionPresent),
        WormDriveProfile.Flag(InputCenterFixed), WormDriveProfile.Flag(InputAxisFixed), WormDriveProfile.Flag(OutputCenterFixed),
        WormDriveProfile.Flag(OutputAxisFixed), InputPortId ?? "", Profile);
}

public sealed class WormDriveDefinition
{
    public WormDriveDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, WormDriveTransmissionDefinition device,
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
            throw new ArgumentException("The added output shaft requires a distinct stable ID; arbitrary source loops are unsupported.");
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; }
    public SourceLengthMapping? SourceMapping { get; }
    public WormDriveTransmissionDefinition Device { get; }
    /// <summary>Worm-local angular calibration: terminal=sign*shaft+PhaseOffset. It is not an admitted rigid port edge.</summary>
    public ShaftPort OutputTerminal { get; }
    public MechanicalOutput Output { get; }
    public Rational? RequiredOutputPhase { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(WormDriveProfile.Id, WormDriveProfile.GeometrySemantics, WormDriveProfile.PoseSemantics,
        Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "", Device.CanonicalRepresentation, WormDriveProfile.Port(OutputTerminal),
        WormDriveProfile.Output(Output), RequiredOutputPhase.HasValue ? F(RequiredOutputPhase.Value) : "", Pack(RequiredValidationDomains.ToArray()));
}

public sealed class WormDriveDraft
{
    public WormDriveDraft(WormDriveDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public WormDriveDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public WormDriveDraft WithDefinition(WormDriveDefinition definition) => new(definition, checked(Revision + 1));
}
