using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class CompoundPairSynthesisResult
{
    public CompoundPairSynthesisResult(CompoundRoutingNormalization normalized, GearRoutingStatus status, int examinedPairs, IEnumerable<AnchoredCompoundToothPair> pairs)
    { Normalized = normalized; Status = status; ExaminedPairs = examinedPairs; Pairs = pairs.ToList().AsReadOnly(); }
    public CompoundRoutingNormalization Normalized { get; }
    public GearRoutingStatus Status { get; }
    public int ExaminedPairs { get; }
    public ReadOnlyCollection<AnchoredCompoundToothPair> Pairs { get; }
    public bool SearchComplete => Status == GearRoutingStatus.Complete || Status == GearRoutingStatus.Infeasible;
}

public sealed class CompoundRouteValidation
{
    public CompoundRouteValidation(string requestId, string candidateId, ValidationBundle generic, SpatialValidationBundle spatial,
        Rational? inputTransfer, Rational? outputTransfer, Rational? totalTransfer, IEnumerable<Diagnostic> diagnostics)
    { RequestId = requestId; CandidateId = candidateId; GenericValidation = generic; Spatial = spatial; InputTransfer = inputTransfer;
        OutputTransfer = outputTransfer; TotalTransfer = totalTransfer; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); }
    public string RequestId { get; }
    public string CandidateId { get; }
    public string ContextId => CompoundRoutingContract.ContextId(RequestId, CandidateId);
    public ValidationBundle GenericValidation { get; }
    public SpatialValidationBundle Spatial { get; }
    public Rational? InputTransfer { get; }
    public Rational? OutputTransfer { get; }
    public Rational? TotalTransfer { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => GenericValidation.IsValid && Spatial.IsValid && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

public sealed class CompoundGearRouteCandidate
{
    public CompoundGearRouteCandidate(string candidateId, AnchoredCompoundToothPair pair, IEnumerable<GearRouteAssignment> inputPath,
        IEnumerable<GearRouteAssignment> outputPath, GenerationCandidate mechanism, GearRouteMetrics metrics, CompoundRouteValidation validation)
    {
        CandidateId = candidateId; Pair = pair; InputPath = Path(inputPath); OutputPath = Path(outputPath); Mechanism = mechanism; Metrics = metrics; Validation = validation;
    }
    private static ReadOnlyCollection<GearRouteAssignment> Path(IEnumerable<GearRouteAssignment> path)
    { var p = path.Take(8).ToList(); if (p.Count > 7) throw new ArgumentException("Compound leg path ceiling exceeded."); return p.AsReadOnly(); }
    public string CandidateId { get; }
    public AnchoredCompoundToothPair Pair { get; }
    public ReadOnlyCollection<GearRouteAssignment> InputPath { get; }
    public ReadOnlyCollection<GearRouteAssignment> OutputPath { get; }
    public GenerationCandidate Mechanism { get; }
    public GearRouteMetrics Metrics { get; }
    public CompoundRouteValidation Validation { get; }
}

public sealed class CompoundRoutingSearchSummary
{
    public CompoundRoutingSearchSummary(int examinedPairs, int ratioValidPairs, int examinedSites, int nodeChecks, int adjacencyChecks,
        int inputExpansions, int outputExpansions, int inputRoutes, int outputRoutes, int mergeAttempts, int globalRejections, int validMechanisms,
        bool workRemaining, string stopStage, string frontier)
    {
        ExaminedPairs = examinedPairs; RatioValidPairs = ratioValidPairs; ExaminedSites = examinedSites; NodeChecks = nodeChecks; AdjacencyChecks = adjacencyChecks;
        InputExpansions = inputExpansions; OutputExpansions = outputExpansions; InputRoutes = inputRoutes; OutputRoutes = outputRoutes; MergeAttempts = mergeAttempts;
        GlobalRejections = globalRejections; ValidMechanisms = validMechanisms; WorkRemaining = workRemaining; StopStage = stopStage; Frontier = frontier;
    }
    public int ExaminedPairs { get; }
    public int RatioValidPairs { get; }
    public int ExaminedSites { get; }
    public int NodeChecks { get; }
    public int AdjacencyChecks { get; }
    public int InputExpansions { get; }
    public int OutputExpansions { get; }
    public int InputRoutes { get; }
    public int OutputRoutes { get; }
    public int MergeAttempts { get; }
    public int GlobalRejections { get; }
    public int ValidMechanisms { get; }
    // Normalized pair/site/path enumeration is injective into role-labelled mechanical payloads.
    public int DeduplicatedMechanisms => ValidMechanisms;
    public int Work => ExaminedPairs + ExaminedSites + InputExpansions + OutputExpansions + MergeAttempts;
    public bool WorkRemaining { get; }
    public string StopStage { get; }
    public string Frontier { get; }
}

public sealed class CompoundRoutingPlacementAttempt
{
    public CompoundRoutingPlacementAttempt(string pairId, GearRoutePoint site, int workStart, int workEnd, int inputRoutes, int outputRoutes,
        int merges, int rejections, int valid, bool complete)
    { PairId = pairId; Site = site; WorkStart = workStart; WorkEnd = workEnd; InputRoutes = inputRoutes; OutputRoutes = outputRoutes; Merges = merges; Rejections = rejections; Valid = valid; Complete = complete; }
    public string PairId { get; }
    public GearRoutePoint Site { get; }
    public int WorkStart { get; }
    public int WorkEnd { get; }
    public int InputRoutes { get; }
    public int OutputRoutes { get; }
    public int Merges { get; }
    public int Rejections { get; }
    public int Valid { get; }
    public bool Complete { get; }
}

public sealed class CompoundGearRoutingResult
{
    public CompoundGearRoutingResult(CompoundRoutingNormalization normalized, GearRoutingStatus status, CompoundRoutingSearchSummary summary,
        IEnumerable<CompoundGearRouteCandidate> candidates, IEnumerable<GearRoutingRejection> rejections,
        IEnumerable<CompoundRoutingPlacementAttempt> attempts, int omittedAttempts, IEnumerable<Diagnostic> details)
    {
        Normalized = normalized; Status = status; Summary = summary;
        var c = candidates.Take(GearRoutingContract.MaxReturned + 1).ToList(); if (c.Count > GearRoutingContract.MaxReturned) throw new ArgumentException("Compound result cap exceeded.");
        Candidates = c.AsReadOnly(); Rejections = rejections.OrderBy(r => r.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        Attempts = attempts.Take(GearRoutingContract.MaxDetails).ToList().AsReadOnly(); OmittedAttempts = omittedAttempts;
        Details = details.Take(GearRoutingContract.MaxDetails).ToList().AsReadOnly();
    }
    public CompoundRoutingNormalization Normalized { get; }
    public string RequestId => Normalized.RequestId;
    public GearRoutingStatus Status { get; }
    public CompoundRoutingSearchSummary Summary { get; }
    public ReadOnlyCollection<CompoundGearRouteCandidate> Candidates { get; }
    public ReadOnlyCollection<GearRoutingRejection> Rejections { get; }
    public ReadOnlyCollection<CompoundRoutingPlacementAttempt> Attempts { get; }
    public int OmittedAttempts { get; }
    public ReadOnlyCollection<Diagnostic> Details { get; }
    public bool SearchComplete => Status == GearRoutingStatus.Complete || Status == GearRoutingStatus.Infeasible;
    public bool ResultTruncated => Summary.DeduplicatedMechanisms > Candidates.Count;
}
