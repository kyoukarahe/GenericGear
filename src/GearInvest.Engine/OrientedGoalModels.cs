using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class OrientedGoalProfile
{
    public const string Id = "bounded-oriented-two-output-goal-v1";
    public const string RefinedId = "bounded-oriented-two-output-pitch-refined-goal-v1";
    public static bool IsSupported(string profile) => profile == Id || profile == RefinedId;
    public static string MechanicalProfile(string profile) => profile == RefinedId ? OrientedTwoOutputProfile.RefinedId :
        profile == Id ? OrientedTwoOutputProfile.Id : throw new ArgumentException("Unsupported oriented goal profile.");
    public const string Lowering = "world-cardinal-derived-cone-v1";
    public const string Traversal = "exact-cartesian-tuples-v1";
    public const string Ranking = "body-envelope-preference-teeth-id-v1";
    public const string Resources = "oriented-goal-bounded-memory-v1";
    public const int MaxSourcesPerBranch = 8, MaxSourceBytes = 512 * 1024, MaxTotalSourceBytes = 4 * 1024 * 1024;
    public const int MaxPlacements = 32, MaxToothChoices = 16, MaxScaleChoices = 8, MaxDomain = 1000000;
    public const int MaxUniqueCandidates = 64, MaxOrigins = 512, MaxDetails = 256;
    public const int MaxRetainedBytes = 8 * 1024 * 1024, MaxDocumentBytes = 24 * 1024 * 1024;
    // Leaves headroom for exact derived cone geometry in the unchanged 19A 128-digit wire profile.
    public const int MaxInputDigits = 24;
}

public enum OrientedTurnedPolicy { NoneOnly, RequiredSource, OptionalSource }
public enum OrientedGoalStatus { Complete, Infeasible, NoValidatedCandidate, IncompleteBudget, IncompleteResource, InvalidInput, Unsupported, Cancelled, Failed }
public enum OrientedTupleVerdict { Accepted, Rejected, Inconclusive, Unretained }
public enum OrientedRankingGuarantee { None, BestAmongExplored, CompleteDeclaredDomain }

/// <summary>Both frames are declared world coordinates. Neither is snapped or replaced by a body center.</summary>
public sealed class OrientedGoalPlacement
{
    public OrientedGoalPlacement(OrientedFrame pose, OrientedFrame inputMatingFrame, Rational preferencePenalty = default)
    { Pose = pose; InputMatingFrame = inputMatingFrame; PreferencePenalty = preferencePenalty; }
    public OrientedFrame Pose { get; }
    public OrientedFrame InputMatingFrame { get; }
    public Rational PreferencePenalty { get; }
}

/// <summary>Portable original bytes, declared original identities and explicit semantic ports. No fixture/URL lookup.</summary>
public sealed class OrientedGoalSource
{
    private readonly byte[] _bytes;
    public OrientedGoalSource(byte[] bytes, string candidateId, string artifactHash, string inputDofId, string outputDofId,
        IEnumerable<OrientedGoalPlacement> placements)
    {
        if (bytes is null || bytes.Length > OrientedGoalProfile.MaxSourceBytes) throw new ArgumentException("Bounded source bytes required.");
        _bytes = (byte[])bytes.Clone(); CandidateId = candidateId; ArtifactHash = artifactHash; InputDofId = inputDofId; OutputDofId = outputDofId;
        Placements = OrientedGoalLists.Take(placements, OrientedGoalProfile.MaxPlacements, "source placements");
    }
    public byte[] SourceBytes => (byte[])_bytes.Clone();
    public int ByteLength => _bytes.Length;
    public string CandidateId { get; }
    public string ArtifactHash { get; }
    public string InputDofId { get; }
    public string OutputDofId { get; }
    public ReadOnlyCollection<OrientedGoalPlacement> Placements { get; }
}

public sealed class OrientedGoalOutput
{
    public OrientedGoalOutput(string key, OrientedOutputRole role, OrientedFrame terminalFrame, Rational requiredTransfer,
        ExactVector3? fixedBodyCenter = null, int? fixedBodyTeeth = null)
    { Key = key; Role = role; TerminalFrame = terminalFrame; RequiredTransfer = requiredTransfer; FixedBodyCenter = fixedBodyCenter; FixedBodyTeeth = fixedBodyTeeth; }
    public string Key { get; }
    public OrientedOutputRole Role { get; }
    public OrientedFrame TerminalFrame { get; }
    public Rational RequiredTransfer { get; }
    public ExactVector3? FixedBodyCenter { get; }
    public int? FixedBodyTeeth { get; }
}

