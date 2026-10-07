using UnityEngine;

namespace BunchAStuff
{
    /// <summary>
    /// The guns' models, built voxel by voxel at the same size as the game's own guns. Each one points down +Z with
    /// the hand at the origin (the middle of the pistol grip). Positions are in voxels; <see cref="At"/> turns one
    /// into the metres the gun's muzzle needs.
    /// </summary>
    internal static class GunModels
    {
        internal static Vector3 At(float x, float y, float z) => new(x * Voxels.Size, y * Voxels.Size, z * Voxels.Size);

        private static Color C(int rgb) => Voxels.Hex(rgb);

        // ------------------------------------------------------------ Moth-9

        internal static readonly Vector3 SmgMuzzle = At(0, 6, 46);

        internal static GameObject Smg()
        {
            var v = new Voxels()
                .Key('G', C(0x4a4f57)).Key('g', C(0x6b717b)).Key('K', C(0x202124)).Key('k', C(0x0a0b0c))
                .Key('S', C(0xa4a9b1)).Key('s', C(0x6d727a)).Key('O', C(0xe0701a)).Key('R', C(0xff2a20), glow: true)
                .Key('L', C(0x2c6a9a)).Key('B', C(0xc79a3a));

            // Receiver, with its edges taken off.
            v.Box(-3, 3, 2, 10, -13, 22, 'G');
            foreach (int x in new[] { -3, 3 })
                v.Carve(x, x, 10, 10, -13, 22).Carve(x, x, 2, 2, -13, 22);
            v.Box(-2, 2, 11, 11, -12, 20, 's');
            for (int z = -10; z <= 18; z += 2)
                v.Box(-1, 1, 12, 12, z, z, 'S');
            v.Paint(-3, 3, 3, 3, -13, 22, 'g').Paint(-3, 3, 9, 9, -13, 22, 'g');
            v.Box(-3, 3, 3, 9, -15, -14, 'K');
            v.Box(-2, 2, 4, 8, -16, -16, 'k');

            // Ejection port on the right, the selector and the charging handle on the left.
            v.Carve(3, 3, 6, 9, 5, 12).Paint(2, 2, 6, 9, 5, 12, 'k');
            v.Box(2, 3, 5, 5, 6, 8, 'B');
            v.Box(-4, -4, 7, 8, 8, 17, 's').Box(-5, -5, 7, 8, 14, 17, 'S');
            v.Box(-4, -4, 4, 4, -6, -5, 'O').Box(4, 4, 4, 4, -6, -5, 'O');
            v.Paint(3, 3, 5, 8, -11, -3, 'g').Paint(-3, -3, 5, 8, -11, -3, 'g');

            // Low reflex sight: a frame round a pane of glass, with the red dot lit.
            v.Box(-2, 2, 12, 12, -1, 7, 'K');
            v.Box(-2, -2, 13, 16, -1, 7, 'K').Box(2, 2, 13, 16, -1, 7, 'K');
            v.Box(-2, 2, 17, 17, -1, 7, 'K');
            v.Box(-1, 1, 13, 16, 3, 3, 'L').Set(0, 15, 3, 'R');
            v.Box(-1, 1, 13, 13, 0, 6, 'K');
            v.Paint(-2, -2, 14, 15, 0, 6, 's').Paint(2, 2, 14, 15, 0, 6, 's');

            // Barrel shroud with its cooling slots, the barrel and the muzzle brake.
            v.Tube(0, 6, 23, 36, 3.4f, 'G');
            for (int z = 25; z <= 35; z += 3)
            {
                v.Box(3, 3, 5, 7, z, z, 'k').Box(-3, -3, 5, 7, z, z, 'k');
                v.Box(-1, 1, 9, 9, z, z, 'k').Box(-1, 1, 3, 3, z, z, 'k');
            }
            v.Paint(-3, 3, 6, 6, 23, 24, 's');
            v.Tube(0, 6, 37, 40, 2.2f, 's');
            v.Tube(0, 6, 41, 46, 2.7f, 'S', 1.1f);
            v.Set(0, 8, 43, 'k').Set(0, 8, 44, 'k').Set(0, 4, 43, 'k').Set(0, 4, 44, 'k');
            v.Box(0, 0, 10, 12, 35, 35, 'K').Box(-1, -1, 10, 11, 34, 36, 'K').Box(1, 1, 10, 11, 34, 36, 'K');

            // Grip, trigger guard and trigger.
            v.Lean(-2, 2, -13, 1, -3, 3, -0.32f, 'K');
            for (int y = -12; y <= 0; y++)
            for (int z = -9; z <= 3; z++)
            {
                if ((y + z) % 2 == 0)
                    v.Paint(-2, -2, y, y, z, z, 'k').Paint(2, 2, y, y, z, z, 'k');
            }
            v.Box(-1, 1, -5, -5, 4, 13, 'K').Box(-1, 1, -5, 1, 12, 13, 'K').Box(0, 0, -3, 0, 7, 8, 'S');

            // A short foregrip under the shroud.
            v.Box(-3, 3, 0, 2, 25, 32, 'G');
            v.Lean(-2, 2, -9, -1, 26, 31, 0.15f, 'K');
            foreach (int y in new[] { -2, -4, -6, -8 })
                v.Paint(-2, 2, y, y, 20, 40, 's');

            // The magazine, curving forward, with a ribbed body and an orange base.
            v.Box(-3, 3, -1, 1, 14, 22, 'G');
            v.Lean(-2, 2, -19, -2, 15, 21, 0.28f, 'K');
            foreach (int y in new[] { -5, -8, -11, -14, -17 })
                v.Paint(-2, 2, y, y, 10, 40, 's');
            v.Paint(-2, 2, -19, -18, 10, 40, 'O');

            // Folding stock: two rods, a hinge, braces, and a curved shoulder plate.
            v.Box(-2, -2, 7, 8, -29, -15, 's').Box(2, 2, 7, 8, -29, -15, 's');
            v.Rod(2, 3, -15, 2, -4, -28, 's').Rod(-2, 3, -15, -2, -4, -28, 's');
            v.Box(-3, 3, 5, 9, -18, -16, 'S');
            for (int y = -5; y <= 10; y++)
            {
                float u = (y - 2.5f) / 7.75f;
                int shift = Mathf.RoundToInt(2.2f * u * u);
                v.Box(-3, 3, y, y, -32 - shift, -29 - shift, 'K');
                v.Box(-3, 3, y, y, -33 - shift, -33 - shift, 'k');
            }
            v.Carve(-1, 1, 0, 6, -33, -29);
            v.Carve(-1, 1, 0, 6, -34, -34);
            return v.Build("Moth-9");
        }

