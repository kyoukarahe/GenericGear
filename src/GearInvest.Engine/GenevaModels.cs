using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class GenevaProfile
{
    public const string Id = "cardinal-external-geneva-point-pin-ideal-lock-v1";
    public const string MotionDomain = "RotaryToIntermittentRotary";
    public const string AnalysisPolicy = "geneva-pin-slot-registered-ideal-arc-lock-v1";
    public const string IdealPhaseReleasedArcLock = "IdealPhaseReleasedArcLock";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Q(ExactQuantity value) => RotaryLinearProfile.Q(value);
    internal static string Flag(bool value) => value ? "1" : "0";
    internal static ReadOnlyCollection<string> OrderedIds(IEnumerable<string> values)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        var list = values.Take(65).ToArray();
        if (list.Length > 64 || list.Distinct(StringComparer.Ordinal).Count() != list.Length) throw new ArgumentException("Bounded distinct material IDs required.");
        foreach (var id in list) MechanicalAuthoringProfile.IdValue(id);
        return Array.AsReadOnly(list);
    }
    internal static BigInteger Floor(Rational value)
    { var q = BigInteger.DivRem(value.Numerator, value.Denominator, out var r); return r.Sign < 0 ? q - 1 : q; }
    internal static Rational ModOne(Rational value) => value - new Rational(Floor(value));
    internal static int Mod(BigInteger value, int count)
    { var r = value % count; return (int)(r.Sign < 0 ? r + count : r); }
    internal static void Derived(params Rational[] values) { foreach (var value in values) MechanicalDerivedNumbers.Check(value); }
}

/// <summary>Ordered IDs identify material slots, independently of the current active slot.</summary>
public sealed class GenevaWheelSpecification
{
    public GenevaWheelSpecification(int slotCount, GenevaLength mouthRadius, ExactQuantity slotRoot, IEnumerable<string> slotIds,
        string slotKind = "StraightRadial")
    {
        SlotCount = slotCount; MouthRadius = mouthRadius ?? throw new ArgumentNullException(nameof(mouthRadius));
        RotaryLinearProfile.Quantity(slotRoot); SlotRoot = slotRoot; SlotIds = GenevaProfile.OrderedIds(slotIds);
        SlotKind = MechanicalAuthoringProfile.IdValue(slotKind);
    }
    public int SlotCount { get; } public GenevaLength MouthRadius { get; } public ExactQuantity SlotRoot { get; }
    public ReadOnlyCollection<string> SlotIds { get; } public string SlotKind { get; }
    public string CanonicalRepresentation => Pack(N(SlotCount), MouthRadius.CanonicalRepresentation, GenevaProfile.Q(SlotRoot), Pack(SlotIds.ToArray()), SlotKind);
}

/// <summary>A full driver circular locus and independent finite material recesses, not a validated relief solid.</summary>
public sealed class GenevaIdealLockSpecification
{
    public GenevaIdealLockSpecification(string driverFeatureId, IEnumerable<string> recessIds, int recessPitchCount,
        ExactQuantity recessCenterDistance, ExactQuantity driverRadius, ExactQuantity recessRadius, ExactQuantity patchHalfWidth,
        ExactQuantity recessMountingTurns, ExactQuantityInterval releaseWindow, bool present = true,
        string policy = GenevaProfile.IdealPhaseReleasedArcLock, string driverSurfaceKind = "FullCircularConvexLocus")
    {
        DriverFeatureId = MechanicalAuthoringProfile.IdValue(driverFeatureId); RecessIds = GenevaProfile.OrderedIds(recessIds);
        RecessPitchCount = recessPitchCount;
        foreach (var q in new[] { recessCenterDistance, driverRadius, recessRadius, patchHalfWidth, recessMountingTurns }) RotaryLinearProfile.Quantity(q);
        RotaryLinearProfile.Interval(releaseWindow); RecessCenterDistance = recessCenterDistance; DriverRadius = driverRadius;
        RecessRadius = recessRadius; PatchHalfWidth = patchHalfWidth; RecessMountingTurns = recessMountingTurns;
        ReleaseWindow = releaseWindow; Present = present; Policy = MechanicalAuthoringProfile.IdValue(policy);
        DriverSurfaceKind = MechanicalAuthoringProfile.IdValue(driverSurfaceKind);
    }
    public string DriverFeatureId { get; } public ReadOnlyCollection<string> RecessIds { get; } public int RecessPitchCount { get; }
    public ExactQuantity RecessCenterDistance { get; } public ExactQuantity DriverRadius { get; } public ExactQuantity RecessRadius { get; }
    public ExactQuantity PatchHalfWidth { get; } public ExactQuantity RecessMountingTurns { get; } public ExactQuantityInterval ReleaseWindow { get; }
    public bool Present { get; } public string Policy { get; } public string DriverSurfaceKind { get; }
    public string CanonicalRepresentation => Pack(DriverFeatureId, Pack(RecessIds.ToArray()), N(RecessPitchCount),
        GenevaProfile.Q(RecessCenterDistance), GenevaProfile.Q(DriverRadius), GenevaProfile.Q(RecessRadius), GenevaProfile.Q(PatchHalfWidth),
        GenevaProfile.Q(RecessMountingTurns), RotaryLinearProfile.I(ReleaseWindow), GenevaProfile.Flag(Present), Policy, DriverSurfaceKind);
}

