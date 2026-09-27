using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class GenevaJson
{
    public static byte[] WriteAnalysis(GenevaAnalysis analysis) => Guard(()=>Encode(w=>Analysis(w,analysis)));
    public static GenevaAnalysis VerifyAnalysis(GenevaDraft draft,byte[] bytes) => Guard(()=>
    {using var p=Parse(bytes);return VerifyAnalysis(draft,NumericOptions(p.RootElement.GetProperty("localCompatibility").GetProperty("request")),bytes);});
    public static GenevaAnalysis VerifyAnalysis(GenevaDraft draft,GenevaNumericRequest request,byte[] bytes) => Guard(()=>
    {using var p=Parse(bytes);var a=GenevaAnalyzer.Analyze(draft,request);Require(bytes.SequenceEqual(WriteAnalysis(a)),"Stored Geneva analysis differs from fresh current definition and explicit request.");return a;});
    public static byte[] WriteCompatibility(GenevaCompatibilityResult result) => Guard(()=>Encode(w=>Compatibility(w,result)));
    public static GenevaCompatibilityResult VerifyCompatibility(GenevaDraft draft,GenevaNumericRequest request,byte[] bytes) => Guard(()=>
    {using var p=Parse(bytes);var c=GenevaAnalyzer.Query(draft,request);Require(bytes.SequenceEqual(WriteCompatibility(c)),"Stored compatibility differs from fresh current geometry and explicit request.");return c;});
    public static byte[] WriteEvaluation(GenevaEvaluation result) => Guard(()=>Encode(w=>Evaluation(w,result)));
    public static GenevaEvaluation VerifyEvaluation(GenevaAnalysis analysis,ExactQuantity root,GenevaNumericRequest request,byte[] bytes) => Guard(()=>
    {using var p=Parse(bytes);var e=GenevaAnalyzer.Evaluate(analysis,root,request);Require(bytes.SequenceEqual(WriteEvaluation(e)),"Stored evaluation differs from current exact root and numeric request.");return e;});
    public static byte[] WriteNumericRequest(GenevaAnalysis analysis,ExactQuantity root,GenevaNumericRequest request) => Guard(()=>Encode(w=>
    {Start(w,"gear-invest.geneva-numeric-request");BoundNumericRequest(w,analysis.DefinitionId,analysis.AnalysisId,root,analysis.CreatePoseRecipe(root)?.RecipeId,request);w.WriteEndObject();}));
    public static GenevaNumericRequest ReadNumericRequest(GenevaAnalysis analysis,ExactQuantity root,byte[] bytes) => Guard(()=>
    {using var p=Parse(bytes);Header(p.RootElement,"gear-invest.geneva-numeric-request");var r=NumericOptions(p.RootElement.GetProperty("options"));Require(bytes.SequenceEqual(WriteNumericRequest(analysis,root,r)),"Numeric request definition/analysis/root/recipe binding mismatch.");return r;});
    private static void BoundNumericRequest(Utf8JsonWriter w,string definition,string analysis,ExactQuantity root,string? recipe,GenevaNumericRequest request)
    {
        w.WriteString("bindingPolicy","definition-analysis-unwrapped-root-recipe-numeric-options-v1");
        w.WriteString("requestId",Hash(System.Text.Encoding.UTF8.GetBytes(OrientedGoalKeys.Pack("geneva-bound-numeric-request-v1",definition,analysis,root.Kind.ToString(),root.Unit,root.Value.ToString(),recipe??"",request.CanonicalRepresentation))));
        w.WriteString("definitionId",definition);w.WriteString("analysisId",analysis);R.Quantity(w,"rootTurns",root);w.WriteString("recipeId",recipe);w.WritePropertyName("options");NumericOptions(w,request);
    }
    private static void Analysis(Utf8JsonWriter w,GenevaAnalysis a)
    {
        Start(w,AnalysisFormat);w.WriteString("analysisId",a.AnalysisId);w.WriteString("definitionId",a.DefinitionId);w.WriteString("draftId",a.DraftId);
        MechanicalAuthoringJson.Long(w,"revision",a.Revision);w.WriteString("policy",a.Policy);w.WriteString("motionDomain",a.MotionDomain);w.WriteString("selectedInputId",a.SelectedInputId);
        w.WriteBoolean("isMechanicallyValid",a.IsMechanicallyValid);w.WriteString("exportAdmission",a.ExportAdmission.ToString());
        w.WritePropertyName("sourceAnalysis");MechanicalAuthoringJson.Analysis(w,a.SourceAnalysis);w.WritePropertyName("localCompatibility");Compatibility(w,a.LocalCompatibility);
        w.WriteString("determinacy",a.Determinacy.ToString());R.Affine(w,"sourceRelation",a.SourceRelation);w.WritePropertyName("descriptor");Descriptor(w,a.Descriptor);
        w.WriteBoolean("hasDeterminedDriverMotion",a.HasDeterminedDriverMotion);w.WriteBoolean("hasFullCycleMotion",a.HasFullCycleMotion);w.WriteString("target",a.Target.ToString());
        Integer(w,"numericWork",a.NumericWork);w.WritePropertyName("referenceNumeric");NumericComputation(w,a.ReferenceNumeric);
        Array(w,"checks",a.Checks,R.Check);Array(w,"diagnostics",a.Diagnostics,MechanicalAuthoringJson.Diagnostic);w.WriteEndObject();
    }
    internal static void Compatibility(Utf8JsonWriter w,GenevaCompatibilityResult c)
    {
        Start(w,"gear-invest.geneva-compatibility");w.WriteString("resultId",c.ResultId);w.WriteString("draftId",c.DraftId);w.WriteString("deviceId",c.DeviceId);
        w.WritePropertyName("request");NumericOptions(w,c.Request);w.WriteString("verdict",c.Verdict.ToString());w.WriteBoolean("isAdmitted",c.IsAdmitted);Integer(w,"numericWork",c.NumericWork);
        w.WriteBoolean("hasValidDriverMounting",c.HasValidDriverMounting);w.WriteBoolean("pinSlotGeometryAdmitted",c.PinSlotGeometryAdmitted);w.WriteBoolean("lockGeometryAdmitted",c.LockGeometryAdmitted);
        OptionalDerived(w,"centerDistanceMm",c.CenterDistanceMm);OptionalDerived(w,"gammaIn",c.GammaIn);OptionalDerived(w,"epsilonIn",c.EpsilonIn);OptionalDerived(w,"gammaOut",c.GammaOut);OptionalDerived(w,"epsilonOut",c.EpsilonOut);
        if(c.MappedSourceFrameMm is null)w.WriteNull("mappedSourceFrameMm");else DerivedFrame(w,"mappedSourceFrameMm",c.MappedSourceFrameMm);
        w.WritePropertyName("lockProof");if(c.LockProof is null)w.WriteNullValue();else
        {
            var l=c.LockProof;w.WriteStartObject();w.WriteString("rule",l.Rule);R.Derived(w,"centerDistanceMm",l.CenterDistanceMm);R.Derived(w,"radiusMm",l.RadiusMm);
            R.Derived(w,"patchHalfWidthTurns",l.PatchHalfWidthTurns);R.Derived(w,"lowerWitnessTurns",l.LowerWitnessTurns);R.Derived(w,"upperWitnessTurns",l.UpperWitnessTurns);
            Integer(w,"lowerNormalVelocitySign",l.LowerNormalVelocitySign);Integer(w,"upperNormalVelocitySign",l.UpperNormalVelocitySign);R.Derived(w,"envelopeSquaredMarginUpperMm2",l.EnvelopeSquaredMarginUpperMm2);w.WriteEndObject();
        }
        Array(w,"lengthComparisons",c.LengthComparisons,(x,l)=>
        {x.WriteStartObject();x.WriteString("subject",l.Subject);x.WritePropertyName("left");Length(x,l.Left);x.WritePropertyName("right");Length(x,l.Right);
            x.WriteString("order",l.Order.ToString());x.WriteString("status",l.Status.ToString());Integer(x,"work",l.Work);Integer(x,"precisionBits",l.PrecisionBits);
            OptionalInterval(x,"differenceMm",l.DifferenceMm,"mm");x.WriteString("detail",l.Detail);x.WriteEndObject();});
        Array(w,"facts",c.Facts,(x,f)=>{x.WriteStartObject();x.WriteString("key",f.Key);OptionalDerived(x,"expected",f.Expected);OptionalDerived(x,"actual",f.Actual);x.WriteEndObject();});
        Array(w,"checks",c.Checks,R.Check);Array(w,"diagnostics",c.Diagnostics,MechanicalAuthoringJson.Diagnostic);w.WriteEndObject();
    }
    internal static void Descriptor(Utf8JsonWriter w,GenevaMotionDescriptor? d)
    {
        if(d is null){w.WriteNullValue();return;}
        w.WriteStartObject();w.WriteString("descriptorId",d.DescriptorId);w.WriteString("policy",GenevaProfile.AnalysisPolicy);w.WriteString("definitionId",d.DefinitionIdentity);
        R.Affine(w,"sourceRelation",d.SourceRelation);R.Affine(w,"physicalPhase",d.PhysicalPhase);R.Derived(w,"centerDistanceMm",d.CenterDistanceMm);
        R.Derived(w,"gammaIn",d.GammaIn);R.Derived(w,"epsilonIn",d.EpsilonIn);R.Derived(w,"gammaOut",d.GammaOut);R.Derived(w,"epsilonOut",d.EpsilonOut);
        Integer(w,"slotCount",d.SlotCount);R.Derived(w,"halfIndexTurns",d.HalfIndexTurns);R.Derived(w,"halfStepTurns",d.HalfStepTurns);
        R.Derived(w,"indexFraction",d.IndexFraction);R.Derived(w,"dwellFraction",d.DwellFraction);R.Derived(w,"averageTerminalAdvancePerRoot",d.AverageTerminalAdvancePerRoot);
        w.WriteString("law","-floor(phi+1/2)/N+piecewise-positive-denominator-principal-atan");w.WriteEndObject();
    }
    internal static void PoseRecipe(Utf8JsonWriter w,GenevaPoseRecipe? p)
    {
        if(p is null){w.WriteNullValue();return;}
        w.WriteStartObject();w.WriteString("recipeId",p.RecipeId);w.WriteString("descriptorId",p.Motion.DescriptorId);R.DerivedQuantity(w,"rootTurns",p.RootTurns);
        R.Derived(w,"sourceTurns",p.SourceTurns);R.Derived(w,"physicalDriverTurns",p.PhysicalDriverTurns);w.WriteString("cycleIndex",p.CycleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        R.Derived(w,"centeredPhaseTurns",p.CenteredPhaseTurns);w.WriteString("regime",p.Regime.ToString());w.WriteString("residualKind",p.ResidualKind);
        OptionalDerived(w,"exactResidualTurns",p.ExactResidualTurns);R.Derived(w,"accumulatedPhysicalTurns",p.AccumulatedPhysicalTurns);R.Derived(w,"accumulatedShaftTurns",p.AccumulatedShaftTurns);
        R.Derived(w,"wheelMaterialStepTurns",p.WheelMaterialStepTurns);OptionalDerived(w,"exactShaftTurns",p.ExactShaftTurns);OptionalDerived(w,"exactTerminalTurns",p.ExactTerminalTurns);
        Integer(w,"nominalSlotIndex",p.NominalSlotIndex);w.WriteString("slotId",p.SlotId);if(p.RecessIndex.HasValue)Integer(w,"recessIndex",p.RecessIndex.Value);else w.WriteNull("recessIndex");w.WriteString("recessId",p.RecessId);
        w.WriteBoolean("pinPhaseEngaged",p.PinPhaseEngaged);w.WriteBoolean("lockPhaseEngaged",p.LockPhaseEngaged);w.WriteEndObject();
    }
    private static void Evaluation(Utf8JsonWriter w,GenevaEvaluation e)
    {
        Start(w,"gear-invest.geneva-evaluation");w.WriteString("resultId",e.ResultId);BoundNumericRequest(w,e.DefinitionId,e.AnalysisId,e.RootTurns,e.Recipe?.RecipeId,e.Request);
        w.WriteString("status",e.Status.ToString());w.WriteBoolean("hasNormalPose",e.HasNormalPose);Integer(w,"numericWork",e.NumericWork);
        Array(w,"sourceShafts",e.SourceShafts,(x,s)=>{x.WriteStartObject();x.WriteString("shaftId",s.ShaftId);R.Derived(x,"turns",s.Turns);R.DerivedVector(x,"positiveAxis",s.PositiveAxis,"dimensionless");R.DerivedVector(x,"worldAngularVelocityPerRoot",s.WorldAngularVelocityPerRoot,"turn/turn");x.WriteEndObject();});
        w.WritePropertyName("driver");if(e.Driver is null)w.WriteNullValue();else
        {var d=e.Driver;w.WriteStartObject();w.WriteString("bodyId",d.BodyId);R.DerivedVector(w,"centerMm",d.CenterMm,"mm");R.DerivedVector(w,"positiveAxis",d.PositiveAxis,"dimensionless");R.DerivedVector(w,"zeroRay",d.ZeroRay,"dimensionless");R.Derived(w,"sourceTurns",d.SourceTurns);OptionalDerived(w,"physicalPhaseTurns",d.PhysicalPhaseTurns);w.WriteEndObject();}
        w.WritePropertyName("recipe");PoseRecipe(w,e.Recipe);w.WritePropertyName("numeric");NumericComputation(w,e.Numeric);w.WritePropertyName("driverNumeric");DriverNumeric(w,e.DriverNumeric);
        w.WritePropertyName("normalPose");NumericPose(w,e.NormalPose?.Numeric);w.WritePropertyName("diagnosticPose");NumericPose(w,e.DiagnosticPose);
        Array(w,"diagnostics",e.Diagnostics,MechanicalAuthoringJson.Diagnostic);w.WriteEndObject();
    }
    private static void NumericOptions(Utf8JsonWriter w,GenevaNumericRequest r)
    {
        w.WriteStartObject();w.WriteString("policy",r.Policy);R.Quantity(w,"angularWidth",r.AngularWidth);R.Quantity(w,"linearWidth",r.LinearWidth);Fraction(w,"directionWidth",r.DirectionWidth);
        Integer(w,"maximumWork",r.MaximumWork);Integer(w,"maximumPrecisionBits",r.MaximumPrecisionBits);Integer(w,"maximumRefinements",r.MaximumRefinements);w.WriteEndObject();
    }
    private static GenevaNumericRequest NumericOptions(JsonElement p) => new(R.Quantity(p.GetProperty("angularWidth")),R.Quantity(p.GetProperty("linearWidth")),F(p.GetProperty("directionWidth")),I(p,"maximumWork"),I(p,"maximumPrecisionBits"),I(p,"maximumRefinements"),MechanicalAuthoringJson.NullableString(p,"policy"));
    internal static void NumericComputation(Utf8JsonWriter w,GenevaNumericComputation? c)
    {
        if(c is null){w.WriteNullValue();return;}w.WriteStartObject();w.WriteString("status",c.Status.ToString());Integer(w,"work",c.Work);Integer(w,"precisionBits",c.PrecisionBits);Integer(w,"refinements",c.Refinements);
        w.WriteBoolean("isAvailable",c.IsAvailable);w.WriteString("detail",c.Detail);w.WritePropertyName("pose");NumericPose(w,c.Pose);w.WritePropertyName("diagnosticPose");NumericPose(w,c.DiagnosticPose);w.WriteEndObject();
    }
    internal static void DriverNumeric(Utf8JsonWriter w,GenevaDriverNumericComputation? c)
    {
        if(c is null){w.WriteNullValue();return;}w.WriteStartObject();w.WriteString("status",c.Status.ToString());Integer(w,"work",c.Work);Integer(w,"precisionBits",c.PrecisionBits);Integer(w,"refinements",c.Refinements);
        w.WriteBoolean("isAvailable",c.IsAvailable);w.WriteString("detail",c.Detail);OptionalVectorInterval(w,"pinMm",c.PinMm,"mm");OptionalVectorInterval(w,"driverE",c.DriverE,"dimensionless");OptionalVectorInterval(w,"driverF",c.DriverF,"dimensionless");
        Array(w,"featurePoints",c.FeaturePoints,FeaturePoint);w.WriteEndObject();
    }
    private static void NumericPose(Utf8JsonWriter w,GenevaNumericPose? p)
    {
        if(p is null){w.WriteNullValue();return;}w.WriteStartObject();
        Interval(w,"residualTurns",p.ResidualTurns,"turn");Interval(w,"outputShaftTurns",p.OutputShaftTurns,"turn");Interval(w,"terminalTurns",p.TerminalTurns,"turn");
        VectorInterval(w,"pinMm",p.PinMm,"mm");VectorInterval(w,"pinRadialUnit",p.PinRadialUnit,"dimensionless");VectorInterval(w,"driverE",p.DriverE,"dimensionless");VectorInterval(w,"driverF",p.DriverF,"dimensionless");VectorInterval(w,"wheelE",p.WheelE,"dimensionless");VectorInterval(w,"wheelF",p.WheelF,"dimensionless");
        Interval(w,"pinRadiusMm",p.PinRadiusMm,"mm");Interval(w,"pinOrbitRadiusMm",p.PinOrbitRadiusMm,"mm");Array(w,"featurePoints",p.FeaturePoints,FeaturePoint);w.WriteEndObject();
    }
    private static void FeaturePoint(Utf8JsonWriter w,GenevaNumericFeaturePoint p) {w.WriteStartObject();w.WriteString("id",p.Id);VectorInterval(w,"pointMm",p.PointMm,"mm");w.WriteEndObject();}
    private static void OptionalDerived(Utf8JsonWriter w,string key,Rational? value){if(value.HasValue)R.Derived(w,key,value.Value);else w.WriteNull(key);}
    private static void DerivedFrame(Utf8JsonWriter w,string key,OrientedFrame f)
    {w.WriteStartObject(key);R.DerivedVector(w,"origin",f.Origin,"mm");R.DerivedVector(w,"x",f.X,"dimensionless");R.DerivedVector(w,"y",f.Y,"dimensionless");R.DerivedVector(w,"z",f.Z,"dimensionless");w.WriteEndObject();}
    private static void OptionalInterval(Utf8JsonWriter w,string key,GenevaInterval? i,string unit){if(i is null)w.WriteNull(key);else Interval(w,key,i,unit);}
    private static void Interval(Utf8JsonWriter w,string key,GenevaInterval i,string unit)
    {w.WriteStartObject(key);w.WriteString("representation","certified-rational-enclosure");w.WriteString("unit",unit);w.WriteString("boundary","closed");R.Derived(w,"lower",i.Lower);R.Derived(w,"upper",i.Upper);R.Derived(w,"width",i.Width);w.WriteBoolean("isExact",i.IsExact);w.WriteEndObject();}
    private static void OptionalVectorInterval(Utf8JsonWriter w,string key,GenevaVectorInterval? v,string unit){if(v is null)w.WriteNull(key);else VectorInterval(w,key,v,unit);}
    private static void VectorInterval(Utf8JsonWriter w,string key,GenevaVectorInterval v,string unit)
    {w.WriteStartObject(key);Interval(w,"x",v.X,unit);Interval(w,"y",v.Y,unit);Interval(w,"z",v.Z,unit);w.WriteEndObject();}
    public static byte[] WriteFinalization(string status,string definitionId,string? artifactIdentity,string? artifactBytesHash,IEnumerable<MechanicalDiagnostic> diagnostics)=>Guard(()=>Encode(w=>
    {Start(w,"gear-invest.geneva-finalization");w.WriteString("status",status);w.WriteString("definitionId",definitionId);w.WriteString("artifactIdentity",artifactIdentity);w.WriteString("artifactBytesHash",artifactBytesHash);Array(w,"diagnostics",diagnostics,MechanicalAuthoringJson.Diagnostic);w.WriteEndObject();}));
}