        // ------------------------------------------------------------ Barrow-12

        internal static readonly Vector3 ShotgunMuzzle = At(0, 8, 76);

        internal static GameObject Shotgun()
        {
            var v = new Voxels()
                .Key('W', C(0x5e3418)).Key('w', C(0x7a4a24)).Key('d', C(0x3e2210)).Key('B', C(0x1d2026))
                .Key('b', C(0x383c44)).Key('S', C(0x8a8e94)).Key('K', C(0x0e0e10)).Key('k', C(0x040405))
                .Key('R', C(0xb81f18)).Key('Y', C(0xd8a636)).Key('O', C(0xe0701a));

            // Receiver, with its ejection port.
            v.Box(-3, 3, 2, 11, -12, 18, 'B');
            v.Box(-1, 1, 12, 12, -11, 17, 'b');
            v.Carve(3, 3, 6, 10, 0, 10).Paint(2, 2, 6, 10, 0, 10, 'k');
            v.Paint(-3, 3, 11, 11, -12, 18, 'b');
            v.Box(-1, 1, 13, 14, -12, -11, 'B').Box(0, 0, 15, 15, -12, -12, 'B');
            v.Box(4, 4, 3, 4, 12, 14, 'S').Box(-4, -4, 3, 4, 12, 14, 'S');

            // Barrel over a magazine tube, held together by a band and capped at the end.
            v.Tube(0, 8, 19, 76, 2.2f, 'B');
            v.Box(0, 0, 11, 11, 19, 76, 'b');
            v.Tube(0, 3, 19, 66, 2.2f, 'B');
            v.Box(-2, 2, 1, 10, 50, 51, 'b');
            v.Box(-2, 2, 1, 10, 66, 67, 'b');
            v.Tube(0, 3, 67, 68, 2.7f, 'b');
            v.Set(0, 12, 75, 'Y');
            v.Tube(0, 8, 76, 76, 2.2f, 'k', 1.0f);

            // The pump: walnut, with a groove every three voxels, riding on two bars.
            v.Box(-3, 3, -3, 6, 28, 48, 'W');
            for (int z = 30; z <= 46; z += 3)
                v.Paint(-3, 3, -3, 6, z, z, 'd');
            v.Paint(-3, 3, 6, 6, 28, 48, 'B');
            v.Box(-3, -3, 5, 6, 18, 28, 'S').Box(3, 3, 5, 6, 18, 28, 'S');

            // Trigger and its guard.
            v.Box(-1, 1, -5, -5, -1, 12, 'B').Box(-1, 1, -5, 1, 11, 12, 'B').Box(0, 0, -3, 0, 5, 6, 'S');

            // Walnut stock, dropping away from the receiver, with a rubber pad.
            for (int z = -13; z >= -46; z--)
            {
                float t = (-12 - z) / 34f;
                int top = Mathf.RoundToInt(Mathf.Lerp(11f, 6f, t));
                int bottom = Mathf.RoundToInt(1f - 11f * Mathf.Pow(t, 1.3f));
                int half = z < -26 ? 3 : 2;
                v.Box(-half, half, bottom, top, z, z, 'W');
                if (z % 5 == 0)
                    v.Paint(-half, half, top - 1, top - 1, z, z, 'w');
            }
            v.Paint(-3, 3, 5, 5, -44, -14, 'w').Paint(-3, 3, 1, 1, -40, -20, 'd').Paint(-3, 3, -2, -2, -42, -30, 'w');
            v.Box(-3, 3, -11, 6, -49, -47, 'K');

            // Spare shells on the left of the receiver, brass up from the bottom.
            for (int i = 0; i < 4; i++)
            {
                int z = -8 + i * 4;
                v.Box(-4, -4, 5, 9, z, z + 1, 'R').Box(-4, -4, 3, 4, z, z + 1, 'Y').Box(-5, -5, 3, 3, z, z + 1, 'Y');
            }
            return v.Build("Barrow-12");
        }

