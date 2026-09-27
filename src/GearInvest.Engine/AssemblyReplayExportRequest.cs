using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Explicit finite absolute-root sampling. No implicit loop, interpolation or solver policy.</summary>
public sealed class AssemblyReplayExportRequest
{
    public AssemblyReplayExportRequest(IEnumerable<Rational> sampleRoots, bool includeExactGenevaBoundaries = true,
        int maximumTotalWork = AssemblyReplayLimits.MaximumTotalWork)
    {
        if (sampleRoots is null) throw new ArgumentNullException(nameof(sampleRoots));
        var roots = sampleRoots.Take(AssemblyReplayLimits.Samples + 1).ToArray();
        if (roots.Length == 0 || roots.Length > AssemblyReplayLimits.Samples)
            throw new ArgumentException("One to512 explicit sample roots are required.", nameof(sampleRoots));
        for (var i = 0; i < roots.Length; i++)
        {
            AssemblyReplayLimits.CheckFraction(roots[i], AssemblyReplayLimits.InputIntegerCharacters);
            if (i > 0 && roots[i] <= roots[i - 1]) throw new ArgumentException("Sample roots must be strictly increasing, without duplicates.", nameof(sampleRoots));
        }
        if (maximumTotalWork < 0 || maximumTotalWork > AssemblyReplayLimits.MaximumTotalWork)
            throw new ArgumentOutOfRangeException(nameof(maximumTotalWork));
        SampleRoots = Array.AsReadOnly(roots); IncludeExactGenevaBoundaries = includeExactGenevaBoundaries;
        MaximumTotalWork = maximumTotalWork;
    }
    public ReadOnlyCollection<Rational> SampleRoots { get; }
    public Rational MinimumRoot => SampleRoots[0];
    public Rational MaximumRoot => SampleRoots[SampleRoots.Count - 1];
    public bool IncludeExactGenevaBoundaries { get; }
    public int MaximumTotalWork { get; }
}

/// <summary>Separate web-consumption bounds; no existing mechanical or legacy web policy is relaxed.</summary>
public static class AssemblyReplayLimits
{
    public const int DocumentBytes = 32 * 1024 * 1024, PayloadBytes = 24 * 1024 * 1024, SourceBytes = 8 * 1024 * 1024;
    public const int JsonDepth = 64, JsonNodes = 1048576, JsonProperties = 256, StringCharacters = 4096, IdCharacters = 256;
    public const int InputIntegerCharacters = 128, DerivedIntegerCharacters = 1024;
    public const int Members = 8, Inventory = 8192, Shafts = 256, Bodies = 256, Features = 4096;
    public const int Samples = 512, SampleChannelProduct = 131072, MaximumTotalWork = 32000000;
    public const int GeometryVertices = 262144, GeometryIndices = 786432;
    public const int Instances = 32, ExpandedInventory = 65536, ExternalBindings = 8192;
    public const double DisplayCoordinateMm = 100000000;

    public static void CheckFraction(Rational value, int maximumCharacters = DerivedIntegerCharacters)
    {
        if (value.Numerator.ToString(CultureInfo.InvariantCulture).Length > maximumCharacters ||
            value.Denominator.ToString(CultureInfo.InvariantCulture).Length > maximumCharacters)
            throw new ArgumentException("Replay rational integer character bound exceeded.");
    }
}
