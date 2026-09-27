using System.Numerics;
using System.Text;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.SdkExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class CoaxialAuthoringTests
{
    [Fact]
    public void PublicAuthoringFinalizesRebuildsAndExportsOneActualRootWithSeparateCoaxialChannels()
    {
        var sdk = GearInvestSdk.CreateDefault();
        var draft = MechanicalAuthoringExample.CreateCoaxialDraft();
        var analysis = sdk.AnalyzeMechanicalDraft(draft);
        Assert.True(analysis.IsMechanicallyValid, string.Join(";", analysis.Diagnostics.Select(x => x.Code + ":" + x.Detail)));
        Assert.Equal(3, draft.Definition.Shafts.Count);
        Assert.Equal(4, draft.Definition.Bodies.Count);
        Assert.Equal(2, draft.Definition.Contacts.Count);
        Assert.Single(draft.Definition.Shafts, s => s.IsPrescribed);
        var roots = new[] { Rational.Zero, new Rational(1, 4), Rational.One, new Rational(12), new Rational(-1, 4), new Rational(12 * BigInteger.Pow(10, 60)) + new Rational(1, 4) };
        foreach (var u in roots.Concat(Enumerable.Reverse(roots)))
        {
            var values = sdk.EvaluateMechanicalAnalysis(analysis, u).ToDictionary(s => s.ShaftId, s => s.Turns);
            Assert.Equal(u, values["rotor/input"]);
            Assert.Equal(-u / 3, values["rotor/compound"]);
            Assert.Equal(u / 12, values["rotor/output"]);
        }
        var final = sdk.TryFinalizeMechanicalDraft(draft, ParallelCoaxialLayout.Profile);
        Assert.True(final.IsFinalized, final.Status + ":" + string.Join(";", final.Diagnostics.Select(x => x.Detail)));
        var imported = sdk.ImportMechanicalArtifact(final.ArtifactBytes!);
        Assert.Equal(draft.DefinitionId, imported.DefinitionId);
        Assert.Equal(final.ArtifactBytes, sdk.RebuildParallelCoaxialArtifact(sdk.ReadParallelCoaxialArtifact(final.ArtifactBytes!)));
        Assert.True(sdk.TryFinalizeMechanicalDraft(imported, ParallelCoaxialLayout.Profile).OriginalBytesPreserved);
        var read = sdk.ReadMechanicalDraft(sdk.WriteMechanicalDraft(imported));
        Assert.Equal(imported.DraftId, read.DraftId);
        var assembly = sdk.ComposeMechanicalAssemblyDraft(read, new SourceLengthMapping(1, OrientedFrame.Identity), Array.Empty<MechanicalAssemblyMember>(),
            new[] { new AssemblyOutputBinding("first", AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, "rotor/input")),
                new AssemblyOutputBinding("second", AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, "rotor/output")) });
        var assembled = sdk.TryFinalizeMechanicalAssembly(assembly);
        Assert.True(assembled.IsFinalized, assembled.Status + ":" + string.Join(";", assembled.Diagnostics.Select(x => x.Detail)));
        Assert.Equal(assembled.ArtifactBytes, sdk.RebuildMechanicalAssemblyArtifact(sdk.ReadMechanicalAssemblyArtifact(assembled.ArtifactBytes!)).Bytes);
        var replay = sdk.ExportMechanicalAssemblyReplay(assembly, new AssemblyReplayExportRequest(roots.Take(5).OrderBy(x => x), false));
        Assert.True(replay.IsExported, replay.Detail + ":" + string.Join(";", replay.SourceFinalization?.Diagnostics.Select(x => x.Detail) ?? Array.Empty<string>()));
        Assert.Equal(sdk.WriteMechanicalAssemblyReplay(replay.Document!), sdk.WriteMechanicalAssemblyReplay(sdk.RebuildMechanicalAssemblyReplay(replay.Document!)));
        Assert.Contains("coaxialLayout", Encoding.UTF8.GetString(sdk.WriteMechanicalAssemblyDraft(assembly)));
    }
}
