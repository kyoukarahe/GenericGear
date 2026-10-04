using System;
using System.Collections.Generic;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.WindingExample;

/// <summary>Author input, not a pose catalog. Uses the same public APIs with arbitrary material identities and link counts.</summary>
public static class SpatialExample
{
    /// <summary>The same finite chain, explicitly authored with its coaxial conical reel prescribed.
    /// Independent sun and reel owners remain distinct; no duplicated or synchronised input coordinate.</summary>
    public static WindingDifferentialDefinition CreateSelectedDrive(int links = 249, Rational? scale = null,
        string materialId = "spatial-chain", int extraStages = 2, Rational? initialTurns = null)
    {
        var source = Create(links, scale, materialId, extraStages);
        var original = (SpatialWindingDefinition)source.WindingSource;
        var selected = original.SelectDriveBoundary(original.OutputShaft.Id, initialTurns ?? new Rational(13,1000));
        return new(selected, selected.DriverShaft.Id, source.Suffix, source.CouplingId, source.SunPortId, source.PlanetPortId,
            source.InitialCouplingOffset, transmission: source.Transmission);
    }
    public static WindingDifferentialDefinition Create(int links = 249, Rational? scale = null, string materialId = "spatial-chain", int extraStages = 2)
    {
        if (extraStages < 1 || extraStages > 4) throw new ArgumentException("One to four explicit stages.");
        var k = scale ?? Rational.One; var mm = (double)k.Numerator / (double)k.Denominator; var pitch = .85 * mm;
        var separation = new Rational(113) + new Rational(85, 100) * (links - 249);
        var a = new HelicalPinGuide(OrientedFrame.Identity.At(new ExactVector3(-separation*k,0,-40*k)), 4.2*mm,.18*mm,1.7*mm,1,0,0,2,4);
        var b = new HelicalPinGuide(OrientedFrame.Identity.At(new ExactVector3(0,0,-40*k)), 4.5*mm,.11*mm,1.7*mm,-1,0,.5,-2,4);
        var geometry = new SpatialWindingGeometry(a,b,new[]{a.Meridian(4),b.Meridian(4)},links,pitch,130,45,-1.2,1.2,-1.35,1.7);
        var winding = new SpatialWindingDefinition(new("spatial/driver",a.Frame,true),new("spatial/passive",b.Frame),"spatial/drum-a","spatial/drum-b",materialId,geometry,0);
        var baseSource = Example.Create(k); var stages = new List<FixedAxisSpurStage>(); var shaft = baseSource.Suffix.OutputShaft; var reference = baseSource.Suffix.OutputReference.Value;
        for (var i = 0; i < extraStages; i++)
        {
            var key = "transmission/stage-" + i; var layer = (30 + i*10)*k;
            var driverFrame = new OrientedFrame(new ExactVector3(shaft.Frame.Origin.X,shaft.Frame.Origin.Y,layer),shaft.Frame.X,shaft.Frame.Y,shaft.Frame.Z);
            var outputFrame = new OrientedFrame(new ExactVector3(shaft.Frame.Origin.X+20*k,shaft.Frame.Origin.Y,layer),shaft.Frame.X,shaft.Frame.Y,shaft.Frame.Z);
            var output = new OrientedShaft(key+"/shaft",outputFrame); var nextReference = -reference;
            stages.Add(new(shaft.Id,output,new(key+"/driver",shaft.Id,OrientedGearKind.PlanarSpur,driverFrame,20,10*k,key),
                new(key+"/output",output.Id,OrientedGearKind.PlanarSpur,outputFrame,20,10*k,key),key+"/contact",0,0,0,nextReference,
                new(key+"/port",output.Id,OrientedFrame.Identity,ExactQuantity.Turns(new Rational(1,7)))));
            shaft=output; reference=nextReference;
        }
        var transmission = new ConnectedTransmission(stages,shaft.Id,"terminal/carrier",shaft.Frame,new Rational(1,9),
            new[]{new CarrierRigidAttachment("terminal/attached-body",OrientedFrame.Identity.At(new ExactVector3(5*k,0,2*k)))});
        return new(winding,baseSource.Suffix,"spatial/coupling",baseSource.SunPortId,baseSource.PlanetPortId,new Rational(1,11),transmission:transmission);
    }
}
