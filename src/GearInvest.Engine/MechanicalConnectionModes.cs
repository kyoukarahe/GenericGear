using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public enum MechanicalConnectionMode { DriveCapture, Released, WorldCarrierLock, PlanetRelativeLock, DirectionRestrictedDrive }
public enum MechanicalConnectionEventKind { Release, Capture, AlignCapture, LockWorldCarrier, LockPlanetRelative, CapturePositive, CaptureNegative }

/// <summary>Immutable mode policy around the same connected source; original contact constants never change.</summary>
public sealed class MechanicalModeDefinition
{
    public const string Profile="bounded-winding-mechanical-modes-v1";
    public MechanicalModeDefinition(WindingDifferentialDefinition connection,IEnumerable<MechanicalConnectionMode>? allowedModes=null,Rational? alignmentOffset=null)
    {
        Connection=connection;var modes=(allowedModes??(MechanicalConnectionMode[])Enum.GetValues(typeof(MechanicalConnectionMode))).Take(6).ToArray();
        if(modes.Length>5||modes.Any(m=>!Enum.IsDefined(typeof(MechanicalConnectionMode),m))||modes.Distinct().Count()!=modes.Length||!modes.Contains(MechanicalConnectionMode.DriveCapture))throw new ArgumentException("Invalid explicit mode policy.");
        AllowedModes=modes.OrderBy(x=>x.ToString(),StringComparer.Ordinal).ToList().AsReadOnly();AlignmentOffset=alignmentOffset??Rational.Zero;MechanicalAuthoringProfile.Number(AlignmentOffset);
        DefinitionId=HashText(Pack(Profile,connection.DefinitionId,Pack(AllowedModes.Select(x=>x.ToString()).ToArray()),F(AlignmentOffset)));
    }
    public WindingDifferentialDefinition Connection{get;}
    public ReadOnlyCollection<MechanicalConnectionMode> AllowedModes{get;}
    public Rational AlignmentOffset{get;}
    public string DefinitionId{get;}
}

public sealed class MechanicalModeInput
{
    public MechanicalModeInput(Rational driverTurns,IEnumerable<KeyValuePair<string,Rational>> independentPorts,IEnumerable<KeyValuePair<string,Rational>>? observations=null)
    {
        MechanicalAuthoringProfile.Number(driverTurns);DriverTurns=driverTurns;
        ReadOnlyDictionary<string,Rational> Set(IEnumerable<KeyValuePair<string,Rational>> values)
        {var a=DifferentialProfile.Set(values,p=>MechanicalAuthoringProfile.IdValue(p.Key),6);foreach(var p in a)MechanicalAuthoringProfile.Number(p.Value);return new(a.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal));}
        IndependentPorts=Set(independentPorts);Observations=Set(observations??Array.Empty<KeyValuePair<string,Rational>>());
    }
    public Rational DriverTurns{get;}
    public IReadOnlyDictionary<string,Rational> IndependentPorts{get;}
    public IReadOnlyDictionary<string,Rational> Observations{get;}
    internal string Key=>Pack(F(DriverTurns),Pack(IndependentPorts.Select(p=>Pack(p.Key,F(p.Value))).ToArray()),Pack(Observations.Select(p=>Pack(p.Key,F(p.Value))).ToArray()));
}

public sealed class MechanicalConnectionEvent
{
    public MechanicalConnectionEvent(string id,long sequence,int ordinal,MechanicalConnectionEventKind kind)
    {Id=MechanicalAuthoringProfile.IdValue(id);if(sequence<1||ordinal<0||ordinal>15||!Enum.IsDefined(typeof(MechanicalConnectionEventKind),kind))throw new ArgumentException("Invalid event declaration.");Sequence=sequence;Ordinal=ordinal;Kind=kind;}
    public string Id{get;}
    public long Sequence{get;}
    public int Ordinal{get;}
    public MechanicalConnectionEventKind Kind{get;}
    internal string Key=>Pack(Id,Sequence.ToString(CultureInfo.InvariantCulture),N(Ordinal),Kind.ToString());
}

/// <summary>Reach endpoint in the old mode, then events in explicitly authored ordinal order.</summary>
public sealed class MechanicalModeSegment
{
    public MechanicalModeSegment(MechanicalModeInput input,IEnumerable<MechanicalConnectionEvent>? events=null)
    {Input=input;var a=(events??Array.Empty<MechanicalConnectionEvent>()).Take(17).ToArray();if(a.Length>16)throw new ArgumentException("Event resource limit.");Events=Array.AsReadOnly(a);}
    public MechanicalModeInput Input{get;}
    public ReadOnlyCollection<MechanicalConnectionEvent> Events{get;}
    internal string Key=>Pack(Input.Key,Pack(Events.Select(e=>e.Key).ToArray()));
}

