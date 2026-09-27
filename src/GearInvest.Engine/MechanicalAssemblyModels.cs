using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public static class MechanicalAssemblyProfile
{
    public const string Id = "single-root-affine-prefix-terminal-device-assembly-v1";
    public const string AnalysisPolicy = "actual-shaft-affine-prefix-terminal-mechanics-v1";
    public const string GenevaAffineSuffixId = "single-root-geneva-affine-suffix-assembly-v1";
    public const string GenevaAffineSuffixAnalysisPolicy = "actual-shaft-geneva-affine-suffix-mechanics-v1";
    public const int MaxGenevaSuffixDepth = 2;
    public static bool IsSupported(string profile) => profile == Id || profile == GenevaAffineSuffixId;
    public static string PolicyFor(string profile) => profile == GenevaAffineSuffixId ? GenevaAffineSuffixAnalysisPolicy : AnalysisPolicy;
    public const int MaxMembers = 8, MaxDepth = 4, MaxAffineDepth = 3, MaxTerminals = 4, MaxOutputs = 16;
    public const int MaxDocumentBytes = 32 * 1024 * 1024, MaxDocumentDepth = 64, MaxJsonNodes = 262144;
    public const int MaxSeedBlobs = 8, MaxSeedBytes = 16 * 1024 * 1024;
    public const int MaxSeedCorrespondence = 1024;
    public const int MaxBatches = 128, MaxTotalOperations = 512, MaxComparisons = 64;
    public const int MaxInventoryEntries = 32768;
    internal static string Q(ExactQuantity value) => Pack(value.Kind.ToString(), value.Unit, F(value.Value));
    internal static string Nullable(Rational? value) => value.HasValue ? F(value.Value) : "";
    internal static void Terminal(ShaftPort port)
    {
        MechanicalAuthoringProfile.IdValue(port.Id); MechanicalAuthoringProfile.IdValue(port.ShaftId);
        MechanicalAuthoringProfile.FrameBound(port.Frame); MechanicalAuthoringProfile.Number(port.PhaseOffset);
        if (!Enum.IsDefined(typeof(ShaftConnectionKind), port.Kind)) throw new ArgumentException("Unknown terminal kind.");
    }
}

public enum AssemblyOwnerKind { Root, Member }
public enum AssemblyComponentKind { Shaft, Body, Port, Output, Feature, LinearDof, Constraint }
public enum AssemblyDeviceKind { WormDrive, OpenBelt, PitchChain, Geneva, CrankSlider, CamFollower }
public enum AssemblyTargetBasis { GlobalRoot, MemberInput }
public enum AssemblyOutputCapability { AffineRotaryShaft, IntermittentRotaryOutput, NonlinearPrismaticOutput }

/// <summary>Structured identity; member/local values are never joined into an ambiguous delimiter key.</summary>
public sealed class AssemblyComponentReference : IEquatable<AssemblyComponentReference>
{
    public AssemblyComponentReference(AssemblyOwnerKind owner, string? memberId, AssemblyComponentKind kind, string localId)
    {
        if (!Enum.IsDefined(typeof(AssemblyOwnerKind), owner) || !Enum.IsDefined(typeof(AssemblyComponentKind), kind)) throw new ArgumentException("Unknown assembly reference kind.");
        if (owner == AssemblyOwnerKind.Root && memberId is not null || owner == AssemblyOwnerKind.Member && memberId is null) throw new ArgumentException("Root and member ownership are distinct.");
        Owner = owner; MemberId = memberId is null ? null : MechanicalAuthoringProfile.IdValue(memberId); Kind = kind;
        LocalId = MechanicalAuthoringProfile.IdValue(localId);
    }
    public AssemblyOwnerKind Owner { get; }
    public string? MemberId { get; }
    public AssemblyComponentKind Kind { get; }
    public string LocalId { get; }
    public string CanonicalRepresentation => Pack(Owner.ToString(), MemberId ?? "", Kind.ToString(), LocalId);
    public static AssemblyComponentReference Root(AssemblyComponentKind kind, string localId) => new(AssemblyOwnerKind.Root, null, kind, localId);
    public static AssemblyComponentReference Member(string memberId, AssemblyComponentKind kind, string localId) => new(AssemblyOwnerKind.Member, memberId, kind, localId);
    public bool Equals(AssemblyComponentReference? other) => other is not null &&
        Owner == other.Owner && Kind == other.Kind &&
        StringComparer.Ordinal.Equals(MemberId, other.MemberId) &&
        StringComparer.Ordinal.Equals(LocalId, other.LocalId);
    public override bool Equals(object? other) => other is AssemblyComponentReference reference && Equals(reference);
    public override int GetHashCode()
    {
        // Runtime lookup only; persisted identity still uses the unchanged canonical representation.
        unchecked
        {
            var hash = (int)Owner;
            hash = (hash * 397) ^ (int)Kind;
            hash = (hash * 397) ^ (MemberId is null ? 0 : StringComparer.Ordinal.GetHashCode(MemberId));
            return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(LocalId);
        }
    }
}

