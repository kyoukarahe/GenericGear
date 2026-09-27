using System;
using GearInvest.Core;
using GearInvest.Layout;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Approximate display of an already computed exact proof. No predicate, physical collision or geometry authority here.</summary>
    public sealed class GearInvestPitchPairView : MonoBehaviour
    {
        private GameObject content;
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        public bool HasPair => content != null;
        public void Clear() { if (content != null) { content.SetActive(false); Destroy(content); content = null; } }
        public void Build(PitchPairProof proof)
        {
            Clear(); content = new GameObject("Exact pitch proof presentation"); content.transform.SetParent(transform, false);
            var lo = Vector3.Min(V(proof.A.Envelope.Min), V(proof.B.Envelope.Min)); var hi = Vector3.Max(V(proof.A.Envelope.Max), V(proof.B.Envelope.Max));
            ViewCenter = (lo + hi) * .5f; ViewRadius = Mathf.Max(5, (hi - lo).magnitude * .65f);
            Shape(proof.A, new Color(.3f, .65f, .95f)); Shape(proof.B, new Color(.95f, .65f, .2f));
            Box(proof.A.Envelope, new Color(.45f, .55f, .65f)); Box(proof.B.Envelope, new Color(.65f, .55f, .4f));
            var disk = proof.A.Kind == PitchShapeKind.ClosedDisk ? proof.A : proof.B.Kind == PitchShapeKind.ClosedDisk ? proof.B : null;
            var cone = proof.A.Kind == PitchShapeKind.FiniteLateralCone ? proof.A : proof.B.Kind == PitchShapeKind.FiniteLateralCone ? proof.B : null;
            if (disk != null && cone != null && disk.Direction.Cross(cone.Direction) == ExactVector3.Zero)
            {
                var station = cone.Direction.Dot(disk.Center - cone.Center); var t = station / cone.Height;
                if (t >= cone.InnerParameter && t <= 1) Circle(V(cone.Center + cone.Direction * station), V(cone.Direction), N(t * cone.Radius), new Color(.95f, .35f, .95f), 1.8f);
            }
            if (proof.Witness.HasValue)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere); dot.name = "Exact rational witness (display marker has thickness)"; dot.transform.SetParent(content.transform, false);
                dot.transform.localPosition = V(proof.Witness.Value); dot.transform.localScale = Vector3.one * Mathf.Max(.5f, ViewRadius * .025f); Destroy(dot.GetComponent<Collider>()); GearInvestPresentationMaterial.Apply(dot.GetComponent<Renderer>(), Color.green);
            }
        }
        private void Shape(PitchShape shape, Color color)
        {
            if (shape.Kind == PitchShapeKind.ClosedAabb) { Box(shape.Box, color, 1.5f); return; }
            var normal = V(shape.Direction); var center = V(shape.Center); var r = N(shape.Radius);
            if (shape.Kind == PitchShapeKind.ClosedDisk)
            {
                var vertices = new Vector3[65]; var triangles = new int[64 * 6]; var rotation = Quaternion.FromToRotation(Vector3.forward, normal); vertices[0] = center;
                for (var i = 0; i < 64; i++) vertices[i + 1] = center + rotation * new Vector3(Mathf.Cos(i * Mathf.PI / 32) * r, Mathf.Sin(i * Mathf.PI / 32) * r, 0);
                for (var i = 0; i < 64; i++) { var j = i * 6; triangles[j] = 0; triangles[j + 1] = i + 1; triangles[j + 2] = (i + 1) % 64 + 1; triangles[j + 3] = 0; triangles[j + 4] = triangles[j + 2]; triangles[j + 5] = i + 1; }
                var go = new GameObject(shape.Id + " filled zero-thickness disk", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(content.transform, false);
                var mesh = new Mesh { vertices = vertices, triangles = triangles }; mesh.RecalculateBounds(); go.GetComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<PitchDisplayMeshOwner>();
                GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color * .7f); Circle(center, normal, r, color, 1.3f); return;
            }
            var outer = center + normal * N(shape.Height); var inner = center + normal * N(shape.Height * shape.InnerParameter); var ir = r * N(shape.InnerParameter);
            Circle(outer, normal, r, color, 1.3f); Circle(inner, normal, ir, color, 1.3f);
            var basis = Quaternion.FromToRotation(Vector3.forward, normal);
            for (var i = 0; i < 16; i++) { var direction = basis * new Vector3(Mathf.Cos(i * Mathf.PI / 8), Mathf.Sin(i * Mathf.PI / 8), 0); Line(new[] { outer + direction * r, inner + direction * ir }, color, .7f); }
        }
        private void Circle(Vector3 center, Vector3 normal, float radius, Color color, float width)
        {
            var points = new Vector3[65]; var basis = Quaternion.FromToRotation(Vector3.forward, normal);
            for (var i = 0; i <= 64; i++) points[i] = center + basis * new Vector3(Mathf.Cos(i * Mathf.PI / 32) * radius, Mathf.Sin(i * Mathf.PI / 32) * radius, 0);
            Line(points, color, width);
        }
        private void Box(ExactEnvelope3 box, Color color, float width = .55f)
        {
            var lo = V(box.Min); var hi = V(box.Max); var points = new Vector3[8];
            for (var i = 0; i < 8; i++) points[i] = new Vector3((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z);
            for (var i = 0; i < 8; i++) foreach (var bit in new[] { 1, 2, 4 }) if ((i & bit) == 0) Line(new[] { points[i], points[i | bit] }, color, width);
        }
        private void Line(Vector3[] points, Color color, float width)
        {
            var go = new GameObject("proof display line"); go.transform.SetParent(content.transform, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.positionCount = points.Length; line.SetPositions(points); line.startWidth = line.endWidth = Mathf.Max(.02f, ViewRadius * width / 100); GearInvestPresentationMaterial.Apply(line, color);
        }
        private static Vector3 V(ExactVector3 v) => GearInvestOrientedMechanismView.V(v);
        private static float N(Rational r) => (float)((double)r.Numerator / (double)r.Denominator);
    }
    internal sealed class PitchDisplayMeshOwner : MonoBehaviour
    {
        private void OnDestroy() { var filter = GetComponent<MeshFilter>(); if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh); }
    }
}
