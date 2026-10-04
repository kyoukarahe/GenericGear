using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Closed production source variants. Geometry cannot supply or replace downstream kinematic laws.</summary>
public abstract class ConnectedWindingSource
{
    internal ConnectedWindingSource() { }
    public abstract OrientedShaft DriverShaft { get; }
    public abstract OrientedShaft OutputShaft { get; }
    public abstract string DriverBodyId { get; }
    public abstract string OutputBodyId { get; }
    public abstract string ChainId { get; }
    public abstract Rational InitialDriverTurns { get; }
    public abstract string DefinitionId { get; }
    public abstract string PinId(int index);
    public abstract string LinkId(int index);
    public abstract string? Validate();
    public abstract int LinkCount { get; }
    public abstract double PitchMm { get; }
    public abstract double DriverMinimumTurns { get; }
    public abstract double DriverMaximumTurns { get; }
}

public enum MaterialTraversalOrder { Forward, Reverse }

public sealed class SpatialWindingDefinition : ConnectedWindingSource
{
    public const string SelectedDriveProfile = "selected-drive-spatial-guided-pin-chain-v1";
    public SpatialWindingDefinition(OrientedShaft driverShaft, OrientedShaft outputShaft, string driverBodyId,
        string outputBodyId, string chainId, SpatialWindingGeometry geometry, Rational initialDriverTurns)
        : this(driverShaft, outputShaft, driverBodyId, outputBodyId, chainId, geometry, initialDriverTurns, null) { }
    /// <summary>Explicit role authoring; material order maps solver walking to persistent pin/link identity.</summary>
    public SpatialWindingDefinition(OrientedShaft driverShaft, OrientedShaft outputShaft, string driverBodyId,
        string outputBodyId, string chainId, SpatialWindingGeometry geometry, Rational initialDriverTurns, MaterialTraversalOrder materialOrder)
        : this(driverShaft, outputShaft, driverBodyId, outputBodyId, chainId, geometry, initialDriverTurns, (MaterialTraversalOrder?)materialOrder) { }
    private SpatialWindingDefinition(OrientedShaft driverShaft, OrientedShaft outputShaft, string driverBodyId,
        string outputBodyId, string chainId, SpatialWindingGeometry geometry, Rational initialDriverTurns, MaterialTraversalOrder? materialOrder)
    {
        if (materialOrder.HasValue && !Enum.IsDefined(typeof(MaterialTraversalOrder), materialOrder.Value)) throw new ArgumentException("InvalidMaterialOrder");
        HasSelectedDriveBoundary = materialOrder.HasValue; MaterialOrder = materialOrder ?? MaterialTraversalOrder.Forward;
        DriverShaft = driverShaft; OutputShaft = outputShaft; DriverBodyId = MechanicalAuthoringProfile.IdValue(driverBodyId);
        OutputBodyId = MechanicalAuthoringProfile.IdValue(outputBodyId); ChainId = MechanicalAuthoringProfile.IdValue(chainId);
        MechanicalAuthoringProfile.Number(initialDriverTurns); InitialDriverTurns = initialDriverTurns; Geometry = geometry;
        foreach (var shaft in new[] { driverShaft, outputShaft }) { MechanicalAuthoringProfile.IdValue(shaft.Id); MechanicalAuthoringProfile.FrameBound(shaft.Frame); }
        string D(double d) => ConnectedMotionValue.Numeric(d);
        string Guide(HelicalPinGuide h) => Pack(Frame(h.Frame), D(h.RadiusMm), D(h.RadiusChangeMmPerTurn), D(h.HeightChangeMmPerTurn), N(h.Hand), D(h.PhaseTurns), D(h.ExitAzimuthTurns), N(h.WindingBranch), D(h.MaximumGuideTurns));
        var g = geometry;
        DefinitionId = HashText(Pack(SpatialWindingGeometry.Profile, SpatialWindingGeometry.Policy, driverShaft.Id, Frame(driverShaft.Frame), driverShaft.IsPrescribed.ToString(),
            outputShaft.Id, Frame(outputShaft.Frame), outputShaft.IsPrescribed.ToString(), driverBodyId, outputBodyId, chainId, F(initialDriverTurns),
            Guide(g.Driver), Guide(g.Output), Pack(g.Bridge.Select(p => Pack(D(p.X), D(p.Y), D(p.Z))).ToArray()), N(g.LinkCount), D(g.PitchMm),
            D(g.MaxBendDegrees), D(g.MaxAttachmentBendDegrees), D(g.DriverMinimumTurns), D(g.DriverMaximumTurns), D(g.OutputMinimumTurns), D(g.OutputMaximumTurns)));
        if (HasSelectedDriveBoundary) DefinitionId = HashText(Pack(SelectedDriveProfile, SpatialWindingSolver.RefinedPolicy, DefinitionId, MaterialOrder.ToString()));
    }
    /// <summary>Author a NEW immutable source, not runtime switching or an inverse command.
    /// Guides retain physical handedness/frames. Reversed walking retains material neighbours.</summary>
    public SpatialWindingDefinition SelectDriveBoundary(string prescribedShaftId, Rational initialPrescribedTurns)
    {
        if (prescribedShaftId != DriverShaft.Id && prescribedShaftId != OutputShaft.Id) throw new ArgumentException("InvalidDriveBoundaryOwner");
        if (prescribedShaftId == DriverShaft.Id)
            return new(new(DriverShaft.Id, DriverShaft.Frame, true), new(OutputShaft.Id, OutputShaft.Frame), DriverBodyId, OutputBodyId, ChainId, Geometry, initialPrescribedTurns, MaterialOrder);
        var g = Geometry;
        var reversed = new SpatialWindingGeometry(g.Output, g.Driver, g.Bridge.Reverse(), g.LinkCount, g.PitchMm,
            g.MaxBendDegrees, g.MaxAttachmentBendDegrees, g.OutputMinimumTurns, g.OutputMaximumTurns, g.DriverMinimumTurns, g.DriverMaximumTurns);
        return new(new(OutputShaft.Id, OutputShaft.Frame, true), new(DriverShaft.Id, DriverShaft.Frame), OutputBodyId, DriverBodyId, ChainId, reversed, initialPrescribedTurns,
            MaterialOrder == MaterialTraversalOrder.Forward ? MaterialTraversalOrder.Reverse : MaterialTraversalOrder.Forward);
    }
    public bool HasSelectedDriveBoundary { get; }
    public MaterialTraversalOrder MaterialOrder { get; }
    public override OrientedShaft DriverShaft { get; }
    public override OrientedShaft OutputShaft { get; }
    public override string DriverBodyId { get; }
    public override string OutputBodyId { get; }
    public override string ChainId { get; }
    public override Rational InitialDriverTurns { get; }
    public override string DefinitionId { get; }
    public SpatialWindingGeometry Geometry { get; }
    public override int LinkCount => Geometry.LinkCount;
    public override double PitchMm => Geometry.PitchMm;
    public override double DriverMinimumTurns => Geometry.DriverMinimumTurns;
    public override double DriverMaximumTurns => Geometry.DriverMaximumTurns;
    public override string PinId(int index) => ChainId + "/pin-" + (MaterialOrder == MaterialTraversalOrder.Reverse ? LinkCount - index : index).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
    public override string LinkId(int index) => ChainId + "/link-" + (MaterialOrder == MaterialTraversalOrder.Reverse ? LinkCount - 1 - index : index).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
    public override string? Validate()
    {
        var error = Geometry.Validate(); if (error is not null) return error;
        if (new[] { DriverShaft.Id, OutputShaft.Id, DriverBodyId, OutputBodyId, ChainId }.Distinct(StringComparer.Ordinal).Count() != 5) return "DuplicateIdentity";
        if (Frame(DriverShaft.Frame) != Frame(Geometry.Driver.Frame) || Frame(OutputShaft.Frame) != Frame(Geometry.Output.Frame)) return "InvalidActualWindingOwner";
        if (!DriverShaft.IsPrescribed || OutputShaft.IsPrescribed) return "InvalidWindingBoundaryOwnership";
        if (InitialDriverTurns < WindingDifferentialEngine.Binary64Boundary(DriverMinimumTurns) || InitialDriverTurns > WindingDifferentialEngine.Binary64Boundary(DriverMaximumTurns)) return "WindingBoundary";
        var q = Query(ConnectedMotionValue.Number(InitialDriverTurns)); return q.IsAccepted ? null : q.Status;
    }
    internal SpatialWindingQuery Query(double q) => HasSelectedDriveBoundary ? SpatialWindingSolver.EvaluateRefined(Geometry, q) : SpatialWindingSolver.Evaluate(Geometry, q);
}

