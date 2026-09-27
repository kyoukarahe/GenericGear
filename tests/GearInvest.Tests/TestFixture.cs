using System;
using System.Linq;
using GearInvest.Cli;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Tests;

internal static class TestFixture
{
    public static LowLevelMechanicalSpecification CreateSpecification()
    {
        return FixtureFactory.CreateCompoundIdlerFixture();
    }

    public static GenerationCandidate GenerateCandidate()
    {
        var result = GearInvestSdk.CreateDefault().Generate(CreateSpecification());
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        }

        return result.Candidates.Single();
    }

    public static DofKinematicState State(GenerationCandidate candidate, string dofId)
    {
        return candidate.Solution.States.Single(item => StringComparer.Ordinal.Equals(item.DofId, dofId));
    }
}
