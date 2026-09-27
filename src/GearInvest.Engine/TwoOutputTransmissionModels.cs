using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

public enum TwoOutputStrategyStatus { ExcludedByPolicy, ProvenInfeasible, PendingBudget, Complete, Infeasible, IncompleteBudget, IncompleteResource, InvalidInput, Unsupported, Cancelled, Failed }

public sealed class TwoOutputCommonMetrics
{
    public TwoOutputCommonMetrics(int compounds, int bodyCount, int nonEndpointBodies, int transmissionGears, BigInteger footprint, BigInteger preferredPenalty,
        BigInteger totalTeeth, IEnumerable<int> layers)
    { Compounds = compounds; BodyCount = bodyCount; NonEndpointBodies = nonEndpointBodies; TransmissionGears = transmissionGears; Footprint = footprint; PreferredPenalty = preferredPenalty;
        TotalTeeth = totalTeeth; Layers = layers.Distinct().OrderBy(x => x).ToList().AsReadOnly(); }
    public int Compounds { get; }
    public int BodyCount { get; }
    public int NonEndpointBodies { get; }
    public int TransmissionGears { get; }
    public BigInteger Footprint { get; }
    public BigInteger PreferredPenalty { get; }
    public BigInteger TotalTeeth { get; }
    public ReadOnlyCollection<int> Layers { get; }
    public string Canonical => GearRoutingContract.Pack(GearRoutingContract.Number(Compounds), GearRoutingContract.Number(BodyCount),
        GearRoutingContract.Number(NonEndpointBodies), GearRoutingContract.Number(TransmissionGears), GearRoutingContract.Number(Footprint), GearRoutingContract.Number(PreferredPenalty),
        GearRoutingContract.Number(TotalTeeth), GearRoutingContract.List(Layers.Select(x => GearRoutingContract.Number(x))));
}

/// <summary>Original validated child whole, including origins even when it was outside the child's retained cap.</summary>
public sealed class TwoOutputCandidateOrigin
{
    public TwoOutputCandidateOrigin(string childGoalId, SharedDriverTransmissionCandidate? rootOnly = null, SharedPrefixTransmissionCandidate? sharedPrefix = null)
    {
        if ((rootOnly == null) == (sharedPrefix == null)) throw new ArgumentException("Exactly one typed sharing origin is required.");
        ChildGoalId = childGoalId; RootOnly = rootOnly; SharedPrefix = sharedPrefix;
    }
    public string ChildGoalId { get; }
    public SharedDriverTransmissionCandidate? RootOnly { get; }
    public SharedPrefixTransmissionCandidate? SharedPrefix { get; }
    public TwoOutputSharingStrategy Strategy => RootOnly != null ? TwoOutputSharingStrategy.RootOnly : TwoOutputSharingStrategy.SharedPrefix;
    public string StrategyId => TwoOutputTransmissionContract.StrategyId(Strategy);
    public string CandidateId => RootOnly?.CandidateId ?? SharedPrefix!.CandidateId;
    public GenerationCandidate Mechanism => RootOnly?.Mechanism ?? SharedPrefix!.Mechanism;
    public IReadOnlyList<SharedDriverOutputBinding> Outputs => RootOnly?.Outputs ?? SharedPrefix!.Outputs;
    public string SourceContextId => RootOnly?.ContextId(ChildGoalId) ?? SharedPrefix!.ContextId(ChildGoalId);
    public string OriginId => TwoOutputTransmissionContract.Id("origin", GearRoutingContract.Pack(StrategyId, ChildGoalId, SourceContextId));
    public string OutputBindingSignature => GearRoutingContract.List(Outputs.Select(o => GearRoutingContract.Pack(o.OutputKey, o.DofId, o.AxisId, o.BodyId, o.ActualTransfer.ToString())));
}

public sealed class TwoOutputTransmissionCandidate
{
    public TwoOutputTransmissionCandidate(IEnumerable<TwoOutputCandidateOrigin> origins, TwoOutputCommonMetrics metrics)
    {
        Origins = origins.Take(33).OrderBy(o => o.OriginId, StringComparer.Ordinal).ToList().AsReadOnly(); Metrics = metrics;
        if (Origins.Count == 0 || Origins.Count > 32) throw new ArgumentException("Bounded nonempty typed origins required.");
    }
    public ReadOnlyCollection<TwoOutputCandidateOrigin> Origins { get; }
    public TwoOutputCommonMetrics Metrics { get; }
    public string CandidateId => Origins[0].CandidateId;
    public GenerationCandidate Mechanism => Origins[0].Mechanism;
    public IReadOnlyList<SharedDriverOutputBinding> Outputs => Origins[0].Outputs;
    public string ContextId(string goalId) => TwoOutputTransmissionContract.Id("context", GearRoutingContract.Pack(goalId, CandidateId,
        GearRoutingContract.List(Origins.Select(o => o.OriginId)), Metrics.Canonical, Origins[0].OutputBindingSignature));
}

