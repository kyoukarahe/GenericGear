using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.SdkExample;

if (args.Length != 1) throw new ArgumentException("Usage: Consumer NEW_OUTPUT_DIRECTORY");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new IOException("Choose a NEW output directory; existing artifacts are never overwritten.");
var sdk = GearInvestSdk.CreateDefault();
Directory.CreateDirectory(output);
foreach (var (name, prefix, teeth, wheel, ratio) in new[] {
    ("coaxial", "rotor", 16, 64, new Rational(1,12)),
    ("variant", "rotor", 20, 60, new Rational(1,9)),
    ("renamed", "gearset-X", 20, 60, new Rational(1,9)) })
{
    var draft = MechanicalAuthoringExample.CreateCoaxialDraft(prefix, teeth, wheel, ratio);
    var analysis = sdk.AnalyzeMechanicalDraft(draft);
    if (!analysis.IsMechanicallyValid) throw new InvalidOperationException("Analysis refused ordinary authored input.");
    var final = sdk.TryFinalizeMechanicalDraft(draft, ParallelCoaxialLayout.Profile);
    if (!final.IsFinalized) throw new InvalidOperationException("Finalization refused: " + final.Status);
    var artifact = final.ArtifactBytes!;
    if (!artifact.SequenceEqual(sdk.RebuildParallelCoaxialArtifact(sdk.ReadParallelCoaxialArtifact(artifact))))
        throw new InvalidOperationException("Source reconstruction differs.");
    var assembly = sdk.ComposeMechanicalAssemblyDraft(sdk.ImportMechanicalArtifact(artifact), new SourceLengthMapping(1, OrientedFrame.Identity),
        Array.Empty<MechanicalAssemblyMember>(), new[] {
            new AssemblyOutputBinding("driver", AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, prefix + "/input")),
            new AssemblyOutputBinding("driven", AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, prefix + "/output")) });
    var replay = sdk.ExportMechanicalAssemblyReplay(assembly, new AssemblyReplayExportRequest(new[] {
        new Rational(-1,4), Rational.Zero, new Rational(1,4), Rational.One, new Rational(12) }, false));
    if (!replay.IsExported) throw new InvalidOperationException("Replay export refused: " + replay.Detail);
    var bytes = sdk.WriteMechanicalAssemblyReplay(replay.Document!);
    if (!bytes.SequenceEqual(sdk.WriteMechanicalAssemblyReplay(sdk.RebuildMechanicalAssemblyReplay(sdk.ReadMechanicalAssemblyReplay(bytes)))))
        throw new InvalidOperationException("Replay source reconstruction differs.");
    File.WriteAllBytes(Path.Combine(output, name + ".artifact.json"), artifact);
    File.WriteAllBytes(Path.Combine(output, name + ".replay.json"), bytes);
    Console.WriteLine(name + " " + replay.Document!.ReplayId + " reconstruction=PASS");
}
Console.WriteLine("GENERICGEAR_PUBLIC_CONSUMER_PASS");
