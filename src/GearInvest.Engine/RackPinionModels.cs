using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class RackPinionProfile
{
    public const string Id = "cardinal-fixed-pinion-cp-rack-v1";
    public const string Parameterization = "circular-pitch";
    public const string NumericSemantics = "exact-rational-plus-inverse-pi-mm-v1";
    public const string MotionDomain = "RotaryLinearContinuousAffine";
    public const string AnalysisPolicy = "exact-circular-pitch-rack-pinion-v1";
    public const string StandaloneSource = "single-prescribed-rotary-shaft-v1";
    public const int MaxTeeth = 4096, MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Pi(ExactPiVector3 p) => Pack(V(p.RationalPartMm), V(p.InversePiCoefficientMm));
    internal static string PiFrame(ExactPiFrame f) => Pack(Pi(f.Origin), V(f.X), V(f.Y), V(f.Z));
    internal static void PiBound(ExactPiVector3 p) { MechanicalAuthoringProfile.VectorBound(p.RationalPartMm); MechanicalAuthoringProfile.VectorBound(p.InversePiCoefficientMm); }
    internal static void FrameBound(ExactPiFrame f)
    { if (f is null) throw new ArgumentNullException(nameof(f)); PiBound(f.Origin); MechanicalAuthoringProfile.VectorBound(f.X); MechanicalAuthoringProfile.VectorBound(f.Y); MechanicalAuthoringProfile.VectorBound(f.Z); }
}

/// <summary>A distinct CP pinion on a retained shaft, and a nonrotating rack. Explicit guide placement never follows tooth/pitch edits implicitly.</summary>
public sealed class CircularPitchRackDefinition
{
    public CircularPitchRackDefinition(string id, string pinionShaftId, string pinionBodyId, string rackBodyId,
        string guideId, string linearDofId, int pinionToothCount, ExactQuantity pinionCircularPitch, ExactQuantity rackPitch,
        ExactVector3 pinionCenterMm, ExactVector3 contactNormal, ExactPiFrame guideFrameMm, ExactQuantity longitudinalOffset,
        ExactQuantity pinionReferenceTurns, ExactQuantity rackReferencePosition,
        ExactQuantityInterval activeMaterialInterval, ExactQuantityInterval guideInterval,
        bool transmissionPresent = true, bool pinionCenterFixed = true, bool pinionAxisFixed = true, bool guidePresent = true,
        bool rackRotationFixed = true, bool transverseMotionFixed = true, string? pinionPortId = null,
        string parameterization = RackPinionProfile.Parameterization)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); PinionShaftId = MechanicalAuthoringProfile.IdValue(pinionShaftId);
        PinionBodyId = MechanicalAuthoringProfile.IdValue(pinionBodyId); RackBodyId = MechanicalAuthoringProfile.IdValue(rackBodyId);
        GuideId = MechanicalAuthoringProfile.IdValue(guideId); LinearDofId = MechanicalAuthoringProfile.IdValue(linearDofId);
        PinionPortId = pinionPortId is null ? null : MechanicalAuthoringProfile.IdValue(pinionPortId);
        Parameterization = MechanicalAuthoringProfile.IdValue(parameterization);
        foreach (var q in new[] { pinionCircularPitch, rackPitch, longitudinalOffset, pinionReferenceTurns, rackReferencePosition }) RotaryLinearProfile.Quantity(q);
        RotaryLinearProfile.Interval(activeMaterialInterval); RotaryLinearProfile.Interval(guideInterval);
        MechanicalAuthoringProfile.VectorBound(pinionCenterMm); MechanicalAuthoringProfile.VectorBound(contactNormal); RackPinionProfile.FrameBound(guideFrameMm);
        PinionToothCount = pinionToothCount; PinionCircularPitch = pinionCircularPitch; RackPitch = rackPitch;
        PinionCenterMm = pinionCenterMm; ContactNormal = contactNormal; GuideFrameMm = guideFrameMm; LongitudinalOffset = longitudinalOffset;
        PinionReferenceTurns = pinionReferenceTurns; RackReferencePosition = rackReferencePosition;
        ActiveMaterialInterval = activeMaterialInterval; GuideInterval = guideInterval;
        TransmissionPresent = transmissionPresent; PinionCenterFixed = pinionCenterFixed; PinionAxisFixed = pinionAxisFixed;
        GuidePresent = guidePresent; RackRotationFixed = rackRotationFixed; TransverseMotionFixed = transverseMotionFixed;
    }
    public string Id { get; }
    public string PinionShaftId { get; }
    public string PinionBodyId { get; }
    public string RackBodyId { get; }
    public string GuideId { get; }
    public string LinearDofId { get; }
    public string? PinionPortId { get; }
    public string Parameterization { get; }
    public int PinionToothCount { get; }
    public ExactQuantity PinionCircularPitch { get; }
    public ExactQuantity RackPitch { get; }
    public ExactVector3 PinionCenterMm { get; }
    public ExactVector3 ContactNormal { get; }
    public ExactPiFrame GuideFrameMm { get; }
    public ExactQuantity LongitudinalOffset { get; }
    public ExactQuantity PinionReferenceTurns { get; }
    public ExactQuantity RackReferencePosition { get; }
    public ExactQuantityInterval ActiveMaterialInterval { get; }
    public ExactQuantityInterval GuideInterval { get; }
    public bool TransmissionPresent { get; }
    public bool PinionCenterFixed { get; }
    public bool PinionAxisFixed { get; }
    public bool GuidePresent { get; }
    public bool RackRotationFixed { get; }
    public bool TransverseMotionFixed { get; }
    /// <summary>Algebraic derived descriptor, not independent input or evidence of admission for an invalid declaration.</summary>
    public ExactPiLength PitchRadius => ExactPiLength.FromCanonical(0, PinionToothCount * PinionCircularPitch.Value / 2);
    public ExactQuantity AdvancePerTurn => ExactQuantity.FromCanonical(QuantityKind.LinearPerAngular, PinionToothCount * PinionCircularPitch.Value);
    public string CanonicalRepresentation => Pack(Id, PinionShaftId, PinionBodyId, RackBodyId, GuideId, LinearDofId, PinionPortId ?? "", Parameterization,
        N(PinionToothCount), RotaryLinearProfile.Q(PinionCircularPitch), RotaryLinearProfile.Q(RackPitch), V(PinionCenterMm), V(ContactNormal), RackPinionProfile.PiFrame(GuideFrameMm),
        RotaryLinearProfile.Q(LongitudinalOffset), RotaryLinearProfile.Q(PinionReferenceTurns), RotaryLinearProfile.Q(RackReferencePosition),
        RotaryLinearProfile.I(ActiveMaterialInterval), RotaryLinearProfile.I(GuideInterval), RotaryLinearProfile.Flag(TransmissionPresent),
        RotaryLinearProfile.Flag(PinionCenterFixed), RotaryLinearProfile.Flag(PinionAxisFixed), RotaryLinearProfile.Flag(GuidePresent), RotaryLinearProfile.Flag(RackRotationFixed), RotaryLinearProfile.Flag(TransverseMotionFixed));
}

