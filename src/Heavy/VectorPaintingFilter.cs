using System.Collections.Generic;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class VectorPaintingFilter : IFilter
    {
        private const float MaxSigmaPerPass = 3.3f;
        private const float ReconstructSigma = 2.2f;

        private Material _material;
        private ComputeShader _compute;

        private int _kernelCCLInit;
        private int _kernelCCLMergeBorders;
        private int _kernelCCLFlattenAndAccum;

        private ComputeBuffer _parentBuffer;
        private ComputeBuffer _shapeSumBuffer;
        private RenderTexture _prevSourceRt;
        private bool _hasPrevSource;
        private int _allocW = -1;
        private int _allocH = -1;
        private RenderTextureFormat _allocSourceFormat;

        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();
        private float _details = 9.0f;
        private float _colorThreshold = 0.0f;
        private float _minAlpha = 0.15f;

        public string Id => "fx_vector_painting";
        public string DisplayName => "Vector Painting";
        public string Description => "Simulates a vector art style with clean areas of color.";
        public FilterCategory Category => FilterCategory.Experimental;
        public bool IsAvailable => _material != null && _compute != null && SystemInfo.supportsComputeShaders;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public VectorPaintingFilter()
        {
            _parameters.Add(FilterParameter.Float(
                "details",
                "Details",
                "Controls vector region granularity (higher = finer shapes).",
                7.0f, 10.0f, 9.0f,
                v => _details = v,
                step: 0.05f));

            _parameters.Add(FilterParameter.Float(
                "color_threshold",
                "Stability Threshold",
                "Coherent motion threshold separating real motion from frame noise.",
                0.0f, 0.2f, 0.0f,
                v => _colorThreshold = v,
                step: 0.002f));

            _parameters.Add(FilterParameter.Float(
                "min_alpha",
                "Trail Fade Speed",
                "Convergence speed in static areas (higher = faster trail fade, lower = smoother).",
                0.02f, 1.0f, 0.15f,
                v => _minAlpha = v,
                step: 0.01f));
        }

        public void Initialize()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                CoreGMICMod.Instance?.LogWarn("Compute shaders are not supported; VectorPaintingFilter disabled.");
                return;
            }

            Shader shader = ShaderManager.GetShader("CoreGMIC/VectorPainting");
            ComputeShader compute = ShaderManager.GetComputeShader("GmicVectorPainting");
            if (shader == null || compute == null)
            {
                return;
            }

            _material = new Material(shader);
            _compute = compute;
            _kernelCCLInit = _compute.FindKernel("CSCCLInit");
            _kernelCCLMergeBorders = _compute.FindKernel("CSCCLMergeBorders");
            _kernelCCLFlattenAndAccum = _compute.FindKernel("CSCCLFlattenAndAccum");
        }

        public void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;

            RenderTextureFormat lumFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat
                : (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                    ? RenderTextureFormat.ARGBFloat
                    : RenderTextureFormat.ARGBHalf);

            RenderTextureFormat labelFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

            RenderTextureFormat stableFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : source.format;

            EnsureBuffers(w, h, stableFormat);

            RenderTexture stableSource = null;
            RenderTexture lumFull = null;
            RenderTexture lumScratch = null;
            RenderTexture halfA = null;
            RenderTexture halfB = null;
            RenderTexture quarterA = null;
            RenderTexture quarterB = null;
            RenderTexture eighthA = null;
            RenderTexture eighthB = null;
            RenderTexture labelRt = null;
            RenderTexture labelCleanRt = null;

            try
            {
                // Soft adaptive IIR in ARGBHalf eliminates 8-bit quantization trails and grain flicker.
                RenderTexture activeSource = source;
                if (_colorThreshold > 0.0001f)
                {
                    if (_hasPrevSource)
                    {
                        stableSource = GetTempRt(w, h, stableFormat, FilterMode.Point);

                        _material.SetTexture("_PrevSourceTex", _prevSourceRt);
                        _material.SetFloat("_ColorThreshold", _colorThreshold);
                        _material.SetFloat("_MinAlpha", _minAlpha);
                        Graphics.Blit(source, stableSource, _material, 6);
                        activeSource = stableSource;
                    }

                    Graphics.Blit(activeSource, _prevSourceRt);
                    _hasPrevSource = true;
                }
                else
                {
                    _hasPrevSource = false;
                }

                lumFull = GetTempRt(w, h, lumFormat, FilterMode.Bilinear);
                Graphics.Blit(activeSource, lumFull, _material, 0);

                float sigmaFull = (10f - _details) * 0.01f * Mathf.Max(w, h);
                if (sigmaFull > 0.05f)
                {
                    lumScratch = GetTempRt(w, h, lumFormat, FilterMode.Bilinear);

                    if (sigmaFull <= 6.0f)
                    {
                        RunMultiPassGaussian(lumFull, lumScratch, sigmaFull);
                    }
                    else
                    {
                        int hw = Mathf.Max(1, w / 2);
                        int hh = Mathf.Max(1, h / 2);
                        halfA = GetTempRt(hw, hh, lumFormat, FilterMode.Bilinear);
                        halfB = GetTempRt(hw, hh, lumFormat, FilterMode.Bilinear);
                        Graphics.Blit(lumFull, halfA);

                        if (sigmaFull <= 14.0f)
                        {
                            float remVar = Mathf.Max(0.25f, sigmaFull * sigmaFull - ReconstructSigma * ReconstructSigma);
                            float sigmaHalf = Mathf.Sqrt(remVar) * 0.5f;
                            RunMultiPassGaussian(halfA, halfB, sigmaHalf);
                        }
                        else
                        {
                            int qw = Mathf.Max(1, w / 4);
                            int qh = Mathf.Max(1, h / 4);
                            quarterA = GetTempRt(qw, qh, lumFormat, FilterMode.Bilinear);
                            quarterB = GetTempRt(qw, qh, lumFormat, FilterMode.Bilinear);
                            Graphics.Blit(halfA, quarterA);

                            if (sigmaFull <= 28.0f)
                            {
                                float reconVar = ReconstructSigma * ReconstructSigma * (1f + 4f);
                                float remVar = Mathf.Max(0.25f, sigmaFull * sigmaFull - reconVar);
                                float sigmaQ = Mathf.Sqrt(remVar) * 0.25f;
                                RunMultiPassGaussian(quarterA, quarterB, sigmaQ);
                            }
                            else
                            {
                                int ew = Mathf.Max(1, w / 8);
                                int eh = Mathf.Max(1, h / 8);
                                eighthA = GetTempRt(ew, eh, lumFormat, FilterMode.Bilinear);
                                eighthB = GetTempRt(ew, eh, lumFormat, FilterMode.Bilinear);
                                Graphics.Blit(quarterA, eighthA);

                                float reconVar = ReconstructSigma * ReconstructSigma * (1f + 4f + 16f);
                                float remVar = Mathf.Max(0.25f, sigmaFull * sigmaFull - reconVar);
                                float sigmaE = Mathf.Sqrt(remVar) * 0.125f;
                                RunMultiPassGaussian(eighthA, eighthB, sigmaE);

                                Graphics.Blit(eighthA, quarterA);
                                RunSingleGaussian(quarterA, quarterB, ReconstructSigma);
                            }

                            Graphics.Blit(quarterA, halfA);
                            RunSingleGaussian(halfA, halfB, ReconstructSigma);
                        }

                        Graphics.Blit(halfA, lumFull);
                        RunSingleGaussian(lumFull, lumScratch, ReconstructSigma);
                    }
                }

                // Classify gradient direction and smooth out 1px bridges/speckles with two 3x3 mode passes.
                labelRt = GetTempRt(w, h, labelFormat, FilterMode.Point);
                labelCleanRt = GetTempRt(w, h, labelFormat, FilterMode.Point);

                Graphics.Blit(lumFull, labelRt, _material, 3);
                Graphics.Blit(labelRt, labelCleanRt, _material, 5);
                Graphics.Blit(labelCleanRt, labelRt, _material, 5);

                int groupsX = (w + 15) / 16;
                int groupsY = (h + 15) / 16;

                _compute.SetInt("_Width", w);
                _compute.SetInt("_Height", h);

                _compute.SetTexture(_kernelCCLInit, "_LabelTex", labelRt);
                _compute.SetBuffer(_kernelCCLInit, "_ParentBuffer", _parentBuffer);
                _compute.SetBuffer(_kernelCCLInit, "_ShapeSumBuffer", _shapeSumBuffer);
                _compute.Dispatch(_kernelCCLInit, groupsX, groupsY, 1);

                _compute.SetTexture(_kernelCCLMergeBorders, "_LabelTex", labelRt);
                _compute.SetBuffer(_kernelCCLMergeBorders, "_ParentBuffer", _parentBuffer);
                _compute.Dispatch(_kernelCCLMergeBorders, groupsX, groupsY, 1);
                _compute.Dispatch(_kernelCCLMergeBorders, groupsX, groupsY, 1);

                _compute.SetTexture(_kernelCCLFlattenAndAccum, "_SourceTex", activeSource);
                _compute.SetBuffer(_kernelCCLFlattenAndAccum, "_ParentBuffer", _parentBuffer);
                _compute.SetBuffer(_kernelCCLFlattenAndAccum, "_ShapeSumBuffer", _shapeSumBuffer);
                _compute.Dispatch(_kernelCCLFlattenAndAccum, groupsX, groupsY, 1);

                _material.SetInt("_Width", w);
                _material.SetInt("_Height", h);
                _material.SetBuffer("_ParentBuffer", _parentBuffer);
                _material.SetBuffer("_ShapeSumBuffer", _shapeSumBuffer);
                Graphics.Blit(activeSource, destination, _material, 4);
            }
            finally
            {
                ReleaseTemp(stableSource);
                ReleaseTemp(lumFull);
                ReleaseTemp(lumScratch);
                ReleaseTemp(halfA);
                ReleaseTemp(halfB);
                ReleaseTemp(quarterA);
                ReleaseTemp(quarterB);
                ReleaseTemp(eighthA);
                ReleaseTemp(eighthB);
                ReleaseTemp(labelRt);
                ReleaseTemp(labelCleanRt);
            }
        }

        public void ResetParameters()
        {
            foreach (FilterParameter param in _parameters)
            {
                param.Reset();
            }
            _hasPrevSource = false;
        }

        public void Dispose()
        {
            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }

            ReleaseRt(ref _prevSourceRt);
            ReleaseBuffer(ref _parentBuffer);
            ReleaseBuffer(ref _shapeSumBuffer);
            _allocW = -1;
            _allocH = -1;
            _hasPrevSource = false;
            _compute = null;
        }

        private void RunMultiPassGaussian(RenderTexture target, RenderTexture scratch, float totalSigma)
        {
            int passes = Mathf.Max(1, Mathf.CeilToInt((totalSigma * totalSigma) / (MaxSigmaPerPass * MaxSigmaPerPass)));
            float sigmaPerPass = totalSigma / Mathf.Sqrt(passes);
            for (int p = 0; p < passes; p++)
            {
                RunSingleGaussian(target, scratch, sigmaPerPass);
            }
        }

        private void RunSingleGaussian(RenderTexture target, RenderTexture scratch, float sigma)
        {
            _material.SetFloat("_SigmaPx", sigma);
            Graphics.Blit(target, scratch, _material, 1);
            Graphics.Blit(scratch, target, _material, 2);
        }

        private void EnsureBuffers(int w, int h, RenderTextureFormat stableFormat)
        {
            if (_parentBuffer != null && _shapeSumBuffer != null && _prevSourceRt != null &&
                _allocW == w && _allocH == h && _allocSourceFormat == stableFormat)
            {
                return;
            }

            ReleaseBuffer(ref _parentBuffer);
            ReleaseBuffer(ref _shapeSumBuffer);
            ReleaseRt(ref _prevSourceRt);

            int totalPixels = Mathf.Max(1, w * h);
            _parentBuffer = new ComputeBuffer(totalPixels, sizeof(uint));
            _shapeSumBuffer = new ComputeBuffer(totalPixels * 4, sizeof(uint));

            RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, stableFormat, 0)
            {
                sRGB = false
            };
            _prevSourceRt = new RenderTexture(desc)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _prevSourceRt.Create();

            _allocW = w;
            _allocH = h;
            _allocSourceFormat = stableFormat;
            _hasPrevSource = false;
        }

        private static RenderTexture GetTempRt(int width, int height, RenderTextureFormat format, FilterMode filterMode)
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height, format, 0)
            {
                sRGB = false
            };
            RenderTexture rt = RenderTexture.GetTemporary(desc);
            rt.filterMode = filterMode;
            rt.wrapMode = TextureWrapMode.Clamp;
            return rt;
        }

        private static void ReleaseTemp(RenderTexture rt)
        {
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void ReleaseRt(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
                rt = null;
            }
        }

        private static void ReleaseBuffer(ref ComputeBuffer buf)
        {
            if (buf != null)
            {
                buf.Release();
                buf = null;
            }
        }
    }
}