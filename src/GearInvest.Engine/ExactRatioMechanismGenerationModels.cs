using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class ExactRatioMechanismGenerationContract
{
    public const string BackendId = "builtin-exact-ratio-mechanism";
    public const string BackendVersion = "1";
    public const string BackendSemanticVersion = "builtin-exact-ratio-mechanism-v1";
    public const string DefaultDeterminismProfile = "portable-managed-generation-v1";
    public const string ResultFormat = "gear-invest.exact-ratio-generation-result";
    public const string ResultFormatVersion = "0.1";
}

public enum ExactRatioMechanismGenerationStatus
{
    Complete,
    IncompleteBudget,
    Infeasible,
    InvalidInput,
    Cancelled,
}

public sealed class ExactRatioMechanismGenerationRequest
{
    public ExactRatioMechanismGenerationRequest(
        ExactRatioSynthesisRequest synthesisRequest,
        SpatialLayoutRequest layoutRequest,
        int maxKinematicCandidatesToLayout,
        int maxLayoutsPerKinematicCandidate,
        int maxReturnedMechanisms,
        string determinismProfile = ExactRatioMechanismGenerationContract.DefaultDeterminismProfile)
    {
        SynthesisRequest = synthesisRequest ?? throw new ArgumentNullException(nameof(synthesisRequest));
        LayoutRequest = layoutRequest ?? throw new ArgumentNullException(nameof(layoutRequest));
        MaxKinematicCandidatesToLayout = maxKinematicCandidatesToLayout;
        MaxLayoutsPerKinematicCandidate = maxLayoutsPerKinematicCandidate;
        MaxReturnedMechanisms = maxReturnedMechanisms;
        DeterminismProfile = determinismProfile ?? string.Empty;
    }

    public ExactRatioSynthesisRequest SynthesisRequest { get; }

    public SpatialLayoutRequest LayoutRequest { get; }

    public int MaxKinematicCandidatesToLayout { get; }

    public int MaxLayoutsPerKinematicCandidate { get; }

    public int MaxReturnedMechanisms { get; }

    public string DeterminismProfile { get; }
}

public sealed class ExactRatioMechanismGenerationFingerprint
{
    public ExactRatioMechanismGenerationFingerprint(
        string backendId,
        string backendVersion,
        string determinismProfile)
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

public sealed class ExactRatioMechanismLayoutAttempt
{
    public ExactRatioMechanismLayoutAttempt(
        int kinematicRank,
        string kinematicCandidateId,
        SpatialLayoutResult result,
        int selectedLayoutCount)
    {
        KinematicRank = kinematicRank;
        KinematicCandidateId = kinematicCandidateId ?? throw new ArgumentNullException(nameof(kinematicCandidateId));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        SelectedLayoutCount = selectedLayoutCount;
    }

    public int KinematicRank { get; }

    public string KinematicCandidateId { get; }

    public SpatialLayoutResult Result { get; }

    public int SelectedLayoutCount { get; }
}

public sealed class ExactRatioMechanismSearchSummary
{
    public ExactRatioMechanismSearchSummary(
        bool synthesisResultTruncated,
        int kinematicCandidatesAvailable,
        int kinematicCandidatesSelected,
        int kinematicCandidatesSkippedByOrchestrationCap,
        int layoutAttempts,
        int incompleteLayoutAttempts,
        int infeasibleLayoutAttempts,
        int truncatedLayoutAttempts,
        int layoutCandidatesSelected,
        int composedCandidates,
        int returnedCandidates,
        bool finalResultTruncated,
        bool searchComplete)
    {
        SynthesisResultTruncated = synthesisResultTruncated;
        KinematicCandidatesAvailable = kinematicCandidatesAvailable;
        KinematicCandidatesSelected = kinematicCandidatesSelected;
        KinematicCandidatesSkippedByOrchestrationCap = kinematicCandidatesSkippedByOrchestrationCap;
        LayoutAttempts = layoutAttempts;
        IncompleteLayoutAttempts = incompleteLayoutAttempts;
        InfeasibleLayoutAttempts = infeasibleLayoutAttempts;
        TruncatedLayoutAttempts = truncatedLayoutAttempts;
        LayoutCandidatesSelected = layoutCandidatesSelected;
        ComposedCandidates = composedCandidates;
        ReturnedCandidates = returnedCandidates;
        FinalResultTruncated = finalResultTruncated;
        SearchComplete = searchComplete;
    }

