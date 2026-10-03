using System;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest;

/// <summary>Transport-only JSON bridge, also usable by a CLI. No browser, UI, file, or network dependency.</summary>
public sealed class MechanicalRuntimeHost : IDisposable
{
    private readonly GearInvestSdk sdk;
    private MechanicalRuntimeSession? session;
    private RuntimeAdvance? pending;
    private string? pendingToken;
    private string? lastCommittedToken;
    private string? lastCommittedState;
    private bool disposed;
    public MechanicalRuntimeHost(GearInvestSdk sdk) { this.sdk = sdk; }
    public string Dispatch(string commandJson)
    {
        try
        {
            if (disposed) return Error("Disposed");
            if (Encoding.UTF8.GetByteCount(commandJson) > WindingConnectionJson.MaxBytes) return Error("ResourceLimit");
            using var doc = JsonDocument.Parse(commandJson, new JsonDocumentOptions { MaxDepth = 40 }); var p = doc.RootElement;
            var op = p.GetProperty("op").GetString();
            switch (op)
            {
                case "load":
                {
                    var raw = Encoding.UTF8.GetBytes(p.GetProperty("sourceUtf8").GetString()!);
                    var initial = Core.Rational.Parse(p.GetProperty("initialPlanet").GetString()!);
                    var next = sdk.PrepareMechanicalRuntime(raw, p.GetProperty("sessionId").GetString()!, initial);
                    Replace(next); return Snapshot("Ready", true);
                }
                case "restore":
                {
                    var next = sdk.RestoreMechanicalRuntime(Encoding.UTF8.GetBytes(p.GetProperty("checkpointUtf8").GetString()!));
                    Replace(next); return Snapshot("Restored", true);
                }
                case "importRecording":
                {
                    var next = sdk.ImportMechanicalRecording(Encoding.UTF8.GetBytes(p.GetProperty("recordingUtf8").GetString()!), p.GetProperty("sessionId").GetString()!);
                    Replace(next); return Snapshot("ImportedRecording", true);
                }
                case "snapshot": return Snapshot("Current", true);
                case "prepare":
                {
                    if (session is null) return Error("NotLoaded"); if (pending is not null) return Error("Busy");
                    var request = MechanicalRuntimeJson.ReadRequest(Encoding.UTF8.GetBytes(p.GetProperty("request").GetRawText()));
                    var result = session.Prepare(request);
                    if (!result.IsAccepted) return Json(new { status = result.Status, lastValidStateId = session.Current.StateId, replayedStateId = result.ReplayedStateId, committed = false });
                    pending = result; pendingToken = request.PayloadId;
                    return Json(new { status = "Prepared", token = pendingToken, expectedStateId = session.Current.StateId, candidateStateId = result.Candidate!.StateId, committed = false });
                }
                case "commit":
                {
                    var token = Token(p);
                    if (pending is null || token != pendingToken) return token == lastCommittedToken ? Snapshot("AlreadyCommitted") : Error("NoPreparedRequest");
                    var status = session!.Commit(pending); pending = null; pendingToken = null;
                    if (status != "Accepted") return Error(status);
                    lastCommittedToken = token; lastCommittedState = session.Current.StateId; return Snapshot("Accepted");
                }
                case "cancel":
                {
                    var token = Token(p);
                    if (pending is not null && token == pendingToken) { pending = null; pendingToken = null; return Json(new { status = "CancelledBeforeCommit", committed = false, lastValidStateId = session!.Current.StateId }); }
                    if (token == lastCommittedToken) return Json(new { status = "TooLateCommitted", committed = true, stateId = lastCommittedState });
                    return Error("NoPreparedRequest");
                }
                case "checkpoint":
                {
                    if (session is null) return Error("NotLoaded"); if (pending is not null) return Error("Busy");
                    var checkpoint = session.Checkpoint(); return Json(new { status = "CheckpointCreated", artifactId = checkpoint.ArtifactId, checkpointUtf8 = Encoding.UTF8.GetString(checkpoint.Bytes), stateId = session.Current.StateId });
                }
                case "dispose": Dispose(); return Json(new { status = "Disposed" });
                default: return Error("UnsupportedOperation");
            }
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is FormatException || e is JsonException || e is KeyNotFoundException || e is OverflowException)
        { return Json(new { status = FailureCategory(e.Message), detail = e.Message, committed = false, lastValidStateId = session?.Current.StateId }); }
    }
    private static string Token(JsonElement command)
    {
        var value = command.GetProperty("token").GetString();
        if (value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("InvalidTransactionToken");
        return value;
    }
    private static string FailureCategory(string message)
    {
        if (message.IndexOf("Unsupported", StringComparison.OrdinalIgnoreCase) >= 0) return "UnsupportedProfile";
        if (message.IndexOf("ResourceLimit", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("resource limit", StringComparison.OrdinalIgnoreCase) >= 0) return "ResourceLimit";
        return "InvalidInput";
    }
    private void Replace(MechanicalRuntimeSession next)
    {
        session?.Dispose(); session = next; pending = null; pendingToken = null; lastCommittedToken = null; lastCommittedState = null;
    }
    private string Snapshot(string status, bool scene = false)
    {
        if (session is null) return Error("NotLoaded");
        using var state = JsonDocument.Parse(session.Snapshot(scene));
        var d = session.Current.Definition; var g = d.Connection.Winding.Geometry;
        return Json(new { status, committed = status == "Accepted" || status == "AlreadyCommitted", snapshot = state.RootElement,
            capabilities = new { profile = MechanicalRuntime.Profile, version = MechanicalRuntime.Version, modes = d.AllowedModes.Select(m => m.ToString()).ToArray(),
                sunPort = d.Connection.SunPortId, planetPort = d.Connection.PlanetPortId,
                driverMinimumTurns = g.DriverMinimumTurns, driverMaximumTurns = g.DriverMaximumTurns,
                currentStateValidation = "source-rebuilt", deletedHistoryValidation = "notPerformed", numericQuality = "NumericResidualOnly", solutionErrorBoundTurns = (double?)null,
                activeProvenanceLimit = 64, retryLedgerLimit = 16, epochRequests = MechanicalRuntime.EpochRequests,
                notPerformed = new[] { "tooth-solids", "swept-solids", "dynamics", "3d-winding", "general-gear-network" } } });
    }
    private static string Error(string status) => Json(new { status, committed = false });
    private static string Json(object value) => JsonSerializer.Serialize(value);
    public void Dispose() { if (disposed) return; session?.Dispose(); session = null; pending = null; disposed = true; }
}
