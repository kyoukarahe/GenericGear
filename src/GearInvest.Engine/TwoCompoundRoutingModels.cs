using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class TwoCompoundAssignmentSynthesisResult
{
    public TwoCompoundAssignmentSynthesisResult(TwoCompoundRoutingNormalization normalized, GearRoutingStatus status, int examined, IEnumerable<TwoCompoundToothAssignment> assignments)
    { Normalized = normalized; Status = status; ExaminedAssignments = examined; Assignments = assignments.ToList().AsReadOnly(); }
    public TwoCompoundRoutingNormalization Normalized { get; }
    public GearRoutingStatus Status { get; }
    public int ExaminedAssignments { get; }
    public ReadOnlyCollection<TwoCompoundToothAssignment> Assignments { get; }
    public bool SearchComplete => Status == GearRoutingStatus.Complete || Status == GearRoutingStatus.Infeasible;
}

public sealed class TwoCompoundRouteValidation
{
    public TwoCompoundRouteValidation(string requestId, string candidateId, ValidationBundle generic, SpatialValidationBundle spatial,
        IEnumerable<Rational?> legTransfers, Rational? totalTransfer, IEnumerable<Diagnostic> diagnostics)
    { RequestId = requestId; CandidateId = candidateId; GenericValidation = generic; Spatial = spatial;
        LegTransfers = Three(legTransfers); TotalTransfer = totalTransfer; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    internal static ReadOnlyCollection<T> Three<T>(IEnumerable<T> items)
    { var a = items.Take(4).ToList(); if (a.Count != 3) throw new ArgumentException("Exactly three ordered legs required."); return a.AsReadOnly(); }
    public string RequestId { get; }
    public string CandidateId { get; }
    public string ContextId => TwoCompoundRoutingContract.ContextId(RequestId, CandidateId);
    public ValidationBundle GenericValidation { get; }
    public SpatialValidationBundle Spatial { get; }
    public ReadOnlyCollection<Rational?> LegTransfers { get; }
    public Rational? TotalTransfer { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => GenericValidation.IsValid && Spatial.IsValid && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

public sealed class TwoCompoundGearRouteCandidate
{
    public TwoCompoundGearRouteCandidate(string candidateId, TwoCompoundToothAssignment assignment, IEnumerable<IEnumerable<GearRouteAssignment>> paths,
        GenerationCandidate mechanism, GearRouteMetrics metrics, TwoCompoundRouteValidation validation)
    {
        CandidateId = candidateId; Assignment = assignment;
        Paths = TwoCompoundRouteValidation.Three(paths.Take(4).Select(p => { var a = p.Take(8).ToList(); if (a.Count < 2 || a.Count > 7) throw new ArgumentException("Path requires 0..5 idlers."); return a.AsReadOnly(); }));
        Mechanism = mechanism; Metrics = metrics; Validation = validation;
    }
    public string CandidateId { get; }
    public TwoCompoundToothAssignment Assignment { get; }
    public ReadOnlyCollection<ReadOnlyCollection<GearRouteAssignment>> Paths { get; }
    public GenerationCandidate Mechanism { get; }
    public GearRouteMetrics Metrics { get; }
    public TwoCompoundRouteValidation Validation { get; }
}

public sealed class TwoCompoundRoutingSearchSummary
{
    public TwoCompoundRoutingSearchSummary(int examinedAssignments, int ratioValidAssignments, int examinedSitePairs, int nodeChecks, int adjacencyChecks,
        IEnumerable<int> legExpansions, IEnumerable<int> observedRoutes, int earlyRejections, int mergeAttempts, int globalRejections, int validMechanisms,
        bool workRemaining, string stopStage, string frontier)
    {
        ExaminedAssignments = examinedAssignments; RatioValidAssignments = ratioValidAssignments; ExaminedSitePairs = examinedSitePairs;
        NodeChecks = nodeChecks; AdjacencyChecks = adjacencyChecks; LegExpansions = TwoCompoundRouteValidation.Three(legExpansions);
        ObservedRoutes = TwoCompoundRouteValidation.Three(observedRoutes); EarlyRejections = earlyRejections; MergeAttempts = mergeAttempts;
        GlobalRejections = globalRejections; ValidMechanisms = validMechanisms; WorkRemaining = workRemaining; StopStage = stopStage; Frontier = frontier;
    }
    public int ExaminedAssignments { get; }
    public int RatioValidAssignments { get; }
    public int ExaminedSitePairs { get; }
    public int NodeChecks { get; }
    public int AdjacencyChecks { get; }
    public ReadOnlyCollection<int> LegExpansions { get; }
    public ReadOnlyCollection<int> ObservedRoutes { get; }
    public int EarlyRejections { get; }
    public int MergeAttempts { get; }
    public int GlobalRejections { get; }
    public int ValidMechanisms { get; }
    // Ordered role labels and unique normalized options make enumeration injective into mechanical payloads.
    public int DeduplicatedMechanisms => ValidMechanisms;
    public int Work => ExaminedAssignments + ExaminedSitePairs + LegExpansions.Sum() + MergeAttempts;
    public bool WorkRemaining { get; }
    public string StopStage { get; }
    public string Frontier { get; }
}

public sealed class TwoCompoundRoutingPlacementAttempt
{
    public TwoCompoundRoutingPlacementAttempt(string assignmentId, IEnumerable<GearRoutePoint> sites, int workStart, int workEnd,
        IEnumerable<int> observedRoutes, int earlyRejections, int merges, int rejections, int valid, bool complete)
    {
        AssignmentId = assignmentId; var s = sites.Take(3).ToList(); if (s.Count != 2) throw new ArgumentException("Two ordered sites required."); Sites = s.AsReadOnly();
        WorkStart = workStart; WorkEnd = workEnd; ObservedRoutes = TwoCompoundRouteValidation.Three(observedRoutes);
        EarlyRejections = earlyRejections; Merges = merges; Rejections = rejections; Valid = valid; Complete = complete;
    }
    public string AssignmentId { get; }
    public ReadOnlyCollection<GearRoutePoint> Sites { get; }
    public int WorkStart { get; }
    public int WorkEnd { get; }
    public ReadOnlyCollection<int> ObservedRoutes { get; }
    public int EarlyRejections { get; }
    public int Merges { get; }
    public int Rejections { get; }
    public int Valid { get; }
    public bool Complete { get; }
}

public sealed class TwoCompoundGearRoutingResult
{
    public TwoCompoundGearRoutingResult(TwoCompoundRoutingNormalization normalized, GearRoutingStatus status, TwoCompoundRoutingSearchSummary summary,
        IEnumerable<TwoCompoundGearRouteCandidate> candidates, IEnumerable<GearRoutingRejection> rejections,
        IEnumerable<TwoCompoundRoutingPlacementAttempt> attempts, int omittedAttempts, IEnumerable<Diagnostic> details)
    {
        Normalized = normalized; Status = status; Summary = summary;
        var c = candidates.Take(GearRoutingContract.MaxReturned + 1).ToList(); if (c.Count > GearRoutingContract.MaxReturned) throw new ArgumentException("Result cap exceeded.");
        Candidates = c.AsReadOnly(); Rejections = rejections.OrderBy(r => r.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        Attempts = attempts.Take(GearRoutingContract.MaxDetails).ToList().AsReadOnly(); OmittedAttempts = omittedAttempts;
        Details = details.Take(GearRoutingContract.MaxDetails).ToList().AsReadOnly();
    }
    public TwoCompoundRoutingNormalization Normalized { get; }
    public string RequestId => Normalized.RequestId;
    public GearRoutingStatus Status { get; }
    public TwoCompoundRoutingSearchSummary Summary { get; }
    public ReadOnlyCollection<TwoCompoundGearRouteCandidate> Candidates { get; }
    public ReadOnlyCollection<GearRoutingRejection> Rejections { get; }
    public ReadOnlyCollection<TwoCompoundRoutingPlacementAttempt> Attempts { get; }
    public int OmittedAttempts { get; }
    public ReadOnlyCollection<Diagnostic> Details { get; }
    public bool SearchComplete => Status == GearRoutingStatus.Complete || Status == GearRoutingStatus.Infeasible;
    public bool ResultTruncated => Summary.DeduplicatedMechanisms > Candidates.Count;
}
