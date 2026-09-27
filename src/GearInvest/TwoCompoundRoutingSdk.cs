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
    public TwoCompoundRoutingNormalization NormalizeAnchoredTwoCompoundRoutingRequest(AnchoredTwoCompoundGearRoutingRequest request) => AnchoredTwoCompoundGearRouter.Normalize(request);
    public TwoCompoundAssignmentSynthesisResult SynthesizeEndpointConditionedTwoCompoundAssignments(AnchoredTwoCompoundGearRoutingRequest request,CancellationToken token = default) => new AnchoredTwoCompoundGearRouter().SynthesizeAssignments(request,token);
    public TwoCompoundGearRoutingResult GenerateAnchoredTwoCompoundGearRoutes(AnchoredTwoCompoundGearRoutingRequest request,CancellationToken token = default,Action<int,int>? progress = null) => new AnchoredTwoCompoundGearRouter().Generate(request,_serializer.ComputeCandidateId,token,progress);
    public TwoCompoundRouteValidation ValidateAnchoredTwoCompoundGearRoute(AnchoredTwoCompoundGearRoutingRequest request,TwoCompoundGearRouteCandidate candidate)
    {
        var id = _serializer.ComputeCandidateId(candidate.Mechanism); var v = TwoCompoundGearRouteValidator.Validate(request,candidate.Assignment,candidate.Paths,candidate.Mechanism,id); var diagnostics = new List<Diagnostic>(v.Diagnostics);
        if (candidate.CandidateId != id || candidate.Validation.RequestId != v.RequestId || candidate.Validation.CandidateId != id || candidate.Validation.ContextId != v.ContextId)
            diagnostics.Add(new Diagnostic("TWO_COMPOUND_ROUTING_ATTACHED_CONTEXT_MISMATCH",DiagnosticSeverity.Error,"Attached mechanical identity or request context differs."));
        return new TwoCompoundRouteValidation(v.RequestId,id,v.GenericValidation,v.Spatial,v.LegTransfers,v.TotalTransfer,diagnostics);
    }
    public byte[] WriteTwoCompoundRoutingRequest(AnchoredTwoCompoundGearRoutingRequest request) => TwoCompoundRoutingJson.WriteRequest(request);
    public AnchoredTwoCompoundGearRoutingRequest ReadTwoCompoundRoutingRequest(byte[] bytes) => TwoCompoundRoutingJson.ReadRequest(bytes);
    public byte[] WriteTwoCompoundRoutingResult(TwoCompoundGearRoutingResult result) => TwoCompoundRoutingJson.WriteResult(result);
    public TwoCompoundGearRoutingResult ReadTwoCompoundRoutingResult(byte[] bytes) => TwoCompoundRoutingJson.ReadResult(bytes);
    public ArtifactWriteResult WriteTwoCompoundGearRouteArtifact(TwoCompoundGearRouteCandidate candidate) => TwoCompoundRoutingJson.WriteMechanism(candidate);
    public TwoCompoundRoutingProjectManifest SaveTwoCompoundRoutingProject(TwoCompoundRoutingProject project,string directory,bool overwrite = false) => TwoCompoundRoutingProjects.Save(this,project,directory,overwrite);
    public TwoCompoundRoutingProject LoadTwoCompoundRoutingProject(string directory) => TwoCompoundRoutingProjects.Load(this,directory);
    public TwoCompoundRoutingProject RegenerateTwoCompoundRoutingProject(TwoCompoundRoutingProject project,CancellationToken token = default,Action<int,int>? progress = null) => TwoCompoundRoutingProjects.Regenerate(this,project,token,progress);
}
