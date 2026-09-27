using System;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public SharedDriverGoalNormalization NormalizeSharedDriverTransmissionGoal(SharedDriverTransmissionGoal goal) => SharedDriverTransmissionCompiler.Normalize(goal);
    public SharedDriverTransmissionSearchPlan CompileSharedDriverTransmissionSearchPlan(SharedDriverTransmissionGoal goal) => SharedDriverTransmissionCompiler.Compile(goal);
    public SharedDriverTransmissionResult GenerateSharedDriverTransmissions(SharedDriverTransmissionGoal goal, CancellationToken token = default, Action<int, int>? progress = null)
        => new SharedDriverTransmissionSearch().Generate(goal, _serializer.ComputeCandidateId, SharedDriverTransmissionJson.PoolCandidateBytes, SharedDriverTransmissionJson.MeasureResultBytes, token, progress);
    public SharedDriverPairResult ComposeSharedDriverTransmissionPair(SharedDriverTransmissionGoal goal, TransmissionMechanismCandidate firstOutput, TransmissionMechanismCandidate secondOutput)
        => SharedDriverTransmissionComposer.Compose(goal, firstOutput, secondOutput, _serializer.ComputeCandidateId);
    public ValidationBundle ValidateSharedDriverTransmissionCandidate(SharedDriverTransmissionGoal goal, SharedDriverTransmissionCandidate candidate)
        => SharedDriverTransmissionComposer.Validate(goal, candidate, _serializer.ComputeCandidateId);
    public byte[] WriteSharedDriverTransmissionGoal(SharedDriverTransmissionGoal goal) => SharedDriverTransmissionJson.WriteGoal(goal);
    public SharedDriverTransmissionGoal ReadSharedDriverTransmissionGoal(byte[] bytes) => SharedDriverTransmissionJson.ReadGoal(bytes);
    public byte[] WriteSharedDriverTransmissionSearchPlan(SharedDriverTransmissionSearchPlan plan) => SharedDriverTransmissionJson.WritePlan(plan);
    public byte[] WriteSharedDriverTransmissionResult(SharedDriverTransmissionResult result) => SharedDriverTransmissionJson.WriteResult(result);
    public SharedDriverTransmissionResult ReadSharedDriverTransmissionResult(byte[] bytes) => SharedDriverTransmissionJson.ReadResult(bytes);
    public ArtifactWriteResult WriteSharedDriverTransmissionMechanism(SharedDriverTransmissionCandidate candidate) => SharedDriverTransmissionJson.WriteMechanism(candidate);
    public SharedDriverTransmissionProjectManifest SaveSharedDriverTransmissionProject(SharedDriverTransmissionProject project, string directory, bool overwrite = false) => SharedDriverTransmissionProjects.Save(this, project, directory, overwrite);
    public SharedDriverTransmissionProject LoadSharedDriverTransmissionProject(string directory) => SharedDriverTransmissionProjects.Load(this, directory);
    public SharedDriverTransmissionProject RegenerateSharedDriverTransmissionProject(SharedDriverTransmissionProject project, CancellationToken token = default, Action<int, int>? progress = null) => SharedDriverTransmissionProjects.Regenerate(this, project, token, progress);
}
