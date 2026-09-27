using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.Cli;

internal static class FixtureFactory
{
    public static LowLevelMechanicalSpecification CreateCompoundIdlerFixture()
    {
        var kinematic = new KinematicSpecification(
            "A",
            new[]
            {
                new RotationalDof("A", isPrescribed: true),
                new RotationalDof("B"),
                new RotationalDof("C"),
                new RotationalDof("D"),
            },
            new[]
            {
                new ExternalGearCoupling("mesh:A1-B1", "A", "B", 10, 50),
                new ExternalGearCoupling("mesh:B2-C1", "B", "C", 10, 20),
                new ExternalGearCoupling("mesh:C1-D1", "C", "D", 20, 120),
            });

        var spatial = new SpatialMechanism(
            new[]
            {
                new SpatialAxis("axis:A", 0, 0),
                new SpatialAxis("axis:B", 30, 0),
                new SpatialAxis("axis:C", 45, 0),
                new SpatialAxis("axis:D", 115, 0),
            },
            new[]
            {
                new SpatialBody("A1", SpatialBodyKind.Gear, "axis:A", "A", 0, 10, 5),
                new SpatialBody("B1", SpatialBodyKind.Gear, "axis:B", "B", 0, 50, 25),
                new SpatialBody("B2", SpatialBodyKind.Gear, "axis:B", "B", 1, 10, 5),
                new SpatialBody("C1", SpatialBodyKind.Gear, "axis:C", "C", 1, 20, 10),
                new SpatialBody("D1", SpatialBodyKind.Gear, "axis:D", "D", 1, 120, 60),
            },
            new[]
            {
                new SpatialContact("contact:A1-B1", SpatialContactKind.ExternalGearMesh, "mesh:A1-B1", "A1", "B1"),
                new SpatialContact("contact:B2-C1", SpatialContactKind.ExternalGearMesh, "mesh:B2-C1", "B2", "C1"),
                new SpatialContact("contact:C1-D1", SpatialContactKind.ExternalGearMesh, "mesh:C1-D1", "C1", "D1"),
            });

        return new LowLevelMechanicalSpecification("fixture.compound-idler.v1", kinematic, spatial);
    }
}
