using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class PlaybackDriver
{
    public PlaybackDriver(string id, string rootDofId)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        RootDofId = rootDofId ?? throw new ArgumentNullException(nameof(rootDofId));
    }

    public string Id { get; }

    public string RootDofId { get; }
}

public sealed class PlaybackChannel
{
    public PlaybackChannel(
        string dofId,
        string driverId,
        Rational exactCoefficient,
        Rational exactPhaseOffset)
    {
        DofId = dofId ?? throw new ArgumentNullException(nameof(dofId));
        DriverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
        ExactCoefficient = exactCoefficient;
        ExactPhaseOffset = exactPhaseOffset;
    }

    public string DofId { get; }

    public string DriverId { get; }

    public Rational ExactCoefficient { get; }

    public Rational ExactPhaseOffset { get; }
}

public sealed class PlaybackBodyBinding
{
    public PlaybackBodyBinding(string bodyId, string dofId, Rational exactMountingPhase)
    {
        BodyId = bodyId ?? throw new ArgumentNullException(nameof(bodyId));
        DofId = dofId ?? throw new ArgumentNullException(nameof(dofId));
        ExactMountingPhase = exactMountingPhase;
    }

    public string BodyId { get; }

    public string DofId { get; }

    public Rational ExactMountingPhase { get; }
}

public sealed class ResolvedPlayback
{
    public ResolvedPlayback(
        IEnumerable<PlaybackDriver> drivers,
        IEnumerable<PlaybackChannel> channels,
        IEnumerable<PlaybackBodyBinding> bodyBindings)
    {
        Drivers = Sort(drivers, item => item.Id);
        Channels = Sort(channels, item => item.DofId);
        BodyBindings = Sort(bodyBindings, item => item.BodyId);
    }

    public ReadOnlyCollection<PlaybackDriver> Drivers { get; }

    public ReadOnlyCollection<PlaybackChannel> Channels { get; }

    public ReadOnlyCollection<PlaybackBodyBinding> BodyBindings { get; }

    private static ReadOnlyCollection<T> Sort<T>(IEnumerable<T> source, Func<T, string> idSelector)
    {
        var items = source?.ToList() ?? throw new ArgumentNullException(nameof(source));
        items.Sort((left, right) => StringComparer.Ordinal.Compare(idSelector(left), idSelector(right)));
        return items.AsReadOnly();
    }
}

public static class ResolvedPlaybackBuilder
{
    public static ResolvedPlayback Build(KinematicSolution solution, SpatialMechanism spatial)
    {
        if (solution is null)
        {
            throw new ArgumentNullException(nameof(solution));
        }

        if (spatial is null)
        {
            throw new ArgumentNullException(nameof(spatial));
        }

        var driverId = "driver:" + solution.RootDofId;
        return new ResolvedPlayback(
            new[] { new PlaybackDriver(driverId, solution.RootDofId) },
            solution.States.Select(state => new PlaybackChannel(
                state.DofId,
                driverId,
                state.Coefficient,
                state.PhaseOffset)),
            spatial.Bodies.Select(body => new PlaybackBodyBinding(
                body.Id,
                body.DofId,
                body.ExactMountingPhase)));
    }
}

public static class ResolvedPlaybackValidator
{
    public static ValidationReport Validate(
        ResolvedPlayback playback,
        KinematicSolution solution,
        SpatialMechanism spatial)
    {
        if (playback is null)
        {
            throw new ArgumentNullException(nameof(playback));
        }

        if (solution is null)
        {
            throw new ArgumentNullException(nameof(solution));
        }

        if (spatial is null)
        {
            throw new ArgumentNullException(nameof(spatial));
        }

        var diagnostics = new List<Diagnostic>();
        var expectedDriverId = "driver:" + solution.RootDofId;
        if (playback.Drivers.Count != 1 ||
            !StringComparer.Ordinal.Equals(playback.Drivers[0].Id, expectedDriverId) ||
            !StringComparer.Ordinal.Equals(playback.Drivers[0].RootDofId, solution.RootDofId))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.PlaybackDriverMismatch,
                DiagnosticSeverity.Error,
                "Resolved playback does not contain the expected root driver.",
                solution.RootDofId));
        }

        var channels = playback.Channels
            .GroupBy(item => item.DofId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var state in solution.States)
        {
            if (!channels.TryGetValue(state.DofId, out var matches) ||
                matches.Count != 1 ||
                !StringComparer.Ordinal.Equals(matches[0].DriverId, expectedDriverId) ||
                matches[0].ExactCoefficient != state.Coefficient ||
                matches[0].ExactPhaseOffset != state.PhaseOffset)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PlaybackChannelMismatch,
                    DiagnosticSeverity.Error,
                    $"Resolved playback channel for DOF '{state.DofId}' does not match the Kinematic solution.",
                    state.DofId));
            }
        }

        foreach (var extra in channels.Keys.Except(solution.States.Select(state => state.DofId), StringComparer.Ordinal))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.PlaybackChannelMismatch,
                DiagnosticSeverity.Error,
                $"Resolved playback contains unknown DOF channel '{extra}'.",
                extra));
        }

        var bindings = playback.BodyBindings
            .GroupBy(item => item.BodyId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var body in spatial.Bodies)
        {
            if (!bindings.TryGetValue(body.Id, out var matches) ||
                matches.Count != 1 ||
                !StringComparer.Ordinal.Equals(matches[0].DofId, body.DofId) ||
                matches[0].ExactMountingPhase != body.ExactMountingPhase)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PlaybackBodyBindingMismatch,
                    DiagnosticSeverity.Error,
                    $"Resolved playback binding for body '{body.Id}' does not match Spatial IR.",
                    body.Id));
            }
        }

        foreach (var extra in bindings.Keys.Except(spatial.Bodies.Select(body => body.Id), StringComparer.Ordinal))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.PlaybackBodyBindingMismatch,
                DiagnosticSeverity.Error,
                $"Resolved playback contains unknown body binding '{extra}'.",
                extra));
        }

        return new ValidationReport(diagnostics);
    }
}
