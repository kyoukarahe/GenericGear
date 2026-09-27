using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public sealed class GearRoutingPlaybackSetup
{
    public GearRoutingPlaybackSetup(Rational rootTurns, Rational turnsPerSecond) { RootTurns = rootTurns; TurnsPerSecond = turnsPerSecond; }
    public Rational RootTurns { get; }
    public Rational TurnsPerSecond { get; }
}
public sealed class GearRoutingProjectFile
{
    public GearRoutingProjectFile(string role, string path, int bytes, string sha256) { Role = role; Path = path; Bytes = bytes; Sha256 = sha256; }
    public string Role { get; }
    public string Path { get; }
    public int Bytes { get; }
    public string Sha256 { get; }
}
public sealed class GearRoutingProjectManifest
{
    public GearRoutingProjectManifest(string requestId, IEnumerable<GearRoutingProjectFile> files, GearRoutingStatus? status = null,
        bool searchComplete = false, bool resultTruncated = false, IEnumerable<string>? orderedIds = null, string? selectedId = null,
        GearRoutingPlaybackSetup? playback = null, string profile = GearRoutingContract.Profile, string backend = GearRoutingContract.Backend, string ranking = GearRoutingContract.Ranking)
    {
        RequestId = requestId; var f = files.Take(4).ToList(); if (f.Count > 3) throw new ArgumentException("Project file ceiling"); Files = f.OrderBy(x => x.Role, StringComparer.Ordinal).ToList().AsReadOnly();
        OrderedIds = GearRoutingContract.Bounded(orderedIds, GearRoutingContract.MaxReturned, out var exceeded); if (exceeded) throw new ArgumentException("Project ID ceiling");
        Status = status; SearchComplete = searchComplete; ResultTruncated = resultTruncated; SelectedId = selectedId;
        Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8)); Profile = profile; Backend = backend; Ranking = ranking;
    }
    public string RequestId { get; }
    public ReadOnlyCollection<GearRoutingProjectFile> Files { get; }
    public GearRoutingStatus? Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public ReadOnlyCollection<string> OrderedIds { get; }
    public string? SelectedId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public string Profile { get; }
    public string Backend { get; }
    public string Ranking { get; }
    public string? ContextId => SelectedId == null ? null : GearRoutingContract.Id("gear-route-context", GearRoutingContract.Pack(RequestId, SelectedId));
}