        // ------------------------------------------------------------ Heron-R

        internal static readonly Vector3 RailgunMuzzle = At(0, 6, 90);

        internal static GameObject Railgun()
        {
            var v = new Voxels()
                .Key('W', C(0xd8dde2)).Key('w', C(0xa4acb6)).Key('D', C(0x16181c)).Key('d', C(0x30343b))
                .Key('S', C(0x7a808a)).Key('C', C(0x2ee6ff), glow: true).Key('c', C(0x147a8c), glow: true)
                .Key('G', C(0x103044));

            // Receiver.
            v.Box(-3, 3, 2, 10, -12, 30, 'W');
            v.Carve(3, 3, 10, 10, -12, 30).Carve(-3, -3, 10, 10, -12, 30);
            v.Paint(3, 3, 5, 7, -8, 26, 'C').Paint(-3, -3, 5, 7, -8, 26, 'C');
            v.Paint(-3, 3, 2, 2, -12, 30, 'w');
            v.Box(-2, 2, 4, 9, 31, 36, 'W');

            // Open stock: a sloping spine with a cheek riser, a strut underneath and a pad, joined up at the back.
            for (int z = -46; z <= -13; z++)
            {
                float t = (z + 46) / 33f;
                int top = Mathf.RoundToInt(Mathf.Lerp(8f, 10f, t));
                v.Box(-2, 2, top - 4, top, z, z, 'W');
                int low = Mathf.RoundToInt(Mathf.Lerp(-9f, -6f, t * t));
                if (z < -24)
                    v.Box(-2, 2, low, low + 2, z, z, 'W');
            }
            v.Box(-2, 2, 11, 13, -34, -18, 'W').Box(-2, 2, 14, 14, -32, -20, 'w');
            v.Paint(0, 0, 14, 14, -32, -20, 'C').Paint(-2, 2, 13, 13, -33, -19, 'w');
            v.Box(-3, 3, -9, 10, -50, -46, 'w').Box(-3, 3, -9, 10, -51, -51, 'D');
            v.Paint(-3, 3, -9, 10, -47, -47, 'd');
            v.Box(-2, 2, -9, 6, -46, -44, 'W');
            v.Rod(2, -6, -24, 2, -9, -34, 'W').Rod(-2, -6, -24, -2, -9, -34, 'W');

            // Grip, trigger and guard.
            v.Lean(-2, 2, -14, 1, -4, 2, -0.35f, 'D');
            v.Box(-1, 1, -6, -6, 3, 13, 'D').Box(-1, 1, -6, 1, 12, 13, 'D').Box(0, 0, -4, 0, 6, 7, 'S');

            // Energy cell under the front of the receiver, glowing through its window.
            v.Box(-2, 2, -4, 1, 10, 28, 'D');
            v.Paint(2, 2, -3, 0, 12, 26, 'c').Paint(-2, -2, -3, 0, 12, 26, 'c');
            v.Paint(2, 2, -2, -1, 14, 24, 'C').Paint(-2, -2, -2, -1, 14, 24, 'C');

            // The rail pair along the barrel, with a coil ring every few voxels.
            v.Tube(0, 6, 37, 88, 1.9f, 'S');
            v.Box(2, 3, 4, 8, 37, 82, 'w').Box(-3, -2, 4, 8, 37, 82, 'w');
            v.Paint(3, 3, 5, 7, 39, 80, 'C').Paint(-3, -3, 5, 7, 39, 80, 'C');
            for (int z = 40; z <= 76; z += 6)
            {
                v.Tube(0, 6, z, z + 1, 4.5f, 'd', 3.2f);
                v.Set(0, 10, z, 'c').Set(0, 2, z, 'c').Set(0, 10, z + 1, 'c').Set(0, 2, z + 1, 'c');
            }
            v.Tube(0, 6, 83, 90, 2.9f, 'W', 1.5f);
            v.Tube(0, 6, 84, 84, 1.4f, 'C');
            v.Box(-1, 1, -8, -1, 42, 46, 'D').Box(-2, 2, -2, 0, 40, 48, 'd');

            // Scope: two mounts, a body, a flared front and a lens.
            v.Box(-1, 1, 11, 13, -5, -3, 'D').Box(-1, 1, 11, 13, 9, 11, 'D');
            v.Tube(0, 15, -9, 15, 2.2f, 'D');
            v.Tube(0, 15, -13, -9, 2.9f, 'd');
            v.Tube(0, 15, 12, 20, 3.1f, 'd', 2.1f);
            v.Tube(0, 15, 15, 15, 2.1f, 'G');
            v.Tube(0, 15, 16, 16, 1.2f, 'c');
            v.Box(-1, 1, 18, 19, 2, 4, 'd').Box(2, 3, 14, 16, 2, 4, 'd');
            return v.Build("Heron-R");
        }

