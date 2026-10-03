using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class RuntimeEvent
{
    public RuntimeEvent(string id, BigInteger sequence, int ordinal, MechanicalConnectionEventKind kind)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); MechanicalRuntime.Counter(sequence);
        if (sequence < 1 || ordinal < 0 || ordinal > 15 || !Enum.IsDefined(typeof(MechanicalConnectionEventKind), kind)) throw new ArgumentException("InvalidEventOrder");
        Sequence = sequence; Ordinal = ordinal; Kind = kind;
    }
    public string Id { get; }
    public BigInteger Sequence { get; }
    public int Ordinal { get; }
    public MechanicalConnectionEventKind Kind { get; }
    internal string Key => Pack(Id, MechanicalRuntime.Text(Sequence), N(Ordinal), Kind.ToString());
}

public sealed class RuntimeSegment
{
    public RuntimeSegment(MechanicalModeInput input, IEnumerable<RuntimeEvent>? events = null)
    {
        Input = input; var values = (events ?? Array.Empty<RuntimeEvent>()).Take(17).ToArray();
        if (values.Length > 16) throw new ArgumentException("ResourceLimit"); Events = Array.AsReadOnly(values);
    }
    public MechanicalModeInput Input { get; }
    public ReadOnlyCollection<RuntimeEvent> Events { get; }
    internal string Key => Pack(Input.Key, Pack(Events.Select(e => e.Key).ToArray()));
}

public sealed class RuntimeRequest
{
    public RuntimeRequest(string id, string sessionId, string definitionId, string expectedStateId, BigInteger epoch, BigInteger revision, IEnumerable<RuntimeSegment> segments)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); SessionId = MechanicalAuthoringProfile.IdValue(sessionId);
        DefinitionId = definitionId; ExpectedStateId = expectedStateId;
        MechanicalRuntime.Counter(epoch); MechanicalRuntime.Counter(revision); Epoch = epoch; Revision = revision;
        var values = segments.Take(17).ToArray();
        if (values.Length == 0 || values.Length > 16 || values.Sum(s => s.Events.Count) > 16) throw new ArgumentException("ResourceLimit");
        Segments = Array.AsReadOnly(values);
        PayloadId = HashText(Pack(sessionId, definitionId, expectedStateId, MechanicalRuntime.Text(epoch), MechanicalRuntime.Text(revision), Pack(values.Select(s => s.Key).ToArray())));
    }
    public string Id { get; }
    public string SessionId { get; }
    public string DefinitionId { get; }
    public string ExpectedStateId { get; }
    public BigInteger Epoch { get; }
    public BigInteger Revision { get; }
    public string PayloadId { get; }
    public ReadOnlyCollection<RuntimeSegment> Segments { get; }
}

/// <summary>Only source q is stored. Restore re-evaluates it; no cached estimate is trusted.</summary>
public sealed class RuntimeWitness
{
    public RuntimeWitness(string latentId, Rational driverTurns) { LatentId = latentId; MechanicalAuthoringProfile.Number(driverTurns); DriverTurns = driverTurns; }
    public string LatentId { get; }
    public Rational DriverTurns { get; }
}

public sealed class RuntimeAffine
{
    public RuntimeAffine(Rational constant, IEnumerable<KeyValuePair<string, Rational>> terms)
    {
        CarrierProfile.Derived(constant); Constant = constant;
        var values = terms.Take(65).ToArray();
        if (values.Length > 64 || values.Select(t => t.Key).Distinct().Count() != values.Length || values.Any(t => t.Value.IsZero)) throw new ArgumentException("InvalidProvenance");
        foreach (var t in values) CarrierProfile.Derived(t.Value);
        Terms = new ReadOnlyDictionary<string, Rational>(values.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal));
    }
    public Rational Constant { get; }
    public IReadOnlyDictionary<string, Rational> Terms { get; }
    public static RuntimeAffine Of(ConnectedMotionValue value) => new(value.Constant, value.Terms.Select(t => new KeyValuePair<string, Rational>(t.Source.Id, t.Coefficient)));
    internal ConnectedMotionValue Resolve(IReadOnlyDictionary<string, ConnectedMotionValue> witnesses)
    {
        var value = ConnectedMotionValue.FromExact(Constant);
        foreach (var t in Terms)
        { if (!witnesses.TryGetValue(t.Key, out var original)) throw new ArgumentException("MissingProvenance"); value = value.Plus(original.Scale(t.Value, 0)); }
        return value;
    }
}

