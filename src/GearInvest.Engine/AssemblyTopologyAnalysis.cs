using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;

namespace GearInvest.Engine;

/// <summary>Declared ownership and dependency validation, before root or local mechanics are evaluated.</summary>
internal static class AssemblyTopologyAnalyzer
{
    internal static AssemblyTopologyAnalysis Analyze(MechanicalAssemblyDefinition d)
    {
        var suffixProfile = d.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId;
        var diagnostics = new List<AssemblyDiagnostic>();
        var members = d.Members.ToDictionary(m => m.InstanceId, StringComparer.Ordinal);
        var problems = d.Members.ToDictionary(m => m.InstanceId, _ => new List<AssemblyDiagnostic>(), StringComparer.Ordinal);
        var inventory = MechanicalAssemblyComponents.RootInventory(d.Root).Concat(d.Members.SelectMany(MechanicalAssemblyComponents.MemberInventory)).ToArray();
        var rootInput = d.Root.Definition.RootShaftId;
        var oneRoot = rootInput is not null && d.Root.Definition.Shafts.Count(s => s.IsPrescribed) == 1 && d.Root.Definition.Shafts.Any(s => s.Id == rootInput && s.IsPrescribed);
        var bounded = MechanicalAssemblyProfile.IsSupported(d.Profile) && d.Members.Count <= MechanicalAssemblyProfile.MaxMembers && d.Outputs.Count <= MechanicalAssemblyProfile.MaxOutputs &&
            d.Members.Count(m => MechanicalAssemblyComponents.Capability(m.Declaration) != AssemblyOutputCapability.AffineRotaryShaft) <= MechanicalAssemblyProfile.MaxTerminals && inventory.Length <= MechanicalAssemblyProfile.MaxInventoryEntries;
        void Global(string code, string detail, IEnumerable<AssemblyComponentReference>? refs = null) => diagnostics.Add(new(code, "Topology", detail, refs));
        void Problem(MechanicalAssemblyMember m, string code, string detail, IEnumerable<AssemblyComponentReference>? path = null)
        {
            var refs = new[] { MechanicalAssemblyComponents.MemberConstraint(m) }.Concat(m.InputBinding is null ? Array.Empty<AssemblyComponentReference>() : new[] { m.InputBinding.UpstreamShaft });
            var issue = new AssemblyDiagnostic(code, "Topology", detail, refs, path);
            problems[m.InstanceId].Add(issue);
            diagnostics.Add(issue);
        }
        if (!oneRoot) Global("SingleRootOwnershipViolation", "Exactly one actual prescribed root shaft is required; declared references cannot introduce another input.");
        if (!bounded) Global("UnsupportedAssemblyProfileOrResourceLimit", "The complete declared topology or inventory exceeds this profile; no member is omitted to obtain approval.");
        foreach (var duplicate in inventory.GroupBy(x => x.Reference.CanonicalRepresentation, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var reference = duplicate.First().Reference;
            if (reference.Owner == AssemblyOwnerKind.Member) Problem(members[reference.MemberId!], "DuplicateOwnerLocalId", "An owner cannot declare two components with one typed local identity.", new[] { reference });
            else Global("DuplicateOwnerLocalId", "Duplicate root typed component identity.", new[] { reference });
        }
        // Validate every declared edge before running device geometry or following any dependency.
        foreach (var m in d.Members)
        {
            var b = m.InputBinding;
            if (b is null)
            {
                Problem(m, "DisconnectedInputBinding", "The member and its IDs are retained, but no input shaft is bound.");
                continue;
            }
            if (b.UpstreamShaft.Kind != AssemblyComponentKind.Shaft)
                Problem(m, "UnsupportedOutputCapability", suffixProfile ? "A binding consumes an actual admitted rotary shaft, never a terminal reading, prismatic coordinate or feature." : "A binding consumes an actual affine rotary shaft, not a terminal reading, prismatic coordinate or feature.");
            if (b.UpstreamShaft.LocalId != m.Declaration.InputShaftId || b.MountingPortId != m.Declaration.InputPortId)
                Problem(m, "InputDeclarationBindingMismatch", "The local shaft/port declaration must name exactly the structured binding endpoint.");
            if (b.MountingStation.Kind != QuantityKind.LinearPosition)
                Problem(m, "DimensionMismatch", "An explicit signed mounting station in canonical mm is required.");
            var ownShaft = MechanicalAssemblyComponents.Shaft(m.Declaration);
            if (ownShaft?.IsPrescribed == true || m.Declaration is AssemblyCrankSliderDeclaration { Device.SliderIsPrescribed: true } || m.Declaration is AssemblyCamFollowerDeclaration { Device.FollowerIsPrescribed: true })
                Problem(m, "HiddenPrescribedOutput", "An added shaft or slider cannot introduce another prescribed driver.");
            if (b.UpstreamShaft.Owner == AssemblyOwnerKind.Root)
            {
                if (!d.Root.Definition.Shafts.Any(s => s.Id == b.UpstreamShaft.LocalId)) Problem(m, "DanglingShaftReference", "The actual root shaft does not exist.");
                if (b.MountingPortId is not null && !d.Root.Definition.Ports.Any(p => p.Id == b.MountingPortId && p.ShaftId == b.UpstreamShaft.LocalId))
                    Problem(m, "DanglingPortReference", "The mounting port does not belong to the named actual root shaft.");
            }
            else if (b.UpstreamShaft.MemberId == m.InstanceId) Problem(m, "SelfBinding", "A member cannot be its own input source.", new[] { MechanicalAssemblyComponents.MemberConstraint(m), b.UpstreamShaft });
            else if (!members.TryGetValue(b.UpstreamShaft.MemberId!, out var parent)) Problem(m, "DanglingOwnerReference", "The upstream member owner does not exist.");
            else
            {
                if (MechanicalAssemblyComponents.Capability(parent.Declaration) != AssemblyOutputCapability.AffineRotaryShaft &&
                    !(suffixProfile && parent.Declaration is AssemblyGenevaDeclaration && m.Declaration.Kind is AssemblyDeviceKind.WormDrive or AssemblyDeviceKind.OpenBelt))
                    Problem(m, "UnsupportedOutputCapability", "Nonlinear and intermittent outputs are terminal-only in this profile.");
                if (MechanicalAssemblyComponents.Shaft(parent.Declaration)?.Id != b.UpstreamShaft.LocalId)
                    Problem(m, "DanglingShaftReference", "The local ID is not a produced shaft of the explicitly selected owner.");
                if (b.MountingPortId is not null && MechanicalAssemblyComponents.Port(parent.Declaration)?.Id != b.MountingPortId)
                    Problem(m, "DanglingPortReference", "The mounting port does not belong to the selected upstream owner.");
            }
        }
        var pending = new SortedSet<string>(members.Keys, StringComparer.Ordinal);
        var order = new List<string>();
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var affineDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        var genevaOnPath = new Dictionary<string, bool>(StringComparer.Ordinal);
        var suffixDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var ready = pending.Where(id => members[id].InputBinding?.UpstreamShaft.Owner != AssemblyOwnerKind.Member ||
                !pending.Contains(members[id].InputBinding!.UpstreamShaft.MemberId!)).ToArray();
            if (ready.Length == 0) break;
            foreach (var id in ready)
            {
                var m = members[id];
                var parentId = m.InputBinding?.UpstreamShaft.MemberId;
                var currentDepth = 1 + (parentId is not null && depth.TryGetValue(parentId, out var pd) ? pd : 0);
                var currentAffineDepth = (MechanicalAssemblyComponents.Capability(m.Declaration) == AssemblyOutputCapability.AffineRotaryShaft ? 1 : 0) +
                    (parentId is not null && affineDepth.TryGetValue(parentId, out var ad) ? ad : 0);
                depth[id] = currentDepth;
                affineDepth[id] = currentAffineDepth;
                var afterGeneva = parentId is not null && genevaOnPath.TryGetValue(parentId, out var seen) && seen;
                genevaOnPath[id] = afterGeneva || m.Declaration is AssemblyGenevaDeclaration;
                suffixDepth[id] = afterGeneva ? 1 + (parentId is not null && suffixDepth.TryGetValue(parentId, out var sd) ? sd : 0) : 0;
                if (suffixProfile && afterGeneva && m.Declaration.Kind is not (AssemblyDeviceKind.WormDrive or AssemblyDeviceKind.OpenBelt))
                    Problem(m, "UnsupportedPostGenevaDevice", "Only local worm/open-belt affine devices may consume a current Geneva-dependent shaft; no second nonlinear device or pitch chain.");
                if (suffixProfile && suffixDepth[id] > MechanicalAssemblyProfile.MaxGenevaSuffixDepth)
                    Problem(m, "GenevaSuffixDepthLimit", "At most two local affine members may follow the one Geneva on a path.");
                if (currentDepth > MechanicalAssemblyProfile.MaxDepth || currentAffineDepth > MechanicalAssemblyProfile.MaxAffineDepth)
                    Problem(m, "AssemblyDepthLimit", "The complete path exceeds the supported member/affine prefix depth.");
                order.Add(id);
                pending.Remove(id);
            }
        }
        foreach (var id in pending)
        {
            var path = new List<AssemblyComponentReference>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var at = id;
            while (members.TryGetValue(at, out var m) && visited.Add(at))
            {
                path.Add(MechanicalAssemblyComponents.MemberConstraint(m));
                var up = m.InputBinding?.UpstreamShaft;
                if (up is null || up.Owner != AssemblyOwnerKind.Member) break;
                path.Add(up);
                at = up.MemberId!;
            }
            if (members.TryGetValue(at, out var repeated)) path.Add(MechanicalAssemblyComponents.MemberConstraint(repeated));
            Problem(members[id], "CyclicDependency", "The declared dependency reaches a cycle; bounded traversal stopped before device evaluation.", path);
        }
        order.AddRange(pending); // Failed nodes stay represented, never silently removed from the aggregate.
        if (suffixProfile)
            inventory = inventory.Select(entry => entry.Reference.Owner == AssemblyOwnerKind.Member &&
                entry.Reference.Kind != AssemblyComponentKind.Constraint &&
                genevaOnPath.TryGetValue(entry.Reference.MemberId!, out var downstream) && downstream &&
                members[entry.Reference.MemberId!].Declaration is not AssemblyGenevaDeclaration
                ? new AssemblyInventoryEntry(entry.Reference, entry.Role, entry.MountedShaft, entry.IsPrescribed, true) : entry).ToArray();
        return new(members, problems, inventory, order, diagnostics, rootInput, oneRoot, bounded);
    }
}

