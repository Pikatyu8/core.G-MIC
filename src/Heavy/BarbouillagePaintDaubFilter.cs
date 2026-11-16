using System;
using System.Collections.Generic;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class BarbouillagePaintDaubFilter : IFilter
    {
        private const int PlasmaTextureSize = 257; // 2^8 + 1 for Diamond-Square
        private const int HistogramBufferCount = 258; // [0]=min, [1]=max, [2..257]=hist[0..255]

        private Material _material;
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

        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();

        private int _iterations = 2;
        private int _amplitude = 2;
        private int _sharpness = 0;
        private float _anisotropy = 0.0f;
        private float _sigma = 1.0f;
        private float _dl = 3.0f;
        private bool _equalize = true;
        private int _plasmaMode = 0;
        private int _scalePlasma = 8;

        public string Id => "samj_barbouillage_paint_daub";
        public string DisplayName => "Barbouillage Daub";
        public string Description => "Painterly engraving and anisotropic paint-daub stylization (GPU Compute equalize + LIC).";
        public FilterCategory Category => FilterCategory.Heavy;
        public bool IsAvailable => _material != null && _histogramCompute != null && SystemInfo.supportsComputeShaders;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public BarbouillagePaintDaubFilter()
        {
            _parameters.Add(FilterParameter.Int(
                "iterations",
                "Gradient Iterations",
                "Number of gradient norm contour passes.",
                1, 3, 2,
                v => _iterations = v));

            _parameters.Add(FilterParameter.Int(
                "amplitude",
                "Amplitude",
                "Number of anisotropic paint-daub smoothing passes.",
                1, 5, 2,
                v => _amplitude = v));

            _parameters.Add(FilterParameter.Int(
                "sharpness",
                "Sharpness",
                "Edge preservation strength during paint-daub smoothing.",
                0, 500, 0,
                v => _sharpness = v));

            _parameters.Add(FilterParameter.Float(
                "anisotropy",
                "Anisotropy",
                "Directional elongation of brush strokes along edges.",
                0f, 0.5f, 0.0f,
                v => _anisotropy = v));

            _parameters.Add(FilterParameter.Float(
                "sigma",
                "Sigma",
                "Structure tensor smoothing radius.",
                0f, 10f, 1.0f,
                v => _sigma = v));

            _parameters.Add(FilterParameter.Float(
                "dl",
                "Dl",
                "Brush stroke integration step length.",
                0f, 10f, 3.0f,
                v => _dl = v));

            _parameters.Add(FilterParameter.Bool(
                "equalize",
                "Equalize",
                "Apply final histogram equalization after paint smoothing.",
                true,
                v => _equalize = v));

            _parameters.Add(FilterParameter.Choice(
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

            _parameters.Add(FilterParameter.Int(
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

        public void Initialize()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                CoreGMICMod.Instance?.LogWarn("Compute shaders are not supported; BarbouillagePaintDaubFilter disabled.");
                return;
            }

            Shader shader = ShaderManager.GetShader("CoreGMIC/Barbouillage");
            ComputeShader compute = ShaderManager.GetComputeShader("GmicHistogram");
            if (shader == null || compute == null)
            {
                return;
            }

            _material = new Material(shader);
            _histogramCompute = compute;
            _kernelClear = _histogramCompute.FindKernel("CSClear");
            _kernelReduceMinMax = _histogramCompute.FindKernel("CSReduceMinMax");
            _kernelHistogram = _histogramCompute.FindKernel("CSHistogram");
            _kernelBuildCdf = _histogramCompute.FindKernel("CSBuildCdf");

            _statsHistBuffer = new ComputeBuffer(HistogramBufferCount, sizeof(uint));
            _cdfHistoryBuffer = new ComputeBuffer(256, sizeof(float) * 4);
            _hasFinalHistory = false;
            _cdfRt = new RenderTexture(256, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _cdfRt.Create();

            RebuildPlasmaTexture();
        }

        public void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;

            RenderTextureFormat colorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

            RenderTextureFormat tensorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                ? RenderTextureFormat.ARGBFloat
                : colorFormat;

            RenderTexture baseRt = null;
            RenderTexture rtA = null;
            RenderTexture rtB = null;
            RenderTexture tensorA = null;
            RenderTexture tensorB = null;

            try
            {
                rtA = RenderTexture.GetTemporary(w, h, 0, colorFormat, RenderTextureReadWrite.Linear);
                rtB = RenderTexture.GetTemporary(w, h, 0, colorFormat, RenderTextureReadWrite.Linear);
                rtA.filterMode = FilterMode.Bilinear;
                rtB.filterMode = FilterMode.Bilinear;
                rtA.wrapMode = TextureWrapMode.Clamp;
                rtB.wrapMode = TextureWrapMode.Clamp;

                tensorA = RenderTexture.GetTemporary(w, h, 0, tensorFormat, RenderTextureReadWrite.Linear);
                tensorB = RenderTexture.GetTemporary(w, h, 0, tensorFormat, RenderTextureReadWrite.Linear);
                tensorA.filterMode = FilterMode.Bilinear;
                tensorB.filterMode = FilterMode.Bilinear;
                tensorA.wrapMode = TextureWrapMode.Clamp;
                tensorB.wrapMode = TextureWrapMode.Clamp;

                // 1. Optional plasma HSL remix (Pass 0).
                RenderTexture baseSource = source;
                if (_plasmaMode > 0 && _plasmaTexture != null)
                {
                    baseRt = RenderTexture.GetTemporary(w, h, 0, colorFormat, RenderTextureReadWrite.Linear);
                    baseRt.filterMode = FilterMode.Bilinear;
                    baseRt.wrapMode = TextureWrapMode.Clamp;

                    _material.SetTexture("_PlasmaTex", _plasmaTexture);
                    _material.SetInt("_PlasmaMode", _plasmaMode);
                    Graphics.Blit(source, baseRt, _material, 0);
                    baseSource = baseRt;
                }

                // 2. Gradient norm + exact GPU histogram equalize loop.
                RenderTexture edgeCurr = baseSource;
                RenderTexture edgeNext = rtA;
                for (int iter = 0; iter < _iterations; iter++)
                {
                    // Pass 1: gradient_norm
                    Graphics.Blit(edgeCurr, edgeNext, _material, 1);

                    // Compute exact min/max + 256-bin luminance histogram + CDF
                    ComputeHistogramCdf(edgeNext, isFinalPass: false);
                    RenderTexture normOut = (edgeNext == rtA) ? rtB : rtA;
                    _material.SetTexture("_CdfTex", _cdfRt);
                    _material.SetFloat("_DoEqualize", 1f);
                    _material.SetFloat("_FinalPass", 0f);
                    Graphics.Blit(edgeNext, normOut, _material, 2);

                    edgeCurr = normOut;
                    edgeNext = (edgeCurr == rtA) ? rtB : rtA;
                }

                // 3. GPU approximation of engrave_modifie (4x sharpen + directional smooth + threshold & add).
                for (int s = 0; s < 4; s++)
                {
                    Graphics.Blit(edgeCurr, edgeNext, _material, 3);
                    edgeCurr = edgeNext;
                    edgeNext = (edgeCurr == rtA) ? rtB : rtA;
                }

                // Connect stipple into engraving contour lines before thresholding (mirrors engrave_modifie's internal smooth).
                _material.SetFloat("_Sigma", 1.2f);
                _material.SetFloat("_Sharpness", 100f);
                _material.SetFloat("_Anisotropy", 0.45f);
                _material.SetFloat("_Dl", 5.5f);
                Graphics.Blit(edgeCurr, tensorA, _material, 5);
                Graphics.Blit(tensorA, tensorB, _material, 6);
                Graphics.Blit(tensorB, tensorA, _material, 7);
                _material.SetTexture("_TensorTex", tensorA);
                Graphics.Blit(edgeCurr, edgeNext, _material, 8);
                edgeCurr = edgeNext;
                edgeNext = (edgeCurr == rtA) ? rtB : rtA;

                // Pass 4: threshold (>= 60%) + add onto baseSource.
                _material.SetTexture("_BaseTex", baseSource);
                Graphics.Blit(edgeCurr, edgeNext, _material, 4);

                RenderTexture paintCurr = edgeNext;
                RenderTexture paintNext = (paintCurr == rtA) ? rtB : rtA;

                // 4. Anisotropic paint-daub smooth (structure tensor + separable blur + curved LIC passes).
                if (_amplitude > 0 && _dl > 0.01f)
                {
                    _material.SetFloat("_Sigma", _sigma);
                    _material.SetFloat("_Sharpness", _sharpness);
                    _material.SetFloat("_Anisotropy", _anisotropy);
                    _material.SetFloat("_Dl", _dl);

                    for (int passIdx = 0; passIdx < _amplitude; passIdx++)
                    {
                        Graphics.Blit(paintCurr, tensorA, _material, 5);

                        RenderTexture smoothedTensor = tensorA;
                        if (_sigma > 0.05f)
                        {
                            Graphics.Blit(tensorA, tensorB, _material, 6);
                            Graphics.Blit(tensorB, tensorA, _material, 7);
                            smoothedTensor = tensorA;
                        }

                        _material.SetTexture("_TensorTex", smoothedTensor);
                        Graphics.Blit(paintCurr, paintNext, _material, 8);

                        paintCurr = paintNext;
                        paintNext = (paintCurr == rtA) ? rtB : rtA;
                    }
                }

                // 5. Final normalize [0, 1] + optional histogram equalize (Pass 2).
                ComputeHistogramCdf(paintCurr, isFinalPass: true);
                _material.SetTexture("_CdfTex", _cdfRt);
                _material.SetFloat("_DoEqualize", _equalize ? 1f : 0f);
                _material.SetFloat("_FinalPass", 1f);
                Graphics.Blit(paintCurr, destination, _material, 2);
            }
            finally
            {
                ReleaseTemp(baseRt);
                ReleaseTemp(rtA);
                ReleaseTemp(rtB);
                ReleaseTemp(tensorA);
                ReleaseTemp(tensorB);
            }
        }

        public void ResetParameters()
        {
            foreach (FilterParameter param in _parameters)
            {
                param.Reset();
            }
        }

        public void Dispose()
        {
            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
            }

            if (_statsHistBuffer != null)
            {
                _statsHistBuffer.Release();
                _statsHistBuffer = null;
            }

            if (_cdfHistoryBuffer != null)
            {
                _cdfHistoryBuffer.Release();
                _cdfHistoryBuffer = null;
            }
            _hasFinalHistory = false;

            if (_cdfRt != null)
            {
                _cdfRt.Release();
                UnityEngine.Object.Destroy(_cdfRt);
                _cdfRt = null;
            }

            if (_plasmaTexture != null)
            {
                UnityEngine.Object.Destroy(_plasmaTexture);
                _plasmaTexture = null;
            }

            _histogramCompute = null;
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

        private static void ReleaseTemp(RenderTexture rt)
        {
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}