    public bool SynthesisResultTruncated { get; }
    public int KinematicCandidatesAvailable { get; }
    public int KinematicCandidatesSelected { get; }
    public int KinematicCandidatesSkippedByOrchestrationCap { get; }
    public int LayoutAttempts { get; }
    public int IncompleteLayoutAttempts { get; }
    public int InfeasibleLayoutAttempts { get; }
    public int TruncatedLayoutAttempts { get; }
    public int LayoutCandidatesSelected { get; }
    public int ComposedCandidates { get; }
    public int ReturnedCandidates { get; }
    public bool FinalResultTruncated { get; }
    public bool SearchComplete { get; }
}

public sealed class ExactRatioMechanismCandidateProvenance
{
    public ExactRatioMechanismCandidateProvenance(
        ExactRatioMechanismGenerationRequest generationRequest,
        string generationRequestCanonicalRepresentation,
        string generationRequestId,
        ExactRatioMechanismGenerationFingerprint generationFingerprint,
        KinematicSynthesisFingerprint synthesisFingerprint,
        KinematicSearchSummary synthesisSearchSummary,
        string kinematicCandidateId,
        int kinematicRank,
        SpatialLayoutFingerprint layoutFingerprint,
        string layoutRequestCanonicalRepresentation,
        string layoutRequestId,
        SpatialLayoutSearchSummary layoutSearchSummary,
        string spatialCandidateId,
        int spatialRank)
    {
        GenerationRequest = generationRequest ?? throw new ArgumentNullException(nameof(generationRequest));
        GenerationRequestCanonicalRepresentation = generationRequestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(generationRequestCanonicalRepresentation));
        GenerationRequestId = generationRequestId ?? throw new ArgumentNullException(nameof(generationRequestId));
        GenerationFingerprint = generationFingerprint ?? throw new ArgumentNullException(nameof(generationFingerprint));
        SynthesisFingerprint = synthesisFingerprint ?? throw new ArgumentNullException(nameof(synthesisFingerprint));
        SynthesisSearchSummary = synthesisSearchSummary ?? throw new ArgumentNullException(nameof(synthesisSearchSummary));
        KinematicCandidateId = kinematicCandidateId ?? throw new ArgumentNullException(nameof(kinematicCandidateId));
        KinematicRank = kinematicRank;
        LayoutFingerprint = layoutFingerprint ?? throw new ArgumentNullException(nameof(layoutFingerprint));
        LayoutRequestCanonicalRepresentation = layoutRequestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(layoutRequestCanonicalRepresentation));
        LayoutRequestId = layoutRequestId ?? throw new ArgumentNullException(nameof(layoutRequestId));
        LayoutSearchSummary = layoutSearchSummary ?? throw new ArgumentNullException(nameof(layoutSearchSummary));
        SpatialCandidateId = spatialCandidateId ?? throw new ArgumentNullException(nameof(spatialCandidateId));
        SpatialRank = spatialRank;
    }

    public ExactRatioMechanismGenerationRequest GenerationRequest { get; }
    public string GenerationRequestCanonicalRepresentation { get; }
    public string GenerationRequestId { get; }
    public ExactRatioMechanismGenerationFingerprint GenerationFingerprint { get; }
    public KinematicSynthesisFingerprint SynthesisFingerprint { get; }
    public KinematicSearchSummary SynthesisSearchSummary { get; }
    public string KinematicCandidateId { get; }
    public int KinematicRank { get; }
    public SpatialLayoutFingerprint LayoutFingerprint { get; }
    public string LayoutRequestCanonicalRepresentation { get; }
    public string LayoutRequestId { get; }
    public SpatialLayoutSearchSummary LayoutSearchSummary { get; }
    public string SpatialCandidateId { get; }
    public int SpatialRank { get; }
}

public sealed class ExactRatioMechanismMetrics
{
    public ExactRatioMechanismMetrics(
        KinematicSynthesisMetrics kinematic,
        SpatialLayoutMetrics spatial,
        int kinematicRank,
        int spatialRank)
    {
        Kinematic = kinematic ?? throw new ArgumentNullException(nameof(kinematic));
        Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        KinematicRank = kinematicRank;
        SpatialRank = spatialRank;
    }

    public KinematicSynthesisMetrics Kinematic { get; }
    public SpatialLayoutMetrics Spatial { get; }
    public int KinematicRank { get; }
    public int SpatialRank { get; }
}

public sealed class ExactRatioMechanismCandidate
{
    public ExactRatioMechanismCandidate(
        GenerationCandidate candidate,
        KinematicSynthesisCandidate kinematicSource,
        SpatialLayoutCandidate spatialSource,
        ExactRatioMechanismCandidateProvenance provenance,
        ExactRatioMechanismMetrics metrics)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        KinematicSource = kinematicSource ?? throw new ArgumentNullException(nameof(kinematicSource));
        SpatialSource = spatialSource ?? throw new ArgumentNullException(nameof(spatialSource));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public GenerationCandidate Candidate { get; }
    public KinematicSynthesisCandidate KinematicSource { get; }
    public SpatialLayoutCandidate SpatialSource { get; }
    public ExactRatioMechanismCandidateProvenance Provenance { get; }
    public ExactRatioMechanismMetrics Metrics { get; }
}

