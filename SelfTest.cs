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
            Check("The blast throws fire, smoke and debris", FruktSharedLibrary.Combat.Effects.Count > 100, $"{FruktSharedLibrary.Combat.Effects.Count} cubes");
            Check("The rocket blows up on the dummy", blast.HasValue && Vector3.Distance(blast.Value, dummy.GetPosition()) < 1.5f,
                blast.HasValue ? $"{Vector3.Distance(blast.Value, dummy.GetPosition()):0.00} m away" : "it never went off");
            Shot("bas-blast");
            yield return Wait(0.5f);
            Check("The blast hurts the dummy", Hurt(dummy) > before, $"{before:0.00} -> {Hurt(dummy):0.00}");
            dummy.Delete();

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
            Check("Made teams are saved", Settings.LoadCustomTeams().Any(t => t.Name == "Test Squad"));
            Teams.Join(a, made);
            Check("People can join a made team", Teams.Of(a) == made && Teams.MembersOf(made).Count == 1);
            Check("A made team can be deleted", Teams.Remove(made) && Teams.Find("Test Squad") == null && Teams.Of(a) == null);
            Check("Deleting it takes it out of the save", !Settings.LoadCustomTeams().Any(t => t.Name == "Test Squad"));
            Check("The starting teams can't be deleted", !Teams.Remove(red) && Teams.Find("Red") != null);

            Check("Split everyone puts two people on Red and Blue", Teams.SplitEveryone() == 2 && Teams.Of(a) != Teams.Of(b) && Teams.Of(a) != null);
            Check("Everyone fights starts both", Fights.EveryoneFights() == 2 && Fights.Count == 2);
            yield return Wait(4f);
            yield return ShotAndWait("bas-team-fight");
            Fights.StopAll();
            Teams.ClearMembers();
            Check("Leaving a team takes the tag away", tag != null && !tag.Exists && Teams.TagOf(a) == null);
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
