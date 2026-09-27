using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class OrientedTwoOutputComposer
{
    public static OrientedTwoOutputResult Compose(OrientedTwoOutputCompositionRequest request, CancellationToken token = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var policy = request.Profile == OrientedTwoOutputProfile.RefinedId ? PitchClearancePolicy.Refined : PitchClearancePolicy.Legacy;
        OrientedTwoOutputResult Fail(OrientedOperationStatus status, string subject, string detail) => Failure(status, subject, detail, policy);
        // Pre-graph failures carry the requested policy but no fabricated pair coverage or clearance verdict.
        OrientedTwoOutputResult SourceFailure(OrientedOperationStatus status, OrientedValidation v) =>
            new(status, null, new OrientedValidation(v.Checks, v.Diagnostics, clearancePolicy: policy));
        if (token.IsCancellationRequested) return Fail(OrientedOperationStatus.Cancelled, "cancelled", "Cancelled before realization.");
        if (!OrientedTwoOutputProfile.IsSupported(request.Profile) || !request.AssemblyPose.IsProperCardinal)
            return Fail(OrientedOperationStatus.Unsupported, "profile", "Only the bounded cardinal two-output profile is supported.");
        if (request.ParallelBranch is null || !ValidOutputRequests(request.Outputs) || request.KeepOuts.Count > 16 ||
            !OrientedMechanismValidator.Unique(request.KeepOuts.Select(k => k.Id)) || request.KeepOuts.Any(k => !k.Envelope.IsOrdered))
            return Fail(OrientedOperationStatus.InvalidInput, "request", "One parallel source, two unique keyed/typed outputs and bounded keep-outs are required.");
        // Realize only the physical pair here. No source is passed through the 18A serial upstream slot.
        var pairResult = OrientedTransmissionComposer.CreatePair(request.Bevel, token);
        if (!pairResult.IsSuccess) return SourceFailure(pairResult.Status, pairResult.Validation);
        var pair = pairResult.Mechanism!;
        var shafts = pair.Shafts.ToList(); var bodies = pair.Bodies.ToList(); var contacts = pair.Contacts.ToList(); var ports = pair.Ports.ToList();
        var links = new List<ShaftPortConnection>(); var maps = new List<SourceShaftMapping>(); var sources = new List<OrientedSourceIdentity>();
        foreach (var entry in new[] { (Key: "parallel", Source: request.ParallelBranch, Mount: request.Bevel.Input), (Key: "turned", Source: request.TurnedBranch, Mount: request.Bevel.Output) })
        {
            if (entry.Source is null) continue;
            var issue = OrientedTransmissionComposer.ValidateSource(entry.Source);
            if (issue is not null) return SourceFailure(issue.Status, issue.Validation);
            // Stable source namespace must not collide with explicit retained shaft/port IDs.
            if (entry.Source.Source.Kinematic.Dofs.Any(d => d.Id != entry.Source.InputDofId && shafts.Any(s => s.Id == entry.Key + "/" + d.Id)) ||
                ports.Any(p => p.Id == entry.Key + "/" + entry.Source.ConnectionPort.Id))
                return Fail(OrientedOperationStatus.InvalidInput, "identifiers", "Source namespace collides with retained identifiers.");
            issue = OrientedTransmissionComposer.LiftSource(entry.Source, entry.Key, entry.Source.InputDofId, entry.Mount, shafts, bodies, contacts, ports, links, maps, sources, true);
            if (issue is not null) return SourceFailure(issue.Status, issue.Validation);
        }
        var outputs = new List<OrientedOutputBinding>();
        foreach (var o in request.Outputs)
        {
            var source = o.Role == OrientedOutputRole.ParallelBranch ? request.ParallelBranch : request.TurnedBranch;
            var prefix = o.Role == OrientedOutputRole.ParallelBranch ? "parallel/" : source is null ? "" : "turned/";
            var expectedShaft = source?.OutputDofId ?? request.Bevel.Output.Shaft.Id;
            var expectedBody = source?.Source.Spatial.Bodies.Single(b => b.DofId == expectedShaft).Id ?? "bevel/wheel";
            if (o.TerminalPort.ShaftId != expectedShaft || o.TerminalBodyId != expectedBody)
                return Fail(OrientedOperationStatus.ConstraintFailure, o.Key + "/terminal", "Output must bind its explicit branch terminal body/shaft, never its driver or another role.");
            var port = new ShaftPort(prefix + o.TerminalPort.Id, prefix + expectedShaft, o.TerminalPort.Frame, o.TerminalPort.PhaseOffset, o.TerminalPort.Kind);
            var shaft = shafts.Single(s => s.Id == port.ShaftId);
            if (!OrientedTransmissionComposer.PortOnShaft(port, shaft))
                return Fail(OrientedOperationStatus.ConstraintFailure, o.Key + "/port", "Terminal port is not an aligned zero-phase station on its shaft.");
            var existing = ports.SingleOrDefault(p => p.Id == port.Id);
            if (existing is not null && (existing.ShaftId != port.ShaftId || !OrientedTransmissionComposer.SameFrame(existing.Frame, port.Frame) || existing.PhaseOffset != port.PhaseOffset || existing.Kind != port.Kind))
                return Fail(OrientedOperationStatus.InvalidInput, o.Key + "/port-id", "A reused port ID must identify the same explicit port.");
            if (existing is null) ports.Add(port);
            outputs.Add(new OrientedOutputBinding(o.Key, o.Role, port.ShaftId, prefix + o.TerminalBodyId, port.Id));
        }
        if (token.IsCancellationRequested) return Fail(OrientedOperationStatus.Cancelled, "cancelled", "Cancelled before graph solve.");
        var root = request.Bevel.Input.Shaft.Id; var pose = request.AssemblyPose;
        shafts = shafts.Select(s => new OrientedShaft(s.Id, pose.Transform(s.Frame), s.Id == root)).ToList();
        bodies = bodies.Select(b => new OrientedGearBody(b.Id, b.ShaftId, b.Kind, pose.Transform(b.MountingFrame), b.Teeth, b.OuterPitchRadius, b.SourceModuleId)).ToList();
        ports = ports.Select(p => new ShaftPort(p.Id, p.ShaftId, pose.Transform(p.Frame), p.PhaseOffset, p.Kind)).ToList();
        contacts = contacts.Select(c => new OrientedGearContact(c.Id, c.Kind, c.BodyAId, c.BodyBId, c.StoredTransfer, c.Cone is null ? null :
            new RightAnglePitchCone(pose.Point(c.Cone.Apex), pose.Vector(c.Cone.OutwardA), pose.Vector(c.Cone.OutwardB), pose.Point(c.Cone.OuterContact), c.Cone.InnerParameter, c.Cone.OuterScaleA, c.Cone.OuterScaleB))).ToList();
        var edges = contacts.Select(c => new ScalarAffineCoupling(c.Id, bodies.Single(b => b.Id == c.BodyAId).ShaftId, bodies.Single(b => b.Id == c.BodyBId).ShaftId, c.StoredTransfer));
        var solved = KinematicSolver.SolveAffine(root, shafts.Select(s => new RotationalDof(s.Id, s.IsPrescribed)), edges);
        if (!solved.IsValid) return new OrientedTwoOutputResult(OrientedOperationStatus.ConstraintFailure, null, new OrientedValidation(Array.Empty<OrientedDomainCheck>(), solved.Diagnostics, clearancePolicy: policy));
        var model = new OrientedTwoOutputMechanism(root, outputs, shafts, bodies, contacts, ports, links, maps, sources, solved.Solution!, request.RequireCrossComponentClearance, request.KeepOuts, request.Profile);
        var validation = OrientedTwoOutputValidator.ValidateContext(request, model);
        return new OrientedTwoOutputResult(validation.IsValid ? OrientedOperationStatus.Complete : OrientedOperationStatus.ConstraintFailure, validation.IsValid ? model : null, validation);
    }

    internal static bool ValidOutputRequests(IEnumerable<OrientedOutputRequest> outputs)
    {
        var all = outputs.ToArray();
        return all.Length == 2 && OrientedMechanismValidator.Unique(all.Select(o => o.Key)) &&
            all.Count(o => o.Role == OrientedOutputRole.ParallelBranch) == 1 && all.Count(o => o.Role == OrientedOutputRole.TurnedBranch) == 1;
    }
    internal static OrientedTwoOutputResult Failure(OrientedOperationStatus status, string subject, string detail, string policy = PitchClearancePolicy.Legacy) => new(status, null,
        new OrientedValidation(new[] { new OrientedDomainCheck("request", subject, OrientedCheckVerdict.Fail, true, detail) },
            new[] { new Diagnostic("ORIENTED_TWO_OUTPUT_" + status.ToString().ToUpperInvariant(), DiagnosticSeverity.Error, detail, subject) }, clearancePolicy: policy));

    public static OrientedTwoOutputEvaluation Evaluate(OrientedTwoOutputMechanism model, Rational rootTurns)
    {
        if (!OrientedTwoOutputValidator.Validate(model).IsValid) throw new ArgumentException("Invalid oriented two-output mechanism.");
        return new OrientedTwoOutputEvaluation(rootTurns, model.Shafts.Select(s => { model.Solution.TryGetState(s.Id, out var state); return new OrientedShaftEvaluation(s.Id, rootTurns * state!.Coefficient, s.Frame.Z, state.Coefficient); }),
            model.Outputs.Select(o => { model.Solution.TryGetState(o.ShaftId, out var state); return new OrientedOutputEvaluation(o, model.Shafts.Single(s => s.Id == o.ShaftId), model.Ports.Single(p => p.Id == o.PortId), state!.Coefficient, rootTurns); }));
    }
}
