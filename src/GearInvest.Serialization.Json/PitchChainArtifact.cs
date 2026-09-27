using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Complete retained source plus distinct sprocket bodies, added shaft and an independently selected immutable chain.</summary>
public sealed class PitchChainArtifact
{
    public const string Format = "gear-invest.pitch-chain-mechanism";
    public const string Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal PitchChainArtifact(string candidateId, string artifactHash, PitchChainDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved, PitchChainAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request;
        sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
        ReferenceEvaluation = PitchChainAnalyzer.Evaluate(analysis, ExactQuantity.Turns(0));
    }
    public string CandidateId { get; } public string ArtifactHash { get; }
    public PitchChainDraft Request { get; } public PitchChainAnalysis Analysis { get; }
    /// <summary>Derived absolute-root-zero material skeleton. Readers recompute it; replay does not adopt cached poses.</summary>
    public PitchChainEvaluation ReferenceEvaluation { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class PitchChainArtifactWriteResult
{
    private readonly byte[] bytes;
    public PitchChainArtifactWriteResult(PitchChainArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public PitchChainArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class PitchChainJson
{
    /// <summary>Facade supplies an actually finalized source. Current route, selected chain and motion are recomputed, never read from the old envelope.</summary>
    public static PitchChainArtifactWriteResult WriteArtifact(PitchChainDraft request, byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved)
    {
        var analysis = PitchChainAnalyzer.Analyze(request);
        Require(analysis.IsMechanicallyValid, "An unresolved pitch chain request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new PitchChainArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new PitchChainArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new(artifact, ArtifactDocument(artifact, true));
    }
    /// <summary>Fresh whole device derivation; the facade also verifies actual source provenance and source-aware finalization correspondence.</summary>
    public static PitchChainArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, PitchChainArtifact.Format);
        Require(S(p, "profile") == PitchChainProfile.Id && S(p, "geometrySemantics") == PitchChainProfile.GeometrySemantics && S(p, "poseSemantics") == PitchChainProfile.PoseSemantics, "Unsupported pitch chain artifact profile or numeric semantics.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var sourceBytes = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, sourceBytes.ValueKind == JsonValueKind.Null ? null : sourceBytes.GetBytesFromBase64(),
            MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Pitch chain identity, current request, fresh polygon/link-count/material/motion, source inventory or derived cache mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(PitchChainArtifact artifact)
    {
        var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false));
        return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteArtifact(PitchChainArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(PitchChainArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, PitchChainArtifact.Format); w.WriteString("profile", PitchChainProfile.Id); w.WriteString("geometrySemantics", PitchChainProfile.GeometrySemantics); w.WriteString("poseSemantics", PitchChainProfile.PoseSemantics); w.WriteString("candidateId", a.CandidateId);
        if (hash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Draft(w, a.Request);
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId); w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved); w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", a.Request.Definition.Source.Definition.Shafts, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedBodyIds", a.Request.Definition.Source.Definition.Bodies, (x, b) => x.WriteStringValue(b.Id));
        Array(w, "retainedOutputKeys", a.Request.Definition.Source.Definition.Outputs, (x, o) => x.WriteStringValue(o.Key));
        w.WriteString("inputShaftId", a.Request.Definition.Device.InputShaftId); w.WriteString("inputPortId", a.Request.Definition.Device.InputPortId); w.WriteEndObject();
        w.WriteStartObject("addedAssembly"); w.WriteString("inputSprocketBodyId", a.Request.Definition.Device.InputSprocketBodyId);
        w.WriteString("outputSprocketBodyId", a.Request.Definition.Device.OutputSprocketBodyId); w.WriteString("outputShaftId", a.Request.Definition.Device.OutputShaft.Id);
        w.WriteString("outputTerminalId", a.Request.Definition.OutputTerminal.Id); w.WriteString("transmissionId", a.Request.Definition.Device.Id);
        w.WriteString("selectedChainSpecificationId", a.Request.Definition.Device.SelectedChain?.SpecificationId);
        w.WriteString("chainId", a.Request.Definition.Device.SelectedChain?.ChainId);
        w.WriteString("materialIdSemantics", "chain-id-and-material-index-sha256-v1");
        Integer(w, "rigidRotatingBodyCount", a.Analysis.RigidRotatingBodyCount); Integer(w, "shaftDofCount", a.Analysis.ShaftDofCount);
        Integer(w, "chainPinCount", a.Analysis.AdmittedPinCount); Integer(w, "idealLinkCount", a.Analysis.AdmittedIdealLinkCount); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis);
        w.WritePropertyName("referenceEvaluation"); Evaluation(w, a.ReferenceEvaluation); w.WriteEndObject();
    }));
}
