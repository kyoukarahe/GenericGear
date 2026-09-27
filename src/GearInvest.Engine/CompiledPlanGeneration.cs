using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class CompiledPlanGenerationContract
{
    public const string PolicySemanticVersion = "exact-ratio-plan-generation-policy-v1";
    public const string ResultFormat = "gear-invest.compiled-plan-generation-result";
    public const string ResultFormatVersion = "0.1";
}

public enum CompiledPlanGenerationStatus
{
    Complete,
    IncompleteBudget,
    Infeasible,
    InvalidInput,
    Cancelled,
}

public sealed class ExactRatioPlanGenerationPolicy
{
    public ExactRatioPlanGenerationPolicy(
        int minTeeth,
        int maxTeeth,
        int minStages,
        int maxStages,
        bool allowCompound,
        int maxKinematicCandidates,
        long maxSynthesisExpansions,
        BigInteger rootAxisX,
        BigInteger rootAxisY,
        BigInteger pitchRadiusTicksPerTooth,
        int maxLayers,
        int maxLayoutsPerKinematicCandidate,
        long maxPlacementExpansions,
        BigInteger clearanceTicks,
        int maxReturnedMechanisms,
        string synthesisDeterminismProfile = ExactRatioSynthesisContract.DefaultDeterminismProfile,
        string layoutDeterminismProfile = SpatialLayoutContract.DefaultDeterminismProfile,
        string generationDeterminismProfile = ExactRatioMechanismGenerationContract.DefaultDeterminismProfile)
    {
        MinTeeth = minTeeth;
        MaxTeeth = maxTeeth;
        MinStages = minStages;
        MaxStages = maxStages;
        AllowCompound = allowCompound;
        MaxKinematicCandidates = maxKinematicCandidates;
        MaxSynthesisExpansions = maxSynthesisExpansions;
        RootAxisX = rootAxisX;
        RootAxisY = rootAxisY;
        PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        MaxLayers = maxLayers;
        MaxLayoutsPerKinematicCandidate = maxLayoutsPerKinematicCandidate;
        MaxPlacementExpansions = maxPlacementExpansions;
        ClearanceTicks = clearanceTicks;
        MaxReturnedMechanisms = maxReturnedMechanisms;
        SynthesisDeterminismProfile = synthesisDeterminismProfile ?? string.Empty;
        LayoutDeterminismProfile = layoutDeterminismProfile ?? string.Empty;
        GenerationDeterminismProfile = generationDeterminismProfile ?? string.Empty;
    }

