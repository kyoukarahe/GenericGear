using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using Xunit;

namespace GearInvest.Tests;

public sealed class SpatialWindingTests
{
    public static SpatialWindingGeometry Create(int links = 249, double pitch = .85, double radiusSlope = .18)
    {
        var a = new HelicalPinGuide(OrientedFrame.Identity.At(new ExactVector3(-113, 0, -40)), 4.2, radiusSlope, 1.7, 1, 0, 0, 2, 4);
        var b = new HelicalPinGuide(OrientedFrame.Identity.At(new ExactVector3(0, 0, -40)), 4.5, .11, 1.7, -1, 0, .5, -2, 4);
        return new(a, b, new[] { a.Meridian(4), b.Meridian(4) }, links, pitch, 130, 45, -1.2, 1.2, -1.35, 1.7);
    }
    [Fact]
    public void Guided_3d_chain_solves_every_material_pitch_not_arclength()
    {
        var g = Create(); Assert.Null(g.Validate());
        var r = SpatialWindingSolver.Evaluate(g, 0); Assert.True(r.IsAccepted, r.Status + ":" + r.NumericWork);
        var p = r.Pose!; Assert.Equal(g.LinkCount + 1, p.Pins.Count);
        for (var i = 1; i < p.Pins.Count; i++) Assert.InRange(Math.Abs((p.Pins[i].PositionMm - p.Pins[i - 1].PositionMm).Length - g.PitchMm), 0, 1e-8);
        Assert.InRange((p.Pins[0].PositionMm - g.Driver.Point(0, 0)).Length, 0, 1e-9);
        Assert.InRange((p.Pins.Last().PositionMm - g.Output.Point(0, p.OutputTurns)).Length, 0, 1e-9);
        Assert.True(p.Pins.Max(x => x.PositionMm.Z) - p.Pins.Min(x => x.PositionMm.Z) > 2);
        Assert.Equal("Underdetermined", p.LinkRoll); Assert.Null(p.SolutionErrorBoundTurns);
    }
    [Fact]
    public void Refuses_resources_and_outside_finite_input_before_solving()
    {
        Assert.Equal("ResourceLimit", SpatialWindingSolver.Evaluate(Create(513), 0).Status);
        Assert.Equal("WindingBoundary", SpatialWindingSolver.Evaluate(Create(), 2).Status);
        Assert.Equal("ResourceLimit", SpatialWindingSolver.Evaluate(Create(), 0, 10).Status);
    }
    [Theory]
    [InlineData(-1.2)]
    [InlineData(-.77)]
    [InlineData(.21)]
    [InlineData(.93)]
    [InlineData(1.2)]
    public void Multiple_turn_finite_domain_is_not_reduced_modulo_one(double q)
    {
        var r = SpatialWindingSolver.Evaluate(Create(), q);
        Assert.True(r.IsAccepted, r.Status + ":" + r.NumericWork);
        Assert.Equal(q, r.Pose!.DriverTurns);
    }
    [Fact]
    public void Direct_independent_pin_equations_cover_contact_transfers_reverse_and_stop()
    {
        var g=Create();var contacts=new System.Collections.Generic.HashSet<(int,int)>();
        GearInvest.Layout.SpatialWindingPose? initial=null;
        foreach(var q in Enumerable.Range(0,49).Select(i=>-1.15+2.3*i/48).Concat(new[]{.4,.4,-.8,0d}))
        {
            var query=SpatialWindingSolver.Evaluate(g,q);Assert.True(query.IsAccepted,query.Status);
            var pose=query.Pose!;contacts.Add((pose.DriverContact,pose.OutputContact));
            for(var i=0;i<pose.Pins.Count;i++)
            {
                var p=pose.Pins[i];var xyz=p.PositionMm;
                if(i>0){var a=pose.Pins[i-1].PositionMm;var distance=Math.Sqrt((xyz.X-a.X)*(xyz.X-a.X)+(xyz.Y-a.Y)*(xyz.Y-a.Y)+(xyz.Z-a.Z)*(xyz.Z-a.Z));Assert.InRange(Math.Abs(distance-g.PitchMm),0,1e-8);}
                if(p.GuidePiece==0||p.GuidePiece==4)
                {
                    var h=p.GuidePiece==0?g.Driver:g.Output;var rotor=p.GuidePiece==0?q:pose.OutputTurns;
                    var t=p.GuidePiece==0?p.Parameter:(h.ExitAzimuthTurns+h.WindingBranch-h.PhaseTurns-rotor)/h.Hand-p.Parameter;
                    // Independent scalar equations, not the production Point/Derivative helpers.
                    var angle=2*Math.PI*(h.PhaseTurns+h.Hand*t+rotor);var radius=h.RadiusMm+h.RadiusChangeMmPerTurn*t;
                    var ox=(double)h.Frame.Origin.X.Numerator/(double)h.Frame.Origin.X.Denominator;
                    var oz=(double)h.Frame.Origin.Z.Numerator/(double)h.Frame.Origin.Z.Denominator;
                    Assert.InRange(Math.Abs(xyz.X-ox-radius*Math.Cos(angle)),0,1e-9);Assert.InRange(Math.Abs(xyz.Y-radius*Math.Sin(angle)),0,1e-9);
                    Assert.InRange(Math.Abs(xyz.Z-oz-h.HeightChangeMmPerTurn*t),0,1e-9);
                }
                else
                {
                    // The authored mechanical transfer rails are straight in world space.
                    var ae=g.Driver.ExitParameter(q);var be=g.Output.ExitParameter(pose.OutputTurns);
                    var a=p.GuidePiece==1?g.Driver.Meridian(ae):p.GuidePiece==2?g.Bridge[0]:g.Bridge[1];
                    var b=p.GuidePiece==1?g.Bridge[0]:p.GuidePiece==2?g.Bridge[1]:g.Output.Meridian(be);
                    var dx=b.X-a.X;var dy=b.Y-a.Y;var dz=b.Z-a.Z;var length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                    Assert.InRange(p.Parameter,0,length);
                    Assert.InRange(Math.Abs(xyz.X-a.X-dx*p.Parameter/length),0,1e-9);
                    Assert.InRange(Math.Abs(xyz.Y-a.Y-dy*p.Parameter/length),0,1e-9);
                    Assert.InRange(Math.Abs(xyz.Z-a.Z-dz*p.Parameter/length),0,1e-9);
                }
                if(i>0&&i<pose.Pins.Count-1)
                {
                    var before=pose.Pins[i-1].PositionMm;var after=pose.Pins[i+1].PositionMm;
                    var u=new[]{xyz.X-before.X,xyz.Y-before.Y,xyz.Z-before.Z};var v=new[]{after.X-xyz.X,after.Y-xyz.Y,after.Z-xyz.Z};
                    var cosine=(u[0]*v[0]+u[1]*v[1]+u[2]*v[2])/Math.Sqrt(u.Sum(x=>x*x)*v.Sum(x=>x*x));
                    Assert.InRange(Math.Acos(Math.Clamp(cosine,-1,1))*180/Math.PI,0,g.MaxBendDegrees);
                }
            }
            if(q==.4){if(initial is not null)Assert.Equal(initial.OutputTurns,pose.OutputTurns);initial=pose;}
        }
        Assert.True(contacts.Count>20);
    }

