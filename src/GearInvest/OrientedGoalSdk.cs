using System;
using System.Linq;
using System.Threading;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public OrientedGoalCompilation CompileOrientedTwoOutputSearchPlan(OrientedTwoOutputGoal goal, CancellationToken token = default) =>
        OrientedGoalCompiler.Compile(goal, ReadOrientedGoalSource, token);

    public OrientedGoalGeneration GenerateOrientedTwoOutput(OrientedTwoOutputGoal goal, CancellationToken token = default, Action<int, int>? progress = null)
    {
        var compiled = CompileOrientedTwoOutputSearchPlan(goal, token);
        if (!compiled.IsSuccess) return new OrientedGoalGeneration("", compiled.Status, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false,
            OrientedRankingGuarantee.None, compiled.Detail, OrientedGoalKeys.HashText("oriented-goal-transcript-v1"), Array.Empty<OrientedGoalCandidate>(), Array.Empty<OrientedGoalTupleDetail>(),
            goal.Profile == OrientedGoalProfile.RefinedId ? OrientedGoalProfile.RefinedId : OrientedGoalProfile.Id);
        return OrientedGoalSearch.Generate(compiled.Plan!, WriteOrientedGoalCandidate, token, progress);
    }

    public OrientedTwoOutputAssemblyRequest LowerOrientedGoalTuple(OrientedGoalPlan plan, OrientedGoalTuple tuple) =>
        ToWireOrientedGoalRequest(plan, tuple, OrientedGoalSearch.Lower(plan, tuple));

    public OrientedGoalTupleInspection InspectOrientedGoalTuple(OrientedGoalPlan plan, OrientedGoalTuple tuple) => OrientedGoalSearch.Inspect(plan, tuple);

    public OrientedValidation ValidateOrientedGoalCandidate(OrientedGoalPlan plan, OrientedGoalTuple tuple, OrientedTwoOutputArtifact artifact)
    {
        var identity = ValidateOrientedTwoOutputArtifact(artifact);
        if (!identity.IsValid) return new OrientedValidation(identity.Validation.Checks.Concat(new[] {
            new OrientedDomainCheck("goal-context", "artifact-integrity", OrientedCheckVerdict.Fail, true, "Artifact integrity or validation failed.") }), identity.Validation.Diagnostics,
            identity.Validation.PitchProofs, identity.Validation.ClearancePolicy);
        var expected = LowerOrientedGoalTuple(plan, tuple);
        if (!WriteOrientedTwoOutputRequest(expected).SequenceEqual(WriteOrientedTwoOutputRequest(artifact.Request)))
            return new OrientedValidation(new[] { new OrientedDomainCheck("goal-context", "declared-origin", OrientedCheckVerdict.Fail, true,
                "Artifact request is not the exact declared goal tuple/context.") }, Array.Empty<Core.Diagnostic>(), clearancePolicy: OrientedTwoOutputProfile.ClearancePolicy(expected.Profile));
        return OrientedGoalSearch.ValidateContext(plan, tuple, artifact.Mechanism);
    }

    private OrientedGoalSourceRead ReadOrientedGoalSource(OrientedGoalSource source)
    {
        try
        {
            var read = ReadArtifact(source.SourceBytes);
            if (read.CandidateId != source.CandidateId || read.ArtifactHash != source.ArtifactHash || !Validate(read).IsValid ||
                !WriteArtifact(read).Bytes.SequenceEqual(source.SourceBytes))
                return new OrientedGoalSourceRead(OrientedGoalStatus.InvalidInput, null, "original identity, validity or canonical bytes mismatch");
            return new OrientedGoalSourceRead(OrientedGoalStatus.Complete, read.Candidate, "original-source-verified");
        }
        catch (Exception e) when (e is ArtifactFormatException || e is ArgumentException || e is InvalidOperationException)
        { return new OrientedGoalSourceRead(OrientedGoalStatus.InvalidInput, null, "malformed original planar artifact"); }
    }

    private static OrientedTwoOutputAssemblyRequest ToWireOrientedGoalRequest(OrientedGoalPlan plan, OrientedGoalTuple tuple, OrientedTwoOutputCompositionRequest request)
    {
        PlanarArtifactPlacement Placement(OrientedGoalResolvedSource source, PlanarShaftModule module) =>
            new(source.Definition.SourceBytes, module.Pose, module.InputDofId, module.OutputDofId, module.ConnectionPort);
        return new OrientedTwoOutputAssemblyRequest(request.Bevel,
            Placement(plan.ParallelSources.Single(s => s.Id == tuple.ParallelSourceId), request.ParallelBranch), request.Outputs,
            tuple.TurnedSourceId is null ? null : Placement(plan.TurnedSources.Single(s => s.Id == tuple.TurnedSourceId), request.TurnedBranch!),
            request.AssemblyPose, request.RequireCrossComponentClearance, request.KeepOuts, request.Profile);
    }

    private OrientedGoalArtifact WriteOrientedGoalCandidate(OrientedGoalPlan plan, OrientedGoalTuple tuple, OrientedTwoOutputCompositionRequest request, OrientedTwoOutputMechanism model)
    {
        var written = WriteOrientedTwoOutputArtifact(model, ToWireOrientedGoalRequest(plan, tuple, request));
        if (!written.Artifact.StoredValidation.IsValid) throw new InvalidOperationException("Authoritative artifact context validation failed.");
        return new OrientedGoalArtifact(written.Artifact.CandidateId, written.Artifact.ArtifactHash, written.Bytes, CanonicalOrientedTwoOutputJson.WriteMechanicalPayload(model));
    }
}
