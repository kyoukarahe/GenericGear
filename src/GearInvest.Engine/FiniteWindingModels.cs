using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class FiniteWindingDefinition : ConnectedWindingSource
{
    public FiniteWindingDefinition(OrientedShaft driverShaft, OrientedShaft outputShaft, string driverBodyId, string outputBodyId,
        string chainId, FiniteWindingGeometry geometry, Rational initialDriverTurns, int initialDriverContact, int initialOutputContact)
    {
        DriverShaft = driverShaft; OutputShaft = outputShaft; Geometry = geometry;
        foreach (var shaft in new[] { driverShaft, outputShaft }) { MechanicalAuthoringProfile.IdValue(shaft.Id); MechanicalAuthoringProfile.FrameBound(shaft.Frame); }
        DriverBodyId = MechanicalAuthoringProfile.IdValue(driverBodyId); OutputBodyId = MechanicalAuthoringProfile.IdValue(outputBodyId); ChainId = MechanicalAuthoringProfile.IdValue(chainId);
        MechanicalAuthoringProfile.Number(initialDriverTurns); InitialDriverTurns = initialDriverTurns; InitialDriverContact = initialDriverContact; InitialOutputContact = initialOutputContact;
        var g = geometry; string D(double v) => ConnectedMotionValue.Numeric(v); string P(WindingPoint p) => Pack(D(p.X), D(p.Y));
        DefinitionId = HashText(Pack(FiniteWindingGeometry.Profile, FiniteWindingGeometry.Policy, driverShaft.Id, Frame(driverShaft.Frame), driverShaft.IsPrescribed.ToString(), outputShaft.Id, Frame(outputShaft.Frame), outputShaft.IsPrescribed.ToString(),
            driverBodyId, outputBodyId, chainId, P(g.DriverCenter), P(g.OutputCenter), D(g.PlaneZMm), Pack(g.DriverSeats.Select(P).ToArray()), Pack(g.OutputSeats.Select(P).ToArray()),
            N(g.LinkCount), D(g.PitchMm), N(g.MaxDriverContact), N(g.MaxOutputContact), D(g.MaxBendDegrees), D(g.DriverMinimumTurns), D(g.DriverMaximumTurns), D(g.OutputMinimumTurns), D(g.OutputMaximumTurns), F(initialDriverTurns), N(initialDriverContact), N(initialOutputContact)));
    }
    public override OrientedShaft DriverShaft { get; }
    public override OrientedShaft OutputShaft { get; }
    public override string DriverBodyId { get; }
    public override string OutputBodyId { get; }
    public override string ChainId { get; }
    public FiniteWindingGeometry Geometry { get; }
    public override Rational InitialDriverTurns { get; }
    public int InitialDriverContact { get; }
    public int InitialOutputContact { get; }
    public override string DefinitionId { get; }
    public override int LinkCount => Geometry.LinkCount;
    public override double PitchMm => Geometry.PitchMm;
    public override double DriverMinimumTurns => Geometry.DriverMinimumTurns;
    public override double DriverMaximumTurns => Geometry.DriverMaximumTurns;
    public override string PinId(int index) => ChainId + "/pin-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public override string LinkId(int index) => ChainId + "/link-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public override string? Validate()
    {
        var error = Geometry.Validate(); if (error is not null) return error;
        if (new[] { DriverShaft.Id, OutputShaft.Id, DriverBodyId, OutputBodyId, ChainId }.Distinct(StringComparer.Ordinal).Count() != 5) return "DuplicateIdentity";
        foreach (var item in new[] { (DriverShaft, Geometry.DriverCenter), (OutputShaft, Geometry.OutputCenter) })
        {
            var (shaft, c) = item; var f = shaft.Frame;
            if (f.X != ExactVector3.UnitX || f.Y != ExactVector3.UnitY || f.Z != ExactVector3.UnitZ ||
                ConnectedMotionValue.Number(f.Origin.X) != c.X || ConnectedMotionValue.Number(f.Origin.Y) != c.Y || ConnectedMotionValue.Number(f.Origin.Z) != Geometry.PlaneZMm) return "UnsupportedWindingFrame";
        }
        if (!DriverShaft.IsPrescribed || OutputShaft.IsPrescribed) return "InvalidWindingBoundaryOwnership";
        if (InitialDriverTurns < WindingDifferentialEngine.Binary64Boundary(Geometry.DriverMinimumTurns) ||
            InitialDriverTurns > WindingDifferentialEngine.Binary64Boundary(Geometry.DriverMaximumTurns)) return "WindingBoundary";
        var initial = FiniteWindingSolver.Evaluate(Geometry, ConnectedMotionValue.Number(InitialDriverTurns));
        if (!initial.IsAccepted) return initial.Status;
        if (initial.Pose!.DriverContact != InitialDriverContact || initial.Pose.OutputContact != InitialOutputContact) return "InconsistentInitialContact";
        return null;
    }
}

