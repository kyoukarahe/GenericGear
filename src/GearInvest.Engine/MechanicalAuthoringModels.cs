using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class MechanicalAuthoringProfile
{
    public const string Id = "bounded-mechanical-authoring-v1";
    public const string MotionDomain = "RotationalContinuousAffine";
    public const string AnalysisPolicy = "exact-declared-admitted-components-v1";
    public const string PlanarExport = "single-layer-external-spur-chain-v1";
    // Original planar pitch disks allow tangency; this is not either oriented clearance policy.
    public const string PlanarClearance = "planar-nonpenetrating-pitch-v1";
    public const int MaxShafts = 64, MaxBodies = 64, MaxContacts = 96, MaxPorts = 96,
        MaxConnections = 96, MaxOutputs = 32, MaxKeepOuts = 16, MaxOperations = 128,
        MaxInputDigits = 128, MaxDerivedDigits = 8192, MaxDocumentBytes = 16 * 1024 * 1024;
    internal static string IdValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl))
            throw new ArgumentException("Bounded nonempty stable ID required.");
        return value;
    }
    internal static ReadOnlyCollection<T> Set<T>(IEnumerable<T> values, Func<T, string> id, int bound)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        var list = values.Take(bound + 1).ToList();
        if (list.Count > bound || list.Any(v => v is null)) throw new ArgumentException("Mechanical collection exceeds its bound or contains null.");
        foreach (var item in list) IdValue(id(item));
        if (list.Select(id).Distinct(StringComparer.Ordinal).Count() != list.Count) throw new ArgumentException("Duplicate typed ID.");
        return list.OrderBy(id, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    internal static void Number(Rational value)
    {
        if (value.Numerator.ToString(CultureInfo.InvariantCulture).Length > MaxInputDigits ||
            value.Denominator.ToString(CultureInfo.InvariantCulture).Length > MaxInputDigits)
            throw new ArgumentException("Exact input digit bound exceeded.");
    }
    internal static void FrameBound(OrientedFrame f)
    {
        if (f is null) throw new ArgumentNullException(nameof(f));
        foreach (var v in new[] { f.Origin, f.X, f.Y, f.Z }) VectorBound(v);
    }
    internal static void VectorBound(ExactVector3 v) { Number(v.X); Number(v.Y); Number(v.Z); }
}

/// <summary>A geometry declaration; the transfer is derived by analysis, never cached in this definition.</summary>
public sealed class MechanicalContact
{
    public MechanicalContact(string id, OrientedContactKind kind, string bodyAId, string bodyBId, RightAnglePitchCone? cone = null)
    {
        Id = MechanicalAuthoringProfile.IdValue(id); BodyAId = MechanicalAuthoringProfile.IdValue(bodyAId);
        BodyBId = MechanicalAuthoringProfile.IdValue(bodyBId);
        if (!Enum.IsDefined(typeof(OrientedContactKind), kind)) throw new ArgumentException("Unsupported contact kind.");
        Kind = kind; Cone = cone;
    }
    public string Id { get; }
    public OrientedContactKind Kind { get; }
    public string BodyAId { get; }
    public string BodyBId { get; }
    public RightAnglePitchCone? Cone { get; }
}

