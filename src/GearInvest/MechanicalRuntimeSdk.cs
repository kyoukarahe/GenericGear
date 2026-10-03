using System;
using System.Linq;
using System.IO;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest;

/// <summary>Single-writer prepared source. Prepare does not publish; Commit is the sole state mutation.</summary>
public sealed class MechanicalRuntimeSession : IDisposable
{
    private readonly MechanicalRuntime runtime;
    private readonly object gate = new();
    private bool disposed;
    internal MechanicalRuntimeSession(WindingConnectionArtifact source, RuntimeSnapshot state)
    { Source = source; Current = state; runtime = new(state.Definition); }
    public WindingConnectionArtifact Source { get; }
    public RuntimeSnapshot Current { get; private set; }
    public RuntimeAdvance Prepare(RuntimeRequest request) { lock (gate) { Alive(); return runtime.Advance(Current, request); } }
    public string Commit(RuntimeAdvance candidate)
    {
        lock (gate)
        {
            Alive(); if (!ReferenceEquals(candidate.Before, Current)) return "StaleSnapshot";
            if (candidate.IsAccepted) Current = candidate.Candidate!; return candidate.Status;
        }
    }
    public RuntimeAdvance Advance(RuntimeRequest request) { lock (gate) { var r = Prepare(request); _ = Commit(r); return r; } }
    public MechanicalCheckpointArtifact Checkpoint() { lock (gate) { Alive(); return MechanicalRuntimeJson.Write(Source, Current); } }
    public byte[] Snapshot(bool includeScene = false) { lock (gate) { Alive(); return MechanicalRuntimeJson.Snapshot(Source, Current, includeScene); } }
    public void Dispose() { lock (gate) disposed = true; }
    private void Alive() { if (disposed) throw new ObjectDisposedException(nameof(MechanicalRuntimeSession)); }
}

public sealed partial class GearInvestSdk
{
    /// <summary>Single-writer filesystem CAS. An incomplete pending file never replaces the last complete checkpoint.</summary>
    public string SaveMechanicalCheckpoint(MechanicalCheckpointArtifact checkpoint, string path, string? expectedArtifactId)
    {
        var validated = MechanicalRuntimeJson.Read(checkpoint.Bytes);
        _ = ReadWindingConnectionArtifact(validated.Source.Bytes);
        var target = Path.GetFullPath(path);
        using var writerLock = new FileStream(target + ".writer-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var exists = File.Exists(target);
        var current = exists ? MechanicalRuntimeJson.Read(PortableProjectStorage.ReadBounded(target, WindingConnectionJson.MaxBytes)).ArtifactId : null;
        if (current != expectedArtifactId) throw new InvalidOperationException("CheckpointWriteConflict");
        using (var pending = new FileStream(target + ".pending", FileMode.Create, FileAccess.Write, FileShare.None))
        { var bytes = validated.Bytes; pending.Write(bytes, 0, bytes.Length); pending.Flush(true); }
        if (exists) File.Replace(target + ".pending", target, null); else File.Move(target + ".pending", target);
        return validated.ArtifactId;
    }
    public MechanicalRuntimeSession LoadMechanicalCheckpoint(string path) => RestoreMechanicalRuntime(PortableProjectStorage.ReadBounded(path, WindingConnectionJson.MaxBytes));
    public MechanicalRuntimeSession PrepareMechanicalRuntime(byte[] sourceBytes, string sessionId, Rational initialPlanetPortTurns, MechanicalModeDefinition? policy = null)
    {
        var source = ReadWindingConnectionArtifact(sourceBytes); var d = policy ?? new MechanicalModeDefinition(source.Source);
        if (d.Connection.DefinitionId != source.Source.DefinitionId) throw new ArgumentException("ForeignPolicy");
        return new(source, new MechanicalRuntime(d).Start(sessionId, initialPlanetPortTurns));
    }
    public MechanicalRuntimeSession RestoreMechanicalRuntime(byte[] checkpointBytes)
    {
        var checkpoint = MechanicalRuntimeJson.Read(checkpointBytes);
        _ = ReadWindingConnectionArtifact(checkpoint.Source.Bytes); return new(checkpoint.Source, checkpoint.Snapshot);
    }
    /// <summary>Explicit bounded recording migration. Original bytes/IDs are never rewritten.</summary>
    public MechanicalRuntimeSession ImportMechanicalRecording(byte[] recordingBytes, string sessionId)
    {
        var old = ReadMechanicalModeRecording(recordingBytes); var r = old.Recording;
        var session = PrepareMechanicalRuntime(old.Source.Bytes, sessionId, r.InitialPlanetPortTurns, r.Definition);
        foreach (var attempt in r.Attempts.Where(a => a.IsAccepted))
        {
            var state = session.Current; var cursor = state.EventCursor;
            var request = new RuntimeRequest(attempt.Request.Id, sessionId, r.Definition.DefinitionId, state.StateId, state.Epoch, state.Revision,
                attempt.Request.Segments.Select(s => new RuntimeSegment(s.Input, s.Events.Select(e => new RuntimeEvent(e.Id, ++cursor, e.Ordinal, e.Kind)))));
            var result = session.Advance(request); if (!result.IsAccepted) { session.Dispose(); throw new ArgumentException(result.Status); }
        }
        return session;
    }
}
