using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.Objects;
using Il2CppLVA.Creatures;
using Il2CppLVA.Limbs;
using Il2CppVoxelMeshGeneration.Painting;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BunchAStuff
{
    /// <summary>
    /// Bruises on people who get punched or hit something hard. A bruise is a cluster of uneven blotches that
    /// darkens from red to deep purple, then slowly turns blue, olive, green and yellow as it heals, the edges first,
    /// and fades away. Only the living bruise.
    /// </summary>
    internal static class Bruises
    {
        // One blotch of a bruise: where it sits (in bruise radii, across the skin), how big it is, how it's turned,
        // which shape it uses, how strong it is, and how far ahead of the middle it heals.
        private struct Blot
        {
            internal Vector2 Offset;
            internal float Size, Turn, Strength, Lead;
            internal int Shape;
        }

        private sealed class Bruise
        {
            internal AbstractLimb Limb;
            internal Transform On;
            internal Vector3 Local, LocalNormal, LocalAcross, LocalAlong;
            internal float Radius, Depth, Age, Life;
            internal Blot[] Blots;
            internal float NextPaint;
        }

        // The colours a bruise goes through as it heals, by how far through its life it is.
        private static readonly (float At, Color Color)[] Colours =
        {
            (0f, new Color(0.6f, 0.2f, 0.2f)),
            (0.03f, new Color(0.47f, 0.13f, 0.2f)),
            (0.1f, new Color(0.34f, 0.11f, 0.24f)),
            (0.24f, new Color(0.24f, 0.13f, 0.28f)),
            (0.42f, new Color(0.3f, 0.2f, 0.27f)),
            (0.55f, new Color(0.36f, 0.31f, 0.23f)),
            (0.68f, new Color(0.43f, 0.42f, 0.23f)),
            (0.82f, new Color(0.6f, 0.54f, 0.3f)),
            (1f, new Color(0.66f, 0.56f, 0.4f)),
        };

        // How fast a limb has to hit something to bruise, and how fast for the worst bruise.
        private const float BruiseSpeed = 5.5f, WorstSpeed = 16f;
        private const float LimbCooldown = 0.4f;
        private const int MostPerLimb = 6, MostInAll = 100;
        private const float LookForPeopleEvery = 1f;
        // Each bruise is painted again this often with its colour at that moment; the steps are too small to see.
        private const float RepaintEvery = 1f;
        private const double BudgetMs = 1.5;
        private const int Shapes = 4;

        private static readonly List<Bruise> All = new();
        private static readonly HashSet<IntPtr> Watched = new();
        private static readonly Dictionary<IntPtr, float> LastBruised = new();
        private static readonly Stopwatch Clock = new();
        private static Texture2D[] _shapes;
        private static float _look;
        private static int _turn;

        internal static int Count => All.Count;

        /// <summary>Bruises on one limb (for the self-test).</summary>
        internal static int On(AbstractLimb limb) => limb == null ? 0 : All.Count(b => b.Limb.Pointer == limb.Pointer);

        /// <summary>
        /// Bruises a limb at a point. <paramref name="force"/> from 0 to 1 sets how big and dark it is. A new knock
        /// on an old bruise makes that one worse rather than starting another. <paramref name="seconds"/> is how long
        /// it takes to heal (the setting when left out).
        /// </summary>
        internal static void Add(AbstractLimb limb, Vector3 point, Vector3 normal, float force, float seconds = -1f)
        {
            if (!Settings.Bruising || !limb.Exists() || force <= 0f)
                return;
            var creature = limb.GetCreature();
            if (creature == null || !creature.IsLiving() || !creature.IsHuman())
                return;
            var on = limb.GetMovingTransform();
            if (!on.Exists())
                return;
            force = Mathf.Clamp01(force);
            // Point the normal out of the limb, whichever way round it came.
            if (Vector3.Dot(normal, point - limb.GetPosition()) < 0f)
                normal = -normal;
            if (normal.sqrMagnitude < 1e-4f)
                normal = (point - limb.GetPosition()).normalized;
            normal.Normalize();
            float radius = Mathf.Lerp(0.1f, 0.2f, force);
            var local = on.InverseTransformPoint(point);
            foreach (var old in All)
            {
                if (old.Limb.Pointer != limb.Pointer || Vector3.Distance(old.Local, local) > old.Radius * 0.8f)
                    continue;
                // The same spot again: darker, a little bigger, and fresher.
                old.Depth = Mathf.Min(1.5f, old.Depth + force * 0.4f);
                old.Radius = Mathf.Min(0.26f, Mathf.Max(old.Radius, radius) * 1.08f);
                old.Age = Mathf.Min(old.Age, old.Life * 0.06f);
                old.NextPaint = 0f;
                return;
            }
            if (All.Count(b => b.Limb.Pointer == limb.Pointer) >= MostPerLimb)
                return;
            if (All.Count >= MostInAll)
            {
                Erase(All[0]);
                All.RemoveAt(0);
            }
            var across = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var along = Vector3.Cross(normal, across);
            var bruise = new Bruise
            {
                Limb = limb,
                On = on,
                Local = local,
                LocalNormal = on.InverseTransformDirection(normal),
                LocalAcross = on.InverseTransformDirection(across),
                LocalAlong = on.InverseTransformDirection(along),
                Radius = radius,
                Depth = Mathf.Lerp(0.75f, 1.15f, force),
                Life = seconds > 0f ? seconds : Settings.BruiseSeconds,
                Blots = MakeBlots(),
            };
            All.Add(bruise);
            Paint(bruise);
        }

        /// <summary>Takes every bruise off someone (a Health Syringe does this).</summary>
        internal static void Heal(AbstractCreature creature)
        {
            if (creature == null)
                return;
            foreach (var bruise in All.Where(b => b.Limb.Exists() && b.Limb.GetCreature()?.Pointer == creature.Pointer).ToList())
            {
                Erase(bruise);
                All.Remove(bruise);
            }
        }

        internal static void Update()
        {
            float dt = Time.deltaTime;
            if ((_look += dt) >= LookForPeopleEvery)
            {
                _look = 0f;
                if (Settings.Bruising)
                    WatchEveryone();
            }
            if (All.Count == 0)
                return;
            float now = Time.time;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var bruise = All[i];
                if (!bruise.Limb.Exists() || !bruise.On.Exists())
                {
                    All.RemoveAt(i);
                    continue;
                }
                bruise.Age += dt;
                if (bruise.Age >= bruise.Life)
                {
                    Erase(bruise);
                    All.RemoveAt(i);
                }
            }
            // Repaint the ones that are due, a few each frame and taking turns, within a time budget.
            Clock.Restart();
            for (int n = 0; n < All.Count && Clock.Elapsed.TotalMilliseconds < BudgetMs; n++)
            {
                var bruise = All[(_turn + n) % All.Count];
                if (now < bruise.NextPaint)
                    continue;
                Paint(bruise);
            }
            _turn++;
        }

        internal static void Reset()
        {
            All.Clear();
            Watched.Clear();
            LastBruised.Clear();
        }

        // ------------------------------------------------------------ knocks

        // Every living person's limbs report hard knocks. New people are picked up within a second.
        private static void WatchEveryone()
        {
            foreach (var creature in Creatures.Living.ToList())
            {
                if (!creature.IsHuman())
                    continue;
                foreach (var limb in creature.GetLimbs())
                {
                    var body = limb.GetRigidbody();
                    if (!body.Exists() || !Watched.Add(body.Pointer))
                        continue;
                    var events = ObjectEvents.For(body.gameObject);
                    events.HitSpeed = BruiseSpeed;
                    var knocked = limb;
                    events.Hit += impact => Knocked(knocked, impact);
                }
            }
        }

        private static void Knocked(AbstractLimb limb, Impact impact)
        {
            if (!limb.Exists())
                return;
            // Their own arms and legs, and small light things, don't count.
            var other = impact.Other != null ? Creatures.FromGameObject(impact.Other) : null;
            var creature = limb.GetCreature();
            if (other != null && creature != null && other.Pointer == creature.Pointer)
                return;
            if (impact.OtherBody != null && impact.OtherBody.mass < 0.5f)
                return;
            float now = Time.time;
            if (LastBruised.TryGetValue(limb.Pointer, out var last) && now - last < LimbCooldown)
                return;
            LastBruised[limb.Pointer] = now;
            Add(limb, impact.Point, impact.Normal, Mathf.InverseLerp(BruiseSpeed, WorstSpeed, impact.Speed) * 0.8f + 0.2f);
        }

        // ------------------------------------------------------------ looks

        // A dark core, blotches round it, small spots towards the edge and a faint wide halo. The outer parts heal
        // ahead of the middle, so the edge goes green and yellow while the middle is still purple.
        private static Blot[] MakeBlots()
        {
            var blots = new List<Blot>
            {
                new() { Size = 1.05f, Strength = 0.32f, Lead = 0.3f },
            };
            void Ring(int count, float from, float to, float sizeFrom, float sizeTo, float strength, float lead)
            {
                float start = Random.value * 360f;
                for (int i = 0; i < count; i++)
                {
                    float angle = (start + 360f * i / count + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
                    float distance = Random.Range(from, to);
                    blots.Add(new Blot
                    {
                        Offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance,
                        Size = Random.Range(sizeFrom, sizeTo),
                        Strength = strength * Random.Range(0.8f, 1.1f),
                        Lead = lead * Random.Range(0.7f, 1.2f),
                    });
                }
            }
            Ring(Random.Range(4, 6), 0.45f, 0.75f, 0.22f, 0.36f, 0.6f, 0.2f);
            Ring(Random.Range(2, 4), 0.2f, 0.4f, 0.4f, 0.55f, 0.8f, 0.09f);
            Ring(2, 0f, 0.14f, 0.5f, 0.68f, 1f, 0f);
            for (int i = 0; i < blots.Count; i++)
            {
                var blot = blots[i];
                blot.Turn = Random.Range(0f, 360f);
                blot.Shape = Random.Range(0, Shapes);
                blots[i] = blot;
            }
            return blots.ToArray();
        }

        private static Color ColourAt(float through)
        {
            through = Mathf.Clamp01(through);
            for (int i = 1; i < Colours.Length; i++)
            {
                if (through > Colours[i].At)
                    continue;
                float t = Mathf.InverseLerp(Colours[i - 1].At, Colours[i].At, through);
                return Color.Lerp(Colours[i - 1].Color, Colours[i].Color, t * t * (3f - 2f * t));
            }
            return Colours[^1].Color;
        }

        // How strongly the colour shows: it comes up over the first moments, holds, then fades out as it heals.
        private static float StrengthAt(float through)
        {
            float rise = Mathf.SmoothStep(0.55f, 1f, Mathf.Clamp01(through / 0.04f));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, through));
            return rise * fade;
        }

        // ------------------------------------------------------------ painting

        // The old paint comes off and the bruise goes on again with its colours as they are now, in the same frame.
        private static void Paint(Bruise bruise)
        {
            bruise.NextPaint = Time.time + RepaintEvery * Random.Range(0.85f, 1.15f);
            var module = PaintModule(bruise.Limb);
            if (module == null || !bruise.On.Exists())
                return;
            float through = bruise.Age / bruise.Life;
            var centre = bruise.On.TransformPoint(bruise.Local);
            var normal = bruise.On.TransformDirection(bruise.LocalNormal);
            var across = bruise.On.TransformDirection(bruise.LocalAcross);
            var along = bruise.On.TransformDirection(bruise.LocalAlong);
            var shapes = ShapeTextures();
            try
            {
                module.TryErase(centre, normal, shapes[0], bruise.Radius * 1.7f, 1f, PaintMode.Surface);
                foreach (var blot in bruise.Blots)
                {
                    float own = Mathf.Clamp01(through * (1f + blot.Lead) + blot.Lead * 0.05f);
                    float opacity = Mathf.Clamp(StrengthAt(own) * blot.Strength * bruise.Depth * 0.72f, 0f, 0.85f);
                    if (opacity < 0.01f)
                        continue;
                    var at = centre + (across * blot.Offset.x + along * blot.Offset.y) * bruise.Radius;
                    var colour = ColourAt(own);
                    colour.a = 1f;
                    module.TryStamp(at, normal, shapes[blot.Shape], bruise.Radius * blot.Size, opacity, PaintMode.Surface, blot.Turn,
                        new Il2CppSystem.Nullable<Color>(colour));
                }
            }
            catch (Exception e)
            {
                FruktLog.Debug("Painting a bruise failed: " + e.Message);
            }
        }

        private static void Erase(Bruise bruise)
        {
            var module = PaintModule(bruise.Limb);
            if (module == null || !bruise.On.Exists())
                return;
            try
            {
                module.TryErase(bruise.On.TransformPoint(bruise.Local), bruise.On.TransformDirection(bruise.LocalNormal), ShapeTextures()[0],
                    bruise.Radius * 1.7f, 1f, PaintMode.Surface);
            }
            catch (Exception e)
            {
                FruktLog.Debug("Taking a bruise off failed: " + e.Message);
            }
        }

        // The limb's own paint layer, the one blood goes on.
        private static VoxelMeshPaintModule PaintModule(AbstractLimb limb)
        {
            if (!limb.Exists() || limb.References == null)
                return null;
            var mesh = limb.GetVoxelMesh();
            VoxelMeshPaintModule first = null;
            foreach (var module in limb.References.Cast<Component>().GetComponentsInChildren<VoxelMeshPaintModule>(true))
            {
                if (module == null)
                    continue;
                if (mesh != null && module.m_mesh != null && module.m_mesh.Pointer == mesh.Pointer)
                    return module;
                first ??= module;
            }
            return first;
        }

        // Soft, uneven blotches with a mottled inside, that the colour is laid on through.
        private static Texture2D[] ShapeTextures()
        {
            if (_shapes != null && _shapes.All(s => s != null))
                return _shapes;
            const int size = 64;
            _shapes = new Texture2D[Shapes];
            for (int n = 0; n < Shapes; n++)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Bruise" + n };
                float seed = 17.3f * (n + 1);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                        // The outline wobbles with the angle, so no blotch is round.
                        float angle = Mathf.Atan2(v, u);
                        float wobble = 0.72f + 0.28f * Mathf.PerlinNoise(seed + Mathf.Cos(angle) * 1.3f, seed + Mathf.Sin(angle) * 1.3f);
                        float d = Mathf.Sqrt(u * u + v * v) / wobble;
                        float a = Mathf.Clamp01(1f - d);
                        a = Mathf.Pow(a, 0.8f);
                        a = a * a * (3f - 2f * a);
                        // Mottled: patches inside are darker and lighter.
                        float mottle = 0.55f * Mathf.PerlinNoise(seed + 50f + x * 0.09f, seed + y * 0.09f)
                                       + 0.45f * Mathf.PerlinNoise(seed + 90f + x * 0.22f, seed + 20f + y * 0.22f);
                        a *= Mathf.Lerp(0.55f, 1.05f, mottle);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                    }
                }
                texture.Apply();
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _shapes[n] = texture;
            }
            return _shapes;
        }
    }
}
