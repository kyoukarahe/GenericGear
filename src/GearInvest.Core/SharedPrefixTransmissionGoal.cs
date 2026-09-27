using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class SharedPrefixTransmissionContract
{
    public const string Version = "0.1", Profile = "bounded-shared-prefix-two-output-transmission-v1";
    public const string Lowering = "teeth-xy-sign-exact-quotient-hypotheses-v1", Allocation = "balanced-hypothesis-prefix-and-output-family-v1";
    public const string Join = "ordinal-hypothesis-prefix-suffix-triples-v1", Ranking = "unique-compounds-transmission-gears-preference-envelope-teeth-id-v1";
    public const string Resources = "bounded-shared-prefix-preparation-pools-triples-v1";
    public const string GoalFormat = "gear-invest.shared-prefix-transmission-goal", ResultFormat = "gear-invest.shared-prefix-transmission-result", ProjectFormat = "gear-invest.shared-prefix-transmission-project";
    public const int MaxHypotheses = 8, MaxSplitterSites = 32, MaxSplitterTeeth = 8, MaxPreparationChecks = 34000000;
    public const int MaxPoolCount = 4096, MaxPoolBytes = 256 * 1024, MaxTotalPoolBytes = 512 * 1024, MaxTripleDomain = 1000000;
    public const int MaxGoalBytes = 512 * 1024, MaxCanonicalGoalChars = 32768, MaxResultBytes = 4 * 1024 * 1024, ResultReserveBytes = 1024 * 1024;
    public const int MaxReturned = 32, MaxDetails = 64, MaxMergedIndex = 4096, MaxBodies = 47;
    public const string RootDof = "dof:actual-input", RootAxis = "axis:actual-input", RootBody = "body:actual-input";
    public const string SplitterDof = "dof:shared-splitter", SplitterAxis = "axis:shared-splitter", SplitterBody = "body:shared-splitter";
    public static string Id(string kind, string canonical) => GearRoutingContract.Id("shared-prefix-" + kind, canonical);
}

public enum SharedPrefixTransmissionStatus { Complete, Infeasible, IncompleteBudget, IncompleteResource, InvalidInput, Unsupported, Cancelled, Failed }

