using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GearInvest.Core;

public static class PeriodicSemanticContract
{
    public const string BaseTimeUnitSeconds = "second";
    public const string GenericCompilerId = "generic-periodic-semantic-compiler";
    public const string GenericCompilerVersion = "1";
    public const string DefaultDeterminismProfile = "portable-managed-semantic-v1";
    public const string CompilationResultFormat = "gear-invest.semantic-compilation-result";
    public const string CompilationResultFormatVersion = "0.1";
    public const int MaximumNodes = 256;
    public const int MaximumRequirements = 1024;
    public const int MaximumRationalDigits = 128;
}

public enum RotationDirection
{
    Same,
    Opposite,
}

public enum SemanticCompilationStatus
{
    Complete,
    InvalidInput,
    Unsupported,
    Cancelled,
}

public sealed class SemanticCompilerFingerprint
{
    public SemanticCompilerFingerprint(string compilerId, string compilerVersion, string determinismProfile)
    {
        CompilerId = compilerId ?? throw new ArgumentNullException(nameof(compilerId));
        CompilerVersion = compilerVersion ?? throw new ArgumentNullException(nameof(compilerVersion));
        DeterminismProfile = determinismProfile ?? throw new ArgumentNullException(nameof(determinismProfile));
    }

    public string CompilerId { get; }
    public string CompilerVersion { get; }
    public string DeterminismProfile { get; }
    public string SemanticVersion => CompilerId + "-v" + CompilerVersion;
}

public sealed class PeriodicRotationNode
{
    public PeriodicRotationNode(string id, Rational exactPeriodInBaseTimeUnit, Rational exactInitialPhaseTurns)
    {
        Id = id ?? string.Empty;
        ExactPeriodInBaseTimeUnit = exactPeriodInBaseTimeUnit;
        ExactInitialPhaseTurns = exactInitialPhaseTurns;
    }

    public string Id { get; }
    public Rational ExactPeriodInBaseTimeUnit { get; }
    public Rational ExactInitialPhaseTurns { get; }
}

public abstract class SemanticMechanicalRequirement
{
    protected SemanticMechanicalRequirement(string id, string fromNodeId, string toNodeId)
    {
        Id = id ?? string.Empty;
        FromNodeId = fromNodeId ?? string.Empty;
        ToNodeId = toNodeId ?? string.Empty;
    }

    public string Id { get; }
    public string FromNodeId { get; }
    public string ToNodeId { get; }
    public abstract string Domain { get; }
}

public sealed class ContinuousRotationRequirement : SemanticMechanicalRequirement
{
    public const string DomainId = "continuous-periodic-rotation";

    public ContinuousRotationRequirement(
        string id,
        string fromNodeId,
        string toNodeId,
        RotationDirection requiredDirection,
        Rational exactPhaseRelation,
        Rational? optionalExplicitTransfer = null)
        : base(id, fromNodeId, toNodeId)
    {
        RequiredDirection = requiredDirection;
        ExactPhaseRelation = exactPhaseRelation;
        OptionalExplicitTransfer = optionalExplicitTransfer;
    }

    public override string Domain => DomainId;
    public RotationDirection RequiredDirection { get; }
    public Rational ExactPhaseRelation { get; }
    public Rational? OptionalExplicitTransfer { get; }
}

public sealed class UnsupportedSemanticRequirement : SemanticMechanicalRequirement
{
    public UnsupportedSemanticRequirement(string id, string fromNodeId, string toNodeId, string domain)
        : base(id, fromNodeId, toNodeId)
    {
        DomainValue = domain ?? string.Empty;
    }

    public string DomainValue { get; }
    public override string Domain => DomainValue;
}

public sealed class SemanticPeriodicSpecification
{
    public SemanticPeriodicSpecification(
        string sourceKind,
        string baseTimeUnit,
        IEnumerable<PeriodicRotationNode> nodes,
        IEnumerable<SemanticMechanicalRequirement> requirements)
    {
        SourceKind = sourceKind ?? string.Empty;
        BaseTimeUnit = baseTimeUnit ?? string.Empty;
        Nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToList().AsReadOnly();
        Requirements = (requirements ?? throw new ArgumentNullException(nameof(requirements))).ToList().AsReadOnly();
    }

