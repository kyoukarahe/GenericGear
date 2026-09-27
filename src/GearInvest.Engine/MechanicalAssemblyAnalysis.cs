using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.MechanicalAssemblyComponents;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

/// <summary>One actual root, bounded stable topological traversal and engine-minted shaft contexts. No surrogate sources.</summary>
public static partial class MechanicalAssemblyAnalyzer
{
    public static MechanicalAssemblyAnalysis Analyze(MechanicalAssemblyDraft draft, AssemblyNumericRequest? numericRequest = null)
        => AnalyzeWithBudget(draft, new AssemblyNumericBudget(numericRequest));

    internal static MechanicalAssemblyAnalysis AnalyzeWithBudget(MechanicalAssemblyDraft draft, AssemblyNumericBudget budget)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var d = draft.Definition; var request = budget.Request; var initialWork = budget.UsedWork;
        var suffixProfile = d.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId;
        budget.ShareGenevaSamples = suffixProfile;
        var topology = AssemblyTopologyAnalyzer.Analyze(d);
        var diagnostics = new List<AssemblyDiagnostic>(topology.Diagnostics);
        var checks = new List<OrientedDomainCheck>();
        var members = topology.Members;
        var problems = topology.Problems;
        var inventory = topology.Inventory;
        var rootInput = topology.RootInput;
        var oneRoot = topology.OneRoot;
        var bounded = topology.Bounded;
        var order = topology.Order;
        var topologyValid = topology.IsValid;
        var root = MechanicalAnalyzer.Analyze(d.Root); // exactly once, after topology validation
        foreach (var issue in root.Diagnostics) diagnostics.Add(new(issue.Code, "Root/" + issue.Stage, issue.Detail,
            issue.Related.Select(r => RootReference(r)), local: issue));
        var mapping = d.RootMapping; var mappingValid = mapping is not null && mapping.MillimetersPerSourceUnit > 0 && mapping.PoseMm.IsProperCardinal;
        var rootLaws = RootLaws(root); var analyzed = new Dictionary<string, AssemblyMemberAnalysis>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            var m = members[id]; var b = m.InputBinding; var localIssues = new List<AssemblyDiagnostic>(problems[id]);
            var path = new List<AssemblyComponentReference>(); OrientedShaft? shaft = null; ShaftPort? port = null; ExactAffineRelation? inputLaw = null;
            AssemblyGenevaAffineMotion? inputGeneva = null;
            var parentIdentity = root.AnalysisId;
            if (b is not null && b.UpstreamShaft.Owner == AssemblyOwnerKind.Root)
            {
                var original = d.Root.Definition.Shafts.FirstOrDefault(s => s.Id == b.UpstreamShaft.LocalId);
                if (mappingValid && original is not null)
                {
                    try { shaft = new(original.Id, mapping!.FrameMm(original.Frame), original.IsPrescribed);
                        var originalPort = d.Root.Definition.Ports.FirstOrDefault(p => p.Id == b.MountingPortId);
                        if (originalPort is not null) port = new(originalPort.Id, originalPort.ShaftId, mapping.FrameMm(originalPort.Frame), originalPort.PhaseOffset, originalPort.Kind); }
                    catch (ArgumentException e) { localIssues.Add(new("NumericResourceLimit", "BindingFrame", e.Message, new[] { b.UpstreamShaft })); }
                }
                if (oneRoot && rootLaws.TryGetValue(b.UpstreamShaft.LocalId, out var law)) inputLaw = law;
                if (rootInput is not null) path.AddRange(MechanicalAnalyzer.Path(rootInput, b.UpstreamShaft.LocalId, root.Edges.Where(e => e.IsDeclaredResolvable))
                    .Select(key => RootReference(new MechanicalReference("Constraint", root.Edges.Single(e => e.ConstraintKey == key).Id))));
                path.Add(b.UpstreamShaft);
            }
            else if (b is not null && b.UpstreamShaft.MemberId is not null && members.TryGetValue(b.UpstreamShaft.MemberId, out var upstreamMember))
            {
                shaft = MechanicalAssemblyComponents.Shaft(upstreamMember.Declaration); port = b.MountingPortId is null ? null : MechanicalAssemblyComponents.Port(upstreamMember.Declaration);
                if (analyzed.TryGetValue(upstreamMember.InstanceId, out var parent))
                { parentIdentity = parent.DependencyIdentity; inputLaw = parent.ShaftRelation; inputGeneva = suffixProfile ? parent.GenevaShaftMotion : null; path.AddRange(parent.Binding.DependencyPath); }
                path.Add(b.UpstreamShaft);
            }
            path.Add(MemberConstraint(m));
            var structural = oneRoot && bounded && problems[id].Count == 0;
            var mounting = structural && shaft?.Frame.IsProperCardinal == true && b is not null && MechanicalAssemblyComponents.StationMatches(m.Declaration, shaft.Frame, b.MountingStation);
            if (structural && !mounting) localIssues.Add(new("InvalidAttachmentMounting", "Binding", "The actual fixed mm shaft frame and explicit signed station do not reconstruct the declared attachment.",
                new[] { MemberConstraint(m) }.Concat(b is null ? Array.Empty<AssemblyComponentReference>() : new[] { b.UpstreamShaft }), path));
            if (!structural || !mounting) { inputLaw = null; inputGeneva = null; }
            var depId = HashText(Pack(d.Root.DraftId, mapping?.CanonicalRepresentation ?? "", parentIdentity, m.CanonicalRepresentation));
            BoundMechanicalInputContext? context = b is null ? null : BoundMechanicalInputContext.FromAssembly(d.DefinitionId, depId, rootInput, m.InstanceId,
                b.UpstreamShaft, shaft, port, inputLaw, structural && mounting, path.Select(p => p.CanonicalRepresentation), b.MountingStation, inputGeneva);
            var binding = new AssemblyBindingAnalysis(id, b, structural, mounting, shaft?.Frame, inputLaw, path, localIssues, depId, inputGeneva);
            object? local = null; AssemblyTerminalLocalResult? terminal = null; bool admitted = false; ExactAffineRelation? outputLaw = null, terminalLaw = null;
            AssemblyGenevaAffineMotion? outputGeneva = null, terminalGeneva = null;
            Rational? transfer = null; var target = MechanicalAxisVerdict.NotAssessed; var localChecks = new List<OrientedDomainCheck>(); var startWork = budget.UsedWork;
            if (context is not null && structural && mounting)
            {
                try
                {
                    IEnumerable<MechanicalDiagnostic> issues = Array.Empty<MechanicalDiagnostic>();
                    switch (m.Declaration)
                    {
                        case AssemblyWormDeclaration w:
                        { var result = WormDriveConnectionQuery.QueryLocal(w.Device, w.OutputTerminal, w.Output, context); local = result; admitted = result.IsAdmitted; transfer = result.AdmittedTransfer; localChecks.AddRange(result.Checks); issues = result.Diagnostics; break; }
                        case AssemblyOpenBeltDeclaration belt:
                        { var result = OpenBeltConnectionQuery.QueryLocal(belt.Device, belt.OutputTerminal, belt.Output, context); local = result; admitted = result.IsAdmitted; transfer = result.AdmittedTransfer; localChecks.AddRange(result.Checks); issues = result.Diagnostics; break; }
                        case AssemblyPitchChainDeclaration chain:
                        { var result = PitchChainConnectionQuery.QueryLocal(chain.Device, chain.OutputTerminal, chain.Output, context); local = result; admitted = result.IsAdmitted; transfer = result.AdmittedTransfer; localChecks.AddRange(result.Checks); issues = result.Diagnostics; break; }
                        default:
                            terminal = AssemblyTerminalMechanics.Analyze(m.Declaration, context, budget); local = terminal.LocalCompatibility;
                            admitted = terminal.LocalAdmitted; target = terminal.Target; localChecks.AddRange(terminal.Checks); issues = terminal.Diagnostics; break;
                    }
                    localIssues.AddRange(issues.Select(issue => LocalIssue(m, path, issue)));
                    if (suffixProfile && terminal?.MotionDetermined == true && terminal.Geneva is not null)
                    {
                        outputGeneva = new(AssemblyComponentReference.Member(id, AssemblyComponentKind.Shaft,
                            terminal.Geneva.Device.OutputShaft.Id), terminal.Geneva, new ExactAffineRelation(1, 0));
                        terminalGeneva = outputGeneva.Then(terminal.Geneva.Output.TerminalSign, terminal.Geneva.Output.TerminalDatum.Value);
                    }
                    if (transfer.HasValue && admitted && inputGeneva is not null)
                    {
                        var r = MechanicalAssemblyComponents.References(m.Declaration);
                        var offset = r.Output - transfer.Value * r.Input;
                        outputGeneva = inputGeneva.Then(transfer.Value, offset);
                        var portOut = MechanicalAssemblyComponents.Port(m.Declaration)!;
                        var shaftOut = MechanicalAssemblyComponents.Shaft(m.Declaration)!;
                        var sign = shaftOut.Frame.Z.Dot(portOut.Frame.Z);
                        terminalGeneva = outputGeneva.Then(sign, portOut.PhaseOffset);
                        var req = MechanicalAssemblyComponents.AffineOutput(m.Declaration)!;
                        var phase = MechanicalAssemblyComponents.RequiredPhase(m.Declaration);
                        if (m.Declaration.TargetBasis == AssemblyTargetBasis.MemberInput)
                        {
                            var localLaw = new ExactAffineRelation(transfer.Value, offset).Then(sign, portOut.PhaseOffset);
                            target = (!req.RequiredTransfer.HasValue || req.RequiredTransfer.Value == localLaw.Coefficient) &&
                                (!phase.HasValue || phase.Value == localLaw.Phase) ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
                        }
                        else if (req.RequiredTransfer.HasValue)
                            target = MechanicalAxisVerdict.Fail; // A nonconstant intermittent function cannot equal a whole-cycle affine law.
                        else target = phase.HasValue ? AssemblyOutputAssessment.AssessGenevaReference(terminalGeneva, ExactQuantity.Turns(0), phase.Value, budget) : MechanicalAxisVerdict.Pass;
                        if (target != MechanicalAxisVerdict.Pass) localIssues.Add(new(target == MechanicalAxisVerdict.Fail ? "TargetMismatch" : "IncompleteNumericBudget",
                            "MemberRequirements", "Requirements do not supply motion. Local transfer and non-affine global-root function are separate.", new[] { MemberOutput(m) }, path));
                    }
                    if (transfer.HasValue && admitted && inputLaw.HasValue)
                    {
                        var r = MechanicalAssemblyComponents.References(m.Declaration); var offset = r.Output - transfer.Value * r.Input;
                        MechanicalDerivedNumbers.Check(offset); outputLaw = inputLaw.Value.Then(transfer.Value, offset);
                        MechanicalDerivedNumbers.Check(outputLaw.Value.Coefficient); MechanicalDerivedNumbers.Check(outputLaw.Value.Phase);
                        var terminalPort = MechanicalAssemblyComponents.Port(m.Declaration)!; var outputShaft = MechanicalAssemblyComponents.Shaft(m.Declaration)!;
                        var sign = outputShaft.Frame.Z.Dot(terminalPort.Frame.Z); terminalLaw = outputLaw.Value.Then(sign, terminalPort.PhaseOffset);
                        var requirementLaw = m.Declaration.TargetBasis == AssemblyTargetBasis.GlobalRoot ? terminalLaw.Value : new ExactAffineRelation(transfer.Value, offset).Then(sign, terminalPort.PhaseOffset);
                        var req = MechanicalAssemblyComponents.AffineOutput(m.Declaration)!; var phase = MechanicalAssemblyComponents.RequiredPhase(m.Declaration);
                        target = (!req.RequiredTransfer.HasValue || req.RequiredTransfer.Value == requirementLaw.Coefficient) && (!phase.HasValue || phase.Value == requirementLaw.Phase)
                            ? MechanicalAxisVerdict.Pass : MechanicalAxisVerdict.Fail;
                        if (target == MechanicalAxisVerdict.Fail) localIssues.Add(new("TargetMismatch", "MemberRequirements", "Current physical motion remains admitted; independent output ratio/reference requirement differs.", new[] { MemberOutput(m) }, path));
                    }
                }
                catch (ArgumentException e)
                { outputLaw = terminalLaw = null; outputGeneva = terminalGeneva = null; localIssues.Add(new("NumericResourceLimit", "LocalAnalysis", e.Message, new[] { MemberConstraint(m) }, path)); }
            }
            if (!inputLaw.HasValue && inputGeneva is null) localIssues.Add(new("UndeterminedInput", "DependencyDeterminacy", "No current admitted absolute input law reaches this member. No zero/reference/cached input was substituted.", new[] { MemberConstraint(m) }, path));
            if (MechanicalAssemblyComponents.Capability(m.Declaration) == AssemblyOutputCapability.AffineRotaryShaft)
            {
                localChecks.Add(new("ExactAffinePrefix", id, outputLaw.HasValue || outputGeneva is not null ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, "Exact actual local transfer composed without modulo, terminal gain or a new prescribed input."));
                localChecks.Add(new("OutputRequirements", id, Verdict(target), true, "Explicit requirement basis is preserved."));
                foreach (var domain in AffineUnperformed(m.Declaration.Kind).Concat(m.Declaration.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
                {
                    if (localChecks.Any(c => c.Domain == domain)) continue;
                    var required = m.Declaration.RequiredValidationDomains.Contains(domain);
                    localChecks.Add(new(domain, id, OrientedCheckVerdict.NotPerformed, required, "Not performed by the inherited bounded device profile."));
                    if (required) localIssues.Add(new("RequiredValidationNotPerformed", "MemberValidationScope", domain, new[] { MemberConstraint(m) }, path));
                }
            }
            var scopes = localChecks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
            analyzed.Add(id, new(m, binding, context, local, terminal, admitted, outputLaw, terminalLaw, transfer, target, scopes, localChecks, localIssues, budget.UsedWork - startWork, outputGeneva, terminalGeneva));
            diagnostics.AddRange(localIssues.Except(problems[id]));
        }
        var outputs = d.Outputs.Select(o => AssemblyOutputAssessment.Analyze(o, d, root, rootLaws, analyzed, budget)).ToArray();
        diagnostics.AddRange(outputs.SelectMany(o => o.Diagnostics));
        void Check(string domain, bool pass, string detail) => checks.Add(new(domain, "assembly", pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
        Check("SingleRootOwnership", oneRoot && inventory.Count(i => i.IsPrescribed) == 1, "One live root and one prescribed coordinate; instances are not deduplicated by specification hash.");
        Check("AcyclicSupportedTopology", topologyValid, "Complete bounded topology validated before local geometry.");
        Check("ActualShaftBinding", analyzed.Values.All(m => m.Binding.StructuralValidity && m.Binding.MountingValidity), "Physical inputs bind actual owned shafts, not calibrated output values.");
        Check("Unit/FrameComposition", mappingValid && analyzed.Values.All(m => m.Binding.MountingValidity), "Root length mapping once; fixed reference frames plus unwrapped angles thereafter.");
        Check("DependencyDeterminacy", analyzed.Values.All(m => m.HasDeterminedMotion), "A failed parent blocks descendants, without erasing independent branches.");
        Check("ExactAffinePrefix", analyzed.Values.Where(m => m.Capability == AssemblyOutputCapability.AffineRotaryShaft).All(m => m.ShaftRelation.HasValue || m.GenevaShaftMotion is not null), "Current admitted rational propagation.");
        Check("MemberProfileValidation", analyzed.Values.All(m => m.LocalAdmitted && m.RequiredScopesSatisfied), "Inherited bounded mechanics and member scopes.");
        Check("AssemblyOutputRequirements", outputs.All(o => o.IsResolved && o.Target == MechanicalAxisVerdict.Pass) && analyzed.Values.All(m => m.Target == MechanicalAxisVerdict.Pass), "All independent member and assembly requirements are retained.");
        Check("AggregateNumericPolicy", request.IsValid && budget.UsedWork <= request.MaximumWork, "Actual numerical work is shared across the request; exact algebra is not converted into numerical work.");
        checks.Add(new("Source/MemberReconstruction", "assembly", OrientedCheckVerdict.NotPerformed, false, "Source-aware fresh finalization/reimport is a separate facade boundary, not asserted by analysis."));
        foreach (var domain in new[] { "CrossMemberFullSolidClearance", "WholeAssemblySweptClearance", "Force/Torque/Inertia/Dynamics", "PhysicalContactRetention", "Manufacturing/Strength/Wear" }.Concat(d.RequiredValidationDomains).Distinct(StringComparer.Ordinal))
        {
            var existing = checks.FindIndex(c => c.Domain == domain);
            if (existing >= 0)
            {
                if (d.RequiredValidationDomains.Contains(domain) && !checks[existing].Required)
                { var c = checks[existing]; checks[existing] = new(c.Domain, c.Subject, c.Verdict, true, c.Detail); }
                continue;
            }
            var required = d.RequiredValidationDomains.Contains(domain);
            checks.Add(new(domain, "assembly", OrientedCheckVerdict.NotPerformed, required, "No whole-assembly solids, swept contact, forces or manufacturing proof is implied."));
            if (required) diagnostics.Add(new("RequiredValidationNotPerformed", "AssemblyValidationScope", domain));
        }
        return new(draft, root, request, order, inventory, analyzed.Values, outputs, checks, diagnostics, topologyValid, budget.UsedWork - initialWork);
    }

    internal static Dictionary<string, ExactAffineRelation> RootLaws(MechanicalAnalysis root) => root.AdmittedComponents
        .Where(c => c.Affine?.Determinacy == MechanicalDeterminacy.DeterminedBySelectedInput).SelectMany(c => c.Affine!.Relations)
        .ToDictionary(r => r.DofId, r => r.Relation, StringComparer.Ordinal);
    internal static AssemblyDiagnostic LocalIssue(MechanicalAssemblyMember m, IEnumerable<AssemblyComponentReference> path, MechanicalDiagnostic issue) =>
        new(issue.Code, "Member/" + issue.Stage, issue.Detail, new[] { MemberConstraint(m), MemberOutput(m) }, path, issue);
    internal static OrientedCheckVerdict Verdict(MechanicalAxisVerdict verdict) => verdict == MechanicalAxisVerdict.Pass ? OrientedCheckVerdict.Pass : verdict == MechanicalAxisVerdict.Fail ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
    private static IEnumerable<string> AffineUnperformed(AssemblyDeviceKind kind) => kind switch
    {
        AssemblyDeviceKind.WormDrive => new[] { "ConjugateFlankGeneration", "ActualToothContact/PressureAngle/ContactRatio", "FullAddedBody/SweptClearance", "Backlash/Elasticity", "SelfLocking/BackdriveResistance", "Friction/Torque/Efficiency/Thermal", "Force/Inertia/Dynamics", "Bearing/Strength/Wear/Manufacturing" },
        AssemblyDeviceKind.OpenBelt => new[] { "PhysicalTraction/Tension", "Slip/Creep/Stretch", "BeltWidth/Thickness/SolidSelfContact", "AddedBody/SweptClearance", "Bearing/Fatigue/Strength", "Dynamics/Efficiency/Manufacturing" },
        _ => new[] { "ActualToothPocket/RollerContact", "LinkPlate/SolidInterference", "FullAddedBody/SweptClearance", "Slack/Tension/Traction", "Backlash/Elasticity/Friction", "Force/Inertia/Impact/Dynamics", "Wear/Lubrication/Strength/Manufacturing" }
    };
}