/// <summary>Explicit coaxial independent rotors joined by s=w+H; world-prescribed moving planet port.</summary>
public sealed class WindingDifferentialDefinition
{
    public const string Profile = "finite-winding-differential-spur-v1";
    public const string SpatialProfile = "spatial-winding-differential-composition-v1";
    public const string SelectedDriveProfile = "selected-drive-spatial-winding-composition-v1";
    public WindingDifferentialDefinition(FiniteWindingDefinition winding, DifferentialSuffixDefinition suffix, string couplingId,
        string sunPortId, string planetPortId, Rational initialCouplingOffset, IEnumerable<string>? requiredDomains = null)
        : this((ConnectedWindingSource)winding, suffix, couplingId, sunPortId, planetPortId, initialCouplingOffset, requiredDomains, null, null) { }
    public WindingDifferentialDefinition(SpatialWindingDefinition winding, DifferentialSuffixDefinition suffix, string couplingId,
        string sunPortId, string planetPortId, Rational initialCouplingOffset, IEnumerable<string>? requiredDomains = null, ConnectedTransmission? transmission = null)
        : this((ConnectedWindingSource)winding, suffix, couplingId, sunPortId, planetPortId, initialCouplingOffset, requiredDomains, transmission, null) { }
    public WindingDifferentialDefinition(SpatialWindingDefinition winding, string couplingShaftId, DifferentialSuffixDefinition suffix, string couplingId,
        string sunPortId, string planetPortId, Rational initialCouplingOffset,
        IEnumerable<string>? requiredDomains = null, ConnectedTransmission? transmission = null)
        : this((ConnectedWindingSource)winding, suffix, couplingId, sunPortId, planetPortId, initialCouplingOffset, requiredDomains, transmission, couplingShaftId) { }
    private WindingDifferentialDefinition(ConnectedWindingSource winding, DifferentialSuffixDefinition suffix, string couplingId,
        string sunPortId, string planetPortId, Rational initialCouplingOffset, IEnumerable<string>? requiredDomains, ConnectedTransmission? transmission, string? couplingShaftId)
    {
        HasSelectedDriveBoundary = winding is SpatialWindingDefinition spatial && spatial.HasSelectedDriveBoundary;
        if (HasSelectedDriveBoundary != (couplingShaftId is not null)) throw new ArgumentException("ExplicitDriveAndCouplingRequired");
        CouplingShaftId = couplingShaftId ?? winding.OutputShaft.Id;
        if (CouplingShaftId != winding.DriverShaft.Id && CouplingShaftId != winding.OutputShaft.Id) throw new ArgumentException("InvalidCouplingOwner");
        WindingSource = winding; Suffix = suffix; Transmission = transmission; CouplingId = MechanicalAuthoringProfile.IdValue(couplingId);
        SunPortId = MechanicalAuthoringProfile.IdValue(sunPortId); PlanetPortId = MechanicalAuthoringProfile.IdValue(planetPortId);
        MechanicalAuthoringProfile.Number(initialCouplingOffset); InitialCouplingOffset = initialCouplingOffset;
        RequiredDomains = DifferentialProfile.Set(requiredDomains ?? Array.Empty<string>(), x => x, 16);
        DefinitionId = HashText(Pack(SourceProfile, winding.DefinitionId, suffix.DefinitionId, couplingId, sunPortId, planetPortId, F(initialCouplingOffset), Pack(RequiredDomains.ToArray())));
        if (winding is SpatialWindingDefinition) DefinitionId = HashText(Pack(DefinitionId, transmission?.DefinitionId ?? ""));
        if (HasSelectedDriveBoundary) DefinitionId = HashText(Pack(DefinitionId, CouplingShaftId));
    }
    public FiniteWindingDefinition Winding => WindingSource as FiniteWindingDefinition ?? throw new InvalidOperationException("Spatial source: use WindingSource.");
    public ConnectedWindingSource WindingSource { get; }
    public ConnectedTransmission? Transmission { get; }
    public bool HasSelectedDriveBoundary { get; }
    public string CouplingShaftId { get; }
    public OrientedShaft CouplingShaft => CouplingShaftId == WindingSource.DriverShaft.Id ? WindingSource.DriverShaft : WindingSource.OutputShaft;
    public string SourceProfile => HasSelectedDriveBoundary ? SelectedDriveProfile : WindingSource is SpatialWindingDefinition ? SpatialProfile : Profile;
    public DifferentialSuffixDefinition Suffix { get; }
    public string CouplingId { get; }
    public string SunPortId { get; }
    public string PlanetPortId { get; }
    public Rational InitialCouplingOffset { get; }
    public ReadOnlyCollection<string> RequiredDomains { get; }
    public string DefinitionId { get; }
}