/// <summary>One point pin integral to a separately mounted driver and a new non-prescribed Geneva wheel shaft.</summary>
public sealed class GenevaDeviceDefinition
{
    public GenevaDeviceDefinition(string id, string sourceShaftId, string driverBodyId, string pinId, string wheelBodyId,
        ExactVector3 driverCenterMm, ExactQuantity driverStation, ExactVector3 planeNormal, ExactVector3 centerDirection,
        OrientedShaft outputShaft, ExactVector3 wheelCenterMm, ExactQuantity wheelStation, ExactQuantity driverMountingTurns,
        ExactQuantity wheelMountingTurns, ExactQuantity outputReferenceTurns, int registrationSlot,
        GenevaLength orbitRadius, GenevaWheelSpecification wheel, GenevaIdealLockSpecification idealLock,
        bool pinPresent = true, bool axesFixed = true, string? sourcePortId = null, string mechanismKind = "External",
        string pinKind = "Point", int pinCount = 1)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); SourceShaftId = MechanicalAuthoringProfile.IdValue(sourceShaftId);
        DriverBodyId = MechanicalAuthoringProfile.IdValue(driverBodyId); PinId = MechanicalAuthoringProfile.IdValue(pinId);
        WheelBodyId = MechanicalAuthoringProfile.IdValue(wheelBodyId); SourcePortId = sourcePortId is null ? null : MechanicalAuthoringProfile.IdValue(sourcePortId);
        foreach (var v in new[] { driverCenterMm, planeNormal, centerDirection, wheelCenterMm }) MechanicalAuthoringProfile.VectorBound(v);
        foreach (var q in new[] { driverStation, wheelStation, driverMountingTurns, wheelMountingTurns, outputReferenceTurns }) RotaryLinearProfile.Quantity(q);
        OutputShaft = outputShaft ?? throw new ArgumentNullException(nameof(outputShaft));
        MechanicalAuthoringProfile.IdValue(outputShaft.Id); MechanicalAuthoringProfile.FrameBound(outputShaft.Frame);
        DriverCenterMm = driverCenterMm; DriverStation = driverStation; PlaneNormal = planeNormal; CenterDirection = centerDirection;
        WheelCenterMm = wheelCenterMm; WheelStation = wheelStation; DriverMountingTurns = driverMountingTurns;
        WheelMountingTurns = wheelMountingTurns; OutputReferenceTurns = outputReferenceTurns; RegistrationSlot = registrationSlot;
        OrbitRadius = orbitRadius ?? throw new ArgumentNullException(nameof(orbitRadius)); Wheel = wheel ?? throw new ArgumentNullException(nameof(wheel));
        IdealLock = idealLock ?? throw new ArgumentNullException(nameof(idealLock)); PinPresent = pinPresent; AxesFixed = axesFixed;
        MechanismKind = MechanicalAuthoringProfile.IdValue(mechanismKind); PinKind = MechanicalAuthoringProfile.IdValue(pinKind); PinCount = pinCount;
    }
    public string Id { get; } public string SourceShaftId { get; } public string? SourcePortId { get; }
    public string DriverBodyId { get; } public string PinId { get; } public string WheelBodyId { get; }
    public ExactVector3 DriverCenterMm { get; } public ExactQuantity DriverStation { get; } public ExactVector3 PlaneNormal { get; }
    public ExactVector3 CenterDirection { get; } public ExactVector3 TransverseDirection => PlaneNormal.Cross(CenterDirection);
    public OrientedShaft OutputShaft { get; } public ExactVector3 WheelCenterMm { get; } public ExactQuantity WheelStation { get; }
    public ExactQuantity DriverMountingTurns { get; } public ExactQuantity WheelMountingTurns { get; } public ExactQuantity OutputReferenceTurns { get; }
    public int RegistrationSlot { get; } public GenevaLength OrbitRadius { get; } public GenevaWheelSpecification Wheel { get; }
    public GenevaIdealLockSpecification IdealLock { get; } public bool PinPresent { get; } public bool AxesFixed { get; }
    public string MechanismKind { get; } public string PinKind { get; } public int PinCount { get; }
    public string CanonicalRepresentation => Pack(Id, SourceShaftId, SourcePortId ?? "", DriverBodyId, PinId, WheelBodyId,
        V(DriverCenterMm), GenevaProfile.Q(DriverStation), V(PlaneNormal), V(CenterDirection), MechanicalDefinition.ShaftKey(OutputShaft),
        V(WheelCenterMm), GenevaProfile.Q(WheelStation), GenevaProfile.Q(DriverMountingTurns), GenevaProfile.Q(WheelMountingTurns),
        GenevaProfile.Q(OutputReferenceTurns), N(RegistrationSlot), OrbitRadius.CanonicalRepresentation, Wheel.CanonicalRepresentation,
        IdealLock.CanonicalRepresentation, GenevaProfile.Flag(PinPresent), GenevaProfile.Flag(AxesFixed), MechanismKind, PinKind, N(PinCount));
}

