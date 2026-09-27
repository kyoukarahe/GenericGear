using System;
using System.Linq;
using BigInteger = System.Numerics.BigInteger;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Display projection only. Rod and slider consume the SDK's travel-admitted enclosed pose.</summary>
    public sealed class GearInvestCrankSliderView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform CrankBody { get; private set; }
        public Transform CrankPin { get; private set; }
        public Transform RodBody { get; private set; }
        public Transform RodMarker { get; private set; }
        public Transform SliderBody { get; private set; }
        public Transform SliderPin { get; private set; }
        public LineRenderer RodLine { get; private set; }
        public bool HasCurrentCrank => CrankBody != null && CrankBody.gameObject.activeSelf;
        public bool HasCurrentConnectedPose => RodBody != null && RodBody.gameObject.activeSelf && SliderBody != null && SliderBody.gameObject.activeSelf;
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        public Bounds ViewBounds { get; private set; }
        private GameObject content;
        private CrankSliderDraft draft;
        private CrankSliderAnalysis analysis;

        public void Clear()
        {
            if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; analysis = null; SourceView = null;
            CrankBody = CrankPin = RodBody = RodMarker = SliderBody = SliderPin = null; RodLine = null;
        }
        private void OnDestroy() { Clear(); }
        public void Build(CrankSliderDraft current, CrankSliderAnalysis currentAnalysis)
        {
            Clear(); draft = current; analysis = currentAnalysis;
            content = new GameObject("Actual retained source, distinct crank, two-endpoint rod and fixed-orientation slider");
            content.transform.SetParent(transform, false);
            var d = current.Definition.Device; var map = current.Definition.SourceMapping;
            var bounds = new Bounds(transform.TransformPoint(V(d.PivotMm)), Vector3.one * 20);
            if (map != null && map.PoseMm.IsProperCardinal && map.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Complete retained source with one explicit mm mapping").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(current.Definition.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(map.PoseMm.Origin); SourceView.transform.localRotation = Rotation(map.PoseMm);
                SourceView.transform.localScale = Vector3.one * Number(map.MillimetersPerSourceUnit);
            }
            if (analysis.HasDeterminedCrankMotion && d.CrankRadius.Kind == QuantityKind.LinearPosition && d.CrankRadius.Value > 0)
            {
                CrankBody = new GameObject(d.CrankBodyId).transform; CrankBody.SetParent(content.transform, false);
                var radius = Number(d.CrankRadius.Value);
                CrankBody.localPosition = V(d.PivotMm);
                Line(CrankBody, "crank rigid arm and zero-ray material", Vector3.zero, Vector3.right * radius, new Color(1, .68f, .22f), 2);
                Marker(CrankBody, "fixed crank pivot", Color.white, 4);
                CrankPin = Marker(CrankBody, d.CrankPinId, Color.yellow, 4); CrankPin.localPosition = Vector3.right * radius;
                var frame = analysis.LocalCompatibility.MappedShaftFrameMm;
                if (frame != null)
                {
                    Line(content.transform, "retained shaft to separate crank mounting", V(frame.Origin), V(d.PivotMm), new Color(.7f, .75f, .9f), .7f);
                    // A plane-aligned square contains the full crank circle at every
                    // phase. Do not expand rod length along all three world axes.
                    foreach (var x in new[] { -1f, 1f }) foreach (var y in new[] { -1f, 1f })
                        bounds.Encapsulate(transform.TransformPoint(V(d.PivotMm) + radius * (x * V(frame.X) + y * V(frame.Y))));
                }
                CrankBody.gameObject.SetActive(false);
            }
            if (d.GuidePresent && d.GuideFrameMm.IsProperCardinal && d.GuideTravel.Kind == QuantityKind.LinearPosition)
            {
                var guide = d.GuideFrameMm;
                var a = V(guide.Point(new ExactVector3(0, 0, d.GuideTravel.Lower.Value)));
                var b = V(guide.Point(new ExactVector3(0, 0, d.GuideTravel.Upper.Value)));
                Line(content.transform, d.GuideId + " finite declared travel", a, b, new Color(.45f, .65f, 1), .8f);
                foreach (var p in new[] { a, b }) Line(content.transform, "closed travel limit", p - V(guide.X) * 6, p + V(guide.X) * 6, new Color(.55f, .75f, 1), .8f);
                bounds.Encapsulate(transform.TransformPoint(a)); bounds.Encapsulate(transform.TransformPoint(b));
            }
            RodBody = new GameObject(d.RodBodyId).transform; RodBody.SetParent(content.transform, false);
            RodLine = Line(RodBody, "actual rod endpoints, fixed length", Vector3.zero, Vector3.zero, new Color(.4f, 1, .76f), 2.4f);
            RodMarker = Marker(RodBody, "rod persistent asymmetric material marker", new Color(1, .35f, .8f), 3.5f);
            RodMarker.localPosition = new Vector3(0, 4, 0);
            SliderBody = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; SliderBody.name = d.SliderBodyId;
            SliderBody.SetParent(content.transform, false); SliderBody.localScale = new Vector3(10, 6, 10);
            var collider = SliderBody.GetComponent<Collider>(); if (collider != null) Destroy(collider);
            GearInvestPresentationMaterial.Apply(SliderBody.GetComponent<Renderer>(), new Color(.48f, .72f, 1));
            // Separate unit-scale pin avoids inheriting the schematic cube's nonuniform scale.
            SliderPin = Marker(content.transform, d.SliderPinId, Color.yellow, 3.5f);
            RodBody.gameObject.SetActive(false); SliderBody.gameObject.SetActive(false); SliderPin.gameObject.SetActive(false);
            foreach (var renderer in content.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is LineRenderer line)
                    for (var i = 0; i < line.positionCount; i++) bounds.Encapsulate(line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i)));
                else if (renderer.gameObject.activeInHierarchy) bounds.Encapsulate(renderer.bounds);
            }
            // An admitted slider lies on the declared guide segment. Its rod
            // joins that segment to the crank circle, so their convex bounds
            // contain all displayed endpoints without recomputing kinematics.
            // Padding includes schematic pins, slider and the asymmetric marker.
            bounds.Expand(20);
            ViewBounds = bounds; ViewCenter = bounds.center; ViewRadius = Mathf.Max(30, bounds.extents.magnitude);
        }
        public void Apply(CrankSliderEvaluation evaluation)
        {
            if (draft == null) return;
            HideCurrentMotion();
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.RootTurns.Value); }
            var crank = evaluation.Crank;
            if (CrankBody != null && crank != null)
            {
                var zero = Quaternion.LookRotation(V(crank.PositiveAxis), V(crank.PositiveAxis.Cross(crank.ZeroRay)));
                CrankBody.localPosition = V(crank.PivotMm);
                CrankBody.localRotation = Quaternion.AngleAxis(360 * Wrapped(crank.SourceTurns + crank.MountingTurns), V(crank.PositiveAxis)) * zero;
                CrankBody.gameObject.SetActive(true);
            }
            var pose = evaluation.Pose;
            if (pose == null) return;
            var a = Mid(pose.CrankPinMm); var p = Mid(pose.SliderPinMm);
            RodBody.localPosition = Mid(pose.RodMidpointMm);
            RodBody.localRotation = Quaternion.LookRotation(V(evaluation.Recipe.Descriptor.PlaneNormal), Mid(pose.RodTransverseDirection));
            RodLine.SetPosition(0, RodBody.InverseTransformPoint(transform.TransformPoint(a)));
            RodLine.SetPosition(1, RodBody.InverseTransformPoint(transform.TransformPoint(p)));
            SliderBody.localPosition = p; SliderBody.localRotation = Rotation(evaluation.Recipe.SliderOrientation);
            SliderPin.localPosition = p;
            RodBody.gameObject.SetActive(true); SliderBody.gameObject.SetActive(true); SliderPin.gameObject.SetActive(true);
        }
        public void HideCurrentMotion()
        {
            if (CrankBody != null) CrankBody.gameObject.SetActive(false);
            if (RodBody != null) RodBody.gameObject.SetActive(false);
            if (SliderBody != null) SliderBody.gameObject.SetActive(false);
            if (SliderPin != null) SliderPin.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
        }
        private static Transform Marker(Transform parent, string name, Color color, float size)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; t.name = name; t.SetParent(parent, false); t.localScale = Vector3.one * size;
            var collider = t.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(t.GetComponent<Renderer>(), color); return t;
        }
        private static LineRenderer Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.startWidth = line.endWidth = width; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b);
            GearInvestPresentationMaterial.Apply(line, color); return line;
        }
        public static Vector3 V(ExactVector3 value) => new Vector3(Number(value.X), Number(value.Y), Number(value.Z));
        public static float Mid(CrankSliderInterval value) => Number((value.Lower + value.Upper) / 2);
        public static Vector3 Mid(CrankSliderVectorInterval value) => new Vector3(Mid(value.X), Mid(value.Y), Mid(value.Z));
        public static Quaternion Rotation(OrientedFrame frame) => Quaternion.LookRotation(V(frame.Z), V(frame.Y));
        private static float Wrapped(Rational value) => Number(new Rational(value.Numerator % value.Denominator, value.Denominator));
        public static float Number(Rational value)
        {
            if (value.IsZero) return 0;
            var n = BigInteger.Abs(value.Numerator); var d = value.Denominator;
            var ns = Math.Max(0, BitLength(n) - 53); var ds = Math.Max(0, BitLength(d) - 53);
            var number = (double)(n >> ns) / (double)(d >> ds) * Math.Pow(2, ns - ds) * value.Sign; var result = (float)number;
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 1e12 || !value.IsZero && result == 0)
                throw new CrankSliderDisplayUnavailableException("Unity display projection exceeds finite resource limits; exact mechanics are unchanged.");
            return result;
        }
        private static int BitLength(BigInteger positive)
        {
            var bytes = positive.ToByteArray(); var top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            var bits = top * 8; var b = bytes[top]; while (b > 0) { bits++; b >>= 1; } return bits;
        }
    }
    public sealed class CrankSliderDisplayUnavailableException : Exception
    { public CrankSliderDisplayUnavailableException(string message) : base(message) { } }
}