public sealed class UpstreamShaftBinding
{
    public UpstreamShaftBinding(AssemblyComponentReference upstreamShaft, ExactQuantity mountingStation, string? mountingPortId = null)
    {
        UpstreamShaft = upstreamShaft ?? throw new ArgumentNullException(nameof(upstreamShaft));
        RotaryLinearProfile.Quantity(mountingStation); MountingStation = mountingStation;
        MountingPortId = mountingPortId is null ? null : MechanicalAuthoringProfile.IdValue(mountingPortId);
    }
    public AssemblyComponentReference UpstreamShaft { get; }
    public ExactQuantity MountingStation { get; }
    public string? MountingPortId { get; }
    public string CanonicalRepresentation => Pack(UpstreamShaft.CanonicalRepresentation, MechanicalAssemblyProfile.Q(MountingStation), MountingPortId ?? "");
}

/// <summary>Source-neutral authored member state. Only the closed six-family declaration set is supported.</summary>
public abstract class AssemblyMemberDeclaration
{
    private protected AssemblyMemberDeclaration(AssemblyDeviceKind kind, IEnumerable<string>? requiredDomains, AssemblyTargetBasis targetBasis)
    {
        if (!Enum.IsDefined(typeof(AssemblyTargetBasis), targetBasis)) throw new ArgumentException("Unknown target input basis.");
        Kind = kind; TargetBasis = targetBasis;
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredDomains ?? Array.Empty<string>(), x => x, 32);
    }
    public AssemblyDeviceKind Kind { get; }
    public AssemblyTargetBasis TargetBasis { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public abstract string LocalDeviceId { get; }
    public abstract string InputShaftId { get; }
    public abstract string? InputPortId { get; }
    public abstract string OutputKey { get; }
    public abstract string CanonicalRepresentation { get; }
    public string DeclarationId => HashText(CanonicalRepresentation);
    protected string PackDeclaration(params string[] fields) => Pack(Kind.ToString(), TargetBasis.ToString(), Pack(fields), Pack(RequiredValidationDomains.ToArray()));
}

public sealed class AssemblyWormDeclaration : AssemblyMemberDeclaration
{
    public AssemblyWormDeclaration(WormDriveTransmissionDefinition device, ShaftPort outputTerminal, MechanicalOutput output,
        Rational? requiredOutputPhase = null, IEnumerable<string>? requiredValidationDomains = null, AssemblyTargetBasis targetBasis = AssemblyTargetBasis.GlobalRoot)
        : base(AssemblyDeviceKind.WormDrive, requiredValidationDomains, targetBasis)
    { Device = device ?? throw new ArgumentNullException(nameof(device)); OutputTerminal = outputTerminal ?? throw new ArgumentNullException(nameof(outputTerminal)); MechanicalAssemblyProfile.Terminal(OutputTerminal); Output = output ?? throw new ArgumentNullException(nameof(output)); RequiredOutputPhase = requiredOutputPhase; if (requiredOutputPhase.HasValue) MechanicalAuthoringProfile.Number(requiredOutputPhase.Value); }
    public WormDriveTransmissionDefinition Device { get; }
    public ShaftPort OutputTerminal { get; }
    public MechanicalOutput Output { get; }
    public Rational? RequiredOutputPhase { get; }
    public override string LocalDeviceId => Device.Id;
    public override string InputShaftId => Device.InputShaftId;
    public override string? InputPortId => Device.InputPortId;
    public override string OutputKey => Output.Key;
    public override string CanonicalRepresentation => PackDeclaration(Device.CanonicalRepresentation, WormDriveProfile.Port(OutputTerminal), WormDriveProfile.Output(Output), MechanicalAssemblyProfile.Nullable(RequiredOutputPhase));
}

