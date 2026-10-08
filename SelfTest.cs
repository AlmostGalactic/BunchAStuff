using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.Spawning;
using FruktSharedLibrary.UI;
using FruktSharedLibrary.Utilities;
using Il2CppData.Maps;
using Il2CppLVA.Limbs;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using Il2CppLVA.Creatures;
using Il2CppSpawnables.Weapons;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BunchAStuff
{
    /// <summary>
    /// The mod's in-game test. Create <c>UserData/BunchAStuff.selftest</c> (write "quit" in it to close the game
    /// afterwards). It loads the Yard, fires every gun at people and things, starts fist fights, tries the teams,
    /// and writes a report to <c>UserData/BunchAStuff.selftest.log</c>. Run the library's
    /// tools/selftest-watch.ps1 alongside for the real clicks and the screenshots.
    /// </summary>
    internal static class SelfTest
    {
        private static readonly List<string> Report = new();
        private static readonly List<GameObject> Spawned = new();
        private static int _passed, _failed;
        private static bool _quit, _started;
        private static string FlagPath => Path.Combine(MelonEnvironment.UserDataDirectory, "BunchAStuff.selftest");
        private static string LogPath => Path.Combine(MelonEnvironment.UserDataDirectory, "BunchAStuff.selftest.log");

        internal static void Initialize()
        {
            if (!File.Exists(FlagPath))
                return;
            _quit = File.ReadAllText(FlagPath).IndexOf("quit", StringComparison.OrdinalIgnoreCase) >= 0;
            Log("Enabled; the Yard loads from the main menu.");
            GameEvents.MainMenuEntered += () =>
            {
                if (!_started)
                    Scheduler.After(3f, () => World.LoadMap(MapID.Yard));
            };
            GameEvents.SandboxReady += _ =>
            {
                if (_started)
                    return;
                _started = true;
                Scheduler.StartCoroutine(Guarded(Run()));
            };
        }

        private static IEnumerator Guarded(IEnumerator test)
        {
            while (true)
            {
                bool more;
                try
                {
                    more = test.MoveNext();
                }
                catch (Exception e)
                {
                    Check("The test ran without exceptions", false, e.ToString());
                    break;
                }
                if (!more)
                    break;
                yield return test.Current;
            }
            Finish();
        }

        private static IEnumerator Run()
        {
            yield return Wait(2f);
            for (float end = Now() + 30f; !Application.isFocused && Now() < end;)
                yield return null;
            Check("The game has focus for real input", Application.isFocused);

            Check("All six guns are under Weapons", Guns.All.Count == 6 && Guns.All.All(g => g.Registered && g.Item.CategoryName == "Weapons"),
                string.Join(", ", Guns.All.Select(g => $"{g.Name} [{g.Item?.CategoryName}]")));
            Check("The guns have icons", Guns.All.All(g => g.Item?.Icon != null));
            foreach (var (name, make, muzzle) in new (string, Func<GameObject>, Vector3)[]
            {
                ("Moth-9", GunModels.Smg, GunModels.SmgMuzzle), ("Barrow-12", GunModels.Shotgun, GunModels.ShotgunMuzzle),
                ("Heron-R", GunModels.Railgun, GunModels.RailgunMuzzle), ("Tusk-40", GunModels.Launcher, GunModels.LauncherMuzzle),
                ("Gale-2", GunModels.AirCannon, GunModels.AirCannonMuzzle), ("Halt-1", GunModels.FreezeGun, GunModels.FreezeGunMuzzle),
            })
            {
                var model = make();
                var mesh = model.GetComponent<MeshFilter>().sharedMesh;
                var size = mesh.bounds.size;
                bool voxels = mesh.vertexCount >= 1500 && size.z > 0.4f && size.z < 1.5f && size.y > 0.1f && size.y < 0.6f;
                float tip = mesh.bounds.max.z;
                Check($"The {name} is a detailed voxel model the size of the game's guns", voxels && mesh.vertexCount < 60000,
                    $"{mesh.vertexCount} vertices, {size.z * 100:0} x {size.y * 100:0} x {size.x * 100:0} cm");
                Check($"The {name}'s muzzle is at the end of its barrel", Mathf.Abs(muzzle.z - tip) < 0.04f && muzzle.z > size.z * 0.5f + mesh.bounds.min.z,
                    $"muzzle {muzzle.z * 100:0} cm, tip {tip * 100:0} cm");
                Object.Destroy(model);
            }
            Creatures.DeleteAll();
            yield return Wait(1f);

            foreach (var step in GunTests())
                yield return step;
            foreach (var step in FightTests())
                yield return step;
            foreach (var step in TeamTests())
                yield return step;
            foreach (var step in SyringeTests())
                yield return step;
            foreach (var step in BruiseTests())
                yield return step;
        }

        private static IEnumerable BruiseTests()
        {
            // A punch on the chest: red, then purple, then green and yellow, then gone.
            AbstractCreature person = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(3f), c => person = c))
                yield return step;
            Check("Someone to bruise", person != null);
            if (person == null)
                yield break;
            Hang(person, 1.5f);
            yield return Wait(0.5f);
            var chest = person.GetLimb(HumanoidNodeTagValue.Spine);
            var from = LocalPlayer.CameraPosition;
            bool aimed = Physics.Raycast(from, chest.GetPosition() + Vector3.up * 0.08f - from, out var hit, 5f, Layers.Puppet, QueryTriggerInteraction.Ignore)
                && Creatures.LimbFromCollider(hit.collider)?.Pointer == chest.Pointer;
            Check("The chest is in front of the camera", aimed);
            // A short-lived bruise, watched as it heals: the colour has to change bit by bit, never in jumps.
            int before = Bruises.Count;
            const float life = 16f;
            Bruises.Add(chest, hit.point, hit.normal, 0.9f, life);
            Check("A knock leaves a bruise", Bruises.Count == before + 1 && Bruises.On(chest) == 1, $"{Bruises.On(chest)} on the chest");
            Bruises.Add(chest, hit.point + Vector3.right * 0.01f, hit.normal, 0.5f);
            Check("A knock on a bruise makes it worse, not another one", Bruises.On(chest) == 1);
            float started = Now();
            foreach (float at in new[] { 0.6f, 2f, 4f, 6f, 8f, 10f, 12f, 14f })
            {
                while (Now() - started < at)
                    yield return null;
                Shot($"bas-bruise-{at:00.0}s");
            }
            for (float end = started + life + 2f; Now() < end;)
                yield return null;
            Check("It heals away", Bruises.On(chest) == 0);
            Shot("bas-bruise-gone");
            yield return Wait(1f);

            // A Health Syringe takes them away.
            Bruises.Add(chest, hit.point, hit.normal, 0.9f);
            Syringes.Inject(SyringeKinds.Health, chest, 1f);
            yield return null;
            Check("A Health Syringe heals bruises", Bruises.On(chest) == 0);
            yield return Wait(1.5f);

            // A fall: dropped from high up, they bruise where they land.
            person.SetFrozen(false);
            person.TeleportTo(LocalPlayer.GetPointInFront(4f) + Vector3.up * 7f);
            Physics.SyncTransforms();
            int beforeFall = Bruises.Count;
            yield return Wait(3.5f);
            Shot("bas-bruise-fall");
            Check("A hard fall bruises them", Bruises.Count > beforeFall, $"{Bruises.Count - beforeFall} bruises");
            person.Delete();
        }

        // ------------------------------------------------------------ guns

        private static IEnumerable GunTests()
        {
            int slot = Enumerable.Range(0, Toolbar.SlotCount).Where(s => Toolbar.CanChange(s) && Toolbar.IsEmpty(s)).DefaultIfEmpty(Toolbar.SlotCount - 1).First();

            // A dummy hanging in the crosshair, frozen so it stays there.
            AbstractCreature dummy = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(5f), c => dummy = c))
                yield return step;
            Check("A dummy spawns", dummy != null);
            if (dummy == null)
                yield break;
            Hang(dummy, 5f);

            foreach (var gun in Guns.All)
            {
                Check($"{gun.Name} goes on the toolbar", Toolbar.Put(gun.Item, slot) && Toolbar.Select(slot));
                yield return Wait(1f);
                Check($"The player holds the {gun.Name}", gun.IsHeld && gun.HeldObject != null);
                yield return ShotAndWait("bas-held-" + gun.Name.Replace(' ', '-').ToLowerInvariant());
            }

            // Moth-9, with a real click and then held down.
            Toolbar.Put(Guns.Smg.Item, slot);
            Toolbar.Select(slot);
            yield return Wait(0.8f);
            Hang(dummy, 5f);
            float before = Hurt(dummy);
            int shots = Guns.Smg.ShotsFired;
            Click("bas-smg-click");
            for (float end = Now() + 3f; Guns.Smg.ShotsFired == shots && Now() < end;)
                yield return null;
            Check("A real click fires the Moth-9", Guns.Smg.ShotsFired > shots);
            shots = Guns.Smg.ShotsFired;
            FruktLog.Msg("[SelfTest] MOUSEDOWN bas-smg-hold");
            yield return Wait(1f);
            FruktLog.Msg("[SelfTest] MOUSEUP bas-smg-hold");
            yield return Wait(0.3f);
            Check("Holding the trigger keeps the Moth-9 firing", Guns.Smg.ShotsFired - shots >= 6, $"{Guns.Smg.ShotsFired - shots} shots in a second");
            yield return Wait(0.5f);
            Check("The Moth-9 hurts the dummy", Hurt(dummy) > before, $"{before:0.00} -> {Hurt(dummy):0.00}");
            Shot("bas-smg-hits");

            foreach (var step in Fire(Guns.Shotgun, dummy, slot))
                yield return step;

            // The Heron-R goes through two people in a row.
            // A fresh person for the rail: the first one's middle has been shot away by now.
            dummy.Delete();
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(5f), c => dummy = c))
                yield return step;
            AbstractCreature second = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(8f), c => second = c))
                yield return step;
            Hang(dummy, 5f);
            Hang(second, 7.5f);
            Toolbar.Put(Guns.Railgun.Item, slot);
            Toolbar.Select(slot);
            yield return Wait(0.8f);
            Hang(dummy, 5f);
            Hang(second, 7.5f);
            float firstBefore = Hurt(dummy), secondBefore = Hurt(second);
            Check("The Heron-R fires", Guns.Railgun.TryFire());
            yield return Wait(0.5f);
            Check("The rail goes through both people", Hurt(dummy) > firstBefore && Hurt(second) > secondBefore,
                $"first {firstBefore:0.00} -> {Hurt(dummy):0.00}, second {secondBefore:0.00} -> {Hurt(second):0.00}");
            Shot("bas-rail");
            second.Delete();

            // The Tusk-40's rocket flies and blows up on the dummy.
            Toolbar.Put(Guns.Launcher.Item, slot);
            Toolbar.Select(slot);
            yield return Wait(0.8f);
            Hang(dummy, 6f);
            before = Hurt(dummy);
            Vector3? blast = null;
            void OnBlast(Vector3 at) => blast = at;
            Guns.Exploded += OnBlast;
            Check("The Tusk-40 fires a rocket", Guns.Launcher.TryFire() && Guns.RocketsInFlight == 1);
            yield return Wait(0.08f);
            Shot("bas-rocket");
            for (float end = Now() + 3f; blast == null && Now() < end;)
                yield return null;
            Guns.Exploded -= OnBlast;
            Check("The blast throws fire, smoke and debris", FruktSharedLibrary.Combat.Effects.Count > 80, $"{FruktSharedLibrary.Combat.Effects.Count} cubes");
            Check("The rocket blows up on the dummy", blast.HasValue && Vector3.Distance(blast.Value, dummy.GetPosition()) < 1.5f,
                blast.HasValue ? $"{Vector3.Distance(blast.Value, dummy.GetPosition()):0.00} m away" : "it never went off");
            Shot("bas-blast");
            yield return Wait(0.5f);
            Check("The blast hurts the dummy", Hurt(dummy) > before, $"{before:0.00} -> {Hurt(dummy):0.00}");
            dummy.Delete();

            // A blast next to someone standing free wounds them and throws them.
            AbstractCreature standing = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(6f), c => standing = c))
                yield return step;
            Check("Someone stands for the blast", standing != null);
            if (standing != null)
            {
                yield return Wait(1f);
                var from = standing.GetPosition();
                float hurtBefore = Hurt(standing);
                Guns.Detonate(from + new Vector3(1.5f, 0f, 0f));
                yield return Wait(0.5f);
                Shot("bas-blast-person");
                yield return Wait(0.8f);
                float moved = Vector3.Distance(from, standing.GetPosition());
                Check("A blast beside someone throws them", moved > 1f, $"{moved:0.0} m");
                Check("A blast beside someone shreds them", Hurt(standing) > hurtBefore + 400f, $"{hurtBefore:0.0} -> {Hurt(standing):0.0}");
                standing.Delete();
            }

            // A blast right on someone blows them apart.
            AbstractCreature target = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(6f), c => target = c))
                yield return step;
            Check("Someone stands on the blast", target != null);
            if (target != null)
            {
                yield return Wait(1f);
                var already = new HashSet<IntPtr>(Creatures.All.Select(c => c.Pointer));
                var centre = target.GetPosition();
                Guns.Detonate(centre + Vector3.up * 0.3f);
                yield return Wait(0.6f);
                Shot("bas-blast-apart");
                yield return Wait(0.6f);
                var pieces = Creatures.All.Where(c => c.IsValid() && !already.Contains(c.Pointer)).ToList();
                Check("A blast on someone blows them apart", pieces.Count >= 6, $"{pieces.Count} pieces");
                float flown = pieces.Select(c => Vector3.Distance(centre, c.GetPosition())).DefaultIfEmpty(0f).Max();
                Check("The pieces fly", flown > 3f, $"furthest {flown:0.0} m");
                foreach (var piece in pieces)
                    piece.Delete();
                target.Delete();
            }

            // The Gale-2 throws a crate; the Halt-1 stops one and lets it go.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var crate = Spawner.SpawnMesh(cube.GetComponent<MeshFilter>().sharedMesh, LocalPlayer.CameraPosition + LocalPlayer.Forward * 4f, mass: 20f);
            Object.Destroy(cube);
            Spawned.Add(crate);
            var body = crate.GetComponent<Rigidbody>();
            Toolbar.Put(Guns.AirCannon.Item, slot);
            Toolbar.Select(slot);
            yield return Wait(0.8f);
            Place(body, 4f);
            Check("The Gale-2 fires", Guns.AirCannon.TryFire());
            yield return new WaitForFixedUpdate();
            Check("The Gale-2 throws the crate", body.velocity.magnitude > 15f, $"{body.velocity.magnitude:0.0} m/s");
            yield return Wait(1f);

            Toolbar.Put(Guns.FreezeGun.Item, slot);
            Toolbar.Select(slot);
            yield return Wait(0.8f);
            body.isKinematic = false;
            Place(body, 4f);
            Guns.FreezeGun.TryFire();
            Check("The Halt-1 freezes the crate", body.isKinematic && Guns.IsFrozen(body));
            yield return Wait(0.5f);
            Place(body, 4f);
            Guns.FreezeGun.TryFire();
            Check("A second shot thaws it", !body.isKinematic && !Guns.IsFrozen(body));
            Object.Destroy(crate);
            Toolbar.Clear(slot);
            Toolbar.Select(Toolbar.CursorSlot);
        }

        private static IEnumerable Fire(ModGun gun, AbstractCreature dummy, int slot)
        {
            Toolbar.Put(gun.Item, slot);
            Toolbar.Select(slot);
            yield return Wait(0.8f);
            Hang(dummy, 5f);
            float before = Hurt(dummy);
            Check($"The {gun.Name} fires", gun.TryFire());
            yield return Wait(0.5f);
            Check($"The {gun.Name} hurts the dummy", Hurt(dummy) > before, $"{before:0.00} -> {Hurt(dummy):0.00}");
            Shot("bas-" + gun.Name.Replace(' ', '-').ToLowerInvariant());
        }

        // ------------------------------------------------------------ fights

        private static IEnumerable FightTests()
        {
            Creatures.DeleteAll();
            yield return Wait(1f);
            var right = LocalPlayer.CameraRotation * Vector3.right;
            AbstractCreature a = null, b = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f) - right * 1.3f, c => a = c))
                yield return step;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f) + right * 1.3f, c => b = c))
                yield return step;
            Check("Two people spawn for a fight", a != null && b != null);
            if (a == null || b == null)
                yield break;
            Log($"They're called {a.GetDisplayName()} and {b.GetDisplayName()}");
            yield return Wait(2f);

            // The right-click menu has the new lines (seen in the screenshot).
            a.SetFrozen(true);
            a.TeleportTo(LocalPlayer.CameraPosition + LocalPlayer.Forward * 3f);
            yield return Wait(0.5f);
            FruktLog.Msg("[SelfTest] RCLICK bas-context-menu");
            yield return Wait(1f);
            Shot("bas-context-menu");
            yield return Wait(0.5f);
            Check("The right-click menu opened", ContextMenus.IsOpen);
            ContextMenus.Close();
            yield return Wait(0.5f);
            a.SetFrozen(false);
            a.TeleportTo(LocalPlayer.GetPointInFront(4f) - right * 1.3f + Vector3.up * 0.9f);
            yield return Wait(3f);

            float startDistance = Vector3.Distance(a.GetPosition(), b.GetPosition());
            Check("Fight nearest starts a fight", Fights.Start(a, out string why), why);
            Check("They fight the other person", Fights.TargetOf(a)?.Pointer == b.Pointer);
            Check("The other person fights back", Fights.IsFighting(b) && Fights.TargetOf(b)?.Pointer == a.Pointer);
            float closest = startDistance;
            float hurtBefore = Hurt(a) + Hurt(b);
            float guard = float.PositiveInfinity;
            float restingDrop = Fights.FistDrop(a);
            for (float end = Now() + 15f; Now() < end && Fights.PunchesLanded + Fights.PunchesBlocked < 5;)
            {
                closest = Mathf.Min(closest, Vector3.Distance(a.GetPosition(), b.GetPosition()));
                if (closest < Fights.Reach + 0.3f)
                    guard = Mathf.Min(guard, Fights.FistDrop(a));
                yield return null;
            }
            for (int i = 1; i <= 4; i++)
            {
                Shot("bas-fight-" + i);
                yield return Wait(0.6f);
            }
            yield return Wait(1.5f);
            Check("They walk up to each other", closest < Fights.Reach + 0.4f, $"{startDistance:0.0} m -> {closest:0.0} m");
            Check("They put their fists up", guard < 0.35f, $"fists {restingDrop:0.00} m under the head at rest, {guard:0.00} m in the fight");
            Check("Punches connect", Fights.PunchesLanded >= 1 && Fights.PunchesLanded + Fights.PunchesBlocked >= 3,
                $"{Fights.PunchesLanded} landed, {Fights.PunchesBlocked} blocked");
            Check("Punches leave bruises", Bruises.Count > 0, $"{Bruises.Count} bruises");
            Check("Punches hurt", Hurt(a) + Hurt(b) > hurtBefore, $"{hurtBefore:0.00} -> {Hurt(a) + Hurt(b):0.00}");
            yield return Wait(4f);
            yield return ShotAndWait("bas-fight-later");
            Fights.Stop(a);
            Check("Stop fighting stops them", !Fights.IsFighting(a));
            Fights.StopAll();
            Check("Stop all fights stops everyone", Fights.Count == 0);
        }

        // ------------------------------------------------------------ teams

        private static IEnumerable TeamTests()
        {
            Creatures.DeleteAll();
            yield return Wait(1f);
            var right = LocalPlayer.CameraRotation * Vector3.right;
            AbstractCreature a = null, b = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(5f) - right * 1.5f, c => a = c))
                yield return step;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(5f) + right * 1.5f, c => b = c))
                yield return step;
            if (a == null || b == null)
            {
                Check("Two people spawn for the team test", false);
                yield break;
            }
            yield return Wait(2f);

            var red = Teams.Find("Red");
            var blue = Teams.Find("Blue");
            Teams.Join(a, red);
            Teams.Join(b, red);
            Check("Both are on Red", Teams.Of(a) == red && Teams.Of(b) == red && Teams.AreAllies(a, b));
            Check("Teammates won't fight each other", !Fights.Start(a, out string why), why);
            yield return Wait(0.3f);
            var tag = Teams.TagOf(a);
            Check("Team members get a name tag in the game's font", tag != null && tag.Exists && tag.Text == "Red" && tag.OnScreen);
            yield return ShotAndWait("bas-team-tags");

            Teams.Join(b, blue);
            Check("Changing team changes the tag", Teams.TagOf(b)?.Text == "Blue");
            Check("Red will fight Blue", Fights.Start(a, out why) && Fights.TargetOf(a)?.Pointer == b.Pointer, why);
            yield return Wait(1f);
            Teams.Join(b, red);
            yield return Wait(0.5f);
            Check("A fight stops when they end up on the same team", !Fights.IsFighting(a) && !Fights.IsFighting(b));

            // Teams the player makes.
            var made = Teams.Add("  Test Squad ", Teams.Colours[5].Color, out why);
            Check("A new team can be made", made != null && made.Name == "Test Squad" && Teams.All.Contains(made), why);
            Check("Team names can't be used twice", Teams.Add("test squad", Color.white, out why) == null && why != null, why);
            Check("Teams need a name", Teams.Add("   ", Color.white, out why) == null, why);
            Check("Made teams are saved", Settings.LoadTeams().Any(t => t.Name == "Test Squad"));
            Teams.Join(a, made);
            Check("People can join a made team", Teams.Of(a) == made && Teams.MembersOf(made).Count == 1);
            Check("A made team can be deleted", Teams.Remove(made) && Teams.Find("Test Squad") == null && Teams.Of(a) == null);
            Check("Deleting it takes it out of the save", !Settings.LoadTeams().Any(t => t.Name == "Test Squad"));
            Check("Any team can be deleted, even the four it starts with", Teams.Remove(red) && Teams.Find("Red") == null && !Settings.LoadTeams().Any(t => t.Name == "Red"));
            Teams.Add("Red", Teams.Colours[0].Color, out _);

            Check("Split everyone puts two people on different teams", Teams.SplitEveryone() == 2 && Teams.Of(a) != Teams.Of(b) && Teams.Of(a) != null);
            var sizes = Teams.All.Select(t => Teams.MembersOf(t).Count).ToList();
            Check("The split is as even as it can be", sizes.Max() - sizes.Min() <= 1, string.Join(", ", sizes));
            Check("Everyone fights starts both", Fights.EveryoneFights() == 2 && Fights.Count == 2);
            yield return Wait(4f);
            yield return ShotAndWait("bas-team-fight");
            Fights.StopAll();
            Teams.ClearMembers();
            Check("Leaving a team takes the tag away", tag != null && !tag.Exists && Teams.TagOf(a) == null);
        }


        // ------------------------------------------------------------ syringes

        private static IEnumerable SyringeTests()
        {
            Creatures.DeleteAll();
            yield return Wait(1f);
            var health = SyringeKinds.Health;
            Check("The Health Syringe is under Props", health.Prop.Registered && health.Prop.Item?.CategoryName == "Props", health.Prop.Item?.CategoryName);

            // A close look at one.
            var look = health.Prop.Place(LocalPlayer.CameraPosition + LocalPlayer.Forward * 0.45f,
                Quaternion.LookRotation(LocalPlayer.CameraRotation * Vector3.right) * Quaternion.Euler(0f, 0f, 0f));
            Check("A syringe can be put in the world", look.Exists());
            if (look.Exists())
            {
                look.GetComponent<Rigidbody>().isKinematic = true;
                Spawned.Add(look);
                yield return ShotAndWait("bas-syringe");
                Object.Destroy(look);
            }

            AbstractCreature patient = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => patient = c))
                yield return step;
            Check("Someone spawns for the syringe", patient != null);
            if (patient == null)
                yield break;
            // Cut first, and let it start bleeding, before they're held still.
            foreach (var part in new[] { HumanoidNodeTagValue.LeftLeg, HumanoidNodeTagValue.RightLeg })
            {
                var limb = patient.GetLimb(part);
                if (limb != null)
                    FruktSharedLibrary.Combat.Bullets.Launch(LocalPlayer.CameraPosition, (limb.GetPosition() - LocalPlayer.CameraPosition).normalized);
            }
            patient.DrainBlood(patient.GetBloodCapacity() * 0.5f);
            yield return Wait(1.5f);
            Hang(patient, 2.6f);
            yield return Wait(0.5f);
            float bloodBefore = patient.GetBlood();
            float hurtBefore = Hurt(patient);
            int limbsBefore = patient.GetLimbCount();
            int bleedingBefore = patient.GetLimbs().Sum(l => l.GetBleedingWoundCount());

            // Thrown at their middle the way the throw key does it, from the camera at the crosshair.
            Check("The throw key is a setting", Settings.ThrowKey != null, Settings.ThrowKey?.ToString() ?? "none");
            var from = LocalPlayer.CameraPosition + LocalPlayer.Forward * 0.9f;
            var syringe = health.Prop.Place(from, Quaternion.LookRotation(-LocalPlayer.Forward));
            Spawned.Add(syringe);
            AbstractLimb hit = null;
            void OnStab(Syringes.Kind kind, AbstractLimb limb) => hit = limb;
            Syringes.Stabbed += OnStab;
            yield return null;
            Check("A syringe can be thrown", Syringes.Throw(syringe, LocalPlayer.CameraPosition, LocalPlayer.Forward));
            for (float end = Now() + 2f; hit == null && Now() < end;)
                yield return null;
            Syringes.Stabbed -= OnStab;
            Check("A thrown syringe sticks in", hit != null && hit.GetCreature() == patient && Syringes.IsStuck(syringe), hit?.GetHumanPart().ToString() ?? "it missed");
            if (hit == null && syringe.Exists())
                Check("Stabbing it in by hand works", Syringes.StabNow(syringe, patient.GetLimb(HumanoidNodeTagValue.Spine)));
            yield return Wait(0.25f);
            Shot("bas-syringe-stuck");
            yield return Wait(1.2f);
            Check("The plunger goes down and it's empty", Syringes.IsEmpty(syringe));
            Check("The dose is working", Syringes.Active.Any(d => d.Creature == patient && d.Kind == health));
            yield return Wait(health.Seconds + 0.5f);
            float bloodAfter = patient.GetBlood();
            int bleedingAfter = patient.GetLimbs().Sum(l => l.GetBleedingWoundCount());
            Check("The Health Syringe refills the blood", bloodAfter > bloodBefore + patient.GetBloodCapacity() * 0.3f && bloodAfter >= patient.GetBloodCapacity() * 0.95f,
                $"{bloodBefore:0} -> {bloodAfter:0} of {patient.GetBloodCapacity():0}");
            Check("The Health Syringe stops the bleeding", bleedingBefore > 0 && bleedingAfter == 0, $"{bleedingBefore} -> {bleedingAfter} wounds");
            for (float end = Now() + 4f; FruktSharedLibrary.Entities.Tissue.Active > 0 && Now() < end;)
                yield return null;
            // Hurt counts from -99 a limb, so this is how much is actually missing.
            float goneBefore = hurtBefore + 99f * limbsBefore, goneAfter = Hurt(patient) + 99f * patient.GetLimbCount();
            Check("The Health Syringe grows the flesh back", goneBefore > 1f && goneAfter < goneBefore * 0.3f, $"{goneBefore:0.0} -> {goneAfter:0.0} missing");
            Check("It doesn't grow new limbs", patient.GetLimbCount() == limbsBefore, $"{limbsBefore} -> {patient.GetLimbCount()}");
            Check("The dose wears off", !Syringes.Active.Any(d => d.Creature == patient));
            Check("An empty syringe does nothing", !Syringes.StabNow(syringe, patient.GetLimb(HumanoidNodeTagValue.Head)));
            Shot("bas-syringe-after");
            yield return Wait(1f);
            patient.Delete();

            foreach (var step in SyringeKindTests())
                yield return step;
        }

        private static IEnumerable SyringeKindTests()
        {
            Check("All nine syringes are under Props", Syringes.All.Count == 9 && Syringes.All.All(k => k.Prop.Registered && k.Prop.Item?.CategoryName == "Props"),
                string.Join(", ", Syringes.All.Select(k => k.Name)));
            AbstractCreature person = null;
            float HeadY() => person.GetLimb(HumanoidNodeTagValue.Head)?.GetPosition().y ?? 0f;
            AbstractLimb Part(HumanoidNodeTagValue part) => person.GetLimb(part);

            // Knockout: they drop, and come round once it wears off.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => person = c))
                yield return step;
            float standing = HeadY();
            Syringes.Inject(SyringeKinds.Knockout, Part(HumanoidNodeTagValue.Spine), 4f);
            yield return Wait(3f);
            Check("The Knockout Syringe drops them", standing - HeadY() > 0.7f && person.GetCognition() < 5f, $"head {standing:0.00} -> {HeadY():0.00}, awake {person.GetCognition():0}");
            yield return Wait(4f);
            Check("They come round afterwards", person.GetCognition() > 50f, $"awake {person.GetCognition():0}");
            person.Delete();

            // Acid: the part it goes into is eaten away, and the parts next to it get some.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => person = c))
                yield return step;
            Hang(person, 3f);
            var forearm = Part(HumanoidNodeTagValue.LeftForearm);
            var arm = Part(HumanoidNodeTagValue.LeftArm);
            Syringes.Inject(SyringeKinds.Acid, forearm);
            yield return Wait(1.5f);
            Shot("bas-syringe-acid");
            yield return Wait(SyringeKinds.Acid.Seconds);
            float forearmLeft = forearm.Exists() ? forearm.GetWholeness() : 0f, armLeft = arm.Exists() ? arm.GetWholeness() : 0f;
            Check("The Acid Syringe eats the part away", forearmLeft < 20f, $"{forearmLeft:0}% left");
            Check("It spreads to the part next to it", armLeft < 90f, $"{armLeft:0}% left");
            person.Delete();

            // Bone Eater: the bones go.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => person = c))
                yield return step;
            float Bones() => person.GetLimbs().SelectMany(l => l.GetAllOrgans()).Where(o => o.TryCast<Il2CppLVA.Organs.Variants.Human.Bone>() != null)
                .Select(o => o.GetIntegrity()).DefaultIfEmpty(-1f).Average();
            float bonesBefore = Bones();
            standing = HeadY();
            Syringes.Inject(SyringeKinds.BoneEater, Part(HumanoidNodeTagValue.Spine), 8f);
            yield return Wait(6f);
            Shot("bas-syringe-bones");
            Check("The Bone Eater Syringe eats the bones", bonesBefore > 0f && Bones() < bonesBefore * 0.2f, $"bones {bonesBefore:0.##} -> {Bones():0.##}");
            Check("They fold up", standing - HeadY() > 0.7f, $"head {standing:0.00} -> {HeadY():0.00}");
            var knee = Part(HumanoidNodeTagValue.LeftKnee);
            Check("Their joints go loose", SyringeKinds.IsLoose(knee) && SyringeKinds.IsLoose(Part(HumanoidNodeTagValue.RightForearm)));
            yield return Wait(2.5f);
            // The bones grow back with a Health Syringe, and the joints firm up again.
            Syringes.Inject(SyringeKinds.Health, Part(HumanoidNodeTagValue.Spine), 2f);
            for (float end = Now() + 8f; Now() < end && SyringeKinds.IsLoose(knee);)
                yield return null;
            Check("The joints firm up when the bones grow back", !SyringeKinds.IsLoose(knee), $"bones {Bones():0.##}");
            person.Delete();

            // Durability: a fresh wound grows back within a second or two.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => person = c))
                yield return step;
            Hang(person, 3f);
            Syringes.Inject(SyringeKinds.Durability, Part(HumanoidNodeTagValue.Spine), 12f);
            yield return Wait(0.5f);
            var cut = Part(HumanoidNodeTagValue.RightForearm);
            FruktSharedLibrary.Combat.Damage.Apply(cut, cut.GetPosition(), 8, 10f);
            // It heals so fast that the lowest it gets is what counts.
            float wounded = 100f;
            for (float end = Now() + 1.5f; Now() < end;)
            {
                yield return null;
                wounded = Mathf.Min(wounded, cut.GetWholeness());
            }
            yield return Wait(1.5f);
            Check("The Durability Syringe heals new wounds fast", wounded < 99f && cut.GetWholeness() >= 99f, $"{wounded:0.0} -> {cut.GetWholeness():0.0}");
            person.Delete();

            // Adrenaline: no pain and no fainting from blood loss while it lasts.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => person = c))
                yield return step;
            Syringes.Inject(SyringeKinds.Adrenaline, Part(HumanoidNodeTagValue.Spine), 6f);
            person.DrainBlood(person.GetBloodCapacity() * 0.8f);
            var leg = Part(HumanoidNodeTagValue.LeftLeg);
            FruktSharedLibrary.Combat.Damage.Apply(leg, leg.GetPosition(), 6, 5f);
            yield return Wait(3f);
            Check("The Adrenaline Syringe keeps them awake and out of pain", person.GetCognition() > 95f && person.GetPain() < 1f,
                $"awake {person.GetCognition():0}, pain {person.GetPain():0.#}");
            person.Delete();

            // Float: up they go.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f), c => person = c))
                yield return step;
            standing = HeadY();
            Syringes.Inject(SyringeKinds.Float, Part(HumanoidNodeTagValue.Spine), 4f);
            yield return Wait(3f);
            Shot("bas-syringe-float");
            Check("The Float Syringe lifts them", HeadY() - standing > 1.5f, $"head {standing:0.00} -> {HeadY():0.00}");
            yield return Wait(1.5f);
            person.Delete();

            // Explosive: it goes off where they are.
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(6f), c => person = c))
                yield return step;
            var where = person.GetPosition();
            float whole = Hurt(person);
            Syringes.Inject(SyringeKinds.Explosive, Part(HumanoidNodeTagValue.Spine));
            yield return Wait(SyringeKinds.Explosive.Seconds - 0.5f);
            Check("It hasn't gone off yet", Hurt(person) < whole + 5f);
            yield return Wait(1.2f);
            Check("The Explosive Syringe blows them up", Hurt(person) > whole + 300f && Vector3.Distance(where, person.GetPosition()) > 1f,
                $"{whole:0} -> {Hurt(person):0}, thrown {Vector3.Distance(where, person.GetPosition()):0.0} m");
            yield return Wait(1f);
            Creatures.DeleteAll();
            yield return Wait(0.5f);

            // Rage: they start a fight.
            var right = LocalPlayer.CameraRotation * Vector3.right;
            AbstractCreature other = null;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f) - right, c => person = c))
                yield return step;
            foreach (var step in SpawnHuman(LocalPlayer.GetPointInFront(4f) + right, c => other = c))
                yield return step;
            Teams.ClearMembers();
            Syringes.Inject(SyringeKinds.Rage, Part(HumanoidNodeTagValue.Spine));
            yield return Wait(0.5f);
            Check("The Rage Syringe starts a fight", Fights.IsFighting(person) && Fights.TargetOf(person) == other);
            Fights.StopAll();
            Creatures.DeleteAll();
            yield return Wait(0.5f);
        }

        // ------------------------------------------------------------ helpers

        private static IEnumerable SpawnHuman(Vector3 position, Action<AbstractCreature> done)
        {
            AbstractCreature spawned = null;
            Creatures.SpawnHuman(position + Vector3.up * 0.1f, LocalPlayer.RotationFacingPlayer(position), c => spawned = c);
            for (float end = Now() + 10f; spawned == null && Now() < end;)
                yield return null;
            yield return Wait(1.5f);
            done(spawned);
        }

        /// <summary>Holds someone still with their hips right in the crosshair.</summary>
        private static void Hang(AbstractCreature creature, float distance)
        {
            if (creature == null || !creature.IsValid())
                return;
            creature.SetFrozen(true);
            creature.TeleportTo(LocalPlayer.CameraPosition + LocalPlayer.Forward * distance);
            Physics.SyncTransforms();
        }

        private static void Place(Rigidbody body, float distance)
        {
            body.velocity = Vector3.zero;
            body.position = LocalPlayer.CameraPosition + LocalPlayer.Forward * distance;
            body.transform.position = body.position;
            Physics.SyncTransforms();
        }

        /// <summary>How much of the body is gone, added up over the limbs (0 for someone untouched).</summary>
        private static float Hurt(AbstractCreature creature)
        {
            if (creature == null || !creature.IsValid())
                return 0f;
            return creature.GetLimbs().Sum(l => 1f - l.GetWholeness());
        }

        private static void Click(string name) => FruktLog.Msg($"[SelfTest] CLICK {name} 0.5000 0.5000");

        private static void Shot(string name) => FruktLog.Msg("[SelfTest] SCREENSHOT " + name);

        /// <summary>A screenshot, with time for the watcher to take it before anything changes.</summary>
        private static IEnumerator ShotAndWait(string name)
        {
            Shot(name);
            return Wait(1.5f);
        }

        private static float Now() => Time.realtimeSinceStartup;

        private static IEnumerator Wait(float seconds)
        {
            for (float end = Now() + seconds; Now() < end;)
                yield return null;
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok)
                _passed++;
            else
                _failed++;
            string line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  ->  " + detail)}";
            Report.Add(line);
            if (ok)
                Log(line);
            else
                FruktLog.Warning("[Bunch-A-Stuff] [SelfTest] " + line);
        }

        private static void Log(string text) => FruktLog.Msg("[Bunch-A-Stuff] [SelfTest] " + text);

        private static void Finish()
        {
            foreach (var thing in Spawned)
            {
                if (thing.Exists())
                    Object.Destroy(thing);
            }
            Spawned.Clear();
            File.WriteAllLines(LogPath, new[] { $"Bunch-A-Stuff! {Main.Version} self-test  {DateTime.Now:yyyy-MM-dd HH:mm:ss}", $"Passed: {_passed}  Failed: {_failed}", "" }.Concat(Report));
            FruktLog.Msg($"[SelfTest] Done: {_passed} passed, {_failed} failed");
            if (_quit)
                Scheduler.After(2f, Application.Quit);
        }
    }
}
