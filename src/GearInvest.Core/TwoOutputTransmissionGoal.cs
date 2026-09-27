using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class TwoOutputTransmissionContract
{
    public const string Version = "0.1", Profile = "bounded-two-output-sharing-union-v1";
    public const string Lowering = "common-goal-fixed-root-or-simple-prefix-v1", Allocation = "balanced-sharing-quota-v1";
    public const string Ranking = "whole-compounds-nonendpoint-bodies-envelope-preference-teeth-id-v1";
    public const string Resources = "bounded-whole-sharing-observations-v1";
    public const string GoalFormat = "gear-invest.two-output-transmission-goal", PlanFormat = "gear-invest.two-output-sharing-plan";
    public const string ResultFormat = "gear-invest.two-output-transmission-result", ProjectFormat = "gear-invest.two-output-transmission-project";
    public const int MaxGoalBytes = 1024 * 1024, MaxCanonicalGoalChars = 65536, MaxResultBytes = 32 * 1024 * 1024;
    public const int MaxObservations = 4096, MaxObservationBytes = 8 * 1024 * 1024, MaxReturned = 32;
    public static string Id(string kind, string canonical) => GearRoutingContract.Id("two-output-" + kind, canonical);
    public static string StrategyId(TwoOutputSharingStrategy strategy) => strategy == TwoOutputSharingStrategy.RootOnly ? "root-only" : "shared-prefix";
    public static IEnumerable<string> Fingerprints(TwoOutputSharingPolicy policy)
        => new[] { TwoOutputSharingStrategy.RootOnly, TwoOutputSharingStrategy.SharedPrefix }
            .Where(s => s == TwoOutputSharingStrategy.RootOnly ? policy != TwoOutputSharingPolicy.RequiredSharedPrefix : policy != TwoOutputSharingPolicy.RootOnly)
            .Select(s => new TwoOutputStrategyPlan(s, TwoOutputStrategyApplicability.ExcludedByPolicy, 0, "").BackendFingerprint)
            .Concat(new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }.Select(TransmissionGoalContract.Fingerprint));
}

public enum TwoOutputSharingPolicy { RootOnly, RequiredSharedPrefix, AutoWithinAllowedProfiles }
public enum TwoOutputSharingStrategy { RootOnly, SharedPrefix }
public enum TwoOutputTransmissionStatus { Complete, Infeasible, IncompleteBudget, IncompleteResource, InvalidInput, Unsupported, Cancelled, Failed }
public enum TwoOutputStrategyApplicability { ExcludedByPolicy, Eligible, ProvenInfeasible, PendingBudget, InvalidInput, Unsupported, IncompleteResource }

/// <summary>Only the active shared-prefix profile owns these sites and local constraints.</summary>
public sealed class TwoOutputPrefixDomain
{
    public TwoOutputPrefixDomain(CompoundRoutingLeg routing, IEnumerable<int> splitterTeeth, IEnumerable<GearRoutePoint>? splitterSites = null,
        GearRouteGrid? splitterGrid = null, GearRouteBox? requiredSplitterRegion = null, GearRouteBox? preferredSplitterRegion = null,
        IEnumerable<GearRouteRegion>? keepOuts = null, int hypothesisLimit = SharedPrefixTransmissionContract.MaxHypotheses)
    {
        Routing = routing; SplitterTeeth = GearRoutingContract.Bounded(splitterTeeth, 16, out var t);
        HasExplicitSites = splitterSites != null; SplitterSites = GearRoutingContract.Bounded(splitterSites, 64, out var s); SplitterGrid = splitterGrid;
        RequiredSplitterRegion = requiredSplitterRegion; PreferredSplitterRegion = preferredSplitterRegion;
        KeepOuts = GearRoutingContract.Bounded(keepOuts, 16, out var k); HypothesisLimit = hypothesisLimit;
        IngestionLimitExceeded = t || s || k;
    }
    public CompoundRoutingLeg Routing { get; }
    public ReadOnlyCollection<int> SplitterTeeth { get; }
    public bool HasExplicitSites { get; }
    public ReadOnlyCollection<GearRoutePoint> SplitterSites { get; }
    public GearRouteGrid? SplitterGrid { get; }
    public GearRouteBox? RequiredSplitterRegion { get; }
    public GearRouteBox? PreferredSplitterRegion { get; }
    public ReadOnlyCollection<GearRouteRegion> KeepOuts { get; }
    public int HypothesisLimit { get; }
    public bool IngestionLimitExceeded { get; }
    public string Canonical => GearRoutingContract.Pack(Routing.Canonical,
        Routing.HasExplicitSites ? "sites" : Routing.Grid == null ? "missing-grid" : GearRoutingContract.Pack("grid", Routing.Grid.Origin.Canonical, Routing.Grid.Step.Canonical, Routing.Grid.Bounds.Canonical),
        GearRoutingContract.List(SplitterTeeth.Select(t => GearRoutingContract.Number(t))),
        HasExplicitSites ? GearRoutingContract.List(SplitterSites.Select(s => s.Canonical)) : SplitterGrid == null ? "" : GearRoutingContract.Pack(SplitterGrid.Origin.Canonical, SplitterGrid.Step.Canonical, SplitterGrid.Bounds.Canonical),
        RequiredSplitterRegion?.Canonical ?? "", PreferredSplitterRegion?.Canonical ?? "", GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)), GearRoutingContract.Number(HypothesisLimit));
}

