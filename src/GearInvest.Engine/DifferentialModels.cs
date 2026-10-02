using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class DifferentialProfile
{
    public const string Id = "parallel-free-sun-single-planet-v1";
    public const string Policy = "exact-bounded-multi-input-carrier-v1";
    public const int MaxDocumentBytes = 8 * 1024 * 1024;
    internal static ReadOnlyCollection<T> Set<T>(IEnumerable<T> values, Func<T, string> id, int max) => MechanicalAuthoringProfile.Set(values, id, max);
    internal static void Angular(ExactQuantity q) { CarrierProfile.Bound(q); if (q.Kind != QuantityKind.AngularPosition) throw new ArgumentException("Exact unwrapped turns required."); }
}

/// <summary>Static coordinate hold, not clutch capture or a runtime state transition.</summary>
public sealed class DifferentialHold
{
    public DifferentialHold(string id, string shaftId, ExactQuantity position)
    { Id = MechanicalAuthoringProfile.IdValue(id); ShaftId = MechanicalAuthoringProfile.IdValue(shaftId); DifferentialProfile.Angular(position); Position = position; }
    public string Id { get; }
    public string ShaftId { get; }
    public ExactQuantity Position { get; }
}

/// <summary>One explicit numeric boundary observation. Aliased ports still constrain the same actual coordinate.</summary>
public sealed class DifferentialBoundary
{
    public DifferentialBoundary(string id, string portId, ExactQuantity position)
    { Id = MechanicalAuthoringProfile.IdValue(id); PortId = MechanicalAuthoringProfile.IdValue(portId); DifferentialProfile.Angular(position); Position = position; }
    public string Id { get; }
    public string PortId { get; }
    public ExactQuantity Position { get; }
}

/// <summary>Independent authored free-sun profile. This does not mutate or unground an E2a definition.</summary>
public sealed class DifferentialDefinition
{
    public DifferentialDefinition(OrientedShaft carrierShaft, OrientedShaft sunShaft, CarrierLocalShaft planetShaft,
        OrientedFrame planeMm, int sunTeeth, int planetTeeth, ExactQuantity module,
        ExactQuantity carrierReference, ExactQuantity sunReference, ExactQuantity planetReference,
        ExactQuantity sunMount, ExactQuantity planetMount, Rational toothRegistration,
        IEnumerable<CarrierOutputPort> ports, IEnumerable<DifferentialHold>? holds = null,
        MechanicalDraft? prefix = null, SourceLengthMapping? prefixMapping = null,
        string carrierBodyId = "carrier-arm", string sunBodyId = "sun-gear", string planetBodyId = "planet-gear",
        bool contactPresent = true, IEnumerable<string>? requiredValidationDomains = null)
    {
        CarrierShaft = carrierShaft ?? throw new ArgumentNullException(nameof(carrierShaft)); SunShaft = sunShaft ?? throw new ArgumentNullException(nameof(sunShaft));
        PlanetShaft = planetShaft ?? throw new ArgumentNullException(nameof(planetShaft));
        foreach (var s in new[] { carrierShaft, sunShaft }) { MechanicalAuthoringProfile.IdValue(s.Id); MechanicalAuthoringProfile.FrameBound(s.Frame); }
        MechanicalAuthoringProfile.FrameBound(planeMm); PlaneMm = planeMm;
        foreach (var q in new[] { module, carrierReference, sunReference, planetReference, sunMount, planetMount }) CarrierProfile.Bound(q);
        MechanicalAuthoringProfile.Number(toothRegistration);
        SunTeeth = sunTeeth; PlanetTeeth = planetTeeth; Module = module; CarrierReference = carrierReference; SunReference = sunReference;
        PlanetReference = planetReference; SunMount = sunMount; PlanetMount = planetMount; ToothRegistration = toothRegistration;
        Ports = DifferentialProfile.Set(ports, p => p.Id, 6); Holds = DifferentialProfile.Set(holds ?? Array.Empty<DifferentialHold>(), h => h.Id, 6);
        Prefix = prefix; PrefixMapping = prefixMapping; ContactPresent = contactPresent;
        CarrierBodyId = MechanicalAuthoringProfile.IdValue(carrierBodyId); SunBodyId = MechanicalAuthoringProfile.IdValue(sunBodyId); PlanetBodyId = MechanicalAuthoringProfile.IdValue(planetBodyId);
        RequiredValidationDomains = DifferentialProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), x => x, 16);
        DefinitionId = HashText(Pack(DifferentialProfile.Id, carrierShaft.Id, Frame(carrierShaft.Frame), carrierShaft.IsPrescribed.ToString(),
            sunShaft.Id, Frame(sunShaft.Frame), sunShaft.IsPrescribed.ToString(), planetShaft.Id, planetShaft.CarrierShaftId, Frame(planetShaft.FrameInCarrier), planetShaft.IsPrescribed.ToString(),
            Frame(planeMm), N(sunTeeth), N(planetTeeth), CarrierProfile.Q(module), CarrierProfile.Q(carrierReference), CarrierProfile.Q(sunReference), CarrierProfile.Q(planetReference),
            CarrierProfile.Q(sunMount), CarrierProfile.Q(planetMount), F(toothRegistration),
            Pack(Ports.Select(p => Pack(p.Id, p.ShaftId, Frame(p.FrameInShaft), CarrierProfile.Q(p.ReadoutOffset))).ToArray()),
            Pack(Holds.Select(h => Pack(h.Id, h.ShaftId, CarrierProfile.Q(h.Position))).ToArray()), prefix?.DraftId ?? "", prefixMapping?.CanonicalRepresentation ?? "",
            carrierBodyId, sunBodyId, planetBodyId, contactPresent.ToString(), Pack(RequiredValidationDomains.ToArray())));
    }
    public OrientedShaft CarrierShaft { get; }
    public OrientedShaft SunShaft { get; }
    public CarrierLocalShaft PlanetShaft { get; }
    public OrientedFrame PlaneMm { get; }
    public int SunTeeth { get; }
    public int PlanetTeeth { get; }
    public ExactQuantity Module { get; }
    public ExactQuantity CarrierReference { get; }
    public ExactQuantity SunReference { get; }
    public ExactQuantity PlanetReference { get; }
    public ExactQuantity SunMount { get; }
    public ExactQuantity PlanetMount { get; }
    public Rational ToothRegistration { get; }
    public ReadOnlyCollection<CarrierOutputPort> Ports { get; }
    public ReadOnlyCollection<DifferentialHold> Holds { get; }
    public MechanicalDraft? Prefix { get; }
    public SourceLengthMapping? PrefixMapping { get; }
    public string CarrierBodyId { get; }
    public string SunBodyId { get; }
    public string PlanetBodyId { get; }
    public bool ContactPresent { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
}

