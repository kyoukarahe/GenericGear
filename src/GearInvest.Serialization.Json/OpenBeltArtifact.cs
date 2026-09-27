using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Complete retained source plus distinct pulley bodies, added shaft and an independently selected immutable belt length.</summary>
public sealed class OpenBeltArtifact
{
    public const string Format = "gear-invest.open-belt-mechanism";
    public const string Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal OpenBeltArtifact(string candidateId, string artifactHash, OpenBeltDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved, OpenBeltAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request;
        sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
    }
    public string CandidateId { get; } public string ArtifactHash { get; }
    public OpenBeltDraft Request { get; } public OpenBeltAnalysis Analysis { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class OpenBeltArtifactWriteResult
{
    private readonly byte[] bytes;
    public OpenBeltArtifactWriteResult(OpenBeltArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public OpenBeltArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class OpenBeltJson
{
    /// <summary>Facade supplies an actually finalized source. Current route, selected length and motion are recomputed, never read from the old envelope.</summary>
    public static OpenBeltArtifactWriteResult WriteArtifact(OpenBeltDraft request, byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved)
    {
        var analysis = OpenBeltAnalyzer.Analyze(request);
        Require(analysis.IsMechanicallyValid, "An unresolved open belt request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new OpenBeltArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new OpenBeltArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new(artifact, ArtifactDocument(artifact, true));
    }
    /// <summary>Fresh whole device derivation; the facade also verifies actual source provenance and source-aware finalization correspondence.</summary>
    public static OpenBeltArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, OpenBeltArtifact.Format);
        Require(S(p, "profile") == OpenBeltProfile.Id && S(p, "numericSemantics") == OpenBeltProfile.NumericSemantics, "Unsupported open belt artifact profile or numeric semantics.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var sourceBytes = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, sourceBytes.ValueKind == JsonValueKind.Null ? null : sourceBytes.GetBytesFromBase64(),
            MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Open belt identity, current request, fresh route/length/motion, source inventory or derived cache mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(OpenBeltArtifact artifact)
    {
        var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false));
        return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteArtifact(OpenBeltArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(OpenBeltArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, OpenBeltArtifact.Format); w.WriteString("profile", OpenBeltProfile.Id); w.WriteString("numericSemantics", OpenBeltProfile.NumericSemantics); w.WriteString("candidateId", a.CandidateId);
        if (hash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Draft(w, a.Request);
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId); w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved); w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", a.Request.Definition.Source.Definition.Shafts, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedBodyIds", a.Request.Definition.Source.Definition.Bodies, (x, b) => x.WriteStringValue(b.Id));
        Array(w, "retainedOutputKeys", a.Request.Definition.Source.Definition.Outputs, (x, o) => x.WriteStringValue(o.Key));
        w.WriteString("inputShaftId", a.Request.Definition.Device.InputShaftId); w.WriteString("inputPortId", a.Request.Definition.Device.InputPortId); w.WriteEndObject();
        w.WriteStartObject("addedAssembly"); w.WriteString("inputPulleyBodyId", a.Request.Definition.Device.InputPulleyBodyId);
        w.WriteString("outputPulleyBodyId", a.Request.Definition.Device.OutputPulleyBodyId); w.WriteString("outputShaftId", a.Request.Definition.Device.OutputShaft.Id);
        w.WriteString("outputTerminalId", a.Request.Definition.OutputTerminal.Id); w.WriteString("beltId", a.Request.Definition.Device.Id);
        w.WriteString("selectedBeltSpecificationId", a.Request.Definition.Device.SelectedBelt?.SpecificationId); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis); w.WriteEndObject();
    }));
}
