using System;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    /// <summary>Explicit root-only alternative using the declared suffix domains, unchanged. No search union or optimality comparison.</summary>
    public SharedDriverTransmissionGoal CreateRootOnlyComparisonGoal(SharedPrefixTransmissionGoal goal)
    {
        var n = NormalizeSharedPrefixTransmissionGoal(goal); if (!n.IsValid) throw new FormatException("Invalid shared-prefix comparison goal"); var g = n.Goal!;
        return new SharedDriverTransmissionGoal(g.Input, g.PitchRadiusTicksPerTooth, g.Outputs, g.Bounds, g.AvailableLayers, g.KeepOuts,
            g.UnrelatedClearance, g.KeepOutClearance, g.MaximumTotalCompounds, Math.Min(30, g.MaximumTotalIdlers), g.PreferredRegion, g.RequiredRegions,
            g.TotalWorkBudget, g.ReservedJoinWork, g.MaximumReturned, g.InputLayer, poolCountLimit: g.PoolCountLimit, poolByteLimit: g.PoolByteLimit, pairDomainLimit: g.TripleDomainLimit);
    }
    /// <summary>Runs the existing 16B API independently. Endpoints/keys/global targets must agree; finite domains and budgets may differ and remain explicit.</summary>
    public SharedDriverTransmissionResult GenerateRootOnlyComparison(SharedPrefixTransmissionGoal shared, SharedDriverTransmissionGoal rootOnly, CancellationToken token = default)
    { ValidateRootOnlyComparisonEndpoints(shared, rootOnly); return GenerateSharedDriverTransmissions(rootOnly, token); }
    public void ValidateRootOnlyComparisonEndpoints(SharedPrefixTransmissionGoal shared, SharedDriverTransmissionGoal rootOnly)
    {
        var a = NormalizeSharedPrefixTransmissionGoal(shared); var b = NormalizeSharedDriverTransmissionGoal(rootOnly);
        if (!a.IsValid || !b.IsValid || a.Goal!.Input.Canonical != b.Goal!.Input.Canonical || a.Goal.PitchRadiusTicksPerTooth != b.Goal.PitchRadiusTicksPerTooth ||
            !a.Goal.Outputs.Select(o => GearRoutingContract.Pack(o.Key, o.Anchor.Canonical, o.TargetTransfer.ToString())).SequenceEqual(b.Goal.Outputs.Select(o => GearRoutingContract.Pack(o.Key, o.Anchor.Canonical, o.TargetTransfer.ToString()))))
            throw new FormatException("Comparison requires identical actual input, scale, stable output keys, fixed endpoints and exact global targets");
    }
}
