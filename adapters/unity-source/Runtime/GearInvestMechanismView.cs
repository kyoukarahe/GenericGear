using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Layout;
using UnityEngine;

namespace GearInvest.Unity
{
    public sealed class GearInvestMechanismView : MonoBehaviour
    {
        [SerializeField]
        private float unitsPerCanonicalUnit = 0.1f;

        [SerializeField]
        private float layerHeight = 0.45f;

        [SerializeField]
        private float bodyThickness = 0.16f;

        private readonly Dictionary<string, GearInvestBodyView> _bodyViews =
            new Dictionary<string, GearInvestBodyView>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, GearInvestBodyView> BodyViews
        {
            get { return _bodyViews; }
        }

        public void Build(GearInvestArtifactSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }

            Clear();
            var spatial = session.Artifact.Candidate.Spatial;
            var axes = spatial.Axes.ToDictionary(axis => axis.Id, StringComparer.Ordinal);

            foreach (var body in spatial.Bodies)
            {
                SpatialAxis axis;
                if (!axes.TryGetValue(body.AxisId, out axis))
                {
                    throw new InvalidOperationException("Body references an unknown axis: " + body.AxisId);
                }

                CreateBody(body, axis);
            }
        }

        public bool TryGetBody(string bodyId, out GearInvestBodyView view)
        {
            return _bodyViews.TryGetValue(bodyId, out view);
        }

        public void Clear()
        {
            _bodyViews.Clear();
            for (var index = transform.childCount - 1; index >= 0; index--)
            {
                var child = transform.GetChild(index).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private void CreateBody(SpatialBody body, SpatialAxis axis)
        {
            var pitchRadius = (double)body.PitchRadius;
            var worldRadius = Mathf.Max(0.05f, (float)pitchRadius * unitsPerCanonicalUnit);
            var root = new GameObject("Body " + body.Id);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(
                (float)((double)axis.X * unitsPerCanonicalUnit),
                body.Layer * layerHeight,
                (float)((double)axis.Y * unitsPerCanonicalUnit));

            var view = root.AddComponent<GearInvestBodyView>();
            view.Initialize(body.Id, body.DofId, body.AxisId, body.Layer, body.ToothCount, pitchRadius);

            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.name = "Pitch circle";
            disk.transform.SetParent(root.transform, false);
            disk.transform.localScale = new Vector3(worldRadius * 2.0f, bodyThickness * 0.5f, worldRadius * 2.0f);
            RemoveCollider(disk);
            Tint(disk, ColorFor(body.DofId, body.Layer));

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Direction marker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(worldRadius * 0.5f, bodyThickness * 0.7f, 0.0f);
            marker.transform.localScale = new Vector3(
                worldRadius,
                bodyThickness * 0.35f,
                Mathf.Max(0.04f, worldRadius * 0.08f));
            RemoveCollider(marker);
            Tint(marker, Color.white);

            _bodyViews.Add(body.Id, view);
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        private static void Tint(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            GearInvestPresentationMaterial.Apply(renderer, color);
        }

        private static Color ColorFor(string dofId, int layer)
        {
            var colors = new[]
            {
                new Color(0.22f, 0.72f, 1.0f),
                new Color(1.0f, 0.58f, 0.18f),
                new Color(0.46f, 0.88f, 0.44f),
                new Color(0.86f, 0.42f, 0.88f),
            };
            var hash = 0;
            foreach (var character in dofId)
            {
                hash = ((hash * 31) + character) & 0x7fffffff;
            }

            var color = colors[hash % colors.Length];
            return layer == 0 ? color : Color.Lerp(color, Color.white, 0.22f);
        }
    }
}
