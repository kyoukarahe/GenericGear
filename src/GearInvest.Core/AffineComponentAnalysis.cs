using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace GearInvest.Core;

/// <summary>Exact unwrapped scalar relation q*s+p. A local symbolic parameter is not a prescribed physical input.</summary>
public readonly struct ExactAffineRelation : IEquatable<ExactAffineRelation>
{
    public ExactAffineRelation(Rational coefficient, Rational phase) { Coefficient = coefficient; Phase = phase; }
    public Rational Coefficient { get; }
    public Rational Phase { get; }
    public Rational Evaluate(Rational parameter) => Coefficient * parameter + Phase;
    public ExactAffineRelation Then(Rational transfer, Rational phase) => new(Coefficient * transfer, Phase * transfer + phase);
    public ExactAffineRelation Inverse() => new(Rational.One / Coefficient, -Phase / Coefficient);
    public bool Equals(ExactAffineRelation other) => Coefficient == other.Coefficient && Phase == other.Phase;
    public override bool Equals(object? obj) => obj is ExactAffineRelation other && Equals(other);
    public override int GetHashCode() { unchecked { return Coefficient.GetHashCode() * 397 ^ Phase.GetHashCode(); } }
}

public enum MechanicalDeterminacy
{
    DeterminedBySelectedInput, UndrivenRelativeMotion, PinnedByConstraints,
    InconsistentWithPrescribedInput, InconsistentConstraints, BlockedByInvalidConstraint, UnsupportedConstraintDomain
}

public sealed class AffineNodeRelation
{
    internal AffineNodeRelation(string dofId, ExactAffineRelation relation, IEnumerable<string> path)
    { DofId = dofId; Relation = relation; ConstraintPath = path.ToList().AsReadOnly(); }
    public string DofId { get; }
    public ExactAffineRelation Relation { get; }
    public ReadOnlyCollection<string> ConstraintPath { get; }
}

public sealed class AffineClosure
{
    internal AffineClosure(string constraintId, Rational coefficient, Rational phase, MechanicalPathWitness existing, MechanicalPathWitness alternative)
    { ConstraintId = constraintId; Coefficient = coefficient; Phase = phase; Existing = existing; Alternative = alternative; }
    public string ConstraintId { get; }
    /// <summary>Every parameter must satisfy Coefficient*s+Phase=0.</summary>
    public Rational Coefficient { get; }
    public Rational Phase { get; }
    public bool IsRedundant => Coefficient.IsZero && Phase.IsZero;
    public MechanicalPathWitness Existing { get; }
    public MechanicalPathWitness Alternative { get; }
}