    public string SourceKind { get; }
    public string BaseTimeUnit { get; }
    public ReadOnlyCollection<PeriodicRotationNode> Nodes { get; }
    public ReadOnlyCollection<SemanticMechanicalRequirement> Requirements { get; }
}

public sealed class CompiledContinuousTransferRequirement
{
    public CompiledContinuousTransferRequirement(
        string compiledRequirementId,
        string sourceRequirementId,
        string fromSemanticNodeId,
        string toSemanticNodeId,
        Rational signedTargetTransfer,
        Rational exactPhaseRelation)
    {
        CompiledRequirementId = compiledRequirementId ?? throw new ArgumentNullException(nameof(compiledRequirementId));
        SourceRequirementId = sourceRequirementId ?? throw new ArgumentNullException(nameof(sourceRequirementId));
        FromSemanticNodeId = fromSemanticNodeId ?? throw new ArgumentNullException(nameof(fromSemanticNodeId));
        ToSemanticNodeId = toSemanticNodeId ?? throw new ArgumentNullException(nameof(toSemanticNodeId));
        SignedTargetTransfer = signedTargetTransfer;
        ExactPhaseRelation = exactPhaseRelation;
    }

    public string CompiledRequirementId { get; }
    public string SourceRequirementId { get; }
    public string FromSemanticNodeId { get; }
    public string ToSemanticNodeId { get; }
    public Rational SignedTargetTransfer { get; }
    public Rational ExactPhaseRelation { get; }
}

public sealed class DerivedContinuousTransferRelation
{
    public DerivedContinuousTransferRelation(
        string relationId,
        string fromSemanticNodeId,
        string toSemanticNodeId,
        Rational signedTransfer,
        Rational exactPhaseRelation)
    {
        RelationId = relationId ?? throw new ArgumentNullException(nameof(relationId));
        FromSemanticNodeId = fromSemanticNodeId ?? throw new ArgumentNullException(nameof(fromSemanticNodeId));
        ToSemanticNodeId = toSemanticNodeId ?? throw new ArgumentNullException(nameof(toSemanticNodeId));
        SignedTransfer = signedTransfer;
        ExactPhaseRelation = exactPhaseRelation;
    }

    public string RelationId { get; }
    public string FromSemanticNodeId { get; }
    public string ToSemanticNodeId { get; }
    public Rational SignedTransfer { get; }
    public Rational ExactPhaseRelation { get; }
}

public sealed class CompiledMechanicalRequirementPlan
{
    public CompiledMechanicalRequirementPlan(
        string compiledPlanId,
        string baseTimeUnit,
        IEnumerable<PeriodicRotationNode> nodes,
        IEnumerable<CompiledContinuousTransferRequirement> orderedRequirements,
        IEnumerable<DerivedContinuousTransferRelation> derivedRelations)
    {
        CompiledPlanId = compiledPlanId ?? throw new ArgumentNullException(nameof(compiledPlanId));
        BaseTimeUnit = baseTimeUnit ?? throw new ArgumentNullException(nameof(baseTimeUnit));
        Nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToList().AsReadOnly();
        OrderedRequirements = (orderedRequirements ?? throw new ArgumentNullException(nameof(orderedRequirements))).ToList().AsReadOnly();
        DerivedRelations = (derivedRelations ?? throw new ArgumentNullException(nameof(derivedRelations))).ToList().AsReadOnly();
    }

    public string CompiledPlanId { get; }
    public string BaseTimeUnit { get; }
    public ReadOnlyCollection<PeriodicRotationNode> Nodes { get; }
    public ReadOnlyCollection<CompiledContinuousTransferRequirement> OrderedRequirements { get; }
    public ReadOnlyCollection<DerivedContinuousTransferRelation> DerivedRelations { get; }
}

