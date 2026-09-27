using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class DiscreteGeometryContract
{
    public const string Profile = "settled-axial-track-geometry-v1";
    public const string Backend = "bounded-cardinal-axial-realizer-v1";
    public const string GeometryFormat = "gear-invest.discrete-geometry";
    public const string ResultFormat = "gear-invest.geometric-discrete-actuation-result";
    public const string Version = "0.1";
    public const string LengthUnit = "geometry-tick";
    public static Rational MillimetersPerTick => new Rational(1, 10);
    public static Rational ApproachEnd => new Rational(1, 50);
    public static Rational Latch => new Rational(1, 20);
    public static Rational ClearDrive => new Rational(1, 10);
    public static string Pack(params string[] fields) => DiscreteEmbodimentContract.Pack(fields);
    public static string List(IEnumerable<string> fields) => DiscreteEmbodimentContract.Pack(fields);
    public static string Id(string domain, params string[] fields) => DiscreteEmbodimentContract.Id(domain, Profile, Pack(fields));
    public static string Int(int value) => DiscreteEmbodimentContract.Integer(value);
    public static ReadOnlyCollection<T> Sort<T>(IEnumerable<T> source, Func<T, string> key) =>
        DiscreteEmbodimentContract.Copy(source.OrderBy(key, StringComparer.Ordinal));
}

/// <summary>Exact model-space coordinates, in geometry-ticks; never implicit gear pitch units.</summary>
public readonly struct GeometryPoint
{
    public GeometryPoint(Rational x, Rational y, Rational z) { X = x; Y = y; Z = z; }
    public Rational X { get; }
    public Rational Y { get; }
    public Rational Z { get; }
    public string Canonical => DiscreteGeometryContract.Pack(X.ToString(), Y.ToString(), Z.ToString());
    public static GeometryPoint operator +(GeometryPoint a, GeometryPoint b) => new GeometryPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static GeometryPoint operator -(GeometryPoint a, GeometryPoint b) => new GeometryPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static GeometryPoint operator *(GeometryPoint a, Rational s) => new GeometryPoint(a.X * s, a.Y * s, a.Z * s);
}

public sealed class GeometryBox
{
    public GeometryBox(GeometryPoint min, GeometryPoint max) { Min = min; Max = max; }
    public GeometryPoint Min { get; }
    public GeometryPoint Max { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Min.Canonical, Max.Canonical);
}

public sealed class AxialSurfacePatch
{
    public AxialSurfacePatch(string id, Rational startTurns, Rational spanTurns, Rational height)
    { Id = id; StartTurns = startTurns; SpanTurns = spanTurns; Height = height; }
    public string Id { get; }
    public Rational StartTurns { get; }
    public Rational SpanTurns { get; }
    public Rational Height { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Id, StartTurns.ToString(), SpanTurns.ToString(), Height.ToString());
}

