using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Shared local material relations, independent of the global driving function.</summary>
internal static class LocalTransmissionRelations
{
    internal static ExactAffineRelation WormInputPhase(WormDriveTransmissionDefinition device, WormDriveCompatibilityResult local)
    {
        var coefficient = device.Worm.Starts * local.Epsilon!.Value;
        return new(coefficient, -coefficient * device.InputReferenceTurns.Value);
    }
    internal static ExactAffineRelation WormWheelPhase(WormDriveTransmissionDefinition device, WormDriveCompatibilityResult local)
    {
        var coefficient = -device.Worm.Handedness * local.Sigma!.Value * device.SelectedWheel!.ToothCount;
        return new(coefficient, -coefficient * device.OutputReferenceTurns.Value);
    }
    internal static ExactAffineRelation BeltTravelPiCoefficient(OpenBeltTransmissionDefinition device, OpenBeltCompatibilityResult local)
    {
        var coefficient = -2 * device.InputPitchRadius.Value * local.InputAxisRouteSign!.Value;
        return new(coefficient, -coefficient * device.InputReferenceTurns.Value);
    }
}