public sealed class GenevaOutputDefinition
{
    public GenevaOutputDefinition(string key, string shaftId, string bodyId, int terminalSign = 1, ExactQuantity? terminalDatum = null)
    {
        Key = MechanicalAuthoringProfile.IdValue(key); ShaftId = MechanicalAuthoringProfile.IdValue(shaftId); BodyId = MechanicalAuthoringProfile.IdValue(bodyId);
        TerminalSign = terminalSign; TerminalDatum = terminalDatum ?? ExactQuantity.Turns(0); RotaryLinearProfile.Quantity(TerminalDatum);
    }
    public string Key { get; } public string ShaftId { get; } public string BodyId { get; } public int TerminalSign { get; } public ExactQuantity TerminalDatum { get; }
    public string CanonicalRepresentation => Pack(Key, ShaftId, BodyId, N(TerminalSign), GenevaProfile.Q(TerminalDatum));
}

public sealed class GenevaRequirement
{
    public GenevaRequirement(ExactQuantity? requiredIndexStep = null, Rational? requiredDwellFraction = null,
        ExactQuantity? requiredReferenceOutput = null, ExactQuantity? referenceRoot = null)
    {
        RequiredIndexStep = requiredIndexStep; RequiredDwellFraction = requiredDwellFraction; RequiredReferenceOutput = requiredReferenceOutput;
        ReferenceRoot = referenceRoot ?? ExactQuantity.Turns(0);
        foreach (var q in new[] { requiredIndexStep, requiredReferenceOutput, ReferenceRoot }) if (q.HasValue) RotaryLinearProfile.Quantity(q.Value);
        if (requiredDwellFraction.HasValue) MechanicalAuthoringProfile.Number(requiredDwellFraction.Value);
    }
    public ExactQuantity? RequiredIndexStep { get; } public Rational? RequiredDwellFraction { get; }
    public ExactQuantity? RequiredReferenceOutput { get; } public ExactQuantity ReferenceRoot { get; }
    public string CanonicalRepresentation => Pack(RequiredIndexStep.HasValue ? GenevaProfile.Q(RequiredIndexStep.Value) : "",
        RequiredDwellFraction.HasValue ? F(RequiredDwellFraction.Value) : "", RequiredReferenceOutput.HasValue ? GenevaProfile.Q(RequiredReferenceOutput.Value) : "",
        GenevaProfile.Q(ReferenceRoot));
}

public sealed class GenevaDefinition
{
    public GenevaDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, GenevaDeviceDefinition device,
        GenevaOutputDefinition output, GenevaRequirement requirement, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 32);
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; } public SourceLengthMapping? SourceMapping { get; } public GenevaDeviceDefinition Device { get; }
    public GenevaOutputDefinition Output { get; } public GenevaRequirement Requirement { get; } public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(GenevaProfile.Id, Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "",
        Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation, Pack(RequiredValidationDomains.ToArray()));
}

public sealed class GenevaDraft
{
    public GenevaDraft(GenevaDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition)); if (revision < 0) throw new ArgumentException("Nonnegative draft revision required.");
        Revision = revision; DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public GenevaDefinition Definition { get; } public long Revision { get; } public string DefinitionId => Definition.DefinitionId; public string DraftId { get; }
    public GenevaDraft WithDefinition(GenevaDefinition definition) => new(definition, checked(Revision + 1));
}

/// <summary>Proposal values only; applying any of them requires an explicit edit.</summary>
public sealed class GenevaMatchingGeometryProposal
{
    internal GenevaMatchingGeometryProposal(GenevaDraft draft, Rational distance, GenevaLength orbit, GenevaWheelSpecification wheel, GenevaIdealLockSpecification idealLock)
    { DraftId = draft.DraftId; CenterDistanceMm = distance; OrbitRadius = orbit; Wheel = wheel; IdealLock = idealLock; }
    public string DraftId { get; } public Rational CenterDistanceMm { get; } public GenevaLength OrbitRadius { get; }
    public GenevaWheelSpecification Wheel { get; } public GenevaIdealLockSpecification IdealLock { get; }
    public string CanonicalRepresentation => Pack(DraftId, F(CenterDistanceMm), OrbitRadius.CanonicalRepresentation, Wheel.CanonicalRepresentation, IdealLock.CanonicalRepresentation);
    public string ProposalId => HashText(CanonicalRepresentation);
}
