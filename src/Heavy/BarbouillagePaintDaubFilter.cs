using System;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class BarbouillagePaintDaubFilter : FilterBase
    {
        private const int PlasmaTextureSize = 257; // 2^8 + 1 for Diamond-Square

        private static readonly GpuStatsOptions LoopHistOptions = new GpuStatsOptions
        {
            RgbMode = true,
            ClipDivisor = 6,
            ZeroBasedCdf = true,
            TemporalAlpha = 1f
        };

        private static readonly GpuStatsOptions FinalHistOptions = new GpuStatsOptions
        {
            RgbMode = true,
            ClipDivisor = 6,
            ZeroBasedCdf = true,
            TemporalAlpha = 0.15f
        };

        private GpuStats _stats;
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
        public override bool IsAvailable => base.IsAvailable && _stats != null && _stats.IsAvailable;

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
            if (!TryCreateMaterial("CoreGMIC/Barbouillage") ||
                !GpuStats.TryCreate(out _stats))
            {
                return;
            }

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

                    _stats.ComputeHistogramCdf(edgeNext, LoopHistOptions);
                    RenderTexture normOut = (edgeNext == rtA) ? rtB : rtA;
                    Material.SetTexture("_CdfTex", _stats.CdfTexture);
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
                _stats.ComputeHistogramCdf(paintCurr, FinalHistOptions);
                Material.SetTexture("_CdfTex", _stats.CdfTexture);
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

        public override void ResetParameters()
        {
            base.ResetParameters();
            _stats?.ResetHistory();
        }

        public override void Dispose()
        {
            _stats?.Dispose();
            _stats = null;
            GpuUtils.DestroyObject(ref _plasmaTexture);
            base.Dispose();
        }

        private void RebuildPlasmaTexture()
        {
            PlasmaGenerator.CreateOrUpdateLegacy(ref _plasmaTexture, PlasmaTextureSize, _scalePlasma, 1337 + _scalePlasma * 97);
        }
    }
}