public sealed class AssemblyOpenBeltDeclaration : AssemblyMemberDeclaration
{
    public AssemblyOpenBeltDeclaration(OpenBeltTransmissionDefinition device, ShaftPort outputTerminal, MechanicalOutput output,
        Rational? requiredOutputPhase = null, IEnumerable<string>? requiredValidationDomains = null, AssemblyTargetBasis targetBasis = AssemblyTargetBasis.GlobalRoot)
        : base(AssemblyDeviceKind.OpenBelt, requiredValidationDomains, targetBasis)
    { Device = device ?? throw new ArgumentNullException(nameof(device)); OutputTerminal = outputTerminal ?? throw new ArgumentNullException(nameof(outputTerminal)); MechanicalAssemblyProfile.Terminal(OutputTerminal); Output = output ?? throw new ArgumentNullException(nameof(output)); RequiredOutputPhase = requiredOutputPhase; if (requiredOutputPhase.HasValue) MechanicalAuthoringProfile.Number(requiredOutputPhase.Value); }
    public OpenBeltTransmissionDefinition Device { get; }
    public ShaftPort OutputTerminal { get; }
    public MechanicalOutput Output { get; }
    public Rational? RequiredOutputPhase { get; }
    public override string LocalDeviceId => Device.Id;
    public override string InputShaftId => Device.InputShaftId;
    public override string? InputPortId => Device.InputPortId;
    public override string OutputKey => Output.Key;
    public override string CanonicalRepresentation => PackDeclaration(Device.CanonicalRepresentation, WormDriveProfile.Port(OutputTerminal), WormDriveProfile.Output(Output), MechanicalAssemblyProfile.Nullable(RequiredOutputPhase));
}

public sealed class AssemblyPitchChainDeclaration : AssemblyMemberDeclaration
{
    public AssemblyPitchChainDeclaration(PitchChainTransmissionDefinition device, ShaftPort outputTerminal, MechanicalOutput output,
        Rational? requiredOutputPhase = null, IEnumerable<string>? requiredValidationDomains = null, AssemblyTargetBasis targetBasis = AssemblyTargetBasis.GlobalRoot)
        : base(AssemblyDeviceKind.PitchChain, requiredValidationDomains, targetBasis)
    { Device = device ?? throw new ArgumentNullException(nameof(device)); OutputTerminal = outputTerminal ?? throw new ArgumentNullException(nameof(outputTerminal)); MechanicalAssemblyProfile.Terminal(OutputTerminal); Output = output ?? throw new ArgumentNullException(nameof(output)); RequiredOutputPhase = requiredOutputPhase; if (requiredOutputPhase.HasValue) MechanicalAuthoringProfile.Number(requiredOutputPhase.Value); }
    public PitchChainTransmissionDefinition Device { get; }
    public ShaftPort OutputTerminal { get; }
    public MechanicalOutput Output { get; }
    public Rational? RequiredOutputPhase { get; }
    public override string LocalDeviceId => Device.Id;
    public override string InputShaftId => Device.InputShaftId;
    public override string? InputPortId => Device.InputPortId;
    public override string OutputKey => Output.Key;
    public override string CanonicalRepresentation => PackDeclaration(Device.CanonicalRepresentation, WormDriveProfile.Port(OutputTerminal), WormDriveProfile.Output(Output), MechanicalAssemblyProfile.Nullable(RequiredOutputPhase));
}

public sealed class AssemblyGenevaDeclaration : AssemblyMemberDeclaration
{
    public AssemblyGenevaDeclaration(GenevaDeviceDefinition device, GenevaOutputDefinition output, GenevaRequirement requirement,
        IEnumerable<string>? requiredValidationDomains = null, AssemblyTargetBasis targetBasis = AssemblyTargetBasis.GlobalRoot)
        : base(AssemblyDeviceKind.Geneva, requiredValidationDomains, targetBasis)
    { Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output)); Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement)); }
    public GenevaDeviceDefinition Device { get; }
    public GenevaOutputDefinition Output { get; }
    public GenevaRequirement Requirement { get; }
    public override string LocalDeviceId => Device.Id;
    public override string InputShaftId => Device.SourceShaftId;
    public override string? InputPortId => Device.SourcePortId;
    public override string OutputKey => Output.Key;
    public override string CanonicalRepresentation => PackDeclaration(Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation);
}

