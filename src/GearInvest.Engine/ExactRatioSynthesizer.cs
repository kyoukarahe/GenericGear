using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class ExactRatioSynthesizer
{
    public KinematicSynthesisResult Synthesize(
        ExactRatioSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var canonicalRequest = BuildRequestCanonicalRepresentation(request);
        var requestId = Hash("kinematic-request-sha256:", canonicalRequest);
        var fingerprint = new KinematicSynthesisFingerprint(
            ExactRatioSynthesisContract.BackendId,
            ExactRatioSynthesisContract.BackendVersion,
            request.DeterminismProfile);
        var inputDiagnostics = ValidateRequest(request);
        if (inputDiagnostics.Count > 0)
        {
            return CreateTerminalResult(
                request,
                canonicalRequest,
                requestId,
                KinematicSynthesisStatus.InvalidInput,
                inputDiagnostics,
                fingerprint,
                searchComplete: false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateTerminalResult(
                request,
                canonicalRequest,
                requestId,
                KinematicSynthesisStatus.Cancelled,
                new[]
                {
                    new Diagnostic(
                        DiagnosticCodes.SynthesisCancelled,
                        DiagnosticSeverity.Warning,
                        "Exact-ratio synthesis was cancelled before search began."),
                },
                fingerprint,
                searchComplete: false);
        }

        var state = new SearchState(request, fingerprint, cancellationToken);
        var targetSign = request.TargetTransfer.Sign;
        for (var stageCount = request.MinStages; stageCount <= request.MaxStages; stageCount++)
        {
            var stageSign = stageCount % 2 == 0 ? 1 : -1;
            if (stageSign != targetSign)
            {
                continue;
            }

            state.StageOptionsConsidered.Add(stageCount);
            if (!SearchStageCount(state, stageCount))
            {
                break;
            }
        }

        var diagnostics = new List<Diagnostic>(state.Diagnostics);
        KinematicSynthesisStatus status;
        var searchComplete = !state.BudgetExhausted && !state.Cancelled;
        if (state.Cancelled)
        {
            status = KinematicSynthesisStatus.Cancelled;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisCancelled,
                DiagnosticSeverity.Warning,
                "Exact-ratio synthesis was cancelled before the bounded search completed."));
        }
        else if (state.BudgetExhausted)
        {
            status = KinematicSynthesisStatus.IncompleteBudget;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisBudgetExhausted,
                DiagnosticSeverity.Warning,
                "The deterministic expansion budget of '" +
                request.MaxSearchExpansions.ToString(CultureInfo.InvariantCulture) +
                "' was exhausted before the bounded search completed.",
                "maxSearchExpansions"));
        }
        else if (state.DeduplicatedCandidateCount == 0)
        {
            status = KinematicSynthesisStatus.Infeasible;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisInfeasible,
                DiagnosticSeverity.Info,
                "No exact fixed-axis external-gear realization exists within the requested bounds."));
        }
        else
        {
            status = KinematicSynthesisStatus.Complete;
        }

        var orderedCandidates = state.TopCandidates
            .OrderBy(candidate => candidate, KinematicSynthesisCandidateComparer.Instance)
            .ToList();
        var resultTruncated = state.DeduplicatedCandidateCount > orderedCandidates.Count;
        if (resultTruncated)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisResultTruncated,
                DiagnosticSeverity.Info,
                "Only the highest-ranked '" +
                request.MaxReturnedCandidates.ToString(CultureInfo.InvariantCulture) +
                "' candidates among the exact candidates discovered by this search were returned.",
                "maxReturnedCandidates"));
        }

        var summary = new KinematicSearchSummary(
            state.StageOptionsConsidered,
            state.SearchExpansions,
            state.RawMatches,
            state.DeduplicatedCandidateCount,
            orderedCandidates.Count,
            resultTruncated,
            searchComplete);
        return new KinematicSynthesisResult(
            request,
            canonicalRequest,
            requestId,
            status,
            orderedCandidates,
            diagnostics,
            summary,
            fingerprint);
    }

    internal static string BuildRequestCanonicalRepresentation(ExactRatioSynthesisRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return ExactRatioSynthesisContract.BackendSemanticVersion +
            "|profile=" + request.DeterminismProfile.Length.ToString(CultureInfo.InvariantCulture) + ":" + request.DeterminismProfile +
            "|target=" + request.TargetTransfer +
            "|minTeeth=" + request.MinTeeth.ToString(CultureInfo.InvariantCulture) +
            "|maxTeeth=" + request.MaxTeeth.ToString(CultureInfo.InvariantCulture) +
            "|minStages=" + request.MinStages.ToString(CultureInfo.InvariantCulture) +
            "|maxStages=" + request.MaxStages.ToString(CultureInfo.InvariantCulture) +
            "|allowCompound=" + request.AllowCompound.ToString().ToLowerInvariant() +
            "|maxReturnedCandidates=" + request.MaxReturnedCandidates.ToString(CultureInfo.InvariantCulture) +
            "|maxSearchExpansions=" + request.MaxSearchExpansions.ToString(CultureInfo.InvariantCulture);
    }

    private static List<Diagnostic> ValidateRequest(ExactRatioSynthesisRequest request)
    {
        var diagnostics = new List<Diagnostic>();
        if (request.TargetTransfer.IsZero)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisInvalidTarget,
                DiagnosticSeverity.Error,
                "A zero transfer cannot be realized by a serial external gear train.",
                "targetTransfer"));
        }

        if (request.MinTeeth <= 0 || request.MaxTeeth <= 0 || request.MinTeeth > request.MaxTeeth)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisInvalidToothBounds,
                DiagnosticSeverity.Error,
                "Tooth bounds must be positive and minTeeth must not exceed maxTeeth.",
                "toothBounds"));
        }

        if (request.MinStages < 1 || request.MaxStages > 3 || request.MinStages > request.MaxStages)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisInvalidStageBounds,
                DiagnosticSeverity.Error,
                "The built-in exact backend supports an inclusive stage range within 1..3.",
                "stageBounds"));
        }

        if (request.MaxReturnedCandidates <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisInvalidCandidateLimit,
                DiagnosticSeverity.Error,
                "maxReturnedCandidates must be positive.",
                "maxReturnedCandidates"));
        }

        if (request.MaxSearchExpansions <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisInvalidSearchBudget,
                DiagnosticSeverity.Error,
                "maxSearchExpansions must be positive.",
                "maxSearchExpansions"));
        }

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                ExactRatioSynthesisContract.DefaultDeterminismProfile))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisUnsupportedDeterminismProfile,
                DiagnosticSeverity.Error,
                "The built-in exact backend supports only determinism profile '" +
                ExactRatioSynthesisContract.DefaultDeterminismProfile + "'.",
                "determinismProfile"));
        }

        return diagnostics;
    }

    private static bool SearchStageCount(SearchState state, int stageCount)
    {
        var request = state.Request;
        var absoluteNumerator = BigInteger.Abs(request.TargetTransfer.Numerator);
        var denominator = request.TargetTransfer.Denominator;
        var minimumProduct = BigInteger.Pow(new BigInteger(request.MinTeeth), stageCount);
        var maximumProduct = BigInteger.Pow(new BigInteger(request.MaxTeeth), stageCount);
        var lowerDriverProduct = BigInteger.Max(
            minimumProduct,
            CeilingDivide(absoluteNumerator * minimumProduct, denominator));
        var upperDriverProduct = BigInteger.Min(
            maximumProduct,
            (absoluteNumerator * maximumProduct) / denominator);
        if (lowerDriverProduct > upperDriverProduct)
        {
            return true;
        }

        var driverTeeth = new int[stageCount];
        return EnumerateDriverTuples(
            state,
            stageCount,
            index: 0,
            currentProduct: BigInteger.One,
            lowerDriverProduct,
            upperDriverProduct,
            absoluteNumerator,
            denominator,
            minimumProduct,
            maximumProduct,
            driverTeeth);
    }

    private static bool EnumerateDriverTuples(
        SearchState state,
        int stageCount,
        int index,
        BigInteger currentProduct,
        BigInteger lowerDriverProduct,
        BigInteger upperDriverProduct,
        BigInteger targetNumerator,
        BigInteger targetDenominator,
        BigInteger minimumProduct,
        BigInteger maximumProduct,
        int[] driverTeeth)
    {
        if (state.CancellationToken.IsCancellationRequested)
        {
            state.Cancelled = true;
            return false;
        }

        if (index == stageCount)
        {
            if (state.SearchExpansions >= state.Request.MaxSearchExpansions)
            {
                state.BudgetExhausted = true;
                return false;
            }

            state.SearchExpansions++;
            var drivenDividend = currentProduct * targetDenominator;
            if (drivenDividend % targetNumerator != BigInteger.Zero)
            {
                return true;
            }

            var requiredDrivenProduct = drivenDividend / targetNumerator;
            if (requiredDrivenProduct < minimumProduct || requiredDrivenProduct > maximumProduct)
            {
                return true;
            }

            var drivenTeeth = new int[stageCount];
            return EnumerateDrivenTuples(
                state,
                stageCount,
                index: 0,
                remainingProduct: requiredDrivenProduct,
                driverTeeth,
                drivenTeeth);
        }

        var remaining = stageCount - index - 1;
        var minimumSuffix = BigInteger.Pow(new BigInteger(state.Request.MinTeeth), remaining);
        var maximumSuffix = BigInteger.Pow(new BigInteger(state.Request.MaxTeeth), remaining);
        for (var toothValue = (long)state.Request.MinTeeth;
             toothValue <= state.Request.MaxTeeth;
             toothValue++)
        {
            if (state.CancellationToken.IsCancellationRequested)
            {
                state.Cancelled = true;
                return false;
            }

            var tooth = (int)toothValue;
            var nextProduct = currentProduct * tooth;
            if ((nextProduct * maximumSuffix) < lowerDriverProduct ||
                (nextProduct * minimumSuffix) > upperDriverProduct)
            {
                continue;
            }

            driverTeeth[index] = tooth;
            if (!EnumerateDriverTuples(
                    state,
                    stageCount,
                    index + 1,
                    nextProduct,
                    lowerDriverProduct,
                    upperDriverProduct,
                    targetNumerator,
                    targetDenominator,
                    minimumProduct,
                    maximumProduct,
                    driverTeeth))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EnumerateDrivenTuples(
        SearchState state,
        int stageCount,
        int index,
        BigInteger remainingProduct,
        int[] driverTeeth,
        int[] drivenTeeth)
    {
        if (state.CancellationToken.IsCancellationRequested)
        {
            state.Cancelled = true;
            return false;
        }

        if (index == stageCount - 1)
        {
            if (remainingProduct < state.Request.MinTeeth || remainingProduct > state.Request.MaxTeeth)
            {
                return true;
            }

            drivenTeeth[index] = (int)remainingProduct;
            AddMatch(state, driverTeeth, drivenTeeth);
            return true;
        }

        if (!state.Request.AllowCompound)
        {
            var fixedTooth = driverTeeth[index + 1];
            if (remainingProduct % fixedTooth != BigInteger.Zero)
            {
                return true;
            }

            var quotient = remainingProduct / fixedTooth;
            var remainingSlots = stageCount - index - 1;
            if (!CanFactorWithinBounds(quotient, remainingSlots, state.Request.MinTeeth, state.Request.MaxTeeth))
            {
                return true;
            }

            drivenTeeth[index] = fixedTooth;
            return EnumerateDrivenTuples(
                state,
                stageCount,
                index + 1,
                quotient,
                driverTeeth,
                drivenTeeth);
        }

        var remainingSlotsForBounds = stageCount - index - 1;
        var minimumSuffix = BigInteger.Pow(new BigInteger(state.Request.MinTeeth), remainingSlotsForBounds);
        var maximumSuffix = BigInteger.Pow(new BigInteger(state.Request.MaxTeeth), remainingSlotsForBounds);
        var minimumFactor = BigInteger.Max(
            state.Request.MinTeeth,
            CeilingDivide(remainingProduct, maximumSuffix));
        var maximumFactor = BigInteger.Min(
            state.Request.MaxTeeth,
            remainingProduct / minimumSuffix);
        if (minimumFactor > maximumFactor)
        {
            return true;
        }

        for (var toothValue = (long)(int)minimumFactor;
             toothValue <= (int)maximumFactor;
             toothValue++)
        {
            if (state.CancellationToken.IsCancellationRequested)
            {
                state.Cancelled = true;
                return false;
            }

            var tooth = (int)toothValue;
            if (remainingProduct % tooth != BigInteger.Zero)
            {
                continue;
            }

            var quotient = remainingProduct / tooth;
            var remainingSlots = stageCount - index - 1;
            if (!CanFactorWithinBounds(quotient, remainingSlots, state.Request.MinTeeth, state.Request.MaxTeeth))
            {
                continue;
            }

            drivenTeeth[index] = tooth;
            if (!EnumerateDrivenTuples(
                    state,
                    stageCount,
                    index + 1,
                    quotient,
                    driverTeeth,
                    drivenTeeth))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddMatch(SearchState state, int[] driverTeeth, int[] drivenTeeth)
    {
        state.RawMatches++;
        var stages = new List<ExactGearStage>(driverTeeth.Length);
        for (var index = 0; index < driverTeeth.Length; index++)
        {
            stages.Add(new ExactGearStage(index + 1, driverTeeth[index], drivenTeeth[index]));
        }

        if (!KinematicSynthesisCandidateFactory.TryCreate(
                stages,
                state.Request.TargetTransfer,
                state.Fingerprint,
                out var candidate,
                out var diagnostics) || candidate is null)
        {
            state.Diagnostics.AddRange(diagnostics);
            return;
        }

        if (!state.CanonicalSignatures.Add(candidate.CanonicalSignature))
        {
            return;
        }

        state.DeduplicatedCandidateCount++;
        AddToTopCandidates(state.TopCandidates, candidate, state.Request.MaxReturnedCandidates);
    }

    private static void AddToTopCandidates(
        List<KinematicSynthesisCandidate> candidates,
        KinematicSynthesisCandidate candidate,
        int maximumCount)
    {
        if (candidates.Count < maximumCount)
        {
            candidates.Add(candidate);
            candidates.Sort(KinematicSynthesisCandidateComparer.Instance);
            return;
        }

        var worstIndex = candidates.Count - 1;
        if (KinematicSynthesisCandidateComparer.Instance.Compare(candidate, candidates[worstIndex]) >= 0)
        {
            return;
        }

        candidates[worstIndex] = candidate;
        candidates.Sort(KinematicSynthesisCandidateComparer.Instance);
    }

    private static bool CanFactorWithinBounds(BigInteger product, int count, int minTeeth, int maxTeeth)
    {
        var minimum = BigInteger.Pow(new BigInteger(minTeeth), count);
        var maximum = BigInteger.Pow(new BigInteger(maxTeeth), count);
        return product >= minimum && product <= maximum;
    }

    private static BigInteger CeilingDivide(BigInteger numerator, BigInteger denominator)
    {
        return (numerator + denominator - BigInteger.One) / denominator;
    }

    private static KinematicSynthesisResult CreateTerminalResult(
        ExactRatioSynthesisRequest request,
        string canonicalRequest,
        string requestId,
        KinematicSynthesisStatus status,
        IEnumerable<Diagnostic> diagnostics,
        KinematicSynthesisFingerprint fingerprint,
        bool searchComplete)
    {
        return new KinematicSynthesisResult(
            request,
            canonicalRequest,
            requestId,
            status,
            Array.Empty<KinematicSynthesisCandidate>(),
            diagnostics,
            new KinematicSearchSummary(Array.Empty<int>(), 0, 0, 0, 0, false, searchComplete),
            fingerprint);
    }

    private static string Hash(string prefix, string value)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        var builder = new StringBuilder(prefix, prefix.Length + (digest.Length * 2));
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed class SearchState
    {
        public SearchState(
            ExactRatioSynthesisRequest request,
            KinematicSynthesisFingerprint fingerprint,
            CancellationToken cancellationToken)
        {
            Request = request;
            Fingerprint = fingerprint;
            CancellationToken = cancellationToken;
        }

        public ExactRatioSynthesisRequest Request { get; }

        public KinematicSynthesisFingerprint Fingerprint { get; }

        public CancellationToken CancellationToken { get; }

        public List<int> StageOptionsConsidered { get; } = new();

        public List<KinematicSynthesisCandidate> TopCandidates { get; } = new();

        public HashSet<string> CanonicalSignatures { get; } = new(StringComparer.Ordinal);

        public List<Diagnostic> Diagnostics { get; } = new();

        public long SearchExpansions { get; set; }

        public long RawMatches { get; set; }

        public long DeduplicatedCandidateCount { get; set; }

        public bool BudgetExhausted { get; set; }

        public bool Cancelled { get; set; }
    }
}

internal static class KinematicSynthesisCandidateFactory
{
    public static bool TryCreate(
        IReadOnlyList<ExactGearStage> stages,
        Rational targetTransfer,
        KinematicSynthesisFingerprint fingerprint,
        out KinematicSynthesisCandidate? candidate,
        out IReadOnlyCollection<Diagnostic> diagnostics)
    {
        if (stages is null)
        {
            throw new ArgumentNullException(nameof(stages));
        }

        if (fingerprint is null)
        {
            throw new ArgumentNullException(nameof(fingerprint));
        }

        var mutableDiagnostics = new List<Diagnostic>();
        var dofs = new List<RotationalDof>();
        for (var index = 0; index <= stages.Count; index++)
        {
            dofs.Add(new RotationalDof(DofId(index, stages.Count), index == 0));
        }

        var couplings = new List<ExternalGearCoupling>();
        for (var index = 0; index < stages.Count; index++)
        {
            var stage = stages[index];
            couplings.Add(new ExternalGearCoupling(
                "mesh:" + (index + 1).ToString("D2", CultureInfo.InvariantCulture),
                DofId(index, stages.Count),
                DofId(index + 1, stages.Count),
                stage.DriverTeeth,
                stage.DrivenTeeth,
                Rational.Zero));
        }

        var specification = new KinematicSpecification("dof:input", dofs, couplings);
        var solved = KinematicSolver.Solve(specification);
        mutableDiagnostics.AddRange(solved.Diagnostics);
        Rational computedTransfer = Rational.One;
        foreach (var stage in stages)
        {
            computedTransfer *= stage.Transfer;
        }

        if (computedTransfer != targetTransfer)
        {
            mutableDiagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisTargetMismatch,
                DiagnosticSeverity.Error,
                "The ordered stage product '" + computedTransfer + "' does not equal target '" + targetTransfer + "'.",
                "dof:output"));
        }

        DofKinematicState? outputState = null;
        if (solved.Solution is not null)
        {
            solved.Solution.TryGetState("dof:output", out outputState);
        }

        if (outputState is null || outputState.Coefficient != targetTransfer || outputState.PhaseOffset != Rational.Zero)
        {
            mutableDiagnostics.Add(new Diagnostic(
                DiagnosticCodes.SynthesisCandidateValidationFailed,
                DiagnosticSeverity.Error,
                "The authoritative Kinematic solver did not reproduce the requested exact output transfer and zero phase.",
                "dof:output"));
        }

        var validation = new ValidationBundle(mutableDiagnostics);
        if (!validation.IsValid || solved.Solution is null || outputState is null)
        {
            diagnostics = validation.Diagnostics;
            candidate = null;
            return false;
        }

        var roles = new List<IntermediateShaftRole>();
        for (var index = 0; index < stages.Count - 1; index++)
        {
            roles.Add(stages[index].DrivenTeeth == stages[index + 1].DriverTeeth
                ? IntermediateShaftRole.SimpleIdlerCompatible
                : IntermediateShaftRole.Compound);
        }

        var allTeeth = stages
            .SelectMany(stage => new[] { stage.DriverTeeth, stage.DrivenTeeth })
            .ToArray();
        var compoundCount = roles.Count(role => role == IntermediateShaftRole.Compound);
        var idlerCount = roles.Count - compoundCount;
        var metrics = new KinematicSynthesisMetrics(
            stages.Count,
            Math.Max(0, stages.Count - 1),
            compoundCount,
            idlerCount,
            (stages.Count * 2) - idlerCount,
            allTeeth.Max(),
            allTeeth.Min(),
            allTeeth.Sum(value => (long)value));
        var signature = BuildCanonicalSignature(
            stages,
            roles,
            specification,
            solved.Solution,
            targetTransfer,
            fingerprint);
        candidate = new KinematicSynthesisCandidate(
            HashCandidate(signature),
            signature,
            stages,
            specification,
            solved.Solution,
            roles,
            targetTransfer,
            metrics,
            validation);
        diagnostics = validation.Diagnostics;
        return true;
    }

    private static string BuildCanonicalSignature(
        IReadOnlyList<ExactGearStage> stages,
        IReadOnlyList<IntermediateShaftRole> roles,
        KinematicSpecification specification,
        KinematicSolution solution,
        Rational targetTransfer,
        KinematicSynthesisFingerprint fingerprint)
    {
        var builder = new StringBuilder();
        builder.Append(fingerprint.SemanticVersion);
        builder.Append("|profile=").Append(fingerprint.DeterminismProfile);
        builder.Append("|root=").Append(specification.RootDofId);
        builder.Append("|path=");
        for (var index = 0; index <= stages.Count; index++)
        {
            if (index > 0) builder.Append('>');
            builder.Append(DofId(index, stages.Count));
        }

        builder.Append("|couplings=");
        for (var index = 0; index < stages.Count; index++)
        {
            if (index > 0) builder.Append(';');
            var stage = stages[index];
            builder.Append("mesh:").Append((index + 1).ToString("D2", CultureInfo.InvariantCulture));
            builder.Append('(').Append(DofId(index, stages.Count)).Append('>');
            builder.Append(DofId(index + 1, stages.Count)).Append(',');
            builder.Append(stage.DriverTeeth.ToString(CultureInfo.InvariantCulture)).Append('/');
            builder.Append(stage.DrivenTeeth.ToString(CultureInfo.InvariantCulture)).Append(",0)");
        }

        builder.Append("|roles=");
        for (var index = 0; index < roles.Count; index++)
        {
            if (index > 0) builder.Append(';');
            builder.Append(RoleToCanonical(roles[index]));
        }

        builder.Append("|target=").Append(targetTransfer).Append("|phase=0|solution=");
        for (var index = 0; index < solution.States.Count; index++)
        {
            if (index > 0) builder.Append(';');
            var state = solution.States[index];
            builder.Append(state.DofId).Append(':').Append(state.Coefficient).Append(':').Append(state.PhaseOffset);
        }

        return builder.ToString();
    }

    private static string DofId(int index, int stageCount)
    {
        if (index == 0) return "dof:input";
        return index == stageCount
            ? "dof:output"
            : "dof:intermediate:" + index.ToString("D2", CultureInfo.InvariantCulture);
    }

    internal static string RoleToCanonical(IntermediateShaftRole role)
    {
        return role switch
        {
            IntermediateShaftRole.Compound => "compound",
            IntermediateShaftRole.SimpleIdlerCompatible => "simple-idler-compatible",
            _ => throw new InvalidOperationException("Unsupported intermediate role '" + role + "'."),
        };
    }

    private static string HashCandidate(string signature)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(signature));
        }

        var builder = new StringBuilder("kinematic-sha256:");
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