    public int MinTeeth { get; }
    public int MaxTeeth { get; }
    public int MinStages { get; }
    public int MaxStages { get; }
    public bool AllowCompound { get; }
    public int MaxKinematicCandidates { get; }
    public long MaxSynthesisExpansions { get; }
    public BigInteger RootAxisX { get; }
    public BigInteger RootAxisY { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public int MaxLayers { get; }
    public int MaxLayoutsPerKinematicCandidate { get; }
    public long MaxPlacementExpansions { get; }
    public BigInteger ClearanceTicks { get; }
    public int MaxReturnedMechanisms { get; }
    public string SynthesisDeterminismProfile { get; }
    public string LayoutDeterminismProfile { get; }
    public string GenerationDeterminismProfile { get; }

    public static ExactRatioPlanGenerationPolicy ReviewedDefault { get; } = new(
        minTeeth: 10,
        maxTeeth: 120,
        minStages: 1,
        maxStages: 3,
        allowCompound: true,
        maxKinematicCandidates: 1,
        maxSynthesisExpansions: 500_000,
        rootAxisX: BigInteger.Zero,
        rootAxisY: BigInteger.Zero,
        pitchRadiusTicksPerTooth: BigInteger.One,
        maxLayers: 2,
        maxLayoutsPerKinematicCandidate: 1,
        maxPlacementExpansions: 100,
        clearanceTicks: BigInteger.Zero,
        maxReturnedMechanisms: 1);
}

public static class ExactRatioPlanGenerationPolicyIdentity
{
    public static string BuildCanonicalRepresentation(ExactRatioPlanGenerationPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        return CompiledPlanGenerationContract.PolicySemanticVersion +
            "|minTeeth=" + policy.MinTeeth.ToString(CultureInfo.InvariantCulture) +
            "|maxTeeth=" + policy.MaxTeeth.ToString(CultureInfo.InvariantCulture) +
            "|minStages=" + policy.MinStages.ToString(CultureInfo.InvariantCulture) +
            "|maxStages=" + policy.MaxStages.ToString(CultureInfo.InvariantCulture) +
            "|allowCompound=" + policy.AllowCompound.ToString().ToLowerInvariant() +
            "|maxKinematicCandidates=" + policy.MaxKinematicCandidates.ToString(CultureInfo.InvariantCulture) +
            "|maxSynthesisExpansions=" + policy.MaxSynthesisExpansions.ToString(CultureInfo.InvariantCulture) +
            "|rootX=" + policy.RootAxisX.ToString(CultureInfo.InvariantCulture) +
            "|rootY=" + policy.RootAxisY.ToString(CultureInfo.InvariantCulture) +
            "|pitchRadiusTicksPerTooth=" + policy.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture) +
            "|maxLayers=" + policy.MaxLayers.ToString(CultureInfo.InvariantCulture) +
            "|maxLayoutsPerKinematicCandidate=" + policy.MaxLayoutsPerKinematicCandidate.ToString(CultureInfo.InvariantCulture) +
            "|maxPlacementExpansions=" + policy.MaxPlacementExpansions.ToString(CultureInfo.InvariantCulture) +
            "|clearanceTicks=" + policy.ClearanceTicks.ToString(CultureInfo.InvariantCulture) +
            "|maxReturnedMechanisms=" + policy.MaxReturnedMechanisms.ToString(CultureInfo.InvariantCulture) +
            "|synthesisProfile=" + PeriodicSemanticIdentity.LengthPrefixed(policy.SynthesisDeterminismProfile) +
            "|layoutProfile=" + PeriodicSemanticIdentity.LengthPrefixed(policy.LayoutDeterminismProfile) +
            "|generationProfile=" + PeriodicSemanticIdentity.LengthPrefixed(policy.GenerationDeterminismProfile);
    }

    public static string ComputePolicyId(ExactRatioPlanGenerationPolicy policy)
    {
        return PeriodicSemanticIdentity.Hash("generation-policy-sha256:", BuildCanonicalRepresentation(policy));
    }
}

public sealed class CompiledGenerationEdge
{
    public CompiledGenerationEdge(
        int index,
        CompiledContinuousTransferRequirement requirement,
        ExactRatioMechanismGenerationRequest generationRequest,
        string generationRequestId)
    {
        Index = index;
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        GenerationRequest = generationRequest ?? throw new ArgumentNullException(nameof(generationRequest));
        GenerationRequestId = generationRequestId ?? throw new ArgumentNullException(nameof(generationRequestId));
    }

    public int Index { get; }
    public CompiledContinuousTransferRequirement Requirement { get; }
    public ExactRatioMechanismGenerationRequest GenerationRequest { get; }
    public string GenerationRequestId { get; }
}

public sealed class CompiledGenerationPlan
{
    public CompiledGenerationPlan(
        CompiledMechanicalRequirementPlan compiledPlan,
        ExactRatioPlanGenerationPolicy policy,
        string generationPolicyId,
        IEnumerable<CompiledGenerationEdge> orderedEdges)
    {
        CompiledPlan = compiledPlan ?? throw new ArgumentNullException(nameof(compiledPlan));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        GenerationPolicyId = generationPolicyId ?? throw new ArgumentNullException(nameof(generationPolicyId));
        OrderedEdges = (orderedEdges ?? throw new ArgumentNullException(nameof(orderedEdges))).ToList().AsReadOnly();
    }

    public CompiledMechanicalRequirementPlan CompiledPlan { get; }
    public ExactRatioPlanGenerationPolicy Policy { get; }
    public string GenerationPolicyId { get; }
    public ReadOnlyCollection<CompiledGenerationEdge> OrderedEdges { get; }
}

public sealed class CompiledPlanGenerationEdgeResult
{
    public CompiledPlanGenerationEdgeResult(CompiledGenerationEdge edge, ExactRatioMechanismGenerationResult generationResult)
    {
        Edge = edge ?? throw new ArgumentNullException(nameof(edge));
        GenerationResult = generationResult ?? throw new ArgumentNullException(nameof(generationResult));
    }

