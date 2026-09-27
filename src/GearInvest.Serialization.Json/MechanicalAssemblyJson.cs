using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

/// <summary>Strict 30A declarations. No serialized source context, law or admission flag is accepted as authority.</summary>
public static partial class MechanicalAssemblyJson
{
    public const string DraftFormat = "gear-invest.mechanical-assembly-draft";
    public const string BatchFormat = "gear-invest.mechanical-assembly-edit-batch";
    public const string SessionFormat = "gear-invest.mechanical-assembly-edit-session";
    public const string Version = "0.1";
    public const string GenevaSuffixVersion = "0.2";
    internal static string VersionFor(string profile) => profile == MechanicalAssemblyProfile.GenevaAffineSuffixId ? GenevaSuffixVersion : Version;
    public const int MaxDocumentBytes = MechanicalAssemblyProfile.MaxDocumentBytes;
    public static byte[] WriteDraft(MechanicalAssemblyDraft draft) => Guard(() => Encode(w => Draft(w, draft)));
    public static MechanicalAssemblyDraft ReadDraft(byte[] bytes) => Guard(() =>
    {
        using var document = Parse(bytes); var draft = Draft(document.RootElement);
        Require(bytes.SequenceEqual(WriteDraft(draft)), "Noncanonical or unknown assembly draft fields."); return draft;
    });
    public static byte[] WriteDefinition(MechanicalAssemblyDefinition definition) => Guard(() => Encode(w => Definition(w, definition)));
    public static byte[] WriteMemberDeclaration(AssemblyMemberDeclaration declaration) => Guard(() => Encode(w => Declaration(w, declaration)));
    public static AssemblyMemberDeclaration ReadMemberDeclaration(byte[] bytes) => Guard(() =>
    {
        using var document = Parse(bytes); var declaration = Declaration(document.RootElement);
        Require(bytes.SequenceEqual(WriteMemberDeclaration(declaration)), "Noncanonical or unknown assembly member declaration fields."); return declaration;
    });
    internal static void Draft(Utf8JsonWriter w, MechanicalAssemblyDraft draft)
    {
        Start(w, DraftFormat, VersionFor(draft.Definition.Profile)); w.WriteString("draftId", draft.DraftId); w.WriteString("definitionId", draft.DefinitionId);
        MechanicalAuthoringJson.Long(w, "revision", draft.Revision); w.WritePropertyName("definition"); Definition(w, draft.Definition); w.WriteEndObject();
    }
    internal static MechanicalAssemblyDraft Draft(JsonElement p)
    {
        Header(p, DraftFormat, true); var draft = new MechanicalAssemblyDraft(Definition(p.GetProperty("definition")), MechanicalAuthoringJson.Long(p, "revision"));
        Require(S(p, "formatVersion") == VersionFor(draft.Definition.Profile), "Assembly profile and wire version differ.");
        Require(draft.DraftId == S(p, "draftId") && draft.DefinitionId == S(p, "definitionId"), "Assembly draft or definition identity mismatch."); return draft;
    }
    internal static void Definition(Utf8JsonWriter w, MechanicalAssemblyDefinition definition)
    {
        w.WriteStartObject(); w.WriteString("profile", definition.Profile); w.WriteString("analysisPolicy", MechanicalAssemblyProfile.PolicyFor(definition.Profile));
        w.WritePropertyName("root"); MechanicalAuthoringJson.Draft(w, definition.Root);
        w.WritePropertyName("rootMapping"); R.Mapping(w, definition.RootMapping);
        Array(w, "members", definition.Members, Member); Array(w, "outputs", definition.Outputs, Output);
        MechanicalAuthoringJson.Strings(w, "requiredValidationDomains", definition.RequiredValidationDomains);
        Array(w, "seedImports", definition.SeedImports, Seed);
        if (definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId)
        {
            w.WritePropertyName("profileOrigin");
            if (definition.ProfileOrigin is null) w.WriteNullValue();
            else
            {
                var origin = definition.ProfileOrigin; w.WriteStartObject(); w.WriteBase64String("originalDocumentUtf8", origin.OriginalDocumentBytes);
                w.WriteString("originalDraftId", origin.OriginalDraftId); w.WriteString("originalDocumentIdentity", origin.OriginalDocumentIdentity); w.WriteEndObject();
            }
        }
        w.WriteEndObject();
    }
    internal static MechanicalAssemblyDefinition Definition(JsonElement p)
    {
        var profile = S(p, "profile");
        Require(MechanicalAssemblyProfile.IsSupported(profile) && S(p, "analysisPolicy") == MechanicalAssemblyProfile.PolicyFor(profile), "Unsupported assembly profile or analysis policy.");
        var root = p.GetProperty("root"); var original = root.GetProperty("importProvenance").GetProperty("originalArtifactUtf8");
        Require(original.ValueKind == JsonValueKind.Null || original.GetString()!.Length <= 4L * ((4 * 1024 * 1024 + 2L) / 3L), "Assembly root original byte bound exceeded.");
        // Unsupported but bounded topology remains a draft so analysis can give structured diagnostics.
        return new MechanicalAssemblyDefinition(MechanicalAuthoringJson.Draft(root), R.Mapping(p.GetProperty("rootMapping")),
            Items(p, "members", 32).Select(Member), Items(p, "outputs", 32).Select(Output),
            Items(p, "requiredValidationDomains", 32).Select(x => x.GetString()!),
            Seeds(p), profile, profile == MechanicalAssemblyProfile.GenevaAffineSuffixId ? ProfileOrigin(p.GetProperty("profileOrigin")) : null);
    }
    private static AssemblyProfileOrigin? ProfileOrigin(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Null) return null;
        var encoded = p.GetProperty("originalDocumentUtf8").GetString();
        Require(encoded is not null && encoded.Length <= 4L * ((MechanicalAssemblyProfile.MaxSeedBytes + 2L) / 3L), "Assembly profile origin encoded byte bound exceeded.");
        var bytes = Convert.FromBase64String(encoded!);
        ReadLegacyOriginFormat(bytes);
        return new(bytes, S(p, "originalDraftId"), S(p, "originalDocumentIdentity"));
    }
    public static string ReadLegacyOriginFormat(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement;
        var format = S(p, "format");
        Require(S(p, "formatVersion") == Version && format is DraftFormat or SessionFormat or MechanicalAssemblyArtifact.Format, "Only original 30A wire documents can be upgrade provenance.");
        var draft = format == DraftFormat ? p : format == SessionFormat ? p.GetProperty("initialDraft") : p.GetProperty("request").GetProperty("draft");
        Require(S(draft, "formatVersion") == Version && S(draft.GetProperty("definition"), "profile") == MechanicalAssemblyProfile.Id, "Original 30A profile required; nested upgrades are refused.");
        return format;
    });
    internal static void Reference(Utf8JsonWriter w, AssemblyComponentReference reference)
    {
        w.WriteStartObject(); w.WriteString("owner", reference.Owner.ToString()); w.WriteString("memberId", reference.MemberId);
        w.WriteString("kind", reference.Kind.ToString()); w.WriteString("localId", reference.LocalId); w.WriteEndObject();
    }
    internal static AssemblyComponentReference Reference(JsonElement p) => new(E<AssemblyOwnerKind>(p, "owner"),
        MechanicalAuthoringJson.NullableString(p, "memberId"), E<AssemblyComponentKind>(p, "kind"), S(p, "localId"));
    internal static void Binding(Utf8JsonWriter w, UpstreamShaftBinding? binding)
    {
        if (binding is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WritePropertyName("upstreamShaft"); Reference(w, binding.UpstreamShaft);
        R.Quantity(w, "mountingStation", binding.MountingStation); w.WriteString("mountingPortId", binding.MountingPortId); w.WriteEndObject();
    }
    internal static UpstreamShaftBinding? Binding(JsonElement p) => p.ValueKind == JsonValueKind.Null ? null :
        new(Reference(p.GetProperty("upstreamShaft")), R.Quantity(p.GetProperty("mountingStation")), MechanicalAuthoringJson.NullableString(p, "mountingPortId"));
    internal static void Member(Utf8JsonWriter w, MechanicalAssemblyMember member)
    {
        w.WriteStartObject(); w.WriteString("instanceId", member.InstanceId); w.WritePropertyName("declaration"); Declaration(w, member.Declaration);
        w.WritePropertyName("inputBinding"); Binding(w, member.InputBinding); w.WriteEndObject();
    }
    internal static MechanicalAssemblyMember Member(JsonElement p) => new(S(p, "instanceId"), Declaration(p.GetProperty("declaration")), Binding(p.GetProperty("inputBinding")));
    internal static void Declaration(Utf8JsonWriter w, AssemblyMemberDeclaration declaration)
    {
        w.WriteStartObject(); w.WriteString("kind", declaration.Kind.ToString()); w.WriteString("declarationId", declaration.DeclarationId); w.WritePropertyName("local");
        switch (declaration)
        {
            case AssemblyWormDeclaration d: WormDriveJson.AssemblyDeclaration(w, d); break;
            case AssemblyOpenBeltDeclaration d: OpenBeltJson.AssemblyDeclaration(w, d); break;
            case AssemblyPitchChainDeclaration d: PitchChainJson.AssemblyDeclaration(w, d); break;
            case AssemblyGenevaDeclaration d: GenevaJson.AssemblyDeclaration(w, d); break;
            case AssemblyCrankSliderDeclaration d: CrankSliderJson.AssemblyDeclaration(w, d); break;
            case AssemblyCamFollowerDeclaration d: CamFollowerJson.AssemblyDeclaration(w, d); break;
            default: throw new ArtifactFormatException("Unsupported assembly member family.");
        }
        w.WriteEndObject();
    }
    internal static AssemblyMemberDeclaration Declaration(JsonElement p)
    {
        var local = p.GetProperty("local"); AssemblyMemberDeclaration declaration = E<AssemblyDeviceKind>(p, "kind") switch
        {
            AssemblyDeviceKind.WormDrive => WormDriveJson.AssemblyDeclaration(local),
            AssemblyDeviceKind.OpenBelt => OpenBeltJson.AssemblyDeclaration(local),
            AssemblyDeviceKind.PitchChain => PitchChainJson.AssemblyDeclaration(local),
            AssemblyDeviceKind.Geneva => GenevaJson.AssemblyDeclaration(local),
            AssemblyDeviceKind.CrankSlider => CrankSliderJson.AssemblyDeclaration(local),
            AssemblyDeviceKind.CamFollower => CamFollowerJson.AssemblyDeclaration(local),
            _ => throw new ArtifactFormatException("Unsupported assembly member family.")
        };
        Require(declaration.DeclarationId == S(p, "declarationId"), "Assembly local declaration identity mismatch."); return declaration;
    }
    internal static void Output(Utf8JsonWriter w, AssemblyOutputBinding output)
    {
        w.WriteStartObject(); w.WriteString("key", output.Key); w.WritePropertyName("target"); Reference(w, output.Target);
        OptionalFraction(w, "requiredGlobalTransfer", output.RequiredGlobalTransfer); R.Quantity(w, "requiredReferenceValue", output.RequiredReferenceValue);
        R.Quantity(w, "referenceRoot", output.ReferenceRoot); w.WriteEndObject();
    }
    internal static AssemblyOutputBinding Output(JsonElement p) => new(S(p, "key"), Reference(p.GetProperty("target")),
        OptionalFraction(p, "requiredGlobalTransfer"), R.OptionalQuantity(p, "requiredReferenceValue"), R.Quantity(p.GetProperty("referenceRoot")));
    private static void Seed(Utf8JsonWriter w, AssemblySeedImport seed)
    {
        w.WriteStartObject(); w.WriteString("instanceId", seed.InstanceId); w.WriteBase64String("originalArtifactUtf8", seed.OriginalArtifactBytes);
        w.WriteString("originalArtifactIdentity", seed.OriginalArtifactIdentity); w.WriteString("originalRootDefinitionId", seed.OriginalRootDefinitionId);
        Array(w, "correspondence", seed.Correspondence, (writer, map) =>
        {
            writer.WriteStartObject(); writer.WriteStartObject("original"); writer.WriteString("kind", map.Original.Kind); writer.WriteString("id", map.Original.Id); writer.WriteEndObject();
            writer.WritePropertyName("current"); Reference(writer, map.Current); writer.WriteEndObject();
        }); w.WriteEndObject();
    }
    private static IEnumerable<AssemblySeedImport> Seeds(JsonElement p)
    {
        long total = 0;
        foreach (var seed in Items(p, "seedImports", MechanicalAssemblyProfile.MaxSeedBlobs))
        {
            var encoded = seed.GetProperty("originalArtifactUtf8").GetString();
            Require(encoded is not null && encoded.Length <= 4L * ((MechanicalAssemblyProfile.MaxSeedBytes + 2L) / 3L), "Assembly seed encoded byte bound exceeded.");
            var bytes = Convert.FromBase64String(encoded!); total += bytes.Length;
            Require(bytes.Length != 0 && total <= MechanicalAssemblyProfile.MaxSeedBytes, "Aggregate assembly seed byte bound exceeded.");
            yield return new AssemblySeedImport(S(seed, "instanceId"), bytes, S(seed, "originalArtifactIdentity"), S(seed, "originalRootDefinitionId"),
                Items(seed, "correspondence", MechanicalAssemblyProfile.MaxSeedCorrespondence).Select(x => new AssemblyImportCorrespondence(
                    new MechanicalReference(S(x.GetProperty("original"), "kind"), S(x.GetProperty("original"), "id")), Reference(x.GetProperty("current")))));
        }
    }
    internal static void Start(Utf8JsonWriter w, string format, string version = Version) { w.WriteStartObject(); w.WriteString("format", format); w.WriteString("formatVersion", version); }
    internal static void Header(JsonElement p, string format, bool allowGenevaSuffix = false) => Require(S(p, "format") == format &&
        (S(p, "formatVersion") == Version || allowGenevaSuffix && S(p, "formatVersion") == GenevaSuffixVersion), "Unsupported assembly format or version.");
    internal static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) write(writer);
        var bytes = stream.ToArray(); using var parsed = Parse(bytes); return bytes;
    }
    internal static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes is not null && bytes.Length > 0 && bytes.Length <= MaxDocumentBytes, "Assembly document byte bound exceeded.");
        var document = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = MechanicalAssemblyProfile.MaxDocumentDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        try
        {
            var pending = new Stack<JsonElement>(); pending.Push(document.RootElement); var nodes = 0;
            while (pending.Count != 0)
            {
                var current = pending.Pop(); Require(++nodes <= MechanicalAssemblyProfile.MaxJsonNodes, "Assembly JSON node bound exceeded.");
                if (current.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in current.EnumerateObject())
                    {
                        Require(names.Count < 64 && names.Add(property.Name), "Duplicate or excessive assembly object keys.");
                        Require(nodes + pending.Count < MechanicalAssemblyProfile.MaxJsonNodes, "Assembly JSON pending node bound exceeded."); pending.Push(property.Value);
                    }
                }
                else if (current.ValueKind == JsonValueKind.Array) foreach (var value in current.EnumerateArray())
                { Require(nodes + pending.Count < MechanicalAssemblyProfile.MaxJsonNodes, "Assembly JSON pending node bound exceeded."); pending.Push(value); }
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    internal static T Guard<T>(Func<T> action) => MechanicalAuthoringJson.Guard(action);
}