public sealed class ExactRatioMechanismGenerationResult
{
    public ExactRatioMechanismGenerationResult(
        ExactRatioMechanismGenerationRequest normalizedRequest,
        string requestCanonicalRepresentation,
        string requestId,
        ExactRatioMechanismGenerationFingerprint generationFingerprint,
        ExactRatioMechanismGenerationStatus status,
        IEnumerable<ExactRatioMechanismCandidate> candidates,
        IEnumerable<Diagnostic> diagnostics,
        ExactRatioMechanismSearchSummary searchSummary,
        KinematicSynthesisResult? synthesisResult,
        IEnumerable<ExactRatioMechanismLayoutAttempt> layoutAttempts)
    {
        NormalizedRequest = normalizedRequest ?? throw new ArgumentNullException(nameof(normalizedRequest));
        RequestCanonicalRepresentation = requestCanonicalRepresentation ?? throw new ArgumentNullException(nameof(requestCanonicalRepresentation));
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        GenerationFingerprint = generationFingerprint ?? throw new ArgumentNullException(nameof(generationFingerprint));
        Status = status;
        Candidates = (candidates ?? throw new ArgumentNullException(nameof(candidates))).ToList().AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
        SearchSummary = searchSummary ?? throw new ArgumentNullException(nameof(searchSummary));
        SynthesisResult = synthesisResult;
        LayoutAttempts = (layoutAttempts ?? throw new ArgumentNullException(nameof(layoutAttempts))).ToList().AsReadOnly();
    }

    public ExactRatioMechanismGenerationRequest NormalizedRequest { get; }
    public string RequestCanonicalRepresentation { get; }
    public string RequestId { get; }
    public ExactRatioMechanismGenerationFingerprint GenerationFingerprint { get; }
    public ExactRatioMechanismGenerationStatus Status { get; }
    public ReadOnlyCollection<ExactRatioMechanismCandidate> Candidates { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public ExactRatioMechanismSearchSummary SearchSummary { get; }
    public KinematicSynthesisResult? SynthesisResult { get; }
    public ReadOnlyCollection<ExactRatioMechanismLayoutAttempt> LayoutAttempts { get; }

    public bool IsSuccess =>
        Status == ExactRatioMechanismGenerationStatus.Complete &&
        Candidates.Count > 0 &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public static class ExactRatioMechanismGenerationRequestIdentity
{
    public static string BuildCanonicalRepresentation(ExactRatioMechanismGenerationRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var synthesis = ExactRatioSynthesizer.BuildRequestCanonicalRepresentation(request.SynthesisRequest);
        var layout = request.LayoutRequest;
        var layoutCanonical =
            SpatialLayoutContract.BackendSemanticVersion +
            "|profile=" + LengthPrefixed(layout.DeterminismProfile) +
            "|rootX=" + layout.RootAxisX.ToString(CultureInfo.InvariantCulture) +
            "|rootY=" + layout.RootAxisY.ToString(CultureInfo.InvariantCulture) +
            "|pitchRadiusTicksPerTooth=" + layout.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture) +
            "|maxLayers=" + layout.MaxLayers.ToString(CultureInfo.InvariantCulture) +
            "|maxReturnedLayouts=" + layout.MaxReturnedLayouts.ToString(CultureInfo.InvariantCulture) +
            "|maxPlacementExpansions=" + layout.MaxPlacementExpansions.ToString(CultureInfo.InvariantCulture) +
            "|clearanceTicks=" + layout.ClearanceTicks.ToString(CultureInfo.InvariantCulture);
        return ExactRatioMechanismGenerationContract.BackendSemanticVersion +
            "|profile=" + LengthPrefixed(request.DeterminismProfile) +
            "|synthesis=" + LengthPrefixed(synthesis) +
            "|layout=" + LengthPrefixed(layoutCanonical) +
            "|maxKinematicCandidatesToLayout=" + request.MaxKinematicCandidatesToLayout.ToString(CultureInfo.InvariantCulture) +
            "|maxLayoutsPerKinematicCandidate=" + request.MaxLayoutsPerKinematicCandidate.ToString(CultureInfo.InvariantCulture) +
            "|maxReturnedMechanisms=" + request.MaxReturnedMechanisms.ToString(CultureInfo.InvariantCulture);
    }

    public static string ComputeRequestId(ExactRatioMechanismGenerationRequest request)
    {
        return ComputeRequestId(BuildCanonicalRepresentation(request));
    }

    public static string ComputeRequestId(string canonicalRepresentation)
    {
        if (canonicalRepresentation is null)
        {
            throw new ArgumentNullException(nameof(canonicalRepresentation));
        }

        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonicalRepresentation));
        }

        var builder = new StringBuilder("generation-request-sha256:");
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}
