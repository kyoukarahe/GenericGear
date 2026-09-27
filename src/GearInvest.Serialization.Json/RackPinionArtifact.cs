using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>New CP device remains separate from the complete retained rotary source. No rational-radius gear is inserted into its source artifact.</summary>
public sealed class RackPinionArtifact
{
    public const string Format = "gear-invest.rack-pinion-mechanism";
    public const string Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal RackPinionArtifact(string candidateId, string artifactHash, RackPinionDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved, RackPinionAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request;
        sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
    }
    public string CandidateId { get; } public string ArtifactHash { get; }
    public RackPinionDraft Request { get; } public RackPinionAnalysis Analysis { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class RackPinionArtifactWriteResult
{
    private readonly byte[] bytes;
    public RackPinionArtifactWriteResult(RackPinionArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public RackPinionArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class RackPinionJson
{
    /// <summary>Facade supplies a freshly finalized whole source; current rack derivation never trusts a saved law, radius or domain.</summary>
    public static RackPinionArtifactWriteResult WriteArtifact(RackPinionDraft request, byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved)
    {
        var analysis = RackPinionAnalyzer.Analyze(request);
        Require(analysis.IsMechanicallyValid, "An unresolved rack request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new RackPinionArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new RackPinionArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new(artifact, ArtifactDocument(artifact, true));
    }
    /// <summary>Fresh device cache check. The public facade also repeats actual source finalization and full definition/identity correspondence.</summary>
    public static RackPinionArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, RackPinionArtifact.Format);
        Require(S(p, "profile") == RackPinionProfile.Id && S(p, "numericSemantics") == RackPinionProfile.NumericSemantics, "Unsupported rack artifact profile or numeric semantics.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var sourceBytes = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, sourceBytes.ValueKind == JsonValueKind.Null ? null : sourceBytes.GetBytesFromBase64(),
            MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Rack identity, current request, fresh analysis, source inventory or derived cache mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(RackPinionArtifact artifact)
    {
        var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false));
        return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteArtifact(RackPinionArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(RackPinionArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, RackPinionArtifact.Format); w.WriteString("profile", RackPinionProfile.Id); w.WriteString("numericSemantics", RackPinionProfile.NumericSemantics); w.WriteString("candidateId", a.CandidateId);
        if (hash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Draft(w, a.Request);
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId); w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved); w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", a.Request.Definition.Source.Definition.Shafts, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedBodyIds", a.Request.Definition.Source.Definition.Bodies, (x, b) => x.WriteStringValue(b.Id));
        Array(w, "retainedOutputKeys", a.Request.Definition.Source.Definition.Outputs, (x, o) => x.WriteStringValue(o.Key));
        w.WriteString("pinionShaftId", a.Request.Definition.Device.PinionShaftId); w.WriteString("pinionPortId", a.Request.Definition.Device.PinionPortId); w.WriteEndObject();
        w.WriteStartObject("addedBodies"); w.WriteString("pinionBodyId", a.Request.Definition.Device.PinionBodyId); w.WriteString("rackBodyId", a.Request.Definition.Device.RackBodyId);
        w.WriteString("linearDofId", a.Request.Definition.Device.LinearDofId); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis); w.WriteEndObject();
    }));
}
