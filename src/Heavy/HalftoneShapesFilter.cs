using System;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class HalftoneShapesFilter : FilterBase
    {
        private static readonly string[] ShapeChoices =
        {
            "Circle",
            "Star",
            "Triangle",
            "Heart"
        };

        private readonly Texture2D[] _shapeTiles = new Texture2D[4];

        private float _scale = 100f;
        private bool _negative = false;
        private int _shape = 0;
        private bool _oversample = false;
        private float _rotate = 0f;
        private float _twirl = 0f;
        private bool _warpIt = false;

        public override string Id => "iain_halftone_shapes";
        public override string DisplayName => "Halftone Shapes";
        public override string Description => "Applies patterned halftone screening using circles, stars, triangles, or hearts.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable => base.IsAvailable && GetOrBakeTile(_shape) != null;

        public HalftoneShapesFilter()
        {
            AddParameter(FilterParameter.Float(
                "scale",
                "Scale",
                "Scale of the halftone cell grid.",
                5f, 200f, 100f,
                v => _scale = v,
                step: 1f));

            AddParameter(FilterParameter.Bool(
                "negative",
                "Negative",
                "Invert halftone ink polarity.",
                false,
                v => _negative = v));

            AddParameter(FilterParameter.Choice(
                "shape",
                "Shape",
                "Halftone screen geometry.",
                ShapeChoices,
                0,
                v => _shape = v));

            AddParameter(FilterParameter.Bool(
                "oversample",
                "Oversample",
                "Enable 4x4 subpixel supersampling for smoother edges.",
                false,
                v => _oversample = v));

            AddParameter(FilterParameter.Bool(
                "warp_it",
                "Turn on Rotate and Twirl",
                "Enable rotation and twirl distortion on the halftone screen.",
                false,
                v => _warpIt = v));

            AddParameter(FilterParameter.Float(
                "rotate",
                "Rotate",
                "Grid rotation angle in degrees.",
                0f, 180f, 0f,
                v => _rotate = v,
                isVisible: () => _warpIt,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "twirl",
                "Twirl",
                "Grid twirl distortion amplitude.",
                0f, 2f, 0f,
                v => _twirl = v,
                isVisible: () => _warpIt,
                step: 0.05f));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/HalftoneShapes"))
            {
                return;
            }

            GetOrBakeTile(_shape);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            Texture2D tileTex = GetOrBakeTile(_shape);
            if (tileTex == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            Vector2 tileSizePx = GetTileSizePx(_shape, _scale);

            Material.SetTexture("_TileTex", tileTex);
            Material.SetVector("_TileSizePx", new Vector4(tileSizePx.x, tileSizePx.y, 0f, 0f));
            Material.SetFloat("_Negative", _negative ? 1f : 0f);
            Material.SetFloat("_Oversample", _oversample ? 1f : 0f);
            Material.SetFloat("_WarpIt", _warpIt ? 1f : 0f);
            Material.SetFloat("_RotateRad", _rotate * Mathf.Deg2Rad);
            Material.SetFloat("_Twirl", _twirl);

            Graphics.Blit(source, destination, Material);
        }

        public override void Dispose()
        {
            for (int i = 0; i < _shapeTiles.Length; i++)
            {
                GpuUtils.DestroyObject(ref _shapeTiles[i]);
            }

            base.Dispose();
        }

        private static Vector2 GetTileSizePx(int shape, float scale)
        {
            float s = Mathf.Max(5f, scale);
            switch (shape)
            {
                case 2: // Triangle
                    return new Vector2(168f * (s * 0.01f), 145f * (s * 0.01f));
                case 3: // Heart
                    return new Vector2(s, s);
                default: // Circle / Star
                    return new Vector2(194f * (s * 0.01f), 112f * (s * 0.01f));
            }
        }

        private Texture2D GetOrBakeTile(int shape)
        {
            int idx = Mathf.Clamp(shape, 0, 3);
            if (_shapeTiles[idx] != null)
            {
                return _shapeTiles[idx];
            }

            switch (idx)
            {
                case 0:
                    _shapeTiles[idx] = BakeCircleTile();
                    break;
                case 1:
                    _shapeTiles[idx] = BakeStarTile();
                    break;
                case 2:
                    _shapeTiles[idx] = BakeTriangleTile();
                    break;
                case 3:
                    _shapeTiles[idx] = BakeHeartTile();
                    break;
            }

            return _shapeTiles[idx];
        }

        private static Texture2D BakeCircleTile()
        {
            const int canvasSize = 400; // 2000 * 0.2
            const float scale = 0.2f;
            float[] grid = new float[canvasSize * canvasSize];

            float[] vx = { 353, 515, 677, 1000, 1323, 1485, 1647, 1485, 1323, 1000, 677, 515 };
            float[] vy = { 1000, 1280, 1560, 1560, 1560, 1280, 1000, 720, 440, 440, 440, 720 };
            float[] px = new float[12];
            float[] py = new float[12];

            float level = 19f;
            for (int iter = 0; iter < 19; iter++)
            {
                for (int k = 0; k < 12; k++)
                {
                    px[k] = vx[k] * scale;
                    py[k] = vy[k] * scale;
                }

                FillPolygonScanline(grid, canvasSize, canvasSize, px, py, 12, level);

                vy[2] -= 78f / 16f;
                vy[4] -= 78f / 16f;
                vy[8] += 78f / 16f;
                vy[10] += 78f / 16f;
                vx[0] += 90f / 16f;
                vx[2] += 45f / 16f;
                vx[4] -= 45f / 16f;
                vx[6] -= 90f / 16f;
                vx[8] -= 45f / 16f;
                vx[10] += 45f / 16f;
                level += 10f;
            }

            // Concentric circles r = 560 -> 0 (in 0.2 scale -> 112 -> 0)
            float cx = 1000f * scale;
            float cy = 1000f * scale;
            float rMax = 560f * scale;
            for (int y = 0; y < canvasSize; y++)
            {
                float dy = (y + 0.5f) - cy;
                for (int x = 0; x < canvasSize; x++)
                {
                    float dx = (x + 0.5f) - cx;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r <= rMax)
                    {
                        float ringIdx = Mathf.Clamp(Mathf.Floor((560f - r / scale) / 10f), 0f, 56f);
                        float cLevel = 209f + 10f * ringIdx;
                        if (cLevel > grid[y * canvasSize + x])
                        {
                            grid[y * canvasSize + x] = cLevel;
                        }
                    }
                }
            }

            NormalizeGrid01(grid);

            int sx = Mathf.RoundToInt(972f * scale);
            int sy = Mathf.RoundToInt(560f * scale);
            float[] tiled = AddShifts(grid, canvasSize, canvasSize, sx, sy);

            int outW = 388;
            int outH = 224;
            float[] outGrid = CropAndResample(tiled, canvasSize, canvasSize, 29f * scale, 440f * scale, 1972f * scale, 1560f * scale, outW, outH);
            EqualizeGrid(outGrid);
            return BuildTileTexture(outGrid, outW, outH, flipY: true);
        }

        private static Texture2D BakeStarTile()
        {
            const int canvasSize = 400; // 2000 * 0.2
            const float scale = 0.2f;
            float[] grid = new float[canvasSize * canvasSize];

            float[] vx = { 50, 194, 281, 511, 742, 829, 972, 829, 742, 511, 281, 192 };
            float[] vy = { 450, 633, 849, 817, 849, 633, 450, 267, 50, 83, 50, 267 };
            float[] px = new float[12];
            float[] py = new float[12];

            float level = 10f;
            for (int iter = 0; iter < 128; iter++)
            {
                for (int k = 0; k < 12; k++)
                {
                    px[k] = vx[k] * scale;
                    py[k] = vy[k] * scale;
                }

                FillPolygonScanline(grid, canvasSize, canvasSize, px, py, 12, level);

                vx[1] += 40f / 16f;
                vx[5] -= 40f / 16f;
                vx[7] -= 40f / 16f;
                vx[11] += 40f / 16f;
                vy[1] -= 23f / 16f;
                vy[3] -= 47f / 16f;
                vy[5] -= 23f / 16f;
                vy[7] += 23f / 16f;
                vy[9] += 47f / 16f;
                vy[11] += 23f / 16f;
                level += 12f;
            }

            NormalizeGrid01(grid);

            int sx = Mathf.RoundToInt(690f * scale);
            int sy = Mathf.RoundToInt(400f * scale);
            float[] tiled = AddShifts(grid, canvasSize, canvasSize, sx, sy);

            int outW = 388;
            int outH = 224;
            float[] outGrid = CropAndResample(tiled, canvasSize, canvasSize, 50f * scale, 50f * scale, 1420f * scale, 840f * scale, outW, outH);
            EqualizeGrid(outGrid);
            return BuildTileTexture(outGrid, outW, outH, flipY: true);
        }

        private static Texture2D BakeTriangleTile()
        {
            const int canvasSize = 400; // 2000 * 0.2
            const float scale = 0.2f;
            float[] grid = new float[canvasSize * canvasSize];

            float sx1 = 161f, sx2 = 1000f, sx3 = 1839f;
            float sy1 = 273f, sy2 = 1727f, sy3 = 273f;
            float[] px = new float[3];
            float[] py = new float[3];

            float level = 10f;
            for (int iter = 0; iter < 375; iter++)
            {
                px[0] = sx1 * scale; px[1] = sx2 * scale; px[2] = sx3 * scale;
                py[0] = sy1 * scale; py[1] = sy2 * scale; py[2] = sy3 * scale;
                FillPolygonScanline(grid, canvasSize, canvasSize, px, py, 3, level);

                sx1 += 2.21f;
                sx3 -= 2.21f;
                sy1 += 1.28f;
                sy2 -= 2.56f;
                sy3 += 1.28f;
                level += 1f;
            }

            // Combine with 180-degree rotated + shifted copy (interlocking upright & inverted triangles)
            float[] combined = new float[canvasSize * canvasSize];
            int shiftA = Mathf.RoundToInt(842f * scale);
            int shiftB = Mathf.RoundToInt(-840f * scale);
            for (int y = 0; y < canvasSize; y++)
            {
                int ry = canvasSize - 1 - y;
                for (int x = 0; x < canvasSize; x++)
                {
                    int rx = canvasSize - 1 - x;
                    float rotValA = SampleZeroBounds(grid, canvasSize, canvasSize, rx - shiftA, ry);
                    float rotValB = SampleZeroBounds(grid, canvasSize, canvasSize, rx - shiftB, ry);
                    combined[y * canvasSize + x] = grid[y * canvasSize + x] + rotValA + rotValB;
                }
            }

            NormalizeGrid01(combined);

            int outW = 336;
            int outH = 290;
            float[] outGrid = CropAndResample(combined, canvasSize, canvasSize, 160f * scale, 272f * scale, 1842f * scale, 1727f * scale, outW, outH);
            EqualizeGrid(outGrid);
            return BuildTileTexture(outGrid, outW, outH, flipY: true);
        }

        private static Texture2D BakeHeartTile()
        {
            const int baseW = 192;
            const int baseH = 192;
            float[] singleHeart = new float[baseW * baseH];

            // Circle (50,50,50) + Circle (140,50,50) + Polygon (10,80 -> 50,50 -> 140,50 -> 180,80 -> 95,190) in 190x190 padded by 1px
            for (int y = 0; y < 190; y++)
            {
                for (int x = 0; x < 190; x++)
                {
                    float dx1 = (x + 0.5f) - 50f;
                    float dy1 = (y + 0.5f) - 50f;
                    float dx2 = (x + 0.5f) - 140f;
                    float dy2 = (y + 0.5f) - 50f;
                    if (dx1 * dx1 + dy1 * dy1 <= 2500f || dx2 * dx2 + dy2 * dy2 <= 2500f)
                    {
                        singleHeart[(y + 1) * baseW + (x + 1)] = 10f;
                    }
                }
            }

            float[] vx = { 11f, 51f, 141f, 181f, 96f };
            float[] vy = { 81f, 51f, 51f, 81f, 191f };
            FillPolygonScanline(singleHeart, baseW, baseH, vx, vy, 5, 10f);

            // Sum 17 scaled copies: 100%, 95%, 90%, 85%, 75%, 65%, 55%, 50%, 45%, 40%, 35%, 30%, 25%, 20%, 15%, 10%, 5%
            float[] scales = { 1f, 0.95f, 0.90f, 0.85f, 0.75f, 0.65f, 0.55f, 0.50f, 0.45f, 0.40f, 0.35f, 0.30f, 0.25f, 0.20f, 0.15f, 0.10f, 0.05f };
            float[] layered = new float[baseW * baseH];
            for (int s = 0; s < scales.Length; s++)
            {
                float sc = scales[s];
                float cx = baseW * 0.5f;
                float cy = baseH * 0.5f;
                for (int y = 0; y < baseH; y++)
                {
                    float sy = (y + 0.5f - cy) / sc + cy;
                    for (int x = 0; x < baseW; x++)
                    {
                        float sx = (x + 0.5f - cx) / sc + cx;
                        layered[y * baseW + x] += SampleBilinearZero(singleHeart, baseW, baseH, sx, sy);
                    }
                }
            }

            // Append blank spacer below -> resize width 200% (384x384) -> shift (±160, +100) and add -> crop (30, 190, 350, 383)
            const int bigW = 384;
            const int bigH = 384;
            float[] big = new float[bigW * bigH];
            for (int y = 0; y < baseH; y++)
            {
                for (int x = 0; x < baseW; x++)
                {
                    big[y * bigW + (x + 96)] = layered[y * baseW + x];
                }
            }

            float[] tiled = new float[bigW * bigH];
            for (int y = 0; y < bigH; y++)
            {
                for (int x = 0; x < bigW; x++)
                {
                    float v0 = big[y * bigW + x];
                    float v1 = SampleZeroBounds(big, bigW, bigH, x - 160, y - 100);
                    float v2 = SampleZeroBounds(big, bigW, bigH, x + 160, y - 100);
                    tiled[y * bigW + x] = v0 + v1 + v2;
                }
            }

            NormalizeGrid01(tiled);

            const int outW = 320;
            const int outH = 200;
            float[] outGrid = CropAndResample(tiled, bigW, bigH, 30f, 190f, 350f, 383f, outW, outH);
            EqualizeGrid(outGrid);
            return BuildTileTexture(outGrid, outW, outH, flipY: true);
        }

        private static float[] AddShifts(float[] src, int w, int h, int sx, int sy)
        {
            float[] dst = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = src[y * w + x]
                        + SampleZeroBounds(src, w, h, x - sx, y - sy)
                        + SampleZeroBounds(src, w, h, x - sx, y + sy)
                        + SampleZeroBounds(src, w, h, x + sx, y - sy)
                        + SampleZeroBounds(src, w, h, x + sx, y + sy);
                    dst[y * w + x] = sum;
                }
            }
            return dst;
        }

        private static float[] CropAndResample(float[] src, int sw, int sh, float x0, float y0, float x1, float y1, int dw, int dh)
        {
            float[] dst = new float[dw * dh];
            float spanX = x1 - x0;
            float spanY = y1 - y0;
            for (int y = 0; y < dh; y++)
            {
                float sy = y0 + ((y + 0.5f) / dh) * spanY;
                for (int x = 0; x < dw; x++)
                {
                    float sx = x0 + ((x + 0.5f) / dw) * spanX;
                    dst[y * dw + x] = SampleBilinearClamp(src, sw, sh, sx, sy);
                }
            }
            return dst;
        }

        private static float SampleZeroBounds(float[] grid, int w, int h, int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h)
            {
                return 0f;
            }
            return grid[y * w + x];
        }

        private static float SampleBilinearZero(float[] grid, int w, int h, float x, float y)
        {
            if (x < 0f || x >= w || y < 0f || y >= h)
            {
                return 0f;
            }
            return SampleBilinearClamp(grid, w, h, x, y);
        }

        private static float SampleBilinearClamp(float[] grid, int w, int h, float x, float y)
        {
            float fx = Mathf.Clamp(x - 0.5f, 0f, w - 1.001f);
            float fy = Mathf.Clamp(y - 0.5f, 0f, h - 1.001f);
            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = Mathf.Min(w - 1, x0 + 1);
            int y1 = Mathf.Min(h - 1, y0 + 1);
            float tx = fx - x0;
            float ty = fy - y0;

            float a = grid[y0 * w + x0];
            float b = grid[y0 * w + x1];
            float c = grid[y1 * w + x0];
            float d = grid[y1 * w + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static void FillPolygonScanline(float[] grid, int w, int h, float[] vx, float[] vy, int n, float value)
        {
            float minY = vy[0];
            float maxY = vy[0];
            for (int i = 1; i < n; i++)
            {
                if (vy[i] < minY) minY = vy[i];
                if (vy[i] > maxY) maxY = vy[i];
            }

            int yStart = Mathf.Clamp(Mathf.FloorToInt(minY), 0, h - 1);
            int yEnd = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, h - 1);
            float[] nodes = new float[n];

            for (int y = yStart; y <= yEnd; y++)
            {
                float yf = y + 0.5f;
                int nodeCount = 0;
                int j = n - 1;
                for (int i = 0; i < n; i++)
                {
                    if ((vy[i] <= yf && vy[j] > yf) || (vy[j] <= yf && vy[i] > yf))
                    {
                        float denom = vy[j] - vy[i];
                        if (Mathf.Abs(denom) > 1e-6f)
                        {
                            nodes[nodeCount++] = vx[i] + (yf - vy[i]) / denom * (vx[j] - vx[i]);
                        }
                    }
                    j = i;
                }

                Array.Sort(nodes, 0, nodeCount);
                for (int k = 0; k + 1 < nodeCount; k += 2)
                {
                    int xStart = Mathf.Clamp(Mathf.CeilToInt(nodes[k] - 0.5f), 0, w - 1);
                    int xEnd = Mathf.Clamp(Mathf.FloorToInt(nodes[k + 1] - 0.5f), 0, w - 1);
                    int row = y * w;
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        grid[row + x] = value;
                    }
                }
            }
        }

        private static void NormalizeGrid01(float[] grid)
        {
            float minV = float.MaxValue;
            float maxV = float.MinValue;
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i] < minV) minV = grid[i];
                if (grid[i] > maxV) maxV = grid[i];
            }

            float range = maxV - minV;
            if (range <= 1e-6f)
            {
                return;
            }

            float inv = 1f / range;
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = (grid[i] - minV) * inv;
            }
        }

        private static void EqualizeGrid(float[] grid)
        {
            int n = grid.Length;
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (a, b) => grid[a].CompareTo(grid[b]));

            float denom = Mathf.Max(1f, n - 1);
            int iStart = 0;
            while (iStart < n)
            {
                int iEnd = iStart + 1;
                float val = grid[order[iStart]];
                while (iEnd < n && Mathf.Abs(grid[order[iEnd]] - val) <= 1e-6f)
                {
                    iEnd++;
                }

                float avgRank = 0.5f * (iStart + iEnd - 1);
                float eq = Mathf.Clamp01(avgRank / denom);
                for (int k = iStart; k < iEnd; k++)
                {
                    grid[order[k]] = eq;
                }

                iStart = iEnd;
            }
        }

        private static Texture2D BuildTileTexture(float[] grid, int w, int h, bool flipY)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = flipY ? (h - 1 - y) : y;
                for (int x = 0; x < w; x++)
                {
                    float v = Mathf.Clamp01(grid[sy * w + x]);
                    pixels[y * w + x] = new Color(v, v, v, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}