using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public sealed class TwoOutputTransmissionProjectManifest
{
    public TwoOutputTransmissionProjectManifest(string goalId, string planId, IEnumerable<GearRoutingProjectFile> files, TwoOutputTransmissionStatus? status = null,
        bool searchComplete = false, bool resultTruncated = false, IEnumerable<string>? orderedIds = null, string? selectedId = null, string? selectedContextId = null,
        GearRoutingPlaybackSetup? playback = null, IEnumerable<KeyValuePair<string, string>>? labels = null,
        string profile = TwoOutputTransmissionContract.Profile, string lowering = TwoOutputTransmissionContract.Lowering, string allocation = TwoOutputTransmissionContract.Allocation,
        string ranking = TwoOutputTransmissionContract.Ranking, string resources = TwoOutputTransmissionContract.Resources,
        IEnumerable<string>? fingerprints = null, TwoOutputSharingPolicy sharingPolicy = TwoOutputSharingPolicy.AutoWithinAllowedProfiles)
    {
        GoalId = goalId; PlanId = planId; Files = GearRoutingContract.Bounded(files, 3, out var f); OrderedIds = GearRoutingContract.Bounded(orderedIds, TwoOutputTransmissionContract.MaxReturned, out var o);
        Labels = GearRoutingContract.Bounded(labels, 2, out var l); BackendFingerprints = GearRoutingContract.Bounded(fingerprints ?? TwoOutputTransmissionContract.Fingerprints(sharingPolicy), 5, out var b);
        if (f || o || l || b) throw new ArgumentException("Shared project bounded members exceeded.");
        Files = Files.OrderBy(x => x.Role, StringComparer.Ordinal).ToList().AsReadOnly(); Labels = Labels.OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        SharingPolicy = sharingPolicy; Status = status; SearchComplete = searchComplete; ResultTruncated = resultTruncated; SelectedId = selectedId; SelectedContextId = selectedContextId;
        Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8)); Profile = profile; Lowering = lowering; Allocation = allocation; Ranking = ranking; Resources = resources;
    }
    public TwoOutputSharingPolicy SharingPolicy { get; }
    public string GoalId { get; }
    public string PlanId { get; }
    public ReadOnlyCollection<GearRoutingProjectFile> Files { get; }
    public TwoOutputTransmissionStatus? Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public ReadOnlyCollection<string> OrderedIds { get; }
    public string? SelectedId { get; }
    public string? SelectedContextId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public ReadOnlyCollection<KeyValuePair<string, string>> Labels { get; }
    public string Profile { get; }
    public string Lowering { get; }
    public string Allocation { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public ReadOnlyCollection<string> BackendFingerprints { get; }
}

