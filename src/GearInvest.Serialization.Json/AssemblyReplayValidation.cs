using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

public static partial class AssemblyReplayJson
{
    // Consumption validation is deliberately not a replacement for the explicit SDK source rebuild.
    private static void ValidateConsumption(JsonElement p, JsonElement original, AssemblyReplayExportRequest request)
    {
        var analysis = p.GetProperty("analysis"); var producer = p.GetProperty("producer");
        Fields(producer, "operation", "mechanicalValidation", "exporterVersion", "packageVersion", "policy", "numericPolicy", "toothSolidValidation");
        Need(Text(producer,"exporterVersion") == "assembly-web-export/0.1" && Text(producer,"packageVersion").Length > 0, "Unsupported/missing producer version.");
        Need(Text(producer,"operation") == "current-source-finalize-and-evaluate" && Text(producer,"mechanicalValidation") == "Finalized" &&
            Text(producer,"policy") == Text(analysis,"policy") && Text(producer,"numericPolicy") == Text(analysis.GetProperty("numericRequest"),"policy") &&
            Text(producer,"toothSolidValidation") == "notPerformed", "Invalid stored producer assertion.");
        var inventory = analysis.GetProperty("inventory").EnumerateArray().ToDictionary(x => ReferenceKey(x.GetProperty("reference")), StringComparer.Ordinal);
        var memberIds = p.GetProperty("definition").GetProperty("members").EnumerateArray().Select(x => Text(x,"instanceId")).ToHashSet(StringComparer.Ordinal);
        var shafts = p.GetProperty("shafts").EnumerateArray().ToDictionary(x => ReferenceKey(x.GetProperty("reference")), StringComparer.Ordinal);
        var originalShafts = original.GetProperty("referenceEvaluation").GetProperty("shafts").EnumerateArray().ToArray();
        Need(originalShafts.Length == shafts.Count, "Affine source shaft coverage mismatch.");
        foreach (var shaft in originalShafts)
        {
            Need(shafts.TryGetValue(ReferenceKey(shaft.GetProperty("reference")), out var channel), "Source shaft missing.");
            Fields(channel, "reference", "mode", "unit", "q", "p", "fixedFrameMm");
            Need(Text(channel,"unit") == "turn", "Unsupported shaft unit.");
            Need(Fraction(channel.GetProperty("q")) == Fraction(shaft.GetProperty("relation").GetProperty("coefficient")) &&
                Fraction(channel.GetProperty("p")) == Fraction(shaft.GetProperty("relation").GetProperty("phase")), "Source/channel relation mismatch.");
            ReplayFrame(channel.GetProperty("fixedFrameMm"));
            foreach (var key in new[] { "origin", "x", "y", "z" })
            {
                var projected = channel.GetProperty("fixedFrameMm").GetProperty(key).EnumerateArray().Select(x => Fraction(x)).ToArray();
                var source = shaft.GetProperty("fixedFrameMm").GetProperty(key);
                Need(projected.SequenceEqual(new[] { Fraction(source.GetProperty("x")), Fraction(source.GetProperty("y")), Fraction(source.GetProperty("z")) }), "Source/channel frame mismatch.");
            }
        }
        foreach (var body in p.GetProperty("bodies").EnumerateArray())
        {
            Fields(body, "reference", "mountedShaft", "mode", "q", "p", "fixedFrameMm", "positiveAxis", "specification");
            var item = inventory[ReferenceKey(body.GetProperty("reference"))];
            var mount = body.GetProperty("mountedShaft"); var originalMount = item.GetProperty("mountedShaft");
            Need(mount.ValueKind == JsonValueKind.Null ? originalMount.ValueKind == JsonValueKind.Null : originalMount.ValueKind != JsonValueKind.Null && ReferenceKey(mount) == ReferenceKey(originalMount), "Body mounting ownership differs.");
            if (Text(body,"mode") == "exactAffine") { ReplayFrame(body.GetProperty("fixedFrameMm")); ReplayVector(body.GetProperty("positiveAxis"), true); }
            else
            {
                Need(body.GetProperty("q").ValueKind == JsonValueKind.Null && body.GetProperty("p").ValueKind == JsonValueKind.Null, "Sampled body cannot claim exact affine law.");
                if (body.GetProperty("fixedFrameMm").ValueKind != JsonValueKind.Null) ReplayFrame(body.GetProperty("fixedFrameMm"));
                ReplayVector(body.GetProperty("positiveAxis"), false);
            }
            var spec = body.GetProperty("specification"); Fields(spec,"family","teeth","pitchRadiusMm","moduleMm","missingSpecification","toothSolidValidation");
            Need(Text(spec,"family").Length > 0 && Text(spec,"family").Length <= AssemblyReplayLimits.IdCharacters &&
                Text(spec,"missingSpecification") == "notSpecified" && Text(spec,"toothSolidValidation") == "notPerformed", "Unsupported body specification/proof.");
            Need(spec.GetProperty("teeth").ValueKind == JsonValueKind.Null || spec.GetProperty("teeth").GetInt32() > 0, "Positive teeth required.");
            foreach (var k in new[] { "pitchRadiusMm", "moduleMm" }) if (spec.GetProperty(k).ValueKind != JsonValueKind.Null) Need(Fraction(spec.GetProperty(k)) > Rational.Zero, "Positive dimension required.");
        }
        var vertices = 0;
        void Features(JsonElement array, bool isStatic)
        {
            Need(array.GetArrayLength() <= AssemblyReplayLimits.Features, "Display feature limit exceeded."); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in array.EnumerateArray())
            {
                Fields(f,"memberId","localId","mechanicalReference","scope","role","closed","pointsMm");
                var member = Text(f,"memberId"); var id = Text(f,"localId"); var reference = f.GetProperty("mechanicalReference");
                Need(memberIds.Contains(member) && id.Length > 0 && id.Length <= AssemblyReplayLimits.IdCharacters && ids.Add(member.Length + ":" + member + id) &&
                    inventory.ContainsKey(ReferenceKey(reference)) && Text(reference,"memberId") == member && Text(f,"scope") == "displayApproximation" &&
                    (Text(f,"role") == "fixedRoute") == isStatic, "Duplicate/stale/invalid display feature.");
                _ = f.GetProperty("closed").GetBoolean();
                foreach (var point in f.GetProperty("pointsMm").EnumerateArray())
                {
                    Need(++vertices <= AssemblyReplayLimits.GeometryVertices && point.GetArrayLength() == 3, "Display vertex limit/shape invalid.");
                    foreach (var n in point.EnumerateArray()) Need(Math.Abs(n.GetDouble()) <= AssemblyReplayLimits.DisplayCoordinateMm, "Display coordinate bound exceeded.");
                }
            }
        }
        Features(p.GetProperty("staticFeatures"), true);
        foreach (var sample in p.GetProperty("samples").EnumerateArray())
        {
            Fields(sample,"root","status","exactBoundaries","originalEvaluationSha256","observation","bodyFrames","displayFeatures");
            var root = Fraction(sample.GetProperty("root")); var obs = sample.GetProperty("observation");
            Need(Text(obs,"format") == "gear-invest.assembly-replay-observation" && Text(obs,"formatVersion") == Version &&
                Text(obs.GetProperty("rootInput"),"unit") == "turn" && Text(obs.GetProperty("rootInput"),"kind") == "AngularPosition", "Observation format/units invalid.");
            Need(Text(sample,"status") == (obs.GetProperty("allRequestedNumericAvailable").GetBoolean() && obs.GetProperty("diagnostics").GetArrayLength() == 0 ? "storedAvailable" : "storedPartial"), "Stored sample status contradiction.");
            var members = obs.GetProperty("members").EnumerateArray().ToArray();
            Need(members.Length == memberIds.Count && members.Select(m => Text(m,"instanceId")).Distinct(StringComparer.Ordinal).Count() == memberIds.Count &&
                members.All(m => memberIds.Contains(Text(m,"instanceId"))), "Sample member coverage invalid.");
            var sampleShafts = obs.GetProperty("shafts").EnumerateArray().ToArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
            Need(sampleShafts.Length == shafts.Count, "Sample shaft coverage invalid.");
            foreach (var shaft in sampleShafts)
            {
                Fields(shaft,"reference","turns"); var key = ReferenceKey(shaft.GetProperty("reference"));
                Need(seen.Add(key) && shafts.TryGetValue(key, out _), "Duplicate/unknown sample shaft."); var law = shafts[key];
                Need(Fraction(shaft.GetProperty("turns").GetProperty("value")) == Fraction(law.GetProperty("q")) * root + Fraction(law.GetProperty("p")), "Sample shaft contradicts resolved exact law.");
            }
            foreach (var body in sample.GetProperty("bodyFrames").EnumerateArray())
            {
                Fields(body,"reference","status","reason","matrixMm");
                if (Text(body,"status") == "displayApproximation") { Need(body.GetProperty("reason").ValueKind == JsonValueKind.Null,"Available pose has failure reason."); ReplayMatrix(body.GetProperty("matrixMm")); }
                else Need(Text(body,"status") == "unavailable" && body.GetProperty("matrixMm").ValueKind == JsonValueKind.Null && Text(body,"reason").Length > 0,"Invalid unavailable pose.");
            }
            var boundaryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in sample.GetProperty("exactBoundaries").EnumerateArray())
            {
                Fields(b,"memberId","regime","recipeId"); var id = Text(b,"memberId");
                Need(memberIds.Contains(id) && boundaryIds.Add(id), "Duplicate/unknown exact boundary member.");
                var recipe = members.Single(m => Text(m,"instanceId") == id).GetProperty("recipe");
                Need(Text(b,"regime") is "LowerPhaseBoundary" or "UpperPhaseBoundary" && Text(b,"regime") == Text(recipe,"regime") && Text(b,"recipeId") == Text(recipe,"recipeId"), "Unproved boundary label.");
            }
            Features(sample.GetProperty("displayFeatures"), false);
        }
    }
    private static ExactVector3 ReplayVector(JsonElement v, bool cardinal)
    {
        Need(v.GetArrayLength() == 3,"Three coordinates required."); var a = v.EnumerateArray().Select(x => Fraction(x, AssemblyReplayLimits.InputIntegerCharacters)).ToArray();
        if (cardinal) Need(a.Count(x => x != Rational.Zero) == 1 && a.All(x => x == Rational.Zero || x == Rational.One || x == -Rational.One),"Cardinal axis required.");
        return new(a[0],a[1],a[2]);
    }
    private static void ReplayFrame(JsonElement f)
    {
        Fields(f,"origin","x","y","z"); ReplayVector(f.GetProperty("origin"),false);
        var x = ReplayVector(f.GetProperty("x"),true); var y = ReplayVector(f.GetProperty("y"),true); var z = ReplayVector(f.GetProperty("z"),true);
        Need(x.Cross(y) == z,"Proper right-handed frame required.");
    }
    private static void ReplayMatrix(JsonElement v)
    {
        Need(v.GetArrayLength() == 16,"Body matrix requires 16 values."); var m = v.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Need(m.All(n => !double.IsNaN(n) && !double.IsInfinity(n) && Math.Abs(n) <= AssemblyReplayLimits.DisplayCoordinateMm) && m[3] == 0 && m[7] == 0 && m[11] == 0 && m[15] == 1,"Invalid affine matrix.");
        for (var i=0;i<3;i++) for(var j=0;j<3;j++) Need(Math.Abs(Enumerable.Range(0,3).Sum(k=>m[4*i+k]*m[4*j+k]) - (i==j?1:0)) <= 1e-10,"Body matrix must be rigid.");
        Need(Math.Abs(m[0]*(m[5]*m[10]-m[9]*m[6])-m[4]*(m[1]*m[10]-m[9]*m[2])+m[8]*(m[1]*m[6]-m[5]*m[2])-1) <= 1e-10,"Reflected body matrix.");
    }
}
