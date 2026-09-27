using System;
using System.Threading;
using System.Collections.Generic;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public GearRoutingNormalization NormalizeGearRoutingRequest(AnchoredGearRoutingRequest request) => AnchoredGearRouter.Normalize(request);
    public GearRoutingResult GenerateGearRoutes(AnchoredGearRoutingRequest request, CancellationToken cancellationToken = default, Action<int, int>? progress = null) => new AnchoredGearRouter().Generate(request, _serializer.ComputeCandidateId, cancellationToken, progress);
    public GearRouteValidation ValidateGearRoute(AnchoredGearRoutingRequest request, GearRouteCandidate candidate)
    {
        var id = _serializer.ComputeCandidateId(candidate.Mechanism); var v = GearRouteValidator.Validate(request, candidate.Path, candidate.Mechanism, id); var d = new List<Diagnostic>(v.Diagnostics);
        if (id != candidate.CandidateId || candidate.Validation.CandidateId != id || candidate.Validation.RequestId != v.RequestId || candidate.Validation.ContextId != v.ContextId)
            d.Add(new Diagnostic("ROUTING_ATTACHED_CONTEXT_MISMATCH", DiagnosticSeverity.Error, "The candidate identity or attached request context differs from the actual mechanism/current request."));
        return new GearRouteValidation(v.RequestId, id, v.GenericValidation, v.Spatial, v.ActualTransfer, d);
    }
    public byte[] WriteGearRoutingRequest(AnchoredGearRoutingRequest request) => GearRoutingJson.WriteRequest(request);
    public AnchoredGearRoutingRequest ReadGearRoutingRequest(byte[] bytes) => GearRoutingJson.ReadRequest(bytes);
    public byte[] WriteGearRoutingResult(GearRoutingResult result) => GearRoutingJson.WriteResult(result);
    public GearRoutingResult ReadGearRoutingResult(byte[] bytes) => GearRoutingJson.ReadResult(bytes);
    public ArtifactWriteResult WriteGearRouteArtifact(GearRouteCandidate candidate) => GearRoutingJson.WriteMechanism(candidate);
    public GearRoutingProjectManifest SaveGearRoutingProject(GearRoutingProject project, string directory, bool overwrite = false) => GearRoutingProjects.Save(this, project, directory, overwrite);
    public GearRoutingProject LoadGearRoutingProject(string directory) => GearRoutingProjects.Load(this, directory);
    public GearRoutingProject RegenerateGearRoutingProject(GearRoutingProject project, CancellationToken token = default, Action<int, int>? progress = null) => GearRoutingProjects.Regenerate(this, project, token, progress);
}
