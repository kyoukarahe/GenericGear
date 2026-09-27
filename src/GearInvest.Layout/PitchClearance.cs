using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GearInvest.Core;

namespace GearInvest.Layout;

public static class PitchClearancePolicy
{
    public const string Legacy = "exact-aabb-only-v1";
    public const string Refined = "exact-closed-pitch-zero-margin-v1";
    public const string Shapes = "filled-zero-thickness-closed-disk/closed-aabb/finite-lateral-cone-no-caps-v1";
    public const int MaxInputDigits = 128;
    public const int MaxProofDigits = 8192;
    public const int MaxPairs = 1105; // 34 choose 2 + 34 * 16; intended contacts are excluded.
}

public enum PitchShapeKind { ClosedDisk, ClosedAabb, FiniteLateralCone }
public enum PitchPairRelation { ProvenSeparated, ProvenNonEmptyIntersection, Inconclusive }
public enum PitchPairScope { Primitive, BodyPair, WorldKeepOut }
public enum PitchBroadRelation { StrictlySeparated, OverlappingClosedEnvelopes }

/// <summary>Immutable exact inspection shape, not a tooth solid or display mesh. Disk normal sign is set-invariant.</summary>
public sealed class PitchShape
{
    private PitchShape(string id, PitchShapeKind kind, ExactVector3 center, ExactVector3 direction, Rational radius,
        Rational height, Rational innerParameter, ExactEnvelope3 box)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256) throw new ArgumentException("Bounded stable shape ID required.");
        var numbers = new[] { center.X, center.Y, center.Z, direction.X, direction.Y, direction.Z, radius, height, innerParameter,
            box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z };
        if (numbers.Any(v => v.Numerator.ToString(CultureInfo.InvariantCulture).Length > PitchClearancePolicy.MaxInputDigits ||
            v.Denominator.ToString(CultureInfo.InvariantCulture).Length > PitchClearancePolicy.MaxInputDigits))
            throw new ArgumentException("Pitch input digit resource bound exceeded.");
        if (kind == PitchShapeKind.ClosedAabb ? !box.IsOrdered : direction.LengthSquared != 1 || radius <= 0 ||
            kind == PitchShapeKind.FiniteLateralCone && (height <= 0 || innerParameter <= 0 || innerParameter >= 1))
            throw new ArgumentException("Invalid pitch geometry; unit direction, positive radius/height and ordered bounds required.");
        Id = id; Kind = kind; Center = center; Direction = kind == PitchShapeKind.ClosedDisk ? Positive(direction) : direction;
        Radius = radius; Height = height; InnerParameter = innerParameter; Box = box;
    }
    public static PitchShape Disk(string id, ExactVector3 center, ExactVector3 normal, Rational radius) => new(id, PitchShapeKind.ClosedDisk, center, normal, radius, 0, 0, default);
    public static PitchShape Aabb(string id, ExactEnvelope3 bounds) => new(id, PitchShapeKind.ClosedAabb, default, default, 0, 0, 0, bounds);
    public static PitchShape LateralCone(string id, ExactVector3 apex, ExactVector3 outward, Rational height, Rational radius, Rational innerParameter) =>
        new(id, PitchShapeKind.FiniteLateralCone, apex, outward, radius, height, innerParameter, default);
    public string Id { get; }
    public PitchShapeKind Kind { get; }
    /// <summary>Disk center or cone apex. Cone direction is physical outward, never shaft-coordinate sign.</summary>
    public ExactVector3 Center { get; }
    public ExactVector3 Direction { get; }
    public Rational Radius { get; }
    public Rational Height { get; }
    public Rational InnerParameter { get; }
    public ExactEnvelope3 Box { get; }
    public string GeometryDigest => PitchProofKeys.Hash(PitchProofKeys.Pack(PitchClearancePolicy.Shapes, Kind.ToString(),
        PitchProofKeys.Vector(Center), PitchProofKeys.Vector(Direction), Radius.ToString(), Height.ToString(), InnerParameter.ToString(),
        PitchProofKeys.Vector(Box.Min), PitchProofKeys.Vector(Box.Max)));
    public ExactEnvelope3 Envelope
    {
        get
        {
            if (Kind == PitchShapeKind.ClosedAabb) return Box;
            // Cardinal envelope exactly matches ExactEnvelope3.Of. Oblique inputs retain a conservative radius cube.
            var extent = Direction.IsCardinal ? new ExactVector3(Direction.X == 0 ? Radius : 0, Direction.Y == 0 ? Radius : 0, Direction.Z == 0 ? Radius : 0) : new ExactVector3(Radius, Radius, Radius);
            if (Kind == PitchShapeKind.ClosedDisk) return new ExactEnvelope3(Center - extent, Center + extent);
            var outer = Center + Direction * Height; var inner = Center + Direction * (Height * InnerParameter);
            var lo = outer - extent; var hi = outer + extent; var ilo = inner - extent * InnerParameter; var ihi = inner + extent * InnerParameter;
            return new ExactEnvelope3(new ExactVector3(Min(lo.X, ilo.X), Min(lo.Y, ilo.Y), Min(lo.Z, ilo.Z)),
                new ExactVector3(Max(hi.X, ihi.X), Max(hi.Y, ihi.Y), Max(hi.Z, ihi.Z)));
        }
    }
    internal static ExactVector3 Positive(ExactVector3 v) => (v.X != 0 ? v.X : v.Y != 0 ? v.Y : v.Z) < 0 ? -v : v;
    internal static Rational Min(Rational a, Rational b) => a < b ? a : b;
    internal static Rational Max(Rational a, Rational b) => a > b ? a : b;
}

