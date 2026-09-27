using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

/// <summary>14C adds request/search provenance, not a new geometric truth or mechanical profile.</summary>
public static class DiscreteLayoutContract
{
    public const string Version = "0.1";
    public const string RequestFormat = "gear-invest.discrete-layout-request";
    public const string ProjectFormat = "gear-invest.discrete-layout-project";
    public const string GenerationFormat = "gear-invest.discrete-layout-generation";
    public const string Backend = "parametric-cardinal-axial-realizer-v1";
    public const string Ranking = "preferred-footprint-travel-size-id-v1";
    public const int DomainCeiling = 8192, OffsetCeiling = 256, OptionCeiling = 16;
    public const int BudgetCeiling = 8192, CandidateCeiling = 128, DiagnosticCeiling = 64;
    public static string Id(string kind, params string[] fields) => DiscreteEmbodimentContract.Id(kind, Version, DiscreteGeometryContract.Pack(fields));
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> source, int ceiling)
    {
        var values = source.Take(ceiling + 1).ToArray();
        if (values.Length > ceiling) throw new ArgumentException("DomainTooLarge: option input collection exceeds resource ceiling");
        return Array.AsReadOnly(values);
    }
}

public sealed class DiscreteLayoutSource
{
    public DiscreteLayoutSource(string modelId, string mechanismId, string driverId, string eventDofId, string eventKey, int maxEvents = 16)
    { ModelId = modelId; MechanismId = mechanismId; DriverId = driverId; EventDofId = eventDofId; EventKey = eventKey; MaxEvents = maxEvents; }
    public string ModelId { get; }
    public string MechanismId { get; }
    public string DriverId { get; }
    public string EventDofId { get; }
    public string EventKey { get; }
    public int MaxEvents { get; }
    public string Canonical => DiscreteGeometryContract.Pack(ModelId, MechanismId, DriverId, EventDofId, EventKey, DiscreteGeometryContract.Int(MaxEvents));
}

public sealed class LayoutDistanceRange
{
    public LayoutDistanceRange(Rational min, Rational max, Rational step) { Min = min; Max = max; Step = step; }
    public Rational Min { get; }
    public Rational Max { get; }
    public Rational Step { get; }
}

public sealed class LayoutProbeConfiguration
{
    public LayoutProbeConfiguration(int bearingQuarter = 0, Rational? radialFraction = null, Rational? travel = null, Rational? retract = null)
    { BearingQuarter = bearingQuarter; RadialFraction = radialFraction ?? new Rational(3, 4); Travel = travel ?? new Rational(40); Retract = retract ?? new Rational(40); }
    public int BearingQuarter { get; }
    public Rational RadialFraction { get; }
    public Rational Travel { get; }
    public Rational Retract { get; }
    public string Canonical => DiscreteGeometryContract.Pack(DiscreteGeometryContract.Int(BearingQuarter), RadialFraction.ToString(), Travel.ToString(), Retract.ToString());
}

