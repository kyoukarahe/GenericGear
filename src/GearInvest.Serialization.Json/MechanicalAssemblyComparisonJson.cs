using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class MechanicalAssemblyJson
{
    public static byte[] WriteComparisonRequest(MechanicalAssemblyOutputComparisonRequest request) => Guard(() => Encode(w => ComparisonRequest(w, request)));
    public static MechanicalAssemblyOutputComparisonRequest ReadComparisonRequest(byte[] bytes) => Guard(() =>
    {
        using var document = Parse(bytes); var request = ComparisonRequest(document.RootElement);
        Require(bytes.SequenceEqual(WriteComparisonRequest(request)), "Noncanonical or unknown assembly comparison request fields."); return request;
    });
    public static byte[] WriteComparison(MechanicalAssemblyOutputComparisonResult result) => Guard(() => Encode(w => Comparison(w, result)));
    public static MechanicalAssemblyOutputComparisonResult VerifyComparison(MechanicalAssemblyAnalysis before, MechanicalAssemblyAnalysis after,
        MechanicalAssemblyOutputComparisonRequest request, byte[] bytes) => Guard(() =>
    {
        var result = MechanicalAssemblyAnalyzer.CompareOutputs(before, after, request);
        Require(bytes.SequenceEqual(WriteComparison(result)), "Fresh whole-function assembly comparison differs from stored evidence."); return result;
    });
    internal static void ComparisonRequest(Utf8JsonWriter w, MechanicalAssemblyOutputComparisonRequest request)
    {
        Start(w, "gear-invest.mechanical-assembly-output-comparison-request"); w.WriteString("requestId", request.RequestId);
        w.WritePropertyName("beforeOutput"); Reference(w, request.BeforeOutput); w.WritePropertyName("afterOutput"); Reference(w, request.AfterOutput);
        w.WriteString("mode", request.Mode.ToString()); R.Quantity(w, "inputAlpha", request.InputAlpha); R.Quantity(w, "inputBeta", request.InputBeta);
        Integer(w, "outputSign", request.OutputSign); R.Quantity(w, "outputDatum", request.OutputDatum);
        w.WritePropertyName("numericRequest"); NumericRequest(w, request.NumericRequest);
        if (request.AfterWorldToBeforeWorldMm is null) w.WriteNull("afterWorldToBeforeWorldMm"); else Frame(w, "afterWorldToBeforeWorldMm", request.AfterWorldToBeforeWorldMm);
        Array(w, "witnessRoots", request.WitnessRoots, (writer, root) => R.Quantity(writer, root));
        w.WriteString("beforeInputId", request.BeforeInputId); w.WriteString("afterInputId", request.AfterInputId); w.WriteEndObject();
    }
    internal static MechanicalAssemblyOutputComparisonRequest ComparisonRequest(JsonElement p)
    {
        Header(p, "gear-invest.mechanical-assembly-output-comparison-request");
        var request = new MechanicalAssemblyOutputComparisonRequest(Reference(p.GetProperty("beforeOutput")), Reference(p.GetProperty("afterOutput")),
            E<AssemblyOutputComparisonMode>(p, "mode"), R.Quantity(p.GetProperty("inputAlpha")), R.Quantity(p.GetProperty("inputBeta")), I(p, "outputSign"),
            R.Quantity(p.GetProperty("outputDatum")), NumericRequest(p.GetProperty("numericRequest")),
            p.GetProperty("afterWorldToBeforeWorldMm").ValueKind == JsonValueKind.Null ? null : Frame(p.GetProperty("afterWorldToBeforeWorldMm")),
            Items(p, "witnessRoots", 32).Select(R.Quantity), MechanicalAuthoringJson.NullableString(p, "beforeInputId"), MechanicalAuthoringJson.NullableString(p, "afterInputId"));
        Require(request.RequestId == S(p, "requestId"), "Assembly comparison request identity mismatch."); return request;
    }
    internal static void Comparison(Utf8JsonWriter w, MechanicalAssemblyOutputComparisonResult result)
    {
        Start(w, "gear-invest.mechanical-assembly-output-comparison", result.FamilyResult is AssemblyGenevaComparisonEvidence ? GenevaSuffixVersion : Version); w.WriteString("resultId", result.ResultId);
        w.WritePropertyName("request"); ComparisonRequest(w, result.Request);
        w.WriteString("beforeAnalysisId", result.BeforeAnalysisId); w.WriteString("afterAnalysisId", result.AfterAnalysisId);
        w.WriteString("beforeDefinitionId", result.BeforeDefinitionId); w.WriteString("afterDefinitionId", result.AfterDefinitionId);
        w.WriteString("beforeExportAdmission", result.BeforeExportAdmission.ToString()); w.WriteString("afterExportAdmission", result.AfterExportAdmission.ToString());
        w.WriteString("verdict", result.Verdict.ToString()); w.WriteString("proofRule", result.ProofRule); w.WriteString("scope", result.Scope);
        Integer(w, "numericWork", result.NumericWork); Integer(w, "freshAnalysisCount", result.FreshAnalysisCount); w.WriteString("familyResultIdentity", result.FamilyResultIdentity);
        w.WritePropertyName("familyResult");
        switch (result.FamilyResult)
        {
            case OutputEquivalenceResult family: MechanicalAuthoringJson.Comparison(w, family); break;
            case GenevaMotionComparisonResult family: GenevaJson.Comparison(w, family); break;
            case CrankSliderOutputEquivalenceResult family: CrankSliderJson.Comparison(w, family); break;
            case CamFollowerOutputEquivalenceResult family: CamFollowerJson.Comparison(w, family); break;
            case AssemblyGenevaComparisonEvidence family:
                w.WriteStartObject(); w.WriteString("evidenceId", family.EvidenceId); w.WriteString("beforeMotionId", family.BeforeMotionId); w.WriteString("afterMotionId", family.AfterMotionId);
                if (family.ConstantDifference.HasValue) R.Derived(w, "constantDifference", family.ConstantDifference.Value); else w.WriteNull("constantDifference");
                Array(w, "witnesses", family.Witnesses, (writer, witness) =>
                {
                    writer.WriteStartObject(); R.DerivedQuantity(writer, "beforeRoot", witness.BeforeRoot); R.DerivedQuantity(writer, "afterRoot", witness.AfterRoot);
                    writer.WriteStartObject("before"); R.Derived(writer, "lower", witness.Before.Lower); R.Derived(writer, "upper", witness.Before.Upper); writer.WriteEndObject();
                    writer.WriteStartObject("mappedAfter"); R.Derived(writer, "lower", witness.MappedAfter.Lower); R.Derived(writer, "upper", witness.MappedAfter.Upper); writer.WriteEndObject();
                    writer.WriteBoolean("isSeparating", witness.IsSeparating); writer.WriteEndObject();
                }); w.WriteEndObject(); break;
            case null: w.WriteNullValue(); break;
            default: throw new ArtifactFormatException("Unknown assembly whole-function comparison type.");
        }
        Array(w, "diagnostics", result.Diagnostics, Diagnostic); w.WriteEndObject();
    }
}