public sealed class AssemblyCrankSliderDeclaration : AssemblyMemberDeclaration
{
    public AssemblyCrankSliderDeclaration(PlanarCrankSliderDefinition device, PrismaticOutputDefinition output, CrankSliderRequirement requirement,
        IEnumerable<string>? requiredValidationDomains = null, AssemblyTargetBasis targetBasis = AssemblyTargetBasis.GlobalRoot)
        : base(AssemblyDeviceKind.CrankSlider, requiredValidationDomains, targetBasis)
    { Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output)); Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement)); }
    public PlanarCrankSliderDefinition Device { get; }
    public PrismaticOutputDefinition Output { get; }
    public CrankSliderRequirement Requirement { get; }
    public override string LocalDeviceId => Device.Id;
    public override string InputShaftId => Device.SourceShaftId;
    public override string? InputPortId => Device.SourcePortId;
    public override string OutputKey => Output.Key;
    public override string CanonicalRepresentation => PackDeclaration(Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation);
}

public sealed class AssemblyCamFollowerDeclaration : AssemblyMemberDeclaration
{
    public AssemblyCamFollowerDeclaration(FlatCamFollowerDefinition device, PrismaticOutputDefinition output, CamFollowerRequirement requirement,
        IEnumerable<string>? requiredValidationDomains = null, AssemblyTargetBasis targetBasis = AssemblyTargetBasis.GlobalRoot)
        : base(AssemblyDeviceKind.CamFollower, requiredValidationDomains, targetBasis)
    { Device = device ?? throw new ArgumentNullException(nameof(device)); Output = output ?? throw new ArgumentNullException(nameof(output)); Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement)); }
    public FlatCamFollowerDefinition Device { get; }
    public PrismaticOutputDefinition Output { get; }
    public CamFollowerRequirement Requirement { get; }
    public override string LocalDeviceId => Device.Id;
    public override string InputShaftId => Device.SourceShaftId;
    public override string? InputPortId => Device.SourcePortId;
    public override string OutputKey => Output.Key;
    public override string CanonicalRepresentation => PackDeclaration(Device.CanonicalRepresentation, Output.CanonicalRepresentation, Requirement.CanonicalRepresentation);
}

public sealed class MechanicalAssemblyMember
{
    public MechanicalAssemblyMember(string instanceId, AssemblyMemberDeclaration declaration, UpstreamShaftBinding? inputBinding)
    { InstanceId = MechanicalAuthoringProfile.IdValue(instanceId); Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration)); InputBinding = inputBinding; }
    public string InstanceId { get; }
    public AssemblyMemberDeclaration Declaration { get; }
    public UpstreamShaftBinding? InputBinding { get; }
    public string CanonicalRepresentation => Pack(InstanceId, Declaration.CanonicalRepresentation, InputBinding?.CanonicalRepresentation ?? "");
}

public sealed class AssemblyOutputBinding
{
    public AssemblyOutputBinding(string key, AssemblyComponentReference target, Rational? requiredGlobalTransfer = null,
        ExactQuantity? requiredReferenceValue = null, ExactQuantity? referenceRoot = null)
    {
        Key = MechanicalAuthoringProfile.IdValue(key); Target = target ?? throw new ArgumentNullException(nameof(target));
        if (requiredGlobalTransfer.HasValue) MechanicalAuthoringProfile.Number(requiredGlobalTransfer.Value);
        RequiredGlobalTransfer = requiredGlobalTransfer; RequiredReferenceValue = requiredReferenceValue; ReferenceRoot = referenceRoot ?? ExactQuantity.Turns(0);
        RotaryLinearProfile.Quantity(ReferenceRoot); if (requiredReferenceValue.HasValue) RotaryLinearProfile.Quantity(requiredReferenceValue.Value);
    }
    public string Key { get; }
    public AssemblyComponentReference Target { get; }
    public Rational? RequiredGlobalTransfer { get; }
    public ExactQuantity? RequiredReferenceValue { get; }
    public ExactQuantity ReferenceRoot { get; }
    public string CanonicalRepresentation => Pack(Key, Target.CanonicalRepresentation, MechanicalAssemblyProfile.Nullable(RequiredGlobalTransfer),
        RequiredReferenceValue.HasValue ? MechanicalAssemblyProfile.Q(RequiredReferenceValue.Value) : "", MechanicalAssemblyProfile.Q(ReferenceRoot));
}

