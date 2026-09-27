using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace GearInvest.Core;

public static class DiscreteEmbodimentContract
{
    public const string Profile = "idealized-discrete-indexing-v1";
    public const string Backend = "builtin-indexed-pair-compiler-v1";
    public const string ModelFormat = "gear-invest.discrete-embodiment";
    public const string ResultFormat = "gear-invest.discrete-actuation-result";
    public const string Version = "0.1";
    public static string Integer(BigInteger n) => n.ToString(CultureInfo.InvariantCulture);
    public static string Id(string domain, params string[] fields) => PeriodicSemanticIdentity.Hash(
        domain + "-sha256:", Pack(new[] { "discrete-v1" }.Concat(fields)));
    public static string Pack(IEnumerable<string> fields) => string.Concat(fields.Select(s =>
        s.Length.ToString(CultureInfo.InvariantCulture) + ":" + s));
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) => values.ToList().AsReadOnly();
}

public enum DiscreteComponentKind
{
    IndexedWheel, IndexedProgramTrack, FollowerProbe, DifferentialSelector,
    SelectionLatch, PositiveIndexer, ReferenceStop, DetentLock, ExternalDrive, ActuationSequence,
}
public enum DiscretePortKind { WheelPosition, DisplacementTicks, Selection, DriveStroke, ReferenceSettled, DetentState }
public enum DiscreteRoute { SingleStep, SeekReference }
public enum DiscreteActionKind { Sense, Latch, Drive, Return, Settle, Hold, ReferenceConfirmed, Commit }
public enum DiscreteStatus { Complete, IncompleteBudget, InvalidInput, Unsupported, Cancelled }

/// <summary>Closed discriminated primitive. Only the parameters admitted for Kind may be non-default.</summary>
public sealed class DiscreteComponent
{
    public DiscreteComponent(string id, DiscreteComponentKind kind, int detents = 0, int reference = 0,
        int capacity = 0, IEnumerable<BigInteger>? sectors = null, Rational? scale = null, Rational? offset = null)
    {
        Id = id; Kind = kind; Detents = detents; Reference = reference; Capacity = capacity;
        Sectors = DiscreteEmbodimentContract.Copy(sectors ?? Array.Empty<BigInteger>());
        Scale = scale ?? Rational.One; Offset = offset ?? Rational.Zero;
    }
    public string Id { get; }
    public DiscreteComponentKind Kind { get; }
    public int Detents { get; }
    public int Reference { get; }
    public int Capacity { get; }
    public ReadOnlyCollection<BigInteger> Sectors { get; }
    public Rational Scale { get; }
    public Rational Offset { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { Id, Kind.ToString(),
        DiscreteEmbodimentContract.Integer(Detents), DiscreteEmbodimentContract.Integer(Reference),
        DiscreteEmbodimentContract.Integer(Capacity), Scale.ToString(), Offset.ToString(),
        DiscreteEmbodimentContract.Pack(Sectors.Select(DiscreteEmbodimentContract.Integer)) });
}

public sealed class DiscreteConnection
{
    public DiscreteConnection(string from, string output, string to, string input, DiscretePortKind kind)
    { From = from; Output = output; To = to; Input = input; Kind = kind; }
    public string From { get; }
    public string Output { get; }
    public string To { get; }
    public string Input { get; }
    public DiscretePortKind Kind { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { From, Output, To, Input, Kind.ToString() });
}

public sealed class DiscreteMotionProfile
{
    public DiscreteMotionProfile(IEnumerable<Rational>? boundaries = null, string id = DiscreteEmbodimentContract.Profile)
    {
        Id = id;
        Boundaries = DiscreteEmbodimentContract.Copy(boundaries ?? new[] {
            Rational.Zero, new Rational(1,20), new Rational(1,10), new Rational(7,10),
            new Rational(3,4), new Rational(4,5), new Rational(17,20), new Rational(9,10), Rational.One });
    }
    public string Id { get; }
    public ReadOnlyCollection<Rational> Boundaries { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { Id,
        DiscreteEmbodimentContract.Pack(Boundaries.Select(x => x.ToString())) });
}

public sealed class DiscreteSourceBinding
{
    public DiscreteSourceBinding(string kind, string sourceId, string componentId)
    { Kind = kind; SourceId = sourceId; ComponentId = componentId; }
    public string Kind { get; }
    public string SourceId { get; }
    public string ComponentId { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { Kind, SourceId, ComponentId });
}

