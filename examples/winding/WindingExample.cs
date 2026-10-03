using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.WindingExample;

/// <summary>Ordinary authoring sample. Geometry is input to the SDK, not a production fixture lookup.</summary>
public static class Example
{
    public static WindingDifferentialDefinition Create(Rational? scale = null, int links = 21, bool circular = false)
    {
        var k = scale ?? Rational.One; var mm = (double)k.Numerator / (double)k.Denominator; var pitch = 2 * mm;
        var radius = pitch / (2 * Math.Sin(Math.PI / 12));
        var driver = Enumerable.Range(0, 12).Select(i => new WindingPoint(radius * Math.Cos(Math.PI - i * Math.PI / 6), radius * Math.Sin(Math.PI - i * Math.PI / 6))).ToArray();
        WindingPoint[] output;
        if (circular) output = Enumerable.Range(0, 12).Select(i => new WindingPoint(radius * Math.Cos(i * Math.PI / 6), radius * Math.Sin(i * Math.PI / 6))).ToArray();
        else
        {
            var angles = new[] { 105, 141, 160, 199, 219, 255 }; var degrees = angles.Concat(angles.Select(x => x + 180)).ToArray();
            var vertices = new List<WindingPoint> { new(0, 0) };
            foreach (var d in degrees.Take(11)) vertices.Add(vertices.Last() + new WindingPoint(pitch * Math.Cos(d * Math.PI / 180), pitch * Math.Sin(d * Math.PI / 180)));
            var center = new WindingPoint(vertices.Average(p => p.X), vertices.Average(p => p.Y)); output = vertices.Select(p => p - center).ToArray();
        }
        var aFrame = OrientedFrame.Identity.At(new ExactVector3(-2 * (links - 6) * k, 0, -20 * k)); var bFrame = OrientedFrame.Identity.At(new ExactVector3(0, 0, -20 * k));
        var geometry = new FiniteWindingGeometry(new(-2 * (links - 6) * mm, 0), new(0, 0), -20 * mm, driver, output, links, pitch, 5, 5, 45, -.125, .125, -.15, .15);
        var initial = FiniteWindingSolver.Evaluate(geometry, 0);
        if (!initial.IsAccepted) throw new ArgumentException(initial.Status);
        var winding = new FiniteWindingDefinition(new("winding/a", aFrame, true), new("winding/b", bFrame), "winding/drum-a", "winding/drum-b", "chain", geometry, 0, initial.Pose!.DriverContact, initial.Pose.OutputContact);
        var c = new OrientedShaft("carrier", OrientedFrame.Identity); var sun = new OrientedShaft("sun", OrientedFrame.Identity);
        var planet = new CarrierLocalShaft("planet", c.Id, new OrientedFrame(new ExactVector3(55 * k, 0, 0), ExactVector3.UnitX, -ExactVector3.UnitY, -ExactVector3.UnitZ));
        var ports = new[] { c.Id, sun.Id, planet.Id }.Select(id => new CarrierOutputPort(id + "-port", id, OrientedFrame.Identity, ExactQuantity.Turns(0)));
        var definition = new DifferentialDefinition(c, sun, planet, OrientedFrame.Identity, 100, 10, ExactQuantity.Millimeters(k), ExactQuantity.Turns(0), ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(9,10)),
            ExactQuantity.Turns(new Rational(1,8)), ExactQuantity.Turns(new Rational(1,4)), 1, ports);
        var outFrame = new OrientedFrame(new ExactVector3(30 * k, 0, 20 * k), ExactVector3.UnitX, -ExactVector3.UnitY, -ExactVector3.UnitZ);
        var suffix = new DifferentialSuffixDefinition(new(definition, new[] { "sun-port", "planet-port" }), c.Id, new("suffix/out", outFrame),
            new("suffix/driver", c.Id, OrientedGearKind.PlanarSpur, OrientedFrame.Identity.At(new ExactVector3(0,0,20*k)),20,10*k,"suffix"),
            new("suffix/wheel", "suffix/out", OrientedGearKind.PlanarSpur, outFrame,40,20*k,"suffix"), "suffix/contact", ExactQuantity.Turns(new Rational(1,8)), ExactQuantity.Turns(new Rational(1,4)),0,ExactQuantity.Turns(new Rational(-3,16)),
            new("suffix/out-port", "suffix/out", OrientedFrame.Identity, ExactQuantity.Turns(0)));
        return new(winding, suffix, "winding-sun-coupling", "sun-port", "planet-port", 0);
    }
}
