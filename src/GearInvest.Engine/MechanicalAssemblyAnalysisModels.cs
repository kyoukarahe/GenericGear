using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Engine.OrientedGoalKeys;

namespace GearInvest.Engine;

public sealed class AssemblyDiagnostic
{
    internal AssemblyDiagnostic(string code, string stage, string detail, IEnumerable<AssemblyComponentReference>? related = null,
        IEnumerable<AssemblyComponentReference>? dependencyPath = null, MechanicalDiagnostic? local = null)
    {
        Code = code; Stage = stage; Detail = detail; Related = (related ?? Array.Empty<AssemblyComponentReference>()).Distinct().OrderBy(r => r.CanonicalRepresentation, StringComparer.Ordinal).ToList().AsReadOnly();
        DependencyPath = (dependencyPath ?? Array.Empty<AssemblyComponentReference>()).ToList().AsReadOnly(); LocalDiagnostic = local;
    }
    public string Code { get; }
    public string Stage { get; }
    public string Detail { get; }
    public ReadOnlyCollection<AssemblyComponentReference> Related { get; }
    public ReadOnlyCollection<AssemblyComponentReference> DependencyPath { get; }
    public MechanicalDiagnostic? LocalDiagnostic { get; }
    public string CanonicalRepresentation => Pack(Code, Stage, Detail, Pack(Related.Select(r => r.CanonicalRepresentation).ToArray()),
        Pack(DependencyPath.Select(r => r.CanonicalRepresentation).ToArray()), LocalDiagnostic?.Code ?? "", LocalDiagnostic?.Stage ?? "", LocalDiagnostic?.Detail ?? "");
}

public sealed class AssemblyInventoryEntry
{
    internal AssemblyInventoryEntry(AssemblyComponentReference reference, string role, AssemblyComponentReference? mountedShaft = null,
        bool prescribed = false, bool nonlinearDependent = false)
    { Reference = reference; Role = role; MountedShaft = mountedShaft; IsPrescribed = prescribed; IsNonlinearDependent = nonlinearDependent; }
    public AssemblyComponentReference Reference { get; }
    public string Role { get; }
    public AssemblyComponentReference? MountedShaft { get; }
    public bool IsPrescribed { get; }
    public bool IsNonlinearDependent { get; }
    public string CanonicalRepresentation => Pack(Reference.CanonicalRepresentation, Role, MountedShaft?.CanonicalRepresentation ?? "", IsPrescribed ? "1" : "0", IsNonlinearDependent ? "1" : "0");
}

