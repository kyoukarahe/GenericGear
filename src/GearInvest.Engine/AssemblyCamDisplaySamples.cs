using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>Bounded contour samples in world mm at the requested absolute global input. Optional display only.</summary>
public sealed class AssemblyCamDisplaySamples
{
    internal AssemblyCamDisplaySamples(string analysisId, ExactQuantity root, int requestedSamples, IEnumerable<CamNumericResult> samples)
    {
        AnalysisId = analysisId; RootTurns = root; RequestedSamples = requestedSamples; SampleResults = samples.ToList().AsReadOnly();
        NumericWork = SampleResults.Sum(s => s.Work); CompletedSamples = SampleResults.Count(s => s.IsAvailable);
        IsAvailable = SampleResults.Count == RequestedSamples && SampleResults.All(s => s.IsAvailable && s.Point is not null);
        var failure = SampleResults.FirstOrDefault(s => !s.IsAvailable);
        Status = IsAvailable ? SampleResults.All(s => s.Status == CamNumericStatus.ExactValue) ? CamNumericStatus.ExactValue : CamNumericStatus.EnclosedValue :
            failure?.Status ?? CamNumericStatus.IncompleteNumericBudget;
        ContourPointsMm = (IsAvailable ? SampleResults.Select(s => s.Point!) : Array.Empty<CamVectorInterval>()).ToList().AsReadOnly();
        MaterialMarkerMm = IsAvailable ? ContourPointsMm[0] : null;
        Detail = IsAvailable ? "Complete requested current-phase world-mm contour; the material-zero marker is the first sample." :
            failure?.Detail ?? "The complete requested contour is unavailable; no partial or previous contour is a normal display.";
        ResultId = HashText(CanonicalRepresentation);
    }
    public string AnalysisId { get; }
    public ExactQuantity RootTurns { get; }
    public int RequestedSamples { get; }
    public int CompletedSamples { get; }
    public bool IsAvailable { get; }
    public CamNumericStatus Status { get; }
    public ReadOnlyCollection<CamVectorInterval> ContourPointsMm { get; }
    public CamVectorInterval? MaterialMarkerMm { get; }
    /// <summary>Current-call evidence, including any failed point; normal contour data is available only when IsAvailable.</summary>
    public ReadOnlyCollection<CamNumericResult> SampleResults { get; }
    public int NumericWork { get; }
    public string Detail { get; }
    public string ResultId { get; }
    public string CanonicalRepresentation => Pack(AnalysisId, MechanicalAssemblyProfile.Q(RootTurns), N(RequestedSamples), N(CompletedSamples),
        IsAvailable ? "1" : "0", Status.ToString(), N(NumericWork), Pack(SampleResults.Select(s => s.CanonicalRepresentation).ToArray()), Detail);
}

internal static class AssemblyCamDisplay
{
    internal static AssemblyCamDisplaySamples Evaluate(string analysisId, CamFollowerMotionDescriptor motion, ExactQuantity root, AssemblyNumericBudget budget)
    {
        var samples = new List<CamNumericResult>(); var count = budget.Request.CamContourSamples;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var recipe = motion.CreateContourPointRecipe(new Rational(i, count), root);
                var numeric = CamFollowerNumerics.Evaluate(recipe, budget.CamRequest); budget.Debit(numeric.Work); samples.Add(numeric);
                if (!numeric.IsAvailable) break;
            }
            catch (Exception e) when (e is ArgumentException || e is CrankSliderNumerics.Stop)
            {
                samples.Add(new CamNumericResult(HashText(Pack(analysisId, MechanicalAssemblyProfile.Q(root), N(i))), budget.CamRequest,
                    CamNumericStatus.NumericResourceLimit, 0, 0, 0, null, null, "Optional contour recipe unavailable: " + e.Message));
                break;
            }
        }
        return new(analysisId, root, count, samples);
    }
}
