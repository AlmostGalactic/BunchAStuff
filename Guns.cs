using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.Utilities;
using Random = UnityEngine.Random;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using Il2CppLVA.Limbs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>Where the guns sit in the hand, relative to where the game holds its own items.</summary>
    internal static class Hand
    {
        internal static readonly Vector3 Rotation = new(0f, 0f, 0f);
    }

    /// <summary>The guns, under Weapons in the terminal, and what each one does when it fires.</summary>
    internal static class Guns
    {
        internal const float Range = 400f;

        internal static readonly List<ModGun> All = new();
        private static readonly List<Rocket> Rockets = new();
        private static readonly HashSet<Rigidbody> FrozenBodies = new();

        internal static ModGun Smg, Shotgun, Railgun, Launcher, AirCannon, FreezeGun;

        internal static void Create()
        {
            Smg = Add("Moth-9", 0.075f, true, GunModels.SmgMuzzle, FireSmg, GunModels.Smg(), new Vector3(0.02f, -0.02f, 0.1f), 0.85f,
                "Submachine gun. Fires for as long as the trigger is held.", ("caliber", "9mm"), ("fire", "automatic"));

            Shotgun = Add("Barrow-12", 0.85f, false, GunModels.ShotgunMuzzle, FireShotgun, GunModels.Shotgun(), new Vector3(0f, -0.02f, 0.2f), 0.8f,
                "Pump shotgun. Nine pellets per shot.", ("caliber", "12ga"), ("fire", "pump"));

            Railgun = Add("Heron-R", 1.2f, false, GunModels.RailgunMuzzle, FireRailgun, GunModels.Railgun(), new Vector3(0f, -0.02f, 0.2f), 0.75f,
                "Rail rifle. A 7.62 round at several times the speed, through everything in a line.", ("caliber", "7.62"), ("fire", "single"));

            Launcher = Add("Tusk-40", 1.3f, false, GunModels.LauncherMuzzle, FireLauncher, GunModels.Launcher(), new Vector3(0.1f, -0.13f, 0.32f), 0.9f,
                "Rocket launcher. The rocket explodes on impact.", ("fire", "single"), ("blast radius", "4.5 m"));

            AirCannon = Add("Gale-2", 0.6f, false, GunModels.AirCannonMuzzle, FireAirCannon, GunModels.AirCannon(), new Vector3(0f, 0f, 0.12f), 0.75f,
                "Air cannon. Throws whatever it hits without damaging it.", ("fire", "single"));

            FreezeGun = Add("Halt-1", 0.3f, false, GunModels.FreezeGunMuzzle, FireFreezeGun, GunModels.FreezeGun(), new Vector3(0f, 0f, 0.1f), 0.85f,
                "Holds whatever it hits still in mid-air. Hit it again to let go.", ("fire", "single"));
        }

        private static ModGun Add(string name, float cooldown, bool automatic, Vector3 muzzle, Action<ModGun> fire, GameObject model,
            Vector3 hold, float scale, string description, params (string Key, string Value)[] card)
        {
            var gun = Inventory.AddGun(name)
                .WithCooldown(cooldown, automatic)
                .WithMuzzle(muzzle)
                .OnFire(fire);
            gun.WithDescription(description)
                .WithModel(model, hold, Hand.Rotation, scale);
            foreach (var (key, value) in card)
                gun.WithCard(key, value);
            All.Add(gun);
            return gun;
        }

        // ------------------------------------------------------------ the guns

        /// <summary>From the barrel to what's under the crosshair, a little off if the gun isn't steady.</summary>
        private static Vector3 Aim(ModGun gun, float spreadDegrees)
        {
            var toAim = Bullets.AimPoint() - gun.Muzzle;
            var direction = toAim.sqrMagnitude > 1f ? toAim.normalized : LocalPlayer.Forward;
            return Bullets.Spread(direction, spreadDegrees);
        }

        private static void FireSmg(ModGun gun)
        {
            var direction = Aim(gun, 1.4f);
            Bullets.Launch(gun.Muzzle, direction, Bullets.Caliber.Pistol);
            Effects.MuzzleFlash(gun.Muzzle, direction);
            Sounds.Play(WeaponSFXType.Shoot9MM, gun.Muzzle, 0.55f);
        }

        private static void FireShotgun(ModGun gun)
        {
            var centre = Aim(gun, 0f);
            for (int i = 0; i < 9; i++)
                Bullets.Launch(gun.Muzzle, Bullets.Spread(centre, 3.2f), Bullets.Caliber.Pellet);
            Effects.MuzzleFlash(gun.Muzzle, centre, 1.8f);
            Effects.Burst(gun.Muzzle, new Color(1f, 0.7f, 0.3f), 14, 12f, 0.04f, 0.18f, true, 0f, centre, 30f, new Color(1f, 0.3f, 0.05f));
            Effects.Smoke(gun.Muzzle + centre * 0.1f, 0.22f, 1.1f, centre * 2.5f, 0.3f);
            Effects.Smoke(gun.Muzzle + centre * 0.2f, 0.18f, 0.9f, centre * 1.5f + Vector3.up * 0.4f, 0.3f);
            Sounds.Play(WeaponSFXType.Shoot12ga, gun.Muzzle);
            Scheduler.After(0.3f, () => Sounds.Play(WeaponSFXType.RackBack12ga, LocalPlayer.CameraPosition, 0.7f), realtime: false);
            Scheduler.After(0.45f, () => Sounds.Play(WeaponSFXType.RackForth12ga, LocalPlayer.CameraPosition, 0.7f), realtime: false);
        }

        private static void FireRailgun(ModGun gun)
        {
            var direction = Aim(gun, 0f);
            Bullets.Launch(gun.Muzzle, direction, Bullets.Caliber.Rifle, 600f);
            var end = gun.Muzzle + direction * Bullets.DefaultRange;
            if (Physics.Raycast(gun.Muzzle, direction, out var hit, Bullets.DefaultRange, Layers.Gameplay, QueryTriggerInteraction.Ignore))
                end = hit.point;
            Bullets.Tracer(gun.Muzzle, end, new Color(0.3f, 0.9f, 1f), 0.05f, 0.35f);
            Bullets.Tracer(gun.Muzzle, end, Color.white, 0.015f, 0.2f);
            Effects.Flash(gun.Muzzle, new Color(0.3f, 0.9f, 1f), 6f, 0.12f, 6f);
            Effects.Burst(gun.Muzzle, new Color(0.7f, 1f, 1f), 16, 14f, 0.035f, 0.2f, true, 0f, direction, 25f, new Color(0.1f, 0.6f, 0.9f));
            Effects.Burst(end, new Color(0.7f, 1f, 1f), 24, 9f, 0.04f, 0.5f, true, 12f, -direction, 120f, new Color(0.1f, 0.5f, 0.8f), true);
            Sounds.Play(WeaponSFXType.Shoot762, gun.Muzzle);
        }

        private static void FireLauncher(ModGun gun)
        {
            // From the barrel to whatever is under the crosshair, so it lands where the player aimed.
            var direction = (Bullets.AimPoint() - gun.Muzzle).normalized;
            Rockets.Add(new Rocket(gun.Muzzle, direction));
            Effects.MuzzleFlash(gun.Muzzle, direction, 2.4f);
            Effects.Burst(gun.Muzzle, new Color(1f, 0.8f, 0.4f), 18, 9f, 0.1f, 0.3f, true, -1f, direction, 35f, new Color(0.8f, 0.2f, 0.05f));
            // The back blast: fire and smoke out of the rear of the tube.
            var rear = gun.Muzzle - direction * 0.95f;
            Effects.Burst(rear, new Color(1f, 0.8f, 0.4f), 22, 8f, 0.11f, 0.35f, true, -1f, -direction, 40f, new Color(0.7f, 0.15f, 0.04f));
            for (int i = 0; i < 7; i++)
                Effects.Smoke(rear, 0.22f, 1.3f, -direction * Random.Range(2f, 5f) + Vector3.up * 0.6f, 0.22f);
            Effects.Flash(rear, new Color(1f, 0.6f, 0.25f), 6f, 0.15f, 4f);
            Sounds.Play(WhooshSFXType.WeaponAimWhoosh, gun.Muzzle);
            Sounds.Play(WeaponSFXType.Shoot12ga, gun.Muzzle, 0.4f);
        }

        private static void FireAirCannon(ModGun gun)
        {
            var ray = LocalPlayer.AimRay;
            var shot = Bullets.Fire(ray, radiusVoxels: 0, push: 0f, sound: false);
            if (shot.Creature != null)
                shot.Creature.AddForce(ray.direction * 22f + Vector3.up * 9f, ForceMode.VelocityChange);
            else if (shot.Body != null && !shot.Body.isKinematic)
                shot.Body.AddForce(ray.direction * 22f + Vector3.up * 9f, ForceMode.VelocityChange);
            Bullets.Tracer(gun.Muzzle, shot.End, new Color(0.85f, 0.4f, 1f), 0.06f, 0.15f);
            Effects.Burst(gun.Muzzle, new Color(0.95f, 0.85f, 1f), 26, 11f, 0.07f, 0.4f, false, 0f, ray.direction, 38f, new Color(0.6f, 0.4f, 0.8f));
            Effects.Burst(gun.Muzzle, new Color(1f, 0.6f, 0.95f), 10, 8f, 0.05f, 0.25f, true, 0f, ray.direction, 25f, new Color(0.5f, 0.2f, 0.7f));
            if (shot.Hit)
                Effects.Burst(shot.End, new Color(0.95f, 0.85f, 1f), 20, 6f, 0.07f, 0.4f, false, 4f, shot.Raycast.normal, 150f, new Color(0.5f, 0.4f, 0.7f));
            Sounds.Play(WhooshSFXType.SpinnerAirWhoosh, gun.Muzzle);
        }

        private static void FireFreezeGun(ModGun gun)
        {
            var shot = Bullets.Fire(LocalPlayer.AimRay, radiusVoxels: 0, push: 0f, sound: false);
            if (shot.Creature != null)
                ToggleFrozen(shot.Creature.GetLimbs().Select(l => l.GetRigidbody()).Where(b => b.Exists()).ToList());
            else if (shot.Body != null)
                ToggleFrozen(new List<Rigidbody> { shot.Body });
            Bullets.Tracer(gun.Muzzle, shot.End, new Color(0.6f, 0.9f, 1f), 0.02f, 0.2f);
            Effects.Burst(gun.Muzzle, new Color(0.8f, 1f, 1f), 14, 7f, 0.04f, 0.3f, true, 0f, shot.End - gun.Muzzle, 20f, new Color(0.2f, 0.6f, 0.9f));
            if (shot.Hit)
            {
                Effects.Burst(shot.End, new Color(0.85f, 1f, 1f), 28, 6f, 0.06f, 0.9f, true, 9f, shot.Raycast.normal, 160f, new Color(0.25f, 0.65f, 0.95f), true);
                Effects.Flash(shot.End, new Color(0.5f, 0.9f, 1f), 5f, 0.25f, 3f);
            }
            Sounds.Play(ToolsSFXType.PinSound, shot.End);
        }

        /// <summary>Freezes the bodies, or thaws them if the Halt-1 froze them.</summary>
        private static void ToggleFrozen(List<Rigidbody> bodies)
        {
            bool thaw = bodies.Any(FrozenBodies.Contains);
            foreach (var body in bodies)
            {
                if (thaw)
                {
                    if (FrozenBodies.Remove(body))
                        body.isKinematic = false;
                }
                else if (!body.isKinematic)
                {
                    body.isKinematic = true;
                    FrozenBodies.Add(body);
                }
            }
        }

        internal static bool IsFrozen(Rigidbody body) => FrozenBodies.Contains(body);

        // ------------------------------------------------------------ rockets

        private const float BlastRadius = 4.5f;

        // Body parts waiting to be torn, each with all its wounds. Tearing a part rebuilds its voxels, which takes a
        // few milliseconds, so a blast's parts are spread over the next frames within a time budget.
        private static readonly Queue<(Collider Collider, List<(Vector3 Point, int RadiusVoxels, float Strength)> Wounds, Vector3 Direction)> Torn = new();
        private const double TearBudgetMs = 4.0;
        // Limbs this close to a blast can be torn clean off; on the blast they always are.
        private const float RipRadius = 1.8f;
        private const float ShredRadius = 0.7f;
        // Limbs to rip off, deepest first so each comes away on its own, and how fast to throw them.
        private static readonly Queue<(AbstractLimb Limb, Vector3 Velocity)> Ripped = new();

        /// <summary>
        /// Rips into every body part in range, harder the closer it is: a strong wound where the blast meets the part,
        /// and for parts close by another from the middle, so people near the blast lose whole limbs. The work grows
        /// with the cube of a wound's radius, so the wounds are kept small and made strong instead.
        /// </summary>
        internal static void Tear(Vector3 at)
        {
            var done = new HashSet<IntPtr>();
            var rip = new List<(AbstractLimb Limb, int Depth, Vector3 Velocity)>();
            float reach = Mathf.Clamp(Settings.ExplosionDamage, 0f, 2f);
            foreach (var collider in Physics.OverlapSphere(at, BlastRadius * 1.1f, Layers.Puppet, QueryTriggerInteraction.Ignore).ToList())
            {
                var limb = Creatures.LimbFromCollider(collider);
                if (limb == null || !done.Add(limb.Pointer))
                    continue;
                var closest = collider.ClosestPoint(at);
                float falloff = 1f - Vector3.Distance(at, closest) / (BlastRadius * 1.1f);
                if (falloff <= 0f)
                    continue;
                var away = (limb.GetPosition() - at).normalized;
                float strength = Settings.ExplosionDamage * Mathf.Lerp(40f, 160f, falloff);
                var wounds = new List<(Vector3, int, float)> { (closest, Mathf.RoundToInt(Mathf.Lerp(6f, 12f, falloff)), strength) };
                if (falloff > 0.4f)
                    wounds.Add((collider.bounds.center, Mathf.RoundToInt(Mathf.Lerp(4f, 10f, falloff)), strength));
                Torn.Enqueue((collider, wounds, away));
                limb.AddForceAtPosition(away * 45f * falloff, closest);

                // Close up, limbs come off. Right on the blast, everything does.
                float distance = Vector3.Distance(at, closest);
                if (reach <= 0f || distance > RipRadius * reach || limb.GetParentLimb() == null)
                    continue;
                float chance = distance < ShredRadius * reach ? 1f : Mathf.InverseLerp(RipRadius * reach, ShredRadius * reach, distance) * 0.9f + 0.1f;
                if (Random.value > chance)
                    continue;
                var fling = (away + Vector3.up * 0.6f + Random.insideUnitSphere * 0.5f).normalized * Mathf.Lerp(9f, 24f, falloff);
                rip.Add((limb, Depth(limb), fling));
            }
            foreach (var (limb, _, velocity) in rip.OrderByDescending(r => r.Depth))
                Ripped.Enqueue((limb, velocity));
        }

        private static int Depth(AbstractLimb limb)
        {
            int depth = 0;
            for (var up = limb.GetParentLimb(); up != null && depth < 32; up = up.GetParentLimb())
                depth++;
            return depth;
        }

        /// <summary>Tears a limb off and throws it, with a spray of blood where it came away.</summary>
        private static void RipOff(AbstractLimb limb, Vector3 velocity)
        {
            if (!limb.Exists() || limb.GetParentLimb() == null)
                return;
            var at = limb.GetPosition();
            if (!limb.Detach())
                return;
            var body = limb.GetRigidbody();
            if (body.Exists())
            {
                body.AddForce(velocity, ForceMode.VelocityChange);
                body.AddTorque(Random.onUnitSphere * Random.Range(10f, 30f), ForceMode.VelocityChange);
            }
            Effects.Burst(at, new Color(0.55f, 0.02f, 0.02f), 14, 5f, 0.05f, 1.1f, false, 9f, velocity.normalized, 70f,
                new Color(0.25f, 0f, 0f), true);
        }

        /// <summary>The blast: wounds, shoves and throws people, and the fire and smoke.</summary>
        internal static void Detonate(Vector3 at)
        {
            Tear(at);
            // Whole bodies get thrown, not just the limbs nearest the blast; the dead too.
            foreach (var creature in Creatures.All.ToList())
            {
                var offset = creature.GetPosition() - at;
                float falloff = 1f - offset.magnitude / (BlastRadius * 1.6f);
                if (falloff <= 0f)
                    continue;
                var away = (offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.up) + Vector3.up * 0.7f;
                creature.AddForce(away.normalized * 10f * falloff, ForceMode.VelocityChange);
            }
            Effects.Explosion(at, BlastRadius);
        }

        /// <summary>Flies a rocket from a point along a direction (the Tusk-40 does this; the self-test too).</summary>
        internal static void LaunchRocket(Vector3 from, Vector3 direction) => Rockets.Add(new Rocket(from, direction.normalized));

        private sealed class Rocket
        {
            private const float Speed = 32f;
            private const float Life = 5f;
            private readonly GameObject _body;
            private Vector3 _velocity;
            private readonly float _born;
            private float _trail;
            private readonly Light _light;
            private bool _puff;

            internal Rocket(Vector3 position, Vector3 direction)
            {
                _body = Object.Instantiate(GunModels.RocketPrototype);
                _body.transform.localScale = Vector3.one * 1.8f;
                _body.transform.position = position;
                _body.transform.rotation = Quaternion.LookRotation(direction);
                _body.SetActive(true);
                _light = _body.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = new Color(1f, 0.6f, 0.2f);
                _light.range = 5f;
                _light.intensity = 3f;
                _light.shadows = LightShadows.None;
                _velocity = direction * Speed;
                _born = Time.time;
            }

            internal Vector3 Position => _body.transform.position;

            /// <summary>Moves on; true once it has exploded.</summary>
            internal bool Step(float dt)
            {
                if (!_body.Exists())
                    return true;
                _velocity += Physics.gravity * 0.15f * dt;
                var from = _body.transform.position;
                var step = _velocity * dt;
                if (Physics.Raycast(from, step.normalized, out var hit, step.magnitude, Layers.Gameplay, QueryTriggerInteraction.Ignore))
                {
                    Explode(hit.point + hit.normal * 0.1f);
                    return true;
                }
                _body.transform.position = from + step;
                _body.transform.rotation = Quaternion.LookRotation(_velocity);
                // Fire out of the back and a trail of smoke behind it.
                // The same trail at any frame rate: a flame every 1/60 s and smoke every other one.
                var back = -_velocity.normalized;
                _trail += dt;
                for (int n = 0; _trail >= 1f / 60f && n < 3; n++)
                {
                    _trail -= 1f / 60f;
                    var tail = _body.transform.position + back * (0.28f + _trail * Speed);
                    Effects.Flame(tail, 0.14f, 0.22f, back * 4f);
                    if ((_puff = !_puff))
                        Effects.Smoke(tail, 0.24f, 1.1f, back * 0.8f, 0.25f);
                }
                _trail = Mathf.Min(_trail, 1f / 60f);
                _light.intensity = Random.Range(2.2f, 3.6f);
                if (Time.time - _born > Life)
                {
                    Explode(_body.transform.position);
                    return true;
                }
                return false;
            }

            private void Explode(Vector3 at)
            {
                Object.Destroy(_body);
                Detonate(at);
                Sounds.Play(ImpactSFXType.Bullet12gaHardSurface, at);
                Sounds.Play(WeaponSFXType.Shoot12ga, at);
                Exploded?.Invoke(at);
            }

            internal void Destroy()
            {
                if (_body.Exists())
                    Object.Destroy(_body);
            }
        }

        /// <summary>Where a rocket blew up (for the self-test).</summary>
        internal static event Action<Vector3> Exploded;

        internal static int RocketsInFlight => Rockets.Count;

        internal static void Update()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (Ripped.Count > 0 && clock.Elapsed.TotalMilliseconds < TearBudgetMs)
            {
                var (limb, velocity) = Ripped.Dequeue();
                RipOff(limb, velocity);
            }
            while (Torn.Count > 0 && clock.Elapsed.TotalMilliseconds < TearBudgetMs)
            {
                var part = Torn.Dequeue();
                Damage.Apply(part.Collider, part.Wounds, part.Direction);
            }
            for (int i = Rockets.Count - 1; i >= 0; i--)
            {
                if (Rockets[i].Step(Time.deltaTime))
                    Rockets.RemoveAt(i);
            }
        }

        /// <summary>A new map: rockets and frozen things from the old one are gone.</summary>
        internal static void Reset()
        {
            foreach (var rocket in Rockets)
                rocket.Destroy();
            Rockets.Clear();
            Torn.Clear();
            Ripped.Clear();
            FrozenBodies.Clear();
        }
    }
}

