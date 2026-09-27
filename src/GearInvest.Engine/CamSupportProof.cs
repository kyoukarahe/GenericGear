using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum CamGeometryProofStatus
{
    StrictConvexityProved, ConvexityRefuted, GeometryProofIncomplete, GeometryResourceLimit, InvalidProfile, InvalidProofRequest
}

/// <summary>Proof options are independent of the immutable profile. Illegal options remain diagnosable.</summary>
public sealed class CamGeometryProofRequest
{
    public const string CurrentPolicy = "cam-quintic-bernstein-midpoint-v1";
    public CamGeometryProofRequest(int maximumPrecisionBits = 512, int maximumDepth = 16, int maximumNodes = 4096,
        int maximumWork = 262144, string? policy = CurrentPolicy)
    { MaximumPrecisionBits = maximumPrecisionBits; MaximumDepth = maximumDepth; MaximumNodes = maximumNodes; MaximumWork = maximumWork; Policy = policy; }
    public int MaximumPrecisionBits { get; }
    public int MaximumDepth { get; }
    public int MaximumNodes { get; }
    public int MaximumWork { get; }
    public string? Policy { get; }
    public static CamGeometryProofRequest Default => new();
    public string CanonicalRepresentation => Pack(Policy is null ? "0" : "1", Policy ?? "", N(MaximumPrecisionBits),
        N(MaximumDepth), N(MaximumNodes), N(MaximumWork));
}

public sealed class CamConvexityWitness
{
    internal CamConvexityWitness(string segment, Rational local, Rational material, Rational height, Rational second, CamInterval interval)
    { SegmentId = segment; LocalParameter = local; MaterialNormalTurns = material; HeightMm = height; SecondDerivativeMmPerTurnSquared = second; RadiusInterval = interval; }
    public string SegmentId { get; }
    public Rational LocalParameter { get; }
    public Rational MaterialNormalTurns { get; }
    public Rational HeightMm { get; }
    public Rational SecondDerivativeMmPerTurnSquared { get; }
    public CamInterval RadiusInterval { get; }
    public string CanonicalRepresentation => Pack(SegmentId, F(LocalParameter), F(MaterialNormalTurns), F(HeightMm),
        F(SecondDerivativeMmPerTurnSquared), RadiusInterval.CanonicalRepresentation);
}

public sealed class CamGeometryProofResult
{
    internal CamGeometryProofResult(string profileId, CamGeometryProofRequest request, CamGeometryProofStatus status, int work,
        int nodes, int precision, int depth, int leaves, Rational? lower, CamConvexityWitness? witness, string summary)
    {
        ProfileId = profileId; Request = request; Status = status; Work = work; Nodes = nodes; PrecisionBits = precision;
        MaximumDepthReached = depth; ProvedLeaves = leaves; MinimumRadiusLowerBoundMm = lower; Witness = witness; Summary = summary;
    }
    public string ProfileId { get; }
    public CamGeometryProofRequest Request { get; }
    public CamGeometryProofStatus Status { get; }
    public bool IsProved => Status == CamGeometryProofStatus.StrictConvexityProved;
    public bool IsRefuted => Status == CamGeometryProofStatus.ConvexityRefuted;
    public int Work { get; }
    public int Nodes { get; }
    public int PrecisionBits { get; }
    public int MaximumDepthReached { get; }
    public int ProvedLeaves { get; }
    /// <summary>A whole-profile lower bound exists only when every closed leaf was certified.</summary>
    public Rational? MinimumRadiusLowerBoundMm { get; }
    public CamConvexityWitness? Witness { get; }
    public string Summary { get; }
    public string ProofId => HashText(CanonicalRepresentation);
    public string CanonicalRepresentation => Pack(ProfileId, Request.CanonicalRepresentation, Status.ToString(), N(Work), N(Nodes),
        N(PrecisionBits), N(MaximumDepthReached), N(ProvedLeaves), MinimumRadiusLowerBoundMm.HasValue ? F(MinimumRadiusLowerBoundMm.Value) : "none",
        Witness?.CanonicalRepresentation ?? "none", Summary);
}

