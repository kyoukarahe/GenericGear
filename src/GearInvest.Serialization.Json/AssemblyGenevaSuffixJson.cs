using System.Text.Json;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class MechanicalAssemblyJson
{
    private static void GenevaAffineMotion(Utf8JsonWriter w, AssemblyGenevaAffineMotion? motion)
    {
        if (motion is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("motionKind", motion.MotionKind); w.WriteString("motionId", motion.MotionId);
        w.WritePropertyName("genevaShaft"); Reference(w, motion.GenevaShaft);
        R.Affine(w, "transformOverGenevaShaft", motion.Transform);
        w.WritePropertyName("genevaDescriptor"); GenevaJson.Descriptor(w, motion.Geneva); w.WriteEndObject();
    }
    private static void GenevaAffinePose(Utf8JsonWriter w, AssemblyGenevaAffinePose pose)
    {
        w.WriteStartObject(); w.WriteString("recipeId", pose.RecipeId); w.WritePropertyName("motion"); GenevaAffineMotion(w, pose.Motion);
        w.WritePropertyName("genevaRecipe"); GenevaJson.PoseRecipe(w, pose.Geneva);
        R.Derived(w, "accumulatedTurns", pose.AccumulatedTurns); R.Derived(w, "residualCoefficient", pose.ResidualCoefficient);
        if (pose.ExactTurns.HasValue) R.Derived(w, "exactTurns", pose.ExactTurns.Value); else w.WriteNull("exactTurns"); w.WriteEndObject();
    }
    private static void SuffixInterval(Utf8JsonWriter w, string name, CrankSliderInterval? value, string unit)
    {
        w.WritePropertyName(name);
        if (value is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WriteString("unit", unit); R.Derived(w, "lower", value.Lower); R.Derived(w, "upper", value.Upper);
        w.WriteBoolean("isExact", value.IsExact); w.WriteEndObject();
    }
    private static void GenevaSuffix(Utf8JsonWriter w, AssemblyGenevaSuffixEvaluation? value)
    {
        if (value is null) { w.WriteNullValue(); return; }
        w.WriteStartObject(); w.WritePropertyName("input"); GenevaAffinePose(w, value.Input);
        w.WritePropertyName("shaft"); GenevaAffinePose(w, value.Shaft); w.WritePropertyName("terminal"); GenevaAffinePose(w, value.Terminal);
        w.WriteString("status", value.Status.ToString()); w.WriteBoolean("isAvailable", value.IsAvailable); w.WriteString("detail", value.Detail);
        Integer(w, "numericWork", value.NumericWork); Integer(w, "precisionBits", value.PrecisionBits);
        Integer(w, "upstreamEvaluations", value.UpstreamEvaluations); Integer(w, "upstreamReuses", value.UpstreamReuses); Integer(w, "upstreamRefinements", value.UpstreamRefinements);
        R.Affine(w, "wormInputPhaseOverGenevaShaft", value.WormInputPhaseOverGenevaShaft);
        R.Affine(w, "wormWheelPhaseOverGenevaShaft", value.WormWheelPhaseOverGenevaShaft);
        if (value.ExactWormPhaseIdentity.HasValue) w.WriteBoolean("exactWormPhaseIdentity", value.ExactWormPhaseIdentity.Value); else w.WriteNull("exactWormPhaseIdentity");
        SuffixInterval(w, "inputTurns", value.InputTurns, "turn"); SuffixInterval(w, "shaftTurns", value.ShaftTurns, "turn"); SuffixInterval(w, "terminalTurns", value.TerminalTurns, "turn");
        w.WritePropertyName("beltTravel");
        if (value.BeltTravel is null) w.WriteNullValue();
        else
        {
            var travel = value.BeltTravel; w.WriteStartObject(); w.WriteString("kind", travel.Kind); w.WriteString("unit", travel.Unit);
            R.Affine(w, "piCoefficientOverGenevaShaft", travel.PiCoefficientOverGenevaShaft);
            R.Derived(w, "accumulatedPiCoefficientMm", travel.AccumulatedPiCoefficientMm); R.Derived(w, "residualPiCoefficientMm", travel.ResidualPiCoefficientMm);
            if (travel.ExactPiCoefficientMm.HasValue) R.Derived(w, "exactPiCoefficientMm", travel.ExactPiCoefficientMm.Value); else w.WriteNull("exactPiCoefficientMm");
            SuffixInterval(w, "displacementMm", value.BeltTravelMm, "mm"); w.WriteEndObject();
        }
        w.WriteEndObject();
    }
}
