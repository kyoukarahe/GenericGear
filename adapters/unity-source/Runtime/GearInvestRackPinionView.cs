using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional CP schematic. Approximation is explicit at this renderer boundary; it never produces a mechanical law or a persisted exact value.</summary>
    public sealed class GearInvestRackPinionView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform Pinion { get; private set; }
        public Transform Rack { get; private set; }
        public Transform PinionMarker { get; private set; }
        public Transform RackMarker { get; private set; }
        public Transform FixedContact { get; private set; }
        public bool HasCurrentLinearPose => Rack != null && Rack.gameObject.activeSelf;
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        private GameObject content; private RackPinionDraft draft; private Quaternion pinionReference;

        public void Clear()
        {
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; SourceView = null; Pinion = Rack = PinionMarker = RackMarker = FixedContact = null;
        }
        public void Build(RackPinionDraft current, RackPinionAnalysis analysis)
        {
            Clear(); draft = current; content = new GameObject("CP rack-pinion schematic in mm"); content.transform.SetParent(transform, false);
            var mapping = current.Definition.SourceMapping; var device = current.Definition.Device;
            if (mapping != null && mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Entire retained source; uniform mm projection").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(current.Definition.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(mapping.PoseMm.Origin); SourceView.transform.localRotation = R(mapping.PoseMm);
                SourceView.transform.localScale = Vector3.one * N(mapping.MillimetersPerSourceUnit);
                var shaft = current.Definition.Source.Definition.Shafts.FirstOrDefault(s => s.Id == device.PinionShaftId);
                if (shaft != null && device.PinionToothCount > 0 && device.PinionCircularPitch.Kind == QuantityKind.LinearPosition && device.PinionCircularPitch.Value > 0)
                {
                    var mappedFrame = mapping.FrameMm(shaft.Frame); var radius = F(device.PitchRadius.ApproximateMillimeters());
                    Pinion = new GameObject(device.PinionBodyId + " / added CP pitch circle").transform; Pinion.SetParent(content.transform, false);
                    Pinion.localPosition = V(device.PinionCenterMm); pinionReference = R(mappedFrame); Pinion.localRotation = pinionReference;
                    var circle = new GameObject("Display approximation of exact radius " + device.PitchRadius).AddComponent<LineRenderer>();
                    circle.transform.SetParent(Pinion, false); circle.useWorldSpace = false; circle.positionCount = 97; circle.startWidth = circle.endWidth = .7f;
                    for (var i = 0; i < circle.positionCount; i++) { var angle = i * 2f * Mathf.PI / 96; circle.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius); }
                    GearInvestPresentationMaterial.Apply(circle, new Color(1, .68f, .22f));
                    // Pitch-index ticks are bounded presentation, not tooth-flank/solid geometry or collision truth.
                    if (device.PinionToothCount <= 256)
                        for (var i = 0; i < device.PinionToothCount; i++)
                        { var angle = i * 2f * Mathf.PI / device.PinionToothCount; var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0); Line(Pinion, "schematic pitch index", direction * (radius - 1), direction * (radius + 1), new Color(1, .75f, .3f), .35f); }
                    Line(Pinion, "pinion material spoke", Vector3.zero, Vector3.right * radius, Color.yellow, .5f);
                    PinionMarker = Primitive(Pinion, PrimitiveType.Cube, "rotating pinion material marker", Color.yellow);
                    PinionMarker.localPosition = Vector3.right * radius; PinionMarker.localScale = new Vector3(2.8f, 1.7f, 1.7f);
                    Line(content.transform, "retained shaft mounting to separate CP body", V(mappedFrame.Origin), V(device.PinionCenterMm), new Color(.55f, .6f, .75f), .65f);
                }
            }
            var guide = device.GuideFrameMm;
            if (guide.IsProperCardinal && device.GuideInterval.Kind == QuantityKind.LinearPosition)
            {
                var low = guide.Origin + ExactPiVector3.FromMillimeters(guide.Z * device.GuideInterval.Lower.Value);
                var high = guide.Origin + ExactPiVector3.FromMillimeters(guide.Z * device.GuideInterval.Upper.Value);
                Line(content.transform, "fixed reference-point guide interval", P(low), P(high), new Color(.3f, .6f, .8f), .4f);
                foreach (var endpoint in new[] { low, high })
                    Line(content.transform, "closed guide endpoint", P(endpoint) - V(guide.X) * 3, P(endpoint) + V(guide.X) * 3, Color.cyan, .6f);
                if (device.ActiveMaterialInterval.Kind == QuantityKind.LinearPosition)
                {
                    Rack = new GameObject(device.RackBodyId + " / translating active material").transform; Rack.SetParent(content.transform, false);
                    Line(Rack, "active rack material interval; pitch contact only", Vector3.forward * N(device.ActiveMaterialInterval.Lower.Value), Vector3.forward * N(device.ActiveMaterialInterval.Upper.Value), new Color(.3f, .95f, .65f), 1.2f);
                    foreach (var value in new[] { device.ActiveMaterialInterval.Lower.Value, device.ActiveMaterialInterval.Upper.Value })
                        Line(Rack, "active material endpoint", new Vector3(-2, 0, N(value)), new Vector3(2, 0, N(value)), Color.green, .7f);
                    RackMarker = Primitive(Rack, PrimitiveType.Cube, "rack material reference / no rotation", new Color(1, .35f, .5f));
                    RackMarker.localScale = new Vector3(3, 3, 4); Rack.gameObject.SetActive(false);
                }
            }
            if (analysis.LocalCompatibility.FixedContactPointMm.HasValue)
            {
                FixedContact = Primitive(content.transform, PrimitiveType.Sphere, "Q fixed pitch contact; not a pinion material point", Color.cyan);
                FixedContact.localPosition = P(analysis.LocalCompatibility.FixedContactPointMm.Value); FixedContact.localScale = Vector3.one * 2.6f;
            }
            var renderers = content.GetComponentsInChildren<Renderer>(); var bounds = renderers.Length == 0 ? new Bounds(Vector3.zero, Vector3.one * 40) : renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Include possible reference/material endpoints for stable framing across caller root changes.
            if (guide.IsProperCardinal && device.GuideInterval.Kind == QuantityKind.LinearPosition && device.ActiveMaterialInterval.Kind == QuantityKind.LinearPosition)
                foreach (var x in new[] { device.GuideInterval.Lower.Value, device.GuideInterval.Upper.Value })
                    foreach (var xi in new[] { device.ActiveMaterialInterval.Lower.Value, device.ActiveMaterialInterval.Upper.Value })
                        bounds.Encapsulate(P(guide.Origin + ExactPiVector3.FromMillimeters(guide.Z * (x + xi))));
            ViewCenter = bounds.center; ViewRadius = Mathf.Max(25, bounds.extents.magnitude);
        }
        public void Apply(RackPinionEvaluation frame)
        {
            if (draft == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(frame.Input.Value); }
            if (Pinion != null)
            {
                var shaft = frame.Rotary.FirstOrDefault(s => s.ShaftId == draft.Definition.Device.PinionShaftId);
                Pinion.gameObject.SetActive(shaft != null);
                if (shaft != null) Pinion.localRotation = Quaternion.AngleAxis(360f * N(shaft.Turns), V(shaft.PositiveAxis)) * pinionReference;
            }
            if (Rack != null)
            {
                Rack.gameObject.SetActive(frame.Status == RackPinionEvaluationStatus.Success && frame.Linear != null);
                if (frame.Linear != null) { Rack.localPosition = P(frame.Linear.WorldPositionMm); Rack.localRotation = Rotation(frame.Linear.RackOrientation); }
            }
            // FixedContact is ground-owned. It is intentionally untouched by material rotation and terminal-coordinate changes.
        }
        public void HideCurrentMotion()
        { if (Rack != null) Rack.gameObject.SetActive(false); if (Pinion != null) Pinion.gameObject.SetActive(false); if (SourceView != null) SourceView.gameObject.SetActive(false); }
        public static Vector3 P(ExactPiVector3 point) { var p = point.ApproximateMillimeters(); return new Vector3(F(p.X), F(p.Y), F(p.Z)); }
        public static Quaternion Rotation(ExactPiFrame frame) => Quaternion.LookRotation(V(frame.Z), V(frame.Y));
        private static float F(double value) { var f = (float)value; if (float.IsNaN(f) || float.IsInfinity(f)) throw new ArgumentException("Exact geometry exceeds finite display range; mechanical definition remains exact."); return f; }
        private static Vector3 V(ExactVector3 value) => GearInvestOrientedMechanismView.V(value);
        private static float N(Rational value) => GearInvestOrientedMechanismView.N(value);
        private static Quaternion R(OrientedFrame frame) => GearInvestOrientedMechanismView.Rotation(frame);
        private static Transform Primitive(Transform parent, PrimitiveType type, string name, Color color)
        { var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color); return go.transform; }
        private static void Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); }
    }
}
