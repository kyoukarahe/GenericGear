using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class ExactRatioSynthesisContract
{
    public const string BackendId = "builtin-exact-ratio";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-exact-ratio-v1";
    public const string DefaultDeterminismProfile = "managed-exact-v1";
    public const string ResultFormat = "gear-invest.kinematic-synthesis-result";
    public const string ResultFormatVersion = "0.1";
}

public enum KinematicSynthesisStatus
{
    Complete,
    IncompleteBudget,
    Infeasible,
    InvalidInput,
    Cancelled,
}

public enum IntermediateShaftRole
{
    Compound,
    SimpleIdlerCompatible,
}

public sealed class ExactRatioSynthesisRequest
{
    public ExactRatioSynthesisRequest(
        Rational targetTransfer,
        int minTeeth,
        int maxTeeth,
        int minStages,
        int maxStages,
        bool allowCompound,
        int maxReturnedCandidates,
        long maxSearchExpansions,
        string determinismProfile = ExactRatioSynthesisContract.DefaultDeterminismProfile)
    {
        TargetTransfer = targetTransfer;
        MinTeeth = minTeeth;
        MaxTeeth = maxTeeth;
        MinStages = minStages;
        MaxStages = maxStages;
        AllowCompound = allowCompound;
        MaxReturnedCandidates = maxReturnedCandidates;
        MaxSearchExpansions = maxSearchExpansions;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public Rational TargetTransfer { get; }

    public int MinTeeth { get; }

    public int MaxTeeth { get; }

    public int MinStages { get; }

    public int MaxStages { get; }

    public bool AllowCompound { get; }

    public int MaxReturnedCandidates { get; }

    public long MaxSearchExpansions { get; }

    public string DeterminismProfile { get; }
}

public sealed class ExactGearStage
{
    public ExactGearStage(int index, int driverTeeth, int drivenTeeth)
    {
        Index = index;
        DriverTeeth = driverTeeth;
        DrivenTeeth = drivenTeeth;
    }

    public int Index { get; }

    public int DriverTeeth { get; }

    public int DrivenTeeth { get; }

    public Rational Transfer => new(-DriverTeeth, DrivenTeeth);
}

public sealed class KinematicSynthesisMetrics
{
    public KinematicSynthesisMetrics(
        int stageCount,
        int intermediateDofCount,
        int compoundIntermediateCount,
        int idlerCompatibleIntermediateCount,
        int estimatedGearBodyCount,
        int maximumToothCount,
        int minimumToothCount,
        long totalToothCount)
    {
        StageCount = stageCount;
        IntermediateDofCount = intermediateDofCount;
        CompoundIntermediateCount = compoundIntermediateCount;
        IdlerCompatibleIntermediateCount = idlerCompatibleIntermediateCount;
        EstimatedGearBodyCount = estimatedGearBodyCount;
        MaximumToothCount = maximumToothCount;
        MinimumToothCount = minimumToothCount;
        ToothSpan = maximumToothCount - minimumToothCount;
        TotalToothCount = totalToothCount;
    }

    public int StageCount { get; }

    public int IntermediateDofCount { get; }

    public int CompoundIntermediateCount { get; }

    public int IdlerCompatibleIntermediateCount { get; }

    public int EstimatedGearBodyCount { get; }

    public int MaximumToothCount { get; }

    public int MinimumToothCount { get; }

    public int ToothSpan { get; }

    public long TotalToothCount { get; }
}

public sealed class KinematicSynthesisFingerprint
{
    public KinematicSynthesisFingerprint(string backendId, string backendVersion, string determinismProfile)
    {
        BackendId = backendId ?? throw new ArgumentNullException(nameof(backendId));
        BackendVersion = backendVersion ?? throw new ArgumentNullException(nameof(backendVersion));
        DeterminismProfile = determinismProfile ?? throw new ArgumentNullException(nameof(determinismProfile));
    }

    public string BackendId { get; }

    public string BackendVersion { get; }

    public string DeterminismProfile { get; }

    public string SemanticVersion => BackendId + "-v" + BackendVersion;
}

public sealed class KinematicSearchSummary
{
    public KinematicSearchSummary(
        IEnumerable<int> stageOptionsConsidered,
        long searchExpansions,
        long rawMatches,
        long deduplicatedCandidates,
        int returnedCandidates,
        bool resultTruncated,
        bool searchComplete)
    {
        StageOptionsConsidered = (stageOptionsConsidered ?? throw new ArgumentNullException(nameof(stageOptionsConsidered)))
            .OrderBy(value => value)
            .ToList()
            .AsReadOnly();
        SearchExpansions = searchExpansions;
        RawMatches = rawMatches;
        DeduplicatedCandidates = deduplicatedCandidates;
        ReturnedCandidates = returnedCandidates;
        ResultTruncated = resultTruncated;
        SearchComplete = searchComplete;
    }

