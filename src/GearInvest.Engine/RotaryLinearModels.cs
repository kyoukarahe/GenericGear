using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class RotaryLinearProfile
{
    public const string Id = "cardinal-grounded-lead-screw-v1";
    public const string MotionDomain = "RotaryLinearContinuousAffine";
    public const string AnalysisPolicy = "exact-dimensioned-grounded-lead-screw-v1";
    public const string StandaloneSource = "single-prescribed-screw-shaft-v1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static void Quantity(ExactQuantity q) => MechanicalAuthoringProfile.Number(q.Value);
    internal static void Interval(ExactQuantityInterval interval)
    { if (interval is null) throw new ArgumentNullException(nameof(interval)); Quantity(interval.Lower); Quantity(interval.Upper); }
    internal static string Q(ExactQuantity q) => Pack(q.Kind.ToString(), q.Unit, F(q.Value));
    internal static string I(ExactQuantityInterval i) => Pack(Q(i.Lower), Q(i.Upper));
    internal static string Flag(bool value) => value ? "1" : "0";
}

/// <summary>One explicit scale for all source geometry; translation is canonical mm and basis is dimensionless.</summary>
public sealed class SourceLengthMapping
{
    public SourceLengthMapping(Rational millimetersPerSourceUnit, OrientedFrame poseMm)
    {
        MechanicalAuthoringProfile.Number(millimetersPerSourceUnit); MechanicalAuthoringProfile.FrameBound(poseMm);
        MillimetersPerSourceUnit = millimetersPerSourceUnit; PoseMm = poseMm;
    }
    public Rational MillimetersPerSourceUnit { get; }
    public OrientedFrame PoseMm { get; }
    public ExactVector3 PointMm(ExactVector3 local)
    { var point = PoseMm.Point(local * MillimetersPerSourceUnit); foreach (var v in new[] { point.X, point.Y, point.Z }) MechanicalDerivedNumbers.Check(v); return point; }
    public ExactVector3 Direction(ExactVector3 local) => PoseMm.Vector(local);
    public OrientedFrame FrameMm(OrientedFrame local) => new(PointMm(local.Origin), Direction(local.X), Direction(local.Y), Direction(local.Z));
    public string CanonicalRepresentation => Pack(F(MillimetersPerSourceUnit), Frame(PoseMm));
}

/// <summary>One real screw body mounted to a retained rotary shaft, one distinct guided nut and one prismatic coordinate.</summary>
public sealed class GroundedLeadScrewDefinition
{
    public GroundedLeadScrewDefinition(string id, string screwShaftId, string screwBodyId, string nutBodyId,
        string guideId, string linearDofId, ExactVector3 physicalAxis, ExactVector3 screwAxialDatumMm,
        OrientedFrame guideFrameMm, ExactQuantity lead, ExactQuantity screwReferenceTurns,
        ExactQuantity nutReferencePosition, ExactQuantityInterval guideInterval, ExactQuantityInterval engagementInterval,
        int handedness = 1, bool transmissionPresent = true, bool screwAxiallyFixed = true, bool guidePresent = true,
        bool nutRotationFixed = true, bool transverseMotionFixed = true, string? screwPortId = null)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); ScrewShaftId = MechanicalAuthoringProfile.IdValue(screwShaftId);
        ScrewBodyId = MechanicalAuthoringProfile.IdValue(screwBodyId); NutBodyId = MechanicalAuthoringProfile.IdValue(nutBodyId);
        GuideId = MechanicalAuthoringProfile.IdValue(guideId); LinearDofId = MechanicalAuthoringProfile.IdValue(linearDofId);
        ScrewPortId = screwPortId is null ? null : MechanicalAuthoringProfile.IdValue(screwPortId);
        MechanicalAuthoringProfile.VectorBound(physicalAxis); MechanicalAuthoringProfile.VectorBound(screwAxialDatumMm);
        MechanicalAuthoringProfile.FrameBound(guideFrameMm);
        foreach (var q in new[] { lead, screwReferenceTurns, nutReferencePosition }) RotaryLinearProfile.Quantity(q);
        RotaryLinearProfile.Interval(guideInterval); RotaryLinearProfile.Interval(engagementInterval);
        PhysicalAxis = physicalAxis; ScrewAxialDatumMm = screwAxialDatumMm; GuideFrameMm = guideFrameMm;
        Lead = lead; ScrewReferenceTurns = screwReferenceTurns; NutReferencePosition = nutReferencePosition;
        GuideInterval = guideInterval; EngagementInterval = engagementInterval; Handedness = handedness;
        TransmissionPresent = transmissionPresent; ScrewAxiallyFixed = screwAxiallyFixed; GuidePresent = guidePresent;
        NutRotationFixed = nutRotationFixed; TransverseMotionFixed = transverseMotionFixed;
    }
    public string Id { get; }
    public string ScrewShaftId { get; }
    public string ScrewBodyId { get; }
    public string NutBodyId { get; }
    public string GuideId { get; }
    public string LinearDofId { get; }
    public string? ScrewPortId { get; }
    public ExactVector3 PhysicalAxis { get; }
    public ExactVector3 ScrewAxialDatumMm { get; }
    /// <summary>Origin is mm; Z is increasing prismatic position, X/Y fix the nonrotating nut orientation.</summary>
    public OrientedFrame GuideFrameMm { get; }
    public ExactQuantity Lead { get; }
    public ExactQuantity ScrewReferenceTurns { get; }
    public ExactQuantity NutReferencePosition { get; }
    public ExactQuantityInterval GuideInterval { get; }
    /// <summary>Finite reference-point engagement interval in the same guide coordinate, not full solid engagement.</summary>
    public ExactQuantityInterval EngagementInterval { get; }
    public int Handedness { get; }
    public bool TransmissionPresent { get; }
    public bool ScrewAxiallyFixed { get; }
    public bool GuidePresent { get; }
    public bool NutRotationFixed { get; }
    public bool TransverseMotionFixed { get; }
    public string CanonicalRepresentation => Pack(Id, ScrewShaftId, ScrewBodyId, NutBodyId, GuideId, LinearDofId, ScrewPortId ?? "",
        V(PhysicalAxis), V(ScrewAxialDatumMm), Frame(GuideFrameMm), RotaryLinearProfile.Q(Lead), RotaryLinearProfile.Q(ScrewReferenceTurns),
        RotaryLinearProfile.Q(NutReferencePosition), RotaryLinearProfile.I(GuideInterval), RotaryLinearProfile.I(EngagementInterval), N(Handedness),
        RotaryLinearProfile.Flag(TransmissionPresent), RotaryLinearProfile.Flag(ScrewAxiallyFixed), RotaryLinearProfile.Flag(GuidePresent),
        RotaryLinearProfile.Flag(NutRotationFixed), RotaryLinearProfile.Flag(TransverseMotionFixed));
}

