using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Layout;

/// <summary>Explicit geometric coincidence of distinct rotor IDs, never a rigid or motion constraint.</summary>
public sealed class CoaxialPlacementGroup
{
    public CoaxialPlacementGroup(string id, IEnumerable<string> shaftIds)
    {
        Id = ParallelCoaxialLayout.StableId(id);
        ShaftIds = ParallelCoaxialLayout.BoundedSet(shaftIds, x => x, ParallelCoaxialLayout.MaxRotors);
        if (ShaftIds.Count < 2) throw new ArgumentException("A coaxial group requires at least two distinct rotor IDs.");
    }
    public string Id { get; }
    public ReadOnlyCollection<string> ShaftIds { get; }
}

/// <summary>Exact source-unit plane lattice and typed geometric rotor groups. Does not own motion or solids.</summary>
public sealed class ParallelCoaxialLayout
{
    public const string Profile = "bounded-parallel-coaxial-rotors-v1";
    public const string ClearancePolicy = "parallel-layer-nonpenetrating-pitch-v1";
    public const int MaxRotors = 64, MaxGroups = 32, MaximumLayers = 3;

    public ParallelCoaxialLayout(OrientedFrame planeFrame, Rational pitchRadiusPerTooth,
        Rational layerSpacing, int layerCount, IEnumerable<CoaxialPlacementGroup> groups)
    {
        PlaneFrame = planeFrame ?? throw new ArgumentNullException(nameof(planeFrame));
        foreach (var vector in new[] { planeFrame.Origin, planeFrame.X, planeFrame.Y, planeFrame.Z })
            foreach (var number in new[] { vector.X, vector.Y, vector.Z }) Number(number);
        Number(pitchRadiusPerTooth); Number(layerSpacing);
        if (!planeFrame.IsProperCardinal || pitchRadiusPerTooth <= 0 || layerSpacing <= 0 || layerCount < 1 || layerCount > MaximumLayers)
            throw new ArgumentException("Proper exact plane, positive pitch/spacing and 1..3 layers required.");
        PitchRadiusPerTooth = pitchRadiusPerTooth; LayerSpacing = layerSpacing; LayerCount = layerCount;
        Groups = BoundedSet(groups, g => g.Id, MaxGroups);
        if (Groups.Sum(g => g.ShaftIds.Count) > MaxRotors) throw new ArgumentException("Aggregate coaxial rotor membership bound exceeded.");
    }
    public OrientedFrame PlaneFrame { get; }
    public Rational PitchRadiusPerTooth { get; }
    public Rational LayerSpacing { get; }
    public int LayerCount { get; }
    public ReadOnlyCollection<CoaxialPlacementGroup> Groups { get; }
    public Rational LayerCoordinate(ExactVector3 position) => (position - PlaneFrame.Origin).Dot(PlaneFrame.Z) / LayerSpacing;
    public bool IsLayer(ExactVector3 position)
    {
        var layer = LayerCoordinate(position);
        return layer.Denominator == 1 && layer >= 0 && layer < LayerCount;
    }
    internal static string StableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl)) throw new ArgumentException("Bounded stable rotor/group ID required.");
        return value;
    }
    internal static ReadOnlyCollection<T> BoundedSet<T>(IEnumerable<T> values, Func<T, string> key, int bound)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        var items = values.Take(bound + 1).ToArray();
        if (items.Length > bound || items.Any(x => x is null)) throw new ArgumentException("Coaxial collection bound/null item.");
        foreach (var item in items) StableId(key(item));
        if (items.Select(key).Distinct(StringComparer.Ordinal).Count() != items.Length) throw new ArgumentException("Duplicate typed coaxial ID.");
        return items.OrderBy(key, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    private static void Number(Rational value)
    {
        if (value.Numerator.ToString(CultureInfo.InvariantCulture).Length > 128 || value.Denominator.ToString(CultureInfo.InvariantCulture).Length > 128)
            throw new ArgumentException("Exact input digit bound exceeded.");
    }
}
