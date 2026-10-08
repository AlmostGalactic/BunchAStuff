using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using Il2CppLVA.Creatures;
using Il2CppLVA.Creatures.Parameters;
using Il2CppLVA.Limbs;
using Il2CppLVA.Organs.Variants.Human;
using FruktSharedLibrary.UI;
using UnityEngine;
using Random = UnityEngine.Random;
using Injection = BunchAStuff.Syringes.Injection;

namespace BunchAStuff
{
    /// <summary>What's in the syringes. Each one is a single <see cref="Syringes.Add"/>.</summary>
    internal static class SyringeKinds
    {
        internal static Syringes.Kind Health { get; private set; }
        internal static Syringes.Kind Knockout { get; private set; }
        internal static Syringes.Kind Acid { get; private set; }
        internal static Syringes.Kind BoneEater { get; private set; }
        internal static Syringes.Kind Durability { get; private set; }
        internal static Syringes.Kind Adrenaline { get; private set; }
        internal static Syringes.Kind Float { get; private set; }
        internal static Syringes.Kind Explosive { get; private set; }
        internal static Syringes.Kind Rage { get; private set; }

        private const float BonesGo = 5f;

        internal static void Create()
        {
            Health = Syringes.Add("Health Syringe", new Color(0.25f, 0.95f, 0.35f), 4f,
                "Stops the bleeding, fills the blood back up and grows damaged flesh back over a few seconds. Limbs that came off stay off.",
                new[] { ("bleeding", "stops"), ("blood", "refills"), ("flesh", "grows back"), ("takes", "4 s") },
                started: dose =>
                {
                    dose.Creature.StopBleeding();
                    // Only the limbs they still have: anything that came off stays off.
                    Tissue.Regrow(dose.Creature, dose.Seconds);
                    Syringes.Glow(dose, 24);
                },
                working: (dose, dt) =>
                {
                    var creature = dose.Creature;
                    float capacity = creature.GetBloodCapacity();
                    if (capacity > 0f)
                        creature.SetBlood(Mathf.Min(capacity, creature.GetBlood() + capacity * dt / dose.Seconds));
                    // New wounds close too while it works.
                    creature.StopBleeding();
                    if (Random.value < dt * 6f)
                        Syringes.Glow(dose, 3);
                },
                ended: dose => dose.Creature.Heal());

            Knockout = Syringes.Add("Knockout Syringe", new Color(0.55f, 0.35f, 1f), 20f,
                "Puts them out cold for 20 seconds. They drop where they stand and come round afterwards, if nothing else is wrong with them.",
                new[] { ("out for", "20 s") },
                started: dose => Syringes.Glow(dose, 12),
                working: (dose, dt) => Hold<CognitionLevel>(dose.Creature, 0f),
                ended: dose => Recalculate<CognitionLevel>(dose.Creature));

            Acid = Syringes.Add("Acid Syringe", new Color(0.75f, 1f, 0.1f), 7f,
                "Eats the part it goes into away from the outside in, then gets into the parts next to it.",
                new[] { ("eats", "flesh and bone"), ("takes", "7 s") },
                started: dose =>
                {
                    // The parts joined on to it, found now: the part itself may be gone by the time the acid spreads.
                    var next = dose.Limb.GetChildLimbs();
                    var parent = dose.Limb.GetParentLimb();
                    if (parent != null)
                        next.Add(parent);
                    dose.Data = next;
                    Tissue.Dissolve(dose.Limb, dose.Seconds * 0.6f);
                    Fizz(dose, 10);
                },
                working: (dose, dt) =>
                {
                    if (dose.Data is List<AbstractLimb> next && dose.Elapsed > dose.Seconds * 0.25f)
                    {
                        dose.Data = null;
                        foreach (var limb in next)
                        {
                            if (limb.Exists())
                                Tissue.Dissolve(limb, dose.Seconds * 0.6f, 0.75f);
                        }
                    }
                    if ((dose.Timer += dt) > 0.08f)
                    {
                        dose.Timer = 0f;
                        Fizz(dose, 3);
                    }
                });

            BoneEater = Syringes.Add("Bone Eater Syringe", new Color(1f, 0.62f, 0.25f), 30f,
                "Dissolves every bone in the body over a few seconds, and they fold up like a sack. A Health Syringe grows the bones back.",
                new[] { ("eats", "bones"), ("takes", "5 s") },
                started: dose =>
                {
                    Tissue.Dissolve(dose.Creature, BonesGo, 1f, organ => organ.TryCast<Bone>() != null);
                    Syringes.Glow(dose, 16);
                },
                working: (dose, dt) =>
                {
                    // Nothing holds them up once the bones are gone.
                    if (dose.Elapsed > BonesGo * 0.6f)
                        Hold<GeneralMuscleForce>(dose.Creature, 0f);
                    if ((dose.Timer += dt) > 0.3f && dose.Creature.IsValid() && dose.Elapsed < BonesGo)
                    {
                        dose.Timer = 0f;
                        var limbs = dose.Creature.GetLimbs();
                        if (limbs.Count > 0)
                        {
                            var limb = limbs[Random.Range(0, limbs.Count)];
                            Effects.Burst(limb.GetPosition(), new Color(1f, 0.95f, 0.85f), 4, 0.8f, 0.02f, 0.5f, false, 2f, null, 180f, new Color(0.8f, 0.7f, 0.55f));
                        }
                    }
                },
                // The body's own sense of how strong it is takes over again; without bones that isn't much.
                ended: dose => Recalculate<GeneralMuscleForce>(dose.Creature));

            Durability = Syringes.Add("Durability Syringe", new Color(0.35f, 0.6f, 1f), 60f,
                "For a minute, lost flesh grows back almost as fast as it's lost, wounds close and the blood stays topped up. Limbs that come off stay off.",
                new[] { ("lasts", "60 s"), ("heals", "within a second") },
                started: dose =>
                {
                    dose.Data = new Dictionary<IntPtr, Tissue.Regrowth>();
                    Syringes.Glow(dose, 20);
                },
                working: (dose, dt) =>
                {
                    if ((dose.Timer += dt) < 0.2f)
                        return;
                    dose.Timer = 0f;
                    var creature = dose.Creature;
                    var growing = (Dictionary<IntPtr, Tissue.Regrowth>)dose.Data;
                    foreach (var limb in creature.GetLimbs())
                    {
                        if (limb.GetWholeness() >= 99.5f || (growing.TryGetValue(limb.Pointer, out var g) && !g.Done))
                            continue;
                        growing[limb.Pointer] = Tissue.Regrow(limb, 0.6f);
                        Effects.Burst(limb.GetPosition(), dose.Kind.Liquid, 3, 1f, 0.02f, 0.4f, true, -1f, null, 180f, dose.Kind.Liquid * 0.4f);
                    }
                    creature.StopBleeding();
                    creature.RefillBlood();
                });

            Adrenaline = Syringes.Add("Adrenaline Syringe", new Color(1f, 0.15f, 0.12f), 30f,
                "For half a minute they feel no pain and nothing knocks them out, even blood loss. When it wears off, it all catches up with them.",
                new[] { ("pain", "none"), ("lasts", "30 s") },
                started: dose => Syringes.Glow(dose, 16),
                working: (dose, dt) =>
                {
                    Hold<CreaturePain>(dose.Creature, 0f);
                    Hold<CognitionLevel>(dose.Creature, 100f);
                },
                ended: dose =>
                {
                    Recalculate<CreaturePain>(dose.Creature);
                    Recalculate<CognitionLevel>(dose.Creature);
                });

            Float = Syringes.Add("Float Syringe", new Color(0.65f, 1f, 1f), 8f,
                "Makes them lighter than air for 8 seconds. They drift up, and come back down when it wears off.",
                new[] { ("lift", "8 s") },
                started: dose => Syringes.Glow(dose, 16),
                working: (dose, dt) =>
                {
                    foreach (var limb in dose.Creature.GetLimbs())
                    {
                        var body = limb.GetRigidbody();
                        if (body == null || body.isKinematic)
                            continue;
                        // Gravity cancelled and a little more, up to a gentle rise.
                        var v = body.velocity;
                        v += -Physics.gravity * 1.15f * dt;
                        if (v.y > 2.5f)
                            v.y = 2.5f;
                        body.velocity = v;
                    }
                    if (Random.value < dt * 4f)
                        Syringes.Glow(dose, 2);
                });

            Explosive = Syringes.Add("Explosive Syringe", new Color(1f, 0.45f, 0.05f), 4f,
                "Ticks for 4 seconds, faster and faster, then they blow up like a Tusk-40 rocket.",
                new[] { ("fuse", "4 s") },
                started: dose => Syringes.Glow(dose, 10),
                working: (dose, dt) =>
                {
                    // The ticks come closer together as it runs down.
                    float gap = Mathf.Lerp(0.6f, 0.08f, dose.Progress);
                    if ((dose.Timer += dt) < gap || !dose.Limb.Exists())
                        return;
                    dose.Timer = 0f;
                    var at = dose.Limb.GetPosition();
                    Sounds.Play(ToolsSFXType.PinSound, at, 0.8f);
                    Effects.Burst(at, new Color(1f, 0.7f, 0.2f), 5, 2.5f, 0.02f, 0.25f, true, 4f, null, 180f, new Color(1f, 0.2f, 0.02f));
                    Effects.Flash(at, new Color(1f, 0.5f, 0.1f), 2.5f, 0.08f, 3f);
                },
                ended: dose =>
                {
                    var at = dose.Limb.Exists() ? dose.Limb.GetPosition() : dose.Creature.IsValid() ? dose.Creature.GetPosition() : (Vector3?)null;
                    if (at.HasValue)
                        Guns.Detonate(at.Value);
                });

            Rage = Syringes.Add("Rage Syringe", new Color(0.9f, 0.1f, 0.45f), 0f,
                "They go for the nearest person from another team with their fists.",
                new[] { ("fights", "the nearest person") },
                started: dose =>
                {
                    Syringes.Glow(dose, 16);
                    if (!Fights.Start(dose.Creature, out var why))
                        Notifications.Warn(why);
                });
        }

        private static void Fizz(Injection dose, int count)
        {
            if (!dose.Limb.Exists())
                return;
            var at = dose.Limb.GetPosition();
            Effects.Burst(at, dose.Kind.Liquid, count, 0.7f, 0.025f, 0.5f, true, -2f, null, 180f, new Color(0.3f, 0.45f, 0.05f));
            if (Random.value < 0.4f)
                Effects.Smoke(at, 0.12f, 0.8f, Vector3.up * 0.6f, 0.45f);
        }

        /// <summary>Holds one of the creature's values where it is, for this frame.</summary>
        private static void Hold<T>(AbstractCreature creature, float value) where T : Il2CppLVA.Core.LVAInternalParameter
        {
            if (creature.IsValid())
                creature.GetParameter<T>()?.ForceValue(value);
        }

        /// <summary>Lets the game work the value out again from the state of the body.</summary>
        private static void Recalculate<T>(AbstractCreature creature) where T : Il2CppLVA.Core.LVAInternalParameter
        {
            if (creature.IsValid())
                creature.GetParameter<T>()?.DependenciesSolver?.UpdateDependedValue();
        }
    }
}
