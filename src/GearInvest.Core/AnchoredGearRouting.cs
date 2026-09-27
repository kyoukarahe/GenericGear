using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace GearInvest.Core;

public static class GearRoutingContract
{
    public const string Version = "0.1";
    public const string Profile = "bounded-single-layer-external-idler-routing-v1";
    public const string Backend = "finite-integer-contact-graph-dfs-v1";
    public const string Ranking = "idler-preference-footprint-teeth-signature-v1";
    public const string RequestFormat = "gear-invest.anchored-gear-routing-request";
    public const string ResultFormat = "gear-invest.anchored-gear-routing-result";
    public const string ProjectFormat = "gear-invest.gear-routing-project";
    public const int MaxSites = 256, MaxToothOptions = 16, MaxNodes = 1024, MaxPairChecks = 523776;
    public const int MaxIdlers = 5, MaxExpansions = 1000000, MaxReturned = 128, MaxDetails = 64;
    public const int MaxKeepOuts = 16, MaxRequired = 8, MaxScalar = 1000000000, MaxTeeth = 10000, MaxPitchScale = 10000;
    public const int MaxRequestBytes = 128 * 1024, MaxResultBytes = 16 * 1024 * 1024;
    public static string Number(BigInteger n) => n.ToString(CultureInfo.InvariantCulture);
    public static string Pack(params string[] values) => string.Concat(values.Select(v => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v));
    public static string List(IEnumerable<string> values) => Pack(values.ToArray());
    public static string Sha(byte[] bytes)
    { using var hash = SHA256.Create(); return string.Concat(hash.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))); }
    public static string Id(string kind, string value) => kind + "-sha256:" + Sha(Encoding.UTF8.GetBytes(value));
    internal static ReadOnlyCollection<T> Bounded<T>(IEnumerable<T>? input, int ceiling, out bool exceeded)
    {
        var result = new List<T>(); exceeded = false;
        if (input != null) foreach (var value in input) { if (result.Count == ceiling) { exceeded = true; break; } result.Add(value); }
        return result.AsReadOnly();
    }
}

public enum GearRoutingStatus { Complete, Infeasible, IncompleteBudget, InvalidInput, Unsupported, Cancelled }

public readonly struct GearRoutePoint : IEquatable<GearRoutePoint>
{
    public GearRoutePoint(BigInteger x, BigInteger y) { X = x; Y = y; }
    public BigInteger X { get; }
    public BigInteger Y { get; }
    public string Canonical => GearRoutingContract.Pack(GearRoutingContract.Number(X), GearRoutingContract.Number(Y));
    public bool Equals(GearRoutePoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object? value) => value is GearRoutePoint p && Equals(p);
    public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
    public override string ToString() => GearRoutingContract.Number(X) + "," + GearRoutingContract.Number(Y);
}