public sealed class AssemblyImportCorrespondence
{
    public AssemblyImportCorrespondence(MechanicalReference original, AssemblyComponentReference current)
    { Original = original ?? throw new ArgumentNullException(nameof(original)); MechanicalAuthoringProfile.IdValue(original.Kind); MechanicalAuthoringProfile.IdValue(original.Id); Current = current ?? throw new ArgumentNullException(nameof(current)); }
    public MechanicalReference Original { get; }
    public AssemblyComponentReference Current { get; }
    public string CanonicalRepresentation => Pack(Original.Kind, Original.Id, Current.CanonicalRepresentation);
}

public sealed class AssemblySeedImport
{
    private readonly byte[] originalBytes;
    public AssemblySeedImport(string instanceId, byte[] originalArtifactBytes, string originalArtifactIdentity,
        string originalRootDefinitionId, IEnumerable<AssemblyImportCorrespondence> correspondence)
    {
        InstanceId = MechanicalAuthoringProfile.IdValue(instanceId);
        if (originalArtifactBytes is null) throw new ArgumentNullException(nameof(originalArtifactBytes));
        if (originalArtifactBytes.Length == 0 || originalArtifactBytes.Length > MechanicalAssemblyProfile.MaxSeedBytes) throw new ArgumentException("Assembly seed byte bound exceeded.");
        originalBytes = (byte[])originalArtifactBytes.Clone();
        OriginalArtifactIdentity = MechanicalAuthoringProfile.IdValue(originalArtifactIdentity); OriginalRootDefinitionId = MechanicalAuthoringProfile.IdValue(originalRootDefinitionId);
        var mappings = (correspondence ?? throw new ArgumentNullException(nameof(correspondence))).Take(MechanicalAssemblyProfile.MaxSeedCorrespondence + 1).ToArray();
        if (mappings.Length > MechanicalAssemblyProfile.MaxSeedCorrespondence || mappings.Any(x => x is null) || mappings.Select(x => Pack(x.Original.Kind, x.Original.Id)).Distinct(StringComparer.Ordinal).Count() != mappings.Length)
            throw new ArgumentException("Seed correspondence must be bounded and single-valued for each original typed identity.");
        Correspondence = mappings.OrderBy(x => x.CanonicalRepresentation, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public string InstanceId { get; }
    public byte[] OriginalArtifactBytes => (byte[])originalBytes.Clone();
    internal int ByteLength => originalBytes.Length;
    public string OriginalArtifactIdentity { get; }
    public string OriginalRootDefinitionId { get; }
    public ReadOnlyCollection<AssemblyImportCorrespondence> Correspondence { get; }
    public string CanonicalRepresentation => Pack(InstanceId, OriginalArtifactIdentity, OriginalRootDefinitionId,
        System.Convert.ToBase64String(originalBytes), Pack(Correspondence.Select(x => x.CanonicalRepresentation).ToArray()));
}

public sealed class MechanicalAssemblyDefinition
{
    public MechanicalAssemblyDefinition(MechanicalDraft root, SourceLengthMapping? rootMapping, IEnumerable<MechanicalAssemblyMember> members,
        IEnumerable<AssemblyOutputBinding>? outputs = null, IEnumerable<string>? requiredValidationDomains = null,
        IEnumerable<AssemblySeedImport>? seedImports = null, string profile = MechanicalAssemblyProfile.Id,
        AssemblyProfileOrigin? profileOrigin = null)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root)); RootMapping = rootMapping; Profile = MechanicalAuthoringProfile.IdValue(profile);
        // A small bounded draft can represent an unsupported topology for structured analysis; never silently truncate.
        Members = MechanicalAuthoringProfile.Set(members ?? throw new ArgumentNullException(nameof(members)), x => x.InstanceId, 32);
        Outputs = MechanicalAuthoringProfile.Set(outputs ?? Array.Empty<AssemblyOutputBinding>(), x => x.Key, 32);
        RequiredValidationDomains = MechanicalAuthoringProfile.Set(requiredValidationDomains ?? Array.Empty<string>(), x => x, 32);
        SeedImports = MechanicalAuthoringProfile.Set(seedImports ?? Array.Empty<AssemblySeedImport>(), x => x.InstanceId, MechanicalAssemblyProfile.MaxSeedBlobs);
        ProfileOrigin = profileOrigin;
        if (profileOrigin is not null && profile != MechanicalAssemblyProfile.GenevaAffineSuffixId) throw new ArgumentException("Only the explicit Geneva-suffix profile retains an upgrade origin.");
        if (SeedImports.Sum(x => (long)x.ByteLength) + (profileOrigin?.ByteLength ?? 0) > MechanicalAssemblyProfile.MaxSeedBytes) throw new ArgumentException("Aggregate assembly seed byte bound exceeded.");
        DefinitionId = HashText(CanonicalRepresentation);
    }
    public MechanicalDraft Root { get; }
    public SourceLengthMapping? RootMapping { get; }
    public string Profile { get; }
    public ReadOnlyCollection<MechanicalAssemblyMember> Members { get; }
    public ReadOnlyCollection<AssemblyOutputBinding> Outputs { get; }
    public ReadOnlyCollection<string> RequiredValidationDomains { get; }
    public ReadOnlyCollection<AssemblySeedImport> SeedImports { get; }
    public AssemblyProfileOrigin? ProfileOrigin { get; }
    public string DefinitionId { get; }
    public string CanonicalRepresentation => Pack(Profile, Root.DefinitionId, RootMapping?.CanonicalRepresentation ?? "", Pack(Members.Select(x => x.CanonicalRepresentation).ToArray()),
        Pack(Outputs.Select(x => x.CanonicalRepresentation).ToArray()), Pack(RequiredValidationDomains.ToArray()), Pack(SeedImports.Select(x => x.CanonicalRepresentation).ToArray())) +
        (ProfileOrigin is null ? "" : Pack("explicit-profile-origin-v1", ProfileOrigin.CanonicalRepresentation));
}

