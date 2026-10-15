using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BunchAStuff
{
    /// <summary>
    /// Grenades, under Grenades in the terminal. Left click throws one; it bounces about and goes off when its fuse
    /// runs out.
    /// </summary>
    internal static class Grenades
    {
        private enum Kind
        {
            Frag,
            Flash,
            Smoke,
        }

        private sealed class Live
        {
            internal Kind Kind;
            internal GameObject Body;
            internal float Fuse;
            internal float SmokeUntil;
            internal Vector3 At;
            internal float NextPuff;
        }

        private static readonly Dictionary<Kind, GameObject> Templates = new();
        internal static readonly List<ModGun> All = new();
        private static readonly List<Live> Thrown = new();
        private const float Fuse = 3f;

        internal static void Create()
        {
            Inventory.AddCategory("Grenades");
            Add(Kind.Frag, "Frag Grenade", GrenadeModels.Frag(), "Goes off three seconds after it's thrown: a blast, and shrapnel that flies through whoever is near.",
                ("fuse", "3 s"), ("shrapnel", "30 pieces"));
            Add(Kind.Flash, "Flashbang", GrenadeModels.Flashbang(), "A blinding flash and a bang. Everyone close by drops, stunned, for a few seconds.",
                ("fuse", "3 s"), ("stuns", "within 7 m"));
            Add(Kind.Smoke, "Smoke Grenade", GrenadeModels.Smoke(), "Pours out thick smoke for fifteen seconds.",
                ("fuse", "2 s"), ("smoke", "15 s"));
        }

        private static void Add(Kind kind, string name, GameObject model, string description, params (string Key, string Value)[] card)
        {
            Templates[kind] = model;
            var gun = Inventory.AddGun(name, "Grenades").WithCooldown(0.7f).OnFire(_ => Throw(kind));
            gun.WithDescription(description).WithModel(model, new Vector3(0.12f, -0.13f, 0.25f), Vector3.zero, 0.9f);
            foreach (var (key, value) in card)
                gun.WithCard(key, value);
            All.Add(gun);
        }

        private static void Throw(Kind kind)
        {
            var forward = LocalPlayer.Forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var at = LocalPlayer.CameraPosition + forward * 0.5f + right * 0.15f - Vector3.up * 0.1f;
            var body = Object.Instantiate(Templates[kind]);
            body.name = "Thrown " + kind;
            body.transform.position = at;
            body.transform.rotation = Quaternion.LookRotation(forward);
            body.SetActive(true);
            var collider = body.AddComponent<SphereCollider>();
            collider.radius = 0.035f;
            collider.center = new Vector3(0f, 0.04f, 0f);
            var rigidbody = body.AddComponent<Rigidbody>();
            rigidbody.mass = 0.4f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.velocity = forward * 13f + Vector3.up * 2.5f;
            rigidbody.angularVelocity = Random.insideUnitSphere * 12f;
            Thrown.Add(new Live { Kind = kind, Body = body, Fuse = Time.time + (kind == Kind.Smoke ? 2f : Fuse) });
            Sounds.Play(WhooshSFXType.WeaponAimWhoosh, at, 0.5f);
            Sounds.Play(ToolsSFXType.PinSound, at, 0.6f);
        }

        internal static void Update()
        {
            if (Thrown.Count == 0)
                return;
            float now = Time.time;
            for (int i = Thrown.Count - 1; i >= 0; i--)
            {
                var g = Thrown[i];
                if (g.SmokeUntil > 0f)
                {
                    if (now > g.SmokeUntil)
                    {
                        if (g.Body.Exists())
                            Object.Destroy(g.Body);
                        Thrown.RemoveAt(i);
                    }
                    else if (now >= g.NextPuff)
                    {
                        g.NextPuff = now + 0.08f;
                        var from = g.Body.Exists() ? g.Body.transform.position : g.At;
                        Effects.Smoke(from + Vector3.up * 0.1f, Random.Range(0.7f, 1.3f), Random.Range(4f, 6.5f),
                            Random.insideUnitSphere * 1.2f + Vector3.up * 0.8f, 0.12f);
                    }
                    continue;
                }
                if (!g.Body.Exists())
                {
                    Thrown.RemoveAt(i);
                    continue;
                }
                if (now < g.Fuse)
                    continue;
                g.At = g.Body.transform.position;
                switch (g.Kind)
                {
                    case Kind.Frag:
                        Frag(g.At);
                        break;
                    case Kind.Flash:
                        Flash(g.At);
                        break;
                    default:
                        g.SmokeUntil = now + 15f;
                        Sounds.Play(WhooshSFXType.SpinnerAirWhoosh, g.At, 0.8f);
                        continue;
                }
                Object.Destroy(g.Body);
                Thrown.RemoveAt(i);
            }
        }

        private static void Frag(Vector3 at)
        {
            Guns.Detonate(at);
            // Shrapnel: the game's own pellets, out in every direction, mostly sideways and up.
            for (int i = 0; i < 30; i++)
            {
                var direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.6f;
                Bullets.Launch(at + Vector3.up * 0.1f, direction.normalized, Bullets.Caliber.Pellet);
            }
        }

        private static void Flash(Vector3 at)
        {
            Effects.Flash(at, Color.white, 30f, 0.5f, 40f);
            Effects.Burst(at, Color.white, 40, 9f, 0.03f, 0.25f, true, 0f, null, 180f, new Color(1f, 0.95f, 0.8f));
            Effects.Smoke(at, 0.5f, 2f, Vector3.up * 0.5f, 0.1f);
            Sounds.Play(WeaponSFXType.Shoot12ga, at, 1f);
            Sounds.Play(WeaponSFXType.Shoot762, at, 0.8f);
            foreach (var creature in Creatures.Living.ToList())
            {
                if (!creature.IsHuman())
                    continue;
                float distance = Vector3.Distance(creature.GetPosition(), at);
                if (distance < 7f)
                    Injuries.KnockOut(creature, Mathf.Lerp(7f, 3f, distance / 7f));
            }
        }

        internal static void Reset()
        {
            foreach (var g in Thrown)
            {
                if (g.Body.Exists())
                    Object.Destroy(g.Body);
            }
            Thrown.Clear();
        }
    }

    /// <summary>The grenades' voxel models, a centimetre to a voxel, standing up with the base at the origin.</summary>
    internal static class GrenadeModels
    {
        private static Color C(int rgb) => Voxels.Hex(rgb);

        internal static GameObject Frag()
        {
            var v = new Voxels()
                .Key('O', C(0x4d5a2c)).Key('o', C(0x3a4422)).Key('S', C(0x8e939a)).Key('s', C(0x6c7178)).Key('R', C(0xc0c4ca));
            // An egg-shaped body with grooves, the fuse on top, the lever down its side and the pin ring.
            for (int y = 0; y <= 8; y++)
            {
                float r = 3.4f * Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow((y - 4f) / 4.6f, 2f)));
                v.Post(0, 0, y, y, r, y % 3 == 0 ? 'o' : 'O');
            }
            v.Post(0, 0, 9, 11, 1.4f, 'S');
            v.Box(1, 1, 3, 11, -2, -2, 's').Box(0, 1, 11, 11, -2, 1, 's');
            v.Box(-3, -2, 11, 11, 0, 0, 'R').Box(-4, -4, 9, 11, 0, 0, 'R').Box(-3, -2, 8, 8, 0, 0, 'R');
            return v.Build("Grenade Frag");
        }

        internal static GameObject Flashbang()
        {
            var v = new Voxels()
                .Key('G', C(0x5c6168)).Key('g', C(0x3d4146)).Key('Y', C(0xd8b02a)).Key('S', C(0x8e939a)).Key('R', C(0xc0c4ca));
            // A tube with rows of holes, a yellow band, the fuse on top.
            v.Post(0, 0, 0, 10, 2.6f, 'G');
            for (int y = 2; y <= 8; y += 3)
                v.Box(-3, 3, y, y, 0, 0, 'g').Box(0, 0, y, y, -3, 3, 'g');
            v.Post(0, 0, 9, 9, 2.6f, 'Y');
            v.Post(0, 0, 11, 13, 1.3f, 'S');
            v.Box(-3, -2, 13, 13, 0, 0, 'R').Box(-4, -4, 11, 13, 0, 0, 'R');
            return v.Build("Grenade Flashbang");
        }

        internal static GameObject Smoke()
        {
            var v = new Voxels()
                .Key('G', C(0x55613a)).Key('W', C(0xe6e6e0)).Key('S', C(0x8e939a)).Key('R', C(0xc0c4ca));
            // A fat canister with a white band, the fuse on top.
            v.Post(0, 0, 0, 11, 3.2f, 'G');
            v.Post(0, 0, 5, 6, 3.2f, 'W');
            v.Post(0, 0, 12, 14, 1.4f, 'S');
            v.Box(-3, -2, 14, 14, 0, 0, 'R').Box(-4, -4, 12, 14, 0, 0, 'R');
            return v.Build("Grenade Smoke");
        }
    }
}
