using System.Linq;
using GearInvest.Core;
using Xunit;

namespace GearInvest.Tests;

public sealed class KinematicTests
{
    [Fact]
    public void FixturePropagatesExactCompoundAndIdlerTransfers()
    {
        var candidate = TestFixture.GenerateCandidate();

        Assert.Equal(Rational.One, TestFixture.State(candidate, "A").Coefficient);
        Assert.Equal(Rational.Parse("-1/5"), TestFixture.State(candidate, "B").Coefficient);
        Assert.Equal(Rational.Parse("1/10"), TestFixture.State(candidate, "C").Coefficient);
        Assert.Equal(Rational.Parse("-1/60"), TestFixture.State(candidate, "D").Coefficient);
        Assert.All(candidate.Solution.States, state => Assert.Equal(Rational.Zero, state.PhaseOffset));
    }

    [Fact]
    public void DetectsContradictoryPaths()
    {
        var specification = new KinematicSpecification(
            "A",
            new[] { new RotationalDof("A", true), new RotationalDof("B") },
            new[]
            {
                new ExternalGearCoupling("mesh:one", "A", "B", 10, 20),
                new ExternalGearCoupling("mesh:two", "A", "B", 10, 30),
            });

        var result = KinematicSolver.Solve(specification);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.Contradiction);
    }

    [Fact]
    public void DiagnosesUnreachableDof()
    {
        var specification = new KinematicSpecification(
            "A",
            new[]
            {
                new RotationalDof("A", true),
                new RotationalDof("B"),
                new RotationalDof("orphan"),
            },
            new[] { new ExternalGearCoupling("mesh:A-B", "A", "B", 10, 20) });

        var result = KinematicSolver.Solve(specification);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == DiagnosticCodes.UnreachableDof && item.SubjectId == "orphan");
    }

    [Fact]
    public void EvaluatesExactAffinePhase()
    {
        var specification = new KinematicSpecification(
            "A",
            new[] { new RotationalDof("A", true), new RotationalDof("B") },
            new[] { new ExternalGearCoupling("mesh:A-B", "A", "B", 1, 1, Rational.Parse("1/3")) });
        var solved = KinematicSolver.Solve(specification);

        Assert.True(solved.IsValid);
        var evaluation = KinematicEvaluator.Evaluate(solved.Solution!, Rational.Parse("1/2"));

        Assert.Equal(Rational.Parse("1/2"), evaluation.Values.Single(item => item.DofId == "A").UnwrappedTurns);
        Assert.Equal(Rational.Parse("-1/6"), evaluation.Values.Single(item => item.DofId == "B").UnwrappedTurns);
    }

    [Fact]
    public void ExternalGearCouplingAlwaysReversesDirection()
    {
        var coupling = new ExternalGearCoupling("mesh", "A", "B", 10, 50);

        Assert.Equal(Rational.Parse("-1/5"), coupling.Transfer);
        Assert.Equal(-1, coupling.Transfer.Sign);
    }
}
