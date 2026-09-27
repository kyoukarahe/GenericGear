using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class TwoCompoundRoutingContract
{
    public const string Profile = "bounded-two-compound-three-layer-routing-v1";
    public const string Backend = "ordered-assignment-site-pair-lazy-legs-v1";
    public const string Normalization = "ordered-integer-domains-v1";
    public const string Ranking = "serial-idlers-preference-footprint-teeth-signature-v1";
    public const string RequestFormat = "gear-invest.anchored-two-compound-routing-request";
    public const string ResultFormat = "gear-invest.anchored-two-compound-routing-result";
    public const string ProjectFormat = "gear-invest.two-compound-routing-project";
    public const int MaxSlotSites = 16, MaxSlotTeeth = 8, MaxTotalIdlers = 15, MaxPreparationChecks = 8000000;
    public const int MaxRequestBytes = 256 * 1024, MaxResultBytes = 32 * 1024 * 1024;
    public static string ContextId(string request, string candidate) => GearRoutingContract.Id("two-compound-route-context", GearRoutingContract.Pack(request, candidate));
}

/// <summary>Three logical planes only. The mask is serialized as an explicit sorted layer subset.</summary>
[Flags] public enum ThreeRouteLayers { Layer0 = 1, Layer1 = 2, Layer2 = 4, All = 7 }
public sealed class ThreeLayerGearRouteKeepOut
{
    public ThreeLayerGearRouteKeepOut(string id, GearRouteBox bounds, ThreeRouteLayers layers = ThreeRouteLayers.All)
    { Id = id; Bounds = bounds; Layers = layers; }
    public string Id { get; }
    public GearRouteBox Bounds { get; }
    public ThreeRouteLayers Layers { get; }
    public bool AppliesTo(int layer) => ((int)Layers & (1 << layer)) != 0;
    public string Canonical => GearRoutingContract.Pack(Id, Bounds.Canonical, GearRoutingContract.Number((int)Layers));
}