public sealed class TwoOutputWholeObservation
{
    public TwoOutputWholeObservation(TwoOutputCandidateOrigin origin, TwoOutputCommonMetrics metrics, IEnumerable<Diagnostic> diagnostics, int canonicalBytes)
    { Origin = origin; Metrics = metrics; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics.Take(64)); CanonicalBytes = canonicalBytes; }
    public TwoOutputCandidateOrigin Origin { get; }
    public TwoOutputCommonMetrics Metrics { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool Accepted => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public int CanonicalBytes { get; }
    public string Canonical => GearRoutingContract.Pack(Origin.OriginId, Metrics.Canonical, GearRoutingContract.List(Diagnostics.Select(d => GearRoutingContract.Pack(d.Code, d.SubjectId ?? "", d.Message))), GearRoutingContract.Number(CanonicalBytes));
}

public sealed class TwoOutputStrategyOutcome
{
    public TwoOutputStrategyOutcome(TwoOutputStrategyPlan plan, TwoOutputStrategyStatus status, int consumed, int observed, int admitted, int dropped,
        string reason, SharedDriverTransmissionResult? rootOnly = null, SharedPrefixTransmissionResult? sharedPrefix = null)
    { Plan = plan; Status = status; ConsumedWork = consumed; ObservedWholes = observed; AdmittedWholes = admitted; DroppedWholes = dropped; Reason = reason; RootOnly = rootOnly; SharedPrefix = sharedPrefix; }
    public TwoOutputStrategyPlan Plan { get; }
    public TwoOutputStrategyStatus Status { get; }
    public int ConsumedWork { get; }
    public int UnusedWork => Plan.Quota - ConsumedWork;
    public int ObservedWholes { get; }
    public int AdmittedWholes { get; }
    public int DroppedWholes { get; }
    public string Reason { get; }
    public SharedDriverTransmissionResult? RootOnly { get; }
    public SharedPrefixTransmissionResult? SharedPrefix { get; }
    public bool Closed => Status == TwoOutputStrategyStatus.ExcludedByPolicy || Status == TwoOutputStrategyStatus.ProvenInfeasible || Status == TwoOutputStrategyStatus.Complete || Status == TwoOutputStrategyStatus.Infeasible;
}

public sealed class TwoOutputTransmissionResult
{
    public TwoOutputTransmissionResult(TwoOutputSharingSearchPlan plan, TwoOutputTransmissionStatus status, IEnumerable<TwoOutputStrategyOutcome> outcomes,
        IEnumerable<TwoOutputWholeObservation> observations, IEnumerable<TwoOutputTransmissionCandidate> candidates, int uniqueAccepted,
        string collectorStop, IEnumerable<Diagnostic> diagnostics, TwoOutputWholeObservation? collectorStopObservation = null)
    {
        Plan = plan; Status = status; Outcomes = outcomes.Take(3).ToList().AsReadOnly(); Observations = observations.Take(TwoOutputTransmissionContract.MaxObservations + 1).ToList().AsReadOnly(); Candidates = candidates.Take(TwoOutputTransmissionContract.MaxReturned + 1).ToList().AsReadOnly();
        UniqueAccepted = uniqueAccepted; CollectorStop = collectorStop; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics.Take(128));
        CollectorStopObservation = collectorStopObservation;
        if (Outcomes.Count > 2 || Observations.Count > TwoOutputTransmissionContract.MaxObservations || Candidates.Count > TwoOutputTransmissionContract.MaxReturned) throw new ArgumentException("Sharing result resource envelope.");
    }
    public TwoOutputSharingSearchPlan Plan { get; }
    public string GoalId => Plan.GoalId;
    public TwoOutputTransmissionStatus Status { get; }
    public ReadOnlyCollection<TwoOutputStrategyOutcome> Outcomes { get; }
    public ReadOnlyCollection<TwoOutputWholeObservation> Observations { get; }
    public ReadOnlyCollection<TwoOutputTransmissionCandidate> Candidates { get; }
    public int UniqueAccepted { get; }
    public string CollectorStop { get; }
    /// <summary>The first actual whole denied admission; not a retained candidate or a search-completeness proof.</summary>
    public TwoOutputWholeObservation? CollectorStopObservation { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public int ConsumedWork => Outcomes.Sum(o => o.ConsumedWork);
    public int UnusedAllocatedWork => Plan.AllocatedWork - ConsumedWork;
    public int UnallocatedWork => (Plan.Normalized.Goal?.TotalWorkBudget ?? 0) - Plan.AllocatedWork;
    public int ObservationBytes => Observations.Sum(o => o.CanonicalBytes);
    public int ObservedWholes => Outcomes.Sum(o => o.ObservedWholes);
    public int AcceptedObservations => Observations.Count(o => o.Accepted);
    public int RejectedObservations => Observations.Count - AcceptedObservations;
    public int DroppedObservations => Outcomes.Sum(o => o.DroppedWholes);
    public bool HasValidatedCandidates => Candidates.Count != 0;
    public bool SearchComplete => Status == TwoOutputTransmissionStatus.Complete || Status == TwoOutputTransmissionStatus.Infeasible;
    public bool ResultTruncated => UniqueAccepted > Candidates.Count;
    public string RankingGuarantee => SearchComplete ? "globalTopKWithinDeclaredSupportedSharingUnion" : "bestAmongExplored";
}
