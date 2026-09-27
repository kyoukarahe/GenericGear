using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

// Consumer representation of already evaluated mechanics. No kinematic solve, contact test or renderer types.
internal static partial class AssemblyReplayProjection
{
    internal sealed class Body
    {
        internal Body(AssemblyInventoryEntry item, ExactAffineRelation? relation, OrientedFrame? frame, ExactVector3 axis,
            string family, int? teeth = null, Rational? radius = null, Rational? module = null)
        { Item = item; Relation = relation; Frame = frame; Axis = axis; Family = family; Teeth = teeth; Radius = radius; Module = module; }
        internal AssemblyInventoryEntry Item { get; }
        internal ExactAffineRelation? Relation { get; }
        internal OrientedFrame? Frame { get; }
        internal ExactVector3 Axis { get; }
        internal string Family { get; }
        internal int? Teeth { get; }
        internal Rational? Radius { get; }
        internal Rational? Module { get; }
    }

    internal static Body[] Bodies(MechanicalAssemblyArtifact artifact)
    {
        var a = artifact.Analysis; var root = a.Draft.Definition.Root.Definition; var map = a.Draft.Definition.RootMapping!;
        var shafts = artifact.ReferenceEvaluation.Shafts.ToDictionary(x => x.Reference);
        return a.Inventory.Where(x => x.Reference.Kind == AssemblyComponentKind.Body).Select(item =>
        {
            if (item.Reference.Owner == AssemblyOwnerKind.Root)
            {
                var b = root.Bodies.Single(x => x.Id == item.Reference.LocalId); var shaft = shafts[item.MountedShaft!];
                return new Body(item, shaft.Relation, map.FrameMm(b.MountingFrame), shaft.FixedFrameMm.Z,
                    b.Kind.ToString(), b.Teeth, b.OuterPitchRadius * map.MillimetersPerSourceUnit,
                    b.Kind.ToString() == "PlanarSpur" ? 2 * b.OuterPitchRadius * map.MillimetersPerSourceUnit / b.Teeth : (Rational?)null);
            }
            var m = a.Members.Single(x => x.InstanceId == item.Reference.MemberId); var d = m.Member.Declaration;
            var input = m.Binding.FixedInputFrameMm!; var id = item.Reference.LocalId;
            Body Rotor(OrientedFrame f, ExactAffineRelation? law, Rational mount, string family, int? teeth = null, Rational? radius = null, Rational? module = null) =>
                new(item, law.HasValue ? law.Value.Then(1, mount) : null, f, f.Z, family, teeth, radius, module);
            switch (d)
            {
                case AssemblyWormDeclaration w:
                    return id == w.Device.InputWormBodyId
                        ? Rotor(input.At(input.Origin + input.Z * w.Device.InputPitchStation.Value), m.InputRelation, w.Device.InputMountingPhase.Value,
                            "CylindricalWorm", null, w.Device.Worm.PitchRadius.Value, w.Device.Worm.AxialModule.Value)
                        : Rotor(w.Device.OutputShaft.Frame.At(w.Device.OutputPitchCenterMm), m.ShaftRelation, w.Device.OutputMountingPhase.Value,
                            "IdealWormWheel", w.Device.SelectedWheel?.ToothCount, w.Device.SelectedWheel is null ? null : w.Device.SelectedWheel.ToothCount * w.Device.SelectedWheel.TransverseModule.Value / 2,
                            w.Device.SelectedWheel?.TransverseModule.Value);
                case AssemblyOpenBeltDeclaration b:
                    return id == b.Device.InputPulleyBodyId
                        ? Rotor(input.At(b.Device.InputPulleyCenterMm), m.InputRelation, 0, "OpenBeltPulley", radius: b.Device.InputPitchRadius.Value)
                        : Rotor(b.Device.OutputShaft.Frame.At(b.Device.OutputPulleyCenterMm), m.ShaftRelation, 0, "OpenBeltPulley", radius: b.Device.OutputPitchRadius.Value);
                case AssemblyPitchChainDeclaration c:
                    return id == c.Device.InputSprocketBodyId
                        ? Rotor(input.At(input.Origin + input.Z * c.Device.InputSprocketStation.Value), m.InputRelation, c.Device.InputMountingPhase.Value,
                            "PitchChainSprocket", c.Device.InputToothCount)
                        : Rotor(c.Device.OutputShaft.Frame.At(c.Device.OutputSprocketCenterMm), m.ShaftRelation, c.Device.OutputMountingPhase.Value,
                            "PitchChainSprocket", c.Device.OutputToothCount);
                case AssemblyGenevaDeclaration g when id == g.Device.DriverBodyId && m.MotionDescriptor is GenevaMotionDescriptor motion:
                    return Rotor(new(g.Device.DriverCenterMm, g.Device.TransverseDirection.Cross(g.Device.PlaneNormal), g.Device.TransverseDirection, g.Device.PlaneNormal),
                        motion.PhysicalPhase, 0, "GenevaDriver");
                case AssemblyCrankSliderDeclaration c when id == c.Device.CrankBodyId && m.MotionDescriptor is CrankSliderMotionDescriptor motion:
                    return Rotor(new(c.Device.PivotMm, c.Device.GuideFrameMm.Z, c.Device.PlaneNormal.Cross(c.Device.GuideFrameMm.Z), c.Device.PlaneNormal),
                        motion.PhysicalPhaseRelation, 0, "Crank");
                case AssemblyCamFollowerDeclaration c when id == c.Device.CamBodyId && m.MotionDescriptor is CamFollowerMotionDescriptor motion:
                    return Rotor(new(c.Device.CenterMm, c.Device.GuideFrameMm.Z, c.Device.PlaneNormal.Cross(c.Device.GuideFrameMm.Z), c.Device.PlaneNormal),
                        motion.PhysicalPhaseRelation, 0, "ConvexDiskCam");
                default: return new Body(item, null, null, ExactVector3.Zero, item.Role);
            }
        }).ToArray();
    }

