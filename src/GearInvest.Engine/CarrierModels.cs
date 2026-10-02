using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class CarrierProfile
{
    public const string Id = "parallel-grounded-sun-single-planet-v1";
    public const string Policy = "exact-carrier-contact-and-pitch-planes-v1";
    public const int MaxDocumentBytes = 8 * 1024 * 1024;
    public const int MaxDerivedDigits = 1024;
    internal static void Derived(Rational v)
    {
        if (v.Numerator.ToString(CultureInfo.InvariantCulture).Length > MaxDerivedDigits ||
            v.Denominator.ToString(CultureInfo.InvariantCulture).Length > MaxDerivedDigits)
            throw new ArgumentException("Carrier derived rational resource bound exceeded.");
    }
    internal static string Law(ExactAffineRelation r) => Pack(F(r.Coefficient), F(r.Phase));
    internal static void Bound(ExactQuantity q) => MechanicalAuthoringProfile.Number(q.Value);
    internal static string Q(ExactQuantity q) => Pack(q.Kind.ToString(), q.Unit, F(q.Value));
}

/// <summary>A distinct rotational coordinate with a reference frame in the carrier, NOT a fixed world shaft.</summary>
public sealed class CarrierLocalShaft
{
    public CarrierLocalShaft(string id, string carrierShaftId, OrientedFrame frameInCarrier, bool isPrescribed = false)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); CarrierShaftId = MechanicalAuthoringProfile.IdValue(carrierShaftId);
        MechanicalAuthoringProfile.FrameBound(frameInCarrier); FrameInCarrier = frameInCarrier; IsPrescribed = isPrescribed;
    }
    public string Id { get; }
    public string CarrierShaftId { get; }
    public OrientedFrame FrameInCarrier { get; }
    public bool IsPrescribed { get; }
}

/// <summary>Frame is shaft-local; calibration changes the readout only, not mechanical pose.</summary>
public sealed class CarrierOutputPort
{
    public CarrierOutputPort(string id, string shaftId, OrientedFrame frameInShaft, ExactQuantity readoutOffset)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); ShaftId = MechanicalAuthoringProfile.IdValue(shaftId);
        MechanicalAuthoringProfile.FrameBound(frameInShaft); CarrierProfile.Bound(readoutOffset);
        FrameInShaft = frameInShaft; ReadoutOffset = readoutOffset;
    }
    public string Id { get; }
    public string ShaftId { get; }
    public OrientedFrame FrameInShaft { get; }
    public ExactQuantity ReadoutOffset { get; }
}

/// <summary>Immutable authored mechanism; references, validity and export admission are separate.</summary>
public sealed class CarrierDefinition
{
    public CarrierDefinition(MechanicalDraft source, SourceLengthMapping sourceMapping, string carrierShaftId,
        OrientedFrame planeMm, OrientedShaft sunShaft, CarrierLocalShaft planetShaft,
        int sunTeeth, int planetTeeth, ExactQuantity module,
        ExactQuantity carrierReference, ExactQuantity sunReference, ExactQuantity planetReference,
        ExactQuantity sunMount, ExactQuantity planetMount, Rational toothRegistration,
        CarrierOutputPort outputPort, string carrierBodyId = "carrier-arm", string sunBodyId = "sun-gear",
        string planetBodyId = "planet-gear", bool sunHeld = true, bool contactPresent = true,
        IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping ?? throw new ArgumentNullException(nameof(sourceMapping));
        CarrierShaftId = MechanicalAuthoringProfile.IdValue(carrierShaftId); MechanicalAuthoringProfile.FrameBound(planeMm); PlaneMm = planeMm;
        SunShaft = sunShaft ?? throw new ArgumentNullException(nameof(sunShaft)); MechanicalAuthoringProfile.IdValue(sunShaft.Id); MechanicalAuthoringProfile.FrameBound(sunShaft.Frame);
        PlanetShaft = planetShaft ?? throw new ArgumentNullException(nameof(planetShaft)); OutputPort = outputPort ?? throw new ArgumentNullException(nameof(outputPort));
        CarrierBodyId = MechanicalAuthoringProfile.IdValue(carrierBodyId); SunBodyId = MechanicalAuthoringProfile.IdValue(sunBodyId); PlanetBodyId = MechanicalAuthoringProfile.IdValue(planetBodyId);
        foreach (var q in new[] { module, carrierReference, sunReference, planetReference, sunMount, planetMount }) CarrierProfile.Bound(q);
        MechanicalAuthoringProfile.Number(toothRegistration);
        SunTeeth = sunTeeth; PlanetTeeth = planetTeeth; Module = module;
        CarrierReference = carrierReference; SunReference = sunReference; PlanetReference = planetReference;
        SunMount = sunMount; PlanetMount = planetMount; ToothRegistration = toothRegistration; SunHeld = sunHeld; ContactPresent = contactPresent;
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), x => x, 16);
        DefinitionId = HashText(Pack(CarrierProfile.Id, source.DraftId, sourceMapping.CanonicalRepresentation, carrierShaftId, Frame(planeMm),
            sunShaft.Id, Frame(sunShaft.Frame), sunShaft.IsPrescribed.ToString(), planetShaft.Id, planetShaft.CarrierShaftId, Frame(planetShaft.FrameInCarrier), planetShaft.IsPrescribed.ToString(),
            N(sunTeeth), N(planetTeeth), CarrierProfile.Q(module), CarrierProfile.Q(carrierReference), CarrierProfile.Q(sunReference), CarrierProfile.Q(planetReference),
            CarrierProfile.Q(sunMount), CarrierProfile.Q(planetMount), F(toothRegistration), outputPort.Id, outputPort.ShaftId, Frame(outputPort.FrameInShaft), CarrierProfile.Q(outputPort.ReadoutOffset),
            carrierBodyId, sunBodyId, planetBodyId, sunHeld.ToString(), contactPresent.ToString(), Pack(RequiredValidationDomains.ToArray())));
    }
    public MechanicalDraft Source { get; }
    public SourceLengthMapping SourceMapping { get; }
    public string CarrierShaftId { get; }
    public OrientedFrame PlaneMm { get; }
    public OrientedShaft SunShaft { get; }
    public CarrierLocalShaft PlanetShaft { get; }
    public int SunTeeth { get; }
    public int PlanetTeeth { get; }
    public ExactQuantity Module { get; }
    public ExactQuantity CarrierReference { get; }
    public ExactQuantity SunReference { get; }
    public ExactQuantity PlanetReference { get; }
    public ExactQuantity SunMount { get; }
    public ExactQuantity PlanetMount { get; }
    public Rational ToothRegistration { get; }
    public CarrierOutputPort OutputPort { get; }
    public string CarrierBodyId { get; }
    public string SunBodyId { get; }
    public string PlanetBodyId { get; }
    public bool SunHeld { get; }
    public bool ContactPresent { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
}

