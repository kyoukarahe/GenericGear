using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Finite, bounded preflight; serialization is supplied by the façade, not referenced by the engine.</summary>
public static class OrientedGoalCompiler
{
    public static OrientedGoalCompilation Compile(OrientedTwoOutputGoal goal, Func<OrientedGoalSource, OrientedGoalSourceRead> readSource,
        CancellationToken token = default)
    {
        if (goal is null || readSource is null) throw new ArgumentNullException(goal is null ? nameof(goal) : nameof(readSource));
        OrientedGoalCompilation Failure(OrientedGoalStatus s, string reason) => new(s, null, reason);
        if (token.IsCancellationRequested) return Failure(OrientedGoalStatus.Cancelled, "cancelled-before-preflight");
        var o = goal.Options; var l = goal.Limits;
        if (!OrientedGoalProfile.IsSupported(goal.Profile) || o.ResourceProfile != OrientedGoalProfile.Resources || !Enum.IsDefined(typeof(OrientedTurnedPolicy), goal.TurnedPolicy))
            return Failure(OrientedGoalStatus.Unsupported, "unsupported-profile-or-policy");
        if (goal.Outputs.Count != 2 || goal.Outputs.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != 2 ||
            goal.Outputs.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 120 || !Enum.IsDefined(typeof(OrientedOutputRole), x.Role)) ||
            goal.Outputs.Select(x => x.Role).Distinct().Count() != 2 || goal.ParallelSources.Count == 0 ||
            (goal.TurnedPolicy != OrientedTurnedPolicy.NoneOnly && goal.TurnedSources.Count == 0) ||
            goal.InputTeeth.Count == 0 || goal.OutputTeeth.Count == 0 || goal.PitchScales.Count == 0 ||
            goal.InputTeeth.Concat(goal.OutputTeeth).Any(t => t <= 0) || goal.PitchScales.Any(s => s <= 0) ||
            goal.InnerParameter <= 0 || goal.InnerParameter >= 1 || goal.Outputs.Any(x => x.RequiredTransfer == 0 || x.FixedBodyTeeth <= 0) ||
            goal.KeepOuts.Any(k => string.IsNullOrWhiteSpace(k.Id) || k.Id.Length > 160 || !k.Envelope.IsOrdered) ||
            goal.KeepOuts.Select(k => k.Id).Distinct(StringComparer.Ordinal).Count() != goal.KeepOuts.Count ||
            l.MaxBodies < 1 || l.MaxShafts < 1 || l.MaxTotalTeeth < 1 || l.WholeBounds.HasValue && !l.WholeBounds.Value.IsOrdered ||
            o.WorkBudget < 0 || o.ReturnedCandidateCap < 1 || o.MaxUniqueCandidates < 1 || o.MaxRetainedBytes < 1 || o.MaxOrigins < 1 || o.MaxDetails < 0)
            return Failure(OrientedGoalStatus.InvalidInput, "invalid-goal-domain-or-bounds");
        if (goal.InputTeeth.Concat(goal.OutputTeeth).Any(t => t > OrientedTransmissionProfile.MaxTeeth) ||
            o.WorkBudget > OrientedGoalProfile.MaxDomain || o.ReturnedCandidateCap > OrientedGoalProfile.MaxUniqueCandidates ||
            o.MaxUniqueCandidates > OrientedGoalProfile.MaxUniqueCandidates || o.MaxRetainedBytes > OrientedGoalProfile.MaxRetainedBytes ||
            o.MaxOrigins > OrientedGoalProfile.MaxOrigins || o.MaxDetails > OrientedGoalProfile.MaxDetails)
            return Failure(OrientedGoalStatus.Unsupported, "bounded-profile-limits-exceeded");
        var frames = new[] { goal.RootShaftFrame, goal.BevelOutputShaftFrame }.Concat(goal.Outputs.Select(x => x.TerminalFrame));
        if (frames.Any(f => f is null || !f.IsProperCardinal) || !goal.InputConeDirection.IsCardinal || !goal.OutputConeDirection.IsCardinal ||
            goal.InputConeDirection.Dot(goal.OutputConeDirection) != 0 || goal.RootShaftFrame.Z.Dot(goal.BevelOutputShaftFrame.Z) != 0)
            return Failure(OrientedGoalStatus.Unsupported, "proper-cardinal-right-angle-frames-required");
        var raw = goal.ParallelSources.Concat(goal.TurnedSources).ToArray();
        if (raw.Any(s => s.Placements.Any(p => p.Pose is null || p.InputMatingFrame is null)))
            return Failure(OrientedGoalStatus.InvalidInput, "missing-placement-frame");
        if (!OrientedGoalKeys.Bounded(goal)) return Failure(OrientedGoalStatus.Unsupported, "exact-number-digit-limit");
        if (raw.Sum(s => s.ByteLength) > OrientedGoalProfile.MaxTotalSourceBytes)
            return Failure(OrientedGoalStatus.Unsupported, "total-source-byte-limit");
        var resolved = new Dictionary<string, GenerationCandidate>(StringComparer.Ordinal);
        foreach (var source in raw)
        {
            if (token.IsCancellationRequested) return Failure(OrientedGoalStatus.Cancelled, "cancelled-during-preflight");
            if (source.Placements.Count == 0 || source.Placements.Any(p => p.PreferencePenalty < 0) ||
                !OrientedGoalKeys.IsPlanarHash(source.CandidateId) || !OrientedGoalKeys.IsPlanarHash(source.ArtifactHash) ||
                string.IsNullOrWhiteSpace(source.InputDofId) || string.IsNullOrWhiteSpace(source.OutputDofId) ||
                source.InputDofId.Length > 120 || source.OutputDofId.Length > 120)
                return Failure(OrientedGoalStatus.InvalidInput, "invalid-source-definition");
            if (source.Placements.Any(p => p.Pose is null || p.InputMatingFrame is null || !p.Pose.IsProperCardinal || !p.InputMatingFrame.IsProperCardinal))
                return Failure(OrientedGoalStatus.Unsupported, "unsupported-placement-frame");
            var id = OrientedGoalKeys.Source(source);
            if (resolved.ContainsKey(id)) continue;
            var read = readSource(source);
            if (read.Status != OrientedGoalStatus.Complete || read.Candidate is null) return Failure(read.Status, "source-preflight: " + read.Detail);
            if (read.Candidate.Kinematic.Couplings.Any(c => c is not ExternalGearCoupling) ||
                read.Candidate.Spatial.Contacts.Any(c => c.Kind != Layout.SpatialContactKind.ExternalGearMesh) ||
                read.Candidate.Spatial.Bodies.Any(b => b.ToothCount > OrientedTransmissionProfile.MaxTeeth || b.PitchRadius.ToString(CultureInfo.InvariantCulture).Length > OrientedGoalProfile.MaxInputDigits) ||
                read.Candidate.Spatial.Axes.Any(a => a.X.ToString(CultureInfo.InvariantCulture).Length > OrientedGoalProfile.MaxInputDigits || a.Y.ToString(CultureInfo.InvariantCulture).Length > OrientedGoalProfile.MaxInputDigits))
                return Failure(OrientedGoalStatus.Unsupported, "external-spur-source-or-derived-number-profile-limit");
            var probe = new PlanarShaftModule(read.Candidate, source.CandidateId, source.ArtifactHash, OrientedFrame.Identity,
                source.InputDofId, source.OutputDofId, new ShaftPort("input-port", source.InputDofId, OrientedFrame.Identity));
            var issue = OrientedTransmissionComposer.ValidateSource(probe);
            if (issue is not null) return Failure(issue.Status == OrientedOperationStatus.Unsupported ? OrientedGoalStatus.Unsupported : OrientedGoalStatus.InvalidInput,
                "source-profile-or-semantic-port: " + string.Join(";", issue.Validation.Checks.Select(c => c.Detail)));
            resolved.Add(id, read.Candidate);
        }
        OrientedGoalSource[] NormalizeSources(IEnumerable<OrientedGoalSource> sources) => sources.GroupBy(OrientedGoalKeys.Source, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            {
                var first = g.First(); var placements = g.SelectMany(s => s.Placements).GroupBy(OrientedGoalKeys.Placement, StringComparer.Ordinal)
                    .Select(x => x.First()).OrderBy(p => p, OrientedGoalKeys.PlacementComparer).ToArray();
                if (placements.Length > OrientedGoalProfile.MaxPlacements) throw new ArgumentException("Merged placement domain exceeds supported bound.");
                return new OrientedGoalSource(first.SourceBytes, first.CandidateId, first.ArtifactHash, first.InputDofId, first.OutputDofId, placements);
            }).ToArray();
        OrientedGoalSource[] a, b;
        try { a = NormalizeSources(goal.ParallelSources); b = NormalizeSources(goal.TurnedSources); }
        catch (ArgumentException e) { return Failure(OrientedGoalStatus.Unsupported, e.Message); }
        var normalized = new OrientedTwoOutputGoal(goal.RootShaftFrame, goal.BevelOutputShaftFrame, goal.Apex, goal.InputConeDirection,
            goal.OutputConeDirection, goal.InnerParameter, goal.Outputs.OrderBy(x => x.Key, StringComparer.Ordinal),
            goal.InputTeeth.Distinct().OrderBy(x => x), goal.OutputTeeth.Distinct().OrderBy(x => x), goal.PitchScales.Distinct().OrderBy(x => x),
            a, goal.TurnedPolicy, b, goal.RequireCrossComponentClearance, goal.KeepOuts.OrderBy(k => k.Id, StringComparer.Ordinal), goal.Limits, goal.Options, goal.Profile, goal.UnattachedTurnedMatingStation);
        BigInteger tuples = new BigInteger(normalized.InputTeeth.Count) * normalized.OutputTeeth.Count * normalized.PitchScales.Count * a.Sum(s => s.Placements.Count) *
            (b.Sum(s => s.Placements.Count) + (goal.TurnedPolicy == OrientedTurnedPolicy.RequiredSource ? 0 : 1));
        if (tuples > OrientedGoalProfile.MaxDomain) return Failure(OrientedGoalStatus.Unsupported, "finite-domain-limit: " + tuples.ToString(CultureInfo.InvariantCulture));
        return new OrientedGoalCompilation(OrientedGoalStatus.Complete, new OrientedGoalPlan(normalized, OrientedGoalKeys.Goal(normalized), tuples,
            a.Select(s => new OrientedGoalResolvedSource(OrientedGoalKeys.Source(s), s, resolved[OrientedGoalKeys.Source(s)])),
            b.Select(s => new OrientedGoalResolvedSource(OrientedGoalKeys.Source(s), s, resolved[OrientedGoalKeys.Source(s)]))), "preflight-complete");
    }

    public static IEnumerable<OrientedGoalTuple> Enumerate(OrientedGoalPlan plan)
    {
        var ordinal = 0;
        foreach (var input in plan.Goal.InputTeeth)
        foreach (var output in plan.Goal.OutputTeeth)
        foreach (var scale in plan.Goal.PitchScales)
        foreach (var a in plan.ParallelSources)
        for (var ai = 0; ai < a.Definition.Placements.Count; ai++)
        {
            if (plan.Goal.TurnedPolicy != OrientedTurnedPolicy.RequiredSource)
                yield return new OrientedGoalTuple(ordinal++, input, output, scale, a.Id, ai, null, -1);
            foreach (var b in plan.TurnedSources)
            for (var bi = 0; bi < b.Definition.Placements.Count; bi++)
                yield return new OrientedGoalTuple(ordinal++, input, output, scale, a.Id, ai, b.Id, bi);
        }
    }

    /// <summary>Decode one tuple without materializing child requests or walking the preceding search prefix.</summary>
    public static OrientedGoalTuple Decode(OrientedGoalPlan plan, int ordinal)
    {
        if (ordinal < 0 || ordinal >= plan.TupleCount) throw new ArgumentOutOfRangeException(nameof(ordinal));
        var bCount = plan.TurnedSources.Sum(s => s.Definition.Placements.Count) + (plan.Goal.TurnedPolicy == OrientedTurnedPolicy.RequiredSource ? 0 : 1);
        var n = ordinal; var bi = n % bCount; n /= bCount;
        var aCount = plan.ParallelSources.Sum(s => s.Definition.Placements.Count); var ai = n % aCount; n /= aCount;
        var scale = plan.Goal.PitchScales[n % plan.Goal.PitchScales.Count]; n /= plan.Goal.PitchScales.Count;
        var output = plan.Goal.OutputTeeth[n % plan.Goal.OutputTeeth.Count]; n /= plan.Goal.OutputTeeth.Count;
        var a = plan.ParallelSources.First(s => { if (ai < s.Definition.Placements.Count) return true; ai -= s.Definition.Placements.Count; return false; });
        OrientedGoalResolvedSource? b = null;
        if (plan.Goal.TurnedPolicy != OrientedTurnedPolicy.RequiredSource) bi--;
        if (bi >= 0) b = plan.TurnedSources.First(s => { if (bi < s.Definition.Placements.Count) return true; bi -= s.Definition.Placements.Count; return false; });
        return new OrientedGoalTuple(ordinal, plan.Goal.InputTeeth[n], output, scale, a.Id, ai, b?.Id, bi);
    }
}