    internal static void WriteBody(Utf8JsonWriter w, Body body)
    {
        w.WriteStartObject(); w.WritePropertyName("reference"); MechanicalAssemblyJson.Reference(w, body.Item.Reference);
        w.WritePropertyName("mountedShaft"); if (body.Item.MountedShaft is null) w.WriteNullValue(); else MechanicalAssemblyJson.Reference(w, body.Item.MountedShaft);
        w.WriteString("mode", body.Relation.HasValue ? "exactAffine" : "sampled");
        w.WritePropertyName("q"); if (body.Relation.HasValue) AssemblyReplayJson.Fraction(w, body.Relation.Value.Coefficient); else w.WriteNullValue();
        w.WritePropertyName("p"); if (body.Relation.HasValue) AssemblyReplayJson.Fraction(w, body.Relation.Value.Phase); else w.WriteNullValue();
        w.WritePropertyName("fixedFrameMm"); if (body.Frame is null) w.WriteNullValue(); else Frame(w, body.Frame);
        w.WritePropertyName("positiveAxis"); Vector(w, body.Axis);
        w.WriteStartObject("specification"); w.WriteString("family", body.Family);
        if (body.Teeth.HasValue) w.WriteNumber("teeth", body.Teeth.Value); else w.WriteNull("teeth");
        Optional(w, "pitchRadiusMm", body.Radius); Optional(w, "moduleMm", body.Module);
        w.WriteString("missingSpecification", "notSpecified"); w.WriteString("toothSolidValidation", "notPerformed");
        w.WriteEndObject(); w.WriteEndObject();
    }
    private static void Optional(Utf8JsonWriter w, string key, Rational? value)
    { w.WritePropertyName(key); if (value.HasValue) AssemblyReplayJson.Fraction(w, value.Value); else w.WriteNullValue(); }
    internal static void Frame(Utf8JsonWriter w, OrientedFrame f)
    {
        w.WriteStartObject(); w.WritePropertyName("origin"); Vector(w, f.Origin); w.WritePropertyName("x"); Vector(w, f.X);
        w.WritePropertyName("y"); Vector(w, f.Y); w.WritePropertyName("z"); Vector(w, f.Z); w.WriteEndObject();
    }
    internal static void Vector(Utf8JsonWriter w, ExactVector3 v)
    { w.WriteStartArray(); AssemblyReplayJson.Fraction(w, v.X); AssemblyReplayJson.Fraction(w, v.Y); AssemblyReplayJson.Fraction(w, v.Z); w.WriteEndArray(); }
    internal static double Number(Rational r)
    {
        var value = (double)r.Numerator / (double)r.Denominator;
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > AssemblyReplayLimits.DisplayCoordinateMm || (!r.IsZero && value == 0))
            throw new ArithmeticException("Display numeric conversion unavailable.");
        return value;
    }
    private static double Reduced(Rational r)
    {
        var n = r.Numerator % r.Denominator; if (n.Sign < 0) n += r.Denominator;
        return Number(new Rational(n, r.Denominator));
    }
    private static double[] Point(ExactVector3 v) => new[] { Number(v.X), Number(v.Y), Number(v.Z) };
    private static double[] Point(GenevaVectorInterval v) => new[] { Number((v.X.Lower + v.X.Upper) / 2), Number((v.Y.Lower + v.Y.Upper) / 2), Number((v.Z.Lower + v.Z.Upper) / 2) };
    private static double[] Point(CrankSliderVectorInterval v) => new[] { Number((v.X.Lower + v.X.Upper) / 2), Number((v.Y.Lower + v.Y.Upper) / 2), Number((v.Z.Lower + v.Z.Upper) / 2) };
    private static double[] Point(CamVectorInterval v) => new[] { Number((v.X.Lower + v.X.Upper) / 2), Number((v.Y.Lower + v.Y.Upper) / 2), Number((v.Z.Lower + v.Z.Upper) / 2) };
    private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    private static double[] Unit(double[] v)
    { var n = Math.Sqrt(v.Sum(x => x * x)); if (!(n > 1e-12) || double.IsInfinity(n)) throw new ArithmeticException("Display direction unavailable."); return v.Select(x => x / n).ToArray(); }
    private static double[] Matrix(double[] origin, double[] x, double[] y, double[] z)
    {
        // Optional display orthonormalization of interval midpoints, not interval proof.
        z = Unit(z); x = Unit(Cross(y, z)); y = Unit(Cross(z, x));
        var result = new[] { x[0], x[1], x[2], 0, y[0], y[1], y[2], 0, z[0], z[1], z[2], 0, origin[0], origin[1], origin[2], 1 };
        if (result.Any(x => double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > AssemblyReplayLimits.DisplayCoordinateMm)) throw new ArithmeticException("Display matrix exceeds bounds.");
        return result;
    }
    private static double[] Rotor(OrientedFrame f, ExactVector3 axis, double turns)
    {
        if (!double.IsFinite(turns) || Math.Abs(turns) > 1) throw new ArithmeticException("Only reduced bounded display turns accepted.");
        var z = Point(axis); var c = Math.Cos(turns * 2 * Math.PI); var s = Math.Sin(turns * 2 * Math.PI);
        double[] Rotate(ExactVector3 e)
        { var v = Point(e); var cross = Cross(z, v); var dot = z.Zip(v, (a, b) => a * b).Sum(); return Enumerable.Range(0, 3).Select(i => v[i] * c + cross[i] * s + z[i] * dot * (1 - c)).ToArray(); }
        return Matrix(Point(f.Origin), Rotate(f.X), Rotate(f.Y), Rotate(f.Z));
    }
    private static double[]? BodyMatrix(Body body, MechanicalAssemblyEvaluation e)
    {
        if (body.Relation.HasValue) return Rotor(body.Frame!, body.Axis, Reduced(body.Relation.Value.Evaluate(e.RootInput.Value)));
        var m = e.Analysis.Members.Single(x => x.InstanceId == body.Item.Reference.MemberId);
        var current = e.Members.SingleOrDefault(x => x.InstanceId == m.InstanceId);
        if (current is null || !current.NumericAvailable) return null;
        var id = body.Item.Reference.LocalId;
        if (current.Display is AssemblyGenevaSuffixDisplay suffix && body.Frame is not null)
        {
            var isInput = m.Member.Declaration is AssemblyWormDeclaration w ? id == w.Device.InputWormBodyId : id == ((AssemblyOpenBeltDeclaration)m.Member.Declaration).Device.InputPulleyBodyId;
            return Rotor(body.Frame, body.Axis, isInput ? suffix.InputMaterialTurnsModuloOne : suffix.ShaftMaterialTurnsModuloOne);
        }
        if (m.Member.Declaration is AssemblyGenevaDeclaration g && current.Numeric is GenevaNumericComputation gn && gn.Pose is not null)
            return Matrix(Point(g.Device.WheelCenterMm), Point(gn.Pose.WheelE), Point(gn.Pose.WheelF), Point(g.Device.PlaneNormal));
        if (m.Member.Declaration is AssemblyCrankSliderDeclaration c && current.Numeric is CrankSliderNumericComputation cn && cn.Pose is not null)
        {
            if (id == c.Device.RodBodyId) return Matrix(Point(cn.Pose.RodMidpointMm), Point(cn.Pose.RodDirection), Point(cn.Pose.RodTransverseDirection), Point(c.Device.PlaneNormal));
            return Matrix(Point(cn.Pose.SliderPinMm), Point(c.Device.GuideFrameMm.X), Point(c.Device.GuideFrameMm.Y), Point(c.Device.GuideFrameMm.Z));
        }
        if (m.Member.Declaration is AssemblyCamFollowerDeclaration cam && current.Recipe is CamFollowerPoseRecipe recipe)
            return Matrix(Point(recipe.FollowerReferencePointMm), Point(cam.Device.GuideFrameMm.X), Point(cam.Device.GuideFrameMm.Y), Point(cam.Device.GuideFrameMm.Z));
        return null;
    }
    internal static void WriteSampleBodies(Utf8JsonWriter w, Body[] bodies, MechanicalAssemblyEvaluation e)
    {
        w.WriteStartArray();
        foreach (var body in bodies)
        {
            double[]? matrix = null; string? reason = null;
            try { matrix = BodyMatrix(body, e); if (matrix is null) reason = "No current numeric/display body pose."; }
            catch (ArithmeticException ex) { reason = ex.Message; }
            w.WriteStartObject(); w.WritePropertyName("reference"); MechanicalAssemblyJson.Reference(w, body.Item.Reference);
            w.WriteString("status", matrix is null ? "unavailable" : "displayApproximation"); w.WriteString("reason", reason);
            w.WritePropertyName("matrixMm"); if (matrix is null) w.WriteNullValue(); else Doubles(w, matrix); w.WriteEndObject();
        }
        w.WriteEndArray();
    }
    private static void Doubles(Utf8JsonWriter w, IEnumerable<double> values)
    {
        w.WriteStartArray();
        foreach (var v in values)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > AssemblyReplayLimits.DisplayCoordinateMm)
                throw new ArithmeticException("Display point bound exceeded.");
            // Default shortest double spelling differs between CoreCLR and Unity.
            // Preserve identical binary64 values and content-addressed replay IDs.
            w.WriteRawValue(v == 0 ? "0" : v.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
        }
        w.WriteEndArray();
    }
}
