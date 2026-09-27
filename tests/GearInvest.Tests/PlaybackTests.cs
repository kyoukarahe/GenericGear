using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using Xunit;

namespace GearInvest.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public void ResolvedPlaybackMatchesKinematicSolutionAndBodyBindings()
    {
        var candidate = TestFixture.GenerateCandidate();
        var report = ResolvedPlaybackValidator.Validate(
            candidate.ResolvedPlayback,
            candidate.Solution,
            candidate.Spatial);

        Assert.True(report.IsValid);
        Assert.Equal(Rational.One, candidate.ResolvedPlayback.Channels.Single(item => item.DofId == "A").ExactCoefficient);
        Assert.Equal(Rational.Parse("-1/5"), candidate.ResolvedPlayback.Channels.Single(item => item.DofId == "B").ExactCoefficient);
        Assert.Equal(Rational.Parse("1/10"), candidate.ResolvedPlayback.Channels.Single(item => item.DofId == "C").ExactCoefficient);
        Assert.Equal(Rational.Parse("-1/60"), candidate.ResolvedPlayback.Channels.Single(item => item.DofId == "D").ExactCoefficient);
        Assert.Equal(5, candidate.ResolvedPlayback.BodyBindings.Count);
        Assert.Equal(2, candidate.ResolvedPlayback.BodyBindings.Count(item => item.DofId == "B"));
    }

    [Theory]
    [InlineData("0", "0", "0", "0", "0")]
    [InlineData("1/4", "1/4", "-1/20", "1/40", "-1/240")]
    [InlineData("1/2", "1/2", "-1/10", "1/20", "-1/120")]
    [InlineData("1", "1", "-1/5", "1/10", "-1/60")]
    public void EvaluatorReturnsExactGoldenTurns(
        string root,
        string expectedA,
        string expectedB,
        string expectedC,
        string expectedD)
    {
        var candidate = TestFixture.GenerateCandidate();
        var evaluation = GearInvestSdk.CreateDefault().Evaluate(candidate, Rational.Parse(root));

        Assert.Equal(Rational.Parse(expectedA), evaluation.Values.Single(item => item.DofId == "A").UnwrappedTurns);
        Assert.Equal(Rational.Parse(expectedB), evaluation.Values.Single(item => item.DofId == "B").UnwrappedTurns);
        Assert.Equal(Rational.Parse(expectedC), evaluation.Values.Single(item => item.DofId == "C").UnwrappedTurns);
        Assert.Equal(Rational.Parse(expectedD), evaluation.Values.Single(item => item.DofId == "D").UnwrappedTurns);
    }

    [Fact]
    public void CandidateValidationDetectsPlaybackChannelMismatch()
    {
        var candidate = TestFixture.GenerateCandidate();
        var channels = candidate.ResolvedPlayback.Channels
            .Where(item => item.DofId != "D")
            .Concat(new[]
            {
                new PlaybackChannel("D", "driver:A", Rational.Parse("1/60"), Rational.Zero),
            });
        var playback = new ResolvedPlayback(
            candidate.ResolvedPlayback.Drivers,
            channels,
            candidate.ResolvedPlayback.BodyBindings);
        var changed = new GenerationCandidate(
            candidate.SourceId,
            candidate.Kinematic,
            candidate.Solution,
            candidate.Spatial,
            playback,
            candidate.Validation);

        var report = new GenerationEngine().Validate(changed);

        Assert.Contains(report.Diagnostics, item => item.Code == DiagnosticCodes.PlaybackChannelMismatch);
    }
}
