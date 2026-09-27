using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Display only: SDK support-contour samples, separate persistent material and current contact markers.</summary>
    public sealed class GearInvestCamFollowerView : MonoBehaviour
    {
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public Transform CamBody { get; private set; }
        public Transform MaterialMarker { get; private set; }
        public Transform FollowerBody { get; private set; }
        public Transform FollowerReference { get; private set; }
        public Transform ContactMarker { get; private set; }
        public LineRenderer FaceLine { get; private set; }
        public LineRenderer ContourLine { get; private set; }
        public int ContourSampleCount { get; private set; }
        public bool IsRejectedGeometryPreview { get; private set; }
        public bool HasCurrentCam => CamBody != null && CamBody.gameObject.activeSelf;
        public bool HasCurrentConnectedPose => FollowerBody != null && FollowerBody.gameObject.activeSelf && ContactMarker != null && ContactMarker.gameObject.activeSelf;
        public Bounds ViewBounds { get; private set; }
        public Vector3 ViewCenter => ViewBounds.center;
        public float ViewRadius => Mathf.Max(30, ViewBounds.extents.magnitude);
        public string DisplayUnavailableReason { get; private set; }
        private GameObject content;
        private CamFollowerDraft draft;
        private CamFollowerAnalysis analysis;
        private Mesh camMesh;

        public void Clear()
        {
            if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            if (camMesh != null) Destroy(camMesh);
            content = null; camMesh = null; draft = null; analysis = null; SourceView = null;
            CamBody = MaterialMarker = FollowerBody = FollowerReference = ContactMarker = null;
            FaceLine = ContourLine = null; ContourSampleCount = 0; IsRejectedGeometryPreview = false; DisplayUnavailableReason = null;
        }
        private void OnDestroy() { Clear(); }

        public void Build(CamFollowerDraft current, CamFollowerAnalysis currentAnalysis, int contourSamples = 96)
        {
            if (contourSamples < 16 || contourSamples > 256) throw new ArgumentException("Display contour sample count must be16..256.");
            Clear(); draft = current; analysis = currentAnalysis;
            content = new GameObject("Retained source, SDK support contour and finite flat follower"); content.transform.SetParent(transform, false);
            var d = current.Definition.Device; var map = current.Definition.SourceMapping;
            var bounds = new Bounds(transform.TransformPoint(V(d.CenterMm)), Vector3.one * 20);
            if (map != null && map.PoseMm.IsProperCardinal && map.MillimetersPerSourceUnit > 0)
            {
                SourceView = new GameObject("Complete retained source with one mm mapping").AddComponent<GearInvestOrientedMechanismView>();
                SourceView.transform.SetParent(content.transform, false);
                SourceView.BuildDraft(current.Definition.Source.Definition, root => GearInvestSdk.CreateDefault().EvaluateMechanicalAnalysis(currentAnalysis.SourceAnalysis, root));
                SourceView.transform.localPosition = V(map.PoseMm.Origin); SourceView.transform.localRotation = Rotation(map.PoseMm);
                SourceView.transform.localScale = Vector3.one * Number(map.MillimetersPerSourceUnit);
            }
            var g = d.GuideFrameMm.Z; var f = d.PlaneNormal.Cross(g);
            if (currentAnalysis.LocalCompatibility.HasValidCamMounting && g.IsCardinal && f.IsCardinal && CamSupportGeometry.Validate(d.SupportProfile).Count == 0)
            {
                var points = new Vector3[contourSamples]; var available = true;
                for (var i = 0; i < points.Length; i++)
                {
                    // Point geometry is entirely SDK-owned. This consumer never differentiates h or interprets it as a radius.
                    var recipe = CamContourPointRecipe.Create(d.SupportProfile, new Rational(i, points.Length), 0, d.CenterMm, g, f);
                    var sample = CamFollowerNumerics.Evaluate(recipe);
                    if (!sample.IsAvailable || sample.Point == null) { available = false; DisplayUnavailableReason = sample.Status.ToString(); break; }
                    points[i] = Mid(sample.Point) - V(d.CenterMm);
                }
                if (available)
                {
                    IsRejectedGeometryPreview = !currentAnalysis.GeometryProof.IsProved;
                    CamBody = new GameObject(IsRejectedGeometryPreview ? "REJECTED / UNPROVED support contour preview" : d.CamBodyId).transform;
                    CamBody.SetParent(content.transform, false); CamBody.localPosition = V(d.CenterMm);
                    var color = IsRejectedGeometryPreview ? new Color(1, .25f, .45f) : new Color(1, .7f, .22f);
                    ContourLine = Line(CamBody, "SDK hN+kT contour", points[0], points[0], color, 1.5f);
                    ContourLine.positionCount = points.Length; ContourLine.SetPositions(points); ContourLine.loop = true; ContourSampleCount = points.Length;
                    // Convexity admission, not a local polygon repair, authorizes this display-only fan.
                    if (!IsRejectedGeometryPreview)
                    {
                        var fill = new GameObject("Convex display fan; not solid clearance"); fill.transform.SetParent(CamBody, false);
                        var vertices = new Vector3[points.Length + 1]; Array.Copy(points, 0, vertices, 1, points.Length);
                        var triangles = new int[points.Length * 6];
                        for (var i = 0; i < points.Length; i++)
                        {
                            var next = (i + 1) % points.Length + 1; var k = i * 6;
                            triangles[k] = 0; triangles[k + 1] = i + 1; triangles[k + 2] = next;
                            triangles[k + 3] = 0; triangles[k + 4] = next; triangles[k + 5] = i + 1;
                        }
                        camMesh = new Mesh { name = "SDK sampled convex support cam presentation" }; camMesh.vertices = vertices; camMesh.triangles = triangles; camMesh.RecalculateBounds(); camMesh.RecalculateNormals();
                        fill.AddComponent<MeshFilter>().sharedMesh = camMesh;
                        GearInvestPresentationMaterial.Apply(fill.AddComponent<MeshRenderer>(), new Color(.46f, .30f, .12f));
                    }
                    MaterialMarker = Marker(content.transform, "persistent cam material normal t=0", new Color(1, .32f, .87f), 3.3f);
                    if (currentAnalysis.LocalCompatibility.MappedShaftFrameMm != null)
                        Line(content.transform, "retained shaft to separate cam station", V(currentAnalysis.LocalCompatibility.MappedShaftFrameMm.Origin), V(d.CenterMm), new Color(.7f, .8f, 1), 1);
                    Marker(CamBody, "fixed cam center", Color.white, 3.2f);
                    // Global support provides |C|<=hMax only for admitted geometry. Rejected preview uses SDK sample bounds, labelled as display only.
                    var radius = currentAnalysis.GeometryProof.IsProved ? Number(currentAnalysis.Envelope.Support.MaximumHeightMm) : points.Max(p => p.magnitude);
                    foreach (var a in new[] { -1f, 1f }) foreach (var b in new[] { -1f, 1f })
                        bounds.Encapsulate(transform.TransformPoint(V(d.CenterMm) + radius * (a * V(g) + b * V(f))));
                    CamBody.gameObject.SetActive(false); MaterialMarker.gameObject.SetActive(false);
                }
            }
            if (d.GuidePresent && d.GuideFrameMm.IsProperCardinal && d.GuideTravel.Kind == QuantityKind.LinearPosition)
            {
                var low = V(d.GuideFrameMm.Origin + g * d.GuideTravel.Lower.Value); var high = V(d.GuideFrameMm.Origin + g * d.GuideTravel.Upper.Value);
                Line(content.transform, "grounded finite guide travel", low, high, new Color(.38f, .58f, .85f), 1);
                foreach (var p in new[] { low, high })
                {
                    Line(content.transform, "closed guide endpoint", p - V(f) * 5, p + V(f) * 5, new Color(.5f, .72f, 1), .9f);
                    bounds.Encapsulate(transform.TransformPoint(p));
                    if (d.FollowerFace.Kind == QuantityKind.LinearPosition)
                    {
                        bounds.Encapsulate(transform.TransformPoint(p + V(f) * Number(d.FollowerFace.Lower.Value)));
                        bounds.Encapsulate(transform.TransformPoint(p + V(f) * Number(d.FollowerFace.Upper.Value)));
                    }
                }
            }
            FollowerBody = new GameObject(d.FollowerBodyId + " fixed orientation finite flat face").transform; FollowerBody.SetParent(content.transform, false);
            FaceLine = Line(FollowerBody, "actual finite flat contact face", Vector3.zero, Vector3.zero, new Color(.3f, 1, .83f), 2.5f);
            FollowerReference = Marker(content.transform, d.FollowerReferenceId + " P", new Color(.2f, .65f, 1), 3.3f);
            ContactMarker = Marker(content.transform, "current material contact Q (not persistent)", Color.yellow, 2.5f);
            FollowerBody.gameObject.SetActive(false); FollowerReference.gameObject.SetActive(false); ContactMarker.gameObject.SetActive(false);
            foreach (var renderer in content.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is LineRenderer line) for (var i = 0; i < line.positionCount; i++) bounds.Encapsulate(line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i)));
                else bounds.Encapsulate(renderer.bounds);
            }
            bounds.Expand(16); ViewBounds = bounds;
        }

        public void Apply(CamFollowerEvaluation evaluation)
        {
            HideCurrentMotion(); if (draft == null) return;
            if (SourceView != null) { SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.RootTurns.Value); }
            var d = draft.Definition.Device;
            if (CamBody != null && evaluation.Cam?.PhysicalPhaseTurns is Rational phi)
            {
                CamBody.localPosition = V(d.CenterMm);
                CamBody.localRotation = Quaternion.AngleAxis(360 * Wrapped(phi), V(d.PlaneNormal));
                CamBody.gameObject.SetActive(true);
                // Independent cam material readback may remain available when the follower is released.
                var material = evaluation.MaterialNumeric;
                if (material == null)
                {
                    var g = d.GuideFrameMm.Z;
                    material = CamFollowerNumerics.Evaluate(CamContourPointRecipe.Create(d.SupportProfile, 0, phi, d.CenterMm, g, d.PlaneNormal.Cross(g)));
                }
                if (MaterialMarker != null && material.IsAvailable && material.Point != null)
                { MaterialMarker.localPosition = Mid(material.Point); MaterialMarker.gameObject.SetActive(true); }
            }
            // Never bypass admission through Recipe, DiagnosticPose or nested available numeric points.
            var pose = evaluation.Pose; if (pose == null) return;
            var p = V(pose.FollowerReferencePointMm); var f = V(evaluation.Recipe.Descriptor.InPlanePerpendicular);
            FollowerBody.localPosition = p; FollowerBody.localRotation = Quaternion.identity;
            FaceLine.SetPosition(0, f * Number(d.FollowerFace.Lower.Value)); FaceLine.SetPosition(1, f * Number(d.FollowerFace.Upper.Value));
            FollowerReference.localPosition = p; ContactMarker.localPosition = Mid(pose.ContactPointMm);
            FollowerBody.gameObject.SetActive(true); FollowerReference.gameObject.SetActive(true); ContactMarker.gameObject.SetActive(true);
        }
        public void HideCurrentMotion()
        {
            foreach (var t in new[] { CamBody, MaterialMarker, FollowerBody, FollowerReference, ContactMarker }) if (t != null) t.gameObject.SetActive(false);
            if (SourceView != null) SourceView.gameObject.SetActive(false);
        }
        private static Transform Marker(Transform parent, string name, Color color, float size)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; t.name = name; t.SetParent(parent, false); t.localScale = Vector3.one * size;
            var collider = t.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(t.GetComponent<Renderer>(), color); return t;
        }
        private static LineRenderer Line(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = line.endWidth = width;
            GearInvestPresentationMaterial.Apply(line, color); return line;
        }
        public static Vector3 Mid(CamVectorInterval v) => new Vector3(Number((v.X.Lower + v.X.Upper) / 2), Number((v.Y.Lower + v.Y.Upper) / 2), Number((v.Z.Lower + v.Z.Upper) / 2));
        public static Vector3 V(ExactVector3 v) => new Vector3(Number(v.X), Number(v.Y), Number(v.Z));
        public static Quaternion Rotation(OrientedFrame f) => Quaternion.LookRotation(V(f.Z), V(f.Y));
        public static float Number(Rational value)
        {
            // Scale numerator and denominator together before converting large exact values to display-only doubles.
            var n = value.Numerator; var d = value.Denominator;
            var digits = Math.Max(System.Numerics.BigInteger.Abs(n).ToString().Length, d.ToString().Length);
            if (digits > 250) { var divisor = System.Numerics.BigInteger.Pow(10, digits - 250); n /= divisor; d /= divisor; }
            var result = (float)((double)n / (double)d);
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new ArgumentException("Display approximation unavailable."); return result;
        }
        private static float Wrapped(Rational turns)
        { var n = turns.Numerator % turns.Denominator; if (n.Sign < 0) n += turns.Denominator; return Number(new Rational(n, turns.Denominator)); }
    }
}
