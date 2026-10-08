using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.Objects;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using Il2CppLVA.Creatures;
using Il2CppLVA.Limbs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>
    /// Syringes under Props in the terminal. Put one down, pick it up with the cursor and push the needle into
    /// someone (or throw it at them): it sticks in, the plunger goes down and whatever is inside takes effect. Grab
    /// it again to pull it out. Each syringe is used once.
    /// </summary>
    internal static class Syringes
    {
        /// <summary>One kind of syringe: what's in it and what that does.</summary>
        internal sealed class Kind
        {
            internal string Name { get; }
            internal Color Liquid { get; }
            /// <summary>How long the effect lasts once injected, in seconds. 0 for something that happens at once.</summary>
            internal float Seconds { get; }
            internal Action<Injection> Started { get; }
            /// <summary>Every frame while it lasts, with the frame's time.</summary>
            internal Action<Injection, float> Working { get; }
            internal Action<Injection> Ended { get; }
            internal ModProp Prop { get; set; }

            internal Kind(string name, Color liquid, float seconds, Action<Injection> started, Action<Injection, float> working, Action<Injection> ended)
            {
                Name = name;
                Liquid = liquid;
                Seconds = seconds;
                Started = started;
                Working = working;
                Ended = ended;
            }
        }

        /// <summary>A dose working in someone.</summary>
        internal sealed class Injection
        {
            internal Kind Kind { get; }
            internal AbstractCreature Creature { get; }
            internal AbstractLimb Limb { get; }
            internal float Elapsed { get; set; }
            /// <summary>How far through it is, from 0 to 1.</summary>
            internal float Progress => Kind.Seconds > 0f ? Mathf.Clamp01(Elapsed / Kind.Seconds) : 1f;

            internal Injection(Kind kind, AbstractCreature creature, AbstractLimb limb)
            {
                Kind = kind;
                Creature = creature;
                Limb = limb;
            }
        }

        // Model sizes: the model is built from 1 cm voxels and shown at half size, so each voxel is 5 mm.
        private const float ModelScale = 0.5f;
        private const float Voxel = Voxels.Size * ModelScale;
        private const int TipZ = 39, LiquidFrom = 4, LiquidTo = 20, PlungerTravel = 16;
        /// <summary>The needle's point, relative to the syringe.</summary>
        internal static readonly Vector3 Tip = new(0f, 0f, (TipZ + 0.5f) * Voxel);
        private const float PlungeSeconds = 0.6f;
        private const float NeedleFrom = 21f * Voxel;

        private static readonly List<Kind> Kinds = new();
        private static readonly Dictionary<IntPtr, Copy> Copies = new();
        private static readonly List<Injection> Working = new();

        internal static IReadOnlyList<Kind> All => Kinds;
        internal static IReadOnlyList<Injection> Active => Working;

        /// <summary>Raised when a syringe goes into someone (for the self-test).</summary>
        internal static event Action<Kind, AbstractLimb> Stabbed;

        internal static Kind Health { get; private set; }

        internal static void Create()
        {
            Health = Add("Health Syringe", new Color(0.25f, 0.95f, 0.35f), 4f,
                "Stops the bleeding and fills the blood back up over a few seconds. Destroyed flesh stays gone.",
                new[] { ("bleeding", "stops"), ("blood", "refills"), ("takes", "4 s") },
                started: dose =>
                {
                    dose.Creature.StopBleeding();
                    Glow(dose, 24);
                },
                working: (dose, dt) =>
                {
                    var creature = dose.Creature;
                    float capacity = creature.GetBloodCapacity();
                    if (capacity > 0f)
                        creature.SetBlood(Mathf.Min(capacity, creature.GetBlood() + capacity * dt / dose.Kind.Seconds));
                    // New wounds close too while it works.
                    creature.StopBleeding();
                    if (UnityEngine.Random.value < dt * 6f)
                        Glow(dose, 3);
                },
                ended: dose => dose.Creature.Heal());
        }

        private static void Glow(Injection dose, int count)
        {
            if (!dose.Limb.Exists())
                return;
            Effects.Burst(dose.Limb.GetPosition(), dose.Kind.Liquid, count, 1.2f, 0.025f, 0.6f, true, -1.5f, null, 180f, dose.Kind.Liquid * 0.4f);
        }

        /// <summary>Adds a kind of syringe to the terminal.</summary>
        internal static Kind Add(string name, Color liquid, float seconds, string description, (string Key, string Value)[] card,
            Action<Injection> started = null, Action<Injection, float> working = null, Action<Injection> ended = null)
        {
            var kind = new Kind(name, liquid, seconds, started, working, ended);
            var prop = Inventory.AddProp(name, Model(kind)).WithDescription(description);
            foreach (var (key, value) in card)
                prop.WithCard(key, value);
            prop.OnPlaced(copy => Track(kind, copy));
            kind.Prop = prop;
            Kinds.Add(kind);
            return kind;
        }

        // ------------------------------------------------------------ the model

        private static GameObject Model(Kind kind)
        {
            var root = new GameObject(kind.Name);
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * ModelScale;

            var frame = new Voxels()
                .Key('W', Voxels.Hex(0xe4e6e2)).Key('w', Voxels.Hex(0xb9bcb8)).Key('N', Voxels.Hex(0xc9ced6)).Key('M', Voxels.Hex(0x3a3d40))
                .Key('H', Darker(kind.Liquid, 0.55f));
            frame.Tube(0, 0, 1, 3, 3.3f, 'W', 2.2f);                     // back of the barrel
            frame.Box(-7, 7, -1, 1, 1, 2, 'W').Box(-7, -7, -1, 1, 1, 2, 'w').Box(7, 7, -1, 1, 1, 2, 'w'); // finger grips
            frame.Tube(0, 0, 21, 22, 3.3f, 'W', 2.2f);                   // front of the barrel
            frame.Tube(0, 0, 23, 23, 2.6f, 'W').Tube(0, 0, 24, 25, 1.6f, 'H'); // shoulder and needle hub
            frame.Box(0, 0, 0, 0, 26, TipZ, 'N');                        // the needle
            foreach (var (x, y) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3) })
                frame.Box(x, x, y, y, 4, 20, 'W');                       // ribs between the windows
            for (int z = 5; z <= 19; z += 2)
                frame.Set(0, 3, z, 'M').Set(1, 3, z, z % 4 == 1 ? 'M' : 'W'); // marks along the top
            Attach(frame.Build("Barrel"), model.transform);

            // The liquid hangs from its front end, so shrinking it empties the barrel from the back.
            var holder = new GameObject("Liquid");
            holder.transform.SetParent(model.transform, false);
            holder.transform.localPosition = new Vector3(0f, 0f, (LiquidTo + 0.5f) * Voxels.Size);
            var liquid = new Voxels().Key('L', kind.Liquid, glow: true).Key('l', Darker(kind.Liquid, 0.75f), glow: true);
            liquid.Tube(0, 0, LiquidFrom, LiquidTo, 2.6f, 'L');
            liquid.Tube(0, 0, LiquidFrom, LiquidTo, 1.4f, 'l');
            var liquidModel = Attach(liquid.Build("Liquid"), holder.transform);
            liquidModel.transform.localPosition = new Vector3(0f, 0f, -(LiquidTo + 0.5f) * Voxels.Size);

            var plunger = new Voxels().Key('K', Voxels.Hex(0x1d1e20)).Key('W', Voxels.Hex(0xd8dad6)).Key('w', Voxels.Hex(0xa9aca8));
            plunger.Tube(0, 0, 2, 3, 2.6f, 'K');                         // rubber stopper
            plunger.Box(-1, 1, -1, 1, -14, 1, 'W').Box(-2, 2, 0, 0, -14, 1, 'w').Box(0, 0, -2, 2, -14, 1, 'w'); // cross-shaped rod
            plunger.Tube(0, 0, -16, -15, 3.4f, 'W');                     // thumb pad
            var plungerModel = Attach(plunger.Build("Plunger"), model.transform);
            plungerModel.name = "Plunger";

            AddBox(root, new Vector3(0f, 0f, 11.5f * Voxel), new Vector3(7f, 7f, 22f) * Voxel);           // barrel
            AddBox(root, new Vector3(0f, 0f, 1.5f * Voxel), new Vector3(15f, 3f, 2f) * Voxel);             // grips
            AddBox(root, new Vector3(0f, 0f, -7.5f * Voxel), new Vector3(7f, 7f, 17f) * Voxel);           // plunger
            AddBox(root, new Vector3(0f, 0f, 31.5f * Voxel), new Vector3(1.2f, 1.2f, 17f) * Voxel);       // needle
            return root;
        }

        private static GameObject Attach(GameObject part, Transform parent)
        {
            part.transform.SetParent(parent, false);
            part.SetActive(true);
            return part;
        }

        private static void AddBox(GameObject root, Vector3 center, Vector3 size)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        private static Color Darker(Color color, float amount) => new(color.r * amount, color.g * amount, color.b * amount, 1f);

        // ------------------------------------------------------------ copies in the world

        private sealed class Copy
        {
            internal Kind Kind;
            internal GameObject Object;
            internal Rigidbody Body;
            internal Transform Liquid, Plunger;
            internal Vector3 PlungerFrom;
            internal JointHandle Stuck;
            internal AbstractLimb Limb;
            internal float Plunged = -1f; // -1 until it goes in, then how far down the plunger is (0 to 1)
            internal bool Empty;
        }

        private static void Track(Kind kind, GameObject copy)
        {
            var model = copy.transform.Find("Model");
            var c = new Copy
            {
                Kind = kind,
                Object = copy,
                Body = copy.GetComponent<Rigidbody>(),
                Liquid = model?.Find("Liquid"),
                Plunger = model?.Find("Plunger"),
            };
            if (c.Plunger != null)
                c.PlungerFrom = c.Plunger.localPosition;
            Copies[copy.Pointer] = c;
            var events = ObjectEvents.For(copy);
            events.ImpactSounds = true;
            events.Collided += impact => TryStab(c, impact.Other, impact.Point);
            events.Grabbed += () => PullOut(c);
        }

        private static void TryStab(Copy c, GameObject other, Vector3 point)
        {
            if (c.Empty || c.Plunged >= 0f || !c.Object.Exists() || other == null)
                return;
            // Only the needle end counts: past the front of the barrel.
            if (c.Object.transform.InverseTransformPoint(point).z < NeedleFrom)
                return;
            var limb = Creatures.LimbFromGameObject(other);
            Stab(c, limb);
        }

        private static bool Stab(Copy c, AbstractLimb limb)
        {
            var creature = limb?.GetCreature();
            var target = limb?.GetRigidbody();
            if (creature == null || !creature.IsValid() || target == null || c.Body == null)
                return false;
            // Push the needle in a little before it's fixed there.
            var forward = c.Object.transform.forward;
            c.Body.position += forward * 0.012f;
            c.Object.transform.position = c.Body.position;
            c.Body.velocity = target.velocity;
            c.Stuck = Joints.Weld(c.Body, target, 900f);
            c.Limb = limb;
            c.Plunged = 0f;
            Sounds.Play(ToolsSFXType.PinSound, c.Object.transform.TransformPoint(Tip), 0.6f);
            Stabbed?.Invoke(c.Kind, limb);
            return true;
        }

        private static void PullOut(Copy c)
        {
            if (c.Stuck == null)
                return;
            c.Stuck.Remove();
            c.Stuck = null;
        }

        /// <summary>Starts a dose in someone without a syringe (for the self-test and for other parts of the mod).</summary>
        internal static Injection Inject(Kind kind, AbstractLimb limb)
        {
            var creature = limb?.GetCreature();
            if (creature == null || !creature.IsValid())
                return null;
            var dose = new Injection(kind, creature, limb);
            Run(() => kind.Started?.Invoke(dose), kind);
            Working.Add(dose);
            return dose;
        }

        /// <summary>Stabs a placed syringe into a limb straight away (for the self-test).</summary>
        internal static bool StabNow(GameObject syringe, AbstractLimb limb) =>
            syringe.Exists() && Copies.TryGetValue(syringe.Pointer, out var c) && !c.Empty && c.Plunged < 0f && Stab(c, limb);

        internal static bool IsEmpty(GameObject syringe) => syringe.Exists() && Copies.TryGetValue(syringe.Pointer, out var c) && c.Empty;

        internal static bool IsStuck(GameObject syringe) =>
            syringe.Exists() && Copies.TryGetValue(syringe.Pointer, out var c) && c.Stuck != null && c.Stuck.IsActive;

        internal static void Update()
        {
            float dt = Time.deltaTime;
            if (Copies.Count > 0)
            {
                foreach (var key in Copies.Keys.ToList())
                {
                    var c = Copies[key];
                    if (!c.Object.Exists())
                    {
                        Copies.Remove(key);
                        continue;
                    }
                    if (c.Stuck != null && !c.Stuck.IsActive)
                        c.Stuck = null;
                    if (c.Plunged < 0f || c.Empty)
                        continue;
                    c.Plunged = Mathf.Min(1f, c.Plunged + dt / PlungeSeconds);
                    if (c.Plunger != null)
                        c.Plunger.localPosition = c.PlungerFrom + Vector3.forward * (PlungerTravel * Voxels.Size * c.Plunged);
                    if (c.Liquid != null)
                    {
                        c.Liquid.localScale = new Vector3(1f, 1f, Mathf.Max(0.001f, 1f - c.Plunged));
                        if (c.Plunged >= 1f)
                            c.Liquid.gameObject.SetActive(false);
                    }
                    if (c.Plunged >= 1f)
                    {
                        c.Empty = true;
                        Inject(c.Kind, c.Limb);
                    }
                }
            }

            for (int i = Working.Count - 1; i >= 0; i--)
            {
                var dose = Working[i];
                if (!dose.Creature.IsValid())
                {
                    Working.RemoveAt(i);
                    continue;
                }
                dose.Elapsed += dt;
                Run(() => dose.Kind.Working?.Invoke(dose, dt), dose.Kind);
                if (dose.Elapsed >= dose.Kind.Seconds)
                {
                    Working.RemoveAt(i);
                    Run(() => dose.Kind.Ended?.Invoke(dose), dose.Kind);
                }
            }
        }

        private static void Run(Action action, Kind kind)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                FruktSharedLibrary.Core.FruktLog.Warning($"The {kind.Name} failed: {e.Message}");
            }
        }

        /// <summary>A new map: the doses stop and the old copies are forgotten.</summary>
        internal static void Reset()
        {
            Working.Clear();
            Copies.Clear();
        }
    }
}
