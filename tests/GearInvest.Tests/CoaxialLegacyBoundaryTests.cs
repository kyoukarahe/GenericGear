using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using Xunit;
using Xunit.Abstractions;

namespace GearInvest.Tests;

public sealed class CoaxialLegacyBoundaryTests
{
    private readonly ITestOutputHelper output;
    public CoaxialLegacyBoundaryTests(ITestOutputHelper output) { this.output = output; }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void ExistingPublicGenerateRetainsCoincidenceAndRigidOwnershipRefusals(bool sharedAxis, int secondLayer)
    {
        var sdk = GearInvestSdk.CreateDefault();
        var kinematic = new KinematicSpecification("M",
            new[] { new RotationalDof("M", true), new RotationalDof("I"), new RotationalDof("H") },
            new[] { new ExternalGearCoupling("ab", "M", "I", 20, 60), new ExternalGearCoupling("cd", "I", "H", 16, 64) });
        var axes = new List<SpatialAxis> { new("axis:M", 0, 0), new("axis:I", 80, 0) };
        if (!sharedAxis) axes.Add(new("axis:H", 0, 0));
        var spatial = new SpatialMechanism(axes, new[] {
            new SpatialBody("A", SpatialBodyKind.Gear, "axis:M", "M", 0, 20, 20),
            new SpatialBody("B", SpatialBodyKind.Gear, "axis:I", "I", 0, 60, 60),
            new SpatialBody("C", SpatialBodyKind.Gear, "axis:I", "I", secondLayer, 16, 16),
            new SpatialBody("D", SpatialBodyKind.Gear, sharedAxis ? "axis:M" : "axis:H", "H", secondLayer, 64, 64)
        }, new[] {
            new SpatialContact("contact:ab", SpatialContactKind.ExternalGearMesh, "ab", "A", "B"),
            new SpatialContact("contact:cd", SpatialContactKind.ExternalGearMesh, "cd", "C", "D")
        });
        var generated = sdk.Generate(new LowLevelMechanicalSpecification("coaxial-legacy-boundary", kinematic, spatial));
        foreach (var diagnostic in generated.Diagnostics) output.WriteLine(diagnostic.Code + " | " + diagnostic.SubjectId + " | " + diagnostic.Message);
        Assert.False(generated.IsSuccess);
        Assert.Empty(generated.Candidates);
        Assert.Contains(generated.Diagnostics, d => d.Code == (sharedAxis ? DiagnosticCodes.CompoundDofMismatch : DiagnosticCodes.CoincidentAxes));
    }

    [Fact]
    public void OrientedObjectsAndSolvedRelationsAloneDoNotFinalizeTheOldPlanarProfile()
    {
        var sdk = GearInvestSdk.CreateDefault();
        OrientedFrame At(int x, int z) => OrientedFrame.Identity.At(new ExactVector3(x, 0, z));
        var definition = new MechanicalDefinition("M",
            new[] { new OrientedShaft("M", At(0, 0), true), new OrientedShaft("I", At(80, 0)), new OrientedShaft("H", At(0, 0)) },
            new[] {
                new OrientedGearBody("A", "M", OrientedGearKind.PlanarSpur, At(0, 0), 20, 20, "authored"),
                new OrientedGearBody("B", "I", OrientedGearKind.PlanarSpur, At(80, 0), 60, 60, "authored"),
                new OrientedGearBody("C", "I", OrientedGearKind.PlanarSpur, At(80, 1), 16, 16, "authored"),
                new OrientedGearBody("D", "H", OrientedGearKind.PlanarSpur, At(0, 1), 64, 64, "authored")
            }, new[] { new MechanicalContact("ab", OrientedContactKind.ExternalSpur, "A", "B"), new MechanicalContact("cd", OrientedContactKind.ExternalSpur, "C", "D") },
            new[] { new ShaftPort("readout", "H", At(0, 1)) }, outputs: new[] { new MechanicalOutput("output", "H", "D", "readout", new Rational(1, 12)) },
            clearancePolicy: MechanicalAuthoringProfile.PlanarClearance);
        var draft = new MechanicalDraft(definition);
        var analysis = sdk.AnalyzeMechanicalDraft(draft);
        Assert.Equal(new Rational(1, 12), analysis.Outputs.Single().ShaftRelation!.Value.Coefficient);
        var final = sdk.TryFinalizeMechanicalDraft(draft, MechanicalAuthoringProfile.PlanarExport);
        output.WriteLine("Finalization=" + final.Status);
        foreach (var diagnostic in final.Diagnostics) output.WriteLine(diagnostic.Code + " | " + diagnostic.Detail);
        Assert.False(final.IsFinalized);
        Assert.Null(final.ArtifactBytes);
    }
}
