using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Layout;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

/// <summary>Explicit AOT proof codec. Reading does not turn self-consistency into original graph/context authority.</summary>
public static class PitchClearanceJson
{
    public const string QueryFormat = "gear-invest.pitch-clearance-query";
    public const string ProofFormat = "gear-invest.pitch-clearance-proof";
    public static byte[] WriteQuery(PitchShape a, PitchShape b, bool required = true, PitchPairScope scope = PitchPairScope.Primitive) => Bytes(w => {
        Header(w, QueryFormat); w.WritePropertyName("shapeA"); Shape(w, a); w.WritePropertyName("shapeB"); Shape(w, b);
        w.WriteString("scope", scope.ToString()); w.WriteBoolean("required", required); w.WriteEndObject(); });
    public static PitchPairProof ClassifyQuery(byte[] bytes) => CheckedRead(() => {
        using var doc = Open(bytes); var p = doc.RootElement; CheckHeader(p, QueryFormat);
        var a = Shape(p.GetProperty("shapeA")); var b = Shape(p.GetProperty("shapeB")); var scope = E<PitchPairScope>(p, "scope"); var required = p.GetProperty("required").GetBoolean();
        Require(F(p.GetProperty("minimumClearance")) == 0, "Positive/negative minimum clearance is unsupported; only explicit zero is supported.");
        Require(bytes.SequenceEqual(WriteQuery(a, b, required, scope)), "Noncanonical/unknown query fields."); return PitchClearanceClassifier.Classify(a, b, required, scope);
    });
    public static byte[] WriteProof(PitchPairProof proof) => Bytes(w => { Header(w, ProofFormat); w.WritePropertyName("proof"); Proof(w, proof); w.WriteEndObject(); });
    public static PitchPairProof ReadProof(byte[] bytes) => CheckedRead(() => {
        using var doc = Open(bytes); var p = doc.RootElement; CheckHeader(p, ProofFormat); var proof = Proof(p.GetProperty("proof"));
        Require(bytes.SequenceEqual(WriteProof(proof)), "Noncanonical/unknown proof fields."); return proof;
    });
    public static byte[] WriteValidation(OrientedValidation value) => Bytes(w => Validation(w, value));
    internal static void Policy(Utf8JsonWriter w) { w.WriteString("clearancePolicy", PitchClearancePolicy.Refined); w.WriteString("shapeSemantics", PitchClearancePolicy.Shapes); Fraction(w, "minimumClearance", 0); }
    internal static void CheckPolicy(JsonElement p) => Require(S(p, "clearancePolicy") == PitchClearancePolicy.Refined && S(p, "shapeSemantics") == PitchClearancePolicy.Shapes && F(p.GetProperty("minimumClearance")) == 0, "Unsupported clearance policy/shape semantics/margin.");
    internal static void Validation(Utf8JsonWriter w, OrientedValidation v)
    {
        if (v.ClearancePolicy == PitchClearancePolicy.Legacy)
        { Require(v.PitchProofs.Count == 0, "Legacy validation cannot carry refined proofs."); CanonicalOrientedJson.Validation(w, v); return; }
        Require(v.ClearancePolicy == PitchClearancePolicy.Refined, "Unsupported validation policy.");
        w.WriteStartObject(); Policy(w); w.WritePropertyName("domainValidation"); CanonicalOrientedJson.Validation(w, v);
        Integer(w, "pairPredicateCount", v.PitchProofs.Count); w.WriteString("coverageDigest", PitchProofKeys.CoverageDigest(v.PitchProofs)); Array(w, "pairProofs", v.PitchProofs, Proof); w.WriteEndObject();
    }
    internal static OrientedValidation Validation(JsonElement p)
    {
        if (!p.TryGetProperty("clearancePolicy", out _)) return CanonicalOrientedJson.Validation(p);
        CheckPolicy(p); var original = CanonicalOrientedJson.Validation(p.GetProperty("domainValidation"));
        var result = new OrientedValidation(original.Checks, original.Diagnostics, Items(p, "pairProofs", PitchClearancePolicy.MaxPairs).Select(Proof), PitchClearancePolicy.Refined);
        Require(result.PitchProofs.Count == I(p, "pairPredicateCount") && PitchProofKeys.CoverageDigest(result.PitchProofs) == S(p, "coverageDigest"), "Pair coverage integrity mismatch."); return result;
    }
    internal static void Shape(Utf8JsonWriter w, PitchShape s)
    {
        w.WriteStartObject(); w.WriteString("id", s.Id); w.WriteString("kind", s.Kind.ToString());
        if (s.Kind == PitchShapeKind.ClosedAabb) { Vector(w, "min", s.Box.Min); Vector(w, "max", s.Box.Max); }
        else { Vector(w, s.Kind == PitchShapeKind.ClosedDisk ? "center" : "apex", s.Center); Vector(w, s.Kind == PitchShapeKind.ClosedDisk ? "normal" : "outward", s.Direction); Fraction(w, "radius", s.Radius);
            if (s.Kind == PitchShapeKind.FiniteLateralCone) { Fraction(w, "height", s.Height); Fraction(w, "innerParameter", s.InnerParameter); } }
        w.WriteString("geometryDigest", s.GeometryDigest); w.WriteEndObject();
    }
    internal static PitchShape Shape(JsonElement p)
    {
        var kind = E<PitchShapeKind>(p, "kind"); var id = S(p, "id"); var s = kind == PitchShapeKind.ClosedAabb ? PitchShape.Aabb(id, new ExactEnvelope3(Vector(p.GetProperty("min")), Vector(p.GetProperty("max")))) :
            kind == PitchShapeKind.ClosedDisk ? PitchShape.Disk(id, Vector(p.GetProperty("center")), Vector(p.GetProperty("normal")), F(p.GetProperty("radius"))) :
            PitchShape.LateralCone(id, Vector(p.GetProperty("apex")), Vector(p.GetProperty("outward")), F(p.GetProperty("height")), F(p.GetProperty("radius")), F(p.GetProperty("innerParameter")));
        Require(s.GeometryDigest == S(p, "geometryDigest"), "Original shape geometry digest mismatch."); return s;
    }
    internal static void Proof(Utf8JsonWriter w, PitchPairProof p)
    {
        w.WriteStartObject(); w.WriteString("policy", p.Policy); w.WriteString("shapeSemantics", p.ShapeSemantics); w.WriteString("pairId", p.PairId);
        w.WritePropertyName("shapeA"); Shape(w, p.A); w.WritePropertyName("shapeB"); Shape(w, p.B); w.WriteString("geometryA", p.GeometryA); w.WriteString("geometryB", p.GeometryB);
        w.WriteString("scope", p.Scope.ToString()); w.WriteBoolean("required", p.Required); w.WriteString("broadRelation", p.Broad.ToString()); w.WriteString("method", p.Method); w.WriteString("relation", p.Relation.ToString());
        Array(w, "terms", p.Terms, (a, t) => { a.WriteStartObject(); a.WriteString("key", t.Key); a.WritePropertyName("value"); DerivedFraction(a, t.Value); a.WriteEndObject(); });
        w.WriteBoolean("boundaryEquality", p.BoundaryEquality); w.WritePropertyName("witness");
        if (p.Witness.HasValue) { w.WriteStartArray(); DerivedFraction(w, p.Witness.Value.X); DerivedFraction(w, p.Witness.Value.Y); DerivedFraction(w, p.Witness.Value.Z); w.WriteEndArray(); } else w.WriteNullValue();
        w.WriteString("proofDigest", p.Digest); w.WriteEndObject();
    }
    internal static PitchPairProof Proof(JsonElement p)
    {
        var witness = p.GetProperty("witness"); Require(witness.ValueKind == JsonValueKind.Null || witness.ValueKind == JsonValueKind.Array && witness.GetArrayLength() == 3, "Invalid exact witness vector.");
        var proof = new PitchPairProof(Shape(p.GetProperty("shapeA")), Shape(p.GetProperty("shapeB")), S(p, "policy"), S(p, "shapeSemantics"), S(p, "pairId"), S(p, "geometryA"), S(p, "geometryB"),
            E<PitchPairScope>(p, "scope"), p.GetProperty("required").GetBoolean(), E<PitchBroadRelation>(p, "broadRelation"), S(p, "method"), E<PitchPairRelation>(p, "relation"),
            Items(p, "terms", 64).Select(t => new KeyValuePair<string, Rational>(S(t, "key"), DerivedFraction(t.GetProperty("value")))), p.GetProperty("boundaryEquality").GetBoolean(),
            witness.ValueKind == JsonValueKind.Null ? null : new ExactVector3(DerivedFraction(witness[0]), DerivedFraction(witness[1]), DerivedFraction(witness[2])));
        Require(proof.Digest == S(p, "proofDigest") && PitchClearanceClassifier.Verify(proof, proof.A, proof.B, proof.Required, proof.Scope), "Exact proof recomputation failed (hash alone is insufficient)."); return proof;
    }
    private static void Header(Utf8JsonWriter w, string format) { w.WriteStartObject(); w.WriteString("format", format); w.WriteString("formatVersion", "0.1"); Policy(w); }
    private static void CheckHeader(JsonElement p, string format) { CanonicalOrientedJson.Header(p, format); CheckPolicy(p); }
    private static void DerivedFraction(Utf8JsonWriter w, Rational value)
    {
        var n = value.Numerator.ToString(CultureInfo.InvariantCulture); var d = value.Denominator.ToString(CultureInfo.InvariantCulture);
        Require(n.Length <= PitchClearancePolicy.MaxProofDigits && d.Length <= PitchClearancePolicy.MaxProofDigits, "Derived proof digit resource bound.");
        w.WriteStartObject(); w.WriteString("numerator", n); w.WriteString("denominator", d); w.WriteEndObject();
    }
    private static Rational DerivedFraction(JsonElement p)
    {
        var n = p.GetProperty("numerator").GetString(); var d = p.GetProperty("denominator").GetString();
        Require(n is not null && d is not null && n.Length <= PitchClearancePolicy.MaxProofDigits && d.Length <= PitchClearancePolicy.MaxProofDigits, "Derived proof digit resource bound.");
        var v = new Rational(BigInteger.Parse(n!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), BigInteger.Parse(d!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
        Require(v.Numerator.ToString(CultureInfo.InvariantCulture) == n && v.Denominator.ToString(CultureInfo.InvariantCulture) == d, "Noncanonical derived proof fraction."); return v;
    }
}