public sealed class SemanticCompilationResult
{
    public SemanticCompilationResult(
        SemanticCompilationStatus status,
        string sourceKind,
        string sourceCatalogId,
        string semanticSpecificationId,
        SemanticCompilerFingerprint compilerFingerprint,
        CompiledMechanicalRequirementPlan? compiledPlan,
        IEnumerable<Diagnostic> diagnostics)
    {
        Status = status;
        SourceKind = sourceKind ?? throw new ArgumentNullException(nameof(sourceKind));
        SourceCatalogId = sourceCatalogId ?? throw new ArgumentNullException(nameof(sourceCatalogId));
        SemanticSpecificationId = semanticSpecificationId ?? throw new ArgumentNullException(nameof(semanticSpecificationId));
        CompilerFingerprint = compilerFingerprint ?? throw new ArgumentNullException(nameof(compilerFingerprint));
        CompiledPlan = compiledPlan;
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }

    public SemanticCompilationStatus Status { get; }
    public string SourceKind { get; }
    public string SourceCatalogId { get; }
    public string SemanticSpecificationId { get; }
    public SemanticCompilerFingerprint CompilerFingerprint { get; }
    public CompiledMechanicalRequirementPlan? CompiledPlan { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsSuccess => Status == SemanticCompilationStatus.Complete && CompiledPlan is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public static class PeriodicSemanticIdentity
{
    public static string ComputeGenericSemanticSpecificationId(SemanticPeriodicSpecification specification)
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        return Hash("semantic-specification-sha256:", BuildGenericSemanticCanonicalRepresentation(specification));
    }

    public static string BuildGenericSemanticCanonicalRepresentation(SemanticPeriodicSpecification specification)
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        var builder = new StringBuilder("generic-periodic-semantic-v1");
        builder.Append("|sourceKind=").Append(LengthPrefixed(specification.SourceKind));
        builder.Append("|baseTimeUnit=").Append(LengthPrefixed(specification.BaseTimeUnit));
        foreach (var node in specification.Nodes.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|node=").Append(LengthPrefixed(
                node.Id + "|period=" + node.ExactPeriodInBaseTimeUnit + "|phase=" + node.ExactInitialPhaseTurns));
        }

        foreach (var requirement in specification.Requirements.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var payload = requirement.Id + "|domain=" + requirement.Domain + "|from=" +
                requirement.FromNodeId + "|to=" + requirement.ToNodeId;
            if (requirement is ContinuousRotationRequirement continuous)
            {
                payload += "|direction=" + continuous.RequiredDirection +
                    "|explicit=" + (continuous.OptionalExplicitTransfer?.ToString() ?? "none") +
                    "|phase=" + continuous.ExactPhaseRelation;
            }

            builder.Append("|requirement=").Append(LengthPrefixed(payload));
        }

        return builder.ToString();
    }

    public static string ComputeCompiledRequirementId(
        string fromNodeId,
        string toNodeId,
        Rational targetTransfer,
        Rational phaseRelation)
    {
        return Hash(
            "compiled-requirement-sha256:",
            "compiled-continuous-transfer-v1|from=" + LengthPrefixed(fromNodeId) +
            "|to=" + LengthPrefixed(toNodeId) +
            "|transfer=" + targetTransfer + "|phase=" + phaseRelation);
    }

    public static string ComputeDerivedRelationId(
        string fromNodeId,
        string toNodeId,
        Rational targetTransfer,
        Rational phaseRelation)
    {
        return Hash(
            "derived-relation-sha256:",
            "derived-continuous-transfer-v1|from=" + LengthPrefixed(fromNodeId) +
            "|to=" + LengthPrefixed(toNodeId) +
            "|transfer=" + targetTransfer + "|phase=" + phaseRelation);
    }

    public static string ComputeCompiledPlanId(
        string baseTimeUnit,
        IEnumerable<PeriodicRotationNode> nodes,
        IEnumerable<CompiledContinuousTransferRequirement> requirements,
        IEnumerable<DerivedContinuousTransferRelation> derivedRelations)
    {
        var builder = new StringBuilder("compiled-periodic-plan-v1|baseTimeUnit=");
        builder.Append(LengthPrefixed(baseTimeUnit));
        foreach (var node in nodes.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|node=").Append(LengthPrefixed(
                node.Id + "|period=" + node.ExactPeriodInBaseTimeUnit + "|phase=" + node.ExactInitialPhaseTurns));
        }

        foreach (var requirement in requirements)
        {
            builder.Append("|requirement=").Append(LengthPrefixed(
                requirement.FromSemanticNodeId + "|to=" + requirement.ToSemanticNodeId +
                "|transfer=" + requirement.SignedTargetTransfer + "|phase=" + requirement.ExactPhaseRelation));
        }

        foreach (var relation in derivedRelations)
        {
            builder.Append("|derived=").Append(LengthPrefixed(
                relation.FromSemanticNodeId + "|to=" + relation.ToSemanticNodeId +
                "|transfer=" + relation.SignedTransfer + "|phase=" + relation.ExactPhaseRelation));
        }

        return Hash("compiled-plan-sha256:", builder.ToString());
    }

    public static string Hash(string prefix, string canonicalRepresentation)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonicalRepresentation));
        }

        var builder = new StringBuilder(prefix);
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static string LengthPrefixed(string value) =>
        value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}

