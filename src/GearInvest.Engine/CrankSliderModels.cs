using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class CrankSliderProfile
{
    public const string Id = "cardinal-planar-full-cycle-crank-slider-v1";
    public const string MotionDomain = "RotaryToPrismaticNonlinear";
    public const string AnalysisPolicy = "crank-slider-length-closure-v1";
    public const string GeometrySemantics = AnalysisPolicy;
    public const string StandaloneSource = "single-prescribed-crank-shaft-v1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Q(ExactQuantity q) => RotaryLinearProfile.Q(q);
    internal static string I(ExactQuantityInterval i) => RotaryLinearProfile.I(i);
    internal static string Flag(bool value) => value ? "1" : "0";
}

/// <summary>Authored lengths and joints, never cached motion. Guide Z increases slider coordinate.</summary>
public sealed class PlanarCrankSliderDefinition
{
    public PlanarCrankSliderDefinition(string id, string sourceShaftId, string crankBodyId, string rodBodyId,
        string sliderBodyId, string guideId, string linearDofId, string crankPinId, string sliderPinId,
        ExactQuantity crankRadius, ExactQuantity rodLength, ExactVector3 pivotMm, ExactQuantity pivotStation,
        ExactVector3 planeNormal, OrientedFrame guideFrameMm, ExactQuantity mountingTurns, int? assemblyBranch,
        ExactQuantityInterval guideTravel, bool transmissionPresent = true, bool guidePresent = true,
        bool sliderRotationFixed = true, bool transverseMotionFixed = true, bool crankAxisFixed = true, string? sourcePortId = null,
        bool sliderIsPrescribed = false, bool crankPinPresent = true, bool sliderPinPresent = true)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); SourceShaftId = MechanicalAuthoringProfile.IdValue(sourceShaftId);
        CrankBodyId = MechanicalAuthoringProfile.IdValue(crankBodyId); RodBodyId = MechanicalAuthoringProfile.IdValue(rodBodyId);
        SliderBodyId = MechanicalAuthoringProfile.IdValue(sliderBodyId); GuideId = MechanicalAuthoringProfile.IdValue(guideId);
        LinearDofId = MechanicalAuthoringProfile.IdValue(linearDofId); CrankPinId = MechanicalAuthoringProfile.IdValue(crankPinId);
        SliderPinId = MechanicalAuthoringProfile.IdValue(sliderPinId); SourcePortId = sourcePortId is null ? null : MechanicalAuthoringProfile.IdValue(sourcePortId);
        foreach (var q in new[] { crankRadius, rodLength, pivotStation, mountingTurns }) RotaryLinearProfile.Quantity(q);
        MechanicalAuthoringProfile.VectorBound(pivotMm); MechanicalAuthoringProfile.VectorBound(planeNormal);
        MechanicalAuthoringProfile.FrameBound(guideFrameMm); RotaryLinearProfile.Interval(guideTravel);
        CrankRadius = crankRadius; RodLength = rodLength; PivotMm = pivotMm; PivotStation = pivotStation;
        PlaneNormal = planeNormal; GuideFrameMm = guideFrameMm; MountingTurns = mountingTurns;
        AssemblyBranch = assemblyBranch; GuideTravel = guideTravel; TransmissionPresent = transmissionPresent;
        GuidePresent = guidePresent; SliderRotationFixed = sliderRotationFixed; TransverseMotionFixed = transverseMotionFixed; CrankAxisFixed = crankAxisFixed;
        SliderIsPrescribed = sliderIsPrescribed; CrankPinPresent = crankPinPresent; SliderPinPresent = sliderPinPresent;
    }
    public string Id { get; } public string SourceShaftId { get; } public string? SourcePortId { get; }
    public string CrankBodyId { get; } public string RodBodyId { get; } public string SliderBodyId { get; }
    public string GuideId { get; } public string LinearDofId { get; } public string CrankPinId { get; } public string SliderPinId { get; }
    public ExactQuantity CrankRadius { get; } public ExactQuantity RodLength { get; }
    public ExactVector3 PivotMm { get; } public ExactQuantity PivotStation { get; } public ExactVector3 PlaneNormal { get; }
    public OrientedFrame GuideFrameMm { get; } public ExactQuantity MountingTurns { get; } public int? AssemblyBranch { get; }
    public ExactQuantityInterval GuideTravel { get; } public bool TransmissionPresent { get; } public bool GuidePresent { get; }
    public bool SliderRotationFixed { get; } public bool TransverseMotionFixed { get; } public bool CrankAxisFixed { get; }
    public bool SliderIsPrescribed { get; } public bool CrankPinPresent { get; } public bool SliderPinPresent { get; }
    public string CanonicalRepresentation => Pack(Id, SourceShaftId, SourcePortId ?? "", CrankBodyId, RodBodyId, SliderBodyId,
        GuideId, LinearDofId, CrankPinId, SliderPinId, CrankSliderProfile.Q(CrankRadius), CrankSliderProfile.Q(RodLength), V(PivotMm),
        CrankSliderProfile.Q(PivotStation), V(PlaneNormal), Frame(GuideFrameMm), CrankSliderProfile.Q(MountingTurns),
        AssemblyBranch?.ToString(CultureInfo.InvariantCulture) ?? "", CrankSliderProfile.I(GuideTravel),
        CrankSliderProfile.Flag(TransmissionPresent), CrankSliderProfile.Flag(GuidePresent), CrankSliderProfile.Flag(SliderRotationFixed),
        CrankSliderProfile.Flag(TransverseMotionFixed), CrankSliderProfile.Flag(CrankAxisFixed), CrankSliderProfile.Flag(SliderIsPrescribed),
        CrankSliderProfile.Flag(CrankPinPresent), CrankSliderProfile.Flag(SliderPinPresent));
}

