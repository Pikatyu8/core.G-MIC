using System;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class BarbouillagePaintDaubFilter : FilterBase
    {
        private const int PlasmaTextureSize = 257; // 2^8 + 1 for Diamond-Square
        private const int HistogramBufferCount = 258; // [0]=min, [1]=max, [2..257]=hist[0..255]

        private ComputeShader _histogramCompute;
        private int _kernelClear;
        private int _kernelReduceMinMax;
        private int _kernelHistogram;
        private int _kernelBuildCdf;

        private ComputeBuffer _statsHistBuffer;
        private ComputeBuffer _cdfHistoryBuffer;
        private bool _hasFinalHistory;
        private RenderTexture _cdfRt;
        private Texture2D _plasmaTexture;

        private int _iterations = 2;
        private int _amplitude = 2;
        private int _sharpness = 0;
        private float _anisotropy = 0.0f;
        private float _sigma = 1.0f;
        private float _dl = 3.0f;
        private bool _equalize = true;
        private int _plasmaMode = 0;
        private int _scalePlasma = 8;

        public override string Id => "samj_barbouillage_paint_daub";
        public override string DisplayName => "Barbouillage Daub";
        public override string Description => "Painterly engraving and anisotropic paint-daub stylization (GPU Compute equalize + LIC).";
        public override FilterCategory Category => FilterCategory.Heavy;
        public override bool IsAvailable => base.IsAvailable && _histogramCompute != null && SystemInfo.supportsComputeShaders;

        public BarbouillagePaintDaubFilter()
        {
            AddParameter(FilterParameter.Int(
                "iterations",
                "Gradient Iterations",
                "Number of gradient norm contour passes.",
                1, 3, 2,
                v => _iterations = v));

            AddParameter(FilterParameter.Int(
                "amplitude",
                "Amplitude",
                "Number of anisotropic paint-daub smoothing passes.",
                1, 5, 2,
                v => _amplitude = v));

            AddParameter(FilterParameter.Int(
                "sharpness",
                "Sharpness",
                "Edge preservation strength during paint-daub smoothing.",
                0, 500, 0,
                v => _sharpness = v));

            AddParameter(FilterParameter.Float(
                "anisotropy",
                "Anisotropy",
                "Directional elongation of brush strokes along edges.",
                0f, 0.5f, 0.0f,
                v => _anisotropy = v));

            AddParameter(FilterParameter.Float(
                "sigma",
                "Sigma",
                "Structure tensor smoothing radius.",
                0f, 10f, 1.0f,
                v => _sigma = v));

            AddParameter(FilterParameter.Float(
                "dl",
                "Dl",
                "Brush stroke integration step length.",
                0f, 10f, 3.0f,
                v => _dl = v));

            AddParameter(FilterParameter.Bool(
                "equalize",
                "Equalize",
                "Apply final histogram equalization after paint smoothing.",
                true,
                v => _equalize = v));

            AddParameter(FilterParameter.Choice(
                "plasma",
                "Plasma",
                "Optional procedural plasma HSL channel remix.",
                new[] { "Without", "Couleurs A", "Couleurs B" },
                0,
                v =>
                {
                    _plasmaMode = v;
                    if (_plasmaMode > 0 && _plasmaTexture == null)
                    {
                        RebuildPlasmaTexture();
                    }
                }));

            AddParameter(FilterParameter.Int(
                "scale_plasma",
                "Scale Plasma",
                "Fractal roughness scale for the plasma texture.",
                1, 30, 8,
                v =>
                {
                    _scalePlasma = v;
                    if (_plasmaTexture != null)
                    {
                        RebuildPlasmaTexture();
                    }
                },
                isVisible: () => _plasmaMode > 0));
        }

        public override void Initialize()
        {
            if (!TryLoadComputeShader("GmicHistogram", out _histogramCompute) ||
                !TryCreateMaterial("CoreGMIC/Barbouillage"))
            {
                _histogramCompute = null;
                return;
            }

            _kernelClear = _histogramCompute.FindKernel("CSClear");
            _kernelReduceMinMax = _histogramCompute.FindKernel("CSReduceMinMax");
            _kernelHistogram = _histogramCompute.FindKernel("CSHistogram");
            _kernelBuildCdf = _histogramCompute.FindKernel("CSBuildCdf");

            _statsHistBuffer = new ComputeBuffer(HistogramBufferCount, sizeof(uint));
            _cdfHistoryBuffer = new ComputeBuffer(256, sizeof(float) * 4);
            _hasFinalHistory = false;
            _cdfRt = GpuUtils.CreateRt(256, 1, RenderTextureFormat.ARGBFloat, FilterMode.Bilinear, RenderTextureReadWrite.Linear, randomWrite: true);

            RebuildPlasmaTexture();
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat tensorFormat = GpuUtils.FloatFormat;

            RenderTexture baseRt = null;
            RenderTexture rtA = null;
            RenderTexture rtB = null;
            RenderTexture tensorA = null;
            RenderTexture tensorB = null;

            try
            {
                rtA = GpuUtils.GetTempRt(w, h, colorFormat);
                rtB = GpuUtils.GetTempRt(w, h, colorFormat);
                tensorA = GpuUtils.GetTempRt(w, h, tensorFormat);
                tensorB = GpuUtils.GetTempRt(w, h, tensorFormat);

                // 1. Optional plasma HSL remix (Pass 0).
                RenderTexture baseSource = source;
                if (_plasmaMode > 0 && _plasmaTexture != null)
                {
                    baseRt = GpuUtils.GetTempRt(w, h, colorFormat);
                    Material.SetTexture("_PlasmaTex", _plasmaTexture);
                    Material.SetInt("_PlasmaMode", _plasmaMode);
                    Graphics.Blit(source, baseRt, Material, 0);
                    baseSource = baseRt;
                }

                // 2. Gradient norm + exact GPU histogram equalize loop.
                RenderTexture edgeCurr = baseSource;
                RenderTexture edgeNext = rtA;
                for (int iter = 0; iter < _iterations; iter++)
                {
                    Graphics.Blit(edgeCurr, edgeNext, Material, 1);

                    ComputeHistogramCdf(edgeNext, isFinalPass: false);
                    RenderTexture normOut = (edgeNext == rtA) ? rtB : rtA;
                    Material.SetTexture("_CdfTex", _cdfRt);
                    Material.SetFloat("_DoEqualize", 1f);
                    Material.SetFloat("_FinalPass", 0f);
                    Graphics.Blit(edgeNext, normOut, Material, 2);

                    edgeCurr = normOut;
                    edgeNext = (edgeCurr == rtA) ? rtB : rtA;
                }

                // 3. GPU approximation of engrave_modifie (4x sharpen + directional smooth + threshold & add).
                for (int s = 0; s < 4; s++)
                {
                    Graphics.Blit(edgeCurr, edgeNext, Material, 3);
                    edgeCurr = edgeNext;
                    edgeNext = (edgeCurr == rtA) ? rtB : rtA;
                }

                Material.SetFloat("_Sigma", 1.2f);
                Material.SetFloat("_Sharpness", 100f);
                Material.SetFloat("_Anisotropy", 0.45f);
                Material.SetFloat("_Dl", 5.5f);
                Graphics.Blit(edgeCurr, tensorA, Material, 5);
                Graphics.Blit(tensorA, tensorB, Material, 6);
                Graphics.Blit(tensorB, tensorA, Material, 7);
                Material.SetTexture("_TensorTex", tensorA);
                Graphics.Blit(edgeCurr, edgeNext, Material, 8);
                edgeCurr = edgeNext;
                edgeNext = (edgeCurr == rtA) ? rtB : rtA;

                Material.SetTexture("_BaseTex", baseSource);
                Graphics.Blit(edgeCurr, edgeNext, Material, 4);

                RenderTexture paintCurr = edgeNext;
                RenderTexture paintNext = (paintCurr == rtA) ? rtB : rtA;

                // 4. Anisotropic paint-daub smooth (structure tensor + separable blur + curved LIC passes).
                if (_amplitude > 0 && _dl > 0.01f)
                {
                    Material.SetFloat("_Sigma", _sigma);
                    Material.SetFloat("_Sharpness", _sharpness);
                    Material.SetFloat("_Anisotropy", _anisotropy);
                    Material.SetFloat("_Dl", _dl);

                    for (int passIdx = 0; passIdx < _amplitude; passIdx++)
                    {
                        Graphics.Blit(paintCurr, tensorA, Material, 5);

                        RenderTexture smoothedTensor = tensorA;
                        if (_sigma > 0.05f)
                        {
                            Graphics.Blit(tensorA, tensorB, Material, 6);
                            Graphics.Blit(tensorB, tensorA, Material, 7);
                            smoothedTensor = tensorA;
                        }

                        Material.SetTexture("_TensorTex", smoothedTensor);
                        Graphics.Blit(paintCurr, paintNext, Material, 8);

                        paintCurr = paintNext;
                        paintNext = (paintCurr == rtA) ? rtB : rtA;
                    }
                }

                // 5. Final normalize [0, 1] + optional histogram equalize (Pass 2).
                ComputeHistogramCdf(paintCurr, isFinalPass: true);
                Material.SetTexture("_CdfTex", _cdfRt);
                Material.SetFloat("_DoEqualize", _equalize ? 1f : 0f);
                Material.SetFloat("_FinalPass", 1f);
                Graphics.Blit(paintCurr, destination, Material, 2);
            }
            finally
            {
                GpuUtils.ReleaseTemp(baseRt);
                GpuUtils.ReleaseTemp(rtA);
                GpuUtils.ReleaseTemp(rtB);
                GpuUtils.ReleaseTemp(tensorA);
                GpuUtils.ReleaseTemp(tensorB);
            }
        }

        public override void Dispose()
        {
            GpuUtils.ReleaseBuffer(ref _statsHistBuffer);
            GpuUtils.ReleaseBuffer(ref _cdfHistoryBuffer);
            _hasFinalHistory = false;
            GpuUtils.ReleaseRt(ref _cdfRt);
            GpuUtils.DestroyObject(ref _plasmaTexture);
            _histogramCompute = null;
            base.Dispose();
        }

        private void ComputeHistogramCdf(RenderTexture input, bool isFinalPass)
        {
            int w = input.width;
            int h = input.height;
            int groupsX = (w + 15) / 16;
            int groupsY = (h + 15) / 16;

            _histogramCompute.SetInt("_Width", w);
            _histogramCompute.SetInt("_Height", h);
            _histogramCompute.SetInt("_IsFinalPass", isFinalPass ? 1 : 0);
            _histogramCompute.SetInt("_ResetHistory", !_hasFinalHistory ? 1 : 0);
            _histogramCompute.SetFloat("_TemporalAlpha", 0.15f);

            _histogramCompute.SetBuffer(_kernelClear, "_StatsAndHist", _statsHistBuffer);
            _histogramCompute.Dispatch(_kernelClear, 2, 1, 1);

            _histogramCompute.SetTexture(_kernelReduceMinMax, "_InputTex", input);
            _histogramCompute.SetBuffer(_kernelReduceMinMax, "_StatsAndHist", _statsHistBuffer);
            _histogramCompute.Dispatch(_kernelReduceMinMax, groupsX, groupsY, 1);

            _histogramCompute.SetTexture(_kernelHistogram, "_InputTex", input);
            _histogramCompute.SetBuffer(_kernelHistogram, "_StatsAndHist", _statsHistBuffer);
            _histogramCompute.Dispatch(_kernelHistogram, groupsX, groupsY, 1);

            _histogramCompute.SetBuffer(_kernelBuildCdf, "_StatsAndHist", _statsHistBuffer);
            _histogramCompute.SetBuffer(_kernelBuildCdf, "_CdfHistory", _cdfHistoryBuffer);
            _histogramCompute.SetTexture(_kernelBuildCdf, "_CdfTex", _cdfRt);
            _histogramCompute.Dispatch(_kernelBuildCdf, 1, 1, 1);

            if (isFinalPass)
            {
                _hasFinalHistory = true;
            }
        }

        private void RebuildPlasmaTexture()
        {
            if (_plasmaTexture == null)
            {
                _plasmaTexture = new Texture2D(PlasmaTextureSize, PlasmaTextureSize, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            int n = PlasmaTextureSize;
            int maxStep = n - 1;
            float[] r = new float[n * n];
            float[] g = new float[n * n];
            float[] b = new float[n * n];

            int initialDiv = Mathf.Clamp(Mathf.RoundToInt(_scalePlasma / 4f), 1, 8);
            int initialStep = Mathf.Max(4, maxStep >> (initialDiv - 1));
            float roughness = Mathf.Lerp(0.45f, 0.85f, (_scalePlasma - 1f) / 29f);

            System.Random rng = new System.Random(1337 + _scalePlasma * 97);

            for (int y = 0; y < n; y += initialStep)
            {
                for (int x = 0; x < n; x += initialStep)
                {
                    int idx = y * n + x;
                    r[idx] = (float)rng.NextDouble();
                    g[idx] = (float)rng.NextDouble();
                    b[idx] = (float)rng.NextDouble();
                }
            }

            RunDiamondSquare(r, n, initialStep, roughness, rng);
            RunDiamondSquare(g, n, initialStep, roughness, rng);
            RunDiamondSquare(b, n, initialStep, roughness, rng);

            float minVal = float.MaxValue;
            float maxVal = float.MinValue;
            for (int i = 0; i < n * n; i++)
            {
                if (r[i] < minVal) minVal = r[i];
                if (g[i] < minVal) minVal = g[i];
                if (b[i] < minVal) minVal = b[i];
                if (r[i] > maxVal) maxVal = r[i];
                if (g[i] > maxVal) maxVal = g[i];
                if (b[i] > maxVal) maxVal = b[i];
            }

            float invRange = (maxVal - minVal) > 1e-5f ? 1f / (maxVal - minVal) : 1f;
            Color[] pixels = new Color[n * n];
            for (int i = 0; i < n * n; i++)
            {
                pixels[i] = new Color(
                    Mathf.Clamp01((r[i] - minVal) * invRange),
                    Mathf.Clamp01((g[i] - minVal) * invRange),
                    Mathf.Clamp01((b[i] - minVal) * invRange),
                    1f);
            }

            _plasmaTexture.SetPixels(pixels);
            _plasmaTexture.Apply(false, false);
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