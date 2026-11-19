using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class GradientNormFilter : FilterBase
    {
        private GpuStats _stats;

        private float _smoothness = 0f;
        private float _linearity = 0.5f;
        private float _minThreshold = 0f;
        private float _maxThreshold = 100f;
        private bool _negativeColors = false;

        public override string Id => "fx_gradient_norm";
        public override string DisplayName => "Gradient Norm";
        public override string Description => "Computes the magnitude of the image gradient.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable =>
            base.IsAvailable && GpuBlur.IsAvailable && _stats != null && _stats.IsAvailable;

        public GradientNormFilter()
        {
            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Pre-blur radius in pixels before computing gradient magnitude.",
                0f, 10f, 0f,
                v => _smoothness = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "linearity",
                "Linearity",
                "Power exponent applied to the gradient magnitude.",
                0f, 1.5f, 0.5f,
                v => _linearity = v,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "min_threshold",
                "Min Threshold",
                "Lower cut percentage of the gradient range.",
                0f, 100f, 0f,
                v => _minThreshold = v,
                step: 0.5f));

            AddParameter(FilterParameter.Float(
                "max_threshold",
                "Max Threshold",
                "Upper cut percentage of the gradient range.",
                0f, 100f, 100f,
                v => _maxThreshold = v,
                step: 0.5f));

            AddParameter(FilterParameter.Bool(
                "negative_colors",
                "Negative Colors",
                "Invert the normalized gradient output.",
                false,
                v => _negativeColors = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/GradientNorm"))
            {
                return;
            }

            GpuStats.TryCreate(out _stats);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat floatFormat = GpuUtils.FloatFormat;

            RenderTexture blurRt = null;
            RenderTexture gradRt = null;

            try
            {
                // 1. Optional pre-blur (b $1 in pixels)
                RenderTexture preGrad = source;
                if (_smoothness > 0.05f)
                {
                    blurRt = GpuUtils.GetTempRt(w, h, colorFormat);
                    GpuBlur.Blur(source, blurRt, _smoothness);
                    preGrad = blurRt;
                }

                // 2. gradient_norm ^ Linearity
                gradRt = GpuUtils.GetTempRt(w, h, floatFormat);
                Material.SetFloat("_Linearity", _linearity);
                Graphics.Blit(preGrad, gradRt, Material, 0);

                // 3. Min/Max reduction for percentage cut (c $3%,$4%)
                _stats.ComputeMinMax(gradRt, rgbMode: false);

                // 4. Cut -> Normalize [0, 1] -> Optional Negative
                Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                Material.SetTexture("_BaseTex", source);
                Material.SetFloat("_MinThreshold", _minThreshold);
                Material.SetFloat("_MaxThreshold", _maxThreshold);
                Material.SetFloat("_NegativeColors", _negativeColors ? 1f : 0f);
                Graphics.Blit(gradRt, destination, Material, 1);
            }
            finally
            {
                GpuUtils.ReleaseTemp(blurRt);
                GpuUtils.ReleaseTemp(gradRt);
            }
        }

        public override void Dispose()
        {
            _stats?.Dispose();
            _stats = null;
            base.Dispose();
        }
    }
}