public sealed class RuntimeCaptureWitness
{
    public RuntimeCaptureWitness(Rational q, Rational sunNativeTurns) { MechanicalAuthoringProfile.Number(q); CarrierProfile.Derived(sunNativeTurns); DriverTurns = q; SunNativeTurns = sunNativeTurns; }
    public Rational DriverTurns { get; }
    public Rational SunNativeTurns { get; }
}

public sealed class RuntimeLockWitness
{
    public RuntimeLockWitness(Rational q, RuntimeAffine planetPort) { MechanicalAuthoringProfile.Number(q); DriverTurns = q; PlanetPort = planetPort; }
    public Rational DriverTurns { get; }
    public RuntimeAffine PlanetPort { get; }
}

public sealed class RuntimeLedgerEntry
{
    public RuntimeLedgerEntry(string requestId, string payloadId, string resultStateId, BigInteger revision, BigInteger cursor, IEnumerable<string> eventIds)
    {
        RequestId = MechanicalAuthoringProfile.IdValue(requestId); PayloadId = payloadId; ResultStateId = resultStateId;
        MechanicalRuntime.Counter(revision); MechanicalRuntime.Counter(cursor); Revision = revision; EventCursor = cursor;
        var ids = eventIds.Take(17).ToArray();
        if (ids.Length > 16 || ids.Distinct().Count() != ids.Length) throw new ArgumentException("InvalidLedger");
        foreach (var id in ids) MechanicalAuthoringProfile.IdValue(id); EventIds = Array.AsReadOnly(ids);
    }
    public string RequestId { get; }
    public string PayloadId { get; }
    public string ResultStateId { get; }
    public BigInteger Revision { get; }
    public BigInteger EventCursor { get; }
    public ReadOnlyCollection<string> EventIds { get; }
}

public sealed class RuntimeSnapshot
{
    internal RuntimeSnapshot(string sessionId, MechanicalModeDefinition definition, MechanicalModeState mechanical, BigInteger revision, BigInteger cursor, string history,
        IEnumerable<RuntimeWitness> witnesses, RuntimeCaptureWitness? capture, RuntimeLockWitness? locked, IEnumerable<RuntimeLedgerEntry> ledger)
    {
        SessionId = sessionId; Definition = definition; Mechanical = mechanical; Revision = revision; EventCursor = cursor; HistoryId = history;
        Witnesses = witnesses.OrderBy(w => w.LatentId, StringComparer.Ordinal).ToList().AsReadOnly(); CaptureWitness = capture; LockWitness = locked;
        StateId = HashText(Pack(MechanicalRuntime.Profile, sessionId, definition.DefinitionId, mechanical.Frame.SnapshotId, mechanical.Mode.ToString(), mechanical.CouplingOffset.Key,
            mechanical.LockReference?.Key ?? "", N(mechanical.AllowedDirection), MechanicalRuntime.Text(revision), MechanicalRuntime.Text(cursor), history));
        Ledger = ledger.TakeLast(16).ToList().AsReadOnly();
    }
    public string SessionId { get; }
    public MechanicalModeDefinition Definition { get; }
    public MechanicalModeState Mechanical { get; }
    public string StateId { get; }
    public string HistoryId { get; }
    public BigInteger Revision { get; }
    public BigInteger EventCursor { get; }
    public BigInteger Epoch => Revision / MechanicalRuntime.EpochRequests;
    public BigInteger StaleBeforeRevision => BigInteger.Max(0, Revision - 16);
    public ReadOnlyCollection<RuntimeWitness> Witnesses { get; }
    public RuntimeCaptureWitness? CaptureWitness { get; }
    public RuntimeLockWitness? LockWitness { get; }
    public ReadOnlyCollection<RuntimeLedgerEntry> Ledger { get; }
}

public sealed class RuntimeAdvance
{
    internal RuntimeAdvance(string status, RuntimeSnapshot before, RuntimeSnapshot? candidate = null, string? replayed = null)
    { Status = status; Before = before; Candidate = candidate; ReplayedStateId = replayed; }
    public string Status { get; }
    public RuntimeSnapshot Before { get; }
    public RuntimeSnapshot? Candidate { get; }
    public string? ReplayedStateId { get; }
    public bool IsAccepted => Status == "Accepted";
}

