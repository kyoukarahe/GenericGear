using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class OrientedTransmissionProfile
{
    public const string Id = "cardinal-right-angle-pitch-cone-transmission-v1";
    public const int MaxSourceShafts = 16;
    public const int MaxTeeth = 4096;
}

public sealed class BevelGearMount
{
    public BevelGearMount(OrientedShaft shaft, ExactVector3 coneDirection, int teeth, Rational outerPitchRadiusPerTooth,
        ExactVector3 fixedCenter, ShaftPort port)
    { Shaft = shaft; ConeDirection = coneDirection; Teeth = teeth; OuterPitchRadiusPerTooth = outerPitchRadiusPerTooth; FixedCenter = fixedCenter; Port = port; }
    public OrientedShaft Shaft { get; }
    public ExactVector3 ConeDirection { get; }
    public int Teeth { get; }
    public Rational OuterPitchRadiusPerTooth { get; }
    public ExactVector3 FixedCenter { get; }
    public ShaftPort Port { get; }
}

public sealed class RightAngleBevelRequest
{
    public RightAngleBevelRequest(ExactVector3 apex, BevelGearMount input, BevelGearMount output, Rational innerParameter,
        Rational? requestedTransfer = null, string profile = OrientedTransmissionProfile.Id)
    { Apex = apex; Input = input; Output = output; InnerParameter = innerParameter; RequestedTransfer = requestedTransfer; Profile = profile; }
    public ExactVector3 Apex { get; }
    public BevelGearMount Input { get; }
    public BevelGearMount Output { get; }
    public Rational InnerParameter { get; }
    public Rational? RequestedTransfer { get; }
    public string Profile { get; }
}

/// <summary>Engine-level imported mathematics. The serialization/façade boundary verifies the original source bytes and identities.</summary>
public sealed class PlanarShaftModule
{
    public PlanarShaftModule(GenerationCandidate source, string candidateId, string artifactHash, OrientedFrame pose,
        string inputDofId, string outputDofId, ShaftPort connectionPort)
    { Source = source; CandidateId = candidateId; ArtifactHash = artifactHash; Pose = pose; InputDofId = inputDofId; OutputDofId = outputDofId; ConnectionPort = connectionPort; }
    public GenerationCandidate Source { get; }
    public string CandidateId { get; }
    public string ArtifactHash { get; }
    public OrientedFrame Pose { get; }
    public string InputDofId { get; }
    public string OutputDofId { get; }
    public ShaftPort ConnectionPort { get; }
}

public sealed class OrientedKeepOut
{
    public OrientedKeepOut(string id, ExactEnvelope3 envelope) { Id = id; Envelope = envelope; }
    public string Id { get; }
    public ExactEnvelope3 Envelope { get; }
}

