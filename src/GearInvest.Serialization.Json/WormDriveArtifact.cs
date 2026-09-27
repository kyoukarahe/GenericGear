using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Complete retained source plus distinct worm/wheel bodies, added shaft and an independently selected immutable ideal wheel.</summary>
public sealed class WormDriveArtifact
{
    public const string Format = "gear-invest.worm-drive-mechanism";
    public const string Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal WormDriveArtifact(string candidateId, string artifactHash, WormDriveDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved, WormDriveAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request;
        sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
        ReferenceEvaluation = WormDriveAnalyzer.Evaluate(analysis, ExactQuantity.Turns(0));
    }
    public string CandidateId { get; } public string ArtifactHash { get; }
    public WormDriveDraft Request { get; } public WormDriveAnalysis Analysis { get; }
    /// <summary>Derived absolute-root-zero material skeleton. Readers recompute it; replay does not adopt cached poses.</summary>
    public WormDriveEvaluation ReferenceEvaluation { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class WormDriveArtifactWriteResult
{
    private readonly byte[] bytes;
    public WormDriveArtifactWriteResult(WormDriveArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public WormDriveArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class WormDriveJson
{
    /// <summary>Facade supplies an actually finalized source. Current worm/wheel specifications and motion are recomputed, never read from the old envelope.</summary>
    public static WormDriveArtifactWriteResult WriteArtifact(WormDriveDraft request, byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved)
    {
        var analysis = WormDriveAnalyzer.Analyze(request);
        Require(analysis.IsMechanicallyValid, "An unresolved worm drive request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new WormDriveArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new WormDriveArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new(artifact, ArtifactDocument(artifact, true));
    }
    /// <summary>Fresh whole device derivation; the facade also verifies actual source provenance and source-aware finalization correspondence.</summary>
    public static WormDriveArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, WormDriveArtifact.Format);
        Require(S(p, "profile") == WormDriveProfile.Id && S(p, "geometrySemantics") == WormDriveProfile.GeometrySemantics && S(p, "poseSemantics") == WormDriveProfile.PoseSemantics, "Unsupported worm drive artifact profile or numeric semantics.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var sourceBytes = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, sourceBytes.ValueKind == JsonValueKind.Null ? null : sourceBytes.GetBytesFromBase64(),
            MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Worm drive identity, current request, fresh pitch/lead/local-trace/proof/motion, source inventory or derived cache mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(WormDriveArtifact artifact)
    {
        var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false));
        return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash);
    }
    public static byte[] WriteArtifact(WormDriveArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(WormDriveArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, WormDriveArtifact.Format); w.WriteString("profile", WormDriveProfile.Id); w.WriteString("geometrySemantics", WormDriveProfile.GeometrySemantics); w.WriteString("poseSemantics", WormDriveProfile.PoseSemantics); w.WriteString("candidateId", a.CandidateId);
        if (hash) w.WriteString("artifactHash", a.ArtifactHash);
        w.WritePropertyName("request"); Draft(w, a.Request);
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId); w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved); w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", a.Request.Definition.Source.Definition.Shafts, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedBodyIds", a.Request.Definition.Source.Definition.Bodies, (x, b) => x.WriteStringValue(b.Id));
        Array(w, "retainedContactIds", a.Request.Definition.Source.Definition.Contacts, (x, c) => x.WriteStringValue(c.Id));
        Array(w, "retainedPortIds", a.Request.Definition.Source.Definition.Ports, (x, p) => x.WriteStringValue(p.Id));
        Array(w, "retainedConnectionIds", a.Request.Definition.Source.Definition.Connections, (x, c) => x.WriteStringValue(c.Id));
        Array(w, "retainedOutputKeys", a.Request.Definition.Source.Definition.Outputs, (x, o) => x.WriteStringValue(o.Key));
        w.WriteString("inputShaftId", a.Request.Definition.Device.InputShaftId); w.WriteString("inputPortId", a.Request.Definition.Device.InputPortId); w.WriteEndObject();
        w.WriteStartObject("addedAssembly"); w.WriteString("inputWormBodyId", a.Request.Definition.Device.InputWormBodyId);
        w.WriteString("outputWheelBodyId", a.Request.Definition.Device.OutputWheelBodyId); w.WriteString("outputShaftId", a.Request.Definition.Device.OutputShaft.Id);
        w.WriteString("outputTerminalId", a.Request.Definition.OutputTerminal.Id); w.WriteString("transmissionId", a.Request.Definition.Device.Id);
        w.WriteString("wormSpecificationId", a.Request.Definition.Device.Worm.SpecificationId);
        w.WriteString("wheelSpecificationId", a.Request.Definition.Device.SelectedWheel?.SpecificationId);
        Integer(w, "rigidRotatingBodyCount", a.Request.Definition.Source.Definition.Bodies.Count + 2);
        Integer(w, "shaftDofCount", a.Analysis.Nodes.Count);
        Integer(w, "prescribedDriverCount", a.Analysis.Nodes.Count(n => n.IsPrescribed));
        Integer(w, "retainedSourceContactCount", a.Request.Definition.Source.Definition.Contacts.Count);
        Integer(w, "wormTransmissionCount", a.Analysis.LocalCompatibility.IsAdmitted ? 1 : 0); w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis);
        w.WritePropertyName("referenceEvaluation"); Evaluation(w, a.ReferenceEvaluation); w.WriteEndObject();
    }));
}