/// <summary>One functional input/output domain; not a pair of independently authored child goals.</summary>
public sealed class TwoOutputTransmissionGoal
{
    public TwoOutputTransmissionGoal(GearRouteAnchor input, BigInteger pitchRadiusTicksPerTooth, IEnumerable<SharedDriverTransmissionOutput> outputs,
        GearRouteBox bounds, TwoOutputSharingPolicy sharingPolicy = TwoOutputSharingPolicy.AutoWithinAllowedProfiles, TwoOutputPrefixDomain? sharedPrefix = null,
        IEnumerable<int>? availableLayers = null, IEnumerable<ThreeLayerGearRouteKeepOut>? keepOuts = null,
        BigInteger unrelatedClearance = default, BigInteger keepOutClearance = default, int maximumTotalCompounds = 4, int maximumBodies = 47,
        int maximumTransmissionGears = 36, GearRouteBox? preferredRegion = null, IEnumerable<GearRouteRegion>? requiredRegions = null,
        int totalWorkBudget = 100000, int maximumReturned = 32, int inputLayer = 0,
        int observationCountLimit = TwoOutputTransmissionContract.MaxObservations, int observationByteLimit = TwoOutputTransmissionContract.MaxObservationBytes,
        string profile = TwoOutputTransmissionContract.Profile, string lowering = TwoOutputTransmissionContract.Lowering,
        string allocation = TwoOutputTransmissionContract.Allocation, string ranking = TwoOutputTransmissionContract.Ranking, string resources = TwoOutputTransmissionContract.Resources)
    {
        Input = input; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth; Bounds = bounds; SharingPolicy = sharingPolicy; SharedPrefix = sharedPrefix;
        Outputs = GearRoutingContract.Bounded(outputs, 2, out var o); AvailableLayers = GearRoutingContract.Bounded(availableLayers ?? new[] { 0, 1, 2 }, 32, out var l);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, 16, out var k); RequiredRegions = GearRoutingContract.Bounded(requiredRegions, 8, out var r);
        IngestionLimitExceeded = o || l || k || r; UnrelatedClearance = unrelatedClearance; KeepOutClearance = keepOutClearance;
        MaximumTotalCompounds = maximumTotalCompounds; MaximumBodies = maximumBodies; MaximumTransmissionGears = maximumTransmissionGears;
        PreferredRegion = preferredRegion; TotalWorkBudget = totalWorkBudget; MaximumReturned = maximumReturned; InputLayer = inputLayer;
        ObservationCountLimit = observationCountLimit; ObservationByteLimit = observationByteLimit;
        Profile = profile; Lowering = lowering; Allocation = allocation; Ranking = ranking; Resources = resources;
    }
    public GearRouteAnchor Input { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<SharedDriverTransmissionOutput> Outputs { get; }
    public GearRouteBox Bounds { get; }
    public TwoOutputSharingPolicy SharingPolicy { get; }
    public TwoOutputPrefixDomain? SharedPrefix { get; }
    public ReadOnlyCollection<int> AvailableLayers { get; }
    public ReadOnlyCollection<ThreeLayerGearRouteKeepOut> KeepOuts { get; }
    public BigInteger UnrelatedClearance { get; }
    public BigInteger KeepOutClearance { get; }
    public int MaximumTotalCompounds { get; }
    public int MaximumBodies { get; }
    public int MaximumTransmissionGears { get; }
    public GearRouteBox? PreferredRegion { get; }
    public ReadOnlyCollection<GearRouteRegion> RequiredRegions { get; }
    public int TotalWorkBudget { get; }
    public int MaximumReturned { get; }
    public int InputLayer { get; }
    public int ObservationCountLimit { get; }
    public int ObservationByteLimit { get; }
    public bool IngestionLimitExceeded { get; }
    public string Profile { get; }
    public string Lowering { get; }
    public string Allocation { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public string Canonical => GearRoutingContract.Pack(Profile, Lowering, Allocation, Ranking, Resources, SharingPolicy.ToString(), Input.Canonical,
        GearRoutingContract.Number(PitchRadiusTicksPerTooth), GearRoutingContract.List(Outputs.Select(o => o.Canonical)), Bounds.Canonical,
        SharingPolicy == TwoOutputSharingPolicy.RootOnly ? "" : SharedPrefix?.Canonical ?? "", GearRoutingContract.List(AvailableLayers.Select(x => GearRoutingContract.Number(x))),
        GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)), GearRoutingContract.Number(UnrelatedClearance), GearRoutingContract.Number(KeepOutClearance),
        PreferredRegion?.Canonical ?? "", GearRoutingContract.List(RequiredRegions.Select(r => r.Canonical)),
        GearRoutingContract.List(new[] { MaximumTotalCompounds, MaximumBodies, MaximumTransmissionGears, TotalWorkBudget, MaximumReturned, InputLayer, ObservationCountLimit, ObservationByteLimit }.Select(x => GearRoutingContract.Number(x))));
}

