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
using Il2CppLVA.Limbs;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BunchAStuff
{
    /// <summary>
    /// Things to hit people with, under Melee in the terminal. Left click swings. Blunt ones bruise, break bones
    /// and knock people out; the machete cuts and can take a limb off; the knife goes in deep.
    /// </summary>
    internal static class Melee
    {
        private enum Hit
        {
            Blunt,
            Cut,
            Stab,
        }

        private sealed class Weapon
        {
            internal string Name;
            internal ModGun Gun;
            internal Hit Kind;
            internal float Reach, Bruise, Push, KnockOut, Breaks;
            internal int Radius;
            internal float Strength;
            // How the swing moves the model: a chop (turning down) or a thrust (straight out).
            internal bool Thrust;
            internal float Speed = 1f;
        }

        private static readonly List<Weapon> Weapons = new();

        internal static IEnumerable<ModGun> All => Weapons.Select(w => w.Gun);
        private static Weapon _swinging;
        private static float _swingStarted;
        private static bool _struck;
        private static Transform _model;
        private static Vector3 _restPosition;
        private static Quaternion _restRotation;

        private const float WindUp = 0.09f, Strike = 0.12f, Recover = 0.24f;

        internal static void Create()
        {
            Inventory.AddCategory("Melee");
            Add(new Weapon { Name = "Baseball Bat", Kind = Hit.Blunt, Reach = 2.3f, Bruise = 0.85f, Push = 60f, KnockOut = 0.25f, Breaks = 0.15f, Radius = 2, Strength = 15f },
                0.6f, MeleeModels.Bat(), new Vector3(0.08f, -0.08f, 0.15f), new Vector3(-40f, 0f, 0f),
                "A wooden bat. Bruises, breaks bones and knocks people out.", ("hits", "blunt"), ("swing", "0.6 s"));
            Add(new Weapon { Name = "Sledgehammer", Kind = Hit.Blunt, Reach = 2.4f, Bruise = 1f, Push = 160f, KnockOut = 0.45f, Breaks = 0.55f, Radius = 4, Strength = 60f, Speed = 0.75f },
                1.1f, MeleeModels.Sledgehammer(), new Vector3(0.08f, -0.1f, 0.12f), new Vector3(-40f, 0f, 0f),
                "Slow and very heavy. Caves in what it hits, snaps bones and throws people.", ("hits", "very hard"), ("swing", "1.1 s"));
            Add(new Weapon { Name = "Frying Pan", Kind = Hit.Blunt, Reach = 2f, Bruise = 0.6f, Push = 35f, KnockOut = 0.55f, Breaks = 0.05f, Radius = 1, Strength = 8f },
                0.55f, MeleeModels.Pan(), new Vector3(0.08f, -0.08f, 0.15f), new Vector3(-25f, 0f, 0f),
                "Cast iron. Not much damage, but a good one to the head puts their lights out.", ("hits", "blunt"), ("knocks out", "often"));
            Add(new Weapon { Name = "Machete", Kind = Hit.Cut, Reach = 2.1f, Bruise = 0.3f, Push = 25f, Radius = 6, Strength = 160f, Speed = 1.15f },
                0.5f, MeleeModels.Machete(), new Vector3(0.07f, -0.08f, 0.12f), new Vector3(-40f, 0f, 0f),
                "Cuts deep, and takes off a limb that's already been hacked at.", ("hits", "cuts"), ("swing", "0.5 s"));
            Add(new Weapon { Name = "Kitchen Knife", Kind = Hit.Stab, Reach = 1.7f, Bruise = 0f, Push = 8f, Radius = 1, Strength = 1f, Thrust = true, Speed = 1.3f },
                0.35f, MeleeModels.Knife(), new Vector3(0.07f, -0.07f, 0.12f), new Vector3(-5f, 0f, 0f),
                "Stabs. Narrow, deep and bleeds hard; in the chest or belly it bleeds inside too.", ("hits", "stabs"), ("swing", "0.35 s"));
        }

        private static void Add(Weapon weapon, float cooldown, GameObject model, Vector3 hold, Vector3 rotation, string description,
            params (string Key, string Value)[] card)
        {
            var gun = Inventory.AddGun(weapon.Name, "Melee").WithCooldown(cooldown).OnFire(_ => Swing(weapon));
            gun.WithDescription(description).WithModel(model, hold, rotation, 1f);
            foreach (var (key, value) in card)
                gun.WithCard(key, value);
            weapon.Gun = gun;
            Weapons.Add(weapon);
        }

        private static void Swing(Weapon weapon)
        {
            EndSwing();
            _swinging = weapon;
            _swingStarted = Time.time;
            _struck = false;
            _model = HeldModel();
            if (_model != null)
            {
                _restPosition = _model.localPosition;
                _restRotation = _model.localRotation;
            }
            Sounds.Play(WhooshSFXType.WeaponAimWhoosh, LocalPlayer.CameraPosition, weapon.Kind == Hit.Stab ? 0.3f : 0.6f);
        }

        // The weapon's model in the player's hand.
        private static Transform HeldModel()
        {
            var held = Toolbar.HeldObject;
            if (!held.Exists())
                return null;
            foreach (var renderer in held.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer != null && renderer.name.StartsWith("Melee ", StringComparison.Ordinal))
                    return renderer.transform;
            }
            return null;
        }

        internal static void Update()
        {
            if (_swinging == null)
                return;
            float t = (Time.time - _swingStarted) * _swinging.Speed;
            if (!_swinging.Gun.IsHeld || t > WindUp + Strike + Recover)
            {
                EndSwing();
                return;
            }
            Animate(t);
            if (!_struck && t >= WindUp + Strike * 0.6f)
            {
                _struck = true;
                Land(_swinging);
            }
        }

        private static void EndSwing()
        {
            if (_model != null && _model.Exists())
            {
                _model.localPosition = _restPosition;
                _model.localRotation = _restRotation;
            }
            _model = null;
            _swinging = null;
        }

        // Back over the shoulder, then down through the target, then back to rest; a knife goes straight out instead.
        private static void Animate(float t)
        {
            if (_model == null || !_model.Exists())
                return;
            float angle, reach;
            if (t < WindUp)
            {
                float k = Smooth(t / WindUp);
                angle = -45f * k;
                reach = -0.04f * k;
            }
            else if (t < WindUp + Strike)
            {
                float k = Smooth((t - WindUp) / Strike);
                angle = Mathf.Lerp(-45f, 75f, k);
                reach = Mathf.Lerp(-0.04f, 0.2f, k);
            }
            else
            {
                float k = Smooth((t - WindUp - Strike) / Recover);
                angle = Mathf.Lerp(75f, 0f, k);
                reach = Mathf.Lerp(0.2f, 0f, k);
            }
            if (_swinging.Thrust)
            {
                _model.localRotation = _restRotation;
                _model.localPosition = _restPosition + Vector3.forward * Mathf.Max(0f, reach) * 1.6f;
            }
            else
            {
                _model.localRotation = Quaternion.Euler(angle, 0f, 0f) * _restRotation;
                _model.localPosition = _restPosition + Vector3.forward * reach * 0.5f;
            }
        }

        private static float Smooth(float k) => k * k * (3f - 2f * k);

        // What the swing meets: the nearest thing along the aim, a little wide.
        private static void Land(Weapon weapon)
        {
            var ray = LocalPlayer.AimRay;
            // Straight along the aim first; then a little wide of it, so a near miss still connects.
            // (Physics.SphereCastAll is stripped from the game.)
            Collider hitCollider = null;
            Vector3 point = default, normal = -ray.direction;
            if (Physics.Raycast(ray, out var hit, weapon.Reach, Layers.Gameplay, QueryTriggerInteraction.Ignore) && hit.distance > 0.2f)
            {
                hitCollider = hit.collider;
                point = hit.point;
                normal = hit.normal;
            }
            for (float d = 0.4f; hitCollider == null && d <= weapon.Reach; d += 0.25f)
            {
                var centre = ray.origin + ray.direction * d;
                float nearest = float.MaxValue;
                foreach (var collider in Physics.OverlapSphere(centre, 0.22f, Layers.Gameplay, QueryTriggerInteraction.Ignore))
                {
                    if (collider == null)
                        continue;
                    var on = collider.ClosestPoint(centre);
                    float distance = (on - centre).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        hitCollider = collider;
                        point = on;
                    }
                }
            }
            if (hitCollider == null)
                return;
            var direction = ray.direction;
            var limb = Creatures.LimbFromCollider(hitCollider);
            if (limb == null)
            {
                var body = hitCollider.attachedRigidbody;
                body?.AddForceAtPosition(direction * weapon.Push * 0.5f, point, ForceMode.Impulse);
                Sounds.Play(ImpactSFXType.RbHit, point, 0.9f);
                Effects.Burst(point, new Color(0.8f, 0.75f, 0.6f), 6, 2f, 0.02f, 0.3f, false, 9f, -direction, 60f);
                return;
            }
            switch (weapon.Kind)
            {
                case Hit.Blunt:
                    Blunt(weapon, limb, point, normal, direction, hitCollider);
                    break;
                case Hit.Cut:
                    Cut(weapon, limb, point, direction, hitCollider);
                    break;
                default:
                    Injuries.Stab(limb, point + direction * 0.02f, direction, 0.16f, hitCollider);
                    limb.AddForceAtPosition(direction * weapon.Push, point);
                    Sounds.Play(ImpactSFXType.FallDamageLightOrganic, point, 0.8f);
                    break;
            }
        }

        private static void Blunt(Weapon weapon, AbstractLimb limb, Vector3 point, Vector3 normal, Vector3 direction, Collider collider)
        {
            var creature = limb.GetCreature();
            Injuries.Wound(limb, point + direction * 0.02f, weapon.Radius, weapon.Strength, direction, collider);
            Bruises.Add(limb, point, normal, weapon.Bruise);
            limb.AddForceAtPosition((direction + Vector3.up * 0.15f).normalized * weapon.Push, point);
            if (Random.value < weapon.Breaks)
                Injuries.BreakNow(limb);
            if (limb.GetHumanPart() == HumanoidNodeTagValue.Head && creature != null && Random.value < weapon.KnockOut)
                Injuries.KnockOut(creature, Random.Range(5f, 12f));
            bool pan = weapon.Name == "Frying Pan";
            Sounds.Play(pan ? ImpactSFXType.RbHit : ImpactSFXType.FallDamageHardOrganic, point, 1f);
            if (pan)
                Sounds.Play(ImpactSFXType.Bullet12gaHardSurface, point, 0.8f);
        }

        // A chop across the limb: a line of wounds, bleeding, and off it comes if it's already badly cut.
        private static void Cut(Weapon weapon, AbstractLimb limb, Vector3 point, Vector3 direction, Collider collider)
        {
            // The chop comes down, so the cut runs up and down across what it hits.
            var across = Vector3.Cross(direction, Vector3.Cross(Vector3.up, direction).normalized).normalized;
            var wounds = new List<(Vector3, int, float)>();
            // A long, deep gash: a row of wounds down the line of the blade, each reaching well in.
            for (int i = -5; i <= 5; i++)
            {
                wounds.Add((point + across * (i * 0.03f) + direction * 0.03f, weapon.Radius, weapon.Strength));
                wounds.Add((point + across * (i * 0.03f) + direction * 0.09f, weapon.Radius - 2, weapon.Strength));
            }
            Injuries.Wound(limb, wounds, direction, collider);
            limb.AddBleeding(0.8f);
            limb.AddForceAtPosition(direction * weapon.Push, point);
            Effects.Burst(point, new Color(0.55f, 0.02f, 0.02f), 14, 2.5f, 0.025f, 0.6f, false, 9f, -direction, 60f, new Color(0.3f, 0f, 0f));
            Sounds.Play(ImpactSFXType.FallDamageLightOrganic, point, 1f);
            var part = limb.GetHumanPart();
            bool core = part is HumanoidNodeTagValue.Spine or HumanoidNodeTagValue.Pelvis;
            if (!core && limb.GetWholeness() < 75f && Random.value < 0.6f)
                limb.Detach();
        }
    }
}
