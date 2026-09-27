using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.SdkExample;
using GearInvest.Serialization.Json;
using Xunit;

namespace GearInvest.Tests;

public sealed class CoaxialBoundaryAndPersistenceTests
{
    private static readonly GearInvestSdk Sdk = GearInvestSdk.CreateDefault();
    private static MechanicalDraft Original() => MechanicalAuthoringExample.CreateCoaxialDraft();
    private static MechanicalEditBatch Batch(MechanicalDraft d, params MechanicalEditOperation[] edits) => new(d.Revision, d.DefinitionId, edits);
    private static MechanicalDraft Apply(MechanicalDraft d, params MechanicalEditOperation[] edits)
    {
        var r = Sdk.ApplyMechanicalEdits(d, Batch(d, edits));
        Assert.Equal(MechanicalEditStatus.Applied, r.Status); return r.Draft!;
    }
    private static ParallelCoaxialLayout Layout(params CoaxialPlacementGroup[] groups) => new(OrientedFrame.Identity, 1, 5, 2, groups);
    private static void Refused(MechanicalDraft d, string? diagnostic = null)
    {
        var a = Sdk.AnalyzeMechanicalDraft(d);
        if (diagnostic is not null) Assert.Contains(a.Diagnostics, x => x.Code == diagnostic);
        Assert.False(Sdk.TryFinalizeMechanicalDraft(d, ParallelCoaxialLayout.Profile).IsFinalized);
    }

    [Theory]
    [InlineData("undeclared")]
    [InlineData("different-line")]
    [InlineData("unknown-owner")]
    [InlineData("overlapping-groups")]
    [InlineData("same-plane")]
    [InlineData("same-center-contact")]
    [InlineData("cross-plane-contact")]
    [InlineData("pitch")]
    [InlineData("center-distance")]
    [InlineData("duplicate-contact")]
    [InlineData("missing-contact")]
    [InlineData("body-owner")]
    [InlineData("out-of-lattice")]
    [InlineData("clearance-disabled")]
    public void InvalidDeclarationsNeverBecomeCoincidenceOrCollisionExemptions(string mutation)
    {
        var d = Original(); var c = d.Definition.Bodies.Single(b => b.Id == "rotor/C");
        var changed = mutation switch {
            "undeclared" => Apply(d, new SetParallelCoaxialLayoutEdit(Layout())),
            "different-line" => Apply(d, new SetParallelCoaxialLayoutEdit(Layout(new CoaxialPlacementGroup("wrong", new[] { "rotor/input", "rotor/compound" })))),
            "unknown-owner" => Apply(d, new SetParallelCoaxialLayoutEdit(Layout(new CoaxialPlacementGroup("wrong", new[] { "other-instance/input", "rotor/output" })))),
            "overlapping-groups" => Apply(d, new SetParallelCoaxialLayoutEdit(Layout(new("one", new[] { "rotor/input", "rotor/output" }), new("two", new[] { "rotor/output", "rotor/compound" })))),
            "same-plane" => Apply(d, new SetBodyMountingEdit("rotor/C", c.MountingFrame.At(new ExactVector3(80, 0, 0))),
                new SetBodyMountingEdit("rotor/D", OrientedFrame.Identity)),
            "same-center-contact" => Apply(d, new RebindContactEdit("rotor/cd", "rotor/A", "rotor/D"), new SetBodyMountingEdit("rotor/D", OrientedFrame.Identity)),
            "cross-plane-contact" => Apply(d, new RebindContactEdit("rotor/ab", "rotor/A", "rotor/C")),
            "pitch" => Apply(d, new SetParallelCoaxialLayoutEdit(new ParallelCoaxialLayout(OrientedFrame.Identity, 2, 5, 2, d.Definition.CoaxialLayout!.Groups))),
            "center-distance" => Apply(d, new MoveShaftGroupEdit("rotor/compound", OrientedFrame.Identity.At(new ExactVector3(1, 0, 0)))),
            "duplicate-contact" => Apply(d, new AddContactEdit(new("duplicate", OrientedContactKind.ExternalSpur, "rotor/A", "rotor/B"))),
            "missing-contact" => Apply(d, new RemoveContactEdit("rotor/cd")),
            "body-owner" => Apply(d, new RebindBodyShaftEdit("rotor/C", "rotor/output")),
            "out-of-lattice" => Apply(d, new SetBodyMountingEdit("rotor/C", c.MountingFrame.At(new ExactVector3(80, 0, 4)))),
            "clearance-disabled" => Apply(d, new SetClearancePolicyEdit(ParallelCoaxialLayout.ClearancePolicy, false)),
            _ => throw new InvalidOperationException()
        };
        Refused(changed);
        // Invalid mechanics remain editable/persistable, never promoted to a normal artifact.
        Assert.Equal(changed.DraftId, Sdk.ReadMechanicalDraft(Sdk.WriteMechanicalDraft(changed)).DraftId);
    }