public sealed class DiscreteProjectionRequest
{
    public DiscreteProjectionRequest(IEnumerable<string> requiredStates, IEnumerable<string> requiredEffects,
        IEnumerable<string> omittedStates, IEnumerable<string> omittedEffects, string eventDefinitionId,
        int primaryPulseCapacity = 4, string profile = DiscreteEmbodimentContract.Profile)
    {
        RequiredStates = Sort(requiredStates); RequiredEffects = Sort(requiredEffects);
        OmittedStates = Sort(omittedStates); OmittedEffects = Sort(omittedEffects);
        EventDefinitionId = eventDefinitionId; PrimaryPulseCapacity = primaryPulseCapacity; Profile = profile;
    }
    private static ReadOnlyCollection<string> Sort(IEnumerable<string> v) =>
        DiscreteEmbodimentContract.Copy(v.OrderBy(x => x, StringComparer.Ordinal));
    public ReadOnlyCollection<string> RequiredStates { get; }
    public ReadOnlyCollection<string> RequiredEffects { get; }
    public ReadOnlyCollection<string> OmittedStates { get; }
    public ReadOnlyCollection<string> OmittedEffects { get; }
    public string EventDefinitionId { get; }
    public int PrimaryPulseCapacity { get; }
    public string Profile { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { Profile, EventDefinitionId,
        DiscreteEmbodimentContract.Integer(PrimaryPulseCapacity), DiscreteEmbodimentContract.Pack(RequiredStates),
        DiscreteEmbodimentContract.Pack(RequiredEffects), DiscreteEmbodimentContract.Pack(OmittedStates),
        DiscreteEmbodimentContract.Pack(OmittedEffects) });
}

public sealed class DiscreteEmbodimentModel
{
    public DiscreteEmbodimentModel(IEnumerable<DiscreteComponent> components, IEnumerable<DiscreteConnection> connections,
        DiscreteMotionProfile motion, string sourcePlanId, DiscreteProjectionRequest projection,
        IEnumerable<DiscreteSourceBinding> bindings)
    {
        Components = DiscreteEmbodimentContract.Copy(components.OrderBy(c => c.Id, StringComparer.Ordinal));
        Connections = DiscreteEmbodimentContract.Copy(connections.OrderBy(c => c.Canonical, StringComparer.Ordinal));
        Motion = motion; SourcePlanId = sourcePlanId; Projection = projection;
        Bindings = DiscreteEmbodimentContract.Copy(bindings.OrderBy(b => b.Canonical, StringComparer.Ordinal));
        ModelId = DiscreteEmbodimentContract.Id("discrete-embodiment-model", motion.Canonical,
            DiscreteEmbodimentContract.Pack(Components.Select(c => c.Canonical)),
            DiscreteEmbodimentContract.Pack(Connections.Select(c => c.Canonical)));
        BindingId = DiscreteEmbodimentContract.Id("discrete-embodiment-binding", ModelId, SourcePlanId,
            projection.Canonical, DiscreteEmbodimentContract.Pack(Bindings.Select(b => b.Canonical)));
    }
    public ReadOnlyCollection<DiscreteComponent> Components { get; }
    public ReadOnlyCollection<DiscreteConnection> Connections { get; }
    public DiscreteMotionProfile Motion { get; }
    public string SourcePlanId { get; }
    public DiscreteProjectionRequest Projection { get; }
    public ReadOnlyCollection<DiscreteSourceBinding> Bindings { get; }
    public string ModelId { get; }
    public string BindingId { get; }
}

public sealed class DiscreteWheelPosition
{
    public DiscreteWheelPosition(string wheelId, int index, Rational unwrappedTurns)
    { WheelId = wheelId; Index = index; UnwrappedTurns = unwrappedTurns; }
    public string WheelId { get; }
    public int Index { get; }
    public Rational UnwrappedTurns { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { WheelId,
        DiscreteEmbodimentContract.Integer(Index), UnwrappedTurns.ToString() });
}