public sealed class TwoOutputGoalNormalization
{
    public TwoOutputGoalNormalization(TwoOutputTransmissionGoal? goal, TwoOutputTransmissionStatus status, IEnumerable<Diagnostic> diagnostics)
    { Goal = goal; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public TwoOutputTransmissionGoal? Goal { get; }
    public TwoOutputTransmissionStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Goal != null;
    public string GoalId => Goal == null ? "" : TwoOutputTransmissionContract.Id("goal", Goal.Canonical);
}

public sealed class TwoOutputStrategyPlan
{
    public TwoOutputStrategyPlan(TwoOutputSharingStrategy strategy, TwoOutputStrategyApplicability applicability, int quota, string reason,
        SharedDriverTransmissionSearchPlan? rootOnly = null, SharedPrefixTransmissionSearchPlan? sharedPrefix = null)
    { Strategy = strategy; Applicability = applicability; Quota = quota; Reason = reason; RootOnly = rootOnly; SharedPrefix = sharedPrefix; }
    public TwoOutputSharingStrategy Strategy { get; }
    public string StrategyId => TwoOutputTransmissionContract.StrategyId(Strategy);
    public TwoOutputStrategyApplicability Applicability { get; }
    public bool Included => Applicability != TwoOutputStrategyApplicability.ExcludedByPolicy;
    public int Quota { get; }
    public string Reason { get; }
    public SharedDriverTransmissionSearchPlan? RootOnly { get; }
    public SharedPrefixTransmissionSearchPlan? SharedPrefix { get; }
    public string ChildGoalId => RootOnly?.GoalId ?? SharedPrefix?.GoalId ?? "";
    public string ChildPlanId => RootOnly?.PlanId ?? SharedPrefix?.PlanId ?? "";
    public string BackendFingerprint => Strategy == TwoOutputSharingStrategy.RootOnly
        ? GearRoutingContract.Pack(SharedDriverTransmissionContract.Profile, SharedDriverTransmissionContract.Lowering, SharedDriverTransmissionContract.Allocation, SharedDriverTransmissionContract.Join, SharedDriverTransmissionContract.Ranking, SharedDriverTransmissionContract.Resources)
        : GearRoutingContract.Pack(SharedPrefixTransmissionContract.Profile, SharedPrefixTransmissionContract.Lowering, SharedPrefixTransmissionContract.Allocation, SharedPrefixTransmissionContract.Join, SharedPrefixTransmissionContract.Ranking, SharedPrefixTransmissionContract.Resources);
    public string FieldMapping => Strategy == TwoOutputSharingStrategy.RootOnly
        ? "global input->local root; fixed outputs/q/layers and resolved branch domains unchanged; all global environment; no prefix occupancy; compounds=min(global,4); idlers=min(transmissionGears,30); retained=1"
        : "global input->prefix input; splitter->suffix local root; local q=global q/r; fixed output domains/layers; all global environment; prefix-only constraints local; compounds=min(global,4); idlers=min(max(transmissionGears-1,0),35); bodies=min(global,47); retained=1";
    public string Canonical => GearRoutingContract.Pack(StrategyId, Applicability.ToString(), GearRoutingContract.Number(Quota), Reason, ChildGoalId, ChildPlanId, BackendFingerprint, FieldMapping);
}

public sealed class TwoOutputSharingSearchPlan
{
    public TwoOutputSharingSearchPlan(TwoOutputGoalNormalization normalized, IEnumerable<TwoOutputStrategyPlan> strategies, IEnumerable<Diagnostic> diagnostics)
    { Normalized = normalized; Strategies = strategies.OrderBy(s => s.Strategy).ToList().AsReadOnly(); Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public TwoOutputGoalNormalization Normalized { get; }
    public string GoalId => Normalized.GoalId;
    public ReadOnlyCollection<TwoOutputStrategyPlan> Strategies { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public int AllocatedWork => Strategies.Sum(s => s.Quota);
    public string PlanId => TwoOutputTransmissionContract.Id("plan", GearRoutingContract.Pack(GoalId, GearRoutingContract.List(Strategies.Select(s => s.Canonical))));
}