/// <summary>One wheel and rigid annular track, one ideal axial point probe and cardinal point actuator.
/// Guides constrain motion; only the probe guide's declared box is a supported occupied guide body.</summary>
public sealed class DiscreteGeometryStation
{
    public DiscreteGeometryStation(string wheelId, string trackId, string probeId, int detents,
        GeometryPoint center, GeometryPoint trackCenter, int frameQuarter, Rational mountingTurns,
        Rational wheelRadius, Rational innerRadius, Rational outerRadius, Rational thickness,
        IEnumerable<AxialSurfacePatch> patches, GeometryPoint probeOrigin, GeometryPoint probeDirection,
        Rational travel, Rational datum, Rational scale, Rational angularMargin,
        GeometryPoint actuatorStart, GeometryPoint actuatorEnd, GeometryBox engagement,
        GeometryBox wheelEnvelope, GeometryBox trackEnvelope, GeometryBox probeSweep,
        GeometryBox probeGuide, GeometryBox actuatorSweep)
    {
        WheelId = wheelId; TrackId = trackId; ProbeId = probeId; Detents = detents;
        Center = center; TrackCenter = trackCenter; FrameQuarter = frameQuarter; MountingTurns = mountingTurns;
        WheelRadius = wheelRadius; InnerRadius = innerRadius; OuterRadius = outerRadius; Thickness = thickness;
        Patches = DiscreteGeometryContract.Sort(patches, p => p.Id);
        ProbeOrigin = probeOrigin; ProbeDirection = probeDirection; Travel = travel; Datum = datum; Scale = scale; AngularMargin = angularMargin;
        ActuatorStart = actuatorStart; ActuatorEnd = actuatorEnd; Engagement = engagement;
        WheelEnvelope = wheelEnvelope; TrackEnvelope = trackEnvelope; ProbeSweep = probeSweep; ProbeGuide = probeGuide; ActuatorSweep = actuatorSweep;
    }
    public string WheelId { get; }
    public string TrackId { get; }
    public string ProbeId { get; }
    public int Detents { get; }
    public GeometryPoint Center { get; }
    public GeometryPoint TrackCenter { get; }
    public int FrameQuarter { get; }
    public Rational MountingTurns { get; }
    public Rational WheelRadius { get; }
    public Rational InnerRadius { get; }
    public Rational OuterRadius { get; }
    public Rational Thickness { get; }
    public ReadOnlyCollection<AxialSurfacePatch> Patches { get; }
    public GeometryPoint ProbeOrigin { get; }
    public GeometryPoint ProbeDirection { get; }
    public Rational Travel { get; }
    public Rational Datum { get; }
    public Rational Scale { get; }
    public Rational AngularMargin { get; }
    public GeometryPoint ActuatorStart { get; }
    public GeometryPoint ActuatorEnd { get; }
    public GeometryBox Engagement { get; }
    public GeometryBox WheelEnvelope { get; }
    public GeometryBox TrackEnvelope { get; }
    public GeometryBox ProbeSweep { get; }
    public GeometryBox ProbeGuide { get; }
    public GeometryBox ActuatorSweep { get; }
    public string Canonical => DiscreteGeometryContract.Pack(WheelId, TrackId, ProbeId, DiscreteGeometryContract.Int(Detents),
        Center.Canonical, TrackCenter.Canonical, DiscreteGeometryContract.Int(FrameQuarter), MountingTurns.ToString(),
        WheelRadius.ToString(), InnerRadius.ToString(), OuterRadius.ToString(), Thickness.ToString(),
        DiscreteGeometryContract.List(Patches.Select(p => p.Canonical)), ProbeOrigin.Canonical, ProbeDirection.Canonical,
        Travel.ToString(), Datum.ToString(), Scale.ToString(), AngularMargin.ToString(), ActuatorStart.Canonical, ActuatorEnd.Canonical,
        Engagement.Canonical, WheelEnvelope.Canonical, TrackEnvelope.Canonical, ProbeSweep.Canonical, ProbeGuide.Canonical, ActuatorSweep.Canonical);

    public DiscreteGeometryStation With(GeometryPoint? center = null, GeometryPoint? trackCenter = null, int? frameQuarter = null,
        Rational? mountingTurns = null, IEnumerable<AxialSurfacePatch>? patches = null, GeometryPoint? probeOrigin = null,
        GeometryPoint? probeDirection = null, Rational? travel = null, Rational? datum = null, Rational? scale = null,
        GeometryBox? wheelEnvelope = null, GeometryBox? trackEnvelope = null, GeometryBox? probeSweep = null,
        GeometryBox? probeGuide = null, GeometryBox? actuatorSweep = null, GeometryPoint? actuatorStart = null,
        GeometryPoint? actuatorEnd = null, GeometryBox? engagement = null) =>
        new DiscreteGeometryStation(WheelId, TrackId, ProbeId, Detents, center ?? Center, trackCenter ?? TrackCenter,
            frameQuarter ?? FrameQuarter, mountingTurns ?? MountingTurns, WheelRadius, InnerRadius, OuterRadius, Thickness,
            patches ?? Patches, probeOrigin ?? ProbeOrigin, probeDirection ?? ProbeDirection, travel ?? Travel, datum ?? Datum,
            scale ?? Scale, AngularMargin, actuatorStart ?? ActuatorStart, actuatorEnd ?? ActuatorEnd, engagement ?? Engagement,
            wheelEnvelope ?? WheelEnvelope, trackEnvelope ?? TrackEnvelope, probeSweep ?? ProbeSweep, probeGuide ?? ProbeGuide, actuatorSweep ?? ActuatorSweep);
}

