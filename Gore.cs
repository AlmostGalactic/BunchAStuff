using System;
using FruktSharedLibrary.Core;
using Il2CppVoxelMeshGeneration.Painting;
using UnityEngine;

namespace BunchAStuff
{
    /// <summary>
    /// Blood inside bodies. The game paints its blood onto the outside of the skin only, so a wound opened up shows
    /// clean flesh inside. Every blood stamp is made to paint all the way in instead, so the inside of a wound, and
    /// what a cut opens up, is bloody too.
    /// </summary>
    internal static class Gore
    {
        /// <summary>Set while the mod paints its own marks (bruises), which stay on the skin.</summary>
        internal static bool Ours;

        internal static void Create()
        {
            try
            {
                var harmony = new HarmonyLib.Harmony("BunchAStuff.Gore");
                var original = HarmonyLib.AccessTools.Method(typeof(VoxelMeshPaintModule), nameof(VoxelMeshPaintModule.TryStamp));
                harmony.Patch(original, prefix: new HarmonyLib.HarmonyMethod(typeof(Gore), nameof(Stamping)));
            }
            catch (Exception e)
            {
                FruktLog.Warning("Blood can't go inside bodies: " + e.Message);
            }
        }

        private static void Stamping(ref PaintMode mode)
        {
            if (!Ours && mode == PaintMode.Surface && Settings.BloodInside)
                mode = PaintMode.Full;
        }
    }
}
