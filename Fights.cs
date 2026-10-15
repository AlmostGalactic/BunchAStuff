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
    /// Fist fights. A fighter walks up to the nearest person who isn't on their team and boxes them: they circle,
    /// plant their feet and throw combinations, block and counter, tire out, get rocked by big shots and knocked
    /// out by enough of them. Someone too badly hurt to go on runs for it. Fighters have to be able to stand and
    /// walk, and what the fight does to their body stays (see <see cref="Injuries"/>).
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
            Shove,
        }

        // How hard each punch hits next to a cross, and what it costs in breath.
        private static float Power(Punch kind) => kind switch
        {
            Punch.Jab => 0.6f,
            Punch.Hook => 1.15f,
            Punch.Uppercut => 1.25f,
            Punch.Body => 0.9f,
            Punch.Shove => 0.2f,
            _ => 1f,
        };

        private static float Cost(Punch kind) => kind switch
        {
            Punch.Jab => 0.05f,
            Punch.Shove => 0.08f,
            Punch.Hook or Punch.Uppercut => 0.12f,
            _ => 0.09f,
        };

        private enum Footwork
        {
            // Walking in to reach.
            Closing,
            // Feet planted, trading punches.
            Planted,
            // Stepping round the other person to a new angle.
            Circling,
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
            // Breath: punching uses it up, it comes back between exchanges. Tired fighters are slower and weaker
            // and keep their hands lower.
            public float Stamina = 1f;
            public Footwork Feet;
            public float FeetUntil;
            public float CircleSide = 1f;
            // Rocked by a big shot: no punches back until this time.
            public float Dazed;
            public bool Out;
            public float CameRound = -10f, Idle, DownFor;
            // Closest they've got while walking in, and when.
            public float Nearest = float.MaxValue, NearestAt;
        }

        private static readonly Dictionary<IntPtr, Fighter> Fighters = new();
        // People who ran from a fight, and when they'll stop running.
        private static readonly Dictionary<IntPtr, (AbstractCreature Creature, Vector3 From, float Until)> Running = new();

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

        /// <summary>Punches thrown, and ones that touched nothing (for the self-test).</summary>
        internal static int PunchesThrown { get; private set; }
        internal static int PunchesMissed { get; private set; }

        /// <summary>Seconds fighters spent walking in, trading and circling (for the self-test).</summary>
        internal static float SecondsClosing, SecondsPlanted, SecondsCircling, SecondsDown;

        /// <summary>People knocked out in fights.</summary>
        internal static int KnockOuts { get; private set; }

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

        // Strength Syringe: punches land this much harder, until the time given.
        private static readonly Dictionary<IntPtr, float> Strong = new();
        // Berserk Syringe: they go for anyone at all, their own team too.
        private static readonly Dictionary<IntPtr, float> Berserk = new();

        internal static void Boost(AbstractCreature creature, float seconds)
        {
            if (creature != null)
                Strong[creature.Pointer] = Time.time + seconds;
        }

        internal static void GoBerserk(AbstractCreature creature, float seconds)
        {
            if (creature == null)
                return;
            Berserk[creature.Pointer] = Time.time + seconds;
            Boost(creature, seconds);
            if (!IsFighting(creature) && !Start(creature, out var why))
                Notifications.Warn(why);
        }

        private static bool Until(Dictionary<IntPtr, float> set, AbstractCreature creature)
            => creature != null && set.TryGetValue(creature.Pointer, out var until) && Time.time < until;

        private static bool Enemies(AbstractCreature me, AbstractCreature other) => Until(Berserk, me) || !Teams.AreAllies(me, other);

        /// <summary>
        /// An arm was broken: its hand is let go, so it hangs instead of being held up in the guard.
        /// </summary>
        internal static void LetGo(AbstractCreature creature, HumanoidNodeTagValue part)
        {
            bool? right = part switch
            {
                HumanoidNodeTagValue.RightArm or HumanoidNodeTagValue.RightForearm or HumanoidNodeTagValue.RightHand => true,
                HumanoidNodeTagValue.LeftArm or HumanoidNodeTagValue.LeftForearm or HumanoidNodeTagValue.LeftHand => false,
                _ => null,
            };
            if (creature == null || right == null || !creature.IsValid())
                return;
            try
            {
                Placer(creature, right.Value)?.RemovePosition();
            }
            catch
            {
                // The arm is already gone.
            }
            if (Fighters.TryGetValue(creature.Pointer, out var fighter) && fighter.Fist != null
                && (fighter.Fist.GetHumanPart() == HumanoidNodeTagValue.RightHand) == right.Value)
                fighter.Fist = null;
        }

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
            Running.Remove(creature.Pointer);
            Fighters[creature.Pointer] = new Fighter
            {
                Me = creature,
                Target = target,
                Retarget = Time.time + 2f,
                RightHand = Random.value < 0.5f,
                Tempo = Random.Range(0.3f, 0.65f),
                CircleSide = Random.value < 0.5f ? 1f : -1f,
                // Not both swinging the moment they meet.
                NextPunch = Time.time + Random.Range(0.2f, 1.2f),
            };
            // Whoever gets picked on fights back.
            if (Settings.FightBack && !IsFighting(target) && target.HasPuppeteer() && !Injuries.IsKnockedOut(target))
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
            foreach (var run in Running.Values)
            {
                if (run.Creature.IsValid())
                    run.Creature.SetWalking(false);
            }
            Running.Clear();
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

        /// <summary>The nearest living person who isn't <paramref name="me"/> or on my team, and is still on their feet.</summary>
        internal static AbstractCreature FindTarget(AbstractCreature me)
        {
            var target = Creatures.GetNearest(me.GetPosition(),
                c => c.Pointer != me.Pointer && c.IsLiving() && c.IsHuman() && c.HasPuppeteer() && Enemies(me, c)
                     && !Injuries.IsKnockedOut(c) && !Running.ContainsKey(c.Pointer));
            // Nobody left standing who wants a fight: go for anyone awake.
            return target ?? Creatures.GetNearest(me.GetPosition(),
                c => c.Pointer != me.Pointer && c.IsLiving() && c.IsHuman() && c.GetLimbCount() > 6 && Enemies(me, c)
                     && !Injuries.IsKnockedOut(c) && !Running.ContainsKey(c.Pointer));
        }

        internal static void Update()
        {
            if (!GameState.InSandbox)
                return;
            if (Running.Count > 0)
                KeepRunning();
            if (Fighters.Count == 0)
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
            float now = Time.time, dt = Time.deltaTime;
            if (!me.IsLiving() && !Injuries.IsKnockedOut(me) && !Injuries.IsWaking(me))
            {
                Stop(me);
                return;
            }
            // Out cold: arms down. They pick the fight up again when they've come round and got back up.
            if (Injuries.IsKnockedOut(me))
            {
                if (!fighter.Out)
                {
                    fighter.Out = true;
                    me.SetWalking(false);
                    DropGuard(fighter);
                }
                fighter.CameRound = now;
                return;
            }
            if (!me.HasPuppeteer() || !me.IsLiving())
            {
                // Still getting up after being knocked out; anyone else who can't stand is done.
                if (now - fighter.CameRound > 15f)
                    Stop(me);
                return;
            }
            // Knocked down: arms let go (held out, they'd stick out stiff), and they get up before going on.
            if (IsDown(me))
            {
                if (fighter.GuardUp || fighter.Fist != null)
                    DropGuard(fighter);
                // Coming round from a knockout, they're being got up (see Injuries.WakeUp).
                if (!Injuries.IsWaking(me))
                    me.SetWalking(false);
                SecondsDown += dt;
                if ((fighter.DownFor += dt) > 15f)
                    Stop(me);
                return;
            }
            fighter.DownFor = 0f;
            if (fighter.Out)
            {
                fighter.Out = false;
                fighter.Dazed = now + 1.5f;
                fighter.Stamina = Mathf.Max(fighter.Stamina, 0.5f);
            }
            if (TooHurt(fighter))
            {
                RunAway(fighter);
                return;
            }

            var target = fighter.Target;
            bool lost = target == null || !target.IsLiving() || !Enemies(me, target) || Injuries.IsKnockedOut(target)
                        || Running.ContainsKey(target.Pointer);
            if (lost || now >= fighter.Retarget)
            {
                fighter.Retarget = now + 1.5f;
                var nearest = FindTarget(me);
                // Stick with the current target unless someone is clearly closer.
                if (lost || (nearest != null && Flat(nearest.GetPosition() - me.GetPosition()) + 1f < Flat(target.GetPosition() - me.GetPosition())))
                {
                    fighter.Target = target = nearest;
                    if (target != null && Settings.FightBack && !IsFighting(target) && target.HasPuppeteer())
                        Start(target, me, out _);
                }
            }
            if (target == null)
            {
                // Nobody left to fight for now: hands down, catch a breath, and keep an eye out for a while.
                if (fighter.GuardUp)
                    DropGuard(fighter);
                me.SetWalking(false);
                fighter.Stamina = Mathf.Min(1f, fighter.Stamina + dt * 0.2f);
                if ((fighter.Idle += dt) > 20f)
                    Stop(me);
                return;
            }
            fighter.Idle = 0f;

            var offset = target.GetPosition() - me.GetPosition();
            float distance = Flat(offset);
            fighter.Guarding = distance < GuardRange;
            FollowFrame(fighter, offset);
            if (fighter.Guarding != fighter.GuardUp)
            {
                if (fighter.Guarding)
                    RaiseGuard(fighter, 1.5f);
                else
                    DropGuard(fighter);
            }
            else if (fighter.GuardUp && fighter.Fist == null && Random.value < dt * 2f)
            {
                // Tired hands sink.
                RaiseGuard(fighter, 1.5f);
            }
            if (fighter.Fist != null)
                UpdatePunch(fighter, target);

            // Breath comes back faster while moving round than while trading.
            fighter.Stamina = Mathf.Min(1f, fighter.Stamina + dt * (fighter.Feet == Footwork.Planted ? 0.06f : 0.16f));

            if (distance > Reach + 0.55f && fighter.Feet != Footwork.Circling && fighter.Feet != Footwork.Closing)
            {
                fighter.Feet = Footwork.Closing;
                fighter.Nearest = float.MaxValue;
                fighter.NearestAt = now;
            }
            if (fighter.Feet == Footwork.Closing)
                SecondsClosing += dt;
            else if (fighter.Feet == Footwork.Planted)
                SecondsPlanted += dt;
            else
                SecondsCircling += dt;
            switch (fighter.Feet)
            {
                case Footwork.Closing:
                    // Raised guards bump into each other a little short of where they're headed: once they're near
                    // and stop getting nearer, that's close enough.
                    if (distance < fighter.Nearest - 0.04f)
                    {
                        fighter.Nearest = distance;
                        fighter.NearestAt = now;
                    }
                    bool stalled = distance < Reach + 0.4f && now - fighter.NearestAt > 0.5f;
                    if (distance > Reach + 0.1f && !stalled)
                    {
                        me.WalkTowards(target.GetPosition());
                        return;
                    }
                    Plant(fighter, now);
                    break;
                case Footwork.Circling:
                    if (now >= fighter.FeetUntil || distance > Reach + 0.9f)
                    {
                        Plant(fighter, now);
                        break;
                    }
                    // A step round to the side, keeping about the same distance.
                    var round = Quaternion.Euler(0f, fighter.CircleSide * 50f, 0f) * -Flatten(offset).normalized * Reach;
                    me.WalkTowards(target.GetPosition() + round);
                    return;
                case Footwork.Planted:
                    if (now >= fighter.FeetUntil && fighter.Fist == null && fighter.Combo == 0)
                    {
                        // Out of the exchange: step round to a new angle, and catch a breath.
                        fighter.Feet = Footwork.Circling;
                        fighter.FeetUntil = now + Random.Range(0.35f, 0.8f) + (1f - fighter.Stamina) * 0.6f;
                        if (Random.value < 0.4f)
                            fighter.CircleSide = -fighter.CircleSide;
                        return;
                    }
                    break;
            }

            // They're down: no hitting them there. Guard up, and wait for them to get up.
            if (IsDown(target))
            {
                if (fighter.Fist == null)
                {
                    me.SetWalking(false);
                    me.FaceTowards(target.GetPosition());
                }
                return;
            }
            // Out of reach while trading: a step in, rather than swinging at the air.
            if (distance > Reach + 0.3f && fighter.Fist == null)
            {
                me.WalkTowards(target.GetPosition());
                return;
            }
            me.SetWalking(false);
            me.FaceTowards(target.GetPosition());
            if (now < fighter.Dazed)
                return;
            // Only once they're in range: a punch thrown at someone too far away just locks the arm out.
            if (now >= fighter.NextPunch && now - fighter.PunchStarted > PunchOut + PunchBack && fighter.Stamina > 0.08f
                && distance <= Reach + 0.3f)
            {
                fighter.NextPunch = now + NextGap(fighter);
                ThrowPunch(fighter, target, distance);
            }
        }

        private static void Plant(Fighter fighter, float now)
        {
            fighter.Nearest = float.MaxValue;
            fighter.NearestAt = now;
            fighter.Feet = Footwork.Planted;
            fighter.FeetUntil = now + Random.Range(2.2f, 4.5f) * Mathf.Lerp(0.6f, 1f, fighter.Stamina);
            fighter.Me.SetWalking(false);
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>Lying or sitting on the ground: the head isn't well above the hips any more.</summary>
        internal static bool IsDown(AbstractCreature creature)
        {
            var head = creature.GetLimb(HumanoidNodeTagValue.Head);
            var hips = creature.GetLimb(HumanoidNodeTagValue.Pelvis) ?? creature.GetRootLimb();
            if (head == null || hips == null)
                return true;
            return head.GetPosition().y - hips.GetPosition().y < 0.32f;
        }

        private static Vector3 Flatten(Vector3 v) => new(v.x, 0f, v.z);

        // ------------------------------------------------------------ giving up

        // Lost too much blood, or both arms are broken: no point going on.
        private static bool TooHurt(Fighter fighter)
        {
            var me = fighter.Me;
            float capacity = me.GetBloodCapacity();
            if (capacity > 0f && me.GetBlood() < capacity * 0.5f)
                return true;
            return !CanPunchWith(me, true) && !CanPunchWith(me, false);
        }

        private static bool CanPunchWith(AbstractCreature me, bool right)
        {
            var hand = me.GetLimb(right ? HumanoidNodeTagValue.RightHand : HumanoidNodeTagValue.LeftHand);
            if (hand == null)
                return false;
            var forearm = me.GetLimb(right ? HumanoidNodeTagValue.RightForearm : HumanoidNodeTagValue.LeftForearm);
            var arm = me.GetLimb(right ? HumanoidNodeTagValue.RightArm : HumanoidNodeTagValue.LeftArm);
            return !Injuries.IsBroken(forearm) && !Injuries.IsBroken(arm);
        }

        private static void RunAway(Fighter fighter)
        {
            var me = fighter.Me;
            var from = fighter.Target != null && fighter.Target.IsValid() ? fighter.Target.GetPosition() : me.GetPosition();
            Stop(me);
            Running[me.Pointer] = (me, from, Time.time + Random.Range(6f, 10f));
            Notifications.Show($"{me.GetDisplayName()} has had enough and runs for it.");
        }

        private static void KeepRunning()
        {
            float now = Time.time;
            foreach (var key in Running.Keys.ToList())
            {
                var (creature, from, until) = Running[key];
                if (!creature.IsValid() || !creature.IsLiving() || now >= until || Injuries.IsKnockedOut(creature))
                {
                    if (creature.IsValid())
                        creature.SetWalking(false);
                    Running.Remove(key);
                    continue;
                }
                var away = Flatten(creature.GetPosition() - from);
                if (away.sqrMagnitude < 0.01f)
                    away = Vector3.forward;
                creature.WalkTowards(creature.GetPosition() + away.normalized * 3f);
            }
        }

        // ------------------------------------------------------------ the arms

        // The arms go through the game's own hand IK, the same thing that moves a hand to cover a wound: the
        // animated body puts its hand where it's told and the physical arm follows it. Spots are given in a frame
        // that sits on the fighter's hips and faces the other person: x to the right, y up, z towards them.
        private static readonly Vector3 GuardRight = new(0.15f, 0.5f, 0.3f);
        private static readonly Vector3 GuardLeft = new(-0.15f, 0.5f, 0.35f);

        // The guard sinks as they tire and as their head takes a beating.
        private static Vector3 Guard(Fighter fighter, bool right)
        {
            var guard = right ? GuardRight : GuardLeft;
            float sag = (1f - fighter.Stamina) * 0.14f + Mathf.Min(0.1f, Injuries.DamageOf(fighter.Me.GetLimb(HumanoidNodeTagValue.Head)) * 0.03f);
            guard.y -= sag;
            return guard;
        }

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
            if (!CanPunchWith(creature, right))
                return;
            // Never further than an arm can reach from the shoulder: a hand sent past it locks the arm out straight.
            local.x = Mathf.Clamp(local.x, -0.45f, 0.45f);
            local.y = Mathf.Clamp(local.y, 0f, 0.62f);
            local.z = Mathf.Clamp(local.z, 0.05f, 0.85f);
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
            // A broken arm hangs; it doesn't come up (Place leaves it alone).
            Place(fighter.Me, true, Guard(fighter, true), fighter.Frame, speed, true);
            Place(fighter.Me, false, Guard(fighter, false), fighter.Frame, speed, true);
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
        /// The wait before the next punch. Everyone has their own pace, strings two to four punches together now
        /// and then, and slows down as they tire.
        /// </summary>
        private static float NextGap(Fighter fighter)
        {
            if (fighter.Combo > 0)
            {
                fighter.Combo--;
                return Random.Range(PunchOut + PunchBack, PunchOut + PunchBack + 0.12f);
            }
            if (Random.value < 0.35f + 0.35f * fighter.Stamina)
                fighter.Combo = Random.Range(1, fighter.Stamina > 0.6f ? 4 : 2);
            return fighter.Tempo * Random.Range(0.6f, 1.4f) * Mathf.Lerp(1.8f, 1f, fighter.Stamina);
        }

        // Combinations the way boxers throw them: a jab to set up, the cross behind it, a hook to finish.
        private static Punch Choose(Fighter fighter, float distance)
        {
            if (distance < Reach * 0.65f && Random.value < 0.5f)
                return Punch.Shove;
            if (fighter.Combo > 0 && fighter.Punches > 0)
            {
                return fighter.Kind switch
                {
                    Punch.Jab => Random.value < 0.7f ? Punch.Cross : Punch.Jab,
                    Punch.Cross => Random.value < 0.6f ? Punch.Hook : Punch.Body,
                    Punch.Body => Random.value < 0.6f ? Punch.Hook : Punch.Uppercut,
                    _ => Random.value < 0.5f ? Punch.Cross : Punch.Uppercut,
                };
            }
            float r = Random.value;
            return r < 0.38f ? Punch.Jab : r < 0.58f ? Punch.Cross : r < 0.72f ? Punch.Hook : r < 0.86f ? Punch.Body : Punch.Uppercut;
        }

        private static void ThrowPunch(Fighter fighter, AbstractCreature target, float distance)
        {
            var me = fighter.Me;
            if (!fighter.GuardUp || fighter.Frame == null)
                return;
            var kind = Choose(fighter, distance);
            // A jab is the lead (left) hand and a cross the rear (right); the rest alternate.
            bool right = kind switch
            {
                Punch.Jab => false,
                Punch.Cross => true,
                _ => !fighter.RightHand,
            };
            // A broken arm can't throw anything: the other one does.
            if (!CanPunchWith(me, right))
                right = !right;
            if (!CanPunchWith(me, right))
                return;
            fighter.RightHand = right;
            var fist = me.GetLimb(right ? HumanoidNodeTagValue.RightHand : HumanoidNodeTagValue.LeftHand);
            var aim = kind is Punch.Body or Punch.Shove
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
            PunchesThrown++;
            fighter.Stamina = Mathf.Max(0f, fighter.Stamina - Cost(kind));

            // Where it lands, in the fight frame (x right, y up from the hips, z towards them), a little past the
            // spot so the arm drives through it. Head shots stay at chin height, body shots at the stomach.
            float side = right ? 1f : -1f;
            var spot = fighter.Frame.InverseTransformPoint(aim.GetPosition());
            spot.y = kind is Punch.Body ? Mathf.Clamp(spot.y, 0.1f, 0.3f) : kind is Punch.Shove ? Mathf.Clamp(spot.y, 0.25f, 0.4f) : Mathf.Clamp(spot.y, 0.4f, 0.55f);
            spot.z += 0.08f;
            var guard = Guard(fighter, right);

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
                case Punch.Shove:
                    // Both hands to the chest, then push.
                    spot.x = side * 0.1f;
                    waypoint = new Vector3(side * 0.12f, spot.y, Mathf.Lerp(guard.z, spot.z, 0.5f));
                    Place(me, !right, new Vector3(-side * 0.1f, spot.y, spot.z), fighter.Frame, 7f, false);
                    break;
                default:
                    // Straight out from the guard, dropping a touch so it doesn't loop.
                    waypoint = new Vector3(Mathf.Lerp(guard.x, spot.x, 0.4f), Mathf.Min(guard.y, spot.y) - 0.04f, Mathf.Lerp(guard.z, spot.z, 0.5f));
                    break;
            }
            fighter.Strike = spot;
            Place(me, right, waypoint, fighter.Frame, kind == Punch.Jab ? 9f : 7f, false);
            if (kind is Punch.Hook or Punch.Uppercut or Punch.Cross)
                Sounds.Play(WhooshSFXType.WeaponAimWhoosh, fist.GetPosition(), 0.25f);
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
            if (!fighter.Connected)
                PunchesMissed++;
            // Back to the guard.
            Place(fighter.Me, right, Guard(fighter, right), fighter.Frame, 4f, false);
            if (fighter.Kind == Punch.Shove)
                Place(fighter.Me, !right, Guard(fighter, !right), fighter.Frame, 4f, false);
            fighter.Fist = null;
        }

        /// <summary>A punch lands when the fist touches the other person: whatever part of them it hits.</summary>
        private static void CheckConnect(Fighter fighter, AbstractLimb fist, AbstractCreature target)
        {
            if (fighter.Connected || !Enemies(fighter.Me, target))
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
            Fighters.TryGetValue(target.Pointer, out var defender);
            if (fighter.Kind == Punch.Shove)
            {
                // A shove goes through any guard.
                fighter.Connected = true;
                var torso = target.GetLimb(HumanoidNodeTagValue.Spine) ?? struck ?? guard;
                var push = Flatten(target.GetPosition() - fighter.Me.GetPosition()).normalized;
                torso.AddForceAtPosition(push * Settings.PunchForce * 4f, torso.GetPosition());
                Sounds.Play(ImpactSFXType.RbHit, torso.GetPosition(), 0.4f);
                if (defender != null)
                    defender.Dazed = Mathf.Max(defender.Dazed, Time.time + 0.4f);
                return;
            }
            if (guard != null && struck == null && !fighter.GuardDecided)
            {
                // The fist reached the raised guard first. A fresh, alert fighter catches most punches there; a
                // tired or dazed one lets more through (round the side, under it, or the guard was late).
                fighter.GuardDecided = true;
                float catches = defender == null ? 0.35f
                    : 0.3f + 0.4f * defender.Stamina - (Time.time < defender.Dazed ? 0.25f : 0f)
                      - Mathf.Min(0.2f, Injuries.DamageOf(target.GetLimb(HumanoidNodeTagValue.Head)) * 0.05f);
                if (fighter.Kind == Punch.Jab)
                    catches += 0.1f;
                if (Random.value > catches)
                {
                    // It gets past: the head or the body takes it.
                    fighter.Slipped = true;
                    struck = target.GetLimb(fighter.Kind == Punch.Body ? HumanoidNodeTagValue.Spine
                        : Random.value < 0.75f ? HumanoidNodeTagValue.Head : HumanoidNodeTagValue.Spine);
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
                Bruises.Add(guard, guardContact, guardContact - fistAt, 0.25f);
                Sounds.Play(ImpactSFXType.RbHit, guardContact, 0.5f);
                // Caught it: now it's their turn.
                if (defender != null && Time.time >= defender.Dazed && defender.Fist == null)
                {
                    defender.NextPunch = Mathf.Min(defender.NextPunch, Time.time + Random.Range(0.08f, 0.2f));
                    defender.Feet = Footwork.Planted;
                    defender.FeetUntil = Mathf.Max(defender.FeetUntil, Time.time + 0.8f);
                }
                return;
            }
            Land(fighter, fist, struck, contact, defender);
        }

        private static void Land(Fighter fighter, AbstractLimb fist, AbstractLimb struck, Vector3 contact, Fighter defender)
        {
            var me = fighter.Me;
            float power = Power(fighter.Kind) * Mathf.Lerp(0.55f, 1f, fighter.Stamina);
            if (Until(Strong, me))
                power *= 2.2f;
            // A hurt arm hits softer.
            var arm = me.GetLimb(fighter.RightHand ? HumanoidNodeTagValue.RightArm : HumanoidNodeTagValue.LeftArm);
            power *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(Injuries.DamageOf(arm) / 3f));
            var body = fist.GetRigidbody();
            var direction = body != null && body.velocity.sqrMagnitude > 0.5f ? body.velocity.normalized : (struck.GetPosition() - fist.GetPosition()).normalized;
            if (fighter.Kind == Punch.Uppercut)
                direction = (direction + Vector3.up).normalized;
            Injuries.Wound(struck, contact + direction * 0.02f, 2, Settings.PunchDamage * power * 30f, direction);
            struck.AddForceAtPosition(direction * Settings.PunchForce * power, contact);
            var target = struck.GetCreature();
            bool wasOut = Injuries.IsKnockedOut(target);
            Bruises.Add(struck, contact, -direction, Mathf.Clamp01((0.35f + Settings.PunchDamage) * power));
            // The puncher's knuckles take some of it too.
            Bruises.Add(fist, fist.GetPosition(), direction, 0.15f * power);
            Sounds.Play(power > 1f ? ImpactSFXType.FallDamageHardOrganic : ImpactSFXType.FallDamageLightOrganic, contact, Mathf.Lerp(0.6f, 1f, power - 0.4f));
            fighter.Landed++;
            PunchesLanded++;
            if (target != null && !wasOut && Injuries.IsKnockedOut(target))
            {
                KnockOuts++;
                // Down they go: a last shove from the punch that did it.
                var spine = target.GetLimb(HumanoidNodeTagValue.Spine);
                spine?.AddForceAtPosition(direction * Settings.PunchForce * 2.5f, spine.GetPosition());
                // A breather before the next one.
                fighter.Feet = Footwork.Circling;
                fighter.FeetUntil = Time.time + 1.5f;
                return;
            }
            if (defender == null)
                return;
            // Big shots, and punches that land while they're already tired, rock them for a moment.
            float rocked = power * (struck.GetHumanPart() == HumanoidNodeTagValue.Head ? 1f : 0.5f) * Mathf.Lerp(1.4f, 0.8f, defender.Stamina);
            if (rocked > 0.85f && Random.value < rocked - 0.6f)
            {
                defender.Dazed = Time.time + Random.Range(0.6f, 1.4f);
                defender.Stamina = Mathf.Max(0f, defender.Stamina - 0.15f);
                var spine = target.GetLimb(HumanoidNodeTagValue.Spine);
                spine?.AddForceAtPosition(direction * Settings.PunchForce * 1.5f, spine.GetPosition());
            }
            defender.Stamina = Mathf.Max(0f, defender.Stamina - (fighter.Kind == Punch.Body ? 0.12f : 0.04f) * power);
        }

        internal static void Reset()
        {
            foreach (var fighter in Fighters.Values)
            {
                if (fighter.Frame != null)
                    Object.Destroy(fighter.Frame.gameObject);
            }
            Fighters.Clear();
            Running.Clear();
            Strong.Clear();
            Berserk.Clear();
            PunchesLanded = 0;
            PunchesThrown = 0;
            PunchesMissed = 0;
            PunchesBlocked = 0;
            KnockOuts = 0;
        }
    }
}