/// <summary>Whole-segment proof for rational quintics plus their second derivatives divided by 4*pi^2.</summary>
public static class CamGeometryProver
{
    public static CamGeometryProofResult Prove(CamSupportProfile profile, CamGeometryProofRequest? request = null)
    {
        request ??= CamGeometryProofRequest.Default;
        var profileId = profile?.ProfileId ?? "none";
        if (request.Policy != CamGeometryProofRequest.CurrentPolicy || !CamNumericalPrimitives.Precisions.Contains(request.MaximumPrecisionBits) ||
            request.MaximumDepth < 0 || request.MaximumDepth > 16 || request.MaximumNodes < 0 || request.MaximumNodes > 4096 ||
            request.MaximumWork < 0 || request.MaximumWork > 262144)
            return new(profileId, request, CamGeometryProofStatus.InvalidProofRequest, 0, 0, 0, 0, 0, null, null, "Invalid proof policy or bounded options.");
        var issues = CamSupportGeometry.Validate(profile!);
        if (issues.Count != 0)
            return new(profileId, request, CamGeometryProofStatus.InvalidProfile, 0, 0, 0, 0, 0, null, null, string.Join(",", issues));
        var c = new CrankSliderNumerics.Context(request.MaximumWork);
        var nodes = 0; var depth = 0; var leaves = 0; var precision = 0; var unresolved = 0; Rational? minimum = null;
        CamGeometryProofResult Result(CamGeometryProofStatus status, string summary, CamConvexityWitness? witness = null) =>
            new(profileId, request, status, c.Work, nodes, precision, depth, leaves,
                status == CamGeometryProofStatus.StrictConvexityProved ? minimum : null, witness, summary);
        try
        {
            if (request.MaximumNodes == 0) return Result(CamGeometryProofStatus.GeometryProofIncomplete, "Proof node budget exhausted before the first segment.");
            c.Step(); c.SetPrecision(request.MaximumPrecisionBits); precision = request.MaximumPrecisionBits;
            var pi = CamNumericalPrimitives.Pi(c);
            var inverseFourPiSquared = CamNumericalPrimitives.Reciprocal(c, c.Scale(c.Square(pi), 4));
            foreach (var segment in profile!.Segments)
            {
                var width = c.Subtract(segment.EndTurns, segment.StartTurns);
                var delta = c.Subtract(segment.EndHeight.Value, segment.StartHeight.Value);
                var derivativeScale = c.Divide(delta, c.Multiply(width, width));
                var derivative = new[] { 0, 12, 6, -6, -12, 0 };
                var coefficients = Enumerable.Range(0, 6).Select(i => new Pair(i < 3 ? segment.StartHeight.Value : segment.EndHeight.Value,
                    segment.Kind == CamSupportSegmentKind.Dwell ? 0 : c.Multiply(derivativeScale, derivative[i]))).ToArray();
                var pending = new Stack<Node>(); pending.Push(new(coefficients, 0, 1, 0));
                while (pending.Count != 0)
                {
                    if (nodes == request.MaximumNodes) return Result(CamGeometryProofStatus.GeometryProofIncomplete, "Proof node budget exhausted with unresolved intervals.");
                    c.Step(); var node = pending.Pop(); nodes++; depth = Math.Max(depth, node.Depth);
                    var bounds = node.Coefficients.Select(pair => Bound(c, pair, inverseFourPiSquared)).ToArray();
                    if (bounds.All(bound => bound.Lower > 0))
                    {
                        leaves++; var lower = bounds.Min(bound => bound.Lower);
                        if (!minimum.HasValue || lower < minimum.Value) minimum = lower;
                        continue;
                    }
                    // A coefficient lower bound is not a pointwise counterexample. Check actual parameters.
                    var split = Subdivide(c, node.Coefficients);
                    var midpoint = c.Divide(c.Add(node.Low, node.High), 2);
                    var candidates = new[] { (node.Low, node.Coefficients[0]), (midpoint, split.Left[5]), (node.High, node.Coefficients[5]) };
                    foreach (var candidate in candidates)
                    {
                        var interval = Bound(c, candidate.Item2, inverseFourPiSquared);
                        if (interval.Upper >= 0) continue;
                        var t = c.Add(segment.StartTurns, c.Multiply(width, candidate.Item1));
                        var witness = new CamConvexityWitness(segment.Id, candidate.Item1, t, candidate.Item2.Height, candidate.Item2.Second,
                            new CamInterval(interval));
                        return Result(CamGeometryProofStatus.ConvexityRefuted, "Certified negative curvature radius at an exact rational parameter.", witness);
                    }
                    if (node.Depth == request.MaximumDepth)
                    {
                        // Keep inspecting independent remaining leaves while budget permits: an unresolved
                        // interval must not hide a certified negative witness in a later segment.
                        unresolved++; continue;
                    }
                    pending.Push(new(split.Right, midpoint, node.High, node.Depth + 1));
                    pending.Push(new(split.Left, node.Low, midpoint, node.Depth + 1));
                }
            }
            if (unresolved != 0) return Result(CamGeometryProofStatus.GeometryProofIncomplete,
                "Subdivision depth or fixed pi precision leaves " + N(unresolved) + " unresolved intervals.");
            return Result(CamGeometryProofStatus.StrictConvexityProved,
                "Every closed Bernstein leaf has a strictly positive radius lower bound; periodic C2 support has unique global flat contact.");
        }
        catch (CrankSliderNumerics.Stop stop)
        {
            return Result(stop.Status == CrankSliderNumericStatus.NumericResourceLimit ? CamGeometryProofStatus.GeometryResourceLimit :
                CamGeometryProofStatus.GeometryProofIncomplete, stop.Message);
        }
    }

    private readonly struct Pair
    {
        internal Pair(Rational height, Rational second) { Height = height; Second = second; }
        internal Rational Height { get; }
        internal Rational Second { get; }
    }
    private sealed class Node
    {
        internal Node(Pair[] coefficients, Rational low, Rational high, int depth) { Coefficients = coefficients; Low = low; High = high; Depth = depth; }
        internal Pair[] Coefficients { get; }
        internal Rational Low { get; }
        internal Rational High { get; }
        internal int Depth { get; }
    }
    private static CrankSliderInterval Bound(CrankSliderNumerics.Context c, Pair pair, CrankSliderInterval inverseFourPiSquared) =>
        c.Add(CamNumericalPrimitives.Point(pair.Height), c.Scale(inverseFourPiSquared, pair.Second));
    private static (Pair[] Left, Pair[] Right) Subdivide(CrankSliderNumerics.Context c, Pair[] source)
    {
        var row = source; var left = new Pair[6]; var right = new Pair[6];
        left[0] = row[0]; right[5] = row[5];
        for (var level = 1; level <= 5; level++)
        {
            var next = new Pair[6 - level];
            for (var i = 0; i < next.Length; i++)
                next[i] = new(c.Divide(c.Add(row[i].Height, row[i + 1].Height), 2), c.Divide(c.Add(row[i].Second, row[i + 1].Second), 2));
            row = next; left[level] = row[0]; right[5 - level] = row[row.Length - 1];
        }
        return (left, right);
    }
}
