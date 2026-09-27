namespace GearInvest.Core;

/// <summary>Lowered scalar mathematics, not a gear kind or a certificate of contact geometry.</summary>
public sealed class ScalarAffineCoupling
{
    public ScalarAffineCoupling(string id, string driverDofId, string drivenDofId, Rational transfer, Rational phaseOffset = default)
    { Id = OrientedIds.Require(id); DriverDofId = OrientedIds.Require(driverDofId); DrivenDofId = OrientedIds.Require(drivenDofId); Transfer = transfer; PhaseOffset = phaseOffset; }
    public string Id { get; }
    public string DriverDofId { get; }
    public string DrivenDofId { get; }
    public Rational Transfer { get; }
    public Rational PhaseOffset { get; }
}