    public ReadOnlyCollection<int> StageOptionsConsidered { get; }

    public long SearchExpansions { get; }

    public long RawMatches { get; }

    public long DeduplicatedCandidates { get; }

    public int ReturnedCandidates { get; }

    public bool ResultTruncated { get; }

    public bool SearchComplete { get; }
}

public sealed class KinematicSynthesisCandidate
{
    internal KinematicSynthesisCandidate(
        string kinematicCandidateId,
        string canonicalSignature,
        IEnumerable<ExactGearStage> orderedStages,
        KinematicSpecification kinematicMechanism,
        KinematicSolution exactSolution,
        IEnumerable<IntermediateShaftRole> intermediateRoles,
        Rational exactTransfer,
        KinematicSynthesisMetrics metrics,
        ValidationBundle validation)
    {
        KinematicCandidateId = kinematicCandidateId ?? throw new ArgumentNullException(nameof(kinematicCandidateId));
        CanonicalSignature = canonicalSignature ?? throw new ArgumentNullException(nameof(canonicalSignature));
        OrderedStages = (orderedStages ?? throw new ArgumentNullException(nameof(orderedStages))).ToList().AsReadOnly();
        KinematicMechanism = kinematicMechanism ?? throw new ArgumentNullException(nameof(kinematicMechanism));
        ExactSolution = exactSolution ?? throw new ArgumentNullException(nameof(exactSolution));
        IntermediateRoles = (intermediateRoles ?? throw new ArgumentNullException(nameof(intermediateRoles))).ToList().AsReadOnly();
        ExactTransfer = exactTransfer;
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public string KinematicCandidateId { get; }

    public string CanonicalSignature { get; }

    public ReadOnlyCollection<ExactGearStage> OrderedStages { get; }

    public KinematicSpecification KinematicMechanism { get; }

    public KinematicSolution ExactSolution { get; }

    public ReadOnlyCollection<IntermediateShaftRole> IntermediateRoles { get; }

    public Rational ExactTransfer { get; }

    public KinematicSynthesisMetrics Metrics { get; }

    public ValidationBundle Validation { get; }
}

public sealed class KinematicSynthesisResult
{
    public KinematicSynthesisResult(
        ExactRatioSynthesisRequest normalizedRequest,
        string requestCanonicalRepresentation,
        string requestId,
        KinematicSynthesisStatus status,
        IEnumerable<KinematicSynthesisCandidate> candidates,
        IEnumerable<Diagnostic> diagnostics,
        KinematicSearchSummary searchSummary,
        KinematicSynthesisFingerprint generatorFingerprint)
    {
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Status = status;
        Candidates = (candidates ?? throw new ArgumentNullException(nameof(candidates))).ToList().AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
        GeneratorFingerprint = generatorFingerprint ?? throw new ArgumentNullException(nameof(generatorFingerprint));
    }

    public ExactRatioSynthesisRequest NormalizedRequest { get; }

    public string RequestCanonicalRepresentation { get; }

    public string RequestId { get; }

    public KinematicSynthesisStatus Status { get; }

    public ReadOnlyCollection<KinematicSynthesisCandidate> Candidates { get; }

    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }

    public KinematicSearchSummary SearchSummary { get; }

    public KinematicSynthesisFingerprint GeneratorFingerprint { get; }

    public bool IsSuccess =>
        Status == KinematicSynthesisStatus.Complete &&
        Candidates.Count > 0 &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

internal sealed class KinematicSynthesisCandidateComparer : IComparer<KinematicSynthesisCandidate>
{
    public static readonly KinematicSynthesisCandidateComparer Instance = new();

    private KinematicSynthesisCandidateComparer()
    {
    }

    public int Compare(KinematicSynthesisCandidate? left, KinematicSynthesisCandidate? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = left.Metrics.StageCount.CompareTo(right.Metrics.StageCount);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.EstimatedGearBodyCount.CompareTo(right.Metrics.EstimatedGearBodyCount);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.CompoundIntermediateCount.CompareTo(right.Metrics.CompoundIntermediateCount);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.MaximumToothCount.CompareTo(right.Metrics.MaximumToothCount);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.ToothSpan.CompareTo(right.Metrics.ToothSpan);
        if (comparison != 0) return comparison;
        comparison = left.Metrics.TotalToothCount.CompareTo(right.Metrics.TotalToothCount);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.CanonicalSignature, right.CanonicalSignature);
    }
}