public sealed class PeriodicSemanticCompiler
{
    private readonly SemanticCompilerFingerprint _fingerprint;

    public PeriodicSemanticCompiler(SemanticCompilerFingerprint? fingerprint = null)
    {
        _fingerprint = fingerprint ?? new SemanticCompilerFingerprint(
            PeriodicSemanticContract.GenericCompilerId,
            PeriodicSemanticContract.GenericCompilerVersion,
            PeriodicSemanticContract.DefaultDeterminismProfile);
    }

    public SemanticCompilationResult Compile(
        SemanticPeriodicSpecification specification,
        string? sourceSemanticSpecificationId = null,
        string? sourceCatalogId = null,
        CancellationToken cancellationToken = default)
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));
        var semanticId = sourceSemanticSpecificationId ??
            PeriodicSemanticIdentity.ComputeGenericSemanticSpecificationId(specification);
        var catalogId = sourceCatalogId ?? semanticId;
        if (cancellationToken.IsCancellationRequested)
        {
            return Terminal(SemanticCompilationStatus.Cancelled, specification.SourceKind, catalogId, semanticId, DiagnosticCodes.SemanticCancelled,
                "Semantic compilation was cancelled.");
        }

        var diagnostics = new List<Diagnostic>();
        ValidateResourceBounds(specification, diagnostics);
        var nodes = NormalizeNodes(specification, diagnostics);
        var requirements = NormalizeRequirements(specification, nodes, diagnostics);

        if (!StringComparer.Ordinal.Equals(specification.BaseTimeUnit, PeriodicSemanticContract.BaseTimeUnitSeconds))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.SemanticUnsupportedDomain,
                DiagnosticSeverity.Error,
                "Only the exact base time unit 'second' is supported.",
                specification.BaseTimeUnit));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticCancelled, DiagnosticSeverity.Error,
                "Semantic compilation was cancelled."));
            return new SemanticCompilationResult(SemanticCompilationStatus.Cancelled, specification.SourceKind, catalogId, semanticId, _fingerprint, null, diagnostics);
        }

        var compiled = CompileRequirements(nodes, requirements, diagnostics);
        var topology = OrderAndValidateTopology(nodes, compiled, requirements, diagnostics);
        ValidateAlternatePaths(nodes, requirements, diagnostics);
        ValidateAllPathConsistency(nodes, requirements, diagnostics);

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            var unsupported = diagnostics.Any(item =>
                item.Code == DiagnosticCodes.SemanticUnsupportedDomain ||
                item.Code == DiagnosticCodes.SemanticUnsupportedPhase);
            return new SemanticCompilationResult(
                unsupported ? SemanticCompilationStatus.Unsupported : SemanticCompilationStatus.InvalidInput,
                specification.SourceKind,
                catalogId,
                semanticId,
                _fingerprint,
                null,
                diagnostics);
        }

        var ordered = topology.OrderedRequirements;
        var derived = BuildDerivedRelations(nodes, ordered);
        var planId = PeriodicSemanticIdentity.ComputeCompiledPlanId(
            specification.BaseTimeUnit,
            nodes.Values,
            ordered,
            derived);
        var plan = new CompiledMechanicalRequirementPlan(
            planId,
            specification.BaseTimeUnit,
            nodes.Values.OrderBy(item => item.Id, StringComparer.Ordinal),
            ordered,
            derived);
        return new SemanticCompilationResult(
            SemanticCompilationStatus.Complete,
            specification.SourceKind,
            catalogId,
            semanticId,
            _fingerprint,
            plan,
            diagnostics);
    }

    private SemanticCompilationResult Terminal(
        SemanticCompilationStatus status,
        string sourceKind,
        string sourceCatalogId,
        string semanticId,
        string code,
        string message)
    {
        return new SemanticCompilationResult(status, sourceKind, sourceCatalogId, semanticId, _fingerprint, null,
            new[] { new Diagnostic(code, DiagnosticSeverity.Error, message) });
    }

    private static void ValidateResourceBounds(SemanticPeriodicSpecification specification, ICollection<Diagnostic> diagnostics)
    {
        if (specification.Nodes.Count > PeriodicSemanticContract.MaximumNodes)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticResourceLimitExceeded, DiagnosticSeverity.Error,
                "Semantic specification exceeds the node resource bound."));
        }

        if (specification.Requirements.Count > PeriodicSemanticContract.MaximumRequirements)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticResourceLimitExceeded, DiagnosticSeverity.Error,
                "Semantic specification exceeds the requirement resource bound."));
        }
    }

    private static Dictionary<string, PeriodicRotationNode> NormalizeNodes(
        SemanticPeriodicSpecification specification,
        ICollection<Diagnostic> diagnostics)
    {
        var result = new Dictionary<string, PeriodicRotationNode>(StringComparer.Ordinal);
        foreach (var node in specification.Nodes.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !result.TryAdd(node.Id, node))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticDuplicateNodeId, DiagnosticSeverity.Error,
                    "Periodic rotation node IDs must be non-empty and unique.", node.Id));
                continue;
            }

            if (node.ExactPeriodInBaseTimeUnit <= Rational.Zero)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticNonPositivePeriod, DiagnosticSeverity.Error,
                    "A periodic rotation period must be exact and positive.", node.Id));
            }

            if (RationalDigitCount(node.ExactPeriodInBaseTimeUnit) > PeriodicSemanticContract.MaximumRationalDigits)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticResourceLimitExceeded, DiagnosticSeverity.Error,
                    "A periodic rotation period exceeds the exact rational digit bound.", node.Id));
            }

            if (!node.ExactInitialPhaseTurns.IsZero)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticUnsupportedPhase, DiagnosticSeverity.Error,
                    "Milestone 7A supports zero initial phase only.", node.Id));
            }
        }

        return result;
    }

    private static List<ContinuousRotationRequirement> NormalizeRequirements(
        SemanticPeriodicSpecification specification,
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        ICollection<Diagnostic> diagnostics)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ContinuousRotationRequirement>();
        foreach (var requirement in specification.Requirements.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(requirement.Id) || !ids.Add(requirement.Id))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticDuplicateRequirementId, DiagnosticSeverity.Error,
                    "Semantic requirement IDs must be non-empty and unique.", requirement.Id));
                continue;
            }

            if (requirement is not ContinuousRotationRequirement continuous)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticUnsupportedDomain, DiagnosticSeverity.Error,
                    "Requirement domain '" + requirement.Domain + "' is not supported by the periodic compiler.", requirement.Id));
                continue;
            }

            if (!nodes.ContainsKey(continuous.FromNodeId))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticUnknownNodeReference, DiagnosticSeverity.Error,
                    "The from-node reference does not exist.", continuous.Id));
            }

            if (!nodes.ContainsKey(continuous.ToNodeId))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticUnknownNodeReference, DiagnosticSeverity.Error,
                    "The to-node reference does not exist.", continuous.Id));
            }

            if (StringComparer.Ordinal.Equals(continuous.FromNodeId, continuous.ToNodeId))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticSelfReference, DiagnosticSeverity.Error,
                    "A continuous transfer requirement cannot reference the same node as input and output.", continuous.Id));
            }

            if (!continuous.ExactPhaseRelation.IsZero)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticUnsupportedPhase, DiagnosticSeverity.Error,
                    "Milestone 7A supports zero exact phase relation only.", continuous.Id));
            }

            result.Add(continuous);
        }

        return result;
    }

    private static List<CompiledContinuousTransferRequirement> CompileRequirements(
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        IEnumerable<ContinuousRotationRequirement> requirements,
        ICollection<Diagnostic> diagnostics)
    {
        var result = new List<CompiledContinuousTransferRequirement>();
        foreach (var requirement in requirements)
        {
            if (!nodes.TryGetValue(requirement.FromNodeId, out var from) ||
                !nodes.TryGetValue(requirement.ToNodeId, out var to) ||
                StringComparer.Ordinal.Equals(requirement.FromNodeId, requirement.ToNodeId))
            {
                continue;
            }

            var magnitude = from.ExactPeriodInBaseTimeUnit / to.ExactPeriodInBaseTimeUnit;
            var target = requirement.RequiredDirection == RotationDirection.Same ? magnitude : -magnitude;
            if (requirement.OptionalExplicitTransfer is Rational explicitTransfer && explicitTransfer != target)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticTransferContradiction, DiagnosticSeverity.Error,
                    "Explicit transfer '" + explicitTransfer + "' contradicts the period-derived transfer '" + target + "'.",
                    requirement.Id));
            }

            var id = PeriodicSemanticIdentity.ComputeCompiledRequirementId(
                requirement.FromNodeId,
                requirement.ToNodeId,
                target,
                requirement.ExactPhaseRelation);
            result.Add(new CompiledContinuousTransferRequirement(
                id,
                requirement.Id,
                requirement.FromNodeId,
                requirement.ToNodeId,
                target,
                requirement.ExactPhaseRelation));
        }

        return result;
    }

    private static TopologyResult OrderAndValidateTopology(
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        IEnumerable<CompiledContinuousTransferRequirement> compiled,
        IEnumerable<ContinuousRotationRequirement> sourceRequirements,
        ICollection<Diagnostic> diagnostics)
    {
        var indegree = nodes.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.Keys.ToDictionary(
            key => key,
            _ => new List<CompiledContinuousTransferRequirement>(),
            StringComparer.Ordinal);
        foreach (var edge in compiled)
        {
            indegree[edge.ToSemanticNodeId]++;
            outgoing[edge.FromSemanticNodeId].Add(edge);
        }

        var ready = indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        var depth = nodes.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        var visited = new List<string>();
        while (ready.Count > 0)
        {
            var current = ready[0];
            ready.RemoveAt(0);
            visited.Add(current);
            foreach (var edge in outgoing[current].OrderBy(item => item.CompiledRequirementId, StringComparer.Ordinal))
            {
                depth[edge.ToSemanticNodeId] = Math.Max(depth[edge.ToSemanticNodeId], depth[current] + 1);
                indegree[edge.ToSemanticNodeId]--;
                if (indegree[edge.ToSemanticNodeId] == 0)
                {
                    ready.Add(edge.ToSemanticNodeId);
                    ready.Sort(StringComparer.Ordinal);
                }
            }
        }

        if (visited.Count != nodes.Count)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticInconsistentCycle, DiagnosticSeverity.Error,
                "The continuous rotation graph contains a directed cycle and has no stable root-to-output ordering."));
        }

        var participating = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in sourceRequirements)
        {
            participating.Add(edge.FromNodeId);
            participating.Add(edge.ToNodeId);
        }

        if (nodes.Count > 1)
        {
            foreach (var nodeId in nodes.Keys.Where(id => !participating.Contains(id)))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticUnreachableNode, DiagnosticSeverity.Error,
                    "Periodic rotation node is not reachable through any continuous transfer requirement.", nodeId));
            }
        }

        var ordered = compiled.OrderBy(item => depth.TryGetValue(item.FromSemanticNodeId, out var value) ? value : int.MaxValue)
            .ThenBy(item => depth.TryGetValue(item.ToSemanticNodeId, out var value) ? value : int.MaxValue)
            .ThenByDescending(item => item.SignedTargetTransfer < Rational.Zero
                ? -item.SignedTargetTransfer
                : item.SignedTargetTransfer)
            .ThenBy(item => item.CompiledRequirementId, StringComparer.Ordinal)
            .ToList();
        return new TopologyResult(ordered);
    }

    private static void ValidateAlternatePaths(
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        IReadOnlyCollection<ContinuousRotationRequirement> requirements,
        ICollection<Diagnostic> diagnostics)
    {
        foreach (var edge in requirements)
        {
            if (!nodes.ContainsKey(edge.FromNodeId) || !nodes.ContainsKey(edge.ToNodeId)) continue;
            if (TryFindPathTransfer(
                    edge.FromNodeId,
                    edge.ToNodeId,
                    requirements,
                    nodes,
                    edge.Id,
                    new HashSet<string>(StringComparer.Ordinal),
                    out var alternate,
                    out _))
            {
                var observed = ObservedTransfer(edge, nodes);
                if (observed != alternate)
                {
                    diagnostics.Add(new Diagnostic(DiagnosticCodes.SemanticInconsistentCycle, DiagnosticSeverity.Error,
                        "Explicit transfer '" + observed + "' is inconsistent with alternate path transfer '" + alternate + "'.",
                        edge.Id));
                }
            }
        }
    }

    private static void ValidateAllPathConsistency(
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        IReadOnlyCollection<ContinuousRotationRequirement> requirements,
        ICollection<Diagnostic> diagnostics)
    {
        var validEdges = requirements.Where(edge =>
                nodes.ContainsKey(edge.FromNodeId) && nodes.ContainsKey(edge.ToNodeId))
            .ToList();
        var outgoing = nodes.Keys.ToDictionary(
            key => key,
            _ => new List<ContinuousRotationRequirement>(),
            StringComparer.Ordinal);
        foreach (var edge in validEdges)
        {
            outgoing[edge.FromNodeId].Add(edge);
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in nodes.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            var transfers = new Dictionary<string, Rational>(StringComparer.Ordinal)
            {
                [source] = Rational.One,
            };
            var queue = new List<string> { source };
            while (queue.Count > 0)
            {
                var current = queue[0];
                queue.RemoveAt(0);
                foreach (var edge in outgoing[current].OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    var candidate = transfers[current] * ObservedTransfer(edge, nodes);
                    if (transfers.TryGetValue(edge.ToNodeId, out var existing))
                    {
                        if (existing != candidate)
                        {
                            var subject = source + "->" + edge.ToNodeId;
                            if (reported.Add(subject))
                            {
                                diagnostics.Add(new Diagnostic(
                                    DiagnosticCodes.SemanticInconsistentCycle,
                                    DiagnosticSeverity.Error,
                                    "Multiple paths imply contradictory transfers '" + existing +
                                    "' and '" + candidate + "'.",
                                    subject));
                            }
                        }

                        continue;
                    }

                    transfers.Add(edge.ToNodeId, candidate);
                    queue.Add(edge.ToNodeId);
                }
            }
        }
    }

    private static List<DerivedContinuousTransferRelation> BuildDerivedRelations(
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        IReadOnlyCollection<CompiledContinuousTransferRequirement> requirements)
    {
        var result = new List<DerivedContinuousTransferRelation>();
        foreach (var from in nodes.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            foreach (var to in nodes.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (StringComparer.Ordinal.Equals(from, to)) continue;
                if (!TryFindCompiledPathTransfer(from, to, requirements, new HashSet<string>(StringComparer.Ordinal),
                        out var transfer, out var hops) || hops < 2)
                {
                    continue;
                }

                var id = PeriodicSemanticIdentity.ComputeDerivedRelationId(from, to, transfer, Rational.Zero);
                result.Add(new DerivedContinuousTransferRelation(id, from, to, transfer, Rational.Zero));
            }
        }

        return result.OrderBy(item => item.FromSemanticNodeId, StringComparer.Ordinal)
            .ThenBy(item => item.ToSemanticNodeId, StringComparer.Ordinal)
            .ThenBy(item => item.RelationId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryFindPathTransfer(
        string current,
        string target,
        IReadOnlyCollection<ContinuousRotationRequirement> requirements,
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes,
        string excludedRequirementId,
        ISet<string> path,
        out Rational transfer,
        out int hops)
    {
        path.Add(current);
        foreach (var edge in requirements.Where(item =>
                     item.Id != excludedRequirementId &&
                     nodes.ContainsKey(item.FromNodeId) &&
                     nodes.ContainsKey(item.ToNodeId) &&
                     StringComparer.Ordinal.Equals(item.FromNodeId, current))
                 .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (path.Contains(edge.ToNodeId)) continue;
            var edgeTransfer = ObservedTransfer(edge, nodes);
            if (StringComparer.Ordinal.Equals(edge.ToNodeId, target))
            {
                transfer = edgeTransfer;
                hops = 1;
                path.Remove(current);
                return true;
            }

            if (TryFindPathTransfer(edge.ToNodeId, target, requirements, nodes, excludedRequirementId, path,
                    out var tail, out var tailHops))
            {
                transfer = edgeTransfer * tail;
                hops = tailHops + 1;
                path.Remove(current);
                return true;
            }
        }

        path.Remove(current);
        transfer = Rational.Zero;
        hops = 0;
        return false;
    }

    private static Rational ObservedTransfer(
        ContinuousRotationRequirement requirement,
        IReadOnlyDictionary<string, PeriodicRotationNode> nodes)
    {
        if (requirement.OptionalExplicitTransfer is Rational explicitTransfer)
        {
            return explicitTransfer;
        }

        var magnitude = nodes[requirement.FromNodeId].ExactPeriodInBaseTimeUnit /
            nodes[requirement.ToNodeId].ExactPeriodInBaseTimeUnit;
        return requirement.RequiredDirection == RotationDirection.Same ? magnitude : -magnitude;
    }

    private static bool TryFindCompiledPathTransfer(
        string current,
        string target,
        IReadOnlyCollection<CompiledContinuousTransferRequirement> requirements,
        ISet<string> path,
        out Rational transfer,
        out int hops)
    {
        path.Add(current);
        foreach (var edge in requirements.Where(item => StringComparer.Ordinal.Equals(item.FromSemanticNodeId, current))
                     .OrderBy(item => item.CompiledRequirementId, StringComparer.Ordinal))
        {
            if (path.Contains(edge.ToSemanticNodeId)) continue;
            if (StringComparer.Ordinal.Equals(edge.ToSemanticNodeId, target))
            {
                transfer = edge.SignedTargetTransfer;
                hops = 1;
                path.Remove(current);
                return true;
            }

            if (TryFindCompiledPathTransfer(edge.ToSemanticNodeId, target, requirements, path,
                    out var tail, out var tailHops))
            {
                transfer = edge.SignedTargetTransfer * tail;
                hops = tailHops + 1;
                path.Remove(current);
                return true;
            }
        }

        path.Remove(current);
        transfer = Rational.Zero;
        hops = 0;
        return false;
    }

    private static int RationalDigitCount(Rational value)
    {
        return value.Numerator.ToString(CultureInfo.InvariantCulture).TrimStart('-').Length +
            value.Denominator.ToString(CultureInfo.InvariantCulture).Length;
    }

    private sealed class TopologyResult
    {
        public TopologyResult(List<CompiledContinuousTransferRequirement> orderedRequirements)
        {
            OrderedRequirements = orderedRequirements;
        }

        public List<CompiledContinuousTransferRequirement> OrderedRequirements { get; }
    }
}
