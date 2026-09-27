#nullable disable
using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest.SdkExample
{
    /// <summary>Small ordinary typed authoring example, also delivered to independent consumers. No catalog or expected artifact dependency.</summary>
    public static class OrientedExample
    {
        public static RightAngleBevelRequest Pair(int inputTeeth = 10, int outputTeeth = 20, bool reverseOutputCoordinate = false, Rational? innerParameter = null)
        {
            var x = ExactVector3.UnitX; var y = ExactVector3.UnitY; var z = ExactVector3.UnitZ;
            var inputFrame = OrientedFrame.Identity; var outputFrame = new OrientedFrame(default, z, reverseOutputCoordinate ? y : -y, reverseOutputCoordinate ? -x : x);
            var input = new OrientedShaft("bevel/input", inputFrame, true); var output = new OrientedShaft("bevel/output", outputFrame);
            return new RightAngleBevelRequest(default,
                new BevelGearMount(input, z, inputTeeth, 1, z * outputTeeth, new ShaftPort("bevel/input-port", input.Id, inputFrame.At(z * 60))),
                new BevelGearMount(output, x, outputTeeth, 1, x * inputTeeth, new ShaftPort("bevel/output-port", output.Id, outputFrame.At(x * 60))), innerParameter ?? new Rational(1, 2));
        }

        public static byte[] SpurSource(int inputTeeth, int outputTeeth, int inputX = 0)
        {
            var sdk = GearInvestSdk.CreateDefault();
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") },
                new[] { new ExternalGearCoupling("mesh", "input", "output", inputTeeth, outputTeeth) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", inputX, 0), new SpatialAxis("output-axis", inputX + inputTeeth + outputTeeth, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, inputTeeth, inputTeeth), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, outputTeeth, outputTeeth) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var result = sdk.Generate(new LowLevelMechanicalSpecification("oriented-example-spur-" + inputTeeth + "-" + outputTeeth + "-" + inputX, kinematic, spatial));
            if (!result.IsSuccess) throw new InvalidOperationException("Planar source generation failed.");
            var written = sdk.WriteArtifact(result.Candidates.Single()); var read = sdk.ReadArtifact(written.Bytes);
            if (!sdk.Validate(read).IsValid || !sdk.WriteArtifact(read).Bytes.SequenceEqual(written.Bytes)) throw new InvalidOperationException("Planar source writer/read/verify/validate roundtrip failed.");
            return written.Bytes;
        }

        public static OrientedAssemblyRequest Assembly(bool mixed = true, bool reverseOutputCoordinate = false, OrientedFrame pose = null, bool invalidPort = false)
        {
            var pair = Pair(reverseOutputCoordinate: reverseOutputCoordinate);
            if (!mixed) return new OrientedAssemblyRequest(pair, assemblyPose: pose);
            var prePose = OrientedFrame.Identity.At(new ExactVector3(0, 0, 60));
            var postPose = new OrientedFrame(new ExactVector3(60, 0, 0), ExactVector3.UnitZ, -ExactVector3.UnitY, ExactVector3.UnitX);
            var prePort = new ShaftPort("output-port", "output", prePose.At(new ExactVector3(invalidPort ? 1 : 0, 0, 60)));
            return new OrientedAssemblyRequest(pair,
                new PlanarArtifactPlacement(SpurSource(10, 20, -30), prePose, "input", "output", prePort),
                new PlanarArtifactPlacement(SpurSource(10, 30), postPose, "input", "output", new ShaftPort("input-port", "input", postPose)),
                pose, new Rational(-1, 12));
        }
    }
}
