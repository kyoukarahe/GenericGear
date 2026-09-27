using System;
using System.Collections.Generic;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public CompoundRoutingNormalization NormalizeAnchoredCompoundRoutingRequest(AnchoredCompoundGearRoutingRequest request) => AnchoredCompoundGearRouter.Normalize(request);
    public CompoundPairSynthesisResult SynthesizeAnchoredCompoundPairs(AnchoredCompoundGearRoutingRequest request, CancellationToken token = default) => new AnchoredCompoundGearRouter().SynthesizePairs(request, token);
    public CompoundGearRoutingResult GenerateAnchoredCompoundGearRoutes(AnchoredCompoundGearRoutingRequest request, CancellationToken token = default, Action<int, int>? progress = null) => new AnchoredCompoundGearRouter().Generate(request, _serializer.ComputeCandidateId, token, progress);
    public CompoundRouteValidation ValidateAnchoredCompoundGearRoute(AnchoredCompoundGearRoutingRequest request, CompoundGearRouteCandidate candidate)
    {
        var id = _serializer.ComputeCandidateId(candidate.Mechanism); var v = CompoundGearRouteValidator.Validate(request, candidate.Pair, candidate.InputPath, candidate.OutputPath, candidate.Mechanism, id); var diagnostics = new List<Diagnostic>(v.Diagnostics);
        if (candidate.CandidateId != id || candidate.Validation.RequestId != v.RequestId || candidate.Validation.CandidateId != id || candidate.Validation.ContextId != v.ContextId)
            diagnostics.Add(new Diagnostic("COMPOUND_ROUTING_ATTACHED_CONTEXT_MISMATCH", DiagnosticSeverity.Error, "Attached mechanical identity or request context differs."));
        return new CompoundRouteValidation(v.RequestId, id, v.GenericValidation, v.Spatial, v.InputTransfer, v.OutputTransfer, v.TotalTransfer, diagnostics);
    }
    public byte[] WriteCompoundRoutingRequest(AnchoredCompoundGearRoutingRequest request) => CompoundRoutingJson.WriteRequest(request);
    public AnchoredCompoundGearRoutingRequest ReadCompoundRoutingRequest(byte[] bytes) => CompoundRoutingJson.ReadRequest(bytes);
    public byte[] WriteCompoundRoutingResult(CompoundGearRoutingResult result) => CompoundRoutingJson.WriteResult(result);
    public CompoundGearRoutingResult ReadCompoundRoutingResult(byte[] bytes) => CompoundRoutingJson.ReadResult(bytes);
    public ArtifactWriteResult WriteCompoundGearRouteArtifact(CompoundGearRouteCandidate candidate) => CompoundRoutingJson.WriteMechanism(candidate);
    public CompoundRoutingProjectManifest SaveCompoundRoutingProject(CompoundRoutingProject project, string directory, bool overwrite = false) => CompoundRoutingProjects.Save(this, project, directory, overwrite);
    public CompoundRoutingProject LoadCompoundRoutingProject(string directory) => CompoundRoutingProjects.Load(this, directory);
    public CompoundRoutingProject RegenerateCompoundRoutingProject(CompoundRoutingProject project, CancellationToken token = default, Action<int, int>? progress = null) => CompoundRoutingProjects.Regenerate(this, project, token, progress);
}
