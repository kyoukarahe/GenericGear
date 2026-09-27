using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class OrientedTransmissionComposer
{
    public static OrientedTransmissionResult CreatePair(RightAngleBevelRequest request, CancellationToken token = default) =>
        Compose(new OrientedCompositionRequest(request), token);

    public static OrientedTransmissionResult Compose(OrientedCompositionRequest request, CancellationToken token = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (token.IsCancellationRequested) return Failure(OrientedOperationStatus.Cancelled, "cancelled", "Operation cancelled before realization.");
        var pair = request.Bevel;
        if (pair is null || pair.Input is null || pair.Output is null) return Failure(OrientedOperationStatus.InvalidInput, "request", "Pair and both mounts are required.");
        var input = pair.Input; var output = pair.Output;
        if (pair.Profile != OrientedTransmissionProfile.Id || !request.AssemblyPose.IsProperCardinal ||
            !input.Shaft.Frame.IsProperCardinal || !output.Shaft.Frame.IsProperCardinal || !input.Port.Frame.IsProperCardinal || !output.Port.Frame.IsProperCardinal ||
            !input.ConeDirection.IsCardinal || !output.ConeDirection.IsCardinal || input.ConeDirection.Dot(output.ConeDirection) != 0 || input.Shaft.Frame.Z.Dot(output.Shaft.Frame.Z) != 0 ||
            input.Port.PhaseOffset != 0 || output.Port.PhaseOffset != 0 || input.Port.Kind != ShaftConnectionKind.RigidZeroPhase || output.Port.Kind != ShaftConnectionKind.RigidZeroPhase)
            return Failure(OrientedOperationStatus.Unsupported, "profile", "Only proper cardinal right-angle frames and zero-offset rigid ports are supported.");
        if (input.Teeth is <= 0 or > OrientedTransmissionProfile.MaxTeeth || output.Teeth is <= 0 or > OrientedTransmissionProfile.MaxTeeth ||
            input.OuterPitchRadiusPerTooth <= 0 || output.OuterPitchRadiusPerTooth <= 0 || pair.InnerParameter <= 0 || pair.InnerParameter >= 1 ||
            input.Shaft.Id == output.Shaft.Id || input.Port.Id == output.Port.Id || !input.Shaft.IsPrescribed || output.Shaft.IsPrescribed ||
            request.KeepOuts.Count > 16 || request.KeepOuts.Any(k => !k.Envelope.IsOrdered))
            return Failure(OrientedOperationStatus.InvalidInput, "request", "Positive bounded teeth/scales, finite frustum, distinct IDs and one local input driver are required.");
        var r1 = input.OuterPitchRadiusPerTooth * input.Teeth; var r2 = output.OuterPitchRadiusPerTooth * output.Teeth;
        if (input.OuterPitchRadiusPerTooth != output.OuterPitchRadiusPerTooth || !input.Shaft.Contains(pair.Apex) || !output.Shaft.Contains(pair.Apex) ||
            input.Shaft.Frame.Z.Dot(output.Shaft.Frame.Z) != 0 || input.Shaft.Frame.Z.Cross(input.ConeDirection) != default || output.Shaft.Frame.Z.Cross(output.ConeDirection) != default ||
            input.FixedCenter != pair.Apex + input.ConeDirection * r2 || output.FixedCenter != pair.Apex + output.ConeDirection * r1 ||
            !PortOnShaft(input.Port, input.Shaft) || !PortOnShaft(output.Port, output.Shaft))
            return Failure(OrientedOperationStatus.ConstraintFailure, "fixed-geometry", "Fixed apex/lines/centers/scales/stations are inconsistent; nothing was moved or sign-flipped.");
        var qPoint = pair.Apex + input.ConeDirection * r2 + output.ConeDirection * r1;
        var q = OrientedMechanismValidator.VelocityTransfer(input.Shaft.Frame.Z, output.Shaft.Frame.Z, qPoint - pair.Apex);
        if (pair.RequestedTransfer.HasValue && pair.RequestedTransfer.Value != q)
            return Failure(OrientedOperationStatus.ConstraintFailure, "requested-bevel-transfer", "Geometry-derived transfer does not satisfy the optional requested transfer.");
        foreach (var module in new[] { request.Upstream, request.Downstream }.Where(m => m is not null))
        {
            var issue = ValidateSource(module!);
            if (issue is not null) return issue;
        }
        var shafts = new List<OrientedShaft> { input.Shaft, output.Shaft };
        var bodies = new List<OrientedGearBody>
        {
            new("bevel/pinion", input.Shaft.Id, OrientedGearKind.RightAngleBevel, input.Shaft.Frame.At(input.FixedCenter), input.Teeth, r1, "bevel"),
            new("bevel/wheel", output.Shaft.Id, OrientedGearKind.RightAngleBevel, output.Shaft.Frame.At(output.FixedCenter), output.Teeth, r2, "bevel")
        };
        var contacts = new List<OrientedGearContact> { new("bevel/contact", OrientedContactKind.RightAngleBevel, "bevel/pinion", "bevel/wheel", q,
            new RightAnglePitchCone(pair.Apex, input.ConeDirection, output.ConeDirection, qPoint, pair.InnerParameter, input.OuterPitchRadiusPerTooth, output.OuterPitchRadiusPerTooth)) };
        var ports = new List<ShaftPort> { input.Port, output.Port };
        var links = new List<ShaftPortConnection>(); var maps = new List<SourceShaftMapping>(); var sources = new List<OrientedSourceIdentity>();
        var root = input.Shaft.Id; var end = output.Shaft.Id;
        foreach (var entry in new[] { (Key: "pre", Source: request.Upstream, Mating: input), (Key: "post", Source: request.Downstream, Mating: output) })
        {
            if (entry.Source is null) continue;
            var source = entry.Source; var prefix = entry.Key + "/";
            var selectedDof = entry.Key == "pre" ? source.OutputDofId : source.InputDofId;
            var issue = LiftSource(source, entry.Key, selectedDof, entry.Mating, shafts, bodies, contacts, ports, links, maps, sources);
            if (issue is not null) return issue;
            string Map(string id) => id == selectedDof ? entry.Mating.Shaft.Id : prefix + id;
            if (entry.Key == "pre") root = Map(source.InputDofId); else end = Map(source.OutputDofId);
        }
        if (token.IsCancellationRequested) return Failure(OrientedOperationStatus.Cancelled, "cancelled", "Operation cancelled before graph solve.");
        var pose = request.AssemblyPose;
        shafts = shafts.Select(s => new OrientedShaft(s.Id, pose.Transform(s.Frame), s.Id == root)).ToList();
        bodies = bodies.Select(b => new OrientedGearBody(b.Id, b.ShaftId, b.Kind, pose.Transform(b.MountingFrame), b.Teeth, b.OuterPitchRadius, b.SourceModuleId)).ToList();
        ports = ports.Select(p => new ShaftPort(p.Id, p.ShaftId, pose.Transform(p.Frame), p.PhaseOffset, p.Kind)).ToList();
        contacts = contacts.Select(c => new OrientedGearContact(c.Id, c.Kind, c.BodyAId, c.BodyBId, c.StoredTransfer, c.Cone is null ? null :
            new RightAnglePitchCone(pose.Point(c.Cone.Apex), pose.Vector(c.Cone.OutwardA), pose.Vector(c.Cone.OutwardB), pose.Point(c.Cone.OuterContact),
                c.Cone.InnerParameter, c.Cone.OuterScaleA, c.Cone.OuterScaleB))).ToList();
        // No copied source-local solution participates in this solve.
        var edges = contacts.Select(c => new ScalarAffineCoupling(c.Id, bodies.Single(b => b.Id == c.BodyAId).ShaftId, bodies.Single(b => b.Id == c.BodyBId).ShaftId, c.StoredTransfer));
        var solved = KinematicSolver.SolveAffine(root, shafts.Select(s => new RotationalDof(s.Id, s.IsPrescribed)), edges);
        if (!solved.IsValid) return new OrientedTransmissionResult(OrientedOperationStatus.ConstraintFailure, null, new OrientedValidation(Array.Empty<OrientedDomainCheck>(), solved.Diagnostics));
        var model = new OrientedMechanism(root, end, shafts, bodies, contacts, ports, links, maps, sources, solved.Solution!, request.RequireCrossComponentClearance, request.KeepOuts);
        var validation = ValidateContext(request, model);
        return new OrientedTransmissionResult(validation.IsValid ? OrientedOperationStatus.Complete : OrientedOperationStatus.ConstraintFailure, validation.IsValid ? model : null, validation);
    }

    /// <summary>Checks source-to-world mappings against the request without regenerating a mechanism or adopting cached source channels.</summary>
    public static OrientedValidation ValidateContext(OrientedCompositionRequest request, OrientedMechanism model)
    {
        var baseResult = OrientedMechanismValidator.Validate(model); var checks = baseResult.Checks.ToList();
        void Check(string subject, bool valid, string detail) => checks.Add(new OrientedDomainCheck("source-request-context", subject, valid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
        if (!baseResult.IsValid) return baseResult;
        var pose = request.AssemblyPose;
        Check("policy", pose.IsProperCardinal && model.Profile == request.Bevel.Profile && model.RequireCrossComponentClearance == request.RequireCrossComponentClearance &&
            model.KeepOuts.Count == request.KeepOuts.Count && model.KeepOuts.Zip(request.KeepOuts, (a, b) => a.Id == b.Id && a.Envelope.Min == b.Envelope.Min && a.Envelope.Max == b.Envelope.Max).All(x => x), "Mechanical pose/profile and declared clearance policy are request-owned.");
        var pair = model.Contacts.Single(c => c.Kind == OrientedContactKind.RightAngleBevel); var cone = pair.Cone!;
        foreach (var entry in new[] { (Mount: request.Bevel.Input, Id: pair.BodyAId), (Mount: request.Bevel.Output, Id: pair.BodyBId) })
        {
            var body = model.Bodies.Single(b => b.Id == entry.Id); var shaft = model.Shafts.Single(s => s.Id == body.ShaftId);
            var port = model.Ports.SingleOrDefault(p => p.Id == entry.Mount.Port.Id);
            Check(entry.Id, body.Teeth == entry.Mount.Teeth && body.OuterPitchRadius == entry.Mount.OuterPitchRadiusPerTooth * entry.Mount.Teeth &&
                body.MountingFrame.Origin == pose.Point(entry.Mount.FixedCenter) && shaft.Id == entry.Mount.Shaft.Id && SameFrame(shaft.Frame, pose.Transform(entry.Mount.Shaft.Frame)) &&
                port is not null && SameFrame(port.Frame, pose.Transform(entry.Mount.Port.Frame)) && port.ShaftId == shaft.Id && port.PhaseOffset == entry.Mount.Port.PhaseOffset,
                "Fixed bevel centers/shaft coordinates/ports are not silently moved or re-signed.");
        }
        Check("cone", cone.Apex == pose.Point(request.Bevel.Apex) && cone.OutwardA == pose.Vector(request.Bevel.Input.ConeDirection) && cone.OutwardB == pose.Vector(request.Bevel.Output.ConeDirection) &&
            cone.InnerParameter == request.Bevel.InnerParameter && (!request.Bevel.RequestedTransfer.HasValue || pair.StoredTransfer == request.Bevel.RequestedTransfer.Value), "Cone mounting and optional target are independent from positive coordinate direction.");
        var expectedSources = 0; var expectedMappings = 0; var expectedBodies = 2; var expectedContacts = 1;
        var expectedRoot = request.Bevel.Input.Shaft.Id; var expectedOutput = request.Bevel.Output.Shaft.Id;
        foreach (var entry in new[] { (Key: "pre", Source: request.Upstream, Shaft: request.Bevel.Input.Shaft.Id, Port: request.Bevel.Input.Port.Id),
            (Key: "post", Source: request.Downstream, Shaft: request.Bevel.Output.Shaft.Id, Port: request.Bevel.Output.Port.Id) })
        {
            if (entry.Source is null) continue;
            var source = entry.Source; var prefix = entry.Key + "/"; expectedSources++; expectedMappings += source.Source.Kinematic.Dofs.Count;
            expectedBodies += source.Source.Spatial.Bodies.Count; expectedContacts += source.Source.Spatial.Contacts.Count;
            var selected = entry.Key == "pre" ? source.OutputDofId : source.InputDofId;
            string Map(string id) => id == selected ? entry.Shaft : prefix + id;
            if (entry.Key == "pre") expectedRoot = Map(source.InputDofId); else expectedOutput = Map(source.OutputDofId);
            CheckSourceContext(source, entry.Key, selected, entry.Shaft, entry.Port, pose, model, Check);
        }
        Check("coverage", model.Sources.Count == expectedSources && model.SourceMappings.Count == expectedMappings && model.Bodies.Count == expectedBodies &&
            model.Contacts.Count == expectedContacts && model.Shafts.Count == expectedMappings + 2 - expectedSources && model.Ports.Count == expectedSources + 2 &&
            model.Connections.Count == expectedSources && model.RootShaftId == expectedRoot && model.OutputShaftId == expectedOutput,
            "No dropped/merged bodies, phantom contacts, coincident-axis auto-merge or local driver declarations.");
        Check("global-target", model.Solution.TryGetState(model.OutputShaftId, out var output) && (!request.RequestedTransfer.HasValue || output!.Coefficient == request.RequestedTransfer.Value),
            "Optional requested global transfer is an acceptance condition, never a solved channel source.");
        return new OrientedValidation(checks, baseResult.Diagnostics);
    }

    public static ReadOnlyCollection<OrientedShaftEvaluation> Evaluate(OrientedMechanism model, Rational rootTurns)
    {
        var validation = OrientedMechanismValidator.Validate(model);
        if (!validation.IsValid) throw new ArgumentException("Oriented mechanism validation failed.");
        return model.Shafts.Select(s => { model.Solution.TryGetState(s.Id, out var state); return new OrientedShaftEvaluation(s.Id, rootTurns * state!.Coefficient, s.Frame.Z, state.Coefficient); }).ToList().AsReadOnly();
    }

    // Shared lifting primitive: callers explicitly select the source terminal.
    // 18A selects pre.output; 19A selects both branch inputs. This helper never chooses a root.
    internal static OrientedTransmissionResult? LiftSource(PlanarShaftModule source, string key, string selectedDof, BevelGearMount mating,
        List<OrientedShaft> shafts, List<OrientedGearBody> bodies, List<OrientedGearContact> contacts, List<ShaftPort> ports,
        List<ShaftPortConnection> links, List<SourceShaftMapping> maps, List<OrientedSourceIdentity> sources, bool allowOppositeSourcePort = false)
    {
        var prefix = key + "/";
        var sourceBody = source.Source.Spatial.Bodies.Single(b => b.DofId == selectedDof);
        var sourceAxis = source.Source.Spatial.Axes.Single(a => a.Id == sourceBody.AxisId);
        var sourceFrame = source.Pose.At(source.Pose.Point(new ExactVector3(new Rational(sourceAxis.X), new Rational(sourceAxis.Y), 0)));
        var sourceShaft = new OrientedShaft(selectedDof, sourceFrame);
        if (source.ConnectionPort.ShaftId != selectedDof || !PortOnShaft(source.ConnectionPort, sourceShaft) ||
            (!allowOppositeSourcePort && source.ConnectionPort.Frame.Z != source.Pose.Z) || source.ConnectionPort.Frame.X != source.Pose.X ||
            !sourceShaft.SameLine(mating.Shaft) || source.ConnectionPort.Frame.Origin != mating.Port.Frame.Origin || source.ConnectionPort.Frame.X != mating.Port.Frame.X)
            return Failure(OrientedOperationStatus.ConstraintFailure, prefix + "port", "Explicit source and mating port station/line/zero reference do not align; no partial assembly is returned.");
        string Map(string id) => id == selectedDof ? mating.Shaft.Id : prefix + id;
        foreach (var dof in source.Source.Kinematic.Dofs)
        {
            var body = source.Source.Spatial.Bodies.Single(b => b.DofId == dof.Id);
            var axis = source.Source.Spatial.Axes.Single(a => a.Id == body.AxisId);
            var center = source.Pose.Point(new ExactVector3(new Rational(axis.X), new Rational(axis.Y), 0));
            if (dof.Id != selectedDof) shafts.Add(new OrientedShaft(Map(dof.Id), source.Pose.At(center)));
            var coordinate = dof.Id == selectedDof ? source.Pose.Z.Dot(mating.Shaft.Frame.Z) : Rational.One;
            maps.Add(new SourceShaftMapping(key, dof.Id, Map(dof.Id), coordinate));
            bodies.Add(new OrientedGearBody(prefix + body.Id, Map(dof.Id), OrientedGearKind.PlanarSpur, source.Pose.At(center), body.ToothCount, new Rational(body.PitchRadius), key));
        }
        foreach (var contact in source.Source.Spatial.Contacts)
        {
            var a = bodies.Single(b => b.Id == prefix + contact.BodyAId); var b = bodies.Single(b => b.Id == prefix + contact.BodyBId);
            var za = shafts.Single(s => s.Id == a.ShaftId).Frame.Z; var zb = shafts.Single(s => s.Id == b.ShaftId).Frame.Z;
            contacts.Add(new OrientedGearContact(prefix + contact.Id, OrientedContactKind.ExternalSpur, a.Id, b.Id, -za.Dot(zb) * a.OuterPitchRadius / b.OuterPitchRadius));
        }
        var port = new ShaftPort(prefix + source.ConnectionPort.Id, mating.Shaft.Id, source.ConnectionPort.Frame);
        ports.Add(port); links.Add(new ShaftPortConnection(prefix + "shaft-connection", port.Id, mating.Port.Id, port.Frame.Z.Dot(mating.Port.Frame.Z)));
        sources.Add(new OrientedSourceIdentity(key, source.CandidateId, source.ArtifactHash));
        return null;
    }

    internal static OrientedTransmissionResult? ValidateSource(PlanarShaftModule module)
    {
        var source = module.Source; var k = source.Kinematic; var spatial = source.Spatial;
        if (!module.Pose.IsProperCardinal || module.ConnectionPort.PhaseOffset != 0 || !module.ConnectionPort.Frame.IsProperCardinal || module.ConnectionPort.Kind != ShaftConnectionKind.RigidZeroPhase ||
            spatial.Bodies.Select(b => b.Layer).Distinct().Count() != 1 || spatial.Bodies.Any(b => b.ExactMountingPhase != 0) || k.Couplings.Any(c => c.PhaseOffset != 0) ||
            k.Dofs.Count is < 2 or > OrientedTransmissionProfile.MaxSourceShafts || spatial.Bodies.Count != k.Dofs.Count || spatial.Axes.Count != k.Dofs.Count ||
            spatial.Bodies.Select(b => b.DofId).Distinct(StringComparer.Ordinal).Count() != k.Dofs.Count)
            return Failure(OrientedOperationStatus.Unsupported, "source-profile", "Only a single-layer, one-body-per-shaft, zero-phase spur chain and proper rigid pose are supported.");
        if (!new GenerationEngine().Validate(source).IsValid || k.Dofs.Count(d => d.IsPrescribed) != 1 || k.RootDofId != module.InputDofId ||
            !k.Dofs.Any(d => d.Id == module.InputDofId && d.IsPrescribed) || !k.Dofs.Any(d => d.Id == module.OutputDofId) || module.InputDofId == module.OutputDofId ||
            k.Couplings.Count != k.Dofs.Count - 1 || k.Dofs.Any(d => k.Couplings.Count(c => c.DriverDofId == d.Id || c.DrivenDofId == d.Id) != (d.Id == module.InputDofId || d.Id == module.OutputDofId ? 1 : 2)))
            return Failure(OrientedOperationStatus.ConstraintFailure, "source-validity", "Source validity, one driver, explicit endpoint ports and connected serial chain are required.");
        return null;
    }
    internal static void CheckSourceContext(PlanarShaftModule source, string key, string selected, string retainedShaft, string matingPort,
        OrientedFrame pose, IOrientedGraph model, Action<string, bool, string> check)
    {
        var prefix = key + "/";
        var sourceValid = ValidateSource(source) is null;
        check(prefix + "source", sourceValid, "Source mechanical validity and bounded single-layer zero-phase chain profile rechecked.");
        if (!sourceValid) return;
        var identity = model.Sources.SingleOrDefault(s => s.ModuleId == key);
        check(prefix + "identity-reference", identity is not null && identity.CandidateId == source.CandidateId && identity.ArtifactHash == source.ArtifactHash,
            "Original source IDs retained; source-byte identity is additionally verified at the façade boundary.");
        string Map(string id) => id == selected ? retainedShaft : prefix + id;
        foreach (var body in source.Source.Spatial.Bodies)
        {
            var axis = source.Source.Spatial.Axes.Single(a => a.Id == body.AxisId);
            var expectedFrame = pose.Transform(source.Pose.At(source.Pose.Point(new ExactVector3(new Rational(axis.X), new Rational(axis.Y), 0))));
            var actual = model.Bodies.SingleOrDefault(b => b.Id == prefix + body.Id);
            var mapping = model.SourceMappings.SingleOrDefault(m => m.ModuleId == key && m.SourceDofId == body.DofId);
            var target = model.Shafts.SingleOrDefault(s => s.Id == Map(body.DofId));
            check(prefix + body.Id, actual is not null && target is not null && mapping is not null && actual.Kind == OrientedGearKind.PlanarSpur && actual.SourceModuleId == key &&
                SameFrame(actual.MountingFrame, expectedFrame) && actual.Teeth == body.ToothCount && actual.OuterPitchRadius == new Rational(body.PitchRadius) &&
                actual.ShaftId == target.Id && mapping.ShaftId == target.Id && mapping.CoordinateTransfer == expectedFrame.Z.Dot(target.Frame.Z),
                "One original gear body and DOF mapping preserved with the exact common rigid pose.");
        }
        foreach (var contact in source.Source.Spatial.Contacts)
        {
            var actual = model.Contacts.SingleOrDefault(c => c.Id == prefix + contact.Id);
            check(prefix + contact.Id, actual is not null && actual.Kind == OrientedContactKind.ExternalSpur && actual.BodyAId == prefix + contact.BodyAId && actual.BodyBId == prefix + contact.BodyBId,
                "Original typed spur contact and source body endpoints preserved.");
        }
        var port = model.Ports.SingleOrDefault(p => p.Id == prefix + source.ConnectionPort.Id);
        var link = model.Connections.SingleOrDefault(l => l.Id == prefix + "shaft-connection");
        check(prefix + "port", source.ConnectionPort.ShaftId == selected && port is not null && port.ShaftId == retainedShaft &&
            SameFrame(port.Frame, pose.Transform(source.ConnectionPort.Frame)) && link is not null && link.PortAId == port.Id && link.PortBId == matingPort,
            "Only the explicitly selected terminal shaft port is unified.");
    }
    internal static bool PortOnShaft(ShaftPort p, OrientedShaft s) => p.ShaftId == s.Id && p.Frame.IsProperCardinal && p.PhaseOffset == 0 &&
        p.Kind == ShaftConnectionKind.RigidZeroPhase && s.Contains(p.Frame.Origin) && p.Frame.Z.Cross(s.Frame.Z) == default && p.Frame.X == s.Frame.X;
    public static bool SameFrame(OrientedFrame a, OrientedFrame b) => a.Origin == b.Origin && a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    private static OrientedTransmissionResult Failure(OrientedOperationStatus status, string subject, string detail) => new(status, null,
        new OrientedValidation(new[] { new OrientedDomainCheck("request", subject, OrientedCheckVerdict.Fail, true, detail) },
            new[] { new Diagnostic("ORIENTED_" + status.ToString().ToUpperInvariant(), DiagnosticSeverity.Error, detail, subject) }));
}