public sealed class DifferentialRequest
{
    public DifferentialRequest(DifferentialDefinition definition, IEnumerable<string> inputPortIds)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InputPortIds = DifferentialProfile.Set(inputPortIds, x => MechanicalAuthoringProfile.IdValue(x), 2);
        RequestId = HashText(Pack(DifferentialProfile.Id, definition.DefinitionId, Pack(InputPortIds.ToArray())));
    }
    public DifferentialDefinition Definition { get; }
    public ReadOnlyCollection<string> InputPortIds { get; }
    public string RequestId { get; }
}

public sealed class DifferentialInputSnapshot
{
    public DifferentialInputSnapshot(IEnumerable<KeyValuePair<string, ExactQuantity>> values)
    {
        var items = DifferentialProfile.Set(values, p => MechanicalAuthoringProfile.IdValue(p.Key), 2);
        foreach (var p in items) DifferentialProfile.Angular(p.Value);
        Values = new ReadOnlyDictionary<string, ExactQuantity>(items.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    }
    public IReadOnlyDictionary<string, ExactQuantity> Values { get; }
}

/// <summary>Affine expression of the declared input vector. Keys are port IDs, never implicit input order.</summary>
public sealed class DifferentialLaw
{
    internal DifferentialLaw(IEnumerable<KeyValuePair<string, Rational>> coefficients, Rational offset)
    {
        var sorted = coefficients.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        foreach (var p in sorted) CarrierProfile.Derived(p.Value); CarrierProfile.Derived(offset);
        Coefficients = new ReadOnlyDictionary<string, Rational>(sorted.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)); Offset = offset;
    }
    public IReadOnlyDictionary<string, Rational> Coefficients { get; }
    public Rational Offset { get; }
    internal DifferentialLaw Then(Rational q, Rational b) => new(Coefficients.Select(p => new KeyValuePair<string, Rational>(p.Key, p.Value * q)), Offset * q + b);
    internal DifferentialLaw Minus(DifferentialLaw other) => new(Coefficients.Select(p => new KeyValuePair<string, Rational>(p.Key, p.Value - other.Coefficients[p.Key])), Offset - other.Offset);
    internal Rational Evaluate(DifferentialInputSnapshot input)
    {
        var value = Offset; foreach (var p in Coefficients) { value += p.Value * input.Values[p.Key].Value; CarrierProfile.Derived(value); } return value;
    }
}