public sealed class AssemblyBindingAnalysis
{
    internal AssemblyBindingAnalysis(string instanceId, UpstreamShaftBinding? binding, bool structural, bool mounting,
        OrientedFrame? frame, ExactAffineRelation? inputRelation, IEnumerable<AssemblyComponentReference> path,
        IEnumerable<AssemblyDiagnostic> diagnostics, string dependencyIdentity, AssemblyGenevaAffineMotion? genevaInputMotion = null)
    { InstanceId = instanceId; Binding = binding; StructuralValidity = structural; MountingValidity = mounting; FixedInputFrameMm = frame;
        InputRelation = inputRelation; GenevaInputMotion = genevaInputMotion; DependencyPath = path.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly(); DependencyIdentity = dependencyIdentity; }
    public string InstanceId { get; }
    public UpstreamShaftBinding? Binding { get; }
    public bool StructuralValidity { get; }
    public bool MountingValidity { get; }
    public OrientedFrame? FixedInputFrameMm { get; }
    public ExactAffineRelation? InputRelation { get; }
    public AssemblyGenevaAffineMotion? GenevaInputMotion { get; }
    public bool HasDeterminedInput => StructuralValidity && MountingValidity && (InputRelation.HasValue || GenevaInputMotion is not null);
    public ReadOnlyCollection<AssemblyComponentReference> DependencyPath { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
    public string DependencyIdentity { get; }
}

public sealed class AssemblyMemberAnalysis
{
    internal AssemblyMemberAnalysis(MechanicalAssemblyMember member, AssemblyBindingAnalysis binding, BoundMechanicalInputContext? context,
        object? localCompatibility, AssemblyTerminalLocalResult? terminal, bool localAdmitted, ExactAffineRelation? shaftRelation,
        ExactAffineRelation? terminalRelation, Rational? localTransfer, MechanicalAxisVerdict target, bool scopesSatisfied,
        IEnumerable<OrientedDomainCheck> checks, IEnumerable<AssemblyDiagnostic> diagnostics, int numericWork,
        AssemblyGenevaAffineMotion? genevaShaftMotion = null, AssemblyGenevaAffineMotion? genevaTerminalMotion = null)
    {
        Member = member; Binding = binding; InputContext = context; LocalCompatibility = localCompatibility; TerminalLocal = terminal;
        LocalAdmitted = localAdmitted; ShaftRelation = shaftRelation; TerminalRelation = terminalRelation; LocalTransfer = localTransfer;
        Target = target; RequiredScopesSatisfied = scopesSatisfied; Checks = checks.ToList().AsReadOnly(); Diagnostics = diagnostics.ToList().AsReadOnly(); NumericWork = numericWork;
        GenevaShaftMotion = genevaShaftMotion; GenevaTerminalMotion = genevaTerminalMotion;
    }
    public MechanicalAssemblyMember Member { get; }
    public string InstanceId => Member.InstanceId;
    public AssemblyBindingAnalysis Binding { get; }
    public AssemblyOutputCapability LocalCapability => MechanicalAssemblyComponents.Capability(Member.Declaration);
    public AssemblyOutputCapability Capability => GenevaShaftMotion is not null ? AssemblyOutputCapability.IntermittentRotaryOutput : LocalCapability;
    public object? LocalCompatibility { get; }
    public WormDriveCompatibilityResult? WormCompatibility => LocalCompatibility as WormDriveCompatibilityResult;
    public OpenBeltCompatibilityResult? BeltCompatibility => LocalCompatibility as OpenBeltCompatibilityResult;
    public PitchChainCompatibilityResult? ChainCompatibility => LocalCompatibility as PitchChainCompatibilityResult;
    public GenevaCompatibilityResult? GenevaCompatibility => LocalCompatibility as GenevaCompatibilityResult;
    public CrankSliderCompatibilityResult? CrankCompatibility => LocalCompatibility as CrankSliderCompatibilityResult;
    public CamFollowerCompatibilityResult? CamCompatibility => LocalCompatibility as CamFollowerCompatibilityResult;
    public object? MotionDescriptor => TerminalLocal?.Descriptor;
    public bool LocalAdmitted { get; }
    public ExactAffineRelation? InputRelation => Binding.InputRelation;
    public ExactAffineRelation? ShaftRelation { get; }
    public ExactAffineRelation? TerminalRelation { get; }
    public Rational? LocalTransfer { get; }
    public AssemblyGenevaAffineMotion? GenevaShaftMotion { get; }
    public AssemblyGenevaAffineMotion? GenevaTerminalMotion { get; }
    public string GlobalMotionKind => ShaftRelation.HasValue ? "ExactAffineOfRoot" : GenevaShaftMotion?.MotionKind ??
        (TerminalLocal?.MotionDetermined == true ? Member.Declaration.Kind.ToString() : "Undetermined");
    public bool HasDeterminedMotion => Binding.HasDeterminedInput && LocalAdmitted && (ShaftRelation.HasValue || GenevaShaftMotion is not null || TerminalLocal?.MotionDetermined == true);
    public MechanicalAxisVerdict Target { get; }
    public bool RequiredScopesSatisfied { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
    public int NumericWork { get; }
    public string DependencyIdentity => Binding.DependencyIdentity;
    internal BoundMechanicalInputContext? InputContext { get; }
    internal AssemblyTerminalLocalResult? TerminalLocal { get; }
}

public sealed class AssemblyOutputAnalysis
{
    internal AssemblyOutputAnalysis(AssemblyOutputBinding binding, bool resolved, AssemblyOutputCapability? capability,
        ExactAffineRelation? relation, MechanicalAxisVerdict target, IEnumerable<AssemblyDiagnostic> diagnostics)
    { Binding = binding; IsResolved = resolved; Capability = capability; AffineRelation = relation; Target = target; Diagnostics = diagnostics.ToList().AsReadOnly(); }
    public AssemblyOutputBinding Binding { get; }
    public string Key => Binding.Key;
    public bool IsResolved { get; }
    public AssemblyOutputCapability? Capability { get; }
    public ExactAffineRelation? AffineRelation { get; }
    public MechanicalAxisVerdict Target { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
}

public sealed class MechanicalAssemblyAnalysis
{
    internal MechanicalAssemblyAnalysis(MechanicalAssemblyDraft draft, MechanicalAnalysis root, AssemblyNumericRequest numericRequest,
        IEnumerable<string> order, IEnumerable<AssemblyInventoryEntry> inventory, IEnumerable<AssemblyMemberAnalysis> members,
        IEnumerable<AssemblyOutputAnalysis> outputs, IEnumerable<OrientedDomainCheck> checks, IEnumerable<AssemblyDiagnostic> diagnostics,
        bool topologyValid, int numericWork)
    {
        Draft = draft; RootAnalysis = root; NumericRequest = numericRequest; TopologicalOrder = order.ToList().AsReadOnly();
        Inventory = inventory.OrderBy(x => x.Reference.CanonicalRepresentation, StringComparer.Ordinal).ToList().AsReadOnly();
        Members = members.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToList().AsReadOnly(); Outputs = outputs.OrderBy(x => x.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        Checks = checks.OrderBy(x => x.Domain, StringComparer.Ordinal).ThenBy(x => x.Subject, StringComparer.Ordinal).ToList().AsReadOnly();
        Diagnostics = diagnostics.OrderBy(x => x.Stage, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.CanonicalRepresentation, StringComparer.Ordinal).ToList().AsReadOnly();
        TopologyValid = topologyValid; NumericWork = numericWork; AnalysisId = HashText(Pack(draft.DraftId, Policy, numericRequest.CanonicalRepresentation));
    }
    public MechanicalAssemblyDraft Draft { get; }
    public string DefinitionId => Draft.DefinitionId;
    public string AnalysisId { get; }
    public string Policy => MechanicalAssemblyProfile.PolicyFor(Draft.Definition.Profile);
    public MechanicalAnalysis RootAnalysis { get; }
    public AssemblyNumericRequest NumericRequest { get; }
    public ReadOnlyCollection<string> TopologicalOrder { get; }
    public ReadOnlyCollection<AssemblyInventoryEntry> Inventory { get; }
    public ReadOnlyCollection<AssemblyMemberAnalysis> Members { get; }
    public ReadOnlyCollection<AssemblyOutputAnalysis> Outputs { get; }
    public ReadOnlyCollection<OrientedDomainCheck> Checks { get; }
    public ReadOnlyCollection<AssemblyDiagnostic> Diagnostics { get; }
    public bool TopologyValid { get; }
    public int NumericWork { get; }
    public int RootAnalysisCount => 1;
    public int LocalAnalysisCount => Members.Count(m => m.LocalCompatibility is not null);
    public bool IsMechanicallyValid => TopologyValid && RootAnalysis.IsMechanicallyValid && Members.All(m => m.HasDeterminedMotion && m.Target == MechanicalAxisVerdict.Pass && m.RequiredScopesSatisfied) &&
        Outputs.All(o => o.IsResolved && o.Target == MechanicalAxisVerdict.Pass) && Checks.All(c => !c.Required || c.Verdict == OrientedCheckVerdict.Pass);
    public MechanicalExportAdmission ExportAdmission => IsMechanicallyValid ? MechanicalExportAdmission.RequiresProfileValidation : MechanicalExportAdmission.BlockedByAnalysis;
}
