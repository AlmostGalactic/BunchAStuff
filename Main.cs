using FruktSharedLibrary.Core;
using MelonLoader;

[assembly: MelonInfo(typeof(BunchAStuff.Main), "Bunch-A-Stuff!", BunchAStuff.Main.Version, "AlmostGalactic")]
[assembly: MelonGame("tripledose", "FRUKT")]
// Loads FruktSharedLibrary first, and warns players who don't have it.
[assembly: MelonAdditionalDependencies("FruktSharedLibrary")]

namespace BunchAStuff
{
    /// <summary>
    /// A bunch of stuff for FRUKT: guns under Weapons in the terminal, fist fights from the right-click menu, and
    /// teams whose members never fight each other.
    /// </summary>
    public class Main : MelonMod
    {
        public const string Version = "0.2.0";

        public override void OnInitializeMelon()
        {
            Settings.Create();
            Guns.Create();
            Teams.Create();
            Fights.Create();
            Page.Create();
            GameEvents.SandboxReady += _ => Reset();
            GameEvents.SandboxExited += Reset;
            SelfTest.Initialize();
        }

        public override void OnUpdate()
        {
            Guns.Update();
            Fights.Update();
            Teams.Update();
        }

        private static void Reset()
        {
            Guns.Reset();
            Fights.Reset();
            Teams.ClearMembers();
        }
    }
}
