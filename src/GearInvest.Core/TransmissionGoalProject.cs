using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public sealed class TransmissionGoalProjectManifest
{
    public TransmissionGoalProjectManifest(string goalId, string planId, IEnumerable<GearRoutingProjectFile> files, TransmissionGoalStatus? status = null,
        bool searchComplete = false, bool resultTruncated = false, IEnumerable<string>? orderedIds = null, string? selectedId = null, string? selectedOriginId = null,
        string? selectedContextId = null, GearRoutingPlaybackSetup? playback = null, string profile = TransmissionGoalContract.Profile,
        string lowering = TransmissionGoalContract.Lowering, string allocation = TransmissionGoalContract.Allocation, string ranking = TransmissionGoalContract.Ranking,
        string resources = TransmissionGoalContract.Resources, IEnumerable<string>? backendFingerprints = null)
    {
        GoalId = goalId; PlanId = planId; Files = GearRoutingContract.Bounded(files, 3, out var f); if (f) throw new ArgumentException("Goal project file ceiling");
        Files = Files.OrderBy(x => x.Role, StringComparer.Ordinal).ToList().AsReadOnly(); OrderedIds = GearRoutingContract.Bounded(orderedIds, 128, out var ids); if (ids) throw new ArgumentException("Goal project ID ceiling");
        Status = status; SearchComplete = searchComplete; ResultTruncated = resultTruncated; SelectedId = selectedId; SelectedOriginId = selectedOriginId; SelectedContextId = selectedContextId;
        Playback = playback ?? new GearRoutingPlaybackSetup(Rational.Zero, new Rational(1, 8)); Profile = profile; Lowering = lowering; Allocation = allocation; Ranking = ranking; Resources = resources;
        BackendFingerprints = GearRoutingContract.Bounded(backendFingerprints ?? new[] { TransmissionFamily.SimpleIdler, TransmissionFamily.OneCompound, TransmissionFamily.TwoCompound }.Select(TransmissionGoalContract.Fingerprint), 3, out var b);
        if (b) throw new ArgumentException("Backend fingerprint ceiling");
    }
    public string GoalId { get; }
    public string PlanId { get; }
    public ReadOnlyCollection<GearRoutingProjectFile> Files { get; }
    public TransmissionGoalStatus? Status { get; }
    public bool SearchComplete { get; }
    public bool ResultTruncated { get; }
    public ReadOnlyCollection<string> OrderedIds { get; }
    public string? SelectedId { get; }
    public string? SelectedOriginId { get; }
    public string? SelectedContextId { get; }
    public GearRoutingPlaybackSetup Playback { get; }
    public string Profile { get; }
    public string Lowering { get; }
    public string Allocation { get; }
    public string Ranking { get; }
    public string Resources { get; }
    public ReadOnlyCollection<string> BackendFingerprints { get; }
}
