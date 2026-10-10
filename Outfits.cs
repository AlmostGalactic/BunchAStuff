using System;
using System.Collections.Generic;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using UnityEngine;

namespace BunchAStuff
{
    /// <summary>
    /// A voxel grid fitted round one body part, for making a piece of clothing on. The body fills cells 0 to X-1,
    /// 0 to Y-1 and 0 to Z-1; clothes go in the layer round it (x = -1 and X, and so on) and on top of that. x runs to
    /// the person's right, y up the part (for an arm or leg, from the hand or foot end up to the joint) and z to the
    /// front.
    /// </summary>
    internal sealed class Fit
    {
        internal readonly Voxels V;
        internal readonly int X, Y, Z;

        internal Fit(Voxels voxels, int x, int y, int z)
        {
            V = voxels;
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>The row a fraction of the way down from the top (0 is the top row).</summary>
        internal int Down(float fraction) => Mathf.Clamp(Y - 1 - Mathf.RoundToInt(fraction * Y), -1, Y - 1);

        internal int MidX => X / 2;

        /// <summary>A ring of cloth all the way round, for rows y0 to y1.</summary>
        internal Fit Wrap(int y0, int y1, char key)
        {
            V.Box(-1, X, y0, y1, -1, -1, key).Box(-1, X, y0, y1, Z, Z, key);
            V.Box(-1, -1, y0, y1, 0, Z - 1, key).Box(X, X, y0, y1, 0, Z - 1, key);
            return this;
        }

        /// <summary>Cloth across the top (or the bottom), leaving a hole in the middle if one is asked for.</summary>
        internal Fit Lid(bool top, char key, float holeX = 0f, float holeZ = 0f, float holeZShift = 0f)
        {
            int y = top ? Y : -1;
            V.Box(-1, X, y, y, -1, Z, key);
            if (holeX > 0f)
            {
                int hx = Mathf.RoundToInt(X * holeX / 2f), hz = Mathf.RoundToInt(Z * holeZ / 2f), cz = Mathf.RoundToInt(Z / 2f + holeZShift * Z);
                V.Carve(MidX - hx, MidX + hx - (X % 2 == 0 ? 1 : 0), y, y, cz - hz, cz + hz);
            }
            return this;
        }

        /// <summary>Recolours the cloth on the front (or back) face.</summary>
        internal Fit Front(int x0, int x1, int y0, int y1, char key, bool back = false)
        {
            int z = back ? -1 : Z;
            V.Paint(x0, x1, y0, y1, z, z, key);
            return this;
        }

        /// <summary>Recolours the cloth on the outer sides.</summary>
        internal Fit Sides(int y0, int y1, char key, int depth = 0)
        {
            V.Paint(-1, -1 + depth, y0, y1, -1, Z, key).Paint(X - depth, X, y0, y1, -1, Z, key);
            return this;
        }

        /// <summary>A layer standing out from the front (or back): pockets, buttons, badges, lapels.</summary>
        internal Fit Raise(int x0, int x1, int y0, int y1, char key, bool back = false)
        {
            int z = back ? -2 : Z + 1;
            V.Box(x0, x1, y0, y1, z, z, key);
            return this;
        }

        /// <summary>Stripes round the piece every few rows.</summary>
        internal Fit Bands(int y0, int y1, int every, int width, char key)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            {
                if ((y - Math.Min(y0, y1)) % every < width)
                    V.Paint(-1, X, y, y, -1, Z, key);
            }
            return this;
        }

        /// <summary>Only the body's own space is hidden, so the inside of the cloth is never drawn.</summary>
        internal Fit HideBody()
        {
            V.Hide(0, X - 1, 0, Y - 1, 0, Z - 1);
            return this;
        }
    }

    /// <summary>
    /// The kinds of people the Clothed Human Spawner makes. Each is a fixed outfit with its own colours and details
    /// (collars, buttons, pockets, ties, belts, stripes, hats), made piece by piece for each body part.
    /// </summary>
    internal static class Outfits
    {
        /// <summary>One outfit: its colours, and how each body part's piece is made. Parts it leaves out stay bare.</summary>
        internal sealed class Outfit
        {
            internal string Name;
            /// <summary>A work or special outfit, put on from the right-click menu rather than at random.</summary>
            internal bool Job;
            internal readonly Dictionary<char, Color> Colours = new();
            internal readonly Dictionary<HumanoidNodeTagValue, Action<Fit>> Pieces = new();
            /// <summary>A hat, made on the head's fit; it sits on top and falls off a badly hurt head.</summary>
            internal Action<Fit> Hat;

            internal Outfit Colour(char key, int rgb)
            {
                Colours[key] = Voxels.Hex(rgb);
                return this;
            }

            internal Outfit Piece(Action<Fit> make, params HumanoidNodeTagValue[] parts)
            {
                foreach (var part in parts)
                    Pieces[part] = make;
                return this;
            }
        }

        /// <summary>Blood on torn cloth: fresh and dark.</summary>
        internal const char Blood = '1', DarkBlood = '2';

        private const HumanoidNodeTagValue Spine = HumanoidNodeTagValue.Spine, Pelvis = HumanoidNodeTagValue.Pelvis;
        private static readonly HumanoidNodeTagValue[] UpperArms = { HumanoidNodeTagValue.LeftArm, HumanoidNodeTagValue.RightArm };
        private static readonly HumanoidNodeTagValue[] Forearms = { HumanoidNodeTagValue.LeftForearm, HumanoidNodeTagValue.RightForearm };
        private static readonly HumanoidNodeTagValue[] Thighs = { HumanoidNodeTagValue.LeftLeg, HumanoidNodeTagValue.RightLeg };
        private static readonly HumanoidNodeTagValue[] Shins = { HumanoidNodeTagValue.LeftKnee, HumanoidNodeTagValue.RightKnee };
        private static readonly HumanoidNodeTagValue[] Feet = { HumanoidNodeTagValue.LeftFoot, HumanoidNodeTagValue.RightFoot };

        // Everyday clothes first; people from the spawner get one of these.
        private static readonly Func<Outfit>[] Everyday =
        {
            TeeAndJeans, Hoodie, Flannel, PoloAndChinos, Sweater, TankTop, Tracksuit, SummerShirt,
        };
        private static readonly Func<Outfit>[] Jobs =
        {
            OfficeWorker, Businessman, Builder, Doctor, PoliceOfficer, Chef, Footballer, Farmer, Prisoner,
        };
        private static readonly Func<Outfit>[] All = Combine(Everyday, Jobs);