public sealed class CrankSliderRequirement
{
    public CrankSliderRequirement(ExactQuantity? requiredStroke = null, ExactQuantity? requiredReferencePosition = null, ExactQuantity? referenceRoot = null)
    {
        ReferenceRoot = referenceRoot ?? ExactQuantity.Turns(0);
        foreach (var q in new[] { requiredStroke, requiredReferencePosition, ReferenceRoot }) if (q.HasValue) RotaryLinearProfile.Quantity(q.Value);
        RequiredStroke = requiredStroke; RequiredReferencePosition = requiredReferencePosition;
    }
    public ExactQuantity? RequiredStroke { get; }
    /// <summary>Terminal position at the explicit ReferenceRoot; never a slider driver or hidden calibration.</summary>
    public ExactQuantity? RequiredReferencePosition { get; }
    public ExactQuantity ReferenceRoot { get; }
    public string CanonicalRepresentation => Pack(RequiredStroke.HasValue ? CrankSliderProfile.Q(RequiredStroke.Value) : "",
        RequiredReferencePosition.HasValue ? CrankSliderProfile.Q(RequiredReferencePosition.Value) : "", CrankSliderProfile.Q(ReferenceRoot));
}

public sealed class CrankSliderDefinition
{
    public CrankSliderDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, PlanarCrankSliderDefinition device,
        PrismaticOutputDefinition output, CrankSliderRequirement requirement, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 24);
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; } public SourceLengthMapping? SourceMapping { get; }
    public PlanarCrankSliderDefinition Device { get; } public PrismaticOutputDefinition Output { get; }
    public CrankSliderRequirement Requirement { get; } public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(CrankSliderProfile.Id, Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "",
        Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation, Pack(RequiredValidationDomains.ToArray()));
}

public sealed class CrankSliderDraft
{
    public CrankSliderDraft(CrankSliderDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public CrankSliderDefinition Definition { get; } public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId; public string DraftId { get; }
    public CrankSliderDraft WithDefinition(CrankSliderDefinition definition) => new(definition, checked(Revision + 1));
}