        // ------------------------------------------------------------ Tusk-40

        internal static readonly Vector3 LauncherMuzzle = At(0, 9, 72);

        internal static GameObject Launcher()
        {
            var v = new Voxels()
                .Key('O', C(0x4a5832)).Key('o', C(0x606f42)).Key('D', C(0x2c3520)).Key('K', C(0x121311))
                .Key('k', C(0x060706)).Key('S', C(0x80838a)).Key('s', C(0x4e5158)).Key('R', C(0xc42a1e))
                .Key('Y', C(0xe8b63a)).Key('G', C(0x1c3446)).Key('T', C(0x9a8a5e)).Key('W', C(0xd8d4c4));

            const float cy = 9f;
            // The tube, open at both ends, with a rocket sitting in the front of it.
            v.Tube(0, cy, -26, 62, 6.9f, 'O', 5.3f);
            v.Tube(0, cy, 6, 56, 4.5f, 'D');
            v.Tube(0, cy, 50, 51, 4.6f, 'Y');
            v.Tube(0, cy, 57, 64, 4.5f, 'R');
            v.Tube(0, cy, 65, 68, 3.6f, 'R');
            v.Tube(0, cy, 69, 71, 2.2f, 'R');
            v.Set(0, 9, 72, 'K');
            v.Tube(0, cy, 63, 64, 4.6f, 'Y');

            // Flared ends, bands round the tube, a warning stripe and stencilled marks.
            v.Tube(0, cy, -30, -25, 8.0f, 'o', 5.4f);
            v.Tube(0, cy, 56, 62, 7.9f, 'o', 5.4f);
            foreach (int z in new[] { -10, 6, 22, 38 })
                v.Tube(0, cy, z, z + 1, 7.5f, 'D', 6.0f);
            v.Paint(-8, 8, 1, 17, 44, 45, 'Y');
            v.Paint(7, 7, 10, 11, -8, 6, 'W').Paint(-7, -7, 10, 11, -8, 6, 'W');
            v.Paint(7, 7, 7, 8, -8, 0, 'T').Paint(-7, -7, 7, 8, -8, 0, 'T');
            v.Paint(7, 7, 13, 13, -8, -2, 'k').Paint(-7, -7, 13, 13, -8, -2, 'k');

            // Trigger group under the tube, the grip, a guard, and a front handle on its mount.
            v.Box(-3, 3, 1, 2, -9, 9, 'D');
            v.Lean(-2, 2, -12, 1, -3, 3, -0.25f, 'K');
            v.Box(-1, 1, -4, -4, 3, 13, 'D').Box(-1, 1, -4, 0, 12, 13, 'D').Box(0, 0, -2, 0, 7, 8, 'S');
            v.Box(-3, 3, 1, 2, 22, 31, 'D');
            v.Lean(-2, 2, -11, 1, 24, 29, 0.18f, 'K');
            foreach (int y in new[] { -3, -6, -9 })
                v.Paint(-2, 2, y, y, 20, 40, 'k');

            // Cheek rest, and an optical sight on top.
            v.Box(-3, 3, 16, 19, -18, -6, 'K');
            v.Paint(-3, 3, 19, 19, -18, -6, 'k');
            v.Box(-2, 2, 16, 18, 4, 12, 'D');
            v.Tube(0, 21, 2, 15, 2.3f, 's');
            v.Tube(0, 21, 16, 18, 2.9f, 'D', 1.9f);
            v.Tube(0, 21, 15, 15, 2.0f, 'G');
            v.Tube(0, 21, -2, 1, 2.9f, 'D');
            v.Box(-1, 1, 24, 25, 6, 8, 's');

            // Sling loops front and back.
            v.Box(-8, -8, 4, 5, -20, -18, 'S').Box(8, 8, 4, 5, -20, -18, 'S');
            v.Box(-8, -8, 4, 5, 40, 42, 'S').Box(8, 8, 4, 5, 40, 42, 'S');
            return v.Build("Tusk-40");
        }