public sealed class OrientedGoalLimits
{
    public OrientedGoalLimits(int maxBodies = OrientedTwoOutputProfile.MaxBodies, int maxShafts = OrientedTwoOutputProfile.MaxShafts,
        int maxTotalTeeth = OrientedTwoOutputProfile.MaxBodies * OrientedTransmissionProfile.MaxTeeth, ExactEnvelope3? wholeBounds = null)
    { MaxBodies = maxBodies; MaxShafts = maxShafts; MaxTotalTeeth = maxTotalTeeth; WholeBounds = wholeBounds; }
    public int MaxBodies { get; }
    public int MaxShafts { get; }
    public int MaxTotalTeeth { get; }
    public ExactEnvelope3? WholeBounds { get; }
}

public sealed class OrientedGoalSearchOptions
{
    public OrientedGoalSearchOptions(int workBudget = 10000, int returnedCandidateCap = 16,
        int maxUniqueCandidates = OrientedGoalProfile.MaxUniqueCandidates, int maxRetainedBytes = OrientedGoalProfile.MaxRetainedBytes,
        int maxOrigins = OrientedGoalProfile.MaxOrigins, int maxDetails = OrientedGoalProfile.MaxDetails,
        string resourceProfile = OrientedGoalProfile.Resources)
    { WorkBudget = workBudget; ReturnedCandidateCap = returnedCandidateCap; MaxUniqueCandidates = maxUniqueCandidates;
      MaxRetainedBytes = maxRetainedBytes; MaxOrigins = maxOrigins; MaxDetails = maxDetails; ResourceProfile = resourceProfile; }
    public int WorkBudget { get; }
    public int ReturnedCandidateCap { get; }
    public int MaxUniqueCandidates { get; }
    public int MaxRetainedBytes { get; }
    public int MaxOrigins { get; }
    public int MaxDetails { get; }
    public string ResourceProfile { get; }
}