public sealed class DiscreteEmbodimentState
{
    public DiscreteEmbodimentState(string modelId, string candidateId, string driverId, Rational rootTurns,
        BigInteger cursor, IEnumerable<DiscreteWheelPosition> positions)
    {
        ModelId = modelId; CandidateId = candidateId; DriverId = driverId; RootTurns = rootTurns; Cursor = cursor;
        Positions = DiscreteEmbodimentContract.Copy(positions.OrderBy(p => p.WheelId, StringComparer.Ordinal));
    }
    public string ModelId { get; }
    public string CandidateId { get; }
    public string DriverId { get; }
    public Rational RootTurns { get; }
    public BigInteger Cursor { get; }
    public ReadOnlyCollection<DiscreteWheelPosition> Positions { get; }
    public string MechanicalCanonical => DiscreteEmbodimentContract.Pack(new[] { ModelId, CandidateId, DriverId,
        DiscreteEmbodimentContract.Integer(Cursor), DiscreteEmbodimentContract.Pack(Positions.Select(p => p.Canonical)) });
    public string StateId => DiscreteEmbodimentContract.Id("discrete-embodiment-state", MechanicalCanonical, RootTurns.ToString());
}

public sealed class DiscreteMicroAction
{
    public DiscreteMicroAction(DiscreteActionKind kind, string componentId, string wheelId,
        Rational begin, Rational end, Rational fromTurns, Rational toTurns)
    { Kind = kind; ComponentId = componentId; WheelId = wheelId; Begin = begin; End = end; FromTurns = fromTurns; ToTurns = toTurns; }
    public DiscreteActionKind Kind { get; }
    public string ComponentId { get; }
    public string WheelId { get; }
    public Rational Begin { get; }
    public Rational End { get; }
    public Rational FromTurns { get; }
    public Rational ToTurns { get; }
    public string Canonical => DiscreteEmbodimentContract.Pack(new[] { Kind.ToString(), ComponentId, WheelId,
        Begin.ToString(), End.ToString(), FromTurns.ToString(), ToTurns.ToString() });
}

public sealed class DiscreteActuationCycle
{
    public DiscreteActuationCycle(string modelId, string occurrenceId, BigInteger ordinal, Rational eventRoot,
        DiscreteEmbodimentState before, DiscreteEmbodimentState after, Rational positionReading,
        Rational limitReading, DiscreteRoute route, IEnumerable<DiscreteMicroAction> actions)
    {
        ModelId = modelId; OccurrenceId = occurrenceId; Ordinal = ordinal; EventRoot = eventRoot;
        Before = before; After = after; PositionReading = positionReading; LimitReading = limitReading; Route = route;
        Actions = DiscreteEmbodimentContract.Copy(actions);
        CycleId = DiscreteEmbodimentContract.Id("discrete-actuation-cycle", modelId, occurrenceId,
            DiscreteEmbodimentContract.Integer(ordinal), eventRoot.ToString(), before.MechanicalCanonical,
            after.MechanicalCanonical, positionReading.ToString(), limitReading.ToString(), route.ToString(),
            DiscreteEmbodimentContract.Pack(Actions.Select(a => a.Canonical)));
    }
    public string ModelId { get; }
    public string CycleId { get; }
    public string OccurrenceId { get; }
    public BigInteger Ordinal { get; }
    public Rational EventRoot { get; }
    public DiscreteEmbodimentState Before { get; }
    public DiscreteEmbodimentState After { get; }
    public Rational PositionReading { get; }
    public Rational LimitReading { get; }
    public DiscreteRoute Route { get; }
    public ReadOnlyCollection<DiscreteMicroAction> Actions { get; }
}

public sealed class DiscreteValidationLevel
{
    public DiscreteValidationLevel(string level, string status, string detail)
    { Level = level; Status = status; Detail = detail; }
    public string Level { get; }
    public string Status { get; }
    public string Detail { get; }
}

public sealed class DiscreteValidation
{
    public DiscreteValidation(IEnumerable<string> diagnostics, IEnumerable<DiscreteValidationLevel>? levels = null)
    {
        Diagnostics = DiscreteEmbodimentContract.Copy(diagnostics.OrderBy(d => d, StringComparer.Ordinal));
        Levels = DiscreteEmbodimentContract.Copy(levels ?? Array.Empty<DiscreteValidationLevel>());
    }
    public bool IsValid => Diagnostics.Count == 0;
    public ReadOnlyCollection<string> Diagnostics { get; }
    public ReadOnlyCollection<DiscreteValidationLevel> Levels { get; }
}

