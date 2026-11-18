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

        private static readonly string[] ChannelChoices =
        {
            "All",
            "RGBA [all]",
            "RGB [all]",
            "RGB [red]",
            "RGB [green]",
            "RGB [blue]",
            "RGBA [alpha]",
            "Linear RGB [all]",
            "Linear RGB [red]",
            "Linear RGB [green]",
            "Linear RGB [blue]",
            "YCbCr [Y]",
            "YCbCr [CbCr]",
            "YCbCr [Cb]",
            "YCbCr [Cr]",
            "YCbCr [Cg]",
            "Lab [L]",
            "Lab [ab]",
            "Lab [a]",
            "Lab [b]",
            "Lch [ch]",
            "Lch [c]",
            "Lch [h]",
            "HSV [H]",
            "HSV [S]",
            "HSV [V]",
            "HSI [I]",
            "HSL [L]",
            "CMYK [C]",
            "CMYK [M]",
            "CMYK [Y]",
            "CMYK [K]"
        };

        private static readonly int[] JfaSteps = { 64, 32, 16, 8, 4, 2, 1 };

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
                ChannelChoices,
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
                ChannelChoices,
                7,
                v => _channelSpread = v));
        }

        public override void Initialize()
        {
            TryCreateMaterial("CoreGMIC/ChalkItUp");
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
            RenderTexture jfaA = null;
            RenderTexture jfaB = null;

            try
            {
                rtA = GpuUtils.GetTempRt(w, h, colorFormat);
                rtB = GpuUtils.GetTempRt(w, h, colorFormat);
                auxA = GpuUtils.GetTempRt(w, h, floatFormat);
                auxB = GpuUtils.GetTempRt(w, h, floatFormat);
                extraA = GpuUtils.GetTempRt(w, h, colorFormat);

                RenderTexture curr = source;
                RenderTexture NextColorRt(RenderTexture c) => (c == rtA) ? rtB : rtA;

                // 1. fx_normalize_local loop on YCbCr luminance (channel 11).
                if (_abstraction > 0)
                {
                    qA = GpuUtils.GetTempRt(qw, qh, colorFormat);
                    qB = GpuUtils.GetTempRt(qw, qh, colorFormat);

                    Graphics.Blit(curr, rtA, Material, 0); // Pass 0: RgbToYCbCr
                    curr = rtA;

                    for (int iter = 0; iter < _abstraction; iter++)
                    {
                        Graphics.Blit(curr, auxA, Material, 1); // Pass 1: LocalMinMaxH (radius 6 -> 13x13)
                        Graphics.Blit(auxA, auxB, Material, 2); // Pass 2: LocalMinMaxV
                        Graphics.Blit(auxB, auxA, Material, 3); // Pass 3: BlurSigma5H
                        Graphics.Blit(auxA, auxB, Material, 4); // Pass 4: BlurSigma5V -> (E_s, D_s)

                        // Quarter-res sigma=5 blur approximates sigma=20 at full resolution.
                        Graphics.Blit(curr, qA);
                        Graphics.Blit(qA, qB, Material, 3);
                        Graphics.Blit(qB, qA, Material, 4);

                        RenderTexture next = NextColorRt(curr);
                        Material.SetTexture("_AuxTex", auxB);
                        Material.SetTexture("_AvgTex", qA);
                        Graphics.Blit(curr, next, Material, 5); // Pass 5: NormalizeLocalCombine
                        curr = next;
                    }

                    RenderTexture rgbOut = NextColorRt(curr);
                    Graphics.Blit(curr, rgbOut, Material, 6); // Pass 6: YCbCrToRgb
                    curr = rgbOut;
                }

                // 2. fx_smooth_anisotropic
                if (_detailsScale > 0.01f)
                {
                    Material.SetFloat("_Sigma", _detailsScale);
                    Material.SetFloat("_Sharpness", 100f * _detailsScale);
                    Material.SetFloat("_Anisotropy", 0.2f);
                    Material.SetFloat("_Dl", 2f * _detailsScale);

                    Graphics.Blit(curr, auxA, Material, 7); // Pass 7: StructureTensor
                    Graphics.Blit(auxA, auxB, Material, 8); // Pass 8: BlurTensorH
                    Graphics.Blit(auxB, auxA, Material, 9); // Pass 9: BlurTensorV

                    RenderTexture next = NextColorRt(curr);
                    Material.SetTexture("_TensorTex", auxA);
                    Graphics.Blit(curr, next, Material, 10); // Pass 10: AnisotropicLIC
                    curr = next;
                }

                // 3. fx_mix_lab (scale a,b by _Color and blur chroma by 0.5%)
                Material.SetFloat("_ColorMul", _color);
                Graphics.Blit(curr, auxA, Material, 11); // Pass 11: RgbToLab01
                Graphics.Blit(auxA, auxB, Material, 12); // Pass 12: ScaleAndBlurLabChromaH
                Graphics.Blit(auxB, auxA, Material, 13); // Pass 13: BlurLabChromaV
                RenderTexture labOut = NextColorRt(curr);
                Graphics.Blit(auxA, labOut, Material, 14); // Pass 14: Lab01ToRgb
                curr = labOut;

                // 4. fx_segment_watershed (GPU JFA approximation)
                if (_sharpenShades)
                {
                    jfaA = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);
                    jfaB = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);

                    Graphics.Blit(curr, auxA, Material, 15); // Pass 15: BlurColor1H
                    Graphics.Blit(auxA, auxB, Material, 16); // Pass 16: BlurColor1V (blurred image)
                    Graphics.Blit(auxB, extraA, Material, 17); // Pass 17: GradientNorm

                    Graphics.Blit(extraA, jfaA, Material, 18); // Pass 18: WatershedSeedInit
                    RenderTexture jfaSrc = jfaA;
                    RenderTexture jfaDst = jfaB;

                    Material.SetTexture("_GradTex", extraA);
                    for (int s = 0; s < JfaSteps.Length; s++)
                    {
                        Material.SetFloat("_JfaStep", JfaSteps[s]);
                        Graphics.Blit(jfaSrc, jfaDst, Material, 19); // Pass 19: WatershedJfaStep
                        jfaSrc = jfaDst;
                        jfaDst = (jfaSrc == jfaA) ? jfaB : jfaA;
                    }

                    RenderTexture next = NextColorRt(curr);
                    Material.SetTexture("_JfaTex", jfaSrc);
                    Graphics.Blit(auxB, next, Material, 20); // Pass 20: WatershedResolve
                    curr = next;
                }

                // 5. Final smooth in fx_painting
                if (_smoothness > 0.5f)
                {
                    Material.SetFloat("_Sigma", 1f);
                    Material.SetFloat("_Sharpness", _smoothness);
                    Material.SetFloat("_Anisotropy", 0f);
                    Material.SetFloat("_Dl", 1f);

                    Graphics.Blit(curr, auxA, Material, 7);
                    Graphics.Blit(auxA, auxB, Material, 8);
                    Graphics.Blit(auxB, auxA, Material, 9);

                    RenderTexture next = NextColorRt(curr);
                    Material.SetTexture("_TensorTex", auxA);
                    Graphics.Blit(curr, next, Material, 10);
                    curr = next;
                }

                // 6. ancien_gimp_morpho
                Graphics.Blit(curr, extraA); // Preserve pre-morpho sRGB
                Material.SetInt("_ChannelMode", _channelMorpho);
                Graphics.Blit(extraA, auxA, Material, 21); // Pass 21: EncodeChannelSpace -> auxA = encodedOrig

                RenderTexture morphoResult;
                bool needsSecondStage = (_action == 2 || _action == 3 || _action == 6 || _action == 7);
                bool firstIsDilate = (_action == 1 || _action == 3 || _action == 5 || _action == 7);

                if (needsSecondStage)
                {
                    RenderTexture stage1 = RunMorpho(auxA, rtA, rtB, auxB, firstIsDilate);
                    RenderTexture freeA = (stage1 == rtA) ? rtB : rtA;
                    RenderTexture freeB = auxB;
                    morphoResult = RunMorpho(stage1, freeA, (freeA == rtA) ? rtB : rtA, freeB, !firstIsDilate);
                }
                else
                {
                    morphoResult = RunMorpho(auxA, rtA, rtB, auxB, firstIsDilate);
                }

                RenderTexture combineOut = (morphoResult == rtA) ? rtB : rtA;
                Material.SetTexture("_BaseTex", auxA);
                Material.SetInt("_MorphoAction", _action);
                Material.SetFloat("_InvertColors", _invertColors ? 1f : 0f);
                Graphics.Blit(morphoResult, combineOut, Material, 25); // Pass 25: MorphoCombineAndInvert

                RenderTexture mergedMorpho = (combineOut == rtA) ? rtB : rtA;
                Material.SetTexture("_BaseTex", extraA);
                Material.SetInt("_ChannelMode", _channelMorpho);
                Graphics.Blit(combineOut, mergedMorpho, Material, 26); // Pass 26: DecodeAndMergeChannel
                curr = mergedMorpho;

                // 7. fx_spread
                if (Mathf.Abs(_xVariations) > 0.001f || Mathf.Abs(_yVariations) > 0.001f)
                {
                    Graphics.Blit(curr, extraA);
                    Material.SetInt("_ChannelMode", _channelSpread);
                    Graphics.Blit(extraA, auxA, Material, 21); // Pass 21: EncodeChannelSpace

                    Material.SetFloat("_SpreadX", _xVariations);
                    Material.SetFloat("_SpreadY", _yVariations);
                    Graphics.Blit(auxA, auxB, Material, 27); // Pass 27: SpreadPass

                    Material.SetTexture("_BaseTex", extraA);
                    Material.SetInt("_ChannelMode", _channelSpread);
                    Graphics.Blit(auxB, destination, Material, 26); // Pass 26: DecodeAndMergeChannel
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
                GpuUtils.ReleaseTemp(jfaA);
                GpuUtils.ReleaseTemp(jfaB);
            }
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
                Graphics.Blit(input, scratch, Material, 22); // Pass 22: MorphoSquareH
                Graphics.Blit(scratch, pingA, Material, 23); // Pass 23: MorphoSquareV
                return pingA;
            }

            // G'MIC erode_oct / dilate_oct decomposition into r diamond + q square steps.
            int ss = _size + 1 - (_size & 1);
            float sqrt2 = 1.41421356f;
            int r = Mathf.RoundToInt((ss - 1) * sqrt2 / (1f + sqrt2) * 0.5f);
            int q = Mathf.RoundToInt((ss - 1) / (1f + sqrt2) * 0.5f);

            RenderTexture cur = input;
            RenderTexture next = pingA;

            for (int step = 0; step < r; step++)
            {
                Graphics.Blit(cur, next, Material, 24); // Pass 24: MorphoDiamond3x3
                cur = next;
                next = (cur == pingA) ? pingB : pingA;
            }

            if (q > 0)
            {
                Material.SetInt("_MorphoSize", 2 * q + 1);
                Graphics.Blit(cur, scratch, Material, 22);
                Graphics.Blit(scratch, next, Material, 23);
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