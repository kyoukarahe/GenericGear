using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional ideal pitch-line display. Only SDK evaluation moves materials; the SDK display sampler owns the belt route approximation.</summary>
    public sealed class GearInvestOpenBeltView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform InputPulley { get; private set; }
        public Transform OutputPulley { get; private set; }
        public Transform InputMarker { get; private set; }
        public Transform OutputMarker { get; private set; }
        public Transform[] FixedTangencies { get; private set; } = Array.Empty<Transform>();
        public Transform[] BeltMaterialMarkers { get; private set; } = Array.Empty<Transform>();
        public LineRenderer BeltLine { get; private set; }
        public OpenBeltDisplayRoute DisplayRoute { get; private set; }
        public bool HasCurrentOutputPose => OutputPulley != null && OutputPulley.gameObject.activeSelf;
        public bool HasCurrentBeltMaterial => BeltMaterialMarkers.Length > 0 && BeltMaterialMarkers[0].gameObject.activeSelf;
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        private GameObject content;
        private OpenBeltDraft draft;
        private Quaternion inputZero, outputZero;

        public void Clear()
        {
            if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; draft = null; SourceView = null; InputPulley = OutputPulley = InputMarker = OutputMarker = null;
            FixedTangencies = BeltMaterialMarkers = Array.Empty<Transform>(); BeltLine = null; DisplayRoute = null;
        }
        private void OnDestroy() { Clear(); }
        public void Build(OpenBeltDraft current, OpenBeltAnalysis analysis)
        {
            Clear(); draft = current; content = new GameObject("Open belt: fixed pitch route / separately moving materials"); content.transform.SetParent(transform, false);
            var d = current.Definition; var c = d.Device; var mapping = d.SourceMapping;
            if (mapping != null && mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Complete retained source in explicit mm mapping").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(d.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(analysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(mapping.PoseMm.Origin); SourceView.transform.localRotation = Rotation(mapping.PoseMm);
                SourceView.transform.localScale = Vector3.one * Number(mapping.MillimetersPerSourceUnit);
                var input = d.Source.Definition.Shafts.FirstOrDefault(s => s.Id == c.InputShaftId);
                if (input != null && input.Frame.IsProperCardinal && c.InputPitchRadius.Kind == QuantityKind.LinearPosition && c.InputPitchRadius.Value > 0)
                {
                    var mapped = mapping.FrameMm(input.Frame); inputZero = Rotation(mapped);
                    InputPulley = Pulley(c.InputPulleyBodyId, c.InputPulleyCenterMm, mapped, c.InputPitchRadius.Value, new Color(1, .68f, .2f), out var marker);
                    InputMarker = marker;
                    Line(content.transform, "retained shaft to separate input pulley", V(mapped.Origin), V(c.InputPulleyCenterMm), new Color(.7f, .7f, .85f), .55f);
                }
            }
            if (c.OutputShaft.Frame.IsProperCardinal && c.OutputPitchRadius.Kind == QuantityKind.LinearPosition && c.OutputPitchRadius.Value > 0 && c.OutputPulleyStation.Kind == QuantityKind.LinearPosition)
            {
                outputZero = Rotation(c.OutputShaft.Frame);
                OutputPulley = Pulley(c.OutputPulleyBodyId, c.OutputPulleyCenterMm, c.OutputShaft.Frame, c.OutputPitchRadius.Value, new Color(.45f, .88f, .6f), out var marker);
                OutputMarker = marker;
                var center = V(c.OutputPulleyCenterMm); var axis = V(c.OutputShaft.Frame.Z);
                Line(content.transform, "added output positive shaft axis", center - axis * 9, center + axis * 13, Color.white, .5f);
                Line(content.transform, "output axis arrow", center + axis * 13, center + axis * 8 + V(c.OutputShaft.Frame.X) * 2, Color.white, .5f);
            }
            if (analysis.LocalCompatibility.Route != null)
            {
                DisplayRoute = OpenBeltDisplay.Approximate(analysis.LocalCompatibility.Route);
                var loop = DisplayRoute.SampleLoop(192); BeltLine = NewLine(content.transform, "fixed SDK sampled open route; not a rotating pulley child",
                    analysis.LocalCompatibility.IsAdmitted ? new Color(.45f, .95f, .82f) : new Color(1, .43f, .24f), 1f);
                BeltLine.positionCount = loop.Count;
                for (var i = 0; i < loop.Count; i++) BeltLine.SetPosition(i, Point(loop[i]));
                BeltLine.gameObject.SetActive(c.TransmissionPresent);
                var points = new[] { DisplayRoute.P1plus, DisplayRoute.P2plus, DisplayRoute.P2minus, DisplayRoute.P1minus };
                FixedTangencies = points.Select((p, i) => { var t = Marker(content.transform, "fixed tangency " + i, Color.cyan, 2f); t.localPosition = Point(p); return t; }).ToArray();
                BeltMaterialMarkers = Enumerable.Range(0, 12).Select(i =>
                { var t = Marker(content.transform, "belt material mark " + i, new Color(1, .85f, .3f), 2.1f); t.localPosition = Point(DisplayRoute.SampleDistance(DisplayRoute.LoopLengthMm * i / 12)); t.gameObject.SetActive(false); return t; }).ToArray();
            }
            var renderers = content.GetComponentsInChildren<Renderer>(true); var bounds = renderers.Length == 0 ? new Bounds(Vector3.zero, Vector3.one * 50) : renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            ViewCenter = bounds.center; ViewRadius = Mathf.Max(30, bounds.extents.magnitude);
        }
        public void Apply(OpenBeltEvaluation evaluation)
        {
            if (draft == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.Input.Value); }
            var input = evaluation.Rotary.FirstOrDefault(r => r.ShaftId == draft.Definition.Device.InputShaftId);
            if (InputPulley != null)
            {
                InputPulley.gameObject.SetActive(input != null);
                if (input != null) InputPulley.localRotation = Quaternion.AngleAxis(360f * WrappedTurns(input.Turns), V(input.PositiveAxis)) * inputZero;
            }
            var output = evaluation.Output; var successful = evaluation.Status == OpenBeltEvaluationStatus.Success && output != null;
            if (OutputPulley != null)
            {
                OutputPulley.gameObject.SetActive(successful);
                if (successful) OutputPulley.localRotation = Quaternion.AngleAxis(360f * WrappedTurns(output.ShaftTurns.Value), V(output.ShaftPositiveAxis)) * outputZero;
            }
            for (var i = 0; i < BeltMaterialMarkers.Length; i++)
            {
                BeltMaterialMarkers[i].gameObject.SetActive(successful && DisplayRoute != null);
                if (successful && DisplayRoute != null)
                    BeltMaterialMarkers[i].localPosition = Point(DisplayRoute.SampleMaterialMark(output.BeltMaterialTravelPiCoefficientMm, new Rational(i, BeltMaterialMarkers.Length)));
            }
            // Tangencies, outlines and span/arc route never inherit either material transform.
        }
        public void HideCurrentMotion()
        {
            if (InputPulley != null) InputPulley.gameObject.SetActive(false);
            if (OutputPulley != null) OutputPulley.gameObject.SetActive(false);
            foreach (var marker in BeltMaterialMarkers) marker.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
        }
        private Transform Pulley(string id, ExactVector3 center, OrientedFrame frame, Rational radius, Color color, out Transform marker)
        {
            var fixedCircle = new GameObject(id + " fixed pitch-circle reference").transform; fixedCircle.SetParent(content.transform, false);
            fixedCircle.localPosition = V(center); fixedCircle.localRotation = Rotation(frame);
            var r = Number(radius); var outline = NewLine(fixedCircle, "ground fixed pitch outline", color, .7f); outline.positionCount = 97;
            for (var i = 0; i < outline.positionCount; i++) { var a = i * 2f * Mathf.PI / 96; outline.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * r); }
            var material = new GameObject(id + " rotating material; hidden when undetermined").transform; material.SetParent(content.transform, false);
            material.localPosition = V(center); material.localRotation = Rotation(frame);
            Line(material, "pulley material zero ray", Vector3.zero, Vector3.right * r, color, .7f);
            marker = Marker(material, "pulley rotating material mark", Color.yellow, 3); marker.localPosition = Vector3.right * r;
            material.gameObject.SetActive(false); return material;
        }
        private static Transform Marker(Transform parent, string name, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * size;
            var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color); return go.transform;
        }
        private static LineRenderer NewLine(Transform parent, string name, Color color, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); return line;
        }
        private static void Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        { var line = NewLine(parent, name, color, width); line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); }
        private static float WrappedTurns(Rational turns) => Number(new Rational(turns.Numerator % turns.Denominator, turns.Denominator));
        public static float Number(Rational value) => Finite(OpenBeltDisplay.ApproximateScalar(value));
        public static Vector3 V(ExactVector3 value) => new Vector3(Number(value.X), Number(value.Y), Number(value.Z));
        public static Vector3 Point(OpenBeltDisplayPoint value) => new Vector3(Finite(value.X), Finite(value.Y), Finite(value.Z));
        public static Quaternion Rotation(OrientedFrame value) => Quaternion.LookRotation(V(value.Z), V(value.Y));
        private static float Finite(double value)
        { var f = (float)value; if (float.IsNaN(f) || float.IsInfinity(f) || value != 0 && f == 0) throw new OpenBeltDisplayUnavailableException("Display float is nonfinite or underflows; exact mechanism was not clamped."); return f; }
    }
}