public sealed class WindingDifferentialAnalysis
{
    private readonly System.Collections.Generic.Dictionary<double, SpatialWindingQuery> spatialQueries = new();
    private readonly System.Collections.Generic.Queue<double> spatialOrder = new();
    internal SpatialWindingQuery SpatialQuery(double q)
    {
        lock (spatialQueries)
        {
            if (spatialQueries.TryGetValue(q, out var cached)) return cached;
            var result = ((SpatialWindingDefinition)Source.WindingSource).Query(q);
            if (spatialOrder.Count == 8) spatialQueries.Remove(spatialOrder.Dequeue());
            spatialQueries.Add(q, result); spatialOrder.Enqueue(q); return result;
        }
    }
    internal WindingDifferentialAnalysis(WindingDifferentialDefinition source, DifferentialSuffixAnalysis suffix, IEnumerable<string> errors)
    { Source = source; Suffix = suffix; Diagnostics = errors.ToList().AsReadOnly(); }
    public WindingDifferentialDefinition Source { get; }
    public DifferentialSuffixAnalysis Suffix { get; }
    public ReadOnlyCollection<string> Diagnostics { get; }
    public bool IsValid => Diagnostics.Count == 0;
}

public static partial class WindingDifferentialEngine
{
    public static WindingDifferentialAnalysis Prepare(WindingDifferentialDefinition s)
    {
        var suffix = DifferentialSuffixAnalyzer.Prepare(s.Suffix); var errors = suffix.Diagnostics.ToList(); var winding = s.WindingSource.Validate(); if (winding is not null) errors.Add(winding);
        // Validate bounded source counts before expanding material identities. Invalid input is
        // diagnostic data, not permission to allocate LinkCount elements (or overflow count + 1).
        if (winding is not null) return new(s, suffix, errors);
        var d = s.Suffix.Parent.Definition;
        if (d.Prefix is not null || d.Holds.Count != 0) errors.Add("UnsupportedConnectedParentBoundary");
        if (s.CouplingShaft.Id == d.SunShaft.Id || !d.SunShaft.Contains(s.CouplingShaft.Frame.Origin) || d.SunShaft.Frame.Z != s.CouplingShaft.Frame.Z || d.SunShaft.Frame.X != s.CouplingShaft.Frame.X) errors.Add("InvalidCoaxialIndependentRotorBinding");
        foreach (var item in new[] { (s.SunPortId, d.SunShaft.Id), (s.PlanetPortId, d.PlanetShaft.Id) })
            if (!d.Ports.Any(p => p.Id == item.Item1 && p.ShaftId == item.Item2) || !s.Suffix.Parent.InputPortIds.Contains(item.Item1)) errors.Add("InvalidInputPortOwner");
        var all = suffix.Parent.CoordinateIds.Concat(new[] { d.CarrierBodyId, d.SunBodyId, d.PlanetBodyId, s.Suffix.OutputShaft.Id, s.Suffix.DriverGear.Id, s.Suffix.OutputGear.Id, s.Suffix.ContactId, s.Suffix.OutputPort.Id })
            .Concat(d.Ports.Select(p => p.Id)).Concat(new[] { s.CouplingId, s.WindingSource.DriverShaft.Id, s.WindingSource.OutputShaft.Id, s.WindingSource.DriverBodyId, s.WindingSource.OutputBodyId, s.WindingSource.ChainId })
            .Concat(Enumerable.Range(0, s.WindingSource.LinkCount + 1).Select(s.WindingSource.PinId)).Concat(Enumerable.Range(0, s.WindingSource.LinkCount).Select(s.WindingSource.LinkId)).ToArray();
        if (all.Distinct(StringComparer.Ordinal).Count() != all.Length) errors.Add("DuplicateIdentity");
        var transmissionError = ConnectedTransmissionCompiler.Validate(s, new System.Collections.Generic.HashSet<string>(all, StringComparer.Ordinal));
        if (transmissionError is not null) errors.Add(transmissionError);
        if (s.WindingSource is FiniteWindingDefinition planar && (ConnectedMotionValue.Number(d.PlaneMm.Origin.Z) == planar.Geometry.PlaneZMm || ConnectedMotionValue.Number(s.Suffix.DriverGear.MountingFrame.Origin.Z) == planar.Geometry.PlaneZMm)) errors.Add("UnsupportedOverlappingWindingPitchPlane");
        if (s.WindingSource is SpatialWindingDefinition spatial)
        {
            var g = spatial.Geometry; var normal = WindingPoint3.Of(d.PlaneMm.Z);
            var extrema = g.Bridge.Select(p => p.Dot(normal)).ToList();
            foreach (var guide in new[] { g.Driver, g.Output })
            {
                var origin = WindingPoint3.Of(guide.Frame.Origin).Dot(normal);
                var axial = WindingPoint3.Of(guide.Frame.Z).Dot(normal) * guide.HeightChangeMmPerTurn * guide.MaximumGuideTurns;
                var radial = Math.Max(guide.RadiusMm, guide.RadiusMm + guide.RadiusChangeMmPerTurn * guide.MaximumGuideTurns) *
                    (Math.Abs(WindingPoint3.Of(guide.Frame.X).Dot(normal)) + Math.Abs(WindingPoint3.Of(guide.Frame.Y).Dot(normal)));
                extrema.Add(origin + Math.Min(0, axial) - radial); extrema.Add(origin + Math.Max(0, axial) + radial);
            }
            var pitchPlanes = new[] { d.PlaneMm.Origin, s.Suffix.DriverGear.MountingFrame.Origin }
                .Concat(s.Transmission?.Stages.Select(stage => stage.DriverGear.MountingFrame.Origin) ?? Array.Empty<ExactVector3>());
            if (pitchPlanes.Select(p => WindingPoint3.Of(p).Dot(normal)).Any(z => z >= extrema.Min() && z <= extrema.Max())) errors.Add("UnsupportedOverlappingWindingPitchPlane");
        }
        // A finalized definition has no requested path yet. Per-request path checks cannot be
        // promoted to a whole-domain source certificate through RequiredDomains.
        var performed = new[] { "source-admission", "exact-relations", "pitch-placement", "numeric-residual" };
        errors.AddRange(s.RequiredDomains.Where(x => !performed.Contains(x)).Select(x => "RequiredNotPerformed:" + x));
        return new(s, suffix, errors);
    }
}
