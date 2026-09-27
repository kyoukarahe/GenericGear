using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Optional projection only. Saved mechanical frames and exact shaft channels own all physical directions.</summary>
    public sealed class GearInvestOrientedMechanismView : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> bodies = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> zeroFrames = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();
        private GameObject content;
        public OrientedMechanism Mechanism { get; private set; }
        public OrientedTwoOutputMechanism TwoOutputMechanism { get; private set; }
        public MechanicalDefinition DraftDefinition { get; private set; }
        public bool HasMechanism => Mechanism != null || TwoOutputMechanism != null;
        public bool IsDraftProjection => DraftDefinition != null;
        private IEnumerable<OrientedGearBody> shownBodies;
        private Func<Rational, IEnumerable<OrientedShaftEvaluation>> evaluate;
        public IReadOnlyDictionary<string, Transform> BodyTransforms => bodies;
        public Rational LastRoot { get; private set; }
        public Vector3 ViewCenter { get; private set; }
        public float ViewRadius { get; private set; }
        public void Clear()
        {
            if (content != null) { content.SetActive(false); Destroy(content); }
            foreach (var mesh in meshes) Destroy(mesh); meshes.Clear(); bodies.Clear(); zeroFrames.Clear(); baseColors.Clear(); Mechanism = null; TwoOutputMechanism = null; DraftDefinition = null; shownBodies = null; evaluate = null;
        }
        public void Build(OrientedMechanism mechanism)
        {
            if (!GearInvestSdk.CreateDefault().ValidateOrientedMechanism(mechanism).IsValid) throw new ArgumentException("Cannot display an invalid oriented mechanism.");
            Clear(); Mechanism = mechanism; evaluate = root => GearInvestSdk.CreateDefault().EvaluateOrientedMechanism(mechanism, root);
            BuildGraph(mechanism.Bodies, mechanism.Shafts, mechanism.Contacts.Select(c => new ViewContact(c.BodyAId, c.BodyBId, c.Cone)), mechanism.Ports);
        }
        public void Build(OrientedTwoOutputMechanism mechanism)
        {
            if (!GearInvestSdk.CreateDefault().ValidateOrientedTwoOutputMechanism(mechanism).IsValid) throw new ArgumentException("Cannot display an invalid two-output mechanism.");
            Clear(); TwoOutputMechanism = mechanism; evaluate = root => GearInvestSdk.CreateDefault().EvaluateOrientedTwoOutput(mechanism, root).Shafts;
            BuildGraph(mechanism.Bodies, mechanism.Shafts, mechanism.Contacts.Select(c => new ViewContact(c.BodyAId, c.BodyBId, c.Cone)), mechanism.Ports);
        }
        /// <summary>Current draft geometry, NOT a validated mechanism. The SDK callback omits undetermined shafts.
        /// Omitted channels retain a labelled reference pose, never a fabricated zero-speed solution.</summary>
        public void BuildDraft(MechanicalDefinition definition, Func<Rational, IEnumerable<OrientedShaftEvaluation>> knownChannels)
        {
            if (definition == null || knownChannels == null) throw new ArgumentNullException();
            Clear(); DraftDefinition = definition; evaluate = knownChannels;
            BuildGraph(definition.Bodies, definition.Shafts, definition.Contacts.Select(c => new ViewContact(c.BodyAId, c.BodyBId, c.Cone)), definition.Ports);
        }
        // Geometry-only projection record: never imports/revalidates old mechanics through a newer draft profile.
        private sealed class ViewContact
        {
            internal ViewContact(string a, string b, RightAnglePitchCone cone) { BodyAId = a; BodyBId = b; Cone = cone; }
            internal string BodyAId, BodyBId; internal RightAnglePitchCone Cone;
        }
        private void BuildGraph(IEnumerable<OrientedGearBody> gearBodies, IEnumerable<OrientedShaft> shafts, IEnumerable<ViewContact> contacts, IEnumerable<ShaftPort> ports)
        {
            shownBodies = gearBodies; content = new GameObject("Oriented mechanical truth"); content.transform.SetParent(transform, false);
            foreach (var body in gearBodies)
            {
                if (IsDraftProjection && (!body.MountingFrame.IsProperCardinal || body.OuterPitchRadius <= 0)) continue;
                var go = new GameObject(body.Id); go.transform.SetParent(content.transform, false); go.transform.localPosition = V(body.MountingFrame.Origin);
                go.transform.localRotation = Rotation(body.MountingFrame); bodies.Add(body.Id, go.transform); zeroFrames.Add(body.Id, go.transform.localRotation);
                var cone = contacts.FirstOrDefault(c => c.Cone != null && (c.BodyAId == body.Id || c.BodyBId == body.Id))?.Cone;
                var radius = N(body.OuterPitchRadius); var innerRadius = cone == null ? radius : radius * N(cone.InnerParameter);
                var innerCenter = cone == null ? .45f : N((cone.Apex + (body.MountingFrame.Origin - cone.Apex) * cone.InnerParameter - body.MountingFrame.Origin).Dot(body.MountingFrame.Z));
                var mesh = Frustum(radius, innerRadius, cone == null ? -.45f : 0, innerCenter); meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>();
                var color = (body.SourceModuleId == "pre" || body.SourceModuleId == "parallel") ? new Color(.28f, .72f, .9f) : (body.SourceModuleId == "post" || body.SourceModuleId == "turned") ? new Color(.5f, .8f, .4f) : body.Id.EndsWith("pinion", StringComparison.Ordinal) ? new Color(.95f, .55f, .18f) : new Color(.72f, .4f, .85f);
                GearInvestPresentationMaterial.Apply(renderer, color);
                LocalLine(go.transform, "zero-angle-meridian", new Vector3(radius, 0, cone == null ? -.65f : 0), new Vector3(innerRadius, 0, innerCenter), Color.white, .6f);
                var markerZ = cone == null ? -.65f : 0;
                LocalLine(go.transform, "radial-phase-marker", new Vector3(0, 0, markerZ), new Vector3(radius, 0, markerZ), Color.white, .6f);
                Ring(go.transform, radius, cone == null ? -.55f : 0, Color.white); Ring(go.transform, innerRadius, innerCenter, color * .7f);
            }
            foreach (var shaft in shafts)
            {
                if (IsDraftProjection && !shaft.Frame.IsProperCardinal) continue;
                var stations = gearBodies.Where(b => b.ShaftId == shaft.Id).Select(b => b.MountingFrame.Origin)
                    .Concat(ports.Where(p => p.ShaftId == shaft.Id).Select(p => p.Frame.Origin)).Concat(new[] { shaft.Frame.Origin }).ToArray();
                var parameters = stations.Select(p => N((p - shaft.Frame.Origin).Dot(shaft.Frame.Z))).ToArray();
                var start = V(shaft.Frame.Origin) + V(shaft.Frame.Z) * (parameters.Min() - 6); var tip = V(shaft.Frame.Origin) + V(shaft.Frame.Z) * (parameters.Max() + 9);
                WorldLine(shaft.Id + "/axis", start, tip, Color.white, .4f);
                WorldLine(shaft.Id + "/positive-arrow-a", tip, tip - V(shaft.Frame.Z) * 4 + V(shaft.Frame.X) * 2, Color.white, .6f);
                WorldLine(shaft.Id + "/positive-arrow-b", tip, tip - V(shaft.Frame.Z) * 4 - V(shaft.Frame.X) * 2, Color.white, .6f);
            }
            foreach (var contact in contacts.Where(c => c.Cone != null))
            {
                Dot("common-apex", V(contact.Cone.Apex), Color.magenta, 1.3f);
                WorldLine("intended-contact-generator", V(contact.Cone.ContactAt(contact.Cone.InnerParameter)), V(contact.Cone.OuterContact), Color.yellow, .9f);
                WorldLine("apex-to-contact-reference", V(contact.Cone.Apex), V(contact.Cone.OuterContact), new Color(.65f, .55f, .2f), .2f);
            }
            foreach (var port in ports) Dot(port.Id + "/station", V(port.Frame.Origin), Color.cyan, 1.4f);
            var renderers = content.GetComponentsInChildren<Renderer>(); var bounds = renderers.Length == 0 ? new Bounds(Vector3.zero, Vector3.one * 20) : renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            foreach (var renderer in renderers) if (renderer.sharedMaterial != null) baseColors[renderer] = renderer.sharedMaterial.color;
            ViewCenter = bounds.center; ViewRadius = Mathf.Max(bounds.extents.magnitude, 10); Apply(Rational.Zero);
        }
        public void Apply(Rational rootTurns)
        {
            if (!HasMechanism && !IsDraftProjection) return;
            var frame = evaluate(rootTurns).ToDictionary(v => v.ShaftId, StringComparer.Ordinal);
            foreach (var body in shownBodies)
            {
                if (!bodies.ContainsKey(body.Id)) continue;
                if (!frame.TryGetValue(body.ShaftId, out var value))
                {
                    if (!IsDraftProjection) throw new InvalidOperationException("Validated mechanism lost a required channel.");
                    bodies[body.Id].localRotation = zeroFrames[body.Id];
                    continue;
                }
                // Rotate around the stored world shaft axis, not world Z followed by a display correction.
                var turns = value.Turns; var normalized = new Rational(turns.Numerator % turns.Denominator, turns.Denominator);
                bodies[body.Id].localRotation = Quaternion.AngleAxis(360f * N(normalized), V(value.PositiveAxis)) * zeroFrames[body.Id];
            }
            LastRoot = rootTurns;
        }
        public void Highlight(IEnumerable<MechanicalReference> references)
        {
            if (shownBodies == null) return;
            var keys = new HashSet<string>(references.Select(r => r.Key), StringComparer.Ordinal);
            // Display binding only: expand a selected declaration to its already-named mounted objects.
            // This is not connectivity traversal, motion propagation or mechanical analysis.
            if (DraftDefinition != null)
            {
                foreach (var contact in DraftDefinition.Contacts.Where(c => keys.Contains("Contact/" + c.Id)))
                { keys.Add("Body/" + contact.BodyAId); keys.Add("Body/" + contact.BodyBId); }
                // Only the explicitly current/after side is drawn; never project former IDs onto current geometry.
                foreach (var output in DraftDefinition.Outputs.Where(o => (keys.Contains("Output/" + o.Key) || keys.Contains("AfterOutput/" + o.Key)) && o.IsResolved))
                    keys.Add("Body/" + output.BodyId);
                foreach (var port in DraftDefinition.Ports.Where(p => keys.Contains("Port/" + p.Id) || keys.Contains("AfterPort/" + p.Id)))
                    keys.Add("Shaft/" + port.ShaftId);
            }
            var highlight = new Color(1f, .2f, .3f);
            foreach (var entry in baseColors) if (entry.Key != null) GearInvestPresentationMaterial.Apply(entry.Key, entry.Value);
            foreach (var body in shownBodies)
            {
                if (!bodies.TryGetValue(body.Id, out var shown)) continue;
                bool selected = keys.Contains("Body/" + body.Id) || keys.Contains("Shaft/" + body.ShaftId);
                if (selected) foreach (var renderer in shown.GetComponentsInChildren<Renderer>()) GearInvestPresentationMaterial.Apply(renderer, highlight);
            }
            foreach (var key in keys.Where(k => k.StartsWith("Shaft/", StringComparison.Ordinal)))
                foreach (var renderer in baseColors.Keys.Where(r => r != null && (r.gameObject.name == key.Substring(6) + "/axis" ||
                    r.gameObject.name == key.Substring(6) + "/positive-arrow-a" || r.gameObject.name == key.Substring(6) + "/positive-arrow-b")))
                    GearInvestPresentationMaterial.Apply(renderer, highlight);
        }
        public static float N(Rational value)
        {
            var number = (double)value.Numerator / (double)value.Denominator;
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 1e8) throw new ArgumentException("Oriented presentation magnitude exceeds 1e8; mechanical exact data was not clamped.");
            return (float)number;
        }
        public static Vector3 V(ExactVector3 value) => new Vector3(N(value.X), N(value.Y), N(value.Z));
        public static Quaternion Rotation(OrientedFrame frame) => Quaternion.LookRotation(V(frame.Z), V(frame.Y));
        private static Mesh Frustum(float outer, float inner, float outerZ, float innerZ)
        {
            // Pitch surfaces are schematic two-sided sheets, not opaque tooth solids. Coordinate reversal must not cull a physically unchanged cone.
            const int segments = 64; var vertices = new Vector3[segments * 4]; var triangles = new int[segments * 12];
            for (int i = 0; i < segments; i++) { float a = i * Mathf.PI * 2 / segments; vertices[i] = new Vector3(Mathf.Cos(a) * outer, Mathf.Sin(a) * outer, outerZ); vertices[i + segments] = new Vector3(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner, innerZ); int j = (i + 1) % segments; int k = i * 6; triangles[k] = i; triangles[k + 1] = j; triangles[k + 2] = i + segments; triangles[k + 3] = j; triangles[k + 4] = j + segments; triangles[k + 5] = i + segments; }
            Array.Copy(vertices, 0, vertices, segments * 2, segments * 2);
            for (int i = 0; i < segments * 6; i += 3) { int k = segments * 6 + i; triangles[k] = triangles[i] + segments * 2; triangles[k + 1] = triangles[i + 2] + segments * 2; triangles[k + 2] = triangles[i + 1] + segments * 2; }
            var mesh = new Mesh { vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void Ring(Transform parent, float radius, float z, Color color)
        {
            var go = new GameObject("pitch-ring"); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true; line.positionCount = 64; line.startWidth = line.endWidth = .3f;
            for (int i = 0; i < 64; i++) { var angle = i * Mathf.PI * 2 / 64; line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z)); } GearInvestPresentationMaterial.Apply(line, color);
        }
        private void WorldLine(string name, Vector3 a, Vector3 b, Color color, float width) => LocalLine(content.transform, name, a, b, color, width);
        private static void LocalLine(Transform parent, string name, Vector3 a, Vector3 b, Color color, float width)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = line.endWidth = width; GearInvestPresentationMaterial.Apply(line, color); }
        private void Dot(string name, Vector3 point, Color color, float radius)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(content.transform, false); go.transform.localPosition = point; go.transform.localScale = Vector3.one * radius; Destroy(go.GetComponent<Collider>()); GearInvestPresentationMaterial.Apply(go.GetComponent<Renderer>(), color); }
        private void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Destroy(mesh); }
    }
}
