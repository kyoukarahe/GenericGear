using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest.WindingExample;

public static class ModeExample
{
    public static MechanicalModeRecordingArtifact Create(GearInvestSdk sdk,WindingConnectionArtifact connection)
    {
        var definition=new MechanicalModeDefinition(connection.Source);var state=sdk.StartMechanicalModes(definition,new Rational(3,10));var requests=new List<MechanicalModeRequest>();
        var sun=connection.Source.SunPortId;var planet=connection.Source.PlanetPortId;
        Dictionary<string,Rational> P(Rational p)=>new(){{planet,p}};
        Dictionary<string,Rational> SP(Rational s,Rational p)=>new(){{sun,s},{planet,p}};
        MechanicalModeAdvance Step(string id,Rational q,Dictionary<string,Rational> inputs,string expected="Accepted",params MechanicalConnectionEventKind[] events)
        {
            var segment=new MechanicalModeSegment(new(q,inputs),events.Select((e,i)=>new MechanicalConnectionEvent(id+"/event-"+i,state.EventCursor+i+1,i,e)));
            var request=new MechanicalModeRequest(id,definition.DefinitionId,state.StateId,state.Revision,new[]{segment});requests.Add(request);
            var next=sdk.AdvanceMechanicalModes(definition,state,request);if(next.Status!=expected)throw new InvalidOperationException(id+": "+next.Status+" != "+expected);if(next.IsAccepted)state=next.State!;return next;
        }
        Step("drive",new Rational(1,50),P(new Rational(3,10)));
        Step("release",new Rational(1,50),P(new Rational(3,10)),"Accepted",MechanicalConnectionEventKind.Release);
        Step("missing-released-inputs",new Rational(1,25),new(),"Underdetermined");
        Step("released-motion",new Rational(3,50),SP(new Rational(1,25),new Rational(-1,10)));
        Step("aligned-capture-refused",new Rational(3,50),SP(new Rational(1,25),new Rational(-1,10)),"GuardIndeterminate",MechanicalConnectionEventKind.AlignCapture);
        Step("capture",new Rational(3,50),SP(new Rational(1,25),new Rational(-1,10)),"Accepted",MechanicalConnectionEventKind.Capture);
        Step("drive-after-capture",new Rational(7,100),P(new Rational(-1,10)));
        Step("lock-world",new Rational(7,100),P(new Rational(-1,10)),"Accepted",MechanicalConnectionEventKind.LockWorldCarrier);
        Step("world-locked-motion",new Rational(8,100),new());
        Step("extra-planet-prescription",new Rational(9,100),P(0),"ModeInputOwnershipConflict");
        Step("lock-relative",new Rational(8,100),new(),"Accepted",MechanicalConnectionEventKind.LockPlanetRelative);
        Step("relative-locked-motion",new Rational(9,100),new());
        Step("release-lock",new Rational(9,100),new(),"Accepted",MechanicalConnectionEventKind.Release);
        Step("released-independent",new Rational(1,10),SP(new Rational(2,25),new Rational(-1,10)));
        Step("capture-direction",new Rational(1,10),SP(new Rational(2,25),new Rational(-1,10)),"Accepted",MechanicalConnectionEventKind.CapturePositive);
        Step("reverse-refused",new Rational(9,100),P(new Rational(-1,10)),"DirectionConflict");
        var badBatch=new MechanicalModeRequest("atomic-boundary",definition.DefinitionId,state.StateId,state.Revision,new[]{
            new MechanicalModeSegment(new(new Rational(11,100),P(new Rational(-1,10))),new[]{new MechanicalConnectionEvent("atomic/release",state.EventCursor+1,0,MechanicalConnectionEventKind.Release)}),
            new MechanicalModeSegment(new(new Rational(3,10),SP(new Rational(2,25),0))) });
        requests.Add(badBatch);if(sdk.AdvanceMechanicalModes(definition,state,badBatch).Status!="WindingBoundary")throw new InvalidOperationException("Boundary batch did not roll back.");
        var ordered=Step("simultaneous-release-capture",new Rational(1,10),P(new Rational(-1,10)),"Accepted",MechanicalConnectionEventKind.Release,MechanicalConnectionEventKind.CapturePositive);
        requests.Add(ordered.Request);if(sdk.AdvanceMechanicalModes(definition,state,ordered.Request).Status!="AlreadyApplied")throw new InvalidOperationException("Retry not idempotent.");
        return sdk.RecordMechanicalModes(connection,definition,new Rational(3,10),requests);
    }
}
