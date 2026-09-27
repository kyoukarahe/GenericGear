using System;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public TransmissionGoalNormalization NormalizeTransmissionGoal(TransmissionGoal goal) => TransmissionGoalCompiler.Normalize(goal);
    public CompiledTransmissionSearchPlan CompileTransmissionSearchPlan(TransmissionGoal goal) => TransmissionGoalCompiler.Compile(goal);
    public TransmissionGenerationResult GenerateTransmissionMechanisms(TransmissionGoal goal, CancellationToken token = default, Action<int, int>? progress = null)
        => new TransmissionSearchOrchestrator().Generate(goal, _serializer.ComputeCandidateId, token, progress);
    public TransmissionGoalValidation ValidateGoalTransmissionCandidate(TransmissionGoal goal, TransmissionMechanismCandidate candidate)
        => TransmissionGoalValidator.Validate(goal, candidate, _serializer.ComputeCandidateId);
    public byte[] WriteTransmissionGoal(TransmissionGoal goal) => TransmissionGoalJson.WriteGoal(goal);
    public TransmissionGoal ReadTransmissionGoal(byte[] bytes) => TransmissionGoalJson.ReadGoal(bytes);
    public byte[] WriteTransmissionSearchPlan(CompiledTransmissionSearchPlan plan) => TransmissionGoalJson.WritePlan(plan);
    public byte[] WriteTransmissionGenerationResult(TransmissionGenerationResult result) => TransmissionGoalJson.WriteResult(result);
    public TransmissionGenerationResult ReadTransmissionGenerationResult(byte[] bytes) => TransmissionGoalJson.ReadResult(bytes);
    public ArtifactWriteResult WriteTransmissionMechanismArtifact(TransmissionCandidateOrigin origin) => TransmissionGoalJson.WriteMechanism(origin);
    public TransmissionGoalProjectManifest SaveTransmissionGoalProject(TransmissionGoalProject project, string directory, bool overwrite = false) => TransmissionGoalProjects.Save(this, project, directory, overwrite);
    public TransmissionGoalProject LoadTransmissionGoalProject(string directory) => TransmissionGoalProjects.Load(this, directory);
    public TransmissionGoalProject RegenerateTransmissionGoalProject(TransmissionGoalProject project, CancellationToken token = default, Action<int, int>? progress = null) => TransmissionGoalProjects.Regenerate(this, project, token, progress);
}
