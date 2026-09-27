using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Complete explicit authored request, not an invented legacy low-level generator provenance.</summary>
public sealed class ParallelCoaxialArtifact
{
    internal ParallelCoaxialArtifact(MechanicalDefinition request, MechanicalAnalysis analysis, string artifactHash)
    { Request = request; Analysis = analysis; ArtifactHash = artifactHash; }
    public MechanicalDefinition Request { get; }
    public MechanicalAnalysis Analysis { get; }
    public string DefinitionId => Request.DefinitionId;
    public string ArtifactHash { get; }
}

public static class ParallelCoaxialArtifactJson
{
    public const string Format = "gear-invest.parallel-coaxial-mechanism";
    public const string Version = "0.1";
    public const int MaxDocumentBytes = 4 * 1024 * 1024;

    public static byte[] Write(MechanicalDefinition request) => MechanicalAuthoringJson.Guard(() =>
    {
        Require(request.CoaxialLayout is not null, "Explicit parallel coaxial source profile required.");
        // The current authored request is authoritative; history/revision/import do not become source identity.
        var analysis = MechanicalAnalyzer.Analyze(new MechanicalDraft(request));
        Require(analysis.IsMechanicallyValid, "Current coaxial source cannot finalize: " + string.Join(",", analysis.Diagnostics.Select(x => x.Code)));
        var payload = Document(request, analysis, null);
        var bytes = Document(request, analysis, Hash(payload));
        Require(bytes.Length <= MaxDocumentBytes, "Final source byte bound exceeded.");
        return bytes;
    });

    public static ParallelCoaxialArtifact Read(byte[] bytes) => MechanicalAuthoringJson.Guard(() =>
    {
        Require(bytes is not null && bytes.Length <= MaxDocumentBytes, "Bounded parallel coaxial artifact required.");
        using var doc = MechanicalAuthoringJson.Parse(bytes!); var p = doc.RootElement;
        Header(p, Format); Require(S(p, "profile") == ParallelCoaxialLayout.Profile, "Unsupported coaxial source profile.");
        var request = MechanicalAuthoringJson.Definition(p.GetProperty("request"));
        var fresh = Write(request);
        Require(bytes!.SequenceEqual(fresh), "Fresh coaxial request, derived law, identity or canonical artifact differs.");
        return new ParallelCoaxialArtifact(request, MechanicalAnalyzer.Analyze(new MechanicalDraft(request)), S(p, "artifactHash"));
    });

    private static byte[] Document(MechanicalDefinition request, MechanicalAnalysis analysis, string? hash) => MechanicalAuthoringJson.Encode(w =>
    {
        w.WriteStartObject(); w.WriteString("format", Format); w.WriteString("formatVersion", Version); w.WriteString("profile", ParallelCoaxialLayout.Profile);
        w.WriteString("definitionId", request.DefinitionId);
        if (hash is not null) w.WriteString("artifactHash", hash);
        w.WritePropertyName("request"); MechanicalAuthoringJson.Definition(w, request);
        w.WritePropertyName("analysis"); MechanicalAuthoringJson.Analysis(w, analysis); w.WriteEndObject();
    });
}