        internal static string[] Names => Array.ConvertAll(All, make => make().Name);
        internal static string[] JobNames => Array.ConvertAll(Jobs, make => make().Name);
        internal static string[] EverydayNames => Array.ConvertAll(Everyday, make => make().Name);

        /// <summary>A random set of everyday clothes.</summary>
        internal static Outfit Any() => Everyday[UnityEngine.Random.Range(0, Everyday.Length)]();

        private static Func<Outfit>[] Combine(Func<Outfit>[] a, Func<Outfit>[] b)
        {
            var all = new Func<Outfit>[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }

        internal static Outfit Named(string name)
        {
            foreach (var make in All)
            {
                var outfit = make();
                if (string.Equals(outfit.Name, name, StringComparison.OrdinalIgnoreCase))
                    return outfit;
            }
            return null;
        }

        private static Outfit Work(string name)
        {
            var outfit = New(name);
            outfit.Job = true;
            return outfit;
        }

        private static Outfit New(string name) => new Outfit { Name = name }.Colour(Blood, 0x7A1414).Colour(DarkBlood, 0x420A0B);

        // ------------------------------------------------------------ the outfits

        // White shirt with a collar, buttons and a breast pocket, a red tie, grey trousers with a black belt, black shoes.
        private static Outfit OfficeWorker() => Work("Office Worker")
            .Colour('s', 0xE8E8E4).Colour('S', 0xCFCFCB).Colour('t', 0x9E1B22).Colour('T', 0x6E1016)
            .Colour('p', 0x55585E).Colour('k', 0x1A1A1C).Colour('m', 0x9A9A9E).Colour('o', 0x141414).Colour('O', 0x2B2B2B)
            .Piece(f => { Shirt(f, 's', 'S'); Buttons(f, 'S'); Pocket(f, 'S', left: true); Tie(f, 't', 'T'); }, Spine)
            .Piece(f => Sleeve(f, 's', 1f, 'S'), UpperArms)
            .Piece(f => Sleeve(f, 's', 0.9f, 'S'), Forearms)
            .Piece(f => Trousers(f, 'p', 'k', 'm'), Pelvis)
            .Piece(f => Leg(f, 'p', 1f), Thighs)
            .Piece(f => Leg(f, 'p', 0.9f), Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O'), Feet);

        // Navy suit jacket with lapels, open over a white shirt and blue tie, matching trousers, black shoes.
        private static Outfit Businessman() => Work("Businessman")
            .Colour('j', 0x1E2A44).Colour('J', 0x141C30).Colour('s', 0xEDEDEA).Colour('t', 0x3C6CB4).Colour('T', 0x28497E)
            .Colour('o', 0x101010).Colour('O', 0x262626).Colour('b', 0xB8B8BC)
            .Piece(f =>
            {
                Shirt(f, 'j', 'J');
                // The opening: a V of shirt and tie down to the lowest button, with lapels either side.
                int bottom = f.Down(0.62f);
                for (int y = bottom; y < f.Y; y++)
                {
                    float t = (y - bottom) / (float)Math.Max(1, f.Y - 1 - bottom);
                    int half = Mathf.RoundToInt(Mathf.Lerp(1f, f.X * 0.26f, t));
                    f.Front(f.MidX - half, f.MidX + half - 1, y, y, 's');
                    f.Raise(f.MidX - half - 2, f.MidX - half - 1, y, y, 'J').Raise(f.MidX + half, f.MidX + half + 1, y, y, 'J');
                }
                Tie(f, 't', 'T', 0.6f);
                f.Raise(f.MidX - 1, f.MidX, bottom - 2, bottom - 2, 'b').Raise(f.MidX - 1, f.MidX, bottom - 6, bottom - 6, 'b');
                Pocket(f, 'J', left: true, flapOnly: true);
            }, Spine)
            .Piece(f => Sleeve(f, 'j', 1f, 'J'), UpperArms)
            .Piece(f => { Sleeve(f, 'j', 0.88f, 'J'); f.Wrap(f.Down(0.93f), f.Down(0.89f), 's'); }, Forearms)
            .Piece(f => Trousers(f, 'j', 'J', 'b'), Pelvis)
            .Piece(f => Leg(f, 'j', 1f), Thighs)
            .Piece(f => Leg(f, 'j', 0.9f), Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O'), Feet);

        // Grey T-shirt under an orange high-visibility vest with silver stripes, jeans, brown work boots, a yellow hard hat.
        private static Outfit Builder() => Work("Builder")
            .Colour('g', 0x6E7075).Colour('v', 0xF26A10).Colour('V', 0xC9520A).Colour('r', 0xD8DCE0)
            .Colour('d', 0x2E3F63).Colour('D', 0x22304C).Colour('k', 0x3A2A1C).Colour('w', 0x5C3B20).Colour('W', 0x3B2614)
            .Colour('h', 0xF2C21A).Colour('H', 0xC99A0E)
            .Piece(f =>
            {
                Shirt(f, 'v', 'V');
                // The T-shirt shows at the shoulders and down the open front.
                f.V.Paint(-1, f.X, f.Y, f.Y, -1, f.Z, 'g');
                f.Front(f.MidX - 1, f.MidX, 0, f.Y - 1, 'g');
                f.Bands(f.Down(0.75f), f.Down(0.7f), 99, 2, 'r').Bands(f.Down(0.42f), f.Down(0.37f), 99, 2, 'r');
                f.Front(f.MidX - 1, f.MidX, f.Down(0.75f), f.Down(0.7f), 'g').Front(f.MidX - 1, f.MidX, f.Down(0.42f), f.Down(0.37f), 'g');
            }, Spine)
            .Piece(f => Sleeve(f, 'g', 0.45f, 'g'), UpperArms)
            .Piece(f => Trousers(f, 'd', 'k', 'r'), Pelvis)
            .Piece(f => Leg(f, 'd', 1f), Thighs)
            .Piece(f => { Leg(f, 'd', 0.75f); f.Wrap(f.Down(0.95f), f.Down(0.75f), 'w'); }, Shins)
            .Piece(f => Shoe(f, 'w', 'W', 'W', boot: true), Feet)
            .WithHat(f => HardHat(f, 'h', 'H'));

        // A long white coat with pockets and buttons over light blue scrubs, white shoes.
        private static Outfit Doctor() => Work("Doctor")
            .Colour('c', 0xF2F2EF).Colour('C', 0xD6D6D2).Colour('u', 0x6FA7C9).Colour('U', 0x5089AB).Colour('o', 0xE6E6E3).Colour('O', 0xB9B9B5)
            .Colour('n', 0x2A6BB0)
            .Piece(f =>
            {
                Shirt(f, 'c', 'C');
                for (int y = f.Down(0.3f); y < f.Y; y++)
                {
                    float t = (y - f.Down(0.3f)) / (float)Math.Max(1, f.Y - 1 - f.Down(0.3f));
                    int half = Mathf.RoundToInt(Mathf.Lerp(2f, f.X * 0.2f, t));
                    f.Front(f.MidX - half, f.MidX + half - 1, y, y, 'u');
                    f.Raise(f.MidX - half - 2, f.MidX - half - 1, y, y, 'C').Raise(f.MidX + half, f.MidX + half + 1, y, y, 'C');
                }
                f.Front(f.MidX - 2, f.MidX + 1, 0, f.Down(0.3f) - 1, 'C');
                Pocket(f, 'c', left: true);
                f.Raise(f.MidX - Mathf.RoundToInt(f.X * 0.33f), f.MidX - Mathf.RoundToInt(f.X * 0.33f), f.Down(0.32f), f.Down(0.22f), 'n');
            }, Spine)
            .Piece(f => Sleeve(f, 'c', 1f, 'C'), UpperArms)
            .Piece(f => Sleeve(f, 'c', 0.88f, 'C'), Forearms)
            .Piece(f => { Trousers(f, 'c', 'C', 'C'); f.Front(f.MidX - 2, f.MidX + 1, 0, f.Y - 1, 'u'); PocketsLow(f, 'C'); }, Pelvis)
            .Piece(f => { Leg(f, 'u', 1f); f.Wrap(f.Down(0.55f), f.Y - 1, 'c'); f.V.Paint(-1, f.X, f.Down(0.55f), f.Y - 1, f.Z, f.Z, 'u'); }, Thighs)
            .Piece(f => Leg(f, 'u', 0.9f), Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O'), Feet);

        // A navy uniform shirt with a gold badge and shoulder patches, a duty belt, navy trousers, black boots and a peaked cap.
        private static Outfit PoliceOfficer() => Work("Police Officer")
            .Colour('n', 0x1C2747).Colour('N', 0x121A31).Colour('g', 0xD9A934).Colour('k', 0x111112).Colour('m', 0xA8A8AC)
            .Colour('p', 0x6D86B5).Colour('o', 0x0E0E0E).Colour('O', 0x232323)
            .Piece(f =>
            {
                Shirt(f, 'n', 'N');
                Buttons(f, 'N');
                Pocket(f, 'N', left: true);
                Pocket(f, 'N', left: false);
                int x = f.MidX - Mathf.RoundToInt(f.X * 0.3f);
                f.Raise(x - 1, x + 1, f.Down(0.2f), f.Down(0.12f), 'g').Raise(x, x, f.Down(0.24f), f.Down(0.24f), 'g');
            }, Spine)
            .Piece(f => { Sleeve(f, 'n', 0.5f, 'N'); f.V.Paint(-1, -1, f.Down(0.3f), f.Down(0.12f), 2, f.Z - 3, 'p').Paint(f.X, f.X, f.Down(0.3f), f.Down(0.12f), 2, f.Z - 3, 'p'); }, UpperArms)
            .Piece(f =>
            {
                Trousers(f, 'n', 'k', 'm');
                f.V.Box(-2, -2, f.Y - 5, f.Y - 2, 2, f.Z - 3, 'k').Box(f.X + 1, f.X + 1, f.Y - 5, f.Y - 2, 2, f.Z - 3, 'k');
            }, Pelvis)
            .Piece(f => Leg(f, 'n', 1f), Thighs)
            .Piece(f => Leg(f, 'n', 0.9f), Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O', boot: true), Feet)
            .WithHat(f => PeakedCap(f, 'n', 'k', 'g'));

        // A white double-breasted jacket, checked trousers, black shoes and a tall white hat.
        private static Outfit Chef() => Work("Chef")
            .Colour('c', 0xF4F4F1).Colour('C', 0xD9D9D5).Colour('b', 0x2B2B2D).Colour('w', 0xE4E4E0).Colour('o', 0x121212).Colour('O', 0x2A2A2A)
            .Piece(f =>
            {
                Shirt(f, 'c', 'C', collar: true);
                for (int y = f.Down(0.15f); y >= f.Down(0.85f); y -= 4)
                {
                    int a = f.MidX - Mathf.RoundToInt(f.X * 0.18f), b = f.MidX + Mathf.RoundToInt(f.X * 0.18f) - 1;
                    f.Raise(a, a, y, y, 'b').Raise(b, b, y, y, 'b');
                }
            }, Spine)
            .Piece(f => Sleeve(f, 'c', 1f, 'C'), UpperArms)
            .Piece(f => { Sleeve(f, 'c', 0.6f, 'C'); f.Wrap(f.Down(0.6f), f.Down(0.52f), 'C'); }, Forearms)
            .Piece(f => { Trousers(f, 'b', 'b', 'b'); Checks(f, 'b', 'w', 0, f.Y - 1); }, Pelvis)
            .Piece(f => { Leg(f, 'b', 1f); Checks(f, 'b', 'w', 0, f.Y - 1); }, Thighs)
            .Piece(f => { Leg(f, 'b', 0.9f); Checks(f, 'b', 'w', f.Down(0.9f), f.Y - 1); }, Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O'), Feet)
            .WithHat(f => ChefHat(f, 'c', 'C'));

        // A red football shirt with white trim and a number on the back, white shorts, red socks, black boots.
        private static Outfit Footballer() => Work("Footballer")
            .Colour('r', 0xC4232B).Colour('R', 0x951A20).Colour('w', 0xF0F0EE).Colour('W', 0xCFCFCB).Colour('o', 0x141414).Colour('O', 0xE8E8E6)
            .Piece(f =>
            {
                Shirt(f, 'r', 'w');
                f.Sides(0, f.Y - 1, 'w');
                Number(f, 'w', back: true);
                f.Raise(f.MidX - Mathf.RoundToInt(f.X * 0.3f) - 1, f.MidX - Mathf.RoundToInt(f.X * 0.3f) + 1, f.Down(0.22f), f.Down(0.14f), 'w');
            }, Spine)
            .Piece(f => { Sleeve(f, 'r', 0.5f, 'w'); }, UpperArms)
            .Piece(f => { Trousers(f, 'w', 'w', 'w'); f.Sides(0, f.Y - 1, 'r'); }, Pelvis)
            .Piece(f => { Leg(f, 'w', 0.45f, 'W'); f.Sides(f.Down(0.45f), f.Y - 1, 'r'); }, Thighs)
            .Piece(f => { Leg(f, 'r', 0.9f); f.Wrap(f.Down(0.15f), f.Down(0.05f), 'w'); f.Wrap(f.Down(0.9f), f.Down(0.6f), 'w'); }, Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O', studs: true), Feet);

        // Denim dungarees with a bib and straps over a red checked shirt, brown boots and a straw hat.
        private static Outfit Farmer() => Work("Farmer")
            .Colour('r', 0xA8262A).Colour('R', 0x5E1518).Colour('d', 0x355089).Colour('D', 0x253A66).Colour('y', 0xD8B03A)
            .Colour('w', 0x5C3B20).Colour('W', 0x3B2614).Colour('h', 0xD9BE7A).Colour('H', 0x7B4A2A)
            .Piece(f =>
            {
                Shirt(f, 'r', 'R');
                Checks(f, 'r', 'R', 0, f.Y - 1);
                int half = Mathf.RoundToInt(f.X * 0.32f), top = f.Down(0.32f);
                f.Front(f.MidX - half, f.MidX + half - 1, 0, top, 'd');
                f.Raise(f.MidX - half + 3, f.MidX + half - 4, top - 6, top - 1, 'D');
                f.Raise(f.MidX - half, f.MidX - half, top, top, 'y').Raise(f.MidX + half - 1, f.MidX + half - 1, top, top, 'y');
                f.Wrap(0, f.Down(0.8f), 'd');
                for (int y = top + 1; y <= f.Y; y++)
                {
                    f.V.Paint(f.MidX - half, f.MidX - half + 2, y, y, f.Z, f.Z, 'd').Paint(f.MidX + half - 3, f.MidX + half - 1, y, y, f.Z, f.Z, 'd');
                    f.V.Paint(f.MidX - half, f.MidX - half + 2, y, y, -1, -1, 'd').Paint(f.MidX + half - 3, f.MidX + half - 1, y, y, -1, -1, 'd');
                }
                f.V.Paint(f.MidX - half, f.MidX - half + 2, f.Y, f.Y, -1, f.Z, 'd').Paint(f.MidX + half - 3, f.MidX + half - 1, f.Y, f.Y, -1, f.Z, 'd');
            }, Spine)
            .Piece(f => { Sleeve(f, 'r', 1f, 'R'); Checks(f, 'r', 'R', 0, f.Y - 1); }, UpperArms)
            .Piece(f => { Sleeve(f, 'r', 0.45f, 'R'); Checks(f, 'r', 'R', f.Down(0.45f), f.Y - 1); f.Wrap(f.Down(0.45f), f.Down(0.38f), 'r'); }, Forearms)
            .Piece(f => { Trousers(f, 'd', 'd', 'd'); PocketsLow(f, 'D'); }, Pelvis)
            .Piece(f => Leg(f, 'd', 1f), Thighs)
            .Piece(f => { Leg(f, 'd', 0.8f); f.Wrap(f.Down(0.8f), f.Down(0.74f), 'D'); }, Shins)
            .Piece(f => Shoe(f, 'w', 'W', 'W', boot: true), Feet)
            .WithHat(f => StrawHat(f, 'h', 'H'));

        // A teal shirt printed with flowers, khaki shorts, sandals and a cap.
        private static Outfit SummerShirt() => New("Summer Shirt")
            .Colour('t', 0x1E9AA0).Colour('T', 0x157579).Colour('f', 0xF4E04D).Colour('F', 0xF07FA8).Colour('l', 0x3FBF6A)
            .Colour('k', 0xC9B07E).Colour('K', 0xA38D5F).Colour('s', 0x6B4425).Colour('S', 0x4A2E18).Colour('c', 0xE8E8E4).Colour('C', 0x2E6FB8)
            .Piece(f => { Shirt(f, 't', 'T', collar: true); Flowers(f); Buttons(f, 'T'); }, Spine)
            .Piece(f => { Sleeve(f, 't', 0.55f, 'T'); Flowers(f); }, UpperArms)
            .Piece(f => { Trousers(f, 'k', 'K', 'K'); PocketsLow(f, 'K'); }, Pelvis)
            .Piece(f => Leg(f, 'k', 0.55f, 'K'), Thighs)
            .Piece(f => Sandal(f, 's', 'S'), Feet)
            .WithHat(f => Cap(f, 'c', 'C'));

        // A grey hoodie with the hood down the back, a front pocket and drawstrings, black joggers and white trainers.
        private static Outfit Hoodie() => New("Hoodie")
            .Colour('h', 0x8C8F95).Colour('H', 0x6E7177).Colour('w', 0xEDEDEA).Colour('j', 0x1C1C1F).Colour('J', 0x2E2E33)
            .Colour('o', 0xF0F0EE).Colour('O', 0xC8C8C4).Colour('r', 0xB02A2A)
            .Piece(f =>
            {
                Shirt(f, 'h', 'H', collar: false);
                f.Wrap(0, 1, 'H');
                int half = Mathf.RoundToInt(f.X * 0.28f);
                f.Raise(f.MidX - half, f.MidX + half - 1, f.Down(0.85f), f.Down(0.55f), 'H');
                f.Raise(f.MidX - 3, f.MidX - 3, f.Down(0.3f), f.Down(0.04f), 'w').Raise(f.MidX + 2, f.MidX + 2, f.Down(0.3f), f.Down(0.04f), 'w');
                // The hood, lying down the back from the neck.
                int hood = Mathf.RoundToInt(f.X * 0.3f);
                f.V.Box(f.MidX - hood, f.MidX + hood - 1, f.Down(0.3f), f.Y, -2, -2, 'H');
                f.V.Box(f.MidX - hood, f.MidX + hood - 1, f.Y + 1, f.Y + 1, -2, 1, 'H');
            }, Spine)
            .Piece(f => Sleeve(f, 'h', 1f, 'H'), UpperArms)
            .Piece(f => { Sleeve(f, 'h', 0.93f, 'H'); f.Wrap(f.Down(0.93f), f.Down(0.86f), 'H'); }, Forearms)
            .Piece(f => { Trousers(f, 'j', 'J', 'J'); f.Sides(0, f.Y - 1, 'J'); }, Pelvis)
            .Piece(f => { Leg(f, 'j', 1f); f.Sides(0, f.Y - 1, 'J'); }, Thighs)
            .Piece(f => { Leg(f, 'j', 0.9f); f.Sides(f.Down(0.9f), f.Y - 1, 'J'); f.Wrap(f.Down(0.9f), f.Down(0.83f), 'J'); }, Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'r'), Feet);

        // An orange jumpsuit with a number on the back, white trainers.
        private static Outfit Prisoner() => Work("Prisoner")
            .Colour('o', 0xF07A1A).Colour('O', 0xC45F0F).Colour('w', 0xF0F0EE).Colour('k', 0x1C1C1C).Colour('s', 0xEDEDEA).Colour('S', 0xBEBEBA)
            .Piece(f => { Shirt(f, 'o', 'O', collar: true); Buttons(f, 'O'); Number(f, 'k', back: true, patch: 'w'); }, Spine)
            .Piece(f => Sleeve(f, 'o', 0.55f, 'O'), UpperArms)
            .Piece(f => Trousers(f, 'o', 'o', 'o'), Pelvis)
            .Piece(f => Leg(f, 'o', 1f), Thighs)
            .Piece(f => Leg(f, 'o', 0.9f), Shins)
            .Piece(f => Shoe(f, 's', 'S', 'S'), Feet);

        private static Outfit WithHat(this Outfit outfit, Action<Fit> make)
        {
            outfit.Hat = make;
            return outfit;
        }

        // A white T-shirt with a red stripe across the chest, blue jeans with a brown belt, white trainers.
        private static Outfit TeeAndJeans() => New("T-Shirt and Jeans")
            .Colour('w', 0xEDEDEA).Colour('W', 0xCDCDC9).Colour('r', 0xC0392B).Colour('d', 0x34508A).Colour('D', 0x263C68)
            .Colour('b', 0x5A3A22).Colour('m', 0xC9A84A).Colour('o', 0xF2F2F0).Colour('O', 0xBFBFBB).Colour('g', 0x8A8A8E)
            .Piece(f => { Shirt(f, 'w', 'W', collar: false); f.Bands(f.Down(0.4f), f.Down(0.3f), 99, 3, 'r'); }, Spine)
            .Piece(f => Sleeve(f, 'w', 0.5f, 'W'), UpperArms)
            .Piece(f => { Trousers(f, 'd', 'b', 'm'); PocketsLow(f, 'D'); }, Pelvis)
            .Piece(f => { Leg(f, 'd', 1f); f.Sides(-2, f.Y + 2, 'D'); }, Thighs)
            .Piece(f => { Leg(f, 'd', 0.9f); f.Sides(f.Down(0.9f), f.Y + 2, 'D'); }, Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'g'), Feet);

        // A green checked flannel shirt worn open over a black T-shirt, jeans and brown boots.
        private static Outfit Flannel() => New("Flannel")
            .Colour('g', 0x2F6B45).Colour('G', 0x173826).Colour('k', 0x1E1E20).Colour('d', 0x3B5A93).Colour('D', 0x2A4270)
            .Colour('b', 0x2A1C12).Colour('m', 0xB0B0B4).Colour('w', 0x6A4428).Colour('W', 0x3E2816)
            .Piece(f =>
            {
                Shirt(f, 'g', 'G');
                Checks(f, 'g', 'G', -2, f.Y);
                int half = Mathf.RoundToInt(f.X * 0.16f);
                f.Front(f.MidX - half, f.MidX + half - 1, -2, f.Y - 1, 'k');
                f.Raise(f.MidX - half - 1, f.MidX - half - 1, -2, f.Y - 1, 'G').Raise(f.MidX + half, f.MidX + half, -2, f.Y - 1, 'G');
            }, Spine)
            .Piece(f => { Sleeve(f, 'g', 1f, 'G'); Checks(f, 'g', 'G', -2, f.Y + 1); }, UpperArms)
            .Piece(f => { Sleeve(f, 'g', 0.55f, 'G'); Checks(f, 'g', 'G', f.Down(0.55f), f.Y + 1); f.Wrap(f.Down(0.55f), f.Down(0.47f), 'g'); }, Forearms)
            .Piece(f => { Trousers(f, 'd', 'b', 'm'); PocketsLow(f, 'D'); }, Pelvis)
            .Piece(f => Leg(f, 'd', 1f), Thighs)
            .Piece(f => { Leg(f, 'd', 0.75f); f.Wrap(f.Down(0.95f), f.Down(0.75f), 'w'); }, Shins)
            .Piece(f => Shoe(f, 'w', 'W', 'W', boot: true), Feet);

        // A navy polo shirt with a white-tipped collar and two buttons, khaki chinos, brown shoes.
        private static Outfit PoloAndChinos() => New("Polo and Chinos")
            .Colour('n', 0x1F3157).Colour('N', 0x15233F).Colour('w', 0xEDEDEA).Colour('k', 0xC2A878).Colour('K', 0x9C845A)
            .Colour('b', 0x3A2616).Colour('m', 0xC9A84A).Colour('o', 0x5A3820).Colour('O', 0x2E1C10)
            .Piece(f =>
            {
                Shirt(f, 'n', 'w');
                f.Front(f.MidX, f.MidX, f.Down(0.3f), f.Y - 1, 'N');
                f.Raise(f.MidX, f.MidX, f.Down(0.12f), f.Down(0.12f), 'w').Raise(f.MidX, f.MidX, f.Down(0.25f), f.Down(0.25f), 'w');
                f.Wrap(-2, -1, 'N');
            }, Spine)
            .Piece(f => Sleeve(f, 'n', 0.5f, 'w'), UpperArms)
            .Piece(f => { Trousers(f, 'k', 'b', 'm'); PocketsLow(f, 'K'); }, Pelvis)
            .Piece(f => { Leg(f, 'k', 1f); f.Front(f.MidX, f.MidX, -2, f.Y + 2, 'K'); }, Thighs)
            .Piece(f => { Leg(f, 'k', 0.9f); f.Front(f.MidX, f.MidX, f.Down(0.9f), f.Y + 2, 'K'); }, Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O'), Feet);

        // A maroon knitted jumper with a pale band of diamonds across the chest, grey trousers, black shoes.
        private static Outfit Sweater() => New("Sweater")
            .Colour('m', 0x7A2433).Colour('M', 0x5C1A26).Colour('c', 0xE8DCC2).Colour('g', 0x4E5157).Colour('G', 0x3A3C41)
            .Colour('k', 0x1A1A1C).Colour('o', 0x121212).Colour('O', 0x2A2A2A)
            .Piece(f =>
            {
                Shirt(f, 'm', 'M', collar: false);
                f.Wrap(-2, 0, 'M');
                int a = f.Down(0.42f), b = f.Down(0.26f), mid = (a + b) / 2;
                f.Bands(a, b, 99, b - a + 1, 'c');
                foreach (var (x, y, z) in new List<(int, int, int)>(f.V.Cells))
                {
                    if (y <= a || y >= b || f.V.At(x, y, z) != 'c')
                        continue;
                    int across = x <= -1 || x >= f.X ? z : x;
                    if (Math.Abs(((across + 40) % 6) - 3) == Math.Abs(y - mid))
                        f.V.Paint(x, x, y, y, z, z, 'm');
                }
            }, Spine)
            .Piece(f => Sleeve(f, 'm', 1f, 'M'), UpperArms)
            .Piece(f => { Sleeve(f, 'm', 0.93f, 'M'); f.Wrap(f.Down(0.93f), f.Down(0.86f), 'M'); }, Forearms)
            .Piece(f => Trousers(f, 'g', 'k', 'G'), Pelvis)
            .Piece(f => Leg(f, 'g', 1f), Thighs)
            .Piece(f => Leg(f, 'g', 0.9f), Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'O'), Feet);

        // A yellow vest top, denim shorts and sandals.
        private static Outfit TankTop() => New("Tank Top and Shorts")
            .Colour('y', 0xE8C23A).Colour('Y', 0xC29E22).Colour('d', 0x5B7DB8).Colour('D', 0x8FA9D6).Colour('b', 0x3A2616)
            .Colour('m', 0xB0B0B4).Colour('s', 0x7A5030).Colour('S', 0x4A3020)
            .Piece(f =>
            {
                Shirt(f, 'y', 'Y', collar: false);
                // Bare shoulders: only straps go over the top.
                int strap = Math.Max(2, Mathf.RoundToInt(f.X * 0.1f)), at = Mathf.RoundToInt(f.X * 0.28f);
                f.V.Carve(-1, f.MidX - at - strap, f.Down(0.12f), f.Y + 1, -1, f.Z).Carve(f.MidX + at + strap - 1, f.X, f.Down(0.12f), f.Y + 1, -1, f.Z);
                f.V.Carve(f.MidX - at + 1, f.MidX + at - 2, f.Down(0.1f), f.Y + 1, f.Z, f.Z + 1);
            }, Spine)
            .Piece(f => { Trousers(f, 'd', 'b', 'm'); PocketsLow(f, 'D'); }, Pelvis)
            .Piece(f => { Leg(f, 'd', 0.35f); f.Wrap(f.Down(0.35f), f.Down(0.35f), 'D'); }, Thighs)
            .Piece(f => Sandal(f, 's', 'S'), Feet);

        // A blue tracksuit, zipped up, with white stripes down the arms and legs, and white trainers.
        private static Outfit Tracksuit() => New("Tracksuit")
            .Colour('t', 0x1F4FA8).Colour('T', 0x163A7C).Colour('w', 0xF0F0EE).Colour('z', 0xB8B8BC).Colour('o', 0xF2F2F0).Colour('O', 0xBFBFBB)
            .Colour('k', 0x1C1C1E)
            .Piece(f =>
            {
                Shirt(f, 't', 'T');
                f.Wrap(-2, -1, 'T');
                f.Raise(f.MidX, f.MidX, -2, f.Y - 1, 'z');
                f.Sides(-2, f.Y - 1, 'w');
            }, Spine)
            .Piece(f => { Sleeve(f, 't', 1f, 'T'); f.Sides(-2, f.Y + 1, 'w'); }, UpperArms)
            .Piece(f => { Sleeve(f, 't', 0.93f, 'T'); f.Sides(f.Down(0.93f), f.Y + 1, 'w'); f.Wrap(f.Down(0.93f), f.Down(0.87f), 'T'); }, Forearms)
            .Piece(f => { Trousers(f, 't', 't', 't'); f.Sides(0, f.Y - 1, 'w'); }, Pelvis)
            .Piece(f => { Leg(f, 't', 1f); f.Sides(-2, f.Y + 2, 'w'); }, Thighs)
            .Piece(f => { Leg(f, 't', 0.9f); f.Sides(f.Down(0.9f), f.Y + 2, 'w'); f.Wrap(f.Down(0.9f), f.Down(0.84f), 'T'); }, Shins)
            .Piece(f => Shoe(f, 'o', 'O', 'k'), Feet);

        // ------------------------------------------------------------ garments

        // The body of a shirt or jacket: all round, over the shoulders with a hole for the neck, and a collar.
        private static void Shirt(Fit f, char cloth, char trim, bool collar = true)
        {
            // Down past the waist, so no skin shows between it and the trousers.
            f.Wrap(-2, f.Y - 1, cloth).Lid(true, cloth, 0.36f, 0.6f).HideBody();
            // Closed underneath, so bending over never shows the body's bare underside.
            f.V.Box(-1, f.X, -3, -3, -1, f.Z, cloth);
            int hx = Mathf.RoundToInt(f.X * 0.18f), hz = Mathf.RoundToInt(f.Z * 0.3f), cz = f.Z / 2;
            if (!collar)
            {
                f.V.Box(f.MidX - hx - 1, f.MidX + hx, f.Y, f.Y, cz - hz - 1, cz + hz + 1, trim).Carve(f.MidX - hx, f.MidX + hx - 1, f.Y, f.Y, cz - hz, cz + hz);
                return;
            }
            // A collar standing up round the back and sides of the neck, folding down at the front.
            f.V.Box(f.MidX - hx - 1, f.MidX + hx, f.Y + 1, f.Y + 1, cz - hz - 1, cz - hz - 1, trim);
            f.V.Box(f.MidX - hx - 1, f.MidX - hx - 1, f.Y + 1, f.Y + 1, cz - hz - 1, cz + hz, trim).Box(f.MidX + hx, f.MidX + hx, f.Y + 1, f.Y + 1, cz - hz - 1, cz + hz, trim);
            f.V.Box(f.MidX - hx - 1, f.MidX - 2, f.Y - 1, f.Y, f.Z + 1, f.Z + 1, trim).Box(f.MidX + 1, f.MidX + hx, f.Y - 1, f.Y, f.Z + 1, f.Z + 1, trim);
        }

        private static void Buttons(Fit f, char key)
        {
            f.Front(f.MidX, f.MidX, 0, f.Y - 1, key);
            for (int y = f.Y - 4; y >= 1; y -= 4)
                f.Raise(f.MidX, f.MidX, y, y, key);
        }

        private static void Pocket(Fit f, char key, bool left, bool flapOnly = false)
        {
            int w = Math.Max(3, Mathf.RoundToInt(f.X * 0.2f));
            int x0 = left ? f.MidX - Mathf.RoundToInt(f.X * 0.38f) : f.MidX + Mathf.RoundToInt(f.X * 0.38f) - w;
            int top = f.Down(0.25f);
            if (!flapOnly)
                f.Raise(x0, x0 + w - 1, top - 4, top, key);
            else
                f.Raise(x0, x0 + w - 1, top, top, key);
        }

        private static void PocketsLow(Fit f, char key)
        {
            int w = Math.Max(3, Mathf.RoundToInt(f.X * 0.22f));
            f.Raise(1, w, f.Down(0.55f), f.Down(0.25f), key).Raise(f.X - 1 - w, f.X - 2, f.Down(0.55f), f.Down(0.25f), key);
        }

        // A tie from the collar down to its point.
        private static void Tie(Fit f, char tie, char knot, float length = 0.85f)
        {
            int top = f.Y - 1, bottom = f.Down(length);
            f.Raise(f.MidX - 1, f.MidX, top - 1, top, knot);
            f.V.Box(f.MidX - 1, f.MidX, f.Y, f.Y, f.Z - 1, f.Z, knot);
            for (int y = top - 2; y >= bottom; y--)
            {
                int half = y <= bottom + 1 ? 0 : y > top - 5 ? 1 : 2;
                f.Raise(f.MidX - 1 - half + (half > 0 ? 1 : 0), f.MidX + half - (half > 0 ? 1 : 0), y, y, tie);
            }
        }

        // A sleeve from the shoulder down, with a band at its end.
        private static void Sleeve(Fit f, char cloth, float length, char cuff)
        {
            // Up past the joint, so a bent elbow or shoulder never opens a gap.
            int end = f.Down(length);
            f.Wrap(length >= 1f ? -2 : Math.Max(0, end), f.Y + 1, cloth).HideBody();
            // Capped over the top of the shoulder or elbow, and across the end of a sleeve that carries on.
            f.V.Box(-1, f.X, f.Y + 2, f.Y + 2, -1, f.Z, cloth);
            if (length >= 1f)
                f.V.Box(-1, f.X, -3, -3, -1, f.Z, cloth);
            if (f.Y - 1 - end > 2 && length < 1f)
                f.Wrap(Math.Max(0, end), Math.Max(0, end), cuff);
        }

        // Trousers' top: all round the hips, closed underneath, with a belt and buckle.
        private static void Trousers(Fit f, char cloth, char belt, char buckle)
        {
            f.Wrap(0, f.Y - 1, cloth).Lid(false, cloth).Lid(true, cloth).HideBody();
            f.Wrap(f.Y - 2, f.Y - 1, belt);
            f.Raise(f.MidX - 1, f.MidX, f.Y - 2, f.Y - 1, buckle);
            for (int x = 2; x < f.X - 2; x += Math.Max(3, f.X / 5))
                f.V.Paint(x, x, f.Y - 3, f.Y - 1, f.Z, f.Z, belt == cloth ? cloth : belt);
        }

        // A trouser leg from the top down to a fraction of the way.
        private static void Leg(Fit f, char cloth, float length, char hem = (char)0)
        {
            int end = f.Down(length);
            f.Wrap(length >= 1f ? -2 : Math.Max(0, end), f.Y + 2, cloth).HideBody();
            f.V.Box(-1, f.X, f.Y + 3, f.Y + 3, -1, f.Z, cloth);
            if (length >= 1f)
                f.V.Box(-1, f.X, -3, -3, -1, f.Z, cloth);
            if (hem != (char)0 && end >= 0)
                f.Wrap(end, end, hem);
        }

        // A shoe: a thick sole, a toe cap, the instep closed with laces, open at the ankle.
        private static void Shoe(Fit f, char upper, char sole, char laces, bool boot = false, bool studs = false)
        {
            f.Wrap(0, f.Y - 1, upper).HideBody();
            f.V.Box(-1, f.X, -2, -1, -1, f.Z, sole);
            if (studs)
            {
                for (int z = 1; z < f.Z; z += 4)
                    f.V.Box(1, 1, -3, -3, z, z, sole).Box(f.X - 2, f.X - 2, -3, -3, z, z, sole);
            }
            int instep = Mathf.RoundToInt(f.Z * 0.48f);
            f.V.Box(-1, f.X, f.Y, f.Y, instep, f.Z, upper);
            for (int z = instep + 1; z < f.Z - 2; z += 2)
                f.V.Box(f.MidX - 2, f.MidX + 1, f.Y + 1, f.Y + 1, z, z, laces);
            f.V.Paint(-1, f.X, 0, 1, f.Z, f.Z, sole);
            if (boot)
                f.V.Box(-1, f.X, f.Y, f.Y + 3, -1, -1, upper).Box(-1, -1, f.Y, f.Y + 3, -1, instep, upper).Box(f.X, f.X, f.Y, f.Y + 3, -1, instep, upper);
        }

        // A sandal: a sole and two straps over the foot.
        private static void Sandal(Fit f, char strap, char sole)
        {
            f.HideBody();
            f.V.Box(-1, f.X, -2, -1, -1, f.Z, sole);
            int a = Mathf.RoundToInt(f.Z * 0.55f), b = Mathf.RoundToInt(f.Z * 0.8f);
            foreach (int z in new[] { a, a + 1, b, b + 1 })
                f.V.Box(-1, f.X, f.Y, f.Y, z, z, strap).Box(-1, -1, 0, f.Y - 1, z, z, strap).Box(f.X, f.X, 0, f.Y - 1, z, z, strap);
        }

        // A check of two colours over the whole piece.
        private static void Checks(Fit f, char a, char b, int y0, int y1)
        {
            foreach (var (x, y, z) in new List<(int, int, int)>(f.V.Cells))
            {
                if (y < y0 || y > y1 || f.V.At(x, y, z) != a)
                    continue;
                int across = x <= -1 || x >= f.X ? z : x;
                if ((Mathf.FloorToInt((across + 50) / 3f) + Mathf.FloorToInt((y + 50) / 3f)) % 2 == 1)
                    f.V.Paint(x, x, y, y, z, z, b);
            }
        }

        // A printed pattern of flowers in rows, each a yellow or pink cross of petals round a centre, with a leaf.
        private static void Flowers(Fit f)
        {
            foreach (var (x, y, z) in new List<(int, int, int)>(f.V.Cells))
            {
                if (f.V.At(x, y, z) != 't')
                    continue;
                // Along the surface: across the front and back, round the sides.
                int u = x <= -1 || x >= f.X ? z + (x <= -1 ? 100 : 200) : x;
                int row = Mathf.FloorToInt((y + 60) / 7f);
                int cu = (u + 60 + (row % 2) * 4) % 8, cv = (y + 60) % 7;
                char petal = row % 2 == 0 ? 'f' : 'F';
                if (cu == 3 && cv == 3)
                    f.V.Paint(x, x, y, y, z, z, petal == 'f' ? 'F' : 'f');
                else if ((Math.Abs(cu - 3) == 1 && cv == 3) || (cu == 3 && Math.Abs(cv - 3) == 1))
                    f.V.Paint(x, x, y, y, z, z, petal);
                else if (cu == 5 && cv == 1)
                    f.V.Paint(x, x, y, y, z, z, 'l');
            }
        }

        // A big number 7 on the back, on a patch if asked.
        private static void Number(Fit f, char key, bool back, char patch = (char)0)
        {
            int z = back ? -1 : f.Z;
            int top = f.Down(0.18f), left = f.MidX - 3;
            if (patch != (char)0)
                f.V.Paint(left - 2, left + 7, top - 11, top + 2, z, z, patch);
            f.V.Paint(left, left + 5, top - 1, top, z, z, key);
            for (int i = 0; i < 9; i++)
            {
                int x = left + 5 - i / 2;
                f.V.Paint(x - 1, x, top - 2 - i, top - 2 - i, z, z, key);
            }
        }

        // ------------------------------------------------------------ hats

        private static void HardHat(Fit f, char shell, char trim)
        {
            f.HideBody();
            int band = f.Down(0.32f);
            f.V.Box(-2, f.X + 1, band, f.Y + 1, -2, f.Z + 1, shell);
            f.V.Box(-1, f.X, f.Y + 2, f.Y + 2, 0, f.Z - 1, shell);
            f.V.Box(f.MidX - 1, f.MidX, f.Y + 2, f.Y + 3, -2, f.Z + 1, trim);
            f.V.Box(-3, f.X + 2, band, band, -3, f.Z + 3, trim);
            f.V.Carve(0, f.X - 1, band, f.Y - 1, 0, f.Z - 1);
        }

        private static void PeakedCap(Fit f, char cloth, char peak, char badge)
        {
            f.HideBody();
            int band = f.Down(0.28f);
            f.V.Box(-1, f.X, band, f.Y - 1, -1, f.Z, peak);
            f.V.Box(-2, f.X + 1, f.Y, f.Y + 2, -2, f.Z + 1, cloth);
            f.V.Box(1, f.X - 2, f.Y + 3, f.Y + 3, 0, f.Z - 1, cloth);
            f.V.Box(1, f.X - 2, band, band, f.Z + 1, f.Z + 4, peak);
            f.V.Box(f.MidX - 1, f.MidX, band + 2, band + 4, f.Z + 1, f.Z + 1, badge);
            f.V.Carve(0, f.X - 1, band, f.Y - 1, 0, f.Z - 1);
        }

        private static void ChefHat(Fit f, char cloth, char shade)
        {
            f.HideBody();
            int band = f.Down(0.2f);
            f.V.Box(-1, f.X, band, f.Y, -1, f.Z, cloth);
            f.V.Box(-2, f.X + 1, f.Y + 1, f.Y + 9, -2, f.Z + 1, cloth);
            f.V.Box(-1, f.X, f.Y + 10, f.Y + 10, -1, f.Z, cloth);
            for (int x = -2; x <= f.X + 1; x += 3)
                f.V.Paint(x, x, f.Y + 1, f.Y + 9, -2, f.Z + 1, shade);
            f.V.Box(-1, f.X, band, band + 1, -1, f.Z, shade);
            f.V.Carve(0, f.X - 1, band, f.Y - 1, 0, f.Z - 1);
        }

        private static void StrawHat(Fit f, char straw, char band)
        {
            f.HideBody();
            int brim = f.Down(0.25f);
            f.V.Box(-1, f.X, brim, f.Y + 3, -1, f.Z, straw);
            f.V.Box(-1, f.X, brim + 1, brim + 2, -1, f.Z, band);
            float cx = (f.X - 1) / 2f, cz = (f.Z - 1) / 2f, r = f.X * 0.5f + 6f;
            for (int x = -8; x <= f.X + 7; x++)
            for (int z = -8; z <= f.Z + 7; z++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (d <= r)
                    f.V.Set(x, brim, z, straw);
            }
            f.V.Carve(0, f.X - 1, brim, f.Y - 1, 0, f.Z - 1);
        }

        private static void Cap(Fit f, char cloth, char peak)
        {
            f.HideBody();
            int band = f.Down(0.3f);
            f.V.Box(-1, f.X, band, f.Y, -1, f.Z, cloth);
            f.V.Box(0, f.X - 1, f.Y + 1, f.Y + 1, 0, f.Z - 1, cloth);
            f.V.Box(f.MidX - 1, f.MidX, f.Y + 2, f.Y + 2, f.Z / 2, f.Z / 2 + 1, peak);
            f.V.Box(1, f.X - 2, band, band, f.Z + 1, f.Z + 6, peak);
            f.V.Carve(0, f.X - 1, band, f.Y - 1, 0, f.Z - 1);
        }
    }
}
