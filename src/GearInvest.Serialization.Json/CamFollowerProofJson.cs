using System;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;
using R = GearInvest.Serialization.Json.RotaryLinearJson;

namespace GearInvest.Serialization.Json;

public static partial class CamFollowerJson
{
    public static byte[] WriteProofRequest(CamSupportProfile profile, CamGeometryProofRequest request) => Guard(() => Encode(w =>
    {
        Start(w, "gear-invest.cam-geometry-proof-request"); w.WriteString("profileId", profile.ProfileId);
        w.WritePropertyName("options"); ProofOptions(w, request); w.WriteEndObject();
    }));
    public static CamGeometryProofRequest ReadProofRequest(CamSupportProfile profile, byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); Header(doc.RootElement, "gear-invest.cam-geometry-proof-request"); var request = ProofOptions(doc.RootElement.GetProperty("options"));
        Require(bytes.SequenceEqual(WriteProofRequest(profile, request)), "Proof request is not bound to the explicit current profile."); return request;
    });
    public static byte[] WriteProof(CamGeometryProofResult proof) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.cam-geometry-proof"); w.WritePropertyName("proof"); Proof(w, proof); w.WriteEndObject(); }));
    public static CamGeometryProofResult VerifyProof(CamSupportProfile profile, CamGeometryProofRequest request, byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var fresh = CamGeometryProver.Prove(profile, request);
        Require(bytes.SequenceEqual(WriteProof(fresh)), "Current full profile proof reconstruction differs from stored proof."); return fresh;
    });
    public static byte[] WriteContourRecipe(CamContourPointRecipe recipe) => Guard(() => Encode(w =>
    { Start(w, "gear-invest.cam-contour-recipe"); w.WritePropertyName("recipe"); ContourRecipe(w, recipe); w.WriteEndObject(); }));
    public static CamContourPointRecipe VerifyContourRecipe(CamSupportProfile profile, Rational materialNormalTurns, Rational physicalPhaseTurns,
        ExactVector3 centerMm, ExactVector3 guideDirection, ExactVector3 tangentDirection, byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var recipe = CamContourPointRecipe.Create(profile, materialNormalTurns, physicalPhaseTurns, centerMm, guideDirection, tangentDirection);
        Require(bytes.SequenceEqual(WriteContourRecipe(recipe)), "Current contour support/derivative recipe differs from stored recipe."); return recipe;
    });
    public static byte[] WriteContourEvaluation(CamContourPointRecipe recipe, CamNumericResult result) => Guard(() => Encode(w =>
    {
        Require(result.InputId == recipe.RecipeId, "Contour numerical result recipe binding mismatch.");
        Start(w, "gear-invest.cam-contour-evaluation"); w.WriteString("scope", "numerical-contour-point-only-not-follower-admission");
        w.WritePropertyName("recipe"); ContourRecipe(w, recipe); w.WritePropertyName("numeric"); NumericComputation(w, result); w.WriteEndObject();
    }));
    public static CamNumericResult VerifyContourEvaluation(CamContourPointRecipe recipe, CamNumericRequest request, byte[] bytes) => Guard(() =>
    {
        using var doc = Parse(bytes); var result = CamFollowerNumerics.Evaluate(recipe, request);
        Require(bytes.SequenceEqual(WriteContourEvaluation(recipe, result)), "Current contour request and numerical reconstruction differs from stored result."); return result;
    });
    private static void ProofOptions(Utf8JsonWriter w, CamGeometryProofRequest r)
    {
        w.WriteStartObject(); w.WriteString("policy", r.Policy); Integer(w, "maximumPrecisionBits", r.MaximumPrecisionBits);
        Integer(w, "maximumDepth", r.MaximumDepth); Integer(w, "maximumNodes", r.MaximumNodes); Integer(w, "maximumWork", r.MaximumWork); w.WriteEndObject();
    }
    private static CamGeometryProofRequest ProofOptions(JsonElement p) => new(I(p, "maximumPrecisionBits"), I(p, "maximumDepth"), I(p, "maximumNodes"), I(p, "maximumWork"), MechanicalAuthoringJson.NullableString(p, "policy"));
    private static void Proof(Utf8JsonWriter w, CamGeometryProofResult p)
    {
        w.WriteStartObject(); w.WriteString("profileId", p.ProfileId); w.WriteString("proofId", p.ProofId); w.WritePropertyName("request"); ProofOptions(w, p.Request);
        w.WriteString("status", p.Status.ToString()); w.WriteBoolean("isProved", p.IsProved); w.WriteBoolean("isRefuted", p.IsRefuted);
        Integer(w, "work", p.Work); Integer(w, "nodes", p.Nodes); Integer(w, "precisionBits", p.PrecisionBits);
        Integer(w, "maximumDepthReached", p.MaximumDepthReached); Integer(w, "provedLeaves", p.ProvedLeaves);
        OptionalDerived(w, "minimumRadiusLowerBoundMm", p.MinimumRadiusLowerBoundMm); w.WriteString("summary", p.Summary);
        w.WritePropertyName("witness");
        if (p.Witness is null) w.WriteNullValue(); else
        {
            var v = p.Witness; w.WriteStartObject(); w.WriteString("segmentId", v.SegmentId); R.Derived(w, "localParameter", v.LocalParameter);
            R.Derived(w, "materialNormalTurns", v.MaterialNormalTurns); R.Derived(w, "heightMm", v.HeightMm); R.Derived(w, "secondDerivativeMmPerTurnSquared", v.SecondDerivativeMmPerTurnSquared);
            w.WriteString("radiusRecipe", "h+h_tt/(4*pi*pi)"); Interval(w, "radiusInterval", v.RadiusInterval, "mm"); w.WriteEndObject();
        }
        w.WriteEndObject();
    }
    private static void ContourRecipe(Utf8JsonWriter w, CamContourPointRecipe p)
    {
        w.WriteStartObject(); w.WriteString("recipeId", p.RecipeId); w.WriteString("profileId", p.ProfileId);
        R.Derived(w, "materialNormalTurns", p.MaterialNormalTurns); R.Derived(w, "physicalPhaseTurns", p.PhysicalPhaseTurns); R.Derived(w, "normalTurns", p.NormalTurns);
        R.DerivedVector(w, "centerMm", p.CenterMm, "mm"); R.DerivedVector(w, "guideDirection", p.GuideDirection, "dimensionless"); R.DerivedVector(w, "tangentDirection", p.TangentDirection, "dimensionless");
        w.WriteString("positionRecipe", "O+h*N+(h_t/(2*pi))*T"); w.WritePropertyName("support"); SupportSample(w, p.Support); w.WriteEndObject();
    }
}
