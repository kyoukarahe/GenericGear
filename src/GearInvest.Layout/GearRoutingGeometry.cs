using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Layout;

/// <summary>Exact, full-angle pitch-disk envelope queries. No tooth-tip, bearing or contact-dynamics claim.</summary>
public static class GearRoutingGeometry
{
    public static BigInteger DistanceSquared(GearRoutePoint a, GearRoutePoint b)
    { var x = a.X - b.X; var y = a.Y - b.Y; return x * x + y * y; }
    public static BigInteger DistanceSquared(GearRoutePoint p, GearRouteBox b)
    {
        var x = p.X < b.MinX ? b.MinX - p.X : p.X > b.MaxX ? p.X - b.MaxX : BigInteger.Zero;
        var y = p.Y < b.MinY ? b.MinY - p.Y : p.Y > b.MaxY ? p.Y - b.MaxY : BigInteger.Zero;
        return x * x + y * y;
    }
    public static bool Contains(GearRouteBox b, GearRoutePoint p) => p.X >= b.MinX && p.X <= b.MaxX && p.Y >= b.MinY && p.Y <= b.MaxY;
    public static bool ContainsDisk(GearRouteBox b, GearRoutePoint p, BigInteger radius) =>
        p.X - radius >= b.MinX && p.X + radius <= b.MaxX && p.Y - radius >= b.MinY && p.Y + radius <= b.MaxY;
    public static bool ClearsKeepOut(GearRoutePoint p, BigInteger radius, GearRouteBox box, BigInteger clearance)
    { var required = radius + clearance; return DistanceSquared(p, box) > required * required; }
    public static bool UnrelatedClear(GearRoutePoint a, BigInteger ra, GearRoutePoint b, BigInteger rb, BigInteger clearance)
    { var squared = DistanceSquared(a, b); var sum = ra + rb; var required = sum + clearance; return squared > sum * sum && squared >= required * required; }
    public static bool Meshes(GearRoutePoint a, BigInteger ra, GearRoutePoint b, BigInteger rb)
    { var sum = ra + rb; return !a.Equals(b) && DistanceSquared(a, b) == sum * sum; }
}