        // ------------------------------------------------------------ Gale-2

        internal static readonly Vector3 AirCannonMuzzle = At(0, 12, 56);

        internal static GameObject AirCannon()
        {
            var v = new Voxels()
                .Key('P', C(0x6c3aa8)).Key('p', C(0x9560d0)).Key('D', C(0x34185a)).Key('K', C(0x131215))
                .Key('k', C(0x070608)).Key('S', C(0x8a8d94)).Key('s', C(0x575a62)).Key('Y', C(0xe8c53a))
                .Key('W', C(0xe6e6e6)).Key('R', C(0xd02a24)).Key('B', C(0xc08a30)).Key('C', C(0xff80f0), glow: true);

            // The pressure tank, with end caps and bands.
            v.Tube(0, 12, -14, 30, 7.2f, 'P');
            v.Tube(0, 12, -18, -15, 5.8f, 'D');
            v.Tube(0, 12, -21, -19, 3.2f, 'D');
            foreach (int z in new[] { -8, 10, 28 })
                v.Tube(0, 12, z, z + 1, 7.8f, 's', 6.0f);
            v.Paint(-8, 8, 12, 18, 2, 3, 'p');
            for (int z = -10; z <= 26; z += 4)
                v.Set(0, 19, z, 'p');

            // The gauge on the left: a rim, a white face and a needle.
            v.Disc(-9, -8, 12, 12, 4.2f, 's');
            v.Disc(-10, -10, 12, 12, 3.4f, 'W');
            v.Set(-10, 13, 13, 'R').Set(-10, 14, 14, 'R').Set(-10, 15, 14, 'R');
            v.Set(-10, 12, 9, 'k').Set(-10, 9, 12, 'k').Set(-10, 12, 15, 'k').Set(-10, 15, 12, 'k');

            // The valve wheel on top.
            v.Box(0, 0, 19, 22, 2, 2, 'S');
            v.Post(0, 2, 23, 23, 4.2f, 'B', 3.0f);
            v.Box(-4, 4, 23, 23, 2, 2, 'B').Box(0, 0, 23, 23, -2, 6, 'B');
            v.Box(0, 0, 24, 24, 2, 2, 'S');

            // The nozzle: a short barrel opening into a smooth bell, glowing at the bottom.
            v.Tube(0, 12, 31, 40, 4.4f, 'D', 3.0f);
            for (int z = 41; z <= 56; z++)
            {
                float t = (z - 41) / 15f;
                float r = 4.6f + 4.4f * t * t + 1.2f * t;
                v.Tube(0, 12, z, z, r, z % 4 < 2 ? 'p' : 'P', r - 1.6f);
            }
            v.Tube(0, 12, 33, 33, 3.0f, 'C');
            // Hazard stripes round the collar, a relief valve and rivets along the bands.
            for (int z = 31; z <= 39; z++)
                if ((z / 2) % 2 == 0)
                    v.Paint(-5, 5, 7, 17, z, z, 'Y');
            v.Post(-3, 8, 19, 22, 1.6f, 'Y').Post(-3, 8, 23, 23, 2.2f, 's');
            foreach (int z in new[] { -8, 10, 28 })
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 4f;
                    v.Set(Mathf.RoundToInt(Mathf.Cos(ang) * 7.8f), 12 + Mathf.RoundToInt(Mathf.Sin(ang) * 7.8f), z, 'S');
                }

