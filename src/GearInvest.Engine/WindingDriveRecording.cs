using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Bounded ordered drive requests and their outcomes. Failed attempts never change the current frame.</summary>
public sealed class WindingDriveRecording
{
    internal WindingDriveRecording(WindingDifferentialAnalysis a, Rational initialPlanetPortTurns, ConnectedFrame initial, IEnumerable<ConnectedAdvance> attempts)
    { Analysis = a; InitialPlanetPortTurns = initialPlanetPortTurns; Initial = initial; Attempts = attempts.ToList().AsReadOnly(); }
    public WindingDifferentialAnalysis Analysis { get; }
    public Rational InitialPlanetPortTurns { get; }
    public ConnectedFrame Initial { get; }
    public ReadOnlyCollection<ConnectedAdvance> Attempts { get; }
    public ConnectedFrame Final => Attempts.LastOrDefault(a => a.IsAccepted)?.Frame ?? Initial;
}

public static partial class WindingDifferentialEngine
{
    public static WindingDriveRecording Record(WindingDifferentialAnalysis a, Rational initialPlanetPortTurns, IEnumerable<IEnumerable<WindingDriveInput>> requests)
    {
        var paths = requests.Take(17).Select(p => p.Take(17).ToArray()).ToArray();
        if (paths.Length > 16 || paths.Any(p => p.Length > 16) || paths.Sum(p => p.Length) > 64) throw new ArgumentException("Recording resource limit.");
        var first = Evaluate(a, new(a.Source.Winding.InitialDriverTurns, initialPlanetPortTurns));
        if (!first.IsAccepted) throw new ArgumentException(first.Status);
        var current = first.Frame!; var attempts = new List<ConnectedAdvance>();
        foreach (var path in paths) { var result = Advance(a, current, path); attempts.Add(result); if (result.IsAccepted) current = result.Frame!; }
        return new(a, initialPlanetPortTurns, first.Frame!, attempts);
    }
}
