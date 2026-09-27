using System;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Owned, explicitly retained presentation shader; no runtime-only Shader.Find dependency.</summary>
    public sealed class GearInvestPresentationMaterial : MonoBehaviour
    {
        private Material owned;
        public static void Apply(Renderer renderer, Color color)
        {
            var shader = Resources.Load<Shader>("GearInvestPresentationUnlit");
            if (shader == null || (!shader.isSupported && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null))
                throw new InvalidOperationException("GearInvest presentation shader is missing or unsupported");
            var owner = renderer.GetComponent<GearInvestPresentationMaterial>();
            if (owner == null) owner = renderer.gameObject.AddComponent<GearInvestPresentationMaterial>();
            if (owner.owned == null) owner.owned = new Material(shader);
            owner.owned.color = color;
            renderer.sharedMaterial = owner.owned;
        }
        private void OnDestroy()
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
        }
    }
}