public sealed class OrientedCompositionRequest
{
    public OrientedCompositionRequest(RightAngleBevelRequest bevel, PlanarShaftModule? upstream = null, PlanarShaftModule? downstream = null,
        OrientedFrame? assemblyPose = null, Rational? requestedTransfer = null, bool requireCrossComponentClearance = true,
        IEnumerable<OrientedKeepOut>? keepOuts = null)
    {
        Bevel = bevel; Upstream = upstream; Downstream = downstream; AssemblyPose = assemblyPose ?? OrientedFrame.Identity;
        RequestedTransfer = requestedTransfer; RequireCrossComponentClearance = requireCrossComponentClearance;
        KeepOuts = OrientedCollections.Order(keepOuts ?? Array.Empty<OrientedKeepOut>(), k => k.Id);
    }
    public RightAngleBevelRequest Bevel { get; }
    public PlanarShaftModule? Upstream { get; }
    public PlanarShaftModule? Downstream { get; }
    public OrientedFrame AssemblyPose { get; }
    public Rational? RequestedTransfer { get; }
    public bool RequireCrossComponentClearance { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
}

public sealed class ShaftPortConnection
{
    public ShaftPortConnection(string id, string portAId, string portBId, Rational coordinateTransfer)
    { Id = id; PortAId = portAId; PortBId = portBId; CoordinateTransfer = coordinateTransfer; }
    public string Id { get; }
    public string PortAId { get; }
    public string PortBId { get; }
    public Rational CoordinateTransfer { get; }
}

public sealed class SourceShaftMapping
{
    public SourceShaftMapping(string moduleId, string sourceDofId, string shaftId, Rational coordinateTransfer)
    { ModuleId = moduleId; SourceDofId = sourceDofId; ShaftId = shaftId; CoordinateTransfer = coordinateTransfer; }
    public string ModuleId { get; }
    public string SourceDofId { get; }
    public string ShaftId { get; }
    /// <summary>source-local turns = this signed factor * assembly shaft turns.</summary>
    public Rational CoordinateTransfer { get; }
}

public sealed class OrientedSourceIdentity
{
    public OrientedSourceIdentity(string moduleId, string candidateId, string artifactHash)
    { ModuleId = moduleId; CandidateId = candidateId; ArtifactHash = artifactHash; }
    public string ModuleId { get; }
    public string CandidateId { get; }
    public string ArtifactHash { get; }
}

public sealed class OrientedMechanism : IOrientedGraph
{
    public OrientedMechanism(string rootShaftId, string outputShaftId, IEnumerable<OrientedShaft> shafts,
        IEnumerable<OrientedGearBody> bodies, IEnumerable<OrientedGearContact> contacts, IEnumerable<ShaftPort> ports,
        IEnumerable<ShaftPortConnection> connections, IEnumerable<SourceShaftMapping> sourceMappings,
        IEnumerable<OrientedSourceIdentity> sources, KinematicSolution solution, bool requireCrossComponentClearance,
        IEnumerable<OrientedKeepOut> keepOuts, string profile = OrientedTransmissionProfile.Id)
    {
        RootShaftId = rootShaftId; OutputShaftId = outputShaftId; Shafts = OrientedCollections.Order(shafts, s => s.Id);
        Bodies = OrientedCollections.Order(bodies, b => b.Id); Contacts = OrientedCollections.Order(contacts, c => c.Id);
        Ports = OrientedCollections.Order(ports, p => p.Id); Connections = OrientedCollections.Order(connections, c => c.Id);
        SourceMappings = OrientedCollections.Order(sourceMappings, m => m.ModuleId + "/" + m.SourceDofId);
        Sources = OrientedCollections.Order(sources, s => s.ModuleId); Solution = solution;
        RequireCrossComponentClearance = requireCrossComponentClearance; KeepOuts = OrientedCollections.Order(keepOuts, k => k.Id); Profile = profile;
    }
    public string Profile { get; }
    public string RootShaftId { get; }
    public string OutputShaftId { get; }
    public ReadOnlyCollection<OrientedShaft> Shafts { get; }
    public ReadOnlyCollection<OrientedGearBody> Bodies { get; }
    public ReadOnlyCollection<OrientedGearContact> Contacts { get; }
    public ReadOnlyCollection<ShaftPort> Ports { get; }
    public ReadOnlyCollection<ShaftPortConnection> Connections { get; }
    public ReadOnlyCollection<SourceShaftMapping> SourceMappings { get; }
    public ReadOnlyCollection<OrientedSourceIdentity> Sources { get; }
    public KinematicSolution Solution { get; }
    public bool RequireCrossComponentClearance { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
}

public enum OrientedOperationStatus { Complete, InvalidInput, Unsupported, ConstraintFailure, Cancelled }

public sealed class OrientedTransmissionResult
{
    public OrientedTransmissionResult(OrientedOperationStatus status, OrientedMechanism? mechanism, OrientedValidation validation)
    { Status = status; Mechanism = mechanism; Validation = validation; }
    public OrientedOperationStatus Status { get; }
    public OrientedMechanism? Mechanism { get; }
    public OrientedValidation Validation { get; }
    public bool IsSuccess => Status == OrientedOperationStatus.Complete && Mechanism is not null && Validation.IsValid;
}

public sealed class OrientedShaftEvaluation
{
    public OrientedShaftEvaluation(string shaftId, Rational turns, ExactVector3 axis, Rational coefficient)
    { ShaftId = shaftId; Turns = turns; PositiveAxis = axis; WorldAngularVelocityPerRoot = axis * coefficient; }
    public string ShaftId { get; }
    public Rational Turns { get; }
    public ExactVector3 PositiveAxis { get; }
    /// <summary>World rotation vector per unit root angular rate (not turns as a physical vector orientation).</summary>
    public ExactVector3 WorldAngularVelocityPerRoot { get; }
}

internal static class OrientedCollections
{
    internal static ReadOnlyCollection<T> Order<T>(IEnumerable<T> source, Func<T, string> key) => source.OrderBy(key, StringComparer.Ordinal).ToList().AsReadOnly();
}