/// <summary>Neutral linear body/reference-point binding; the legacy 22A nut-named public/wire binding is retained unchanged.</summary>
public sealed class PrismaticOutputDefinition
{
    public PrismaticOutputDefinition(string key, string linearDofId, string bodyId, string referencePointId, int terminalSign, ExactQuantity terminalDatum)
    {
        Key = MechanicalAuthoringProfile.IdValue(key); LinearDofId = MechanicalAuthoringProfile.IdValue(linearDofId);
        BodyId = MechanicalAuthoringProfile.IdValue(bodyId); ReferencePointId = MechanicalAuthoringProfile.IdValue(referencePointId);
        RotaryLinearProfile.Quantity(terminalDatum); TerminalSign = terminalSign; TerminalDatum = terminalDatum;
    }
    public string Key { get; }
    public string LinearDofId { get; }
    public string BodyId { get; }
    public string ReferencePointId { get; }
    public int TerminalSign { get; }
    public ExactQuantity TerminalDatum { get; }
    public QuantityKind Kind => QuantityKind.LinearPosition;
    public string CanonicalRepresentation => Pack(Key, LinearDofId, BodyId, ReferencePointId, N(TerminalSign), RotaryLinearProfile.Q(TerminalDatum));
}

public sealed class RackPinionDefinition
{
    public RackPinionDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, CircularPitchRackDefinition device,
        PrismaticOutputDefinition output, LinearOutputRequirement requirement, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 24);
        if (source.Definition.Shafts.Any(s => s.Id == device.LinearDofId)) throw new ArgumentException("Rotary and linear DOF stable IDs must be distinct.");
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; }
    public SourceLengthMapping? SourceMapping { get; }
    public CircularPitchRackDefinition Device { get; }
    public PrismaticOutputDefinition Output { get; }
    public LinearOutputRequirement Requirement { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(RackPinionProfile.Id, RackPinionProfile.NumericSemantics, Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "",
        Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation, Pack(RequiredValidationDomains.ToArray()));
}

public sealed class RackPinionDraft
{
    public RackPinionDraft(RackPinionDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public RackPinionDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public RackPinionDraft WithDefinition(RackPinionDefinition definition) => new(definition, checked(Revision + 1));
}

public static class RackPinionGeometry
{
    /// <summary>Explicit convenience calculation. Applying the returned placement is a separate caller edit; no hidden tooth-dependent constraint is installed.</summary>
    public static ExactPiFrame CreateTangentRackGuide(ExactVector3 pinionCenterMm, ExactVector3 shaftPositiveAxis,
        ExactVector3 contactNormal, ExactVector3 guideDirection, int pinionToothCount, ExactQuantity circularPitch, ExactQuantity longitudinalOffset)
    {
        if (!shaftPositiveAxis.IsCardinal || !contactNormal.IsCardinal || !guideDirection.IsCardinal || shaftPositiveAxis.Dot(contactNormal) != 0 ||
            shaftPositiveAxis.Dot(guideDirection) != 0 || contactNormal.Dot(guideDirection) != 0)
            throw new ArgumentException("Mutually perpendicular cardinal shaft, side and guide directions required.");
        if (pinionToothCount <= 0 || pinionToothCount > RackPinionProfile.MaxTeeth || circularPitch.Kind != QuantityKind.LinearPosition || circularPitch.Value <= 0 || longitudinalOffset.Kind != QuantityKind.LinearPosition)
            throw new ArgumentException("Positive bounded CP tooth count, circular pitch mm and longitudinal offset mm required.");
        var origin = ExactPiVector3.FromCanonical(pinionCenterMm + guideDirection * longitudinalOffset.Value, contactNormal * (pinionToothCount * circularPitch.Value / 2));
        return new ExactPiFrame(origin, shaftPositiveAxis, guideDirection.Cross(shaftPositiveAxis), guideDirection);
    }
}
