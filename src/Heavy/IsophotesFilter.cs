using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class IsophotesFilter : FilterBase
    {
        private GpuShapeAverage _shapeAvg;

        private int _levels = 8;
        private float _smoothness = 4f;
        private int _filling = 1; // 0 = Transparent, 1 = Colors

        public override string Id => "fx_isophotes";
        public override string DisplayName => "Isophotes";
        public override string Description => "Draws lines or topographic regions connecting points of equal brightness.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable =>
            base.IsAvailable && GpuBlur.IsAvailable && _shapeAvg != null && _shapeAvg.IsAvailable;

        public IsophotesFilter()
        {
            AddParameter(FilterParameter.Int(
                "levels",
                "Levels",
                "Number of brightness contour levels.",
                1, 256, 8,
                v => _levels = v));

            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Gaussian pre-blur radius in pixels before extracting contours.",
                0f, 10f, 4f,
                v => _smoothness = v,
                step: 0.1f));

            AddParameter(FilterParameter.Choice(
                "filling",
                "Filling",
                "Render contour lines only or fill topographic regions with average color.",
                new[] { "Transparent", "Colors" },
                1,
                v => _filling = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Isophotes"))
            {
                return;
            }

            GpuShapeAverage.TryCreate(out _shapeAvg);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat lumFormat = GpuUtils.LuminanceFormat;

            RenderTexture blurRt = null;
            RenderTexture labelRt = null;

            try
            {
                RenderTexture contourSrc = source;
                if (_smoothness > 0.05f)
                {
                    blurRt = GpuUtils.GetTempRt(w, h, colorFormat);
                    GpuBlur.Blur(source, blurRt, _smoothness);
                    contourSrc = blurRt;
                }

                Material.SetFloat("_Levels", _levels);

                if (_filling == 1)
                {
                    // Colors (topographic_map): direct band labeling avoids muddy contour-border seams.
                    labelRt = GpuUtils.GetTempRt(w, h, lumFormat, FilterMode.Point);
                    Graphics.Blit(contourSrc, labelRt, Material, 0);
                    _shapeAvg.Apply(labelRt, source, destination);
                }
                else
                {
                    // Transparent: border-free 1px colored isolines over transparent black.
                    Material.SetTexture("_BaseTex", source);
                    Graphics.Blit(contourSrc, destination, Material, 1);
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(blurRt);
                GpuUtils.ReleaseTemp(labelRt);
            }
        }

        public override void Dispose()
        {
            _shapeAvg?.Dispose();
            _shapeAvg = null;
            base.Dispose();
        }
    }
}