public sealed class GeometryKeepOut
{
    public GeometryKeepOut(string id, GeometryBox bounds) { Id = id; Bounds = bounds; }
    public string Id { get; }
    public GeometryBox Bounds { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Id, Bounds.Canonical);
}

public sealed class GeometryContactPermission
{
    public GeometryContactPermission(string mover, string target, string port, string kind, Rational begin, Rational end, string condition)
    { Mover = mover; Target = target; Port = port; Kind = kind; Begin = begin; End = end; Condition = condition; }
    public string Mover { get; }
    public string Target { get; }
    public string Port { get; }
    public string Kind { get; }
    public Rational Begin { get; }
    public Rational End { get; }
    public string Condition { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Mover, Target, Port, Kind, Begin.ToString(), End.ToString(), Condition);
}

public sealed class DiscreteGeometryCandidate
{
    public DiscreteGeometryCandidate(string modelId, string sourceBindingId, IEnumerable<DiscreteGeometryStation> stations,
        IEnumerable<GeometryKeepOut> keepOuts, IEnumerable<GeometryContactPermission> contacts,
        Rational minimumClearance, string profile = DiscreteGeometryContract.Profile,
        string lengthUnit = DiscreteGeometryContract.LengthUnit, Rational? millimetersPerTick = null)
    {
        ModelId = modelId; SourceBindingId = sourceBindingId; Stations = DiscreteGeometryContract.Sort(stations, s => s.WheelId);
        KeepOuts = DiscreteGeometryContract.Sort(keepOuts, s => s.Id); Contacts = DiscreteGeometryContract.Sort(contacts, c => c.Canonical);
        MinimumClearance = minimumClearance; Profile = profile; LengthUnit = lengthUnit;
        MillimetersPerTick = millimetersPerTick ?? DiscreteGeometryContract.MillimetersPerTick;
        GeometryId = DiscreteGeometryContract.Id("discrete-geometry", GeometryCanonical);
        RealizationBindingId = DiscreteGeometryContract.Id("discrete-realization-binding", GeometryId, ModelId, SourceBindingId);
    }
    public string ModelId { get; }
    public string SourceBindingId { get; }
    public ReadOnlyCollection<DiscreteGeometryStation> Stations { get; }
    public ReadOnlyCollection<GeometryKeepOut> KeepOuts { get; }
    public ReadOnlyCollection<GeometryContactPermission> Contacts { get; }
    public Rational MinimumClearance { get; }
    public string Profile { get; }
    public string LengthUnit { get; }
    public Rational MillimetersPerTick { get; }
    public string GeometryId { get; }
    public string RealizationBindingId { get; }
    public string GeometryCanonical => DiscreteGeometryContract.Pack(Profile, LengthUnit, MillimetersPerTick.ToString(), MinimumClearance.ToString(),
        DiscreteGeometryContract.List(Stations.Select(s => s.Canonical)), DiscreteGeometryContract.List(KeepOuts.Select(s => s.Canonical)), DiscreteGeometryContract.List(Contacts.Select(s => s.Canonical)));
    public DiscreteGeometryCandidate With(IEnumerable<DiscreteGeometryStation>? stations = null, IEnumerable<GeometryKeepOut>? keepOuts = null,
        IEnumerable<GeometryContactPermission>? contacts = null) => new DiscreteGeometryCandidate(ModelId, SourceBindingId,
            stations ?? Stations, keepOuts ?? KeepOuts, contacts ?? Contacts, MinimumClearance, Profile, LengthUnit, MillimetersPerTick);
}