/// <summary>Verifiable decision data. A boundary equality is not a claim that the complete intersection is a singleton.</summary>
public sealed class PitchPairProof
{
    public PitchPairProof(PitchShape a, PitchShape b, string policy, string shapeSemantics, string pairId, string geometryA, string geometryB,
        PitchPairScope scope, bool required, PitchBroadRelation broad, string method, PitchPairRelation relation,
        IEnumerable<KeyValuePair<string, Rational>> terms, bool boundaryEquality, ExactVector3? witness = null)
    {
        A = a; B = b; Policy = policy; ShapeSemantics = shapeSemantics; PairId = pairId; GeometryA = geometryA; GeometryB = geometryB;
        Scope = scope; Required = required; Broad = broad; Method = method; Relation = relation;
        var values = terms.Take(65).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        if (values.Length > 64 || values.Select(v => v.Key).Distinct(StringComparer.Ordinal).Count() != values.Length || values.Any(v => string.IsNullOrEmpty(v.Key) || v.Key.Length > 64))
            throw new ArgumentException("Bounded unique proof terms required.");
        Terms = System.Array.AsReadOnly(values); BoundaryEquality = boundaryEquality; Witness = witness;
    }
    public PitchShape A { get; }
    public PitchShape B { get; }
    public string Policy { get; }
    public string ShapeSemantics { get; }
    public string PairId { get; }
    public string GeometryA { get; }
    public string GeometryB { get; }
    public PitchPairScope Scope { get; }
    public bool Required { get; }
    public PitchBroadRelation Broad { get; }
    public string Method { get; }
    public PitchPairRelation Relation { get; }
    public ReadOnlyCollection<KeyValuePair<string, Rational>> Terms { get; }
    public bool BoundaryEquality { get; }
    public ExactVector3? Witness { get; }
    public OrientedCheckVerdict Verdict => Relation == PitchPairRelation.ProvenSeparated ? OrientedCheckVerdict.Pass : Relation == PitchPairRelation.ProvenNonEmptyIntersection ? OrientedCheckVerdict.Fail : OrientedCheckVerdict.Inconclusive;
    public string Digest => PitchProofKeys.Hash(PitchProofKeys.Pack(Policy, ShapeSemantics, PairId, A.Id, A.GeometryDigest, B.Id, B.GeometryDigest,
        GeometryA, GeometryB, Scope.ToString(), Required ? "required" : "optional", Broad.ToString(), Method, Relation.ToString(),
        PitchProofKeys.Pack(Terms.Select(t => PitchProofKeys.Pack(t.Key, t.Value.ToString())).ToArray()), BoundaryEquality ? "equality" : "strict",
        Witness.HasValue ? PitchProofKeys.Vector(Witness.Value) : "none"));
}

public static class PitchProofKeys
{
    public static string Pack(params string[] values) => string.Concat(values.Select(s => s.Length.ToString(CultureInfo.InvariantCulture) + ":" + s));
    public static string Vector(ExactVector3 v) => Pack(v.X.ToString(), v.Y.ToString(), v.Z.ToString());
    public static string Hash(string value) { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))); }
    public static string CoverageDigest(IEnumerable<PitchPairProof> proofs) => Hash(Pack(proofs.OrderBy(p => p.PairId, StringComparer.Ordinal).Select(p => p.Digest).ToArray()));
}

