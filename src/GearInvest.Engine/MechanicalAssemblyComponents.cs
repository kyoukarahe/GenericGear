using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using GearInvest.Core;

namespace GearInvest.Engine;

internal static class MechanicalAssemblyComponents
{
    internal static AssemblyComponentReference MemberConstraint(MechanicalAssemblyMember m) => AssemblyComponentReference.Member(m.InstanceId, AssemblyComponentKind.Constraint, m.Declaration.LocalDeviceId);
    internal static AssemblyComponentReference MemberOutput(MechanicalAssemblyMember m) => AssemblyComponentReference.Member(m.InstanceId, AssemblyComponentKind.Output, m.Declaration.OutputKey);
    internal static AssemblyComponentReference RootReference(MechanicalReference r) => AssemblyComponentReference.Root(r.Kind switch
    { "Shaft" or "InputShaft" => AssemblyComponentKind.Shaft, "Port" => AssemblyComponentKind.Port, "Output" => AssemblyComponentKind.Output, "Body" => AssemblyComponentKind.Body, _ => AssemblyComponentKind.Constraint }, r.Id);

    internal static AssemblyOutputCapability Capability(AssemblyMemberDeclaration d) => d.Kind switch
    { AssemblyDeviceKind.Geneva => AssemblyOutputCapability.IntermittentRotaryOutput,
        AssemblyDeviceKind.CrankSlider or AssemblyDeviceKind.CamFollower => AssemblyOutputCapability.NonlinearPrismaticOutput,
        _ => AssemblyOutputCapability.AffineRotaryShaft };
    internal static OrientedShaft? Shaft(AssemblyMemberDeclaration d) => d switch
    { AssemblyWormDeclaration w => w.Device.OutputShaft, AssemblyOpenBeltDeclaration b => b.Device.OutputShaft,
        AssemblyPitchChainDeclaration c => c.Device.OutputShaft, AssemblyGenevaDeclaration g => g.Device.OutputShaft, _ => null };
    internal static ShaftPort? Port(AssemblyMemberDeclaration d) => d switch
    { AssemblyWormDeclaration w => w.OutputTerminal, AssemblyOpenBeltDeclaration b => b.OutputTerminal, AssemblyPitchChainDeclaration c => c.OutputTerminal, _ => null };
    internal static MechanicalOutput? AffineOutput(AssemblyMemberDeclaration d) => d switch
    { AssemblyWormDeclaration w => w.Output, AssemblyOpenBeltDeclaration b => b.Output, AssemblyPitchChainDeclaration c => c.Output, _ => null };
    internal static Rational? RequiredPhase(AssemblyMemberDeclaration d) => d switch
    { AssemblyWormDeclaration w => w.RequiredOutputPhase, AssemblyOpenBeltDeclaration b => b.RequiredOutputPhase, AssemblyPitchChainDeclaration c => c.RequiredOutputPhase, _ => null };
    internal static (Rational Input, Rational Output) References(AssemblyMemberDeclaration d) => d switch
    { AssemblyWormDeclaration w => (w.Device.InputReferenceTurns.Value, w.Device.OutputReferenceTurns.Value),
        AssemblyOpenBeltDeclaration b => (b.Device.InputReferenceTurns.Value, b.Device.OutputReferenceTurns.Value),
        AssemblyPitchChainDeclaration c => (c.Device.InputReferenceTurns.Value, c.Device.OutputReferenceTurns.Value), _ => throw new ArgumentException("Not an affine member.") };
    internal static bool StationMatches(AssemblyMemberDeclaration d, OrientedFrame f, ExactQuantity station)
    {
        if (station.Kind != QuantityKind.LinearPosition) return false;
        return d switch
        {
            AssemblyWormDeclaration w => w.Device.InputPitchStation == station,
            AssemblyOpenBeltDeclaration b => b.Device.InputPulleyCenterMm == f.Origin + f.Z * station.Value,
            AssemblyPitchChainDeclaration c => c.Device.InputSprocketStation == station,
            AssemblyGenevaDeclaration g => g.Device.DriverStation == station && g.Device.DriverCenterMm == f.Origin + f.Z * station.Value,
            AssemblyCrankSliderDeclaration c => c.Device.PivotStation == station && c.Device.PivotMm == f.Origin + f.Z * station.Value,
            AssemblyCamFollowerDeclaration c => c.Device.CamStation == station && c.Device.CenterMm == f.Origin + f.Z * station.Value,
            _ => false
        };
    }
    internal static IEnumerable<AssemblyInventoryEntry> RootInventory(MechanicalDraft root)
    {
        AssemblyComponentReference R(AssemblyComponentKind k, string id) => AssemblyComponentReference.Root(k, id);
        foreach (var s in root.Definition.Shafts) yield return new(R(AssemblyComponentKind.Shaft, s.Id), "RootShaft", prescribed: s.IsPrescribed);
        foreach (var b in root.Definition.Bodies) yield return new(R(AssemblyComponentKind.Body, b.Id), "RootGearBody", R(AssemblyComponentKind.Shaft, b.ShaftId));
        foreach (var p in root.Definition.Ports) yield return new(R(AssemblyComponentKind.Port, p.Id), "RootPort", R(AssemblyComponentKind.Shaft, p.ShaftId));
        foreach (var c in root.Definition.Contacts) yield return new(R(AssemblyComponentKind.Constraint, c.Id), "RootContact");
        foreach (var c in root.Definition.Connections) yield return new(R(AssemblyComponentKind.Constraint, c.Id), "RootPortConnection");
        foreach (var o in root.Definition.Outputs) yield return new(R(AssemblyComponentKind.Output, o.Key), "RootOutput", o.ShaftId is null ? null : R(AssemblyComponentKind.Shaft, o.ShaftId));
    }
    internal static IEnumerable<AssemblyInventoryEntry> MemberInventory(MechanicalAssemblyMember member)
    {
        var d = member.Declaration; var list = new List<AssemblyInventoryEntry>();
        AssemblyComponentReference R(AssemblyComponentKind k, string id) => AssemblyComponentReference.Member(member.InstanceId, k, id);
        void Add(AssemblyComponentKind k, string id, string role, AssemblyComponentReference? shaft = null, bool prescribed = false, bool nonlinear = false) => list.Add(new(R(k, id), role, shaft, prescribed, nonlinear));
        var upstream = member.InputBinding?.UpstreamShaft; var shaft = Shaft(d); var outputShaft = shaft is null ? null : R(AssemblyComponentKind.Shaft, shaft.Id);
        if (shaft is not null) Add(AssemblyComponentKind.Shaft, shaft.Id, "MemberOutputShaft", prescribed: shaft.IsPrescribed, nonlinear: d is AssemblyGenevaDeclaration);
        var port = Port(d); if (port is not null) Add(AssemblyComponentKind.Port, port.Id, "CalibratedReadoutPort", outputShaft);
        Add(AssemblyComponentKind.Output, d.OutputKey, "MemberOutput", outputShaft, nonlinear: Capability(d) != AssemblyOutputCapability.AffineRotaryShaft);
        Add(AssemblyComponentKind.Constraint, d.LocalDeviceId, "LocalDeviceConstraint");
        switch (d)
        {
            case AssemblyWormDeclaration w:
                Add(AssemblyComponentKind.Body, w.Device.InputWormBodyId, "WormBody", upstream); Add(AssemblyComponentKind.Body, w.Device.OutputWheelBodyId, "WormWheelBody", outputShaft); break;
            case AssemblyOpenBeltDeclaration b:
                Add(AssemblyComponentKind.Body, b.Device.InputPulleyBodyId, "InputPulleyBody", upstream); Add(AssemblyComponentKind.Body, b.Device.OutputPulleyBodyId, "OutputPulleyBody", outputShaft); break;
            case AssemblyPitchChainDeclaration c:
                Add(AssemblyComponentKind.Body, c.Device.InputSprocketBodyId, "InputSprocketBody", upstream); Add(AssemblyComponentKind.Body, c.Device.OutputSprocketBodyId, "OutputSprocketBody", outputShaft);
                if (c.Device.SelectedChain is not null && c.Device.SelectedChain.LinkCount > 0 && c.Device.SelectedChain.LinkCount <= PitchChainProfile.MaxLinks)
                {
                    // Same persistent material IDs as PitchChainGeometry; these are not prescribed shafts.
                    for (var i = 0; i < c.Device.SelectedChain.LinkCount; i++)
                    { Add(AssemblyComponentKind.Feature, c.Device.SelectedChain.PinId(i), "ChainPin");
                        Add(AssemblyComponentKind.Feature, c.Device.SelectedChain.LinkId(i), "ChainLink"); }
                }
                break;
            case AssemblyGenevaDeclaration g:
                Add(AssemblyComponentKind.Body, g.Device.DriverBodyId, "GenevaDriverBody", upstream); Add(AssemblyComponentKind.Body, g.Device.WheelBodyId, "GenevaWheelBody", outputShaft, nonlinear: true);
                Add(AssemblyComponentKind.Feature, g.Device.PinId, "GenevaDrivePin", upstream);
                foreach (var id in g.Device.Wheel.SlotIds) Add(AssemblyComponentKind.Feature, id, "GenevaSlot", outputShaft, nonlinear: true);
                Add(AssemblyComponentKind.Feature, g.Device.IdealLock.DriverFeatureId, "GenevaLockDisc", upstream);
                foreach (var id in g.Device.IdealLock.RecessIds) Add(AssemblyComponentKind.Feature, id, "GenevaRecess", outputShaft, nonlinear: true);
                break;
            case AssemblyCrankSliderDeclaration c:
                Add(AssemblyComponentKind.Body, c.Device.CrankBodyId, "CrankBody", upstream); Add(AssemblyComponentKind.Body, c.Device.RodBodyId, "DependentRodBody", nonlinear: true);
                Add(AssemblyComponentKind.Body, c.Device.SliderBodyId, "SliderBody", nonlinear: true); Add(AssemblyComponentKind.LinearDof, c.Device.LinearDofId, "PrismaticCoordinate", prescribed: c.Device.SliderIsPrescribed, nonlinear: true);
                Add(AssemblyComponentKind.Constraint, c.Device.GuideId, "PrismaticGuide"); Add(AssemblyComponentKind.Feature, c.Device.CrankPinId, "CrankPin", upstream); Add(AssemblyComponentKind.Feature, c.Device.SliderPinId, "SliderPin", nonlinear: true); break;
            case AssemblyCamFollowerDeclaration c:
                Add(AssemblyComponentKind.Body, c.Device.CamBodyId, "CamBody", upstream); Add(AssemblyComponentKind.Body, c.Device.FollowerBodyId, "FollowerBody", nonlinear: true);
                Add(AssemblyComponentKind.LinearDof, c.Device.LinearDofId, "PrismaticCoordinate", prescribed: c.Device.FollowerIsPrescribed, nonlinear: true);
                Add(AssemblyComponentKind.Constraint, c.Device.GuideId, "PrismaticGuide"); Add(AssemblyComponentKind.Feature, c.Device.FollowerReferenceId, "FollowerFaceReference", nonlinear: true); break;
        }
        return list;
    }
}