public sealed class LinearOutputDefinition
{
    public LinearOutputDefinition(string key, string linearDofId, string nutBodyId, string referencePointId,
        int terminalSign, ExactQuantity terminalDatum)
    {
        Key = MechanicalAuthoringProfile.IdValue(key); LinearDofId = MechanicalAuthoringProfile.IdValue(linearDofId);
        NutBodyId = MechanicalAuthoringProfile.IdValue(nutBodyId); ReferencePointId = MechanicalAuthoringProfile.IdValue(referencePointId);
        RotaryLinearProfile.Quantity(terminalDatum); TerminalSign = terminalSign; TerminalDatum = terminalDatum;
    }
    public string Key { get; }
    public string LinearDofId { get; }
    public string NutBodyId { get; }
    public string ReferencePointId { get; }
    public int TerminalSign { get; }
    public ExactQuantity TerminalDatum { get; }
    public QuantityKind Kind => QuantityKind.LinearPosition;
    public string CanonicalRepresentation => Pack(Key, LinearDofId, NutBodyId, ReferencePointId, N(TerminalSign), RotaryLinearProfile.Q(TerminalDatum));
}

public sealed class LinearOutputRequirement
{
    public LinearOutputRequirement(ExactQuantity? requiredGain, ExactQuantity? requiredReferencePosition, ExactQuantityInterval requiredRootInterval)
    {
        if (requiredGain.HasValue) RotaryLinearProfile.Quantity(requiredGain.Value);
        if (requiredReferencePosition.HasValue) RotaryLinearProfile.Quantity(requiredReferencePosition.Value);
        RotaryLinearProfile.Interval(requiredRootInterval);
        RequiredGain = requiredGain; RequiredReferencePosition = requiredReferencePosition; RequiredRootInterval = requiredRootInterval;
    }
    public ExactQuantity? RequiredGain { get; }
    /// <summary>Required TERMINAL position at global input zero, not a hidden drive or nut calibration.</summary>
    public ExactQuantity? RequiredReferencePosition { get; }
    public ExactQuantityInterval RequiredRootInterval { get; }
    public string CanonicalRepresentation => Pack(RequiredGain.HasValue ? RotaryLinearProfile.Q(RequiredGain.Value) : "",
        RequiredReferencePosition.HasValue ? RotaryLinearProfile.Q(RequiredReferencePosition.Value) : "", RotaryLinearProfile.I(RequiredRootInterval));
}

/// <summary>Complete retained source plus one dimensioned device; no solved coefficients or adjusted requirements are accepted.</summary>
public sealed class RotaryLinearDefinition
{
    public RotaryLinearDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, GroundedLeadScrewDefinition device,
        LinearOutputDefinition output, LinearOutputRequirement requirement, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 16);
        if (source.Definition.Shafts.Any(s => s.Id == device.LinearDofId)) throw new ArgumentException("Rotary and linear DOF stable IDs must be distinct.");
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; }
    public SourceLengthMapping? SourceMapping { get; }
    public GroundedLeadScrewDefinition Device { get; }
    public LinearOutputDefinition Output { get; }
    public LinearOutputRequirement Requirement { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(RotaryLinearProfile.Id, Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "",
        Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation, Pack(RequiredValidationDomains.ToArray()));
}

public sealed class RotaryLinearDraft
{
    public RotaryLinearDraft(RotaryLinearDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public RotaryLinearDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public RotaryLinearDraft WithDefinition(RotaryLinearDefinition definition) => new(definition, checked(Revision + 1));
}
