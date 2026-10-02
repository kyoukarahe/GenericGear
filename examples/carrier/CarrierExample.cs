using System;
using System.Linq;
using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.CarrierExample;

/// <summary>Ordinary authored input, not a fixture or manufacturer-specific model.</summary>
public static class Example
{
    public static CarrierDefinition Create(int sunTeeth = 100, int planetTeeth = 10, bool prefix = false,
        bool reversePlanet = false, bool nonzeroReference = false, OrientedFrame? relocation = null)
    {
        var sdk = GearInvestSdk.CreateDefault(); MechanicalDraft source;
        if (prefix)
        {
            var k = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("carrier") },
                new[] { new ExternalGearCoupling("mesh", "input", "carrier", 20, 40) });
            var s = new SpatialMechanism(new[] { new SpatialAxis("a", -60, 0), new SpatialAxis("b", 0, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "a", "input", 0, 20, 20),
                    new SpatialBody("prefix-gear", SpatialBodyKind.Gear, "b", "carrier", 0, 40, 40) },
                new[] { new SpatialContact("mesh", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "prefix-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("carrier-example-prefix", k, s));
            if (!generated.IsSuccess) throw new InvalidOperationException("Prefix generation failed.");
            source = sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generated.Candidates.Single()).Bytes);
        }
        else source = new MechanicalDraft(new MechanicalDefinition("carrier", new[] { new OrientedShaft("carrier", OrientedFrame.Identity, true) },
            Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance));
        var mapping = new SourceLengthMapping(1, relocation ?? OrientedFrame.Identity);
        var plane = mapping.FrameMm(OrientedFrame.Identity.At(new ExactVector3(0, 0, prefix ? 20 : 0)));
        Rational c0 = nonzeroReference ? new Rational(1, 8) : 0, s0 = nonzeroReference ? new Rational(1, 4) : 0;
        Rational gs = nonzeroReference ? new Rational(1, 8) : 0, gp = nonzeroReference ? new Rational(1, 4) : 0;
        var commonP0 = (sunTeeth + planetTeeth) * c0 / planetTeeth - sunTeeth * (s0 + gs) / planetTeeth;
        var p0 = (reversePlanet ? -commonP0 : commonP0) - gp;
        var local = new OrientedFrame(new ExactVector3(new Rational(sunTeeth + planetTeeth, 2), 0, 0), ExactVector3.UnitX,
            reversePlanet ? -ExactVector3.UnitY : ExactVector3.UnitY, reversePlanet ? -ExactVector3.UnitZ : ExactVector3.UnitZ);
        return new CarrierDefinition(source, mapping, "carrier", plane, new OrientedShaft("sun", plane), new CarrierLocalShaft("planet", "carrier", local),
            sunTeeth, planetTeeth, ExactQuantity.Millimeters(1), ExactQuantity.Turns(c0), ExactQuantity.Turns(s0), ExactQuantity.Turns(p0),
            ExactQuantity.Turns(gs), ExactQuantity.Turns(gp), 0,
            new CarrierOutputPort("planet-output", "planet", OrientedFrame.Identity.At(new ExactVector3(0, 0, 4)), ExactQuantity.Turns(0)));
    }
}
