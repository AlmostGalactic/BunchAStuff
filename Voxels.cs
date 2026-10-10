using System;
using System.Collections.Generic;
using FruktSharedLibrary.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>
    /// A model made of voxels, built in code and turned into one mesh with only the outside faces. The game's own
    /// guns are voxel meshes about one centimetre to a voxel, so these are made at the same size. Coordinates are in
    /// voxels: x across, y up, z forward along the barrel, and the model's origin (0, 0, 0) is where the hand holds it.
    /// </summary>
    internal sealed class Voxels
    {
        /// <summary>The size of one voxel in metres, the same as the game's guns.</summary>
        internal const float Size = 0.01f;

        /// <summary>Every colour comes in this many slightly different tones, so flat areas don't look printed on.</summary>
        private const int Tones = 3;
        private static readonly float[] ToneBrightness = { 0.93f, 1f, 1.07f };

        private readonly Dictionary<(int X, int Y, int Z), char> _cells = new();
        private readonly HashSet<(int X, int Y, int Z)> _hidden = new();
        private Func<int, int, int, bool> _hiddenWhere;
        private readonly Dictionary<char, int> _keyIndex = new();
        private readonly List<(Color Color, bool Glow)> _palette = new();

        // ------------------------------------------------------------ colours

        /// <summary>Gives a letter a colour. <paramref name="glow"/> makes it light up on its own.</summary>
        internal Voxels Key(char key, Color color, bool glow = false)
        {
            if (_keyIndex.TryGetValue(key, out int existing))
            {
                _palette[existing] = (color, glow);
                return this;
            }
            _keyIndex[key] = _palette.Count;
            _palette.Add((color, glow));
            return this;
        }

        internal static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        // ------------------------------------------------------------ shapes

        internal Voxels Set(int x, int y, int z, char key)
        {
            if (!_keyIndex.ContainsKey(key))
                throw new ArgumentException($"'{key}' has no colour.");
            _cells[(x, y, z)] = key;
            return this;
        }

        internal Voxels Clear(int x, int y, int z)
        {
            _cells.Remove((x, y, z));
            return this;
        }

        /// <summary>A solid block, both corners included.</summary>
        internal Voxels Box(int x0, int x1, int y0, int y1, int z0, int z1, char key)
        {
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                Set(x, y, z, key);
            return this;
        }

        /// <summary>Takes a block away.</summary>
        internal Voxels Carve(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                _cells.Remove((x, y, z));
            return this;
        }

        /// <summary>
        /// A block that leans: each row going down shifts the block along z by <paramref name="dzPerRow"/> voxels
        /// (the top row is at <paramref name="y1"/>). For grips and curved magazines.
        /// </summary>
        internal Voxels Lean(int x0, int x1, int y0, int y1, int z0, int z1, float dzPerRow, char key)
        {
            for (int y = Math.Max(y0, y1); y >= Math.Min(y0, y1); y--)
            {
                int shift = (int)Math.Round((Math.Max(y0, y1) - y) * dzPerRow);
                Box(x0, x1, y, y, z0 + shift, z1 + shift, key);
            }
            return this;
        }

        /// <summary>A cylinder along z (a barrel). <paramref name="inner"/> makes it hollow.</summary>
        internal Voxels Tube(float cx, float cy, int z0, int z1, float radius, char key, float inner = 0f)
        {
            int r = (int)Math.Ceiling(radius);
            for (int x = (int)Math.Floor(cx) - r - 1; x <= (int)Math.Ceiling(cx) + r + 1; x++)
            for (int y = (int)Math.Floor(cy) - r - 1; y <= (int)Math.Ceiling(cy) + r + 1; y++)
            {
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d > radius + 0.01f || (inner > 0f && d < inner - 0.01f))
                    continue;
                for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                    Set(x, y, z, key);
            }
            return this;
        }

        /// <summary>A cylinder along x (a dial, a wheel seen from the side).</summary>
        internal Voxels Disc(int x0, int x1, float cy, float cz, float radius, char key, float inner = 0f)
        {
            int r = (int)Math.Ceiling(radius);
            for (int y = (int)Math.Floor(cy) - r - 1; y <= (int)Math.Ceiling(cy) + r + 1; y++)
            for (int z = (int)Math.Floor(cz) - r - 1; z <= (int)Math.Ceiling(cz) + r + 1; z++)
            {
                float d = (float)Math.Sqrt((y - cy) * (y - cy) + (z - cz) * (z - cz));
                if (d > radius + 0.01f || (inner > 0f && d < inner - 0.01f))
                    continue;
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                    Set(x, y, z, key);
            }
            return this;
        }

        /// <summary>A cylinder along y (a can, a tank standing up).</summary>
        internal Voxels Post(float cx, float cz, int y0, int y1, float radius, char key, float inner = 0f)
        {
            int r = (int)Math.Ceiling(radius);
            for (int x = (int)Math.Floor(cx) - r - 1; x <= (int)Math.Ceiling(cx) + r + 1; x++)
            for (int z = (int)Math.Floor(cz) - r - 1; z <= (int)Math.Ceiling(cz) + r + 1; z++)
            {
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (d > radius + 0.01f || (inner > 0f && d < inner - 0.01f))
                    continue;
                for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                    Set(x, y, z, key);
            }
            return this;
        }

        internal Voxels Ball(float cx, float cy, float cz, float radius, char key)
        {
            int r = (int)Math.Ceiling(radius);
            for (int x = (int)Math.Floor(cx) - r - 1; x <= (int)Math.Ceiling(cx) + r + 1; x++)
            for (int y = (int)Math.Floor(cy) - r - 1; y <= (int)Math.Ceiling(cy) + r + 1; y++)
            for (int z = (int)Math.Floor(cz) - r - 1; z <= (int)Math.Ceiling(cz) + r + 1; z++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz) <= radius * radius + 0.01f)
                    Set(x, y, z, key);
            }
            return this;
        }

        /// <summary>A one-voxel line between two points.</summary>
        internal Voxels Rod(int x0, int y0, int z0, int x1, int y1, int z1, char key, int thickness = 1)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Max(Math.Abs(y1 - y0), Math.Abs(z1 - z0)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                int x = (int)Math.Round(x0 + (x1 - x0) * t);
                int y = (int)Math.Round(y0 + (y1 - y0) * t);
                int z = (int)Math.Round(z0 + (z1 - z0) * t);
                Box(x, x + thickness - 1, y, y + thickness - 1, z, z, key);
            }
            return this;
        }

        /// <summary>Paints every voxel inside a block that is already there (for panels, stripes and wear).</summary>
        internal Voxels Paint(int x0, int x1, int y0, int y1, int z0, int z1, char key)
        {
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
            {
                if (_cells.ContainsKey((x, y, z)))
                    _cells[(x, y, z)] = key;
            }
            return this;
        }

        /// <summary>Copies everything on the +x side onto the -x side, so a model only has to be built once.</summary>
        internal Voxels MirrorX()
        {
            foreach (var cell in new List<KeyValuePair<(int X, int Y, int Z), char>>(_cells))
            {
                if (cell.Key.X > 0)
                    _cells[(-cell.Key.X, cell.Key.Y, cell.Key.Z)] = cell.Value;
            }
            return this;
        }

        internal bool Has(int x, int y, int z) => _cells.ContainsKey((x, y, z));

        /// <summary>The colour letter at a voxel, or a space for none.</summary>
        internal char At(int x, int y, int z) => _cells.TryGetValue((x, y, z), out var key) ? key : (char)32;

        /// <summary>Every voxel there is.</summary>
        internal IEnumerable<(int X, int Y, int Z)> Cells => _cells.Keys;

        /// <summary>
        /// Replaces everything marked hidden with whatever the test says is filled, asked as the mesh is built. Faces
        /// against those cells are left out.
        /// </summary>
        internal Voxels HideWhere(Func<int, int, int, bool> filled)
        {
            _hidden.Clear();
            _hiddenWhere = filled;
            return this;
        }

        /// <summary>
        /// Marks a block as filled by something else (the body under a piece of clothing): nothing is drawn there,
        /// and faces against it are left out.
        /// </summary>
        internal Voxels Hide(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                _hidden.Add((x, y, z));
            return this;
        }

        internal int Count => _cells.Count;

        // ------------------------------------------------------------ the mesh

        private static readonly Dictionary<string, Material> Materials = new();

        /// <summary>The finished model: one object, one mesh, one material. Inactive, ready to be put in a hand.</summary>
        internal GameObject Build(string name)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var mesh = BuildMesh(name);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BuildMaterial(name);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return root;
        }

        // Shared by every build, so rebuilding a mesh allocates nothing new on the managed side.
        private static readonly List<Vector3> BuildVertices = new(), BuildNormals = new();
        private static readonly List<Vector2> BuildUvs = new();
        private static readonly List<int> BuildTriangles = new();

        /// <summary>
        /// The model as a mesh with only the outside faces. Given a mesh, it's filled again in place (for a model that
        /// changes, like torn clothes) instead of a new one being made.
        /// </summary>
        internal Mesh BuildMesh(string name, Mesh into = null)
        {
            var vertices = BuildVertices;
            var normals = BuildNormals;
            var uvs = BuildUvs;
            var triangles = BuildTriangles;
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            triangles.Clear();
            float width = _palette.Count * Tones;
            var hiddenWhere = _hiddenWhere;
            bool anyHidden = _hidden.Count > 0;

            foreach (var cell in _cells)
            {
                var (x, y, z) = cell.Key;
                int tone = ((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) & int.MaxValue;
                float u = (_keyIndex[cell.Value] * Tones + tone % Tones + 0.5f) / width;
                foreach (var face in Faces)
                {
                    int nx = x + face.Normal.x, ny = y + face.Normal.y, nz = z + face.Normal.z;
                    if (_cells.ContainsKey((nx, ny, nz)) || (anyHidden && _hidden.Contains((nx, ny, nz))) || (hiddenWhere != null && hiddenWhere(nx, ny, nz)))
                        continue;
                    int first = vertices.Count;
                    var normal = new Vector3(face.Normal.x, face.Normal.y, face.Normal.z);
                    foreach (var corner in face.Corners)
                    {
                        vertices.Add(new Vector3((x + corner.x - 0.5f) * Size, (y + corner.y - 0.5f) * Size, (z + corner.z - 0.5f) * Size));
                        normals.Add(normal);
                        uvs.Add(new Vector2(u, 0.5f));
                    }
                    triangles.Add(first);
                    triangles.Add(first + 1);
                    triangles.Add(first + 2);
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 3);
                }
            }

            var mesh = into != null ? into : new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal Material BuildMaterial(string name)
        {
            int width = _palette.Count * Tones;
            var colours = new Texture2D(width, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name + " colours" };
            var glow = new Texture2D(width, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name + " glow" };
            bool anyGlow = false;
            for (int i = 0; i < _palette.Count; i++)
            {
                for (int t = 0; t < Tones; t++)
                {
                    var c = _palette[i].Color;
                    float b = ToneBrightness[t];
                    var tinted = new Color(Mathf.Clamp01(c.r * b), Mathf.Clamp01(c.g * b), Mathf.Clamp01(c.b * b), 1f);
                    colours.SetPixel(i * Tones + t, 0, tinted);
                    glow.SetPixel(i * Tones + t, 0, _palette[i].Glow ? tinted : Color.black);
                    anyGlow |= _palette[i].Glow;
                }
            }
            colours.Apply();
            glow.Apply();
            colours.hideFlags = HideFlags.HideAndDontSave;
            glow.hideFlags = HideFlags.HideAndDontSave;

            var material = Meshes.CreateMaterial(colours, Color.white);
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            material.name = name;
            // Matte, like the game's own guns.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.12f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (anyGlow)
            {
                material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap", glow);
                material.SetColor("_EmissionColor", Color.white * 1.6f);
            }
            return material;
        }

        private readonly struct Face
        {
            internal Face(Vector3Int normal, params Vector3Int[] corners)
            {
                Normal = normal;
                Corners = corners;
            }

            internal Vector3Int Normal { get; }
            internal Vector3Int[] Corners { get; }
        }

        // Corners go round so that the cross product of the first two edges points along the normal, which is the
        // front of a face in Unity. They're written for a cube from 0 to 1 and moved half a voxel when the mesh is
        // made, so voxel (x, y, z) is centred on (x, y, z).
        private static readonly Face[] Faces = BuildFaces();

        private static Face[] BuildFaces()
        {
            Face Make(int nx, int ny, int nz, params int[][] corners)
            {
                var list = new Vector3Int[corners.Length];
                for (int i = 0; i < corners.Length; i++)
                    list[i] = new Vector3Int(corners[i][0], corners[i][1], corners[i][2]);
                return new Face(new Vector3Int(nx, ny, nz), list);
            }

            return new[]
            {
                Make(1, 0, 0, new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 }, new[] { 1, 0, 1 }),
                Make(-1, 0, 0, new[] { 0, 0, 1 }, new[] { 0, 1, 1 }, new[] { 0, 1, 0 }, new[] { 0, 0, 0 }),
                Make(0, 1, 0, new[] { 0, 1, 0 }, new[] { 0, 1, 1 }, new[] { 1, 1, 1 }, new[] { 1, 1, 0 }),
                Make(0, -1, 0, new[] { 0, 0, 1 }, new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 0, 1 }),
                Make(0, 0, 1, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 }, new[] { 0, 0, 1 }),
                Make(0, 0, -1, new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 1, 1, 0 }, new[] { 1, 0, 0 }),
            };
        }
    }
}