    [Theory]
    [InlineData("pitch", "UnsupportedJointBounds")]
    [InlineData("bend", "JointLimit")]
    [InlineData("attachment", "JointLimit")]
    [InlineData("branch", "InvalidWindingBranch")]
    [InlineData("domain", "UnsupportedFiniteDomain")]
    [InlineData("bridge", "DisconnectedTransferGuide")]
    [InlineData("height", "UnsupportedGuideBounds")]
    public void Invalid_declared_constraints_do_not_produce_a_success_pose(string change, string expected)
    {
        var g=Create();var a=g.Driver;
        if(change is "branch" or "height") a=new(a.Frame,a.RadiusMm,a.RadiusChangeMmPerTurn,
            change=="height"?0:a.HeightChangeMmPerTurn,a.Hand,a.PhaseTurns,a.ExitAzimuthTurns,
            change=="branch"?10:a.WindingBranch,a.MaximumGuideTurns);
        var bridge=g.Bridge.ToArray();if(change=="bridge")bridge[0]+=new WindingPoint3(0,0,.1);
        var invalid=new SpatialWindingGeometry(a,g.Output,bridge,g.LinkCount,change=="pitch"?0:g.PitchMm,
            change=="bend"?1:g.MaxBendDegrees,change=="attachment"?.01:g.MaxAttachmentBendDegrees,
            g.DriverMinimumTurns,change=="domain"?g.DriverMinimumTurns:g.DriverMaximumTurns,g.OutputMinimumTurns,g.OutputMaximumTurns);
        var r=SpatialWindingSolver.Evaluate(invalid,0);Assert.Equal(expected,r.Status);Assert.Null(r.Pose);
    }

    [Fact]
    public void Rotated_and_translated_guides_preserve_all_pins_not_only_the_output_angle()
    {
        var g=Create();
        // A proper cardinal rotation (x,y,z)->(z,x,y), plus a translation.
        ExactVector3 E(ExactVector3 p)=>new(p.Z+7,p.X-3,p.Y+2);
        WindingPoint3 P(WindingPoint3 p)=>new(p.Z+7,p.X-3,p.Y+2);
        HelicalPinGuide H(HelicalPinGuide h)=>new(new OrientedFrame(E(h.Frame.Origin),new(0,1,0),new(0,0,1),new(1,0,0)),
            h.RadiusMm,h.RadiusChangeMmPerTurn,h.HeightChangeMmPerTurn,h.Hand,h.PhaseTurns,h.ExitAzimuthTurns,h.WindingBranch,h.MaximumGuideTurns);
        var changed=new SpatialWindingGeometry(H(g.Driver),H(g.Output),g.Bridge.Select(P),g.LinkCount,g.PitchMm,g.MaxBendDegrees,
            g.MaxAttachmentBendDegrees,g.DriverMinimumTurns,g.DriverMaximumTurns,g.OutputMinimumTurns,g.OutputMaximumTurns);
        foreach(var q in new[]{-.93,0,.83})
        {
            var original=SpatialWindingSolver.Evaluate(g,q);var rotated=SpatialWindingSolver.Evaluate(changed,q);
            Assert.True(rotated.IsAccepted,rotated.Status);Assert.InRange(Math.Abs(original.Pose!.OutputTurns-rotated.Pose!.OutputTurns),0,1e-10);
            for(var i=0;i<g.LinkCount+1;i++) Assert.InRange((P(original.Pose.Pins[i].PositionMm)-rotated.Pose.Pins[i].PositionMm).Length,0,1e-8);
        }
    }
}
