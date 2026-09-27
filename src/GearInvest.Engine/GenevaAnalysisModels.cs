using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum GenevaLengthOrder { Less, Equal, Greater, Unresolved }
public sealed class GenevaLengthComparison
{
    internal GenevaLengthComparison(string subject, GenevaLength left, GenevaLength right, GenevaLengthOrder order,
        GenevaNumericStatus status, int work, int bits, GenevaInterval? difference, string detail)
    { Subject = subject; Left = left; Right = right; Order = order; Status = status; Work = work; PrecisionBits = bits; DifferenceMm = difference; Detail = detail; }
    public string Subject { get; } public GenevaLength Left { get; } public GenevaLength Right { get; } public GenevaLengthOrder Order { get; }
    public GenevaNumericStatus Status { get; } public int Work { get; } public int PrecisionBits { get; } public GenevaInterval? DifferenceMm { get; } public string Detail { get; }
    public string CanonicalRepresentation => Pack(Subject, Left.CanonicalRepresentation, Right.CanonicalRepresentation, Order.ToString(),
        Status.ToString(), N(Work), N(PrecisionBits), DifferenceMm?.CanonicalRepresentation ?? "", Detail);
}

/// <summary>Exact sufficient whole-patch certificate for a named bounded ideal mate, not a swept solid proof.</summary>
public sealed class GenevaIdealLockProof
{
    internal GenevaIdealLockProof(Rational distance, Rational radius, Rational halfWidth)
    {
        CenterDistanceMm = distance; RadiusMm = radius; PatchHalfWidthTurns = halfWidth;
        LowerWitnessTurns = -halfWidth / 2; UpperWitnessTurns = halfWidth / 2;
        EnvelopeSquaredMarginUpperMm2 = radius * radius - new Rational(11, 6) * distance * radius + distance * distance / 2;
        GenevaProfile.Derived(EnvelopeSquaredMarginUpperMm2);
    }
    public Rational CenterDistanceMm { get; } public Rational RadiusMm { get; } public Rational PatchHalfWidthTurns { get; }
    public Rational LowerWitnessTurns { get; } public Rational UpperWitnessTurns { get; }
    public int LowerNormalVelocitySign => 1; public int UpperNormalVelocitySign => -1;
    public Rational EnvelopeSquaredMarginUpperMm2 { get; }
    public string Rule => "finite-wheel-arc-full-driver-circle-bilateral-local-lock-v1";
    public string CanonicalRepresentation => Pack(Rule, F(CenterDistanceMm), F(RadiusMm), F(PatchHalfWidthTurns), F(LowerWitnessTurns),
        F(UpperWitnessTurns), N(LowerNormalVelocitySign), N(UpperNormalVelocitySign), F(EnvelopeSquaredMarginUpperMm2));
}