    [Fact]
    public void EncodingMembershipAndResourceBoundsRejectRatherThanTruncate()
    {
        Assert.Throws<ArgumentException>(() => new CoaxialPlacementGroup("g", new[] { "a", "a" }));
        Assert.Throws<ArgumentException>(() => new CoaxialPlacementGroup("g", new[] { "a", "" }));
        Assert.Throws<ArgumentException>(() => new CoaxialPlacementGroup("g", Enumerable.Range(0, 65).Select(i => "s" + i)));
        Assert.Throws<ArgumentException>(() => Layout(new("g", new[] { "a", "b" }), new("g", new[] { "c", "d" })));
        Assert.Throws<ArgumentException>(() => new ParallelCoaxialLayout(OrientedFrame.Identity, 1, 1, 4, Array.Empty<CoaxialPlacementGroup>()));
        Assert.Throws<ArgumentException>(() => new ParallelCoaxialLayout(OrientedFrame.Identity, 0, 1, 2, Array.Empty<CoaxialPlacementGroup>()));
        var d = Original();
        Assert.False(Sdk.TryFinalizeMechanicalDraft(d, MechanicalAuthoringProfile.PlanarExport).IsFinalized);
        var old = Sdk.ImportMechanicalArtifact(MechanicalAuthoringExample.CreatePlanarArtifact());
        Assert.False(Sdk.TryFinalizeMechanicalDraft(old, ParallelCoaxialLayout.Profile).IsFinalized);
        Assert.Equal(MechanicalEditStatus.Rejected, Sdk.ApplyMechanicalEdits(old, Batch(old, new SetParallelCoaxialLayoutEdit(d.Definition.CoaxialLayout!))).Status);
    }

    [Fact]
    public void ToothTargetDisconnectionAndLateFailureAreIndependentAtomicOperations()
    {
        var original = Original();
        var toothBatch = Batch(original, new SetToothCountEdit("rotor/C", 20, ToothDimensionPolicy.PreservePitchRadiusPerTooth),
            new SetToothCountEdit("rotor/D", 60, ToothDimensionPolicy.PreservePitchRadiusPerTooth));
        var changed = Sdk.ApplyMechanicalEdits(original, toothBatch).Draft!;
        var analysis = Sdk.AnalyzeMechanicalDraft(changed);
        Assert.Equal(new Rational(1, 9), analysis.Outputs.Single(o => o.OutputKey == "driven").ShaftRelation!.Value.Coefficient);
        Assert.Equal(MechanicalFinalizationStatus.TargetMismatch, Sdk.TryFinalizeMechanicalDraft(changed, ParallelCoaxialLayout.Profile).Status);
        var targetBatch = Batch(changed, new SetRequestedTransferEdit("driven", new Rational(1, 9)));
        var current = Sdk.ApplyMechanicalEdits(changed, targetBatch).Draft!;
        Assert.True(Sdk.TryFinalizeMechanicalDraft(current, ParallelCoaxialLayout.Profile).IsFinalized);
        var before = Sdk.WriteMechanicalDraft(current);
        var late = Batch(current, new SetToothCountEdit("rotor/A", 21, ToothDimensionPolicy.PreservePitchRadiusPerTooth),
            new SetParallelCoaxialLayoutEdit(Layout()), new RebindBodyShaftEdit("rotor/C", "rotor/input"), new RemoveContactEdit("not-present"));
        var rejected = Sdk.ApplyMechanicalEdits(current, late);
        Assert.Equal(3, rejected.FailingOperationIndex); Assert.Null(rejected.Draft); Assert.True(rejected.NetChanges.IsEmpty);
        Assert.Equal(before, Sdk.WriteMechanicalDraft(current));
        var remove = Batch(current, new RemoveContactEdit("rotor/cd"));
        var disconnected = Sdk.ApplyMechanicalEdits(current, remove).Draft!;
        var disconnectedAnalysis = Sdk.AnalyzeMechanicalDraft(disconnected);
        Assert.False(disconnectedAnalysis.Outputs.Single(o => o.OutputKey == "driven").HasDeterminedMotion);
        Assert.DoesNotContain(Sdk.EvaluateMechanicalAnalysis(disconnectedAnalysis, 7), s => s.ShaftId == "rotor/output");
        var restore = Batch(disconnected, new AddContactEdit(current.Definition.Contacts.Single(c => c.Id == "rotor/cd")));
        var session = Sdk.CreateMechanicalEditSession(original, new[] { toothBatch, targetBatch, late, remove, restore });
        var bytes = Sdk.WriteMechanicalEditSession(session); var read = Sdk.ReadMechanicalEditSession(bytes);
        Assert.Equal(current.DefinitionId, read.CurrentDraft.DefinitionId);
        Assert.Equal(bytes, Sdk.WriteMechanicalEditSession(read));
        Assert.Equal(1, read.Results.Count(r => r.Status == MechanicalEditStatus.Rejected));
        Assert.True(Sdk.TryFinalizeMechanicalDraft(Sdk.ReapplyMechanicalEditSession(read), ParallelCoaxialLayout.Profile).IsFinalized);
    }