/// <summary>Versioned invariant, length-prefixed identities; numeric traversal never uses these string keys as coordinate order.</summary>
public static class OrientedGoalKeys
{
    public static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))); }
    public static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static bool IsHash(string value) => value is not null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
    public static bool IsPlanarHash(string value) => value is not null && value.StartsWith("sha256:", StringComparison.Ordinal) && IsHash(value.Substring(7));
    public static string Pack(params string[] values) => string.Concat(values.Select(s => s.Length.ToString(CultureInfo.InvariantCulture) + ":" + s));
    public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
    public static string F(Rational f) => f.Numerator.ToString(CultureInfo.InvariantCulture) + "/" + f.Denominator.ToString(CultureInfo.InvariantCulture);
    public static string V(ExactVector3 v) => Pack(F(v.X), F(v.Y), F(v.Z));
    public static string Frame(OrientedFrame f) => Pack(V(f.Origin), V(f.X), V(f.Y), V(f.Z));
    public static string Source(OrientedGoalSource s) => HashText(Pack(Hash(s.SourceBytes), s.CandidateId, s.ArtifactHash, s.InputDofId, s.OutputDofId));
    public static string Placement(OrientedGoalPlacement p) => Pack(Frame(p.Pose), Frame(p.InputMatingFrame), F(p.PreferencePenalty));
    public static string Tuple(OrientedGoalTuple t) => Pack(N(t.InputTeeth), N(t.OutputTeeth), F(t.Scale), t.ParallelSourceId, N(t.ParallelPlacement), t.TurnedSourceId ?? "None", N(t.TurnedPlacement));
    public static string Origin(OrientedGoalPlan plan, OrientedGoalTuple t) => HashText(Pack(plan.GoalId, Tuple(t)));
    internal static string Goal(OrientedTwoOutputGoal g) => HashText(Pack(g.Profile, OrientedGoalProfile.Lowering, OrientedGoalProfile.Traversal, OrientedGoalProfile.Ranking,
        Frame(g.RootShaftFrame), Frame(g.BevelOutputShaftFrame), V(g.UnattachedTurnedMatingStation), V(g.Apex), V(g.InputConeDirection), V(g.OutputConeDirection), F(g.InnerParameter),
        Pack(g.Outputs.Select(o => Pack(o.Key, o.Role.ToString(), Frame(o.TerminalFrame), F(o.RequiredTransfer), o.FixedBodyCenter.HasValue ? V(o.FixedBodyCenter.Value) : "none", o.FixedBodyTeeth.HasValue ? N(o.FixedBodyTeeth.Value) : "none")).ToArray()),
        Pack(g.InputTeeth.Select(N).ToArray()), Pack(g.OutputTeeth.Select(N).ToArray()), Pack(g.PitchScales.Select(F).ToArray()),
        Pack(g.ParallelSources.Select(s => Pack(Source(s), Pack(s.Placements.Select(Placement).ToArray()))).ToArray()), g.TurnedPolicy.ToString(),
        Pack(g.TurnedSources.Select(s => Pack(Source(s), Pack(s.Placements.Select(Placement).ToArray()))).ToArray()), g.RequireCrossComponentClearance ? "required" : "optional",
        Pack(g.KeepOuts.Select(k => Pack(k.Id, V(k.Envelope.Min), V(k.Envelope.Max))).ToArray()), N(g.Limits.MaxBodies), N(g.Limits.MaxShafts), N(g.Limits.MaxTotalTeeth),
        g.Limits.WholeBounds.HasValue ? Pack(V(g.Limits.WholeBounds.Value.Min), V(g.Limits.WholeBounds.Value.Max)) : "none"));
    private static IEnumerable<Rational> Components(ExactVector3 v) => new[] { v.X, v.Y, v.Z };
    private static IEnumerable<Rational> Components(OrientedFrame f) => Components(f.Origin).Concat(Components(f.X)).Concat(Components(f.Y)).Concat(Components(f.Z));
    public static IComparer<OrientedGoalPlacement> PlacementComparer { get; } = Comparer<OrientedGoalPlacement>.Create((a, b) =>
    {
        var av = Components(a.Pose).Concat(Components(a.InputMatingFrame)).Append(a.PreferencePenalty);
        var bv = Components(b.Pose).Concat(Components(b.InputMatingFrame)).Append(b.PreferencePenalty);
        foreach (var pair in av.Zip(bv, (x, y) => (X: x, Y: y))) { var c = pair.X.CompareTo(pair.Y); if (c != 0) return c; } return 0;
    });
    internal static bool Bounded(OrientedTwoOutputGoal g)
    {
        var values = Components(g.RootShaftFrame).Concat(Components(g.BevelOutputShaftFrame)).Concat(Components(g.UnattachedTurnedMatingStation)).Concat(Components(g.Apex)).Concat(g.PitchScales).Append(g.InnerParameter)
            .Concat(g.Outputs.SelectMany(o => Components(o.TerminalFrame).Append(o.RequiredTransfer).Concat(o.FixedBodyCenter.HasValue ? Components(o.FixedBodyCenter.Value) : Array.Empty<Rational>())))
            .Concat(g.ParallelSources.Concat(g.TurnedSources).SelectMany(s => s.Placements.SelectMany(p => Components(p.Pose).Concat(Components(p.InputMatingFrame)).Append(p.PreferencePenalty))))
            .Concat(g.KeepOuts.SelectMany(k => Components(k.Envelope.Min).Concat(Components(k.Envelope.Max))));
        if (g.Limits.WholeBounds.HasValue) values = values.Concat(Components(g.Limits.WholeBounds.Value.Min)).Concat(Components(g.Limits.WholeBounds.Value.Max));
        return values.All(v => v.Numerator.ToString(CultureInfo.InvariantCulture).Length <= OrientedGoalProfile.MaxInputDigits && v.Denominator.ToString(CultureInfo.InvariantCulture).Length <= OrientedGoalProfile.MaxInputDigits);
    }
}
