using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class CompoundRoutingContract
{
    public const string Profile = "bounded-one-compound-two-layer-routing-v1";
    public const string Backend = "endpoint-pair-site-lazy-contact-dfs-v1";
    public const string Ranking = "total-idlers-preference-footprint-teeth-signature-v1";
    public const string RequestFormat = "gear-invest.anchored-compound-routing-request";
    public const string ResultFormat = "gear-invest.anchored-compound-routing-result";
    public const string ProjectFormat = "gear-invest.compound-routing-project";
    public const int MaxCompoundSites = 32, MaxTotalIdlers = 10, MaxPreparationChecks = 8000000;
    public const int MaxRequestBytes = 256 * 1024, MaxResultBytes = 32 * 1024 * 1024;
    public static string ContextId(string requestId, string candidateId) => GearRoutingContract.Id("compound-route-context", GearRoutingContract.Pack(requestId, candidateId));
}

/// <summary>Logical plane mask, not physical spacing. Omitted scope means both planes.</summary>
[Flags] public enum GearRouteLayers { Layer0 = 1, Layer1 = 2, Both = 3 }
public sealed class LayeredGearRouteKeepOut
{
    public LayeredGearRouteKeepOut(string id, GearRouteBox bounds, GearRouteLayers layers = GearRouteLayers.Both)
    { Id = id; Bounds = bounds; Layers = layers; }
    public string Id { get; }
    public GearRouteBox Bounds { get; }
    public GearRouteLayers Layers { get; }
    public bool AppliesTo(int layer) => ((int)Layers & (1 << layer)) != 0;
    public string Canonical => GearRoutingContract.Pack(Id, Bounds.Canonical, GearRoutingContract.Number((int)Layers));
}