/// <summary>Immutable original 30A bytes. This is provenance, not a second live root or admitted context.</summary>
public sealed class AssemblyProfileOrigin
{
    private readonly byte[] bytes;
    public AssemblyProfileOrigin(byte[] originalDocumentBytes, string originalDraftId, string originalDocumentIdentity)
    {
        if (originalDocumentBytes is null || originalDocumentBytes.Length == 0 || originalDocumentBytes.Length > MechanicalAssemblyProfile.MaxSeedBytes)
            throw new ArgumentException("Assembly profile origin byte bound exceeded.");
        if (!IsHash(originalDraftId) || !IsHash(originalDocumentIdentity)) throw new ArgumentException("Original draft and document identity are required.");
        bytes = (byte[])originalDocumentBytes.Clone(); OriginalDraftId = originalDraftId; OriginalDocumentIdentity = originalDocumentIdentity;
    }
    public byte[] OriginalDocumentBytes => (byte[])bytes.Clone();
    internal int ByteLength => bytes.Length;
    public string OriginalDraftId { get; }
    public string OriginalDocumentIdentity { get; }
    public string CanonicalRepresentation => Pack(Convert.ToBase64String(bytes), OriginalDraftId, OriginalDocumentIdentity);
}

public sealed class MechanicalAssemblyDraft
{
    public MechanicalAssemblyDraft(MechanicalAssemblyDefinition definition, long revision = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition)); if (revision < 0) throw new ArgumentException("Nonnegative assembly revision required.");
        Revision = revision; DraftId = HashText(Pack(definition.DefinitionId, revision.ToString(CultureInfo.InvariantCulture), definition.Root.DraftId));
    }
    public MechanicalAssemblyDefinition Definition { get; }
    public long Revision { get; }
    public string DefinitionId => Definition.DefinitionId;
    public string DraftId { get; }
    public MechanicalAssemblyDraft WithDefinition(MechanicalAssemblyDefinition definition) => new(definition, checked(Revision + 1));
}
