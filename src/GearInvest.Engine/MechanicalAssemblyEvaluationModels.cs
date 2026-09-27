using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class AssemblyShaftEvaluation
{
    internal AssemblyShaftEvaluation(AssemblyComponentReference reference, OrientedFrame frame, Rational turns, ExactAffineRelation relation)
    { Reference = reference; FixedFrameMm = frame; Turns = ExactQuantity.Turns(turns); Relation = relation; }
    public AssemblyComponentReference Reference { get; }
    public OrientedFrame FixedFrameMm { get; }
    public ExactQuantity Turns { get; }
    public ExactAffineRelation Relation { get; }
    public ExactVector3 WorldAngularVelocityPerRoot => FixedFrameMm.Z * Relation.Coefficient;
}

public sealed class AssemblyMemberEvaluation
{
    internal AssemblyMemberEvaluation(string instanceId, string status, ExactQuantity? inputTurns, object? affineOutput,
        object? material, object? recipe, object? numeric, object? inputObservation, object? display,
        bool numericAvailable, bool displayAvailable, int numericWork, IEnumerable<AssemblyDiagnostic> diagnostics,
        AssemblyGenevaSuffixEvaluation? genevaSuffix = null)
    {
        InstanceId = instanceId; Status = status; InputTurns = inputTurns; AffineOutput = affineOutput; Material = material;
        Recipe = recipe; Numeric = numeric; InputObservation = inputObservation; Display = display; NumericAvailable = numericAvailable;
        DisplayAvailable = displayAvailable; NumericWork = numericWork; Diagnostics = diagnostics.ToList().AsReadOnly();
        GenevaSuffix = genevaSuffix;
    }
    public string InstanceId { get; }
    public string Status { get; }
    public ExactQuantity? InputTurns { get; }
    public object? AffineOutput { get; }
    public AssemblyGenevaSuffixEvaluation? GenevaSuffix { get; }
    public WormDriveOutputEvaluation? WormOutput => AffineOutput as WormDriveOutputEvaluation;
    public OpenBeltOutputEvaluation? BeltOutput => AffineOutput as OpenBeltOutputEvaluation;
    public PitchChainOutputEvaluation? ChainOutput => AffineOutput as PitchChainOutputEvaluation;
    public object? Material { get; }
    public IndexedChainPoseDescriptor? ChainMaterial => Material as IndexedChainPoseDescriptor;
    public object? Recipe { get; }
    public object? Numeric { get; }
    public object? InputObservation { get; }
    public object? Display { get; }
    public bool NumericAvailable { get; }
    public bool DisplayAvailable { get; }
    public int NumericWork { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
}

public sealed class MechanicalAssemblyEvaluation
{
    internal MechanicalAssemblyEvaluation(MechanicalAssemblyAnalysis analysis, ExactQuantity root, IEnumerable<AssemblyShaftEvaluation> shafts,
        IEnumerable<AssemblyMemberEvaluation> members, IEnumerable<AssemblyDiagnostic> diagnostics, int numericWork, int rootEvaluationCount)
    {
        Analysis = analysis; RootInput = root; Shafts = shafts.OrderBy(s => s.Reference.CanonicalRepresentation, StringComparer.Ordinal).ToList().AsReadOnly();
        Members = members.OrderBy(m => m.InstanceId, StringComparer.Ordinal).ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly(); NumericWork = numericWork;
        RootEvaluationCount = rootEvaluationCount; EvaluationId = HashText(Pack(analysis.AnalysisId, MechanicalAssemblyProfile.Q(root)));
    }
    public MechanicalAssemblyAnalysis Analysis { get; }
    public string AnalysisId => Analysis.AnalysisId;
    public string EvaluationId { get; }
    public ExactQuantity RootInput { get; }
    /// <summary>Only exactly known affine shafts. Intermittent rotary and nonlinear prismatic poses remain typed member recipes/intervals.</summary>
    public ReadOnlyCollection<AssemblyShaftEvaluation> Shafts { get; }
    public ReadOnlyCollection<AssemblyMemberEvaluation> Members { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
    /// <summary>Actual interval arithmetic work including the matching analysis and each current member call, counted once.</summary>
    public int NumericWork { get; }
    public int RootEvaluationCount { get; }
    public int LocalEvaluationCount => Members.Count(m => m.InputTurns.HasValue || m.GenevaSuffix is not null);
    public bool AllRequestedNumericAvailable => Members.Count == Analysis.Draft.Definition.Members.Count && Members.All(m => m.NumericAvailable);
    public bool AllRequestedDisplayAvailable => Members.Count == Analysis.Draft.Definition.Members.Count && Members.All(m => m.DisplayAvailable);
}