            // The pump grip under the barrel, then the pistol grip, housing and guard.
            v.Box(-3, 3, 2, 6, 22, 38, 'K');
            for (int z = 24; z <= 36; z += 3)
                v.Paint(-3, 3, 2, 6, z, z, 'k');
            v.Box(-1, 1, 5, 6, 8, 22, 's');
            v.Box(-4, 4, 3, 5, -9, 12, 'D');
            v.Lean(-2, 2, -12, 2, -3, 3, -0.25f, 'K');
            v.Box(-1, 1, -4, -4, 3, 13, 'D').Box(-1, 1, -4, 2, 12, 13, 'D').Box(0, 0, -2, 1, 7, 8, 'S');

            // A hose from the tank down to the grip housing.
            v.Rod(5, 6, -6, 6, 5, -2, 'K').Rod(6, 5, -2, 6, 4, 4, 'K').Rod(6, 4, 4, 5, 5, 9, 'K');
            return v.Build("Gale-2");
        }

        // ------------------------------------------------------------ Halt-1

        internal static readonly Vector3 FreezeGunMuzzle = At(0, 8, 62);

        internal static GameObject FreezeGun()
        {
            var v = new Voxels()
                .Key('W', C(0xe6f0f8)).Key('w', C(0xa8bfd0)).Key('B', C(0x3a82d8)).Key('b', C(0x1c4682))
                .Key('C', C(0x8ff4ff), glow: true).Key('c', C(0x3aa8c8), glow: true).Key('K', C(0x141a20))
                .Key('S', C(0x8a9098)).Key('s', C(0x585e66)).Key('R', C(0xe02820));

            // Body, with cooling fins at the back.
            v.Box(-3, 3, 2, 12, -10, 22, 'W');
            v.Carve(3, 3, 12, 12, -10, 22).Carve(-3, -3, 12, 12, -10, 22);
            v.Paint(-3, 3, 2, 3, -10, 22, 'w');
            v.Paint(-3, 3, 7, 7, -10, 22, 'B');
            for (int z = -10; z <= -4; z += 3)
                v.Box(-4, 4, 4, 11, z, z, 'w');

            // The coolant canister on top, glowing, in a steel frame.
            v.Tube(0, 17, -5, 16, 3.5f, 'c');
            v.Tube(0, 17, -3, 14, 2.0f, 'C');
            foreach (int z in new[] { -6, -1, 4, 9, 14, 17 })
                v.Tube(0, 17, z, z, 4.5f, 's', 3.3f);
            v.Box(-1, 1, 13, 14, -4, 15, 'S');
            v.Box(-2, 2, 13, 14, -4, -3, 'S').Box(-2, 2, 13, 14, 14, 15, 'S');

            // The barrel, wound with coils, and the crystal emitter.
            v.Tube(0, 8, 23, 50, 3.2f, 'W');
            for (int z = 25; z <= 48; z += 3)
                v.Tube(0, 8, z, z, 4.3f, 'B', 3.0f);
            v.Tube(0, 8, 51, 53, 4.5f, 's', 2.6f);
            v.Tube(0, 8, 51, 54, 3.2f, 'c');
            v.Tube(0, 8, 55, 56, 2.8f, 'C');
            v.Tube(0, 8, 57, 58, 2.0f, 'C');
            v.Tube(0, 8, 59, 60, 1.2f, 'C');
            v.Set(0, 8, 61, 'C').Set(0, 8, 62, 'C');
            v.Rod(0, 11, 55, 0, 14, 59, 'c').Rod(0, 5, 55, 0, 2, 59, 'c');
            v.Rod(3, 8, 55, 6, 8, 59, 'c').Rod(-3, 8, 55, -6, 8, 59, 'c');

            // Frost gathered under the coils.
            foreach (var (z, x) in new[] { (27, 0), (31, -1), (35, 1), (39, 0), (44, -1), (47, 1) })
                v.Rod(x, 5, z, x, 3, z, 'W');
            v.Set(0, 14, 55, 'C').Set(1, 12, 53, 'C').Set(-1, 4, 53, 'C');

            // A temperature gauge on the right.
            v.Disc(3, 4, 9, 6, 3.3f, 's');
            v.Disc(5, 5, 9, 6, 2.5f, 'W');
            v.Set(5, 9, 7, 'R').Set(5, 10, 8, 'R').Set(5, 8, 5, 'b');

            // Grip, a blue stripe, trigger and guard.
            v.Lean(-2, 2, -13, 1, -3, 3, -0.28f, 'K');
            v.Paint(-2, 2, -9, -8, -9, 4, 'B');
            v.Box(-1, 1, -4, -4, 4, 13, 'K').Box(-1, 1, -4, 1, 12, 13, 'K').Box(0, 0, -2, 0, 7, 8, 'S');
            return v.Build("Halt-1");
        }

