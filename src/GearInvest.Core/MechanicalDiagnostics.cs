using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

/// <summary>Typed references remain useful when their former object no longer exists.</summary>
public sealed class MechanicalReference
{
    public MechanicalReference(string kind, string id) { Kind = kind; Id = id; }
    public string Kind { get; }
    public string Id { get; }
    public string Key => Kind + "/" + Id;
}

public sealed class MechanicalExactFact
{
    public MechanicalExactFact(string key, Rational? expected, Rational? actual)
    { Key = key; Expected = expected; Actual = actual; }
    public string Key { get; }
    public Rational? Expected { get; }
    public Rational? Actual { get; }
}

public sealed class MechanicalPathWitness
{
    public MechanicalPathWitness(string rootId, string targetId, Rational coefficient, Rational phase,
        IEnumerable<string> constraintIds)
    { RootId = rootId; TargetId = targetId; Coefficient = coefficient; Phase = phase; ConstraintIds = constraintIds.ToList().AsReadOnly(); }
    public string RootId { get; }
    public string TargetId { get; }
    public Rational Coefficient { get; }
    public Rational Phase { get; }
    /// <summary>Ordered path, not a set.</summary>
    public ReadOnlyCollection<string> ConstraintIds { get; }
}

/// <summary>Structured product diagnostic; prose is never used to recover authoritative values.</summary>
public sealed class MechanicalDiagnostic
{
    public MechanicalDiagnostic(string code, string stage, DiagnosticSeverity severity = DiagnosticSeverity.Error,
        bool required = true, IEnumerable<MechanicalReference>? related = null,
        IEnumerable<MechanicalExactFact>? facts = null, IEnumerable<string>? affectedOutputs = null,
        IEnumerable<MechanicalPathWitness>? paths = null, IEnumerable<string>? blockedPrerequisites = null,
        string? proofReference = null, string scope = "WholeDraft", bool complete = true, string detail = "")
    {
        Code = code; Stage = stage; Severity = severity; Required = required;
        Related = (related ?? Array.Empty<MechanicalReference>()).OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = (facts ?? Array.Empty<MechanicalExactFact>()).OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        AffectedOutputs = (affectedOutputs ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        Paths = (paths ?? Array.Empty<MechanicalPathWitness>()).ToList().AsReadOnly();
        BlockedPrerequisites = (blockedPrerequisites ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        ProofReference = proofReference; Scope = scope; Complete = complete; Detail = detail;
    }
    public string Code { get; }
    public string Stage { get; }
    public DiagnosticSeverity Severity { get; }
    public bool Required { get; }
    public ReadOnlyCollection<MechanicalReference> Related { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<string> AffectedOutputs { get; }
    public ReadOnlyCollection<MechanicalPathWitness> Paths { get; }
    public ReadOnlyCollection<string> BlockedPrerequisites { get; }
    public string? ProofReference { get; }
    public string Scope { get; }
    public bool Complete { get; }
    public string Detail { get; }
}
