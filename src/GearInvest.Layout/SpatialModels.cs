using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Layout;

public enum SpatialBodyKind
{
    Gear,
}

public enum SpatialContactKind
{
    ExternalGearMesh,
}

public sealed class SpatialAxis
{
    public SpatialAxis(string id, BigInteger x, BigInteger y)
    {
        Id = RequireId(id, nameof(id));
        X = x;
        Y = y;
    }

    public string Id { get; }

    public BigInteger X { get; }

    public BigInteger Y { get; }

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable ID is required.", parameterName);
        }

        return value;
    }
}

public sealed class SpatialBody
{
    public SpatialBody(
        string id,
        SpatialBodyKind kind,
        string axisId,
        string dofId,
        int layer,
        int toothCount,
        BigInteger pitchRadius,
        Rational exactMountingPhase = default)
    {
        Id = RequireId(id, nameof(id));
        Kind = kind;
        AxisId = RequireId(axisId, nameof(axisId));
        DofId = RequireId(dofId, nameof(dofId));
        Layer = layer;
        ToothCount = toothCount;
        PitchRadius = pitchRadius;
        ExactMountingPhase = exactMountingPhase;
    }

    public string Id { get; }

    public SpatialBodyKind Kind { get; }

    public string AxisId { get; }

    public string DofId { get; }

    public int Layer { get; }

    public int ToothCount { get; }

    public BigInteger PitchRadius { get; }

    public Rational ExactMountingPhase { get; }

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable ID is required.", parameterName);
        }

        return value;
    }
}

public sealed class SpatialContact
{
    public SpatialContact(
        string id,
        SpatialContactKind kind,
        string constraintId,
        string bodyAId,
        string bodyBId)
    {
        Id = RequireId(id, nameof(id));
        Kind = kind;
        ConstraintId = RequireId(constraintId, nameof(constraintId));
        BodyAId = RequireId(bodyAId, nameof(bodyAId));
        BodyBId = RequireId(bodyBId, nameof(bodyBId));
    }

    public string Id { get; }

    public SpatialContactKind Kind { get; }

    public string ConstraintId { get; }

    public string BodyAId { get; }

    public string BodyBId { get; }

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A stable ID is required.", parameterName);
        }

        return value;
    }
}

public sealed class SpatialMechanism
{
    public SpatialMechanism(
        IEnumerable<SpatialAxis> axes,
        IEnumerable<SpatialBody> bodies,
        IEnumerable<SpatialContact> contacts)
    {
        Axes = Sort(axes, item => item.Id);
        Bodies = Sort(bodies, item => item.Id);
        Contacts = Sort(contacts, item => item.Id);
    }

    public ReadOnlyCollection<SpatialAxis> Axes { get; }

    public ReadOnlyCollection<SpatialBody> Bodies { get; }

    public ReadOnlyCollection<SpatialContact> Contacts { get; }

    private static ReadOnlyCollection<T> Sort<T>(IEnumerable<T> source, Func<T, string> idSelector)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var items = source.ToList();
        items.Sort((left, right) => StringComparer.Ordinal.Compare(idSelector(left), idSelector(right)));
        return items.AsReadOnly();
    }
}