/// <summary>Immutable typed input. Invalid values are diagnosed by normalization, never silently repaired.
/// Normalized wire form contains explicit offsets; range/list syntax is authoring sugar only.</summary>
public sealed class DiscreteLayoutRequest
{
    public DiscreteLayoutRequest(DiscreteLayoutSource source, GeometryPoint anchor = default,
        IEnumerable<int>? directions = null, IEnumerable<Rational>? distances = null, LayoutDistanceRange? range = null,
        IEnumerable<GeometryPoint>? offsets = null, IEnumerable<Rational>? primaryRadii = null, IEnumerable<Rational>? secondaryRadii = null,
        IEnumerable<LayoutProbeConfiguration>? primaryProbes = null, IEnumerable<LayoutProbeConfiguration>? secondaryProbes = null,
        int frameQuarter = 0, IEnumerable<GeometryKeepOut>? keepOuts = null, GeometryBox? requiredRegion = null,
        GeometryPoint? preferredOffset = null, Rational? minimumClearance = null, int expansionBudget = 256, int candidateCap = 32,
        string profile = DiscreteGeometryContract.Profile, string backend = DiscreteLayoutContract.Backend,
        string ranking = DiscreteLayoutContract.Ranking, string unit = DiscreteGeometryContract.LengthUnit)
    {
        Source = source; Anchor = anchor; Range = range;
        Directions = DiscreteLayoutContract.Copy(directions ?? (offsets == null ? new[] { 0, 1, 2, 3 } : Array.Empty<int>()), 16);
        Distances = DiscreteLayoutContract.Copy(distances ?? (range == null && offsets == null ? new Rational[] { 24 } : Array.Empty<Rational>()), 256);
        Offsets = DiscreteLayoutContract.Copy(offsets ?? Array.Empty<GeometryPoint>(), 256);
        PrimaryRadii = DiscreteLayoutContract.Copy(primaryRadii ?? new Rational[] { 6, 7 }, 16);
        SecondaryRadii = DiscreteLayoutContract.Copy(secondaryRadii ?? new Rational[] { 6 }, 16);
        PrimaryProbes = DiscreteLayoutContract.Copy(primaryProbes ?? new[] { new LayoutProbeConfiguration() }, 16);
        SecondaryProbes = DiscreteLayoutContract.Copy(secondaryProbes ?? new[] { new LayoutProbeConfiguration() }, 16);
        KeepOuts = DiscreteLayoutContract.Copy(keepOuts ?? Array.Empty<GeometryKeepOut>(), 32);
        FrameQuarter = frameQuarter; RequiredRegion = requiredRegion; PreferredOffset = preferredOffset;
        MinimumClearance = minimumClearance ?? new Rational(1, 2); ExpansionBudget = expansionBudget; CandidateCap = candidateCap;
        Profile = profile; Backend = backend; Ranking = ranking; Unit = unit;
    }
    public DiscreteLayoutSource Source { get; }
    public GeometryPoint Anchor { get; }
    public ReadOnlyCollection<int> Directions { get; }
    public ReadOnlyCollection<Rational> Distances { get; }
    public LayoutDistanceRange? Range { get; }
    public ReadOnlyCollection<GeometryPoint> Offsets { get; }
    public ReadOnlyCollection<Rational> PrimaryRadii { get; }
    public ReadOnlyCollection<Rational> SecondaryRadii { get; }
    public ReadOnlyCollection<LayoutProbeConfiguration> PrimaryProbes { get; }
    public ReadOnlyCollection<LayoutProbeConfiguration> SecondaryProbes { get; }
    public ReadOnlyCollection<GeometryKeepOut> KeepOuts { get; }
    public int FrameQuarter { get; }
    public GeometryBox? RequiredRegion { get; }
    public GeometryPoint? PreferredOffset { get; }
    public Rational MinimumClearance { get; }
    public int ExpansionBudget { get; }
    public int CandidateCap { get; }
    public string Profile { get; }
    public string Backend { get; }
    public string Ranking { get; }
    public string Unit { get; }
    public string NormalizedCanonical => DiscreteGeometryContract.Pack(Source.Canonical, Anchor.Canonical,
        DiscreteGeometryContract.List(Offsets.Select(p => p.Canonical)), DiscreteGeometryContract.List(PrimaryRadii.Select(r => r.ToString())),
        DiscreteGeometryContract.List(SecondaryRadii.Select(r => r.ToString())), DiscreteGeometryContract.List(PrimaryProbes.Select(p => p.Canonical)),
        DiscreteGeometryContract.List(SecondaryProbes.Select(p => p.Canonical)), DiscreteGeometryContract.Int(FrameQuarter),
        DiscreteGeometryContract.List(KeepOuts.Select(k => k.Canonical)), RequiredRegion?.Canonical ?? "", PreferredOffset?.Canonical ?? "",
        MinimumClearance.ToString(), DiscreteGeometryContract.Int(ExpansionBudget), DiscreteGeometryContract.Int(CandidateCap), Profile, Backend, Ranking, Unit);
}

public enum DiscreteLayoutStatus { Complete, Infeasible, IncompleteBudget, InvalidInput, Unsupported, Cancelled }

public sealed class LayoutDiagnostic
{
    public LayoutDiagnostic(string code, string field, string assignment, string domain, string item, string detail)
    { Code = code; Field = field; Assignment = assignment; Domain = domain; Item = item; Detail = detail; }
    public string Code { get; }
    public string Field { get; }
    public string Assignment { get; }
    public string Domain { get; }
    public string Item { get; }
    public string Detail { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Code, Field, Assignment, Domain, Item, Detail);
}

