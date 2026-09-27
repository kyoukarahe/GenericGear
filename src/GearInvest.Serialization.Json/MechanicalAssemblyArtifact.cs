using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public sealed class MechanicalAssemblyArtifactRequest
{
    internal MechanicalAssemblyArtifactRequest(MechanicalAssemblyDraft draft, AssemblyNumericRequest numericRequest) { Draft = draft; NumericRequest = numericRequest; }
    public MechanicalAssemblyDraft Draft { get; }
    public AssemblyNumericRequest NumericRequest { get; }
}

public sealed class MechanicalAssemblyArtifact
{
    public const string Format = "gear-invest.mechanical-assembly";
    public const string Version = "0.1";
    private readonly byte[]? sourceBytes;
    internal MechanicalAssemblyArtifact(string artifactHash, MechanicalAssemblyDraft request, byte[]? finalizedRootBytes,
        string? finalizedRootIdentity, bool originalRootBytesPreserved, MechanicalAssemblyAnalysis analysis, MechanicalAssemblyEvaluation reference)
    {
        ArtifactHash = artifactHash; Request = request; sourceBytes = finalizedRootBytes is null ? null : (byte[])finalizedRootBytes.Clone();
        FinalizedRootIdentity = finalizedRootIdentity; OriginalRootBytesPreserved = originalRootBytesPreserved; Analysis = analysis; ReferenceEvaluation = reference;
    }
    public string CandidateId => Request.DefinitionId;
    public string ArtifactHash { get; }
    public MechanicalAssemblyDraft Request { get; }
    public AssemblyNumericRequest NumericRequest => Analysis.NumericRequest;
    public byte[]? FinalizedRootBytes => sourceBytes is null ? null : (byte[])sourceBytes.Clone();
    public string? FinalizedRootIdentity { get; }
    public bool OriginalRootBytesPreserved { get; }
    public MechanicalAssemblyAnalysis Analysis { get; }
    public MechanicalAssemblyEvaluation ReferenceEvaluation { get; }
}