public sealed class DiscreteCompilationResult
{
    public DiscreteCompilationResult(DiscreteStatus status, DiscreteProjectionRequest request,
        DiscreteEmbodimentModel? model, DiscreteValidation validation)
    { Status = status; Request = request; Model = model; Validation = validation; }
    public DiscreteStatus Status { get; }
    public DiscreteProjectionRequest Request { get; }
    public DiscreteEmbodimentModel? Model { get; }
    public DiscreteValidation Validation { get; }
    public string Backend => DiscreteEmbodimentContract.Backend;
}

public sealed class DiscreteActuationResult
{
    public DiscreteActuationResult(string modelId, string bindingId, string eventRequestId, int budget,
        DiscreteStatus status, int knownOccurrences, Rational toRoot,
        DiscreteEmbodimentState initial, IEnumerable<DiscreteActuationCycle> cycles,
        DiscreteEmbodimentState? final, DiscreteEmbodimentState? checkpoint, DiscreteValidation validation)
    {
        ModelId = modelId; BindingId = bindingId; EventRequestId = eventRequestId; Budget = budget;
        Status = status; KnownOccurrences = knownOccurrences; ToRoot = toRoot; Initial = initial;
        Cycles = DiscreteEmbodimentContract.Copy(cycles); Final = final; Checkpoint = checkpoint; Validation = validation;
        RequestId = DiscreteEmbodimentContract.Id("discrete-execution-request", modelId, bindingId,
            eventRequestId, initial.StateId, toRoot.ToString(), DiscreteEmbodimentContract.Integer(budget));
    }
    public string ModelId { get; }
    public string BindingId { get; }
    public string EventRequestId { get; }
    public string RequestId { get; }
    public int Budget { get; }
    public DiscreteStatus Status { get; }
    public int KnownOccurrences { get; }
    public int AppliedOccurrences => Cycles.Count;
    public int OmittedOccurrences => KnownOccurrences - AppliedOccurrences;
    public bool SearchComplete => Status == DiscreteStatus.Complete;
    public bool ResultTruncated => Status == DiscreteStatus.IncompleteBudget;
    public Rational ToRoot { get; }
    public DiscreteEmbodimentState Initial { get; }
    public ReadOnlyCollection<DiscreteActuationCycle> Cycles { get; }
    public DiscreteEmbodimentState? Final { get; }
    public DiscreteEmbodimentState? Checkpoint { get; }
    public DiscreteValidation Validation { get; }
}

public sealed class DiscreteActuationFrame
{
    public DiscreteActuationFrame(string cycleId, Rational phase, DiscreteActionKind stage, DiscreteRoute route,
        bool latched, bool committed, string activeComponent, Rational stroke,
        IEnumerable<DiscreteWheelPosition> poses, IEnumerable<string> unlockedWheels,
        DiscreteEmbodimentState committedState, BigInteger ordinal, Rational eventRoot,
        Rational positionReading, Rational limitReading)
    {
        CycleId = cycleId; Phase = phase; Stage = stage; Route = route; Latched = latched; Committed = committed;
        ActiveComponent = activeComponent; Stroke = stroke; Poses = DiscreteEmbodimentContract.Copy(poses);
        UnlockedWheels = DiscreteEmbodimentContract.Copy(unlockedWheels); CommittedState = committedState;
        Ordinal = ordinal; EventRoot = eventRoot; PositionReading = positionReading; LimitReading = limitReading;
    }
    public string CycleId { get; }
    public Rational Phase { get; }
    public DiscreteActionKind Stage { get; }
    public DiscreteRoute Route { get; }
    public bool Latched { get; }
    public bool Committed { get; }
    public string ActiveComponent { get; }
    public Rational Stroke { get; }
    public ReadOnlyCollection<DiscreteWheelPosition> Poses { get; }
    public ReadOnlyCollection<string> UnlockedWheels { get; }
    public DiscreteEmbodimentState CommittedState { get; }
    public BigInteger Ordinal { get; }
    public Rational EventRoot { get; }
    public Rational PositionReading { get; }
    public Rational LimitReading { get; }
    public string FrameId => DiscreteEmbodimentContract.Id("discrete-actuation-frame", CycleId, Phase.ToString(),
        Stage.ToString(), Route.ToString(), Latched.ToString(), Committed.ToString(), ActiveComponent, Stroke.ToString(),
        DiscreteEmbodimentContract.Pack(Poses.Select(p => p.Canonical)), DiscreteEmbodimentContract.Pack(UnlockedWheels), CommittedState.StateId);
}
