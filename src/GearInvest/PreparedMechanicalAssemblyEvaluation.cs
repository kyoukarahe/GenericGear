using System;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest;

/// <summary>
/// An SDK-owned, provenance-verified assembly snapshot for repeated absolute-root evaluation.
/// Process-local only: not an artifact, finalization certificate, persistent cache or trust token.
/// </summary>
public sealed class PreparedMechanicalAssemblyEvaluation
{
    internal PreparedMechanicalAssemblyEvaluation(MechanicalAssemblyAnalysis analysis) => Analysis = analysis;

    /// <summary>
    /// The immutable owned analysis, including its exact draft/revision/profile and fixed numeric
    /// request. Partial mechanical failures remain diagnostics, not proof of finalizability.
    /// Returned evaluations retain this analysis; release those references as well as this handle.
    /// </summary>
    public MechanicalAssemblyAnalysis Analysis { get; }

    /// <summary>
    /// Evaluate the same verified snapshot at an unwrapped absolute root. All root-specific
    /// validation, arithmetic budgets, refinement, material and display work remain enabled.
    /// To change the definition or numeric policy, explicitly prepare a new snapshot.
    /// </summary>
    public MechanicalAssemblyEvaluation Evaluate(ExactQuantity root) => MechanicalAssemblyAnalyzer.Evaluate(Analysis, root);
}

public sealed partial class GearInvestSdk
{
    /// <summary>
    /// Own a bounded canonical copy, verify that copy's actual provenance, then analyze it once.
    /// Supports the existing serialized assembly profiles, including diagnostic partial mechanics,
    /// with a valid fixed numeric policy. This never finalizes or silently repairs a definition.
    /// Existing Read/Analyze/Evaluate/Validate/Finalize/Rebuild retain fresh verification.
    /// </summary>
    public PreparedMechanicalAssemblyEvaluation PrepareMechanicalAssemblyEvaluation(
        MechanicalAssemblyDraft draft, AssemblyNumericRequest? numericRequest = null)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var request = numericRequest ?? AssemblyNumericRequest.Default;
        if (!request.IsValid) throw new ArgumentException("A prepared assembly requires a valid fixed numeric request; use ordinary analysis for invalid-policy diagnostics.", nameof(numericRequest));

        // The codec reconstructs the closed declaration graph (including cloned original bytes).
        // No caller analysis, identity stamp or cached proof is used to mint the prepared state.
        var snapshot = MechanicalAssemblyJson.ReadDraft(MechanicalAssemblyJson.WriteDraft(draft));
        var ownedRequest = new AssemblyNumericRequest(request.MaximumWork, request.AngularWidth,
            request.LinearWidth, request.DirectionWidth, request.MaximumPrecisionBits,
            request.MaximumRefinements, request.IncludeDisplay, request.Policy, request.CamContourSamples);
        VerifyMechanicalAssemblyProvenance(snapshot);
        return new PreparedMechanicalAssemblyEvaluation(MechanicalAssemblyAnalyzer.Analyze(snapshot, ownedRequest));
    }
}
