using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.Serialization.Json;

/// <summary>Original planar artifact bytes and explicit mechanical placement. Bytes are defensively copied, never reinterpreted as oriented format.</summary>
public sealed class PlanarArtifactPlacement
{
    private readonly byte[] _sourceBytes;
    public PlanarArtifactPlacement(byte[] sourceBytes, OrientedFrame pose, string inputDofId, string outputDofId, ShaftPort connectionPort)
    {
        if (sourceBytes is null || sourceBytes.Length > 512 * 1024) throw new ArgumentException("A bounded source artifact is required.");
        _sourceBytes = (byte[])sourceBytes.Clone(); Pose = pose; InputDofId = inputDofId; OutputDofId = outputDofId; ConnectionPort = connectionPort;
    }
    public byte[] SourceBytes => (byte[])_sourceBytes.Clone();
    public OrientedFrame Pose { get; }
    public string InputDofId { get; }
    public string OutputDofId { get; }
    public ShaftPort ConnectionPort { get; }
    public PlanarShaftModule ReadEngineSource()
    {
        var json = new CanonicalMechanismJson(); var source = json.Read(_sourceBytes); var identity = json.VerifyIdentity(source);
        if (!identity.CandidateIdMatches || !identity.ArtifactHashMatches || !new GenerationEngine().Validate(source.Candidate).IsValid)
            throw new ArtifactFormatException("Original planar source identity or mechanical validity failed.");
        return new PlanarShaftModule(source.Candidate, source.CandidateId, source.ArtifactHash, Pose, InputDofId, OutputDofId, ConnectionPort);
    }
}

/// <summary>Small standalone versioned assembly request; embedded original sources make it portable without repository fixtures.</summary>
public sealed class OrientedAssemblyRequest
{
    public OrientedAssemblyRequest(RightAngleBevelRequest bevel, PlanarArtifactPlacement? upstream = null, PlanarArtifactPlacement? downstream = null,
        OrientedFrame? assemblyPose = null, Rational? requestedTransfer = null, bool requireCrossComponentClearance = true, IEnumerable<OrientedKeepOut>? keepOuts = null)
    {
        Bevel = bevel; Upstream = upstream; Downstream = downstream; AssemblyPose = assemblyPose ?? OrientedFrame.Identity;
        RequestedTransfer = requestedTransfer; RequireCrossComponentClearance = requireCrossComponentClearance;
        KeepOuts = (keepOuts ?? Array.Empty<OrientedKeepOut>()).OrderBy(k => k.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public RightAngleBevelRequest Bevel { get; }
    public PlanarArtifactPlacement? Upstream { get; }
    public PlanarArtifactPlacement? Downstream { get; }
    public OrientedFrame AssemblyPose { get; }
    public Rational? RequestedTransfer { get; }
    public bool RequireCrossComponentClearance { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
    public OrientedCompositionRequest ToEngineRequest() => new(Bevel, Upstream?.ReadEngineSource(), Downstream?.ReadEngineSource(), AssemblyPose, RequestedTransfer, RequireCrossComponentClearance, KeepOuts);
}

public sealed class OrientedArtifact
{
    public const string Format = "gear-invest.oriented-mechanism";
    public const string Version = "0.1";
    public OrientedArtifact(string candidateId, string artifactHash, OrientedAssemblyRequest request, OrientedMechanism mechanism, OrientedValidation storedValidation)
    { CandidateId = candidateId; ArtifactHash = artifactHash; Request = request; Mechanism = mechanism; StoredValidation = storedValidation; }
    public string CandidateId { get; }
    public string ArtifactHash { get; }
    public OrientedAssemblyRequest Request { get; }
    public OrientedMechanism Mechanism { get; }
    public OrientedValidation StoredValidation { get; }
}

public sealed class OrientedArtifactWriteResult
{
    private readonly byte[] _bytes;
    public OrientedArtifactWriteResult(OrientedArtifact artifact, byte[] bytes) { Artifact = artifact; _bytes = (byte[])bytes.Clone(); }
    public OrientedArtifact Artifact { get; }
    public byte[] Bytes => (byte[])_bytes.Clone();
}
