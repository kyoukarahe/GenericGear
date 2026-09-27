using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum CamSupportSegmentKind { Dwell, Quintic }

/// <summary>Authored support distance versus outward normal turns, never a polar-radius table.</summary>
public sealed class CamSupportSegment
{
    public CamSupportSegment(string id, Rational startTurns, Rational endTurns, ExactQuantity startHeight,
        ExactQuantity endHeight, CamSupportSegmentKind kind)
    {
        Id = MechanicalAuthoringProfile.IdValue(id);
        MechanicalAuthoringProfile.Number(startTurns); MechanicalAuthoringProfile.Number(endTurns);
        RotaryLinearProfile.Quantity(startHeight); RotaryLinearProfile.Quantity(endHeight);
        StartTurns = startTurns; EndTurns = endTurns; StartHeight = startHeight; EndHeight = endHeight; Kind = kind;
    }
    public string Id { get; }
    public Rational StartTurns { get; }
    public Rational EndTurns { get; }
    public ExactQuantity StartHeight { get; }
    public ExactQuantity EndHeight { get; }
    public CamSupportSegmentKind Kind { get; }
    public string CanonicalRepresentation => Pack(Id, F(StartTurns), F(EndTurns), RotaryLinearProfile.Q(StartHeight),
        RotaryLinearProfile.Q(EndHeight), Kind.ToString());
}

/// <summary>Immutable authored profile. Bounded malformed topology is retained for explicit diagnosis.</summary>
public sealed class CamSupportProfile
{
    public const string GeometryPolicy = "cardinal-convex-support-cam-flat-follower-v1";
    public const int MaximumSegments = 32;
    public CamSupportProfile(IEnumerable<CamSupportSegment> segments)
    {
        if (segments is null) throw new ArgumentNullException(nameof(segments));
        var items = segments.Take(MaximumSegments + 1).ToArray();
        if (items.Length > MaximumSegments || items.Any(s => s is null)) throw new ArgumentException("Bounded non-null support segments required.");
        Segments = items.OrderBy(s => s.StartTurns).ThenBy(s => s.Id, StringComparer.Ordinal)
            .ThenBy(s => s.CanonicalRepresentation, StringComparer.Ordinal).ToList().AsReadOnly();
        ProfileId = HashText(CanonicalRepresentation);
    }
    public ReadOnlyCollection<CamSupportSegment> Segments { get; }
    public string ProfileId { get; }
    public string CanonicalRepresentation => Pack(GeometryPolicy, Pack(Segments.Select(s => s.CanonicalRepresentation).ToArray()));
}

public sealed class CamSupportSample
{
    internal CamSupportSample(Rational reduced, string segmentId, Rational local, Rational height, Rational first, Rational second)
    {
        foreach (var value in new[] { reduced, local, height, first, second }) MechanicalDerivedNumbers.Check(value);
        ReducedTurns = reduced; SegmentId = segmentId; LocalParameter = local; HeightMm = height;
        FirstDerivativeMmPerTurn = first; SecondDerivativeMmPerTurnSquared = second;
        TangentOffsetMm = ExactPiLength.FromCanonical(0, first / 2);
    }
    public Rational ReducedTurns { get; }
    public string SegmentId { get; }
    public Rational LocalParameter { get; }
    public Rational HeightMm { get; }
    public Rational FirstDerivativeMmPerTurn { get; }
    public Rational SecondDerivativeMmPerTurnSquared { get; }
    public ExactPiLength TangentOffsetMm { get; }
    public string CanonicalRepresentation => Pack(F(ReducedTurns), SegmentId, F(LocalParameter), F(HeightMm),
        F(FirstDerivativeMmPerTurn), F(SecondDerivativeMmPerTurnSquared));
}