public sealed class MechanicalAssemblyArtifactWriteResult
{
    private readonly byte[] bytes;
    internal MechanicalAssemblyArtifactWriteResult(MechanicalAssemblyArtifact artifact, byte[] bytes) { Artifact = artifact; this.bytes = (byte[])bytes.Clone(); }
    public MechanicalAssemblyArtifact Artifact { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

public static partial class MechanicalAssemblyJson
{
    /// <summary>Source reconstruction is the sole known pending scope fulfilled by the facade after root finalize/reimport.</summary>
    public static bool IsReadyForReconstruction(MechanicalAssemblyAnalysis analysis) => analysis.TopologyValid && analysis.RootAnalysis.IsMechanicallyValid &&
        analysis.Members.All(x => x.HasDeterminedMotion && x.Target == MechanicalAxisVerdict.Pass && x.RequiredScopesSatisfied) &&
        analysis.Outputs.All(x => x.IsResolved && x.Target == MechanicalAxisVerdict.Pass) &&
        analysis.Checks.All(x => !x.Required || x.Verdict == OrientedCheckVerdict.Pass || x.Domain == "Source/MemberReconstruction" && x.Verdict == OrientedCheckVerdict.NotPerformed);
    /// <summary>Reads authoritative requests only. The facade finalizes the complete root and freshly rebuilds every derived record.</summary>
    public static MechanicalAssemblyArtifactRequest ReadArtifactRequest(byte[] bytes) => Guard(() =>
    {
        using var document = Parse(bytes); var p = document.RootElement; Header(p, MechanicalAssemblyArtifact.Format, true);
        var profile = S(p, "profile");
        Require(MechanicalAssemblyProfile.IsSupported(profile) && S(p, "analysisPolicy") == MechanicalAssemblyProfile.PolicyFor(profile) &&
            S(p, "formatVersion") == VersionFor(profile), "Unsupported assembly artifact profile or analysis policy.");
        var request = p.GetProperty("request"); var draft = Draft(request.GetProperty("draft"));
        Require(draft.Definition.Profile == profile, "Artifact and request profiles differ.");
        return new MechanicalAssemblyArtifactRequest(draft, NumericRequest(request.GetProperty("numericRequest")));
    });
    /// <summary>Only engine-produced immutable analysis/evaluation records are written; source-aware validation belongs to the facade.</summary>
    public static MechanicalAssemblyArtifactWriteResult WriteArtifact(MechanicalAssemblyDraft request, byte[]? finalizedRootBytes, string? finalizedRootIdentity,
        bool originalRootBytesPreserved, MechanicalAssemblyAnalysis analysis, MechanicalAssemblyEvaluation reference) => Guard(() =>
    {
        Require(analysis.Draft.DraftId == request.DraftId && IsReadyForReconstruction(analysis), "Complete admitted current assembly analysis required.");
        Require(reference.AnalysisId == analysis.AnalysisId && reference.RootInput == ExactQuantity.Turns(0) && reference.AllRequestedNumericAvailable,
            "Current root-zero aggregate reference evaluation required.");
        Require((finalizedRootBytes is null) == (finalizedRootIdentity is null) && (finalizedRootBytes is null || finalizedRootBytes.Length <= 4 * 1024 * 1024), "Bounded finalized root bytes and identity must be supplied together.");
        var provisional = new MechanicalAssemblyArtifact("", request, finalizedRootBytes, finalizedRootIdentity, originalRootBytesPreserved, analysis, reference);
        var artifact = new MechanicalAssemblyArtifact(Hash(ArtifactDocument(provisional, false)), request, finalizedRootBytes, finalizedRootIdentity, originalRootBytesPreserved, analysis, reference);
        return new MechanicalAssemblyArtifactWriteResult(artifact, ArtifactDocument(artifact, true));
    });
    public static byte[] WriteArtifact(MechanicalAssemblyArtifact artifact) => Guard(() => ArtifactDocument(artifact, true));
    public static ArtifactIdentityVerification VerifyIdentity(MechanicalAssemblyArtifact artifact)
    {
        var hash = Hash(ArtifactDocument(artifact, false)); return new(artifact.Request.DefinitionId, hash, artifact.CandidateId == artifact.Request.DefinitionId, hash == artifact.ArtifactHash);
    }
    private static byte[] ArtifactDocument(MechanicalAssemblyArtifact artifact, bool includeHash) => Encode(w =>
    {
        Start(w, MechanicalAssemblyArtifact.Format, VersionFor(artifact.Request.Definition.Profile)); w.WriteString("profile", artifact.Request.Definition.Profile); w.WriteString("analysisPolicy", artifact.Analysis.Policy);
        w.WriteString("candidateId", artifact.CandidateId); if (includeHash) w.WriteString("artifactHash", artifact.ArtifactHash);
        w.WriteStartObject("request"); w.WritePropertyName("draft"); Draft(w, artifact.Request); w.WritePropertyName("numericRequest"); NumericRequest(w, artifact.NumericRequest); w.WriteEndObject();
        w.WriteStartObject("finalizedRoot"); w.WriteString("definitionId", artifact.Request.Definition.Root.DefinitionId); w.WriteString("artifactIdentity", artifact.FinalizedRootIdentity);
        if (artifact.FinalizedRootBytes is null) w.WriteNull("artifactUtf8"); else w.WriteBase64String("artifactUtf8", artifact.FinalizedRootBytes);
        w.WriteBoolean("originalBytesPreserved", artifact.OriginalRootBytesPreserved); w.WriteString("bindingPolicy", "single-complete-root-structured-owner-local-identifiers-v1"); w.WriteEndObject();
        w.WriteStartArray("finalizationChecks"); w.WriteStartObject(); w.WriteString("domain", "Source/MemberReconstruction"); w.WriteString("subject", "assembly");
        w.WriteString("verdict", "Pass"); w.WriteBoolean("required", true); w.WriteString("detail", "Complete current root finalized and reimported once; every current member rebuilt in deterministic dependency order.");
        w.WriteEndObject(); w.WriteEndArray();
        w.WritePropertyName("analysis"); Analysis(w, artifact.Analysis); w.WritePropertyName("referenceEvaluation"); Evaluation(w, artifact.ReferenceEvaluation); w.WriteEndObject();
    });
}