/// <summary>Required nonempty prefix. Actual-input output targets are never local splitter targets.</summary>
public sealed class SharedPrefixTransmissionGoal
{
    public SharedPrefixTransmissionGoal(GearRouteAnchor input, BigInteger pitchRadiusTicksPerTooth, IEnumerable<SharedDriverTransmissionOutput> outputs,
        CompoundRoutingLeg prefix, IEnumerable<int> splitterTeeth, GearRouteBox bounds, IEnumerable<GearRoutePoint>? splitterSites = null, GearRouteGrid? splitterGrid = null,
        GearRouteBox? requiredSplitterRegion = null, GearRouteBox? preferredSplitterRegion = null, IEnumerable<GearRouteRegion>? prefixKeepOuts = null,
        IEnumerable<int>? availableLayers = null, IEnumerable<ThreeLayerGearRouteKeepOut>? keepOuts = null,
        BigInteger unrelatedClearance = default, BigInteger keepOutClearance = default, int maximumTotalCompounds = 4, int maximumTotalIdlers = 35, int maximumBodies = 47,
        GearRouteBox? preferredRegion = null, IEnumerable<GearRouteRegion>? requiredRegions = null,
        int totalWorkBudget = 100000, int reservedPrefixWork = 30000, int reservedJoinWork = 10000, int maximumReturned = 32,
        int hypothesisLimit = SharedPrefixTransmissionContract.MaxHypotheses, int poolCountLimit = SharedPrefixTransmissionContract.MaxPoolCount,
        int poolByteLimit = SharedPrefixTransmissionContract.MaxPoolBytes, int totalPoolByteLimit = SharedPrefixTransmissionContract.MaxTotalPoolBytes,
        int tripleDomainLimit = SharedPrefixTransmissionContract.MaxTripleDomain, int inputLayer = 0,
        string profile = SharedPrefixTransmissionContract.Profile, string lowering = SharedPrefixTransmissionContract.Lowering,
        string allocation = SharedPrefixTransmissionContract.Allocation, string join = SharedPrefixTransmissionContract.Join,
        string ranking = SharedPrefixTransmissionContract.Ranking, string resources = SharedPrefixTransmissionContract.Resources)
    {
        Input = input; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth; Prefix = prefix; Bounds = bounds;
        Outputs = GearRoutingContract.Bounded(outputs, 2, out var o); SplitterTeeth = GearRoutingContract.Bounded(splitterTeeth, 16, out var t);
        HasExplicitSplitterSites = splitterSites != null; SplitterSites = GearRoutingContract.Bounded(splitterSites, 64, out var s); SplitterGrid = splitterGrid;
        RequiredSplitterRegion = requiredSplitterRegion; PreferredSplitterRegion = preferredSplitterRegion;
        PrefixKeepOuts = GearRoutingContract.Bounded(prefixKeepOuts, 16, out var p); AvailableLayers = GearRoutingContract.Bounded(availableLayers ?? new[] { 0, 1, 2 }, 32, out var l);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, 16, out var k); RequiredRegions = GearRoutingContract.Bounded(requiredRegions, 8, out var r);
        IngestionLimitExceeded = o || t || s || p || l || k || r; UnrelatedClearance = unrelatedClearance; KeepOutClearance = keepOutClearance;
        MaximumTotalCompounds = maximumTotalCompounds; MaximumTotalIdlers = maximumTotalIdlers; MaximumBodies = maximumBodies; PreferredRegion = preferredRegion;
        TotalWorkBudget = totalWorkBudget; ReservedPrefixWork = reservedPrefixWork; ReservedJoinWork = reservedJoinWork; MaximumReturned = maximumReturned;
        HypothesisLimit = hypothesisLimit; PoolCountLimit = poolCountLimit; PoolByteLimit = poolByteLimit; TotalPoolByteLimit = totalPoolByteLimit; TripleDomainLimit = tripleDomainLimit;
        InputLayer = inputLayer; Profile = profile; Lowering = lowering; Allocation = allocation; Join = join; Ranking = ranking; Resources = resources;
    }
    public GearRouteAnchor Input { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<SharedDriverTransmissionOutput> Outputs { get; }
    public CompoundRoutingLeg Prefix { get; }
    public ReadOnlyCollection<int> SplitterTeeth { get; }
    public bool HasExplicitSplitterSites { get; }
    public ReadOnlyCollection<GearRoutePoint> SplitterSites { get; }
    public GearRouteGrid? SplitterGrid { get; }
    public GearRouteBox? RequiredSplitterRegion { get; }
    public GearRouteBox? PreferredSplitterRegion { get; }
    public ReadOnlyCollection<GearRouteRegion> PrefixKeepOuts { get; }
    public GearRouteBox Bounds { get; }
    public ReadOnlyCollection<int> AvailableLayers { get; }
    public ReadOnlyCollection<ThreeLayerGearRouteKeepOut> KeepOuts { get; }
    public BigInteger UnrelatedClearance { get; }
    public BigInteger KeepOutClearance { get; }
    public int MaximumTotalCompounds { get; }
    public int MaximumTotalIdlers { get; }
    public int MaximumBodies { get; }
    public GearRouteBox? PreferredRegion { get; }
    public ReadOnlyCollection<GearRouteRegion> RequiredRegions { get; }
    public int TotalWorkBudget { get; }
    public int ReservedPrefixWork { get; }
    public int ReservedJoinWork { get; }
    public int SuffixWorkPool => TotalWorkBudget - ReservedPrefixWork - ReservedJoinWork;
    public int MaximumReturned { get; }
    public int HypothesisLimit { get; }
    public int PoolCountLimit { get; }
    public int PoolByteLimit { get; }
    public int TotalPoolByteLimit { get; }
    public int TripleDomainLimit { get; }
    public int InputLayer { get; }
    public bool IngestionLimitExceeded { get; }
    public string Profile { get; }
    public string Lowering { get; }
    public string Allocation { get; }
    public string Join { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public string Canonical => GearRoutingContract.Pack(Profile, Lowering, Allocation, Join, Ranking, Resources, Input.Canonical,
        GearRoutingContract.Number(PitchRadiusTicksPerTooth), GearRoutingContract.List(Outputs.Select(o => o.Canonical)), Prefix.Canonical,
        GearRoutingContract.List(SplitterTeeth.Select(t => GearRoutingContract.Number(t))), GearRoutingContract.List(SplitterSites.Select(s => s.Canonical)),
        RequiredSplitterRegion?.Canonical ?? "", PreferredSplitterRegion?.Canonical ?? "", GearRoutingContract.List(PrefixKeepOuts.Select(k => k.Canonical)), Bounds.Canonical,
        GearRoutingContract.List(AvailableLayers.Select(l => GearRoutingContract.Number(l))), GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)),
        GearRoutingContract.Number(UnrelatedClearance), GearRoutingContract.Number(KeepOutClearance), PreferredRegion?.Canonical ?? "", GearRoutingContract.List(RequiredRegions.Select(r => r.Canonical)),
        GearRoutingContract.List(new[] { MaximumTotalCompounds, MaximumTotalIdlers, MaximumBodies, TotalWorkBudget, ReservedPrefixWork, ReservedJoinWork,
            MaximumReturned, HypothesisLimit, PoolCountLimit, PoolByteLimit, TotalPoolByteLimit, TripleDomainLimit, InputLayer }.Select(n => GearRoutingContract.Number(n))));
}

