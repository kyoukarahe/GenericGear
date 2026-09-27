using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional schematic projection. SDK linear frames alone move the nut; no lead conversion or root wrapping is performed here.</summary>
    public sealed class GearInvestLinearLeadScrewView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform Screw { get; private set; }
        public Transform Nut { get; private set; }
        public Transform ScrewMarker { get; private set; }
        public Transform NutMarker { get; private set; }
        public bool HasCurrentLinearPose => Nut != null && Nut.gameObject.activeSelf;
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        private GameObject content; private RotaryLinearDraft draft; private Quaternion screwReference;
        public void Clear()
        {
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; SourceView = null; Screw = Nut = ScrewMarker = NutMarker = null;
        }
        public void Build(RotaryLinearDraft current, RotaryLinearAnalysis analysis)
        {
            Clear(); draft = current; content = new GameObject("Dimensioned mechanical projection"); content.transform.SetParent(transform, false);
            var mapping = current.Definition.SourceMapping; var device = current.Definition.Device;
            if (mapping != null && mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Original source geometry, uniformly mapped to mm").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(current.Definition.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(mapping.PoseMm.Origin); SourceView.transform.localRotation = R(mapping.PoseMm);
                SourceView.transform.localScale = Vector3.one * N(mapping.MillimetersPerSourceUnit);
            }
            var guide = device.GuideFrameMm;
            if (guide.IsProperCardinal && device.GuideInterval.Kind == QuantityKind.LinearPosition)
            {
                var low = guide.Origin + guide.Z * device.GuideInterval.Lower.Value;
                var high = guide.Origin + guide.Z * device.GuideInterval.Upper.Value;
                Line(content.transform, "guide-reference-axis", V(low), V(high), new Color(.45f, 1f, .65f), .45f);
                foreach (var endpoint in new[] { low, high })
                    Line(content.transform, "closed-reference-point-boundary", V(endpoint - guide.X * 4), V(endpoint + guide.X * 4), Color.green, .65f);
                foreach (var side in new[] { -1, 1 })
                    Line(content.transform, "schematic-fixed-guide", V(low + guide.X * (side * 4)), V(high + guide.X * (side * 4)), new Color(.35f, .65f, .8f), .3f);
            }
            if (device.PhysicalAxis.LengthSquared == 1)
            {
                Screw = new GameObject(device.ScrewBodyId + " (not a gear)").transform; Screw.SetParent(content.transform, false);
                Screw.localPosition = V(device.ScrewAxialDatumMm);
                screwReference = Quaternion.FromToRotation(Vector3.up, V(device.PhysicalAxis)); Screw.localRotation = screwReference;
                var length = Mathf.Max(12, Mathf.Abs(N(device.GuideInterval.Upper.Value)) + 6);
                var cylinder = Primitive(Screw, PrimitiveType.Cylinder, "schematic screw shaft", new Color(.8f, .67f, .33f));
                cylinder.localPosition = Vector3.up * length * .5f; cylinder.localScale = new Vector3(2.2f, length * .5f, 2.2f);
                ScrewMarker = Primitive(Screw, PrimitiveType.Cube, "screw material meridian", Color.yellow);
                ScrewMarker.localPosition = new Vector3(2.2f, length * .6f, 0); ScrewMarker.localScale = new Vector3(2.6f, .8f, .8f);
                var rib = new GameObject("SCHEMATIC rib; pitch and thickness are not thread validation").AddComponent<LineRenderer>(); rib.transform.SetParent(Screw, false);
                rib.useWorldSpace = false; rib.positionCount = 145; rib.startWidth = rib.endWidth = .28f;
                for (var i = 0; i < rib.positionCount; i++) { var t = (float)i / (rib.positionCount - 1); var a = device.Handedness * t * Mathf.PI * 12; rib.SetPosition(i, new Vector3(Mathf.Cos(a) * 1.35f, t * length, Mathf.Sin(a) * 1.35f)); }
                GearInvestPresentationMaterial.Apply(rib, new Color(.95f, .83f, .4f));
            }
            Nut = Primitive(content.transform, PrimitiveType.Cube, device.NutBodyId + " (translation only)", new Color(.35f, .9f, .58f));
            Nut.localScale = new Vector3(6, 4, 4);
            NutMarker = Primitive(Nut, PrimitiveType.Cube, "fixed nut orientation marker", new Color(1, .35f, .4f));
            NutMarker.localPosition = new Vector3(.6f, 0, 0); NutMarker.localScale = new Vector3(.2f, 1.1f, .35f);
            Nut.gameObject.SetActive(false);
            var renderers = content.GetComponentsInChildren<Renderer>(); var bounds = renderers.Length == 0 ? new Bounds(Vector3.zero, Vector3.one * 40) : renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            ViewCenter = bounds.center; ViewRadius = Mathf.Max(15, bounds.extents.magnitude);
        }
        public void Apply(RotaryLinearEvaluation frame)
        {
            if (draft == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(frame.Input.Value); }
            if (Screw != null)
            {
                var shaft = frame.Rotary.FirstOrDefault(s => s.ShaftId == draft.Definition.Device.ScrewShaftId);
                Screw.gameObject.SetActive(shaft != null);
                if (shaft != null) Screw.localRotation = Quaternion.AngleAxis(360f * N(shaft.Turns), V(shaft.PositiveAxis)) * screwReference;
            }
            if (Nut != null)
            {
                Nut.gameObject.SetActive(frame.Status == RotaryLinearEvaluationStatus.Success && frame.Linear != null);
                if (frame.Linear != null) { Nut.localPosition = V(frame.Linear.WorldPositionMm); Nut.localRotation = R(frame.Linear.NutOrientation); }
            }
        }
        public void HideCurrentMotion()
        {
            if (Nut != null) Nut.gameObject.SetActive(false);
            if (Screw != null) Screw.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
        }
        private static Transform Primitive(Transform parent, PrimitiveType kind, string name, Color color)
        { var go = GameObject.CreatePrimitive(kind); go.name = name; go.transform.SetParent(parent, false); var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color); return go.transform; }
        private static void Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); }
        private static Vector3 V(ExactVector3 p) => GearInvestOrientedMechanismView.V(p);
        private static float N(Rational q) => GearInvestOrientedMechanismView.N(q);
        private static Quaternion R(OrientedFrame f) => GearInvestOrientedMechanismView.Rotation(f);
    }
}
