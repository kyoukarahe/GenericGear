using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Read original validated graph geometry; never infer a cone's direction from positive shaft coordinates.</summary>
public static class OrientedPitchShapes
{
    public static PitchShape Body(MechanicalDefinition definition, OrientedGearBody body)
    {
        if (!definition.Bodies.Contains(body)) throw new ArgumentException("Body reference must belong to the current definition.");
        if (body.Kind == OrientedGearKind.PlanarSpur) return Shape(body, null, false);
        var contact = definition.Contacts.Single(c => c.Cone is not null && (c.BodyAId == body.Id || c.BodyBId == body.Id));
        return Shape(body, contact.Cone, contact.BodyAId == body.Id);
    }
    public static PitchShape Body(OrientedTwoOutputMechanism model, OrientedGearBody body) => Body((IOrientedGraph)model, body);
    internal static PitchShape Body(IOrientedGraph model, OrientedGearBody body)
    {
        if (!model.Bodies.Contains(body)) throw new ArgumentException("Body reference must belong to the original graph.");
        if (body.Kind == OrientedGearKind.PlanarSpur) return Shape(body, null, false);
        var contact = model.Contacts.Single(c => c.Cone is not null && (c.BodyAId == body.Id || c.BodyBId == body.Id));
        return Shape(body, contact.Cone, contact.BodyAId == body.Id);
    }
    private static PitchShape Shape(OrientedGearBody body, RightAnglePitchCone? data, bool endpointA)
    {
        if (body.Kind == OrientedGearKind.PlanarSpur) return PitchShape.Disk("body/" + body.Id, body.MountingFrame.Origin, body.MountingFrame.Z, body.OuterPitchRadius);
        var cone = data!; var outward = endpointA ? cone.OutwardA : cone.OutwardB;
        var displacement = body.MountingFrame.Origin - cone.Apex;
        if (displacement.Cross(outward) != ExactVector3.Zero) throw new ArgumentException("Cone outer center is not on its physical axis.");
        return PitchShape.LateralCone("body/" + body.Id, cone.Apex, outward, displacement.Dot(outward), body.OuterPitchRadius, cone.InnerParameter);
    }
}
