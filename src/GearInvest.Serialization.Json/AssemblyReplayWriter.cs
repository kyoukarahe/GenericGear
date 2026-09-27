using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public static partial class AssemblyReplayJson
{
    /// <summary>Writes already finalized/evaluated data. The facade owns source verification and bounded public evaluation.</summary>
    public static AssemblyReplayDocument Create(MechanicalAssemblyArtifact source, byte[] originalSource,
        AssemblyReplayExportRequest request, IEnumerable<MechanicalAssemblyEvaluation> evaluations)
    {
        if (source is null || originalSource is null || request is null || evaluations is null) throw new ArgumentNullException();
        Need(originalSource.Length <= AssemblyReplayLimits.SourceBytes, "Replay source byte limit exceeded.");
        Need(MechanicalAssemblyJson.WriteArtifact(source).SequenceEqual(originalSource), "Source artifact bytes do not match the source object.");
        var values = evaluations.Take(AssemblyReplayLimits.Samples + 1).ToArray();
        var a = source.Analysis;
        Need(values.Length > 0 && values.Length <= AssemblyReplayLimits.Samples &&
            (long)values.Length * Math.Max(1, a.Inventory.Count) <= AssemblyReplayLimits.SampleChannelProduct, "Replay sample/channel limit exceeded.");
        Need(values.All(x => x.AnalysisId == a.AnalysisId), "Mixed source/evaluation identity.");
        Need(values.Sum(x => (long)x.NumericWork) + a.NumericWork <= request.MaximumTotalWork, "Replay aggregate work limit exceeded.");
        var bodies = AssemblyReplayProjection.Bodies(source);
        var payload = Encode(w =>
        {
            w.WriteStartObject(); w.WriteString("profile", Profile); w.WriteStartObject("source");
            w.WriteString("format", MechanicalAssemblyArtifact.Format); w.WriteString("formatVersion", MechanicalAssemblyJson.VersionFor(a.Draft.Definition.Profile));
            w.WriteString("profile", a.Draft.Definition.Profile); w.WriteString("artifactId", source.ArtifactHash);
            w.WriteString("definitionId", source.CandidateId); w.WriteString("draftId", a.Draft.DraftId); w.WriteString("analysisId", a.AnalysisId);
            w.WriteString("rawSha256", Hash(originalSource)); w.WriteBase64String("artifactUtf8", originalSource); w.WriteEndObject();
            w.WritePropertyName("request"); Request(w, request);
            w.WriteStartObject("producer"); w.WriteString("operation", "current-source-finalize-and-evaluate"); w.WriteString("mechanicalValidation", "Finalized");
            w.WriteString("exporterVersion", "assembly-web-export/0.1");
            w.WriteString("packageVersion", typeof(AssemblyReplayJson).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable");
            w.WriteString("policy", a.Policy); w.WriteString("numericPolicy", a.NumericRequest.Policy);
            w.WriteString("toothSolidValidation", "notPerformed"); w.WriteEndObject();
            w.WritePropertyName("definition"); MechanicalAssemblyJson.Definition(w, a.Draft.Definition);
            w.WritePropertyName("analysis"); MechanicalAssemblyJson.Analysis(w, a);
            w.WriteStartArray("shafts");
            foreach (var shaft in source.ReferenceEvaluation.Shafts)
            {
                w.WriteStartObject(); w.WritePropertyName("reference"); MechanicalAssemblyJson.Reference(w, shaft.Reference);
                w.WriteString("mode", "exactAffine"); w.WriteString("unit", "turn"); w.WritePropertyName("q"); Fraction(w, shaft.Relation.Coefficient);
                w.WritePropertyName("p"); Fraction(w, shaft.Relation.Phase); w.WritePropertyName("fixedFrameMm"); AssemblyReplayProjection.Frame(w, shaft.FixedFrameMm); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteStartArray("bodies"); foreach (var body in bodies) AssemblyReplayProjection.WriteBody(w, body); w.WriteEndArray();
            w.WritePropertyName("staticFeatures");
            var vertices = AssemblyReplayProjection.WriteSampleFeatures(w, source.ReferenceEvaluation, AssemblyReplayLimits.GeometryVertices, fixedOnly: true);
            w.WriteStartArray("samples");
            foreach (var e in values)
            {
                w.WriteStartObject(); w.WritePropertyName("root"); Fraction(w, e.RootInput.Value);
                w.WriteString("status", e.AllRequestedNumericAvailable && e.Diagnostics.Count == 0 ? "storedAvailable" : "storedPartial");
                w.WriteStartArray("exactBoundaries");
                foreach (var member in e.Members)
                    if (member.Recipe is GenevaPoseRecipe g && (g.Regime == GenevaRegime.LowerPhaseBoundary || g.Regime == GenevaRegime.UpperPhaseBoundary))
                    { w.WriteStartObject(); w.WriteString("memberId", member.InstanceId); w.WriteString("regime", g.Regime.ToString()); w.WriteString("recipeId", g.RecipeId); w.WriteEndObject(); }
                w.WriteEndArray();
                var original = MechanicalAssemblyJson.WriteEvaluation(e); w.WriteString("originalEvaluationSha256", Hash(original));
                w.WritePropertyName("observation"); using (var read = JsonDocument.Parse(original)) WriteObservation(w, read.RootElement, e);
                w.WritePropertyName("bodyFrames"); AssemblyReplayProjection.WriteSampleBodies(w, bodies, e);
                w.WritePropertyName("displayFeatures"); vertices += AssemblyReplayProjection.WriteSampleFeatures(w, e, AssemblyReplayLimits.GeometryVertices - vertices);
                Need(vertices <= AssemblyReplayLimits.GeometryVertices, "Replay geometry vertex limit exceeded."); w.WriteEndObject();
                w.Flush(); // The bounded stream refuses during production, not after allocating an unbounded document.
            }
            w.WriteEndArray(); w.WriteEndObject();
        });
        return Read(Envelope(payload));
    }

    // Static route/helix descriptions and suffix descriptors already live in source analysis once.
    // This is a new consumption observation, not a counterfeit original evaluation wire document.
    private static void WriteObservation(Utf8JsonWriter w, JsonElement original, MechanicalAssemblyEvaluation e)
    {
        w.WriteStartObject();
        foreach (var property in original.EnumerateObject())
        {
            if (property.Name == "format") { w.WriteString("format", "gear-invest.assembly-replay-observation"); continue; }
            if (property.Name == "formatVersion") { w.WriteString("formatVersion", Version); continue; }
            w.WritePropertyName(property.Name);
            if (property.Name == "shafts")
            {
                w.WriteStartArray(); foreach (var shaft in property.Value.EnumerateArray())
                { w.WriteStartObject(); foreach (var field in shaft.EnumerateObject().Where(x => x.Name is "reference" or "turns")) field.WriteTo(w); w.WriteEndObject(); } w.WriteEndArray();
            }
            else if (property.Name == "members")
            {
                w.WriteStartArray();
                foreach (var member in property.Value.EnumerateArray())
                {
                    var value = e.Members.Single(x => x.InstanceId == Text(member, "instanceId")); w.WriteStartObject();
                    foreach (var field in member.EnumerateObject())
                    {
                        if (field.Name == "material" && (value.Material is WormDriveGeometryDescriptor || value.Material is OpenBeltRouteDescriptor))
                        { w.WriteString("materialStorage", "staticAnalysis"); continue; }
                        w.WritePropertyName(field.Name); WriteWithoutStaticDescriptor(w, field.Value);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            else property.Value.WriteTo(w);
        }
        w.WriteEndObject();
    }
    private static void WriteWithoutStaticDescriptor(Utf8JsonWriter w, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            w.WriteStartObject(); foreach (var p in value.EnumerateObject())
            { if (p.Name == "genevaDescriptor") { w.WriteString("descriptorId", Text(p.Value, "descriptorId")); continue; } w.WritePropertyName(p.Name); WriteWithoutStaticDescriptor(w, p.Value); } w.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { w.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteWithoutStaticDescriptor(w, item); w.WriteEndArray(); }
        else value.WriteTo(w);
    }

    public static IReadOnlyList<Rational> ResolveSampleRoots(MechanicalAssemblyAnalysis analysis, AssemblyReplayExportRequest request)
    {
        var roots = new SortedSet<Rational>(request.SampleRoots);
        if (request.IncludeExactGenevaBoundaries)
            foreach (var m in analysis.Members)
            {
                if (m.MotionDescriptor is not GenevaMotionDescriptor g || g.PhysicalPhase.Coefficient.IsZero) continue;
                var first = g.PhysicalPhase.Evaluate(request.MinimumRoot); var last = g.PhysicalPhase.Evaluate(request.MaximumRoot);
                if (last < first) (first, last) = (last, first);
                var low = Floor(first) - 1; var high = Floor(last) + 1;
                Need(high - low <= AssemblyReplayLimits.Samples, "Too many exact Geneva boundaries in requested interval.");
                for (var cycle = low; cycle <= high; cycle++)
                    foreach (var offset in new[] { -g.HalfIndexTurns, g.HalfIndexTurns })
                    {
                        var root = (new Rational(cycle) + offset - g.PhysicalPhase.Phase) / g.PhysicalPhase.Coefficient;
                        if (root < request.MinimumRoot || root > request.MaximumRoot) continue;
                        AssemblyReplayLimits.CheckFraction(root, AssemblyReplayLimits.InputIntegerCharacters); roots.Add(root);
                        Need(roots.Count <= AssemblyReplayLimits.Samples, "Exact boundary expansion exceeds sample limit.");
                    }
            }
        Need((long)roots.Count * Math.Max(1, analysis.Inventory.Count) <= AssemblyReplayLimits.SampleChannelProduct, "Sample/inventory product limit exceeded.");
        return roots.ToList().AsReadOnly();
    }
    private static BigInteger Floor(Rational r)
    { var quotient = BigInteger.DivRem(r.Numerator, r.Denominator, out var rest); return rest.Sign < 0 ? quotient - 1 : quotient; }
}
