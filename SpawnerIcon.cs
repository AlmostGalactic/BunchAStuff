using System;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Gameplay;
using UnityEngine;

namespace BunchAStuff
{
    /// <summary>
    /// The Clothed Human Spawner's icon: the game's own Human Spawner icon with a brown hat on its head, so the two
    /// can be told apart on the toolbar.
    /// </summary>
    internal static class SpawnerIcon
    {
        private const int Size = 128;
        // The person is drawn a little smaller and low down, to leave room for the hat.
        private const float Shrink = 0.8f;

        private static readonly Color Felt = new(0.38f, 0.26f, 0.16f), FeltLight = new(0.5f, 0.36f, 0.23f);
        private static readonly Color Band = new(0.62f, 0.15f, 0.12f), Edge = new(0.07f, 0.05f, 0.04f);

        private static bool _done;
        private static float _next;

        internal static void Update(ModTool spawner)
        {
            if (_done || spawner == null || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 1f;
            var original = Inventory.Find("Human")?.Icon;
            if (original == null || original.texture == null)
                return;
            _done = true;
            try
            {
                var sprite = Make(original);
                if (sprite != null)
                    spawner.WithIcon(sprite);
            }
            catch (Exception e)
            {
                FruktLog.Debug("Making the Clothed Human Spawner's icon failed: " + e.Message);
            }
        }

        private static Sprite Make(Sprite original)
        {
            var source = Copy(original);
            // Where the picture is, and where the head is: the top tenth of it.
            int top = -1, bottom = Size, left = Size, right = -1;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (source.GetPixel(x, y).a < 0.2f)
                        continue;
                    top = Math.Max(top, y);
                    bottom = Math.Min(bottom, y);
                }
            }
            if (top < 0)
                return null;
            int headRows = Math.Max(3, (top - bottom) / 10);
            for (int y = top - headRows; y <= top; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (source.GetPixel(x, y).a < 0.2f)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }

            var icon = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Clothed Human Spawner", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = ((x + 0.5f) / Size - 0.5f) / Shrink + 0.5f, v = (y + 0.5f) / Size / Shrink;
                    icon.SetPixel(x, y, u < 0f || u > 1f || v > 1f ? Color.clear : source.GetPixelBilinear(u, v));
                }
            }
            UnityEngine.Object.Destroy(source);

            // The head as it sits in the new picture.
            float headTop = (top + 1) * Shrink;
            float headX = ((left + right + 1) / 2f - Size / 2f) * Shrink + Size / 2f;
            float headHalf = Mathf.Max(6f, (right - left + 1) * Shrink / 2f);
            DrawHat(icon, headX, headTop - Size * 0.03f, headHalf);
            icon.Apply();
            icon.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(icon, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Clothed Human Spawner";
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // A fedora: a wide brim, a crown with a dent in the top, and a red band, drawn with a dark edge so it reads
        // on the toolbar.
        private static void DrawHat(Texture2D icon, float centre, float brimY, float headHalf)
        {
            float brimHalf = Mathf.Min(Size * 0.47f, headHalf * 1.75f), brimThick = Size * 0.045f;
            float crownHalf = Mathf.Min(Size * 0.3f, headHalf * 1.1f), crownHigh = Size * 0.15f, bandHigh = Size * 0.035f;
            bool Brim(float x, float y)
            {
                float dx = (x - centre) / brimHalf, dy = (y - brimY - brimThick / 2f) / (brimThick / 2f);
                return dx * dx * dx * dx + dy * dy <= 1f;
            }
            bool Crown(float x, float y)
            {
                float up = y - (brimY + brimThick * 0.6f);
                if (up < 0f || up > crownHigh)
                    return false;
                // Narrower towards the top, rounded at the corners, with a dip in the middle of the top.
                float half = crownHalf * Mathf.Lerp(1f, 0.82f, up / crownHigh);
                float dx = Mathf.Abs(x - centre) / half;
                if (dx > 1f)
                    return false;
                float dent = crownHigh * 0.12f * (1f - dx * dx);
                float round = crownHigh * 0.18f * Mathf.Pow(dx, 6f);
                return up <= crownHigh - dent - round;
            }
            bool Hat(float x, float y) => Brim(x, y) || Crown(x, y);

            int x0 = Mathf.Max(0, (int)(centre - brimHalf - 3)), x1 = Mathf.Min(Size - 1, (int)(centre + brimHalf + 3));
            int y0 = Mathf.Max(0, (int)(brimY - 3)), y1 = Mathf.Min(Size - 1, (int)(brimY + brimThick + crownHigh + 3));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    if (Hat(px, py))
                    {
                        Color colour;
                        float up = py - (brimY + brimThick * 0.6f);
                        if (Crown(px, py) && up >= 0f && up < bandHigh)
                            colour = Band;
                        else
                            // Lit from the upper left.
                            colour = Color.Lerp(FeltLight, Felt, Mathf.Clamp01((px - (centre - brimHalf)) / (brimHalf * 2f) + 0.2f));
                        icon.SetPixel(x, y, colour);
                        continue;
                    }
                    // A dark edge round the hat.
                    bool near = false;
                    for (int dy = -2; dy <= 2 && !near; dy++)
                    {
                        for (int dx = -2; dx <= 2 && !near; dx++)
                            near = dx * dx + dy * dy <= 5 && Hat(px + dx, py + dy);
                    }
                    if (near)
                        icon.SetPixel(x, y, Edge);
                }
            }
        }

        // The sprite as a readable picture of its own: the game's textures can't be read directly.
        private static Texture2D Copy(Sprite sprite)
        {
            var texture = sprite.texture;
            var area = sprite.textureRect;
            var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var before = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, target, new Vector2(area.width / texture.width, area.height / texture.height),
                    new Vector2(area.x / texture.width, area.y / texture.height));
                RenderTexture.active = target;
                var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