    [Fact]
    public void CanonicalOrderingAndEqualRatesNeverMergeRotors()
    {
        var d = Original().Definition;
        var reordered = new MechanicalDefinition(new ParallelCoaxialLayout(OrientedFrame.Identity, 1, 5, 2,
            new[] { new CoaxialPlacementGroup("rotor/common-line", new[] { "rotor/output", "rotor/input" }) }), d.RootShaftId,
            d.Shafts.Reverse(), d.Bodies.Reverse(), d.Contacts.Reverse(), d.Ports.Reverse(), d.Connections.Reverse(), d.Outputs.Reverse());
        Assert.Equal(d.DefinitionId, reordered.DefinitionId);
        Assert.Equal(Sdk.WriteMechanicalDraft(new(d)), Sdk.WriteMechanicalDraft(new(reordered)));
        var equal = MechanicalAuthoringExample.CreateCoaxialDraft("not-a-clock", 60, 20, 1);
        var final = Sdk.TryFinalizeMechanicalDraft(equal, ParallelCoaxialLayout.Profile);
        Assert.True(final.IsFinalized);
        Assert.Equal(3, Sdk.ImportMechanicalArtifact(final.ArtifactBytes!).Definition.Shafts.Count);
        var poses = Sdk.EvaluateMechanicalAnalysis(Sdk.AnalyzeMechanicalDraft(equal), new Rational(1, 4));
        Assert.Equal(poses.Single(p => p.ShaftId == "not-a-clock/input").Turns, poses.Single(p => p.ShaftId == "not-a-clock/output").Turns);
    }

