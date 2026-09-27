using System.Collections.ObjectModel;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

// Internal read-only graph view shared by the two bounded public profiles. It
// does not adapt a two-output result to the old single-output artifact format.
internal interface IOrientedGraph
{
    string RootShaftId { get; }
    ReadOnlyCollection<OrientedShaft> Shafts { get; }
    ReadOnlyCollection<OrientedGearBody> Bodies { get; }
    ReadOnlyCollection<OrientedGearContact> Contacts { get; }
    ReadOnlyCollection<ShaftPort> Ports { get; }
    ReadOnlyCollection<ShaftPortConnection> Connections { get; }
    ReadOnlyCollection<SourceShaftMapping> SourceMappings { get; }
    ReadOnlyCollection<OrientedSourceIdentity> Sources { get; }
    KinematicSolution Solution { get; }
    bool RequireCrossComponentClearance { get; }
    ReadOnlyCollection<OrientedKeepOut> KeepOuts { get; }
}
