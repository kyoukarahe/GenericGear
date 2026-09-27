using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class TransmissionGoalContract
{
    public const string Version = "0.1", Profile = "bounded-three-family-transmission-goal-v1";
    public const string Lowering = "fixed-canonical-planes-resolved-domains-v1", Allocation = "balanced-fixed-quota-v1";
    public const string Ranking = "compound-idlers-goal-preference-footprint-teeth-id-v1";
    public const string Resources = "bounded-goal-preparation-and-observations-v1";
    public const string GoalFormat = "gear-invest.transmission-goal", ResultFormat = "gear-invest.transmission-search-result", ProjectFormat = "gear-invest.transmission-goal-project";
    // Work is an operating pool of child-defined units, not a CPU/time equivalence.
    public const int MaxWork = 1000000, MaxPreparationChecks = 17000000, MaxUniqueObservations = 16384;
    // The reused canonical envelope has a four-MiB ceiling; do not advertise a larger writable result.
    public const int MaxGoalBytes = 512 * 1024, MaxResultBytes = 4 * 1024 * 1024;
    public static string FamilyId(TransmissionFamily family) => family switch {
        TransmissionFamily.SimpleIdler => "simple-idler", TransmissionFamily.OneCompound => "one-compound",
        TransmissionFamily.TwoCompound => "two-compound", _ => throw new ArgumentOutOfRangeException(nameof(family)) };
    public static string Fingerprint(TransmissionFamily family) => family switch {
        TransmissionFamily.SimpleIdler => GearRoutingContract.Pack(GearRoutingContract.Profile, GearRoutingContract.Backend, GearRoutingContract.Ranking),
        TransmissionFamily.OneCompound => GearRoutingContract.Pack(CompoundRoutingContract.Profile, CompoundRoutingContract.Backend, CompoundRoutingContract.Ranking),
        TransmissionFamily.TwoCompound => GearRoutingContract.Pack(TwoCompoundRoutingContract.Profile, TwoCompoundRoutingContract.Backend, TwoCompoundRoutingContract.Normalization, TwoCompoundRoutingContract.Ranking),
        _ => throw new ArgumentOutOfRangeException(nameof(family)) };
    public static string ContextId(string goal, string request, string mechanism) => GearRoutingContract.Id("transmission-goal-context", GearRoutingContract.Pack(goal, request, mechanism));
}

public enum TransmissionFamily { SimpleIdler, OneCompound, TwoCompound }
public enum TransmissionOutputLayerPolicy { FixedOutputLayer, AllowedOutputLayers }
public enum TransmissionGoalStatus { Complete, Infeasible, IncompleteBudget, InvalidInput, Unsupported, Cancelled, Failed }
public enum TransmissionApplicability { ExcludedByGoal, ProvenInfeasible, Eligible, Unsupported, Malformed }
public enum TransmissionFamilyStatus { ExcludedByGoal, ProvenInfeasible, PendingBudget, Complete, Infeasible, IncompleteBudget, Unsupported, Cancelled, Failed }

