using System;
using System.Collections.Generic;
using System.Numerics;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Internal binding shared by the two supported serial profiles; not a topology synthesizer.</summary>
internal static class SerialRouteBinding
{
    internal static LowLevelMechanicalSpecification Build(IReadOnlyList<IReadOnlyList<GearRouteAssignment>> paths, BigInteger scale, string profile,
        Func<int, int, int, string> role, Func<string, string> dof, Func<string, string> axis,
        Func<int, int, int, string> body, Func<int, int, string> mesh, Func<int, int, string> contact)
    {
        if (paths.Count != 2 && paths.Count != 3) throw new ArgumentException("Only the bounded one- and two-compound profiles are supported.");
        var dofs = new List<RotationalDof>(); var axes = new List<SpatialAxis>(); var bodies = new List<SpatialBody>();
        var couplings = new List<ExternalGearCoupling>(); var contacts = new List<SpatialContact>();
        for (int leg = 0; leg < paths.Count; leg++)
        {
            var path = paths[leg];
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i]; var r = role(leg, i, path.Count);
                // Only each explicit compound boundary shares its axis/DOF; never infer sharing from velocity or XY.
                if (!(leg > 0 && i == 0)) { dofs.Add(new RotationalDof(dof(r), r == "input")); axes.Add(new SpatialAxis(axis(r), a.Position.X, a.Position.Y)); }
                bodies.Add(new SpatialBody(body(leg, i, path.Count), SpatialBodyKind.Gear, axis(r), dof(r), leg, a.Teeth, a.Teeth * scale));
                if (i == path.Count - 1) continue;
                couplings.Add(new ExternalGearCoupling(mesh(leg, i), dof(r), dof(role(leg, i + 1, path.Count)), a.Teeth, path[i + 1].Teeth));
                contacts.Add(new SpatialContact(contact(leg, i), SpatialContactKind.ExternalGearMesh, mesh(leg, i), body(leg, i, path.Count), body(leg, i + 1, path.Count)));
            }
        }
        return new LowLevelMechanicalSpecification(profile, new KinematicSpecification(dof("input"), dofs, couplings), new SpatialMechanism(axes, bodies, contacts));
    }
}
