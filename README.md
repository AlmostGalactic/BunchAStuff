# Bunch-A-Stuff!

A bunch of stuff for FRUKT: six guns, fist fights, and teams.

Requires [FruktSharedLibrary](https://github.com/AlmostGalactic/FruktSharedLibrary) 0.3.0 or newer.

## Installing

1. Install [MelonLoader](https://melonwiki.xyz/) 0.7 or newer for FRUKT.
2. Put `FruktSharedLibrary.dll` (0.3.0 or newer) in `FRUKT/Mods`.
3. Put `BunchAStuff.dll` in `FRUKT/Mods`.

## Guns

They're under Weapons in the terminal (I). Left click fires.

| Gun | What it is |
|-----|------------|
| Moth-9 | Submachine gun. Fires while the button is held. |
| Barrow-12 | Pump shotgun, nine pellets a shot. |
| Heron-R | Rail rifle. Goes through up to eight bodies or loose objects in a line. |
| Tusk-40 | Rocket launcher. The rocket explodes on impact. |
| Gale-2 | Air cannon. Throws whatever it hits without hurting it. |
| Halt-1 | Holds whatever it hits still in mid-air. Hit it again to let go. |

The models are placeholders for now.

## Fights

Right-click a person and pick **Fight nearest**. They walk up to the nearest person who isn't on their team, put
their guard up and start throwing punches: jabs, crosses, hooks, uppercuts and body shots, each at their own pace.
Punches that hit the other person's hands or forearms are blocked. Whoever gets picked on fights back. When their
opponent is down, they go for the next one.

Pick **Stop fighting** on the same menu to stop them. The mod's page in the mod menu (F8) has **Everyone fights**
and **Stop all fights**.

## Teams

Right-click a person and open **Team** to put them on Red, Blue, Green or Yellow. Teammates never fight or hit each
other, and each person's team shows over their head.

On the mod's page in the mod menu you can make your own teams (a name and a colour), delete them, split everyone
into Red and Blue, or take everyone off their teams. Teams you make are saved.

## Settings

The Bunch-A-Stuff! settings page in the mod menu has gun damage, punch damage and force, whether people fight back,
and whether team names show.

## Testing

Create `UserData/BunchAStuff.selftest` (write `quit` in it to close the game afterwards) and start the game with
FruktSharedLibrary's `tools/selftest-watch.ps1` running. It loads the Yard, fires every gun, starts fights, tries
the teams, and writes `UserData/BunchAStuff.selftest.log`. Delete the file afterwards, or it runs every time.
