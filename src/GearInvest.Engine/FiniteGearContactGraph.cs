using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

/// <summary>Shared 15A/15B exact plane queries. No output cap, work budget, ranking or mechanical identity.</summary>
internal sealed class FiniteGearContactGraph
{
    private FiniteGearContactGraph(AnchoredGearRoutingRequest request)
    {
        Request = request; Nodes = new List<GearRouteAssignment> { new GearRouteAssignment(request.Input.Position, request.Input.Teeth) };
        foreach (var site in request.Sites) foreach (int teeth in request.IdlerTeeth) Nodes.Add(new GearRouteAssignment(site, teeth));
        Nodes.Add(new GearRouteAssignment(request.Output.Position, request.Output.Teeth));
        Active = new bool[Nodes.Count]; Adjacency = Enumerable.Range(0, Nodes.Count).Select(_ => new List<int>()).ToArray();
    }
    internal AnchoredGearRoutingRequest Request { get; }
    internal List<GearRouteAssignment> Nodes { get; }
    internal bool[] Active { get; }
    internal List<int>[] Adjacency { get; }
    internal int Output => Nodes.Count - 1;
    internal bool EndpointsAvailable => Active[0] && Active[Output];
    internal static FiniteGearContactGraph? Prepare(AnchoredGearRoutingRequest r, CancellationToken token,
        Action<GearRouteAssignment, string?> nodeChecked, Action<bool> pairChecked)
    {
        var graph = new FiniteGearContactGraph(r);
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            if (token.IsCancellationRequested) return null;
            var issue = GearRouteValidator.StaticIssue(r, graph.Nodes[i]); graph.Active[i] = issue == null; nodeChecked(graph.Nodes[i], issue);
        }
        if (!graph.EndpointsAvailable) return graph;
        for (int i = 0; i < graph.Nodes.Count; i++) for (int j = i + 1; j < graph.Nodes.Count; j++)
        {
            if (token.IsCancellationRequested) return null;
            var a = graph.Nodes[i]; var b = graph.Nodes[j];
            bool mesh = graph.Active[i] && graph.Active[j] && GearRoutingGeometry.Meshes(a.Position, GearRouteValidator.Radius(r, a), b.Position, GearRouteValidator.Radius(r, b));
            pairChecked(mesh); if (mesh) { graph.Adjacency[i].Add(j); graph.Adjacency[j].Add(i); }
        }
        foreach (var neighbors in graph.Adjacency) neighbors.Sort();
        return graph;
    }
    internal string? SuccessorIssue(IReadOnlyList<int> path, int successor)
    {
        var next = Nodes[successor];
        if (path.Any(index => Nodes[index].Position.Equals(next.Position))) return "OCCUPIED_POSITION";
        if (successor != Output && path.Count - 1 >= Request.MaxIdlerCount) return "MAX_DEPTH";
        for (int i = 0; i < path.Count - 1; i++)
            if (!GearRoutingGeometry.UnrelatedClear(next.Position, GearRouteValidator.Radius(Request, next), Nodes[path[i]].Position,
                GearRouteValidator.Radius(Request, Nodes[path[i]]), Request.UnrelatedClearance)) return "UNINTENDED_CONTACT";
        return null;
    }
    /// <summary>Every successor, including rejects/goals, is charged before examining it. Returning false unwinds all lazy frames.</summary>
    internal bool VisitRoutes(int sign, Func<IReadOnlyList<int>, int, bool> charge, Action<string> reject,
        Func<IReadOnlyList<GearRouteAssignment>, bool> onRoute)
    {
        if (!EndpointsAvailable) return true;
        var path = new List<int> { 0 };
        bool Search()
        {
            foreach (var successor in Adjacency[path[path.Count - 1]])
            {
                if (!charge(path, successor)) return false;
                var issue = SuccessorIssue(path, successor);
                if (issue != null) { reject(issue); continue; }
                path.Add(successor); bool more = true;
                if (successor == Output)
                {
                    int k = path.Count - 2;
                    if (k < Request.MinIdlers || ((k + 1) % 2 == 0 ? 1 : -1) != sign) reject("GOAL_PARITY_OR_DEPTH");
                    else if (Request.RequiredRegions.Any(region => !path.Skip(1).Take(k).Any(i => GearRoutingGeometry.Contains(region.Bounds, Nodes[i].Position)))) reject("REQUIRED_REGION");
                    else more = onRoute(path.Select(i => Nodes[i]).ToArray());
                }
                else more = Search();
                path.RemoveAt(path.Count - 1); if (!more) return false;
            }
            return true;
        }
        return Search();
    }
}
