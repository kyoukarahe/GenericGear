using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public sealed class SharedDriverTransmissionProjectManifest
{
    public SharedDriverTransmissionProjectManifest(string goalId, string planId, IEnumerable<GearRoutingProjectFile> files, SharedDriverTransmissionStatus? status = null,
        bool searchComplete = false, bool resultTruncated = false, IEnumerable<string>? orderedIds = null, string? selectedId = null, string? selectedContextId = null,
        GearRoutingPlaybackSetup? playback = null, IEnumerable<KeyValuePair<string, string>>? labels = null,
        string profile = SharedDriverTransmissionContract.Profile, string lowering = SharedDriverTransmissionContract.Lowering, string allocation = SharedDriverTransmissionContract.Allocation,
        string join = SharedDriverTransmissionContract.Join, string ranking = SharedDriverTransmissionContract.Ranking, string resources = SharedDriverTransmissionContract.Resources,
        IEnumerable<string>? fingerprints = null)
    {
        GoalId = goalId; PlanId = planId; Files = GearRoutingContract.Bounded(files, 3, out var f); OrderedIds = GearRoutingContract.Bounded(orderedIds, SharedDriverTransmissionContract.MaxReturned, out var o);
        Labels = GearRoutingContract.Bounded(labels, 2, out var l); BackendFingerprints = GearRoutingContract.Bounded(fingerprints ?? new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }.Select(TransmissionGoalContract.Fingerprint), 3, out var b);
        if (f || o || l || b) throw new ArgumentException("Shared project bounded members exceeded.");
        Files = Files.OrderBy(x => x.Role, StringComparer.Ordinal).ToList().AsReadOnly(); Labels = Labels.OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Status = status; SearchComplete = searchComplete; ResultTruncated = resultTruncated; SelectedId = selectedId; SelectedContextId = selectedContextId;
        Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8)); Profile = profile; Lowering = lowering; Allocation = allocation; Join = join; Ranking = ranking; Resources = resources;
    }
    public string GoalId { get; }
    public string PlanId { get; }
    public ReadOnlyCollection<GearRoutingProjectFile> Files { get; }
    public SharedDriverTransmissionStatus? Status { get; }
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
    public string Join { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public ReadOnlyCollection<string> BackendFingerprints { get; }
}
