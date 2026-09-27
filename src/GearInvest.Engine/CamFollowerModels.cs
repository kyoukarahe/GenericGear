using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class CamFollowerProfile
{
    public const string Id = "cardinal-convex-support-cam-flat-follower-v1";
    public const string MotionDomain = "RotaryToPrismaticPiecewiseNonlinear";
    public const string AnalysisPolicy = "convex-support-global-flat-contact-v1";
    public const string GeometrySemantics = AnalysisPolicy;
    public const string MaintainedContactIdeal = "MaintainedContactIdeal";
    public const string StandaloneSource = "single-prescribed-cam-shaft-v1";
    public const int MaxDocumentBytes = 16 * 1024 * 1024, MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    internal static string Q(ExactQuantity q) => RotaryLinearProfile.Q(q);
    internal static string I(ExactQuantityInterval value) => RotaryLinearProfile.I(value);
    internal static string Flag(bool value) => value ? "1" : "0";
    internal static string Pi(ExactPiLength value) => Pack(F(value.RationalPartMm), F(value.InversePiCoefficientMm));
    internal static string PiVector(ExactPiVector3 value) => Pack(V(value.RationalPartMm), V(value.InversePiCoefficientMm));
}

/// <summary>Authored support geometry and finite flat face. No cached lift or contact-point table.</summary>
public sealed class FlatCamFollowerDefinition
{
    public FlatCamFollowerDefinition(string id, string sourceShaftId, string camBodyId, string followerBodyId,
        string guideId, string linearDofId, string followerReferenceId, CamSupportProfile supportProfile,
        ExactVector3 centerMm, ExactQuantity camStation, ExactVector3 planeNormal, OrientedFrame guideFrameMm,
        ExactQuantity mountingTurns, ExactQuantityInterval guideTravel, ExactQuantityInterval followerFace,
        bool contactPresent = true, string? contactPolicy = CamFollowerProfile.MaintainedContactIdeal,
        bool guidePresent = true, bool followerRotationFixed = true, bool transverseMotionFixed = true,
        bool camAxisFixed = true, string? sourcePortId = null, bool followerIsPrescribed = false,
        string followerKind = "FlatTranslating")
    {
        Id = MechanicalAuthoringProfile.IdValue(id); SourceShaftId = MechanicalAuthoringProfile.IdValue(sourceShaftId);
        CamBodyId = MechanicalAuthoringProfile.IdValue(camBodyId); FollowerBodyId = MechanicalAuthoringProfile.IdValue(followerBodyId);
        GuideId = MechanicalAuthoringProfile.IdValue(guideId); LinearDofId = MechanicalAuthoringProfile.IdValue(linearDofId);
        FollowerReferenceId = MechanicalAuthoringProfile.IdValue(followerReferenceId);
        SourcePortId = sourcePortId is null ? null : MechanicalAuthoringProfile.IdValue(sourcePortId);
        SupportProfile = supportProfile ?? throw new ArgumentNullException(nameof(supportProfile));
        MechanicalAuthoringProfile.VectorBound(centerMm); MechanicalAuthoringProfile.VectorBound(planeNormal);
        MechanicalAuthoringProfile.FrameBound(guideFrameMm); RotaryLinearProfile.Quantity(camStation); RotaryLinearProfile.Quantity(mountingTurns);
        RotaryLinearProfile.Interval(guideTravel); RotaryLinearProfile.Interval(followerFace);
        CenterMm = centerMm; CamStation = camStation; PlaneNormal = planeNormal; GuideFrameMm = guideFrameMm;
        MountingTurns = mountingTurns; GuideTravel = guideTravel; FollowerFace = followerFace; ContactPresent = contactPresent;
        ContactPolicy = contactPolicy is null ? null : MechanicalAuthoringProfile.IdValue(contactPolicy);
        GuidePresent = guidePresent; FollowerRotationFixed = followerRotationFixed; TransverseMotionFixed = transverseMotionFixed;
        CamAxisFixed = camAxisFixed; FollowerIsPrescribed = followerIsPrescribed; FollowerKind = MechanicalAuthoringProfile.IdValue(followerKind);
    }
    public string Id { get; } public string SourceShaftId { get; } public string? SourcePortId { get; }
    public string CamBodyId { get; } public string FollowerBodyId { get; } public string GuideId { get; }
    public string LinearDofId { get; } public string FollowerReferenceId { get; } public CamSupportProfile SupportProfile { get; }
    public ExactVector3 CenterMm { get; } public ExactQuantity CamStation { get; } public ExactVector3 PlaneNormal { get; }
    public OrientedFrame GuideFrameMm { get; } public ExactQuantity MountingTurns { get; }
    public ExactQuantityInterval GuideTravel { get; } public ExactQuantityInterval FollowerFace { get; }
    public bool ContactPresent { get; } public string? ContactPolicy { get; } public bool GuidePresent { get; }
    public bool FollowerRotationFixed { get; } public bool TransverseMotionFixed { get; } public bool CamAxisFixed { get; }
    public bool FollowerIsPrescribed { get; } public string FollowerKind { get; }
    public string CanonicalRepresentation => Pack(Id, SourceShaftId, SourcePortId ?? "", CamBodyId, FollowerBodyId,
        GuideId, LinearDofId, FollowerReferenceId, SupportProfile.CanonicalRepresentation, V(CenterMm), CamFollowerProfile.Q(CamStation),
        V(PlaneNormal), Frame(GuideFrameMm), CamFollowerProfile.Q(MountingTurns), CamFollowerProfile.I(GuideTravel),
        CamFollowerProfile.I(FollowerFace), CamFollowerProfile.Flag(ContactPresent), ContactPolicy ?? "",
        CamFollowerProfile.Flag(GuidePresent), CamFollowerProfile.Flag(FollowerRotationFixed), CamFollowerProfile.Flag(TransverseMotionFixed),
        CamFollowerProfile.Flag(CamAxisFixed), CamFollowerProfile.Flag(FollowerIsPrescribed), FollowerKind);
}