internal static class ConnectedWindingKinematics
{
    internal static string? ValidateSegment(ConnectedWindingSource source, double from, double to, WindingDifferentialAnalysis? analysis = null)
    {
        if (source is FiniteWindingDefinition planar) return FiniteWindingSolver.ValidateSegment(planar.Geometry, from, to);
        var g = ((SpatialWindingDefinition)source).Geometry;
        if (from < g.DriverMinimumTurns || from > g.DriverMaximumTurns || to < g.DriverMinimumTurns || to > g.DriverMaximumTurns) return "WindingBoundary";
        // Explicit sampled/refined numerical path policy, not a swept-geometry certificate.
        var count = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) * 128));
        if (count > 1536) return "ResourceLimit";
        long work = 0;
        for (var i = 0; i <= count; i++)
        {
            var q = from + (to - from) * i / count;
            var r = analysis?.SpatialQuery(q) ?? ((SpatialWindingDefinition)source).Query(q);
            work += r.NumericWork; if (!r.IsAccepted) return r.Status;
            if (work > 40000000) return "ResourceLimit";
        }
        return null;
    }
    internal static bool UnresolvedEvent(ConnectedWindingSource source, double q, WindingDifferentialAnalysis? analysis = null)
    {
        if (source is FiniteWindingDefinition planar) return FiniteWindingSolver.HasUnresolvedEventOrder(planar.Geometry, q);
        var g = ((SpatialWindingDefinition)source).Geometry; var r = analysis?.SpatialQuery(q) ?? ((SpatialWindingDefinition)source).Query(q);
        if (!r.IsAccepted) return true;
        var junctions = g.Bridge.Concat(new[] { g.Driver.Meridian(g.Driver.ExitParameter(q)), g.Output.Meridian(g.Output.ExitParameter(r.Pose!.OutputTurns)) });
        return r.Pose!.Pins.Any(p => junctions.Any(j => (p.PositionMm - j).Length < 1e-7));
    }
}