public sealed class DifferentialPoseNode
{
    internal DifferentialPoseNode(string id, string? parent, OrientedFrame frame, DifferentialLaw rotation, string shaftId, string? bodyId = null, int? teeth = null, Rational? radius = null)
    { Id = id; ParentId = parent; Frame = frame; Rotation = rotation; ShaftId = shaftId; BodyId = bodyId; Teeth = teeth; PitchRadiusMm = radius; }
    public string Id { get; }
    public string? ParentId { get; }
    public OrientedFrame Frame { get; }
    public DifferentialLaw Rotation { get; }
    public string ShaftId { get; }
    public string? BodyId { get; }
    public int? Teeth { get; }
    public Rational? PitchRadiusMm { get; }
}

public sealed class DifferentialCoordinate
{
    internal DifferentialCoordinate(string id, DifferentialLaw? law, IEnumerable<KeyValuePair<string, Rational>> free)
    { ShaftId = id; Law = law; FreeTerms = new ReadOnlyDictionary<string, Rational>(free.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)); }
    public string ShaftId { get; }
    /// <summary>Known input part; with FreeTerms, this is NOT a determined coordinate.</summary>
    public DifferentialLaw? Law { get; }
    public IReadOnlyDictionary<string, Rational> FreeTerms { get; }
    public bool IsKnown => Law is not null && FreeTerms.Count == 0;
}

public sealed class DifferentialAnalysis
{
    internal DifferentialAnalysis(DifferentialRequest request, bool numeric, IEnumerable<DifferentialBoundary> boundary, IEnumerable<OrientedDomainCheck> checks,
        IEnumerable<MechanicalDiagnostic> diagnostics, MechanicalDeterminacy status, IEnumerable<string> ids, IEnumerable<ExactLinearRow>? rows = null,
        ExactLinearReduction? reduction = null, IEnumerable<DifferentialCoordinate>? coordinates = null, IEnumerable<DifferentialPoseNode>? poses = null,
        DifferentialLaw? carrier = null, DifferentialLaw? planet = null, DifferentialLaw? relative = null)
    {
        Request = request; IsNumericBoundaryAnalysis = numeric; Boundary = boundary.ToList().AsReadOnly(); Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal).ToList().AsReadOnly(); Status = status; CoordinateIds = ids.ToList().AsReadOnly();
        Rows = (rows ?? Array.Empty<ExactLinearRow>()).OrderBy(r => r.Id, StringComparer.Ordinal).ToList().AsReadOnly(); Reduction = reduction;
        Coordinates = (coordinates ?? Array.Empty<DifferentialCoordinate>()).ToList().AsReadOnly(); PoseNodes = (poses ?? Array.Empty<DifferentialPoseNode>()).ToList().AsReadOnly();
        CarrierCommon = carrier; PlanetCommon = planet; PlanetRelative = relative;
    }
    public DifferentialRequest Request { get; }
    public bool IsNumericBoundaryAnalysis { get; }
    public ReadOnlyCollection<DifferentialBoundary> Boundary { get; }
    public ReadOnlyCollection<string> CoordinateIds { get; }
    public ReadOnlyCollection<ExactLinearRow> Rows { get; }
    public ExactLinearReduction? Reduction { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public MechanicalDeterminacy Status { get; }
    public ReadOnlyCollection<DifferentialCoordinate> Coordinates { get; }
    public ReadOnlyCollection<DifferentialPoseNode> PoseNodes { get; }
    public DifferentialLaw? CarrierCommon { get; }
    public DifferentialLaw? PlanetCommon { get; }
    public DifferentialLaw? PlanetRelative { get; }
    public bool IsMechanicallyValid => Reduction is not null && !Reduction.HasIncompatibleRightHandSide && Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public bool IsFullyDetermined => IsMechanicallyValid && Coordinates.All(c => c.IsKnown);
    public bool CanExport => !IsNumericBoundaryAnalysis && IsFullyDetermined;
}
