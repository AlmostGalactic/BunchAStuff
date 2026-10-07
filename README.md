# Bunch-A-Stuff!

A bunch of stuff for FRUKT: six guns, fist fights, and teams.

Requires [FruktSharedLibrary](https://github.com/AlmostGalactic/FruktSharedLibrary) 0.3.1 or newer.

## Installing

1. Install [MelonLoader](https://melonwiki.xyz/) 0.7 or newer for FRUKT.
2. Put `FruktSharedLibrary.dll` (0.3.1 or newer) in `FRUKT/Mods`.
3. Put `BunchAStuff.dll` in `FRUKT/Mods`.

## Guns

They're under Weapons in the terminal (I). Left click fires. The bullets are the game's own, so they wound the way
the Viper-17 and Grist-03 do: through the body, with blood, and out the other side. The models are made of voxels
at the same size as the game's guns.

| Gun | What it is |
|-----|------------|
| Moth-9 | Submachine gun, 9mm. Fires while the button is held. |
| Barrow-12 | Pump shotgun, nine 12-gauge pellets a shot. |
| Heron-R | Rail rifle. A 7.62 round at several times the speed, through everything in a line. |
| Tusk-40 | Rocket launcher. The rocket leaves a trail and explodes on impact. |
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

## Settings

The Bunch-A-Stuff! settings page in the mod menu has explosion damage, punch damage and force, whether people fight back,
and whether team names show.

## Testing

Create `UserData/BunchAStuff.selftest` (write `quit` in it to close the game afterwards) and start the game with
FruktSharedLibrary's `tools/selftest-watch.ps1` running. It loads the Yard, fires every gun, starts fights, tries
the teams, and writes `UserData/BunchAStuff.selftest.log`. Delete the file afterwards, or it runs every time.