public sealed class CamFollowerRequirement
{
    public CamFollowerRequirement(ExactQuantity? requiredStroke = null, ExactQuantity? requiredReferencePosition = null,
        ExactQuantity? referenceRoot = null, bool positiveStrokeRequired = false)
    {
        ReferenceRoot = referenceRoot ?? ExactQuantity.Turns(0);
        foreach (var q in new[] { requiredStroke, requiredReferencePosition, ReferenceRoot }) if (q.HasValue) RotaryLinearProfile.Quantity(q.Value);
        RequiredStroke = requiredStroke; RequiredReferencePosition = requiredReferencePosition; PositiveStrokeRequired = positiveStrokeRequired;
    }
    public ExactQuantity? RequiredStroke { get; } public ExactQuantity? RequiredReferencePosition { get; }
    public ExactQuantity ReferenceRoot { get; } public bool PositiveStrokeRequired { get; }
    public string CanonicalRepresentation => Pack(RequiredStroke.HasValue ? CamFollowerProfile.Q(RequiredStroke.Value) : "",
        RequiredReferencePosition.HasValue ? CamFollowerProfile.Q(RequiredReferencePosition.Value) : "", CamFollowerProfile.Q(ReferenceRoot),
        CamFollowerProfile.Flag(PositiveStrokeRequired));
}

public sealed class CamFollowerDefinition
{
    public CamFollowerDefinition(MechanicalDraft source, SourceLengthMapping? sourceMapping, FlatCamFollowerDefinition device,
        PrismaticOutputDefinition output, CamFollowerRequirement requirement, IEnumerable<string>? requiredValidationDomains = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); SourceMapping = sourceMapping;
        Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), s => s, 24);
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Source { get; } public SourceLengthMapping? SourceMapping { get; }
    public FlatCamFollowerDefinition Device { get; } public PrismaticOutputDefinition Output { get; }
    public CamFollowerRequirement Requirement { get; } public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(CamFollowerProfile.Id, Source.DefinitionId, SourceMapping?.CanonicalRepresentation ?? "",
        Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation, Pack(RequiredValidationDomains.ToArray()));
}

public sealed class CamFollowerDraft
{
    public CamFollowerDraft(CamFollowerDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative."); Revision = revision;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Source.DraftId));
    }
    public CamFollowerDefinition Definition { get; } public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId; public string DraftId { get; }
    public CamFollowerDraft WithDefinition(CamFollowerDefinition definition) => new(definition, checked(Revision + 1));
}
