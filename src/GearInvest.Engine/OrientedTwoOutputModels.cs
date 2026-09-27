using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static class OrientedTwoOutputProfile
{
    public const string Id = "cardinal-shared-shaft-two-output-transmission-v1";
    public const string RefinedId = "cardinal-shared-shaft-two-output-pitch-refined-v1";
    public static bool IsSupported(string profile) => profile == Id || profile == RefinedId;
    public static string ClearancePolicy(string profile) => profile == RefinedId ? PitchClearancePolicy.Refined :
        profile == Id ? PitchClearancePolicy.Legacy : throw new ArgumentException("Unsupported two-output profile.");
    public const int MaxShafts = 32, MaxBodies = 34, MaxContacts = 31, MaxPorts = 6;
}

public enum OrientedOutputRole { ParallelBranch, TurnedBranch }

/// <summary>Explicit terminal in its branch's source-local namespace (bevel namespace when B is absent).
/// The terminal port frame is in assembly-local coordinates. Target refers to port-coordinate turns.</summary>
public sealed class OrientedOutputRequest
{
    public OrientedOutputRequest(string key, OrientedOutputRole role, string terminalBodyId, ShaftPort terminalPort, Rational? requestedTransfer = null)
    { Key = key; Role = role; TerminalBodyId = terminalBodyId; TerminalPort = terminalPort; RequestedTransfer = requestedTransfer; }
    public string Key { get; }
    public OrientedOutputRole Role { get; }
    public string TerminalBodyId { get; }
    public ShaftPort TerminalPort { get; }
    public Rational? RequestedTransfer { get; }
}

public sealed class OrientedOutputBinding
{
    public OrientedOutputBinding(string key, OrientedOutputRole role, string shaftId, string bodyId, string portId)
    { Key = key; Role = role; ShaftId = shaftId; BodyId = bodyId; PortId = portId; }
    public string Key { get; }
    public OrientedOutputRole Role { get; }
    public string ShaftId { get; }
    public string BodyId { get; }
    public string PortId { get; }
}

public sealed class OrientedTwoOutputCompositionRequest
{
    public OrientedTwoOutputCompositionRequest(RightAngleBevelRequest bevel, PlanarShaftModule parallelBranch,
        IEnumerable<OrientedOutputRequest> outputs, PlanarShaftModule? turnedBranch = null, OrientedFrame? assemblyPose = null,
        bool requireCrossComponentClearance = true, IEnumerable<OrientedKeepOut>? keepOuts = null, string profile = OrientedTwoOutputProfile.Id)
    {
        Bevel = bevel; ParallelBranch = parallelBranch; TurnedBranch = turnedBranch;
        Outputs = OrientedCollections.Order(outputs, o => o.Key); AssemblyPose = assemblyPose ?? OrientedFrame.Identity;
        RequireCrossComponentClearance = requireCrossComponentClearance; KeepOuts = OrientedCollections.Order(keepOuts ?? Array.Empty<OrientedKeepOut>(), k => k.Id); Profile = profile;
    }
    public string Profile { get; }
    public RightAngleBevelRequest Bevel { get; }
    public PlanarShaftModule ParallelBranch { get; }
    public PlanarShaftModule? TurnedBranch { get; }
    public ReadOnlyCollection<OrientedOutputRequest> Outputs { get; }
    public OrientedFrame AssemblyPose { get; }
    public bool RequireCrossComponentClearance { get; }
    public ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
}