/// <summary>Pure candidate evaluation. The host owns publication. No clock, renderer, IO or global mutable session.</summary>
public sealed class MechanicalRuntime
{
    public const string Profile = "bounded-winding-runtime-checkpoint-v1";
    public const string Version = "1.0";
    public const int EpochRequests = 256;
    private readonly WindingDifferentialAnalysis analysis;
    public MechanicalRuntime(MechanicalModeDefinition definition)
    { Definition = definition; analysis = WindingDifferentialEngine.Prepare(definition.Connection); if (!analysis.IsValid) throw new ArgumentException("InvalidDefinition"); }
    public MechanicalModeDefinition Definition { get; }
    public static string Text(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
    public static void Counter(BigInteger value)
    { if (value < 0 || Text(value).Length > 128) throw new ArgumentException("CounterResourceLimit"); }
    public RuntimeSnapshot Start(string sessionId, Rational planetInput)
    {
        MechanicalAuthoringProfile.IdValue(sessionId); var s = MechanicalModeEngine.Start(Definition, planetInput);
        var witness = Witness(s.Frame.DriverTurns);
        return new(sessionId, Definition, s, 0, 0, HashText(Pack("runtime-start", sessionId, s.StateId)), new[] { witness }, null, null, Array.Empty<RuntimeLedgerEntry>());
    }
    private RuntimeWitness Witness(Rational q)
    { var f = SourceFrame(q); return new(f.Coordinates[Definition.Connection.Winding.OutputShaft.Id].Terms.Single().Source.Id, q); }
    private ConnectedFrame SourceFrame(Rational q)
    {
        var evaluated = WindingDifferentialEngine.Evaluate(analysis, new(q, 0));
        if (!evaluated.IsAccepted) throw new ArgumentException(evaluated.Status); return evaluated.Frame!;
    }
    public RuntimeAdvance Advance(RuntimeSnapshot before, RuntimeRequest request)
    {
        RuntimeAdvance Fail(string status) => new(status, before);
        if (before.Definition.DefinitionId != Definition.DefinitionId || request.DefinitionId != Definition.DefinitionId || request.SessionId != before.SessionId) return Fail("ForeignSnapshot");
        var known = before.Ledger.FirstOrDefault(e => e.RequestId == request.Id);
        if (known is not null) return known.PayloadId == request.PayloadId ? new("AlreadyApplied", before, replayed: known.ResultStateId) : Fail("IdempotencyConflict");
        if (request.Revision != before.Revision || request.Epoch != before.Epoch || request.ExpectedStateId != before.StateId) return Fail("StaleSnapshot");
        try
        {
            Counter(before.Revision + 1); Counter(before.EventCursor + request.Segments.Sum(s => s.Events.Count));
            var expected = before.EventCursor;
            var local = before.Mechanical.EventCursor;
            var segments = new List<MechanicalModeSegment>();
            foreach (var segment in request.Segments)
            {
                var events = new List<MechanicalConnectionEvent>();
                foreach (var e in segment.Events)
                { if (e.Sequence != ++expected) return Fail("StaleEventSequence"); events.Add(new(e.Id, ++local, e.Ordinal, e.Kind)); }
                segments.Add(new(segment.Input, events));
            }
            var r = new MechanicalModeRequest(request.Id, Definition.DefinitionId, before.Mechanical.StateId, before.Mechanical.Revision, segments);
            var next = MechanicalModeEngine.AdvancePrepared(Definition, before.Mechanical, r, analysis);
            if (!next.IsAccepted) return Fail(next.Status == "Motion provenance resource limit." ? "ProvenanceResourceLimit" : next.Status);
            var capture = before.CaptureWitness; var locked = before.LockWitness;
            var sampleIndex = 0; var s = Definition.Connection; var pd = s.Suffix.Parent.Definition;
            var previous = before.Mechanical;
            foreach (var segment in segments)
            {
                previous = next.Samples[sampleIndex++];
                foreach (var e in segment.Events)
                {
                    var current = next.Samples[sampleIndex++];
                    if (current.CouplingOffset.Key != previous.CouplingOffset.Key)
                    {
                        var sun = previous.Frame.Coordinates[pd.SunShaft.Id];
                        if (!sun.IsExact) throw new ArgumentException("UnresolvedCaptureWitness");
                        capture = new(previous.Frame.DriverTurns, sun.Exact!.Value);
                    }
                    locked = current.LockReference is null ? null : new RuntimeLockWitness(current.Frame.DriverTurns, RuntimeAffine.Of(current.Frame.Ports[s.PlanetPortId]));
                    previous = current;
                }
            }
            var end = next.State!; var live = LiveTerms(end, locked);
            var witnessMap = before.Witnesses.Concat(request.Segments.Select(seg => Witness(seg.Input.DriverTurns))).GroupBy(w => w.LatentId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (capture is not null) { var w = Witness(capture.DriverTurns); witnessMap[w.LatentId] = w; live.Add(w.LatentId); }
            if (locked is not null) { var w = Witness(locked.DriverTurns); witnessMap[w.LatentId] = w; live.Add(w.LatentId); }
            if (live.Count > 64) return Fail("ProvenanceResourceLimit");
            var witnesses = live.Select(id => witnessMap[id]).ToArray();
            var revision = before.Revision + 1;
            // Epoch-local counters are implementation details. Physical phase, source and lifetime counters never reset.
            if (revision % EpochRequests == 0)
                end = new(Definition, end.Frame, end.Mode, end.CouplingOffset, end.LockReference, end.AllowedDirection, 0, 0, end.HistoryId, end.Ledger);
            var history = HashText(Pack(before.HistoryId, request.Id, request.PayloadId));
            var snapshot = new RuntimeSnapshot(before.SessionId, Definition, end, revision, expected, history, witnesses, capture, locked, before.Ledger);
            var ledger = before.Ledger.Concat(new[] { new RuntimeLedgerEntry(request.Id, request.PayloadId, snapshot.StateId, revision, expected, request.Segments.SelectMany(seg => seg.Events).Select(e => e.Id)) });
            return new("Accepted", before, new(before.SessionId, Definition, end, revision, expected, history, witnesses, capture, locked, ledger));
        }
        catch (ArgumentException e) { return Fail(e.Message.Contains("resource limit") ? "ProvenanceResourceLimit" : e.Message); }
    }
    private static HashSet<string> LiveTerms(MechanicalModeState state, RuntimeLockWitness? locked)
    {
        var values = state.Frame.Coordinates.Values.Concat(state.Frame.Ports.Values).Concat(new[] { state.CouplingOffset });
        if (state.LockReference is not null) values = values.Concat(new[] { state.LockReference });
        var ids = new HashSet<string>(values.SelectMany(v => v.Terms).Select(t => t.Source.Id), StringComparer.Ordinal);
        if (locked is not null) ids.UnionWith(locked.PlanetPort.Terms.Keys); return ids;
    }

    /// <summary>Reconstruct current state against its source. Does NOT certify deleted historical requests.</summary>
    public RuntimeSnapshot Restore(string sessionId, BigInteger revision, BigInteger cursor, string history, MechanicalConnectionMode mode, int direction,
        Rational q, RuntimeAffine hSpec, RuntimeAffine? lockSpec, RuntimeAffine sunSpec, RuntimeAffine planetSpec,
        IEnumerable<RuntimeWitness> originals, RuntimeCaptureWitness? capture, RuntimeLockWitness? locked, IEnumerable<RuntimeLedgerEntry> recent)
    {
        MechanicalAuthoringProfile.IdValue(sessionId); Counter(revision); Counter(cursor);
        if (!Definition.AllowedModes.Contains(mode) || cursor > revision * 16) throw new ArgumentException("InvalidModeCursor");
        if (history.Length != 64 || history.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("InvalidHistoryIdentity");
        if (mode == MechanicalConnectionMode.DirectionRestrictedDrive ? direction != 1 && direction != -1 : direction != 0) throw new ArgumentException("InvalidDirection");
        var witnesses = originals.Take(65).ToArray();
        if (witnesses.Length > 64 || witnesses.Select(w => w.LatentId).Distinct().Count() != witnesses.Length) throw new ArgumentException("InvalidProvenance");
        var values = new Dictionary<string, ConnectedMotionValue>(StringComparer.Ordinal);
        foreach (var w in witnesses)
        {
            var value = SourceFrame(w.DriverTurns).Coordinates[Definition.Connection.Winding.OutputShaft.Id];
            if (value.Terms.Single().Source.Id != w.LatentId) throw new ArgumentException("ForeignLatentSource"); values.Add(w.LatentId, value);
        }
        var h = hSpec.Resolve(values); var l = lockSpec?.Resolve(values); var sun = sunSpec.Resolve(values); var planet = planetSpec.Resolve(values);
        var expectedH = capture is null ? ConnectedMotionValue.FromExact(Definition.Connection.InitialCouplingOffset) :
            ConnectedMotionValue.FromExact(capture.SunNativeTurns).Minus(SourceFrame(capture.DriverTurns).Coordinates[Definition.Connection.Winding.OutputShaft.Id]);
        if (h.Key != expectedH.Key) throw new ArgumentException("CaptureWitnessMismatch");
        if (capture is not null && FiniteWindingSolver.HasUnresolvedEventOrder(Definition.Connection.Winding.Geometry, ConnectedMotionValue.Number(capture.DriverTurns))) throw new ArgumentException("GuardIndeterminate");
        if (cursor.IsZero && (mode != MechanicalConnectionMode.DriveCapture || capture is not null || lockSpec is not null || direction != 0)) throw new ArgumentException("InvalidModeCursor");
        var lockedMode = mode == MechanicalConnectionMode.WorldCarrierLock || mode == MechanicalConnectionMode.PlanetRelativeLock;
        if (lockedMode != (locked is not null && l is not null) || !lockedMode && (locked is not null || l is not null)) throw new ArgumentException("LockWitnessMismatch");
        var current = WindingDifferentialEngine.EvaluateMode(analysis, q, (_, _) => (sun, planet));
        if (!current.IsAccepted) throw new ArgumentException(current.Status); var f = current.Frame!;
        var s = Definition.Connection; var pd = s.Suffix.Parent.Definition;
        if (mode != MechanicalConnectionMode.Released && sun.Minus(f.Coordinates[s.Winding.OutputShaft.Id]).Key != h.Key) throw new ArgumentException("ActiveCouplingMismatch");
        if (lockedMode)
        {
            if (FiniteWindingSolver.HasUnresolvedEventOrder(s.Winding.Geometry, ConnectedMotionValue.Number(locked!.DriverTurns))) throw new ArgumentException("GuardIndeterminate");
            var witnessFrame = WindingDifferentialEngine.EvaluateMode(analysis, locked.DriverTurns, (w, _) => (w.Plus(h), locked.PlanetPort.Resolve(values)));
            if (!witnessFrame.IsAccepted) throw new ArgumentException(witnessFrame.Status);
            ConnectedMotionValue LockValue(ConnectedFrame frame) => mode == MechanicalConnectionMode.WorldCarrierLock ? frame.Coordinates[pd.CarrierShaft.Id] :
                ConnectedMotionValue.Apply(MechanicalModeEngine.RelativeLaw(analysis), frame.Ports.Where(p => analysis.Suffix.Parent.Request.InputPortIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
            if (LockValue(witnessFrame.Frame!).Key != l!.Key || LockValue(f).Key != l.Key) throw new ArgumentException("ActiveLockMismatch");
        }
        var ledger = recent.Take(17).ToArray();
        if (ledger.Length != (int)BigInteger.Min(16, revision) || ledger.Select(e => e.RequestId).Distinct().Count() != ledger.Length || ledger.SelectMany(e => e.EventIds).Distinct().Count() != ledger.Sum(e => e.EventIds.Count)) throw new ArgumentException("InvalidLedger");
        for (var i = 0; i < ledger.Length; i++)
        {
            var e = ledger[i];
            if (e.Revision != revision - ledger.Length + i + 1 || e.EventCursor > cursor || e.EventCursor < e.EventIds.Count || i > 0 && e.EventCursor != ledger[i-1].EventCursor + e.EventIds.Count || new[] { e.PayloadId, e.ResultStateId }.Any(id => id.Length != 64 || id.Any(c => !Uri.IsHexDigit(c)))) throw new ArgumentException("InvalidLedgerCursor");
        }
        if (ledger.Length > 0 && ledger.Last().EventCursor != cursor) throw new ArgumentException("InvalidLedgerCursor");
        var localLedger = ledger.Select(e => new MechanicalModeLedgerEntry(e.RequestId, e.PayloadId, e.ResultStateId, e.EventIds));
        var mechanical = new MechanicalModeState(Definition, f, mode, h, l, direction, (long)(revision % EpochRequests), 0, history, localLedger);
        var state = new RuntimeSnapshot(sessionId, Definition, mechanical, revision, cursor, history, witnesses, capture, locked, ledger);
        if (ledger.Length > 0 && ledger.Last().ResultStateId != state.StateId) throw new ArgumentException("LedgerStateMismatch");
        var live = LiveTerms(mechanical, locked);
        if (capture is not null) live.Add(Witness(capture.DriverTurns).LatentId);
        if (locked is not null) live.Add(Witness(locked.DriverTurns).LatentId);
        if (!live.SetEquals(witnesses.Select(w => w.LatentId))) throw new ArgumentException("UnreferencedProvenance");
        return state;
    }
}