/// <summary>Function first. Common domains are inherited by every slot/leg unless explicitly overridden.
/// Normalize resolves all inheritance into two ordered slots and three ordered legs; no route is supplied.</summary>
public sealed class TransmissionGoal
{
    public TransmissionGoal(GearRouteAnchor input, GearRouteAnchor output, Rational targetTransfer, BigInteger pitchRadiusTicksPerTooth,
        CompoundRoutingLeg commonRouting, CompoundRoutingSlot commonCompound, int maximumCompounds = 2, int maximumTotalIdlers = 15,
        IEnumerable<int>? availableLayers = null, IEnumerable<int>? outputLayers = null,
        TransmissionOutputLayerPolicy layerPolicy = TransmissionOutputLayerPolicy.AllowedOutputLayers, int maximumLayerCount = 3,
        IEnumerable<TransmissionFamily>? allowedFamilies = null, IEnumerable<ThreeLayerGearRouteKeepOut>? keepOuts = null,
        CompoundRoutingSlot? slot0 = null, CompoundRoutingSlot? slot1 = null,
        CompoundRoutingLeg? leg0 = null, CompoundRoutingLeg? leg1 = null, CompoundRoutingLeg? leg2 = null,
        int workBudget = 300000, int maximumReturned = 128, int inputLayer = 0,
        string profile = TransmissionGoalContract.Profile, string lowering = TransmissionGoalContract.Lowering,
        string allocation = TransmissionGoalContract.Allocation, string ranking = TransmissionGoalContract.Ranking,
        string resources = TransmissionGoalContract.Resources, string unit = "tick")
    {
        Input = input; Output = output; TargetTransfer = targetTransfer; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        Slots = new[] { slot0 ?? commonCompound, slot1 ?? commonCompound }.ToList().AsReadOnly();
        Legs = new[] { leg0 ?? commonRouting, leg1 ?? commonRouting, leg2 ?? commonRouting }.ToList().AsReadOnly();
        AvailableLayers = GearRoutingContract.Bounded(availableLayers ?? new[] { 0, 1, 2 }, 32, out var a);
        OutputLayers = GearRoutingContract.Bounded(outputLayers ?? new[] { 0, 1, 2 }, 32, out var o);
        AllowedFamilies = GearRoutingContract.Bounded(allowedFamilies ?? new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }, 6, out var f);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, GearRoutingContract.MaxKeepOuts, out var k);
        IngestionLimitExceeded = a || o || f || k; MaximumCompounds = maximumCompounds; MaximumTotalIdlers = maximumTotalIdlers;
        LayerPolicy = layerPolicy; MaximumLayerCount = maximumLayerCount; InputLayer = inputLayer; WorkBudget = workBudget; MaximumReturned = maximumReturned;
        Profile = profile; Lowering = lowering; Allocation = allocation; Ranking = ranking; Resources = resources; Unit = unit;
    }
    public GearRouteAnchor Input { get; }
    public GearRouteAnchor Output { get; }
    public Rational TargetTransfer { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public ReadOnlyCollection<CompoundRoutingSlot> Slots { get; }
    public ReadOnlyCollection<CompoundRoutingLeg> Legs { get; }
    public ReadOnlyCollection<int> AvailableLayers { get; }
    public ReadOnlyCollection<int> OutputLayers { get; }
    public ReadOnlyCollection<TransmissionFamily> AllowedFamilies { get; }
    public ReadOnlyCollection<ThreeLayerGearRouteKeepOut> KeepOuts { get; }
    public TransmissionOutputLayerPolicy LayerPolicy { get; }
    public int MaximumLayerCount { get; }
    public int MaximumCompounds { get; }
    public int MaximumTotalIdlers { get; }
    public int InputLayer { get; }
    public int WorkBudget { get; }
    public int MaximumReturned { get; }
    public bool IngestionLimitExceeded { get; }
    public string Profile { get; }
    public string Lowering { get; }
    public string Allocation { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public string Unit { get; }
    // Only normalized objects enter identity. Raw grid/list syntax is not canonical truth.
    public string Canonical => GearRoutingContract.Pack(Profile, Lowering, Allocation, Ranking, Resources, Unit,
        Input.Canonical, Output.Canonical, TargetTransfer.ToString(), GearRoutingContract.Number(PitchRadiusTicksPerTooth),
        GearRoutingContract.Number(InputLayer), GearRoutingContract.Number(MaximumCompounds), GearRoutingContract.Number(MaximumTotalIdlers),
        LayerPolicy.ToString(), GearRoutingContract.Number(MaximumLayerCount), GearRoutingContract.List(AvailableLayers.Select(x => GearRoutingContract.Number(x))),
        GearRoutingContract.List(OutputLayers.Select(x => GearRoutingContract.Number(x))), GearRoutingContract.List(AllowedFamilies.Select(TransmissionGoalContract.FamilyId)),
        GearRoutingContract.List(Slots.Select(s => s.Canonical)), GearRoutingContract.List(Legs.Select(l => l.Canonical)),
        GearRoutingContract.List(KeepOuts.Select(k => k.Canonical)), GearRoutingContract.Number(WorkBudget), GearRoutingContract.Number(MaximumReturned));
}

public sealed class TransmissionGoalNormalization
{
    public TransmissionGoalNormalization(TransmissionGoal? goal, TransmissionGoalStatus status, IEnumerable<Diagnostic> diagnostics)
    { Goal = goal; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public TransmissionGoal? Goal { get; }
    public TransmissionGoalStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Goal != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public string GoalId => Goal == null ? "" : GearRoutingContract.Id("transmission-goal", Goal.Canonical);
}

/// <summary>Exactly one typed request for an included supported family. Exclusions never masquerade as search.</summary>
public sealed class TransmissionChildPlan
{
    public TransmissionChildPlan(TransmissionFamily family, TransmissionApplicability applicability, string reason, int quota, int proofWork, int preparationCeiling,
        AnchoredGearRoutingRequest? simple = null, AnchoredCompoundGearRoutingRequest? one = null, AnchoredTwoCompoundGearRoutingRequest? two = null)
    { Family = family; Applicability = applicability; Reason = reason; Quota = quota; ProofWork = proofWork; PreparationCeiling = preparationCeiling;
        Simple = simple; One = one; Two = two; }
    public TransmissionFamily Family { get; }
    public string FamilyId => TransmissionGoalContract.FamilyId(Family);
    public string BackendFingerprint => TransmissionGoalContract.Fingerprint(Family);
    public TransmissionApplicability Applicability { get; }
    public string Reason { get; }
    public int Quota { get; }
    public int ProofWork { get; }
    public int PreparationCeiling { get; }
    public AnchoredGearRoutingRequest? Simple { get; }
    public AnchoredCompoundGearRoutingRequest? One { get; }
    public AnchoredTwoCompoundGearRoutingRequest? Two { get; }
    public int OutputLayer => (int)Family;
    public string RequestId => Simple != null ? GearRoutingContract.Id("anchored-gear-routing-request", Simple.Canonical) :
        One != null ? GearRoutingContract.Id("anchored-compound-routing-request", One.Canonical) :
        Two != null ? GearRoutingContract.Id("anchored-two-compound-routing-request", Two.Canonical) : "";
    public string Canonical => GearRoutingContract.Pack(FamilyId, BackendFingerprint, Applicability.ToString(), Reason,
        GearRoutingContract.Number(Quota), GearRoutingContract.Number(ProofWork), GearRoutingContract.Number(PreparationCeiling), RequestId);
    public ReadOnlyCollection<string> FieldMappings => new[] {
        "input/output/targetTransfer/pitchRadiusTicksPerTooth -> unchanged child anchors/transfer/scale",
        "layer policy -> fixed canonical input 0, output " + GearRoutingContract.Number(OutputLayer) + "; no plane remapping",
        "resolved legs[0.." + GearRoutingContract.Number(OutputLayer) + "] -> corresponding ordered child legs; simple uses leg 0",
        "resolved slots[0..compoundCount) -> corresponding ordered compound tooth/site/Required/Preferred domains",
        "keepOuts -> exact intersection with used logical layers; absent planes have no bodies",
        "maximumTotalIdlers -> intersection with topology's intrinsic summed per-leg maximum",
        "allocated quota -> child work budget; child return cap 1 is not the common candidate stream",
        "all normalized domains retain the backend's documented endpoint-site normalization"
    }.ToList().AsReadOnly();
}

public sealed class CompiledTransmissionSearchPlan
{
    public CompiledTransmissionSearchPlan(TransmissionGoalNormalization normalized, TransmissionGoalStatus status,
        IEnumerable<TransmissionChildPlan> families, IEnumerable<Diagnostic> diagnostics)
    { Normalized = normalized; Status = status; Families = families.OrderBy(f => f.Family).ToList().AsReadOnly(); Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public TransmissionGoalNormalization Normalized { get; }
    public string GoalId => Normalized.GoalId;
    public TransmissionGoalStatus Status { get; }
    public ReadOnlyCollection<TransmissionChildPlan> Families { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsSupported => Normalized.IsValid && Status == TransmissionGoalStatus.Complete;
    public int AllocatedWork => Families.Sum(f => f.Quota);
    public int ProofWork => Families.Sum(f => f.ProofWork);
    public int PreparationCeiling => Families.Sum(f => f.PreparationCeiling);
    public string PlanId => GearRoutingContract.Id("transmission-search-plan", GearRoutingContract.Pack(GoalId, TransmissionGoalContract.Lowering,
        TransmissionGoalContract.Allocation, Status.ToString(), GearRoutingContract.List(Families.Select(f => f.Canonical))));
}