public sealed class OrientedTwoOutputGoal
{
    public OrientedTwoOutputGoal(OrientedFrame rootShaftFrame, OrientedFrame bevelOutputShaftFrame, ExactVector3 apex,
        ExactVector3 inputConeDirection, ExactVector3 outputConeDirection, Rational innerParameter,
        IEnumerable<OrientedGoalOutput> outputs, IEnumerable<int> inputTeeth, IEnumerable<int> outputTeeth, IEnumerable<Rational> pitchScales,
        IEnumerable<OrientedGoalSource> parallelSources, OrientedTurnedPolicy turnedPolicy = OrientedTurnedPolicy.NoneOnly,
        IEnumerable<OrientedGoalSource>? turnedSources = null, bool requireCrossComponentClearance = true,
        IEnumerable<OrientedKeepOut>? keepOuts = null, OrientedGoalLimits? limits = null, OrientedGoalSearchOptions? options = null,
        string profile = OrientedGoalProfile.Id, ExactVector3? unattachedTurnedMatingStation = null)
    {
        RootShaftFrame = rootShaftFrame; BevelOutputShaftFrame = bevelOutputShaftFrame; Apex = apex;
        UnattachedTurnedMatingStation = unattachedTurnedMatingStation ?? bevelOutputShaftFrame.Origin;
        InputConeDirection = inputConeDirection; OutputConeDirection = outputConeDirection; InnerParameter = innerParameter;
        Outputs = OrientedGoalLists.Take(outputs, 2, "outputs"); InputTeeth = OrientedGoalLists.Take(inputTeeth, OrientedGoalProfile.MaxToothChoices, "input teeth");
        OutputTeeth = OrientedGoalLists.Take(outputTeeth, OrientedGoalProfile.MaxToothChoices, "output teeth");
        PitchScales = OrientedGoalLists.Take(pitchScales, OrientedGoalProfile.MaxScaleChoices, "pitch scales");
        ParallelSources = OrientedGoalLists.Take(parallelSources, OrientedGoalProfile.MaxSourcesPerBranch, "parallel sources"); TurnedPolicy = turnedPolicy;
        // Inactive UI/domain data is deliberately not enumerated, parsed or validated.
        TurnedSources = OrientedGoalLists.Take(turnedPolicy == OrientedTurnedPolicy.NoneOnly ? Array.Empty<OrientedGoalSource>() : turnedSources ?? Array.Empty<OrientedGoalSource>(), OrientedGoalProfile.MaxSourcesPerBranch, "turned sources");
        RequireCrossComponentClearance = requireCrossComponentClearance; KeepOuts = OrientedGoalLists.Take(keepOuts ?? Array.Empty<OrientedKeepOut>(), 16, "keep-outs");
        Limits = limits ?? new OrientedGoalLimits(); Options = options ?? new OrientedGoalSearchOptions(); Profile = profile;
    }
    public string Profile { get; }
    public OrientedFrame RootShaftFrame { get; }
    public OrientedFrame BevelOutputShaftFrame { get; }
    /// <summary>Fixed world mating station for the None choice; never inferred from inactive B source text.</summary>
    public ExactVector3 UnattachedTurnedMatingStation { get; }
    public ExactVector3 Apex { get; }
    public ExactVector3 InputConeDirection { get; }
    public ExactVector3 OutputConeDirection { get; }
    public Rational InnerParameter { get; }
    public ReadOnlyCollection<OrientedGoalOutput> Outputs { get; }
    public ReadOnlyCollection<int> InputTeeth { get; }
    public ReadOnlyCollection<int> OutputTeeth { get; }
    public ReadOnlyCollection<Rational> PitchScales { get; }
    public ReadOnlyCollection<OrientedGoalSource> ParallelSources { get; }
    public OrientedTurnedPolicy TurnedPolicy { get; }
    public ReadOnlyCollection<OrientedGoalSource> TurnedSources { get; }
    public bool RequireCrossComponentClearance { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
    public OrientedGoalLimits Limits { get; }
    public OrientedGoalSearchOptions Options { get; }
    /// <summary>Explicit new evaluation policy; domain/options/source bytes are unchanged. Does not relabel a cached generation.</summary>
    public OrientedTwoOutputGoal WithClearancePolicy(string policy) => new(RootShaftFrame, BevelOutputShaftFrame, Apex, InputConeDirection, OutputConeDirection,
        InnerParameter, Outputs, InputTeeth, OutputTeeth, PitchScales, ParallelSources, TurnedPolicy, TurnedSources, RequireCrossComponentClearance,
        KeepOuts, Limits, Options, policy == PitchClearancePolicy.Refined ? OrientedGoalProfile.RefinedId : policy == PitchClearancePolicy.Legacy ? OrientedGoalProfile.Id :
            throw new ArgumentException("Unsupported pitch clearance policy."), UnattachedTurnedMatingStation);
}

public sealed class OrientedGoalSourceRead
{
    public OrientedGoalSourceRead(OrientedGoalStatus status, GenerationCandidate? candidate, string detail)
    { Status = status; Candidate = candidate; Detail = detail; }
    public OrientedGoalStatus Status { get; }
    public GenerationCandidate? Candidate { get; }
    public string Detail { get; }
}

public sealed class OrientedGoalResolvedSource
{
    internal OrientedGoalResolvedSource(string id, OrientedGoalSource definition, GenerationCandidate candidate)
    { Id = id; Definition = definition; Candidate = candidate; }
    public string Id { get; }
    public OrientedGoalSource Definition { get; }
    public GenerationCandidate Candidate { get; }
}

public sealed class OrientedGoalPlan
{
    internal OrientedGoalPlan(OrientedTwoOutputGoal goal, string goalId, BigInteger tupleCount,
        IEnumerable<OrientedGoalResolvedSource> parallel, IEnumerable<OrientedGoalResolvedSource> turned)
    { Goal = goal; GoalId = goalId; TupleCount = tupleCount; ParallelSources = parallel.ToList().AsReadOnly(); TurnedSources = turned.ToList().AsReadOnly(); }
    public OrientedTwoOutputGoal Goal { get; }
    /// <summary>Fixed goals and normalized domains; work budget, return cap and resource retention settings are not mechanical identity.</summary>
    public string GoalId { get; }
    public BigInteger TupleCount { get; }
    public ReadOnlyCollection<OrientedGoalResolvedSource> ParallelSources { get; }
    public ReadOnlyCollection<OrientedGoalResolvedSource> TurnedSources { get; }
}

public sealed class OrientedGoalCompilation
{
    public OrientedGoalCompilation(OrientedGoalStatus status, OrientedGoalPlan? plan, string detail)
    { Status = status; Plan = plan; Detail = detail; }
    public OrientedGoalStatus Status { get; }
    public OrientedGoalPlan? Plan { get; }
    public string Detail { get; }
    public bool IsSuccess => Status == OrientedGoalStatus.Complete && Plan is not null;
}

public sealed class OrientedGoalTuple
{
    public OrientedGoalTuple(int ordinal, int inputTeeth, int outputTeeth, Rational scale, string parallelSourceId, int parallelPlacement,
        string? turnedSourceId, int turnedPlacement)
    { Ordinal = ordinal; InputTeeth = inputTeeth; OutputTeeth = outputTeeth; Scale = scale; ParallelSourceId = parallelSourceId;
      ParallelPlacement = parallelPlacement; TurnedSourceId = turnedSourceId; TurnedPlacement = turnedPlacement; }
    public int Ordinal { get; }
    public int InputTeeth { get; }
    public int OutputTeeth { get; }
    public Rational Scale { get; }
    public string ParallelSourceId { get; }
    public int ParallelPlacement { get; }
    public string? TurnedSourceId { get; }
    public int TurnedPlacement { get; }
}

/// <summary>Serialization callback result. Mechanical payload excludes request/provenance; original artifact bytes are never remapped.</summary>
public sealed class OrientedGoalArtifact
{
    private readonly byte[] _bytes, _mechanicalPayload;
    public OrientedGoalArtifact(string candidateId, string artifactHash, byte[] bytes, byte[] mechanicalPayload)
    { CandidateId = candidateId; ArtifactHash = artifactHash; _bytes = (byte[])bytes.Clone(); _mechanicalPayload = (byte[])mechanicalPayload.Clone(); }
    public string CandidateId { get; }
    public string ArtifactHash { get; }
    public byte[] Bytes => (byte[])_bytes.Clone();
    public byte[] MechanicalPayload => (byte[])_mechanicalPayload.Clone();
    public int ByteLength => _bytes.Length + _mechanicalPayload.Length;
}

public sealed class OrientedGoalMetrics
{
    public OrientedGoalMetrics(int bodies, ExactEnvelope3 envelope, Rational preferencePenalty, int totalTeeth)
    { Bodies = bodies; Envelope = envelope; PreferencePenalty = preferencePenalty; TotalTeeth = totalTeeth; }
    public int Bodies { get; }
    public ExactEnvelope3 Envelope { get; }
    public Rational Volume => (Envelope.Max.X - Envelope.Min.X) * (Envelope.Max.Y - Envelope.Min.Y) * (Envelope.Max.Z - Envelope.Min.Z);
    public Rational MaximumExtent => new[] { Envelope.Max.X - Envelope.Min.X, Envelope.Max.Y - Envelope.Min.Y, Envelope.Max.Z - Envelope.Min.Z }.Max();
    public Rational PreferencePenalty { get; }
    public int TotalTeeth { get; }
}

public sealed class OrientedGoalOrigin
{
    public OrientedGoalOrigin(string id, OrientedGoalTuple tuple, string artifactHash, Rational preferencePenalty)
    { Id = id; Tuple = tuple; ArtifactHash = artifactHash; PreferencePenalty = preferencePenalty; }
    public string Id { get; }
    public OrientedGoalTuple Tuple { get; }
    public string ArtifactHash { get; }
    public Rational PreferencePenalty { get; }
}

public sealed class OrientedGoalCandidate
{
    public OrientedGoalCandidate(OrientedGoalArtifact artifact, OrientedGoalMetrics metrics, IEnumerable<OrientedGoalOrigin> origins, string representativeOriginId)
    { Artifact = artifact; Metrics = metrics; Origins = origins.OrderBy(o => o.Id, StringComparer.Ordinal).ToList().AsReadOnly(); RepresentativeOriginId = representativeOriginId; }
    public OrientedGoalArtifact Artifact { get; }
    public string CandidateId => Artifact.CandidateId;
    public OrientedGoalMetrics Metrics { get; }
    public ReadOnlyCollection<OrientedGoalOrigin> Origins { get; }
    public string RepresentativeOriginId { get; }
}

public sealed class OrientedGoalTupleDetail
{
    public OrientedGoalTupleDetail(OrientedGoalTuple tuple, OrientedTupleVerdict verdict, string reason, string? candidateId = null,
        int pairPredicateCount = 0, string? clearanceDigest = null)
    { Tuple = tuple; Verdict = verdict; Reason = reason; CandidateId = candidateId; PairPredicateCount = pairPredicateCount; ClearanceDigest = clearanceDigest; }
    public OrientedGoalTuple Tuple { get; }
    public OrientedTupleVerdict Verdict { get; }
    public string Reason { get; }
    public string? CandidateId { get; }
    public int PairPredicateCount { get; }
    public string? ClearanceDigest { get; }
}

/// <summary>Fresh tuple inspection, including unsuccessful validation. Not a successful mechanism on failure.</summary>
public sealed class OrientedGoalTupleInspection
{
    internal OrientedGoalTupleInspection(OrientedTwoOutputCompositionRequest request, OrientedTwoOutputResult? composition, OrientedValidation validation, string? filterReason)
    { Request = request; Composition = composition; Validation = validation; FilterReason = filterReason; }
    public OrientedTwoOutputCompositionRequest Request { get; }
    public OrientedTwoOutputResult? Composition { get; }
    public OrientedValidation Validation { get; }
    public string? FilterReason { get; }
}

public sealed class OrientedGoalGeneration
{
    public OrientedGoalGeneration(string goalId, OrientedGoalStatus status, BigInteger theoreticalTuples, int processedTuples,
        int acceptedTuples, int rejectedTuples, int inconclusiveTuples, int unretainedTuples, int uniqueAcceptedCount, int retainedUniqueCount, int retainedOriginCount,
        bool enumerationComplete, bool resultTruncated, OrientedRankingGuarantee rankingGuarantee, string limitingReason,
        string transcriptDigest, IEnumerable<OrientedGoalCandidate> candidates, IEnumerable<OrientedGoalTupleDetail> details,
        string profile = OrientedGoalProfile.Id, int pairPredicateCount = 0)
    {
        GoalId = goalId; Status = status; TheoreticalTuples = theoreticalTuples; ProcessedTuples = processedTuples;
        AcceptedTuples = acceptedTuples; RejectedTuples = rejectedTuples; InconclusiveTuples = inconclusiveTuples; UnretainedTuples = unretainedTuples;
        UniqueAcceptedCount = uniqueAcceptedCount; RetainedUniqueCount = retainedUniqueCount; RetainedOriginCount = retainedOriginCount; EnumerationComplete = enumerationComplete; ResultTruncated = resultTruncated;
        RankingGuarantee = rankingGuarantee; LimitingReason = limitingReason; TranscriptDigest = transcriptDigest;
        Candidates = candidates.ToList().AsReadOnly(); Details = details.ToList().AsReadOnly();
        Profile = profile; PairPredicateCount = pairPredicateCount;
    }
    public string GoalId { get; }
    public string Profile { get; }
    /// <summary>Sum of unique classified non-intended pairs in primary tuple evaluations, excluding packaging/revalidation work.</summary>
    public int PairPredicateCount { get; }
    public OrientedGoalStatus Status { get; }
    public BigInteger TheoreticalTuples { get; }
    public int ProcessedTuples { get; }
    public BigInteger RemainingTuples => TheoreticalTuples - ProcessedTuples;
    public int AcceptedTuples { get; }
    public int RejectedTuples { get; }
    public int InconclusiveTuples { get; }
    public int UnretainedTuples { get; }
    public int UniqueAcceptedCount { get; }
    public int RetainedUniqueCount { get; }
    public int RetainedOriginCount { get; }
    public bool HasValidatedCandidates => Candidates.Count != 0;
    public bool EnumerationComplete { get; }
    public bool ResultTruncated { get; }
    public OrientedRankingGuarantee RankingGuarantee { get; }
    public string LimitingReason { get; }
    public string TranscriptDigest { get; }
    public ReadOnlyCollection<OrientedGoalCandidate> Candidates { get; }
    public ReadOnlyCollection<OrientedGoalTupleDetail> Details { get; }
}

internal static class OrientedGoalLists
{
    internal static ReadOnlyCollection<T> Take<T>(IEnumerable<T> values, int max, string name)
    {
        if (values is null) throw new ArgumentNullException(name);
        var list = values.Take(max + 1).ToList();
        if (list.Count > max) throw new ArgumentException("Raw ingestion limit exceeded: " + name);
        if (list.Any(v => v is null)) throw new ArgumentException("Null entry: " + name);
        return list.AsReadOnly();
    }
}
