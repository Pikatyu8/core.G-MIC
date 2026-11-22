using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class EdgesFilter : FilterBase
    {
        private GpuStats _stats;

        private float _smoothness = 0f;
        private float _threshold = 15f;
        private bool _negativeColors = false;

        public override string Id => "fx_edges";
        public override string DisplayName => "Edges";
        public override string Description => "Generic edge detection filter.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable =>
            base.IsAvailable &&
            GpuBlur.IsAvailable &&
            GpuGradientNorm.IsAvailable &&
            GpuJfa.IsAvailable &&
            _stats != null &&
            _stats.IsAvailable;

        public EdgesFilter()
        {
            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Pre-blur percentage relative to frame size before edge extraction.",
                0f, 10f, 0f,
                v => _smoothness = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "threshold",
                "Threshold",
                "Gradient norm threshold percentage.",
                0f, 50f, 15f,
                v => _threshold = v,
                step: 0.5f));

            AddParameter(FilterParameter.Bool(
                "negative_colors",
                "Negative Colors",
                "Invert edge output (white edges on black background).",
                false,
                v => _negativeColors = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Edges"))
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

            RenderTexture blurFull = null;
            RenderTexture gradRt = null;
            RenderTexture jfaA = null;
            RenderTexture jfaB = null;
            RenderTexture distRt = null;

            try
            {
                // 1. Optional pre-blur (b $1%)
                RenderTexture preGrad = source;
                float sigmaFull = _smoothness * 0.01f * Mathf.Max(w, h);
                if (sigmaFull > 0.05f)
                {
                    blurFull = GpuUtils.GetTempRt(w, h, colorFormat);
                    GpuBlur.Blur(source, blurFull, sigmaFull);
                    preGrad = blurFull;
                }

                // 2. gradient_norm + b 0.5
                gradRt = GpuUtils.GetTempRt(w, h, floatFormat);
                GpuGradientNorm.ComputeGradientNorm(preGrad, gradRt, scalarMode: false);
                GpuBlur.BlurInPlace(gradRt, 0.5f);

                // 3. Min/Max of blurred gradient_norm for >= threshold%
                _stats.ComputeMinMax(gradRt, rgbMode: false);

                // 4. Threshold & JFA seed initialization (distance 0)
                jfaA = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);
                jfaB = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);

                Material.SetFloat("_ThresholdPct", _threshold);
                Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                Graphics.Blit(gradRt, jfaA, Material, 0);

                RenderTexture jfaOut = GpuJfa.Run(jfaA, jfaB, maxStep: 128);

                // 5. Resolve Euclidean distance in pixels
                distRt = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);
                GpuJfa.ResolveDistance(jfaOut, distRt);

                // 6. Compute exact CDF on distance field (equalize 256)
                _stats.ComputeHistogramCdf(distRt);

                // 7. Equalize -> Negate -> Cut [30%, 70%] -> Normalize -> Optional Negative
                Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                Material.SetBuffer("_CdfBuffer", _stats.CdfBuffer);
                Material.SetTexture("_BaseTex", source);
                Material.SetFloat("_NegativeColors", _negativeColors ? 1f : 0f);
                Graphics.Blit(distRt, destination, Material, 1);
            }
            finally
            {
                GpuUtils.ReleaseTemp(blurFull);
                GpuUtils.ReleaseTemp(gradRt);
                GpuUtils.ReleaseTemp(jfaA);
                GpuUtils.ReleaseTemp(jfaB);
                GpuUtils.ReleaseTemp(distRt);
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