    [Fact]
    public void NonzeroReferenceRigidRelocationSignedCoordinatesAndPreparedOwnershipSurvive()
    {
        var original = Original(); var u0 = new Rational(3, 7); var du = new Rational(-1, 4);
        var a = Sdk.AnalyzeMechanicalDraft(original);
        var before = Sdk.EvaluateMechanicalAnalysis(a, u0).ToDictionary(x => x.ShaftId, x => x.Turns);
        var after = Sdk.EvaluateMechanicalAnalysis(a, u0 + du).ToDictionary(x => x.ShaftId, x => x.Turns);
        Assert.Equal(du / 12, after["rotor/output"] - before["rotor/output"]);
        var pose = new OrientedFrame(new ExactVector3(17, -23, 11), ExactVector3.UnitY, -ExactVector3.UnitX, ExactVector3.UnitZ);
        var layout = original.Definition.CoaxialLayout!;
        var edits = original.Definition.Shafts.Select(s => (MechanicalEditOperation)new MoveShaftGroupEdit(s.Id, pose)).Concat(new[] {
            new SetParallelCoaxialLayoutEdit(new ParallelCoaxialLayout(pose.Transform(layout.PlaneFrame), 1, 5, 2, layout.Groups)) }).ToArray();
        var relocated = Apply(original, edits);
        Assert.True(Sdk.TryFinalizeMechanicalDraft(relocated, ParallelCoaxialLayout.Profile).IsFinalized);
        var rd = relocated.Definition;
        Assert.True(rd.Shafts.Single(s => s.Id == "rotor/input").SameLine(rd.Shafts.Single(s => s.Id == "rotor/output")));
        var negative = new OrientedFrame(default, ExactVector3.UnitX, -ExactVector3.UnitY, -ExactVector3.UnitZ);
        var d = original.Definition;
        var reverse = new MechanicalDefinition(layout, d.RootShaftId,
            d.Shafts.Select(s => s.Id == "rotor/output" ? new OrientedShaft(s.Id, negative) : s),
            d.Bodies.Select(b => b.Id == "rotor/D" ? new OrientedGearBody(b.Id, b.ShaftId, b.Kind, negative.At(b.MountingFrame.Origin), b.Teeth, b.OuterPitchRadius, b.SourceModuleId) : b),
            d.Contacts, d.Ports.Select(p => p.ShaftId == "rotor/output" ? new ShaftPort(p.Id, p.ShaftId, negative.At(p.Frame.Origin)) : p),
            outputs: d.Outputs.Select(o => o.Key == "driven" ? new MechanicalOutput(o.Key, o.ShaftId, o.BodyId, o.PortId, new Rational(-1, 12)) : o));
        var reversed = new MechanicalDraft(reverse);
        Assert.True(Sdk.TryFinalizeMechanicalDraft(reversed, ParallelCoaxialLayout.Profile).IsFinalized);
        var value = Sdk.EvaluateMechanicalAnalysis(Sdk.AnalyzeMechanicalDraft(reversed), 1).Single(s => s.ShaftId == "rotor/output");
        Assert.Equal(new Rational(-1, 12), value.Turns); Assert.Equal(ExactVector3.UnitZ * new Rational(1, 12), value.PositiveAxis * value.Turns);
        var phase = Apply(original, new SetPortEdit(new ShaftPort("rotor/output-port", "rotor/output", d.Ports.Single(p => p.ShaftId == "rotor/output").Frame, new Rational(1, 7))));
        Refused(phase);
        var assembly = Sdk.ComposeMechanicalAssemblyDraft(relocated, new SourceLengthMapping(new Rational(1, 2), OrientedFrame.Identity), Array.Empty<MechanicalAssemblyMember>());
        var prepared = Sdk.PrepareMechanicalAssemblyEvaluation(assembly);
        Assert.NotSame(assembly, prepared.Analysis.Draft);
        Assert.Equal(assembly.DefinitionId, prepared.Analysis.Draft.DefinitionId);
        Assert.Single(prepared.Analysis.Draft.Definition.Root.Definition.CoaxialLayout!.Groups);
        Assert.Equal(3, prepared.Evaluate(ExactQuantity.Turns(12)).Shafts.Count);
        var required = Sdk.ComposeMechanicalAssemblyDraft(relocated, assembly.Definition.RootMapping, Array.Empty<MechanicalAssemblyMember>(), requiredValidationDomains: new[] { "manufacturing" });
        Assert.Equal(MechanicalAssemblyFinalizationStatus.UnresolvedRequiredValidation, Sdk.TryFinalizeMechanicalAssembly(required).Status);
    }

    [Theory]
    [InlineData("law")]
    [InlineData("group")]
    [InlineData("teeth")]
    [InlineData("owner")]
    [InlineData("contact")]
    [InlineData("source-identity")]
    [InlineData("profile")]
    public void ProperlyRehashedSemanticTamperingStillFailsFreshSourceReconstruction(string mutation)
    {
        var bytes = Sdk.TryFinalizeMechanicalDraft(Original(), ParallelCoaxialLayout.Profile).ArtifactBytes!;
        var root = JsonNode.Parse(bytes)!.AsObject(); var request = root["request"]!.AsObject();
        switch (mutation)
        {
            case "law":
                var output = root["analysis"]!["outputs"]!.AsArray().Single(x => x!["binding"]!["key"]!.GetValue<string>() == "driven")!;
                output["shaftRelation"]!["coefficient"]!["numerator"] = "1"; output["shaftRelation"]!["coefficient"]!["denominator"] = "1"; break;
            case "group": request["coaxialLayout"]!["groups"]![0]!["shaftIds"]![0] = "other-owner/input"; break;
            case "teeth": request["bodies"]![2]!["teeth"] = "20"; break;
            case "owner": request["bodies"]![3]!["shaftId"] = "rotor/input"; break;
            case "contact": request["contacts"]!.AsArray().RemoveAt(1); break;
            case "source-identity": root["definitionId"] = new string('0', 64); break;
            case "profile": root["profile"] = "unknown-coaxial-v99"; break;
        }
        root.Remove("artifactHash");
        var hash = CanonicalOrientedJson.Hash(JsonSerializer.SerializeToUtf8Bytes(root));
        // Reinsert in its canonical position, so rejection is not merely property ordering or stale digest.
        var ordered = new JsonObject();
        foreach (var property in root) { ordered.Add(property.Key, property.Value?.DeepClone()); if (property.Key == "definitionId") ordered.Add("artifactHash", hash); }
        Assert.Throws<ArtifactFormatException>(() => Sdk.ImportMechanicalArtifact(JsonSerializer.SerializeToUtf8Bytes(ordered)));
    }
}
