using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.UI;
using FruktSharedLibrary.Utilities;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using Il2CppLVA.Creatures;
using Il2CppLVA.Creatures.Parameters;
using Il2CppLVA.Limbs;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BunchAStuff
{
    /// <summary>
    /// What a beating does under the skin. Every hard knock adds to a limb's damage, which heals slowly. A limb
    /// that keeps getting hit starts bleeding inside: the bruise spreads and goes dark, the person loses blood, and
    /// a bleeding chest or head brings blood up. A badly bruised spot can split open, a bone can snap and leave the
    /// limb hanging, and enough hits to the head knock someone out. A Health Syringe mends all of it.
    /// </summary>
    internal static class Injuries
    {
        private sealed class Hurt
        {
            internal AbstractLimb Limb;
            internal AbstractCreature Creature;
            internal HumanoidNodeTagValue Part;
            internal float Damage;
            internal float Bleeding;
            internal bool Broken;
            internal ConfigurableJoint Joint;
            internal JointDrive Slerp, AngularX, AngularYZ;
            internal float NextSplit, NextCough;
        }

        // Damage a limb can take before it bleeds inside, before a hard hit can break it, and how fast it heals.
        private const float BleedsAt = 3f, BreaksAt = 4.2f, SplitsAt = 1.6f;
        private const float HealPerSecond = 0.012f;
        // How hard a single hit has to be (0 to 1) to split the skin or break a bone.
        private const float SplitForce = 0.55f, BreakForce = 0.62f;
        // Blood lost to internal bleeding, as a share of all their blood per second at full bleeding.
        private const float BloodPerSecond = 0.0015f;

        private static readonly Dictionary<IntPtr, Hurt> Limbs = new();
        private static readonly Dictionary<IntPtr, (AbstractCreature Creature, float Until)> Out = new();
        private static float _tick;

        /// <summary>Limbs bleeding inside right now.</summary>
        internal static int BleedingCount => Limbs.Values.Count(h => h.Bleeding > 0f);

        /// <summary>Whether someone is knocked out by a blow to the head.</summary>
        internal static bool IsKnockedOut(AbstractCreature creature)
            => creature != null && Out.TryGetValue(creature.Pointer, out var o) && Time.time < o.Until;

        /// <summary>Just come round and still getting up: the game can count them as lifeless for a moment.</summary>
        internal static bool IsWaking(AbstractCreature creature) => creature != null && Waking.ContainsKey(creature.Pointer);

        internal static bool IsBroken(AbstractLimb limb)
            => limb != null && Limbs.TryGetValue(limb.Pointer, out var h) && h.Broken;

        /// <summary>How beaten up a limb is, from 0 (fine) up; past about 3 it can break.</summary>
        internal static float DamageOf(AbstractLimb limb)
            => limb != null && Limbs.TryGetValue(limb.Pointer, out var h) ? h.Damage : 0f;

        /// <summary>
        /// A blunt hit on a limb, <paramref name="force"/> from 0 to 1. Clothes don't stop any of this, only the
        /// bruise from showing.
        /// </summary>
        internal static void Hit(AbstractLimb limb, Vector3 point, Vector3 direction, float force)
        {
            if (!Settings.Injuries || !limb.Exists() || force <= 0f)
                return;
            var creature = limb.GetCreature();
            if (creature == null || !creature.IsLiving() || !creature.IsHuman())
                return;
            var part = limb.GetHumanPart();
            if (part == null)
                return;
            if (!Limbs.TryGetValue(limb.Pointer, out var hurt))
                Limbs[limb.Pointer] = hurt = new Hurt { Limb = limb, Creature = creature, Part = part.Value };
            float before = hurt.Damage;
            hurt.Damage += force * Toughness(hurt.Part);
            float now = Time.time;

            // A hard hit on a spot that's already badly bruised splits the skin.
            if (force >= SplitForce && before >= SplitsAt)
                SplitSkin(limb, point, direction);
            // Bleeding inside starts, and gets worse with every hit after.
            if (hurt.Damage >= BleedsAt)
            {
                bool starting = hurt.Bleeding <= 0f;
                hurt.Bleeding = Mathf.Min(1.5f, hurt.Bleeding + force * 0.2f + (starting ? 0.2f : 0f));
                if (starting)
                    hurt.NextCough = now + Random.Range(2f, 5f);
                Bruises.AddDeep(limb, point, direction, Mathf.Clamp01(hurt.Bleeding));
            }
            // Bones break from one hard hit on a limb that's already taken a lot.
            if (!hurt.Broken && hurt.Damage >= BreaksAt && force >= BreakForce && CanBreak(hurt.Part))
                Break(hurt, point);
            if (hurt.Part == HumanoidNodeTagValue.Head)
                HeadHit(creature, hurt, force);
        }

        /// <summary>
        /// Opens wounds on a limb through its collider, the way the rockets do. The game needs strengths in the tens
        /// to take voxels away (a rocket sends 40 to 160). <paramref name="collider"/> is the one that was hit, if known.
        /// </summary>
        internal static bool Wound(AbstractLimb limb, IReadOnlyList<(Vector3 Point, int Radius, float Strength)> wounds, Vector3 direction,
            Collider collider = null)
        {
            if (!limb.Exists() || wounds.Count == 0)
                return false;
            // Only the limb's colliders on the puppet layer take wounds (the rockets use those too).
            collider = ColliderOf(limb, wounds[0].Point) ?? collider;
            return collider != null && Damage.Apply(collider, wounds, direction);
        }

        internal static bool Wound(AbstractLimb limb, Vector3 point, int radius, float strength, Vector3 direction, Collider collider = null)
            => Wound(limb, new[] { (point, radius, strength) }, direction, collider);

        // The limb's own collider on the puppet layer nearest the point (not one of the limbs hanging off it).
        private static Collider ColliderOf(AbstractLimb limb, Vector3 point)
        {
            Collider best = null;
            float nearest = float.MaxValue;
            foreach (var collider in Physics.OverlapSphere(point, 0.35f, Layers.Puppet, QueryTriggerInteraction.Ignore))
            {
                if (collider == null || Creatures.LimbFromCollider(collider)?.Pointer != limb.Pointer)
                    continue;
                float d = (collider.ClosestPoint(point) - point).sqrMagnitude;
                if (d < nearest)
                {
                    nearest = d;
                    best = collider;
                }
            }
            return best;
        }

        private static Hurt HurtOf(AbstractLimb limb)
        {
            if (!limb.Exists())
                return null;
            var creature = limb.GetCreature();
            var part = limb.GetHumanPart();
            if (creature == null || !creature.IsHuman() || part == null)
                return null;
            if (!Limbs.TryGetValue(limb.Pointer, out var hurt))
                Limbs[limb.Pointer] = hurt = new Hurt { Limb = limb, Creature = creature, Part = part.Value };
            return hurt;
        }

        /// <summary>Whether a part has a bone that can snap (arms and legs).</summary>
        internal static bool Breakable(AbstractLimb limb) => limb.Exists() && limb.GetHumanPart() is { } part && CanBreak(part);

        /// <summary>Snaps the bone in an arm or leg there and then.</summary>
        internal static bool BreakNow(AbstractLimb limb)
        {
            var hurt = HurtOf(limb);
            if (hurt == null || hurt.Broken || !CanBreak(hurt.Part))
                return false;
            hurt.Damage = Mathf.Max(hurt.Damage, BreaksAt);
            Break(hurt, limb.GetPosition());
            return true;
        }

        /// <summary>Starts bleeding inside a part, <paramref name="amount"/> from 0 to 1.5, with the deep bruise over it.</summary>
        internal static void BleedInside(AbstractLimb limb, float amount, Vector3? at = null)
        {
            var hurt = HurtOf(limb);
            if (hurt == null)
                return;
            bool starting = hurt.Bleeding <= 0f;
            hurt.Bleeding = Mathf.Min(1.5f, hurt.Bleeding + amount);
            hurt.Damage = Mathf.Max(hurt.Damage, BleedsAt);
            if (starting)
                hurt.NextCough = Time.time + Random.Range(1.5f, 4f);
            var point = at ?? limb.GetPosition();
            Bruises.AddDeep(limb, point, point - limb.GetPosition(), Mathf.Clamp01(hurt.Bleeding));
        }

        /// <summary>
        /// A blade driven in: a deep narrow wound along <paramref name="direction"/> that bleeds hard, and bleeding
        /// inside if it went into the chest, belly or head.
        /// </summary>
        internal static void Stab(AbstractLimb limb, Vector3 point, Vector3 direction, float depth, Collider collider = null)
        {
            if (!limb.Exists())
                return;
            direction.Normalize();
            var wounds = new List<(Vector3, int, float)>();
            for (float d = 0f; d <= depth; d += 0.025f)
                wounds.Add((point + direction * d, 4, 160f));
            Wound(limb, wounds, direction, collider);
            limb.AddBleeding(1.2f);
            Effects.Burst(point, new Color(0.55f, 0.02f, 0.02f), 10, 2f, 0.025f, 0.7f, false, 9f, -direction, 40f, new Color(0.3f, 0f, 0f));
            if (limb.GetHumanPart() is HumanoidNodeTagValue.Spine or HumanoidNodeTagValue.Pelvis or HumanoidNodeTagValue.Head)
                BleedInside(limb, 0.5f, point);
        }

        /// <summary>
        /// The skin over a bad bruise gives way: an open, bleeding wound right there. At most once a second or so
        /// per limb, and not through clothes.
        /// </summary>
        internal static void SplitSkin(AbstractLimb limb, Vector3 point, Vector3 direction)
        {
            var hurt = HurtOf(limb);
            float now = Time.time;
            if (hurt == null || now < hurt.NextSplit || Clothes.Covers(limb, point))
                return;
            hurt.NextSplit = now + 1.2f;
            direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : (limb.GetPosition() - point).normalized;
            Wound(limb, new[] { (point + direction * 0.02f, 3, 90f), (point + direction * 0.05f, 2, 90f) }, direction);
            limb.AddBleeding(0.6f);
            Effects.Burst(point, new Color(0.55f, 0.02f, 0.02f), 10, 1.6f, 0.025f, 0.6f, false, 9f, -direction, 50f, new Color(0.3f, 0f, 0f));
        }

        // Some parts take more before they give: the chest and hips have the most around them.
        private static float Toughness(HumanoidNodeTagValue part) => part switch
        {
            HumanoidNodeTagValue.Head => 1.1f,
            HumanoidNodeTagValue.Spine or HumanoidNodeTagValue.Pelvis => 0.7f,
            HumanoidNodeTagValue.LeftHand or HumanoidNodeTagValue.RightHand or HumanoidNodeTagValue.LeftFoot or HumanoidNodeTagValue.RightFoot => 0.6f,
            _ => 0.9f,
        };

        private static bool CanBreak(HumanoidNodeTagValue part) => part is HumanoidNodeTagValue.LeftArm or HumanoidNodeTagValue.RightArm
            or HumanoidNodeTagValue.LeftForearm or HumanoidNodeTagValue.RightForearm or HumanoidNodeTagValue.LeftLeg
            or HumanoidNodeTagValue.RightLeg or HumanoidNodeTagValue.LeftKnee or HumanoidNodeTagValue.RightKnee;

        private static void Break(Hurt hurt, Vector3 point)
        {
            hurt.Broken = true;
            Sounds.Play(ImpactSFXType.FallDamageHardOrganic, point, 1f);
            Sounds.Play(ImpactSFXType.RbHit, point, 0.7f);
            hurt.Bleeding = Mathf.Max(hurt.Bleeding, 0.4f);
            Bruises.AddDeep(hurt.Limb, point, point - hurt.Limb.GetPosition(), 0.8f);
            // The muscles past the break can't hold it any more: the limb hangs and swings, but still only bends
            // the ways a joint can.
            var joint = hurt.Limb.References?.Physics?.JointProvider?.m_joint;
            if (joint.Exists())
            {
                hurt.Joint = joint;
                hurt.Slerp = joint.slerpDrive;
                hurt.AngularX = joint.angularXDrive;
                hurt.AngularYZ = joint.angularYZDrive;
                Limp(joint);
            }
            Fights.LetGo(hurt.Creature, hurt.Part);
        }

        private static readonly JointDrive Slack = new() { positionSpring = 0f, positionDamper = 2f, maximumForce = 0f };

        // The game sets its joints' drives again as the body moves, so this is done again every so often.
        private static void Limp(ConfigurableJoint joint)
        {
            if (joint.slerpDrive.positionSpring != 0f || joint.slerpDrive.maximumForce != 0f)
                joint.slerpDrive = Slack;
            if (joint.angularXDrive.positionSpring != 0f || joint.angularXDrive.maximumForce != 0f)
                joint.angularXDrive = Slack;
            if (joint.angularYZDrive.positionSpring != 0f || joint.angularYZDrive.maximumForce != 0f)
                joint.angularYZDrive = Slack;
        }

        // Each blow to the head can knock them out; the more the head has taken, the likelier, and the longer.
        private static void HeadHit(AbstractCreature creature, Hurt head, float force)
        {
            float chance = (head.Damage - 1.5f) * 0.18f + (force - 0.6f) * 0.5f;
            if (IsKnockedOut(creature) || Random.value >= chance)
                return;
            KnockOut(creature, Mathf.Clamp(3f + head.Damage * 2.5f + Random.Range(-1f, 2f), 3f, 16f));
        }

        /// <summary>Puts someone out cold for a while; they drop where they stand.</summary>
        internal static void KnockOut(AbstractCreature creature, float seconds)
        {
            if (creature == null || !creature.IsLiving())
                return;
            Out[creature.Pointer] = (creature, Time.time + seconds);
            GoLimp(creature);
        }

        // Out cold, held there for as long as it lasts: they drop where they stand.
        private static void GoLimp(AbstractCreature creature) => SyringeKinds.Hold<CognitionLevel>(creature, 0f);

        private static void ComeRound(AbstractCreature creature) => WakeUp(creature);

        /// <summary>
        /// Brings someone round after being out cold. The game lets the muscles go slack while they're out and
        /// doesn't firm them up again, so they'd lie there awake for good: strength comes back over a few seconds.
        /// </summary>
        internal static void WakeUp(AbstractCreature creature)
        {
            if (creature == null || !creature.IsValid())
                return;
            SyringeKinds.Recalculate<CognitionLevel>(creature);
            float from = creature.GetParameterValue<GeneralMuscleForce>() ?? 0f;
            Waking[creature.Pointer] = (creature, Time.time, Mathf.Clamp(from, 0f, 100f));
        }

        private const float RegainStrength = 3f;
        private static readonly Dictionary<IntPtr, (AbstractCreature Creature, float Since, float From)> Waking = new();

        /// <summary>Right-click actions for hurting someone on purpose.</summary>
        internal static void CreateMenu()
        {
            var menu = ContextMenus.AddGroup(ContextMenuTarget.Limb, "Injure", ctx => ctx.Creature != null && ctx.Creature.IsHuman() && ctx.Creature.IsLiving(),
                priority: 480);
            menu.AddLimbAction("Break this bone", l => BreakNow(l), l => Breakable(l) && !IsBroken(l));
            menu.AddLimbAction("Bleed inside here", l => BleedInside(l, 0.8f));
            menu.AddLimbAction("Stab here", l => Stab(l, l.GetPosition() + (LocalPlayer.CameraPosition - l.GetPosition()).normalized * 0.1f,
                (l.GetPosition() - LocalPlayer.CameraPosition).normalized, 0.14f));
            menu.AddCreatureAction("Knock out", c => KnockOut(c, Random.Range(8f, 14f)), c => !IsKnockedOut(c));
            menu.AddCreatureAction("Wake up", c => { Out.Remove(c.Pointer); WakeUp(c); }, IsKnockedOut);
        }

        /// <summary>Mends everything: bones set, bleeding stopped, damage gone, awake again.</summary>
        internal static void Heal(AbstractCreature creature)
        {
            if (creature == null)
                return;
            foreach (var key in Limbs.Keys.ToList())
            {
                var hurt = Limbs[key];
                if (hurt.Creature?.Pointer != creature.Pointer)
                    continue;
                Mend(hurt);
                Limbs.Remove(key);
            }
            if (Out.Remove(creature.Pointer) || Waking.ContainsKey(creature.Pointer))
                ComeRound(creature);
        }

        private static void Mend(Hurt hurt)
        {
            if (hurt.Broken && hurt.Joint.Exists())
            {
                hurt.Joint.slerpDrive = hurt.Slerp;
                hurt.Joint.angularXDrive = hurt.AngularX;
                hurt.Joint.angularYZDrive = hurt.AngularYZ;
            }
        }

        internal static void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            foreach (var key in Out.Keys.ToList())
            {
                var (creature, until) = Out[key];
                // Out cold counts as lifeless to the game, so only a body that's gone ends it early.
                if (!creature.IsValid())
                {
                    Out.Remove(key);
                    continue;
                }
                if (now < until)
                {
                    GoLimp(creature);
                    continue;
                }
                Out.Remove(key);
                ComeRound(creature);
            }
            foreach (var key in Waking.Keys.ToList())
            {
                var (creature, since, from) = Waking[key];
                // Dead, or out cold again (that starts its own count).
                if (!creature.IsValid() || (!creature.IsLiving() && now - since > 2f) || Out.ContainsKey(key))
                {
                    Waking.Remove(key);
                    continue;
                }
                float t = (now - since) / RegainStrength;
                if (t < 1f)
                {
                    SyringeKinds.Hold<GeneralMuscleForce>(creature, Mathf.Lerp(from, 100f, t * t * (3f - 2f * t)));
                    continue;
                }
                // Strength is held up until they're back on their feet; after that the game keeps it there itself.
                // Not for someone fading (losing blood): the game is letting them go for a reason.
                bool awake = (creature.GetParameterValue<CognitionLevel>() ?? 0f) >= 80f;
                // The game only gets someone up off the ground when they're asked to walk.
                if (awake && Fights.IsDown(creature) && now - since < RegainStrength + 10f)
                {
                    SyringeKinds.Hold<GeneralMuscleForce>(creature, 100f);
                    creature.SetWalking(true);
                    continue;
                }
                if (!Fights.IsFighting(creature))
                    creature.SetWalking(false);
                SyringeKinds.Recalculate<GeneralMuscleForce>(creature);
                Waking.Remove(key);
            }
            if (Limbs.Count == 0 || (_tick += dt) < 0.25f)
                return;
            dt = _tick;
            _tick = 0f;
            foreach (var key in Limbs.Keys.ToList())
            {
                var hurt = Limbs[key];
                if (!hurt.Limb.Exists() || !hurt.Creature.IsValid() || !(hurt.Creature.IsLiving() || IsKnockedOut(hurt.Creature))
                    || hurt.Limb.GetCreature()?.Pointer != hurt.Creature.Pointer)
                {
                    // Dead, deleted or cut off: nothing more to do for it.
                    Limbs.Remove(key);
                    continue;
                }
                hurt.Damage = Mathf.Max(0f, hurt.Damage - HealPerSecond * dt);
                if (hurt.Broken && hurt.Joint.Exists())
                    Limp(hurt.Joint);
                if (hurt.Bleeding > 0f)
                    Bleed(hurt, dt, now);
                if (hurt.Damage <= 0f && hurt.Bleeding <= 0f && !hurt.Broken)
                    Limbs.Remove(key);
            }
        }

        // Blood lost inside. It slows down by itself over a few minutes; a chest, belly or head that's bleeding
        // inside brings blood up out of the mouth now and then.
        private static void Bleed(Hurt hurt, float dt, float now)
        {
            var creature = hurt.Creature;
            float capacity = creature.GetBloodCapacity();
            if (capacity > 0f)
                creature.DrainBlood(capacity * BloodPerSecond * hurt.Bleeding * Weight(hurt.Part) * dt);
            hurt.Bleeding = Mathf.Max(0f, hurt.Bleeding - dt / 200f);
            bool deep = hurt.Part is HumanoidNodeTagValue.Spine or HumanoidNodeTagValue.Pelvis or HumanoidNodeTagValue.Head;
            if (!deep || now < hurt.NextCough || hurt.Bleeding < 0.2f)
                return;
            hurt.NextCough = now + Random.Range(3f, 9f) / Mathf.Max(0.4f, hurt.Bleeding);
            var head = creature.GetLimb(HumanoidNodeTagValue.Head);
            if (head == null)
                return;
            var mouth = head.GetPosition() + head.GetMovingTransform().forward * 0.12f - Vector3.up * 0.05f;
            Effects.Burst(mouth, new Color(0.5f, 0.02f, 0.03f), Mathf.RoundToInt(5 + 8 * hurt.Bleeding), 1.6f, 0.03f, 0.8f, false, 9.8f,
                head.GetMovingTransform().forward, 35f, new Color(0.28f, 0f, 0.01f));
            if (Physics.Raycast(mouth, Vector3.down, out var ground, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Blood.Drop(ground.point + head.GetMovingTransform().forward * 0.2f, ground.normal);
        }

        // How much blood a part has to lose: the chest and belly hold the most.
        private static float Weight(HumanoidNodeTagValue part) => part switch
        {
            HumanoidNodeTagValue.Spine or HumanoidNodeTagValue.Pelvis => 1.4f,
            HumanoidNodeTagValue.Head => 0.9f,
            HumanoidNodeTagValue.LeftLeg or HumanoidNodeTagValue.RightLeg => 0.8f,
            _ => 0.4f,
        };

        internal static void Reset()
        {
            Limbs.Clear();
            Out.Clear();
            Waking.Clear();
        }
    }

    /// <summary>The game's own blood splats on the ground.</summary>
    internal static class Blood
    {
        private static Il2CppVFX.Blood.BloodDecalsPool _pool;

        internal static void Drop(Vector3 point, Vector3 normal)
        {
            try
            {
                if (_pool == null || _pool.WasCollected)
                    _pool = UnityEngine.Object.FindObjectOfType<Il2CppVFX.Blood.BloodDecalsPool>();
                _pool?.TryDrawDecal(point, normal);
            }
            catch (Exception e)
            {
                FruktLog.Debug("A blood splat failed: " + e.Message);
            }
        }
    }
}
