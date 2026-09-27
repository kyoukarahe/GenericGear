using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public static class TwoOutputTransmissionCompiler
{
    internal static Diagnostic Error(string code, string subject, string message) => new Diagnostic("TWO_OUTPUT_" + code, DiagnosticSeverity.Error, message, subject);
    internal static SharedDriverTransmissionGoal LowerRoot(TwoOutputTransmissionGoal g, int quota) => new SharedDriverTransmissionGoal(
        g.Input, g.PitchRadiusTicksPerTooth, g.Outputs, g.Bounds, g.AvailableLayers, g.KeepOuts, g.UnrelatedClearance, g.KeepOutClearance,
        Math.Min(g.MaximumTotalCompounds, 4), Math.Min(g.MaximumTransmissionGears, 30), g.PreferredRegion, g.RequiredRegions,
        quota, quota / 4, 1, g.InputLayer);

    internal static SharedPrefixTransmissionGoal LowerPrefix(TwoOutputTransmissionGoal g, int quota)
    {
        var p = g.SharedPrefix!;
        return new SharedPrefixTransmissionGoal(g.Input, g.PitchRadiusTicksPerTooth, g.Outputs, p.Routing, p.SplitterTeeth, g.Bounds,
            p.HasExplicitSites ? p.SplitterSites : null, p.SplitterGrid, p.RequiredSplitterRegion, p.PreferredSplitterRegion, p.KeepOuts,
            g.AvailableLayers, g.KeepOuts, g.UnrelatedClearance, g.KeepOutClearance, Math.Min(g.MaximumTotalCompounds, 4),
            Math.Min(Math.Max(0, g.MaximumTransmissionGears - 1), 35), Math.Min(Math.Max(1, g.MaximumBodies), 47), g.PreferredRegion, g.RequiredRegions,
            quota, quota / 4, quota / 4, 1, hypothesisLimit: p.HypothesisLimit, inputLayer: g.InputLayer);
    }

    public static TwoOutputGoalNormalization Normalize(TwoOutputTransmissionGoal? raw)
    {
        var ds = new List<Diagnostic>();
        void Bad(string field, string text) => ds.Add(Error("INVALID_INPUT", field, text));
        void Unsupported(string field, string text) => ds.Add(Error("UNSUPPORTED", field, text));
        TwoOutputGoalNormalization Invalid() => new TwoOutputGoalNormalization(null,
            ds.Any(d => d.Code == "TWO_OUTPUT_INVALID_INPUT") ? TwoOutputTransmissionStatus.InvalidInput : TwoOutputTransmissionStatus.Unsupported, ds);
        if (raw == null || raw.Input == null || raw.Bounds == null) { Bad("goal", "Common input and bounds required."); return Invalid(); }
        if (!Enum.IsDefined(typeof(TwoOutputSharingPolicy), raw.SharingPolicy)) Bad("sharingPolicy", "Unknown active sharing policy.");
        if (raw.MaximumTotalCompounds < 0 || raw.MaximumBodies < 0 || raw.MaximumTransmissionGears < 0 || raw.TotalWorkBudget < 0 || raw.MaximumReturned < 1 ||
            raw.ObservationCountLimit < 1 || raw.ObservationByteLimit < 1) Bad("limits", "Nonnegative topology/work and positive return/observation limits required.");
        if (raw.TotalWorkBudget > TransmissionGoalContract.MaxWork || raw.MaximumReturned > TwoOutputTransmissionContract.MaxReturned ||
            raw.ObservationCountLimit > TwoOutputTransmissionContract.MaxObservations || raw.ObservationByteLimit > TwoOutputTransmissionContract.MaxObservationBytes)
            Unsupported("limits", "Versioned finite work/observation/return envelope exceeded.");
        if (raw.Profile != TwoOutputTransmissionContract.Profile || raw.Lowering != TwoOutputTransmissionContract.Lowering || raw.Allocation != TwoOutputTransmissionContract.Allocation ||
            raw.Ranking != TwoOutputTransmissionContract.Ranking || raw.Resources != TwoOutputTransmissionContract.Resources) Unsupported("policyVersion", "Unknown policy/backend version; no migration.");
        if (raw.IngestionLimitExceeded) Unsupported("ingestion", "Truncated ingestion is not a smaller valid goal.");
        bool prefixActive = raw.SharingPolicy != TwoOutputSharingPolicy.RootOnly;
        if (prefixActive && (raw.SharedPrefix == null || raw.SharedPrefix.Routing == null)) Bad("sharedPrefix", "Active sharing policy requires a prefix domain.");
        if (ds.Count != 0) return Invalid();
        // The old root normalizer owns the common scalar/layer/leg/slot semantics, not sharing policy.
        // Loose global upper bounds are intersected with intrinsic profile maxima, never per-leg depth.
        var common = SharedDriverTransmissionCompiler.Normalize(LowerRoot(raw, 0));
        if (!common.IsValid)
        {
            ds.AddRange(common.Diagnostics.Select(d => Error(common.Status == SharedDriverTransmissionStatus.InvalidInput ? "INVALID_INPUT" : "UNSUPPORTED", d.SubjectId ?? "common", d.Message)));
            return Invalid();
        }
        TwoOutputPrefixDomain? prefix = null;
        if (prefixActive)
        {
            if (raw.SharedPrefix!.IngestionLimitExceeded || raw.SharedPrefix!.Routing!.IngestionLimitExceeded)
            { Unsupported("sharedPrefix", "Truncated active prefix ingestion is not accepted."); return Invalid(); }
            var n = SharedPrefixTransmissionCompiler.Normalize(LowerPrefix(raw, 0));
            if (n.Status == SharedPrefixTransmissionStatus.InvalidInput)
            { ds.AddRange(n.Diagnostics.Select(d => Error("INVALID_INPUT", d.SubjectId ?? "sharedPrefix", d.Message))); return Invalid(); }
            // Unsupported active prefix coverage remains explicit in its strategy. A supported independent root profile may still execute.
            if (n.Goal == null) prefix = raw.SharedPrefix;
            else
            {
                var p = n.Goal;
                prefix = new TwoOutputPrefixDomain(p.Prefix, p.SplitterTeeth, p.SplitterSites, requiredSplitterRegion: p.RequiredSplitterRegion,
                    preferredSplitterRegion: p.PreferredSplitterRegion, keepOuts: p.PrefixKeepOuts, hypothesisLimit: p.HypothesisLimit);
            }
        }
        var c = common.Goal!;
        var goal = new TwoOutputTransmissionGoal(c.Input, c.PitchRadiusTicksPerTooth, c.Outputs, c.Bounds, raw.SharingPolicy, prefix, c.AvailableLayers,
            c.KeepOuts, c.UnrelatedClearance, c.KeepOutClearance, raw.MaximumTotalCompounds, raw.MaximumBodies, raw.MaximumTransmissionGears,
            c.PreferredRegion, c.RequiredRegions, raw.TotalWorkBudget, raw.MaximumReturned, raw.InputLayer, raw.ObservationCountLimit, raw.ObservationByteLimit,
            raw.Profile, raw.Lowering, raw.Allocation, raw.Ranking, raw.Resources);
        if (goal.Canonical.Length > TwoOutputTransmissionContract.MaxCanonicalGoalChars) { Unsupported("canonicalGoal", "Bounded preparation/goal envelope exceeded."); return Invalid(); }
        return new TwoOutputGoalNormalization(goal, TwoOutputTransmissionStatus.Complete, ds);
    }

    private static bool Empty(CompiledTransmissionSearchPlan p) => p.IsSupported && p.Families.All(f => f.Applicability != TransmissionApplicability.Eligible);
    private static TwoOutputStrategyPlan Analyze(TwoOutputTransmissionGoal g, TwoOutputSharingStrategy s, int quota)
    {
        if (s == TwoOutputSharingStrategy.RootOnly && g.SharingPolicy == TwoOutputSharingPolicy.RequiredSharedPrefix ||
            s == TwoOutputSharingStrategy.SharedPrefix && g.SharingPolicy == TwoOutputSharingPolicy.RootOnly)
            return new TwoOutputStrategyPlan(s, TwoOutputStrategyApplicability.ExcludedByPolicy, 0, "EXCLUDED_BY_SHARING_POLICY");
        if (s == TwoOutputSharingStrategy.RootOnly)
        {
            var p = SharedDriverTransmissionCompiler.Compile(LowerRoot(g, quota));
            if (!p.IsSupported) return new TwoOutputStrategyPlan(s, p.Status == SharedDriverTransmissionStatus.InvalidInput ? TwoOutputStrategyApplicability.InvalidInput : TwoOutputStrategyApplicability.Unsupported,
                0, "ROOT_ONLY_DOMAIN_NOT_SUPPORTED", rootOnly: p);
            string proof = g.MaximumBodies < 3 ? "COMMON_MINIMUM_THREE_ENDPOINT_BODIES" :
                p.Branches.Any(b => Empty(b.Plan)) ? "ROOT_REQUIRED_OUTPUT_EXACT_EMPTY:" + p.Branches.First(b => Empty(b.Plan)).OutputKey : "";
            return new TwoOutputStrategyPlan(s, proof.Length != 0 ? TwoOutputStrategyApplicability.ProvenInfeasible : quota == 0 ? TwoOutputStrategyApplicability.PendingBudget : TwoOutputStrategyApplicability.Eligible,
                quota, proof.Length != 0 ? proof : quota == 0 ? "ZERO_FIXED_QUOTA_NO_BACKEND_CALL" : "SUPPORTED_FINITE_ROOT_ONLY_DOMAIN", rootOnly: p);
        }
        else
        {
            var p = SharedPrefixTransmissionCompiler.Compile(LowerPrefix(g, quota));
            if (!p.IsSupported) return new TwoOutputStrategyPlan(s, p.Status == SharedPrefixTransmissionStatus.IncompleteResource ? TwoOutputStrategyApplicability.IncompleteResource :
                p.Status == SharedPrefixTransmissionStatus.InvalidInput ? TwoOutputStrategyApplicability.InvalidInput : TwoOutputStrategyApplicability.Unsupported,
                0, p.LimitingReason, sharedPrefix: p);
            string proof = g.MaximumBodies < 4 ? "COMMON_MINIMUM_FOUR_PREFIX_ENDPOINT_BODIES" : g.MaximumTransmissionGears < 1 ? "COMMON_SPLITTER_IS_ONE_TRANSMISSION_GEAR" :
                p.Hypotheses.All(h => h.ExclusionProof.Length != 0 || Empty(h.Prefix!) || h.Suffix!.Branches.Any(b => Empty(b.Plan))) ? "ALL_PREFIX_HYPOTHESES_EXACT_FACTOR_EMPTY_OR_EXCLUDED" : "";
            return new TwoOutputStrategyPlan(s, proof.Length != 0 ? TwoOutputStrategyApplicability.ProvenInfeasible : quota == 0 ? TwoOutputStrategyApplicability.PendingBudget : TwoOutputStrategyApplicability.Eligible,
                quota, proof.Length != 0 ? proof : quota == 0 ? "ZERO_FIXED_QUOTA_NO_BACKEND_CALL" : "SUPPORTED_FINITE_SHARED_PREFIX_DOMAIN", sharedPrefix: p);
        }
    }

    public static TwoOutputSharingSearchPlan Compile(TwoOutputTransmissionGoal raw)
    {
        var n = Normalize(raw);
        if (!n.IsValid) return new TwoOutputSharingSearchPlan(n, Array.Empty<TwoOutputStrategyPlan>(), n.Diagnostics);
        var g = n.Goal!;
        var probes = new[] { Analyze(g, TwoOutputSharingStrategy.RootOnly, 0), Analyze(g, TwoOutputSharingStrategy.SharedPrefix, 0) };
        int count = probes.Count(p => p.Applicability == TwoOutputStrategyApplicability.PendingBudget), ordinal = 0;
        var strategies = new List<TwoOutputStrategyPlan>();
        foreach (var probe in probes)
        {
            if (probe.Applicability != TwoOutputStrategyApplicability.PendingBudget) { strategies.Add(probe); continue; }
            int quota = g.TotalWorkBudget / count + (ordinal++ < g.TotalWorkBudget % count ? 1 : 0);
            var actual = Analyze(g, probe.Strategy, quota);
            if (actual.Quota != quota) throw new InvalidOperationException("SharingAnalysisChangedAfterAllocation");
            if (actual.RootOnly != null)
            {
                var child = actual.RootOnly.Normalized.Goal!;
                if (child.TotalWorkBudget != quota || child.ReservedCombinationWork != quota / 4 || actual.RootOnly.AllocatedGenerationWork > quota - quota / 4)
                    throw new InvalidOperationException("RootNestedAllocationDisagreement");
            }
            if (actual.SharedPrefix != null)
            {
                var child = actual.SharedPrefix.Normalized.Goal!;
                if (child.TotalWorkBudget != quota || child.ReservedPrefixWork != quota / 4 || child.ReservedJoinWork != quota / 4 ||
                    actual.SharedPrefix.AllocatedPrefixWork > quota / 4 || actual.SharedPrefix.AllocatedSuffixWork > quota - 2 * (quota / 4))
                    throw new InvalidOperationException("PrefixNestedAllocationDisagreement");
            }
            strategies.Add(actual);
        }
        return new TwoOutputSharingSearchPlan(n, strategies, n.Diagnostics);
    }
}
