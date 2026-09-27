using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public sealed class RotationalDof
{
    public RotationalDof(string id, bool isPrescribed = false)
    {
        Id = RequireId(id, nameof(id));
        IsPrescribed = isPrescribed;
    }

    public string Id { get; }

    public bool IsPrescribed { get; }

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable ID is required.", parameterName);
        }

        return value;
    }
}

public sealed class ExternalGearCoupling
{
    public ExternalGearCoupling(
        string id,
        string driverDofId,
        string drivenDofId,
        int driverTeeth,
        int drivenTeeth,
        Rational phaseOffset = default)
    {
        Id = RequireId(id, nameof(id));
        DriverDofId = RequireId(driverDofId, nameof(driverDofId));
        DrivenDofId = RequireId(drivenDofId, nameof(drivenDofId));
        DriverTeeth = driverTeeth;
        DrivenTeeth = drivenTeeth;
        PhaseOffset = phaseOffset;
    }

    public string Id { get; }

    public string DriverDofId { get; }

    public string DrivenDofId { get; }

    public int DriverTeeth { get; }

    public int DrivenTeeth { get; }

    public Rational PhaseOffset { get; }

    public Rational Transfer => new(BigInteger.Negate(new BigInteger(DriverTeeth)), new BigInteger(DrivenTeeth));

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable ID is required.", parameterName);
        }

        return value;
    }
}

public sealed class KinematicSpecification
{
    public KinematicSpecification(
        string rootDofId,
        IEnumerable<RotationalDof> dofs,
        IEnumerable<ExternalGearCoupling> couplings)
    {
        if (string.IsNullOrWhiteSpace(rootDofId))
        {
            throw new ArgumentException("A root DOF ID is required.", nameof(rootDofId));
        }

        RootDofId = rootDofId;
        Dofs = Sort(dofs, item => item.Id);
        Couplings = Sort(couplings, item => item.Id);
    }

    public string RootDofId { get; }

    public ReadOnlyCollection<RotationalDof> Dofs { get; }

    public ReadOnlyCollection<ExternalGearCoupling> Couplings { get; }

    private static ReadOnlyCollection<T> Sort<T>(IEnumerable<T> source, Func<T, string> idSelector)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var items = source.ToList();
        items.Sort((left, right) => StringComparer.Ordinal.Compare(idSelector(left), idSelector(right)));
        return items.AsReadOnly();
    }
}

public sealed class DofKinematicState
{
    public DofKinematicState(string dofId, Rational coefficient, Rational phaseOffset)
    {
        DofId = dofId ?? throw new ArgumentNullException(nameof(dofId));
        Coefficient = coefficient;
        PhaseOffset = phaseOffset;
    }

    public string DofId { get; }

    public Rational Coefficient { get; }

    public Rational PhaseOffset { get; }
}

public sealed class KinematicSolution
{
    private readonly IReadOnlyDictionary<string, DofKinematicState> _byDofId;

    public KinematicSolution(string rootDofId, IEnumerable<DofKinematicState> states)
    {
        RootDofId = rootDofId ?? throw new ArgumentNullException(nameof(rootDofId));
        var ordered = states?.OrderBy(state => state.DofId, StringComparer.Ordinal).ToList()
            ?? throw new ArgumentNullException(nameof(states));
        States = ordered.AsReadOnly();
        _byDofId = ordered.ToDictionary(state => state.DofId, StringComparer.Ordinal);
    }

    public string RootDofId { get; }

    public ReadOnlyCollection<DofKinematicState> States { get; }

    public bool TryGetState(string dofId, out DofKinematicState? state)
    {
        if (_byDofId.TryGetValue(dofId, out var found))
        {
            state = found;
            return true;
        }

        state = null;
        return false;
    }
}

public sealed class KinematicSolveResult
{
    public KinematicSolveResult(KinematicSolution? solution, IEnumerable<Diagnostic> diagnostics)
    {
        Solution = solution;
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }

    public KinematicSolution? Solution { get; }

    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }

    public bool IsValid => Solution is not null && Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public sealed class DofEvaluation
{
    public DofEvaluation(string dofId, Rational unwrappedTurns)
    {
        DofId = dofId ?? throw new ArgumentNullException(nameof(dofId));
        UnwrappedTurns = unwrappedTurns;
    }

    public string DofId { get; }

    public Rational UnwrappedTurns { get; }
}

public sealed class KinematicEvaluation
{
    public KinematicEvaluation(Rational rootTurns, IEnumerable<DofEvaluation> values)
    {
        RootTurns = rootTurns;
        Values = values.OrderBy(value => value.DofId, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    public Rational RootTurns { get; }

    public ReadOnlyCollection<DofEvaluation> Values { get; }
}

public static class KinematicSolver
{
    public static KinematicSolveResult Solve(KinematicSpecification specification)
    {
        if (specification is null)
        {
            throw new ArgumentNullException(nameof(specification));
        }

        var diagnostics = new List<Diagnostic>();
        var dofs = BuildDofMap(specification, diagnostics);
        var couplings = BuildCouplingMap(specification, diagnostics);

        if (!dofs.TryGetValue(specification.RootDofId, out var root))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnknownRootDof,
                DiagnosticSeverity.Error,
                $"Root DOF '{specification.RootDofId}' does not exist.",
                specification.RootDofId));
            return new KinematicSolveResult(null, diagnostics);
        }

