using System.Security.Cryptography;
using System.Text.Json;
using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using GearInvest.WindingExample;

if(args.Length<2)throw new ArgumentException("produce|restore|modes-produce|modes-restore|modes-resume OUTPUT_DIRECTORY [authoring options]");
var sdk=GearInvestSdk.CreateDefault();var output=Path.GetFullPath(args[1]);WindingRecordingArtifact recording;
string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
void Save(string name,byte[] b){Directory.CreateDirectory(output);using var f=new FileStream(Path.Combine(output,name),FileMode.CreateNew);f.Write(b);}
if(args[0].StartsWith("modes-",StringComparison.Ordinal))
{
    MechanicalModeRecordingArtifact modes;
    if(args[0]=="modes-produce")
    {
        var source=args.Length>2?sdk.LoadWindingConnection(args[2]):sdk.FinalizeWindingConnection(Example.Create());modes=ModeExample.Create(sdk,source);
        Save("recording.json",modes.Bytes);Save("replay.json",sdk.ExportMechanicalModeReplay(modes));
    }
    else if(args[0]=="modes-restore"||args[0]=="modes-resume")
    {
        modes=sdk.LoadMechanicalModeRecording(Path.Combine(output,"recording.json"));var rebuilt=sdk.RebuildMechanicalModeReplay(File.ReadAllBytes(Path.Combine(output,"replay.json")));
        if(modes.Recording.Final.StateId!=rebuilt.Recording.Final.StateId)throw new InvalidOperationException("Mode restore differs.");
        if(args[0]=="modes-resume")
        {
            var old=modes.Recording.Final;var d=modes.Recording.Definition;
            var request=new MechanicalModeRequest("fresh-process-resume",d.DefinitionId,old.StateId,old.Revision,new[]{new MechanicalModeSegment(new(new Rational(11,100),new Dictionary<string,Rational>{{d.Connection.PlanetPortId,new Rational(-1,10)}}))});
            var next=sdk.AdvanceMechanicalModes(d,old,request);if(!next.IsAccepted)throw new InvalidOperationException(next.Status);
            modes=sdk.RecordMechanicalModes(modes.Source,d,modes.Recording.InitialPlanetPortTurns,modes.Recording.Attempts.Select(a=>a.Request).Append(request));
            Save("resumed-recording.json",modes.Bytes);Save("resumed-replay.json",sdk.ExportMechanicalModeReplay(modes));
        }
    }
    else throw new ArgumentException("Unknown mode command.");
    Console.WriteLine(JsonSerializer.Serialize(new{command=args[0],verdict="PASS",sourceId=modes.Source.Source.DefinitionId,definitionId=modes.Recording.Definition.DefinitionId,
        recordingId=modes.RecordingId,stateId=modes.Recording.Final.StateId,mode=modes.Recording.Final.Mode.ToString(),revision=modes.Recording.Final.Revision,
        eventCursor=modes.Recording.Final.EventCursor,samples=1+modes.Recording.Attempts.Sum(a=>a.Samples.Count+(a.IsAccepted?1:0)),
        rejected=modes.Recording.Attempts.Count(a=>!a.IsAccepted&&a.Status!="AlreadyApplied"),retries=modes.Recording.Attempts.Count(a=>a.Status=="AlreadyApplied"),
        captureQuality=modes.Recording.Final.CouplingOffset.Kind,sdkSha256=Hash(File.ReadAllBytes(typeof(GearInvestSdk).Assembly.Location))}));return;
}
if(args[0]=="produce")
{
    var scale=args.Length>2?int.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture):1;
    var links=args.Length>3?int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture):21;var circular=args.Length>4&&bool.Parse(args[4]);
    var source=Example.Create(scale,links,circular);var artifact=sdk.FinalizeWindingConnection(source);
    var paths=new List<WindingDriveInput[]>();
    foreach(var bounds in new[]{(0,12),(12,0),(0,-12),(-12,0)})
    {
        var dir=Math.Sign(bounds.Item2-bounds.Item1);paths.Add(Enumerable.Range(1,12).Select(i=>new WindingDriveInput(new Rational(bounds.Item1+dir*i,96),new Rational(3,10))).ToArray());
    }
    paths.Add(new[]{new WindingDriveInput(0,new Rational(3,10)),new WindingDriveInput(0,new Rational(4,10))});
    paths.Add(new[]{new WindingDriveInput(new Rational(3,10),0),new WindingDriveInput(0,0)});
    paths.Add(new[]{new WindingDriveInput(new Rational(-3,10),0),new WindingDriveInput(0,0)});
    recording=sdk.RecordWindingConnection(artifact,new Rational(3,10),paths);
    if(recording.Recording.Attempts.Take(5).Any(a=>!a.IsAccepted)||recording.Recording.Attempts.Skip(5).Any(a=>a.Status!="WindingBoundary"))throw new InvalidOperationException("Unexpected ordinary path result.");
    Save("source.json",artifact.Bytes);Save("recording.json",recording.Bytes);Save("replay.json",sdk.ExportWindingReplay(recording));
    Save("suffix.json",sdk.FinalizeDifferentialSuffix(source.Suffix).Bytes);
}
else if(args[0]=="restore")
{
    recording=sdk.LoadWindingRecording(Path.Combine(output,"recording.json"));var replay=sdk.RebuildWindingReplay(File.ReadAllBytes(Path.Combine(output,"replay.json")));
    if(recording.Recording.Final.SnapshotId!=replay.Recording.Final.SnapshotId)throw new InvalidOperationException("Restore identity differs.");
    _=sdk.ReadDifferentialSuffix(File.ReadAllBytes(Path.Combine(output,"suffix.json")));
}
else throw new ArgumentException("Unknown command.");
Console.WriteLine(JsonSerializer.Serialize(new{command=args[0],verdict="PASS",sourceId=recording.Source.Source.DefinitionId,recordingId=recording.RecordingId,
    snapshotId=recording.Recording.Final.SnapshotId,sourceHash=Hash(recording.Source.Bytes),replayHash=Hash(File.ReadAllBytes(Path.Combine(output,"replay.json"))),
    samples=1+recording.Recording.Attempts.Sum(a=>a.Frames.Count),failed=recording.Recording.Attempts.Count(a=>!a.IsAccepted),pins=recording.Recording.Final.Winding.Pins.Count,
    suffixQuality=recording.Recording.Final.Coordinates["suffix/out"].Kind,solutionErrorBoundTurns=recording.Recording.Final.Coordinates["suffix/out"].SolutionErrorBoundTurns,
    sdkSha256=Hash(File.ReadAllBytes(typeof(GearInvestSdk).Assembly.Location))}));
