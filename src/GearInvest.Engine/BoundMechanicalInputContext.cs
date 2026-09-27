using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>
/// Engine-only snapshot of a resolved actual shaft. Not a draft, prescribed input, wire DTO,
/// user gain, or terminal reading. Local mechanics may inspect it but cannot publish a context.
/// </summary>
internal sealed class BoundMechanicalInputContext
{
    private readonly Lazy<OrientedFrame?> fixedFrame;
    private BoundMechanicalInputContext(string definitionIdentity, string dependencyIdentity, string? rootInputId,
        string? consumerMemberId, AssemblyComponentReference upstream, OrientedShaft? shaft, ShaftPort? port,
        OrientedFrame? fixedFrameMm, ExactAffineRelation? globalRelation, bool bindingAdmitted,
        IEnumerable<string> dependencyPath, SourceLengthMapping? mappingEvidence, MechanicalDraft? legacySource,
        ExactQuantity? mountingStation, AssemblyGenevaAffineMotion? genevaMotion = null)
    {
        DefinitionIdentity = definitionIdentity; DependencyIdentity = dependencyIdentity; RootInputId = rootInputId;
        ConsumerMemberId = consumerMemberId; UpstreamShaft = upstream; Shaft = shaft; Port = port;
        fixedFrame = new Lazy<OrientedFrame?>(() => legacySource is null ? fixedFrameMm :
            shaft?.Frame.IsProperCardinal == true && mappingEvidence is not null && mappingEvidence.MillimetersPerSourceUnit > 0 && mappingEvidence.PoseMm.IsProperCardinal
                ? mappingEvidence.FrameMm(shaft.Frame) : null);
        GlobalRelation = globalRelation; BindingAdmitted = bindingAdmitted; GenevaMotion = genevaMotion;
        DependencyPath = dependencyPath.ToList().AsReadOnly(); MappingEvidence = mappingEvidence;
        LegacySource = legacySource; MountingStation = mountingStation;
        ContextId = HashText(Pack(definitionIdentity, dependencyIdentity, rootInputId ?? "", consumerMemberId ?? "",
            upstream.CanonicalRepresentation, shaft?.Id ?? "", port?.Id ?? "", legacySource is null ? fixedFrameMm is null ? "" : Frame(fixedFrameMm) : mappingEvidence?.CanonicalRepresentation ?? "",
            globalRelation.HasValue ? Pack(F(globalRelation.Value.Coefficient), F(globalRelation.Value.Phase)) : "",
            bindingAdmitted ? "1" : "0", Pack(DependencyPath.ToArray()), mountingStation.HasValue ? MechanicalAssemblyProfile.Q(mountingStation.Value) : "") +
            (genevaMotion is null ? "" : Pack("geneva-affine-input-v1", genevaMotion.MotionId)));
    }
    internal string DefinitionIdentity { get; }
    internal string DependencyIdentity { get; }
    internal string ContextId { get; }
    internal string? RootInputId { get; }
    internal string? ConsumerMemberId { get; }
    internal AssemblyComponentReference UpstreamShaft { get; }
    internal OrientedShaft? Shaft { get; }
    internal ShaftPort? Port { get; }
    internal OrientedFrame? FixedFrameMm => fixedFrame.Value;
    internal ExactAffineRelation? GlobalRelation { get; }
    internal AssemblyGenevaAffineMotion? GenevaMotion { get; }
    internal bool BindingAdmitted { get; }
    internal ReadOnlyCollection<string> DependencyPath { get; }
    internal ExactQuantity? MountingStation { get; }
    // Evidence only: local geometry uses FixedFrameMm and must never apply this mapping again.
    internal SourceLengthMapping? MappingEvidence { get; }
    internal MechanicalDraft? LegacySource { get; }
    internal bool IsLegacy => LegacySource is not null;
    internal bool MappingScaleValid => IsLegacy ? MappingEvidence is not null && MappingEvidence.MillimetersPerSourceUnit > 0 : FixedFrameMm is not null;
    internal bool MappingPoseValid => IsLegacy ? MappingEvidence?.PoseMm.IsProperCardinal == true : FixedFrameMm?.IsProperCardinal == true;
    internal bool MappingValid => MappingScaleValid && MappingPoseValid;
    internal bool PortIsMounted => Shaft is not null && Port is not null && Port.ShaftId == Shaft.Id && Port.Frame.IsProperCardinal &&
        MechanicalConnectionPredicates.PortMounting(new ShaftPort(Port.Id, Port.ShaftId, Port.Frame, IsLegacy ? Port.PhaseOffset : 0, Port.Kind), Shaft);
    internal Rational? PortSign => PortIsMounted ? Shaft!.Frame.Z.Dot(Port!.Frame.Z) : null;
    internal bool HasBodyId(string id) => LegacySource?.Definition.Bodies.Any(b => b.Id == id) == true;
    internal bool HasShaftId(string id) => LegacySource?.Definition.Shafts.Any(s => s.Id == id) == true;
    internal bool HasPortId(string id) => LegacySource?.Definition.Ports.Any(p => p.Id == id) == true;
    internal bool HasOutputId(string id) => LegacySource?.Definition.Outputs.Any(o => o.Key == id) == true;

    internal static BoundMechanicalInputContext FromLegacy(MechanicalDraft source, SourceLengthMapping? mapping,
        string shaftId, string? portId)
    {
        var shaft = source.Definition.Shafts.FirstOrDefault(s => s.Id == shaftId);
        var port = portId is null ? null : source.Definition.Ports.FirstOrDefault(p => p.Id == portId);
        var valid = shaft?.Frame.IsProperCardinal == true && mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal;
        return new(source.DefinitionId, source.DraftId, source.Definition.RootShaftId, null,
            AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, shaftId), shaft, port, null,
            null, valid, Array.Empty<string>(), mapping, source, null);
    }

    // Called only by the assembly compiler after resolving owner, topology, actual shaft and dependency evidence.
    internal static BoundMechanicalInputContext FromAssembly(string definitionIdentity, string dependencyIdentity,
        string? rootInputId, string consumerMemberId, AssemblyComponentReference upstream,
        OrientedShaft? shaftInAssemblyMm, ShaftPort? portInAssemblyMm, ExactAffineRelation? globalRelation,
        bool bindingAdmitted, IEnumerable<string> dependencyPath, ExactQuantity mountingStation,
        AssemblyGenevaAffineMotion? genevaMotion = null) =>
        new(definitionIdentity, dependencyIdentity, rootInputId, consumerMemberId, upstream, shaftInAssemblyMm, portInAssemblyMm,
            shaftInAssemblyMm?.Frame, globalRelation, bindingAdmitted, dependencyPath, null, null, mountingStation, genevaMotion);
}
