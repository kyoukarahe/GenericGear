using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public sealed class TwoCompoundRoutingProjectManifest
{
    public TwoCompoundRoutingProjectManifest(string requestId, IEnumerable<GearRoutingProjectFile> files, GearRoutingStatus? status = null,
        bool searchComplete = false, bool resultTruncated = false, IEnumerable<string>? orderedIds = null, string? selectedId = null,
        GearRoutingPlaybackSetup? playback = null, string profile = TwoCompoundRoutingContract.Profile, string backend = TwoCompoundRoutingContract.Backend,
        string normalization = TwoCompoundRoutingContract.Normalization, string ranking = TwoCompoundRoutingContract.Ranking)
    {
        RequestId = requestId; var f = files.Take(4).ToList(); if (f.Count > 3) throw new ArgumentException("Project file ceiling"); Files = f.OrderBy(x => x.Role,StringComparer.Ordinal).ToList().AsReadOnly();
        OrderedIds = GearRoutingContract.Bounded(orderedIds,GearRoutingContract.MaxReturned,out var exceeded); if (exceeded) throw new ArgumentException("Project ID ceiling");
        Status = status; SearchComplete = searchComplete; ResultTruncated = resultTruncated; SelectedId = selectedId;
        Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero,new Rational(1,8)); Profile = profile; Backend = backend; Normalization = normalization; Ranking = ranking;
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
    public string Normalization { get; }
    public string Ranking { get; }
    public string? ContextId => SelectedId == null ? null : TwoCompoundRoutingContract.ContextId(RequestId,SelectedId);
}
