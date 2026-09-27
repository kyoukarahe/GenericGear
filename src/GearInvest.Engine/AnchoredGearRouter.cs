using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed partial class AnchoredGearRouter
{
    public GearRoutingResult Generate(AnchoredGearRoutingRequest request, Func<GenerationCandidate, string> candidateIdFactory,
        CancellationToken cancellationToken = default, Action<int, int>? progress = null)
        => GenerateObserved(request, candidateIdFactory, cancellationToken, progress, null);

    // Observe every whole-validated candidate before the unchanged specialized top-K collector.
    internal GearRoutingResult GenerateObserved(AnchoredGearRoutingRequest request, Func<GenerationCandidate, string> candidateIdFactory,
        CancellationToken cancellationToken, Action<int, int>? progress, Action<GearRouteCandidate>? candidateSink)
    {
        if (candidateIdFactory == null) throw new ArgumentNullException(nameof(candidateIdFactory));
        var normalized = Normalize(request); var candidates = new List<GearRouteCandidate>(); var details = new List<Diagnostic>();
        var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        int nodeChecks = 0, staticRejected = 0, pairChecks = 0, edges = 0, expansions = 0, rejectedSuccessors = 0, goals = 0, valid = 0, omitted = 0;
        bool workRemaining = false;
        void Reject(string code, string subject, string detail)
        {
            rejected.TryGetValue(code, out int count); rejected[code] = count + 1;
            if (details.Count < GearRoutingContract.MaxDetails) details.Add(Error(code, subject, detail)); else omitted++;
        }
        GearRoutingResult Finish(GearRoutingStatus status) => new GearRoutingResult(normalized, status,
            new GearRoutingSearchSummary(normalized.PotentialNodes, nodeChecks, staticRejected, pairChecks, edges, expansions, rejectedSuccessors, goals, valid, workRemaining),
            candidates, rejected.Select(p => new GearRoutingRejection(p.Key, p.Value)), details, omitted);
        if (!normalized.IsValid) { details.AddRange(normalized.Diagnostics); return Finish(normalized.Status); }
        var r = normalized.Request!;
        if (cancellationToken.IsCancellationRequested) { workRemaining = true; return Finish(GearRoutingStatus.Cancelled); }
        var magnitude = r.TargetTransfer.Sign < 0 ? -r.TargetTransfer : r.TargetTransfer;
        if (magnitude != new Rational(r.Input.Teeth, r.Output.Teeth))
        {
            Reject("MAGNITUDE_INVARIANT", "targetTransfer", "Simple idlers cancel: required absolute transfer is " + new Rational(r.Input.Teeth, r.Output.Teeth) + ". Endpoints are not changed.");
            return Finish(GearRoutingStatus.Infeasible);
        }
        if (!Enumerable.Range(r.MinIdlers, r.MaxIdlerCount - r.MinIdlers + 1).Any(k => ((k + 1) % 2 == 0 ? 1 : -1) == r.TargetTransfer.Sign))
        { Reject("PARITY_INVARIANT", "idlerCount", "No permitted idler count has the requested external-mesh sign."); return Finish(GearRoutingStatus.Infeasible); }

        // No physical path, expected witness, cached candidate, or preference is supplied to graph preparation.
        var graph = FiniteGearContactGraph.Prepare(r, cancellationToken, (node, issue) => {
            nodeChecks++;
            if (issue != null) { staticRejected++; Reject(issue, "site:" + node.Position + ":" + node.Teeth.ToString(System.Globalization.CultureInfo.InvariantCulture), "Whole pitch disk is unavailable in the fixed environment."); }
        }, mesh => { pairChecks++; if (mesh) edges++; });
        if (graph == null) { workRemaining = true; return Finish(GearRoutingStatus.Cancelled); }
        if (!graph.EndpointsAvailable) { Reject("ENDPOINT_ENVIRONMENT", "anchors", "A fixed endpoint envelope is unavailable; no anchor movement is permitted."); return Finish(GearRoutingStatus.Infeasible); }
        var nodes = graph.Nodes; var adjacency = graph.Adjacency; int output = graph.Output;
        var path = new List<int> { 0 }; bool stopped = false; var generation = new GenerationEngine();
        void Search()
        {
            foreach (int successor in adjacency[path[path.Count - 1]])
            {
                if (stopped) return;
                if (cancellationToken.IsCancellationRequested || expansions == r.ExpansionBudget) { workRemaining = true; stopped = true; return; }
                expansions++; progress?.Invoke(expansions, r.ExpansionBudget);
                var next = nodes[successor]; string? issue = graph.SuccessorIssue(path, successor);
                if (issue != null) { rejectedSuccessors++; Reject(issue, "path:" + GearRoutingContract.List(path.Select(i => nodes[i].Canonical)), "Rejected successor " + next.Position + "."); continue; }
                path.Add(successor);
                if (successor == output)
                {
                    goals++; int k = path.Count - 2;
                    if (k < r.MinIdlers || ((k + 1) % 2 == 0 ? 1 : -1) != r.TargetTransfer.Sign)
                    { rejectedSuccessors++; Reject("GOAL_PARITY_OR_DEPTH", "output", "Connected path does not meet minimum idlers or the signed target."); }
                    else
                    {
                        var assignment = path.Select(i => nodes[i]).ToArray();
                        if (r.RequiredRegions.Any(region => !assignment.Skip(1).Take(k).Any(a => GearRoutingGeometry.Contains(region.Bounds, a.Position))))
                        { rejectedSuccessors++; Reject("REQUIRED_REGION", "output", "Connected path does not visit every required intermediate-center region."); }
                        else
                        {
                            var built = generation.Generate(Build(assignment, r.PitchRadiusTicksPerTooth));
                            if (!built.IsSuccess) { rejectedSuccessors++; Reject("GENERIC_VALIDATION", "mechanism", string.Join("|", built.Diagnostics.Select(d => d.Code))); }
                            else
                            {
                                var mechanism = built.Candidates.Single(); var id = candidateIdFactory(mechanism);
                                var validation = GearRouteValidator.Validate(r, assignment, mechanism, id);
                                if (!validation.IsValid) { rejectedSuccessors++; Reject("CONTEXT_VALIDATION", id, string.Join("|", validation.Diagnostics.Select(d => d.Code))); }
                                else
                                {
                                    valid++; var candidate = new GearRouteCandidate(id, assignment, mechanism, GearRouteValidator.Metrics(r, assignment), validation);
                                    candidateSink?.Invoke(candidate); candidates.Add(candidate); candidates.Sort(GearRouteValidator.Compare);
                                    // Unique normalized node paths map injectively to role-labelled mechanical payloads.
                                    // Retain only top K, but enumerate the entire declared path domain unless cancelled/budgeted.
                                    if (candidates.Count > r.MaximumReturned) candidates.RemoveAt(candidates.Count - 1);
                                }
                            }
                        }
                    }
                }
                else Search(); // Output is always terminal; it can never become an intermediate axis.
                path.RemoveAt(path.Count - 1);
            }
        }
        Search();
        if (cancellationToken.IsCancellationRequested) { workRemaining = true; return Finish(GearRoutingStatus.Cancelled); }
        if (workRemaining) return Finish(GearRoutingStatus.IncompleteBudget);
        if (valid == 0) Reject("NO_ROUTE", "domain", "No approved route in the complete declared finite sites/teeth/depth/layer domain.");
        return Finish(valid == 0 ? GearRoutingStatus.Infeasible : GearRoutingStatus.Complete);
    }

    private static LowLevelMechanicalSpecification Build(IReadOnlyList<GearRouteAssignment> path, BigInteger scale)
    {
        int n = path.Count; var dofs = new List<RotationalDof>(); var axes = new List<SpatialAxis>(); var bodies = new List<SpatialBody>();
        var couplings = new List<ExternalGearCoupling>(); var contacts = new List<SpatialContact>();
        for (int i = 0; i < n; i++)
        {
            var a = path[i]; dofs.Add(new RotationalDof(GearRouteValidator.Dof(i, n), i == 0)); axes.Add(new SpatialAxis(GearRouteValidator.Axis(i, n), a.Position.X, a.Position.Y));
            bodies.Add(new SpatialBody(GearRouteValidator.Body(i, n), SpatialBodyKind.Gear, GearRouteValidator.Axis(i, n), GearRouteValidator.Dof(i, n), 0, a.Teeth, a.Teeth * scale));
            if (i == n - 1) continue;
            couplings.Add(new ExternalGearCoupling(GearRouteValidator.Mesh(i), GearRouteValidator.Dof(i, n), GearRouteValidator.Dof(i + 1, n), a.Teeth, path[i + 1].Teeth));
            contacts.Add(new SpatialContact(GearRouteValidator.Contact(i), SpatialContactKind.ExternalGearMesh, GearRouteValidator.Mesh(i), GearRouteValidator.Body(i, n), GearRouteValidator.Body(i + 1, n)));
        }
        return new LowLevelMechanicalSpecification(GearRoutingContract.Profile, new KinematicSpecification(GearRouteValidator.Dof(0, n), dofs, couplings), new SpatialMechanism(axes, bodies, contacts));
    }
}
