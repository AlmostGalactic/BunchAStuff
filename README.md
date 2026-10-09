# Bunch-A-Stuff!

A bunch of stuff for FRUKT: six guns, fist fights, teams, syringes and bruises.

Requires [FruktSharedLibrary](https://github.com/AlmostGalactic/FruktSharedLibrary) 0.3.8 or newer.

## Installing

1. Install [MelonLoader](https://melonwiki.xyz/) 0.7 or newer for FRUKT.
2. Put `FruktSharedLibrary.dll` (0.3.8 or newer) in `FRUKT/Mods`.
3. Put `BunchAStuff.dll` in `FRUKT/Mods`.

## Guns

They have their own **Guns** tab in the terminal (I). Left click fires. The bullets are the game's own, so they wound the way
the Viper-17 and Grist-03 do: through the body, with blood, and out the other side. The models are made of voxels
at the same size as the game's guns.

| Gun | What it is |
|-----|------------|
| Moth-9 | Submachine gun, 9mm. Fires while the button is held. |
| Barrow-12 | Pump shotgun, nine 12-gauge pellets a shot. |
| Heron-R | Rail rifle. A 7.62 round at several times the speed, through everything in a line. |
| Tusk-40 | Rocket launcher. The rocket leaves a trail and explodes on impact, tearing the limbs off anyone close and throwing them. |
| Gale-2 | Air cannon. Throws whatever it hits without hurting it. |
| Halt-1 | Holds whatever it hits still in mid-air. Hit it again to let go. |

## Fights

Right-click a person and pick **Fight nearest**. They walk up to the nearest person who isn't on their team, put
their guard up and start throwing punches: jabs, crosses, hooks, uppercuts and body shots, each at their own pace.
Punches that hit the other person's hands or forearms are blocked. Whoever gets picked on fights back. When their
opponent is down, they go for the next one.

Pick **Stop fighting** on the same menu to stop them. The mod's page in the mod menu (F8) has **Everyone fights**
and **Stop all fights**.

## Teams

Right-click a person and open **Team** to put them on one of your teams. You start with Red, Blue, Green and Yellow. Teammates never fight or hit each
other, and each person's team shows over their head.

On the mod's page in the mod menu you can make teams (a name and a colour), delete any of them, split everyone
between all the teams as evenly as possible, or take everyone off their teams. Your list of teams is saved.

## Syringes

Syringes have their own **Syringes** tab in the terminal. Put one down, pick it up with the cursor and push the needle into
someone, or press **G** while you hold it to throw it, needle first, at whatever you're aiming at. It sticks in, the
plunger goes down and the dose takes effect. Grab it again to pull it out. Each syringe works once. The throw key can
be changed in the mod's settings.

| Syringe | What it does |
|---------|--------------|
| Health Syringe | Stops the bleeding, fills the blood back up and grows damaged flesh back over about 4 seconds. Limbs that came off stay off. |
| Knockout Syringe | Out cold for 20 seconds. They come round afterwards if nothing else is wrong with them. |
| Acid Syringe | Eats away the part it goes into, spreading unevenly out from the needle, then gets into the parts next to it. |
| Bone Eater Syringe | Dissolves every bone in the body and they fold up, and joints without a bone bend any way. A Health Syringe grows the bones back. |
| Durability Syringe | For a minute, lost flesh grows back within a second, wounds close and the blood stays topped up. |
| Adrenaline Syringe | For 30 seconds no pain, and nothing knocks them out, not even blood loss. |
| Float Syringe | Lighter than air for 8 seconds. |
| Explosive Syringe | Ticks for 4 seconds, faster and faster, then blows up like a Tusk-40 rocket. |
| Rage Syringe | They go for the nearest person from another team with their fists. |

## Bruises

People bruise where they're punched or hit something hard: a fall, a crash, being thrown. A bruise is a patch of
uneven blotches that starts red, darkens to deep purple, then slowly turns blue, olive, green and yellow as it heals,
the edges before the middle, and fades away. The colour changes gradually the whole time. A bruise takes 20 minutes
to heal (you can set anything from 1 minute to 2 hours). Another knock on the same spot makes it darker and bigger.
Only the living bruise, and a Health Syringe clears them.

## Settings

The Bunch-A-Stuff! settings page in the mod menu has explosion damage, punch damage and force, whether people fight back,
whether team names show, the key that throws a syringe, whether people bruise and how long bruises take to heal.

## Building

You need FRUKT with MelonLoader (start the game once so it makes `MelonLoader/Il2CppAssemblies`) and
`FruktSharedLibrary.dll` in `FRUKT/Mods`. Set the `FRUKT_DIR` environment variable to the game's folder if it isn't
at `D:\SteamLibrary\steamapps\common\FRUKT`, then run `dotnet build`. The built mod is copied into `FRUKT/Mods`.

## Testing

Create `UserData/BunchAStuff.selftest` (write `quit` in it to close the game afterwards) and start the game with
FruktSharedLibrary's `tools/selftest-watch.ps1` running. It loads the Yard, fires every gun, starts fights, tries
the teams, the syringes and bruises, and writes `UserData/BunchAStuff.selftest.log`. Delete the file afterwards, or it runs every time.
