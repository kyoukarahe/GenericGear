using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GearInvest.Core;
using GearInvest.Engine;

namespace GearInvest.Serialization.Json;

internal static partial class AssemblyReplayProjection
{
    internal static int WriteSampleFeatures(Utf8JsonWriter w, MechanicalAssemblyEvaluation e, int remainingVertices, bool fixedOnly = false)
    {
        var vertices = 0; var count = 0;
        w.WriteStartArray();
        foreach (var current in e.Members)
        {
            var member = e.Analysis.Members.Single(x => x.InstanceId == current.InstanceId);
            var parent = AssemblyComponentReference.Member(current.InstanceId, AssemblyComponentKind.Constraint, member.Member.Declaration.LocalDeviceId);
            void Feature(string id, IEnumerable<double[]> source, bool closed = false, string role = "material")
            {
                if ((role == "fixedRoute") != fixedOnly) return;
                var points = source.Take(remainingVertices - vertices + 1).ToArray(); vertices += points.Length;
                AssemblyReplayJson.Need(vertices <= remainingVertices && ++count <= AssemblyReplayLimits.Features, "Replay feature/vertex bound exceeded.");
                w.WriteStartObject(); w.WriteString("memberId", current.InstanceId); w.WriteString("localId", id);
                w.WritePropertyName("mechanicalReference"); MechanicalAssemblyJson.Reference(w, parent);
                w.WriteString("scope", "displayApproximation"); w.WriteString("role", role); w.WriteBoolean("closed", closed);
                w.WriteStartArray("pointsMm"); foreach (var p in points) Doubles(w, p); w.WriteEndArray(); w.WriteEndObject();
            }
            var worm = current.Display as WormDriveDisplayPose ?? (current.Display as AssemblyGenevaSuffixDisplay)?.Worm;
            if (worm is not null)
            {
                foreach (var line in worm.WormHelices.Concat(worm.WheelToothTraces)) Feature(line.Id, line.Points.Select(p => new[] { p.X, p.Y, p.Z }));
                var p = worm.WormMaterialMarker; Feature("worm-material-zero", new[] { new[] { p.X, p.Y, p.Z } });
                p = worm.WheelMaterialMarker; Feature("wheel-material-zero", new[] { new[] { p.X, p.Y, p.Z } });
            }
            if (current.Display is OpenBeltDisplayRoute belt && current.BeltOutput is not null)
            {
                Feature("belt-route", belt.SampleLoop().Select(p => new[] { p.X, p.Y, p.Z }), true, "fixedRoute");
                for (var i = 0; i < 12; i++)
                { var p = belt.SampleMaterialMark(current.BeltOutput.BeltMaterialTravelPiCoefficientMm, new Rational(i, 12)); Feature("belt-material-" + i, new[] { new[] { p.X, p.Y, p.Z } }); }
            }
            if (current.Display is AssemblyGenevaSuffixDisplay suffix && suffix.Belt is not null)
            {
                Feature("belt-route", suffix.Belt.SampleLoop().Select(p => new[] { p.X, p.Y, p.Z }), true, "fixedRoute");
                for (var i = 0; i < suffix.BeltMaterialMarksMm.Count; i++)
                { var p = suffix.BeltMaterialMarksMm[i]; Feature("belt-material-" + i, new[] { new[] { p.X, p.Y, p.Z } }); }
            }
            if (current.Display is PitchChainDisplayPose chain)
            {
                foreach (var pin in chain.Pins) Feature(pin.PinId, new[] { new[] { pin.Position.X, pin.Position.Y, pin.Position.Z } });
                foreach (var link in chain.Links) Feature(link.LinkId, new[] { new[] { link.Start.X, link.Start.Y, link.Start.Z }, new[] { link.End.X, link.End.Y, link.End.Z } });
            }
            var geneva = current.Display as GenevaNumericComputation;
            var driver = current.Display as GenevaDriverNumericComputation;
            var features = geneva?.Pose?.FeaturePoints ?? driver?.FeaturePoints;
            if (features is not null) foreach (var f in features) Feature(f.Id, new[] { Point(f.PointMm) });
            if (member.Member.Declaration is AssemblyGenevaDeclaration g && current.NumericAvailable && current.Numeric is GenevaNumericComputation n && n.Pose is not null)
                Feature(g.Device.PinId, new[] { Point(n.Pose.PinMm) }, role: "driverPin");
            if (member.Member.Declaration is AssemblyCrankSliderDeclaration c && current.NumericAvailable && current.Numeric is CrankSliderNumericComputation cn && cn.Pose is not null)
            { Feature(c.Device.CrankPinId, new[] { Point(cn.Pose.CrankPinMm) }); Feature(c.Device.SliderPinId, new[] { Point(cn.Pose.SliderPinMm) }); }
            if (current.Display is AssemblyCamDisplaySamples cam && cam.IsAvailable)
            {
                Feature("cam-contour", cam.ContourPointsMm.Select(Point), true);
                if (cam.MaterialMarkerMm is not null) Feature("cam-material-zero", new[] { Point(cam.MaterialMarkerMm) });
            }
            if (member.Member.Declaration is AssemblyCamFollowerDeclaration ca && current.NumericAvailable && current.Recipe is CamFollowerPoseRecipe cr)
            {
                Feature(ca.Device.FollowerReferenceId, new[] { Point(cr.FollowerReferencePointMm) }, role: "followerReference");
                if (current.Numeric is CamNumericResult nr && nr.IsAvailable && nr.Point is not null) Feature("cam-contact", new[] { Point(nr.Point) }, role: "contact");
            }
        }
        w.WriteEndArray(); return vertices;
    }
}
