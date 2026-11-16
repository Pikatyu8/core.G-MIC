using System;
using UnityEngine;

namespace CoreGMIC.LUT
{
    public static class LutTextureGenerator
    {
        public const int DefaultSize = 32;

        /// <summary>
        /// Generates a 2D strip texture representing a 3D LUT of size^3.
        /// Input coordinates passed to colorTransform are normalized in [0, 1].
        /// </summary>
        public static Texture2D GenerateStrip(Func<float, float, float, Color> colorTransform, int size = DefaultSize)
        {
            int width = size * size;
            int height = size;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[width * height];
            float invMax = 1f / (size - 1);

            for (int b = 0; b < size; b++)
            {
                float bf = b * invMax;
                for (int g = 0; g < size; g++)
                {
                    float gf = g * invMax;
                    int rowOffset = g * width + b * size;
                    for (int r = 0; r < size; r++)
                    {
                        float rf = r * invMax;
                        Color c = colorTransform(rf, gf, bf);
                        c.a = 1f;
                        pixels[rowOffset + r] = c;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}