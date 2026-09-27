using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>An exact scalar witness of a named local identity, never a manufactured flank or force proof.</summary>
public sealed class WormDriveExactProof
{
    internal WormDriveExactProof(string id, string kind, Rational residual, bool passed, string detail)
    { MechanicalDerivedNumbers.Check(residual); Id = id; Kind = kind; Residual = residual; Passed = passed; Detail = detail; }
    public string Id { get; }
    public string Kind { get; }
    public Rational Residual { get; }
    public bool Passed { get; }
    public bool IsVerified => Passed;
    public string Detail { get; }
    public string CanonicalRepresentation => Pack(Id, Kind, F(Residual), WormDriveProfile.Flag(Passed), Detail);
}

/// <summary>Device-local exact pi coefficients and tangent-plane recipe. All velocities omit common 2*pi and input turn rate.</summary>
public sealed class WormDriveGeometryDescriptor
{
    internal WormDriveGeometryDescriptor(WormDriveTransmissionDefinition d, ExactVector3 inputCenter, ExactVector3 inputAxis)
    {
        var w = d.Worm; var g = d.SelectedWheel!; var n = d.PhysicalHelixAxis; var b = d.ContactSide;
        InputPitchCenterMm = inputCenter; OutputPitchCenterMm = d.OutputPitchCenterMm;
        WormPitchRadiusMm = w.PitchRadius.Value; WheelPitchRadiusMm = g.TransverseModule.Value * g.ToothCount / 2;
        AxialPitchPiCoefficientMm = w.AxialModule.Value; WheelPitchPiCoefficientMm = g.TransverseModule.Value;
        LeadPiCoefficientMm = w.Starts * w.AxialModule.Value; LeadSlope = LeadPiCoefficientMm / (2 * WormPitchRadiusMm);
        CenterDistanceMm = WormPitchRadiusMm + WheelPitchRadiusMm;
        PitchPointMm = inputCenter + b * WormPitchRadiusMm;
        var tw = n.Cross(b); var tg = d.OutputShaft.Frame.Z.Cross(b * -1);
        Epsilon = inputAxis.Dot(n); Sigma = tg.Dot(n);
        WormTrace = tw + n * (w.Handedness * LeadSlope);
        WheelTrace = d.OutputShaft.Frame.Z + tg * (g.Handedness * g.TraceSlope);
        PhaseCovector = n - tw * (w.Handedness * LeadSlope);
        WormMaterialVelocityReduced = inputAxis.Cross(b) * WormPitchRadiusMm;
        var wheelUnitVelocity = tg * WheelPitchRadiusMm;
        // Independent path one: axial motion of the rotating material helix's phase intersection.
        PhaseAdvanceTransfer = (-w.Handedness * Epsilon * LeadPiCoefficientMm) / (2 * WheelPitchRadiusMm * Sigma);
        // Independent path two: c dot (vw - q*vgUnit) = 0.
        CovectorTransfer = PhaseCovector.Dot(WormMaterialVelocityReduced) / PhaseCovector.Dot(wheelUnitVelocity);
        WheelMaterialVelocityReduced = wheelUnitVelocity * CovectorTransfer;
        RelativeMaterialVelocityReduced = WormMaterialVelocityReduced - WheelMaterialVelocityReduced;
        var proofs = new List<WormDriveExactProof>();
        void Zero(string id, Rational residual, string detail) => proofs.Add(new(id, "ExactZero", residual, residual == 0, detail));
        void VectorZero(string id, ExactVector3 residual, string detail)
        { Zero(id + ".x", residual.X, detail); Zero(id + ".y", residual.Y, detail); Zero(id + ".z", residual.Z, detail); }
        Zero("AxialTransversePitch", AxialPitchPiCoefficientMm - WheelPitchPiCoefficientMm, "pi cancels between axial and transverse circular pitch.");
        Zero("WheelCircularPitch", 2 * WheelPitchRadiusMm / g.ToothCount - g.TransverseModule.Value, "2*pi*Rg/Z = pi*mG.");
        Zero("LeadSlope", 2 * WormPitchRadiusMm * LeadSlope - LeadPiCoefficientMm, "L/(2*pi*Rw) = S*mW/(2*Rw).");
        VectorZero("PitchStationDisplacement", OutputPitchCenterMm - InputPitchCenterMm - b * CenterDistanceMm, "Declared stations share the positive common perpendicular of length Rw+Rg.");
        VectorZero("CommonPitchPoint", PitchPointMm - (OutputPitchCenterMm - b * WheelPitchRadiusMm), "The worm cylinder and wheel pitch circle have the same reference point.");
        VectorZero("LocalTraceParallelism", WormTrace.Cross(WheelTrace), "Parallel unoriented local pitch traces, not a conjugate tooth surface.");
        Zero("HelixTraceCovector", PhaseCovector.Dot(WormTrace), "The phase covector annihilates the helix trace.");
        Zero("IndependentTransferAgreement", PhaseAdvanceTransfer - CovectorTransfer, "Axial phase advance agrees with independently contracted material velocities.");
        Zero("CompatibleStartsToTeeth", CovectorTransfer - (-w.Handedness * Epsilon * Sigma * w.Starts / g.ToothCount), "The starts/teeth expression follows only after compatible modules.");
        Zero("RelativePhaseVelocity", PhaseCovector.Dot(RelativeMaterialVelocityReduced), "Only the pitch-phase covector residual vanishes; full velocities need not agree.");
        VectorZero("RelativeMotionAlongTrace", RelativeMaterialVelocityReduced.Cross(WormTrace), "Relative material sliding is along the local trace.");
        var slidingSquared = RelativeMaterialVelocityReduced.Dot(RelativeMaterialVelocityReduced);
        proofs.Add(new("NonzeroMaterialSliding", "StrictlyPositive", slidingSquared, slidingSquared > 0, "Nonzero input-normalized relative material velocity; no friction or loss estimate."));
        Proofs = proofs.OrderBy(p => p.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        foreach (var value in Scalars()) MechanicalDerivedNumbers.Check(value);
        foreach (var point in Vectors()) { MechanicalDerivedNumbers.Check(point.X); MechanicalDerivedNumbers.Check(point.Y); MechanicalDerivedNumbers.Check(point.Z); }
        GeometryId = HashText(CanonicalRepresentation);
    }
    public string GeometryId { get; }
    public string Semantics => WormDriveProfile.GeometrySemantics;
    public Rational AxialPitchPiCoefficientMm { get; }
    public Rational WheelPitchPiCoefficientMm { get; }
    public Rational LeadPiCoefficientMm { get; }
    public Rational LeadSlope { get; }
    public string LeadAngleRecipe => "atan(LeadSlope)";
    public Rational WormPitchRadiusMm { get; }
    public Rational WheelPitchRadiusMm { get; }
    public Rational CenterDistanceMm { get; }
    public Rational Epsilon { get; }
    public Rational Sigma { get; }
    public Rational PhaseAdvanceTransfer { get; }
    public Rational CovectorTransfer { get; }
    public ExactVector3 InputPitchCenterMm { get; }
    public ExactVector3 OutputPitchCenterMm { get; }
    public ExactVector3 PitchPointMm { get; }
    public ExactVector3 WormTrace { get; }
    public ExactVector3 WheelTrace { get; }
    public ExactVector3 PhaseCovector { get; }
    public ExactVector3 WormMaterialVelocityReduced { get; }
    public ExactVector3 WheelMaterialVelocityReduced { get; }
    public ExactVector3 RelativeMaterialVelocityReduced { get; }
    public ReadOnlyCollection<WormDriveExactProof> Proofs { get; }
    public bool HasExactProof => Proofs.All(p => p.Passed);
    private IEnumerable<Rational> Scalars() => new[] { AxialPitchPiCoefficientMm, WheelPitchPiCoefficientMm, LeadPiCoefficientMm, LeadSlope,
        WormPitchRadiusMm, WheelPitchRadiusMm, CenterDistanceMm, Epsilon, Sigma, PhaseAdvanceTransfer, CovectorTransfer };
    private IEnumerable<ExactVector3> Vectors() => new[] { InputPitchCenterMm, OutputPitchCenterMm, PitchPointMm, WormTrace, WheelTrace,
        PhaseCovector, WormMaterialVelocityReduced, WheelMaterialVelocityReduced, RelativeMaterialVelocityReduced };
    public string CanonicalRepresentation => Pack(Semantics, Pack(Scalars().Select(F).ToArray()), Pack(Vectors().Select(V).ToArray()),
        Pack(Proofs.Select(p => p.CanonicalRepresentation).ToArray()));
}

public static class WormDriveBuilders
{
    /// <summary>Proposes an independent specimen. This operation never selects it or edits a draft.</summary>
    public static IdealWormWheelSpecification CreateMatchingIdealWormWheel(CylindricalWormSpecification worm, int wheelToothCount)
    {
        if (worm is null) throw new ArgumentNullException(nameof(worm));
        if (worm.GeometryKind != WormDriveProfile.WormGeometry || worm.Parameterization != WormDriveProfile.AxialParameterization ||
            worm.Starts < WormDriveProfile.MinStarts || worm.Starts > WormDriveProfile.MaxStarts ||
            (worm.Handedness != -1 && worm.Handedness != 1) || worm.AxialModule.Kind != QuantityKind.LinearPosition ||
            worm.PitchRadius.Kind != QuantityKind.LinearPosition || worm.AxialModule.Value <= 0 || worm.PitchRadius.Value <= 0 ||
            wheelToothCount < WormDriveProfile.MinTeeth || wheelToothCount > WormDriveProfile.MaxTeeth || wheelToothCount <= worm.Starts)
            throw new ArgumentException("A supported positive axial-module worm and bounded wheel tooth count are required for a matching proposal.");
        return new(wheelToothCount, worm.AxialModule, worm.Handedness, worm.Starts * worm.AxialModule.Value / (2 * worm.PitchRadius.Value));
    }
    /// <summary>Returns a required output pitch-center proposal only. Shaft origin/station and terminal must be moved explicitly.</summary>
    public static ExactVector3 CreateCompatibleWormPlacement(WormDriveDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var c = d.Device; var g = c.SelectedWheel;
        var source = d.Source.Definition.Shafts.SingleOrDefault(s => s.Id == c.InputShaftId);
        if (source is null || !source.Frame.IsProperCardinal || d.SourceMapping is null || d.SourceMapping.MillimetersPerSourceUnit <= 0 ||
            !d.SourceMapping.PoseMm.IsProperCardinal || !c.ContactSide.IsCardinal || !c.PhysicalHelixAxis.IsCardinal ||
            !c.OutputShaft.Frame.IsProperCardinal || c.InputPitchStation.Kind != QuantityKind.LinearPosition ||
            c.Worm.PitchRadius.Kind != QuantityKind.LinearPosition || c.Worm.PitchRadius.Value <= 0 || g is null ||
            g.TransverseModule.Kind != QuantityKind.LinearPosition || g.TransverseModule.Value <= 0 ||
            g.ToothCount < WormDriveProfile.MinTeeth || g.ToothCount > WormDriveProfile.MaxTeeth)
            throw new ArgumentException("Valid declared shaft lines, contact side and positive pitch dimensions are required for a placement proposal.");
        var frame = d.SourceMapping.FrameMm(source.Frame); var n = c.PhysicalHelixAxis; var b = c.ContactSide; var ag = c.OutputShaft.Frame.Z;
        if (frame.Z.Cross(n) != ExactVector3.Zero || n.Dot(ag) != 0 || b.Dot(n) != 0 || b.Dot(ag) != 0)
            throw new ArgumentException("A placement proposal requires the supported orthogonal axis and perpendicular contact-side declarations.");
        var result = frame.Origin + frame.Z * c.InputPitchStation.Value + b * (c.Worm.PitchRadius.Value + g.TransverseModule.Value * g.ToothCount / 2);
        MechanicalDerivedNumbers.Check(result.X); MechanicalDerivedNumbers.Check(result.Y); MechanicalDerivedNumbers.Check(result.Z); return result;
    }
}
