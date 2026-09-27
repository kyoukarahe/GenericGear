using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional point-pin/ideal-lock schematic. All current material points and directions come from the SDK.</summary>
    public sealed class GearInvestGenevaView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform DriverBody { get; private set; }
        public Transform WheelBody { get; private set; }
        public Transform Pin { get; private set; }
        public Transform DriverMaterialMarker { get; private set; }
        public Transform WheelMaterialMarker { get; private set; }
        public LineRenderer DriverLockCircle { get; private set; }
        public LineRenderer WheelEnvelope { get; private set; }
        public readonly Dictionary<string, LineRenderer> Slots = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);
        public readonly Dictionary<string, LineRenderer> Recesses = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);
        public readonly Dictionary<string, Vector3> CurrentFeaturePoints = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        public bool HasCurrentDriver => DriverBody != null && DriverBody.gameObject.activeSelf;
        public bool HasCurrentPin => Pin != null && Pin.gameObject.activeSelf;
        public bool HasCurrentOutput => WheelBody != null && WheelBody.gameObject.activeSelf;
        public string ActiveSlotId { get; private set; }
        public string ActiveRecessId { get; private set; }
        public string DisplayUnavailableReason { get; private set; }
        public Bounds ViewBounds { get; private set; }
        public Vector3 ViewCenter => ViewBounds.center;
        public float ViewRadius => Mathf.Max(30, ViewBounds.extents.magnitude);
        private GameObject content;
        private GenevaDraft draft;
        private LineRenderer driverRay;
        private LineRenderer wheelRay;
        public void Clear()
        {
            if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; SourceView = null; DriverBody = WheelBody = Pin = DriverMaterialMarker = WheelMaterialMarker = null;
            DriverLockCircle = WheelEnvelope = driverRay = wheelRay = null; Slots.Clear(); Recesses.Clear(); CurrentFeaturePoints.Clear();
            ActiveSlotId = ActiveRecessId = DisplayUnavailableReason = null;
        }
        private void OnDestroy() { Clear(); }
        public void Build(GenevaDraft current, GenevaAnalysis analysis)
        {
            Clear(); draft = current; content = new GameObject("Point pin / straight radial slots / ideal phase-released lock schematic"); content.transform.SetParent(transform, false);
            var g = current.Definition.Device; var map = current.Definition.SourceMapping;
            if (map != null && map.PoseMm.IsProperCardinal && map.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Whole source retained at its actual mm mapping").AddComponent<GearInvestOrientedMechanismView>(); SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(current.Definition.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(map.PoseMm.Origin); SourceView.transform.localRotation = Rotation(map.PoseMm); SourceView.transform.localScale = Vector3.one * Number(map.MillimetersPerSourceUnit);
            }
            DriverBody = Body(g.DriverBodyId, g.DriverCenterMm); WheelBody = Body(g.WheelBodyId, g.WheelCenterMm);
            DriverLockCircle = Line(DriverBody, g.IdealLock.DriverFeatureId + " ideal circular locus (release solid NOT validated)", new Color(.84f, .47f, .18f), 1.3f, true);
            WheelEnvelope = Line(WheelBody, "Selected mouth envelope (schematic, not a machined outline)", new Color(.18f, .7f, .76f), 1.3f, true);
            driverRay = Line(DriverBody, "Integral point pin driver ray", new Color(1, .65f, .2f), 1.6f);
            wheelRay = Line(WheelBody, "Persistent wheel material slot zero", new Color(1, .25f, .82f), 1.2f);
            Pin = Marker(content.transform, g.PinId + " point pin marker only", Color.yellow, 4);
            DriverMaterialMarker = Marker(content.transform, "Persistent driver pin direction", new Color(1, .6f, .17f), 3);
            WheelMaterialMarker = Marker(content.transform, "Persistent material slot zero mouth", new Color(1, .25f, .82f), 3.6f);
            foreach (var id in g.Wheel.SlotIds) Slots.Add(id, Line(WheelBody, id, new Color(.35f, .83f, .86f), 1.6f));
            foreach (var id in g.IdealLock.RecessIds) Recesses.Add(id, Line(WheelBody, id + " finite ideal concave patch", new Color(.44f, .5f, .66f), 1.6f));
            var anchors = new[] { g.DriverCenterMm, g.WheelCenterMm, g.OutputShaft.Frame.Origin };
            foreach (var p in anchors) Marker(content.transform, "Authored fixed shaft anchor", new Color(.66f, .72f, .85f), 2.5f).localPosition = V(p);
            var mount = analysis.Local.MappedSourceFrameMm;
            if (mount != null) SetLine(Line(content.transform, "Actual source shaft to driver station", new Color(.6f, .68f, .9f), .8f), new[] { V(mount.Origin), V(g.DriverCenterMm) });
            SetLine(Line(content.transform, "Output shaft to wheel station", new Color(.6f, .68f, .9f), .8f), new[] { V(g.OutputShaft.Frame.Origin), V(g.WheelCenterMm) });
            HideCurrentMotion(); UpdateBounds();
        }
        public void Apply(GenevaEvaluation evaluation)
        {
            HideCurrentMotion(); if (draft == null || evaluation == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.RootTurns.Value); }
            var g = draft.Definition.Device; var normal = V(g.PlaneNormal);
            var pose = evaluation.NormalPose?.Numeric;
            var driverNumeric = evaluation.DriverNumeric;
            var driverE = pose?.DriverE ?? driverNumeric?.DriverE;
            var driverF = pose?.DriverF ?? driverNumeric?.DriverF;
            var pin = pose?.PinMm ?? driverNumeric?.PinMm;
            var points = pose?.FeaturePoints ?? driverNumeric?.FeaturePoints;
            if (points != null) foreach (var point in points) CurrentFeaturePoints.Add(point.Id, Mid(point.PointMm));
            if (evaluation.Driver != null && driverE != null && pin != null)
            {
                var f = Mid(driverF);
                DriverBody.localRotation = Quaternion.LookRotation(normal, f); DriverBody.gameObject.SetActive(true);
                SetLine(driverRay, new[] { V(g.DriverCenterMm), Mid(pin) });
                DriverMaterialMarker.localPosition = Mid(pin); DriverMaterialMarker.gameObject.SetActive(true);
                if (g.PinPresent) { Pin.localPosition = Mid(pin); Pin.gameObject.SetActive(true); }
                SetFeatureLine(DriverLockCircle, Enumerable.Range(0, 16).Select(i => g.IdealLock.DriverFeatureId + "/circle-" + i.ToString("D2")));
                DriverLockCircle.gameObject.SetActive(g.IdealLock.Present && DriverLockCircle.positionCount == 16);
            }
            // Conditional recipe and DiagnosticPose can explain a refusal, but cannot drive normal output graphics.
            if (pose != null)
            {
                WheelBody.localRotation = Quaternion.LookRotation(normal, Mid(pose.WheelF)); WheelBody.gameObject.SetActive(true);
                ActiveSlotId = evaluation.Recipe.SlotId; ActiveRecessId = evaluation.Recipe.RecessId;
                foreach (var pair in Slots)
                {
                    SetFeatureLine(pair.Value, new[] { pair.Key + "/root", pair.Key + "/mouth" });
                    GearInvestPresentationMaterial.Apply(pair.Value, pair.Key == ActiveSlotId ? Color.yellow : new Color(.35f, .83f, .86f));
                }
                foreach (var pair in Recesses)
                {
                    SetFeatureLine(pair.Value, Enumerable.Range(0, 3).Select(i => pair.Key + "/arc-" + i));
                    GearInvestPresentationMaterial.Apply(pair.Value, pair.Key == ActiveRecessId ? new Color(.45f, 1, .48f) : new Color(.44f, .5f, .66f));
                }
                SetFeatureLine(WheelEnvelope, Enumerable.Range(0, 24).Select(i => g.WheelBodyId + "/schematic-mouth-" + i.ToString("D2")));
                if (CurrentFeaturePoints.TryGetValue(g.Wheel.SlotIds[0] + "/mouth", out var material))
                { WheelMaterialMarker.localPosition = material; WheelMaterialMarker.gameObject.SetActive(true); SetLine(wheelRay, new[] { V(g.WheelCenterMm), material }); }
            }
            DisplayUnavailableReason = pose == null ? evaluation.Status.ToString() : null;
            UpdateBounds();
        }
        public void HideCurrentMotion()
        {
            foreach (var t in new[] { DriverBody, WheelBody, Pin, DriverMaterialMarker, WheelMaterialMarker }) if (t != null) t.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
            CurrentFeaturePoints.Clear(); ActiveSlotId = ActiveRecessId = null;
        }
        private Transform Body(string id, ExactVector3 center)
        { var t = new GameObject(id).transform; t.SetParent(content.transform, false); t.localPosition = V(center); Marker(t, "Body axis center", Color.white, 3); return t; }
        private void SetFeatureLine(LineRenderer line, IEnumerable<string> ids)
        {
            var values = ids.ToArray(); if (values.Any(id => !CurrentFeaturePoints.ContainsKey(id))) { line.positionCount = 0; return; }
            SetLine(line, values.Select(id => CurrentFeaturePoints[id]).ToArray());
        }
        // This is only an ordinary transform from SDK world-mm samples into the renderer's local coordinates.
        private void SetLine(LineRenderer line, Vector3[] points)
        { line.positionCount = points.Length; line.SetPositions(points.Select(p => line.transform.InverseTransformPoint(transform.TransformPoint(p))).ToArray()); }
        private void UpdateBounds()
        {
            var bounds = new Bounds(transform.TransformPoint(draft == null ? Vector3.zero : V(draft.Definition.Device.DriverCenterMm)), Vector3.one * 10);
            foreach (var renderer in content.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is LineRenderer line) for (var i = 0; i < line.positionCount; i++) bounds.Encapsulate(line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i)));
                else if (renderer.GetComponent<MeshFilter>()?.sharedMesh is Mesh mesh)
                    foreach (var vertex in mesh.vertices) bounds.Encapsulate(renderer.transform.TransformPoint(vertex));
            }
            bounds.Expand(16); ViewBounds = bounds;
        }
        private static Transform Marker(Transform parent, string name, Color color, float size)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; t.name = name; t.SetParent(parent, false); t.localScale = Vector3.one * size;
            var collider = t.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(t.GetComponent<Renderer>(), color); return t;
        }
        private static LineRenderer Line(Transform parent, string name, Color color, float width, bool loop = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = loop;
            line.positionCount = 0; line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); return line;
        }
        public static Vector3 Mid(GenevaVectorInterval v) => new Vector3(Number((v.X.Lower + v.X.Upper) / 2), Number((v.Y.Lower + v.Y.Upper) / 2), Number((v.Z.Lower + v.Z.Upper) / 2));
        public static Vector3 V(ExactVector3 v) => GearInvestCamFollowerView.V(v);
        public static float Number(Rational v) => GearInvestCamFollowerView.Number(v);
        public static Quaternion Rotation(OrientedFrame frame) => GearInvestCamFollowerView.Rotation(frame);
    }
}
