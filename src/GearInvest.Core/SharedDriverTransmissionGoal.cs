using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class SharedDriverTransmissionContract
{
    public const string Version = "0.1", Profile = "bounded-root-shared-two-output-transmission-v1";
    public const string Lowering = "two-required-output-canonical-planes-v1", Allocation = "balanced-output-family-fixed-quota-v1";
    public const string Join = "ordinal-observed-identity-product-root-only-v1", Ranking = "merged-compounds-idlers-preference-envelope-teeth-id-v1";
    public const string Resources = "bounded-shared-driver-pools-product-v1";
    public const string GoalFormat = "gear-invest.shared-driver-transmission-goal", ResultFormat = "gear-invest.shared-driver-transmission-result", ProjectFormat = "gear-invest.shared-driver-transmission-project";
    public const int MaxPoolCount = 4096, MaxPoolBytes = 256 * 1024, MaxPairDomain = 1000000, MaxPairDetails = 64;
    public const int MaxGoalBytes = 512 * 1024, MaxResultBytes = 4 * 1024 * 1024, MaxReturned = 32;
    public const int MaxCanonicalGoalChars = 32768, MaxMergedObservations = 4096, MaxMergedSignatureBytes = 128 * 1024;
    public const int MaxMergedIndexBytes = 4 * 1024 * 1024, ResultReserveBytes = 1024 * 1024;
    public const string RootDof = "dof:shared-input", RootAxis = "axis:shared-input", RootBody = "body:shared-input";
    public static string Pack(params string[] values) => GearRoutingContract.Pack(values);
    public static string Id(string kind, string canonical) => GearRoutingContract.Id("shared-driver-" + kind, canonical);
}

public enum SharedDriverTransmissionStatus { Complete, Infeasible, IncompleteBudget, IncompleteResource, InvalidInput, Unsupported, Cancelled, Failed }

/// <summary>A required semantic output, never a second input. Labels are presentation-only and are not generation identity.</summary>
public sealed class SharedDriverTransmissionOutput
{
    public SharedDriverTransmissionOutput(string key, GearRouteAnchor anchor, Rational targetTransfer,
        CompoundRoutingLeg commonRouting, CompoundRoutingSlot commonCompound, IEnumerable<int>? outputLayers = null,
        TransmissionOutputLayerPolicy layerPolicy = TransmissionOutputLayerPolicy.AllowedOutputLayers,
        IEnumerable<TransmissionFamily>? allowedFamilies = null, int maximumCompounds = 2, int maximumIdlers = 15,
        CompoundRoutingSlot? slot0 = null, CompoundRoutingSlot? slot1 = null,
        CompoundRoutingLeg? leg0 = null, CompoundRoutingLeg? leg1 = null, CompoundRoutingLeg? leg2 = null, string? displayLabel = null)
    {
        Key = key; Anchor = anchor; TargetTransfer = targetTransfer; DisplayLabel = displayLabel ?? key;
        Slots = new[] { slot0 ?? commonCompound, slot1 ?? commonCompound }.ToList().AsReadOnly();
        Legs = new[] { leg0 ?? commonRouting, leg1 ?? commonRouting, leg2 ?? commonRouting }.ToList().AsReadOnly();
        OutputLayers = GearRoutingContract.Bounded(outputLayers ?? new[] { 0, 1, 2 }, 32, out var l);
        AllowedFamilies = GearRoutingContract.Bounded(allowedFamilies ?? new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }, 6, out var f);
        LayerPolicy = layerPolicy; MaximumCompounds = maximumCompounds; MaximumIdlers = maximumIdlers; IngestionLimitExceeded = l || f;
    }
    public string Key { get; }
    public string DisplayLabel { get; }
    public GearRouteAnchor Anchor { get; }
    public Rational TargetTransfer { get; }
    public ReadOnlyCollection<CompoundRoutingSlot> Slots { get; }
    public ReadOnlyCollection<CompoundRoutingLeg> Legs { get; }
    public ReadOnlyCollection<int> OutputLayers { get; }
    public ReadOnlyCollection<TransmissionFamily> AllowedFamilies { get; }
    public TransmissionOutputLayerPolicy LayerPolicy { get; }
    public int MaximumCompounds { get; }
    public int MaximumIdlers { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(Key, Anchor.Canonical, TargetTransfer.ToString(), LayerPolicy.ToString(),
        GearRoutingContract.List(OutputLayers.Select(x => GearRoutingContract.Number(x))), GearRoutingContract.List(AllowedFamilies.Select(TransmissionGoalContract.FamilyId)),
        GearRoutingContract.Number(MaximumCompounds), GearRoutingContract.Number(MaximumIdlers), GearRoutingContract.List(Slots.Select(x => x.Canonical)), GearRoutingContract.List(Legs.Select(x => x.Canonical)));
}