public sealed class DiscreteGeometryRequest
{
    public DiscreteGeometryRequest(GeometryPoint primaryAnchor, IEnumerable<GeometryPoint> secondaryOffsets,
        IEnumerable<Rational> trackRadii, int expansionBudget = 64, int candidateCap = 8,
        GeometryPoint? preferredOffset = null, GeometryBox? requiredSecondaryRegion = null,
        IEnumerable<GeometryKeepOut>? keepOuts = null, int frameQuarter = 0, int probeBearingQuarter = 0,
        Rational? probeTravel = null, Rational? retractHeight = null, Rational? minimumClearance = null,
        Rational? minimumBaseZ = null, Rational? maximumBaseZ = null, string profile = DiscreteGeometryContract.Profile)
    {
        PrimaryAnchor = primaryAnchor; SecondaryOffsets = DiscreteGeometryContract.Sort(secondaryOffsets, p => p.Canonical);
        TrackRadii = DiscreteEmbodimentContract.Copy(trackRadii.OrderBy(r => r));
        ExpansionBudget = expansionBudget; CandidateCap = candidateCap; PreferredOffset = preferredOffset; RequiredSecondaryRegion = requiredSecondaryRegion;
        KeepOuts = DiscreteGeometryContract.Sort(keepOuts ?? Array.Empty<GeometryKeepOut>(), k => k.Id);
        FrameQuarter = frameQuarter; ProbeBearingQuarter = probeBearingQuarter;
        ProbeTravel = probeTravel ?? new Rational(40); RetractHeight = retractHeight ?? new Rational(40);
        MinimumClearance = minimumClearance ?? new Rational(1, 2); MinimumBaseZ = minimumBaseZ ?? new Rational(-10000); MaximumBaseZ = maximumBaseZ ?? new Rational(10000); Profile = profile;
    }
    public GeometryPoint PrimaryAnchor { get; }
    public ReadOnlyCollection<GeometryPoint> SecondaryOffsets { get; }
    public ReadOnlyCollection<Rational> TrackRadii { get; }
    public int ExpansionBudget { get; }
    public int CandidateCap { get; }
    public GeometryPoint? PreferredOffset { get; }
    public GeometryBox? RequiredSecondaryRegion { get; }
    public ReadOnlyCollection<GeometryKeepOut> KeepOuts { get; }
    public int FrameQuarter { get; }
    public int ProbeBearingQuarter { get; }
    public Rational ProbeTravel { get; }
    public Rational RetractHeight { get; }
    public Rational MinimumClearance { get; }
    public Rational MinimumBaseZ { get; }
    public Rational MaximumBaseZ { get; }
    public string Profile { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Profile, PrimaryAnchor.Canonical,
        DiscreteGeometryContract.List(SecondaryOffsets.Select(p => p.Canonical)), DiscreteGeometryContract.List(TrackRadii.Select(r => r.ToString())),
        DiscreteGeometryContract.Int(ExpansionBudget), DiscreteGeometryContract.Int(CandidateCap), PreferredOffset?.Canonical ?? "", RequiredSecondaryRegion?.Canonical ?? "",
        DiscreteGeometryContract.List(KeepOuts.Select(k => k.Canonical)), DiscreteGeometryContract.Int(FrameQuarter), DiscreteGeometryContract.Int(ProbeBearingQuarter),
        ProbeTravel.ToString(), RetractHeight.ToString(), MinimumClearance.ToString(), MinimumBaseZ.ToString(), MaximumBaseZ.ToString());
}

public enum GeometryGenerationStatus { Complete, Exhausted, BudgetExhausted, InvalidInput, Unsupported, Cancelled }
public enum GeometryVerdict { Pass, ProvenClearWithinDeclaredEnvelope, ProvenViolation, Inconclusive, SampledOnly, NotPerformed }
public enum GeometryHitStatus { Hit, Missing, TravelLimit, Boundary, Ambiguous, Occluded, InvalidCalibration, Unsupported }

public sealed class GeometryCheck
{
    public GeometryCheck(string domain, string item, GeometryVerdict verdict, string method, Rational begin, Rational end, string detail, bool required = true)
    { Domain = domain; Item = item; Verdict = verdict; Method = method; Begin = begin; End = end; Detail = detail; Required = required; }
    public string Domain { get; }
    public string Item { get; }
    public GeometryVerdict Verdict { get; }
    public string Method { get; }
    public Rational Begin { get; }
    public Rational End { get; }
    public string Detail { get; }
    public bool Required { get; }
    public string Canonical => DiscreteGeometryContract.Pack(Domain, Item, Verdict.ToString(), Method, Begin.ToString(), End.ToString(), Detail, Required.ToString());
}

