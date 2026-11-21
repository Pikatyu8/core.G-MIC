using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class StencilFilter : FilterBase
    {
        private static readonly string[] StencilTypeChoices =
        {
            "Black & White",
            "RGB",
            "Color"
        };

        private GpuShapeAverage _shapeAvg;

        private float _radius = 3f;
        private float _smoothness = 0f;
        private int _iterations = 8;
        private float _aliasing = 0f;
        private int _stencilType = 2;
        private bool _transparency = false;

        public override string Id => "fx_stencil";
        public override string DisplayName => "Stencil";
        public override string Description => "Creates a crisp two-tone or posterized stencil pattern.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable =>
            base.IsAvailable &&
            GpuBlur.IsAvailable &&
            GpuNormalize.IsAvailable &&
            GpuNorm.IsAvailable &&
            GpuAnisoSmooth.IsAvailable &&
            _shapeAvg != null &&
            _shapeAvg.IsAvailable;

        public StencilFilter()
        {
            AddParameter(FilterParameter.Float(
                "radius",
                "Radius",
                "Blur radius per stencil sharpening iteration.",
                0f, 10f, 3f,
                v => _radius = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Extra unsharp mask radius for broader stencil regions.",
                0f, 30f, 0f,
                v => _smoothness = v,
                step: 0.25f));

            AddParameter(FilterParameter.Int(
                "iterations",
                "Iterations",
                "Number of stencil contrast iterations.",
                1, 100, 8,
                v => _iterations = v));

            AddParameter(FilterParameter.Float(
                "aliasing",
                "Aliasing",
                "Anisotropic post-smoothing along stencil contours.",
                0f, 5f, 0f,
                v => _aliasing = v,
                step: 0.1f));

            AddParameter(FilterParameter.Choice(
                "stencil_type",
                "Stencil Type",
                "Black & White, independent RGB, or shape-averaged Color.",
                StencilTypeChoices,
                2,
                v => _stencilType = v));

            AddParameter(FilterParameter.Bool(
                "transparency",
                "Transparency",
                "Turn pure black areas transparent.",
                false,
                v => _transparency = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Stencil"))
            {
                return;
            }

            GpuShapeAverage.TryCreate(out _shapeAvg);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            RenderTextureFormat floatFmt = GpuUtils.FloatFormat;
            RenderTextureFormat colorFmt = GpuUtils.ColorFormat;

            RenderTexture normRt = null;
            RenderTexture workA = null;
            RenderTexture workB = null;
            RenderTexture blur1Rt = null;
            RenderTexture blur2Rt = null;
            RenderTexture maskRt = null;
            RenderTexture shapeRt = null;
            RenderTexture outTempRt = null;

            try
            {
                workA = GpuUtils.GetTempRt(w, h, floatFmt);
                workB = GpuUtils.GetTempRt(w, h, floatFmt);
                blur1Rt = GpuUtils.GetTempRt(w, h, floatFmt);
                blur2Rt = GpuUtils.GetTempRt(w, h, floatFmt);

                bool isRgb = (_stencilType == 1);
                if (!isRgb)
                {
                    normRt = GpuUtils.GetTempRt(w, h, floatFmt);
                    GpuNorm.ComputeNorm(source, normRt);
                    GpuNormalize.Normalize(normRt, workA, rgbMode: false);
                }
                else
                {
                    GpuNormalize.Normalize(source, workA, rgbMode: true);
                }

                RenderTexture curr = workA;
                Material.SetFloat("_Amount", 1000f);

                for (int iter = 0; iter < _iterations; iter++)
                {
                    if (_radius > 0.05f)
                    {
                        GpuBlur.Blur(curr, blur1Rt, _radius);
                    }
                    else
                    {
                        Graphics.Blit(curr, blur1Rt);
                    }

                    float unsharpRadius = _radius + _smoothness;
                    if (unsharpRadius > 0.05f)
                    {
                        GpuBlur.Blur(blur1Rt, blur2Rt, unsharpRadius);
                    }
                    else
                    {
                        Graphics.Blit(blur1Rt, blur2Rt);
                    }

                    RenderTexture next = (curr == workA) ? workB : workA;
                    Material.SetTexture("_BlurTex", blur2Rt);
                    Graphics.Blit(blur1Rt, next, Material, 0);
                    curr = next;
                }

                bool needSmooth = (_aliasing > 0.05f);
                Material.SetTexture("_BaseTex", source);
                Material.SetFloat("_Transparency", _transparency ? 1f : 0f);

                if (_stencilType == 2)
                {
                    maskRt = GpuUtils.GetTempRt(w, h, GpuUtils.LuminanceFormat, FilterMode.Point);
                    Graphics.Blit(curr, maskRt, Material, 2);

                    if (!_transparency && !needSmooth)
                    {
                        _shapeAvg.Apply(maskRt, source, destination);
                        return;
                    }

                    shapeRt = GpuUtils.GetTempRt(w, h, colorFmt);
                    _shapeAvg.Apply(maskRt, source, shapeRt);

                    if (needSmooth)
                    {
                        outTempRt = GpuUtils.GetTempRt(w, h, colorFmt);
                        Graphics.Blit(shapeRt, outTempRt, Material, 3);
                        GpuAnisoSmooth.Smooth(outTempRt, destination, 30f * _aliasing, sharpness: 0f, anisotropy: 1f, alpha: 1f);
                    }
                    else
                    {
                        Graphics.Blit(shapeRt, destination, Material, 3);
                    }
                }
                else
                {
                    if (needSmooth)
                    {
                        outTempRt = GpuUtils.GetTempRt(w, h, colorFmt);
                        Graphics.Blit(curr, outTempRt, Material, 1);
                        GpuAnisoSmooth.Smooth(outTempRt, destination, 30f * _aliasing, sharpness: 0f, anisotropy: 1f, alpha: 1f);
                    }
                    else
                    {
                        Graphics.Blit(curr, destination, Material, 1);
                    }
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(normRt);
                GpuUtils.ReleaseTemp(workA);
                GpuUtils.ReleaseTemp(workB);
                GpuUtils.ReleaseTemp(blur1Rt);
                GpuUtils.ReleaseTemp(blur2Rt);
                GpuUtils.ReleaseTemp(maskRt);
                GpuUtils.ReleaseTemp(shapeRt);
                GpuUtils.ReleaseTemp(outTempRt);
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