/// <summary>Root-only sharing in one fixed physical layer coordinate system. All units are exact ticks/turns.</summary>
public sealed class SharedDriverTransmissionGoal
{
    public SharedDriverTransmissionGoal(GearRouteAnchor input, BigInteger pitchRadiusTicksPerTooth, IEnumerable<SharedDriverTransmissionOutput> outputs,
        GearRouteBox bounds, IEnumerable<int>? availableLayers = null, IEnumerable<ThreeLayerGearRouteKeepOut>? keepOuts = null,
        BigInteger unrelatedClearance = default, BigInteger keepOutClearance = default, int maximumTotalCompounds = 4, int maximumTotalIdlers = 30,
        GearRouteBox? preferredRegion = null, IEnumerable<GearRouteRegion>? requiredRegions = null,
        int totalWorkBudget = 100000, int reservedCombinationWork = 10000, int maximumReturned = 32, int inputLayer = 0,
        string profile = SharedDriverTransmissionContract.Profile, string lowering = SharedDriverTransmissionContract.Lowering,
        string allocation = SharedDriverTransmissionContract.Allocation, string join = SharedDriverTransmissionContract.Join,
        string ranking = SharedDriverTransmissionContract.Ranking, string resources = SharedDriverTransmissionContract.Resources,
        int poolCountLimit = SharedDriverTransmissionContract.MaxPoolCount, int poolByteLimit = SharedDriverTransmissionContract.MaxPoolBytes,
        int pairDomainLimit = SharedDriverTransmissionContract.MaxPairDomain)
    {
        Input = input; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth; Bounds = bounds;
        Outputs = GearRoutingContract.Bounded(outputs, 2, out var o); AvailableLayers = GearRoutingContract.Bounded(availableLayers ?? new[] { 0, 1, 2 }, 32, out var l);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, GearRoutingContract.MaxKeepOuts, out var k); RequiredRegions = GearRoutingContract.Bounded(requiredRegions, GearRoutingContract.MaxKeepOuts, out var r);
        IngestionLimitExceeded = o || l || k || r; UnrelatedClearance = unrelatedClearance; KeepOutClearance = keepOutClearance;
        MaximumTotalCompounds = maximumTotalCompounds; MaximumTotalIdlers = maximumTotalIdlers; PreferredRegion = preferredRegion;
        TotalWorkBudget = totalWorkBudget; ReservedCombinationWork = reservedCombinationWork; MaximumReturned = maximumReturned; InputLayer = inputLayer;
        Profile = profile; Lowering = lowering; Allocation = allocation; Join = join; Ranking = ranking; Resources = resources;
        PoolCountLimit = poolCountLimit; PoolByteLimit = poolByteLimit; PairDomainLimit = pairDomainLimit;
    }
    public GearRouteAnchor Input { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<SharedDriverTransmissionOutput> Outputs { get; }
    public GearRouteBox Bounds { get; }
    public ReadOnlyCollection<int> AvailableLayers { get; }
    public ReadOnlyCollection<ThreeLayerGearRouteKeepOut> KeepOuts { get; }
    public BigInteger UnrelatedClearance { get; }
    public BigInteger KeepOutClearance { get; }
    public int MaximumTotalCompounds { get; }
    public int MaximumTotalIdlers { get; }
    public GearRouteBox? PreferredRegion { get; }
    public ReadOnlyCollection<GearRouteRegion> RequiredRegions { get; }
    public int TotalWorkBudget { get; }
    public int ReservedCombinationWork { get; }
    public int GenerationWorkPool => TotalWorkBudget - ReservedCombinationWork;
    public int MaximumReturned { get; }
    public int InputLayer { get; }
    public string Profile { get; }
    public string Lowering { get; }
    public string Allocation { get; }
    public string Join { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public int PoolCountLimit { get; }
    public int PoolByteLimit { get; }
    public int PairDomainLimit { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(Profile, Lowering, Allocation, Join, Ranking, Resources, Input.Canonical,
        GearRoutingContract.Number(PitchRadiusTicksPerTooth), GearRoutingContract.Number(InputLayer), GearRoutingContract.List(Outputs.Select(o => o.Canonical)), Bounds.Canonical,
        GearRoutingContract.List(AvailableLayers.Select(x => GearRoutingContract.Number(x))), GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)),
        GearRoutingContract.Number(UnrelatedClearance), GearRoutingContract.Number(KeepOutClearance), GearRoutingContract.Number(MaximumTotalCompounds), GearRoutingContract.Number(MaximumTotalIdlers),
        PreferredRegion?.Canonical ?? "", GearRoutingContract.List(RequiredRegions.Select(r => r.Canonical)), GearRoutingContract.Number(TotalWorkBudget), GearRoutingContract.Number(ReservedCombinationWork),
        GearRoutingContract.Number(MaximumReturned), GearRoutingContract.Number(PoolCountLimit), GearRoutingContract.Number(PoolByteLimit), GearRoutingContract.Number(PairDomainLimit));
}

