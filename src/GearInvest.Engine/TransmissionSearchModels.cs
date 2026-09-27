using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Typed original child authority, not a copied display-only certificate.</summary>
public sealed class TransmissionCandidateOrigin
{
    public TransmissionCandidateOrigin(GearRouteCandidate candidate) { Simple = candidate; Family = TransmissionFamily.SimpleIdler; }
    public TransmissionCandidateOrigin(CompoundGearRouteCandidate candidate) { One = candidate; Family = TransmissionFamily.OneCompound; }
    public TransmissionCandidateOrigin(TwoCompoundGearRouteCandidate candidate) { Two = candidate; Family = TransmissionFamily.TwoCompound; }
    public TransmissionFamily Family { get; }
    public GearRouteCandidate? Simple { get; }
    public CompoundGearRouteCandidate? One { get; }
    public TwoCompoundGearRouteCandidate? Two { get; }
    public GenerationCandidate Mechanism => Simple?.Mechanism ?? One?.Mechanism ?? Two!.Mechanism;
    public string CandidateId => Simple?.CandidateId ?? One?.CandidateId ?? Two!.CandidateId;
    public string RequestId => Simple?.Validation.RequestId ?? One?.Validation.RequestId ?? Two!.Validation.RequestId;
    public string ContextId => Simple?.Validation.ContextId ?? One?.Validation.ContextId ?? Two!.Validation.ContextId;
    public string OriginId => GearRoutingContract.Id("transmission-origin", GearRoutingContract.Pack(TransmissionGoalContract.FamilyId(Family), RequestId, ContextId));
}

public sealed class TransmissionCommonMetrics
{
    public TransmissionCommonMetrics(int compounds, int idlers, BigInteger preferredPenalty, BigInteger footprint, BigInteger totalTeeth, IEnumerable<int> layers)
    { Compounds = compounds; Idlers = idlers; PreferredPenalty = preferredPenalty; Footprint = footprint; TotalTeeth = totalTeeth; Layers = layers.Distinct().OrderBy(x => x).ToList().AsReadOnly(); }
    public int Compounds { get; }
    public int Idlers { get; }
    public BigInteger PreferredPenalty { get; }
    public BigInteger Footprint { get; }
    public BigInteger TotalTeeth { get; }
    public ReadOnlyCollection<int> Layers { get; }
    public string Canonical => GearRoutingContract.Pack(GearRoutingContract.Number(Compounds), GearRoutingContract.Number(Idlers), GearRoutingContract.Number(PreferredPenalty),
        GearRoutingContract.Number(Footprint), GearRoutingContract.Number(TotalTeeth), GearRoutingContract.List(Layers.Select(x => GearRoutingContract.Number(x))));
}

public sealed class TransmissionMechanismCandidate
{
    public TransmissionMechanismCandidate(string candidateId, IEnumerable<TransmissionCandidateOrigin> origins, TransmissionCommonMetrics metrics)
    {
        var all = origins.Take(4).ToList(); if (all.Count < 1 || all.Count > 3) throw new ArgumentException("One to three typed candidate origins required.");
        CandidateId = candidateId; Origins = all.OrderBy(o => o.Family).ThenBy(o => o.OriginId, StringComparer.Ordinal).ToList().AsReadOnly(); Metrics = metrics;
    }
    public string CandidateId { get; }
    public ReadOnlyCollection<TransmissionCandidateOrigin> Origins { get; }
    public GenerationCandidate Mechanism => Origins[0].Mechanism;
    public TransmissionCommonMetrics Metrics { get; }
}

