using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;
using Xunit;

namespace GearInvest.Tests;

public sealed class CarrierTests
{
    private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
    [Theory]
    [InlineData(100, 10, false, false, false)] [InlineData(42, 18, false, false, false)]
    [InlineData(100, 10, true, false, false)] [InlineData(42, 18, true, true, false)]
    [InlineData(100, 10, false, false, true)] [InlineData(42, 18, true, true, true)]
    public void PublicCreationFreshPersistenceAndIndependentWorldRelativeOracle(int ns, int np, bool prefix, bool reverse, bool phases)
    {
        var d = CarrierExample.Example.Create(ns, np, prefix, reverse, phases);
        var final = sdk.TryFinalizeCarrier(d);
        Assert.True(final.IsFinalized, string.Join(";", final.Analysis.Checks.Where(c => c.Verdict != OrientedCheckVerdict.Pass).Select(c => c.Domain + ":" + c.Detail)));
        var a = sdk.ReadCarrierArtifact(final.Artifact!.Bytes);
        Assert.Equal(a.Bytes, sdk.RebuildCarrierArtifact(a).Bytes);
        Assert.Equal(a.Bytes, sdk.RebuildCarrierReplay(sdk.ExportCarrierReplay(a)).Bytes);
        Assert.Equal(sdk.WriteCarrierDraft(d), sdk.WriteCarrierDraft(sdk.ReadCarrierDraft(sdk.WriteCarrierDraft(d))));
        Assert.Equal(prefix ? 4 : 3, a.Analysis.Shafts.Count); Assert.Equal(prefix ? 5 : 3, a.Analysis.PoseNodes.Count(n => n.BodyId is not null));
        var roots = new[] { Rational.Zero, new Rational(1, 4), new Rational(-1, 4), new Rational(2, 7), new Rational(BigInteger.Pow(10, 60)) + new Rational(1, 4) };
        foreach (var u in roots.Concat(Enumerable.Reverse(roots)))
        {
            var c = prefix ? -u / 2 : u; var physicalSun = phases ? new Rational(3, 8) : 0;
            var pPhysical = new Rational(ns + np, np)*c - new Rational(ns, np)*physicalSun;
            var pWorld = (reverse ? -pPhysical : pPhysical) - (phases ? new Rational(1, 4) : 0);
            var eval = sdk.EvaluateCarrier(a.Analysis, ExactQuantity.Turns(u)); var p = eval.Shafts.Single(s => s.ShaftId == "planet");
            Assert.Equal(c, eval.CarrierCommonTurns); Assert.Equal(pWorld, p.WorldTurns); Assert.Equal(pWorld - (reverse ? -c : c), p.CarrierRelativeTurns);
            Assert.Equal(0, ns*(physicalSun-c) + np*(eval.PlanetBodyCommonTurns-c));
            var display = sdk.DisplayCarrier(a.Analysis, ExactQuantity.Turns(u)); Assert.True(display.IsAvailable);
            var cm = Mod(c); var pm = Mod(pPhysical); var m = display.Matrices["planet-body"];
            Near((ns+np)/2.0*Math.Cos(2*Math.PI*cm), m[12]); Near((ns+np)/2.0*Math.Sin(2*Math.PI*cm), m[13]);
            Near(Math.Cos(2*Math.PI*pm), m[0]); Near(Math.Sin(2*Math.PI*pm), m[1]);
            Assert.Equal(prefix ? 20 : 0, m[14]);
        }
    }
    [Fact]
    public void IndependentReversedInputAndSunAxesWithQuarterZeroRaysRetainWorldMotion()
    {
        var d = CarrierExample.Example.Create();
        var inputFrame = new OrientedFrame(default, ExactVector3.UnitX, -ExactVector3.UnitY, -ExactVector3.UnitZ);
        var source = new MechanicalDraft(new MechanicalDefinition("carrier", new[] { new OrientedShaft("carrier", inputFrame, true) },
            Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance));
        var sun = new OrientedShaft("sun", new OrientedFrame(default, ExactVector3.UnitY, ExactVector3.UnitX, -ExactVector3.UnitZ));
        var planet = new CarrierLocalShaft("planet", "carrier", new OrientedFrame(new ExactVector3(55,0,0), ExactVector3.UnitY, -ExactVector3.UnitX, ExactVector3.UnitZ));
        var request = new CarrierDefinition(source,d.SourceMapping,"carrier",d.PlaneMm,sun,planet,100,10,d.Module,ExactQuantity.Turns(0),
            ExactQuantity.Turns(new Rational(1,4)),ExactQuantity.Turns(new Rational(-1,4)),ExactQuantity.Turns(0),ExactQuantity.Turns(0),0,d.OutputPort);
        var a = sdk.TryFinalizeCarrier(request); Assert.True(a.IsFinalized);
        var u = new Rational(-1,4); var e = sdk.EvaluateCarrier(a.Analysis,ExactQuantity.Turns(u));
        Assert.Equal(-u,e.CarrierCommonTurns); Assert.Equal(new Rational(5,2),e.Shafts.Single(x=>x.ShaftId=="planet").WorldTurns);
        var m = sdk.DisplayCarrier(a.Analysis,ExactQuantity.Turns(u)).Matrices["planet-body"];
        Near(0,m[12]);Near(55,m[13]);Near(0,m[0]);Near(-1,m[1]);
        Assert.Equal(a.Artifact!.Bytes,sdk.ReadCarrierArtifact(a.Artifact.Bytes).Bytes);
    }
    [Fact]
    public void NonzeroIntegerContactRegistrationAndDisplayFailureKeepExactLawSeparate()
    {
        var d = CarrierExample.Example.Create();
        var request = new CarrierDefinition(d.Source,d.SourceMapping,d.CarrierShaftId,d.PlaneMm,d.SunShaft,d.PlanetShaft,100,10,d.Module,
            d.CarrierReference,d.SunReference,ExactQuantity.Turns(new Rational(1,2)),d.SunMount,d.PlanetMount,5,d.OutputPort);
        var final = sdk.TryFinalizeCarrier(request); Assert.True(final.IsFinalized);
        Assert.Equal(new Rational(13,4),sdk.EvaluateCarrier(final.Analysis,ExactQuantity.Turns(new Rational(1,4))).Shafts.Single(s=>s.ShaftId=="planet").WorldTurns);
        var far = CarrierExample.Example.Create(relocation:OrientedFrame.Identity.At(new ExactVector3(100000001,0,0)));
        var farResult = sdk.TryFinalizeCarrier(far); Assert.True(farResult.IsFinalized);
        Assert.Equal(new Rational(11,4),sdk.EvaluateCarrier(farResult.Analysis,ExactQuantity.Turns(new Rational(1,4))).PlanetBodyCommonTurns);
        Assert.False(sdk.DisplayCarrier(farResult.Analysis,ExactQuantity.Turns(new Rational(1,4))).IsAvailable);
    }
    [Fact]
    public void RigidSignedPlaneRelocationAndPortCalibrationDoNotDoubleCarrier()
    {
        var relocation = new OrientedFrame(new ExactVector3(7, -11, 3), ExactVector3.UnitY, ExactVector3.UnitZ, ExactVector3.UnitX);
        var d = CarrierExample.Example.Create(relocation: relocation); var localPort = new OrientedFrame(new ExactVector3(0,0,4), ExactVector3.UnitY, ExactVector3.UnitX, -ExactVector3.UnitZ);
        d = Copy(d, port: new CarrierOutputPort("p", "planet", localPort, ExactQuantity.Turns(new Rational(2, 7))));
        var a = sdk.TryFinalizeCarrier(d).Artifact!; var e = sdk.EvaluateCarrier(a.Analysis, ExactQuantity.Turns(new Rational(1,4)));
        Assert.Equal(-new Rational(11,4)+new Rational(2,7), e.PortReadoutTurns);
        var pose = sdk.DisplayCarrier(a.Analysis, ExactQuantity.Turns(new Rational(1,4)));
        var m = pose.Matrices["planet-body"]; Near(7,m[12]); Near(-11,m[13]); Near(58,m[14]);
        // Proper pose has world ray -Z; both omitted and duplicate carrier would point sideways.
        Near(0,m[0]); Near(0,m[1]); Near(-1,m[2]); Near(11,pose.Matrices["output-port"][12]);
    }
    [Theory]
    [InlineData("hold")] [InlineData("contact")] [InlineData("phase")] [InlineData("owner")] [InlineData("secondInput")]
    [InlineData("center")] [InlineData("tilt")] [InlineData("units")] [InlineData("radius")] [InlineData("port")]
    [InlineData("prefixPlane")] [InlineData("solids")]
    public void ExplicitRefusalsDoNotMutateOriginal(string kind)
    {
        var d = CarrierExample.Example.Create(prefix: kind == "prefixPlane"); var before = sdk.WriteCarrierDraft(d);
        var f = d.PlanetShaft.FrameInCarrier;
        var bad = Copy(d, held: kind == "hold" ? false : null, contact: kind == "contact" ? false : null,
            planetReference: kind == "phase" ? ExactQuantity.Turns(new Rational(1,7)) : null,
            planet: kind == "owner" ? new CarrierLocalShaft("planet", "missing", f) : kind == "secondInput" ? new CarrierLocalShaft("planet", "carrier", f, true) :
                kind == "center" ? new CarrierLocalShaft("planet", "carrier", f.At(new ExactVector3(54,0,0))) : kind == "tilt" ? new CarrierLocalShaft("planet", "carrier", new OrientedFrame(f.Origin, ExactVector3.UnitZ, ExactVector3.UnitX, ExactVector3.UnitY)) : null,
            module: kind == "units" ? ExactQuantity.Turns(1) : kind == "radius" ? ExactQuantity.Millimeters(2) : null,
            port: kind == "port" ? new CarrierOutputPort("p", "carrier", OrientedFrame.Identity, ExactQuantity.Turns(0)) : null,
            plane: kind == "prefixPlane" ? OrientedFrame.Identity : null, required: kind == "solids" ? new[] { "SweptSolids" } : null);
        var result = sdk.TryFinalizeCarrier(bad); Assert.False(result.IsFinalized); Assert.Null(result.Artifact);
        Assert.Equal(before, sdk.WriteCarrierDraft(d)); Assert.Equal(sdk.WriteCarrierDraft(bad), sdk.WriteCarrierDraft(sdk.ReadCarrierDraft(sdk.WriteCarrierDraft(bad))));
    }
    [Theory]
    [InlineData("law")] [InlineData("phase")] [InlineData("owner")] [InlineData("teeth")] [InlineData("parent")]
    public void CorrectlyRehashedFalseDerivedDataStillFailsFreshReconstruction(string kind)
    {
        var a = sdk.TryFinalizeCarrier(CarrierExample.Example.Create()).Artifact!;
        var o = JsonNode.Parse(a.Bytes)!.AsObject(); var compiled = o["compiled"]!;
        var nodes = compiled["poseNodes"]!.AsArray(); var planet = nodes.Single(n => n!["id"]!.GetValue<string>() == "planet-body")!;
        if (kind == "law") compiled["planetCommon"]!["q"]!["numerator"] = "12";
        if (kind == "phase") compiled["planetCommon"]!["p"]!["numerator"] = "1";
        if (kind == "owner") planet["shaftId"] = "sun";
        if (kind == "teeth") planet["teeth"] = "11";
        if (kind == "parent") nodes.Single(n => n!["id"]!.GetValue<string>() == "planet-shaft")!["parentId"] = "sun-shaft";
        var copy = o.DeepClone().AsObject(); copy.Remove("artifactHash"); o["artifactHash"] = CanonicalOrientedJson.Hash(Encode(copy));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadCarrierArtifact(Encode(o)));
    }
    [Fact]
    public void OldReadersMalformedBoundsAndCreateOnlyStorageKeepOriginal()
    {
        var a = sdk.TryFinalizeCarrier(CarrierExample.Example.Create(prefix: true)).Artifact!;
        Assert.Throws<ArtifactFormatException>(() => sdk.ImportMechanicalArtifact(a.Bytes));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadMechanicalDraft(sdk.WriteCarrierDraft(a.Request)));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadCarrierDraft(Encoding.UTF8.GetBytes("{\"format\":\"x\",\"format\":\"y\"}")));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadCarrierArtifact(new byte[CarrierProfile.MaxDocumentBytes+1]));
        Assert.Throws<ArgumentException>(() => sdk.EvaluateCarrier(a.Analysis, ExactQuantity.Millimeters(1)));
        Assert.Throws<ArgumentException>(() => sdk.EvaluateCarrier(a.Analysis, ExactQuantity.FromCanonical(QuantityKind.AngularPosition, new Rational(BigInteger.Pow(10,128)))));
        var path = Path.Combine(Path.GetTempPath(), "carrier-"+Guid.NewGuid().ToString("N")+".json");
        sdk.SaveCarrierArtifact(a, path); Assert.Throws<IOException>(() => sdk.SaveCarrierArtifact(a,path));
        Assert.Equal(a.Bytes,sdk.LoadCarrierArtifact(path).Bytes); // Small create-only artifact retained; no cleanup outside owner authority.
        var original = a.Request.Source.OriginalArtifactBytes!; Assert.Equal(original, sdk.TryFinalizeMechanicalDraft(a.Request.Source,a.Request.Source.ImportedProfile!).ArtifactBytes);
    }
    internal static CarrierDefinition Copy(CarrierDefinition d, bool? held = null, bool? contact = null, ExactQuantity? planetReference = null,
        CarrierLocalShaft? planet = null, ExactQuantity? module = null, CarrierOutputPort? port = null, OrientedFrame? plane = null, string[]? required = null) =>
        new(d.Source,d.SourceMapping,d.CarrierShaftId,plane??d.PlaneMm,d.SunShaft,planet??d.PlanetShaft,d.SunTeeth,d.PlanetTeeth,module??d.Module,
            d.CarrierReference,d.SunReference,planetReference??d.PlanetReference,d.SunMount,d.PlanetMount,d.ToothRegistration,port??d.OutputPort,
            d.CarrierBodyId,d.SunBodyId,d.PlanetBodyId,held??d.SunHeld,contact??d.ContactPresent,required??d.RequiredValidationDomains.ToArray());
    private static byte[] Encode(JsonNode node) { using var m = new MemoryStream(); using (var w = new System.Text.Json.Utf8JsonWriter(m)) node.WriteTo(w); return m.ToArray(); }
    private static double Mod(Rational r) => (double)((r.Numerator % r.Denominator+r.Denominator)%r.Denominator)/(double)r.Denominator;
    private static void Near(double expected,double actual) => Assert.InRange(actual,expected-1e-8,expected+1e-8);
}
