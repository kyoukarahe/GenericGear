using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional presentation of SDK fixed-pitch recipes. No material routing, link selection or kinematic solver.</summary>
    public sealed class GearInvestPitchChainView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform InputSprocket { get; private set; }
        public Transform OutputSprocket { get; private set; }
        public Transform InputMarker { get; private set; }
        public Transform OutputMarker { get; private set; }
        public Dictionary<string, Transform> MaterialPins { get; } = new Dictionary<string, Transform>(StringComparer.Ordinal);
        public Dictionary<string, LineRenderer> MaterialLinks { get; } = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);
        public bool HasCurrentOutputPose => OutputSprocket != null && OutputSprocket.gameObject.activeSelf;
        public bool HasCurrentChainMaterial => MaterialPins.Count > 0 && MaterialPins.Values.First().gameObject.activeSelf;
        public string SelectedMaterialId { get; private set; }
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        private readonly Dictionary<string, Color> linkColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private GameObject content;
        private PitchChainDraft draft;
        private Quaternion inputZero, outputZero;
        public void Clear()
        {
            if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; SourceView = null; InputSprocket = OutputSprocket = InputMarker = OutputMarker = null;
            MaterialPins.Clear(); MaterialLinks.Clear(); linkColors.Clear();
        }
        private void OnDestroy() { Clear(); }
        public void Build(PitchChainDraft current, PitchChainAnalysis analysis)
        {
            Clear(); draft = current; content = new GameObject("Fixed-pitch material chain and complete retained source"); content.transform.SetParent(transform, false);
            var d = current.Definition; var c = d.Device; var mapping = d.SourceMapping; var geometry = analysis.LocalCompatibility.Geometry;
            if (mapping != null && mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Complete retained source with explicit mm mapping").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(d.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(mapping.PoseMm.Origin); SourceView.transform.localRotation = Rotation(mapping.PoseMm);
                SourceView.transform.localScale = Vector3.one * Number(mapping.MillimetersPerSourceUnit);
                var input = d.Source.Definition.Shafts.FirstOrDefault(s => s.Id == c.InputShaftId);
                if (input != null && input.Frame.IsProperCardinal && geometry != null)
                {
                    var mapped = mapping.FrameMm(input.Frame);
                    inputZero = Rotation(mapped) * Quaternion.AngleAxis(360 * WrappedTurns(c.InputMountingPhase.Value), Vector3.forward);
                    InputSprocket = Sprocket(c.InputSprocketBodyId, geometry.InputCenterMm, inputZero, geometry.Pitch, new Color(1, .68f, .2f), out var marker);
                    InputMarker = marker;
                    Line(content.transform, "retained shaft to distinct input sprocket", V(mapped.Origin), V(geometry.InputCenterMm), new Color(.7f, .7f, .85f), .55f);
                }
            }
            if (geometry != null && c.OutputShaft.Frame.IsProperCardinal)
            {
                outputZero = Rotation(c.OutputShaft.Frame) * Quaternion.AngleAxis(360 * WrappedTurns(c.OutputMountingPhase.Value), Vector3.forward);
                OutputSprocket = Sprocket(c.OutputSprocketBodyId, geometry.OutputCenterMm, outputZero, geometry.Pitch, new Color(.45f, .88f, .6f), out var marker);
                OutputMarker = marker;
                var center = V(geometry.OutputCenterMm); var axis = V(c.OutputShaft.Frame.Z);
                Line(content.transform, "output positive shaft axis", center - axis * 9, center + axis * 13, Color.white, .5f);
                Line(content.transform, "output positive axis arrow", center + axis * 13, center + axis * 8 + V(c.OutputShaft.Frame.X) * 2, Color.white, .5f);
            }
            if (geometry != null)
            {
                var radius = Finite(PitchChainDisplay.ApproximateRadius(geometry.Pitch));
                var low = V(geometry.InputCenterMm) - Vector3.one * radius; var high = V(geometry.OutputCenterMm) + Vector3.one * radius;
                var bounds = new Bounds((low + high) / 2, Vector3.zero); bounds.Encapsulate(low); bounds.Encapsulate(high);
                foreach (var renderer in content.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(renderer.bounds);
                ViewCenter = bounds.center; ViewRadius = Mathf.Max(30, bounds.extents.magnitude);
            }
            else
            {
                var renderers = content.GetComponentsInChildren<Renderer>(true); var bounds = new Bounds(Vector3.zero, Vector3.one * 80);
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds); ViewCenter = bounds.center; ViewRadius = Mathf.Max(30, bounds.extents.magnitude);
            }
        }
        public void Apply(PitchChainEvaluation evaluation)
        {
            if (draft == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.Input.Value); }
            var input = evaluation.Rotary.FirstOrDefault(r => r.ShaftId == draft.Definition.Device.InputShaftId);
            if (InputSprocket != null)
            {
                InputSprocket.gameObject.SetActive(input != null);
                if (input != null) InputSprocket.localRotation = Quaternion.AngleAxis(360 * WrappedTurns(input.Turns), V(input.PositiveAxis)) * inputZero;
            }
            var output = evaluation.Output; var pose = evaluation.ChainPose;
            var success = evaluation.Status == PitchChainEvaluationStatus.Success && output != null && pose != null;
            if (OutputSprocket != null)
            {
                OutputSprocket.gameObject.SetActive(success);
                if (success) OutputSprocket.localRotation = Quaternion.AngleAxis(360 * WrappedTurns(output.ShaftTurns.Value), V(output.ShaftPositiveAxis)) * outputZero;
            }
            foreach (var pin in MaterialPins.Values) pin.gameObject.SetActive(false);
            foreach (var link in MaterialLinks.Values) link.gameObject.SetActive(false);
            if (!success) return;
            var display = PitchChainDisplay.Approximate(pose);
            var floatPoints = display.Pins.ToDictionary(p => p.PinId, p => Point(p.Position), StringComparer.Ordinal);
            foreach (var link in display.Links)
            {
                var length = Vector3.Distance(floatPoints[link.StartPinId], floatPoints[link.EndPinId]);
                if (!(length > 0) || Math.Abs(length - display.PitchMm) > display.PitchMm * 1e-4)
                    throw new PitchChainDisplayUnavailableException("Unity float projection cannot preserve distinct fixed-pitch link endpoints.");
            }
            foreach (var pin in display.Pins)
            {
                if (!MaterialPins.TryGetValue(pin.PinId, out var item))
                { item = Marker(content.transform, pin.PinId, Color.cyan, 1.6f); MaterialPins.Add(pin.PinId, item); }
                item.localPosition = floatPoints[pin.PinId]; item.gameObject.SetActive(true);
            }
            foreach (var link in pose.Links)
            {
                if (!MaterialLinks.TryGetValue(link.LinkId, out var item))
                {
                    var color = link.Alternation == "inner" ? new Color(.3f, .93f, .78f) : new Color(.94f, .68f, .38f);
                    item = NewLine(content.transform, link.LinkId + " " + link.Alternation, color, 1.2f); item.positionCount = 2;
                    MaterialLinks.Add(link.LinkId, item); linkColors.Add(link.LinkId, color);
                }
                // Connectivity is supplied by the SDK's persistent endpoints, never inferred from geometric array order.
                item.SetPosition(0, MaterialPins[link.StartPinId].localPosition); item.SetPosition(1, MaterialPins[link.EndPinId].localPosition); item.gameObject.SetActive(true);
            }
            Highlight();
        }
        public void SelectMaterial(string id)
        { SelectedMaterialId = id; Highlight(); }
        private void Highlight()
        {
            foreach (var pair in MaterialPins) pair.Value.localScale = Vector3.one * (pair.Key == SelectedMaterialId ? 4 : 1.6f);
            foreach (var pair in MaterialLinks)
            {
                var selected = pair.Key == SelectedMaterialId; pair.Value.startWidth = pair.Value.endWidth = selected ? 2.7f : 1.2f;
                GearInvestPresentationMaterial.Apply(pair.Value, selected ? Color.yellow : linkColors[pair.Key]);
            }
        }
        public void HideCurrentMotion()
        {
            if (InputSprocket != null) InputSprocket.gameObject.SetActive(false);
            if (OutputSprocket != null) OutputSprocket.gameObject.SetActive(false);
            foreach (var item in MaterialPins.Values) item.gameObject.SetActive(false);
            foreach (var item in MaterialLinks.Values) item.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
        }
        private Transform Sprocket(string id, ExactVector3 center, Quaternion zero, RegularSprocketPitchDescriptor pitch, Color color, out Transform marker)
        {
            var t = new GameObject(id + " labeled rotating pitch polygon").transform; t.SetParent(content.transform, false); t.localPosition = V(center); t.localRotation = zero;
            var r = Finite(PitchChainDisplay.ApproximateRadius(pitch)); var polygon = NewLine(t, "ideal pitch polygon, not tooth solids", color, .7f);
            polygon.positionCount = pitch.ToothCount + 1;
            for (var i = 0; i <= pitch.ToothCount; i++) { var angle = i * 2f * Mathf.PI / pitch.ToothCount; polygon.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * r); }
            Line(t, "labeled tooth zero ray", Vector3.zero, Vector3.right * r, color, .7f);
            marker = Marker(t, "labeled tooth zero", Color.yellow, 3); marker.localPosition = Vector3.right * r;
            t.gameObject.SetActive(false); return t;
        }
        private static Transform Marker(Transform parent, string name, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * size;
            var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color); return go.transform;
        }
        private static LineRenderer NewLine(Transform parent, string name, Color color, float width)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); return line; }
        private static void Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        { var line = NewLine(parent, name, color, width); line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); }
        private static float WrappedTurns(Rational turns) => Number(new Rational(turns.Numerator % turns.Denominator, turns.Denominator));
        public static float Number(Rational value) => Finite(PitchChainDisplay.ApproximateScalar(value));
        public static Vector3 V(ExactVector3 value) => new Vector3(Number(value.X), Number(value.Y), Number(value.Z));
        public static Vector3 Point(PitchChainDisplayPoint value) => new Vector3(Finite(value.X), Finite(value.Y), Finite(value.Z));
        public static Quaternion Rotation(OrientedFrame value) => Quaternion.LookRotation(V(value.Z), V(value.Y));
        private static float Finite(double value)
        { var f = (float)value; if (float.IsNaN(f) || float.IsInfinity(f) || value != 0 && f == 0) throw new PitchChainDisplayUnavailableException("Display float is nonfinite or underflows; exact mechanism was not clamped."); return f; }
    }
}
