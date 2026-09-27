using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Layout;

public enum OrientedGearKind { PlanarSpur, RightAngleBevel }
public enum OrientedContactKind { ExternalSpur, RightAngleBevel }

public sealed class OrientedGearBody
{
    public OrientedGearBody(string id, string shaftId, OrientedGearKind kind, OrientedFrame mountingFrame,
        int teeth, Rational outerPitchRadius, string sourceModuleId)
    { Id = id; ShaftId = shaftId; Kind = kind; MountingFrame = mountingFrame; Teeth = teeth; OuterPitchRadius = outerPitchRadius; SourceModuleId = sourceModuleId; }
    public string Id { get; }
    public string ShaftId { get; }
    public OrientedGearKind Kind { get; }
    public OrientedFrame MountingFrame { get; }
    public int Teeth { get; }
    public Rational OuterPitchRadius { get; }
    public string SourceModuleId { get; }
}

/// <summary>Finite ideal pitch surfaces on one common generator interval; no tooth solids.</summary>
public sealed class RightAnglePitchCone
{
    public RightAnglePitchCone(ExactVector3 apex, ExactVector3 outwardA, ExactVector3 outwardB,
        ExactVector3 outerContact, Rational innerParameter, Rational outerScaleA, Rational outerScaleB)
    { Apex = apex; OutwardA = outwardA; OutwardB = outwardB; OuterContact = outerContact; InnerParameter = innerParameter; OuterScaleA = outerScaleA; OuterScaleB = outerScaleB; }
    public ExactVector3 Apex { get; }
    public ExactVector3 OutwardA { get; }
    public ExactVector3 OutwardB { get; }
    public ExactVector3 OuterContact { get; }
    public Rational InnerParameter { get; }
    public Rational OuterScaleA { get; }
    public Rational OuterScaleB { get; }
    public Rational ConeDistanceSquared => (OuterContact - Apex).LengthSquared;
    public ExactVector3 ContactAt(Rational parameter) => Apex + (OuterContact - Apex) * parameter;
}

public sealed class OrientedGearContact
{
    public OrientedGearContact(string id, OrientedContactKind kind, string bodyAId, string bodyBId,
        Rational storedTransfer, RightAnglePitchCone? cone = null)
    { Id = id; Kind = kind; BodyAId = bodyAId; BodyBId = bodyBId; StoredTransfer = storedTransfer; Cone = cone; }
    public string Id { get; }
    public OrientedContactKind Kind { get; }
    public string BodyAId { get; }
    public string BodyBId { get; }
    public Rational StoredTransfer { get; }
    public RightAnglePitchCone? Cone { get; }
}

public enum OrientedCheckVerdict { Pass, Fail, Inconclusive, NotPerformed }

public sealed class OrientedDomainCheck
{
    public OrientedDomainCheck(string domain, string subject, OrientedCheckVerdict verdict, bool required, string detail)
    { Domain = domain; Subject = subject; Verdict = verdict; Required = required; Detail = detail; }
    public string Domain { get; }
    public string Subject { get; }
    public OrientedCheckVerdict Verdict { get; }
    public bool Required { get; }
    public string Detail { get; }
}

public sealed class OrientedValidation
{
    public OrientedValidation(IEnumerable<OrientedDomainCheck> checks, IEnumerable<Diagnostic>? diagnostics = null,
        IEnumerable<PitchPairProof>? pitchProofs = null, string clearancePolicy = PitchClearancePolicy.Legacy)
    {
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics ?? Array.Empty<Diagnostic>());
        ClearancePolicy = clearancePolicy;
        PitchProofs = (pitchProofs ?? Array.Empty<PitchPairProof>()).OrderBy(p => p.PairId, StringComparer.Ordinal).ToList().AsReadOnly();
        if (PitchProofs.Count > PitchClearancePolicy.MaxPairs || PitchProofs.Select(p => p.PairId).Distinct(StringComparer.Ordinal).Count() != PitchProofs.Count)
            throw new ArgumentException("Bounded unique pair proof coverage required.");
    }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public string ClearancePolicy { get; }
    public ReadOnlyCollection<PitchPairProof> PitchProofs { get; }
    public bool IsValid => Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass) && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

public readonly struct ExactEnvelope3
{
    public ExactEnvelope3(ExactVector3 min, ExactVector3 max) { Min = min; Max = max; }
    public ExactVector3 Min { get; }
    public ExactVector3 Max { get; }
    public bool IsOrdered => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;
    public bool Disjoint(ExactEnvelope3 other) => Max.X < other.Min.X || other.Max.X < Min.X || Max.Y < other.Min.Y || other.Max.Y < Min.Y || Max.Z < other.Min.Z || other.Max.Z < Min.Z;
    public static ExactEnvelope3 Of(OrientedGearBody body, RightAnglePitchCone? cone)
    {
        var center = body.MountingFrame.Origin; var axis = body.MountingFrame.Z;
        var radius = new ExactVector3(axis.X.IsZero ? body.OuterPitchRadius : 0, axis.Y.IsZero ? body.OuterPitchRadius : 0, axis.Z.IsZero ? body.OuterPitchRadius : 0);
        var min = center - radius; var max = center + radius;
        if (cone is null) return new ExactEnvelope3(min, max);
        var innerCenter = cone.Apex + (center - cone.Apex) * cone.InnerParameter;
        var innerRadius = radius * cone.InnerParameter;
        return new ExactEnvelope3(Minimum(min, innerCenter - innerRadius), Maximum(max, innerCenter + innerRadius));
    }
    private static ExactVector3 Minimum(ExactVector3 a, ExactVector3 b) => new(a.X < b.X ? a.X : b.X, a.Y < b.Y ? a.Y : b.Y, a.Z < b.Z ? a.Z : b.Z);
    private static ExactVector3 Maximum(ExactVector3 a, ExactVector3 b) => new(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y, a.Z > b.Z ? a.Z : b.Z);
}