public sealed class TransmissionGoalValidation
{
    public TransmissionGoalValidation(string goalId, string candidateId, Rational? actualTransfer, IEnumerable<string> contexts, IEnumerable<Diagnostic> diagnostics)
    { GoalId = goalId; CandidateId = candidateId; ActualTransfer = actualTransfer; ContextIds = contexts.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly(); Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public string GoalId { get; }
    public string CandidateId { get; }
    public Rational? ActualTransfer { get; }
    public ReadOnlyCollection<string> ContextIds { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && ActualTransfer.HasValue;
    public string Scope => "exact single-driver zero-phase graph; fixed anchors/teeth/canonical layers; child finite domain and ideal pitch-envelope environment; no tooth/contact/shaft/dynamics/manufacturing";
}

public sealed class TransmissionFamilyOutcome
{
    public TransmissionFamilyOutcome(TransmissionFamily family, TransmissionFamilyStatus status, int allocated, int consumed, int nodeChecks, int adjacencyChecks,
        int validatedObservations, string reason, GearRoutingResult? simple = null, CompoundGearRoutingResult? one = null, TwoCompoundGearRoutingResult? two = null)
    { Family = family; Status = status; Allocated = allocated; Consumed = consumed; NodeChecks = nodeChecks; AdjacencyChecks = adjacencyChecks;
        ValidatedObservations = validatedObservations; Reason = reason; Simple = simple; One = one; Two = two; }
    public TransmissionFamily Family { get; }
    public TransmissionFamilyStatus Status { get; }
    public int Allocated { get; }
    public int Consumed { get; }
    public int Unused => Allocated - Consumed;
    public int NodeChecks { get; }
    public int AdjacencyChecks { get; }
    public int ValidatedObservations { get; }
    public string Reason { get; }
    public GearRoutingResult? Simple { get; }
    public CompoundGearRoutingResult? One { get; }
    public TwoCompoundGearRoutingResult? Two { get; }
    public bool SearchComplete => Status == TransmissionFamilyStatus.Complete || Status == TransmissionFamilyStatus.Infeasible || Status == TransmissionFamilyStatus.ProvenInfeasible;
}

public sealed class TransmissionGenerationResult
{
    public TransmissionGenerationResult(CompiledTransmissionSearchPlan plan, TransmissionGoalStatus status, IEnumerable<TransmissionFamilyOutcome> outcomes,
        IEnumerable<TransmissionMechanismCandidate> candidates, int observedUnique, bool observationsExact, IEnumerable<Diagnostic> diagnostics)
    {
        Plan = plan; Status = status; var o = outcomes.Take(4).ToList(); if (o.Count > 3) throw new ArgumentException("Three-family outcome bound.");
        Outcomes = o.OrderBy(x => x.Family).ToList().AsReadOnly(); var c = candidates.Take(129).ToList(); if (c.Count > 128) throw new ArgumentException("Common return bound.");
        Candidates = c.AsReadOnly(); ObservedUnique = observedUnique; ObservationsExact = observationsExact; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }
    public CompiledTransmissionSearchPlan Plan { get; }
    public string GoalId => Plan.GoalId;
    public TransmissionGoalStatus Status { get; }
    public ReadOnlyCollection<TransmissionFamilyOutcome> Outcomes { get; }
    public ReadOnlyCollection<TransmissionMechanismCandidate> Candidates { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    // Counts only actually observed unique IDs; never an estimate of unseen domain cardinality.
    public int ObservedUnique { get; }
    public bool ObservationsExact { get; }
    public int? TotalUnique => SearchComplete && ObservationsExact ? ObservedUnique : (int?)null;
    public bool HasValidatedCandidates => Candidates.Count > 0;
    public bool SearchComplete => Status == TransmissionGoalStatus.Complete || Status == TransmissionGoalStatus.Infeasible;
    public bool ResultTruncated => ObservedUnique > Candidates.Count;
    public string RankingGuarantee => SearchComplete ? "globalTopKWithinDeclaredSupportedUnion" : "bestAmongExplored";
    public int AllocatedWork => Plan.AllocatedWork;
    public int ConsumedWork => Outcomes.Sum(o => o.Consumed);
    public int UnusedAllocatedWork => AllocatedWork - ConsumedWork;
    public int UnallocatedWork => (Plan.Normalized.Goal?.WorkBudget ?? 0) - AllocatedWork;
}