    public CompiledGenerationEdge Edge { get; }
    public ExactRatioMechanismGenerationResult GenerationResult { get; }
}

public sealed class CompiledPlanGenerationResult
{
    public CompiledPlanGenerationResult(
        string sourceKind,
        string sourceCatalogId,
        string semanticSpecificationId,
        SemanticCompilerFingerprint compilerFingerprint,
        string compiledPlanId,
        string generationPolicyId,
        ExactRatioPlanGenerationPolicy policy,
        CompiledPlanGenerationStatus status,
        IEnumerable<CompiledPlanGenerationEdgeResult> edgeResults,
        IEnumerable<DerivedContinuousTransferRelation> derivedRelations,
        IEnumerable<Diagnostic> diagnostics)
    {
        SourceKind = sourceKind ?? throw new ArgumentNullException(nameof(sourceKind));
        SourceCatalogId = sourceCatalogId ?? throw new ArgumentNullException(nameof(sourceCatalogId));
        SemanticSpecificationId = semanticSpecificationId ?? throw new ArgumentNullException(nameof(semanticSpecificationId));
        CompilerFingerprint = compilerFingerprint ?? throw new ArgumentNullException(nameof(compilerFingerprint));
        CompiledPlanId = compiledPlanId ?? throw new ArgumentNullException(nameof(compiledPlanId));
        GenerationPolicyId = generationPolicyId ?? throw new ArgumentNullException(nameof(generationPolicyId));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Status = status;
        EdgeResults = (edgeResults ?? throw new ArgumentNullException(nameof(edgeResults))).ToList().AsReadOnly();
        DerivedRelations = (derivedRelations ?? throw new ArgumentNullException(nameof(derivedRelations))).ToList().AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }

    public string SourceKind { get; }
    public string SourceCatalogId { get; }
    public string SemanticSpecificationId { get; }
    public SemanticCompilerFingerprint CompilerFingerprint { get; }
    public string CompiledPlanId { get; }
    public string GenerationPolicyId { get; }
    public ExactRatioPlanGenerationPolicy Policy { get; }
    public CompiledPlanGenerationStatus Status { get; }
    public ReadOnlyCollection<CompiledPlanGenerationEdgeResult> EdgeResults { get; }
    public ReadOnlyCollection<DerivedContinuousTransferRelation> DerivedRelations { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsSuccess => Status == CompiledPlanGenerationStatus.Complete && EdgeResults.Count > 0 &&
        EdgeResults.All(edge => edge.GenerationResult.IsSuccess) &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public static class CompiledPlanLowering
{
    public static CompiledGenerationPlan Lower(
        CompiledMechanicalRequirementPlan plan,
        ExactRatioPlanGenerationPolicy policy)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        var edges = plan.OrderedRequirements.Select((requirement, index) =>
        {
            var request = CreateRequest(requirement.SignedTargetTransfer, policy);
            return new CompiledGenerationEdge(
                index,
                requirement,
                request,
                ExactRatioMechanismGenerationRequestIdentity.ComputeRequestId(request));
        });
        return new CompiledGenerationPlan(
            plan,
            policy,
            ExactRatioPlanGenerationPolicyIdentity.ComputePolicyId(policy),
            edges);
    }

    public static ExactRatioMechanismGenerationRequest CreateRequest(
        Rational targetTransfer,
        ExactRatioPlanGenerationPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        return new ExactRatioMechanismGenerationRequest(
            new ExactRatioSynthesisRequest(
                targetTransfer,
                policy.MinTeeth,
                policy.MaxTeeth,
                policy.MinStages,
                policy.MaxStages,
                policy.AllowCompound,
                policy.MaxKinematicCandidates,
                policy.MaxSynthesisExpansions,
                policy.SynthesisDeterminismProfile),
            new SpatialLayoutRequest(
                policy.RootAxisX,
                policy.RootAxisY,
                policy.PitchRadiusTicksPerTooth,
                policy.MaxLayers,
                policy.MaxLayoutsPerKinematicCandidate,
                policy.MaxPlacementExpansions,
                policy.ClearanceTicks,
                policy.LayoutDeterminismProfile),
            policy.MaxKinematicCandidates,
            policy.MaxLayoutsPerKinematicCandidate,
            policy.MaxReturnedMechanisms,
            policy.GenerationDeterminismProfile);
    }
}