public sealed class DiscreteGeometryValidation
{
    public DiscreteGeometryValidation(IEnumerable<GeometryCheck> checks) { Checks = DiscreteGeometryContract.Sort(checks, c => c.Canonical); }
    public ReadOnlyCollection<GeometryCheck> Checks { get; }
    public bool IsValid => Checks.Count > 0 && Checks.Where(c => c.Required).All(c => c.Verdict == GeometryVerdict.Pass || c.Verdict == GeometryVerdict.ProvenClearWithinDeclaredEnvelope);
    public string Canonical => DiscreteGeometryContract.List(Checks.Select(c => c.Canonical));
}

public sealed class DiscreteGeometryGenerationResult
{
    public DiscreteGeometryGenerationResult(string requestId, GeometryGenerationStatus status, bool searchComplete, int expanded, int validCount,
        IEnumerable<DiscreteGeometryCandidate> candidates, IEnumerable<string> diagnostics)
    { RequestId = requestId; Status = status; SearchComplete = searchComplete; Expanded = expanded; ValidCount = validCount;
        Candidates = DiscreteEmbodimentContract.Copy(candidates); Diagnostics = DiscreteGeometryContract.Sort(diagnostics, d => d); }
    public string RequestId { get; }
    public GeometryGenerationStatus Status { get; }
    public bool SearchComplete { get; }
    public int Expanded { get; }
    public int ValidCount { get; }
    public bool ResultTruncated => ValidCount > Candidates.Count;
    public ReadOnlyCollection<DiscreteGeometryCandidate> Candidates { get; }
    public ReadOnlyCollection<string> Diagnostics { get; }
}

public sealed class DiscreteGeometryHit
{
    public DiscreteGeometryHit(string geometryId, string wheelId, string trackId, string probeId, string surfaceId, GeometryHitStatus status,
        Rational wheelTurns, Rational localBearing, GeometryPoint point, Rational travel, Rational displacement, Rational value, string diagnostic)
    { GeometryId = geometryId; WheelId = wheelId; TrackId = trackId; ProbeId = probeId; SurfaceId = surfaceId; Status = status;
        WheelTurns = wheelTurns; LocalBearing = localBearing; Point = point; Travel = travel; Displacement = displacement; Value = value; Diagnostic = diagnostic; }
    public string GeometryId { get; }
    public string WheelId { get; }
    public string TrackId { get; }
    public string ProbeId { get; }
    public string SurfaceId { get; }
    public GeometryHitStatus Status { get; }
    public Rational WheelTurns { get; }
    public Rational LocalBearing { get; }
    public GeometryPoint Point { get; }
    public Rational Travel { get; }
    public Rational Displacement { get; }
    public Rational Value { get; }
    public string Diagnostic { get; }
    public bool InteriorMember => Status == GeometryHitStatus.Hit || Status == GeometryHitStatus.InvalidCalibration;
    public string Canonical => DiscreteGeometryContract.Pack(GeometryId, WheelId, TrackId, ProbeId, SurfaceId, Status.ToString(), WheelTurns.ToString(),
        LocalBearing.ToString(), Point.Canonical, Travel.ToString(), Displacement.ToString(), Value.ToString(), Diagnostic);
    public string WitnessId => DiscreteGeometryContract.Id("discrete-geometry-hit", Canonical);
}

public sealed class GeometricDiscreteCycle
{
    public GeometricDiscreteCycle(string geometryId, DiscreteActuationCycle motion, IEnumerable<DiscreteGeometryHit> hits)
    { GeometryId = geometryId; Motion = motion; Hits = DiscreteGeometryContract.Sort(hits, h => h.ProbeId); }
    public string GeometryId { get; }
    public DiscreteActuationCycle Motion { get; }
    public ReadOnlyCollection<DiscreteGeometryHit> Hits { get; }
    public string CycleId => DiscreteGeometryContract.Id("geometric-discrete-cycle", GeometryId, Motion.CycleId, DiscreteGeometryContract.List(Hits.Select(h => h.Canonical)));
}