/// <summary>Canonical mechanical truth for exactly two explicit output terminals sharing one prescribed driver.</summary>
public sealed class OrientedTwoOutputMechanism : IOrientedGraph
{
    public OrientedTwoOutputMechanism(string rootShaftId, IEnumerable<OrientedOutputBinding> outputs, IEnumerable<OrientedShaft> shafts,
        IEnumerable<OrientedGearBody> bodies, IEnumerable<OrientedGearContact> contacts, IEnumerable<ShaftPort> ports,
        IEnumerable<ShaftPortConnection> connections, IEnumerable<SourceShaftMapping> sourceMappings,
        IEnumerable<OrientedSourceIdentity> sources, KinematicSolution solution, bool requireCrossComponentClearance,
        IEnumerable<OrientedKeepOut> keepOuts, string profile = OrientedTwoOutputProfile.Id)
    {
        RootShaftId = rootShaftId; Outputs = OrientedCollections.Order(outputs, o => o.Key); Shafts = OrientedCollections.Order(shafts, s => s.Id);
        Bodies = OrientedCollections.Order(bodies, b => b.Id); Contacts = OrientedCollections.Order(contacts, c => c.Id); Ports = OrientedCollections.Order(ports, p => p.Id);
        Connections = OrientedCollections.Order(connections, c => c.Id); SourceMappings = OrientedCollections.Order(sourceMappings, m => m.ModuleId + "/" + m.SourceDofId);
        Sources = OrientedCollections.Order(sources, s => s.ModuleId); Solution = solution; RequireCrossComponentClearance = requireCrossComponentClearance;
        KeepOuts = OrientedCollections.Order(keepOuts, k => k.Id); Profile = profile;
    }
    public string Profile { get; }
    public string RootShaftId { get; }
    public ReadOnlyCollection<OrientedOutputBinding> Outputs { get; }
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

public sealed class OrientedTwoOutputResult
{
    public OrientedTwoOutputResult(OrientedOperationStatus status, OrientedTwoOutputMechanism? mechanism, OrientedValidation validation)
    { Status = status; Mechanism = mechanism; Validation = validation; }
    public OrientedOperationStatus Status { get; }
    public OrientedTwoOutputMechanism? Mechanism { get; }
    public OrientedValidation Validation { get; }
    public bool IsSuccess => Status == OrientedOperationStatus.Complete && Mechanism is not null && Validation.IsValid;
}

public sealed class OrientedOutputEvaluation
{
    public OrientedOutputEvaluation(OrientedOutputBinding binding, OrientedShaft shaft, ShaftPort port, Rational shaftCoefficient, Rational rootTurns)
    {
        Binding = binding; ShaftCoefficient = shaftCoefficient; ShaftTurns = rootTurns * shaftCoefficient;
        PortCoordinateSign = port.Frame.Z.Dot(shaft.Frame.Z); PortCoefficient = PortCoordinateSign * shaftCoefficient;
        PortTurns = rootTurns * PortCoefficient; ShaftPositiveAxis = shaft.Frame.Z; PortPositiveAxis = port.Frame.Z;
        WorldAngularVelocityPerRoot = shaft.Frame.Z * shaftCoefficient;
    }
    public OrientedOutputBinding Binding { get; }
    public Rational ShaftTurns { get; }
    public Rational ShaftCoefficient { get; }
    public Rational PortTurns { get; }
    public Rational PortCoefficient { get; }
    public Rational PortCoordinateSign { get; }
    public ExactVector3 ShaftPositiveAxis { get; }
    public ExactVector3 PortPositiveAxis { get; }
    public ExactVector3 WorldAngularVelocityPerRoot { get; }
}

public sealed class OrientedTwoOutputEvaluation
{
    public OrientedTwoOutputEvaluation(Rational rootTurns, IEnumerable<OrientedShaftEvaluation> shafts, IEnumerable<OrientedOutputEvaluation> outputs)
    { RootTurns = rootTurns; Shafts = OrientedCollections.Order(shafts, s => s.ShaftId); Outputs = OrientedCollections.Order(outputs, o => o.Binding.Key); }
    public Rational RootTurns { get; }
    public ReadOnlyCollection<OrientedShaftEvaluation> Shafts { get; }
    public ReadOnlyCollection<OrientedOutputEvaluation> Outputs { get; }
}
