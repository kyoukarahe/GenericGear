using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class GearRouteMetrics
{
    public GearRouteMetrics(int idlers, BigInteger preferredPenalty, BigInteger footprint, BigInteger totalTeeth, string signature)
    { Idlers = idlers; PreferredPenalty = preferredPenalty; Footprint = footprint; TotalTeeth = totalTeeth; Signature = signature; }
    public int Idlers { get; }
    public BigInteger PreferredPenalty { get; }
    public BigInteger Footprint { get; }
    public BigInteger TotalTeeth { get; }
    public string Signature { get; }
}

public sealed class GearRouteValidation
{
    public GearRouteValidation(string requestId, string candidateId, ValidationBundle genericValidation, SpatialValidationBundle spatial,
        Rational? actualTransfer, IEnumerable<Diagnostic> diagnostics)
    {
        RequestId = requestId; CandidateId = candidateId; GenericValidation = genericValidation; Spatial = spatial;
        ActualTransfer = actualTransfer; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        ContextId = GearRoutingContract.Id("gear-route-context", GearRoutingContract.Pack(requestId, candidateId));
    }
    public string RequestId { get; }
    public string CandidateId { get; }
    public string ContextId { get; }
    public ValidationBundle GenericValidation { get; }
    public SpatialValidationBundle Spatial { get; }
    public Rational? ActualTransfer { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => GenericValidation.IsValid && Spatial.IsValid && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

public sealed class GearRouteCandidate
{
    public GearRouteCandidate(string candidateId, IEnumerable<GearRouteAssignment> path, GenerationCandidate mechanism, GearRouteMetrics metrics, GearRouteValidation validation)
    {
        CandidateId = candidateId; var p = path.Take(8).ToList(); if (p.Count > 7) throw new ArgumentException("Routing path ceiling exceeded.");
        Path = p.AsReadOnly(); Mechanism = mechanism; Metrics = metrics; Validation = validation;
    }
    public string CandidateId { get; }
    public ReadOnlyCollection<GearRouteAssignment> Path { get; }
    public GenerationCandidate Mechanism { get; }
    public GearRouteMetrics Metrics { get; }
    public GearRouteValidation Validation { get; }
}

public sealed class GearRoutingSearchSummary
{
    public GearRoutingSearchSummary(int potentialNodes, int nodeChecks, int staticRejectedNodes, int adjacencyChecks, int edges,
        int expansions, int rejectedSuccessors, int goalChecks, int validRoutes, bool workRemaining)
    { PotentialNodes = potentialNodes; NodeChecks = nodeChecks; StaticRejectedNodes = staticRejectedNodes; AdjacencyChecks = adjacencyChecks; Edges = edges;
        Expansions = expansions; RejectedSuccessors = rejectedSuccessors; GoalChecks = goalChecks; ValidRoutes = validRoutes; WorkRemaining = workRemaining; }
    public int PotentialNodes { get; }
    public int NodeChecks { get; }
    public int StaticRejectedNodes { get; }
    public int AdjacencyChecks { get; }
    public int Edges { get; }
    public int Expansions { get; }
    public int RejectedSuccessors { get; }
    public int GoalChecks { get; }
    public int ValidRoutes { get; }
    public bool WorkRemaining { get; }
}

public sealed class GearRoutingRejection
{
    public GearRoutingRejection(string code, int count) { Code = code; Count = count; }
    public string Code { get; }
    public int Count { get; }
}

public sealed class GearRoutingResult
{
    public GearRoutingResult(GearRoutingNormalization normalized, GearRoutingStatus status, GearRoutingSearchSummary summary,
        IEnumerable<GearRouteCandidate> candidates, IEnumerable<GearRoutingRejection> rejections, IEnumerable<Diagnostic> details, int omittedDetails)
    {
        Normalized = normalized; Status = status; Summary = summary;
        var list = candidates.Take(GearRoutingContract.MaxReturned + 1).ToList(); if (list.Count > GearRoutingContract.MaxReturned) throw new ArgumentException("Routing result ceiling exceeded.");
        Candidates = list.AsReadOnly(); Rejections = rejections.OrderBy(r => r.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        Details = details.Take(GearRoutingContract.MaxDetails).ToList().AsReadOnly(); OmittedDetails = omittedDetails;
    }
    public GearRoutingNormalization Normalized { get; }
    public string RequestId => Normalized.RequestId;
    public GearRoutingStatus Status { get; }
    public GearRoutingSearchSummary Summary { get; }
    public ReadOnlyCollection<GearRouteCandidate> Candidates { get; }
    public ReadOnlyCollection<GearRoutingRejection> Rejections { get; }
    public ReadOnlyCollection<Diagnostic> Details { get; }
    public int OmittedDetails { get; }
    public bool SearchComplete => Status == GearRoutingStatus.Complete || Status == GearRoutingStatus.Infeasible;
    public bool ResultTruncated => Summary.ValidRoutes > Candidates.Count;
}
