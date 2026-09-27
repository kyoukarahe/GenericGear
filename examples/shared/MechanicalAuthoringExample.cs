#nullable disable
using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest.SdkExample
{
    /// <summary>Editable input examples built by the public SDK, not stored artifacts or expected-result catalogs.</summary>
    public static class MechanicalAuthoringExample
    {
        /// <summary>One prescribed bevel input, 20:40 parallel source and 10:30 turned source. No keep-out is included.</summary>
        public static byte[] CreateTwoOutputArtifact(bool refined = true)
        {
            var sdk = GearInvestSdk.CreateDefault();
            var input = OrientedTwoOutputExample.Assembly(parallelStation: 100);
            var request = new OrientedTwoOutputAssemblyRequest(input.Bevel, input.ParallelBranch, input.Outputs,
                input.TurnedBranch, input.AssemblyPose, true, Array.Empty<OrientedKeepOut>(),
                refined ? OrientedTwoOutputProfile.RefinedId : OrientedTwoOutputProfile.Id);
            var result = sdk.ComposeOrientedTwoOutput(request);
            if (!result.IsSuccess) throw new InvalidOperationException("Typed two-output example is not valid: " + result.Status);
            var written = sdk.WriteOrientedTwoOutputArtifact(result.Mechanism, request);
            var loaded = sdk.ReadOrientedTwoOutputArtifact(written.Bytes);
            if (!sdk.ValidateOrientedTwoOutputArtifact(loaded).IsValid || !sdk.RebuildOrientedTwoOutput(loaded).Bytes.SequenceEqual(written.Bytes))
                throw new InvalidOperationException("Typed example did not survive source-only reconstruction.");
            return written.Bytes;
        }

        public static byte[] CreatePlanarArtifact() => OrientedExample.SpurSource(20, 40);

        /// <summary>Ordinary explicit input: two distinct coaxial rotors and one real compound shaft.</summary>
        public static MechanicalDraft CreateCoaxialDraft(string prefix = "rotor", int returnDriverTeeth = 16,
            int returnWheelTeeth = 64, Rational? requiredTransfer = null)
        {
            string Id(string name) => prefix + "/" + name;
            OrientedFrame At(int x, int layer) => OrientedFrame.Identity.At(new ExactVector3(x, 0, 5 * layer));
            var layout = new ParallelCoaxialLayout(OrientedFrame.Identity, 1, 5, 2,
                new[] { new CoaxialPlacementGroup(Id("common-line"), new[] { Id("input"), Id("output") }) });
            return new MechanicalDraft(new MechanicalDefinition(layout, Id("input"),
                new[] { new OrientedShaft(Id("input"), At(0, 0), true), new OrientedShaft(Id("compound"), At(80, 0)), new OrientedShaft(Id("output"), At(0, 0)) },
                new[] {
                    new OrientedGearBody(Id("A"), Id("input"), OrientedGearKind.PlanarSpur, At(0, 0), 20, 20, prefix),
                    new OrientedGearBody(Id("B"), Id("compound"), OrientedGearKind.PlanarSpur, At(80, 0), 60, 60, prefix),
                    new OrientedGearBody(Id("C"), Id("compound"), OrientedGearKind.PlanarSpur, At(80, 1), returnDriverTeeth, returnDriverTeeth, prefix),
                    new OrientedGearBody(Id("D"), Id("output"), OrientedGearKind.PlanarSpur, At(0, 1), returnWheelTeeth, returnWheelTeeth, prefix)
                }, new[] { new MechanicalContact(Id("ab"), OrientedContactKind.ExternalSpur, Id("A"), Id("B")),
                    new MechanicalContact(Id("cd"), OrientedContactKind.ExternalSpur, Id("C"), Id("D")) },
                new[] { new ShaftPort(Id("input-port"), Id("input"), At(0, 0)), new ShaftPort(Id("output-port"), Id("output"), At(0, 1)) },
                outputs: new[] { new MechanicalOutput("driver", Id("input"), Id("A"), Id("input-port"), 1),
                    new MechanicalOutput("driven", Id("output"), Id("D"), Id("output-port"), requiredTransfer ?? new Rational(1, 12)) }));
        }

        /// <summary>Three exact locally tangent pitch circles. The closed driven odd mesh loop is an analysis input, not an exported mechanism.</summary>
        public static MechanicalDraft CreateTriangularDraft()
        {
            var a = OrientedFrame.Identity;
            var b = a.At(new ExactVector3(30, 0, 0));
            var c = a.At(new ExactVector3(0, 40, 0));
            return new MechanicalDraft(new MechanicalDefinition("A",
                new[] { new OrientedShaft("A", a, true), new OrientedShaft("B", b), new OrientedShaft("C", c) },
                new[] { new OrientedGearBody("A-gear", "A", OrientedGearKind.PlanarSpur, a, 10, 10, "authored"),
                    new OrientedGearBody("B-gear", "B", OrientedGearKind.PlanarSpur, b, 20, 20, "authored"),
                    new OrientedGearBody("C-gear", "C", OrientedGearKind.PlanarSpur, c, 30, 30, "authored") },
                new[] { new MechanicalContact("AB", OrientedContactKind.ExternalSpur, "A-gear", "B-gear"),
                    new MechanicalContact("AC", OrientedContactKind.ExternalSpur, "A-gear", "C-gear"),
                    new MechanicalContact("BC", OrientedContactKind.ExternalSpur, "B-gear", "C-gear") },
                new[] { new ShaftPort("C-terminal", "C", c) },
                outputs: new[] { new MechanicalOutput("C", "C", "C-gear", "C-terminal") },
                clearancePolicy: PitchClearancePolicy.Refined));
        }
    }
}