/// <summary>Exact zero-margin decision predicates. Unsupported overlapping shapes are never declared safe.</summary>
public static class PitchClearanceClassifier
{
    public static PitchPairProof Classify(PitchShape a, PitchShape b, bool required = true, PitchPairScope scope = PitchPairScope.Primitive, Rational minimumClearance = default)
    {
        if (a is null || b is null) throw new ArgumentNullException(a is null ? nameof(a) : nameof(b));
        if (minimumClearance != 0) throw new NotSupportedException("Only zero-margin strict nonintersection is supported; a requested margin is never discarded.");
        if (a.Id == b.Id || !Enum.IsDefined(typeof(PitchPairScope), scope)) throw new ArgumentException("Distinct shape references and a known scope required.");
        if (StringComparer.Ordinal.Compare(a.Id, b.Id) > 0) { var swap = a; a = b; b = swap; }
        var terms = new SortedDictionary<string, Rational>(StringComparer.Ordinal); var broad = a.Envelope.Disjoint(b.Envelope) ? PitchBroadRelation.StrictlySeparated : PitchBroadRelation.OverlappingClosedEnvelopes;
        PitchPairProof Done(string method, PitchPairRelation relation, bool equality = false, ExactVector3? witness = null) => new(a, b,
            PitchClearancePolicy.Refined, PitchClearancePolicy.Shapes, PitchProofKeys.Hash(PitchProofKeys.Pack(a.Id, b.Id)), a.GeometryDigest, b.GeometryDigest,
            scope, required, broad, method, relation, terms, equality, witness);
        const PitchPairRelation separate = PitchPairRelation.ProvenSeparated, intersect = PitchPairRelation.ProvenNonEmptyIntersection;
        if (broad == PitchBroadRelation.StrictlySeparated) return Done("exact-aabb-strict-separation-v1", separate);
        var disk = a.Kind == PitchShapeKind.ClosedDisk ? a : b.Kind == PitchShapeKind.ClosedDisk ? b : null;
        var other = ReferenceEquals(disk, a) ? b : a;
        if (disk is null || !disk.Direction.IsCardinal || other.Kind != PitchShapeKind.ClosedAabb && !other.Direction.IsCardinal)
            return Done("unsupported-overlap-v1", PitchPairRelation.Inconclusive);
        if (other.Kind == PitchShapeKind.ClosedDisk)
        {
            var n = disk.Direction; var m = other.Direction; var delta = other.Center - disk.Center;
            if (n.Cross(m) == ExactVector3.Zero)
            {
                var offset = n.Dot(delta); terms["planeOffset"] = offset;
                if (offset != 0) return Done("parallel-distinct-disk-planes-v1", separate);
                var distance = delta.LengthSquared; var sum = disk.Radius + other.Radius;
                terms["centerDistanceSquared"] = distance; terms["radiusSumSquared"] = sum * sum;
                ExactVector3? witness = distance <= other.Radius * other.Radius ? disk.Center : distance <= disk.Radius * disk.Radius ? other.Center : null;
                if (distance == sum * sum) witness = disk.Center + delta * (disk.Radius / sum);
                return Done("coplanar-filled-disks-v1", distance > sum * sum ? separate : intersect, distance == sum * sum, witness);
            }
            var u = PitchShape.Positive(n.Cross(m)); var p = n * n.Dot(disk.Center) + m * m.Dot(other.Center);
            var t1 = u.Dot(disk.Center - p); var t2 = u.Dot(other.Center - p);
            var A = disk.Radius * disk.Radius - (disk.Center - p - u * t1).LengthSquared;
            var B = other.Radius * other.Radius - (other.Center - p - u * t2).LengthSquared;
            AddVector(terms, "linePoint", p); AddVector(terms, "lineDirection", u); terms["t1"] = t1; terms["t2"] = t2; terms["A"] = A; terms["B"] = B;
            if (A < 0 || B < 0) return Done("perpendicular-disk-line-intervals-v1", separate);
            var D = (t1 - t2) * (t1 - t2); var K = D - A - B; terms["D"] = D; terms["AplusB"] = A + B; terms["K"] = K;
            var overlaps = D <= A + B; var equality = false;
            if (!overlaps) { terms["Ksquared"] = K * K; terms["fourAB"] = 4 * A * B; overlaps = K * K <= 4 * A * B; equality = K * K == 4 * A * B; }
            else equality = D == A + B && (A == 0 || B == 0);
            ExactVector3? point = null;
            foreach (var t in new[] { t1, t2, (t1 + t2) / 2 })
                if ((t - t1) * (t - t1) <= A && (t - t2) * (t - t2) <= B) { point = p + u * t; break; }
            return Done("perpendicular-disk-line-intervals-v1", overlaps ? intersect : separate, equality, point);
        }
        if (other.Kind == PitchShapeKind.ClosedAabb)
        {
            var box = other.Box; var axis = disk.Direction.X != 0 ? 0 : disk.Direction.Y != 0 ? 1 : 2;
            var c = Components(disk.Center); var lo = Components(box.Min); var hi = Components(box.Max);
            terms["planeCoordinate"] = c[axis]; terms["planeIntervalMin"] = lo[axis]; terms["planeIntervalMax"] = hi[axis];
            if (c[axis] < lo[axis] || c[axis] > hi[axis]) return Done("cardinal-disk-closed-box-v1", separate);
            var closest = new ExactVector3(Clamp(c[0], lo[0], hi[0]), Clamp(c[1], lo[1], hi[1]), Clamp(c[2], lo[2], hi[2]));
            var d2 = (disk.Center - closest).LengthSquared; var r2 = disk.Radius * disk.Radius;
            AddVector(terms, "closestPoint", closest); terms["closestDistanceSquared"] = d2; terms["radiusSquared"] = r2;
            return Done("cardinal-disk-closed-box-v1", d2 > r2 ? separate : intersect, d2 == r2, d2 <= r2 ? closest : null);
        }
        if (disk.Direction.Cross(other.Direction) != ExactVector3.Zero) return Done("unsupported-overlap-v1", PitchPairRelation.Inconclusive);
        var station = other.Direction.Dot(disk.Center - other.Center); var parameter = station / other.Height;
        terms["axialStation"] = station; terms["sectionParameter"] = parameter; terms["innerParameter"] = other.InnerParameter;
        if (parameter < other.InnerParameter || parameter > 1) return Done("parallel-disk-lateral-cone-section-v1", separate);
        var center = other.Center + other.Direction * station; var rho = parameter * other.Radius; var r = disk.Radius;
        var dSquared = (disk.Center - center).LengthSquared; var sumSquared = (rho + r) * (rho + r); var differenceSquared = (rho - r) * (rho - r);
        AddVector(terms, "sectionCenter", center); terms["ringRadius"] = rho; terms["diskRadius"] = r; terms["centerDistanceSquared"] = dSquared;
        terms["radiusSumSquared"] = sumSquared; terms["radiusDifferenceSquared"] = differenceSquared; terms["ringMinusDiskRadius"] = rho - r;
        var separated = dSquared > sumSquared || rho > r && dSquared < differenceSquared;
        ExactVector3? ringPoint = null;
        if (dSquared == 0 && r >= rho) ringPoint = center + (other.Direction.X == 0 ? ExactVector3.UnitX : ExactVector3.UnitY) * rho;
        else if (dSquared == sumSquared) ringPoint = center + (disk.Center - center) * (rho / (rho + r));
        return Done("parallel-disk-lateral-cone-section-v1", separated ? separate : intersect,
            dSquared == sumSquared || rho >= r && dSquared == differenceSquared, ringPoint);
    }

    /// <summary>Recompute from independently supplied original shapes/context, including witness, terms and coverage binding.</summary>
    public static bool Verify(PitchPairProof proof, PitchShape originalA, PitchShape originalB, bool required = true, PitchPairScope scope = PitchPairScope.Primitive) =>
        proof is not null && proof.Digest == Classify(originalA, originalB, required, scope).Digest;
    private static Rational[] Components(ExactVector3 v) => new[] { v.X, v.Y, v.Z };
    private static Rational Clamp(Rational v, Rational lo, Rational hi) => v < lo ? lo : v > hi ? hi : v;
    private static void AddVector(IDictionary<string, Rational> terms, string prefix, ExactVector3 p) { terms[prefix + "X"] = p.X; terms[prefix + "Y"] = p.Y; terms[prefix + "Z"] = p.Z; }
}