public sealed class DiscreteLayoutNormalization
{
    public DiscreteLayoutNormalization(DiscreteLayoutRequest? request, BigInteger domainSize, DiscreteLayoutStatus status, IEnumerable<LayoutDiagnostic> diagnostics)
    { Request = request; DomainSize = domainSize; Status = status; Diagnostics = DiscreteLayoutContract.Copy(diagnostics, 64); }
    public DiscreteLayoutRequest? Request { get; }
    public BigInteger DomainSize { get; }
    public DiscreteLayoutStatus Status { get; }
    public ReadOnlyCollection<LayoutDiagnostic> Diagnostics { get; }
    public bool IsValid => Request != null;
    public string RequestId => Request == null ? "" : DiscreteLayoutContract.Id("discrete-layout-request", Request.NormalizedCanonical);
}

public sealed class DiscreteLayoutAssignment
{
    public DiscreteLayoutAssignment(GeometryPoint offset, Rational primaryRadius, Rational secondaryRadius, LayoutProbeConfiguration primaryProbe, LayoutProbeConfiguration secondaryProbe)
    { Offset = offset; PrimaryRadius = primaryRadius; SecondaryRadius = secondaryRadius; PrimaryProbe = primaryProbe; SecondaryProbe = secondaryProbe; }
    public GeometryPoint Offset { get; }
    public Rational PrimaryRadius { get; }
    public Rational SecondaryRadius { get; }
    public LayoutProbeConfiguration PrimaryProbe { get; }
    public LayoutProbeConfiguration SecondaryProbe { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Offset.Canonical, PrimaryRadius.ToString(), SecondaryRadius.ToString(), PrimaryProbe.Canonical, SecondaryProbe.Canonical);
    public string Key => DiscreteLayoutContract.Id("layout-assignment", Canonical);
}

public sealed class DiscreteLayoutCandidate
{
    public DiscreteLayoutCandidate(DiscreteGeometryCandidate geometry, DiscreteLayoutAssignment assignment, Rational preferredPenalty, Rational footprint, Rational travelSize,
        DiscreteGeometryValidation validation)
    { Geometry = geometry; Assignment = assignment; PreferredPenalty = preferredPenalty; Footprint = footprint; TravelSize = travelSize; Validation = validation; }
    public DiscreteGeometryCandidate Geometry { get; }
    public DiscreteLayoutAssignment Assignment { get; }
    public Rational PreferredPenalty { get; }
    public Rational Footprint { get; }
    public Rational TravelSize { get; }
    public DiscreteGeometryValidation Validation { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Geometry.GeometryId, Assignment.Canonical, PreferredPenalty.ToString(), Footprint.ToString(), TravelSize.ToString(), Validation.Canonical);
}

public sealed class DiscreteLayoutGeneration
{
    public DiscreteLayoutGeneration(DiscreteLayoutNormalization normalized, DiscreteLayoutStatus status, bool complete, int evaluated, int valid, int rejected,
        int inconclusive, int deduplicated, IEnumerable<DiscreteLayoutCandidate> candidates, IEnumerable<LayoutDiagnostic> diagnostics, int omittedDiagnostics)
    {
        Normalized = normalized; Status = status; SearchComplete = complete; Evaluated = evaluated; Valid = valid; Rejected = rejected; Inconclusive = inconclusive;
        Deduplicated = deduplicated; Candidates = DiscreteLayoutContract.Copy(candidates, 128); Diagnostics = DiscreteLayoutContract.Copy(diagnostics, 64); OmittedDiagnostics = omittedDiagnostics;
    }
    public DiscreteLayoutNormalization Normalized { get; }
    public string RequestId => Normalized.RequestId;
    public DiscreteLayoutStatus Status { get; }
    public bool SearchComplete { get; }
    public int Evaluated { get; }
    public int Valid { get; }
    public int Rejected { get; }
    public int Inconclusive { get; }
    public int Deduplicated { get; }
    public int OmittedDiagnostics { get; }
    public bool ResultTruncated => Valid - Deduplicated > Candidates.Count;
    public BigInteger Unvisited => Normalized.DomainSize - Evaluated;
    public ReadOnlyCollection<DiscreteLayoutCandidate> Candidates { get; }
    public ReadOnlyCollection<LayoutDiagnostic> Diagnostics { get; }
    public string Canonical => DiscreteGeometryContract.Pack(RequestId, Status.ToString(), SearchComplete.ToString(), Normalized.DomainSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DiscreteGeometryContract.Int(Evaluated), DiscreteGeometryContract.Int(Valid), DiscreteGeometryContract.Int(Rejected), DiscreteGeometryContract.Int(Inconclusive), DiscreteGeometryContract.Int(Deduplicated),
        ResultTruncated.ToString(), DiscreteGeometryContract.Int(OmittedDiagnostics), DiscreteGeometryContract.List(Candidates.Select(c => c.Canonical)), DiscreteGeometryContract.List(Diagnostics.Select(d => d.Canonical)));
}

