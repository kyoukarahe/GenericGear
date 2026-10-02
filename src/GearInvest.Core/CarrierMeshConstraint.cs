using System;

namespace GearInvest.Core;

/// <summary>Exact scalar lowering of Ns*(sun-carrier)+Np*(planet-carrier)=registration.
/// Physical contact, grounded ownership and frames must be admitted separately.</summary>
public sealed class CarrierMeshConstraint
{
    public CarrierMeshConstraint(int sunTeeth, int planetTeeth, Rational registration)
    {
        if (sunTeeth < 1 || sunTeeth > 4096 || planetTeeth < 1 || planetTeeth > 4096 || registration.Denominator != 1)
            throw new ArgumentException("Positive bounded teeth and integer registration required.");
        SunTeeth = sunTeeth; PlanetTeeth = planetTeeth; Registration = registration;
    }
    public int SunTeeth { get; }
    public int PlanetTeeth { get; }
    public Rational Registration { get; }
    public Rational Residual(Rational sun, Rational carrier, Rational planet) =>
        SunTeeth * (sun - carrier) + PlanetTeeth * (planet - carrier) - Registration;
    public ExactAffineRelation ResolvePlanet(ExactAffineRelation carrier, ExactAffineRelation sun)
    {
        var a = new Rational(SunTeeth + PlanetTeeth, PlanetTeeth);
        var b = new Rational(SunTeeth, PlanetTeeth);
        return new ExactAffineRelation(a * carrier.Coefficient - b * sun.Coefficient,
            a * carrier.Phase - b * sun.Phase + Registration / PlanetTeeth);
    }
}
