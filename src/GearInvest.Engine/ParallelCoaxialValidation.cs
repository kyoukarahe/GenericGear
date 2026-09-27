using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Explicit-profile ideal geometry. Motion still comes only from existing admitted contact constraints.</summary>
internal static class ParallelCoaxialValidation
{
    internal static void Validate(MechanicalDefinition d, IReadOnlyList<MechanicalAnalyzedEdge> edges,
        List<OrientedDomainCheck> checks, List<MechanicalDiagnostic> diagnostics)
    {
        var layout = d.CoaxialLayout!;
        var shafts = d.Shafts.ToDictionary(s => s.Id, StringComparer.Ordinal);
        void Check(string code, string subject, bool pass, params MechanicalReference[] related)
        {
            checks.Add(new OrientedDomainCheck("parallel-coaxial-placement", subject, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, code));
            if (!pass) diagnostics.Add(new MechanicalDiagnostic(code, "Spatial", related: related, scope: ParallelCoaxialLayout.Profile));
        }
        MechanicalReference S(string id) => new("Shaft", id);
        MechanicalReference B(string id) => new("Body", id);
        Check("CoaxialClearancePolicyRequired", "policy", d.ClearancePolicy == ParallelCoaxialLayout.ClearancePolicy && d.RequireCrossComponentClearance);
        Check("CoaxialSingleInputRequired", "root", d.RootShaftId is not null && shafts.ContainsKey(d.RootShaftId) &&
            d.Shafts.Count(s => s.IsPrescribed) == 1 && shafts[d.RootShaftId].IsPrescribed);
        Check("CoaxialSerialExternalProfileRequired", "contact-domain", d.Shafts.Count >= 2 && d.Contacts.Count == d.Shafts.Count - 1 &&
            d.Connections.Count == 0 && d.Contacts.All(c => c.Kind == OrientedContactKind.ExternalSpur && c.Cone is null));
        foreach (var shaft in d.Shafts)
        {
            Check("CoaxialParallelAxisRequired", shaft.Id, shaft.Frame.IsProperCardinal && shaft.Frame.Z.Cross(layout.PlaneFrame.Z) == default, S(shaft.Id));
            Check("CoaxialUnrealizedRotor", shaft.Id + "/body", d.Bodies.Any(b => b.ShaftId == shaft.Id), S(shaft.Id));
            var degree = edges.Count(e => e.Kind == "Contact" && (e.ShaftAId == shaft.Id || e.ShaftBId == shaft.Id));
            Check("CoaxialSerialDegreeRequired", shaft.Id + "/degree", degree >= 1 && degree <= 2 && (shaft.Id != d.RootShaftId || degree == 1), S(shaft.Id));
        }
        var membership = new Dictionary<string, string>(StringComparer.Ordinal);
        var admittedGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in layout.Groups)
        {
            var exists = group.ShaftIds.All(shafts.ContainsKey);
            var distinctGroups = group.ShaftIds.All(id => !membership.ContainsKey(id));
            Check("CoaxialUnknownRotor", group.Id, exists, group.ShaftIds.Select(S).ToArray());
            Check("CoaxialConflictingGroups", group.Id + "/membership", distinctGroups, group.ShaftIds.Select(S).ToArray());
            var same = exists && group.ShaftIds.All(id => shafts[group.ShaftIds[0]].SameLine(shafts[id]));
            Check("CoaxialLineMismatch", group.Id + "/line", same, group.ShaftIds.Select(S).ToArray());
            if (exists && distinctGroups && same) admittedGroups.Add(group.Id);
            foreach (var id in group.ShaftIds) if (!membership.ContainsKey(id)) membership.Add(id, group.Id);
        }
        for (var i = 0; i < d.Shafts.Count; i++) for (var j = i + 1; j < d.Shafts.Count; j++)
        {
            var a = d.Shafts[i]; var b = d.Shafts[j];
            if (!a.SameLine(b)) continue;
            var declared = membership.TryGetValue(a.Id, out var ga) && membership.TryGetValue(b.Id, out var gb) && ga == gb && admittedGroups.Contains(ga);
            Check(DiagnosticCodes.CoincidentAxes, a.Id + "|" + b.Id, declared, S(a.Id), S(b.Id));
        }
        foreach (var body in d.Bodies)
        {
            Check("CoaxialBodyPitchMismatch", body.Id, body.Kind == OrientedGearKind.PlanarSpur && body.Teeth > 0 &&
                body.OuterPitchRadius == layout.PitchRadiusPerTooth * body.Teeth, B(body.Id));
            Check("CoaxialLayerMismatch", body.Id + "/layer", layout.IsLayer(body.MountingFrame.Origin), B(body.Id));
            Check(DiagnosticCodes.DanglingBody, body.Id + "/contact", d.Contacts.Any(c => c.BodyAId == body.Id || c.BodyBId == body.Id), B(body.Id));
        }
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contact in d.Contacts)
        {
            var pair = OrientedGoalKeys.Pack(new[] { contact.BodyAId, contact.BodyBId }.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Check("CoaxialDuplicateContact", contact.Id, pairs.Add(pair), new MechanicalReference("Contact", contact.Id));
        }
        for (var i = 0; i < d.Bodies.Count; i++) for (var j = i + 1; j < d.Bodies.Count; j++)
        {
            var a = d.Bodies[i]; var b = d.Bodies[j]; var pair = a.Id + "|" + b.Id;
            var delta = b.MountingFrame.Origin - a.MountingFrame.Origin;
            // Distinct exact planes separate ideal pitch disks only. No thickness/shaft-solid assertion.
            if (delta.Dot(layout.PlaneFrame.Z) != 0)
            {
                checks.Add(new OrientedDomainCheck("ideal-pitch-plane-clearance", pair, OrientedCheckVerdict.Pass, true, "Distinct exact mounting planes; finite solids not performed."));
                continue;
            }
            var intended = d.Contacts.Any(c => (c.BodyAId == a.Id && c.BodyBId == b.Id || c.BodyAId == b.Id && c.BodyBId == a.Id) &&
                edges.Single(e => e.Kind == "Contact" && e.Id == c.Id).IsAdmitted);
            var pass = intended || a.OuterPitchRadius > 0 && b.OuterPitchRadius > 0 && SpatialValidator.NonPenetratingPitchDisks(delta.LengthSquared, a.OuterPitchRadius, b.OuterPitchRadius);
            Check("CoaxialPitchPenetration", pair, pass, B(a.Id), B(b.Id));
        }
        foreach (var keepOut in d.KeepOuts)
        {
            checks.Add(new OrientedDomainCheck("coaxial-keep-out", keepOut.Id, OrientedCheckVerdict.NotPerformed, true, "Solid keep-out inspection is outside the ideal parallel-plane profile."));
            diagnostics.Add(new MechanicalDiagnostic("UnresolvedRequiredValidation", "Spatial", related: new[] { new MechanicalReference("KeepOut", keepOut.Id) }, complete: false));
        }
    }
}
