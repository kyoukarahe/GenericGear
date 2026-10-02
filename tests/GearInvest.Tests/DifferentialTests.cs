using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using Xunit;
using Example = GearInvest.DifferentialExample.Example;

namespace GearInvest.Tests;

public sealed class DifferentialTests
{
    private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
    private static DifferentialInputSnapshot Input(params (string id, Rational value)[] values) => new(values.Select(p => new KeyValuePair<string, ExactQuantity>(p.id, ExactQuantity.Turns(p.value))));
    private static DifferentialBoundary B(string id, string port, Rational value) => new(id, port, ExactQuantity.Turns(value));
    private DifferentialAnalysis Prepare(DifferentialDefinition d, params string[] ports) => sdk.PrepareDifferential(new(d, ports));

    [Theory]
    [InlineData(0, 1, 11, 4, 5, 2)]
    [InlineData(1, 10, 7, 4, 3, 2)]
    [InlineData(1, 4, 1, 4, 0, 1)]
    [InlineData(-1, 5, 19, 4, 9, 2)]
    public void IndependentTwoInputOracleAndBodyMotion(int sn, int sd, int pn, int pd, int rn, int rd)
    {
        var a = Prepare(Example.Create(), "sun-port", "carrier-port"); Assert.True(a.CanExport); Assert.Equal(3, a.Reduction!.Rank);
        var input = Input(("carrier-port", new(1, 4)), ("sun-port", new(sn, sd))); var e = sdk.EvaluateDifferential(a, input);
        Assert.Equal(new Rational(pn, pd), e.Coordinates["planet"]); Assert.Equal(new Rational(rn, rd), e.PlanetRelativeTurns);
        var m = sdk.DisplayDifferential(a, input).Matrices["planet-body"];
        Near(0, m[12]); Near(55, m[13]); Near(Math.Cos(2 * Math.PI * Mod(new(pn, pd))), m[0]); Near(Math.Sin(2 * Math.PI * Mod(new(pn, pd))), m[1]);
    }
    [Fact]
    public void EachIndependentBasisResolvesTheThirdCoordinateWithoutSingleRootCollapse()
    {
        var d = Example.Create(); var a = Prepare(d, "carrier-port", "sun-port");
        Assert.Equal(-1, sdk.EvaluateDifferential(a, Input(("sun-port", new(1, 10)), ("carrier-port", 0))).Coordinates["planet"]);
        var alternate = Prepare(d, "sun-port", "planet-port"); var e = sdk.EvaluateDifferential(alternate, Input(("sun-port", 0), ("planet-port", 1)));
        Assert.Equal(new Rational(1, 11), e.Coordinates["carrier"]); Assert.Equal(new Rational(10, 11), e.PlanetRelativeTurns);
        var third = Prepare(d, "carrier-port", "planet-port"); Assert.Equal(new Rational(1, 10), sdk.EvaluateDifferential(third, Input(("carrier-port", new(1, 4)), ("planet-port", new(7, 4)))).Coordinates["sun"]);
        Assert.NotEqual(a.Request.RequestId, alternate.Request.RequestId); Assert.Equal(a.Request.Definition.DefinitionId, alternate.Request.Definition.DefinitionId);
    }
    [Fact]
    public void EqualTeethRankPartialKnownConsistentAndInconsistentRedundancy()
    {
        var d = Example.Create(24, 24); var a = Prepare(d, "sun-port", "planet-port");
        Assert.Equal(new Rational(1, 12), sdk.EvaluateDifferential(a, Input(("sun-port", new(1, 3)), ("planet-port", new(-1, 6)))).Coordinates["carrier"]);
        Assert.Equal(0, sdk.EvaluateDifferential(a, Input(("sun-port", 1), ("planet-port", -1))).Coordinates["carrier"]);
        var partial = sdk.AnalyzeDifferentialBoundary(d, new[] { B("known-sun", "sun-port", new(1, 3)) });
        Assert.Equal(MechanicalDeterminacy.UndrivenRelativeMotion, partial.Status); Assert.Equal(2, partial.Reduction!.Rank);
        Assert.True(partial.Coordinates.Single(c => c.ShaftId == "sun").IsKnown); Assert.False(partial.Coordinates.Single(c => c.ShaftId == "planet").IsKnown);
        Assert.Single(sdk.EvaluateDifferential(partial, Input()).Coordinates); Assert.False(sdk.DisplayDifferential(partial, Input()).IsAvailable); Assert.Empty(partial.PoseNodes);
        var good = new[] { B("s", "sun-port", 1), B("p", "planet-port", -1), B("c", "carrier-port", 0) };
        Assert.True(sdk.AnalyzeDifferentialBoundary(d, good).IsFullyDetermined);
        var bad = sdk.AnalyzeDifferentialBoundary(d, new[] { B("s", "sun-port", 0), B("p", "planet-port", 0), B("c", "carrier-port", 1) });
        Assert.Equal(MechanicalDeterminacy.InconsistentConstraints, bad.Status); Assert.Empty(bad.Coordinates); Assert.Empty(bad.PoseNodes);
        Assert.Contains(bad.Diagnostics[0].Related, r => r.Id == "boundary/c"); Assert.Throws<ArgumentException>(() => sdk.EvaluateDifferential(bad, Input()));
        Assert.Equal(sdk.WriteDifferentialAnalysis(sdk.AnalyzeDifferentialBoundary(d, good)), sdk.WriteDifferentialAnalysis(sdk.AnalyzeDifferentialBoundary(d, Enumerable.Reverse(good))));
    }
    [Fact]
    public void StaticHoldAndAliasRankDoNotPretendToBeTwoIndependentInputs()
    {
        var d = Example.Create(holdSun: true); var a = Prepare(d, "carrier-port"); Assert.True(a.CanExport);
        Assert.Equal(new Rational(11, 4), sdk.EvaluateDifferential(a, Input(("carrier-port", new(1, 4)))).Coordinates["planet"]);
        var old = sdk.AnalyzeCarrier(CarrierExample.Example.Create());
        foreach (var root in new[] { new Rational(-1, 3), new Rational(0), new Rational(1, 4) })
        { var e = sdk.EvaluateDifferential(a, Input(("carrier-port", root))); Assert.Equal(sdk.EvaluateCarrier(old, ExactQuantity.Turns(root)).PlanetBodyCommonTurns, e.PlanetCommonTurns); }
        Assert.Equal(MechanicalDeterminacy.InconsistentWithPrescribedInput, Prepare(d, "sun-port", "carrier-port").Status);
        var free = Example.Create(); var alias = Copy(free, ports: free.Ports.Concat(new[] { new CarrierOutputPort("sun-alias", "sun", OrientedFrame.Identity, ExactQuantity.Turns(0)) }));
        var dup = Prepare(alias, "sun-port", "sun-alias"); Assert.False(dup.CanExport); Assert.Equal(2, dup.Reduction!.Rank);
        var consistent = sdk.AnalyzeDifferentialBoundary(alias, new[] { B("a", "sun-port", 0), B("b", "sun-alias", 0) });
        Assert.Equal(MechanicalDeterminacy.UndrivenRelativeMotion, consistent.Status); Assert.Equal(2, consistent.Reduction!.Rank);
        Assert.Equal(MechanicalDeterminacy.InconsistentConstraints, sdk.AnalyzeDifferentialBoundary(alias, new[] { B("a", "sun-port", 0), B("b", "sun-alias", 1) }).Status);
    }
    [Fact]
    public void ActualPrefixAndIndependentSunMeetFractionOracleAndOldE2a()
    {
        var d = Example.Create(42, 18, prefix: true); var a = Prepare(d, "drive-port", "sun-port"); Assert.True(a.CanExport);
        var e = sdk.EvaluateDifferential(a, Input(("drive-port", new(1, 13)), ("sun-port", new(1, 7))));
        Assert.Equal(new Rational(-1, 26), e.Coordinates["carrier"]); Assert.Equal(new Rational(-6, 13), e.Coordinates["planet"]); Assert.Equal(new Rational(-11, 26), e.PlanetRelativeTurns);
        Assert.Equal(5, a.PoseNodes.Count(n => n.BodyId is not null)); Assert.Equal(4, a.Reduction!.Rank);
        var old = sdk.AnalyzeCarrier(CarrierExample.Example.Create(42, 18, prefix: true));
        Assert.Equal(sdk.EvaluateCarrier(old, ExactQuantity.Turns(new(1, 13))).PlanetBodyCommonTurns, sdk.EvaluateDifferential(a, Input(("drive-port", new(1, 13)), ("sun-port", 0))).PlanetCommonTurns);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SignedReferenceMountRegistrationAndHugeExactInputsKeepWorldRelativeDistinct(bool moved)
    {
        var relocation = moved ? new OrientedFrame(new ExactVector3(17, -29, 11), ExactVector3.UnitY, ExactVector3.UnitZ, ExactVector3.UnitX) : OrientedFrame.Identity;
        var a = Prepare(Example.Create(37, 23, signedReference: true, relocation: relocation), "sun-port", "carrier-port"); Assert.True(a.CanExport);
        var roots = new[] { new Rational(1, 4), new Rational(-4, 7), new Rational(BigInteger.Pow(10, 60)) + new Rational(1, 4) };
        foreach (var c in roots)
        {
            var e = sdk.EvaluateDifferential(a, Input(("carrier-port", c), ("sun-port", new(1, 10))));
            var physical = (1 + 60 * c - 37 * new Rational(9, 40)) / 23; Assert.Equal(physical, e.PlanetCommonTurns);
            Assert.Equal(-physical - new Rational(1, 4), e.Coordinates["planet"]); Assert.Equal(e.Coordinates["planet"] + c, e.PlanetRelativeTurns);
            var display = sdk.DisplayDifferential(a, Input(("carrier-port", c), ("sun-port", new(1, 10)))); Assert.True(display.IsAvailable);
            var pos = relocation.Origin;
            var m = display.Matrices["planet-body"]; var x = 30 * Math.Cos(2 * Math.PI * Mod(c)); var y = 30 * Math.Sin(2 * Math.PI * Mod(c));
            Near(Number(pos.X) + x * Number(relocation.X.X) + y * Number(relocation.Y.X), m[12]);
            Near(Number(pos.Y) + x * Number(relocation.X.Y) + y * Number(relocation.Y.Y), m[13]);
            Near(Number(pos.Z) + x * Number(relocation.X.Z) + y * Number(relocation.Y.Z), m[14]);
        }
    }
    [Fact]
    public void ImmutableInputsCanonicalEnumerationAndStrictAtomicSnapshot()
    {
        var d = Example.Create(); var ports = d.Ports.ToArray(); var one = new DifferentialRequest(Copy(d, ports: Enumerable.Reverse(ports)), new[] { "sun-port", "carrier-port" });
        var two = new DifferentialRequest(d, new[] { "carrier-port", "sun-port" }); Assert.Equal(sdk.WriteDifferentialDraft(two), sdk.WriteDifferentialDraft(one));
        var snapshotValues = new Dictionary<string, ExactQuantity> { ["carrier-port"] = ExactQuantity.Turns(new(1, 4)), ["sun-port"] = ExactQuantity.Turns(0) };
        var snapshot = new DifferentialInputSnapshot(snapshotValues); snapshotValues["sun-port"] = ExactQuantity.Turns(1);
        var a = sdk.PrepareDifferential(two); Assert.Equal(new Rational(11, 4), sdk.EvaluateDifferential(a, snapshot).Coordinates["planet"]);
        Assert.Throws<ArgumentException>(() => sdk.EvaluateDifferential(a, Input(("carrier-port", 0)))); Assert.Throws<ArgumentException>(() => sdk.EvaluateDifferential(a, Input(("unknown", 0), ("sun-port", 0))));
        Assert.Throws<ArgumentException>(() => new DifferentialInputSnapshot(new[] { new KeyValuePair<string, ExactQuantity>("sun-port", ExactQuantity.Millimeters(1)) }));
        Assert.Throws<ArgumentException>(() => Input(("sun-port", new Rational(BigInteger.Pow(10, 128)))));
        var partial = Prepare(d, "sun-port"); Assert.False(partial.CanExport); Assert.Single(sdk.EvaluateDifferential(partial, Input(("sun-port", 0))).Coordinates);
    }
    [Theory]
    [InlineData("owner")] [InlineData("basis")] [InlineData("units")] [InlineData("radius")] [InlineData("contact")] [InlineData("reference")] [InlineData("solids")] [InlineData("hold-owner")] [InlineData("prefix-plane")]
    public void UnsupportedDefinitionsDoNotAcquireACompletePose(string kind)
    {
        var d = Example.Create(prefix: kind == "prefix-plane");
        var bad = Copy(d, ports: kind == "owner" ? new[] { new CarrierOutputPort("unknown", "foreign", OrientedFrame.Identity, ExactQuantity.Turns(0)) } : null,
            module: kind == "units" ? ExactQuantity.Turns(1) : kind == "radius" ? ExactQuantity.Millimeters(2) : null,
            contact: kind == "contact" ? false : null, planetReference: kind == "reference" ? ExactQuantity.Turns(new(1, 11)) : null,
            required: kind == "solids" ? new[] { "SweptSolids" } : null, holds: kind == "hold-owner" ? new[] { new DifferentialHold("bad", "foreign", ExactQuantity.Turns(0)) } : null,
            plane: kind == "prefix-plane" ? OrientedFrame.Identity : null);
        var r = new DifferentialRequest(bad, new[] { kind == "basis" ? "unknown" : kind == "prefix-plane" ? "drive-port" : "carrier-port", "sun-port" });
        var final = sdk.TryFinalizeDifferential(r); Assert.False(final.IsFinalized); Assert.Null(final.Artifact);
        Assert.Equal(sdk.WriteDifferentialDraft(r), sdk.WriteDifferentialDraft(sdk.ReadDifferentialDraft(sdk.WriteDifferentialDraft(r))));
        Assert.Equal(MechanicalDeterminacy.BlockedByInvalidConstraint, sdk.AnalyzeDifferentialBoundary(d, new[] { B("x", "missing", 0) }).Status);
    }
    [Theory]
    [InlineData("q")] [InlineData("b")] [InlineData("row")] [InlineData("K")] [InlineData("hold")] [InlineData("basis")] [InlineData("owner")] [InlineData("parent")] [InlineData("frame")] [InlineData("check")] [InlineData("prefix")]
    public void CorrectlyRehashedCachesCannotOverrideSourceReconstruction(string kind)
    {
        var d = Example.Create(prefix: true); var a = sdk.TryFinalizeDifferential(new(d, new[] { "drive-port", "sun-port" })).Artifact!;
        var o = JsonNode.Parse(a.Bytes)!.AsObject(); var c = o["compiled"]!;
        if (kind == "q") c["planetCommon"]!["q"]!["sun-port"]!["numerator"] = "-8";
        if (kind == "b") c["planetCommon"]!["b"]!["numerator"] = "1";
        if (kind == "row") c["rows"]![0]!["a"]![0]!["numerator"] = "-111";
        if (kind == "K") o["request"]!["definition"]!["toothRegistration"]!["numerator"] = "1";
        if (kind == "hold") o["request"]!["definition"]!["holds"]!.AsArray().Add(new JsonObject { ["id"] = "forged" });
        if (kind == "basis") c["inputPortIds"]![0] = "carrier-port";
        var n = c["poseNodes"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "planet-shaft")!;
        if (kind == "owner") n["shaftId"] = "sun"; if (kind == "parent") n["parentId"] = "sun-shaft";
        if (kind == "frame") n["frame"]!["origin"]![0]!["numerator"] = "500";
        if (kind == "check") c["checks"]![0]!["detail"] = "forged PASS";
        if (kind == "prefix") o["attachedSource"]!["identity"] = new string('0', 64);
        var hashable = o.DeepClone().AsObject(); hashable.Remove("artifactHash"); o["artifactHash"] = CanonicalOrientedJson.Hash(Encode(hashable));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadDifferentialArtifact(Encode(o)));
    }
    [Fact]
    public void RehashedToothChangeWithValidSourceIdentitiesCannotRetainOldCompiledMechanics()
    {
        var oldArtifact = sdk.TryFinalizeDifferential(new(Example.Create(42, 18, prefix: true), new[] { "drive-port", "sun-port" })).Artifact!;
        // Keep the same pitch-center distance and valid reference, but change the actual contact ratio.
        var changedArtifact = sdk.TryFinalizeDifferential(new(Example.Create(43, 17, prefix: true), new[] { "drive-port", "sun-port" })).Artifact!;
        Assert.Equal(changedArtifact.Bytes, sdk.ReadDifferentialArtifact(changedArtifact.Bytes).Bytes);
        var forged = JsonNode.Parse(oldArtifact.Bytes)!.AsObject();
        var changed = JsonNode.Parse(changedArtifact.Bytes)!.AsObject();
        forged["request"] = changed["request"]!.DeepClone();
        forged["requestId"] = changed["requestId"]!.DeepClone();
        forged["compiled"]!["definitionId"] = changed["compiled"]!["definitionId"]!.DeepClone();
        forged["compiled"]!["requestId"] = changed["compiled"]!["requestId"]!.DeepClone();
        var hashable = forged.DeepClone().AsObject(); hashable.Remove("artifactHash");
        forged["artifactHash"] = CanonicalOrientedJson.Hash(Encode(hashable));
        var error = Assert.Throws<ArtifactFormatException>(() => sdk.ReadDifferentialArtifact(Encode(forged)));
        Assert.Contains("Fresh differential", error.Message);
    }
    [Fact]
    public void SignedCarrierSunAndPortZeroRaysAreExplicitRatherThanImplicitCommonAngles()
    {
        var d = Example.Create();
        var carrier = new OrientedShaft("carrier", new OrientedFrame(default, ExactVector3.UnitX, -ExactVector3.UnitY, -ExactVector3.UnitZ));
        var sun = new OrientedShaft("sun", new OrientedFrame(default, ExactVector3.UnitY, ExactVector3.UnitX, -ExactVector3.UnitZ));
        var planet = new CarrierLocalShaft("planet", "carrier", new OrientedFrame(new ExactVector3(55, 0, 0), ExactVector3.UnitY, -ExactVector3.UnitX, ExactVector3.UnitZ));
        var ports = d.Ports.Where(p => p.Id != "sun-port").Append(new CarrierOutputPort("sun-port", "sun", new OrientedFrame(default, ExactVector3.UnitX, -ExactVector3.UnitY, -ExactVector3.UnitZ), ExactQuantity.Turns(new(1, 8))));
        var signed = new DifferentialDefinition(carrier, sun, planet, d.PlaneMm, 100, 10, d.Module, ExactQuantity.Turns(0), ExactQuantity.Turns(new(1, 4)), ExactQuantity.Turns(new(-1, 4)), d.SunMount, d.PlanetMount, 0, ports);
        var a = Prepare(signed, "sun-port", "carrier-port"); Assert.True(a.CanExport);
        var input = Input(("carrier-port", new(-1, 4)), ("sun-port", new(-1, 8))); var e = sdk.EvaluateDifferential(a, input);
        Assert.Equal(new Rational(1, 4), e.CarrierCommonTurns); Assert.Equal(new Rational(1, 4), e.Coordinates["sun"]);
        Assert.Equal(new Rational(11, 4), e.PlanetCommonTurns); Assert.Equal(new Rational(5, 2), e.Coordinates["planet"]); Assert.Equal(new Rational(9, 4), e.PlanetRelativeTurns);
        Assert.Equal(new Rational(-1, 8), e.Ports["sun-port"]); var m = sdk.DisplayDifferential(a, input).Matrices["planet-body"]; Near(0, m[12]); Near(55, m[13]); Near(0, m[0]); Near(-1, m[1]);
    }
    [Fact]
    public void StaticPinnedAndStaticConflictingHoldsAreNotMisclassifiedAsInputs()
    {
        var d = Copy(Example.Create(), holds: new[] { new DifferentialHold("s", "sun", ExactQuantity.Turns(new(1, 10))), new DifferentialHold("c", "carrier", ExactQuantity.Turns(new(1, 4))) });
        var a = Prepare(d); Assert.Equal(MechanicalDeterminacy.PinnedByConstraints, a.Status); Assert.True(a.CanExport); Assert.Equal(new Rational(7, 4), sdk.EvaluateDifferential(a, Input()).Coordinates["planet"]);
        var bad = Copy(d, holds: d.Holds.Append(new DifferentialHold("conflict", "sun", ExactQuantity.Turns(0))));
        Assert.Equal(MechanicalDeterminacy.InconsistentConstraints, Prepare(bad).Status);
    }
    [Fact]
    public void FreshRoundtripOldReadersResourceAndDisplayUnavailableAreDistinct()
    {
        var d = Example.Create(); var r = new DifferentialRequest(d, new[] { "sun-port", "carrier-port" }); var a = sdk.TryFinalizeDifferential(r).Artifact!;
        Assert.Equal(a.Bytes, sdk.ReadDifferentialArtifact(a.Bytes).Bytes); Assert.Equal(a.Bytes, sdk.RebuildDifferentialReplay(sdk.ExportDifferentialReplay(a)).Bytes);
        var copy = a.Bytes; copy[0] = 0; Assert.NotEqual(copy, a.Bytes);
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadCarrierArtifact(a.Bytes)); Assert.Throws<ArtifactFormatException>(() => sdk.ImportMechanicalArtifact(a.Bytes));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadDifferentialDraft(Encoding.UTF8.GetBytes("{\"format\":\"a\",\"format\":\"b\"}")));
        Assert.Throws<ArtifactFormatException>(() => sdk.ReadDifferentialArtifact(new byte[DifferentialProfile.MaxDocumentBytes + 1]));
        Assert.Throws<ExactLinearResourceException>(() => new ExactLinearRow("oversize", new[] { new Rational(BigInteger.Pow(10, 1024)) }, new[] { Rational.Zero }));
        var translated = Example.Create(relocation: OrientedFrame.Identity.At(new ExactVector3(new Rational(BigInteger.Pow(10, 110)), 0, 0)));
        var prepared = Prepare(translated, "sun-port", "carrier-port"); Assert.True(prepared.CanExport);
        Assert.Equal(new Rational(11, 4), sdk.EvaluateDifferential(prepared, Input(("sun-port", 0), ("carrier-port", new(1, 4)))).Coordinates["planet"]);
        var display = sdk.DisplayDifferential(prepared, Input(("sun-port", 0), ("carrier-port", new(1, 4)))); Assert.False(display.IsAvailable); Assert.Empty(display.Matrices);
    }
    private static DifferentialDefinition Copy(DifferentialDefinition d, IEnumerable<CarrierOutputPort>? ports = null, IEnumerable<DifferentialHold>? holds = null,
        ExactQuantity? module = null, bool? contact = null, ExactQuantity? planetReference = null, IEnumerable<string>? required = null, OrientedFrame? plane = null) =>
        new(d.CarrierShaft, d.SunShaft, d.PlanetShaft, plane ?? d.PlaneMm, d.SunTeeth, d.PlanetTeeth, module ?? d.Module, d.CarrierReference, d.SunReference, planetReference ?? d.PlanetReference,
            d.SunMount, d.PlanetMount, d.ToothRegistration, ports ?? d.Ports, holds ?? d.Holds, d.Prefix, d.PrefixMapping, d.CarrierBodyId, d.SunBodyId, d.PlanetBodyId, contact ?? d.ContactPresent, required ?? d.RequiredValidationDomains);
    private static byte[] Encode(JsonNode node) { using var m = new MemoryStream(); using (var w = new System.Text.Json.Utf8JsonWriter(m)) node.WriteTo(w); return m.ToArray(); }
    private static double Mod(Rational r) => (double)((r.Numerator % r.Denominator + r.Denominator) % r.Denominator) / (double)r.Denominator;
    private static double Number(Rational r) => (double)r.Numerator / (double)r.Denominator;
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - 1e-8, expected + 1e-8);
}
