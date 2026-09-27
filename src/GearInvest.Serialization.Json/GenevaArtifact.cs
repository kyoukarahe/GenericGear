using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Complete source, authored mechanism and freshly derived exact reference recipe. Numeric samples have a separate lifecycle.</summary>
public sealed class GenevaArtifact
{
    public const string Format = "gear-invest.geneva-mechanism", Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal GenevaArtifact(string candidateId, string artifactHash, GenevaDraft request, byte[]? finalizedSourceBytes,
        string? finalizedSourceIdentity, bool originalSourceBytesPreserved, GenevaAnalysis analysis)
    {
        CandidateId = candidateId; ArtifactHash = artifactHash; Request = request; sourceBytes = finalizedSourceBytes is null ? null : (byte[])finalizedSourceBytes.Clone();
        FinalizedSourceIdentity = finalizedSourceIdentity; OriginalSourceBytesPreserved = originalSourceBytesPreserved; Analysis = analysis;
        ReferenceRecipe = analysis.CreatePoseRecipe(ExactQuantity.Turns(0)) ?? throw new ArtifactFormatException("Normal geneva artifact requires determined root-zero exact recipe.");
    }
    public string CandidateId { get; } public string ArtifactHash { get; } public GenevaDraft Request { get; }
    public GenevaAnalysis Analysis { get; } public GenevaPoseRecipe ReferenceRecipe { get; }
    public byte[]? FinalizedSourceBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedSourceIdentity { get; } public bool OriginalSourceBytesPreserved { get; }
}
public sealed class GenevaArtifactWriteResult
{
    private readonly byte[] bytes;
    public GenevaArtifactWriteResult(GenevaArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public GenevaArtifact Artifact { get; } public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class GenevaJson
{
    public static GenevaArtifactWriteResult WriteArtifact(GenevaDraft request, byte[]? finalizedSourceBytes, string? finalizedSourceIdentity, bool originalSourceBytesPreserved) => Guard(() =>
    {
        var analysis = GenevaAnalyzer.Analyze(request, GenevaNumericRequest.Default); Require(analysis.IsMechanicallyValid, "Unresolved geneva request cannot be written as a normal artifact.");
        Require((finalizedSourceBytes is null) == (finalizedSourceIdentity is null), "Source bytes and identity must be supplied together.");
        Require(finalizedSourceBytes is null || finalizedSourceBytes.Length <= 4 * 1024 * 1024, "Final source byte bound exceeded.");
        var provisional = new GenevaArtifact(request.DefinitionId, "", request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        var artifact = new GenevaArtifact(request.DefinitionId, Hash(ArtifactDocument(provisional, false)), request, finalizedSourceBytes, finalizedSourceIdentity, originalSourceBytesPreserved, analysis);
        return new GenevaArtifactWriteResult(artifact, ArtifactDocument(artifact, true));
    });
    public static GenevaArtifact ReadArtifact(byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var p = doc.RootElement; Header(p, GenevaArtifact.Format);
        Require(S(p, "profile") == GenevaProfile.Id && S(p, "motionDomain") == GenevaProfile.MotionDomain && S(p, "exactSemantics") == GenevaProfile.AnalysisPolicy, "Unsupported geneva artifact semantics.");
        var request = Draft(p.GetProperty("request")); var source = p.GetProperty("finalizedSource"); var raw = source.GetProperty("artifactUtf8");
        var fresh = WriteArtifact(request, raw.ValueKind == JsonValueKind.Null ? null : raw.GetBytesFromBase64(), MechanicalAuthoringJson.NullableString(source, "artifactIdentity"), source.GetProperty("originalBytesPreserved").GetBoolean());
        Require(bytes.SequenceEqual(fresh.Bytes), "Geneva identity, current geometry/registration/pin-slot/lock/root-zero recipe or complete source inventory mismatch."); return fresh.Artifact;
    });
    public static ArtifactIdentityVerification VerifyIdentity(GenevaArtifact artifact)
    { var id = artifact.Request.DefinitionId; var hash = Hash(ArtifactDocument(artifact, false)); return new(id, hash, id == artifact.CandidateId, hash == artifact.ArtifactHash); }
    public static byte[] WriteArtifact(GenevaArtifact artifact) => ArtifactDocument(artifact, true);
    private static byte[] ArtifactDocument(GenevaArtifact a, bool hash) => Guard(() => Encode(w =>
    {
        Start(w, GenevaArtifact.Format); w.WriteString("profile", GenevaProfile.Id); w.WriteString("motionDomain", GenevaProfile.MotionDomain); w.WriteString("exactSemantics", GenevaProfile.AnalysisPolicy);
        w.WriteString("candidateId", a.CandidateId); if (hash) w.WriteString("artifactHash", a.ArtifactHash); w.WritePropertyName("request"); Draft(w, a.Request);
        var source = a.Request.Definition.Source.Definition; var d = a.Request.Definition.Device;
        w.WriteStartObject("finalizedSource"); w.WriteString("definitionId", a.Request.Definition.Source.DefinitionId); w.WriteString("artifactIdentity", a.FinalizedSourceIdentity);
        if (a.FinalizedSourceBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", a.FinalizedSourceBytes);
        w.WriteBoolean("originalBytesPreserved", a.OriginalSourceBytesPreserved); w.WriteString("bindingPolicy", "complete-definition-exact-retained-identifiers-v1");
        Array(w, "retainedShaftIds", source.Shafts, (x, s) => x.WriteStringValue(s.Id)); Array(w, "retainedBodyIds", source.Bodies, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedContactIds", source.Contacts, (x, s) => x.WriteStringValue(s.Id)); Array(w, "retainedPortIds", source.Ports, (x, s) => x.WriteStringValue(s.Id));
        Array(w, "retainedConnectionIds", source.Connections, (x, s) => x.WriteStringValue(s.Id)); Array(w, "retainedOutputKeys", source.Outputs, (x, s) => x.WriteStringValue(s.Key));
        w.WriteString("sourceShaftId", d.SourceShaftId); w.WriteString("sourcePortId", d.SourcePortId); w.WriteEndObject();
        w.WriteStartObject("addedAssembly");w.WriteString("driverBodyId",d.DriverBodyId);w.WriteString("pinId",d.PinId);w.WriteString("wheelBodyId",d.WheelBodyId);
        w.WriteString("outputShaftId",d.OutputShaft.Id);w.WriteString("driverLockFeatureId",d.IdealLock.DriverFeatureId);
        MechanicalAuthoringJson.Strings(w,"slotIds",d.Wheel.SlotIds);MechanicalAuthoringJson.Strings(w,"recessIds",d.IdealLock.RecessIds);
        Integer(w,"retainedShaftCount",source.Shafts.Count);Integer(w,"retainedGearBodyCount",source.Bodies.Count);Integer(w,"addedMovingBodyCount",2);
        Integer(w,"addedNonPrescribedShaftCount",1);Integer(w,"prescribedDriverCount",source.Shafts.Count(s=>s.IsPrescribed));
        w.WriteString("pinPolicy","ideal-point-pin-zero-clearance-straight-radial-slot");
        w.WriteString("lockingFeatureOwnership","driver-body-integral");w.WriteString("lockPolicy",d.IdealLock.Policy);
        w.WriteString("reliefSolidValidation","NotPerformed");w.WriteString("fullSweptClearance","NotPerformed");w.WriteEndObject();
        w.WritePropertyName("analysis"); Analysis(w, a.Analysis); w.WritePropertyName("referenceRecipe"); PoseRecipe(w, a.ReferenceRecipe); w.WriteEndObject();
    }));
}

