using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;

// Ordinary public SDK authoring. The browser never invents these q,p values.
if (args.Length != 2 || Directory.Exists(args[1])) throw new ArgumentException("Existing source artifact and NEW output directory required.");
var sdk = GearInvestSdk.CreateDefault();
var source = sdk.ImportMechanicalArtifact(File.ReadAllBytes(args[0]));
var input = source.Definition.Shafts.Single(s => s.IsPrescribed);
var members = new List<MechanicalAssemblyMember>();
var outputs = new List<AssemblyOutputBinding>();
var beltFrame = input.Frame.At(input.Frame.Origin + input.Frame.Z * 20 + input.Frame.X * 160);
var beltShaft = new OrientedShaft("belt-shaft", beltFrame);
var beltRadius = ExactQuantity.Millimeters(20);
var belt = new OpenBeltTransmissionDefinition("equal-belt", input.Id, "input-pulley", input.Frame.Origin + input.Frame.Z * 20,
    beltRadius, beltShaft, "output-pulley", ExactQuantity.Millimeters(0), beltRadius, input.Frame.Z,
    ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(1,7)), new OpenBeltLengthSpecification(ExactQuantity.Millimeters(160), beltRadius, beltRadius));
var beltPort = new ShaftPort("terminal", beltShaft.Id, beltFrame);
members.Add(new MechanicalAssemblyMember("one", new AssemblyOpenBeltDeclaration(belt, beltPort, new MechanicalOutput("output", beltShaft.Id, belt.OutputPulleyBodyId, beltPort.Id)),
    new UpstreamShaftBinding(AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, input.Id), ExactQuantity.Millimeters(20))));
outputs.Add(new AssemblyOutputBinding("one", AssemblyComponentReference.Member("one", AssemblyComponentKind.Shaft, beltShaft.Id)));
foreach (var (id, starts, teeth, station) in new[] { ("twelfth", 1, 12, 50), ("one-over-960", 1, 960, 80) })
{
    var frame = input.Frame; var center = frame.Origin + frame.Z * station;
    var worm = new CylindricalWormSpecification(starts, ExactQuantity.Millimeters(2), ExactQuantity.Millimeters(10), 1);
    var wheel = sdk.CreateMatchingIdealWormWheel(worm, teeth);
    var wheelCenter = center + frame.Y * (10 + teeth);
    var output = new OrientedShaft("wheel-shaft", new OrientedFrame(wheelCenter, frame.Y, frame.Z, frame.X));
    var device = new WormDriveTransmissionDefinition("drive", input.Id, "input-body", worm, frame.Z, ExactQuantity.Millimeters(station),
        output, "wheel-body", wheel, ExactQuantity.Millimeters(0), frame.Y, ExactQuantity.Turns(0), ExactQuantity.Turns(0),
        ExactQuantity.Turns(0), ExactQuantity.Turns(new Rational(1, 7)));
    var port = new ShaftPort("terminal", output.Id, output.Frame);
    var local = new AssemblyWormDeclaration(device, port, new MechanicalOutput("output", output.Id, device.OutputWheelBodyId, port.Id));
    members.Add(new MechanicalAssemblyMember(id, local,
        new UpstreamShaftBinding(AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, input.Id), ExactQuantity.Millimeters(station))));
    outputs.Add(new AssemblyOutputBinding(id, AssemblyComponentReference.Member(id, AssemblyComponentKind.Shaft, output.Id)));
}
var draft = sdk.ComposeMechanicalAssemblyDraft(source, new SourceLengthMapping(1, OrientedFrame.Identity), members, outputs);
var final = sdk.TryFinalizeMechanicalAssembly(draft);
if (!final.IsFinalized) throw new InvalidOperationException("Normal finalization failed: " + final.Status + ": " + string.Join("; ", final.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
var exported = sdk.ExportMechanicalAssemblyReplay(draft, new AssemblyReplayExportRequest(new[] { new Rational(-1,4), Rational.Zero, new Rational(1,4), Rational.One }, false));
if (!exported.IsExported) throw new InvalidOperationException(exported.Detail);
var document = exported.Document ?? throw new InvalidOperationException("Missing exported document.");
var replay = sdk.WriteMechanicalAssemblyReplay(document);
if (!sdk.WriteMechanicalAssemblyReplay(sdk.RebuildMechanicalAssemblyReplay(sdk.ReadMechanicalAssemblyReplay(replay))).SequenceEqual(replay))
    throw new InvalidOperationException("Current same-version replay reconstruction differs.");
Directory.CreateDirectory(args[1]);
void Save(string name, byte[] bytes) { using var stream = new FileStream(Path.Combine(args[1], name), FileMode.CreateNew); stream.Write(bytes); }
Save("exact-phases.assembly.json", final.ArtifactBytes!);
Save("exact-phases.replay.json", replay);
Save("exact-phases.draft.json", sdk.WriteMechanicalAssemblyDraft(draft));
Console.WriteLine("GUIDANCE_PUBLIC_PRODUCER_PASS " + document.ReplayId);
