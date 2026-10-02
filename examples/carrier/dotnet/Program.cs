using System.Numerics;
using System.Text.Json;
using GearInvest;
using GearInvest.Core;
using GearInvest.CarrierExample;

if (args.Length < 2 || args[0] != "create" && args[0] != "reopen")
    throw new ArgumentException("create <new-directory> [sunTeeth planetTeeth prefix] | reopen <directory>");
var sdk = GearInvestSdk.CreateDefault(); var directory = Path.GetFullPath(args[1]);
var artifactPath = Path.Combine(directory, "mechanism.carrier.json");
if (args[0] == "create")
{
    if (Directory.Exists(directory)) throw new IOException("Choose a new directory; existing results are preserved.");
    var sun = args.Length > 2 ? int.Parse(args[2]) : 100; var planet = args.Length > 3 ? int.Parse(args[3]) : 10;
    var request = Example.Create(sun, planet, args.Length > 4 && args[4] == "prefix");
    var final = sdk.TryFinalizeCarrier(request);
    if (!final.IsFinalized) throw new InvalidOperationException(final.Status + ":" + string.Join(";", final.Diagnostics.Select(d => d.Detail)));
    Directory.CreateDirectory(directory);
    sdk.SaveCarrierDraft(request, Path.Combine(directory, "mechanism.carrier-draft.json"));
    sdk.SaveCarrierArtifact(final.Artifact!, artifactPath);
    sdk.SaveCarrierReplay(final.Artifact!, Path.Combine(directory, "mechanism.carrier-replay.json"));
}
// This path also runs in a distinct reopen process. Stored laws/PASS are never an input to source admission.
var artifact = sdk.LoadCarrierArtifact(artifactPath);
var draft = sdk.LoadCarrierDraft(Path.Combine(directory, "mechanism.carrier-draft.json"));
var fresh = sdk.TryFinalizeCarrier(draft);
if (!fresh.IsFinalized || !fresh.Artifact!.Bytes.SequenceEqual(artifact.Bytes)) throw new InvalidOperationException("Fresh draft differs.");
var replay = File.ReadAllBytes(Path.Combine(directory, "mechanism.carrier-replay.json"));
if (!sdk.RebuildCarrierReplay(replay).Bytes.SequenceEqual(artifact.Bytes)) throw new InvalidOperationException("Replay reconstruction differs.");
var roots = new[] { Rational.Zero, new Rational(1, 4), new Rational(-1, 4), new Rational(2, 7), new Rational(BigInteger.Pow(10, 60)) + new Rational(1, 4) };
var samples = roots.Select(root =>
{
    var state = sdk.EvaluateCarrier(artifact.Analysis, ExactQuantity.Turns(root));
    var display = sdk.DisplayCarrier(artifact.Analysis, ExactQuantity.Turns(root));
    if (!display.IsAvailable) throw new InvalidOperationException(display.UnavailableReason);
    return new { root = root.ToString(), carrier = state.CarrierCommonTurns.ToString(), planetCommon = state.PlanetBodyCommonTurns.ToString(),
        shafts = state.Shafts.Select(s => new { s.ShaftId, world = s.WorldTurns.ToString(), relative = s.CarrierRelativeTurns?.ToString() }),
        port = state.PortReadoutTurns.ToString(), matrices = display.Matrices };
});
Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", processId = Environment.ProcessId, artifact.ArtifactHash,
    artifact.Request.DefinitionId, shaftCount = artifact.Analysis.Shafts.Count, bodyCount = artifact.Analysis.PoseNodes.Count(n => n.BodyId is not null), samples },
    new JsonSerializerOptions { WriteIndented = true }));
