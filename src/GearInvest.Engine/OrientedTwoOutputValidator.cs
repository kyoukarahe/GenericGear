using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Validates a supplied graph and its source/request correspondence without calling the composer.</summary>
public static class OrientedTwoOutputValidator
{
    public static OrientedValidation Validate(OrientedTwoOutputMechanism model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        var baseResult = OrientedMechanismValidator.ValidateGraph(model,
            OrientedTwoOutputProfile.IsSupported(model.Profile) && model.Shafts.Count is >= 3 and <= OrientedTwoOutputProfile.MaxShafts &&
            model.Bodies.Count is >= 4 and <= OrientedTwoOutputProfile.MaxBodies && model.Contacts.Count <= OrientedTwoOutputProfile.MaxContacts &&
            model.Ports.Count <= OrientedTwoOutputProfile.MaxPorts && model.KeepOuts.Count <= 16 && model.Connections.Count is >= 1 and <= 2 &&
            model.SourceMappings.Count <= 32 && model.Sources.Count is >= 1 and <= 2 && model.Outputs.Count == 2,
            model.Outputs.Select(o => o.ShaftId), "Exactly two branch terminals, one retained input driver and one bevel pair; at most two bounded planar chains.",
            "Only the bevel input shaft is prescribed; all source-local driver flags are removed.",
            model.Profile == OrientedTwoOutputProfile.RefinedId ? PitchClearancePolicy.Refined : PitchClearancePolicy.Legacy);
        if (!baseResult.IsValid) return baseResult;
        var checks = baseResult.Checks.ToList();
        void Check(string subject, bool valid, string detail) => checks.Add(new OrientedDomainCheck("two-output-topology", subject, valid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
        var pair = model.Contacts.Single(c => c.Kind == OrientedContactKind.RightAngleBevel);
        var pinion = model.Bodies.Single(b => b.Id == pair.BodyAId); var wheel = model.Bodies.Single(b => b.Id == pair.BodyBId);
        var withB = model.Sources.Any(s => s.ModuleId == "turned");
        var endpoints = model.Contacts.Select(c => (A: model.Bodies.Single(b => b.Id == c.BodyAId).ShaftId, B: model.Bodies.Single(b => b.Id == c.BodyBId).ShaftId)).ToArray();
        int Degree(string id) => endpoints.Count(e => e.A == id || e.B == id);
        Check("tree", model.Contacts.Count == model.Shafts.Count - 1 && pinion.ShaftId == model.RootShaftId &&
            model.Sources.Any(s => s.ModuleId == "parallel") && model.Sources.All(s => s.ModuleId is "parallel" or "turned") &&
            model.Bodies.Count == model.SourceMappings.Count + 2 && model.Shafts.Count == model.SourceMappings.Count + 2 - model.Sources.Count &&
            model.Connections.Count == model.Sources.Count, "Actual connected tree with preserved source inventory, one bevel input root and only explicit shaft unification.");
        Check("output-keys-roles", OrientedMechanismValidator.Unique(model.Outputs.Select(o => o.Key)) &&
            model.Outputs.Count(o => o.Role == OrientedOutputRole.ParallelBranch) == 1 && model.Outputs.Count(o => o.Role == OrientedOutputRole.TurnedBranch) == 1 &&
            model.Outputs.Select(o => o.ShaftId).Distinct(StringComparer.Ordinal).Count() == 2 && model.Outputs.Select(o => o.PortId).Distinct(StringComparer.Ordinal).Count() == 2,
            "Two distinct keyed and typed terminals remain present even when their coefficients are equal.");
        foreach (var o in model.Outputs)
        {
            var body = model.Bodies.SingleOrDefault(b => b.Id == o.BodyId); var port = model.Ports.SingleOrDefault(p => p.Id == o.PortId);
            var module = o.Role == OrientedOutputRole.ParallelBranch ? "parallel" : withB ? "turned" : "bevel";
            Check(o.Key, body is not null && port is not null && body.ShaftId == o.ShaftId && port.ShaftId == o.ShaftId &&
                o.ShaftId != model.RootShaftId && Degree(o.ShaftId) == 1 && body.SourceModuleId == module &&
                (o.Role != OrientedOutputRole.TurnedBranch || withB || body.Id == wheel.Id), "Output body, shaft, port and terminal role agree; no root or internal shaft masquerades as an output.");
        }
        foreach (var shaft in model.Shafts)
        {
            var shared = shaft.Id == pinion.ShaftId || (withB && shaft.Id == wheel.ShaftId);
            Check("inventory/" + shaft.Id, model.Bodies.Count(b => b.ShaftId == shaft.Id) == (shared ? 2 : 1) &&
                Degree(shaft.Id) == (model.Outputs.Any(o => o.ShaftId == shaft.Id) ? 1 : 2), "Only designated shared shafts carry two separate bodies; every other shaft is a chain member or terminal.");
        }
        return new OrientedValidation(checks, baseResult.Diagnostics, baseResult.PitchProofs, baseResult.ClearancePolicy);
    }

    public static OrientedValidation ValidateContext(OrientedTwoOutputCompositionRequest request, OrientedTwoOutputMechanism model)
    {
        var validated = Validate(model); if (!validated.IsValid) return validated;
        var checks = validated.Checks.ToList();
        void Check(string subject, bool valid, string detail) => checks.Add(new OrientedDomainCheck("two-output-request-context", subject, valid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
        var pose = request.AssemblyPose;
        Check("policy", request.Profile == model.Profile && pose.IsProperCardinal && request.ParallelBranch is not null &&
            OrientedTwoOutputComposer.ValidOutputRequests(request.Outputs) && request.RequireCrossComponentClearance == model.RequireCrossComponentClearance &&
            request.KeepOuts.Count == model.KeepOuts.Count && request.KeepOuts.Zip(model.KeepOuts, (a,b) => a.Id == b.Id && a.Envelope.Min == b.Envelope.Min && a.Envelope.Max == b.Envelope.Max).All(x => x),
            "Request owns output roles, proper assembly pose and world-space keep-outs; keep-outs are not posed with the assembly.");
        if (request.ParallelBranch is null || !OrientedTwoOutputComposer.ValidOutputRequests(request.Outputs) || request.Bevel is null) return new OrientedValidation(checks, validated.Diagnostics, validated.PitchProofs, validated.ClearancePolicy);
        var bevel = request.Bevel; var pair = model.Contacts.Single(c => c.Kind == OrientedContactKind.RightAngleBevel); var cone = pair.Cone!;
        Check("root", model.RootShaftId == bevel.Input.Shaft.Id && bevel.Input.Shaft.IsPrescribed && !bevel.Output.Shaft.IsPrescribed && bevel.Profile == OrientedTransmissionProfile.Id,
            "The original bevel input is the only global root. A attaches its input, never its serial output.");
        foreach (var entry in new[] { (Mount: bevel.Input, Id: "bevel/pinion"), (Mount: bevel.Output, Id: "bevel/wheel") })
        {
            var mount = entry.Mount; var body = model.Bodies.SingleOrDefault(b => b.Id == entry.Id); var shaft = model.Shafts.SingleOrDefault(s => s.Id == mount.Shaft.Id);
            var port = model.Ports.SingleOrDefault(p => p.Id == mount.Port.Id);
            Check(entry.Id, body is not null && shaft is not null && port is not null && body.ShaftId == shaft.Id && body.SourceModuleId == "bevel" &&
                body.Teeth == mount.Teeth && body.OuterPitchRadius == mount.Teeth * mount.OuterPitchRadiusPerTooth &&
                OrientedTransmissionComposer.SameFrame(body.MountingFrame, pose.Transform(mount.Shaft.Frame.At(mount.FixedCenter))) &&
                OrientedTransmissionComposer.SameFrame(shaft.Frame, pose.Transform(mount.Shaft.Frame)) &&
                OrientedTransmissionComposer.PortOnShaft(mount.Port, mount.Shaft) && port.ShaftId == shaft.Id && port.Kind == mount.Port.Kind && port.PhaseOffset == mount.Port.PhaseOffset &&
                OrientedTransmissionComposer.SameFrame(port.Frame, pose.Transform(mount.Port.Frame)), "Exact fixed bevel body, retained shaft frame and explicit port are request-owned, not repaired.");
        }
        Check("cone", pair.Id == "bevel/contact" && pair.BodyAId == "bevel/pinion" && pair.BodyBId == "bevel/wheel" &&
            cone.Apex == pose.Point(bevel.Apex) && cone.OutwardA == pose.Vector(bevel.Input.ConeDirection) && cone.OutwardB == pose.Vector(bevel.Output.ConeDirection) &&
            cone.InnerParameter == bevel.InnerParameter && cone.OuterScaleA == bevel.Input.OuterPitchRadiusPerTooth && cone.OuterScaleB == bevel.Output.OuterPitchRadiusPerTooth &&
            (!bevel.RequestedTransfer.HasValue || pair.StoredTransfer == bevel.RequestedTransfer.Value), "Physical cone mounting is independent of shaft-coordinate signs and target constraints.");
        int mappings = 0, bodyCount = 2, contactCount = 1, sourceCount = 0;
        foreach (var entry in new[] { (Key: "parallel", Source: request.ParallelBranch, Mount: bevel.Input), (Key: "turned", Source: request.TurnedBranch, Mount: bevel.Output) })
        {
            if (entry.Source is null) continue;
            var source = entry.Source; sourceCount++; mappings += source.Source.Kinematic.Dofs.Count; bodyCount += source.Source.Spatial.Bodies.Count; contactCount += source.Source.Spatial.Contacts.Count;
            OrientedTransmissionComposer.CheckSourceContext(source, entry.Key, source.InputDofId, entry.Mount.Shaft.Id, entry.Mount.Port.Id, pose, model, Check);
            if (OrientedTransmissionComposer.ValidateSource(source) is not null) continue;
            foreach (var body in source.Source.Spatial.Bodies)
            {
                var axis = source.Source.Spatial.Axes.Single(a => a.Id == body.AxisId);
                var frame = source.Pose.At(source.Pose.Point(new ExactVector3(new Rational(axis.X), new Rational(axis.Y), 0)));
                var selected = body.DofId == source.InputDofId;
                var shaft = model.Shafts.SingleOrDefault(s => s.Id == (selected ? entry.Mount.Shaft.Id : entry.Key + "/" + body.DofId));
                Check(entry.Key + "/shaft/" + body.DofId, shaft is not null && (selected ?
                    new OrientedShaft(body.DofId, frame).SameLine(entry.Mount.Shaft) && OrientedTransmissionComposer.PortOnShaft(source.ConnectionPort, new OrientedShaft(body.DofId, frame)) &&
                        source.ConnectionPort.Frame.Origin == entry.Mount.Port.Frame.Origin && source.ConnectionPort.Frame.X == entry.Mount.Port.Frame.X :
                    OrientedTransmissionComposer.SameFrame(shaft.Frame, pose.Transform(frame))), "Original shaft station/frame survives lifting; only the selected source input shares the retained shaft.");
            }
        }
        var expectedPortIds = new[] { bevel.Input.Port.Id, bevel.Output.Port.Id, "parallel/" + request.ParallelBranch.ConnectionPort.Id }.ToList();
        if (request.TurnedBranch is not null) expectedPortIds.Add("turned/" + request.TurnedBranch.ConnectionPort.Id);
        foreach (var requested in request.Outputs)
        {
            var source = requested.Role == OrientedOutputRole.ParallelBranch ? request.ParallelBranch : request.TurnedBranch;
            var prefix = requested.Role == OrientedOutputRole.ParallelBranch ? "parallel/" : source is null ? "" : "turned/";
            var expectedDof = source?.OutputDofId ?? bevel.Output.Shaft.Id;
            var expectedBody = source?.Source.Spatial.Bodies.SingleOrDefault(b => b.DofId == expectedDof)?.Id ?? "bevel/wheel";
            var output = model.Outputs.SingleOrDefault(o => o.Key == requested.Key);
            var portId = prefix + requested.TerminalPort.Id; expectedPortIds.Add(portId);
            var port = model.Ports.SingleOrDefault(p => p.Id == portId);
            Check("output/" + requested.Key, output is not null && port is not null && output.Role == requested.Role && requested.TerminalPort.ShaftId == expectedDof &&
                requested.TerminalBodyId == expectedBody && output.BodyId == prefix + expectedBody && output.ShaftId == prefix + expectedDof && output.PortId == portId &&
                port.PhaseOffset == requested.TerminalPort.PhaseOffset && port.Kind == requested.TerminalPort.Kind &&
                OrientedTransmissionComposer.SameFrame(port.Frame, pose.Transform(requested.TerminalPort.Frame)), "Stable output key is bound to its explicit topology role, original terminal and exact port frame.");
            if (output is not null && port is not null && model.Solution.TryGetState(output.ShaftId, out var state))
            {
                var shaft = model.Shafts.Single(s => s.Id == output.ShaftId); var sign = port.Frame.Z.Dot(shaft.Frame.Z); var qPort = sign * state!.Coefficient;
                Check("target/" + requested.Key, !requested.RequestedTransfer.HasValue || requested.RequestedTransfer.Value == qPort,
                    "Requested port transfer is checked against geometry-derived solution: actual=" + qPort + ";requested=" + (requested.RequestedTransfer?.ToString() ?? "none"));
                Check("world-rate/" + requested.Key, port.Frame.Z * qPort == shaft.Frame.Z * state.Coefficient, "Port-coordinate reversal changes coordinates, not physical world rotation.");
            }
        }
        Check("inventory", model.Sources.Count == sourceCount && model.SourceMappings.Count == mappings && model.Shafts.Count == 2 + mappings - sourceCount &&
            model.Bodies.Count == bodyCount && model.Contacts.Count == contactCount && model.Connections.Count == sourceCount &&
            model.Ports.Select(p => p.Id).SequenceEqual(expectedPortIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)),
            "Complete original inventory and explicit port coverage; no omitted or automatically deduplicated bodies, outputs, contacts or sources.");
        return new OrientedValidation(checks, validated.Diagnostics, validated.PitchProofs, validated.ClearancePolicy);
    }
}