public sealed class GearRouteBox
{
    public GearRouteBox(BigInteger minX, BigInteger minY, BigInteger maxX, BigInteger maxY)
    { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
    public BigInteger MinX { get; }
    public BigInteger MinY { get; }
    public BigInteger MaxX { get; }
    public BigInteger MaxY { get; }
    public bool IsOrdered => MinX <= MaxX && MinY <= MaxY;
    public string Canonical => GearRoutingContract.Pack(GearRoutingContract.Number(MinX), GearRoutingContract.Number(MinY), GearRoutingContract.Number(MaxX), GearRoutingContract.Number(MaxY));
}

public sealed class GearRouteRegion
{
    public GearRouteRegion(string id, GearRouteBox bounds) { Id = id; Bounds = bounds; }
    public string Id { get; }
    public GearRouteBox Bounds { get; }
    public string Canonical => GearRoutingContract.Pack(Id, Bounds.Canonical);
}

public sealed class GearRouteGrid
{
    public GearRouteGrid(GearRoutePoint origin, GearRoutePoint step, GearRouteBox bounds)
    { Origin = origin; Step = step; Bounds = bounds; }
    public GearRoutePoint Origin { get; }
    public GearRoutePoint Step { get; }
    public GearRouteBox Bounds { get; }
}

public sealed class GearRouteAnchor
{
    public GearRouteAnchor(GearRoutePoint position, int teeth) { Position = position; Teeth = teeth; }
    public GearRoutePoint Position { get; }
    public int Teeth { get; }
    public string Canonical => GearRoutingContract.Pack(Position.Canonical, GearRoutingContract.Number(Teeth));
}

/// <summary>Raw finite request. Normalize through the SDK before use. Enumerable ingestion itself is bounded.</summary>
public sealed class AnchoredGearRoutingRequest
{
    public AnchoredGearRoutingRequest(GearRouteAnchor input, GearRouteAnchor output, Rational targetTransfer,
        BigInteger pitchRadiusTicksPerTooth, GearRouteBox bounds,
        IEnumerable<GearRoutePoint>? sites = null, GearRouteGrid? grid = null, IEnumerable<int>? idlerTeeth = null,
        int minIdlers = 0, int maxIdlers = 5, BigInteger unrelatedClearance = default, BigInteger keepOutClearance = default,
        IEnumerable<GearRouteRegion>? keepOuts = null, IEnumerable<GearRouteRegion>? requiredRegions = null, GearRouteBox? preferredRegion = null,
        int expansionBudget = 100000, int maximumReturned = 128, int layer = 0, string unit = "tick",
        string topology = "simple-external-idler-chain", string profile = GearRoutingContract.Profile,
        string backend = GearRoutingContract.Backend, string ranking = GearRoutingContract.Ranking)
    {
        Input = input; Output = output; TargetTransfer = targetTransfer; PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth; Bounds = bounds;
        HasExplicitSites = sites != null; Sites = GearRoutingContract.Bounded(sites, GearRoutingContract.MaxSites * 2, out var s);
        IdlerTeeth = GearRoutingContract.Bounded(idlerTeeth ?? new[] { 10 }, GearRoutingContract.MaxToothOptions * 2, out var t);
        KeepOuts = GearRoutingContract.Bounded(keepOuts, GearRoutingContract.MaxKeepOuts, out var k);
        RequiredRegions = GearRoutingContract.Bounded(requiredRegions, GearRoutingContract.MaxRequired, out var r);
        IngestionLimitExceeded = s || t || k || r; Grid = grid; MinIdlers = minIdlers; MaxIdlerCount = maxIdlers;
        UnrelatedClearance = unrelatedClearance; KeepOutClearance = keepOutClearance; PreferredRegion = preferredRegion;
        ExpansionBudget = expansionBudget; MaximumReturned = maximumReturned; Layer = layer; Unit = unit;
        Topology = topology; Profile = profile; Backend = backend; Ranking = ranking;
    }
    public GearRouteAnchor Input { get; }
    public GearRouteAnchor Output { get; }
    public Rational TargetTransfer { get; }
    public BigInteger PitchRadiusTicksPerTooth { get; }
    public GearRouteBox Bounds { get; }
    public bool HasExplicitSites { get; }
    public ReadOnlyCollection<GearRoutePoint> Sites { get; }
    public GearRouteGrid? Grid { get; }
    public ReadOnlyCollection<int> IdlerTeeth { get; }
    public int MinIdlers { get; }
    public int MaxIdlerCount { get; }
    public BigInteger UnrelatedClearance { get; }
    public BigInteger KeepOutClearance { get; }
    public ReadOnlyCollection<GearRouteRegion> KeepOuts { get; }
    public ReadOnlyCollection<GearRouteRegion> RequiredRegions { get; }
    public GearRouteBox? PreferredRegion { get; }
    public int ExpansionBudget { get; }
    public int MaximumReturned { get; }
    public int Layer { get; }
    public string Unit { get; }
    public string Topology { get; }
    public string Profile { get; }
    public string Backend { get; }
    public string Ranking { get; }
    public bool IngestionLimitExceeded { get; }

    // Canonical is only consumed after normalization; grid syntax never enters identity.
    public string Canonical => GearRoutingContract.Pack(Profile, Backend, Ranking, Unit, Topology, GearRoutingContract.Number(Layer),
        Input.Canonical, Output.Canonical, TargetTransfer.ToString(), GearRoutingContract.Number(PitchRadiusTicksPerTooth), Bounds.Canonical,
        GearRoutingContract.List(Sites.Select(p => p.Canonical)), GearRoutingContract.List(IdlerTeeth.Select(n => GearRoutingContract.Number(n))),
        GearRoutingContract.Number(MinIdlers), GearRoutingContract.Number(MaxIdlerCount), GearRoutingContract.Number(UnrelatedClearance), GearRoutingContract.Number(KeepOutClearance),
        GearRoutingContract.List(KeepOuts.Select(r => r.Canonical)), GearRoutingContract.List(RequiredRegions.Select(r => r.Canonical)), PreferredRegion?.Canonical ?? "none",
        GearRoutingContract.Number(ExpansionBudget), GearRoutingContract.Number(MaximumReturned));
}

public sealed class GearRoutingNormalization
{
    public GearRoutingNormalization(AnchoredGearRoutingRequest? request, GearRoutingStatus status, IEnumerable<Diagnostic> diagnostics)
    { Request = request; Status = status; Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics); RequestId = request == null ? "" : GearRoutingContract.Id("anchored-gear-routing-request", request.Canonical); }
    public AnchoredGearRoutingRequest? Request { get; }
    public string RequestId { get; }
    public GearRoutingStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Request != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public int PotentialNodes => Request == null ? 0 : checked(Request.Sites.Count * Request.IdlerTeeth.Count + 2);
}

public sealed class GearRouteAssignment
{
    public GearRouteAssignment(GearRoutePoint position, int teeth) { Position = position; Teeth = teeth; }
    public GearRoutePoint Position { get; }
    public int Teeth { get; }
    public string Canonical => GearRoutingContract.Pack(Position.Canonical, GearRoutingContract.Number(Teeth));
}