public sealed class MechanicalOutput
{
    public MechanicalOutput(string key, string? shaftId, string? bodyId, string? portId,
        Rational? requiredTransfer = null, OrientedOutputRole? role = null,
        string? unresolvedReason = null, IEnumerable<MechanicalReference>? formerEndpoint = null)
    {
        Key = MechanicalAuthoringProfile.IdValue(key);
        ShaftId = shaftId is null ? null : MechanicalAuthoringProfile.IdValue(shaftId);
        BodyId = bodyId is null ? null : MechanicalAuthoringProfile.IdValue(bodyId);
        PortId = portId is null ? null : MechanicalAuthoringProfile.IdValue(portId);
        if (role.HasValue && !Enum.IsDefined(typeof(OrientedOutputRole), role.Value)) throw new ArgumentException("Unsupported output role.");
        if (requiredTransfer.HasValue) MechanicalAuthoringProfile.Number(requiredTransfer.Value);
        RequiredTransfer = requiredTransfer; Role = role; UnresolvedReason = unresolvedReason;
        FormerEndpoint = (formerEndpoint ?? Array.Empty<MechanicalReference>()).Take(4).OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        if (FormerEndpoint.Count > 3) throw new ArgumentException("At most three former endpoint references.");
        foreach (var r in FormerEndpoint) { MechanicalAuthoringProfile.IdValue(r.Kind); MechanicalAuthoringProfile.IdValue(r.Id); }
        if (unresolvedReason is not null) MechanicalAuthoringProfile.IdValue(unresolvedReason);
        if ((shaftId is null || bodyId is null || portId is null) && (shaftId is not null || bodyId is not null || portId is not null))
            throw new ArgumentException("Output endpoint is wholly bound or explicitly unresolved.");
    }
    public string Key { get; }
    public string? ShaftId { get; }
    public string? BodyId { get; }
    public string? PortId { get; }
    public Rational? RequiredTransfer { get; }
    public OrientedOutputRole? Role { get; }
    public string? UnresolvedReason { get; }
    public ReadOnlyCollection<MechanicalReference> FormerEndpoint { get; }
    public bool IsResolved => ShaftId is not null && BodyId is not null && PortId is not null && UnresolvedReason is null;
}

/// <summary>Bounded editable mechanics without a solution or historic proof. Bad mechanics can be analyzed and persisted.</summary>
public sealed class MechanicalDefinition
{
    public MechanicalDefinition(string? rootShaftId, IEnumerable<OrientedShaft> shafts, IEnumerable<OrientedGearBody> bodies,
        IEnumerable<MechanicalContact> contacts, IEnumerable<ShaftPort>? ports = null,
        IEnumerable<ShaftPortConnection>? connections = null, IEnumerable<MechanicalOutput>? outputs = null,
        IEnumerable<OrientedKeepOut>? keepOuts = null, string clearancePolicy = PitchClearancePolicy.Legacy,
        bool requireCrossComponentClearance = true)
        : this(rootShaftId, shafts, bodies, contacts, ports, connections, outputs, keepOuts,
            clearancePolicy, requireCrossComponentClearance, null) { }

    /// <summary>Explicit adoption of bounded parallel rotor placement; the legacy constructor never upgrades.</summary>
    public MechanicalDefinition(ParallelCoaxialLayout coaxialLayout, string? rootShaftId,
        IEnumerable<OrientedShaft> shafts, IEnumerable<OrientedGearBody> bodies,
        IEnumerable<MechanicalContact> contacts, IEnumerable<ShaftPort>? ports = null,
        IEnumerable<ShaftPortConnection>? connections = null, IEnumerable<MechanicalOutput>? outputs = null,
        IEnumerable<OrientedKeepOut>? keepOuts = null, string clearancePolicy = ParallelCoaxialLayout.ClearancePolicy,
        bool requireCrossComponentClearance = true)
        : this(rootShaftId, shafts, bodies, contacts, ports, connections, outputs, keepOuts,
            clearancePolicy, requireCrossComponentClearance, coaxialLayout ?? throw new ArgumentNullException(nameof(coaxialLayout))) { }

