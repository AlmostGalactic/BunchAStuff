using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.Utilities;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>Where the guns sit in the hand, relative to where the game holds its own items.</summary>
    internal static class Hand
    {
        internal static readonly Vector3 Position = new(0f, 0f, 0f);
        internal static readonly Vector3 Rotation = new(0f, 0f, 0f);
        internal const float Scale = 0.5f;
    }

    /// <summary>The guns, under Weapons in the terminal, and what each one does when it fires.</summary>
    internal static class Guns
    {
        internal const float Range = 400f;

        internal static readonly List<ModGun> All = new();
        private static readonly List<Rocket> Rockets = new();
        private static readonly HashSet<Rigidbody> FrozenBodies = new();

        internal static ModGun Smg, Shotgun, Railgun, Launcher, AirCannon, FreezeGun;

        private static readonly Color Steel = new(0.22f, 0.23f, 0.25f);
        private static readonly Color Dark = new(0.1f, 0.1f, 0.11f);

        internal static void Create()
        {
            Smg = Add("Moth-9", 0.075f, true, new Vector3(0f, 0.05f, 0.34f), FireSmg,
                new Blocks("Moth-9")
                    .Box(new Vector3(0f, 0.045f, 0.08f), new Vector3(0.055f, 0.075f, 0.26f), Steel)
                    .Tube(new Vector3(0f, 0.05f, 0.27f), 0.025f, 0.12f, Dark)
                    .Box(new Vector3(0f, -0.035f, 0f), new Vector3(0.04f, 0.1f, 0.045f), Dark, new Vector3(15f, 0f, 0f))
                    .Box(new Vector3(0f, -0.045f, 0.1f), new Vector3(0.03f, 0.12f, 0.04f), new Color(0.95f, 0.75f, 0.1f))
                    .Box(new Vector3(0f, 0.09f, 0.05f), new Vector3(0.02f, 0.02f, 0.08f), new Color(0.95f, 0.75f, 0.1f))
                    .Build(),
                "Submachine gun. Fires for as long as the trigger is held.", ("caliber", "9mm"), ("fire", "automatic"));

            Shotgun = Add("Barrow-12", 0.85f, false, new Vector3(0f, 0.06f, 0.5f), FireShotgun,
                new Blocks("Barrow-12")
                    .Box(new Vector3(0f, 0.04f, 0.05f), new Vector3(0.065f, 0.08f, 0.22f), new Color(0.45f, 0.27f, 0.14f))
                    .Tube(new Vector3(0f, 0.065f, 0.3f), 0.04f, 0.4f, Steel)
                    .Tube(new Vector3(0f, 0.025f, 0.27f), 0.035f, 0.3f, Dark)
                    .Box(new Vector3(0f, 0.025f, 0.28f), new Vector3(0.05f, 0.045f, 0.1f), new Color(0.45f, 0.27f, 0.14f))
                    .Box(new Vector3(0f, 0.01f, -0.14f), new Vector3(0.05f, 0.09f, 0.2f), new Color(0.45f, 0.27f, 0.14f), new Vector3(-8f, 0f, 0f))
                    .Build(),
                "Pump shotgun. Nine pellets per shot.", ("caliber", "12ga"), ("fire", "pump"));

            Railgun = Add("Heron-R", 1.2f, false, new Vector3(0f, 0.05f, 0.62f), FireRailgun,
                new Blocks("Heron-R")
                    .Box(new Vector3(0f, 0.04f, 0.15f), new Vector3(0.06f, 0.09f, 0.4f), new Color(0.85f, 0.88f, 0.9f))
                    .Box(new Vector3(0.022f, 0.05f, 0.45f), new Vector3(0.012f, 0.03f, 0.35f), new Color(0.2f, 0.85f, 1f))
                    .Box(new Vector3(-0.022f, 0.05f, 0.45f), new Vector3(0.012f, 0.03f, 0.35f), new Color(0.2f, 0.85f, 1f))
                    .Box(new Vector3(0f, 0.12f, 0.15f), new Vector3(0.03f, 0.04f, 0.14f), Dark)
                    .Box(new Vector3(0f, -0.04f, 0f), new Vector3(0.04f, 0.1f, 0.045f), Dark, new Vector3(15f, 0f, 0f))
                    .Box(new Vector3(0f, 0.02f, -0.16f), new Vector3(0.05f, 0.08f, 0.18f), new Color(0.85f, 0.88f, 0.9f))
                    .Build(),
                "Rail rifle. The slug goes through bodies and loose objects, up to eight in a line.", ("fire", "single"), ("pierces", "8"));

            Launcher = Add("Tusk-40", 1.3f, false, new Vector3(0f, 0.07f, 0.45f), FireLauncher,
                new Blocks("Tusk-40")
                    .Tube(new Vector3(0f, 0.07f, 0.1f), 0.11f, 0.7f, new Color(0.3f, 0.4f, 0.22f))
                    .Tube(new Vector3(0f, 0.07f, 0.44f), 0.13f, 0.05f, Dark)
                    .Box(new Vector3(0f, -0.03f, 0f), new Vector3(0.04f, 0.1f, 0.045f), Dark, new Vector3(15f, 0f, 0f))
                    .Box(new Vector3(0.07f, 0.11f, 0.05f), new Vector3(0.03f, 0.05f, 0.08f), Dark)
                    .Build(),
                "Rocket launcher. The rocket explodes on impact.", ("fire", "single"), ("blast radius", "3.5 m"));

            AirCannon = Add("Gale-2", 0.6f, false, new Vector3(0f, 0.06f, 0.36f), FireAirCannon,
                new Blocks("Gale-2")
                    .Box(new Vector3(0f, 0.05f, 0.08f), new Vector3(0.1f, 0.1f, 0.24f), new Color(0.6f, 0.25f, 0.85f))
                    .Tube(new Vector3(0f, 0.06f, 0.26f), 0.09f, 0.14f, Dark)
                    .Ball(new Vector3(0f, 0.06f, 0.33f), 0.07f, new Color(1f, 0.5f, 0.95f))
                    .Box(new Vector3(0f, -0.04f, 0f), new Vector3(0.04f, 0.1f, 0.045f), Dark, new Vector3(15f, 0f, 0f))
                    .Build(),
                "Air cannon. Throws whatever it hits without damaging it.", ("fire", "single"));

            FreezeGun = Add("Halt-1", 0.3f, false, new Vector3(0f, 0.06f, 0.33f), FireFreezeGun,
                new Blocks("Halt-1")
                    .Box(new Vector3(0f, 0.05f, 0.06f), new Vector3(0.07f, 0.08f, 0.2f), new Color(0.85f, 0.95f, 1f))
                    .Tube(new Vector3(0f, 0.055f, 0.22f), 0.03f, 0.14f, new Color(0.35f, 0.75f, 1f))
                    .Ball(new Vector3(0f, 0.055f, 0.3f), 0.05f, new Color(0.6f, 0.9f, 1f))
                    .Box(new Vector3(0f, -0.04f, 0f), new Vector3(0.04f, 0.1f, 0.045f), Dark, new Vector3(15f, 0f, 0f))
                    .Build(),
                "Holds whatever it hits still in mid-air. Hit it again to let go.", ("fire", "single"));
        }

        private static ModGun Add(string name, float cooldown, bool automatic, Vector3 muzzle, Action<ModGun> fire, GameObject model,
            string description, params (string Key, string Value)[] card)
        {
            var gun = Inventory.AddGun(name)
                .WithCooldown(cooldown, automatic)
                .WithMuzzle(muzzle)
                .OnFire(fire);
            gun.WithDescription(description)
                .WithModel(model, Hand.Position, Hand.Rotation, Hand.Scale);
            foreach (var (key, value) in card)
                gun.WithCard(key, value);
            All.Add(gun);
            return gun;
        }

        // ------------------------------------------------------------ the guns

        private static void FireSmg(ModGun gun)
        {
            var shot = Bullets.Fire(Bullets.Spread(LocalPlayer.AimRay, 1.5f), radiusVoxels: 2, strength: Settings.GunDamage, push: 4f);
            Bullets.Tracer(gun.Muzzle, shot.End, new Color(1f, 0.85f, 0.35f), 0.01f, 0.05f);
            Sounds.Play(WeaponSFXType.Shoot9MM, gun.Muzzle, 0.55f);
        }

        private static void FireShotgun(ModGun gun)
        {
            for (int i = 0; i < 9; i++)
            {
                var shot = Bullets.Fire(Bullets.Spread(LocalPlayer.AimRay, 5f), range: 60f, radiusVoxels: 2, strength: Settings.GunDamage, push: 7f, sound: i == 0);
                Bullets.Tracer(gun.Muzzle, shot.End, new Color(1f, 0.7f, 0.3f), 0.008f, 0.06f);
            }
            Sounds.Play(WeaponSFXType.Shoot12ga, gun.Muzzle);
            Scheduler.After(0.3f, () => Sounds.Play(WeaponSFXType.RackBack12ga, LocalPlayer.CameraPosition, 0.7f), realtime: false);
            Scheduler.After(0.45f, () => Sounds.Play(WeaponSFXType.RackForth12ga, LocalPlayer.CameraPosition, 0.7f), realtime: false);
        }

        private static void FireRailgun(ModGun gun)
        {
            var ray = LocalPlayer.AimRay;
            var hits = Bullets.Pierce(ray, maxHits: 8, radiusVoxels: 4, strength: Settings.GunDamage, push: 40f);
            var end = Bullets.EndOf(hits, ray);
            Bullets.Tracer(gun.Muzzle, end, new Color(0.3f, 0.9f, 1f), 0.05f, 0.35f);
            Bullets.Tracer(gun.Muzzle, end, Color.white, 0.015f, 0.2f);
            Sounds.Play(WeaponSFXType.Shoot762, gun.Muzzle);
        }

        private static void FireLauncher(ModGun gun)
        {
            // From the barrel to whatever is under the crosshair, so it lands where the player aimed.
            Rockets.Add(new Rocket(gun.Muzzle, (Bullets.AimPoint() - gun.Muzzle).normalized));
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

        private sealed class Rocket
        {
            private const float Speed = 32f;
            private const float Life = 5f;
            private readonly GameObject _body;
            private Vector3 _velocity;
            private readonly float _born;

            internal Rocket(Vector3 position, Vector3 direction)
            {
                _body = new Blocks("Tusk-40 rocket")
                    .Tube(Vector3.zero, 0.07f, 0.3f, new Color(0.3f, 0.4f, 0.22f))
                    .Ball(new Vector3(0f, 0f, 0.15f), 0.07f, new Color(0.85f, 0.2f, 0.15f))
                    .Ball(new Vector3(0f, 0f, -0.17f), 0.06f, new Color(1f, 0.7f, 0.2f))
                    .Build();
                _body.transform.position = position;
                _body.transform.rotation = Quaternion.LookRotation(direction);
                _body.SetActive(true);
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
                Damage.Explosion(at, 3.5f, force: 45f, maxRadiusVoxels: 6, strength: Settings.GunDamage);
                Effects.Flash(at, 3f, new Color(1f, 0.55f, 0.1f), 0.35f);
                Effects.Flash(at, 1.8f, new Color(1f, 0.95f, 0.6f), 0.18f);
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
            FrozenBodies.Clear();
        }
    }
}
