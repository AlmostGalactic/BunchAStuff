using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.UI;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using Il2CppLVA.Creatures;
using Il2CppLVA.Limbs;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using Il2CppLVA.Puppeteers.Variants.Humanoid;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BunchAStuff
{
    /// <summary>
    /// Fist fights. A fighter walks up to the nearest person who isn't on their team and punches them until they
    /// stop moving, then goes for the next one. Fighters have to be able to stand and walk.
    /// </summary>
    internal static class Fights
    {
        /// <summary>How close a fighter walks up before stopping to box, in metres (between the hips).</summary>
        internal const float Reach = 0.85f;

        /// <summary>Within this distance a fighter has their guard up.</summary>
        private const float GuardRange = 2.5f;

        // A punch: the fist goes out to a waypoint that gives it its arc, on to the target, then back to the guard.
        private const float PunchWindUp = 0.08f;
        private const float PunchOut = 0.24f;
        private const float PunchBack = 0.2f;

        internal enum Punch
        {
            Jab,
            Cross,
            Hook,
            Uppercut,
            Body,
        }

        private sealed class Fighter
        {
            public AbstractCreature Me;
            public AbstractCreature Target;
            public float NextPunch;
            public float Retarget;
            public bool RightHand;
            public int Punches, Landed;
            // The punch being thrown: which hand, at what, since when, and whether it has connected yet.
            public AbstractLimb Fist, Aim;
            public Vector3 Strike;
            public Punch Kind;
            public bool Struck;
            public int Blocked;
            // Their own rhythm: seconds between punches on average, and punches left in the current flurry.
            public float Tempo;
            public int Combo;
            public float PunchStarted = -10f;
            public bool Connected;
            public bool GuardDecided, Slipped;
            public bool Guarding;
            public Transform Frame;
            public bool GuardUp;
        }

        private static readonly Dictionary<IntPtr, Fighter> Fighters = new();

        internal static int Count => Fighters.Count;

        /// <summary>How high someone's fists are under their head, in metres: about 0.2 with the guard up (for the self-test).</summary>
        internal static float FistDrop(AbstractCreature creature)
        {
            var head = creature.GetLimb(HumanoidNodeTagValue.Head);
            var right = creature.GetLimb(HumanoidNodeTagValue.RightHand);
            var left = creature.GetLimb(HumanoidNodeTagValue.LeftHand);
            if (head == null || right == null || left == null)
                return float.PositiveInfinity;
            return head.GetPosition().y - (right.GetPosition().y + left.GetPosition().y) / 2f;
        }

        /// <summary>Punches that landed, all fights together (for the self-test).</summary>
        internal static int PunchesLanded { get; private set; }

        /// <summary>Punches caught on someone's guard, all fights together (for the self-test).</summary>
        internal static int PunchesBlocked { get; private set; }

        internal static void Create()
        {
            ContextMenus.AddAction(ContextMenuTarget.Limb,
                ctx => IsFighting(ctx.Creature) ? "Stop fighting" : "Fight nearest",
                ctx =>
                {
                    if (IsFighting(ctx.Creature))
                        Stop(ctx.Creature);
                    else if (!Start(ctx.Creature, out string why))
                        Notifications.Warn(why);
                },
                ctx => ctx.Creature != null && ctx.Creature.IsHuman(),
                priority: 490);
        }

        internal static bool IsFighting(AbstractCreature creature) => creature != null && Fighters.ContainsKey(creature.Pointer);

        internal static AbstractCreature TargetOf(AbstractCreature creature)
            => creature != null && Fighters.TryGetValue(creature.Pointer, out var fighter) ? fighter.Target : null;

        /// <summary>Makes someone fight the nearest person who isn't on their team.</summary>
        internal static bool Start(AbstractCreature creature, out string why) => Start(creature, null, out why);

        private static bool Start(AbstractCreature creature, AbstractCreature target, out string why)
        {
            why = null;
            if (creature == null || !creature.IsLiving() || !creature.IsHuman())
                why = "Only living people can fight.";
            else if (!creature.HasPuppeteer())
                why = $"{creature.GetDisplayName()} can't stand up to fight.";
            if (why != null)
                return false;
            target ??= FindTarget(creature);
            if (target == null)
            {
                why = Teams.Of(creature) == null ? "There's no one else to fight." : "There's no one from another team to fight.";
                return false;
            }
            Fighters[creature.Pointer] = new Fighter
            {
                Me = creature,
                Target = target,
                Retarget = Time.time + 2f,
                RightHand = Random.value < 0.5f,
                Tempo = Random.Range(0.5f, 1.1f),
                // Not both swinging the moment they meet.
                NextPunch = Time.time + Random.Range(0.2f, 1.2f),
            };
            // Whoever gets picked on fights back.
            if (Settings.FightBack && !IsFighting(target) && target.HasPuppeteer())
                Start(target, creature, out _);
            return true;
        }

        internal static void Stop(AbstractCreature creature)
        {
            if (creature == null || !Fighters.TryGetValue(creature.Pointer, out var fighter))
                return;
            Fighters.Remove(creature.Pointer);
            if (creature.IsValid())
            {
                creature.SetWalking(false);
                DropGuard(fighter);
            }
            if (fighter.Frame != null)
                Object.Destroy(fighter.Frame.gameObject);
        }

        internal static void StopAll()
        {
            foreach (var fighter in Fighters.Values.ToList())
                Stop(fighter.Me);
        }

        /// <summary>Every living person who can stand starts fighting. Returns how many did.</summary>
        internal static int EveryoneFights()
        {
            int started = 0;
            foreach (var human in Creatures.Humans.Select(h => (AbstractCreature)h).ToList())
            {
                if (IsFighting(human) || Start(human, out _))
                    started++;
            }
            return started;
        }

        /// <summary>The nearest living person who isn't <paramref name="me"/> or on my team.</summary>
        internal static AbstractCreature FindTarget(AbstractCreature me)
        {
            var target = Creatures.GetNearest(me.GetPosition(),
                c => c.Pointer != me.Pointer && c.IsLiving() && c.IsHuman() && c.HasPuppeteer() && !Teams.AreAllies(me, c));
            // Nobody left standing: go for the ones on the ground.
            return target ?? Creatures.GetNearest(me.GetPosition(),
                c => c.Pointer != me.Pointer && c.IsLiving() && c.IsHuman() && c.GetLimbCount() > 6 && !Teams.AreAllies(me, c));
        }

        internal static void Update()
        {
            if (Fighters.Count == 0 || !GameState.InSandbox)
                return;
            foreach (var fighter in Fighters.Values.ToList())
            {
                try
                {
                    Think(fighter);
                }
                catch (Exception e)
                {
                    FruktLog.Warning("A fighter got confused and stopped: " + e);
                    Fighters.Remove(fighter.Me.Pointer);
                }
            }
        }

        private static void Think(Fighter fighter)
        {
            var me = fighter.Me;
            if (!me.IsValid())
            {
                // Deleted or cut apart: nothing left to tidy up on the body.
                Fighters.Remove(me.Pointer);
                if (fighter.Frame != null)
                    Object.Destroy(fighter.Frame.gameObject);
                return;
            }
            if (!me.IsLiving() || !me.HasPuppeteer())
            {
                Stop(me);
                return;
            }

            var target = fighter.Target;
            bool lost = target == null || !target.IsLiving() || Teams.AreAllies(me, target);
            if (lost || Time.time >= fighter.Retarget)
            {
                fighter.Retarget = Time.time + 1.5f;
                var nearest = FindTarget(me);
                // Stick with the current target unless someone is clearly closer.
                if (lost || (nearest != null && Flat(nearest.GetPosition() - me.GetPosition()) + 1f < Flat(target.GetPosition() - me.GetPosition())))
                    fighter.Target = target = nearest;
            }
            if (target == null)
            {
                Stop(me);
                return;
            }

            var offset = target.GetPosition() - me.GetPosition();
            fighter.Guarding = Flat(offset) < GuardRange;
            FollowFrame(fighter, offset);
            if (fighter.Guarding != fighter.GuardUp)
            {
                if (fighter.Guarding)
                    RaiseGuard(fighter, 1.5f);
                else
                    DropGuard(fighter);
            }
            if (fighter.Fist != null)
                UpdatePunch(fighter, target);
            if (Flat(offset) > Reach)
            {
                me.WalkTowards(target.GetPosition());
                return;
            }
            me.SetWalking(false);
            me.FaceTowards(target.GetPosition());
            if (Time.time >= fighter.NextPunch && Time.time - fighter.PunchStarted > PunchOut + PunchBack)
            {
                fighter.NextPunch = Time.time + NextGap(fighter);
                ThrowPunch(fighter, target);
            }
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // ------------------------------------------------------------ the arms

        // The arms go through the game's own hand IK, the same thing that moves a hand to cover a wound: the
        // animated body puts its hand where it's told and the physical arm follows it. Spots are given in a frame
        // that sits on the fighter's hips and faces the other person: x to the right, y up, z towards them.
        private static readonly Vector3 GuardRight = new(0.15f, 0.5f, 0.3f);
        private static readonly Vector3 GuardLeft = new(-0.15f, 0.5f, 0.35f);

        private static void FollowFrame(Fighter fighter, Vector3 towardsTarget)
        {
            if (fighter.Frame == null)
                fighter.Frame = new GameObject("Bunch-A-Stuff fight frame").transform;
            towardsTarget.y = 0f;
            fighter.Frame.position = fighter.Me.GetPosition();
            if (towardsTarget.sqrMagnitude > 1e-4f)
                fighter.Frame.rotation = Quaternion.LookRotation(towardsTarget.normalized);
        }

        private static HandEffectorPlacer Placer(AbstractCreature creature, bool right)
        {
            try
            {
                var ik = creature.GetPuppeteer()?.HumanoidReferences?.IK;
                return right ? ik?.RightHandEffectorPlacer : ik?.LeftHandEffectorPlacer;
            }
            catch
            {
                return null;
            }
        }

        private static void Place(AbstractCreature creature, bool right, Vector3 local, Transform frame, float speed, bool smooth)
        {
            var placer = Placer(creature, right);
            if (placer == null || frame == null)
                return;
            var none = new Il2CppSystem.Nullable<Vector3>(Vector3.zero) { hasValue = false };
            var noRotation = new Il2CppSystem.Nullable<Quaternion>(Quaternion.identity) { hasValue = false };
            try
            {
                placer.SetLocalPosition(local, frame, speed, smooth, none, noRotation, null);
            }
            catch
            {
                // That hand has been cut off or the body is coming apart: the other arm carries on.
            }
        }

        private static void RaiseGuard(Fighter fighter, float speed)
        {
            Place(fighter.Me, true, GuardRight, fighter.Frame, speed, true);
            Place(fighter.Me, false, GuardLeft, fighter.Frame, speed, true);
            fighter.GuardUp = true;
        }

        private static void DropGuard(Fighter fighter)
        {
            foreach (bool right in new[] { true, false })
            {
                try
                {
                    Placer(fighter.Me, right)?.RemovePosition();
                }
                catch
                {
                    // The body is already gone.
                }
            }
            fighter.GuardUp = false;
            fighter.Fist = null;
        }

        // ------------------------------------------------------------ punching

        /// <summary>
        /// The wait before the next punch. Everyone has their own pace, and now and then strings two or three
        /// punches together quickly before backing off.
        /// </summary>
        private static float NextGap(Fighter fighter)
        {
            if (fighter.Combo > 0)
            {
                fighter.Combo--;
                return Random.Range(PunchOut + PunchBack, PunchOut + PunchBack + 0.12f);
            }
            if (Random.value < 0.35f)
                fighter.Combo = Random.Range(1, 3);
            return fighter.Tempo * Random.Range(0.6f, 1.6f);
        }

        private static void ThrowPunch(Fighter fighter, AbstractCreature target)
        {
            var me = fighter.Me;
            if (!fighter.GuardUp || fighter.Frame == null)
                return;
            var kind = (Punch)Random.Range(0, 5);
            // A jab is the lead (left) hand and a cross the rear (right); the rest alternate.
            fighter.RightHand = kind switch
            {
                Punch.Jab => false,
                Punch.Cross => true,
                _ => !fighter.RightHand,
            };
            var fist = me.GetLimb(fighter.RightHand ? HumanoidNodeTagValue.RightHand : HumanoidNodeTagValue.LeftHand);
            var aim = kind == Punch.Body
                ? target.GetLimb(HumanoidNodeTagValue.Spine) ?? target.GetRootLimb()
                : target.GetLimb(HumanoidNodeTagValue.Head) ?? target.GetLimb(HumanoidNodeTagValue.Spine);
            if (fist == null || aim == null)
                return;
            fighter.Kind = kind;
            fighter.Fist = fist;
            fighter.Aim = aim;
            fighter.PunchStarted = Time.time;
            fighter.Connected = false;
            fighter.GuardDecided = false;
            fighter.Slipped = false;
            fighter.Struck = false;
            fighter.Punches++;

            // Where it lands, in the fight frame (x right, y up from the hips, z towards them), a little past the
            // spot so the arm drives through it. Head shots stay at chin height, body shots at the stomach.
            float side = fighter.RightHand ? 1f : -1f;
            var spot = fighter.Frame.InverseTransformPoint(aim.GetPosition());
            spot.y = kind == Punch.Body ? Mathf.Clamp(spot.y, 0.1f, 0.3f) : Mathf.Clamp(spot.y, 0.4f, 0.55f);
            spot.z += 0.08f;
            var guard = fighter.RightHand ? GuardRight : GuardLeft;

            // The arc: the fist goes through this point first, then on to the spot.
            Vector3 waypoint;
            switch (kind)
            {
                case Punch.Hook:
                    // Out wide to the side, then round onto the head.
                    waypoint = new Vector3(side * 0.42f, Mathf.Lerp(guard.y, spot.y, 0.5f), Mathf.Lerp(guard.z, spot.z, 0.35f));
                    break;
                case Punch.Uppercut:
                    // Drops low and close, then drives up under the chin.
                    spot.z -= 0.05f;
                    waypoint = new Vector3(side * 0.12f, 0.15f, Mathf.Lerp(guard.z, spot.z, 0.5f));
                    break;
                case Punch.Body:
                    // Drops to stomach height, then drives in.
                    waypoint = new Vector3(side * 0.2f, Mathf.Lerp(guard.y, spot.y, 0.6f), Mathf.Lerp(guard.z, spot.z, 0.4f));
                    break;
                default:
                    // Straight out from the guard, dropping a touch so it doesn't loop.
                    waypoint = new Vector3(Mathf.Lerp(guard.x, spot.x, 0.4f), Mathf.Min(guard.y, spot.y) - 0.04f, Mathf.Lerp(guard.z, spot.z, 0.5f));
                    break;
            }
            fighter.Strike = spot;
            Place(me, fighter.RightHand, waypoint, fighter.Frame, kind == Punch.Jab ? 9f : 7f, false);
        }

        private static void UpdatePunch(Fighter fighter, AbstractCreature target)
        {
            float t = Time.time - fighter.PunchStarted;
            bool right = fighter.Fist.GetHumanPart() == HumanoidNodeTagValue.RightHand;
            if (!fighter.Struck && t >= PunchWindUp)
            {
                // From the waypoint onto the target.
                fighter.Struck = true;
                Place(fighter.Me, right, fighter.Strike, fighter.Frame, fighter.Kind == Punch.Jab ? 10f : fighter.Kind == Punch.Hook ? 6f : 8f, false);
            }
            if (t <= PunchOut)
            {
                if (fighter.Struck)
                    CheckConnect(fighter, fighter.Fist, target);
                return;
            }
            // Back to the guard.
            Place(fighter.Me, right, right ? GuardRight : GuardLeft, fighter.Frame, 4f, false);
            fighter.Fist = null;
        }

        /// <summary>A punch lands when the fist touches the other person: whatever part of them it hits.</summary>
        private static void CheckConnect(Fighter fighter, AbstractLimb fist, AbstractCreature target)
        {
            if (fighter.Connected || Teams.AreAllies(fighter.Me, target))
                return;
            var fistAt = fist.GetPosition();
            AbstractLimb struck = null, guard = null;
            Vector3 contact = default, guardContact = default;
            foreach (var collider in Physics.OverlapSphere(fistAt, 0.13f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var limb = Creatures.LimbFromCollider(collider);
                if (limb == null || limb.GetCreature()?.Pointer != target.Pointer)
                    continue;
                var overlapped = limb.GetHumanPart();
                bool isGuard = overlapped is HumanoidNodeTagValue.LeftHand or HumanoidNodeTagValue.RightHand
                    or HumanoidNodeTagValue.LeftForearm or HumanoidNodeTagValue.RightForearm;
                if (isGuard && guard == null)
                {
                    guard = limb;
                    guardContact = collider.ClosestPoint(fistAt);
                }
                else if (!isGuard && struck == null)
                {
                    struck = limb;
                    contact = collider.ClosestPoint(fistAt);
                }
            }
            // Once a punch has slipped past the guard, only the head and body count for the rest of it.
            if (fighter.Slipped)
                guard = null;
            if (struck == null && guard == null)
                return;
            if (guard != null && struck == null && !fighter.GuardDecided)
            {
                // The fist reached the raised guard first. About half the time it's caught there, and otherwise it
                // gets past (round the side, under it, or the guard was late) and carries on to the head or body.
                fighter.GuardDecided = true;
                if (Random.value > 0.5f)
                {
                    // It gets past: the head or the body takes it.
                    fighter.Slipped = true;
                    struck = target.GetLimb(Random.value < 0.5f ? HumanoidNodeTagValue.Head : HumanoidNodeTagValue.Spine);
                    if (struck == null)
                        return;
                    contact = struck.GetPosition();
                    guard = null;
                }
            }
            fighter.Connected = true;
            if (guard != null && (struck == null || Random.value < 0.5f))
            {
                fighter.Blocked++;
                PunchesBlocked++;
                Bruises.Add(guard, guardContact, guardContact - fistAt, 0.3f);
                Sounds.Play(ImpactSFXType.RbHit, guardContact, 0.5f);
                return;
            }
            var body = fist.GetRigidbody();
            var direction = body != null && body.velocity.sqrMagnitude > 0.5f ? body.velocity.normalized : (struck.GetPosition() - fistAt).normalized;
            Damage.Apply(struck, contact, radiusVoxels: 2, strength: Settings.PunchDamage, direction: direction);
            struck.AddForceAtPosition(direction * Settings.PunchForce, contact);
            Bruises.Add(struck, contact, -direction, Mathf.Clamp01(0.45f + Settings.PunchDamage));
            Sounds.Play(ImpactSFXType.FallDamageLightOrganic, contact, 0.9f);
            fighter.Landed++;
            PunchesLanded++;
        }

        internal static void Reset()
        {
            foreach (var fighter in Fighters.Values)
            {
                if (fighter.Frame != null)
                    Object.Destroy(fighter.Frame.gameObject);
            }
            Fighters.Clear();
            PunchesLanded = 0;
            PunchesBlocked = 0;
        }
    }
}
