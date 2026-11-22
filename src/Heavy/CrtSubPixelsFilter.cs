using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class CrtSubPixelsFilter : FilterBase
    {
        private const int ExpandPad = 12;
        private const int MeanCurvatureIters = 4;

        private static readonly GpuStatsOptions EqualizeOptions = new GpuStatsOptions
        {
            RgbMode = true,
            ClipDivisor = 0,
            ZeroBasedCdf = true,
            TemporalAlpha = 0.2f
        };

        private GpuStats _stats;

        private float _horizBlur = 1.8f;
        private float _vertBlur = 1.8f;
        private bool _screenBorder = false;
        private bool _equalize = false;

        public override string Id => "fx_gcd_crt";
        public override string DisplayName => "CRT Sub-Pixels";
        public override string Description => "Cathode ray tube sub-pixel phosphor rendering filter.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable =>
            base.IsAvailable &&
            GpuBlur.IsAvailable &&
            GpuNormalize.IsAvailable &&
            _stats != null &&
            _stats.IsAvailable;

        public CrtSubPixelsFilter()
        {
            AddParameter(FilterParameter.Float(
                "horiz_blur",
                "Horizontal Blur",
                "Horizontal phosphor bleed blur sigma.",
                0f, 5f, 1.8f,
                v => _horizBlur = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "vert_blur",
                "Vertical Blur",
                "Vertical phosphor bleed blur sigma.",
                0f, 5f, 1.8f,
                v => _vertBlur = v,
                step: 0.1f));

            AddParameter(FilterParameter.Bool(
                "screen_border",
                "Screen Border",
                "Apply a soft CRT screen edge vignette.",
                false,
                v => _screenBorder = v));

            AddParameter(FilterParameter.Bool(
                "equalize",
                "Equalize",
                "Apply 256-bin histogram equalization after sub-pixel rendering.",
                false,
                v => _equalize = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/CrtSubPixels"))
            {
                return;
            }

            GpuStats.TryCreate(out _stats);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            int expW = w + ExpandPad * 2;
            int expH = h + ExpandPad * 2;

            RenderTextureFormat floatFmt = GpuUtils.FloatFormat;
            RenderTextureFormat lumFmt = GpuUtils.LuminanceFormat;
            RenderTextureFormat colorFmt = GpuUtils.ColorFormat;

            RenderTexture expA = null;
            RenderTexture expB = null;
            RenderTexture ieeRt = null;
            RenderTexture absIeeRt = null;
            RenderTexture borderRt = null;
            RenderTexture croppedRt = null;
            RenderTexture normRt = null;

            try
            {
                expA = GpuUtils.GetTempRt(expW, expH, floatFmt, FilterMode.Bilinear);
                expB = GpuUtils.GetTempRt(expW, expH, floatFmt, FilterMode.Bilinear);
                ieeRt = GpuUtils.GetTempRt(expW, expH, floatFmt, FilterMode.Point);
                absIeeRt = GpuUtils.GetTempRt(expW, expH, lumFmt, FilterMode.Point);

                // 1. Expand xy,12 + 24x14 RGB sub-pixel lattice + shapeaverage0
                Material.SetVector("_SrcSize", new Vector4(w, h, expW, expH));
                Graphics.Blit(source, expA, Material, 0);

                // 2. Normalize [0, 255]
                GpuNormalize.Normalize(expA, expB, rgbMode: true, stats: _stats);
                RenderTexture curr = expB;
                RenderTexture NextExp(RenderTexture c) => (c == expA) ? expB : expA;

                // 3. meancurvature_flow 4 (dt = 30)
                for (int it = 0; it < MeanCurvatureIters; it++)
                {
                    Graphics.Blit(curr, ieeRt, Material, 1);
                    Graphics.Blit(ieeRt, absIeeRt, Material, 2);
                    _stats.ComputeMinMax(absIeeRt, rgbMode: false);

                    RenderTexture next = NextExp(curr);
                    Material.SetTexture("_IeeTex", ieeRt);
                    Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                    Graphics.Blit(curr, next, Material, 3);
                    curr = next;
                }

                // 4. Optional CRT screen border vignette
                if (_screenBorder)
                {
                    borderRt = GpuUtils.GetTempRt(expW, expH, lumFmt, FilterMode.Bilinear);
                    Graphics.Blit(curr, borderRt, Material, 4);
                    GpuBlur.BlurInPlace(borderRt, 0.10f * Mathf.Max(expW, expH));

                    RenderTexture next = NextExp(curr);
                    Material.SetTexture("_BorderTex", borderRt);
                    Graphics.Blit(curr, next, Material, 5);
                    curr = next;
                }

                // 5. Separable X/Y phosphor blur (Deriche sigma approximation)
                if (_horizBlur > 0.05f || _vertBlur > 0.05f)
                {
                    RenderTexture next = NextExp(curr);
                    GpuBlur.BlurSeparable(curr, next, _horizBlur, _vertBlur);
                    curr = next;
                }

                // 6. Shrink xy,12 -> Normalize [0, 255] -> Optional Equalize 256
                croppedRt = GpuUtils.GetTempRt(w, h, colorFmt, FilterMode.Point);
                Material.SetVector("_SrcSize", new Vector4(w, h, expW, expH));
                Material.SetTexture("_BaseTex", source);
                Graphics.Blit(curr, croppedRt, Material, 6);

                if (_equalize)
                {
                    normRt = GpuUtils.GetTempRt(w, h, colorFmt, FilterMode.Point);
                    GpuNormalize.Normalize(croppedRt, normRt, preserveAlphaFrom: source, rgbMode: true, stats: _stats);
                    _stats.ComputeHistogramCdf(normRt, EqualizeOptions);
                    Material.SetTexture("_CdfTex", _stats.CdfTexture);
                    Graphics.Blit(normRt, destination, Material, 7);
                }
                else
                {
                    GpuNormalize.Normalize(croppedRt, destination, preserveAlphaFrom: source, rgbMode: true, stats: _stats);
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(expA);
                GpuUtils.ReleaseTemp(expB);
                GpuUtils.ReleaseTemp(ieeRt);
                GpuUtils.ReleaseTemp(absIeeRt);
                GpuUtils.ReleaseTemp(borderRt);
                GpuUtils.ReleaseTemp(croppedRt);
                GpuUtils.ReleaseTemp(normRt);
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
            base.Dispose();
        }
    }
}