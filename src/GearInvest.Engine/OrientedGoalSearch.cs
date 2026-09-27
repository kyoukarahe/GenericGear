using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class OrientedGoalSearch
{
    public static OrientedTwoOutputCompositionRequest Lower(OrientedGoalPlan plan, OrientedGoalTuple tuple)
    {
        if (OrientedGoalKeys.Tuple(tuple) != OrientedGoalKeys.Tuple(OrientedGoalCompiler.Decode(plan, tuple.Ordinal)))
            throw new ArgumentException("Tuple is not a declared normalized domain member.");
        var goal = plan.Goal;
        var a = plan.ParallelSources.Single(s => s.Id == tuple.ParallelSourceId); var ap = a.Definition.Placements[tuple.ParallelPlacement];
        var b = tuple.TurnedSourceId is null ? null : plan.TurnedSources.Single(s => s.Id == tuple.TurnedSourceId);
        var bp = b?.Definition.Placements[tuple.TurnedPlacement];
        var input = new OrientedShaft("bevel/input", goal.RootShaftFrame, true);
        var output = new OrientedShaft("bevel/output", goal.BevelOutputShaftFrame);
        var bevel = new RightAngleBevelRequest(goal.Apex,
            new BevelGearMount(input, goal.InputConeDirection, tuple.InputTeeth, tuple.Scale,
                goal.Apex + goal.InputConeDirection * (tuple.OutputTeeth * tuple.Scale),
                new ShaftPort("bevel/input-port", input.Id, input.Frame.At(ap.InputMatingFrame.Origin))),
            new BevelGearMount(output, goal.OutputConeDirection, tuple.OutputTeeth, tuple.Scale,
                goal.Apex + goal.OutputConeDirection * (tuple.InputTeeth * tuple.Scale),
                new ShaftPort("bevel/output-port", output.Id, output.Frame.At(bp?.InputMatingFrame.Origin ?? goal.UnattachedTurnedMatingStation))),
            goal.InnerParameter);
        PlanarShaftModule Module(OrientedGoalResolvedSource s, OrientedGoalPlacement p) => new(s.Candidate, s.Definition.CandidateId, s.Definition.ArtifactHash,
            p.Pose, s.Definition.InputDofId, s.Definition.OutputDofId, new ShaftPort("input-port", s.Definition.InputDofId, p.InputMatingFrame));
        var parallel = Module(a, ap); var turned = b is null ? null : Module(b, bp!);
        var outputs = goal.Outputs.Select(o =>
        {
            var source = o.Role == OrientedOutputRole.ParallelBranch ? parallel : turned;
            var shaft = source?.OutputDofId ?? output.Id;
            var body = source?.Source.Spatial.Bodies.Single(g => g.DofId == shaft).Id ?? "bevel/wheel";
            return new OrientedOutputRequest(o.Key, o.Role, body, new ShaftPort("terminal-port", shaft, o.TerminalFrame), o.RequiredTransfer);
        });
        return new OrientedTwoOutputCompositionRequest(bevel, parallel, outputs, turned, OrientedFrame.Identity,
            goal.RequireCrossComponentClearance, goal.KeepOuts, OrientedGoalProfile.MechanicalProfile(goal.Profile));
    }

    public static Rational Preference(OrientedGoalPlan plan, OrientedGoalTuple t) =>
        plan.ParallelSources.Single(s => s.Id == t.ParallelSourceId).Definition.Placements[t.ParallelPlacement].PreferencePenalty +
        (t.TurnedSourceId is null ? Rational.Zero : plan.TurnedSources.Single(s => s.Id == t.TurnedSourceId).Definition.Placements[t.TurnedPlacement].PreferencePenalty);

    private static string? ProvenTargetMismatch(OrientedTwoOutputCompositionRequest r)
    {
        if (r.Bevel.Input.Shaft.Frame.Z.Cross(r.Bevel.Input.ConeDirection) != ExactVector3.Zero ||
            r.Bevel.Output.Shaft.Frame.Z.Cross(r.Bevel.Output.ConeDirection) != ExactVector3.Zero) return "fixed-bevel-axis-mismatch";
        var contactVector = r.Bevel.Input.ConeDirection * (r.Bevel.Output.Teeth * r.Bevel.Output.OuterPitchRadiusPerTooth) +
            r.Bevel.Output.ConeDirection * (r.Bevel.Input.Teeth * r.Bevel.Input.OuterPitchRadiusPerTooth);
        var bevel = OrientedMechanismValidator.VelocityTransfer(r.Bevel.Input.Shaft.Frame.Z, r.Bevel.Output.Shaft.Frame.Z, contactVector);
        foreach (var target in r.Outputs)
        {
            var isA = target.Role == OrientedOutputRole.ParallelBranch; var source = isA ? r.ParallelBranch : r.TurnedBranch;
            var driverAxis = isA ? r.Bevel.Input.Shaft.Frame.Z : r.Bevel.Output.Shaft.Frame.Z;
            var epsilon = source is null ? Rational.One : source.Pose.Z.Dot(driverAxis);
            var terminalSign = target.TerminalPort.Frame.Z.Dot(source?.Pose.Z ?? driverAxis);
            if (epsilon == 0 || terminalSign == 0) return "terminal-source-axis-mismatch/" + target.Key;
            var sourceRatio = Rational.One;
            if (source is not null)
            {
                source.Source.Solution.TryGetState(source.InputDofId, out var input); source.Source.Solution.TryGetState(source.OutputDofId, out var output);
                sourceRatio = output!.Coefficient / input!.Coefficient;
            }
            var actual = terminalSign * epsilon * sourceRatio * (isA ? Rational.One : bevel);
            if (target.RequestedTransfer.HasValue && target.RequestedTransfer.Value != actual) return "exact-terminal-transfer/" + target.Key;
        }
        return null;
    }

    public static OrientedGoalMetrics Measure(OrientedTwoOutputMechanism model, Rational penalty)
    {
        var boxes = model.Bodies.Select(b => ExactEnvelope3.Of(b, model.Contacts.FirstOrDefault(c => c.Cone is not null && (c.BodyAId == b.Id || c.BodyBId == b.Id))?.Cone)).ToArray();
        var min = new ExactVector3(boxes.Min(e => e.Min.X), boxes.Min(e => e.Min.Y), boxes.Min(e => e.Min.Z));
        var max = new ExactVector3(boxes.Max(e => e.Max.X), boxes.Max(e => e.Max.Y), boxes.Max(e => e.Max.Z));
        return new OrientedGoalMetrics(model.Bodies.Count, new ExactEnvelope3(min, max), penalty, model.Bodies.Sum(b => b.Teeth));
    }

    /// <summary>Additional goal constraints follow authoritative 19A validation; no goal channel is ever inserted into the solution.</summary>
    public static OrientedValidation ValidateContext(OrientedGoalPlan plan, OrientedGoalTuple tuple, OrientedTwoOutputMechanism model)
    {
        var request = Lower(plan, tuple); var original = OrientedTwoOutputValidator.ValidateContext(request, model); var checks = original.Checks.ToList();
        void Check(string subject, bool pass) => checks.Add(new OrientedDomainCheck("goal-context", subject,
            pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Declared fixed functional and whole-mechanism constraint."));
        if (!original.IsValid) return original;
        var metrics = Measure(model, Preference(plan, tuple)); var limits = plan.Goal.Limits;
        Check("whole-count-limits", model.Bodies.Count <= limits.MaxBodies && model.Shafts.Count <= limits.MaxShafts && metrics.TotalTeeth <= limits.MaxTotalTeeth);
        if (limits.WholeBounds.HasValue)
        {
            var e = metrics.Envelope; var b = limits.WholeBounds.Value;
            Check("whole-bounds", e.Min.X >= b.Min.X && e.Min.Y >= b.Min.Y && e.Min.Z >= b.Min.Z && e.Max.X <= b.Max.X && e.Max.Y <= b.Max.Y && e.Max.Z <= b.Max.Z);
        }
        foreach (var target in plan.Goal.Outputs)
        {
            var binding = model.Outputs.Single(x => x.Key == target.Key); var body = model.Bodies.Single(x => x.Id == binding.BodyId);
            Check("body-center/" + target.Key, !target.FixedBodyCenter.HasValue || body.MountingFrame.Origin == target.FixedBodyCenter.Value);
            Check("body-teeth/" + target.Key, !target.FixedBodyTeeth.HasValue || body.Teeth == target.FixedBodyTeeth.Value);
        }
        return new OrientedValidation(checks, original.Diagnostics, original.PitchProofs, original.ClearancePolicy);
    }

    public static OrientedGoalTupleInspection Inspect(OrientedGoalPlan plan, OrientedGoalTuple tuple)
    {
        var request = Lower(plan, tuple); var mismatch = ProvenTargetMismatch(request);
        if (mismatch is not null) return new OrientedGoalTupleInspection(request, null, new OrientedValidation(new[] {
            new OrientedDomainCheck("goal-prune", "exact-target", OrientedCheckVerdict.Fail, true, mismatch) },
            clearancePolicy: OrientedTwoOutputProfile.ClearancePolicy(request.Profile)), mismatch);
        var result = OrientedTwoOutputComposer.Compose(request);
        var validation = result.IsSuccess ? ValidateContext(plan, tuple, result.Mechanism!) : result.Validation;
        return new OrientedGoalTupleInspection(request, result, validation, null);
    }

    public static IComparer<OrientedGoalCandidate> CandidateComparer { get; } = Comparer<OrientedGoalCandidate>.Create((a, b) =>
    {
        var x = a.Metrics; var y = b.Metrics;
        var c = x.Bodies.CompareTo(y.Bodies); if (c != 0) return c;
        c = x.Volume.CompareTo(y.Volume); if (c != 0) return c;
        c = x.MaximumExtent.CompareTo(y.MaximumExtent); if (c != 0) return c;
        c = x.PreferencePenalty.CompareTo(y.PreferencePenalty); if (c != 0) return c;
        c = x.TotalTeeth.CompareTo(y.TotalTeeth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.CandidateId, b.CandidateId);
    });

    public static OrientedGoalGeneration Generate(OrientedGoalPlan plan,
        Func<OrientedGoalPlan, OrientedGoalTuple, OrientedTwoOutputCompositionRequest, OrientedTwoOutputMechanism, OrientedGoalArtifact> writeArtifact,
        CancellationToken token = default, Action<int, int>? progress = null)
    {
        if (plan is null || writeArtifact is null) throw new ArgumentNullException(plan is null ? nameof(plan) : nameof(writeArtifact));
        var options = plan.Goal.Options; var retained = new Dictionary<string, OrientedGoalCandidate>(StringComparer.Ordinal);
        var details = new List<OrientedGoalTupleDetail>(); var digest = OrientedGoalKeys.HashText("oriented-goal-transcript-v1");
        var processed = 0; var accepted = 0; var rejected = 0; var inconclusive = 0; var unretained = 0; var unique = 0; var originCount = 0; var retainedBytes = 0;
        var status = OrientedGoalStatus.Complete; var reason = "declared-domain-exhausted";
        var refined = plan.Goal.Profile == OrientedGoalProfile.RefinedId; var pairPredicates = 0;
        OrientedGoalTupleInspection? inspection = null;
        bool ReportProgress()
        {
            try { progress?.Invoke(processed, (int)plan.TupleCount); return true; }
            catch (Exception e) when (Recoverable(e)) { status = OrientedGoalStatus.Failed; reason = "progress-observer-failed: " + e.GetType().Name; return false; }
        }
        void Record(OrientedGoalTuple tuple, OrientedTupleVerdict verdict, string why, string? candidate = null)
        {
            var proofs = inspection?.Validation.PitchProofs ?? Enumerable.Empty<PitchPairProof>();
            var d = new OrientedGoalTupleDetail(tuple, verdict, why, candidate, refined ? proofs.Count() : 0,
                refined ? PitchProofKeys.CoverageDigest(proofs) : null);
            digest = ExtendTranscript(digest, d);
            if (details.Count < options.MaxDetails) details.Add(d);
        }
        foreach (var tuple in OrientedGoalCompiler.Enumerate(plan))
        {
            if (token.IsCancellationRequested) { status = OrientedGoalStatus.Cancelled; reason = "cancelled-at-tuple-boundary"; break; }
            if (processed >= options.WorkBudget) { status = OrientedGoalStatus.IncompleteBudget; reason = "work-budget"; break; }
            // Charge before any lowering/filter/composition. A tuple is an atomic observation: cancellation is only between tuples.
            processed++;
            inspection = null;
            try
            {
                inspection = Inspect(plan, tuple); var request = inspection.Request; var mismatch = inspection.FilterReason;
                pairPredicates += inspection.Validation.PitchProofs.Count;
                if (mismatch is not null)
                {
                    rejected++; Record(tuple, OrientedTupleVerdict.Rejected, mismatch); if (!ReportProgress()) break; continue;
                }
                var result = inspection.Composition!; var validation = inspection.Validation;
                if (!result.IsSuccess || !validation.IsValid)
                {
                    var failed = validation.Checks.Where(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass).ToArray();
                    var uncertain = failed.Any(c => c.Verdict == OrientedCheckVerdict.Inconclusive) && !failed.Any(c => c.Verdict == OrientedCheckVerdict.Fail);
                    var why = string.Join(";", failed.Select(c => c.Domain + "/" + c.Subject + ":" + c.Verdict));
                    if (uncertain) inconclusive++; else rejected++;
                    Record(tuple, uncertain ? OrientedTupleVerdict.Inconclusive : OrientedTupleVerdict.Rejected, why);
                }
                else
                {
                    var artifact = writeArtifact(plan, tuple, request, result.Mechanism!);
                    if (!OrientedGoalKeys.IsHash(artifact.CandidateId) || !OrientedGoalKeys.IsHash(artifact.ArtifactHash) ||
                        OrientedGoalKeys.Hash(artifact.MechanicalPayload) != artifact.CandidateId) throw new InvalidOperationException("Artifact writer supplied an inconsistent mechanical identity.");
                    retained.TryGetValue(artifact.CandidateId, out var prior);
                    if (prior is not null && !prior.Artifact.MechanicalPayload.SequenceEqual(artifact.MechanicalPayload)) throw new InvalidOperationException("Same candidate ID has different canonical mechanical payload.");
                    accepted++; if (prior is null) unique++;
                    var penalty = Preference(plan, tuple); var origin = new OrientedGoalOrigin(OrientedGoalKeys.Origin(plan, tuple), tuple, artifact.ArtifactHash, penalty);
                    var bytes = prior is null ? artifact.ByteLength : 0;
                    if (unique > options.MaxUniqueCandidates || retainedBytes + bytes > options.MaxRetainedBytes || originCount + 1 > options.MaxOrigins)
                    {
                        unretained++; status = OrientedGoalStatus.IncompleteResource; reason = unique > options.MaxUniqueCandidates ? "unique-candidate-retention" : originCount + 1 > options.MaxOrigins ? "origin-retention" : "artifact-byte-retention";
                        Record(tuple, OrientedTupleVerdict.Unretained, reason, artifact.CandidateId); break;
                    }
                    retainedBytes += bytes; originCount++;
                    var origins = (prior?.Origins ?? Enumerable.Empty<OrientedGoalOrigin>()).Append(origin).ToArray();
                    var representative = origins.OrderBy(x => x.PreferencePenalty).ThenBy(x => x.Id, StringComparer.Ordinal).First();
                    retained[artifact.CandidateId] = new OrientedGoalCandidate(representative.Id == origin.Id ? artifact : prior!.Artifact,
                        Measure(result.Mechanism!, representative.PreferencePenalty), origins, representative.Id);
                    Record(tuple, OrientedTupleVerdict.Accepted, "validated", artifact.CandidateId);
                }
            }
            catch (Exception e) when (Recoverable(e))
            {
                status = OrientedGoalStatus.Failed; reason = "tuple-operation-failed: " + e.GetType().Name;
                Record(tuple, OrientedTupleVerdict.Unretained, reason); break;
            }
            if (!ReportProgress()) break;
        }
        var complete = processed == plan.TupleCount && status == OrientedGoalStatus.Complete;
        if (complete && accepted == 0) status = inconclusive == 0 ? OrientedGoalStatus.Infeasible : OrientedGoalStatus.NoValidatedCandidate;
        var sorted = retained.Values.OrderBy(x => x, CandidateComparer).ToArray();
        var guarantee = unretained != 0 || status == OrientedGoalStatus.Failed || sorted.Length == 0 ? OrientedRankingGuarantee.None :
            complete ? OrientedRankingGuarantee.CompleteDeclaredDomain : OrientedRankingGuarantee.BestAmongExplored;
        return new OrientedGoalGeneration(plan.GoalId, status, plan.TupleCount, processed, accepted, rejected, inconclusive, unretained, unique, retained.Count, originCount,
            complete, sorted.Length > options.ReturnedCandidateCap, guarantee, reason, digest, sorted.Take(options.ReturnedCandidateCap), details, plan.Goal.Profile, pairPredicates);
    }

    private static bool Recoverable(Exception e) => e is not OutOfMemoryException && e is not StackOverflowException && e is not AccessViolationException;

    public static string ExtendTranscript(string digest, OrientedGoalTupleDetail d)
    {
        var legacy = OrientedGoalKeys.Pack(digest, OrientedGoalKeys.N(d.Tuple.Ordinal), OrientedGoalKeys.Tuple(d.Tuple), d.Verdict.ToString(), d.Reason, d.CandidateId ?? "none");
        return OrientedGoalKeys.HashText(d.ClearanceDigest is null ? legacy : OrientedGoalKeys.Pack(legacy, PitchClearancePolicy.Refined, OrientedGoalKeys.N(d.PairPredicateCount), d.ClearanceDigest));
    }
}
