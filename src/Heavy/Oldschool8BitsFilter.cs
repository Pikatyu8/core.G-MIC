using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class Oldschool8BitsFilter : FilterBase
    {
        private const float TemporalAlpha = 0.2f;

        private GpuStats _stats;
        private GpuShapeAverage _shapeAvg;

        private float _scale = 25f;
        private float _dithering = 800f;
        private int _levels = 16;

        public override string Id => "fx_8bits";
        public override string DisplayName => "Oldschool 8bits";
        public override string Description => "Reduces color depth to simulate 8-bit graphics.";
        public override FilterCategory Category => FilterCategory.Experimental;
        public override bool IsAvailable =>
            base.IsAvailable && _stats != null && _stats.IsAvailable && _shapeAvg != null && _shapeAvg.IsAvailable;
        public override bool SuppressFastNoise => true;

        public Oldschool8BitsFilter()
        {
            AddParameter(FilterParameter.Float(
                "scale",
                "Scale",
                "Internal downscale percentage before quantization.",
                1f, 100f, 25f,
                v => _scale = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "dithering",
                "Dithering",
                "Laplacian sharpen amplitude driving Otsu pattern dithering.",
                0f, 10000f, 800f,
                v => _dithering = v,
                step: 50f));

            AddParameter(FilterParameter.Int(
                "levels",
                "Levels",
                "Number of color quantization levels per RGB channel.",
                2, 256, 16,
                v => _levels = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Oldschool8Bits"))
            {
                return;
            }

            GpuStats.TryCreate(out _stats);
            GpuShapeAverage.TryCreate(out _shapeAvg);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            int sw = Mathf.Max(1, Mathf.RoundToInt(w * (_scale * 0.01f)));
            int sh = Mathf.Max(1, Mathf.RoundToInt(h * (_scale * 0.01f)));

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat floatFormat = GpuUtils.FloatFormat;

            RenderTexture scaledRt = null;
            RenderTexture lumRt = null;
            RenderTexture sharpRt = null;
            RenderTexture maskRt = null;
            RenderTexture shapeRt = null;

            try
            {
                // 1. Downscale RGB (r $1%,$1%,1,100%,2)
                scaledRt = GpuUtils.GetTempRt(sw, sh, colorFormat, FilterMode.Bilinear);
                Graphics.Blit(source, scaledRt);

                // 2. Luminance + Laplacian Sharpen
                lumRt = GpuUtils.GetTempRt(sw, sh, floatFormat, FilterMode.Point);
                Graphics.Blit(scaledRt, lumRt, Material, 0);

                RenderTexture otsuInput = lumRt;
                if (_dithering > 0.0001f)
                {
                    _stats.ComputeMinMax(lumRt, rgbMode: false);
                    sharpRt = GpuUtils.GetTempRt(sw, sh, floatFormat, FilterMode.Point);
                    Material.SetFloat("_Dithering", _dithering);
                    Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                    Graphics.Blit(lumRt, sharpRt, Material, 1);
                    otsuInput = sharpRt;
                }

                // 3. Otsu 256 binary threshold (with temporal smoothing)
                _stats.ComputeOtsuThreshold(otsuInput, rgbMode: false, temporalAlpha: TemporalAlpha);
                maskRt = GpuUtils.GetTempRt(sw, sh, floatFormat, FilterMode.Point);
                Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                Graphics.Blit(otsuInput, maskRt, Material, 2);

                // 4. 4-connected CCL + shapeaverage0 (3 merge+compress passes for dithered mazes)
                shapeRt = GpuUtils.GetTempRt(sw, sh, colorFormat, FilterMode.Point);
                _shapeAvg.Apply(maskRt, scaledRt, shapeRt, mergePasses: 3);

                // 5. Per-channel uniform quantize + nearest upscale + original alpha
                _stats.ComputeChannelMinMax(shapeRt, temporalAlpha: TemporalAlpha);
                Material.SetBuffer("_ChannelStatsBuf", _stats.ChannelStatsBuffer);
                Material.SetFloat("_Levels", _levels);
                Material.SetTexture("_BaseTex", source);
                Graphics.Blit(shapeRt, destination, Material, 3);
            }
            finally
            {
                GpuUtils.ReleaseTemp(scaledRt);
                GpuUtils.ReleaseTemp(lumRt);
                GpuUtils.ReleaseTemp(sharpRt);
                GpuUtils.ReleaseTemp(maskRt);
                GpuUtils.ReleaseTemp(shapeRt);
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
            _shapeAvg?.Dispose();
            _shapeAvg = null;
            base.Dispose();
        }
    }
}