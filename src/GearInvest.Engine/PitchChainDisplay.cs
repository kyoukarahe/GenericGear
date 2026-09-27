using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Only the optional numerical projection failed; canonical mechanics and exact material identity remain unchanged.</summary>
public sealed class PitchChainDisplayUnavailableException : Exception
{
    public PitchChainDisplayUnavailableException(string message) : base(message) { }
}

public readonly struct PitchChainDisplayPoint
{
    internal PitchChainDisplayPoint(double x, double y, double z)
    { PitchChainDisplay.Finite(x); PitchChainDisplay.Finite(y); PitchChainDisplay.Finite(z); X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

public sealed class PitchChainDisplayPin
{
    internal PitchChainDisplayPin(PitchChainPinPose pin, PitchChainDisplayPoint position)
    { PinId = pin.PinId; MaterialIndex = pin.MaterialIndex; RouteIndex = pin.RouteIndex; Section = pin.Section; Position = position; }
    public string PinId { get; }
    public int MaterialIndex { get; }
    public int RouteIndex { get; }
    public PitchChainRouteSection Section { get; }
    public PitchChainDisplayPoint Position { get; }
}

public sealed class PitchChainDisplayLink
{
    internal PitchChainDisplayLink(PitchChainLinkPose link, PitchChainDisplayPoint start, PitchChainDisplayPoint end)
    { LinkId = link.LinkId; MaterialIndex = link.MaterialIndex; StartPinId = link.StartPinId; EndPinId = link.EndPinId; Alternation = link.Alternation; Start = start; End = end; }
    public string LinkId { get; }
    public int MaterialIndex { get; }
    public string StartPinId { get; }
    public string EndPinId { get; }
    public string Alternation { get; }
    public PitchChainDisplayPoint Start { get; }
    public PitchChainDisplayPoint End { get; }
}

public sealed class PitchChainDisplayPose
{
    internal PitchChainDisplayPose(IndexedChainPoseDescriptor pose, IEnumerable<PitchChainDisplayPin> pins, IEnumerable<PitchChainDisplayLink> links)
    {
        ExactPoseId = pose.PoseId; GeometryId = pose.Geometry.GeometryId; ChainId = pose.ChainId;
        PitchRadiusMm = PitchChainDisplay.ApproximateRadius(pose.Geometry.Pitch); PitchMm = PitchChainDisplay.ApproximateScalar(pose.Geometry.Pitch.PitchMm);
        InputCenterMm = PitchChainDisplay.ApproximatePoint(pose.Geometry.InputCenterMm); OutputCenterMm = PitchChainDisplay.ApproximatePoint(pose.Geometry.OutputCenterMm);
        Pins = pins.ToList().AsReadOnly(); Links = links.ToList().AsReadOnly();
    }
    public string ExactPoseId { get; }
    public string GeometryId { get; }
    public string ChainId { get; }
    public string Scope => "ApproximateDisplayOnly";
    public double PitchRadiusMm { get; }
    public double PitchMm { get; }
    public PitchChainDisplayPoint InputCenterMm { get; }
    public PitchChainDisplayPoint OutputCenterMm { get; }
    public ReadOnlyCollection<PitchChainDisplayPin> Pins { get; }
    public ReadOnlyCollection<PitchChainDisplayLink> Links { get; }
}

public static class PitchChainDisplay
{
    public static PitchChainDisplayPose Approximate(IndexedChainPoseDescriptor pose)
    {
        if (pose is null) throw new ArgumentNullException(nameof(pose));
        if (!pose.HasExactProof || pose.Pins.Count > PitchChainProfile.MaxMaterialPoses) throw new PitchChainDisplayUnavailableException("A complete bounded admitted exact pose is required for display.");
        var pins = pose.Pins.Select(p => new PitchChainDisplayPin(p, Approximate(p.Position))).ToArray();
        var points = pins.ToDictionary(p => p.PinId, p => p.Position, StringComparer.Ordinal);
        var links = new List<PitchChainDisplayLink>(pose.Links.Count); var pitch = Number(pose.Geometry.Pitch.PitchMm);
        foreach (var link in pose.Links)
        {
            var a = points[link.StartPinId]; var b = points[link.EndPinId];
            var dx = a.X - b.X; var dy = a.Y - b.Y; var dz = a.Z - b.Z;
            var displayedLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(displayedLength > 0) || Math.Abs(displayedLength - pitch) > pitch * 1e-7)
                throw new PitchChainDisplayUnavailableException("Display precision cannot retain distinct fixed-pitch endpoints; exact mechanics were not stretched, clamped or repaired.");
            links.Add(new PitchChainDisplayLink(link, a, b));
        }
        return new PitchChainDisplayPose(pose, pins, links);
    }
    public static PitchChainDisplayPoint Approximate(PitchChainPointRecipe recipe)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        var radius = ApproximateRadius(recipe.Pitch); var angle = Number(PitchChainGeometry.ReduceTurns(recipe.AngleTurns)) * (2 * Math.PI);
        var along = recipe.PitchOffset * Number(recipe.Pitch.PitchMm) + radius * Math.Cos(angle); var side = radius * Math.Sin(angle);
        var c = ApproximatePoint(recipe.CenterMm); var e = ApproximatePoint(recipe.CenterDirection); var f = ApproximatePoint(recipe.SideDirection);
        return new PitchChainDisplayPoint(c.X + along * e.X + side * f.X, c.Y + along * e.Y + side * f.Y, c.Z + along * e.Z + side * f.Z);
    }
    public static double ApproximateRadius(RegularSprocketPitchDescriptor pitch)
    {
        if (pitch is null) throw new ArgumentNullException(nameof(pitch));
        var radius = Number(pitch.PitchMm) / (2 * Math.Sin(Math.PI / pitch.ToothCount)); Finite(radius);
        if (!(radius > 0)) throw new PitchChainDisplayUnavailableException("Positive exact pin radius cannot be represented for display.");
        return radius;
    }
    public static PitchChainDisplayPoint ApproximatePoint(ExactVector3 point) => new(Number(point.X), Number(point.Y), Number(point.Z));
    public static PitchChainDisplayPoint ApproximatePoint(PitchChainPointRecipe point) => Approximate(point);
    public static double ApproximateScalar(Rational value) => Number(value);
    /// <summary>Exact reduction precedes numerical conversion, so large unwrapped turn counters are never lost before material indexing.</summary>
    public static double ApproximateTurns(Rational value) => Number(PitchChainGeometry.ReduceTurns(value));
    internal static double Number(Rational value)
    {
        if (value.IsZero) return 0;
        var n = BigInteger.Abs(value.Numerator); var d = value.Denominator;
        var sn = Math.Max(0, BitLength(n) - 53); var sd = Math.Max(0, BitLength(d) - 53);
        var result = (double)(n >> sn) / (double)(d >> sd) * Math.Pow(2, sn - sd) * value.Sign;
        Finite(result);
        if (result == 0) throw new PitchChainDisplayUnavailableException("A nonzero exact value underflows the optional display projection.");
        return result;
    }
    private static int BitLength(BigInteger positive)
    {
        var bytes = positive.ToByteArray(); var top = bytes.Length - 1;
        while (top > 0 && bytes[top] == 0) top--;
        var bits = top * 8; var value = bytes[top]; while (value > 0) { bits++; value >>= 1; } return bits;
    }
    internal static void Finite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1e12)
            throw new PitchChainDisplayUnavailableException("Display numeric resource bound exceeded; exact mechanics and material recipes are unchanged.");
    }
}
