# Changelog

## 1.1.0

Requires FruktSharedLibrary 0.4.2 or newer. That fixes the broken terminal and toolbar people got with only this mod
and the library installed.

Melee, under the new Melee tab (left click swings):

- Baseball Bat: bruises, breaks bones and knocks people out.
- Sledgehammer: slow and very heavy. Caves in what it hits, snaps bones and throws people.
- Frying Pan: not much damage, but a good one to the head puts their lights out.
- Machete: cuts deep, and takes off a limb that's already been hacked at.
- Kitchen Knife: stabs. Narrow, deep and bleeds hard; in the chest or belly it bleeds inside too.

Grenades, under the new Grenades tab (left click throws):

- Frag Grenade: a blast and 30 pieces of shrapnel.
- Flashbang: everyone close by drops, stunned, for a few seconds.
- Smoke Grenade: thick smoke for fifteen seconds.

Syringes:

- Poison: they bleed inside, cough up blood and weaken over half a minute.
- Blood Thinner: every wound they have pours.
- Strength: punches land more than twice as hard for half a minute.
- Paralysis: every muscle goes slack for 15 seconds; they get back up afterwards.
- Berserk: they go for anyone near them, their own team too, hitting twice as hard.
- Bone Breaker: snaps the bones in their arms and legs one after another.

Right-click a person, Injure: break this bone, bleed inside here, stab here, knock out, wake up.

Performance:

- Clothes are far cheaper to draw: neighbouring faces of the same colour are joined into one, so ten dressed people
  went from about 840,000 vertices to under 40,000, with the same look. The guns and syringes are drawn the same way.
- Everyone wearing the same outfit colours shares one material, so they draw together.
- Clothes no longer throw their own shadow on top of the body's.
- Clothes only work out where they've been torn when the body under them has really lost voxels.

Blood:

- The game's blood now goes into wounds and onto whatever a cut opens up, not only onto the skin. There's a setting
  to turn this off.

Injuries:

- Every hard knock now hurts under the skin too, even under clothes. A limb that keeps getting hit starts bleeding
  inside: they lose blood, and a chest, belly or head that's bleeding inside brings blood up out of the mouth.
- A hard hit on a spot that's already badly bruised splits the skin open and it bleeds.
- Arms and legs that take too much break with a crack and hang loose.
- Enough blows to the head knock people out. They drop, and come round some seconds later.
- The Health Syringe mends all of it: bleeding inside stops, bones set, and they wake up.
- A new setting turns injuries off.

Bruises:

- Bruises are darker. Fresh ones start red and swollen, then go deep plum, blue, green and yellow.
- Hard knocks leave a scatter of small dark dots round the edge.
- Bleeding underneath shows as a deep bruise that spreads over the next minutes, goes nearly black in the middle and
  takes much longer to heal.

Fights:

- Fighters move: they close in, plant their feet and trade, then step round to a new angle.
- They throw real combinations (jab, cross, hook, body shot, uppercut) and shove when they're too close.
- Each punch hits as hard as it should: a jab is light, a hook or an uppercut is heavy.
- Fighters tire. Tired fighters punch slower and softer, and their hands sink.
- A fresh fighter blocks more, and a punch caught on the guard gets a counter straight back.
- Big shots rock people for a moment, and enough of them knock them out. The winner moves on to the next person.
- A broken arm can't punch. Someone who's lost half their blood, or has both arms broken, gives up and runs.
- Knuckles bruise too.
- People knocked down let their arms go loose and get back up before carrying on; whoever knocked them down waits
  for them to get up.
- A knocked-out fighter comes round and picks the fight back up. The winner keeps an eye out for the next one.
- A broken arm hangs limp, and a punch never reaches further than an arm can.

Other:

- The Clothed Human Spawner has its own icon: the Human Spawner's with a hat on.
- People put out by the Knockout Syringe get back up when they come round, instead of lying there awake.

## 1.0.3

Requires FruktSharedLibrary 0.4.1 or newer.

- The game's Human Spawner no longer makes dressed people while the Clothed Human Spawner is in the toolbar or in hand.
  A spawner put down in the world decides for the people born at it.

## 1.0.2

Requires FruktSharedLibrary 0.4.1 or newer.

- The Clothed Human Spawner, under Etc next to the Human Spawner. The people it makes come out in everyday clothes:
  T-shirt and jeans, a hoodie, a flannel shirt, a polo and chinos, a sweater, a tank top and shorts, a tracksuit or a
  summer shirt.
- Work clothes from the right-click menu (Clothes > Work clothes): office worker, businessman, builder, doctor, police
  officer, chef, footballer, farmer and prisoner. Clothes > Everyday clothes changes them back, and anyone can be
  dressed this way.
- The clothes are voxel models on the body. Shots and cuts tear holes in them with bloody edges, a cut-off limb takes
  its sleeve or trouser leg with it, blood gets on them, and a hat falls off a badly hurt head.

## 1.0.1

Requires FruktSharedLibrary 0.4.0 or newer.

- The guns and the syringes have their own tabs in the terminal, Guns and Syringes, instead of being mixed in with
  Weapons and Props.

## 1.0.0

The first release. Requires FruktSharedLibrary 0.3.7 or newer.

- Six guns under Weapons: the Moth-9, Barrow-12, Heron-R, Tusk-40, Gale-2 and Halt-1. They fire the game's own
  bullets, and the Tusk-40's rockets tear the limbs off anyone close and throw them.
- Fist fights from the right-click menu, with a guard, five kinds of punch, blocks and people who fight back.
- Teams that never fight each other, made and changed on the mod's page.
- Nine syringes under Props: Health, Knockout, Acid, Bone Eater, Durability, Adrenaline, Float, Explosive and Rage.
  Stab them in with the cursor, or throw them needle first with G.
- Bruises where people are punched or hit something hard, healing slowly from red through purple to yellow.