public sealed class CompoundRoutingLeg
{
    public CompoundRoutingLeg(GearRouteBox bounds, IEnumerable<GearRoutePoint>? sites = null, GearRouteGrid? grid = null,
        IEnumerable<int>? idlerTeeth = null, int minIdlers = 0, int maxIdlers = 5,
        BigInteger unrelatedClearance = default, BigInteger keepOutClearance = default,
        IEnumerable<GearRouteRegion>? requiredRegions = null, GearRouteBox? preferredRegion = null)
    {
        Bounds = bounds; HasExplicitSites = sites != null; Grid = grid;
        Sites = GearRoutingContract.Bounded(sites, GearRoutingContract.MaxSites * 2, out var s);
        IdlerTeeth = GearRoutingContract.Bounded(idlerTeeth ?? new[] { 10 }, GearRoutingContract.MaxToothOptions * 2, out var t);
        RequiredRegions = GearRoutingContract.Bounded(requiredRegions, GearRoutingContract.MaxRequired, out var r);
        IngestionLimitExceeded = s || t || r; MinIdlers = minIdlers; MaxIdlers = maxIdlers;
        UnrelatedClearance = unrelatedClearance; KeepOutClearance = keepOutClearance; PreferredRegion = preferredRegion;
    }
    public GearRouteBox Bounds { get; }
    public bool HasExplicitSites { get; }
    public ReadOnlyCollection<GearRoutePoint> Sites { get; }
    public GearRouteGrid? Grid { get; }
    public ReadOnlyCollection<int> IdlerTeeth { get; }
    public int MinIdlers { get; }
    public int MaxIdlers { get; }
    public BigInteger UnrelatedClearance { get; }
    public BigInteger KeepOutClearance { get; }
    public ReadOnlyCollection<GearRouteRegion> RequiredRegions { get; }
    public GearRouteBox? PreferredRegion { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(Bounds.Canonical, GearRoutingContract.List(Sites.Select(p => p.Canonical)),
        GearRoutingContract.List(IdlerTeeth.Select(n => GearRoutingContract.Number(n))), GearRoutingContract.Number(MinIdlers), GearRoutingContract.Number(MaxIdlers),
        GearRoutingContract.Number(UnrelatedClearance), GearRoutingContract.Number(KeepOutClearance),
        GearRoutingContract.List(RequiredRegions.Select(r => r.Canonical)), PreferredRegion?.Canonical ?? "none");
}

/// <summary>Finite raw domain; normalize before search. Input/output roles fix planes 0 and 1.</summary>
public sealed class AnchoredCompoundGearRoutingRequest
{
    public AnchoredCompoundGearRoutingRequest(GearRouteAnchor input, GearRouteAnchor output, Rational targetTransfer,
        BigInteger pitchRadiusTicksPerTooth, IEnumerable<int> receivingTeeth, IEnumerable<int> drivingTeeth,
        CompoundRoutingLeg inputLeg, CompoundRoutingLeg outputLeg, IEnumerable<GearRoutePoint>? compoundSites = null,
        GearRouteGrid? compoundGrid = null, int maximumTotalIdlers = 10, IEnumerable<LayeredGearRouteKeepOut>? keepOuts = null,
        GearRouteBox? requiredCompoundRegion = null, GearRouteBox? preferredCompoundRegion = null,
        int workBudget = 100000, int maximumReturned = 128, int inputLayer = 0, int outputLayer = 1,
        string unit = "tick", string profile = CompoundRoutingContract.Profile, string backend = CompoundRoutingContract.Backend,
        string ranking = CompoundRoutingContract.Ranking)
    {
        Input = input; Output = output; TargetTransfer = targetTransfer; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        ReceivingTeeth = GearRoutingContract.Bounded(receivingTeeth, GearRoutingContract.MaxToothOptions * 2, out var a);
        DrivingTeeth = GearRoutingContract.Bounded(drivingTeeth, GearRoutingContract.MaxToothOptions * 2, out var b);
        HasExplicitCompoundSites = compoundSites != null; CompoundGrid = compoundGrid;
        CompoundSites = GearRoutingContract.Bounded(compoundSites, CompoundRoutingContract.MaxCompoundSites * 2, out var s);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, GearRoutingContract.MaxKeepOuts, out var k);
        IngestionLimitExceeded = a || b || s || k; InputLeg = inputLeg; OutputLeg = outputLeg; MaximumTotalIdlers = maximumTotalIdlers;
        RequiredCompoundRegion = requiredCompoundRegion; PreferredCompoundRegion = preferredCompoundRegion;
        WorkBudget = workBudget; MaximumReturned = maximumReturned; InputLayer = inputLayer; OutputLayer = outputLayer;
        Unit = unit; Profile = profile; Backend = backend; Ranking = ranking;
    }
    public GearRouteAnchor Input { get; }
    public GearRouteAnchor Output { get; }
    public Rational TargetTransfer { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<int> ReceivingTeeth { get; }
    public ReadOnlyCollection<int> DrivingTeeth { get; }
    public CompoundRoutingLeg InputLeg { get; }
    public CompoundRoutingLeg OutputLeg { get; }
    public ReadOnlyCollection<GearRoutePoint> CompoundSites { get; }
    public bool HasExplicitCompoundSites { get; }
    public GearRouteGrid? CompoundGrid { get; }
    public int MaximumTotalIdlers { get; }
    public ReadOnlyCollection<LayeredGearRouteKeepOut> KeepOuts { get; }
    public GearRouteBox? RequiredCompoundRegion { get; }
    public GearRouteBox? PreferredCompoundRegion { get; }
    public int WorkBudget { get; }
    public int MaximumReturned { get; }
    public int InputLayer { get; }
    public int OutputLayer { get; }
    public string Unit { get; }
    public string Profile { get; }
    public string Backend { get; }
    public string Ranking { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(Profile, Backend, Ranking, Unit, Input.Canonical, Output.Canonical,
        GearRoutingContract.Number(InputLayer), GearRoutingContract.Number(OutputLayer), TargetTransfer.ToString(), GearRoutingContract.Number(PitchRadiusTicksPerTooth),
        GearRoutingContract.List(ReceivingTeeth.Select(n => GearRoutingContract.Number(n))), GearRoutingContract.List(DrivingTeeth.Select(n => GearRoutingContract.Number(n))),
        GearRoutingContract.List(CompoundSites.Select(p => p.Canonical)), InputLeg.Canonical, OutputLeg.Canonical, GearRoutingContract.Number(MaximumTotalIdlers),
        GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)), RequiredCompoundRegion?.Canonical ?? "none", PreferredCompoundRegion?.Canonical ?? "none",
        GearRoutingContract.Number(WorkBudget), GearRoutingContract.Number(MaximumReturned));
}

public sealed class CompoundRoutingNormalization
{
    public CompoundRoutingNormalization(AnchoredCompoundGearRoutingRequest? request, GearRoutingStatus status, IEnumerable<Diagnostic> diagnostics, int preparationCeiling = 0)
    { Request = request; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); PreparationCeiling = preparationCeiling; }
    public AnchoredCompoundGearRoutingRequest? Request { get; }
    public GearRoutingStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Request != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public int PreparationCeiling { get; }
    public string RequestId => Request == null ? "" : GearRoutingContract.Id("anchored-compound-routing-request", Request.Canonical);
}

public sealed class AnchoredCompoundToothPair
{
    public AnchoredCompoundToothPair(int inputTeeth, int outputTeeth, int receivingTeeth, int drivingTeeth)
    { InputTeeth = inputTeeth; OutputTeeth = outputTeeth; ReceivingTeeth = receivingTeeth; DrivingTeeth = drivingTeeth; }
    public int InputTeeth { get; }
    public int OutputTeeth { get; }
    public int ReceivingTeeth { get; }
    public int DrivingTeeth { get; }
    public Rational Magnitude => new Rational(new BigInteger(InputTeeth) * DrivingTeeth, new BigInteger(ReceivingTeeth) * OutputTeeth);
    public string PairId => GearRoutingContract.Id("anchored-compound-pair", GearRoutingContract.Pack(CompoundRoutingContract.Profile,
        GearRoutingContract.Number(InputTeeth), GearRoutingContract.Number(OutputTeeth), GearRoutingContract.Number(ReceivingTeeth), GearRoutingContract.Number(DrivingTeeth)));
}