public sealed class GeometricDiscreteResult
{
    public GeometricDiscreteResult(string geometryId, string bindingId, string modelId, string eventRequestId,
        DiscreteEmbodimentState initial, Rational toRoot, int budget, int known, DiscreteStatus status,
        IEnumerable<GeometricDiscreteCycle> cycles, DiscreteEmbodimentState? final, DiscreteEmbodimentState? checkpoint, DiscreteGeometryValidation validation)
    {
        GeometryId = geometryId; RealizationBindingId = bindingId; ModelId = modelId; EventRequestId = eventRequestId;
        Initial = initial; ToRoot = toRoot; Budget = budget; Known = known; Status = status;
        Cycles = DiscreteEmbodimentContract.Copy(cycles); Final = final; Checkpoint = checkpoint; Validation = validation;
    }
    public string GeometryId { get; }
    public string RealizationBindingId { get; }
    public string ModelId { get; }
    public string EventRequestId { get; }
    public DiscreteEmbodimentState Initial { get; }
    public Rational ToRoot { get; }
    public int Budget { get; }
    public int Known { get; }
    public int Applied => Cycles.Count;
    public int Omitted => Known - Applied;
    public DiscreteStatus Status { get; }
    public ReadOnlyCollection<GeometricDiscreteCycle> Cycles { get; }
    public DiscreteEmbodimentState? Final { get; }
    public DiscreteEmbodimentState? Checkpoint { get; }
    public DiscreteGeometryValidation Validation { get; }
    public string ExecutionId => DiscreteGeometryContract.Id("geometric-discrete-execution", GeometryId, RealizationBindingId, ModelId, EventRequestId, Initial.StateId, ToRoot.ToString(), DiscreteGeometryContract.Int(Budget));
}

public sealed class GeometricProbePose
{
    public GeometricProbePose(string id, GeometryPoint position, string stage, bool clear) { ProbeId = id; Position = position; Stage = stage; ClearanceConfirmed = clear; }
    public string ProbeId { get; }
    public GeometryPoint Position { get; }
    public string Stage { get; }
    public bool ClearanceConfirmed { get; }
    public string Canonical => DiscreteGeometryContract.Pack(ProbeId, Position.Canonical, Stage, ClearanceConfirmed.ToString());
}

public sealed class GeometricDiscreteFrame
{
    public GeometricDiscreteFrame(string geometryId, string cycleId, DiscreteActuationFrame motion, IEnumerable<GeometricProbePose> probes, IEnumerable<DiscreteGeometryHit> hits)
    { GeometryId = geometryId; CycleId = cycleId; Motion = motion; Probes = DiscreteGeometryContract.Sort(probes, p => p.ProbeId); Hits = DiscreteGeometryContract.Sort(hits, h => h.ProbeId); }
    public string GeometryId { get; }
    public string CycleId { get; }
    public DiscreteActuationFrame Motion { get; }
    public ReadOnlyCollection<GeometricProbePose> Probes { get; }
    public ReadOnlyCollection<DiscreteGeometryHit> Hits { get; }
    public bool Latched => Motion.Phase >= DiscreteGeometryContract.Latch;
    public string Canonical => DiscreteGeometryContract.Pack(GeometryId, CycleId, Motion.FrameId,
        Motion.Phase.ToString(), Motion.Stage.ToString(), Motion.Route.ToString(), Latched.ToString(), Motion.Committed.ToString(),
        Motion.ActiveComponent, Motion.Stroke.ToString(), DiscreteGeometryContract.List(Motion.Poses.Select(p => p.Canonical)),
        DiscreteGeometryContract.List(Motion.UnlockedWheels), Motion.CommittedState.StateId,
        Motion.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), Motion.EventRoot.ToString(), Motion.PositionReading.ToString(), Motion.LimitReading.ToString(),
        DiscreteGeometryContract.List(Probes.Select(p => p.Canonical)), DiscreteGeometryContract.List(Hits.Select(h => h.Canonical)));
    public string FrameId => DiscreteGeometryContract.Id("geometric-discrete-frame", Canonical);
}
