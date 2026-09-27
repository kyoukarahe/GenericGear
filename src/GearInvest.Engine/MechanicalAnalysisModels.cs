using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum MechanicalAxisVerdict { Pass, Fail, Inconclusive, NotAssessed }
public enum MechanicalExportAdmission { RequiresProfileValidation, BlockedByAnalysis }
public enum ConnectionCompatibilityVerdict { CompatibleWithinProfile, Incompatible, Unsupported, Inconclusive, InvalidInput }

internal static class MechanicalDerivedNumbers
{
    // Every Int64 value has at most20 signed decimal characters. This is only a
    // sufficient admission test: larger values still receive the exact full check.
    // Direct mixed BigInteger/Int64 operators also handle long.MinValue without Abs.
    internal static bool IsWithinComponentBound(BigInteger value) =>
        (value >= long.MinValue && value <= long.MaxValue) || ComponentBounds.Contains(value);

    private static class ComponentBounds
    {
        private static readonly BigInteger Positive = BigInteger.Pow(10, MechanicalAuthoringProfile.MaxDerivedDigits);
        private static readonly BigInteger Negative = -BigInteger.Pow(10, MechanicalAuthoringProfile.MaxDerivedDigits - 1);

        // Deliberately no beforefieldinit: small-only consumers must not build the
        // powers. Runtime type initialization publishes these immutable values once.
        static ComponentBounds() { }

        // Keep the initialization boundary off the small path in JIT and AOT code.
        // The invariant decimal minus sign consumes one of the allowed characters.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Contains(BigInteger value) => value.Sign < 0 ? value > Negative : value < Positive;
    }

    internal static void Check(Rational value)
    {
        if (!IsWithinComponentBound(value.Numerator) || !IsWithinComponentBound(value.Denominator))
            throw new ArgumentException("Derived exact digit bound exceeded.");
    }
}

