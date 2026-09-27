using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Bounded whole reanalysis; historic solutions, source proofs and draft caches are never inputs.</summary>
public static class MechanicalAnalyzer
{
    public static MechanicalAnalysis Analyze(MechanicalDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var shafts = d.Shafts.ToDictionary(s => s.Id, StringComparer.Ordinal); var bodies = d.Bodies.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var ports = d.Ports.ToDictionary(p => p.Id, StringComparer.Ordinal); var edges = new List<MechanicalAnalyzedEdge>(); var diagnostics = new List<MechanicalDiagnostic>();
        var geometryChecks = new List<OrientedDomainCheck>(); var proofs = new List<PitchPairProof>(); var referencesValid = true;
        void Missing(params MechanicalReference[] refs) { referencesValid = false; diagnostics.Add(new MechanicalDiagnostic("MissingEndpoint", "References", related: refs)); }
        void GeometricCheck(string domain, string id, bool valid) => geometryChecks.Add(new OrientedDomainCheck(domain, id, valid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Current bounded exact mechanical definition."));
        if (d.RootShaftId is not null && !shafts.ContainsKey(d.RootShaftId)) Missing(new MechanicalReference("InputShaft", d.RootShaftId));
        foreach (var s in d.Shafts) GeometricCheck("frame", "shaft/" + s.Id, s.Frame.IsProperCardinal);
        foreach (var b in d.Bodies)
        {
            GeometricCheck("frame", "body/" + b.Id, b.MountingFrame.IsProperCardinal);
            if (!shafts.TryGetValue(b.ShaftId, out var s)) Missing(new MechanicalReference("Body", b.Id), new MechanicalReference("Shaft", b.ShaftId));
            else GeometricCheck("mounting", b.Id, MechanicalConnectionPredicates.BodyMounting(b, s, OrientedTransmissionProfile.MaxTeeth));
        }
        foreach (var p in d.Ports)
        {
            GeometricCheck("frame", "port/" + p.Id, p.Frame.IsProperCardinal);
            if (!shafts.TryGetValue(p.ShaftId, out var s)) Missing(new MechanicalReference("Port", p.Id), new MechanicalReference("Shaft", p.ShaftId));
            else GeometricCheck("port", p.Id, MechanicalConnectionPredicates.PortMounting(p, s));
        }
        foreach (var c in d.Contacts)
        {
            var local = MechanicalConnectionQuery.QueryContact(d, c); diagnostics.AddRange(local.Diagnostics); geometryChecks.AddRange(local.Checks);
            var a = bodies.TryGetValue(c.BodyAId, out var ba) && shafts.ContainsKey(ba.ShaftId) ? ba.ShaftId : null;
            var b = bodies.TryGetValue(c.BodyBId, out var bb) && shafts.ContainsKey(bb.ShaftId) ? bb.ShaftId : null;
            if (a is null || b is null) referencesValid = false;
            edges.Add(new MechanicalAnalyzedEdge("Contact", c.Id, a, b, local));
        }
        foreach (var c in d.Connections)
        {
            var local = MechanicalConnectionQuery.QueryPortConnection(d, c); diagnostics.AddRange(local.Diagnostics); geometryChecks.AddRange(local.Checks);
            var a = ports.TryGetValue(c.PortAId, out var pa) && shafts.ContainsKey(pa.ShaftId) ? pa.ShaftId : null;
            var b = ports.TryGetValue(c.PortBId, out var pb) && shafts.ContainsKey(pb.ShaftId) ? pb.ShaftId : null;
            if (a is null || b is null) referencesValid = false;
            edges.Add(new MechanicalAnalyzedEdge("Connection", c.Id, a, b, local));
        }
        var declaredGroups = Groups(shafts.Keys, edges.Where(e => e.IsDeclaredResolvable));
        var selected = d.RootShaftId is not null && shafts.ContainsKey(d.RootShaftId) ? d.RootShaftId : null;
        var lowered = LowerAdmitted(d, edges);
        var affine = AffineComponentAnalyzer.Analyze(selected, d.Shafts.Select(s => new RotationalDof(s.Id, s.IsPrescribed)), lowered);
        MechanicalConnectivityComponent Component(IEnumerable<string> ids, bool admitted, AffineComponentAnalysis? relation = null)
        {
            var members = new HashSet<string>(ids, StringComparer.Ordinal);
            return new MechanicalConnectivityComponent(members, d.Bodies.Where(b => members.Contains(b.ShaftId)).Select(b => b.Id), d.Ports.Where(p => members.Contains(p.ShaftId)).Select(p => p.Id),
                edges.Where(e => (admitted ? e.IsAdmitted : e.IsDeclaredResolvable) && members.Contains(e.ShaftAId!)).Select(e => e.ConstraintKey),
                d.Shafts.Where(s => s.IsPrescribed && members.Contains(s.Id)).Select(s => s.Id), d.Outputs.Where(o => o.ShaftId is not null && members.Contains(o.ShaftId)).Select(o => o.Key), selected is not null && members.Contains(selected), relation,
                edges.Where(e => !e.IsAdmitted && (e.ShaftAId is not null && members.Contains(e.ShaftAId) || e.ShaftBId is not null && members.Contains(e.ShaftBId))).Select(e => e.ConstraintKey));
        }
        var declared = declaredGroups.Select(ids => Component(ids, false)).ToArray(); var admitted = affine.Select(c => Component(c.MemberIds, true, c)).ToArray();
        foreach (var component in admitted)
            foreach (var issue in component.Affine!.Diagnostics)
                diagnostics.Add(WithOutputs(issue, component.OutputKeys, edges));
        var outputs = new List<MechanicalOutputAnalysis>();
        foreach (var output in d.Outputs)
        {
            var issues = new List<MechanicalDiagnostic>(); var declaredPath = Array.Empty<string>(); var admittedPath = Array.Empty<string>(); var blocked = Array.Empty<string>();
            var declaredReachable = false; var admittedReachable = false; ExactAffineRelation? shaftRelation = null, portRelation = null; ExactVector3? axis = null; OrientedFrame? frame = null;
            var status = MechanicalDeterminacy.BlockedByInvalidConstraint; var target = MechanicalAxisVerdict.NotAssessed;
            var refs = new List<MechanicalReference> { new("Output", output.Key) };
            if (!output.IsResolved || !shafts.TryGetValue(output.ShaftId!, out var shaft) || !bodies.TryGetValue(output.BodyId!, out var body) ||
                !ports.TryGetValue(output.PortId!, out var port) || body.ShaftId != output.ShaftId || port.ShaftId != output.ShaftId)
            {
                referencesValid = false; refs.AddRange(output.FormerEndpoint);
                if (output.ShaftId is not null) refs.Add(new MechanicalReference("Shaft", output.ShaftId));
                if (output.BodyId is not null) refs.Add(new MechanicalReference("Body", output.BodyId));
                if (output.PortId is not null) refs.Add(new MechanicalReference("Port", output.PortId));
                issues.Add(new MechanicalDiagnostic("MissingEndpoint", "OutputBinding", related: refs, affectedOutputs: new[] { output.Key }, scope: "Output"));
            }
            else
            {
                refs.Add(new MechanicalReference("Shaft", shaft.Id)); refs.Add(new MechanicalReference("Body", body.Id)); refs.Add(new MechanicalReference("Port", port.Id));
                axis = shaft.Frame.Z; frame = port.Frame;
                var dc = declared.First(c => c.ShaftIds.Contains(shaft.Id)); var ac = admitted.First(c => c.ShaftIds.Contains(shaft.Id));
                declaredReachable = dc.IsSelectedInputReachable; admittedReachable = ac.IsSelectedInputReachable;
                declaredPath = selected is null ? Array.Empty<string>() : Path(selected, shaft.Id, edges.Where(e => e.IsDeclaredResolvable));
                blocked = declaredPath.Where(key => !edges.Single(e => e.ConstraintKey == key).IsAdmitted).ToArray();
                var relation = ac.Affine!.Relations.Single(r => r.DofId == shaft.Id); admittedPath = relation.ConstraintPath.ToArray(); status = ac.Affine.Determinacy;
                if (declaredReachable && !admittedReachable)
                {
                    status = MechanicalDeterminacy.BlockedByInvalidConstraint;
                    issues.Add(new MechanicalDiagnostic("InvalidPrerequisiteContact", "OutputDeterminacy", related: refs, affectedOutputs: new[] { output.Key },
                        blockedPrerequisites: blocked, scope: "DeclaredConnectivity", complete: false));
                }
                else if (!declaredReachable)
                    issues.Add(new MechanicalDiagnostic("DisconnectedFromInput", "OutputDeterminacy", related: refs, affectedOutputs: new[] { output.Key }, scope: "Output", detail: "Relative component motion is not a zero-speed absolute solution."));
                var endpointAdmitted = shaft.Frame.IsProperCardinal && body.MountingFrame.IsProperCardinal && port.Frame.IsProperCardinal &&
                    MechanicalConnectionPredicates.BodyMounting(body, shaft, OrientedTransmissionProfile.MaxTeeth) && MechanicalConnectionPredicates.PortMounting(port, shaft);
                if (!endpointAdmitted)
                {
                    status = MechanicalDeterminacy.BlockedByInvalidConstraint; issues.Add(new MechanicalDiagnostic(port.PhaseOffset != 0 ? "UnsupportedPhaseDomain" : "InvalidOutputMounting", "OutputBinding", related: refs, affectedOutputs: new[] { output.Key }, scope: "Output"));
                }
                if (status == MechanicalDeterminacy.DeterminedBySelectedInput)
                {
                    shaftRelation = relation.Relation; portRelation = relation.Relation.Then(shaft.Frame.Z.Dot(port.Frame.Z), 0);
                    target = output.RequiredTransfer.HasValue && output.RequiredTransfer.Value != portRelation.Value.Coefficient ? MechanicalAxisVerdict.Fail : MechanicalAxisVerdict.Pass;
                    if (target == MechanicalAxisVerdict.Fail) issues.Add(new MechanicalDiagnostic("TargetMismatch", "OutputRequirement", related: refs,
                        facts: new[] { new MechanicalExactFact("requestedPortTransfer", output.RequiredTransfer, portRelation.Value.Coefficient) }, affectedOutputs: new[] { output.Key }, scope: "Output"));
                }
                else issues.AddRange(ac.Affine.Diagnostics.Select(issue => WithOutputs(issue, new[] { output.Key }, edges)));
            }
            diagnostics.AddRange(issues); outputs.Add(new MechanicalOutputAnalysis(output, declaredReachable, admittedReachable, status, shaftRelation, portRelation, axis, frame, admittedPath, declaredPath, blocked, target, issues));
        }
        AnalyzeClearance(d, shafts, edges, geometryChecks, proofs, diagnostics);
        var geometry = geometryChecks.Any(c => c.Required && c.Verdict == OrientedCheckVerdict.Fail) ? MechanicalAxisVerdict.Fail
            : geometryChecks.Any(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass) ? MechanicalAxisVerdict.Inconclusive : MechanicalAxisVerdict.Pass;
        var constraints = edges.All(e => e.IsAdmitted) && affine.All(c => c.Determinacy != MechanicalDeterminacy.InconsistentConstraints && c.Determinacy != MechanicalDeterminacy.InconsistentWithPrescribedInput && c.Determinacy != MechanicalDeterminacy.UnsupportedConstraintDomain)
            ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
        var targets = outputs.Any(o => o.Target == MechanicalAxisVerdict.Fail) ? MechanicalAxisVerdict.Fail : outputs.Any(o => o.Target != MechanicalAxisVerdict.Pass) ? MechanicalAxisVerdict.Inconclusive : MechanicalAxisVerdict.Pass;
        return new MechanicalAnalysis(draft, edges, declared, admitted, outputs, geometryChecks, proofs, diagnostics,
            referencesValid ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail,
            outputs.All(o => o.IsDeclaredReachable) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail, constraints, geometry, targets);
    }

    /// <summary>Only known motion is emitted. Consumers must label omitted shafts as unknown/reference pose, not zero-speed.</summary>
    public static ReadOnlyCollection<OrientedShaftEvaluation> Evaluate(MechanicalAnalysis analysis, Rational inputTurns)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis)); MechanicalAuthoringProfile.Number(inputTurns);
        var d = analysis.Draft.Definition; var values = new List<OrientedShaftEvaluation>();
        foreach (var component in analysis.AdmittedComponents.Where(c => c.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput))
            foreach (var node in component.Affine!.Relations)
            {
                var shaft = d.Shafts.Single(s => s.Id == node.DofId); if (!shaft.Frame.IsProperCardinal) continue;
                var turns = node.Relation.Evaluate(inputTurns); MechanicalDerivedNumbers.Check(turns);
                values.Add(new OrientedShaftEvaluation(shaft.Id, turns, shaft.Frame.Z, node.Relation.Coefficient));
            }
        return values.OrderBy(v => v.ShaftId, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    private static void AnalyzeClearance(MechanicalDefinition d, IReadOnlyDictionary<string, OrientedShaft> shafts, IReadOnlyList<MechanicalAnalyzedEdge> edges,
        List<OrientedDomainCheck> checks, List<PitchPairProof> proofs, List<MechanicalDiagnostic> diagnostics)
    {
        if (d.CoaxialLayout is not null)
        {
            ParallelCoaxialValidation.Validate(d, edges, checks, diagnostics);
            return;
        }
        if (d.ClearancePolicy == MechanicalAuthoringProfile.PlanarClearance)
        {
            AnalyzePlanarClearance(d, shafts, edges, checks, diagnostics);
            return;
        }
        var shapes = new Dictionary<string, PitchShape>(StringComparer.Ordinal);
        foreach (var body in d.Bodies)
        {
            if (!shafts.TryGetValue(body.ShaftId, out var shaft) || !shaft.Frame.IsProperCardinal || !body.MountingFrame.IsProperCardinal ||
                !MechanicalConnectionPredicates.BodyMounting(body, shaft, OrientedTransmissionProfile.MaxTeeth))
            { Unresolved(body.Id); continue; }
            try { shapes.Add(body.Id, OrientedPitchShapes.Body(d, body)); }
            catch (ArgumentException) { Unresolved(body.Id); }
            catch (InvalidOperationException) { Unresolved(body.Id); }
        }
        void Unresolved(string bodyId)
        {
            checks.Add(new OrientedDomainCheck("pitch-shape", bodyId, OrientedCheckVerdict.Inconclusive, true, "Current body cannot establish its supported pitch inspection shape."));
            diagnostics.Add(new MechanicalDiagnostic("UnresolvedPitchShape", "Clearance", related: new[] { new MechanicalReference("Body", bodyId) }, complete: false));
        }
        void Proof(PitchShape a, PitchShape b, bool required, PitchPairScope scope)
        {
            var proof = PitchClearanceClassifier.Classify(a, b, required, scope); proofs.Add(proof);
            checks.Add(new OrientedDomainCheck("pitch-clearance", proof.PairId, proof.Verdict, required, proof.Method + "/" + proof.Relation));
            if (proof.Verdict != OrientedCheckVerdict.Pass) diagnostics.Add(new MechanicalDiagnostic(proof.Verdict == OrientedCheckVerdict.Fail ? "PitchClearanceIntersection" : "UnresolvedClearance", "Clearance",
                required ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, required, related: new[] { new MechanicalReference("PitchShape", a.Id), new MechanicalReference("PitchShape", b.Id) },
                facts: proof.Terms.Select(t => new MechanicalExactFact(t.Key, null, t.Value)), proofReference: proof.Digest, scope: scope.ToString(), complete: proof.Verdict != OrientedCheckVerdict.Inconclusive));
        }
        var ordered = d.Bodies.ToArray();
        for (var i = 0; i < ordered.Length; i++) for (var j = i + 1; j < ordered.Length; j++)
        {
            var a = ordered[i]; var b = ordered[j]; if (!shapes.TryGetValue(a.Id, out var sa) || !shapes.TryGetValue(b.Id, out var sb)) continue;
            var intended = d.Contacts.Any(c => (c.BodyAId == a.Id && c.BodyBId == b.Id || c.BodyAId == b.Id && c.BodyBId == a.Id) && edges.Single(e => e.Kind == "Contact" && e.Id == c.Id).IsAdmitted);
            if (intended)
            { checks.Add(new OrientedDomainCheck("pitch-clearance", a.Id + "|" + b.Id, OrientedCheckVerdict.Pass, d.RequireCrossComponentClearance, "Intended contact has independently passed shared exact local surface predicates.")); continue; }
            if (d.ClearancePolicy == PitchClearancePolicy.Refined) Proof(sa, sb, d.RequireCrossComponentClearance, PitchPairScope.BodyPair);
            else Legacy(sa, sb, d.RequireCrossComponentClearance, PitchPairScope.BodyPair);
        }
        foreach (var keepOut in d.KeepOuts)
        {
            checks.Add(new OrientedDomainCheck("keep-out", keepOut.Id, keepOut.Envelope.IsOrdered ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "World keep-out stays fixed in current exact coordinates."));
            if (!keepOut.Envelope.IsOrdered) continue;
            var box = PitchShape.Aabb("keep-out/" + keepOut.Id, keepOut.Envelope);
            foreach (var shape in shapes.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
                if (d.ClearancePolicy == PitchClearancePolicy.Refined) Proof(shape, box, true, PitchPairScope.WorldKeepOut); else Legacy(shape, box, true, PitchPairScope.WorldKeepOut);
        }
        void Legacy(PitchShape a, PitchShape b, bool required, PitchPairScope scope)
        {
            var pass = a.Envelope.Disjoint(b.Envelope); var pair = a.Id + "|" + b.Id;
            checks.Add(new OrientedDomainCheck("legacy-pitch-clearance", pair, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Inconclusive, required, "Conservative AABB policy; overlap is inconclusive, not collision."));
            if (!pass) diagnostics.Add(new MechanicalDiagnostic("UnresolvedClearance", "Clearance", required ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, required,
                related: new[] { new MechanicalReference("PitchShape", a.Id), new MechanicalReference("PitchShape", b.Id) }, scope: scope.ToString(), complete: false));
        }
    }
    private static void AnalyzePlanarClearance(MechanicalDefinition d, IReadOnlyDictionary<string, OrientedShaft> shafts,
        IReadOnlyList<MechanicalAnalyzedEdge> edges, List<OrientedDomainCheck> checks, List<MechanicalDiagnostic> diagnostics)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal);
        void Unsupported(string id, params MechanicalReference[] related)
        {
            checks.Add(new OrientedDomainCheck("planar-clearance-domain", id, OrientedCheckVerdict.Inconclusive, true,
                "Only same-plane planar pitch disks and zero clearance are covered; no oriented/keep-out fallback."));
            diagnostics.Add(new MechanicalDiagnostic("UnsupportedPlanarClearanceDomain", "Clearance", related: related, scope: "PlanarSameLayerBodyPair", complete: false));
        }
        foreach (var body in d.Bodies)
        {
            if (body.Kind == OrientedGearKind.PlanarSpur && body.MountingFrame.IsProperCardinal && shafts.TryGetValue(body.ShaftId, out var shaft) &&
                shaft.Frame.IsProperCardinal && MechanicalConnectionPredicates.BodyMounting(body, shaft, OrientedTransmissionProfile.MaxTeeth)) supported.Add(body.Id);
            else Unsupported(body.Id, new MechanicalReference("Body", body.Id));
        }
        foreach (var keepOut in d.KeepOuts) Unsupported("keep-out/" + keepOut.Id, new MechanicalReference("KeepOut", keepOut.Id));
        var bodies = d.Bodies.ToArray();
        for (var i = 0; i < bodies.Length; i++) for (var j = i + 1; j < bodies.Length; j++)
        {
            var a = bodies[i]; var b = bodies[j]; if (!supported.Contains(a.Id) || !supported.Contains(b.Id)) continue;
            var pair = a.Id + "|" + b.Id; var delta = b.MountingFrame.Origin - a.MountingFrame.Origin;
            if (a.MountingFrame.Z.Cross(b.MountingFrame.Z) != default || delta.Dot(a.MountingFrame.Z) != 0)
            { Unsupported(pair, new MechanicalReference("Body", a.Id), new MechanicalReference("Body", b.Id)); continue; }
            var intended = d.Contacts.Any(c => (c.BodyAId == a.Id && c.BodyBId == b.Id || c.BodyAId == b.Id && c.BodyBId == a.Id) &&
                edges.Single(e => e.Kind == "Contact" && e.Id == c.Id).IsAdmitted);
            if (intended)
            {
                checks.Add(new OrientedDomainCheck("planar-nonpenetration", pair, OrientedCheckVerdict.Pass, d.RequireCrossComponentClearance,
                    "Intended external contact has passed the shared exact local geometry predicates.")); continue;
            }
            var distance = delta.LengthSquared; var sum = a.OuterPitchRadius + b.OuterPitchRadius;
            var pass = SpatialValidator.NonPenetratingPitchDisks(distance, a.OuterPitchRadius, b.OuterPitchRadius);
            checks.Add(new OrientedDomainCheck("planar-nonpenetration", pair, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, d.RequireCrossComponentClearance,
                "Shared SpatialValidator predicate: centerDistanceSquared >= radiusSumSquared; tangency is nonpenetrating, not strict closed-set separation."));
            diagnostics.Add(new MechanicalDiagnostic(pass ? "PlanarPitchNonPenetration" : "PlanarPitchPenetration", "Clearance",
                pass ? DiagnosticSeverity.Info : d.RequireCrossComponentClearance ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, d.RequireCrossComponentClearance,
                related: new[] { new MechanicalReference("Body", a.Id), new MechanicalReference("Body", b.Id) },
                facts: new[] { new MechanicalExactFact("centerDistanceSquaredMinimum", sum * sum, distance), new MechanicalExactFact("clearanceTicks", 0, 0) },
                scope: "PlanarSameLayerBodyPair"));
        }
    }
    private static MechanicalDiagnostic WithOutputs(MechanicalDiagnostic issue, IEnumerable<string> outputs, IEnumerable<MechanicalAnalyzedEdge> edges)
    {
        var referenced = new HashSet<string>(issue.Paths.SelectMany(p => p.ConstraintIds), StringComparer.Ordinal);
        var related = issue.Related.Concat(edges.Where(e => referenced.Contains(e.ConstraintKey)).SelectMany(e => e.Compatibility.Involved)).GroupBy(r => r.Key, StringComparer.Ordinal).Select(g => g.First());
        return new MechanicalDiagnostic(issue.Code, issue.Stage, issue.Severity, issue.Required, related, issue.Facts, outputs, issue.Paths, issue.BlockedPrerequisites, issue.ProofReference, issue.Scope, issue.Complete, issue.Detail);
    }
    private static List<string[]> Groups(IEnumerable<string> ids, IEnumerable<MechanicalAnalyzedEdge> edgeSource)
    {
        var remaining = new HashSet<string>(ids, StringComparer.Ordinal); var edges = edgeSource.ToArray(); var groups = new List<string[]>();
        while (remaining.Count > 0)
        {
            var members = new HashSet<string>(StringComparer.Ordinal); var queue = new Queue<string>(); queue.Enqueue(remaining.OrderBy(x => x, StringComparer.Ordinal).First());
            while (queue.Count > 0)
            {
                var id = queue.Dequeue(); if (!members.Add(id)) continue;
                foreach (var e in edges.Where(e => e.ShaftAId == id || e.ShaftBId == id).OrderBy(e => e.ConstraintKey, StringComparer.Ordinal)) queue.Enqueue(e.ShaftAId == id ? e.ShaftBId! : e.ShaftAId!);
            }
            remaining.ExceptWith(members); groups.Add(members.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
        return groups;
    }
    internal static ScalarAffineCoupling[] LowerAdmitted(MechanicalDefinition definition, IEnumerable<MechanicalAnalyzedEdge> edgeSource)
    {
        var shafts = definition.Shafts.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var ports = definition.Ports.ToDictionary(p => p.Id, StringComparer.Ordinal);
        Rational Transfer(MechanicalAnalyzedEdge edge)
        {
            if (edge.Kind == "Contact") return edge.Compatibility.SignedTransfer!.Value;
            var link = definition.Connections.Single(c => c.Id == edge.Id); var a = ports[link.PortAId]; var b = ports[link.PortBId];
            return MechanicalConnectionPredicates.RigidShaftTransfer(shafts[a.ShaftId], a, shafts[b.ShaftId], b, link.CoordinateTransfer);
        }
        return edgeSource.Where(e => e.IsAdmitted).Select(e => new ScalarAffineCoupling(e.ConstraintKey, e.ShaftAId!, e.ShaftBId!, Transfer(e))).ToArray();
    }

    internal static string[] Path(string root, string target, IEnumerable<MechanicalAnalyzedEdge> edgeSource)
    {
        var paths = new Dictionary<string, string[]>(StringComparer.Ordinal) { [root] = Array.Empty<string>() }; var edges = edgeSource.OrderBy(e => e.ConstraintKey, StringComparer.Ordinal).ToArray(); var queue = new Queue<string>(); queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue(); if (id == target) return paths[id];
            foreach (var edge in edges.Where(e => e.ShaftAId == id || e.ShaftBId == id))
            {
                var next = edge.ShaftAId == id ? edge.ShaftBId! : edge.ShaftAId!; if (paths.ContainsKey(next)) continue;
                paths.Add(next, paths[id].Concat(new[] { edge.ConstraintKey }).ToArray()); queue.Enqueue(next);
            }
        }
        return Array.Empty<string>();
    }
}
