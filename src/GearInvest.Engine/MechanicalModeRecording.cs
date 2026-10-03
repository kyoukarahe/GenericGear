using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

public sealed class MechanicalModeRecording
{
    private MechanicalModeRecording(MechanicalModeDefinition d,Rational initialPlanet,MechanicalModeState initial,IEnumerable<MechanicalModeAdvance> attempts)
    {Definition=d;InitialPlanetPortTurns=initialPlanet;Initial=initial;Attempts=attempts.ToList().AsReadOnly();}
    public MechanicalModeDefinition Definition{get;}
    public Rational InitialPlanetPortTurns{get;}
    public MechanicalModeState Initial{get;}
    public ReadOnlyCollection<MechanicalModeAdvance> Attempts{get;}
    public MechanicalModeState Final=>Attempts.LastOrDefault(a=>a.IsAccepted)?.State??Initial;
    public static MechanicalModeRecording Rebuild(MechanicalModeDefinition d,Rational initialPlanet,IEnumerable<MechanicalModeRequest> requests)
    {
        var rr=requests.Take(33).ToArray();if(rr.Length>32||rr.Sum(r=>r.Segments.Count+r.Segments.Sum(s=>s.Events.Count))>64)throw new ArgumentException("Mode recording resource limit.");
        var initial=MechanicalModeEngine.Start(d,initialPlanet);var state=initial;var attempts=new List<MechanicalModeAdvance>();
        foreach(var r in rr){var next=MechanicalModeEngine.Advance(d,state,r);attempts.Add(next);if(next.IsAccepted)state=next.State!;}
        return new(d,initialPlanet,initial,attempts);
    }
}
