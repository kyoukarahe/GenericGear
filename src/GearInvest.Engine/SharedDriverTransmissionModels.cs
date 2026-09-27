using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class SharedDriverRoleMapping
{
    public SharedDriverRoleMapping(string kind, string localId, string mergedId) { Kind = kind; LocalId = localId; MergedId = mergedId; }
    public string Kind { get; }
    public string LocalId { get; }
    public string MergedId { get; }
    public string Canonical => GearRoutingContract.Pack(Kind, LocalId, MergedId);
}

public sealed class SharedDriverOutputBinding
{
    public SharedDriverOutputBinding(string outputKey, string childGoalId, TransmissionMechanismCandidate child,
        string axisId, string dofId, string bodyId, Rational requested, Rational actual, IEnumerable<SharedDriverRoleMapping> mappings)
    {
        OutputKey = outputKey; ChildGoalId = childGoalId; Child = child; AxisId = axisId; DofId = dofId; BodyId = bodyId;
        RequestedTransfer = requested; ActualTransfer = actual; Mappings = mappings.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.LocalId, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string OutputKey { get; }
    public string ChildGoalId { get; }
    public TransmissionMechanismCandidate Child { get; }
    public string AxisId { get; }
    public string DofId { get; }
    public string BodyId { get; }
    public Rational RequestedTransfer { get; }
    public Rational ActualTransfer { get; }
    public ReadOnlyCollection<SharedDriverRoleMapping> Mappings { get; }
    public string Canonical => GearRoutingContract.Pack(OutputKey, ChildGoalId, Child.CandidateId, GearRoutingContract.List(Child.Origins.Select(o => o.OriginId)), AxisId, DofId, BodyId,
        RequestedTransfer.ToString(), ActualTransfer.ToString(), GearRoutingContract.List(Mappings.Select(m => m.Canonical)));
}

public sealed class SharedDriverTransmissionCandidate
{
    public SharedDriverTransmissionCandidate(string candidateId, GenerationCandidate mechanism, IEnumerable<SharedDriverOutputBinding> outputs, TransmissionCommonMetrics metrics)
    {
        CandidateId = candidateId; Mechanism = mechanism; var all = outputs.Take(3).ToList(); if (all.Count != 2) throw new ArgumentException("Exactly two output bindings required.");
        Outputs = all.OrderBy(o => o.OutputKey, StringComparer.Ordinal).ToList().AsReadOnly(); Metrics = metrics;
    }
    public string CandidateId { get; }
    public GenerationCandidate Mechanism { get; }
    public ReadOnlyCollection<SharedDriverOutputBinding> Outputs { get; }
    public TransmissionCommonMetrics Metrics { get; }
    public string ContextId(string goalId) => SharedDriverTransmissionContract.Id("context", GearRoutingContract.Pack(goalId, CandidateId,
        GearRoutingContract.List(Outputs.Select(o => o.Canonical)), Metrics.Canonical));
}

public sealed class SharedDriverPairResult
{
    public SharedDriverPairResult(SharedDriverTransmissionCandidate? candidate, IEnumerable<Diagnostic> diagnostics)
    { Candidate = candidate; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics.Take(SharedDriverTransmissionContract.MaxPairDetails)); }
    public SharedDriverTransmissionCandidate? Candidate { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Candidate != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

/// <summary>The bounded all-observed pool is not the 16A retained list. Its canonical order drives joins.</summary>
public sealed class SharedDriverBranchObservation
{
    public SharedDriverBranchObservation(string outputKey, TransmissionGenerationResult result, IEnumerable<TransmissionMechanismCandidate> pool,
        int poolBytes, bool resourceStopped = false)
    {
        OutputKey = outputKey; Result = result; var list = pool.Take(SharedDriverTransmissionContract.MaxPoolCount + 1).ToList();
        if (list.Count > SharedDriverTransmissionContract.MaxPoolCount) throw new ArgumentException("Observed pool ceiling.");
        Pool = list.OrderBy(c => c.CandidateId, StringComparer.Ordinal).ToList().AsReadOnly(); PoolBytes = poolBytes; ResourceStopped = resourceStopped;
    }
    public string OutputKey { get; }
    public TransmissionGenerationResult Result { get; }
    public ReadOnlyCollection<TransmissionMechanismCandidate> Pool { get; }
    public int PoolBytes { get; }
    public bool ResourceStopped { get; }
    public bool SearchComplete => Result.SearchComplete && !ResourceStopped;
}

public sealed class SharedDriverPairObservation
{
    public SharedDriverPairObservation(string leftId, string rightId, string? candidateId, IEnumerable<Diagnostic> diagnostics)
    { LeftId = leftId; RightId = rightId; CandidateId = candidateId; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics.Take(16)); }
    public string LeftId { get; }
    public string RightId { get; }
    public string? CandidateId { get; }
    public bool Approved => CandidateId != null;
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public string Canonical => GearRoutingContract.Pack(LeftId, RightId, CandidateId ?? "", GearRoutingContract.List(Diagnostics.Select(d => GearRoutingContract.Pack(d.Code, d.SubjectId ?? "", d.Message))));
}

public sealed class SharedDriverTransmissionResult
{
    public SharedDriverTransmissionResult(SharedDriverTransmissionSearchPlan plan, SharedDriverTransmissionStatus status,
        IEnumerable<SharedDriverBranchObservation> branches, IEnumerable<SharedDriverTransmissionCandidate> candidates, int examinedPairs, int approvedPairs,
        int uniqueMerged, IEnumerable<SharedDriverPairObservation> pairDetails, string pairTranscriptHash, string limitingReason = "", string exactEmptyProof = "",
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        Plan = plan; Status = status; Branches = branches.OrderBy(b => b.OutputKey, StringComparer.Ordinal).ToList().AsReadOnly();
        var kept = candidates.Take(SharedDriverTransmissionContract.MaxReturned + 1).ToList(); if (kept.Count > SharedDriverTransmissionContract.MaxReturned) throw new ArgumentException("Shared return ceiling.");
        Candidates = kept.AsReadOnly(); ExaminedPairs = examinedPairs; ApprovedPairs = approvedPairs; UniqueMerged = uniqueMerged;
        PairDetails = pairDetails.Take(SharedDriverTransmissionContract.MaxPairDetails).ToList().AsReadOnly(); PairTranscriptHash = pairTranscriptHash;
        LimitingReason = limitingReason; ExactEmptyProof = exactEmptyProof; Diagnostics = DiagnosticOrdering.Canonicalize((diagnostics ?? Array.Empty<Diagnostic>()).Take(128));
    }
    public SharedDriverTransmissionSearchPlan Plan { get; }
    public string GoalId => Plan.GoalId;
    public SharedDriverTransmissionStatus Status { get; }
    public ReadOnlyCollection<SharedDriverBranchObservation> Branches { get; }
    public ReadOnlyCollection<SharedDriverTransmissionCandidate> Candidates { get; }
    public int ExaminedPairs { get; }
    public int ApprovedPairs { get; }
    public int RejectedPairs => ExaminedPairs - ApprovedPairs;
    public int UniqueMerged { get; }
    public long ObservedPairDomain => Branches.Count == 2 ? (long)Branches[0].Pool.Count * Branches[1].Pool.Count : 0;
    public bool AllBranchSearchesComplete => Branches.Count == 2 && Branches.All(b => b.SearchComplete);
    public bool ObservedPairDomainExhausted => Branches.Count == 2 && ExaminedPairs == ObservedPairDomain;
    public bool HasValidatedMultiOutputCandidates => Candidates.Count > 0;
    public bool SearchComplete => Status == SharedDriverTransmissionStatus.Complete || Status == SharedDriverTransmissionStatus.Infeasible;
    public bool ResultTruncated => UniqueMerged > Candidates.Count;
    public string RankingGuarantee => SearchComplete ? "globalTopKWithinDeclaredSupportedProduct" : "bestAmongExplored";
    public string LimitingReason { get; }
    public string ExactEmptyProof { get; }
    public int ConsumedGenerationWork => Branches.Sum(b => b.Result.ConsumedWork);
    public int ConsumedWork => ConsumedGenerationWork + ExaminedPairs;
    public ReadOnlyCollection<SharedDriverPairObservation> PairDetails { get; }
    public bool PairDetailsTruncated => ExaminedPairs > PairDetails.Count;
    public string PairTranscriptHash { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
}