public sealed class CamSupportEnvelope
{
    internal CamSupportEnvelope(Rational minimum, Rational maximum, Rational minimumKCoefficient, Rational maximumKCoefficient)
    {
        foreach (var value in new[] { minimum, maximum, maximum - minimum, minimumKCoefficient, maximumKCoefficient }) MechanicalDerivedNumbers.Check(value);
        MinimumHeightMm = minimum; MaximumHeightMm = maximum; StrokeMm = maximum - minimum;
        MinimumTangentOffsetMm = ExactPiLength.FromCanonical(0, minimumKCoefficient);
        MaximumTangentOffsetMm = ExactPiLength.FromCanonical(0, maximumKCoefficient);
    }
    public Rational MinimumHeightMm { get; }
    public Rational MaximumHeightMm { get; }
    public Rational StrokeMm { get; }
    public ExactPiLength MinimumTangentOffsetMm { get; }
    public ExactPiLength MaximumTangentOffsetMm { get; }
    /// <summary>Bounds the contour radius only after its global convex-support proof succeeds.</summary>
    public Rational AdmittedBodyRadiusBoundMm => MaximumHeightMm;
    public string CanonicalRepresentation => Pack(F(MinimumHeightMm), F(MaximumHeightMm), F(StrokeMm),
        F(MinimumTangentOffsetMm.InversePiCoefficientMm), F(MaximumTangentOffsetMm.InversePiCoefficientMm));
}

