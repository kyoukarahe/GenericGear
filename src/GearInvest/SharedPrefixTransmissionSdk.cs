using System;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public SharedPrefixGoalNormalization NormalizeSharedPrefixTransmissionGoal(SharedPrefixTransmissionGoal goal) => SharedPrefixTransmissionCompiler.Normalize(goal);
    public SharedPrefixTransmissionSearchPlan CompileSharedPrefixTransmissionSearchPlan(SharedPrefixTransmissionGoal goal) => SharedPrefixTransmissionCompiler.Compile(goal);
    public SharedPrefixTransmissionResult GenerateSharedPrefixTransmissions(SharedPrefixTransmissionGoal goal, CancellationToken token = default, Action<int, int>? progress = null)
        => new SharedPrefixTransmissionSearch().Generate(goal, _serializer.ComputeCandidateId, SharedDriverTransmissionJson.PoolCandidateBytes, SharedPrefixTransmissionJson.MeasureResultBytes, token, progress);
    public SharedPrefixCompositionResult ComposeSharedPrefixTransmission(SharedPrefixTransmissionGoal goal, string hypothesisId, TransmissionMechanismCandidate prefix, TransmissionMechanismCandidate left, TransmissionMechanismCandidate right)
        => SharedPrefixTransmissionComposer.Compose(goal, hypothesisId, prefix, left, right, _serializer.ComputeCandidateId);
    public ValidationBundle ValidateSharedPrefixTransmissionCandidate(SharedPrefixTransmissionGoal goal, SharedPrefixTransmissionCandidate c) => SharedPrefixTransmissionComposer.Validate(goal, c, _serializer.ComputeCandidateId);
    public byte[] WriteSharedPrefixTransmissionGoal(SharedPrefixTransmissionGoal goal) => SharedPrefixTransmissionJson.WriteGoal(goal);
    public SharedPrefixTransmissionGoal ReadSharedPrefixTransmissionGoal(byte[] bytes) => SharedPrefixTransmissionJson.ReadGoal(bytes);
    public byte[] WriteSharedPrefixTransmissionSearchPlan(SharedPrefixTransmissionSearchPlan plan) => SharedPrefixTransmissionJson.WritePlan(plan);
    public byte[] WriteSharedPrefixTransmissionResult(SharedPrefixTransmissionResult result) => SharedPrefixTransmissionJson.WriteResult(result);
    public SharedPrefixTransmissionResult ReadSharedPrefixTransmissionResult(byte[] bytes) => SharedPrefixTransmissionJson.ReadResult(bytes);
    public ArtifactWriteResult WriteSharedPrefixTransmissionMechanism(SharedPrefixTransmissionCandidate c) => SharedPrefixTransmissionJson.WriteMechanism(c);
    public SharedPrefixTransmissionProjectManifest SaveSharedPrefixTransmissionProject(SharedPrefixTransmissionProject project, string directory, bool overwrite = false) => SharedPrefixTransmissionProjects.Save(this, project, directory, overwrite);
    public SharedPrefixTransmissionProject LoadSharedPrefixTransmissionProject(string directory) => SharedPrefixTransmissionProjects.Load(this, directory);
    public SharedPrefixTransmissionProject RegenerateSharedPrefixTransmissionProject(SharedPrefixTransmissionProject project, CancellationToken token = default, Action<int, int>? progress = null) => SharedPrefixTransmissionProjects.Regenerate(this, project, token, progress);
}
