using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.DifferentialExample;

public static class Example
{
    public static DifferentialDefinition Create(int sunTeeth = 100, int planetTeeth = 10, bool prefix = false, string idPrefix = "",
        bool signedReference = false, bool holdSun = false, OrientedFrame? relocation = null)
    {
        var c = idPrefix + "carrier"; var s = idPrefix + "sun"; var p = idPrefix + "planet"; var u = idPrefix + "drive";
        var sdk = GearInvestSdk.CreateDefault(); MechanicalDraft? source = null; SourceLengthMapping? map = null;
        var world = relocation ?? OrientedFrame.Identity;
        if (prefix)
        {
            var k = new KinematicSpecification(u, new[] { new RotationalDof(u, true), new RotationalDof(c) }, new[] { new ExternalGearCoupling(idPrefix + "fixed-mesh", u, c, 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("a", -60, 0), new SpatialAxis("b", 0, 0) },
                new[] { new SpatialBody(idPrefix + "drive-gear", SpatialBodyKind.Gear, "a", u, 0, 20, 20), new SpatialBody(idPrefix + "prefix-gear", SpatialBodyKind.Gear, "b", c, 0, 40, 40) },
                new[] { new SpatialContact(idPrefix + "fixed-mesh", SpatialContactKind.ExternalGearMesh, idPrefix + "fixed-mesh", idPrefix + "drive-gear", idPrefix + "prefix-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("differential-prefix", k, spatial));
            if (!generated.IsSuccess) throw new InvalidOperationException("Actual prefix generation failed.");
            source = sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generated.Candidates.Single()).Bytes); map = new SourceLengthMapping(1, world);
        }
        var plane = new SourceLengthMapping(1, world).FrameMm(OrientedFrame.Identity.At(new ExactVector3(0, 0, prefix ? 20 : 0)));
        Rational c0 = signedReference ? new Rational(1, 8) : 0, s0 = signedReference ? new Rational(-1, 7) : 0;
        Rational sm = signedReference ? new Rational(1, 8) : 0, pm = signedReference ? new Rational(1, 4) : 0, registration = signedReference ? 1 : 0;
        var ep = signedReference ? -1 : 1;
        var p0 = ep * (registration + (sunTeeth + planetTeeth) * c0 - sunTeeth * (s0 + sm)) / planetTeeth - pm;
        var local = new OrientedFrame(new ExactVector3(new Rational(sunTeeth + planetTeeth, 2), 0, 0), ExactVector3.UnitX, ep * ExactVector3.UnitY, ep * ExactVector3.UnitZ);
        var ports = new[] { c, s, p }.Concat(prefix ? new[] { u } : Array.Empty<string>()).Select(id => new CarrierOutputPort(id + "-port", id, OrientedFrame.Identity.At(new ExactVector3(0, 0, 4)), ExactQuantity.Turns(0)));
        return new DifferentialDefinition(new OrientedShaft(c, world), new OrientedShaft(s, plane), new CarrierLocalShaft(p, c, local), plane, sunTeeth, planetTeeth,
            ExactQuantity.Millimeters(1), ExactQuantity.Turns(c0), ExactQuantity.Turns(s0), ExactQuantity.Turns(p0), ExactQuantity.Turns(sm), ExactQuantity.Turns(pm), registration,
            ports, holdSun ? new[] { new DifferentialHold("sun-hold", s, ExactQuantity.Turns(s0)) } : null, source, map,
            idPrefix + "arm", idPrefix + "sun-gear", idPrefix + "planet-gear");
    }
}
