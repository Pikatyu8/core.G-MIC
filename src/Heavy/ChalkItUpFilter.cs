using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class ChalkItUpFilter : FilterBase
    {
        private static readonly string[] MorphoActionChoices =
        {
            "Erosion",
            "Dilation",
            "Opening",
            "Closing",
            "Orig - Erosion",
            "Dilation - Orig",
            "Orig - Opening",
            "Closing - Orig"
        };

        private static readonly string[] LegacyChannelChoices;

        static ChalkItUpFilter()
        {
            LegacyChannelChoices = new string[32];
            System.Array.Copy(GpuChannelOps.ChannelChoices, LegacyChannelChoices, 32);
        }

        private GpuWatershed _watershed;

        private int _abstraction = 5;
        private float _detailsScale = 2.5f;
        private float _color = 1.5f;
        private float _smoothness = 50f;
        private bool _sharpenShades = true;

        private int _action = 5;
        private int _size = 5;
        private bool _invertColors = false;
        private bool _octagonalShape = false;
        private int _channelMorpho = 7;

        private float _xVariations = 0.8f;
        private float _yVariations = 1.9f;
        private int _channelSpread = 7;

        public override string Id => "samj_chalkitup";
        public override string DisplayName => "Chalk It Up";
        public override string Description => "Chalk-and-pastel stylization with local normalization, JFA watershed, morphology, and spread.";
        public override FilterCategory Category => FilterCategory.Heavy;
        public override bool IsAvailable =>
            base.IsAvailable &&
            GpuChannelOps.IsAvailable &&
            GpuAnisoSmooth.IsAvailable &&
            _watershed != null &&
            _watershed.IsAvailable;

        public ChalkItUpFilter()
        {
            AddParameter(FilterParameter.Int(
                "abstraction",
                "Abstraction",
                "Iterations of local luminance normalization.",
                0, 10, 5,
                v => _abstraction = v));

            AddParameter(FilterParameter.Float(
                "details_scale",
                "Details Scale",
                "Anisotropic painting stroke scale and edge sharpness.",
                0f, 10f, 2.5f,
                v => _detailsScale = v));

            AddParameter(FilterParameter.Float(
                "color",
                "Color",
                "CIE Lab chromaticity multiplier.",
                0f, 5f, 1.5f,
                v => _color = v));

            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Post-segmentation edge-preserving smoothness.",
                0f, 200f, 50f,
                v => _smoothness = v));

            AddParameter(FilterParameter.Bool(
                "sharpen_shades",
                "Sharpen Shades",
                "Apply GPU watershed color segmentation.",
                true,
                v => _sharpenShades = v));

            AddParameter(FilterParameter.Choice(
                "action",
                "Morpho Action",
                "Morphological outline/filter operation.",
                MorphoActionChoices,
                5,
                v => _action = v));

            AddParameter(FilterParameter.Int(
                "size",
                "Morpho Size",
                "Kernel size for morphological erosion/dilation.",
                0, 32, 5,
                v => _size = v));

            AddParameter(FilterParameter.Bool(
                "invert_colors",
                "Invert Colors",
                "Invert output after morphological operation.",
                false,
                v => _invertColors = v));

            AddParameter(FilterParameter.Bool(
                "shape",
                "Octagonal Shape",
                "Use octagonal structuring element instead of square.",
                false,
                v => _octagonalShape = v));

            AddParameter(FilterParameter.Choice(
                "channel_morpho",
                "Morpho Channel",
                "Color space/channels used for morphology.",
                LegacyChannelChoices,
                7,
                v => _channelMorpho = v));

            AddParameter(FilterParameter.Float(
                "x_variations",
                "Spread X",
                "Horizontal chalk scatter radius in pixels.",
                -5f, 5f, 0.8f,
                v => _xVariations = v));

            AddParameter(FilterParameter.Float(
                "y_variations",
                "Spread Y",
                "Vertical chalk scatter radius in pixels.",
                -5f, 5f, 1.9f,
                v => _yVariations = v));

            AddParameter(FilterParameter.Choice(
                "channel_spread",
                "Spread Channel",
                "Color space/channels used for spread.",
                LegacyChannelChoices,
                7,
                v => _channelSpread = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/ChalkItUp"))
            {
                return;
            }

            GpuWatershed.TryCreate(out _watershed);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            int qw = Mathf.Max(1, w / 4);
            int qh = Mathf.Max(1, h / 4);

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat floatFormat = GpuUtils.FloatFormat;

            RenderTexture rtA = null;
            RenderTexture rtB = null;
            RenderTexture auxA = null;
            RenderTexture auxB = null;
            RenderTexture extraA = null;
            RenderTexture qA = null;
            RenderTexture qB = null;

            try
            {
                rtA = GpuUtils.GetTempRt(w, h, colorFormat);
                rtB = GpuUtils.GetTempRt(w, h, colorFormat);
                auxA = GpuUtils.GetTempRt(w, h, floatFormat);
                auxB = GpuUtils.GetTempRt(w, h, floatFormat);
                extraA = GpuUtils.GetTempRt(w, h, colorFormat);

                RenderTexture curr = source;
                RenderTexture NextColorRt(RenderTexture c) => (c == rtA) ? rtB : rtA;

                // 1. fx_normalize_local loop on YCbCr luminance.
                if (_abstraction > 0)
                {
                    qA = GpuUtils.GetTempRt(qw, qh, colorFormat);
                    qB = GpuUtils.GetTempRt(qw, qh, colorFormat);

                    Graphics.Blit(curr, rtA, Material, 0);
                    curr = rtA;

                    for (int iter = 0; iter < _abstraction; iter++)
                    {
                        Graphics.Blit(curr, auxA, Material, 1);
                        Graphics.Blit(auxA, auxB, Material, 2);
                        Graphics.Blit(auxB, auxA, Material, 3);
                        Graphics.Blit(auxA, auxB, Material, 4);

                        Graphics.Blit(curr, qA);
                        Graphics.Blit(qA, qB, Material, 3);
                        Graphics.Blit(qB, qA, Material, 4);

                        RenderTexture next = NextColorRt(curr);
                        Material.SetTexture("_AuxTex", auxB);
                        Material.SetTexture("_AvgTex", qA);
                        Graphics.Blit(curr, next, Material, 5);
                        curr = next;
                    }

                    RenderTexture rgbOut = NextColorRt(curr);
                    Graphics.Blit(curr, rgbOut, Material, 6);
                    curr = rgbOut;
                }

                // 2. fx_smooth_anisotropic via shared GpuAnisoSmooth
                if (_detailsScale > 0.01f)
                {
                    RenderTexture next = NextColorRt(curr);
                    GpuAnisoSmooth.SmoothPasses(
                        curr,
                        next,
                        passes: 1,
                        sharpness: 100f * _detailsScale,
                        anisotropy: 0.2f,
                        alpha: 0f,
                        sigma: _detailsScale,
                        dl: 2f * _detailsScale);
                    curr = next;
                }

                // 3. fx_mix_lab
                Material.SetFloat("_ColorMul", _color);
                Graphics.Blit(curr, auxA, Material, 7);
                Graphics.Blit(auxA, auxB, Material, 8);
                Graphics.Blit(auxB, auxA, Material, 9);
                RenderTexture labOut = NextColorRt(curr);
                Graphics.Blit(auxA, labOut, Material, 10);
                curr = labOut;

                // 4. Shared GPU watershed segmentation
                if (_sharpenShades)
                {
                    RenderTexture next = NextColorRt(curr);
                    _watershed.Apply(curr, next, edgeThreshold: 10f, smoothness: 1.2f);
                    curr = next;
                }

                // 5. Final smooth via shared GpuAnisoSmooth
                if (_smoothness > 0.5f)
                {
                    RenderTexture next = NextColorRt(curr);
                    GpuAnisoSmooth.SmoothPasses(
                        curr,
                        next,
                        passes: 1,
                        sharpness: _smoothness,
                        anisotropy: 0f,
                        alpha: 0f,
                        sigma: 1f,
                        dl: 1f);
                    curr = next;
                }

                // 6. ancien_gimp_morpho via shared GpuChannelOps
                Graphics.Blit(curr, extraA);
                GpuChannelOps.Encode(extraA, auxA, _channelMorpho);

                RenderTexture morphoResult;
                bool needsSecondStage = (_action == 2 || _action == 3 || _action == 6 || _action == 7);
                bool firstIsDilate = (_action == 1 || _action == 3 || _action == 5 || _action == 7);

                if (needsSecondStage)
                {
                    RenderTexture stage1 = RunMorpho(auxA, rtA, rtB, auxB, firstIsDilate);
                    RenderTexture freeA = (stage1 == rtA) ? rtB : rtA;
                    morphoResult = RunMorpho(stage1, freeA, (freeA == rtA) ? rtB : rtA, auxB, !firstIsDilate);
                }
                else
                {
                    morphoResult = RunMorpho(auxA, rtA, rtB, auxB, firstIsDilate);
                }

                RenderTexture combineOut = (morphoResult == rtA) ? rtB : rtA;
                Material.SetTexture("_BaseTex", auxA);
                Material.SetInt("_MorphoAction", _action);
                Material.SetFloat("_InvertColors", _invertColors ? 1f : 0f);
                Graphics.Blit(morphoResult, combineOut, Material, 14);

                RenderTexture mergedMorpho = (combineOut == rtA) ? rtB : rtA;
                GpuChannelOps.DecodeAndMerge(extraA, combineOut, mergedMorpho, _channelMorpho);
                curr = mergedMorpho;

                // 7. fx_spread via shared GpuChannelOps
                if (Mathf.Abs(_xVariations) > 0.001f || Mathf.Abs(_yVariations) > 0.001f)
                {
                    Graphics.Blit(curr, extraA);
                    GpuChannelOps.Encode(extraA, auxA, _channelSpread);

                    Material.SetFloat("_SpreadX", _xVariations);
                    Material.SetFloat("_SpreadY", _yVariations);
                    Graphics.Blit(auxA, auxB, Material, 15);

                    GpuChannelOps.DecodeAndMerge(extraA, auxB, destination, _channelSpread);
                }
                else
                {
                    Graphics.Blit(curr, destination);
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(rtA);
                GpuUtils.ReleaseTemp(rtB);
                GpuUtils.ReleaseTemp(auxA);
                GpuUtils.ReleaseTemp(auxB);
                GpuUtils.ReleaseTemp(extraA);
                GpuUtils.ReleaseTemp(qA);
                GpuUtils.ReleaseTemp(qB);
            }
        }

        public override void Dispose()
        {
            _watershed?.Dispose();
            _watershed = null;
            base.Dispose();
        }

        private RenderTexture RunMorpho(
            RenderTexture input,
            RenderTexture pingA,
            RenderTexture pingB,
            RenderTexture scratch,
            bool isDilate)
        {
            if (_size < 2)
            {
                Graphics.Blit(input, pingA);
                return pingA;
            }

            Material.SetFloat("_MorphoIsDilate", isDilate ? 1f : 0f);

            if (!_octagonalShape)
            {
                Material.SetInt("_MorphoSize", _size);
                Graphics.Blit(input, scratch, Material, 11);
                Graphics.Blit(scratch, pingA, Material, 12);
                return pingA;
            }

            int ss = _size + 1 - (_size & 1);
            float sqrt2 = 1.41421356f;
            int r = Mathf.RoundToInt((ss - 1) * sqrt2 / (1f + sqrt2) * 0.5f);
            int q = Mathf.RoundToInt((ss - 1) / (1f + sqrt2) * 0.5f);

            RenderTexture cur = input;
            RenderTexture next = pingA;

            for (int step = 0; step < r; step++)
            {
                Graphics.Blit(cur, next, Material, 13);
                cur = next;
                next = (cur == pingA) ? pingB : pingA;
            }

            if (q > 0)
            {
                Material.SetInt("_MorphoSize", 2 * q + 1);
                Graphics.Blit(cur, scratch, Material, 11);
                Graphics.Blit(scratch, next, Material, 12);
                cur = next;
            }
            else if (cur == input)
            {
                Graphics.Blit(input, pingA);
                cur = pingA;
            }

            return cur;
        }
    }
}