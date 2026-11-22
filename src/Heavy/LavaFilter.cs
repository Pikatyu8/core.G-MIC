using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class LavaFilter : FilterBase
    {
        private const int PlasmaTextureSize = 513;

        private static readonly GpuStatsOptions HistOptions = new GpuStatsOptions
        {
            RgbMode = false,
            ClipDivisor = 0,
            ZeroBasedCdf = false,
            TemporalAlpha = 0.25f
        };

        private GpuStats _stats;
        private Texture2D _plasmaTex;
        private Texture2D _hotPaletteTex;

        private int _seed = 0x4A7B1C3D;
        private bool _plasmaDirty = true;

        private int _perturbation = 8;
        private float _smoothness = 5f;
        private float _scale = 3f;
        private float _sharpness = 0f;

        public override string Id => "fx_lava";
        public override string DisplayName => "Lava";
        public override string Description => "Generates a fluid, glowing lava-like pattern.";
        public override FilterCategory Category => FilterCategory.Heavy;
        public override bool SuppressFastNoise => true;
        public override bool IsAvailable =>
            base.IsAvailable &&
            _hotPaletteTex != null &&
            _stats != null &&
            _stats.IsAvailable &&
            GpuNorm.IsAvailable &&
            GpuGradientNorm.IsAvailable &&
            GpuAnisoSmooth.IsAvailable;

        public LavaFilter()
        {
            AddParameter(FilterParameter.Button(
                "recompute",
                "Recompute",
                "Roll a new random fractal plasma pattern.",
                Recompute));

            AddParameter(FilterParameter.Int(
                "perturbation",
                "Perturbation",
                "Fractal perturbation frequency (higher = finer plasma cells).",
                0, 15, 8,
                v =>
                {
                    if (_perturbation != v)
                    {
                        _perturbation = v;
                        _plasmaDirty = true;
                    }
                }));

            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Anisotropic flow smoothing amplitude.",
                0f, 100f, 5f,
                v => _smoothness = v,
                step: 0.5f));

            AddParameter(FilterParameter.Float(
                "scale",
                "Scale",
                "Structure tensor scale for directional plasma flow.",
                0f, 20f, 3f,
                v => _scale = v,
                step: 0.25f));

            AddParameter(FilterParameter.Float(
                "sharpness",
                "Sharpness",
                "Laplacian edge sharpening intensity.",
                0f, 1000f, 0f,
                v => _sharpness = v,
                step: 10f));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Lava") ||
                !GpuStats.TryCreate(out _stats))
            {
                return;
            }

            _hotPaletteTex = GmicPaletteLoader.CreatePaletteTexture(GmicPaletteLoader.PaletteHotBase64, 256);
            _plasmaDirty = true;
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            if (_plasmaDirty || _plasmaTex == null)
            {
                int plasmaScale = Mathf.Clamp(14 - _perturbation, 0, 9);
                PlasmaGenerator.CreateOrUpdateGmic(ref _plasmaTex, PlasmaTextureSize, plasmaScale, _seed);
                _plasmaDirty = false;
            }

            int w = source.width;
            int h = source.height;
            RenderTextureFormat floatFmt = GpuUtils.FloatFormat;
            RenderTextureFormat colorFmt = GpuUtils.ColorFormat;

            RenderTexture normRt = null;
            RenderTexture plasmaRt = null;
            RenderTexture smoothPlasmaRt = null;
            RenderTexture gradRt = null;
            RenderTexture mappedRt = null;

            try
            {
                normRt = GpuUtils.GetTempRt(w, h, floatFmt);
                GpuNorm.ComputeNorm(source, normRt);

                Texture activePlasma = _plasmaTex;
                if (_smoothness > 0.05f)
                {
                    plasmaRt = GpuUtils.GetTempRt(PlasmaTextureSize, PlasmaTextureSize, colorFmt);
                    smoothPlasmaRt = GpuUtils.GetTempRt(PlasmaTextureSize, PlasmaTextureSize, colorFmt);
                    Graphics.Blit(_plasmaTex, plasmaRt);
                    GpuAnisoSmooth.Smooth(
                        plasmaRt,
                        smoothPlasmaRt,
                        amplitude: _smoothness,
                        sharpness: 0f,
                        anisotropy: 1f,
                        alpha: _scale,
                        sigma: _scale,
                        dl: 0.8f);
                    activePlasma = smoothPlasmaRt;
                }

                gradRt = GpuUtils.GetTempRt(w, h, floatFmt);
                Material.SetTexture("_PlasmaTex", activePlasma);
                Graphics.Blit(normRt, gradRt, Material, 0);
                GpuGradientNorm.ComputeGradientNorm(gradRt, normRt, scalarMode: true);

                _stats.ComputeHistogramCdf(normRt, HistOptions);
                Material.SetTexture("_CdfTex", _stats.CdfTexture);
                Material.SetTexture("_PaletteTex", _hotPaletteTex);
                Material.SetTexture("_BaseTex", source);

                if (_sharpness > 0.0001f)
                {
                    mappedRt = GpuUtils.GetTempRt(w, h, colorFmt);
                    Graphics.Blit(normRt, mappedRt, Material, 1);

                    Material.SetFloat("_Sharpness", _sharpness);
                    Graphics.Blit(mappedRt, destination, Material, 2);
                }
                else
                {
                    Graphics.Blit(normRt, destination, Material, 1);
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(normRt);
                GpuUtils.ReleaseTemp(plasmaRt);
                GpuUtils.ReleaseTemp(smoothPlasmaRt);
                GpuUtils.ReleaseTemp(gradRt);
                GpuUtils.ReleaseTemp(mappedRt);
            }
        }

        public override void ResetParameters()
        {
            base.ResetParameters();
            _stats?.ResetHistory();
            Recompute();
        }

        public override void Dispose()
        {
            _stats?.Dispose();
            _stats = null;
            GpuUtils.DestroyObject(ref _plasmaTex);
            GpuUtils.DestroyObject(ref _hotPaletteTex);
            base.Dispose();
        }

        private void Recompute()
        {
            _seed = unchecked(_seed * 1664525 + 1013904223);
            if (_seed == 0)
            {
                _seed = 0x4A7B1C3D;
            }
            _plasmaDirty = true;
        }
    }
}