using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Complete source, authored mechanism and freshly derived exact reference recipe. Numeric samples have a separate lifecycle.</summary>
public sealed class CrankSliderArtifact
{
    public const string Format = "gear-invest.crank-slider-mechanism", Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal CrankSliderArtifact(string candidateId, string artifactHash, CrankSliderDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved, CrankSliderAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request; sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
        ReferenceRecipe = analysis.CreatePoseRecipe(ExactQuantity.Turns(0)) ?? throw new ArtifactFormatException("Normal crank-slider artifact requires determined root-zero exact recipe.");
    }
    public string CandidateId { get; } public string ArtifactHash { get; } public CrankSliderDraft Request { get; }
    public CrankSliderAnalysis Analysis { get; } public CrankSliderPoseRecipe ReferenceRecipe { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class CrankSliderArtifactWriteResult
{
    private readonly byte[] bytes;
    public CrankSliderArtifactWriteResult(CrankSliderArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public CrankSliderArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class CrankSliderJson
{
    public static CrankSliderArtifactWriteResult WriteArtifact(CrankSliderDraft request, byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved) => Guard(() =>
    {
        var analysis = CrankSliderAnalyzer.Analyze(request); Require(analysis.IsMechanicallyValid, "Unresolved crank-slider request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new CrankSliderArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new CrankSliderArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new CrankSliderArtifactWriteResult(artifact, ArtifactDocument(artifact, true));
    });
    public static CrankSliderArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, CrankSliderArtifact.Format);
        Require(S(p, "profile") == CrankSliderProfile.Id && S(p, "motionDomain") == CrankSliderProfile.MotionDomain && S(p, "exactSemantics") == CrankSliderProfile.AnalysisPolicy, "Unsupported crank-slider artifact semantics.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var raw = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, raw.ValueKind == JsonValueKind.Null ? null : raw.GetBytesFromBase64(), MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Crank-slider identity, current geometry/branch/function/proof/root-zero recipe or complete source inventory mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(CrankSliderArtifact artifact)
    { var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false)); return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash); }
    public static byte[] WriteArtifact(CrankSliderArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(CrankSliderArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, CrankSliderArtifact.Format); w.WriteString("profile", CrankSliderProfile.Id); w.WriteString("motionDomain", CrankSliderProfile.MotionDomain); w.WriteString("exactSemantics", CrankSliderProfile.AnalysisPolicy);
        w.WriteString("candidateId", a.CandidateId); if (hash) w.WriteString("artifactHash", a.ArtifactHash); w.WritePropertyName("request"); Draft(w, a.Request);
        var source = a.Request.Definition.Source.Definition; var d = a.Request.Definition.Device;
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId); w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved); w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", source.Shafts, (x, s) => x.WriteStringValue(s.Id)); Array(w, "retainedBodyIds", source.Bodies, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedContactIds", source.Contacts, (x, s) => x.WriteStringValue(s.Id)); Array(w, "retainedPortIds", source.Ports, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedConnectionIds", source.Connections, (x, s) => x.WriteStringValue(s.Id)); Array(w, "retainedOutputKeys", source.Outputs, (x, s) => x.WriteStringValue(s.Key));
        w.WriteString("sourceShaftId", d.SourceShaftId); w.WriteString("sourcePortId", d.SourcePortId); w.WriteEndObject();
        w.WriteStartObject("addedAssembly"); w.WriteString("crankBodyId", d.CrankBodyId); w.WriteString("rodBodyId", d.RodBodyId); w.WriteString("sliderBodyId", d.SliderBodyId);
        w.WriteString("crankPinId", d.CrankPinId); w.WriteString("sliderPinId", d.SliderPinId); w.WriteString("guideId", d.GuideId); w.WriteString("linearDofId", d.LinearDofId);
        Integer(w, "retainedShaftCount", source.Shafts.Count); Integer(w, "retainedGearBodyCount", source.Bodies.Count); Integer(w, "addedMovingBodyCount", 3);
        Integer(w, "prescribedDriverCount", source.Shafts.Count(s => s.IsPrescribed)); Integer(w, "sliderCoordinateCount", 1); w.WriteString("rodPoseAuthority", "two-endpoints-fixed-length"); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis); w.WritePropertyName("referenceRecipe"); PoseRecipe(w, a.ReferenceRecipe); w.WriteEndObject();
    }));
}
