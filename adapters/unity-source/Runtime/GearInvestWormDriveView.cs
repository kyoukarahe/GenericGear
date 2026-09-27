using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional schematic pitch-cylinder/helix display. Exact admission and material samples come from the SDK.</summary>
    public sealed class GearInvestWormDriveView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform WormBody { get; private set; }
        public Transform WheelBody { get; private set; }
        public Transform WormMarker { get; private set; }
        public Transform WheelMarker { get; private set; }
        public Transform PitchPoint { get; private set; }
        public Dictionary<string, LineRenderer> WormHelices { get; } = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);
        public Dictionary<string, LineRenderer> WheelTraces { get; } = new Dictionary<string, LineRenderer>(StringComparer.Ordinal);
        public bool HasCurrentOutputPose => WheelBody != null && WheelBody.gameObject.activeSelf;
        public bool HasCurrentMaterial => WormHelices.Count > 0 && WormHelices.Values.First().gameObject.activeSelf;
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        private GameObject content;
        private WormDriveDraft draft;
        private WormDriveAnalysis analysis;
        private Quaternion inputZero, outputZero;
        public void Clear()
        {
            if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; analysis = null; SourceView = null;
            WormBody = WheelBody = WormMarker = WheelMarker = PitchPoint = null;
            WormHelices.Clear(); WheelTraces.Clear();
        }
        private void OnDestroy() { Clear(); }
        public void Build(WormDriveDraft current, WormDriveAnalysis currentAnalysis)
        {
            Clear(); draft = current; analysis = currentAnalysis;
            content = new GameObject("Ideal cylindrical worm, separate wheel and complete retained source"); content.transform.SetParent(transform, false);
            var d = draft.Definition; var c = d.Device; var mapping = d.SourceMapping; var g = analysis.LocalCompatibility.Geometry;
            if (mapping != null && mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Complete actual source with explicit mm mapping").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(d.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(mapping.PoseMm.Origin); SourceView.transform.localRotation = Rotation(mapping.PoseMm);
                SourceView.transform.localScale = Vector3.one * Number(mapping.MillimetersPerSourceUnit);
                var input = d.Source.Definition.Shafts.FirstOrDefault(s => s.Id == c.InputShaftId);
                if (input != null && input.Frame.IsProperCardinal && g != null)
                {
                    var frame = mapping.FrameMm(input.Frame);
                    inputZero = Rotation(frame) * Quaternion.AngleAxis(360 * Wrapped(c.InputMountingPhase.Value), Vector3.forward);
                    WormBody = Body(c.InputWormBodyId, g.InputPitchCenterMm, inputZero, g.WormPitchRadiusMm, true, new Color(1, .66f, .23f));
                    WormMarker = Marker(WormBody, "worm persistent material marker", Color.yellow, 2.8f);
                    Line(content.transform, "retained source shaft to DISTINCT worm body", V(frame.Origin), V(g.InputPitchCenterMm), new Color(.7f, .72f, .86f), .55f);
                    Axis("physical helix +n", g.InputPitchCenterMm, c.PhysicalHelixAxis, new Color(.4f, .72f, 1));
                }
            }
            if (g != null && c.OutputShaft.Frame.IsProperCardinal)
            {
                outputZero = Rotation(c.OutputShaft.Frame) * Quaternion.AngleAxis(360 * Wrapped(c.OutputMountingPhase.Value), Vector3.forward);
                WheelBody = Body(c.OutputWheelBodyId, g.OutputPitchCenterMm, outputZero, g.WheelPitchRadiusMm, false, new Color(.35f, .9f, .65f));
                WheelMarker = Marker(WheelBody, "wheel persistent material marker", Color.yellow, 3.2f);
                Axis("actual wheel positive axis", g.OutputPitchCenterMm, c.OutputShaft.Frame.Z, Color.white);
                Line(content.transform, "common normal b, fixed pitch centers", V(g.InputPitchCenterMm), V(g.OutputPitchCenterMm), new Color(.6f, .68f, .75f), .3f);
                PitchPoint = Marker(content.transform, "fixed reference pitch point Q (not flank contact proof)", Color.magenta, 2.8f);
                PitchPoint.localPosition = V(g.PitchPointMm);
            }
            var bounds = new Bounds(Vector3.zero, Vector3.one * 60);
            // Inactive pitch bodies do not have dependable current Renderer.bounds. Include their
            // actual local line geometry before the first evaluation activates the wheel.
            foreach (var renderer in content.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is LineRenderer line)
                {
                    for (var i = 0; i < line.positionCount; i++)
                        bounds.Encapsulate(line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i)));
                }
                else if (renderer.gameObject.activeInHierarchy) bounds.Encapsulate(renderer.bounds);
            }
            ViewCenter = bounds.center; ViewRadius = Mathf.Max(40, bounds.extents.magnitude);
        }
        public void Apply(WormDriveEvaluation evaluation)
        {
            if (draft == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.Input.Value); }
            var c = draft.Definition.Device; var input = evaluation.Rotary.FirstOrDefault(r => r.ShaftId == c.InputShaftId);
            if (WormBody != null)
            {
                WormBody.gameObject.SetActive(input != null);
                if (input != null && analysis.LocalCompatibility.InputPositiveAxis.HasValue) WormBody.localRotation = Quaternion.AngleAxis(360 * Wrapped(input.Turns), V(analysis.LocalCompatibility.InputPositiveAxis.Value)) * inputZero;
            }
            var success = evaluation.Status == WormDriveEvaluationStatus.Success && evaluation.Output != null;
            if (WheelBody != null)
            {
                WheelBody.gameObject.SetActive(success);
                if (success) WheelBody.localRotation = Quaternion.AngleAxis(360 * Wrapped(evaluation.Output.ShaftTurns.Value), V(evaluation.Output.ShaftPositiveAxis)) * outputZero;
            }
            foreach (var line in WormHelices.Values.Concat(WheelTraces.Values)) line.gameObject.SetActive(false);
            if (!success) return;
            var display = WormDriveDisplay.Approximate(analysis, evaluation);
            ApplyTraces(display.WormHelices, WormHelices, WormBody, new Color(1, .69f, .24f), .8f);
            ApplyTraces(display.WheelToothTraces, WheelTraces, WheelBody, new Color(.35f, 1, .78f), .55f);
            // World material samples are authoritative display recipes; body matrices are independently driven by actual shaft states.
            WormMarker.localPosition = WormBody.InverseTransformPoint(transform.TransformPoint(Point(display.WormMaterialMarker)));
            WheelMarker.localPosition = WheelBody.InverseTransformPoint(transform.TransformPoint(Point(display.WheelMaterialMarker)));
        }
        private void ApplyTraces(IEnumerable<WormDriveDisplayPolyline> samples, Dictionary<string, LineRenderer> lines, Transform body, Color color, float width)
        {
            foreach (var sample in samples)
            {
                if (!lines.TryGetValue(sample.Id, out var line)) { line = NewLine(body, sample.Id, color, width); lines.Add(sample.Id, line); }
                line.positionCount = sample.Points.Count;
                for (var i = 0; i < sample.Points.Count; i++) line.SetPosition(i, body.InverseTransformPoint(transform.TransformPoint(Point(sample.Points[i]))));
                line.gameObject.SetActive(true);
            }
        }
        public void HideCurrentMotion()
        {
            if (WormBody != null) WormBody.gameObject.SetActive(false);
            if (WheelBody != null) WheelBody.gameObject.SetActive(false);
            foreach (var line in WormHelices.Values.Concat(WheelTraces.Values)) line.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
        }
        private Transform Body(string id, ExactVector3 center, Quaternion zero, Rational radius, bool worm, Color color)
        {
            var t = new GameObject(id + (worm ? " schematic cylindrical pitch body" : " ideal wheel pitch cylinder; NOT spur flanks")).transform;
            t.SetParent(content.transform, false); t.localPosition = V(center); t.localRotation = zero;
            var r = Number(radius); var half = worm ? Mathf.Max(8, Finite(Math.PI * WormDriveDisplay.ApproximateScalar(analysis.LocalCompatibility.Geometry.LeadPiCoefficientMm))) : 2.5f;
            foreach (var z in new[] { -half, half })
            {
                var ring = NewLine(t, "pitch cylinder rim", color * .75f, .4f); ring.positionCount = 65;
                for (var i = 0; i <= 64; i++) { var angle = i * 2f * Mathf.PI / 64; ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, z)); }
            }
            for (var i = 0; i < 4; i++) { var a = i * Mathf.PI / 2; var p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0); Line(t, "cylinder material generator", p - Vector3.forward * half, p + Vector3.forward * half, color * .6f, .3f); }
            Line(t, "body material zero ray", Vector3.zero, Vector3.right * r, color, .55f);
            t.gameObject.SetActive(false); return t;
        }
        private void Axis(string name, ExactVector3 center, ExactVector3 direction, Color color)
        { var p = V(center); var a = V(direction); Line(content.transform, name, p - a * 18, p + a * 24, color, .55f); }
        private static Transform Marker(Transform parent, string name, Color color, float size)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * size; var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color); return go.transform; }
        private static LineRenderer NewLine(Transform parent, string name, Color color, float width)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); return line; }
        private static void Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        { var line = NewLine(parent, name, color, width); line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); }
        private static float Wrapped(Rational turns) => Number(new Rational(turns.Numerator % turns.Denominator, turns.Denominator));
        public static float Number(Rational value) => Finite(WormDriveDisplay.ApproximateScalar(value));
        public static Vector3 V(ExactVector3 value) => new Vector3(Number(value.X), Number(value.Y), Number(value.Z));
        public static Vector3 Point(WormDriveDisplayPoint value) => new Vector3(Finite(value.X), Finite(value.Y), Finite(value.Z));
        public static Quaternion Rotation(OrientedFrame frame) => Quaternion.LookRotation(V(frame.Z), V(frame.Y));
        private static float Finite(double value)
        { var f = (float)value; if (float.IsNaN(f) || float.IsInfinity(f) || value != 0 && f == 0) throw new WormDriveDisplayUnavailableException("Unity float projection is nonfinite or underflows; exact mechanism was not clamped."); return f; }
    }
}
