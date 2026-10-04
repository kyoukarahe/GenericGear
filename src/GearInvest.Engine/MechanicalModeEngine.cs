using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class MechanicalModeEngine
{
    public static MechanicalModeState Start(MechanicalModeDefinition definition,Rational initialPlanetPortTurns)
    {
        var a=WindingDifferentialEngine.Prepare(definition.Connection);if(!a.IsValid)throw new ArgumentException(string.Join(",",a.Diagnostics));
        var first=WindingDifferentialEngine.Evaluate(a,new(definition.Connection.WindingSource.InitialDriverTurns,initialPlanetPortTurns));if(!first.IsAccepted)throw new ArgumentException(first.Status);
        return new(definition,first.Frame!,MechanicalConnectionMode.DriveCapture,ConnectedMotionValue.FromExact(definition.Connection.InitialCouplingOffset),null,0,0,0,
            HashText(Pack("initial",definition.DefinitionId,F(initialPlanetPortTurns))),Array.Empty<MechanicalModeLedgerEntry>());
    }
    public static string[] RequiredInputPorts(MechanicalModeDefinition d,MechanicalConnectionMode mode)=>mode==MechanicalConnectionMode.Released?
        new[]{d.Connection.SunPortId,d.Connection.PlanetPortId}.OrderBy(x=>x,StringComparer.Ordinal).ToArray():
        mode==MechanicalConnectionMode.DriveCapture||mode==MechanicalConnectionMode.DirectionRestrictedDrive?new[]{d.Connection.PlanetPortId}:Array.Empty<string>();

    public static MechanicalModeAdvance Advance(MechanicalModeDefinition d,MechanicalModeState state,MechanicalModeRequest request)
        => AdvancePrepared(d,state,request,null);

    // Only an admitted immutable runtime session supplies this analysis. The legacy entry still prepares each call.
    internal static MechanicalModeAdvance AdvancePrepared(MechanicalModeDefinition d,MechanicalModeState state,MechanicalModeRequest request,WindingDifferentialAnalysis? prepared)
    {
        MechanicalModeAdvance Fail(string status)=>new(status,state,request);
        if(state.DefinitionId!=d.DefinitionId||request.SourceId!=d.DefinitionId)return Fail("ForeignSnapshot");
        var previous=state.Ledger.FirstOrDefault(e=>e.RequestId==request.Id);
        if(previous is not null)return previous.PayloadId==request.PayloadId?new("AlreadyApplied",state,request,replayedStateId:previous.ResultStateId):Fail("IdempotencyConflict");
        if(request.ExpectedRevision!=state.Revision||request.ExpectedStateId!=state.StateId)return Fail("StaleSnapshot");
        if(state.Revision>=4096)return Fail("ResourceLimit");
        var a=prepared??WindingDifferentialEngine.Prepare(d.Connection);if(!a.IsValid||a.Source.DefinitionId!=d.Connection.DefinitionId)return Fail("InvalidDefinition");
        var current=state;var samples=new List<MechanicalModeState>();var ids=new HashSet<string>(state.Ledger.SelectMany(e=>e.EventIds),StringComparer.Ordinal);
        foreach(var segment in request.Segments)
        {
            var needed=RequiredInputPorts(d,current.Mode);var actual=segment.Input.IndependentPorts.Keys.ToArray();
            if(actual.Except(needed).Any())return Fail("ModeInputOwnershipConflict");
            if(needed.Except(actual).Any())return Fail("Underdetermined");
            if(segment.Input.Observations.Keys.Except(d.Connection.Suffix.Parent.Definition.Ports.Select(p=>p.Id).Concat(new[]{d.Connection.Suffix.OutputPort.Id}).Concat(d.Connection.Transmission?.Stages.Select(s=>s.OutputPort.Id)??Array.Empty<string>())).Any())return Fail("InvalidReference");
            if(current.Mode==MechanicalConnectionMode.DirectionRestrictedDrive && (segment.Input.DriverTurns-current.Frame.DriverTurns)*current.AllowedDirection<0)return Fail("DirectionConflict");
            // DirectionRestrictedDrive constrains the declared unwrapped DRIVER coordinate above.
            // Validate the chosen source's path; do not assume a spatial passive rotor is monotone.
            var error=ConnectedWindingKinematics.ValidateSegment(d.Connection.WindingSource,ConnectedMotionValue.Number(current.Frame.DriverTurns),ConnectedMotionValue.Number(segment.Input.DriverTurns),a);
            if(error is not null)return Fail(error);
            var next=Evaluate(a,current,segment.Input);if(!next.IsAccepted)return Fail(next.Status);
            if(segment.Events.Count>0&&ConnectedWindingKinematics.UnresolvedEvent(d.Connection.WindingSource,ConnectedMotionValue.Number(segment.Input.DriverTurns),a))return Fail("GuardIndeterminate");
            foreach(var observation in segment.Input.Observations)
            {
                var difference=next.Frame!.Ports[observation.Key].Minus(ConnectedMotionValue.FromExact(observation.Value));
                if(!difference.IsExact)return Fail("GuardIndeterminate");if(difference.Exact!=Rational.Zero)return Fail("InconsistentObservation");
            }
            current=New(current,next.Frame!,current.Mode,current.CouplingOffset,current.LockReference,current.AllowedDirection,current.EventCursor,"arrival");samples.Add(current);
            for(var ordinal=0;ordinal<segment.Events.Count;ordinal++)
            {
                var e=segment.Events[ordinal];if(e.Ordinal!=ordinal)return Fail("InvalidEventOrder");if(e.Sequence!=current.EventCursor+1)return Fail("StaleEventSequence");if(!ids.Add(e.Id))return Fail("DuplicateEventIdentity");
                var mode=current.Mode;var h=current.CouplingOffset;ConnectedMotionValue? locked=null;var direction=0;
                var s=d.Connection;var pd=s.Suffix.Parent.Definition;
                switch(e.Kind)
                {
                    case MechanicalConnectionEventKind.Release:mode=MechanicalConnectionMode.Released;break;
                    case MechanicalConnectionEventKind.AlignCapture:
                    {
                        var delta=current.Frame.Coordinates[pd.SunShaft.Id].Minus(current.Frame.Coordinates[s.CouplingShaftId]).Scale(1,-d.AlignmentOffset);
                        if(!delta.IsExact)return Fail("GuardIndeterminate");if(delta.Exact!=Rational.Zero)return Fail("AlignmentConflict");
                        h=ConnectedMotionValue.FromExact(d.AlignmentOffset);mode=MechanicalConnectionMode.DriveCapture;break;
                    }
                    case MechanicalConnectionEventKind.Capture:
                    case MechanicalConnectionEventKind.CapturePositive:
                    case MechanicalConnectionEventKind.CaptureNegative:
                        h=current.Frame.Coordinates[pd.SunShaft.Id].Minus(current.Frame.Coordinates[s.CouplingShaftId]);
                        direction=e.Kind==MechanicalConnectionEventKind.CapturePositive?1:e.Kind==MechanicalConnectionEventKind.CaptureNegative?-1:0;
                        mode=direction==0?MechanicalConnectionMode.DriveCapture:MechanicalConnectionMode.DirectionRestrictedDrive;break;
                    case MechanicalConnectionEventKind.LockWorldCarrier:
                        if(current.Mode==MechanicalConnectionMode.Released)return Fail("MissingCouplingBoundary");
                        mode=MechanicalConnectionMode.WorldCarrierLock;locked=current.Frame.Coordinates[pd.CarrierShaft.Id];break;
                    case MechanicalConnectionEventKind.LockPlanetRelative:
                        if(current.Mode==MechanicalConnectionMode.Released)return Fail("MissingCouplingBoundary");
                        mode=MechanicalConnectionMode.PlanetRelativeLock;locked=ConnectedMotionValue.Apply(RelativeLaw(a),current.Frame.Ports.Where(p=>a.Suffix.Parent.Request.InputPortIds.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal));break;
                    default:return Fail("UnsupportedProfile");
                }
                if(!d.AllowedModes.Contains(mode))return Fail("UnsupportedMode");
                current=New(current,current.Frame,mode,h,locked,direction,e.Sequence,e.Key);samples.Add(current);
            }
        }
        var history=HashText(Pack(state.HistoryId,request.Id,request.PayloadId));
        var committed=new MechanicalModeState(d,current.Frame,current.Mode,current.CouplingOffset,current.LockReference,current.AllowedDirection,state.Revision+1,current.EventCursor,history,state.Ledger);
        var ledger=state.Ledger.Concat(new[]{new MechanicalModeLedgerEntry(request.Id,request.PayloadId,committed.StateId,request.Segments.SelectMany(s=>s.Events).Select(e=>e.Id))});
        committed=new(d,committed.Frame,committed.Mode,committed.CouplingOffset,committed.LockReference,committed.AllowedDirection,committed.Revision,committed.EventCursor,history,ledger);
        return new("Accepted",state,request,committed,samples);

        MechanicalModeState New(MechanicalModeState old,ConnectedFrame frame,MechanicalConnectionMode mode,ConnectedMotionValue h,ConnectedMotionValue? locked,int direction,long cursor,string step)=>
            new(d,frame,mode,h,locked,direction,state.Revision+1,cursor,HashText(Pack(old.HistoryId,request.Id,request.PayloadId,step)),state.Ledger);
    }

    internal static DifferentialLaw RelativeLaw(WindingDifferentialAnalysis a)
    {
        var d=a.Source.Suffix.Parent.Definition;var ep=d.PlanetShaft.FrameInCarrier.Z.Z;
        return a.Suffix.Parent.PlanetRelative!.Then(ep,0);
    }
    private static ConnectedEvaluation Evaluate(WindingDifferentialAnalysis a,MechanicalModeState state,MechanicalModeInput input)=>
        WindingDifferentialEngine.EvaluateMode(a,input.DriverTurns,(w,s)=>
        {
            var pd=s.Suffix.Parent.Definition;var sunPort=pd.Ports.Single(p=>p.Id==s.SunPortId);var planetPort=pd.Ports.Single(p=>p.Id==s.PlanetPortId);
            if(state.Mode==MechanicalConnectionMode.Released)
                return (ConnectedMotionValue.FromExact((input.IndependentPorts[s.SunPortId]-sunPort.ReadoutOffset.Value)/sunPort.FrameInShaft.Z.Z),ConnectedMotionValue.FromExact(input.IndependentPorts[s.PlanetPortId]));
            var sun=w.Plus(state.CouplingOffset);
            if(state.Mode==MechanicalConnectionMode.DriveCapture||state.Mode==MechanicalConnectionMode.DirectionRestrictedDrive)return(sun,ConnectedMotionValue.FromExact(input.IndependentPorts[s.PlanetPortId]));
            var law=state.Mode==MechanicalConnectionMode.WorldCarrierLock?a.Suffix.Parent.Coordinates.Single(c=>c.ShaftId==pd.CarrierShaft.Id).Law!:RelativeLaw(a);
            var sunReadout=sun.Scale(sunPort.FrameInShaft.Z.Z,sunPort.ReadoutOffset.Value);
            var coefficient=law.Coefficients[s.PlanetPortId];if(coefficient.IsZero)throw new ArgumentException("Underdetermined");
            var planetReadout=state.LockReference!.Minus(sunReadout.Scale(law.Coefficients[s.SunPortId],law.Offset)).Scale(1/coefficient,0);
            return(sun,planetReadout);
        });
}