public sealed class GenevaCompatibilityResult
{
    internal GenevaCompatibilityResult(GenevaDraft draft, GenevaNumericRequest request, ConnectionCompatibilityVerdict verdict,
        bool driverMounting, bool pinSlot, bool arcLock, Rational? distance, OrientedFrame? mapped, Rational? gammaIn,
        Rational? epsilonIn, Rational? gammaOut, Rational? epsilonOut, GenevaIdealLockProof? proof,
        int numericWork, IEnumerable<GenevaLengthComparison> comparisons, IEnumerable<OrientedDomainCheck> checks,
        IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
        : this(draft.DraftId, draft.Definition.Device.Id, request, verdict, driverMounting, pinSlot, arcLock, distance, mapped,
            gammaIn, epsilonIn, gammaOut, epsilonOut, proof, numericWork, comparisons, checks, facts, diagnostics) { }

    internal GenevaCompatibilityResult(string draftId, string deviceId, GenevaNumericRequest request, ConnectionCompatibilityVerdict verdict,
        bool driverMounting, bool pinSlot, bool arcLock, Rational? distance, OrientedFrame? mapped, Rational? gammaIn,
        Rational? epsilonIn, Rational? gammaOut, Rational? epsilonOut, GenevaIdealLockProof? proof,
        int numericWork, IEnumerable<GenevaLengthComparison> comparisons, IEnumerable<OrientedDomainCheck> checks,
        IEnumerable<MechanicalExactFact> facts, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        DraftId = draftId; DeviceId = deviceId; Request = request; Verdict = verdict;
        HasValidDriverMounting = driverMounting; PinSlotGeometryAdmitted = pinSlot; LockGeometryAdmitted = arcLock;
        CenterDistanceMm = distance; MappedSourceFrameMm = mapped; GammaIn = gammaIn; EpsilonIn = epsilonIn; GammaOut = gammaOut; EpsilonOut = epsilonOut;
        LockProof = proof; NumericWork = numericWork; LengthComparisons = comparisons.ToList().AsReadOnly(); Checks = checks.ToList().AsReadOnly();
        Facts = facts.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly();
        ResultId = HashText(Pack(DraftId, GenevaProfile.AnalysisPolicy, request.CanonicalRepresentation));
    }
    public string DraftId { get; } public string DeviceId { get; } public string ResultId { get; } public GenevaNumericRequest Request { get; }
    public ConnectionCompatibilityVerdict Verdict { get; } public bool IsAdmitted => Verdict == ConnectionCompatibilityVerdict.CompatibleWithinProfile;
    public bool HasValidDriverMounting { get; } public bool PinSlotGeometryAdmitted { get; } public bool LockGeometryAdmitted { get; }
    public Rational? CenterDistanceMm { get; } public OrientedFrame? MappedSourceFrameMm { get; }
    public Rational? GammaIn { get; } public Rational? EpsilonIn { get; } public Rational? GammaOut { get; } public Rational? EpsilonOut { get; }
    public GenevaIdealLockProof? LockProof { get; } public ReadOnlyCollection<GenevaLengthComparison> LengthComparisons { get; }
    public int NumericWork { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; } public ReadOnlyCollection<MechanicalExactFact> Facts { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}

public enum GenevaRegime { DwellBefore, LowerPhaseBoundary, Indexing, UpperPhaseBoundary, DwellAfter }

/// <summary>Conditional pin-slot angular construction; full-cycle admission remains an independent analysis result.</summary>
public sealed class GenevaMotionDescriptor
{
    private readonly GenevaDefinition? standaloneDefinition;
    internal GenevaMotionDescriptor(GenevaDefinition definition, GenevaCompatibilityResult local, ExactAffineRelation source)
        : this(definition.DefinitionId, definition.Device, definition.Output, local, source) { standaloneDefinition = definition; }

    internal GenevaMotionDescriptor(string definitionId, GenevaDeviceDefinition device, GenevaOutputDefinition output,
        GenevaCompatibilityResult local, ExactAffineRelation source)
    {
        DefinitionIdentity = definitionId; Device = device; Output = output; SourceRelation = source; CenterDistanceMm = local.CenterDistanceMm!.Value;
        EpsilonIn = local.EpsilonIn!.Value; EpsilonOut = local.EpsilonOut!.Value; GammaIn = local.GammaIn!.Value; GammaOut = local.GammaOut!.Value;
        PhysicalPhase = new(source.Coefficient * EpsilonIn, GammaIn + EpsilonIn * source.Phase);
        GenevaProfile.Derived(PhysicalPhase.Coefficient, PhysicalPhase.Phase);
        DescriptorId = HashText(CanonicalRepresentation);
    }
    public GenevaDefinition Definition => standaloneDefinition ?? throw new InvalidOperationException("Assembly motion owns a source-neutral declaration; use Device, Output and DefinitionIdentity.");
    public string DefinitionIdentity { get; }
    public GenevaDeviceDefinition Device { get; } public GenevaOutputDefinition Output { get; }
    public ExactAffineRelation SourceRelation { get; } public ExactAffineRelation PhysicalPhase { get; }
    public Rational CenterDistanceMm { get; } public Rational GammaIn { get; } public Rational EpsilonIn { get; } public Rational GammaOut { get; } public Rational EpsilonOut { get; }
    public int SlotCount => Device.Wheel.SlotCount; public Rational HalfIndexTurns => new Rational(1, 4) - HalfStepTurns;
    public Rational HalfStepTurns => new(1, 2 * SlotCount); public Rational IndexFraction => 2 * HalfIndexTurns; public Rational DwellFraction => 1 - IndexFraction;
    public Rational AverageTerminalAdvancePerRoot => -Output.TerminalSign * EpsilonOut * PhysicalPhase.Coefficient / SlotCount;
    public string DescriptorId { get; }
    public string CanonicalRepresentation => Pack(GenevaProfile.AnalysisPolicy, DefinitionIdentity, F(SourceRelation.Coefficient), F(SourceRelation.Phase),
        F(GammaIn), F(EpsilonIn), F(GammaOut), F(EpsilonOut), F(CenterDistanceMm), F(HalfIndexTurns), F(HalfStepTurns));
    public GenevaPoseRecipe At(ExactQuantity root)
    {
        if (root.Kind != QuantityKind.AngularPosition) throw new ArgumentException("Geneva root is angular turns.");
        MechanicalDerivedNumbers.Check(root.Value);
        return new(this, root);
    }
}

/// <summary>Exact absolute-root recipe. BigInteger cycles are retained before any numerical projection.</summary>
public sealed class GenevaPoseRecipe
{
    internal GenevaPoseRecipe(GenevaMotionDescriptor motion, ExactQuantity root)
    {
        Motion = motion; RootTurns = root; SourceTurns = motion.SourceRelation.Evaluate(root.Value); PhysicalDriverTurns = motion.PhysicalPhase.Evaluate(root.Value);
        GenevaProfile.Derived(SourceTurns, PhysicalDriverTurns);
        CycleIndex = GenevaProfile.Floor(PhysicalDriverTurns + new Rational(1, 2));
        CenteredPhaseTurns = PhysicalDriverTurns - new Rational(CycleIndex);
        var a = motion.HalfIndexTurns; var b = motion.HalfStepTurns;
        Regime = CenteredPhaseTurns < -a ? GenevaRegime.DwellBefore : CenteredPhaseTurns == -a ? GenevaRegime.LowerPhaseBoundary :
            CenteredPhaseTurns > a ? GenevaRegime.DwellAfter : CenteredPhaseTurns == a ? GenevaRegime.UpperPhaseBoundary : GenevaRegime.Indexing;
        ExactResidualTurns = CenteredPhaseTurns <= -a ? b : CenteredPhaseTurns >= a ? -b : CenteredPhaseTurns.IsZero ? Rational.Zero : (Rational?)null;
        AccumulatedPhysicalTurns = new Rational(-CycleIndex, motion.SlotCount);
        AccumulatedShaftTurns = motion.Device.OutputReferenceTurns.Value + motion.EpsilonOut * AccumulatedPhysicalTurns;
        WheelMaterialStepTurns = motion.GammaOut + motion.EpsilonOut * motion.Device.OutputReferenceTurns.Value + AccumulatedPhysicalTurns;
        NominalSlotIndex = GenevaProfile.Mod(CycleIndex + motion.Device.RegistrationSlot, motion.SlotCount);
        SlotId = PinPhaseEngaged ? motion.Device.Wheel.SlotIds[NominalSlotIndex] : null;
        RecessIndex = LockPhaseEngaged ? GenevaProfile.Mod(CycleIndex + motion.Device.RegistrationSlot + (CenteredPhaseTurns <= -a ? -1 : 0), motion.SlotCount) : (int?)null;
        RecessId = RecessIndex.HasValue && RecessIndex.Value < motion.Device.IdealLock.RecessIds.Count ? motion.Device.IdealLock.RecessIds[RecessIndex.Value] : null;
        ExactShaftTurns = ExactResidualTurns.HasValue ? AccumulatedShaftTurns + motion.EpsilonOut * ExactResidualTurns.Value : (Rational?)null;
        ExactTerminalTurns = ExactShaftTurns.HasValue ? motion.Output.TerminalSign * ExactShaftTurns.Value + motion.Output.TerminalDatum.Value : (Rational?)null;
        GenevaProfile.Derived(CenteredPhaseTurns, AccumulatedPhysicalTurns, AccumulatedShaftTurns, WheelMaterialStepTurns);
        if (ExactTerminalTurns.HasValue) GenevaProfile.Derived(ExactShaftTurns!.Value, ExactTerminalTurns.Value);
        RecipeId = HashText(CanonicalRepresentation);
    }
    public GenevaMotionDescriptor Motion { get; } public ExactQuantity RootTurns { get; } public Rational SourceTurns { get; } public Rational PhysicalDriverTurns { get; }
    public BigInteger CycleIndex { get; } public Rational CenteredPhaseTurns { get; } public GenevaRegime Regime { get; }
    public Rational? ExactResidualTurns { get; } public Rational AccumulatedPhysicalTurns { get; } public Rational AccumulatedShaftTurns { get; }
    public Rational WheelMaterialStepTurns { get; } public Rational? ExactShaftTurns { get; } public Rational? ExactTerminalTurns { get; }
    public int NominalSlotIndex { get; } public string? SlotId { get; } public int? RecessIndex { get; } public string? RecessId { get; }
    public bool PinPhaseEngaged => Regime == GenevaRegime.Indexing || Regime == GenevaRegime.LowerPhaseBoundary || Regime == GenevaRegime.UpperPhaseBoundary;
    public bool LockPhaseEngaged => Regime != GenevaRegime.Indexing;
    public string RecipeId { get; } public string ResidualKind => ExactResidualTurns.HasValue ? "RationalTurns" : "PositiveDenominatorPrincipalAtanTurns";
    public string CanonicalRepresentation => Pack(Motion.DescriptorId, GenevaProfile.Q(RootTurns), F(SourceTurns), F(PhysicalDriverTurns),
        CycleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), F(CenteredPhaseTurns), Regime.ToString(),
        F(AccumulatedPhysicalTurns), F(AccumulatedShaftTurns), ResidualKind, ExactResidualTurns.HasValue ? F(ExactResidualTurns.Value) : "",
        N(NominalSlotIndex), SlotId ?? "", RecessIndex.HasValue ? N(RecessIndex.Value) : "", RecessId ?? "", F(WheelMaterialStepTurns));
}

public sealed class GenevaAnalysis
{
    internal GenevaAnalysis(GenevaDraft draft, MechanicalAnalysis source, GenevaCompatibilityResult local, ExactAffineRelation? retained,
        GenevaMotionDescriptor? descriptor, MechanicalDeterminacy determinacy, MechanicalAxisVerdict target, GenevaNumericComputation? referenceNumeric,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        Draft = draft; SourceAnalysis = source; LocalCompatibility = local; SourceRelation = retained; Descriptor = descriptor;
        Determinacy = determinacy; Target = target; ReferenceNumeric = referenceNumeric;
        Checks = checks.OrderBy(c => c.Domain, StringComparer.Ordinal).ThenBy(c => c.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(c => c.Stage, StringComparer.Ordinal).ThenBy(c => c.Code, StringComparer.Ordinal).ToList().AsReadOnly();
        AnalysisId = HashText(Pack(DraftId, Policy, local.Request.CanonicalRepresentation));
    }
    public GenevaDraft Draft { get; } public string DefinitionId => Draft.DefinitionId; public string DraftId => Draft.DraftId; public long Revision => Draft.Revision;
    public string AnalysisId { get; } public string Policy => GenevaProfile.AnalysisPolicy; public string MotionDomain => GenevaProfile.MotionDomain;
    public string? SelectedInputId => SourceAnalysis.SelectedInputId;
    public MechanicalAnalysis SourceAnalysis { get; } public GenevaCompatibilityResult LocalCompatibility { get; } public GenevaCompatibilityResult Local => LocalCompatibility;
    public ExactAffineRelation? SourceRelation { get; } public GenevaMotionDescriptor? Descriptor { get; } public MechanicalDeterminacy Determinacy { get; }
    public bool HasDeterminedDriverMotion => SourceRelation.HasValue && Local.HasValidDriverMounting;
    public bool HasFullCycleMotion => Descriptor is not null && Local.IsAdmitted && Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput;
    public bool HasDeterminedMotion => HasFullCycleMotion;
    public MechanicalAxisVerdict Target { get; } public MechanicalAxisVerdict Requirements => Target;
    public GenevaNumericComputation? ReferenceNumeric { get; }
    public int NumericWork => Local.NumericWork + (ReferenceNumeric?.Work ?? 0);
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; } public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
    public bool IsMechanicallyValid => SourceAnalysis.IsMechanicallyValid && HasFullCycleMotion && Target == MechanicalAxisVerdict.Pass &&
        Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
    public GenevaPoseRecipe? CreatePoseRecipe(ExactQuantity root) => Descriptor?.At(root);
}

public sealed class GenevaDriverObservation
{
    internal GenevaDriverObservation(string bodyId, ExactVector3 center, ExactVector3 axis, ExactVector3 zeroRay, Rational sourceTurns, Rational? physicalPhase)
    { BodyId = bodyId; CenterMm = center; PositiveAxis = axis; ZeroRay = zeroRay; SourceTurns = sourceTurns; PhysicalPhaseTurns = physicalPhase; }
    public string BodyId { get; } public ExactVector3 CenterMm { get; } public ExactVector3 PositiveAxis { get; } public ExactVector3 ZeroRay { get; }
    public Rational SourceTurns { get; } public Rational? PhysicalPhaseTurns { get; }
}
public enum GenevaEvaluationStatus { Success, UndeterminedIntermittentOutput, InvalidDefinition, DimensionMismatch, GeometryInconclusive, IncompleteNumericBudget, NumericResourceLimit, InvalidNumericRequest }
public sealed class GenevaPose
{
    internal GenevaPose(GenevaPoseRecipe recipe, GenevaNumericPose numeric) { Recipe = recipe; Numeric = numeric; }
    public GenevaPoseRecipe Recipe { get; } public GenevaNumericPose Numeric { get; }
}
public sealed class GenevaEvaluation
{
    internal GenevaEvaluation(GenevaAnalysis analysis, ExactQuantity root, GenevaNumericRequest request, GenevaEvaluationStatus status,
        IEnumerable<OrientedShaftEvaluation> source, GenevaDriverObservation? driver, GenevaPoseRecipe? recipe,
        GenevaNumericComputation? numeric, GenevaDriverNumericComputation? driverNumeric, IEnumerable<MechanicalDiagnostic> diagnostics)
    {
        AnalysisId = analysis.AnalysisId; DefinitionId = analysis.DefinitionId; RootTurns = root; Request = request; Status = status;
        SourceShafts = source.ToList().AsReadOnly(); Driver = driver; Recipe = recipe; Numeric = numeric; DriverNumeric = driverNumeric;
        NormalPose = status == GenevaEvaluationStatus.Success && recipe is not null && numeric?.Pose is not null ? new(recipe, numeric.Pose) : null;
        DiagnosticPose = NormalPose is null ? numeric?.DiagnosticPose ?? numeric?.Pose : null;
        Diagnostics = diagnostics.ToList().AsReadOnly();
        ResultId = HashText(Pack(AnalysisId, GenevaProfile.Q(root), request.CanonicalRepresentation, status.ToString(),
            recipe?.RecipeId ?? "", numeric?.CanonicalRepresentation ?? "", driverNumeric?.CanonicalRepresentation ?? ""));
    }
    public string AnalysisId { get; } public string DefinitionId { get; } public string ResultId { get; } public ExactQuantity RootTurns { get; }
    public GenevaNumericRequest Request { get; } public GenevaEvaluationStatus Status { get; } public bool HasNormalPose => NormalPose is not null;
    public ReadOnlyCollection<OrientedShaftEvaluation> SourceShafts { get; } public GenevaDriverObservation? Driver { get; }
    public GenevaPoseRecipe? Recipe { get; } public GenevaNumericComputation? Numeric { get; } public GenevaDriverNumericComputation? DriverNumeric { get; }
    public int NumericWork => (Numeric?.Work ?? 0) + (DriverNumeric?.Work ?? 0);
    public GenevaPose? NormalPose { get; } public GenevaNumericPose? DiagnosticPose { get; }
    public ReadOnlyCollection<MechanicalDiagnostic> Diagnostics { get; }
}
