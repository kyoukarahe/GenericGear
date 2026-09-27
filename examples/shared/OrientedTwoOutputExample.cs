#nullable disable
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest.SdkExample
{
    /// <summary>Ordinary typed SDK authoring, independent of the conformance catalog.</summary>
    public static class OrientedTwoOutputExample
    {
        public static OrientedTwoOutputAssemblyRequest Assembly(bool withTurnedBranch = true, int parallelInputTeeth = 20, int parallelOutputTeeth = 40,
            int turnedInputTeeth = 10, int turnedOutputTeeth = 30, bool reverseBevelOutput = false, bool reverseParallelTerminal = false,
            OrientedFrame assemblyPose = null, int parallelStation = 100, int parallelPortDeltaX = 0, Rational? turnedTarget = null)
        {
            var x = ExactVector3.UnitX; var y = ExactVector3.UnitY; var z = ExactVector3.UnitZ;
            var original = OrientedExample.Pair(reverseOutputCoordinate: reverseBevelOutput);
            var aPose = OrientedFrame.Identity.At(z * parallelStation);
            var bPose = new OrientedFrame(x * 60, z, -y, x);
            var bevel = new RightAngleBevelRequest(original.Apex,
                new BevelGearMount(original.Input.Shaft, original.Input.ConeDirection, original.Input.Teeth, original.Input.OuterPitchRadiusPerTooth,
                    original.Input.FixedCenter, new ShaftPort(original.Input.Port.Id, original.Input.Shaft.Id, aPose)), original.Output, original.InnerParameter);
            var a = new PlanarArtifactPlacement(OrientedExample.SpurSource(parallelInputTeeth, parallelOutputTeeth), aPose, "input", "output",
                new ShaftPort("input-port", "input", aPose.At(aPose.Origin + x * parallelPortDeltaX)));
            var b = withTurnedBranch ? new PlanarArtifactPlacement(OrientedExample.SpurSource(turnedInputTeeth, turnedOutputTeeth), bPose, "input", "output", new ShaftPort("input-port", "input", bPose)) : null;
            var terminalA = new OrientedFrame(aPose.Point(x * (parallelInputTeeth + parallelOutputTeeth)), x, reverseParallelTerminal ? -y : y, reverseParallelTerminal ? -z : z);
            var terminalB = b == null ? bevel.Output.Port : new ShaftPort("terminal-port", "output", bPose.At(bPose.Point(x * (turnedInputTeeth + turnedOutputTeeth))));
            return new OrientedTwoOutputAssemblyRequest(bevel, a, new[] {
                new OrientedOutputRequest("A", OrientedOutputRole.ParallelBranch, "output-gear", new ShaftPort("terminal-port", "output", terminalA), new Rational(reverseParallelTerminal ? parallelInputTeeth : -parallelInputTeeth, parallelOutputTeeth)),
                new OrientedOutputRequest("B", OrientedOutputRole.TurnedBranch, b == null ? "bevel/wheel" : "output-gear", terminalB,
                    turnedTarget ?? (b == null ? new Rational(reverseBevelOutput ? 1 : -1, 2) : new Rational(turnedInputTeeth, 2 * turnedOutputTeeth)))
            }, b, assemblyPose);
        }
    }
}
