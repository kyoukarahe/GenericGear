using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.WindingExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class DriveBoundaryTests
{
    // This authoring scenario is written before product changes. Initially the existing
    // output-only coupling rejects it. Keep its real rotor, scale and intended flow.
    internal static WindingDifferentialDefinition Source(int links = 249) => SpatialExample.CreateSelectedDrive(links);

    [Fact]
    public void D1_selected_real_conical_reel_drives_same_chain_coupling_and_persisted_runtime()
    {
        var sdk=GearInvestSdk.CreateDefault();var source=Source();var artifact=sdk.FinalizeWindingConnection(source);
        using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"drv-d1",new Rational(3,10));
        Apply(run,"new-schedule",new Rational(23,1000),new Rational(3,10));
        var f=run.Current.Mechanical.Frame;var w=(SpatialWindingDefinition)source.WindingSource;
        Assert.Equal(new Rational(23,1000),f.Coordinates[w.DriverShaft.Id].Exact);
        Assert.False(f.Coordinates[w.OutputShaft.Id].IsExact);
        Assert.Equal(source.InitialCouplingOffset,f.Coordinates[source.Suffix.Parent.Definition.SunShaft.Id].Minus(f.Coordinates[w.DriverShaft.Id]).Exact);
        Assert.InRange((f.SpatialWinding!.Pins[0].PositionMm-w.Geometry.Driver.Point(0,.023)).Length,0,1e-8);
        Assert.Equal(250,f.SpatialWinding.Pins.Count);
        Apply(run,"release",new Rational(23,1000),new Rational(3,10),MechanicalConnectionEventKind.Release);
        var before=run.Current;var input=new MechanicalModeInput(new Rational(31,1000),new Dictionary<string,Rational>{{source.SunPortId,new Rational(7,19)},{source.PlanetPortId,new Rational(2,7)}});
        var capture=new RuntimeRequest("capture",before.SessionId,before.Definition.DefinitionId,before.StateId,before.Epoch,before.Revision,
            new[]{new RuntimeSegment(input,new[]{new RuntimeEvent("capture-event",before.EventCursor+1,0,MechanicalConnectionEventKind.Capture)})});
        var result=run.Advance(capture);Assert.True(result.IsAccepted,result.Status);
        using var restored=sdk.RestoreMechanicalRuntime(run.Checkpoint().Bytes);
        Assert.Equal(run.Current.StateId,restored.Current.StateId);
        Apply(restored,"after-reopen",new Rational(-17,1000),new Rational(1,9));
        Assert.Equal(new Rational(-17,1000),restored.Current.Mechanical.Frame.Coordinates[w.DriverShaft.Id].Exact);
    }

    [Fact]
    public void D2_same_session_exact_schedule_has_independent_contact_law_and_nonlinear_opposite_reel()
    {
        var sdk=GearInvestSdk.CreateDefault();var source=Source();var artifact=sdk.FinalizeWindingConnection(source);
        using var run=sdk.PrepareMechanicalRuntime(artifact.Bytes,"drv-d2",new Rational(3,10));
        var b=new List<double>();var y=new List<ConnectedMotionValue>();var p=new Rational(3,10);
        foreach(var r in new[]{new Rational(-1,10),new Rational(-5,100),Rational.Zero,new Rational(5,100),new Rational(1,10)})
        {
            Apply(run,"schedule-"+b.Count,r,p);var f=run.Current.Mechanical.Frame;
            var actual=f.Ports[source.Transmission!.Stages.Last().OutputPort.Id];
            // Independent equations: 100*s - 10*p - 110*c = -9;
            // opposite-axis 20:40 suffix o=c/2-3/16; two 20:20 stages, readout +1/7.
            var expected=new Rational(5,11)*(r+source.InitialCouplingOffset)-p/22+new Rational(9,220)-new Rational(3,16)+new Rational(1,7);
            Assert.Equal(expected,actual.Exact);y.Add(actual);
            b.Add(f.Coordinates[source.WindingSource.OutputShaft.Id].Estimate);
            Assert.InRange(f.SpatialWinding!.PitchResidualMm,0,1e-8);
            Assert.InRange(f.SpatialWinding.AttachmentResidualMm,0,1e-8);
        }
        for(var i=1;i<y.Count;i++)Assert.Equal(new Rational(1,44),y[i].Minus(y[i-1]).Exact);
        Assert.True(Math.Abs((b[2]-b[1])-(b[1]-b[0]))>1e-8,"Representative opposite winding must remain nonlinear.");
        Apply(run,"standstill",new Rational(1,10),p);
        Apply(run,"reverse-with-other-input",new Rational(-17,1000),new Rational(-2,9));
    }

    internal static void Apply(MechanicalRuntimeSession run,string id,Rational r,Rational p,MechanicalConnectionEventKind? kind=null)
    {var result=run.Advance(SpatialRuntimeTests.Request(run.Current,id,r,p,kind));Assert.True(result.IsAccepted,result.Status);}
}