/// <summary>A completed topology pass. Diagnostic instances are shared with each member's problem list.</summary>
internal sealed class AssemblyTopologyAnalysis
{
    internal AssemblyTopologyAnalysis(
        Dictionary<string, MechanicalAssemblyMember> members,
        Dictionary<string, List<AssemblyDiagnostic>> problems,
        AssemblyInventoryEntry[] inventory,
        List<string> order,
        List<AssemblyDiagnostic> diagnostics,
        string? rootInput,
        bool oneRoot,
        bool bounded)
    {
        Members = new ReadOnlyDictionary<string, MechanicalAssemblyMember>(members);
        Problems = new ReadOnlyDictionary<string, ReadOnlyCollection<AssemblyDiagnostic>>(
            problems.ToDictionary(pair => pair.Key, pair => pair.Value.AsReadOnly(), StringComparer.Ordinal));
        Inventory = Array.AsReadOnly(inventory);
        Order = order.AsReadOnly();
        Diagnostics = diagnostics.AsReadOnly();
        RootInput = rootInput;
        OneRoot = oneRoot;
        Bounded = bounded;
        IsValid = diagnostics.Count == 0;
    }

    internal IReadOnlyDictionary<string, MechanicalAssemblyMember> Members { get; }
    internal IReadOnlyDictionary<string, ReadOnlyCollection<AssemblyDiagnostic>> Problems { get; }
    internal ReadOnlyCollection<AssemblyInventoryEntry> Inventory { get; }
    internal ReadOnlyCollection<string> Order { get; }
    internal ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
    internal string? RootInput { get; }
    internal bool OneRoot { get; }
    internal bool Bounded { get; }
    internal bool IsValid { get; }
}