public sealed class DiscreteLayoutEvaluation
{
    public DiscreteLayoutEvaluation(int primaryIndex, int secondaryIndex, Rational fromRoot, Rational toRoot, BigInteger cursor = default,
        int maxOccurrences = 16, Rational? primaryTurns = null, Rational? secondaryTurns = null)
    { PrimaryIndex = primaryIndex; SecondaryIndex = secondaryIndex; FromRoot = fromRoot; ToRoot = toRoot; Cursor = cursor; MaxOccurrences = maxOccurrences; PrimaryTurns = primaryTurns; SecondaryTurns = secondaryTurns; }
    public int PrimaryIndex { get; }
    public int SecondaryIndex { get; }
    public Rational FromRoot { get; }
    public Rational ToRoot { get; }
    public BigInteger Cursor { get; }
    public int MaxOccurrences { get; }
    public Rational? PrimaryTurns { get; }
    public Rational? SecondaryTurns { get; }
    public string Canonical => DiscreteGeometryContract.Pack(DiscreteGeometryContract.Int(PrimaryIndex), DiscreteGeometryContract.Int(SecondaryIndex), FromRoot.ToString(), ToRoot.ToString(),
        Cursor.ToString(System.Globalization.CultureInfo.InvariantCulture), DiscreteGeometryContract.Int(MaxOccurrences), PrimaryTurns?.ToString() ?? "", SecondaryTurns?.ToString() ?? "");
}

public sealed class DiscreteLayoutProjectFile
{
    public DiscreteLayoutProjectFile(string role, string path, int bytes, string sha256) { Role = role; Path = path; Bytes = bytes; Sha256 = sha256; }
    public string Role { get; }
    public string Path { get; }
    public int Bytes { get; }
    public string Sha256 { get; }
}

/// <summary>Portable bundle manifest. No absolute paths, timestamps, operation tokens or renderer state.</summary>
public sealed class DiscreteLayoutProjectManifest
{
    public DiscreteLayoutProjectManifest(string modelId, string mechanismId, string requestId, IEnumerable<DiscreteLayoutProjectFile> files,
        DiscreteLayoutStatus? generationStatus = null, bool complete = false, bool truncated = false, IEnumerable<string>? orderedGeometryIds = null,
        string? selectedGeometryId = null, DiscreteLayoutEvaluation? evaluation = null, string? executionId = null,
        string backend = DiscreteLayoutContract.Backend, string ranking = DiscreteLayoutContract.Ranking, string profile = DiscreteGeometryContract.Profile)
    {
        ModelId = modelId; MechanismId = mechanismId; RequestId = requestId; Files = DiscreteLayoutContract.Copy(files.OrderBy(f => f.Role, StringComparer.Ordinal), 6);
        GenerationStatus = generationStatus; SearchComplete = complete; ResultTruncated = truncated;
        OrderedGeometryIds = DiscreteLayoutContract.Copy(orderedGeometryIds ?? Array.Empty<string>(), 128);
        SelectedGeometryId = selectedGeometryId; Evaluation = evaluation; ExecutionId = executionId; Backend = backend; Ranking = ranking; Profile = profile;
    }
    public string ModelId { get; }
    public string MechanismId { get; }
    public string RequestId { get; }
    public string Backend { get; }
    public string Ranking { get; }
    public string Profile { get; }
    public ReadOnlyCollection<DiscreteLayoutProjectFile> Files { get; }
    public DiscreteLayoutStatus? GenerationStatus { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public ReadOnlyCollection<string> OrderedGeometryIds { get; }
    public string? SelectedGeometryId { get; }
    public DiscreteLayoutEvaluation? Evaluation { get; }
    public string? ExecutionId { get; }
}