    private MechanicalDefinition(string? rootShaftId, IEnumerable<OrientedShaft> shafts, IEnumerable<OrientedGearBody> bodies,
        IEnumerable<MechanicalContact> contacts, IEnumerable<ShaftPort>? ports,
        IEnumerable<ShaftPortConnection>? connections, IEnumerable<MechanicalOutput>? outputs,
        IEnumerable<OrientedKeepOut>? keepOuts, string clearancePolicy, bool requireCrossComponentClearance,
        ParallelCoaxialLayout? coaxialLayout)
    {
        CoaxialLayout = coaxialLayout;
        RootShaftId = rootShaftId is null ? null : MechanicalAuthoringProfile.IdValue(rootShaftId);
        Shafts = MechanicalAuthoringProfile.Set(shafts, s => s.Id, MechanicalAuthoringProfile.MaxShafts);
        Bodies = MechanicalAuthoringProfile.Set(bodies, b => b.Id, MechanicalAuthoringProfile.MaxBodies);
        Contacts = MechanicalAuthoringProfile.Set(contacts, c => c.Id, MechanicalAuthoringProfile.MaxContacts);
        Ports = MechanicalAuthoringProfile.Set(ports ?? Array.Empty<ShaftPort>(), p => p.Id, MechanicalAuthoringProfile.MaxPorts);
        Connections = MechanicalAuthoringProfile.Set(connections ?? Array.Empty<ShaftPortConnection>(), c => c.Id, MechanicalAuthoringProfile.MaxConnections);
        Outputs = MechanicalAuthoringProfile.Set(outputs ?? Array.Empty<MechanicalOutput>(), o => o.Key, MechanicalAuthoringProfile.MaxOutputs);
        KeepOuts = MechanicalAuthoringProfile.Set(keepOuts ?? Array.Empty<OrientedKeepOut>(), k => k.Id, MechanicalAuthoringProfile.MaxKeepOuts);
        if (clearancePolicy != PitchClearancePolicy.Legacy && clearancePolicy != PitchClearancePolicy.Refined && clearancePolicy != MechanicalAuthoringProfile.PlanarClearance &&
            !(coaxialLayout is not null && clearancePolicy == ParallelCoaxialLayout.ClearancePolicy)) throw new ArgumentException("Unknown clearance policy.");
        ClearancePolicy = clearancePolicy; RequireCrossComponentClearance = requireCrossComponentClearance;
        foreach (var s in Shafts) MechanicalAuthoringProfile.FrameBound(s.Frame);
        foreach (var b in Bodies)
        {
            MechanicalAuthoringProfile.IdValue(b.ShaftId); MechanicalAuthoringProfile.IdValue(b.SourceModuleId);
            MechanicalAuthoringProfile.FrameBound(b.MountingFrame); MechanicalAuthoringProfile.Number(b.OuterPitchRadius);
            if (!Enum.IsDefined(typeof(OrientedGearKind), b.Kind)) throw new ArgumentException("Unsupported body kind.");
        }
        foreach (var p in Ports) { MechanicalAuthoringProfile.FrameBound(p.Frame); MechanicalAuthoringProfile.Number(p.PhaseOffset); }
        foreach (var c in Connections) { MechanicalAuthoringProfile.IdValue(c.PortAId); MechanicalAuthoringProfile.IdValue(c.PortBId); MechanicalAuthoringProfile.Number(c.CoordinateTransfer); }
        foreach (var c in Contacts.Where(c => c.Cone is not null))
        {
            var cone = c.Cone!; foreach (var v in new[] { cone.Apex, cone.OutwardA, cone.OutwardB, cone.OuterContact }) MechanicalAuthoringProfile.VectorBound(v);
            MechanicalAuthoringProfile.Number(cone.InnerParameter); MechanicalAuthoringProfile.Number(cone.OuterScaleA); MechanicalAuthoringProfile.Number(cone.OuterScaleB);
        }
        foreach (var k in KeepOuts) { MechanicalAuthoringProfile.VectorBound(k.Envelope.Min); MechanicalAuthoringProfile.VectorBound(k.Envelope.Max); }
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public string? RootShaftId { get; }
    public ParallelCoaxialLayout? CoaxialLayout { get; }
    public string Profile => CoaxialLayout is null ? MechanicalAuthoringProfile.Id : ParallelCoaxialLayout.Profile;
    public ReadOnlyCollection<OrientedShaft> Shafts { get; }
    public ReadOnlyCollection<OrientedGearBody> Bodies { get; }
    public ReadOnlyCollection<MechanicalContact> Contacts { get; }
    public ReadOnlyCollection<ShaftPort> Ports { get; }
    public ReadOnlyCollection<ShaftPortConnection> Connections { get; }
    public ReadOnlyCollection<MechanicalOutput> Outputs { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
    public string ClearancePolicy { get; }
    public bool RequireCrossComponentClearance { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(Profile, RootShaftId ?? "", ClearancePolicy, RequireCrossComponentClearance ? "1" : "0",
        Pack(Shafts.Select(ShaftKey).ToArray()), Pack(Bodies.Select(BodyKey).ToArray()), Pack(Contacts.Select(ContactKey).ToArray()),
        Pack(Ports.Select(PortKey).ToArray()), Pack(Connections.Select(ConnectionKey).ToArray()), Pack(Outputs.Select(OutputKey).ToArray()), Pack(KeepOuts.Select(KeepOutKey).ToArray())) +
        (CoaxialLayout is null ? "" : Pack("explicit-parallel-coaxial-layout-v1", CoaxialLayoutKey(CoaxialLayout)));
    public static string CoaxialLayoutKey(ParallelCoaxialLayout layout) => Pack(Frame(layout.PlaneFrame), F(layout.PitchRadiusPerTooth),
        F(layout.LayerSpacing), N(layout.LayerCount), Pack(layout.Groups.Select(g => Pack(g.Id, Pack(g.ShaftIds.ToArray()))).ToArray()));
    public static string ShaftKey(OrientedShaft s) => Pack(s.Id, Frame(s.Frame), s.IsPrescribed ? "1" : "0");
    public static string BodyKey(OrientedGearBody b) => Pack(b.Id, b.ShaftId, b.Kind.ToString(), Frame(b.MountingFrame), N(b.Teeth), F(b.OuterPitchRadius), b.SourceModuleId);
    public static string ConeKey(RightAnglePitchCone? c) => c is null ? "" : Pack(V(c.Apex), V(c.OutwardA), V(c.OutwardB), V(c.OuterContact), F(c.InnerParameter), F(c.OuterScaleA), F(c.OuterScaleB));
    public static string ContactKey(MechanicalContact c) => Pack(c.Id, c.Kind.ToString(), c.BodyAId, c.BodyBId, ConeKey(c.Cone));
    public static string PortKey(ShaftPort p) => Pack(p.Id, p.ShaftId, Frame(p.Frame), F(p.PhaseOffset), p.Kind.ToString());
    public static string ConnectionKey(ShaftPortConnection c) => Pack(c.Id, c.PortAId, c.PortBId, F(c.CoordinateTransfer));
    public static string OutputKey(MechanicalOutput o) => Pack(o.Key, o.ShaftId ?? "", o.BodyId ?? "", o.PortId ?? "", o.RequiredTransfer.HasValue ? F(o.RequiredTransfer.Value) : "", o.Role?.ToString() ?? "", o.UnresolvedReason ?? "", Pack(o.FormerEndpoint.Select(r => Pack(r.Kind, r.Id)).ToArray()));
    public static string KeepOutKey(OrientedKeepOut k) => Pack(k.Id, V(k.Envelope.Min), V(k.Envelope.Max));
}

public sealed class MechanicalDraft
{
    private readonly byte[]? originalBytes;
    public MechanicalDraft(MechanicalDefinition definition, long revision = 0, byte[]? originalArtifactBytes = null,
        string? originalArtifactIdentity = null, string? originalDefinitionId = null, string? importedProfile = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (revision < 0) throw new ArgumentException("Revision must be nonnegative.");
        if (originalArtifactBytes is not null && originalArtifactBytes.Length > 4 * 1024 * 1024) throw new ArgumentException("Original artifact bound exceeded.");
        if ((originalArtifactBytes is null) != (originalArtifactIdentity is null) || (originalArtifactBytes is null) != (originalDefinitionId is null))
            throw new ArgumentException("Original bytes and identity/context must be supplied together.");
        Revision = revision; originalBytes = originalArtifactBytes is null ? null : (byte[])originalArtifactBytes.Clone();
        OriginalArtifactIdentity = originalArtifactIdentity; OriginalDefinitionId = originalDefinitionId; ImportedProfile = importedProfile;
        DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture),
            originalBytes is null ? "" : Hash(originalBytes), originalArtifactIdentity ?? "", originalDefinitionId ?? "", importedProfile ?? ""));
    }
    public MechanicalDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public byte[]? OriginalArtifactBytes => originalBytes is null ? null : (byte[])originalBytes.Clone();
    public string? OriginalArtifactIdentity { get; }
    public string? OriginalDefinitionId { get; }
    public string? ImportedProfile { get; }
    public MechanicalDraft WithDefinition(MechanicalDefinition definition) => new(definition, checked(Revision + 1), originalBytes, OriginalArtifactIdentity, OriginalDefinitionId, ImportedProfile);
}