public static class CamSupportGeometry
{
    public static ReadOnlyCollection<string> Validate(CamSupportProfile profile)
    {
        if (profile is null) return new List<string> { "MissingSupportProfile" }.AsReadOnly();
        var issues = new HashSet<string>(StringComparer.Ordinal);
        if (profile.Segments.Count == 0) issues.Add("EmptySupportProfile");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        CamSupportSegment? previous = null;
        foreach (var s in profile.Segments)
        {
            if (!ids.Add(s.Id)) issues.Add("DuplicateSupportSegmentId");
            if (s.StartTurns < 0 || s.EndTurns > 1 || s.StartTurns >= s.EndTurns) issues.Add("InvalidSupportSegmentWidth");
            if (s.StartHeight.Kind != QuantityKind.LinearPosition || s.EndHeight.Kind != QuantityKind.LinearPosition) issues.Add("SupportHeightDimensionMismatch");
            if (s.StartHeight.Value <= 0 || s.EndHeight.Value <= 0) issues.Add("NonPositiveSupportHeight");
            if (s.Kind != CamSupportSegmentKind.Dwell && s.Kind != CamSupportSegmentKind.Quintic) issues.Add("UnsupportedSupportSegmentKind");
            if (s.Kind == CamSupportSegmentKind.Dwell && s.StartHeight.Value != s.EndHeight.Value) issues.Add("NonConstantSupportDwell");
            if (previous is null)
            {
                if (s.StartTurns != 0) issues.Add("MissingSupportCoverage");
            }
            else
            {
                if (s.StartTurns > previous.EndTurns) issues.Add("MissingSupportCoverage");
                if (s.StartTurns < previous.EndTurns) issues.Add("OverlappingSupportSegments");
                if (s.StartHeight.Value != previous.EndHeight.Value) issues.Add("DiscontinuousSupportHeight");
            }
            previous = s;
        }
        if (previous is not null)
        {
            if (previous.EndTurns != 1) issues.Add("MissingSupportCoverage");
            if (previous.EndHeight.Value != profile.Segments[0].StartHeight.Value) issues.Add("NonPeriodicSupportHeight");
        }
        return issues.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    public static CamSupportSample At(CamSupportProfile profile, Rational materialNormalTurns)
    {
        RequireValid(profile); MechanicalDerivedNumbers.Check(materialNormalTurns);
        var t = PitchChainGeometry.ReduceTurns(materialNormalTurns);
        var s = profile.Segments.First(segment => segment.StartTurns <= t && t < segment.EndTurns);
        // Exact construction has fixed degree and segment bounds. Reuse the integer guards as well,
        // so a maximal derived phase cannot create unchecked polynomial intermediates.
        var c = new CrankSliderNumerics.Context(262144);
        try
        {
            var w = c.Subtract(s.EndTurns, s.StartTurns); var v = c.Divide(c.Subtract(t, s.StartTurns), w);
            if (s.Kind == CamSupportSegmentKind.Dwell) return new(t, s.Id, v, s.StartHeight.Value, 0, 0);
            var delta = c.Subtract(s.EndHeight.Value, s.StartHeight.Value);
            var v2 = c.Multiply(v, v); var v3 = c.Multiply(v2, v); var oneMinus = c.Subtract(1, v);
            var h = c.Add(s.StartHeight.Value, c.Multiply(c.Multiply(delta, v3), c.Add(10, c.Multiply(v, c.Add(-15, c.Multiply(6, v))))));
            var first = c.Divide(c.Multiply(c.Multiply(c.Multiply(c.Multiply(delta, 30), v2), oneMinus), oneMinus), w);
            var second = c.Divide(c.Multiply(c.Multiply(c.Multiply(c.Multiply(delta, 60), v), oneMinus), c.Subtract(1, c.Multiply(2, v))), c.Multiply(w, w));
            return new(t, s.Id, v, h, first, second);
        }
        catch (CrankSliderNumerics.Stop stop)
        { throw new ArgumentException("Cam exact sample resource limit: " + stop.Message, stop); }
    }

    public static CamSupportEnvelope GetEnvelope(CamSupportProfile profile)
    {
        RequireValid(profile);
        var min = profile.Segments.Min(s => s.StartHeight.Value); var max = profile.Segments.Max(s => s.StartHeight.Value);
        Rational minK = 0, maxK = 0;
        foreach (var s in profile.Segments)
        {
            if (s.Kind == CamSupportSegmentKind.Dwell) continue;
            var coefficient = 15 * (s.EndHeight.Value - s.StartHeight.Value) / (16 * (s.EndTurns - s.StartTurns));
            MechanicalDerivedNumbers.Check(coefficient);
            if (coefficient < minK) minK = coefficient;
            if (coefficient > maxK) maxK = coefficient;
        }
        return new(min, max, minK, maxK);
    }

    internal static void RequireValid(CamSupportProfile profile)
    {
        var issues = Validate(profile);
        if (issues.Count != 0) throw new ArgumentException("Invalid support profile: " + string.Join(",", issues));
    }
}

/// <summary>Exact material contour recipe; its bounded evaluation is separate from device admission.</summary>
public sealed class CamContourPointRecipe
{
    private CamContourPointRecipe(CamSupportProfile profile, Rational t, Rational phi, ExactVector3 origin, ExactVector3 g, ExactVector3 f)
    {
        if (!g.IsCardinal || !f.IsCardinal || g.Dot(f) != 0) throw new ArgumentException("Perpendicular cardinal cam plane basis required.");
        foreach (var value in new[] { t, phi, origin.X, origin.Y, origin.Z }) MechanicalDerivedNumbers.Check(value);
        Profile = profile; Support = CamSupportGeometry.At(profile, t); MaterialNormalTurns = t; PhysicalPhaseTurns = phi;
        NormalTurns = PitchChainGeometry.ReduceTurns(Support.ReducedTurns + PitchChainGeometry.ReduceTurns(phi));
        MechanicalDerivedNumbers.Check(NormalTurns);
        CenterMm = origin; GuideDirection = g; TangentDirection = f; RecipeId = HashText(CanonicalRepresentation);
    }
    public static CamContourPointRecipe Create(CamSupportProfile profile, Rational materialNormalTurns, Rational physicalPhaseTurns,
        ExactVector3 centerMm, ExactVector3 guideDirection, ExactVector3 tangentDirection) =>
        new(profile, materialNormalTurns, physicalPhaseTurns, centerMm, guideDirection, tangentDirection);
    public CamSupportProfile Profile { get; }
    public string ProfileId => Profile.ProfileId;
    public CamSupportSample Support { get; }
    public Rational MaterialNormalTurns { get; }
    public Rational PhysicalPhaseTurns { get; }
    public Rational NormalTurns { get; }
    public ExactVector3 CenterMm { get; }
    public ExactVector3 GuideDirection { get; }
    public ExactVector3 TangentDirection { get; }
    public string RecipeId { get; }
    public string CanonicalRepresentation => Pack("cam-contour-recipe-v1", ProfileId, F(MaterialNormalTurns), F(PhysicalPhaseTurns),
        F(NormalTurns), V(CenterMm), V(GuideDirection), V(TangentDirection), Support.CanonicalRepresentation);
}