/// <summary>A slot's role is its position in the ordered two-slot request, never its XY order.</summary>
public sealed class CompoundRoutingSlot
{
    public CompoundRoutingSlot(IEnumerable<int> receivingTeeth, IEnumerable<int> drivingTeeth,
        IEnumerable<GearRoutePoint>? sites = null, GearRouteGrid? grid = null,
        GearRouteBox? requiredRegion = null, GearRouteBox? preferredRegion = null)
    {
        ReceivingTeeth = GearRoutingContract.Bounded(receivingTeeth, TwoCompoundRoutingContract.MaxSlotTeeth * 2, out var a);
        DrivingTeeth = GearRoutingContract.Bounded(drivingTeeth, TwoCompoundRoutingContract.MaxSlotTeeth * 2, out var b);
        Sites = GearRoutingContract.Bounded(sites, TwoCompoundRoutingContract.MaxSlotSites * 2, out var s);
        HasExplicitSites = sites != null; Grid = grid; RequiredRegion = requiredRegion; PreferredRegion = preferredRegion;
        IngestionLimitExceeded = a || b || s;
    }
    public ReadOnlyCollection<int> ReceivingTeeth { get; }
    public ReadOnlyCollection<int> DrivingTeeth { get; }
    public ReadOnlyCollection<GearRoutePoint> Sites { get; }
    public bool HasExplicitSites { get; }
    public GearRouteGrid? Grid { get; }
    public GearRouteBox? RequiredRegion { get; }
    public GearRouteBox? PreferredRegion { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(GearRoutingContract.List(ReceivingTeeth.Select(x => GearRoutingContract.Number(x))),
        GearRoutingContract.List(DrivingTeeth.Select(x => GearRoutingContract.Number(x))), GearRoutingContract.List(Sites.Select(p => p.Canonical)), RequiredRegion?.Canonical ?? "none", PreferredRegion?.Canonical ?? "none");
}

public sealed class AnchoredTwoCompoundGearRoutingRequest
{
    public AnchoredTwoCompoundGearRoutingRequest(GearRouteAnchor input, GearRouteAnchor output, Rational targetTransfer,
        BigInteger pitchRadiusTicksPerTooth, IEnumerable<CompoundRoutingSlot> compounds, IEnumerable<CompoundRoutingLeg> legs,
        int maximumTotalIdlers = 15, IEnumerable<ThreeLayerGearRouteKeepOut>? keepOuts = null, int workBudget = 100000,
        int maximumReturned = 128, int inputLayer = 0, int outputLayer = 2, string unit = "tick",
        string profile = TwoCompoundRoutingContract.Profile, string backend = TwoCompoundRoutingContract.Backend,
        string normalization = TwoCompoundRoutingContract.Normalization, string ranking = TwoCompoundRoutingContract.Ranking)
    {
        Input = input; Output = output; TargetTransfer = targetTransfer; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        Compounds = GearRoutingContract.Bounded(compounds, 2, out var c); Legs = GearRoutingContract.Bounded(legs, 3, out var l);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, GearRoutingContract.MaxKeepOuts, out var k); IngestionLimitExceeded = c || l || k;
        MaximumTotalIdlers = maximumTotalIdlers; WorkBudget = workBudget; MaximumReturned = maximumReturned;
        InputLayer = inputLayer; OutputLayer = outputLayer; Unit = unit; Profile = profile; Backend = backend; Normalization = normalization; Ranking = ranking;
    }
    public GearRouteAnchor Input { get; }
    public GearRouteAnchor Output { get; }
    public Rational TargetTransfer { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<CompoundRoutingSlot> Compounds { get; }
    public ReadOnlyCollection<CompoundRoutingLeg> Legs { get; }
    public ReadOnlyCollection<ThreeLayerGearRouteKeepOut> KeepOuts { get; }
    public int MaximumTotalIdlers { get; }
    public int WorkBudget { get; }
    public int MaximumReturned { get; }
    public int InputLayer { get; }
    public int OutputLayer { get; }
    public string Unit { get; }
    public string Profile { get; }
    public string Backend { get; }
    public string Normalization { get; }
    public string Ranking { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(Profile, Backend, Normalization, Ranking, Unit, Input.Canonical, Output.Canonical,
        GearRoutingContract.Number(InputLayer), GearRoutingContract.Number(OutputLayer), TargetTransfer.ToString(), GearRoutingContract.Number(PitchRadiusTicksPerTooth),
        GearRoutingContract.List(Compounds.Select(s => s.Canonical)), GearRoutingContract.List(Legs.Select(l => l.Canonical)),
        GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)), GearRoutingContract.Number(MaximumTotalIdlers), GearRoutingContract.Number(WorkBudget), GearRoutingContract.Number(MaximumReturned));
}

public sealed class TwoCompoundRoutingNormalization
{
    public TwoCompoundRoutingNormalization(AnchoredTwoCompoundGearRoutingRequest? request, GearRoutingStatus status, IEnumerable<Diagnostic> diagnostics, int preparationCeiling = 0)
    { Request = request; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); PreparationCeiling = preparationCeiling; }
    public AnchoredTwoCompoundGearRoutingRequest? Request { get; }
    public GearRoutingStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public int PreparationCeiling { get; }
    public bool IsValid => Request != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public string RequestId => Request == null ? "" : GearRoutingContract.Id("anchored-two-compound-routing-request", Request.Canonical);
}

public sealed class TwoCompoundToothAssignment
{
    public TwoCompoundToothAssignment(int inputTeeth, int outputTeeth, int a1, int b1, int a2, int b2)
    { InputTeeth = inputTeeth; OutputTeeth = outputTeeth; Teeth = new[] { a1, b1, a2, b2 }.ToList().AsReadOnly(); }
    public int InputTeeth { get; }
    public int OutputTeeth { get; }
    public ReadOnlyCollection<int> Teeth { get; }
    public int Receiving(int slot) => Teeth[slot * 2];
    public int Driving(int slot) => Teeth[slot * 2 + 1];
    public Rational Magnitude => new Rational(new BigInteger(InputTeeth) * Teeth[1] * Teeth[3], new BigInteger(OutputTeeth) * Teeth[0] * Teeth[2]);
    public string AssignmentId => GearRoutingContract.Id("two-compound-assignment", GearRoutingContract.Pack(TwoCompoundRoutingContract.Profile,
        GearRoutingContract.Number(InputTeeth), GearRoutingContract.Number(OutputTeeth), Magnitude.ToString(), GearRoutingContract.List(Teeth.Select(x => GearRoutingContract.Number(x)))));
}
