using System.Collections.Generic;
using FruktSharedLibrary.Interop;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>The flash of an explosion, until there are real particles.</summary>
    internal static class Effects
    {
        private sealed class Fading
        {
            public GameObject Object;
            public float Born, Life, Width;
        }

        private static readonly List<Fading> Live = new();

        /// <summary>A ball that swells and vanishes: an explosion, until there are real particles.</summary>
        internal static void Flash(Vector3 at, float size, Color color, float life = 0.25f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Bunch-A-Stuff flash";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Blocks.MaterialFor(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.position = at;
            go.transform.localScale = Vector3.one * size * 0.3f;
            Live.Add(new Fading { Object = go, Born = Time.time, Life = life, Width = size });
        }

        internal static void Update()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var fading = Live[i];
                float t = (Time.time - fading.Born) / fading.Life;
                if (!fading.Object.Exists() || t >= 1f)
                {
                    if (fading.Object.Exists())
                        Object.Destroy(fading.Object);
                    Live.RemoveAt(i);
                    continue;
                }
                fading.Object.transform.localScale = Vector3.one * fading.Width * Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(t)) * (t > 0.7f ? (1f - t) / 0.3f : 1f);
            }
        }

        internal static void Clear()
        {
            foreach (var fading in Live)
            {
                if (fading.Object.Exists())
                    Object.Destroy(fading.Object);
            }
            Live.Clear();
        }
    }
}
