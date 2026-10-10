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
        public const string Version = "1.0.3";

        public override void OnInitializeMelon()
        {
            Settings.Create();
            Guns.Create();
            Teams.Create();
            Fights.Create();
            Syringes.Create();
            Clothes.Create();
            Page.Create();
            GameEvents.SandboxReady += _ => Reset();
            GameEvents.SandboxExited += Reset;
            SelfTest.Initialize();
        }

        public override void OnUpdate()
        {
            Guns.Update();
            Fights.Update();
            Syringes.Update();
            Bruises.Update();
            Clothes.Update();
            Teams.Update();
        }

        private static void Reset()
        {
            Guns.Reset();
            Fights.Reset();
            Syringes.Reset();
            Bruises.Reset();
            Clothes.Reset();
            Teams.ClearMembers();
        }
    }
}
