using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.WindingExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class SpatialSegmentBoundaryTests
{
    public static IEnumerable<object[]> Segments()
    {
        foreach (var (a, b) in new[]
        {
            (4562d / 11000, -1d), (-4562d / 11000, 1d),
            (9d / 37, .5), (-7d / 13, -1d), (.5, 9d / 37), (-1d, -7d / 13),
            (-9d / 37, 7d / 11), (17d / 19, 1d / 31), (-32d, -20d), (20d, 32d),
            (13d / 17, 13d / 17), (-0d, 0d), (0d, -0d),
            (Math.BitDecrement(.5), .5), (-1d, Math.BitIncrement(-1)),
            (-double.Epsilon, double.Epsilon)
        })
            foreach (var count in new[] { 1, 2, 3, 17, 182, 257, 1536 })
                yield return new object[] { a, b, count };
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public void Product_checkpoints_preserve_exact_endpoints_and_closed_segment(double from, double to, int count)
        => Check(from, to, count);

    [Fact]
    public void Original_roundoff_failure_is_not_a_user_domain_violation()
    {
        var from = 4562d / 11000; const double to = -1; const int count = 182;
        Assert.True(from + (to - from) * count / count < to);
        Check(from, to, count);
    }

    [Fact]
    public void Deterministic_bounded_fraction_starts_cover_both_domain_endpoints()
    {
        // Fixed bounded set, not unbounded fuzzing. Equality between adjacent samples is legal.
        for (var i = 1; i <= 256; i++)
        {
            var from = -1 + 1.5 * i / 257;
            Check(from, -1, Math.Max(1, (int)Math.Ceiling(Math.Abs(from + 1) * 128)));
            Check(from, .5, Math.Max(1, (int)Math.Ceiling(Math.Abs(.5 - from) * 128)));
        }
    }

    private static void Check(double from, double to, int count)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(from), BitConverter.DoubleToInt64Bits(ConnectedWindingKinematics.SegmentCheckpoint(from, to, 0, count)));
        Assert.Equal(BitConverter.DoubleToInt64Bits(to), BitConverter.DoubleToInt64Bits(ConnectedWindingKinematics.SegmentCheckpoint(from, to, count, count)));
        var previous = from;
        for (var i = 0; i <= count; i++)
        {
            var q = ConnectedWindingKinematics.SegmentCheckpoint(from, to, i, count);
            Assert.True(double.IsFinite(q)); Assert.InRange(q, Math.Min(from, to), Math.Max(from, to));
            Assert.True(to >= from ? q >= previous : q <= previous); previous = q;
        }
    }

    [Theory]
    [InlineData(true, 201)]
    [InlineData(true, 320)]
    [InlineData(false, 249)]
    public void Ordinary_authored_sources_commit_boundaries_and_restore_without_role_or_material_changes(bool selected, int links)
    {
        var sdk = GearInvestSdk.CreateDefault(); var source = Source(selected, links);
        var artifact = sdk.FinalizeWindingConnection(source);
        using var run = sdk.PrepareMechanicalRuntime(artifact.Bytes, "boundary-generic", new Rational(3, 10));
        foreach (var q in new[] { new Rational(4562, 11000), new Rational(-1), new Rational(-3, 4), new Rational(9, 37), new Rational(1, 2), new Rational(1, 2), new Rational(-1) })
        {
            var before = run.Current; var prepared = run.Prepare(SpatialRuntimeTests.Request(before, "input-" + before.Revision, q, new Rational(3, 10)));
            Assert.True(prepared.IsAccepted, prepared.Status); Assert.Same(before, run.Current);
            Assert.Equal("Accepted", run.Commit(prepared));
            Assert.Equal(q, run.Current.Mechanical.Frame.DriverTurns);
            Assert.Equal(q, run.Current.Mechanical.Frame.Coordinates[source.WindingSource.DriverShaft.Id].Exact);
            Assert.Equal(before.Definition.DefinitionId, run.Current.Definition.DefinitionId);
            Assert.Equal(before.Mechanical.Mode, run.Current.Mechanical.Mode);
            Assert.Equal(links + 1, run.Current.Mechanical.Frame.SpatialWinding!.Pins.Count);
            Assert.Equal(source.WindingSource.DefinitionId, run.Current.Definition.Connection.WindingSource.DefinitionId);
        }
        using var restored = sdk.RestoreMechanicalRuntime(run.Checkpoint().Bytes);
        Assert.Equal(run.Current.StateId, restored.Current.StateId);
        var next = restored.Advance(SpatialRuntimeTests.Request(restored.Current, "continued", new Rational(-3, 4), new Rational(3, 10)));
        Assert.True(next.IsAccepted, next.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Real_outside_values_including_rationals_rounded_to_boundary_are_atomic_refusals(bool selected)
    {
        var sdk = GearInvestSdk.CreateDefault(); var source = Source(selected, 249);
        using var run = sdk.PrepareMechanicalRuntime(sdk.FinalizeWindingConnection(source).Bytes, "boundary-negative", new Rational(3, 10));
        var tiny = new Rational(1, BigInteger.Pow(10, 20));
        Assert.Equal(-1d, ConnectedMotionValue.Number(-Rational.One - tiny));
        Assert.Equal(.5, ConnectedMotionValue.Number(new Rational(1, 2) + tiny));
        foreach (var q in new[] { WindingDifferentialEngine.Binary64Boundary(Math.BitDecrement(-1)), WindingDifferentialEngine.Binary64Boundary(Math.BitIncrement(.5)), -Rational.One - tiny, new Rational(1, 2) + tiny })
        {
            var before = run.Current; var bytes = run.Snapshot();
            var request = SpatialRuntimeTests.Request(before, "outside", q, new Rational(3, 10));
            var result = run.Prepare(request);
            Assert.Equal("WindingBoundary", result.Status); Assert.Null(result.Candidate); Assert.Same(before, run.Current); Assert.Equal(bytes, run.Snapshot());
            var valid = SpatialRuntimeTests.Request(before, "valid", new Rational(1, 4), new Rational(3, 10)).Segments[0];
            result = run.Advance(new("out-and-back", before.SessionId, before.Definition.DefinitionId, before.StateId, before.Epoch, before.Revision, new[] { valid, request.Segments[0], valid }));
            Assert.Equal("WindingBoundary", result.Status); Assert.Null(result.Candidate); Assert.Same(before, run.Current); Assert.Equal(bytes, run.Snapshot());
        }
    }

    [Fact]
    public void Segment_admission_rejects_nonfinite_values_before_checkpoint_generation()
    {
        var source = Source(true, 249).WindingSource;
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Equal("WindingBoundary", ConnectedWindingKinematics.ValidateSegment(source, value, 0));
            Assert.Equal("WindingBoundary", ConnectedWindingKinematics.ValidateSegment(source, 0, value));
        }
    }

    [Fact]
    public void In_domain_but_unsolved_geometry_is_still_refused()
    {
        var source = SpatialExample.CreateSelectedDrive();
        Assert.Equal("NoBracketInDeclaredBranch", ConnectedWindingKinematics.ValidateSegment(source.WindingSource, -1, -23d / 20));
    }

    private static WindingDifferentialDefinition Source(bool selected, int links)
    {
        var source = selected ? SpatialExample.CreateSelectedDrive(links, materialId: "boundary-generic-material", extraStages: 1) : SpatialExample.Create(links, materialId: "boundary-legacy-material", extraStages: 1);
        var w = (SpatialWindingDefinition)source.WindingSource; var g = w.Geometry;
        var geometry = new SpatialWindingGeometry(g.Driver, g.Output, g.Bridge, g.LinkCount, g.PitchMm, g.MaxBendDegrees, g.MaxAttachmentBendDegrees, -1, .5, g.OutputMinimumTurns, g.OutputMaximumTurns);
        var winding = selected ? new SpatialWindingDefinition(w.DriverShaft, w.OutputShaft, w.DriverBodyId, w.OutputBodyId, w.ChainId, geometry, new Rational(1, 2), w.MaterialOrder) :
            new SpatialWindingDefinition(w.DriverShaft, w.OutputShaft, w.DriverBodyId, w.OutputBodyId, w.ChainId, geometry, new Rational(1, 2));
        return selected ? new(winding, winding.DriverShaft.Id, source.Suffix, source.CouplingId, source.SunPortId, source.PlanetPortId, source.InitialCouplingOffset, transmission: source.Transmission) :
            new(winding, source.Suffix, source.CouplingId, source.SunPortId, source.PlanetPortId, source.InitialCouplingOffset, transmission: source.Transmission);
    }
}
