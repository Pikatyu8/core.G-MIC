using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class PlasmaGenerator
    {
        public static void CreateOrUpdateLegacy(ref Texture2D texture, int size, int scalePlasma, int seed)
        {
            int maxStep = size - 1;
            int initialDiv = Mathf.Clamp(Mathf.RoundToInt(scalePlasma / 4f), 1, 8);
            int initialStep = Mathf.Max(4, maxStep >> (initialDiv - 1));
            float roughness = Mathf.Lerp(0.45f, 0.85f, (scalePlasma - 1f) / 29f);
            GenerateInternal(ref texture, size, initialStep, roughness, seed, null, false);
        }

        public static void CreateOrUpdateGmic(
            ref Texture2D texture,
            int size,
            float scale,
            int seed,
            Color? balanceGammaTarget = null,
            bool generateAlpha = false)
        {
            int maxStep = size - 1;
            int scaleInt = Mathf.Clamp((int)scale, 0, 12);
            int initialStep = Mathf.Clamp(1 << scaleInt, 1, maxStep);
            GenerateInternal(ref texture, size, initialStep, 0.5f, seed, balanceGammaTarget, generateAlpha);
        }

        private static void GenerateInternal(
            ref Texture2D texture,
            int n,
            int initialStep,
            float roughness,
            int seed,
            Color? balanceGammaTarget,
            bool generateAlpha)
        {
            if (texture == null || texture.width != n || texture.height != n)
            {
                GpuUtils.DestroyObject(ref texture);
                texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            int total = n * n;
            float[] r = new float[total];
            float[] g = new float[total];
            float[] b = new float[total];
            float[] a = generateAlpha ? new float[total] : null;

            System.Random rng = new System.Random(seed);

            for (int y = 0; y < n; y += initialStep)
            {
                for (int x = 0; x < n; x += initialStep)
                {
                    int idx = y * n + x;
                    r[idx] = (float)rng.NextDouble();
                    g[idx] = (float)rng.NextDouble();
                    b[idx] = (float)rng.NextDouble();
                    if (a != null)
                    {
                        a[idx] = (float)rng.NextDouble();
                    }
                }
            }

            if (initialStep > 1)
            {
                RunDiamondSquare(r, n, initialStep, roughness, rng);
                RunDiamondSquare(g, n, initialStep, roughness, rng);
                RunDiamondSquare(b, n, initialStep, roughness, rng);
                if (a != null)
                {
                    RunDiamondSquare(a, n, initialStep, roughness, rng);
                }
            }

            float minVal = float.MaxValue;
            float maxVal = float.MinValue;
            for (int i = 0; i < total; i++)
            {
                if (r[i] < minVal) minVal = r[i];
                if (g[i] < minVal) minVal = g[i];
                if (b[i] < minVal) minVal = b[i];
                if (r[i] > maxVal) maxVal = r[i];
                if (g[i] > maxVal) maxVal = g[i];
                if (b[i] > maxVal) maxVal = b[i];
                if (a != null)
                {
                    if (a[i] < minVal) minVal = a[i];
                    if (a[i] > maxVal) maxVal = a[i];
                }
            }

            float invRange = (maxVal - minVal) > 1e-5f ? 1f / (maxVal - minVal) : 1f;
            for (int i = 0; i < total; i++)
            {
                r[i] = Mathf.Clamp01((r[i] - minVal) * invRange);
                g[i] = Mathf.Clamp01((g[i] - minVal) * invRange);
                b[i] = Mathf.Clamp01((b[i] - minVal) * invRange);
                if (a != null)
                {
                    a[i] = Mathf.Clamp01((a[i] - minVal) * invRange);
                }
            }

            if (balanceGammaTarget.HasValue)
            {
                Color target = balanceGammaTarget.Value;
                ApplyBalanceGamma(r, target.r);
                ApplyBalanceGamma(g, target.g);
                ApplyBalanceGamma(b, target.b);
                if (a != null)
                {
                    ApplyBalanceGamma(a, target.b);
                }
            }

            Color[] pixels = new Color[total];
            for (int i = 0; i < total; i++)
            {
                pixels[i] = new Color(r[i], g[i], b[i], a != null ? a[i] : 1f);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static void ApplyBalanceGamma(float[] ch, float target)
        {
            double sum = 0.0;
            for (int i = 0; i < ch.Length; i++)
            {
                sum += ch[i];
            }

            float mean = (float)(sum / Math.Max(1, ch.Length));
            float t = Mathf.Clamp(target, 1e-4f, 0.9999f);
            float m = Mathf.Clamp(mean, 1e-4f, 0.9999f);
            if (Mathf.Abs(Mathf.Log(m)) < 1e-5f)
            {
                return;
            }

            float gamma = Mathf.Log(t) / Mathf.Log(m);
            for (int i = 0; i < ch.Length; i++)
            {
                ch[i] = Mathf.Clamp01(Mathf.Pow(ch[i], gamma));
            }
        }

        private static void RunDiamondSquare(float[] grid, int n, int initialStep, float roughness, System.Random rng)
        {
            int maxIdx = n - 1;
            float scale = 0.5f;

            for (int step = initialStep; step > 1; step >>= 1)
            {
                int half = step >> 1;

                for (int y = half; y < maxIdx; y += step)
                {
                    for (int x = half; x < maxIdx; x += step)
                    {
                        float avg = 0.25f * (
                            grid[(y - half) * n + (x - half)] +
                            grid[(y - half) * n + (x + half)] +
                            grid[(y + half) * n + (x - half)] +
                            grid[(y + half) * n + (x + half)]
                        );
                        float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * scale;
                        grid[y * n + x] = avg + jitter;
                    }
                }

                for (int y = 0; y < n; y += half)
                {
                    int xStart = ((y / half) % 2 == 0) ? half : 0;
                    for (int x = xStart; x < n; x += step)
                    {
                        float sum = 0f;
                        int count = 0;
                        if (x - half >= 0) { sum += grid[y * n + (x - half)]; count++; }
                        if (x + half < n) { sum += grid[y * n + (x + half)]; count++; }
                        if (y - half >= 0) { sum += grid[(y - half) * n + x]; count++; }
                        if (y + half < n) { sum += grid[(y + half) * n + x]; count++; }

                        float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * scale;
                        grid[y * n + x] = (count > 0 ? sum / count : 0.5f) + jitter;
                    }
                }

                scale *= roughness;
            }
        }
    }
}