public sealed class AffineComponentAnalysis
{
    internal AffineComponentAnalysis(string parameterId, IEnumerable<string> members, IEnumerable<string> constraints,
        IEnumerable<string> inputs, MechanicalDeterminacy determinacy, IEnumerable<AffineNodeRelation> relations,
        IEnumerable<AffineClosure> closures, Rational? pinnedParameter, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        ParameterId = parameterId; MemberIds = members.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        ConstraintIds = constraints.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        PrescribedInputIds = inputs.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        Determinacy = determinacy; Relations = relations.OrderBy(x => x.DofId, StringComparer.Ordinal).ToList().AsReadOnly();
        Closures = closures.ToList().AsReadOnly(); PinnedParameter = pinnedParameter; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string ParameterId { get; }
    public ReadOnlyCollection<string> MemberIds { get; }
    public ReadOnlyCollection<string> ConstraintIds { get; }
    public ReadOnlyCollection<string> PrescribedInputIds { get; }
    public MechanicalDeterminacy Determinacy { get; }
    /// <summary>Relative symbolic relations only; authoritative driven motion only when DeterminedBySelectedInput.</summary>
    public ReadOnlyCollection<AffineNodeRelation> Relations { get; }
    public ReadOnlyCollection<AffineClosure> Closures { get; }
    public Rational? PinnedParameter { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

/// <summary>Bounded scalar mathematical analysis, not admission of physical gear or nonzero tooth phase geometry.</summary>
public static class AffineComponentAnalyzer
{
    public static ReadOnlyCollection<AffineComponentAnalysis> Analyze(string? selectedInputId,
        IEnumerable<RotationalDof> dofSource, IEnumerable<ScalarAffineCoupling> constraintSource)
    {
        if (dofSource is null || constraintSource is null) throw new ArgumentNullException();
        return AnalyzeScalars(selectedInputId, dofSource.Select(d => new ScalarCoordinate(d.Id, d.IsPrescribed)), constraintSource, 64, 192, 128);
    }

    internal static ReadOnlyCollection<AffineComponentAnalysis> AnalyzeScalars(string? selectedInputId,
        IEnumerable<ScalarCoordinate> dofSource, IEnumerable<ScalarAffineCoupling> constraintSource,
        int maxNodes, int maxEdges, int edgeDigits)
    {
        if (dofSource is null || constraintSource is null) throw new ArgumentNullException();
        var dofs = dofSource.Take(maxNodes + 1).OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        var edges = constraintSource.Take(maxEdges + 1).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        if (dofs.Length > maxNodes || edges.Length > maxEdges || dofs.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != dofs.Length ||
            edges.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != edges.Length) throw new ArgumentException("Bounded unique affine IDs required.");
        var map = dofs.ToDictionary(d => d.Id, StringComparer.Ordinal);
        if (selectedInputId is not null && !map.ContainsKey(selectedInputId)) throw new ArgumentException("Unknown selected affine input.");
        var adjacency = dofs.ToDictionary(d => d.Id, _ => new List<Edge>(), StringComparer.Ordinal);
        foreach (var c in edges)
        {
            if (!map.ContainsKey(c.DriverDofId) || !map.ContainsKey(c.DrivenDofId) || c.Transfer.IsZero)
                throw new ArgumentException("Every admitted affine constraint requires known endpoints and nonzero transfer.");
            Bound(c.Transfer, edgeDigits); Bound(c.PhaseOffset, edgeDigits);
            var relation = new ExactAffineRelation(c.Transfer, c.PhaseOffset);
            adjacency[c.DriverDofId].Add(new Edge(c.Id, c.DrivenDofId, relation));
            adjacency[c.DrivenDofId].Add(new Edge(c.Id, c.DriverDofId, relation.Inverse()));
        }
        foreach (var list in adjacency.Values) list.Sort((a, b) => { var c = StringComparer.Ordinal.Compare(a.Target, b.Target); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Id, b.Id); });
        var remaining = new HashSet<string>(map.Keys, StringComparer.Ordinal); var result = new List<AffineComponentAnalysis>();
        while (remaining.Count > 0)
        {
            var first = remaining.OrderBy(x => x, StringComparer.Ordinal).First(); var members = new HashSet<string>(StringComparer.Ordinal); var work = new Queue<string>(); work.Enqueue(first);
            while (work.Count > 0) { var id = work.Dequeue(); if (!members.Add(id)) continue; foreach (var e in adjacency[id]) work.Enqueue(e.Target); }
            remaining.ExceptWith(members);
            var parameter = selectedInputId is not null && members.Contains(selectedInputId) ? selectedInputId : first;
            var relations = new Dictionary<string, AffineNodeRelation>(StringComparer.Ordinal) { [parameter] = new AffineNodeRelation(parameter, new ExactAffineRelation(1, 0), Array.Empty<string>()) };
            var treeEdges = new HashSet<string>(StringComparer.Ordinal); work.Enqueue(parameter);
            while (work.Count > 0)
            {
                var id = work.Dequeue(); var source = relations[id];
                foreach (var e in adjacency[id])
                {
                    if (relations.ContainsKey(e.Target)) continue;
                    var r = source.Relation.Then(e.Relation.Coefficient, e.Relation.Phase); Bound(r.Coefficient, 8192); Bound(r.Phase, 8192);
                    relations.Add(e.Target, new AffineNodeRelation(e.Target, r, source.ConstraintPath.Concat(new[] { e.Id }))); treeEdges.Add(e.Id); work.Enqueue(e.Target);
                }
            }
            var componentEdges = edges.Where(c => members.Contains(c.DriverDofId)).ToArray(); var closures = new List<AffineClosure>(); Rational? pin = null; var impossible = false;
            foreach (var c in componentEdges.Where(c => !treeEdges.Contains(c.Id)))
            {
                var from = relations[c.DriverDofId]; var to = relations[c.DrivenDofId]; var alternative = from.Relation.Then(c.Transfer, c.PhaseOffset);
                var a = alternative.Coefficient - to.Relation.Coefficient; var b = alternative.Phase - to.Relation.Phase; Bound(a, 8192); Bound(b, 8192);
                closures.Add(new AffineClosure(c.Id, a, b, Witness(parameter, to),
                    new MechanicalPathWitness(parameter, to.DofId, alternative.Coefficient, alternative.Phase, from.ConstraintPath.Concat(new[] { c.Id }))));
                if (a.IsZero) { if (!b.IsZero) impossible = true; }
                else { var requiredPin = -b / a; Bound(requiredPin, 8192); if (pin.HasValue && pin.Value != requiredPin) impossible = true; pin = requiredPin; }
            }
            var inputs = members.Where(id => map[id].IsPrescribed).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var constrained = closures.Any(c => !c.IsRedundant);
            var status = inputs.Length > 1 || (inputs.Length == 1 && inputs[0] != selectedInputId)
                ? MechanicalDeterminacy.UnsupportedConstraintDomain
                : inputs.Length == 1 && constrained ? MechanicalDeterminacy.InconsistentWithPrescribedInput
                : impossible ? MechanicalDeterminacy.InconsistentConstraints
                : pin.HasValue ? MechanicalDeterminacy.PinnedByConstraints
                : inputs.Length == 1 ? MechanicalDeterminacy.DeterminedBySelectedInput : MechanicalDeterminacy.UndrivenRelativeMotion;
            var diagnostics = new List<MechanicalDiagnostic>();
            foreach (var closure in closures.Where(c => !c.IsRedundant))
                diagnostics.Add(new MechanicalDiagnostic(status == MechanicalDeterminacy.PinnedByConstraints ? "PinnedByConstraints" : "ConflictingTransferPaths", "AffineClosure",
                    status == MechanicalDeterminacy.PinnedByConstraints ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    related: new[] { new MechanicalReference("Constraint", closure.ConstraintId), new MechanicalReference("Shaft", closure.Existing.TargetId) },
                    facts: new[] { new MechanicalExactFact("closureCoefficient", 0, closure.Coefficient), new MechanicalExactFact("closurePhase", 0, closure.Phase) },
                    paths: new[] { closure.Existing, closure.Alternative }, scope: "AffineComponent"));
            if (status == MechanicalDeterminacy.UnsupportedConstraintDomain || status == MechanicalDeterminacy.InconsistentWithPrescribedInput)
                diagnostics.Add(new MechanicalDiagnostic(status.ToString(), "AffineDeterminacy", related: inputs.Select(id => new MechanicalReference("Shaft", id)), scope: "AffineComponent"));
            result.Add(new AffineComponentAnalysis(parameter, members, componentEdges.Select(c => c.Id), inputs, status, relations.Values, closures, impossible ? null : pin, diagnostics));
        }
        return result.OrderBy(c => c.MemberIds[0], StringComparer.Ordinal).ToList().AsReadOnly();
    }
    private static MechanicalPathWitness Witness(string root, AffineNodeRelation node) => new(root, node.DofId, node.Relation.Coefficient, node.Relation.Phase, node.ConstraintPath);
    private static void Bound(Rational value, int digits)
    { if (value.Numerator.ToString(CultureInfo.InvariantCulture).Length > digits || value.Denominator.ToString(CultureInfo.InvariantCulture).Length > digits) throw new ArgumentException("Affine exact digit bound exceeded."); }
    private sealed class Edge
    { internal Edge(string id, string target, ExactAffineRelation relation) { Id = id; Target = target; Relation = relation; } internal string Id { get; } internal string Target { get; } internal ExactAffineRelation Relation { get; } }
}

internal sealed class ScalarCoordinate
{
    internal ScalarCoordinate(string id, bool isPrescribed) { Id = id; IsPrescribed = isPrescribed; }
    internal string Id { get; }
    internal bool IsPrescribed { get; }
}
