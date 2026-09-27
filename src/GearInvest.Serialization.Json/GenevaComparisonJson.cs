using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R=GearInvest.Serialization.Json.RotaryLinearJson;
namespace GearInvest.Serialization.Json;
public static partial class GenevaJson
{
    public static byte[] WriteComparison(GenevaMotionComparisonResult result)=>Guard(()=>Encode(w=>Comparison(w,result)));
    public static GenevaMotionComparisonResult VerifyComparison(GenevaAnalysis before,GenevaAnalysis after,GenevaMotionComparisonRequest request,byte[] bytes)=>Guard(()=>
    {using var p=Parse(bytes);var r=GenevaOutputComparer.Compare(before,after,request);Require(bytes.SequenceEqual(WriteComparison(r)),"Stored comparison differs from current analyses and explicit correspondence.");return r;});
    public static byte[] WriteComparisonRequest(GenevaMotionComparisonRequest request)=>Guard(()=>Encode(w=>
    {Start(w,"gear-invest.geneva-comparison-request");w.WritePropertyName("request");ComparisonRequest(w,request);w.WriteEndObject();}));
    public static GenevaMotionComparisonRequest ReadComparisonRequest(byte[] bytes)=>Guard(()=>
    {using var p=Parse(bytes);Header(p.RootElement,"gear-invest.geneva-comparison-request");var r=ComparisonRequest(p.RootElement.GetProperty("request"));Require(bytes.SequenceEqual(WriteComparisonRequest(r)),"Noncanonical Geneva comparison request.");return r;});
    private static void ComparisonRequest(Utf8JsonWriter w,GenevaMotionComparisonRequest r)
    {
        w.WriteStartObject();w.WriteString("requestId",r.RequestId);w.WriteString("beforeOutputKey",r.BeforeOutputKey);w.WriteString("afterOutputKey",r.AfterOutputKey);w.WriteString("beforeInputId",r.BeforeInputId);w.WriteString("afterInputId",r.AfterInputId);
        R.Quantity(w,"alpha",r.Alpha);R.Quantity(w,"beta",r.Beta);Integer(w,"sign",r.Sign);R.Quantity(w,"delta",r.Delta);w.WriteString("mode",r.Mode.ToString());w.WriteString("domain",r.Domain);w.WriteString("proofPolicy",r.ProofPolicy);
        Array(w,"witnessRoots",r.WitnessRoots,(x,root)=>{x.WriteStartObject();R.Quantity(x,"root",root);x.WriteEndObject();});w.WritePropertyName("numericRequest");NumericOptions(w,r.NumericRequest);w.WriteEndObject();
    }
    private static GenevaMotionComparisonRequest ComparisonRequest(JsonElement p)
    {
        var r=new GenevaMotionComparisonRequest(S(p,"beforeOutputKey"),S(p,"afterOutputKey"),S(p,"beforeInputId"),S(p,"afterInputId"),R.Quantity(p.GetProperty("alpha")),R.Quantity(p.GetProperty("beta")),I(p,"sign"),R.Quantity(p.GetProperty("delta")),
            E<GenevaComparisonMode>(p,"mode"),Items(p,"witnessRoots",32).Select(x=>R.Quantity(x.GetProperty("root"))),NumericOptions(p.GetProperty("numericRequest")),S(p,"domain"),S(p,"proofPolicy"));
        Require(r.RequestId==S(p,"requestId"),"Geneva comparison request identity mismatch.");return r;
    }
    internal static void Comparison(Utf8JsonWriter w,GenevaMotionComparisonResult r)
    {
        Start(w,"gear-invest.geneva-output-comparison");w.WriteString("resultId",r.ResultId);w.WritePropertyName("request");ComparisonRequest(w,r.Request);
        w.WriteString("beforeAnalysisId",r.BeforeAnalysisId);w.WriteString("afterAnalysisId",r.AfterAnalysisId);w.WriteString("verdict",r.Verdict.ToString());w.WriteString("proofRule",r.ProofRule);w.WriteString("policy",r.Policy);w.WriteString("scope",r.Scope);
        w.WriteString("beforeExportAdmission",r.BeforeExportAdmission.ToString());w.WriteString("afterExportAdmission",r.AfterExportAdmission.ToString());w.WriteBoolean("beforeFullCycleMotion",r.BeforeFullCycleMotion);w.WriteBoolean("afterFullCycleMotion",r.AfterFullCycleMotion);
        w.WriteBoolean("materialRegistrationEqual",r.MaterialRegistrationEqual);w.WriteBoolean("geometryEqual",r.GeometryEqual);Integer(w,"numericWork",r.NumericWork);
        Array(w,"witnesses",r.Witnesses,(x,v)=>{x.WriteStartObject();R.DerivedQuantity(x,"beforeRoot",v.BeforeRoot);R.DerivedQuantity(x,"afterRoot",v.AfterRoot);Interval(x,"before",v.Before,"turn");Interval(x,"mappedAfter",v.MappedAfter,"turn");x.WriteBoolean("isSeparating",v.IsSeparating);x.WriteEndObject();});
        Array(w,"diagnostics",r.Diagnostics,MechanicalAuthoringJson.Diagnostic);w.WriteEndObject();
    }
}
