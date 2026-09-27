using System;
using System.Linq;
using GearInvest.Core;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>One deterministic arithmetic ceiling for a complete assembly request.</summary>
public sealed class AssemblyNumericRequest
{
    public const string CurrentPolicy = "assembly-aggregate-existing-numeric-kernels-v1";
    public AssemblyNumericRequest(int maximumWork = 262144, ExactQuantity? angularWidth = null,
        ExactQuantity? linearWidth = null, Rational? directionWidth = null, int maximumPrecisionBits = 512,
        int maximumRefinements = 4, bool includeDisplay = true, string policy = CurrentPolicy, int camContourSamples = 32)
    {
        MaximumWork = maximumWork; AngularWidth = angularWidth ?? ExactQuantity.Turns(new Rational(1, 1000000000000));
        LinearWidth = linearWidth ?? ExactQuantity.Millimeters(new Rational(1, 1000000000));
        DirectionWidth = directionWidth ?? new Rational(1, 1000000000000);
        MaximumPrecisionBits = maximumPrecisionBits; MaximumRefinements = maximumRefinements; IncludeDisplay = includeDisplay; Policy = policy;
        CamContourSamples = camContourSamples;
    }
    public int MaximumWork { get; }
    public ExactQuantity AngularWidth { get; }
    public ExactQuantity LinearWidth { get; }
    public Rational DirectionWidth { get; }
    public int MaximumPrecisionBits { get; }
    public int MaximumRefinements { get; }
    public bool IncludeDisplay { get; }
    public int CamContourSamples { get; }
    public string Policy { get; }
    public static AssemblyNumericRequest Default => new();
    public bool IsValid => Policy == CurrentPolicy && MaximumWork >= 0 && MaximumWork <= 262144 &&
        AngularWidth.Kind == QuantityKind.AngularPosition && AngularWidth.Value > 0 &&
        LinearWidth.Kind == QuantityKind.LinearPosition && LinearWidth.Value > 0 && DirectionWidth > 0 &&
        new[] { 64, 128, 256, 512, 1024, 2048, 4096 }.Contains(MaximumPrecisionBits) && MaximumRefinements >= 1 && MaximumRefinements <= 7 &&
        CamContourSamples >= 8 && CamContourSamples <= 128 && CrankSliderNumerics.InputBound(AngularWidth.Value) && CrankSliderNumerics.InputBound(LinearWidth.Value) && CrankSliderNumerics.InputBound(DirectionWidth);
    public string CanonicalRepresentation => Pack(Policy ?? "", N(MaximumWork), MechanicalAssemblyProfile.Q(AngularWidth),
        MechanicalAssemblyProfile.Q(LinearWidth), F(DirectionWidth), N(MaximumPrecisionBits), N(MaximumRefinements), IncludeDisplay ? "1" : "0", N(CamContourSamples));
}

internal sealed class AssemblyNumericBudget
{
    internal AssemblyNumericBudget(AssemblyNumericRequest? request = null) { Request = request ?? AssemblyNumericRequest.Default; }
    internal AssemblyNumericRequest Request { get; }
    internal int UsedWork { get; private set; }
    internal int RemainingWork => Request.IsValid ? Math.Max(0, Request.MaximumWork - UsedWork) : 0;
    internal bool IsValid => Request.IsValid;
    internal bool ShareGenevaSamples { get; set; }
    internal AssemblyGenevaSampleCache GenevaSamples { get; } = new();
    internal void Debit(int actualWork)
    {
        if (actualWork < 0 || actualWork > RemainingWork) throw new InvalidOperationException("A child arithmetic kernel exceeded its allocated assembly budget.");
        UsedWork = checked(UsedWork + actualWork);
    }
    internal GenevaNumericRequest GenevaRequest => new(Request.AngularWidth, Request.LinearWidth, Request.DirectionWidth,
        RemainingWork, Request.MaximumPrecisionBits, Request.MaximumRefinements);
    internal CrankSliderNumericRequest CrankRequest => new(Request.LinearWidth, RemainingWork, Request.MaximumPrecisionBits, Request.MaximumRefinements);
    internal CamNumericRequest CamRequest => new(Request.LinearWidth, RemainingWork, Request.MaximumPrecisionBits, Request.MaximumRefinements);
    internal CamGeometryProofRequest ProofRequest => new(Request.MaximumPrecisionBits, 16, 4096, RemainingWork);
}
