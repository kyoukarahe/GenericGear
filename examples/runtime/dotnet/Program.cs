using GearInvest;
using GearInvest.WindingExample;

if (args.Length == 3 && args[0] == "check-example")
{
    var source = args[1] switch { "selected-drive" => SpatialExample.CreateSelectedDrive(), "spatial" => SpatialExample.Create(), "planar" => Example.Create(), _ => throw new ArgumentException("Unknown example kind") };
    var expected = GearInvestSdk.CreateDefault().FinalizeWindingConnection(source);
    if (!File.ReadAllBytes(args[2]).SequenceEqual(expected.Bytes)) throw new InvalidOperationException("Existing source does not match selected authoring inputs. Choose a new output directory; do not overwrite prior evidence.");
    Console.WriteLine(expected.ArtifactId); return;
}
if (args.Length == 2 && args[0] == "create-selected-drive-example")
{
    var bytes = GearInvestSdk.CreateDefault().FinalizeWindingConnection(SpatialExample.CreateSelectedDrive()).Bytes;
    using var f = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write); f.Write(bytes); return;
}
if (args.Length == 2 && args[0] == "measure") { RuntimeMeasurement.Run(args[1]); return; }
if (args.Length == 3 && args[0] == "measure") { RuntimeMeasurement.Run(args[1],int.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture)); return; }
if (args.Length == 2 && args[0] == "inspect-spatial") { SpatialAcceptance.Run(args[1]); return; }
if (args.Length == 4 && args[0] == "inspect-spatial") { SpatialAcceptance.Run(args[1],GearInvest.Core.Rational.Parse(args[2]),GearInvest.Core.Rational.Parse(args[3])); return; }
if (args.Length == 2 && args[0] == "create-spatial-example")
{
    var bytes = GearInvestSdk.CreateDefault().FinalizeWindingConnection(SpatialExample.Create()).Bytes;
    using var f = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write); f.Write(bytes); return;
}
if (args.Length == 2 && args[0] == "create-example")
{
    var bytes = GearInvestSdk.CreateDefault().FinalizeWindingConnection(Example.Create()).Bytes;
    using var f = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write); f.Write(bytes); return;
}
using var host = new MechanicalRuntimeHost(GearInvestSdk.CreateDefault());
while (Console.ReadLine() is { } line) Console.WriteLine(host.Dispatch(line));
