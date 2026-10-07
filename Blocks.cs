using System.Collections.Generic;
using FruktSharedLibrary.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>
    /// Blockout models: a few boxes and cylinders put together in code, until the guns get real models. Sizes are in
    /// metres, and every model points down +Z with the grip at the origin.
    /// </summary>
    internal sealed class Blocks
    {
        private static readonly Dictionary<Color, Material> Materials = new();
        private readonly GameObject _root;

        internal Blocks(string name)
        {
            _root = new GameObject(name);
            _root.SetActive(false);
            Object.DontDestroyOnLoad(_root);
        }

        internal Blocks Box(Vector3 center, Vector3 size, Color color, Vector3 rotation = default)
            => Part(PrimitiveType.Cube, center, size, color, rotation);

        /// <summary>A cylinder lying along Z (a barrel), <paramref name="length"/> long.</summary>
        internal Blocks Tube(Vector3 center, float diameter, float length, Color color)
            => Part(PrimitiveType.Cylinder, center, new Vector3(diameter, length / 2f, diameter), color, new Vector3(90f, 0f, 0f));

        internal Blocks Ball(Vector3 center, float diameter, Color color)
            => Part(PrimitiveType.Sphere, center, Vector3.one * diameter, color, default);

        internal GameObject Build() => _root;

        private Blocks Part(PrimitiveType type, Vector3 center, Vector3 size, Color color, Vector3 rotation)
        {
            var part = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(_root.transform, false);
            part.transform.localPosition = center;
            part.transform.localRotation = Quaternion.Euler(rotation);
            part.transform.localScale = size;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return this;
        }

        internal static Material MaterialFor(Color color)
        {
            if (!Materials.TryGetValue(color, out var material) || material == null)
            {
                material = Meshes.CreateMaterial(color: color);
                material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Materials[color] = material;
            }
            return material;
        }
    }
}