public sealed class SharedPrefixGoalNormalization
{
    public SharedPrefixGoalNormalization(SharedPrefixTransmissionGoal? goal, SharedPrefixTransmissionStatus status, IEnumerable<Diagnostic> diagnostics)
    { Goal = goal; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public SharedPrefixTransmissionGoal? Goal { get; }
    public SharedPrefixTransmissionStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Goal != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public string GoalId => Goal == null ? "" : SharedPrefixTransmissionContract.Id("goal", Goal.Canonical);
}

public sealed class SharedPrefixHypothesis
{
    public SharedPrefixHypothesis(GearRouteAnchor splitter, Rational transfer, CompiledTransmissionSearchPlan? prefix, SharedDriverTransmissionSearchPlan? suffix, string proof = "")
    { Splitter = splitter; Transfer = transfer; Prefix = prefix; Suffix = suffix; ExclusionProof = proof; }
    public GearRouteAnchor Splitter { get; }
    public Rational Transfer { get; }
    public CompiledTransmissionSearchPlan? Prefix { get; }
    public SharedDriverTransmissionSearchPlan? Suffix { get; }
    public string ExclusionProof { get; }
    public string HypothesisId => SharedPrefixTransmissionContract.Id("hypothesis", GearRoutingContract.Pack(Splitter.Canonical, Transfer.ToString()));
    public string Canonical => GearRoutingContract.Pack(HypothesisId, ExclusionProof, Prefix?.PlanId ?? "", Suffix?.PlanId ?? "");
}

public sealed class SharedPrefixTransmissionSearchPlan
{
    public SharedPrefixTransmissionSearchPlan(SharedPrefixGoalNormalization normalized, SharedPrefixTransmissionStatus status, IEnumerable<SharedPrefixHypothesis> hypotheses,
        int potentialHypotheses, string limitingReason, IEnumerable<Diagnostic> diagnostics)
    { Normalized = normalized; Status = status; Hypotheses = hypotheses.ToList().AsReadOnly(); PotentialHypotheses = potentialHypotheses; LimitingReason = limitingReason; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public SharedPrefixGoalNormalization Normalized { get; }
    public SharedPrefixTransmissionStatus Status { get; }
    public ReadOnlyCollection<SharedPrefixHypothesis> Hypotheses { get; }
    public int PotentialHypotheses { get; }
    public string LimitingReason { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsSupported => Normalized.IsValid && Status == SharedPrefixTransmissionStatus.Complete;
    public string GoalId => Normalized.GoalId;
    public string PlanId => SharedPrefixTransmissionContract.Id("plan", GearRoutingContract.Pack(GoalId, Status.ToString(), GearRoutingContract.Number(PotentialHypotheses), LimitingReason, GearRoutingContract.List(Hypotheses.Select(h => h.Canonical))));
    public int AllocatedPrefixWork => Hypotheses.Sum(h => h.Prefix?.AllocatedWork ?? 0);
    public int AllocatedSuffixWork => Hypotheses.Sum(h => h.Suffix?.AllocatedGenerationWork ?? 0);
    public long PreparationCeiling => Hypotheses.Sum(h => (long)(h.Prefix?.PreparationCeiling ?? 0) + (h.Suffix?.Branches.Sum(b => (long)b.Plan.PreparationCeiling) ?? 0));
}
