using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>A distinct mixed artifact; original source and current request remain independently inspectable.</summary>
public sealed class RotaryLinearArtifact
{
    public const string Format = "gear-invest.rotary-linear-mechanism";
    public const string Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal RotaryLinearArtifact(string candidateId, string artifactHash, RotaryLinearDraft request,
        byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved, RotaryLinearAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request;
        sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
    }
    public string CandidateId { get; } public string ArtifactHash { get; }
    public RotaryLinearDraft Request { get; } public RotaryLinearAnalysis Analysis { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class RotaryLinearArtifactWriteResult
{
    private readonly byte[] bytes;
    public RotaryLinearArtifactWriteResult(RotaryLinearArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public RotaryLinearArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class RotaryLinearJson
{
    /// <summary>Facade finalization supplies a freshly finalized complete source; this writer never invents source mappings.</summary>
    public static RotaryLinearArtifactWriteResult WriteArtifact(RotaryLinearDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved)
    {
        var analysis = RotaryLinearAnalyzer.Analyze(request);
        Require(analysis.IsMechanicallyValid, "An unresolved mixed request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new RotaryLinearArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new RotaryLinearArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new(artifact, ArtifactDocument(artifact, true));
    }
    /// <summary>Read recomputes mixed answers; the public SDK additionally freshly finalizes and verifies source correspondence.</summary>
    public static RotaryLinearArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, RotaryLinearArtifact.Format);
        Require(S(p, "profile") == RotaryLinearProfile.Id, "Unsupported mixed artifact profile.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var sourceBytes = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, sourceBytes.ValueKind == JsonValueKind.Null ? null : sourceBytes.GetBytesFromBase64(),
            MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Mixed identity, current request, fresh analysis, source record or derived cache mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(RotaryLinearArtifact artifact)
    {
        var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false));
        return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteArtifact(RotaryLinearArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(RotaryLinearArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, RotaryLinearArtifact.Format); w.WriteString("profile", RotaryLinearProfile.Id); w.WriteString("candidateId", a.CandidateId);
        if (hash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Draft(w, a.Request);
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId);
        w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved);
        w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", a.Request.Definition.Source.Definition.Shafts, (x, s) => x.WriteStringValue(s.Id));
        w.WriteString("screwShaftId", a.Request.Definition.Device.ScrewShaftId); w.WriteString("screwPortId", a.Request.Definition.Device.ScrewPortId); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis); w.WriteEndObject();
    }));
}
