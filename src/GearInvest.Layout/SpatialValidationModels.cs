using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Layout;

public sealed class SpatialValidationOptions
{
    public SpatialValidationOptions(
        BigInteger pitchRadiusTicksPerTooth,
        int maxLayers,
        BigInteger clearanceTicks)
    {
        PitchRadiusTicksPerTooth = pitchRadiusTicksPerTooth;
        MaxLayers = maxLayers;
        ClearanceTicks = clearanceTicks;
    }

    public BigInteger PitchRadiusTicksPerTooth { get; }

    public int MaxLayers { get; }

    public BigInteger ClearanceTicks { get; }
}

public sealed class SpatialContactReadback
{
    public SpatialContactReadback(
        string contactId,
        BigInteger actualCenterDistanceSquared,
        BigInteger expectedCenterDistanceSquared)
    {
        ContactId = contactId ?? throw new ArgumentNullException(nameof(contactId));
        ActualCenterDistanceSquared = actualCenterDistanceSquared;
        ExpectedCenterDistanceSquared = expectedCenterDistanceSquared;
    }

    public string ContactId { get; }

    public BigInteger ActualCenterDistanceSquared { get; }

    public BigInteger ExpectedCenterDistanceSquared { get; }

    public BigInteger ResidualSquared => ActualCenterDistanceSquared - ExpectedCenterDistanceSquared;

    public bool IsExact => ResidualSquared.IsZero;
}

public sealed class SpatialValidationBundle
{
    public SpatialValidationBundle(
        IEnumerable<Diagnostic> diagnostics,
        IEnumerable<SpatialContactReadback> contactReadbacks,
        int unrelatedSameLayerPairChecks)
    {
        Report = new ValidationReport(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
        ContactReadbacks = (contactReadbacks ?? throw new ArgumentNullException(nameof(contactReadbacks)))
            .OrderBy(item => item.ContactId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        UnrelatedSameLayerPairChecks = unrelatedSameLayerPairChecks;
    }

    public ValidationReport Report { get; }

    public bool IsValid => Report.IsValid;

    public ReadOnlyCollection<Diagnostic> Diagnostics => Report.Diagnostics;

    public ReadOnlyCollection<SpatialContactReadback> ContactReadbacks { get; }

    public int UnrelatedSameLayerPairChecks { get; }
}
