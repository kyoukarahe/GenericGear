using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

namespace GearInvest.Serialization.Json;

public sealed class OrientedTwoOutputAssemblyRequest
{
    public OrientedTwoOutputAssemblyRequest(RightAngleBevelRequest bevel, PlanarArtifactPlacement parallelBranch,
        IEnumerable<OrientedOutputRequest> outputs, PlanarArtifactPlacement? turnedBranch = null, OrientedFrame? assemblyPose = null,
        bool requireCrossComponentClearance = true, IEnumerable<OrientedKeepOut>? keepOuts = null, string profile = OrientedTwoOutputProfile.Id)
    {
        Bevel = bevel; ParallelBranch = parallelBranch; Outputs = outputs.OrderBy(o => o.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        TurnedBranch = turnedBranch; AssemblyPose = assemblyPose ?? OrientedFrame.Identity; RequireCrossComponentClearance = requireCrossComponentClearance;
        KeepOuts = (keepOuts ?? Array.Empty<OrientedKeepOut>()).OrderBy(k => k.Id, StringComparer.Ordinal).ToList().AsReadOnly(); Profile = profile;
    }
    public string Profile { get; }
    public RightAngleBevelRequest Bevel { get; }
    public PlanarArtifactPlacement ParallelBranch { get; }
    public PlanarArtifactPlacement? TurnedBranch { get; }
    public ReadOnlyCollection<OrientedOutputRequest> Outputs { get; }
    public OrientedFrame AssemblyPose { get; }
    public bool RequireCrossComponentClearance { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
    public OrientedTwoOutputCompositionRequest ToEngineRequest() => new(Bevel, ParallelBranch.ReadEngineSource(), Outputs, TurnedBranch?.ReadEngineSource(), AssemblyPose, RequireCrossComponentClearance, KeepOuts, Profile);
}

public sealed class OrientedTwoOutputArtifact
{
    public const string Format = "gear-invest.oriented-two-output-mechanism";
    public const string Version = "0.1";
    public OrientedTwoOutputArtifact(string candidateId, string artifactHash, OrientedTwoOutputAssemblyRequest request, OrientedTwoOutputMechanism mechanism, OrientedValidation storedValidation)
    { CandidateId = candidateId; ArtifactHash = artifactHash; Request = request; Mechanism = mechanism; StoredValidation = storedValidation; }
    public string CandidateId { get; }
    public string ArtifactHash { get; }
    public OrientedTwoOutputAssemblyRequest Request { get; }
    public OrientedTwoOutputMechanism Mechanism { get; }
    public OrientedValidation StoredValidation { get; }
}

public sealed class OrientedTwoOutputArtifactWriteResult
{
    private readonly byte[] _bytes;
    public OrientedTwoOutputArtifactWriteResult(OrientedTwoOutputArtifact artifact, byte[] bytes) { Artifact = artifact; _bytes = (byte[])bytes.Clone(); }
    public OrientedTwoOutputArtifact Artifact { get; }
    public byte[] Bytes => (byte[])_bytes.Clone();
}