        if (!root.IsPrescribed)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.RootNotPrescribed,
                DiagnosticSeverity.Error,
                $"Root DOF '{root.Id}' must be prescribed.",
                root.Id));
        }

        var adjacency = dofs.Keys.ToDictionary(
            id => id,
            _ => new List<PropagationEdge>(),
            StringComparer.Ordinal);

        foreach (var coupling in couplings.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (coupling.DriverTeeth <= 0 || coupling.DrivenTeeth <= 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidToothCount,
                    DiagnosticSeverity.Error,
                    $"Coupling '{coupling.Id}' must use positive tooth counts.",
                    coupling.Id));
                continue;
            }

            if (!dofs.ContainsKey(coupling.DriverDofId) || !dofs.ContainsKey(coupling.DrivenDofId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownDofReference,
                    DiagnosticSeverity.Error,
                    $"Coupling '{coupling.Id}' references a DOF that does not exist.",
                    coupling.Id));
                continue;
            }

            var transfer = coupling.Transfer;
            adjacency[coupling.DriverDofId].Add(new PropagationEdge(
                coupling.Id,
                coupling.DrivenDofId,
                transfer,
                coupling.PhaseOffset));

            adjacency[coupling.DrivenDofId].Add(new PropagationEdge(
                coupling.Id,
                coupling.DriverDofId,
                Rational.One / transfer,
                -coupling.PhaseOffset / transfer));
        }

        return Propagate(root, dofs, adjacency, diagnostics);
    }

    private static KinematicSolveResult Propagate(RotationalDof root, IReadOnlyDictionary<string, RotationalDof> dofs,
        Dictionary<string, List<PropagationEdge>> adjacency, List<Diagnostic> diagnostics)
    {
        foreach (var edges in adjacency.Values)
        {
            edges.Sort(PropagationEdge.Compare);
        }

        var solved = new Dictionary<string, AffineState>(StringComparer.Ordinal)
        {
            [root.Id] = new AffineState(Rational.One, Rational.Zero),
        };
        var queue = new Queue<string>();
        queue.Enqueue(root.Id);
        var reportedContradictions = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var current = solved[currentId];

            foreach (var edge in adjacency[currentId])
            {
                var propagated = new ExactAffineRelation(current.Coefficient, current.PhaseOffset).Then(edge.Transfer, edge.PhaseOffset);
                var candidate = new AffineState(propagated.Coefficient, propagated.Phase);

                if (!solved.TryGetValue(edge.TargetDofId, out var existing))
                {
                    solved.Add(edge.TargetDofId, candidate);
                    queue.Enqueue(edge.TargetDofId);
                    continue;
                }

                if (existing.Equals(candidate))
                {
                    continue;
                }

                var contradictionKey = edge.ConstraintId + "\u001f" + edge.TargetDofId;
                if (reportedContradictions.Add(contradictionKey))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.Contradiction,
                        DiagnosticSeverity.Error,
                        $"Constraint '{edge.ConstraintId}' reaches DOF '{edge.TargetDofId}' as " +
                        $"{candidate.Coefficient}*root+{candidate.PhaseOffset}, but the existing value is " +
                        $"{existing.Coefficient}*root+{existing.PhaseOffset}.",
                        edge.ConstraintId));
                }
            }
        }

        foreach (var dof in dofs.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!solved.ContainsKey(dof.Id))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnreachableDof,
                    DiagnosticSeverity.Error,
                    $"DOF '{dof.Id}' is unreachable from root '{root.Id}'.",
                    dof.Id));
            }
        }

        var states = solved
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DofKinematicState(pair.Key, pair.Value.Coefficient, pair.Value.PhaseOffset));
        return new KinematicSolveResult(new KinematicSolution(root.Id, states), diagnostics);
    }

    /// <summary>Exact scalar graph solve only. Callers must independently establish the mechanical meaning of each lowered relation.</summary>
    public static KinematicSolveResult SolveAffine(string rootDofId, IEnumerable<RotationalDof> dofSource, IEnumerable<ScalarAffineCoupling> constraintSource)
    {
        if (dofSource is null || constraintSource is null) throw new ArgumentNullException();
        var diagnostics = new List<Diagnostic>();
        var dofs = new Dictionary<string, RotationalDof>(StringComparer.Ordinal);
        foreach (var dof in dofSource)
            if (!dofs.TryAdd(dof.Id, dof)) diagnostics.Add(new Diagnostic(DiagnosticCodes.DuplicateDofId, DiagnosticSeverity.Error, "Duplicate affine DOF.", dof.Id));
        if (!dofs.TryGetValue(rootDofId, out var root))
            return new KinematicSolveResult(null, new[] { new Diagnostic(DiagnosticCodes.UnknownRootDof, DiagnosticSeverity.Error, "Unknown affine root.", rootDofId) });
        if (!root.IsPrescribed || dofs.Values.Count(d => d.IsPrescribed) != 1)
            diagnostics.Add(new Diagnostic(DiagnosticCodes.RootNotPrescribed, DiagnosticSeverity.Error, "Exactly one prescribed driver, the root, is required.", rootDofId));
        var adjacency = dofs.Keys.ToDictionary(id => id, _ => new List<PropagationEdge>(), StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in constraintSource.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (!ids.Add(edge.Id)) diagnostics.Add(new Diagnostic(DiagnosticCodes.DuplicateConstraintId, DiagnosticSeverity.Error, "Duplicate affine constraint.", edge.Id));
            if (!dofs.ContainsKey(edge.DriverDofId) || !dofs.ContainsKey(edge.DrivenDofId) || edge.Transfer.IsZero)
            { diagnostics.Add(new Diagnostic(DiagnosticCodes.UnknownDofReference, DiagnosticSeverity.Error, "An invertible affine edge requires two known DOFs.", edge.Id)); continue; }
            adjacency[edge.DriverDofId].Add(new PropagationEdge(edge.Id, edge.DrivenDofId, edge.Transfer, edge.PhaseOffset));
            adjacency[edge.DrivenDofId].Add(new PropagationEdge(edge.Id, edge.DriverDofId, Rational.One / edge.Transfer, -edge.PhaseOffset / edge.Transfer));
        }
        return Propagate(root, dofs, adjacency, diagnostics);
    }

    private static Dictionary<string, RotationalDof> BuildDofMap(
        KinematicSpecification specification,
        ICollection<Diagnostic> diagnostics)
    {
        var result = new Dictionary<string, RotationalDof>(StringComparer.Ordinal);
        foreach (var dof in specification.Dofs)
        {
            if (!result.TryAdd(dof.Id, dof))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DuplicateDofId,
                    DiagnosticSeverity.Error,
                    $"DOF ID '{dof.Id}' is duplicated.",
                    dof.Id));
            }
        }

        return result;
    }

    private static Dictionary<string, ExternalGearCoupling> BuildCouplingMap(
        KinematicSpecification specification,
        ICollection<Diagnostic> diagnostics)
    {
        var result = new Dictionary<string, ExternalGearCoupling>(StringComparer.Ordinal);
        foreach (var coupling in specification.Couplings)
        {
            if (!result.TryAdd(coupling.Id, coupling))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DuplicateConstraintId,
                    DiagnosticSeverity.Error,
                    $"Constraint ID '{coupling.Id}' is duplicated.",
                    coupling.Id));
            }
        }

        return result;
    }

    private sealed class PropagationEdge
    {
        public PropagationEdge(string constraintId, string targetDofId, Rational transfer, Rational phaseOffset)
        {
            ConstraintId = constraintId;
            TargetDofId = targetDofId;
            Transfer = transfer;
            PhaseOffset = phaseOffset;
        }

        public string ConstraintId { get; }

        public string TargetDofId { get; }

        public Rational Transfer { get; }

        public Rational PhaseOffset { get; }

        public static int Compare(PropagationEdge left, PropagationEdge right)
        {
            var byTarget = StringComparer.Ordinal.Compare(left.TargetDofId, right.TargetDofId);
            return byTarget != 0
                ? byTarget
                : StringComparer.Ordinal.Compare(left.ConstraintId, right.ConstraintId);
        }
    }

    private readonly struct AffineState : IEquatable<AffineState>
    {
        public AffineState(Rational coefficient, Rational phaseOffset)
        {
            Coefficient = coefficient;
            PhaseOffset = phaseOffset;
        }

        public Rational Coefficient { get; }

        public Rational PhaseOffset { get; }

        public bool Equals(AffineState other)
        {
            return Coefficient == other.Coefficient && PhaseOffset == other.PhaseOffset;
        }

        public override bool Equals(object? obj)
        {
            return obj is AffineState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Coefficient.GetHashCode() * 397) ^ PhaseOffset.GetHashCode();
            }
        }
    }
}

public static class KinematicEvaluator
{
    public static KinematicEvaluation Evaluate(KinematicSolution solution, Rational rootTurns)
    {
        if (solution is null)
        {
            throw new ArgumentNullException(nameof(solution));
        }

        var values = solution.States.Select(state => new DofEvaluation(
            state.DofId,
            (rootTurns * state.Coefficient) + state.PhaseOffset));
        return new KinematicEvaluation(rootTurns, values);
    }
}
