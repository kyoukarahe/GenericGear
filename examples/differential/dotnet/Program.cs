using System.Numerics;
using System.Text.Json;
using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.DifferentialExample;

if (args.Length < 2 || args[0] != "create" && args[0] != "reopen") throw new ArgumentException("create <new-directory> [sunTeeth planetTeeth mode idPrefix] | reopen <directory>");
var sdk = GearInvestSdk.CreateDefault(); var directory = Path.GetFullPath(args[1]);
if (args[0] == "create")
{
    if (Directory.Exists(directory)) throw new IOException("Choose a new directory; existing results are preserved.");
    int ns = args.Length > 2 ? int.Parse(args[2]) : 100, np = args.Length > 3 ? int.Parse(args[3]) : 10;
    string mode = args.Length > 4 ? args[4] : "sun-carrier", names = args.Length > 5 ? args[5] : "";
    var d = Example.Create(ns, np, mode == "prefix", names, mode == "signed", mode == "hold",
        mode == "display-unavailable" ? OrientedFrame.Identity.At(new ExactVector3(new Rational(BigInteger.Pow(10, 110)), 0, 0)) : null);
    string[] basis = mode == "sun-planet" ? new[] { names + "sun-port", names + "planet-port" } : mode == "carrier-planet" ? new[] { names + "carrier-port", names + "planet-port" } :
        mode == "hold" ? new[] { names + "carrier-port" } : new[] { names + (mode == "prefix" ? "drive-port" : "carrier-port"), names + "sun-port" };
    var r = new DifferentialRequest(d, basis); var final = sdk.TryFinalizeDifferential(r);
    if (!final.IsFinalized) throw new InvalidOperationException(final.Status + ":" + string.Join(";", final.Diagnostics.Select(x => x.Detail)));
    Directory.CreateDirectory(directory);
    sdk.SaveDifferentialDraft(r, Path.Combine(directory, "mechanism.differential-draft.json"));
    sdk.SaveDifferentialArtifact(final.Artifact!, Path.Combine(directory, "mechanism.differential.json"));
    sdk.SaveDifferentialReplay(final.Artifact!, Path.Combine(directory, "mechanism.differential-replay.json"));
    var partial = sdk.AnalyzeDifferentialBoundary(d, new[] { new DifferentialBoundary("sun-only", names + "sun-port", ExactQuantity.Turns(new Rational(1, 10))) });
    File.WriteAllBytes(Path.Combine(directory, "partial.differential-analysis.json"), sdk.WriteDifferentialAnalysis(partial));
    var inconsistent = sdk.AnalyzeDifferentialBoundary(d, new[] { new DifferentialBoundary("s", names + "sun-port", ExactQuantity.Turns(0)), new DifferentialBoundary("c", names + "carrier-port", ExactQuantity.Turns(1)), new DifferentialBoundary("p", names + "planet-port", ExactQuantity.Turns(0)) });
    File.WriteAllBytes(Path.Combine(directory, "inconsistent.differential-analysis.json"), sdk.WriteDifferentialAnalysis(inconsistent));
}
// Fresh process, actual persisted draft and facade source rebuild, never cached PASS/laws.
var artifact = sdk.LoadDifferentialArtifact(Path.Combine(directory, "mechanism.differential.json"));
var draft = sdk.LoadDifferentialDraft(Path.Combine(directory, "mechanism.differential-draft.json"));
var fresh = sdk.TryFinalizeDifferential(draft);
if (!fresh.IsFinalized || !fresh.Artifact!.Bytes.SequenceEqual(artifact.Bytes) || !sdk.RebuildDifferentialReplay(File.ReadAllBytes(Path.Combine(directory, "mechanism.differential-replay.json"))).Bytes.SequenceEqual(artifact.Bytes)) throw new InvalidOperationException("Fresh rebuild mismatch.");
var inputs = new[] { new Rational(1, 4), new Rational(-1, 7), new Rational(BigInteger.Pow(10, 60)) + new Rational(1, 4) };
var observations = inputs.Select(value =>
{
    var snapshot = new DifferentialInputSnapshot(draft.InputPortIds.Select((id, i) => new KeyValuePair<string, ExactQuantity>(id, ExactQuantity.Turns(i == 0 ? value : new Rational(1, 10)))));
    var evaluation = sdk.EvaluateDifferential(artifact.Analysis, snapshot); var display = sdk.DisplayDifferential(artifact.Analysis, snapshot);
    if (!display.IsAvailable && display.Matrices.Count != 0) throw new InvalidOperationException("Unavailable display retained stale poses.");
    return new { input = snapshot.Values.ToDictionary(p => p.Key, p => p.Value.Value.ToString()), coordinates = evaluation.Coordinates.ToDictionary(p => p.Key, p => p.Value.ToString()),
        carrier = evaluation.CarrierCommonTurns.ToString(), planet = evaluation.PlanetCommonTurns.ToString(), relative = evaluation.PlanetRelativeTurns.ToString(), displayAvailable = display.IsAvailable, matrices = display.Matrices };
}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", processId = Environment.ProcessId, artifact.ArtifactHash, draft.RequestId, draft.Definition.DefinitionId, basis = draft.InputPortIds,
    bodyCount = artifact.Analysis.PoseNodes.Count(n => n.BodyId is not null), rank = artifact.Analysis.Reduction!.Rank, observations }));