public sealed class MechanicalModeRequest
{
    public MechanicalModeRequest(string id,string sourceId,string expectedStateId,long expectedRevision,IEnumerable<MechanicalModeSegment> segments)
    {
        Id=MechanicalAuthoringProfile.IdValue(id);SourceId=sourceId;ExpectedStateId=expectedStateId;ExpectedRevision=expectedRevision;
        var a=segments.Take(17).ToArray();if(a.Length==0||a.Length>16||a.Sum(s=>s.Events.Count)>16||expectedRevision<0)throw new ArgumentException("Mode request resource limit.");Segments=Array.AsReadOnly(a);
        PayloadId=HashText(Pack(sourceId,expectedStateId,expectedRevision.ToString(CultureInfo.InvariantCulture),Pack(a.Select(s=>s.Key).ToArray())));
    }
    public string Id{get;}
    public string SourceId{get;}
    public string ExpectedStateId{get;}
    public long ExpectedRevision{get;}
    public ReadOnlyCollection<MechanicalModeSegment> Segments{get;}
    public string PayloadId{get;}
}

public sealed class MechanicalModeLedgerEntry
{
    internal MechanicalModeLedgerEntry(string id,string payload,string result,IEnumerable<string> events){RequestId=id;PayloadId=payload;ResultStateId=result;EventIds=events.ToList().AsReadOnly();}
    public string RequestId{get;}
    public string PayloadId{get;}
    public string ResultStateId{get;}
    public ReadOnlyCollection<string> EventIds{get;}
}

public sealed class MechanicalModeState
{
    internal MechanicalModeState(MechanicalModeDefinition d,ConnectedFrame frame,MechanicalConnectionMode mode,ConnectedMotionValue h,ConnectedMotionValue? locked,int direction,long revision,long cursor,string history,IEnumerable<MechanicalModeLedgerEntry> ledger)
    {
        DefinitionId=d.DefinitionId;Frame=frame;Mode=mode;CouplingOffset=h;LockReference=locked;AllowedDirection=direction;Revision=revision;EventCursor=cursor;HistoryId=history;
        StateId=HashText(Pack(d.DefinitionId,frame.SnapshotId,mode.ToString(),h.Key,locked?.Key??"",N(direction),revision.ToString(CultureInfo.InvariantCulture),cursor.ToString(CultureInfo.InvariantCulture),history));
        Ledger=ledger.TakeLast(16).ToList().AsReadOnly();
    }
    public string DefinitionId{get;}
    public string StateId{get;}
    public string HistoryId{get;}
    public ConnectedFrame Frame{get;}
    public MechanicalConnectionMode Mode{get;}
    public ConnectedMotionValue CouplingOffset{get;}
    public ConnectedMotionValue? LockReference{get;}
    public int AllowedDirection{get;}
    public long Revision{get;}
    public long EventCursor{get;}
    public ReadOnlyCollection<MechanicalModeLedgerEntry> Ledger{get;}
}

public sealed class MechanicalModeAdvance
{
    internal MechanicalModeAdvance(string status,MechanicalModeState old,MechanicalModeRequest request,MechanicalModeState? state=null,IEnumerable<MechanicalModeState>? samples=null,string? replayedStateId=null)
    {Status=status;LastValidSnapshot=old;Request=request;State=state;Samples=(samples??Array.Empty<MechanicalModeState>()).ToList().AsReadOnly();ReplayedStateId=replayedStateId;}
    public string Status{get;}
    public MechanicalModeState LastValidSnapshot{get;}
    public MechanicalModeRequest Request{get;}
    public MechanicalModeState? State{get;}
    public ReadOnlyCollection<MechanicalModeState> Samples{get;}
    public string? ReplayedStateId{get;}
    public bool IsAccepted=>Status=="Accepted";
    public int AppliedSegments=>IsAccepted?Request.Segments.Count:0;
    public ReadOnlyCollection<MechanicalModeSegment> Remainder=>IsAccepted||Status=="AlreadyApplied"?Array.Empty<MechanicalModeSegment>().ToList().AsReadOnly():Request.Segments;
}
