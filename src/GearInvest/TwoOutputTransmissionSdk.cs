using System;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    /// <summary>Canonicalizes the active finite goal. Check IsValid and Diagnostics before use; inactive prefix text is not part of a typed goal.</summary>
    public TwoOutputGoalNormalization NormalizeTwoOutputTransmissionGoal(TwoOutputTransmissionGoal goal) => TwoOutputTransmissionCompiler.Normalize(goal);
    /// <summary>Analyzes policy applicability and lowers one total budget into immutable typed child plans. This does not execute synthesis.</summary>
    public TwoOutputSharingSearchPlan CompileTwoOutputSharingSearchPlan(TwoOutputTransmissionGoal goal) => TwoOutputTransmissionCompiler.Compile(goal);
    /// <summary>Executes the declared bounded sharing union. Valid candidates may coexist with incomplete search; check Status, SearchComplete and RankingGuarantee independently.</summary>
    /// <remarks>Synchronous, engine-independent operation. A UI may call it on a worker and marshal completion to its own thread. Progress is invoked on the calling thread. Cancellation is cooperative; never adopt a late result after editing its goal.</remarks>
    public TwoOutputTransmissionResult GenerateTwoOutputTransmissions(TwoOutputTransmissionGoal goal, CancellationToken token = default, Action<int, int>? progress = null)
        => new TwoOutputTransmissionSearch().Generate(goal, _serializer.ComputeCandidateId, SharedDriverTransmissionJson.PoolCandidateBytes,
            SharedDriverTransmissionJson.MeasureResultBytes, SharedPrefixTransmissionJson.MeasureResultBytes, TwoOutputTransmissionJson.MeasureObservationBytes, token, progress);
    /// <summary>Checks an original whole candidate and its typed origin against the common goal, exact targets, geometry and common limits. Not a physical engineering certificate.</summary>
    public ValidationBundle ValidateTwoOutputTransmissionCandidate(TwoOutputTransmissionGoal goal, TwoOutputTransmissionCandidate candidate)
        => TwoOutputTransmissionValidator.Validate(goal, candidate, _serializer.ComputeCandidateId);
    /// <summary>Returns the selected child's original canonical mechanism bytes and artifact; does not remap mechanical identity.</summary>
    public ArtifactWriteResult WriteTwoOutputTransmissionMechanism(TwoOutputTransmissionCandidate candidate) => TwoOutputTransmissionJson.WriteMechanism(candidate);
    /// <summary>Writes canonical active goal JSON with exact integer/fraction representation. Invalid goals are rejected.</summary>
    public byte[] WriteTwoOutputTransmissionGoal(TwoOutputTransmissionGoal goal) => TwoOutputTransmissionJson.WriteGoal(goal);
    /// <summary>Reads bounded versioned canonical goal JSON. Malformed, noncanonical or unsupported documents raise FormatException or ArgumentException.</summary>
    public TwoOutputTransmissionGoal ReadTwoOutputTransmissionGoal(byte[] bytes) => TwoOutputTransmissionJson.ReadGoal(bytes);
    /// <summary>Writes the immutable normalized goal, applicability, lowerings and allocation plan, not proof that search has run.</summary>
    public byte[] WriteTwoOutputSharingSearchPlan(TwoOutputSharingSearchPlan plan) => TwoOutputTransmissionJson.WritePlan(plan);
    /// <summary>Reads and verifies a canonical typed sharing plan; rejects incompatible versions and inconsistent derived context.</summary>
    public TwoOutputSharingSearchPlan ReadTwoOutputSharingSearchPlan(byte[] bytes) => TwoOutputTransmissionJson.ReadPlan(bytes);
    /// <summary>Writes the bounded full result, source origins, common observations/ranking and closure claims without replacing their original mechanism bytes.</summary>
    public byte[] WriteTwoOutputTransmissionResult(TwoOutputTransmissionResult result) => TwoOutputTransmissionJson.WriteResult(result);
    /// <summary>Validates cached canonical result structure and context. It does not independently establish that historical searches actually executed.</summary>
    public TwoOutputTransmissionResult ReadTwoOutputTransmissionResult(byte[] bytes) => TwoOutputTransmissionJson.ReadResult(bytes);
    /// <summary>Atomically saves a portable project. Save As requires an empty destination; overwrite explicitly validates an existing project before replacing its manifest.</summary>
    /// <remarks>Selection is an optional stable ID with typed origin/context, never an array rank. Filesystem errors are operational failures, not infeasibility.</remarks>
    public TwoOutputTransmissionProjectManifest SaveTwoOutputTransmissionProject(TwoOutputTransmissionProject project, string directory, bool overwrite = false)
        => TwoOutputTransmissionProjects.Save(this, project, directory, overwrite);
    /// <summary>Loads bounded canonical project members and validates identity, selected artifact and goal context. Cached Load is not Fresh. Invalid/mixed/path-linked documents are rejected; filesystem errors propagate.</summary>
    public TwoOutputTransmissionProject LoadTwoOutputTransmissionProject(string directory) => TwoOutputTransmissionProjects.Load(this, directory);
    /// <summary>Repeats actual lowering and included searches from the stored common goal, then compares full result/selection/artifact bytes and exact channels. Throws on drift; never silently selects a replacement.</summary>
    public TwoOutputTransmissionProject RegenerateTwoOutputTransmissionProject(TwoOutputTransmissionProject project, CancellationToken token = default, Action<int, int>? progress = null)
        => TwoOutputTransmissionProjects.Regenerate(this, project, token, progress);
}
