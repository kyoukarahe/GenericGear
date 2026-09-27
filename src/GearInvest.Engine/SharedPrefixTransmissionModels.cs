using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class SharedPrefixTransmissionCandidate
{
    public SharedPrefixTransmissionCandidate(string candidateId, string hypothesisId, TransmissionMechanismCandidate prefix, GenerationCandidate mechanism,
        Rational splitterTransfer, IEnumerable<SharedDriverRoleMapping> prefixMappings, IEnumerable<SharedDriverOutputBinding> outputs, TransmissionCommonMetrics metrics)
    {
        CandidateId = candidateId; HypothesisId = hypothesisId; Prefix = prefix; Mechanism = mechanism; SplitterTransfer = splitterTransfer; Metrics = metrics;
        PrefixMappings = prefixMappings.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.LocalId, StringComparer.Ordinal).ToList().AsReadOnly();
        Outputs = outputs.OrderBy(o => o.OutputKey, StringComparer.Ordinal).ToList().AsReadOnly();
        if (Outputs.Count != 2 || PrefixMappings.Count > 64) throw new ArgumentException("Bounded prefix and two output bindings required.");
    }
    public string CandidateId { get; }
    public string HypothesisId { get; }
    public TransmissionMechanismCandidate Prefix { get; }
    public GenerationCandidate Mechanism { get; }
    public Rational SplitterTransfer { get; }
    public ReadOnlyCollection<SharedDriverRoleMapping> PrefixMappings { get; }
    public ReadOnlyCollection<SharedDriverOutputBinding> Outputs { get; }
    public TransmissionCommonMetrics Metrics { get; }
    public int PrefixIdlers => Prefix.Mechanism.Spatial.Bodies.Count - 2;
    public int SplitterCount => 1;
    public int SuffixIdlers => Metrics.Idlers - PrefixIdlers - SplitterCount;
    public string ContextId(string goalId) => SharedPrefixTransmissionContract.Id("context", GearRoutingContract.Pack(goalId, CandidateId, HypothesisId, Prefix.CandidateId,
        GearRoutingContract.List(Prefix.Origins.Select(o => o.OriginId)), SplitterTransfer.ToString(), GearRoutingContract.List(PrefixMappings.Select(m => m.Canonical)),
        GearRoutingContract.List(Outputs.Select(o => o.Canonical)), Metrics.Canonical));
}

