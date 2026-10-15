using UnityEngine;

namespace BunchAStuff
{
    /// <summary>
    /// The melee weapons' voxel models, a centimetre to a voxel like the guns: z runs from the hand out along the
    /// weapon, y up, and the hand holds it at the origin.
    /// </summary>
    internal static class MeleeModels
    {
        private static Color C(int rgb) => Voxels.Hex(rgb);

        internal static GameObject Bat()
        {
            var v = new Voxels()
                .Key('W', C(0xc89a5c)).Key('w', C(0xa97b42)).Key('D', C(0x7a5229))
                .Key('T', C(0x1d1d20)).Key('t', C(0x34343a)).Key('L', C(0xe8e2d0));
            // Knob, taped handle, then the barrel swelling out to the end.
            v.Tube(0, 0, -10, -9, 2.6f, 'D');
            v.Tube(0, 0, -8, 14, 1.6f, 'T');
            for (int z = -7; z <= 13; z += 3)
                v.Tube(0, 0, z, z, 1.6f, 't');
            for (int z = 15; z <= 72; z++)
            {
                float r = Mathf.Lerp(1.7f, 3.6f, Mathf.SmoothStep(0f, 1f, (z - 15) / 45f));
                v.Tube(0, 0, z, z, r, (z / 4) % 3 == 0 ? 'w' : 'W');
            }
            v.Tube(0, 0, 73, 73, 3.1f, 'w');
            // A maker's stamp on the barrel.
            v.Paint(-1, 1, 3, 4, 40, 50, 'D');
            return v.Build("Melee Baseball Bat");
        }

        internal static GameObject Sledgehammer()
        {
            var v = new Voxels()
                .Key('W', C(0xb58a4f)).Key('w', C(0x8e6a38)).Key('S', C(0x4b4f55)).Key('s', C(0x6e737a))
                .Key('E', C(0x9aa0a8)).Key('R', C(0xb02c22)).Key('K', C(0x202124));
            // A long hickory handle with a rubber grip, and a heavy steel head across it.
            v.Tube(0, 0, -10, 8, 1.8f, 'K');
            v.Tube(0, 0, 9, 78, 1.5f, 'W');
            for (int z = 12; z <= 76; z += 6)
                v.Tube(0, 0, z, z + 1, 1.5f, 'w');
            v.Box(-4, 4, -11, 11, 76, 86, 'S');
            v.Box(-3, 3, -10, 10, 77, 85, 's');
            // Worn striking faces, and a red band at the neck.
            v.Paint(-4, 4, 11, 11, 76, 86, 'E').Paint(-4, 4, -11, -11, 76, 86, 'E');
            v.Tube(0, 0, 70, 72, 2.2f, 'R');
            return v.Build("Melee Sledgehammer");
        }

        internal static GameObject Pan()
        {
            var v = new Voxels()
                .Key('I', C(0x2a2b2e)).Key('i', C(0x3c3e42)).Key('G', C(0x55585e)).Key('K', C(0x141416));
            // Handle, then a round pan lying flat with a raised rim.
            v.Box(-1, 1, -1, 1, -6, 20, 'K');
            v.Box(-1, 1, 0, 0, -6, 20, 'i');
            v.Post(0, 33, -1, 0, 13f, 'I');
            v.Post(0, 33, 1, 3, 13f, 'I', 11.6f);
            v.Post(0, 33, 0, 0, 10f, 'G');
            v.Box(-2, 2, -1, 1, 19, 21, 'I');
            return v.Build("Melee Frying Pan");
        }

        internal static GameObject Machete()
        {
            var v = new Voxels()
                .Key('K', C(0x18181a)).Key('k', C(0x2c2c30)).Key('B', C(0x8d939b)).Key('E', C(0xd9dee4)).Key('Y', C(0xc9a24a));
            // Rubber grip with brass rivets.
            v.Box(-1, 1, -2, 2, -9, 10, 'K');
            v.Box(-1, 1, -2, -2, -9, 10, 'k');
            v.Set(1, 0, -4, 'Y').Set(1, 0, 4, 'Y').Set(-1, 0, -4, 'Y').Set(-1, 0, 4, 'Y');
            v.Box(-1, 1, -3, 3, 10, 11, 'K');
            // A long flat blade that widens towards the tip, with a bright edge along the bottom.
            for (int z = 12; z <= 60; z++)
            {
                float k = (z - 12) / 48f;
                int top = Mathf.RoundToInt(Mathf.Lerp(2f, 4f, k));
                int bottom = -Mathf.RoundToInt(Mathf.Lerp(3f, 4.5f, k));
                if (z > 54)
                    top -= (z - 54);
                v.Box(0, 0, bottom + 1, top, z, z, 'B');
                v.Set(0, bottom, z, 'E');
            }
            return v.Build("Melee Machete");
        }

        internal static GameObject Knife()
        {
            var v = new Voxels()
                .Key('H', C(0x3a2416)).Key('h', C(0x5a3a22)).Key('S', C(0xb7bcc3)).Key('E', C(0xe8ecf0)).Key('M', C(0xa0a5ac));
            // Wooden handle with steel rivets, a bolster, and a pointed blade.
            v.Box(-1, 1, -2, 1, -7, 7, 'H');
            v.Box(-1, 1, 1, 1, -7, 7, 'h');
            v.Set(1, 0, -3, 'M').Set(1, 0, 3, 'M').Set(-1, 0, -3, 'M').Set(-1, 0, 3, 'M');
            v.Box(-1, 1, -2, 2, 8, 8, 'M');
            for (int z = 9; z <= 28; z++)
            {
                float k = (z - 9) / 19f;
                int bottom = -Mathf.RoundToInt(Mathf.Lerp(3f, 0f, k * k));
                v.Box(0, 0, bottom + 1, 1, z, z, 'S');
                v.Set(0, bottom, z, 'E');
            }
            return v.Build("Melee Kitchen Knife");
        }
    }
}