public sealed class CarrierShaftLaw
{
    internal CarrierShaftLaw(string id, string motion, ExactAffineRelation world, ExactAffineRelation? relative, string poseNodeId)
    { ShaftId = id; Motion = motion; World = world; CarrierRelative = relative; PoseNodeId = poseNodeId; }
    public string ShaftId { get; }
    public string Motion { get; }
    public ExactAffineRelation World { get; }
    public ExactAffineRelation? CarrierRelative { get; }
    public string PoseNodeId { get; }
}

/// <summary>Generic compiled rigid-frame recipe. Frame then local-Z rotation; parent applied exactly once.</summary>
public sealed class CarrierPoseNode
{
    internal CarrierPoseNode(string id, string? parent, OrientedFrame frame, ExactAffineRelation rotation, string shaftId,
        string? bodyId = null, int? teeth = null, Rational? pitchRadius = null)
    { Id = id; ParentId = parent; Frame = frame; Rotation = rotation; ShaftId = shaftId; BodyId = bodyId; Teeth = teeth; PitchRadiusMm = pitchRadius; }
    public string Id { get; }
    public string? ParentId { get; }
    public OrientedFrame Frame { get; }
    public ExactAffineRelation Rotation { get; }
    public string ShaftId { get; }
    public string? BodyId { get; }
    public int? Teeth { get; }
    public Rational? PitchRadiusMm { get; }
}

public sealed class CarrierAnalysis
{
    internal CarrierAnalysis(CarrierDefinition request, IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics,
        IEnumerable<CarrierShaftLaw>? shafts = null, IEnumerable<CarrierPoseNode>? poses = null,
        ExactAffineRelation? carrierCommon = null, ExactAffineRelation? planetCommon = null, ExactAffineRelation? port = null)
    {
        Request = request; Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(c => c.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        Shafts = (shafts ?? Array.Empty<CarrierShaftLaw>()).OrderBy(s => s.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
        PoseNodes = (poses ?? Array.Empty<CarrierPoseNode>()).ToList().AsReadOnly(); CarrierCommon = carrierCommon; PlanetCommon = planetCommon; PortReadout = port;
    }
    public CarrierDefinition Request { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public ReadOnlyCollection<CarrierShaftLaw> Shafts { get; }
    public ReadOnlyCollection<CarrierPoseNode> PoseNodes { get; }
    public ExactAffineRelation? CarrierCommon { get; }
    public ExactAffineRelation? PlanetCommon { get; }
    public ExactAffineRelation? PortReadout { get; }
    public bool IsValid => Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass) && Shafts.Count >= 3;
}