public sealed class SharedPrefixCompositionResult
{
    public SharedPrefixCompositionResult(SharedPrefixTransmissionCandidate? candidate, IEnumerable<Diagnostic> diagnostics)
    { Candidate = candidate; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics.Take(64)); }
    public SharedPrefixTransmissionCandidate? Candidate { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Candidate != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

/// <summary>A non-executed sibling remains non-executed; an empty-factor proof does not relabel it complete.</summary>
public sealed class SharedPrefixStageObservation
{
    public SharedPrefixStageObservation(string role, SharedDriverBranchObservation? observation, string notExecutedReason = "")
    { Role = role; Observation = observation; NotExecutedReason = notExecutedReason; }
    public string Role { get; }
    public SharedDriverBranchObservation? Observation { get; }
    public string NotExecutedReason { get; }
    public bool SearchComplete => Observation?.SearchComplete == true;
    public bool ProvenEmpty => SearchComplete && Observation!.Pool.Count == 0;
    public int PoolCount => Observation?.Pool.Count ?? 0;
    public int Consumed => Observation?.Result.ConsumedWork ?? 0;
}

public sealed class SharedPrefixHypothesisObservation
{
    public SharedPrefixHypothesisObservation(string hypothesisId, IEnumerable<SharedPrefixStageObservation> stages, int examined, int approved, string closureProof = "")
    { HypothesisId = hypothesisId; Stages = stages.ToList().AsReadOnly(); Examined = examined; Approved = approved; ClosureProof = closureProof; }
    public string HypothesisId { get; }
    public ReadOnlyCollection<SharedPrefixStageObservation> Stages { get; }
    public int Examined { get; }
    public int Approved { get; }
    public int Rejected => Examined - Approved;
    public long ObservedProduct => Stages.Count == 3 ? (long)Stages[0].PoolCount * Stages[1].PoolCount * Stages[2].PoolCount : 0;
    public string ClosureProof { get; }
    public bool AllStagesComplete => Stages.Count == 3 && Stages.All(s => s.SearchComplete);
    public bool Closed => ClosureProof.Length != 0 || AllStagesComplete && Examined == ObservedProduct;
}

public sealed class SharedPrefixTripleObservation
{
    public SharedPrefixTripleObservation(string hypothesisId, string prefixId, string leftId, string rightId, string? candidateId, IEnumerable<Diagnostic> diagnostics)
    { HypothesisId = hypothesisId; PrefixId = prefixId; LeftId = leftId; RightId = rightId; CandidateId = candidateId; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics.Take(16)); }
    public string HypothesisId { get; }
    public string PrefixId { get; }
    public string LeftId { get; }
    public string RightId { get; }
    public string? CandidateId { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public string Canonical => GearRoutingContract.Pack(HypothesisId, PrefixId, LeftId, RightId, CandidateId ?? "", GearRoutingContract.List(Diagnostics.Select(d => GearRoutingContract.Pack(d.Code, d.SubjectId ?? "", d.Message))));
}

public sealed class SharedPrefixTransmissionResult
{
    public SharedPrefixTransmissionResult(SharedPrefixTransmissionSearchPlan plan, SharedPrefixTransmissionStatus status, IEnumerable<SharedPrefixHypothesisObservation> hypotheses,
        IEnumerable<SharedPrefixTransmissionCandidate> candidates, int uniqueMerged, IEnumerable<SharedPrefixTripleObservation> details, string transcript,
        string limitingReason = "", IEnumerable<Diagnostic>? diagnostics = null)
    {
        Plan = plan; Status = status; Hypotheses = hypotheses.ToList().AsReadOnly(); Candidates = candidates.ToList().AsReadOnly(); UniqueMerged = uniqueMerged;
        Details = details.ToList().AsReadOnly(); Transcript = transcript; LimitingReason = limitingReason; Diagnostics = DiagnosticOrdering.Canonicalize((diagnostics ?? Array.Empty<Diagnostic>()).Take(128));
        if (Hypotheses.Count > 8 || Candidates.Count > 32 || Details.Count > 64) throw new ArgumentException("Shared-prefix result resource envelope.");
    }
    public SharedPrefixTransmissionSearchPlan Plan { get; }
    public string GoalId => Plan.GoalId;
    public SharedPrefixTransmissionStatus Status { get; }
    public ReadOnlyCollection<SharedPrefixHypothesisObservation> Hypotheses { get; }
    public ReadOnlyCollection<SharedPrefixTransmissionCandidate> Candidates { get; }
    public int UniqueMerged { get; }
    public ReadOnlyCollection<SharedPrefixTripleObservation> Details { get; }
    public string Transcript { get; }
    public string LimitingReason { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public int ExaminedTriples => Hypotheses.Sum(h => h.Examined);
    public int ApprovedTriples => Hypotheses.Sum(h => h.Approved);
    public int RejectedTriples => ExaminedTriples - ApprovedTriples;
    public long ObservedTripleDomain => Hypotheses.Sum(h => h.ObservedProduct);
    public bool HasValidatedCandidates => Candidates.Count > 0;
    public bool AllRelevantSearchesClosed => Plan.IsSupported && Hypotheses.Count == Plan.Hypotheses.Count && Hypotheses.All(h => h.ClosureProof.Length != 0 || h.AllStagesComplete);
    public bool ObservedJoinExhausted => Hypotheses.Count == Plan.Hypotheses.Count && Hypotheses.All(h => h.Examined == h.ObservedProduct);
    public bool SearchComplete => Status == SharedPrefixTransmissionStatus.Complete || Status == SharedPrefixTransmissionStatus.Infeasible;
    public bool ResultTruncated => UniqueMerged > Candidates.Count;
    public string RankingGuarantee => SearchComplete ? "globalTopKWithinDeclaredSupportedHypothesisUnion" : "bestAmongExplored";
    public int ConsumedPrefixWork => Hypotheses.Sum(h => h.Stages.FirstOrDefault(s => s.Role == "prefix")?.Consumed ?? 0);
    public int ConsumedSuffixWork => Hypotheses.Sum(h => h.Stages.Where(s => s.Role != "prefix").Sum(s => s.Consumed));
    public int ConsumedWork => ConsumedPrefixWork + ConsumedSuffixWork + ExaminedTriples;
    public int PoolBytes => Hypotheses.Sum(h => h.Stages.Sum(s => s.Observation?.PoolBytes ?? 0));
}
