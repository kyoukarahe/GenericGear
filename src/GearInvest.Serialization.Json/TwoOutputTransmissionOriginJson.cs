using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using static GearInvest.Serialization.DiscreteEmbodimentJson;
using static GearInvest.Serialization.GearRoutingJson;
using static GearInvest.Serialization.CompoundRoutingJson;

namespace GearInvest.Serialization;

public static partial class TwoOutputTransmissionJson
{
    private static readonly CanonicalMechanismJson Mechanisms = new CanonicalMechanismJson();
    private static byte[] ObjectBytes(Action<Utf8JsonWriter> action)
    { using var stream = new MemoryStream(); using (var w = new Utf8JsonWriter(stream)) { action(w); w.Flush(); } return stream.ToArray(); }
    private static void Mappings(Utf8JsonWriter w, string name, IEnumerable<SharedDriverRoleMapping> mappings) => Array(w, name, mappings, (a, m) => {
        a.WriteStartObject(); a.WriteString("kind", m.Kind); a.WriteString("localId", m.LocalId); a.WriteString("mergedId", m.MergedId); a.WriteEndObject(); });
    private static SharedDriverRoleMapping[] Mappings(JsonElement p, string name) => A(p, name, 128).Select(m => new SharedDriverRoleMapping(S(m, "kind"), S(m, "localId"), S(m, "mergedId"))).ToArray();
    public static ArtifactWriteResult WriteMechanism(TwoOutputTransmissionCandidate c) => WriteMechanism(c.Origins[0]);
    public static ArtifactWriteResult WriteMechanism(TwoOutputCandidateOrigin o) => o.RootOnly != null ? SharedDriverTransmissionJson.WriteMechanism(o.RootOnly) : SharedPrefixTransmissionJson.WriteMechanism(o.SharedPrefix!);
    public static int MeasureOriginBytes(TwoOutputCandidateOrigin origin) => ObjectBytes(w => Origin(w, origin)).Length;
    private static void Origin(Utf8JsonWriter w, TwoOutputCandidateOrigin o)
    {
        w.WriteStartObject(); w.WriteString("strategy", o.Strategy.ToString()); w.WriteString("childGoalId", o.ChildGoalId); w.WriteString("candidateId", o.CandidateId);
        w.WriteString("originId", o.OriginId); w.WriteString("sourceContextId", o.SourceContextId);
        w.WritePropertyName("sourceMetrics"); TransmissionGoalJson.Metrics(w, o.RootOnly?.Metrics ?? o.SharedPrefix!.Metrics);
        if (o.SharedPrefix != null)
        {
            w.WriteString("hypothesisId", o.SharedPrefix.HypothesisId); w.WritePropertyName("splitterTransfer"); R(w, o.SharedPrefix.SplitterTransfer);
            w.WritePropertyName("prefixCandidate"); SharedDriverTransmissionJson.PoolCandidate(w, o.SharedPrefix.Prefix); Mappings(w, "prefixMappings", o.SharedPrefix.PrefixMappings);
        }
        Array(w, "outputs", o.Outputs, (a, b) => {
            a.WriteStartObject(); a.WriteString("outputKey", b.OutputKey); a.WriteString("childGoalId", b.ChildGoalId);
            a.WritePropertyName("childCandidate"); SharedDriverTransmissionJson.PoolCandidate(a, b.Child);
            a.WriteString("axisId", b.AxisId); a.WriteString("dofId", b.DofId); a.WriteString("bodyId", b.BodyId);
            a.WritePropertyName("requestedGlobalTransfer"); R(a, b.RequestedTransfer); a.WritePropertyName("actualGlobalTransfer"); R(a, b.ActualTransfer);
            Mappings(a, "roleMappings", b.Mappings); a.WriteEndObject();
        });
        Embed(w, "mechanism", WriteMechanism(o).Bytes); w.WriteEndObject();
    }
    private static TwoOutputCandidateOrigin Origin(JsonElement p, TwoOutputSharingSearchPlan plan)
    {
        var s = E<TwoOutputSharingStrategy>(p, "strategy"); var strategy = plan.Strategies.Single(x => x.Strategy == s);
        Check(strategy.Applicability == TwoOutputStrategyApplicability.Eligible, "origin-active-strategy");
        var bytes = Bytes(p.GetProperty("mechanism")); Check(bytes.Length <= 512 * 1024, "original-whole-byte-ceiling");
        var artifact = Mechanisms.Read(bytes); var identity = Mechanisms.VerifyIdentity(artifact);
        Check(identity.CandidateIdMatches && identity.ArtifactHashMatches && artifact.CandidateId == S(p, "candidateId"), "original-mechanical-identity");
        var h = s == TwoOutputSharingStrategy.SharedPrefix ? strategy.SharedPrefix!.Hypotheses.Single(x => x.HypothesisId == S(p, "hypothesisId")) : null;
        var outputs = A(p, "outputs", 2).Select(o => {
            string key = S(o, "outputKey"); var child = s == TwoOutputSharingStrategy.RootOnly ? strategy.RootOnly!.Branches.Single(x => x.OutputKey == key).Plan : h!.Suffix!.Branches.Single(x => x.OutputKey == key).Plan;
            return new SharedDriverOutputBinding(key, S(o, "childGoalId"), SharedDriverTransmissionJson.PoolCandidate(o.GetProperty("childCandidate"), child),
                S(o, "axisId"), S(o, "dofId"), S(o, "bodyId"), R(o.GetProperty("requestedGlobalTransfer")), R(o.GetProperty("actualGlobalTransfer")), Mappings(o, "roleMappings"));
        }).ToArray();
        var metrics = TransmissionGoalJson.Metrics(p.GetProperty("sourceMetrics"));
        return s == TwoOutputSharingStrategy.RootOnly
            ? new TwoOutputCandidateOrigin(S(p, "childGoalId"), rootOnly: new SharedDriverTransmissionCandidate(artifact.CandidateId, artifact.Candidate, outputs, metrics))
            : new TwoOutputCandidateOrigin(S(p, "childGoalId"), sharedPrefix: new SharedPrefixTransmissionCandidate(artifact.CandidateId, h!.HypothesisId,
                SharedDriverTransmissionJson.PoolCandidate(p.GetProperty("prefixCandidate"), h.Prefix!), artifact.Candidate,
                R(p.GetProperty("splitterTransfer")), Mappings(p, "prefixMappings"), outputs, metrics));
    }
}