        // ------------------------------------------------------------ the Tusk-40's rocket

        private static GameObject _rocket;

        /// <summary>The rocket the Tusk-40 fires, made once. Copy it with Instantiate.</summary>
        internal static GameObject RocketPrototype
        {
            get
            {
                if (_rocket != null)
                    return _rocket;
                var v = new Voxels()
                    .Key('D', C(0x2c3520)).Key('R', C(0xc42a1e)).Key('Y', C(0xe8b63a)).Key('K', C(0x121311))
                    .Key('S', C(0x80838a)).Key('F', C(0xffb030), glow: true).Key('f', C(0xff6a10), glow: true);
                v.Tube(0, 0, -10, 6, 3.1f, 'D');
                v.Tube(0, 0, 7, 11, 3.1f, 'R');
                v.Tube(0, 0, 12, 14, 2.4f, 'R');
                v.Tube(0, 0, 15, 16, 1.4f, 'R').Set(0, 0, 17, 'K');
                v.Tube(0, 0, 6, 6, 3.3f, 'Y');
                v.Tube(0, 0, -13, -11, 2.2f, 'S', 1.2f);
                v.Tube(0, 0, -12, -9, 1.4f, 'f').Tube(0, 0, -13, -13, 0.8f, 'F');
                foreach (var (x, y) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    for (int i = 0; i < 6; i++)
                    {
                        v.Box(x * (4 + i / 3), x * (4 + i / 3), y * (4 + i / 3), y * (4 + i / 3), -10 + i, -10 + i, 'K');
                        v.Box(x * 4, x * (4 + (5 - i) / 2), y * 4, y * (4 + (5 - i) / 2), -10 + i, -10 + i, 'K');
                    }
                }
                _rocket = v.Build("Tusk-40 rocket");
                return _rocket;
            }
        }
    }
}
