using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Independent exact geometry/graph validation of a supplied mechanism, not a call to the realization builder.</summary>
public static class OrientedMechanismValidator
{
    public static OrientedValidation Validate(OrientedMechanism model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        return ValidateGraph(model, model.Profile == OrientedTransmissionProfile.Id && model.Shafts.Count is >= 2 and <= 34 &&
            model.Bodies.Count is >= 2 and <= 34 && model.Contacts.Count <= 33 && model.Ports.Count <= 4 && model.KeepOuts.Count <= 16,
            new[] { model.OutputShaftId }, "One fixed pair, at most two 16-shaft planar chains; bounded cardinal profile.",
            "Only the actual upstream root remains prescribed.");
    }

    internal static OrientedValidation ValidateGraph(IOrientedGraph model, bool profileValid, IEnumerable<string> outputShaftIds, string profileDetail, string driverDetail,
        string clearancePolicy = PitchClearancePolicy.Legacy)
    {
        var checks = new List<OrientedDomainCheck>();
        void Check(string domain, string subject, bool valid, string detail) => checks.Add(new OrientedDomainCheck(domain, subject,
            valid ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
        bool Failed() => checks.Any(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass);
        Check("profile", "mechanism", profileValid, profileDetail);
        Check("references", "identifiers", Unique(model.Shafts.Select(s => s.Id)) && Unique(model.Bodies.Select(b => b.Id)) &&
            Unique(model.Contacts.Select(c => c.Id)) && Unique(model.Ports.Select(p => p.Id)) && Unique(model.Connections.Select(c => c.Id)) &&
            Unique(model.Sources.Select(s => s.ModuleId)) && Unique(model.SourceMappings.Select(m => m.ModuleId + "/" + m.SourceDofId)) &&
            Unique(model.KeepOuts.Select(k => k.Id)), "Stable bounded IDs are unique within each typed namespace.");
        if (Failed()) return new OrientedValidation(checks, clearancePolicy: clearancePolicy);
        var shafts = model.Shafts.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var bodies = model.Bodies.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var ports = model.Ports.ToDictionary(p => p.Id, StringComparer.Ordinal);
        Check("references", "bindings", bodies.Values.All(b => shafts.ContainsKey(b.ShaftId)) &&
            model.Contacts.All(c => bodies.ContainsKey(c.BodyAId) && bodies.ContainsKey(c.BodyBId) && c.BodyAId != c.BodyBId) &&
            ports.Values.All(p => shafts.ContainsKey(p.ShaftId)) && model.Connections.All(c => ports.ContainsKey(c.PortAId) && ports.ContainsKey(c.PortBId)) &&
            model.SourceMappings.All(m => shafts.ContainsKey(m.ShaftId)) && shafts.ContainsKey(model.RootShaftId) && outputShaftIds.All(shafts.ContainsKey),
            "All typed references, driver and selected output resolve.");
        foreach (var shaft in shafts.Values) Check("frame", shaft.Id, shaft.Frame.IsProperCardinal, "Proper signed-permutation frame; positive rotation is right-hand about Z.");
        foreach (var body in bodies.Values) Check("frame", body.Id, body.MountingFrame.IsProperCardinal, "Zero mounting frame is proper, not a reflection.");
        foreach (var port in ports.Values) Check("frame", port.Id, port.Frame.IsProperCardinal, "Port frame is proper.");
        if (Failed()) return new OrientedValidation(checks, clearancePolicy: clearancePolicy);
        Check("profile", "typed-pair", model.Contacts.Count(c => c.Kind == OrientedContactKind.RightAngleBevel) == 1 &&
            bodies.Values.Count(b => b.Kind == OrientedGearKind.RightAngleBevel) == 2 &&
            model.Contacts.All(c => Enum.IsDefined(typeof(OrientedContactKind), c.Kind)) && bodies.Values.All(b => Enum.IsDefined(typeof(OrientedGearKind), b.Kind)),
            "Exactly one actual bevel contact and two separate bevel bodies; no disguised planar edge.");
        Check("driver", "global", shafts.Values.Count(s => s.IsPrescribed) == 1 && shafts[model.RootShaftId].IsPrescribed,
            driverDetail);
        foreach (var body in bodies.Values)
        {
            var shaft = shafts[body.ShaftId];
            Check("mounting", body.Id, MechanicalConnectionPredicates.BodyMounting(body, shaft, OrientedTransmissionProfile.MaxTeeth),
                "Separate body on its own shaft line; zero reference aligned; no body merge or hidden phase.");
        }
        foreach (var port in ports.Values)
        {
            var shaft = shafts[port.ShaftId];
            Check("port", port.Id, MechanicalConnectionPredicates.PortMounting(port, shaft),
                "Explicit axial station on shaft; same zero ray; coordinate direction may be opposite.");
        }
        foreach (var link in model.Connections)
        {
            var a = ports[link.PortAId]; var b = ports[link.PortBId];
            Check("shaft-unification", link.Id, MechanicalConnectionPredicates.RigidPortConnection(a, b, link.CoordinateTransfer),
                "Explicit coincident station/line and aligned zero ray; local signed coordinates follow physical angular velocity.");
        }
        Check("references", "contact-coverage", bodies.Values.All(b => model.Contacts.Any(c => c.BodyAId == b.Id || c.BodyBId == b.Id)) &&
            Unique(model.Contacts.Select(c => PairKey(c.BodyAId, c.BodyBId))), "Every gear has a contact; each pair has one typed contact.");
        if (Failed()) return new OrientedValidation(checks, clearancePolicy: clearancePolicy);

        var lowered = new List<ScalarAffineCoupling>();
        foreach (var contact in model.Contacts)
        {
            var a = bodies[contact.BodyAId]; var b = bodies[contact.BodyBId];
            var sa = shafts[a.ShaftId]; var sb = shafts[b.ShaftId];
            var local = MechanicalConnectionPredicates.Contact(contact.Id, contact.Kind, a, b, sa, sb, contact.Cone);
            checks.AddRange(local.Checks);
            var transfer = local.Transfer;
            // Preserve old invalid-cone short circuit, including diagnostic bytes.
            if (contact.Kind == OrientedContactKind.RightAngleBevel && transfer.IsZero) continue;
            Check("stored-relation", contact.Id, contact.StoredTransfer == transfer && !transfer.IsZero, "Stored edge equals relation derived from actual geometry and positive axes.");
            if (!transfer.IsZero) lowered.Add(new ScalarAffineCoupling(contact.Id, a.ShaftId, b.ShaftId, transfer));
        }
        if (Failed()) return new OrientedValidation(checks, clearancePolicy: clearancePolicy);
        var solved = KinematicSolver.SolveAffine(model.RootShaftId, shafts.Values.Select(s => new RotationalDof(s.Id, s.IsPrescribed)), lowered);
        Check("global-kinematics", "fresh-graph", solved.IsValid, "All typed contacts lowered only after geometry checks; one connected exact global graph.");
        Check("global-kinematics", "stored-channels", solved.Solution is not null && SameSolution(solved.Solution, model.Solution),
            "Stored all-channel coefficients/zero phases match a new solve, not concatenated local solutions.");
        foreach (var contact in model.Contacts.Where(c => c.Cone is not null))
        {
            var sa = shafts[bodies[contact.BodyAId].ShaftId]; var sb = shafts[bodies[contact.BodyBId].ShaftId];
            if (solved.Solution is not null && solved.Solution.TryGetState(sa.Id, out var va) && solved.Solution.TryGetState(sb.Id, out var vb))
            {
                var g = contact.Cone!.OuterContact - contact.Cone.Apex;
                Check("global-vector-kinematics", contact.Id, (sa.Frame.Z * va!.Coefficient).Cross(g) == (sb.Frame.Z * vb!.Coefficient).Cross(g),
                    "World angular-velocity vectors from the single global driver have equal contact velocity.");
            }
        }

        if (clearancePolicy == PitchClearancePolicy.Refined && Failed()) return new OrientedValidation(checks, solved.Diagnostics, clearancePolicy: clearancePolicy);
        var proofs = new List<PitchPairProof>();
        var envelopes = bodies.Values.ToDictionary(b => b.Id, b => ExactEnvelope3.Of(b, model.Contacts.FirstOrDefault(c => c.Cone is not null && (c.BodyAId == b.Id || c.BodyBId == b.Id))?.Cone), StringComparer.Ordinal);
        var ordered = bodies.Values.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < ordered.Length; i++) for (var j = i + 1; j < ordered.Length; j++)
        {
            var a = ordered[i]; var b = ordered[j];
            var intended = model.Contacts.FirstOrDefault(c => PairKey(c.BodyAId, c.BodyBId) == PairKey(a.Id, b.Id));
            var separated = envelopes[a.Id].Disjoint(envelopes[b.Id]);
            if (clearancePolicy == PitchClearancePolicy.Refined && intended is null)
            {
                var proof = PitchClearanceClassifier.Classify(OrientedPitchShapes.Body(model, a), OrientedPitchShapes.Body(model, b), model.RequireCrossComponentClearance, PitchPairScope.BodyPair);
                proofs.Add(proof); checks.Add(new OrientedDomainCheck("pitch-envelope", PairKey(a.Id, b.Id), proof.Verdict, proof.Required, "Exact closed pitch pair " + proof.PairId + ";" + proof.Method + ";" + proof.Relation));
                continue;
            }
            checks.Add(new OrientedDomainCheck("pitch-envelope", PairKey(a.Id, b.Id), separated || intended is not null ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Inconclusive,
                model.RequireCrossComponentClearance, "AABB A=" + Describe(envelopes[a.Id]) + ";B=" + Describe(envelopes[b.Id]) + ";" +
                (separated ? "strictly separated" : intended is not null ? "intended contact separately proven by exact surface geometry (no wholesale pair exemption)" : "overlap is inconclusive, not a physical collision finding")));
        }
        foreach (var keepOut in model.KeepOuts)
        {
            Check("keep-out", keepOut.Id, keepOut.Envelope.IsOrdered, "World-space exact bounded AABB.");
            if (clearancePolicy == PitchClearancePolicy.Refined)
            {
                if (!keepOut.Envelope.IsOrdered) continue;
                foreach (var body in ordered)
                {
                    var proof = PitchClearanceClassifier.Classify(OrientedPitchShapes.Body(model, body), PitchShape.Aabb("keep-out/" + keepOut.Id, keepOut.Envelope), true, PitchPairScope.WorldKeepOut);
                    proofs.Add(proof); checks.Add(new OrientedDomainCheck("keep-out", keepOut.Id + "/" + body.Id, proof.Verdict, true, "Exact closed pitch pair " + proof.PairId + ";" + proof.Method + ";" + proof.Relation));
                }
                continue;
            }
            foreach (var body in ordered) checks.Add(new OrientedDomainCheck("keep-out", keepOut.Id + "/" + body.Id,
                envelopes[body.Id].Disjoint(keepOut.Envelope) ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Inconclusive, true,
                "Declared pitch-envelope/keep-out separation; overlapping boxes do not establish solid collision."));
        }
        checks.Add(new OrientedDomainCheck("presentation", "renderer", OrientedCheckVerdict.NotPerformed, false, "Mechanical operation does not certify pixels, tessellation or camera."));
        checks.Add(new OrientedDomainCheck("physical", "tooth-shaft-bearing-dynamics-manufacturing", OrientedCheckVerdict.NotPerformed, false, "No tooth/shaft/bearing solids, assembly tooth phase, torque or manufacturing certificate."));
        return new OrientedValidation(checks, solved.Diagnostics, proofs, clearancePolicy);
    }

    public static Rational VelocityTransfer(ExactVector3 positiveA, ExactVector3 positiveB, ExactVector3 generator)
    {
        return MechanicalConnectionPredicates.VelocityTransfer(positiveA, positiveB, generator);
    }

    public static bool SameSolution(KinematicSolution a, KinematicSolution b) => a.RootDofId == b.RootDofId && a.States.Count == b.States.Count &&
        a.States.Zip(b.States, (x, y) => x.DofId == y.DofId && x.Coefficient == y.Coefficient && x.PhaseOffset == y.PhaseOffset).All(equal => equal);
    private static string Describe(ExactEnvelope3 e) => e.Min + ".." + e.Max;
    internal static bool Unique(IEnumerable<string> values) { var all = values.ToArray(); return all.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 256) && all.Distinct(StringComparer.Ordinal).Count() == all.Length; }
    private static string PairKey(string a, string b) => StringComparer.Ordinal.Compare(a, b) < 0 ? a + "|" + b : b + "|" + a;
}