public sealed class SharedDriverGoalNormalization
{
    public SharedDriverGoalNormalization(SharedDriverTransmissionGoal? goal, SharedDriverTransmissionStatus status, IEnumerable<Diagnostic> diagnostics)
    { Goal = goal; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public SharedDriverTransmissionGoal? Goal { get; }
    public SharedDriverTransmissionStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Goal != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public string GoalId => Goal == null ? "" : SharedDriverTransmissionContract.Id("goal", Goal.Canonical);
}

public sealed class SharedDriverBranchPlan
{
    public SharedDriverBranchPlan(string outputKey, CompiledTransmissionSearchPlan plan) { OutputKey = outputKey; Plan = plan; }
    public string OutputKey { get; }
    public CompiledTransmissionSearchPlan Plan { get; }
    public string Canonical => GearRoutingContract.Pack(OutputKey, Plan.PlanId);
}

public sealed class SharedDriverTransmissionSearchPlan
{
    public SharedDriverTransmissionSearchPlan(SharedDriverGoalNormalization normalized, SharedDriverTransmissionStatus status, IEnumerable<SharedDriverBranchPlan> branches, IEnumerable<Diagnostic> diagnostics)
    { Normalized = normalized; Status = status; Branches = branches.OrderBy(b => b.OutputKey, StringComparer.Ordinal).ToList().AsReadOnly(); Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public SharedDriverGoalNormalization Normalized { get; }
    public string GoalId => Normalized.GoalId;
    public string PlanId => SharedDriverTransmissionContract.Id("plan", GearRoutingContract.Pack(GoalId, Status.ToString(), GearRoutingContract.List(Branches.Select(b => b.Canonical))));
    public SharedDriverTransmissionStatus Status { get; }
    public bool IsSupported => Normalized.IsValid && Status == SharedDriverTransmissionStatus.Complete;
    public ReadOnlyCollection<SharedDriverBranchPlan> Branches { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public int AllocatedGenerationWork => Branches.Sum(b => b.Plan.AllocatedWork);
}