public sealed class ConnectionCompatibilityResult
{
    internal ConnectionCompatibilityResult(string kind, string id, ConnectionCompatibilityVerdict verdict, IEnumerable<MechanicalReference> involved,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalExactFact> facts, Rational? transfer, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Kind = kind; ConnectionId = id; Verdict = verdict; Involved = involved.OrderBy(r => r.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList().AsReadOnly(); SignedTransfer = transfer; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public string Kind { get; }
    public string ConnectionId { get; }
    public string Scope => "LocalConnectionOnly";
    public ConnectionCompatibilityVerdict Verdict { get; }
    public ReadOnlyCollection<MechanicalReference> Involved { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public Rational? SignedTransfer { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public sealed class MechanicalAnalyzedEdge
{
    internal MechanicalAnalyzedEdge(string kind, string id, string? a, string? b, ConnectionCompatibilityResult local)
    { Kind = kind; Id = id; ShaftAId = a; ShaftBId = b; Compatibility = local; }
    public string Kind { get; }
    public string Id { get; }
    public string ConstraintKey => Kind + "/" + Id;
    public string? ShaftAId { get; }
    public string? ShaftBId { get; }
    public bool IsDeclaredResolvable => ShaftAId is not null && ShaftBId is not null;
    public bool IsAdmitted => IsDeclaredResolvable && Compatibility.Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile;
    public ConnectionCompatibilityResult Compatibility { get; }
}

public sealed class MechanicalConnectivityComponent
{
    internal MechanicalConnectivityComponent(IEnumerable<string> shafts, IEnumerable<string> bodies, IEnumerable<string> ports,
        IEnumerable<string> edges, IEnumerable<string> inputs, IEnumerable<string> outputs, bool selectedReachable, AffineComponentAnalysis? affine = null, IEnumerable<string>? blockedEdges = null)
    {
        ShaftIds = Ordered(shafts); BodyIds = Ordered(bodies); PortIds = Ordered(ports); EdgeKeys = Ordered(edges); PrescribedInputIds = Ordered(inputs); OutputKeys = Ordered(outputs);
        IsSelectedInputReachable = selectedReachable; Affine = affine; ComponentKey = HashText(Pack(ShaftIds.ToArray())); BlockedEdgeKeys = Ordered(blockedEdges ?? Array.Empty<string>());
    }
    public string ComponentKey { get; }
    public ReadOnlyCollection<string> ShaftIds { get; }
    public ReadOnlyCollection<string> BodyIds { get; }
    public ReadOnlyCollection<string> PortIds { get; }
    public ReadOnlyCollection<string> EdgeKeys { get; }
    public ReadOnlyCollection<string> BlockedEdgeKeys { get; }
    public ReadOnlyCollection<string> PrescribedInputIds { get; }
    public ReadOnlyCollection<string> OutputKeys { get; }
    public bool IsSelectedInputReachable { get; }
    public AffineComponentAnalysis? Affine { get; }
    private static ReadOnlyCollection<string> Ordered(IEnumerable<string> items) => items.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
}

public sealed class MechanicalOutputAnalysis
{
    internal MechanicalOutputAnalysis(MechanicalOutput binding, bool declaredReachable, bool admittedReachable, MechanicalDeterminacy status,
        ExactAffineRelation? shaft, ExactAffineRelation? port, ExactVector3? shaftAxis, OrientedFrame? portFrame,
        IEnumerable<string> path, IEnumerable<string> declaredPath, IEnumerable<string> blocked, MechanicalAxisVerdict target, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Binding = binding; IsDeclaredReachable = declaredReachable; IsAdmittedReachable = admittedReachable; Determinacy = status;
        ShaftRelation = shaft; PortRelation = port; ShaftPositiveAxis = shaftAxis; PortFrame = portFrame; ConstraintPath = path.ToList().AsReadOnly();
        DeclaredConstraintPath = declaredPath.ToList().AsReadOnly();
        BlockedPrerequisites = blocked.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly(); Target = target; Diagnostics = diagnostics.ToList().AsReadOnly();
    }
    public MechanicalOutput Binding { get; }
    public string OutputKey => Binding.Key;
    public bool IsDeclaredReachable { get; }
    public bool IsAdmittedReachable { get; }
    public MechanicalDeterminacy Determinacy { get; }
    public ExactAffineRelation? ShaftRelation { get; }
    public ExactAffineRelation? PortRelation { get; }
    public ExactVector3? ShaftPositiveAxis { get; }
    public OrientedFrame? PortFrame { get; }
    public ReadOnlyCollection<string> ConstraintPath { get; }
    /// <summary>Structural path only; invalid/excluded edges mean no affine coefficient can be asserted for this path.</summary>
    public ReadOnlyCollection<string> DeclaredConstraintPath { get; }
    public ReadOnlyCollection<string> BlockedPrerequisites { get; }
    public MechanicalAxisVerdict Target { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool HasDeterminedMotion => Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput && ShaftRelation.HasValue && PortRelation.HasValue;
}

public sealed class MechanicalAnalysis
{
    internal MechanicalAnalysis(MechanicalDraft draft, IEnumerable<MechanicalAnalyzedEdge> edges,
        IEnumerable<MechanicalConnectivityComponent> declared, IEnumerable<MechanicalConnectivityComponent> admitted,
        IEnumerable<MechanicalOutputAnalysis> outputs, IEnumerable<OrientedDomainCheck> geometryChecks,
        IEnumerable<PitchPairProof> proofs, IEnumerable<MechanicalDiagnostic> diagnostics,
        MechanicalAxisVerdict referenceIntegrity, MechanicalAxisVerdict declaredConnectivity, MechanicalAxisVerdict constraintAdmission,
        MechanicalAxisVerdict geometry, MechanicalAxisVerdict targets)
    {
        Draft = draft; Edges = edges.OrderBy(e => e.ConstraintKey, StringComparer.Ordinal).ToList().AsReadOnly();
        DeclaredComponents = declared.ToList().AsReadOnly(); AdmittedComponents = admitted.ToList().AsReadOnly(); Outputs = outputs.OrderBy(o => o.OutputKey, StringComparer.Ordinal).ToList().AsReadOnly();
        GeometryChecks = geometryChecks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        PitchProofs = proofs.OrderBy(p => p.PairId, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(d => d.Stage, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => string.Join("|", d.Related.Select(r => r.Key)), StringComparer.Ordinal).ToList().AsReadOnly();
        ReferenceIntegrity = referenceIntegrity; DeclaredConnectivity = declaredConnectivity; ConstraintAdmission = constraintAdmission; Geometry = geometry; Targets = targets;
        AnalysisId = HashText(Pack(draft.DraftId, Policy));
    }
    public MechanicalDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string DraftId => Draft.DraftId;
    public long Revision => Draft.Revision;
    public string AnalysisId { get; }
    public string Policy => MechanicalAuthoringProfile.AnalysisPolicy;
    public string MotionDomain => MechanicalAuthoringProfile.MotionDomain;
    public string? SelectedInputId => Draft.Definition.RootShaftId;
    public ReadOnlyCollection<MechanicalAnalyzedEdge> Edges { get; }
    public ReadOnlyCollection<MechanicalConnectivityComponent> DeclaredComponents { get; }
    public ReadOnlyCollection<MechanicalConnectivityComponent> AdmittedComponents { get; }
    public ReadOnlyCollection<MechanicalOutputAnalysis> Outputs { get; }
    public ReadOnlyCollection<OrientedDomainCheck> GeometryChecks { get; }
    public ReadOnlyCollection<PitchPairProof> PitchProofs { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public MechanicalAxisVerdict ReferenceIntegrity { get; }
    public MechanicalAxisVerdict DeclaredConnectivity { get; }
    public MechanicalAxisVerdict ConstraintAdmission { get; }
    public MechanicalAxisVerdict Geometry { get; }
    public MechanicalAxisVerdict Targets { get; }
    /// <summary>Not profile approval. Finalization must independently validate and reconstruct the existing export profile.</summary>
    public bool IsMechanicallyValid => SelectedInputId is not null && Draft.Definition.Shafts.Any(s => s.Id == SelectedInputId && s.IsPrescribed) &&
        ReferenceIntegrity == MechanicalAxisVerdict.Pass && ConstraintAdmission == MechanicalAxisVerdict.Pass && Geometry == MechanicalAxisVerdict.Pass && Targets == MechanicalAxisVerdict.Pass &&
        AdmittedComponents.All(c => c.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput) && Outputs.All(o => o.HasDeterminedMotion);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}
